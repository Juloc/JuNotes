using JuNotes.Backend.Ai;
using JuNotes.Frontend.Admin;
using JuNotes.Backend.Backup;
using JuNotes.Backend.School;
using JuNotes.Backend.Service;
using JuNotes.Integrations.Codex;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Admin
{
    public sealed class IndexModel(HealthService health, BackupService backups, SchoolImportService school, AiUsageStore usage, AiOptions ai, CodexAccount codex, TimeProvider time) : PageModel
    {
        public bool DatabaseReady { get; private set; }

        public string BackupText { get; private set; } = "";

        public string BackupDot { get; private set; } = "warn";

        public SchoolImportStatus School { get; private set; } = new(false, false, null, null, 0, 0, 0);

        public string CodexText { get; private set; } = "";

        public string CodexDot { get; private set; } = "warn";

        public int Limit => ai.DailyLimit;

        public AiUsage Today { get; private set; } = new(default, 0, 0);

        public IReadOnlyList<AiUsage> Recent { get; private set; } = [];

        public async Task OnGetAsync(CancellationToken ct)
        {
            DatabaseReady = await health.IsReadyAsync(ct);
            var last = backups.Latest();
            BackupText = last is null ? "noch keines" : AdminFormat.Time(last.At) + $" ({AdminFormat.Kilobytes(last.Bytes)})";
            BackupDot = BackupSchedule.IsOverdue(last?.At, time.GetUtcNow()) ? "warn" : "ok";
            School = school.Status;

            var status = await codex.StatusAsync(ct);
            (CodexText, CodexDot) = !status.Installed ? ("Codex ist auf dem Server nicht installiert", "bad") : status.SignedIn ? ("angemeldet" + (status.SignIn == "chatgpt" ? " mit ChatGPT" : ""), "ok") : ("nicht angemeldet", "warn");

            var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            Recent = await usage.RecentAsync(7, ct);
            Today = Recent.FirstOrDefault(item => item.Day == day) ?? new AiUsage(day, 0, 0);
            Recent = Recent.Any(item => item.Day == day) ? Recent : [Today, .. Recent];
        }

        public async Task<IActionResult> OnPostSchoolAsync(CancellationToken ct)
        {
            var status = await school.ImportAsync(ct);
            TempData[status.LastError is null ? "Message" : "Error"] = status.LastError ?? "Stundenplan und News sind aktuell.";
            return RedirectToPage();
        }
    }
}
