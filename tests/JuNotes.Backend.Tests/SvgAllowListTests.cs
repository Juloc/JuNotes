using System.Text.RegularExpressions;
using System.Xml.Linq;
using JuNotes.Backend.Library;

namespace JuNotes.Backend.Tests
{
    // The picture of a page leaves the server only through the allow list. Two kinds of test: nothing the renderer writes is lost, and everything else is.
    public partial class SvgAllowListTests
    {
        private static readonly Guid Picture = Guid.Parse("60000000-0000-0000-0000-000000000001");

        private static readonly StrokeInfo[] Strokes =
        [
            new(Guid.NewGuid(), "pen", "#1d3a8a", 2.5, [(10, 20), (30, 40), (50, 20)]),
            new(Guid.NewGuid(), "marker", "#ffcc00", 12, [(5, 5), (80, 90)]),
            new(Guid.NewGuid(), "pen", "not-a-colour", 2, [(1, 1)]),
        ];

        private static ObjectInfo Item(string type, string? text = null, Guid? asset = null) => new(Guid.NewGuid(), type, 40, 80, 200, 60, 12, text, asset, "#336699", 18, 1);

        private static List<ObjectInfo> Objects() =>
        [
            Item("text", "Merke: <b>Hauptnenner</b> & \"Klammern\""),
            Item("image", asset: Picture),
            Item("image"),
            Item("sticker", "🌱"),
            Item("sticker", "star.fill"),
            Item("tape"),
            Item("audio"),
        ];

        private static (int Elements, int Attributes, string Text) Measure(string markup)
        {
            var root = XDocument.Parse(markup).Root!;
            var all = root.DescendantsAndSelf().ToList();
            return (all.Count, all.Sum(element => element.Attributes().Count(attribute => !attribute.IsNamespaceDeclaration)), string.Concat(root.DescendantNodes().OfType<XText>().Select(text => text.Value)));
        }

        [Theory]
        [InlineData("pages", "blank")]
        [InlineData("pages", "ruled")]
        [InlineData("pages", "ruledMedium")]
        [InlineData("pages", "ruledFine")]
        [InlineData("pages", "grid")]
        [InlineData("pages", "gridFine")]
        [InlineData("pages", "gridLarge")]
        [InlineData("pages", "dotted")]
        [InlineData("pages", "cornell")]
        [InlineData("pages", "checklist")]
        [InlineData("pages", "planner")]
        [InlineData("whiteboard", "blank")]
        public void Nothing_the_renderer_writes_is_lost(string notebookType, string template)
        {
            var drawn = PageRenderer.Svg(notebookType, template, Strokes, Objects(), "/api/web/v1/assets/");

            var clean = SvgAllowList.Clean(drawn);

            Assert.Equal(Measure(drawn), Measure(clean));
            Assert.StartsWith("<svg", clean, StringComparison.Ordinal);
        }

        [Fact]
        public void The_text_of_a_text_box_stays_text_and_never_becomes_markup()
        {
            var clean = SvgAllowList.Clean(PageRenderer.Svg("pages", "blank", [], [Item("text", "<img src=x onerror=alert(1)> & <script>x()</script>")]));

            Assert.DoesNotContain("<img", clean, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", clean, StringComparison.Ordinal);
            Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", clean, StringComparison.Ordinal);
        }

        public static TheoryData<string, string> Dangerous => new()
        {
            { "<script>alert(1)</script>", "script" },
            { "<style>@import 'x'</style>", "style>" },
            { "<link rel=\"stylesheet\" href=\"/x.css\"/>", "link" },
            { "<meta http-equiv=\"refresh\" content=\"0;url=/x\"/>", "meta" },
            { "<base href=\"/x/\"/>", "base" },
            { "<form action=\"/api/auth/logout\"><input/></form>", "form" },
            { "<a href=\"/api/x\"><text>x</text></a>", "<a" },
            { "<animate attributeName=\"href\" to=\"/api/x\"/>", "animate" },
            { "<set attributeName=\"href\" to=\"/api/x\"/>", "<set" },
            { "<use href=\"/api/web/v1/folders\"/>", "<use" },
            { "<image href=\"/api/web/v1/folders\" width=\"1\" height=\"1\"/>", "folders" },
            { "<image href=\"https://example.invalid/x.png\" width=\"1\" height=\"1\"/>", "example.invalid" },
            { "<image xlink:href=\"/api/web/v1/folders\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"1\" height=\"1\"/>", "xlink" },
            { "<image src=\"/api/x\" srcset=\"/api/y 2x\" width=\"1\" height=\"1\"/>", "srcset" },
            { "<rect width=\"1\" height=\"1\" onload=\"alert(1)\" onclick=\"alert(2)\"/>", "onload" },
            { "<rect width=\"1\" height=\"1\" style=\"fill:url(/api/x)\"/>", "style=" },
            { "<rect width=\"1\" height=\"1\" fill=\"url(#x)\"/>", "url(" },
            { "<rect width=\"1\" height=\"1\" action=\"/api/x\"/>", "action" },
            { "<foreignObject width=\"1\" height=\"1\"><iframe src=\"/api/x\" xmlns=\"http://www.w3.org/1999/xhtml\"/></foreignObject>", "iframe" },
            { "<foreignObject width=\"1\" height=\"1\"><img xmlns=\"http://www.w3.org/1999/xhtml\" src=\"/api/x\"/></foreignObject>", "<img" },
            { "<foreignObject width=\"1\" height=\"1\"><form xmlns=\"http://www.w3.org/1999/xhtml\" action=\"/api/x\"/></foreignObject>", "form" },
            { "<foreignObject width=\"1\" height=\"1\"><div xmlns=\"http://www.w3.org/1999/xhtml\" style=\"background:url(/api/x);position:fixed\">x</div></foreignObject>", "background" },
            { "<foreignObject width=\"1\" height=\"1\"><div xmlns=\"http://www.w3.org/1999/xhtml\" style=\"position:fixed;top:0\">x</div></foreignObject>", "position" },
            { "<div xmlns=\"http://www.w3.org/1999/xhtml\">outside</div>", "outside" },
            { "<g transform=\"translate(1 1)\"><rect width=\"1\" height=\"1\"/></g>", "translate" },
            { "<path d=\"M0 0 L1 1\" stroke=\"red\"/>", "red" },
            { "<g class=\"evil\"><rect width=\"1\" height=\"1\"/></g>", "evil" },
        };

        [Theory]
        [MemberData(nameof(Dangerous))]
        public void Everything_else_is_dropped(string extra, string marker)
        {
            var wrapped = $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 10 10\" onload=\"alert(0)\">{extra}</svg>";

            var clean = SvgAllowList.Clean(wrapped);

            Assert.DoesNotContain(marker, clean, StringComparison.Ordinal);
            Assert.DoesNotContain("onload", clean, StringComparison.Ordinal);
            Assert.StartsWith("<svg", clean, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("not xml at all")]
        [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><g></svg>")]
        [InlineData("<!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>")]
        [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><script>x()</script></html>")]
        public void Something_that_is_not_the_picture_becomes_an_empty_one(string markup)
            => Assert.Equal(SvgAllowList.EmptyPicture, SvgAllowList.Clean(markup));

        [Fact]
        public void The_image_may_point_only_to_a_file_of_the_web_api_by_its_id()
        {
            Assert.Contains("href=\"/api/web/v1/assets/60000000-0000-0000-0000-000000000001\"", SvgAllowList.Clean(PageRenderer.Svg("pages", "blank", [], [Item("image", asset: Picture)], "/api/web/v1/assets/")), StringComparison.Ordinal);
            Assert.DoesNotContain("href", SvgAllowList.Clean(PageRenderer.Svg("pages", "blank", [], [Item("image", asset: Picture)], "/Notes/Asset/")), StringComparison.Ordinal);
            Assert.DoesNotContain("href", SvgAllowList.Clean(PageRenderer.Svg("pages", "blank", [], [Item("image", asset: Picture)], "/api/web/v1/assets/../folders?x=")), StringComparison.Ordinal);
        }

        // The page repeats the lists (pages/util.js): the same elements, the same attributes, the same style properties.
        [Fact]
        public void The_lists_of_the_page_are_the_lists_of_the_server()
        {
            var text = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/JuNotes.Frontend/wwwroot/pages/util.js")));
            var fromPage = ElementLine().Matches(text).ToDictionary(match => match.Groups[1].Value, match => Quoted().Matches(match.Groups[2].Value).Select(item => item.Groups[1].Value).Order().ToArray());
            var style = StyleLine().Match(text).Groups[1].Value;

            Assert.Equal(SvgAllowList.Elements.Keys.Order(), fromPage.Keys.Order());
            foreach (var (element, attributes) in SvgAllowList.Elements)
            {
                Assert.Equal(attributes.Order(), fromPage[element]);
            }

            Assert.Equal(SvgAllowList.StyleProperties.Order(), Quoted().Matches(style).Select(item => item.Groups[1].Value).Order());
        }

        [GeneratedRegex(@"^\s*'?([a-zA-Z]+)'?: \[(.*)\],?\s*(//.*)?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
        private static partial Regex ElementLine();

        [GeneratedRegex(@"STYLE_PROPERTIES = \[(.*)\]", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
        private static partial Regex StyleLine();

        [GeneratedRegex(@"'([^']+)'", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
        private static partial Regex Quoted();
    }
}
