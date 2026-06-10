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

// ── 1. Sequence or UniProt ID ─────────────────────────────────────────────
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

double tolerance = 5.0;
Console.Write("Mass tolerance in Da (default 5.0): ");
string? tolStr = Console.ReadLine()?.Trim();
if (!string.IsNullOrEmpty(tolStr) &&
    double.TryParse(tolStr, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out double parsedTol) && parsedTol > 0)
    tolerance = parsedTol;

// ── 6. Build proteoforms ──────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"Building proteoform list (truncations: {includeTrunc}, tolerance ±{tolerance} Da)...");

var proteoforms = ProteoformBuilder.Build(sequence, allPtms, includeTrunc, tolerance);
Console.WriteLine($"Generated {proteoforms.Count} proteoform entries.");

// ── 7. Export CSV ─────────────────────────────────────────────────────────
string defaultCsv = uniprotId is not null ? $"{uniprotId}_proteoforms.csv" : "proteoforms.csv";

string csvPath;
while (true)
{
    Console.Write($"\nOutput CSV path (press Enter for default: {defaultCsv}): ");
    string? raw = Console.ReadLine()?.Trim().Trim('"');  // strip surrounding quotes Windows may add
    csvPath = string.IsNullOrEmpty(raw) ? defaultCsv : raw;

    // Ensure the directory exists
    string? dir = Path.GetDirectoryName(csvPath);
    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
    {
        Console.WriteLine($"  Directory not found: {dir}");
        Console.WriteLine("  Please enter a path whose folder already exists.");
        continue;
    }

    try
    {
        CsvExporter.Export(proteoforms, csvPath);
        Console.WriteLine($"Saved: {Path.GetFullPath(csvPath)}");
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Could not save to that path: {ex.Message}");
        Console.WriteLine("  Please enter a different path.");
    }
}

// ── 8. Console preview ────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"{"Seq Pos",-22} {"Modification",-50} {"Mass (Da)",14}  {"Tol",10}  σ (Da)");
Console.WriteLine(new string('─', 105));

foreach (var pf in proteoforms.Take(15))
{
    string sig = pf.Envelope is not null ? $"{pf.Envelope.Sigma:F2}" : "";
    Console.WriteLine(
        $"{pf.SequencePosition,-22} {pf.ModificationName,-50} {pf.CentroidMass,14:F4}  {$"+/-{pf.Tolerance:F1}",10}  {sig}");
}

if (proteoforms.Count > 15)
    Console.WriteLine($"  ... and {proteoforms.Count - 15} more rows (see CSV).");

Console.WriteLine("\nDone.");
