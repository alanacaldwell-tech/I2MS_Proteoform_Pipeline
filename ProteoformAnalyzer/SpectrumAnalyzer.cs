namespace ProteoformAnalyzer;

/// <summary>
/// Analyses a single .dmt file:
///   1. Read all ion neutral masses.
///   2. Bin at 1-Da resolution → histogram.
///   3. Find peaks via local-maximum + FWHM criterion.
///   4. Refine each peak centroid from the raw ion masses within its FWHM window.
///   5. Assign adaptive tolerances (shrink when neighbouring peaks are close).
///   6. Match peaks against the proteoform database at ±0.5 Da.
///   7. Count ions within the adaptive tolerance window for each match.
/// </summary>
public static class SpectrumAnalyzer
{
    // Fixed window for matching a peak centroid to a database predicted mass.
    private const double MatchWindow = 0.5;

    public static List<(ProteoformEntry Entry, double ExperimentalCentroid, long IonCount)>
        ProcessFile(
            string dmtPath,
            List<ProteoformEntry> database,
            double defaultTolerance)
    {
        // ── Step 1: read all masses ───────────────────────────────────────
        var masses = DmtParser.ReadMasses(dmtPath).ToList();
        if (masses.Count == 0)
            return new();

        // ── Step 2: build 1-Da histogram ─────────────────────────────────
        var histogram = new Dictionary<int, long>();
        foreach (double m in masses)
        {
            int bin = (int)Math.Floor(m);
            histogram[bin] = histogram.TryGetValue(bin, out long c) ? c + 1 : 1;
        }

        // ── Step 3 + 4: find peaks and compute precise centroids ──────────
        var peaks = FindPeaks(histogram, masses);
        if (peaks.Count == 0)
            return new();

        // ── Step 5: assign adaptive tolerances ───────────────────────────
        AssignAdaptiveTolerances(peaks, defaultTolerance);

        // ── Step 6 + 7: match to database and count ions ─────────────────
        return MatchAndCount(peaks, masses, database);
    }

    // ─────────────────────────────────────────────────────────────────────

    private static List<SpectrumPeak> FindPeaks(
        Dictionary<int, long> histogram,
        List<double> masses)
    {
        if (histogram.Count == 0) return new();

        int minBin = histogram.Keys.Min();
        int maxBin = histogram.Keys.Max();

        var peaks = new List<SpectrumPeak>();

        for (int b = minBin + 1; b < maxBin; b++)
        {
            histogram.TryGetValue(b, out long height);
            if (height == 0) continue;

            // Local maximum: strictly greater than immediate neighbours
            histogram.TryGetValue(b - 1, out long left);
            histogram.TryGetValue(b + 1, out long right);
            if (height <= left || height <= right) continue;

            long halfMax = Math.Max(1, height / 2);

            // Scan left for FWHM edge
            int leftBin = b;
            for (int i = b - 1; i >= minBin; i--)
            {
                histogram.TryGetValue(i, out long cnt);
                if (cnt < halfMax) break;
                leftBin = i;
            }

            // Scan right for FWHM edge
            int rightBin = b;
            for (int i = b + 1; i <= maxBin; i++)
            {
                histogram.TryGetValue(i, out long cnt);
                if (cnt < halfMax) break;
                rightBin = i;
            }

            // Precise centroid: intensity-weighted mean of raw masses in window
            // Window spans [leftBin, rightBin + 1) in Da
            double windowLo = leftBin;
            double windowHi = rightBin + 1.0;

            double sumMass = 0, sumCount = 0;
            foreach (double m in masses)
            {
                if (m >= windowLo && m < windowHi)
                {
                    sumMass += m;
                    sumCount += 1;
                }
            }

            if (sumCount == 0) continue;

            peaks.Add(new SpectrumPeak
            {
                Centroid  = sumMass / sumCount,
                Height    = height,
                Fwhm      = rightBin - leftBin + 1.0,
                LeftBin   = leftBin,
                RightBin  = rightBin
            });
        }

        return peaks;
    }

    private static void AssignAdaptiveTolerances(
        List<SpectrumPeak> peaks, double defaultTolerance)
    {
        // Initialise all peaks with the default
        foreach (var p in peaks)
            p.Tolerance = defaultTolerance;

        if (peaks.Count < 2) return;

        // Sort by centroid for neighbour comparison
        peaks.Sort((a, b) => a.Centroid.CompareTo(b.Centroid));

        for (int i = 0; i < peaks.Count - 1; i++)
        {
            double dist = peaks[i + 1].Centroid - peaks[i].Centroid;
            if (dist < 2 * defaultTolerance)
            {
                double half = dist / 2.0;
                // Shrink — never grow beyond the default
                if (half < peaks[i].Tolerance)     peaks[i].Tolerance     = half;
                if (half < peaks[i + 1].Tolerance) peaks[i + 1].Tolerance = half;
            }
        }
    }

    private static List<(ProteoformEntry, double, long)> MatchAndCount(
        List<SpectrumPeak> peaks,
        List<double> masses,
        List<ProteoformEntry> database)
    {
        var results = new List<(ProteoformEntry, double, long)>();

        foreach (var peak in peaks)
        {
            // Find all database entries whose predicted mass is within MatchWindow of the peak centroid
            foreach (var entry in database)
            {
                if (Math.Abs(entry.CentroidMass - peak.Centroid) > MatchWindow)
                    continue;

                // Count all ions within the adaptive tolerance window around the centroid
                double lo = peak.Centroid - peak.Tolerance;
                double hi = peak.Centroid + peak.Tolerance;
                long ionCount = masses.LongCount(m => m >= lo && m <= hi);

                results.Add((entry, peak.Centroid, ionCount));
            }
        }

        return results;
    }
}
