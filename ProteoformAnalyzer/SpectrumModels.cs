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
    /// <summary>
    /// Adaptive ion-counting tolerance (Da).
    /// Defaults to the user-supplied value; shrinks to half the distance to the
    /// nearest neighbouring peak when that distance is less than 2 × default.
    /// </summary>
    public double Tolerance { get; set; }
}

/// <summary>
/// The result of matching one database proteoform to experimental peaks
/// across one or more .dmt files.
/// </summary>
public class AnalysisResult
{
    public ProteoformEntry DatabaseEntry { get; init; } = null!;
    /// <summary>Mean experimental centroid across all files in which this proteoform was matched.</summary>
    public double MeanExperimentalCentroid { get; set; }
    /// <summary>filename → ion count (0 if the proteoform was not matched in that file).</summary>
    public Dictionary<string, long> IonCountsPerFile { get; set; } = new();
}
