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

        public Task<PushResponse> PushAsync(PushRequest request, CancellationToken ct) => PushAsync(request, null, null, ct);

        // With a person: a row of somebody else is never touched, and a row may only hang under a parent that is the person's own. Such a change is left out, the rest of the
        // batch is stored, and `accepted` counts only what was taken: an error for the whole batch would block the app for good, because it sends the same batch again
        // (and a batch it was told was fine, it deletes). Nothing in the answer says which row it was. Without a person (no account exists yet) everything is as it was.
        public async Task<PushResponse> PushAsync(PushRequest request, SyncOwner? owner, ISecurityLog? log, CancellationToken ct)
        {
            Validate(request);

            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var gate = new NpgsqlCommand("select pg_advisory_xact_lock($1)", connection, transaction) { Parameters = { new() { Value = PushLock } } })
            {
                await gate.ExecuteNonQueryAsync(ct);
            }

            var skipped = new HashSet<int>();
            if (owner is { } person)
            {
                await ClaimAsync(connection, transaction, person.Id, ct);
                var foreignRows = await ForeignAsync(connection, transaction, request.Changes, person.Id, ct);
                var foreignParents = await ForeignParentsAsync(connection, transaction, request.Changes, person.Id, ct);
                var rowCount = 0;
                var parentCount = 0;
                for (var index = 0; index < request.Changes.Count; index++)
                {
                    var change = request.Changes[index];
                    if (foreignRows.Contains((change.Kind, change.Id)))
                    {
                        skipped.Add(index);
                        rowCount++;
                    }
                    else if (change.ParentId is { } parent && foreignParents.Contains(parent))
                    {
                        skipped.Add(index);
                        parentCount++;
                    }
                }

                if (rowCount > 0)
                {
                    log?.RuleBroken(person, SecurityRules.ForeignRow, rowCount);
                }

                if (parentCount > 0)
                {
                    log?.RuleBroken(person, SecurityRules.ForeignParent, parentCount);
                }
            }

            for (var index = 0; index < request.Changes.Count; index++)
            {
                if (!skipped.Contains(index))
                {
                    await Apply(connection, transaction, request.Changes[index], owner?.Id, ct);
                }
            }

            long seq;
            await using (var latest = new NpgsqlCommand("select coalesce(max(seq), 0) from entities where owner_id is not distinct from $1", connection, transaction))
            {
                latest.Parameters.Add(new NpgsqlParameter { Value = (object?)owner?.Id ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
                seq = (long)(await latest.ExecuteScalarAsync(ct))!;
            }

            await transaction.CommitAsync(ct);
            return new PushResponse(request.Changes.Count - skipped.Count, seq);
        }

        public Task<PullResponse> PullAsync(long since, int limit, CancellationToken ct) => PullAsync(since, limit, null, ct);

        // With a person, only what is theirs; without one (no account yet) everything, which is all there is.
        public async Task<PullResponse> PullAsync(long since, int limit, Guid? owner, CancellationToken ct)
        {
            limit = Math.Clamp(limit, 1, MaxChanges);
            var records = new List<SyncRecord>();

            await using var command = dataSource.CreateCommand("select kind, id, parent_id, payload::text, deleted, seq from entities where seq > $1 and owner_id is not distinct from $3 order by seq limit $2");
            command.Parameters.Add(new NpgsqlParameter { Value = since });
            command.Parameters.Add(new NpgsqlParameter { Value = limit + 1 });
            command.Parameters.Add(new NpgsqlParameter { Value = (object?)owner ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
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

        // Gives every row that has no owner yet to the first administrator: one statement, and nothing to do once it has run (the partial index holds only unowned rows).
        public async Task ClaimAsync(Guid owner, CancellationToken ct)
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await ClaimAsync(connection, null, owner, ct);
        }

        private static async Task ClaimAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid owner, CancellationToken ct)
        {
            await using var claim = new NpgsqlCommand("update entities set owner_id = $1 where owner_id is null", connection, transaction);
            claim.Parameters.Add(new NpgsqlParameter { Value = owner });
            await claim.ExecuteNonQueryAsync(ct);
        }

        // Rows of this batch that already exist and belong to somebody else.
        private static async Task<HashSet<(string Kind, Guid Id)>> ForeignAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyList<SyncChange> changes, Guid owner, CancellationToken ct)
        {
            var found = new HashSet<(string, Guid)>();
            await using var command = new NpgsqlCommand(
                "select e.kind, e.id from entities e join unnest($1::text[], $2::uuid[]) as t(kind, id) on e.kind = t.kind and e.id = t.id where e.owner_id is distinct from $3", connection, transaction);
            command.Parameters.Add(new NpgsqlParameter { Value = changes.Select(change => change.Kind).ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
            command.Parameters.Add(new NpgsqlParameter { Value = changes.Select(change => change.Id).ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid });
            command.Parameters.Add(new NpgsqlParameter { Value = owner });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                found.Add((reader.GetString(0), reader.GetGuid(1)));
            }

            return found;
        }

        // Parents named in this batch that exist and belong to somebody else (of any kind: an id that is somebody else's under any kind is not to be hung under).
        private static async Task<HashSet<Guid>> ForeignParentsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyList<SyncChange> changes, Guid owner, CancellationToken ct)
        {
            var found = new HashSet<Guid>();
            var parents = changes.Where(change => change.ParentId is not null).Select(change => change.ParentId!.Value).Distinct().ToArray();
            if (parents.Length == 0)
            {
                return found;
            }

            await using var command = new NpgsqlCommand("select distinct id from entities where id = any($1::uuid[]) and owner_id is distinct from $2", connection, transaction);
            command.Parameters.Add(new NpgsqlParameter { Value = parents, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid });
            command.Parameters.Add(new NpgsqlParameter { Value = owner });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                found.Add(reader.GetGuid(0));
            }

            return found;
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

                if ((change.Kind is EntityKinds.Page or EntityKinds.Stroke or EntityKinds.Grade or EntityKinds.Card or EntityKinds.PageObject) && change.ParentId is null)
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

        private static async Task Apply(NpgsqlConnection connection, NpgsqlTransaction transaction, SyncChange change, Guid? owner, CancellationToken ct)
        {
            // A stroke that exists stays as it is unless this deletes it; other kinds take the new values unless nothing would change.
            var conflict = change.Kind == EntityKinds.Stroke
                ? "on conflict (kind, id) do update set deleted = true, seq = nextval('change_seq') where excluded.deleted and not entities.deleted"
                : "on conflict (kind, id) do update set parent_id = excluded.parent_id, payload = excluded.payload, deleted = excluded.deleted, seq = nextval('change_seq') "
                    + "where (entities.parent_id, entities.payload, entities.deleted) is distinct from (excluded.parent_id, excluded.payload, excluded.deleted)";

            await using var command = new NpgsqlCommand(
                "insert into entities (kind, id, parent_id, payload, deleted, seq, owner_id) values ($1, $2, $3, $4, $5, nextval('change_seq'), $6) " + conflict, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter { Value = change.Kind });
            command.Parameters.Add(new NpgsqlParameter { Value = change.Id });
            command.Parameters.Add(new NpgsqlParameter { Value = (object?)change.ParentId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
            command.Parameters.Add(new NpgsqlParameter { Value = change.Payload is { } payload ? payload.GetRawText() : DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new NpgsqlParameter { Value = change.Deleted });
            command.Parameters.Add(new NpgsqlParameter { Value = (object?)owner ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
