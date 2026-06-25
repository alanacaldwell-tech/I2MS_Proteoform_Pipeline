using System.Collections.Generic;
using System.Linq;
using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class DiseaseAnnotatorTests
{
    private static DiseaseInfo SampleDisease() => new()
    {
        ProteinDiseases = new() { "Parkinson disease (PARK1)" },
        VariantSites = new() { (5, "in PARK1; loss of function"), (30, "benign polymorphism") }
    };

    [Fact]
    public void Annotate_FlagsColocalizedSitesOnly()
    {
        var ptms = new List<PtmAnnotation>
        {
            new() { ModificationName = "Phosphoserine", Position = 5,  Residue = 'S' },  // colocalizes with variant
            new() { ModificationName = "Phosphoserine", Position = 9,  Residue = 'S' },  // no variant here
        };

        var notes = DiseaseAnnotator.Annotate(ptms, SampleDisease());

        Assert.Contains("in PARK1; loss of function", ptms[0].DiseaseAssociations);
        Assert.Empty(ptms[1].DiseaseAssociations);
        Assert.Single(notes);
        Assert.Contains("S5", notes[0]);
    }

    [Fact]
    public void Annotate_UnknownPositionsAreNeverFlagged()
    {
        var ptms = new List<PtmAnnotation>
        {
            new() { ModificationName = "Whole-protein mod", Position = 0 }
        };

        var notes = DiseaseAnnotator.Annotate(ptms, SampleDisease());

        Assert.Empty(ptms[0].DiseaseAssociations);
        Assert.Empty(notes);
    }

    [Fact]
    public void Apply_CopiesProteinAndSiteContextToEveryEntry()
    {
        var disease = SampleDisease();
        var notes = new List<string> { "S5 Phosphoserine @ variant: in PARK1" };
        var entries = new List<ProteoformEntry>
        {
            new() { ModificationName = "Unmodified (intact)" },
            new() { ModificationName = "Mono-Phospho" },
        };

        DiseaseAnnotator.Apply(entries, disease, notes);

        Assert.All(entries, e =>
        {
            Assert.Equal(disease.ProteinDiseases, e.ProteinDiseaseInvolvement);
            Assert.Equal(notes, e.PtmVariantSites);
        });
    }

    [Fact]
    public void Annotate_NoVariants_ReturnsEmpty()
    {
        var ptms = new List<PtmAnnotation> { new() { ModificationName = "Phospho", Position = 5 } };
        var notes = DiseaseAnnotator.Annotate(ptms, new DiseaseInfo());
        Assert.Empty(notes);
        Assert.Empty(ptms[0].DiseaseAssociations);
    }
}
