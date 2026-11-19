using Npgsql;

namespace JuNotes.Backend.School
{
    public sealed record StoredEvent(string Uid, DateTimeOffset Start, DateTimeOffset End, string Summary, string? Location, string? Description, bool Cancelled, bool AllDay);

    public sealed record StoredNews(string Key, string Title, string? Link, DateTimeOffset? PublishedAt, string? Summary);

    public sealed class SchoolStore(NpgsqlDataSource dataSource)
    {
        // A source is replaced as a whole in one transaction: a half-imported timetable is never visible.
        public async Task ReplaceEventsAsync(string source, IReadOnlyList<IcsEvent> events, CancellationToken ct)
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var delete = new NpgsqlCommand("delete from calendar_events where source = $1", connection, transaction) { Parameters = { new() { Value = source } } })
            {
                await delete.ExecuteNonQueryAsync(ct);
            }

            foreach (var item in events)
            {
                await using var insert = new NpgsqlCommand(
                    "insert into calendar_events (source, uid, starts_at, ends_at, summary, location, description, cancelled, all_day) values ($1, $2, $3, $4, $5, $6, $7, $8, $9) on conflict do nothing",
                    connection, transaction);
                insert.Parameters.Add(new NpgsqlParameter { Value = source });
                insert.Parameters.Add(new NpgsqlParameter { Value = item.Uid });
                insert.Parameters.Add(new NpgsqlParameter { Value = item.Start.ToUniversalTime() });
                insert.Parameters.Add(new NpgsqlParameter { Value = item.End.ToUniversalTime() });
                insert.Parameters.Add(new NpgsqlParameter { Value = item.Summary });
                insert.Parameters.Add(new NpgsqlParameter { Value = (object?)item.Location ?? DBNull.Value });
                insert.Parameters.Add(new NpgsqlParameter { Value = (object?)item.Description ?? DBNull.Value });
                insert.Parameters.Add(new NpgsqlParameter { Value = item.Cancelled });
                insert.Parameters.Add(new NpgsqlParameter { Value = item.AllDay });
                await insert.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        public async Task ReplaceNewsAsync(string source, IReadOnlyList<NewsItem> items, CancellationToken ct)
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var delete = new NpgsqlCommand("delete from school_news where source = $1", connection, transaction) { Parameters = { new() { Value = source } } })
            {
                await delete.ExecuteNonQueryAsync(ct);
            }

            foreach (var item in items)
            {
                await using var insert = new NpgsqlCommand(
                    "insert into school_news (source, guid, title, link, published_at, summary) values ($1, $2, $3, $4, $5, $6) on conflict do nothing", connection, transaction);
                insert.Parameters.Add(new NpgsqlParameter { Value = source });
                insert.Parameters.Add(new NpgsqlParameter { Value = item.Key });
                insert.Parameters.Add(new NpgsqlParameter { Value = item.Title });
                insert.Parameters.Add(new NpgsqlParameter { Value = (object?)item.Link ?? DBNull.Value });
                insert.Parameters.Add(new NpgsqlParameter { Value = (object?)item.PublishedAt?.ToUniversalTime() ?? DBNull.Value });
                insert.Parameters.Add(new NpgsqlParameter { Value = (object?)item.Summary ?? DBNull.Value });
                await insert.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        public async Task<IReadOnlyList<StoredEvent>> EventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        {
            var result = new List<StoredEvent>();
            await using var command = dataSource.CreateCommand(
                "select uid, starts_at, ends_at, summary, location, description, cancelled, all_day from calendar_events where starts_at < $2 and ends_at >= $1 order by starts_at, summary limit 2000");
            command.Parameters.Add(new NpgsqlParameter { Value = from.ToUniversalTime() });
            command.Parameters.Add(new NpgsqlParameter { Value = to.ToUniversalTime() });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new StoredEvent(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7)));
            }

            return result;
        }

        public async Task<IReadOnlyList<StoredNews>> NewsAsync(int limit, CancellationToken ct)
        {
            var result = new List<StoredNews>();
            await using var command = dataSource.CreateCommand("select guid, title, link, published_at, summary from school_news order by published_at desc nulls last, title limit $1");
            command.Parameters.Add(new NpgsqlParameter { Value = Math.Clamp(limit, 1, 100) });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new StoredNews(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            return result;
        }
    }
}
