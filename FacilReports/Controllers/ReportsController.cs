using FacilReports.Models;
using FacilReports.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacilReports.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly ReportGenerator _generator;
    private readonly FastReportGenerator _fastReportGenerator;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        ReportGenerator generator,
        FastReportGenerator fastReportGenerator,
        ILogger<ReportsController> logger)
    {
        _generator = generator;
        _fastReportGenerator = fastReportGenerator;
        _logger = logger;
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
            byte[] pdfBytes;
            try
            {
                // REPX is still the only stored artifact. FastReport receives an in-memory
                // conversion for this request; DevExpress remains the compatibility fallback.
                pdfBytes = await _fastReportGenerator.GenerateFromJson(
                    tenant, request.TemplateKey, request.Data, request.FileId);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "REPX {TemplateKey} aún no es compatible con FastReport; usando DevExpress.", request.TemplateKey);
                pdfBytes = await _generator.GenerateFromJson(
                    tenant, request.TemplateKey, request.Data, request.FileId);
            }

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
