using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;

namespace PCPlus.Dashboard.Controllers
{
    /// <summary>
    /// API endpoints for agents to POST rich security telemetry data.
    /// AllowAnonymous - agents authenticate via device ID (same pattern as EndpointController).
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/endpoint")]
    public class AgentDataController : ControllerBase
    {
        private readonly DashboardDb _db;
        private readonly ILogger<AgentDataController> _log;

        public AgentDataController(DashboardDb db, ILogger<AgentDataController> log)
        {
            _db = db;
            _log = log;
        }

        /// <summary>
        /// POST /api/endpoint/security-log
        /// Agent reports a security event.
        /// </summary>
        [HttpPost("security-log")]
        public async Task<ActionResult> ReportSecurityLog([FromBody] SecurityLogReport report)
        {
            if (string.IsNullOrEmpty(report.DeviceId))
                return BadRequest("DeviceId required");

            var log = new SecurityLog
            {
                DeviceId = report.DeviceId,
                Hostname = report.Hostname,
                EventType = report.EventType,
                Source = report.Source,
                Severity = report.Severity,
                Category = report.Category,
                Message = report.Message,
                DetailsJson = report.DetailsJson,
                Timestamp = DateTime.UtcNow
            };

            _db.SecurityLogs.Add(log);
            await _db.SaveChangesAsync();

            _log.LogInformation("[SecurityLog] {Severity} from {DeviceId}: {EventType} - {Message}",
                report.Severity, report.DeviceId, report.EventType, report.Message);

            return Ok(new { id = log.Id });
        }

        /// <summary>
        /// POST /api/endpoint/backup-status
        /// Agent reports backup status. Upserts by DeviceId + Provider.
        /// </summary>
        [HttpPost("backup-status")]
        public async Task<ActionResult> ReportBackupStatus([FromBody] BackupStatusReport report)
        {
            if (string.IsNullOrEmpty(report.DeviceId))
                return BadRequest("DeviceId required");

            // Upsert: one record per device per provider
            var existing = await _db.BackupStatuses
                .FirstOrDefaultAsync(b => b.DeviceId == report.DeviceId && b.Provider == report.Provider);

            if (existing == null)
            {
                existing = new BackupStatus
                {
                    DeviceId = report.DeviceId,
                    Hostname = report.Hostname,
                    Provider = report.Provider
                };
                _db.BackupStatuses.Add(existing);
            }

            existing.Hostname = report.Hostname;
            existing.LastBackupTime = report.LastBackupTime;
            existing.NextBackupTime = report.NextBackupTime;
            existing.Status = report.Status;
            existing.SizeBytes = report.SizeBytes;
            existing.ProtectedPathsJson = JsonSerializer.Serialize(report.ProtectedPaths);
            existing.ShadowCopyEnabled = report.ShadowCopyEnabled;
            existing.ShadowCopyCount = report.ShadowCopyCount;
            existing.RecoveryPointCount = report.RecoveryPointCount;
            existing.LastTestedAt = report.LastTestedAt;
            existing.LastUpdated = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.LogInformation("[Backup] {Provider} status for {DeviceId}: {Status}",
                report.Provider, report.DeviceId, report.Status);

            return Ok(new { id = existing.Id });
        }

        /// <summary>
        /// POST /api/endpoint/network-security
        /// Agent reports network security state. Upserts by DeviceId.
        /// </summary>
        [HttpPost("network-security")]
        public async Task<ActionResult> ReportNetworkSecurity([FromBody] NetworkSecurityReport report)
        {
            if (string.IsNullOrEmpty(report.DeviceId))
                return BadRequest("DeviceId required");

            // Upsert: one record per device
            var existing = await _db.NetworkSecurityData
                .FirstOrDefaultAsync(n => n.DeviceId == report.DeviceId);

            if (existing == null)
            {
                existing = new NetworkSecurityData
                {
                    DeviceId = report.DeviceId,
                    Hostname = report.Hostname
                };
                _db.NetworkSecurityData.Add(existing);
            }

            existing.Hostname = report.Hostname;
            existing.FirewallEnabled = report.FirewallEnabled;
            existing.FirewallProfilesJson = JsonSerializer.Serialize(report.FirewallProfiles);
            existing.OpenPortsJson = JsonSerializer.Serialize(report.OpenPorts);
            existing.ActiveConnections = report.ActiveConnections;
            existing.RdpEnabled = report.RdpEnabled;
            existing.DnsServersJson = JsonSerializer.Serialize(report.DnsServers);
            existing.WifiSecurityType = report.WifiSecurityType;
            existing.LastUpdated = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.LogInformation("[Network] Security state for {DeviceId}: Firewall={Enabled}, RDP={Rdp}, Ports={Ports}",
                report.DeviceId, report.FirewallEnabled, report.RdpEnabled, report.OpenPorts?.Count ?? 0);

            return Ok(new { id = existing.Id });
        }

        /// <summary>
        /// POST /api/endpoint/ransomware-status
        /// Agent reports ransomware shield state. Upserts by DeviceId.
        /// </summary>
        [HttpPost("ransomware-status")]
        public async Task<ActionResult> ReportRansomwareStatus([FromBody] RansomwareStatusReport report)
        {
            if (string.IsNullOrEmpty(report.DeviceId))
                return BadRequest("DeviceId required");

            // Upsert: one record per device
            var existing = await _db.RansomwareStatuses
                .FirstOrDefaultAsync(r => r.DeviceId == report.DeviceId);

            if (existing == null)
            {
                existing = new RansomwareStatus
                {
                    DeviceId = report.DeviceId,
                    Hostname = report.Hostname
                };
                _db.RansomwareStatuses.Add(existing);
            }

            existing.Hostname = report.Hostname;
            existing.BehaviorMonitoringEnabled = report.BehaviorMonitoringEnabled;
            existing.ProtectedFoldersJson = JsonSerializer.Serialize(report.ProtectedFolders);
            existing.ShadowCopyProtected = report.ShadowCopyProtected;
            existing.HoneypotActive = report.HoneypotActive;
            existing.RollbackCapable = report.RollbackCapable;
            existing.DetectionRulesJson = JsonSerializer.Serialize(report.DetectionRules);
            existing.ThreatHistoryJson = JsonSerializer.Serialize(report.ThreatHistory);
            existing.LastUpdated = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.LogInformation("[Ransomware] Status for {DeviceId}: BehaviorMon={BM}, Honeypot={HP}, Rollback={RB}",
                report.DeviceId, report.BehaviorMonitoringEnabled, report.HoneypotActive, report.RollbackCapable);

            return Ok(new { id = existing.Id });
        }

        /// <summary>
        /// POST /api/endpoint/antivirus
        /// Agent reports installed AV products. Replaces all products for device.
        /// </summary>
        [HttpPost("antivirus")]
        public async Task<ActionResult> ReportAntivirus([FromBody] AntivirusReport report)
        {
            if (string.IsNullOrEmpty(report.DeviceId))
                return BadRequest("DeviceId required");

            // Remove existing products for this device and replace with new set
            var existing = await _db.AntivirusProducts
                .Where(a => a.DeviceId == report.DeviceId)
                .ToListAsync();
            _db.AntivirusProducts.RemoveRange(existing);

            foreach (var product in report.Products)
            {
                _db.AntivirusProducts.Add(new AntivirusProduct
                {
                    DeviceId = report.DeviceId,
                    Hostname = report.Hostname,
                    Name = product.Name,
                    Vendor = product.Vendor,
                    Version = product.Version,
                    DefinitionDate = product.DefinitionDate,
                    Status = product.Status,
                    RealTimeEnabled = product.RealTimeEnabled,
                    LastScanTime = product.LastScanTime,
                    QuarantineCount = product.QuarantineCount,
                    LastUpdated = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            _log.LogInformation("[Antivirus] {Count} products reported for {DeviceId}",
                report.Products.Count, report.DeviceId);

            return Ok(new { count = report.Products.Count });
        }
    }
}
