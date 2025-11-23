using System.Globalization;
using System.Text;

public static class CsvParser
{
    // Supports two formats:
    // 1) Simple: tags;start;end;notes  (legacy)
    // 2) ManicTime CSV with header containing Name,Start,End,Notes - fields may be quoted and Name contains commas
    public static IEnumerable<WorkEntry> Read(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) yield break;

        // Skip initial comments and blank lines to find first meaningful line
        int i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length) yield break;

        // If the first non-empty line looks like a ManicTime header (contains Name and Start), use CSV parsing
        var first = lines[i].Trim();
        if (first.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0 && first.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Parse header to find column indices (join lines if header contains embedded newlines)
            var headerBuilder = new StringBuilder(first);
            var (headerFieldsTemp, headerComplete) = TryParseCsvLine(headerBuilder.ToString());
            int headerLineIdx = i;
            while (!headerComplete)
            {
                headerLineIdx++;
                if (headerLineIdx >= lines.Length) break;
                headerBuilder.Append("\n");
                headerBuilder.Append(lines[headerLineIdx]);
                (headerFieldsTemp, headerComplete) = TryParseCsvLine(headerBuilder.ToString());
            }
            var headerFields = headerFieldsTemp.Select(h => h.Trim()).ToArray();
            int nameIdx = Array.FindIndex(headerFields, h => string.Equals(h, "name", StringComparison.OrdinalIgnoreCase));
            int startIdx = Array.FindIndex(headerFields, h => string.Equals(h, "start", StringComparison.OrdinalIgnoreCase));
            int endIdx = Array.FindIndex(headerFields, h => string.Equals(h, "end", StringComparison.OrdinalIgnoreCase));
            int notesIdx = Array.FindIndex(headerFields, h => h.IndexOf("note", StringComparison.OrdinalIgnoreCase) >= 0);
            int billableIdx = Array.FindIndex(headerFields, h => string.Equals(h, "billable", StringComparison.OrdinalIgnoreCase));

            // Advance to data rows
            i++;
            i++; // move to first data line after header
            for (; i < lines.Length; i++)
            {
                // Accumulate physical lines until we have a complete CSV record
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                if (lines[i].TrimStart().StartsWith("#")) continue;

                var recordBuilder = new StringBuilder(lines[i]);
                var (fieldsTemp, complete) = TryParseCsvLine(recordBuilder.ToString());
                int recordLineIdx = i;
                while (!complete)
                {
                    recordLineIdx++;
                    if (recordLineIdx >= lines.Length) break;
                    recordBuilder.Append("\n");
                    recordBuilder.Append(lines[recordLineIdx]);
                    (fieldsTemp, complete) = TryParseCsvLine(recordBuilder.ToString());
                }
                var fields = fieldsTemp.ToArray();
                // advance i to the last consumed physical line
                i = recordLineIdx;
                string nameField = nameIdx >= 0 && nameIdx < fields.Length ? fields[nameIdx] : string.Empty;
                string startField = startIdx >= 0 && startIdx < fields.Length ? fields[startIdx] : string.Empty;
                string endField = endIdx >= 0 && endIdx < fields.Length ? fields[endIdx] : string.Empty;
                string notesField = notesIdx >= 0 && notesIdx < fields.Length ? fields[notesIdx] : string.Empty;
                string billableField = billableIdx >= 0 && billableIdx < fields.Length ? fields[billableIdx] : string.Empty;

                foreach (var e in BuildEntriesFromName(nameField, startField, endField, notesField, billableField)) yield return e;
            }

            yield break;
        }

        // Fallback: original semicolon-based simple parser
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.TrimStart().StartsWith("#")) continue;
            if (raw.TrimStart().ToLower().StartsWith("tags")) continue;

            var parts = raw.Split(';');
            if (parts.Length < 4)
            {
                // Try comma as separator fallback (naive)
                var commaParts = raw.Split(',');
                if (commaParts.Length >= 4)
                {
                    var tagsPart = string.Join(',', commaParts.Take(commaParts.Length - 3));
                    var start = commaParts[^3].Trim();
                    var end = commaParts[^2].Trim();
                    var notes = commaParts[^1].Trim();
                    foreach (var e in BuildEntriesFromName(tagsPart, start, end, notes, string.Empty)) yield return e;
                    continue;
                }
                Console.WriteLine($"Skipping malformed line: {raw}");
                continue;
            }

            var tagsPart2 = parts[0].Trim();
            var startStr = parts[1].Trim();
            var endStr = parts[2].Trim();
            var notesStr = parts[3].Trim();

            foreach (var e in BuildEntriesFromName(tagsPart2, startStr, endStr, notesStr, string.Empty)) yield return e;
        }
    }

    static IEnumerable<WorkEntry> BuildEntriesFromName(string nameField, string startStr, string endStr, string notes, string billableField)
    {
        // nameField is a comma-separated list of tags (e.g. "Company, Project, Group, Task")
        var tags = nameField.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();

        // Make mapping forgiving: allow 2..n tags
        string company = tags.Length > 0 ? tags[0] : string.Empty;
        string project = tags.Length > 1 ? tags[1] : string.Empty;
        string group = tags.Length > 2 ? tags[2] : string.Empty;
        string task = tags.Length > 3 ? tags[3] : (tags.Length > 2 ? tags[2] : string.Empty);
        var extras = tags.Length > 4 ? tags.Skip(4).ToArray() : Array.Empty<string>();

        DateTime? start = ParseDate(startStr);
        DateTime? end = ParseDate(endStr);

        var noteWithExtras = notes ?? string.Empty;
        if (extras.Any()) noteWithExtras += " | " + string.Join(", ", extras);

        bool? billable = null;
        if (!string.IsNullOrWhiteSpace(billableField))
        {
            var v = billableField.Trim().ToLowerInvariant();
            if (v == "yes" || v == "y" || v == "true" || v == "1") billable = true;
            else if (v == "no" || v == "n" || v == "false" || v == "0") billable = false;
        }

        yield return new WorkEntry
        {
            Company = company,
            Project = project,
            Group = group,
            Task = task,
            Extras = extras,
            Start = start,
            End = end,
            Notes = noteWithExtras,
            Billable = billable
        };
    }

    // Try parsing a CSV record; returns (fields array, complete)
    // complete==false means the input ended while inside an open quoted field (needs more physical lines)
    static (IEnumerable<string> fields, bool complete) TryParseCsvLine(string line)
    {
        var fields = new List<string>();
        if (line == null) return (fields, true);
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    // lookahead for escaped quote
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // skip next
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        // if still inQuotes => record incomplete
        if (inQuotes)
        {
            return (fields, false);
        }
        fields.Add(sb.ToString());
        return (fields, true);
    }

    static DateTime? ParseDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // Try round-trip / ISO formats first
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)) return dt;
        if (DateTime.TryParse(s, out dt)) return dt;
        // Try trimming fractional seconds timezone formats
        if (DateTime.TryParseExact(s, "yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;
        if (DateTime.TryParseExact(s, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;
        return null;
    }
}
