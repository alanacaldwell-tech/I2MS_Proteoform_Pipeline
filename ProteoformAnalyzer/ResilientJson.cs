using System.Net;
using System.Text.Json;

namespace ProteoformAnalyzer;

public enum FetchOutcome
{
    Success,        // fetched from network and deserialized
    CachedSuccess,  // served from the local cache
    NotFound,       // 404 — the resource genuinely does not exist
    Failed          // network/server error after retries, or a deserialization error
}

/// <summary>
/// HTTP JSON GET with retry + exponential backoff (honoring Retry-After), local response
/// caching, and provenance logging. Returns default(T) on failure so callers branch on the
/// returned <see cref="FetchOutcome"/> instead of handling raw HTTP. This centralises the
/// network-resilience policy that the individual database clients previously lacked.
/// </summary>
public static class ResilientJson
{
    public static int MaxRetries { get; set; } = 4;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<(T? Value, FetchOutcome Outcome)> GetAsync<T>(
        HttpClient http, string url, string source, bool useCache = true)
    {
        // 1. Cache
        if (useCache && ResponseCache.TryGet(url, out string cached))
        {
            try
            {
                var v = JsonSerializer.Deserialize<T>(cached, JsonOpts);
                RunManifest.RecordFetch(source, url, "cache");
                return (v, FetchOutcome.CachedSuccess);
            }
            catch
            {
                // Corrupt cache entry — fall through to a live fetch.
            }
        }

        // 2. Network with retry/backoff
        HttpStatusCode lastStatus = 0;
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var resp = await http.GetAsync(url);
                lastStatus = resp.StatusCode;

                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    RunManifest.RecordFetch(source, url, "404");
                    return (default, FetchOutcome.NotFound);
                }

                // Transient: rate-limited or server error → back off and retry.
                if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
                {
                    if (attempt == MaxRetries) break;
                    await BackoffAsync(attempt, resp);
                    continue;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    RunManifest.RecordFetch(source, url, resp.StatusCode.ToString());
                    return (default, FetchOutcome.Failed);
                }

                string body = await resp.Content.ReadAsStringAsync();
                if (useCache) ResponseCache.Put(url, body);
                var value = JsonSerializer.Deserialize<T>(body, JsonOpts);
                RunManifest.RecordFetch(source, url, "200");
                return (value, FetchOutcome.Success);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Transient network/timeout error — retry with backoff.
                if (attempt == MaxRetries)
                {
                    RunManifest.RecordFetch(source, url, $"error:{ex.GetType().Name}");
                    return (default, FetchOutcome.Failed);
                }
                await BackoffAsync(attempt, null);
            }
            catch (Exception ex)
            {
                // Non-retryable (e.g. malformed JSON) — fail cleanly so the run continues.
                RunManifest.RecordFetch(source, url, $"error:{ex.GetType().Name}");
                return (default, FetchOutcome.Failed);
            }
        }

        RunManifest.RecordFetch(source, url, $"giveup:{lastStatus}");
        return (default, FetchOutcome.Failed);
    }

    private static async Task BackoffAsync(int attempt, HttpResponseMessage? resp)
    {
        // Respect an explicit Retry-After header when the server provides one.
        var retryAfter = resp?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                await Task.Delay(delta);
                return;
            }
            if (retryAfter.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait);
                    return;
                }
            }
        }

        // Exponential backoff: 0.5s, 1s, 2s, 4s, ...
        await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)));
    }
}
