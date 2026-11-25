using System.Net;
using System.Text.Json;
using JuNotes.Frontend.Admin;

namespace JuNotes.Backend.Tests
{
    // The doors of the server and which key opens which. There are two keys that must never open each other's door: the instance token of the iPad app
    // (only /api/v1) and the session of a person (everything else under /api, and the pages).
    public class DoorsTests
    {
        private static Uri Url(string path) => new(path, UriKind.Relative);

        private static async Task<(AdminHost Host, TestDatabase Database)> StartAsync()
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            return (new AdminHost(database, fake.Executable), database);
        }

        [RequiresDatabaseFact]
        public async Task The_instance_token_opens_nothing_outside_api_v1()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                using var client = host.Browser();
                client.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);

                // Everything of the base that needs a session, and a path under /api/web/v1 that does not exist yet but will: the token is not a key to any of them.
                foreach (var path in new[] { "/api/people", "/api/settings", "/api/web/v1/library", "/api/web/v1/me", "/api/auth/sessions" })
                {
                    var response = await client.GetAsync(Url(path));
                    Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{path}: {(int)response.StatusCode}");
                }

                foreach (var page in new[] { "/Notes", "/Admin", "/Admin/Codex" })
                {
                    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(Url(page))).StatusCode);
                }
            }
        }

        [RequiresDatabaseFact]
        public async Task A_session_opens_nothing_under_api_v1()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var admin = await host.SignedInAsync();

                foreach (var (method, path) in new[] { (HttpMethod.Get, "/api/v1/sync/pull"), (HttpMethod.Get, "/api/v1/sync/info"), (HttpMethod.Post, "/api/v1/sync/push"), (HttpMethod.Get, "/api/v1/school/status") })
                {
                    using var request = new HttpRequestMessage(method, Url(path)) { Content = method == HttpMethod.Post ? new StringContent("{}") : null };
                    // as the page of the base would send it, with its header and its origin
                    request.Headers.Add("X-Base", "1");
                    request.Headers.Add("Origin", "http://localhost");
                    var response = await admin.SendAsync(request);
                    Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path}: {(int)response.StatusCode}");
                }
            }
        }

        [RequiresDatabaseFact]
        public async Task The_door_is_the_same_door_in_any_spelling_of_its_path()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var admin = await host.SignedInAsync();
                using var anonymous = host.Browser();

                foreach (var path in new[] { "/API/V1/sync/pull", "/Api/v1/Sync/Pull", "/api/V1/school/status" })
                {
                    // Without the token, and with the cookie of a signed-in administrator: neither opens it, whatever the case of the path.
                    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(path))).StatusCode);
                    Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync(Url(path))).StatusCode);
                }

                using var app = host.Browser();
                app.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
                Assert.Equal(HttpStatusCode.OK, (await app.GetAsync(Url("/API/V1/sync/pull"))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_token_door_needs_no_header_of_the_base_and_the_base_door_does()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                // The app sends neither X-Base nor an Origin: its push must still go through, which is why /api/v1 is a door of its own.
                using var app = host.Browser();
                app.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
                var push = await app.PostAsync(Url("/api/v1/sync/push"), new StringContent("""{"changes":[{"kind":"subject","id":"00000000-0000-0000-0000-000000000009","parentId":null,"payload":{"name":"x"},"deleted":false}]}""", System.Text.Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.OK, push.StatusCode);

                // A change under the rest of /api without the header is refused by the guard of the base, even for a signed-in person.
                var admin = await host.SignedInAsync();
                var refused = await admin.PostAsync(Url("/api/auth/logout"), new StringContent("{}"));
                Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_base_answers_health_about_and_says_a_setup_is_needed_on_a_new_server()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                using var client = host.Browser();

                var health = await client.GetAsync(Url("/api/health"));
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);

                using var about = JsonDocument.Parse(await client.GetStringAsync(Url("/api/about")));
                Assert.Equal("notes", about.RootElement.GetProperty("id").GetString());
                Assert.Equal(75, about.RootElement.GetProperty("accentHue").GetInt32());

                using var setup = JsonDocument.Parse(await client.GetStringAsync(Url("/api/setup/status")));
                Assert.Contains(JsonValueKind.True, setup.RootElement.EnumerateObject().Select(property => property.Value.ValueKind));
            }
        }

        [RequiresDatabaseFact]
        public async Task Both_sequences_of_migrations_are_recorded_in_tables_of_their_own_and_the_two_tables_of_the_same_name_do_not_clash()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                _ = host.Browser();

                // Ours: schema_migrations with numbers. The base's: base_migrations with ids and checksums.
                Assert.True(await database.CountAsync("schema_migrations") >= 9);
                Assert.True(await database.CountAsync("base_migrations") >= 4);
                Assert.Equal(0, await database.CountAsync("app_instance_settings"));
                await using var ours = database.Data.CreateCommand("select count(*) from information_schema.columns where table_name = 'schema_migrations' and column_name = 'number'");
                Assert.Equal(1L, (long)(await ours.ExecuteScalarAsync())!);
            }
        }

        // 1: what the service worker may keep, and that private pages say "no-store".
        [Fact]
        public async Task The_service_worker_names_only_the_static_shell_and_its_cache_has_a_new_name()
        {
            var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "../../../../../src/JuNotes.Frontend/wwwroot/sw.js"));

            Assert.Contains("const CACHE = 'notes-v3'", text, StringComparison.Ordinal);
            Assert.Contains("!isShell(url.pathname)) return", text, StringComparison.Ordinal);
            // No private path is in the list, and the old rule (everything outside /api) is gone.
            foreach (var private_ in new[] { "/Notes", "/Admin", "/api" })
            {
                Assert.DoesNotContain("'" + private_, text, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("startsWith('/api/')", text, StringComparison.Ordinal);
        }

        [RequiresDatabaseFact]
        public async Task Private_pages_and_scans_are_never_kept_by_a_browser_or_a_proxy()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var (person, client) = await host.SignedInPersonAsync();
                var sync = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<JuNotes.Backend.Service.SyncService>(host.Services);
                var notebook = Guid.NewGuid();
                var asset = Guid.NewGuid();
                await sync.PushAsync(new JuNotes.Backend.Data.Sync.PushRequest([new("notebook", notebook, null, JsonElement.Parse("""{"title":"x","type":"pages","template":"blank"}"""), false)]), new JuNotes.Backend.Data.Sync.SyncOwner(person, "p", "test"), null, default);
                await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<JuNotes.Backend.Assets.AssetService>(host.Services).SaveAsync(asset, new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1]), person, default);

                foreach (var path in new[] { "/Notes", $"/Notes/{notebook}", $"/Notes/Asset/{asset}", "/Admin", "/Admin/Codex", "/Admin/Backups" })
                {
                    var response = await client.GetAsync(Url(path));
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
                }
            }
        }
    }
}
