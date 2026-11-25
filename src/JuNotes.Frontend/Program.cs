using JuNotes.Backend.Ai;
using Base.Accounts;
using Base.Core;
using Base.Web;
using JuNotes.Frontend.Admin;
using JuNotes.Frontend.Api;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Backup;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Database;
using JuNotes.Backend.School;
using JuNotes.Backend.Service;
using JuNotes.Integrations.Codex;
using JuNotes.Integrations.Paperless;
using JuNotes.Workers;
using Npgsql;

// `decrypt-backup <encrypted file> <output file>` reads the passphrase from Backup__Passphrase, undoes the encryption of a copy and ends.
if (args is ["decrypt-backup", var encrypted, var plain])
{
    var passphrase = Environment.GetEnvironmentVariable("Backup__Passphrase");
    if (string.IsNullOrEmpty(passphrase))
    {
        await Console.Error.WriteLineAsync("Set the passphrase in the environment variable Backup__Passphrase.");
        return 2;
    }

    try
    {
        await BackupCrypto.DecryptAsync(encrypted, plain, passphrase, CancellationToken.None);
        Console.WriteLine("Decrypted to " + plain);
        return 0;
    }
    catch (Exception error) when (error is BackupDecryptionException or IOException)
    {
        await Console.Error.WriteLineAsync(error.Message);
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
// The base reaches the same database as the rest of the server; an installation made before the base arrived keeps its ConnectionStrings__JuNotes.
builder.Configuration["BASE_DB"] ??= builder.Configuration.GetConnectionString("JuNotes");
// The one place where the app says what it is (read by /api/about, the sign-in pages and the people management).
builder.AddBase(new BaseAppOptions("notes", "Notes", typeof(Program).Assembly)
{
    Description = new Localized("Notebooks for school and everyday life, written by hand or typed.", "Hefte für Schule und Alltag, von Hand geschrieben oder getippt."),
    Icon = "file",
    AccentHue = 75,                                   // the same number as --accent-hue in wwwroot/app.css
    Start = "/",
    Roles =
    [
        new AppRole("admin", new Localized("Administrator", "Administration")),
        new AppRole("member", new Localized("Member", "Mitglied")),
    ],
    // Our own table of numbered migrations is called schema_migrations; the base keeps its own under another name and leaves ours alone (DATA-04).
    MigrationTable = "base_migrations",
    // The contract with the iPad app: the instance token, no session (SEC-07).
    Doors = [new ApiDoor(V1Door.Prefix, V1Door.CheckAsync, V1Door.MaxBodyBytes)],
});
builder.Services.AddSingleton<CodexLoginSession>();
// A connection of its own without a time limit for what may take long on a big table (the schema upgrade and the giving of old rows to their owner).
var maintenanceConnection = new NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("JuNotes")) { CommandTimeout = 0, ApplicationName = "notes-maintenance", MaxPoolSize = 2 };
var maintenance = new MaintenanceData(Base.Data.Database.Create(maintenanceConnection.ConnectionString, builder.Configuration["BASE_DB_PASSWORD_FILE"]));
builder.Services.AddSingleton(maintenance);
builder.Services.AddSingleton<OwnerDirectory>();
builder.Services.AddSingleton<TokenBrake>();
builder.Services.AddSingleton<ISecurityLog, SecurityLog>();
// After the base, so that the one data source of the server is ours (the base asks for "the" NpgsqlDataSource and gets this one).
builder.Services.AddJuNotesServices(builder.Configuration);
builder.Services.AddJuNotesWorkers();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));

var app = builder.Build();

var stopping = app.Lifetime.ApplicationStopping;
var backend = typeof(MigrationSet).Assembly;
var data = app.Services.GetRequiredService<NpgsqlDataSource>();
var migrations = MigrationSet.FromAssembly(backend);

// Two sequences of migrations live in the one database: ours (numbers, table schema_migrations) and those of the base (table base_migrations). One table of our
// baseline has the name of a table of the base and is renamed first. The base goes first, so that ours may point at its tables.
var backups = app.Services.GetRequiredService<BackupService>();
if (backups.Options.BeforeMigration && await AnythingToUpgradeAsync(data, migrations, stopping))
{
    // Anything that changes the schema is preceded by a backup; if that fails, nothing is changed and the host does not start.
    await backups.RunAsync(BackupKind.BeforeMigration, stopping);
}

// Only now, after the backup: the one rename (a table that only has another name afterwards, nothing is rewritten).
await DatabaseUpgrade.RenameLegacyTableAsync(maintenance.Source, stopping);
await app.StartBaseAsync();
await DatabaseUpgrade.RunAsync(maintenance.Source, migrations, DatabaseUpgrade.ReadBaseline(backend), stopping);

await app.Services.GetRequiredService<AiProfileStore>().EnsureDefaultsAsync(stopping);
await app.Services.GetRequiredService<OwnerDirectory>().EnsureClaimedAsync(stopping);

app.UseBase();
// The addresses of the old pages lead to the new ones, so bookmarks keep working.
var oldAddresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["/Notes"] = "/#/",
    ["/Admin"] = "/#/admin",
    ["/Admin/Codex"] = "/#/admin/codex",
    ["/Admin/Profiles"] = "/#/admin/profiles",
    ["/Admin/Backups"] = "/#/admin/backups",
};
foreach (var (from, to) in oldAddresses)
{
    app.MapGet(from, () => Results.Redirect(to));
}

app.MapGet("/Notes/{id:guid}", (Guid id) => Results.Redirect($"/#/notebook/{id}"));
app.MapBase();
app.MapV1();
app.MapWebApi();
app.MapAdminApi();
app.MapGet("/health/live", () => Results.Ok());
app.MapGet("/health/ready", async (HealthService health, CancellationToken ct) => await health.IsReadyAsync(ct) ? Results.Ok() : Results.StatusCode(503));

app.Run();
return 0;

// Worth a backup first: a database that an older server filled, and something of either sequence still to apply. A new, empty database has nothing to save.
static async Task<bool> AnythingToUpgradeAsync(NpgsqlDataSource data, MigrationSet migrations, CancellationToken ct)
{
    if (await DatabaseUpgrade.PendingAsync(data, migrations, ct) > 0)
    {
        return true;
    }

    await using var exists = data.CreateCommand("select to_regclass('schema_migrations') is not null");
    if (!(bool)(await exists.ExecuteScalarAsync(ct))!)
    {
        return false;
    }

    var known = new HashSet<string>(StringComparer.Ordinal);
    await using (var table = data.CreateCommand("select to_regclass('base_migrations') is not null"))
    {
        if ((bool)(await table.ExecuteScalarAsync(ct))!)
        {
            await using var ids = data.CreateCommand("select id from base_migrations");
            await using var reader = await ids.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                known.Add(reader.GetString(0));
            }
        }
    }

    return new[] { AccountsModule.Migrations, BaseApp.Migrations }.SelectMany(set => set.Scripts).Any(script => !known.Contains(script.Id));
}

public partial class Program;
