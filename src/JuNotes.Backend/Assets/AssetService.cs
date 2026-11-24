using Npgsql;

namespace JuNotes.Backend.Assets
{
    public sealed class AssetOptions
    {
        public string Directory { get; set; } = "assets";
    }

    public sealed class AssetException(string message, int statusCode) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }

    // Scans and imported PDFs are files, not rows: they are too big for a sync entry. One file per id, named by the id only.
    public sealed class AssetService(AssetOptions options, NpgsqlDataSource? dataSource = null)
    {
        public const long MaxBytes = 20 * 1024 * 1024;

        private string PathFor(Guid id) => Path.Combine(options.Directory, id.ToString("N"));

        public bool Exists(Guid id) => File.Exists(PathFor(id));

        // What the file really is, by its first bytes: the type the client names is not trusted.
        public static string? SniffContentType(ReadOnlySpan<byte> head)
        {
            if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            {
                return "image/jpeg";
            }

            if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
            {
                return "image/png";
            }

            if (head.Length >= 5 && head[0] == '%' && head[1] == 'P' && head[2] == 'D' && head[3] == 'F' && head[4] == '-')
            {
                return "application/pdf";
            }

            return null;
        }

        public async Task SaveAsync(Guid id, Stream body, CancellationToken ct) => _ = await SaveAsync(id, body, null, ct);

        // With an owner: the file is checked first (the answer for a wrong file is the same for everybody), then the id is claimed in the table (whoever asks first for a free
        // id owns it), and only then the file is put in place. A file of somebody else is not touched and the answer is the same as for a stored one (false). A stop in between
        // leaves a row without a file, never a file without an owner.
        public async Task<bool> SaveAsync(Guid id, Stream body, Guid? owner, CancellationToken ct)
        {
            if (id == Guid.Empty)
            {
                throw new AssetException("An asset needs an id.", 400);
            }

            System.IO.Directory.CreateDirectory(options.Directory);
            var path = PathFor(id);
            var partial = path + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var chunk = new byte[64 * 1024];
                    long total = 0;
                    int read;
                    while ((read = await body.ReadAsync(chunk, ct)) > 0)
                    {
                        if (total == 0 && SniffContentType(chunk.AsSpan(0, read)) is null)
                        {
                            throw new AssetException("Only JPEG, PNG and PDF files are accepted.", 415);
                        }

                        total += read;
                        if (total > MaxBytes)
                        {
                            throw new AssetException("The file is larger than 20 MB.", 413);
                        }

                        await file.WriteAsync(chunk.AsMemory(0, read), ct);
                    }

                    if (total == 0)
                    {
                        throw new AssetException("The file is empty.", 400);
                    }
                }

                if (owner is { } person)
                {
                    await using (var claim = dataSource!.CreateCommand("insert into assets (id, owner_id) values ($1, $2) on conflict (id) do nothing"))
                    {
                        claim.Parameters.Add(new NpgsqlParameter { Value = id });
                        claim.Parameters.Add(new NpgsqlParameter { Value = person });
                        await claim.ExecuteNonQueryAsync(ct);
                    }

                    if (await OwnerOfAsync(id, ct) != person)
                    {
                        return false;
                    }
                }

                // The same id always holds the same file, so a repeated upload simply replaces it.
                File.Move(partial, path, overwrite: true);
                return true;
            }
            finally
            {
                File.Delete(partial);
            }
        }

        // Who a file belongs to; nobody when there is no row (files made before the table existed get their owner from the claim).
        private async Task<Guid?> OwnerOfAsync(Guid id, CancellationToken ct)
        {
            await using var command = dataSource!.CreateCommand("select owner_id from assets where id = $1");
            command.Parameters.Add(new NpgsqlParameter { Value = id });
            return await command.ExecuteScalarAsync(ct) as Guid?;
        }

        public async Task<bool> ExistsAsync(Guid id, Guid? owner, CancellationToken ct) => Exists(id) && (owner is not { } person || await OwnerOfAsync(id, ct) == person);

        // A file of somebody else looks exactly like a file that is not there.
        public async Task<(Stream Stream, string ContentType)?> OpenAsync(Guid id, Guid? owner, CancellationToken ct)
            => owner is { } person && await OwnerOfAsync(id, ct) != person ? null : Open(id);

        // Gives every file that has no owner yet to this person, once, when the first account exists.
        public async Task ClaimFilesAsync(Guid owner, NpgsqlDataSource source, CancellationToken ct)
        {
            if (!System.IO.Directory.Exists(options.Directory))
            {
                return;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(options.Directory))
            {
                if (!Guid.TryParseExact(Path.GetFileName(file), "N", out var id))
                {
                    continue;
                }

                await using var claim = source.CreateCommand("insert into assets (id, owner_id) values ($1, $2) on conflict (id) do nothing");
                claim.Parameters.Add(new NpgsqlParameter { Value = id });
                claim.Parameters.Add(new NpgsqlParameter { Value = owner });
                await claim.ExecuteNonQueryAsync(ct);
            }
        }

        public (Stream Stream, string ContentType)? Open(Guid id)
        {
            var path = PathFor(id);
            if (!File.Exists(path))
            {
                return null;
            }

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = new byte[16];
            var read = stream.Read(head, 0, head.Length);
            stream.Position = 0;
            return (stream, SniffContentType(head.AsSpan(0, read)) ?? "application/octet-stream");
        }
    }
}
