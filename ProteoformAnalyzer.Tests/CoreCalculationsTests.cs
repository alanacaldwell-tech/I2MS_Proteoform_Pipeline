using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class CoreCalculationsTests
{
    [Fact]
    public void GetFormula_AddsWaterTermini()
    {
        // Glycine residue (C2H3NO) + H2O = free glycine C2H5NO2
        var f = AminoAcidData.GetFormula("G");
        Assert.Equal(2, f.C);
        Assert.Equal(5, f.H);
        Assert.Equal(1, f.N);
        Assert.Equal(2, f.O);
    }

    [Theory]
    [InlineData("G", 75.07)]   // glycine
    [InlineData("A", 89.09)]   // alanine
    public void AverageMass_MatchesKnownAminoAcidMass(string aa, double expected)
    {
        double mass = AminoAcidData.AverageMass(AminoAcidData.GetFormula(aa));
        Assert.Equal(expected, mass, precision: 1);
    }

    [Theory]
    [InlineData("ACDEFGHIKLMNPQRSTVWY", true)]
    [InlineData("ACBZ", false)]   // B, Z not standard residues
    [InlineData("", false)]
    public void IsValidSequence_RejectsNonResidues(string seq, bool expected)
    {
        Assert.Equal(expected, AminoAcidData.IsValidSequence(seq));
    }

    [Fact]
    public void IsotopeEnvelope_CentroidEqualsAverageMass_AndSigmaPositive()
    {
        var f = AminoAcidData.GetFormula("ACDEFGHIKLMNPQRSTVWY");
        var env = IsotopeCalculator.Compute(f);

        Assert.Equal(AminoAcidData.AverageMass(f), env.Centroid, precision: 6);
        Assert.True(env.Sigma > 0);
        Assert.NotEmpty(env.Points);
    }

    [Fact]
    public void ComputeFromDelta_ShiftsCentroidByDelta_KeepsSigma()
    {
        var f = AminoAcidData.GetFormula("ACDEFGHIKLMNPQRSTVWY");
        var baseEnv = IsotopeCalculator.Compute(f);

        const double phospho = 79.96633;
        var shifted = IsotopeCalculator.ComputeFromDelta(f, phospho);

        Assert.Equal(baseEnv.Centroid + phospho, shifted.Centroid, precision: 6);
        Assert.Equal(baseEnv.Sigma, shifted.Sigma, precision: 9);
    }
}
