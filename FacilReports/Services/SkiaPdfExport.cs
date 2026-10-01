using System.Drawing;
using System.Drawing.Imaging;
using FastReport;
using FastReport.Export;
using FastReport.Utils;
using SkiaSharp;

namespace FacilReports.Services;

/// Prototype: vector PDF exporter for FastReport OpenSource prepared pages (SkiaSharp).
public sealed class SkiaPdfExport : ExportBase
{
    private const float Px2Pt = 72f / 96f;
    private SKDocument? _doc;
    private SKCanvas? _canvas;
    private SKPictureRecorder? _recorder;
    private float _maxBottom, _pageW, _pageH;
    private bool _unlimited;
    private SKManagedWStream? _wstream;
    private float _ox, _oy;
    private readonly List<(string Name, SKRect Box)> _lines = new();
    private readonly List<(string Name, string Text, SKRect Box)> _texts = new();
    private int _pageNo;
    public List<string> Overlaps { get; } = new();

    protected override void Start()
    {
        base.Start();
        _wstream = new SKManagedWStream(Stream);
        _doc = SKDocument.CreatePdf(_wstream);
    }

    protected override void ExportPageBegin(ReportPage page)
    {
        base.ExportPageBegin(page);
        var w = page.PaperWidth * 72f / 25.4f;
        var h = page.PaperHeight * 72f / 25.4f;
        _unlimited = page.UnlimitedHeight || h > 14000;
        if (_unlimited) h = 14000;
        _pageW = w; _pageH = h; _maxBottom = 0;
        _recorder = new SKPictureRecorder();
        _canvas = _recorder.BeginRecording(new SKRect(0, 0, w, Math.Max(h, 14000)));
        _pageNo++; _lines.Clear(); _texts.Clear();
        _ox = page.LeftMargin * 96f / 25.4f; _oy = page.TopMargin * 96f / 25.4f;
    }

    protected override void ExportPageEnd(ReportPage page)
    {
        foreach (var l in _lines) foreach (var t in _texts)
            if (l.Box.Left < t.Box.Right - 1 && l.Box.Right > t.Box.Left + 1 && l.Box.MidY > t.Box.Top + 0.5f && l.Box.MidY < t.Box.Bottom - 0.5f)
                Overlaps.Add($"p{_pageNo}: línea '{l.Name}' cruza el texto '{t.Name}' ({t.Text.Replace('\n', ' ')[..Math.Min(30, t.Text.Replace('\n', ' ').Length)]})");
        for (var i = 0; i < _texts.Count; i++) for (var j = i + 1; j < _texts.Count; j++)
        {
            var a = _texts[i].Box; var b = _texts[j].Box;
            var ox = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left); var oy = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            if (ox > 3 && oy > 2.5f) Overlaps.Add($"p{_pageNo}: texto '{_texts[i].Name}' ({_texts[i].Text.Replace('\n', ' ')[..Math.Min(22, _texts[i].Text.Replace('\n', ' ').Length)]}) se monta con '{_texts[j].Name}' ({_texts[j].Text.Replace('\n', ' ')[..Math.Min(22, _texts[j].Text.Replace('\n', ' ').Length)]})");
        }
        using (var picture = _recorder!.EndRecording())
        {
            // Tirilla: la hoja mide lo que mida el contenido (+ margen inferior); el resto, su alto fijo.
            var hFinal = _unlimited ? Math.Max(50f, _maxBottom + page.BottomMargin * 72f / 25.4f) : _pageH;
            var target = _doc!.BeginPage(_pageW, hFinal);
            target.DrawPicture(picture);
            _doc.EndPage();
        }
        _recorder.Dispose(); _recorder = null;
        _canvas = null;
        base.ExportPageEnd(page);
    }

    protected override void ExportBand(BandBase band)
    {
        foreach (var obj in band.AllObjects)
            if (obj is ReportComponentBase c && c.Visible && c.Exportable && c is not BandBase)
                Draw(c);
    }

    protected override void Finish()
    {
        _doc!.Close();
        _wstream!.Flush();
        base.Finish();
    }

    // En las páginas preparadas el binario de la imagen vive en ImageData (interno) y no en Image.
    private static readonly System.Reflection.PropertyInfo? ImageDataProp = typeof(PictureObject).GetProperty("ImageData",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
    private static byte[]? PictureBytes(PictureObject pic) => ImageDataProp?.GetValue(pic) as byte[];

    // QR vectorial: cada módulo es un cuadrado, así sale nítido a cualquier tamaño.
    private void DrawQr(string text, SKRect rect)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        var m = data.ModuleMatrix;
        // la matriz trae 4 módulos de zona de silencio; se deja 1 dentro del recuadro de la plantilla
        const int trim = 3;
        var n = m.Count - 2 * trim;
        var side = Math.Min(rect.Width, rect.Height);
        var cell = side / n;
        var x0 = rect.MidX - side / 2; var y0 = rect.MidY - side / 2;
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.Black, IsAntialias = false };
        for (var r = 0; r < n; r++)
            for (var c = 0; c < n; c++)
                if (m[r + trim][c + trim])
                    _canvas!.DrawRect(x0 + c * cell, y0 + r * cell, cell + 0.02f, cell + 0.02f, paint);
    }

    private static SKColor Col(Color c) => new(c.R, c.G, c.B, c.A);

    private void Draw(ReportComponentBase o)
    {
        var cv = _canvas!;
        var rect = new SKRect((o.AbsLeft + _ox) * Px2Pt, (o.AbsTop + _oy) * Px2Pt, (o.AbsLeft + _ox + o.Width) * Px2Pt, (o.AbsTop + _oy + o.Height) * Px2Pt);

        _maxBottom = Math.Max(_maxBottom, rect.Bottom);
        if (o is LineObject)
        {
            using var lp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(0.5f, o.Border.Width * Px2Pt), Color = Col(o.Border.Color) };
            _lines.Add((o.Name, rect));
            var diag = (o as LineObject)!.Diagonal;
            if (diag) cv.DrawLine(rect.Left, rect.Top, rect.Right, rect.Bottom, lp);
            else if (rect.Width >= rect.Height) cv.DrawLine(rect.Left, rect.MidY, rect.Right, rect.MidY, lp);
            else cv.DrawLine(rect.MidX, rect.Top, rect.MidX, rect.Bottom, lp);
            return;
        }

        if (o.Fill is SolidFill sf && sf.Color.A > 0)
        {
            using var fp = new SKPaint { Style = SKPaintStyle.Fill, Color = Col(sf.Color) };
            cv.DrawRect(rect, fp);
        }

        if (o is PictureObject qr && qr.Tag is string tag && tag.StartsWith("qr:"))
        {
            DrawQr(tag[3..], rect);
        }
        else if (o is PictureObject pic && PictureBytes(pic) is { Length: > 0 } imageBytes)
        {
            using var bmp = SKBitmap.Decode(imageBytes);
            if (bmp != null)
            {
                var dest = rect;
                if (pic.SizeMode == System.Windows.Forms.PictureBoxSizeMode.Zoom)
                {
                    var scale = Math.Min(rect.Width / bmp.Width, rect.Height / bmp.Height);
                    var w = bmp.Width * scale; var h = bmp.Height * scale;
                    dest = new SKRect(rect.MidX - w / 2, rect.MidY - h / 2, rect.MidX + w / 2, rect.MidY + h / 2);
                }
                using var ip = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
                // Resolución útil de impresión: 300 ppp sobre el tamaño en el papel (los puntos son 1/72").
                var tw = (int)Math.Ceiling(dest.Width * 300f / 72f); var th = (int)Math.Ceiling(dest.Height * 300f / 72f);
                if (bmp.Width > tw * 1.25 && tw > 0 && th > 0)
                {
                    using var small = bmp.Resize(new SKImageInfo(tw, th), SKFilterQuality.High);
                    if (small != null) { cv.DrawBitmap(small, dest, ip); goto doneImage; }
                }
                cv.DrawBitmap(bmp, dest, ip);
                doneImage:;
            }
        }
        else if (o is TextObject t) DrawText(t, rect);

        DrawBorder(o, rect);
    }

    private void DrawBorder(ReportComponentBase o, SKRect r)
    {
        if (o.Border.Lines == BorderLines.None) return;
        using var p = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(0.4f, o.Border.Width * Px2Pt), Color = Col(o.Border.Color) };
        var cv = _canvas!;
        if (o.Border.Lines.HasFlag(BorderLines.Left)) cv.DrawLine(r.Left, r.Top, r.Left, r.Bottom, p);
        if (o.Border.Lines.HasFlag(BorderLines.Right)) cv.DrawLine(r.Right, r.Top, r.Right, r.Bottom, p);
        if (o.Border.Lines.HasFlag(BorderLines.Top)) cv.DrawLine(r.Left, r.Top, r.Right, r.Top, p);
        if (o.Border.Lines.HasFlag(BorderLines.Bottom)) cv.DrawLine(r.Left, r.Bottom, r.Right, r.Bottom, p);
    }

    private void DrawText(TextObject t, SKRect r)
    {
        var cv = _canvas!;
        var text = t.Text ?? string.Empty;
        if (text.Length == 0) return;
        var style = t.Font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var tf = SKTypeface.FromFamilyName(t.Font.Name, new SKFontStyle(style, SKFontStyleWidth.Normal, t.Font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright))
                 ?? SKTypeface.Default;
        using var font = new SKFont(tf, t.Font.Size * 96f / 96f) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        using var paint = new SKPaint { IsAntialias = true, Color = Col(t.TextColor) };
        var padL = t.Padding.Left * Px2Pt; var padR = t.Padding.Right * Px2Pt; var padT = t.Padding.Top * Px2Pt;
        var maxW = Math.Max(1, r.Width - padL - padR);
        using var paintM = new SKPaint(font);
        var lines = Wrap(text, font, maxW);
        var lh = font.Spacing;
        // FastReport mide con GDI+ y aquí se dibuja con Skia: si el texto sale en más renglones de los que
        // caben en la caja que calculó FastReport, se le da hasta un 8 % de ancho antes de aceptar el desborde.
        for (var f = 1.02f; f <= 1.08f && lines.Count * lh > r.Height - padT + lh * 0.3f && lines.Count > 1; f += 0.02f)
            lines = Wrap(text, font, maxW * f);
        var total = lines.Count * lh;
        var y = t.VertAlign switch
        {
            VertAlign.Center => r.Top + (r.Height - total) / 2,
            VertAlign.Bottom => r.Bottom - total - padT,
            _ => r.Top + padT
        };
        _texts.Add((t.Name, text, new SKRect(r.Left, y, r.Right, y + total)));
        foreach (var line in lines)
        {
            var w = paintM.MeasureText(line);
            var x = t.HorzAlign switch
            {
                HorzAlign.Center => r.Left + padL + (maxW - w) / 2,
                HorzAlign.Right => r.Right - padR - w,
                _ => r.Left + padL
            };
            cv.DrawText(line, x, y - font.Metrics.Ascent, font, paint);
            y += lh;
        }
    }

    private static List<string> Wrap(string text, SKFont font, float maxW)
    {
        using var paintM = new SKPaint(font);
        var res = new List<string>();
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var cur = "";
            foreach (var word0 in para.Split(' '))
            {
                var word = word0;
                // palabra más ancha que la caja (p. ej. un CUFE): se parte por caracteres
                while (paintM.MeasureText(word) > maxW && word.Length > 1)
                {
                    if (cur.Length > 0) { res.Add(cur); cur = ""; }
                    var n = 1;
                    while (n < word.Length && paintM.MeasureText(word[..(n + 1)]) <= maxW) n++;
                    res.Add(word[..n]); word = word[n..];
                }
                var cand = cur.Length == 0 ? word : cur + " " + word;
                if (paintM.MeasureText(cand) <= maxW || cur.Length == 0) cur = cand;
                else { res.Add(cur); cur = word; }
            }
            res.Add(cur);
        }
        return res;
    }
}
