using JuNotes.Frontend.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Admin
{
    public sealed class LogoutModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("/Admin/Login");

        public async Task<IActionResult> OnPostAsync()
        {
            await HttpContext.SignOutAsync(AdminSetup.Scheme);
            return RedirectToPage("/Admin/Login");
        }
    }
}
