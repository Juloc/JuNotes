using Base.Accounts;
using Base.Core;
using Base.Web;
using Base.Web.Auth;
using JuNotes.Backend.Service;
using JuNotes.Frontend.Admin;

namespace JuNotes.Frontend.Api
{
    public sealed record SharedTokenRequest(string? Code);

    public sealed record PersonDevicesDto(Guid Id, string Name, string State, int Devices);

    // The administration of devices (role admin): how many devices each person has, taking them all back, and the switch of the shared token. The administrator never sees a token,
    // a name of a device or an address of one here. Switching the shared token off or on needs a fresh code of the administrator's own second factor.
    internal static class DevicesAdminApi
    {
        public static void MapDevicesAdminApi(this IEndpointRouteBuilder app)
        {
            var admin = app.MapGroup(WebApi.Prefix + "/admin").RequireRole("admin");

            admin.MapGet("/devices", async (HttpContext ctx, DeviceStore devices) =>
                Results.Ok(new { people = (await devices.CountsAsync(ctx.RequestAborted)).Select(person => new PersonDevicesDto(person.Id, person.Name, person.State, person.Devices)).ToList() }));

            // All devices of a person at once: they get 401 with the next request. Nothing is asked (it only takes keys away); the page asks before.
            admin.MapPost("/people/{id:guid}/devices/revoke", async (Guid id, HttpContext ctx, DeviceStore devices, AuditStore audit) =>
            {
                if (await devices.RevokeAllAsync(id, ctx.RequestAborted) is not var (revoked, name))
                {
                    return Error.NotFound().ToHttp();
                }

                await audit.AddAsync(ctx.Person().UserName, "device.revoked_by_admin", ctx.ClientIp(), $"{revoked} device(s) of {name}", ctx.RequestAborted);
                return Results.Ok(new { revoked });
            });

            admin.MapGet("/shared-token", async (HttpContext ctx, SharedToken token, SyncAccess access) => Results.Ok(await Describe(token, access, ctx.RequestAborted)));

            admin.MapPost("/shared-token/off", async (SharedTokenRequest body, HttpContext ctx, SharedToken token, SyncAccess access, FreshSecondFactor factor, AuditStore audit) =>
            {
                if (await factor.CheckAsync(ctx, body.Code) is { } refused)
                {
                    return refused.ToHttp();
                }

                var state = await token.StateAsync(ctx.RequestAborted);
                await token.SetOffAsync(true, ctx.RequestAborted);
                await audit.AddAsync(ctx.Person().UserName, "sharedtoken.off", ctx.ClientIp(), $"{state.RecentAddresses} address(es) had used it in the last 30 days", ctx.RequestAborted);
                return Results.Ok(await Describe(token, access, ctx.RequestAborted));
            });

            // Back on only while the environment still carries the token: a token that is not there cannot be switched on.
            admin.MapPost("/shared-token/on", async (SharedTokenRequest body, HttpContext ctx, SharedToken token, SyncAccess access, FreshSecondFactor factor, AuditStore audit) =>
            {
                if (!access.IsConfigured)
                {
                    return Error.Conflict("sharedtoken.not_configured", "The server has no shared token in its environment any more, so it cannot be switched on.").ToHttp();
                }

                if (await factor.CheckAsync(ctx, body.Code) is { } refused)
                {
                    return refused.ToHttp();
                }

                await token.SetOffAsync(false, ctx.RequestAborted);
                await audit.AddAsync(ctx.Person().UserName, "sharedtoken.on", ctx.ClientIp(), null, ctx.RequestAborted);
                return Results.Ok(await Describe(token, access, ctx.RequestAborted));
            });
        }

        private static async Task<object> Describe(SharedToken token, SyncAccess access, CancellationToken ct)
        {
            var state = await token.StateAsync(ct);
            return new { configured = access.IsConfigured, enabled = !await token.IsOffAsync(ct), recentAddresses = state.RecentAddresses, lastUse = state.LastUse };
        }
    }
}
