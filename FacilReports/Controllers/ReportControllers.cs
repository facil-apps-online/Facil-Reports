using DevExpress.AspNetCore.Reporting.QueryBuilder;
using DevExpress.AspNetCore.Reporting.QueryBuilder.Native.Services;
using DevExpress.AspNetCore.Reporting.ReportDesigner;
using DevExpress.AspNetCore.Reporting.ReportDesigner.Native.Services;
using DevExpress.AspNetCore.Reporting.WebDocumentViewer;
using DevExpress.AspNetCore.Reporting.WebDocumentViewer.Native.Services;
using DevExpress.XtraReports.Web.ReportDesigner;
using Microsoft.AspNetCore.Mvc;

namespace FacilReports.Controllers;

// A partir de DevExpress Reporting, estas clases base son abstractas por seguridad — hay que
// declarar un descendiente explícito de cada una o la app no arranca (ReportingConfigurationException
// al iniciar). Ver: https://go.devexpress.com/Reporting_BC1019255.aspx
public class CustomWebDocumentViewerController : WebDocumentViewerController
{
    public CustomWebDocumentViewerController(IWebDocumentViewerMvcControllerService controllerService) : base(controllerService)
    {
    }
}

public class CustomReportDesignerController : ReportDesignerController
{
    public CustomReportDesignerController(IReportDesignerMvcControllerService controllerService) : base(controllerService)
    {
    }

    // Confirmado por soporte de DevExpress: esta acción es obligatoria en TODAS las versiones —
    // ReportDesignerController no trae un GetDesignerModel propio, porque el modelo inicial del
    // designer (qué reporte abrir, qué fuentes de datos exponer) solo puede definirse del lado del
    // servidor. Sin esta acción, /DXXRD/GetDesignerModel daba 404 y el designer nunca cargaba nada
    // (el POST a /DXXRD a secas sí "funcionaba" pero devolvía {"success":false} sin excepción
    // visible, porque el SDK intentaba resolver un modelo que nunca se había construido).
    //
    // IReportDesignerModelBuilder (el builder fluido que usa el ejemplo oficial de versiones 24+)
    // no existe en 22.1.15 — acá el API real es IReportDesignerClientSideModelGenerator.GetModel.
    // "reportUrl" es el nombre de campo que el cliente realmente envía (coincide con la opción
    // reportUrl de JSReportDesignerBinding en TemplateEditor.tsx). No se declara ninguna fuente de
    // datos manual porque nuestras plantillas ya se resuelven por nombre a través de
    // CustomReportStorageWebExtension (Google Drive).
    [HttpPost("[action]")]
    public IActionResult GetDesignerModel(
        [FromForm] string reportUrl,
        [FromServices] IReportDesignerClientSideModelGenerator modelGenerator)
    {
        var model = modelGenerator.GetModel(
            reportUrl,
            null,
            ReportDesignerController.DefaultUri,
            WebDocumentViewerController.DefaultUri,
            QueryBuilderController.DefaultUri);

        return DesignerModel(model);
    }
}

public class CustomQueryBuilderController : QueryBuilderController
{
    public CustomQueryBuilderController(IQueryBuilderMvcControllerService controllerService) : base(controllerService)
    {
    }
}
