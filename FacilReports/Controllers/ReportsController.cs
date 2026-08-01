using FacilReports.Models;
using FacilReports.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacilReports.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly ReportGenerator _generator;

    public ReportsController(ReportGenerator generator)
    {
        _generator = generator;
    }

    /// <summary>
    /// Generate a PDF from a template and data
    /// Returns the PDF as base64 or binary
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateReportRequest request)
    {
        var tenant = HttpContext.Items["Tenant"] as TenantConfig;
        if (tenant == null) return Unauthorized();

        try
        {
            var pdfBytes = await _generator.GenerateFromJson(
                tenant.Id,
                request.TemplateKey,
                request.Data
            );

            // Return as base64 if requested
            if (request.AsBase64 == true)
            {
                return Ok(new
                {
                    success = true,
                    pdfBase64 = Convert.ToBase64String(pdfBytes),
                    templateKey = request.TemplateKey
                });
            }

            // Return as binary PDF
            return File(pdfBytes, "application/pdf", $"{request.TemplateKey}.pdf");
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
