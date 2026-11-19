using System.Net.Http.Headers;
using System.Text.Json;

namespace JuNotes.Integrations.Paperless
{
    public sealed class PaperlessOptions
    {
        // The address of the Paperless-ngx server, for example https://paperless.example.org
        public string? BaseUrl { get; set; }

        // An API token of a Paperless user who may add documents and tags.
        public string? Token { get; set; }

        public bool IsConfigured => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrWhiteSpace(Token);
    }

    // Paperless refused or could not be reached; the message is safe to show.
    public sealed class PaperlessException(string message) : Exception(message);

    // Sends finished notebooks to Paperless-ngx as searchable archive documents. Paperless wants tag numbers, so a tag is looked up by name or created first.
    public sealed class PaperlessClient(HttpClient client, PaperlessOptions options)
    {
        public static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(5);

        public async Task<int> EnsureTagAsync(string name, CancellationToken ct)
        {
            var found = await SendAsync(Request(HttpMethod.Get, "api/tags/?page_size=5&name__iexact=" + Uri.EscapeDataString(name)), "looking up a tag", ct);
            using (var body = JsonDocument.Parse(found))
            {
                foreach (var tag in body.RootElement.GetProperty("results").EnumerateArray())
                {
                    if (string.Equals(tag.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return tag.GetProperty("id").GetInt32();
                    }
                }
            }

            var create = Request(HttpMethod.Post, "api/tags/");
            create.Content = new StringContent(JsonSerializer.Serialize(new { name, matching_algorithm = 0 }), System.Text.Encoding.UTF8, "application/json");
            using var created = JsonDocument.Parse(await SendAsync(create, "creating a tag", ct));
            return created.RootElement.GetProperty("id").GetInt32();
        }

        // Returns the id of the task Paperless started to process the file.
        public async Task<string> UploadAsync(byte[] pdf, string title, DateTimeOffset created, IReadOnlyList<string> tags, CancellationToken ct)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(pdf);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "document", SafeFileName(title) + ".pdf");
            form.Add(new StringContent(title), "title");
            form.Add(new StringContent(created.ToString("yyyy-MM-dd'T'HH:mm:ssK", System.Globalization.CultureInfo.InvariantCulture)), "created");
            foreach (var tag in tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                form.Add(new StringContent((await EnsureTagAsync(tag.Trim(), ct)).ToString(System.Globalization.CultureInfo.InvariantCulture)), "tags");
            }

            var request = Request(HttpMethod.Post, "api/documents/post_document/");
            request.Content = form;
            var answer = await SendAsync(request, "uploading", ct);
            // Paperless answers with the task id as a bare JSON string.
            try
            {
                return JsonSerializer.Deserialize<string>(answer) ?? answer.Trim('"');
            }
            catch (JsonException)
            {
                return answer.Trim().Trim('"');
            }
        }

        // A name safe for a header and a file system.
        internal static string SafeFileName(string title)
        {
            var cleaned = new string(title.Select(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '.' ? character : '_').ToArray()).Trim();
            return cleaned.Length == 0 ? "JuNotes" : cleaned[..Math.Min(cleaned.Length, 80)];
        }

        private HttpRequestMessage Request(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, new Uri(new Uri(options.BaseUrl!.TrimEnd('/') + "/"), path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", options.Token);
            request.Headers.Accept.ParseAdd("application/json; version=5");
            return request;
        }

        // The answer's own text is left out of the error: it can name the account.
        private async Task<string> SendAsync(HttpRequestMessage request, string step, CancellationToken ct)
        {
            using (request)
            {
                try
                {
                    using var response = await client.SendAsync(request, ct);
                    var body = await response.Content.ReadAsStringAsync(ct);
                    return response.IsSuccessStatusCode ? body : throw new PaperlessException($"Paperless refused {step} ({(int)response.StatusCode}).");
                }
                catch (HttpRequestException)
                {
                    throw new PaperlessException($"Paperless could not be reached while {step}.");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new PaperlessException($"Paperless did not answer in time while {step}.");
                }
            }
        }
    }
}
