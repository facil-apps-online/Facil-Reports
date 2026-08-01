namespace FacilReports.Models;

public class TenantConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DriveFolderId { get; set; } = "";
    public string SupabaseUrl { get; set; } = "";
    public string SupabaseKey { get; set; } = "";
    public string SupabaseServiceKey { get; set; } = "";
}
