using JuNotes.Backend.Backup;

namespace JuNotes.Backend.Tests
{
    public class BackupScheduleTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        [Fact]
        public void Before_the_hour_it_runs_today()
        {
            // 01:30 in Berlin (UTC+1 in November) is 00:30 UTC.
            var next = BackupSchedule.NextRun(new DateTimeOffset(2026, 11, 19, 0, 30, 0, TimeSpan.Zero), 3, Berlin);

            Assert.Equal(new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.FromHours(1)), next);
        }

        [Fact]
        public void After_the_hour_it_runs_tomorrow()
        {
            var next = BackupSchedule.NextRun(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero), 3, Berlin);

            Assert.Equal(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.FromHours(1)), next);
        }

        [Fact]
        public void Exactly_on_the_hour_it_waits_for_the_next_day()
        {
            var next = BackupSchedule.NextRun(new DateTimeOffset(2026, 11, 19, 2, 0, 0, TimeSpan.Zero), 3, Berlin);

            Assert.Equal(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.FromHours(1)), next);
        }

        [Fact]
        public void Summer_time_moves_the_offset()
        {
            var next = BackupSchedule.NextRun(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), 3, Berlin);

            Assert.Equal(new DateTimeOffset(2026, 7, 2, 3, 0, 0, TimeSpan.FromHours(2)), next);
        }

        [Fact]
        public void A_spring_forward_night_without_the_hour_runs_an_hour_later()
        {
            // 29 Mar 2026: 02:00 jumps to 03:00 in Berlin, so a backup at 02:xx has to wait.
            var next = BackupSchedule.NextRun(new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero), 2, Berlin);

            Assert.Equal(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)), next);
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData(29, false)]
        [InlineData(31, true)]
        public void A_backup_is_overdue_after_thirty_hours(int? hoursAgo, bool overdue)
        {
            var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

            Assert.Equal(overdue, BackupSchedule.IsOverdue(hoursAgo is { } hours ? now.AddHours(-hours) : null, now));
        }
    }
}
