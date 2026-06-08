using IonCounter;

Console.WriteLine("==============================================");
Console.WriteLine("  I2MS Proteoform Ion Counter");
Console.WriteLine("==============================================");
Console.WriteLine();

// ── 1. Reference CSV ─────────────────────────────────────────────────────────

string csvPath;
while (true)
{
    Console.Write("Enter path to the reference CSV file: ");
    csvPath = Console.ReadLine()?.Trim().Trim('"') ?? string.Empty;

    if (File.Exists(csvPath)) break;
    Console.WriteLine($"  File not found: {csvPath}");
}

List<CentroidEntry> entries;
string[] originalHeaders;
try
{
    (entries, originalHeaders) = CsvReader.Parse(csvPath);
}
catch (Exception ex)
{
    Console.WriteLine($"Error reading reference CSV: {ex.Message}");
    return 1;
}

Console.WriteLine($"  Loaded {entries.Count} centroid(s) from reference CSV.");

// ── 2. Folder of .dmt files ───────────────────────────────────────────────────

string dmtFolder;
while (true)
{
    Console.Write("Enter path to the folder containing .dmt files: ");
    dmtFolder = Console.ReadLine()?.Trim().Trim('"') ?? string.Empty;

    if (Directory.Exists(dmtFolder)) break;
    Console.WriteLine($"  Directory not found: {dmtFolder}");
}

var dmtFiles = Directory.GetFiles(dmtFolder, "*.dmt", SearchOption.TopDirectoryOnly)
                        .OrderBy(f => f)
                        .ToArray();

if (dmtFiles.Length == 0)
{
    Console.WriteLine("No .dmt files found in the specified folder. Exiting.");
    return 1;
}

Console.WriteLine($"  Found {dmtFiles.Length} .dmt file(s).");

// ── 3. Process each .dmt file ────────────────────────────────────────────────

var dmtFileNames = dmtFiles.Select(Path.GetFileName).ToList()!;

Console.WriteLine();
Console.WriteLine("Processing files...");

for (int f = 0; f < dmtFiles.Length; f++)
{
    string filePath = dmtFiles[f];
    string fileName = dmtFileNames[f];

    Console.Write($"  [{f + 1}/{dmtFiles.Length}] {fileName} ... ");

    // Initialise counts for this file on every centroid entry.
    var counts = new long[entries.Count];

    try
    {
        foreach (double mass in DmtParser.ReadMasses(filePath))
        {
            for (int e = 0; e < entries.Count; e++)
            {
                if (entries[e].Contains(mass))
                    counts[e]++;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR — {ex.Message}");
        // Record zeros and continue with remaining files.
        for (int e = 0; e < entries.Count; e++)
            entries[e].Counts.Add((fileName, 0));
        continue;
    }

    long total = 0;
    for (int e = 0; e < entries.Count; e++)
    {
        entries[e].Counts.Add((fileName, counts[e]));
        total += counts[e];
    }

    Console.WriteLine($"done  ({total} matching ion(s) across all centroids)");
}

// ── 4. Write output CSV ───────────────────────────────────────────────────────

string outputDir = Path.GetDirectoryName(csvPath) ?? Directory.GetCurrentDirectory();
string baseName  = Path.GetFileNameWithoutExtension(csvPath);
string outputPath = Path.Combine(outputDir, baseName + "_ion_counts.csv");

try
{
    CsvWriter.Write(outputPath, entries, originalHeaders, dmtFileNames);
}
catch (Exception ex)
{
    Console.WriteLine($"Error writing output CSV: {ex.Message}");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"Output written to: {outputPath}");

// ── 5. Summary table ─────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("Summary (centroid → total ions across all files):");
foreach (var entry in entries)
{
    long grand = entry.Counts.Sum(c => c.Count);
    Console.WriteLine($"  {entry.CentroidMass,14:G}  ±{entry.Tolerance:G} Da  →  {grand,8} ion(s)");
}

return 0;
