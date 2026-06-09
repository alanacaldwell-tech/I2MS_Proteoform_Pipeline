namespace IonCounterGUI;

internal sealed class CentroidEntry
{
    public double CentroidMass { get; }
    public double Tolerance { get; }
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
