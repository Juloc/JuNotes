using System.Diagnostics;
using JuNotes.Backend.Backup;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace JuNotes.Backend.Tests
{
    // The dump tool has to be at least as new as the server. Point JUNOTES_TEST_PGDUMP and JUNOTES_TEST_PGRESTORE at version 18 tools if the ones on the PATH are older.
    internal static class PostgresTools
    {
        public static string PgDump => Environment.GetEnvironmentVariable("JUNOTES_TEST_PGDUMP") ?? "pg_dump";

        public static string PgRestore => Environment.GetEnvironmentVariable("JUNOTES_TEST_PGRESTORE") ?? "pg_restore";
    }

    public sealed class BackupServiceTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "junotes-dump-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private BackupService Service(TestDatabase database, string? pgDump = null, int keep = 7)
        {
            var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(RequiresDatabaseFactAttribute.Variable)!) { Database = new NpgsqlConnectionStringBuilder(database.Data.ConnectionString).Database };
            return new BackupService(admin.ConnectionString, new BackupOptions { Directory = _directory, Keep = keep, PgDump = pgDump ?? PostgresTools.PgDump }, TimeProvider.System, NullLogger<BackupService>.Instance);
        }

        [RequiresDatabaseFact]
        public async Task A_backup_is_a_restorable_dump_with_the_data_in_it()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            await using (var insert = database.Data.CreateCommand("insert into instance_settings (key, value) values ('hello', 'world')"))
            {
                await insert.ExecuteNonQueryAsync();
            }

            using var service = Service(database);
            var file = await service.RunAsync(BackupKind.Manual, CancellationToken.None);

            Assert.True(file.Bytes > 0);
            Assert.True(File.Exists(file.Path));
            Assert.Equal(file.Path, service.Latest()!.Path);
            Assert.Empty(Directory.GetFiles(_directory, "*.partial"));

            // pg_restore can read it and the table is in it.
            var listing = Process.Start(new ProcessStartInfo(PostgresTools.PgRestore, ["--list", file.Path]) { RedirectStandardOutput = true })!;
            var text = await listing.StandardOutput.ReadToEndAsync();
            await listing.WaitForExitAsync();
            Assert.Equal(0, listing.ExitCode);
            Assert.Contains("instance_settings", text);
            Assert.Contains("entities", text);
        }

        [RequiresDatabaseFact]
        public async Task The_scanned_files_are_archived_beside_the_dump_and_rotated_with_it()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var assets = Path.Combine(_directory, "assets");
            Directory.CreateDirectory(assets);
            await File.WriteAllBytesAsync(Path.Combine(assets, "abc"), [0xFF, 0xD8, 0xFF, 1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(assets, "half.partial"), "x");
            var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(RequiresDatabaseFactAttribute.Variable)!) { Database = new NpgsqlConnectionStringBuilder(database.Data.ConnectionString).Database };
            var backups = Path.Combine(_directory, "backups");
            using var service = new BackupService(admin.ConnectionString, new BackupOptions { Directory = backups, PgDump = PostgresTools.PgDump, Keep = 1, AssetsDirectory = assets }, TimeProvider.System, NullLogger<BackupService>.Instance);

            var first = await service.RunAsync(BackupKind.Automatic, CancellationToken.None);
            var archive = BackupFiles.AssetsArchiveFor(first.Path);

            Assert.True(File.Exists(archive));
            using (var zip = System.IO.Compression.ZipFile.OpenRead(archive))
            {
                Assert.Equal(["abc"], zip.Entries.Select(entry => entry.Name));
            }

            await Task.Delay(1100);
            await service.RunAsync(BackupKind.Automatic, CancellationToken.None);

            Assert.False(File.Exists(first.Path));
            Assert.False(File.Exists(archive));
        }

        [RequiresDatabaseFact]
        public async Task Rotation_runs_after_each_backup()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            using var service = Service(database, keep: 2);

            for (var i = 0; i < 4; i++)
            {
                await service.RunAsync(BackupKind.Automatic, CancellationToken.None);
                await Task.Delay(1100);
            }

            Assert.Equal(2, BackupFiles.List(_directory).Count);
        }

        [RequiresDatabaseFact]
        public async Task A_missing_pg_dump_fails_with_a_clear_message_and_leaves_nothing()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            using var service = Service(database, pgDump: "/nonexistent/pg_dump");

            var error = await Assert.ThrowsAsync<BackupFailedException>(() => service.RunAsync(BackupKind.Manual, CancellationToken.None));

            Assert.Contains("pg_dump could not be started", error.Message);
            Assert.Empty(BackupFiles.List(_directory));
            Assert.Empty(Directory.GetFiles(_directory));
        }

        [RequiresDatabaseFact]
        public async Task A_failing_dump_leaves_no_partial_file()
        {
            await using var database = await TestDatabase.CreateCurrentAsync();
            var wrong = new BackupService("Host=127.0.0.1;Port=1;Username=x;Password=y;Database=z", new BackupOptions { Directory = _directory }, TimeProvider.System, NullLogger<BackupService>.Instance);

            await Assert.ThrowsAsync<BackupFailedException>(() => wrong.RunAsync(BackupKind.Manual, CancellationToken.None));

            Assert.Empty(Directory.GetFiles(_directory));
            wrong.Dispose();
        }

        [RequiresDatabaseFact]
        public async Task Pending_migrations_are_counted_and_a_new_database_has_none()
        {
            var assembly = typeof(JuNotes.Backend.Database.MigrationSet).Assembly;
            var shipped = JuNotes.Backend.Database.MigrationSet.FromAssembly(assembly);

            await using var empty = await TestDatabase.CreateAsync();
            Assert.Equal(0, await JuNotes.Backend.Database.DatabaseUpgrade.PendingAsync(empty.Data, shipped, CancellationToken.None));

            await using var current = await TestDatabase.CreateCurrentAsync();
            Assert.Equal(0, await JuNotes.Backend.Database.DatabaseUpgrade.PendingAsync(current.Data, shipped, CancellationToken.None));

            await using var old = await TestDatabase.CreateAsync();
            await JuNotes.Backend.Database.DatabaseUpgrade.RunAsync(old.Data, JuNotes.Backend.Database.MigrationSet.FromNames([], _ => ""), JuNotes.Backend.Database.DatabaseUpgrade.ReadBaseline(assembly), CancellationToken.None);
            Assert.Equal(shipped.Latest, await JuNotes.Backend.Database.DatabaseUpgrade.PendingAsync(old.Data, shipped, CancellationToken.None));
        }
    }
}
