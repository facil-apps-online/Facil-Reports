using System.Security.Cryptography;
using System.Text;

namespace FacilReports.Services;

/// <summary>
/// Service to generate and manage API keys for the Reporting API
/// Keys follow the pattern: {platformSlug}_live_{random12hex}
/// Using platform_slug because it's stable (tenant_slug can change during testing)
/// </summary>
public class ApiKeyGenerator
{
    private readonly IConfiguration _config;
    private readonly ILogger<ApiKeyGenerator> _logger;
    private readonly Dictionary<string, string> _generatedKeys = new();

    public ApiKeyGenerator(IConfiguration config, ILogger<ApiKeyGenerator> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Generate a new API key for a platform
    /// Format: {platformSlug}_live_{random12hex}
    /// </summary>
    public string GenerateApiKey(string platformSlug)
    {
        var prefix = _config.GetValue<string>($"Platforms:{platformSlug}:ApiKeyPrefix") ?? $"{platformSlug}_live_";
        var randomBytes = new byte[12];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var randomHex = BitConverter.ToString(randomBytes).Replace("-", "").ToLowerInvariant();
        var apiKey = $"{prefix}{randomHex}";

        // Store in memory (in production, this would be stored in a database)
        _generatedKeys[apiKey] = platformSlug;

        _logger.LogInformation("Generated API key for platform {Platform}: {Prefix}...", platformSlug, apiKey[..Math.Min(20, apiKey.Length)]);

        return apiKey;
    }

    /// <summary>
    /// Validate an API key and return the platform slug
    /// </summary>
    public string? ValidateApiKey(string apiKey)
    {
        // First check static keys from configuration
        var platformsSection = _config.GetSection("Platforms");
        foreach (var platform in platformsSection.GetChildren())
        {
            var configKey = platform["ApiKey"];
            if (configKey == apiKey)
            {
                return platform.Key;
            }
        }

        // Then check dynamically generated keys
        if (_generatedKeys.TryGetValue(apiKey, out var platformSlug))
        {
            return platformSlug;
        }

        return null;
    }

    /// <summary>
    /// Revoke an API key
    /// </summary>
    public bool RevokeApiKey(string apiKey)
    {
        return _generatedKeys.Remove(apiKey);
    }

    /// <summary>
    /// Get all active keys (for admin display)
    /// </summary>
    public Dictionary<string, string> GetActiveKeys()
    {
        return new Dictionary<string, string>(_generatedKeys);
    }

    /// <summary>
    /// Generate a display-safe version of the key (first 8 chars + ...)
    /// </summary>
    public static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            return "****";
        
        return $"{apiKey[..8]}...{apiKey[^4..]}";
    }
}
