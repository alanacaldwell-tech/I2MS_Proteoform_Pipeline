namespace ProteoformAnalyzer;

/// <summary>
/// Collects provenance for a run — data sources queried (with status and time),
/// parameters used, and summary events — and writes it next to the output CSV as a
/// "&lt;output&gt;.manifest.txt" sidecar. This makes any result auditable and improves
/// reproducibility: the manifest records exactly which databases were hit (or served
/// from cache) and when.
///
/// Implemented as a process-wide collector because the data clients are scattered and a
/// single console run is sequential; <see cref="Reset"/> clears it between runs/tests.
/// </summary>
public static class RunManifest
{
    public const string ToolVersion = "2.1";

    private static readonly object Lock = new();
    private static readonly List<string> Events = new();
    private static DateTime _startedUtc = DateTime.UtcNow;

    public static void Reset()
    {
        lock (Lock)
        {
            Events.Clear();
            _startedUtc = DateTime.UtcNow;
        }
    }

    public static void Record(string line)
    {
        lock (Lock)
            Events.Add($"{DateTime.UtcNow:O}  {line}");
    }

    public static void RecordFetch(string source, string url, string status) =>
        Record($"FETCH  [{source}] {status,-8} {url}");

    public static void RecordParam(string key, string value) =>
        Record($"PARAM  {key} = {value}");

    /// <summary>Writes the manifest as "&lt;outputPath&gt;.manifest.txt". Never throws.</summary>
    public static void Write(string outputPath)
    {
        try
        {
            string path = outputPath + ".manifest.txt";
            using var w = new StreamWriter(path);
            w.WriteLine("Proteoform Analyzer — run manifest");
            w.WriteLine($"Tool version : {ToolVersion}");
            w.WriteLine($"Run started  : {_startedUtc:O} (UTC)");
            w.WriteLine($"Written      : {DateTime.UtcNow:O} (UTC)");
            w.WriteLine($"Cache        : {(ResponseCache.Enabled ? $"enabled (TTL {ResponseCache.Ttl.TotalDays:0} d)" : "disabled")}");
            w.WriteLine(new string('-', 70));
            lock (Lock)
                foreach (var e in Events)
                    w.WriteLine(e);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [manifest] could not write sidecar: {ex.Message}");
        }
    }
}
