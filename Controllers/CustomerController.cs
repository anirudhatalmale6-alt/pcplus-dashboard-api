using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;

namespace PCPlus.Dashboard.Controllers
{
    /// <summary>
    /// API endpoints for customer management.
    /// Provides customer listing, detail, and tier management.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/dashboard/customers")]
    public class CustomerController : ControllerBase
    {
        private readonly DashboardDb _db;
        private readonly ILogger<CustomerController> _log;

        public CustomerController(DashboardDb db, ILogger<CustomerController> log)
        {
            _db = db;
            _log = log;
        }

        /// <summary>
        /// GET /api/dashboard/customers
        /// List all unique customers with device counts, avg scores, tier.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<CustomerSummary>>> GetCustomers(
            [FromQuery] string? search = null,
            [FromQuery] string? tier = null)
        {
            var devicesQuery = _db.Devices.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(search))
                devicesQuery = devicesQuery.Where(d => d.CustomerName.Contains(search));
            if (!string.IsNullOrEmpty(tier))
                devicesQuery = devicesQuery.Where(d => d.LicenseTier == tier);

            var devices = await devicesQuery.ToListAsync();

            // Get unacknowledged alert counts per customer
            var alertCounts = await _db.Alerts
                .AsNoTracking()
                .Where(a => !a.Acknowledged)
                .GroupBy(a => a.DeviceId)
                .Select(g => new { DeviceId = g.Key, Count = g.Count(), Critical = g.Count(a => a.Severity == "Critical" || a.Severity == "Emergency") })
                .ToListAsync();

            var alertLookup = alertCounts.ToDictionary(a => a.DeviceId, a => (a.Count, a.Critical));

            var customers = devices
                .Where(d => !string.IsNullOrEmpty(d.CustomerName))
                .GroupBy(d => d.CustomerName)
                .Select(g =>
                {
                    var totalAlerts = 0;
                    var criticalAlerts = 0;
                    foreach (var d in g)
                    {
                        if (alertLookup.TryGetValue(d.DeviceId, out var counts))
                        {
                            totalAlerts += counts.Count;
                            criticalAlerts += counts.Critical;
                        }
                    }

                    var avgScore = g.Average(d => d.SecurityScore);

                    return new CustomerSummary
                    {
                        CustomerName = g.Key,
                        LicenseTier = g.First().LicenseTier,
                        DeviceCount = g.Count(),
                        OnlineDevices = g.Count(d => d.IsOnline),
                        AvgSecurityScore = (float)Math.Round(avgScore, 1),
                        AvgGrade = CalculateGrade((float)avgScore),
                        TotalAlerts = totalAlerts,
                        CriticalAlerts = criticalAlerts,
                        LastSeen = g.Max(d => d.LastSeen)
                    };
                })
                .OrderBy(c => c.CustomerName)
                .ToList();

            return Ok(customers);
        }

        /// <summary>
        /// GET /api/dashboard/customers/{customerName}
        /// Customer detail with all devices.
        /// </summary>
        [HttpGet("{customerName}")]
        public async Task<ActionResult<CustomerDetailResponse>> GetCustomerDetail(string customerName)
        {
            customerName = System.Net.WebUtility.UrlDecode(customerName);

            var devices = await _db.Devices
                .AsNoTracking()
                .Where(d => d.CustomerName == customerName)
                .OrderByDescending(d => d.LastSeen)
                .ToListAsync();

            if (devices.Count == 0)
                return NotFound(new { message = $"No devices found for customer '{customerName}'" });

            var avgScore = devices.Average(d => d.SecurityScore);

            return Ok(new CustomerDetailResponse
            {
                CustomerName = customerName,
                LicenseTier = devices.First().LicenseTier,
                DeviceCount = devices.Count,
                OnlineDevices = devices.Count(d => d.IsOnline),
                AvgSecurityScore = (float)Math.Round(avgScore, 1),
                AvgGrade = CalculateGrade((float)avgScore),
                Devices = devices
            });
        }

        /// <summary>
        /// PUT /api/dashboard/customers/{customerName}/tier
        /// Update tier for all devices belonging to a customer.
        /// </summary>
        [HttpPut("{customerName}/tier")]
        public async Task<ActionResult> SetCustomerTier(string customerName, [FromBody] SetTierRequest req)
        {
            customerName = System.Net.WebUtility.UrlDecode(customerName);

            var validTiers = new[] { "Free", "Home", "Business", "Enterprise" };
            if (!validTiers.Contains(req.Tier))
                return BadRequest(new { message = $"Invalid tier '{req.Tier}'. Must be one of: {string.Join(", ", validTiers)}" });

            var devices = await _db.Devices
                .Where(d => d.CustomerName == customerName)
                .ToListAsync();

            if (devices.Count == 0)
                return NotFound(new { message = $"No devices found for customer '{customerName}'" });

            foreach (var device in devices)
            {
                device.LicenseTier = req.Tier;

                // Push tier update to agent
                _db.ConfigPushes.Add(new ConfigPush
                {
                    DeviceId = device.DeviceId,
                    Key = "licenseTier",
                    Value = req.Tier,
                    CreatedBy = "dashboard"
                });
            }

            await _db.SaveChangesAsync();
            _log.LogInformation("Customer '{CustomerName}' tier updated to '{Tier}' ({Count} devices)",
                customerName, req.Tier, devices.Count);

            return Ok(new { updated = true, customerName, tier = req.Tier, deviceCount = devices.Count });
        }

        private static string CalculateGrade(float score)
        {
            return score switch
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
