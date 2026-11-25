using Base.Accounts;
using Base.Core;
using Base.Web;
using Base.Web.Auth;
using JuNotes.Frontend.Admin;

namespace JuNotes.Frontend.Api
{
    public sealed record PairingRequest(string? Code);

    public sealed record PairingCode(string Code, DateTimeOffset ExpiresAt);

    public sealed record DeviceDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastSeen, string? Address);

    // The devices of the person who is signed in (ARCHITECTURE 10.1), behind the session. Everything here is about the person's own devices only: another person's device is a 404 like
    // one that does not exist. A pairing code is only made against a fresh code of the person's second factor (a session alone is not enough, as with the password), and the
    // code of the second factor is spent, so the same one cannot be used twice. Nothing here ever returns a token or a hash.
    internal static class DevicesApi
    {
        public static void MapDevicesApi(this IEndpointRouteBuilder app)
        {
            var devices = app.MapGroup(WebApi.Prefix + "/devices");

            devices.MapGet(string.Empty, async (int? limit, string? cursor, HttpContext ctx, DeviceStore store) =>
                WebApi.Paged((await store.ListAsync(ctx.Person().UserId, ctx.RequestAborted)).Select(device => new DeviceDto(device.Id, device.Name, device.CreatedAt, device.LastSeen, DeviceStore.Shorten(device.LastAddress))).ToList(), limit, cursor, device => device.Id));

            devices.MapPost("/pairing", async (PairingRequest body, HttpContext ctx, DeviceStore store, FreshSecondFactor factor, AuditStore audit) =>
            {
                var who = ctx.Person();
                if (await factor.CheckAsync(ctx, body.Code) is { } refused)
                {
                    return refused.ToHttp();
                }

                var (code, expires) = await store.NewCodeAsync(who.UserId, ctx.RequestAborted);
                await audit.AddAsync(who.UserName, "pairing.code_made", ctx.ClientIp(), null, ctx.RequestAborted);
                return Results.Ok(new PairingCode(code, expires));
            });

            devices.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, DeviceStore store, AuditStore audit) =>
            {
                var who = ctx.Person();
                if (await store.RemoveAsync(who.UserId, id, ctx.RequestAborted) is not { } name)
                {
                    return Error.NotFound().ToHttp();
                }

                await audit.AddAsync(who.UserName, "device.removed", ctx.ClientIp(), "device " + name, ctx.RequestAborted);
                return Results.NoContent();
            });
        }
    }
}
