using JuNotes.Backend.Service;

namespace JuNotes.Backend.Tests
{
    public class SyncAccessTests
    {
        [Fact]
        public void The_right_token_is_accepted() => Assert.True(new SyncAccess("secret").IsAuthorized("Bearer secret"));

        [Theory]
        [InlineData("Bearer other")]
        [InlineData("Bearer ")]
        [InlineData("secret")]
        [InlineData("bearer secret")]
        [InlineData(null)]
        public void Anything_else_is_refused(string? header) => Assert.False(new SyncAccess("secret").IsAuthorized(header));

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
    }
}
