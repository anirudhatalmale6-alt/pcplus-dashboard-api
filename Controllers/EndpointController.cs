using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;

namespace PCPlus.Dashboard.Controllers
{
    /// <summary>
    /// API endpoints called by the PCPlus Service on each endpoint.
    /// Handles heartbeats, alert reporting, and config polling.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/endpoint")]
    public class EndpointController : ControllerBase
    {
        private readonly DashboardDb _db;
        private readonly ILogger<EndpointController> _log;
        private readonly PCPlus.Dashboard.Services.NotificationService _notifications;

        public EndpointController(DashboardDb db, ILogger<EndpointController> log, PCPlus.Dashboard.Services.NotificationService notifications)
        {
            _db = db;
            _log = log;
            _notifications = notifications;
        }

        /// <summary>
        /// POST /api/endpoint/heartbeat
        /// Called by each endpoint every 30 seconds. Updates device status and returns pending config.
        /// </summary>
        [HttpPost("heartbeat")]
        public async Task<ActionResult<HeartbeatResponse>> Heartbeat(
            [FromBody] HeartbeatRequest request,
            [FromHeader(Name = "X-Api-Token")] string? apiToken)
        {
            if (string.IsNullOrEmpty(request.DeviceId))
                return BadRequest("DeviceId required");

            var device = await _db.Devices.FindAsync(request.DeviceId);
            if (device == null)
            {
                // Auto-register new device
                device = new Device
                {
                    DeviceId = request.DeviceId,
                    RegisteredAt = DateTime.UtcNow
                };
                _db.Devices.Add(device);
                _log.LogInformation("New device registered: {DeviceId} ({Hostname})",
                    request.DeviceId, request.Hostname);
            }

            // Update device state
            device.Hostname = request.Hostname;
            // Only update customer name from agent if dashboard hasn't set a meaningful one,
            // and the incoming name isn't an unresolved template variable
            if (!string.IsNullOrEmpty(request.CustomerName)
                && !request.CustomerName.Contains("{{")
                && (string.IsNullOrEmpty(device.CustomerName) || device.CustomerName == request.CustomerName))
                device.CustomerName = request.CustomerName;
            if (!string.IsNullOrEmpty(request.LocalIp))
                device.LocalIp = request.LocalIp;
            if (!string.IsNullOrEmpty(request.PublicIp))
                device.PublicIp = request.PublicIp;
            device.OsVersion = request.OsVersion;
            device.AgentVersion = request.AgentVersion;
            device.LicenseTier = request.LicenseTier;
            device.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            device.IsOnline = true;
            device.LastSeen = DateTime.UtcNow;
            device.CpuPercent = request.CpuPercent;
            device.RamPercent = request.RamPercent;
            device.DiskPercent = request.DiskPercent;
            if (request.CpuTempC > 0)
                device.CpuTempC = request.CpuTempC;
            if (request.GpuTempC > 0)
                device.GpuTempC = request.GpuTempC;
            // Only update security score if non-zero (PS heartbeat sends 0; don't overwrite real score)
            if (request.SecurityScore > 0)
            {
                device.SecurityScore = request.SecurityScore;
                device.SecurityGrade = request.SecurityGrade;
            }
            device.LockdownActive = request.LockdownActive;
            device.ActiveAlerts = request.ActiveAlerts;
            device.RunningModules = request.RunningModules;

            // Store security check details
            if (request.SecurityChecks?.Count > 0)
                device.SecurityChecksJson = JsonSerializer.Serialize(request.SecurityChecks);

            // Store software inventory
            if (request.InstalledSoftware?.Count > 0)
                device.InstalledSoftwareJson = JsonSerializer.Serialize(request.InstalledSoftware);

            // Store BitLocker recovery keys
            if (request.BitLockerRecoveryKeys?.Count > 0)
                device.BitLockerKeysJson = JsonSerializer.Serialize(request.BitLockerRecoveryKeys);

            // Policy engine data
            device.PolicyActiveRules = request.PolicyActiveRules;
            device.PolicyViolations24h = request.PolicyViolations24h;

            // WiFi security data
            device.WifiNetworks = request.WifiNetworks;
            device.WifiUnsecure = request.WifiUnsecure;
            if (!string.IsNullOrEmpty(request.WifiConnectedSsid))
                device.WifiConnectedSsid = request.WifiConnectedSsid;

            await _db.SaveChangesAsync();

            // Check for pending config pushes
            var pendingConfig = await _db.ConfigPushes
                .Where(c => !c.Applied &&
                    (c.DeviceId == request.DeviceId || c.DeviceId == "" || c.PolicyProfile == device.PolicyProfile))
                .OrderBy(c => c.CreatedAt)
                .Take(50)
                .Select(c => new ConfigChange { Id = c.Id, Key = c.Key, Value = c.Value })
                .ToListAsync();

            return Ok(new HeartbeatResponse
            {
                Ok = true,
                PendingConfig = pendingConfig,
                CustomerName = device.CustomerName
            });
        }

        /// <summary>
        /// POST /api/endpoint/heartbeat/ack
        /// Endpoint acknowledges that config changes have been applied.
        /// </summary>
        [HttpPost("heartbeat/ack")]
        public async Task<ActionResult> AcknowledgeConfig([FromBody] List<int> configIds)
        {
            var configs = await _db.ConfigPushes
                .Where(c => configIds.Contains(c.Id))
                .ToListAsync();

            foreach (var config in configs)
            {
                config.Applied = true;
                config.AppliedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// POST /api/endpoint/alert
        /// Endpoint reports an alert.
        /// </summary>
        [HttpPost("alert")]
        public async Task<ActionResult> ReportAlert([FromBody] AlertReport report)
        {
            if (string.IsNullOrEmpty(report.DeviceId))
                return BadRequest("DeviceId required");

            var alert = new DashboardAlert
            {
                DeviceId = report.DeviceId,
                Hostname = report.Hostname,
                ModuleId = report.ModuleId,
                Title = report.Title,
                Message = report.Message,
                Severity = report.Severity,
                Category = report.Category,
                Metadata = report.Metadata,
                Timestamp = DateTime.UtcNow
            };

            _db.Alerts.Add(alert);

            // Auto-create incident for critical/emergency alerts
            if (report.Severity is "Critical" or "Emergency")
            {
                _db.Incidents.Add(new Incident
                {
                    DeviceId = report.DeviceId,
                    Hostname = report.Hostname,
                    Type = report.Category,
                    Description = $"{report.Title}: {report.Message}",
                    Severity = report.Severity,
                    OccurredAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            _log.LogInformation("[{Severity}] Alert from {DeviceId}: {Title}",
                report.Severity, report.DeviceId, report.Title);

            // Fire webhook/Slack/Teams notifications
            _ = _notifications.SendNotificationAsync(
                report.Title, report.Message, report.Severity,
                report.DeviceId, report.Hostname);

            return Ok();
        }

        /// <summary>
        /// POST /api/endpoint/alert/ack
        /// Acknowledge an alert by ID.
        /// </summary>
        [HttpPost("alert/ack")]
        public async Task<ActionResult> AcknowledgeAlert([FromBody] AlertAckRequest ack)
        {
            var alert = await _db.Alerts.FindAsync(ack.AlertId);
            if (alert == null) return NotFound("Alert not found");

            alert.Acknowledged = true;
            alert.AcknowledgedBy = ack.AcknowledgedBy ?? "user";
            await _db.SaveChangesAsync();

            _log.LogInformation("Alert {AlertId} acknowledged by {By}", ack.AlertId, ack.AcknowledgedBy);
            return Ok(new { success = true });
        }

        /// <summary>
        /// POST /api/endpoint/register
        /// Explicit device registration with customer info.
        /// </summary>
        [HttpPost("register")]
        public async Task<ActionResult> Register([FromBody] DeviceRegistration reg)
        {
            var device = await _db.Devices.FindAsync(reg.DeviceId);
            if (device == null)
            {
                device = new Device { DeviceId = reg.DeviceId, RegisteredAt = DateTime.UtcNow };
                _db.Devices.Add(device);
            }

            device.Hostname = reg.Hostname;
            device.CustomerId = reg.CustomerId;
            device.CustomerName = reg.CustomerName;
            device.OsVersion = reg.OsVersion;
            device.AgentVersion = reg.AgentVersion;
            device.PolicyProfile = reg.PolicyProfile;
            device.LicenseKey = reg.LicenseKey;

            await _db.SaveChangesAsync();

            _log.LogInformation("Device registered: {DeviceId} for customer {CustomerName}",
                reg.DeviceId, reg.CustomerName);

            return Ok(new { registered = true, deviceId = device.DeviceId });
        }
    }

    public class DeviceRegistration
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string AgentVersion { get; set; } = "";
        public string PolicyProfile { get; set; } = "default";
        public string LicenseKey { get; set; } = "";
    }
}
