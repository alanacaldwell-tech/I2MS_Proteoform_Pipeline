using System.Collections.Generic;
using System.Linq;
using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class DecoyFdrTests
{
    private static List<ProteoformEntry> Targets() => new()
    {
        new ProteoformEntry { ModificationName = "Unmodified (intact)", CentroidMass = 10000,
            Envelope = IsotopeCalculator.Compute(AminoAcidData.GetFormula("ACDEFGHIK")) },
        new ProteoformEntry { ModificationName = "Mono-Phospho",        CentroidMass = 10079.97 },
        new ProteoformEntry { ModificationName = "Di-Phospho",          CentroidMass = 10159.93 },
    };

    [Fact]
    public void Generate_ProducesOneShiftedDecoyPerTarget()
    {
        var targets = Targets();
        var decoys = DecoyGenerator.Generate(targets);

        Assert.Equal(targets.Count, decoys.Count);
        Assert.All(decoys, d => Assert.True(d.IsDecoy));
        for (int i = 0; i < targets.Count; i++)
            Assert.True(System.Math.Abs(decoys[i].CentroidMass - targets[i].CentroidMass) >= 11.0,
                "Decoy mass shift must exceed the match tolerance band.");
    }

    [Fact]
    public void Generate_IsDeterministicForAGivenSeed()
    {
        var targets = Targets();
        var a = DecoyGenerator.Generate(targets, seed: 42);
        var b = DecoyGenerator.Generate(targets, seed: 42);

        Assert.Equal(a.Select(x => x.CentroidMass), b.Select(x => x.CentroidMass));
    }

    [Fact]
    public void Estimate_FdrIsDecoyOverTarget()
    {
        var results = new List<AnalysisResult>();
        for (int i = 0; i < 10; i++)
            results.Add(new AnalysisResult { DatabaseEntry = new ProteoformEntry { ModificationName = "Hit" + i } });
        for (int i = 0; i < 2; i++)
            results.Add(new AnalysisResult { DatabaseEntry = new ProteoformEntry { ModificationName = "DECOY", IsDecoy = true } });
        // Unmatched peaks must not count as target hits.
        results.Add(new AnalysisResult { DatabaseEntry = new ProteoformEntry { ModificationName = "Unmatched peak (123.00 Da)" } });

        var fdr = FdrEstimator.Estimate(results);

        Assert.Equal(10, fdr.TargetHits);
        Assert.Equal(2, fdr.DecoyHits);
        Assert.Equal(0.2, fdr.Fdr, precision: 6);
    }

    [Fact]
    public void Estimate_FdrCapsAtOne()
    {
        var results = new List<AnalysisResult>
        {
            new() { DatabaseEntry = new ProteoformEntry { ModificationName = "Hit" } },
            new() { DatabaseEntry = new ProteoformEntry { ModificationName = "DECOY", IsDecoy = true } },
            new() { DatabaseEntry = new ProteoformEntry { ModificationName = "DECOY", IsDecoy = true } },
            new() { DatabaseEntry = new ProteoformEntry { ModificationName = "DECOY", IsDecoy = true } },
        };

        Assert.Equal(1.0, FdrEstimator.Estimate(results).Fdr, precision: 6);
    }
}
