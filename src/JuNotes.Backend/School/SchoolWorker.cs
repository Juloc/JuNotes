using Microsoft.Extensions.Hosting;

namespace JuNotes.Backend.School
{
    // Fetches the timetable and the news every half hour (School:IntervalMinutes), and once shortly after the start.
    // The import itself catches and records every failure, so a bad day at the school's website never stops this loop or the server.
    public sealed class SchoolWorker(SchoolImportService import, SchoolOptions options, TimeProvider time) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!import.IsConfigured)
            {
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(5, options.IntervalMinutes));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), time, stoppingToken);
                using var timer = new PeriodicTimer(interval, time);
                do
                {
                    await import.ImportAsync(stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Stopping.
            }
        }
    }
}
