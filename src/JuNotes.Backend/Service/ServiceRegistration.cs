using JuNotes.Backend.Ai;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Backup;
using JuNotes.Backend.Infrastructure.Sql;
using JuNotes.Backend.School;
using JuNotes.Integrations.Codex;
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
            var assets = new AssetOptions();
            configuration.GetSection("Assets").Bind(assets);
            services.AddSingleton(assets);
            services.AddSingleton<AssetService>();
            var backup = new BackupOptions();
            configuration.GetSection("Backup").Bind(backup);
            // The backup takes the scanned files along with the database.
            backup.AssetsDirectory ??= assets.Directory;
            services.AddSingleton(backup);
            services.AddSingleton(provider => new BackupService(connectionString, backup, provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<BackupService>>()));
            var codex = new CodexOptions();
            configuration.GetSection("Codex").Bind(codex);
            services.AddSingleton(codex);
            services.AddSingleton<CodexAccount>();
            services.AddSingleton<ICodexRunner, CodexRunner>();
            var ai = new AiOptions();
            configuration.GetSection("Ai").Bind(ai);
            services.AddSingleton(ai);
            services.AddSingleton<AiProfileStore>();
            services.AddSingleton<AiUsageStore>();
            services.AddSingleton<LearningService>();
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
