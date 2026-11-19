using System.Globalization;
using System.Text;

namespace JuNotes.Backend.School
{
    public sealed record IcsEvent(string Uid, DateTimeOffset Start, DateTimeOffset End, string Summary, string? Location, string? Description, bool Cancelled, bool AllDay);

    // The part of iCalendar (RFC 5545) that school timetables use: single events with a start, an end and some text.
    // Times with a TZID or in UTC are exact; floating times are read in `defaultZone`. Recurrence rules are not expanded; they are counted instead.
    public static class IcsParser
    {
        public sealed record Result(IReadOnlyList<IcsEvent> Events, int Skipped);

        public static Result Parse(string text, TimeZoneInfo defaultZone)
        {
            var events = new List<IcsEvent>();
            var skipped = 0;
            Dictionary<string, (Dictionary<string, string> Parameters, string Value)>? current = null;

            foreach (var line in Unfold(text))
            {
                if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    current = new Dictionary<string, (Dictionary<string, string>, string)>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    if (current is not null)
                    {
                        if (TryBuild(current, defaultZone, out var built))
                        {
                            events.Add(built);
                        }
                        else
                        {
                            skipped++;
                        }
                    }

                    current = null;
                    continue;
                }

                if (current is not null && TrySplit(line, out var name, out var parameters, out var value))
                {
                    current[name] = (parameters, value);
                }
            }

            return new Result(events, skipped);
        }

        // A line that continues on the next one starts with a space or a tab there.
        internal static IEnumerable<string> Unfold(string text)
        {
            var builder = new StringBuilder();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                {
                    builder.Append(line, 1, line.Length - 1);
                    continue;
                }

                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                builder.Append(line);
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
            }
        }

        private static bool TrySplit(string line, out string name, out Dictionary<string, string> parameters, out string value)
        {
            name = "";
            value = "";
            parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // The first colon outside quotes ends the name and its parameters.
            var inQuotes = false;
            var colon = -1;
            for (var index = 0; index < line.Length; index++)
            {
                if (line[index] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[index] == ':' && !inQuotes)
                {
                    colon = index;
                    break;
                }
            }

            if (colon <= 0)
            {
                return false;
            }

            var head = line[..colon].Split(';');
            name = head[0].Trim();
            foreach (var parameter in head.Skip(1))
            {
                var equals = parameter.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0)
                {
                    parameters[parameter[..equals]] = parameter[(equals + 1)..].Trim('"');
                }
            }

            value = line[(colon + 1)..];
            return true;
        }

        private static bool TryBuild(Dictionary<string, (Dictionary<string, string> Parameters, string Value)> fields, TimeZoneInfo defaultZone, out IcsEvent result)
        {
            result = null!;
            if (fields.ContainsKey("RRULE") || !fields.TryGetValue("DTSTART", out var start) || !TryTime(start.Parameters, start.Value, defaultZone, out var from, out var allDay))
            {
                return false;
            }

            DateTimeOffset to;
            if (fields.TryGetValue("DTEND", out var end))
            {
                if (!TryTime(end.Parameters, end.Value, defaultZone, out to, out _))
                {
                    return false;
                }
            }
            else
            {
                to = allDay ? from.AddDays(1) : from;
            }

            var summary = fields.TryGetValue("SUMMARY", out var title) ? Unescape(title.Value) : "";
            var uid = fields.TryGetValue("UID", out var id) ? id.Value : from.ToString("O", CultureInfo.InvariantCulture) + summary;
            var status = fields.TryGetValue("STATUS", out var state) ? state.Value : "";
            result = new IcsEvent(
                uid,
                from,
                to < from ? from : to,
                summary,
                fields.TryGetValue("LOCATION", out var place) ? Unescape(place.Value) : null,
                fields.TryGetValue("DESCRIPTION", out var description) ? Unescape(description.Value) : null,
                status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase),
                allDay);
            return true;
        }

        private static bool TryTime(Dictionary<string, string> parameters, string value, TimeZoneInfo defaultZone, out DateTimeOffset time, out bool allDay)
        {
            time = default;
            allDay = parameters.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase) || value.Length == 8;

            if (allDay)
            {
                if (!DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    return false;
                }

                time = new DateTimeOffset(day, defaultZone.GetUtcOffset(day));
                return true;
            }

            var utc = value.EndsWith('Z');
            if (!DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                return false;
            }

            if (utc)
            {
                time = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc));
                return true;
            }

            var zone = defaultZone;
            if (parameters.TryGetValue("TZID", out var tzid))
            {
                try
                {
                    zone = TimeZoneInfo.FindSystemTimeZoneById(tzid);
                }
                catch (TimeZoneNotFoundException)
                {
                    // An unknown zone name is read as the school's own.
                }
            }

            // A time that does not exist on a spring-forward night is read an hour later.
            if (zone.IsInvalidTime(local))
            {
                local = local.AddHours(1);
            }

            time = new DateTimeOffset(local, zone.GetUtcOffset(local));
            return true;
        }

        // One pass, so an escaped backslash followed by an n stays a backslash and an n.
        private static string Unescape(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] == '\\' && index + 1 < value.Length)
                {
                    index++;
                    builder.Append(value[index] is 'n' or 'N' ? '\n' : value[index]);
                }
                else
                {
                    builder.Append(value[index]);
                }
            }

            return builder.ToString();
        }
    }
}
