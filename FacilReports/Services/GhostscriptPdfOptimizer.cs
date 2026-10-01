using System.Diagnostics;

namespace FacilReports.Services;

/// <summary>
/// Pasa el PDF por Ghostscript para recortar las fuentes embebidas a los caracteres usados.
/// El exportador vectorial (Skia) incrusta cada fuente completa (~700 KB por tipografía); con esto una
/// factura baja de ~1 MB a ~150 KB sin pérdida visual (el texto sigue siendo texto y el QR sigue vectorial).
/// Si Ghostscript no está instalado o falla, devuelve el PDF original y lo deja registrado: el tamaño es
/// una optimización, no un requisito para que el documento sea correcto.
/// </summary>
public sealed class GhostscriptPdfOptimizer
{
    private readonly IConfiguration _config;
    private readonly ILogger<GhostscriptPdfOptimizer> _logger;

    public GhostscriptPdfOptimizer(IConfiguration config, ILogger<GhostscriptPdfOptimizer> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<byte[]> OptimizeAsync(byte[] pdf, CancellationToken ct = default)
    {
        if (!_config.GetValue("Rendering:CompressPdf", true)) return pdf;
        var gs = _config["Rendering:GhostscriptPath"] ?? "gs";
        var dir = Path.Combine(Path.GetTempPath(), "facilreports-gs");
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid().ToString("N");
        var input = Path.Combine(dir, id + ".in.pdf");
        var output = Path.Combine(dir, id + ".out.pdf");
        try
        {
            await File.WriteAllBytesAsync(input, pdf, ct);
            var psi = new ProcessStartInfo(gs)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (var arg in new[]
            {
                "-q", "-dBATCH", "-dNOPAUSE", "-dSAFER", "-sDEVICE=pdfwrite", "-dCompatibilityLevel=1.5",
                "-dSubsetFonts=true", "-dEmbedAllFonts=true", "-dCompressFonts=true",
                "-dDownsampleColorImages=false", "-dDownsampleGrayImages=false",
                "-dAutoFilterColorImages=false", "-dColorImageFilter=/FlateEncode",
                "-sOutputFile=" + output, input
            })
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("no se pudo iniciar Ghostscript");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0 || !File.Exists(output))
            {
                _logger.LogWarning("Ghostscript terminó con código {Code}; se entrega el PDF sin optimizar. {Error}", process.ExitCode, await stderr);
                return pdf;
            }
            var optimized = await File.ReadAllBytesAsync(output, ct);
            // Si por algo quedó más grande o vacío, se conserva el original.
            return optimized.Length > 0 && optimized.Length < pdf.Length ? optimized : pdf;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "No se pudo optimizar el PDF con Ghostscript ('{Path}'); se entrega sin optimizar.", gs);
            return pdf;
        }
        finally
        {
            try { File.Delete(input); } catch { }
            try { File.Delete(output); } catch { }
        }
    }
}
