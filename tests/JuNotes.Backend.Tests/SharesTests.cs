using System.Net;
using System.Text;
using System.Text.Json;
using Base.Accounts;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    // Sharing one notebook, read only (milestone 3, slice 3c). Three people: Ada owns the notebooks and shares, Bea is the person the notebook is shared with, Cleo is somebody
    // else (and owns one file and one stray row that Ada's notebook must never make readable). Every path is asked of the one who must not get in as well.
    public class SharesTests
    {
        private static readonly Guid Subject = Guid.Parse("50000000-0000-0000-0000-000000000001");
        private static readonly Guid Shared = Guid.Parse("50000000-0000-0000-0000-000000000011");
        private static readonly Guid Other = Guid.Parse("50000000-0000-0000-0000-000000000012");
        private static readonly Guid PageDrawn = Guid.Parse("50000000-0000-0000-0000-000000000021");
        private static readonly Guid PageScan = Guid.Parse("50000000-0000-0000-0000-000000000022");
        private static readonly Guid PageOther = Guid.Parse("50000000-0000-0000-0000-000000000023");
        private static readonly Guid ObjectPicture = Guid.Parse("50000000-0000-0000-0000-000000000031");
        private static readonly Guid ObjectForeign = Guid.Parse("50000000-0000-0000-0000-000000000032");
        private static readonly Guid ObjectOther = Guid.Parse("50000000-0000-0000-0000-000000000033");
        private static readonly Guid Stroke = Guid.Parse("50000000-0000-0000-0000-000000000041");
        private static readonly Guid FileCover = Guid.Parse("50000000-0000-0000-0000-0000000000d1");
        private static readonly Guid FileScan = Guid.Parse("50000000-0000-0000-0000-0000000000d2");
        private static readonly Guid FilePicture = Guid.Parse("50000000-0000-0000-0000-0000000000d3");
        private static readonly Guid FileLoose = Guid.Parse("50000000-0000-0000-0000-0000000000d4");
        private static readonly Guid FileOtherOnly = Guid.Parse("50000000-0000-0000-0000-0000000000d5");
        private static readonly Guid FileCleo = Guid.Parse("50000000-0000-0000-0000-0000000000d6");

        private static byte[] Jpeg(byte tag) => [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, tag, 0, 0, 1, 0, 1];

        private static Uri Url(string path) => new("/api/web/v1" + path, UriKind.Relative);

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

        private static HttpRequestMessage Send(HttpMethod method, string path, string? json = null)
        {
            var request = new HttpRequestMessage(method, Url(path)) { Content = json is null ? null : Json(json) };
            request.Headers.Add("X-Base", "1");
            request.Headers.Add("Origin", "http://localhost");
            return request;
        }

        private static SyncChange Row(string kind, Guid id, Guid? parent, string json) => new(kind, id, parent, JsonElement.Parse(json), false);

        private static Task<HttpResponseMessage> Get(HttpClient client, string path) => client.SendAsync(Send(HttpMethod.Get, path));

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        private static string[] Ids(JsonElement list) => list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();

        private sealed class Scene : IAsyncDisposable
        {
            private readonly FakeCodex _fake;

            private Scene(AdminHost host, TestDatabase database, FakeCodex fake, (Guid Id, HttpClient Client) ada, (Guid Id, HttpClient Client) bea, (Guid Id, HttpClient Client) cleo)
            {
                Host = host;
                Database = database;
                _fake = fake;
                Ada = ada.Id;
                AdaClient = ada.Client;
                Bea = bea.Id;
                BeaClient = bea.Client;
                Cleo = cleo.Id;
                CleoClient = cleo.Client;
            }

            public AdminHost Host { get; }

            public TestDatabase Database { get; }

            public Guid Ada { get; }

            public HttpClient AdaClient { get; }

            public Guid Bea { get; }

            public HttpClient BeaClient { get; }

            public Guid Cleo { get; }

            public HttpClient CleoClient { get; }

            public SyncService Sync => Host.Services.GetRequiredService<SyncService>();

            public static async Task<Scene> StartAsync()
            {
                var database = await TestDatabase.CreateAsync();
                var fake = new FakeCodex("exit 0");
                var host = new AdminHost(database, fake.Executable);
                var scene = new Scene(host, database, fake, await host.SignedInPersonAsync(), await host.SignedInPersonAsync("member"), await host.SignedInPersonAsync("member"));
                await scene.SeedAsync();
                return scene;
            }

            public async ValueTask DisposeAsync()
            {
                Host.Dispose();
                _fake.Dispose();
                await Database.DisposeAsync();
            }

            public SyncOwner AsAda => new(Ada, "ada", "test");

            public SyncOwner AsCleo => new(Cleo, "cleo", "test");

            // Ada: a folder, a notebook to share (a cover picture, a drawn page with a picture, a stroke and a picture of Cleo's, a scanned page) and one that is not shared (a page with
            // a picture of its own). Cleo: a file, and a page that hangs under Ada's notebook (a parent id that did not exist when she wrote it is accepted: the known gap).
            private async Task SeedAsync()
            {
                var assets = Host.Services.GetRequiredService<AssetService>();
                foreach (var (file, owner, tag) in new[] { (FileCover, Ada, (byte)1), (FileScan, Ada, (byte)2), (FilePicture, Ada, (byte)3), (FileLoose, Ada, (byte)4), (FileOtherOnly, Ada, (byte)5), (FileCleo, Cleo, (byte)6) })
                {
                    Assert.True(await assets.SaveAsync(file, new MemoryStream(Jpeg(tag)), owner, default));
                }

                await Sync.PushAsync(new PushRequest(
                [
                    Row("subject", Subject, null, """{"name":"Mathe"}"""),
                    Row("notebook", Shared, Subject, $$$"""{"title":"Brüche","type":"pages","template":"ruled","createdAt":"2026-11-01T08:00:00Z","cover":{"style":"image","colorA":"#112233","colorB":"#112233","assetID":"{{{FileCover.ToString().ToUpperInvariant()}}}"}}"""),
                    Row("notebook", Other, Subject, """{"title":"Privat","type":"pages","template":"ruled","createdAt":"2026-11-02T08:00:00Z"}"""),
                    Row("page", PageDrawn, Shared, """{"sort":"a","heading":"Seite eins","printedText":"Aufgabe 1","solution":"Lösung"}"""),
                    Row("page", PageScan, Shared, $$$"""{"sort":"b","content":{"type":"scan","assetID":"{{{FileScan}}}"}}"""),
                    Row("page", PageOther, Other, """{"sort":"a","heading":"Geheim"}"""),
                    Row("object", ObjectPicture, PageDrawn, $$"""{"type":"image","assetID":"{{FilePicture}}","x":10,"y":10,"width":100,"height":100}"""),
                    Row("object", ObjectForeign, PageDrawn, $$"""{"type":"image","assetID":"{{FileCleo}}","x":10,"y":120,"width":100,"height":100}"""),
                    Row("object", ObjectOther, PageOther, $$"""{"type":"image","assetID":"{{FileOtherOnly}}","x":10,"y":10,"width":100,"height":100}"""),
                    Row("stroke", Stroke, PageDrawn, """{"tool":"pen","color":"#1d3a8a","width":2.5,"points":[{"x":20,"y":20},{"x":80,"y":90}]}"""),
                ]), AsAda, null, default);
            }

            public async Task<Guid> ShareAsync(Guid notebook, Guid recipient, HttpClient? owner = null)
            {
                var response = await (owner ?? AdaClient).SendAsync(Send(HttpMethod.Post, $"/notebooks/{notebook}/shares", $$"""{"recipientId":"{{recipient}}"}"""));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return Guid.Parse((await ReadAsync(response)).GetProperty("id").GetString()!);
            }

            public async Task<long> CountAsync(string table, string where = "true")
            {
                await using var command = Database.Data.CreateCommand($"select count(*) from {table} where {where}");
                return (long)(await command.ExecuteScalarAsync())!;
            }

            // Every way into the shared notebook that the recipient has, with what it must answer: 200 for a share that is there, 404 for none.
            public static async Task<string> ReadingAsync(HttpClient client)
            {
                var codes = new List<string>();
                foreach (var path in new[] { $"/shared/notebooks/{Shared}", $"/shared/notebooks/{Shared}/pages", $"/shared/pages/{PageDrawn}", $"/shared/pages/{PageScan}", $"/shared/assets/{FileCover}", $"/shared/assets/{FileScan}", $"/shared/assets/{FilePicture}" })
                {
                    codes.Add(((int)(await Get(client, path)).StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                return string.Join(',', codes);
            }
        }

        // ---------- the names list ----------

        [RequiresDatabaseFact]
        public async Task The_names_list_gives_name_and_id_of_the_other_active_people_and_nothing_else()
        {
            await using var scene = await Scene.StartAsync();
            var users = scene.Host.Services.GetRequiredService<UserStore>();
            var blocked = await users.CreateAsync("blocked-" + Guid.NewGuid().ToString("N")[..6], "not-a-hash", "TESTSECRET", confirmed: true, recoveryHashes: [], roles: ["member"], replacePending: false);
            await users.SetStateAsync(blocked.Id, PersonState.Blocked);

            var list = await ReadAsync(await Get(scene.BeaClient, "/people"));

            Assert.Equal(new[] { scene.Ada, scene.Cleo }.Select(id => id.ToString()).Order().ToArray(), Ids(list).Order().ToArray());
            Assert.All(list.GetProperty("items").EnumerateArray(), item => Assert.Equal(["id", "name"], item.EnumerateObject().Select(property => property.Name).ToArray()));
            Assert.DoesNotContain(blocked.Id.ToString(), list.GetRawText(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Get(scene.Host.Browser(), "/people")).StatusCode);
        }

        // ---------- reading through a share ----------

        [RequiresDatabaseFact]
        public async Task Bea_reads_the_shared_notebook_its_pages_its_drawing_and_its_files_and_Cleo_reads_nothing_of_it()
        {
            await using var scene = await Scene.StartAsync();
            Assert.Equal("404,404,404,404,404,404,404", await Scene.ReadingAsync(scene.BeaClient));

            await scene.ShareAsync(Shared, scene.Bea);

            Assert.Equal("200,200,200,200,200,200,200", await Scene.ReadingAsync(scene.BeaClient));
            Assert.Equal("404,404,404,404,404,404,404", await Scene.ReadingAsync(scene.CleoClient));
            // The owner is not a recipient of her own notebook: the shared paths do not open it for her either.
            Assert.Equal("404,404,404,404,404,404,404", await Scene.ReadingAsync(scene.AdaClient));

            var list = await ReadAsync(await Get(scene.BeaClient, "/shared"));
            var entry = Assert.Single(list.GetProperty("items").EnumerateArray());
            Assert.Equal("Brüche", entry.GetProperty("title").GetString());
            Assert.NotEqual(string.Empty, entry.GetProperty("ownerName").GetString());
            var detail = await ReadAsync(await Get(scene.BeaClient, $"/shared/notebooks/{Shared}"));
            Assert.Equal(2, detail.GetProperty("pageCount").GetInt32());
            Assert.Equal([PageDrawn.ToString(), PageScan.ToString()], Ids(await ReadAsync(await Get(scene.BeaClient, $"/shared/notebooks/{Shared}/pages"))));

            // The drawn page: strokes and objects as a picture, the files on it fetched from the shared path (the allow list lets that path through, and only that one).
            var view = await ReadAsync(await Get(scene.BeaClient, $"/shared/pages/{PageDrawn}"));
            var svg = view.GetProperty("svg").GetString()!;
            Assert.Contains($"/api/web/v1/shared/assets/{FilePicture}", svg, StringComparison.Ordinal);
            Assert.DoesNotContain("/api/web/v1/assets/", svg, StringComparison.Ordinal);
            Assert.Equal("Seite eins", view.GetProperty("page").GetProperty("heading").GetString());

            // The files are the right bytes.
            Assert.Equal(Jpeg(1), await (await Get(scene.BeaClient, $"/shared/assets/{FileCover}")).Content.ReadAsByteArrayAsync());
            Assert.Equal(Jpeg(2), await (await Get(scene.BeaClient, $"/shared/assets/{FileScan}")).Content.ReadAsByteArrayAsync());
        }

        [RequiresDatabaseFact]
        public async Task A_share_opens_one_notebook_and_not_another_of_the_same_owner_nor_the_files_that_are_not_in_it()
        {
            await using var scene = await Scene.StartAsync();
            await scene.ShareAsync(Shared, scene.Bea);

            foreach (var path in new[] { $"/shared/notebooks/{Other}", $"/shared/notebooks/{Other}/pages", $"/shared/pages/{PageOther}", $"/shared/assets/{FileOtherOnly}", $"/shared/assets/{FileLoose}", $"/shared/assets/{FileCleo}", $"/shared/notebooks/{Subject}", $"/shared/pages/{Stroke}", $"/shared/assets/{Guid.NewGuid()}" })
            {
                var response = await Get(scene.BeaClient, path);
                Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{path}: {(int)response.StatusCode}");
            }

            // Her own paths know nothing of Ada's notebook, shared or not.
            foreach (var path in new[] { $"/notebooks/{Shared}", $"/notebooks/{Shared}/pages", $"/pages/{PageDrawn}", $"/assets/{FileCover}", $"/assets/{FileLoose}" })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.BeaClient, path)).StatusCode);
            }

            var notebooks = await ReadAsync(await Get(scene.BeaClient, "/notebooks"));
            Assert.Empty(Ids(notebooks));
        }

        [RequiresDatabaseFact]
        public async Task A_file_that_the_shared_notebook_names_but_that_belongs_to_somebody_else_is_not_given()
        {
            await using var scene = await Scene.StartAsync();
            await scene.ShareAsync(Shared, scene.Bea);

            // Ada's page names Cleo's file (any id can be written into a page); the picture is on the page, but the file is not Ada's to show.
            Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.BeaClient, $"/shared/assets/{FileCleo}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Get(scene.CleoClient, $"/assets/{FileCleo}")).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task A_stray_row_of_somebody_else_under_the_shared_notebook_is_not_shown_and_its_files_are_not_given()
        {
            await using var scene = await Scene.StartAsync();
            // Cleo writes a page and an object under ids that Ada's notebook and page already have: the rules refuse foreign parents that exist. So she writes them under ids that
            // do not exist yet, which is accepted (the known gap), and Ada makes those rows into her notebook afterwards.
            var futureNotebook = Guid.NewGuid();
            var strayPage = Guid.NewGuid();
            var strayObject = Guid.NewGuid();
            Assert.Equal(2, (await scene.Sync.PushAsync(new PushRequest(
            [
                Row("page", strayPage, futureNotebook, """{"sort":"z","heading":"Von Cleo"}"""),
                Row("object", strayObject, strayPage, $$"""{"type":"image","assetID":"{{FileCleo}}","x":1,"y":1,"width":10,"height":10}"""),
            ]), scene.AsCleo, null, default)).Accepted);
            await scene.Sync.PushAsync(new PushRequest([Row("notebook", futureNotebook, Subject, """{"title":"Neu","type":"pages","template":"ruled"}""")]), scene.AsAda, null, default);
            await scene.ShareAsync(futureNotebook, scene.Bea);

            Assert.Empty(Ids(await ReadAsync(await Get(scene.BeaClient, $"/shared/notebooks/{futureNotebook}/pages"))));
            Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.BeaClient, $"/shared/pages/{strayPage}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.BeaClient, $"/shared/assets/{FileCleo}")).StatusCode);
        }

        // ---------- taking a share back ----------

        [RequiresDatabaseFact]
        public async Task Taking_a_share_back_works_with_the_next_request_for_every_path_and_the_share_can_be_made_again()
        {
            await using var scene = await Scene.StartAsync();
            var share = await scene.ShareAsync(Shared, scene.Bea);
            Assert.Equal("200,200,200,200,200,200,200", await Scene.ReadingAsync(scene.BeaClient));

            // Bea and Cleo cannot take Ada's share back (the same 404 as for an id that is not there); Ada can.
            Assert.Equal(HttpStatusCode.NotFound, (await scene.BeaClient.SendAsync(Send(HttpMethod.Delete, $"/shares/{share}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.CleoClient.SendAsync(Send(HttpMethod.Delete, $"/shares/{share}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.AdaClient.SendAsync(Send(HttpMethod.Delete, $"/shares/{Guid.NewGuid()}"))).StatusCode);
            Assert.Equal("200,200,200,200,200,200,200", await Scene.ReadingAsync(scene.BeaClient));

            Assert.Equal(HttpStatusCode.NoContent, (await scene.AdaClient.SendAsync(Send(HttpMethod.Delete, $"/shares/{share}"))).StatusCode);

            Assert.Equal("404,404,404,404,404,404,404", await Scene.ReadingAsync(scene.BeaClient));
            Assert.Empty(Ids(await ReadAsync(await Get(scene.BeaClient, "/shared"))));
            Assert.Equal(HttpStatusCode.NotFound, (await scene.AdaClient.SendAsync(Send(HttpMethod.Delete, $"/shares/{share}"))).StatusCode);
            Assert.Equal(1, await scene.CountAsync("shares", "revoked_at is not null"));

            var again = await scene.ShareAsync(Shared, scene.Bea);
            Assert.NotEqual(share, again);
            Assert.Equal("200,200,200,200,200,200,200", await Scene.ReadingAsync(scene.BeaClient));
        }

        [RequiresDatabaseFact]
        public async Task A_notebook_in_the_trash_or_deleted_is_gone_for_the_recipient_and_comes_back_when_it_does()
        {
            await using var scene = await Scene.StartAsync();
            await scene.ShareAsync(Shared, scene.Bea);

            await scene.Sync.PushAsync(new PushRequest([Row("notebook", Shared, Subject, $$$"""{"title":"Brüche","type":"pages","template":"ruled","trashedAt":"2026-11-25T08:00:00Z","cover":{"style":"image","colorA":"#112233","colorB":"#112233","assetID":"{{{FileCover}}}"}}""")]), scene.AsAda, null, default);
            Assert.Equal("404,404,404,404,404,404,404", await Scene.ReadingAsync(scene.BeaClient));
            Assert.Empty(Ids(await ReadAsync(await Get(scene.BeaClient, "/shared"))));

            await scene.Sync.PushAsync(new PushRequest([Row("notebook", Shared, Subject, $$$"""{"title":"Brüche","type":"pages","template":"ruled","cover":{"style":"image","colorA":"#112233","colorB":"#112233","assetID":"{{{FileCover}}}"}}""")]), scene.AsAda, null, default);
            Assert.Equal("200,200,200,200,200,200,200", await Scene.ReadingAsync(scene.BeaClient));

            await scene.Sync.PushAsync(new PushRequest([new SyncChange("notebook", Shared, Subject, null, true)]), scene.AsAda, null, default);
            Assert.Equal("404,404,404,404,404,404,404", await Scene.ReadingAsync(scene.BeaClient));
        }

        [RequiresDatabaseFact]
        public async Task A_page_in_the_trash_and_a_file_only_that_page_used_are_gone_for_the_recipient()
        {
            await using var scene = await Scene.StartAsync();
            await scene.ShareAsync(Shared, scene.Bea);
            Assert.Equal(HttpStatusCode.OK, (await Get(scene.BeaClient, $"/shared/assets/{FileScan}")).StatusCode);

            await scene.Sync.PushAsync(new PushRequest([Row("page", PageScan, Shared, $$$"""{"sort":"b","trashedAt":"2026-11-25T08:00:00Z","content":{"type":"scan","assetID":"{{{FileScan}}}"}}""")]), scene.AsAda, null, default);

            Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.BeaClient, $"/shared/pages/{PageScan}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.BeaClient, $"/shared/assets/{FileScan}")).StatusCode);
            Assert.Equal([PageDrawn.ToString()], Ids(await ReadAsync(await Get(scene.BeaClient, $"/shared/notebooks/{Shared}/pages"))));
        }

        // ---------- who may share ----------

        [RequiresDatabaseFact]
        public async Task Only_the_owner_shares_and_only_a_notebook_that_is_there_with_a_person_who_is_there_and_never_with_oneself()
        {
            await using var scene = await Scene.StartAsync();
            string Body(Guid person) => $$"""{"recipientId":"{{person}}"}""";

            // Bea cannot share Ada's notebook (not with herself, not with Cleo): it is not hers, which looks like a notebook that does not exist.
            Assert.Equal(HttpStatusCode.NotFound, (await scene.BeaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Shared}/shares", Body(scene.Bea)))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.BeaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Shared}/shares", Body(scene.Cleo)))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.BeaClient.SendAsync(Send(HttpMethod.Get, $"/notebooks/{Shared}/shares"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.AdaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Guid.NewGuid()}/shares", Body(scene.Bea)))).StatusCode);
            // A folder, a page and a stroke are not notebooks.
            foreach (var id in new[] { Subject, PageDrawn, Stroke })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await scene.AdaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{id}/shares", Body(scene.Bea)))).StatusCode);
            }

            var self = await scene.AdaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Shared}/shares", Body(scene.Ada)));
            Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
            Assert.Equal("share.self", (await ReadAsync(self)).GetProperty("error").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await scene.AdaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Shared}/shares", Body(Guid.NewGuid())))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await scene.AdaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Shared}/shares", "{}"))).StatusCode);

            var users = scene.Host.Services.GetRequiredService<UserStore>();
            await users.SetStateAsync(scene.Cleo, PersonState.Blocked);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.AdaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Shared}/shares", Body(scene.Cleo)))).StatusCode);
            Assert.Equal(0, await scene.CountAsync("shares"));

            // Twice with the same person is one share (the second answer is the first share), and a notebook in the trash is not shared.
            var first = await scene.ShareAsync(Shared, scene.Bea);
            Assert.Equal(first, await scene.ShareAsync(Shared, scene.Bea));
            Assert.Equal(1, await scene.CountAsync("shares"));
            await scene.Sync.PushAsync(new PushRequest([Row("notebook", Other, Subject, """{"title":"Privat","type":"pages","template":"ruled","trashedAt":"2026-11-25T08:00:00Z"}""")]), scene.AsAda, null, default);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.AdaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Other}/shares", Body(scene.Bea)))).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task The_owner_sees_who_has_the_notebook_and_nobody_else_does()
        {
            await using var scene = await Scene.StartAsync();
            await scene.ShareAsync(Shared, scene.Bea);
            await scene.ShareAsync(Shared, scene.Cleo);

            var list = await ReadAsync(await Get(scene.AdaClient, $"/notebooks/{Shared}/shares"));

            Assert.Equal(2, list.GetProperty("items").GetArrayLength());
            Assert.All(list.GetProperty("items").EnumerateArray(), item => Assert.Equal(["id", "recipientId", "recipientName", "createdAt"], item.EnumerateObject().Select(property => property.Name).ToArray()));
            Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.BeaClient, $"/notebooks/{Shared}/shares")).StatusCode);
        }

        [RequiresDatabaseFact]
        public async Task With_three_people_each_reads_what_was_shared_with_them_and_a_recipient_cannot_pass_it_on()
        {
            await using var scene = await Scene.StartAsync();
            await scene.ShareAsync(Shared, scene.Bea);
            await scene.ShareAsync(Other, scene.Cleo);

            // Bea has the first, Cleo the second, and neither can read the other's.
            Assert.Equal("200,200,200,200,200,200,200", await Scene.ReadingAsync(scene.BeaClient));
            Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.BeaClient, $"/shared/notebooks/{Other}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Get(scene.CleoClient, $"/shared/notebooks/{Other}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Get(scene.CleoClient, $"/shared/assets/{FileOtherOnly}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Get(scene.CleoClient, $"/shared/assets/{FileCover}")).StatusCode);
            Assert.Equal([Other.ToString()], Ids(await ReadAsync(await Get(scene.CleoClient, "/shared"))));
            Assert.Equal([Shared.ToString()], Ids(await ReadAsync(await Get(scene.BeaClient, "/shared"))));

            // Bea cannot share what she may read, nor take away a share of Ada's.
            Assert.Equal(HttpStatusCode.NotFound, (await scene.BeaClient.SendAsync(Send(HttpMethod.Post, $"/notebooks/{Shared}/shares", $$"""{"recipientId":"{{scene.Cleo}}"}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.BeaClient.SendAsync(Send(HttpMethod.Get, $"/notebooks/{Shared}/shares"))).StatusCode);
            Assert.Equal("404,404,404,404,404,404,404", await Scene.ReadingAsync(scene.CleoClient));
        }

        // ---------- what a share does not change ----------

        [RequiresDatabaseFact]
        public async Task The_sync_of_the_recipient_does_not_carry_the_shared_notebook_and_does_not_let_her_change_it()
        {
            await using var scene = await Scene.StartAsync();
            await scene.ShareAsync(Shared, scene.Bea);

            var pull = await scene.Sync.PullAsync(0, 500, scene.Bea, default);
            Assert.Empty(pull.Changes);

            var attempt = await scene.Sync.PushAsync(new PushRequest([Row("notebook", Shared, Subject, """{"title":"Umgetauft","type":"pages","template":"ruled"}"""), Row("page", PageDrawn, Shared, """{"sort":"a","heading":"Überschrieben"}""")]), new SyncOwner(scene.Bea, "bea", "test"), null, default);

            Assert.Equal(0, attempt.Accepted);
            Assert.Equal("Brüche", (await ReadAsync(await Get(scene.BeaClient, $"/shared/notebooks/{Shared}"))).GetProperty("notebook").GetProperty("title").GetString());
            Assert.Equal("Seite eins", (await ReadAsync(await Get(scene.BeaClient, $"/shared/pages/{PageDrawn}"))).GetProperty("page").GetProperty("heading").GetString());
        }

        [RequiresDatabaseFact]
        public async Task A_share_can_only_read_and_the_table_does_not_know_any_other_mode()
        {
            await using var scene = await Scene.StartAsync();
            await using var write = scene.Database.Data.CreateCommand($"insert into shares (id, notebook_id, owner_id, recipient_id, mode) values (gen_random_uuid(), '{Shared}', '{scene.Ada}', '{scene.Bea}', 'write')");

            await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => write.ExecuteNonQueryAsync());
            // And there is no way to write through the shared paths: only GET exists there.
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Post, HttpMethod.Delete, HttpMethod.Patch })
            {
                var response = await scene.BeaClient.SendAsync(Send(method, $"/shared/notebooks/{Shared}", "{}"));
                Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{method}: {(int)response.StatusCode}");
            }
        }

        [RequiresDatabaseFact]
        public async Task The_app_token_and_a_device_token_open_nothing_of_the_shares_and_nobody_without_a_session_does()
        {
            await using var scene = await Scene.StartAsync();
            await scene.ShareAsync(Shared, scene.Bea);
            using var token = scene.Host.Browser();
            token.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);

            foreach (var path in new[] { "/shared", $"/shared/notebooks/{Shared}", $"/shared/pages/{PageDrawn}", $"/shared/assets/{FileCover}", "/people", $"/notebooks/{Shared}/shares" })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await Get(token, path)).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await Get(scene.Host.Browser(), path)).StatusCode);
            }
        }

        // ---------- the log ----------

        [RequiresDatabaseFact]
        public async Task Sharing_and_taking_back_are_written_to_the_security_log_with_the_people_and_the_notebook_id_and_nothing_of_the_content()
        {
            await using var scene = await Scene.StartAsync();
            var share = await scene.ShareAsync(Shared, scene.Bea);
            await scene.ShareAsync(Shared, scene.Bea);
            await scene.AdaClient.SendAsync(Send(HttpMethod.Delete, $"/shares/{share}"));

            var users = scene.Host.Services.GetRequiredService<UserStore>();
            var ada = (await users.FindByIdAsync(scene.Ada))!.Name;
            var bea = (await users.FindByIdAsync(scene.Bea))!.Name;
            var log = await scene.Host.Services.GetRequiredService<AuditStore>().ListAsync(100);

            var made = Assert.Single(log, entry => entry.Event == "share.created");
            Assert.Equal(ada, made.UserName);
            Assert.Equal($"notebook {Shared} with {bea}", made.Detail);
            var taken = Assert.Single(log, entry => entry.Event == "share.revoked");
            Assert.Equal(ada, taken.UserName);
            Assert.Equal($"notebook {Shared} with {bea}", taken.Detail);
            Assert.DoesNotContain(log, entry => (entry.Detail ?? string.Empty).Contains("Brüche", StringComparison.Ordinal));
        }

        // ---------- the page ----------

        [Fact]
        public void The_page_lets_the_shared_path_of_the_files_through_as_the_server_does()
        {
            var text = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/JuNotes.Frontend/wwwroot/pages/util.js")));
            Assert.Contains("(?:\\\\/shared)?\\\\/assets\\\\/", text, StringComparison.Ordinal);
            Assert.Contains("href=\"/api/web/v1/shared/assets/60000000-0000-0000-0000-000000000001\"", JuNotes.Backend.Library.SvgAllowList.Clean(JuNotes.Backend.Library.PageRenderer.Svg("pages", "blank", [], [new JuNotes.Backend.Library.ObjectInfo(Guid.NewGuid(), "image", 0, 0, 10, 10, 0, null, Guid.Parse("60000000-0000-0000-0000-000000000001"), null, 16, 0)], "/api/web/v1/shared/assets/")), StringComparison.Ordinal);
            Assert.DoesNotContain("href", JuNotes.Backend.Library.SvgAllowList.Clean(JuNotes.Backend.Library.PageRenderer.Svg("pages", "blank", [], [new JuNotes.Backend.Library.ObjectInfo(Guid.NewGuid(), "image", 0, 0, 10, 10, 0, null, Guid.Parse("60000000-0000-0000-0000-000000000001"), null, 16, 0)], "/api/web/v1/shared/../folders?x=")), StringComparison.Ordinal);
        }
    }
}
