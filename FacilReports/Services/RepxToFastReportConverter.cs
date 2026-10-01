using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FastReport;
using FastReport.Data;
using FastReport.Utils;

namespace FacilReports.Services;

/// <summary>
/// Converts the supported subset of a DevExpress REPX into an in-memory FastReport document.
/// The REPX remains the source template; no FRX is persisted.
/// </summary>
public sealed class RepxToFastReportConverter
{
    private readonly ILogger<RepxToFastReportConverter>? _logger;

    public RepxToFastReportConverter(ILogger<RepxToFastReportConverter>? logger = null) => _logger = logger;

    private IReadOnlyDictionary<string, object?> _reportData = new Dictionary<string, object?>();
    private IReadOnlyDictionary<string, object?> _flat = new Dictionary<string, object?>();
    private bool _usesTotalPages;
    private (float Below, float Delta)[]? _activeShifts;
    private Report? _report;
    private ReportPage? _measurePage;
    private readonly Dictionary<XElement, float[]> _subreportHeights = new();

    // Una banda de datos (Items, Impuestos...) con las columnas calculadas que necesita.
    private sealed class DataBandInfo
    {
        public required string DataMember { get; init; }
        public required DataBand Band { get; init; }
        public required List<Dictionary<string, object?>> Rows { get; init; }
        public HashSet<string> RowKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Column, string Expression)> Computed { get; } = new();
        public int Counter;
    }

    private readonly List<DataBandInfo> _dataBands = new();
    private DataBandInfo? _ctxBand;

    public Report Convert(byte[] repxBytes, IReadOnlyDictionary<string, object?> data)
    {
        _reportData = data;
        _dataBands.Clear();
        _subreportHeights.Clear();
        _usesTotalPages = false;
        using var stream = new MemoryStream(repxBytes, writable: false);
        var root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("El REPX no contiene un documento XML.");

        if (!string.Equals((string?)root.Attribute("ReportUnit"), "TenthsOfAMillimeter", StringComparison.Ordinal))
            throw new NotSupportedException("El conversor FastReport solo soporta REPX en TenthsOfAMillimeter.");

        var report = new Report();
        _report = report;
        _measurePage = null;
        _flat = Flatten(data);
        var page = new ReportPage { Name = "Page1" };
        page.PaperWidth = Number(root, "PageWidth", 2159) / 10f;
        page.PaperHeight = Number(root, "PageHeight", 2794) / 10f;
        var margins = ((string?)root.Attribute("Margins") ?? "100,100,100,100")
            .Split(',').Select(value => ParseNumber(value)).ToArray();
        if (margins.Length == 4)
        {
            page.LeftMargin = margins[0] / 10f;
            page.RightMargin = margins[1] / 10f;
            page.TopMargin = margins[2] / 10f;
            page.BottomMargin = margins[3] / 10f;
        }
        // Tirilla (rollo de papel, p. ej. 80 mm): la hoja mide lo que mida el contenido.
        if (page.PaperWidth <= 100f) page.UnlimitedHeight = true;
        report.Pages.Add(page);
        page.TitleBeforeHeader = false;

        foreach (var band in root.Element("Bands")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var type = (string?)band.Attribute("ControlType");
            var height = Mm((string?)band.Attribute("HeightF"));
            var controls = band.Element("Controls");
            var bandName = (string?)band.Attribute("Name");
            switch (type)
            {
                case "ReportHeaderBand":
                    page.ReportTitle = new ReportTitleBand { Name = bandName ?? "ReportHeader", Height = height };
                    if (controls != null) page.ReportTitle.Height = height + AddBandControls(page.ReportTitle, controls);
                    break;
                case "PageHeaderBand":
                    page.PageHeader = new PageHeaderBand { Name = bandName ?? "PageHeader", Height = height };
                    if (controls != null) page.PageHeader.Height = height + AddBandControls(page.PageHeader, controls);
                    break;
                case "PageFooterBand":
                    page.PageFooter = new PageFooterBand { Name = bandName ?? "PageFooter", Height = height };
                    if (controls != null) page.PageFooter.Height = height + AddBandControls(page.PageFooter, controls);
                    break;
                case "ReportFooterBand":
                    // El pie del informe (totales, impuestos, QR...) se imprime al fondo de la
                    // última página y nunca se parte; si no cabe pasa entero a una página nueva.
                    page.ReportSummary = new ReportSummaryBand { Name = bandName ?? "ReportFooter", Height = height, PrintOnBottom = !page.UnlimitedHeight, KeepWithData = false, CanBreak = false };
                    if (controls != null) page.ReportSummary.Height = height + AddBandControls(page.ReportSummary, controls);
                    break;
                case "DetailBand":
                    // El Detail vacío (altura 0, sin controles) que DevExpress deja junto a un DetailReport no imprime nada.
                    if (height <= 0 && (controls == null || !controls.HasElements)) break;
                    AddDataBand(page, band, (string?)band.Attribute("DataMember") ?? "Items", root);
                    break;
                case "DetailReportBand":
                    var nested = band.Element("Bands")?.Elements()
                        .FirstOrDefault(x => (string?)x.Attribute("ControlType") == "DetailBand")
                        ?? throw new NotSupportedException($"DetailReportBand '{bandName}' no contiene un DetailBand interno.");
                    var header = band.Element("Bands")?.Elements()
                        .FirstOrDefault(x => (string?)x.Attribute("ControlType") == "GroupHeaderBand");
                    AddDataBand(page, nested, (string?)band.Attribute("DataMember") ?? "Items", root, header);
                    break;
                case "TopMarginBand":
                case "BottomMarginBand":
                    break;
                default:
                    throw new NotSupportedException($"La banda REPX '{type}' ('{bandName}') no está soportada por FastReport.");
            }
        }

        if (_measurePage != null) { report.Pages.Remove(_measurePage); _measurePage = null; }
        report.DoublePass = _usesTotalPages; // necesario para [TotalPages#]

        // Una tabla por banda de datos, con las columnas de las filas + las referenciadas + las calculadas.
        foreach (var info in _dataBands)
        {
            var table = new DataTable(info.DataMember);
            var fields = info.Rows.SelectMany(row => row.Keys)
                .Concat(FindFields(info.Band, info.DataMember))
                .Concat(info.Computed.Select(c => c.Column))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var field in fields) table.Columns.Add(field, typeof(string));
            foreach (var row in info.Rows)
            {
                var dataRow = table.NewRow();
                foreach (var field in fields)
                    dataRow[field] = row.TryGetValue(field, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
                foreach (var (column, expression) in info.Computed)
                    dataRow[column] = ExprEvaluator.EvaluateString(expression, Resolver(row, _flat));
                table.Rows.Add(dataRow);
            }
            report.RegisterData(table, info.DataMember);
            info.Band.DataSource = report.GetDataSource(info.DataMember);
            report.GetDataSource(info.DataMember)!.Enabled = true;
        }
        return report;
    }

    // ---- bandas --------------------------------------------------------------------------

    private float AddBandControls(BandBase band, XElement controls)
    {
        var shifts = ComputeShifts(controls);
        _activeShifts = shifts;
        AddControls(band, controls, false, 0, 0, _flat);
        _activeShifts = null;
        return shifts.Sum(s => s.Delta);
    }

    private void AddDataBand(ReportPage page, XElement bandElement, string dataMember, XElement root, XElement? headerElement = null)
    {
        var rows = GetRows(_reportData, dataMember).ToList();
        var band = new DataBand
        {
            Name = (string?)bandElement.Attribute("Name") ?? ("Detail" + dataMember),
            Height = Mm((string?)bandElement.Attribute("HeightF")),
            CanGrow = true
        };
        page.Bands.Add(band);
        var info = new DataBandInfo { DataMember = dataMember, Band = band, Rows = rows };
        foreach (var row in rows) foreach (var key in row.Keys) info.RowKeys.Add(key);
        _dataBands.Add(info);
        if (headerElement != null)
        {
            // GroupHeaderBand dentro de un DetailReportBand = encabezado de la lista (título + columnas);
            // en FastReport es el Header de la banda de datos y solo se imprime si hay filas.
            var hBand = new DataHeaderBand
            {
                Name = (string?)headerElement.Attribute("Name") ?? ("Header" + dataMember),
                Height = Mm((string?)headerElement.Attribute("HeightF")),
                KeepWithData = true
            };
            band.Header = hBand;
            if (headerElement.Element("Controls") is { } hc) AddControls(hBand, hc, false, 0, 0, _flat);
        }
        _ctxBand = info;
        if (bandElement.Element("Controls") is { } controls) AddControls(band, controls, true, 0, 0, _flat);
        _ctxBand = null;
    }

    // Los controles que quedan debajo de un XRSubreport se desplazan lo que este crece (o se
    // encoge) según las filas reales que traiga — así los totales nunca quedan montados sobre
    // una lista larga de retenciones, ni dejan un hueco cuando no hay ninguna.
    private (float Below, float Delta)[] ComputeShifts(XElement controls)
    {
        var list = new List<(float, float)>();
        foreach (var element in controls.Elements().Where(e => (string?)e.Attribute("ControlType") == "XRSubreport"))
        {
            var b = Bounds(element, 0, 0);
            list.Add((b.Bottom - 0.5f, SubreportRowHeights(element, b).Sum() - b.Height));
        }
        return list.ToArray();
    }

    private static (float RowHeight, string DataMember, XElement? Controls) SubreportInfo(XElement subreport, float fallbackHeight)
    {
        var source = subreport.Element("ReportSource");
        var inner = source?.Element("Bands")?.Elements().FirstOrDefault(x => (string?)x.Attribute("ControlType") == "DetailBand");
        var rowHeight = Mm((string?)inner?.Attribute("HeightF"));
        if (rowHeight <= 0) rowHeight = fallbackHeight;
        return (rowHeight, (string?)source?.Attribute("DataMember") ?? "Impuestos", inner?.Element("Controls"));
    }

    // ---- controles -----------------------------------------------------------------------

    private void AddControls(BandBase parent, XElement controls, bool detail, float offsetX, float offsetY, IReadOnlyDictionary<string, object?> flat, IReadOnlyDictionary<string, object?>? rowValues = null)
    {
        var shifts = _activeShifts; _activeShifts = null; // solo aplica a los controles de primer nivel de la banda
        var ctx = rowValues ?? flat;
        foreach (var element in controls.Elements())
        {
            var type = (string?)element.Attribute("ControlType");
            var top = Bounds(element, 0, 0).Top;
            var shift = shifts == null ? 0 : shifts.Where(s => s.Below <= top + 0.01f && type != "XRSubreport").Sum(s => s.Delta);
            // un subreport también baja si hay otro subreport por encima de él
            if (shifts != null && type == "XRSubreport") shift = shifts.Where(s => s.Below <= top + 0.01f && Math.Abs(s.Below - (Bounds(element, 0, 0).Bottom - 0.5f)) > 0.01f).Sum(s => s.Delta);
            var bounds = Bounds(element, offsetX, offsetY + shift);
            var name = (string?)element.Attribute("Name") ?? type ?? "Object";
            if (!detail && !IsVisible(element, ctx)) continue;
            switch (type)
            {
                case "XRLabel":
                    var text = new TextObject { Name = name, Bounds = bounds, CanGrow = true, GrowToBottom = detail, VertAlign = VertAlign.Top, ShiftMode = ShiftMode.Never };
                    SetText(text, element, "Text", detail, ctx);
                    ApplyStyle(text, element);
                    if (detail) text.VertAlign = VertAlign.Top;
                    ApplyForeColor(text, element, detail, ctx);
                    parent.Objects.Add(text);
                    break;
                case "XRPageInfo":
                    var pageInfo = new TextObject { Name = name, Bounds = bounds, CanGrow = false, VertAlign = VertAlign.Top };
                    var fmt = (string?)element.Attribute("TextFormatString") ?? "PÁGINA {0} / {1}";
                    _usesTotalPages |= fmt.Contains("{1}");
                    pageInfo.Text = fmt.Replace("{0}", "[Page#]").Replace("{1}", "[TotalPages#]");
                    ApplyStyle(pageInfo, element);
                    parent.Objects.Add(pageInfo);
                    break;
                case "XRLine":
                    var line = new LineObject { Name = name, Bounds = bounds, ShiftMode = ShiftMode.Never };
                    line.Border.Lines = BorderLines.Top;
                    if ((string?)element.Attribute("ForeColor") is { } lineColor) line.Border.Color = ParseColor(lineColor);
                    parent.Objects.Add(line);
                    break;
                case "XRPanel":
                    if (element.Element("Controls") is { } panelControls)
                        AddControls(parent, panelControls, detail, bounds.X, bounds.Y, flat, rowValues);
                    break;
                case "XRTable":
                    AddTable(parent, element, detail, bounds, flat, rowValues);
                    break;
                case "XRPictureBox":
                    AddPicture(parent, element, bounds, ctx);
                    break;
                case "XRSubreport":
                    AddSubreport(parent, element, bounds, flat);
                    break;
                default:
                    throw new NotSupportedException($"El control REPX '{type}' ('{name}') no está soportado por FastReport.");
            }
        }
    }

    private void AddSubreport(BandBase parent, XElement subreport, RectangleF bounds, IReadOnlyDictionary<string, object?> flat)
    {
        var (_, dataMember, controls) = SubreportInfo(subreport, bounds.Height);
        if (controls == null) return;
        var rows = GetRows(_reportData, dataMember).ToList();
        var heights = SubreportRowHeights(subreport, bounds);
        var y = bounds.Y;
        for (var index = 0; index < rows.Count; index++)
        {
            AddControls(parent, controls, false, bounds.X, y, RowValues(flat, rows[index]), RowValues(flat, rows[index]));
            y += heights[index];
        }
    }

    private static Dictionary<string, object?> RowValues(IReadOnlyDictionary<string, object?> flat, Dictionary<string, object?> row)
    {
        var values = flat.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in row) values[pair.Key] = pair.Value;
        return values;
    }

    // Alto real de cada fila del subreporte: lo que pida el texto más largo (una descripción que
    // se parte en dos renglones hace crecer la fila). Se mide con el propio motor de FastReport.
    private float[] SubreportRowHeights(XElement subreport, RectangleF bounds)
    {
        if (_subreportHeights.TryGetValue(subreport, out var cached)) return cached;
        var (rowHeight, dataMember, controls) = SubreportInfo(subreport, bounds.Height);
        var rows = GetRows(_reportData, dataMember).ToList();
        var heights = new float[rows.Count];
        for (var i = 0; i < rows.Count; i++) heights[i] = rowHeight;
        if (controls != null && rows.Count > 0 && _report != null)
        {
            _measurePage ??= new ReportPage { Name = "__measure" };
            if (!_report.Pages.Contains(_measurePage)) _report.Pages.Add(_measurePage);
            for (var i = 0; i < rows.Count; i++)
            {
                var temp = new ReportTitleBand { Name = "__row" + i, Height = rowHeight };
                _measurePage.ReportTitle = temp;
                var values = RowValues(_flat, rows[i]);
                var saved = _activeShifts; _activeShifts = null;
                AddControls(temp, controls, false, 0, 0, values, values);
                _activeShifts = saved;
                foreach (var text in temp.Objects.OfType<TextObject>())
                    heights[i] = Math.Max(heights[i], text.Top + text.CalcHeight() + (rowHeight - bounds.Height > 0 ? 0 : 3));
                _measurePage.ReportTitle = null;
            }
        }
        _subreportHeights[subreport] = heights;
        return heights;
    }

    private void AddPicture(BandBase parent, XElement element, RectangleF bounds, IReadOnlyDictionary<string, object?> ctx)
    {
        var expression = FindExpression(element, "ImageUrl");
        var source = expression == null ? string.Empty : ExprEvaluator.EvaluateString(expression, Resolver(ctx, null)).Trim();
        var picture = new PictureObject
        {
            Name = (string?)element.Attribute("Name") ?? "Picture",
            Bounds = bounds,
            ShowErrorImage = false,
            ShiftMode = ShiftMode.Never,
            SizeMode = (string?)element.Attribute("Sizing") switch
            {
                "ZoomImage" => System.Windows.Forms.PictureBoxSizeMode.Zoom,
                "StretchImage" => System.Windows.Forms.PictureBoxSizeMode.StretchImage,
                "AutoSize" => System.Windows.Forms.PictureBoxSizeMode.AutoSize,
                _ => System.Windows.Forms.PictureBoxSizeMode.Normal
            }
        };

        if (string.IsNullOrWhiteSpace(source))
        {
            picture.Visible = false;
        }
        else if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');
            if (comma < 0) throw new NotSupportedException("Imagen Base64 inválida en XRPictureBox.");
            picture.SetImageData(System.Convert.FromBase64String(source[(comma + 1)..]));
        }
        else if (TryQrData(source) is { } qrText)
        {
            // El QR se genera aquí (sin llamar a api.qrserver.com) y el exportador lo dibuja vectorial.
            picture.Tag = "qr:" + qrText;
        }
        else if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var bytes = DownloadImage(source);
            if (bytes == null) picture.Visible = false;
            else picture.SetImageData(bytes);
        }
        else
        {
            throw new NotSupportedException($"Origen de imagen no soportado: {source}");
        }

        parent.Objects.Add(picture);
    }

    // Detecta las URLs del servicio api.qrserver.com y devuelve el texto que codifican.
    private static string? TryQrData(string source)
    {
        if (!source.Contains("create-qr-code", StringComparison.OrdinalIgnoreCase)) return null;
        var i = source.IndexOf("data=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var data = source[(i + 5)..];
        var amp = data.IndexOf('&');
        if (amp >= 0) data = data[..amp];
        try { data = Uri.UnescapeDataString(data); } catch { }
        return data.Length == 0 ? null : data;
    }

    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) })
    { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, byte[]?> ImageCache = new();
    private const int MaxImageBytes = 5 * 1024 * 1024;

    // Descarga (con tope de tiempo, de tamaño y caché) una imagen remota, p. ej. el logo del emisor.
    // La URL llega en los datos que envía la plataforma, así que solo se permiten destinos públicos:
    // sin redirecciones y sin IPs privadas/loopback/link-local (evita usar el servicio para llegar a la red interna).
    private byte[]? DownloadImage(string url)
    {
        lock (ImageCache)
        {
            if (ImageCache.TryGetValue(url, out var cached)) return cached;
            byte[]? bytes = null;
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException("solo se permiten URLs http/https");
                if (!IsPublicHost(uri.Host)) throw new InvalidOperationException("destino no público");
                using var resp = Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentLength > MaxImageBytes) throw new InvalidOperationException("imagen demasiado grande");
                using var stream = resp.Content.ReadAsStream();
                using var ms = new MemoryStream();
                var buffer = new byte[81920]; int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                    if (ms.Length > MaxImageBytes) throw new InvalidOperationException("imagen demasiado grande");
                }
                bytes = ms.ToArray();
            }
            catch (Exception ex) { _logger?.LogWarning("No se pudo descargar la imagen {Url}: {Message}", url, ex.Message); }
            ImageCache[url] = bytes;
            return bytes;
        }
    }

    private static bool IsPublicHost(string host)
    {
        System.Net.IPAddress[] addresses;
        try { addresses = System.Net.IPAddress.TryParse(host, out var ip) ? new[] { ip } : System.Net.Dns.GetHostAddresses(host); }
        catch { return false; }
        if (addresses.Length == 0) return false;
        foreach (var a in addresses)
        {
            if (System.Net.IPAddress.IsLoopback(a)) return false;
            if (a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast) return false;
            if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var b = a.GetAddressBytes();
                if (b[0] == 10 || b[0] == 127 || b[0] == 0) return false;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
                if (b[0] == 192 && b[1] == 168) return false;
                if (b[0] == 169 && b[1] == 254) return false;
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
            }
            else if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                var b = a.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return false; // fc00::/7 (únicas locales)
            }
        }
        return true;
    }

    private void AddTable(BandBase parent, XElement table, bool detail, RectangleF bounds, IReadOnlyDictionary<string, object?> flat, IReadOnlyDictionary<string, object?>? rowValues = null)
    {
        var ctx = rowValues ?? flat;
        var rows = table.Element("Rows")?.Elements() ?? Enumerable.Empty<XElement>();
        var y = bounds.Y;
        foreach (var row in rows)
        {
            var cells = row.Element("Cells")?.Elements().Where(c => detail || IsVisible(c, ctx)).ToList() ?? new List<XElement>();
            var weights = cells.Select(c => Number(c, "Weight", 1)).ToArray();
            var total = weights.Sum();
            var x = bounds.X;
            foreach (var (cell, weight) in cells.Zip(weights))
            {
                var cellBounds = new RectangleF(x, y, total == 0 ? 0 : bounds.Width * weight / total, bounds.Height);
                var text = new TextObject { Name = (string?)cell.Attribute("Name") ?? "Cell", Bounds = cellBounds, CanGrow = detail, GrowToBottom = detail, VertAlign = VertAlign.Top, ShiftMode = ShiftMode.Never };
                text.Border.Lines = ParseBorders((string?)cell.Attribute("Borders") ?? (string?)table.Attribute("Borders"));
                SetText(text, cell, "Text", detail, ctx);
                ApplyStyle(text, cell);
                ApplyForeColor(text, cell, detail, ctx);
                // Política de alineación vertical: el detalle siempre arriba (las filas crecen hacia
                // abajo) y los títulos de columna siempre centrados, sin importar el TextAlignment
                // individual de cada celda.
                if (detail) text.VertAlign = VertAlign.Top;
                else if (((string?)table.Attribute("Name") ?? string.Empty).Contains("Header", StringComparison.OrdinalIgnoreCase))
                    text.VertAlign = VertAlign.Center;
                parent.Objects.Add(text);
                x += cellBounds.Width;
            }
            y += bounds.Height;
        }
    }

    // ---- texto y expresiones --------------------------------------------------------------

    private static readonly Regex SimpleExpr = new(@"^(\s*('([^']|'')*'|\[[^\]]+\])\s*)(\+\s*('([^']|'')*'|\[[^\]]+\])\s*)*$", RegexOptions.Compiled);

    private void SetText(TextObject text, XElement element, string property, bool detail, IReadOnlyDictionary<string, object?> ctx)
    {
        var expression = FindExpression(element, property);
        if (expression == null)
        {
            text.AllowExpressions = false;
            text.Text = (string?)element.Attribute(property) ?? string.Empty;
            return;
        }

        if (!detail || _ctxBand == null)
        {
            // Fuera de una banda de datos (o en una fila de subreporte) la expresión se evalúa ya con los datos.
            text.AllowExpressions = false;
            text.Text = ExprEvaluator.EvaluateString(expression, Resolver(ctx, null)).Trim();
            return;
        }

        // En una banda de datos: las concatenaciones simples de campos van como expresión nativa de
        // FastReport; todo lo demás (Iif, Len, comparaciones) se precalcula en una columna por fila.
        var info = _ctxBand;
        if (SimpleExpr.IsMatch(expression))
            text.Text = TranslateSimple(expression, info);
        else
        {
            var column = $"__e{++info.Counter}";
            info.Computed.Add((column, expression));
            text.Text = $"[{info.DataMember}.{column}]";
        }
    }

    private string TranslateSimple(string expression, DataBandInfo info)
    {
        var result = new StringBuilder();
        for (var i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '\'')
            {
                var end = i + 1;
                var sb = new StringBuilder();
                while (end < expression.Length)
                {
                    if (expression[end] == '\'') { if (end + 1 < expression.Length && expression[end + 1] == '\'') { sb.Append('\''); end += 2; continue; } break; }
                    sb.Append(expression[end]); end++;
                }
                result.Append(sb); i = end;
            }
            else if (expression[i] == '[')
            {
                var end = expression.IndexOf(']', i + 1);
                var field = StripPrefix(expression[(i + 1)..end]);
                if (info.RowKeys.Contains(field) || !_flat.ContainsKey(field)) result.Append('[').Append(info.DataMember).Append('.').Append(field).Append(']');
                else result.Append(_flat[field]?.ToString()); // dato del documento dentro de una fila de detalle
                i = end;
            }
        }
        return result.ToString();
    }

    // El ForeColor ligado a una expresión: fijo si no es de banda de datos; si lo es, una columna
    // calculada + una condición de resaltado por cada color distinto que aparezca en las filas.
    private void ApplyForeColor(TextObject text, XElement element, bool detail, IReadOnlyDictionary<string, object?> ctx)
    {
        var expression = FindExpression(element, "ForeColor");
        if (expression == null) return;
        if (!detail || _ctxBand == null)
        {
            text.TextColor = ParseColor(ExprEvaluator.EvaluateString(expression, Resolver(ctx, null)).Trim());
            return;
        }
        var info = _ctxBand;
        var column = $"__c{++info.Counter}";
        info.Computed.Add((column, expression));
        var colors = info.Rows
            .Select(row => ExprEvaluator.EvaluateString(expression, Resolver(row, _flat)).Trim())
            .Where(c => c.Length > 0).Distinct();
        foreach (var color in colors)
        {
            var condition = new HighlightCondition { Expression = $"[{info.DataMember}.{column}] == \"{color}\"", ApplyTextFill = true, TextFill = new SolidFill(ParseColor(color)) };
            text.Highlight.Add(condition);
        }
    }

    private bool IsVisible(XElement element, IReadOnlyDictionary<string, object?> ctx)
    {
        var expression = FindExpression(element, "Visible");
        return expression == null || ExprEvaluator.EvaluateBool(expression, Resolver(ctx, null));
    }

    private static string StripPrefix(string name)
    {
        name = name.Trim().TrimStart('?');
        foreach (var prefix in new[] { "Parameters.", "Documento.", "Parent.", "Items." })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return name[prefix.Length..];
        return name;
    }

    // Busca el campo en la fila (si hay) y luego en los datos planos del documento.
    private static Func<string, object?> Resolver(IReadOnlyDictionary<string, object?> primary, IReadOnlyDictionary<string, object?>? secondary) => name =>
    {
        var key = StripPrefix(name);
        foreach (var dict in new[] { primary, secondary })
        {
            if (dict == null) continue;
            if (dict.TryGetValue(key, out var value)) return value;
            foreach (var pair in dict) if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }
        return null;
    };

    // ---- estilo ----------------------------------------------------------------------------

    private static BorderLines ParseBorders(string? v)
    {
        if (string.IsNullOrWhiteSpace(v) || v.Trim() == "None") return BorderLines.None;
        if (v.Trim() == "All") return BorderLines.All;
        var r = BorderLines.None;
        foreach (var part in v.Split(',', ' ')) r |= part.Trim() switch { "Left" => BorderLines.Left, "Right" => BorderLines.Right, "Top" => BorderLines.Top, "Bottom" => BorderLines.Bottom, _ => BorderLines.None };
        return r;
    }

    private static RectangleF Bounds(XElement element, float xOffset, float yOffset)
    {
        var size = Pair((string?)element.Attribute("SizeF"));
        var location = Pair((string?)element.Attribute("LocationFloat"));
        return new RectangleF(Mm(location.x) + xOffset, Mm(location.y) + yOffset, Mm(size.x), Mm(size.y));
    }

    private static void ApplyStyle(TextObject text, XElement element)
    {
        if ((string?)element.Attribute("Font") is { } font) text.Font = ParseFont(font);
        if ((string?)element.Attribute("ForeColor") is { } fore) text.TextColor = ParseColor(fore);
        if ((string?)element.Attribute("BackColor") is { } back) text.FillColor = ParseColor(back);
        (text.HorzAlign, text.VertAlign) = ((string?)element.Attribute("TextAlignment")) switch
        {
            "TopLeft" => (HorzAlign.Left, VertAlign.Top), "TopCenter" => (HorzAlign.Center, VertAlign.Top), "TopRight" => (HorzAlign.Right, VertAlign.Top),
            "MiddleLeft" => (HorzAlign.Left, VertAlign.Center), "MiddleCenter" => (HorzAlign.Center, VertAlign.Center), "MiddleRight" => (HorzAlign.Right, VertAlign.Center),
            "BottomLeft" => (HorzAlign.Left, VertAlign.Bottom), "BottomCenter" => (HorzAlign.Center, VertAlign.Bottom), "BottomRight" => (HorzAlign.Right, VertAlign.Bottom),
            _ => (text.HorzAlign, text.VertAlign)
        };
    }

    private static string? FindExpression(XElement element, string property) =>
        element.Element("ExpressionBindings")?.Elements().FirstOrDefault(x => (string?)x.Attribute("PropertyName") == property)?.Attribute("Expression")?.Value;

    private static IEnumerable<string> FindFields(FastReport.Base root, string dataMember)
    {
        var pattern = $@"\[{Regex.Escape(dataMember)}\.(\w+)\]";
        foreach (var child in root.ChildObjects.OfType<FastReport.Base>())
        {
            if (child is TextObject text)
                foreach (Match match in Regex.Matches(text.Text ?? string.Empty, pattern)) yield return match.Groups[1].Value;
            foreach (var field in FindFields(child, dataMember)) yield return field;
        }
    }

    private static IEnumerable<Dictionary<string, object?>> GetRows(IReadOnlyDictionary<string, object?> data, string name)
    {
        if (!data.TryGetValue(name, out var items) || items == null) yield break;
        var token = Newtonsoft.Json.Linq.JToken.FromObject(items);
        foreach (var item in token.Children<Newtonsoft.Json.Linq.JObject>())
            yield return item.Properties().ToDictionary(p => p.Name, p => (object?)p.Value.ToObject<object>());
    }

    private static Dictionary<string, object?> Flatten(IReadOnlyDictionary<string, object?> data)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in data)
        {
            if (pair.Value is null) { result[pair.Key] = null; continue; }
            var token = Newtonsoft.Json.Linq.JToken.FromObject(pair.Value);
            if (token is Newtonsoft.Json.Linq.JObject obj)
                foreach (var nested in obj.Properties()) result[nested.Name] = nested.Value.ToObject<object>();
            else if (token is not Newtonsoft.Json.Linq.JArray) result[pair.Key] = token.ToObject<object>();
        }
        return result;
    }

    // REPX coordinates are tenths of a millimeter. FastReport page dimensions are
    // expressed in millimeters, while control coordinates/heights use its internal
    // pixel unit, so convert through Units.Millimeters for layout objects.
    private static float Mm(string? value) => ParseNumber(value) / 10f * Units.Millimeters;
    private static float Number(XElement e, string name, float fallback) => ParseNumber((string?)e.Attribute(name), fallback);
    private static float ParseNumber(string? value, float fallback = 0) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    private static (string? x, string? y) Pair(string? value) { var p = value?.Split(','); return (p?.ElementAtOrDefault(0), p?.ElementAtOrDefault(1)); }

    private static Font ParseFont(string value)
    {
        var p = value.Split(',').Select(x => x.Trim()).ToArray();
        var style = FontStyle.Regular;
        if (value.Contains("Bold")) style |= FontStyle.Bold;
        if (value.Contains("Italic")) style |= FontStyle.Italic;
        if (value.Contains("Underline")) style |= FontStyle.Underline;
        if (value.Contains("Strikeout")) style |= FontStyle.Strikeout;
        return new Font(p.ElementAtOrDefault(0) ?? "Arial", ParseNumber(p.ElementAtOrDefault(1)?.Replace("pt", ""), 8), style);
    }

    private static Color ParseColor(string value)
    {
        if (!value.Contains(','))
        {
            var named = Color.FromName(value.Trim());
            if (named.IsKnownColor || string.Equals(value.Trim(), "Transparent", StringComparison.OrdinalIgnoreCase))
                return named;
        }

        var p = value.Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return p.Length >= 4 ? Color.FromArgb(p[0], p[1], p[2], p[3]) : Color.FromArgb(p[0], p[1], p[2]);
    }
}
