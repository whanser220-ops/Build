#!/usr/bin/env python3
"""Validate AI-authored GPU pass catalog coverage.

This script is intentionally validation-only. It must not generate catalog
semantics, because gpu_pass_catalog.yaml and *.compute.ai.yaml are maintained
by AI/humans after reading the local C#/HLSL implementation.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys
from typing import Any

try:
    import yaml
except ImportError as exc:  # pragma: no cover - local tool dependency.
    print(f"PyYAML is required: {exc}", file=sys.stderr)
    raise SystemExit(2)


DEFAULT_CATALOG = Path("docs/gpu-pass-catalog/gpu_pass_catalog.yaml")
DEFAULT_SHADER_DOC = Path("docs/gpu-pass-catalog/shaders/CrowdVatIndirect.compute.ai.yaml")
DEFAULT_COMPUTE = Path("Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute")
DEFAULT_GPU_DEBUG_CS = Path("Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.GpuDebug.cs")
DEFAULT_SCHEMA = Path("docs/gpu-pass-catalog/schema/gpu_pass_catalog.schema.json")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Validate GPU pass catalog and compute shader AI docs.")
    parser.add_argument("--repo", default=".", help="Repository root.")
    parser.add_argument("--catalog", default=str(DEFAULT_CATALOG), help="GPU pass catalog YAML.")
    parser.add_argument("--shader-doc", default=str(DEFAULT_SHADER_DOC), help="Compute shader AI YAML.")
    parser.add_argument("--compute", default=str(DEFAULT_COMPUTE), help="Compute shader file to compare #pragma kernels.")
    parser.add_argument("--gpu-debug-cs", default=str(DEFAULT_GPU_DEBUG_CS), help="C# GPU pass debug registry source.")
    parser.add_argument("--schema", default=str(DEFAULT_SCHEMA), help="Optional JSON schema for catalog shape.")
    return parser


def load_yaml(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)
    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError(f"Expected mapping in {path}")
    return data


def validate_schema(catalog_path: Path, schema_path: Path, failures: list[str]) -> None:
    if not schema_path.exists():
        return
    try:
        import jsonschema
    except ImportError:
        print("jsonschema unavailable; skipping schema validation")
        return

    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    data = yaml.safe_load(catalog_path.read_text(encoding="utf-8"))
    try:
        jsonschema.validate(data, schema)
    except Exception as exc:  # pragma: no cover - exact jsonschema exception varies.
        failures.append(f"catalog schema validation failed: {exc}")


def require(condition: bool, failures: list[str], message: str) -> None:
    if not condition:
        failures.append(message)


def validate_catalog(repo: Path, catalog: dict[str, Any], failures: list[str]) -> dict[str, dict[str, Any]]:
    passes = catalog.get("passes")
    require(isinstance(passes, list) and len(passes) > 0, failures, "catalog.passes must be a non-empty list")
    by_id: dict[str, dict[str, Any]] = {}
    aliases: set[str] = set()

    for index, item in enumerate(passes if isinstance(passes, list) else []):
        if not isinstance(item, dict):
            failures.append(f"catalog pass #{index} is not a mapping")
            continue

        pass_id = str(item.get("pass_id", "")).strip()
        require(bool(pass_id), failures, f"catalog pass #{index} missing pass_id")
        require(pass_id not in by_id, failures, f"duplicate pass_id: {pass_id}")
        if pass_id:
            by_id[pass_id] = item

        for key in ("display_name", "type", "owner", "purpose"):
            require(bool(str(item.get(key, "")).strip()), failures, f"{pass_id} missing {key}")

        item_aliases = item.get("aliases")
        require(isinstance(item_aliases, list) and len(item_aliases) > 0, failures, f"{pass_id} missing aliases")
        for alias in item_aliases if isinstance(item_aliases, list) else []:
            aliases.add(str(alias))

        source = item.get("source")
        require(isinstance(source, dict), failures, f"{pass_id} missing source")
        if isinstance(source, dict):
            source_file = str(source.get("file", "")).strip()
            source_function = str(source.get("function", "")).strip()
            require(bool(source_file), failures, f"{pass_id} missing source.file")
            require(bool(source_function), failures, f"{pass_id} missing source.function")
            if source_file:
                require((repo / source_file).exists(), failures, f"{pass_id} source.file does not exist: {source_file}")

        pass_type = str(item.get("type", ""))
        if pass_type == "compute":
            for key in ("shader", "kernel", "dispatch", "performance_hints"):
                require(bool(item.get(key)), failures, f"{pass_id} missing {key}")
            require(isinstance(item.get("inputs"), list), failures, f"{pass_id} inputs must be a list")
            require(isinstance(item.get("outputs"), list), failures, f"{pass_id} outputs must be a list")
        elif pass_type == "raster_draw":
            require(bool(item.get("shader")), failures, f"{pass_id} missing shader")
            require(isinstance(item.get("inputs"), list), failures, f"{pass_id} inputs must be a list")
            require(bool(item.get("raster_draw") or item.get("draw")), failures, f"{pass_id} missing raster_draw metadata")
        elif pass_type == "indirect_draw":
            require(bool(item.get("shader")), failures, f"{pass_id} missing shader")
            require(isinstance(item.get("inputs"), list), failures, f"{pass_id} inputs must be a list")
            require(bool(item.get("indirect_draw") or item.get("draw")), failures, f"{pass_id} missing indirect_draw metadata")
        else:
            failures.append(f"{pass_id} has unsupported type: {pass_type}")

    return by_id


def parse_compute_kernels(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    return re.findall(r"^#pragma\s+kernel\s+(\w+)", text, flags=re.MULTILINE)


def validate_shader_doc(
    shader_doc: dict[str, Any],
    compute_kernels: list[str],
    catalog_by_id: dict[str, dict[str, Any]],
    failures: list[str],
) -> None:
    kernels = shader_doc.get("kernels")
    require(isinstance(kernels, list) and len(kernels) > 0, failures, "shader doc kernels must be a non-empty list")

    documented = []
    for index, item in enumerate(kernels if isinstance(kernels, list) else []):
        if not isinstance(item, dict):
            failures.append(f"shader doc kernel #{index} is not a mapping")
            continue
        name = str(item.get("name", "")).strip()
        pass_id = str(item.get("pass_id", "")).strip()
        documented.append(name)
        require(bool(name), failures, f"shader doc kernel #{index} missing name")
        require(pass_id in catalog_by_id, failures, f"{name} pass_id not found in catalog: {pass_id}")
        for key in ("responsibility", "data_parallel_unit", "input_scale", "memory_access", "known_risks", "optimization_notes"):
            require(bool(item.get(key)), failures, f"{name} missing {key}")
        if pass_id in catalog_by_id:
            catalog_kernel = str(catalog_by_id[pass_id].get("kernel", "")).strip()
            require(catalog_kernel == name, failures, f"{pass_id} catalog kernel '{catalog_kernel}' != shader doc kernel '{name}'")

    missing = sorted(set(compute_kernels) - set(documented))
    extra = sorted(set(documented) - set(compute_kernels))
    require(not missing, failures, f"shader doc missing compute kernels: {missing}")
    require(not extra, failures, f"shader doc has kernels not in compute file: {extra}")
    require(len(documented) == len(set(documented)), failures, "shader doc contains duplicate kernel names")


def validate_gpu_debug_aliases(path: Path, catalog: dict[str, Any], failures: list[str]) -> None:
    text = path.read_text(encoding="utf-8")
    registered_marker_matches = re.findall(r'private\s+const\s+string\s+(GpuPass\w+)\s*=\s*"([^"]+)";', text)
    registered_markers = [
        value
        for name, value in registered_marker_matches
        if not name.endswith("PassId")
    ]
    aliases = {
        str(alias)
        for item in catalog.get("passes", [])
        if isinstance(item, dict)
        for alias in item.get("aliases", [])
    }
    missing = sorted(set(registered_markers) - aliases)
    require(not missing, failures, f"catalog aliases missing registered GPU markers: {missing}")


def main() -> int:
    args = build_parser().parse_args()
    repo = Path(args.repo).resolve()
    catalog_path = (repo / args.catalog).resolve()
    shader_doc_path = (repo / args.shader_doc).resolve()
    compute_path = (repo / args.compute).resolve()
    gpu_debug_cs_path = (repo / args.gpu_debug_cs).resolve()
    schema_path = (repo / args.schema).resolve()

    failures: list[str] = []
    try:
        catalog = load_yaml(catalog_path)
        shader_doc = load_yaml(shader_doc_path)
        compute_kernels = parse_compute_kernels(compute_path)
        validate_schema(catalog_path, schema_path, failures)
        catalog_by_id = validate_catalog(repo, catalog, failures)
        validate_shader_doc(shader_doc, compute_kernels, catalog_by_id, failures)
        validate_gpu_debug_aliases(gpu_debug_cs_path, catalog, failures)
    except Exception as exc:
        failures.append(str(exc))

    if failures:
        print("GPU pass catalog validation failed:")
        for failure in failures:
            print(f"- {failure}")
        return 1

    print(
        "GPU pass catalog validation ok: "
        f"passes={len(catalog.get('passes', []))}, "
        f"compute_kernels={len(compute_kernels)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
