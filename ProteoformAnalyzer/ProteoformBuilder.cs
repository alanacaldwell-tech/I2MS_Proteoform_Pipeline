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
            SequencePosition = "N/A",
            ModificationName = "Unmodified (intact)",
            CentroidMass = intactEnvelope.Centroid,
            Tolerance = tolerance,
            Envelope = intactEnvelope
        });

        // ── 2. Merge and deduplicate PTMs from all sources ────────────────
        // Group by (position, modification type-family) so that differently
        // worded annotations for the same chemical event at the same site
        // collapse into one row.  Within each group, prefer the entry with
        // the most informative name (longest) and the best-known mass delta
        // (largest absolute value; non-zero beats zero).
        var modPtms = allPtms
            .Where(p => !p.IsNTerminalTruncation && !p.IsCTerminalTruncation)
            .GroupBy(p => (ModFamily(p.ModificationName), p.Position))
            .Select(g =>
            {
                // Best mass delta: largest |delta| (falls back to 0 if none known)
                var withDelta = g.Where(p => p.MassDelta != 0)
                                 .OrderByDescending(p => Math.Abs(p.MassDelta))
                                 .FirstOrDefault();
                var best = withDelta ?? g.First();
                // Most descriptive name wins
                best.ModificationName = g.OrderByDescending(p => p.ModificationName.Length)
                                         .First().ModificationName;
                return best;
            })
            .OrderBy(p => p.Position)
            .ToList();

        foreach (var ptm in modPtms)
        {
            var envelope = IsotopeCalculator.ComputeFromDelta(intactFormula, ptm.MassDelta);
            entries.Add(new ProteoformEntry
            {
                SequencePosition = FormatPosition(ptm, sequence),
                ModificationName = ptm.ModificationName,
                CentroidMass = envelope.Centroid,
                Tolerance = tolerance,
                Envelope = envelope
            });
        }

        // ── 3. Truncations ────────────────────────────────────────────────
        if (includeTruncations && sequence.Length > 1)
        {
            var truncations = TruncationGenerator.Generate(sequence);
            foreach (var trunc in truncations)
            {
                var envelope = IsotopeCalculator.ComputeFromDelta(intactFormula, trunc.MassDelta);

                string pos = trunc.IsNTerminalTruncation
                    ? $"1–{trunc.TruncationLength} removed"
                    : $"{sequence.Length - trunc.TruncationLength + 1}–{sequence.Length} removed";

                entries.Add(new ProteoformEntry
                {
                    SequencePosition = pos,
                    ModificationName = trunc.ModificationName,
                    CentroidMass = envelope.Centroid,
                    Tolerance = tolerance,
                    Envelope = envelope
                });
            }
        }

        return entries;
    }

    private static string FormatPosition(PtmAnnotation ptm, string sequence)
    {
        if (ptm.Position <= 0) return "N/A";
        char res = ptm.Residue ?? (ptm.Position <= sequence.Length
            ? char.ToUpper(sequence[ptm.Position - 1]) : '?');
        return $"{res}{ptm.Position}";
    }

    /// <summary>
    /// Maps a modification name to a canonical type-family key so that
    /// synonymous annotations (e.g. "Phosphoserine", "Phosphorylation; S",
    /// "phospho (S)") collapse to the same group at a given position.
    /// </summary>
    private static string ModFamily(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("phospho"))                          return "phosphorylation";
        if (n.Contains("trimethyl") || (n.Contains("methyl") && n.Contains("tri"))) return "trimethylation";
        if (n.Contains("dimethyl") || (n.Contains("methyl") && n.Contains("di")))   return "dimethylation";
        if (n.Contains("methyl"))                           return "methylation";
        if (n.Contains("acetyl"))                           return "acetylation";
        if (n.Contains("ubiquitin") || n.Contains("glygly") || n.Contains("gg-"))   return "ubiquitination";
        if (n.Contains("sumo"))                             return "sumoylation";
        if (n.Contains("nedd"))                             return "neddylation";
        if (n.Contains("glyco") || n.Contains("hexnac") || n.Contains("hex"))       return "glycosylation";
        if (n.Contains("hydroxyl"))                         return "hydroxylation";
        if (n.Contains("oxidat") || n.Contains("dioxidat") || n.Contains("trioxidat")) return "oxidation";
        if (n.Contains("deamidat") || n.Contains("deamin")) return "deamidation";
        if (n.Contains("citrullin") || n.Contains("deiminat")) return "citrullination";
        if (n.Contains("succinyl"))                         return "succinylation";
        if (n.Contains("malonyl"))                          return "malonylation";
        if (n.Contains("glutaryl"))                         return "glutarylation";
        if (n.Contains("crotonyl"))                         return "crotonylation";
        if (n.Contains("butyryl"))                          return "butyrylation";
        if (n.Contains("lactyl"))                           return "lactylation";
        if (n.Contains("propionyl"))                        return "propionylation";
        if (n.Contains("palmitoyl"))                        return "palmitoylation";
        if (n.Contains("myristoyl"))                        return "myristoylation";
        if (n.Contains("farnesyl"))                         return "farnesylation";
        if (n.Contains("geranylgeranyl"))                   return "geranylgeranylation";
        if (n.Contains("sulfat"))                           return "sulfation";
        if (n.Contains("nitros"))                           return "nitrosylation";
        if (n.Contains("nitrat"))                           return "nitration";
        if (n.Contains("adp-ribosyl") || n.Contains("adpribosyl")) return "adp-ribosylation";
        if (n.Contains("pyroglutam"))                       return "pyroglutamylation";
        if (n.Contains("amidation") || n.Contains("amidat")) return "amidation";
        if (n.Contains("disulfide") || n.Contains("cross-link")) return "disulfide";
        if (n.Contains("formyl"))                           return "formylation";
        if (n.Contains("truncat") || n.Contains("signal") || n.Contains("transit") || n.Contains("propeptide")) return "truncation";
        // Fall back to the normalised name itself (preserves uniqueness for unknowns)
        return n.Trim();
    }
}
