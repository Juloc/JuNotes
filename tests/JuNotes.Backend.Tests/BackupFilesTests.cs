using JuNotes.Backend.Backup;

namespace JuNotes.Backend.Tests
{
    public sealed class BackupFilesTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "junotes-backups-" + Guid.NewGuid().ToString("N"));
        private static readonly DateTimeOffset Noon = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        public BackupFilesTests() => Directory.CreateDirectory(_directory);

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private string Touch(int daysAgo, BackupKind kind)
        {
            var path = Path.Combine(_directory, BackupFiles.NameFor(Noon.AddDays(-daysAgo), kind));
            File.WriteAllText(path, "x");
            return path;
        }

        [Fact]
        public void A_name_round_trips()
        {
            var name = BackupFiles.NameFor(Noon, BackupKind.BeforeMigration);

            Assert.Equal("junotes-20261119-120000-migration.dump", name);
            Assert.True(BackupFiles.TryParse(name, out var at, out var kind));
            Assert.Equal(Noon, at);
            Assert.Equal(BackupKind.BeforeMigration, kind);
        }

        [Theory]
        [InlineData("notes.dump")]
        [InlineData("junotes-20261119-120000.dump")]
        [InlineData("junotes-20261119-120000-other.dump")]
        [InlineData("junotes-2026-120000-manual.dump")]
        [InlineData("junotes-20261119-120000-manual.dump.partial")]
        [InlineData("junotes--manual.dump")]
        public void Other_files_are_not_backups(string name) => Assert.False(BackupFiles.TryParse(name, out _, out _));

        [Fact]
        public void The_list_is_newest_first_and_skips_foreign_files()
        {
            Touch(3, BackupKind.Automatic);
            Touch(1, BackupKind.Manual);
            Touch(2, BackupKind.Automatic);
            File.WriteAllText(Path.Combine(_directory, "readme.txt"), "x");
            File.WriteAllText(Path.Combine(_directory, "junotes-20261119-120000-automatic.dump.partial"), "x");

            var list = BackupFiles.List(_directory);

            Assert.Equal([1, 2, 3], list.Select(item => (int)(Noon - item.At).TotalDays));
        }

        [Fact]
        public void A_missing_folder_is_an_empty_list() => Assert.Empty(BackupFiles.List(Path.Combine(_directory, "nope")));

        [Fact]
        public void Rotation_keeps_the_newest_automatic_and_migration_backups_together()
        {
            var oldest = Touch(5, BackupKind.Automatic);
            var oldMigration = Touch(4, BackupKind.BeforeMigration);
            var kept1 = Touch(3, BackupKind.Automatic);
            var kept2 = Touch(2, BackupKind.BeforeMigration);

            var removed = BackupFiles.Rotate(_directory, keep: 2);

            Assert.Equal(2, removed.Count);
            Assert.False(File.Exists(oldest));
            Assert.False(File.Exists(oldMigration));
            Assert.True(File.Exists(kept1));
            Assert.True(File.Exists(kept2));
        }

        [Fact]
        public void Rotation_never_touches_manual_backups()
        {
            var manual = Touch(100, BackupKind.Manual);
            Touch(2, BackupKind.Automatic);
            Touch(1, BackupKind.Automatic);

            BackupFiles.Rotate(_directory, keep: 1);

            Assert.True(File.Exists(manual));
        }

        [Fact]
        public void Rotation_always_keeps_at_least_one()
        {
            var only = Touch(1, BackupKind.Automatic);

            BackupFiles.Rotate(_directory, keep: 0);

            Assert.True(File.Exists(only));
        }
    }
}
