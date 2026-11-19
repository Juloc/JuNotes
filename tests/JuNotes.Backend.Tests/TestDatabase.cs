using JuNotes.Backend.Database;
using Npgsql;

namespace JuNotes.Backend.Tests
{
    // A throwaway database on the isolated test server, dropped when disposed.
    internal sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _admin;
        private readonly string _name;

        private TestDatabase(string admin, string name, string connectionString, NpgsqlDataSource data)
        {
            _admin = admin;
            _name = name;
            ConnectionString = connectionString;
            Data = data;
        }

        // With the password, which a data source's own ConnectionString leaves out.
        public string ConnectionString { get; }

        public NpgsqlDataSource Data { get; }

        // Empty, no schema.
        public static async Task<TestDatabase> CreateAsync()
        {
            var admin = Environment.GetEnvironmentVariable(RequiresDatabaseFactAttribute.Variable)!;
            var name = "junotes_test_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync();
                await using var create = new NpgsqlCommand($"create database {name}", connection);
                await create.ExecuteNonQueryAsync();
            }

            var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false }.ConnectionString;
            return new TestDatabase(admin, name, connectionString, NpgsqlDataSource.Create(connectionString));
        }

        // With the baseline and every shipped migration, as the host would leave it.
        public static async Task<TestDatabase> CreateCurrentAsync()
        {
            var database = await CreateAsync();
            var assembly = typeof(MigrationSet).Assembly;
            await DatabaseUpgrade.RunAsync(database.Data, MigrationSet.FromAssembly(assembly), DatabaseUpgrade.ReadBaseline(assembly), CancellationToken.None);
            return database;
        }

        public async Task<long> CountAsync(string table)
        {
            await using var command = Data.CreateCommand($"select count(*) from {table}");
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Data.DisposeAsync();
            await using var connection = new NpgsqlConnection(_admin);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"drop database {_name} with (force)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
