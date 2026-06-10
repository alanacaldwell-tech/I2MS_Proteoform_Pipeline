namespace ProteoformAnalyzer;

/// <summary>
/// Batch CSV input mode.
///
/// Reads an input CSV where each row specifies one protein to analyse.
/// Accepted column layouts (auto-detected by header):
///
///   Layout A — two columns:
///     UniProt ID or Sequence  |  Output Path (optional)
///
///   Layout B — one column:
///     UniProt ID or Sequence
///
/// Header row is optional; if the first cell of row 1 parses as a known
/// UniProt accession or valid amino-acid sequence it is treated as data.
///
/// For each protein the full pipeline runs (UniProt + PRIDE + PTMeXchange
/// queries if an accession is given, truncations, custom mods are skipped
/// in batch mode) and a proteoform CSV is written next to the input file
/// unless an explicit output path is supplied in the input CSV.
/// </summary>
public static class CsvBatchMode
{
    public static async Task RunAsync(
        string inputCsvPath,
        HttpClient http,
        bool includeTruncations,
        double tolerance)
    {
        // ── Parse input CSV ───────────────────────────────────────────────
        var rows = ReadInputCsv(inputCsvPath);
        if (rows.Count == 0)
        {
            Console.WriteLine("No protein rows found in the input CSV.");
            return;
        }

        Console.WriteLine($"Found {rows.Count} protein(s) to process.");
        Console.WriteLine();

        string inputDir = Path.GetDirectoryName(Path.GetFullPath(inputCsvPath))
                          ?? Directory.GetCurrentDirectory();

        int success = 0, failed = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            var (proteinInput, customOutputPath) = rows[i];
            Console.WriteLine($"── [{i + 1}/{rows.Count}] {proteinInput} ──────────────────────────");

            // ── Resolve sequence ──────────────────────────────────────────
            string sequence = "";
            string? uniprotId = null;

            if (IsUniProtAccession(proteinInput))
            {
                uniprotId = proteinInput.ToUpper();
                var uniprotClient = new UniProtClient(http);
                List<PtmAnnotation> uniprotPtms;
                (sequence, uniprotPtms) = await uniprotClient.FetchAsync(uniprotId);

                if (string.IsNullOrEmpty(sequence))
                {
                    Console.WriteLine($"  Could not retrieve sequence — skipping.");
                    failed++;
                    continue;
                }

                // Collect PTMs from all three sources
                var allPtms = new List<PtmAnnotation>(uniprotPtms);

                var prideClient = new PrideClient(http);
                allPtms.AddRange(await prideClient.FetchAsync(uniprotId, sequence));

                var ptmExClient = new PtmExchangeClient(http);
                allPtms.AddRange(await ptmExClient.FetchAsync(uniprotId, sequence));

                // Build and export
                var proteoforms = ProteoformBuilder.Build(sequence, allPtms, includeTruncations, tolerance);
                string outPath = ResolveOutputPath(proteinInput, customOutputPath, inputDir);
                ExportSafe(proteoforms, outPath);
                success++;
            }
            else if (AminoAcidData.IsValidSequence(proteinInput))
            {
                sequence = proteinInput.ToUpper();
                Console.WriteLine($"  Treating as raw sequence ({sequence.Length} aa). No database query.");

                var proteoforms = ProteoformBuilder.Build(sequence, new List<PtmAnnotation>(), includeTruncations, tolerance);
                string outPath = ResolveOutputPath($"sequence_{i + 1}", customOutputPath, inputDir);
                ExportSafe(proteoforms, outPath);
                success++;
            }
            else
            {
                Console.WriteLine($"  Not a valid UniProt accession or amino-acid sequence — skipping.");
                failed++;
            }

            Console.WriteLine();
        }

        Console.WriteLine($"Batch complete: {success} succeeded, {failed} skipped.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static List<(string ProteinInput, string? OutputPath)> ReadInputCsv(string path)
    {
        var result = new List<(string, string?)>();
        var lines = File.ReadAllLines(path);

        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var cols = SplitCsvLine(line);
            string first = cols[0].Trim().Trim('"');
            string? second = cols.Length > 1 && !string.IsNullOrWhiteSpace(cols[1])
                ? cols[1].Trim().Trim('"') : null;

            // Skip header row if the first cell is clearly a column label
            if (first.Equals("UniProt ID", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Sequence", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Protein", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Accession", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(first))
                result.Add((first, second));
        }

        return result;
    }

    private static string[] SplitCsvLine(string line)
    {
        // Handles quoted fields containing commas
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); continue; }
            current.Append(c);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static bool IsUniProtAccession(string s) =>
        System.Text.RegularExpressions.Regex.IsMatch(s,
            @"^[OPQ][0-9][A-Z0-9]{3}[0-9]$|^[A-NR-Z][0-9]([A-Z][A-Z0-9]{2}[0-9]){1,2}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string ResolveOutputPath(string proteinId, string? customPath, string inputDir)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            if (!Path.GetFileName(customPath).Contains('.'))
                customPath += ".csv";
            return customPath;
        }

        string safe = string.Concat(proteinId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(inputDir, $"{safe}_proteoforms.csv");
    }

    private static void ExportSafe(List<ProteoformEntry> proteoforms, string outputPath)
    {
        try
        {
            CsvExporter.Export(proteoforms, outputPath);
            Console.WriteLine($"  Saved {proteoforms.Count} proteoforms → {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Could not write output: {ex.Message}");
        }
    }
}
