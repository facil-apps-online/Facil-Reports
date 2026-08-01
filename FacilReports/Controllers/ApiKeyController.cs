using FacilReports.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacilReports.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApiKeyController : ControllerBase
{
    private readonly ApiKeyGenerator _keyGenerator;
    private readonly IConfiguration _config;
    private readonly ILogger<ApiKeyController> _logger;

    public ApiKeyController(
        ApiKeyGenerator keyGenerator,
        IConfiguration config,
        ILogger<ApiKeyController> logger)
    {
        _keyGenerator = keyGenerator;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Generate a new API key for a platform
    /// POST /api/apikey/generate
    /// </summary>
    [HttpPost("generate")]
    public IActionResult GenerateKey([FromBody] GenerateKeyRequest request)
    {
        // Validate admin secret
        var adminSecret = Request.Headers["X-Admin-Secret"].FirstOrDefault();
        var expectedSecret = _config["AdminSecret"];
        
        if (string.IsNullOrEmpty(adminSecret) || adminSecret != expectedSecret)
        {
            return Unauthorized(new { error = "Invalid admin secret" });
        }

        if (string.IsNullOrEmpty(request.PlatformSlug))
        {
            return BadRequest(new { error = "platformSlug is required" });
        }

        var apiKey = _keyGenerator.GenerateApiKey(request.PlatformSlug);

        return Ok(new
        {
            success = true,
            apiKey,
            platformSlug = request.PlatformSlug,
            apiUrl = _config["AppUrl"] ?? "https://reports.facil-apps.online",
            message = "Save this key securely. It will not be shown again."
        });
    }

    /// <summary>
    /// Validate an API key
    /// GET /api/apikey/validate
    /// </summary>
    [HttpGet("validate")]
    public IActionResult ValidateKey()
    {
        var apiKey = Request.Headers["X-API-Key"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(apiKey))
        {
            return Ok(new { valid = false, error = "No API key provided" });
        }

        var platformSlug = _keyGenerator.ValidateApiKey(apiKey);
        
        if (platformSlug == null)
        {
            return Ok(new { valid = false, error = "Invalid API key" });
        }

        var platformsSection = _config.GetSection($"Platforms:{platformSlug}");

        return Ok(new
        {
            valid = true,
            platformSlug,
            platformId = platformsSection["Id"],
            platformName = platformsSection["Name"]
        });
    }

    /// <summary>
    /// List all active API keys (admin only)
    /// GET /api/apikey/list
    /// </summary>
    [HttpGet("list")]
    public IActionResult ListKeys()
    {
        var adminSecret = Request.Headers["X-Admin-Secret"].FirstOrDefault();
        var expectedSecret = _config["AdminSecret"];
        
        if (string.IsNullOrEmpty(adminSecret) || adminSecret != expectedSecret)
        {
            return Unauthorized(new { error = "Invalid admin secret" });
        }

        var keys = _keyGenerator.GetActiveKeys();
        var platformsSection = _config.GetSection("Platforms");

        var result = keys.Select(k => new
        {
            apiKeyPrefix = ApiKeyGenerator.MaskApiKey(k.Key),
            platformSlug = k.Value,
            platformName = platformsSection[$"{k.Value}:Name"]
        }).ToList();

        // Also include static keys from config
        foreach (var platform in platformsSection.GetChildren())
        {
            var staticKey = platform["ApiKey"];
            if (staticKey != null && !keys.ContainsKey(staticKey))
            {
                result.Add(new
                {
                    apiKeyPrefix = ApiKeyGenerator.MaskApiKey(staticKey),
                    platformSlug = platform.Key,
                    platformName = platform["Name"]
                });
            }
        }

        return Ok(new { keys = result });
    }

    /// <summary>
    /// Revoke an API key
    /// DELETE /api/apikey
    /// </summary>
    [HttpDelete]
    public IActionResult RevokeKey([FromBody] RevokeKeyRequest request)
    {
        var adminSecret = Request.Headers["X-Admin-Secret"].FirstOrDefault();
        var expectedSecret = _config["AdminSecret"];
        
        if (string.IsNullOrEmpty(adminSecret) || adminSecret != expectedSecret)
        {
            return Unauthorized(new { error = "Invalid admin secret" });
        }

        if (string.IsNullOrEmpty(request.ApiKey))
        {
            return BadRequest(new { error = "apiKey is required" });
        }

        var revoked = _keyGenerator.RevokeApiKey(request.ApiKey);

        return Ok(new
        {
            success = revoked,
            message = revoked ? "API key revoked" : "API key not found"
        });
    }
}

public class GenerateKeyRequest
{
    public string PlatformSlug { get; set; } = "";
}

public class RevokeKeyRequest
{
    public string ApiKey { get; set; } = "";
}
