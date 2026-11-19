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
    }

    public sealed class BackupFailedException(string message) : Exception(message);

    public sealed partial class BackupService(string connectionString, BackupOptions options, TimeProvider time, ILogger<BackupService> logger) : IDisposable
    {
        // One backup at a time: the nightly run and a click on "Jetzt sichern" never overlap.
        private readonly SemaphoreSlim _gate = new(1, 1);

        public BackupOptions Options => options;

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
                    File.Move(partial, final, overwrite: true);
                }
                catch
                {
                    File.Delete(partial);
                    throw;
                }

                var removed = BackupFiles.Rotate(options.Directory, options.Keep);
                var bytes = new FileInfo(final).Length;
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
