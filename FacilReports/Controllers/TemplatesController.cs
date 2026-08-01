using FacilReports.Models;
using FacilReports.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacilReports.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TemplatesController : ControllerBase
{
    private readonly GoogleDriveService _driveService;

    public TemplatesController(GoogleDriveService driveService)
    {
        _driveService = driveService;
    }

    /// <summary>
    /// Save a .repx template to Google Drive
    /// </summary>
    [HttpPost("save")]
    public async Task<IActionResult> SaveTemplate([FromBody] SaveTemplateRequest request)
    {
        var tenant = HttpContext.Items["Tenant"] as TenantConfig;
        if (tenant == null) return Unauthorized();

        try
        {
            var repxBytes = Convert.FromBase64String(request.RepxBase64);
            
            var fileId = await _driveService.UploadTemplate(
                tenant,
                request.TemplateKey,
                repxBytes
            );

            return Ok(new
            {
                success = true,
                templateKey = request.TemplateKey,
                fileId,
                message = "Template saved to Google Drive"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get a .repx template from Google Drive
    /// </summary>
    [HttpGet("{templateKey}")]
    public async Task<IActionResult> GetTemplate(string templateKey)
    {
        var tenant = HttpContext.Items["Tenant"] as TenantConfig;
        if (tenant == null) return Unauthorized();

        try
        {
            var repxBytes = await _driveService.DownloadTemplate(tenant, templateKey);
            if (repxBytes == null)
                return NotFound(new { error = $"Template '{templateKey}' not found" });

            return File(repxBytes, "application/xml", $"{templateKey}.repx");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get a .repx template from Google Drive (alias for backward compatibility)
    /// </summary>
    [HttpGet("repx/{templateKey}")]
    public async Task<IActionResult> GetRepxTemplate(string templateKey)
    {
        return await GetTemplate(templateKey);
    }

    /// <summary>
    /// List all templates for the tenant
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> ListTemplates()
    {
        var tenant = HttpContext.Items["Tenant"] as TenantConfig;
        if (tenant == null) return Unauthorized();

        try
        {
            var templates = await _driveService.ListTemplates(tenant);
            return Ok(new { templates });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a template from Google Drive
    /// </summary>
    [HttpDelete("{templateKey}")]
    public async Task<IActionResult> DeleteTemplate(string templateKey)
    {
        var tenant = HttpContext.Items["Tenant"] as TenantConfig;
        if (tenant == null) return Unauthorized();

        try
        {
            await _driveService.DeleteTemplate(tenant, templateKey);
            return Ok(new { success = true, message = $"Template '{templateKey}' deleted" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
