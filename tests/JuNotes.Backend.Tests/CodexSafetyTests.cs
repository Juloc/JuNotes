using JuNotes.Frontend.Admin;
using JuNotes.Integrations.Codex;

namespace JuNotes.Backend.Tests
{
    // The program Codex gets almost nothing of what the server knows, and no program it was asked to start is left running. These use stand-ins (shell scripts) and /proc, so they
    // run on Linux only.
    public sealed class CodexSafetyTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "junotes-codexsafety-" + Guid.NewGuid().ToString("N"));

        public CodexSafetyTests() => Directory.CreateDirectory(_folder);

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private static bool Alive(int pid) => Directory.Exists("/proc/" + pid);

        private static async Task<bool> GoneAsync(int pid)
        {
            for (var wait = 0; wait < 60; wait++)
            {
                if (!Alive(pid))
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return false;
        }

        [Fact]
        public void The_environment_of_codex_is_only_where_to_find_programs_and_its_own_home()
        {
            Environment.SetEnvironmentVariable("JUNOTES_TEST_CANARY", "secret-" + Guid.NewGuid().ToString("N"));
            try
            {
                var start = CodexRunner.Prepare(new CodexOptions { Executable = "codex", Home = Path.Combine(_folder, "home") });

                var keys = start.Environment.Keys.Order().ToList();
                Assert.Subset(new HashSet<string> { "PATH", "HOME", "CODEX_HOME" }, keys.ToHashSet());
                Assert.Contains("CODEX_HOME", keys);
                Assert.Contains("HOME", keys);
                Assert.Equal(start.Environment["CODEX_HOME"], start.Environment["HOME"]);
                Assert.DoesNotContain("JUNOTES_TEST_CANARY", keys);
            }
            finally
            {
                Environment.SetEnvironmentVariable("JUNOTES_TEST_CANARY", null);
            }
        }

        [Fact]
        public async Task The_running_program_really_does_not_see_the_environment_of_the_server()
        {
            if (!OperatingSystem.IsLinux())
            {
                return;
            }

            var canary = "secret-" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable("JUNOTES_TEST_CANARY", canary);
            try
            {
                var dump = Path.Combine(_folder, "env.txt");
                using var fake = new FakeCodex($"case \"$1\" in \"--version\") env > '{dump}'; echo 'codex-cli 0.1.0'; exit 0;; esac\nexit 0");

                _ = await new CodexAccount(new CodexOptions { Executable = fake.Executable, Home = fake.Home }).StatusAsync(CancellationToken.None);

                var text = await File.ReadAllTextAsync(dump);
                Assert.DoesNotContain(canary, text, StringComparison.Ordinal);
                Assert.DoesNotContain("JUNOTES_TEST_CANARY", text, StringComparison.Ordinal);
                Assert.Contains("CODEX_HOME=", text, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable("JUNOTES_TEST_CANARY", null);
            }
        }

        [Fact]
        public async Task A_login_that_shows_no_code_in_time_is_ended()
        {
            if (!OperatingSystem.IsLinux())
            {
                return;
            }

            var pids = Path.Combine(_folder, "pid.txt");
            using var fake = new FakeCodex($"case \"$1 $2\" in \"--version \") echo 'codex-cli 0.1.0'; exit 0;; \"login --device-auth\") echo $$ > '{pids}'; sleep 30;; esac\nexit 0");

            var login = await new CodexAccount(new CodexOptions { Executable = fake.Executable, Home = fake.Home }) { CodeTimeout = TimeSpan.FromMilliseconds(400) }.StartLoginAsync(CancellationToken.None);

            Assert.Null(login);
            Assert.True(await GoneAsync(int.Parse((await File.ReadAllTextAsync(pids)).Trim(), System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Fact]
        public async Task Two_logins_at_once_leave_one_program_running()
        {
            if (!OperatingSystem.IsLinux())
            {
                return;
            }

            var pids = Path.Combine(_folder, "pids.txt");
            using var fake = new FakeCodex($"case \"$1 $2\" in \"--version \") echo 'codex-cli 0.1.0'; exit 0;; \"login --device-auth\") echo $$ >> '{pids}'; echo 'https://auth.example.com/codex/device'; echo 'WXYZ-98765'; sleep 30;; esac\nexit 0");
            using var session = new CodexLoginSession(TimeProvider.System);
            var account = new CodexAccount(new CodexOptions { Executable = fake.Executable, Home = fake.Home });

            var both = await Task.WhenAll(session.StartAsync(account, CancellationToken.None), session.StartAsync(account, CancellationToken.None));

            Assert.All(both, code => Assert.Equal("WXYZ-98765", code!.Code));
            var started = (await File.ReadAllLinesAsync(pids)).Select(line => int.Parse(line.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(2, started.Count);
            await Task.Delay(300);
            Assert.Equal(1, started.Count(Alive));

            // Cancelling ends the one that is left.
            session.Cancel();
            foreach (var pid in started)
            {
                Assert.True(await GoneAsync(pid));
            }
        }

        [Fact]
        public async Task A_command_that_runs_past_its_time_is_ended()
        {
            if (!OperatingSystem.IsLinux())
            {
                return;
            }

            var pids = Path.Combine(_folder, "slow.txt");
            using var fake = new FakeCodex($"case \"$1 $2\" in \"--version \") echo 'codex-cli 0.1.0'; exit 0;; \"login status\") echo $$ > '{pids}'; sleep 30;; esac\nexit 0");

            var status = await new CodexAccount(new CodexOptions { Executable = fake.Executable, Home = fake.Home }) { CommandTimeout = TimeSpan.FromMilliseconds(400) }.StatusAsync(CancellationToken.None);

            Assert.False(status.SignedIn);
            Assert.True(await GoneAsync(int.Parse((await File.ReadAllTextAsync(pids)).Trim(), System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
