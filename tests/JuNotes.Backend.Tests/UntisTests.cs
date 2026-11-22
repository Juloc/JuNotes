using System.Net;
using System.Text;
using System.Text.Json;
using JuNotes.Backend.School;
using Microsoft.Extensions.Logging.Abstractions;

namespace JuNotes.Backend.Tests
{
    public class UntisTests
    {
        private sealed class FakeUntis(Func<string, string?, string> answer) : HttpMessageHandler
        {
            public List<(string Method, string? Cookie, Uri Url, string Body)> Calls { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                var method = JsonElement.Parse(body).GetProperty("method").GetString()!;
                var cookie = request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : null;
                Calls.Add((method, cookie, request.RequestUri!, body));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer(method, cookie), Encoding.UTF8, "application/json") };
            }
        }

        private static SchoolOptions Options() => new()
        {
            Untis = { Server = "https://xyz.webuntis.com/WebUntis/", School = "Meine Schule", User = "anna", Password = "geheim&sicher" },
        };

        private const string Timetable = """
            {"result":[
              {"id":11,"date":20261123,"startTime":800,"endTime":845,"su":[{"id":1,"name":"M","longname":"Mathematik"}],"ro":[{"id":5,"name":"R204"}],"te":[{"id":9,"name":"MUE"}]},
              {"id":12,"date":20261123,"startTime":955,"endTime":1040,"su":[{"id":2,"name":"E","longname":"Englisch"}],"ro":[{"id":6,"name":"R118"}],"code":"cancelled"},
              {"id":13,"date":20261124,"startTime":800,"endTime":845,"su":[{"id":3,"name":"D","longname":"Deutsch"}],"ro":[{"id":7,"name":"R112"}],"code":"irregular","substText":"Raumwechsel"},
              {"id":14,"date":20261126,"startTime":800,"endTime":930,"lstype":"ex","su":[{"id":1,"name":"M","longname":"Mathematik"}]},
              {"id":15,"date":20261124,"startTime":1040,"endTime":1055,"lstype":"bs","su":[]}
            ]}
            """;

        private static string Answer(string method, string? cookie) => method switch
        {
            "authenticate" => """{"result":{"sessionId":"S3SS","personType":5,"personId":4711,"klasseId":42}}""",
            "getTimetable" => cookie == "JSESSIONID=S3SS" ? Timetable : """{"error":{"code":-8520,"message":"Not authenticated"}}""",
            "getHolidays" => """{"result":[{"id":3,"name":"Wi","longName":"Weihnachtsferien","startDate":20261222,"endDate":20270108}]}""",
            _ => """{"result":{}}""",
        };

        private static UntisClient Client(FakeUntis handler, SchoolOptions? options = null) => new(new HttpClient(handler), options ?? Options());

        [Fact]
        public async Task The_timetable_becomes_lessons_exams_cancellations_and_holidays()
        {
            var handler = new FakeUntis(Answer);

            var events = await Client(handler).FetchAsync(new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 31), CancellationToken.None);

            Assert.Equal(["Mathematik", "Englisch", "Deutsch", "Klassenarbeit Mathematik", "Weihnachtsferien"], events.Select(item => item.Summary));
            var math = events[0];
            Assert.Equal("R204", math.Location);
            Assert.Equal(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(1)), math.Start);
            Assert.Equal(new DateTimeOffset(2026, 11, 23, 8, 45, 0, TimeSpan.FromHours(1)), math.End);
            Assert.True(events[1].Cancelled);
            Assert.False(events[0].Cancelled);
            Assert.Contains("Raumwechsel", events[2].Description, StringComparison.Ordinal);
            Assert.Contains("Änderung", events[2].Description, StringComparison.Ordinal);
            Assert.True(events[4].AllDay);
            Assert.Equal(new DateTimeOffset(2027, 1, 9, 0, 0, 0, TimeSpan.FromHours(1)), events[4].End);
        }

        [Fact]
        public async Task The_calls_run_in_order_the_session_is_sent_and_the_password_goes_nowhere_but_the_login()
        {
            var handler = new FakeUntis(Answer);
            await Client(handler).FetchAsync(new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 31), CancellationToken.None);

            Assert.Equal(["authenticate", "getTimetable", "getHolidays", "logout"], handler.Calls.Select(call => call.Method));
            Assert.Null(handler.Calls[0].Cookie);
            Assert.All(handler.Calls.Skip(1), call => Assert.Equal("JSESSIONID=S3SS", call.Cookie));
            Assert.All(handler.Calls, call => Assert.Equal("https://xyz.webuntis.com/WebUntis/jsonrpc.do?school=Meine%20Schule", call.Url.AbsoluteUri));
            Assert.Equal("geheim&sicher", JsonElement.Parse(handler.Calls[0].Body).GetProperty("params").GetProperty("password").GetString());
            Assert.All(handler.Calls.Skip(1), call => Assert.DoesNotContain("geheim", call.Body, StringComparison.Ordinal));
            Assert.All(handler.Calls, call => Assert.DoesNotContain("geheim", call.Url.ToString(), StringComparison.Ordinal));
            Assert.Contains("\"startDate\":20261120", handler.Calls[1].Body, StringComparison.Ordinal);
            Assert.Contains("\"id\":4711", handler.Calls[1].Body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_school_that_forbids_holidays_does_not_lose_the_timetable()
        {
            var handler = new FakeUntis((method, cookie) => method == "getHolidays" ? """{"error":{"code":-8509,"message":"no right"}}""" : Answer(method, cookie));

            var events = await Client(handler).FetchAsync(new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 31), CancellationToken.None);

            Assert.Equal(4, events.Count);
        }

        [Fact]
        public async Task A_wrong_password_is_explained_without_repeating_the_login()
        {
            var handler = new FakeUntis((_, _) => """{"error":{"code":-8504,"message":"bad credentials for anna"}}""");

            var failure = await Assert.ThrowsAsync<FeedFetchException>(() => Client(handler).FetchAsync(new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 31), CancellationToken.None));

            Assert.Contains("Passwort", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("anna", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("geheim", failure.Message, StringComparison.Ordinal);
            Assert.Equal(["authenticate"], handler.Calls.Select(call => call.Method));
        }

        [Fact]
        public async Task A_school_that_does_not_allow_the_timetable_says_so_and_still_signs_out()
        {
            var handler = new FakeUntis((method, cookie) => method == "getTimetable" ? """{"error":{"code":-8509,"message":"no right for getTimetable()"}}""" : Answer(method, cookie));

            var failure = await Assert.ThrowsAsync<FeedFetchException>(() => Client(handler).FetchAsync(new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 31), CancellationToken.None));

            Assert.Contains("nicht", failure.Message, StringComparison.Ordinal);
            Assert.Equal("logout", handler.Calls[^1].Method);
        }

        [Fact]
        public async Task An_answer_that_is_not_json_means_the_server_or_the_school_name_is_wrong()
        {
            var handler = new FakeUntis((_, _) => "<html>Login</html>");

            var failure = await Assert.ThrowsAsync<FeedFetchException>(() => Client(handler).FetchAsync(new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 31), CancellationToken.None));

            Assert.Contains("Schulname", failure.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("xyz.webuntis.com", "xyz.webuntis.com")]
        [InlineData("https://XYZ.webuntis.com/WebUntis/index.do", "xyz.webuntis.com")]
        [InlineData("  http://neilo.webuntis.com:443/  ", "neilo.webuntis.com:443")]
        public void The_server_may_be_given_as_a_host_or_a_whole_address(string given, string expected) => Assert.Equal(expected, UntisClient.NormalizeHost(given));

        [Theory]
        [InlineData("")]
        [InlineData("user@evil.example")]
        [InlineData("a b.example")]
        [InlineData("localhost:99999999")]
        [InlineData("..")]
        public void Anything_that_is_not_a_plain_host_is_refused(string given) => Assert.Throws<FeedFetchException>(() => UntisClient.NormalizeHost(given));

        [Fact]
        public async Task Without_a_complete_login_nothing_is_called()
        {
            var handler = new FakeUntis(Answer);
            var options = new SchoolOptions { Untis = { Server = "xyz.webuntis.com", School = "s", User = "anna" } };

            await Assert.ThrowsAsync<FeedFetchException>(() => Client(handler, options).FetchAsync(new DateOnly(2026, 11, 20), new DateOnly(2027, 1, 31), CancellationToken.None));

            Assert.Empty(handler.Calls);
            Assert.False(options.Untis.IsConfigured);
        }

        private sealed class FakeSource(IReadOnlyList<IcsEvent> events) : IUntisSource
        {
            public Task<IReadOnlyList<IcsEvent>> FetchAsync(DateOnly from, DateOnly until, CancellationToken ct) => Task.FromResult(events);
        }

        [RequiresDatabaseFact]
        public async Task The_import_prefers_webuntis_over_an_ical_link_and_stores_what_it_got()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var lesson = new IcsEvent("untis-1", new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.FromHours(1)), new DateTimeOffset(2026, 11, 23, 8, 45, 0, TimeSpan.FromHours(1)), "Mathematik", "R204", null, false, false);
            var options = Options();
            options.TimetableIcsUrl = "https://u.example/never-asked.ics";
            var service = new SchoolImportService(options, new ThrowingFetcher(), new SchoolStore(database.Data), TimeProvider.System, NullLogger<SchoolImportService>.Instance, new FakeSource([lesson]));

            var status = await service.ImportAsync(CancellationToken.None);

            Assert.Null(status.LastError);
            Assert.True(status.TimetableConfigured);
            Assert.Equal(1, status.EventCount);
            Assert.Equal(1, await database.CountAsync("calendar_events"));
        }

        private sealed class ThrowingFetcher : IFeedFetcher
        {
            public Task<string> GetStringAsync(string url, CancellationToken ct) => throw new FeedFetchException("must not be asked");
        }
    }
}
