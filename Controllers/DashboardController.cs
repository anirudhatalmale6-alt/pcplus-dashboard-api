using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;
using System.Text.Json;

namespace PCPlus.Dashboard.Controllers
{
    /// <summary>
    /// API endpoints for the web dashboard UI.
    /// Provides device management, alert feed, config push, policy management.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly DashboardDb _db;
        private readonly ILogger<DashboardController> _log;

        public DashboardController(DashboardDb db, ILogger<DashboardController> log)
        {
            _db = db;
            _log = log;
        }

        /// <summary>GET /api/dashboard/overview - Dashboard summary stats.</summary>
        [HttpGet("overview")]
        public async Task<ActionResult<DashboardOverview>> GetOverview()
        {
            // Mark devices offline if not seen in 2 minutes
            var offlineCutoff = DateTime.UtcNow.AddMinutes(-2);
            var staleDevices = await _db.Devices
                .Where(d => d.IsOnline && d.LastSeen < offlineCutoff)
                .ToListAsync();
            foreach (var d in staleDevices) d.IsOnline = false;
            if (staleDevices.Count > 0) await _db.SaveChangesAsync();

            var devices = await _db.Devices.ToListAsync();
            var alerts = await _db.Alerts.Where(a => !a.Acknowledged).ToListAsync();

            return Ok(new DashboardOverview
            {
                TotalDevices = devices.Count,
                OnlineDevices = devices.Count(d => d.IsOnline),
                OfflineDevices = devices.Count(d => !d.IsOnline),
                ActiveAlerts = alerts.Count,
                CriticalAlerts = alerts.Count(a => a.Severity is "Critical" or "Emergency"),
                DevicesInLockdown = devices.Count(d => d.LockdownActive),
                OpenIncidents = await _db.Incidents.CountAsync(i => !i.Resolved),
                AvgSecurityScore = devices.Count > 0 ? (float)devices.Average(d => d.SecurityScore) : 0,
                DevicesByTier = devices.GroupBy(d => d.LicenseTier)
                    .ToDictionary(g => g.Key, g => g.Count())
            });
        }

        /// <summary>GET /api/dashboard/devices - List all devices.</summary>
        [HttpGet("devices")]
        public async Task<ActionResult<List<Device>>> GetDevices(
            [FromQuery] string? customerId = null,
            [FromQuery] bool? online = null,
            [FromQuery] string? search = null)
        {
            var query = _db.Devices.AsQueryable();

            if (!string.IsNullOrEmpty(customerId))
                query = query.Where(d => d.CustomerId == customerId);
            if (online.HasValue)
                query = query.Where(d => d.IsOnline == online.Value);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(d => d.Hostname.Contains(search) || d.CustomerName.Contains(search));

            return Ok(await query.OrderByDescending(d => d.LastSeen).ToListAsync());
        }

        /// <summary>GET /api/dashboard/devices/{id} - Device detail.</summary>
        [HttpGet("devices/{deviceId}")]
        public async Task<ActionResult<Device>> GetDevice(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();
            return Ok(device);
        }

        /// <summary>PUT /api/dashboard/devices/{id}/customer - Reassign device to different customer.</summary>
        [HttpPut("devices/{deviceId}/customer")]
        public async Task<ActionResult> SetDeviceCustomer(string deviceId, [FromBody] SetCustomerRequest req)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            var oldCustomer = device.CustomerName;
            device.CustomerName = req.CustomerName;
            if (!string.IsNullOrEmpty(req.CustomerId))
                device.CustomerId = req.CustomerId;

            // Also push companyName config so the agent picks it up
            _db.ConfigPushes.Add(new ConfigPush
            {
                DeviceId = deviceId,
                Key = "companyName",
                Value = req.CustomerName,
                CreatedBy = "dashboard"
            });

            await _db.SaveChangesAsync();
            _log.LogInformation("Device {DeviceId} reassigned from '{Old}' to '{New}'",
                deviceId, oldCustomer, req.CustomerName);
            return Ok();
        }

        /// <summary>PUT /api/dashboard/devices/{id}/policy - Assign policy profile.</summary>
        [HttpPut("devices/{deviceId}/policy")]
        public async Task<ActionResult> SetDevicePolicy(string deviceId, [FromBody] SetPolicyRequest req)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            var profile = await _db.PolicyProfiles.FindAsync(req.ProfileName);
            if (profile == null) return BadRequest($"Profile '{req.ProfileName}' not found");

            device.PolicyProfile = req.ProfileName;

            // Push profile config to the device
            var configDict = JsonSerializer.Deserialize<Dictionary<string, string>>(profile.ConfigJson) ?? new();
            foreach (var (key, value) in configDict)
            {
                _db.ConfigPushes.Add(new ConfigPush
                {
                    DeviceId = deviceId,
                    Key = key,
                    Value = value,
                    CreatedBy = "dashboard"
                });
            }

            await _db.SaveChangesAsync();
            _log.LogInformation("Policy '{Profile}' applied to device {DeviceId}", req.ProfileName, deviceId);
            return Ok();
        }

        /// <summary>GET /api/dashboard/alerts - Alert feed.</summary>
        [HttpGet("alerts")]
        public async Task<ActionResult<List<DashboardAlert>>> GetAlerts(
            [FromQuery] string? deviceId = null,
            [FromQuery] string? severity = null,
            [FromQuery] bool? acknowledged = null,
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0)
        {
            var query = _db.Alerts.AsQueryable();

            if (!string.IsNullOrEmpty(deviceId))
                query = query.Where(a => a.DeviceId == deviceId);
            if (!string.IsNullOrEmpty(severity))
                query = query.Where(a => a.Severity == severity);
            if (acknowledged.HasValue)
                query = query.Where(a => a.Acknowledged == acknowledged.Value);

            return Ok(await query
                .OrderByDescending(a => a.Timestamp)
                .Skip(offset)
                .Take(Math.Min(limit, 200))
                .ToListAsync());
        }

        /// <summary>POST /api/dashboard/alerts/{id}/ack - Acknowledge an alert.</summary>
        [HttpPost("alerts/{id}/ack")]
        public async Task<ActionResult> AcknowledgeAlert(int id)
        {
            var alert = await _db.Alerts.FindAsync(id);
            if (alert == null) return NotFound();
            alert.Acknowledged = true;
            alert.AcknowledgedBy = "dashboard";
            await _db.SaveChangesAsync();
            return Ok();
        }

        /// <summary>POST /api/dashboard/config/push - Push config to device(s).</summary>
        [HttpPost("config/push")]
        public async Task<ActionResult> PushConfig([FromBody] PushConfigRequest req)
        {
            foreach (var (key, value) in req.Config)
            {
                _db.ConfigPushes.Add(new ConfigPush
                {
                    DeviceId = req.DeviceId ?? "",
                    PolicyProfile = req.PolicyProfile ?? "",
                    Key = key,
                    Value = value,
                    CreatedBy = "dashboard"
                });
            }

            await _db.SaveChangesAsync();
            _log.LogInformation("Config pushed: {Count} changes to device={Device} profile={Profile}",
                req.Config.Count, req.DeviceId ?? "all", req.PolicyProfile ?? "none");
            return Ok();
        }

        /// <summary>POST /api/dashboard/devices/{id}/remediate - Fix a failed security check.</summary>
        [HttpPost("devices/{deviceId}/remediate")]
        public async Task<ActionResult> RemediateCheck(string deviceId, [FromBody] RemediateRequest req)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            // Queue remediation command for the agent to pick up
            _db.ConfigPushes.Add(new ConfigPush
            {
                DeviceId = deviceId,
                Key = "_remediate",
                Value = req.CheckId,
                CreatedBy = "dashboard"
            });

            // Also queue a rescan command so results update quickly
            _db.ConfigPushes.Add(new ConfigPush
            {
                DeviceId = deviceId,
                Key = "_command",
                Value = "rescan",
                CreatedBy = "dashboard"
            });

            await _db.SaveChangesAsync();
            _log.LogInformation("Remediation '{CheckId}' queued for device {DeviceId}", req.CheckId, deviceId);
            return Ok(new { queued = true, checkId = req.CheckId, message = "Fix will be applied on next heartbeat (within 30 seconds)" });
        }

        /// <summary>POST /api/dashboard/devices/{id}/command - Send command to device.</summary>
        [HttpPost("devices/{deviceId}/command")]
        public async Task<ActionResult> SendCommand(string deviceId, [FromBody] DeviceCommandRequest req)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            // Store as a special config push that the endpoint picks up
            _db.ConfigPushes.Add(new ConfigPush
            {
                DeviceId = deviceId,
                Key = "_command",
                Value = req.Command,
                CreatedBy = "dashboard"
            });

            await _db.SaveChangesAsync();
            _log.LogInformation("Command '{Command}' sent to device {DeviceId}", req.Command, deviceId);
            return Ok();
        }

        /// <summary>POST /api/dashboard/company/{customerName}/command - Send command to ALL devices under a customer.</summary>
        [HttpPost("company/{customerName}/command")]
        public async Task<ActionResult> SendCompanyCommand(string customerName, [FromBody] DeviceCommandRequest req)
        {
            customerName = System.Net.WebUtility.UrlDecode(customerName);
            var devices = await _db.Devices.Where(d => d.CustomerName == customerName).ToListAsync();
            if (devices.Count == 0) return NotFound(new { message = $"No devices found for '{customerName}'" });

            foreach (var device in devices)
            {
                _db.ConfigPushes.Add(new ConfigPush
                {
                    DeviceId = device.DeviceId,
                    Key = "_command",
                    Value = req.Command,
                    CreatedBy = "dashboard"
                });
            }

            await _db.SaveChangesAsync();
            _log.LogInformation("Command '{Command}' sent to {Count} devices for customer '{Customer}'", req.Command, devices.Count, customerName);
            return Ok(new { queued = true, deviceCount = devices.Count, command = req.Command });
        }

        /// <summary>POST /api/dashboard/company/{customerName}/remediate - Fix a security check on ALL devices under a customer.</summary>
        [HttpPost("company/{customerName}/remediate")]
        public async Task<ActionResult> RemediateCompany(string customerName, [FromBody] RemediateRequest req)
        {
            customerName = System.Net.WebUtility.UrlDecode(customerName);
            var devices = await _db.Devices.Where(d => d.CustomerName == customerName).ToListAsync();
            if (devices.Count == 0) return NotFound(new { message = $"No devices found for '{customerName}'" });

            foreach (var device in devices)
            {
                _db.ConfigPushes.Add(new ConfigPush
                {
                    DeviceId = device.DeviceId,
                    Key = "_remediate",
                    Value = req.CheckId,
                    CreatedBy = "dashboard"
                });
                _db.ConfigPushes.Add(new ConfigPush
                {
                    DeviceId = device.DeviceId,
                    Key = "_command",
                    Value = "rescan",
                    CreatedBy = "dashboard"
                });
            }

            await _db.SaveChangesAsync();
            _log.LogInformation("Remediation '{CheckId}' queued for {Count} devices under '{Customer}'", req.CheckId, devices.Count, customerName);
            return Ok(new { queued = true, checkId = req.CheckId, deviceCount = devices.Count });
        }

        /// <summary>GET /api/dashboard/policies - List all policy profiles.</summary>
        [HttpGet("policies")]
        public async Task<ActionResult<List<PolicyProfile>>> GetPolicies()
        {
            return Ok(await _db.PolicyProfiles.ToListAsync());
        }

        /// <summary>POST /api/dashboard/policies - Create/update policy profile.</summary>
        [HttpPost("policies")]
        public async Task<ActionResult> SavePolicy([FromBody] PolicyProfile profile)
        {
            var existing = await _db.PolicyProfiles.FindAsync(profile.Name);
            if (existing != null)
            {
                existing.Description = profile.Description;
                existing.ConfigJson = profile.ConfigJson;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = "dashboard";
            }
            else
            {
                profile.UpdatedAt = DateTime.UtcNow;
                profile.UpdatedBy = "dashboard";
                _db.PolicyProfiles.Add(profile);
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        /// <summary>GET /api/dashboard/incidents - Incident history.</summary>
        [HttpGet("incidents")]
        public async Task<ActionResult<List<Incident>>> GetIncidents(
            [FromQuery] string? deviceId = null,
            [FromQuery] bool? resolved = null,
            [FromQuery] int limit = 50)
        {
            var query = _db.Incidents.AsQueryable();

            if (!string.IsNullOrEmpty(deviceId))
                query = query.Where(i => i.DeviceId == deviceId);
            if (resolved.HasValue)
                query = query.Where(i => i.Resolved == resolved.Value);

            return Ok(await query
                .OrderByDescending(i => i.OccurredAt)
                .Take(Math.Min(limit, 200))
                .ToListAsync());
        }

        /// <summary>GET /api/dashboard/devices/{id}/bitlocker - Get BitLocker recovery keys.</summary>
        [HttpGet("devices/{deviceId}/bitlocker")]
        public async Task<ActionResult> GetBitLockerKeys(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();
            try
            {
                var keys = JsonSerializer.Deserialize<List<BitLockerKeyReport>>(device.BitLockerKeysJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                return Ok(keys);
            }
            catch
            {
                return Ok(new List<BitLockerKeyReport>());
            }
        }

        /// <summary>POST /api/dashboard/incidents/{id}/resolve - Resolve an incident.</summary>
        [HttpPost("incidents/{id}/resolve")]
        public async Task<ActionResult> ResolveIncident(int id, [FromBody] ResolveRequest req)
        {
            var incident = await _db.Incidents.FindAsync(id);
            if (incident == null) return NotFound();
            incident.Resolved = true;
            incident.ResolvedBy = req.ResolvedBy;
            incident.ResolvedAt = DateTime.UtcNow;
            incident.ActionsTaken = req.ActionsTaken;
            await _db.SaveChangesAsync();
            return Ok();
        }
    }

    // Request models
    public class SetCustomerRequest
    {
        public string CustomerName { get; set; } = "";
        public string? CustomerId { get; set; }
    }
    public class SetPolicyRequest { public string ProfileName { get; set; } = ""; }
    public class PushConfigRequest
    {
        public string? DeviceId { get; set; }
        public string? PolicyProfile { get; set; }
        public Dictionary<string, string> Config { get; set; } = new();
    }
    public class RemediateRequest { public string CheckId { get; set; } = ""; }
    public class DeviceCommandRequest { public string Command { get; set; } = ""; }
    public class ResolveRequest
    {
        public string ResolvedBy { get; set; } = "";
        public string ActionsTaken { get; set; } = "";
    }
}
