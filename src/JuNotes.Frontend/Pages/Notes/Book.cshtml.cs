using JuNotes.Backend.Library;
using JuNotes.Frontend.Admin;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Notes
{
    public sealed record RenderedPage(PageInfo Info, string Svg);

    public sealed class BookModel(LibraryReader library) : PageModel
    {
        public NotebookInfo? Notebook { get; private set; }

        public IReadOnlyList<RenderedPage> Pages { get; private set; } = [];

        public async Task OnGetAsync(Guid id, CancellationToken ct)
        {
            var mine = library.Of(User.PersonId());
            Notebook = await mine.NotebookAsync(id, ct);
            if (Notebook is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var rendered = new List<RenderedPage>();
            foreach (var page in await mine.PagesAsync(id, ct))
            {
                // A scan or PDF page has no printed lines of its own; what she wrote on it is drawn over the sheet.
                var template = page.ContentType == "drawing" ? page.Template ?? Notebook.Template : "blank";
                rendered.Add(new RenderedPage(page, PageRenderer.Svg(Notebook.Type, template, await mine.StrokesAsync(page.Id, ct), await mine.ObjectsAsync(page.Id, ct))));
            }

            Pages = rendered;
        }
    }
}
