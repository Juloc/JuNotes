using JuNotes.Backend.School;

namespace JuNotes.Backend.Tests
{
    public class FeedParserTests
    {
        [Fact]
        public void An_rss_feed_gives_its_items()
        {
            var items = FeedParser.Parse("""
                <?xml version="1.0"?><rss version="2.0"><channel><title>Schule</title>
                <item><title>Elternabend</title><link>https://s.example/n/1</link><guid>g1</guid><pubDate>Wed, 18 Nov 2026 10:00:00 GMT</pubDate><description>&lt;p&gt;Am &lt;b&gt;Dienstag&lt;/b&gt; um 19 Uhr.&lt;/p&gt;</description></item>
                <item><title>Ohne Guid</title><link>https://s.example/n/2</link></item>
                <item><description>kein Titel</description></item>
                </channel></rss>
                """);

            Assert.Equal(2, items.Count);
            Assert.Equal("g1", items[0].Key);
            Assert.Equal("Elternabend", items[0].Title);
            Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero), items[0].PublishedAt);
            Assert.Equal("Am Dienstag um 19 Uhr.", items[0].Summary);
            Assert.Equal("https://s.example/n/2", items[1].Key);
        }

        [Fact]
        public void An_atom_feed_gives_its_entries()
        {
            var items = FeedParser.Parse("""
                <feed xmlns="http://www.w3.org/2005/Atom"><title>Schule</title>
                <entry><title>Sportfest</title><id>urn:1</id><link rel="alternate" href="https://s.example/a"/><published>2026-11-17T08:00:00+01:00</published><summary>Alle machen mit.</summary></entry>
                </feed>
                """);

            var item = Assert.Single(items);
            Assert.Equal("urn:1", item.Key);
            Assert.Equal("https://s.example/a", item.Link);
            Assert.Equal(new DateTimeOffset(2026, 11, 17, 7, 0, 0, TimeSpan.Zero), item.PublishedAt!.Value.ToUniversalTime());
        }

        [Fact]
        public void A_long_summary_is_shortened()
        {
            var item = Assert.Single(FeedParser.Parse($"<rss><channel><item><title>T</title><description>{new string('a', 2000)}</description></item></channel></rss>"));

            Assert.True(item.Summary!.Length < 700);
            Assert.EndsWith("…", item.Summary);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not xml")]
        [InlineData("<html><body>captive portal</body></html>")]
        public void Anything_else_gives_nothing(string text) => Assert.Empty(FeedParser.Parse(text));

        [Fact]
        public void A_doctype_with_an_external_entity_is_not_followed()
        {
            // Entity expansion from outside must not read files or reach the network.
            var items = FeedParser.Parse("""<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY x SYSTEM "file:///etc/passwd">]><rss><channel><item><title>&x;</title></item></channel></rss>""");

            Assert.DoesNotContain(items, item => item.Title.Contains("root:", StringComparison.Ordinal));
        }
    }
}
