namespace ProteoformAnalyzer;

/// <summary>
/// Attaches disease/variant context to PTM annotations and proteoform entries.
///
/// Site-level signal: a PTM whose position colocalizes with an annotated sequence variant is a
/// candidate "PTM-disrupting variant" — the modification may be lost or altered by the variant.
/// Protein-level signal: the diseases the protein is implicated in.
///
/// Both are reported as context, honestly labelled; neither asserts that a specific proteoform is
/// itself pathological.
/// </summary>
public static class DiseaseAnnotator
{
    // Cap the per-protein variant-site note list so the CSV cell stays readable.
    private const int MaxSiteNotes = 25;

    /// <summary>
    /// Marks PTM sites that colocalize with a sequence variant (adding the variant description to
    /// <see cref="PtmAnnotation.DiseaseAssociations"/>) and returns human-readable site notes,
    /// e.g. "S129 Phosphoserine @ variant: in PARK1; ...".
    /// </summary>
    public static List<string> Annotate(List<PtmAnnotation> ptms, DiseaseInfo disease)
    {
        if (disease.VariantSites.Count == 0) return new();

        var variantsByPosition = disease.VariantSites
            .Where(v => v.Position > 0)
            .GroupBy(v => v.Position)
            .ToDictionary(g => g.Key, g => g.Select(v => v.Description).Distinct().ToList());

        var notes = new List<string>();
        foreach (var p in ptms)
        {
            if (p.Position <= 0 || !variantsByPosition.TryGetValue(p.Position, out var descriptions))
                continue;

            p.DiseaseAssociations.AddRange(descriptions);
            string residue = p.Residue?.ToString() ?? "";
            notes.Add($"{residue}{p.Position} {p.ModificationName} @ variant: {string.Join(" | ", descriptions)}");
        }

        notes = notes.Distinct().ToList();
        if (notes.Count > MaxSiteNotes)
        {
            int extra = notes.Count - MaxSiteNotes;
            notes = notes.Take(MaxSiteNotes).Append($"(+{extra} more variant-colocalized site(s))").ToList();
        }
        return notes;
    }

    /// <summary>Copies the protein-level disease list and site notes onto every proteoform entry.</summary>
    public static void Apply(IEnumerable<ProteoformEntry> entries, DiseaseInfo disease, List<string> siteNotes)
    {
        foreach (var e in entries)
        {
            e.ProteinDiseaseInvolvement = disease.ProteinDiseases;
            e.PtmVariantSites = siteNotes;
        }
    }
}
