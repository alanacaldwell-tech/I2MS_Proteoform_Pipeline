namespace ProteoformAnalyzer;

/// <summary>
/// Counts ions from a set of .dmt files against a list of proteoforms.
/// Each proteoform has a centroid mass and tolerance window; an ion is
/// counted for a proteoform if its neutral mass falls within that window.
/// </summary>
public static class DmtIonCounter
{
    /// <summary>
    /// Processes every .dmt file in <paramref name="dmtFolder"/> and records
    /// per-file ion counts on each <see cref="ProteoformEntry"/>.
    /// </summary>
    public static List<string> CountIons(
        List<ProteoformEntry> proteoforms,
        string dmtFolder)
    {
        var dmtFiles = Directory.GetFiles(dmtFolder, "*.dmt", SearchOption.TopDirectoryOnly)
                                .OrderBy(f => f)
                                .ToArray();

        if (dmtFiles.Length == 0)
        {
            Console.WriteLine("  No .dmt files found in that folder.");
            return new List<string>();
        }

        Console.WriteLine($"  Found {dmtFiles.Length} .dmt file(s). Counting ions...");
        Console.WriteLine();

        var fileNames = dmtFiles.Select(Path.GetFileName).ToList()!;

        // Initialise count storage on every proteoform entry
        foreach (var pf in proteoforms)
            pf.IonCounts = new Dictionary<string, long>(fileNames.Count);

        for (int f = 0; f < dmtFiles.Length; f++)
        {
            string filePath = dmtFiles[f];
            string fileName = fileNames[f];

            Console.Write($"  [{f + 1}/{dmtFiles.Length}] {fileName} ... ");

            var counts = new long[proteoforms.Count];

            try
            {
                foreach (double mass in DmtParser.ReadMasses(filePath))
                {
                    for (int p = 0; p < proteoforms.Count; p++)
                    {
                        var pf = proteoforms[p];
                        double lo = pf.CentroidMass - pf.Tolerance;
                        double hi = pf.CentroidMass + pf.Tolerance;
                        if (mass >= lo && mass <= hi)
                            counts[p]++;
                    }
                }

                long total = 0;
                for (int p = 0; p < proteoforms.Count; p++)
                {
                    proteoforms[p].IonCounts![fileName] = counts[p];
                    total += counts[p];
                }
                Console.WriteLine($"done  ({total:N0} matching ion(s))");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR — {ex.Message}");
                for (int p = 0; p < proteoforms.Count; p++)
                    proteoforms[p].IonCounts![fileName] = 0;
            }
        }

        return fileNames;
    }
}
