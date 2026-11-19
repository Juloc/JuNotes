using System.Text.Json;

namespace JuNotes.Backend.Data.Sync
{
    public static class EntityKinds
    {
        public const string Subject = "subject";
        public const string Notebook = "notebook";
        public const string Page = "page";
        public const string Stroke = "stroke";
        public const string Grade = "grade";
        public const string Card = "card";

        public static readonly IReadOnlySet<string> All = new HashSet<string> { Subject, Notebook, Page, Stroke, Grade, Card };
    }

    // What a device sends: one entity created, changed or deleted.
    public sealed record SyncChange(string Kind, Guid Id, Guid? ParentId, JsonElement? Payload, bool Deleted);

    // What the server returns: the same, plus the position in the change order.
    public sealed record SyncRecord(string Kind, Guid Id, Guid? ParentId, JsonElement? Payload, bool Deleted, long Seq);

    public sealed record PushRequest(IReadOnlyList<SyncChange> Changes);

    public sealed record PushResponse(int Accepted, long Seq);

    public sealed record PullResponse(IReadOnlyList<SyncRecord> Changes, long Cursor, bool More);

    // The request itself is wrong (a bad kind, too many changes), as opposed to the server failing.
    public sealed class SyncValidationException(string message) : Exception(message);
}
