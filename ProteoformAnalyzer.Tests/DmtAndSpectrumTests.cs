using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class DmtAndSpectrumTests
{
    private const double ProtonMass = 1.007825;

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"paz_test_{Guid.NewGuid():N}.dmt");

    /// <summary>Creates a .dmt SQLite file with an Ion(Mz,Charge) table populated from rows.</summary>
    private static string CreateDmt(IEnumerable<(double mz, double charge)> rows,
                                    string createTableSql = "CREATE TABLE Ion (Mz REAL, Charge REAL);")
    {
        string path = TempPath();
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using (var c = conn.CreateCommand()) { c.CommandText = createTableSql; c.ExecuteNonQuery(); }
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Ion (Mz, Charge) VALUES ($mz, $ch);";
            var pMz = cmd.CreateParameter(); pMz.ParameterName = "$mz"; cmd.Parameters.Add(pMz);
            var pCh = cmd.CreateParameter(); pCh.ParameterName = "$ch"; cmd.Parameters.Add(pCh);
            foreach (var (mz, ch) in rows) { pMz.Value = mz; pCh.Value = ch; cmd.ExecuteNonQuery(); }
            tx.Commit();
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    /// <summary>Builds ion rows at a target neutral mass and charge: mz = neutral/z + proton.</summary>
    private static IEnumerable<(double, double)> Ions(double neutralMass, int charge, int count)
        => Enumerable.Range(0, count).Select(_ => (neutralMass / charge + ProtonMass, (double)charge));

    // ── DmtParser validation ──────────────────────────────────────────────

    [Fact]
    public void ReadIons_ConvertsMzChargeToNeutralMass()
    {
        // mz=1001.007825, z=10 → neutral = 1001.007825*10 - 10*1.007825 = 10000
        string path = CreateDmt(new[] { (1001.007825, 10.0) });
        try
        {
            var ions = DmtParser.ReadIons(path).ToList();
            Assert.Single(ions);
            Assert.Equal(10000.0, ions[0].Mass, precision: 3);
            Assert.Equal(10, ions[0].Charge);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadIons_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            DmtParser.ReadIons(Path.Combine(Path.GetTempPath(), "does_not_exist.dmt")).ToList());
    }

    [Fact]
    public void ReadIons_NoIonTable_ThrowsInvalidData()
    {
        string path = CreateDmt(Enumerable.Empty<(double, double)>(),
            createTableSql: "CREATE TABLE NotIon (X REAL);");
        try { Assert.Throws<InvalidDataException>(() => DmtParser.ReadIons(path).ToList()); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadIons_MissingChargeColumn_ThrowsInvalidData()
    {
        string path = CreateDmt(Enumerable.Empty<(double, double)>(),
            createTableSql: "CREATE TABLE Ion (Mz REAL);");
        try { Assert.Throws<InvalidDataException>(() => DmtParser.ReadIons(path).ToList()); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadIons_NonSqliteFile_ThrowsInvalidData()
    {
        string path = TempPath();
        File.WriteAllText(path, "this is plain text, not a SQLite database");
        try { Assert.Throws<InvalidDataException>(() => DmtParser.ReadIons(path).ToList()); }
        finally { File.Delete(path); }
    }

    // ── SpectrumAnalyzer end-to-end (exercises binary search, adaptive window, charge sets) ──

    [Fact]
    public void ProcessFile_MatchesPeak_CountsIons_AndCollectsChargeStates()
    {
        // A peak centred near 10000 Da, spread over three 1-Da bins, across charges 9/10/11.
        var rows = Ions(9999.4, 9, 5)
            .Concat(Ions(10000.4, 10, 10))
            .Concat(Ions(10001.4, 11, 6))
            .ToList();
        string path = CreateDmt(rows);

        var db = new List<ProteoformEntry>
        {
            new() { ProteinLabel = "TEST", ModificationName = "Unmodified (intact)", CentroidMass = 10000.0 }
        };

        try
        {
            var matches = SpectrumAnalyzer.ProcessFile(
                path, db, matchWindow: 2.0, ionCountingWindow: 5.0, includeUnmatchedPeaks: false);

            Assert.Single(matches);
            var m = matches[0];
            Assert.False(m.IsUnmatchedPeak);
            Assert.Equal("Unmodified (intact)", m.Entry.ModificationName);
            Assert.Equal(21, m.IonCount);                       // all 21 ions within ±5 Da
            Assert.Equal(new[] { 9, 10, 11 }, m.ChargeStates);  // distinct charges, sorted
            Assert.Equal(1, m.RankWithinPeak);
            Assert.True(Math.Abs(m.MassErrorDa) < 1.0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProcessFile_EmptyFile_ReturnsNoMatches()
    {
        string path = CreateDmt(Enumerable.Empty<(double, double)>());
        try
        {
            var matches = SpectrumAnalyzer.ProcessFile(path, new List<ProteoformEntry>(),
                includeUnmatchedPeaks: false);
            Assert.Empty(matches);
        }
        finally { File.Delete(path); }
    }
}
