namespace ProteoformAnalyzer;

/// <summary>
/// Builds decoy proteoforms for target–decoy false-discovery-rate (FDR) estimation.
///
/// Each decoy mirrors a real entry but with its predicted mass shifted by a deterministic
/// pseudo-random offset drawn from a band that (a) is wider than any sensible match
/// tolerance and (b) avoids small PTM mass deltas, so a decoy can only match an
/// experimental peak by chance. The decoy match rate therefore estimates how often a
/// target match could arise at random. Decoys are searched alongside targets and then
/// removed from the exported results.
/// </summary>
public static class DecoyGenerator
{
    // Mass-shift band (Da). Lower bound exceeds typical match windows (≤2 Da) and common
    // mod deltas; upper bound keeps decoys near the same mass region as their targets.
    private const double MinShift = 11.0;
    private const double MaxShift = 50.0;

    /// <param name="seed">Fixed for reproducibility — the same database yields the same decoys.</param>
    public static List<ProteoformEntry> Generate(IReadOnlyList<ProteoformEntry> targets, int seed = 1_234_567)
    {
        var rng = new Random(seed);
        var decoys = new List<ProteoformEntry>(targets.Count);

        foreach (var t in targets)
        {
            if (t.IsDecoy) continue; // never decoy a decoy

            double shift = MinShift + rng.NextDouble() * (MaxShift - MinShift);
            if (rng.Next(2) == 0) shift = -shift; // shift up or down

            IsotopeEnvelope? decoyEnvelope = t.Envelope is null
                ? null
                : new IsotopeEnvelope
                {
                    Centroid = t.Envelope.Centroid + shift,
                    Sigma    = t.Envelope.Sigma,
                    Points   = t.Envelope.Points.Select(p => (p.Mass + shift, p.Intensity)).ToList()
                };

            decoys.Add(new ProteoformEntry
            {
                ProteinLabel     = t.ProteinLabel,
                ModificationName = "DECOY " + t.ModificationName,
                AlternativeName  = t.AlternativeName,
                CentroidMass     = t.CentroidMass + shift,
                Tolerance        = t.Tolerance,
                Envelope         = decoyEnvelope,
                IsDecoy          = true
            });
        }

        return decoys;
    }
}
