using System.Net;
using System.Text.RegularExpressions;
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

    internal static partial class Web
    {
        // Razor pages refuse a post without the anti-forgery token, which is in the page's form and in a cookie.
        public static async Task<string> TokenAsync(HttpClient client, string url)
        {
            var html = await client.GetStringAsync(new Uri(url, UriKind.Relative));
            return TokenPattern().Match(html) is { Success: true } match ? WebUtility.HtmlDecode(match.Groups["value"].Value) : throw new InvalidOperationException("No anti-forgery token on " + url);
        }

        public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string page, string url, Dictionary<string, string> fields)
        {
            fields["__RequestVerificationToken"] = await TokenAsync(client, page);
            return await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(fields));
        }

        // Where a redirect leads, as a path: the server answers with a relative or an absolute address depending on who sent the user away.
        public static string PathOf(HttpResponseMessage response)
            => response.Headers.Location is { IsAbsoluteUri: true } absolute ? absolute.AbsolutePath : response.Headers.Location!.OriginalString.Split('?')[0];

        [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
        private static partial Regex TokenPattern();
    }

    public class AdminTests
    {
        private const string SignedInCodex = """
            case "$1 $2" in
              "--version ") echo "codex-cli 0.166.0"; exit 0;;
              "login status") echo "Logged in using ChatGPT"; exit 0;;
              "logout ") exit 0;;
            esac
            exit 0
            """;

        private const string SignedOutCodex = """
            case "$1 $2" in
              "--version ") echo "codex-cli 0.166.0"; exit 0;;
              "login status") echo "Not logged in"; exit 1;;
              "login --device-auth") echo "https://auth.example.com/codex/device"; echo "WXYZ-98765"; sleep 30;;
            esac
            exit 0
            """;

        private static async Task<(AdminHost Host, FakeCodex Fake, TestDatabase Database)> StartAsync(string codex = SignedInCodex)
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex(codex);
            return (new AdminHost(database, fake.Executable), fake, database);
        }

        [RequiresDatabaseFact]
        public async Task Without_a_session_the_admin_area_sends_you_to_the_front_door()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var response = await host.Browser().GetAsync(new Uri("/Admin/Profiles", UriKind.Relative));

                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Equal("/", Web.PathOf(response));
            }
        }

        [RequiresDatabaseFact]
        public async Task A_member_reads_notes_but_does_not_open_the_admin_area_and_an_admin_opens_both()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var member = await host.SignedInAsync("member");
                var admin = await host.SignedInAsync();

                Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(new Uri("/Admin/Codex", UriKind.Relative))).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(new Uri("/Notes", UriKind.Relative))).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(new Uri("/Notes", UriKind.Relative))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task An_ended_session_or_an_unknown_cookie_opens_nothing()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();
                client.DefaultRequestHeaders.Add("Cookie", "notes_sid=not-a-session");

                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);
                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Notes", UriKind.Relative))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_old_pages_get_only_the_one_relaxation_they_need_in_the_content_policy()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var admin = await host.SignedInAsync();
                var page = await admin.GetAsync(new Uri("/Notes", UriKind.Relative));
                var front = await host.Browser().GetAsync(new Uri("/", UriKind.Relative));

                var policy = Assert.Single(page.Headers.GetValues("Content-Security-Policy"));
                Assert.StartsWith(Base.Web.SecurityHeaders.Csp, policy, StringComparison.Ordinal);
                Assert.EndsWith("; style-src-attr 'unsafe-inline'", policy, StringComparison.Ordinal);
                Assert.DoesNotContain("script-src 'self' 'unsafe", policy, StringComparison.Ordinal);
                // Everywhere else the policy of the base stands as it is.
                Assert.Equal(Base.Web.SecurityHeaders.Csp, Assert.Single(front.Headers.GetValues("Content-Security-Policy")));
            }
        }

        [RequiresDatabaseFact]
        public async Task The_api_token_does_not_open_the_admin_area()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();
                client.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);

                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_post_without_the_anti_forgery_token_is_refused()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await host.SignedInAsync();

                var response = await client.PostAsync(new Uri("/Admin/Codex?handler=Login", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>()));

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_profile_can_be_changed_switched_off_and_reset()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await host.SignedInAsync();
                var store = host.Services.GetRequiredService<AiProfileStore>();

                var saved = await Web.PostAsync(client, "/Admin/Profiles", "/Admin/Profiles?handler=Save", new() { ["key"] = "quiz", ["systemPrompt"] = "Frage nur nach Jahreszahlen.", ["difficulty"] = "schwer", ["tone"] = "sachlich" });
                Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
                var profile = (await store.GetAsync("quiz", CancellationToken.None))!;
                Assert.Equal("Frage nur nach Jahreszahlen.", profile.SystemPrompt);
                Assert.Equal("schwer", profile.Difficulty);
                Assert.Equal("sachlich", profile.Tone);
                Assert.False(profile.Enabled);
                Assert.Contains("ist gespeichert", await client.GetStringAsync(new Uri("/Admin/Profiles", UriKind.Relative)), StringComparison.Ordinal);

                await Web.PostAsync(client, "/Admin/Profiles", "/Admin/Profiles?handler=Reset", new() { ["key"] = "quiz" });
                Assert.Equal(AiDefaults.Profiles[0], await store.GetAsync("quiz", CancellationToken.None));
            }
        }

        [RequiresDatabaseFact]
        public async Task The_admin_chooses_model_effort_and_limit_and_sees_the_usage()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await host.SignedInAsync();
                var settings = host.Services.GetRequiredService<AiSettingsStore>();

                var saved = await Web.PostAsync(client, "/Admin/Codex", "/Admin/Codex?handler=Settings", new() { ["model"] = "mein-modell", ["effort"] = "high", ["dailyLimit"] = "25" });
                Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
                Assert.Equal(new AiSettings("mein-modell", "high", 25), await settings.GetAsync(CancellationToken.None));

                var page = await client.GetStringAsync(new Uri("/Admin/Codex", UriKind.Relative));
                Assert.Contains("Gespeichert", page, StringComparison.Ordinal);
                Assert.Contains("mein-modell", page, StringComparison.Ordinal);
                Assert.Contains("von 25 Anfragen", page, StringComparison.Ordinal);
                Assert.Contains("von 25 Anfragen", await client.GetStringAsync(new Uri("/Admin", UriKind.Relative)), StringComparison.Ordinal);

                await Web.PostAsync(client, "/Admin/Codex", "/Admin/Codex?handler=Settings", new() { ["model"] = "x", ["effort"] = "turbo", ["dailyLimit"] = "25" });
                Assert.Contains("Aufwand muss aus der Liste", await client.GetStringAsync(new Uri("/Admin/Codex", UriKind.Relative)), StringComparison.Ordinal);
                Assert.Equal("high", (await settings.GetAsync(CancellationToken.None)).Effort);

                await Web.PostAsync(client, "/Admin/Codex", "/Admin/Codex?handler=Settings", new() { ["model"] = "", ["effort"] = "", ["dailyLimit"] = "viele" });
                Assert.Contains("ganze Zahl", await client.GetStringAsync(new Uri("/Admin/Codex", UriKind.Relative)), StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task An_empty_prompt_or_an_unknown_value_is_refused_with_a_message()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await host.SignedInAsync();

                await Web.PostAsync(client, "/Admin/Profiles", "/Admin/Profiles?handler=Save", new() { ["key"] = "quiz", ["systemPrompt"] = "   ", ["difficulty"] = "mittel", ["tone"] = "freundlich", ["enabled"] = "on" });

                Assert.Contains("darf nicht leer sein", await client.GetStringAsync(new Uri("/Admin/Profiles", UriKind.Relative)), StringComparison.Ordinal);
                Assert.Equal(AiDefaults.Profiles[0].SystemPrompt, (await host.Services.GetRequiredService<AiProfileStore>().GetAsync("quiz", CancellationToken.None))!.SystemPrompt);
                Assert.Equal(HttpStatusCode.NotFound, (await Web.PostAsync(client, "/Admin/Profiles", "/Admin/Profiles?handler=Save", new() { ["key"] = "gibtsnicht", ["systemPrompt"] = "x", ["difficulty"] = "mittel", ["tone"] = "freundlich" })).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_overview_shows_the_server_and_a_signed_in_codex()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await host.SignedInAsync();

                var page = await client.GetStringAsync(new Uri("/Admin", UriKind.Relative));

                Assert.Contains("erreichbar", page, StringComparison.Ordinal);
                Assert.Contains("angemeldet mit ChatGPT", page, StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_manual_backup_is_written_and_listed()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await host.SignedInAsync();

                await Web.PostAsync(client, "/Admin/Backups", "/Admin/Backups?handler=Run", []);

                Assert.Equal(BackupKind.Manual, Assert.Single(BackupFiles.List(host.BackupDirectory)).Kind);
                var page = await client.GetStringAsync(new Uri("/Admin/Backups", UriKind.Relative));
                Assert.Contains("von Hand", page, StringComparison.Ordinal);
                Assert.Contains("Gesichert", page, StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_signed_out_codex_shows_the_device_code_and_the_login_can_be_cancelled()
        {
            var (host, fake, database) = await StartAsync(codex: SignedOutCodex);
            await using (database) using (fake) using (host)
            {
                var client = await host.SignedInAsync();
                Assert.Contains("Nicht angemeldet", await client.GetStringAsync(new Uri("/Admin/Codex", UriKind.Relative)), StringComparison.Ordinal);

                await Web.PostAsync(client, "/Admin/Codex", "/Admin/Codex?handler=Login", []);
                var waiting = await client.GetStringAsync(new Uri("/Admin/Codex", UriKind.Relative));
                Assert.Contains("WXYZ-98765", waiting, StringComparison.Ordinal);
                Assert.Contains("https://auth.example.com/codex/device", waiting, StringComparison.Ordinal);
                Assert.Contains("http-equiv=\"refresh\"", waiting, StringComparison.Ordinal);

                await Web.PostAsync(client, "/Admin/Codex", "/Admin/Codex?handler=Cancel", []);
                Assert.DoesNotContain("WXYZ-98765", await client.GetStringAsync(new Uri("/Admin/Codex", UriKind.Relative)), StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_open_endpoints_stay_open_and_the_api_stays_closed()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();

                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/status", UriKind.Relative))).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/v1/sync/pull", UriKind.Relative))).StatusCode);
            }
        }
    }
}
