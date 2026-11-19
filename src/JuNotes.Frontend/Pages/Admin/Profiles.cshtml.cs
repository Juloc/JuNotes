using JuNotes.Backend.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Admin
{
    public sealed class ProfilesModel(AiProfileStore store) : PageModel
    {
        public IReadOnlyList<AiProfile> Profiles { get; private set; } = [];

        public async Task OnGetAsync(CancellationToken ct) => Profiles = await store.ListAsync(ct);

        public async Task<IActionResult> OnPostSaveAsync(string key, string? systemPrompt, string difficulty, string tone, string? enabled, CancellationToken ct)
        {
            var current = await store.GetAsync(key, ct);
            if (current is null)
            {
                return NotFound();
            }

            var saved = await store.UpdateAsync(current with { SystemPrompt = systemPrompt ?? "", Difficulty = difficulty, Tone = tone, Enabled = enabled == "on" }, ct);
            TempData[saved ? "Message" : "Error"] = saved ? $"„{current.Name}“ ist gespeichert." : "Die Anweisung darf nicht leer sein, und Schwierigkeit und Ton müssen aus der Liste stammen.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostResetAsync(string key, CancellationToken ct)
        {
            TempData[await store.ResetAsync(key, ct) ? "Message" : "Error"] = await store.ResetAsync(key, ct) ? "Auf den Standard zurückgesetzt." : "Dieses Profil gibt es nicht.";
            return RedirectToPage();
        }
    }
}
