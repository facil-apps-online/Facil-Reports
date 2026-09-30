using FastReport;
using FastReport.Export.PdfSimple;
using FacilReports.Models;

namespace FacilReports.Services;

/// <summary>
/// In-memory FastReport printing path. It deliberately does not write an FRX: REPX remains the
/// only persisted template and is converted for the duration of a print request.
/// </summary>
public sealed class FastReportGenerator
{
    private readonly GoogleDriveService _templates;
    private readonly RepxToFastReportConverter _converter;
    private readonly ILogger<FastReportGenerator> _logger;

    public FastReportGenerator(
        GoogleDriveService templates,
        RepxToFastReportConverter converter,
        ILogger<FastReportGenerator> logger)
    {
        _templates = templates;
        _converter = converter;
        _logger = logger;
    }

    public async Task<byte[]> GenerateFromJson(
        TenantConfig tenant,
        string templateKey,
        Dictionary<string, object> data,
        string? fileId = null)
    {
        var repx = await _templates.GetTemplateAsync(tenant, templateKey, fileId)
            ?? throw new FileNotFoundException($"Template '{templateKey}' not found");

        _logger.LogInformation("Convirtiendo REPX {TemplateKey} en memoria para FastReport.", templateKey);
        var report = _converter.Convert(repx, data.ToDictionary(x => x.Key, x => (object?)x.Value));
        report.Prepare();

        using var output = new MemoryStream();
        report.Export(new PDFSimpleExport(), output);
        return output.ToArray();
    }
}
