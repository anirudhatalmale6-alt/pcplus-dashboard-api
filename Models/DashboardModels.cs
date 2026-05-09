using System.ComponentModel.DataAnnotations;

namespace PCPlus.Dashboard.Models
{
    /// <summary>Registered endpoint device.</summary>
    public class Device
    {
        [Key]
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string AgentVersion { get; set; } = "";
        public string LicenseTier { get; set; } = "Free";
        public string LicenseKey { get; set; } = "";
        public string PolicyProfile { get; set; } = "default";
        public string IpAddress { get; set; } = "";
        public string LocalIp { get; set; } = "";
        public string PublicIp { get; set; } = "";
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        // Latest health snapshot
        public float CpuPercent { get; set; }
        public float RamPercent { get; set; }
        public float DiskPercent { get; set; }
        public float CpuTempC { get; set; }
        public float GpuTempC { get; set; }
        public int HealthScore { get; set; }
        public int SecurityScore { get; set; }
        public string SecurityGrade { get; set; } = "?";
        public bool LockdownActive { get; set; }
        public int ActiveAlerts { get; set; }
        public int RunningModules { get; set; }

        // Security check details (JSON)
        public string SecurityChecksJson { get; set; } = "[]";

        // Installed software inventory (JSON)
        public string InstalledSoftwareJson { get; set; } = "[]";

        // BitLocker recovery keys (JSON)
        public string BitLockerKeysJson { get; set; } = "[]";

        // Storage drives detail (JSON)
        public string StorageDrivesJson { get; set; } = "[]";

        // Network details
        public string MacAddress { get; set; } = "";
        public float NetworkUpMbps { get; set; }
        public float NetworkDownMbps { get; set; }

        // Policy engine
        public int PolicyActiveRules { get; set; }
        public int PolicyViolations24h { get; set; }

        // WiFi security
        public int WifiNetworks { get; set; }
        public int WifiUnsecure { get; set; }
        public string WifiConnectedSsid { get; set; } = "";
    }

    /// <summary>Alert received from an endpoint.</summary>
    public class DashboardAlert
    {
        [Key]
        public int Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string ModuleId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "Info";
        public string Category { get; set; } = "";
        public bool Acknowledged { get; set; }
        public string AcknowledgedBy { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>Config push - queued config changes for an endpoint to pick up.</summary>
    public class ConfigPush
    {
        [Key]
        public int Id { get; set; }
        public string DeviceId { get; set; } = "";    // Empty = apply to all devices
        public string PolicyProfile { get; set; } = ""; // Empty = device-specific
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Applied { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AppliedAt { get; set; }
        public string CreatedBy { get; set; } = "";
    }

    /// <summary>Incident record for history tracking.</summary>
    public class Incident
    {
        [Key]
        public int Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string Type { get; set; } = "";         // threat, lockdown, policy_violation
        public string Description { get; set; } = "";
        public string Severity { get; set; } = "";
        public string ActionsTaken { get; set; } = "";
        public bool Resolved { get; set; }
        public string ResolvedBy { get; set; } = "";
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
    }

    /// <summary>Policy profile - named set of config values.</summary>
    public class PolicyProfile
    {
        [Key]
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ConfigJson { get; set; } = "{}"; // JSON dictionary of config key-value pairs
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; } = "";
    }

    /// <summary>Dashboard user/admin account.</summary>
    public class DashboardUser
    {
        [Key]
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Role { get; set; } = "viewer";  // admin, operator, viewer
        public string DisplayName { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastLogin { get; set; }
        public string ApiToken { get; set; } = "";
        public string CustomerName { get; set; } = "";  // For customer portal: limits access to this customer only
    }

    /// <summary>Email report schedule.</summary>
    public class EmailSchedule
    {
        [Key]
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";
        public string RecipientEmails { get; set; } = "";  // comma-separated
        public string Frequency { get; set; } = "weekly";  // weekly, biweekly, monthly
        public int DayOfWeek { get; set; } = 1;           // 0=Sun, 1=Mon...
        public int Hour { get; set; } = 8;                // UTC hour
        public bool Enabled { get; set; } = true;
        public DateTime? LastSentAt { get; set; }
        public DateTime? NextSendAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = "";
    }

    /// <summary>SMTP configuration.</summary>
    public class SmtpConfig
    {
        [Key]
        public int Id { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = "PC Plus Computing";
        public bool UseSsl { get; set; } = true;
    }

    // --- API request/response models ---

    /// <summary>Heartbeat sent by endpoints every 30 seconds.</summary>
    public class HeartbeatRequest
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string LocalIp { get; set; } = "";
        public string PublicIp { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string AgentVersion { get; set; } = "";
        public string LicenseTier { get; set; } = "";
        public float CpuPercent { get; set; }
        public float RamPercent { get; set; }
        public float DiskPercent { get; set; }
        public float CpuTempC { get; set; }
        public float GpuTempC { get; set; }
        public int SecurityScore { get; set; }
        public string SecurityGrade { get; set; } = "";
        public bool LockdownActive { get; set; }
        public int ActiveAlerts { get; set; }
        public int RunningModules { get; set; }
        public List<ModuleStatusReport> Modules { get; set; } = new();
        public List<SecurityCheckReport>? SecurityChecks { get; set; }
        public List<InstalledSoftwareReport>? InstalledSoftware { get; set; }
        public List<BitLockerKeyReport>? BitLockerRecoveryKeys { get; set; }

        // Policy engine data
        public int PolicyActiveRules { get; set; }
        public int PolicyViolations24h { get; set; }

        // WiFi security data
        public int WifiNetworks { get; set; }
        public int WifiUnsecure { get; set; }
        public string WifiConnectedSsid { get; set; } = "";
    }

    public class BitLockerKeyReport
    {
        public string DriveLetter { get; set; } = "";
        public string KeyProtectorId { get; set; } = "";
        public string RecoveryKey { get; set; } = "";
    }

    public class SecurityCheckReport
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public bool Passed { get; set; }
        public string Detail { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public int Weight { get; set; }
        public DateTime? LastChecked { get; set; }
    }

    public class InstalledSoftwareReport
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string InstallDate { get; set; } = "";
        public bool IsOutdated { get; set; }
    }

    public class ModuleStatusReport
    {
        public string ModuleId { get; set; } = "";
        public string ModuleName { get; set; } = "";
        public bool IsRunning { get; set; }
        public string StatusText { get; set; } = "";
        public Dictionary<string, object> Metrics { get; set; } = new();
    }

    /// <summary>Response to heartbeat - may include pending config changes.</summary>
    public class HeartbeatResponse
    {
        public bool Ok { get; set; } = true;
        public List<ConfigChange> PendingConfig { get; set; } = new();
        public string? Command { get; set; }  // Optional command: "rescan", "lockdown", "restart"
        public string? CustomerName { get; set; }  // Sync customer name from dashboard to agent
    }

    public class ConfigChange
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    /// <summary>Alert report from endpoint.</summary>
    public class AlertReport
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string ModuleId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class AlertAckRequest
    {
        public int AlertId { get; set; }
        public string? AcknowledgedBy { get; set; }
    }

    /// <summary>Dashboard overview statistics.</summary>
    public class DashboardOverview
    {
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OfflineDevices { get; set; }
        public int ActiveAlerts { get; set; }
        public int CriticalAlerts { get; set; }
        public int DevicesInLockdown { get; set; }
        public int OpenIncidents { get; set; }
        public float AvgSecurityScore { get; set; }
        public float AvgHealthScore { get; set; }
        public Dictionary<string, int> DevicesByTier { get; set; } = new();
    }
}
