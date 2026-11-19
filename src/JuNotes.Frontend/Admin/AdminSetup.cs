using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;

namespace JuNotes.Frontend.Admin
{
    public static class AdminSetup
    {
        public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

        public static IServiceCollection AddJuNotesAdmin(this IServiceCollection services, IConfiguration configuration)
        {
            var options = new AdminOptions();
            configuration.GetSection("Admin").Bind(options);
            services.AddSingleton(options);
            services.AddSingleton<AdminAccess>();
            services.AddSingleton<CodexLoginSession>();

            services.AddAuthentication(Scheme).AddCookie(cookie =>
            {
                cookie.LoginPath = "/Admin/Login";
                cookie.Cookie.Name = "junotes-admin";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                // Secure whenever the request came in over https, which behind the reverse proxy the forwarded headers tell.
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                cookie.SlidingExpiration = true;
            });
            services.AddAuthorization();

            // The server listens on the loopback address only, behind the reverse proxy, so the proxy's headers can be believed.
            services.Configure<ForwardedHeadersOptions>(forwarded =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                forwarded.KnownIPNetworks.Clear();
                forwarded.KnownProxies.Clear();
            });
            return services;
        }
    }
}
