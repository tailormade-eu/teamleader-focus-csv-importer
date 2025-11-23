using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

public static class CsvParser
{
    // Read entries from CSV. Prefer header-based CSV (ManicTime) and use CsvHelper for robust parsing.
    public static IEnumerable<WorkEntry> Read(string path)
    {
        if (!File.Exists(path)) yield break;

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Find first meaningful line to detect header
        string? firstNonEmpty = null;
        while (!reader.EndOfStream)
        {
            var l = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(l)) continue;
            if (l.TrimStart().StartsWith("#")) continue;
            firstNonEmpty = l;
            break;
        }

        // Reset to beginning for CsvHelper consumption
        stream.Seek(0, SeekOrigin.Begin);
        reader.DiscardBufferedData();

        if (firstNonEmpty != null && firstNonEmpty.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0 && firstNonEmpty.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                IgnoreBlankLines = true,
                BadDataFound = null,
                DetectColumnCountChanges = false,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null
            };

            using var csv = new CsvReader(reader, cfg);
            csv.Read();
            csv.ReadHeader();
            // determine header indices for robust fallback
            var header = csv.HeaderRecord ?? Array.Empty<string>();
            int idxName = Array.FindIndex(header, h => string.Equals(h, "name", StringComparison.OrdinalIgnoreCase));
            int idxStart = Array.FindIndex(header, h => string.Equals(h, "start", StringComparison.OrdinalIgnoreCase));
            int idxEnd = Array.FindIndex(header, h => string.Equals(h, "end", StringComparison.OrdinalIgnoreCase));
            int idxNotes = Array.FindIndex(header, h => h != null && h.IndexOf("note", StringComparison.OrdinalIgnoreCase) >= 0);
            int idxBillable = Array.FindIndex(header, h => string.Equals(h, "billable", StringComparison.OrdinalIgnoreCase));

            while (csv.Read())
            {
                csv.TryGetField<string?>("Name", out var nameField);
                csv.TryGetField<string?>("Start", out var startField);
                csv.TryGetField<string?>("End", out var endField);
                csv.TryGetField<string?>("Notes", out var notesField);
                csv.TryGetField<string?>("Billable", out var billableField);

                // fallback to index-based access when header lookup yields null/empty
                if (string.IsNullOrWhiteSpace(nameField) && idxName >= 0)
                    nameField = csv.GetField(idxName);
                else if (string.IsNullOrWhiteSpace(nameField) && header.Length > 0)
                    nameField = csv.GetField(0);

                if (string.IsNullOrWhiteSpace(startField) && idxStart >= 0)
                    startField = csv.GetField(idxStart);
                else if (string.IsNullOrWhiteSpace(startField) && header.Length > 1)
                    startField = csv.GetField(1);

                if (string.IsNullOrWhiteSpace(endField) && idxEnd >= 0)
                    endField = csv.GetField(idxEnd);
                else if (string.IsNullOrWhiteSpace(endField) && header.Length > 2)
                    endField = csv.GetField(2);

                if (string.IsNullOrWhiteSpace(notesField) && idxNotes >= 0)
                    notesField = csv.GetField(idxNotes);
                else if (string.IsNullOrWhiteSpace(notesField) && header.Length > 4)
                    notesField = csv.GetField(4);

                if (string.IsNullOrWhiteSpace(billableField) && idxBillable >= 0)
                    billableField = csv.GetField(idxBillable);
                else if (string.IsNullOrWhiteSpace(billableField) && header.Length > 5)
                    billableField = csv.GetField(5);

                foreach (var e in BuildEntriesFromName(nameField ?? string.Empty, startField ?? string.Empty, endField ?? string.Empty, notesField ?? string.Empty, billableField ?? string.Empty))
                    yield return e;
            }

            yield break;
        }

        // Fallback: legacy semicolon format or naive comma-split lines
        var all = File.ReadAllLines(path);
        foreach (var raw in all)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.TrimStart().StartsWith("#")) continue;
            if (raw.TrimStart().ToLower().StartsWith("tags")) continue;

            var parts = raw.Split(';');
            if (parts.Length < 4)
            {
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
                // skip malformed
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
        var tags = string.IsNullOrWhiteSpace(nameField)
            ? Array.Empty<string>()
            : nameField.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();

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

    static DateTime? ParseDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)) return dt;
        if (DateTime.TryParse(s, out dt)) return dt;
        if (DateTime.TryParseExact(s, "yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;
        if (DateTime.TryParseExact(s, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;
        return null;
    }
}
