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

        // ── 1. Intact, unmodified ─────────────────────────────────────────
        entries.Add(new ProteoformEntry
        {
            ModificationName = "Unmodified (intact)",
            CentroidMass = intactEnvelope.Centroid,
            Tolerance = tolerance,
            Envelope = intactEnvelope
        });

        // ── 2. Build PTM family groups (shared by PTM-only and combinations)
        var ptmFamilies = BuildFamilyGroups(allPtms);

        // ── 3. PTM-only proteoforms (no truncation) ───────────────────────
        foreach (var family in ptmFamilies)
            foreach (var entry in MakePtmEntries(family, intactFormula, tolerance))
                entries.Add(entry);

        // ── 4. Truncations and truncation+PTM combinations ────────────────
        if (includeTruncations && sequence.Length > 1)
        {
            var truncations = TruncationGenerator.Generate(sequence);

            int combinationCount = truncations.Count
                * (1 + ptmFamilies.Sum(f => f.SiteCount));
            if (combinationCount > 50_000)
                Console.WriteLine($"  [Warning] Generating {combinationCount:N0} truncation+PTM " +
                                  "combination rows — this may take a moment.");

            foreach (var trunc in truncations)
            {
                // 4a. Truncation alone
                var truncEnvelope = IsotopeCalculator.Compute(trunc.Formula);
                entries.Add(new ProteoformEntry
                {
                    ModificationName = trunc.Name,
                    CentroidMass = truncEnvelope.Centroid,
                    Tolerance = tolerance,
                    Envelope = truncEnvelope
                });

                // 4b. Truncation + each PTM family at each occupancy level
                foreach (var family in ptmFamilies)
                    foreach (var entry in MakePtmEntries(family, trunc.Formula, tolerance,
                                                         prefix: trunc.Name))
                        entries.Add(entry);
            }
        }

        return entries;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private record PtmFamily(
        string FamilyKey,
        string BestName,
        double Delta,
        int SiteCount);

    /// <summary>
    /// Collapses all PTM annotations into per-family groups, deduplicating
    /// sites that appear in multiple databases.
    /// </summary>
    private static List<PtmFamily> BuildFamilyGroups(List<PtmAnnotation> allPtms)
    {
        return allPtms
            // Deduplicate: same family + same position → keep best delta/name
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
            // Group by family → one PtmFamily per type
            .GroupBy(p => ModFamily(p.ModificationName))
            .Select(g =>
            {
                double delta = g.Where(p => p.MassDelta != 0)
                                 .OrderByDescending(p => Math.Abs(p.MassDelta))
                                 .Select(p => p.MassDelta)
                                 .FirstOrDefault();
                string name = g.OrderByDescending(p => p.ModificationName.Length)
                                .First().ModificationName;
                return new PtmFamily(g.Key, name, delta, g.Count());
            })
            .ToList();
    }

    /// <summary>
    /// Generates mono/di/tri/... ProteoformEntry rows for one PTM family
    /// applied to <paramref name="baseFormula"/>.
    /// If <paramref name="prefix"/> is provided, the name becomes
    /// "[prefix] + [multiplicity name]".
    /// </summary>
    private static IEnumerable<ProteoformEntry> MakePtmEntries(
        PtmFamily family,
        MolecularFormula baseFormula,
        double tolerance,
        string? prefix = null)
    {
        for (int k = 1; k <= family.SiteCount; k++)
        {
            var envelope = IsotopeCalculator.ComputeFromDelta(baseFormula, family.Delta * k);
            string modName = FormatMultiplicity(family.BestName, family.FamilyKey, k, family.SiteCount);
            yield return new ProteoformEntry
            {
                ModificationName = prefix is null ? modName : $"{prefix} + {modName}",
                CentroidMass = envelope.Centroid,
                Tolerance = tolerance,
                Envelope = envelope
            };
        }
    }

    private static string FormatMultiplicity(string baseName, string family, int k, int total)
    {
        if (total == 1) return baseName;
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
        string display = char.ToUpper(family[0]) + family[1..];
        return $"{prefix}-{display} ({k} of {total} sites)";
    }

    public static string ModFamily(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("phospho"))                                              return "phosphorylation";
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
