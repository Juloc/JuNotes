using System.Reflection;
using JuNotes.Backend.Ai;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Backup;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Database;
using JuNotes.Backend.School;
using JuNotes.Backend.Service;
using JuNotes.Integrations.Codex;
using JuNotes.Workers;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddJuNotesServices(builder.Configuration);
builder.Services.AddJuNotesWorkers();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));

var app = builder.Build();

// The database is current before the first request is answered; a failed upgrade rolls back and stops the host.
var backend = typeof(MigrationSet).Assembly;
var data = app.Services.GetRequiredService<NpgsqlDataSource>();
var migrations = MigrationSet.FromAssembly(backend);
var backups = app.Services.GetRequiredService<BackupService>();
if (backups.Options.BeforeMigration && await DatabaseUpgrade.PendingAsync(data, migrations, app.Lifetime.ApplicationStopping) > 0)
{
    // Anything that changes the schema is preceded by a backup; if that fails, nothing is changed and the host does not start.
    await backups.RunAsync(BackupKind.BeforeMigration, app.Lifetime.ApplicationStopping);
}

await DatabaseUpgrade.RunAsync(data, migrations, DatabaseUpgrade.ReadBaseline(backend), app.Lifetime.ApplicationStopping);

await app.Services.GetRequiredService<AiProfileStore>().EnsureDefaultsAsync(app.Lifetime.ApplicationStopping);

app.UseStaticFiles();
app.MapRazorPages();
app.MapGet("/health/live", () => Results.Ok());
app.MapGet("/health/ready", async (HealthService health, CancellationToken ct) => await health.IsReadyAsync(ct) ? Results.Ok() : Results.StatusCode(503));
app.MapGet("/api/v1/status", () => Results.Json(new { name = "JuNotes", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0", api = 1 }));

// Everything under /api/v1 except /status needs the instance token. This runs before routing and body binding, so a caller without the token learns nothing else.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments("/api/v1") && !path.StartsWithSegments("/api/v1/status"))
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
var secured = app.MapGroup("/api/v1");
var sync = secured.MapGroup("/sync");
sync.MapPost("/push", async (PushRequest request, SyncService service, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await service.PushAsync(request, ct));
    }
    catch (SyncValidationException error)
    {
        return Results.Problem(error.Message, statusCode: 400);
    }
});
sync.MapGet("/info", (BackupService backup, TimeProvider time) => Results.Ok(new { serverTime = time.GetUtcNow(), lastBackupAt = backup.Latest()?.At }));
sync.MapGet("/pull", async (long? since, int? limit, SyncService service, CancellationToken ct) => Results.Ok(await service.PullAsync(since ?? 0, limit ?? SyncService.MaxChanges, ct)));

var schoolApi = secured.MapGroup("/school");
schoolApi.MapGet("/status", (SchoolImportService import) => Results.Ok(import.Status));
schoolApi.MapGet("/calendar", async (DateTimeOffset? from, DateTimeOffset? to, SchoolStore store, TimeProvider time, CancellationToken ct) =>
{
    var start = from ?? time.GetUtcNow().AddDays(-1);
    var end = to ?? start.AddDays(14);
    return end <= start || end - start > TimeSpan.FromDays(366)
        ? Results.Problem("The range must be positive and at most a year.", statusCode: 400)
        : Results.Ok(await store.EventsAsync(start, end, ct));
});
schoolApi.MapGet("/news", async (int? limit, SchoolStore store, CancellationToken ct) => Results.Ok(await store.NewsAsync(limit ?? 20, ct)));

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
aiApi.MapGet("/profiles", async (AiProfileStore store, CancellationToken ct) => Results.Ok((await store.ListAsync(ct)).Select(profile => new { profile.Key, profile.Name, profile.Enabled })));
aiApi.MapPost("/{task}", async (string task, AiRequest request, LearningService learning, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await learning.RunAsync(task, request, ct));
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

app.Run();

public partial class Program;
