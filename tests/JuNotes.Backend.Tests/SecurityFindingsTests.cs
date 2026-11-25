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

        // Review 2, point 4: a push that meets rows nobody owns yet (the first claim is running) takes them, and only for the person the app token stands for.
        [RequiresDatabaseFact]
        public async Task A_push_of_a_row_that_nobody_owns_yet_takes_it_for_the_app_person_and_nobody_else_may()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                var (a, _) = await host.SignedInPersonAsync();
                var (b, _) = await host.SignedInPersonAsync("member");
                var sync = host.Services.GetRequiredService<SyncService>();
                var unowned = Guid.NewGuid();
                var child = Guid.NewGuid();
                await using (var insert = database.Data.CreateCommand($"insert into entities (kind, id, parent_id, payload, deleted, seq, owner_id) values ('subject', '{unowned}', null, '{{\"name\":\"alt\"}}', false, nextval('change_seq'), null)"))
                {
                    await insert.ExecuteNonQueryAsync();
                }

                SyncChange Row(string kind, Guid id, Guid? parent, string name) => new(kind, id, parent, System.Text.Json.JsonElement.Parse($$"""{"name":"{{name}}"}"""), false);

                // A member (who adopts nothing) is turned away: the row is not theirs.
                var refused = await sync.PushAsync(new PushRequest([Row("subject", unowned, null, "geklaut")]), new SyncOwner(b, "bob", "test"), null, default);
                Assert.Equal(0, refused.Accepted);
                Assert.Equal(string.Empty, await ScalarAsync(database, $"select coalesce(owner_id::text, '') from entities where id = '{unowned}'"));

                // The app person takes it, and a child under it as well.
                var taken = await sync.PushAsync(new PushRequest([Row("subject", unowned, null, "neu"), Row("notebook", child, unowned, "Heft")]), new SyncOwner(a, "ada", "app-token", AdoptsUnowned: true), null, default);
                Assert.Equal(2, taken.Accepted);
                Assert.Equal(a.ToString(), await ScalarAsync(database, $"select owner_id from entities where id = '{unowned}'"));
                Assert.Contains("neu", await ScalarAsync(database, $"select payload::text from entities where id = '{unowned}'") ?? string.Empty, StringComparison.Ordinal);
            }
        }

        // Review 2, point 7: the claim goes on when the request that started it gives up.
        [RequiresDatabaseFact]
        public async Task The_claim_finishes_although_the_request_that_started_it_is_given_up()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                using var app = Token(host);
                Assert.Equal(HttpStatusCode.OK, (await app.PostAsync(Url("/api/v1/sync/push"), Push(Guid.NewGuid(), "alt"))).StatusCode);
                // A slow claim: every update of entities takes a moment.
                foreach (var sql in new[] { "create function slow() returns trigger language plpgsql as $$ begin perform pg_sleep(1.0); return new; end $$", "create trigger slow_update before update on entities for each row execute function slow()" })
                {
                    await using var command = database.Data.CreateCommand(sql);
                    await command.ExecuteNonQueryAsync();
                }

                var (_, _) = await host.SignedInPersonAsync();
                var owners = host.Services.GetRequiredService<OwnerDirectory>();
                using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

                var owner = await owners.TokenOwnerAsync(giveUp.Token);

                Assert.Equal(TokenState.Person, owner.State);
                Assert.Equal("0", await ScalarAsync(database, "select count(*) from entities where owner_id is null"));
            }
        }

        // 8, review 2 (1 and 2): wrong tokens are counted and written down once an hour at most, and a right token always gets through, also from an address that has been
        // sending wrong ones (a shared address, or the address of a proxy).
        [RequiresDatabaseFact]
        public async Task A_right_token_gets_through_from_an_address_that_sent_many_wrong_ones_and_the_log_gets_one_line_without_the_token()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                using var wrong = host.Browser();
                wrong.DefaultRequestHeaders.Authorization = new("Bearer", "a-wrong-token-that-must-never-show-up-in-a-log-line");
                for (var attempt = 0; attempt < TokenBrake.FailuresPerMinute * 3; attempt++)
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
                }

                // The same address (the test server shows one) with the right token: through.
                using var right = Token(host);
                Assert.Equal(HttpStatusCode.OK, (await right.GetAsync(Url("/api/v1/sync/pull"))).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await right.PostAsync(Url("/api/v1/sync/push"), Push(Guid.NewGuid(), "durch"))).StatusCode);

                var lines = new List<AuditEntry>();
                for (var wait = 0; wait < 40 && lines.Count == 0; wait++)
                {
                    await Task.Delay(50);
                    lines = (await host.Services.GetRequiredService<AuditStore>().ListAsync(50)).Where(entry => entry.Event == "api.token_refused").ToList();
                }

                // Thirty wrong tokens in a minute, many locked minutes' worth of tries: still one line in the hour.
                var line = Assert.Single(lines);
                Assert.DoesNotContain("wrong-token", line.Detail + line.UserName, StringComparison.Ordinal);
                Assert.DoesNotContain(AdminHost.Token, line.Detail + line.UserName, StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_brake_writes_at_most_one_line_an_hour_for_one_address_and_forgets_what_has_run_out()
        {
            var (host, database, fake) = await StartAsync();
            await using (database)
            using (fake)
            using (host)
            {
                _ = host.Browser();
                var clock = new ManualClock();
                var brake = new TokenBrake(host.Services.GetRequiredService<Base.Web.RateLimit>(), host.Services.GetRequiredService<AuditStore>(), clock);
                var audit = host.Services.GetRequiredService<AuditStore>();

                void Flood(string address)
                {
                    for (var attempt = 0; attempt <= TokenBrake.FailuresPerMinute; attempt++)
                    {
                        brake.Failed(address);
                    }
                }

                async Task<int> LinesAsync()
                {
                    await Task.Delay(300);
                    return (await audit.ListAsync(200)).Count(entry => entry.Event == "api.token_refused");
                }

                Flood("198.51.100.1");
                Assert.True(brake.IsLocked("198.51.100.1"));
                Assert.False(brake.IsLocked("198.51.100.2"));
                Assert.Equal(1, await LinesAsync());

                // Minutes later the same address is locked again but nothing more is written; another address gets its own line.
                clock.Advance(TimeSpan.FromMinutes(5));
                Flood("198.51.100.1");
                Flood("198.51.100.2");
                Assert.Equal(2, await LinesAsync());

                // After the hour the address may be written again, and the old locks are gone.
                clock.Advance(TimeSpan.FromHours(2));
                Assert.False(brake.IsLocked("198.51.100.1"));
                Flood("198.51.100.1");
                Assert.Equal(3, await LinesAsync());
            }
        }

        private sealed class ManualClock : TimeProvider
        {
            private DateTimeOffset _now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

            public void Advance(TimeSpan by) => _now += by;

            public override DateTimeOffset GetUtcNow() => _now;
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
