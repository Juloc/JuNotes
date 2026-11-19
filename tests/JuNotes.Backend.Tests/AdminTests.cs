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
        public const string Password = "ein-langes-test-passwort";

        private readonly TestDatabase _database;
        private readonly string? _password;
        private readonly string _codex;

        public string BackupDirectory { get; } = Path.Combine(Path.GetTempPath(), "junotes-admin-" + Guid.NewGuid().ToString("N"));

        public AdminHost(TestDatabase database, string? password, string codex)
        {
            _database = database;
            _password = password;
            _codex = codex;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JuNotes", _database.ConnectionString);
            builder.UseSetting("Sync:Token", "sync-token");
            builder.UseSetting("Backup:Directory", BackupDirectory);
            builder.UseSetting("Backup:BeforeMigration", "false");
            builder.UseSetting("Backup:PgDump", PostgresTools.PgDump);
            builder.UseSetting("Codex:Executable", _codex);
            builder.UseSetting("Codex:Home", Path.Combine(BackupDirectory, "codex"));
            if (_password is not null)
            {
                builder.UseSetting("Admin:Password", _password);
            }
        }

        public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

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

        public static async Task LoginAsync(HttpClient client)
        {
            var response = await PostAsync(client, "/Admin/Login", "/Admin/Login", new() { ["password"] = AdminHost.Password });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

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

        private static async Task<(AdminHost Host, FakeCodex Fake, TestDatabase Database)> StartAsync(string? password = AdminHost.Password, string codex = SignedInCodex)
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex(codex);
            return (new AdminHost(database, password, fake.Executable), fake, database);
        }

        [RequiresDatabaseFact]
        public async Task Without_a_login_the_admin_area_sends_you_to_the_login_page()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var response = await host.Browser().GetAsync(new Uri("/Admin/Profiles", UriKind.Relative));

                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Equal("/Admin/Login", response.Headers.Location!.AbsolutePath);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_api_token_does_not_open_the_admin_area()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();
                client.DefaultRequestHeaders.Authorization = new("Bearer", "sync-token");

                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task Without_a_configured_password_the_area_stays_closed_and_says_how_to_open_it()
        {
            var (host, fake, database) = await StartAsync(password: null);
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();

                var page = await client.GetStringAsync(new Uri("/Admin/Login", UriKind.Relative));
                Assert.Contains("Admin__Password", page, StringComparison.Ordinal);

                // There is no form to log in with, and a post made by hand is refused as well.
                Assert.DoesNotContain("<form", page, StringComparison.Ordinal);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(new Uri("/Admin/Login", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = "" }))).StatusCode);
                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_wrong_password_is_refused_and_the_right_one_opens_the_area()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();

                var wrong = await Web.PostAsync(client, "/Admin/Login", "/Admin/Login", new() { ["password"] = "falsch" });
                Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
                Assert.Contains("stimmt nicht", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);

                await Web.LoginAsync(client);
                var overview = await client.GetAsync(new Uri("/Admin", UriKind.Relative));

                Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
                Assert.Contains("Übersicht", await overview.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_cookie_cannot_be_read_by_scripts_and_is_strict()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();
                var token = await Web.TokenAsync(client, "/Admin/Login");

                var response = await client.PostAsync(new Uri("/Admin/Login", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = AdminHost.Password, ["__RequestVerificationToken"] = token }));

                var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("junotes-admin=", StringComparison.Ordinal));
                Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_post_without_the_anti_forgery_token_is_refused()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();

                var response = await client.PostAsync(new Uri("/Admin/Login", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = AdminHost.Password }));

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task Five_wrong_tries_lock_the_door_even_for_the_right_password()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();
                for (var i = 0; i < AdminAccess.MaxFailures; i++)
                {
                    await Web.PostAsync(client, "/Admin/Login", "/Admin/Login", new() { ["password"] = "falsch" + i });
                }

                var response = await Web.PostAsync(client, "/Admin/Login", "/Admin/Login", new() { ["password"] = AdminHost.Password });

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("Zu viele Versuche", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task After_logging_in_only_a_path_of_the_admin_area_can_be_the_next_page()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();

                var response = await Web.PostAsync(client, "/Admin/Login", "/Admin/Login", new() { ["password"] = AdminHost.Password, ["returnUrl"] = "https://evil.example/steal" });

                Assert.Equal("/Admin", response.Headers.Location!.OriginalString);
            }
        }

        [RequiresDatabaseFact]
        public async Task Logging_out_closes_the_area_again()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();
                await Web.LoginAsync(client);

                await Web.PostAsync(client, "/Admin", "/Admin/Logout", []);

                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_profile_can_be_changed_switched_off_and_reset()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();
                await Web.LoginAsync(client);
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
        public async Task An_empty_prompt_or_an_unknown_value_is_refused_with_a_message()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();
                await Web.LoginAsync(client);

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
                var client = host.Browser();
                await Web.LoginAsync(client);

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
                var client = host.Browser();
                await Web.LoginAsync(client);

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
                var client = host.Browser();
                await Web.LoginAsync(client);
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

    public class AdminAccessTests
    {
        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;
        }

        [Fact]
        public void The_lock_ends_after_ten_minutes()
        {
            var clock = new Clock();
            var access = new AdminAccess(new AdminOptions { Password = "richtig" }, clock);
            for (var i = 0; i < AdminAccess.MaxFailures; i++)
            {
                access.TryLogin("falsch", "1.2.3.4");
            }

            Assert.Equal(LoginResult.LockedOut, access.TryLogin("richtig", "1.2.3.4"));
            clock.Now += AdminAccess.Window + TimeSpan.FromSeconds(1);

            Assert.Equal(LoginResult.Accepted, access.TryLogin("richtig", "1.2.3.4"));
        }

        [Fact]
        public void One_address_being_locked_out_does_not_lock_the_others()
        {
            var access = new AdminAccess(new AdminOptions { Password = "richtig" }, new Clock());
            for (var i = 0; i < AdminAccess.MaxFailures; i++)
            {
                access.TryLogin("falsch", "1.2.3.4");
            }

            Assert.Equal(LoginResult.Accepted, access.TryLogin("richtig", "5.6.7.8"));
        }

        [Fact]
        public void A_right_password_clears_the_failures()
        {
            var access = new AdminAccess(new AdminOptions { Password = "richtig" }, new Clock());
            for (var round = 0; round < 3; round++)
            {
                access.TryLogin("falsch", "1.2.3.4");
                access.TryLogin("falsch", "1.2.3.4");
                Assert.Equal(LoginResult.Accepted, access.TryLogin("richtig", "1.2.3.4"));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Without_a_password_nobody_gets_in(string? password)
            => Assert.Equal(LoginResult.Disabled, new AdminAccess(new AdminOptions { Password = password }, new Clock()).TryLogin("", "1.2.3.4"));

        [Theory]
        [InlineData("/Admin/Profiles", "/Admin/Profiles")]
        [InlineData("/admin", "/admin")]
        [InlineData("https://evil.example", "/Admin")]
        [InlineData("//evil.example/Admin", "/Admin")]
        [InlineData("/Admin\\evil", "/Admin")]
        [InlineData("/Other", "/Admin")]
        [InlineData(null, "/Admin")]
        public void The_next_page_is_always_one_of_the_admin_area(string? asked, string expected)
            => Assert.Equal(expected, JuNotes.Frontend.Pages.Admin.LoginModel.SafeReturn(asked));
    }
}
