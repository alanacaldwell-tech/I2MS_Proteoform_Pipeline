using System.Text.Json.Serialization;

namespace ProteoformAnalyzer;

public class UniProtClient(HttpClient http)
{
    private const string Base = "https://rest.uniprot.org/uniprotkb";

    public async Task<(string sequence, List<PtmAnnotation> ptms)> FetchAsync(string accession)
    {
        Console.WriteLine($"  [UniProt] Querying {accession}...");

        string url = $"{Base}/{accession}?format=json";
        var (entry, outcome) = await ResilientJson.GetAsync<UniProtEntry>(http, url, "UniProt");
        if (entry is null || outcome is FetchOutcome.Failed or FetchOutcome.NotFound)
        {
            Console.WriteLine($"  [UniProt] Could not retrieve {accession} ({outcome}).");
            return ("", new List<PtmAnnotation>());
        }

        string seq = entry.Sequence?.Value ?? "";
        var ptms = new List<PtmAnnotation>();

        foreach (var feature in entry.Features ?? [])
        {
            // Capture modified residues, cross-links, glycosylation, lipidation, etc.
            if (feature.Type is null) continue;
            string ftype = feature.Type;
            if (!IsModFeature(ftype)) continue;

            int pos = feature.Location?.Start?.Value ?? 0;
            string name = feature.Description ?? ftype;

            if (!IsValidModName(name)) continue;

            double delta = KnownDeltaFromName(name);

            char? residue = null;
            if (pos > 0 && pos <= seq.Length)
                residue = char.ToUpper(seq[pos - 1]);

            ptms.Add(new PtmAnnotation
            {
                ModificationName = name,
                Position = pos,
                Residue = residue,
                MassDelta = delta,
                Source = "UniProt"
            });
        }

        Console.WriteLine($"  [UniProt] Retrieved sequence ({seq.Length} aa), {ptms.Count} PTM annotations.");
        RunManifest.Record($"UniProt {accession}: {seq.Length} aa, {ptms.Count} PTM annotations ({outcome})");
        return (seq, ptms);
    }

    /// <summary>
    /// Fetches annotated single-residue substitutions ("Natural variant" features) for an accession,
    /// with the average-mass delta of each substitution computed from the residue formulas. Only
    /// single standard-residue → single standard-residue changes are returned (insertions, deletions,
    /// multi-residue and non-standard variants are skipped). Reuses the same cached UniProt JSON.
    /// </summary>
    public async Task<List<PointVariant>> FetchVariantsAsync(string accession)
    {
        var result = new List<PointVariant>();

        string url = $"{Base}/{accession}?format=json";
        var (entry, outcome) = await ResilientJson.GetAsync<UniProtEntry>(http, url, "UniProt-variants");
        if (entry is null || outcome is FetchOutcome.Failed or FetchOutcome.NotFound) return result;

        foreach (var f in entry.Features ?? [])
        {
            if (!string.Equals(f.Type, "Natural variant", StringComparison.OrdinalIgnoreCase)) continue;

            int start = f.Location?.Start?.Value ?? 0;
            int end = f.Location?.End?.Value ?? start;
            if (start <= 0 || end != start) continue;   // single-residue positions only

            string orig = f.AlternativeSequence?.OriginalSequence ?? "";
            var alts = f.AlternativeSequence?.AlternativeSequences;
            if (orig.Length != 1 || alts is null) continue;

            char from = char.ToUpper(orig[0]);
            if (!AminoAcidData.ResidueFormulas.TryGetValue(from, out var fromFormula)) continue;

            foreach (var altSeq in alts)
            {
                if (string.IsNullOrEmpty(altSeq) || altSeq.Length != 1) continue;
                char to = char.ToUpper(altSeq[0]);
                if (to == from) continue;
                if (!AminoAcidData.ResidueFormulas.TryGetValue(to, out var toFormula)) continue;

                result.Add(new PointVariant
                {
                    Position = start,
                    From = from,
                    To = to,
                    MassDelta = AminoAcidData.AverageMass(toFormula) - AminoAcidData.AverageMass(fromFormula),
                    Description = f.Description ?? ""
                });
            }
        }

        RunManifest.Record($"UniProt variants {accession}: {result.Count} single-residue substitution(s) ({outcome})");
        return result;
    }

    // Only genuine chemical PTM feature types from UniProt.
    // Signal/transit/propeptide are excluded: their description fields contain
    // functional notes (e.g. "No nuclear targeting of...") not modification names.
    private static bool IsModFeature(string t) => t is
        "Modified residue" or "Glycosylation" or "Lipidation" or
        "Cross-link" or "Disulfide bond";

    // Guard against any description that is clearly not a modification name:
    // real PTM names are short; sentences are noise from annotation free-text.
    private static bool IsValidModName(string name) =>
        name.Length <= 80 && !name.Contains(". ") && !name.Contains("; No ") &&
        !name.StartsWith("In ") && !name.StartsWith("No ");

    // Best-effort delta lookup for common UniProt PTM descriptions
    private static double KnownDeltaFromName(string name)
    {
        string n = name.ToLower();
        if (n.Contains("phospho"))           return 79.96633;
        if (n.Contains("methyl") && n.Contains("tri"))  return 42.04695;
        if (n.Contains("dimethyl"))          return 28.03130;
        if (n.Contains("methyl"))            return 14.01565;
        if (n.Contains("acetyl"))            return 42.01057;
        if (n.Contains("ubiquitin"))         return 114.04293;
        if (n.Contains("sumo"))              return 484.22817;
        if (n.Contains("neddyl"))            return 114.04293;
        if (n.Contains("glcnac") || n.Contains("o-glcnac"))  return 203.07937;
        if (n.Contains("glycosyl") || n.Contains("hex"))     return 162.05282;
        if (n.Contains("hydroxyl"))          return 15.99491;
        if (n.Contains("oxidat"))            return 15.99491;
        if (n.Contains("deamidat"))          return 0.98402;
        if (n.Contains("citrullin"))         return 0.98402;
        if (n.Contains("formyl"))            return 27.99491;
        if (n.Contains("succinyl"))          return 100.01604;
        if (n.Contains("malonyl"))           return 86.00039;
        if (n.Contains("crotonyl"))          return 68.02621;
        if (n.Contains("glutaryl"))          return 114.03169;
        if (n.Contains("butyryl"))           return 70.04187;
        if (n.Contains("lactyl"))            return 72.02113;
        if (n.Contains("palmitoyl"))         return 238.22966;
        if (n.Contains("myristoyl"))         return 210.19836;
        if (n.Contains("farnesyl"))          return 204.18780;
        if (n.Contains("geranylgeranyl"))    return 272.25040;
        if (n.Contains("propionyl"))         return 56.02621;
        if (n.Contains("sulfat"))            return 79.95682;
        if (n.Contains("nitros"))            return 28.99020;
        if (n.Contains("nitrat"))            return 44.98508;
        if (n.Contains("pyroglutam"))        return -17.02655;
        if (n.Contains("amidation"))         return -0.98402;
        if (n.Contains("disulfide") || n.Contains("cross-link")) return -2.01565;
        return 0.0;
    }
}

// ── JSON model for UniProt REST API response ──────────────────────────────

file class UniProtEntry
{
    [JsonPropertyName("sequence")]
    public UniProtSequence? Sequence { get; set; }

    [JsonPropertyName("features")]
    public List<UniProtFeature>? Features { get; set; }
}

file class UniProtSequence
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

file class UniProtFeature
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("location")]
    public UniProtLocation? Location { get; set; }

    [JsonPropertyName("alternativeSequence")]
    public UniProtAltSeq? AlternativeSequence { get; set; }
}

file class UniProtAltSeq
{
    [JsonPropertyName("originalSequence")]
    public string? OriginalSequence { get; set; }

    [JsonPropertyName("alternativeSequences")]
    public List<string>? AlternativeSequences { get; set; }
}

file class UniProtLocation
{
    [JsonPropertyName("start")]
    public UniProtPosition? Start { get; set; }

    [JsonPropertyName("end")]
    public UniProtPosition? End { get; set; }
}

file class UniProtPosition
{
    [JsonPropertyName("value")]
    public int Value { get; set; }
}
