using JuNotes.Backend.Library;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JuNotes.Frontend.Pages.Notes
{
    public sealed record SubjectGroup(string Name, string Color, IReadOnlyList<NotebookInfo> Notebooks);

    public sealed class IndexModel(LibraryReader library) : PageModel
    {
        public IReadOnlyList<SubjectGroup> Groups { get; private set; } = [];

        public async Task OnGetAsync(CancellationToken ct)
        {
            var subjects = await library.SubjectsAsync(ct);
            var notebooks = await library.NotebooksAsync(ct);
            var groups = subjects
                .Select(subject => new SubjectGroup(subject.Name, PageRenderer_SafeColor(subject.Color), notebooks.Where(notebook => notebook.SubjectId == subject.Id).ToList()))
                .Where(group => group.Notebooks.Count > 0)
                .ToList();

            // Notebooks that belong to no subject, such as a scan made when no lesson was running.
            var known = subjects.Select(subject => subject.Id).ToHashSet();
            var loose = notebooks.Where(notebook => notebook.SubjectId is null || !known.Contains(notebook.SubjectId.Value)).ToList();
            if (loose.Count > 0)
            {
                groups.Add(new SubjectGroup("Lose Blätter", "#8e8e93", loose));
            }

            Groups = groups;
        }

        // A colour from the data goes into a style attribute, so only a plain hex colour is allowed through.
        internal static string PageRenderer_SafeColor(string color) => color.Length == 7 && color[0] == '#' && color[1..].All(char.IsAsciiHexDigit) ? color : "#8e8e93";
    }
}
