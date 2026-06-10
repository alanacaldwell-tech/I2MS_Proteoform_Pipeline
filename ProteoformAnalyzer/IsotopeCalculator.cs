namespace ProteoformAnalyzer;

/// <summary>
/// Computes the isotopic envelope using the Gaussian approximation.
///
/// Derivation:
///   Each element contributes independent isotope variance.
///   Total variance  σ² = Σ_elements( n_elem * σ²_elem )
///   Centroid (intensity-weighted average mass) = chemical average mass.
///
/// Per-element isotope variance (IUPAC 2016 abundances, Da² units):
///   σ²_C  ≈ 0.01059   (C-12 98.93%, C-13 1.07%)
///   σ²_H  ≈ 0.000152  (H-1 99.985%, H-2 0.015%)
///   σ²_N  ≈ 0.003649  (N-14 99.632%, N-15 0.368%)
///   σ²_O  ≈ 0.000630  (O-16 99.757%, O-17 0.038%, O-18 0.205%)
///   σ²_S  ≈ 0.163     (S-32 94.93%, S-33 0.76%, S-34 4.25%, S-36 0.02%)
///   σ²_P  ≈ 0         (P is 100% P-31)
/// </summary>
public static class IsotopeCalculator
{
    private const double VarC = 0.010590;
    private const double VarH = 0.000152;
    private const double VarN = 0.003649;
    private const double VarO = 0.000630;
    private const double VarS = 0.163000;

    // Number of points to sample across ±4σ of the Gaussian envelope
    private const int SamplePoints = 120;

    public static IsotopeEnvelope Compute(MolecularFormula formula)
    {
        double centroid = AminoAcidData.AverageMass(formula);

        double variance = formula.C * VarC
                        + formula.H * VarH
                        + formula.N * VarN
                        + formula.O * VarO
                        + formula.S * VarS;

        double sigma = Math.Sqrt(Math.Max(variance, 1e-9));

        // Sample the Gaussian over ±4σ
        double massMin = centroid - 4 * sigma;
        double massMax = centroid + 4 * sigma;
        double step = (massMax - massMin) / (SamplePoints - 1);

        var points = new List<(double, double)>(SamplePoints);
        double scale = 1.0 / (sigma * Math.Sqrt(2 * Math.PI));
        for (int i = 0; i < SamplePoints; i++)
        {
            double m = massMin + i * step;
            double diff = m - centroid;
            double intensity = scale * Math.Exp(-0.5 * (diff / sigma) * (diff / sigma));
            points.Add((m, intensity));
        }

        return new IsotopeEnvelope
        {
            Centroid = centroid,
            Sigma = sigma,
            Points = points
        };
    }

    /// <summary>
    /// Computes isotope envelope when only the intact formula is known and we
    /// are adding a modification as a mass delta (no formula change).
    /// The centroid shifts by the delta; sigma is unchanged (valid approximation
    /// for modifications small relative to the intact protein).
    /// </summary>
    public static IsotopeEnvelope ComputeFromDelta(MolecularFormula intactFormula, double massDelta)
    {
        var intactEnvelope = Compute(intactFormula);
        double newCentroid = intactEnvelope.Centroid + massDelta;
        double sigma = intactEnvelope.Sigma;

        double massMin = newCentroid - 4 * sigma;
        double step = 8 * sigma / (SamplePoints - 1);
        double scale = 1.0 / (sigma * Math.Sqrt(2 * Math.PI));

        var points = new List<(double, double)>(SamplePoints);
        for (int i = 0; i < SamplePoints; i++)
        {
            double m = massMin + i * step;
            double diff = m - newCentroid;
            double intensity = scale * Math.Exp(-0.5 * (diff / sigma) * (diff / sigma));
            points.Add((m, intensity));
        }

        return new IsotopeEnvelope
        {
            Centroid = newCentroid,
            Sigma = sigma,
            Points = points
        };
    }
}
