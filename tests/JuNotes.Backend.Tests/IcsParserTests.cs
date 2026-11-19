using JuNotes.Backend.School;

namespace JuNotes.Backend.Tests
{
    public class IcsParserTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        private static string Calendar(params string[] events) => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//EN\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

        private static string Event(params string[] lines) => "BEGIN:VEVENT\r\n" + string.Concat(lines.Select(line => line + "\r\n")) + "END:VEVENT\r\n";

        [Fact]
        public void A_lesson_with_a_time_zone_keeps_its_exact_time()
        {
            var result = IcsParser.Parse(Calendar(Event("UID:a1", "DTSTART;TZID=Europe/Berlin:20261119T080000", "DTEND;TZID=Europe/Berlin:20261119T084500", "SUMMARY:Mathematik", "LOCATION:R 204")), Berlin);

            var lesson = Assert.Single(result.Events);
            Assert.Equal(new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.FromHours(1)), lesson.Start);
            Assert.Equal(new DateTimeOffset(2026, 11, 19, 8, 45, 0, TimeSpan.FromHours(1)), lesson.End);
            Assert.Equal("Mathematik", lesson.Summary);
            Assert.Equal("R 204", lesson.Location);
            Assert.False(lesson.Cancelled);
            Assert.False(lesson.AllDay);
        }

        [Fact]
        public void A_utc_time_is_converted_exactly()
        {
            var lesson = Assert.Single(IcsParser.Parse(Calendar(Event("UID:a", "DTSTART:20261119T070000Z", "DTEND:20261119T074500Z", "SUMMARY:Deutsch")), Berlin).Events);

            Assert.Equal(new DateTimeOffset(2026, 11, 19, 7, 0, 0, TimeSpan.Zero), lesson.Start);
        }

        [Fact]
        public void A_floating_time_is_read_in_the_schools_zone_and_follows_summer_time()
        {
            var winter = Assert.Single(IcsParser.Parse(Calendar(Event("UID:w", "DTSTART:20261119T080000", "DTEND:20261119T084500", "SUMMARY:x")), Berlin).Events);
            var summer = Assert.Single(IcsParser.Parse(Calendar(Event("UID:s", "DTSTART:20260701T080000", "DTEND:20260701T084500", "SUMMARY:x")), Berlin).Events);

            Assert.Equal(TimeSpan.FromHours(1), winter.Start.Offset);
            Assert.Equal(TimeSpan.FromHours(2), summer.Start.Offset);
        }

        [Fact]
        public void An_unknown_zone_name_falls_back_to_the_schools_zone()
        {
            var lesson = Assert.Single(IcsParser.Parse(Calendar(Event("UID:a", "DTSTART;TZID=W. Europe Standard Time:20261119T080000", "DTEND;TZID=W. Europe Standard Time:20261119T084500", "SUMMARY:x")), Berlin).Events);

            Assert.Equal(TimeSpan.FromHours(1), lesson.Start.Offset);
        }

        [Fact]
        public void An_all_day_event_is_marked_and_spans_the_day()
        {
            var holiday = Assert.Single(IcsParser.Parse(Calendar(Event("UID:h", "DTSTART;VALUE=DATE:20261224", "DTEND;VALUE=DATE:20261225", "SUMMARY:Weihnachtsferien")), Berlin).Events);

            Assert.True(holiday.AllDay);
            Assert.Equal(TimeSpan.FromDays(1), holiday.End - holiday.Start);
        }

        [Fact]
        public void A_cancelled_lesson_is_marked()
        {
            var lesson = Assert.Single(IcsParser.Parse(Calendar(Event("UID:c", "DTSTART:20261119T070000Z", "DTEND:20261119T074500Z", "SUMMARY:Englisch", "STATUS:CANCELLED")), Berlin).Events);

            Assert.True(lesson.Cancelled);
        }

        [Fact]
        public void Folded_lines_and_escapes_are_read_as_one_text()
        {
            // Raw string: the backslashes are the file's, as an iCalendar writes them. A line break in the file is CRLF, a fold is CRLF and a space.
            var text = """
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                UID:f
                DTSTART:20261119T070000Z
                DTEND:20261119T074500Z
                SUMMARY:Klassenarbeit\,
                  Mathe
                DESCRIPTION:Erste Zeile\nzweite
                  Zeile mit Leerzeichen\; und \\ Rest
                END:VEVENT
                END:VCALENDAR
                """.Replace("\n", "\r\n", StringComparison.Ordinal);

            var lesson = Assert.Single(IcsParser.Parse(text, Berlin).Events);

            Assert.Equal("Klassenarbeit, Mathe", lesson.Summary);
            Assert.Equal("Erste Zeile" + "\n" + "zweite Zeile mit Leerzeichen; und " + "\\" + " Rest", lesson.Description);
        }

        [Fact]
        public void A_backslash_before_an_n_is_not_a_line_break()
        {
            var lesson = Assert.Single(IcsParser.Parse(Calendar(Event("UID:b", "DTSTART:20261119T070000Z", "DTEND:20261119T074500Z", "SUMMARY:C:\\\\neu")), Berlin).Events);

            Assert.Equal("C:\\neu", lesson.Summary);
        }

        [Fact]
        public void A_colon_inside_a_quoted_parameter_does_not_end_the_name()
        {
            var lesson = Assert.Single(IcsParser.Parse(Calendar(Event("UID:q", "DTSTART;X-NOTE=\"a:b\":20261119T070000Z", "DTEND:20261119T074500Z", "SUMMARY:x")), Berlin).Events);

            Assert.Equal(new DateTimeOffset(2026, 11, 19, 7, 0, 0, TimeSpan.Zero), lesson.Start);
        }

        [Fact]
        public void Recurring_and_broken_events_are_skipped_and_counted()
        {
            var result = IcsParser.Parse(Calendar(
                Event("UID:r", "DTSTART:20261119T070000Z", "DTEND:20261119T074500Z", "SUMMARY:wöchentlich", "RRULE:FREQ=WEEKLY"),
                Event("UID:n", "SUMMARY:ohne Beginn"),
                Event("UID:d", "DTSTART:nonsense", "SUMMARY:kaputt"),
                Event("UID:ok", "DTSTART:20261119T070000Z", "DTEND:20261119T074500Z", "SUMMARY:gut")), Berlin);

            Assert.Equal("gut", Assert.Single(result.Events).Summary);
            Assert.Equal(3, result.Skipped);
        }

        [Fact]
        public void An_end_before_the_start_is_pulled_up_to_the_start()
        {
            var lesson = Assert.Single(IcsParser.Parse(Calendar(Event("UID:e", "DTSTART:20261119T080000Z", "DTEND:20261119T070000Z", "SUMMARY:x")), Berlin).Events);

            Assert.Equal(lesson.Start, lesson.End);
        }

        [Fact]
        public void Garbage_gives_no_events_and_no_crash()
        {
            Assert.Empty(IcsParser.Parse("this is not a calendar", Berlin).Events);
            Assert.Empty(IcsParser.Parse("", Berlin).Events);
        }
    }
}
