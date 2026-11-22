using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JuNotes.Backend.School
{
    public interface IUntisSource
    {
        Task<IReadOnlyList<IcsEvent>> FetchAsync(DateOnly from, DateOnly until, CancellationToken ct);
    }

    // Reads her timetable from WebUntis through its JSON-RPC interface: sign in, ask for the timetable of the signed-in student, sign out.
    // The answer becomes the same events an iCal file would, so the rest of the school features does not care where they come from.
    public sealed partial class UntisClient(HttpClient client, SchoolOptions options) : IUntisSource
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private static readonly string[] ClassFields = ["id", "name"];
        private static readonly string[] NamedFields = ["id", "name", "longname"];

        [GeneratedRegex(@"^[a-z0-9]([a-z0-9.-]*[a-z0-9])?(:\d{1,5})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex HostPattern();

        // "https://xyz.webuntis.com/WebUntis/" and "xyz.webuntis.com" both mean the same server. Anything that is not a plain host name is refused,
        // so the setting cannot be used to make the server call somewhere else.
        public static string NormalizeHost(string? value)
        {
            var host = (value ?? string.Empty).Trim();
            foreach (var prefix in new[] { "https://", "http://" })
            {
                if (host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    host = host[prefix.Length..];
                }
            }

            var slash = host.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                host = host[..slash];
            }

            return HostPattern().IsMatch(host) ? host.ToLowerInvariant() : throw new FeedFetchException("Der WebUntis-Server ist keine gültige Adresse (zum Beispiel xyz.webuntis.com).");
        }

        public async Task<IReadOnlyList<IcsEvent>> FetchAsync(DateOnly from, DateOnly until, CancellationToken ct)
        {
            var untis = options.Untis;
            if (!untis.IsConfigured)
            {
                throw new FeedFetchException("WebUntis ist nicht vollständig eingerichtet.");
            }

            var endpoint = new Uri($"https://{NormalizeHost(untis.Server)}/WebUntis/jsonrpc.do?school={Uri.EscapeDataString(untis.School!.Trim())}");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Timeout);
            string? session = null;
            try
            {
                var sign = await CallAsync(endpoint, null, "authenticate", new { user = untis.User!.Trim(), password = untis.Password, client = "JuNotes" }, limit.Token);
                session = sign.TryGetProperty("sessionId", out var id) ? id.GetString() : null;
                if (string.IsNullOrEmpty(session) || !sign.TryGetProperty("personId", out var person) || !sign.TryGetProperty("personType", out var type))
                {
                    throw new FeedFetchException("WebUntis hat die Anmeldung angenommen, aber keinen Stundenplan für diese Person genannt.");
                }

                var timetable = await CallAsync(endpoint, session, "getTimetable", new
                {
                    options = new
                    {
                        element = new { id = person.GetInt32(), type = type.GetInt32() },
                        startDate = Number(from),
                        endDate = Number(until),
                        showInfo = true,
                        showSubstText = true,
                        showLsText = true,
                        klasseFields = ClassFields,
                        roomFields = NamedFields,
                        subjectFields = NamedFields,
                        teacherFields = ClassFields,
                    },
                }, limit.Token);

                var events = new List<IcsEvent>();
                if (timetable.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in timetable.EnumerateArray())
                    {
                        if (Lesson(entry, options.Zone) is { } lesson)
                        {
                            events.Add(lesson);
                        }
                    }
                }

                // Holidays are a bonus: a school that does not allow them does not lose the timetable.
                try
                {
                    var holidays = await CallAsync(endpoint, session, "getHolidays", new { }, limit.Token);
                    if (holidays.ValueKind == JsonValueKind.Array)
                    {
                        events.AddRange(holidays.EnumerateArray().Select(item => Holiday(item, options.Zone, from, until)).OfType<IcsEvent>());
                    }
                }
                catch (FeedFetchException)
                {
                    // Left out.
                }

                return events.OrderBy(item => item.Start).ThenBy(item => item.Summary, StringComparer.Ordinal).ToList();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new FeedFetchException("WebUntis hat nicht rechtzeitig geantwortet.");
            }
            catch (HttpRequestException)
            {
                throw new FeedFetchException("WebUntis ist nicht erreichbar.");
            }
            finally
            {
                if (session is not null)
                {
                    try
                    {
                        await CallAsync(endpoint, session, "logout", new { }, CancellationToken.None);
                    }
                    catch (Exception failure) when (failure is FeedFetchException or HttpRequestException or OperationCanceledException)
                    {
                        // A session that stays open for a while does no harm.
                    }
                }
            }
        }

        private async Task<JsonElement> CallAsync(Uri endpoint, string? session, string method, object parameters, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(new { id = "junotes", method, @params = parameters, jsonrpc = "2.0" }) };
            if (session is not null)
            {
                request.Headers.TryAddWithoutValidation("Cookie", "JSESSIONID=" + session);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new FeedFetchException($"WebUntis antwortet mit {(int)response.StatusCode}. Prüfe Server und Schulname.");
            }

            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            }
            catch (JsonException)
            {
                throw new FeedFetchException("WebUntis hat keine lesbare Antwort gegeben. Prüfe Server und Schulname.");
            }

            using (document)
            {
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    throw new FeedFetchException(Explain(error.TryGetProperty("code", out var code) && code.TryGetInt32(out var number) ? number : 0));
                }

                return document.RootElement.TryGetProperty("result", out var result) ? result.Clone() : default;
            }
        }

        // What WebUntis's error codes mean, in a sentence for the admin. The server's own message is not repeated, it may contain the user name.
        internal static string Explain(int code) => code switch
        {
            -8504 => "Benutzername oder Passwort stimmen bei WebUntis nicht.",
            -8500 => "WebUntis kennt den Benutzer nicht.",
            -8509 => "Dieser Zugang darf den Stundenplan nicht über die Schnittstelle von WebUntis lesen.",
            -8520 => "Die Anmeldung bei WebUntis ist nicht gültig.",
            -8998 => "WebUntis kennt den Schulnamen nicht. Er steht in der Anmeldeadresse hinter school=.",
            _ => $"WebUntis meldet einen Fehler (Code {code}).",
        };

        private static int Number(DateOnly date) => (date.Year * 10000) + (date.Month * 100) + date.Day;

        private static DateTimeOffset At(int date, int time, TimeZoneInfo zone)
        {
            var local = new DateTime(date / 10000, (date / 100) % 100, date % 100, time / 100, time % 100, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        private static string? Name(JsonElement entry, string property, bool preferLong)
        {
            if (!entry.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
            {
                return null;
            }

            var first = list[0];
            var text = preferLong ? Text(first, "longname") ?? Text(first, "name") : Text(first, "name") ?? Text(first, "longname");
            return text;
        }

        private static string? Text(JsonElement parent, string name)
            => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()?.Trim() is { Length: > 0 } text ? text : null;

        private static int Int(JsonElement parent, string name)
            => parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

        internal static IcsEvent? Lesson(JsonElement entry, TimeZoneInfo zone)
        {
            var date = Int(entry, "date");
            var start = Int(entry, "startTime");
            var end = Int(entry, "endTime");
            if (date < 20000101 || end <= start)
            {
                return null;
            }

            var type = Text(entry, "lstype") ?? "ls";
            // Lessons and exams. Breaks, standby and office hours are not lessons for her.
            if (type is not ("ls" or "ex"))
            {
                return null;
            }

            var subject = Name(entry, "su", preferLong: true) ?? Text(entry, "lstext") ?? "Unterricht";
            var summary = type == "ex" ? "Klassenarbeit " + subject : subject;
            var code = Text(entry, "code");
            var notes = new[] { Text(entry, "substText"), Text(entry, "lstext"), code == "irregular" ? "Änderung gegenüber dem Plan" : null }.OfType<string>().Distinct().ToList();
            var id = Int(entry, "id");
            return new IcsEvent($"untis-{id}-{date}-{start}", At(date, start, zone), At(date, end, zone), summary, Name(entry, "ro", preferLong: false), notes.Count == 0 ? null : string.Join(" · ", notes), code == "cancelled", false);
        }

        internal static IcsEvent? Holiday(JsonElement item, TimeZoneInfo zone, DateOnly from, DateOnly until)
        {
            var first = Int(item, "startDate");
            var last = Int(item, "endDate");
            if (first < 20000101 || last < first || (Text(item, "longName") ?? Text(item, "name")) is not { } name)
            {
                return null;
            }

            var start = At(first, 0, zone);
            var end = At(last, 0, zone).AddDays(1);
            var windowStart = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(from.ToDateTime(TimeOnly.MinValue)));
            var windowEnd = new DateTimeOffset(until.ToDateTime(TimeOnly.MaxValue), zone.GetUtcOffset(until.ToDateTime(TimeOnly.MaxValue)));
            return end < windowStart || start > windowEnd ? null : new IcsEvent($"untis-holiday-{Int(item, "id")}-{first}", start, end, name, null, null, false, true);
        }
    }
}
