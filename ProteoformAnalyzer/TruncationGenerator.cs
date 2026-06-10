namespace ProteoformAnalyzer;

public static class TruncationGenerator
{
    /// <summary>
    /// Generates N- and C-terminal truncation proteoforms.
    /// Each truncation removes between 1 and (length-1) residues from one terminus.
    /// </summary>
    public static List<PtmAnnotation> Generate(string sequence)
    {
        var truncations = new List<PtmAnnotation>();
        int len = sequence.Length;

        // N-terminal truncations: remove first i residues → protein starts at i+1
        for (int i = 1; i < len; i++)
        {
            string truncSeq = sequence[i..];
            double intactMass = AminoAcidData.AverageMass(AminoAcidData.GetFormula(sequence));
            double truncMass  = AminoAcidData.AverageMass(AminoAcidData.GetFormula(truncSeq));

            truncations.Add(new PtmAnnotation
            {
                ModificationName = $"N-terminal truncation (remove residues 1–{i})",
                Position = 0,
                MassDelta = truncMass - intactMass,
                Source = "Computed",
                IsNTerminalTruncation = true,
                TruncationLength = i
            });
        }

        // C-terminal truncations: remove last i residues → protein ends at len-i
        for (int i = 1; i < len; i++)
        {
            string truncSeq = sequence[..^i];
            double intactMass = AminoAcidData.AverageMass(AminoAcidData.GetFormula(sequence));
            double truncMass  = AminoAcidData.AverageMass(AminoAcidData.GetFormula(truncSeq));

            truncations.Add(new PtmAnnotation
            {
                ModificationName = $"C-terminal truncation (remove residues {len - i + 1}–{len})",
                Position = 0,
                MassDelta = truncMass - intactMass,
                Source = "Computed",
                IsCTerminalTruncation = true,
                TruncationLength = i
            });
        }

        return truncations;
    }
}
