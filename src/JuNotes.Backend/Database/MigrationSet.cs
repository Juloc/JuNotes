using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace JuNotes.Backend.Database
{
    // The numbered migrations that ship inside the assembly. The baseline Schema.sql is frozen; every change is a new NNNN_name.sql.
    public sealed partial class MigrationSet
    {
        private const string Prefix = "JuNotes.Database.Migrations.";

        private MigrationSet(IReadOnlyList<Migration> migrations) => Migrations = migrations;

        public IReadOnlyList<Migration> Migrations { get; }

        public int Latest => Migrations.Count == 0 ? 0 : Migrations[^1].Number;

        public static MigrationSet FromAssembly(Assembly assembly)
            => FromNames(assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)).Select(name => (name[Prefix.Length..], name)),
                name => ReadResource(assembly, name));

        // Numbers start at 1 and have no gaps, so a missing file is noticed before any database is touched.
        internal static MigrationSet FromNames(IEnumerable<(string File, string Resource)> files, Func<string, string> read)
        {
            var list = new List<Migration>();
            foreach (var (file, resource) in files)
            {
                var match = FileName().Match(file);
                if (!match.Success)
                {
                    throw new InvalidOperationException($"Migration file '{file}' must be named NNNN_short_name.sql.");
                }

                list.Add(new Migration(int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture), match.Groups["name"].Value, () => read(resource)));
            }

            list.Sort((left, right) => left.Number.CompareTo(right.Number));
            for (var index = 0; index < list.Count; index++)
            {
                if (list[index].Number != index + 1)
                {
                    throw new InvalidOperationException($"Migrations must be numbered 1, 2, 3 without gaps or duplicates; found {list[index].Number} at position {index + 1}.");
                }
            }

            return new MigrationSet(list);
        }

        private static string ReadResource(Assembly assembly, string name)
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing embedded resource '{name}'.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        [GeneratedRegex(@"^(?<number>\d{4})_(?<name>[a-z0-9_]+)\.sql$")]
        private static partial Regex FileName();
    }

    public sealed record Migration(int Number, string Name, Func<string> Sql);
}
