using FacilReports.Services;

namespace FacilReports.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, PlatformResolver platformResolver)
    {
        // Skip auth for health checks and apikey endpoints
        if (context.Request.Path.StartsWithSegments("/api/health") ||
            context.Request.Path.StartsWithSegments("/api/apikey"))
        {
            await _next(context);
            return;
        }

        // Check for API key in header
        if (!context.Request.Headers.TryGetValue("X-API-Key", out var apiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "API key required",
                message = "Include X-API-Key header in your request"
            });
            return;
        }

        // Resolve platform from API key
        var platform = await platformResolver.ResolveAsync(apiKey!);
        if (platform == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Invalid API key",
                message = "The provided API key is not valid"
            });
            return;
        }

        // Store platform in HttpContext for controllers
        context.Items["Tenant"] = platform;
        await _next(context);
    }
}
