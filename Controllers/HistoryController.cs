using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Services;

namespace PCPlus.Dashboard.Controllers
{
    /// <summary>
    /// API endpoints for device history trend data.
    /// Returns daily-aggregated metrics suitable for chart rendering.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/dashboard/history")]
    public class HistoryController : ControllerBase
    {
        private readonly DashboardDb _db;

        public HistoryController(DashboardDb db)
        {
            _db = db;
        }

        /// <summary>GET /api/dashboard/history/device/{deviceId}?days=30 - History for one device.</summary>
        [HttpGet("device/{deviceId}")]
        public async Task<ActionResult> GetDeviceHistory(string deviceId, [FromQuery] int days = 30)
        {
            days = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.AddDays(-days);

            var data = await _db.DeviceHistories
                .AsNoTracking()
                .Where(h => h.DeviceId == deviceId && h.Timestamp >= since)
                .GroupBy(h => h.Timestamp.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    AvgSecurityScore = Math.Round(g.Average(h => h.SecurityScore), 1),
                    AvgCpuPercent = Math.Round(g.Average(h => (double)h.CpuPercent), 1),
                    AvgRamPercent = Math.Round(g.Average(h => (double)h.RamPercent), 1),
                    AvgDiskPercent = Math.Round(g.Average(h => (double)h.DiskPercent), 1),
                    AvgCpuTempC = Math.Round(g.Average(h => (double)h.CpuTempC), 1),
                    OnlinePercent = Math.Round(g.Average(h => h.IsOnline ? 100.0 : 0.0), 1),
                    SampleCount = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToListAsync();

            return Ok(data);
        }

        /// <summary>GET /api/dashboard/history/customer/{customerName}?days=30 - Aggregated history for a customer.</summary>
        [HttpGet("customer/{customerName}")]
        public async Task<ActionResult> GetCustomerHistory(string customerName, [FromQuery] int days = 30)
        {
            days = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.AddDays(-days);

            var data = await _db.DeviceHistories
                .AsNoTracking()
                .Where(h => h.CustomerName == customerName && h.Timestamp >= since)
                .GroupBy(h => h.Timestamp.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    AvgSecurityScore = Math.Round(g.Average(h => h.SecurityScore), 1),
                    AvgCpuPercent = Math.Round(g.Average(h => (double)h.CpuPercent), 1),
                    AvgRamPercent = Math.Round(g.Average(h => (double)h.RamPercent), 1),
                    AvgDiskPercent = Math.Round(g.Average(h => (double)h.DiskPercent), 1),
                    AvgCpuTempC = Math.Round(g.Average(h => (double)h.CpuTempC), 1),
                    OnlinePercent = Math.Round(g.Average(h => h.IsOnline ? 100.0 : 0.0), 1),
                    DeviceCount = g.Select(h => h.DeviceId).Distinct().Count(),
                    SampleCount = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToListAsync();

            return Ok(data);
        }

        /// <summary>GET /api/dashboard/history/overview?days=30 - Aggregated history across all devices.</summary>
        [HttpGet("overview")]
        public async Task<ActionResult> GetOverviewHistory([FromQuery] int days = 30)
        {
            days = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.AddDays(-days);

            var data = await _db.DeviceHistories
                .AsNoTracking()
                .Where(h => h.Timestamp >= since)
                .GroupBy(h => h.Timestamp.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    AvgSecurityScore = Math.Round(g.Average(h => h.SecurityScore), 1),
                    AvgCpuPercent = Math.Round(g.Average(h => (double)h.CpuPercent), 1),
                    AvgRamPercent = Math.Round(g.Average(h => (double)h.RamPercent), 1),
                    AvgDiskPercent = Math.Round(g.Average(h => (double)h.DiskPercent), 1),
                    AvgCpuTempC = Math.Round(g.Average(h => (double)h.CpuTempC), 1),
                    OnlinePercent = Math.Round(g.Average(h => h.IsOnline ? 100.0 : 0.0), 1),
                    DeviceCount = g.Select(h => h.DeviceId).Distinct().Count(),
                    SampleCount = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToListAsync();

            return Ok(data);
        }
    }
}
