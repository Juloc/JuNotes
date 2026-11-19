using System.Security.Claims;
using JuNotes.Frontend.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages
{
    // One login page for both passwords: the admin's opens everything, the reader's opens her notes.
    public sealed class ReaderLoginModel(AdminAccess admin, ReaderAccess reader) : PageModel
    {
        public bool Enabled => reader.Enabled || admin.Enabled;

        public string? Failure { get; private set; }

        public string ReturnUrl { get; private set; } = "/Notes";

        public void OnGet(string? returnUrl) => ReturnUrl = SafeReturn(returnUrl);

        public async Task<IActionResult> OnPostAsync(string? password, string? returnUrl)
        {
            ReturnUrl = SafeReturn(returnUrl);
            var address = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var asAdmin = admin.TryLogin(password, address);
            var asReader = asAdmin == LoginResult.Accepted ? LoginResult.Refused : reader.TryLogin(password, address);
            string? role = asAdmin == LoginResult.Accepted ? "admin" : asReader == LoginResult.Accepted ? "reader" : null;

            if (role is not null)
            {
                await HttpContext.SignInAsync(AdminSetup.Scheme, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role)], AdminSetup.Scheme)));
                return LocalRedirect(ReturnUrl);
            }

            Failure = asAdmin == LoginResult.LockedOut || asReader == LoginResult.LockedOut ? "Zu viele Versuche. Warte zehn Minuten." : Enabled ? "Das Passwort stimmt nicht." : "Die Web-Ansicht ist noch nicht eingerichtet.";
            await Task.Delay(400);
            return Page();
        }

        // Only a page of this web app can be the place to go afterwards.
        internal static string SafeReturn(string? returnUrl)
            => !string.IsNullOrEmpty(returnUrl) && (returnUrl.StartsWith("/Notes", StringComparison.OrdinalIgnoreCase) || returnUrl.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase))
                && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.Contains('\\', StringComparison.Ordinal) ? returnUrl : "/Notes";
    }
}
