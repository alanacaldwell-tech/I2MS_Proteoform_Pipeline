namespace IonCounter;

/// <summary>
/// Represents one row of the reference CSV: a centroid mass and its +/- tolerance window.
/// Ion counts per .dmt file are accumulated in <see cref="Counts"/>.
/// </summary>
internal sealed class CentroidEntry
{
    public double CentroidMass { get; }
    public double Tolerance { get; }

    // Ordered list of (filename, count) pairs — one per .dmt file processed.
    public List<(string FileName, long Count)> Counts { get; } = new();

    public CentroidEntry(double centroidMass, double tolerance)
    {
        CentroidMass = centroidMass;
        Tolerance = tolerance;
    }

    public double LowerBound => CentroidMass - Tolerance;
    public double UpperBound => CentroidMass + Tolerance;

    public bool Contains(double mass) => mass >= LowerBound && mass <= UpperBound;
}
