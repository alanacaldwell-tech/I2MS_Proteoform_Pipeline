using Microsoft.Data.Sqlite;

namespace IonCounterWeb.Services;

internal static class DmtParser
{
    private const double ProtonMass = 1.007825;

    // The dmt file must exist on disk for SQLite to open it.
    // Callers should write the uploaded bytes to a temp file first.
    public static IEnumerable<double> ReadMasses(string filePath)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(cs);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Mz, Charge FROM Ion WHERE Mz IS NOT NULL AND Charge IS NOT NULL AND Charge != 0;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            double mz     = reader.GetDouble(0);
            double charge = reader.GetDouble(1);
            yield return (mz * charge) - (charge * ProtonMass);
        }
    }
}
