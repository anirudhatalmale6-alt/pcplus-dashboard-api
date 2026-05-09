using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;
using PCPlus.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

// SQLite database (easy deployment, no external DB needed)
var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "dashboard.db");

builder.Services.AddDbContext<DashboardDb>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login.html";
        options.LogoutPath = "/api/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            // Return 401 for API calls, redirect for page requests
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// Background services
builder.Services.AddSingleton<ReportGenerator>();
builder.Services.AddHostedService<EmailReportService>();
builder.Services.AddHostedService<HistoryService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<NotificationService>();

var app = builder.Build();

// Auto-create/migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DashboardDb>();
    db.Database.EnsureCreated();

    // Add new columns if missing (EnsureCreated doesn't alter existing tables)
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN GpuTempC REAL NOT NULL DEFAULT 0"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN LocalIp TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN PublicIp TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN BitLockerKeysJson TEXT NOT NULL DEFAULT '[]'"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN StorageDrivesJson TEXT NOT NULL DEFAULT '[]'"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN MacAddress TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN NetworkUpMbps REAL NOT NULL DEFAULT 0"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN NetworkDownMbps REAL NOT NULL DEFAULT 0"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS EmailSchedules (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        CustomerName TEXT NOT NULL DEFAULT '',
        RecipientEmails TEXT NOT NULL DEFAULT '',
        Frequency TEXT NOT NULL DEFAULT 'weekly',
        DayOfWeek INTEGER NOT NULL DEFAULT 1,
        Hour INTEGER NOT NULL DEFAULT 8,
        Enabled INTEGER NOT NULL DEFAULT 1,
        LastSentAt TEXT,
        NextSendAt TEXT,
        CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
        CreatedBy TEXT NOT NULL DEFAULT ''
    )"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS DeviceHistories (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        DeviceId TEXT NOT NULL DEFAULT '',
        Hostname TEXT NOT NULL DEFAULT '',
        CustomerName TEXT NOT NULL DEFAULT '',
        SecurityScore INTEGER NOT NULL DEFAULT 0,
        CpuPercent REAL NOT NULL DEFAULT 0,
        RamPercent REAL NOT NULL DEFAULT 0,
        DiskPercent REAL NOT NULL DEFAULT 0,
        CpuTempC REAL NOT NULL DEFAULT 0,
        IsOnline INTEGER NOT NULL DEFAULT 0,
        Timestamp TEXT NOT NULL DEFAULT (datetime('now'))
    )"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_DeviceHistories_DeviceId ON DeviceHistories (DeviceId)"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_DeviceHistories_CustomerName ON DeviceHistories (CustomerName)"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_DeviceHistories_Timestamp ON DeviceHistories (Timestamp)"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN CustomerName TEXT NOT NULL DEFAULT ''"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS NotificationConfigs (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT NOT NULL DEFAULT '',
        Type TEXT NOT NULL DEFAULT 'webhook',
        WebhookUrl TEXT NOT NULL DEFAULT '',
        MinSeverity TEXT NOT NULL DEFAULT 'Critical',
        Enabled INTEGER NOT NULL DEFAULT 1,
        CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
    )"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS SmtpConfigs (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Host TEXT NOT NULL DEFAULT '',
        Port INTEGER NOT NULL DEFAULT 587,
        Username TEXT NOT NULL DEFAULT '',
        Password TEXT NOT NULL DEFAULT '',
        FromAddress TEXT NOT NULL DEFAULT '',
        FromName TEXT NOT NULL DEFAULT 'PC Plus Computing',
        UseSsl INTEGER NOT NULL DEFAULT 1
    )"); } catch { }

    // Security logs table
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS SecurityLogs (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        DeviceId TEXT NOT NULL DEFAULT '',
        Hostname TEXT NOT NULL DEFAULT '',
        EventType TEXT NOT NULL DEFAULT '',
        Source TEXT NOT NULL DEFAULT '',
        Severity TEXT NOT NULL DEFAULT 'Info',
        Category TEXT NOT NULL DEFAULT '',
        Message TEXT NOT NULL DEFAULT '',
        DetailsJson TEXT NOT NULL DEFAULT '{}',
        Timestamp TEXT NOT NULL DEFAULT (datetime('now'))
    )"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SecurityLogs_DeviceId ON SecurityLogs (DeviceId)"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SecurityLogs_Severity ON SecurityLogs (Severity)"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SecurityLogs_Category ON SecurityLogs (Category)"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SecurityLogs_Timestamp ON SecurityLogs (Timestamp)"); } catch { }

    // Backup statuses table
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS BackupStatuses (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        DeviceId TEXT NOT NULL DEFAULT '',
        Hostname TEXT NOT NULL DEFAULT '',
        Provider TEXT NOT NULL DEFAULT '',
        LastBackupTime TEXT,
        NextBackupTime TEXT,
        Status TEXT NOT NULL DEFAULT '',
        SizeBytes INTEGER NOT NULL DEFAULT 0,
        ProtectedPathsJson TEXT NOT NULL DEFAULT '[]',
        ShadowCopyEnabled INTEGER NOT NULL DEFAULT 0,
        ShadowCopyCount INTEGER NOT NULL DEFAULT 0,
        RecoveryPointCount INTEGER NOT NULL DEFAULT 0,
        LastTestedAt TEXT,
        LastUpdated TEXT NOT NULL DEFAULT (datetime('now'))
    )"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_BackupStatuses_DeviceId ON BackupStatuses (DeviceId)"); } catch { }

    // Network security data table
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS NetworkSecurityData (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        DeviceId TEXT NOT NULL DEFAULT '',
        Hostname TEXT NOT NULL DEFAULT '',
        FirewallEnabled INTEGER NOT NULL DEFAULT 0,
        FirewallProfilesJson TEXT NOT NULL DEFAULT '[]',
        OpenPortsJson TEXT NOT NULL DEFAULT '[]',
        ActiveConnections INTEGER NOT NULL DEFAULT 0,
        RdpEnabled INTEGER NOT NULL DEFAULT 0,
        DnsServersJson TEXT NOT NULL DEFAULT '[]',
        WifiSecurityType TEXT NOT NULL DEFAULT '',
        LastUpdated TEXT NOT NULL DEFAULT (datetime('now'))
    )"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_NetworkSecurityData_DeviceId ON NetworkSecurityData (DeviceId)"); } catch { }

    // Ransomware statuses table
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS RansomwareStatuses (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        DeviceId TEXT NOT NULL DEFAULT '',
        Hostname TEXT NOT NULL DEFAULT '',
        BehaviorMonitoringEnabled INTEGER NOT NULL DEFAULT 0,
        ProtectedFoldersJson TEXT NOT NULL DEFAULT '[]',
        ShadowCopyProtected INTEGER NOT NULL DEFAULT 0,
        HoneypotActive INTEGER NOT NULL DEFAULT 0,
        RollbackCapable INTEGER NOT NULL DEFAULT 0,
        DetectionRulesJson TEXT NOT NULL DEFAULT '[]',
        ThreatHistoryJson TEXT NOT NULL DEFAULT '[]',
        LastUpdated TEXT NOT NULL DEFAULT (datetime('now'))
    )"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_RansomwareStatuses_DeviceId ON RansomwareStatuses (DeviceId)"); } catch { }

    // Antivirus products table
    try { db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS AntivirusProducts (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        DeviceId TEXT NOT NULL DEFAULT '',
        Hostname TEXT NOT NULL DEFAULT '',
        Name TEXT NOT NULL DEFAULT '',
        Vendor TEXT NOT NULL DEFAULT '',
        Version TEXT NOT NULL DEFAULT '',
        DefinitionDate TEXT,
        Status TEXT NOT NULL DEFAULT '',
        RealTimeEnabled INTEGER NOT NULL DEFAULT 0,
        LastScanTime TEXT,
        QuarantineCount INTEGER NOT NULL DEFAULT 0,
        LastUpdated TEXT NOT NULL DEFAULT (datetime('now'))
    )"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_AntivirusProducts_DeviceId ON AntivirusProducts (DeviceId)"); } catch { }
}

app.UseCors();

// Portal mode detection - when accessed via portal.pcpluscomputing.com,
// nginx adds X-Portal-Mode header. Pass it to responses for JS to detect.
app.Use(async (context, next) =>
{
    var portalMode = context.Request.Headers["X-Portal-Mode"].FirstOrDefault();
    if (!string.IsNullOrEmpty(portalMode))
        context.Items["PortalMode"] = portalMode;
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

// Serve branding assets (uploaded logos) from data directory
var brandingDir = Path.Combine(AppContext.BaseDirectory, "data");
if (Directory.Exists(brandingDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(brandingDir),
        RequestPath = "/branding-assets"
    });
}

app.MapControllers();

// Serve the dashboard SPA - fallback to index.html (requires auth)
app.MapFallbackToFile("index.html").RequireAuthorization();

app.Run();
