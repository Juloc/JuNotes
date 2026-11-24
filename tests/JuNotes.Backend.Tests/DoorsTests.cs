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
                client.DefaultRequestHeaders.Authorization = new("Bearer", "sync-token");

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
        public async Task The_token_door_needs_no_header_of_the_base_and_the_base_door_does()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                // The app sends neither X-Base nor an Origin: its push must still go through, which is why /api/v1 is a door of its own.
                using var app = host.Browser();
                app.DefaultRequestHeaders.Authorization = new("Bearer", "sync-token");
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
        public async Task The_old_and_the_new_tables_of_a_migration_do_not_clash_and_both_sequences_are_recorded()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                _ = host.Browser();

                Assert.True(await database.CountAsync("app_migrations") >= 8);
                Assert.True(await database.CountAsync("schema_migrations") >= 4);
                Assert.Equal(0, await database.CountAsync("app_instance_settings"));
                await using var legacy = database.Data.CreateCommand("select count(*) from information_schema.columns where table_name = 'schema_migrations' and column_name = 'number'");
                Assert.Equal(0L, (long)(await legacy.ExecuteScalarAsync())!);
            }
        }
    }
}
