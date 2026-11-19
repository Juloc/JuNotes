using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;

namespace JuNotes.Frontend.Admin
{
    public static class AdminSetup
    {
        public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        public const string AdminPolicy = "Admin";
        public const string ReaderPolicy = "Reader";

        public static IServiceCollection AddJuNotesAdmin(this IServiceCollection services, IConfiguration configuration)
        {
            var options = new AdminOptions();
            configuration.GetSection("Admin").Bind(options);
            services.AddSingleton(options);
            var web = new WebOptions();
            configuration.GetSection("Web").Bind(web);
            services.AddSingleton(web);
            services.AddSingleton<AdminAccess>();
            services.AddSingleton<ReaderAccess>();
            services.AddSingleton<CodexLoginSession>();

            services.AddAuthentication(Scheme).AddCookie(cookie =>
            {
                cookie.LoginPath = "/Login";
                cookie.AccessDeniedPath = "/Admin/Login";
                // The admin area has its own door; everything else uses the reader's.
                cookie.Events.OnRedirectToLogin = context =>
                {
                    var target = context.Request.Path.StartsWithSegments("/Admin") ? "/Admin/Login" : "/Login";
                    context.Response.Redirect(target + "?ReturnUrl=" + Uri.EscapeDataString(context.Request.Path + context.Request.QueryString));
                    return Task.CompletedTask;
                };
                cookie.Cookie.Name = "junotes-admin";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                // Secure whenever the request came in over https, which behind the reverse proxy the forwarded headers tell.
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                cookie.SlidingExpiration = true;
            });
            services.AddAuthorization(authorization =>
            {
                authorization.AddPolicy(AdminPolicy, policy => policy.RequireRole("admin"));
                // Whoever may administer may read as well.
                authorization.AddPolicy(ReaderPolicy, policy => policy.RequireRole("admin", "reader"));
            });

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
