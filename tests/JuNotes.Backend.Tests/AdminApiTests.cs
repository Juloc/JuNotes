using System.Net;
using System.Text;
using System.Text.Json;
using JuNotes.Frontend.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    // The administration as JSON (/api/web/v1/admin): only for the role admin, and the same things the old pages did.
    public sealed partial class AdminApiTests : IDisposable
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

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "junotes-adminapi-" + Guid.NewGuid().ToString("N"));

        public AdminApiTests() => Directory.CreateDirectory(_folder);

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private static Uri Url(string path) => new("/api/web/v1/admin" + path, UriKind.Relative);

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

        private static HttpRequestMessage Change(HttpMethod method, string path, string? json = null)
        {
            var request = new HttpRequestMessage(method, Url(path)) { Content = json is null ? null : Json(json) };
            // as the page of the base sends it
            request.Headers.Add("X-Base", "1");
            request.Headers.Add("Origin", "http://localhost");
            return request;
        }

        private async Task<(AdminHost Host, FakeCodex Fake, TestDatabase Database)> StartAsync(string codex)
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex(codex);
            var pgDump = Path.Combine(_folder, "pg_dump");
            await File.WriteAllTextAsync(pgDump, "#!/bin/sh\nfor a in \"$@\"; do case \"$a\" in --file=*) printf 'dump' > \"${a#--file=}\";; esac; done\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(pgDump, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return (new AdminHost(database, fake.Executable) { PgDump = pgDump }, fake, database);
        }

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        // The routes are the ones the server really has (taken from its own list of endpoints), so a new administration path cannot be left out of this test.
        private static List<(HttpMethod Method, string Path, string Template)> Endpoints(AdminHost host, string prefix)
        {
            var found = new List<(HttpMethod, string, string)>();
            foreach (var endpoint in host.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>())
            {
                var template = endpoint.RoutePattern.RawText ?? string.Empty;
                if (!template.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = GuidParameter().Replace(template, Guid.NewGuid().ToString());
                path = Parameter().Replace(path, "quiz");
                foreach (var method in endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                {
                    found.Add((new HttpMethod(method), path, template));
                }
            }

            return found;
        }

        [RequiresDatabaseFact]
        public async Task Only_an_administrator_gets_in_at_any_path()
        {
            var (host, fake, database) = await StartAsync(SignedInCodex);
            await using (database)
            using (fake)
            using (host)
            {
                var admin = await host.SignedInAsync();
                var member = await host.SignedInAsync("member");
                using var anonymous = host.Browser();
                var routes = Endpoints(host, "/api/web/v1/admin");

                // The list is the real one: at least what is known to exist is in it.
                Assert.True(routes.Count >= 12, $"only {routes.Count} administration endpoints found");
                foreach (var (method, path, template) in routes)
                {
                    var body = method == HttpMethod.Get ? null : "{}";
                    Assert.True((await anonymous.SendAsync(Change(method, path.Replace("/api/web/v1/admin", string.Empty, StringComparison.Ordinal), body))).StatusCode == HttpStatusCode.Unauthorized, $"anonymous {method} {template}");
                    Assert.True((await member.SendAsync(Change(method, path.Replace("/api/web/v1/admin", string.Empty, StringComparison.Ordinal), body))).StatusCode == HttpStatusCode.Forbidden, $"member {method} {template}");
                }

                Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Change(HttpMethod.Get, "/overview"))).StatusCode);

                // And every other path of the web API needs a session (no path of it answers a stranger).
                foreach (var (method, path, template) in Endpoints(host, "/api/web/v1").Where(route => !route.Template.StartsWith("/api/web/v1/admin", StringComparison.OrdinalIgnoreCase)))
                {
                    using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
                    request.Headers.Add("X-Base", "1");
                    request.Headers.Add("Origin", "http://localhost");
                    Assert.True((await anonymous.SendAsync(request)).StatusCode == HttpStatusCode.Unauthorized, $"anonymous {method} {template}");
                }
            }
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"\{[a-zA-Z]+:guid\}", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial System.Text.RegularExpressions.Regex GuidParameter();

        [System.Text.RegularExpressions.GeneratedRegex(@"\{[a-zA-Z]+\}", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial System.Text.RegularExpressions.Regex Parameter();

        [RequiresDatabaseFact]
        public async Task The_overview_and_the_codex_page_show_the_server_and_the_settings_can_be_saved_and_refused()
        {
            var (host, fake, database) = await StartAsync(SignedInCodex);
            await using (database)
            using (fake)
            using (host)
            {
                var admin = await host.SignedInAsync();

                var overview = await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/overview")));
                Assert.True(overview.GetProperty("databaseReady").GetBoolean());
                Assert.True(overview.GetProperty("codex").GetProperty("signedIn").GetBoolean());
                Assert.Equal(60, overview.GetProperty("limit").GetInt32());

                Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Change(HttpMethod.Put, "/codex/settings", """{"model":"gpt-test","effort":"high","dailyLimit":25}"""))).StatusCode);
                var codex = await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/codex")));
                Assert.Equal("gpt-test", codex.GetProperty("settings").GetProperty("model").GetString());
                Assert.Equal(25, codex.GetProperty("limit").GetInt32());
                // Signed in: no code is waiting (an empty value is left out of the answer).
                Assert.False(codex.TryGetProperty("waiting", out _));

                foreach (var bad in new[] { """{"effort":"enormous"}""", """{"model":"a b;rm -rf"}""", """{"dailyLimit":0}""", """{"dailyLimit":999999}""" })
                {
                    Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(Change(HttpMethod.Put, "/codex/settings", bad))).StatusCode);
                }

                // This stand-in stays signed in after "logout", so the answer is the honest one: it did not work.
                Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(Change(HttpMethod.Post, "/codex/logout"))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_signed_out_codex_gives_the_device_code_and_the_login_can_be_cancelled()
        {
            var (host, fake, database) = await StartAsync(SignedOutCodex);
            await using (database)
            using (fake)
            using (host)
            {
                var admin = await host.SignedInAsync();

                var started = await ReadAsync(await admin.SendAsync(Change(HttpMethod.Post, "/codex/login")));
                Assert.Equal("WXYZ-98765", started.GetProperty("code").GetString());
                var waiting = (await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/codex")))).GetProperty("waiting");
                Assert.Equal("https://auth.example.com/codex/device", waiting.GetProperty("url").GetString());

                Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Change(HttpMethod.Post, "/codex/cancel"))).StatusCode);
                var after = (await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/codex")))).GetProperty("status");
                Assert.False(after.GetProperty("signedIn").GetBoolean());
            }
        }

        [RequiresDatabaseFact]
        public async Task A_profile_can_be_changed_refused_and_reset()
        {
            var (host, fake, database) = await StartAsync(SignedInCodex);
            await using (database)
            using (fake)
            using (host)
            {
                var admin = await host.SignedInAsync();

                var list = await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/profiles")));
                var quiz = list.GetProperty("profiles").EnumerateArray().Single(profile => profile.GetProperty("key").GetString() == "quiz");
                var original = quiz.GetProperty("systemPrompt").GetString();

                Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Change(HttpMethod.Put, "/profiles/quiz", """{"systemPrompt":"Frage kurz.","difficulty":"schwer","tone":"sachlich","enabled":false,"model":"gpt-x","effort":"low"}"""))).StatusCode);
                var changed = (await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/profiles")))).GetProperty("profiles").EnumerateArray().Single(profile => profile.GetProperty("key").GetString() == "quiz");
                Assert.Equal("Frage kurz.", changed.GetProperty("systemPrompt").GetString());
                Assert.False(changed.GetProperty("enabled").GetBoolean());

                Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(Change(HttpMethod.Put, "/profiles/quiz", """{"systemPrompt":"","difficulty":"schwer","tone":"sachlich","enabled":true}"""))).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(Change(HttpMethod.Put, "/profiles/quiz", """{"systemPrompt":"x","difficulty":"unmöglich","tone":"sachlich","enabled":true}"""))).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Change(HttpMethod.Put, "/profiles/nothing", """{"systemPrompt":"x","difficulty":"leicht","tone":"sachlich","enabled":true}"""))).StatusCode);

                Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Change(HttpMethod.Post, "/profiles/quiz/reset"))).StatusCode);
                var reset = (await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/profiles")))).GetProperty("profiles").EnumerateArray().Single(profile => profile.GetProperty("key").GetString() == "quiz");
                Assert.Equal(original, reset.GetProperty("systemPrompt").GetString());
                Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Change(HttpMethod.Post, "/profiles/nothing/reset"))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_backup_is_made_by_hand_and_listed_and_the_page_says_where_the_key_is()
        {
            var (host, fake, database) = await StartAsync(SignedInCodex);
            await using (database)
            using (fake)
            using (host)
            {
                var admin = await host.SignedInAsync();
                Assert.Empty((await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/backups")))).GetProperty("files").EnumerateArray());

                var made = await ReadAsync(await admin.SendAsync(Change(HttpMethod.Post, "/backups/run")));
                Assert.Equal("Manual", made.GetProperty("kind").GetString());

                var listed = await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/backups")));
                Assert.Single(listed.GetProperty("files").EnumerateArray());
                Assert.Equal("none", listed.GetProperty("keyState").GetString());
                Assert.Equal(3, listed.GetProperty("hour").GetInt32());

                // A key file and no encrypted copy anywhere: the page says the key is in no backup.
                var key = Path.Combine(host.BackupDirectory, "base", "secret.key");
                Directory.CreateDirectory(Path.GetDirectoryName(key)!);
                await File.WriteAllTextAsync(key, "x");
                Assert.Equal("nowhere", (await ReadAsync(await admin.SendAsync(Change(HttpMethod.Get, "/backups")))).GetProperty("keyState").GetString());
            }
        }
    }
}
