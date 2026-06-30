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
    public void ExportDatabase_WritesHeaderAndRow()
    {
        var entries = new List<ProteoformEntry>
        {
            new() { ProteinLabel = "P1", ModificationName = "Unmodified (intact)", CentroidMass = 1000 }
        };

        string path = TempCsv();
        try
        {
            CsvExporter.ExportDatabase(entries, path);
            var lines = File.ReadAllLines(path);

            Assert.Contains("Protein", lines[0]);
            Assert.Contains("Modification Name", lines[0]);
            Assert.Contains("Predicted Centroid Mass (Da)", lines[0]);
            Assert.Contains("P1", lines[1]);
            Assert.Contains("Unmodified (intact)", lines[1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportResults_HeaderAndRow_IncludeMatchAndPerFileColumns()
    {
        var entry = new ProteoformEntry
        {
            ProteinLabel = "P1", ModificationName = "Mono-Phospho", CentroidMass = 1079
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
            Assert.Contains("fileA.dmt Ion Count", header);
            Assert.Contains("fileA.dmt Exp Mass (Da)", header);

            Assert.Contains("Mono-Phospho", row);
            Assert.Contains("500", row);
        }
        finally { File.Delete(path); }
    }
}
