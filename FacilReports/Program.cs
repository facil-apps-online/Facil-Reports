using FacilReports.Services;
using FacilReports.Middleware;
using DevExpress.AspNetCore;
using DevExpress.AspNetCore.Reporting;
using DevExpress.XtraReports.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services
// AddMvc (no el AddControllers más liviano de solo-API) — DevExpress Reporting depende de piezas
// de MVC completo para descubrir/enrutar sus controladores del Designer; con AddControllers a
// secas, las rutas DXXRD/* nunca se registran (404 aunque el controlador exista).
// AddNewtonsoftJson es obligatorio para DevExpress Reporting: el modelo del Report Designer
// (culturas, tipos polimórficos de bandas/controles) se serializa con converters propios de
// Json.NET — sin esto, System.Text.Json produce JSON corrupto (ej. CultureInfo serializado como
// arreglos vacíos anidados en vez del nombre de cultura) y el cliente falla con
// "Unexpected end of JSON input" al intentar parsear la respuesta.
builder.Services.AddMvc().AddNewtonsoftJson();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<GoogleDriveService>();
builder.Services.AddHttpContextAccessor();

// Custom services
builder.Services.AddSingleton<PlatformResolver>();
builder.Services.AddSingleton<ApiKeyGenerator>();
builder.Services.AddScoped<GoogleDriveService>();
builder.Services.AddScoped<ReportGenerator>();
builder.Services.AddTransient<RepxToFastReportConverter>();
builder.Services.AddScoped<FastReportGenerator>();

// DevExpress Web Report Designer — el mismo motor que ya usa ReportGenerator para exportar PDF,
// expuesto ahora también en modo edición interactiva. CustomReportStorageWebExtension reutiliza
// GoogleDriveService (el mismo almacenamiento que ya usan TemplatesController/ReportsController),
// no un storage nuevo.
builder.Services.AddDevExpressControls();
builder.Services.AddScoped<ReportStorageWebExtension, CustomReportStorageWebExtension>();
builder.Services.ConfigureReportingServices(configurator =>
{
    // DIAG temporal — detecta mismatch de versión entre los paquetes npm del cliente y el NuGet
    // del servidor (recomendado por DevExpress para diagnosticar el 500 vacío del Designer).
    configurator.UseDevelopmentMode();
    configurator.ConfigureReportDesigner(designerConfigurator => { });
    // El verificador de DevExpress exige que el Viewer también quede configurado aunque solo
    // usemos el Designer (sin esto, MapControllers lanza ReportingConfigurationException al
    // arrancar) — no se expone ninguna pantalla de Viewer nueva, solo satisface la validación.
    configurator.ConfigureWebDocumentViewer(viewerConfigurator =>
    {
        viewerConfigurator.UseCachedReportSourceBuilder();
    });
});

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

// UseRouting explícito antes de CORS/DevExpressControls — el orden importa: sin esto, el hosting
// mínimo inserta el enrutamiento implícitamente en el punto de MapControllers (al final), y el
// middleware de DevExpress corre antes de que exista una ruta que atender, devolviendo 404 siempre.
app.UseRouting();
app.UseCors("FacilApps");
app.UseMiddleware<ApiKeyMiddleware>();

// DIAG temporal — loguea cualquier excepción no capturada Y el cuerpo real de cualquier
// respuesta de error, por si DevExpress la captura internamente sin relanzarla.
app.Use(async (context, next) =>
{
    var originalBody = context.Response.Body;
    using var buffer = new MemoryStream();
    context.Response.Body = buffer;
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "DIAG Excepción no manejada en {Path}", context.Request.Path);
        throw;
    }
    finally
    {
        buffer.Seek(0, SeekOrigin.Begin);
        if (context.Response.StatusCode >= 400)
        {
            var text = await new StreamReader(buffer).ReadToEndAsync();
            app.Logger.LogError("DIAG Respuesta {Status} en {Path}: {Body}", context.Response.StatusCode, context.Request.Path, text);
            buffer.Seek(0, SeekOrigin.Begin);
        }
        await buffer.CopyToAsync(originalBody);
        context.Response.Body = originalBody;
    }
});

app.UseDevExpressControls();
app.MapControllers();

// Los controladores de DevExpress (CustomReportDesignerController/CustomWebDocumentViewerController)
// se resuelven por ruta CONVENCIONAL ({controller}/{action}), no por atributo — sin esto, MapControllers
// por sí solo nunca los expone y toda petición al Designer devuelve 404. Nuestros propios
// controladores (TemplatesController, etc.) siguen funcionando igual por sus rutas de atributo.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action=Index}/{id?}");

app.Run();
