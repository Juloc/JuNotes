using Npgsql;

namespace JuNotes.Backend.Library
{
    // A live share as the recipient's request meets it: whose notebook it is. Nothing here says what the notebook contains.
    public sealed record Share(Guid Id, Guid NotebookId, Guid OwnerId, string OwnerName);

    // A share as its owner sees it.
    public sealed record ShareRecipient(Guid Id, Guid RecipientId, string RecipientName, DateTimeOffset CreatedAt);

    // The names list for the sharing dialog: the display name of a person and nothing else about them.
    public sealed record PersonName(Guid Id, string Name);

    public enum ShareOutcome
    {
        Created,
        AlreadyShared,
        NotebookNotFound,
        RecipientNotFound,
        Self,
    }

    // Shares of single notebooks, read only (ARCHITECTURE 10.2). Every question about a share is asked of the table each time and nothing is kept, so a revoked share is gone
    // with the next request. Who may share: only the owner of the notebook, and only a notebook that is not deleted or in the trash.
    public sealed class ShareStore(NpgsqlDataSource data, TimeProvider time)
    {
        // The people one can share with: active people of this installation who can sign in, without the asker. Name and id only.
        public async Task<IReadOnlyList<PersonName>> PeopleAsync(Guid except, CancellationToken ct)
        {
            await using var command = data.CreateCommand("select id, name from users where state = 'active' and (totp_confirmed or hub_sub is not null) and id <> $1 order by lower(name), id");
            command.Parameters.Add(new NpgsqlParameter { Value = except });
            await using var reader = await command.ExecuteReaderAsync(ct);
            var list = new List<PersonName>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(new PersonName(reader.GetGuid(0), reader.GetString(1)));
            }

            return list;
        }

        // The share is made (or found, when it is there already). The names for the log come back with it.
        public async Task<(ShareOutcome Outcome, ShareRecipient? Share)> CreateAsync(Guid owner, Guid notebook, Guid recipient, CancellationToken ct)
        {
            await using var mine = data.CreateCommand("select exists (select 1 from entities where kind = 'notebook' and id = $1 and owner_id = $2 and not deleted and payload is not null and payload->>'trashedAt' is null)");
            mine.Parameters.Add(new NpgsqlParameter { Value = notebook });
            mine.Parameters.Add(new NpgsqlParameter { Value = owner });
            if (!(bool)(await mine.ExecuteScalarAsync(ct))!)
            {
                return (ShareOutcome.NotebookNotFound, null);
            }

            // After the notebook, so somebody who does not own it gets the 404 whatever they ask for.
            if (recipient == owner)
            {
                return (ShareOutcome.Self, null);
            }

            await using var who = data.CreateCommand("select name from users where id = $1 and state = 'active' and (totp_confirmed or hub_sub is not null)");
            who.Parameters.Add(new NpgsqlParameter { Value = recipient });
            if (await who.ExecuteScalarAsync(ct) is not string name)
            {
                return (ShareOutcome.RecipientNotFound, null);
            }

            var now = time.GetUtcNow();
            await using var insert = data.CreateCommand("insert into shares (id, notebook_id, owner_id, recipient_id, mode, created_at) values ($1, $2, $3, $4, 'read', $5) on conflict (notebook_id, recipient_id) where revoked_at is null do nothing returning id");
            insert.Parameters.Add(new NpgsqlParameter { Value = Guid.NewGuid() });
            insert.Parameters.Add(new NpgsqlParameter { Value = notebook });
            insert.Parameters.Add(new NpgsqlParameter { Value = owner });
            insert.Parameters.Add(new NpgsqlParameter { Value = recipient });
            insert.Parameters.Add(new NpgsqlParameter { Value = now });
            if (await insert.ExecuteScalarAsync(ct) is Guid id)
            {
                return (ShareOutcome.Created, new ShareRecipient(id, recipient, name, now));
            }

            var existing = (await ForNotebookAsync(owner, notebook, ct)).First(share => share.RecipientId == recipient);
            return (ShareOutcome.AlreadyShared, existing);
        }

        // The live shares of one notebook of this owner, oldest first. (Whether the notebook is the owner's is for the caller to check.)
        public async Task<IReadOnlyList<ShareRecipient>> ForNotebookAsync(Guid owner, Guid notebook, CancellationToken ct)
        {
            await using var command = data.CreateCommand("select s.id, s.recipient_id, u.name, s.created_at from shares s join users u on u.id = s.recipient_id where s.notebook_id = $1 and s.owner_id = $2 and s.revoked_at is null order by s.created_at, s.id");
            command.Parameters.Add(new NpgsqlParameter { Value = notebook });
            command.Parameters.Add(new NpgsqlParameter { Value = owner });
            await using var reader = await command.ExecuteReaderAsync(ct);
            var list = new List<ShareRecipient>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(new ShareRecipient(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
            }

            return list;
        }

        // Takes a share back; only its owner can. The notebook and the name of the recipient come back for the log, or nothing when the share is not this owner's (or is gone already).
        public async Task<(Guid Notebook, string Recipient)?> RevokeAsync(Guid owner, Guid share, CancellationToken ct)
        {
            await using var command = data.CreateCommand("update shares s set revoked_at = $3 where s.id = $1 and s.owner_id = $2 and s.revoked_at is null returning s.notebook_id, (select name from users where id = s.recipient_id)");
            command.Parameters.Add(new NpgsqlParameter { Value = share });
            command.Parameters.Add(new NpgsqlParameter { Value = owner });
            command.Parameters.Add(new NpgsqlParameter { Value = time.GetUtcNow() });
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? (reader.GetGuid(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)) : null;
        }

        // The live shares made with this person (the notebook has to be still the sharer's, and the sharer still there).
        public async Task<IReadOnlyList<Share>> ForRecipientAsync(Guid recipient, CancellationToken ct)
        {
            await using var command = data.CreateCommand("select s.id, s.notebook_id, s.owner_id, u.name from shares s join users u on u.id = s.owner_id join entities e on e.id = s.notebook_id and e.kind = 'notebook' and e.owner_id = s.owner_id where s.recipient_id = $1 and s.revoked_at is null and s.mode = 'read' order by s.created_at, s.id");
            command.Parameters.Add(new NpgsqlParameter { Value = recipient });
            return await ReadSharesAsync(command, ct);
        }

        // The share that lets this person read this notebook, or nothing. Asked at every request.
        public async Task<Share?> FindAsync(Guid recipient, Guid notebook, CancellationToken ct)
        {
            await using var command = data.CreateCommand("select s.id, s.notebook_id, s.owner_id, u.name from shares s join users u on u.id = s.owner_id join entities e on e.id = s.notebook_id and e.kind = 'notebook' and e.owner_id = s.owner_id where s.recipient_id = $1 and s.notebook_id = $2 and s.revoked_at is null and s.mode = 'read'");
            command.Parameters.Add(new NpgsqlParameter { Value = recipient });
            command.Parameters.Add(new NpgsqlParameter { Value = notebook });
            return (await ReadSharesAsync(command, ct)).FirstOrDefault();
        }

        // The share through which this person may read this page: the page has to lie in a notebook that is shared with them by the person who owns the page.
        public async Task<Share?> FindForPageAsync(Guid recipient, Guid page, CancellationToken ct)
        {
            await using var command = data.CreateCommand("select s.id, s.notebook_id, s.owner_id, u.name from shares s join users u on u.id = s.owner_id join entities p on p.kind = 'page' and p.parent_id = s.notebook_id and p.owner_id = s.owner_id join entities e on e.id = s.notebook_id and e.kind = 'notebook' and e.owner_id = s.owner_id where p.id = $2 and s.recipient_id = $1 and s.revoked_at is null and s.mode = 'read'");
            command.Parameters.Add(new NpgsqlParameter { Value = recipient });
            command.Parameters.Add(new NpgsqlParameter { Value = page });
            return (await ReadSharesAsync(command, ct)).FirstOrDefault();
        }

        // Does this file occur in the shared notebook: as its cover, as the scan or PDF of one of its pages, or as a picture, sticker or recording on one of them? Rows in the trash and
        // deleted rows do not count. Whether the file belongs to the owner is for the caller to check (a notebook can name any id).
        public async Task<bool> AssetInNotebookAsync(Guid owner, Guid notebook, Guid asset, CancellationToken ct)
        {
            await using var command = data.CreateCommand("""
                select exists (
                    select 1 from entities n
                    where n.kind = 'notebook' and n.id = $2 and n.owner_id = $1 and not n.deleted and n.payload->>'trashedAt' is null and lower(n.payload->'cover'->>'assetID') = $3
                  union all
                    select 1 from entities p
                    where p.kind = 'page' and p.parent_id = $2 and p.owner_id = $1 and not p.deleted and p.payload->>'trashedAt' is null and lower(p.payload->'content'->>'assetID') = $3
                  union all
                    select 1 from entities o join entities p on p.id = o.parent_id and p.kind = 'page' and p.owner_id = $1
                    where o.kind = 'object' and o.owner_id = $1 and p.parent_id = $2 and not o.deleted and not p.deleted and p.payload->>'trashedAt' is null and lower(o.payload->>'assetID') = $3)
                """);
            command.Parameters.Add(new NpgsqlParameter { Value = owner });
            command.Parameters.Add(new NpgsqlParameter { Value = notebook });
            command.Parameters.Add(new NpgsqlParameter { Value = asset.ToString("D") });
            return (bool)(await command.ExecuteScalarAsync(ct))!;
        }

        private static async Task<List<Share>> ReadSharesAsync(NpgsqlCommand command, CancellationToken ct)
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            var list = new List<Share>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(new Share(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3)));
            }

            return list;
        }
    }
}
