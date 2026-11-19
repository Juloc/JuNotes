using JuNotes.Backend.Database;
using Npgsql;

namespace JuNotes.Backend.Tests
{
    public class DatabaseUpgradeTests
    {
        private static async Task<(NpgsqlDataSource Data, Func<Task> Drop)> FreshAsync()
        {
            var admin = Environment.GetEnvironmentVariable(RequiresDatabaseFactAttribute.Variable)!;
            var name = "junotes_test_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync();
                await using var create = new NpgsqlCommand($"create database {name}", connection);
                await create.ExecuteNonQueryAsync();
            }

            var data = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false }.ConnectionString);
            return (data, async () =>
            {
                await data.DisposeAsync();
                await using var connection = new NpgsqlConnection(admin);
                await connection.OpenAsync();
                await using var drop = new NpgsqlCommand($"drop database {name} with (force)", connection);
                await drop.ExecuteNonQueryAsync();
            });
        }

        private static MigrationSet Set(params (string File, string Sql)[] files) => MigrationSet.FromNames(files.Select(item => (item.File, item.File)), name => files.Single(item => item.File == name).Sql);

        private static async Task<long> CountAsync(NpgsqlDataSource data, string table)
        {
            await using var command = data.CreateCommand($"select count(*) from {table}");
            return (long)(await command.ExecuteScalarAsync())!;
        }

        [RequiresDatabaseFact]
        public async Task An_empty_database_gets_the_baseline_and_all_migrations()
        {
            var (data, drop) = await FreshAsync();
            try
            {
                var set = Set(("0001_extra.sql", "create table extra (id int)"));
                var latest = await DatabaseUpgrade.RunAsync(data, set, DatabaseUpgrade.ReadBaseline(typeof(MigrationSet).Assembly), CancellationToken.None);

                Assert.Equal(1, latest);
                Assert.Equal(0, await CountAsync(data, "instance_settings"));
                Assert.Equal(0, await CountAsync(data, "extra"));
                Assert.Equal(2, await CountAsync(data, "schema_migrations"));
            }
            finally
            {
                await drop();
            }
        }

        [RequiresDatabaseFact]
        public async Task A_second_run_applies_only_new_migrations_and_keeps_data()
        {
            var (data, drop) = await FreshAsync();
            try
            {
                var baseline = DatabaseUpgrade.ReadBaseline(typeof(MigrationSet).Assembly);
                await DatabaseUpgrade.RunAsync(data, Set(), baseline, CancellationToken.None);
                await using (var insert = data.CreateCommand("insert into instance_settings (key, value) values ('a', 'b')"))
                {
                    await insert.ExecuteNonQueryAsync();
                }

                await DatabaseUpgrade.RunAsync(data, Set(("0001_extra.sql", "create table extra (id int)")), baseline, CancellationToken.None);

                Assert.Equal(1, await CountAsync(data, "instance_settings"));
                Assert.Equal(2, await CountAsync(data, "schema_migrations"));
            }
            finally
            {
                await drop();
            }
        }

        [RequiresDatabaseFact]
        public async Task A_failing_migration_changes_nothing()
        {
            var (data, drop) = await FreshAsync();
            try
            {
                var baseline = DatabaseUpgrade.ReadBaseline(typeof(MigrationSet).Assembly);
                await DatabaseUpgrade.RunAsync(data, Set(), baseline, CancellationToken.None);

                await Assert.ThrowsAsync<PostgresException>(() => DatabaseUpgrade.RunAsync(data, Set(("0001_ok.sql", "create table ok (id int)"), ("0002_bad.sql", "select * from missing")), baseline, CancellationToken.None));

                await Assert.ThrowsAsync<PostgresException>(() => CountAsync(data, "ok"));
                Assert.Equal(1, await CountAsync(data, "schema_migrations"));
            }
            finally
            {
                await drop();
            }
        }

        [RequiresDatabaseFact]
        public async Task A_database_from_a_newer_version_is_refused()
        {
            var (data, drop) = await FreshAsync();
            try
            {
                var baseline = DatabaseUpgrade.ReadBaseline(typeof(MigrationSet).Assembly);
                await DatabaseUpgrade.RunAsync(data, Set(("0001_a.sql", "select 1")), baseline, CancellationToken.None);

                await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseUpgrade.RunAsync(data, Set(), baseline, CancellationToken.None));
            }
            finally
            {
                await drop();
            }
        }
    }
}
