using System.Globalization;
using System.Xml.Linq;

namespace JuNotes.Backend.School
{
    public sealed record NewsItem(string Key, string Title, string? Link, DateTimeOffset? PublishedAt, string? Summary);

    // RSS 2.0 and Atom, which is what a school website's news come as.
    public static class FeedParser
    {
        public static IReadOnlyList<NewsItem> Parse(string xml)
        {
            XDocument document;
            try
            {
                // No DTD and no external entities: the feed comes from outside.
                document = XDocument.Parse(xml, LoadOptions.None);
            }
            catch (System.Xml.XmlException)
            {
                return [];
            }

            var root = document.Root;
            if (root is null)
            {
                return [];
            }

            return root.Name.LocalName switch
            {
                "rss" => root.Descendants().Where(element => element.Name.LocalName == "item").Select(Rss).OfType<NewsItem>().ToList(),
                "feed" => root.Elements().Where(element => element.Name.LocalName == "entry").Select(Atom).OfType<NewsItem>().ToList(),
                _ => [],
            };
        }

        private static NewsItem? Rss(XElement item)
        {
            var title = Text(item, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var link = Text(item, "link");
            return new NewsItem(Text(item, "guid") ?? link ?? title, title, link, Date(Text(item, "pubDate") ?? Text(item, "date")), Plain(Text(item, "description")));
        }

        private static NewsItem? Atom(XElement entry)
        {
            var title = Text(entry, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var link = entry.Elements().FirstOrDefault(element => element.Name.LocalName == "link" && ((string?)element.Attribute("rel") is null or "alternate"))?.Attribute("href")?.Value;
            return new NewsItem(Text(entry, "id") ?? link ?? title, title, link, Date(Text(entry, "published") ?? Text(entry, "updated")), Plain(Text(entry, "summary") ?? Text(entry, "content")));
        }

        private static string? Text(XElement parent, string name) => parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();

        private static DateTimeOffset? Date(string? text)
            => text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

        // A summary may carry HTML; only the text is kept, and not too much of it.
        internal static string? Plain(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
            text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1))).Trim();
            return text.Length > 600 ? text[..600].TrimEnd() + " …" : text;
        }
    }
}
