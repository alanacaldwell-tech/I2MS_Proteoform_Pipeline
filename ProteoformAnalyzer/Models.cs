namespace ProteoformAnalyzer;

/// <summary>Molecular formula tracked as element counts.</summary>
public class MolecularFormula
{
    public int C { get; set; }
    public int H { get; set; }
    public int N { get; set; }
    public int O { get; set; }
    public int S { get; set; }
    public int P { get; set; }

    public static MolecularFormula operator +(MolecularFormula a, MolecularFormula b) => new()
    {
        C = a.C + b.C, H = a.H + b.H, N = a.N + b.N,
        O = a.O + b.O, S = a.S + b.S, P = a.P + b.P
    };

    public override string ToString() =>
        $"C{C}H{H}N{N}O{O}" + (S > 0 ? $"S{S}" : "") + (P > 0 ? $"P{P}" : "");
}

/// <summary>A PTM annotation from any source database.</summary>
public class PtmAnnotation
{
    public string ModificationName { get; set; } = "";
    public int Position { get; set; }
    public char? Residue { get; set; }
    public double MassDelta { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>One row in the output CSV.</summary>
public class ProteoformEntry
{
    public string ModificationName { get; set; } = "";
    public double CentroidMass { get; set; }
    public double Tolerance { get; set; } = 5.0;
    public IsotopeEnvelope? Envelope { get; set; }
    // Populated after .dmt processing: filename → ion count
    public Dictionary<string, long>? IonCounts { get; set; }
}

/// <summary>Isotopic envelope summary from the distribution calculation.</summary>
public class IsotopeEnvelope
{
    /// <summary>Intensity-weighted centroid (= chemical average mass).</summary>
    public double Centroid { get; set; }
    /// <summary>Standard deviation of the envelope (Da).</summary>
    public double Sigma { get; set; }
    /// <summary>Sampled (mass, relative_intensity) pairs for the bell curve.</summary>
    public List<(double Mass, double Intensity)> Points { get; set; } = new();
}
