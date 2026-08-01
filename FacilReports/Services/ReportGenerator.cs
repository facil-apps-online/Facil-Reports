using DevExpress.XtraReports.UI;
using FacilReports.Models;
using System.Text.Json;

namespace FacilReports.Services;

public class ReportGenerator
{
    private readonly GoogleDriveService _driveService;
    private readonly ILogger<ReportGenerator> _logger;

    public ReportGenerator(
        GoogleDriveService driveService,
        ILogger<ReportGenerator> logger)
    {
        _driveService = driveService;
        _logger = logger;
    }

    /// <summary>
    /// Generate a PDF from a template and JSON data
    /// </summary>
    public async Task<byte[]> GenerateFromJson(
        Models.TenantConfig tenant,
        string templateKey,
        Dictionary<string, object> data)
    {
        // 1. Load template from Google Drive
        var repxBytes = await _driveService.DownloadTemplate(tenant, templateKey);
        if (repxBytes == null)
            throw new FileNotFoundException($"Template '{templateKey}' not found in Google Drive");

        // 2. Load the XtraReport from bytes
        var report = new XtraReport();
        using (var ms = new MemoryStream(repxBytes))
        {
            report.LoadLayout(ms);
        }

        // 3. Apply data to report parameters or data source
        ApplyData(report, data);

        // 4. Export to PDF
        using var pdfStream = new MemoryStream();
        report.ExportToPdf(pdfStream);
        return pdfStream.ToArray();
    }

    /// <summary>
    /// Apply JSON data to the report
    /// Supports both parameters and data sources
    /// </summary>
    private void ApplyData(XtraReport report, Dictionary<string, object> data)
    {
        // Flatten nested objects for parameter binding
        var flatData = FlattenDictionary(data);

        // Apply to report parameters
        foreach (var param in report.Parameters)
        {
            if (flatData.TryGetValue(param.Name, out var value))
            {
                param.Value = ConvertValue(value, param.Type);
            }
        }

        // If report has a data source, set it
        if (data.ContainsKey("DataSource"))
        {
            report.DataSource = data["DataSource"];
        }
    }

    /// <summary>
    /// Flatten nested dictionary (e.g., "empleado.nombre" -> "Nombre")
    /// </summary>
    private Dictionary<string, object> FlattenDictionary(
        Dictionary<string, object> dict,
        string prefix = "")
    {
        var result = new Dictionary<string, object>();

        foreach (var kvp in dict)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

            if (kvp.Value is JsonElement jsonElement)
            {
                switch (jsonElement.ValueKind)
                {
                    case JsonValueKind.Object:
                        var nested = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            jsonElement.GetRawText()
                        );
                        if (nested != null)
                        {
                            foreach (var nestedKvp in FlattenDictionary(nested, key))
                            {
                                result[nestedKvp.Key] = nestedKvp.Value;
                            }
                        }
                        break;
                    case JsonValueKind.Array:
                        result[key] = jsonElement.GetRawText();
                        break;
                    default:
                        result[key] = jsonElement.ToString();
                        break;
                }
            }
            else if (kvp.Value is Dictionary<string, object> nestedDict)
            {
                foreach (var nestedKvp in FlattenDictionary(nestedDict, key))
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else
            {
                result[key] = kvp.Value;
            }
        }

        return result;
    }

    private object ConvertValue(object value, Type targetType)
    {
        try
        {
            if (value is JsonElement jsonElement)
            {
                return targetType switch
                {
                    Type t when t == typeof(string) => jsonElement.GetString() ?? "",
                    Type t when t == typeof(int) => jsonElement.GetInt32(),
                    Type t when t == typeof(long) => jsonElement.GetInt64(),
                    Type t when t == typeof(decimal) => jsonElement.GetDecimal(),
                    Type t when t == typeof(double) => jsonElement.GetDouble(),
                    Type t when t == typeof(bool) => jsonElement.GetBoolean(),
                    Type t when t == typeof(DateTime) => jsonElement.GetDateTime(),
                    _ => jsonElement.ToString() ?? ""
                };
            }

            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value?.ToString() ?? "";
        }
    }
}
