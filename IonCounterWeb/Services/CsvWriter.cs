namespace IonCounterWeb.Services;

internal static class CsvWriter
{
    public static byte[] WriteToBytes(List<CentroidEntry> entries,
                                      string[] originalHeaders,
                                      IReadOnlyList<string> dmtFileNames)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

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

            var lookup = entry.Counts.ToDictionary(c => c.FileName, c => c.Count);
            foreach (var name in dmtFileNames)
                cells.Add(lookup.TryGetValue(name, out long cnt) ? cnt.ToString() : "0");

            writer.WriteLine(string.Join(",", cells));
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
