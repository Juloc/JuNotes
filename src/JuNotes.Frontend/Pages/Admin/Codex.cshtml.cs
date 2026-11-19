using JuNotes.Frontend.Admin;
using JuNotes.Integrations.Codex;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Admin
{
    public sealed class CodexModel(CodexAccount account, CodexLoginSession session) : PageModel
    {
        public CodexStatus Status { get; private set; } = new(false, null, false, null);

        public DeviceCode? Waiting { get; private set; }

        public async Task OnGetAsync(CancellationToken ct)
        {
            Status = await account.StatusAsync(ct);
            // A finished login shows as signed in, so the waiting code is only shown while it is still needed.
            Waiting = Status.SignedIn ? null : session.Waiting;
        }

        public async Task<IActionResult> OnPostLoginAsync(CancellationToken ct)
        {
            try
            {
                if (await session.StartAsync(account, ct) is null)
                {
                    TempData["Error"] = "Die Anmeldung konnte nicht gestartet werden: Codex hat keinen Code gezeigt.";
                }
            }
            catch (CodexUnavailableException error)
            {
                TempData["Error"] = error.Message;
            }

            return RedirectToPage();
        }

        public IActionResult OnPostCancel()
        {
            session.Cancel();
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostLogoutAsync(CancellationToken ct)
        {
            session.Cancel();
            TempData[await account.LogoutAsync(ct) ? "Message" : "Error"] = await account.StatusAsync(ct) is { SignedIn: false } ? "Abgemeldet." : "Das Abmelden hat nicht geklappt.";
            return RedirectToPage();
        }
    }
}
