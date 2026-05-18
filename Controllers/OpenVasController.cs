using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PCPlus.Dashboard.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/vulnerability")]
    public class OpenVasController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<OpenVasController> _log;
        private static readonly SemaphoreSlim _gmpLock = new(1, 1);
        private static string? _cachedTasks;
        private static string? _cachedLatestReport;
        private static DateTime _tasksCacheExpiry;
        private static DateTime _reportCacheExpiry;

        public OpenVasController(IConfiguration config, ILogger<OpenVasController> log)
        {
            _config = config;
            _log = log;
        }

        [HttpGet("scans")]
        public async Task<IActionResult> GetScans()
        {
            if (_cachedTasks != null && DateTime.UtcNow < _tasksCacheExpiry)
                return Content(_cachedTasks, "application/json");

            var result = await RunGmpQueryAsync("<get_tasks/>");
            if (result == null)
                return StatusCode(502, new { error = "Failed to query OpenVAS" });

            var tasks = ParseTasks(result);
            var json = JsonSerializer.Serialize(tasks);
            _cachedTasks = json;
            _tasksCacheExpiry = DateTime.UtcNow.AddMinutes(5);
            return Content(json, "application/json");
        }

        [HttpGet("reports/latest")]
        public async Task<IActionResult> GetLatestReport([FromQuery] int rows = 100)
        {
            if (_cachedLatestReport != null && DateTime.UtcNow < _reportCacheExpiry)
                return Content(_cachedLatestReport, "application/json");

            var tasksXml = await RunGmpQueryAsync("<get_reports details=\"0\"/>");
            if (tasksXml == null)
                return StatusCode(502, new { error = "Failed to query OpenVAS" });

            var latestId = ParseLatestReportId(tasksXml);
            if (string.IsNullOrEmpty(latestId))
                return Ok(new { results = Array.Empty<object>(), message = "No reports found" });

            var reportXml = await RunGmpQueryAsync(
                $"<get_reports report_id=\"{latestId}\" details=\"1\" filter=\"rows={rows} sort-reverse=severity\"/>");
            if (reportXml == null)
                return StatusCode(502, new { error = "Failed to fetch report" });

            var report = ParseReport(reportXml, latestId);
            var json = JsonSerializer.Serialize(report);
            _cachedLatestReport = json;
            _reportCacheExpiry = DateTime.UtcNow.AddMinutes(10);
            return Content(json, "application/json");
        }

        [HttpGet("reports/{reportId}")]
        public async Task<IActionResult> GetReport(string reportId, [FromQuery] int rows = 200)
        {
            if (!IsValidGuid(reportId))
                return BadRequest("Invalid report ID");

            var reportXml = await RunGmpQueryAsync(
                $"<get_reports report_id=\"{reportId}\" details=\"1\" filter=\"rows={rows} sort-reverse=severity\"/>");
            if (reportXml == null)
                return StatusCode(502, new { error = "Failed to fetch report" });

            var report = ParseReport(reportXml, reportId);
            return Ok(report);
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var tasksXml = await RunGmpQueryAsync("<get_reports details=\"0\"/>");
            if (tasksXml == null)
                return StatusCode(502, new { error = "Failed to query OpenVAS" });

            var latestId = ParseLatestReportId(tasksXml);
            if (string.IsNullOrEmpty(latestId))
                return Ok(new VulnSummary());

            var reportXml = await RunGmpQueryAsync(
                $"<get_reports report_id=\"{latestId}\" details=\"1\" filter=\"rows=500\"/>");
            if (reportXml == null)
                return StatusCode(502, new { error = "Failed to fetch report" });

            var report = ParseReport(reportXml, latestId);
            var summary = new VulnSummary
            {
                TotalFindings = report.TotalResults,
                Critical = report.Results.Count(r => r.Severity >= 9.0),
                High = report.Results.Count(r => r.Severity >= 7.0 && r.Severity < 9.0),
                Medium = report.Results.Count(r => r.Severity >= 4.0 && r.Severity < 7.0),
                Low = report.Results.Count(r => r.Severity >= 0.1 && r.Severity < 4.0),
                Info = report.Results.Count(r => r.Severity < 0.1),
                HostsScanned = report.Results.Select(r => r.Host).Distinct().Count(),
                ScanDate = report.ScanDate,
                TopVulnerabilities = report.Results
                    .Where(r => r.Severity >= 4.0)
                    .OrderByDescending(r => r.Severity)
                    .Take(10)
                    .ToList(),
                HostBreakdown = report.Results
                    .GroupBy(r => r.Host)
                    .Select(g => new HostVulnCount
                    {
                        Host = g.Key,
                        High = g.Count(r => r.Severity >= 7.0),
                        Medium = g.Count(r => r.Severity >= 4.0 && r.Severity < 7.0),
                        Low = g.Count(r => r.Severity >= 0.1 && r.Severity < 4.0),
                        Info = g.Count(r => r.Severity < 0.1)
                    })
                    .OrderByDescending(h => h.High)
                    .ToList()
            };
            return Ok(summary);
        }

        private async Task<string?> RunGmpQueryAsync(string xmlCommand)
        {
            if (!await _gmpLock.WaitAsync(TimeSpan.FromSeconds(30)))
                return null;

            try
            {
                var container = _config["OpenVAS:Container"] ?? "openvas";
                var user = _config["OpenVAS:User"] ?? "admin";
                var pass = _config["OpenVAS:Password"] ?? "PCplus2026!";

                var psi = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"exec -u gvm {container} gvm-cli --gmp-username {user} --gmp-password \"{pass}\" tls --hostname 127.0.0.1 --port 9390 --xml \"{xmlCommand.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                return proc.ExitCode == 0 ? output : null;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GMP query failed");
                return null;
            }
            finally
            {
                _gmpLock.Release();
            }
        }

        private static List<ScanTask> ParseTasks(string xml)
        {
            var tasks = new List<ScanTask>();
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);
                foreach (System.Xml.XmlNode node in doc.SelectNodes("//task")!)
                {
                    tasks.Add(new ScanTask
                    {
                        Id = node.Attributes?["id"]?.Value ?? "",
                        Name = node.SelectSingleNode("name")?.InnerText ?? "",
                        Status = node.SelectSingleNode("status")?.InnerText ?? "",
                        Target = node.SelectSingleNode("target/name")?.InnerText ?? "",
                        ReportCount = int.TryParse(node.SelectSingleNode("report_count")?.InnerText, out var rc) ? rc : 0,
                        LastReportDate = node.SelectSingleNode("last_report/report/timestamp")?.InnerText ?? ""
                    });
                }
            }
            catch { }
            return tasks;
        }

        private static string? ParseLatestReportId(string xml)
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);
                var reports = doc.SelectNodes("//report[@id]");
                string? latestId = null;
                DateTime latestTs = DateTime.MinValue;
                if (reports == null) return null;
                foreach (System.Xml.XmlNode r in reports)
                {
                    var ts = r.SelectSingleNode("timestamp")?.InnerText;
                    if (!string.IsNullOrEmpty(ts) && DateTime.TryParse(ts, out var dt) && dt > latestTs)
                    {
                        latestTs = dt;
                        latestId = r.Attributes?["id"]?.Value;
                    }
                }
                return latestId;
            }
            catch { return null; }
        }

        private static VulnReport ParseReport(string xml, string reportId)
        {
            var report = new VulnReport { ReportId = reportId };
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);
                var results = doc.SelectNodes("//results/result");
                if (results == null) return report;

                report.TotalResults = results.Count;
                var scanDate = doc.SelectSingleNode("//report/timestamp")?.InnerText;
                if (!string.IsNullOrEmpty(scanDate))
                    report.ScanDate = scanDate;

                foreach (System.Xml.XmlNode r in results)
                {
                    var sev = 0.0;
                    if (double.TryParse(r.SelectSingleNode("severity")?.InnerText, out var s))
                        sev = s;

                    report.Results.Add(new VulnResult
                    {
                        Name = r.SelectSingleNode("name")?.InnerText ?? "",
                        Host = r.SelectSingleNode("host")?.InnerText ?? "",
                        Port = r.SelectSingleNode("port")?.InnerText ?? "",
                        Severity = sev,
                        Threat = r.SelectSingleNode("threat")?.InnerText ?? "",
                        Description = (r.SelectSingleNode("description")?.InnerText ?? "").Trim(),
                        Nvt = r.SelectSingleNode("nvt/name")?.InnerText ?? "",
                        Cve = r.SelectSingleNode("nvt/refs/ref[@type='cve']/@id")?.Value ?? ""
                    });
                }
                report.Results = report.Results.OrderByDescending(r => r.Severity).ToList();
            }
            catch { }
            return report;
        }

        private static bool IsValidGuid(string s) =>
            Guid.TryParse(s, out _);
    }

    public class ScanTask
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string Target { get; set; } = "";
        public int ReportCount { get; set; }
        public string LastReportDate { get; set; } = "";
    }

    public class VulnReport
    {
        public string ReportId { get; set; } = "";
        public string ScanDate { get; set; } = "";
        public int TotalResults { get; set; }
        public List<VulnResult> Results { get; set; } = new();
    }

    public class VulnResult
    {
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public string Port { get; set; } = "";
        public double Severity { get; set; }
        public string Threat { get; set; } = "";
        public string Description { get; set; } = "";
        public string Nvt { get; set; } = "";
        public string Cve { get; set; } = "";
    }

    public class VulnSummary
    {
        public int TotalFindings { get; set; }
        public int Critical { get; set; }
        public int High { get; set; }
        public int Medium { get; set; }
        public int Low { get; set; }
        public int Info { get; set; }
        public int HostsScanned { get; set; }
        public string ScanDate { get; set; } = "";
        public List<VulnResult> TopVulnerabilities { get; set; } = new();
        public List<HostVulnCount> HostBreakdown { get; set; } = new();
    }

    public class HostVulnCount
    {
        public string Host { get; set; } = "";
        public int High { get; set; }
        public int Medium { get; set; }
        public int Low { get; set; }
        public int Info { get; set; }
    }
}
