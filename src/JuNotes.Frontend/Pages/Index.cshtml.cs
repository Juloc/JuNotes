using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages
{
    // The front door leads to her notes, and the login page if she is not signed in.
    public sealed class IndexModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("/Notes/Index");
    }
}
