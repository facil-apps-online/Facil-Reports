using FacilReports.Models;

namespace FacilReports.Services;

/// <summary>
/// Resolves platforms by API key.
/// Uses platform_slug as the stable identifier (tenant_slug can change during testing).
/// </summary>
public class PlatformResolver
{
    private readonly Dictionary<string, TenantConfig> _platforms;

    public PlatformResolver(IConfiguration config)
    {
        _platforms = new Dictionary<string, TenantConfig>();

        // Load from appsettings.json (Platforms section)
        var platformsSection = config.GetSection("Platforms");
        foreach (var platformSection in platformsSection.GetChildren())
        {
            var id = platformSection["Id"];
            var apiKey = platformSection["ApiKey"];
            
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(apiKey))
            {
                _platforms[apiKey] = new TenantConfig
                {
                    Id = id,
                    Name = platformSection["Name"] ?? "",
                    Slug = platformSection.Key,
                    ApiKey = apiKey,
                    DriveFolderId = platformSection["DriveFolderId"] ?? ""
                };
            }
        }

        // Also support the old "Tenants" section for backward compatibility
        var tenantsSection = config.GetSection("Tenants");
        foreach (var tenantSection in tenantsSection.GetChildren())
        {
            var id = tenantSection["Id"];
            var apiKey = tenantSection["ApiKey"];
            
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(apiKey) && !_platforms.ContainsKey(apiKey))
            {
                _platforms[apiKey] = new TenantConfig
                {
                    Id = id,
                    Name = tenantSection["Name"] ?? "",
                    Slug = tenantSection.Key,
                    ApiKey = apiKey,
                    DriveFolderId = tenantSection["DriveFolderId"] ?? ""
                };
            }
        }

        // Override with environment variables if present
        var envKeys = new Dictionary<string, string>
        {
            { "GLAMTICA_API_KEY", "glamtica" },
            { "TATTOOSUITE_API_KEY", "tattoosuite" },
            { "NEXU_API_KEY", "nexu" },
            { "FACULFACTURA_API_KEY", "faculfactura" }
        };

        foreach (var envKey in envKeys)
        {
            var envValue = config[envKey.Key];
            if (!string.IsNullOrEmpty(envValue))
            {
                var platformSlug = envKey.Value;
                
                _platforms[envValue] = new TenantConfig
                {
                    Id = $"{platformSlug}-001",
                    Name = char.ToUpper(platformSlug[0]) + platformSlug[1..],
                    Slug = platformSlug,
                    ApiKey = envValue,
                    DriveFolderId = ""
                };
            }
        }
    }

    public TenantConfig? Resolve(string apiKey) =>
        _platforms.TryGetValue(apiKey, out var platform) ? platform : null;

    public TenantConfig? GetBySlug(string slug) =>
        _platforms.Values.FirstOrDefault(p => p.Slug == slug);

    public TenantConfig? GetById(string platformId) =>
        _platforms.Values.FirstOrDefault(p => p.Id == platformId);

    public List<TenantConfig> GetAll() =>
        _platforms.Values.ToList();
}
