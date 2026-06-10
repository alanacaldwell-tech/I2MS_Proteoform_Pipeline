namespace ProteoformAnalyzer;

public static class ProteoformBuilder
{
    public static List<ProteoformEntry> Build(
        string sequence,
        List<PtmAnnotation> allPtms,
        bool includeTruncations,
        double tolerance)
    {
        var entries = new List<ProteoformEntry>();
        var intactFormula = AminoAcidData.GetFormula(sequence);
        var intactEnvelope = IsotopeCalculator.Compute(intactFormula);

        // ── 1. Intact, unmodified proteoform ──────────────────────────────
        entries.Add(new ProteoformEntry
        {
            ModificationName = "Unmodified (intact)",
            CentroidMass = intactEnvelope.Centroid,
            Tolerance = tolerance,
            Envelope = intactEnvelope
        });

        // ── 2. Group PTMs by modification family across all sources ───────
        // Each family produces mono/di/tri/... proteoforms based on the
        // number of distinct sites found in the database annotations.
        var byFamily = allPtms
            .Where(p => !p.IsNTerminalTruncation && !p.IsCTerminalTruncation)
            // First collapse identical sites (same family + same position)
            .GroupBy(p => (ModFamily(p.ModificationName), p.Position))
            .Select(g =>
            {
                var best = g.Where(p => p.MassDelta != 0)
                             .OrderByDescending(p => Math.Abs(p.MassDelta))
                             .FirstOrDefault() ?? g.First();
                best.ModificationName = g.OrderByDescending(p => p.ModificationName.Length)
                                         .First().ModificationName;
                return best;
            })
            // Then group by family to get all sites for each mod type
            .GroupBy(p => ModFamily(p.ModificationName))
            .ToList();

        foreach (var familyGroup in byFamily)
        {
            var sites = familyGroup.ToList();
            int siteCount = sites.Count;

            // Representative delta and display name for this family
            double delta = sites
                .Where(p => p.MassDelta != 0)
                .OrderByDescending(p => Math.Abs(p.MassDelta))
                .Select(p => p.MassDelta)
                .FirstOrDefault();

            string baseName = sites
                .OrderByDescending(p => p.ModificationName.Length)
                .First().ModificationName;

            // Generate one proteoform per occupancy level (×1 through ×N)
            for (int k = 1; k <= siteCount; k++)
            {
                double totalDelta = delta * k;
                var envelope = IsotopeCalculator.ComputeFromDelta(intactFormula, totalDelta);

                entries.Add(new ProteoformEntry
                {
                    ModificationName = FormatMultiplicity(baseName, familyGroup.Key, k, siteCount),
                    CentroidMass = envelope.Centroid,
                    Tolerance = tolerance,
                    Envelope = envelope
                });
            }
        }

        // ── 3. Truncations ────────────────────────────────────────────────
        if (includeTruncations && sequence.Length > 1)
        {
            var truncations = TruncationGenerator.Generate(sequence);
            foreach (var trunc in truncations)
            {
                var envelope = IsotopeCalculator.ComputeFromDelta(intactFormula, trunc.MassDelta);
                entries.Add(new ProteoformEntry
                {
                    ModificationName = trunc.ModificationName,
                    CentroidMass = envelope.Centroid,
                    Tolerance = tolerance,
                    Envelope = envelope
                });
            }
        }

        return entries;
    }

    /// <summary>
    /// Formats the modification name with a multiplicity prefix.
    /// k=1 of 1 site  → original name unchanged
    /// k=1 of N sites → "Mono-[family] (1 of N sites)"
    /// k=2            → "Di-[family] (2 of N sites)"
    /// k=3            → "Tri-[family] (3 of N sites)"
    /// k≥4            → "4× [family] (4 of N sites)"
    /// </summary>
    private static string FormatMultiplicity(string baseName, string family, int k, int total)
    {
        if (total == 1) return baseName;  // only one site — no multiplicity prefix needed

        string prefix = k switch
        {
            1 => "Mono",
            2 => "Di",
            3 => "Tri",
            4 => "Tetra",
            5 => "Penta",
            6 => "Hexa",
            _ => $"{k}×"
        };

        // Capitalise family for display
        string display = char.ToUpper(family[0]) + family[1..];
        return $"{prefix}-{display} ({k} of {total} sites)";
    }

    /// <summary>
    /// Maps any modification name to a canonical family string used for grouping.
    /// </summary>
    public static string ModFamily(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("phospho"))                                              return "phosphorylation";
        if (n.Contains("trimethyl") || (n.Contains("methyl") && n.Contains("tri"))) return "methylation";
        if (n.Contains("dimethyl")  || (n.Contains("methyl") && n.Contains("di")))  return "methylation";
        if (n.Contains("methyl"))                                               return "methylation";
        if (n.Contains("acetyl"))                                               return "acetylation";
        if (n.Contains("ubiquitin") || n.Contains("glygly") || n.Contains("gg-"))   return "ubiquitination";
        if (n.Contains("sumo"))                                                 return "sumoylation";
        if (n.Contains("nedd"))                                                 return "neddylation";
        if (n.Contains("glyco") || n.Contains("hexnac") || n.Contains("hex"))  return "glycosylation";
        if (n.Contains("hydroxyl"))                                             return "hydroxylation";
        if (n.Contains("oxidat"))                                               return "oxidation";
        if (n.Contains("deamidat") || n.Contains("deamin"))                    return "deamidation";
        if (n.Contains("citrullin") || n.Contains("deiminat"))                 return "citrullination";
        if (n.Contains("succinyl"))                                             return "succinylation";
        if (n.Contains("malonyl"))                                              return "malonylation";
        if (n.Contains("glutaryl"))                                             return "glutarylation";
        if (n.Contains("crotonyl"))                                             return "crotonylation";
        if (n.Contains("butyryl"))                                              return "butyrylation";
        if (n.Contains("lactyl"))                                               return "lactylation";
        if (n.Contains("propionyl"))                                            return "propionylation";
        if (n.Contains("palmitoyl"))                                            return "palmitoylation";
        if (n.Contains("myristoyl"))                                            return "myristoylation";
        if (n.Contains("farnesyl"))                                             return "farnesylation";
        if (n.Contains("geranylgeranyl"))                                       return "geranylgeranylation";
        if (n.Contains("sulfat"))                                               return "sulfation";
        if (n.Contains("nitros"))                                               return "nitrosylation";
        if (n.Contains("nitrat"))                                               return "nitration";
        if (n.Contains("adp-ribosyl") || n.Contains("adpribosyl"))             return "adp-ribosylation";
        if (n.Contains("pyroglutam"))                                           return "pyroglutamylation";
        if (n.Contains("amidation") || n.Contains("amidat"))                   return "amidation";
        if (n.Contains("disulfide") || n.Contains("cross-link"))               return "disulfide";
        if (n.Contains("formyl"))                                               return "formylation";
        return n.Trim();
    }
}
