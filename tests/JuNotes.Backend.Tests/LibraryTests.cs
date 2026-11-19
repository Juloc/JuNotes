using System.Text.Json;
using System.Xml.Linq;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Library;
using JuNotes.Backend.Service;

namespace JuNotes.Backend.Tests
{
    public class PageRendererTests
    {
        private static StrokeInfo Stroke(string tool = "pen", string color = "#112233", double width = 2, params (double, double)[] points)
            => new(Guid.NewGuid(), tool, color, width, points);

        private static XDocument Parse(string svg) => XDocument.Parse(svg);

        private static int Count(XDocument document, string element) => document.Descendants().Count(node => node.Name.LocalName == element);

        [Fact]
        public void A_sheet_and_a_whiteboard_have_their_own_size()
        {
            Assert.Equal("0 0 595 842", Parse(PageRenderer.Svg("pages", "blank", [])).Root!.Attribute("viewBox")!.Value);
            Assert.Equal("0 0 3000 2000", Parse(PageRenderer.Svg("whiteboard", "blank", [])).Root!.Attribute("viewBox")!.Value);
        }

        [Fact]
        public void Ruled_paper_has_lines_and_a_margin_and_blank_has_nothing()
        {
            var ruled = Parse(PageRenderer.Svg("pages", "ruled", []));
            var blank = Parse(PageRenderer.Svg("pages", "blank", []));

            Assert.True(Count(ruled, "line") > 20);
            Assert.Contains(ruled.Descendants(), node => node.Name.LocalName == "line" && node.Attribute("x1")?.Value == "64" && node.Attribute("x2")?.Value == "64");
            Assert.Equal(0, Count(blank, "line"));
        }

        [Fact]
        public void Grid_paper_has_lines_both_ways_dots_have_circles_and_cornell_its_two_guides()
        {
            var grid = Parse(PageRenderer.Svg("pages", "grid", []));
            Assert.Contains(grid.Descendants(), node => node.Attribute("x1")?.Value == node.Attribute("x2")?.Value && node.Attribute("x1")?.Value != "0");
            Assert.Contains(grid.Descendants(), node => node.Attribute("y1")?.Value == node.Attribute("y2")?.Value);

            Assert.True(Count(Parse(PageRenderer.Svg("pages", "dotted", [])), "circle") > 500);

            var cornell = Parse(PageRenderer.Svg("pages", "cornell", []));
            Assert.Contains(cornell.Descendants(), node => node.Attribute("x1")?.Value == "170");
            Assert.Contains(cornell.Descendants(), node => node.Attribute("y1")?.Value == "692" && node.Attribute("y2")?.Value == "692");
        }

        [Fact]
        public void A_stroke_is_a_path_through_its_points_in_its_colour_and_width()
        {
            var svg = Parse(PageRenderer.Svg("pages", "blank", [Stroke(width: 2.5, points: [(1, 2), (10.126, 5.5), (20, 0)])]));

            var path = Assert.Single(svg.Descendants(), node => node.Name.LocalName == "path");
            Assert.Equal("M1 2 L10.13 5.5 L20 0", path.Attribute("d")!.Value);
            Assert.Equal("#112233", path.Attribute("stroke")!.Value);
            Assert.Equal("2.5", path.Attribute("stroke-width")!.Value);
            Assert.Null(path.Attribute("stroke-opacity"));
        }

        [Fact]
        public void A_marker_is_see_through_a_tap_leaves_a_dot_and_an_empty_stroke_is_left_out()
        {
            var svg = Parse(PageRenderer.Svg("pages", "blank", [Stroke("marker", "#ffcc00", 14, (5, 5), (50, 5)), Stroke(points: [(7, 8)]), Stroke(points: [])]));

            var paths = svg.Descendants().Where(node => node.Name.LocalName == "path").ToList();
            Assert.Equal(2, paths.Count);
            Assert.Equal("0.45", paths[0].Attribute("stroke-opacity")!.Value);
            Assert.Equal("M7 8 L7 8", paths[1].Attribute("d")!.Value);
        }

        [Theory]
        [InlineData("#abc", "#000000")]
        [InlineData("red\" onload=\"alert(1)", "#000000")]
        [InlineData("#12345g", "#000000")]
        [InlineData("#AbCdEf", "#AbCdEf")]
        [InlineData(null, "#000000")]
        public void Only_a_hex_colour_gets_into_the_markup(string? given, string expected) => Assert.Equal(expected, PageRenderer.SafeColor(given));

        [Fact]
        public void Hostile_data_still_gives_well_formed_markup_without_scripts()
        {
            var svg = PageRenderer.Svg("pages", "ruled\"><script>alert(1)</script>", [Stroke(color: "\"><script>x</script>", width: 1e9, points: [(0, 0), (double.MaxValue / 1e300, 1)])]);

            var document = Parse(svg);

            Assert.DoesNotContain("<script", svg, StringComparison.Ordinal);
            Assert.Equal("60", Assert.Single(document.Descendants(), node => node.Name.LocalName == "path").Attribute("stroke-width")!.Value);
        }
    }

    public class LibraryReaderTests
    {
        private static readonly Guid Math = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        private static readonly Guid German = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
        private static readonly Guid Fractions = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
        private static readonly Guid Old = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
        private static readonly Guid PageB = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
        private static readonly Guid PageA = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
        private static readonly Guid Asset = Guid.Parse("00000000-0000-0000-0000-0000000000d1");

        private static SyncChange Entity(string kind, Guid id, Guid? parent, string json) => new(kind, id, parent, JsonElement.Parse(json), false);

        private static async Task<LibraryReader> SeedAsync(TestDatabase database)
        {
            await new SyncService(database.Data).PushAsync(new PushRequest(
            [
                Entity("subject", Math, null, """{"name":"Mathe","color":"#0a84ff","symbol":"function","sort":1}"""),
                Entity("subject", German, null, """{"name":"Deutsch","color":"#ff9f0a","symbol":"text.book.closed","sort":0}"""),
                Entity("notebook", Fractions, Math, """{"title":"Brüche","type":"pages","template":"grid","createdAt":"2026-11-10T08:00:00Z"}"""),
                Entity("notebook", Old, Math, """{"title":"Altes Heft","type":"pages","template":"ruled","createdAt":"2026-09-01T08:00:00Z","trashedAt":"2026-11-15T08:00:00Z"}"""),
                Entity("page", PageB, Fractions, """{"sort":"j","template":null,"content":{"type":"scan","assetID":"00000000-0000-0000-0000-0000000000d1"},"heading":"Do 19.11. · Mathe","additions":["Beim Kürzen den ggT nehmen."]}"""),
                Entity("page", PageA, Fractions, """{"sort":"b","printedText":"Aufgabe 1","solution":"5/6"}"""),
                Entity("page", Guid.NewGuid(), Fractions, """{"sort":"a","trashedAt":"2026-11-15T08:00:00Z"}"""),
                Entity("stroke", Guid.NewGuid(), PageA, """{"tool":"marker","color":"#ffcc00","width":12,"points":[{"x":1,"y":2,"pressure":1},{"x":3.5,"y":4}]}"""),
                new SyncChange("stroke", Guid.NewGuid(), PageA, null, true),
            ]), CancellationToken.None);
            return new LibraryReader(database.Data);
        }

        [RequiresDatabaseFact]
        public async Task Subjects_come_in_the_order_she_gave_them()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var reader = await SeedAsync(database);

            var subjects = await reader.SubjectsAsync(CancellationToken.None);

            Assert.Equal(["Deutsch", "Mathe"], subjects.Select(subject => subject.Name));
            Assert.Equal("#0a84ff", subjects[1].Color);
        }

        [RequiresDatabaseFact]
        public async Task Notebooks_in_the_trash_are_not_shown()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var reader = await SeedAsync(database);

            var notebooks = await reader.NotebooksAsync(CancellationToken.None);

            var notebook = Assert.Single(notebooks);
            Assert.Equal("Brüche", notebook.Title);
            Assert.Equal(Math, notebook.SubjectId);
            Assert.Equal("grid", notebook.Template);
            Assert.Null(await reader.NotebookAsync(Old, CancellationToken.None));
            Assert.NotNull(await reader.NotebookAsync(Fractions, CancellationToken.None));
        }

        [RequiresDatabaseFact]
        public async Task Pages_come_in_order_without_the_trashed_one_and_keep_their_content()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var reader = await SeedAsync(database);

            var pages = await reader.PagesAsync(Fractions, CancellationToken.None);

            Assert.Equal([PageA, PageB], pages.Select(page => page.Id));
            Assert.Equal("drawing", pages[0].ContentType);
            Assert.Equal("Aufgabe 1", pages[0].PrintedText);
            Assert.Equal("5/6", pages[0].Solution);
            Assert.Equal("scan", pages[1].ContentType);
            Assert.Equal(Asset, pages[1].AssetId);
            Assert.Equal("Do 19.11. · Mathe", pages[1].Heading);
            Assert.Equal(["Beim Kürzen den ggT nehmen."], pages[1].Additions);
        }

        [RequiresDatabaseFact]
        public async Task Strokes_come_with_their_points_and_deleted_ones_are_gone()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var reader = await SeedAsync(database);

            var strokes = await reader.StrokesAsync(PageA, CancellationToken.None);

            var stroke = Assert.Single(strokes);
            Assert.Equal("marker", stroke.Tool);
            Assert.Equal(12, stroke.Width);
            Assert.Equal([(1, 2), (3.5, 4)], stroke.Points);
        }

        [RequiresDatabaseFact]
        public async Task An_empty_library_reads_as_empty()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var reader = new LibraryReader(database.Data);

            Assert.Empty(await reader.SubjectsAsync(CancellationToken.None));
            Assert.Empty(await reader.NotebooksAsync(CancellationToken.None));
            Assert.Empty(await reader.PagesAsync(Guid.NewGuid(), CancellationToken.None));
        }
    }
}
