#!/usr/bin/env python3
"""
Post-process a loaded-scene prefab audit JSON export into a minimal prefab metrics report.

Usage:
    python tools/asset-stats/prefab_audit_metrics.py input.json
    python tools/asset-stats/prefab_audit_metrics.py input.json -o output.json
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


EPSILON = 1.0e-6


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Compute minimal prefab metrics from a loaded-scene prefab audit JSON export."
    )
    parser.add_argument("input", help="Input prefab audit JSON file exported from Unity.")
    parser.add_argument(
        "-o",
        "--output",
        help="Optional output path. Defaults to '<input>.derived.json'.",
    )
    parser.add_argument(
        "--top",
        type=int,
        default=0,
        help="Compatibility no-op. Kept so the Unity exporter can call older argument shapes safely.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    input_path = Path(args.input).resolve()
    output_path = Path(args.output).resolve() if args.output else default_output_path(input_path)

    with input_path.open("r", encoding="utf-8-sig") as handle:
        payload = json.load(handle)

    derived_report = build_prefab_metrics_report(payload)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8") as handle:
        json.dump(derived_report, handle, ensure_ascii=False, indent=2)
        handle.write("\n")

    print(output_path)
    return 0


def default_output_path(input_path: Path) -> Path:
    if input_path.suffix:
        return input_path.with_name(f"{input_path.stem}.derived{input_path.suffix}")
    return input_path.with_name(f"{input_path.name}.derived.json")


def build_prefab_metrics_report(payload: dict[str, Any]) -> dict[str, Any]:
    prefabs = payload.get("prefabs") or []

    return {
        "prefabs": [
            build_prefab_metrics_record(prefab)
            for prefab in prefabs
            if isinstance(prefab, dict)
        ],
    }


def build_prefab_metrics_record(prefab: dict[str, Any]) -> dict[str, Any]:
    lods = normalize_lod_records(prefab.get("lods"))
    highest_detail_lod = resolve_highest_detail_lod(lods)
    highest_triangle_count = int(highest_detail_lod.get("triangleCount") or 0) if highest_detail_lod else 0
    scene_usage_count = resolve_scene_usage_count(prefab)
    highest_lod_vertex_count = resolve_total_vertex_count(highest_detail_lod)
    total_vertex_count = (
        highest_lod_vertex_count * scene_usage_count
        if highest_lod_vertex_count is not None
        else None
    )
    bounds_surface_area = resolve_float(prefab.get("boundsSurfaceArea"))

    return {
        "prefabName": prefab.get("prefabName"),
        "prefabPath": prefab.get("prefabPath"),
        "totalVertexCount": total_vertex_count,
        "lodTriangleRatios": build_lod_triangle_ratios(lods, highest_triangle_count),
        "trianglesPerUnitSurfaceArea": safe_div(highest_triangle_count, bounds_surface_area),
    }


def build_lod_triangle_ratios(lods: list[dict[str, Any]], highest_triangle_count: int) -> list[dict[str, Any]]:
    ratios: list[dict[str, Any]] = []
    for lod in lods:
        triangle_count = int(lod.get("triangleCount") or 0)
        ratios.append(
            {
                "lodIndex": int(lod.get("lodIndex") or 0),
                "triangleCount": triangle_count,
                "ratioToHighestLod": safe_div(triangle_count, highest_triangle_count),
            }
        )

    return ratios


def resolve_highest_detail_lod(lods: list[dict[str, Any]]) -> dict[str, Any] | None:
    if not lods:
        return None

    return max(
        lods,
        key=lambda lod: (
            int(lod.get("triangleCount") or 0),
            -int(lod.get("lodIndex") or 0),
        ),
    )


def resolve_total_vertex_count(highest_detail_lod: dict[str, Any] | None) -> int | None:
    if not highest_detail_lod:
        return None

    vertex_count = highest_detail_lod.get("vertexCount")
    if vertex_count is None:
        return None

    return int(vertex_count or 0)


def resolve_scene_usage_count(prefab: dict[str, Any]) -> int:
    scene_usage_count = prefab.get("sceneUsageCount")
    if scene_usage_count is None:
        scene_usage_count = prefab.get("currentSceneUsageCount")

    return int(scene_usage_count or 0)


def normalize_lod_records(value: Any) -> list[dict[str, Any]]:
    if not isinstance(value, list):
        return []

    lods: list[dict[str, Any]] = []
    for item in value:
        if not isinstance(item, dict):
            continue

        lods.append(
            {
                "lodIndex": int(item.get("lodIndex") or 0),
                "triangleCount": int(item.get("triangleCount") or 0),
                "vertexCount": resolve_int(item.get("vertexCount")),
            }
        )

    lods.sort(key=lambda item: int(item.get("lodIndex") or 0))
    return lods


def resolve_int(value: Any) -> int | None:
    if value is None:
        return None

    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def resolve_float(value: Any) -> float | None:
    if value is None:
        return None

    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def safe_div(numerator: float, denominator: float | None) -> float | None:
    if denominator is None or abs(float(denominator)) <= EPSILON:
        return None

    return float(numerator) / float(denominator)


if __name__ == "__main__":
    raise SystemExit(main())
