using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JuNotes.Integrations.Codex
{
    public sealed class CodexOptions
    {
        public string Executable { get; set; } = "codex";

        // Holds the sign-in (CODEX_HOME). On the server it lives on the data volume.
        public string Home { get; set; } = "codex";

        // Empty means the account's own default model, which is safer than guessing a name.
        public string? Model { get; set; }

        public int TimeoutSeconds { get; set; } = 240;

        public int MaxParallel { get; set; } = 2;
    }

    // One question for the model: what to do, the material, and the shape the answer must have.
    // Model and Effort override the program's defaults for this one question; empty means the account's own default.
    public sealed record CodexRequest(string Instruction, string Input, string? SchemaJson = null, IReadOnlyList<string>? ImagePaths = null, string? Model = null, string? Effort = null);

    public sealed record CodexResult(string Text, JsonElement? Json);

    // The CLI is missing or nobody has signed in.
    public sealed class CodexUnavailableException(string message) : Exception(message);

    // The model did not give a usable answer: it failed, took too long or did not follow the shape.
    public sealed class CodexFailedException(string message) : Exception(message);

    public interface ICodexRunner
    {
        Task<CodexResult> RunAsync(CodexRequest request, CancellationToken ct);
    }

    // The official Codex CLI, called as Jularr and FullWorth call it: one prompt in, one answer out, no tools and no files. The sandbox is read-only, the working
    // folder is empty, nothing is stored, and the user's own config is ignored. The sign-in is the one under CODEX_HOME; nothing here reads or returns it.
    public sealed partial class CodexRunner(CodexOptions options) : ICodexRunner, IDisposable
    {
        private static readonly HashSet<string> EffortLevels = new(["minimal", "low", "medium", "high", "xhigh"], StringComparer.Ordinal);

        private readonly SemaphoreSlim _slots = new(Math.Max(1, options.MaxParallel));

        public void Dispose() => _slots.Dispose();

        internal static string WorkFolder(CodexOptions options) => Path.Combine(options.Home, "work");

        public async Task<CodexResult> RunAsync(CodexRequest request, CancellationToken ct)
        {
            await _slots.WaitAsync(ct);
            try
            {
                return await RunOneAsync(request, ct);
            }
            finally
            {
                _slots.Release();
            }
        }

        private async Task<CodexResult> RunOneAsync(CodexRequest request, CancellationToken ct)
        {
            var work = WorkFolder(options);
            Directory.CreateDirectory(work);
            var id = Guid.NewGuid().ToString("N");
            var answerFile = Path.Combine(work, id + ".answer");
            var schemaFile = Path.Combine(work, id + ".schema.json");

            var start = Prepare(options);
            foreach (var argument in new[] { "exec", "--skip-git-repo-check", "--ephemeral", "--ignore-user-config", "--ignore-rules", "-s", "read-only", "--color", "never", "-C", work })
            {
                start.ArgumentList.Add(argument);
            }

            var model = string.IsNullOrWhiteSpace(request.Model) ? options.Model : request.Model.Trim();
            if (!string.IsNullOrWhiteSpace(model))
            {
                start.ArgumentList.Add("-m");
                start.ArgumentList.Add(model);
            }

            // How hard the model thinks, only from the known list, as a setting of the program ("-c key=value", the value as TOML text).
            if (request.Effort?.Trim() is { Length: > 0 } effort && EffortLevels.Contains(effort))
            {
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add($"model_reasoning_effort=\"{effort}\"");
            }

            if (request.SchemaJson is not null)
            {
                await File.WriteAllTextAsync(schemaFile, request.SchemaJson, ct);
                start.ArgumentList.Add("--output-schema");
                start.ArgumentList.Add(schemaFile);
            }

            foreach (var image in request.ImagePaths ?? [])
            {
                start.ArgumentList.Add("-i");
                start.ArgumentList.Add(image);
            }

            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(answerFile);
            start.ArgumentList.Add("-");

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));
            try
            {
                using var process = StartProcess(start);
                try
                {
                    await process.StandardInput.WriteAsync((request.Instruction + "\n\n" + request.Input).AsMemory(), limit.Token);
                    process.StandardInput.Close();
                    // Read away both streams: the CLI's diagnostics may name the account and are dropped here.
                    var output = process.StandardOutput.ReadToEndAsync(limit.Token);
                    var errors = process.StandardError.ReadToEndAsync(limit.Token);
                    await process.WaitForExitAsync(limit.Token);
                    await output;
                    await errors;

                    if (process.ExitCode != 0 || !File.Exists(answerFile))
                    {
                        throw new CodexFailedException("Codex did not answer.");
                    }
                }
                catch
                {
                    Kill(process);
                    throw;
                }

                var text = (await File.ReadAllTextAsync(answerFile, limit.Token)).Trim();
                if (text.Length == 0)
                {
                    throw new CodexFailedException("Codex answered with nothing.");
                }

                return request.SchemaJson is null ? new CodexResult(text, null) : new CodexResult(text, ParseJson(text));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new CodexFailedException("Codex took too long.");
            }
            finally
            {
                TryDelete(answerFile);
                TryDelete(schemaFile);
            }
        }

        // The model sometimes wraps the JSON in a code fence or adds a sentence; the first complete JSON value is what counts.
        internal static JsonElement ParseJson(string text)
        {
            var trimmed = text.Trim();
            var fence = CodeFence().Match(trimmed);
            if (fence.Success)
            {
                trimmed = fence.Groups["body"].Value.Trim();
            }

            try
            {
                return JsonElement.Parse(trimmed);
            }
            catch (JsonException)
            {
                var open = trimmed.IndexOfAny(['{', '[']);
                var close = trimmed.LastIndexOfAny(['}', ']']);
                if (open >= 0 && close > open)
                {
                    try
                    {
                        return JsonElement.Parse(trimmed[open..(close + 1)]);
                    }
                    catch (JsonException)
                    {
                        // Falls through to the failure below.
                    }
                }

                throw new CodexFailedException("Codex did not answer in the requested format.");
            }
        }

        internal static ProcessStartInfo Prepare(CodexOptions options)
        {
            var start = new ProcessStartInfo(options.Executable)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
            };
            Directory.CreateDirectory(options.Home);
            start.Environment["CODEX_HOME"] = Path.GetFullPath(options.Home);
            return start;
        }

        private static Process StartProcess(ProcessStartInfo start)
        {
            try
            {
                return Process.Start(start) ?? throw new CodexUnavailableException("Codex could not be started.");
            }
            catch (Win32Exception)
            {
                throw new CodexUnavailableException("Codex is not installed on the server.");
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception)
            {
                // Already gone.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover file in the work folder is harmless.
            }
        }

        [GeneratedRegex(@"^```[a-zA-Z]*\s*(?<body>.*?)\s*```$", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
        private static partial Regex CodeFence();
    }
}
