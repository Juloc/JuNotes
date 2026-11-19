using System.Diagnostics;
using System.Text.RegularExpressions;

namespace JuNotes.Integrations.Codex
{
    public sealed record CodexStatus(bool Installed, string? Version, bool SignedIn, string? SignIn);

    public sealed record DeviceCode(string Url, string Code);

    // Sign-in of the server's Codex: by the device flow, so the person signs in on a page of their own browser and the server never sees a password.
    public sealed partial class CodexAccount(CodexOptions options)
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

        private sealed record CommandResult(int ExitCode, string Output);

        public async Task<CodexStatus> StatusAsync(CancellationToken ct)
        {
            var version = await RunAsync(["--version"], ct);
            if (version is not { ExitCode: 0 })
            {
                return new CodexStatus(false, null, false, null);
            }

            var number = VersionPattern().Match(version.Output);
            var login = await RunAsync(["login", "status"], ct);
            var signedIn = login is { ExitCode: 0 } && login.Output.Contains("Logged in", StringComparison.OrdinalIgnoreCase);
            var how = !signedIn ? null : login!.Output.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) ? "chatgpt" : login.Output.Contains("API key", StringComparison.OrdinalIgnoreCase) ? "apiKey" : "other";
            return new CodexStatus(true, number.Success ? number.Value : null, signedIn, how);
        }

        public async Task<bool> LogoutAsync(CancellationToken ct) => await RunAsync(["logout"], ct) is { ExitCode: 0 };

        // Starts the device flow and returns the address and the code the person has to enter; null if the CLI showed neither.
        public async Task<DeviceLogin?> StartLoginAsync(CancellationToken ct)
        {
            var start = CodexRunner.Prepare(options);
            start.ArgumentList.Add("login");
            start.ArgumentList.Add("--device-auth");
            Process process;
            try
            {
                process = Process.Start(start) ?? throw new CodexUnavailableException("Codex could not be started.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new CodexUnavailableException("Codex is not installed on the server.");
            }

            process.StandardInput.Close();
            var login = new DeviceLogin(process);
            await login.WaitForCodeAsync(TimeSpan.FromSeconds(20), ct);
            return login.Code is null ? null : login;
        }

        private async Task<CommandResult?> RunAsync(string[] arguments, CancellationToken ct)
        {
            var start = CodexRunner.Prepare(options);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(CommandTimeout);
            try
            {
                using var process = Process.Start(start);
                if (process is null)
                {
                    return null;
                }

                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync(limit.Token);
                var errors = process.StandardError.ReadToEndAsync(limit.Token);
                await process.WaitForExitAsync(limit.Token);
                return new CommandResult(process.ExitCode, StripAnsi(await output + await errors));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        internal static string StripAnsi(string text) => AnsiPattern().Replace(text, "");

        [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex AnsiPattern();

        [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex VersionPattern();
    }

    // A running device login. It ends when the person finished, the code expired or it was cancelled.
    public sealed partial class DeviceLogin : IDisposable
    {
        private readonly Process _process;
        private readonly TaskCompletionSource _found = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _url;
        private string? _code;

        internal DeviceLogin(Process process)
        {
            _process = process;
            _ = Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError)).ContinueWith(_ => _found.TrySetResult(), TaskScheduler.Default);
        }

        public DeviceCode? Code => _url is not null && _code is not null ? new DeviceCode(_url, _code) : null;

        public bool HasEnded => _process.HasExited;

        // True when the CLI ended cleanly, which means the person signed in.
        public async Task<bool> CompletedAsync(CancellationToken ct)
        {
            await _process.WaitForExitAsync(ct);
            return _process.ExitCode == 0;
        }

        internal async Task WaitForCodeAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            try
            {
                while (Code is null && !_found.Task.IsCompleted)
                {
                    await Task.Delay(50, limit.Token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // No code within the time: the caller sees a null code.
            }
        }

        public void Cancel()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }

        public void Dispose()
        {
            Cancel();
            _process.Dispose();
        }

        private async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } raw)
            {
                var line = CodexAccount.StripAnsi(raw);
                _url ??= UrlPattern().Match(line) is { Success: true } url ? url.Value.TrimEnd('.', ',', ')') : null;
                _code ??= CodePattern().Match(line) is { Success: true } code ? code.Value : null;
            }
        }

        [GeneratedRegex(@"https://[^\s]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex UrlPattern();

        [GeneratedRegex(@"\b[A-Z0-9]{4}-[A-Z0-9]{4,6}\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex CodePattern();
    }
}
