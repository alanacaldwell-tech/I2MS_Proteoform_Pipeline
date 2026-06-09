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
    // Max bytes per uploaded file allowed through the Blazor InputFile component.
    public const long MaxFileSizeBytes = 2L * 1024 * 1024 * 1024; // 2 GB

    public async Task<ProcessResult> RunAsync(
        Stream csvStream,
        string csvFileName,
        IReadOnlyList<(Stream Data, string Name)> dmtFiles,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken ct = default)
    {
        var log = new List<string>();

        void Log(string msg) { log.Add(msg); }

        // ── Parse CSV ────────────────────────────────────────────────────────
        // Buffer the upload stream into memory first — Blazor upload streams
        // only support async reads, but CsvReader uses synchronous StreamReader.
        var csvBuffer = new MemoryStream();
        await csvStream.CopyToAsync(csvBuffer, ct);
        csvBuffer.Position = 0;

        List<CentroidEntry> entries;
        string[] originalHeaders;
        try
        {
            (entries, originalHeaders) = CsvReader.Parse(csvBuffer);
        }
        catch (Exception ex)
        {
            return Fail($"Error reading CSV: {ex.Message}", log);
        }

        Log($"Loaded {entries.Count} centroid(s) from reference CSV.");
        foreach (var e in entries)
            Log($"  {e.CentroidMass:G} ± {e.Tolerance:G} Da");

        if (dmtFiles.Count == 0)
            return Fail("No .dmt files were uploaded.", log);

        Log($"\nProcessing {dmtFiles.Count} .dmt file(s)…\n");

        var dmtFileNames = dmtFiles.Select(f => f.Name).ToList();
        var tempFiles    = new List<string>();

        try
        {
            // ── Write dmt streams to temp files (SQLite needs a real path) ──
            for (int i = 0; i < dmtFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                string tmp = Path.GetTempFileName();
                tempFiles.Add(tmp);

                await using var fs = File.OpenWrite(tmp);
                await dmtFiles[i].Data.CopyToAsync(fs, ct);
            }

            // ── Count ions ──────────────────────────────────────────────────
            for (int f = 0; f < tempFiles.Count; f++)
            {
                ct.ThrowIfCancellationRequested();
                string fileName = dmtFileNames[f];

                progress?.Report((f, dmtFiles.Count, $"Processing {fileName}…"));
                Log($"[{f + 1}/{dmtFiles.Count}]  {fileName}…");

                var counts = new long[entries.Count];
                try
                {
                    foreach (double mass in DmtParser.ReadMasses(tempFiles[f]))
                    {
                        ct.ThrowIfCancellationRequested();
                        for (int e = 0; e < entries.Count; e++)
                            if (entries[e].Contains(mass))
                                counts[e]++;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log($"  ERROR reading {fileName}: {ex.Message}");
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
        }
        finally
        {
            foreach (var tmp in tempFiles)
                try { File.Delete(tmp); } catch { /* best effort */ }
        }

        progress?.Report((dmtFiles.Count, dmtFiles.Count, "Writing output…"));

        // ── Build output CSV ─────────────────────────────────────────────────
        byte[] outputBytes;
        try
        {
            outputBytes = CsvWriter.WriteToBytes(entries, originalHeaders, dmtFileNames);
        }
        catch (Exception ex)
        {
            return Fail($"Error writing output CSV: {ex.Message}", log);
        }

        string baseName    = Path.GetFileNameWithoutExtension(csvFileName);
        string outputName  = baseName + "_ion_counts.csv";

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
