namespace JuNotes.Backend.Library
{
    public static class CoverStyle
    {
        // The background of a notebook's cover for the shelf in the web view: only checked colours and a typed id go into the style.
        public static string Css(NotebookInfo notebook, string fallbackColor)
        {
            var fallback = PageRenderer.SafeColor(fallbackColor, "#8e8e93");
            if (notebook.Cover is not { } cover)
            {
                return $"background:{fallback}";
            }

            var a = PageRenderer.SafeColor(cover.ColorA, fallback);
            var b = PageRenderer.SafeColor(cover.ColorB, a);
            return cover.Style switch
            {
                "image" when cover.AssetId is { } asset => $"background:url(/Notes/Asset/{asset}) center/cover, {a}",
                "color" => $"background:{a}",
                _ => $"background:linear-gradient(135deg,{a},{b})",
            };
        }
    }
}
