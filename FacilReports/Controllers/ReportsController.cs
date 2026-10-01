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
    private readonly IConfiguration _config;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        ReportGenerator generator,
        FastReportGenerator fastReportGenerator,
        IConfiguration config,
        ILogger<ReportsController> logger)
    {
        _generator = generator;
        _fastReportGenerator = fastReportGenerator;
        _config = config;
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

        var engine = ResolveEngine(request.Engine);
        if (engine == null)
            return BadRequest(new { error = $"Motor de impresión desconocido: '{request.Engine}'. Valores válidos: fastreport, devexpress." });

        try
        {
            byte[] pdfBytes;
            if (engine == RenderingEngine.DevExpress)
            {
                pdfBytes = await _generator.GenerateFromJson(tenant, request.TemplateKey, request.Data, request.FileId);
            }
            else
            {
                try
                {
                    // REPX es el único artefacto guardado: FastReport recibe una conversión en memoria
                    // para esta petición y la exporta como PDF vectorial.
                    pdfBytes = await _fastReportGenerator.GenerateFromJson(
                        tenant, request.TemplateKey, request.Data, request.FileId);
                }
                catch (NotSupportedException ex) when (_config.GetValue("Rendering:FallbackToDevExpress", false))
                {
                    // Solo si se activó explícitamente; por defecto un diseño no soportado es un error.
                    _logger.LogWarning(ex, "REPX {TemplateKey} no es compatible con FastReport; usando DevExpress (Rendering:FallbackToDevExpress).", request.TemplateKey);
                    pdfBytes = await _generator.GenerateFromJson(
                        tenant, request.TemplateKey, request.Data, request.FileId);
                }
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
        catch (NotSupportedException ex)
        {
            // La plantilla usa un control o una banda que el motor FastReport no soporta: error explícito
            // (con el nombre de la plantilla y del control) en vez de imprimir algo distinto de lo diseñado.
            _logger.LogError(ex, "Plantilla {TemplateKey} no soportada por el motor {Engine}.", request.TemplateKey, engine);
            return UnprocessableEntity(new { error = ex.Message, engine = engine.ToString(), templateKey = request.TemplateKey });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando {TemplateKey} con el motor {Engine}.", request.TemplateKey, engine);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private enum RenderingEngine { FastReport, DevExpress }

    // Petición > configuración (Rendering:Engine) > FastReport.
    private RenderingEngine? ResolveEngine(string? requested)
    {
        var value = string.IsNullOrWhiteSpace(requested) ? _config["Rendering:Engine"] : requested;
        if (string.IsNullOrWhiteSpace(value)) return RenderingEngine.FastReport;
        return value.Trim().ToLowerInvariant() switch
        {
            "fastreport" => RenderingEngine.FastReport,
            "devexpress" => RenderingEngine.DevExpress,
            _ => null
        };
    }
}
