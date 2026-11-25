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

            devices.MapPost("/pairing", async (PairingRequest body, HttpContext ctx, DeviceStore store, UserStore users, LoginLimiter limiter, AuditStore audit, TimeProvider time) =>
            {
                var who = ctx.Person();
                var address = ctx.ClientIp();
                string[] keys = ["acct:" + who.UserId, "ip:" + address];
                if (limiter.WaitSeconds(keys) is > 0 and var wait)
                {
                    return Error.TooMany(wait).ToHttp();
                }

                var user = await users.FindByIdAsync(who.UserId, ctx.RequestAborted);
                if (user is null || user.State != PersonState.Active)
                {
                    return Error.Unauthorized().ToHttp();
                }

                // A person who signs in only through the hub has no second factor here to ask for.
                if (!user.TotpConfirmed)
                {
                    return Error.Conflict("pairing.needs_totp", "Pairing needs a second factor in this account.").ToHttp();
                }

                var step = Totp.Check(user.TotpSecret, (body.Code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal), user.TotpLastStep, time.GetUtcNow());
                if (step is null || !await users.TryAdvanceTotpStepAsync(user.Id, step.Value, ctx.RequestAborted))
                {
                    foreach (var key in keys)
                    {
                        limiter.Fail(key);
                    }

                    return new Error(ErrorKind.Forbidden, "totp.wrong", "The code is not right.").ToHttp();
                }

                limiter.Succeed("acct:" + user.Id);
                var (code, expires) = await store.NewCodeAsync(user.Id, ctx.RequestAborted);
                await audit.AddAsync(user.Name, "pairing.code_made", address, null, ctx.RequestAborted);
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
