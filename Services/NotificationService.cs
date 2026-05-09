using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PCPlus.Dashboard.Data;

namespace PCPlus.Dashboard.Services
{
    /// <summary>
    /// Singleton service that dispatches notifications (webhook, Slack, Teams)
    /// based on notification configs stored in the database.
    /// </summary>
    public class NotificationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<NotificationService> _log;

        private static readonly Dictionary<string, int> SeverityRank = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Info"] = 0,
            ["Warning"] = 1,
            ["Critical"] = 2,
            ["Emergency"] = 3
        };

        public NotificationService(
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            ILogger<NotificationService> log)
        {
            _scopeFactory = scopeFactory;
            _httpClientFactory = httpClientFactory;
            _log = log;
        }

        /// <summary>
        /// Send a notification to all enabled configs whose minimum severity is met.
        /// </summary>
        public async Task SendNotificationAsync(string title, string message, string severity, string deviceId, string hostname)
        {
            List<NotificationConfig> configs;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DashboardDb>();
                configs = await db.Set<NotificationConfig>()
                    .Where(c => c.Enabled)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to load notification configs from database");
                return;
            }

            var eventRank = SeverityRank.GetValueOrDefault(severity, 0);

            foreach (var config in configs)
            {
                var minRank = SeverityRank.GetValueOrDefault(config.MinSeverity, 2);
                if (eventRank < minRank)
                    continue;

                try
                {
                    switch (config.Type.ToLowerInvariant())
                    {
                        case "webhook":
                            await SendWebhookAsync(config, title, message, severity, deviceId, hostname);
                            break;
                        case "slack":
                            await SendSlackAsync(config, title, message, severity, deviceId, hostname);
                            break;
                        case "teams":
                            await SendTeamsAsync(config, title, message, severity, deviceId, hostname);
                            break;
                        case "email":
                            await SendEmailAlertAsync(config, title, message, severity, deviceId, hostname);
                            break;
                        case "sms":
                            await SendSmsAsync(config, title, message, severity, deviceId, hostname);
                            break;
                        default:
                            _log.LogWarning("Unknown notification type '{Type}' for config '{Name}'", config.Type, config.Name);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to send {Type} notification '{Name}' to {Url}",
                        config.Type, config.Name, config.WebhookUrl);
                }
            }
        }

        private async Task SendWebhookAsync(NotificationConfig config, string title, string message, string severity, string deviceId, string hostname)
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new
            {
                title,
                message,
                severity,
                deviceId,
                hostname,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            var response = await client.PostAsJsonAsync(config.WebhookUrl, payload);
            _log.LogInformation("Webhook '{Name}' responded {StatusCode}", config.Name, response.StatusCode);
        }

        private async Task SendSlackAsync(NotificationConfig config, string title, string message, string severity, string deviceId, string hostname)
        {
            var client = _httpClientFactory.CreateClient();
            var emoji = severity switch
            {
                "Emergency" => ":rotating_light:",
                "Critical" => ":red_circle:",
                "Warning" => ":warning:",
                _ => ":information_source:"
            };

            var payload = new
            {
                blocks = new object[]
                {
                    new
                    {
                        type = "header",
                        text = new { type = "plain_text", text = $"{emoji} {title}" }
                    },
                    new
                    {
                        type = "section",
                        fields = new object[]
                        {
                            new { type = "mrkdwn", text = $"*Severity:*\n{severity}" },
                            new { type = "mrkdwn", text = $"*Device:*\n{hostname} ({deviceId})" }
                        }
                    },
                    new
                    {
                        type = "section",
                        text = new { type = "mrkdwn", text = message }
                    }
                }
            };

            var response = await client.PostAsJsonAsync(config.WebhookUrl, payload);
            _log.LogInformation("Slack '{Name}' responded {StatusCode}", config.Name, response.StatusCode);
        }

        private async Task SendTeamsAsync(NotificationConfig config, string title, string message, string severity, string deviceId, string hostname)
        {
            var client = _httpClientFactory.CreateClient();
            var color = severity switch
            {
                "Emergency" => "attention",
                "Critical" => "attention",
                "Warning" => "warning",
                _ => "default"
            };

            var payload = new
            {
                type = "message",
                attachments = new object[]
                {
                    new
                    {
                        contentType = "application/vnd.microsoft.card.adaptive",
                        content = new
                        {
                            type = "AdaptiveCard",
                            version = "1.4",
                            body = new object[]
                            {
                                new
                                {
                                    type = "TextBlock",
                                    text = title,
                                    weight = "Bolder",
                                    size = "Medium",
                                    color
                                },
                                new
                                {
                                    type = "FactSet",
                                    facts = new object[]
                                    {
                                        new { title = "Severity", value = severity },
                                        new { title = "Device", value = hostname },
                                        new { title = "Device ID", value = deviceId },
                                        new { title = "Time", value = DateTime.UtcNow.ToString("o") }
                                    }
                                },
                                new
                                {
                                    type = "TextBlock",
                                    text = message,
                                    wrap = true
                                }
                            }
                        }
                    }
                }
            };

            var response = await client.PostAsJsonAsync(config.WebhookUrl, payload);
            _log.LogInformation("Teams '{Name}' responded {StatusCode}", config.Name, response.StatusCode);
        }

        private async Task SendEmailAlertAsync(NotificationConfig config, string title, string message, string severity, string deviceId, string hostname)
        {
            // Load SMTP config from database
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DashboardDb>();
            var smtp = await db.SmtpConfigs.FirstOrDefaultAsync();
            if (smtp == null || string.IsNullOrEmpty(smtp.Host))
            {
                _log.LogWarning("Email alert skipped - no SMTP configured");
                return;
            }

            var severityEmoji = severity switch
            {
                "Emergency" => "[EMERGENCY]",
                "Critical" => "[CRITICAL]",
                "Warning" => "[WARNING]",
                _ => "[INFO]"
            };

            var subject = $"{severityEmoji} {title} - {hostname}";
            var htmlBody = $@"
<div style='font-family: Segoe UI, Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <div style='background: {(severity is "Emergency" or "Critical" ? "#dc3545" : severity == "Warning" ? "#f5a623" : "#0078d7")}; color: white; padding: 16px 24px; border-radius: 8px 8px 0 0;'>
        <h2 style='margin: 0; font-size: 18px;'>{severityEmoji} PC Plus Endpoint Alert</h2>
    </div>
    <div style='background: #ffffff; border: 1px solid #e0e0e0; border-top: none; padding: 24px; border-radius: 0 0 8px 8px;'>
        <h3 style='margin-top: 0; color: #333;'>{title}</h3>
        <p style='color: #555; line-height: 1.6;'>{message}</p>
        <table style='width: 100%; border-collapse: collapse; margin-top: 16px;'>
            <tr><td style='padding: 8px 0; color: #888; width: 120px;'>Severity:</td><td style='padding: 8px 0; font-weight: bold;'>{severity}</td></tr>
            <tr><td style='padding: 8px 0; color: #888;'>Device:</td><td style='padding: 8px 0;'>{hostname}</td></tr>
            <tr><td style='padding: 8px 0; color: #888;'>Device ID:</td><td style='padding: 8px 0; font-family: monospace;'>{deviceId}</td></tr>
            <tr><td style='padding: 8px 0; color: #888;'>Time:</td><td style='padding: 8px 0;'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</td></tr>
        </table>
        <div style='margin-top: 20px; padding-top: 16px; border-top: 1px solid #eee; color: #999; font-size: 12px;'>
            PC Plus Computing - Endpoint Protection Dashboard
        </div>
    </div>
</div>";

            var recipients = config.WebhookUrl.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                Credentials = new NetworkCredential(smtp.Username, smtp.Password),
                EnableSsl = smtp.UseSsl
            };

            foreach (var email in recipients)
            {
                var msg = new MailMessage
                {
                    From = new MailAddress(smtp.FromAddress, smtp.FromName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                msg.To.Add(email.Trim());
                await client.SendMailAsync(msg);
            }

            _log.LogInformation("Email alert sent to {Recipients} for '{Title}'", config.WebhookUrl, title);
        }

        private async Task SendSmsAsync(NotificationConfig config, string title, string message, string severity, string deviceId, string hostname)
        {
            // Twilio SMS via REST API (no SDK dependency needed)
            // config.WebhookUrl format: "accountSid:authToken:fromNumber:toNumber1,toNumber2"
            var parts = config.WebhookUrl.Split(':');
            if (parts.Length < 4)
            {
                _log.LogWarning("SMS config '{Name}' invalid format. Expected: accountSid:authToken:fromNumber:toNumbers", config.Name);
                return;
            }

            var accountSid = parts[0];
            var authToken = parts[1];
            var fromNumber = parts[2];
            var toNumbers = parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var smsBody = $"[PC Plus] {severity}: {title} on {hostname} - {message}";
            if (smsBody.Length > 160) smsBody = smsBody[..157] + "...";

            var client = _httpClientFactory.CreateClient();
            var authBytes = System.Text.Encoding.ASCII.GetBytes($"{accountSid}:{authToken}");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            foreach (var toNumber in toNumbers)
            {
                var formData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("To", toNumber.Trim()),
                    new KeyValuePair<string, string>("From", fromNumber),
                    new KeyValuePair<string, string>("Body", smsBody)
                });

                var response = await client.PostAsync(
                    $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json",
                    formData);

                _log.LogInformation("SMS to {To} responded {StatusCode}", toNumber, response.StatusCode);
            }
        }
    }

    /// <summary>
    /// Notification configuration entity stored in the database.
    /// </summary>
    public class NotificationConfig
    {
        [System.ComponentModel.DataAnnotations.Key]
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "webhook"; // webhook, slack, teams
        public string WebhookUrl { get; set; } = "";
        public string MinSeverity { get; set; } = "Critical"; // Info, Warning, Critical, Emergency
        public bool Enabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
