using System.Net;
using JuNotes.Backend.School;

namespace JuNotes.Backend.Tests
{
    public class FeedFetcherTests
    {
        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
        }

        private static FeedFetcher Fetcher(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new HttpClient(new Handler(respond)));

        [Fact]
        public void Webcal_is_read_as_https()
            => Assert.Equal("https://u.example/cal.ics", FeedFetcher.Normalize("webcal://u.example/cal.ics").ToString());

        [Theory]
        [InlineData("ftp://x.example/a")]
        [InlineData("file:///etc/passwd")]
        [InlineData("not a url")]
        [InlineData("")]
        public void Other_addresses_are_refused(string url) => Assert.Throws<FeedFetchException>(() => FeedFetcher.Normalize(url));

        [Fact]
        public async Task The_body_comes_back_as_text()
        {
            var text = await Fetcher(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("BEGIN:VCALENDAR") }).GetStringAsync("https://u.example/c.ics", CancellationToken.None);

            Assert.Equal("BEGIN:VCALENDAR", text);
        }

        [Fact]
        public async Task An_error_status_is_explained()
        {
            var error = await Assert.ThrowsAsync<FeedFetchException>(() => Fetcher(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)).GetStringAsync("https://u.example/c.ics", CancellationToken.None));

            Assert.Contains("403", error.Message);
        }

        [Fact]
        public async Task An_answer_over_five_megabytes_is_refused()
        {
            var big = new string('x', FeedFetcher.MaxBytes + 10);

            await Assert.ThrowsAsync<FeedFetchException>(() => Fetcher(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(big) }).GetStringAsync("https://u.example/c.ics", CancellationToken.None));
        }

        [Fact]
        public async Task A_network_failure_is_a_fetch_failure()
        {
            var failing = new FeedFetcher(new HttpClient(new Handler(_ => throw new HttpRequestException("no route"))));

            var error = await Assert.ThrowsAsync<FeedFetchException>(() => failing.GetStringAsync("https://u.example/c.ics", CancellationToken.None));

            Assert.Contains("could not be reached", error.Message);
        }
    }
}
