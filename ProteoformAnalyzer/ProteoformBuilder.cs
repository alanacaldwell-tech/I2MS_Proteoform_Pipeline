namespace ProteoformAnalyzer;

public static class ProteoformBuilder
{
    /// <summary>
    /// Builds the full list of proteoforms from a sequence, a modification database,
    /// and optionally a set of truncations.
    /// </summary>
    public static List<Proteoform> Build(
        string sequence,
        ModificationDatabase db,
        bool includeTruncations,
        double tolerance)
    {
        var results = new List<Proteoform>();
        double baseMass = AminoAcidMasses.CalculateMass(sequence);

        // 1. Intact (unmodified) proteoform
        results.Add(new Proteoform
        {
            Description = "Intact (unmodified)",
            Mass = baseMass,
            Tolerance = tolerance
        });

        // 2. Single modifications applied to the intact sequence
        var applicableMods = db.GetApplicable(sequence).ToList();
        foreach (var mod in applicableMods)
        {
            results.Add(new Proteoform
            {
                Description = mod.Name,
                Mass = baseMass + mod.MassDelta,
                Tolerance = tolerance
            });
        }

        // 3. Truncations (N- and C-terminal)
        if (includeTruncations && sequence.Length > 1)
        {
            var truncations = TruncationGenerator.Generate(sequence);
            foreach (var trunc in truncations)
            {
                double truncMass = baseMass + trunc.MassDelta;
                results.Add(new Proteoform
                {
                    Description = trunc.Name,
                    Mass = truncMass,
                    Tolerance = tolerance
                });

                // Also apply single modifications to each truncation
                string truncSeq = trunc.IsNTerminal
                    ? sequence[trunc.ResiduesToRemove..]
                    : sequence[..^trunc.ResiduesToRemove];

                var truncMods = db.GetApplicable(truncSeq);
                foreach (var mod in truncMods)
                {
                    results.Add(new Proteoform
                    {
                        Description = $"{trunc.Name} + {mod.Name}",
                        Mass = truncMass + mod.MassDelta,
                        Tolerance = tolerance
                    });
                }
            }
        }

        return results;
    }
}
