using JuNotes.Backend.Backup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Admin
{
    public sealed class BackupsModel(BackupService backups) : PageModel
    {
        public IReadOnlyList<BackupFile> Files { get; private set; } = [];

        public int Hour => backups.Options.Hour;

        public int Keep => backups.Options.Keep;

        public void OnGet() => Files = BackupFiles.List(backups.Options.Directory);

        public async Task<IActionResult> OnPostRunAsync(CancellationToken ct)
        {
            try
            {
                var file = await backups.RunAsync(BackupKind.Manual, ct);
                TempData["Message"] = $"Gesichert ({JuNotes.Frontend.Admin.AdminFormat.Kilobytes(file.Bytes)}).";
            }
            catch (BackupFailedException error)
            {
                TempData["Error"] = error.Message;
            }

            return RedirectToPage();
        }

        public string KindName(BackupKind kind) => kind switch
        {
            BackupKind.Manual => "von Hand",
            BackupKind.BeforeMigration => "vor einer Änderung",
            _ => "nachts",
        };

        public long? AssetBytes(BackupFile file) => System.IO.File.Exists(BackupFiles.AssetsArchiveFor(file.Path)) ? new FileInfo(BackupFiles.AssetsArchiveFor(file.Path)).Length : null;
    }
}
