using JuNotes.Backend.Infrastructure.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Backend.Service
{
    public static class ServiceRegistration
    {
        public static IServiceCollection AddJuNotesServices(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("JuNotes")
                ?? throw new InvalidOperationException("ConnectionStrings:JuNotes is not set (environment variable ConnectionStrings__JuNotes).");
            services.AddSingleton(_ => SqlDatabase.Create(connectionString, SqlLane.Web, 20, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
            services.AddSingleton(provider => provider.GetRequiredService<SqlDatabase>().DataSource);
            services.AddSingleton<HealthService>();
            return services;
        }
    }
}
