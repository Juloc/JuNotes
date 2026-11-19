using Npgsql;

namespace JuNotes.Backend.Service
{
    public sealed class HealthService(NpgsqlDataSource dataSource)
    {
        // Ready means the database answers; live only means the process runs.
        public async Task<bool> IsReadyAsync(CancellationToken ct)
        {
            try
            {
                await using var command = dataSource.CreateCommand("select 1");
                return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) == 1;
            }
            catch (NpgsqlException)
            {
                return false;
            }
        }
    }
}
