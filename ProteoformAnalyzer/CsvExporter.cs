namespace ProteoformAnalyzer;

public static class CsvExporter
{
    public static void Export(List<ProteoformEntry> entries, string path)
    {
        using var w = new StreamWriter(path);

        // Headers
        w.WriteLine("Sequence Position,Modification Name,Proteoform Centroid Mass (Da),Tolerance Range,Source,Envelope Sigma (Da),Envelope FWHM (Da)");

        foreach (var e in entries)
        {
            string pos  = Csv(e.SequencePosition);
            string name = Csv(e.ModificationName);
            string mass = $"{e.CentroidMass:F4}";
            string tol  = $"+/- {e.Tolerance:F1} Da";
            string src  = Csv(e.Source);
            string sig  = e.Envelope is not null ? $"{e.Envelope.Sigma:F3}" : "";
            string fwhm = e.Envelope is not null ? $"{e.Envelope.Fwhm:F3}" : "";

            w.WriteLine($"{pos},{name},{mass},{tol},{src},{sig},{fwhm}");
        }
    }

    private static string Csv(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
}
