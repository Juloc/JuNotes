using System.Net;
using System.Text;
using System.Text.Json;
using JuNotes.Integrations.Paperless;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Tests
{
    public class PaperlessClientTests
    {
        private sealed class FakePaperless(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
        {
            public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Calls { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Calls.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
                return respond(request, body);
            }
        }

        private static readonly PaperlessOptions Options = new() { BaseUrl = "https://paperless.example/", Token = "tok" };

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static readonly byte[] Pdf = "%PDF-1.7 fake"u8.ToArray();

        [Fact]
        public async Task An_existing_tag_is_found_by_name_ignoring_case()
        {
            var handler = new FakePaperless((_, _) => Json("""{"results":[{"id":7,"name":"mathe"},{"id":8,"name":"Mathematik"}]}"""));

            var id = await new PaperlessClient(new HttpClient(handler), Options).EnsureTagAsync("Mathe", CancellationToken.None);

            Assert.Equal(7, id);
            var call = Assert.Single(handler.Calls);
            Assert.Contains("name__iexact=Mathe", call.Url, StringComparison.Ordinal);
            Assert.Equal("Token tok", call.Auth);
        }

        [Fact]
        public async Task A_missing_tag_is_created_and_its_id_used()
        {
            var handler = new FakePaperless((request, _) => request.Method == HttpMethod.Get ? Json("""{"results":[]}""") : Json("""{"id":42,"name":"Mathe"}""", HttpStatusCode.Created));

            var id = await new PaperlessClient(new HttpClient(handler), Options).EnsureTagAsync("Mathe", CancellationToken.None);

            Assert.Equal(42, id);
            Assert.Equal(HttpMethod.Post, handler.Calls[1].Method);
            Assert.EndsWith("/api/tags/", handler.Calls[1].Url, StringComparison.Ordinal);
            using var created = JsonDocument.Parse(handler.Calls[1].Body!);
            Assert.Equal("Mathe", created.RootElement.GetProperty("name").GetString());
        }

        [Fact]
        public async Task An_upload_carries_the_pdf_the_title_the_date_and_the_tag_numbers()
        {
            var handler = new FakePaperless((request, _) => request.RequestUri!.AbsolutePath switch
            {
                "/api/tags/" when request.Method == HttpMethod.Get => Json("""{"results":[{"id":7,"name":"Mathe"}]}"""),
                "/api/tags/" => Json("""{"id":9,"name":"Schule"}""", HttpStatusCode.Created),
                _ => Json("\"4c8b7e86-0000-0000-0000-000000000000\""),
            });

            var task = await new PaperlessClient(new HttpClient(handler), Options).UploadAsync(Pdf, "Bruchrechnung", new DateTimeOffset(2026, 11, 19, 8, 30, 0, TimeSpan.Zero), ["Mathe", "Schule", "mathe"], CancellationToken.None);

            Assert.Equal("4c8b7e86-0000-0000-0000-000000000000", task);
            var upload = handler.Calls.Single(call => call.Url.Contains("post_document", StringComparison.Ordinal));
            Assert.Equal(HttpMethod.Post, upload.Method);
            Assert.Contains("name=title", upload.Body, StringComparison.Ordinal);
            Assert.Contains("Bruchrechnung", upload.Body, StringComparison.Ordinal);
            Assert.Contains("2026-11-19T08:30:00", upload.Body, StringComparison.Ordinal);
            Assert.Contains("%PDF-1.7 fake", upload.Body, StringComparison.Ordinal);
            Assert.Contains("filename=Bruchrechnung.pdf", upload.Body, StringComparison.Ordinal);
            Assert.Equal(2, upload.Body!.Split("name=tags").Length - 1);
        }

        [Fact]
        public async Task A_refusal_names_the_step_but_not_the_answer_and_a_dead_server_is_a_clear_error()
        {
            var refusing = new FakePaperless((_, _) => Json("""{"detail":"user admin@example.com has no permission"}""", HttpStatusCode.Forbidden));
            var error = await Assert.ThrowsAsync<PaperlessException>(() => new PaperlessClient(new HttpClient(refusing), Options).UploadAsync(Pdf, "Titel", DateTimeOffset.UtcNow, [], CancellationToken.None));
            Assert.Contains("uploading", error.Message, StringComparison.Ordinal);
            Assert.Contains("403", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("admin@example.com", error.Message, StringComparison.Ordinal);

            var dead = new FakePaperless((_, _) => throw new HttpRequestException("no route"));
            var unreachable = await Assert.ThrowsAsync<PaperlessException>(() => new PaperlessClient(new HttpClient(dead), Options).UploadAsync(Pdf, "Titel", DateTimeOffset.UtcNow, [], CancellationToken.None));
            Assert.Contains("could not be reached", unreachable.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("Brüche: Test/1", "Brüche_ Test_1")]
        [InlineData("../../etc/passwd", ".._.._etc_passwd")]
        [InlineData("   ", "JuNotes")]
        public void A_title_becomes_a_safe_file_name(string title, string expected) => Assert.Equal(expected, PaperlessClient.SafeFileName(title));

        [Fact]
        public void Paperless_counts_as_set_up_only_with_an_address_and_a_token()
        {
            Assert.True(Options.IsConfigured);
            Assert.False(new PaperlessOptions { BaseUrl = "https://p.example" }.IsConfigured);
            Assert.False(new PaperlessOptions { Token = "t" }.IsConfigured);
            Assert.False(new PaperlessOptions { BaseUrl = "ftp://p.example", Token = "t" }.IsConfigured);
            Assert.False(new PaperlessOptions().IsConfigured);
        }
    }

    public class PaperlessEndpointTests
    {
        private static async Task<(AdminHost Host, FakeCodex Fake, TestDatabase Database)> StartAsync(bool configured)
        {
            var database = await TestDatabase.CreateAsync();
            var fake = new FakeCodex("exit 0");
            return (new AdminHost(database, fake.Executable, configured ? ("http://127.0.0.1:1", "tok") : null), fake, database);
        }

        private static HttpClient Api(AdminHost host)
        {
            var client = host.Browser();
            client.DefaultRequestHeaders.Authorization = new("Bearer", AdminHost.Token);
            return client;
        }

        [RequiresDatabaseFact]
        public async Task The_status_says_whether_paperless_is_set_up()
        {
            foreach (var configured in new[] { true, false })
            {
                var (host, fake, database) = await StartAsync(configured);
                await using (database) using (fake) using (host)
                {
                    using var status = JsonDocument.Parse(await Api(host).GetStringAsync(new Uri("/api/v1/export/status", UriKind.Relative)));

                    Assert.Equal(configured, status.RootElement.GetProperty("paperless").GetBoolean());
                }
            }
        }

        [RequiresDatabaseFact]
        public async Task Without_paperless_the_export_is_a_503_with_a_sentence()
        {
            var (host, fake, database) = await StartAsync(configured: false);
            await using (database) using (fake) using (host)
            {
                var response = await Api(host).PostAsync(new Uri("/api/v1/export/paperless?title=Heft", UriKind.Relative), new ByteArrayContent("%PDF-1.7"u8.ToArray()));

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Contains("nicht eingerichtet", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }

        [RequiresDatabaseFact]
        public async Task Only_a_pdf_with_a_title_is_taken_and_the_token_is_needed()
        {
            var (host, fake, database) = await StartAsync(configured: true);
            await using (database) using (fake) using (host)
            {
                var client = Api(host);
                var notPdf = await client.PostAsync(new Uri("/api/v1/export/paperless?title=Heft", UriKind.Relative), new ByteArrayContent("<html>"u8.ToArray()));
                var noTitle = await client.PostAsync(new Uri("/api/v1/export/paperless?title=%20", UriKind.Relative), new ByteArrayContent("%PDF-1.7"u8.ToArray()));
                var anonymous = await host.Browser().PostAsync(new Uri("/api/v1/export/paperless?title=Heft", UriKind.Relative), new ByteArrayContent("%PDF-1.7"u8.ToArray()));

                Assert.Equal(HttpStatusCode.UnsupportedMediaType, notPdf.StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            }
        }

        [RequiresDatabaseFact]
        public async Task An_unreachable_paperless_is_a_502_not_a_crash()
        {
            var (host, fake, database) = await StartAsync(configured: true);
            await using (database) using (fake) using (host)
            {
                var response = await Api(host).PostAsync(new Uri("/api/v1/export/paperless?title=Heft&tags=Mathe", UriKind.Relative), new ByteArrayContent("%PDF-1.7 x"u8.ToArray()));

                Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
                Assert.Contains("could not be reached", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }
    }
}
