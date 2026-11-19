using System.Text.Json;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;

namespace JuNotes.Backend.Tests
{
    public class SyncValidationTests
    {
        private static readonly JsonElement Empty = JsonElement.Parse("{}");

        private static void Rejects(params SyncChange[] changes) => Assert.Throws<SyncValidationException>(() => SyncService.Validate(new PushRequest(changes)));

        [Fact]
        public void A_valid_push_passes() => SyncService.Validate(new PushRequest([
            new SyncChange(EntityKinds.Subject, Guid.NewGuid(), null, Empty, false),
            new SyncChange(EntityKinds.Stroke, Guid.NewGuid(), Guid.NewGuid(), null, true),
        ]));

        [Fact]
        public void An_empty_push_is_rejected() => Rejects();

        [Fact]
        public void An_unknown_kind_is_rejected() => Rejects(new SyncChange("folder", Guid.NewGuid(), null, Empty, false));

        [Fact]
        public void An_empty_id_is_rejected() => Rejects(new SyncChange(EntityKinds.Subject, Guid.Empty, null, Empty, false));

        [Theory]
        [InlineData(EntityKinds.Page)]
        [InlineData(EntityKinds.Stroke)]
        public void Pages_and_strokes_need_a_parent(string kind) => Rejects(new SyncChange(kind, Guid.NewGuid(), null, Empty, false));

        [Fact]
        public void A_change_that_is_not_a_deletion_needs_a_payload() => Rejects(new SyncChange(EntityKinds.Subject, Guid.NewGuid(), null, null, false));

        [Fact]
        public void Too_many_changes_are_rejected() =>
            Rejects(Enumerable.Range(0, SyncService.MaxChanges + 1).Select(_ => new SyncChange(EntityKinds.Subject, Guid.NewGuid(), null, Empty, false)).ToArray());

        [Fact]
        public void A_huge_payload_is_rejected() =>
            Rejects(new SyncChange(EntityKinds.Subject, Guid.NewGuid(), null, JsonElement.Parse("\"" + new string('x', SyncService.MaxPayloadBytes) + "\""), false));
    }
}
