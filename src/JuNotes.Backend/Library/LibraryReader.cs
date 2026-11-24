using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace JuNotes.Backend.Library
{
    // A folder: kind "subject" on the wire. It lies in ParentId, and only a subject (IsSubject) takes marks and cards.
    public sealed record SubjectInfo(Guid Id, string Name, string Color, string Symbol, int Sort, Guid? ParentId = null, bool IsSubject = true);

    // The front of a notebook as she designed it: a colour, a gradient or her picture, with an emoji on it.
    public sealed record CoverInfo(string Style, string ColorA, string ColorB, Guid? AssetId, string? Emoji);

    public sealed record NotebookInfo(Guid Id, Guid? SubjectId, string Title, string Type, string Template, DateTimeOffset? CreatedAt, CoverInfo? Cover = null);

    // Something she put on a page besides handwriting: a text box, photo, sticker, tape or recording. Positions are in page points.
    public sealed record ObjectInfo(Guid Id, string Type, double X, double Y, double Width, double Height, double Rotation, string? Text, Guid? AssetId, string? Color, double FontSize, int Z);

    // The kinds of content a page can have: her own drawing, a scanned sheet or a page of an imported PDF.
    public sealed record PageInfo(
        Guid Id,
        Guid NotebookId,
        string Sort,
        string? Template,
        string ContentType,
        Guid? AssetId,
        int PdfPageIndex,
        string? Heading,
        string? PrintedText,
        string? Solution,
        IReadOnlyList<string> Additions);

    public sealed record StrokeInfo(Guid Id, string Tool, string Color, double Width, IReadOnlyList<(double X, double Y)> Points);

    // Reads what the iPad synced, for the web view. Everything it hands out is what is not in the trash and not deleted.
    public sealed class LibraryReader(NpgsqlDataSource dataSource, Guid? owner = null)
    {
        // The same reader, held to one person's rows: what somebody else owns is not there. Without a person (the plain reader) it reads everything.
        public LibraryReader Of(Guid person) => new(dataSource, person);

        public async Task<IReadOnlyList<SubjectInfo>> SubjectsAsync(CancellationToken ct)
        {
            var result = new List<SubjectInfo>();
            foreach (var (id, parent, payload) in await ReadAsync("subject", null, ct))
            {
                var isSubject = !payload.TryGetProperty("isSubject", out var flag) || flag.ValueKind != JsonValueKind.False;
                result.Add(new SubjectInfo(id, Text(payload, "name") ?? "Fach", Text(payload, "color") ?? "#8e8e93", Text(payload, "symbol") ?? "book", Number(payload, "sort"), parent, isSubject));
            }

            return result.OrderBy(subject => subject.Sort).ThenBy(subject => subject.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public async Task<IReadOnlyList<NotebookInfo>> NotebooksAsync(CancellationToken ct)
        {
            var result = new List<NotebookInfo>();
            foreach (var (id, parent, payload) in await ReadAsync("notebook", null, ct))
            {
                if (IsTrashed(payload))
                {
                    continue;
                }

                CoverInfo? cover = null;
                if (payload.TryGetProperty("cover", out var node) && node.ValueKind == JsonValueKind.Object)
                {
                    var colorA = Text(node, "colorA") ?? "#8e8e93";
                    cover = new CoverInfo(Text(node, "style") ?? "gradient", colorA, Text(node, "colorB") ?? colorA, Guid.TryParse(Text(node, "assetID"), out var picture) ? picture : null, Text(node, "emoji"));
                }

                result.Add(new NotebookInfo(id, parent, Text(payload, "title") ?? "Heft", Text(payload, "type") ?? "pages", Text(payload, "template") ?? "ruled", Date(payload, "createdAt"), cover));
            }

            return result.OrderBy(notebook => notebook.CreatedAt).ThenBy(notebook => notebook.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public async Task<NotebookInfo?> NotebookAsync(Guid id, CancellationToken ct) => (await NotebooksAsync(ct)).FirstOrDefault(notebook => notebook.Id == id);

        public async Task<IReadOnlyList<PageInfo>> PagesAsync(Guid notebookId, CancellationToken ct)
        {
            var result = new List<PageInfo>();
            foreach (var (id, parent, payload) in await ReadAsync("page", notebookId, ct))
            {
                if (IsTrashed(payload))
                {
                    continue;
                }

                var content = payload.TryGetProperty("content", out var node) && node.ValueKind == JsonValueKind.Object ? node : default;
                var additions = payload.TryGetProperty("additions", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Select(item => item.GetString()).OfType<string>().ToList() : [];
                result.Add(new PageInfo(id, parent ?? notebookId, Text(payload, "sort") ?? "", Text(payload, "template"), content.ValueKind == JsonValueKind.Object ? Text(content, "type") ?? "drawing" : "drawing",
                    content.ValueKind == JsonValueKind.Object && Guid.TryParse(Text(content, "assetID"), out var asset) ? asset : null, content.ValueKind == JsonValueKind.Object ? Number(content, "pageIndex") : 0,
                    Text(payload, "heading"), Text(payload, "printedText"), Text(payload, "solution"), additions));
            }

            // Equal keys can come from two devices; the id makes the order the same everywhere.
            return result.OrderBy(page => page.Sort, StringComparer.Ordinal).ThenBy(page => page.Id).ToList();
        }

        public async Task<IReadOnlyList<ObjectInfo>> ObjectsAsync(Guid pageId, CancellationToken ct)
        {
            var result = new List<ObjectInfo>();
            foreach (var (id, _, payload) in await ReadAsync("object", pageId, ct))
            {
                var type = Text(payload, "type");
                if (type is not ("text" or "image" or "sticker" or "tape" or "audio"))
                {
                    continue;
                }

                result.Add(new ObjectInfo(id, type, Real(payload, "x", 0), Real(payload, "y", 0), Real(payload, "width", 100), Real(payload, "height", 40), Real(payload, "rotation", 0),
                    Text(payload, "text"), Guid.TryParse(Text(payload, "assetID"), out var asset) ? asset : null, Text(payload, "color"), Real(payload, "fontSize", 16), (int)Real(payload, "z", 0)));
            }

            return result.OrderBy(item => item.Z).ThenBy(item => item.Id).ToList();
        }

        public async Task<IReadOnlyList<StrokeInfo>> StrokesAsync(Guid pageId, CancellationToken ct)
        {
            var result = new List<StrokeInfo>();
            foreach (var (id, _, payload) in await ReadAsync("stroke", pageId, ct))
            {
                var points = new List<(double, double)>();
                if (payload.TryGetProperty("points", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var point in list.EnumerateArray())
                    {
                        if (point.TryGetProperty("x", out var x) && point.TryGetProperty("y", out var y) && x.TryGetDouble(out var px) && y.TryGetDouble(out var py))
                        {
                            points.Add((px, py));
                        }
                    }
                }

                result.Add(new StrokeInfo(id, Text(payload, "tool") ?? "pen", Text(payload, "color") ?? "#000000", payload.TryGetProperty("width", out var width) && width.TryGetDouble(out var w) ? w : 2, points));
            }

            return result;
        }

        private async Task<List<(Guid Id, Guid? Parent, JsonElement Payload)>> ReadAsync(string kind, Guid? parent, CancellationToken ct)
        {
            var rows = new List<(Guid, Guid?, JsonElement)>();
            await using var command = dataSource.CreateCommand("select id, parent_id, payload::text from entities where kind = $1 and not deleted and payload is not null"
                + (parent is null ? "" : " and parent_id = $2") + (owner is null ? "" : " and owner_id = $" + (parent is null ? "2" : "3")) + " order by seq");
            command.Parameters.Add(new NpgsqlParameter { Value = kind });
            if (parent is { } value)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value });
            }

            if (owner is { } person)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = person });
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), JsonElement.Parse(reader.GetString(2))));
            }

            return rows;
        }

        private static bool IsTrashed(JsonElement payload) => payload.TryGetProperty("trashedAt", out var trashed) && trashed.ValueKind != JsonValueKind.Null;

        private static string? Text(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static double Real(JsonElement parent, string name, double fallback) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : fallback;

        private static int Number(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

        private static DateTimeOffset? Date(JsonElement parent, string name) => Text(parent, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    }
}
