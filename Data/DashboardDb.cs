using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Models;
using PCPlus.Dashboard.Services;
using System.Text.Json;

namespace PCPlus.Dashboard.Data
{
    public class DashboardDb : DbContext
    {
        public DashboardDb(DbContextOptions<DashboardDb> options) : base(options) { }

        public DbSet<Device> Devices => Set<Device>();
        public DbSet<DashboardAlert> Alerts => Set<DashboardAlert>();
        public DbSet<ConfigPush> ConfigPushes => Set<ConfigPush>();
        public DbSet<Incident> Incidents => Set<Incident>();
        public DbSet<PolicyProfile> PolicyProfiles => Set<PolicyProfile>();
        public DbSet<DashboardUser> Users => Set<DashboardUser>();
        public DbSet<EmailSchedule> EmailSchedules => Set<EmailSchedule>();
        public DbSet<SmtpConfig> SmtpConfigs => Set<SmtpConfig>();
        public DbSet<DeviceHistory> DeviceHistories => Set<DeviceHistory>();
        public DbSet<PCPlus.Dashboard.Services.NotificationConfig> NotificationConfigs => Set<PCPlus.Dashboard.Services.NotificationConfig>();
        public DbSet<SecurityLog> SecurityLogs => Set<SecurityLog>();
        public DbSet<BackupStatus> BackupStatuses => Set<BackupStatus>();
        public DbSet<NetworkSecurityData> NetworkSecurityData => Set<NetworkSecurityData>();
        public DbSet<RansomwareStatus> RansomwareStatuses => Set<RansomwareStatus>();
        public DbSet<AntivirusProduct> AntivirusProducts => Set<AntivirusProduct>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Device>(e =>
            {
                e.HasIndex(d => d.CustomerId);
                e.HasIndex(d => d.IsOnline);
                e.HasIndex(d => d.LastSeen);
            });

            modelBuilder.Entity<DashboardAlert>(e =>
            {
                e.HasIndex(a => a.DeviceId);
                e.HasIndex(a => a.Severity);
                e.HasIndex(a => a.Acknowledged);
                e.HasIndex(a => a.Timestamp);
                e.Property(a => a.Metadata)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new());
            });

            modelBuilder.Entity<ConfigPush>(e =>
            {
                e.HasIndex(c => c.DeviceId);
                e.HasIndex(c => c.Applied);
            });

            modelBuilder.Entity<Incident>(e =>
            {
                e.HasIndex(i => i.DeviceId);
                e.HasIndex(i => i.Resolved);
            });

            modelBuilder.Entity<DashboardUser>(e =>
            {
                e.HasIndex(u => u.Username).IsUnique();
                e.HasIndex(u => u.ApiToken);
            });

            modelBuilder.Entity<DeviceHistory>(e =>
            {
                e.HasIndex(h => h.DeviceId);
                e.HasIndex(h => h.CustomerName);
                e.HasIndex(h => h.Timestamp);
            });

            modelBuilder.Entity<SecurityLog>(e =>
            {
                e.HasIndex(l => l.DeviceId);
                e.HasIndex(l => l.Severity);
                e.HasIndex(l => l.Category);
                e.HasIndex(l => l.Timestamp);
            });

            modelBuilder.Entity<BackupStatus>(e =>
            {
                e.HasIndex(b => b.DeviceId);
            });

            modelBuilder.Entity<NetworkSecurityData>(e =>
            {
                e.HasIndex(n => n.DeviceId);
            });

            modelBuilder.Entity<RansomwareStatus>(e =>
            {
                e.HasIndex(r => r.DeviceId);
            });

            modelBuilder.Entity<AntivirusProduct>(e =>
            {
                e.HasIndex(a => a.DeviceId);
            });

            // Seed default admin user and default policy profile
            modelBuilder.Entity<DashboardUser>().HasData(new DashboardUser
            {
                Id = 1,
                Username = "admin",
                // Default password: "pcplus2026" - SHA256 hash
                PasswordHash = "a23a6ffd2ec65d5bac0b47276832316cc802eae56dfee5b5c3cde8c59c64459b",
                Role = "admin",
                DisplayName = "Paul - PC Plus",
                ApiToken = ""
            });

            modelBuilder.Entity<PolicyProfile>().HasData(
                new PolicyProfile
                {
                    Name = "default",
                    Description = "Default policy - balanced protection",
                    ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["ransomwareProtectionEnabled"] = "true",
                        ["autoContainmentEnabled"] = "true",
                        ["lockdownOnDetection"] = "true",
                        ["blockUSB"] = "false",
                        ["blockPowerShell"] = "false",
                        ["scoringWarningThreshold"] = "30",
                        ["scoringContainmentThreshold"] = "60",
                        ["scoringLockdownThreshold"] = "80"
                    })
                },
                new PolicyProfile
                {
                    Name = "high-security",
                    Description = "High security - stricter thresholds, USB blocked, PowerShell restricted",
                    ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["ransomwareProtectionEnabled"] = "true",
                        ["autoContainmentEnabled"] = "true",
                        ["lockdownOnDetection"] = "true",
                        ["blockUSB"] = "true",
                        ["blockPowerShell"] = "true",
                        ["enforceBitLocker"] = "true",
                        ["scoringWarningThreshold"] = "20",
                        ["scoringContainmentThreshold"] = "40",
                        ["scoringLockdownThreshold"] = "60"
                    })
                },
                new PolicyProfile
                {
                    Name = "home-user",
                    Description = "Home user - relaxed thresholds, fewer false positives",
                    ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["ransomwareProtectionEnabled"] = "true",
                        ["autoContainmentEnabled"] = "true",
                        ["lockdownOnDetection"] = "false",
                        ["blockUSB"] = "false",
                        ["blockPowerShell"] = "false",
                        ["scoringWarningThreshold"] = "40",
                        ["scoringContainmentThreshold"] = "70",
                        ["scoringLockdownThreshold"] = "90"
                    })
                }
            );
        }
    }
}
