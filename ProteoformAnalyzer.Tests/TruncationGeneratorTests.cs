using System.Linq;
using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class TruncationGeneratorTests
{
    [Fact]
    public void Generate_SingleEnded_SpansWholeSequenceByDefault()
    {
        var t = TruncationGenerator.Generate("ACDEFGHIKL");   // 10-mer
        // 9 N-terminal + 9 C-terminal single-ended truncations, no internal fragments.
        Assert.Equal(9, t.Count(x => x.NTermRemoved > 0 && x.CTermRemoved == 0));
        Assert.Equal(9, t.Count(x => x.CTermRemoved > 0 && x.NTermRemoved == 0));
        Assert.DoesNotContain(t, x => x.NTermRemoved > 0 && x.CTermRemoved > 0);
    }

    [Fact]
    public void Generate_RespectsTerminusDepthCap()
    {
        var t = TruncationGenerator.Generate("ACDEFGHIKL", maxTerminusDepth: 3);
        Assert.All(t, x => Assert.True(x.NTermRemoved <= 3 && x.CTermRemoved <= 3));
        Assert.Equal(3, t.Count(x => x.NTermRemoved > 0 && x.CTermRemoved == 0));
    }

    [Fact]
    public void Generate_InternalFragments_RespectCapAndMinLength()
    {
        var t = TruncationGenerator.Generate("ACDEFGHIKL", maxTerminusDepth: 4,
            includeInternal: true, minFragmentLength: 5);

        var internals = t.Where(x => x.NTermRemoved > 0 && x.CTermRemoved > 0).ToList();
        Assert.NotEmpty(internals);
        // Every internal fragment keeps at least the minimum length (10 - i - j >= 5).
        Assert.All(internals, x => Assert.True(10 - x.NTermRemoved - x.CTermRemoved >= 5));
    }
}
