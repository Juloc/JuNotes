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

        public IReadOnlyList<string> Destinations => backups.Mirror?.Names ?? [];

        public bool Encrypts => backups.Mirror?.Encrypts ?? false;

        public IReadOnlyList<BackupMirror.Result> LastResults => backups.Mirror?.Last ?? [];

        // The key of the server decrypts the accounts' second factors in every dump. Where it is kept is said on the page: with an encrypted copy, or nowhere but the server.
        public string KeyState => backups.Options.KeyFile is not { } file || !System.IO.File.Exists(file)
            ? "none"
            : backups.Mirror is { IsActive: true, Encrypts: true } ? "with-copies" : "nowhere";

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
