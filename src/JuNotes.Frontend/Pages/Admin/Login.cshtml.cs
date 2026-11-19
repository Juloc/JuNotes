using System.Security.Claims;
using JuNotes.Frontend.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Admin
{
    public sealed class LoginModel(AdminAccess access) : PageModel
    {
        public bool Enabled => access.Enabled;

        public string? Failure { get; private set; }

        public string ReturnUrl { get; private set; } = "/Admin";

        public void OnGet(string? returnUrl) => ReturnUrl = SafeReturn(returnUrl);

        public async Task<IActionResult> OnPostAsync(string? password, string? returnUrl)
        {
            ReturnUrl = SafeReturn(returnUrl);
            var address = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            switch (access.TryLogin(password, address))
            {
                case LoginResult.Accepted:
                    await HttpContext.SignInAsync(AdminSetup.Scheme, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], AdminSetup.Scheme)));
                    return LocalRedirect(ReturnUrl);
                case LoginResult.LockedOut:
                    Failure = "Zu viele Versuche. Warte zehn Minuten.";
                    break;
                case LoginResult.Disabled:
                    Failure = "Der Admin-Bereich ist noch nicht eingerichtet.";
                    break;
                default:
                    Failure = "Das Passwort stimmt nicht.";
                    break;
            }

            // The same answer time whatever was wrong, and a failed try is never quicker than a right one.
            await Task.Delay(400);
            return Page();
        }

        // Only a path on this server is accepted as the place to go afterwards, so a link cannot send the admin elsewhere.
        internal static string SafeReturn(string? returnUrl)
            => !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.Contains('\\', StringComparison.Ordinal) ? returnUrl : "/Admin";
    }
}
