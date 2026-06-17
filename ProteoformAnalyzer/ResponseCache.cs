using System.Security.Cryptography;
using System.Text;

namespace ProteoformAnalyzer;

/// <summary>
/// Best-effort, file-based cache of HTTP response bodies keyed by request URL.
/// Improves reproducibility (re-runs use the same fetched data) and avoids hammering
/// the upstream databases. Failures (read/write/parse) are swallowed so the cache can
/// never break a run — a cache miss just triggers a normal fetch.
/// </summary>
public static class ResponseCache
{
    public static bool Enabled { get; set; } = true;
    public static TimeSpan Ttl { get; set; } = TimeSpan.FromDays(7);

    private static readonly string Dir =
        Path.Combine(Path.GetTempPath(), "proteoform_analyzer_cache");

    private static string PathFor(string url)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(Dir, hash + ".json");
    }

    /// <summary>Returns a cached body if present and within the TTL.</summary>
    public static bool TryGet(string url, out string body)
    {
        body = "";
        if (!Enabled) return false;
        try
        {
            string path = PathFor(url);
            if (!File.Exists(path)) return false;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > Ttl) return false;
            body = File.ReadAllText(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Put(string url, string body)
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathFor(url), body);
        }
        catch
        {
            // Cache writes are best-effort; ignore failures.
        }
    }
}
