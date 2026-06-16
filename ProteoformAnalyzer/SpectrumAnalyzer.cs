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

    // Ion-counting window half-width, in units of the predicted isotope-envelope σ.
    // 3σ captures ~99.7% of a Gaussian envelope, so the integration width scales with
    // protein size instead of using a single fixed window. The user-supplied window acts
    // as a floor (see MatchAndCount), so small proteoforms never integrate below it.
    private const double EnvelopeSigmaK = 3.0;

    public static List<SpectrumMatch>
        ProcessFile(
            string dmtPath,
            List<ProteoformEntry> database,
            double matchWindow = 2.0,
            double ionCountingWindow = 5.0,
            long minIonCount = 0)
    {
        // ── Step 1: read all ions and sort once by mass ───────────────────
        // Sorting once lets every downstream window query use binary search
        // instead of re-scanning the full ion list, and lets us collect the
        // charge states present in any mass window from a contiguous slice.
        var ions = DmtParser.ReadIons(dmtPath).ToList();
        if (ions.Count == 0)
            return new();

        ions.Sort((a, b) => a.Mass.CompareTo(b.Mass));
        var sortedMasses = new double[ions.Count];
        for (int i = 0; i < ions.Count; i++) sortedMasses[i] = ions[i].Mass;

        // ── Step 2: build 1-Da histogram ─────────────────────────────────
        var histogram = new Dictionary<int, long>();
        foreach (double m in sortedMasses)
        {
            int bin = (int)Math.Floor(m);
            histogram[bin] = histogram.TryGetValue(bin, out long c) ? c + 1 : 1;
        }

        // ── Step 2b: auto-estimate noise floor if no threshold supplied ───
        // Scan the full mass range with non-overlapping windows of width
        // 2*ionCountingWindow.  The 25th-percentile window ion count is used as
        // a dataset-wide background estimate, which avoids the bias that a
        // fixed local-flanking window introduces when two proteoforms are
        // closely spaced (their flanking regions overlap each other's signal).
        long noiseFloor = minIonCount > 0
            ? minIonCount
            : EstimateNoiseFloor(sortedMasses, ionCountingWindow);
        Console.WriteLine($"  Noise floor: {noiseFloor} ions (peaks with ≤ {noiseFloor} ions will be discarded)");

        // ── Step 3 + 4: find local-maxima peaks and compute FWHM centroids ─
        var peaks = FindPeaks(histogram, sortedMasses);
        if (peaks.Count == 0)
            return new();

        // ── Step 4b: centroid-space deduplication ─────────────────────────
        // The neighbourhood check operates in bin-space, but the FWHM centroid
        // can be far from the apex bin.  Two apex bins >5 apart can still yield
        // centroids within a few Da of each other, making them look like isotope
        // peaks of the same envelope.  Deduplicate by keeping only the tallest
        // peak within any ionCountingWindow-wide centroid window.
        peaks = DeduplicatePeaks(peaks, ionCountingWindow);

        // ── Step 4c: envelope width filter ───────────────────────────────
        // Small molecules and single-ion artefacts occupy only one 1-Da bin
        // (Fwhm = 1).  Real intact-protein isotope envelopes always span at
        // least 2 Da at half-maximum even for the smallest proteins (~1 kDa).
        // Discard any peak whose FWHM window is a single bin.
        peaks = peaks.Where(p => p.Fwhm >= 2.0).ToList();

        // ── Step 5 + 6: match to database, count ions, score and rank ─────
        var results = MatchAndCount(peaks, ions, sortedMasses, database, matchWindow, ionCountingWindow);

        // ── Step 7: report peaks that had no database match ───────────────
        // Collect the rounded centroids of every matched peak so we can find the gaps.
        var matchedCentroids = new HashSet<long>(results.Select(r => (long)Math.Round(r.ExperimentalCentroid)));
        foreach (var peak in peaks)
        {
            if (matchedCentroids.Contains((long)Math.Round(peak.Centroid))) continue;
            var (ionCount, charges) = CountWindow(
                ions, sortedMasses, peak.Centroid - ionCountingWindow, peak.Centroid + ionCountingWindow);
            results.Add(new SpectrumMatch(
                new ProteoformEntry
                {
                    ModificationName = $"Unmatched peak ({peak.Centroid:F2} Da)",
                    CentroidMass = peak.Centroid
                },
                peak.Centroid,
                ionCount,
                MassErrorDa: 0.0,
                ChargeStates: charges,
                RankWithinPeak: 1));
        }

        // ── Step 8: noise floor filter ────────────────────────────────────
        // Filter is applied to the final ion counts (the same values written
        // to the CSV) rather than to raw peaks, so the threshold is directly
        // comparable to what the user sees in the output.
        results = results.Where(r => r.IonCount > noiseFloor).ToList();

        return results;
    }

    /// <summary>
    /// Estimates the background noise floor from the full ion mass list.
    /// Scans non-overlapping windows of width 2*windowHalfWidth across the
    /// entire mass range, collects the ion count for each window that contains
    /// at least one ion, then returns the 25th percentile of those non-empty
    /// counts.  Most non-empty windows in a typical I2MS spectrum contain only
    /// 1–10 scattered background ions; the 25th percentile sits comfortably in
    /// that noise range while being insensitive to the handful of high-count
    /// real-proteoform windows that would skew the median upward.
    /// </summary>
    public static long EstimateNoiseFloor(double[] sortedMasses, double windowHalfWidth)
    {
        if (sortedMasses.Length == 0) return 0;

        double minMass = sortedMasses[0];
        double maxMass = sortedMasses[^1];
        double windowWidth = windowHalfWidth * 2.0;

        double span = maxMass - minMass;
        int numWindows = (int)(span / windowWidth);
        // Too few windows to characterise a background — skip noise filtering entirely.
        if (numWindows < 10) return 0;

        // Two-pointer scan over the already-sorted masses for O(n) window counting.
        var nonEmptyCounts = new List<long>();

        int left = 0;
        for (int w = 0; w < numWindows; w++)
        {
            double lo = minMass + w * windowWidth;
            double hi = lo + windowWidth;

            while (left < sortedMasses.Length && sortedMasses[left] < lo) left++;

            int right = left;
            while (right < sortedMasses.Length && sortedMasses[right] < hi) right++;

            long count = right - left;
            if (count > 0)
                nonEmptyCounts.Add(count);
        }

        if (nonEmptyCounts.Count == 0) return 0;

        // 25th percentile of non-empty window counts = noise floor, minimum 10.
        nonEmptyCounts.Sort();
        int p25index = (int)Math.Floor(nonEmptyCounts.Count * 0.25);
        return Math.Max(10L, nonEmptyCounts[p25index]);
    }

    // ─────────────────────────────────────────────────────────────────────

    private static List<SpectrumPeak> FindPeaks(
        Dictionary<int, long> histogram,
        double[] sortedMasses)
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

            // Precise centroid: mean of raw masses within the FWHM window
            // Window spans [leftBin, rightBin + 1) in Da. Binary search bounds the
            // contiguous slice of the sorted mass array instead of scanning all ions.
            double windowLo = leftBin;
            double windowHi = rightBin + 1.0;

            int sliceStart = LowerBound(sortedMasses, windowLo);   // first mass >= windowLo
            int sliceEnd   = LowerBound(sortedMasses, windowHi);   // first mass >= windowHi (exclusive)
            int sliceCount = sliceEnd - sliceStart;
            if (sliceCount == 0) continue;

            double sumMass = 0;
            for (int i = sliceStart; i < sliceEnd; i++) sumMass += sortedMasses[i];

            peaks.Add(new SpectrumPeak
            {
                Centroid = sumMass / sliceCount,
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

    private static List<SpectrumMatch> MatchAndCount(
        List<SpectrumPeak> peaks,
        IReadOnlyList<IonMeasurement> ions,
        double[] sortedMasses,
        List<ProteoformEntry> database,
        double matchWindow,
        double ionCountingWindow)
    {
        var results = new List<SpectrumMatch>();

        foreach (var peak in peaks)
        {
            // Gather every database entry whose predicted mass falls within matchWindow
            // of this peak, then rank them so ambiguous assignments are ordered by evidence.
            var candidates = new List<SpectrumMatch>();
            foreach (var entry in database)
            {
                // Match: peak FWHM centroid must be within matchWindow of the database predicted mass
                if (Math.Abs(entry.CentroidMass - peak.Centroid) > matchWindow)
                    continue;

                // Integrate over an adaptive window: ±max(user window, k·σ) of the predicted
                // envelope, so large proteoforms capture their full (wider) isotope envelope
                // while the user-supplied window remains a floor for small ones.
                double window = Math.Max(ionCountingWindow, EnvelopeSigmaK * (entry.Envelope?.Sigma ?? 0.0));
                var (ionCount, charges) = CountWindow(
                    ions, sortedMasses, entry.CentroidMass - window, entry.CentroidMass + window);

                double massError = entry.CentroidMass - peak.Centroid;
                candidates.Add(new SpectrumMatch(entry, peak.Centroid, ionCount, massError, charges, 0));
            }

            // Rank within this peak: more corroborating charge states is the stronger signal
            // (a real proteoform is seen at several charges); break ties by smaller mass error.
            var ranked = candidates
                .OrderByDescending(c => c.ChargeStates.Count)
                .ThenBy(c => Math.Abs(c.MassErrorDa))
                .ToList();
            for (int r = 0; r < ranked.Count; r++)
                results.Add(ranked[r] with { RankWithinPeak = r + 1 });
        }

        return results;
    }

    // ── Sorted-mass window helpers ────────────────────────────────────────

    /// <summary>First index i with sortedMasses[i] >= value (lower bound).</summary>
    private static int LowerBound(double[] sortedMasses, double value)
    {
        int lo = 0, hi = sortedMasses.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sortedMasses[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>First index i with sortedMasses[i] > value (upper bound).</summary>
    private static int UpperBound(double[] sortedMasses, double value)
    {
        int lo = 0, hi = sortedMasses.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sortedMasses[mid] <= value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Counts ions in the inclusive mass range [lo, hi] and returns the distinct charge
    /// states observed there (sorted). Both come from the same contiguous slice of the
    /// mass-sorted ion list, so the cost is O(log n + slice) rather than O(n).
    /// </summary>
    private static (long Count, IReadOnlyList<int> Charges) CountWindow(
        IReadOnlyList<IonMeasurement> ions, double[] sortedMasses, double lo, double hi)
    {
        int start = LowerBound(sortedMasses, lo);   // first mass >= lo
        int end   = UpperBound(sortedMasses, hi);   // first mass >  hi (so [start,end) is inclusive of hi)

        var charges = new HashSet<int>();
        for (int i = start; i < end; i++) charges.Add(ions[i].Charge);

        var sortedCharges = charges.ToList();
        sortedCharges.Sort();
        return (end - start, sortedCharges);
    }
}
