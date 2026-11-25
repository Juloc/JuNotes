using JuNotes.Backend.Ai;
using JuNotes.Backend.Assets;
using JuNotes.Backend.Backup;
using JuNotes.Backend.Library;
using JuNotes.Backend.Infrastructure.Sql;
using JuNotes.Backend.School;
using JuNotes.Integrations.Codex;
using JuNotes.Integrations.Paperless;
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
            // The key of the server: the file the base keeps in BASE_DATA (there is none when the key comes from a setting).
            backup.KeyFile ??= Path.Combine(configuration["BASE_DATA"] ?? "data", "secret.key");
            services.AddSingleton(backup);
            services.AddSingleton(provider =>
            {
                var destinations = new List<IBackupDestination>();
                if (!string.IsNullOrWhiteSpace(backup.CopyTo))
                {
                    destinations.Add(new FolderDestination(backup.CopyTo));
                }

                if (backup.Drive.IsComplete)
                {
                    // Uploads can take a while, so this client is not held to the usual hundred seconds.
                    destinations.Add(new GoogleDriveDestination(new HttpClient { Timeout = TimeSpan.FromMinutes(30) }, backup.Drive));
                }

                return new BackupMirror(destinations, backup.Passphrase, backup.Keep, provider.GetRequiredService<ILogger<BackupMirror>>());
            });
            services.AddSingleton(provider => new BackupService(connectionString, backup, provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<BackupService>>(), provider.GetRequiredService<BackupMirror>()));
            var codex = new CodexOptions();
            configuration.GetSection("Codex").Bind(codex);
            services.AddSingleton(codex);
            services.AddSingleton<CodexAccount>();
            services.AddSingleton<ICodexRunner, CodexRunner>();
            var paperless = new PaperlessOptions();
            configuration.GetSection("Paperless").Bind(paperless);
            services.AddSingleton(paperless);
            services.AddSingleton(_ => new PaperlessClient(new HttpClient { Timeout = PaperlessClient.UploadTimeout }, paperless));
            var ai = new AiOptions();
            configuration.GetSection("Ai").Bind(ai);
            services.AddSingleton(ai);
            services.AddSingleton<AiProfileStore>();
            services.AddSingleton<AiUsageStore>();
            services.AddSingleton<AiSettingsStore>();
            services.AddSingleton<LearningService>();
            var school = new SchoolOptions();
            configuration.GetSection("School").Bind(school);
            services.AddSingleton(school);
            services.AddHttpClient<IFeedFetcher, FeedFetcher>(client => client.DefaultRequestHeaders.UserAgent.ParseAdd("JuNotes"));
            services.AddHttpClient<IUntisSource, UntisClient>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("JuNotes");
                client.Timeout = TimeSpan.FromSeconds(40);
            });
            services.AddSingleton<SchoolStore>();
            services.AddSingleton<SchoolImportService>();
            services.AddSingleton<Libraries>();
            services.AddSingleton<ShareStore>();
            services.AddSingleton<HealthService>();
            services.AddSingleton<SyncService>();
            services.AddSingleton(new SyncAccess(configuration["Sync:Token"]));
            return services;
        }
    }
}
