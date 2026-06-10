namespace ProteoformAnalyzer;

public static class CsvExporter
{
    /// <summary>
    /// Exports the proteoform database without experimental data (no .dmt files processed).
    /// Columns: Modification Name, Predicted Centroid Mass (Da), Tolerance Range, Envelope Sigma (Da)
    /// </summary>
    public static void ExportDatabase(List<ProteoformEntry> entries, string path)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("Modification Name,Predicted Centroid Mass (Da),Tolerance Range,Envelope Sigma (Da)");

        foreach (var e in entries)
        {
            w.WriteLine($"{Csv(e.ModificationName)},{e.CentroidMass:F4}," +
                        $"+/- {e.Tolerance:F1} Da," +
                        $"{(e.Envelope is not null ? e.Envelope.Sigma.ToString("F3") : "")}");
        }
    }

    /// <summary>
    /// Exports matched proteoforms with experimental centroids and per-file ion counts.
    /// Columns: Modification Name | Predicted Mass (Da) | Experimental Centroid (Da) | [file1] | [file2] | ...
    /// Only proteoforms matched in at least one file are included.
    /// </summary>
    public static void ExportResults(
        List<AnalysisResult> results,
        IReadOnlyList<string> fileNames,
        string path)
    {
        using var w = new StreamWriter(path);

        // Header
        var header = "Modification Name,Predicted Centroid Mass (Da),Experimental Centroid (Da)";
        foreach (var fn in fileNames)
            header += $",{Csv(fn)} Ion Count";
        w.WriteLine(header);

        foreach (var r in results)
        {
            string name  = Csv(r.DatabaseEntry.ModificationName);
            string pred  = r.DatabaseEntry.CentroidMass.ToString("F4");
            string expt  = r.MeanExperimentalCentroid.ToString("F4");
            string row   = $"{name},{pred},{expt}";

            foreach (var fn in fileNames)
            {
                long cnt = r.IonCountsPerFile.TryGetValue(fn, out long c) ? c : 0;
                row += $",{cnt}";
            }

            w.WriteLine(row);
        }
    }

    private static string Csv(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
}
