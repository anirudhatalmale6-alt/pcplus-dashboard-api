using System.ComponentModel.DataAnnotations;

namespace PCPlus.Dashboard.Models
{
    // --- Entity models (stored in SQLite) ---

    /// <summary>Consolidated security event log from all sources.</summary>
    public class SecurityLog
    {
        [Key]
        public long Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string EventType { get; set; } = "";
        public string Source { get; set; } = "";           // Wazuh, WindowsSecurity, PCPlusAgent, UrBackup
        public string Severity { get; set; } = "Info";     // Critical, High, Medium, Low, Info
        public string Category { get; set; } = "";         // Authentication, FileIntegrity, Network, System, Compliance, Backup
        public string Message { get; set; } = "";
        public string DetailsJson { get; set; } = "{}";    // Arbitrary JSON payload
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Backup status for a device.</summary>
    public class BackupStatus
    {
        [Key]
        public long Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string Provider { get; set; } = "";         // UrBackup, WindowsBackup, Veeam
        public DateTime? LastBackupTime { get; set; }
        public DateTime? NextBackupTime { get; set; }
        public string Status { get; set; } = "";           // Success, Failed, InProgress, Overdue
        public long SizeBytes { get; set; }
        public string ProtectedPathsJson { get; set; } = "[]";
        public bool ShadowCopyEnabled { get; set; }
        public int ShadowCopyCount { get; set; }
        public int RecoveryPointCount { get; set; }
        public DateTime? LastTestedAt { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Network security state for a device.</summary>
    public class NetworkSecurityData
    {
        [Key]
        public long Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public bool FirewallEnabled { get; set; }
        public string FirewallProfilesJson { get; set; } = "[]";   // JSON array of {Name, Enabled, DefaultAction}
        public string OpenPortsJson { get; set; } = "[]";          // JSON array of {Port, Protocol, Process, State}
        public int ActiveConnections { get; set; }
        public bool RdpEnabled { get; set; }
        public string DnsServersJson { get; set; } = "[]";         // JSON array of DNS server IPs
        public string WifiSecurityType { get; set; } = "";         // WPA3, WPA2, WEP, Open, N/A
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Ransomware protection state for a device.</summary>
    public class RansomwareStatus
    {
        [Key]
        public long Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public bool BehaviorMonitoringEnabled { get; set; }
        public string ProtectedFoldersJson { get; set; } = "[]";   // JSON array of protected folder paths
        public bool ShadowCopyProtected { get; set; }
        public bool HoneypotActive { get; set; }
        public bool RollbackCapable { get; set; }
        public string DetectionRulesJson { get; set; } = "[]";     // JSON array of {RuleName, Enabled, Severity}
        public string ThreatHistoryJson { get; set; } = "[]";      // JSON array of {DetectedAt, ThreatName, Action, FilePath}
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Antivirus/security product installed on a device.</summary>
    public class AntivirusProduct
    {
        [Key]
        public long Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string Version { get; set; } = "";
        public DateTime? DefinitionDate { get; set; }
        public string Status { get; set; } = "";           // Active, Passive, OnDemand
        public bool RealTimeEnabled { get; set; }
        public DateTime? LastScanTime { get; set; }
        public int QuarantineCount { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    // --- Response DTOs ---

    /// <summary>175-point security audit results grouped by category.</summary>
    public class ScanResultsResponse
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public int TotalChecks { get; set; }
        public int PassedChecks { get; set; }
        public int FailedChecks { get; set; }
        public int SecurityScore { get; set; }
        public string SecurityGrade { get; set; } = "";
        public List<ScanCategoryGroup> Categories { get; set; } = new();
    }

    public class ScanCategoryGroup
    {
        public string Category { get; set; } = "";
        public int TotalChecks { get; set; }
        public int PassedChecks { get; set; }
        public float CompliancePercent { get; set; }
        public List<ScanCheckItem> Checks { get; set; } = new();
    }

    public class ScanCheckItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public bool Passed { get; set; }
        public string Detail { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public int Weight { get; set; }
        public DateTime? LastChecked { get; set; }
        public List<string> ComplianceFrameworks { get; set; } = new();
    }

    /// <summary>Access control data for a device.</summary>
    public class AccessControlResponse
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public List<LoginActivity> RecentLogins { get; set; } = new();
        public List<UserAccountInfo> UserAccounts { get; set; } = new();
        public MfaStatusInfo MfaStatus { get; set; } = new();
    }

    public class LoginActivity
    {
        public string EventType { get; set; } = "";
        public string Source { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class UserAccountInfo
    {
        public string Username { get; set; } = "";
        public string AccountType { get; set; } = "";      // Admin, Standard, Service
        public bool IsEnabled { get; set; }
        public bool PasswordExpires { get; set; }
        public DateTime? LastLogin { get; set; }
    }

    public class MfaStatusInfo
    {
        public bool WindowsHelloEnabled { get; set; }
        public bool SmartCardRequired { get; set; }
        public bool PinConfigured { get; set; }
        public string Detail { get; set; } = "";
    }

    /// <summary>Backup status response for a device.</summary>
    public class BackupResponse
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public List<BackupProviderStatus> Providers { get; set; } = new();
        public bool ShadowCopyEnabled { get; set; }
        public int ShadowCopyCount { get; set; }
        public int TotalRecoveryPoints { get; set; }
    }

    public class BackupProviderStatus
    {
        public string Provider { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? LastBackupTime { get; set; }
        public DateTime? NextBackupTime { get; set; }
        public long SizeBytes { get; set; }
        public List<string> ProtectedPaths { get; set; } = new();
        public int RecoveryPointCount { get; set; }
        public DateTime? LastTestedAt { get; set; }
    }

    /// <summary>Network security response for a device.</summary>
    public class NetworkSecurityResponse
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public bool FirewallEnabled { get; set; }
        public List<FirewallProfileInfo> FirewallProfiles { get; set; } = new();
        public List<OpenPortInfo> OpenPorts { get; set; } = new();
        public int ActiveConnections { get; set; }
        public bool RdpEnabled { get; set; }
        public List<string> DnsServers { get; set; } = new();
        public string WifiSecurityType { get; set; } = "";
        public DateTime? LastUpdated { get; set; }
    }

    public class FirewallProfileInfo
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; }
        public string DefaultAction { get; set; } = "";
    }

    public class OpenPortInfo
    {
        public int Port { get; set; }
        public string Protocol { get; set; } = "";
        public string Process { get; set; } = "";
        public string State { get; set; } = "";
    }

    /// <summary>Ransomware protection response for a device.</summary>
    public class RansomwareResponse
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public bool BehaviorMonitoringEnabled { get; set; }
        public List<string> ProtectedFolders { get; set; } = new();
        public bool ShadowCopyProtected { get; set; }
        public bool HoneypotActive { get; set; }
        public bool RollbackCapable { get; set; }
        public List<DetectionRuleInfo> DetectionRules { get; set; } = new();
        public List<ThreatHistoryItem> ThreatHistory { get; set; } = new();
        public DateTime? LastUpdated { get; set; }
    }

    public class DetectionRuleInfo
    {
        public string RuleName { get; set; } = "";
        public bool Enabled { get; set; }
        public string Severity { get; set; } = "";
    }

    public class ThreatHistoryItem
    {
        public DateTime DetectedAt { get; set; }
        public string ThreatName { get; set; } = "";
        public string Action { get; set; } = "";
        public string FilePath { get; set; } = "";
    }

    /// <summary>Realtime protection response showing AV products.</summary>
    public class RealtimeProtectionResponse
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public bool AnyRealTimeActive { get; set; }
        public int TotalProducts { get; set; }
        public List<AntivirusProductInfo> Products { get; set; } = new();
    }

    public class AntivirusProductInfo
    {
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string Version { get; set; } = "";
        public DateTime? DefinitionDate { get; set; }
        public string Status { get; set; } = "";           // Active, Passive, OnDemand
        public bool RealTimeEnabled { get; set; }
        public DateTime? LastScanTime { get; set; }
        public int QuarantineCount { get; set; }
        public bool DefinitionsOutdated { get; set; }
    }

    /// <summary>Compliance scores per framework.</summary>
    public class ComplianceResponse
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public int OverallScore { get; set; }
        public string OverallGrade { get; set; } = "";
        public List<FrameworkScore> Frameworks { get; set; } = new();
    }

    public class FrameworkScore
    {
        public string FrameworkId { get; set; } = "";
        public string FrameworkName { get; set; } = "";
        public float ScorePercent { get; set; }
        public string Grade { get; set; } = "";
        public int TotalControls { get; set; }
        public int PassedControls { get; set; }
        public int FailedControls { get; set; }
        public List<string> FailedCheckIds { get; set; } = new();
    }

    /// <summary>Customer summary for customer list.</summary>
    public class CustomerSummary
    {
        public string CustomerName { get; set; } = "";
        public string LicenseTier { get; set; } = "";
        public int DeviceCount { get; set; }
        public int OnlineDevices { get; set; }
        public float AvgSecurityScore { get; set; }
        public string AvgGrade { get; set; } = "";
        public int TotalAlerts { get; set; }
        public int CriticalAlerts { get; set; }
        public DateTime? LastSeen { get; set; }
    }

    /// <summary>Customer detail response.</summary>
    public class CustomerDetailResponse
    {
        public string CustomerName { get; set; } = "";
        public string LicenseTier { get; set; } = "";
        public int DeviceCount { get; set; }
        public int OnlineDevices { get; set; }
        public float AvgSecurityScore { get; set; }
        public string AvgGrade { get; set; } = "";
        public List<Device> Devices { get; set; } = new();
    }

    /// <summary>Security log query response with pagination.</summary>
    public class SecurityLogResponse
    {
        public int TotalCount { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public List<SecurityLog> Logs { get; set; } = new();
    }

    // --- Request DTOs for agent reporting ---

    public class SecurityLogReport
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string EventType { get; set; } = "";
        public string Source { get; set; } = "";
        public string Severity { get; set; } = "Info";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
        public string DetailsJson { get; set; } = "{}";
    }

    public class BackupStatusReport
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string Provider { get; set; } = "";
        public DateTime? LastBackupTime { get; set; }
        public DateTime? NextBackupTime { get; set; }
        public string Status { get; set; } = "";
        public long SizeBytes { get; set; }
        public List<string> ProtectedPaths { get; set; } = new();
        public bool ShadowCopyEnabled { get; set; }
        public int ShadowCopyCount { get; set; }
        public int RecoveryPointCount { get; set; }
        public DateTime? LastTestedAt { get; set; }
    }

    public class NetworkSecurityReport
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public bool FirewallEnabled { get; set; }
        public List<FirewallProfileInfo> FirewallProfiles { get; set; } = new();
        public List<OpenPortInfo> OpenPorts { get; set; } = new();
        public int ActiveConnections { get; set; }
        public bool RdpEnabled { get; set; }
        public List<string> DnsServers { get; set; } = new();
        public string WifiSecurityType { get; set; } = "";
    }

    public class RansomwareStatusReport
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public bool BehaviorMonitoringEnabled { get; set; }
        public List<string> ProtectedFolders { get; set; } = new();
        public bool ShadowCopyProtected { get; set; }
        public bool HoneypotActive { get; set; }
        public bool RollbackCapable { get; set; }
        public List<DetectionRuleInfo> DetectionRules { get; set; } = new();
        public List<ThreatHistoryItem> ThreatHistory { get; set; } = new();
    }

    public class AntivirusReport
    {
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public List<AntivirusProductReport> Products { get; set; } = new();
    }

    public class AntivirusProductReport
    {
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string Version { get; set; } = "";
        public DateTime? DefinitionDate { get; set; }
        public string Status { get; set; } = "";
        public bool RealTimeEnabled { get; set; }
        public DateTime? LastScanTime { get; set; }
        public int QuarantineCount { get; set; }
    }

    /// <summary>Request to update customer tier.</summary>
    public class SetTierRequest
    {
        public string Tier { get; set; } = "";
    }
}
