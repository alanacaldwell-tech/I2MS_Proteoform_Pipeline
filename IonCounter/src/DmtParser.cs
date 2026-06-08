namespace IonCounter;

/// <summary>
/// Reads a .dmt file and returns all ion masses found in it.
///
/// .dmt files are plain-text, whitespace- or tab-delimited files where one
/// column contains the measured neutral mass (or m/z) of each detected ion.
/// The parser auto-detects the delimiter (tab, comma, or space) and, on first
/// call, asks the user which column (0-indexed) holds the mass values.
/// The choice is cached for the rest of the session so the prompt only appears once.
/// </summary>
internal static class DmtParser
{
    private static int? _massColumnIndex;

    /// <summary>
    /// Reset column selection (used in tests / re-runs).
    /// </summary>
    public static void ResetColumnSelection() => _massColumnIndex = null;

    public static IEnumerable<double> ReadMasses(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        // Skip completely empty files.
        if (lines.Length == 0) yield break;

        // Detect delimiter from the first non-empty line.
        string firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        char delimiter = DetectDelimiter(firstLine);

        // Determine how many columns exist (from first data line).
        int columnCount = firstLine.Split(delimiter).Length;

        // Decide whether the first line is a header.
        bool hasHeader = HasHeader(firstLine, delimiter);

        // If column selection not yet made, prompt the user.
        if (_massColumnIndex == null)
        {
            _massColumnIndex = PromptForMassColumn(filePath, firstLine, delimiter, hasHeader, columnCount);
        }

        int col = _massColumnIndex.Value;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            // Skip header row if present.
            if (hasHeader && rawLine == firstLine) continue;

            var parts = rawLine.Split(delimiter);
            if (parts.Length <= col) continue;

            if (double.TryParse(parts[col].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double mass))
            {
                yield return mass;
            }
        }
    }

    private static char DetectDelimiter(string line)
    {
        if (line.Contains('\t')) return '\t';
        if (line.Contains(',')) return ',';
        return ' ';
    }

    private static bool HasHeader(string firstLine, char delimiter)
    {
        // If any token in the first line cannot be parsed as a number, treat it as a header.
        return firstLine.Split(delimiter)
                        .Any(t => !string.IsNullOrWhiteSpace(t) &&
                                  !double.TryParse(t.Trim(),
                                      System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out _));
    }

    private static int PromptForMassColumn(string filePath, string firstLine,
        char delimiter, bool hasHeader, int columnCount)
    {
        Console.WriteLine();
        Console.WriteLine("──────────────────────────────────────────────────────");
        Console.WriteLine($"First .dmt file: {Path.GetFileName(filePath)}");
        Console.WriteLine($"Delimiter detected: {DescribeDelimiter(delimiter)}");
        Console.WriteLine();
        Console.WriteLine("First line of file:");
        Console.WriteLine($"  {firstLine}");
        Console.WriteLine();

        if (hasHeader)
        {
            var headers = firstLine.Split(delimiter);
            Console.WriteLine("Columns (0-indexed):");
            for (int i = 0; i < headers.Length; i++)
                Console.WriteLine($"  [{i}]  {headers[i].Trim()}");
        }
        else
        {
            Console.WriteLine($"No header row detected. File has {columnCount} column(s).");
        }

        Console.WriteLine();
        Console.Write("Enter the column index that contains the ion mass values: ");

        while (true)
        {
            var input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int idx) && idx >= 0 && idx < columnCount)
                return idx;
            Console.Write($"Please enter a number between 0 and {columnCount - 1}: ");
        }
    }

    private static string DescribeDelimiter(char c) => c switch
    {
        '\t' => "tab",
        ',' => "comma",
        ' ' => "space",
        _ => c.ToString()
    };
}
