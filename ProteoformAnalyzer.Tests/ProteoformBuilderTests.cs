using System.Collections.Generic;
using System.Linq;
using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class ProteoformBuilderTests
{
    [Theory]
    [InlineData("Phosphoserine", "phosphorylation")]
    [InlineData("N6-acetyllysine", "acetylation")]
    [InlineData("Omega-N-methylarginine", "methylation")]
    [InlineData("Glycyl lysine isopeptide (GlyGly)", "ubiquitination")]
    public void ModFamily_MapsNamesToFamilies(string name, string expected)
    {
        Assert.Equal(expected, ProteoformBuilder.ModFamily(name));
    }

    [Fact]
    public void Build_AlwaysIncludesIntactUnmodified()
    {
        var entries = ProteoformBuilder.Build("ACDEFGHIK", new List<PtmAnnotation>(),
            includeTruncations: false, tolerance: 5.0);

        Assert.Contains(entries, e => e.ModificationName == "Unmodified (intact)");
    }

    [Fact]
    public void Build_TwoFamiliesSharingTheOnlySite_ProducesNoCoOccupancy()
    {
        // Phospho and acetyl both annotated at the SAME position → cannot co-occur.
        var ptms = new List<PtmAnnotation>
        {
            new() { ModificationName = "Phospho", Position = 5, MassDelta = 79.96633 },
            new() { ModificationName = "Acetyl",  Position = 5, MassDelta = 42.01057 },
        };

        var entries = ProteoformBuilder.Build("ACDEFGHIKL", ptms,
            includeTruncations: false, tolerance: 5.0);

        bool anyCombo = entries.Any(e =>
            e.ModificationName.ToLower().Contains("phospho") &&
            e.ModificationName.ToLower().Contains("acetyl"));

        Assert.False(anyCombo, "Two different PTMs on the same single site must not co-occur.");
        Assert.Contains(entries, e => e.ModificationName.ToLower().Contains("phospho"));
        Assert.Contains(entries, e => e.ModificationName.ToLower().Contains("acetyl"));
    }

    [Fact]
    public void Build_TwoFamiliesOnDistinctSites_ProducesCoOccupancy()
    {
        var ptms = new List<PtmAnnotation>
        {
            new() { ModificationName = "Phospho", Position = 5, MassDelta = 79.96633 },
            new() { ModificationName = "Acetyl",  Position = 8, MassDelta = 42.01057 },
        };

        var entries = ProteoformBuilder.Build("ACDEFGHIKL", ptms,
            includeTruncations: false, tolerance: 5.0);

        bool anyCombo = entries.Any(e =>
            e.ModificationName.ToLower().Contains("phospho") &&
            e.ModificationName.ToLower().Contains("acetyl"));

        Assert.True(anyCombo, "Different PTMs on distinct sites should be able to co-occur.");
    }

    [Fact]
    public void Build_BlankPtmNames_AreIgnoredWithoutThrowing()
    {
        // Some source endpoints return PTM entries with an empty modification type. These must not
        // form an unnamed family (which previously threw IndexOutOfRangeException during name
        // formatting); they should simply be dropped.
        var ptms = new List<PtmAnnotation>
        {
            new() { ModificationName = "",   Position = 3, MassDelta = 42.0 },
            new() { ModificationName = "  ", Position = 7, MassDelta = 42.0 },
            new() { ModificationName = "Phospho", Position = 5, MassDelta = 79.96633 },
        };

        var entries = ProteoformBuilder.Build("ACDEFGHIKL", ptms,
            includeTruncations: true, tolerance: 5.0);

        // The valid PTM still produces an entry; the blank ones contribute nothing.
        Assert.Contains(entries, e => e.ModificationName.ToLower().Contains("phospho"));
        Assert.DoesNotContain(entries, e => string.IsNullOrWhiteSpace(e.ModificationName));
    }

    [Fact]
    public void Build_TruncationsRenderedAsResidueRanges()
    {
        var entries = ProteoformBuilder.Build("ACDEFG", new List<PtmAnnotation>(),
            includeTruncations: true, tolerance: 5.0);

        // N-terminal -1 residue → protein starts at residue 2 of a 6-mer
        Assert.Contains(entries, e => e.AlternativeName == "2-6");
        // C-terminal -1 residue → protein ends at residue 5
        Assert.Contains(entries, e => e.AlternativeName == "1-5");
    }

    [Fact]
    public void Build_InternalFragments_RenderedAsBothEndedRanges()
    {
        // 10-mer; internal fragment removing 2 from each terminus → residues 3-8.
        var entries = ProteoformBuilder.Build("ACDEFGHIKL", new List<PtmAnnotation>(),
            includeTruncations: true, tolerance: 5.0,
            maxTerminusDepth: 4, includeInternalFragments: true, minFragmentLength: 2);

        Assert.Contains(entries, e => e.AlternativeName == "3-8");
    }

    [Fact]
    public void Build_InternalFragments_DisabledByDefault()
    {
        var entries = ProteoformBuilder.Build("ACDEFGHIKL", new List<PtmAnnotation>(),
            includeTruncations: true, tolerance: 5.0);

        // No both-ended fragment names when internal fragments are not requested.
        Assert.DoesNotContain(entries, e =>
            e.ModificationName.Contains("N-terminal truncation") &&
            e.ModificationName.Contains("C-terminal truncation"));
    }

    [Fact]
    public void Build_PointVariant_AddsMassShiftedProteoform()
    {
        var variants = new List<PointVariant>
        {
            new() { Position = 3, From = 'D', To = 'E', MassDelta = 14.01565 }
        };

        var entries = ProteoformBuilder.Build("ACDEFGHIKL", new List<PtmAnnotation>(),
            includeTruncations: false, tolerance: 5.0, variants: variants);

        var intact = entries.Find(e => e.ModificationName == "Unmodified (intact)");
        var variant = entries.Find(e => e.ModificationName.Contains("Variant D3E"));

        Assert.NotNull(intact);
        Assert.NotNull(variant);
        Assert.Equal(intact!.CentroidMass + 14.01565, variant!.CentroidMass, 3);
    }
}
