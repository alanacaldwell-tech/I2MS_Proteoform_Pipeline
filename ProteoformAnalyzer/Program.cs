using ProteoformAnalyzer;

Console.WriteLine("=== Proteoform Analyzer ===");
Console.WriteLine();

// ── 1. Get and validate protein sequence ──────────────────────────────────
string sequence = "";
while (true)
{
    Console.Write("Enter protein sequence (single-letter amino acid codes): ");
    string? input = Console.ReadLine()?.Trim().ToUpper();
    if (string.IsNullOrEmpty(input))
    {
        Console.WriteLine("  Sequence cannot be empty. Please try again.");
        continue;
    }

    var invalid = input.Where(c => !AminoAcidMasses.Residue.ContainsKey(c)).Distinct().ToList();
    if (invalid.Count > 0)
    {
        Console.WriteLine($"  Unknown residue(s): {string.Join(", ", invalid)}. Please use standard single-letter codes.");
        continue;
    }

    sequence = input;
    break;
}

Console.WriteLine($"\nSequence accepted: {sequence}");
Console.WriteLine($"Length: {sequence.Length} residues");
Console.WriteLine($"Base monoisotopic mass: {AminoAcidMasses.CalculateMass(sequence):F4} Da");

// ── 2. Set up database ────────────────────────────────────────────────────
var db = new ModificationDatabase();
Console.WriteLine($"\nModification database loaded: {db.Modifications.Count} modifications.");

// ── 3. Custom modifications ───────────────────────────────────────────────
Console.WriteLine();
while (true)
{
    Console.Write("Add a custom modification? (y/n): ");
    string? answer = Console.ReadLine()?.Trim().ToLower();
    if (answer != "y") break;

    var custom = PromptCustomModification();
    db.AddCustomModification(custom);
    Console.WriteLine($"  Added: {custom}");
}

// ── 4. Options ────────────────────────────────────────────────────────────
Console.WriteLine();
Console.Write("Include N- and C-terminal truncations? (y/n, default y): ");
bool includeTruncations = (Console.ReadLine()?.Trim().ToLower() ?? "y") != "n";

double tolerance = 5.0;
Console.Write("Mass tolerance in Da (default 5.0): ");
string? tolInput = Console.ReadLine()?.Trim();
if (!string.IsNullOrEmpty(tolInput) && double.TryParse(tolInput, out double parsedTol) && parsedTol > 0)
    tolerance = parsedTol;

Console.WriteLine($"\nBuilding proteoform list (truncations: {includeTruncations}, tolerance: +/- {tolerance} Da)...");

// ── 5. Build proteoforms ──────────────────────────────────────────────────
var proteoforms = ProteoformBuilder.Build(sequence, db, includeTruncations, tolerance);
Console.WriteLine($"Generated {proteoforms.Count} proteoforms.");

// ── 6. Export CSV ─────────────────────────────────────────────────────────
Console.WriteLine();
Console.Write("Output CSV file path (default: proteoforms.csv): ");
string? csvPath = Console.ReadLine()?.Trim();
if (string.IsNullOrEmpty(csvPath))
    csvPath = "proteoforms.csv";

CsvExporter.Export(proteoforms, csvPath);
Console.WriteLine($"Exported to: {Path.GetFullPath(csvPath)}");

// ── 7. Preview first 10 rows ──────────────────────────────────────────────
Console.WriteLine("\n--- Preview (first 10 proteoforms) ---");
Console.WriteLine($"{"Modification",-60} {"Mass (Da)",12}  {"Tolerance",12}");
Console.WriteLine(new string('-', 88));
foreach (var pf in proteoforms.Take(10))
    Console.WriteLine($"{pf.Description,-60} {pf.MassFormatted,12}  {pf.ToleranceFormatted,12}");
if (proteoforms.Count > 10)
    Console.WriteLine($"  ... and {proteoforms.Count - 10} more rows in the CSV.");

Console.WriteLine("\nDone.");

// ─────────────────────────────────────────────────────────────────────────
static Modification PromptCustomModification()
{
    var mod = new Modification();

    Console.Write("  Modification name: ");
    mod.Name = Console.ReadLine()?.Trim() ?? "Custom";

    Console.Write("  Category (e.g., Phosphorylation, Methylation, Other): ");
    mod.Category = Console.ReadLine()?.Trim() ?? "Custom";

    while (true)
    {
        Console.Write("  Mass delta in Da (e.g., +79.96633 or -18.01056): ");
        string? raw = Console.ReadLine()?.Trim();
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double delta))
        {
            mod.MassDelta = delta;
            break;
        }
        Console.WriteLine("  Invalid number. Please enter a decimal value.");
    }

    Console.Write("  Type: (1) Residue-specific  (2) N-terminal  (3) C-terminal  [1]: ");
    string? typeChoice = Console.ReadLine()?.Trim();
    if (typeChoice == "2") mod.IsNTerminal = true;
    else if (typeChoice == "3") mod.IsCTerminal = true;

    if (!mod.IsNTerminal && !mod.IsCTerminal)
    {
        Console.Write("  Target residues (e.g., STY) or leave blank for any: ");
        string? residues = Console.ReadLine()?.Trim().ToUpper();
        if (!string.IsNullOrEmpty(residues))
            mod.TargetResidues = residues.Where(c => AminoAcidMasses.Residue.ContainsKey(c)).ToList();
    }
    else
    {
        Console.Write("  Specific terminal residue required? (leave blank for any): ");
        string? res = Console.ReadLine()?.Trim().ToUpper();
        if (!string.IsNullOrEmpty(res))
            mod.TargetResidues = res.Where(c => AminoAcidMasses.Residue.ContainsKey(c)).ToList();
    }

    return mod;
}
