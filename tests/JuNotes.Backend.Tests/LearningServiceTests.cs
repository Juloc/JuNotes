using System.Text.Json;
using JuNotes.Backend.Ai;
using JuNotes.Integrations.Codex;

namespace JuNotes.Backend.Tests
{
    public class LearningServiceTests
    {
        private sealed class FakeRunner : ICodexRunner
        {
            public CodexRequest? Last { get; private set; }

            public int Calls { get; private set; }

            public string Answer { get; set; } = """{"questions":[{"question":"Was ist ein Nenner?","answer":"Der untere Teil","hint":null}]}""";

            public Exception? Failure { get; set; }

            public Task<CodexResult> RunAsync(CodexRequest request, CancellationToken ct)
            {
                Calls++;
                Last = request;
                return Failure is not null ? throw Failure : Task.FromResult(new CodexResult(Answer, JsonElement.Parse(Answer)));
            }
        }

        private sealed class Fixture : IAsyncDisposable
        {
            public required TestDatabase Database { get; init; }

            public required FakeRunner Runner { get; init; }

            public required AiProfileStore Profiles { get; init; }

            public required AiUsageStore Usage { get; init; }

            public required LearningService Service { get; init; }

            public ValueTask DisposeAsync() => Database.DisposeAsync();
        }

        private static async Task<Fixture> CreateAsync(int dailyLimit = 60)
        {
            var database = await TestDatabase.CreateCurrentAsync();
            var profiles = new AiProfileStore(database.Data);
            await profiles.EnsureDefaultsAsync(CancellationToken.None);
            var usage = new AiUsageStore(database.Data);
            var runner = new FakeRunner();
            return new Fixture { Database = database, Runner = runner, Profiles = profiles, Usage = usage, Service = new LearningService(runner, profiles, usage, new AiOptions { DailyLimit = dailyLimit }, TimeProvider.System) };
        }

        private static AiRequest Material => new("Brüche: Nenner unten, Zähler oben.", 3, null, "Mathe", null, null, null);

        [RequiresDatabaseFact]
        public async Task A_task_runs_with_the_profiles_prompt_the_schema_and_the_material()
        {
            await using var fixture = await CreateAsync();

            var result = await fixture.Service.RunAsync("quiz", Material, CancellationToken.None);

            var quiz = Assert.IsType<QuizResult>(result);
            Assert.Equal("Was ist ein Nenner?", Assert.Single(quiz.Questions).Question);
            var request = fixture.Runner.Last!;
            Assert.Contains("Du stellst Fragen zum Lernstoff", request.Instruction, StringComparison.Ordinal);
            Assert.Contains("Schwierigkeit: mittel", request.Instruction, StringComparison.Ordinal);
            Assert.Contains("Ton: freundlich", request.Instruction, StringComparison.Ordinal);
            Assert.Contains("genau 3 Fragen", request.Instruction, StringComparison.Ordinal);
            Assert.Contains("<material>", request.Input, StringComparison.Ordinal);
            Assert.Equal(LearningTasks.Schema("quiz"), request.SchemaJson);
        }

        [RequiresDatabaseFact]
        public async Task The_admins_changes_to_a_profile_reach_the_next_request()
        {
            await using var fixture = await CreateAsync();
            var original = (await fixture.Profiles.GetAsync("quiz", CancellationToken.None))!;
            Assert.True(await fixture.Profiles.UpdateAsync(original with { SystemPrompt = "Frage nur nach Daten.", Difficulty = "schwer", Tone = "sachlich" }, CancellationToken.None));

            await fixture.Service.RunAsync("quiz", Material, CancellationToken.None);

            Assert.Contains("Frage nur nach Daten.", fixture.Runner.Last!.Instruction, StringComparison.Ordinal);
            Assert.Contains("Schwierigkeit: schwer", fixture.Runner.Last.Instruction, StringComparison.Ordinal);
            Assert.Contains("Ton: sachlich", fixture.Runner.Last.Instruction, StringComparison.Ordinal);
        }

        [RequiresDatabaseFact]
        public async Task A_switched_off_task_is_refused_and_never_reaches_the_model()
        {
            await using var fixture = await CreateAsync();
            var original = (await fixture.Profiles.GetAsync("test", CancellationToken.None))!;
            await fixture.Profiles.UpdateAsync(original with { Enabled = false }, CancellationToken.None);

            var error = await Assert.ThrowsAsync<AiRefusedException>(() => fixture.Service.RunAsync("test", Material, CancellationToken.None));

            Assert.Equal(403, error.StatusCode);
            Assert.Equal(0, fixture.Runner.Calls);
        }

        [RequiresDatabaseFact]
        public async Task The_daily_limit_stops_requests_and_the_model_is_not_called()
        {
            await using var fixture = await CreateAsync(dailyLimit: 2);
            await fixture.Service.RunAsync("quiz", Material, CancellationToken.None);
            await fixture.Service.RunAsync("quiz", Material, CancellationToken.None);

            var error = await Assert.ThrowsAsync<AiRefusedException>(() => fixture.Service.RunAsync("quiz", Material, CancellationToken.None));

            Assert.Equal(429, error.StatusCode);
            Assert.Equal(2, fixture.Runner.Calls);
        }

        [RequiresDatabaseFact]
        public async Task Two_requests_at_once_cannot_both_take_the_last_place()
        {
            await using var fixture = await CreateAsync(dailyLimit: 1);

            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            {
                try
                {
                    await fixture.Service.RunAsync("quiz", Material, CancellationToken.None);
                    return true;
                }
                catch (AiRefusedException)
                {
                    return false;
                }
            }));

            Assert.Single(results, ok => ok);
            Assert.Equal(1, fixture.Runner.Calls);
        }

        [RequiresDatabaseFact]
        public async Task A_failed_answer_is_counted_as_a_failure_and_passed_on()
        {
            await using var fixture = await CreateAsync();
            fixture.Runner.Failure = new CodexFailedException("Codex took too long.");

            await Assert.ThrowsAsync<CodexFailedException>(() => fixture.Service.RunAsync("quiz", Material, CancellationToken.None));

            var today = Assert.Single(await fixture.Usage.RecentAsync(7, CancellationToken.None));
            Assert.Equal(1, today.Requests);
            Assert.Equal(1, today.Failures);
        }

        [RequiresDatabaseFact]
        public async Task A_missing_codex_is_passed_on_as_unavailable()
        {
            await using var fixture = await CreateAsync();
            fixture.Runner.Failure = new CodexUnavailableException("not installed");

            await Assert.ThrowsAsync<CodexUnavailableException>(() => fixture.Service.RunAsync("quiz", Material, CancellationToken.None));
        }

        [RequiresDatabaseFact]
        public async Task An_answer_of_the_wrong_shape_is_a_failed_answer_too()
        {
            await using var fixture = await CreateAsync();
            fixture.Runner.Answer = """{"something":"else"}""";

            await Assert.ThrowsAsync<CodexFailedException>(() => fixture.Service.RunAsync("quiz", Material, CancellationToken.None));
        }

        [RequiresDatabaseFact]
        public async Task Invalid_requests_are_refused_before_anything_is_counted()
        {
            await using var fixture = await CreateAsync();

            await Assert.ThrowsAsync<AiRequestException>(() => fixture.Service.RunAsync("quiz", new AiRequest(null, null, null, null, null, null, null), CancellationToken.None));

            Assert.Empty(await fixture.Usage.RecentAsync(7, CancellationToken.None));
        }

        [RequiresDatabaseFact]
        public async Task The_general_settings_and_a_tasks_own_choice_decide_model_effort_and_limit()
        {
            await using var fixture = await CreateAsync();
            var settings = new AiSettingsStore(fixture.Database.Data);
            var service = new LearningService(fixture.Runner, fixture.Profiles, fixture.Usage, new AiOptions { DailyLimit = 60 }, TimeProvider.System, settings);

            await service.RunAsync("quiz", Material, CancellationToken.None);
            Assert.Null(fixture.Runner.Last!.Model);
            Assert.Null(fixture.Runner.Last.Effort);

            Assert.True(await settings.SaveAsync(new AiSettings("general-model", "low", 2), CancellationToken.None));
            await service.RunAsync("quiz", Material, CancellationToken.None);
            Assert.Equal("general-model", fixture.Runner.Last.Model);
            Assert.Equal("low", fixture.Runner.Last.Effort);

            var quiz = (await fixture.Profiles.GetAsync("quiz", CancellationToken.None))!;
            Assert.True(await fixture.Profiles.UpdateAsync(quiz with { Model = "quiz-model", Effort = "xhigh" }, CancellationToken.None));
            await Assert.ThrowsAsync<AiRefusedException>(() => service.RunAsync("quiz", Material, CancellationToken.None));
            Assert.Equal("general-model", fixture.Runner.Last.Model);

            Assert.True(await settings.SaveAsync(new AiSettings("general-model", "low", 5), CancellationToken.None));
            await service.RunAsync("quiz", Material, CancellationToken.None);
            Assert.Equal("quiz-model", fixture.Runner.Last.Model);
            Assert.Equal("xhigh", fixture.Runner.Last.Effort);
        }

        [RequiresDatabaseFact]
        public async Task Settings_are_validated_and_empty_values_fall_back_to_the_default()
        {
            await using var fixture = await CreateAsync();
            var settings = new AiSettingsStore(fixture.Database.Data);

            Assert.False(await settings.SaveAsync(new AiSettings("bad name; x", null, null), CancellationToken.None));
            Assert.False(await settings.SaveAsync(new AiSettings(null, "turbo", null), CancellationToken.None));
            Assert.False(await settings.SaveAsync(new AiSettings(null, null, 0), CancellationToken.None));
            Assert.False(await settings.SaveAsync(new AiSettings(null, null, AiSettingsRules.MaxDailyLimit + 1), CancellationToken.None));
            Assert.Equal(new AiSettings(null, null, null), await settings.GetAsync(CancellationToken.None));

            Assert.True(await settings.SaveAsync(new AiSettings(" model-1.2 ", "medium", 30), CancellationToken.None));
            Assert.Equal(new AiSettings("model-1.2", "medium", 30), await settings.GetAsync(CancellationToken.None));
            Assert.True(await settings.SaveAsync(new AiSettings("", "", null), CancellationToken.None));
            Assert.Equal(new AiSettings(null, null, null), await settings.GetAsync(CancellationToken.None));

            var quiz = (await fixture.Profiles.GetAsync("quiz", CancellationToken.None))!;
            Assert.False(await fixture.Profiles.UpdateAsync(quiz with { Model = "x y" }, CancellationToken.None));
            Assert.False(await fixture.Profiles.UpdateAsync(quiz with { Effort = "turbo" }, CancellationToken.None));
        }

        [RequiresDatabaseFact]
        public async Task The_defaults_are_created_once_and_never_overwrite_the_admins_changes()
        {
            await using var fixture = await CreateAsync();
            Assert.Equal(AiDefaults.Profiles.Select(profile => profile.Key), (await fixture.Profiles.ListAsync(CancellationToken.None)).Select(profile => profile.Key));
            var quiz = (await fixture.Profiles.GetAsync("quiz", CancellationToken.None))!;
            await fixture.Profiles.UpdateAsync(quiz with { SystemPrompt = "Mein Prompt" }, CancellationToken.None);

            await fixture.Profiles.EnsureDefaultsAsync(CancellationToken.None);

            Assert.Equal("Mein Prompt", (await fixture.Profiles.GetAsync("quiz", CancellationToken.None))!.SystemPrompt);
            Assert.Equal(AiDefaults.Profiles.Count, (await fixture.Profiles.ListAsync(CancellationToken.None)).Count);
        }

        [RequiresDatabaseFact]
        public async Task A_bad_profile_change_is_refused_and_a_reset_restores_the_original()
        {
            await using var fixture = await CreateAsync();
            var quiz = (await fixture.Profiles.GetAsync("quiz", CancellationToken.None))!;

            Assert.False(await fixture.Profiles.UpdateAsync(quiz with { SystemPrompt = "  " }, CancellationToken.None));
            Assert.False(await fixture.Profiles.UpdateAsync(quiz with { Difficulty = "unmöglich" }, CancellationToken.None));
            Assert.False(await fixture.Profiles.UpdateAsync(quiz with { Tone = "laut" }, CancellationToken.None));
            Assert.False(await fixture.Profiles.UpdateAsync(quiz with { Key = "unbekannt" }, CancellationToken.None));
            await fixture.Profiles.UpdateAsync(quiz with { SystemPrompt = "anders", Difficulty = "schwer" }, CancellationToken.None);

            Assert.True(await fixture.Profiles.ResetAsync("quiz", CancellationToken.None));

            Assert.Equal(AiDefaults.Profiles[0], await fixture.Profiles.GetAsync("quiz", CancellationToken.None));
            Assert.False(await fixture.Profiles.ResetAsync("unbekannt", CancellationToken.None));
        }

        [Fact]
        public void The_default_prompts_all_treat_the_material_as_data()
        {
            foreach (var profile in AiDefaults.Profiles)
            {
                Assert.Contains("nur Daten", profile.SystemPrompt, StringComparison.Ordinal);
                Assert.Contains(profile.Difficulty, AiDefaults.Difficulties);
                Assert.Contains(profile.Tone, AiDefaults.Tones);
            }

            Assert.Equal(LearningTasks.Keys.Order(), AiDefaults.Profiles.Select(profile => profile.Key).Order());
        }
    }
}
