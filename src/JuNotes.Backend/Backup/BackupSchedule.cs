namespace JuNotes.Backend.Backup
{
    public static class BackupSchedule
    {
        // The next time the clock in `zone` shows `hour`:00 after `now`.
        public static DateTimeOffset NextRun(DateTimeOffset now, int hour, TimeZoneInfo zone)
        {
            var local = TimeZoneInfo.ConvertTime(now, zone);
            var today = new DateTime(local.Year, local.Month, local.Day, hour, 0, 0, DateTimeKind.Unspecified);
            for (var day = 0; day < 3; day++)
            {
                var candidate = today.AddDays(day);
                if (zone.IsInvalidTime(candidate))
                {
                    // The hour does not exist on a spring-forward night; the hour after it does.
                    candidate = candidate.AddHours(1);
                }

                var at = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
                if (at > now)
                {
                    return at;
                }
            }

            throw new InvalidOperationException("No backup time found within three days.");
        }

        // A server that was off at night catches up soon after it starts: the last backup is older than this.
        public static readonly TimeSpan Overdue = TimeSpan.FromHours(30);

        public static bool IsOverdue(DateTimeOffset? last, DateTimeOffset now) => last is null || now - last.Value > Overdue;
    }
}
