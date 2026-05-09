using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;

namespace PCPlus.Dashboard.Services
{
    public class EmailReportService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ReportGenerator _reportGenerator;
        private readonly ILogger<EmailReportService> _log;

        public EmailReportService(IServiceScopeFactory scopeFactory, ReportGenerator reportGenerator, ILogger<EmailReportService> log)
        {
            _scopeFactory = scopeFactory;
            _reportGenerator = reportGenerator;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Check every 5 minutes for due schedules
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndSendDueReports(stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error checking email schedules");
                }
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        private async Task CheckAndSendDueReports(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DashboardDb>();

            // Find due schedules
            var now = DateTime.UtcNow;
            var dueSchedules = await db.EmailSchedules
                .Where(s => s.Enabled && s.NextSendAt != null && s.NextSendAt <= now)
                .ToListAsync(ct);

            if (dueSchedules.Count == 0) return;

            // Load SMTP config
            var smtp = await db.SmtpConfigs.FirstOrDefaultAsync(ct);
            if (smtp == null || string.IsNullOrEmpty(smtp.Host))
            {
                _log.LogWarning("Email schedules due but no SMTP configured");
                return;
            }

            foreach (var schedule in dueSchedules)
            {
                try
                {
                    var html = await _reportGenerator.GenerateCompanyReportHtml(schedule.CustomerName, forEmail: true);
                    if (html == null)
                    {
                        _log.LogWarning("No devices for customer {Customer}, skipping", schedule.CustomerName);
                        continue;
                    }

                    // Send to each recipient
                    var recipients = schedule.RecipientEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var email in recipients)
                    {
                        await SendEmail(smtp, email, $"Security Report - {schedule.CustomerName}", html);
                    }

                    schedule.LastSentAt = now;
                    schedule.NextSendAt = ComputeNextSend(schedule);
                    _log.LogInformation("Report sent for {Customer} to {Recipients}", schedule.CustomerName, schedule.RecipientEmails);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to send report for {Customer}", schedule.CustomerName);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        private static async Task SendEmail(SmtpConfig config, string to, string subject, string htmlBody)
        {
            using var client = new SmtpClient(config.Host, config.Port)
            {
                Credentials = new NetworkCredential(config.Username, config.Password),
                EnableSsl = config.UseSsl
            };

            var msg = new MailMessage
            {
                From = new MailAddress(config.FromAddress, config.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(to);

            await client.SendMailAsync(msg);
        }

        // Compute next send datetime based on frequency
        private static DateTime ComputeNextSend(EmailSchedule schedule)
        {
            var last = schedule.LastSentAt ?? DateTime.UtcNow;
            DateTime next;
            switch (schedule.Frequency)
            {
                case "biweekly":
                    next = last.AddDays(14);
                    break;
                case "monthly":
                    next = last.AddMonths(1);
                    break;
                default: // weekly
                    next = last.AddDays(7);
                    break;
            }
            // Snap to configured day of week and hour
            while ((int)next.DayOfWeek != schedule.DayOfWeek)
                next = next.AddDays(1);
            next = new DateTime(next.Year, next.Month, next.Day, schedule.Hour, 0, 0, DateTimeKind.Utc);
            return next;
        }
    }
}
