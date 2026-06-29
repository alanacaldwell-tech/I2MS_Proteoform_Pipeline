using System.Text.RegularExpressions;

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
    /// One PTM site whose position colocalizes with an annotated sequence variant. Carries the
    /// modification <see cref="FamilyKey"/> (so we can tell which proteoforms could host it), the
    /// original 1-based <see cref="Position"/> (so we can drop it from truncated proteoforms that no
    /// longer span the site), a human-readable <see cref="Note"/>, and whether the variant is
    /// disease-associated (<see cref="IsDiseaseAssociated"/>, i.e. linked to one of the protein's
    /// documented diseases) versus a variant of unspecified significance.
    /// </summary>
    public record VariantColocalizedSite(string FamilyKey, int Position, string Note, bool IsDiseaseAssociated);

    /// <summary>
    /// Finds every PTM whose position colocalizes with a sequence variant, marks the PTM (adding the
    /// variant description to <see cref="PtmAnnotation.DiseaseAssociations"/>) and returns the
    /// colocalized sites. Each note links the site to the specific disease where the UniProt variant
    /// description names one of the protein's documented diseases, and frames it against the normal
    /// (reference) sequence so the analyst has a healthy-state reference point.
    /// </summary>
    public static List<VariantColocalizedSite> ColocalizedSites(List<PtmAnnotation> ptms, DiseaseInfo disease)
    {
        if (disease.VariantSites.Count == 0) return new();

        // Map each documented disease acronym (e.g. "PARK1") back to its full name so a variant
        // description like "in PARK1; loss of function" can be linked to "Parkinson disease (PARK1)".
        var diseasesByAcronym = disease.ProteinDiseases
            .Select(name => (Name: name, Acronym: ExtractAcronym(name)))
            .Where(x => x.Acronym.Length > 0)
            .ToList();

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
            string desc = string.Join(" | ", descriptions);

            bool isDisease = TryLinkDisease(desc, diseasesByAcronym, out string linkedDisease);
            string note = isDisease
                ? $"{residue}{p.Position} {p.ModificationName} — modification disrupted by a {linkedDisease}-" +
                  $"associated variant (\"{desc}\"); the site is intact in the normal/reference sequence"
                : $"{residue}{p.Position} {p.ModificationName} — at a sequence variant of unspecified " +
                  $"clinical significance (\"{desc}\")";

            sites.Add(new VariantColocalizedSite(
                ProteoformBuilder.ModFamily(p.ModificationName), p.Position, note, isDisease));
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

            var own = sites
                .Where(s => e.PtmFamilies.Contains(s.FamilyKey)
                         && s.Position >= e.StartResidue && s.Position <= e.EndResidue)
                .ToList();

            e.PtmVariantSites = Cap(own.Select(s => s.Note).Distinct().ToList());
            e.DiseaseRelevance = own.Count == 0
                ? ""
                : own.Any(s => s.IsDiseaseAssociated) ? "Disease variant PTM" : "Sequence variant PTM";
        }
    }

    /// <summary>Extracts a trailing parenthesised acronym, e.g. "Parkinson disease (PARK1)" → "PARK1".</summary>
    private static string ExtractAcronym(string diseaseName)
    {
        var m = Regex.Match(diseaseName, @"\(([^)]+)\)\s*$");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>
    /// True when the variant description names one of the protein's documented diseases (matched on
    /// the disease acronym as a whole word), or carries explicit pathogenic language. Returns the
    /// linked disease name (or "disease") so the note can reference the specific condition.
    /// </summary>
    private static bool TryLinkDisease(
        string description, List<(string Name, string Acronym)> diseases, out string linked)
    {
        foreach (var (name, acronym) in diseases)
        {
            if (Regex.IsMatch(description, $@"\b{Regex.Escape(acronym)}\b", RegexOptions.IgnoreCase))
            {
                linked = name;
                return true;
            }
        }
        if (Regex.IsMatch(description, @"\bpathogenic\b", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(description, @"\b(benign|non-pathogenic|likely benign)\b", RegexOptions.IgnoreCase))
        {
            linked = "disease";
            return true;
        }
        linked = "";
        return false;
    }

    private static List<string> Cap(List<string> notes)
    {
        if (notes.Count <= MaxSiteNotes) return notes;
        int extra = notes.Count - MaxSiteNotes;
        return notes.Take(MaxSiteNotes).Append($"(+{extra} more variant-colocalized site(s))").ToList();
    }
}
