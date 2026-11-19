using JuNotes.Backend.Backup;
using JuNotes.Backend.School;
using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Workers
{
    // Workers only run System services. Phase 2 adds the Untis worker, Phase 3 the AI queue.
    public static class WorkerRegistration
    {
        public static IServiceCollection AddJuNotesWorkers(this IServiceCollection services) => services.AddHostedService<BackupWorker>().AddHostedService<SchoolWorker>();
    }
}
