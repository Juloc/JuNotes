using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using JuNotes.Backend.Ai;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Backup;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.School;
using JuNotes.Backend.Service;
using JuNotes.Frontend.Admin;
using JuNotes.Integrations.Codex;
using JuNotes.Integrations.Paperless;

namespace JuNotes.Frontend.Api
{
    // /api/v1 is the contract with the iPad app and an own door of the base (SEC-07, declared in BaseAppOptions.Doors): the guard of the base asks neither for its header
    // nor for an origin, never reads a session cookie there and sets no identity, and lets a request through only when the check below says yes. The check is the
    // instance token, and the token opens nothing else; a session opens nothing here. Only /status is open.
    public static class V1Door
    {
        public const string Prefix = "/api/v1";

        // The largest body of a request at this door: what Kestrel allowed before (a scan or a PDF is at most 20 MB, a batch of changes less).
        public const long MaxBodyBytes = 30_000_000;

        public static ValueTask<bool> CheckAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments(Prefix + "/status", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(true);
            }

            var access = context.RequestServices.GetRequiredService<SyncAccess>();
            return ValueTask.FromResult(access.IsConfigured && access.IsAuthorized(context.Request.Headers.Authorization));
        }

        // The answers are written as they always were: the options of the framework (camel case, its own escaping) with the nulls kept. The base leaves the nulls
        // out of its own answers, and the app, which has never heard of that, would no longer find them.
        private static JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        private static IResult Json<T>(T value) => Results.Json(value, _options);

        public static void MapV1(this WebApplication app)
        {
            _options = new JsonSerializerOptions(app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
            Map(app);
        }

        private static void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/v1/status", () => Json(new { name = "JuNotes", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0", api = 1 }));

            var secured = endpoints.MapGroup("/api/v1");
            var sync = secured.MapGroup("/sync");
            sync.MapPost("/push", async (PushRequest request, SyncService service, OwnerDirectory owners, ISecurityLog log, CancellationToken ct) =>
            {
                try
                {
                    return Json(await service.PushAsync(request, await owners.TokenOwnerAsync(ct), log, ct));
                }
                catch (SyncValidationException error)
                {
                    return Results.Problem(error.Message, statusCode: 400);
                }
            });
            sync.MapGet("/info", (BackupService backup, TimeProvider time) => Json(new { serverTime = time.GetUtcNow(), lastBackupAt = backup.Latest()?.At }));
            sync.MapGet("/pull", async (long? since, int? limit, SyncService service, OwnerDirectory owners, CancellationToken ct) => Json(await service.PullAsync(since ?? 0, limit ?? SyncService.MaxChanges, (await owners.TokenOwnerAsync(ct))?.Id, ct)));

            var schoolApi = secured.MapGroup("/school");
            schoolApi.MapGet("/status", (SchoolImportService import) => Json(import.Status));
            schoolApi.MapGet("/calendar", async (DateTimeOffset? from, DateTimeOffset? to, SchoolStore store, TimeProvider time, CancellationToken ct) =>
            {
                var start = from ?? time.GetUtcNow().AddDays(-1);
                var end = to ?? start.AddDays(14);
                return end <= start || end - start > TimeSpan.FromDays(366)
                    ? Results.Problem("The range must be positive and at most a year.", statusCode: 400)
                    : Json(await store.EventsAsync(start, end, ct));
            });
            schoolApi.MapGet("/news", async (int? limit, SchoolStore store, CancellationToken ct) => Json(await store.NewsAsync(limit ?? 20, ct)));

            var assetApi = secured.MapGroup("/assets");
            assetApi.MapPut("/{id:guid}", async (Guid id, HttpRequest request, AssetService assets, OwnerDirectory owners, CancellationToken ct) =>
            {
                try
                {
                    // A file of somebody else is left alone and the answer is the same as for a stored one.
                    await assets.SaveAsync(id, request.Body, (await owners.TokenOwnerAsync(ct))?.Id, ct);
                    return Results.NoContent();
                }
                catch (AssetException error)
                {
                    return Results.Problem(error.Message, statusCode: error.StatusCode);
                }
            });
            assetApi.MapMethods("/{id:guid}", ["HEAD"], async (Guid id, AssetService assets, OwnerDirectory owners, CancellationToken ct) => await assets.ExistsAsync(id, (await owners.TokenOwnerAsync(ct))?.Id, ct) ? Results.Ok() : Results.NotFound());
            assetApi.MapGet("/{id:guid}", async (Guid id, AssetService assets, OwnerDirectory owners, CancellationToken ct) => await assets.OpenAsync(id, (await owners.TokenOwnerAsync(ct))?.Id, ct) is { } file ? Results.Stream(file.Stream, file.ContentType, enableRangeProcessing: true) : Results.NotFound());

            var aiApi = secured.MapGroup("/ai");
            aiApi.MapGet("/profiles", async (AiProfileStore store, CancellationToken ct) => Json((await store.ListAsync(ct)).Select(profile => new { profile.Key, profile.Name, profile.Enabled })));
            aiApi.MapPost("/{task}", async (string task, AiRequest request, LearningService learning, CancellationToken ct) =>
            {
                try
                {
                    return Json(await learning.RunAsync(task, request, ct));
                }
                catch (AiRequestException error)
                {
                    return Results.Problem(error.Message, statusCode: 400);
                }
                catch (AiRefusedException error)
                {
                    return Results.Problem(error.Message, statusCode: error.StatusCode);
                }
                catch (CodexUnavailableException)
                {
                    return Results.Problem("Die KI ist auf dem Server noch nicht eingerichtet.", statusCode: 503);
                }
                catch (CodexFailedException error)
                {
                    return Results.Problem(error.Message, statusCode: 502);
                }
            });

            var exportApi = secured.MapGroup("/export");
            exportApi.MapGet("/status", (PaperlessOptions paperless) => Json(new { paperless = paperless.IsConfigured }));
            // The finished PDF of a notebook goes to Paperless as an archive copy: the body is the PDF, the title and tags come in the query.
            exportApi.MapPost("/paperless", async (HttpRequest request, string title, string? tags, PaperlessOptions options, PaperlessClient paperless, CancellationToken ct) =>
            {
                if (!options.IsConfigured)
                {
                    return Results.Problem("Paperless ist auf dem Server nicht eingerichtet.", statusCode: 503);
                }

                if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
                {
                    return Results.Problem("Der Titel fehlt oder ist zu lang.", statusCode: 400);
                }

                using var buffer = new MemoryStream();
                var chunk = new byte[64 * 1024];
                int read;
                while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > AssetService.MaxBytes)
                    {
                        return Results.Problem("Die Datei ist größer als 20 MB.", statusCode: 413);
                    }
                }

                var pdf = buffer.ToArray();
                if (AssetService.SniffContentType(pdf.AsSpan(0, Math.Min(pdf.Length, 16))) != "application/pdf")
                {
                    return Results.Problem("Das ist keine PDF-Datei.", statusCode: 415);
                }

                try
                {
                    var names = (tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(10).ToList();
                    return Results.Accepted(value: new { task = await paperless.UploadAsync(pdf, title.Trim(), DateTimeOffset.UtcNow, names, ct) });
                }
                catch (PaperlessException error)
                {
                    return Results.Problem(error.Message, statusCode: 502);
                }
            });
        }
    }
}
