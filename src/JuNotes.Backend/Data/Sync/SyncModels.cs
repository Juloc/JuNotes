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
        // A picture, text box, sticker or recording on a page.
        public const string PageObject = "object";

        public static readonly IReadOnlySet<string> All = new HashSet<string> { Subject, Notebook, Page, Stroke, Grade, Card, PageObject };
    }

    // What a device sends: one entity created, changed or deleted.
    public sealed record SyncChange(string Kind, Guid Id, Guid? ParentId, JsonElement? Payload, bool Deleted);

    // What the server returns: the same, plus the position in the change order.
    public sealed record SyncRecord(string Kind, Guid Id, Guid? ParentId, JsonElement? Payload, bool Deleted, long Seq);

    public sealed record PushRequest(IReadOnlyList<SyncChange> Changes);

    public sealed record PushResponse(int Accepted, long Seq);

    public sealed record PullResponse(IReadOnlyList<SyncRecord> Changes, long Cursor, bool More);

    // Who is writing: the person the instance token stands for, as a name for the security log, and the way they came in. AdoptsUnowned is true only for the person the
    // app token stands for: rows that nobody owns yet (made before the first account, or pushed while the first claim runs) are theirs by definition, and a push of such a
    // row takes it. Nobody else may.
    public sealed record SyncOwner(Guid Id, string Name, string Device, bool AdoptsUnowned = false);

    public static class SecurityRules
    {
        // A change to a row that is somebody else's.
        public const string ForeignRow = "sync.foreign_row";

        // A change that would hang a row under a parent that is somebody else's.
        public const string ForeignParent = "sync.foreign_parent";
    }

    // Where the server writes down that a rule was broken: the person, the device, the kind of rule and how many; never what was in it.
    public interface ISecurityLog
    {
        void RuleBroken(SyncOwner owner, string rule, int count);
    }

    // The request itself is wrong (a bad kind, too many changes), as opposed to the server failing.
    public sealed class SyncValidationException(string message) : Exception(message);
}
