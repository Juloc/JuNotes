using Base.Accounts;
using Base.Core;
using Base.Web;
using Base.Web.Auth;
using JuNotes.Frontend.Admin;

namespace JuNotes.Frontend.Api
{
    public sealed record PairingRequest(string? Code);

    public sealed record AppPairingRequest(string? Challenge);

    public sealed record PairingCode(string Code, DateTimeOffset ExpiresAt);

    public sealed record DeviceDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastSeen, string? Address);

    // The devices of the person who is signed in (ARCHITECTURE 10.1), behind the session. Everything here is about the person's own devices only: another person's device is a 404 like
    // one that does not exist. A pairing code is only made against a fresh code of the person's second factor (a session alone is not enough, as with the password), and the
    // code of the second factor is spent, so the same one cannot be used twice. Nothing here ever returns a token or a hash.
    internal static partial class DevicesApi
    {
        // How old a sign-in may be to connect an app from its sign-in window.
        public static readonly TimeSpan FreshSignIn = TimeSpan.FromMinutes(5);

        [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
        private static partial System.Text.RegularExpressions.Regex ChallengeShape();

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

            // The sign-in window of the app (/#/app-login): the person has just signed in there, through the hub or with password and second factor, so the sign-in itself is the
            // fresh proof that a code of the second factor is elsewhere (people who sign in through the hub have none here). Only a session made in the last few minutes counts;
            // an older one (a browser that was signed in already) is refused and the page asks to sign in again. The code goes to the app at once and is redeemed by it.
            devices.MapPost("/pairing/app", async (AppPairingRequest body, HttpContext ctx, DeviceStore store, AuditStore audit) =>
            {
                var who = ctx.Person();
                // The challenge binds the code to the app that opened the window (PKCE, S256): without the app's secret the code is worth nothing to whoever catches it.
                if (body.Challenge is not { } challenge || !ChallengeShape().IsMatch(challenge))
                {
                    return Error.Invalid("pairing.no_challenge", "Open this page from the app.").ToHttp();
                }

                if (!await store.SignedInJustNowAsync(who.Id, who.UserId, FreshSignIn, ctx.RequestAborted))
                {
                    return Error.Forbidden("pairing.not_fresh", "Sign in again: only a sign-in of the last few minutes can connect an app.").ToHttp();
                }

                var (code, expires) = await store.NewCodeAsync(who.UserId, ctx.RequestAborted, challenge);
                await audit.AddAsync(who.UserName, "pairing.code_made", ctx.ClientIp(), "sign-in window of the app", ctx.RequestAborted);
                return Results.Ok(new PairingCode(code, expires));
            });

            // All the person's own devices at once (the remedy for a lost device, and for a changed password or second factor: device tokens survive those on purpose, see STATE.md).
            devices.MapDelete(string.Empty, async (HttpContext ctx, DeviceStore store, AuditStore audit) =>
            {
                var who = ctx.Person();
                var removed = (await store.RevokeAllAsync(who.UserId, ctx.RequestAborted))?.Revoked ?? 0;
                await audit.AddAsync(who.UserName, "device.removed_all", ctx.ClientIp(), $"{removed} device(s)", ctx.RequestAborted);
                return Results.Ok(new { removed });
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
