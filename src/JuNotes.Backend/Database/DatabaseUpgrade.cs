using System.Reflection;
using Npgsql;

namespace JuNotes.Backend.Database
{
    // Brings an empty database to the baseline and every database to the newest migration, all pending migrations in one transaction.
    public static class DatabaseUpgrade
    {
        // One table of the baseline of an older server, instance_settings (unused, key and text value), has the name of a table of the base with other columns
        // (key and json value). Ours is renamed once, before the base starts: only the name changes, every row stays, and renaming it back is the way back.
        public static async Task RenameLegacyTableAsync(NpgsqlDataSource dataSource, CancellationToken ct)
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await Execute(connection, transaction, "select pg_advisory_xact_lock(7481236)", ct);
            var settings = await Scalar(connection, transaction,
                "select count(*) from information_schema.columns where table_schema = current_schema() and table_name = 'instance_settings' and column_name = 'value' and data_type = 'text'"
                + " and to_regclass('app_instance_settings') is null", ct);
            if (settings > 0)
            {
                await Execute(connection, transaction, "alter table instance_settings rename to app_instance_settings", ct);
                await Execute(connection, transaction, "alter index if exists instance_settings_pkey rename to app_instance_settings_pkey", ct);
            }

            await transaction.CommitAsync(ct);
        }

        // How many migrations the database still lacks; 0 for a database that is new (nothing worth saving yet) or current.
        public static async Task<int> PendingAsync(NpgsqlDataSource dataSource, MigrationSet migrations, CancellationToken ct)
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var exists = new NpgsqlCommand("select to_regclass('schema_migrations') is not null", connection);
            if (!(bool)(await exists.ExecuteScalarAsync(ct))!)
            {
                return 0;
            }

            await using var current = new NpgsqlCommand("select coalesce(max(number), 0) from schema_migrations", connection);
            return Math.Max(0, migrations.Latest - Convert.ToInt32(await current.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture));
        }

        public static async Task<int> RunAsync(NpgsqlDataSource dataSource, MigrationSet migrations, string baseline, CancellationToken ct)
        {
            await RenameLegacyTableAsync(dataSource, ct);
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);

            // One upgrade at a time, even with two hosts starting together.
            await Execute(connection, transaction, "select pg_advisory_xact_lock(7481236)", ct);
            await Execute(connection, transaction, "create table if not exists schema_migrations (number integer primary key, name text not null, applied_at timestamptz not null default now())", ct);

            var current = await Scalar(connection, transaction, "select coalesce(max(number), -1) from schema_migrations", ct);
            if (current < 0)
            {
                await Execute(connection, transaction, baseline, ct);
                await Execute(connection, transaction, "insert into schema_migrations (number, name) values (0, 'baseline')", ct);
                current = 0;
            }

            if (current > migrations.Latest)
            {
                throw new InvalidOperationException($"The database is at migration {current}, newer than this version ({migrations.Latest}). Start the newer version.");
            }

            foreach (var migration in migrations.Migrations.Where(item => item.Number > current))
            {
                await Execute(connection, transaction, migration.Sql(), ct);
                await using var record = new NpgsqlCommand("insert into schema_migrations (number, name) values ($1, $2)", connection, transaction)
                {
                    Parameters = { new() { Value = migration.Number }, new() { Value = migration.Name } },
                };
                await record.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return migrations.Latest;
        }

        public static string ReadBaseline(Assembly assembly)
        {
            using var stream = assembly.GetManifestResourceStream("JuNotes.Database.Schema.sql") ?? throw new InvalidOperationException("Missing embedded Schema.sql.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(ct);
        }

        private static async Task<int> Scalar(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
