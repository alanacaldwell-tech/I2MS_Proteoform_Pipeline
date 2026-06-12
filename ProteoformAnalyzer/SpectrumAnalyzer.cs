namespace ProteoformAnalyzer;

/// <summary>
/// Analyses a single .dmt file:
///   1. Read all ion neutral masses.
///   2. Bin at 1-Da resolution → histogram.
///   3. Find peaks via local-maximum + FWHM criterion (minimum apex height of 2).
///   4. Refine each peak centroid from the raw ion masses within its FWHM window.
///   5. Match peaks against the proteoform database within matchWindow Da.
///   6. Count ions within ±ionCountingWindow of the matched database centroid.
/// </summary>
public static class SpectrumAnalyzer
{
    // Minimum number of ions in the apex bin to be considered a real peak (filters single-ion noise).
    private const long MinApexHeight = 2;

    // A bin qualifies as a peak apex only if it is the tallest bin within ±NeighbourhoodRadius bins.
    // Radius of 5 (10-Da window) is wider than any single 1-Da isotope spacing but narrower than
    // the typical inter-proteoform spacing, so one real isotope envelope produces exactly one peak.
    private const int NeighbourhoodRadius = 5;

    public static List<(ProteoformEntry Entry, double ExperimentalCentroid, long IonCount)>
        ProcessFile(
            string dmtPath,
            List<ProteoformEntry> database,
            double matchWindow = 2.0,
            double ionCountingWindow = 5.0)
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

        // ── Step 3 + 4: find local-maxima peaks and compute FWHM centroids ─
        var peaks = FindPeaks(histogram, masses);
        if (peaks.Count == 0)
            return new();

        // ── Step 4b: centroid-space deduplication ─────────────────────────
        // The neighbourhood check operates in bin-space, but the FWHM centroid
        // can be far from the apex bin.  Two apex bins >5 apart can still yield
        // centroids within a few Da of each other, making them look like isotope
        // peaks of the same envelope.  Deduplicate by keeping only the tallest
        // peak within any ionCountingWindow-wide centroid window.
        peaks = DeduplicatePeaks(peaks, ionCountingWindow);

        // ── Step 5 + 6: match to database and count ions ──────────────────
        var results = MatchAndCount(peaks, masses, database, matchWindow, ionCountingWindow);

        // ── Step 7: report peaks that had no database match ───────────────
        // Collect the rounded centroids of every matched peak so we can find the gaps.
        var matchedCentroids = new HashSet<long>(results.Select(r => (long)Math.Round(r.Item2)));
        foreach (var peak in peaks)
        {
            if (matchedCentroids.Contains((long)Math.Round(peak.Centroid))) continue;
            double lo = peak.Centroid - ionCountingWindow;
            double hi = peak.Centroid + ionCountingWindow;
            long ionCount = masses.LongCount(m => m >= lo && m <= hi);
            results.Add((
                new ProteoformEntry
                {
                    ModificationName = $"Unmatched peak ({peak.Centroid:F2} Da)",
                    CentroidMass = peak.Centroid
                },
                peak.Centroid,
                ionCount));
        }

        return results;
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

            // Require a minimum apex height to suppress single-ion noise bins
            if (height < MinApexHeight) continue;

            // Neighbourhood-based local maximum: apex must be the tallest bin in [b-N, b+N].
            // This prevents every noisy bump within an isotope envelope from being called a peak.
            bool isApex = true;
            for (int d = -NeighbourhoodRadius; d <= NeighbourhoodRadius; d++)
            {
                if (d == 0) continue;
                histogram.TryGetValue(b + d, out long nb);
                if (nb >= height) { isApex = false; break; }
            }
            if (!isApex) continue;

            // Ceiling division prevents half-max from being under-estimated for small apex heights,
            // which would otherwise extend the FWHM scan through empty bins in sparse data.
            long halfMax = Math.Max(1L, (height + 1) / 2);

            // Scan left for FWHM edge — bounded to NeighbourhoodRadius bins from the apex.
            // Bounding the scan prevents this peak's centroid window from extending into a
            // neighbouring peak's territory, which would pull the centroid off-centre and
            // cause the deduplication step to incorrectly merge two distinct proteoforms.
            int leftBin = b;
            for (int i = b - 1; i >= Math.Max(minBin, b - NeighbourhoodRadius); i--)
            {
                histogram.TryGetValue(i, out long cnt);
                if (cnt < halfMax) break;
                leftBin = i;
            }

            // Scan right for FWHM edge — same bound
            int rightBin = b;
            for (int i = b + 1; i <= Math.Min(maxBin, b + NeighbourhoodRadius); i++)
            {
                histogram.TryGetValue(i, out long cnt);
                if (cnt < halfMax) break;
                rightBin = i;
            }

            // Precise centroid: intensity-weighted mean of raw masses within the FWHM window
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
                Centroid = sumMass / sumCount,
                Height   = height,
                Fwhm     = rightBin - leftBin + 1.0,
                LeftBin  = leftBin,
                RightBin = rightBin
            });
        }

        return peaks;
    }

    // Sort tallest-first, then keep a peak only if its centroid is at least
    // minSeparation Da from every already-accepted peak.  This removes isotope
    // sub-peaks whose apex bins were >NeighbourhoodRadius apart in bin-space
    // but whose FWHM centroids ended up close together.
    private static List<SpectrumPeak> DeduplicatePeaks(
        List<SpectrumPeak> peaks, double minSeparation)
    {
        var sorted = peaks.OrderByDescending(p => p.Height).ToList();
        var kept   = new List<SpectrumPeak>();
        foreach (var p in sorted)
            if (kept.All(k => Math.Abs(k.Centroid - p.Centroid) >= minSeparation))
                kept.Add(p);
        return kept;
    }

    private static List<(ProteoformEntry, double, long)> MatchAndCount(
        List<SpectrumPeak> peaks,
        List<double> masses,
        List<ProteoformEntry> database,
        double matchWindow,
        double ionCountingWindow)
    {
        var results = new List<(ProteoformEntry, double, long)>();

        foreach (var peak in peaks)
        {
            foreach (var entry in database)
            {
                // Match: peak FWHM centroid must be within matchWindow of the database predicted mass
                if (Math.Abs(entry.CentroidMass - peak.Centroid) > matchWindow)
                    continue;

                // Count ALL ions within ±ionCountingWindow of the database entry centroid.
                // This captures the full proteoform signal (all isotopes), not just one bin.
                double lo = entry.CentroidMass - ionCountingWindow;
                double hi = entry.CentroidMass + ionCountingWindow;
                long ionCount = masses.LongCount(m => m >= lo && m <= hi);

                results.Add((entry, peak.Centroid, ionCount));
            }
        }

        return results;
    }
}
