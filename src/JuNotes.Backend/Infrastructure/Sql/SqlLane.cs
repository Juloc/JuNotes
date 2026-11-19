namespace JuNotes.Backend.Infrastructure.Sql
{
    // Pages and the sync API read short statements on the web lane; background work gets its own pool so it never starves a request.
    public enum SqlLane
    {
        Web,
        Worker,
    }
}
