namespace ProteoformAnalyzer;

public class Modification
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double MassDelta { get; set; }
    // Residues this modification can occur on; empty = any (for terminal mods)
    public List<char> TargetResidues { get; set; } = new();
    public bool IsNTerminal { get; set; }
    public bool IsCTerminal { get; set; }
    public bool IsCustom { get; set; }

    public override string ToString() => $"{Name} ({(MassDelta >= 0 ? "+" : "")}{MassDelta:F4} Da)";
}

public class Truncation
{
    public string Name { get; set; } = "";
    public bool IsNTerminal { get; set; }
    public int ResiduesToRemove { get; set; }
    public double MassDelta { get; set; }

    public override string ToString() => $"{Name} ({(MassDelta >= 0 ? "+" : "")}{MassDelta:F4} Da)";
}

public class Proteoform
{
    public string Description { get; set; } = "";
    public double Mass { get; set; }
    public double Tolerance { get; set; } = 5.0;

    public string MassFormatted => $"{Mass:F4}";
    public string ToleranceFormatted => $"+/- {Tolerance:F1}";
}
