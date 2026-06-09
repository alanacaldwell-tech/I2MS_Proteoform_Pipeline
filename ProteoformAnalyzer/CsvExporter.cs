namespace ProteoformAnalyzer;

public static class CsvExporter
{
    public static void Export(List<Proteoform> proteoforms, string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("Proteoform Modification,Mass (Da),Tolerance Range");

        foreach (var pf in proteoforms)
        {
            string desc = EscapeCsv(pf.Description);
            writer.WriteLine($"{desc},{pf.MassFormatted},{pf.ToleranceFormatted}");
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
