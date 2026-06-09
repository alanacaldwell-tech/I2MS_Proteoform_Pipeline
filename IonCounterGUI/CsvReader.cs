namespace IonCounterGUI;

internal static class CsvReader
{
    public static (List<CentroidEntry> entries, string[] originalHeaders) Parse(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            throw new InvalidDataException("Reference CSV must have at least one header row and one data row.");

        var originalHeaders = SplitLine(lines[0]);
        var entries = new List<CentroidEntry>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var cols = SplitLine(line);
            if (cols.Length < 2)
                throw new InvalidDataException($"Line {i + 1} has fewer than 2 columns: '{line}'");

            if (!double.TryParse(cols[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double centroid))
                throw new InvalidDataException($"Cannot parse centroid mass on line {i + 1}: '{cols[0]}'");

            if (!double.TryParse(cols[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double tolerance))
                throw new InvalidDataException($"Cannot parse tolerance on line {i + 1}: '{cols[1]}'");

            entries.Add(new CentroidEntry(centroid, tolerance));
        }

        return (entries, originalHeaders);
    }

    private static string[] SplitLine(string line) =>
        line.Split(',').Select(c => c.Trim()).ToArray();
}
