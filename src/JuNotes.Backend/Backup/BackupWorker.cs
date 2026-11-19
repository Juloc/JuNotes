using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JuNotes.Backend.Backup
{
    // Nightly at the configured hour, and soon after a start when the last backup is overdue.
    public sealed partial class BackupWorker(BackupService backups, TimeProvider time, ILogger<BackupWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var due = BackupSchedule.IsOverdue(backups.Latest()?.At, time.GetUtcNow()) ? time.GetUtcNow() + StartDelay : NextNight();
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(due - time.GetUtcNow() is { Ticks: > 0 } wait ? wait : TimeSpan.Zero, time, stoppingToken);
                    await backups.RunAsync(BackupKind.Automatic, stoppingToken);
                    due = NextNight();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (BackupFailedException error)
                {
                    LogFailed(logger, error.Message);
                    due = time.GetUtcNow() + RetryAfterFailure;
                }
            }
        }

        [LoggerMessage(Level = LogLevel.Error, Message = "Nightly backup failed: {Message}")]
        private static partial void LogFailed(ILogger logger, string message);

        private DateTimeOffset NextNight() => BackupSchedule.NextRun(time.GetUtcNow(), backups.Options.Hour, time.LocalTimeZone);
    }
}
