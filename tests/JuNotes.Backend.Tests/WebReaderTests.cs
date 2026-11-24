using System.Net;
using System.Text.Json;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;
using JuNotes.Frontend.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    public class WebReaderTests
    {
                private static readonly Guid Subject = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        private static readonly Guid Book = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
        private static readonly Guid Board = Guid.Parse("00000000-0000-0000-0000-0000000000b3");
        private static readonly Guid Trashed = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
        private static readonly Guid Scan = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1];

        private static SyncChange Entity(string kind, Guid id, Guid? parent, string json) => new(kind, id, parent, JsonElement.Parse(json), false);

        private static async Task<(AdminHost Host, FakeCodex Fake, TestDatabase Database)> StartAsync(bool seed = false)
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            var host = new AdminHost(database, fake.Executable);
            if (seed)
            {
                var pageA = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
                var pageB = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
                _ = host.Services;
                await host.Services.GetRequiredService<SyncService>().PushAsync(new PushRequest(
                [
                    Entity("subject", Subject, null, """{"name":"Mathe","color":"#0a84ff","symbol":"function","sort":0}"""),
                    Entity("subject", Guid.Parse("00000000-0000-0000-0000-0000000000a2"), null, """{"name":"Leeres Fach","color":"red;background:url(x)","symbol":"book","sort":1}"""),
                    Entity("notebook", Book, Subject, """{"title":"Brüche <script>alert(1)</script>","type":"pages","template":"grid","createdAt":"2026-11-10T08:00:00Z"}"""),
                    Entity("notebook", Board, Subject, """{"title":"Mindmap","type":"whiteboard","template":"blank","createdAt":"2026-11-11T08:00:00Z"}"""),
                    Entity("notebook", Trashed, Subject, """{"title":"Im Papierkorb","type":"pages","template":"ruled","createdAt":"2026-11-12T08:00:00Z","trashedAt":"2026-11-15T08:00:00Z"}"""),
                    Entity("page", pageA, Book, """{"sort":"b","printedText":"Aufgabe 1: 1/2 + 1/3","solution":"5/6","heading":"Do 19.11.","additions":["Kürzen nicht vergessen."]}"""),
                    Entity("page", pageB, Book, """{"sort":"c","content":{"type":"scan","assetID":"00000000-0000-0000-0000-0000000000d1"}}"""),
                    Entity("stroke", Guid.NewGuid(), pageA, """{"tool":"pen","color":"#1d3a8a","width":2.5,"points":[{"x":10,"y":20},{"x":30,"y":40}]}"""),
                    Entity("stroke", Guid.NewGuid(), pageA, """{"tool":"pen","color":"\"><script>x</script>","width":2,"points":[{"x":1,"y":1}]}"""),
                    Entity("page", Guid.NewGuid(), Board, """{"sort":"i"}"""),
                ]), CancellationToken.None);
                await host.Services.GetRequiredService<AssetService>().SaveAsync(Scan, new MemoryStream(Jpeg), CancellationToken.None);
            }

            return (host, fake, database);
        }

        private static Task<HttpClient> ReaderAsync(AdminHost host) => host.SignedInAsync("member");

        [RequiresDatabaseFact]
        public async Task The_front_door_is_the_page_of_the_base_and_the_notes_need_a_session()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();

                var door = await client.GetAsync(new Uri("/", UriKind.Relative));
                Assert.Equal(HttpStatusCode.OK, door.StatusCode);
                Assert.Contains("/app.js", await door.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                var notes = await client.GetAsync(new Uri("/Notes", UriKind.Relative));

                Assert.Equal(HttpStatusCode.Redirect, notes.StatusCode);
                Assert.Equal("/", Web.PathOf(notes));
            }
        }

        [RequiresDatabaseFact]
        public async Task The_reader_opens_the_notes_but_never_the_admin_area()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await ReaderAsync(host);

                var notes = await client.GetAsync(new Uri("/Notes", UriKind.Relative));
                var admin = await client.GetAsync(new Uri("/Admin", UriKind.Relative));
                var profiles = await client.GetAsync(new Uri("/Admin/Profiles", UriKind.Relative));

                Assert.Equal(HttpStatusCode.OK, notes.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, profiles.StatusCode);
                Assert.DoesNotContain("/Admin\"", await notes.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task An_administrator_opens_everything()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await host.SignedInAsync();

                var notes = await client.GetAsync(new Uri("/Notes", UriKind.Relative));

                Assert.Equal(HttpStatusCode.OK, notes.StatusCode);
                Assert.Contains("href=\"/Admin\"", await notes.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/Admin", UriKind.Relative))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_overview_lists_notebooks_under_their_subjects_and_hides_the_trash()
        {
            var (host, fake, database) = await StartAsync(seed: true);
            await using (database) using (fake) using (host)
            {
                var client = await ReaderAsync(host);

                var page = await client.GetStringAsync(new Uri("/Notes", UriKind.Relative));

                Assert.Contains("Mathe", page, StringComparison.Ordinal);
                Assert.Contains("Mindmap", page, StringComparison.Ordinal);
                Assert.Contains($"href=\"/Notes/{Book}\"", page, StringComparison.Ordinal);
                Assert.DoesNotContain("Im Papierkorb", page, StringComparison.Ordinal);
                Assert.DoesNotContain("Leeres Fach", page, StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task Markup_in_names_and_colours_never_reaches_the_page_as_markup()
        {
            var (host, fake, database) = await StartAsync(seed: true);
            await using (database) using (fake) using (host)
            {
                var client = await ReaderAsync(host);

                var overview = await client.GetStringAsync(new Uri("/Notes", UriKind.Relative));
                var book = await client.GetStringAsync(new Uri($"/Notes/{Book}", UriKind.Relative));

                Assert.DoesNotContain("<script>alert(1)</script>", overview, StringComparison.Ordinal);
                Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", overview, StringComparison.Ordinal);
                Assert.DoesNotContain("<script>x</script>", book, StringComparison.Ordinal);
                Assert.DoesNotContain("background:url(x)", overview, StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_notebook_shows_its_pages_with_strokes_printed_text_solution_and_additions()
        {
            var (host, fake, database) = await StartAsync(seed: true);
            await using (database) using (fake) using (host)
            {
                var client = await ReaderAsync(host);

                var page = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/Notes/{Book}", UriKind.Relative)));

                Assert.Contains("viewBox=\"0 0 595 842\"", page, StringComparison.Ordinal);
                Assert.Contains("d=\"M10 20 L30 40\"", page, StringComparison.Ordinal);
                Assert.Contains("stroke=\"#1d3a8a\"", page, StringComparison.Ordinal);
                Assert.Contains("Aufgabe 1: 1/2", page, StringComparison.Ordinal);
                Assert.Contains("1/3", page, StringComparison.Ordinal);
                Assert.Contains("<summary>Lösung</summary>", page, StringComparison.Ordinal);
                Assert.Contains("5/6", page, StringComparison.Ordinal);
                Assert.Contains("Kürzen nicht vergessen.", page, StringComparison.Ordinal);
                Assert.Contains("Do 19.11.", page, StringComparison.Ordinal);
                Assert.Contains($"src=\"/Notes/Asset/{Scan}\"", page, StringComparison.Ordinal);
                Assert.Contains("Seite 1 von 2", page, StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_whiteboard_is_shown_as_one_big_surface_and_an_unknown_notebook_is_a_404()
        {
            var (host, fake, database) = await StartAsync(seed: true);
            await using (database) using (fake) using (host)
            {
                var client = await ReaderAsync(host);

                var board = await client.GetStringAsync(new Uri($"/Notes/{Board}", UriKind.Relative));
                var missing = await client.GetAsync(new Uri($"/Notes/{Guid.NewGuid()}", UriKind.Relative));
                var trashed = await client.GetAsync(new Uri($"/Notes/{Trashed}", UriKind.Relative));

                Assert.Contains("viewBox=\"0 0 3000 2000\"", board, StringComparison.Ordinal);
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, trashed.StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task A_scan_is_only_given_to_a_signed_in_reader_with_its_real_type()
        {
            var (host, fake, database) = await StartAsync(seed: true);
            await using (database) using (fake) using (host)
            {
                var anonymous = await host.Browser().GetAsync(new Uri($"/Notes/Asset/{Scan}", UriKind.Relative));
                Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);

                var client = await ReaderAsync(host);
                var scan = await client.GetAsync(new Uri($"/Notes/Asset/{Scan}", UriKind.Relative));

                Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
                Assert.Equal("image/jpeg", scan.Content.Headers.ContentType!.MediaType);
                Assert.Equal("nosniff", scan.Headers.GetValues("X-Content-Type-Options").Single());
                Assert.Equal(Jpeg, await scan.Content.ReadAsByteArrayAsync());
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/Notes/Asset/{Guid.NewGuid()}", UriKind.Relative))).StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task The_app_manifest_and_icon_are_open_so_the_page_can_be_installed()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = host.Browser();

                var manifest = await client.GetAsync(new Uri("/manifest.webmanifest", UriKind.Relative));
                var icon = await client.GetAsync(new Uri("/icon.svg", UriKind.Relative));

                Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
                using var document = JsonDocument.Parse(await manifest.Content.ReadAsStringAsync());
                Assert.Equal("Notes", document.RootElement.GetProperty("name").GetString());
                Assert.Equal("/", document.RootElement.GetProperty("start_url").GetString());
                Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task Signing_out_closes_the_notes_again()
        {
            var (host, fake, database) = await StartAsync();
            await using (database) using (fake) using (host)
            {
                var client = await ReaderAsync(host);

                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/Notes", UriKind.Relative))).StatusCode);
                using var logout = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/logout", UriKind.Relative));
                logout.Headers.Add("X-Base", "1");
                logout.Headers.Add("Origin", "http://localhost");
                Assert.True((await client.SendAsync(logout)).IsSuccessStatusCode);

                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/Notes", UriKind.Relative))).StatusCode);
            }
        }
    }
}
