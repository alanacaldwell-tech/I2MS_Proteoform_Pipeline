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
            Source = "Computed",
            Envelope = intactEnvelope
        });

        // ── 2. Database-sourced single modifications ──────────────────────
        // Deduplicate across sources: same (name, position) keeps the entry
        // with the largest mass delta (non-zero beats zero if one source
        // didn't know the delta).
        var modPtms = allPtms
            .Where(p => !p.IsNTerminalTruncation && !p.IsCTerminalTruncation)
            .GroupBy(p => (NormName(p.ModificationName), p.Position))
            .Select(g => g.OrderByDescending(p => Math.Abs(p.MassDelta)).First())
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
                Source = ptm.Source,
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
                    Source = "Computed",
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

    private static string NormName(string s) =>
        s.Trim().ToLowerInvariant();
}
