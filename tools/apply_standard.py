#!/usr/bin/env python3
"""Reemplaza cada .repx por el estándar de su familia/tamaño/posición de QR, conservando el nombre del
archivo (templateKey) y el nombre interno del informe. Uso: apply_standard.py <entrada> <estandar> <salida>"""
import sys, os, glob, shutil, csv
import xml.etree.ElementTree as ET
IN, STD, OUT = sys.argv[1:4]
os.makedirs(OUT, exist_ok=True)
SIZE = {('2159', '2794'): 'carta', ('2159', '1397'): 'mediacarta', ('800', '3500'): 'pos'}

def classify(path):
    name = os.path.basename(path).lower()
    root = ET.parse(path).getroot()
    pw, ph = root.get('PageWidth'), root.get('PageHeight')
    if pw is None:
        return None, 'vacío (sin bandas ni tamaño)'
    if 'test_' in name:
        return None, 'prueba de comportamiento (se deja igual)'
    size = SIZE.get((pw, ph))
    if size is None:
        return None, f'tamaño {pw}x{ph} sin estándar'
    if 'ne-pago' in name: fam = 'nomina'
    elif 'de-pos' in name: fam = 'pos'
    elif '_ds' in name or name.startswith('v2_ds'): fam = 'documento-soporte'
    else: fam = 'factura'
    qr_band = None
    for b in (root.find('Bands') or []):
        for e in b.iter():
            if e.get('Name') == 'picQr':
                qr_band = b.get('ControlType')
    qr = 'qr-arriba' if qr_band == 'ReportHeaderBand' else 'qr-abajo'
    std = f"pos_80mm_{qr}.repx" if fam == 'pos' else f"{fam}_{size}_{qr}.repx"
    return std, ''

rows = []
for f in sorted(glob.glob(os.path.join(IN, '*.repx'))):
    base = os.path.basename(f)
    std, why = classify(f)
    dst = os.path.join(OUT, base)
    if std is None:
        shutil.copyfile(f, dst); rows.append((base, 'SIN CAMBIOS', why)); continue
    orig_name = ET.parse(f).getroot().get('Name')
    tree = ET.parse(os.path.join(STD, std)); root = tree.getroot()
    if orig_name: root.set('Name', orig_name)
    ET.indent(root, space='  ')
    tree.write(dst, encoding='utf-8', xml_declaration=True)
    rows.append((base, std, ''))
with open(os.path.join(OUT, '_mapeo.csv'), 'w', newline='', encoding='utf-8-sig') as fh:
    w = csv.writer(fh); w.writerow(['archivo', 'estandar_aplicado', 'nota']); w.writerows(rows)
for r in rows: print(f"{r[0][:62]:62} -> {r[1]} {r[2]}")
