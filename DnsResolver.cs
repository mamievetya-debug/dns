using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace QuickBypass;

public class DnsResolver
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(30),
        EnableMultipleHttp2Connections = true,
        UseProxy = false
    })
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private record CachedDnsEntry(IPAddress[] Addresses, DateTime Expiry);
    private readonly ConcurrentDictionary<string, CachedDnsEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Fast Anycast IPs for Discord categorized by service (pre-warmed for 0ms latency)
    private static readonly IPAddress[] DiscordApiIps =
    [
        IPAddress.Parse("162.159.135.232"),
        IPAddress.Parse("162.159.136.232"),
        IPAddress.Parse("162.159.137.232"),
        IPAddress.Parse("162.159.138.232"),
        IPAddress.Parse("162.159.128.233")
    ];

    private static readonly IPAddress[] DiscordGatewayIps =
    [
        IPAddress.Parse("162.159.130.234"),
        IPAddress.Parse("162.159.133.234"),
        IPAddress.Parse("162.159.134.234"),
        IPAddress.Parse("162.159.135.234"),
        IPAddress.Parse("162.159.136.234")
    ];

    private static readonly IPAddress[] DiscordCdnIps =
    [
        IPAddress.Parse("162.159.129.233"),
        IPAddress.Parse("162.159.130.233"),
        IPAddress.Parse("162.159.133.233"),
        IPAddress.Parse("162.159.134.233"),
        IPAddress.Parse("162.159.135.233")
    ];

    private static readonly IPAddress[] DiscordMediaIps =
    [
        IPAddress.Parse("162.159.128.235"),
        IPAddress.Parse("162.159.129.235"),
        IPAddress.Parse("162.159.130.235"),
        IPAddress.Parse("162.159.137.234"),
        IPAddress.Parse("162.159.138.234")
    ];

    private static readonly IPAddress[] RobloxAnycastIps =
    [
        IPAddress.Parse("128.116.119.3"),
        IPAddress.Parse("128.116.119.4"),
        IPAddress.Parse("128.116.119.5")
    ];

    public DnsResolver()
    {
        var initialExpiry = DateTime.UtcNow.AddHours(24);

        // Discord Gateway (WebSocket - Critical for instant Discord load!)
        _cache["gateway.discord.gg"] = new CachedDnsEntry(DiscordGatewayIps, initialExpiry);

        // Discord CDN & Assets
        _cache["cdn.discordapp.com"] = new CachedDnsEntry(DiscordCdnIps, initialExpiry);
        _cache["discordapp.com"] = new CachedDnsEntry(DiscordCdnIps, initialExpiry);
        _cache["dl.discordapp.net"] = new CachedDnsEntry(DiscordCdnIps, initialExpiry);
        _cache["media.discordapp.net"] = new CachedDnsEntry(DiscordCdnIps, initialExpiry);
        _cache["images-ext-1.discordapp.net"] = new CachedDnsEntry(DiscordCdnIps, initialExpiry);
        _cache["images-ext-2.discordapp.net"] = new CachedDnsEntry(DiscordCdnIps, initialExpiry);

        // Discord Media / Voice
        _cache["latency.discord.media"] = new CachedDnsEntry(DiscordMediaIps, initialExpiry);
        _cache["router.discordapp.net"] = new CachedDnsEntry(DiscordMediaIps, initialExpiry);

        // Discord API & Web
        _cache["discord.com"] = new CachedDnsEntry(DiscordApiIps, initialExpiry);
        _cache["www.discord.com"] = new CachedDnsEntry(DiscordApiIps, initialExpiry);
        _cache["updates.discord.com"] = new CachedDnsEntry(DiscordApiIps, initialExpiry);
        _cache["status.discord.com"] = new CachedDnsEntry(DiscordApiIps, initialExpiry);

        _cache["roblox.com"] = new CachedDnsEntry(RobloxAnycastIps, initialExpiry);
        _cache["www.roblox.com"] = new CachedDnsEntry(RobloxAnycastIps, initialExpiry);

        // 2. Background task to refresh DNS asynchronously without blocking user
        _ = Task.Run(PreWarmInParallelAsync);
    }

    private async Task PreWarmInParallelAsync()
    {
        try
        {
            var tasks = new List<Task>();
            foreach (var domain in _cache.Keys)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var ips = await ResolveFastRaceAsync(domain, CancellationToken.None);
                        if (ips.Length > 0)
                        {
                            _cache[domain] = new CachedDnsEntry(ips, DateTime.UtcNow.AddHours(2));
                        }
                    }
                    catch { }
                }));
            }
            await Task.WhenAll(tasks);
        }
        catch { }
    }

    public async Task<IPAddress[]> ResolveAsync(string hostname, CancellationToken ct = default)
    {
        if (IPAddress.TryParse(hostname, out var ip))
        {
            return [ip];
        }

        if (hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return [IPAddress.Loopback];
        }

        // Check in-memory cache (Instant 0.00 ms return!)
        if (_cache.TryGetValue(hostname, out var entry) && DateTime.UtcNow < entry.Expiry)
        {
            return entry.Addresses;
        }

        // Fast Parallel Race: Query Cloudflare and Google simultaneously; fastest response wins!
        var resolvedIps = await ResolveFastRaceAsync(hostname, ct);
        if (resolvedIps.Length > 0)
        {
            _cache[hostname] = new CachedDnsEntry(resolvedIps, DateTime.UtcNow.AddMinutes(30));
            return resolvedIps;
        }

        // Fallback to standard DNS if DoH failed
        try
        {
            var fallback = await Dns.GetHostAddressesAsync(hostname, ct);
            if (fallback.Length > 0)
            {
                _cache[hostname] = new CachedDnsEntry(fallback, DateTime.UtcNow.AddMinutes(5));
                return fallback;
            }
        }
        catch { }

        throw new WebException($"Host cannot be resolved: {hostname}");
    }

    /// <summary>
    /// Simultaneously queries primary DoH endpoints and takes the fastest responder.
    /// </summary>
    private async Task<IPAddress[]> ResolveFastRaceAsync(string hostname, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(2));

        var q1 = QueryDoHAsync("https://1.1.1.1/dns-query", hostname, linkedCts.Token);
        var q2 = QueryDoHAsync("https://1.0.0.1/dns-query", hostname, linkedCts.Token);
        var q3 = QueryDoHAsync("https://8.8.8.8/resolve", hostname, linkedCts.Token);

        var queryList = new List<Task<IPAddress[]>> { q1, q2, q3 };

        while (queryList.Count > 0)
        {
            var completedTask = await Task.WhenAny(queryList);
            queryList.Remove(completedTask);

            try
            {
                var ips = await completedTask;
                if (ips.Length > 0)
                {
                    linkedCts.Cancel(); // Cancel other slower queries
                    return ips;
                }
            }
            catch { }
        }

        return [];
    }

    private async Task<IPAddress[]> QueryDoHAsync(string endpoint, string hostname, CancellationToken ct)
    {
        var url = $"{endpoint}?name={Uri.EscapeDataString(hostname)}&type=A";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Accept", "application/dns-json");

        using var response = await HttpClient.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
            return [];

        var result = await response.Content.ReadFromJsonAsync<DohResponse>(cancellationToken: ct);
        if (result?.Answer == null || result.Answer.Length == 0)
            return [];

        var ips = new List<IPAddress>();
        foreach (var ans in result.Answer)
        {
            if (ans.Type == 1 && IPAddress.TryParse(ans.Data, out var ipv4)) // Type 1: A record
            {
                ips.Add(ipv4);
            }
        }

        return ips.ToArray();
    }

    private class DohResponse
    {
        [JsonPropertyName("Status")] public int Status { get; set; }
        [JsonPropertyName("Answer")] public DohAnswer[]? Answer { get; set; }
    }

    private class DohAnswer
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("type")] public int Type { get; set; }
        [JsonPropertyName("TTL")] public int Ttl { get; set; }
        [JsonPropertyName("data")] public string? Data { get; set; }
    }
}
