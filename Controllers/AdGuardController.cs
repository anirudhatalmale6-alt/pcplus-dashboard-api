using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PCPlus.Dashboard.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/dns")]
    public class AdGuardController : ControllerBase
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<AdGuardController> _log;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        public AdGuardController(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AdGuardController> log)
        {
            _httpFactory = httpFactory;
            _config = config;
            _log = log;
        }

        private HttpClient CreateClient()
        {
            var client = _httpFactory.CreateClient("adguard");
            var url = _config["AdGuardHome:Url"] ?? "http://127.0.0.1:8083";
            var user = _config["AdGuardHome:User"] ?? "admin";
            var pass = _config["AdGuardHome:Password"] ?? "";
            client.BaseAddress = new Uri(url.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(15);
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
            return client;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync("/control/stats");
                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, "AdGuard Home API error");
                var json = await resp.Content.ReadAsStringAsync();
                var stats = JsonSerializer.Deserialize<AdGuardStats>(json, JsonOpts);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to fetch AdGuard stats");
                return StatusCode(502, new { error = ex.Message });
            }
        }

        [HttpGet("stats/client/{clientIp}")]
        public async Task<IActionResult> GetClientStats(string clientIp, [FromQuery] int limit = 500)
        {
            try
            {
                using var client = CreateClient();
                var url = $"/control/querylog?search={Uri.EscapeDataString(clientIp)}&limit={Math.Min(limit, 2000)}";
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, "AdGuard Home API error");
                var json = await resp.Content.ReadAsStringAsync();
                var log = JsonSerializer.Deserialize<QueryLogResponse>(json, JsonOpts);

                var entries = log?.Data ?? new();
                var blocked = entries.Where(e =>
                    e.Reason == "FilteredBlackList" ||
                    e.Reason == "FilteredBlockedService" ||
                    e.Reason == "FilteredSafeBrowsing" ||
                    e.Reason == "FilteredParental").ToList();

                var result = new ClientDnsStats
                {
                    ClientIp = clientIp,
                    TotalQueries = entries.Count,
                    BlockedQueries = blocked.Count,
                    BlockRate = entries.Count > 0 ? Math.Round((double)blocked.Count / entries.Count * 100, 1) : 0,
                    TopBlockedDomains = blocked
                        .GroupBy(e => (e.Question?.Name ?? "unknown").TrimEnd('.'))
                        .OrderByDescending(g => g.Count())
                        .Take(10)
                        .Select(g => new DomainCount { Domain = g.Key, Count = g.Count() })
                        .ToList(),
                    TopQueriedDomains = entries
                        .GroupBy(e => (e.Question?.Name ?? "unknown").TrimEnd('.'))
                        .OrderByDescending(g => g.Count())
                        .Take(10)
                        .Select(g => new DomainCount { Domain = g.Key, Count = g.Count() })
                        .ToList(),
                    LastBlocked = blocked
                        .OrderByDescending(e => e.Time)
                        .FirstOrDefault()?.Question?.Name?.TrimEnd('.') ?? ""
                };
                return Ok(result);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to fetch client DNS stats for {Ip}", clientIp);
                return StatusCode(502, new { error = ex.Message });
            }
        }

        [HttpGet("querylog")]
        public async Task<IActionResult> GetQueryLog([FromQuery] string? search, [FromQuery] int limit = 100)
        {
            try
            {
                using var client = CreateClient();
                var url = $"/control/querylog?limit={Math.Min(limit, 500)}";
                if (!string.IsNullOrEmpty(search))
                    url += $"&search={Uri.EscapeDataString(search)}";
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, "AdGuard Home API error");
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to fetch query log");
                return StatusCode(502, new { error = ex.Message });
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync("/control/status");
                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, "AdGuard Home API error");
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = ex.Message });
            }
        }

        [HttpGet("filtering/status")]
        public async Task<IActionResult> GetFilteringStatus()
        {
            try
            {
                using var client = CreateClient();
                var resp = await client.GetAsync("/control/filtering/status");
                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = ex.Message });
            }
        }
    }

    public class AdGuardStats
    {
        [JsonPropertyName("num_dns_queries")]
        public long NumDnsQueries { get; set; }
        [JsonPropertyName("num_blocked_filtering")]
        public long NumBlockedFiltering { get; set; }
        [JsonPropertyName("num_replaced_safebrowsing")]
        public long NumReplacedSafebrowsing { get; set; }
        [JsonPropertyName("num_replaced_parental")]
        public long NumReplacedParental { get; set; }
        [JsonPropertyName("avg_processing_time")]
        public double AvgProcessingTime { get; set; }
        [JsonPropertyName("top_queried_domains")]
        public List<Dictionary<string, long>>? TopQueriedDomains { get; set; }
        [JsonPropertyName("top_blocked_domains")]
        public List<Dictionary<string, long>>? TopBlockedDomains { get; set; }
        [JsonPropertyName("top_clients")]
        public List<Dictionary<string, long>>? TopClients { get; set; }
        [JsonPropertyName("dns_queries")]
        public List<long>? DnsQueries { get; set; }
        [JsonPropertyName("blocked_filtering")]
        public List<long>? BlockedFiltering { get; set; }
    }

    public class QueryLogResponse
    {
        public List<QueryLogEntry>? Data { get; set; }
    }

    public class QueryLogEntry
    {
        public string? Reason { get; set; }
        public string? Client { get; set; }
        public DateTime Time { get; set; }
        public QueryLogQuestion? Question { get; set; }
    }

    public class QueryLogQuestion
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
    }

    public class ClientDnsStats
    {
        public string ClientIp { get; set; } = "";
        public int TotalQueries { get; set; }
        public int BlockedQueries { get; set; }
        public double BlockRate { get; set; }
        public List<DomainCount> TopBlockedDomains { get; set; } = new();
        public List<DomainCount> TopQueriedDomains { get; set; } = new();
        public string LastBlocked { get; set; } = "";
    }

    public class DomainCount
    {
        public string Domain { get; set; } = "";
        public int Count { get; set; }
    }
}
