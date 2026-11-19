using Microsoft.Extensions.DependencyInjection;

namespace JuNotes.Workers
{
    // Workers only run System services. Phase 1 adds the backup worker, Phase 2 the Untis worker, Phase 3 the AI queue.
    public static class WorkerRegistration
    {
        public static IServiceCollection AddJuNotesWorkers(this IServiceCollection services) => services;
    }
}
