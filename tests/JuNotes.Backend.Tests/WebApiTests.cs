using System.Net;
using System.Text.Json;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;
using JuNotes.Frontend.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    // The reading paths of the web app (/api/web/v1): every one answers only what belongs to the person who asks, and everything else exactly as if it did not exist. Person A has
    // the notes, person B has none; B asks for each of A's things by its id and gets what a stranger gets.
    public class WebApiTests
    {
        private static readonly Guid Folder = Guid.Parse("50000000-0000-0000-0000-000000000001");
        private static readonly Guid Notebook = Guid.Parse("50000000-0000-0000-0000-000000000002");
        private static readonly Guid Page = Guid.Parse("50000000-0000-0000-0000-000000000003");
        private static readonly Guid ScanPage = Guid.Parse("50000000-0000-0000-0000-000000000004");
        private static readonly Guid Scan = Guid.Parse("50000000-0000-0000-0000-0000000000d1");
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1];

        private static Uri Url(string path) => new(path, UriKind.Relative);

        private static SyncChange Row(string kind, Guid id, Guid? parent, string json) => new(kind, id, parent, JsonElement.Parse(json), false);

        private sealed record Scene(AdminHost Host, TestDatabase Database, FakeCodex Fake, Guid A, HttpClient AClient, Guid B, HttpClient BClient);

        private static async Task<Scene> StartAsync()
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            var host = new AdminHost(database, fake.Executable);
            var (a, aClient) = await host.SignedInPersonAsync();
            var (b, bClient) = await host.SignedInPersonAsync("member");
            var sync = host.Services.GetRequiredService<SyncService>();
            await sync.PushAsync(new PushRequest(
            [
                Row("subject", Folder, null, """{"name":"Mathe","color":"#0a84ff","symbol":"function","sort":0}"""),
                Row("notebook", Notebook, Folder, """{"title":"Brüche","type":"pages","template":"grid","createdAt":"2026-11-10T08:00:00Z"}"""),
                Row("page", Page, Notebook, """{"sort":"b","heading":"Do","printedText":"1/2 + 1/3","solution":"5/6","additions":["Kürzen"]}"""),
                Row("page", ScanPage, Notebook, $$$"""{"sort":"c","content":{"type":"scan","assetID":"{{{Scan}}}"}}"""),
                Row("stroke", Guid.NewGuid(), Page, """{"tool":"pen","color":"#1d3a8a","width":2.5,"points":[{"x":10,"y":20},{"x":30,"y":40}]}"""),
                Row("grade", Guid.NewGuid(), Folder, """{"value":2.3,"category":"written","date":"2026-11-12T08:00:00Z","title":"Arbeit 1"}"""),
                Row("card", Guid.NewGuid(), Folder, """{"front":"Vorderseite","back":"Rückseite"}"""),
            ]), new SyncOwner(a, "ada", "test"), null, default);
            await host.Services.GetRequiredService<AssetService>().SaveAsync(Scan, new MemoryStream(Jpeg), a, default);
            return new Scene(host, database, fake, a, aClient, b, bClient);
        }

        private static async Task EndAsync(Scene scene)
        {
            scene.Host.Dispose();
            scene.Fake.Dispose();
            await scene.Database.DisposeAsync();
        }

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        [RequiresDatabaseFact]
        public async Task Nothing_here_opens_without_a_session_and_the_app_token_is_not_a_key()
        {
            var scene = await StartAsync();
            try
            {
                using var anonymous = scene.Host.Browser();
                using var app = scene.Host.Browser();
                app.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
                var paths = new[] { "/folders", "/notebooks", $"/notebooks/{Notebook}", $"/notebooks/{Notebook}/pages", $"/pages/{Page}", $"/assets/{Scan}", "/grades", "/cards", "/search?q=x" };

                foreach (var path in paths)
                {
                    Assert.True((await anonymous.GetAsync(Url("/api/web/v1" + path))).StatusCode == HttpStatusCode.Unauthorized, path);
                    Assert.True((await app.GetAsync(Url("/api/web/v1" + path))).StatusCode == HttpStatusCode.Unauthorized, path);
                }
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_owner_reads_the_library_and_a_stranger_gets_empty_lists_and_404s()
        {
            var scene = await StartAsync();
            try
            {
                var folders = await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/folders")));
                Assert.Equal("Mathe", Assert.Single(folders.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
                var notebooks = await JsonAsync(await scene.AClient.GetAsync(Url($"/api/web/v1/notebooks?folder={Folder}")));
                Assert.Equal("Brüche", Assert.Single(notebooks.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
                var detail = await JsonAsync(await scene.AClient.GetAsync(Url($"/api/web/v1/notebooks/{Notebook}")));
                Assert.Equal(2, detail.GetProperty("pageCount").GetInt32());
                var pages = await JsonAsync(await scene.AClient.GetAsync(Url($"/api/web/v1/notebooks/{Notebook}/pages")));
                Assert.Equal(2, pages.GetProperty("items").GetArrayLength());

                // The stranger: empty lists, and a 404 for each thing by its id (the same answer as for an id that does not exist).
                Assert.Empty((await JsonAsync(await scene.BClient.GetAsync(Url("/api/web/v1/folders")))).GetProperty("items").EnumerateArray());
                Assert.Empty((await JsonAsync(await scene.BClient.GetAsync(Url("/api/web/v1/notebooks")))).GetProperty("items").EnumerateArray());
                Assert.Empty((await JsonAsync(await scene.BClient.GetAsync(Url($"/api/web/v1/notebooks?folder={Folder}")))).GetProperty("items").EnumerateArray());
                foreach (var path in new[] { $"/notebooks/{Notebook}", $"/notebooks/{Notebook}/pages", $"/pages/{Page}", $"/pages/{ScanPage}", $"/assets/{Scan}" })
                {
                    var theirs = await scene.BClient.GetAsync(Url("/api/web/v1" + path));
                    var nothing = await scene.BClient.GetAsync(Url("/api/web/v1" + path.Replace(Notebook.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal).Replace(Page.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)
                        .Replace(ScanPage.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal).Replace(Scan.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)));
                    Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
                    Assert.Equal(await nothing.Content.ReadAsStringAsync(), await theirs.Content.ReadAsStringAsync());
                }
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_page_comes_as_one_picture_with_the_asset_path_of_the_web_api_and_a_scan_is_a_file_of_its_own()
        {
            var scene = await StartAsync();
            try
            {
                var view = await JsonAsync(await scene.AClient.GetAsync(Url($"/api/web/v1/pages/{Page}")));
                Assert.Equal("5/6", view.GetProperty("page").GetProperty("solution").GetString());
                Assert.StartsWith("<svg", view.GetProperty("svg").GetString(), StringComparison.Ordinal);
                Assert.Contains("<path", view.GetProperty("svg").GetString(), StringComparison.Ordinal);
                var scan = await JsonAsync(await scene.AClient.GetAsync(Url($"/api/web/v1/pages/{ScanPage}")));
                Assert.Equal("scan", scan.GetProperty("page").GetProperty("contentType").GetString());
                Assert.Equal(Scan, scan.GetProperty("page").GetProperty("assetId").GetGuid());

                var file = await scene.AClient.GetAsync(Url($"/api/web/v1/assets/{Scan}"));
                Assert.Equal(HttpStatusCode.OK, file.StatusCode);
                Assert.Equal("image/jpeg", file.Content.Headers.ContentType?.MediaType);
                Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
                Assert.Contains("no-store", file.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
                Assert.Equal(Jpeg, await file.Content.ReadAsByteArrayAsync());
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task Marks_and_cards_are_listed_for_their_owner_only()
        {
            var scene = await StartAsync();
            try
            {
                var grades = await JsonAsync(await scene.AClient.GetAsync(Url($"/api/web/v1/grades?folder={Folder}")));
                var grade = Assert.Single(grades.GetProperty("items").EnumerateArray());
                Assert.Equal(2.3, grade.GetProperty("value").GetDouble());
                Assert.Equal("written", grade.GetProperty("category").GetString());
                var cards = await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/cards")));
                Assert.Equal("Vorderseite", Assert.Single(cards.GetProperty("items").EnumerateArray()).GetProperty("front").GetString());

                Assert.Empty((await JsonAsync(await scene.BClient.GetAsync(Url("/api/web/v1/grades")))).GetProperty("items").EnumerateArray());
                Assert.Empty((await JsonAsync(await scene.BClient.GetAsync(Url($"/api/web/v1/grades?folder={Folder}")))).GetProperty("items").EnumerateArray());
                Assert.Empty((await JsonAsync(await scene.BClient.GetAsync(Url("/api/web/v1/cards")))).GetProperty("items").EnumerateArray());
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_search_finds_names_of_the_persons_own_folders_and_notebooks_and_nothing_of_anybody_elses()
        {
            var scene = await StartAsync();
            try
            {
                var mine = await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/search?q=br%C3%BC")));
                Assert.Equal("Brüche", Assert.Single(mine.GetProperty("notebooks").EnumerateArray()).GetProperty("title").GetString());
                var folder = await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/search?q=MATHE")));
                Assert.Single(folder.GetProperty("folders").EnumerateArray());

                var theirs = await JsonAsync(await scene.BClient.GetAsync(Url("/api/web/v1/search?q=mathe")));
                Assert.Empty(theirs.GetProperty("folders").EnumerateArray());
                Assert.Empty(theirs.GetProperty("notebooks").EnumerateArray());
                // Too long or empty: nothing, not an error.
                Assert.Empty((await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/search?q=" + new string('a', 101))))).GetProperty("notebooks").EnumerateArray());
                Assert.Empty((await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/search")))).GetProperty("folders").EnumerateArray());
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task Lists_come_page_by_page_without_losing_or_repeating_a_row_and_the_limit_is_clamped()
        {
            var scene = await StartAsync();
            try
            {
                var sync = scene.Host.Services.GetRequiredService<SyncService>();
                var owner = new SyncOwner(scene.A, "ada", "test");
                var ids = new List<Guid>();
                for (var batch = 0; batch < 3; batch++)
                {
                    var rows = Enumerable.Range(0, 50).Select(index =>
                    {
                        var id = Guid.NewGuid();
                        ids.Add(id);
                        return Row("notebook", id, null, $$"""{"title":"Heft {{batch * 50 + index}}","type":"pages","template":"blank"}""");
                    }).ToArray();
                    await sync.PushAsync(new PushRequest(rows), owner, null, default);
                }

                var seen = new List<Guid>();
                string? cursor = null;
                var pages = 0;
                do
                {
                    var page = await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/notebooks?folder=root&limit=40" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)))));
                    seen.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
                    cursor = page.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                    pages++;
                }
                while (cursor is not null);

                Assert.Equal(4, pages);
                Assert.Equal(150, seen.Count);
                Assert.Equal(150, seen.Distinct().Count());
                Assert.Equal(ids.Order(), seen.Order());
                // A limit of 0 is raised, one above 200 is lowered, a cursor that is not ours starts at the beginning.
                Assert.Single((await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/notebooks?folder=root&limit=0")))).GetProperty("items").EnumerateArray());
                Assert.Equal(150, (await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/notebooks?folder=root&limit=100000")))).GetProperty("items").GetArrayLength());
                Assert.Equal(40, (await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/notebooks?folder=root&limit=40&cursor=nonsense")))).GetProperty("items").GetArrayLength());
            }
            finally
            {
                await EndAsync(scene);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_first_person_sees_the_old_rows_in_the_web_even_if_nothing_else_has_touched_the_server_since_the_account_was_made()
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            using var host = new AdminHost(database, fake.Executable);
            await using (database)
            using (fake)
            {
                // Rows made before any account exists, through the door of the app.
                using var app = host.Browser();
                app.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
                var push = await app.PostAsync(Url("/api/v1/sync/push"), new StringContent("""{"changes":[{"kind":"subject","id":"50000000-0000-0000-0000-0000000000a1","parentId":null,"payload":{"name":"Altbestand"},"deleted":false}]}""", System.Text.Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.OK, push.StatusCode);

                var (_, first) = await host.SignedInPersonAsync();
                var folders = await JsonAsync(await first.GetAsync(Url("/api/web/v1/folders")));

                Assert.Equal("Altbestand", Assert.Single(folders.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
            }
        }

        // Review 2, point 6: a payload that is not an object is refused when it comes in and skipped when it is read, so it can never break the lists of its owner.
        [RequiresDatabaseFact]
        public async Task A_payload_that_is_not_an_object_is_refused_and_if_one_is_there_all_the_same_the_lists_still_work()
        {
            var scene = await StartAsync();
            try
            {
                using var app = scene.Host.Browser();
                app.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
                foreach (var payload in new[] { "5", "[1,2]", "\"text\"", "true" })
                {
                    var response = await app.PostAsync(Url("/api/v1/sync/push"), new StringContent($$"""{"changes":[{"kind":"subject","id":"{{Guid.NewGuid()}}","parentId":null,"payload":{{payload}},"deleted":false}]}""", System.Text.Encoding.UTF8, "application/json"));
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                }

                // Rows like that, made some other way, are skipped in every list.
                var parents = new Dictionary<string, Guid?> { ["subject"] = null, ["notebook"] = Folder, ["page"] = Notebook, ["grade"] = Folder, ["card"] = Folder, ["stroke"] = Page, ["object"] = Page };
                foreach (var (kind, parent) in parents)
                {
                    await using var insert = scene.Database.Data.CreateCommand(
                        $"insert into entities (kind, id, parent_id, payload, deleted, seq, owner_id) values ('{kind}', '{Guid.NewGuid()}', {(parent is { } id ? "'" + id + "'" : "null")}, '[1]', false, nextval('change_seq'), '{scene.A}')");
                    await insert.ExecuteNonQueryAsync();
                }

                Assert.Single((await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/folders")))).GetProperty("items").EnumerateArray());
                Assert.Single((await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/notebooks")))).GetProperty("items").EnumerateArray());
                Assert.Equal(2, (await JsonAsync(await scene.AClient.GetAsync(Url($"/api/web/v1/notebooks/{Notebook}/pages")))).GetProperty("items").GetArrayLength());
                Assert.Equal("5/6", (await JsonAsync(await scene.AClient.GetAsync(Url($"/api/web/v1/pages/{Page}")))).GetProperty("page").GetProperty("solution").GetString());
                Assert.Single((await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/grades")))).GetProperty("items").EnumerateArray());
                Assert.Single((await JsonAsync(await scene.AClient.GetAsync(Url("/api/web/v1/cards")))).GetProperty("items").EnumerateArray());
            }
            finally
            {
                await EndAsync(scene);
            }
        }
    }
}
