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
});
builder.Services.AddRazorPages(options =>
{
    // Until the web pages move to the base (milestone 2): behind the session of the base, one policy per folder, nothing open.
    options.Conventions.AuthorizeFolder("/Admin", AdminSetup.AdminPolicy);
    options.Conventions.AuthorizeFolder("/Notes", AdminSetup.ReaderPolicy);
});
builder.Services.AddJuNotesSignIn();
// After the base, so that the one data source of the server is ours (the base asks for "the" NpgsqlDataSource and gets this one).
builder.Services.AddJuNotesServices(builder.Configuration);
builder.Services.AddJuNotesWorkers();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));

var app = builder.Build();

var stopping = app.Lifetime.ApplicationStopping;
var backend = typeof(MigrationSet).Assembly;
var data = app.Services.GetRequiredService<NpgsqlDataSource>();
var migrations = MigrationSet.FromAssembly(backend);

// Two sequences of migrations live in the one database: ours (numbers, table app_migrations) and those of the base (table schema_migrations). Ours was called
// schema_migrations before the base arrived and is renamed first. The base goes first, so that ours may point at its tables.
await DatabaseUpgrade.RenameLegacyTableAsync(data, stopping);
var backups = app.Services.GetRequiredService<BackupService>();
if (backups.Options.BeforeMigration && await AnythingToUpgradeAsync(data, migrations, stopping))
{
    // Anything that changes the schema is preceded by a backup; if that fails, nothing is changed and the host does not start.
    await backups.RunAsync(BackupKind.BeforeMigration, stopping);
}

await app.StartBaseAsync();
await DatabaseUpgrade.RunAsync(data, migrations, DatabaseUpgrade.ReadBaseline(backend), stopping);

await app.Services.GetRequiredService<AiProfileStore>().EnsureDefaultsAsync(stopping);

// The contract with the iPad app is its own door and comes before everything of the base.
app.UseV1Door();
app.UseBase();
// Until the web pages move to the base (milestone 2): the old pages style single elements with the style attribute (cover colours); only that, on these two folders.
app.Use((context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Notes", StringComparison.OrdinalIgnoreCase) || context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.ContentSecurityPolicy = SecurityHeaders.Csp + "; style-src-attr 'unsafe-inline'";
    }

    return next();
});
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapBase();
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

    await using var exists = data.CreateCommand("select to_regclass('app_migrations') is not null");
    if (!(bool)(await exists.ExecuteScalarAsync(ct))!)
    {
        return false;
    }

    var known = new HashSet<string>(StringComparer.Ordinal);
    await using (var table = data.CreateCommand("select to_regclass('schema_migrations') is not null"))
    {
        if ((bool)(await table.ExecuteScalarAsync(ct))!)
        {
            await using var ids = data.CreateCommand("select id from schema_migrations");
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
