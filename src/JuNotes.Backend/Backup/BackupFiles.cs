using System.Globalization;

namespace JuNotes.Backend.Backup
{
    public enum BackupKind
    {
        // The nightly one.
        Automatic,

        // Taken right before a migration changes the schema.
        BeforeMigration,

        // "Jetzt sichern" in the admin area; only ever deleted by hand.
        Manual,
    }

    public sealed record BackupFile(string Path, DateTimeOffset At, BackupKind Kind, long Bytes);

    // Backups are plain files named junotes-yyyyMMdd-HHmmss-kind.dump (UTC), so the folder is the whole inventory.
    public static class BackupFiles
    {
        private const string Prefix = "junotes-";
        private const string Extension = ".dump";
        private const string TimeFormat = "yyyyMMdd-HHmmss";

        public static string NameFor(DateTimeOffset at, BackupKind kind)
            => Prefix + at.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture) + "-" + Slug(kind) + Extension;

        public static bool TryParse(string fileName, out DateTimeOffset at, out BackupKind kind)
        {
            at = default;
            kind = default;
            if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) || !fileName.EndsWith(Extension, StringComparison.Ordinal))
            {
                return false;
            }

            var body = fileName[Prefix.Length..^Extension.Length];
            var split = body.LastIndexOf('-');
            if (split <= 0)
            {
                return false;
            }

            var found = Enum.GetValues<BackupKind>().Where(item => Slug(item) == body[(split + 1)..]).ToArray();
            if (found.Length != 1 || !DateTime.TryParseExact(body[..split], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            {
                return false;
            }

            at = new DateTimeOffset(time, TimeSpan.Zero);
            kind = found[0];
            return true;
        }

        // The scanned files that belong to a dump: junotes-…-kind.dump has junotes-…-kind.assets.zip beside it.
        public static string AssetsArchiveFor(string dumpPath) => dumpPath[..^Extension.Length] + ".assets.zip";

        // The key of the server, as it goes along with a copy: the name of the dump's file without its ending, then .key (a file name only, no folder).
        public static string KeyFileFor(string dumpPath) => Path.GetFileName(dumpPath)[..^Extension.Length] + ".key";

        // Newest first. Files that are not ours, and half-written ones, are ignored.
        public static IReadOnlyList<BackupFile> List(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var files = new List<BackupFile>();
            foreach (var info in new DirectoryInfo(directory).EnumerateFiles("*" + Extension))
            {
                if (TryParse(info.Name, out var at, out var kind))
                {
                    files.Add(new BackupFile(info.FullName, at, kind, info.Length));
                }
            }

            return files.OrderByDescending(item => item.At).ToList();
        }

        // Keeps the newest `keep` automatic and before-migration backups together; manual ones are never touched. Returns what it deleted.
        public static IReadOnlyList<BackupFile> Rotate(string directory, int keep)
        {
            var removable = List(directory).Where(item => item.Kind != BackupKind.Manual).Skip(Math.Max(keep, 1)).ToList();
            foreach (var file in removable)
            {
                File.Delete(file.Path);
                File.Delete(AssetsArchiveFor(file.Path));
            }

            return removable;
        }

        private static string Slug(BackupKind kind) => kind switch
        {
            BackupKind.Automatic => "automatic",
            BackupKind.BeforeMigration => "migration",
            BackupKind.Manual => "manual",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
