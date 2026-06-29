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
    // Cap the per-proteoform variant-site note list so the CSV cell stays readable.
    private const int MaxSiteNotes = 25;

    /// <summary>
    /// One PTM site whose position colocalizes with an annotated disease sequence variant. Carries
    /// the modification <see cref="FamilyKey"/> (so we can tell which proteoforms could host it) and
    /// the original 1-based <see cref="Position"/> (so we can drop it from truncated proteoforms that
    /// no longer span the site), plus a human-readable <see cref="Note"/>.
    /// </summary>
    public record VariantColocalizedSite(string FamilyKey, int Position, string Note);

    /// <summary>
    /// Finds every PTM whose position colocalizes with a disease sequence variant, marks the PTM
    /// (adding the variant description to <see cref="PtmAnnotation.DiseaseAssociations"/>) and returns
    /// the colocalized sites with the metadata needed to attach them to the right proteoforms.
    /// </summary>
    public static List<VariantColocalizedSite> ColocalizedSites(List<PtmAnnotation> ptms, DiseaseInfo disease)
    {
        if (disease.VariantSites.Count == 0) return new();

        var variantsByPosition = disease.VariantSites
            .Where(v => v.Position > 0)
            .GroupBy(v => v.Position)
            .ToDictionary(g => g.Key, g => g.Select(v => v.Description).Distinct().ToList());

        var sites = new List<VariantColocalizedSite>();
        foreach (var p in ptms)
        {
            if (p.Position <= 0 || !variantsByPosition.TryGetValue(p.Position, out var descriptions))
                continue;

            p.DiseaseAssociations.AddRange(descriptions);
            string residue = p.Residue?.ToString() ?? "";
            string note = $"{residue}{p.Position} {p.ModificationName} @ variant: {string.Join(" | ", descriptions)}";
            sites.Add(new VariantColocalizedSite(ProteoformBuilder.ModFamily(p.ModificationName), p.Position, note));
        }
        return sites;
    }

    /// <summary>
    /// Protein-wide variant-colocalized site notes (deduplicated, capped). Convenience for console
    /// summaries; per-proteoform attribution is done by <see cref="Apply"/>.
    /// </summary>
    public static List<string> Annotate(List<PtmAnnotation> ptms, DiseaseInfo disease) =>
        Cap(ColocalizedSites(ptms, disease).Select(s => s.Note).Distinct().ToList());

    /// <summary>
    /// Attaches disease context to each proteoform. The protein-level disease list is shared context
    /// (the same for every proteoform of a protein), but the variant-colocalized PTM sites are made
    /// proteoform-specific: a site is attached to a proteoform only when that proteoform actually
    /// carries the site's modification family AND still spans the site's residue. So the unmodified
    /// form, or a form modified only at other positions, carries no variant sites and is not flagged
    /// as a disease-relevant proteoform.
    /// </summary>
    public static void Apply(
        IEnumerable<ProteoformEntry> entries, DiseaseInfo disease, IReadOnlyList<VariantColocalizedSite> sites)
    {
        foreach (var e in entries)
        {
            e.ProteinDiseaseInvolvement = disease.ProteinDiseases;
            e.PtmVariantSites = Cap(sites
                .Where(s => e.PtmFamilies.Contains(s.FamilyKey)
                         && s.Position >= e.StartResidue && s.Position <= e.EndResidue)
                .Select(s => s.Note)
                .Distinct()
                .ToList());
        }
    }

    private static List<string> Cap(List<string> notes)
    {
        if (notes.Count <= MaxSiteNotes) return notes;
        int extra = notes.Count - MaxSiteNotes;
        return notes.Take(MaxSiteNotes).Append($"(+{extra} more variant-colocalized site(s))").ToList();
    }
}
