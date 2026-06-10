namespace ProteoformAnalyzer;

public static class CsvExporter
{
    public static void Export(List<ProteoformEntry> entries, string path,
                              IReadOnlyList<string>? dmtFileNames = null)
    {
        using var w = new StreamWriter(path);

        // Header
        var header = "Sequence Position,Modification Name,Proteoform Centroid Mass (Da),Tolerance Range,Envelope Sigma (Da)";
        if (dmtFileNames is { Count: > 0 })
            header += "," + string.Join(",", dmtFileNames.Select(Csv));
        w.WriteLine(header);

        foreach (var e in entries)
        {
            string pos  = Csv(e.SequencePosition);
            string name = Csv(e.ModificationName);
            string mass = $"{e.CentroidMass:F4}";
            string tol  = $"+/- {e.Tolerance:F1} Da";
            string sig  = e.Envelope is not null ? $"{e.Envelope.Sigma:F3}" : "";

            string row = $"{pos},{name},{mass},{tol},{sig}";

            if (dmtFileNames is { Count: > 0 } && e.IonCounts is not null)
            {
                foreach (string fn in dmtFileNames)
                {
                    long count = e.IonCounts.TryGetValue(fn, out long c) ? c : 0;
                    row += $",{count}";
                }
            }

            w.WriteLine(row);
        }
    }

    private static string Csv(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
}
