namespace JuNotes.Backend.Tests
{
    // A stand-in for the codex program: a shell script that records how it was called and answers as the test says.
    internal sealed class FakeCodex : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "junotes-fakecodex-" + Guid.NewGuid().ToString("N"));

        public string Home => Path.Combine(Folder, "home");

        public string Executable => Path.Combine(Folder, "codex");

        public string Record => Path.Combine(Folder, "record");

        // `body` runs after the arguments are parsed: $out is the answer file, $schema the schema file, $prompt what came on stdin.
        public FakeCodex(string body)
        {
            Directory.CreateDirectory(Record);
            var script = $$"""
                #!/usr/bin/env bash
                out=""; schema=""; args=("$@")
                printf '%s\n' "$@" > '{{Record}}/args.txt'
                while [ $# -gt 0 ]; do
                  case "$1" in
                    -o) out="$2"; shift 2;;
                    --output-schema) schema="$2"; shift 2;;
                    *) shift;;
                  esac
                done
                set -- "${args[@]}"
                echo "$CODEX_HOME" > '{{Record}}/home.txt'
                {{body}}
                """;
            File.WriteAllText(Executable, script.Replace("\r\n", "\n", StringComparison.Ordinal));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public string[] Args => File.ReadAllLines(Path.Combine(Record, "args.txt"));

        public string Read(string name) => File.ReadAllText(Path.Combine(Record, name));

        public void Dispose()
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
    }
}
