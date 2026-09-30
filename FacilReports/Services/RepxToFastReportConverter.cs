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
/// Converts the supported, layout-only subset of a DevExpress REPX into an in-memory
/// FastReport document. The REPX remains the source template; no FRX is persisted.
/// </summary>
public sealed class RepxToFastReportConverter
{
    private IReadOnlyDictionary<string, object?> _reportData = new Dictionary<string, object?>();

    public Report Convert(byte[] repxBytes, IReadOnlyDictionary<string, object?> data)
    {
        _reportData = data;
        using var stream = new MemoryStream(repxBytes, writable: false);
        var root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("El REPX no contiene un documento XML.");

        if (!string.Equals((string?)root.Attribute("ReportUnit"), "TenthsOfAMillimeter", StringComparison.Ordinal))
            throw new NotSupportedException("El conversor FastReport solo soporta REPX en TenthsOfAMillimeter.");

        var report = new Report();
        var flat = Flatten(data);
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
        report.Pages.Add(page);

        foreach (var parameter in root.Element("Parameters")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var name = (string?)parameter.Attribute("Name");
            if (!string.IsNullOrWhiteSpace(name))
                report.Parameters.Add(new Parameter { Name = name, DataType = typeof(string) });
        }

        DataBand? detail = null;
        foreach (var band in root.Element("Bands")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var type = (string?)band.Attribute("ControlType");
            var height = Mm((string?)band.Attribute("HeightF"));
            var controls = band.Element("Controls");
            switch (type)
            {
                case "ReportHeaderBand":
                    page.ReportTitle = new ReportTitleBand { Name = (string?)band.Attribute("Name") ?? "ReportHeader", Height = height };
                    if (controls != null) AddControls(page.ReportTitle, controls, false, 0, 0, flat);
                    break;
                case "DetailBand":
                    detail = new DataBand { Name = (string?)band.Attribute("Name") ?? "Detail", Height = height, CanGrow = true };
                    page.Bands.Add(detail);
                    if (controls != null) AddControls(detail, controls, true, 0, 0, flat);
                    break;
                case "DetailReportBand":
                    // DevExpress suele envolver las líneas en un DetailReportBand con un
                    // DetailBand interno. FastReport representa esa misma relación con un
                    // DataBand conectado a la tabla Items.
                    var nestedDetail = band.Element("Bands")?.Elements()
                        .FirstOrDefault(x => (string?)x.Attribute("ControlType") == "DetailBand");
                    if (nestedDetail == null)
                        throw new NotSupportedException("DetailReportBand no contiene un DetailBand interno.");

                    detail = new DataBand
                    {
                        Name = (string?)nestedDetail.Attribute("Name") ?? "Detail",
                        Height = Mm((string?)nestedDetail.Attribute("HeightF")),
                        CanGrow = true
                    };
                    page.Bands.Add(detail);
                    if (nestedDetail.Element("Controls") is { } nestedControls)
                        AddControls(detail, nestedControls, true, 0, 0, flat);
                    break;
                case "ReportFooterBand":
                    page.ReportSummary = new ReportSummaryBand { Name = (string?)band.Attribute("Name") ?? "ReportFooter", Height = height };
                    if (controls != null) AddControls(page.ReportSummary, controls, false, 0, 0, flat);
                    break;
                case "TopMarginBand":
                case "BottomMarginBand":
                    break;
                default:
                    throw new NotSupportedException($"La banda REPX '{type}' no está soportada por FastReport.");
            }
        }

        foreach (Parameter parameter in report.Parameters)
            if (flat.TryGetValue(parameter.Name, out var value))
                report.SetParameterValue(parameter.Name, value?.ToString() ?? string.Empty);

        if (detail != null)
        {
            var rows = GetRows(data, "Items");
            var table = new DataTable("Items");
            // Register every field present in the invoice rows. Relying only on the
            // translated expressions misses cells whose REPX expression is nested or
            // absent, leaving the DataTable with no columns and blank detail rows.
            var rowList = rows.ToList();
            var fields = rowList
                .SelectMany(row => row.Keys)
                .Concat(FindFields(detail))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var field in fields) table.Columns.Add(field, typeof(string));
            foreach (var row in rowList)
            {
                var dataRow = table.NewRow();
                foreach (var field in fields)
                    dataRow[field] = row.TryGetValue(field, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
                table.Rows.Add(dataRow);
            }
            report.RegisterData(table, "Items");
            detail.DataSource = report.GetDataSource("Items");
            report.GetDataSource("Items")!.Enabled = true;
        }
        return report;
    }

    private void AddControls(BandBase parent, XElement controls, bool detail, float offsetX, float offsetY, IReadOnlyDictionary<string, object?> flat, IReadOnlyDictionary<string, object?>? rowValues = null)
    {
        foreach (var element in controls.Elements())
        {
            var type = (string?)element.Attribute("ControlType");
            var bounds = Bounds(element, offsetX, offsetY);
            var name = (string?)element.Attribute("Name") ?? type ?? "Object";
            switch (type)
            {
                case "XRLabel":
                    var text = new TextObject { Name = name, Bounds = bounds, CanGrow = true, GrowToBottom = detail, AutoShrink = AutoShrinkMode.FontSize, VertAlign = VertAlign.Top };
                    var textExpression = FindExpression(element, "Text");
                    text.Text = rowValues != null && textExpression != null
                        ? EvaluateScalar(textExpression, rowValues)
                        : textExpression != null && !detail
                            ? EvaluateScalar(textExpression, flat)
                            : Translate(textExpression ?? (string?)element.Attribute("Text") ?? string.Empty, detail);
                    ApplyStyle(text, element);
                    parent.Objects.Add(text);
                    break;
                case "XRLine":
                    var line = new LineObject { Name = name, Bounds = bounds };
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
                    AddPicture(parent, element, bounds, flat);
                    break;
                case "XRSubreport":
                    AddSubreport(parent, element, bounds, flat);
                    break;
                default:
                    throw new NotSupportedException($"El control REPX '{type}' no está soportado por FastReport.");
            }
        }
    }

    private void AddSubreport(BandBase parent, XElement subreport, RectangleF bounds, IReadOnlyDictionary<string, object?> flat)
    {
        var controls = subreport.Element("ReportSource")?.Element("Bands")?.Elements()
            .FirstOrDefault(x => (string?)x.Attribute("ControlType") == "DetailBand")?.Element("Controls");
        if (controls == null) return;

        var rows = GetRows(_reportData, "Impuestos").ToList();
        var rowHeight = Mm((string?)subreport.Element("ReportSource")?.Element("Bands")?.Elements()
            .FirstOrDefault(x => (string?)x.Attribute("ControlType") == "DetailBand")?.Attribute("HeightF"));
        if (rowHeight <= 0) rowHeight = bounds.Height;

        for (var index = 0; index < rows.Count; index++)
        {
            var values = flat.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows[index]) values[row.Key] = row.Value;
            AddControls(parent, controls, false, bounds.X, bounds.Y + index * rowHeight, values, values);
        }
    }

    private static void AddPicture(BandBase parent, XElement element, RectangleF bounds, IReadOnlyDictionary<string, object?> flat)
    {
        var expression = FindExpression(element, "ImageUrl");
        var source = EvaluateScalar(expression, flat);
        var picture = new PictureObject
        {
            Name = (string?)element.Attribute("Name") ?? "Picture",
            Bounds = bounds,
            ShowErrorImage = false
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
        else if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            picture.ImageLocation = source;
            picture.LoadImage();
        }
        else
        {
            throw new NotSupportedException($"Origen de imagen no soportado: {source}");
        }

        parent.Objects.Add(picture);
    }

    private void AddTable(BandBase parent, XElement table, bool detail, RectangleF bounds, IReadOnlyDictionary<string, object?> flat, IReadOnlyDictionary<string, object?>? rowValues = null)
    {
        var rows = table.Element("Rows")?.Elements() ?? Enumerable.Empty<XElement>();
        var y = bounds.Y;
        foreach (var row in rows)
        {
            var cells = row.Element("Cells")?.Elements().ToList() ?? new List<XElement>();
            var weights = cells.Select(c => Number(c, "Weight", 1)).ToArray();
            var total = weights.Sum();
            var x = bounds.X;
            foreach (var (cell, weight) in cells.Zip(weights))
            {
                var cellBounds = new RectangleF(x, y, total == 0 ? 0 : bounds.Width * weight / total, bounds.Height);
                var text = new TextObject { Name = (string?)cell.Attribute("Name") ?? "Cell", Bounds = cellBounds, CanGrow = detail, GrowToBottom = detail, AutoShrink = AutoShrinkMode.FontSize, VertAlign = VertAlign.Top, Border = { Lines = BorderLines.All } };
                var textExpression = FindExpression(cell, "Text");
                text.Text = rowValues != null && textExpression != null
                    ? EvaluateScalar(textExpression, rowValues)
                    : textExpression != null && !detail
                        ? EvaluateScalar(textExpression, flat)
                        : Translate(textExpression ?? (string?)cell.Attribute("Text") ?? string.Empty, detail);
                ApplyStyle(text, cell);
                parent.Objects.Add(text);
                x += cellBounds.Width;
            }
            y += bounds.Height;
        }
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
            "TopCenter" => (HorzAlign.Center, VertAlign.Top), "TopRight" => (HorzAlign.Right, VertAlign.Top),
            "MiddleCenter" => (HorzAlign.Center, VertAlign.Center), "MiddleRight" => (HorzAlign.Right, VertAlign.Center),
            "MiddleLeft" => (HorzAlign.Left, VertAlign.Center), _ => (text.HorzAlign, text.VertAlign)
        };
    }

    private static string Translate(string expression, bool detail)
    {
        var value = expression.Replace("&amp;", "&").Replace("&#xA;", "\n");
        var result = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\'')
            {
                var end = value.IndexOf('\'', i + 1);
                if (end < 0) break;
                result.Append(value[(i + 1)..end]); i = end;
            }
            else if (value[i] == '[')
            {
                var end = value.IndexOf(']', i + 1);
                if (end < 0) { result.Append(value[i]); continue; }
                var field = value[(i + 1)..end];
                if (field.StartsWith("Parameters.", StringComparison.Ordinal)) field = field[11..];
                // The invoice payload exposes Documento's properties as flat FastReport
                // fields. REPX expressions still qualify them as [Documento.Campo].
                if (field.StartsWith("Documento.", StringComparison.OrdinalIgnoreCase))
                    field = field["Documento.".Length..];
                result.Append('[').Append(detail ? "Items." : string.Empty).Append(field).Append(']'); i = end;
            }
            else if (value[i] != '+') result.Append(value[i]);
        }
        return result.ToString();
    }

    private static string? FindExpression(XElement element, string property) => element.Element("ExpressionBindings")?.Elements().FirstOrDefault(x => (string?)x.Attribute("PropertyName") == property)?.Attribute("Expression")?.Value;
    private static string EvaluateScalar(string? expression, IReadOnlyDictionary<string, object?> values)
    {
        if (string.IsNullOrWhiteSpace(expression)) return string.Empty;
        var value = expression.Replace("&amp;", "&").Replace("&gt;", ">" ).Replace("&lt;", "<");
        var result = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\'')
            {
                var end = value.IndexOf('\'', i + 1);
                if (end < 0) break;
                result.Append(value[(i + 1)..end]); i = end;
            }
            else if (value[i] == '[')
            {
                var end = value.IndexOf(']', i + 1);
                if (end < 0) { result.Append(value[i]); continue; }
                var key = value[(i + 1)..end];
                if (key.StartsWith("Parameters.", StringComparison.Ordinal)) key = key[11..];
                if (key.StartsWith("Documento.", StringComparison.OrdinalIgnoreCase)) key = key["Documento.".Length..];
                if (values.TryGetValue(key, out var item)) result.Append(item?.ToString());
                i = end;
            }
            else if (value[i] != '+') result.Append(value[i]);
        }
        return result.ToString().Trim();
    }
    private static IEnumerable<string> FindFields(FastReport.Base root)
    {
        foreach (var child in root.ChildObjects.OfType<FastReport.Base>())
        {
            if (child is TextObject text)
                foreach (Match match in Regex.Matches(text.Text ?? string.Empty, @"\[Items\.(\w+)\]")) yield return match.Groups[1].Value;
            foreach (var field in FindFields(child)) yield return field;
        }
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> GetRows(IReadOnlyDictionary<string, object?> data, string name)
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
    private static Font ParseFont(string value) { var p = value.Split(',').Select(x => x.Trim()).ToArray(); var style = value.Contains("style=Bold") ? FontStyle.Bold : FontStyle.Regular; return new Font(p.ElementAtOrDefault(0) ?? "Arial", ParseNumber(p.ElementAtOrDefault(1)?.Replace("pt", ""), 8), style); }
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
