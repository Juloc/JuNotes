using JuNotes.Backend.Infrastructure.Sql;
using Npgsql;

namespace JuNotes.Backend.Tests
{
    public class SqlDatabaseTests
    {
        private const string Connection = "Host=localhost;Database=x;Username=u;Password=p";

        [Fact]
        public async Task The_web_lane_turns_jit_off_and_names_itself()
        {
            await using var database = SqlDatabase.Create(Connection, SqlLane.Web, 7, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
            var options = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString);

            Assert.Equal("junotes-web", options.ApplicationName);
            Assert.Equal(7, options.MaxPoolSize);
            Assert.Contains("-c jit=off", options.Options);
            Assert.Contains("-c statement_timeout=5000", options.Options);
            Assert.Contains("-c idle_in_transaction_session_timeout=10000", options.Options);
        }

        [Fact]
        public async Task The_worker_lane_keeps_the_servers_jit_setting()
        {
            await using var database = SqlDatabase.Create(Connection, SqlLane.Worker, 3, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
            var options = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString);

            Assert.Equal("junotes-worker", options.ApplicationName);
            Assert.DoesNotContain("jit", options.Options);
        }

        [Fact]
        public void The_client_gives_up_after_the_servers_limit() => Assert.Equal(10, SqlDatabase.CommandTimeout(TimeSpan.FromSeconds(5)));
    }
}
