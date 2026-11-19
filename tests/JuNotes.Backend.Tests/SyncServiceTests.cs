using System.Text.Json;
using JuNotes.Backend.Data.Sync;
using JuNotes.Backend.Service;

namespace JuNotes.Backend.Tests
{
    public class SyncServiceTests
    {
        private static readonly Guid Page = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

        private static JsonElement Json(string text) => JsonElement.Parse(text);

        private static SyncChange Stroke(int n, bool deleted = false) =>
            new(EntityKinds.Stroke, Guid.Parse($"00000000-0000-0000-0000-00000000010{n}"), Page, deleted ? null : Json($"{{\"n\":{n}}}"), deleted);

        private static SyncChange Notebook(string title, bool deleted = false) =>
            new(EntityKinds.Notebook, Guid.Parse("00000000-0000-0000-0000-0000000000b1"), null, deleted ? null : Json($"{{\"title\":\"{title}\"}}"), deleted);

        private static PushRequest Push(params SyncChange[] changes) => new(changes);

        [RequiresDatabaseFact]
        public async Task A_push_shows_up_in_a_pull_in_order()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);

            var pushed = await service.PushAsync(Push(Notebook("Mathe"), Stroke(1), Stroke(2)), CancellationToken.None);
            var pulled = await service.PullAsync(0, 100, CancellationToken.None);

            Assert.Equal(3, pushed.Accepted);
            Assert.Equal(pushed.Seq, pulled.Cursor);
            Assert.False(pulled.More);
            Assert.Equal([EntityKinds.Notebook, EntityKinds.Stroke, EntityKinds.Stroke], pulled.Changes.Select(item => item.Kind));
            Assert.True(pulled.Changes.Zip(pulled.Changes.Skip(1)).All(pair => pair.First.Seq < pair.Second.Seq));
            Assert.Equal("Mathe", pulled.Changes[0].Payload!.Value.GetProperty("title").GetString());
        }

        [RequiresDatabaseFact]
        public async Task A_pull_returns_only_what_is_newer_than_the_cursor()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            var first = await service.PushAsync(Push(Stroke(1)), CancellationToken.None);
            await service.PushAsync(Push(Stroke(2)), CancellationToken.None);

            var pulled = await service.PullAsync(first.Seq, 100, CancellationToken.None);

            Assert.Single(pulled.Changes);
            Assert.Equal(2, pulled.Changes[0].Payload!.Value.GetProperty("n").GetInt32());
        }

        [RequiresDatabaseFact]
        public async Task A_pull_pages_through_large_changes_with_more_set()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            await service.PushAsync(Push(Stroke(1), Stroke(2), Stroke(3)), CancellationToken.None);

            var first = await service.PullAsync(0, 2, CancellationToken.None);
            var second = await service.PullAsync(first.Cursor, 2, CancellationToken.None);

            Assert.Equal(2, first.Changes.Count);
            Assert.True(first.More);
            Assert.Single(second.Changes);
            Assert.False(second.More);
        }

        [RequiresDatabaseFact]
        public async Task A_pull_with_nothing_new_keeps_the_cursor()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var pulled = await new SyncService(database.Data).PullAsync(42, 100, CancellationToken.None);

            Assert.Empty(pulled.Changes);
            Assert.Equal(42, pulled.Cursor);
        }

        [RequiresDatabaseFact]
        public async Task The_last_write_wins_for_a_notebook()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            await service.PushAsync(Push(Notebook("Mathe")), CancellationToken.None);
            await service.PushAsync(Push(Notebook("Mathematik")), CancellationToken.None);

            var pulled = await service.PullAsync(0, 100, CancellationToken.None);

            Assert.Single(pulled.Changes);
            Assert.Equal("Mathematik", pulled.Changes[0].Payload!.Value.GetProperty("title").GetString());
        }

        [RequiresDatabaseFact]
        public async Task Pushing_the_same_change_again_does_not_create_a_new_change()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            var first = await service.PushAsync(Push(Notebook("Mathe"), Stroke(1)), CancellationToken.None);

            var again = await service.PushAsync(Push(Notebook("Mathe"), Stroke(1)), CancellationToken.None);

            Assert.Equal(first.Seq, again.Seq);
            Assert.Empty((await service.PullAsync(first.Seq, 100, CancellationToken.None)).Changes);
        }

        [RequiresDatabaseFact]
        public async Task A_stroke_is_never_overwritten()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            await service.PushAsync(Push(Stroke(1)), CancellationToken.None);
            var changed = Stroke(1) with { Payload = Json("{\"n\":99}") };
            await service.PushAsync(Push(changed), CancellationToken.None);

            var pulled = await service.PullAsync(0, 100, CancellationToken.None);

            Assert.Equal(1, pulled.Changes[0].Payload!.Value.GetProperty("n").GetInt32());
        }

        [RequiresDatabaseFact]
        public async Task Deleting_a_stroke_is_a_change_a_second_device_pulls()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            var added = await service.PushAsync(Push(Stroke(1)), CancellationToken.None);
            await service.PushAsync(Push(Stroke(1, deleted: true)), CancellationToken.None);

            var pulled = await service.PullAsync(added.Seq, 100, CancellationToken.None);

            var record = Assert.Single(pulled.Changes);
            Assert.True(record.Deleted);
        }

        [RequiresDatabaseFact]
        public async Task A_deleted_stroke_does_not_come_back_when_an_old_device_pushes_it()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            await service.PushAsync(Push(Stroke(1)), CancellationToken.None);
            await service.PushAsync(Push(Stroke(1, deleted: true)), CancellationToken.None);
            await service.PushAsync(Push(Stroke(1)), CancellationToken.None);

            Assert.True((await service.PullAsync(0, 100, CancellationToken.None)).Changes[0].Deleted);
        }

        [RequiresDatabaseFact]
        public async Task Deleting_a_stroke_that_never_arrived_leaves_a_tombstone()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            await service.PushAsync(Push(Stroke(7, deleted: true)), CancellationToken.None);

            Assert.True(Assert.Single((await service.PullAsync(0, 100, CancellationToken.None)).Changes).Deleted);
        }

        [RequiresDatabaseFact]
        public async Task A_grade_is_stored_like_any_other_entity()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            var grade = new SyncChange(EntityKinds.Grade, Guid.NewGuid(), Page, Json("{\"value\":2.3}"), false);

            await service.PushAsync(Push(grade), CancellationToken.None);

            var pulled = await service.PullAsync(0, 10, CancellationToken.None);
            Assert.Equal(EntityKinds.Grade, Assert.Single(pulled.Changes).Kind);
        }

        [RequiresDatabaseFact]
        public async Task A_rejected_push_changes_nothing()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var service = new SyncService(database.Data);
            var bad = new SyncChange("stroke", Guid.NewGuid(), null, Json("{}"), false);

            await Assert.ThrowsAsync<SyncValidationException>(() => service.PushAsync(Push(Notebook("Mathe"), bad), CancellationToken.None));

            Assert.Equal(0, await database.CountAsync("entities"));
        }
    }
}
