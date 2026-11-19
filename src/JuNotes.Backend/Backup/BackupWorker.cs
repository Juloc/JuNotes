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
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Any failure, a full disk included, is logged and retried; an unhandled one would stop the whole server.
                    LogFailed(logger, error);
                    due = time.GetUtcNow() + RetryAfterFailure;
                }
            }
        }

        [LoggerMessage(Level = LogLevel.Error, Message = "Nightly backup failed")]
        private static partial void LogFailed(ILogger logger, Exception exception);

        private DateTimeOffset NextNight() => BackupSchedule.NextRun(time.GetUtcNow(), backups.Options.Hour, time.LocalTimeZone);
    }
}
