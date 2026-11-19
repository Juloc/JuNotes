using JuNotes.Backend.Assets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Notes
{
    // The scans and PDFs, for the signed-in reader. The same ids the iPad uses.
    public sealed class AssetModel(AssetService assets) : PageModel
    {
        public IActionResult OnGet(Guid id)
        {
            if (assets.Open(id) is not { } file)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, max-age=86400";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(file.Stream, file.ContentType);
        }
    }
}
