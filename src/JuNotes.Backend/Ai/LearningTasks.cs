using System.Text.Json;

namespace JuNotes.Backend.Ai
{
    public sealed record AiRequest(string? Text, int? Count, int? DurationMinutes, string? Subject, string? Question, string? Expected, string? Answer);

    public sealed record QuizQuestion(string Question, string Answer, string? Hint);

    public sealed record QuizResult(IReadOnlyList<QuizQuestion> Questions);

    public sealed record TestTask(string Question, int Points, string Solution);

    public sealed record TestResult(string Title, IReadOnlyList<TestTask> Tasks, int TotalPoints);

    public sealed record Card(string Front, string Back);

    public sealed record CardsResult(IReadOnlyList<Card> Cards);

    public sealed record CheckNote(string Kind, string? Quote, string Text, string? Suggestion);

    public sealed record CheckResult(IReadOnlyList<CheckNote> Notes);

    public sealed record GradeResult(bool Correct, double Score, string Feedback);

    // A cover the model designed from a description, in the parts the app can draw itself: two colours, an emoji or one of the app's symbols, and a pattern.
    public sealed record CoverResult(string ColorA, string ColorB, string? Emoji, string? Symbol, string Pattern);

    // The request itself is not usable (no material, a number out of range), as opposed to the model failing.
    public sealed class AiRequestException(string message) : Exception(message);

    // One kind of help: what it asks the model for, in which shape, and how the answer is checked. The schemas are strict, and the answer is checked again
    // here, because the model's own promise to follow a schema is not something the app should depend on.
    public static class LearningTasks
    {
        public const int MaxTextLength = 30_000;

        public static readonly IReadOnlyList<string> Keys = ["quiz", "test", "cards", "check", "grade", "cover"];

        // The symbols of the iPad app a cover may use; anything else the model names is dropped.
        public static readonly IReadOnlyList<string> CoverSymbols =
        [
            "function", "text.book.closed", "character.bubble", "leaf", "atom", "globe.europe.africa", "paintpalette", "music.note", "figure.run", "laptopcomputer",
            "book", "pencil", "graduationcap", "heart", "star", "sparkles", "house", "figure.2.and.child.holdinghands", "teddybear", "puzzlepiece",
            "camera", "folder", "tray.full", "calendar", "flag", "lightbulb", "cross.case", "theatermasks", "hands.clap", "brain.head.profile",
        ];

        private const string StringSchema = """{"type":"string"}""";
        private const string NullableStringSchema = """{"type":["string","null"]}""";

        public static string Schema(string task) => task switch
        {
            "quiz" => Object("questions", Array(ObjectOf(("question", StringSchema), ("answer", StringSchema), ("hint", NullableStringSchema)))),
            "test" => Object(("title", StringSchema), ("tasks", Array(ObjectOf(("question", StringSchema), ("points", """{"type":"integer"}"""), ("solution", StringSchema))))),
            "cards" => Object("cards", Array(ObjectOf(("front", StringSchema), ("back", StringSchema)))),
            "check" => Object("notes", Array(ObjectOf(("kind", """{"type":"string","enum":["missing","error","unclear"]}"""), ("quote", NullableStringSchema), ("text", StringSchema), ("suggestion", NullableStringSchema)))),
            "grade" => ObjectOf(("correct", """{"type":"boolean"}"""), ("score", """{"type":"number"}"""), ("feedback", StringSchema)),
            "cover" => ObjectOf(("colorA", StringSchema), ("colorB", StringSchema), ("emoji", NullableStringSchema), ("symbol", NullableStringSchema), ("pattern", """{"type":"string","enum":["none","lines","grid","dots"]}""")),
            _ => throw new ArgumentOutOfRangeException(nameof(task)),
        };

        // What is asked, in addition to the profile's own prompt.
        public static string Task(string task, AiRequest request) => task switch
        {
            "quiz" => $"Stelle genau {Count(request, 5, 1, 15)} Fragen zu diesem Material.",
            "test" => $"Stelle einen Probetest mit {Count(request, 6, 2, 12)} Aufgaben zusammen, der etwa {Math.Clamp(request.DurationMinutes ?? 30, 10, 90)} Minuten dauert.",
            "cards" => $"Mache höchstens {Count(request, 12, 1, 30)} Karteikarten aus diesem Material.",
            "check" => "Prüfe dieses Material auf Lücken, Fehler und Unklares.",
            "grade" => "Bewerte die Antwort.",
            "cover" => "Entwirf ein Cover für ein Heft oder einen Ordner nach der Beschreibung: zwei Farben als Hex-Code (#rrggbb) für einen Farbverlauf, passend zum Thema und gut lesbar, "
                + "entweder ein Emoji oder eines dieser Symbole (nie beides, das andere ist null): " + string.Join(", ", CoverSymbols) + ", und ein Muster (none, lines, grid, dots).",
            _ => throw new ArgumentOutOfRangeException(nameof(task)),
        };

        // The material the model gets, labelled as data.
        public static string Input(string task, AiRequest request)
        {
            var subject = string.IsNullOrWhiteSpace(request.Subject) ? "" : $"Fach: {request.Subject!.Trim()}\n\n";
            return task == "grade"
                ? subject + $"<frage>\n{request.Question!.Trim()}\n</frage>\n<musterantwort>\n{request.Expected?.Trim()}\n</musterantwort>\n<antwort>\n{request.Answer!.Trim()}\n</antwort>"
                : subject + $"<material>\n{request.Text!.Trim()}\n</material>";
        }

        public static void Validate(string task, AiRequest request)
        {
            if (!Keys.Contains(task))
            {
                throw new AiRequestException($"Unknown task '{task}'.");
            }

            if (task == "grade")
            {
                if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Answer))
                {
                    throw new AiRequestException("A question and an answer are needed.");
                }

                if ((request.Question.Length + (request.Expected?.Length ?? 0) + request.Answer.Length) > MaxTextLength)
                {
                    throw new AiRequestException("The text is too long.");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                throw new AiRequestException("There is no material to work with.");
            }

            if (request.Text.Length > MaxTextLength)
            {
                throw new AiRequestException($"The material is longer than {MaxTextLength} characters.");
            }
        }

        // Turns the model's answer into the shape the app is promised: unusable parts are dropped, numbers are put in range, and an answer with nothing usable fails.
        public static object Normalize(string task, JsonElement answer)
        {
            try
            {
                return task switch
                {
                    "quiz" => Require(new QuizResult(Items(answer, "questions", item => Text(item, "question") is { } question && Text(item, "answer") is { } reply ? new QuizQuestion(question, reply, Text(item, "hint")) : null)), "questions"),
                    "test" => NormalizeTest(answer),
                    "cards" => Require(new CardsResult(Items(answer, "cards", item => Text(item, "front") is { } front && Text(item, "back") is { } back ? new Card(front, back) : null)), "cards"),
                    "check" => new CheckResult(Items(answer, "notes", item => item.TryGetProperty("kind", out var kind) && kind.GetString() is "missing" or "error" or "unclear" && Text(item, "text") is { } text
                        ? new CheckNote(kind.GetString()!, Text(item, "quote"), text, Text(item, "suggestion")) : null)),
                    "grade" => new GradeResult(answer.GetProperty("correct").GetBoolean(), Math.Clamp(answer.GetProperty("score").GetDouble(), 0, 1), Text(answer, "feedback") ?? ""),
                    "cover" => NormalizeCover(answer),
                    _ => throw new ArgumentOutOfRangeException(nameof(task)),
                };
            }
            catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new Integrations.Codex.CodexFailedException("The answer did not have the expected shape.");
            }
        }

        private static CoverResult NormalizeCover(JsonElement answer)
        {
            var a = HexColor(Text(answer, "colorA")) ?? throw new Integrations.Codex.CodexFailedException("The cover had no usable colour.");
            var b = HexColor(Text(answer, "colorB")) ?? a;
            var emoji = Text(answer, "emoji");
            // An emoji is a few characters without letters or digits; anything longer or with words in it is not an emoji.
            if (emoji is not null && (emoji.Length > 8 || emoji.Any(char.IsAsciiLetterOrDigit)))
            {
                emoji = null;
            }

            var symbol = Text(answer, "symbol") is { } name && CoverSymbols.Contains(name) ? name : null;
            if (emoji is not null)
            {
                symbol = null;
            }

            var pattern = Text(answer, "pattern") is "lines" or "grid" or "dots" ? Text(answer, "pattern")! : "none";
            return new CoverResult(a, b, emoji, symbol, pattern);
        }

        private static string? HexColor(string? value)
            => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;

        private static TestResult NormalizeTest(JsonElement answer)
        {
            var tasks = Items(answer, "tasks", item => Text(item, "question") is { } question && Text(item, "solution") is { } solution
                ? new TestTask(question, Math.Max(0, item.TryGetProperty("points", out var points) && points.TryGetInt32(out var value) ? value : 0), solution) : null);
            var result = new TestResult(Text(answer, "title") ?? "Probetest", tasks, tasks.Sum(task => task.Points));
            return tasks.Count == 0 ? throw new Integrations.Codex.CodexFailedException("The answer had no tasks.") : result;
        }

        private static T Require<T>(T result, string what) where T : class
            => (result is QuizResult { Questions.Count: 0 } or CardsResult { Cards.Count: 0 }) ? throw new Integrations.Codex.CodexFailedException($"The answer had no {what}.") : result;

        private static List<T> Items<T>(JsonElement parent, string name, Func<JsonElement, T?> read) where T : class
            => parent.GetProperty(name).EnumerateArray().Select(read).OfType<T>().ToList();

        private static string? Text(JsonElement parent, string name)
            => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()?.Trim() is { Length: > 0 } text ? text : null;

        private static int Count(AiRequest request, int fallback, int min, int max) => Math.Clamp(request.Count ?? fallback, min, max);

        private static string Array(string item) => $$"""{"type":"array","items":{{item}}}""";

        private static string ObjectOf(params (string Name, string Schema)[] properties)
        {
            var required = string.Join(",", properties.Select(property => "\"" + property.Name + "\""));
            var members = string.Join(",", properties.Select(property => "\"" + property.Name + "\":" + property.Schema));
            return "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[" + required + "],\"properties\":{" + members + "}}";
        }

        private static string Object(string name, string schema) => ObjectOf((name, schema));

        private static string Object(params (string Name, string Schema)[] properties) => ObjectOf(properties);
    }
}
