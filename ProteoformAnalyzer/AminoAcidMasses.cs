namespace ProteoformAnalyzer;

public static class AminoAcidMasses
{
    // Monoisotopic residue masses (Da)
    public static readonly Dictionary<char, double> Residue = new()
    {
        ['A'] = 71.03711,
        ['R'] = 156.10111,
        ['N'] = 114.04293,
        ['D'] = 115.02694,
        ['C'] = 103.00919,
        ['E'] = 129.04259,
        ['Q'] = 128.05858,
        ['G'] = 57.02146,
        ['H'] = 137.05891,
        ['I'] = 113.08406,
        ['L'] = 113.08406,
        ['K'] = 128.09496,
        ['M'] = 131.04049,
        ['F'] = 147.06841,
        ['P'] = 97.05276,
        ['S'] = 87.03203,
        ['T'] = 101.04768,
        ['W'] = 186.07931,
        ['Y'] = 163.06333,
        ['V'] = 99.06841,
    };

    // Water mass added for full peptide/protein mass (N-term H + C-term OH)
    public const double Water = 18.01056;

    public static double CalculateMass(string sequence)
    {
        double mass = Water;
        foreach (char aa in sequence)
        {
            if (Residue.TryGetValue(char.ToUpper(aa), out double m))
                mass += m;
        }
        return mass;
    }

    public static bool IsValidSequence(string sequence) =>
        sequence.All(c => Residue.ContainsKey(char.ToUpper(c)));
}
