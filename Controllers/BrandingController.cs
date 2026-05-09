using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace PCPlus.Dashboard.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard/branding")]
public class BrandingController : ControllerBase
{
    private static readonly string BrandingFile = Path.Combine(AppContext.BaseDirectory, "data", "branding.json");

    [HttpGet]
    public ActionResult GetBranding()
    {
        if (!System.IO.File.Exists(BrandingFile))
            return Ok(new BrandingConfig());
        var json = System.IO.File.ReadAllText(BrandingFile);
        return Ok(JsonSerializer.Deserialize<BrandingConfig>(json) ?? new BrandingConfig());
    }

    [HttpPost]
    public ActionResult SaveBranding([FromBody] BrandingConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BrandingFile)!);
        System.IO.File.WriteAllText(BrandingFile, JsonSerializer.Serialize(config));
        return Ok(new { ok = true });
    }

    [HttpPost("logo")]
    public async Task<ActionResult> UploadLogo(IFormFile logo)
    {
        if (logo == null || logo.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (logo.Length > 2 * 1024 * 1024)
            return BadRequest(new { error = "File too large (max 2MB)" });

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);

        var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".svg" or ".webp"))
            return BadRequest(new { error = "Invalid file type" });

        var fileName = "logo" + ext;
        var filePath = Path.Combine(dataDir, fileName);

        using var stream = new FileStream(filePath, FileMode.Create);
        await logo.CopyToAsync(stream);

        var url = "/branding-assets/" + fileName;

        // Update branding config
        BrandingConfig config;
        if (System.IO.File.Exists(BrandingFile))
        {
            var json = System.IO.File.ReadAllText(BrandingFile);
            config = JsonSerializer.Deserialize<BrandingConfig>(json) ?? new BrandingConfig();
        }
        else
        {
            config = new BrandingConfig();
        }
        config.LogoUrl = url;
        System.IO.File.WriteAllText(BrandingFile, JsonSerializer.Serialize(config));

        return Ok(new { url });
    }
}

public class BrandingConfig
{
    public string CompanyName { get; set; } = "PC Plus Computing";
    public string Tagline { get; set; } = "Endpoint Protection";
    public string LogoUrl { get; set; } = "";
}
