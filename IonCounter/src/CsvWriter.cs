namespace IonCounter;

internal static class CsvWriter
{
    /// <summary>
    /// Writes the results CSV.
    /// Layout:
    ///   Col 0 : Centroid Mass
    ///   Col 1 : Tolerance (Da)
    ///   Col 2+ : one column per .dmt file, header = file name, value = ion count
    /// </summary>
    public static void Write(string outputPath,
                             List<CentroidEntry> entries,
                             string[] originalHeaders,
                             IReadOnlyList<string> dmtFileNames)
    {
        using var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8);

        // Header row: keep original first two header cells, then append file names.
        var headerCells = new List<string>(originalHeaders.Take(2));
        foreach (var name in dmtFileNames)
            headerCells.Add(EscapeCsv(name));

        writer.WriteLine(string.Join(",", headerCells));

        // Data rows.
        foreach (var entry in entries)
        {
            var cells = new List<string>
            {
                entry.CentroidMass.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                entry.Tolerance.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
            };

            // Emit counts in the same order as dmtFileNames.
            var countLookup = entry.Counts.ToDictionary(c => c.FileName, c => c.Count);
            foreach (var name in dmtFileNames)
                cells.Add(countLookup.TryGetValue(name, out long cnt) ? cnt.ToString() : "0");

            writer.WriteLine(string.Join(",", cells));
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
