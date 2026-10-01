#!/usr/bin/env python3
"""Familias adicionales del estándar: documento soporte, nómina y POS (tirilla de 80 mm).
Uso: gen2.py <repx_base> <carpeta_salida>
"""
import sys, os, copy
import xml.etree.ElementTree as ET

BASE_PATH, OUT_DIR = sys.argv[1], sys.argv[2]
sys.argv = [sys.argv[0], BASE_PATH, OUT_DIR]
import gen_repx as g
from gen_repx import mk, expr, drop_expr, place, band, renumber, load_base, SIZES

BLUE = "255,36,85,201"


def small(e, font):
    e.set("Font", font)
    return e


# ------------------------------------------------------------------ documento soporte
def build_ds(size, qr_top):
    root = g.build(size, qr_top)
    root.set("Name", f"DocumentoSoporte_{size}_{'QrArriba' if qr_top else 'QrAbajo'}")
    subs = [("AdquirenteIdentificacion", "ProveedorIdentificacion"), ("AdquirenteNombre", "ProveedorNombre"),
            ("AdquirenteDireccion", "ProveedorDireccion"), ("AdquirenteCiudad", "ProveedorCiudad"),
            ("AdquirenteTelefono", "ProveedorTelefono"), ("AdquirenteEmail", "ProveedorEmail")]
    for e in root.iter():
        for k in ("Expression", "Text"):
            v = e.get(k)
            if v is None:
                continue
            for a, b in subs:
                v = v.replace("Documento." + a, "Documento." + b)
            if k == "Text" and v == "ADQUIRENTE":
                v = "PROVEEDOR"
            if k == "Text" and v == "NIT/CC":
                v = "NIT/CC"
            e.set(k, v)
    # El título legal del documento soporte es largo (2 líneas): se le da más alto y se baja lo de abajo.
    by = {e.get("Name"): e for e in root.iter() if e.get("Name")}
    compact = size == "mediacarta"
    dy = 25 if compact else 25
    t = by["lblDocTitle"]; n = by["lblDocNumber"]; r = by["lblResolucion"]
    x, y = t.get("LocationFloat").split(","); w = t.get("SizeF").split(",")[0]
    t.set("SizeF", f"{w},{70 if not compact else 60}"); t.set("Font", "DejaVu Sans, 7pt, style=Bold")
    for el in (n, r):
        ex, ey = el.get("LocationFloat").split(","); eh = el.get("SizeF").split(",")[1]
        el.set("LocationFloat", f"{ex},{float(ey) + (25 if not compact else 25):g}")
    if compact:
        r.set("SizeF", f"{r.get('SizeF').split(',')[0]},45")
    # texto legal corto y propio del documento soporte (el título largo no cabe en el pie)
    for e in root.iter():
        if e.get("Name") == "lblLegal" and e.find("ExpressionBindings") is not None:
            for it in e.find("ExpressionBindings"):
                if it.get("PropertyName") == "Text":
                    it.set("Expression", "'Representación gráfica del Documento Soporte, generada a partir del XML autorizado por la DIAN.' + '\n' + 'Este documento no requiere firma manuscrita — ley 527 de 1999.'" if not compact else "'Representación gráfica del Documento Soporte, generada a partir del XML autorizado por la DIAN. Este documento no requiere firma manuscrita — ley 527 de 1999.'")
    return root


# ------------------------------------------------------------------ nómina
def concept_table(ctl, base, name, loc, size, cells, header):
    """Tabla de 3 columnas (Código, Concepto, Valor) clonada de la tabla de ítems de la factura."""
    t = copy.deepcopy(ctl[base]); t.set("Name", name); place(t, *loc, *size)
    row = t.find("Rows")[0]
    cs = row.find("Cells")
    keep = list(cs)[:3]
    for c in list(cs):
        cs.remove(c)
    specs = [("cellA", "0.6", "MiddleLeft"), ("cellB", "3", "MiddleLeft"), ("cellC", "1.2", "MiddleRight")]
    for c, (nm, w, al), (text, ex) in zip(keep, specs, cells):
        c.set("Name", f"{name}_{nm}"); c.set("Weight", w); c.set("TextAlignment", al)
        c.attrib.pop("Padding", None)
        if header:
            c.set("Text", text)
        else:
            c.attrib.pop("Text", None); expr(c, "Text", ex)
        cs.append(c)
    return t


def build_nomina(size, qr_top):
    root, ctl = load_base()
    pw, ph, margins = SIZES[size]
    root.set("PageWidth", str(pw)); root.set("PageHeight", str(ph)); root.set("Margins", margins)
    root.set("Name", f"Nomina_{size}_{'QrArriba' if qr_top else 'QrAbajo'}")
    W = 1959
    cmp_ = size == "mediacarta"
    ROW_H, SEC_H, INFO_H, PANEL_Y, RH_H = (36, 85, 85, 170, 275) if cmp_ else (45, 100, 100, 180, 290)
    HAS_LOGO = "Len([Documento.EmisorLogoUrl]) > 0"; NO_LOGO = "Len([Documento.EmisorLogoUrl]) = 0"
    HAS_QR = "Len([Documento.QrImageUrl]) > 0"

    # PageHeader
    logo = mk(ctl, "picLogo", "picLogo", 0, 0, 300, 110); expr(logo, "Visible", HAS_LOGO)
    brand = mk(ctl, "lblEmisorNombre", "lblBrandName", 0, 0, 900, 90, Font="DejaVu Sans, 11pt, style=Bold", Multiline="true")
    expr(brand, "Text", "[Documento.EmisorRazonSocial]"); expr(brand, "Visible", NO_LOGO)
    rx, rw = (759, 1040) if qr_top else (959, 1000)
    title = small(mk(ctl, "lblDocTitle", "lblDocTitle", rx, 0, rw, 35), "DejaVu Sans, 8pt, style=Bold")
    expr(title, "Text", "[Documento.DocumentoTipo]")
    number = small(mk(ctl, "lblDocNumber", "lblDocNumber", rx, 35, rw, 50), "DejaVu Sans Mono, 12pt, style=Bold")
    period = small(mk(ctl, "lblResolucion", "lblPeriodo", rx, 90, rw, 60), "DejaVu Sans, 6.5pt")
    expr(period, "Text", "'Período: ' + [Documento.PeriodoTexto]")
    pc = [logo, brand, title, number, period]
    if qr_top:
        qr = mk(ctl, "picQr", "picQr", 1809, 0, 150, 150); expr(qr, "Visible", HAS_QR)
        cune = mk(ctl, "lblLegal", "lblCuneTop", 859, 154, 1100, 32, Font="DejaVu Sans Mono, 5pt", TextAlignment="TopRight", Multiline="true")
        expr(cune, "Text", "'CUNE: ' + [Documento.Cune]"); expr(cune, "Visible", "Len([Documento.Cune]) > 0")
        pc += [qr, cune]; hh, ly = 195, 188
    else:
        hh, ly = 160, 153
    pc.append(mk(ctl, "lineHeader", "lineHeader", 0, ly, W, 10))
    page_header = band("Item2", "PageHeaderBand", "PageHeader", hh, pc)

    # ReportHeader: empleador / trabajador + datos de la liquidación
    y0 = 10
    rh = [mk(ctl, "lblEmisorH", "lblEmisorH", 0, y0, 950, 25, Text="EMPLEADOR"),
          small(mk(ctl, "lblEmisorNombre", "lblEmisorNombre", 0, y0 + 26, 950, 40), "DejaVu Sans, 9pt, style=Bold"),
          small(mk(ctl, "lblEmisorInfo", "lblEmisorInfo", 0, y0 + 68, 950, INFO_H), "DejaVu Sans, 7pt"),
          mk(ctl, "lblAdqH", "lblEmpH", 1009, y0, 950, 25, Text="TRABAJADOR"),
          small(mk(ctl, "lblAdqNombre", "lblEmpNombre", 1009, y0 + 26, 950, 40), "DejaVu Sans, 9pt, style=Bold"),
          small(mk(ctl, "lblAdqInfo", "lblEmpInfo", 1009, y0 + 68, 950, INFO_H), "DejaVu Sans, 7pt")]
    for e in rh:
        if e.get("Name") == "lblEmpNombre":
            expr(e, "Text", "[Documento.EmpleadoNombre]")
        if e.get("Name") == "lblEmpInfo":
            expr(e, "Text", "[Documento.EmpleadoTipoIdentificacion] + ' ' + [Documento.EmpleadoIdentificacion] + '\n' + [Documento.EmpleadoDireccion] + ', ' + [Documento.EmpleadoCiudad]")
    panel = copy.deepcopy(ctl["pnlMeta"]); place(panel, 0, y0 + PANEL_Y, W, 95)
    meta = [(10, 520, "Fecha y hora de generación", "[Documento.FechaGeneracion]"),
            (540, 520, "Período", "[Documento.PeriodoTexto]"),
            (1070, 330, "Fecha de pago", "[Documento.FechaPago]"),
            (1410, 539, "Medio de pago", "[Documento.MedioPago]")]
    for ch in list(panel.find("Controls")):
        n = ch.get("Name")
        if n == "lblNotaRef":
            place(ch, 10, 62, 1939, 34); continue
        if n.startswith("lblMeta"):
            i = int(n[7]); suffix = n[8]
            if i > len(meta):
                panel.find("Controls").remove(ch); continue
            x, w, label, ex = meta[i - 1]
            place(ch, x, 5 if suffix == "k" else 28, w, 25 if suffix == "k" else 28)
            if suffix == "k": ch.set("Text", label)
            else: expr(ch, "Text", ex)
    rh.append(panel)
    report_header = band("Item3", "ReportHeaderBand", "ReportHeader", y0 + RH_H, rh)

    # Devengos y Deducciones: un DetailReportBand cada uno, con su encabezado de columnas
    detail = copy.deepcopy([b for b in root.find("Bands") if b.get("ControlType") == "DetailBand"][0])
    template_dr = [b for b in root.find("Bands") if b.get("ControlType") == "DetailReportBand"][0]

    def section(level, member, title_text):
        dr = copy.deepcopy(template_dr)
        dr.set("Name", f"DetailReport{member}"); dr.set("DataMember", member); dr.set("Level", str(level))
        sub = dr.find("Bands")
        db = sub[0]
        db.set("Name", f"Detail{member}")
        ctrls = db.find("Controls")
        for c in list(ctrls):
            ctrls.remove(c)
        ctrls.append(concept_table(ctl, "tblItemRow", f"tbl{member}Row", (0, 0), (W, ROW_H),
                                   [(None, "[Codigo]"), (None, "[Descripcion]"), (None, "[Valor]")], False))
        db.set("HeightF", str(ROW_H))
        gh = ET.Element("Item1", {"Ref": "0", "ControlType": "GroupHeaderBand", "Name": f"Header{member}", "HeightF": str(SEC_H), "Dpi": "254"})
        gc = ET.SubElement(gh, "Controls")
        t = mk(ctl, "lblEmisorH", f"lblTitulo{member}", 0, 10, W, 30, Text=title_text,
               Font="DejaVu Sans, 8pt, style=Bold", ForeColor=BLUE)
        gc.append(t)
        gc.append(concept_table(ctl, "tblItemsHeader", f"tbl{member}Header", (0, 42), (W, 40),
                                [("Código", None), ("Concepto", None), ("Valor", None)], True))
        sub.insert(0, gh)
        return dr

    dev = section(0, "Devengos", "DEVENGADOS")
    ded = section(1, "Deducciones", "DEDUCCIONES")

    # ReportFooter: totales a la derecha (al fondo de la última hoja); QR + CUNE a la izquierda si va abajo
    rf = [mk(ctl, "lineTotals", "lineTotals", 0, 0, W, 10)]
    rows = [("Total devengado", "[Documento.TotalDevengado]", 20), ("Total deducciones", "'-' + [Documento.TotalDeduccion]", 55)]
    for label, ex, y in rows:
        k = mk(ctl, "lblSubtotalK", f"lblK{y}", 1159, y, 500, 30, Text=label); rf.append(k)
        v = mk(ctl, "lblSubtotalV", f"lblV{y}", 1659, y, 300, 30); expr(v, "Text", ex); rf.append(v)
    rf.append(mk(ctl, "lineGrand", "lineGrand", 1159, 95, 800, 6))
    rf.append(mk(ctl, "lblTotalK", "lblTotalK", 1159, 105, 500, 50, Text="NETO A PAGAR"))
    nv = mk(ctl, "lblTotalV", "lblTotalV", 1659, 105, 300, 50); expr(nv, "Text", "[Documento.NetoPagar]"); rf.append(nv)
    if qr_top:
        letras = mk(ctl, "lblTotalLetras", "lblNetoLetras", 0, 20, 1100, 120, Multiline="true")
    else:
        # QR abajo: el QR y el CUNE quedan junto a los totales, al final de la última hoja
        letras = mk(ctl, "lblTotalLetras", "lblNetoLetras", 165, 95, 940, 80, Multiline="true")
    expr(letras, "Text", "'Son: ' + [Documento.NetoPagarEnLetras]"); rf.append(letras)
    rfh = 180
    if not qr_top:
        qr = mk(ctl, "picQr", "picQr", 0, 20, 150, 150); expr(qr, "Visible", HAS_QR)
        cune = mk(ctl, "lblLegal", "lblCune", 165, 20, 940, 70, Font="DejaVu Sans Mono, 5.5pt", Multiline="true")
        expr(cune, "Text", "'CUNE: ' + [Documento.Cune]"); expr(cune, "Visible", "Len([Documento.Cune]) > 0")
        rf += [qr, cune]
    report_footer = band("Item6", "ReportFooterBand", "ReportFooter", rfh, rf, KeepTogether="true")

    # PageFooter
    legal = small(mk(ctl, "lblLegal", "lblLegal", 0, 0, W, 45, Multiline="true"), "DejaVu Sans, 6pt")
    expr(legal, "Text", "'Representación gráfica de la Nómina Electrónica — este documento no requiere firma manuscrita.'")
    pf = [legal, mk(ctl, "lineSoftware", "lineSoftware", 0, 48, W, 4),
          small(mk(ctl, "lblFabricante", "lblFabricante", 0, 56, 900, 40), "DejaVu Sans, 6pt"),
          small(mk(ctl, "lblProveedor", "lblProveedor", 910, 56, 700, 40), "DejaVu Sans, 6pt"),
          pager(ctl, 1659, 56)]
    page_footer = band("Item7", "PageFooterBand", "PageFooter", 100, pf)
    assemble(root, [page_header, report_header, detail, dev, ded, report_footer, page_footer])
    return root


def pager(ctl, x, y):
    p = copy.deepcopy(ctl["lblFabricante"])
    p.set("Name", "lblPagina"); p.set("ControlType", "XRPageInfo"); place(p, x, y, 300, 35)
    p.set("PageInfo", "NumberOfTotal"); p.set("TextFormatString", "PÁGINA {0} / {1}")
    p.set("TextAlignment", "TopRight"); p.set("Font", "DejaVu Sans, 7pt, style=Bold")
    for x_ in p.findall("ExpressionBindings"):
        p.remove(x_)
    return p


def assemble(root, middle):
    bands = root.find("Bands")
    top = [b for b in bands if b.get("ControlType") == "TopMarginBand"][0]
    bottom = [b for b in bands if b.get("ControlType") == "BottomMarginBand"][0]
    for b in list(bands):
        bands.remove(b)
    for b in [top] + middle + [bottom]:
        bands.append(b)
    renumber(root)


# ------------------------------------------------------------------ POS (tirilla 80 mm)
def build_pos(qr_top):
    root, ctl = load_base()
    root.set("PageWidth", "800"); root.set("PageHeight", "3500"); root.set("Margins", "20, 20, 20, 20")
    root.set("Name", f"POS_80mm_{'QrArriba' if qr_top else 'QrAbajo'}")
    W = 760
    HAS_LOGO = "Len([Documento.EmisorLogoUrl]) > 0"; NO_LOGO = "Len([Documento.EmisorLogoUrl]) = 0"
    HAS_QR = "Len([Documento.QrImageUrl]) > 0"
    C = "MiddleCenter"

    def lab(base, name, y, h, font, text_expr=None, text=None, align="TopCenter", color=None, **kw):
        e = mk(ctl, base, name, 0, y, W, h, Font=font, TextAlignment=align, Multiline="true", **kw)
        if color: e.set("ForeColor", color)
        if text_expr: expr(e, "Text", text_expr)
        if text is not None:
            drop_expr(e, "Text"); e.set("Text", text)
        return e

    rh = []
    y = 0
    logo = mk(ctl, "picLogo", "picLogo", 190, y, 380, 150); expr(logo, "Visible", HAS_LOGO); rh.append(logo)
    brand = lab("lblEmisorNombre", "lblBrandName", y, 90, "DejaVu Sans, 11pt, style=Bold", "[Documento.EmisorRazonSocial]")
    expr(brand, "Visible", NO_LOGO); rh.append(brand)
    y = 160
    rh.append(lab("lblEmisorInfo", "lblEmisorInfo", y, 200, "DejaVu Sans, 7.5pt",
                  "'NIT ' + [Documento.EmisorNit] + '\n' + [Documento.EmisorDireccion] + ', ' + [Documento.EmisorCiudad] + '\n' + [Documento.EmisorTelefono] + '\n' + [Documento.EmisorEmail]",
                  color="255,60,60,60"))
    y = 365
    rh.append(lab("lblCalidadTrib", "lblCalidadTrib", y, 35, "DejaVu Sans, 7pt, style=Bold", "[Documento.EmisorCalidadTributaria]", color=BLUE, align=C))
    y = 410
    rh.append(mk(ctl, "lineHeader", "lineTop", 0, y, W, 8)); y += 20
    rh.append(lab("lblDocTitle", "lblDocTitle", y, 95, "DejaVu Sans, 8.5pt, style=Bold", "[Documento.DocumentoTipo]", align=C)); y += 100
    rh.append(lab("lblDocNumber", "lblDocNumber", y, 55, "DejaVu Sans Mono, 14pt, style=Bold", "[Documento.DocumentoNumero]", color=BLUE, align=C)); y += 60
    rh.append(lab("lblResolucion", "lblResolucion", y, 130, "DejaVu Sans, 6.5pt", "[Documento.ResolucionTexto]", color="Gray", align="TopCenter")); y += 135
    if qr_top:
        qr = mk(ctl, "picQr", "picQr", 255, y, 250, 250); expr(qr, "Visible", HAS_QR); rh.append(qr); y += 258
        cufe = lab("lblLegal", "lblCufeTop", y, 80, "DejaVu Sans Mono, 5pt", "'CUFE: ' + [Documento.Cufe]", align=C, color="255,60,60,60")
        expr(cufe, "Visible", "Len([Documento.Cufe]) > 0"); rh.append(cufe); y += 88
    rh.append(mk(ctl, "lineHeader", "lineMid", 0, y, W, 8)); y += 18
    rh.append(lab("lblMeta1v", "lblFecha", y, 60, "DejaVu Sans, 7.5pt", "'Fecha: ' + [Documento.FechaGeneracion] + '\n' + 'Pago: ' + [Documento.FormaPago] + ' · ' + [Documento.MedioPago]", align="TopLeft")); y += 70
    rh.append(lab("lblAdqNombre", "lblCliente", y, 120, "DejaVu Sans, 7.5pt",
                  "'Cliente: ' + [Documento.AdquirenteNombre] + '\n' + 'NIT/CC: ' + [Documento.AdquirenteIdentificacion] + '\n' + [Documento.AdquirenteDireccion]", align="TopLeft")); y += 125
    # encabezado de columnas de la tirilla
    t = copy.deepcopy(ctl["tblItemsHeader"]); t.set("Name", "tblItemsHeader"); place(t, 0, y, W, 40)
    cs = t.find("Rows")[0].find("Cells"); keep = list(cs)[:2]
    for c in list(cs): cs.remove(c)
    for c, (nm, w, al, tx) in zip(keep, [("c1", "3", "MiddleLeft", "Descripción"), ("c2", "1.1", "MiddleRight", "Total")]):
        c.set("Name", "hdr" + nm); c.set("Weight", w); c.set("TextAlignment", al); c.set("Text", tx); c.attrib.pop("Padding", None); cs.append(c)
    rh.append(t); y += 45
    report_header = band("Item2", "ReportHeaderBand", "ReportHeader", y, rh)

    detail = copy.deepcopy([b for b in root.find("Bands") if b.get("ControlType") == "DetailBand"][0])
    dr = copy.deepcopy([b for b in root.find("Bands") if b.get("ControlType") == "DetailReportBand"][0])
    db = dr.find("Bands")[0]; ctrls = db.find("Controls")
    for c in list(ctrls): ctrls.remove(c)
    row = copy.deepcopy(ctl["tblItemRow"]); row.set("Name", "tblItemRow"); place(row, 0, 0, W, 60)
    cs = row.find("Rows")[0].find("Cells"); keep = list(cs)[:2]
    for c in list(cs): cs.remove(c)
    specs = [("d1", "3", "TopLeft", "[Codigo] + ' ' + [Nombre] + '\n' + [Cantidad] + ' x ' + [ValorUnitario] + '  (IVA ' + [PorcentajeIva] + ')'"),
             ("d2", "1.1", "TopRight", "[TotalLinea]")]
    for c, (nm, w, al, ex) in zip(keep, specs):
        c.set("Name", "row" + nm); c.set("Weight", w); c.set("TextAlignment", al); c.set("Font", "DejaVu Sans, 7.5pt"); c.attrib.pop("Padding", None)
        c.attrib.pop("Text", None); expr(c, "Text", ex); cs.append(c)
    ctrls.append(row); db.set("HeightF", "60")

    rf = [mk(ctl, "lineTotals", "lineTotals", 0, 0, W, 8)]
    rf.append(mk(ctl, "lblSubtotalK", "lblSubtotalK", 0, 15, 380, 30))
    rf.append(mk(ctl, "lblSubtotalV", "lblSubtotalV", 380, 15, 380, 30))
    sub = copy.deepcopy(ctl["subImpuestos"]); place(sub, 0, 50, W, 105); rf.append(sub)
    # ancho de las columnas del subreporte al de la tirilla
    for tbl in sub.iter():
        if tbl.get("Name") == "tblImpuestoRow":
            tbl.set("SizeF", f"{W},30")
    rf += [mk(ctl, "lineGrand", "lineGrand", 0, 165, W, 6), mk(ctl, "lblTotalK", "lblTotalK", 0, 175, 380, 50),
           mk(ctl, "lblTotalV", "lblTotalV", 380, 175, 380, 50),
           mk(ctl, "lblNetoK", "lblNetoK", 0, 228, 380, 30), mk(ctl, "lblNetoV", "lblNetoV", 380, 228, 380, 30)]
    y = 270
    rf.append(lab("lblTotalLetras", "lblTotalLetras", y, 90, "DejaVu Sans, 7pt", "'Son: ' + [Documento.TotalEnLetras]", align="TopLeft")); y += 95
    rf.append(lab("lblObservaciones", "lblObservaciones", y, 70, "DejaVu Sans, 7pt", "'Observaciones: ' + [Documento.Notas]", align="TopLeft"))
    expr(rf[-1], "Visible", "Len([Documento.Notas]) > 0"); y += 75
    if not qr_top:
        qr = mk(ctl, "picQr", "picQr", 255, y, 250, 250); expr(qr, "Visible", HAS_QR); rf.append(qr); y += 258
        cufe = lab("lblLegal", "lblCufe", y, 80, "DejaVu Sans Mono, 5pt", "'CUFE: ' + [Documento.Cufe]", align=C, color="255,60,60,60")
        expr(cufe, "Visible", "Len([Documento.Cufe]) > 0"); rf.append(cufe); y += 88
    rf.append(lab("lblLegal", "lblLegal", y, 110, "DejaVu Sans, 6pt",
                  "'Representación gráfica de la ' + [Documento.DocumentoTipo] + ', generada a partir del XML autorizado por la DIAN. Este documento no requiere firma manuscrita — ley 527 de 1999.'",
                  align=C, color="255,60,60,60")); y += 115
    rf.append(lab("lblFabricante", "lblFabricante", y, 80, "DejaVu Sans, 6pt",
                  "'Software: ' + [Documento.NombreSoftware] + ' · ' + [Documento.FabricanteSoftwareNombre] + ' NIT ' + [Documento.FabricanteSoftwareNit]", align=C, color="255,60,60,60")); y += 85
    pv = lab("lblProveedor", "lblProveedor", y, 80, "DejaVu Sans, 6pt",
             "'Proveedor Tecnológico: ' + [Documento.ProveedorTecnologicoNombre] + ' NIT ' + [Documento.ProveedorTecnologicoNit]", align=C, color="255,60,60,60")
    expr(pv, "Visible", "Len([Documento.ProveedorTecnologicoNombre]) > 0"); rf.append(pv); y += 90
    report_footer = band("Item5", "ReportFooterBand", "ReportFooter", y, rf, KeepTogether="true")
    assemble(root, [report_header, detail, dr, report_footer])
    return root


def write(root, name):
    ET.indent(root, space="  ")
    ET.ElementTree(root).write(os.path.join(OUT_DIR, name), encoding="utf-8", xml_declaration=True)
    print("generado", name)


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    for size in SIZES:
        for qr_top in (True, False):
            tag = "qr-arriba" if qr_top else "qr-abajo"
            write(build_ds(size, qr_top), f"documento-soporte_{size}_{tag}.repx")
            write(build_nomina(size, qr_top), f"nomina_{size}_{tag}.repx")
    for qr_top in (True, False):
        write(build_pos(qr_top), f"pos_80mm_{'qr-arriba' if qr_top else 'qr-abajo'}.repx")
