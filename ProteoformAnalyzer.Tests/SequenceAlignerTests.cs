using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class SequenceAlignerTests
{
    [Fact]
    public void Maps_ReferenceContainedInQuery_NTerminalTag()
    {
        // Construct = tag + native; the native reference is a contiguous substring of the query.
        var map = SequenceAligner.MapReferenceToQuery(reference: "ACDEFG", query: "MGSSACDEFGHH");

        // 'A' of the reference (pos 1) sits at query position 5 (after the 4-residue tag "MGSS").
        Assert.Equal(5, map[1]);
        Assert.Equal(10, map[6]);
        Assert.Equal(6, map.Count);
    }

    [Fact]
    public void Maps_QueryContainedInReference_NativeFragment()
    {
        // Construct is a native sub-fragment of the full reference protein.
        var map = SequenceAligner.MapReferenceToQuery(reference: "MGSSACDEFGHH", query: "ACDEFG");

        // Reference position 5 ('A') maps to query position 1.
        Assert.Equal(1, map[5]);
        Assert.Equal(6, map[10]);
    }

    [Fact]
    public void Maps_OnlyExactMatches_WhenSubstitutionPresent()
    {
        // No exact containment either way → local alignment; the mismatch column is not mapped.
        var map = SequenceAligner.MapReferenceToQuery(reference: "PEPTIDE", query: "PEPXIDE");

        Assert.Equal(1, map[1]);   // P
        Assert.Equal(7, map[7]);   // E
        Assert.False(map.ContainsKey(4));   // T↔X mismatch is dropped
    }

    [Fact]
    public void ReturnsEmpty_ForEmptyInput()
    {
        Assert.Empty(SequenceAligner.MapReferenceToQuery("", "ACDE"));
        Assert.Empty(SequenceAligner.MapReferenceToQuery("ACDE", ""));
    }
}
