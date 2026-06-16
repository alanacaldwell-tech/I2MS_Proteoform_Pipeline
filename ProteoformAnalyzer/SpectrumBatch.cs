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

        // Key on the database proteoform identity (protein, modification, predicted mass) only.
        // Slightly different experimental masses that track to the same proteoform — whether from
        // different peaks or different files — therefore merge onto one row; the experimental masses
        // are preserved per file (ExperimentalMassPerFile) and surfaced as trailing CSV columns.
        var resultMap = new Dictionary<(string, string, double), AnalysisResult>();

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
                    var key = (m.Entry.ProteinLabel, m.Entry.ModificationName, m.Entry.CentroidMass);
                    if (!resultMap.TryGetValue(key, out var ar))
                    {
                        ar = new AnalysisResult
                        {
                            DatabaseEntry  = m.Entry,
                            RankWithinPeak = m.RankWithinPeak
                        };
                        resultMap[key] = ar;
                    }

                    // Ion count is computed from the entry's own centroid window, so it is identical
                    // for every peak matching this entry in this file — assign (don't accumulate).
                    ar.IonCountsPerFile[fileName] = m.IonCount;

                    // When several peaks in one file match this entry, keep the experimental mass of
                    // the one closest to the predicted mass (smallest |mass error|).
                    if (!ar.ExperimentalMassPerFile.TryGetValue(fileName, out double existing) ||
                        Math.Abs(m.Entry.CentroidMass - m.ExperimentalCentroid) <
                        Math.Abs(m.Entry.CentroidMass - existing))
                        ar.ExperimentalMassPerFile[fileName] = m.ExperimentalCentroid;

                    ar.ChargeStatesObserved.UnionWith(m.ChargeStates);
                    ar.RankWithinPeak = Math.Min(ar.RankWithinPeak, m.RankWithinPeak);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR — {ex.Message}");
            }
        }

        // Collapse the per-file experimental masses into one representative value (ion-count-weighted
        // mean) and derive the mass error from it.
        foreach (var ar in resultMap.Values)
        {
            double weightedSum = 0, weight = 0, plainSum = 0;
            int n = 0;
            foreach (var (file, mass) in ar.ExperimentalMassPerFile)
            {
                long ions = ar.IonCountsPerFile.TryGetValue(file, out long c) ? c : 0;
                weightedSum += mass * ions;
                weight      += ions;
                plainSum    += mass;
                n++;
            }
            ar.ExperimentalCentroid = weight > 0 ? weightedSum / weight
                                    : n > 0      ? plainSum / n
                                    : 0;
            ar.MassErrorDa = ar.DatabaseEntry.CentroidMass - ar.ExperimentalCentroid;
        }

        var results = resultMap.Values
            .OrderBy(r => r.DatabaseEntry.ModificationName)
            .ThenBy(r => r.ExperimentalCentroid)
            .ToList();

        // Fill zeros for files where a hit was not detected so every row has a value per file.
        // (Experimental-mass columns are left blank for undetected files, not zero-filled.)
        foreach (var ar in results)
            foreach (var fn in fileNames)
                ar.IonCountsPerFile.TryAdd(fn, 0);

        Console.WriteLine($"  {results.Count} proteoform(s) matched across {fileNames.Count} file(s).");
        return (results, fileNames);
    }
}
