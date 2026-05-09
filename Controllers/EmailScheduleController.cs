using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;
using PCPlus.Dashboard.Services;

namespace PCPlus.Dashboard.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/dashboard/email-schedules")]
    public class EmailScheduleController : ControllerBase
    {
        private readonly DashboardDb _db;
        private readonly ReportGenerator _reportGenerator;
        private readonly ILogger<EmailScheduleController> _log;

        public EmailScheduleController(DashboardDb db, ReportGenerator reportGenerator, ILogger<EmailScheduleController> log)
        {
            _db = db;
            _reportGenerator = reportGenerator;
            _log = log;
        }

        [HttpGet]
        public async Task<ActionResult<List<EmailSchedule>>> GetAll()
        {
            return Ok(await _db.EmailSchedules.OrderBy(s => s.CustomerName).ToListAsync());
        }

        [HttpPost]
        public async Task<ActionResult> Create([FromBody] EmailScheduleRequest req)
        {
            var schedule = new EmailSchedule
            {
                CustomerName = req.CustomerName,
                RecipientEmails = req.RecipientEmails,
                Frequency = req.Frequency,
                DayOfWeek = req.DayOfWeek,
                Hour = req.Hour,
                Enabled = true,
                CreatedBy = User.Identity?.Name ?? "admin",
                NextSendAt = ComputeFirstSend(req.Frequency, req.DayOfWeek, req.Hour)
            };
            _db.EmailSchedules.Add(schedule);
            await _db.SaveChangesAsync();
            return Ok(schedule);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult> Update(int id, [FromBody] EmailScheduleRequest req)
        {
            var schedule = await _db.EmailSchedules.FindAsync(id);
            if (schedule == null) return NotFound();

            schedule.CustomerName = req.CustomerName;
            schedule.RecipientEmails = req.RecipientEmails;
            schedule.Frequency = req.Frequency;
            schedule.DayOfWeek = req.DayOfWeek;
            schedule.Hour = req.Hour;
            schedule.NextSendAt = ComputeFirstSend(req.Frequency, req.DayOfWeek, req.Hour);
            await _db.SaveChangesAsync();
            return Ok(schedule);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var schedule = await _db.EmailSchedules.FindAsync(id);
            if (schedule == null) return NotFound();
            _db.EmailSchedules.Remove(schedule);
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("{id}/toggle")]
        public async Task<ActionResult> Toggle(int id)
        {
            var schedule = await _db.EmailSchedules.FindAsync(id);
            if (schedule == null) return NotFound();
            schedule.Enabled = !schedule.Enabled;
            if (schedule.Enabled && schedule.NextSendAt == null)
                schedule.NextSendAt = ComputeFirstSend(schedule.Frequency, schedule.DayOfWeek, schedule.Hour);
            await _db.SaveChangesAsync();
            return Ok(schedule);
        }

        [HttpPost("{id}/send-now")]
        public async Task<ActionResult> SendNow(int id)
        {
            var schedule = await _db.EmailSchedules.FindAsync(id);
            if (schedule == null) return NotFound();

            var smtp = await _db.SmtpConfigs.FirstOrDefaultAsync();
            if (smtp == null || string.IsNullOrEmpty(smtp.Host))
                return BadRequest(new { error = "SMTP not configured" });

            var html = await _reportGenerator.GenerateCompanyReportHtml(schedule.CustomerName, forEmail: true);
            if (html == null)
                return BadRequest(new { error = "No devices found for this customer" });

            var recipients = schedule.RecipientEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var email in recipients)
            {
                using var client = new SmtpClient(smtp.Host, smtp.Port)
                {
                    Credentials = new NetworkCredential(smtp.Username, smtp.Password),
                    EnableSsl = smtp.UseSsl
                };
                var msg = new MailMessage
                {
                    From = new MailAddress(smtp.FromAddress, smtp.FromName),
                    Subject = $"Security Report - {schedule.CustomerName}",
                    Body = html,
                    IsBodyHtml = true
                };
                msg.To.Add(email);
                await client.SendMailAsync(msg);
            }

            schedule.LastSentAt = DateTime.UtcNow;
            schedule.NextSendAt = ComputeFirstSend(schedule.Frequency, schedule.DayOfWeek, schedule.Hour);
            await _db.SaveChangesAsync();

            return Ok(new { sent = true, recipients = recipients.Length });
        }

        // SMTP config endpoints
        [HttpGet("smtp")]
        public async Task<ActionResult> GetSmtp()
        {
            var smtp = await _db.SmtpConfigs.FirstOrDefaultAsync();
            if (smtp == null) return Ok(new SmtpConfig());
            // Don't send password back in clear
            return Ok(new { smtp.Id, smtp.Host, smtp.Port, smtp.Username, password = string.IsNullOrEmpty(smtp.Password) ? "" : "********", smtp.FromAddress, smtp.FromName, smtp.UseSsl });
        }

        [HttpPost("smtp")]
        public async Task<ActionResult> SaveSmtp([FromBody] SmtpConfigRequest req)
        {
            var smtp = await _db.SmtpConfigs.FirstOrDefaultAsync();
            if (smtp == null)
            {
                smtp = new SmtpConfig();
                _db.SmtpConfigs.Add(smtp);
            }
            smtp.Host = req.Host;
            smtp.Port = req.Port;
            smtp.Username = req.Username;
            if (req.Password != "********")
                smtp.Password = req.Password;
            smtp.FromAddress = req.FromAddress;
            smtp.FromName = req.FromName;
            smtp.UseSsl = req.UseSsl;
            await _db.SaveChangesAsync();
            return Ok(new { saved = true });
        }

        [HttpPost("smtp/test")]
        public async Task<ActionResult> TestSmtp([FromBody] SmtpConfigRequest req)
        {
            try
            {
                // Use saved password if placeholder sent
                var password = req.Password;
                if (password == "********")
                {
                    var existing = await _db.SmtpConfigs.FirstOrDefaultAsync();
                    password = existing?.Password ?? "";
                }

                using var client = new SmtpClient(req.Host, req.Port)
                {
                    Credentials = new NetworkCredential(req.Username, password),
                    EnableSsl = req.UseSsl,
                    Timeout = 10000
                };
                var msg = new MailMessage
                {
                    From = new MailAddress(req.FromAddress, req.FromName),
                    Subject = "PC Plus Computing - SMTP Test",
                    Body = "This is a test email from your PC Plus Endpoint Protection Dashboard. SMTP is working correctly!",
                    IsBodyHtml = false
                };
                msg.To.Add(req.FromAddress); // Send test to self
                await client.SendMailAsync(msg);
                return Ok(new { success = true, message = "Test email sent successfully" });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, message = ex.Message });
            }
        }

        private static DateTime ComputeFirstSend(string frequency, int dayOfWeek, int hour)
        {
            var now = DateTime.UtcNow;
            var next = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0, DateTimeKind.Utc);
            if (next <= now) next = next.AddDays(1);
            while ((int)next.DayOfWeek != dayOfWeek)
                next = next.AddDays(1);
            return next;
        }
    }

    public class EmailScheduleRequest
    {
        public string CustomerName { get; set; } = "";
        public string RecipientEmails { get; set; } = "";
        public string Frequency { get; set; } = "weekly";
        public int DayOfWeek { get; set; } = 1;
        public int Hour { get; set; } = 8;
    }

    public class SmtpConfigRequest
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = "PC Plus Computing";
        public bool UseSsl { get; set; } = true;
    }
}
