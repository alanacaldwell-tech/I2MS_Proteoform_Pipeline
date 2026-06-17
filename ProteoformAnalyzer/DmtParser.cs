using Microsoft.Data.Sqlite;

namespace ProteoformAnalyzer;

/// <summary>
/// Reads individual ions (neutral mass + charge state) from a .dmt SQLite database.
/// Neutral mass = (Mz * Charge) - (Charge * ProtonMass)
/// In I2MS each row is one individually-measured ion, so the charge is real per-ion
/// information rather than something inferred from an isotope envelope.
///
/// The file is validated up front (readable SQLite, an Ion table, Mz/Charge columns) so
/// malformed inputs fail with a clear message instead of an opaque SQLite error.
/// </summary>
public static class DmtParser
{
    private const double ProtonMass = 1.007825; // Da (hydrogen atom mass)

    public static IEnumerable<IonMeasurement> ReadIons(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("No .dmt file path was provided.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($".dmt file not found: {filePath}");

        SqliteConnection connection;
        try
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            connection.Open();
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(filePath)}' is not a readable SQLite/.dmt database: {ex.Message}", ex);
        }

        ValidateSchema(connection, filePath);
        return ReadIonsCore(connection);
    }

    /// <summary>Confirms the database has an Ion table with Mz and Charge columns.</summary>
    private static void ValidateSchema(SqliteConnection connection, string filePath)
    {
        string file = Path.GetFileName(filePath);

        bool hasIonTable;
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND lower(name)='ion' LIMIT 1;";
                hasIonTable = cmd.ExecuteScalar() is string;
            }

            if (hasIonTable)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA table_info('Ion');";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    columns.Add(reader.GetString(1)); // column 1 = name
            }
        }
        catch (SqliteException ex)
        {
            // A non-SQLite file typically fails here ("file is not a database").
            connection.Dispose();
            throw new InvalidDataException(
                $"'{file}' is not a readable SQLite/.dmt database: {ex.Message}", ex);
        }

        if (!hasIonTable)
        {
            connection.Dispose();
            throw new InvalidDataException(
                $"'{file}' has no 'Ion' table — it does not look like a .dmt ion list.");
        }

        if (!columns.Contains("Mz") || !columns.Contains("Charge"))
        {
            connection.Dispose();
            throw new InvalidDataException(
                $"'{file}' Ion table is missing required column(s) Mz/Charge " +
                $"(found: {string.Join(", ", columns)}).");
        }
    }

    private static IEnumerable<IonMeasurement> ReadIonsCore(SqliteConnection connection)
    {
        using (connection)
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT Mz, Charge FROM Ion " +
                "WHERE Mz IS NOT NULL AND Charge IS NOT NULL AND Charge != 0;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                double mz     = reader.GetDouble(0);
                double charge = reader.GetDouble(1);
                if (charge == 0) continue;
                double mass   = (mz * charge) - (charge * ProtonMass);
                yield return new IonMeasurement(mass, (int)Math.Round(charge));
            }
        }
    }
}
