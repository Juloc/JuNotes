using JuNotes.Backend.Backup;
using JuNotes.Backend.Infrastructure.Sql;
using JuNotes.Backend.School;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
            services.TryAddSingleton(TimeProvider.System);
            var backup = new BackupOptions();
            configuration.GetSection("Backup").Bind(backup);
            services.AddSingleton(backup);
            services.AddSingleton(provider => new BackupService(connectionString, backup, provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<BackupService>>()));
            var school = new SchoolOptions();
            configuration.GetSection("School").Bind(school);
            services.AddSingleton(school);
            services.AddHttpClient<IFeedFetcher, FeedFetcher>(client => client.DefaultRequestHeaders.UserAgent.ParseAdd("JuNotes"));
            services.AddSingleton<SchoolStore>();
            services.AddSingleton<SchoolImportService>();
            services.AddSingleton<HealthService>();
            services.AddSingleton<SyncService>();
            services.AddSingleton(new SyncAccess(configuration["Sync:Token"]));
            return services;
        }
    }
}
