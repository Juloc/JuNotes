using JuNotes.Integrations.Codex;

namespace JuNotes.Backend.Tests
{
    public class CodexAccountTests
    {
        private static CodexAccount Account(FakeCodex fake) => new(new CodexOptions { Executable = fake.Executable, Home = fake.Home });

        private const string Routing = """
            case "$1 $2" in
              "--version ") echo "codex-cli 0.166.0"; exit 0;;
              "login status") %STATUS%;;
              "login --device-auth") %LOGIN%;;
              "logout ") exit 0;;
            esac
            exit 0
            """;

        private static string Script(string status, string login = "exit 1") => Routing.Replace("%STATUS%", status, StringComparison.Ordinal).Replace("%LOGIN%", login, StringComparison.Ordinal);

        [Fact]
        public async Task A_signed_in_account_with_chatgpt_is_recognised()
        {
            using var fake = new FakeCodex(Script("echo 'Logged in using ChatGPT'; exit 0"));

            var status = await Account(fake).StatusAsync(CancellationToken.None);

            Assert.Equal(new CodexStatus(true, "0.166.0", true, "chatgpt"), status);
        }

        [Fact]
        public async Task An_api_key_sign_in_is_recognised()
        {
            using var fake = new FakeCodex(Script("echo 'Logged in using an API key - sk-***'; exit 0"));

            Assert.Equal("apiKey", (await Account(fake).StatusAsync(CancellationToken.None)).SignIn);
        }

        [Fact]
        public async Task Without_a_sign_in_it_says_so()
        {
            using var fake = new FakeCodex(Script("echo 'Not logged in'; exit 1"));

            var status = await Account(fake).StatusAsync(CancellationToken.None);

            Assert.True(status.Installed);
            Assert.False(status.SignedIn);
            Assert.Null(status.SignIn);
        }

        [Fact]
        public async Task A_missing_program_is_reported_as_not_installed()
        {
            var account = new CodexAccount(new CodexOptions { Executable = "/nonexistent/codex", Home = Path.Combine(Path.GetTempPath(), "junotes-nohome-" + Guid.NewGuid().ToString("N")) });

            Assert.Equal(new CodexStatus(false, null, false, null), await account.StatusAsync(CancellationToken.None));
        }

        [Fact]
        public async Task The_device_login_shows_the_address_and_the_code_even_in_colour()
        {
            using var fake = new FakeCodex(Script("exit 1", "printf '\\033[94mhttps://auth.example.com/codex/device\\033[0m\\n'; printf 'Enter code: \\033[1mABCD-12345\\033[0m\\n'; sleep 30"));
            using var login = await Account(fake).StartLoginAsync(CancellationToken.None) ?? throw new InvalidOperationException("no login");

            Assert.Equal(new DeviceCode("https://auth.example.com/codex/device", "ABCD-12345"), login.Code);
            Assert.False(login.HasEnded);
            login.Cancel();
            Assert.False(await login.CompletedAsync(CancellationToken.None));
        }

        [Fact]
        public async Task A_login_that_shows_no_code_gives_null()
        {
            using var fake = new FakeCodex(Script("exit 1", "echo 'something went wrong'; exit 1"));

            Assert.Null(await Account(fake).StartLoginAsync(CancellationToken.None));
        }

        [Fact]
        public async Task A_finished_login_completes_successfully()
        {
            using var fake = new FakeCodex(Script("exit 1", "echo 'https://auth.example.com/d'; echo 'ABCD-12345'; sleep 1; exit 0"));
            using var login = await Account(fake).StartLoginAsync(CancellationToken.None) ?? throw new InvalidOperationException("no login");

            Assert.True(await login.CompletedAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Logout_succeeds_through_the_cli()
        {
            using var fake = new FakeCodex(Script("exit 0"));

            Assert.True(await Account(fake).LogoutAsync(CancellationToken.None));
        }
    }
}
