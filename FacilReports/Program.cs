using FacilReports.Services;
using FacilReports.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<GoogleDriveService>();

// Custom services
builder.Services.AddSingleton<PlatformResolver>();
builder.Services.AddSingleton<ApiKeyGenerator>();
builder.Services.AddScoped<GoogleDriveService>();
builder.Services.AddScoped<ReportGenerator>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("FacilApps", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                      ?? new[] { "http://localhost:3000" };
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors("FacilApps");
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

app.Run();
