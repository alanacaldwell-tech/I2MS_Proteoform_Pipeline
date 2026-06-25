namespace ProteoformAnalyzer;

/// <summary>
/// Disease context for one protein, drawn from UniProt's free annotations:
///   • <see cref="ProteinDiseases"/> — diseases the protein is implicated in (DISEASE comments).
///   • <see cref="VariantSites"/> — annotated sequence-variant positions and their descriptions
///     (Natural variant features). A PTM whose site colocalizes with one of these is a candidate
///     "PTM-disrupting variant".
///
/// This is intentionally not a claim that a given PTM is pathological — that requires curated
/// site-level disease data (e.g. PhosphoSitePlus) and/or case-vs-control quantitation. It surfaces
/// the documented disease/variant context so the analyst can prioritise.
/// </summary>
public class DiseaseInfo
{
    public List<string> ProteinDiseases { get; set; } = new();
    public List<(int Position, string Description)> VariantSites { get; set; } = new();

    public bool IsEmpty => ProteinDiseases.Count == 0 && VariantSites.Count == 0;
}
