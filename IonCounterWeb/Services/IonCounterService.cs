namespace IonCounterWeb.Services;

public sealed class ProcessResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public byte[]? OutputCsvBytes { get; init; }
    public string OutputFileName { get; init; } = "ion_counts.csv";
    public List<string> Log { get; init; } = new();
    public List<(double Centroid, double Tolerance, long TotalCount)> Summary { get; init; } = new();
}

public sealed class IonCounterService
{
    public async Task<ProcessResult> RunAsync(
        string csvPath,
        string dmtFolder,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken ct = default)
    {
        var log = new List<string>();
        void Log(string msg) { log.Add(msg); }

        // ── Validate inputs ──────────────────────────────────────────────────
        if (!File.Exists(csvPath))
            return Fail($"CSV file not found: {csvPath}", log);

        if (!Directory.Exists(dmtFolder))
            return Fail($"Folder not found: {dmtFolder}", log);

        // ── Parse CSV ────────────────────────────────────────────────────────
        List<CentroidEntry> entries;
        string[] originalHeaders;
        try
        {
            using var stream = File.OpenRead(csvPath);
            (entries, originalHeaders) = CsvReader.Parse(stream);
        }
        catch (Exception ex)
        {
            return Fail($"Error reading CSV: {ex.Message}", log);
        }

        Log($"Loaded {entries.Count} centroid(s) from reference CSV.");
        foreach (var e in entries)
            Log($"  {e.CentroidMass:G} ± {e.Tolerance:G} Da");

        // ── Find .dmt files ──────────────────────────────────────────────────
        var dmtFiles = Directory.GetFiles(dmtFolder, "*.dmt", SearchOption.TopDirectoryOnly)
                                .OrderBy(f => f)
                                .ToArray();

        if (dmtFiles.Length == 0)
            return Fail("No .dmt files found in the specified folder.", log);

        Log($"\nFound {dmtFiles.Length} .dmt file(s).\n");

        // ── Count ions ───────────────────────────────────────────────────────
        for (int f = 0; f < dmtFiles.Length; f++)
        {
            ct.ThrowIfCancellationRequested();
            string filePath = dmtFiles[f];
            string fileName = Path.GetFileName(filePath);

            progress?.Report((f, dmtFiles.Length, $"Processing {fileName}…"));
            Log($"[{f + 1}/{dmtFiles.Length}]  {fileName}…");

            var counts = new long[entries.Count];
            try
            {
                await Task.Run(() =>
                {
                    foreach (double mass in DmtParser.ReadMasses(filePath))
                    {
                        ct.ThrowIfCancellationRequested();
                        for (int e = 0; e < entries.Count; e++)
                            if (entries[e].Contains(mass))
                                counts[e]++;
                    }
                }, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log($"  ERROR: {ex.Message}");
                for (int e = 0; e < entries.Count; e++)
                    entries[e].Counts.Add((fileName, 0));
                continue;
            }

            long total = 0;
            for (int e = 0; e < entries.Count; e++)
            {
                entries[e].Counts.Add((fileName, counts[e]));
                total += counts[e];
            }

            Log($"  → {total:N0} matching ion(s)");
        }

        progress?.Report((dmtFiles.Length, dmtFiles.Length, "Writing output…"));

        // ── Build output CSV ─────────────────────────────────────────────────
        var dmtFileNames = dmtFiles.Select(Path.GetFileName).ToList()!;
        byte[] outputBytes;
        try
        {
            outputBytes = CsvWriter.WriteToBytes(entries, originalHeaders, dmtFileNames);
        }
        catch (Exception ex)
        {
            return Fail($"Error writing output: {ex.Message}", log);
        }

        string baseName   = Path.GetFileNameWithoutExtension(csvPath);
        string outputName = baseName + "_ion_counts.csv";
        Log($"\nDone. Output: {outputName}");

        var summary = entries
            .Select(e => (e.CentroidMass, e.Tolerance, e.Counts.Sum(c => c.Count)))
            .ToList();

        return new ProcessResult
        {
            Success        = true,
            OutputCsvBytes = outputBytes,
            OutputFileName = outputName,
            Log            = log,
            Summary        = summary
        };
    }

    private static ProcessResult Fail(string message, List<string> log) =>
        new() { Success = false, ErrorMessage = message, Log = log };
}
