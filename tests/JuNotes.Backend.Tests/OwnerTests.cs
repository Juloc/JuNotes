using System.Net;
using System.Text;
using System.Text.Json;
using Base.Accounts;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;
using JuNotes.Frontend.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    // Whose is a row. Every rule that keeps one person away from another's notes is a test with two people here; the second person exists only in these tests
    // (their own pages come in milestone 3). "Foreign" always means: the row exists and belongs to somebody else.
    public class OwnerTests
    {
        private static readonly Guid Subject = Guid.Parse("20000000-0000-0000-0000-000000000001");
        private static readonly Guid Notebook = Guid.Parse("20000000-0000-0000-0000-000000000002");
        private static readonly Guid Page = Guid.Parse("20000000-0000-0000-0000-000000000003");
        private static readonly Guid Stroke = Guid.Parse("20000000-0000-0000-0000-000000000004");
        private static readonly Guid Scan = Guid.Parse("20000000-0000-0000-0000-0000000000d1");
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1];
        private static readonly byte[] OtherJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 2, 0, 0, 2, 0, 2];

        private static Uri Url(string path) => new(path, UriKind.Relative);

        private static SyncChange Row(string kind, Guid id, Guid? parent, string json, bool deleted = false)
            => new(kind, id, parent, json is null ? null : JsonElement.Parse(json), deleted);

        private static PushRequest Batch(params SyncChange[] changes) => new(changes);

        private static SyncOwner Owner(Guid id, string name) => new(id, name, "test");

        // Two people with sessions, and the way to write as each of them.
        private sealed record Scene(AdminHost Host, TestDatabase Database, FakeCodex Fake, Guid A, HttpClient AClient, Guid B, HttpClient BClient)
        {
            public SyncService Sync => Host.Services.GetRequiredService<SyncService>();

            public AssetService Assets => Host.Services.GetRequiredService<AssetService>();

            public SyncOwner AsA => Owner(A, "ada");

            public SyncOwner AsB => Owner(B, "bob");

            // The row exactly as it stands in the table, as one string: for "byte for byte unchanged".
            public async Task<string?> SnapshotAsync(string kind, Guid id)
            {
                await using var command = Database.Data.CreateCommand($"select concat_ws('|', kind, id, parent_id, payload::text, deleted, seq, owner_id) from entities where kind = '{kind}' and id = '{id}'");
                return await command.ExecuteScalarAsync() as string;
            }
        }

        private static async Task<Scene> StartAsync()
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            var host = new AdminHost(database, fake.Executable);
            var (a, aClient) = await host.SignedInPersonAsync();
            var (b, bClient) = await host.SignedInPersonAsync("member");
            return new Scene(host, database, fake, a, aClient, b, bClient);
        }

        private static async Task EndAsync(Scene scene)
        {
            scene.Host.Dispose();
            scene.Fake.Dispose();
            await scene.Database.DisposeAsync();
        }

        [RequiresDatabaseFact]
        public async Task A_batch_with_one_forbidden_and_two_allowed_rows_stores_exactly_the_two_and_the_foreign_row_stays_as_it_was()
        {
            var scene = await StartAsync();
            try
            {
                await scene.Sync.PushAsync(Batch(Row("subject", Subject, null, """{"name":"Mathe"}"""), Row("notebook", Notebook, Subject, """{"title":"Brüche"}""")), scene.AsA, null, default);
                var before = await scene.SnapshotAsync("notebook", Notebook);
                var own1 = Guid.NewGuid();
                var own2 = Guid.NewGuid();

                var answer = await scene.Sync.PushAsync(Batch(
                    Row("subject", own1, null, """{"name":"Eins"}"""),
                    Row("notebook", Notebook, Subject, """{"title":"Überschrieben"}"""),
                    Row("subject", own2, null, """{"name":"Zwei"}""")), scene.AsB, null, default);

                Assert.Equal(2, answer.Accepted);
                Assert.Equal(before, await scene.SnapshotAsync("notebook", Notebook));
                Assert.EndsWith("|" + scene.B, await scene.SnapshotAsync("subject", own1), StringComparison.Ordinal);
                Assert.EndsWith("|" + scene.B, await scene.SnapshotAsync("subject", own2), StringComparison.Ordinal);
                Assert.Equal(4L, await scene.Database.CountAsync("entities"));
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_row_may_not_be_hung_under_a_parent_that_is_somebody_elses_and_the_parent_row_stays_as_it_was()
        {
            var scene = await StartAsync();
            try
            {
                await scene.Sync.PushAsync(Batch(Row("subject", Subject, null, """{"name":"Mathe"}"""), Row("notebook", Notebook, Subject, """{"title":"Brüche"}"""), Row("page", Page, Notebook, """{"sort":"a"}""")), scene.AsA, null, default);
                var before = new[] { await scene.SnapshotAsync("subject", Subject), await scene.SnapshotAsync("notebook", Notebook), await scene.SnapshotAsync("page", Page) };
                var own = Guid.NewGuid();
                var stroke = Guid.NewGuid();
                var notebook = Guid.NewGuid();

                var answer = await scene.Sync.PushAsync(Batch(
                    Row("stroke", stroke, Page, """{"points":[{"x":1,"y":1}]}"""),
                    Row("notebook", notebook, Subject, """{"title":"Eingeschmuggelt"}"""),
                    Row("subject", own, null, """{"name":"Mein Fach"}""")), scene.AsB, null, default);

                Assert.Equal(1, answer.Accepted);
                Assert.Null(await scene.SnapshotAsync("stroke", stroke));
                Assert.Null(await scene.SnapshotAsync("notebook", notebook));
                Assert.NotNull(await scene.SnapshotAsync("subject", own));
                Assert.Equal(before, new[] { await scene.SnapshotAsync("subject", Subject), await scene.SnapshotAsync("notebook", Notebook), await scene.SnapshotAsync("page", Page) });
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task Another_person_can_neither_delete_nor_replace_a_row_nor_bring_back_a_stroke_of_somebody_else()
        {
            var scene = await StartAsync();
            try
            {
                await scene.Sync.PushAsync(Batch(Row("page", Page, Notebook, """{"sort":"a"}"""), Row("stroke", Stroke, Page, """{"points":[{"x":1,"y":1}]}""")), scene.AsA, null, default);
                var before = new[] { await scene.SnapshotAsync("page", Page), await scene.SnapshotAsync("stroke", Stroke) };

                var answer = await scene.Sync.PushAsync(Batch(Row("page", Page, Notebook, null!, deleted: true), Row("stroke", Stroke, Page, null!, deleted: true)), scene.AsB, null, default);

                Assert.Equal(0, answer.Accepted);
                Assert.Equal(before, new[] { await scene.SnapshotAsync("page", Page), await scene.SnapshotAsync("stroke", Stroke) });
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_pull_gives_only_the_persons_own_rows_and_the_seq_in_the_answer_tells_nothing_of_the_others()
        {
            var scene = await StartAsync();
            try
            {
                var a1 = Guid.NewGuid();
                var b1 = Guid.NewGuid();
                var answerA = await scene.Sync.PushAsync(Batch(Row("subject", a1, null, """{"name":"A"}""")), scene.AsA, null, default);
                var answerB = await scene.Sync.PushAsync(Batch(Row("subject", b1, null, """{"name":"B"}"""), Row("subject", Guid.NewGuid(), null, """{"name":"B2"}""")), scene.AsB, null, default);
                var answerA2 = await scene.Sync.PushAsync(Batch(Row("subject", Guid.NewGuid(), null, """{"name":"A2"}""")), scene.AsA, null, default);

                var pullA = await scene.Sync.PullAsync(0, 500, scene.A, default);
                var pullB = await scene.Sync.PullAsync(0, 500, scene.B, default);

                Assert.Equal(2, pullA.Changes.Count);
                Assert.Equal(2, pullB.Changes.Count);
                Assert.DoesNotContain(pullA.Changes, change => change.Id == b1);
                Assert.DoesNotContain(pullB.Changes, change => change.Id == a1);
                // The answer of a push carries the highest seq of the person's own rows, not of the table.
                Assert.Equal(1, answerA.Seq);
                Assert.True(answerB.Seq > 1 && answerB.Seq < answerA2.Seq);
                Assert.Equal(pullA.Changes[^1].Seq, answerA2.Seq);
                // Nothing after the cursor: the same cursor back.
                var none = await scene.Sync.PullAsync(pullB.Cursor, 500, scene.B, default);
                Assert.Empty(none.Changes);
                Assert.Equal(pullB.Cursor, none.Cursor);
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_file_of_somebody_else_is_not_replaced_not_read_and_not_even_found_and_the_answer_does_not_say_so()
        {
            var scene = await StartAsync();
            try
            {
                Assert.True(await scene.Assets.SaveAsync(Scan, new MemoryStream(Jpeg), scene.A, default));

                Assert.False(await scene.Assets.SaveAsync(Scan, new MemoryStream(OtherJpeg), scene.B, default));
                Assert.False(await scene.Assets.ExistsAsync(Scan, scene.B, default));
                Assert.Null(await scene.Assets.OpenAsync(Scan, scene.B, default));

                Assert.True(await scene.Assets.ExistsAsync(Scan, scene.A, default));
                await using var stream = (await scene.Assets.OpenAsync(Scan, scene.A, default))!.Value.Stream;
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy);
                Assert.Equal(Jpeg, copy.ToArray());
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task Rows_and_files_that_nobody_owned_go_to_the_first_administrator_when_the_first_account_is_there_and_to_nobody_else()
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            using var host = new AdminHost(database, fake.Executable);
            await using (database)
            using (fake)
            {
                // Before any account everything is as it was: the token of the app writes rows and files that belong to nobody yet.
                using var app = host.Browser();
                app.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
                var push = await app.PostAsync(Url("/api/v1/sync/push"), new StringContent("""{"changes":[{"kind":"subject","id":"20000000-0000-0000-0000-000000000011","parentId":null,"payload":{"name":"Altbestand"},"deleted":false}]}""", Encoding.UTF8, "application/json"));
                Assert.Equal("""{"accepted":1,"seq":1}""", await push.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsync(Url($"/api/v1/assets/{Scan}"), new ByteArrayContent(Jpeg))).StatusCode);
                await using (var unowned = database.Data.CreateCommand("select count(*) from entities where owner_id is null"))
                {
                    Assert.Equal(1L, (long)(await unowned.ExecuteScalarAsync())!);
                }

                // The first account: the next thing that touches the server gives everything to it, a second person gets nothing.
                var (first, firstClient) = await host.SignedInPersonAsync();
                var (second, secondClient) = await host.SignedInPersonAsync("member");
                Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync(Url("/api/web/v1/folders"))).StatusCode);

                await using (var owned = database.Data.CreateCommand("select owner_id from entities where id = '20000000-0000-0000-0000-000000000011'"))
                {
                    Assert.Equal(first, (Guid)(await owned.ExecuteScalarAsync())!);
                }

                await using (var file = database.Data.CreateCommand($"select owner_id from assets where id = '{Scan}'"))
                {
                    Assert.Equal(first, (Guid)(await file.ExecuteScalarAsync())!);
                }

                Assert.Empty((await host.Services.GetRequiredService<SyncService>().PullAsync(0, 500, second, default)).Changes);
                Assert.Equal(HttpStatusCode.NotFound, (await secondClient.GetAsync(Url($"/api/web/v1/assets/{Scan}"))).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync(Url($"/api/web/v1/assets/{Scan}"))).StatusCode);

                // And from now on the token of the app is the first administrator, also when more administrators come.
                var (_, _) = await host.SignedInPersonAsync();
                var pull = await app.GetStringAsync(Url("/api/v1/sync/pull"));
                Assert.Contains("Altbestand", pull, StringComparison.Ordinal);
                await app.PostAsync(Url("/api/v1/sync/push"), new StringContent("""{"changes":[{"kind":"subject","id":"20000000-0000-0000-0000-000000000012","parentId":null,"payload":{"name":"Neu"},"deleted":false}]}""", Encoding.UTF8, "application/json"));
                await using var newer = database.Data.CreateCommand("select owner_id from entities where id = '20000000-0000-0000-0000-000000000012'");
                Assert.Equal(first, (Guid)(await newer.ExecuteScalarAsync())!);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_broken_rule_through_the_door_of_the_app_is_left_out_without_a_word_and_written_down_once_a_minute_without_content()
        {
            var scene = await StartAsync();
            try
            {
                // Make the first person the one the token stands for, then give the second person a row and let the token try to overwrite it.
                Assert.Equal(HttpStatusCode.OK, (await scene.AClient.GetAsync(Url("/api/web/v1/folders"))).StatusCode);
                var foreign = Guid.NewGuid();
                await scene.Sync.PushAsync(Batch(Row("subject", foreign, null, """{"name":"Geheim"}""")), scene.AsB, null, default);
                var before = await scene.SnapshotAsync("subject", foreign);

                using var app = scene.Host.Browser();
                app.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
                var own = Guid.NewGuid();
                async Task<string> PushAsync() => await (await app.PostAsync(Url("/api/v1/sync/push"), new StringContent(
                    $$"""{"changes":[{"kind":"subject","id":"{{foreign}}","parentId":null,"payload":{"name":"Zugriff"},"deleted":false},{"kind":"subject","id":"{{Guid.NewGuid()}}","parentId":null,"payload":{"name":"{{own}}"},"deleted":false}]}""", Encoding.UTF8, "application/json"))).Content.ReadAsStringAsync();

                var first = await PushAsync();
                var second = await PushAsync();

                using var document = JsonDocument.Parse(first);
                Assert.Equal(["accepted", "seq"], document.RootElement.EnumerateObject().Select(property => property.Name));
                Assert.Equal(1, document.RootElement.GetProperty("accepted").GetInt32());
                Assert.DoesNotContain(foreign.ToString(), first + second, StringComparison.Ordinal);
                Assert.Equal(before, await scene.SnapshotAsync("subject", foreign));

                var lines = new List<AuditEntry>();
                for (var wait = 0; wait < 40 && lines.Count == 0; wait++)
                {
                    await Task.Delay(50);
                    lines = (await scene.Host.Services.GetRequiredService<AuditStore>().ListAsync(50)).Where(entry => entry.Event == SecurityRules.ForeignRow).ToList();
                }

                var line = Assert.Single(lines);
                Assert.Contains("device app-token", line.Detail, StringComparison.Ordinal);
                Assert.DoesNotContain("Geheim", line.Detail + line.UserName, StringComparison.Ordinal);
                Assert.DoesNotContain("Zugriff", line.Detail + line.UserName, StringComparison.Ordinal);
                Assert.DoesNotContain(foreign.ToString(), line.Detail ?? string.Empty, StringComparison.Ordinal);
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_notebooks_of_the_web_are_only_the_persons_own_and_a_foreign_notebook_or_scan_is_a_404()
        {
            var scene = await StartAsync();
            try
            {
                await scene.Sync.PushAsync(Batch(Row("subject", Subject, null, """{"name":"Mathe","color":"#0a84ff","symbol":"function","sort":0}"""),
                    Row("notebook", Notebook, Subject, """{"title":"Nur fuer A","type":"pages","template":"grid","createdAt":"2026-11-10T08:00:00Z"}"""),
                    Row("page", Page, Notebook, """{"sort":"b"}""")), scene.AsA, null, default);
                await scene.Assets.SaveAsync(Scan, new MemoryStream(Jpeg), scene.A, default);

                var aList = await scene.AClient.GetStringAsync(Url("/api/web/v1/notebooks"));
                var bList = await scene.BClient.GetStringAsync(Url("/api/web/v1/notebooks"));

                Assert.Contains("Nur fuer A", aList, StringComparison.Ordinal);
                Assert.DoesNotContain("Nur fuer A", bList, StringComparison.Ordinal);
                Assert.Equal(HttpStatusCode.OK, (await scene.AClient.GetAsync(Url($"/api/web/v1/notebooks/{Notebook}"))).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await scene.BClient.GetAsync(Url($"/api/web/v1/notebooks/{Notebook}"))).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await scene.BClient.GetAsync(Url($"/api/web/v1/assets/{Scan}"))).StatusCode);
            }
            finally
            {
                await EndAsync(scene);
            }
        }
    }
}
