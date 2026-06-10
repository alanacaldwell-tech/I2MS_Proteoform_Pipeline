namespace ProteoformAnalyzer;

/// <summary>
/// A truncation variant: carries both the display name and the truncated
/// molecular formula so ProteoformBuilder can compute accurate isotope
/// envelopes for truncation+PTM combinations without re-parsing the sequence.
/// </summary>
public class TruncationVariant
{
    public string Name { get; set; } = "";
    public bool IsNTerminal { get; set; }
    public int ResiduesToRemove { get; set; }
    public MolecularFormula Formula { get; set; } = new();
    public double Mass => AminoAcidData.AverageMass(Formula);
}

public static class TruncationGenerator
{
    /// <summary>
    /// Generates all N- and C-terminal truncations for a given sequence.
    /// Intact formula is pre-computed once and passed in to avoid redundant work.
    /// </summary>
    public static List<TruncationVariant> Generate(string sequence)
    {
        var result = new List<TruncationVariant>();
        int len = sequence.Length;

        // N-terminal: remove first i residues (protein starts at position i+1)
        for (int i = 1; i < len; i++)
        {
            result.Add(new TruncationVariant
            {
                Name = $"N-terminal truncation (-{i} residue{(i > 1 ? "s" : "")})",
                IsNTerminal = true,
                ResiduesToRemove = i,
                Formula = AminoAcidData.GetFormula(sequence[i..])
            });
        }

        // C-terminal: remove last i residues (protein ends at position len-i)
        for (int i = 1; i < len; i++)
        {
            result.Add(new TruncationVariant
            {
                Name = $"C-terminal truncation (-{i} residue{(i > 1 ? "s" : "")})",
                IsNTerminal = false,
                ResiduesToRemove = i,
                Formula = AminoAcidData.GetFormula(sequence[..^i])
            });
        }

        return result;
    }
}
