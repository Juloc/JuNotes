using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JuNotes.Backend.Database;
using JuNotes.Frontend.Admin;

namespace JuNotes.Backend.Tests
{
    // The contract with the iPad app, written down as it is today (/api/v1). The app is frozen and never talks to a test server, so a change here that nobody
    // meant would only show up on the iPad. Where a test says "golden", the exact form of the answer is the point: a later change to it must be a decision, and
    // the test is changed in the same commit that changes the answer.
    public class ApiContractTests
    {
        private const string Token = "sync-token";
        private static readonly Guid Subject = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private static readonly Guid Notebook = Guid.Parse("10000000-0000-0000-0000-000000000002");
        private static readonly Guid Page = Guid.Parse("10000000-0000-0000-0000-000000000003");
        private static readonly Guid Stroke = Guid.Parse("10000000-0000-0000-0000-000000000004");
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1];

        private static Uri Url(string path) => new(path, UriKind.Relative);

        private static async Task<(AdminHost Host, TestDatabase Database)> StartAsync()
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            return (new AdminHost(database, AdminHost.Password, fake.Executable), database);
        }

        private static HttpClient Api(AdminHost host)
        {
            var client = host.Browser();
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
            return client;
        }

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

        private static string Change(string kind, Guid id, Guid? parent, string? payload, bool deleted = false)
            => $$"""{"kind":"{{kind}}","id":"{{id}}","parentId":{{(parent is { } p ? "\"" + p + "\"" : "null")}},"payload":{{payload ?? "null"}},"deleted":{{(deleted ? "true" : "false")}}}""";

        private static string Push(params string[] changes) => "{\"changes\":[" + string.Join(',', changes) + "]}";

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        // The same JSON whatever the order of its keys (the database keeps jsonb in an order of its own).
        private static string Canonical(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(',', element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).Select(property => JsonSerializer.Serialize(property.Name) + ":" + Canonical(property.Value))) + "}",
            JsonValueKind.Array => "[" + string.Join(',', element.EnumerateArray().Select(Canonical)) + "]",
            _ => element.GetRawText(),
        };

        private static string[] Names(JsonElement element) => element.EnumerateObject().Select(property => property.Name).ToArray();

        // ---- /status and the token ----

        [RequiresDatabaseFact]
        public async Task Status_is_open_and_says_who_answers()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var response = await host.Browser().GetAsync(Url("/api/v1/status"));
                var body = await ReadAsync(response);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(["name", "version", "api"], Names(body));
                Assert.Equal("JuNotes", body.GetProperty("name").GetString());
                Assert.Equal(1, body.GetProperty("api").GetInt32());
                Assert.Matches(@"^\d+\.\d+\.\d+$", body.GetProperty("version").GetString()!);
            }
        }

        [RequiresDatabaseFact]
        public async Task Every_other_route_answers_401_without_the_token_and_with_a_wrong_one()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var id = Guid.NewGuid();
                var routes = new (HttpMethod Method, string Path)[]
                {
                    (HttpMethod.Post, "/api/v1/sync/push"),
                    (HttpMethod.Get, "/api/v1/sync/pull"),
                    (HttpMethod.Get, "/api/v1/sync/info"),
                    (HttpMethod.Get, "/api/v1/school/status"),
                    (HttpMethod.Get, "/api/v1/school/calendar"),
                    (HttpMethod.Get, "/api/v1/school/news"),
                    (HttpMethod.Put, $"/api/v1/assets/{id}"),
                    (HttpMethod.Head, $"/api/v1/assets/{id}"),
                    (HttpMethod.Get, $"/api/v1/assets/{id}"),
                    (HttpMethod.Get, "/api/v1/ai/profiles"),
                    (HttpMethod.Post, "/api/v1/ai/quiz"),
                    (HttpMethod.Get, "/api/v1/export/status"),
                    (HttpMethod.Post, "/api/v1/export/paperless?title=x"),
                    (HttpMethod.Get, "/api/v1/nothing-here"),
                };

                foreach (var (method, path) in routes)
                {
                    foreach (var header in new AuthenticationHeaderValue?[] { null, new("Bearer", "wrong"), new("Basic", Token) })
                    {
                        using var client = host.Browser();
                        using var request = new HttpRequestMessage(method, Url(path)) { Content = method == HttpMethod.Get || method == HttpMethod.Head ? null : Json("{}") };
                        request.Headers.Authorization = header;
                        var response = await client.SendAsync(request);

                        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} with {header?.Scheme ?? "no header"}: {(int)response.StatusCode}");
                        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
                    }
                }
            }
        }

        // ---- sync: push ----

        [RequiresDatabaseFact]
        public async Task Push_answers_accepted_and_the_highest_seq_golden()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);
                var first = await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(
                    Change("subject", Subject, null, """{"name":"Mathe"}"""),
                    Change("notebook", Notebook, Subject, """{"title":"Brüche"}"""),
                    Change("page", Page, Notebook, """{"sort":"a"}"""))));

                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                Assert.Equal("application/json; charset=utf-8", first.Content.Headers.ContentType?.ToString());
                Assert.Equal("""{"accepted":3,"seq":3}""", await first.Content.ReadAsStringAsync());

                // The same push again changes nothing, and still counts every change it was sent, and the seq stays. Every change sent burns a number of the sequence
                // even when nothing is stored (the insert asks for one before the conflict is known), and a change that does update asks for a second: seq has gaps.
                var again = await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(
                    Change("subject", Subject, null, """{"name":"Mathe"}"""),
                    Change("notebook", Notebook, Subject, """{"title":"Brüche"}"""))));
                Assert.Equal("""{"accepted":2,"seq":3}""", await again.Content.ReadAsStringAsync());

                var changed = await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(Change("subject", Subject, null, """{"name":"Mathematik"}"""))));
                Assert.Equal("""{"accepted":1,"seq":7}""", await changed.Content.ReadAsStringAsync());
            }
        }

        [RequiresDatabaseFact]
        public async Task Push_refuses_a_malformed_batch_with_400_and_stores_nothing()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);
                var bad = new Dictionary<string, string>
                {
                    ["empty"] = Push(),
                    ["unknown kind"] = Push(Change("note", Guid.NewGuid(), null, "{}")),
                    ["no id"] = Push(Change("subject", Guid.Empty, null, "{}")),
                    ["page without a parent"] = Push(Change("page", Guid.NewGuid(), null, "{}")),
                    ["stroke without a parent"] = Push(Change("stroke", Guid.NewGuid(), null, "{}")),
                    ["grade without a parent"] = Push(Change("grade", Guid.NewGuid(), null, "{}")),
                    ["card without a parent"] = Push(Change("card", Guid.NewGuid(), null, "{}")),
                    ["object without a parent"] = Push(Change("object", Guid.NewGuid(), null, "{}")),
                    ["no payload"] = Push(Change("subject", Guid.NewGuid(), null, null)),
                    ["too many"] = Push(Enumerable.Range(0, 501).Select(_ => Change("subject", Guid.NewGuid(), null, "{}")).ToArray()),
                    ["payload over 1 MB"] = Push(Change("subject", Guid.NewGuid(), null, "{\"text\":\"" + new string('x', 1_000_001) + "\"}")),
                    // One bad change spoils the whole batch: the good one before it is not kept.
                    ["good then bad"] = Push(Change("subject", Guid.NewGuid(), null, "{}"), Change("note", Guid.NewGuid(), null, "{}")),
                };

                foreach (var (name, body) in bad)
                {
                    var response = await api.PostAsync(Url("/api/v1/sync/push"), Json(body));
                    Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {(int)response.StatusCode}");
                    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                }

                Assert.Equal(0, await database.CountAsync("entities"));
            }
        }

        [RequiresDatabaseFact]
        public async Task Push_accepts_exactly_500_changes_and_every_kind()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);
                var response = await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(Enumerable.Range(0, 500).Select(_ => Change("subject", Guid.NewGuid(), null, "{}")).ToArray())));
                Assert.Equal("""{"accepted":500,"seq":500}""", await response.Content.ReadAsStringAsync());

                var kinds = await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(
                    Change("notebook", Notebook, null, "{}"),
                    Change("page", Page, Notebook, "{}"),
                    Change("stroke", Stroke, Page, "{}"),
                    Change("grade", Guid.NewGuid(), Subject, "{}"),
                    Change("card", Guid.NewGuid(), Subject, "{}"),
                    Change("object", Guid.NewGuid(), Page, "{}"))));
                Assert.Equal("""{"accepted":6,"seq":506}""", await kinds.Content.ReadAsStringAsync());
            }
        }

        [RequiresDatabaseFact]
        public async Task A_stroke_is_only_added_or_deleted_and_the_rest_is_last_write_wins()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);
                async Task<string> PushOne(string change) => await (await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(change)))).Content.ReadAsStringAsync();

                // The seq has gaps (see the push test): every change sent burns a number, and an update burns two.
                Assert.Equal("""{"accepted":1,"seq":1}""", await PushOne(Change("stroke", Stroke, Page, """{"points":[1]}""")));
                // Sending an existing stroke with other points does not change it.
                Assert.Equal("""{"accepted":1,"seq":1}""", await PushOne(Change("stroke", Stroke, Page, """{"points":[2]}""")));
                Assert.Equal("""{"accepted":1,"seq":4}""", await PushOne(Change("stroke", Stroke, Page, null, deleted: true)));
                // A deleted stroke does not come back, and deleting it again changes nothing.
                Assert.Equal("""{"accepted":1,"seq":4}""", await PushOne(Change("stroke", Stroke, Page, """{"points":[3]}""")));
                Assert.Equal("""{"accepted":1,"seq":4}""", await PushOne(Change("stroke", Stroke, Page, null, deleted: true)));

                var pull = await ReadAsync(await api.GetAsync(Url("/api/v1/sync/pull")));
                var record = Assert.Single(pull.GetProperty("changes").EnumerateArray());
                Assert.True(record.GetProperty("deleted").GetBoolean());
                Assert.Equal(4, record.GetProperty("seq").GetInt64());

                // Anything else: the last write wins, a deletion removes the payload from view but the row stays.
                await PushOne(Change("notebook", Notebook, Subject, """{"title":"A"}"""));
                await PushOne(Change("notebook", Notebook, Subject, """{"title":"B"}"""));
                Assert.Equal("""{"accepted":1,"seq":11}""", await PushOne(Change("notebook", Notebook, Subject, null, deleted: true)));
                var after = await ReadAsync(await api.GetAsync(Url("/api/v1/sync/pull?since=4")));
                var notebook = Assert.Single(after.GetProperty("changes").EnumerateArray());
                Assert.True(notebook.GetProperty("deleted").GetBoolean());
                Assert.Equal(11, notebook.GetProperty("seq").GetInt64());
            }
        }

        // ---- sync: pull and info ----

        [RequiresDatabaseFact]
        public async Task Pull_answers_changes_in_seq_order_golden()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);

                var empty = await api.GetAsync(Url("/api/v1/sync/pull"));
                Assert.Equal("""{"changes":[],"cursor":0,"more":false}""", await empty.Content.ReadAsStringAsync());

                await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(
                    Change("subject", Subject, null, """{"name":"Mathe"}"""),
                    Change("notebook", Notebook, Subject, """{"title":"Brüche"}"""),
                    Change("page", Page, Notebook, null, deleted: true))));

                var all = await api.GetAsync(Url("/api/v1/sync/pull"));
                Assert.Equal(HttpStatusCode.OK, all.StatusCode);
                Assert.Equal(
                    "{\"changes\":["
                    + $"{{\"kind\":\"subject\",\"id\":\"{Subject}\",\"parentId\":null,\"payload\":{{\"name\":\"Mathe\"}},\"deleted\":false,\"seq\":1}},"
                    + $"{{\"kind\":\"notebook\",\"id\":\"{Notebook}\",\"parentId\":\"{Subject}\",\"payload\":{{\"title\":\"Brüche\"}},\"deleted\":false,\"seq\":2}},"
                    + $"{{\"kind\":\"page\",\"id\":\"{Page}\",\"parentId\":\"{Notebook}\",\"payload\":null,\"deleted\":true,\"seq\":3}}"
                    + "],\"cursor\":3,\"more\":false}",
                    await all.Content.ReadAsStringAsync());

                // Nothing after the cursor: an empty list and the same cursor.
                var none = await api.GetAsync(Url("/api/v1/sync/pull?since=3"));
                Assert.Equal("""{"changes":[],"cursor":3,"more":false}""", await none.Content.ReadAsStringAsync());

                // A page of one: more is true and the cursor is the seq of the last change delivered.
                var one = await ReadAsync(await api.GetAsync(Url("/api/v1/sync/pull?since=0&limit=1")));
                Assert.Equal(1, one.GetProperty("changes").GetArrayLength());
                Assert.Equal(1, one.GetProperty("cursor").GetInt64());
                Assert.True(one.GetProperty("more").GetBoolean());

                var rest = await ReadAsync(await api.GetAsync(Url("/api/v1/sync/pull?since=1&limit=2")));
                Assert.Equal(2, rest.GetProperty("changes").GetArrayLength());
                Assert.Equal(3, rest.GetProperty("cursor").GetInt64());
                Assert.False(rest.GetProperty("more").GetBoolean());

                // A limit of 0 or less is raised to 1, a limit above 500 is lowered to 500 (and never an error).
                Assert.Equal(1, (await ReadAsync(await api.GetAsync(Url("/api/v1/sync/pull?limit=0")))).GetProperty("changes").GetArrayLength());
                Assert.Equal(3, (await ReadAsync(await api.GetAsync(Url("/api/v1/sync/pull?limit=100000")))).GetProperty("changes").GetArrayLength());
            }
        }

        [RequiresDatabaseFact]
        public async Task Pull_pages_through_more_than_500_changes_without_losing_one()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);
                for (var batch = 0; batch < 2; batch++)
                {
                    await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(Enumerable.Range(0, 300).Select(_ => Change("subject", Guid.NewGuid(), null, "{}")).ToArray())));
                }

                long cursor = 0;
                var seen = new HashSet<Guid>();
                var pages = 0;
                while (true)
                {
                    var page = await ReadAsync(await api.GetAsync(Url($"/api/v1/sync/pull?since={cursor}")));
                    foreach (var record in page.GetProperty("changes").EnumerateArray())
                    {
                        Assert.True(seen.Add(record.GetProperty("id").GetGuid()));
                    }

                    cursor = page.GetProperty("cursor").GetInt64();
                    pages++;
                    if (!page.GetProperty("more").GetBoolean())
                    {
                        break;
                    }
                }

                Assert.Equal(600, seen.Count);
                Assert.Equal(600, cursor);
                Assert.Equal(2, pages);
            }
        }

        [RequiresDatabaseFact]
        public async Task Info_answers_the_server_time_and_the_last_backup()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var response = await Api(host).GetAsync(Url("/api/v1/sync/info"));
                var body = await ReadAsync(response);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(["serverTime", "lastBackupAt"], Names(body));
                Assert.True(DateTimeOffset.TryParse(body.GetProperty("serverTime").GetString(), out var time) && Math.Abs((DateTimeOffset.UtcNow - time).TotalMinutes) < 5);
                Assert.Equal(JsonValueKind.Null, body.GetProperty("lastBackupAt").ValueKind);
            }
        }

        // ---- assets ----

        [RequiresDatabaseFact]
        public async Task Assets_are_put_checked_and_read_back_by_id()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);
                var id = Guid.NewGuid();

                Assert.Equal(HttpStatusCode.NotFound, (await api.SendAsync(new HttpRequestMessage(HttpMethod.Head, Url($"/api/v1/assets/{id}")))).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync(Url($"/api/v1/assets/{id}"))).StatusCode);

                var put = await api.PutAsync(Url($"/api/v1/assets/{id}"), new ByteArrayContent(Jpeg));
                Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
                Assert.Empty(await put.Content.ReadAsByteArrayAsync());

                Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(new HttpRequestMessage(HttpMethod.Head, Url($"/api/v1/assets/{id}")))).StatusCode);

                var get = await api.GetAsync(Url($"/api/v1/assets/{id}"));
                Assert.Equal(HttpStatusCode.OK, get.StatusCode);
                Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
                Assert.Equal(Jpeg, await get.Content.ReadAsByteArrayAsync());

                // A byte range is honoured (the app streams large PDFs).
                using var range = new HttpRequestMessage(HttpMethod.Get, Url($"/api/v1/assets/{id}")) { Headers = { Range = new RangeHeaderValue(0, 3) } };
                var partial = await api.SendAsync(range);
                Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
                Assert.Equal(Jpeg[..4], await partial.Content.ReadAsByteArrayAsync());

                // The same id again replaces the file; the answer is the same.
                Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsync(Url($"/api/v1/assets/{id}"), new ByteArrayContent(Jpeg))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task Assets_refuse_what_is_not_a_picture_or_a_pdf_and_what_is_empty_or_too_big()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);
                var id = Guid.NewGuid();

                var html = await api.PutAsync(Url($"/api/v1/assets/{id}"), new ByteArrayContent("<html></html>"u8.ToArray()));
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, html.StatusCode);
                Assert.Equal("application/problem+json", html.Content.Headers.ContentType?.MediaType);

                Assert.Equal(HttpStatusCode.BadRequest, (await api.PutAsync(Url($"/api/v1/assets/{id}"), new ByteArrayContent([]))).StatusCode);

                var big = new byte[(20 * 1024 * 1024) + 1];
                big[0] = 0xFF;
                big[1] = 0xD8;
                big[2] = 0xFF;
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await api.PutAsync(Url($"/api/v1/assets/{id}"), new ByteArrayContent(big))).StatusCode);

                // None of the three left a file behind.
                Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync(Url($"/api/v1/assets/{id}"))).StatusCode);

                // The id in the path must be a guid.
                Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync(Url("/api/v1/assets/not-a-guid"))).StatusCode);
            }
        }

        // ---- school, ai, export ----

        [RequiresDatabaseFact]
        public async Task School_answers_status_calendar_and_news_golden_on_an_empty_server()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);

                var status = await api.GetAsync(Url("/api/v1/school/status"));
                Assert.Equal(HttpStatusCode.OK, status.StatusCode);
                Assert.Equal("""{"timetableConfigured":false,"newsConfigured":false,"lastSuccess":null,"lastError":null,"eventCount":0,"newsCount":0,"skippedEvents":0}""", await status.Content.ReadAsStringAsync());

                Assert.Equal("[]", await api.GetStringAsync(Url("/api/v1/school/calendar")));
                Assert.Equal("[]", await api.GetStringAsync(Url("/api/v1/school/calendar?from=2026-11-01T00:00:00Z&to=2026-11-30T00:00:00Z")));
                Assert.Equal("[]", await api.GetStringAsync(Url("/api/v1/school/news")));
                Assert.Equal("[]", await api.GetStringAsync(Url("/api/v1/school/news?limit=5")));

                // The range must be positive and at most a year.
                foreach (var query in new[] { "from=2026-11-30T00:00:00Z&to=2026-11-01T00:00:00Z", "from=2026-11-01T00:00:00Z&to=2026-11-01T00:00:00Z", "from=2025-01-01T00:00:00Z&to=2026-11-01T00:00:00Z" })
                {
                    var response = await api.GetAsync(Url("/api/v1/school/calendar?" + query));
                    Assert.True(response.StatusCode == HttpStatusCode.BadRequest, query);
                }
            }
        }

        [RequiresDatabaseFact]
        public async Task Ai_lists_its_profiles_and_refuses_an_unknown_task_with_400()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                var api = Api(host);

                var profiles = await ReadAsync(await api.GetAsync(Url("/api/v1/ai/profiles")));
                var list = profiles.EnumerateArray().ToArray();
                Assert.All(list, profile => Assert.Equal(["key", "name", "enabled"], Names(profile)));
                Assert.Equal(["cards", "check", "cover", "grade", "quiz", "test"], list.Select(profile => profile.GetProperty("key").GetString()!).Order());

                var unknown = await api.PostAsync(Url("/api/v1/ai/nonsense"), Json("""{"text":"x"}"""));
                Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
                Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
            }
        }

        [RequiresDatabaseFact]
        public async Task Export_status_says_whether_paperless_is_set_up_golden()
        {
            var (host, database) = await StartAsync();
            await using (database)
            using (host)
            {
                Assert.Equal("""{"paperless":false}""", await Api(host).GetStringAsync(Url("/api/v1/export/status")));
            }
        }

        // ---- the upgrade path ----

        // A database as an older server left it, with rows of every kind in it, is brought up by the host to the newest schema and then answers the app exactly as
        // before: the same rows, the same seq values, the same cursor, and a new push continues the numbering. A later migration (the owner of a row, say) must
        // pass through this test unchanged, and that is what makes "nothing breaks for the existing iPad app" provable.
        [RequiresDatabaseFact]
        public async Task A_database_from_an_older_version_answers_the_app_the_same_after_every_migration()
        {
            await using var database = await TestDatabase.CreateAsync();
            var assembly = typeof(MigrationSet).Assembly;
            var all = MigrationSet.FromAssembly(assembly);
            const int OldVersion = 5;
            Assert.True(all.Latest > OldVersion, "There must be at least one migration after the old version, or this test proves nothing.");

            // The schema as migration 5 left it, filled by SQL with what an older server would have stored.
            var old = all.Migrations.Where(migration => migration.Number <= OldVersion).ToArray();
            var oldSet = MigrationSet.FromNames(old.Select(migration => ($"{migration.Number:0000}_{migration.Name}.sql", migration.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                resource => old.Single(migration => migration.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) == resource).Sql());
            await DatabaseUpgrade.RunAsync(database.Data, oldSet, DatabaseUpgrade.ReadBaseline(assembly), CancellationToken.None);

            var rows = new (string Kind, Guid Id, Guid? Parent, string? Payload, bool Deleted)[]
            {
                ("subject", Subject, null, """{"name":"Mathe","color":"#0a84ff"}""", false),
                ("notebook", Notebook, Subject, """{"title":"Brüche","type":"pages"}""", false),
                ("page", Page, Notebook, """{"sort":"a","heading":"Do"}""", false),
                ("stroke", Stroke, Page, """{"tool":"pen","points":[{"x":1,"y":2}]}""", false),
                ("grade", Guid.Parse("10000000-0000-0000-0000-000000000005"), Subject, """{"value":2.3}""", false),
                ("card", Guid.Parse("10000000-0000-0000-0000-000000000006"), Subject, """{"front":"a","back":"b"}""", false),
                ("page", Guid.Parse("10000000-0000-0000-0000-000000000007"), Notebook, null, true),
            };
            foreach (var row in rows)
            {
                await using var insert = database.Data.CreateCommand("insert into entities (kind, id, parent_id, payload, deleted, seq) values ($1, $2, $3, $4::jsonb, $5, nextval('change_seq'))");
                insert.Parameters.Add(new Npgsql.NpgsqlParameter { Value = row.Kind });
                insert.Parameters.Add(new Npgsql.NpgsqlParameter { Value = row.Id });
                insert.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)row.Parent ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
                insert.Parameters.Add(new Npgsql.NpgsqlParameter { Value = (object?)row.Payload ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
                insert.Parameters.Add(new Npgsql.NpgsqlParameter { Value = row.Deleted });
                await insert.ExecuteNonQueryAsync();
            }

            var asset = Guid.NewGuid();
            var fake = new FakeCodex("exit 0");
            using var host = new AdminHost(database, AdminHost.Password, fake.Executable);
            var api = Api(host);

            // Starting the host ran the rest of the migrations.
            await using (var count = database.Data.CreateCommand("select max(number) from schema_migrations"))
            {
                Assert.Equal(all.Latest, (int)(await count.ExecuteScalarAsync())!);
            }

            var pull = await ReadAsync(await api.GetAsync(Url("/api/v1/sync/pull")));
            var changes = pull.GetProperty("changes").EnumerateArray().ToArray();
            Assert.Equal(rows.Length, changes.Length);
            for (var index = 0; index < rows.Length; index++)
            {
                Assert.Equal(rows[index].Kind, changes[index].GetProperty("kind").GetString());
                Assert.Equal(rows[index].Id, changes[index].GetProperty("id").GetGuid());
                Assert.Equal(rows[index].Parent?.ToString(), changes[index].GetProperty("parentId").GetString());
                Assert.Equal(rows[index].Deleted, changes[index].GetProperty("deleted").GetBoolean());
                Assert.Equal(index + 1, changes[index].GetProperty("seq").GetInt64());
                if (rows[index].Payload is { } payload)
                {
                    using var expected = JsonDocument.Parse(payload);
                    Assert.Equal(Canonical(expected.RootElement), Canonical(changes[index].GetProperty("payload")));
                }
                else
                {
                    Assert.Equal(JsonValueKind.Null, changes[index].GetProperty("payload").ValueKind);
                }
            }

            Assert.Equal(rows.Length, pull.GetProperty("cursor").GetInt64());
            Assert.False(pull.GetProperty("more").GetBoolean());

            // The old token still works, a new push continues the numbering, and assets and the rest still answer.
            var push = await api.PostAsync(Url("/api/v1/sync/push"), Json(Push(Change("subject", Guid.NewGuid(), null, """{"name":"Deutsch"}"""))));
            Assert.Equal($$"""{"accepted":1,"seq":{{rows.Length + 1}}}""", await push.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NoContent, (await api.PutAsync(Url($"/api/v1/assets/{asset}"), new ByteArrayContent(Jpeg))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync(Url("/api/v1/ai/profiles"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync(Url("/api/v1/school/status"))).StatusCode);
        }
    }
}
