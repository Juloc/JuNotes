using System.Text.RegularExpressions;

namespace JuNotes.Backend.Tests
{
    // The pages of the web app are script files without a build; what can go wrong without a browser is checked as text: the texts exist in both languages, every key the
    // pages ask for is there, and no page writes markup from data or a colour of its own.
    public partial class WebPagesTests
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        private static string Wwwroot => Path.Combine(Root, "src/JuNotes.Frontend/wwwroot");

        private static HashSet<string> Keys(string file) => KeyPattern().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        private static IEnumerable<string> Scripts() => Directory.GetFiles(Wwwroot, "*.js", SearchOption.AllDirectories).Where(file => !file.Contains("/i18n/", StringComparison.Ordinal) && !Path.GetFileName(file).StartsWith("legacy-", StringComparison.Ordinal) && Path.GetFileName(file) != "sw.js");

        [Fact]
        public void German_and_English_have_the_same_keys()
        {
            var de = Keys(Path.Combine(Wwwroot, "i18n/de.js"));
            var en = Keys(Path.Combine(Wwwroot, "i18n/en.js"));

            Assert.NotEmpty(de);
            Assert.Empty(de.Except(en));
            Assert.Empty(en.Except(de));
        }

        [Fact]
        public void Every_text_the_pages_ask_for_exists()
        {
            var known = Keys(Path.Combine(Wwwroot, "i18n/de.js"));
            known.UnionWith(Keys(Path.Combine(Root, "base/ui/i18n/de.js")));
            var asked = new List<string>();
            foreach (var file in Scripts())
            {
                var text = File.ReadAllText(file);
                asked.AddRange(AskPattern().Matches(text).Select(match => match.Groups[1].Value));
                // tp() asks for key.one and key.other.
                asked.AddRange(PluralPattern().Matches(text).SelectMany(match => new[] { match.Groups[1].Value + ".one", match.Groups[1].Value + ".other" }));
            }

            Assert.NotEmpty(asked);
            Assert.Empty(asked.Where(key => !known.Contains(key) && !key.Contains('$', StringComparison.Ordinal)).Distinct().Order());
        }

        [Fact]
        public void The_pages_put_texts_in_as_text_and_use_no_colour_of_their_own()
        {
            foreach (var file in Scripts())
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("innerHTML", text, StringComparison.Ordinal);
                Assert.DoesNotContain("insertAdjacentHTML", text, StringComparison.Ordinal);
                Assert.DoesNotContain("document.write", text, StringComparison.Ordinal);
                Assert.DoesNotContain("eval(", text, StringComparison.Ordinal);
            }

            // The stylesheet of the app holds one colour variable and no colour value (COLOR-01); the old pages' files are exempt until they are gone.
            var css = File.ReadAllText(Path.Combine(Wwwroot, "app.css"));
            Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|rgb\(|hsl\(|oklch\(", css.Replace("--accent-hue: 75", string.Empty, StringComparison.Ordinal));
        }

        // Review 2, point 8: signing out empties the page, takes the search term out of the address, and a page brought back from the memory of the browser is loaded again.
        [Fact]
        public void Signing_out_empties_the_page_and_a_restored_page_is_loaded_again()
        {
            var text = File.ReadAllText(Path.Combine(Wwwroot, "app.js"));
            var signOut = text[text.IndexOf("onSignOut:", StringComparison.Ordinal)..];

            Assert.Contains("document.body.replaceChildren()", signOut, StringComparison.Ordinal);
            Assert.Contains("history.replaceState(null, '', location.pathname)", signOut, StringComparison.Ordinal);
            Assert.True(signOut.IndexOf("replaceChildren", StringComparison.Ordinal) < signOut.IndexOf("location.reload()", StringComparison.Ordinal));
            Assert.Contains("addEventListener('pageshow', (e) => { if (e.persisted) location.reload(); })", text, StringComparison.Ordinal);
        }

        [GeneratedRegex(@"^\s*'([a-zA-Z0-9.]+)':", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
        private static partial Regex KeyPattern();

        [GeneratedRegex(@"\bt\('([a-zA-Z0-9.$`{}]+)'", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
        private static partial Regex AskPattern();

        [GeneratedRegex(@"\btp\('([a-zA-Z0-9.]+)'", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
        private static partial Regex PluralPattern();
    }
}
