using JuNotes.Backend.Service;

namespace JuNotes.Backend.Tests
{
    public class SyncAccessTests
    {
        private const string Long = "a-token-of-more-than-thirty-two-characters";

        [Fact]
        public void The_right_token_is_accepted() => Assert.True(new SyncAccess(Long).IsAuthorized("Bearer " + Long));

        [Theory]
        [InlineData("Bearer other")]
        [InlineData("Bearer ")]
        [InlineData(Long)]
        [InlineData("bearer " + Long)]
        [InlineData(null)]
        public void Anything_else_is_refused(string? header) => Assert.False(new SyncAccess(Long).IsAuthorized(header));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Without_a_configured_token_nobody_gets_in(string? token)
        {
            var access = new SyncAccess(token);

            Assert.False(access.IsConfigured);
            Assert.False(access.IsAuthorized("Bearer "));
            Assert.False(access.IsAuthorized("Bearer anything"));
        }

        [Theory]
        [InlineData("x")]
        [InlineData("secret")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void A_token_shorter_than_32_characters_stops_the_start_with_a_clear_message(string token)
        {
            var error = Assert.Throws<InvalidOperationException>(() => new SyncAccess(token));

            Assert.Contains("Sync__Token", error.Message, StringComparison.Ordinal);
            Assert.Contains("32", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(token + " ", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_token_of_exactly_32_characters_is_accepted() => Assert.True(new SyncAccess(new string('a', 32)).IsConfigured);
    }
}
