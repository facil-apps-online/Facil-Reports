using System.Linq;
using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.Web.ClientControls;
using DevExpress.XtraReports.Web.Extensions;
using FacilReports.Models;

namespace FacilReports.Services;

// Conecta el Web Report Designer de DevExpress con el mismo almacenamiento que ya usan
// TemplatesController/ReportsController (GoogleDriveService) — no es un storage nuevo, es el
// mismo, para que un templateKey editado aquí sea el mismo archivo que ya sirve /api/reports/generate.
// El tenant/plataforma viene del ApiKeyMiddleware (HttpContext.Items["Tenant"]), igual que en los
// demás controladores — el Designer llama a estos endpoints con el mismo header X-API-Key.
public class CustomReportStorageWebExtension : ReportStorageWebExtension
{
    private readonly GoogleDriveService _driveService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CustomReportStorageWebExtension> _logger;

    public CustomReportStorageWebExtension(GoogleDriveService driveService, IHttpContextAccessor httpContextAccessor, ILogger<CustomReportStorageWebExtension> logger)
    {
        _driveService = driveService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private TenantConfig CurrentTenant =>
        _httpContextAccessor.HttpContext?.Items["Tenant"] as TenantConfig
        ?? throw new FaultException("No se pudo determinar la plataforma actual.");

    public override bool IsValidUrl(string url)
    {
        // Mismas reglas de nombre que ya usa el resto del servicio para un templateKey.
        return !string.IsNullOrWhiteSpace(url) && !url.Contains("..") && !url.Any(char.IsWhiteSpace);
    }

    public override bool CanSetData(string url) => true;

    public override byte[] GetData(string url)
    {
        try
        {
            var bytes = _driveService.GetTemplateAsync(CurrentTenant, url).GetAwaiter().GetResult();
            if (bytes == null)
            {
                throw new FaultException($"No se encontró la plantilla '{url}'.");
            }
            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar la plantilla '{Url}' para el Report Designer", url);
            throw;
        }
    }

    public override void SetData(XtraReport report, string url)
    {
        using var ms = new MemoryStream();
        report.SaveLayoutToXml(ms);
        _driveService.UploadTemplate(CurrentTenant, url, ms.ToArray()).GetAwaiter().GetResult();
    }

    public override string SetNewData(XtraReport report, string defaultUrl)
    {
        SetData(report, defaultUrl);
        return defaultUrl;
    }

    public override Dictionary<string, string> GetUrls()
    {
        try
        {
            var templates = _driveService.ListTemplates(CurrentTenant).GetAwaiter().GetResult();
            return templates
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.Name)
                .ToDictionary(g => g.Key, g => g.Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al listar plantillas para el Report Designer");
            throw;
        }
    }
}
