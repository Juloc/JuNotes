using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace JuNotes.Backend.Library
{
    // What the picture of a page may contain: exactly what PageRenderer writes, element by element and attribute by attribute, with a pattern for every value. Anything else
    // (a script, a style, a link, a form, an animation, a reference to another address, any other HTML inside a foreignObject, any other CSS property) is dropped when the server
    // answers, so the page of the browser never sees it. The page repeats the same lists (pages/util.js; a test keeps both the same).
    public static partial class SvgAllowList
    {
        public const string Svg = "http://www.w3.org/2000/svg";
        public const string Xhtml = "http://www.w3.org/1999/xhtml";
        public const string AssetPrefix = "/api/web/v1/assets/";

        // Elements of the svg namespace and the attributes each may carry (no namespace on the attributes). "div" is the only element of the xhtml namespace, and only in a foreignObject.
        public static readonly IReadOnlyDictionary<string, string[]> Elements = new Dictionary<string, string[]>
        {
            ["svg"] = ["viewBox", "preserveAspectRatio"],
            ["g"] = ["class", "stroke-width", "fill", "stroke-linecap", "stroke-linejoin", "transform"],
            ["rect"] = ["x", "y", "width", "height", "rx", "fill", "fill-opacity", "stroke", "stroke-width"],
            ["line"] = ["x1", "y1", "x2", "y2", "stroke", "stroke-width"],
            ["circle"] = ["cx", "cy", "r", "fill", "stroke"],
            ["path"] = ["d", "stroke", "stroke-width", "stroke-opacity"],
            ["text"] = ["x", "y", "font-size", "font-weight", "text-anchor", "dominant-baseline", "fill", "stroke"],
            ["image"] = ["href", "x", "y", "width", "height", "preserveAspectRatio"],
            ["foreignObject"] = ["x", "y", "width", "height"],
            ["div"] = ["style"],
        };

        // The CSS properties of the one div (the text of a text box).
        public static readonly string[] StyleProperties = ["font", "color", "white-space", "overflow-wrap"];

        public const string EmptyPicture = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 595 842\"/>";

        public static string Clean(string markup)
        {
            XDocument document;
            try
            {
                using var reader = XmlReader.Create(new StringReader(markup), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
                document = XDocument.Load(reader);
            }
            catch (XmlException)
            {
                return EmptyPicture;
            }

            if (document.Root is not { } root || root.Name != XName.Get("svg", Svg) || Copy(root, inForeignObject: false) is not { } clean)
            {
                return EmptyPicture;
            }

            return clean.ToString(SaveOptions.DisableFormatting);
        }

        private static XElement? Copy(XElement source, bool inForeignObject)
        {
            var local = source.Name.LocalName;
            var ns = source.Name.NamespaceName;
            var isDiv = ns == Xhtml && local == "div" && inForeignObject;
            if (!isDiv && (ns != Svg || !Elements.ContainsKey(local) || local == "div"))
            {
                return null;
            }

            var target = new XElement(isDiv ? XName.Get("div", Xhtml) : XName.Get(local, Svg));
            var allowed = Elements[local];
            foreach (var attribute in source.Attributes())
            {
                // Namespace declarations are not attributes to copy: the element names carry them. href is the plain one only (no xlink).
                if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length > 0 || !allowed.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                {
                    continue;
                }

                if (ValueOf(local, attribute.Name.LocalName, attribute.Value) is { } value)
                {
                    target.SetAttributeValue(attribute.Name.LocalName, value);
                }
            }

            foreach (var node in source.Nodes())
            {
                switch (node)
                {
                    case XElement child:
                        // Text holds only text; a foreignObject holds only the one div; everything else may hold the allowed elements.
                        if (local != "text" && !isDiv && Copy(child, inForeignObject: local == "foreignObject") is { } copy)
                        {
                            target.Add(copy);
                        }

                        break;
                    case XText text when local is "text" || isDiv:
                        target.Add(new XText(text.Value));
                        break;
                }
            }

            return target;
        }

        // The value, if it has the form this attribute has in what the renderer writes; otherwise nothing (the attribute is dropped).
        private static string? ValueOf(string element, string name, string value)
        {
            var text = value.Trim();
            return name switch
            {
                "href" => element == "image" && AssetReference().IsMatch(text) ? text : null,
                "style" => CleanStyle(text),
                "class" => text is "paper" or "objects" or "ink" or "recordings" ? text : null,
                "viewBox" => ViewBox().IsMatch(text) ? text : null,
                "preserveAspectRatio" => Aspect().IsMatch(text) ? text : null,
                "transform" => Rotation().IsMatch(text) ? text : null,
                "d" => PathData().IsMatch(text) ? text : null,
                "fill" or "stroke" => Paint().IsMatch(text) ? text : null,
                "stroke-linecap" or "stroke-linejoin" => text == "round" ? text : null,
                "text-anchor" => text is "middle" or "start" or "end" ? text : null,
                "dominant-baseline" => text == "central" ? text : null,
                "font-weight" => text.Length is > 0 and <= 3 && text.All(char.IsAsciiDigit) ? text : null,
                _ => Number().IsMatch(text) ? text : null,
            };
        }

        // Only the four properties the text box uses, each with a plain value.
        private static string? CleanStyle(string css)
        {
            var kept = new List<string>();
            foreach (var declaration in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = declaration.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    continue;
                }

                var property = declaration[..colon].Trim().ToLowerInvariant();
                var value = declaration[(colon + 1)..].Trim();
                var ok = property switch
                {
                    "font" => Font().IsMatch(value),
                    "color" => Paint().IsMatch(value) && value != "none",
                    "white-space" => value == "pre-wrap",
                    "overflow-wrap" => value == "anywhere",
                    _ => false,
                };
                if (ok)
                {
                    kept.Add(property + ":" + value);
                }
            }

            return kept.Count == 0 ? null : string.Join(';', kept);
        }

        [GeneratedRegex(@"^/api/web/v1/assets/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex AssetReference();

        [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?(e-?[0-9]+)?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Number();

        [GeneratedRegex(@"^(none|#[0-9a-fA-F]{6})$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Paint();

        [GeneratedRegex(@"^-?[0-9.]+ -?[0-9.]+ [0-9.]+ [0-9.]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex ViewBox();

        [GeneratedRegex(@"^x(Min|Mid|Max)Y(Min|Mid|Max)( (meet|slice))?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Aspect();

        [GeneratedRegex(@"^rotate\(-?[0-9.]+ -?[0-9.]+ -?[0-9.]+\)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Rotation();

        [GeneratedRegex(@"^[MLmlZz0-9 .\-e]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex PathData();

        [GeneratedRegex(@"^[0-9.]+px sans-serif$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Font();
    }
}
