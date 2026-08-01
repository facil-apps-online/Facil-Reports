using System.Text;
using System.Text.Json;
using FacilReports.Models;

namespace FacilReports.Services;

/// <summary>
/// Template storage with a local vault cache per platform plus Google Drive
/// as source of truth.
///
/// Vault layout: {Storage:VaultRoot}/{platform_slug}/templates/{template_key}.repx
///
/// Drive access goes through Core edge functions (google-drive-upload /
/// google-drive-download / google-drive-delete) which resolve the platform's
/// system_owner Google Drive credentials from tenant_integrations, so Facil
/// Reports never handles Google credentials itself.
/// </summary>
public class GoogleDriveService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleDriveService> _logger;
    private readonly string _vaultRoot;

    public GoogleDriveService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<GoogleDriveService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _vaultRoot = config["Storage:VaultRoot"] ?? "Reports/vault";
    }

    private string GetVaultPath(TenantConfig platform, string templateKey)
    {
        var slug = SanitizeSegment(platform.Slug);
        var key = SanitizeSegment(templateKey);
        return Path.Combine(_vaultRoot, slug, "templates", $"{key}.repx");
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private string CoreUrl => (_config["Core:Url"] ?? "").TrimEnd('/');

    private void EnsureVaultDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Loads a template for rendering. Tries the local vault first; if missing
    /// and a Drive fileId is provided, downloads from Drive and caches it.
    /// </summary>
    public async Task<byte[]?> GetTemplateAsync(TenantConfig platform, string templateKey, string? fileId = null)
    {
        var vaultPath = GetVaultPath(platform, templateKey);

        // 1. Local vault (fast path, no network)
        if (File.Exists(vaultPath))
        {
            _logger.LogInformation("Template '{TemplateKey}' served from local vault", templateKey);
            return await File.ReadAllBytesAsync(vaultPath);
        }

        // 2. Fallback to Drive by fileId, then cache in the vault
        if (!string.IsNullOrEmpty(fileId))
        {
            var bytes = await DownloadFromDriveAsync(platform, fileId);
            if (bytes != null)
            {
                await SaveToVaultAsync(vaultPath, bytes);
                return bytes;
            }
        }

        _logger.LogWarning("Template '{TemplateKey}' not found in vault or Drive", templateKey);
        return null;
    }

    /// <summary>
    /// Uploads a template: saves to the local vault and mirrors to Google Drive
    /// (Drive keeps the single backup / source of truth).
    /// </summary>
    public async Task<string?> UploadTemplate(TenantConfig platform, string templateKey, byte[] repxBytes)
    {
        var vaultPath = GetVaultPath(platform, templateKey);
        await SaveToVaultAsync(vaultPath, repxBytes);

        var fileId = await UploadToDriveAsync(platform, templateKey, repxBytes);
        return fileId;
    }

    private async Task SaveToVaultAsync(string vaultPath, byte[] bytes)
    {
        EnsureVaultDirectory(vaultPath);
        await File.WriteAllBytesAsync(vaultPath, bytes);
    }

    private async Task<byte[]?> DownloadFromDriveAsync(TenantConfig platform, string fileId)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { fileId, platformId = platform.Id });
            var response = await CallCoreAsync("google-drive-download", payload);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Drive download failed with status {Status}: {Body}", response.StatusCode, errorBody);
                return null;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(
                await response.Content.ReadAsStringAsync()
            );

            if (!result.TryGetProperty("fileBase64", out var base64Prop))
            {
                _logger.LogError("Drive download response missing fileBase64");
                return null;
            }

            return Convert.FromBase64String(base64Prop.GetString() ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download template from Drive");
            return null;
        }
    }

    private async Task<string?> UploadToDriveAsync(TenantConfig platform, string templateKey, byte[] repxBytes)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                platform_id = platform.Id,
                fileBase64 = Convert.ToBase64String(repxBytes),
                mimeType = "application/xml",
                fileName = $"{templateKey}.repx",
                path_components = new[] { "templates" },
                tenantId = platform.Slug
            });

            var response = await CallCoreAsync("google-drive-upload", payload);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Drive upload failed with status {Status}", response.StatusCode);
                return null;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(
                await response.Content.ReadAsStringAsync()
            );

            if (result.TryGetProperty("fileId", out var fileIdProp))
            {
                return fileIdProp.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload template to Drive");
            return null;
        }
    }

    /// <summary>
    /// Lists templates from the local vault and Google Drive (source of truth).
    /// </summary>
    public async Task<List<TemplateInfo>> ListTemplates(TenantConfig platform)
    {
        var templates = new List<TemplateInfo>();

        // Local vault
        var slug = SanitizeSegment(platform.Slug);
        var templatesDir = Path.Combine(_vaultRoot, slug, "templates");
        if (Directory.Exists(templatesDir))
        {
            foreach (var file in Directory.GetFiles(templatesDir, "*.repx"))
            {
                var info = new FileInfo(file);
                templates.Add(new TemplateInfo
                {
                    Id = "",
                    Name = Path.GetFileNameWithoutExtension(file),
                    CreatedAt = info.CreationTimeUtc.ToString("O"),
                    ModifiedAt = info.LastWriteTimeUtc.ToString("O")
                });
            }
        }

        // Google Drive
        var driveTemplates = await ListFromDriveAsync(platform);
        foreach (var dt in driveTemplates)
        {
            if (templates.All(t => t.Name != dt.Name))
            {
                templates.Add(dt);
            }
        }

        return templates;
    }

    private async Task<List<TemplateInfo>> ListFromDriveAsync(TenantConfig platform)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { platformId = platform.Id });
            var response = await CallCoreAsync("google-drive-list", payload);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Drive list failed with status {Status}: {Body}", response.StatusCode, errorBody);
                return new List<TemplateInfo>();
            }

            var result = JsonSerializer.Deserialize<JsonElement>(
                await response.Content.ReadAsStringAsync()
            );

            if (!result.TryGetProperty("files", out var files))
            {
                return new List<TemplateInfo>();
            }

            var list = new List<TemplateInfo>();
            foreach (var file in files.EnumerateArray())
            {
                list.Add(new TemplateInfo
                {
                    Id = file.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name = file.TryGetProperty("name", out var name) ? name.GetString()?.Replace(".repx", "") ?? "" : "",
                    CreatedAt = file.TryGetProperty("createdTime", out var ct) ? ct.GetString() ?? "" : "",
                    ModifiedAt = file.TryGetProperty("modifiedTime", out var mt) ? mt.GetString() ?? "" : ""
                });
            }

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list templates from Drive");
            return new List<TemplateInfo>();
        }
    }

    /// <summary>
    /// Deletes a template from the local vault and Google Drive.
    /// </summary>
    public async Task DeleteTemplate(TenantConfig platform, string templateKey, string? fileId = null)
    {
        // Local vault
        var vaultPath = GetVaultPath(platform, templateKey);
        if (File.Exists(vaultPath))
        {
            File.Delete(vaultPath);
            _logger.LogInformation("Template '{TemplateKey}' removed from local vault", templateKey);
        }

        // Google Drive
        if (!string.IsNullOrEmpty(fileId))
        {
            await DeleteFromDriveAsync(platform, fileId);
        }
    }

    private async Task DeleteFromDriveAsync(TenantConfig platform, string fileId)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { fileId, platform_id = platform.Id });
            var response = await CallCoreAsync("google-drive-delete", payload);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Drive delete failed with status {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete template from Drive");
        }
    }

    private async Task<HttpResponseMessage> CallCoreAsync(string function, string jsonPayload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{CoreUrl}/functions/v1/{function}");

        // Gateway auth (public anon key) + service secret for the function itself
        var anonKey = _config["Core:AnonKey"] ?? "";
        request.Headers.Add("apikey", anonKey);
        request.Headers.Add("Authorization", $"Bearer {anonKey}");
        request.Headers.Add("X-Service-Secret", _config["Core:ServiceSecret"] ?? "");

        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await _httpClient.SendAsync(request, cts.Token);
    }
}

public class TemplateInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string ModifiedAt { get; set; } = "";
}
