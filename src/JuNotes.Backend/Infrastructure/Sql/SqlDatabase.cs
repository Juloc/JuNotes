using System.Globalization;
using Npgsql;

namespace JuNotes.Backend.Infrastructure.Sql
{
    // One lane of PostgreSQL connections: its own application name, pool and server-side limits.
    public sealed class SqlDatabase(NpgsqlDataSource dataSource) : IAsyncDisposable
    {
        // Npgsql gives up a little after the server's limit, so a statement past it ends with the server's own cancel.
        internal static readonly TimeSpan CommandBuffer = TimeSpan.FromSeconds(5);

        public NpgsqlDataSource DataSource { get; } = dataSource;

        public static SqlDatabase Create(string connectionString, SqlLane lane, int poolSize, TimeSpan idleInTransaction, TimeSpan statementTimeout)
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            var options = builder.ConnectionStringBuilder;
            options.ApplicationName = Name(lane);
            options.MaxPoolSize = poolSize;
            options.CommandTimeout = CommandTimeout(statementTimeout);
            // Short reads on small tables are priced above the JIT threshold and then lose time compiling, so only the web lane turns JIT off.
            options.Options = (options.Options + " -c idle_in_transaction_session_timeout=" + Milliseconds(idleInTransaction)
                + " -c statement_timeout=" + Milliseconds(statementTimeout) + (lane == SqlLane.Web ? " -c jit=off" : "")).Trim();
            return new SqlDatabase(builder.Build());
        }

        internal static string Name(SqlLane lane) => lane == SqlLane.Web ? "junotes-web" : "junotes-worker";

        internal static string Milliseconds(TimeSpan value) => ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        // Whole seconds above the server's limit.
        internal static int CommandTimeout(TimeSpan statementTimeout) => (int)Math.Ceiling((statementTimeout + CommandBuffer).TotalSeconds);

        public ValueTask DisposeAsync() => DataSource.DisposeAsync();
    }
}
