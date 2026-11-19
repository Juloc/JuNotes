using JuNotes.Backend.School;
using Microsoft.Extensions.Logging.Abstractions;

namespace JuNotes.Backend.Tests
{
    public class SchoolImportTests
    {
        private sealed class FakeFetcher : IFeedFetcher
        {
            public Dictionary<string, string> Answers { get; } = [];

            public HashSet<string> Failing { get; } = [];

            public Task<string> GetStringAsync(string url, CancellationToken ct)
                => Failing.Contains(url) ? throw new FeedFetchException("down") : Task.FromResult(Answers[url]);
        }

        private const string TimetableUrl = "https://u.example/cal.ics";
        private const string NewsUrl = "https://s.example/feed.xml";

        private static string Lesson(string uid, string start, string summary) =>
            $"BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTART;TZID=Europe/Berlin:{start}\r\nDTEND;TZID=Europe/Berlin:{start[..8]}T084500\r\nSUMMARY:{summary}\r\nEND:VEVENT\r\n";

        private static string Calendar(params string[] events) => "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

        private static SchoolImportService Service(TestDatabase database, FakeFetcher fetcher, SchoolOptions? options = null)
            => new(options ?? new SchoolOptions { TimetableIcsUrl = TimetableUrl, NewsFeedUrl = NewsUrl }, fetcher, new SchoolStore(database.Data), TimeProvider.System, NullLogger<SchoolImportService>.Instance);

        [RequiresDatabaseFact]
        public async Task The_timetable_and_the_news_are_imported_and_can_be_read()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var fetcher = new FakeFetcher();
            fetcher.Answers[TimetableUrl] = Calendar(Lesson("1", "20261119T080000", "Mathematik"), Lesson("2", "20261120T080000", "Deutsch"));
            fetcher.Answers[NewsUrl] = "<rss><channel><item><title>Elternabend</title><guid>g</guid></item></channel></rss>";

            var status = await Service(database, fetcher).ImportAsync(CancellationToken.None);

            Assert.Null(status.LastError);
            Assert.Equal(2, status.EventCount);
            Assert.Equal(1, status.NewsCount);
            var store = new SchoolStore(database.Data);
            var lessons = await store.EventsAsync(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
            Assert.Equal("Mathematik", Assert.Single(lessons).Summary);
            Assert.Equal("Elternabend", Assert.Single(await store.NewsAsync(10, CancellationToken.None)).Title);
        }

        [RequiresDatabaseFact]
        public async Task A_second_import_replaces_the_first_so_nothing_stale_stays()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var fetcher = new FakeFetcher();
            var service = Service(database, fetcher, new SchoolOptions { TimetableIcsUrl = TimetableUrl });
            fetcher.Answers[TimetableUrl] = Calendar(Lesson("1", "20261119T080000", "Mathematik"), Lesson("2", "20261120T080000", "Deutsch"));
            await service.ImportAsync(CancellationToken.None);

            fetcher.Answers[TimetableUrl] = Calendar(Lesson("2", "20261120T080000", "Deutsch"));
            await service.ImportAsync(CancellationToken.None);

            Assert.Equal(1, await database.CountAsync("calendar_events"));
        }

        [RequiresDatabaseFact]
        public async Task A_failing_source_keeps_its_last_good_data_and_reports_the_error()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var fetcher = new FakeFetcher();
            var service = Service(database, fetcher);
            fetcher.Answers[TimetableUrl] = Calendar(Lesson("1", "20261119T080000", "Mathematik"));
            fetcher.Answers[NewsUrl] = "<rss><channel><item><title>A</title></item></channel></rss>";
            await service.ImportAsync(CancellationToken.None);

            fetcher.Failing.Add(TimetableUrl);
            var status = await service.ImportAsync(CancellationToken.None);

            Assert.Contains("Stundenplan", status.LastError);
            Assert.Equal(1, await database.CountAsync("calendar_events"));
            Assert.Equal(1, status.EventCount);
        }

        [RequiresDatabaseFact]
        public async Task One_failing_source_does_not_stop_the_other()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var fetcher = new FakeFetcher();
            fetcher.Failing.Add(NewsUrl);
            fetcher.Answers[TimetableUrl] = Calendar(Lesson("1", "20261119T080000", "Mathematik"));

            var status = await Service(database, fetcher).ImportAsync(CancellationToken.None);

            Assert.Equal(1, await database.CountAsync("calendar_events"));
            Assert.Contains("Schul-News", status.LastError);
        }

        [Fact]
        public async Task A_database_that_fails_is_reported_and_never_thrown_into_the_worker()
        {
            var fetcher = new FakeFetcher();
            fetcher.Answers[TimetableUrl] = Calendar(Lesson("1", "20261119T080000", "Mathematik"));
            var unreachable = new SchoolStore(Npgsql.NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=x;Timeout=2"));
            var service = new SchoolImportService(new SchoolOptions { TimetableIcsUrl = TimetableUrl }, fetcher, unreachable, TimeProvider.System, NullLogger<SchoolImportService>.Instance);

            var status = await service.ImportAsync(CancellationToken.None);

            Assert.Contains("Stundenplan", status.LastError);
            Assert.Contains("unexpected error", status.LastError);
            Assert.Null(status.LastSuccess);
        }

        [Fact]
        public void Without_addresses_the_service_says_it_is_not_configured()
        {
            var service = new SchoolImportService(new SchoolOptions(), new FakeFetcher(), new SchoolStore(Npgsql.NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=x")), TimeProvider.System, NullLogger<SchoolImportService>.Instance);

            Assert.False(service.IsConfigured);
            Assert.False(service.Status.TimetableConfigured);
            Assert.False(service.Status.NewsConfigured);
        }

        [RequiresDatabaseFact]
        public async Task Cancelled_and_all_day_events_are_stored_with_their_flags()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var fetcher = new FakeFetcher();
            fetcher.Answers[TimetableUrl] = Calendar(
                "BEGIN:VEVENT\r\nUID:c\r\nDTSTART:20261119T070000Z\r\nDTEND:20261119T074500Z\r\nSUMMARY:Englisch\r\nSTATUS:CANCELLED\r\nEND:VEVENT\r\n",
                "BEGIN:VEVENT\r\nUID:h\r\nDTSTART;VALUE=DATE:20261119\r\nDTEND;VALUE=DATE:20261120\r\nSUMMARY:Studientag\r\nEND:VEVENT\r\n");
            await Service(database, fetcher, new SchoolOptions { TimetableIcsUrl = TimetableUrl }).ImportAsync(CancellationToken.None);

            var events = await new SchoolStore(database.Data).EventsAsync(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

            Assert.True(events.Single(item => item.Summary == "Englisch").Cancelled);
            Assert.True(events.Single(item => item.Summary == "Studientag").AllDay);
        }
    }
}
