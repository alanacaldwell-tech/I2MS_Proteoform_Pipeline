namespace ProteoformAnalyzer;

/// <summary>
/// Shared .dmt folder analysis used by both interactive (Program.cs) and batch
/// (CsvBatchMode) modes. Discovers .dmt files, runs <see cref="SpectrumAnalyzer.ProcessFile"/>
/// on each, aggregates per-file ion counts and charge-state support per
/// (proteoform, experimental peak), and zero-fills files where a hit was not detected.
/// </summary>
public static class SpectrumBatch
{
    /// <summary>
    /// Analyses every .dmt file in <paramref name="dmtFolder"/> against the database.
    /// Returns the merged hit rows and the ordered list of file names (one ion-count
    /// column per file). Returns empty lists when no folder/files are available.
    /// </summary>
    public static (List<AnalysisResult> Results, List<string> FileNames) AnalyzeFolder(
        string? dmtFolder,
        List<ProteoformEntry> database,
        double matchWindow,
        double ionCountingWindow,
        long minIonCount)
    {
        if (string.IsNullOrEmpty(dmtFolder) || !Directory.Exists(dmtFolder))
            return (new List<AnalysisResult>(), new List<string>());

        var dmtFiles = Directory.GetFiles(dmtFolder, "*.dmt", SearchOption.TopDirectoryOnly)
                                .OrderBy(f => f).ToArray();
        if (dmtFiles.Length == 0)
            return (new List<AnalysisResult>(), new List<string>());

        var fileNames = dmtFiles.Select(Path.GetFileName).ToList()!;

        // Key on (modification, predicted mass, rounded experimental centroid) so that
        // multiple distinct peaks matching the same entry stay separate rows, while the
        // same peak detected across several files merges into one row.
        var resultMap = new Dictionary<(string, double, long), AnalysisResult>();

        for (int f = 0; f < dmtFiles.Length; f++)
        {
            string fileName = fileNames[f];
            Console.Write($"  [{f + 1}/{dmtFiles.Length}] {fileName} — analysing ... ");
            try
            {
                var matches = SpectrumAnalyzer.ProcessFile(
                    dmtFiles[f], database, matchWindow, ionCountingWindow, minIonCount);
                long totalIons = matches.Sum(m => m.IonCount);
                Console.WriteLine($"{matches.Count} match(es), {totalIons:N0} ions");

                foreach (var m in matches)
                {
                    long roundedCentroid = (long)Math.Round(m.ExperimentalCentroid);
                    var key = (m.Entry.ModificationName, m.Entry.CentroidMass, roundedCentroid);
                    if (!resultMap.TryGetValue(key, out var ar))
                    {
                        ar = new AnalysisResult
                        {
                            DatabaseEntry        = m.Entry,
                            ExperimentalCentroid = m.ExperimentalCentroid,
                            MassErrorDa          = m.MassErrorDa,
                            RankWithinPeak       = m.RankWithinPeak
                        };
                        resultMap[key] = ar;
                    }
                    ar.IonCountsPerFile[fileName] = m.IonCount;
                    ar.ChargeStatesObserved.UnionWith(m.ChargeStates);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR — {ex.Message}");
            }
        }

        var results = resultMap.Values
            .OrderBy(r => r.DatabaseEntry.ModificationName)
            .ThenBy(r => r.ExperimentalCentroid)
            .ToList();

        // Fill zeros for files where a hit was not detected so every row has a value per file.
        foreach (var ar in results)
            foreach (var fn in fileNames)
                ar.IonCountsPerFile.TryAdd(fn, 0);

        Console.WriteLine($"  {results.Count} hit(s) matched across {fileNames.Count} file(s).");
        return (results, fileNames);
    }
}
