using Base.Core;
using Base.Web;
using Base.Web.Auth;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Library;
using JuNotes.Frontend.Admin;

namespace JuNotes.Frontend.Api
{
    public sealed record FolderDto(Guid Id, string Name, string Color, string Symbol, int Sort, Guid? ParentId, bool IsSubject);

    public sealed record NotebookDto(Guid Id, Guid? FolderId, string Title, string Type, string Template, DateTimeOffset? CreatedAt, CoverInfo? Cover);

    public sealed record NotebookDetail(NotebookDto Notebook, int PageCount);

    public sealed record PageDto(Guid Id, Guid NotebookId, string Sort, string ContentType, Guid? AssetId, int PdfPageIndex, string? Heading, string? PrintedText, string? Solution, IReadOnlyList<string> Additions);

    public sealed record PageView(PageDto Page, string NotebookType, string Svg);

    public sealed record SearchResult(IReadOnlyList<FolderDto> Folders, IReadOnlyList<NotebookDto> Notebooks);

    // The reading side of the web app, for the pages in the toolkit of the base. Every path is behind the session (the guard of the base) and answers only what belongs to the
    // person who asks; somebody else's notebook, page or file is a 404 like one that does not exist. Lists come page by page (LIST-01). The instance token is never read here.
    internal static class WebApi
    {
        public const string Prefix = "/api/web/v1";

        public static void MapWebApi(this IEndpointRouteBuilder app)
        {
            var api = app.MapGroup(Prefix);
            // The first request after the first account may be one of these (nobody has used the old pages or the app yet): the old rows are given to their owner first.
            api.AddEndpointFilter(async (invocation, next) =>
            {
                await invocation.HttpContext.RequestServices.GetRequiredService<OwnerDirectory>().EnsureClaimedAsync(invocation.HttpContext.RequestAborted);
                return await next(invocation);
            });

            api.MapGet("/folders", async (int? limit, string? cursor, HttpContext ctx, Libraries libraries) =>
                Paged((await Mine(ctx, libraries).SubjectsAsync(ctx.RequestAborted)).Select(Folder).ToList(), limit, cursor, folder => folder.Id));

            api.MapGet("/notebooks", async (string? folder, int? limit, string? cursor, HttpContext ctx, Libraries libraries) =>
            {
                var all = await Mine(ctx, libraries).NotebooksAsync(ctx.RequestAborted);
                var shown = folder switch
                {
                    null or "" => all,
                    "root" => all.Where(notebook => notebook.SubjectId is null).ToList(),
                    _ when Guid.TryParse(folder, out var id) => all.Where(notebook => notebook.SubjectId == id).ToList(),
                    _ => [],
                };
                return Paged(shown.Select(Notebook).ToList(), limit, cursor, notebook => notebook.Id);
            });

            api.MapGet("/notebooks/{id:guid}", async (Guid id, HttpContext ctx, Libraries libraries) =>
            {
                var mine = Mine(ctx, libraries);
                return await mine.NotebookAsync(id, ctx.RequestAborted) is { } notebook
                    ? Results.Ok(new NotebookDetail(Notebook(notebook), (await mine.PagesAsync(id, ctx.RequestAborted)).Count))
                    : Error.NotFound().ToHttp();
            });

            api.MapGet("/notebooks/{id:guid}/pages", async (Guid id, int? limit, string? cursor, HttpContext ctx, Libraries libraries) =>
            {
                var mine = Mine(ctx, libraries);
                return await mine.NotebookAsync(id, ctx.RequestAborted) is null
                    ? Error.NotFound().ToHttp()
                    : Paged((await mine.PagesAsync(id, ctx.RequestAborted)).Select(Page).ToList(), limit, cursor, page => page.Id);
            });

            // The page as it is drawn: the paper, her objects and her strokes as one picture, as the old web view drew it. The scan or PDF a page may stand on is a file of its own.
            api.MapGet("/pages/{id:guid}", async (Guid id, HttpContext ctx, Libraries libraries) =>
            {
                var mine = Mine(ctx, libraries);
                if (await mine.PageAsync(id, ctx.RequestAborted) is not { } page || await mine.NotebookAsync(page.NotebookId, ctx.RequestAborted) is not { } notebook)
                {
                    return Error.NotFound().ToHttp();
                }

                var template = page.ContentType == "drawing" ? page.Template ?? notebook.Template : "blank";
                // Only what the renderer writes gets out: the picture goes through the allow list before it leaves the server.
                var svg = SvgAllowList.Clean(PageRenderer.Svg(notebook.Type, template, await mine.StrokesAsync(id, ctx.RequestAborted), await mine.ObjectsAsync(id, ctx.RequestAborted), Prefix + "/assets/"));
                return Results.Ok(new PageView(Page(page), notebook.Type, svg));
            });

            api.MapGet("/assets/{id:guid}", async (Guid id, HttpContext ctx, AssetService assets) =>
            {
                if (await assets.OpenAsync(id, ctx.Person().UserId, ctx.RequestAborted) is not { } file)
                {
                    return Error.NotFound().ToHttp();
                }

                ctx.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.Stream(file.Stream, file.ContentType, enableRangeProcessing: true);
            });

            api.MapGet("/grades", async (string? folder, int? limit, string? cursor, HttpContext ctx, Libraries libraries) =>
            {
                var all = await Mine(ctx, libraries).GradesAsync(ctx.RequestAborted);
                return Paged((Guid.TryParse(folder, out var id) ? all.Where(grade => grade.SubjectId == id).ToList() : all), limit, cursor, grade => grade.Id);
            });

            api.MapGet("/cards", async (string? folder, int? limit, string? cursor, HttpContext ctx, Libraries libraries) =>
            {
                var all = await Mine(ctx, libraries).CardsAsync(ctx.RequestAborted);
                return Paged((Guid.TryParse(folder, out var id) ? all.Where(card => card.SubjectId == id).ToList() : all), limit, cursor, card => card.Id);
            });

            // Names of folders and notebooks. The text the iPad recognises in handwriting stays there (it is not synced), so the web cannot search inside pages yet.
            api.MapGet("/search", async (string? q, HttpContext ctx, Libraries libraries) =>
            {
                var term = (q ?? string.Empty).Trim();
                if (term.Length is 0 or > 100)
                {
                    return Results.Ok(new SearchResult([], []));
                }

                var mine = Mine(ctx, libraries);
                bool Has(string text) => text.Contains(term, StringComparison.CurrentCultureIgnoreCase);
                var folders = (await mine.SubjectsAsync(ctx.RequestAborted)).Where(folder => Has(folder.Name)).Take(30).Select(Folder).ToList();
                var notebooks = (await mine.NotebooksAsync(ctx.RequestAborted)).Where(notebook => Has(notebook.Title)).Take(30).Select(Notebook).ToList();
                return Results.Ok(new SearchResult(folders, notebooks));
            });
        }

        private static LibraryReader Mine(HttpContext ctx, Libraries libraries) => libraries.Of(ctx.Person().UserId);

        private static FolderDto Folder(SubjectInfo folder) => new(folder.Id, folder.Name, folder.Color, folder.Symbol, folder.Sort, folder.ParentId, folder.IsSubject);

        private static NotebookDto Notebook(NotebookInfo notebook) => new(notebook.Id, notebook.SubjectId, notebook.Title, notebook.Type, notebook.Template, notebook.CreatedAt, notebook.Cover);

        private static PageDto Page(PageInfo page) => new(page.Id, page.NotebookId, page.Sort, page.ContentType, page.AssetId, page.PdfPageIndex, page.Heading, page.PrintedText, page.Solution, page.Additions);

        // Page by page, the cursor being the id of the last row given. A cursor that is not ours, or whose row is gone since the last page, is an error (cursor.stale) and
        // not a start from the beginning: the page asks again from the top and drops what it has seen twice.
        private static IResult Paged<T>(IReadOnlyList<T> all, int? limit, string? cursor, Func<T, Guid> idOf)
        {
            var request = PageRequest.From(limit, cursor);
            var start = 0;
            if (!string.IsNullOrEmpty(cursor))
            {
                var index = request.Cursor is not null && Cursor.TryDecode(request.Cursor, out var last) ? all.ToList().FindIndex(item => idOf(item) == last) : -1;
                if (index < 0)
                {
                    return Error.Invalid("cursor.stale", "The list changed since the last page; start again from the top.").ToHttp();
                }

                start = index + 1;
            }

            return Results.Ok(Page<T>.From(all.Skip(start).Take(request.Limit + 1).ToList(), request.Limit, item => Cursor.Encode(idOf(item))));
        }
    }
}
