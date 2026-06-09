namespace ProteoformAnalyzer;

public static class TruncationGenerator
{
    /// <summary>
    /// Generates all N- and C-terminal truncations for a given sequence.
    /// Each truncation removes 1..n-1 residues from the respective terminus.
    /// The description records the mass delta relative to the intact sequence.
    /// </summary>
    public static List<Truncation> Generate(string sequence)
    {
        var truncations = new List<Truncation>();
        int len = sequence.Length;
        double intactMass = AminoAcidMasses.CalculateMass(sequence);

        // N-terminal truncations: remove residues 0..i-1
        for (int i = 1; i < len; i++)
        {
            string truncSeq = sequence[i..];
            double truncMass = AminoAcidMasses.CalculateMass(truncSeq);
            truncations.Add(new Truncation
            {
                Name = $"N-terminal truncation (-{i} residue{(i > 1 ? "s" : "")}, starts at pos {i + 1})",
                IsNTerminal = true,
                ResiduesToRemove = i,
                MassDelta = truncMass - intactMass
            });
        }

        // C-terminal truncations: remove residues len-i..len-1
        for (int i = 1; i < len; i++)
        {
            string truncSeq = sequence[..^i];
            double truncMass = AminoAcidMasses.CalculateMass(truncSeq);
            truncations.Add(new Truncation
            {
                Name = $"C-terminal truncation (-{i} residue{(i > 1 ? "s" : "")}, ends at pos {len - i})",
                IsNTerminal = false,
                ResiduesToRemove = i,
                MassDelta = truncMass - intactMass
            });
        }

        return truncations;
    }
}
