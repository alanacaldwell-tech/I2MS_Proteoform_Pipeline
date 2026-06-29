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
    public void Apply_AttachesVariantSiteOnlyToProteoformsCarryingThatPtmFamily()
    {
        var disease = SampleDisease();   // disease variant at position 5
        var ptms = new List<PtmAnnotation>
        {
            new() { ModificationName = "Phosphoserine", Position = 5, Residue = 'S' },
        };
        var sites = DiseaseAnnotator.ColocalizedSites(ptms, disease);

        var phospho = new ProteoformEntry
        {
            ModificationName = "Mono-Phospho",
            PtmFamilies = new() { "phosphorylation" }, StartResidue = 1, EndResidue = 100
        };
        var acetyl = new ProteoformEntry
        {
            ModificationName = "Acetylation",
            PtmFamilies = new() { "acetylation" }, StartResidue = 1, EndResidue = 100
        };
        var intact = new ProteoformEntry
        {
            ModificationName = "Unmodified (intact)", StartResidue = 1, EndResidue = 100
        };

        var entries = new[] { phospho, acetyl, intact };
        DiseaseAnnotator.Apply(entries, disease, sites);

        // Only the phospho proteoform carries the variant-colocalized phospho site.
        Assert.Single(phospho.PtmVariantSites);
        Assert.Contains("S5", phospho.PtmVariantSites[0]);
        Assert.Empty(acetyl.PtmVariantSites);   // carries a different modification family
        Assert.Empty(intact.PtmVariantSites);   // carries no modification

        // The variant ("in PARK1") is linked to the protein's documented disease, so the phospho
        // proteoform is flagged as disease-relevant; the others carry no relevance label.
        Assert.Equal("Disease variant PTM", phospho.DiseaseRelevance);
        Assert.Equal("", acetyl.DiseaseRelevance);
        Assert.Equal("", intact.DiseaseRelevance);
        Assert.Contains("Parkinson disease (PARK1)", phospho.PtmVariantSites[0]);

        // Protein-level disease involvement is shared context on every proteoform.
        Assert.All(entries, e => Assert.Equal(disease.ProteinDiseases, e.ProteinDiseaseInvolvement));
    }

    [Fact]
    public void ColocalizedSites_VariantOfUnspecifiedSignificance_IsNotDiseaseLabelled()
    {
        // A variant whose description names no documented disease and no pathogenic language.
        var disease = new DiseaseInfo
        {
            ProteinDiseases = new() { "Parkinson disease (PARK1)" },
            VariantSites = new() { (5, "in dbSNP:rs12345") }
        };
        var ptms = new List<PtmAnnotation>
        {
            new() { ModificationName = "Phosphoserine", Position = 5, Residue = 'S' },
        };

        var sites = DiseaseAnnotator.ColocalizedSites(ptms, disease);

        var entry = new ProteoformEntry
        {
            ModificationName = "Mono-Phospho",
            PtmFamilies = new() { "phosphorylation" }, StartResidue = 1, EndResidue = 100
        };
        DiseaseAnnotator.Apply(new[] { entry }, disease, sites);

        Assert.Single(sites);
        Assert.False(sites[0].IsDiseaseAssociated);
        Assert.Equal("Sequence variant PTM", entry.DiseaseRelevance);
    }

    [Fact]
    public void Apply_DropsVariantSiteRemovedByTruncation()
    {
        var disease = SampleDisease();   // disease variant at position 5
        var ptms = new List<PtmAnnotation>
        {
            new() { ModificationName = "Phosphoserine", Position = 5, Residue = 'S' },
        };
        var sites = DiseaseAnnotator.ColocalizedSites(ptms, disease);

        // An N-terminal truncation starting at residue 11 no longer spans position 5.
        var truncated = new ProteoformEntry
        {
            ModificationName = "11-100 + Mono-Phospho",
            PtmFamilies = new() { "phosphorylation" }, StartResidue = 11, EndResidue = 100
        };

        DiseaseAnnotator.Apply(new[] { truncated }, disease, sites);

        Assert.Empty(truncated.PtmVariantSites);
        Assert.Equal("", truncated.DiseaseRelevance);   // site removed by truncation → not relevant
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
