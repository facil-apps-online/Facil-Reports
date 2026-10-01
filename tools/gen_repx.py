#!/usr/bin/env python3
"""Genera las plantillas estándar de factura electrónica (.repx) a partir de una plantilla base que el
diseñador de DevExpress ya abre. Reposiciona/clona controles existentes (mismos atributos, mismas
expresiones) y agrega PageHeader, PageFooter y el paginador. Variantes: tamaño x posición del QR.
"""
import copy, sys, re
import xml.etree.ElementTree as ET

BASE = sys.argv[1]
OUT = sys.argv[2]

SIZES = {
    # nombre: (PageWidth, PageHeight, Margins)
    "carta":      (2159, 2794, "100, 100, 100, 100"),
    "mediacarta": (2159, 1397, "100, 100, 60, 60"),
}
BLUE = "255,36,85,201"


def load_base():
    tree = ET.parse(BASE)
    root = tree.getroot()
    ctl = {}
    for e in root.iter():
        n = e.get("Name")
        if n and e.get("ControlType") and n not in ctl:
            ctl[n] = e
    return root, ctl


def expr(el, prop, expression):
    eb = el.find("ExpressionBindings")
    if eb is None:
        eb = ET.SubElement(el, "ExpressionBindings")
    for it in eb:
        if it.get("PropertyName") == prop:
            it.set("Expression", expression)
            return
    ET.SubElement(eb, "Item9", {"Ref": "0", "EventName": "BeforePrint", "PropertyName": prop, "Expression": expression})


def drop_expr(el, prop):
    eb = el.find("ExpressionBindings")
    if eb is None:
        return
    for it in list(eb):
        if it.get("PropertyName") == prop:
            eb.remove(it)
    if len(eb) == 0:
        el.remove(eb)


def place(el, x, y, w, h):
    el.set("LocationFloat", f"{x:g},{y:g}")
    el.set("SizeF", f"{w:g},{h:g}")
    return el


def mk(ctl, base, name, x, y, w, h, **attrs):
    e = copy.deepcopy(ctl[base])
    e.set("Name", name)
    place(e, x, y, w, h)
    for k, v in attrs.items():
        if v is None:
            e.attrib.pop(k, None)
        else:
            e.set(k, v)
    return e


def band(tag, ctype, name, height, controls, **attrs):
    b = ET.Element(tag, {"Ref": "0", "ControlType": ctype, "Name": name, "HeightF": f"{height:g}", "Dpi": "254", **attrs})
    c = ET.SubElement(b, "Controls")
    for e in controls:
        c.append(e)
    return b


def renumber(root):
    ref = [0]
    def walk(e):
        if e.get("Ref") is not None:
            e.set("Ref", str(ref[0])); ref[0] += 1
        kids = list(e)
        if kids and all(k.tag.startswith("Item") for k in kids):
            for i, k in enumerate(kids, 1):
                k.tag = f"Item{i}"
        for k in kids:
            walk(k)
    walk(root)


def build(size, qr_top):
    if size == "mediacarta":
        return build_compact(size, qr_top)
    root, ctl = load_base()
    pw, ph, margins = SIZES[size]
    root.set("PageWidth", str(pw)); root.set("PageHeight", str(ph)); root.set("Margins", margins)
    root.set("Name", f"Factura_{size}_{'QrArriba' if qr_top else 'QrAbajo'}")
    W = 1959  # ancho útil

    HAS_LOGO = "Len([Documento.EmisorLogoUrl]) > 0"
    NO_LOGO = "Len([Documento.EmisorLogoUrl]) = 0"
    HAS_QR = "Len([Documento.QrImageUrl]) > 0"

    # ---------------- PageHeader (se repite en cada hoja) ----------------
    logo = mk(ctl, "picLogo", "picLogo", 0, 0, 350, 150); expr(logo, "Visible", HAS_LOGO)
    brand = mk(ctl, "lblEmisorNombre", "lblBrandName", 0, 0, 1000, 110,
               Font="DejaVu Sans, 13pt, style=Bold", Multiline="true")
    expr(brand, "Text", "[Documento.EmisorRazonSocial]"); expr(brand, "Visible", NO_LOGO)
    ph_controls = [logo, brand]
    right_x, right_w = (759, 980) if qr_top else (959, 1000)
    title = mk(ctl, "lblDocTitle", "lblDocTitle", right_x, 0, right_w, 45)
    number = mk(ctl, "lblDocNumber", "lblDocNumber", right_x, 45, right_w, 65)
    resol = mk(ctl, "lblResolucion", "lblResolucion", right_x, 115, right_w, 80)
    ph_controls += [title, number, resol]
    if qr_top:
        qr = mk(ctl, "picQr", "picQr", 1759, 0, 200, 200); expr(qr, "Visible", HAS_QR)
        # el CUFE real tiene 96 caracteres: 5pt en una caja de 125 mm cabe en una sola línea
        cufe = mk(ctl, "lblLegal", "lblCufeTop", 709, 205, 1250, 45,
                  Font="DejaVu Sans Mono, 5pt", TextAlignment="TopRight", Multiline="true")
        expr(cufe, "Text", "'CUFE: ' + [Documento.Cufe]")
        expr(cufe, "Visible", "Len([Documento.Cufe]) > 0")
        ph_controls += [qr, cufe]
        header_h, line_y = 275, 262
    else:
        header_h, line_y = 220, 207
    ph_controls.append(mk(ctl, "lineHeader", "lineHeader", 0, line_y, W, 10))
    page_header = band("Item2", "PageHeaderBand", "PageHeader", header_h, ph_controls)

    # ---------------- ReportHeader (solo la primera hoja) ----------------
    rh = []
    gy = 10
    for n in ("lblActividadEconomica", "lblGranContribuyente", "lblAgenteRetenedorIva", "lblAutorretenedorRenta"):
        rh.append(mk(ctl, n, n, 0, gy, 1300, 24)); gy += 26
    y0 = 125
    rh += [
        mk(ctl, "lblEmisorH", "lblEmisorH", 0, y0, 950, 30),
        mk(ctl, "lblEmisorNombre", "lblEmisorNombre", 0, y0 + 32, 950, 45),
        mk(ctl, "lblEmisorInfo", "lblEmisorInfo", 0, y0 + 80, 950, 150),
        mk(ctl, "lblCalidadTrib", "lblCalidadTrib", 0, y0 + 235, 950, 45),
        mk(ctl, "lblAdqH", "lblAdqH", 1009, y0, 950, 30),
        mk(ctl, "lblAdqNombre", "lblAdqNombre", 1009, y0 + 32, 950, 45),
        mk(ctl, "lblAdqInfo", "lblAdqInfo", 1009, y0 + 80, 950, 150),
    ]
    panel = copy.deepcopy(ctl["pnlMeta"]); place(panel, 0, y0 + 300, W, 160)
    cols = [(10, 500), (520, 340), (870, 340), (1220, 440), (1670, 279)]
    for i, (x, w) in enumerate(cols, 1):
        for suffix, yy, hh in (("k", 10, 25), ("v", 35, 30)):
            for ch in panel.find("Controls"):
                if ch.get("Name") == f"lblMeta{i}{suffix}":
                    place(ch, x, yy, w, hh)
    for ch in panel.find("Controls"):
        if ch.get("Name") == "lblNotaRef":
            place(ch, 10, 72, 1939, 40)
    rh.append(panel)
    rh.append(mk(ctl, "tblItemsHeader", "tblItemsHeader", 0, y0 + 480, W, 60))
    report_header = band("Item3", "ReportHeaderBand", "ReportHeader", y0 + 545, rh)

    # ---------------- Detalle (igual a la base) ----------------
    detail = copy.deepcopy([b for b in root.find("Bands") if b.get("ControlType") == "DetailBand"][0])
    detail_report = copy.deepcopy([b for b in root.find("Bands") if b.get("ControlType") == "DetailReportBand"][0])

    # ---------------- ReportFooter (totales, al fondo de la última hoja) ----------------
    rf = [mk(ctl, "lineTotals", "lineTotals", 0, 0, W, 10)]
    for n, x, y, w, h in (("lblSubtotalK", 1359, 20, 300, 30), ("lblSubtotalV", 1659, 20, 300, 30)):
        rf.append(mk(ctl, n, n, x, y, w, h))
    sub = copy.deepcopy(ctl["subImpuestos"]); place(sub, 1359, 55, 600, 140); rf.append(sub)
    for n, x, y, w, h in (("lineGrand", 1359, 238, 600, 6), ("lblTotalK", 1359, 248, 300, 50), ("lblTotalV", 1659, 248, 300, 50),
                          ("lblNetoK", 1359, 300, 300, 30), ("lblNetoV", 1659, 300, 300, 30)):
        rf.append(mk(ctl, n, n, x, y, w, h))
    rf.append(mk(ctl, "lblTotalLetras", "lblTotalLetras", 0, 345, W, 95, Multiline="true"))
    rf.append(mk(ctl, "lblObservaciones", "lblObservaciones", 0, 445, W, 70))
    footer_h = 530
    if not qr_top:
        qr = mk(ctl, "picQr", "picQr", 0, 525, 220, 220); expr(qr, "Visible", HAS_QR)
        cufe = mk(ctl, "lblLegal", "lblCufe", 240, 525, 1719, 90, Font="DejaVu Sans Mono, 6pt", Multiline="true")
        expr(cufe, "Text", "'CUFE: ' + [Documento.Cufe]"); expr(cufe, "Visible", "Len([Documento.Cufe]) > 0")
        rf += [qr, cufe]
        footer_h = 760
    report_footer = band("Item6", "ReportFooterBand", "ReportFooter", footer_h, rf, KeepTogether="true")

    # ---------------- PageFooter (se repite: legal, fabricante, paginador) ----------------
    legal = mk(ctl, "lblLegal", "lblLegal", 0, 0, W, 60, Multiline="true")
    expr(legal, "Text", "'Representación gráfica de la ' + [Documento.DocumentoTipo] + ', generada a partir del XML autorizado por la DIAN.' + '\n' + 'Este documento no requiere firma manuscrita — ley 527 de 1999.'")
    pf = [legal,
          mk(ctl, "lineSoftware", "lineSoftware", 0, 65, W, 4),
          mk(ctl, "lblFabricante", "lblFabricante", 0, 75, 900, 60),
          mk(ctl, "lblProveedor", "lblProveedor", 910, 75, 700, 60)]
    pager = copy.deepcopy(ctl["lblFabricante"])
    pager.set("Name", "lblPagina"); pager.set("ControlType", "XRPageInfo")
    place(pager, 1659, 75, 300, 40)
    pager.set("PageInfo", "NumberOfTotal"); pager.set("TextFormatString", "PÁGINA {0} / {1}")
    pager.set("TextAlignment", "TopRight"); pager.set("Font", "DejaVu Sans, 7.5pt, style=Bold")
    for tag in ("ExpressionBindings",):
        for x in pager.findall(tag): pager.remove(x)
    pf.append(pager)
    page_footer = band("Item7", "PageFooterBand", "PageFooter", 140, pf)

    # ---------------- ensamblar ----------------
    bands = root.find("Bands")
    top = [b for b in bands if b.get("ControlType") == "TopMarginBand"][0]
    bottom = [b for b in bands if b.get("ControlType") == "BottomMarginBand"][0]
    for b in list(bands):
        bands.remove(b)
    for b in (top, page_header, report_header, detail, detail_report, report_footer, page_footer, bottom):
        bands.append(b)
    renumber(root)
    return root


def build_compact(size, qr_top):
    """Media carta: mismo estándar pero compacto para que una factura corta quepa en una hoja."""
    root, ctl = load_base()
    pw, ph, margins = SIZES[size]
    root.set("PageWidth", str(pw)); root.set("PageHeight", str(ph)); root.set("Margins", margins)
    root.set("Name", f"Factura_{size}_{'QrArriba' if qr_top else 'QrAbajo'}")
    W = 1959
    HAS_LOGO = "Len([Documento.EmisorLogoUrl]) > 0"
    NO_LOGO = "Len([Documento.EmisorLogoUrl]) = 0"
    HAS_QR = "Len([Documento.QrImageUrl]) > 0"
    small = lambda e, font: (e.set("Font", font), e)[1]

    # PageHeader
    logo = mk(ctl, "picLogo", "picLogo", 0, 0, 300, 110); expr(logo, "Visible", HAS_LOGO)
    brand = mk(ctl, "lblEmisorNombre", "lblBrandName", 0, 0, 900, 90, Font="DejaVu Sans, 11pt, style=Bold", Multiline="true")
    expr(brand, "Text", "[Documento.EmisorRazonSocial]"); expr(brand, "Visible", NO_LOGO)
    rx, rw = (759, 1040) if qr_top else (959, 1000)
    title = small(mk(ctl, "lblDocTitle", "lblDocTitle", rx, 0, rw, 35), "DejaVu Sans, 8pt, style=Bold")
    number = small(mk(ctl, "lblDocNumber", "lblDocNumber", rx, 35, rw, 50), "DejaVu Sans Mono, 12pt, style=Bold")
    resol = small(mk(ctl, "lblResolucion", "lblResolucion", rx, 90, rw, 60), "DejaVu Sans, 6.5pt")
    pc = [logo, brand, title, number, resol]
    if qr_top:
        qr = mk(ctl, "picQr", "picQr", 1809, 0, 150, 150); expr(qr, "Visible", HAS_QR)
        cufe = mk(ctl, "lblLegal", "lblCufeTop", 859, 154, 1100, 32, Font="DejaVu Sans Mono, 5pt", TextAlignment="TopRight", Multiline="true")
        expr(cufe, "Text", "'CUFE: ' + [Documento.Cufe]"); expr(cufe, "Visible", "Len([Documento.Cufe]) > 0")
        pc += [qr, cufe]; hh, ly = 195, 188
    else:
        hh, ly = 160, 153
    pc.append(mk(ctl, "lineHeader", "lineHeader", 0, ly, W, 10))
    page_header = band("Item2", "PageHeaderBand", "PageHeader", hh, pc)

    # ReportHeader (solo la primera hoja)
    legend = mk(ctl, "lblActividadEconomica", "lblActividadEconomica", 0, 5, W, 40, Font="DejaVu Sans, 6pt", Multiline="true")
    expr(legend, "Text", "'Actividad Económica Principal: ' + [Documento.EmisorActividadEconomica] + '   ·   ' + [Documento.EmisorGranContribuyente] + '   ·   ' + [Documento.EmisorAgenteRetenedorIva] + '   ·   ' + [Documento.EmisorAutorretenedorRenta]")
    y0 = 55
    rh = [legend,
          mk(ctl, "lblEmisorH", "lblEmisorH", 0, y0, 950, 25),
          small(mk(ctl, "lblEmisorNombre", "lblEmisorNombre", 0, y0 + 26, 950, 40), "DejaVu Sans, 9pt, style=Bold"),
          small(mk(ctl, "lblEmisorInfo", "lblEmisorInfo", 0, y0 + 68, 950, 100), "DejaVu Sans, 7pt"),
          mk(ctl, "lblCalidadTrib", "lblCalidadTrib", 0, y0 + 172, 950, 38),
          mk(ctl, "lblAdqH", "lblAdqH", 1009, y0, 950, 25),
          small(mk(ctl, "lblAdqNombre", "lblAdqNombre", 1009, y0 + 26, 950, 40), "DejaVu Sans, 9pt, style=Bold"),
          small(mk(ctl, "lblAdqInfo", "lblAdqInfo", 1009, y0 + 68, 950, 100), "DejaVu Sans, 7pt")]
    panel = copy.deepcopy(ctl["pnlMeta"]); place(panel, 0, y0 + 215, W, 105)
    cols = [(10, 500), (520, 340), (870, 340), (1220, 440), (1670, 279)]
    for i, (x, w) in enumerate(cols, 1):
        for suffix, yy, hh2 in (("k", 5, 25), ("v", 28, 28)):
            for ch in panel.find("Controls"):
                if ch.get("Name") == f"lblMeta{i}{suffix}":
                    place(ch, x, yy, w, hh2)
    for ch in panel.find("Controls"):
        if ch.get("Name") == "lblNotaRef":
            place(ch, 10, 62, 1939, 38)
    rh.append(panel)
    rh.append(mk(ctl, "tblItemsHeader", "tblItemsHeader", 0, y0 + 328, W, 50))
    report_header = band("Item3", "ReportHeaderBand", "ReportHeader", y0 + 385, rh)

    detail = copy.deepcopy([b for b in root.find("Bands") if b.get("ControlType") == "DetailBand"][0])
    detail_report = copy.deepcopy([b for b in root.find("Bands") if b.get("ControlType") == "DetailReportBand"][0])

    # ReportFooter: columna izquierda (letras, observaciones, QR+CUFE) y derecha (totales)
    rf = [mk(ctl, "lineTotals", "lineTotals", 0, 0, W, 10)]
    for n, x, y, w, h in (("lblSubtotalK", 1359, 15, 300, 28), ("lblSubtotalV", 1659, 15, 300, 28)):
        rf.append(mk(ctl, n, n, x, y, w, h))
    sub = copy.deepcopy(ctl["subImpuestos"]); place(sub, 1359, 45, 600, 105); rf.append(sub)
    for n, x, y, w, h in (("lineGrand", 1359, 160, 600, 6), ("lblTotalK", 1359, 168, 300, 45), ("lblTotalV", 1659, 168, 300, 45),
                          ("lblNetoK", 1359, 215, 300, 28), ("lblNetoV", 1659, 215, 300, 28)):
        rf.append(mk(ctl, n, n, x, y, w, h))
    rf.append(mk(ctl, "lblTotalLetras", "lblTotalLetras", 0, 15, 1300, 85, Multiline="true"))
    rf.append(mk(ctl, "lblObservaciones", "lblObservaciones", 0, 105, 1300, 60))
    rfh = 260
    if not qr_top:
        qr = mk(ctl, "picQr", "picQr", 0, 170, 150, 150); expr(qr, "Visible", HAS_QR)
        cufe = mk(ctl, "lblLegal", "lblCufe", 165, 170, 1150, 70, Font="DejaVu Sans Mono, 5.5pt", Multiline="true")
        expr(cufe, "Text", "'CUFE: ' + [Documento.Cufe]"); expr(cufe, "Visible", "Len([Documento.Cufe]) > 0")
        rf += [qr, cufe]; rfh = 335
    report_footer = band("Item6", "ReportFooterBand", "ReportFooter", rfh, rf, KeepTogether="true")

    # PageFooter
    legal = small(mk(ctl, "lblLegal", "lblLegal", 0, 0, W, 45, Multiline="true"), "DejaVu Sans, 6pt")
    expr(legal, "Text", "'Representación gráfica de la ' + [Documento.DocumentoTipo] + ', generada a partir del XML autorizado por la DIAN. Este documento no requiere firma manuscrita — ley 527 de 1999.'")
    pf = [legal, mk(ctl, "lineSoftware", "lineSoftware", 0, 48, W, 4),
          small(mk(ctl, "lblFabricante", "lblFabricante", 0, 56, 900, 40), "DejaVu Sans, 6pt"),
          small(mk(ctl, "lblProveedor", "lblProveedor", 910, 56, 700, 40), "DejaVu Sans, 6pt")]
    pager = copy.deepcopy(ctl["lblFabricante"])
    pager.set("Name", "lblPagina"); pager.set("ControlType", "XRPageInfo"); place(pager, 1659, 56, 300, 35)
    pager.set("PageInfo", "NumberOfTotal"); pager.set("TextFormatString", "PÁGINA {0} / {1}")
    pager.set("TextAlignment", "TopRight"); pager.set("Font", "DejaVu Sans, 7pt, style=Bold")
    for x in pager.findall("ExpressionBindings"): pager.remove(x)
    pf.append(pager)
    page_footer = band("Item7", "PageFooterBand", "PageFooter", 100, pf)

    bands = root.find("Bands")
    top = [b for b in bands if b.get("ControlType") == "TopMarginBand"][0]
    bottom = [b for b in bands if b.get("ControlType") == "BottomMarginBand"][0]
    for b in list(bands): bands.remove(b)
    for b in (top, page_header, report_header, detail, detail_report, report_footer, page_footer, bottom): bands.append(b)
    renumber(root)
    return root


if __name__ == "__main__":
    import os
    os.makedirs(OUT, exist_ok=True)
    for size in SIZES:
        for qr_top in (True, False):
            root = build(size, qr_top)
            name = f"factura_{size}_{'qr-arriba' if qr_top else 'qr-abajo'}.repx"
            ET.indent(root, space="  ")
            ET.ElementTree(root).write(os.path.join(OUT, name), encoding="utf-8", xml_declaration=True)
            print("generado", name)
