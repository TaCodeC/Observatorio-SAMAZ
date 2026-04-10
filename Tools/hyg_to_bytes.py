#!/usr/bin/env python3
"""
hyg_to_bytes.py — Convierte el catálogo HYG (CSV) a formato binario .bytes
para carga eficiente en Unity/Quest 3.

Uso:
    python hyg_to_bytes.py                          # descarga HYG v4.2 automáticamente
    python hyg_to_bytes.py --input hyg_v42.csv      # usa CSV local
    python hyg_to_bytes.py --output-dir ./out        # directorio de salida personalizado
    python hyg_to_bytes.py --mag-limit 6.0           # límite de magnitud diferente

Salida:
    hyg_stars.bytes   — datos binarios (header 16B + records de 48B)
    star_names.json   — mapeo { "hip_id": "nombre propio" }

Requisitos: Python 3.8+ (solo stdlib, sin dependencias externas)

Formato binario
===============

Header (16 bytes):
    Offset  Tipo        Descripción
    0       char[4]     Magic: b'HYG\\x00'
    4       uint32 LE   Versión: 1
    8       uint32 LE   Cantidad de estrellas (N)
    12      uint32 LE   Reservado (0)

Record por estrella (48 bytes, × N):
    Offset  Tipo        Descripción
    0       int32 LE    HIP ID (-1 si no tiene)
    4       float32 LE  Right Ascension en radianes
    8       float32 LE  Declinación en radianes
    12      float32 LE  Magnitud aparente
    16      float32 LE  Color index (B-V), NaN si falta
    20      float32 LE  Distancia en parsecs
    24      float32 LE  Luminosidad (unidades solares), NaN si falta
    28      char[8]     Tipo espectral (null-padded)
    36      char[4]     Constelación IAU (null-padded)
    40      char[4]     Reservado
    44      uint32 LE   Flags (bit 0: tiene nombre propio)

Struct C# correspondiente (para deserialización en Unity — Fase 1):

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StarRecord
    {
        public int hipId;
        public float raRad;
        public float decRad;
        public float magnitude;
        public float colorIndex;
        public float distance;
        public float luminosity;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] spectralType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] constellation;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] reserved;
        public uint flags;
    }  // 48 bytes

Lectura en Android/Quest (StreamingAssets):
    Usar UnityWebRequest.Get(Application.streamingAssetsPath + "/Data/hyg_stars.bytes")
    NO usar File.ReadAllBytes — no funciona con StreamingAssets en Android.
"""

import argparse
import csv
import gzip
import io
import json
import math
import os
import struct
import sys
import urllib.request

# --- Constantes -----------------------------------------------------------

MAGIC = b"HYG\x00"
FORMAT_VERSION = 1
RECORD_FMT = "<i fff ff f 8s 4s 4s I"
RECORD_SIZE = struct.calcsize(RECORD_FMT)  # debe ser 48
HEADER_FMT = "<4s I I I"
HEADER_SIZE = struct.calcsize(HEADER_FMT)  # debe ser 16

DEFAULT_MAG_LIMIT = 7.0
HYG_URL = (
    "https://raw.githubusercontent.com/astronexus/HYG-Database"
    "/main/hyg/CURRENT/hygdata_v41.csv"
)

# Columnas requeridas del CSV
REQUIRED_COLS = {"hip", "ra", "dec", "mag"}


def download_catalog(url: str) -> str:
    """Descarga el catálogo HYG y devuelve el contenido como string."""
    print(f"Descargando catálogo desde:\n  {url}")
    req = urllib.request.Request(url, headers={"User-Agent": "hyg_to_bytes/1.0"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        raw = resp.read()

    # Detectar si es gzip
    if url.endswith(".gz") or raw[:2] == b"\x1f\x8b":
        raw = gzip.decompress(raw)

    text = raw.decode("utf-8", errors="replace")
    print(f"  Descargado: {len(text):,} caracteres")
    return text


def safe_float(value: str, default: float = math.nan) -> float:
    """Convierte string a float, devuelve default si está vacío o falla."""
    if not value or not value.strip():
        return default
    try:
        return float(value)
    except (ValueError, TypeError):
        return default


def safe_int(value: str, default: int = -1) -> int:
    """Convierte string a int, devuelve default si está vacío o falla."""
    if not value or not value.strip():
        return default
    try:
        return int(float(value))
    except (ValueError, TypeError):
        return default


def encode_fixed_str(s: str, length: int) -> bytes:
    """Codifica string a bytes de largo fijo, truncado y null-padded."""
    encoded = s.encode("utf-8", errors="replace")[:length]
    return encoded.ljust(length, b"\x00")


def parse_catalog(csv_text: str, mag_limit: float) -> list:
    """Parsea el CSV y filtra estrellas por magnitud. Devuelve lista de dicts."""
    reader = csv.DictReader(io.StringIO(csv_text))

    # Verificar columnas
    if reader.fieldnames is None:
        raise ValueError("El CSV no tiene encabezado")

    cols = set(reader.fieldnames)
    missing = REQUIRED_COLS - cols
    if missing:
        raise ValueError(f"Columnas faltantes en CSV: {missing}")

    stars = []
    skipped = 0

    for row in reader:
        mag = safe_float(row.get("mag", ""))
        if math.isnan(mag) or mag > mag_limit:
            skipped += 1
            continue

        ra_hours = safe_float(row.get("ra", ""))
        dec_deg = safe_float(row.get("dec", ""))
        if math.isnan(ra_hours) or math.isnan(dec_deg):
            skipped += 1
            continue

        star = {
            "hip": safe_int(row.get("hip", "")),
            "ra_rad": ra_hours * (math.pi / 12.0),
            "dec_rad": dec_deg * (math.pi / 180.0),
            "mag": mag,
            "ci": safe_float(row.get("ci", "")),
            "dist": safe_float(row.get("dist", ""), default=0.0),
            "lum": safe_float(row.get("lum", "")),
            "spect": (row.get("spect", "") or "").strip(),
            "con": (row.get("con", "") or "").strip(),
            "proper": (row.get("proper", "") or "").strip(),
        }
        stars.append(star)

    # Ordenar por magnitud (más brillantes primero)
    stars.sort(key=lambda s: s["mag"])

    print(f"  Total filas en CSV: {len(stars) + skipped:,}")
    print(f"  Estrellas con mag <= {mag_limit}: {len(stars):,}")
    print(f"  Descartadas: {skipped:,}")

    return stars


def write_bytes(stars: list, output_path: str) -> None:
    """Escribe el archivo binario .bytes."""
    assert RECORD_SIZE == 48, f"Record size inesperado: {RECORD_SIZE}"
    assert HEADER_SIZE == 16, f"Header size inesperado: {HEADER_SIZE}"

    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    with open(output_path, "wb") as f:
        # Header
        f.write(struct.pack(HEADER_FMT, MAGIC, FORMAT_VERSION, len(stars), 0))

        # Records
        for s in stars:
            has_name = 1 if s["proper"] else 0
            record = struct.pack(
                RECORD_FMT,
                s["hip"],
                s["ra_rad"],
                s["dec_rad"],
                s["mag"],
                s["ci"],
                s["dist"],
                s["lum"],
                encode_fixed_str(s["spect"], 8),
                encode_fixed_str(s["con"], 4),
                b"\x00\x00\x00\x00",  # reserved
                has_name,
            )
            f.write(record)

    file_size = os.path.getsize(output_path)
    print(f"\n  hyg_stars.bytes: {file_size:,} bytes ({file_size / 1024:.1f} KB)")
    print(f"  Header: {HEADER_SIZE} bytes + {len(stars)} records × {RECORD_SIZE} bytes")


def write_names(stars: list, output_path: str) -> None:
    """Escribe el JSON de nombres propios (HIP ID -> nombre)."""
    names = {}
    for s in stars:
        if s["proper"] and s["hip"] != -1:
            names[str(s["hip"])] = s["proper"]

    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(names, f, ensure_ascii=False, indent=2, sort_keys=True)

    print(f"  star_names.json: {len(names)} estrellas con nombre propio")


def main():
    parser = argparse.ArgumentParser(
        description="Convierte el catálogo HYG (CSV) a binario .bytes para Unity"
    )
    parser.add_argument(
        "--input",
        help="Ruta al CSV local del catálogo HYG (si se omite, descarga automáticamente)",
    )
    parser.add_argument(
        "--output-dir",
        default=os.path.join(
            os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
            "Assets",
            "StreamingAssets",
            "Data",
        ),
        help="Directorio de salida (default: ../Assets/StreamingAssets/Data/)",
    )
    parser.add_argument(
        "--mag-limit",
        type=float,
        default=DEFAULT_MAG_LIMIT,
        help=f"Magnitud aparente máxima (default: {DEFAULT_MAG_LIMIT})",
    )
    parser.add_argument(
        "--url",
        default=HYG_URL,
        help="URL alternativa para descargar el catálogo",
    )
    args = parser.parse_args()

    print("=" * 55)
    print("  HYG → .bytes  |  Observatorio Estelar SAMAZ")
    print("=" * 55)

    # 1. Obtener CSV
    if args.input:
        path = args.input
        print(f"\nLeyendo CSV local: {path}")
        open_fn = gzip.open if path.endswith(".gz") else open
        with open_fn(path, "rt", encoding="utf-8", errors="replace") as f:
            csv_text = f.read()
        print(f"  Leído: {len(csv_text):,} caracteres")
    else:
        print()
        csv_text = download_catalog(args.url)

    # 2. Parsear y filtrar
    print(f"\nFiltrando estrellas (magnitud <= {args.mag_limit})...")
    stars = parse_catalog(csv_text, args.mag_limit)

    if not stars:
        print("ERROR: No se encontraron estrellas. Verifica el CSV.", file=sys.stderr)
        sys.exit(1)

    # 3. Escribir binario
    bytes_path = os.path.join(args.output_dir, "hyg_stars.bytes")
    print(f"\nEscribiendo binario...")
    write_bytes(stars, bytes_path)

    # 4. Escribir nombres
    names_path = os.path.join(args.output_dir, "star_names.json")
    write_names(stars, names_path)

    # Resumen
    print(f"\n{'=' * 55}")
    print(f"  Listo. Archivos generados en:")
    print(f"    {bytes_path}")
    print(f"    {names_path}")
    print(f"{'=' * 55}")


if __name__ == "__main__":
    main()
