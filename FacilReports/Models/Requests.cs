namespace FacilReports.Models;

public class SaveTemplateRequest
{
    public string TemplateKey { get; set; } = "";
    public string RepxBase64 { get; set; } = "";
    public string? FileId { get; set; }
    public string? Description { get; set; }
}

public class GenerateReportRequest
{
    public string TemplateKey { get; set; } = "";
    public string? FileId { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public bool? AsBase64 { get; set; } = false;

    /// <summary>
    /// Motor de impresión para esta petición: "fastreport" o "devexpress". Opcional — si no se envía
    /// se usa la configuración Rendering:Engine. Sirve para comparar la misma plantilla en ambos motores.
    /// </summary>
    public string? Engine { get; set; }
}
