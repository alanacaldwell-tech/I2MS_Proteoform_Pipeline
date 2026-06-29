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
    /// <summary>Variant-colocalized PTM sites that THIS proteoform can actually carry — i.e. sites
    /// whose modification family is present in this proteoform and whose position survives in this
    /// proteoform's residue range. Empty for proteoforms that do not carry a disease-variant-
    /// colocalized modification (e.g. the unmodified form, or a form modified only elsewhere).</summary>
    public List<string> PtmVariantSites { get; set; } = new();

    /// <summary>Short qualitative label for how this specific proteoform relates to disease, derived
    /// from the variant-colocalized PTM sites it carries: "Disease variant PTM" (carries a PTM at a
    /// site disrupted by a disease-associated variant), "Sequence variant PTM" (carries a PTM at a
    /// variant of unspecified significance), or "" (no variant-colocalized PTM — consistent with the
    /// normal/reference form). This is a composition claim, not an abundance claim.</summary>
    public string DiseaseRelevance { get; set; } = "";

    /// <summary>Modification families this proteoform carries (e.g. "phosphorylation"), keyed the
    /// same way as <see cref="ProteoformBuilder.ModFamily"/>. Empty for the unmodified/truncation-only
    /// forms. Used to decide which disease-variant-colocalized sites apply to this specific proteoform.</summary>
    public HashSet<string> PtmFamilies { get; set; } = new();

    /// <summary>First surviving residue (1-based, inclusive) for this proteoform. 1 for full-length
    /// and C-terminal truncations; higher for N-terminal truncations. Used to drop variant sites
    /// that fall outside a truncated proteoform.</summary>
    public int StartResidue { get; set; } = 1;

    /// <summary>Last surviving residue (1-based, inclusive) for this proteoform. Equals the sequence
    /// length for full-length and N-terminal truncations; lower for C-terminal truncations.</summary>
    public int EndResidue { get; set; }
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
