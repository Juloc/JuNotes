using System.Net;
using JuNotes.Backend.Ai;
using JuNotes.Backend.Backup;
using JuNotes.Frontend.Admin;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    // The real host with a throwaway database, a fake codex program and an admin password.
    internal sealed class AdminHost : WebApplicationFactory<Program>
    {
        public const string SetupCode = "test-setup-code";

        // Longer than the least the server accepts (32 characters).
        public const string Token = "token-of-the-tests-0123456789-abcdefghij";

        private readonly TestDatabase _database;
        private readonly string _codex;
        private readonly (string Url, string Token)? _paperless;

        // A stand-in for pg_dump, for tests that only need a backup to be made (the real one must be of the version of the server).
        public string? PgDump { get; init; }

        public string BackupDirectory { get; } = Path.Combine(Path.GetTempPath(), "junotes-admin-" + Guid.NewGuid().ToString("N"));

        public AdminHost(TestDatabase database, string codex, (string Url, string Token)? paperless = null)
        {
            _database = database;
            _codex = codex;
            _paperless = paperless;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JuNotes", _database.ConnectionString);
            builder.UseSetting("Sync:Token", Token);
            builder.UseSetting("Backup:Directory", BackupDirectory);
            builder.UseSetting("Backup:BeforeMigration", "false");
            builder.UseSetting("Backup:PgDump", PgDump ?? PostgresTools.PgDump);
            builder.UseSetting("Codex:Executable", _codex);
            builder.UseSetting("Codex:Home", Path.Combine(BackupDirectory, "codex"));
            builder.UseSetting("Assets:Directory", Path.Combine(BackupDirectory, "assets"));
            // The base: a fixed key and setup code (made up for the tests) and a folder of its own for the key file.
            builder.UseSetting("BASE_SECRET_KEY", Convert.ToBase64String(new byte[32].Select((_, index) => (byte)(index + 1)).ToArray()));
            builder.UseSetting("BASE_SETUP_CODE", SetupCode);
            builder.UseSetting("BASE_DATA", Path.Combine(BackupDirectory, "base"));

            if (_paperless is { } paperless)
            {
                builder.UseSetting("Paperless:BaseUrl", paperless.Url);
                builder.UseSetting("Paperless:Token", paperless.Token);
            }
        }

        public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        // A person with a session of the base, made directly in the database (the sign-in itself, with its second factor, is tested in the base). The throwaway
        // names and values exist only in the test database.
        // What to store for the first person who signs in, as their own (the notes of a test).
        public Func<JuNotes.Backend.Data.Sync.SyncOwner, Task>? SeedFor { get; set; }

        public async Task<HttpClient> SignedInAsync(string role = "admin")
        {
            var (_, client) = await SignedInPersonAsync(role);
            return client;
        }

        public async Task<(Guid UserId, HttpClient Client)> SignedInPersonAsync(string role = "admin")
        {
            var users = Services.GetRequiredService<Base.Accounts.UserStore>();
            var user = await users.CreateAsync(role + "-" + Guid.NewGuid().ToString("N")[..8], "not-a-hash", "TESTSECRET", confirmed: true, recoveryHashes: [], roles: [role], replacePending: false);
            var token = await Services.GetRequiredService<Base.Accounts.SessionStore>().CreateAsync(user.Id, "127.0.0.1", "tests");
            var client = Browser();
            client.DefaultRequestHeaders.Add("Cookie", "notes_sid=" + token);
            if (SeedFor is { } seed)
            {
                SeedFor = null;
                await seed(new JuNotes.Backend.Data.Sync.SyncOwner(user.Id, user.Name, "test"));
            }

            return (user.Id, client);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(BackupDirectory))
            {
                Directory.Delete(BackupDirectory, recursive: true);
            }
        }
    }
}
