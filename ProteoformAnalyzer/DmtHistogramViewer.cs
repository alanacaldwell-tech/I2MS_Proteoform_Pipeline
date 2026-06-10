namespace ProteoformAnalyzer;

/// <summary>
/// Loads a .dmt file, builds a 1-Da binned mass histogram, and renders it
/// as an ASCII bar chart with summary statistics.
/// </summary>
public static class DmtHistogramViewer
{
    private const int BarWidth    = 60;   // max bar length in characters
    private const int MinBinCount = 1;    // suppress bins with zero ions
    private const int MaxBinsDisplayed = 200; // cap to avoid console flood

    public static void Run()
    {
        Console.WriteLine("─── DMT Histogram Viewer ────────────────────────────────");
        Console.WriteLine();

        // ── 1. Get a valid .dmt file path ─────────────────────────────────
        string dmtPath;
        while (true)
        {
            Console.Write("Path to .dmt file: ");
            dmtPath = Console.ReadLine()?.Trim().Trim('"') ?? "";
            if (File.Exists(dmtPath)) break;
            Console.WriteLine($"  File not found: {dmtPath}");
        }

        // ── 2. Load neutral masses ─────────────────────────────────────────
        Console.WriteLine();
        Console.Write("Reading masses … ");
        List<double> masses;
        try
        {
            masses = DmtParser.ReadMasses(dmtPath).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  Error reading file: {ex.Message}");
            return;
        }

        if (masses.Count == 0)
        {
            Console.WriteLine("No ions found in the file.");
            return;
        }
        Console.WriteLine($"{masses.Count:N0} ions loaded.");

        // ── 3. Build 1-Da histogram ────────────────────────────────────────
        var histogram = new Dictionary<int, long>();
        foreach (double m in masses)
        {
            int bin = (int)Math.Floor(m);
            histogram[bin] = histogram.TryGetValue(bin, out long c) ? c + 1 : 1;
        }

        // ── 4. Summary statistics ──────────────────────────────────────────
        int minBin  = histogram.Keys.Min();
        int maxBin  = histogram.Keys.Max();
        long maxCnt = histogram.Values.Max();
        int  peakBin = histogram.MaxBy(kv => kv.Value).Key;
        long totalIons = histogram.Values.Sum();
        int  occupiedBins = histogram.Count;

        Console.WriteLine();
        Console.WriteLine("  Summary");
        Console.WriteLine($"    Mass range   : {minBin} – {maxBin + 1} Da");
        Console.WriteLine($"    Bins occupied: {occupiedBins} of {maxBin - minBin + 1}");
        Console.WriteLine($"    Total ions   : {totalIons:N0}");
        Console.WriteLine($"    Apex bin     : {peakBin} – {peakBin + 1} Da  ({maxCnt:N0} ions)");
        Console.WriteLine();

        // ── 5. Optional mass range filter ─────────────────────────────────
        int displayMin = minBin;
        int displayMax = maxBin;

        Console.Write($"Display mass range [{minBin}–{maxBin + 1} Da] — enter min Da or press Enter to use full range: ");
        string? lo = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(lo) && int.TryParse(lo, out int loVal))
        {
            displayMin = loVal;
            Console.Write($"Enter max Da (current max {maxBin + 1}): ");
            string? hi = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(hi) && int.TryParse(hi, out int hiVal))
                displayMax = hiVal - 1;
        }

        // ── 6. Collect bins to display ─────────────────────────────────────
        var binsToShow = histogram
            .Where(kv => kv.Key >= displayMin && kv.Key <= displayMax && kv.Value >= MinBinCount)
            .OrderBy(kv => kv.Key)
            .ToList();

        if (binsToShow.Count == 0)
        {
            Console.WriteLine("No bins in the selected range.");
            return;
        }

        // Downsample if too many bins for comfortable display
        bool downsampled = false;
        if (binsToShow.Count > MaxBinsDisplayed)
        {
            int step = (int)Math.Ceiling((double)binsToShow.Count / MaxBinsDisplayed);
            var merged = new List<KeyValuePair<int, long>>();
            for (int i = 0; i < binsToShow.Count; i += step)
            {
                var group = binsToShow.Skip(i).Take(step).ToList();
                int  binLabel = group[0].Key;
                long summed   = group.Sum(kv => kv.Value);
                merged.Add(new KeyValuePair<int, long>(binLabel, summed));
            }
            binsToShow = merged;
            downsampled = true;
            Console.WriteLine($"  (range too wide to show every bin — displaying {binsToShow.Count} grouped bars, each ≈{step} Da wide)");
            Console.WriteLine();
        }

        // ── 7. Render ASCII bar chart ──────────────────────────────────────
        long chartMax = binsToShow.Max(kv => kv.Value);
        int  labelWidth = Math.Max(displayMax.ToString().Length + 3, 12); // "NNNN – MMMM"

        Console.WriteLine("  1-Da Binned Mass Histogram");
        Console.WriteLine($"  {"Bin (Da)",-{labelWidth}}  {"Ions",10}  Bar");
        Console.WriteLine($"  {new string('─', labelWidth)}  {"──────────",10}  {new string('─', BarWidth)}");

        foreach (var (bin, count) in binsToShow)
        {
            int barLen = (int)Math.Round((double)count / chartMax * BarWidth);
            string bar   = new string('█', barLen);
            string label = downsampled ? $"{bin,6}" : $"{bin,6} – {bin + 1}";
            Console.WriteLine($"  {label,-{labelWidth}}  {count,10:N0}  {bar}");
        }

        Console.WriteLine();
        Console.WriteLine("  Press Enter to continue.");
        Console.ReadLine();
    }
}
