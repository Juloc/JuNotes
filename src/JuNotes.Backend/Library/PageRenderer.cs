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

        private static readonly string[] Days = ["Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag"];

        // The printed paper, drawn as in the app: ruled (three distances, with a margin), grids, dots, Cornell, a checklist or a week planner.
        internal static void Background(StringBuilder svg, string template, double width, double height)
        {
            const string line = "#c7ddf5";
            const string margin = "#f5a3a3";
            var lineSpacing = template switch { "ruled" or "cornell" => 28.0, "ruledMedium" => 20.0, "ruledFine" => 14.0, _ => 0.0 };
            var lineTop = template switch { "ruled" => 60.0, "cornell" => 90.0, "ruledMedium" or "ruledFine" => 50.0, _ => 0.0 };
            var gridSpacing = template switch { "grid" => 20.0, "gridFine" => 14.0, "gridLarge" => 28.0, _ => 0.0 };

            if (lineSpacing > 0)
            {
                Lines(svg, line, width, height, lineSpacing, lineTop);
            }

            if (template is "ruled" or "ruledMedium" or "ruledFine")
            {
                Line(svg, margin, 64, 0, 64, height, 1);
            }

            if (gridSpacing > 0)
            {
                Lines(svg, line, width, height, gridSpacing, 0);
                for (var x = gridSpacing; x < width; x += gridSpacing)
                {
                    Line(svg, line, x, 0, x, height, 0.6);
                }
            }

            switch (template)
            {
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
                    Line(svg, margin, 170, 70, 170, height - 150, 1);
                    Line(svg, margin, 0, height - 150, width, height - 150, 1);
                    break;
                case "checklist":
                    for (var y = 70.0; y < height - 30; y += 38)
                    {
                        Line(svg, line, 70, y, width - 40, y, 0.8);
                        svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"40\" y=\"{Num(y - 22)}\" width=\"18\" height=\"18\" rx=\"3\" fill=\"none\" stroke=\"#999999\" stroke-width=\"1\"/>");
                    }

                    break;
                case "planner":
                    var top = 60.0;
                    var bottom = height - 40;
                    var block = (bottom - top) / 7;
                    for (var index = 0; index < 7; index++)
                    {
                        var y = top + (block * index);
                        Line(svg, margin, 30, y, width - 30, y, 1);
                        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"36\" y=\"{Num(y + 18)}\" font-size=\"12\" font-weight=\"600\" fill=\"#8c8c8c\" stroke=\"none\">{Days[index]}</text>");
                    }

                    Line(svg, margin, 30, bottom, width - 30, bottom, 1);
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
