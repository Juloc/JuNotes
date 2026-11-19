using System.Diagnostics;
using JuNotes.Integrations.Codex;

namespace JuNotes.Backend.Tests
{
    public class CodexRunnerTests
    {
        private const string Schema = """{"type":"object","properties":{"answer":{"type":"integer"}},"required":["answer"]}""";

        private static CodexRunner Runner(FakeCodex fake, Action<CodexOptions>? configure = null)
        {
            var options = new CodexOptions { Executable = fake.Executable, Home = fake.Home, TimeoutSeconds = 20 };
            configure?.Invoke(options);
            return new CodexRunner(options);
        }

        private const string Answer42 = """
            cat > /dev/null
            printf '{"answer":42}' > "$out"
            """;

        [Fact]
        public async Task An_answer_in_the_requested_shape_comes_back_as_json()
        {
            using var fake = new FakeCodex(Answer42);
            using var runner = Runner(fake);

            var result = await runner.RunAsync(new CodexRequest("Rechne.", "6 mal 7", Schema), CancellationToken.None);

            Assert.Equal(42, result.Json!.Value.GetProperty("answer").GetInt32());
        }

        [Fact]
        public async Task It_is_called_with_no_tools_no_storage_and_the_sign_in_folder()
        {
            using var fake = new FakeCodex(Answer42);
            using var runner = Runner(fake);

            await runner.RunAsync(new CodexRequest("Rechne.", "6 mal 7", Schema), CancellationToken.None);

            var args = fake.Args;
            Assert.Equal("exec", args[0]);
            Assert.Contains("--ephemeral", args);
            Assert.Contains("--ignore-user-config", args);
            Assert.Contains("--ignore-rules", args);
            Assert.Contains("--skip-git-repo-check", args);
            Assert.Equal("read-only", args[Array.IndexOf(args, "-s") + 1]);
            Assert.Contains("--output-schema", args);
            Assert.Equal("-", args[^1]);
            Assert.DoesNotContain("-m", args);
            Assert.Equal(Path.GetFullPath(fake.Home), fake.Read("home.txt").Trim());
        }

        [Fact]
        public async Task The_model_and_images_are_passed_when_given()
        {
            using var fake = new FakeCodex(Answer42);
            using var runner = Runner(fake, options => options.Model = "some-model");

            await runner.RunAsync(new CodexRequest("x", "y", Schema, ["/tmp/a.jpg", "/tmp/b.jpg"]), CancellationToken.None);

            var args = fake.Args;
            Assert.Equal("some-model", args[Array.IndexOf(args, "-m") + 1]);
            Assert.Equal(["/tmp/a.jpg", "/tmp/b.jpg"], args.Where((_, index) => index > 0 && args[index - 1] == "-i"));
        }

        [Fact]
        public async Task The_instruction_and_the_material_go_in_through_stdin_and_the_schema_through_a_file()
        {
            using var fake = new FakeCodex("RECORD=\"$(dirname \"$0\")/record\"\nprompt=\"$(cat)\"\nprintf '%s' \"$prompt\" > \"$RECORD/prompt.txt\"\ncp \"$schema\" \"$RECORD/schema.json\"\nprintf '{\"answer\":1}' > \"$out\"");
            using var runner = Runner(fake);

            await runner.RunAsync(new CodexRequest("Antworte kurz.", "Material: Brüche", Schema), CancellationToken.None);

            var prompt = fake.Read("prompt.txt");
            Assert.StartsWith("Antworte kurz.", prompt, StringComparison.Ordinal);
            Assert.Contains("Material: Brüche", prompt, StringComparison.Ordinal);
            Assert.Equal(Schema, fake.Read("schema.json"));
        }

        [Fact]
        public async Task A_json_answer_in_a_code_fence_is_still_read()
        {
            using var fake = new FakeCodex("cat > /dev/null\nprintf '```json\\n{\"answer\":7}\\n```' > \"$out\"");
            using var runner = Runner(fake);

            var result = await runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None);

            Assert.Equal(7, result.Json!.Value.GetProperty("answer").GetInt32());
        }

        [Fact]
        public async Task A_sentence_around_the_json_is_ignored()
        {
            using var fake = new FakeCodex("cat > /dev/null\nprintf 'Hier ist es: {\"answer\":9} Viel Erfolg!' > \"$out\"");
            using var runner = Runner(fake);

            var result = await runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None);

            Assert.Equal(9, result.Json!.Value.GetProperty("answer").GetInt32());
        }

        [Fact]
        public async Task An_answer_that_is_not_json_fails_when_a_schema_was_asked_for()
        {
            using var fake = new FakeCodex("cat > /dev/null\nprintf 'ich weiss nicht' > \"$out\"");
            using var runner = Runner(fake);

            var error = await Assert.ThrowsAsync<CodexFailedException>(() => runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None));

            Assert.Contains("format", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Without_a_schema_the_text_is_returned_as_it_is()
        {
            using var fake = new FakeCodex("cat > /dev/null\nprintf 'Hallo Welt\\n' > \"$out\"");
            using var runner = Runner(fake);

            var result = await runner.RunAsync(new CodexRequest("x", "y"), CancellationToken.None);

            Assert.Equal("Hallo Welt", result.Text);
            Assert.Null(result.Json);
            Assert.DoesNotContain("--output-schema", fake.Args);
        }

        [Fact]
        public async Task A_failing_program_is_a_failed_answer()
        {
            using var fake = new FakeCodex("cat > /dev/null\necho 'boom' >&2\nexit 3");
            using var runner = Runner(fake);

            await Assert.ThrowsAsync<CodexFailedException>(() => runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None));
        }

        [Fact]
        public async Task An_empty_answer_is_a_failed_answer()
        {
            using var fake = new FakeCodex("cat > /dev/null\n: > \"$out\"");
            using var runner = Runner(fake);

            await Assert.ThrowsAsync<CodexFailedException>(() => runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None));
        }

        [Fact]
        public async Task A_program_that_takes_too_long_is_stopped()
        {
            using var fake = new FakeCodex("cat > /dev/null\nsleep 30");
            using var runner = Runner(fake, options => options.TimeoutSeconds = 1);
            var watch = Stopwatch.StartNew();

            var error = await Assert.ThrowsAsync<CodexFailedException>(() => runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None));

            Assert.Contains("too long", error.Message, StringComparison.Ordinal);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
        }

        [Fact]
        public async Task A_missing_program_is_reported_as_not_installed()
        {
            using var runner = new CodexRunner(new CodexOptions { Executable = "/nonexistent/codex", Home = Path.Combine(Path.GetTempPath(), "junotes-nohome-" + Guid.NewGuid().ToString("N")) });

            await Assert.ThrowsAsync<CodexUnavailableException>(() => runner.RunAsync(new CodexRequest("x", "y"), CancellationToken.None));
        }

        [Fact]
        public async Task The_work_folder_is_left_clean()
        {
            using var fake = new FakeCodex(Answer42);
            using var runner = Runner(fake);

            await runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None);

            Assert.Empty(Directory.GetFiles(Path.Combine(fake.Home, "work")));
        }

        [Fact]
        public async Task The_number_of_parallel_runs_is_limited()
        {
            using var fake = new FakeCodex("cat > /dev/null\nsleep 1\nprintf '{\"answer\":1}' > \"$out\"");

            async Task<TimeSpan> Time(int parallel)
            {
                using var runner = Runner(fake, options => options.MaxParallel = parallel);
                var watch = Stopwatch.StartNew();
                await Task.WhenAll(runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None), runner.RunAsync(new CodexRequest("x", "y", Schema), CancellationToken.None));
                return watch.Elapsed;
            }

            Assert.True(await Time(1) >= TimeSpan.FromSeconds(1.9));
            Assert.True(await Time(2) < TimeSpan.FromSeconds(1.9));
        }

        [Fact]
        public void Json_is_found_in_the_usual_wrappers()
        {
            Assert.Equal(1, CodexRunner.ParseJson("{\"a\":1}").GetProperty("a").GetInt32());
            Assert.Equal(System.Text.Json.JsonValueKind.Array, CodexRunner.ParseJson("[1,2]").ValueKind);
            Assert.Throws<CodexFailedException>(() => CodexRunner.ParseJson("nothing here"));
            Assert.Throws<CodexFailedException>(() => CodexRunner.ParseJson("{ broken"));
        }
    }
}
