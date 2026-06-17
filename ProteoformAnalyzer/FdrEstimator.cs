namespace ProteoformAnalyzer;

/// <summary>Target/decoy hit counts and the resulting false-discovery-rate estimate.</summary>
public readonly record struct FdrSummary(int TargetHits, int DecoyHits, double Fdr)
{
    public string Describe() =>
        $"{Fdr:P2} estimated FDR ({DecoyHits} decoy / {TargetHits} target hits)";
}

/// <summary>
/// Classic target–decoy FDR estimation: FDR ≈ (number of decoy hits) / (number of target
/// hits). Decoys are matched under identical rules to targets, so their hit count
/// approximates the number of target hits expected by chance. Unmatched-peak rows and
/// decoys themselves are not counted as target hits.
/// </summary>
public static class FdrEstimator
{
    public static FdrSummary Estimate(IEnumerable<AnalysisResult> results)
    {
        int targets = 0, decoys = 0;
        foreach (var r in results)
        {
            if (r.DatabaseEntry.IsDecoy) decoys++;
            else if (!IsUnmatched(r)) targets++;
        }

        double fdr = targets > 0 ? Math.Min(1.0, (double)decoys / targets) : 0.0;
        return new FdrSummary(targets, decoys, fdr);
    }

    private static bool IsUnmatched(AnalysisResult r) =>
        r.DatabaseEntry.ModificationName.StartsWith("Unmatched peak", StringComparison.Ordinal);
}
