using System.Globalization;

namespace JuNotes.Frontend.Admin
{
    // Dates and sizes as the admin reads them: German, in the server's local time.
    public static class AdminFormat
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        public static string Time(DateTimeOffset at) => at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", German);

        public static string Day(DateOnly day) => day.ToString("dd.MM.yyyy", German);

        public static string Kilobytes(long bytes) => (bytes / 1024).ToString("N0", German) + " KB";
    }
}
