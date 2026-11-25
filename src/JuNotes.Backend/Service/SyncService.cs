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
                if (person.AdoptsUnowned)
                {
                    // Under the lock of this push: rows of this batch (and the parents it names) that nobody owns yet become the person's, so a push that meets the first claim
                    // half done neither loses its change nor leaves the row without an owner until the next start.
                    var ids = request.Changes.Select(change => change.Id).Concat(request.Changes.Where(change => change.ParentId is not null).Select(change => change.ParentId!.Value)).Distinct().ToArray();
                    await using var adopt = new NpgsqlCommand("update entities set owner_id = $1 where owner_id is null and id = any($2::uuid[])", connection, transaction);
                    adopt.Parameters.Add(new NpgsqlParameter { Value = person.Id });
                    adopt.Parameters.Add(new NpgsqlParameter { Value = ids, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid });
                    await adopt.ExecuteNonQueryAsync(ct);
                }

                var foreignRows = await ForeignAsync(connection, transaction, request.Changes, person.Id, ct);
                var foreignParents = await ForeignParentsAsync(connection, transaction, request.Changes, person.Id, ct);
                var rowCount = 0;
                var parentCount = 0;
                for (var index = 0; index < request.Changes.Count; index++)
                {
                    var change = request.Changes[index];
                    if (foreignRows.Contains(change.Id))
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
        // The caller names the connection: it may take long on a big table and is not held to the time limit of the web.
        public static async Task ClaimAsync(Guid owner, NpgsqlDataSource source, CancellationToken ct)
        {
            await using var connection = await source.OpenConnectionAsync(ct);
            await using var claim = new NpgsqlCommand("update entities set owner_id = $1 where owner_id is null", connection) { CommandTimeout = 0 };
            claim.Parameters.Add(new NpgsqlParameter { Value = owner });
            await claim.ExecuteNonQueryAsync(ct);
        }

        // Ids of this batch that already stand in the table, under any kind, and belong to somebody else: nobody puts a row under an id that somebody else uses.
        private static async Task<HashSet<Guid>> ForeignAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyList<SyncChange> changes, Guid owner, CancellationToken ct)
        {
            var found = new HashSet<Guid>();
            await using var command = new NpgsqlCommand("select distinct id from entities where id = any($1::uuid[]) and owner_id is distinct from $2", connection, transaction);
            command.Parameters.Add(new NpgsqlParameter { Value = changes.Select(change => change.Id).Distinct().ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid });
            command.Parameters.Add(new NpgsqlParameter { Value = owner });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                found.Add(reader.GetGuid(0));
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

                // What every reader of the notes expects of a payload: a JSON object. Anything else (a number, a list, a text) would make the lists of its owner fail.
                if (change.Payload is { ValueKind: not JsonValueKind.Object })
                {
                    throw new SyncValidationException("A payload must be an object.");
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
                ? "on conflict (kind, id) do update set deleted = true, seq = nextval('change_seq') where excluded.deleted and not entities.deleted and entities.owner_id is not distinct from excluded.owner_id"
                : "on conflict (kind, id) do update set parent_id = excluded.parent_id, payload = excluded.payload, deleted = excluded.deleted, seq = nextval('change_seq') "
                    + "where entities.owner_id is not distinct from excluded.owner_id and (entities.parent_id, entities.payload, entities.deleted) is distinct from (excluded.parent_id, excluded.payload, excluded.deleted)";

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
