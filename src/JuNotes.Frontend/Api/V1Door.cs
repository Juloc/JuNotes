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
using JuNotes.Integrations.Codex;
using JuNotes.Integrations.Paperless;

namespace JuNotes.Frontend.Api
{
    // /api/v1 is the contract with the iPad app and its own door: it is branched off before the middleware of the base, so the guard of the base (which wants a
    // session and the header X-Base for every change under /api) never sees these requests, and only the instance token opens them. The token is checked first,
    // before routing and body binding, so a caller without it learns nothing else. A session cookie is never read here, and the token is never read anywhere else.
    public static class V1Door
    {
        public const string Prefix = "/api/v1";

        // The answers are written as they always were: the options of the framework (camel case, its own escaping) with the nulls kept. The base leaves the nulls
        // out of its own answers, and the app, which has never heard of that, would no longer find them.
        private static JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        private static IResult Json<T>(T value) => Results.Json(value, _options);

        public static void UseV1Door(this WebApplication app)
        {
            _options = new JsonSerializerOptions(app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
            app.MapWhen(context => context.Request.Path.StartsWithSegments(Prefix), door =>
            {
                door.Use(async (context, next) =>
                {
                    if (!context.Request.Path.StartsWithSegments(Prefix + "/status"))
                    {
                        var access = context.RequestServices.GetRequiredService<SyncAccess>();
                        if (!access.IsConfigured)
                        {
                            await Results.Problem("The API is not set up: set Sync:Token (environment variable Sync__Token).", statusCode: 503).ExecuteAsync(context);
                            return;
                        }

                        if (!access.IsAuthorized(context.Request.Headers.Authorization))
                        {
                            await Results.Unauthorized().ExecuteAsync(context);
                            return;
                        }
                    }

                    await next();
                });
                door.UseRouting();
                door.UseEndpoints(Map);
            });
        }

        private static void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/v1/status", () => Json(new { name = "JuNotes", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0", api = 1 }));

            var secured = endpoints.MapGroup("/api/v1");
            var sync = secured.MapGroup("/sync");
            sync.MapPost("/push", async (PushRequest request, SyncService service, CancellationToken ct) =>
            {
                try
                {
                    return Json(await service.PushAsync(request, ct));
                }
                catch (SyncValidationException error)
                {
                    return Results.Problem(error.Message, statusCode: 400);
                }
            });
            sync.MapGet("/info", (BackupService backup, TimeProvider time) => Json(new { serverTime = time.GetUtcNow(), lastBackupAt = backup.Latest()?.At }));
            sync.MapGet("/pull", async (long? since, int? limit, SyncService service, CancellationToken ct) => Json(await service.PullAsync(since ?? 0, limit ?? SyncService.MaxChanges, ct)));

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
            assetApi.MapPut("/{id:guid}", async (Guid id, HttpRequest request, AssetService assets, CancellationToken ct) =>
            {
                try
                {
                    await assets.SaveAsync(id, request.Body, ct);
                    return Results.NoContent();
                }
                catch (AssetException error)
                {
                    return Results.Problem(error.Message, statusCode: error.StatusCode);
                }
            });
            assetApi.MapMethods("/{id:guid}", ["HEAD"], (Guid id, AssetService assets) => assets.Exists(id) ? Results.Ok() : Results.NotFound());
            assetApi.MapGet("/{id:guid}", (Guid id, AssetService assets) => assets.Open(id) is { } file ? Results.Stream(file.Stream, file.ContentType, enableRangeProcessing: true) : Results.NotFound());

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
