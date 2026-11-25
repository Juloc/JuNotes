using Base.Accounts;
using Npgsql;

namespace JuNotes.Backend.Tests
{
    // Codes of the second factor for the test people (they share one key made up for the tests). A code works once, so before each code is handed out the step that the server
    // has recorded as spent for that person is reset: the code is always the one of the current time step, which the server accepts at any moment (its window reaches one step
    // back), so a step boundary in the middle of a test changes nothing and a test may take as many codes as it needs.
    internal sealed class TestSecondFactor(NpgsqlDataSource data)
    {
        public string Next(Guid person)
        {
            using (var reset = data.CreateCommand($"update users set totp_last_step = 0 where id = '{person}'"))
            {
                reset.ExecuteNonQuery();
            }

            return Totp.Hotp(Base32.Decode("TESTSECRET"), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Totp.StepSeconds);
        }
    }
}
