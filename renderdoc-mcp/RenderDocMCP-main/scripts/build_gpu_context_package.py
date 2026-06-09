#!/usr/bin/env python3
"""Build AI-ready GPU pass context JSON files.

This script keeps the output factual: it aligns RenderDoc events, runtime
debug sidecars, and static shader structure, then derives only small
mechanical summaries that are safe to compute from those facts.
"""

from __future__ import annotations

import argparse
from bisect import bisect_right
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from collections import defaultdict
from typing import Any

try:
    import yaml
except ImportError:  # pragma: no cover - optional dependency in local tooling.
    yaml = None


SCHEMA_VERSION = "1.6"
DEFAULT_GPU_PASS_CATALOG = Path("docs/gpu-pass-catalog/gpu_pass_catalog.yaml")
RAW_GPU_PASS_EVENTS_FILE = "gpu_pass_events.json"
GPU_PASS_OVERVIEW_FILE = "gpu_pass_overview.json"
GPU_PASS_RESOURCES_FILE = "gpu_pass_resources.json"
GPU_PASS_CATALOG_FILE = "gpu_pass_catalog.json"
GPU_FRAME_CONTEXT_FILE = "gpu_frame_context.json"
PASS_ID_MARKER_RE = re.compile(r"^\s*\[(?P<pass_id>[A-Za-z0-9][A-Za-z0-9_.:-]*)\]")

ACCESS_SLOT_PREFIX = {
    "srv": "t",
    "uav": "u",
    "cbv": "b",
}

ACCESS_NAME = {
    "srv": "SRV",
    "uav": "UAV",
    "cbv": "CBV",
}

RESOURCE_DECL_RE = re.compile(
    r"^\s*(?P<type>(?:RW)?(?:StructuredBuffer|ByteAddressBuffer|RWByteAddressBuffer|"
    r"AppendStructuredBuffer|ConsumeStructuredBuffer|Texture\w*|RWTexture\w*|"
    r"SamplerState|SamplerComparisonState|ConstantBuffer)(?:\s*<[^;]+>)?)\s+"
    r"(?P<name>[_A-Za-z]\w*)\s*(?:\[[^\]]+\])?"
    r"\s*(?::\s*register\s*\(\s*(?P<slot>[tubs]\d+)\s*(?:,\s*space(?P<space>\d+))?\s*\))?\s*;",
    re.MULTILINE,
)

CBUFFER_RE = re.compile(
    r"^\s*cbuffer\s+(?P<name>[_A-Za-z]\w*)"
    r"\s*(?::\s*register\s*\(\s*(?P<slot>b\d+)\s*(?:,\s*space(?P<space>\d+))?\s*\))?",
    re.MULTILINE,
)

GROUPSHARED_RE = re.compile(
    r"^\s*groupshared\s+(?P<type>[^;\n]+?)\s+"
    r"(?P<name>[_A-Za-z]\w*)\s*(?:\[(?P<count>[^\]]+)\])?\s*;",
    re.MULTILINE,
)

NUMTHREADS_RE = re.compile(
    r"\[numthreads\s*\(\s*(?P<x>\d+)\s*,\s*(?P<y>\d+)\s*,\s*(?P<z>\d+)\s*\)\]"
    r"\s*(?:\[[^\]]+\]\s*)*(?:[_A-Za-z]\w*(?:\s*<[^>]+>)?\s+)?(?P<entry>[_A-Za-z]\w*)\s*\(",
    re.MULTILINE,
)

FUNCTION_DEF_RE = re.compile(
    r"(?P<attributes>(?:\s*\[[^\]]+\]\s*)*)"
    r"(?P<return>(?:[_A-Za-z]\w*(?:\s*<[^;{}()]+>)?\s+)+)"
    r"(?P<name>[_A-Za-z]\w*)\s*"
    r"\((?P<params>[^)]*)\)\s*"
    r"(?::\s*[^{\n]+)?\s*\{",
    re.MULTILINE,
)

FOR_RE = re.compile(
    r"\bfor\s*\(\s*(?P<init>[^;]*?)\s*;\s*(?P<condition>[^;]*?)\s*;\s*(?P<increment>[^)]*?)\s*\)",
    re.MULTILINE | re.DOTALL,
)

ATOMIC_RE = re.compile(r"\b(?P<name>Interlocked[_A-Za-z0-9]*)\s*\(")
BRANCH_KEYWORDS = ("if", "else if", "while")
BARRIER_RE = re.compile(
    r"\b(?P<name>(?:AllMemoryBarrier|DeviceMemoryBarrier|GroupMemoryBarrier)(?:WithGroupSync)?)\s*\("
)
INCLUDE_RE = re.compile(r"^\s*#include\s+(?:\"(?P<quoted>[^\"]+)\"|<(?P<angled>[^>]+)>)")


def utc_stamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_json(path: Path | None) -> dict[str, Any]:
    if path is None:
        return {}
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8-sig"))


def load_structured_file(path: Path | None) -> dict[str, Any]:
    if path is None or not path.exists():
        return {}

    suffix = path.suffix.lower()
    text = path.read_text(encoding="utf-8-sig")
    if suffix == ".json":
        return json.loads(text)

    if suffix in (".yaml", ".yml"):
        if yaml is None:
            raise RuntimeError("PyYAML is required to read GPU pass catalog YAML files.")
        loaded = yaml.safe_load(text)
        return loaded if isinstance(loaded, dict) else {}

    raise RuntimeError(f"Unsupported structured file type: {path}")


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def extract_marker_pass_id(value: Any) -> str:
    if value is None:
        return ""

    match = PASS_ID_MARKER_RE.match(str(value))
    if not match:
        return ""

    return match.group("pass_id").strip()


def looks_like_semantic_pass_name(value: Any) -> bool:
    text = str(value or "").strip()
    if not text:
        return False
    if extract_marker_pass_id(text):
        return True
    if "." not in text:
        return False

    technical_prefixes = (
        "ExecuteIndirect(",
        "ID3D",
        "[0] ",
        "[1] ",
    )
    if any(text.startswith(prefix) for prefix in technical_prefixes):
        return False

    engine_prefixes = (
        "RenderLoop.",
        "Shadows.",
        "UniversalRenderPipeline.",
        "FrameTime.",
        "Camera.Render",
    )
    return not any(text.startswith(prefix) for prefix in engine_prefixes)


def resolve_semantic_pass_name(raw: dict[str, Any], fallback_pass_name: str) -> str:
    marker_path = raw.get("marker_path") or raw.get("markerPath") or []
    if isinstance(marker_path, list):
        for marker in reversed(marker_path):
            if looks_like_semantic_pass_name(marker):
                return str(marker).strip()

    if extract_marker_pass_id(fallback_pass_name):
        return fallback_pass_name

    return ""


def normalize_lookup_key(value: Any) -> str:
    if value is None:
        return ""

    text = extract_marker_pass_id(value) or str(value).strip()
    return text.lower()


def same_path_or_name(left: Any, right: Any) -> bool:
    if not left or not right:
        return False

    left_text = str(left).replace("\\", "/").lower()
    right_text = str(right).replace("\\", "/").lower()
    return left_text == right_text or left_text.endswith("/" + right_text) or right_text.endswith("/" + left_text)


def import_bridge(repo: Path):
    bridge_root = repo / "renderdoc-mcp" / "RenderDocMCP-main"
    if not bridge_root.exists():
        raise RuntimeError(f"RenderDoc MCP package root not found: {bridge_root}")
    sys.path.insert(0, str(bridge_root))
    from mcp_server.bridge.client import RenderDocBridge

    return RenderDocBridge


def default_artifact_dir(repo: Path) -> Path:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return repo / ".workspace" / "artifacts" / "renderdoc-analysis" / f"gpu-context-{stamp}"


def latest_capture_dir(repo: Path) -> Path | None:
    root = repo / ".workspace" / "artifacts" / "renderdoc-captures"
    if not root.exists():
        return None
    captures = sorted(root.rglob("*.rdc"), key=lambda p: (p.stat().st_mtime, str(p)), reverse=True)
    return captures[0].parent if captures else None


def fetch_renderdoc_gpu_passes(repo: Path, capture: Path | None, args: argparse.Namespace) -> dict[str, Any]:
    bridge_type = import_bridge(repo)
    bridge = bridge_type()
    if capture is not None:
        bridge.call("open_capture", {"capture_path": str(capture)})

    params: dict[str, Any] = {
        "include_pipeline_state": not args.no_pipeline_state,
        "include_resources": not args.no_resources,
        "include_timings": not args.no_timings,
        "include_draws": args.include_draws,
        "max_passes": args.max_passes,
    }
    if args.marker_filter:
        params["marker_filter"] = args.marker_filter
    if args.exclude_marker:
        params["exclude_markers"] = args.exclude_marker
    if args.event_id_min is not None:
        params["event_id_min"] = args.event_id_min
    if args.event_id_max is not None:
        params["event_id_max"] = args.event_id_max
    return bridge.call("get_gpu_passes", params)


def fetch_shader_runtime_details(
    repo: Path,
    capture: Path | None,
    events: dict[str, Any],
    raw_event_passes: list[dict[str, Any]],
) -> dict[int, dict[str, Any]]:
    if capture is None:
        return {}

    raw_events_by_id: dict[int, dict[str, Any]] = {}
    for raw_event in raw_event_passes:
        if not isinstance(raw_event, dict):
            continue
        event_id = safe_optional_int(raw_event.get("event_id") if raw_event.get("event_id") is not None else raw_event.get("eventId"))
        if event_id is not None:
            raw_events_by_id[event_id] = raw_event

    requested_event_ids: set[int] = set()
    for event in events.get("events", []):
        event_id = safe_optional_int(event.get("event_id"))
        if event_id is None:
            continue
        raw_event = raw_events_by_id.get(event_id, {})
        resources = raw_event.get("resources") if isinstance(raw_event, dict) else {}
        cbuffers = resources.get("cbv", []) if isinstance(resources, dict) else []
        if cbuffers:
            requested_event_ids.add(event_id)

    if not requested_event_ids:
        return {}

    bridge_type = import_bridge(repo)
    bridge = bridge_type()
    bridge.call("open_capture", {"capture_path": str(capture)})

    details_by_event: dict[int, dict[str, Any]] = {}
    allowed_stages = {"vertex", "hull", "domain", "geometry", "pixel", "compute"}
    for event in events.get("events", []):
        event_id = safe_optional_int(event.get("event_id"))
        stage = first_text(event, "stage").lower()
        if event_id is None or event_id not in requested_event_ids or stage not in allowed_stages:
            continue
        try:
            shader_info = bridge.call("get_shader_info", {"event_id": event_id, "stage": stage})
        except Exception:
            continue
        details_by_event[event_id] = normalize_shader_runtime_detail(shader_info)

    return details_by_event


def normalize_debug_map(data: dict[str, Any], generated_at: str) -> dict[str, Any]:
    raw_passes = data.get("mappings") or data.get("passes", data if isinstance(data, dict) else {})
    normalized = []

    if isinstance(raw_passes, dict):
        iterator = raw_passes.items()
    elif isinstance(raw_passes, list):
        iterator = [(None, item) for item in raw_passes]
    else:
        iterator = []

    for key, raw in iterator:
        if not isinstance(raw, dict):
            continue
        pass_name = first_text(raw, "pass_name", "passName", "name") or str(key or "")
        if not pass_name:
            continue
        pass_id = first_text(raw, "pass_id", "passId") or extract_marker_pass_id(pass_name)
        shader = first_text(raw, "shader", "shader_file", "shaderFile")
        shader_identity = split_shader_identity(shader)
        entry = first_text(raw, "entry", "shader_entry", "shaderEntry")
        thread_group = normalize_thread_group(raw.get("thread_group") or [
            raw.get("threadGroupX", 0),
            raw.get("threadGroupY", 0),
            raw.get("threadGroupZ", 0),
        ])
        normalized.append(
            drop_empty(
                {
                "pass_id": pass_id,
                "pass_name": pass_name,
                "debug_label": first_text(raw, "debug_label", "debugLabel") or pass_name,
                "cpu_scope": first_text(raw, "cpu_scope", "cpp_function", "cppFunction", "cpuScope"),
                "cpu_file": first_text(raw, "cpu_file", "cpu_path", "cpp_file", "cppFile", "cpuFile"),
                "cpu_function": first_text(raw, "cpu_function", "cpp_function", "cppFunction", "cpuScope"),
                "shader": shader_identity["shader"],
                "shader_path": shader_identity.get("shader_path"),
                "entry": entry,
                "stage": infer_stage(first_text(raw, "stage") or first_text(raw, "type", "passType", "pass_type"), entry, None),
                "thread_group": thread_group,
                "type": first_text(raw, "type", "passType", "pass_type"),
                "dispatch_kind": first_text(raw, "dispatch_kind", "dispatchKind"),
                }
            )
        )

    normalized.sort(key=lambda item: item["pass_name"])
    return {
        "schema_version": SCHEMA_VERSION,
        "generated_at_utc": generated_at,
        "mappings": normalized,
    }


def normalize_events(data: dict[str, Any], generated_at: str, frame: int | None) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    raw_passes = data.get("gpu", {}).get("passes")
    if raw_passes is None:
        raw_passes = data.get("events") or data.get("passes") or []

    normalized = []
    for raw in raw_passes if isinstance(raw_passes, list) else []:
        if not isinstance(raw, dict):
            continue
        pass_name = resolve_semantic_pass_name(raw, first_text(raw, "pass_name", "name", "passName"))
        if not pass_name:
            continue
        pass_id = first_text(raw, "pass_id", "passId") or extract_marker_pass_id(pass_name)
        event_type = normalize_event_type(raw)
        pass_type = normalize_pass_type(raw, {}, event_type)
        shader_identity = split_shader_identity(first_text(raw, "shader", "shader_name", "shaderName"))
        stage = infer_stage(first_text(raw, "stage"), first_text(raw, "entry", "shader_entry", "shaderEntry"), event_type)
        event = {
            "event_id": safe_optional_int(raw.get("event_id") if raw.get("event_id") is not None else raw.get("eventId")),
            "action_id": safe_optional_int(raw.get("action_id") if raw.get("action_id") is not None else raw.get("actionId")),
            "pass_id": pass_id,
            "pass_name": pass_name,
            "debug_label": first_text(raw, "debug_label", "debugLabel")
            or (raw.get("marker_path", [pass_name])[-1] if isinstance(raw.get("marker_path"), list) and raw.get("marker_path") else pass_name),
            "event_name": first_text(raw, "event_name", "eventName"),
            "queue": first_text(raw, "queue", "queue_name", "queueName"),
            "command_list": first_text(raw, "command_list", "commandList"),
            "event_type": event_type,
            "pass_type": pass_type,
            "dispatch": normalize_dispatch(raw.get("dispatch")),
            "draw": raw.get("draw"),
            "gpu_time_ms": first_number(raw, "gpu_time_ms", "gpu_ms", "gpuMs"),
            "shader": shader_identity["shader"],
            "shader_path": shader_identity.get("shader_path"),
            "entry": first_text(raw, "entry", "shader_entry", "shaderEntry"),
            "stage": stage,
            "pipeline_hash": first_text(raw, "pipeline_hash", "pipelineHash", "pipeline_state_hash", "pipelineStateHash"),
            "shader_hash": first_text(raw, "shader_hash", "shaderHash"),
            "permutation": raw.get("permutation") if isinstance(raw.get("permutation"), dict) else None,
            "marker_path": raw.get("marker_path") or raw.get("markerPath") or [],
            "indirect": raw.get("indirect"),
            "pipeline": raw.get("pipeline") if isinstance(raw.get("pipeline"), dict) else None,
            "draw_resources": raw.get("draw_resources") if isinstance(raw.get("draw_resources"), dict) else None,
            "resources": raw.get("resources") if isinstance(raw.get("resources"), dict) else {},
        }
        normalized.append(drop_empty(event))

    return {
        "schema_version": SCHEMA_VERSION,
        "frame": frame if frame is not None else data.get("frame"),
        "generated_at_utc": generated_at,
        "events": normalized,
    }, raw_passes if isinstance(raw_passes, list) else []


def enrich_events_with_catalog(
    events: dict[str, Any],
    catalog: dict[str, Any],
) -> dict[str, Any]:
    """Attach stable catalog ids to raw RenderDoc events without inventing semantics."""
    catalog_lookup = build_catalog_lookup(catalog)

    for event in events.get("events", []):
        if not isinstance(event, dict):
            continue
        catalog_entry = find_catalog_entry_for_event(event, catalog_lookup)
        pass_id = stable_pass_id_for_event(event, catalog_entry)
        if pass_id:
            event["pass_id"] = pass_id
            event["matched_pass_id"] = pass_id
        name = first_text(event, "pass_name", "debug_label", "event_name")
        if name:
            event["name"] = name
        event_type = normalize_overview_event_type(event)
        pass_type = normalize_pass_type(event, catalog_entry, event_type)
        if event_type:
            event["type"] = event_type
            event["event_type"] = event_type
        event["pass_type"] = pass_type
        if event.get("gpu_time_ms") is not None:
            event["duration_ms"] = event.get("gpu_time_ms")
        if catalog_entry.get("display_name"):
            event["display_name"] = catalog_entry.get("display_name")

    return events


def normalize_runtime_bindings(data: dict[str, Any]) -> tuple[dict[int, dict[str, Any]], dict[str, list[dict[str, Any]]], dict[str, dict[str, Any]]]:
    by_event: dict[int, dict[str, Any]] = {}
    by_pass: dict[str, list[dict[str, Any]]] = {}
    resources = normalize_runtime_resources(data)

    for raw_event_bindings in data.get("bindings_by_event", []) if isinstance(data.get("bindings_by_event"), list) else []:
        if not isinstance(raw_event_bindings, dict):
            continue
        event_id = safe_optional_int(raw_event_bindings.get("event_id") or raw_event_bindings.get("eventId"))
        pass_name = first_text(raw_event_bindings, "pass_name", "passName", "name")
        bindings = raw_event_bindings.get("bindings", [])
        if event_id is None or not isinstance(bindings, list):
            continue
        by_event[event_id] = {
            "event_id": event_id,
            "pass_name": pass_name,
            "bindings": [normalize_existing_binding_record(binding) for binding in bindings if isinstance(binding, dict)],
        }

    raw_passes = data.get("passes", data if isinstance(data, dict) else {})
    if isinstance(raw_passes, list):
        for raw_pass in raw_passes:
            if not isinstance(raw_pass, dict):
                continue
            pass_name = first_text(raw_pass, "pass_name", "name", "passName")
            event_id = safe_optional_int(raw_pass.get("event_id") or raw_pass.get("eventId"))
            bindings = raw_pass.get("bindings", [])
            if not isinstance(bindings, list):
                continue
            normalized_bindings = [normalize_existing_binding_record(binding) for binding in bindings if isinstance(binding, dict)]
            if event_id is not None:
                by_event[event_id] = {
                    "event_id": event_id,
                    "pass_name": pass_name,
                    "bindings": normalized_bindings,
                }
            elif pass_name:
                by_pass.setdefault(pass_name, []).extend(normalized_bindings)
        return by_event, by_pass, resources

    if not isinstance(raw_passes, dict):
        return by_event, by_pass, resources

    for pass_name, raw_pass in raw_passes.items():
        if not isinstance(raw_pass, dict):
            continue
        bindings_block = raw_pass.get("bindings", {})
        if isinstance(bindings_block, list):
            by_pass.setdefault(str(pass_name), []).extend(bindings_block)
            continue
        if not isinstance(bindings_block, dict):
            continue
        for access in ("srv", "uav", "cbv"):
            for raw_binding in bindings_block.get(access, []):
                if not isinstance(raw_binding, dict):
                    continue
                binding = dict(raw_binding)
                binding["access"] = access
                by_pass.setdefault(str(pass_name), []).append(binding)
    return by_event, by_pass, resources


def normalize_runtime_resources(data: dict[str, Any]) -> dict[str, dict[str, Any]]:
    raw_resources = data.get("resources", {})
    if isinstance(raw_resources, dict):
        iterator = raw_resources.items()
    elif isinstance(raw_resources, list):
        iterator = [(None, item) for item in raw_resources]
    else:
        iterator = []

    resources: dict[str, dict[str, Any]] = {}
    for key, raw_resource in iterator:
        if not isinstance(raw_resource, dict):
            continue
        runtime_name = first_text(raw_resource, "runtime_name", "runtimeName")
        logical_name = first_text(raw_resource, "logical_name", "logicalName", "name") or runtime_name or str(key or "")
        if not logical_name:
            continue
        stride = first_number(raw_resource, "stride", "stride_bytes", "strideBytes")
        element_count = first_number(raw_resource, "element_count", "elementCount", "count")
        byte_size = first_number(raw_resource, "byte_size", "byteSize", "size_bytes", "sizeBytes")
        if byte_size is None and stride is not None and element_count is not None:
            byte_size = int(stride) * int(element_count)
        access_pattern = raw_resource.get("access_pattern") if isinstance(raw_resource.get("access_pattern"), dict) else {}
        resource = {
            "logical_name": logical_name,
            "name": first_text(raw_resource, "name") or logical_name,
            "runtime_name": runtime_name or logical_name,
            "resource_type": first_text(raw_resource, "resource_type", "resourceType", "type"),
            "byte_size": byte_size,
            "size_bytes": byte_size,
            "stride": stride,
            "stride_bytes": stride,
            "element_count": element_count,
            "usage": normalize_string_list(raw_resource.get("usage")),
            "producer": first_text(raw_resource, "producer"),
            "producer_passes": normalize_string_list(raw_resource.get("producer_passes") or raw_resource.get("producerPasses")),
            "consumer_passes": normalize_string_list(raw_resource.get("consumer_passes") or raw_resource.get("consumerPasses")),
            "access_pattern": drop_empty(
                {
                    "read": first_text(access_pattern, "read"),
                    "write": first_text(access_pattern, "write"),
                    "random_access": normalize_string_list(access_pattern.get("random_access") or access_pattern.get("randomAccess")),
                }
            ),
        }
        resources[logical_name] = drop_empty(resource, keep_null_keys={"byte_size", "size_bytes"})
    return resources


def build_bindings(
    runtime_bindings_by_event: dict[int, dict[str, Any]],
    runtime_bindings_by_pass: dict[str, list[dict[str, Any]]],
    runtime_resources: dict[str, dict[str, Any]],
    shader_runtime_details_by_event: dict[int, dict[str, Any]],
    events: dict[str, Any],
    raw_events: list[dict[str, Any]],
    generated_at: str,
    frame: int | None,
) -> dict[str, Any]:
    raw_events_by_id: dict[int, dict[str, Any]] = {}
    raw_events_by_pass: dict[str, list[dict[str, Any]]] = {}

    for raw_event in raw_events:
        if not isinstance(raw_event, dict):
            continue
        pass_name = first_text(raw_event, "pass_name", "name", "passName")
        event_id = safe_optional_int(raw_event.get("event_id") if raw_event.get("event_id") is not None else raw_event.get("eventId"))
        if event_id is not None:
            raw_events_by_id[event_id] = raw_event
        if not pass_name:
            continue
        raw_events_by_pass.setdefault(pass_name, []).append(raw_event)

    bindings_by_event = []
    for event in events.get("events", []):
        event_id = safe_optional_int(event.get("event_id"))
        pass_name = first_text(event, "pass_name")
        if event_id is None and not pass_name:
            continue

        event_bindings = []
        matched_runtime_ids: set[int] = set()
        runtime_event_record = runtime_bindings_by_event.get(event_id) if event_id is not None else None
        runtime_pass_bindings = runtime_bindings_by_pass.get(pass_name, [])
        raw_event = raw_events_by_id.get(event_id) if event_id is not None else None
        if raw_event is None and pass_name:
            raw_event = first_unconsumed_raw_event(raw_events_by_pass.get(pass_name, []), event_id)

        has_binding_fact = bool(runtime_event_record) or bool(runtime_pass_bindings)
        resources = raw_event.get("resources") if isinstance(raw_event, dict) else {}
        if isinstance(raw_event, dict) and "resources" in raw_event and isinstance(resources, dict):
            has_binding_fact = True
        if isinstance(resources, dict):
            for access in ("srv", "uav", "cbv"):
                for raw_resource in resources.get(access, []):
                    if not isinstance(raw_resource, dict):
                        continue
                    runtime_match, runtime_index = find_runtime_binding(runtime_pass_bindings, access, raw_resource)
                    if runtime_index is not None:
                        matched_runtime_ids.add(runtime_index)
                    cbuffer_detail = find_cbuffer_detail(shader_runtime_details_by_event.get(event_id), raw_resource) if access == "cbv" else None
                    event_bindings.append(build_binding_record(access, raw_resource, runtime_match, cbuffer_detail))

        if runtime_event_record:
            for binding in runtime_event_record.get("bindings", []):
                if isinstance(binding, dict):
                    event_bindings.append(enrich_binding_with_cbuffer_detail(binding, shader_runtime_details_by_event.get(event_id)))

        for index, binding in enumerate(runtime_pass_bindings):
            if index in matched_runtime_ids:
                continue
            event_bindings.append(
                build_binding_record(
                    binding.get("access", "srv"),
                    None,
                    binding,
                    find_cbuffer_detail(shader_runtime_details_by_event.get(event_id), binding) if normalize_access(binding.get("access")) == "cbv" else None,
                )
            )

        if has_binding_fact:
            bindings_by_event.append(
                {
                    "event_id": event_id,
                    "pass_name": pass_name,
                    "bindings": sorted(unique_bindings(event_bindings), key=binding_sort_key),
                }
            )

    return {
        "schema_version": SCHEMA_VERSION,
        "frame": frame,
        "generated_at_utc": generated_at,
        "bindings_by_event": bindings_by_event,
        "resources": sorted(runtime_resources.values(), key=lambda item: str(item.get("logical_name") or item.get("name") or "")),
    }


def build_resource_views(
    events: dict[str, Any],
    catalog: dict[str, Any],
    generated_at: str,
    frame: int | None,
    struct_sizes: dict[str, int] | None = None,
) -> tuple[dict[str, Any], dict[int, dict[str, Any]]]:
    catalog_lookup = build_catalog_lookup(catalog)
    access_map = {"srv": "SRV", "uav": "UAV", "cbv": "CBV"}

    # Collect all bindings and unique resources across all events
    all_bindings: list[dict[str, Any]] = []
    unique_resources: dict[str, dict[str, Any]] = {}
    resource_to_catalog_name: dict[str, str] = {}
    resource_to_catalog_type: dict[str, str] = {}
    resource_to_pass_invocations: dict[str, set[int]] = defaultdict(set)

    for event in events.get("events", []):
        if not isinstance(event, dict):
            continue
        event_id = safe_optional_int(event.get("event_id"))
        catalog_entry = find_catalog_entry_for_event(event, catalog_lookup)
        pass_id = stable_pass_id_for_event(event, catalog_entry)
        raw_resources = (event.get("resources") if isinstance(event.get("resources"), dict) else {})

        # Compute total invocations for data-parallel stride estimation
        dispatch_cfg = catalog_entry.get("dispatch") if isinstance(catalog_entry.get("dispatch"), dict) else {}
        numthreads = normalize_thread_group(dispatch_cfg.get("thread_group_size"))
        groups = normalize_dispatch(event.get("dispatch"))
        total_invocations = list_product(groups) * list_product(numthreads) if list_product(numthreads) else 0

        # Per-event binding index counters (reset for each event)
        access_index: dict[str, int] = {"srv": 0, "uav": 0, "cbv": 0}

        for access_key, access_label in access_map.items():
            for binding in raw_resources.get(access_key, []) if isinstance(raw_resources.get(access_key), list) else []:
                if not isinstance(binding, dict):
                    continue
                resource_id = first_text(binding, "resource_id")
                if not resource_id:
                    continue

                shader_name = first_text(binding, "name", "resource_name")
                renderdoc_type = first_text(binding, "resource_type")
                byte_size = derive_byte_size(binding, renderdoc_type)
                stride = first_number(binding, "stride_bytes", "stride", "byte_stride")

                # Track unique resource properties
                if resource_id not in unique_resources:
                    unique_resources[resource_id] = {
                        "resource_id": resource_id,
                        "type": renderdoc_type,
                        "byte_size": byte_size,
                        "stride": stride,
                        "shader_names": set(),
                    }
                res_entry = unique_resources[resource_id]
                res_entry["shader_names"].add(shader_name)
                if byte_size is not None:
                    res_entry["byte_size"] = max(int(res_entry.get("byte_size") or 0), int(byte_size))
                if stride is not None and res_entry.get("stride") is None:
                    res_entry["stride"] = stride
                if total_invocations > 0:
                    resource_to_pass_invocations[resource_id].add(total_invocations)

                # Try to map catalog name for this resource in this pass
                idx = access_index.get(access_key, 0)
                cat_name, cat_type = match_catalog_resource_for_binding(catalog_entry, access_label, idx)
                access_index[access_key] = idx + 1
                if cat_name:
                    resource_to_catalog_name[resource_id] = cat_name
                    resource_to_catalog_type[resource_id] = cat_type

                all_bindings.append(drop_empty({
                    "event_id": event_id,
                    "pass_id": pass_id,
                    "resource_id": resource_id,
                    "name": cat_name or shader_name,
                    "shader_name": shader_name,
                    "slot": binding_slot_str(access_key, binding.get("slot")),
                    "bind_type": access_label,
                    "access": access_type_to_pattern(access_label, shader_name, catalog_entry),
                }))

    # Build merged resource properties (deduplicated by resource_id)
    resource_props: dict[str, dict[str, Any]] = {}
    for resource_id, entry in unique_resources.items():
        cat_name = resource_to_catalog_name.get(resource_id)
        cat_type = resource_to_catalog_type.get(resource_id)
        stride = entry.get("stride") or derive_stride_from_hlsl_type(cat_type or "", struct_sizes)
        byte_size = entry.get("byte_size")
        # Fallback: estimate stride from total invocations in data-parallel passes
        if stride is None and byte_size is not None:
            for invocations in sorted(resource_to_pass_invocations.get(resource_id, set())):
                if invocations > 0 and int(byte_size) % invocations == 0:
                    stride = int(byte_size) // invocations
                    break
        estimated_element_count = None
        if byte_size is not None and stride and stride > 0:
            estimated_element_count = int(byte_size) // int(stride)
        resource_props[resource_id] = drop_empty({
            "resource_id": resource_id,
            "name": cat_name or sorted(entry.pop("shader_names", set()))[0],
            "type": entry["type"],
            "hlsl_type": resource_to_catalog_type.get(resource_id) or None,
            "byte_size": byte_size,
            "stride": stride,
            "estimated_element_count": estimated_element_count,
        })

    # event_resource_bindings — per-event list with resource properties merged inline
    bindings_by_event: dict[int, list[dict[str, Any]]] = {}
    event_pass_id: dict[int, str] = {}
    for b in all_bindings:
        eid = safe_optional_int(b.get("event_id"))
        if eid is not None:
            event_pass_id[eid] = b.get("pass_id") or event_pass_id.get(eid, "")
            props = resource_props.get(b["resource_id"], {})
            bindings_by_event.setdefault(eid, []).append(drop_empty({
                "resource_id": b["resource_id"],
                "name": props.get("name") or b["name"],
                "type": props.get("type"),
                "hlsl_type": props.get("hlsl_type"),
                "byte_size": props.get("byte_size"),
                "stride": props.get("stride"),
                "estimated_element_count": props.get("estimated_element_count"),
                "slot": b["slot"],
                "bind_type": b["bind_type"],
                "access": b["access"],
            }))

    bindings_list = [
        drop_empty({"event_id": eid, "pass_id": event_pass_id.get(eid), "bindings": sorted(bindings, key=lambda x: str(x.get("slot") or ""))})
        for eid, bindings in sorted(bindings_by_event.items())
    ]

    # C) resource_usages — producer/consumer per resource
    resource_usages = []
    for resource_id, entry in sorted(unique_resources.items()):
        cat_name = resource_to_catalog_name.get(resource_id)
        resource_bindings = [b for b in all_bindings if b["resource_id"] == resource_id]
        producers = sorted(set(
            b.get("pass_id")
            for b in resource_bindings
            if b["access"] in ("write", "read_write") and b.get("pass_id")
        ))
        consumers = sorted(set(
            b.get("pass_id")
            for b in resource_bindings
            if b["access"] == "read" and b.get("pass_id")
        ))
        resource_usages.append(drop_empty({
            "resource_id": resource_id,
            "name": cat_name or None,
            "type": entry.get("type"),
            "hlsl_type": resource_to_catalog_type.get(resource_id) or None,
            "producer_passes": producers or None,
            "consumer_passes": consumers or None,
        }))

    resource_usages.extend(build_catalog_resource_usages(events, catalog_lookup))
    resource_usages = unique_records(resource_usages, ("resource_id", "name", "source"))

    # Populate per-event lookup for build_frame_context
    by_event: dict[int, dict[str, Any]] = {}
    for eid, bindings in bindings_by_event.items():
        by_event[eid] = {
            "resources": bindings,
        }

    public = {
        "schema_version": SCHEMA_VERSION,
        "frame": frame,
        "generated_at_utc": generated_at,
        "event_resource_bindings": bindings_list,
        "resource_usages": resource_usages,
    }
    return public, by_event


def build_catalog_resource_usages(events: dict[str, Any], catalog_lookup: dict[str, dict[str, Any]]) -> list[dict[str, Any]]:
    present_passes: dict[str, dict[str, Any]] = {}
    for event in events.get("events", []) if isinstance(events.get("events"), list) else []:
        if not isinstance(event, dict):
            continue
        catalog_entry = find_catalog_entry_for_event(event, catalog_lookup)
        pass_id = stable_pass_id_for_event(event, catalog_entry)
        if pass_id and catalog_entry:
            present_passes[pass_id] = catalog_entry

    by_resource: dict[str, dict[str, Any]] = {}
    for pass_id, catalog_entry in present_passes.items():
        for section in ("inputs", "outputs"):
            for resource in catalog_entry.get(section, []) if isinstance(catalog_entry.get(section), list) else []:
                if not isinstance(resource, dict):
                    continue
                name = first_text(resource, "name")
                if not name:
                    continue
                key = normalize_resource_symbol(name)
                entry = by_resource.setdefault(
                    key,
                    {
                        "name": name,
                        "hlsl_type": first_text(resource, "type"),
                        "producer_passes": set(),
                        "consumer_passes": set(),
                    },
                )
                if not entry.get("hlsl_type"):
                    entry["hlsl_type"] = first_text(resource, "type")
                access = first_text(resource, "access").lower()
                if section == "outputs" and access in ("write", "read_write", "append", "unknown", ""):
                    entry["producer_passes"].add(pass_id)
                if section == "inputs" and access in ("read", "read_write", "consume", "unknown", ""):
                    entry["consumer_passes"].add(pass_id)
                if section == "outputs" and access == "read_write":
                    entry["consumer_passes"].add(pass_id)

    usages = []
    for _, entry in sorted(by_resource.items()):
        producers = sorted(entry["producer_passes"])
        consumers = sorted(entry["consumer_passes"])
        if not producers and not consumers:
            continue
        usages.append(
            drop_empty(
                {
                    "name": entry.get("name"),
                    "hlsl_type": entry.get("hlsl_type"),
                    "source": "catalog",
                    "producer_passes": producers or None,
                    "consumer_passes": consumers or None,
                }
            )
        )
    return usages


def derive_byte_size(binding: dict[str, Any], resource_type: str) -> int | None:
    if resource_type == "buffer":
        return first_number(binding, "length")
    elif resource_type == "texture":
        w = first_number(binding, "width") or 0
        h = first_number(binding, "height") or 0
        d = first_number(binding, "depth") or 1
        return int(w) * int(h) * int(d) * 4  # RGBA8 default
    return None


def derive_element_estimate(entry: dict[str, Any]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    byte_size = entry.get("byte_size")
    stride = entry.get("stride")
    if byte_size is not None and stride is not None and int(stride) > 0:
        result["estimated_element_count"] = int(byte_size) // int(stride)
    return result


def binding_slot_str(access_key: str, slot: Any) -> str:
    prefix = {"srv": "t", "uav": "u", "cbv": "b"}.get(access_key, "")
    slot_int = safe_int(slot)
    return f"{prefix}{slot_int}"


def access_type_to_pattern(access_label: str, shader_name: str, catalog_entry: dict[str, Any]) -> str:
    if access_label == "UAV":
        cat_outputs = {first_text(r, "name"): r for r in (catalog_entry.get("outputs") or []) if isinstance(r, dict)}
        cat_decl = cat_outputs.get(shader_name, {})
        return "read_write" if first_text(cat_decl, "access") == "read_write" else "write"
    return "read"


def match_catalog_resource_for_binding(catalog_entry: dict[str, Any], access_label: str, binding_index: int) -> tuple[str, str]:
    """Returns (catalog_name, catalog_type_str) if matched, else ("", "")."""
    if not catalog_entry:
        return "", ""
    if access_label == "UAV":
        outputs = catalog_entry.get("outputs") or []
        if isinstance(outputs, list) and binding_index < len(outputs):
            entry = outputs[binding_index]
            if isinstance(entry, dict):
                return first_text(entry, "name"), first_text(entry, "type")
    elif access_label == "SRV":
        inputs = catalog_entry.get("inputs") or []
        if isinstance(inputs, list) and binding_index < len(inputs):
            entry = inputs[binding_index]
            if isinstance(entry, dict):
                return first_text(entry, "name"), first_text(entry, "type")
    return "", ""


def derive_stride_from_hlsl_type(type_str: str, struct_sizes: dict[str, int] | None = None) -> int | None:
    """Derive byte stride from HLSL type declarations like 'StructuredBuffer<uint>', 'RWStructuredBuffer<float4>', etc.

    When struct_sizes is provided, also resolves complex types like 'StructuredBuffer<InstanceSimulationState>'.
    """
    if not type_str:
        return None
    import re as _re
    m = _re.search(r'<([^>]+)>', type_str)
    if not m:
        return None
    inner = m.group(1).strip()
    # Try simple scalar/vector first
    scalar_sizes = {"float": 4, "uint": 4, "int": 4, "half": 2, "bool": 4}
    vec_m = _re.match(r'^(float|uint|int|half|bool)(\d?)$', inner)
    if vec_m:
        base = scalar_sizes.get(vec_m.group(1))
        if base is None:
            return None
        vec = vec_m.group(2)
        components = int(vec) if vec else 1
        return base * components
    # Look up struct name
    if struct_sizes and inner in struct_sizes:
        return struct_sizes[inner]
    return None


HLSL_STRUCT_RE = re.compile(
    r'struct\s+(?P<name>[_A-Za-z]\w*)\s*\{(?P<body>[^}]*(?:\{[^}]*\}[^}]*)*)\}\s*;',
    re.MULTILINE | re.DOTALL,
)

HLSL_MEMBER_RE = re.compile(
    r'(?P<type>[_A-Za-z]\w*(?:\d+)?(?:\s*<[^>]+>)?)\s+(?P<name>[_A-Za-z]\w*)\s*(?:\[[^\]]+\])?\s*;',
)


def _hlsl_type_size_align(type_str: str, struct_sizes: dict[str, int] | None = None) -> tuple[int, int]:
    """Returns (size, alignment) for an HLSL type name."""
    type_str = type_str.strip()
    # Matrix types: float4x4, float3x3, uint4x4, etc.
    mat_m = re.match(r'^(float|uint|int|half)(\d)x(\d)$', type_str)
    if mat_m:
        base_size = {"float": 4, "uint": 4, "int": 4, "half": 2}.get(mat_m.group(1), 4)
        rows = int(mat_m.group(3))
        cols = int(mat_m.group(2))
        vec_size = base_size * rows
        vec_align = 16 if rows >= 3 else (8 if rows == 2 else 4)
        return vec_size * cols, vec_align
    # Vector/scalar types
    vec_m = re.match(r'^(float|uint|int|half|bool)(\d?)$', type_str)
    if vec_m:
        base_size = {"float": 4, "uint": 4, "int": 4, "half": 2, "bool": 4}.get(vec_m.group(1), 4)
        components = int(vec_m.group(2)) if vec_m.group(2) else 1
        size = base_size * components
        align = 16 if components >= 3 else (8 if components == 2 else 4)
        return size, align
    # Look up as nested struct
    if struct_sizes and type_str in struct_sizes:
        sz = struct_sizes[type_str]
        # Alignment of a struct is the max alignment of its members, bounded by 16
        return sz, _estimate_struct_alignment(sz)
    # Unknown type: assume 4 bytes
    return 4, 4


def _estimate_struct_alignment(size: int) -> int:
    """Heuristic: largest power-of-2 divisor ≤ 16 that divides size."""
    for align in (16, 8, 4):
        if size % align == 0:
            return align
    return 4


def parse_hlsl_struct_sizes(hlsl_paths: list[str], repo_root: str = ".") -> dict[str, int]:
    """Parse HLSL struct definitions and compute sizeof for each struct.

    Follows HLSL structured-buffer layout rules: members are naturally aligned
    (float3 = 16-byte aligned, float4 = 16, float2 = 8, scalars = 4), and the
    struct is padded to a multiple of the largest member alignment.
    """
    if not hlsl_paths:
        return {}

    # Collect all struct body texts
    struct_bodies: dict[str, str] = {}  # name -> body text
    for path in hlsl_paths:
        resolved = str(Path(path)) if Path(path).exists() else str(Path(repo_root) / path)
        try:
            with open(resolved, 'r', encoding='utf-8', errors='replace') as f:
                source = f.read()
        except (OSError, IOError):
            continue
        for m in HLSL_STRUCT_RE.finditer(source):
            name = m.group("name")
            body = m.group("body")
            if name not in struct_bodies:
                struct_bodies[name] = body

    # Compute sizes — need topological ordering for nested structs
    sizes: dict[str, int] = {}
    # Iteratively resolve until all known or no progress
    changed = True
    while changed:
        changed = False
        for name, body in list(struct_bodies.items()):
            if name in sizes:
                continue
            offset = 0
            max_align = 4
            unresolved = False
            for member_m in HLSL_MEMBER_RE.finditer(body):
                type_str = member_m.group("type")
                size, align = _hlsl_type_size_align(type_str, sizes)
                if size is None:
                    unresolved = True
                    break
                # Align offset
                if offset % align != 0:
                    offset += align - (offset % align)
                offset += size
                max_align = max(max_align, align)
            if unresolved:
                continue
            # Pad struct to alignment boundary
            if offset % max_align != 0:
                offset += max_align - (offset % max_align)
            sizes[name] = max(offset, 1)
            changed = True

    return sizes


def parse_hlsl_struct_sizes_for_catalog(catalog: dict[str, Any], repo_root: str = ".") -> dict[str, int]:
    """Discover HLSL include files from catalog shader paths and parse struct sizes."""
    shader_dirs: set[str] = set()
    for p in catalog.get("passes", []) if isinstance(catalog.get("passes"), list) else []:
        shader_path = first_text(p, "shader")
        if shader_path:
            shader_dirs.add(str(Path(repo_root) / Path(shader_path).parent))

    # Also add the known crowds shader directory as fallback
    default_dir = str(Path(repo_root) / "Assets/Project/Crowds/VAT/Shader")
    if Path(default_dir).is_dir():
        shader_dirs.add(default_dir)

    hlsl_files: list[str] = []
    for d in shader_dirs:
        try:
            for f in Path(d).glob("*.hlsl"):
                hlsl_files.append(str(f))
        except (OSError, IOError):
            continue

    return parse_hlsl_struct_sizes(hlsl_files, repo_root)


def build_merged_resources_for_event(
    observed_bindings: list[dict[str, Any]],
    shader_fact: dict[str, Any] | None,
    shader_runtime_detail: dict[str, Any] | None,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    observed_bindings = [binding for binding in observed_bindings if isinstance(binding, dict)]
    renderdoc_bindings = [
        binding
        for binding in observed_bindings
        if "renderdoc" in first_text(binding, "source").lower() and "runtime_sidecar" not in first_text(binding, "source").lower()
    ]
    runtime_bindings = [binding for binding in observed_bindings if "runtime_sidecar" in first_text(binding, "source").lower()]
    slot_accesses = shader_runtime_detail.get("slot_accesses", {}) if isinstance(shader_runtime_detail, dict) else {}
    insights = build_shader_resource_insights(shader_fact)

    matched_renderdoc_indices: set[int] = set()
    merged_resources = []
    for runtime_binding in runtime_bindings:
        insight = resolve_shader_resource_insight(runtime_binding, insights)
        if "renderdoc" in first_text(runtime_binding, "source").lower():
            renderdoc_binding = runtime_binding
        else:
            renderdoc_index = find_best_renderdoc_binding(runtime_binding, renderdoc_bindings, matched_renderdoc_indices, insight, slot_accesses)
            renderdoc_binding = renderdoc_bindings[renderdoc_index] if renderdoc_index is not None else None
            if renderdoc_index is not None:
                matched_renderdoc_indices.add(renderdoc_index)
        merged_resources.append(build_public_resource_binding(runtime_binding, renderdoc_binding, insight, slot_accesses))

    for index, renderdoc_binding in enumerate(renderdoc_bindings):
        if index in matched_renderdoc_indices:
            continue
        insight = resolve_shader_resource_insight(renderdoc_binding, insights)
        merged_resources.append(build_public_resource_binding(None, renderdoc_binding, insight, slot_accesses))

    merged_resources = collapse_shadow_resource_aliases(merged_resources)
    merged_resources = unique_records(
        merged_resources,
        ("slot", "logical_name", "shader_symbol", "renderdoc_name", "type", "size_bytes", "stride_bytes", "element_count"),
    )
    merged_resources.sort(key=public_resource_sort_key)
    unresolved_bindings = build_unresolved_bindings(merged_resources)
    return merged_resources, unresolved_bindings


def collapse_shadow_resource_aliases(resources: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[tuple[str, str, str, str], list[dict[str, Any]]] = {}
    passthrough = []
    for resource in resources:
        renderdoc_name = first_text(resource, "renderdoc_name")
        slot = first_text(resource, "slot")
        resource_type = first_text(resource, "type")
        size_text = str(first_number(resource, "size_bytes", "byte_size") or "")
        if not renderdoc_name or not slot:
            passthrough.append(resource)
            continue
        grouped.setdefault((slot, renderdoc_name, resource_type, size_text), []).append(resource)

    collapsed = list(passthrough)
    for group in grouped.values():
        if len(group) == 1:
            collapsed.extend(group)
            continue
        primary = max(group, key=resource_binding_richness)
        merged = dict(primary)
        merged["actual_access"] = normalize_actual_access_list(
            [
                access
                for item in group
                for access in (item.get("actual_access", []) if isinstance(item.get("actual_access"), list) else [])
            ]
        )
        if first_number(merged, "stride_bytes") is None:
            for item in group:
                stride = first_number(item, "stride_bytes", "stride")
                if stride is not None:
                    merged["stride_bytes"] = stride
                    break
        if first_number(merged, "element_count") is None:
            for item in group:
                element_count = first_number(item, "element_count")
                if element_count is not None:
                    merged["element_count"] = element_count
                    break
        collapsed.append(drop_empty(merged, keep_null_keys={"slot", "size_bytes"}))
    return collapsed


def resource_binding_richness(resource: dict[str, Any]) -> int:
    score = 0
    logical_name = first_text(resource, "logical_name")
    renderdoc_name = first_text(resource, "renderdoc_name")
    shader_symbol = first_text(resource, "shader_symbol")
    if shader_symbol:
        score += 100
    if logical_name and logical_name != renderdoc_name:
        score += 40
    if first_number(resource, "stride_bytes", "stride") is not None:
        score += 20
    if first_number(resource, "element_count") is not None:
        score += 20
    if first_text(resource, "access_pattern"):
        score += 10
    return score


def build_shader_resource_insights(shader_fact: dict[str, Any] | None) -> dict[str, dict[str, Any]]:
    shader_fact = shader_fact or {}
    insights: dict[str, dict[str, Any]] = {}
    for declaration in shader_fact.get("resources_declared", []) if isinstance(shader_fact.get("resources_declared"), list) else []:
        if not isinstance(declaration, dict):
            continue
        symbol = first_text(declaration, "name")
        if not symbol:
            continue
        key = normalize_resource_symbol(symbol)
        insights[key] = {
            "shader_symbol": symbol,
            "bind_type": first_text(declaration, "bind_type"),
            "resource_type": first_text(declaration, "type"),
            "slot": normalize_slot_text(declaration.get("slot"), normalize_access(first_text(declaration, "bind_type"))),
            "actual_access": [],
        }

    for access_record in shader_fact.get("resource_accesses", []) if isinstance(shader_fact.get("resource_accesses"), list) else []:
        if not isinstance(access_record, dict):
            continue
        resource = first_text(access_record, "resource")
        if not resource:
            continue
        key = normalize_resource_symbol(resource)
        insight = insights.setdefault(
            key,
            {
                "shader_symbol": resource,
                "bind_type": "",
                "resource_type": "",
                "slot": None,
                "actual_access": [],
            },
        )
        access_name = first_text(access_record, "access")
        if access_name and access_name not in insight["actual_access"]:
            insight["actual_access"].append(access_name)

    for atomic_record in shader_fact.get("atomics", []) if isinstance(shader_fact.get("atomics"), list) else []:
        if not isinstance(atomic_record, dict):
            continue
        target_symbol = extract_atomic_target_symbol(first_text(atomic_record, "arguments"))
        if not target_symbol:
            continue
        key = normalize_resource_symbol(target_symbol)
        insight = insights.setdefault(
            key,
            {
                "shader_symbol": target_symbol,
                "bind_type": "",
                "resource_type": "",
                "slot": None,
                "actual_access": [],
            },
        )
        atomic_name = normalize_atomic_function_name(first_text(atomic_record, "function"))
        if atomic_name not in insight["actual_access"]:
            insight["actual_access"].append(atomic_name)

    for insight in insights.values():
        insight["actual_access"] = normalize_actual_access_list(insight.get("actual_access", []))
    return insights


def resolve_shader_resource_insight(binding: dict[str, Any] | None, insights: dict[str, dict[str, Any]]) -> dict[str, Any] | None:
    if not isinstance(binding, dict):
        return None
    for key_name in ("shader_symbol", "logical_name", "runtime_name", "name", "renderdoc_name"):
        key = normalize_resource_symbol(first_text(binding, key_name))
        if key and key in insights:
            return insights[key]
    return None


def find_best_renderdoc_binding(
    runtime_binding: dict[str, Any],
    renderdoc_bindings: list[dict[str, Any]],
    matched_indices: set[int],
    insight: dict[str, Any] | None,
    slot_accesses: dict[str, list[str]],
) -> int | None:
    best_index = None
    best_score = 0
    for index, renderdoc_binding in enumerate(renderdoc_bindings):
        if index in matched_indices:
            continue
        score = score_renderdoc_binding(runtime_binding, renderdoc_binding, insight, slot_accesses)
        if score > best_score:
            best_score = score
            best_index = index
    return best_index if best_score > 0 else None


def score_renderdoc_binding(
    runtime_binding: dict[str, Any],
    renderdoc_binding: dict[str, Any],
    insight: dict[str, Any] | None,
    slot_accesses: dict[str, list[str]],
) -> int:
    score = 0
    desired_type = first_text(insight or {}, "bind_type") or first_text(runtime_binding, "type")
    candidate_type = first_text(renderdoc_binding, "type")
    if desired_type and candidate_type == desired_type:
        score += 40
    runtime_size = first_number(runtime_binding, "size_bytes", "byte_size")
    renderdoc_size = first_number(renderdoc_binding, "size_bytes", "byte_size")
    if runtime_size is not None and renderdoc_size is not None and int(runtime_size) == int(renderdoc_size):
        score += 35
    desired_accesses = insight.get("actual_access", []) if isinstance(insight, dict) else []
    candidate_accesses = slot_accesses.get(first_text(renderdoc_binding, "slot").lower(), [])
    if desired_accesses and candidate_accesses:
        overlap = set(desired_accesses).intersection(candidate_accesses)
        if overlap:
            score += 60
        if any(value.startswith("atomic_") for value in overlap):
            score += 40
    if desired_type == "CBV" and first_text(renderdoc_binding, "slot").startswith("b"):
        score += 20
    if desired_type == "SRV" and first_text(renderdoc_binding, "slot").startswith("t"):
        score += 20
    if desired_type == "UAV" and first_text(renderdoc_binding, "slot").startswith("u"):
        score += 20
    return score


def build_public_resource_binding(
    runtime_binding: dict[str, Any] | None,
    renderdoc_binding: dict[str, Any] | None,
    insight: dict[str, Any] | None,
    slot_accesses: dict[str, list[str]],
) -> dict[str, Any]:
    runtime_binding = runtime_binding or {}
    renderdoc_binding = renderdoc_binding or {}
    insight = insight or {}
    logical_name = (
        first_text(runtime_binding, "logical_name", "runtime_name", "name")
        or first_text(renderdoc_binding, "logical_name", "name")
        or first_text(insight, "shader_symbol")
    )
    shader_symbol = first_text(runtime_binding, "shader_symbol", "name") or first_text(insight, "shader_symbol")
    slot = first_text(renderdoc_binding, "slot") or first_text(runtime_binding, "slot") or first_text(insight, "slot")
    resource_type = first_text(runtime_binding, "resource_type") or first_text(insight, "resource_type") or first_text(renderdoc_binding, "resource_type")
    size_bytes = first_number(runtime_binding, "size_bytes", "byte_size")
    if size_bytes is None:
        size_bytes = first_number(renderdoc_binding, "size_bytes", "byte_size")
    stride_bytes = first_number(runtime_binding, "stride_bytes", "stride", "byte_stride")
    element_count = first_number(runtime_binding, "element_count")
    actual_access = derive_public_resource_access(runtime_binding, renderdoc_binding, insight, slot_accesses)
    sources = normalize_public_binding_sources(runtime_binding, renderdoc_binding, insight, slot_accesses)
    return drop_empty(
        {
            "slot": slot or None,
            "space": first_number(renderdoc_binding, "space", "register_space", "registerSpace"),
            "logical_name": logical_name,
            "shader_symbol": shader_symbol,
            "renderdoc_name": first_text(renderdoc_binding, "renderdoc_name", "name"),
            "type": first_text(renderdoc_binding, "type") or first_text(insight, "bind_type") or first_text(runtime_binding, "type"),
            "resource_type": resource_type,
            "size_bytes": size_bytes,
            "stride_bytes": stride_bytes,
            "element_count": element_count,
            "actual_access": actual_access,
            "access_pattern": normalize_public_access_pattern(runtime_binding),
            "sources": sources,
        },
        keep_null_keys={"slot", "size_bytes"},
    )


def normalize_public_binding_sources(
    runtime_binding: dict[str, Any],
    renderdoc_binding: dict[str, Any],
    insight: dict[str, Any],
    slot_accesses: dict[str, list[str]],
) -> list[str]:
    sources = []
    if runtime_binding:
        sources.append("runtime_sidecar")
    if renderdoc_binding:
        sources.append("renderdoc")
    if insight:
        sources.append("shader_reflection")
    elif first_text(renderdoc_binding, "slot").lower() in slot_accesses:
        sources.append("shader_disassembly")
    return sources


def normalize_public_access_pattern(runtime_binding: dict[str, Any]) -> dict[str, Any] | None:
    raw_value = runtime_binding.get("access_pattern")
    if isinstance(raw_value, dict):
        return drop_empty(
            {
                "known": True,
                "read": first_text(raw_value, "read"),
                "write": first_text(raw_value, "write"),
                "random_access": normalize_string_list(raw_value.get("random_access") or raw_value.get("randomAccess")),
            }
        )
    text = first_text(runtime_binding, "access_pattern")
    if not text:
        return None
    normalized = normalize_access_pattern_text(text)
    if normalized is not None:
        return normalized
    return {"known": False, "reason": "unstructured_annotation"}


def normalize_access_pattern_text(text: str) -> dict[str, Any] | None:
    value = normalize_name(text)
    if not value:
        return None
    if "not annotated" in value:
        return {"known": False, "reason": "not_annotated"}
    if "append" in value and "counter" in value:
        return {"known": True, "write": "compact_append_by_atomic_index"}
    if "read by entity id" in value or "read by instance" in value:
        return {"known": True, "read": "linear_by_instance_index"}
    if "write by entity id and bone id" in value:
        return {"known": True, "write": "indexed_by_entity_and_bone"}
    return None


def derive_public_resource_access(
    runtime_binding: dict[str, Any],
    renderdoc_binding: dict[str, Any],
    insight: dict[str, Any],
    slot_accesses: dict[str, list[str]],
) -> list[str]:
    access = []
    access.extend(insight.get("actual_access", []) if isinstance(insight.get("actual_access"), list) else [])
    slot = first_text(renderdoc_binding, "slot").lower()
    access.extend(slot_accesses.get(slot, []))
    if not access:
        bind_type = normalize_access(first_text(renderdoc_binding, "type") or first_text(runtime_binding, "type"))
        if bind_type == "uav":
            access.append("write")
        elif bind_type == "srv":
            access.append("read")
    return normalize_actual_access_list(access)


def normalize_actual_access_list(values: list[str]) -> list[str]:
    access = []
    for value in values:
        text = str(value or "").strip().lower()
        if not text or text in access:
            continue
        access.append(text)
    atomics = [value for value in access if value.startswith("atomic_")]
    if atomics:
        access = [value for value in access if value not in {"write", "atomic"}]
    return access


def extract_atomic_target_symbol(arguments: str) -> str:
    match = re.match(r"\s*([_A-Za-z]\w*)\s*\[", str(arguments or ""))
    return match.group(1) if match else ""


def public_resource_sort_key(resource: dict[str, Any]) -> tuple[str, str, str]:
    return (
        str(resource.get("type") or ""),
        str(resource.get("slot") or ""),
        str(resource.get("logical_name") or ""),
    )


def build_unresolved_bindings(resources: list[dict[str, Any]]) -> list[dict[str, Any]]:
    unresolved = []
    for resource in resources:
        logical_name = first_text(resource, "logical_name")
        renderdoc_name = first_text(resource, "renderdoc_name")
        shader_symbol = first_text(resource, "shader_symbol")
        sources = resource.get("sources", []) if isinstance(resource.get("sources"), list) else []
        if shader_symbol:
            continue
        if logical_name and logical_name != renderdoc_name and not is_generic_binding_name(logical_name):
            continue
        unresolved.append(
            drop_empty(
                {
                    "slot": first_text(resource, "slot"),
                    "renderdoc_name": renderdoc_name or logical_name,
                    "reason": "no_semantic_mapping",
                    "sources": sources,
                }
            )
        )
    return unique_records(unresolved, ("slot", "renderdoc_name", "reason"))


def is_generic_binding_name(name: str) -> bool:
    text = str(name or "")
    return bool(
        re.match(r"^(?:cbuffer\d+|[tub]av\d+|[tub]\d+|buffer-\d+-\d+)$", text, flags=re.IGNORECASE)
    )


def build_constants_view(resources: list[dict[str, Any]], shader_runtime_detail: dict[str, Any] | None) -> dict[str, Any]:
    shader_runtime_detail = shader_runtime_detail or {}
    cbuffers_by_slot = shader_runtime_detail.get("cbuffers_by_slot", {}) if isinstance(shader_runtime_detail, dict) else {}
    decoded: dict[str, Any] = {}
    saw_variables = False
    hidden_raw_values = False
    for resource in resources:
        if first_text(resource, "type") != "CBV":
            continue
        slot = first_text(resource, "slot")
        detail = cbuffers_by_slot.get(slot, {})
        variables = detail.get("variables", []) if isinstance(detail, dict) else []
        if not variables:
            continue
        saw_variables = True
        has_layout = all(first_text(variable, "name") and first_text(variable, "declared_type") for variable in variables)
        if (not has_layout) or all(is_generic_cbuffer_variable_name(first_text(variable, "name")) for variable in variables):
            hidden_raw_values = True
            continue
        for variable in variables:
            name = first_text(variable, "name")
            if not name:
                continue
            decoded[name] = variable.get("value")
    if decoded and not hidden_raw_values:
        return {
            "decoded": decoded,
            "raw_bytes_available": saw_variables,
            "decode_status": "matched_shader_reflection",
        }
    if decoded and hidden_raw_values:
        return {
            "decoded": decoded,
            "raw_bytes_available": True,
            "raw_values_hidden_from_ai": True,
            "decode_status": "partial_match",
        }
    if saw_variables:
        return {
            "decode_status": "unknown_layout",
            "raw_values_hidden_from_ai": True,
        }
    return {}


def is_generic_cbuffer_variable_name(name: str) -> bool:
    return bool(re.match(r"^cb\d+_v\d+$", str(name or ""), flags=re.IGNORECASE))


def first_unconsumed_raw_event(raw_events: list[dict[str, Any]], event_id: int | None) -> dict[str, Any] | None:
    if not raw_events:
        return None
    if event_id is None:
        return raw_events[0]
    for raw_event in raw_events:
        raw_event_id = safe_optional_int(raw_event.get("event_id") if raw_event.get("event_id") is not None else raw_event.get("eventId"))
        if raw_event_id == event_id:
            return raw_event
    return raw_events[0]


def normalize_existing_binding_record(binding: dict[str, Any]) -> dict[str, Any]:
    access = normalize_access(binding.get("access") or binding.get("type") or binding.get("bind_type") or binding.get("slot"))
    type_text = first_text(binding, "type")
    resource_type = first_text(binding, "resource_type", "resourceType")
    if not resource_type and type_text.upper() not in {"SRV", "UAV", "CBV"}:
        resource_type = type_text
    logical_name = first_text(binding, "logical_name", "logicalName", "runtime_name", "runtimeName", "resource_name", "resourceName", "name")
    byte_size = first_number(binding, "byte_size", "byteSize", "size_bytes", "sizeBytes")
    stride = first_number(binding, "stride", "stride_bytes", "strideBytes")
    element_count = first_number(binding, "element_count", "elementCount", "count")
    if byte_size is None and stride is not None and element_count is not None:
        byte_size = int(stride) * int(element_count)
    record = {
        "slot": normalize_slot_text(binding.get("slot"), access),
        "space": first_number(binding, "space", "register_space", "registerSpace"),
        "name": first_text(binding, "name", "runtime_name", "runtimeName", "resource_name", "resourceName", "shader_name", "shaderName"),
        "logical_name": logical_name,
        "shader_symbol": first_text(binding, "shader_symbol", "shaderSymbol", "shader_name", "shaderName"),
        "runtime_name": first_text(binding, "runtime_name", "runtimeName"),
        "renderdoc_name": first_text(binding, "renderdoc_name", "renderdocName"),
        "type": ACCESS_NAME.get(access, access.upper()),
        "source": first_text(binding, "source"),
        "resource_type": resource_type,
        "byte_size": byte_size,
        "size_bytes": byte_size,
        "stride": stride,
        "stride_bytes": stride,
        "element_count": element_count,
        "access_pattern": first_text(binding, "access_pattern", "accessPattern"),
        "cbv_values": normalize_cbv_values(binding.get("cbv_values")),
    }
    if record["space"] is None and record["slot"]:
        record["space"] = 0
    return drop_empty(record, keep_null_keys={"slot", "byte_size", "size_bytes"})


def build_binding_record(
    access: str,
    renderdoc_resource: dict[str, Any] | None,
    runtime_binding: dict[str, Any] | None,
    cbuffer_detail: dict[str, Any] | None = None,
) -> dict[str, Any]:
    access = normalize_access(access)
    rd_resource = renderdoc_resource or {}
    rt_binding = runtime_binding or {}
    slot = rd_resource.get("slot")
    shader_name = first_text(rd_resource, "name") or first_text(rt_binding, "shader_name", "shaderName")
    runtime_name = first_text(rt_binding, "runtime_name", "runtimeName")
    renderdoc_name = first_text(rd_resource, "resource_name", "resourceName")
    resource_name = first_text(rt_binding, "resource_name", "resourceName")
    logical_name = runtime_name or resource_name or first_text(cbuffer_detail or {}, "name")
    byte_size = size_bytes_from(rd_resource, rt_binding)
    cbuffer_byte_size = first_number(cbuffer_detail or {}, "byte_size", "size")
    if access == "cbv" and cbuffer_byte_size is not None:
        byte_size = cbuffer_byte_size
    elif byte_size is None:
        byte_size = cbuffer_byte_size
    stride = first_number(rt_binding, "stride", "stride_bytes", "strideBytes")
    element_count = first_number(rt_binding, "element_count", "elementCount", "count")
    if byte_size is None and stride is not None and element_count is not None:
        byte_size = int(stride) * int(element_count)
    slot_text = normalize_slot_text(slot if slot is not None else rt_binding.get("slot"), access)
    space = first_number(rd_resource, "space", "register_space", "registerSpace")
    if space is None:
        space = first_number(rt_binding, "space", "register_space", "registerSpace")
    if space is None and slot_text:
        space = 0
    source_parts = []
    if renderdoc_resource is not None:
        source_parts.append("renderdoc")
    if runtime_binding is not None:
        source_parts.append("runtime_sidecar")
    if cbuffer_detail is not None:
        source_parts.append("shader_info")
    source = "+".join(source_parts) if source_parts else "runtime_sidecar"
    resource_type = first_text(rt_binding, "resource_type", "resourceType") or first_text(rd_resource, "resource_type", "resourceType")
    rt_type = first_text(rt_binding, "type")
    if not resource_type and rt_type.upper() not in {"SRV", "UAV", "CBV"}:
        resource_type = rt_type
    cbv_values = normalize_cbv_values((cbuffer_detail or {}).get("cbv_values"))
    display_name = logical_name or renderdoc_name or shader_name
    record = {
        "slot": slot_text,
        "space": space,
        "name": display_name,
        "logical_name": logical_name,
        "shader_symbol": shader_name,
        "runtime_name": runtime_name,
        "renderdoc_name": renderdoc_name,
        "type": ACCESS_NAME.get(access, access.upper()),
        "source": source,
        "resource_type": resource_type,
        "byte_size": byte_size,
        "size_bytes": byte_size,
        "stride": stride,
        "stride_bytes": stride,
        "element_count": element_count,
        "access_pattern": first_text(rt_binding, "access_pattern", "accessPattern"),
        "cbv_values": cbv_values,
    }
    return drop_empty(record, keep_null_keys={"slot", "byte_size", "size_bytes"})


def enrich_binding_with_cbuffer_detail(binding: dict[str, Any], shader_runtime_detail: dict[str, Any] | None) -> dict[str, Any]:
    if not isinstance(binding, dict):
        return {}
    if normalize_access(binding.get("type") or binding.get("access") or binding.get("slot")) != "cbv":
        return binding
    cbuffer_detail = find_cbuffer_detail(shader_runtime_detail, binding)
    if cbuffer_detail is None:
        return binding
    enriched = dict(binding)
    enriched.setdefault("logical_name", cbuffer_detail.get("name"))
    enriched.setdefault("name", cbuffer_detail.get("name"))
    enriched.setdefault("byte_size", cbuffer_detail.get("byte_size"))
    enriched.setdefault("size_bytes", cbuffer_detail.get("byte_size"))
    enriched["cbv_values"] = cbuffer_detail.get("cbv_values", [])
    source = first_text(binding, "source")
    if "shader_info" not in source:
        enriched["source"] = f"{source}+shader_info" if source else "shader_info"
    return normalize_existing_binding_record(enriched)


def find_cbuffer_detail(shader_runtime_detail: dict[str, Any] | None, binding: dict[str, Any]) -> dict[str, Any] | None:
    if not isinstance(shader_runtime_detail, dict):
        return None
    slot_text = normalize_slot_text(binding.get("slot"), "cbv")
    cbuffers = shader_runtime_detail.get("cbuffers_by_slot", {})
    if slot_text and slot_text in cbuffers:
        return cbuffers[slot_text]
    binding_name = normalize_resource_symbol(first_text(binding, "name", "logical_name", "logicalName"))
    for cbuffer in shader_runtime_detail.get("constant_buffers", []):
        if normalize_resource_symbol(first_text(cbuffer, "name")) == binding_name:
            return cbuffer
    return None


def normalize_shader_runtime_detail(shader_info: dict[str, Any]) -> dict[str, Any]:
    shader = {}
    if isinstance(shader_info, dict):
        if isinstance(shader_info.get("shader"), dict):
            shader = shader_info.get("shader") or {}
        else:
            shader = shader_info
    constant_buffers = []
    cbuffers_by_slot: dict[str, dict[str, Any]] = {}
    for raw_buffer in shader.get("constant_buffers", []) if isinstance(shader.get("constant_buffers"), list) else []:
        if not isinstance(raw_buffer, dict):
            continue
        slot_text = normalize_slot_text(first_number(raw_buffer, "slot"), "cbv")
        name = first_text(raw_buffer, "name")
        variables = flatten_shader_variables(raw_buffer.get("variables", []), "")
        values = [
            {
                "cbuffer": name,
                "slot": slot_text,
                "name": variable["name"],
                "value": variable["value"],
                "declared_type": variable.get("declared_type"),
            }
            for variable in variables
        ]
        cbuffer = drop_empty(
            {
                "name": name,
                "slot": slot_text,
                "byte_size": first_number(raw_buffer, "byte_size", "size", "bound_byte_size", "byteSize"),
                "variables": variables,
                "cbv_values": values,
            },
            keep_null_keys={"byte_size"},
        )
        constant_buffers.append(cbuffer)
        if slot_text:
            cbuffers_by_slot[slot_text] = cbuffer
    return {
        "constant_buffers": constant_buffers,
        "cbuffers_by_slot": cbuffers_by_slot,
        "slot_accesses": parse_shader_slot_accesses(first_text(shader, "disassembly")),
    }


def flatten_shader_variables(variables: Any, prefix: str) -> list[dict[str, Any]]:
    flattened = []
    for variable in variables if isinstance(variables, list) else []:
        if not isinstance(variable, dict):
            continue
        name = first_text(variable, "name")
        full_name = f"{prefix}.{name}" if prefix and name else name or prefix
        members = variable.get("members")
        if isinstance(members, list) and members:
            flattened.extend(flatten_shader_variables(members, full_name))
            continue
        if "value" not in variable:
            continue
        flattened.append(
            {
                "name": full_name,
                "declared_type": first_text(variable, "type"),
                "value": normalize_shader_variable_value(variable.get("value")),
            }
        )
    return flattened


def normalize_shader_variable_value(value: Any) -> Any:
    if isinstance(value, list):
        normalized = [normalize_shader_variable_scalar(item) for item in value]
        return normalized[0] if len(normalized) == 1 else normalized
    return normalize_shader_variable_scalar(value)


def normalize_shader_variable_scalar(value: Any) -> Any:
    if isinstance(value, float) and value.is_integer():
        return int(value)
    return value


def normalize_cbv_values(values: Any) -> list[dict[str, Any]]:
    normalized = []
    for value in values if isinstance(values, list) else []:
        if not isinstance(value, dict):
            continue
        normalized.append(
            drop_empty(
                {
                    "cbuffer": first_text(value, "cbuffer"),
                    "slot": normalize_slot_text(value.get("slot"), "cbv"),
                    "name": first_text(value, "name"),
                    "value": value.get("value"),
                }
            )
        )
    return unique_records(normalized, ("cbuffer", "slot", "name", "value"))


def parse_shader_slot_accesses(disassembly: str) -> dict[str, list[str]]:
    slot_accesses: dict[str, list[str]] = {}
    for raw_line in str(disassembly or "").splitlines():
        if ":" not in raw_line:
            continue
        try:
            _, instruction = raw_line.split(":", 1)
        except ValueError:
            continue
        text = instruction.strip()
        if not text:
            continue
        op = text.split()[0].lower()
        slots = re.findall(r"\b([tub]\d+)\b", text)
        if not slots:
            continue
        if "atomic_" in op:
            access = normalize_atomic_function_name(op)
        elif op.startswith("store_"):
            access = "write"
        elif op.startswith(("ld_", "sample", "gather", "resinfo", "bufinfo")):
            access = "read"
        else:
            continue
        for slot in slots:
            slot_accesses.setdefault(slot.lower(), [])
            if access not in slot_accesses[slot.lower()]:
                slot_accesses[slot.lower()].append(access)
    return slot_accesses


def normalize_atomic_function_name(name: str) -> str:
    text = str(name or "").lower()
    if "iadd" in text or text.endswith("add"):
        return "atomic_add"
    if "exchange" in text:
        return "atomic_exchange"
    if "cmp" in text:
        return "atomic_compare_exchange"
    if "min" in text:
        return "atomic_min"
    if "max" in text:
        return "atomic_max"
    if "and" in text:
        return "atomic_and"
    if "or" in text:
        return "atomic_or"
    if "xor" in text:
        return "atomic_xor"
    return "atomic"


def find_runtime_binding(
    runtime_bindings: list[dict[str, Any]],
    access: str,
    renderdoc_resource: dict[str, Any],
) -> tuple[dict[str, Any] | None, int | None]:
    shader_name = normalize_resource_symbol(first_text(renderdoc_resource, "name"))
    resource_name = normalize_resource_symbol(first_text(renderdoc_resource, "resource_name", "resourceName"))
    access = normalize_access(access)
    for index, binding in enumerate(runtime_bindings):
        binding_access = normalize_access(str(binding.get("access", "")))
        if binding_access and binding_access != access:
            continue
        candidates = {
            normalize_resource_symbol(first_text(binding, "shader_symbol", "shaderSymbol")),
            normalize_resource_symbol(first_text(binding, "shader_name", "shaderName")),
            normalize_resource_symbol(first_text(binding, "runtime_name", "runtimeName")),
            normalize_resource_symbol(first_text(binding, "resource_name", "resourceName")),
            normalize_resource_symbol(first_text(binding, "name")),
        }
        if shader_name in candidates or resource_name in candidates:
            return binding, index
    return None, None


def parse_shader_file(path: Path, repo: Path) -> dict[str, Any]:
    source, source_lines = load_shader_source_with_includes(path, repo)
    locate = build_source_locator(repo, source, source_lines)
    stripped = strip_comments(source)
    resources = parse_resource_declarations(stripped, locate)
    groupshared = parse_groupshared(stripped, locate)
    entry_matches = list(NUMTHREADS_RE.finditer(stripped))
    function_records = build_function_records(stripped, resources, locate) if entry_matches else {}
    entries = []
    for match in entry_matches:
        body_start = stripped.find("{", match.end())
        if body_start < 0:
            continue
        body, _ = extract_braced(stripped, body_start)
        entry_name = match.group("entry")
        function_record = function_records.get(entry_name, {})
        usage = collect_function_resource_usage(entry_name, function_records)
        direct_resource_accesses = function_record.get("direct_resource_accesses") or parse_resource_accesses(
            body,
            resources,
            locate,
            body_start + 1,
        )
        entry_resource_names = usage.get("resource_names") or find_resource_mentions(body, resources)
        entry_groupshared_names = find_named_mentions(body, groupshared, "name")
        entry = {
            "entry": entry_name,
            "stage": "compute",
            "source_location": locate(match.start()),
            "thread_group": [int(match.group("x")), int(match.group("y")), int(match.group("z"))],
            "resources_declared": filter_resource_declarations(resources, entry_resource_names),
            "resource_accesses": usage.get("resource_accesses") or direct_resource_accesses,
            "resource_accesses_direct": direct_resource_accesses,
            "loops": parse_loops(body, locate, body_start + 1),
            "branches": parse_branches(body, locate, body_start + 1),
            "atomics": parse_atomics(body, locate, body_start + 1),
            "barriers": parse_barriers(body, locate, body_start + 1),
            "groupshared": filter_named_records(
                groupshared,
                entry_groupshared_names,
                ("name", "type", "count_expression", "source_location"),
            ),
        }
        entries.append(entry)
    return {
        "shader": str(path),
        "stage": "compute" if entries or path.suffix.lower() == ".compute" else "graphics",
        "resources_declared": resources,
        "groupshared": groupshared,
        "entries": entries,
    }


def build_shader_facts(
    repo: Path,
    debug_map: dict[str, Any],
    events: dict[str, Any],
    generated_at: str,
) -> tuple[dict[str, Any], dict[tuple[str, str, str, str], dict[str, Any]]]:
    requested: list[dict[str, Any]] = []
    debug_by_pass = {item["pass_name"]: item for item in debug_map.get("mappings", []) if item.get("pass_name")}

    for event in events.get("events", []):
        debug = debug_by_pass.get(event.get("pass_name"), {})
        shader_path = debug.get("shader_path") or event.get("shader_path")
        shader = shader_path or debug.get("shader") or event.get("shader")
        entry = select_shader_entry(event.get("entry"), debug.get("entry"))
        if shader:
            requested.append(
                {
                    "shader": shader,
                    "entry": entry,
                    "stage": event.get("stage") or debug.get("stage"),
                    "shader_hash": event.get("shader_hash"),
                    "permutation": event.get("permutation"),
                    "fallback_thread_group": debug.get("thread_group"),
                }
            )

    event_pass_names = {event.get("pass_name") for event in events.get("events", [])}
    for debug in debug_map.get("mappings", []):
        if debug.get("pass_name") in event_pass_names:
            continue
        shader = debug.get("shader_path") or debug.get("shader")
        shader_text = str(shader or "")
        stage = str(debug.get("stage") or "")
        if shader and stage.lower() != "compute" and not shader_text.lower().endswith(".compute"):
            continue
        if shader:
            requested.append(
                {
                    "shader": shader,
                    "entry": debug.get("entry") or "",
                    "stage": debug.get("stage"),
                    "shader_hash": "",
                    "permutation": None,
                    "fallback_thread_group": debug.get("thread_group"),
                }
            )

    parsed_cache: dict[Path, dict[str, Any]] = {}
    facts = []
    facts_by_key: dict[tuple[str, str, str, str], dict[str, Any]] = {}
    seen: set[tuple[str, str, str, str]] = set()

    for request in requested:
        shader_text = str(request.get("shader") or "")
        entry = str(request.get("entry") or "")
        shader_hash = str(request.get("shader_hash") or "")
        stage = str(request.get("stage") or "")
        shader_path = resolve_shader_path(repo, shader_text)
        shader_identity = shader_identity_for_fact(repo, shader_text, shader_path)
        key = canonical_shader_fact_key(shader_hash, shader_identity.get("shader_path") or shader_identity["shader"], stage, entry)
        if key in seen:
            continue
        seen.add(key)

        parsed = None
        if shader_path is not None and shader_path.exists():
            parsed = parsed_cache.get(shader_path)
            if parsed is None:
                parsed = parse_shader_file(shader_path, repo)
                parsed_cache[shader_path] = parsed

        fact = build_shader_fact(
            shader_identity,
            entry,
            stage,
            shader_hash,
            request.get("permutation") if isinstance(request.get("permutation"), dict) else None,
            request.get("fallback_thread_group"),
            parsed,
        )
        facts.append(fact)
        index_shader_fact(facts_by_key, fact)

    return {
        "schema_version": SCHEMA_VERSION,
        "generated_at_utc": generated_at,
        "shaders": [serialize_shader_fact_public(fact) for fact in facts],
    }, facts_by_key


def build_shader_fact(
    shader_identity: dict[str, str],
    entry: str,
    stage: str,
    shader_hash: str,
    permutation: dict[str, Any] | None,
    fallback_thread_group: list[int] | None,
    parsed: dict[str, Any] | None,
) -> dict[str, Any]:
    parsed = parsed or {}
    entry_fact = None
    for candidate in parsed.get("entries", []):
        if candidate.get("entry") == entry:
            entry_fact = candidate
            break
    if entry_fact is None and parsed.get("entries") and not entry:
        entry_fact = parsed["entries"][0]
        entry = entry_fact.get("entry", entry)

    has_entry_fact = entry_fact is not None
    entry_fact = entry_fact or {}
    return drop_empty(
        {
            "shader": shader_identity["shader"],
            "shader_path": shader_identity.get("shader_path"),
            "entry": entry,
            "stage": stage or entry_fact.get("stage") or parsed.get("stage") or "unknown",
            "source_location": entry_fact.get("source_location"),
            "shader_hash": shader_hash,
            "permutation": permutation,
            "thread_group": entry_fact.get("thread_group") or fallback_thread_group or [0, 0, 0],
            "resources_declared": entry_fact.get("resources_declared", []) if has_entry_fact else [],
            "resource_accesses": entry_fact.get("resource_accesses", []) if has_entry_fact else [],
            "resource_accesses_direct": entry_fact.get("resource_accesses_direct", []) if has_entry_fact else [],
            "loops": entry_fact.get("loops", []) if has_entry_fact else [],
            "branches": entry_fact.get("branches", []) if has_entry_fact else [],
            "atomics": entry_fact.get("atomics", []) if has_entry_fact else [],
            "barriers": entry_fact.get("barriers", []) if has_entry_fact else [],
            "groupshared": (entry_fact.get("groupshared") or []) if has_entry_fact else [],
        }
    )


def normalize_catalog(data: dict[str, Any], generated_at: str, source_path: Path | None) -> dict[str, Any]:
    # Catalog semantics are authored by AI/humans in docs/gpu-pass-catalog.
    # Keep this function as a copier/normalizer only: do not invent purpose,
    # resource meanings, display names, source paths or performance hints here.
    raw_passes = data.get("passes", []) if isinstance(data, dict) else []
    normalized: list[dict[str, Any]] = []

    for raw in raw_passes if isinstance(raw_passes, list) else []:
        if not isinstance(raw, dict):
            continue

        pass_id = first_text(raw, "pass_id", "passId", "id") or extract_marker_pass_id(
            first_text(raw, "marker", "marker_label")
        )
        if not pass_id:
            continue

        source = raw.get("source") if isinstance(raw.get("source"), dict) else {}
        shader = first_text(raw, "shader")
        kernel = first_text(raw, "kernel", "entry")
        pass_type = normalize_pass_type(raw)

        normalized.append(
            drop_empty(
                {
                    "pass_id": pass_id,
                    "display_name": first_text(raw, "display_name", "displayName", "name"),
                    "type": pass_type,
                    "owner": first_text(raw, "owner"),
                    "shader": shader,
                    "kernel": kernel,
                    "shader_pass": first_text(raw, "shader_pass", "shaderPass"),
                    "purpose": first_text(raw, "purpose"),
                    "inputs": normalize_catalog_resources(raw.get("inputs")),
                    "outputs": normalize_catalog_resources(raw.get("outputs")),
                    "dispatch": raw.get("dispatch") if isinstance(raw.get("dispatch"), dict) else None,
                    "raster_draw": raw.get("raster_draw") if isinstance(raw.get("raster_draw"), dict) else None,
                    "indirect_draw": raw.get("indirect_draw") if isinstance(raw.get("indirect_draw"), dict) else None,
                    "pipeline": raw.get("pipeline") if isinstance(raw.get("pipeline"), dict) else None,
                    "performance_hints": raw.get("performance_hints")
                    if isinstance(raw.get("performance_hints"), dict)
                    else None,
                    "source": drop_empty(
                        {
                            "file": first_text(source, "file"),
                            "function": first_text(source, "function"),
                            "line": safe_optional_int(source.get("line")) if isinstance(source, dict) else None,
                        }
                    ),
                    "aliases": normalize_string_list(raw.get("aliases")),
                    "marker_labels": normalize_string_list(raw.get("marker_labels") or raw.get("markers")),
                    "notes": normalize_string_list(raw.get("notes")),
                }
            )
        )

    normalized.sort(key=lambda item: item["pass_id"])
    return drop_empty(
        {
            "schema_version": str(data.get("schema_version") or data.get("version") or "1"),
            "generated_at_utc": generated_at,
            "source_path": str(source_path) if source_path else None,
            "passes": normalized,
        }
    )


def normalize_catalog_resources(raw_resources: Any) -> list[dict[str, Any]]:
    resources: list[dict[str, Any]] = []
    if not isinstance(raw_resources, list):
        return resources

    for raw in raw_resources:
        if not isinstance(raw, dict):
            continue
        resources.append(
            drop_empty(
                {
                    "name": first_text(raw, "name"),
                    "type": first_text(raw, "type"),
                    "access": first_text(raw, "access"),
                    "meaning": first_text(raw, "meaning"),
                    "scale": first_text(raw, "scale"),
                }
            )
        )
    return resources


def build_catalog_lookup(catalog: dict[str, Any]) -> dict[str, dict[str, Any]]:
    lookup: dict[str, dict[str, Any]] = {}
    for item in catalog.get("passes", []) if isinstance(catalog.get("passes"), list) else []:
        if not isinstance(item, dict):
            continue

        add_catalog_lookup(lookup, item.get("pass_id"), item)
        add_catalog_lookup(lookup, item.get("display_name"), item)
        add_catalog_lookup(lookup, item.get("shader"), item)
        for alias in normalize_string_list(item.get("aliases")):
            add_catalog_lookup(lookup, alias, item)
        for marker_label in normalize_string_list(item.get("marker_labels")):
            add_catalog_lookup(lookup, marker_label, item)
    return lookup


def add_catalog_lookup(lookup: dict[str, dict[str, Any]], key: Any, item: dict[str, Any]) -> None:
    normalized = normalize_lookup_key(key)
    if normalized:
        lookup[normalized] = item


def find_catalog_entry_for_event(
    event: dict[str, Any],
    catalog_lookup: dict[str, dict[str, Any]],
) -> dict[str, Any]:
    candidates = [
        event.get("pass_id"),
        event.get("pass_name"),
        event.get("debug_label"),
    ]
    for marker in normalize_string_list(event.get("marker_path")):
        candidates.append(marker)

    for candidate in candidates:
        item = catalog_lookup.get(normalize_lookup_key(candidate))
        if item:
            return item

    shader_candidates = [
        (event.get("shader_path") or event.get("shader"), event.get("entry")),
    ]
    for shader, entry in shader_candidates:
        if not shader or not entry:
            continue
        for item in catalog_lookup.values():
            if same_path_or_name(shader, item.get("shader")) and str(entry) == str(item.get("kernel", "")):
                return item

    return {}


def compact_catalog_for_context(catalog_entry: dict[str, Any]) -> dict[str, Any] | None:
    if not catalog_entry:
        return None

    return drop_empty(
        {
            "display_name": catalog_entry.get("display_name"),
            "owner": catalog_entry.get("owner"),
            "purpose": catalog_entry.get("purpose"),
            "type": catalog_entry.get("type"),
            "inputs": catalog_entry.get("inputs"),
            "outputs": catalog_entry.get("outputs"),
            "dispatch": catalog_entry.get("dispatch"),
            "raster_draw": catalog_entry.get("raster_draw"),
            "indirect_draw": catalog_entry.get("indirect_draw"),
            "pipeline": catalog_entry.get("pipeline"),
            "performance_hints": catalog_entry.get("performance_hints"),
            "source": catalog_entry.get("source"),
        }
    )


def stable_pass_id_for_event(event: dict[str, Any], catalog_entry: dict[str, Any]) -> str:
    return (
        first_text(catalog_entry, "pass_id")
        or first_text(event, "pass_id")
        or extract_marker_pass_id(first_text(event, "pass_name"))
        or extract_marker_pass_id(first_text(event, "debug_label"))
    )


def normalize_pass_type(
    event: dict[str, Any],
    catalog_entry: dict[str, Any] | None = None,
    event_type: str | None = None,
) -> str:
    catalog_entry = catalog_entry or {}
    raw_type = first_text(catalog_entry, "type") or first_text(event, "pass_type", "passType", "type")
    text = raw_type.strip().lower()
    if text in ("compute", "raster_draw", "indirect_draw"):
        return text
    if text in ("draw_indirect", "indirectdraw", "drawindexedindirect"):
        return "indirect_draw"
    if text in ("draw", "drawcall", "draw_indexed", "drawindexed", "raster"):
        if bool(event.get("indirect")) or (event_type or "").lower() == "draw_indirect":
            return "indirect_draw"
        draw_cfg = catalog_entry.get("indirect_draw") or catalog_entry.get("draw") or catalog_entry.get("dispatch")
        if isinstance(draw_cfg, dict):
            draw_kind = first_text(draw_cfg, "draw_kind", "kind")
            if "indirect" in draw_kind.lower():
                return "indirect_draw"
        return "raster_draw"
    if normalize_dispatch(event.get("dispatch")) or (event_type or "").lower() in ("dispatch", "compute"):
        return "compute"
    if event.get("draw") is not None:
        return "indirect_draw" if bool(event.get("indirect")) else "raster_draw"
    return "compute" if (event_type or "").lower() == "dispatch" else "raster_draw"


def normalize_overview_event_type(event: dict[str, Any]) -> str:
    event_type = first_text(event, "event_type", "type").lower()
    if event_type in ("compute", "dispatch"):
        return "dispatch"
    if event_type in ("draw_indirect", "indirect_draw", "drawindexedindirect"):
        return "draw_indirect"
    if event_type in ("draw", "drawcall", "draw_indexed", "drawindexed", "raster_draw"):
        return "draw"
    if normalize_dispatch(event.get("dispatch")):
        return "dispatch"
    if event.get("draw") is not None:
        return "draw_indirect" if bool(event.get("indirect")) else "draw"
    return event_type or "unknown"


def build_overview_dispatch(event: dict[str, Any], catalog_entry: dict[str, Any]) -> dict[str, Any] | None:
    groups = normalize_dispatch(event.get("dispatch"))
    if not groups:
        return None
    dispatch_cfg = catalog_entry.get("dispatch") if isinstance(catalog_entry.get("dispatch"), dict) else {}
    threads_per_group = normalize_thread_group(dispatch_cfg.get("thread_group_size"))
    estimated_threads = list_product(groups) * list_product(threads_per_group) if list_product(threads_per_group) else None
    return drop_empty(
        {
            "groups": groups,
            "threads_per_group": threads_per_group if any(threads_per_group) else None,
            "estimated_threads": estimated_threads,
        }
    )


def build_overview_draw(event: dict[str, Any]) -> Any:
    draw = event.get("draw")
    if isinstance(draw, dict):
        return drop_empty(
            {
                "vertex_count": first_number(draw, "vertex_count", "vertexCount", "numVertices"),
                "instance_count": first_number(draw, "instance_count", "instanceCount", "numInstances"),
                "index_count": first_number(draw, "index_count", "indexCount", "numIndices"),
                "draw": draw,
            }
        )
    return draw


def build_overview_pipeline(event: dict[str, Any], catalog_entry: dict[str, Any]) -> dict[str, Any] | None:
    pipeline: dict[str, Any] = {}
    raw_pipeline = event.get("pipeline") if isinstance(event.get("pipeline"), dict) else {}
    catalog_pipeline = catalog_entry.get("pipeline") if isinstance(catalog_entry.get("pipeline"), dict) else {}
    pipeline.update(catalog_pipeline)
    pipeline.update(raw_pipeline)
    return drop_empty(pipeline) or None


def build_draw_resource_summary(
    event: dict[str, Any],
    bindings: list[dict[str, Any]],
    catalog_entry: dict[str, Any],
) -> dict[str, Any] | None:
    raw = event.get("draw_resources") if isinstance(event.get("draw_resources"), dict) else {}
    summary = {
        "vertex_inputs": normalize_string_list(raw.get("vertex_inputs")),
        "structured_buffers": normalize_string_list(raw.get("structured_buffers")),
        "textures": normalize_string_list(raw.get("textures")),
        "render_targets": normalize_string_list(raw.get("render_targets")),
    }

    for binding in bindings if isinstance(bindings, list) else []:
        if not isinstance(binding, dict):
            continue
        name = first_text(binding, "name", "shader_name", "resource_id")
        resource_type = first_text(binding, "type", "resource_type")
        hlsl_type = first_text(binding, "hlsl_type")
        if resource_type == "buffer" or "Buffer" in hlsl_type:
            summary["structured_buffers"].append(name)
        elif resource_type == "texture" or "Texture" in hlsl_type:
            summary["textures"].append(name)

    for resource in catalog_entry.get("inputs", []) if isinstance(catalog_entry.get("inputs"), list) else []:
        if not isinstance(resource, dict):
            continue
        name = first_text(resource, "name")
        resource_type = first_text(resource, "type")
        if not name:
            continue
        if "Texture" in resource_type:
            summary["textures"].append(name)
        elif "Buffer" in resource_type or "Args" in resource_type:
            summary["structured_buffers"].append(name)

    return drop_empty({key: unique_strings(value) for key, value in summary.items()}) or None


def compact_catalog_for_overview(catalog_entry: dict[str, Any]) -> dict[str, Any] | None:
    if not catalog_entry:
        return None
    return drop_empty(
        {
            "display_name": catalog_entry.get("display_name"),
            "type": catalog_entry.get("type"),
            "shader": catalog_entry.get("shader"),
            "kernel": catalog_entry.get("kernel"),
            "shader_pass": catalog_entry.get("shader_pass"),
            "raster_draw": catalog_entry.get("raster_draw"),
            "indirect_draw": catalog_entry.get("indirect_draw"),
            "source": catalog_entry.get("source"),
        }
    )


def build_pass_overview(
    events: dict[str, Any],
    catalog: dict[str, Any],
    generated_at: str,
    frame: int | None,
) -> dict[str, Any]:
    catalog_lookup = build_catalog_lookup(catalog)
    overview_events = []

    for event in events.get("events", []):
        if not isinstance(event, dict):
            continue
        catalog_entry = find_catalog_entry_for_event(event, catalog_lookup)
        event_type = normalize_overview_event_type(event)
        pass_type = normalize_pass_type(event, catalog_entry, event_type)
        overview_events.append(
            drop_empty(
                {
                    "event_id": event.get("event_id"),
                    "action_id": event.get("action_id"),
                    "name": first_text(event, "name", "pass_name", "debug_label", "event_name"),
                    "pass_id": stable_pass_id_for_event(event, catalog_entry),
                    "matched_pass_id": stable_pass_id_for_event(event, catalog_entry),
                    "display_name": catalog_entry.get("display_name") if catalog_entry else event.get("display_name"),
                    "pass_type": pass_type,
                    "type": event_type,
                    "event_type": event_type,
                    "duration_ms": event.get("duration_ms") if event.get("duration_ms") is not None else event.get("gpu_time_ms"),
                    "dispatch": build_overview_dispatch(event, catalog_entry) if pass_type == "compute" else None,
                    "draw": build_overview_draw(event) if pass_type in ("raster_draw", "indirect_draw") else None,
                    "pipeline": build_overview_pipeline(event, catalog_entry) if pass_type in ("raster_draw", "indirect_draw") else None,
                    "resources": build_draw_resource_summary(event, [], catalog_entry)
                    if pass_type in ("raster_draw", "indirect_draw")
                    else None,
                    "shader": compact_shader_identity_for_context(event, catalog_entry),
                    "catalog": compact_catalog_for_overview(catalog_entry),
                    "marker_path": event.get("marker_path"),
                }
            )
        )

    return {
        "schema_version": SCHEMA_VERSION,
        "frame": frame if frame is not None else events.get("frame"),
        "generated_at_utc": generated_at,
        "events": overview_events,
    }


def build_frame_context(
    frame: int | None,
    events: dict[str, Any],
    catalog: dict[str, Any],
    resource_views_by_event: dict[int, dict[str, Any]],
    resource_usage_summary: list[dict[str, Any]],
    generated_at: str,
    capture: dict[str, str],
) -> dict[str, Any]:
    catalog_lookup = build_catalog_lookup(catalog)
    output_passes = []
    resource_flow_by_pass = build_resource_flow_by_pass(resource_usage_summary)

    for event in events.get("events", []):
        pass_name = event.get("pass_name") or ""
        event_id = safe_optional_int(event.get("event_id"))
        catalog_entry = find_catalog_entry_for_event(event, catalog_lookup)
        pass_id = stable_pass_id_for_event(event, catalog_entry)
        resource_record = resource_views_by_event.get(event_id, {})
        output_passes.append(
            build_context_pass(
                event,
                catalog_entry,
                resource_record.get("resources", []) if isinstance(resource_record, dict) else [],
                resource_record.get("constants", {}) if isinstance(resource_record, dict) else {},
                resource_record.get("unresolved_bindings", []) if isinstance(resource_record, dict) else [],
                event_id in resource_views_by_event,
                resource_flow_by_pass.get(pass_id, {}),
            )
        )

    data_quality = build_context_data_quality(events)
    return drop_empty(
        {
        "schema_version": SCHEMA_VERSION,
        "generated_at_utc": generated_at,
        "capture": drop_empty(
            {
                "source": capture.get("source") or "RenderDoc",
                "engine": capture.get("engine"),
                "engine_build": capture.get("engine_build"),
            }
        ),
        "frame": frame,
        "summary": build_context_summary(output_passes),
        "context_sources": build_context_sources(catalog),
        "pass_graph": build_pass_graph(output_passes, resource_usage_summary),
        "gpu_passes": output_passes,
        "data_quality": data_quality,
        }
    )


def build_context_pass(
    event: dict[str, Any],
    catalog_entry: dict[str, Any],
    resources: list[dict[str, Any]],
    constants: dict[str, Any],
    unresolved_bindings: list[dict[str, Any]],
    resources_matched: bool,
    resource_flow: dict[str, Any] | None = None,
) -> dict[str, Any]:
    unresolved = []
    if not catalog_entry:
        unresolved.append({"type": "missing_catalog_entry", "pass_name": event.get("pass_name")})
    if not resources_matched:
        unresolved.append({"type": "missing_resources", "event_id": event.get("event_id")})

    has_cbv_resource = any(first_text(resource, "type") == "CBV" for resource in resources)
    if has_cbv_resource and not constants:
        unresolved.append({"type": "missing_constants", "event_id": event.get("event_id")})

    warnings = build_pass_data_quality_warnings(resources, constants, unresolved_bindings)
    data_quality = {
        "linked": not unresolved,
        "warnings": unique_records(unresolved + warnings, ("type", "resource", "slot", "renderdoc_name", "reason", "event_id", "pass_name")),
    }
    pass_id = (
        stable_pass_id_for_event(event, catalog_entry)
    )
    event_type = normalize_overview_event_type(event)
    pass_type = normalize_pass_type(event, catalog_entry, event_type)
    context_resources: Any = resources
    if pass_type in ("raster_draw", "indirect_draw"):
        context_resources = build_draw_resource_summary(event, resources, catalog_entry)
    result = {
        "pass_id": pass_id,
        "matched_pass_id": pass_id,
        "pass_name": event.get("pass_name"),
        "display_name": catalog_entry.get("display_name") if catalog_entry else None,
        "event_id": event.get("event_id"),
        "event_type": event_type,
        "pass_type": pass_type,
        "gpu_time_ms": event.get("gpu_time_ms"),
        "dispatch": event.get("dispatch"),
        "draw": event.get("draw"),
        "pipeline": build_overview_pipeline(event, catalog_entry) if pass_type in ("raster_draw", "indirect_draw") else None,
        "queue": event.get("queue"),
        "command_list": event.get("command_list"),
        "pipeline_hash": event.get("pipeline_hash"),
        "shader": compact_shader_identity_for_context(event, catalog_entry),
        "derived_metrics": (derived := build_derived_pass_metrics(event, resources, catalog_entry)),
        "performance_relevant_facts": build_performance_relevant_facts(resources, catalog_entry, derived),
        "catalog": compact_catalog_for_context(catalog_entry),
        "resources": context_resources,
        "resource_bindings": resources if pass_type in ("raster_draw", "indirect_draw") else None,
        "resource_flow": resource_flow,
        "constants": constants,
        "source_ids": build_pass_source_ids(event, catalog_entry),
        "data_quality": data_quality,
    }
    return drop_empty(result)


def compact_debug_for_context(debug: dict[str, Any]) -> dict[str, Any]:
    return drop_empty(
        {
            "cpu_scope": debug.get("cpu_scope"),
            "cpu_file": debug.get("cpu_file"),
            "cpu_function": debug.get("cpu_function"),
        }
    )


def compact_shader_identity_for_context(
    event: dict[str, Any],
    catalog_entry: dict[str, Any],
) -> dict[str, Any]:
    catalog_shader = first_text(catalog_entry, "shader")
    catalog_entry_point = first_text(catalog_entry, "kernel")
    shader = os.path.basename(catalog_shader) if catalog_shader else first_text(event, "shader")
    entry = catalog_entry_point or first_text(event, "shader_entry")
    stage = "compute" if first_text(event, "event_type", "type").lower() in ("compute", "dispatch") else first_text(event, "stage")
    dispatch_cfg = catalog_entry.get("dispatch") if isinstance(catalog_entry.get("dispatch"), dict) else {}
    numthreads = normalize_thread_group(dispatch_cfg.get("thread_group_size"))
    return drop_empty(
        {
            "shader": shader,
            "shader_path": catalog_shader,
            "entry": entry,
            "stage": stage or None,
            "shader_hash": event.get("shader_hash"),
            "numthreads": numthreads if any(numthreads) else None,
            "permutation": event.get("permutation"),
        }
    )


def compact_shader_facts_for_context(shader_fact: dict[str, Any] | None) -> dict[str, Any]:
    public = serialize_shader_fact_public(shader_fact)
    return {
        "resources_declared": public["resources_declared"],
        "resource_accesses": public["resource_accesses"],
        "loops": public["loops"],
        "branches": public["branches"],
        "sync_primitives": public["sync_primitives"],
        "atomic_operations": public["atomic_operations"],
        "groupshared_variables": public["groupshared_variables"],
        "source_refs": public["source_refs"],
    }


def find_shader_fact_for_event(
    event: dict[str, Any],
    debug: dict[str, Any],
    shader_facts_by_key: dict[tuple[str, str, str, str], dict[str, Any]],
) -> tuple[dict[str, Any] | None, str | None]:
    stage = event.get("stage") or debug.get("stage") or ""
    entry = select_shader_entry(event.get("entry"), debug.get("entry"))
    shader_hash = event.get("shader_hash") or ""
    shader_candidates = [
        event.get("shader_path"),
        debug.get("shader_path"),
        event.get("shader"),
        debug.get("shader"),
    ]
    if shader_hash:
        for shader in shader_candidates:
            fact = shader_facts_by_key.get(canonical_shader_fact_key(shader_hash, shader or "", stage, entry))
            if fact:
                return fact, "shader_hash"
    for shader in shader_candidates:
        fact = shader_facts_by_key.get(canonical_shader_fact_key("", shader or "", stage, entry))
        if fact:
            return fact, "shader_file_stage_entry"
    for shader in shader_candidates:
        fact = shader_facts_by_key.get(canonical_shader_fact_key("", shader or "", "", entry))
        if fact:
            return fact, "shader_file_entry"
    return None, None


def build_context_summary(output_passes: list[dict[str, Any]]) -> dict[str, Any]:
    timings = [
        pass_context.get("gpu_time_ms")
        for pass_context in output_passes
        if isinstance(pass_context.get("gpu_time_ms"), (int, float))
    ]
    summary = {"gpu_pass_count": len(output_passes)}
    if timings:
        summary["total_gpu_time_ms"] = round(sum(float(value) for value in timings), 6)
    return summary


def build_pass_graph(output_passes: list[dict[str, Any]], resources: list[dict[str, Any]]) -> dict[str, Any]:
    pass_keys = set()
    for pass_context in output_passes:
        if not isinstance(pass_context, dict):
            continue
        for key in ("pass_id", "pass_name"):
            value = pass_context.get(key)
            if value:
                pass_keys.add(value)
    edges = []
    for resource in resources if isinstance(resources, list) else []:
        if not isinstance(resource, dict):
            continue
        logical_name = first_text(resource, "logical_name", "name", "resource_name")
        producers = [name for name in normalize_string_list(resource.get("producer_passes")) if name in pass_keys]
        consumers = [name for name in normalize_string_list(resource.get("consumer_passes")) if name in pass_keys]
        if not logical_name or not producers or not consumers:
            continue
        for producer in producers:
            for consumer in consumers:
                if producer == consumer:
                    continue
                edges.append(
                    drop_empty(
                        {
                            "from": producer,
                            "to": consumer,
                            "resource": logical_name,
                            "resource_type": first_text(resource, "resource_type"),
                            "size_bytes": first_number(resource, "size_bytes", "byte_size"),
                            "stride_bytes": first_number(resource, "stride_bytes", "stride"),
                            "element_count": first_number(resource, "element_count"),
                        },
                        keep_null_keys={"size_bytes"},
                    )
                )
    edges = unique_records(edges, ("from", "to", "resource"))
    edges.sort(key=lambda item: (str(item.get("from") or ""), str(item.get("to") or ""), str(item.get("resource") or "")))
    return {"edges": edges}


def build_resource_flow_by_pass(resources: list[dict[str, Any]]) -> dict[str, dict[str, list[dict[str, Any]]]]:
    by_pass: dict[str, dict[str, list[dict[str, Any]]]] = defaultdict(lambda: {"inputs_from": [], "outputs_to": []})
    for resource in resources if isinstance(resources, list) else []:
        if not isinstance(resource, dict):
            continue
        resource_name = first_text(resource, "logical_name", "name", "resource_name")
        producers = normalize_string_list(resource.get("producer_passes"))
        consumers = normalize_string_list(resource.get("consumer_passes"))
        if not resource_name:
            continue
        for consumer in consumers:
            for producer in producers:
                if producer == consumer:
                    continue
                by_pass[consumer]["inputs_from"].append(
                    drop_empty(
                        {
                            "pass_id": producer,
                            "resource": resource_name,
                            "hlsl_type": first_text(resource, "hlsl_type"),
                            "source": first_text(resource, "source"),
                        }
                    )
                )
                by_pass[producer]["outputs_to"].append(
                    drop_empty(
                        {
                            "pass_id": consumer,
                            "resource": resource_name,
                            "hlsl_type": first_text(resource, "hlsl_type"),
                            "source": first_text(resource, "source"),
                        }
                    )
                )

    return {
        pass_id: {
            "inputs_from": unique_resource_flow_records(flow.get("inputs_from", [])),
            "outputs_to": unique_resource_flow_records(flow.get("outputs_to", [])),
        }
        for pass_id, flow in by_pass.items()
    }


def unique_resource_flow_records(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    by_key: dict[tuple[str, str], dict[str, Any]] = {}
    for record in records:
        key = (str(record.get("pass_id") or ""), str(record.get("resource") or ""))
        existing = by_key.get(key)
        if existing is None or (record.get("source") == "catalog" and existing.get("source") != "catalog"):
            by_key[key] = record
    return list(by_key.values())


def collect_cbv_values(bindings: list[dict[str, Any]]) -> list[dict[str, Any]]:
    values = []
    for binding in bindings:
        for value in binding.get("cbv_values", []) if isinstance(binding.get("cbv_values"), list) else []:
            if isinstance(value, dict):
                values.append(value)
    return unique_records(values, ("cbuffer", "slot", "name", "value"))


def build_context_data_quality(events: dict[str, Any]) -> dict[str, Any]:
    warnings = []
    event_ids = [event.get("event_id") for event in events.get("events", []) if event.get("event_id") is not None]
    duplicate_event_ids = sorted({event_id for event_id in event_ids if event_ids.count(event_id) > 1})
    for event_id in duplicate_event_ids:
        warnings.append({"type": "duplicate_event_id", "event_id": event_id})

    pass_names = [event.get("pass_name") for event in events.get("events", []) if event.get("pass_name")]
    duplicate_pass_names = sorted({pass_name for pass_name in pass_names if pass_names.count(pass_name) > 1})
    for pass_name in duplicate_pass_names:
        warnings.append({"type": "pass_name_appears_multiple_times", "pass_name": pass_name})
    return {
        "linked": not warnings,
        "warnings": unique_records(warnings, ("type", "event_id", "pass_name")),
    }


def build_pass_data_quality_warnings(
    resources: list[dict[str, Any]],
    constants: dict[str, Any],
    unresolved_bindings: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    warnings = []
    has_cbv_resource = any(first_text(resource, "type") == "CBV" for resource in resources)
    if has_cbv_resource and not constants:
        warnings.append({"type": "missing_constants"})
    if constants and first_text(constants, "decode_status") == "unknown_layout":
        warnings.append({"type": "constants_unknown_layout"})
    if constants and first_text(constants, "decode_status") == "partial_match":
        warnings.append({"type": "constants_partially_decoded"})
    for binding in unresolved_bindings if isinstance(unresolved_bindings, list) else []:
        warnings.append(
            drop_empty(
                {
                    "type": "unresolved_binding",
                    "slot": first_text(binding, "slot"),
                    "renderdoc_name": first_text(binding, "renderdoc_name"),
                    "reason": first_text(binding, "reason"),
                }
            )
        )
    return unique_records(warnings, ("type", "resource", "slot", "renderdoc_name", "reason"))


def build_context_sources(catalog: dict[str, Any]) -> dict[str, str]:
    sources = {
        "events": RAW_GPU_PASS_EVENTS_FILE,
        "overview": GPU_PASS_OVERVIEW_FILE,
        "resources": GPU_PASS_RESOURCES_FILE,
    }
    if catalog.get("passes"):
        sources["catalog"] = GPU_PASS_CATALOG_FILE
    return sources


def build_pass_source_ids(
    event: dict[str, Any],
    catalog_entry: dict[str, Any],
) -> dict[str, str]:
    shader = first_text(catalog_entry, "shader") or first_text(event, "shader")
    entry = first_text(catalog_entry, "kernel") or first_text(event, "shader_entry")
    if shader and entry:
        return {"shader_fact_id": f"{shader}:{entry}"}
    if shader:
        return {"shader_fact_id": shader}
    return {}


def list_product(values: list[int] | None) -> int:
    if not values:
        return 0
    product = 1
    for value in values:
        product *= safe_int(value)
    return product


def max_resource_element_count(resources: list[dict[str, Any]], desired_accesses: set[str]) -> int | None:
    counts = []
    for resource in resources:
        actual_access = resource.get("actual_access", []) if isinstance(resource.get("actual_access"), list) else []
        if not set(actual_access).intersection(desired_accesses):
            continue
        element_count = first_number(resource, "element_count")
        if element_count is None:
            continue
        counts.append(int(element_count))
    return max(counts) if counts else None


def build_performance_relevant_facts(
    resources: list[dict[str, Any]],
    catalog_entry: dict[str, Any],
    derived: dict[str, Any] | None = None,
) -> dict[str, Any]:
    hints = catalog_entry.get("performance_hints") if isinstance(catalog_entry.get("performance_hints"), dict) else {}
    catalog_memory_pattern = hints.get("memory_pattern")
    access_patterns = [resource.get("access_pattern") for resource in resources if isinstance(resource.get("access_pattern"), dict)]

    # Automatic memory pattern classification from resource bindings
    auto_patterns = classify_pass_memory_patterns(resources, catalog_entry, derived, hints)

    risk_tags: list[str] = []
    if hints.get("has_atomics"):
        risk_tags.append("has_atomics")
    if hints.get("has_group_shared_memory"):
        risk_tags.append("has_group_shared_memory")
    if hints.get("branch_divergence_risk"):
        risk_tags.append(f"branch_risk:{hints.get('branch_divergence_risk')}")
    expected_bottleneck = hints.get("expected_bottleneck")
    if expected_bottleneck:
        risk_tags.append(str(expected_bottleneck))

    memory_pattern_known = bool(
        catalog_memory_pattern
        or auto_patterns.get("dominant_read_pattern")
        or auto_patterns.get("dominant_write_pattern")
        or any(pattern.get("known") is True for pattern in access_patterns)
    )

    return drop_empty(
        {
            "has_atomic": hints.get("has_atomics"),
            "has_groupshared": hints.get("has_group_shared_memory"),
            "memory_pattern_known": memory_pattern_known,
            "memory_pattern": catalog_memory_pattern,
            "auto_memory_pattern_tags": auto_patterns.get("tags") or None,
            "dominant_read_pattern": auto_patterns.get("dominant_read_pattern"),
            "dominant_write_pattern": auto_patterns.get("dominant_write_pattern"),
            "per_resource_patterns": auto_patterns.get("per_resource") or None,
            "possible_risk_tags": risk_tags or None,
        }
    )


def classify_pass_memory_patterns(
    resources: list[dict[str, Any]],
    catalog_entry: dict[str, Any],
    derived: dict[str, Any] | None,
    hints: dict[str, Any],
) -> dict[str, Any]:
    """Classify memory access patterns for all resources in a pass."""
    total_invocations = derived.get("total_invocations") if derived else None
    is_data_parallel = bool(total_invocations and total_invocations > 1)
    has_atomics = bool(hints.get("has_atomics"))

    per_resource: list[dict[str, Any]] = []
    all_tags: set[str] = set()

    for r in resources:
        result = _classify_single_resource_pattern(r, catalog_entry, is_data_parallel, has_atomics)
        if result:
            per_resource.append(drop_empty(result))
            all_tags.update(result.get("tags", []))

    # Derive dominant patterns from catalog meaning text
    dominant_read, dominant_write = _infer_dominant_patterns_from_catalog(catalog_entry, all_tags)

    # Fallback: pick from auto-classified tags
    if not dominant_read:
        read_tags = sorted(t for t in all_tags if "read" in t.lower() and t != "read")
        dominant_read = read_tags[0] if read_tags else None
    if not dominant_write:
        write_tags = sorted(t for t in all_tags if any(kw in t.lower() for kw in ("write", "atomic", "compact", "scatter")))
        dominant_write = write_tags[0] if write_tags else None

    return drop_empty({
        "dominant_read_pattern": dominant_read,
        "dominant_write_pattern": dominant_write,
        "tags": sorted(all_tags) or None,
        "per_resource": per_resource or None,
    })


def _infer_dominant_patterns_from_catalog(
    catalog_entry: dict[str, Any],
    auto_tags: set[str],
) -> tuple[str | None, str | None]:
    """Use catalog meaning text and dispatch metadata to pick dominant patterns."""
    dispatch_cfg = catalog_entry.get("dispatch") if isinstance(catalog_entry.get("dispatch"), dict) else {}
    data_parallel_unit = dispatch_cfg.get("data_parallel_unit", "")

    # Aggregate meaning texts from all catalog inputs/outputs
    meanings: list[str] = []
    for section in ("inputs", "outputs"):
        items = catalog_entry.get(section) if isinstance(catalog_entry.get(section), list) else []
        for item in items:
            m = first_text(item, "meaning") if isinstance(item, dict) else ""
            if m:
                meanings.append(m.lower())

    read_pattern = None
    write_pattern = None

    if data_parallel_unit and "one_thread_per_instance" in data_parallel_unit:
        if any("data_parallel_read" in t for t in auto_tags):
            read_pattern = "data_parallel_read"
        if any("data_parallel_write" in t for t in auto_tags):
            write_pattern = "data_parallel_write"

    combined = " ".join(meanings)
    if any(kw in combined for kw in ("atomic", "interlocked")):
        if not write_pattern and any("atomic_scatter" in t for t in auto_tags):
            write_pattern = "atomic_scatter"
    if any(kw in combined for kw in ("compact", "append", "workset")):
        if any("stream_compact" in t for t in auto_tags):
            write_pattern = "stream_compact"
    if any(kw in combined for kw in ("grid", "spatial", "cell", "neighbor")):
        if not read_pattern and any("scatter_gather" in t for t in auto_tags):
            read_pattern = "scatter_gather"

    return read_pattern, write_pattern


def _classify_single_resource_pattern(
    resource: dict[str, Any],
    catalog_entry: dict[str, Any],
    is_data_parallel: bool,
    has_atomics: bool,
) -> dict[str, Any] | None:
    """Classify the memory access pattern for a single resource binding."""
    bind_type = resource.get("bind_type", "")
    access = resource.get("access", "")
    hlsl_type = resource.get("hlsl_type", "")
    name = resource.get("name", "")
    slot = resource.get("slot", "")

    if not bind_type or not name:
        return None

    tags: list[str] = []
    confidence = "medium"

    # ── Read-side classification ──
    if bind_type == "CBV":
        tags.append("uniform_read")
        confidence = "high"

    elif bind_type == "SRV":
        if "Texture" in hlsl_type or "Texture" in str(resource.get("type", "")):
            tags.append("texture_sample")
            confidence = "high"
        elif "StructuredBuffer" in hlsl_type:
            if is_data_parallel:
                tags.append("data_parallel_read")
                confidence = "high"
            else:
                tags.append("indexed_read")
                confidence = "medium"
        elif "ByteAddressBuffer" in hlsl_type:
            tags.append("scattered_read")
            confidence = "medium"
        elif "ConstantBuffer" in hlsl_type:
            tags.append("uniform_read")
            confidence = "high"
        else:
            tags.append("read")
            confidence = "low"

    # ── Write-side classification ──
    elif bind_type == "UAV":
        if "AppendStructuredBuffer" in hlsl_type or "ConsumeStructuredBuffer" in hlsl_type:
            tags.append("stream_compact")
            confidence = "high"
        elif has_atomics and "RWStructuredBuffer" in hlsl_type:
            tags.append("atomic_scatter")
            confidence = "high"
        elif "RWStructuredBuffer" in hlsl_type:
            if access == "read_write":
                tags.append("scatter_read_modify_write")
                confidence = "medium"
            elif is_data_parallel:
                tags.append("data_parallel_write")
                confidence = "high"
            else:
                tags.append("scatter_write")
                confidence = "medium"
        elif "RWByteAddressBuffer" in hlsl_type:
            tags.append("scattered_read_write")
            confidence = "medium"
        elif "RWTexture" in hlsl_type:
            tags.append("texture_scatter_write")
            confidence = "medium"
        else:
            if access == "write":
                tags.append("write")
            else:
                tags.append("read_write")
            confidence = "low"

    if not tags:
        return None

    return {
        "name": name,
        "slot": slot,
        "hlsl_type": hlsl_type or resource.get("type"),
        "pattern": tags[0],
        "tags": tags,
        "secondary_tags": tags[1:] if len(tags) > 1 else None,
        "confidence": confidence,
    }


def build_possible_risk_tags(
    resources: list[dict[str, Any]],
    atomic_targets: list[str],
    branch_conditions: list[str],
) -> list[str]:
    tags = []
    if any(target.endswith("[0]") for target in atomic_targets):
        tags.append("global_atomic_counter")
    has_compact_append = any(
        isinstance(resource.get("access_pattern"), dict) and first_text(resource.get("access_pattern"), "write") == "compact_append_by_atomic_index"
        for resource in resources
    )
    if atomic_targets and branch_conditions and has_compact_append:
        tags.append("data_dependent_compaction")
    return tags


def extract_atomic_target_expression(arguments: str) -> str:
    if not arguments:
        return ""
    return compact_whitespace(str(arguments).split(",", 1)[0])


def unique_texts(values: list[str]) -> list[str]:
    unique = []
    for value in values:
        text = str(value or "").strip()
        if text and text not in unique:
            unique.append(text)
    return unique


def normalize_hint_expression(value: str) -> str:
    text = compact_whitespace(value)
    if not text:
        return ""
    return re.sub(r"\((?:u?int|float|half|min16int|min16uint)\)\s*", "", text)


def build_derived_pass_metrics(
    event: dict[str, Any],
    resources: list[dict[str, Any]],
    catalog_entry: dict[str, Any],
) -> dict[str, Any]:
    dispatch_groups = normalize_dispatch(event.get("dispatch"))
    dispatch_cfg = catalog_entry.get("dispatch") if isinstance(catalog_entry.get("dispatch"), dict) else {}
    numthreads = normalize_thread_group(dispatch_cfg.get("thread_group_size"))
    thread_group_count = list_product(dispatch_groups)
    threads_per_group = list_product(numthreads)
    total_invocations = thread_group_count * threads_per_group if thread_group_count and threads_per_group else None
    input_element_count = max_resource_element_count(resources, {"read"})
    output_capacity = max_resource_element_count(resources, {"write", "atomic", "atomic_add", "atomic_exchange", "atomic_compare_exchange", "atomic_min", "atomic_max", "atomic_and", "atomic_or", "atomic_xor"})
    likely_one_thread_per_instance = None
    if total_invocations and input_element_count and output_capacity:
        likely_one_thread_per_instance = total_invocations == input_element_count == output_capacity
    elif total_invocations and input_element_count:
        likely_one_thread_per_instance = total_invocations == input_element_count
    gpu_time_ms = event.get("gpu_time_ms")
    us_per_invocation = None
    if gpu_time_ms is not None and total_invocations and total_invocations > 0 and gpu_time_ms > 0:
        us_per_invocation = round(gpu_time_ms * 1000.0 / total_invocations, 2)
    derived = drop_empty(
        {
            "dispatch_groups": dispatch_groups,
            "numthreads": numthreads if any(numthreads) else None,
            "thread_group_count": thread_group_count or None,
            "threads_per_group": threads_per_group or None,
            "total_invocations": total_invocations,
            "us_per_invocation": us_per_invocation,
            "likely_one_thread_per_instance": likely_one_thread_per_instance,
            "input_element_count": input_element_count,
            "output_capacity": output_capacity,
        }
    )
    return derived


def serialize_shader_fact_public(shader_fact: dict[str, Any] | None) -> dict[str, Any]:
    shader_fact = shader_fact or {}
    numthreads = normalize_thread_group(shader_fact.get("thread_group"))
    source_refs: dict[str, str] = {}
    source_ref_by_location: dict[str, str] = {}

    def register_source_ref(location: str) -> str:
        text = first_text({"location": location}, "location")
        if not text:
            return ""
        if text in source_ref_by_location:
            return source_ref_by_location[text]
        ref_id = f"S{len(source_ref_by_location) + 1}"
        source_ref_by_location[text] = ref_id
        source_refs[ref_id] = text
        return ref_id

    return {
        "shader": first_text(shader_fact, "shader"),
        "entry": first_text(shader_fact, "entry"),
        "stage": first_text(shader_fact, "stage") or "unknown",
        "numthreads": numthreads,
        "resources_declared": serialize_resource_declarations_public(shader_fact),
        "resource_accesses": serialize_resource_accesses_public(shader_fact.get("resource_accesses", []), register_source_ref),
        "loops": serialize_loops_public(shader_fact.get("loops", []), register_source_ref),
        "branches": serialize_branches_public(shader_fact.get("branches", []), register_source_ref),
        "sync_primitives": serialize_sync_primitives_public(shader_fact.get("barriers", []), register_source_ref),
        "atomic_operations": serialize_atomic_operations_public(shader_fact.get("atomics", []), register_source_ref),
        "groupshared_variables": serialize_groupshared_public(shader_fact.get("groupshared", []), register_source_ref),
        "source_refs": source_refs,
    }


def serialize_resource_declarations_public(shader_fact: dict[str, Any]) -> list[dict[str, Any]]:
    records = shader_fact.get("resources_declared", []) if isinstance(shader_fact.get("resources_declared"), list) else []
    actual_access_by_resource: dict[str, list[str]] = {}
    for record in shader_fact.get("resource_accesses", []) if isinstance(shader_fact.get("resource_accesses"), list) else []:
        resource = first_text(record, "resource")
        access = first_text(record, "access")
        if not resource or not access:
            continue
        actual_access_by_resource.setdefault(resource, [])
        if access not in actual_access_by_resource[resource]:
            actual_access_by_resource[resource].append(access)
    for record in shader_fact.get("atomics", []) if isinstance(shader_fact.get("atomics"), list) else []:
        target_symbol = extract_atomic_target_symbol(first_text(record, "arguments"))
        atomic_name = normalize_atomic_function_name(first_text(record, "function"))
        if not target_symbol or not atomic_name:
            continue
        actual_access_by_resource.setdefault(target_symbol, [])
        if atomic_name not in actual_access_by_resource[target_symbol]:
            actual_access_by_resource[target_symbol].append(atomic_name)

    public = []
    for record in records:
        name = first_text(record, "name")
        public.append(
            {
                "name": name,
                "declared_type": compact_whitespace(first_text(record, "type")),
                "declared_capability": declared_resource_capability(first_text(record, "type"), first_text(record, "bind_type")),
                "actual_access": normalize_actual_access_list(actual_access_by_resource.get(name, [])),
            }
        )
    return unique_records(public, ("name", "declared_type", "declared_capability"))


def serialize_resource_accesses_public(records: list[dict[str, Any]], register_source_ref: Any) -> list[dict[str, Any]]:
    public = []
    for record in records:
        public.append(
            {
                "resource": first_text(record, "resource"),
                "access": first_text(record, "access"),
                "index_expression": first_text(record, "index_expression"),
                "source_ref": register_source_ref(first_text(record, "source_location")),
            }
        )
    return unique_records(public, ("resource", "access", "index_expression", "source_ref"))


def serialize_loops_public(records: list[dict[str, Any]], register_source_ref: Any) -> list[dict[str, Any]]:
    public = []
    for record in records:
        public.append(
            {
                "source_ref": register_source_ref(first_text(record, "source_location")),
                "loop_variable": first_text(record, "loop_variable"),
                "condition": first_text(record, "condition"),
                "step": first_text(record, "increment"),
            }
        )
    return unique_records(public, ("source_ref", "loop_variable", "condition", "step"))


def serialize_branches_public(records: list[dict[str, Any]], register_source_ref: Any) -> list[dict[str, Any]]:
    public = []
    for record in records:
        public.append(
            {
                "source_ref": register_source_ref(first_text(record, "source_location")),
                "condition": normalize_hint_expression(first_text(record, "condition")),
            }
        )
    return unique_records(public, ("source_ref", "condition"))


def serialize_sync_primitives_public(records: list[dict[str, Any]], register_source_ref: Any) -> list[dict[str, Any]]:
    public = []
    for record in records:
        public.append(
            {
                "function": first_text(record, "function"),
                "source_ref": register_source_ref(first_text(record, "source_location")),
            }
        )
    return unique_records(public, ("function", "source_ref"))


def serialize_atomic_operations_public(records: list[dict[str, Any]], register_source_ref: Any) -> list[dict[str, Any]]:
    public = []
    for record in records:
        public.append(
            {
                "function": first_text(record, "function"),
                "arguments": first_text(record, "arguments"),
                "source_ref": register_source_ref(first_text(record, "source_location")),
            }
        )
    return unique_records(public, ("function", "arguments", "source_ref"))


def serialize_groupshared_public(records: list[dict[str, Any]], register_source_ref: Any) -> list[dict[str, Any]]:
    public = []
    for record in records:
        public.append(
            {
                "name": first_text(record, "name"),
                "type": normalize_declared_resource_type(first_text(record, "type")),
                "count_expression": first_text(record, "count_expression"),
                "source_ref": register_source_ref(first_text(record, "source_location")),
            }
        )
    return unique_records(public, ("name", "type", "count_expression", "source_ref"))


def build_function_records(source: str, resources: list[dict[str, Any]], locate: Any) -> dict[str, dict[str, Any]]:
    records: dict[str, dict[str, Any]] = {}
    for match in FUNCTION_DEF_RE.finditer(source):
        name = match.group("name")
        open_index = source.find("{", match.end() - 1)
        body, close_index = extract_braced(source, open_index)
        if close_index < 0:
            continue
        direct_resource_accesses = parse_resource_accesses(body, resources, locate, open_index + 1)
        direct_resource_names = find_resource_mentions(body, resources)
        for access in direct_resource_accesses:
            resource_name = first_text(access, "resource")
            if resource_name:
                direct_resource_names.add(resource_name)
        records[name] = {
            "name": name,
            "source_location": locate(match.start("name")),
            "body": body,
            "direct_resource_accesses": direct_resource_accesses,
            "direct_resource_names": direct_resource_names,
        }

    function_names = set(records)
    for record in records.values():
        record["calls"] = find_function_calls(record["body"], function_names, record["name"])
    return records


def find_function_calls(body: str, function_names: set[str], current_name: str) -> list[str]:
    calls = []
    for name in sorted(function_names):
        if name == current_name:
            continue
        if re.search(r"\b" + re.escape(name) + r"\s*\(", body):
            calls.append(name)
    return calls


def collect_function_resource_usage(
    function_name: str,
    function_records: dict[str, dict[str, Any]],
    call_stack: tuple[str, ...] | None = None,
) -> dict[str, Any]:
    record = function_records.get(function_name)
    if record is None:
        return {"resource_names": set(), "resource_accesses": []}

    stack = call_stack or (function_name,)
    resource_names = set(record.get("direct_resource_names", set()))
    resource_accesses = []
    for access in record.get("direct_resource_accesses", []):
        enriched = dict(access)
        enriched["source_function"] = function_name
        enriched["access_scope"] = "direct" if len(stack) == 1 else "indirect"
        resource_accesses.append(drop_empty(enriched))

    for callee in record.get("calls", []):
        if callee in stack:
            continue
        nested = collect_function_resource_usage(callee, function_records, stack + (callee,))
        resource_names.update(nested["resource_names"])
        resource_accesses.extend(nested["resource_accesses"])

    return {
        "resource_names": resource_names,
        "resource_accesses": unique_resource_access_records(resource_accesses),
    }


def find_resource_mentions(body: str, resources: list[dict[str, Any]]) -> set[str]:
    return find_named_mentions(body, resources, "name")


def find_named_mentions(body: str, records: list[dict[str, Any]], name_key: str) -> set[str]:
    mentions = set()
    for record in records:
        name = first_text(record, name_key)
        if not name:
            continue
        if re.search(r"\b" + re.escape(name) + r"\b", body):
            mentions.add(name)
    return mentions


def filter_resource_declarations(resources: list[dict[str, Any]], resource_names: set[str]) -> list[dict[str, Any]]:
    return filter_named_records(
        resources,
        resource_names,
        ("name", "type", "access", "bind_type", "slot", "space", "source_location"),
    )


def filter_named_records(records: list[dict[str, Any]], names: set[str], identity_keys: tuple[str, ...]) -> list[dict[str, Any]]:
    normalized_names = {normalize_resource_symbol(name) for name in names if name}
    filtered = []
    for record in records:
        name = first_text(record, "name")
        if normalize_resource_symbol(name) in normalized_names:
            filtered.append(record)
    return unique_records(filtered, identity_keys)


def normalize_declared_resource_type(resource_type: str) -> str:
    text = compact_whitespace(resource_type)
    if not text:
        return ""
    if text == "cbuffer":
        return "ConstantBuffer"
    match = re.match(r"[_A-Za-z]\w*", text)
    return match.group(0) if match else text


def declared_resource_capability(resource_type: str, bind_type: str) -> str:
    text = compact_whitespace(resource_type)
    if bind_type == "CBV":
        return "read_only"
    if text.startswith("RW"):
        return "read_write"
    if text.startswith("AppendStructuredBuffer"):
        return "write_only"
    if text.startswith("ConsumeStructuredBuffer"):
        return "read_only"
    return "read_only"


def parse_resource_declarations(source: str, locate: Any) -> list[dict[str, Any]]:
    declarations = []
    for match in RESOURCE_DECL_RE.finditer(source):
        resource_type = compact_whitespace(match.group("type"))
        bind_type = declared_resource_bind_type(resource_type, match.group("slot"))
        declarations.append(
            drop_empty(
                {
                    "name": match.group("name"),
                    "type": resource_type,
                    "access": declared_resource_access(resource_type, bind_type),
                    "bind_type": bind_type,
                    "slot": match.group("slot"),
                    "space": safe_optional_int(match.group("space")),
                    "source_location": locate(match.start("type")),
                }
            )
        )
    for match in CBUFFER_RE.finditer(source):
        declarations.append(
            drop_empty(
                {
                    "name": match.group("name"),
                    "type": "cbuffer",
                    "access": "read",
                    "bind_type": "CBV",
                    "slot": match.group("slot"),
                    "space": safe_optional_int(match.group("space")),
                    "source_location": locate(match.start("name")),
                }
            )
        )
    return unique_records(declarations, ("name", "type", "access", "bind_type", "slot", "space", "source_location"))


def parse_groupshared(source: str, locate: Any) -> list[dict[str, Any]]:
    values = []
    for match in GROUPSHARED_RE.finditer(source):
        values.append(
            drop_empty(
                {
                    "name": match.group("name"),
                    "type": compact_whitespace(match.group("type")),
                    "count_expression": compact_whitespace(match.group("count") or ""),
                    "source_location": locate(match.start("name")),
                }
            )
        )
    return unique_records(values, ("name", "type", "count_expression", "source_location"))


def parse_resource_accesses(body: str, resources: list[dict[str, Any]], locate: Any, body_offset: int) -> list[dict[str, Any]]:
    records = []
    resource_names = {item["name"] for item in resources}
    for name in sorted(resource_names):
        pattern = re.compile(r"\b" + re.escape(name) + r"\s*\[")
        for match in pattern.finditer(body):
            open_index = body.find("[", match.start())
            index_expr, close_index = extract_bracket(body, open_index, "[", "]")
            if close_index < 0:
                continue
            access = classify_resource_access(body, match.start(), close_index)
            records.append(
                {
                    "resource": name,
                    "access": access,
                    "index_expression": compact_whitespace(index_expr),
                    "source_location": locate(body_offset + match.start()),
                }
            )
    return unique_records(records, ("resource", "access", "index_expression", "source_location"))


def classify_resource_access(body: str, start: int, close_index: int) -> str:
    line_start = body.rfind("\n", 0, start) + 1
    prefix = body[line_start:start]
    if re.search(r"\bInterlocked[_A-Za-z0-9]*\s*\([^;\n]*$", prefix):
        return "write"

    suffix = body[close_index + 1 : close_index + 16].lstrip()
    if suffix.startswith(("=", "+=", "-=", "*=", "/=", "&=", "|=", "^=", "++", "--")):
        return "write"
    return "read"


def parse_loops(body: str, locate: Any, body_offset: int) -> list[dict[str, Any]]:
    loops = []
    for match in FOR_RE.finditer(body):
        init = compact_whitespace(match.group("init"))
        condition = compact_whitespace(match.group("condition"))
        variable_match = re.search(r"(?:\b(?:uint|int|float|half|min16int|min16uint)\s+)?(?P<var>[_A-Za-z]\w*)\s*=", init)
        loops.append(
            drop_empty(
                {
                    "loop_variable": variable_match.group("var") if variable_match else "",
                    "init": init,
                    "condition": condition,
                    "increment": compact_whitespace(match.group("increment")),
                    "source_location": locate(body_offset + match.start()),
                }
            )
        )
    return unique_records(loops, ("loop_variable", "init", "condition", "increment", "source_location"))


def parse_branches(body: str, locate: Any, body_offset: int) -> list[dict[str, Any]]:
    branches = []
    for keyword in BRANCH_KEYWORDS:
        for match in re.finditer(r"\b" + re.escape(keyword) + r"\s*\(", body):
            open_index = body.find("(", match.end() - 1)
            condition, close_index = extract_bracket(body, open_index, "(", ")")
            if close_index >= 0:
                branches.append(
                    {
                        "type": keyword,
                        "condition": compact_whitespace(condition),
                        "source_location": locate(body_offset + match.start()),
                    }
                )
    return unique_records(branches, ("type", "condition", "source_location"))


def parse_atomics(body: str, locate: Any, body_offset: int) -> list[dict[str, Any]]:
    atomics = []
    for match in ATOMIC_RE.finditer(body):
        args, close_index = extract_bracket(body, match.end() - 1, "(", ")")
        if close_index >= 0:
            atomics.append(
                {
                    "function": match.group("name"),
                    "arguments": compact_whitespace(args),
                    "source_location": locate(body_offset + match.start()),
                }
            )
    return unique_records(atomics, ("function", "arguments", "source_location"))


def parse_barriers(body: str, locate: Any, body_offset: int) -> list[dict[str, Any]]:
    return unique_records(
        [
            {
                "function": match.group("name"),
                "source_location": locate(body_offset + match.start()),
            }
            for match in BARRIER_RE.finditer(body)
        ],
        ("function", "source_location"),
    )


def load_shader_source_with_includes(path: Path, repo: Path) -> tuple[str, list[tuple[Path, int]]]:
    lines = load_shader_source_lines(path.resolve(), repo.resolve(), set())
    source = "\n".join(line for line, _, _ in lines)
    locations = [(line_path, line_number) for _, line_path, line_number in lines]
    return source, locations


def load_shader_source_lines(path: Path, repo: Path, include_stack: set[Path]) -> list[tuple[str, Path, int]]:
    if path in include_stack:
        return []

    try:
        raw_lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    except OSError:
        return []

    include_stack.add(path)
    output: list[tuple[str, Path, int]] = []
    for line_number, line in enumerate(raw_lines, start=1):
        include_match = INCLUDE_RE.match(line)
        if include_match:
            include_value = include_match.group("quoted") or include_match.group("angled") or ""
            include_path = resolve_shader_include_path(include_value, path.parent, repo)
            if include_path is not None and include_path.exists():
                output.extend(load_shader_source_lines(include_path.resolve(), repo, include_stack))
                continue
        output.append((line, path, line_number))
    include_stack.remove(path)
    return output


def resolve_shader_include_path(value: str, current_dir: Path, repo: Path) -> Path | None:
    if not value:
        return None

    raw = Path(value)
    if raw.is_absolute():
        return raw.resolve() if raw.exists() else None

    relative_to_current = (current_dir / raw).resolve()
    if relative_to_current.exists():
        return relative_to_current

    relative_to_repo = (repo / raw).resolve()
    if relative_to_repo.exists():
        return relative_to_repo

    name = raw.name
    if not name:
        return None
    for root_name in ("Assets", "Packages"):
        root = repo / root_name
        if not root.exists():
            continue
        for candidate in root.rglob(name):
            if candidate.is_file():
                return candidate.resolve()
    return None


def build_source_locator(repo: Path, source: str, source_lines: list[tuple[Path, int]]):
    line_starts = [0]
    for match in re.finditer("\n", source):
        line_starts.append(match.end())

    def locate(position: int) -> str:
        if not source_lines:
            return ""
        line_index = max(0, min(bisect_right(line_starts, position) - 1, len(source_lines) - 1))
        source_path, source_line = source_lines[line_index]
        try:
            source_text = source_path.resolve().relative_to(repo.resolve()).as_posix()
        except Exception:
            source_text = source_path.as_posix()
        return f"{source_text}:{source_line}"

    return locate


def strip_comments(source: str) -> str:
    def preserve_shape(match: re.Match[str]) -> str:
        return "".join("\n" if char == "\n" else " " for char in match.group(0))

    source = re.sub(r"/\*.*?\*/", preserve_shape, source, flags=re.DOTALL)
    source = re.sub(r"//[^\n\r]*", preserve_shape, source)
    return source


def extract_braced(source: str, open_index: int) -> tuple[str, int]:
    return extract_bracket(source, open_index, "{", "}")


def extract_bracket(source: str, open_index: int, open_char: str, close_char: str) -> tuple[str, int]:
    if open_index < 0 or open_index >= len(source) or source[open_index] != open_char:
        return "", -1
    depth = 0
    for index in range(open_index, len(source)):
        char = source[index]
        if char == open_char:
            depth += 1
        elif char == close_char:
            depth -= 1
            if depth == 0:
                return source[open_index + 1 : index], index
    return "", -1


def resolve_shader_path(repo: Path, value: str) -> Path | None:
    if not value:
        return None
    raw = Path(value)
    if raw.is_absolute() and raw.exists():
        return raw.resolve()
    direct = (repo / value).resolve()
    if direct.exists():
        return direct
    name = raw.name
    if not name or "." not in name:
        return None
    for root_name in ("Assets", "Packages"):
        root = repo / root_name
        if not root.exists():
            continue
        for candidate in root.rglob(name):
            if candidate.is_file():
                return candidate.resolve()
    return None


def resolve_path_only(value: str) -> Path:
    try:
        return Path(value).resolve()
    except Exception:
        return Path(value)


def split_shader_identity(value: str) -> dict[str, str]:
    text = (value or "").strip().replace("\\", "/")
    if not text:
        return {"shader": ""}
    path = Path(text)
    if "/" in text or "\\" in (value or ""):
        return {"shader": path.name, "shader_path": text}
    return {"shader": text}


def shader_identity_for_fact(repo: Path, shader_text: str, shader_path: Path | None) -> dict[str, str]:
    if shader_path is not None:
        try:
            shader_path_text = shader_path.resolve().relative_to(repo.resolve()).as_posix()
        except Exception:
            shader_path_text = shader_path.as_posix()
        return {"shader": shader_path.name, "shader_path": shader_path_text}
    return split_shader_identity(shader_text)


def canonical_shader_fact_key(shader_hash: str, shader: str, stage: str, entry: str) -> tuple[str, str, str, str]:
    shader_text = (shader or "").replace("\\", "/").strip().lower()
    return (
        (shader_hash or "").strip().lower(),
        shader_text,
        (stage or "").strip().lower(),
        (entry or "").strip(),
    )


def index_shader_fact(
    facts_by_key: dict[tuple[str, str, str, str], dict[str, Any]],
    fact: dict[str, Any],
) -> None:
    shader_hash = fact.get("shader_hash") or ""
    stage = fact.get("stage") or ""
    entry = fact.get("entry") or ""
    candidates = [
        fact.get("shader_path"),
        fact.get("shader"),
    ]
    for shader in candidates:
        if not shader:
            continue
        facts_by_key[canonical_shader_fact_key(shader_hash, shader, stage, entry)] = fact
        facts_by_key[canonical_shader_fact_key("", shader, stage, entry)] = fact
        facts_by_key[canonical_shader_fact_key("", shader, "", entry)] = fact


def normalize_event_type(raw: dict[str, Any]) -> str:
    event_type = first_text(raw, "event_type", "eventType", "type").lower()
    if event_type in ("compute", "dispatch"):
        return "dispatch"
    if event_type in ("indirect_draw", "draw_indirect", "drawindexedindirect"):
        return "draw_indirect"
    if event_type in ("raster_draw", "draw", "drawcall", "draw_indexed", "drawindexed"):
        return "draw_indirect" if bool(raw.get("indirect")) else "draw"
    if event_type:
        return event_type
    if normalize_dispatch(raw.get("dispatch")):
        return "dispatch"
    if raw.get("draw") is not None:
        return "draw_indirect" if bool(raw.get("indirect")) else "draw"
    return "unknown"


def infer_stage(stage_text: str, entry: str, event_type: str | None) -> str:
    stage = (stage_text or "").strip().lower()
    if stage in ("compute", "vertex", "fragment", "pixel", "geometry", "hull", "domain", "mesh", "amplification"):
        return "fragment" if stage == "pixel" else stage
    if (event_type or "").lower() in ("dispatch", "compute"):
        return "compute"
    if entry and entry.lower().startswith("cs"):
        return "compute"
    return stage or "unknown"


def select_shader_entry(event_entry: Any, debug_entry: Any) -> str:
    event_text = str(event_entry or "").strip()
    debug_text = str(debug_entry or "").strip()
    if debug_text and event_text.lower() in ("", "main"):
        return debug_text
    return event_text or debug_text


def declared_resource_bind_type(resource_type: str, slot: str | None = None) -> str:
    slot_prefix = (slot or "")[:1].lower()
    if slot_prefix == "u":
        return "UAV"
    if slot_prefix == "b":
        return "CBV"
    if slot_prefix == "s":
        return "Sampler"
    if resource_type.startswith(("RW", "Append", "Consume")):
        return "UAV"
    if resource_type.startswith("Sampler"):
        return "Sampler"
    if resource_type.startswith(("ConstantBuffer", "cbuffer")):
        return "CBV"
    return "SRV"


def declared_resource_access(resource_type: str, bind_type: str) -> str:
    if bind_type == "UAV":
        return "write"
    if resource_type.startswith("ConsumeStructuredBuffer"):
        return "read"
    if resource_type.startswith("AppendStructuredBuffer"):
        return "write"
    return "read"


def normalize_thread_group(value: Any) -> list[int]:
    if isinstance(value, list) and len(value) >= 3:
        return [safe_int(value[0]), safe_int(value[1]), safe_int(value[2])]
    if isinstance(value, tuple) and len(value) >= 3:
        return [safe_int(value[0]), safe_int(value[1]), safe_int(value[2])]
    return [0, 0, 0]


def normalize_dispatch(value: Any) -> list[int] | None:
    if isinstance(value, list) and len(value) >= 3:
        return [safe_int(value[0]), safe_int(value[1]), safe_int(value[2])]
    return None


def normalize_string_list(value: Any) -> list[str]:
    normalized = []
    for item in value if isinstance(value, list) else []:
        text = str(item or "").strip()
        if text:
            normalized.append(text)
    return normalized


def unique_strings(value: Any) -> list[str]:
    unique = []
    for item in value if isinstance(value, list) else []:
        text = str(item or "").strip()
        if text and text not in unique:
            unique.append(text)
    return unique


def normalize_access(value: Any) -> str:
    text = str(value or "").strip().lower()
    if text.startswith("t") and text[1:].isdigit():
        return "srv"
    if text.startswith("u") and text[1:].isdigit():
        return "uav"
    if text.startswith("b") and text[1:].isdigit():
        return "cbv"
    if text in ("read", "readonly", "ro", "t", "srv"):
        return "srv"
    if text in ("write", "readwrite", "rw", "u", "uav"):
        return "uav"
    if text in ("constant", "constantbuffer", "b", "cbv"):
        return "cbv"
    return text or "srv"


def format_slot(access: str, slot: Any) -> str | None:
    if slot is None or slot == "":
        return None
    try:
        slot_index = int(slot)
    except Exception:
        return str(slot)
    return f"{ACCESS_SLOT_PREFIX.get(access, '')}{slot_index}"


def normalize_slot_text(slot: Any, access: str) -> str | None:
    if slot is None or slot == "":
        return None
    text = str(slot).strip()
    if re.match(r"^[tubs]\d+$", text, flags=re.IGNORECASE):
        return text[:1].lower() + text[1:]
    return format_slot(access, slot)


def size_bytes_from(renderdoc_resource: dict[str, Any], runtime_binding: dict[str, Any]) -> int | None:
    bound_byte_size = first_number(renderdoc_resource, "bound_byte_size", "byte_size", "byteSize")
    if bound_byte_size is not None:
        return int(bound_byte_size)
    length = first_number(renderdoc_resource, "length")
    if length is not None:
        return int(length)
    count = first_number(runtime_binding, "element_count", "elementCount", "count")
    stride = first_number(runtime_binding, "stride_bytes", "strideBytes")
    if count is not None and stride is not None:
        return int(count) * int(stride)
    return None


def binding_sort_key(binding: dict[str, Any]) -> tuple[str, str, str]:
    return (
        str(binding.get("type") or ""),
        str(binding.get("slot") or ""),
        str(binding.get("name") or ""),
    )


def compact_whitespace(value: str) -> str:
    return re.sub(r"\s+", " ", value or "").strip()


def first_text(data: dict[str, Any], *keys: str) -> str:
    for key in keys:
        value = data.get(key)
        if value is None:
            continue
        text = str(value).strip()
        if text:
            return text
    return ""


def first_number(data: dict[str, Any], *keys: str) -> int | float | None:
    for key in keys:
        value = data.get(key)
        if value is None or value == "":
            continue
        if isinstance(value, (int, float)):
            return value
        try:
            number = float(value)
            return int(number) if number.is_integer() else number
        except Exception:
            continue
    return None


def safe_int(value: Any) -> int:
    try:
        return int(value)
    except Exception:
        return 0


def safe_optional_int(value: Any) -> int | None:
    if value is None or value == "":
        return None
    try:
        return int(value)
    except Exception:
        return None


def normalize_name(value: str) -> str:
    return (value or "").strip().lower()


def normalize_resource_symbol(value: str) -> str:
    text = normalize_name(value)
    if text.startswith("g_"):
        text = text[2:]
    return text


def drop_empty(data: dict[str, Any], keep_null_keys: set[str] | None = None) -> dict[str, Any]:
    keep_null_keys = keep_null_keys or set()
    result = {}
    for key, value in data.items():
        if value is None and key not in keep_null_keys:
            continue
        if value == "" or value == [] or value == {}:
            continue
        result[key] = value
    return result


def unique_records(records: list[dict[str, Any]], keys: tuple[str, ...]) -> list[dict[str, Any]]:
    seen = set()
    unique = []
    for record in records:
        identity = tuple(str(record.get(key, "")) for key in keys)
        if identity in seen:
            continue
        seen.add(identity)
        unique.append(record)
    return unique


def unique_bindings(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return unique_records(records, ("slot", "space", "name", "logical_name", "shader_symbol", "type", "source", "byte_size"))


def unique_resource_access_records(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return unique_records(
        records,
        ("resource", "access", "index_expression", "source_location", "source_function", "access_scope"),
    )


def resolve_default_inputs(repo: Path, args: argparse.Namespace) -> Path | None:
    capture = Path(args.capture).resolve() if args.capture else None
    artifact_dir = capture.parent if capture else latest_capture_dir(repo)

    if artifact_dir is not None:
        capture = capture or next(
            (
                path
                for path in sorted(
                    artifact_dir.glob("*.rdc"),
                    key=lambda item: (item.stat().st_mtime, str(item)),
                    reverse=True,
                )
            ),
            None,
        )
    return capture


def resolve_catalog_path(repo: Path, args: argparse.Namespace) -> Path | None:
    if args.no_catalog:
        return None
    if args.catalog:
        return Path(args.catalog).resolve()

    default_catalog = repo / DEFAULT_GPU_PASS_CATALOG
    return default_catalog if default_catalog.exists() else None


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Read raw RenderDoc gpu_pass_events.json and build derived "
            "gpu_pass_overview.json, gpu_pass_resources.json and gpu_frame_context.json without performance inference."
        )
    )
    parser.add_argument("--repo", default=".", help="Repository root.")
    parser.add_argument("--capture", help="Optional .rdc capture path. When provided, RenderDoc events are fetched unless --no-fetch-events is set.")
    parser.add_argument("--events-in", help=f"Existing raw {RAW_GPU_PASS_EVENTS_FILE} JSON to normalize instead of fetching through RenderDoc.")
    parser.add_argument("--catalog", help=f"GPU pass catalog YAML/JSON to join into {GPU_FRAME_CONTEXT_FILE}.")
    parser.add_argument("--no-catalog", action="store_true", help="Skip GPU pass catalog discovery and joining.")
    parser.add_argument("--artifact-dir", help="Output directory. Defaults to .workspace/artifacts/renderdoc-analysis/gpu-context-<timestamp>.")
    parser.add_argument("--frame", type=int, help=f"Frame index to write into {GPU_FRAME_CONTEXT_FILE}.")
    parser.add_argument("--capture-source", default="RenderDoc", help=f"Capture/profiler source label for {GPU_FRAME_CONTEXT_FILE}.")
    parser.add_argument("--engine", default="Unity", help=f"Engine label for {GPU_FRAME_CONTEXT_FILE} capture metadata.")
    parser.add_argument("--engine-build", default="", help=f"Engine build label for {GPU_FRAME_CONTEXT_FILE} capture metadata.")
    parser.add_argument("--marker-filter", default="Crowd.", help="RenderDoc marker filter for event extraction.")
    parser.add_argument("--exclude-marker", action="append", default=[], help="Marker substring to exclude. Can be repeated.")
    parser.add_argument("--include-draws", action="store_true", help="Include draw calls when fetching RenderDoc events.")
    parser.add_argument("--no-fetch-events", action="store_true", help="Do not call RenderDoc even when --capture is provided.")
    parser.add_argument("--no-pipeline-state", action="store_true", help="Do not fetch shader/resource state from RenderDoc.")
    parser.add_argument("--no-resources", action="store_true", help="Do not include SRV/UAV/CBV resources from RenderDoc.")
    parser.add_argument("--no-timings", action="store_true", help="Do not fetch EventGPUDuration timings from RenderDoc.")
    parser.add_argument("--max-passes", type=int, default=256, help="Maximum RenderDoc pass records to fetch. Use 0 for no limit.")
    parser.add_argument("--event-id-min", type=int, help="Only include RenderDoc events with event_id >= this value.")
    parser.add_argument("--event-id-max", type=int, help="Only include RenderDoc events with event_id <= this value.")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    repo = Path(args.repo).resolve()
    output_dir = Path(args.artifact_dir).resolve() if args.artifact_dir else default_artifact_dir(repo)
    catalog_path = resolve_catalog_path(repo, args)
    generated_at = utc_stamp()

    raw_events: dict[str, Any] = {}
    events_in = Path(args.events_in).resolve() if args.events_in else None
    if events_in is not None:
        raw_events = load_json(events_in)
    elif not args.no_fetch_events:
        capture = resolve_default_inputs(repo, args)
        if capture is not None:
            raw_events = fetch_renderdoc_gpu_passes(repo, capture, args)

    if raw_events:
        write_json(output_dir / RAW_GPU_PASS_EVENTS_FILE, raw_events)

    catalog = normalize_catalog(load_structured_file(catalog_path), generated_at, catalog_path)
    events, _ = normalize_events(raw_events, generated_at, args.frame)
    events = enrich_events_with_catalog(events, catalog)
    resource_views, resource_views_by_event = build_resource_views(events, catalog, generated_at, args.frame,
                                                                    struct_sizes=parse_hlsl_struct_sizes_for_catalog(catalog, repo))
    pass_overview = build_pass_overview(events, catalog, generated_at, args.frame)
    frame_context = build_frame_context(
        args.frame,
        events,
        catalog,
        resource_views_by_event,
        resource_views.get("resource_usages", []),
        generated_at,
        {
            "source": args.capture_source,
            "engine": args.engine,
            "engine_build": args.engine_build,
        },
    )

    write_json(output_dir / GPU_PASS_CATALOG_FILE, catalog)
    write_json(output_dir / GPU_PASS_OVERVIEW_FILE, pass_overview)
    write_json(output_dir / GPU_PASS_RESOURCES_FILE, resource_views)
    write_json(output_dir / GPU_FRAME_CONTEXT_FILE, frame_context)

    print(
        "GPU frame context ok: "
        f"passes={len(frame_context.get('gpu_passes', []))}, "
        f"events={len(pass_overview.get('events', []))}, "
        f"catalog_passes={len(catalog.get('passes', []))}, "
        f"out={output_dir}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
