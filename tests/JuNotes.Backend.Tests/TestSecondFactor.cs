using Base.Accounts;

namespace JuNotes.Backend.Tests
{
    // Codes of the second factor for the test people (they share one key made up for the tests). A code works once, so each call gives the oldest step that the server still accepts
    // and that is newer than the last one given for that person; a step boundary in the middle of a test changes nothing. A person has three codes at most in one test.
    internal sealed class TestSecondFactor
    {
        private readonly Dictionary<Guid, long> _last = [];

        public string Next(Guid person)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Totp.StepSeconds;
            var step = Math.Max(_last.GetValueOrDefault(person) + 1, now - 1);
            Assert.True(step <= now + 1, "a person has three fresh codes of the second factor in a test");
            _last[person] = step;
            return Totp.Hotp(Base32.Decode("TESTSECRET"), step);
        }
    }
}
