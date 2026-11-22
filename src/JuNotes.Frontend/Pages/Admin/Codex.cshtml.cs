using JuNotes.Backend.Ai;
using JuNotes.Frontend.Admin;
using JuNotes.Integrations.Codex;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Admin
{
    public sealed class CodexModel(CodexAccount account, CodexLoginSession session, AiSettingsStore settingsStore, AiOptions ai, AiUsageStore usage, TimeProvider time) : PageModel
    {
        public CodexStatus Status { get; private set; } = new(false, null, false, null);

        public DeviceCode? Waiting { get; private set; }

        public AiSettings Settings { get; private set; } = new(null, null, null);

        public int Limit => Settings.DailyLimit ?? ai.DailyLimit;

        public IReadOnlyList<AiUsage> Recent { get; private set; } = [];

        public AiUsage Today { get; private set; } = new(default, 0, 0);

        public async Task OnGetAsync(CancellationToken ct)
        {
            Settings = await settingsStore.GetAsync(ct);
            var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            Recent = await usage.RecentAsync(14, ct);
            Today = Recent.FirstOrDefault(item => item.Day == day) ?? new AiUsage(day, 0, 0);
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

        public async Task<IActionResult> OnPostSettingsAsync(string? model, string? effort, string? dailyLimit, CancellationToken ct)
        {
            int? limit = null;
            if (!string.IsNullOrWhiteSpace(dailyLimit))
            {
                if (!int.TryParse(dailyLimit.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    TempData["Error"] = "Das Tageslimit muss eine ganze Zahl sein.";
                    return RedirectToPage();
                }

                limit = parsed;
            }

            var saved = await settingsStore.SaveAsync(new AiSettings(model, effort, limit), ct);
            TempData[saved ? "Message" : "Error"] = saved
                ? "Gespeichert. Es gilt für die nächste Anfrage."
                : $"Das Modell darf nur aus Buchstaben, Ziffern und . _ : / - bestehen, der Aufwand muss aus der Liste stammen und das Tageslimit zwischen 1 und {AiSettingsRules.MaxDailyLimit} liegen.";
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
