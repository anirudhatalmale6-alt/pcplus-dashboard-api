using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Models;

namespace PCPlus.Dashboard.Services
{
    public class ReportGenerator
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ReportGenerator(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<string?> GenerateCompanyReportHtml(string customerName, bool forEmail = false)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DashboardDb>();

            var devices = await db.Devices
                .Where(d => d.CustomerName == customerName)
                .OrderBy(d => d.SecurityScore)
                .ToListAsync();

            if (devices.Count == 0) return null;

            var deviceData = devices.Select(d =>
            {
                var checks = new List<SecurityCheckReport>();
                try
                {
                    if (!string.IsNullOrEmpty(d.SecurityChecksJson) && d.SecurityChecksJson != "[]")
                        checks = JsonSerializer.Deserialize<List<SecurityCheckReport>>(d.SecurityChecksJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
                catch { }
                return new { Device = d, Checks = checks };
            }).ToList();

            int totalDevices = deviceData.Count;
            int online = deviceData.Count(d => d.Device.IsOnline);
            int offline = totalDevices - online;
            int avgScore = totalDevices > 0
                ? (int)Math.Round(deviceData.Average(d => d.Device.SecurityScore))
                : 0;
            int totalChecks = deviceData.Sum(d => d.Checks.Count);
            int totalPassed = deviceData.Sum(d => d.Checks.Count(c => c.Passed));
            int totalFailed = totalChecks - totalPassed;
            int protectedCount = deviceData.Count(d => d.Device.RunningModules > 0);
            int passRate = totalChecks > 0 ? (int)Math.Round(100.0 * totalPassed / totalChecks) : 0;
            string grade = avgScore >= 90 ? "A" : avgScore >= 80 ? "B" : avgScore >= 70 ? "C" : avgScore >= 60 ? "D" : avgScore >= 50 ? "E" : "F";
            string gradeWord = avgScore >= 90 ? "Excellent" : avgScore >= 80 ? "Good" : avgScore >= 70 ? "Fair" : avgScore >= 60 ? "Needs Improvement" : avgScore >= 50 ? "Poor" : "Critical";
            string scoreColor = avgScore >= 80 ? "#00e676" : avgScore >= 60 ? "#ffa726" : "#ef5350";
            string riskLevel = avgScore >= 80 ? "LOW" : avgScore >= 60 ? "MEDIUM" : "HIGH";
            string riskColor = avgScore >= 80 ? "#00e676" : avgScore >= 60 ? "#ffa726" : "#ef5350";

            var issueMap = new Dictionary<string, (string Name, string Category, string Rec, int Weight, List<string> Devices)>();
            foreach (var dd in deviceData)
                foreach (var c in dd.Checks.Where(c => !c.Passed))
                {
                    if (!issueMap.ContainsKey(c.Id))
                        issueMap[c.Id] = (c.Name, c.Category, c.Recommendation, c.Weight, new List<string>());
                    issueMap[c.Id].Devices.Add(dd.Device.Hostname);
                }
            var topIssues = issueMap.Values.OrderByDescending(i => i.Devices.Count).ThenByDescending(i => i.Weight).ToList();

            var catMap = new Dictionary<string, (int Total, int Passed)>();
            foreach (var dd in deviceData)
                foreach (var c in dd.Checks)
                {
                    var cat = string.IsNullOrEmpty(c.Category) ? "Other" : c.Category;
                    if (!catMap.ContainsKey(cat)) catMap[cat] = (0, 0);
                    var cur = catMap[cat];
                    catMap[cat] = (cur.Total + 1, cur.Passed + (c.Passed ? 1 : 0));
                }
            var catEntries = catMap.OrderBy(kv => (double)kv.Value.Passed / kv.Value.Total).ToList();

            int highRisk = topIssues.Count(i => i.Weight >= 10);
            int medRisk = topIssues.Count(i => i.Weight >= 5 && i.Weight < 10);
            int lowRisk = topIssues.Count(i => i.Weight > 0 && i.Weight < 5);
            int projectedScore = Math.Min(100, avgScore + (int)(totalFailed * 0.8));
            int riskReduction = totalFailed > 0 ? (int)Math.Round((double)(projectedScore - avgScore) / (100 - avgScore) * 100) : 0;

            var now = DateTime.Now;
            var reportId = "CR-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString("X");

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"en\"><head>");
            sb.Append("<meta charset=\"UTF-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.Append($"<title>Endpoint Protection Audit - {Esc(customerName)}</title>");
            sb.Append("<style>");
            sb.Append(GetCss());
            sb.Append("</style></head><body>");

            if (!forEmail)
            {
                sb.Append(@"<div class=""action-bar no-print"">
                    <button onclick=""window.print()"">Save as PDF</button>
                    <a class=""btn-dl"" id=""dl-btn"">Download HTML</a>
                    <button class=""btn-gray"" onclick=""window.close()"">Close</button>
                </div>");
            }

            // ==================================================================
            // PAGE 1: EXECUTIVE SUMMARY - Overall Score & Overview
            // ==================================================================
            sb.Append(@"<div class=""exec-page"">");
            sb.Append(ExecHeader(customerName, now, reportId, "PAGE 1 OF 4"));
            sb.Append(@"<div class=""exec-page-title"">EXECUTIVE SUMMARY</div>");

            sb.Append(@"<div class=""p1-layout"">");
            // Left: Score donut + Grade
            sb.Append($@"<div class=""p1-score-section"">
                <div class=""p1-score-label"">OVERALL SECURITY SCORE</div>
                <div class=""p1-score-donut-wrap"">
                    {DarkDonut(avgScore, 100, scoreColor, 200, $"{avgScore}", "/100")}
                </div>
                <div class=""p1-grade-row"">
                    <div class=""p1-grade-badge"" style=""background:{scoreColor}18;border:2px solid {scoreColor};color:{scoreColor}"">{grade}</div>
                    <div class=""p1-grade-word"" style=""color:{scoreColor}"">{gradeWord}</div>
                </div>
                <div class=""p1-risk-badge"" style=""background:{riskColor}20;border:2px solid {riskColor};color:{riskColor}"">
                    Risk Level: {riskLevel}
                </div>
            </div>");

            // Right: Total checks + breakdown cards
            int needsAttention = medRisk + lowRisk;
            sb.Append($@"<div class=""p1-stats-section"">
                <div class=""p1-total-checks"">
                    <div class=""p1-total-num"">{totalChecks}</div>
                    <div class=""p1-total-label"">TOTAL CHECKS</div>
                    <div class=""p1-total-sub"">Across {catEntries.Count} Critical Security Areas</div>
                </div>
                <div class=""p1-stat-grid"">
                    <div class=""p1-mini-stat passed"">
                        <div class=""p1-mini-num"">{totalPassed}</div>
                        <div class=""p1-mini-label"">Passed ({passRate}%)</div>
                    </div>
                    <div class=""p1-mini-stat failed"">
                        <div class=""p1-mini-num"">{totalFailed}</div>
                        <div class=""p1-mini-label"">Failed</div>
                    </div>
                </div>
                <div class=""p1-stat-grid"">
                    <div class=""p1-mini-stat attention"">
                        <div class=""p1-mini-num"">{needsAttention}</div>
                        <div class=""p1-mini-label"">Needs Attention</div>
                    </div>
                    <div class=""p1-mini-stat highrisk"">
                        <div class=""p1-mini-num"">{highRisk}</div>
                        <div class=""p1-mini-label"">High Risk</div>
                    </div>
                </div>
            </div>");
            sb.Append("</div>");

            // TOP 4 PRIORITIES - critical items that need fixing
            var top4 = topIssues.Take(4).ToList();
            if (top4.Count > 0)
            {
                string[] priorityColors = { "#ef5350", "#ff7043", "#ffa726", "#ffca28" };
                string[] priorityIcons = {
                    @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""{0}"" stroke-width=""2"" width=""28"" height=""28""><path d=""M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z""/><line x1=""12"" y1=""9"" x2=""12"" y2=""13""/><line x1=""12"" y1=""17"" x2=""12.01"" y2=""17""/></svg>",
                    @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""{0}"" stroke-width=""2"" width=""28"" height=""28""><path d=""M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z""/><line x1=""12"" y1=""8"" x2=""12"" y2=""12""/><line x1=""12"" y1=""16"" x2=""12.01"" y2=""16""/></svg>",
                    @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""{0}"" stroke-width=""2"" width=""28"" height=""28""><circle cx=""12"" cy=""12"" r=""10""/><line x1=""12"" y1=""8"" x2=""12"" y2=""12""/><line x1=""12"" y1=""16"" x2=""12.01"" y2=""16""/></svg>",
                    @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""{0}"" stroke-width=""2"" width=""28"" height=""28""><path d=""M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z""/><path d=""M14 2v6h6""/><line x1=""12"" y1=""18"" x2=""12"" y2=""12""/><polyline points=""9 15 12 12 15 15""/></svg>"
                };
                sb.Append(@"<div class=""p1-priorities-title"">TOP PRIORITIES - IMMEDIATE ACTION REQUIRED</div>");
                sb.Append(@"<div class=""p1-priorities"">");
                for (int i = 0; i < top4.Count; i++)
                {
                    var issue = top4[i];
                    string color = priorityColors[i];
                    string icon = string.Format(priorityIcons[i], color);
                    string severity = issue.Weight >= 10 ? "CRITICAL" : issue.Weight >= 5 ? "HIGH" : "MEDIUM";
                    sb.Append($@"<div class=""p1-pri-item"" style=""border-left:4px solid {color}"">
                        <div class=""p1-pri-icon"">{icon}</div>
                        <div class=""p1-pri-content"">
                            <div class=""p1-pri-name"">{Esc(issue.Name)}</div>
                            <div class=""p1-pri-detail"">{Esc(issue.Rec)}</div>
                        </div>
                        <div class=""p1-pri-badge"" style=""background:{color}20;color:{color};border:1px solid {color}"">{severity}</div>
                    </div>");
                }
                sb.Append("</div>");
            }

            // COVERAGE STATUS - Firewall, Backup, DR
            sb.Append(@"<div class=""p1-coverage"">
                <div class=""p1-cov-item"">
                    <div class=""p1-cov-icon"" style=""border-color:#42a5f5"">
                        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#42a5f5"" stroke-width=""2"" width=""30"" height=""30""><rect x=""3"" y=""3"" width=""18"" height=""18"" rx=""2""/><path d=""M3 9h18""/><path d=""M9 21V9""/></svg>
                    </div>
                    <div class=""p1-cov-label"">FIREWALL</div>
                    <div class=""p1-cov-desc"">External threats covered with perimeter firewall protection</div>
                </div>
                <div class=""p1-cov-item"">
                    <div class=""p1-cov-icon"" style=""border-color:#00e676"">
                        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#00e676"" stroke-width=""2"" width=""30"" height=""30""><path d=""M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4""/><polyline points=""17 8 12 3 7 8""/><line x1=""12"" y1=""3"" x2=""12"" y2=""15""/></svg>
                    </div>
                    <div class=""p1-cov-label"">BACKUP 3-2-1</div>
                    <div class=""p1-cov-desc"">Data protected with 3 copies, 2 media types, 1 offsite</div>
                </div>
                <div class=""p1-cov-item"">
                    <div class=""p1-cov-icon"" style=""border-color:#ab47bc"">
                        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#ab47bc"" stroke-width=""2"" width=""30"" height=""30""><path d=""M3 12a9 9 0 019-9 9.75 9.75 0 016.74 2.74L21 8""/><path d=""M21 3v5h-5""/><path d=""M21 12a9 9 0 01-9 9 9.75 9.75 0 01-6.74-2.74L3 16""/><path d=""M3 21v-5h5""/></svg>
                    </div>
                    <div class=""p1-cov-label"">DISASTER RECOVERY</div>
                    <div class=""p1-cov-desc"">Rapid VM spinup and bare metal recovery for business continuity</div>
                </div>
            </div>");

            sb.Append(ExecFooter());
            sb.Append("</div>");

            // ==================================================================
            // PAGE 2: 120-POINT AUDIT SUMMARY BY CATEGORY
            // ==================================================================
            sb.Append(@"<div class=""exec-page page-break"">");
            sb.Append(ExecHeader(customerName, now, reportId, "PAGE 2 OF 4"));
            sb.Append(@"<div class=""exec-page-title"">SECURITY AUDIT SUMMARY BY CATEGORY</div>");

            sb.Append(@"<div class=""p2-layout"">");
            // Category table
            sb.Append(@"<div class=""p2-table-section"">
                <table class=""p2-cat-table"">
                <thead><tr>
                    <th>Category</th><th>Points</th><th>Score</th><th>Risk Level</th>
                </tr></thead><tbody>");

            foreach (var (cat, data) in catEntries)
            {
                int pct = (int)Math.Round(100.0 * data.Passed / data.Total);
                string catRisk = pct >= 80 ? "Low Risk" : pct >= 60 ? "Medium Risk" : "High Risk";
                string catRiskColor = pct >= 80 ? "#00e676" : pct >= 60 ? "#ffa726" : "#ef5350";
                string catRiskBg = pct >= 80 ? "#00e67620" : pct >= 60 ? "#ffa72620" : "#ef535020";
                sb.Append($@"<tr>
                    <td class=""p2-cat-name"">{Esc(cat)}</td>
                    <td class=""p2-cat-pts"">{data.Total}</td>
                    <td class=""p2-cat-score"" style=""color:{catRiskColor}"">{pct}%</td>
                    <td><span class=""p2-risk-tag"" style=""background:{catRiskBg};color:{catRiskColor};border:1px solid {catRiskColor}"">{catRisk}</span></td>
                </tr>");
            }
            sb.Append($@"<tr class=""p2-total-row"">
                <td><strong>TOTAL</strong></td>
                <td><strong>{totalChecks}</strong></td>
                <td style=""color:{scoreColor}""><strong>{passRate}%</strong></td>
                <td><span class=""p2-risk-tag"" style=""background:{riskColor}20;color:{riskColor};border:1px solid {riskColor}"">{riskLevel}</span></td>
            </tr>");
            sb.Append("</tbody></table></div>");

            // Right side: Risk distribution + top priorities
            sb.Append($@"<div class=""p2-side-section"">
                <div class=""p2-risk-dist"">
                    <div class=""p2-section-head"">Risk Distribution</div>
                    {DarkDonut(totalPassed, totalChecks, "#00e676", 130, $"{totalChecks}", "Total")}
                    <div class=""p2-risk-legend"">
                        <div class=""p2-legend-item""><span class=""p2-legend-dot"" style=""background:#ef5350""></span> High Risk: {highRisk} ({(totalChecks > 0 ? (int)Math.Round(100.0 * highRisk / totalChecks) : 0)}%)</div>
                        <div class=""p2-legend-item""><span class=""p2-legend-dot"" style=""background:#ffa726""></span> Medium Risk: {medRisk} ({(totalChecks > 0 ? (int)Math.Round(100.0 * medRisk / totalChecks) : 0)}%)</div>
                        <div class=""p2-legend-item""><span class=""p2-legend-dot"" style=""background:#00e676""></span> Low Risk / Passed: {totalPassed} ({passRate}%)</div>
                    </div>
                </div>

                <div class=""p2-top-risks"">
                    <div class=""p2-section-head"">Top Priority Risks</div>");
            foreach (var issue in topIssues.Take(5))
            {
                string priColor = issue.Weight >= 10 ? "#ef5350" : issue.Weight >= 5 ? "#ffa726" : "#42a5f5";
                string priLabel = issue.Weight >= 10 ? "Critical" : issue.Weight >= 5 ? "High" : "Medium";
                sb.Append($@"<div class=""p2-risk-item"">
                    <div class=""p2-risk-name"">{Esc(issue.Name)}</div>
                    <span class=""p2-risk-pri"" style=""background:{priColor}20;color:{priColor};border:1px solid {priColor}"">{priLabel}</span>
                </div>");
            }
            sb.Append("</div></div>");
            sb.Append("</div>");

            sb.Append(ExecFooter());
            sb.Append("</div>");

            // ==================================================================
            // PAGE 3: INSIGHTS, IMPACT & RECOMMENDATIONS
            // ==================================================================
            sb.Append(@"<div class=""exec-page page-break"">");
            sb.Append(ExecHeader(customerName, now, reportId, "PAGE 3 OF 4"));
            sb.Append(@"<div class=""exec-page-title"">INSIGHTS, IMPACT &amp; RECOMMENDATIONS</div>");

            // Before vs After
            sb.Append($@"<div class=""p3-bva-section"">
                <div class=""p3-bva-card"">
                    <div class=""p3-bva-label"">Current Score</div>
                    {DarkDonut(avgScore, 100, riskColor, 110, $"{avgScore}%", "")}
                    <div class=""p3-bva-status"" style=""color:{riskColor}"">{riskLevel} Risk</div>
                </div>
                <div class=""p3-bva-arrow"">
                    <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#42a5f5"" stroke-width=""2"" width=""40"" height=""40""><path d=""M5 12h14m-7-7l7 7-7 7""/></svg>
                </div>
                <div class=""p3-bva-card"">
                    <div class=""p3-bva-label"">Projected Score</div>
                    {DarkDonut(projectedScore, 100, "#00e676", 110, $"{projectedScore}%", "")}
                    <div class=""p3-bva-status"" style=""color:#00e676"">Low Risk</div>
                </div>
                <div class=""p3-bva-stat"">
                    <div class=""p3-bva-big"" style=""color:#00e676"">+{projectedScore - avgScore}%</div>
                    <div class=""p3-bva-sublabel"">Potential Improvement</div>
                </div>
            </div>");

            // Business Impact
            sb.Append(@"<div class=""p3-impact"">
                <div class=""p3-section-head"">Business Impact</div>
                <div class=""p3-impact-grid"">
                    <div class=""p3-impact-item"">
                        <div class=""p3-impact-icon"" style=""color:#42a5f5"">
                            <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" width=""24"" height=""24""><circle cx=""12"" cy=""12"" r=""10""/><path d=""M12 6v6l4 2""/></svg>
                        </div>
                        <div class=""p3-impact-title"">Reduce Downtime</div>
                        <div class=""p3-impact-desc"">Prevent costly outages and disruptions</div>
                    </div>
                    <div class=""p3-impact-item"">
                        <div class=""p3-impact-icon"" style=""color:#00e676"">
                            <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" width=""24"" height=""24""><path d=""M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z""/></svg>
                        </div>
                        <div class=""p3-impact-title"">Protect Data</div>
                        <div class=""p3-impact-desc"">Safeguard business data and reputation</div>
                    </div>
                    <div class=""p3-impact-item"">
                        <div class=""p3-impact-icon"" style=""color:#ffa726"">
                            <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" width=""24"" height=""24""><path d=""M9 11l3 3L22 4""/><path d=""M21 12v7a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h11""/></svg>
                        </div>
                        <div class=""p3-impact-title"">Ensure Compliance</div>
                        <div class=""p3-impact-desc"">Meet industry security standards</div>
                    </div>
                    <div class=""p3-impact-item"">
                        <div class=""p3-impact-icon"" style=""color:#ab47bc"">
                            <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" width=""24"" height=""24""><line x1=""12"" y1=""1"" x2=""12"" y2=""23""/><path d=""M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6""/></svg>
                        </div>
                        <div class=""p3-impact-title"">Lower Costs</div>
                        <div class=""p3-impact-desc"">Reduce breach-related expenses</div>
                    </div>
                </div>
            </div>");

            // Top 5 Priority Actions
            sb.Append(@"<div class=""p3-actions"">
                <div class=""p3-section-head"">Top 5 Priority Actions</div>
                <div class=""p3-action-list"">");
            var topActions = topIssues.Take(5).ToList();
            string[] actionColors = { "#ef5350", "#ffa726", "#42a5f5", "#00e676", "#ab47bc" };
            for (int i = 0; i < topActions.Count; i++)
            {
                var a = topActions[i];
                string ac = actionColors[i % actionColors.Length];
                sb.Append($@"<div class=""p3-action-item"">
                    <div class=""p3-action-num"" style=""background:{ac}"">{i + 1}</div>
                    <div class=""p3-action-body"">
                        <div class=""p3-action-name"">{Esc(a.Name)}</div>
                        <div class=""p3-action-rec"">{Esc(a.Rec)}</div>
                    </div>
                    <div class=""p3-action-devices"">{a.Devices.Count} device{(a.Devices.Count != 1 ? "s" : "")}</div>
                </div>");
            }
            sb.Append("</div></div>");

            // Next Steps
            sb.Append(@"<div class=""p3-next-steps"">
                <div class=""p3-step"">
                    <div class=""p3-step-icon"" style=""background:#42a5f520;border-color:#42a5f5"">
                        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#42a5f5"" stroke-width=""2"" width=""20"" height=""20""><circle cx=""11"" cy=""11"" r=""8""/><path d=""M21 21l-4.35-4.35""/></svg>
                    </div>
                    <div class=""p3-step-label"">ASSESS</div>
                </div>
                <div class=""p3-step-arrow"">&#8594;</div>
                <div class=""p3-step"">
                    <div class=""p3-step-icon"" style=""background:#00e67620;border-color:#00e676"">
                        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#00e676"" stroke-width=""2"" width=""20"" height=""20""><path d=""M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z""/></svg>
                    </div>
                    <div class=""p3-step-label"">PROTECT</div>
                </div>
                <div class=""p3-step-arrow"">&#8594;</div>
                <div class=""p3-step"">
                    <div class=""p3-step-icon"" style=""background:#ffa72620;border-color:#ffa726"">
                        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#ffa726"" stroke-width=""2"" width=""20"" height=""20""><path d=""M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z""/><circle cx=""12"" cy=""12"" r=""3""/></svg>
                    </div>
                    <div class=""p3-step-label"">MONITOR</div>
                </div>
                <div class=""p3-step-arrow"">&#8594;</div>
                <div class=""p3-step"">
                    <div class=""p3-step-icon"" style=""background:#ab47bc20;border-color:#ab47bc"">
                        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#ab47bc"" stroke-width=""2"" width=""20"" height=""20""><path d=""M22 12h-4l-3 9L9 3l-3 9H2""/></svg>
                    </div>
                    <div class=""p3-step-label"">IMPROVE</div>
                </div>
            </div>");

            sb.Append(ExecFooter());
            sb.Append("</div>");

            // ==================================================================
            // PAGE 4: DETAILED RISK OVERVIEW & REMEDIATION ROADMAP
            // ==================================================================
            sb.Append(@"<div class=""exec-page page-break"">");
            sb.Append(ExecHeader(customerName, now, reportId, "PAGE 4 OF 4"));
            sb.Append(@"<div class=""exec-page-title"">DETAILED RISK OVERVIEW &amp; REMEDIATION ROADMAP</div>");

            sb.Append(@"<div class=""p4-layout"">");
            // Risk table
            sb.Append(@"<div class=""p4-table-section"">
                <table class=""p4-risk-table"">
                <thead><tr>
                    <th>Risk Level</th><th>Areas of Concern</th><th>Business Impact</th><th>Recommended Actions</th>
                </tr></thead><tbody>");

            var highRiskIssues = topIssues.Where(i => i.Weight >= 10).Take(4).ToList();
            var medRiskIssues = topIssues.Where(i => i.Weight >= 5 && i.Weight < 10).Take(3).ToList();
            var lowRiskIssues = topIssues.Where(i => i.Weight < 5).Take(2).ToList();

            if (highRiskIssues.Count > 0)
            {
                bool first = true;
                foreach (var hi in highRiskIssues)
                {
                    string impact = hi.Category switch
                    {
                        "Network" => "Increased risk of unauthorized access",
                        "Ransomware Protection" => "High risk of data loss and downtime",
                        "Updates" => "Vulnerabilities exploited by attackers",
                        "Identity & Access" or "Access" => "Potential for breaches and escalation",
                        _ => "Increased security exposure"
                    };
                    sb.Append($@"<tr>
                        {(first ? $@"<td rowspan=""{highRiskIssues.Count}"" class=""p4-risk-cell high""><span class=""p4-risk-dot high""></span>HIGH RISK</td>" : "")}
                        <td>{Esc(hi.Name)}<br><span class=""p4-cat-sub"">{Esc(hi.Category)}</span></td>
                        <td class=""p4-impact"">{impact}</td>
                        <td class=""p4-action"">{Esc(hi.Rec)}</td>
                    </tr>");
                    first = false;
                }
            }

            if (medRiskIssues.Count > 0)
            {
                bool first = true;
                foreach (var mi in medRiskIssues)
                {
                    string impact = mi.Category switch
                    {
                        "Data Protection" => "Data confidentiality at risk",
                        "Logging & Visibility" => "Delayed threat detection",
                        "Endpoint Hardening" => "Increased attack surface",
                        _ => "Moderate security exposure"
                    };
                    sb.Append($@"<tr>
                        {(first ? $@"<td rowspan=""{medRiskIssues.Count}"" class=""p4-risk-cell medium""><span class=""p4-risk-dot medium""></span>MEDIUM RISK</td>" : "")}
                        <td>{Esc(mi.Name)}<br><span class=""p4-cat-sub"">{Esc(mi.Category)}</span></td>
                        <td class=""p4-impact"">{impact}</td>
                        <td class=""p4-action"">{Esc(mi.Rec)}</td>
                    </tr>");
                    first = false;
                }
            }

            if (lowRiskIssues.Count > 0)
            {
                bool first = true;
                foreach (var li in lowRiskIssues)
                {
                    sb.Append($@"<tr>
                        {(first ? $@"<td rowspan=""{lowRiskIssues.Count}"" class=""p4-risk-cell low""><span class=""p4-risk-dot low""></span>LOW RISK</td>" : "")}
                        <td>{Esc(li.Name)}<br><span class=""p4-cat-sub"">{Esc(li.Category)}</span></td>
                        <td class=""p4-impact"">Low risk, potential data leakage</td>
                        <td class=""p4-action"">{Esc(li.Rec)}</td>
                    </tr>");
                    first = false;
                }
            }
            sb.Append("</tbody></table></div>");

            // Remediation roadmap
            sb.Append($@"<div class=""p4-roadmap-section"">
                <div class=""p4-roadmap-title"">REMEDIATION ROADMAP</div>
                <div class=""p4-roadmap-item"">
                    <div class=""p4-rm-badge"" style=""background:#ef535030;border-color:#ef5350;color:#ef5350"">SHORT TERM</div>
                    <div class=""p4-rm-time"">0-30 DAYS</div>
                    <ul class=""p4-rm-list"">
                        <li>Address high risk issues</li>
                        <li>Close critical security gaps</li>
                        <li>Enable missing protections</li>
                    </ul>
                </div>
                <div class=""p4-roadmap-item"">
                    <div class=""p4-rm-badge"" style=""background:#ffa72630;border-color:#ffa726;color:#ffa726"">MID TERM</div>
                    <div class=""p4-rm-time"">31-90 DAYS</div>
                    <ul class=""p4-rm-list"">
                        <li>Strengthen security posture</li>
                        <li>Improve visibility and monitoring</li>
                        <li>Implement backup solutions</li>
                    </ul>
                </div>
                <div class=""p4-roadmap-item"">
                    <div class=""p4-rm-badge"" style=""background:#00e67630;border-color:#00e676;color:#00e676"">LONG TERM</div>
                    <div class=""p4-rm-time"">90+ DAYS</div>
                    <ul class=""p4-rm-list"">
                        <li>Optimize security controls</li>
                        <li>Continuous improvement</li>
                        <li>Advanced threat protection</li>
                    </ul>
                </div>

                <div class=""p4-promise"">
                    <div class=""p4-promise-title"">THE PC PLUS PROMISE</div>
                    <div class=""p4-promise-text"">We don't just find problems. We provide solutions that protect your business, empower your team, and secure your future.</div>
                </div>
            </div>");
            sb.Append("</div>");

            sb.Append(ExecFooter());
            sb.Append("</div>");

            // ==================================================================
            // DETAILED REPORT PAGES (kept for technician view)
            // ==================================================================
            sb.Append(@"<div class=""detail-section page-break"">");
            sb.Append(@"<div class=""section-title"">Device Security Overview</div>
                <div class=""table-wrap""><table class=""device-table""><thead><tr>
                    <th>Device</th><th>OS</th><th>Grade</th><th>Score</th>
                    <th>Passed</th><th>Failed</th><th>CPU</th><th>RAM</th><th>Disk</th><th>Status</th>
                </tr></thead><tbody>");

            foreach (var dd in deviceData)
            {
                var d = dd.Device;
                int passed = dd.Checks.Count(c => c.Passed);
                int failed = dd.Checks.Count(c => !c.Passed);
                string g = (d.SecurityGrade ?? "F").ToUpper();
                string gc = g switch { "A" => "grade-a", "B" => "grade-b", "C" => "grade-c", "D" => "grade-d", _ => "grade-f" };
                string statusColor = d.IsOnline ? "#16a34a" : "#dc2626";
                sb.Append($@"<tr>
                    <td style=""font-weight:600"">{Esc(d.Hostname)}</td>
                    <td class=""os-col"">{Esc(d.OsVersion)}</td>
                    <td><span class=""grade-badge {gc}"">{g}</span></td>
                    <td style=""font-weight:600"">{d.SecurityScore}/100</td>
                    <td class=""pass"">{passed}</td><td class=""fail"">{failed}</td>
                    <td>{Math.Round(d.CpuPercent)}%</td><td>{Math.Round(d.RamPercent)}%</td><td>{Math.Round(d.DiskPercent)}%</td>
                    <td style=""color:{statusColor};font-weight:600"">{(d.IsOnline ? "Online" : "Offline")}</td>
                </tr>");
            }
            sb.Append("</tbody></table></div>");

            sb.Append(@"<div class=""section-title"" style=""margin-top:30px"">Priority Recommendations</div>
                <div class=""table-wrap""><table class=""device-table""><thead><tr>
                    <th style=""width:30px"">#</th><th>Issue</th><th>Affected Devices</th><th>Priority</th><th>Recommendation</th>
                </tr></thead><tbody>");
            int recNum = 0;
            foreach (var issue in topIssues.Take(15))
            {
                recNum++;
                string priority = issue.Weight >= 10 ? "<span style=\"color:#dc2626;font-weight:600\">HIGH</span>"
                    : issue.Weight >= 5 ? "<span style=\"color:#d97706;font-weight:600\">MEDIUM</span>"
                    : "<span style=\"color:#666\">LOW</span>";
                sb.Append($@"<tr>
                    <td style=""font-weight:600"">{recNum}</td>
                    <td class=""fail"" style=""font-weight:600"">{Esc(issue.Name)}</td>
                    <td class=""os-col"">{Esc(string.Join(", ", issue.Devices))}</td>
                    <td>{priority}</td>
                    <td style=""font-size:12px"">{Esc(issue.Rec)}</td>
                </tr>");
            }
            sb.Append("</tbody></table></div></div>");

            // Per-device breakdown
            sb.Append(@"<div class=""detail-section page-break""><div class=""section-title"">Per-Device Security Breakdown</div>");
            foreach (var dd in deviceData)
            {
                var d = dd.Device;
                var failed = dd.Checks.Where(c => !c.Passed).ToList();
                sb.Append($@"<div class=""device-block"">
                    <div class=""device-block-header"">
                        <span>{Esc(d.Hostname)}{(string.IsNullOrEmpty(d.OsVersion) ? "" : " - " + Esc(d.OsVersion))}</span>
                        <span>Score: {d.SecurityScore}/100 ({d.SecurityGrade?.ToUpper() ?? "?"}) | {failed.Count} issue{(failed.Count != 1 ? "s" : "")}</span>
                    </div>");
                if (failed.Count == 0)
                    sb.Append($@"<div class=""device-all-pass"">All {dd.Checks.Count} security checks passed</div>");
                else
                {
                    sb.Append(@"<table><tr><th>Check</th><th>Category</th><th>Details</th><th style=""width:50px"">Weight</th><th style=""width:120px"">Last Validated</th></tr>");
                    foreach (var c in failed)
                        sb.Append($@"<tr>
                            <td class=""fail"" style=""font-weight:600"">{Esc(c.Name)}</td>
                            <td>{Esc(c.Category)}</td>
                            <td>{Esc(c.Detail)}</td>
                            <td style=""text-align:center;font-weight:600"">{c.Weight}</td>
                            <td style=""font-size:11px;color:#888"">{(c.LastChecked.HasValue ? c.LastChecked.Value.ToString("yyyy-MM-dd HH:mm") : "-")}</td>
                        </tr>");
                    sb.Append("</table>");
                }
                sb.Append("</div>");
            }
            sb.Append("</div>");

            sb.Append(@"<div class=""footer"">
                <p>Prepared by PC Plus Computing - Endpoint Protection Platform</p>
                <p>This report reflects the state of all endpoints at the time of generation.</p>
                <p style=""margin-top:6px;color:#bbb"">www.pcpluscomputing.com | Managed IT Services &amp; Security</p>
            </div>");

            if (!forEmail)
            {
                sb.Append(@"<script>
                document.getElementById('dl-btn').addEventListener('click', function() {
                    var html = document.documentElement.outerHTML;
                    var blob = new Blob([html], {type: 'text/html'});
                    var a = document.createElement('a');
                    a.href = URL.createObjectURL(blob);
                    a.download = document.title.replace(/[^a-zA-Z0-9 -]/g,'') + '.html';
                    a.click();
                });
                </script>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string ExecHeader(string customerName, DateTime now, string reportId, string pageNum)
        {
            return $@"<div class=""exec-header"">
                <div class=""exec-header-left"">
                    <div class=""exec-logo"">
                        <svg viewBox=""0 0 24 24"" fill=""none"" stroke=""#42a5f5"" stroke-width=""2"" width=""28"" height=""28""><path d=""M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z""/><path d=""M9 12l2 2 4-4"" stroke-linecap=""round"" stroke-linejoin=""round""/></svg>
                    </div>
                    <div>
                        <div class=""exec-brand"">PC PLUS COMPUTING</div>
                        <div class=""exec-sub"">ENDPOINT PROTECTION AUDIT</div>
                    </div>
                </div>
                <div class=""exec-header-center"">
                    <div class=""exec-customer"">{Esc(customerName)}</div>
                    <div class=""exec-slogan"">Protecting What Matters Most</div>
                </div>
                <div class=""exec-header-right"">
                    <div class=""exec-date"">{now:MMMM d, yyyy}</div>
                    <div class=""exec-time"">Generated: {now:h:mm tt}</div>
                    <div class=""exec-rid"">Report #{reportId}</div>
                    <div class=""exec-page-num"">{pageNum}</div>
                </div>
            </div>";
        }

        private static string ExecFooter()
        {
            return @"<div class=""exec-footer-bar"">
                <div class=""exec-footer-tagline"">Stronger Today. Safer Tomorrow.</div>
                <div class=""exec-footer-contact"">pcpluscomputing.com | 604-760-1662</div>
            </div>";
        }

        private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");

        private static string DarkDonut(int value, int total, string fillColor, int size,
            string centerText, string? centerSub = null)
        {
            double pct = total > 0 ? (double)value / total : 0;
            int r = size / 2 - 16;
            double circ = 2 * Math.PI * r;
            double fillLen = circ * pct;
            double gapLen = circ - fillLen;
            int cx = size / 2, cy = size / 2;
            int strokeW = 14;
            int textSize = size > 140 ? 28 : 20;
            int subSize = 11;

            var sb = new StringBuilder();
            sb.Append($@"<svg width=""{size}"" height=""{size}"" viewBox=""0 0 {size} {size}"" xmlns=""http://www.w3.org/2000/svg"">");
            sb.Append($@"<circle cx=""{cx}"" cy=""{cy}"" r=""{r}"" fill=""none"" stroke=""#1a2744"" stroke-width=""{strokeW}""/>");
            if (pct > 0)
                sb.Append($@"<circle cx=""{cx}"" cy=""{cy}"" r=""{r}"" fill=""none"" stroke=""{fillColor}"" stroke-width=""{strokeW}"" stroke-dasharray=""{fillLen:F1} {gapLen:F1}"" transform=""rotate(-90 {cx} {cy})"" stroke-linecap=""round""/>");
            int textY = !string.IsNullOrEmpty(centerSub) ? cy - 6 : cy + 2;
            sb.Append($@"<text x=""{cx}"" y=""{textY}"" text-anchor=""middle"" dominant-baseline=""middle"" font-family=""Segoe UI,Tahoma,sans-serif"" font-weight=""700"" font-size=""{textSize}"" fill=""#fff"">{Esc(centerText)}</text>");
            if (!string.IsNullOrEmpty(centerSub))
                sb.Append($@"<text x=""{cx}"" y=""{cy + 14}"" text-anchor=""middle"" dominant-baseline=""middle"" font-family=""Segoe UI,Tahoma,sans-serif"" font-size=""{subSize}"" fill=""#8899aa"">{Esc(centerSub)}</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string SvgDonut(int value, int total, string fillColor, string bgColor, int size,
            string centerText, string? centerSub = null)
        {
            double pct = total > 0 ? (double)value / total : 0;
            int r = size / 2 - 14;
            double circ = 2 * Math.PI * r;
            double fillLen = circ * pct;
            double gapLen = circ - fillLen;
            int cx = size / 2, cy = size / 2;
            int strokeW = size > 110 ? 16 : 12;
            int textSize = size > 110 ? 22 : 16;
            int subSize = size > 110 ? 11 : 9;

            var sb = new StringBuilder();
            sb.Append($@"<svg width=""{size}"" height=""{size}"" viewBox=""0 0 {size} {size}"" xmlns=""http://www.w3.org/2000/svg"">");
            sb.Append($@"<circle cx=""{cx}"" cy=""{cy}"" r=""{r}"" fill=""none"" stroke=""{bgColor}"" stroke-width=""{strokeW}""/>");
            if (pct > 0)
                sb.Append($@"<circle cx=""{cx}"" cy=""{cy}"" r=""{r}"" fill=""none"" stroke=""{fillColor}"" stroke-width=""{strokeW}"" stroke-dasharray=""{fillLen:F1} {gapLen:F1}"" transform=""rotate(-90 {cx} {cy})"" stroke-linecap=""butt""/>");
            int textY = centerSub != null ? cy - 5 : cy + 2;
            sb.Append($@"<text x=""{cx}"" y=""{textY}"" text-anchor=""middle"" dominant-baseline=""middle"" font-family=""Segoe UI,Tahoma,sans-serif"" font-weight=""700"" font-size=""{textSize}"" fill=""#1a1a2e"">{Esc(centerText)}</text>");
            if (centerSub != null)
                sb.Append($@"<text x=""{cx}"" y=""{cy + (size > 110 ? 14 : 10)}"" text-anchor=""middle"" dominant-baseline=""middle"" font-family=""Segoe UI,Tahoma,sans-serif"" font-size=""{subSize}"" fill=""#888"">{Esc(centerSub)}</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string GetCss() => @"
            * { margin:0; padding:0; box-sizing:border-box; }
            body { font-family:'Segoe UI',Tahoma,Geneva,Verdana,sans-serif; color:#e0e6ed; background:#0a1628; }

            .action-bar { padding:14px; text-align:center; background:#0d1b2a; border-bottom:1px solid #1a2744; }
            .action-bar button, .action-bar .btn-dl { background:#42a5f5; color:#fff; border:none; padding:10px 28px; border-radius:8px; font-size:13px; cursor:pointer; margin:0 4px; display:inline-block; text-decoration:none; }
            .action-bar button:hover, .action-bar .btn-dl:hover { background:#1e88e5; }
            .action-bar .btn-gray { background:#455a64; }

            /* EXECUTIVE PAGE - DARK THEME */
            .exec-page { background:linear-gradient(180deg,#0a1628 0%,#0d1f3c 100%); padding:0 0 20px; }
            .exec-header { display:flex; justify-content:space-between; align-items:center; padding:16px 36px; border-bottom:1px solid #1a2744; }
            .exec-header-left { display:flex; align-items:center; gap:10px; }
            .exec-logo { width:40px; height:40px; border:2px solid #42a5f5; border-radius:10px; display:flex; align-items:center; justify-content:center; background:#42a5f510; }
            .exec-brand { color:#fff; font-size:16px; font-weight:700; letter-spacing:1.5px; }
            .exec-sub { color:#42a5f5; font-size:10px; text-transform:uppercase; letter-spacing:2px; font-weight:600; }
            .exec-header-center { text-align:center; }
            .exec-customer { color:#42a5f5; font-size:13px; font-weight:600; }
            .exec-slogan { color:#5a7a9a; font-size:9px; text-transform:uppercase; letter-spacing:1.5px; margin-top:2px; font-style:italic; }
            .exec-header-right { text-align:right; }
            .exec-date { color:#8899aa; font-size:11px; }
            .exec-time { color:#5a7a9a; font-size:9px; margin-top:1px; }
            .exec-rid { color:#5a7a9a; font-size:9px; margin-top:1px; }
            .exec-page-num { color:#5a7a9a; font-size:10px; margin-top:2px; }

            .exec-page-title { text-align:center; font-size:20px; font-weight:800; color:#fff; padding:24px 36px 8px; letter-spacing:1px; }

            /* Page 1 */
            .p1-layout { display:flex; gap:30px; padding:20px 36px; align-items:stretch; flex-wrap:wrap; }
            .p1-score-section { flex:0 0 340px; text-align:center; background:#0d1f3c; border:1px solid #1a2744; border-radius:16px; padding:28px 24px; display:flex; flex-direction:column; align-items:center; }
            .p1-score-label { color:#8899aa; font-size:13px; text-transform:uppercase; letter-spacing:2px; margin-bottom:12px; font-weight:600; }
            .p1-score-donut-wrap { margin:8px 0; }
            .p1-grade-row { display:flex; align-items:center; gap:12px; margin-top:14px; }
            .p1-grade-badge { width:48px; height:48px; border-radius:12px; display:flex; align-items:center; justify-content:center; font-size:28px; font-weight:900; letter-spacing:1px; }
            .p1-grade-word { font-size:16px; font-weight:700; letter-spacing:0.5px; }
            .p1-risk-badge { display:inline-block; padding:8px 24px; border-radius:20px; font-size:13px; font-weight:700; letter-spacing:1px; margin-top:16px; }

            .p1-stats-section { flex:1; min-width:280px; display:flex; flex-direction:column; gap:16px; }
            .p1-total-checks { background:#0d1f3c; border:1px solid #1a2744; border-radius:14px; padding:24px; text-align:center; }
            .p1-total-num { font-size:52px; font-weight:900; color:#42a5f5; line-height:1; }
            .p1-total-label { color:#8899aa; font-size:13px; text-transform:uppercase; letter-spacing:2px; margin-top:6px; font-weight:600; }
            .p1-total-sub { color:#5a7a9a; font-size:11px; margin-top:4px; }
            .p1-stat-grid { display:grid; grid-template-columns:repeat(2,1fr); gap:12px; }
            .p1-mini-stat { background:#0d1f3c; border:2px solid #1a2744; border-radius:12px; padding:18px 12px; text-align:center; }
            .p1-mini-stat.passed { border-color:#00e676; }
            .p1-mini-stat.passed .p1-mini-num { color:#00e676; }
            .p1-mini-stat.failed { border-color:#ef5350; }
            .p1-mini-stat.failed .p1-mini-num { color:#ef5350; }
            .p1-mini-stat.attention { border-color:#ffa726; }
            .p1-mini-stat.attention .p1-mini-num { color:#ffa726; }
            .p1-mini-stat.highrisk { border-color:#ef5350; }
            .p1-mini-stat.highrisk .p1-mini-num { color:#ef5350; }
            .p1-mini-num { font-size:28px; font-weight:900; }
            .p1-mini-label { color:#8899aa; font-size:11px; margin-top:4px; text-transform:uppercase; letter-spacing:0.5px; }

            .p1-priorities-title { color:#ef5350; font-size:14px; font-weight:800; letter-spacing:2px; text-align:center; padding:20px 36px 10px; }
            .p1-priorities { display:grid; grid-template-columns:repeat(2,1fr); gap:12px; padding:0 36px 8px; }
            .p1-pri-item { display:flex; align-items:center; gap:12px; background:#0d1f3c; border:1px solid #1a2744; border-radius:12px; padding:14px 16px; }
            .p1-pri-icon { flex-shrink:0; width:36px; height:36px; display:flex; align-items:center; justify-content:center; }
            .p1-pri-content { flex:1; min-width:0; }
            .p1-pri-name { color:#fff; font-size:13px; font-weight:700; margin-bottom:3px; }
            .p1-pri-detail { color:#5a7a9a; font-size:10px; line-height:1.3; overflow:hidden; text-overflow:ellipsis; display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical; }
            .p1-pri-badge { font-size:9px; font-weight:700; letter-spacing:1px; padding:4px 10px; border-radius:10px; white-space:nowrap; flex-shrink:0; }

            .p1-coverage { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; padding:12px 36px 0; }
            .p1-cov-item { text-align:center; background:#0d1f3c; border:1px solid #1a2744; border-radius:14px; padding:18px 12px; }
            .p1-cov-icon { width:56px; height:56px; border:2px solid; border-radius:50%; display:flex; align-items:center; justify-content:center; margin:0 auto 10px; background:#0a162810; }
            .p1-cov-label { color:#fff; font-size:13px; font-weight:700; letter-spacing:1.5px; margin-bottom:5px; }
            .p1-cov-desc { color:#5a7a9a; font-size:10px; line-height:1.4; }

            /* Page 2 */
            .p2-layout { display:flex; gap:24px; padding:16px 36px; flex-wrap:wrap; }
            .p2-table-section { flex:2; min-width:300px; }
            .p2-cat-table { width:100%; border-collapse:collapse; background:#0d1f3c; border-radius:12px; overflow:hidden; }
            .p2-cat-table thead th { background:#142240; color:#8899aa; text-align:left; padding:10px 14px; font-size:10px; text-transform:uppercase; letter-spacing:0.5px; border-bottom:1px solid #1a2744; }
            .p2-cat-table td { padding:8px 14px; border-bottom:1px solid #1a274440; font-size:12px; color:#c0cdd8; }
            .p2-cat-name { font-weight:600; color:#e0e6ed; }
            .p2-cat-pts { text-align:center; color:#8899aa; }
            .p2-cat-score { font-weight:700; text-align:center; }
            .p2-risk-tag { display:inline-block; padding:3px 10px; border-radius:12px; font-size:10px; font-weight:600; }
            .p2-total-row { background:#142240; }
            .p2-total-row td { border-top:2px solid #42a5f5; }

            .p2-side-section { flex:1; min-width:220px; display:flex; flex-direction:column; gap:16px; }
            .p2-risk-dist { background:#0d1f3c; border:1px solid #1a2744; border-radius:14px; padding:20px; text-align:center; }
            .p2-section-head { color:#fff; font-size:14px; font-weight:700; margin-bottom:14px; }
            .p2-risk-legend { text-align:left; margin-top:14px; }
            .p2-legend-item { display:flex; align-items:center; gap:8px; margin-bottom:6px; font-size:11px; color:#8899aa; }
            .p2-legend-dot { width:10px; height:10px; border-radius:50%; flex-shrink:0; }

            .p2-top-risks { background:#0d1f3c; border:1px solid #1a2744; border-radius:14px; padding:16px; }
            .p2-risk-item { display:flex; align-items:center; justify-content:space-between; gap:8px; padding:8px 0; border-bottom:1px solid #1a274440; }
            .p2-risk-item:last-child { border-bottom:none; }
            .p2-risk-name { font-size:11px; color:#c0cdd8; font-weight:500; flex:1; }
            .p2-risk-pri { font-size:9px; padding:2px 8px; border-radius:10px; font-weight:600; white-space:nowrap; }

            /* Page 3 */
            .p3-bva-section { display:flex; align-items:center; justify-content:center; gap:24px; padding:20px 36px; flex-wrap:wrap; }
            .p3-bva-card { background:#0d1f3c; border:1px solid #1a2744; border-radius:16px; padding:20px 28px; text-align:center; }
            .p3-bva-label { color:#8899aa; font-size:11px; text-transform:uppercase; letter-spacing:1px; margin-bottom:10px; }
            .p3-bva-status { font-size:13px; font-weight:700; margin-top:8px; }
            .p3-bva-arrow { color:#42a5f5; }
            .p3-bva-stat { background:#0d1f3c; border:1px solid #1a2744; border-radius:16px; padding:20px 28px; text-align:center; }
            .p3-bva-big { font-size:36px; font-weight:800; }
            .p3-bva-sublabel { color:#8899aa; font-size:11px; margin-top:4px; }

            .p3-impact { padding:16px 36px; }
            .p3-section-head { color:#fff; font-size:15px; font-weight:700; margin-bottom:14px; padding-bottom:6px; border-bottom:1px solid #1a2744; }
            .p3-impact-grid { display:grid; grid-template-columns:repeat(4,1fr); gap:14px; }
            .p3-impact-item { background:#0d1f3c; border:1px solid #1a2744; border-radius:12px; padding:16px; text-align:center; }
            .p3-impact-icon { margin-bottom:8px; }
            .p3-impact-title { color:#fff; font-size:13px; font-weight:700; margin-bottom:4px; }
            .p3-impact-desc { color:#5a7a9a; font-size:10px; line-height:1.4; }

            .p3-actions { padding:16px 36px; }
            .p3-action-list { display:flex; flex-direction:column; gap:8px; }
            .p3-action-item { display:flex; align-items:center; gap:14px; background:#0d1f3c; border:1px solid #1a2744; border-radius:12px; padding:12px 16px; }
            .p3-action-num { width:32px; height:32px; border-radius:50%; display:flex; align-items:center; justify-content:center; color:#fff; font-weight:700; font-size:14px; flex-shrink:0; }
            .p3-action-body { flex:1; }
            .p3-action-name { color:#e0e6ed; font-size:13px; font-weight:600; }
            .p3-action-rec { color:#5a7a9a; font-size:11px; margin-top:2px; }
            .p3-action-devices { color:#8899aa; font-size:11px; white-space:nowrap; }

            .p3-next-steps { display:flex; align-items:center; justify-content:center; gap:12px; padding:20px 36px; flex-wrap:wrap; }
            .p3-step { text-align:center; }
            .p3-step-icon { width:48px; height:48px; border:2px solid; border-radius:50%; display:flex; align-items:center; justify-content:center; margin:0 auto 6px; }
            .p3-step-label { color:#fff; font-size:11px; font-weight:700; letter-spacing:1px; }
            .p3-step-arrow { color:#42a5f5; font-size:24px; margin:0 4px; }

            /* Page 4 */
            .p4-layout { display:flex; gap:20px; padding:12px 36px; flex-wrap:wrap; }
            .p4-table-section { flex:2; min-width:320px; }
            .p4-risk-table { width:100%; border-collapse:collapse; background:#0d1f3c; border-radius:12px; overflow:hidden; }
            .p4-risk-table thead th { background:#142240; color:#8899aa; text-align:left; padding:10px 12px; font-size:10px; text-transform:uppercase; letter-spacing:0.5px; }
            .p4-risk-table td { padding:10px 12px; border-bottom:1px solid #1a274440; font-size:11px; color:#c0cdd8; vertical-align:top; }
            .p4-risk-cell { font-weight:700; font-size:11px; text-align:center; vertical-align:middle; width:90px; }
            .p4-risk-cell.high { color:#ef5350; background:#ef535010; }
            .p4-risk-cell.medium { color:#ffa726; background:#ffa72610; }
            .p4-risk-cell.low { color:#00e676; background:#00e67610; }
            .p4-risk-dot { display:inline-block; width:8px; height:8px; border-radius:50%; margin-right:6px; }
            .p4-risk-dot.high { background:#ef5350; }
            .p4-risk-dot.medium { background:#ffa726; }
            .p4-risk-dot.low { background:#00e676; }
            .p4-cat-sub { color:#5a7a9a; font-size:10px; }
            .p4-impact { color:#8899aa; font-size:10px; font-style:italic; }
            .p4-action { color:#42a5f5; font-size:10px; }

            .p4-roadmap-section { flex:1; min-width:220px; }
            .p4-roadmap-title { color:#fff; font-size:14px; font-weight:700; margin-bottom:14px; text-align:center; }
            .p4-roadmap-item { background:#0d1f3c; border:1px solid #1a2744; border-radius:12px; padding:14px; margin-bottom:10px; }
            .p4-rm-badge { display:inline-block; padding:3px 12px; border-radius:10px; font-size:10px; font-weight:700; border:1px solid; letter-spacing:0.5px; }
            .p4-rm-time { color:#8899aa; font-size:10px; margin-top:6px; font-weight:600; }
            .p4-rm-list { margin:8px 0 0 16px; color:#c0cdd8; font-size:11px; line-height:1.8; }

            .p4-promise { background:linear-gradient(135deg,#142240,#1a2744); border:1px solid #42a5f540; border-radius:14px; padding:18px; text-align:center; margin-top:12px; }
            .p4-promise-title { color:#42a5f5; font-size:13px; font-weight:700; margin-bottom:6px; }
            .p4-promise-text { color:#8899aa; font-size:11px; line-height:1.5; }

            .exec-footer-bar { padding:16px 36px; display:flex; justify-content:space-between; align-items:center; border-top:1px solid #1a2744; margin-top:auto; }
            .exec-footer-tagline { color:#42a5f5; font-size:14px; font-weight:700; font-style:italic; }
            .exec-footer-contact { color:#5a7a9a; font-size:11px; }

            /* DETAILED REPORT - LIGHT THEME */
            .detail-section { padding:36px 40px; background:#fff; color:#1a1a2e; }
            .page-break { page-break-before:always; }
            .section-title { font-size:18px; color:#1a56db; font-weight:700; margin-bottom:14px; padding-bottom:6px; border-bottom:2px solid #e5e7eb; }

            .table-wrap { overflow-x:auto; -webkit-overflow-scrolling:touch; }
            .device-table { width:100%; border-collapse:collapse; margin-bottom:20px; min-width:600px; }
            .device-table th { background:#1e293b; color:#fff; text-align:left; padding:8px 10px; font-size:10px; text-transform:uppercase; letter-spacing:0.5px; white-space:nowrap; }
            .device-table td { padding:8px 10px; border-bottom:1px solid #f1f5f9; font-size:12px; color:#333; }
            .device-table tr:nth-child(even) { background:#f8fafc; }
            .os-col { font-size:10px; color:#666; }
            .pass { color:#16a34a; font-weight:600; } .fail { color:#dc2626; font-weight:600; }

            .grade-badge { display:inline-flex; align-items:center; justify-content:center; width:28px; height:28px; border-radius:50%; color:#fff; font-weight:700; font-size:12px; }
            .grade-a { background:#16a34a; } .grade-b { background:#2563eb; } .grade-c { background:#d97706; } .grade-d { background:#ea580c; } .grade-e { background:#dc2626; } .grade-f { background:#dc2626; }

            .device-block { margin-bottom:20px; border:1px solid #e5e7eb; border-radius:10px; overflow:hidden; }
            .device-block-header { padding:10px 14px; background:#1e293b; color:#fff; font-weight:600; font-size:13px; display:flex; justify-content:space-between; flex-wrap:wrap; gap:6px; }
            .device-block table { width:100%; border-collapse:collapse; }
            .device-block table th { background:#fef3f2; color:#1a1a2e; text-align:left; padding:6px 10px; font-size:10px; text-transform:uppercase; }
            .device-block table td { padding:6px 10px; border-bottom:1px solid #f1f5f9; font-size:11px; color:#333; }
            .device-all-pass { padding:14px; background:#f0fdf4; color:#16a34a; font-weight:600; text-align:center; font-size:13px; }

            .footer { padding:24px 40px; text-align:center; color:#999; font-size:10px; border-top:2px solid #e5e7eb; background:#fff; }

            .green { color:#16a34a; } .red { color:#dc2626; } .blue { color:#1a56db; } .orange { color:#d97706; }

            @media (max-width:700px) {
                .exec-header { flex-direction:column; align-items:flex-start; padding:12px 20px; }
                .exec-page-title { font-size:16px; }
                .p1-layout { flex-direction:column; padding:16px 20px; }
                .p1-priorities { grid-template-columns:1fr; padding:8px 20px; }
                .p1-coverage { grid-template-columns:1fr; padding:8px 20px; }
                .p1-stat-grid { grid-template-columns:1fr; }
                .p2-layout { flex-direction:column; padding:16px 20px; }
                .p3-bva-section { flex-direction:column; }
                .p3-impact-grid { grid-template-columns:repeat(2,1fr); }
                .p3-next-steps { flex-direction:column; }
                .p3-step-arrow { transform:rotate(90deg); }
                .p4-layout { flex-direction:column; padding:16px 20px; }
                .detail-section { padding:24px 16px; }
            }

            @media print {
                .no-print { display:none !important; }
                .exec-page { page-break-after:always; }
                .page-break { page-break-before:always; }
                body { -webkit-print-color-adjust:exact; print-color-adjust:exact; }
                .exec-page, .exec-header, .exec-footer-bar, .p1-score-section, .p1-mini-stat, .p1-pri-item, .p1-pri-badge, .p1-cov-item, .p1-cov-icon, .p2-cat-table, .p2-cat-table thead th, .p2-risk-dist, .p2-top-risks, .p3-bva-card, .p3-bva-stat, .p3-impact-item, .p3-action-item, .p3-step-icon, .p4-risk-table, .p4-risk-table thead th, .p4-roadmap-item, .p4-promise, .p4-risk-cell, .exec-logo, .p1-risk-badge, .p1-grade-badge, .p2-risk-tag, .p2-risk-pri, .p4-rm-badge, .p3-action-num, .grade-badge, .device-table th, .device-block-header { -webkit-print-color-adjust:exact; print-color-adjust:exact; }
            }
        ";
    }
}
