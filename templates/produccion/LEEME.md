# Plantillas de producción estandarizadas

49 archivos `.repx` con **el mismo nombre** que las plantillas que había en el servidor (el nombre es la
`templateKey`), cada uno reemplazado por el estándar de su familia, tamaño y posición del QR
(ver `../estandar/LEEME.md` y `_mapeo.csv`). Cargarlos en su lugar no cambia ningún identificador.

Quedaron fuera los 10 archivos de prueba (`*test_*`), que se dejan tal cual están en el servidor.

Generados con `tools/apply_standard.py`:
```
python3 tools/apply_standard.py <carpeta_con_los_repx_originales> templates/estandar <salida>
```

**Orden de despliegue:** primero Facil-Reports y estas plantillas; los mappers nuevos de Facil-Factura
(documento soporte y nómina) al final, porque las plantillas de esas dos familias usan el contrato nuevo.
