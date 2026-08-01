using System.Text;
using System.Text.Json;
using FacilReports.Models;

namespace FacilReports.Services;

/// <summary>
/// Resolves platforms by API key.
/// Primary source: Core (Supabase) via the get-platform-reporting-config edge function.
/// Fallback: appsettings.json "Platforms" section and environment variables.
/// Uses platform_slug as the stable identifier (tenant_slug can change during testing).
/// Results are cached in memory.
/// </summary>
public class PlatformResolver
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<PlatformResolver> _logger;
    private readonly Dictionary<string, TenantConfig> _platforms;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeSpan _cacheTtl;

    public PlatformResolver(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<PlatformResolver> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _config = config;
        _logger = logger;
        _platforms = new Dictionary<string, TenantConfig>(StringComparer.Ordinal);
        _cacheTtl = config.GetValue<TimeSpan?>("Platforms:CacheTtl") ?? TimeSpan.FromMinutes(10);

        LoadStaticConfig(config);
    }

    /// <summary>
    /// Loads platforms from appsettings.json (Platforms section), legacy Tenants section
    /// and environment variables as a fallback when Core is unreachable.
    /// </summary>
    private void LoadStaticConfig(IConfiguration config)
    {
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
                    DriveFolderId = platformSection["DriveFolderId"] ?? "",
                    SupabaseUrl = platformSection["SupabaseUrl"] ?? "",
                    SupabaseKey = platformSection["SupabaseKey"] ?? "",
                    SupabaseServiceKey = platformSection["SupabaseServiceKey"] ?? ""
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
            if (!string.IsNullOrEmpty(envValue) && !_platforms.ContainsKey(envValue))
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

    /// <summary>
    /// Resolves a platform from an API key, checking cache first, then Core (edge function),
    /// then falling back to static config.
    /// </summary>
    public async Task<TenantConfig?> ResolveAsync(string apiKey)
    {
        // Fast path: cache / static config
        if (_platforms.TryGetValue(apiKey, out var cached))
        {
            return cached;
        }

        // Slow path: query Core
        var fromCore = await ResolveFromCoreAsync(apiKey);
        if (fromCore != null)
        {
            await _lock.WaitAsync();
            try
            {
                _platforms[apiKey] = fromCore;
            }
            finally
            {
                _lock.Release();
            }
            return fromCore;
        }

        return null;
    }

    /// <summary>
    /// Synchronous alias for compatibility (uses only cached/static data, no Core call).
    /// </summary>
    public TenantConfig? Resolve(string apiKey) =>
        _platforms.TryGetValue(apiKey, out var platform) ? platform : null;

    private async Task<TenantConfig?> ResolveFromCoreAsync(string apiKey)
    {
        var coreUrl = _config["Core:Url"];
        var coreSecret = _config["Core:ServiceSecret"];
        if (string.IsNullOrEmpty(coreUrl) || string.IsNullOrEmpty(coreSecret))
        {
            return null;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{coreUrl.TrimEnd('/')}/functions/v1/get-platform-reporting-config");
            request.Headers.Add("X-Service-Secret", coreSecret);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { apiKey }),
                Encoding.UTF8,
                "application/json"
            );

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Core config lookup failed with status {Status}", response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(body);
            if (!result.TryGetProperty("valid", out var valid) || !valid.GetBoolean())
            {
                return null;
            }

            return new TenantConfig
            {
                Id = result.GetProperty("platformId").GetString() ?? "",
                Name = result.GetProperty("platformName").GetString() ?? "",
                Slug = result.GetProperty("platformSlug").GetString() ?? "",
                ApiKey = apiKey,
                DriveFolderId = result.GetProperty("driveFolderId").GetString() ?? "",
                SupabaseUrl = result.GetProperty("supabaseUrl").GetString() ?? "",
                SupabaseKey = result.GetProperty("supabaseServiceKey").GetString() ?? "",
                SupabaseServiceKey = result.GetProperty("supabaseServiceKey").GetString() ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving platform config from Core");
            return null;
        }
    }

    public TenantConfig? GetBySlug(string slug) =>
        _platforms.Values.FirstOrDefault(p => p.Slug == slug);

    public TenantConfig? GetById(string platformId) =>
        _platforms.Values.FirstOrDefault(p => p.Id == platformId);

    public List<TenantConfig> GetAll() =>
        _platforms.Values.ToList();
}
