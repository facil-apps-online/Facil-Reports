using DevExpress.DataAccess.Json;
using DevExpress.XtraReports.UI;
using FacilReports.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Linq;

namespace FacilReports.Services;

public class ReportGenerator
{
    private readonly GoogleDriveService _driveService;
    private readonly ILogger<ReportGenerator> _logger;

    public ReportGenerator(
        GoogleDriveService driveService,
        ILogger<ReportGenerator> logger)
    {
        _driveService = driveService;
        _logger = logger;
    }

    /// <summary>
    /// Generate a PDF from a template and JSON data.
    /// The template is loaded from the local vault or from Drive by fileId.
    /// </summary>
    public async Task<byte[]> GenerateFromJson(
        Models.TenantConfig tenant,
        string templateKey,
        Dictionary<string, object> data,
        string? fileId = null)
    {
        // 1. Load template (local vault first, then Drive by fileId)
        var repxBytes = await _driveService.GetTemplateAsync(tenant, templateKey, fileId);
        if (repxBytes == null)
            throw new FileNotFoundException($"Template '{templateKey}' not found");

        // 2. Load the XtraReport from bytes.
        // XtraReport.FromStream (no new XtraReport() + LoadLayout) — LoadLayout sobre una
        // instancia ya construida no restauraba el tamaño de página del .repx (PaperKind/
        // PageWidth/PageHeight), así que todo terminaba exportado a Carta estándar (612x792pt)
        // sin importar lo que declarara la plantilla: Media Carta salía en página completa y el
        // Tiquete POS a todo el ancho. FromStream reconstruye el reporte completo desde cero,
        // tamaño de página incluido.
        XtraReport report;
        using (var ms = new MemoryStream(repxBytes))
        {
            report = XtraReport.FromStream(ms);
        }

        // 3. Apply data to report parameters or data source
        ApplyData(report, data);

        // 3.5. Reforzar CanGrow/WordWrap/Multiline en código, no solo en el XML del .repx.
        // Según soporte de DevExpress, Controls[...] devuelve el tipo base XRControl — si algo en
        // el pipeline de deserialización (XtraReport.FromStream) no restaura bien estas
        // propiedades desde el XML, hay que castear al tipo real (XRLabel) y fijarlas explícitas.
        ForceGrowOnTextControls(report.Bands);

        // 4. Export to PDF
        // CreateDocument() explícito antes de exportar: sin esto, el cálculo de layout/crecimiento
        // (CanGrow, WordWrap) no se resuelve de forma confiable en Linux — ExportToPdf terminaba
        // exportando con los tamaños de diseño en vez de recalcular el crecimiento real del
        // contenido, así que las celdas con texto largo se veían cortadas en vez de crecer.
        report.CreateDocument();
        using var pdfStream = new MemoryStream();
        report.ExportToPdf(pdfStream);
        return pdfStream.ToArray();
    }

    /// <summary>
    /// Apply JSON data to the report
    /// Supports both parameters and data sources
    /// </summary>
    private void ApplyData(XtraReport report, Dictionary<string, object> data)
    {
        // Plantillas nuevas (por ahora: Factura/Nota Crédito/Nota Débito de Facil Factura) mandan
        // un único objeto con "Documento" (los campos de encabezado, antes repartidos en ~30
        // Parameters sueltos) más los arreglos anidados que hagan falta (Items, Impuestos, …) —
        // cada uno se resuelve solo como su propia tabla navegable dentro de un JsonDataSource, en
        // vez de tener que emparejar Parameters por nombre y limitarnos a un solo DataSource plano.
        // Las plantillas viejas (Documento Soporte, Nómina — sin tocar en esta migración) no traen
        // "Documento" y siguen exactamente el camino de abajo.
        if (data.ContainsKey("Documento"))
        {
            ApplyJsonDataSource(report, data);
            return;
        }

        // Flatten nested objects for parameter binding
        var flatData = FlattenDictionary(data);

        // Apply to report parameters
        foreach (var param in report.Parameters)
        {
            if (flatData.TryGetValue(param.Name, out var value))
            {
                param.Value = ConvertValue(value, param.Type);
            }
        }

        // Si el reporte trae una lista de filas (ej. las líneas de una factura), se asigna como
        // DataSource. AddNewtonsoftJson() (obligatorio para el Web Report Designer de DevExpress,
        // ver Program.cs) reemplaza los formatters de System.Text.Json en todo el pipeline de MVC
        // — así que los valores `object` de Data llegan como Newtonsoft.Json.Linq.JArray/JObject/
        // JValue, no como System.Text.Json.JsonElement. DevExpress tampoco acepta un JArray
        // directo como DataSource ("no implementa ninguna de las interfaces soportadas") — hay que
        // materializarlo primero a un DataTable, el tipo que XtraReport.DataSource sabe bindear
        // por nombre de columna.
        if (data.TryGetValue("DataSource", out var dataSourceValue))
        {
            report.DataSource = ConvertToDataTable(dataSourceValue);
        }
    }

    /// <summary>
    /// Recorre todas las bandas (incluidas las anidadas de un DetailReportBand, ej. "Items") y
    /// todos los controles (incluidos los anidados dentro de una celda de tabla) forzando
    /// CanGrow/WordWrap/Multiline explícitos en código — el .repx ya los trae en el XML, pero por
    /// sugerencia de soporte de DevExpress esto se refuerza acá por si la deserialización de
    /// XtraReport.FromStream no las restaura de forma confiable.
    /// </summary>
    private static void ForceGrowOnTextControls(BandCollection bands)
    {
        foreach (Band band in bands)
        {
            ForceGrowOnControls(band.Controls);
            if (band is DetailReportBand detailReportBand)
            {
                ForceGrowOnTextControls(detailReportBand.Bands);
            }
        }
    }

    private static void ForceGrowOnControls(XRControlCollection controls)
    {
        foreach (XRControl control in controls)
        {
            ForceGrowOnSingleControl(control);

            if (control.Controls.Count > 0)
            {
                ForceGrowOnControls(control.Controls);
            }

            if (control is XRTable table)
            {
                foreach (XRTableRow row in table.Rows)
                {
                    row.CanGrow = true;
                    foreach (XRTableCell cell in row.Cells)
                    {
                        ForceGrowOnSingleControl(cell);
                        if (cell.Controls.Count > 0)
                        {
                            ForceGrowOnControls(cell.Controls);
                        }
                    }
                }
            }
        }
    }

    private static void ForceGrowOnSingleControl(XRControl control)
    {
        control.CanGrow = true;

        if (control is XRLabel label)
        {
            label.WordWrap = true;
            label.Multiline = true;
        }
        else if (control is XRTableCell cell)
        {
            cell.WordWrap = true;
            cell.Multiline = true;
        }
    }

    /// <summary>
    /// Adjunta el payload completo como un JsonDataSource — cada rama del objeto raíz (un objeto
    /// simple como "Documento", o un arreglo como "Items"/"Impuestos") queda expuesta como su
    /// propia tabla, con las columnas que ya traiga cada fila. La plantilla decide, banda por
    /// banda, a cuál de esas tablas bindearse (DataMember) — acá solo se entrega la fuente
    /// completa, sin adivinar qué banda usa qué.
    /// </summary>
    private static void ApplyJsonDataSource(XtraReport report, Dictionary<string, object> data)
    {
        var json = JsonConvert.SerializeObject(data);
        var jsonDataSource = new JsonDataSource
        {
            JsonSource = new CustomJsonSource(json)
        };

        // Fill() DEBE llamarse antes de que cualquier banda con DataMember resuelva su tabla —
        // sin esto, el JsonDataSource nunca discovery su esquema y una DetailReportBand anidada
        // (ej. "Items", "Impuestos") queda con su DataMember "colgado" (sin fila detrás), aunque
        // el nombre esté bien escrito en el .repx. Verificado reproduciendo el pipeline completo
        // (guardar → recargar con XtraReport.FromStream → reasignar DataSource) con el motor real
        // de DevExpress: sin este Fill() el detalle no repite ninguna fila; con él, sí.
        jsonDataSource.Fill();
        report.DataSource = jsonDataSource;

        // Tampoco basta con asignar report.DataSource: una DetailReportBand bindeada a un
        // DataMember anidado guarda su propia referencia a la fuente de datos (DataSource) —
        // si esa banda no trae la suya propia asignada, el DataMember queda sin tabla real
        // detrás aunque report.DataSource sí esté bien puesto. Lo mismo aplica a un XRSubreport
        // (ej. el subreporte de "Impuestos" embebido dentro del ReportFooterBand): su
        // ReportSource es en sí mismo otro XtraReport con su propio DataMember/DataSource, que
        // hay que resolver igual — si no, el subreporte no repite ninguna fila.
        AssignDataSourceToDataMemberBands(report.Bands, jsonDataSource);
    }

    private static void AssignDataSourceToDataMemberBands(BandCollection bands, JsonDataSource dataSource)
    {
        foreach (Band band in bands)
        {
            if (band is DetailReportBand detailReportBand)
            {
                if (!string.IsNullOrEmpty(detailReportBand.DataMember))
                {
                    detailReportBand.DataSource = dataSource;
                }
                AssignDataSourceToDataMemberBands(detailReportBand.Bands, dataSource);
            }
            AssignDataSourceToSubreports(band.Controls, dataSource);
        }
    }

    private static void AssignDataSourceToSubreports(XRControlCollection controls, JsonDataSource dataSource)
    {
        foreach (XRControl control in controls)
        {
            if (control is XRSubreport { ReportSource: not null } subreport)
            {
                if (!string.IsNullOrEmpty(subreport.ReportSource.DataMember))
                {
                    subreport.ReportSource.DataSource = dataSource;
                }
                AssignDataSourceToDataMemberBands(subreport.ReportSource.Bands, dataSource);
            }
            if (control.Controls.Count > 0)
            {
                AssignDataSourceToSubreports(control.Controls, dataSource);
            }
        }
    }

    /// <summary>
    /// Convierte un arreglo JSON de objetos planos (una fila por objeto) en un DataTable, que
    /// XtraReport.DataSource sí acepta de forma nativa — las expresiones del .repx referencian
    /// columnas por nombre (ej. [Codigo]) igual que lo harían contra este DataTable.
    /// </summary>
    private static DataTable ConvertToDataTable(object dataSourceValue)
    {
        var table = new DataTable();

        if (dataSourceValue is not JArray array)
        {
            // DataSource vacío o de un tipo que no reconocemos: reporte sin filas en vez de un 500.
            return table;
        }

        var rows = array.OfType<JObject>().ToList();

        // Columnas = unión de las claves de todas las filas, en el orden en que aparecen — una
        // fila puede omitir una clave que sí trae otra (ej. un campo opcional en null).
        foreach (var row in rows)
        {
            foreach (var prop in row.Properties())
            {
                if (!table.Columns.Contains(prop.Name))
                {
                    table.Columns.Add(prop.Name, typeof(string));
                }
            }
        }

        foreach (var row in rows)
        {
            var newRow = table.NewRow();
            foreach (var prop in row.Properties())
            {
                newRow[prop.Name] = prop.Value.Type switch
                {
                    JTokenType.Null or JTokenType.Undefined => DBNull.Value,
                    _ => prop.Value.Value<string>() ?? (object)DBNull.Value
                };
            }
            table.Rows.Add(newRow);
        }

        return table;
    }

    /// <summary>
    /// Flatten nested dictionary (e.g., "empleado.nombre" -> "Nombre")
    /// </summary>
    private Dictionary<string, object> FlattenDictionary(
        Dictionary<string, object> dict,
        string prefix = "")
    {
        var result = new Dictionary<string, object>();

        foreach (var kvp in dict)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

            if (kvp.Value is JObject jObject)
            {
                var nested = jObject.Properties().ToDictionary(p => p.Name, p => (object)p.Value);
                foreach (var nestedKvp in FlattenDictionary(nested, key))
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else if (kvp.Value is JArray jArray)
            {
                result[key] = jArray.ToString(Newtonsoft.Json.Formatting.None);
            }
            else if (kvp.Value is Dictionary<string, object> nestedDict)
            {
                foreach (var nestedKvp in FlattenDictionary(nestedDict, key))
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else
            {
                result[key] = kvp.Value;
            }
        }

        return result;
    }

    private object ConvertValue(object value, Type targetType)
    {
        try
        {
            // JValue (Newtonsoft) implementa IConvertible, así que Convert.ChangeType ya lo
            // resuelve directamente sin necesidad de una rama aparte.
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value?.ToString() ?? "";
        }
    }
}
