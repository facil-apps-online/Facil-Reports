"""Genera las plantillas de factura de mandato y de exportacion a partir de la factura estandar vigente (con logo).

Parte de templates/produccion/facilfactura_fe-std_conlogo_218c0c3d7f964105.repx (la que el cliente tiene habilitada y aprobada), de modo que
ambas heredan su diseno y cualquier correccion que se le haga. Escribe:
  templates/estandar/factura-mandato_conlogo.repx
  templates/estandar/factura-exportacion_conlogo.repx

Mandato:     mandante bajo cada producto y lista de observaciones tras los items.
Exportacion: marca, modelo y subpartida bajo cada producto y la condicion de entrega (Incoterm) bajo los datos del documento.
"""
import copy
import xml.etree.ElementTree as ET

ROOT_DIR = 'C:/Desarrollos/FacilReports/templates'
SRC = ROOT_DIR + '/produccion/facilfactura_fe-std_conlogo_218c0c3d7f964105.repx'
OUT_MANDATO = ROOT_DIR + '/estandar/factura-mandato_conlogo.repx'
OUT_EXPORTACION = ROOT_DIR + '/estandar/factura-exportacion_conlogo.repx'

MANDATE_CELL = (
    "[Codigo] + ' — ' + [Nombre] + Iif(Len([MandanteTexto]) > 0, '\n' + [MandanteTexto] + "
    "Iif(Len([MandanteNombre]) > 0, ' — ' + [MandanteNombre], ''), '')"
)
EXPORT_CELL = (
    "[Codigo] + ' — ' + [Nombre]"
    " + Iif(Len([Marca]) > 0, '\n' + 'Marca: ' + [Marca], '')"
    " + Iif(Len([Modelo]) > 0, '\n' + 'Modelo: ' + [Modelo], '')"
    " + Iif(Len([PartidaArancelaria]) > 0, '\n' + 'Subpartida arancelaria: ' + [PartidaArancelaria], '')"
)

OBSERVACIONES_BAND = '''<Item99 Ref="{r0}" ControlType="DetailReportBand" Name="DetailReportObservaciones" Level="1" DataMember="Observaciones" Dpi="254">
  <Bands>
    <Item1 Ref="{r1}" ControlType="ReportHeaderBand" Name="HeaderObservaciones" HeightF="60" Dpi="254">
      <Controls>
        <Item1 Ref="{r2}" ControlType="XRLabel" Name="lblObsTitulo" Text="Observaciones" SizeF="1959,40" LocationFloat="0,20" Dpi="254" Font="DejaVu Sans, 8pt, style=Bold" ForeColor="255,90,90,90">
          <StylePriority Ref="{r3}" UseFont="false" UseForeColor="false" />
        </Item1>
      </Controls>
    </Item1>
    <Item2 Ref="{r4}" ControlType="DetailBand" Name="DetailObservaciones" HeightF="40" Dpi="254">
      <Controls>
        <Item1 Ref="{r5}" ControlType="XRLabel" Name="lblObsTexto" Multiline="true" CanGrow="true" SizeF="1959,40" LocationFloat="0,0" Dpi="254" Font="DejaVu Sans, 7.5pt" ForeColor="255,90,90,90">
          <ExpressionBindings>
            <Item1 Ref="{r6}" EventName="BeforePrint" PropertyName="Text" Expression="[Texto]" />
          </ExpressionBindings>
          <StylePriority Ref="{r7}" UseFont="false" UseForeColor="false" />
        </Item1>
      </Controls>
    </Item2>
  </Bands>
</Item99>'''

INCOTERM_LABEL = '''<Item99 Ref="{r0}" ControlType="XRLabel" Name="lblIncoterm" SizeF="1939,40" LocationFloat="10,{y}" Dpi="254" Font="{font}">
  <ExpressionBindings>
    <Item1 Ref="{r1}" EventName="BeforePrint" PropertyName="Text" Expression="'Condición de entrega: ' + [Documento.Incoterm]" />
    <Item2 Ref="{r2}" EventName="BeforePrint" PropertyName="Visible" Expression="Len([Documento.Incoterm]) &gt; 0" />
  </ExpressionBindings>
  <StylePriority Ref="{r3}" UseFont="false" />
</Item99>'''


class Template:
    def __init__(self):
        self.tree = ET.parse(SRC)
        self.root = self.tree.getroot()
        self.parent = {c: p for p in self.root.iter() for c in p}
        self._next_ref = max(int(e.get('Ref')) for e in self.root.iter() if (e.get('Ref') or '').isdigit()) + 1

    def find(self, name):
        found = [e for e in self.root.iter() if e.get('Name') == name]
        assert len(found) == 1, (name, len(found))
        return found[0]

    def refs(self, n):
        first = self._next_ref
        self._next_ref += n
        return {'r%d' % i: first + i for i in range(n)}

    def set_cell_expression(self, expression):
        binding = self.find('cellD1').find('ExpressionBindings')[0]
        assert binding.get('PropertyName') == 'Text'
        binding.set('Expression', expression)

    def append_numbered(self, container, element):
        """Agrega un hijo a un nodo Controls/Bands y renumera sus ItemN en orden."""
        container.append(element)
        for i, child in enumerate(list(container), start=1):
            child.tag = 'Item%d' % i

    def save(self, path):
        refs = [e.get('Ref') for e in self.root.iter() if e.get('Ref')]
        assert len(refs) == len(set(refs)), 'Ref duplicado'
        ET.indent(self.tree, space='  ')
        self.tree.write(path, encoding='utf-8', xml_declaration=True)
        print('ok', path.split('/')[-1], len(refs), 'refs')


def build_mandato():
    t = Template()

    # 1. El tipo de operacion ("Mandatos") ya sale en la columna que la factura estandar trae en su fila de datos.

    # 2. Mandante bajo el producto de cada linea.
    t.set_cell_expression(MANDATE_CELL)

    # 3. Observaciones: una por fila justo despues de los items; el texto unico del pie ya no se usa.
    for b in t.find('lblObservaciones').find('ExpressionBindings'):
        if b.get('PropertyName') == 'Visible':
            b.set('Expression', '1 = 0')
    items_band = t.find('DetailReportItems')
    bands = t.parent[items_band]
    assert bands.tag == 'Bands', bands.tag
    band = ET.fromstring(OBSERVACIONES_BAND.format(**t.refs(8)))
    bands.insert(list(bands).index(items_band) + 1, band)
    for i, child in enumerate(list(bands), start=1):
        child.tag = 'Item%d' % i

    t.save(OUT_MANDATO)


def build_exportacion():
    t = Template()

    # 1. Marca, modelo y subpartida bajo el producto.
    t.set_cell_expression(EXPORT_CELL)

    # 2. Condicion de entrega (Incoterm) en su propia fila, debajo del tipo de operacion, visible solo si el documento la trae.
    panel = t.find('pnlMeta').find('Controls')
    nota = t.find('lblNotaRef')
    label = ET.fromstring(INCOTERM_LABEL.format(
        y="%g" % (float(nota.get("LocationFloat").split(",")[1]) + 40), font=t.find('lblMeta1v').get('Font'), **t.refs(4)))
    t.append_numbered(panel, label)

    t.save(OUT_EXPORTACION)


build_mandato()
build_exportacion()
