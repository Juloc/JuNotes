using System.Net;
using System.Text;
using Base.Accounts;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;
using JuNotes.Frontend.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    // What the independent security review of milestone 1 found, one test each (the findings are numbered as in STATE.md, "Security review").
    public class SecurityFindingsTests
    {
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1];
        private static readonly byte[] OtherJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 2, 0, 0, 2, 0, 2];

        private static Uri Url(string path) => new(path, UriKind.Relative);

        private static async Task<(AdminHost Host, TestDatabase Database, FakeCodex Fake)> StartAsync()
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            return (new AdminHost(database, fake.Executable), database, fake);
        }

        private static HttpClient Token(AdminHost host)
        {
            var client = host.Browser();
            client.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
            return client;
        }

        private static StringContent Push(Guid id, string name) => new($$"""{"changes":[{"kind":"subject","id":"{{id}}","parentId":null,"payload":{"name":"{{name}}"},"deleted":false}]}""", Encoding.UTF8, "application/json");

        private static async Task<string?> ScalarAsync(TestDatabase database, string sql)
        {
            await using var command = database.Data.CreateCommand(sql);
            return (await command.ExecuteScalarAsync())?.ToString();
        }

        // 2: the person behind the token is written down once and never chosen again.
        [RequiresDatabaseFact]
        public async Task The_person_behind_the_token_is_written_down_once_and_never_swapped_for_somebody_else()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                var (first, _) = await host.SignedInPersonAsync();
                var (second, _) = await host.SignedInPersonAsync();
                using var app = Token(host);
                var one = Guid.NewGuid();
                Assert.Equal(HttpStatusCode.OK, (await app.PostAsync(Url("/api/v1/sync/push"), Push(one, "eins"))).StatusCode);
                Assert.Equal(first.ToString(), await ScalarAsync(database, "select value from app_instance_settings where key = 'token_owner'"));

                // The second administrator takes the role from the first: the token still writes as the first person (who is active), never as the second.
                var users = host.Services.GetRequiredService<UserStore>();
                await users.SetRolesAsync(first, ["member"]);
                var two = Guid.NewGuid();
                Assert.Equal(HttpStatusCode.OK, (await app.PostAsync(Url("/api/v1/sync/push"), Push(two, "zwei"))).StatusCode);
                Assert.Equal(first.ToString(), await ScalarAsync(database, $"select owner_id from entities where id = '{two}'"));

                // The first person is blocked: the token opens nothing, and the second person is not chosen in their place.
                await users.SetStateAsync(first, PersonState.Blocked);
                Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await app.PostAsync(Url("/api/v1/sync/push"), Push(Guid.NewGuid(), "drei"))).StatusCode);
                Assert.Equal("0", await ScalarAsync(database, $"select count(*) from entities where owner_id = '{second}'"));

                // Deleted: the same.
                await users.SetStateAsync(first, PersonState.Active);
                await users.DeleteAsync(first);
                Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
                Assert.Equal(first.ToString(), await ScalarAsync(database, "select value from app_instance_settings where key = 'token_owner'"));
            }
        }

        // 4: "nobody owns" works only while no account exists at all.
        [RequiresDatabaseFact]
        public async Task The_token_works_without_an_owner_only_while_no_account_exists()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                using var app = Token(host);
                Assert.Equal(HttpStatusCode.OK, (await app.PostAsync(Url("/api/v1/sync/push"), Push(Guid.NewGuid(), "alt"))).StatusCode);

                // An account exists, but no administrator: the token is not "free" now, it opens nothing.
                _ = await host.SignedInPersonAsync("member");
                Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await app.PostAsync(Url("/api/v1/sync/push"), Push(Guid.NewGuid(), "neu"))).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync(Url($"/api/v1/assets/{Guid.NewGuid()}"))).StatusCode);
            }
        }

        // 3: a row of another kind under the same id.
        [RequiresDatabaseFact]
        public async Task Nobody_puts_a_row_of_any_kind_under_an_id_that_somebody_else_uses_and_the_owners_children_stay_possible()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                var (a, _) = await host.SignedInPersonAsync();
                var (b, _) = await host.SignedInPersonAsync("member");
                var sync = host.Services.GetRequiredService<SyncService>();
                var asA = new SyncOwner(a, "ada", "test");
                var asB = new SyncOwner(b, "bob", "test");
                var page = Guid.NewGuid();
                var notebook = Guid.NewGuid();
                SyncChange Row(string kind, Guid id, Guid? parent, string json) => new(kind, id, parent, System.Text.Json.JsonElement.Parse(json), false);
                await sync.PushAsync(new PushRequest([Row("page", page, notebook, """{"sort":"a"}""")]), asA, null, default);

                // B tries a row of another kind under the same id: refused. A's strokes under the page stay possible.
                var answer = await sync.PushAsync(new PushRequest([Row("subject", page, null, """{"name":"Eingeschmuggelt"}""")]), asB, null, default);
                Assert.Equal(0, answer.Accepted);
                var children = await sync.PushAsync(new PushRequest([Row("stroke", Guid.NewGuid(), page, """{"points":[]}""")]), asA, null, default);
                Assert.Equal(1, children.Accepted);
                Assert.Equal("0", await ScalarAsync(database, $"select count(*) from entities where owner_id = '{b}'"));
            }
        }

        // 5: a failing claim does not turn into 500 for every request, and it is not tried again for every request.
        [RequiresDatabaseFact]
        public async Task A_claim_that_fails_is_a_503_with_a_sentence_and_not_a_500_and_nothing_is_written_for_unowned_rows()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                using var app = Token(host);
                Assert.Equal(HttpStatusCode.OK, (await app.PostAsync(Url("/api/v1/sync/push"), Push(Guid.NewGuid(), "alt"))).StatusCode);
                // The giving of old rows fails: a trigger that refuses every update of entities.
                await using (var trap = database.Data.CreateCommand("create function refuse() returns trigger language plpgsql as $$ begin raise exception 'no'; end $$"))
                {
                    await trap.ExecuteNonQueryAsync();
                }

                await using (var trigger = database.Data.CreateCommand("create trigger refuse_update before update on entities for each row execute function refuse()"))
                {
                    await trigger.ExecuteNonQueryAsync();
                }

                var (_, admin) = await host.SignedInPersonAsync();
                var pull = await app.GetAsync(Url("/api/v1/sync/pull"));
                var again = await app.PostAsync(Url("/api/v1/sync/push"), Push(Guid.NewGuid(), "neu"));

                Assert.Equal(HttpStatusCode.ServiceUnavailable, pull.StatusCode);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, again.StatusCode);
                Assert.Equal("1", await ScalarAsync(database, "select count(*) from entities"));

                // The administration says so, in words that tell what to do.
                using var overview = System.Text.Json.JsonDocument.Parse(await admin.GetStringAsync(Url("/api/web/v1/admin/overview")));
                Assert.True(overview.RootElement.GetProperty("claimFailed").GetBoolean());
            }
        }

        // 8: the lock hits one address and no other (the server must see the address of the caller, not of a proxy; that is checked before the roll-out).
        [RequiresDatabaseFact]
        public async Task A_lock_hits_the_one_address_and_no_other()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                _ = host.Browser();
                var brake = host.Services.GetRequiredService<TokenBrake>();

                for (var attempt = 0; attempt <= TokenBrake.FailuresPerMinute; attempt++)
                {
                    brake.Failed("203.0.113.7");
                }

                Assert.True(brake.IsLocked("203.0.113.7"));
                Assert.False(brake.IsLocked("203.0.113.8"));
                Assert.False(brake.IsLocked("2001:db8::1"));
                brake.Failed("203.0.113.8");
                Assert.False(brake.IsLocked("203.0.113.8"));
            }
        }

        // 7: the file is checked first, the same answer for everybody; the id is claimed before the file is put in place; two at once cannot swap content and owner.
        [RequiresDatabaseFact]
        public async Task A_file_for_an_id_of_somebody_else_gets_the_same_answers_as_one_of_your_own_and_two_at_once_cannot_swap_owner_and_content()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                var (a, _) = await host.SignedInPersonAsync();
                var (b, _) = await host.SignedInPersonAsync("member");
                var assets = host.Services.GetRequiredService<AssetService>();
                var mine = Guid.NewGuid();
                var theirs = Guid.NewGuid();
                Assert.True(await assets.SaveAsync(theirs, new MemoryStream(Jpeg), a, default));

                // A wrong file for a foreign id is refused exactly as for an own id (so the answer does not tell whose the id is); a right file is "taken" without a trace.
                var foreign = await Assert.ThrowsAsync<AssetException>(() => assets.SaveAsync(theirs, new MemoryStream("<html>"u8.ToArray()), b, default));
                var own = await Assert.ThrowsAsync<AssetException>(() => assets.SaveAsync(mine, new MemoryStream("<html>"u8.ToArray()), b, default));
                Assert.Equal(own.StatusCode, foreign.StatusCode);
                Assert.False(await assets.SaveAsync(theirs, new MemoryStream(OtherJpeg), b, default));

                // Eight at once for one new id, alternating between the two people: exactly one wins, and the file is the winner's.
                var race = Guid.NewGuid();
                var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() => assets.SaveAsync(race, new MemoryStream(index % 2 == 0 ? Jpeg : OtherJpeg), index % 2 == 0 ? a : b, default))));
                var winner = Guid.Parse((await ScalarAsync(database, $"select owner_id from assets where id = '{race}'"))!);
                Assert.Equal(4, results.Where((result, index) => result && (index % 2 == 0) == (winner == a)).Count());
                Assert.DoesNotContain(results.Select((result, index) => (result, index)), pair => pair.result && (pair.index % 2 == 0) != (winner == a));
                await using var stream = (await assets.OpenAsync(race, winner, default))!.Value.Stream;
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy);
                Assert.Equal(winner == a ? Jpeg : OtherJpeg, copy.ToArray());
            }
        }

        // 8: wrong tokens are slowed down, written down once, and never echoed.
        [RequiresDatabaseFact]
        public async Task Too_many_wrong_tokens_lock_the_address_even_for_the_right_one_and_the_security_log_gets_one_line_without_the_token()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                using var wrong = host.Browser();
                wrong.DefaultRequestHeaders.Authorization = new("Bearer", "a-wrong-token-that-must-never-show-up-in-a-log-line");
                for (var attempt = 0; attempt < TokenBrake.FailuresPerMinute + 3; attempt++)
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
                }

                using var right = Token(host);
                Assert.Equal(HttpStatusCode.Unauthorized, (await right.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);

                var lines = new List<AuditEntry>();
                for (var wait = 0; wait < 40 && lines.Count == 0; wait++)
                {
                    await Task.Delay(50);
                    lines = (await host.Services.GetRequiredService<AuditStore>().ListAsync(50)).Where(entry => entry.Event == "api.token_refused").ToList();
                }

                var line = Assert.Single(lines);
                Assert.DoesNotContain("wrong-token", line.Detail + line.UserName, StringComparison.Ordinal);
                Assert.DoesNotContain(AdminHost.Token, line.Detail + line.UserName, StringComparison.Ordinal);
            }
        }

        // small: the open exception is the exact path.
        [RequiresDatabaseFact]
        public async Task Only_the_exact_status_path_is_open()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                using var client = host.Browser();

                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url("/api/v1/status"))).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url("/API/V1/STATUS"))).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url("/api/v1/status/more"))).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url("/api/v1/statusx"))).StatusCode);
            }
        }
    }
}
