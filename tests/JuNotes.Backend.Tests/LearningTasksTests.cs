using System.Text.Json;
using JuNotes.Backend.Ai;
using JuNotes.Integrations.Codex;

namespace JuNotes.Backend.Tests
{
    public class LearningTasksTests
    {
        private static JsonElement Json(string text) => JsonElement.Parse(text);

        [Theory]
        [InlineData("quiz")]
        [InlineData("test")]
        [InlineData("cards")]
        [InlineData("check")]
        [InlineData("grade")]
        public void Every_schema_is_valid_json_and_strict_with_everything_required(string task)
        {
            var schema = Json(LearningTasks.Schema(task));

            Assert.Equal("object", schema.GetProperty("type").GetString());
            AssertStrict(schema);
        }

        // The strict structured-output mode needs additionalProperties false and every property listed as required, on every object.
        private static void AssertStrict(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (schema.TryGetProperty("properties", out var properties))
            {
                Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
                var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).Order().ToList();
                Assert.Equal(properties.EnumerateObject().Select(property => property.Name).Order().ToList(), required);
                foreach (var property in properties.EnumerateObject())
                {
                    AssertStrict(property.Value);
                }
            }

            if (schema.TryGetProperty("items", out var items))
            {
                AssertStrict(items);
            }
        }

        [Fact]
        public void An_unknown_task_has_no_schema() => Assert.Throws<ArgumentOutOfRangeException>(() => LearningTasks.Schema("poem"));

        [Fact]
        public void The_requested_counts_are_kept_within_sensible_bounds()
        {
            Assert.Contains("genau 5 Fragen", LearningTasks.Task("quiz", new AiRequest("x", null, null, null, null, null, null)), StringComparison.Ordinal);
            Assert.Contains("genau 15 Fragen", LearningTasks.Task("quiz", new AiRequest("x", 500, null, null, null, null, null)), StringComparison.Ordinal);
            Assert.Contains("genau 1 Fragen", LearningTasks.Task("quiz", new AiRequest("x", -3, null, null, null, null, null)), StringComparison.Ordinal);
            Assert.Contains("etwa 90 Minuten", LearningTasks.Task("test", new AiRequest("x", null, 600, null, null, null, null)), StringComparison.Ordinal);
        }

        [Fact]
        public void The_material_is_wrapped_so_the_model_takes_it_as_data()
        {
            var input = LearningTasks.Input("quiz", new AiRequest("  Brüche addieren  ", null, null, "Mathe", null, null, null));

            Assert.Equal("Fach: Mathe\n\n<material>\nBrüche addieren\n</material>", input);
        }

        [Fact]
        public void A_grade_request_labels_question_expected_answer_and_answer()
        {
            var input = LearningTasks.Input("grade", new AiRequest(null, null, null, null, "Was ist 1/2 + 1/3?", "5/6", "fünf Sechstel"));

            Assert.Contains("<frage>\nWas ist 1/2 + 1/3?\n</frage>", input, StringComparison.Ordinal);
            Assert.Contains("<musterantwort>\n5/6\n</musterantwort>", input, StringComparison.Ordinal);
            Assert.Contains("<antwort>\nfünf Sechstel\n</antwort>", input, StringComparison.Ordinal);
        }

        [Fact]
        public void Requests_without_material_or_with_too_much_are_refused()
        {
            Assert.Throws<AiRequestException>(() => LearningTasks.Validate("quiz", new AiRequest(null, null, null, null, null, null, null)));
            Assert.Throws<AiRequestException>(() => LearningTasks.Validate("quiz", new AiRequest("   ", null, null, null, null, null, null)));
            Assert.Throws<AiRequestException>(() => LearningTasks.Validate("quiz", new AiRequest(new string('x', LearningTasks.MaxTextLength + 1), null, null, null, null, null, null)));
            Assert.Throws<AiRequestException>(() => LearningTasks.Validate("poem", new AiRequest("x", null, null, null, null, null, null)));
            Assert.Throws<AiRequestException>(() => LearningTasks.Validate("grade", new AiRequest(null, null, null, null, "Frage", null, null)));
            LearningTasks.Validate("quiz", new AiRequest("Stoff", null, null, null, null, null, null));
            LearningTasks.Validate("grade", new AiRequest(null, null, null, null, "Frage", null, "Antwort"));
        }

        [Fact]
        public void A_quiz_keeps_the_usable_questions_and_trims_them()
        {
            var result = (QuizResult)LearningTasks.Normalize("quiz", Json("""{"questions":[{"question":"  Wie heißt der Nenner? ","answer":"unten","hint":null},{"question":"","answer":"x","hint":null},{"question":"ohne Antwort","answer":"  ","hint":"h"},{"question":"Zähler?","answer":"oben","hint":"Bruchstrich"}]}"""));

            Assert.Equal(["Wie heißt der Nenner?", "Zähler?"], result.Questions.Select(question => question.Question));
            Assert.Null(result.Questions[0].Hint);
            Assert.Equal("Bruchstrich", result.Questions[1].Hint);
        }

        [Fact]
        public void A_quiz_with_nothing_usable_fails() =>
            Assert.Throws<CodexFailedException>(() => LearningTasks.Normalize("quiz", Json("""{"questions":[{"question":"","answer":""}]}""")));

        [Fact]
        public void A_test_sums_its_points_and_never_has_negative_ones()
        {
            var result = (TestResult)LearningTasks.Normalize("test", Json("""{"title":"Brüche","tasks":[{"question":"a","points":3,"solution":"x"},{"question":"b","points":-5,"solution":"y"},{"question":"c","points":4,"solution":"z"}]}"""));

            Assert.Equal("Brüche", result.Title);
            Assert.Equal(7, result.TotalPoints);
            Assert.Equal([3, 0, 4], result.Tasks.Select(task => task.Points));
        }

        [Fact]
        public void A_test_without_a_title_gets_one_and_without_tasks_fails()
        {
            Assert.Equal("Probetest", ((TestResult)LearningTasks.Normalize("test", Json("""{"title":"","tasks":[{"question":"a","points":1,"solution":"x"}]}"""))).Title);
            Assert.Throws<CodexFailedException>(() => LearningTasks.Normalize("test", Json("""{"title":"x","tasks":[]}""")));
        }

        [Fact]
        public void Cards_need_both_sides()
        {
            var result = (CardsResult)LearningTasks.Normalize("cards", Json("""{"cards":[{"front":"Nenner","back":"unter dem Strich"},{"front":"nur vorn","back":""}]}"""));

            Assert.Equal(new Card("Nenner", "unter dem Strich"), Assert.Single(result.Cards));
        }

        [Fact]
        public void Check_notes_have_a_known_kind_and_may_be_empty()
        {
            var result = (CheckResult)LearningTasks.Normalize("check", Json("""{"notes":[{"kind":"error","quote":"1/2+1/3=2/5","text":"Das ist falsch.","suggestion":"5/6"},{"kind":"praise","quote":null,"text":"Toll","suggestion":null},{"kind":"missing","quote":null,"text":"Kürzen fehlt","suggestion":null}]}"""));

            Assert.Equal(["error", "missing"], result.Notes.Select(note => note.Kind));
            Assert.Equal("5/6", result.Notes[0].Suggestion);
            Assert.Empty(((CheckResult)LearningTasks.Normalize("check", Json("""{"notes":[]}"""))).Notes);
        }

        [Theory]
        [InlineData(1.7, 1.0)]
        [InlineData(-0.4, 0.0)]
        [InlineData(0.6, 0.6)]
        public void A_grade_score_is_kept_between_zero_and_one(double given, double expected)
        {
            var result = (GradeResult)LearningTasks.Normalize("grade", Json($$"""{"correct":true,"score":{{given.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"feedback":" gut "}"""));

            Assert.Equal(expected, result.Score, 3);
            Assert.Equal("gut", result.Feedback);
        }

        [Theory]
        [InlineData("quiz", """{"wrong":[]}""")]
        [InlineData("quiz", """{"questions":"not a list"}""")]
        [InlineData("grade", """{"correct":"yes","score":1,"feedback":"x"}""")]
        [InlineData("grade", """{"score":1}""")]
        public void An_answer_of_the_wrong_shape_fails_cleanly(string task, string answer)
            => Assert.Throws<CodexFailedException>(() => LearningTasks.Normalize(task, Json(answer)));
    }
}
