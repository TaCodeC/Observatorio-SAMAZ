#!/usr/bin/env python3
"""Build the compact HIP-polyline constellation catalogue used by the Unity beta.

The app already owns the J2000 positions through HYG.  This importer intentionally
keeps only Stellarium's constellation topology (ordered HIP identifiers), so runtime
never has a second source of star coordinates to keep in sync.

Usage:
    python3 Tools/import_stellarium_modern_constellations.py
    python3 Tools/import_stellarium_modern_constellations.py --input modern-index.json

The default source is pinned to one Stellarium commit and checked by SHA-256.  This
makes updates reviewable: use --allow-different-source only after updating attribution,
coverage validation, and the expected regression test values together.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys
from typing import List, Optional
import urllib.request


SOURCE_COMMIT = "daace2add6a1bf886e8ee1934f51e9c69f818d18"
SOURCE_URL = (
    "https://raw.githubusercontent.com/Stellarium/stellarium/"
    f"{SOURCE_COMMIT}/skycultures/modern/index.json"
)
SOURCE_SHA256 = "1f2f5ffd6c9e25a7d0dcfdbf1f756e2db03dd3b8ed4ec016a2839b09f6b0fe1e"
SOURCE_LICENSE = "CC BY-SA 4.0"
ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / "Assets/Resources/Constellations/ModernConstellationLines.json"


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--input",
        type=Path,
        help="Ruta a index.json ya descargado. Si se omite se descarga el origen fijado.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
        help=f"Catálogo Unity de salida (por defecto: {DEFAULT_OUTPUT})",
    )
    parser.add_argument(
        "--allow-different-source",
        action="store_true",
        help="Permite una suma SHA-256 distinta; úsalo sólo al actualizar deliberadamente la fuente.",
    )
    return parser.parse_args()


def read_source(input_path: Optional[Path]) -> bytes:
    if input_path is not None:
        return input_path.read_bytes()

    print(f"Descargando trazado moderno de Stellarium:\n  {SOURCE_URL}")
    with urllib.request.urlopen(SOURCE_URL, timeout=30) as response:
        return response.read()


def constellation_code(source_id: str) -> str:
    parts = source_id.split()
    if len(parts) < 3 or parts[0] != "CON":
        raise ValueError(f"Identificador de constelación no reconocido: {source_id!r}")
    return parts[-1]


def display_name(source_figure: dict, fallback: str) -> str:
    common_name = source_figure.get("common_name") or {}
    return common_name.get("native") or common_name.get("english") or fallback


def convert(source: dict) -> List[dict]:
    figures: List[dict] = []
    for source_figure in source.get("constellations", []):
        code = constellation_code(source_figure["id"])
        polylines: List[dict] = []
        for source_line in source_figure.get("lines", []):
            hip_ids = [int(hip_id) for hip_id in source_line]
            if len(hip_ids) < 2 or any(hip_id <= 0 for hip_id in hip_ids):
                raise ValueError(f"Polilínea HIP inválida en {code}: {source_line!r}")
            polylines.append({"hipIds": hip_ids})

        if not polylines:
            raise ValueError(f"La constelación {code} no tiene polilíneas utilizables.")

        figures.append(
            {
                "code": code,
                "displayName": display_name(source_figure, code),
                "polylines": polylines,
            }
        )

    figures.sort(key=lambda figure: figure["code"])
    return figures


def main() -> int:
    arguments = parse_arguments()
    source_bytes = read_source(arguments.input)
    actual_sha256 = hashlib.sha256(source_bytes).hexdigest()
    if actual_sha256 != SOURCE_SHA256 and not arguments.allow_different_source:
        print(
            "ERROR: La suma SHA-256 no coincide con el origen fijado.\n"
            f"Esperada: {SOURCE_SHA256}\nRecibida: {actual_sha256}\n"
            "Revisa la actualización o usa --allow-different-source de forma explícita.",
            file=sys.stderr,
        )
        return 2

    source = json.loads(source_bytes.decode("utf-8"))
    figures = convert(source)
    polyline_count = sum(len(figure["polylines"]) for figure in figures)
    segment_count = sum(
        len(polyline["hipIds"]) - 1
        for figure in figures
        for polyline in figure["polylines"]
    )
    unique_hips = {
        hip_id
        for figure in figures
        for polyline in figure["polylines"]
        for hip_id in polyline["hipIds"]
    }

    output = {
        "schemaVersion": 1,
        "sourceName": "Stellarium modern sky culture",
        "sourceRepository": "https://github.com/Stellarium/stellarium",
        "sourceCommit": SOURCE_COMMIT,
        "sourceLicense": SOURCE_LICENSE,
        "sourceFileSha256": actual_sha256,
        "figures": figures,
    }

    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(
        json.dumps(output, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(
        f"Escrito {arguments.output}\n"
        f"  Figuras: {len(figures)}\n"
        f"  Polilíneas: {polyline_count}\n"
        f"  Segmentos: {segment_count}\n"
        f"  HIP únicos: {len(unique_hips)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
