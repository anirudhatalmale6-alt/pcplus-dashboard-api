using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PCPlus.Dashboard.Services;

namespace PCPlus.Dashboard.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/reports")]
    public class ReportController : ControllerBase
    {
        private readonly ReportGenerator _reportGen;

        public ReportController(ReportGenerator reportGen) => _reportGen = reportGen;

        [HttpGet("company/{customerName}")]
        public async Task<ContentResult> CompanyReport(string customerName, [FromQuery] string? format = null)
        {
            customerName = WebUtility.UrlDecode(customerName);
            var html = await _reportGen.GenerateCompanyReportHtml(customerName);

            if (html == null)
                return new ContentResult
                {
                    Content = $"<html><body><p>No devices found for \"{WebUtility.HtmlEncode(customerName)}\"</p></body></html>",
                    ContentType = "text/html"
                };

            return new ContentResult { Content = html, ContentType = "text/html" };
        }
    }
}
