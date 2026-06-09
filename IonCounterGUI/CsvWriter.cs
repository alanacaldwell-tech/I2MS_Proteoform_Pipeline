namespace IonCounterGUI;

internal static class CsvWriter
{
    public static void Write(string outputPath,
                             List<CentroidEntry> entries,
                             string[] originalHeaders,
                             IReadOnlyList<string> dmtFileNames)
    {
        using var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8);

        var headerCells = new List<string>(originalHeaders.Take(2));
        foreach (var name in dmtFileNames)
            headerCells.Add(EscapeCsv(name));
        writer.WriteLine(string.Join(",", headerCells));

        foreach (var entry in entries)
        {
            var cells = new List<string>
            {
                entry.CentroidMass.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                entry.Tolerance.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
            };

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
