using System.Text.RegularExpressions;
using ProteoformAnalyzer;

Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║       Proteoform Analyzer  v2.0              ║");
Console.WriteLine("║  UniProt · PRIDE · PTMeXchange/EBI           ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.WriteLine();

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("User-Agent", "ProteoformAnalyzer/2.0 (research tool)");
http.DefaultRequestHeaders.Add("Accept", "application/json");
http.Timeout = TimeSpan.FromSeconds(30);

// ── Mode selection ────────────────────────────────────────────────────────
Console.WriteLine("Select mode:");
Console.WriteLine("  1  Interactive  — enter one protein sequence or UniProt ID");
Console.WriteLine("  2  Batch CSV    — provide a CSV file containing multiple proteins");
Console.Write("> ");
string? modeInput = Console.ReadLine()?.Trim();
Console.WriteLine();

if (modeInput == "2")
{
    // ── Batch CSV options ─────────────────────────────────────────────────
    string batchCsvPath;
    while (true)
    {
        Console.Write("Path to input CSV file: ");
        batchCsvPath = Console.ReadLine()?.Trim().Trim('"') ?? "";
        if (File.Exists(batchCsvPath)) break;
        Console.WriteLine($"  File not found: {batchCsvPath}");
    }

    Console.Write("Include N- and C-terminal truncations? (y/n, default y): ");
    bool batchTrunc = (Console.ReadLine()?.Trim().ToLower() ?? "y") != "n";

    double batchMatchTol = 2.0;
    Console.Write("Match tolerance in Da — max distance from peak centroid to database mass (default 2.0): ");
    string? batchMatchTolStr = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(batchMatchTolStr) &&
        double.TryParse(batchMatchTolStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double bmt) && bmt > 0)
        batchMatchTol = bmt;

    double batchIonWindow = 5.0;
    Console.Write("Ion-counting window in Da — signal summed within ±window of database mass (default 5.0): ");
    string? batchIonWinStr = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(batchIonWinStr) &&
        double.TryParse(batchIonWinStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double biw) && biw > 0)
        batchIonWindow = biw;

    Console.Write("Path to folder containing .dmt files (press Enter to skip): ");
    string? batchDmtFolder = Console.ReadLine()?.Trim().Trim('"');
    if (!string.IsNullOrEmpty(batchDmtFolder) && !Directory.Exists(batchDmtFolder))
    {
        Console.WriteLine("  Folder not found — skipping ion counting.");
        batchDmtFolder = null;
    }

    Console.WriteLine();
    await CsvBatchMode.RunAsync(batchCsvPath, http, batchTrunc, batchMatchTol, batchIonWindow, batchDmtFolder);
    return;
}

// ── 1. Sequence or UniProt ID (interactive mode) ──────────────────────────
string sequence = "";
string? uniprotId = null;

Console.WriteLine("Enter a protein sequence (single-letter codes) OR a UniProt accession (e.g. P04637).");
while (true)
{
    Console.Write("> ");
    string? raw = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(raw)) continue;

    if (Regex.IsMatch(raw, @"^[A-Z][0-9][A-Z0-9]{3}[0-9]$", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(raw, @"^[OPQ][0-9][A-Z0-9]{3}[0-9]$", RegexOptions.IgnoreCase))
    {
        // Looks like a UniProt accession
        uniprotId = raw.ToUpper();
        break;
    }

    string candidate = raw.ToUpper().Replace(" ", "").Replace("\t", "");
    var invalid = candidate.Where(c => !AminoAcidData.ResidueFormulas.ContainsKey(c)).Distinct().ToList();
    if (invalid.Count > 0)
    {
        Console.WriteLine($"  Unknown character(s): {string.Join(", ", invalid)}");
        Console.WriteLine("  Enter a valid amino acid sequence or a UniProt accession.");
        continue;
    }

    sequence = candidate;
    break;
}

// ── 2. Fetch from UniProt / databases if accession was given ──────────────
var allPtms = new List<PtmAnnotation>();

if (uniprotId is not null)
{
    Console.WriteLine();
    var uniprotClient = new UniProtClient(http);
    (sequence, var uniprotPtms) = await uniprotClient.FetchAsync(uniprotId);

    if (string.IsNullOrEmpty(sequence))
    {
        Console.WriteLine("Could not retrieve sequence from UniProt. Exiting.");
        return;
    }

    allPtms.AddRange(uniprotPtms);

    var prideClient = new PrideClient(http);
    var pridePtms = await prideClient.FetchAsync(uniprotId, sequence);
    allPtms.AddRange(pridePtms);

    var ptmExClient = new PtmExchangeClient(http);
    var ptmExPtms = await ptmExClient.FetchAsync(uniprotId, sequence);
    allPtms.AddRange(ptmExPtms);
}
else
{
    Console.WriteLine($"Sequence provided directly ({sequence.Length} aa). Skipping database queries.");
}

// ── 3. Summary ────────────────────────────────────────────────────────────
var intactFormula = AminoAcidData.GetFormula(sequence);
double intactMass = AminoAcidData.AverageMass(intactFormula);

Console.WriteLine();
Console.WriteLine($"Sequence  : {(sequence.Length <= 60 ? sequence : sequence[..57] + "...")}");
Console.WriteLine($"Length    : {sequence.Length} aa");
Console.WriteLine($"Formula   : {intactFormula}");
Console.WriteLine($"Avg mass  : {intactMass:F4} Da  (isotope envelope centroid)");
Console.WriteLine($"DB PTMs   : {allPtms.Count} annotations collected");

// ── 4. Custom modifications ───────────────────────────────────────────────
Console.WriteLine();
while (true)
{
    Console.Write("Add a custom modification? (y/n): ");
    if ((Console.ReadLine()?.Trim().ToLower() ?? "n") != "y") break;

    string modName;
    while (true)
    {
        Console.Write("  Modification name: ");
        modName = Console.ReadLine()?.Trim() ?? "";
        if (!string.IsNullOrEmpty(modName)) break;
    }

    double delta = 0;
    while (true)
    {
        Console.Write("  Mass change in Da (e.g. +79.966 or -18.011): ");
        string? d = Console.ReadLine()?.Trim();
        if (double.TryParse(d, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out delta)) break;
        Console.WriteLine("  Please enter a valid decimal number.");
    }

    int pos = 0;
    Console.Write("  Sequence position (1-based, 0 = whole-protein / unknown): ");
    int.TryParse(Console.ReadLine()?.Trim(), out pos);

    char? residue = pos > 0 && pos <= sequence.Length ? char.ToUpper(sequence[pos - 1]) : null;

    allPtms.Add(new PtmAnnotation
    {
        ModificationName = modName,
        Position = pos,
        Residue = residue,
        MassDelta = delta,
        Source = "Custom"
    });

    Console.WriteLine($"  Added: {modName} @ pos {(pos > 0 ? pos.ToString() : "N/A")} ({(delta >= 0 ? "+" : "")}{delta:F4} Da)");
}

// ── 5. Options ────────────────────────────────────────────────────────────
Console.WriteLine();
Console.Write("Include N- and C-terminal truncations? (y/n, default y): ");
bool includeTrunc = (Console.ReadLine()?.Trim().ToLower() ?? "y") != "n";

// ── 6. Build proteoform database ──────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"Building proteoform database (truncations: {includeTrunc})...");
var proteoforms = ProteoformBuilder.Build(sequence, allPtms, includeTrunc, tolerance: 5.0);
Console.WriteLine($"Generated {proteoforms.Count} database entries.");

// ── 7. .dmt spectrum analysis ─────────────────────────────────────────────
Console.WriteLine();
Console.Write("Path to folder containing .dmt files (press Enter to skip): ");
string? dmtFolder = Console.ReadLine()?.Trim().Trim('"');

double matchTol = 2.0;
double ionWindow = 5.0;
List<string> dmtFileNames = new();
List<AnalysisResult> analysisResults = new();

if (!string.IsNullOrEmpty(dmtFolder))
{
    if (!Directory.Exists(dmtFolder))
    {
        Console.WriteLine("  Folder not found — skipping spectrum analysis.");
    }
    else
    {
        Console.Write("Match tolerance in Da — max distance from peak centroid to database mass (default 2.0): ");
        string? matchTolStr = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(matchTolStr) &&
            double.TryParse(matchTolStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double mt) && mt > 0)
            matchTol = mt;

        Console.Write("Ion-counting window in Da — signal summed within ±window of database mass (default 5.0): ");
        string? ionWinStr = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(ionWinStr) &&
            double.TryParse(ionWinStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double iw) && iw > 0)
            ionWindow = iw;

        var dmtFiles = Directory.GetFiles(dmtFolder, "*.dmt", SearchOption.TopDirectoryOnly)
                                .OrderBy(f => f).ToArray();
        if (dmtFiles.Length == 0)
        {
            Console.WriteLine("  No .dmt files found.");
        }
        else
        {
            dmtFileNames = dmtFiles.Select(Path.GetFileName).ToList()!;
            Console.WriteLine($"  Found {dmtFiles.Length} .dmt file(s).");
            Console.WriteLine();

            // Key = (modName, predictedMass, roundedExptCentroid) so that multiple peaks
            // matching the same database entry appear as separate rows, while the same
            // peak detected across multiple files is merged into one row.
            var resultMap = new Dictionary<(string, double, long), AnalysisResult>();

            for (int f = 0; f < dmtFiles.Length; f++)
            {
                string fileName = dmtFileNames[f];
                Console.Write($"  [{f + 1}/{dmtFiles.Length}] {fileName} — binning & peak-finding ... ");

                try
                {
                    var matches = SpectrumAnalyzer.ProcessFile(dmtFiles[f], proteoforms, matchTol, ionWindow);
                    long totalIons = matches.Sum(m => m.IonCount);
                    Console.WriteLine($"{matches.Count} peak match(es), {totalIons:N0} ions");

                    foreach (var (entry, exptCentroid, ionCount) in matches)
                    {
                        long roundedCentroid = (long)Math.Round(exptCentroid);
                        var key = (entry.ModificationName, entry.CentroidMass, roundedCentroid);
                        if (!resultMap.TryGetValue(key, out var ar))
                        {
                            ar = new AnalysisResult
                            {
                                DatabaseEntry = entry,
                                ExperimentalCentroid = exptCentroid
                            };
                            resultMap[key] = ar;
                        }
                        ar.IonCountsPerFile[fileName] = ionCount;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR — {ex.Message}");
                }
            }

            // Fill zeros for files where a hit was not detected
            analysisResults = resultMap.Values
                .OrderBy(r => r.DatabaseEntry.ModificationName)
                .ThenBy(r => r.ExperimentalCentroid)
                .ToList();

            foreach (var ar in analysisResults)
                foreach (var fn in dmtFileNames)
                    ar.IonCountsPerFile.TryAdd(fn, 0);

            Console.WriteLine($"\n  {analysisResults.Count} hit(s) matched across all files.");
        }
    }
}

// ── 8. Export CSV ─────────────────────────────────────────────────────────
string defaultCsv = uniprotId is not null ? $"{uniprotId}_proteoforms.csv" : "proteoforms.csv";

string csvPath;
while (true)
{
    Console.Write($"\nOutput CSV path (press Enter for default: {defaultCsv}): ");
    string? raw = Console.ReadLine()?.Trim().Trim('"');
    csvPath = string.IsNullOrEmpty(raw) ? defaultCsv : raw;

    if (!Path.GetFileName(csvPath).Contains('.'))
        csvPath += ".csv";

    string? dir = Path.GetDirectoryName(csvPath);
    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
    {
        Console.WriteLine($"  Directory not found: {dir}");
        continue;
    }

    try
    {
        if (analysisResults.Count > 0)
            CsvExporter.ExportResults(analysisResults, dmtFileNames, csvPath);
        else
            CsvExporter.ExportDatabase(proteoforms, csvPath);

        Console.WriteLine($"Saved: {Path.GetFullPath(csvPath)}");
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Could not save: {ex.Message}");
    }
}

// ── 9. Console preview ────────────────────────────────────────────────────
Console.WriteLine();
if (analysisResults.Count > 0)
{
    Console.WriteLine($"{"Modification",-50} {"Pred. Mass",14}  {"Expt. Centroid",15}  {"Total Ions",12}");
    Console.WriteLine(new string('─', 97));
    foreach (var r in analysisResults.Take(15))
    {
        long total = r.IonCountsPerFile.Values.Sum();
        Console.WriteLine($"{r.DatabaseEntry.ModificationName,-50} " +
                          $"{r.DatabaseEntry.CentroidMass,14:F4}  " +
                          $"{r.ExperimentalCentroid,15:F4}  {total,12:N0}");
    }
    if (analysisResults.Count > 15)
        Console.WriteLine($"  ... and {analysisResults.Count - 15} more (see CSV).");
}
else
{
    Console.WriteLine($"{"Modification",-55} {"Pred. Mass (Da)",16}");
    Console.WriteLine(new string('─', 73));
    foreach (var pf in proteoforms.Take(15))
        Console.WriteLine($"{pf.ModificationName,-55} {pf.CentroidMass,16:F4}");
    if (proteoforms.Count > 15)
        Console.WriteLine($"  ... and {proteoforms.Count - 15} more (see CSV).");
}

Console.WriteLine("\nDone.");
