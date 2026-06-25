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
    /// <summary>Disease/variant context attached to this site (e.g. a UniProt sequence variant
    /// that colocalizes with this PTM position). Empty when no disease association is known.</summary>
    public List<string> DiseaseAssociations { get; set; } = new();
}

/// <summary>One entry in the proteoform database (predicted proteoform).</summary>
public class ProteoformEntry
{
    /// <summary>UniProt accession or short label identifying the source protein.</summary>
    public string ProteinLabel { get; set; } = "";
    public string ModificationName { get; set; } = "";
    /// <summary>
    /// Human-friendly alias: truncations rendered as residue ranges (e.g. "6-140"),
    /// "(X of Y sites)" removed from PTM names.
    /// </summary>
    public string AlternativeName { get; set; } = "";
    public double CentroidMass { get; set; }       // predicted centroid (Da)
    public double Tolerance { get; set; } = 5.0;   // kept for ProteoformBuilder; overridden by adaptive value at match time
    public IsotopeEnvelope? Envelope { get; set; }
    /// <summary>True for a decoy entry used only to estimate the false-discovery rate;
    /// decoys are matched alongside targets but excluded from the exported results.</summary>
    public bool IsDecoy { get; set; }
    /// <summary>Diseases the source protein is implicated in (UniProt DISEASE annotations).
    /// Protein-level context, identical for every proteoform of the same protein.</summary>
    public List<string> ProteinDiseaseInvolvement { get; set; } = new();
    /// <summary>PTM sites of the source protein that colocalize with an annotated sequence
    /// variant ("PTM-disrupting variant" candidates). Protein-scoped; NOT a claim that this
    /// specific proteoform occupies those sites.</summary>
    public List<string> PtmVariantSites { get; set; } = new();
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
