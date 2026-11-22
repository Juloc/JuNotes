using Microsoft.Extensions.Logging;

namespace JuNotes.Backend.School
{
    public sealed record SchoolImportStatus(bool TimetableConfigured, bool NewsConfigured, DateTimeOffset? LastSuccess, string? LastError, int EventCount, int NewsCount, int SkippedEvents);

    public sealed partial class SchoolImportService(SchoolOptions options, IFeedFetcher fetcher, SchoolStore store, TimeProvider time, ILogger<SchoolImportService> logger, IUntisSource? untis = null)
    {
        private const string TimetableSource = "timetable";
        private const string NewsSource = "news";
        private SchoolImportStatus _status = new(false, false, null, null, 0, 0, 0);

        public SchoolImportStatus Status => _status with { TimetableConfigured = !string.IsNullOrWhiteSpace(options.TimetableIcsUrl) || options.Untis.IsConfigured, NewsConfigured = !string.IsNullOrWhiteSpace(options.NewsFeedUrl) };

        public bool IsConfigured => Status is { TimetableConfigured: true } or { NewsConfigured: true };

        // Each source on its own: a school website that is down does not stop the timetable. A source that fails keeps its last good data.
        public async Task<SchoolImportStatus> ImportAsync(CancellationToken ct)
        {
            string? error = null;
            var events = _status.EventCount;
            var news = _status.NewsCount;
            var skipped = _status.SkippedEvents;

            if (options.Untis.IsConfigured && untis is not null)
            {
                try
                {
                    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.Zone).DateTime);
                    var fetched = await untis.FetchAsync(today.AddDays(-7), today.AddDays(75), ct);
                    await store.ReplaceEventsAsync(TimetableSource, fetched, ct);
                    events = fetched.Count;
                    skipped = 0;
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    error = "Stundenplan: " + Describe(failure);
                    LogFailed(logger, TimetableSource, failure);
                }
            }
            else if (!string.IsNullOrWhiteSpace(options.TimetableIcsUrl))
            {
                try
                {
                    var parsed = IcsParser.Parse(await fetcher.GetStringAsync(options.TimetableIcsUrl, ct), options.Zone);
                    await store.ReplaceEventsAsync(TimetableSource, parsed.Events, ct);
                    events = parsed.Events.Count;
                    skipped = parsed.Skipped;
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    error = "Stundenplan: " + Describe(failure);
                    LogFailed(logger, TimetableSource, failure);
                }
            }

            if (!string.IsNullOrWhiteSpace(options.NewsFeedUrl))
            {
                try
                {
                    var items = FeedParser.Parse(await fetcher.GetStringAsync(options.NewsFeedUrl, ct));
                    await store.ReplaceNewsAsync(NewsSource, items, ct);
                    news = items.Count;
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    error = (error is null ? "" : error + " ") + "Schul-News: " + Describe(failure);
                    LogFailed(logger, NewsSource, failure);
                }
            }

            _status = _status with { LastSuccess = error is null ? time.GetUtcNow() : _status.LastSuccess, LastError = error, EventCount = events, NewsCount = news, SkippedEvents = skipped };
            return Status;
        }

        // The school's own failures are explained to the user; anything else is only named, and logged in full.
        private static string Describe(Exception failure) => failure is FeedFetchException ? failure.Message : "unexpected error (" + failure.GetType().Name + ")";

        [LoggerMessage(Level = LogLevel.Warning, Message = "School import of {Source} failed")]
        private static partial void LogFailed(ILogger logger, string source, Exception exception);
    }
}
