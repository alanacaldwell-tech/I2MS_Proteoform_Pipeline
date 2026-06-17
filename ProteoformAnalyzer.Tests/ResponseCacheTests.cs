using System;
using System.Threading;
using ProteoformAnalyzer;
using Xunit;

namespace ProteoformAnalyzer.Tests;

public class ResponseCacheTests
{
    [Fact]
    public void PutThenGet_RoundTripsBody()
    {
        bool prevEnabled = ResponseCache.Enabled;
        var prevTtl = ResponseCache.Ttl;
        try
        {
            ResponseCache.Enabled = true;
            ResponseCache.Ttl = TimeSpan.FromDays(7);

            string url = "https://example.test/" + Guid.NewGuid();
            ResponseCache.Put(url, "{\"hello\":1}");

            Assert.True(ResponseCache.TryGet(url, out string body));
            Assert.Equal("{\"hello\":1}", body);
        }
        finally
        {
            ResponseCache.Enabled = prevEnabled;
            ResponseCache.Ttl = prevTtl;
        }
    }

    [Fact]
    public void Disabled_AlwaysMisses()
    {
        bool prevEnabled = ResponseCache.Enabled;
        try
        {
            string url = "https://example.test/" + Guid.NewGuid();
            ResponseCache.Enabled = true;
            ResponseCache.Put(url, "cached");

            ResponseCache.Enabled = false;
            Assert.False(ResponseCache.TryGet(url, out _));
        }
        finally { ResponseCache.Enabled = prevEnabled; }
    }

    [Fact]
    public void ExpiredEntry_IsAMiss()
    {
        bool prevEnabled = ResponseCache.Enabled;
        var prevTtl = ResponseCache.Ttl;
        try
        {
            ResponseCache.Enabled = true;
            ResponseCache.Ttl = TimeSpan.FromMilliseconds(1);

            string url = "https://example.test/" + Guid.NewGuid();
            ResponseCache.Put(url, "stale");
            Thread.Sleep(25);

            Assert.False(ResponseCache.TryGet(url, out _));
        }
        finally
        {
            ResponseCache.Enabled = prevEnabled;
            ResponseCache.Ttl = prevTtl;
        }
    }
}
