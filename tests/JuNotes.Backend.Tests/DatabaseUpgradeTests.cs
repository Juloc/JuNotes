using JuNotes.Backend.Database;
using Npgsql;

namespace JuNotes.Backend.Tests
{
    public class DatabaseUpgradeTests
    {
        private static MigrationSet Set(params (string File, string Sql)[] files) => MigrationSet.FromNames(files.Select(item => (item.File, item.File)), name => files.Single(item => item.File == name).Sql);

        [RequiresDatabaseFact]
        public async Task An_empty_database_gets_the_baseline_and_all_migrations()
        {
            await using var database = await TestDatabase.CreateAsync();
            var data = database.Data;
            {
                var set = Set(("0001_extra.sql", "create table extra (id int)"));
                var latest = await DatabaseUpgrade.RunAsync(data, set, DatabaseUpgrade.ReadBaseline(typeof(MigrationSet).Assembly), CancellationToken.None);

                Assert.Equal(1, latest);
                Assert.Equal(0, await database.CountAsync("app_instance_settings"));
                Assert.Equal(0, await database.CountAsync("extra"));
                Assert.Equal(2, await database.CountAsync("schema_migrations"));
            }
        }

        [RequiresDatabaseFact]
        public async Task A_second_run_applies_only_new_migrations_and_keeps_data()
        {
            await using var database = await TestDatabase.CreateAsync();
            var data = database.Data;
            {
                var baseline = DatabaseUpgrade.ReadBaseline(typeof(MigrationSet).Assembly);
                await DatabaseUpgrade.RunAsync(data, Set(), baseline, CancellationToken.None);
                await using (var insert = data.CreateCommand("insert into app_instance_settings (key, value) values ('a', 'b')"))
                {
                    await insert.ExecuteNonQueryAsync();
                }

                await DatabaseUpgrade.RunAsync(data, Set(("0001_extra.sql", "create table extra (id int)")), baseline, CancellationToken.None);

                Assert.Equal(1, await database.CountAsync("app_instance_settings"));
                Assert.Equal(2, await database.CountAsync("schema_migrations"));
            }
        }

        [RequiresDatabaseFact]
        public async Task A_failing_migration_changes_nothing()
        {
            await using var database = await TestDatabase.CreateAsync();
            var data = database.Data;
            {
                var baseline = DatabaseUpgrade.ReadBaseline(typeof(MigrationSet).Assembly);
                await DatabaseUpgrade.RunAsync(data, Set(), baseline, CancellationToken.None);

                await Assert.ThrowsAsync<PostgresException>(() => DatabaseUpgrade.RunAsync(data, Set(("0001_ok.sql", "create table ok (id int)"), ("0002_bad.sql", "select * from missing")), baseline, CancellationToken.None));

                await Assert.ThrowsAsync<PostgresException>(() => database.CountAsync("ok"));
                Assert.Equal(1, await database.CountAsync("schema_migrations"));
            }
        }

        [RequiresDatabaseFact]
        public async Task A_database_from_a_newer_version_is_refused()
        {
            await using var database = await TestDatabase.CreateAsync();
            var data = database.Data;
            {
                var baseline = DatabaseUpgrade.ReadBaseline(typeof(MigrationSet).Assembly);
                await DatabaseUpgrade.RunAsync(data, Set(("0001_a.sql", "select 1")), baseline, CancellationToken.None);

                await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseUpgrade.RunAsync(data, Set(), baseline, CancellationToken.None));
            }
        }

        [RequiresDatabaseFact]
        public async Task Looking_at_what_is_pending_changes_nothing_and_the_rename_comes_with_the_upgrade()
        {
            await using var database = await TestDatabase.CreateAsync();
            var data = database.Data;
            var assembly = typeof(MigrationSet).Assembly;
            // A database as an older server left it: the settings table under the name the base also uses.
            await DatabaseUpgrade.RunAsync(data, Set(), DatabaseUpgrade.ReadBaseline(assembly), CancellationToken.None);
            await using (var back = data.CreateCommand("alter table app_instance_settings rename to instance_settings"))
            {
                await back.ExecuteNonQueryAsync();
            }

            // The check that runs before the backup is read only.
            _ = await DatabaseUpgrade.PendingAsync(data, Set(("0001_extra.sql", "create table extra (id int)")), CancellationToken.None);
            await using (var still = data.CreateCommand("select to_regclass('instance_settings') is not null and to_regclass('app_instance_settings') is null"))
            {
                Assert.True((bool)(await still.ExecuteScalarAsync())!);
            }

            // The rename is its own step, taken after the backup.
            await DatabaseUpgrade.RenameLegacyTableAsync(data, CancellationToken.None);
            await using (var renamed = data.CreateCommand("select to_regclass('instance_settings') is null and to_regclass('app_instance_settings') is not null"))
            {
                Assert.True((bool)(await renamed.ExecuteScalarAsync())!);
            }
        }
    }
}
