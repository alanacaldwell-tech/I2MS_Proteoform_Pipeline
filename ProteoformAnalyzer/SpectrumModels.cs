namespace ProteoformAnalyzer;

/// <summary>
/// A single individually-measured ion from a .dmt file: its neutral mass and the
/// charge state it was observed at. In I2MS every ion is measured individually, so
/// the charge is real per-ion information (not inferred from an envelope).
/// A value struct keeps the full ion list as one contiguous allocation — .dmt files
/// can hold millions of ions.
/// </summary>
public readonly record struct IonMeasurement(double Mass, int Charge);

/// <summary>
/// One experimental peak matched (or not) to a database proteoform within a single
/// .dmt file, carrying the evidence used for scoring.
/// </summary>
public record SpectrumMatch(
    ProteoformEntry Entry,
    double ExperimentalCentroid,
    long IonCount,
    // MassErrorDa: predicted centroid minus experimental centroid (Da), signed.
    double MassErrorDa,
    // ChargeStates: distinct charge states observed within the integration window (sorted).
    IReadOnlyList<int> ChargeStates,
    // RankWithinPeak: rank among all entries matching the same peak (1 = best).
    int RankWithinPeak,
    // IsUnmatchedPeak: true for a peak that matched no database proteoform (a synthetic
    // "Unmatched peak" row), false for a real database match.
    bool IsUnmatchedPeak);

/// <summary>
/// One peak identified in a 1-Da binned mass spectrum.
/// </summary>
public class SpectrumPeak
{
    /// <summary>Intensity-weighted centroid of all raw ion masses within the FWHM window (Da).</summary>
    public double Centroid { get; set; }
    /// <summary>Maximum bin count (apex height).</summary>
    public long Height { get; set; }
    /// <summary>Full width at half maximum in Da (measured on the binned histogram).</summary>
    public double Fwhm { get; set; }
    /// <summary>Leftmost 1-Da bin index included in this peak's FWHM window.</summary>
    public int LeftBin { get; set; }
    /// <summary>Rightmost 1-Da bin index included in this peak's FWHM window.</summary>
    public int RightBin { get; set; }
}

/// <summary>
/// One hit: a single experimental peak matched to a database proteoform,
/// with ion counts accumulated across .dmt files.
/// Multiple hits to the same database entry are stored as separate instances.
/// </summary>
public class AnalysisResult
{
    public ProteoformEntry DatabaseEntry { get; init; } = null!;
    /// <summary>Representative experimental mass: the ion-count-weighted mean of the per-file
    /// experimental masses (Da). Slightly different experimental masses that track to this same
    /// proteoform are merged into this one value; the per-file detail lives in ExperimentalMassPerFile.</summary>
    public double ExperimentalCentroid { get; set; }
    /// <summary>Predicted centroid minus the representative experimental mass (Da); signed.</summary>
    public double MassErrorDa { get; set; }
    /// <summary>Best (lowest) rank this assignment achieved among entries matching a peak (1 = best).</summary>
    public int RankWithinPeak { get; set; }
    /// <summary>Distinct charge states observed for this proteoform, unioned across all .dmt files.
    /// A larger set is stronger corroboration that the assignment is real.</summary>
    public SortedSet<int> ChargeStatesObserved { get; set; } = new();
    /// <summary>filename → ion count within the integration window of the database centroid (0 if not matched in that file).</summary>
    public Dictionary<string, long> IonCountsPerFile { get; set; } = new();
    /// <summary>filename → experimental peak mass observed for this proteoform in that file (Da).
    /// Absent when the proteoform was not detected in a given file.</summary>
    public Dictionary<string, double> ExperimentalMassPerFile { get; set; } = new();
}
