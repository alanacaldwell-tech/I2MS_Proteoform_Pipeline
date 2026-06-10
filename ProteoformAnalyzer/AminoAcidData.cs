namespace ProteoformAnalyzer;

public static class AminoAcidData
{
    // ── Residue molecular formulas (after forming peptide bond — water already removed) ──
    public static readonly Dictionary<char, MolecularFormula> ResidueFormulas = new()
    {
        ['A'] = new() { C=3,  H=5,  N=1, O=1 },
        ['R'] = new() { C=6,  H=12, N=4, O=1 },
        ['N'] = new() { C=4,  H=6,  N=2, O=2 },
        ['D'] = new() { C=4,  H=5,  N=1, O=3 },
        ['C'] = new() { C=3,  H=5,  N=1, O=1, S=1 },
        ['E'] = new() { C=5,  H=7,  N=1, O=3 },
        ['Q'] = new() { C=5,  H=8,  N=2, O=2 },
        ['G'] = new() { C=2,  H=3,  N=1, O=1 },
        ['H'] = new() { C=6,  H=7,  N=3, O=1 },
        ['I'] = new() { C=6,  H=11, N=1, O=1 },
        ['L'] = new() { C=6,  H=11, N=1, O=1 },
        ['K'] = new() { C=6,  H=12, N=2, O=1 },
        ['M'] = new() { C=5,  H=9,  N=1, O=1, S=1 },
        ['F'] = new() { C=9,  H=9,  N=1, O=1 },
        ['P'] = new() { C=5,  H=7,  N=1, O=1 },
        ['S'] = new() { C=3,  H=5,  N=1, O=2 },
        ['T'] = new() { C=4,  H=7,  N=1, O=2 },
        ['W'] = new() { C=11, H=10, N=2, O=1 },
        ['Y'] = new() { C=9,  H=9,  N=1, O=2 },
        ['V'] = new() { C=5,  H=9,  N=1, O=1 },
    };

    // ── Average atomic masses (IUPAC 2021) ──
    public const double AvgH = 1.00794;
    public const double AvgC = 12.01070;
    public const double AvgN = 14.00670;
    public const double AvgO = 15.99940;
    public const double AvgS = 32.06500;
    public const double AvgP = 30.97376;  // P is monoisotopic (100% P-31)

    // H2O added to complete termini
    public const double WaterAvg = 2 * AvgH + AvgO; // 18.01528

    public static MolecularFormula GetFormula(string sequence)
    {
        var f = new MolecularFormula { H = 2, O = 1 }; // H2O for termini
        foreach (char aa in sequence.ToUpper())
            if (ResidueFormulas.TryGetValue(aa, out var r))
                f = f + r;
        return f;
    }

    public static double AverageMass(MolecularFormula f) =>
        f.C * AvgC + f.H * AvgH + f.N * AvgN + f.O * AvgO + f.S * AvgS + f.P * AvgP;

    public static bool IsValidSequence(string seq) =>
        !string.IsNullOrEmpty(seq) && seq.ToUpper().All(c => ResidueFormulas.ContainsKey(c));
}
