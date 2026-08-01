using System.Text;
using System.Text.Json;

namespace FacilReports.Services;

/// <summary>
/// Service to interact with Google Drive via Supabase Edge Function
/// Uses the existing google-drive-upload edge function from Core
/// </summary>
public class GoogleDriveService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleDriveService> _logger;

    public GoogleDriveService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<GoogleDriveService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Upload a .repx template to Google Drive
    /// Path: {tenant_id}/templates/{template_key}.repx
    /// </summary>
    public async Task<string?> UploadTemplate(string tenantId, string templateKey, byte[] repxBytes)
    {
        var supabaseUrl = _config["Supabase:Url"];
        var supabaseKey = _config["Supabase:ServiceKey"];
        var platformId = _config["Supabase:PlatformId"];

        var fileBase64 = Convert.ToBase64String(repxBytes);
        var fileName = $"{templateKey}.repx";

        var payload = new
        {
            platform_id = platformId,
            fileBase64,
            mimeType = "application/xml",
            fileName,
            path_components = new[] { "templates" },
            tenantId
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("apikey", supabaseKey);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseKey}");

        var response = await _httpClient.PostAsync(
            $"{supabaseUrl}/functions/v1/google-drive-upload",
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to upload template to Google Drive: {Error}", error);
            throw new Exception($"Google Drive upload failed: {error}");
        }

        var result = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync()
        );

        return result.GetProperty("fileId").GetString();
    }

    /// <summary>
    /// Download a .repx template from Google Drive
    /// </summary>
    public async Task<byte[]?> DownloadTemplate(string tenantId, string templateKey)
    {
        var supabaseUrl = _config["Supabase:Url"];
        var supabaseKey = _config["Supabase:ServiceKey"];

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("apikey", supabaseKey);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseKey}");

        // Search for the file
        var fileName = $"{templateKey}.repx";
        var searchQuery = $"name = '{fileName}' and '{tenantId}/templates' in parents and trashed = false";
        var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(searchQuery)}&fields=files(id,name)&key={supabaseKey}";

        var searchResponse = await _httpClient.GetAsync(searchUrl);
        if (!searchResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to search for template in Google Drive");
            return null;
        }

        var searchResult = JsonSerializer.Deserialize<JsonElement>(
            await searchResponse.Content.ReadAsStringAsync()
        );

        var files = searchResult.GetProperty("files");
        if (files.GetArrayLength() == 0)
        {
            _logger.LogWarning("Template '{TemplateKey}' not found in Google Drive", templateKey);
            return null;
        }

        var fileId = files[0].GetProperty("id").GetString();

        // Download the file content
        var downloadUrl = $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media&key={supabaseKey}";
        var downloadResponse = await _httpClient.GetAsync(downloadUrl);
        if (!downloadResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to download template from Google Drive");
            return null;
        }

        return await downloadResponse.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// List all templates for a tenant
    /// </summary>
    public async Task<List<TemplateInfo>> ListTemplates(string tenantId)
    {
        var supabaseUrl = _config["Supabase:Url"];
        var supabaseKey = _config["Supabase:ServiceKey"];

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("apikey", supabaseKey);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseKey}");

        var searchQuery = $"mimeType = 'application/xml' and '{tenantId}/templates' in parents and trashed = false";
        var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(searchQuery)}&fields=files(id,name,createdTime,modifiedTime)&orderBy=name&key={supabaseKey}";

        var searchResponse = await _httpClient.GetAsync(searchUrl);
        if (!searchResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to list templates from Google Drive");
            return new List<TemplateInfo>();
        }

        var searchResult = JsonSerializer.Deserialize<JsonElement>(
            await searchResponse.Content.ReadAsStringAsync()
        );

        var files = searchResult.GetProperty("files");
        var templates = new List<TemplateInfo>();

        foreach (var file in files.EnumerateArray())
        {
            templates.Add(new TemplateInfo
            {
                Id = file.GetProperty("id").GetString() ?? "",
                Name = file.GetProperty("name").GetString()?.Replace(".repx", "") ?? "",
                CreatedAt = file.TryGetProperty("createdTime", out var ct) ? ct.GetString() ?? "" : "",
                ModifiedAt = file.TryGetProperty("modifiedTime", out var mt) ? mt.GetString() ?? "" : ""
            });
        }

        return templates;
    }

    /// <summary>
    /// Delete a template from Google Drive
    /// </summary>
    public async Task DeleteTemplate(string tenantId, string templateKey)
    {
        var supabaseUrl = _config["Supabase:Url"];
        var supabaseKey = _config["Supabase:ServiceKey"];

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("apikey", supabaseKey);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseKey}");

        var fileName = $"{templateKey}.repx";
        var searchQuery = $"name = '{fileName}' and '{tenantId}/templates' in parents and trashed = false";
        var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(searchQuery)}&fields=files(id)&key={supabaseKey}";

        var searchResponse = await _httpClient.GetAsync(searchUrl);
        if (!searchResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to search for template to delete");
            return;
        }

        var searchResult = JsonSerializer.Deserialize<JsonElement>(
            await searchResponse.Content.ReadAsStringAsync()
        );

        var files = searchResult.GetProperty("files");
        if (files.GetArrayLength() == 0)
        {
            _logger.LogWarning("Template '{TemplateKey}' not found for deletion", templateKey);
            return;
        }

        var fileId = files[0].GetProperty("id").GetString();

        // Delete the file
        var deleteUrl = $"https://www.googleapis.com/drive/v3/files/{fileId}?key={supabaseKey}";
        var deleteResponse = await _httpClient.DeleteAsync(deleteUrl);
        if (!deleteResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to delete template from Google Drive");
        }
    }
}

public class TemplateInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string ModifiedAt { get; set; } = "";
}
