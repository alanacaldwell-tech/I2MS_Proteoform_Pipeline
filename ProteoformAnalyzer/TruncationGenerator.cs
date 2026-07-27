namespace ProteoformAnalyzer;

/// <summary>
/// A truncation variant: carries the display name and the truncated molecular formula so
/// ProteoformBuilder can compute accurate isotope envelopes without re-parsing the sequence.
/// N- and C-terminal losses are tracked independently so a single type covers pure N-terminal,
/// pure C-terminal, and internal (both-ends) fragments. The surviving residues are the original
/// 1-based positions (<see cref="NTermRemoved"/>+1) .. (length − <see cref="CTermRemoved"/>).
/// </summary>
public class TruncationVariant
{
    public string Name { get; set; } = "";
    public int NTermRemoved { get; set; }
    public int CTermRemoved { get; set; }
    public MolecularFormula Formula { get; set; } = new();
    public double Mass => AminoAcidData.AverageMass(Formula);
}

public static class TruncationGenerator
{
    /// <summary>
    /// Generates truncation variants for a sequence.
    ///
    /// N- and C-terminal single-ended truncations are always produced. When
    /// <paramref name="includeInternal"/> is set, internal fragments (residues removed from BOTH
    /// termini) are added — this is O(depthN·depthC), so it is gated behind the depth cap.
    /// </summary>
    /// <param name="sequence">The (already region-restricted, if applicable) protein sequence.</param>
    /// <param name="maxTerminusDepth">Maximum residues removable from each terminus. 0 = no cap
    /// (single-ended truncations span the whole sequence). A positive value bounds both the
    /// single-ended depth and the internal-fragment grid.</param>
    /// <param name="includeInternal">Also generate internal (both-ends) fragments.</param>
    /// <param name="minFragmentLength">Drop any fragment shorter than this many residues.</param>
    public static List<TruncationVariant> Generate(
        string sequence,
        int maxTerminusDepth = 0,
        bool includeInternal = false,
        int minFragmentLength = 1)
    {
        var result = new List<TruncationVariant>();
        int len = sequence.Length;
        if (len <= 1) return result;

        int cap = maxTerminusDepth > 0 ? Math.Min(maxTerminusDepth, len - 1) : len - 1;
        int minLen = Math.Max(1, minFragmentLength);

        // N-terminal: remove first i residues (protein starts at position i+1)
        for (int i = 1; i <= cap; i++)
        {
            if (len - i < minLen) break;
            result.Add(new TruncationVariant
            {
                Name = $"N-terminal truncation (-{i} residue{(i > 1 ? "s" : "")})",
                NTermRemoved = i,
                Formula = AminoAcidData.GetFormula(sequence[i..])
            });
        }

        // C-terminal: remove last j residues (protein ends at position len-j)
        for (int j = 1; j <= cap; j++)
        {
            if (len - j < minLen) break;
            result.Add(new TruncationVariant
            {
                Name = $"C-terminal truncation (-{j} residue{(j > 1 ? "s" : "")})",
                CTermRemoved = j,
                Formula = AminoAcidData.GetFormula(sequence[..^j])
            });
        }

        // Internal fragments: remove i from the N-terminus AND j from the C-terminus.
        if (includeInternal)
        {
            for (int i = 1; i <= cap; i++)
            {
                for (int j = 1; j <= cap; j++)
                {
                    int fragLen = len - i - j;
                    if (fragLen < minLen) continue;
                    result.Add(new TruncationVariant
                    {
                        Name = $"N-terminal truncation (-{i} residue{(i > 1 ? "s" : "")}) + " +
                               $"C-terminal truncation (-{j} residue{(j > 1 ? "s" : "")})",
                        NTermRemoved = i,
                        CTermRemoved = j,
                        Formula = AminoAcidData.GetFormula(sequence.Substring(i, fragLen))
                    });
                }
            }
        }

        return result;
    }
}
