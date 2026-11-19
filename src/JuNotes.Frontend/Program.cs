using System.Reflection;
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
await DatabaseUpgrade.RunAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), MigrationSet.FromAssembly(backend), DatabaseUpgrade.ReadBaseline(backend), app.Lifetime.ApplicationStopping);

app.UseStaticFiles();
app.MapRazorPages();
app.MapGet("/health/live", () => Results.Ok());
app.MapGet("/health/ready", async (HealthService health, CancellationToken ct) => await health.IsReadyAsync(ct) ? Results.Ok() : Results.StatusCode(503));
app.MapGet("/api/v1/status", () => Results.Json(new { name = "JuNotes", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0", api = 1 }));

app.Run();

public partial class Program;
