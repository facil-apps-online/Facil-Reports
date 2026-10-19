using FastReport;
using FacilReports.Models;

namespace FacilReports.Services;

/// <summary>
/// Motor de impresión FastReport. REPX sigue siendo la única plantilla guardada: se convierte en memoria
/// para esta petición (<see cref="RepxToFastReportConverter"/>), FastReport calcula bandas, crecimiento y
/// paginación, y <see cref="SkiaPdfExport"/> dibuja el resultado como PDF vectorial (texto real, fuentes
/// embebidas, QR vectorial). Al final <see cref="GhostscriptPdfOptimizer"/> recorta las fuentes.
/// Lo que el conversor no soporta lanza NotSupportedException con el nombre de la plantilla y del
/// control, para que el llamador pueda reportarlo (no se imprime un PDF distinto del diseñado).
/// </summary>
public sealed class FastReportGenerator
{
    public const string WatermarkKey = "__Watermark";

    private readonly GoogleDriveService _templates;
    private readonly RepxToFastReportConverter _converter;
    private readonly GhostscriptPdfOptimizer _optimizer;
    private readonly ILogger<FastReportGenerator> _logger;

    public FastReportGenerator(
        GoogleDriveService templates,
        RepxToFastReportConverter converter,
        GhostscriptPdfOptimizer optimizer,
        ILogger<FastReportGenerator> logger)
    {
        _templates = templates;
        _converter = converter;
        _optimizer = optimizer;
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

        // "__Watermark" no es un dato de la plantilla: es la orden de dibujar ese texto en diagonal sobre cada página (p. ej. "ANULADA").
        string? watermark = null;
        if (data.TryGetValue(WatermarkKey, out var watermarkValue))
        {
            watermark = watermarkValue?.ToString();
            data = data.Where(x => x.Key != WatermarkKey).ToDictionary(x => x.Key, x => x.Value);
        }

        _logger.LogInformation("Convirtiendo REPX {TemplateKey} en memoria para FastReport.", templateKey);
        Report report;
        try
        {
            report = _converter.Convert(repx, data.ToDictionary(x => x.Key, x => (object?)x.Value));
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException($"Plantilla '{templateKey}': {ex.Message}", ex);
        }
        report.Prepare();

        using var output = new MemoryStream();
        var exporter = new SkiaPdfExport { Watermark = watermark };
        report.Export(exporter, output);
        foreach (var overlap in exporter.Overlaps.Distinct())
            _logger.LogWarning("Plantilla {TemplateKey}: {Overlap}", templateKey, overlap);
        report.Dispose();
        return await _optimizer.OptimizeAsync(output.ToArray());
    }
}
