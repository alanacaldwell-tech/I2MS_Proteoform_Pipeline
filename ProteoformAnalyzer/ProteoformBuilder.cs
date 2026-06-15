namespace ProteoformAnalyzer;

public static class ProteoformBuilder
{
    // Maximum number of simultaneously active PTM families in a combination.
    // Pairwise (2) and triple (3) are generated; higher orders grow exponentially
    // and are biologically rare for single proteoforms detected by I2MS.
    private const int MaxCombinationDepth = 3;

    public static List<ProteoformEntry> Build(
        string sequence,
        List<PtmAnnotation> allPtms,
        bool includeTruncations,
        double tolerance,
        string proteinLabel = "",
        int maxOccupancyPerFamily = 12)
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

        // ── 2. Build PTM family groups ────────────────────────────────────
        var ptmFamilies = BuildFamilyGroups(allPtms);

        // ── 3. Single-family PTM proteoforms (mono/di/tri/...) ────────────
        foreach (var family in ptmFamilies)
            foreach (var entry in MakeSingleFamilyEntries(family, intactFormula, tolerance,
                                                           maxOccupancy: maxOccupancyPerFamily))
                entries.Add(entry);

        // ── 4. Cross-family PTM combinations ─────────────────────────────
        // Generate all subsets of 2..MaxCombinationDepth different families,
        // each at every valid occupancy level (1..min(SiteCount, maxOccupancyPerFamily)).
        // Same-family combinations (e.g. mono+di phospho = tri-phospho) are
        // already covered by step 3 and are excluded here.
        foreach (var combo in CrossFamilyCombinations(ptmFamilies, MaxCombinationDepth, maxOccupancyPerFamily))
        {
            double totalDelta = combo.Sum(kv => kv.Family.Delta * kv.Occupancy);
            var envelope = IsotopeCalculator.ComputeFromDelta(intactFormula, totalDelta);
            entries.Add(new ProteoformEntry
            {
                ModificationName = FormatCombinationName(combo),
                CentroidMass = envelope.Centroid,
                Tolerance = tolerance,
                Envelope = envelope
            });
        }

        // ── 5. Truncations, truncation+single-PTM combinations ────────────
        if (includeTruncations && sequence.Length > 1)
        {
            var truncations = TruncationGenerator.Generate(sequence);

            int truncPtmCount = truncations.Count * (1 + ptmFamilies.Sum(f => f.SiteCount));
            if (truncPtmCount > 50_000)
                Console.WriteLine($"  [Warning] Generating {truncPtmCount:N0} truncation+PTM " +
                                  "combination rows — this may take a moment.");

            foreach (var trunc in truncations)
            {
                // 5a. Truncation alone
                var truncEnvelope = IsotopeCalculator.Compute(trunc.Formula);
                entries.Add(new ProteoformEntry
                {
                    ModificationName = trunc.Name,
                    CentroidMass = truncEnvelope.Centroid,
                    Tolerance = tolerance,
                    Envelope = truncEnvelope
                });

                // Only PTMs annotated at positions that still exist in the truncated
                // sequence can occur. N-terminal truncations remove positions 1..i;
                // C-terminal truncations remove the last i positions.
                var survivingPtms = trunc.IsNTerminal
                    ? allPtms.Where(p => p.Position > trunc.ResiduesToRemove).ToList()
                    : allPtms.Where(p => p.Position <= sequence.Length - trunc.ResiduesToRemove).ToList();

                if (survivingPtms.Count == 0)
                    continue;

                var truncFamilies = BuildFamilyGroups(survivingPtms);

                // 5b. Truncation + each single PTM family at each occupancy level
                foreach (var family in truncFamilies)
                    foreach (var entry in MakeSingleFamilyEntries(
                        family, trunc.Formula, tolerance, prefix: trunc.Name,
                        maxOccupancy: maxOccupancyPerFamily))
                        entries.Add(entry);

                // 5c. Truncation + cross-family PTM combinations
                foreach (var combo in CrossFamilyCombinations(truncFamilies, MaxCombinationDepth, maxOccupancyPerFamily))
                {
                    double totalDelta = combo.Sum(kv => kv.Family.Delta * kv.Occupancy);
                    var envelope = IsotopeCalculator.ComputeFromDelta(trunc.Formula, totalDelta);
                    entries.Add(new ProteoformEntry
                    {
                        ModificationName = $"{trunc.Name} + {FormatCombinationName(combo)}",
                        CentroidMass = envelope.Centroid,
                        Tolerance = tolerance,
                        Envelope = envelope
                    });
                }
            }
        }

        // Post-processing: set ProteinLabel and compute AlternativeName for every entry
        foreach (var e in entries)
        {
            e.ProteinLabel = proteinLabel;
            e.AlternativeName = ComputeAlternativeName(e.ModificationName, sequence.Length);
        }
        return entries;
    }

    // ── Alternative name generator ────────────────────────────────────────

    /// <summary>
    /// Derives a user-friendly alternative name:
    ///   • Truncations → residue range, e.g. "6-140" or "1-135"
    ///   • PTMs        → strips redundant "(X of Y sites)" suffix
    ///   • Combinations of the above are handled component-by-component
    /// </summary>
    private static string ComputeAlternativeName(string modName, int seqLen)
    {
        // Remove "(X of Y sites)" from any PTM component
        string alt = System.Text.RegularExpressions.Regex.Replace(
            modName, @"\s*\(\d+ of \d+ sites?\)", "");

        // N-terminal truncation (-i residues) → "{i+1}-{seqLen}"
        alt = System.Text.RegularExpressions.Regex.Replace(
            alt,
            @"N-terminal truncation \(-(\d+) residues?\)",
            m => $"{int.Parse(m.Groups[1].Value) + 1}-{seqLen}");

        // C-terminal truncation (-i residues) → "1-{seqLen-i}"
        alt = System.Text.RegularExpressions.Regex.Replace(
            alt,
            @"C-terminal truncation \(-(\d+) residues?\)",
            m => $"1-{seqLen - int.Parse(m.Groups[1].Value)}");

        return alt.Trim();
    }

    // ── Cross-family combination generator ───────────────────────────────

    private record OccupiedFamily(PtmFamily Family, int Occupancy);

    /// <summary>
    /// Yields every combination of 2..maxDepth distinct PTM families where
    /// each chosen family has occupancy 1..SiteCount.
    /// Families are taken from distinct indices so the same family is never
    /// combined with itself.
    /// </summary>
    private static IEnumerable<List<OccupiedFamily>> CrossFamilyCombinations(
        List<PtmFamily> families, int maxDepth, int maxOccupancy = int.MaxValue)
    {
        int n = families.Count;
        if (n < 2) yield break;

        // Iterate over all subsets of size 2..maxDepth
        foreach (var indices in Subsets(n, 2, maxDepth))
        {
            // For each chosen subset, iterate all occupancy combinations
            var subFamilies = indices.Select(i => families[i]).ToList();
            foreach (var occupancies in OccupancyProduct(subFamilies, maxOccupancy))
            {
                yield return indices
                    .Select((idx, pos) => new OccupiedFamily(families[idx], occupancies[pos]))
                    .ToList();
            }
        }
    }

    /// <summary>Yields all subsets of {0..n-1} with size in [minSize, maxSize].</summary>
    private static IEnumerable<List<int>> Subsets(int n, int minSize, int maxSize)
    {
        return SubsetsFrom(0, n, minSize, maxSize, new List<int>());
    }

    private static IEnumerable<List<int>> SubsetsFrom(
        int start, int n, int minSize, int maxSize, List<int> current)
    {
        if (current.Count >= minSize)
            yield return new List<int>(current);

        if (current.Count == maxSize) yield break;

        for (int i = start; i < n; i++)
        {
            current.Add(i);
            foreach (var s in SubsetsFrom(i + 1, n, minSize, maxSize, current))
                yield return s;
            current.RemoveAt(current.Count - 1);
        }
    }

    /// <summary>
    /// Yields every combination of occupancy values (1..SiteCount) for a list
    /// of families — i.e. the Cartesian product of [1..S_i] for each family i.
    /// </summary>
    private static IEnumerable<List<int>> OccupancyProduct(List<PtmFamily> families, int maxOccupancy = int.MaxValue)
    {
        return OccupancyProductFrom(families, 0, new List<int>(), maxOccupancy);
    }

    private static IEnumerable<List<int>> OccupancyProductFrom(
        List<PtmFamily> families, int pos, List<int> current, int maxOccupancy)
    {
        if (pos == families.Count)
        {
            yield return new List<int>(current);
            yield break;
        }
        int cap = Math.Min(families[pos].SiteCount, maxOccupancy);
        for (int k = 1; k <= cap; k++)
        {
            current.Add(k);
            foreach (var combo in OccupancyProductFrom(families, pos + 1, current, maxOccupancy))
                yield return combo;
            current.RemoveAt(current.Count - 1);
        }
    }

    // ── Entry builders ────────────────────────────────────────────────────

    private record PtmFamily(
        string FamilyKey,
        string BestName,
        double Delta,
        int SiteCount);

    private static List<PtmFamily> BuildFamilyGroups(List<PtmAnnotation> allPtms)
    {
        return allPtms
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

    private static IEnumerable<ProteoformEntry> MakeSingleFamilyEntries(
        PtmFamily family,
        MolecularFormula baseFormula,
        double tolerance,
        string? prefix = null,
        int maxOccupancy = int.MaxValue)
    {
        int cap = Math.Min(family.SiteCount, maxOccupancy);
        for (int k = 1; k <= cap; k++)
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

    // ── Name formatters ───────────────────────────────────────────────────

    private static string FormatCombinationName(List<OccupiedFamily> combo)
    {
        return string.Join(" + ", combo.Select(kv =>
            FormatMultiplicity(kv.Family.BestName, kv.Family.FamilyKey,
                               kv.Occupancy, kv.Family.SiteCount)));
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

    // ── ModFamily mapping ─────────────────────────────────────────────────

    public static string ModFamily(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("phospho"))                                                   return "phosphorylation";
        if (n.Contains("methyl"))                                                    return "methylation";
        if (n.Contains("acetyl"))                                                    return "acetylation";
        if (n.Contains("ubiquitin") || n.Contains("glygly") || n.Contains("gg-"))   return "ubiquitination";
        if (n.Contains("sumo"))                                                      return "sumoylation";
        if (n.Contains("nedd"))                                                      return "neddylation";
        if (n.Contains("glyco") || n.Contains("hexnac") || n.Contains("hex"))       return "glycosylation";
        if (n.Contains("hydroxyl"))                                                  return "hydroxylation";
        if (n.Contains("oxidat"))                                                    return "oxidation";
        if (n.Contains("deamidat") || n.Contains("deamin"))                         return "deamidation";
        if (n.Contains("citrullin") || n.Contains("deiminat"))                      return "citrullination";
        if (n.Contains("succinyl"))                                                  return "succinylation";
        if (n.Contains("malonyl"))                                                   return "malonylation";
        if (n.Contains("glutaryl"))                                                  return "glutarylation";
        if (n.Contains("crotonyl"))                                                  return "crotonylation";
        if (n.Contains("butyryl"))                                                   return "butyrylation";
        if (n.Contains("lactyl"))                                                    return "lactylation";
        if (n.Contains("propionyl"))                                                 return "propionylation";
        if (n.Contains("palmitoyl"))                                                 return "palmitoylation";
        if (n.Contains("myristoyl"))                                                 return "myristoylation";
        if (n.Contains("farnesyl"))                                                  return "farnesylation";
        if (n.Contains("geranylgeranyl"))                                            return "geranylgeranylation";
        if (n.Contains("sulfat"))                                                    return "sulfation";
        if (n.Contains("nitros"))                                                    return "nitrosylation";
        if (n.Contains("nitrat"))                                                    return "nitration";
        if (n.Contains("adp-ribosyl") || n.Contains("adpribosyl"))                  return "adp-ribosylation";
        if (n.Contains("pyroglutam"))                                                return "pyroglutamylation";
        if (n.Contains("amidation") || n.Contains("amidat"))                        return "amidation";
        if (n.Contains("disulfide") || n.Contains("cross-link"))                    return "disulfide";
        if (n.Contains("formyl"))                                                    return "formylation";
        return n.Trim();
    }
}
