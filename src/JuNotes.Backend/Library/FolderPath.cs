namespace JuNotes.Backend.Library
{
    public static class FolderPath
    {
        // "Schule / Mathe": the folder's place in the tree, so nested folders read the way she sorted them.
        // A parent that is missing ends the walk, and so does a circle in the data.
        public static string Of(SubjectInfo folder, IReadOnlyDictionary<Guid, SubjectInfo> byId)
        {
            var names = new List<string>();
            var seen = new HashSet<Guid>();
            for (var current = folder; seen.Add(current.Id);)
            {
                names.Add(current.Name);
                if (current.ParentId is not { } parent || !byId.TryGetValue(parent, out var next))
                {
                    break;
                }

                current = next;
            }

            names.Reverse();
            return string.Join(" / ", names);
        }
    }
}
