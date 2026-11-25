using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace JuNotes.Backend.Backup
{
    public sealed class BackupOptions
    {
        public string Directory { get; set; } = "backups";

        // Automatic and before-migration backups kept together; manual ones stay.
        public int Keep { get; set; } = 7;

        // Local hour of the nightly backup.
        public int Hour { get; set; } = 3;

        public string PgDump { get; set; } = "pg_dump";

        public bool BeforeMigration { get; set; } = true;

        // Scans and PDFs; zipped next to each dump. Set from Assets:Directory.
        public string? AssetsDirectory { get; set; }

        // A second folder every backup is copied to, such as a share of the NAS mounted into the container.
        public string? CopyTo { get; set; }

        // The key file of the server (the base keeps the secrets of the accounts under it). It goes along with a copy only when the copy is encrypted: next to a dump in the
        // clear it would undo the point of the key. Null when the key comes from a setting and not from a file.
        public string? KeyFile { get; set; }

        // With a passphrase, copies that leave the server are encrypted first. Without one they leave as they are.
        public string? Passphrase { get; set; }

        public DriveOptions Drive { get; set; } = new();
    }

    public sealed class BackupFailedException(string message) : Exception(message);

    public sealed partial class BackupService(string connectionString, BackupOptions options, TimeProvider time, ILogger<BackupService> logger, BackupMirror? mirror = null) : IDisposable
    {
        // One backup at a time: the nightly run and a click on "Jetzt sichern" never overlap.
        private readonly SemaphoreSlim _gate = new(1, 1);

        public BackupOptions Options => options;

        public BackupMirror? Mirror => mirror;

        public BackupFile? Latest() => BackupFiles.List(options.Directory) is { Count: > 0 } files ? files[0] : null;

        public void Dispose() => _gate.Dispose();

        public async Task<BackupFile> RunAsync(BackupKind kind, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(options.Directory);
                var at = time.GetUtcNow();
                var final = Path.Combine(options.Directory, BackupFiles.NameFor(at, kind));
                // Written under another name and renamed when done, so a crash never leaves something that looks like a backup.
                var partial = final + ".partial";
                try
                {
                    await DumpAsync(partial, ct);
                    ArchiveAssets(BackupFiles.AssetsArchiveFor(final));
                    File.Move(partial, final, overwrite: true);
                }
                catch
                {
                    File.Delete(partial);
                    throw;
                }

                var removed = BackupFiles.Rotate(options.Directory, options.Keep);
                var bytes = new FileInfo(final).Length;
                if (mirror is { IsActive: true })
                {
                    // A copy that fails is reported and logged, never allowed to fail the backup that is already safe here.
                    var files = new List<BackupMirror.MirrorFile> { new(final, Path.GetFileName(final)) };
                    if (File.Exists(BackupFiles.AssetsArchiveFor(final)))
                    {
                        files.Add(new(BackupFiles.AssetsArchiveFor(final), Path.GetFileName(BackupFiles.AssetsArchiveFor(final))));
                    }

                    // The key of the server goes with an encrypted copy, under the name of the backup (so pruning keeps and removes them together). It is encrypted straight from
                    // its file: no copy in the clear is ever made.
                    if (mirror.Encrypts && options.KeyFile is { } keyFile && File.Exists(keyFile))
                    {
                        files.Add(new(keyFile, BackupFiles.KeyFileFor(final), EncryptBesideSource: false));
                    }

                    await mirror.MirrorAsync(files, prune: kind != BackupKind.Manual, ct);
                }

                LogWritten(logger, kind, bytes, removed.Count);
                return new BackupFile(final, new DateTimeOffset(at.UtcDateTime, TimeSpan.Zero), kind, bytes);
            }
            finally
            {
                _gate.Release();
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Backup {Kind} written ({Bytes} bytes), {Removed} old removed")]
        private static partial void LogWritten(ILogger logger, BackupKind kind, long bytes, int removed);

        // The files are archived under another name first, like the dump, and only the finished archive is kept.
        private void ArchiveAssets(string archive)
        {
            if (options.AssetsDirectory is not { } directory || !System.IO.Directory.Exists(directory) || !System.IO.Directory.EnumerateFiles(directory).Any(file => !file.EndsWith(".partial", StringComparison.Ordinal)))
            {
                return;
            }

            var partial = archive + ".partial";
            try
            {
                File.Delete(partial);
                using (var zip = System.IO.Compression.ZipFile.Open(partial, System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var file in System.IO.Directory.EnumerateFiles(directory).Where(file => !file.EndsWith(".partial", StringComparison.Ordinal)))
                    {
                        System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, file, Path.GetFileName(file), System.IO.Compression.CompressionLevel.NoCompression);
                    }
                }

                File.Move(partial, archive, overwrite: true);
            }
            finally
            {
                File.Delete(partial);
            }
        }

        private async Task DumpAsync(string path, CancellationToken ct)
        {
            var source = new NpgsqlConnectionStringBuilder(connectionString);
            var start = new ProcessStartInfo(options.PgDump)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[] { "--format=custom", "--no-owner", "--file=" + path, "--host=" + source.Host, "--port=" + source.Port, "--username=" + source.Username, "--dbname=" + source.Database })
            {
                start.ArgumentList.Add(argument);
            }

            // The password goes through the environment, never onto a command line.
            if (!string.IsNullOrEmpty(source.Password))
            {
                start.Environment["PGPASSWORD"] = source.Password;
            }

            Process process;
            try
            {
                process = Process.Start(start) ?? throw new BackupFailedException("pg_dump did not start.");
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                throw new BackupFailedException($"pg_dump could not be started ({error.Message}). Install the PostgreSQL client or set Backup:PgDump.");
            }

            using (process)
            {
                var errors = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                if (process.ExitCode != 0)
                {
                    throw new BackupFailedException("pg_dump failed: " + (await errors).Trim());
                }
            }
        }
    }
}
