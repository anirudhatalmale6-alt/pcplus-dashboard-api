using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;

namespace PCPlus.Dashboard.Services
{
    /// <summary>Snapshot of a device's stats at a point in time, for trend charts.</summary>
    public class DeviceHistory
    {
        [Key]
        public long Id { get; set; }
        public string DeviceId { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public int SecurityScore { get; set; }
        public float CpuPercent { get; set; }
        public float RamPercent { get; set; }
        public float DiskPercent { get; set; }
        public float CpuTempC { get; set; }
        public bool IsOnline { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Background service that snapshots device stats every 30 minutes
    /// into DeviceHistory for trend charts, and prunes records older than 90 days.
    /// </summary>
    public class HistoryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<HistoryService> _log;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);
        private readonly int _retentionDays = 90;

        public HistoryService(IServiceScopeFactory scopeFactory, ILogger<HistoryService> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("HistoryService started — interval {Interval} min, retention {Days} days",
                _interval.TotalMinutes, _retentionDays);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SnapshotAndPrune(stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "HistoryService tick failed");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task SnapshotAndPrune(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DashboardDb>();

            // --- Snapshot every device ---
            var devices = await db.Devices.AsNoTracking().ToListAsync(ct);
            var now = DateTime.UtcNow;

            foreach (var d in devices)
            {
                db.DeviceHistories.Add(new DeviceHistory
                {
                    DeviceId = d.DeviceId,
                    Hostname = d.Hostname,
                    CustomerName = d.CustomerName,
                    SecurityScore = d.SecurityScore,
                    CpuPercent = d.CpuPercent,
                    RamPercent = d.RamPercent,
                    DiskPercent = d.DiskPercent,
                    CpuTempC = d.CpuTempC,
                    IsOnline = d.IsOnline,
                    Timestamp = now
                });
            }

            await db.SaveChangesAsync(ct);
            _log.LogInformation("HistoryService: snapshotted {Count} devices", devices.Count);

            // --- Prune old records ---
            var cutoff = now.AddDays(-_retentionDays);
            var deleted = await db.DeviceHistories
                .Where(h => h.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _log.LogInformation("HistoryService: pruned {Count} records older than {Days} days", deleted, _retentionDays);
        }
    }
}
