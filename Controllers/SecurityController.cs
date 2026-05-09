using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;

namespace PCPlus.Dashboard.Controllers
{
    /// <summary>
    /// API endpoints for advanced security dashboard pages.
    /// Provides scan results, access control, backup, network, ransomware, AV, and compliance data.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/dashboard")]
    public class SecurityController : ControllerBase
    {
        private readonly DashboardDb _db;
        private readonly ILogger<SecurityController> _log;

        public SecurityController(DashboardDb db, ILogger<SecurityController> log)
        {
            _db = db;
            _log = log;
        }

        /// <summary>
        /// GET /api/dashboard/devices/{deviceId}/scan-results
        /// Returns the 175-point security audit results grouped by category with compliance tags.
        /// </summary>
        [HttpGet("devices/{deviceId}/scan-results")]
        public async Task<ActionResult<ScanResultsResponse>> GetScanResults(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            List<SecurityCheckReport> checks;
            try
            {
                checks = JsonSerializer.Deserialize<List<SecurityCheckReport>>(device.SecurityChecksJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch
            {
                checks = new();
            }

            var categoryGroups = checks
                .GroupBy(c => c.Category)
                .OrderBy(g => g.Key)
                .Select(g => new ScanCategoryGroup
                {
                    Category = g.Key,
                    TotalChecks = g.Count(),
                    PassedChecks = g.Count(c => c.Passed),
                    CompliancePercent = g.Count() > 0 ? (float)g.Count(c => c.Passed) / g.Count() * 100f : 0f,
                    Checks = g.Select(c => new ScanCheckItem
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Category = c.Category,
                        Passed = c.Passed,
                        Detail = c.Detail,
                        Recommendation = c.Recommendation,
                        Weight = c.Weight,
                        LastChecked = c.LastChecked,
                        ComplianceFrameworks = GetFrameworksForCheck(c.Id)
                    }).OrderBy(c => c.Passed).ThenBy(c => c.Name).ToList()
                }).ToList();

            return Ok(new ScanResultsResponse
            {
                DeviceId = device.DeviceId,
                Hostname = device.Hostname,
                TotalChecks = checks.Count,
                PassedChecks = checks.Count(c => c.Passed),
                FailedChecks = checks.Count(c => !c.Passed),
                SecurityScore = device.SecurityScore,
                SecurityGrade = device.SecurityGrade,
                Categories = categoryGroups
            });
        }

        /// <summary>
        /// GET /api/dashboard/devices/{deviceId}/access-control
        /// Returns access control data: login activity, user accounts, MFA status.
        /// </summary>
        [HttpGet("devices/{deviceId}/access-control")]
        public async Task<ActionResult<AccessControlResponse>> GetAccessControl(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            // Get recent authentication-related security logs
            var recentLogins = await _db.SecurityLogs
                .AsNoTracking()
                .Where(l => l.DeviceId == deviceId && l.Category == "Authentication")
                .OrderByDescending(l => l.Timestamp)
                .Take(50)
                .Select(l => new LoginActivity
                {
                    EventType = l.EventType,
                    Source = l.Source,
                    Severity = l.Severity,
                    Message = l.Message,
                    Timestamp = l.Timestamp
                })
                .ToListAsync();

            // Parse user accounts and MFA from security checks
            List<SecurityCheckReport> checks;
            try
            {
                checks = JsonSerializer.Deserialize<List<SecurityCheckReport>>(device.SecurityChecksJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch
            {
                checks = new();
            }

            var userAccounts = new List<UserAccountInfo>();
            var mfaStatus = new MfaStatusInfo();

            // Extract user account info from security checks
            var accountChecks = checks.Where(c =>
                c.Category == "User Accounts" || c.Category == "Authentication" ||
                c.Id.StartsWith("acct_") || c.Id.StartsWith("auth_")).ToList();

            foreach (var check in accountChecks)
            {
                if (check.Id.StartsWith("acct_"))
                {
                    userAccounts.Add(new UserAccountInfo
                    {
                        Username = check.Name,
                        AccountType = check.Detail.Contains("Admin") ? "Admin" : "Standard",
                        IsEnabled = true,
                        PasswordExpires = check.Passed
                    });
                }
            }

            // Extract MFA status from checks
            var helloCheck = checks.FirstOrDefault(c => c.Id == "auth_windows_hello" || c.Name.Contains("Windows Hello"));
            var smartCardCheck = checks.FirstOrDefault(c => c.Id == "auth_smartcard" || c.Name.Contains("Smart Card"));
            var pinCheck = checks.FirstOrDefault(c => c.Id == "auth_pin" || c.Name.Contains("PIN"));

            mfaStatus.WindowsHelloEnabled = helloCheck?.Passed ?? false;
            mfaStatus.SmartCardRequired = smartCardCheck?.Passed ?? false;
            mfaStatus.PinConfigured = pinCheck?.Passed ?? false;
            mfaStatus.Detail = helloCheck?.Detail ?? smartCardCheck?.Detail ?? "No MFA data available";

            return Ok(new AccessControlResponse
            {
                DeviceId = device.DeviceId,
                Hostname = device.Hostname,
                RecentLogins = recentLogins,
                UserAccounts = userAccounts,
                MfaStatus = mfaStatus
            });
        }

        /// <summary>
        /// GET /api/dashboard/devices/{deviceId}/backup
        /// Returns backup status for the device.
        /// </summary>
        [HttpGet("devices/{deviceId}/backup")]
        public async Task<ActionResult<BackupResponse>> GetBackupStatus(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            var backups = await _db.BackupStatuses
                .AsNoTracking()
                .Where(b => b.DeviceId == deviceId)
                .OrderByDescending(b => b.LastUpdated)
                .ToListAsync();

            var providers = backups.Select(b =>
            {
                List<string> paths;
                try { paths = JsonSerializer.Deserialize<List<string>>(b.ProtectedPathsJson) ?? new(); }
                catch { paths = new(); }

                return new BackupProviderStatus
                {
                    Provider = b.Provider,
                    Status = b.Status,
                    LastBackupTime = b.LastBackupTime,
                    NextBackupTime = b.NextBackupTime,
                    SizeBytes = b.SizeBytes,
                    ProtectedPaths = paths,
                    RecoveryPointCount = b.RecoveryPointCount,
                    LastTestedAt = b.LastTestedAt
                };
            }).ToList();

            var latestWithShadow = backups.FirstOrDefault(b => b.ShadowCopyEnabled);

            return Ok(new BackupResponse
            {
                DeviceId = device.DeviceId,
                Hostname = device.Hostname,
                Providers = providers,
                ShadowCopyEnabled = latestWithShadow?.ShadowCopyEnabled ?? false,
                ShadowCopyCount = latestWithShadow?.ShadowCopyCount ?? 0,
                TotalRecoveryPoints = backups.Sum(b => b.RecoveryPointCount)
            });
        }

        /// <summary>
        /// GET /api/dashboard/devices/{deviceId}/network
        /// Returns network security data (firewall, ports, DNS).
        /// </summary>
        [HttpGet("devices/{deviceId}/network")]
        public async Task<ActionResult<NetworkSecurityResponse>> GetNetworkSecurity(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            var network = await _db.NetworkSecurityData
                .AsNoTracking()
                .Where(n => n.DeviceId == deviceId)
                .OrderByDescending(n => n.LastUpdated)
                .FirstOrDefaultAsync();

            if (network == null)
            {
                return Ok(new NetworkSecurityResponse
                {
                    DeviceId = device.DeviceId,
                    Hostname = device.Hostname,
                    FirewallEnabled = false,
                    FirewallProfiles = new(),
                    OpenPorts = new(),
                    ActiveConnections = 0,
                    RdpEnabled = false,
                    DnsServers = new(),
                    WifiSecurityType = "",
                    LastUpdated = null
                });
            }

            List<FirewallProfileInfo> profiles;
            try { profiles = JsonSerializer.Deserialize<List<FirewallProfileInfo>>(network.FirewallProfilesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
            catch { profiles = new(); }

            List<OpenPortInfo> ports;
            try { ports = JsonSerializer.Deserialize<List<OpenPortInfo>>(network.OpenPortsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
            catch { ports = new(); }

            List<string> dns;
            try { dns = JsonSerializer.Deserialize<List<string>>(network.DnsServersJson) ?? new(); }
            catch { dns = new(); }

            return Ok(new NetworkSecurityResponse
            {
                DeviceId = device.DeviceId,
                Hostname = device.Hostname,
                FirewallEnabled = network.FirewallEnabled,
                FirewallProfiles = profiles,
                OpenPorts = ports,
                ActiveConnections = network.ActiveConnections,
                RdpEnabled = network.RdpEnabled,
                DnsServers = dns,
                WifiSecurityType = network.WifiSecurityType,
                LastUpdated = network.LastUpdated
            });
        }

        /// <summary>
        /// GET /api/dashboard/devices/{deviceId}/ransomware
        /// Returns ransomware protection status.
        /// </summary>
        [HttpGet("devices/{deviceId}/ransomware")]
        public async Task<ActionResult<RansomwareResponse>> GetRansomwareStatus(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            var ransomware = await _db.RansomwareStatuses
                .AsNoTracking()
                .Where(r => r.DeviceId == deviceId)
                .OrderByDescending(r => r.LastUpdated)
                .FirstOrDefaultAsync();

            if (ransomware == null)
            {
                return Ok(new RansomwareResponse
                {
                    DeviceId = device.DeviceId,
                    Hostname = device.Hostname,
                    BehaviorMonitoringEnabled = false,
                    ProtectedFolders = new(),
                    ShadowCopyProtected = false,
                    HoneypotActive = false,
                    RollbackCapable = false,
                    DetectionRules = new(),
                    ThreatHistory = new(),
                    LastUpdated = null
                });
            }

            List<string> folders;
            try { folders = JsonSerializer.Deserialize<List<string>>(ransomware.ProtectedFoldersJson) ?? new(); }
            catch { folders = new(); }

            List<DetectionRuleInfo> rules;
            try { rules = JsonSerializer.Deserialize<List<DetectionRuleInfo>>(ransomware.DetectionRulesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
            catch { rules = new(); }

            List<ThreatHistoryItem> threats;
            try { threats = JsonSerializer.Deserialize<List<ThreatHistoryItem>>(ransomware.ThreatHistoryJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
            catch { threats = new(); }

            return Ok(new RansomwareResponse
            {
                DeviceId = device.DeviceId,
                Hostname = device.Hostname,
                BehaviorMonitoringEnabled = ransomware.BehaviorMonitoringEnabled,
                ProtectedFolders = folders,
                ShadowCopyProtected = ransomware.ShadowCopyProtected,
                HoneypotActive = ransomware.HoneypotActive,
                RollbackCapable = ransomware.RollbackCapable,
                DetectionRules = rules,
                ThreatHistory = threats,
                LastUpdated = ransomware.LastUpdated
            });
        }

        /// <summary>
        /// GET /api/dashboard/devices/{deviceId}/realtime-protection
        /// Returns AV products with ACTIVE/PASSIVE/ON-DEMAND status.
        /// </summary>
        [HttpGet("devices/{deviceId}/realtime-protection")]
        public async Task<ActionResult<RealtimeProtectionResponse>> GetRealtimeProtection(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            var products = await _db.AntivirusProducts
                .AsNoTracking()
                .Where(a => a.DeviceId == deviceId)
                .OrderByDescending(a => a.LastUpdated)
                .ToListAsync();

            var productInfos = products.Select(p => new AntivirusProductInfo
            {
                Name = p.Name,
                Vendor = p.Vendor,
                Version = p.Version,
                DefinitionDate = p.DefinitionDate,
                Status = p.Status,
                RealTimeEnabled = p.RealTimeEnabled,
                LastScanTime = p.LastScanTime,
                QuarantineCount = p.QuarantineCount,
                DefinitionsOutdated = p.DefinitionDate.HasValue && p.DefinitionDate.Value < DateTime.UtcNow.AddDays(-3)
            }).ToList();

            return Ok(new RealtimeProtectionResponse
            {
                DeviceId = device.DeviceId,
                Hostname = device.Hostname,
                AnyRealTimeActive = productInfos.Any(p => p.RealTimeEnabled && p.Status == "Active"),
                TotalProducts = productInfos.Count,
                Products = productInfos
            });
        }

        /// <summary>
        /// GET /api/dashboard/devices/{deviceId}/compliance
        /// Returns compliance scores per framework calculated from security checks.
        /// </summary>
        [HttpGet("devices/{deviceId}/compliance")]
        public async Task<ActionResult<ComplianceResponse>> GetCompliance(string deviceId)
        {
            var device = await _db.Devices.FindAsync(deviceId);
            if (device == null) return NotFound();

            List<SecurityCheckReport> checks;
            try
            {
                checks = JsonSerializer.Deserialize<List<SecurityCheckReport>>(device.SecurityChecksJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch
            {
                checks = new();
            }

            var frameworks = CalculateFrameworkScores(checks);

            return Ok(new ComplianceResponse
            {
                DeviceId = device.DeviceId,
                Hostname = device.Hostname,
                OverallScore = device.SecurityScore,
                OverallGrade = device.SecurityGrade,
                Frameworks = frameworks
            });
        }

        /// <summary>
        /// GET /api/dashboard/security-logs
        /// Consolidated security log with filters.
        /// </summary>
        [HttpGet("security-logs")]
        public async Task<ActionResult<SecurityLogResponse>> GetSecurityLogs(
            [FromQuery] string? deviceId = null,
            [FromQuery] string? severity = null,
            [FromQuery] string? category = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0)
        {
            var query = _db.SecurityLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(deviceId))
                query = query.Where(l => l.DeviceId == deviceId);
            if (!string.IsNullOrEmpty(severity))
                query = query.Where(l => l.Severity == severity);
            if (!string.IsNullOrEmpty(category))
                query = query.Where(l => l.Category == category);
            if (from.HasValue)
                query = query.Where(l => l.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(l => l.Timestamp <= to.Value);

            var totalCount = await query.CountAsync();

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip(offset)
                .Take(Math.Min(limit, 200))
                .ToListAsync();

            return Ok(new SecurityLogResponse
            {
                TotalCount = totalCount,
                Offset = offset,
                Limit = limit,
                Logs = logs
            });
        }

        // --- Framework compliance mapping ---

        /// <summary>Maps security check IDs to compliance framework requirements.</summary>
        private static readonly Dictionary<string, List<string>> FrameworkMapping = new()
        {
            // CyberSecure Canada - 13 baseline controls
            ["firewall_enabled"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["firewall_domain"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["firewall_private"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["firewall_public"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["antivirus_active"] = new() { "CyberSecure", "NIST_CSF", "CIS", "Insurance" },
            ["antivirus_updated"] = new() { "CyberSecure", "NIST_CSF", "CIS", "Insurance" },
            ["realtime_protection"] = new() { "CyberSecure", "NIST_CSF", "CIS", "Insurance" },
            ["windows_update_enabled"] = new() { "CyberSecure", "NIST_CSF", "CIS", "Insurance" },
            ["windows_update_recent"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["bitlocker_enabled"] = new() { "CyberSecure", "NIST_CSF", "CIS", "PIPEDA", "Insurance" },
            ["password_complexity"] = new() { "CyberSecure", "NIST_CSF", "CIS", "PIPEDA" },
            ["password_expiry"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["lockout_policy"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["guest_account_disabled"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["admin_accounts_limited"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["uac_enabled"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["screensaver_lock"] = new() { "CyberSecure", "CIS" },
            ["auto_login_disabled"] = new() { "CyberSecure", "CIS" },
            ["rdp_disabled"] = new() { "CyberSecure", "NIST_CSF", "CIS", "Insurance" },
            ["rdp_nla_enabled"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["smb1_disabled"] = new() { "CyberSecure", "NIST_CSF", "CIS" },
            ["backup_configured"] = new() { "CyberSecure", "NIST_CSF", "Insurance" },
            ["backup_recent"] = new() { "CyberSecure", "NIST_CSF", "Insurance" },
            ["secure_boot"] = new() { "NIST_CSF", "CIS" },
            ["tpm_enabled"] = new() { "NIST_CSF", "CIS" },
            ["audit_policy_enabled"] = new() { "NIST_CSF", "CIS", "PIPEDA" },
            ["event_log_retention"] = new() { "NIST_CSF", "CIS", "PIPEDA" },
            ["powershell_logging"] = new() { "NIST_CSF", "CIS" },
            ["dns_secure"] = new() { "CIS", "Insurance" },
            ["wifi_secure"] = new() { "CyberSecure", "CIS" },
            ["usb_restricted"] = new() { "CIS", "PIPEDA" },
            ["autorun_disabled"] = new() { "CIS" },
            ["remote_assistance_disabled"] = new() { "CIS" },
            ["network_discovery_disabled"] = new() { "CIS" },
            ["telnet_disabled"] = new() { "CIS" },
            ["netbios_disabled"] = new() { "CIS" },
            ["llmnr_disabled"] = new() { "CIS" },
            ["print_spooler_disabled"] = new() { "CIS" },
            ["wdigest_disabled"] = new() { "CIS" },
            ["lsa_protection"] = new() { "NIST_CSF", "CIS" },
            ["credential_guard"] = new() { "NIST_CSF", "CIS" },
            ["spectre_meltdown_mitigated"] = new() { "NIST_CSF", "CIS" },
            ["dep_enabled"] = new() { "NIST_CSF", "CIS" },
            ["aslr_enabled"] = new() { "NIST_CSF", "CIS" },
            ["ransomware_protection"] = new() { "Insurance", "NIST_CSF" },
            ["mfa_enabled"] = new() { "CyberSecure", "NIST_CSF", "PIPEDA", "Insurance" },
            ["encryption_at_rest"] = new() { "PIPEDA", "NIST_CSF", "Insurance" },
            ["privacy_settings"] = new() { "PIPEDA" },
            ["data_retention_policy"] = new() { "PIPEDA" },
            ["incident_response_plan"] = new() { "NIST_CSF", "Insurance" },
            ["vulnerability_scanning"] = new() { "NIST_CSF", "Insurance" },
            ["endpoint_detection"] = new() { "NIST_CSF", "Insurance" },
            ["network_segmentation"] = new() { "NIST_CSF", "Insurance" },
        };

        private static readonly Dictionary<string, string> FrameworkNames = new()
        {
            ["CyberSecure"] = "CyberSecure Canada",
            ["NIST_CSF"] = "NIST Cybersecurity Framework",
            ["CIS"] = "CIS Controls v8",
            ["PIPEDA"] = "PIPEDA / Privacy",
            ["Insurance"] = "Cyber Insurance Requirements"
        };

        private static List<string> GetFrameworksForCheck(string checkId)
        {
            if (FrameworkMapping.TryGetValue(checkId, out var frameworks))
                return frameworks.Select(f => FrameworkNames.GetValueOrDefault(f, f)).ToList();
            return new List<string>();
        }

        private static List<FrameworkScore> CalculateFrameworkScores(List<SecurityCheckReport> checks)
        {
            var frameworkScores = new Dictionary<string, (int total, int passed, List<string> failed)>();

            // Initialize frameworks
            foreach (var (id, name) in FrameworkNames)
                frameworkScores[id] = (0, 0, new List<string>());

            // Map each check to its frameworks
            foreach (var check in checks)
            {
                if (FrameworkMapping.TryGetValue(check.Id, out var frameworks))
                {
                    foreach (var fw in frameworks)
                    {
                        if (!frameworkScores.ContainsKey(fw)) continue;
                        var (total, passed, failed) = frameworkScores[fw];
                        total++;
                        if (check.Passed)
                            passed++;
                        else
                            failed.Add(check.Id);
                        frameworkScores[fw] = (total, passed, failed);
                    }
                }
            }

            return frameworkScores
                .Where(kvp => kvp.Value.total > 0)
                .Select(kvp => new FrameworkScore
                {
                    FrameworkId = kvp.Key,
                    FrameworkName = FrameworkNames.GetValueOrDefault(kvp.Key, kvp.Key),
                    ScorePercent = (float)kvp.Value.passed / kvp.Value.total * 100f,
                    Grade = CalculateGrade((float)kvp.Value.passed / kvp.Value.total * 100f),
                    TotalControls = kvp.Value.total,
                    PassedControls = kvp.Value.passed,
                    FailedControls = kvp.Value.total - kvp.Value.passed,
                    FailedCheckIds = kvp.Value.failed
                })
                .OrderByDescending(f => f.ScorePercent)
                .ToList();
        }

        private static string CalculateGrade(float percent)
        {
            return percent switch
            {
                >= 95 => "A+",
                >= 90 => "A",
                >= 85 => "A-",
                >= 80 => "B+",
                >= 75 => "B",
                >= 70 => "B-",
                >= 65 => "C+",
                >= 60 => "C",
                >= 55 => "C-",
                >= 50 => "D",
                _ => "F"
            };
        }
    }
}
