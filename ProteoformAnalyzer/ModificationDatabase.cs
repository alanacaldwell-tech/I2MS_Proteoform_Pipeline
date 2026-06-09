namespace ProteoformAnalyzer;

public class ModificationDatabase
{
    public List<Modification> Modifications { get; } = new();
    public List<Modification> CustomModifications { get; } = new();

    public ModificationDatabase()
    {
        LoadBuiltinModifications();
    }

    private void LoadBuiltinModifications()
    {
        // --- Phosphorylation ---
        Add("Phosphorylation", "Phosphorylation", +79.96633, 'S', 'T', 'Y');

        // --- Methylation ---
        Add("Monomethylation", "Methylation", +14.01565, 'K', 'R');
        Add("Dimethylation", "Methylation", +28.03130, 'K', 'R');
        Add("Trimethylation", "Methylation", +42.04695, 'K');
        Add("Arginine symmetric dimethylation (SDMA)", "Methylation", +28.03130, 'R');
        Add("Arginine asymmetric dimethylation (ADMA)", "Methylation", +28.03130, 'R');

        // --- Acetylation ---
        Add("Acetylation", "Acetylation", +42.01057, 'K');
        AddNTerm("N-terminal acetylation", "Acetylation", +42.01057);

        // --- Ubiquitination ---
        Add("Ubiquitination (GlyGly remnant)", "Ubiquitination", +114.04293, 'K');
        Add("SUMOylation (EQTGG remnant)", "SUMOylation", +484.22817, 'K');
        Add("NEDDylation (GG remnant)", "NEDDylation", +114.04293, 'K');

        // --- Glycosylation ---
        Add("N-glycosylation (core HexNAc)", "Glycosylation", +203.07937, 'N');
        Add("O-GlcNAc (HexNAc)", "Glycosylation", +203.07937, 'S', 'T');
        Add("O-Fucosylation (Hex)", "Glycosylation", +162.05282, 'S', 'T');
        Add("O-Mannose (Hex)", "Glycosylation", +162.05282, 'S', 'T');
        Add("O-GalNAc core 1 (Tn antigen)", "Glycosylation", +203.07937, 'S', 'T');
        Add("Hex2HexNAc (complex N-glycan stub)", "Glycosylation", +527.19559, 'N');
        Add("HexNAc2 (N-glycan core)", "Glycosylation", +406.15874, 'N');
        Add("Sialylation (NeuAc)", "Glycosylation", +291.09542, 'N', 'S', 'T');

        // --- Oxidation / Reduction ---
        Add("Oxidation", "Oxidation", +15.99491, 'M', 'W', 'C', 'H', 'P');
        Add("Dioxidation (sulfonation)", "Oxidation", +31.98983, 'M', 'W', 'C');
        Add("Trioxidation", "Oxidation", +47.98474, 'C');
        Add("Carbamidomethylation (IAA alkylation)", "Alkylation", +57.02146, 'C');
        Add("Propionamide (acrylamide adduct)", "Alkylation", +71.03711, 'C');

        // --- Deamidation ---
        Add("Deamidation", "Deamidation", +0.98402, 'N', 'Q');

        // --- Hydroxylation ---
        Add("Hydroxylation", "Hydroxylation", +15.99491, 'P', 'K', 'R', 'N', 'D', 'W', 'F', 'Y');

        // --- Formylation ---
        Add("Formylation", "Formylation", +27.99491, 'K', 'S', 'T');
        AddNTerm("N-terminal formylation", "Formylation", +27.99491);

        // --- Propionylation ---
        Add("Propionylation", "Propionylation", +56.02621, 'K');

        // --- Crotonylation ---
        Add("Crotonylation", "Crotonylation", +68.02621, 'K');

        // --- Succinylation ---
        Add("Succinylation", "Succinylation", +100.01604, 'K');

        // --- Malonylation ---
        Add("Malonylation", "Malonylation", +86.00039, 'K');

        // --- Glutarylation ---
        Add("Glutarylation", "Glutarylation", +114.03169, 'K');

        // --- 2-Hydroxyisobutyrylation ---
        Add("2-Hydroxyisobutyrylation", "Acylation", +86.03678, 'K');

        // --- Butyrylation ---
        Add("Butyrylation", "Acylation", +70.04187, 'K');

        // --- Lactylation ---
        Add("Lactylation", "Acylation", +72.02113, 'K');

        // --- Palmitoylation ---
        Add("Palmitoylation", "Lipidation", +238.22966, 'C', 'K', 'S');

        // --- Myristoylation ---
        AddNTerm("N-terminal myristoylation", "Lipidation", +210.19836);
        Add("Myristoylation", "Lipidation", +210.19836, 'K');

        // --- Farnesylation ---
        Add("Farnesylation", "Lipidation", +204.18780, 'C');

        // --- Geranylgeranylation ---
        Add("Geranylgeranylation", "Lipidation", +272.25040, 'C');

        // --- GPI anchor ---
        Add("GPI anchor (EtNP)", "Lipidation", +141.01921, 'N'); // simplified

        // --- ADP-ribosylation ---
        Add("Mono-ADP-ribosylation", "ADP-ribosylation", +541.06111, 'E', 'D', 'K', 'R', 'N', 'Q', 'S', 'T', 'C');
        Add("Poly-ADP-ribosylation (n=2)", "ADP-ribosylation", +1040.08742, 'E', 'D', 'K', 'R');

        // --- Neddylation / ISGylation ---
        Add("ISGylation (GG remnant)", "Ubiquitin-like", +114.04293, 'K');

        // --- Citrullination ---
        Add("Citrullination (deimination)", "Deimination", +0.98402, 'R');

        // --- Nitrosylation ---
        Add("Nitrosylation (S-nitrosylation)", "Nitrosylation", +28.99020, 'C');
        Add("Tyrosine nitration", "Nitration", +44.98508, 'Y');

        // --- Sulfation ---
        Add("Sulfation", "Sulfation", +79.95682, 'Y', 'S', 'T');

        // --- Disulfide / Cysteine modifications ---
        Add("Glutathionylation", "Cysteine mod", +305.06820, 'C');
        Add("Cysteinylation", "Cysteine mod", +119.00418, 'C');

        // --- Pyroglutamate ---
        AddNTerm("N-terminal pyroglutamate from Glu", "Cyclization", -18.01056, 'E');
        AddNTerm("N-terminal pyroglutamate from Gln", "Cyclization", -17.02655, 'Q');

        // --- Amidation ---
        AddCTerm("C-terminal amidation", "Amidation", -0.98402);

        // --- Methylester ---
        AddCTerm("C-terminal methyl ester", "Esterification", +14.01565);

        // --- Proteolytic cleavage artifacts ---
        Add("Met-oxidation (common artifact)", "Artifact", +15.99491, 'M');
    }

    private void Add(string name, string category, double delta, params char[] residues)
    {
        Modifications.Add(new Modification
        {
            Name = name,
            Category = category,
            MassDelta = delta,
            TargetResidues = residues.ToList()
        });
    }

    private void AddNTerm(string name, string category, double delta, params char[] residues)
    {
        Modifications.Add(new Modification
        {
            Name = name,
            Category = category,
            MassDelta = delta,
            TargetResidues = residues.ToList(),
            IsNTerminal = true
        });
    }

    private void AddCTerm(string name, string category, double delta, params char[] residues)
    {
        Modifications.Add(new Modification
        {
            Name = name,
            Category = category,
            MassDelta = delta,
            TargetResidues = residues.ToList(),
            IsCTerminal = true
        });
    }

    public void AddCustomModification(Modification mod)
    {
        mod.IsCustom = true;
        CustomModifications.Add(mod);
        Modifications.Add(mod);
    }

    public IEnumerable<Modification> GetApplicable(string sequence)
    {
        foreach (var mod in Modifications)
        {
            if (mod.IsNTerminal)
            {
                // Applies if no specific residue required, or if N-terminal residue matches
                if (!mod.TargetResidues.Any() || mod.TargetResidues.Contains(char.ToUpper(sequence[0])))
                    yield return mod;
            }
            else if (mod.IsCTerminal)
            {
                if (!mod.TargetResidues.Any() || mod.TargetResidues.Contains(char.ToUpper(sequence[^1])))
                    yield return mod;
            }
            else
            {
                // Applies if any residue in the sequence matches
                if (mod.TargetResidues.Any(r => sequence.ToUpper().Contains(r)))
                    yield return mod;
            }
        }
    }
}
