# Plantillas estándar (.repx)

Generadas con `tools/gen_repx.py` y `tools/gen2.py` a partir de `_base/fe-std-base.repx` (una plantilla que el
diseñador de DevExpress ya abre). Sirven en **los dos motores** (FastReport y DevExpress): son `.repx` normales.

| Familia | Formatos | Variantes |
|---|---|---|
| `factura_*` (también notas débito/crédito) | carta, media carta | `qr-arriba` / `qr-abajo` |
| `documento-soporte_*` | carta, media carta | `qr-arriba` / `qr-abajo` |
| `nomina_*` | carta, media carta | `qr-arriba` / `qr-abajo` |
| `pos_80mm_*` | tirilla 80 mm | `qr-arriba` / `qr-abajo` |

Todas: `PageHeader` y `PageFooter` repetidos en cada hoja, paginador "PÁGINA n / N", totales al fondo de la
última hoja (si no caben pasan a una hoja nueva), detalle sin celdas con borde y que crece hacia abajo.
QR arriba: el QR va pegado al borde, junto al consecutivo. QR abajo: arriba solo el consecutivo y el QR al final.

## Contrato de datos
Todas usan el objeto `Documento` más listas. Ejemplos en `datos_ejemplo/`.
- factura y pos: `Documento`, `Items[]`, `Impuestos[]` (`Concepto`, `Valor`, `Tipo`: Iva|Retencion|Descuento|Cargo).
- documento-soporte: igual que factura pero con `Documento.Proveedor*` en lugar de `Adquirente*`.
- nomina: `Documento` + `Devengos[]` y `Deducciones[]` (`Codigo`, `Descripcion`, `Valor`).

## Regenerar
```
python3 tools/gen_repx.py templates/estandar/_base/fe-std-base.repx <salida>   # facturas
python3 tools/gen2.py     templates/estandar/_base/fe-std-base.repx <salida>   # doc. soporte, nómina, POS
```

## Pendiente de validar
Abrir una plantilla en el diseñador de DevExpress (Windows) y confirmar que abre y se ve bien.
