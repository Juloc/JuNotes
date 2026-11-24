using System.Security.Claims;
using System.Text.Encodings.Web;
using Base.Accounts;
using Base.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace JuNotes.Frontend.Admin
{
    // Until the web pages move to the base (milestone 2), the old Razor pages stand behind the session of the base: the same cookie, the same sessions in the
    // database, one place that reads them. Nobody signs in here; the page of the base does that, and a visitor without a session is sent to it.
    public static class AdminSetup
    {
        public const string Scheme = "BaseSession";
        public const string AdminPolicy = "Admin";
        public const string ReaderPolicy = "Reader";

        // The person behind the session, for the pages that show only their notes.
        public static Guid PersonId(this System.Security.Claims.ClaimsPrincipal user) => Guid.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        public static IServiceCollection AddJuNotesSignIn(this IServiceCollection services)
        {
            services.AddSingleton<CodexLoginSession>();
            services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, BaseSessionHandler>(Scheme, null);
            services.AddAuthorization(authorization =>
            {
                authorization.AddPolicy(AdminPolicy, policy => policy.RequireRole(Roles.Admin));
                // Whoever may administer may read as well; every other person reads their own notes.
                authorization.AddPolicy(ReaderPolicy, policy => policy.RequireRole(Roles.Admin, "member"));
            });
            return services;
        }
    }

    internal sealed class BaseSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SessionCookie cookie, AccountService accounts, OwnerDirectory owners)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (cookie.Read(Request) is not { } token)
            {
                return AuthenticateResult.NoResult();
            }

            var who = await accounts.ResolveSessionAsync(token, rotate: true, Context.RequestAborted);
            if (who is null)
            {
                return AuthenticateResult.NoResult();
            }

            if (who.NewToken is { } fresh)
            {
                cookie.Write(Context, fresh);
            }

            // The first account may have just been made: the rows nobody owns yet are given to it.
            await owners.EnsureClaimedAsync(Context.RequestAborted);

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, who.UserId.ToString()), new(ClaimTypes.Name, who.UserName) };
            claims.AddRange(who.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Redirect("/");
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
