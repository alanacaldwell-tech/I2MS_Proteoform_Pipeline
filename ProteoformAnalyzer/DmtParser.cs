using Microsoft.Data.Sqlite;

namespace ProteoformAnalyzer;

/// <summary>
/// Reads ion neutral masses from a .dmt SQLite database.
/// Neutral mass = (Mz * Charge) - (Charge * ProtonMass)
/// </summary>
public static class DmtParser
{
    private const double ProtonMass = 1.007825; // Da (hydrogen atom mass)

    public static IEnumerable<double> ReadMasses(string filePath)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connStr);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Mz, Charge FROM Ion " +
            "WHERE Mz IS NOT NULL AND Charge IS NOT NULL AND Charge != 0;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            double mz     = reader.GetDouble(0);
            double charge = reader.GetDouble(1);
            yield return (mz * charge) - (charge * ProtonMass);
        }
    }
}
