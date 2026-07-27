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

    Console.Write("Estimate false-discovery rate with decoys? (y/n, default y): ");
    bool batchEstimateFdr = (Console.ReadLine()?.Trim().ToLower() ?? "y") != "n";

    int batchMaxOccupancy = 12;
    Console.Write("Max simultaneous modifications per type (default 12): ");
    string? batchMaxOccStr = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(batchMaxOccStr) &&
        int.TryParse(batchMaxOccStr, out int bmo) && bmo >= 1)
        batchMaxOccupancy = bmo;

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

    Console.Write("Check for contaminating proteins? (y/n, default n): ");
    List<string>? batchContaminants = null;
    if ((Console.ReadLine()?.Trim().ToLower() ?? "n") == "y")
    {
        batchContaminants = new List<string>();
        while (true)
        {
            Console.Write("  Contaminant UniProt accession (press Enter to finish): ");
            string? cId = Console.ReadLine()?.Trim().ToUpper();
            if (string.IsNullOrEmpty(cId)) break;
            batchContaminants.Add(cId);
        }
    }

    Console.WriteLine();
    await CsvBatchMode.RunAsync(batchCsvPath, http, batchTrunc, batchMatchTol, batchIonWindow, batchDmtFolder, batchContaminants, batchMaxOccupancy, batchEstimateFdr);
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

// Label used to identify this protein in the output. For an accession it is the accession;
// for a raw sequence the user must supply a name (collected below).
string? proteinName = null;

// Offset added to the working-sequence positions to recover the original full-length numbering in
// the output (truncation ranges). It accumulates a recombinant→reference shift (from the alignment)
// and/or a region→sequence shift (from a region restriction). 0 means the numbers are already
// full-length. regionSuffix records a chosen region for the label/filename.
int residueOffset = 0;
string regionSuffix = "";

// Known single-residue substitutions (point mutations) pulled from UniProt, in reference numbering.
var knownVariants = new List<PointVariant>();

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

    // Known point mutations (offered as an option later); reuses the cached UniProt record.
    knownVariants = await uniprotClient.FetchVariantsAsync(uniprotId);
}
else
{
    Console.WriteLine($"Sequence provided directly ({sequence.Length} aa).");

    // A raw sequence carries no accession, so require a name to identify it in the output.
    while (true)
    {
        Console.Write("Enter a name for this protein: ");
        proteinName = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(proteinName)) break;
        Console.WriteLine("  A name is required when a sequence is provided.");
    }

    // Optional: treat this as a recombinant/tagged construct and import known PTMs from a UniProt
    // reference. The reference sequence is aligned to the construct so PTM positions are remapped
    // (tags/linkers fall outside the alignment and their PTMs are dropped).
    Console.Write("Map known PTMs from a UniProt reference (e.g. a tagged construct)? Enter accession or press Enter to skip: ");
    string? refAcc = Console.ReadLine()?.Trim().ToUpper();
    if (!string.IsNullOrEmpty(refAcc))
    {
        Console.WriteLine();
        var refClient = new UniProtClient(http);
        var (refSeq, refUniProtPtms) = await refClient.FetchAsync(refAcc);

        if (string.IsNullOrEmpty(refSeq))
        {
            Console.WriteLine($"  Could not retrieve reference {refAcc} — proceeding with no imported PTMs.");
        }
        else
        {
            var refPtms = new List<PtmAnnotation>(refUniProtPtms);
            refPtms.AddRange(await new PrideClient(http).FetchAsync(refAcc, refSeq));
            refPtms.AddRange(await new PtmExchangeClient(http).FetchAsync(refAcc, refSeq));

            var map = SequenceAligner.MapReferenceToQuery(refSeq, sequence);
            int mapped = 0;
            foreach (var p in refPtms)
            {
                if (p.Position > 0 && map.TryGetValue(p.Position, out int q))
                {
                    allPtms.Add(new PtmAnnotation
                    {
                        ModificationName = p.ModificationName,
                        Position = q,
                        Residue = char.ToUpper(sequence[q - 1]),
                        MassDelta = p.MassDelta,
                        Source = p.Source
                    });
                    mapped++;
                }
            }

            if (map.Count == 0)
                Console.WriteLine($"  Could not align the construct to {refAcc} (no matching region found) — no PTMs imported.");
            else
            {
                // Number the output by the reference's original positions: shift construct positions by
                // (referencePos - constructPos) measured at the start of the aligned native region.
                var anchor = map.OrderBy(kv => kv.Value).First();   // smallest construct (query) position
                residueOffset = anchor.Key - anchor.Value;

                // Keep known variants whose reference residue is present in the construct.
                var refVariants = await refClient.FetchVariantsAsync(refAcc);
                knownVariants = refVariants.Where(v => map.ContainsKey(v.Position)).ToList();

                Console.WriteLine($"  Aligned to {refAcc}: {map.Count}/{refSeq.Length} reference residues matched; " +
                                  $"imported {mapped} of {refPtms.Count} known PTM annotation(s). " +
                                  $"Output numbered by {refAcc} positions.");
            }
        }
    }
}

// ── 2b. Optional region restriction ───────────────────────────────────────
// Lets the user focus on a known fragment, e.g. a cleavage product (the C-terminus
// of MUC1 from residue 1098). The sequence is sliced to the chosen region, PTMs are
// kept only if they fall inside it (and remapped to the fragment), and proteoforms —
// including further truncations — are built from the fragment. Truncation ranges are
// still displayed in the original UniProt coordinates via the residue offset.
Console.WriteLine();
Console.WriteLine("Restrict analysis to a sequence region? Examples: \"1098-1255\", \"1098-\" (to the C-terminus),");
Console.Write($"\"-500\" (from the N-terminus). Press Enter for the whole sequence (1-{sequence.Length}): ");
string? regionInput = Console.ReadLine()?.Trim();
while (!string.IsNullOrEmpty(regionInput))
{
    int fullLen = sequence.Length;
    int start = 1, end = fullLen;
    bool ok = true;

    var parts = regionInput.Split('-');
    if (parts.Length == 1)
    {
        ok = int.TryParse(parts[0].Trim(), out start);   // single number → start to C-terminus
    }
    else if (parts.Length == 2)
    {
        ok = string.IsNullOrWhiteSpace(parts[0]) || int.TryParse(parts[0].Trim(), out start);
        if (string.IsNullOrWhiteSpace(parts[0])) start = 1;
        if (ok && !string.IsNullOrWhiteSpace(parts[1])) ok = int.TryParse(parts[1].Trim(), out end);
    }
    else ok = false;

    if (!ok || start < 1 || end > fullLen || start > end)
    {
        Console.Write($"  Invalid range. Enter start-end within 1-{fullLen} (or press Enter to skip): ");
        regionInput = Console.ReadLine()?.Trim();
        continue;
    }

    sequence = sequence.Substring(start - 1, end - start + 1);
    int s = start, e = end;
    // Keep PTMs that are positioned inside the region (remapped to fragment coordinates) and
    // whole-protein / unknown-position PTMs (Position ≤ 0, carried over unchanged). PTMs with a
    // defined position outside the region are dropped.
    allPtms = allPtms
        .Where(p => p.Position <= 0 || (p.Position >= s && p.Position <= e))
        .Select(p => new PtmAnnotation
        {
            ModificationName = p.ModificationName,
            Position = p.Position <= 0 ? 0 : p.Position - (s - 1),
            Residue = p.Position <= 0 ? null : p.Residue,
            MassDelta = p.MassDelta,
            Source = p.Source
        })
        .ToList();
    // Keep only variants whose (original-numbered) position lies within the region.
    knownVariants = knownVariants.Where(v => v.Position >= s && v.Position <= e).ToList();

    // Accumulate onto any recombinant→reference offset so combined use stays in reference numbering.
    residueOffset += start - 1;
    regionSuffix = $":{start}-{end}";
    Console.WriteLine($"  Restricted to residues {start}-{end} ({sequence.Length} aa); {allPtms.Count} PTM annotation(s) retained (in-region + whole-protein).");
    break;
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

// Truncation-depth cap and internal (both-ends) fragments. Both bound processing time on long
// proteins: internal fragments are O(depth²), so they are opt-in and gated by the cap.
int maxTerminusDepth = 0;      // 0 = no cap (single-ended truncations span the whole sequence)
bool includeInternal = false;
int minFragmentLength = 1;      // no minimum unless internal fragments are enabled
if (includeTrunc)
{
    Console.Write("Max residues to remove from each terminus (Enter = no limit): ");
    string? depthStr = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(depthStr) && int.TryParse(depthStr, out int d) && d >= 1)
        maxTerminusDepth = d;

    Console.Write("Also include internal fragments (truncated at BOTH termini)? (y/n, default n): ");
    includeInternal = (Console.ReadLine()?.Trim().ToLower() ?? "n") == "y";
    if (includeInternal)
    {
        if (maxTerminusDepth == 0)
        {
            maxTerminusDepth = 50;   // internal fragments are O(depth²) — require a cap
            Console.WriteLine($"  Internal fragments require a per-terminus cap; using {maxTerminusDepth}.");
        }
        minFragmentLength = 20;      // default minimum for internal fragments
        Console.Write($"Minimum fragment length in residues (default {minFragmentLength}): ");
        string? minStr = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(minStr) && int.TryParse(minStr, out int ml) && ml >= 1)
            minFragmentLength = ml;
    }
}

// Known point mutations (UniProt "Natural variant" substitutions). At most one per proteoform,
// applied to the intact and single-PTM forms only, so it stays a bounded multiplier.
bool includeVariants = false;
if (knownVariants.Count > 0)
{
    Console.Write($"Search for known point mutations? {knownVariants.Count} UniProt variant(s) available (y/n, default n): ");
    includeVariants = (Console.ReadLine()?.Trim().ToLower() ?? "n") == "y";
}

int maxOccupancy = 12;
Console.Write("Max simultaneous modifications per type (default 12, e.g. limits hyperphosphorylated proteins): ");
string? maxOccStr = Console.ReadLine()?.Trim();
if (!string.IsNullOrEmpty(maxOccStr) &&
    int.TryParse(maxOccStr, out int mo) && mo >= 1)
    maxOccupancy = mo;

var variantsForBuild = includeVariants ? knownVariants : new List<PointVariant>();

// ── 6. Build proteoform database ──────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"Building proteoform database (truncations: {includeTrunc}, internal fragments: {includeInternal}, " +
                  $"point mutations: {includeVariants}, max occupancy per PTM type: {maxOccupancy})...");
var proteoforms = ProteoformBuilder.Build(sequence, allPtms, includeTrunc, tolerance: 5.0,
                                         proteinLabel: (uniprotId ?? proteinName ?? "Target") + regionSuffix,
                                         maxOccupancyPerFamily: maxOccupancy,
                                         residueOffset: residueOffset,
                                         variants: variantsForBuild,
                                         maxTerminusDepth: maxTerminusDepth,
                                         includeInternalFragments: includeInternal,
                                         minFragmentLength: minFragmentLength);
Console.WriteLine($"Generated {proteoforms.Count} database entries.");

// ── 6b. Contaminant proteins ──────────────────────────────────────────────
// If the user wants to check for contaminating proteins, fetch their databases
// and merge them into the main list.  Target entries are labelled so results
// from each protein are distinguishable in the output.
Console.WriteLine();
Console.Write("Check for contaminating proteins? (y/n, default n): ");
bool searchContaminants = (Console.ReadLine()?.Trim().ToLower() ?? "n") == "y";
if (searchContaminants)
{
    var contUniProtClient = new UniProtClient(http);
    var contPrideClient   = new PrideClient(http);
    var contPtmExClient   = new PtmExchangeClient(http);

    while (true)
    {
        Console.Write("  Contaminant UniProt accession (press Enter to finish): ");
        string? contId = Console.ReadLine()?.Trim().ToUpper();
        if (string.IsNullOrEmpty(contId)) break;

        Console.Write($"  Fetching {contId} ... ");
        var (contSeq, contUniProtPtms) = await contUniProtClient.FetchAsync(contId);
        if (string.IsNullOrEmpty(contSeq)) { Console.WriteLine("not found — skipping."); continue; }

        var contPridePtms  = await contPrideClient.FetchAsync(contId, contSeq);
        var contPtmExPtms  = await contPtmExClient.FetchAsync(contId, contSeq);
        var contAllPtms    = contUniProtPtms.Concat(contPridePtms).Concat(contPtmExPtms).ToList();

        var contEntries = ProteoformBuilder.Build(contSeq, contAllPtms, includeTrunc, tolerance: 5.0,
                                                   proteinLabel: contId,
                                                   maxOccupancyPerFamily: maxOccupancy);

        proteoforms.AddRange(contEntries);
        Console.WriteLine($"done — {contEntries.Count} entries added ({contAllPtms.Count} PTM annotations).");
    }
}

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

        Console.Write("Estimate false-discovery rate with decoys? (y/n, default y): ");
        bool estimateFdr = (Console.ReadLine()?.Trim().ToLower() ?? "y") != "n";

        // Add decoy proteoforms so chance matches can be quantified (target–decoy FDR).
        var searchDb = proteoforms;
        if (estimateFdr)
        {
            var decoys = DecoyGenerator.Generate(proteoforms);
            searchDb = proteoforms.Concat(decoys).ToList();
            Console.WriteLine($"  Added {decoys.Count} decoy proteoforms for FDR estimation.");
        }

        RunManifest.RecordParam("matchTolerance", matchTol.ToString("F2"));
        RunManifest.RecordParam("ionWindow", ionWindow.ToString("F2"));
        RunManifest.RecordParam("truncations", includeTrunc.ToString());
        RunManifest.RecordParam("maxOccupancyPerFamily", maxOccupancy.ToString());
        RunManifest.RecordParam("estimateFdr", estimateFdr.ToString());

        // Unmatched peaks are only reported when the user is screening for contaminants.
        var (results, fileNames) = SpectrumBatch.AnalyzeFolder(
            dmtFolder, searchDb, matchTol, ionWindow,
            includeUnmatchedPeaks: searchContaminants);

        if (estimateFdr)
        {
            var fdr = FdrEstimator.Estimate(results);
            Console.WriteLine($"  {fdr.Describe()}");
            RunManifest.Record($"FDR: {fdr.Describe()}");
            results = results.Where(r => !r.DatabaseEntry.IsDecoy).ToList();
        }

        analysisResults = results;
        dmtFileNames    = fileNames;
    }
}

// ── 8. Export CSV ─────────────────────────────────────────────────────────
string outputLabel = (uniprotId ?? proteinName ?? "proteoforms") + regionSuffix;
string safeLabel = string.Concat(outputLabel.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
string defaultCsv = $"{safeLabel}_proteoforms.csv";

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
        RunManifest.Write(csvPath);
        Console.WriteLine($"Run manifest: {Path.GetFullPath(csvPath)}.manifest.txt");
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
