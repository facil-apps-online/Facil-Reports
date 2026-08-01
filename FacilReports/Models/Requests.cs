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
}
