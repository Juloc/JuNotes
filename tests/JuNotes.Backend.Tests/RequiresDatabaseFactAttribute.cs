namespace JuNotes.Backend.Tests
{
    // Integration tests run only against the isolated test server named by JUNOTES_TEST_DB, never against a live or dev database.
    public sealed class RequiresDatabaseFactAttribute : FactAttribute
    {
        public const string Variable = "JUNOTES_TEST_DB";

        public RequiresDatabaseFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
            {
                Skip = $"{Variable} must point to the isolated PostgreSQL test server.";
            }
        }
    }
}
