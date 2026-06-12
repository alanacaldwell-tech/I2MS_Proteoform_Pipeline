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
        double matchWindow = 2.0,
        double ionCountingWindow = 5.0,
        string? dmtFolder = null,
        List<string>? contaminantIds = null)
    {
        // ── Parse input CSV ───────────────────────────────────────────────
        var rows = ReadInputCsv(inputCsvPath);
        if (rows.Count == 0)
        {
            Console.WriteLine("No protein rows found in the input CSV.");
            return;
        }

        Console.WriteLine($"Found {rows.Count} protein(s) to process.");

        // ── Pre-build contaminant database (shared across all proteins) ───
        // Each contaminant's entries are labelled "[AccessionID] ..." so they
        // are distinguishable from the target protein in every output CSV.
        List<ProteoformEntry> contaminantEntries = new();
        if (contaminantIds is { Count: > 0 })
        {
            Console.WriteLine();
            Console.WriteLine($"Building contaminant database ({contaminantIds.Count} protein(s))...");
            var cUniProt = new UniProtClient(http);
            var cPride   = new PrideClient(http);
            var cPtmEx   = new PtmExchangeClient(http);
            foreach (var cId in contaminantIds)
            {
                Console.Write($"  {cId} ... ");
                var (cSeq, cUPtms) = await cUniProt.FetchAsync(cId);
                if (string.IsNullOrEmpty(cSeq)) { Console.WriteLine("not found — skipping."); continue; }
                var cPtms = cUPtms
                    .Concat(await cPride.FetchAsync(cId, cSeq))
                    .Concat(await cPtmEx.FetchAsync(cId, cSeq))
                    .ToList();
                var cEntries = ProteoformBuilder.Build(cSeq, cPtms, includeTruncations, tolerance: 5.0,
                                                       proteinLabel: cId);
                contaminantEntries.AddRange(cEntries);
                Console.WriteLine($"done ({cEntries.Count} entries).");
            }
        }

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

                // Build database and run spectrum analysis
                var proteoforms = ProteoformBuilder.Build(sequence, allPtms, includeTruncations, tolerance: 5.0,
                                                          proteinLabel: uniprotId);
                var combinedDb  = MergeWithContaminants(proteoforms, contaminantEntries);
                var (results, fileNames) = AnalyzeIfProvided(combinedDb, dmtFolder, matchWindow, ionCountingWindow);
                string outPath = ResolveOutputPath(proteinInput, customOutputPath, inputDir);
                ExportSafe(combinedDb, results, fileNames, outPath);
                success++;
            }
            else if (AminoAcidData.IsValidSequence(proteinInput))
            {
                sequence = proteinInput.ToUpper();
                Console.WriteLine($"  Treating as raw sequence ({sequence.Length} aa). No database query.");

                var proteoforms = ProteoformBuilder.Build(sequence, new List<PtmAnnotation>(), includeTruncations,
                                                          tolerance: 5.0, proteinLabel: $"sequence_{i + 1}");
                var combinedDb  = MergeWithContaminants(proteoforms, contaminantEntries);
                var (results, fileNames) = AnalyzeIfProvided(combinedDb, dmtFolder, matchWindow, ionCountingWindow);
                string outPath = ResolveOutputPath($"sequence_{i + 1}", customOutputPath, inputDir);
                ExportSafe(combinedDb, results, fileNames, outPath);
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

    // ── Database merging ──────────────────────────────────────────────────

    private static List<ProteoformEntry> MergeWithContaminants(
        List<ProteoformEntry> targetEntries,
        List<ProteoformEntry> contaminantEntries)
    {
        if (contaminantEntries.Count == 0) return targetEntries;
        var combined = new List<ProteoformEntry>(targetEntries);
        combined.AddRange(contaminantEntries);
        return combined;
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

    private static (List<AnalysisResult> results, List<string> fileNames)
        AnalyzeIfProvided(
            List<ProteoformEntry> proteoforms,
            string? dmtFolder,
            double matchWindow,
            double ionCountingWindow)
    {
        var emptyFileNames = new List<string>();
        if (string.IsNullOrEmpty(dmtFolder) || !Directory.Exists(dmtFolder))
            return (new List<AnalysisResult>(), emptyFileNames);

        var dmtFiles = Directory.GetFiles(dmtFolder, "*.dmt", SearchOption.TopDirectoryOnly)
                                .OrderBy(f => f).ToArray();
        if (dmtFiles.Length == 0)
            return (new List<AnalysisResult>(), emptyFileNames);

        var fileNames = dmtFiles.Select(Path.GetFileName).ToList()!;

        // Key includes rounded experimental centroid so multiple peaks matching the
        // same database entry are listed as separate rows.
        var resultMap = new Dictionary<(string, double, long), AnalysisResult>();

        foreach (var (filePath, fileName) in dmtFiles.Zip(fileNames))
        {
            try
            {
                var matches = SpectrumAnalyzer.ProcessFile(filePath, proteoforms, matchWindow, ionCountingWindow);
                foreach (var (entry, centroid, count) in matches)
                {
                    long roundedCentroid = (long)Math.Round(centroid);
                    var key = (entry.ModificationName, entry.CentroidMass, roundedCentroid);
                    if (!resultMap.TryGetValue(key, out var ar))
                    {
                        ar = new AnalysisResult { DatabaseEntry = entry, ExperimentalCentroid = centroid };
                        resultMap[key] = ar;
                    }
                    ar.IonCountsPerFile[fileName] = count;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: could not process {fileName} — {ex.Message}");
            }
        }

        var results = resultMap.Values
            .OrderBy(r => r.DatabaseEntry.ModificationName)
            .ThenBy(r => r.ExperimentalCentroid)
            .ToList();
        foreach (var ar in results)
            foreach (var fn in fileNames) ar.IonCountsPerFile.TryAdd(fn, 0);

        Console.WriteLine($"  {results.Count} hit(s) matched across {fileNames.Count} file(s).");
        return (results, fileNames);
    }

    private static void ExportSafe(
        List<ProteoformEntry> proteoforms,
        List<AnalysisResult> results,
        List<string> fileNames,
        string outputPath)
    {
        try
        {
            if (results.Count > 0)
                CsvExporter.ExportResults(results, fileNames, outputPath);
            else
                CsvExporter.ExportDatabase(proteoforms, outputPath);
            Console.WriteLine($"  Saved → {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Could not write output: {ex.Message}");
        }
    }
}
