using System.Text.Json.Serialization;

namespace ProteoformAnalyzer;

/// <summary>
/// Queries the PRIDE Archive REST API v2 for peptide-level PTM evidence
/// associated with a UniProt accession.
/// Endpoint: GET /peptideevidences?proteinAccession={acc}&amp;pageSize=100
/// </summary>
public class PrideClient(HttpClient http)
{
    private const string Base = "https://www.ebi.ac.uk/pride/ws/archive/v2";

    public async Task<List<PtmAnnotation>> FetchAsync(string uniprotAccession, string proteinSequence)
    {
        Console.WriteLine($"  [PRIDE] Querying experimental PTMs for {uniprotAccession}...");
        var ptms = new List<PtmAnnotation>();

        try
        {
            // Page through up to 3 pages of 100 peptides each
            for (int page = 0; page < 3; page++)
            {
                string url = $"{Base}/peptideevidences" +
                             $"?proteinAccession={Uri.EscapeDataString(uniprotAccession)}" +
                             $"&pageSize=100&page={page}";

                var (payload, outcome) = await ResilientJson.GetAsync<PrideEvidenceResponse>(http, url, "PRIDE");
                if (outcome is FetchOutcome.Failed or FetchOutcome.NotFound) break;
                if (payload?.PeptideEvidenceList is null || payload.PeptideEvidenceList.Count == 0)
                    break;

                foreach (var pep in payload.PeptideEvidenceList)
                {
                    if (pep.Modifications is null) continue;
                    foreach (var mod in pep.Modifications)
                    {
                        if (string.IsNullOrWhiteSpace(mod.Name)) continue;

                        // Map modification position within peptide back to protein position
                        int proteinPos = MapToProtein(proteinSequence, pep.PeptideSequence, mod.PositionInPeptide);

                        ptms.Add(new PtmAnnotation
                        {
                            ModificationName = mod.Name,
                            Position = proteinPos,
                            Residue = proteinPos > 0 && proteinPos <= proteinSequence.Length
                                ? char.ToUpper(proteinSequence[proteinPos - 1])
                                : null,
                            MassDelta = mod.MonoisotopicMassDelta,
                            Source = "PRIDE"
                        });
                    }
                }

                if (payload.PeptideEvidenceList.Count < 100) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [PRIDE] Error: {ex.Message}");
        }

        // Deduplicate by (name, position)
        var deduped = ptms
            .GroupBy(p => (p.ModificationName, p.Position))
            .Select(g => g.First())
            .ToList();

        Console.WriteLine($"  [PRIDE] Found {deduped.Count} unique PTM sites.");
        RunManifest.Record($"PRIDE {uniprotAccession}: {deduped.Count} unique PTM sites");
        return deduped;
    }

    private static int MapToProtein(string proteinSeq, string? peptideSeq, int posInPeptide)
    {
        if (string.IsNullOrEmpty(peptideSeq) || posInPeptide <= 0) return 0;
        int offset = proteinSeq.IndexOf(peptideSeq, StringComparison.OrdinalIgnoreCase);
        if (offset < 0) return 0;
        return offset + posInPeptide; // 1-based
    }
}

// ── JSON models ───────────────────────────────────────────────────────────

file class PrideEvidenceResponse
{
    [JsonPropertyName("peptideEvidenceList")]
    public List<PridePeptideEvidence>? PeptideEvidenceList { get; set; }
}

file class PridePeptideEvidence
{
    [JsonPropertyName("peptideSequence")]
    public string? PeptideSequence { get; set; }

    [JsonPropertyName("modifications")]
    public List<PrideModification>? Modifications { get; set; }
}

file class PrideModification
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("positionInPeptide")]
    public int PositionInPeptide { get; set; }

    [JsonPropertyName("monoisotopicMassDelta")]
    public double MonoisotopicMassDelta { get; set; }
}
