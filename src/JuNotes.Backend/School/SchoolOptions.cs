namespace JuNotes.Backend.School
{
    // Everything is optional: without addresses the school features are simply off.
    // The login to WebUntis: the part of the address before /WebUntis (for example "xyz.webuntis.com"), the school's short name as it stands in the login address
    // (school=...), and her user name and password. The password is only ever sent to that server.
    public sealed class UntisOptions
    {
        public string? Server { get; set; }

        public string? School { get; set; }

        public string? User { get; set; }

        public string? Password { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(School) && !string.IsNullOrWhiteSpace(User) && !string.IsNullOrEmpty(Password);
    }

    public sealed class SchoolOptions
    {
        // The timetable as an iCal link, which WebUntis offers under "Stundenplan abonnieren" (webcal:// is read as https://).
        public string? TimetableIcsUrl { get; set; }

        // The timetable straight from WebUntis, with her own login. This works where the school offers no iCal link. If both are set, WebUntis wins.
        public UntisOptions Untis { get; } = new();

        // The school website's news as an RSS or Atom feed.
        public string? NewsFeedUrl { get; set; }

        public string TimeZone { get; set; } = "Europe/Berlin";

        public int IntervalMinutes { get; set; } = 30;

        public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
    }
}
