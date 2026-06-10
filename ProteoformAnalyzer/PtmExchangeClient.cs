using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ProteoformAnalyzer;

/// <summary>
/// Queries PTM data via two complementary EBI endpoints:
///
///  1. EBI Proteins API — proteomics-ptm endpoint (PTMeXchange-indexed data):
///     GET https://www.ebi.ac.uk/proteins/api/proteomics-ptm/{accession}
///
///  2. EBI Proteins API — PTM/processing features from UniProt annotations
///     (supplementary to the dedicated UniProt client).
/// </summary>
public class PtmExchangeClient(HttpClient http)
{
    private const string ProteomicsPtmBase = "https://www.ebi.ac.uk/proteins/api/proteomics-ptm";
    private const string ProteinsBase      = "https://www.ebi.ac.uk/proteins/api/proteins";

    public async Task<List<PtmAnnotation>> FetchAsync(string uniprotAccession, string proteinSequence)
    {
        Console.WriteLine($"  [PTMeXchange/EBI] Querying {uniprotAccession}...");
        var ptms = new List<PtmAnnotation>();

        ptms.AddRange(await FetchProteomicsPtmAsync(uniprotAccession, proteinSequence));
        ptms.AddRange(await FetchProteinsFeaturesAsync(uniprotAccession, proteinSequence));

        var deduped = ptms
            .GroupBy(p => (p.ModificationName.ToLower(), p.Position))
            .Select(g => g.First())
            .ToList();

        Console.WriteLine($"  [PTMeXchange/EBI] Found {deduped.Count} unique PTM sites.");
        return deduped;
    }

    private async Task<List<PtmAnnotation>> FetchProteomicsPtmAsync(
        string accession, string sequence)
    {
        var result = new List<PtmAnnotation>();
        try
        {
            var resp = await http.GetAsync(
                $"{ProteomicsPtmBase}/{Uri.EscapeDataString(accession)}");
            if (!resp.IsSuccessStatusCode) return result;

            var payload = await resp.Content
                .ReadFromJsonAsync<List<EbiPtmEntry>>();
            if (payload is null) return result;

            foreach (var entry in payload)
            {
                if (entry.PtmType is null) continue;
                string name = entry.PtmType;
                int pos = entry.Position;
                double delta = entry.MassDelta ?? DeltaFromName(name);

                char? residue = pos > 0 && pos <= sequence.Length
                    ? char.ToUpper(sequence[pos - 1]) : null;

                result.Add(new PtmAnnotation
                {
                    ModificationName = name,
                    Position = pos,
                    Residue = residue,
                    MassDelta = delta,
                    Source = "PTMeXchange"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [PTMeXchange] proteomics-ptm endpoint error: {ex.Message}");
        }
        return result;
    }

    private async Task<List<PtmAnnotation>> FetchProteinsFeaturesAsync(
        string accession, string sequence)
    {
        var result = new List<PtmAnnotation>();
        try
        {
            // Request only PTM/processing feature categories
            string url = $"{ProteinsBase}/{Uri.EscapeDataString(accession)}" +
                         "?categories=PTM_PROCESSING";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return result;

            var payload = await resp.Content
                .ReadFromJsonAsync<EbiProteinEntry>();
            if (payload?.Features is null) return result;

            foreach (var feat in payload.Features)
            {
                if (feat.Type is null) continue;

                // Accept only true chemical modification feature types;
                // exclude Region, Chain, Peptide, Compositional bias, etc.
                if (!IsPtmType(feat.Type)) continue;

                int pos = feat.Begin ?? 0;
                string name = feat.Description ?? feat.Type;

                // Skip free-text sentences that are not modification names
                if (!IsValidModName(name)) continue;

                double delta = DeltaFromName(name);

                char? residue = pos > 0 && pos <= sequence.Length
                    ? char.ToUpper(sequence[pos - 1]) : null;

                result.Add(new PtmAnnotation
                {
                    ModificationName = name,
                    Position = pos,
                    Residue = residue,
                    MassDelta = delta,
                    Source = "EBI-Proteins"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [EBI-Proteins] features endpoint error: {ex.Message}");
        }
        return result;
    }

    private static bool IsPtmType(string t) => t is
        "Modified residue" or "Glycosylation" or "Lipidation" or
        "Cross-link" or "Disulfide bond";

    private static bool IsValidModName(string name) =>
        name.Length <= 80 && !name.Contains(". ") && !name.Contains("; No ") &&
        !name.StartsWith("In ") && !name.StartsWith("No ");

    private static double DeltaFromName(string name)
    {
        string n = name.ToLower();
        if (n.Contains("phospho"))        return 79.96633;
        if (n.Contains("trimethyl"))      return 42.04695;
        if (n.Contains("dimethyl"))       return 28.03130;
        if (n.Contains("methyl"))         return 14.01565;
        if (n.Contains("acetyl"))         return 42.01057;
        if (n.Contains("ubiquitin"))      return 114.04293;
        if (n.Contains("glyco") || n.Contains("hex")) return 162.05282;
        if (n.Contains("hydroxyl") || n.Contains("oxidat")) return 15.99491;
        if (n.Contains("deamidat"))       return 0.98402;
        if (n.Contains("succinyl"))       return 100.01604;
        if (n.Contains("crotonyl"))       return 68.02621;
        if (n.Contains("palmitoyl"))      return 238.22966;
        if (n.Contains("myristoyl"))      return 210.19836;
        if (n.Contains("farnesyl"))       return 204.18780;
        return 0.0;
    }
}

// ── JSON models ───────────────────────────────────────────────────────────

file class EbiPtmEntry
{
    [JsonPropertyName("ptmType")]
    public string? PtmType { get; set; }

    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("massDelta")]
    public double? MassDelta { get; set; }
}

file class EbiProteinEntry
{
    [JsonPropertyName("features")]
    public List<EbiFeature>? Features { get; set; }
}

file class EbiFeature
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("begin")]
    public int? Begin { get; set; }
}
