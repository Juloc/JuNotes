namespace JuNotes.Backend.School
{
    // Everything is optional: without addresses the school features are simply off.
    public sealed class SchoolOptions
    {
        // The timetable as an iCal link, which WebUntis offers under "Stundenplan abonnieren" (webcal:// is read as https://).
        public string? TimetableIcsUrl { get; set; }

        // The school website's news as an RSS or Atom feed.
        public string? NewsFeedUrl { get; set; }

        public string TimeZone { get; set; } = "Europe/Berlin";

        public int IntervalMinutes { get; set; } = 30;

        public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
    }
}
