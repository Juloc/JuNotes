using Base.Accounts;
using Base.Core;
using Base.Web;
using Base.Web.Auth;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Library;
using JuNotes.Frontend.Admin;

namespace JuNotes.Frontend.Api
{
    public sealed record PersonDto(Guid Id, string Name);

    public sealed record ShareRequest(Guid? RecipientId);

    public sealed record ShareDto(Guid Id, Guid RecipientId, string RecipientName, DateTimeOffset CreatedAt);

    public sealed record SharedNotebookDto(Guid Id, string Title, string Type, string Template, DateTimeOffset? CreatedAt, CoverInfo? Cover, string OwnerName);

    public sealed record SharedNotebookDetail(SharedNotebookDto Notebook, int PageCount);

    // Sharing one notebook, read only (ARCHITECTURE 10.2), behind the session. The share is asked of the table at **every** request and nothing is kept, so taking it back works
    // with the next request. A recipient reads through the owner's reader of that one notebook: its pages, strokes, objects and the files that occur in it. Nothing else of the
    // owner is reachable, and the sync of the recipient is not touched (the pull gives only their own rows). Whatever is not shared, or is gone, is a 404.
    internal static class SharesApi
    {
        public static void MapSharesApi(this IEndpointRouteBuilder app)
        {
            var api = app.MapGroup(WebApi.Prefix);

            // Only the display names of the other people of this installation, for the dialog that picks who to share with. Never an account, a role or a state.
            api.MapGet("/people", async (int? limit, string? cursor, HttpContext ctx, ShareStore shares) =>
                WebApi.Paged((await shares.PeopleAsync(ctx.Person().UserId, ctx.RequestAborted)).Select(person => new PersonDto(person.Id, person.Name)).ToList(), limit, cursor, person => person.Id));

            // ----- the owner's side -----

            api.MapGet("/notebooks/{id:guid}/shares", async (Guid id, int? limit, string? cursor, HttpContext ctx, Libraries libraries, ShareStore shares) =>
            {
                var me = ctx.Person().UserId;
                return await libraries.Of(me).NotebookAsync(id, ctx.RequestAborted) is null
                    ? Error.NotFound().ToHttp()
                    : WebApi.Paged((await shares.ForNotebookAsync(me, id, ctx.RequestAborted)).Select(Dto).ToList(), limit, cursor, share => share.Id);
            });

            api.MapPost("/notebooks/{id:guid}/shares", async (Guid id, ShareRequest body, HttpContext ctx, ShareStore shares, AuditStore audit) =>
            {
                var who = ctx.Person();
                if (body.RecipientId is not { } recipient)
                {
                    return new Error(ErrorKind.Invalid, "input", "A person is needed.", new Dictionary<string, string> { ["recipientId"] = "required" }).ToHttp();
                }

                var (outcome, share) = await shares.CreateAsync(who.UserId, id, recipient, ctx.RequestAborted);
                switch (outcome)
                {
                    case ShareOutcome.Self:
                        return Error.Invalid("share.self", "A notebook is shared with somebody else.").ToHttp();
                    case ShareOutcome.NotebookNotFound:
                        return Error.NotFound().ToHttp();
                    case ShareOutcome.RecipientNotFound:
                        return Error.NotFound("share.person", "That person is not there.").ToHttp();
                    case ShareOutcome.Created:
                        await audit.AddAsync(who.UserName, "share.created", ctx.ClientIp(), $"notebook {id} with {share!.RecipientName}", ctx.RequestAborted);
                        return Results.Ok(Dto(share));
                    default:
                        return Results.Ok(Dto(share!));
                }
            });

            // Taking a share back: only its owner can; somebody else's, an unknown and a taken-back one are the same 404.
            api.MapDelete("/shares/{id:guid}", async (Guid id, HttpContext ctx, ShareStore shares, AuditStore audit) =>
            {
                var who = ctx.Person();
                if (await shares.RevokeAsync(who.UserId, id, ctx.RequestAborted) is not { } taken)
                {
                    return Error.NotFound().ToHttp();
                }

                await audit.AddAsync(who.UserName, "share.revoked", ctx.ClientIp(), $"notebook {taken.Notebook} with {taken.Recipient}", ctx.RequestAborted);
                return Results.NoContent();
            });

            // ----- the recipient's side -----

            var shared = api.MapGroup("/shared");

            shared.MapGet(string.Empty, async (int? limit, string? cursor, HttpContext ctx, Libraries libraries, ShareStore shares) =>
            {
                var live = await shares.ForRecipientAsync(ctx.Person().UserId, ctx.RequestAborted);
                var list = new List<SharedNotebookDto>();
                foreach (var owner in live.GroupBy(share => share.OwnerId))
                {
                    // Through the reader of the owner: a notebook that is deleted or in the trash is not in it, so it is not in the list either.
                    var ids = owner.ToDictionary(share => share.NotebookId, share => share.OwnerName);
                    foreach (var notebook in await libraries.Of(owner.Key).NotebooksAsync(ctx.RequestAborted))
                    {
                        if (ids.TryGetValue(notebook.Id, out var ownerName))
                        {
                            list.Add(Shared(notebook, ownerName));
                        }
                    }
                }

                return WebApi.Paged(list, limit, cursor, notebook => notebook.Id);
            });

            shared.MapGet("/notebooks/{id:guid}", async (Guid id, HttpContext ctx, Libraries libraries, ShareStore shares) =>
            {
                if (await Open(ctx, id, shares, libraries) is not var (share, reader, notebook))
                {
                    return Error.NotFound().ToHttp();
                }

                return Results.Ok(new SharedNotebookDetail(Shared(notebook, share.OwnerName), (await reader.PagesAsync(id, ctx.RequestAborted)).Count));
            });

            shared.MapGet("/notebooks/{id:guid}/pages", async (Guid id, int? limit, string? cursor, HttpContext ctx, Libraries libraries, ShareStore shares) =>
            {
                if (await Open(ctx, id, shares, libraries) is not var (_, reader, _))
                {
                    return Error.NotFound().ToHttp();
                }

                return WebApi.Paged((await reader.PagesAsync(id, ctx.RequestAborted)).Select(WebApi.Page).ToList(), limit, cursor, page => page.Id);
            });

            // The page is looked up through the share of the notebook it lies in; the files on it are fetched from the shared path, which checks the same share.
            shared.MapGet("/pages/{id:guid}", async (Guid id, HttpContext ctx, Libraries libraries, ShareStore shares) =>
            {
                if (await shares.FindForPageAsync(ctx.Person().UserId, id, ctx.RequestAborted) is not { } share)
                {
                    return Error.NotFound().ToHttp();
                }

                var reader = libraries.Of(share.OwnerId);
                if (await reader.PageAsync(id, ctx.RequestAborted) is not { } page || page.NotebookId != share.NotebookId || await reader.NotebookAsync(share.NotebookId, ctx.RequestAborted) is not { } notebook)
                {
                    return Error.NotFound().ToHttp();
                }

                return Results.Ok(await WebApi.ViewAsync(reader, page, notebook, WebApi.Prefix + "/shared/assets/", ctx.RequestAborted));
            });

            // A file is given only when it occurs in a notebook shared with this person (its cover, the scan or PDF of a page, a picture, sticker or recording on a page) **and**
            // belongs to the person who shares it (a notebook can name any id, also that of somebody else's file).
            shared.MapGet("/assets/{id:guid}", async (Guid id, HttpContext ctx, Libraries libraries, ShareStore shares, AssetService assets) =>
            {
                foreach (var share in await shares.ForRecipientAsync(ctx.Person().UserId, ctx.RequestAborted))
                {
                    if (await libraries.Of(share.OwnerId).NotebookAsync(share.NotebookId, ctx.RequestAborted) is null
                        || !await shares.AssetInNotebookAsync(share.OwnerId, share.NotebookId, id, ctx.RequestAborted)
                        || await assets.OpenAsync(id, share.OwnerId, ctx.RequestAborted) is not { } file)
                    {
                        continue;
                    }

                    ctx.Response.Headers.XContentTypeOptions = "nosniff";
                    return Results.Stream(file.Stream, file.ContentType, enableRangeProcessing: true);
                }

                return Error.NotFound().ToHttp();
            });
        }

        // The share, the owner's reader and the notebook, or nothing: the share has to be there now, and the notebook neither deleted nor in the trash.
        private static async Task<(Share Share, LibraryReader Reader, NotebookInfo Notebook)?> Open(HttpContext ctx, Guid notebook, ShareStore shares, Libraries libraries)
        {
            if (await shares.FindAsync(ctx.Person().UserId, notebook, ctx.RequestAborted) is not { } share)
            {
                return null;
            }

            var reader = libraries.Of(share.OwnerId);
            return await reader.NotebookAsync(notebook, ctx.RequestAborted) is { } found ? (share, reader, found) : null;
        }

        private static ShareDto Dto(ShareRecipient share) => new(share.Id, share.RecipientId, share.RecipientName, share.CreatedAt);

        private static SharedNotebookDto Shared(NotebookInfo notebook, string ownerName) => new(notebook.Id, notebook.Title, notebook.Type, notebook.Template, notebook.CreatedAt, notebook.Cover, ownerName);
    }
}
