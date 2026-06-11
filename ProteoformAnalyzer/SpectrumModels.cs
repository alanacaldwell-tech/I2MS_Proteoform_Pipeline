namespace ProteoformAnalyzer;

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
    /// <summary>Experimental FWHM centroid of the peak that matched this proteoform (Da).</summary>
    public double ExperimentalCentroid { get; set; }
    /// <summary>filename → ion count within ±ionCountingWindow of the database centroid (0 if not matched in that file).</summary>
    public Dictionary<string, long> IonCountsPerFile { get; set; } = new();
}
