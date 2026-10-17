import xml.etree.ElementTree as ET

SRC_DIR = 'C:/Desarrollos/FacilReports/templates/estandar'
VARIANTS = ['qr-abajo', 'qr-arriba']


def build(variant):
    src = '%s/factura_carta_%s.repx' % (SRC_DIR, variant)
    out = '%s/factura-mandato_carta_%s.repx' % (SRC_DIR, variant)
    tree = ET.parse(src)
    root = tree.getroot()
    parent_map = {c: p for p in root.iter() for c in p}

    def find(name):
        found = [e for e in root.iter() if e.get('Name') == name]
        assert len(found) == 1, (name, len(found))
        return found[0]

    # --- 1. Encabezado: sexta columna "Tipo de operación" ---------------------------------
    cols = {
        'lblMeta1k': ('410,25', '10,10'), 'lblMeta1v': ('410,30', '10,35'),
        'lblMeta2k': ('340,25', '430,10'), 'lblMeta2v': ('340,30', '430,35'),
        'lblMeta3k': ('220,25', '780,10'), 'lblMeta3v': ('220,30', '780,35'),
        'lblMeta4k': ('290,25', '1010,10'), 'lblMeta4v': ('290,30', '1010,35'),
        'lblMeta5k': ('260,25', '1310,10'), 'lblMeta5v': ('260,30', '1310,35'),
    }
    for name, (size, loc) in cols.items():
        e = find(name)
        e.set('SizeF', size)
        e.set('LocationFloat', loc)

    panel_controls = find('pnlMeta').find('Controls')
    existing = [c.tag for c in panel_controls]
    assert 'Item12' not in existing and 'Item13' not in existing, existing
    new_meta = ET.fromstring('''<Controls>
  <Item12 Ref="1100" ControlType="XRLabel" Name="lblMeta6k" Text="Tipo de operación" SizeF="370,25" LocationFloat="1570,10" Dpi="254" Font="DejaVu Sans, 6.5pt, style=Bold" ForeColor="Gray">
    <StylePriority Ref="1101" UseFont="false" UseForeColor="false" />
  </Item12>
  <Item13 Ref="1102" ControlType="XRLabel" Name="lblMeta6v" SizeF="370,30" LocationFloat="1570,35" Dpi="254" Font="DejaVu Sans, 8pt, style=Bold">
    <ExpressionBindings>
      <Item1 Ref="1103" EventName="BeforePrint" PropertyName="Text" Expression="[Documento.TipoOperacion]" />
    </ExpressionBindings>
    <StylePriority Ref="1104" UseFont="false" />
  </Item13>
</Controls>''')
    for child in list(new_meta):
        panel_controls.append(child)

    # --- 2. Descripción de cada línea: mandante bajo el producto --------------------------
    binding = find('cellD1').find('ExpressionBindings')[0]
    assert binding.get('PropertyName') == 'Text'
    binding.set('Expression',
                "[Codigo] + ' — ' + [Nombre] + Iif(Len([MandanteTexto]) > 0, '\n' + [MandanteTexto] + "
                "Iif(Len([MandanteNombre]) > 0, ' — ' + [MandanteNombre], ''), '')")

    # --- 3. Observaciones: grupo de detalle justo después de los ítems --------------------
    for b in find('lblObservaciones').find('ExpressionBindings'):
        if b.get('PropertyName') == 'Visible':
            b.set('Expression', '1 = 0')  # el texto único del pie ya no se usa: salen una por fila bajo los ítems

    items_band = find('DetailReportItems')
    bands = parent_map[items_band]
    assert bands.tag == 'Bands', bands.tag
    obs_band = ET.fromstring('''<Item99 Ref="1000" ControlType="DetailReportBand" Name="DetailReportObservaciones" Level="1" DataMember="Observaciones" Dpi="254">
  <Bands>
    <Item1 Ref="1001" ControlType="ReportHeaderBand" Name="HeaderObservaciones" HeightF="60" Dpi="254">
      <Controls>
        <Item1 Ref="1002" ControlType="XRLabel" Name="lblObsTitulo" Text="Observaciones" SizeF="1959,40" LocationFloat="0,20" Dpi="254" Font="DejaVu Sans, 8pt, style=Bold" ForeColor="255,90,90,90">
          <StylePriority Ref="1003" UseFont="false" UseForeColor="false" />
        </Item1>
      </Controls>
    </Item1>
    <Item2 Ref="1004" ControlType="DetailBand" Name="DetailObservaciones" HeightF="40" Dpi="254">
      <Controls>
        <Item1 Ref="1005" ControlType="XRLabel" Name="lblObsTexto" Multiline="true" CanGrow="true" SizeF="1959,40" LocationFloat="0,0" Dpi="254" Font="DejaVu Sans, 7.5pt" ForeColor="255,90,90,90">
          <ExpressionBindings>
            <Item1 Ref="1006" EventName="BeforePrint" PropertyName="Text" Expression="[Texto]" />
          </ExpressionBindings>
          <StylePriority Ref="1007" UseFont="false" UseForeColor="false" />
        </Item1>
      </Controls>
    </Item2>
  </Bands>
</Item99>''')
    bands.insert(list(bands).index(items_band) + 1, obs_band)
    for i, child in enumerate(list(bands), start=1):
        child.tag = 'Item%d' % i

    refs = [e.get('Ref') for e in root.iter() if e.get('Ref')]
    assert len(refs) == len(set(refs)), 'Ref duplicado'

    ET.indent(tree, space='  ')
    tree.write(out, encoding='utf-8', xml_declaration=True)
    print('ok', out, len(refs), 'refs')


for v in VARIANTS:
    build(v)
