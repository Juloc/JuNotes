using System.Text.Json;
using JuNotes.Backend.Data.Sync;
using Npgsql;
using NpgsqlTypes;

namespace JuNotes.Backend.Service
{
    // Strokes only ever get added or deleted; everything else is last write wins. Pushes run one at a time, so a higher seq is always committed later
    // and a device that pulled up to a cursor never misses a change below it.
    public sealed class SyncService(NpgsqlDataSource dataSource)
    {
        public const int MaxChanges = 500;
        public const int MaxPayloadBytes = 1_000_000;
        private const long PushLock = 7481237;

        public async Task<PushResponse> PushAsync(PushRequest request, CancellationToken ct)
        {
            Validate(request);

            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var gate = new NpgsqlCommand("select pg_advisory_xact_lock($1)", connection, transaction) { Parameters = { new() { Value = PushLock } } })
            {
                await gate.ExecuteNonQueryAsync(ct);
            }

            foreach (var change in request.Changes)
            {
                await Apply(connection, transaction, change, ct);
            }

            long seq;
            await using (var latest = new NpgsqlCommand("select coalesce(max(seq), 0) from entities", connection, transaction))
            {
                seq = (long)(await latest.ExecuteScalarAsync(ct))!;
            }

            await transaction.CommitAsync(ct);
            return new PushResponse(request.Changes.Count, seq);
        }

        public async Task<PullResponse> PullAsync(long since, int limit, CancellationToken ct)
        {
            limit = Math.Clamp(limit, 1, MaxChanges);
            var records = new List<SyncRecord>();

            await using var command = dataSource.CreateCommand("select kind, id, parent_id, payload::text, deleted, seq from entities where seq > $1 order by seq limit $2");
            command.Parameters.Add(new NpgsqlParameter { Value = since });
            command.Parameters.Add(new NpgsqlParameter { Value = limit + 1 });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                JsonElement? payload = reader.IsDBNull(3) ? null : JsonElement.Parse(reader.GetString(3));
                records.Add(new SyncRecord(reader.GetString(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), payload, reader.GetBoolean(4), reader.GetInt64(5)));
            }

            var more = records.Count > limit;
            if (more)
            {
                records.RemoveAt(records.Count - 1);
            }

            return new PullResponse(records, records.Count == 0 ? since : records[^1].Seq, more);
        }

        internal static void Validate(PushRequest request)
        {
            if (request.Changes is null || request.Changes.Count == 0)
            {
                throw new SyncValidationException("A push needs at least one change.");
            }

            if (request.Changes.Count > MaxChanges)
            {
                throw new SyncValidationException($"A push holds at most {MaxChanges} changes.");
            }

            foreach (var change in request.Changes)
            {
                if (!EntityKinds.All.Contains(change.Kind))
                {
                    throw new SyncValidationException($"Unknown kind '{change.Kind}'.");
                }

                if (change.Id == Guid.Empty)
                {
                    throw new SyncValidationException("Every change needs an id.");
                }

                if ((change.Kind is EntityKinds.Page or EntityKinds.Stroke or EntityKinds.Grade or EntityKinds.Card or EntityKinds.Object) && change.ParentId is null)
                {
                    throw new SyncValidationException($"A {change.Kind} needs a parent.");
                }

                if (!change.Deleted && change.Payload is null)
                {
                    throw new SyncValidationException("A change that is not a deletion needs a payload.");
                }

                if (change.Payload is { } payload && payload.GetRawText().Length > MaxPayloadBytes)
                {
                    throw new SyncValidationException("A payload is larger than 1 MB.");
                }
            }
        }

        private static async Task Apply(NpgsqlConnection connection, NpgsqlTransaction transaction, SyncChange change, CancellationToken ct)
        {
            // A stroke that exists stays as it is unless this deletes it; other kinds take the new values unless nothing would change.
            var conflict = change.Kind == EntityKinds.Stroke
                ? "on conflict (kind, id) do update set deleted = true, seq = nextval('change_seq') where excluded.deleted and not entities.deleted"
                : "on conflict (kind, id) do update set parent_id = excluded.parent_id, payload = excluded.payload, deleted = excluded.deleted, seq = nextval('change_seq') "
                    + "where (entities.parent_id, entities.payload, entities.deleted) is distinct from (excluded.parent_id, excluded.payload, excluded.deleted)";

            await using var command = new NpgsqlCommand(
                "insert into entities (kind, id, parent_id, payload, deleted, seq) values ($1, $2, $3, $4, $5, nextval('change_seq')) " + conflict, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter { Value = change.Kind });
            command.Parameters.Add(new NpgsqlParameter { Value = change.Id });
            command.Parameters.Add(new NpgsqlParameter { Value = (object?)change.ParentId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
            command.Parameters.Add(new NpgsqlParameter { Value = change.Payload is { } payload ? payload.GetRawText() : DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new NpgsqlParameter { Value = change.Deleted });
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
