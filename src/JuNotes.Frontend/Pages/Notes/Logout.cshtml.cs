using JuNotes.Frontend.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Notes
{
    public sealed class LogoutModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("/Notes/Index");

        public async Task<IActionResult> OnPostAsync()
        {
            await HttpContext.SignOutAsync(AdminSetup.Scheme);
            return RedirectToPage("/Login");
        }
    }
}
