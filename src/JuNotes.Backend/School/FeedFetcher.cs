namespace JuNotes.Backend.School
{
    public interface IFeedFetcher
    {
        Task<string> GetStringAsync(string url, CancellationToken ct);
    }

    public sealed class FeedFetchException(string message) : Exception(message);

    // Fetches a calendar or a feed. Only http and https, at most 5 MB, at most 20 seconds.
    public sealed class FeedFetcher(HttpClient client) : IFeedFetcher
    {
        public const int MaxBytes = 5 * 1024 * 1024;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        public static Uri Normalize(string url)
        {
            var trimmed = url.Trim();
            if (trimmed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "https://" + trimmed["webcal://".Length..];
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new FeedFetchException("The address must start with https:// (or webcal://).");
            }

            return uri;
        }

        public async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            var uri = Normalize(url);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Timeout);
            try
            {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, limit.Token);
                if (!response.IsSuccessStatusCode)
                {
                    throw new FeedFetchException($"The server answered {(int)response.StatusCode}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(chunk, limit.Token)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxBytes)
                    {
                        throw new FeedFetchException("The answer is larger than 5 MB.");
                    }
                }

                return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
            catch (HttpRequestException error)
            {
                throw new FeedFetchException("The server could not be reached: " + error.Message);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new FeedFetchException("The server did not answer within 20 seconds.");
            }
        }
    }
}
