using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Services;

namespace PCPlus.Dashboard.Controllers
{
    /// <summary>
    /// Manage notification configs (webhook, Slack, Teams) and send test notifications.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/dashboard/notifications")]
    public class NotificationController : ControllerBase
    {
        private readonly DashboardDb _db;
        private readonly NotificationService _notifications;
        private readonly ILogger<NotificationController> _log;

        public NotificationController(DashboardDb db, NotificationService notifications, ILogger<NotificationController> log)
        {
            _db = db;
            _notifications = notifications;
            _log = log;
        }

        /// <summary>GET /api/dashboard/notifications - List all notification configs.</summary>
        [HttpGet]
        public async Task<ActionResult<List<NotificationConfig>>> GetAll()
        {
            var configs = await _db.Set<NotificationConfig>()
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
            return Ok(configs);
        }

        /// <summary>POST /api/dashboard/notifications - Create a notification config.</summary>
        [HttpPost]
        public async Task<ActionResult<NotificationConfig>> Create([FromBody] NotificationConfigDto dto)
        {
            var config = new NotificationConfig
            {
                Name = dto.Name,
                Type = dto.Type,
                WebhookUrl = dto.WebhookUrl,
                MinSeverity = dto.MinSeverity,
                Enabled = dto.Enabled,
                CreatedAt = DateTime.UtcNow
            };

            _db.Set<NotificationConfig>().Add(config);
            await _db.SaveChangesAsync();

            _log.LogInformation("Created notification config '{Name}' (type={Type})", config.Name, config.Type);
            return CreatedAtAction(nameof(GetAll), new { id = config.Id }, config);
        }

        /// <summary>PUT /api/dashboard/notifications/{id} - Update a notification config.</summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<NotificationConfig>> Update(int id, [FromBody] NotificationConfigDto dto)
        {
            var config = await _db.Set<NotificationConfig>().FindAsync(id);
            if (config == null)
                return NotFound(new { error = "Notification config not found" });

            config.Name = dto.Name;
            config.Type = dto.Type;
            config.WebhookUrl = dto.WebhookUrl;
            config.MinSeverity = dto.MinSeverity;
            config.Enabled = dto.Enabled;

            await _db.SaveChangesAsync();

            _log.LogInformation("Updated notification config '{Name}' (id={Id})", config.Name, config.Id);
            return Ok(config);
        }

        /// <summary>DELETE /api/dashboard/notifications/{id} - Delete a notification config.</summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var config = await _db.Set<NotificationConfig>().FindAsync(id);
            if (config == null)
                return NotFound(new { error = "Notification config not found" });

            _db.Set<NotificationConfig>().Remove(config);
            await _db.SaveChangesAsync();

            _log.LogInformation("Deleted notification config '{Name}' (id={Id})", config.Name, id);
            return NoContent();
        }

        /// <summary>POST /api/dashboard/notifications/{id}/test - Send a test notification.</summary>
        [HttpPost("{id}/test")]
        public async Task<ActionResult> SendTest(int id)
        {
            var config = await _db.Set<NotificationConfig>().FindAsync(id);
            if (config == null)
                return NotFound(new { error = "Notification config not found" });

            // Temporarily force-enable and set severity to Info so the test always fires
            var originalEnabled = config.Enabled;
            var originalSeverity = config.MinSeverity;
            try
            {
                config.Enabled = true;
                config.MinSeverity = "Info";

                await _notifications.SendNotificationAsync(
                    title: "PC Plus Test Notification",
                    message: $"This is a test notification from config '{config.Name}'. If you see this, your {config.Type} integration is working correctly.",
                    severity: "Info",
                    deviceId: "test-device",
                    hostname: "dashboard-test"
                );

                return Ok(new { success = true, message = $"Test notification sent via {config.Type}" });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Test notification failed for config '{Name}'", config.Name);
                return StatusCode(500, new { error = "Test notification failed", detail = ex.Message });
            }
            finally
            {
                // Restore original values (in-memory only, not saved)
                config.Enabled = originalEnabled;
                config.MinSeverity = originalSeverity;
            }
        }
    }

    /// <summary>DTO for creating/updating notification configs.</summary>
    public class NotificationConfigDto
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "webhook";
        public string WebhookUrl { get; set; } = "";
        public string MinSeverity { get; set; } = "Critical";
        public bool Enabled { get; set; } = true;
    }
}
