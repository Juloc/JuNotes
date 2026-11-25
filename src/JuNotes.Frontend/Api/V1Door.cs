using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Base.Accounts;
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

        private const string OwnerKey = "notes.token-owner";

        // The check of the door. Only /status (the exact path) is open, and POST /api/v1/pair (below). Everything else needs a token, and a right token always gets through;
        // wrong ones are counted per address (TokenBrake). A device token (nt1_...) is looked for first and stands for its person; otherwise the instance token is checked,
        // which stands for the person written down once (OwnerDirectory). If the token has no person any more the door says 401.
        public static async ValueTask<bool> CheckAsync(HttpContext context)
        {
            if (context.Request.Path.Equals(Prefix + "/status", StringComparison.OrdinalIgnoreCase) || IsPairRequest(context.Request))
            {
                return true;
            }

            var services = context.RequestServices;
            var address = Base.Web.Auth.RequestIdentity.ClientIp(context);
            var header = context.Request.Headers.Authorization.ToString();
            if (header.StartsWith(DeviceBearer, StringComparison.Ordinal))
            {
                // A device token. One that is unknown, removed or of a blocked person falls through to the old check, which it fails: counted like any wrong token.
                var device = await services.GetRequiredService<DeviceStore>().FindAsync(header[BearerPrefix.Length..], address, context.RequestAborted);
                if (device is not null)
                {
                    context.Items[OwnerKey] = await services.GetRequiredService<OwnerDirectory>().ForDeviceAsync(device, context.RequestAborted);
                    return true;
                }
            }

            // The instance token is looked at next: a right one always gets through. Only wrong ones are counted (and, past ten a minute from one address, written to the log once an hour).
            var access = services.GetRequiredService<SyncAccess>();
            if (!access.IsConfigured || !access.IsAuthorized(header))
            {
                services.GetRequiredService<TokenBrake>().Failed(address);
                return false;
            }

            // The right shared token, but the administrator has switched it off: 401, and not counted as a wrong token (it is not one). Devices with their own token are not affected.
            var sharedToken = services.GetRequiredService<SharedToken>();
            if (await sharedToken.IsOffAsync(context.RequestAborted))
            {
                sharedToken.Refused(address);
                return false;
            }

            await sharedToken.SeenAsync(address, context.RequestAborted);
            var owner = await services.GetRequiredService<OwnerDirectory>().TokenOwnerAsync(context.RequestAborted);
            if (owner.State == TokenState.Refused)
            {
                return false;
            }

            context.Items[OwnerKey] = owner;
            return true;
        }

        private const string BearerPrefix = "Bearer ";
        private const string DeviceBearer = BearerPrefix + DeviceStore.TokenPrefix;

        // The one exception of the door besides /status: exactly this path (no prefix, no trailing slash, no other spelling) and only POST. The endpoint refuses what is left to refuse.
        private static bool IsPairRequest(HttpRequest request) => HttpMethods.IsPost(request.Method) && string.Equals(request.Path.Value, PairPath, StringComparison.Ordinal);

        public const string PairPath = Prefix + "/pair";

        private static TokenOwner OwnerOf(HttpContext context) => (TokenOwner)context.Items[OwnerKey]!;

        // The person for a sync or file request: null while no account exists (as it was); the door has already refused a token without a person.
        private static Guid? PersonId(HttpContext context) => OwnerOf(context).Person?.Id;

        // The answers are written as they always were: the options of the framework (camel case, its own escaping) with the nulls kept. The base leaves the nulls
        // out of its own answers, and the app, which has never heard of that, would no longer find them.
        private static JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        private static IResult Json<T>(T value) => Results.Json(value, _options);

        public static void MapV1(this WebApplication app)
        {
            _options = new JsonSerializerOptions(app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
            Map(app);
        }

        private sealed record PairRequest(string? Code, string? DeviceName);

        private const int PairBodyLimit = 2048;

        // Pairing (ARCHITECTURE 10.1): the person has a one-time code from their settings and gives it with the name of the device; the answer is the device token, once.
        // This is the only path of the door that answers without a token, so every refusal looks the same (401 with one body): a code that never was, one that ran out, one
        // already used, a request over http, a body that is not ours. After three wrong codes from an address even a right one is refused for the rest of the minute (429).
        private static async Task<IResult> PairAsync(HttpContext context, DeviceStore devices, TokenBrake brake, AuditStore audit, CancellationToken ct)
        {
            var address = Base.Web.Auth.RequestIdentity.ClientIp(context);
            if (brake.PairWait(address) is > 0 and var wait)
            {
                context.Response.Headers.RetryAfter = wait.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Problem("Too many wrong codes. Try again in a minute.", statusCode: 429);
            }

            // Not over https: refused before the code is looked at, so a code that was sent in the clear stays unspent (and nobody has learned anything about it).
            if (!ArrivedSecurely(context))
            {
                return PairRefused();
            }

            var request = await ReadPairRequestAsync(context.Request, ct);
            var paired = request is null ? null : await devices.RedeemAsync(request.Code, request.DeviceName, address, ct);
            if (paired is null)
            {
                brake.PairFailed(address);
                return PairRefused();
            }

            await audit.AddAsync(paired.PersonName, "device.paired", address, "device " + DeviceStore.CleanName(request!.DeviceName), ct);
            return Json(new { token = paired.Token, deviceId = paired.DeviceId, personName = paired.PersonName, api = 1 });
        }

        private static IResult PairRefused() => Results.Problem("The code is not valid or has expired.", statusCode: 401);

        // https as the server sees it (the base believes X-Forwarded-Proto only from its trusted proxies), or the machine itself under the name localhost (development and tests).
        private static bool ArrivedSecurely(HttpContext context) =>
            context.Request.IsHttps
            || (string.Equals(context.Request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                && (context.Connection.RemoteIpAddress is null || System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)));

        // At most 2 KB, read here and not through the binding of the framework: this path is open, and the door allows 30 MB for the others.
        private static async Task<PairRequest?> ReadPairRequestAsync(HttpRequest request, CancellationToken ct)
        {
            if (request.ContentLength is > PairBodyLimit)
            {
                return null;
            }

            var buffer = new byte[PairBodyLimit + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(total), ct)) > 0)
            {
                total += read;
            }

            if (total > PairBodyLimit)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PairRequest>(buffer.AsSpan(0, total), _options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/v1/status", () => Json(new { name = "JuNotes", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0", api = 1 }));

            endpoints.MapPost(PairPath, PairAsync);

            var secured = endpoints.MapGroup("/api/v1");
            // Giving the old rows to their owner did not work (logged, tried again at the next start): no sync on rows nobody owns.
            secured.AddEndpointFilter(async (invocation, next) => invocation.HttpContext.Items[OwnerKey] is TokenOwner { State: TokenState.Pending }
                ? Results.Problem("The server is not ready: the notes are not assigned yet. Try again later.", statusCode: 503)
                : await next(invocation));
            var sync = secured.MapGroup("/sync");
            sync.MapPost("/push", async (PushRequest request, HttpContext context, SyncService service, ISecurityLog log, CancellationToken ct) =>
            {
                try
                {
                    return Json(await service.PushAsync(request, OwnerOf(context).Person, log, ct));
                }
                catch (SyncValidationException error)
                {
                    return Results.Problem(error.Message, statusCode: 400);
                }
            });
            sync.MapGet("/info", (BackupService backup, TimeProvider time) => Json(new { serverTime = time.GetUtcNow(), lastBackupAt = backup.Latest()?.At }));
            sync.MapGet("/pull", async (long? since, int? limit, HttpContext context, SyncService service, CancellationToken ct) => Json(await service.PullAsync(since ?? 0, limit ?? SyncService.MaxChanges, PersonId(context), ct)));

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
            assetApi.MapPut("/{id:guid}", async (Guid id, HttpRequest request, HttpContext context, AssetService assets, CancellationToken ct) =>
            {
                try
                {
                    // A file of somebody else is left alone and the answer is the same as for a stored one.
                    await assets.SaveAsync(id, request.Body, PersonId(context), ct);
                    return Results.NoContent();
                }
                catch (AssetException error)
                {
                    return Results.Problem(error.Message, statusCode: error.StatusCode);
                }
            });
            assetApi.MapMethods("/{id:guid}", ["HEAD"], async (Guid id, HttpContext context, AssetService assets, CancellationToken ct) => await assets.ExistsAsync(id, PersonId(context), ct) ? Results.Ok() : Results.NotFound());
            assetApi.MapGet("/{id:guid}", async (Guid id, HttpContext context, AssetService assets, CancellationToken ct) => await assets.OpenAsync(id, PersonId(context), ct) is { } file ? Results.Stream(file.Stream, file.ContentType, enableRangeProcessing: true) : Results.NotFound());

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
