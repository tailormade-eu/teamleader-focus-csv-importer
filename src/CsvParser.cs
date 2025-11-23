using System.Globalization;

public static class CsvParser
{
    // Expected format per project definition:
    // tags;start;end;notes  where tags is comma-separated list
    public static IEnumerable<WorkEntry> Read(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.TrimStart().StartsWith("#")) continue; // allow header or comments

            // Allow a header line starting with 'tags' to be skipped
            if (raw.TrimStart().ToLower().StartsWith("tags")) continue;

            // Split by semicolon into 4 parts: tagsPart;start;end;notes
            var parts = raw.Split(';');
            if (parts.Length < 4)
            {
                // Try comma as separator fallback: tags,start,end,notes
                var commaParts = raw.Split(',');
                if (commaParts.Length >= 4)
                {
                    var tagsPart = string.Join(',', commaParts.Take(commaParts.Length - 3));
                    var start = commaParts[^3].Trim();
                    var end = commaParts[^2].Trim();
                    var notes = commaParts[^1].Trim();
                    foreach (var e in BuildEntries(tagsPart, start, end, notes)) yield return e;
                    continue;
                }
                Console.WriteLine($"Skipping malformed line: {raw}");
                continue;
            }

            var tagsPart2 = parts[0].Trim();
            var startStr = parts[1].Trim();
            var endStr = parts[2].Trim();
            var notesStr = parts[3].Trim();

            foreach (var e in BuildEntries(tagsPart2, startStr, endStr, notesStr)) yield return e;
        }
    }

    static IEnumerable<WorkEntry> BuildEntries(string tagsPart, string startStr, string endStr, string notes)
    {
        var tags = tagsPart.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();
        if (tags.Length < 4)
        {
            Console.WriteLine($"Not enough tag levels (need >=4): {tagsPart}");
            yield break;
        }

        DateTime? start = ParseDate(startStr);
        DateTime? end = ParseDate(endStr);

        var extras = tags.Skip(4).ToArray();
        var noteWithExtras = notes;
        if (extras.Any()) noteWithExtras += " | " + string.Join(", ", extras);

        yield return new WorkEntry
        {
            Company = tags[0],
            Project = tags[1],
            Group = tags[2],
            Task = tags[3],
            Extras = extras,
            Start = start,
            End = end,
            Notes = noteWithExtras
        };
    }

    static DateTime? ParseDate(string s)
    {
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)) return dt;
        if (DateTime.TryParse(s, out dt)) return dt;
        return null;
    }
}
