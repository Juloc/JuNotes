using System.Reflection;
using JuNotes.Backend.Backup;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Database;
using JuNotes.Backend.Service;
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

app.UseStaticFiles();
app.MapRazorPages();
app.MapGet("/health/live", () => Results.Ok());
app.MapGet("/health/ready", async (HealthService health, CancellationToken ct) => await health.IsReadyAsync(ct) ? Results.Ok() : Results.StatusCode(503));
app.MapGet("/api/v1/status", () => Results.Json(new { name = "JuNotes", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0", api = 1 }));

var sync = app.MapGroup("/api/v1/sync");
sync.AddEndpointFilter(async (context, next) =>
{
    var access = context.HttpContext.RequestServices.GetRequiredService<SyncAccess>();
    if (!access.IsConfigured)
    {
        return Results.Problem("Sync is not set up: set Sync:Token (environment variable Sync__Token).", statusCode: 503);
    }

    return access.IsAuthorized(context.HttpContext.Request.Headers.Authorization) ? await next(context) : Results.Unauthorized();
});
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

app.Run();

public partial class Program;
