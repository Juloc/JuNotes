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
    public sealed class AssetService(AssetOptions options)
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

        public async Task SaveAsync(Guid id, Stream body, CancellationToken ct)
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

                // The same id always holds the same file, so a repeated upload simply replaces it.
                File.Move(partial, path, overwrite: true);
            }
            finally
            {
                File.Delete(partial);
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
