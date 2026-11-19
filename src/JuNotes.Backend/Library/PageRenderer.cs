using System.Globalization;
using System.Text;

namespace JuNotes.Backend.Library
{
    // A page as SVG, drawn in the page's own units (a sheet of paper is 595 by 842, a whiteboard 3000 by 2000), so it looks the same at any size. The printed
    // lines of the paper and her strokes are drawn here; text the app printed on the page is laid over it by the web page itself.
    public static class PageRenderer
    {
        public const double PageWidth = 595;
        public const double PageHeight = 842;
        public const double BoardWidth = 3000;
        public const double BoardHeight = 2000;

        public static (double Width, double Height) SizeOf(string notebookType) => notebookType == "whiteboard" ? (BoardWidth, BoardHeight) : (PageWidth, PageHeight);

        public static string Svg(string notebookType, string template, IReadOnlyList<StrokeInfo> strokes)
        {
            var (width, height) = SizeOf(notebookType);
            var svg = new StringBuilder();
            svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Num(width)} {Num(height)}\" preserveAspectRatio=\"xMidYMid meet\">");
            svg.Append("<g class=\"paper\" stroke-width=\"0.6\">");
            Background(svg, template, width, height);
            svg.Append("</g><g class=\"ink\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");
            foreach (var stroke in strokes)
            {
                Stroke(svg, stroke);
            }

            svg.Append("</g></svg>");
            return svg.ToString();
        }

        // The printed paper, drawn as in the app: ruled with a margin, grid, dots, or the Cornell layout.
        internal static void Background(StringBuilder svg, string template, double width, double height)
        {
            const string line = "#c7ddf5";
            const string margin = "#f5a3a3";
            switch (template)
            {
                case "ruled":
                    Lines(svg, line, width, height, 28, 60);
                    Line(svg, margin, 64, 0, 64, height, 1);
                    break;
                case "grid":
                    Lines(svg, line, width, height, 20, 0);
                    for (var x = 20.0; x < width; x += 20)
                    {
                        Line(svg, line, x, 0, x, height, 0.6);
                    }

                    break;
                case "dotted":
                    for (var y = 20.0; y < height; y += 20)
                    {
                        for (var x = 20.0; x < width; x += 20)
                        {
                            svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{Num(x)}\" cy=\"{Num(y)}\" r=\"0.9\" fill=\"#b3b3b3\" stroke=\"none\"/>");
                        }
                    }

                    break;
                case "cornell":
                    Lines(svg, line, width, height, 28, 90);
                    Line(svg, margin, 170, 70, 170, height - 150, 1);
                    Line(svg, margin, 0, height - 150, width, height - 150, 1);
                    break;
            }
        }

        private static void Lines(StringBuilder svg, string color, double width, double height, double spacing, double top)
        {
            for (var y = Math.Max(top, spacing); y < height; y += spacing)
            {
                Line(svg, color, 0, y, width, y, 0.6);
            }
        }

        private static void Line(StringBuilder svg, string color, double x1, double y1, double x2, double y2, double weight)
            => svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{Num(x1)}\" y1=\"{Num(y1)}\" x2=\"{Num(x2)}\" y2=\"{Num(y2)}\" stroke=\"{color}\" stroke-width=\"{Num(weight)}\"/>");

        private static void Stroke(StringBuilder svg, StrokeInfo stroke)
        {
            if (stroke.Points.Count == 0)
            {
                return;
            }

            var marker = stroke.Tool == "marker";
            var path = new StringBuilder();
            path.Append(CultureInfo.InvariantCulture, $"M{Num(stroke.Points[0].X)} {Num(stroke.Points[0].Y)}");
            if (stroke.Points.Count == 1)
            {
                // A tap leaves a dot: a zero-length line with round caps.
                path.Append(CultureInfo.InvariantCulture, $" L{Num(stroke.Points[0].X)} {Num(stroke.Points[0].Y)}");
            }

            for (var index = 1; index < stroke.Points.Count; index++)
            {
                path.Append(CultureInfo.InvariantCulture, $" L{Num(stroke.Points[index].X)} {Num(stroke.Points[index].Y)}");
            }

            svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{path}\" stroke=\"{SafeColor(stroke.Color)}\" stroke-width=\"{Num(Math.Clamp(stroke.Width, 0.5, 60))}\"{(marker ? " stroke-opacity=\"0.45\"" : "")}/>");
        }

        // A colour from the data may only be a hex colour: it goes into markup, so nothing else is allowed through.
        internal static string SafeColor(string? color)
            => color is { Length: 7 } && color[0] == '#' && color[1..].All(char.IsAsciiHexDigit) ? color : "#000000";

        private static string Num(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
