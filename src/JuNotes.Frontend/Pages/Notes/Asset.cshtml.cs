using JuNotes.Backend.Assets;
using JuNotes.Frontend.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Notes
{
    // The scans and PDFs, for the signed-in reader. The same ids the iPad uses.
    public sealed class AssetModel(AssetService assets) : PageModel
    {
        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
        {
            if (await assets.OpenAsync(id, User.PersonId(), ct) is not { } file)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "no-store";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(file.Stream, file.ContentType);
        }
    }
}
