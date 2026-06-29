using System;
using System.Collections.Generic;
using System.IO;
using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class CsvExporterTests
{
    private static string TempCsv() => Path.Combine(Path.GetTempPath(), $"paz_csv_{Guid.NewGuid():N}.csv");

    [Fact]
    public void ExportDatabase_HeaderAndRow_IncludeDiseaseColumns()
    {
        var entries = new List<ProteoformEntry>
        {
            new()
            {
                ProteinLabel = "P1", ModificationName = "Mono-Phospho", CentroidMass = 1000,
                ProteinDiseaseInvolvement = new() { "Parkinson disease (PARK1)" },
                DiseaseRelevance = "Disease variant PTM",
                PtmVariantSites = new() { "S5 Phosphoserine @ variant: in PARK1" }
            }
        };

        string path = TempCsv();
        try
        {
            CsvExporter.ExportDatabase(entries, path);
            var lines = File.ReadAllLines(path);

            Assert.Contains("Protein Disease Involvement", lines[0]);
            Assert.Contains("Disease-Relevant Proteoform", lines[0]);
            Assert.Contains("PTM Sites at Variants", lines[0]);
            Assert.Contains("Parkinson disease (PARK1)", lines[1]);
            Assert.Contains("Disease variant PTM", lines[1]);   // descriptive per-proteoform flag
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportDatabase_NonRelevantProteoform_HasBlankFlag()
    {
        var entries = new List<ProteoformEntry>
        {
            new()
            {
                ProteinLabel = "P1", ModificationName = "Unmodified (intact)", CentroidMass = 1000,
                ProteinDiseaseInvolvement = new() { "Parkinson disease (PARK1)" },
                PtmVariantSites = new()   // no variant-colocalized site on this proteoform
            }
        };

        string path = TempCsv();
        try
        {
            CsvExporter.ExportDatabase(entries, path);
            string row = File.ReadAllLines(path)[1];
            // Protein-level disease context present, but the per-proteoform flag is blank.
            Assert.Contains("Parkinson disease (PARK1)", row);
            Assert.Contains("Parkinson disease (PARK1),,", row);  // empty flag, empty sites
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportResults_HeaderAndRow_IncludeAllNewColumns()
    {
        var entry = new ProteoformEntry
        {
            ProteinLabel = "P1", ModificationName = "Mono-Phospho", CentroidMass = 1079,
            ProteinDiseaseInvolvement = new() { "Alzheimer disease (AD)" },
            DiseaseRelevance = "Disease variant PTM",
            PtmVariantSites = new() { "S9 Phosphoserine @ variant: in AD" }
        };
        var ar = new AnalysisResult
        {
            DatabaseEntry = entry,
            ExperimentalCentroid = 1079.1,
            MassErrorDa = -0.1,
            RankWithinPeak = 1
        };
        ar.ChargeStatesObserved.UnionWith(new[] { 10, 11 });
        ar.IonCountsPerFile["fileA.dmt"] = 500;
        ar.ExperimentalMassPerFile["fileA.dmt"] = 1079.1;

        string path = TempCsv();
        try
        {
            CsvExporter.ExportResults(new() { ar }, new List<string> { "fileA.dmt" }, path);
            string header = File.ReadAllLines(path)[0];
            string row = File.ReadAllLines(path)[1];

            Assert.Contains("Charge States Observed", header);
            Assert.Contains("# Charge States", header);
            Assert.Contains("Match Rank", header);
            Assert.Contains("Protein Disease Involvement", header);
            Assert.Contains("Disease-Relevant Proteoform", header);
            Assert.Contains("PTM Sites at Variants", header);
            Assert.Contains("fileA.dmt Ion Count", header);
            Assert.Contains("fileA.dmt Exp Mass (Da)", header);

            Assert.Contains("Alzheimer disease (AD)", row);
            Assert.Contains("Disease variant PTM", row);
            Assert.Contains("500", row);

            // Disease-context columns are now placed after the per-file columns.
            Assert.True(header.IndexOf("Protein Disease Involvement", StringComparison.Ordinal)
                        > header.IndexOf("fileA.dmt Exp Mass (Da)", StringComparison.Ordinal),
                        "Disease columns should come after the per-file columns.");
        }
        finally { File.Delete(path); }
    }
}
