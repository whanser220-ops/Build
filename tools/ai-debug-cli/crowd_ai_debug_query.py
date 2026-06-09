#!/usr/bin/env python3
"""Query Crowd AI Debug JSONL recordings as composable investigation probes.

The tool is intentionally read-only. It does not decide the next query for the
LLM; it exposes small state views and raw evidence slices so the caller can
choose what to inspect next.
"""

from __future__ import annotations

import argparse
import collections
import json
import sys
from pathlib import Path
from typing import Any, Iterable


CONTROL_KEYS = {"_line", "f", "t", "dt", "p", "id", "ph"}

IMPORTANT_FIELD_ORDER = [
    "found",
    "recorded",
    "capacity",
    "reason",
    "reasonMask",
    "gpu.frame",
    "slot",
    "iter",
    "pos",
    "localPos",
    "yaw",
    "vel",
    "localVel",
    "speed",
    "scale",
    "physicsActiveRaw",
    "physicsActive",
    "dead",
    "deathRaw",
    "health",
    "grid.cellKey",
    "grid.occ",
    "grid.overflow",
    "target",
    "hasTarget",
    "los",
    "fired",
    "dist",
    "cooldown",
    "combat.flags",
    "acq.target",
    "acq.flags",
    "reject.fov",
    "reject.range",
    "reject.terrain",
    "reject.envSdf",
    "reject.maxCandidates",
]

STATE_FIELD_ORDER = [
    "pos",
    "localPos",
    "vel",
    "localVel",
    "speed",
    "physicsActive",
    "physicsActiveRaw",
    "dead",
    "health",
    "grid.cellKey",
    "grid.occ",
    "grid.overflow",
    "target",
    "hasTarget",
    "los",
    "fired",
    "cooldown",
    "acq.target",
    "acq.flags",
]

FRAME_FIELD_ORDER = [
    "sys.instance",
    "sys.squad",
    "sys.assignment",
    "sys.formationSlot",
    "sys.aliveGpu",
    "sys.aliveDispatchGroups",
    "sys.physicsActiveGpu",
    "sys.physicsActiveDispatchGroups",
    "sys.combatActiveGpu",
    "sys.combatActiveDispatchGroups",
    "sys.visibleGpu",
    "query.count",
    "query.max",
    "query.maxHits",
    "grid.dim",
    "grid.cellSize",
    "grid.maxOcc",
    "gpu.solve.iter",
    "gpu.combat",
    "gpu.approxCollision",
    "gpu.localAvoid",
    "gpu.terrainCollision",
    "gpu.staticSdf",
    "gpu.envDistance",
    "gpu.runtimeSquad",
    "render.visible",
    "render.hasBounds",
]

PROVIDER_ROLES = {
    "session.begin": "recording scope, issue statement, discovery target",
    "session.end": "recording closeout and dropped-frame count",
    "frame.dispatch": "system-wide counts, dispatch groups, feature toggles",
    "gpu.resource": "GPU buffer binding/count/stride evidence quality",
    "gpu.discovery": "candidate instance set selected for evidence capture",
    "gpu.stage": "GPU pipeline stage snapshots for candidate instances",
    "gpu.phase": "GPU frame-end instance state for CPU/GPU comparison",
    "agent.state": "CPU/debug-layer instance state",
    "spatial.grid": "spatial grid cell/occupant/overflow state",
    "combat.query": "targeting, LOS, firing, hit, and combat state",
    "squad.intent": "upstream squad command and tactical intent",
    "animation.vat": "VAT animation state",
    "visibility.render": "render visibility and LOD state",
}

DOMAIN_MAP = [
    (
        "session",
        ["session.begin", "session.end"],
        "Defines the capture window, issue statement, and discovery target.",
        "none",
    ),
    (
        "frame",
        ["frame.dispatch"],
        "Explains system-wide workset counts and feature toggles per frame.",
        "session",
    ),
    (
        "resources",
        ["gpu.resource"],
        "Checks whether debug/runtime buffers were bound with plausible counts.",
        "frame",
    ),
    (
        "discovery",
        ["gpu.discovery"],
        "Selects which runtime instances became evidence candidates.",
        "resources + target config",
    ),
    (
        "gpu_stages",
        ["gpu.stage", "gpu.phase"],
        "Locates instance state changes after GPU phases.",
        "discovery candidates",
    ),
    (
        "domain_streams",
        [
            "agent.state",
            "squad.intent",
            "combat.query",
            "spatial.grid",
            "animation.vat",
            "visibility.render",
        ],
        "Optional domain-specific streams for CPU, combat, grid, animation, and rendering.",
        "target instances",
    ),
]


def repo_root_from_here() -> Path:
    return Path(__file__).resolve().parents[2]


def print_json(data: Any) -> None:
    print(json.dumps(data, ensure_ascii=False, indent=2))


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def format_value(value: Any, max_len: int = 96) -> str:
    if value is None:
        text = ""
    elif isinstance(value, float):
        text = f"{value:.4f}".rstrip("0").rstrip(".")
    elif isinstance(value, (str, int, bool)):
        text = str(value)
    else:
        text = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    if len(text) > max_len:
        return text[: max_len - 3] + "..."
    return text


def as_text(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, (str, int, float, bool)):
        return str(value)
    return json.dumps(value, ensure_ascii=False, sort_keys=True)


def frame_span(frames: Iterable[Any]) -> str:
    values = sorted({int(frame) for frame in frames if isinstance(frame, int)})
    if not values:
        return "-"
    if len(values) == 1:
        return str(values[0])
    return f"{values[0]}-{values[-1]} ({len(values)})"


def value_set_summary(values: Iterable[Any], max_items: int = 4) -> str:
    unique = []
    seen = set()
    for value in values:
        key = json.dumps(value, ensure_ascii=False, sort_keys=True) if not isinstance(value, (str, int, float, bool, type(None))) else str(value)
        if key in seen:
            continue
        seen.add(key)
        unique.append(format_value(value, 48))
    if not unique:
        return "-"
    if len(unique) > max_items:
        return ", ".join(unique[:max_items]) + f", +{len(unique) - max_items}"
    return ", ".join(unique)


def print_table(headers: list[str], rows: list[list[Any]]) -> None:
    if not rows:
        print("(none)")
        return

    text_rows = [[format_value(cell) for cell in row] for row in rows]
    widths = [len(header) for header in headers]
    for row in text_rows:
        for index, cell in enumerate(row):
            widths[index] = max(widths[index], len(cell))

    print(" | ".join(header.ljust(widths[index]) for index, header in enumerate(headers)))
    print(" | ".join("-" * width for width in widths))
    for row in text_rows:
        print(" | ".join(cell.ljust(widths[index]) for index, cell in enumerate(row)))


def latest_ai_debug_jsonl(repo: Path) -> Path | None:
    root = repo / ".workspace" / "artifacts" / "ai-debug"
    if not root.exists():
        return None
    candidates = [path for path in root.glob("crowd_ai_debug_*.jsonl") if path.is_file()]
    candidates.sort(key=lambda path: (path.stat().st_mtime, str(path)), reverse=True)
    return candidates[0] if candidates else None


def latest_repro_trace(repo: Path) -> Path | None:
    root = repo / ".workspace" / "artifacts" / "ai-debug" / "repro-traces"
    if not root.exists():
        return None
    candidates = [path for path in root.glob("crowd_ai_repro_*.json") if path.is_file()]
    candidates.sort(key=lambda path: (path.stat().st_mtime, str(path)), reverse=True)
    return candidates[0] if candidates else None


def resolve_input(args: argparse.Namespace) -> Path:
    repo = Path(args.repo).resolve()
    if args.input:
        path = Path(args.input).resolve()
    else:
        path = latest_ai_debug_jsonl(repo)
        if path is None:
            raise FileNotFoundError("No AI Debug JSONL recording found under .workspace/artifacts/ai-debug.")
    if not path.exists():
        raise FileNotFoundError(f"AI Debug JSONL not found: {path}")
    return path


def resolve_profile(args: argparse.Namespace) -> Path | None:
    if args.profile:
        path = Path(args.profile).resolve()
        return path if path.exists() else None
    path = Path(args.repo).resolve() / ".workspace" / "artifacts" / "ai-debug" / "external-player-profile.json"
    return path if path.exists() else None


def resolve_repro_trace(args: argparse.Namespace) -> Path | None:
    if args.repro_trace:
        path = Path(args.repro_trace).resolve()
        return path if path.exists() else None
    return latest_repro_trace(Path(args.repo).resolve())


def pair_markdown_path(input_path: Path) -> Path | None:
    path = input_path.with_suffix(".md")
    return path if path.exists() else None


def load_records(path: Path) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8-sig") as handle:
        for line_number, line in enumerate(handle, start=1):
            text = line.strip()
            if not text:
                continue
            try:
                record = json.loads(text)
            except json.JSONDecodeError as exc:
                raise ValueError(f"Invalid JSONL at {path}:{line_number}: {exc}") from exc
            if isinstance(record, dict):
                record["_line"] = line_number
                records.append(record)
    return records


def summarize_records(records: list[dict[str, Any]]) -> dict[str, Any]:
    provider_counts: collections.Counter[str] = collections.Counter()
    provider_frames: dict[str, set[int]] = collections.defaultdict(set)
    provider_ids: dict[str, set[Any]] = collections.defaultdict(set)
    provider_phases: dict[str, set[str]] = collections.defaultdict(set)
    frames: set[int] = set()
    times: list[float] = []
    ids: collections.Counter[Any] = collections.Counter()
    phases: collections.Counter[str] = collections.Counter()

    for record in records:
        provider = str(record.get("p", ""))
        provider_counts[provider] += 1
        frame = record.get("f")
        if isinstance(frame, int):
            frames.add(frame)
            provider_frames[provider].add(frame)
        time = record.get("t")
        if isinstance(time, (int, float)):
            times.append(float(time))
        instance_id = record.get("id")
        if instance_id is not None:
            ids[instance_id] += 1
            provider_ids[provider].add(instance_id)
        phase = record.get("ph")
        if isinstance(phase, str):
            phases[phase] += 1
            provider_phases[provider].add(phase)

    session_begin = next((record for record in records if record.get("p") == "session.begin"), None)
    session_end = next((record for record in reversed(records) if record.get("p") == "session.end"), None)
    return {
        "record_count": len(records),
        "frames": sorted(frames),
        "time_start": min(times) if times else None,
        "time_end": max(times) if times else None,
        "provider_counts": provider_counts,
        "provider_frames": provider_frames,
        "provider_ids": provider_ids,
        "provider_phases": provider_phases,
        "ids": ids,
        "phases": phases,
        "session_begin": session_begin,
        "session_end": session_end,
    }


def interesting_fields(record: dict[str, Any], field_order: list[str] | None = None, max_fields: int = 12) -> str:
    order = field_order or IMPORTANT_FIELD_ORDER
    fields: list[str] = []
    used = set(CONTROL_KEYS)

    for key in order:
        if key in record:
            fields.append(f"{key}={format_value(record[key], 64)}")
            used.add(key)
        if len(fields) >= max_fields:
            return "; ".join(fields)

    for key in sorted(record.keys()):
        if key in used or key.startswith("_"):
            continue
        fields.append(f"{key}={format_value(record[key], 64)}")
        if len(fields) >= max_fields:
            break
    return "; ".join(fields)


def selected_state_fields(state: dict[str, Any], max_fields: int = 8) -> str:
    fields = []
    for key in STATE_FIELD_ORDER:
        if key in state:
            fields.append(f"{key}={format_value(state[key], 48)}")
        if len(fields) >= max_fields:
            break
    return "; ".join(fields) if fields else "-"


def filter_records(
    records: list[dict[str, Any]],
    provider: str | None = None,
    frame: int | None = None,
    instance_id: str | None = None,
    phase: str | None = None,
    around: int = 0,
) -> list[dict[str, Any]]:
    result = records
    if provider:
        result = [record for record in result if str(record.get("p", "")).lower() == provider.lower()]
    if frame is not None:
        start = frame - around
        end = frame + around
        result = [record for record in result if isinstance(record.get("f"), int) and start <= int(record["f"]) <= end]
    if instance_id is not None:
        result = [record for record in result if str(record.get("id", "")) == str(instance_id)]
    if phase:
        result = [record for record in result if str(record.get("ph", "")).lower() == phase.lower()]
    return result


def collect_resource_rows(records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    resources: dict[str, dict[str, Any]] = {}
    for record in records:
        if record.get("p") != "gpu.resource":
            continue
        frame = record.get("f")
        for key, value in record.items():
            suffix = None
            for candidate in (".bound", ".count", ".stride"):
                if key.endswith(candidate):
                    suffix = candidate[1:]
                    break
            if suffix is None:
                continue
            name = key[: -(len(suffix) + 1)]
            entry = resources.setdefault(
                name,
                {"resource": name, "frames": set(), "bound": [], "count": [], "stride": [], "lines": []},
            )
            if isinstance(frame, int):
                entry["frames"].add(frame)
            entry[suffix].append(value)
            entry["lines"].append(record.get("_line"))

    rows = []
    for entry in resources.values():
        rows.append(
            {
                "resource": entry["resource"],
                "frames": sorted(entry["frames"]),
                "bound": entry["bound"],
                "count": entry["count"],
                "stride": entry["stride"],
                "lines": entry["lines"],
            }
        )
    rows.sort(key=lambda row: (False not in row["bound"], row["resource"]))
    return rows


def collect_signals(records: list[dict[str, Any]], summary: dict[str, Any]) -> list[dict[str, Any]]:
    signals: list[dict[str, Any]] = []

    for record in records:
        if record.get("p") != "gpu.discovery" or "found" not in record:
            continue
        found = record.get("found")
        recorded = record.get("recorded")
        capacity = record.get("capacity")
        if isinstance(found, int) and isinstance(recorded, int) and isinstance(capacity, int):
            if found > recorded and recorded == capacity:
                signals.append(
                    {
                        "kind": "candidate_pool_saturated",
                        "frame": record.get("f"),
                        "line": record.get("_line"),
                        "evidence": f"found={found}; recorded={recorded}; capacity={capacity}",
                    }
                )

    for row in collect_resource_rows(records):
        if False in row["bound"]:
            signals.append(
                {
                    "kind": "resource_unbound",
                    "frame": frame_span(row["frames"]),
                    "line": value_set_summary(row["lines"], 3),
                    "evidence": f"{row['resource']}.bound includes false",
                }
            )

    frames = summary["frames"]
    if len(frames) <= 3:
        signals.append(
            {
                "kind": "short_recording_window",
                "frame": frame_span(frames),
                "line": "-",
                "evidence": f"only {len(frames)} recorded frame(s)",
            }
        )

    present = set(summary["provider_counts"].keys())
    for provider in ["agent.state", "combat.query", "spatial.grid", "squad.intent", "animation.vat", "visibility.render"]:
        if provider not in present:
            signals.append(
                {
                    "kind": "provider_absent",
                    "frame": "-",
                    "line": "-",
                    "evidence": f"{provider} not present in this recording",
                }
            )

    session_end = summary.get("session_end")
    if (
        session_end
        and isinstance(session_end.get("recordCount"), int)
        and session_end["recordCount"] not in {summary["record_count"], summary["record_count"] - 1}
    ):
        signals.append(
            {
                "kind": "record_count_mismatch",
                "frame": session_end.get("f"),
                "line": session_end.get("_line"),
                "evidence": f"session.end.recordCount={session_end['recordCount']}; parsed={summary['record_count']}",
            }
        )

    return signals


def base_context(args: argparse.Namespace) -> tuple[Path, list[dict[str, Any]], dict[str, Any]]:
    input_path = resolve_input(args)
    records = load_records(input_path)
    return input_path, records, summarize_records(records)


def emit_or_print(args: argparse.Namespace, data: dict[str, Any], text_fn) -> None:
    if args.format == "json":
        print_json(data)
    else:
        text_fn(data)


def cmd_session_map(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    md_path = pair_markdown_path(input_path)
    profile_path = resolve_profile(args)
    repro_path = resolve_repro_trace(args)
    session_begin = summary["session_begin"] or {}

    provider_rows = []
    for provider, count in summary["provider_counts"].most_common():
        provider_rows.append(
            {
                "provider": provider,
                "count": count,
                "frames": frame_span(summary["provider_frames"][provider]),
                "ids": len(summary["provider_ids"][provider]),
                "phases": value_set_summary(sorted(summary["provider_phases"][provider])),
                "role": PROVIDER_ROLES.get(provider, "unclassified stream"),
            }
        )

    domain_rows = []
    present = set(summary["provider_counts"].keys())
    for domain, providers, role, depends_on in DOMAIN_MAP:
        domain_rows.append(
            {
                "domain": domain,
                "providers": ", ".join(providers),
                "present": ", ".join(provider for provider in providers if provider in present) or "-",
                "missing": ", ".join(provider for provider in providers if provider not in present) or "-",
                "depends_on": depends_on,
                "role": role,
            }
        )

    top_entities = []
    for instance_id, count in summary["ids"].most_common(args.limit):
        entity_records = [record for record in records if record.get("id") == instance_id]
        top_entities.append(
            {
                "id": instance_id,
                "records": count,
                "frames": frame_span(record.get("f") for record in entity_records),
                "providers": value_set_summary(record.get("p") for record in entity_records),
                "phases": value_set_summary(record.get("ph") for record in entity_records if record.get("ph")),
            }
        )

    data = {
        "source": str(input_path),
        "sidecars": {
            "markdown": str(md_path) if md_path else None,
            "profile": str(profile_path) if profile_path else None,
            "repro_trace": str(repro_path) if repro_path else None,
        },
        "session": {
            "records": summary["record_count"],
            "frames": frame_span(summary["frames"]),
            "time_start": summary["time_start"],
            "time_end": summary["time_end"],
            "phenomenon": session_begin.get("phenomenon"),
            "repro": session_begin.get("repro"),
            "expected": session_begin.get("expected"),
        },
        "discovery_target": {
            key: session_begin.get(key)
            for key in [
                "targetCount",
                "discovery.mode",
                "discovery.capacity",
                "discovery.center",
                "discovery.radius",
                "discovery.boxCenter",
                "discovery.boxExtents",
                "discovery.screenRect01",
                "discovery.hasWorldToClip",
                "discovery.squad",
            ]
            if key in session_begin
        },
        "domains": domain_rows,
        "providers": provider_rows,
        "top_entities": top_entities,
        "signals": collect_signals(records, summary)[: args.signal_limit],
    }

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_runtime_map")
        print(f"source: {data['source']}")
        print(f"markdown: {data['sidecars']['markdown'] or '-'}")
        print(f"profile: {data['sidecars']['profile'] or '-'}")
        print(f"repro_trace: {data['sidecars']['repro_trace'] or '-'}")
        print()
        print("session")
        for key, value in data["session"].items():
            print(f"  {key}: {format_value(value)}")
        print()
        print("discovery_target")
        if data["discovery_target"]:
            for key, value in data["discovery_target"].items():
                print(f"  {key}: {format_value(value)}")
        else:
            print("  -")
        print()
        print("runtime_domains")
        print_table(
            ["domain", "present", "missing", "depends_on", "role"],
            [[row["domain"], row["present"], row["missing"], row["depends_on"], row["role"]] for row in data["domains"]],
        )
        print()
        print("providers")
        print_table(
            ["provider", "count", "frames", "ids", "phases", "role"],
            [[row["provider"], row["count"], row["frames"], row["ids"], row["phases"], row["role"]] for row in data["providers"]],
        )
        print()
        print("top_entities")
        print_table(
            ["id", "records", "frames", "providers", "phases"],
            [[row["id"], row["records"], row["frames"], row["providers"], row["phases"]] for row in data["top_entities"]],
        )
        print()
        print("evidence_signals")
        print_table(
            ["kind", "frame", "line", "evidence"],
            [[row["kind"], row["frame"], row["line"], row["evidence"]] for row in data["signals"]],
        )

    emit_or_print(args, data, text)


def cmd_session_overview(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    md_path = pair_markdown_path(input_path)
    session_begin = summary["session_begin"] or {}
    session_end = summary["session_end"] or {}
    frame_records = [record for record in records if record.get("p") == "frame.dispatch"]

    data = {
        "source": str(input_path),
        "markdown": str(md_path) if md_path else None,
        "records": summary["record_count"],
        "frames": frame_span(summary["frames"]),
        "time_start": summary["time_start"],
        "time_end": summary["time_end"],
        "session_begin": {key: value for key, value in session_begin.items() if key not in {"_line"}},
        "session_end": {key: value for key, value in session_end.items() if key not in {"_line"}},
        "provider_counts": dict(summary["provider_counts"]),
        "frame_dispatch": frame_records,
    }

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_session_overview")
        print(f"source: {data['source']}")
        print(f"markdown: {data['markdown'] or '-'}")
        print(f"records: {data['records']}")
        print(f"frames: {data['frames']}")
        print(f"time: {format_value(data['time_start'])} -> {format_value(data['time_end'])}")
        if data["session_begin"]:
            print(f"phenomenon: {format_value(data['session_begin'].get('phenomenon'))}")
            print(f"repro: {format_value(data['session_begin'].get('repro'))}")
            print(f"expected: {format_value(data['session_begin'].get('expected'))}")
        print()
        print("provider_counts")
        print_table(["provider", "count"], [[key, value] for key, value in summary["provider_counts"].most_common()])
        print()
        print("frame_dispatch")
        print_table(
            ["line", "frame", "time", "fields"],
            [
                [record.get("_line"), record.get("f"), record.get("t"), interesting_fields(record, FRAME_FIELD_ORDER, 14)]
                for record in frame_records[: args.limit]
            ],
        )
        if len(frame_records) > args.limit:
            print(f"... {len(frame_records) - args.limit} more frame.dispatch records")

    emit_or_print(args, data, text)


def cmd_discovery_overview(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    discovery = [record for record in records if record.get("p") == "gpu.discovery"]
    global_rows = [record for record in discovery if "found" in record or "recorded" in record or "capacity" in record]
    candidate_rows = [record for record in discovery if "id" in record]
    reasons = collections.Counter(str(record.get("reason")) for record in candidate_rows if record.get("reason") is not None)
    id_counts = collections.Counter(record.get("id") for record in candidate_rows if record.get("id") is not None)

    candidates = []
    for instance_id, count in id_counts.most_common(args.limit):
        items = [record for record in candidate_rows if record.get("id") == instance_id]
        first = items[0]
        last = items[-1]
        candidates.append(
            {
                "id": instance_id,
                "records": count,
                "frames": frame_span(record.get("f") for record in items),
                "slot": value_set_summary(record.get("slot") for record in items if "slot" in record),
                "reason": value_set_summary(record.get("reason") for record in items if record.get("reason") is not None),
                "first": interesting_fields(first, IMPORTANT_FIELD_ORDER, 8),
                "last": interesting_fields(last, IMPORTANT_FIELD_ORDER, 8),
            }
        )

    saturated = []
    for record in global_rows:
        found = record.get("found")
        recorded = record.get("recorded")
        capacity = record.get("capacity")
        if isinstance(found, int) and isinstance(recorded, int) and isinstance(capacity, int):
            if found > recorded and recorded == capacity:
                saturated.append(record)

    data = {
        "source": str(input_path),
        "summary": {
            "records": len(discovery),
            "global_records": len(global_rows),
            "candidate_records": len(candidate_rows),
            "candidate_ids": len(id_counts),
            "frames": frame_span(record.get("f") for record in discovery),
            "pool_saturated_frames": [record.get("f") for record in saturated],
        },
        "global_records": global_rows,
        "reason_counts": dict(reasons),
        "top_candidates": candidates,
    }

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_discovery_overview")
        print(f"source: {data['source']}")
        for key, value in data["summary"].items():
            print(f"{key}: {format_value(value)}")
        print()
        print("global_discovery_records")
        print_table(
            ["line", "frame", "found", "recorded", "capacity", "status"],
            [
                [
                    record.get("_line"),
                    record.get("f"),
                    record.get("found"),
                    record.get("recorded"),
                    record.get("capacity"),
                    "saturated"
                    if isinstance(record.get("found"), int)
                    and isinstance(record.get("recorded"), int)
                    and isinstance(record.get("capacity"), int)
                    and record["found"] > record["recorded"] == record["capacity"]
                    else "ok",
                ]
                for record in data["global_records"]
            ],
        )
        print()
        print("reason_counts")
        print_table(["reason", "count"], [[key, value] for key, value in reasons.most_common()])
        print()
        print("top_candidates")
        print_table(
            ["id", "records", "frames", "slot", "reason", "first_fields"],
            [[row["id"], row["records"], row["frames"], row["slot"], row["reason"], row["first"]] for row in data["top_candidates"]],
        )

    emit_or_print(args, data, text)


def cmd_stage_phases(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    stage_records = filter_records(records, provider="gpu.stage", instance_id=args.id, frame=args.frame, phase=args.phase)
    phase_counts: dict[str, int] = collections.Counter(str(record.get("ph", "-")) for record in stage_records)
    field_counts: dict[str, collections.Counter[str]] = collections.defaultdict(collections.Counter)
    phase_frames: dict[str, set[int]] = collections.defaultdict(set)
    phase_ids: dict[str, set[Any]] = collections.defaultdict(set)

    for record in stage_records:
        phase = str(record.get("ph", "-"))
        frame = record.get("f")
        if isinstance(frame, int):
            phase_frames[phase].add(frame)
        if record.get("id") is not None:
            phase_ids[phase].add(record.get("id"))
        for key in record:
            if key not in CONTROL_KEYS and not key.startswith("_"):
                field_counts[phase][key] += 1

    rows = []
    for phase, count in phase_counts.most_common():
        rows.append(
            {
                "phase": phase,
                "count": count,
                "frames": frame_span(phase_frames[phase]),
                "ids": len(phase_ids[phase]),
                "top_fields": ", ".join(f"{key}:{value}" for key, value in field_counts[phase].most_common(8)),
            }
        )

    samples = [
        {
            "line": record.get("_line"),
            "frame": record.get("f"),
            "time": record.get("t"),
            "id": record.get("id"),
            "phase": record.get("ph"),
            "fields": interesting_fields(record, IMPORTANT_FIELD_ORDER, 10),
        }
        for record in stage_records[: args.limit]
    ]

    data = {"source": str(input_path), "filters": {"id": args.id, "frame": args.frame, "phase": args.phase}, "phases": rows, "samples": samples}

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_stage_phases")
        print(f"source: {data['source']}")
        print(f"filters: id={args.id or '-'} frame={args.frame if args.frame is not None else '-'} phase={args.phase or '-'}")
        print()
        print("phase_breakdown")
        print_table(
            ["phase", "count", "frames", "ids", "top_fields"],
            [[row["phase"], row["count"], row["frames"], row["ids"], row["top_fields"]] for row in data["phases"]],
        )
        print()
        print("samples")
        print_table(
            ["line", "frame", "time", "id", "phase", "fields"],
            [[row["line"], row["frame"], row["time"], row["id"], row["phase"], row["fields"]] for row in data["samples"]],
        )
        if len(stage_records) > args.limit:
            print(f"... {len(stage_records) - args.limit} more gpu.stage records")

    emit_or_print(args, data, text)


def cmd_agent_timeline(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    matches = filter_records(records, instance_id=args.id, provider=args.provider, frame=args.frame, phase=args.phase, around=args.around)
    matches.sort(key=lambda record: (record.get("f", -1), record.get("t", 0.0), record.get("_line", 0)))
    state: dict[str, Any] = {}
    rows = []

    for record in matches:
        for key, value in record.items():
            if key in CONTROL_KEYS or key.startswith("_"):
                continue
            state[key] = value
        rows.append(
            {
                "line": record.get("_line"),
                "frame": record.get("f"),
                "time": record.get("t"),
                "provider": record.get("p"),
                "phase": record.get("ph"),
                "changed": interesting_fields(record, IMPORTANT_FIELD_ORDER, args.fields),
                "state": selected_state_fields(state, args.state_fields),
            }
        )

    data = {
        "source": str(input_path),
        "id": args.id,
        "filters": {"provider": args.provider, "frame": args.frame, "around": args.around, "phase": args.phase},
        "records": rows[: args.limit],
        "total_matches": len(rows),
    }

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_agent_timeline")
        print(f"source: {data['source']}")
        print(f"id: {data['id']}")
        print(
            f"filters: provider={args.provider or '-'} frame={args.frame if args.frame is not None else '-'} "
            f"around={args.around} phase={args.phase or '-'}"
        )
        print(f"matches: {data['total_matches']}")
        print()
        print_table(
            ["line", "frame", "time", "provider", "phase", "changed_fields", "running_state"],
            [
                [row["line"], row["frame"], row["time"], row["provider"], row["phase"], row["changed"], row["state"]]
                for row in data["records"]
            ],
        )
        if data["total_matches"] > args.limit:
            print(f"... {data['total_matches'] - args.limit} more records")

    emit_or_print(args, data, text)


def cmd_frame_inspect(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    matches = filter_records(records, frame=args.frame, around=args.around)
    provider_counts = collections.Counter(str(record.get("p", "")) for record in matches)
    id_counts = collections.Counter(record.get("id") for record in matches if record.get("id") is not None)
    phase_counts = collections.Counter(str(record.get("ph")) for record in matches if record.get("ph") is not None)
    frame_dispatch = [record for record in matches if record.get("p") == "frame.dispatch"]
    discovery_global = [record for record in matches if record.get("p") == "gpu.discovery" and ("found" in record or "recorded" in record)]
    resource_rows = collect_resource_rows(matches)
    unbound = [row for row in resource_rows if False in row["bound"]]

    samples = [
        {
            "line": record.get("_line"),
            "frame": record.get("f"),
            "time": record.get("t"),
            "provider": record.get("p"),
            "id": record.get("id"),
            "phase": record.get("ph"),
            "fields": interesting_fields(record, IMPORTANT_FIELD_ORDER, 8),
        }
        for record in matches[: args.limit]
    ]

    data = {
        "source": str(input_path),
        "frame": args.frame,
        "around": args.around,
        "records": len(matches),
        "provider_counts": dict(provider_counts),
        "id_counts": dict(id_counts.most_common(args.limit)),
        "phase_counts": dict(phase_counts),
        "frame_dispatch": frame_dispatch,
        "discovery_global": discovery_global,
        "unbound_resources": unbound,
        "samples": samples,
    }

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_frame_inspect")
        print(f"source: {data['source']}")
        print(f"frame: {args.frame} around={args.around}")
        print(f"records: {data['records']}")
        print()
        print("providers")
        print_table(["provider", "count"], [[key, value] for key, value in provider_counts.most_common()])
        print()
        print("frame_dispatch")
        print_table(
            ["line", "frame", "time", "fields"],
            [[record.get("_line"), record.get("f"), record.get("t"), interesting_fields(record, FRAME_FIELD_ORDER, 14)] for record in frame_dispatch],
        )
        print()
        print("discovery_global")
        print_table(
            ["line", "frame", "found", "recorded", "capacity"],
            [[record.get("_line"), record.get("f"), record.get("found"), record.get("recorded"), record.get("capacity")] for record in discovery_global],
        )
        print()
        print("phase_counts")
        print_table(["phase", "count"], [[key, value] for key, value in phase_counts.most_common()])
        print()
        print("top_ids")
        print_table(["id", "count"], [[key, value] for key, value in id_counts.most_common(args.limit)])
        print()
        print("unbound_resources")
        print_table(
            ["resource", "frames", "bound", "count", "stride", "lines"],
            [
                [
                    row["resource"],
                    frame_span(row["frames"]),
                    value_set_summary(row["bound"]),
                    value_set_summary(row["count"]),
                    value_set_summary(row["stride"]),
                    value_set_summary(row["lines"], 3),
                ]
                for row in unbound
            ],
        )
        print()
        print("samples")
        print_table(
            ["line", "frame", "time", "provider", "id", "phase", "fields"],
            [[row["line"], row["frame"], row["time"], row["provider"], row["id"], row["phase"], row["fields"]] for row in data["samples"]],
        )
        if len(matches) > args.limit:
            print(f"... {len(matches) - args.limit} more records")

    emit_or_print(args, data, text)


def cmd_provider_records(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    matches = filter_records(records, provider=args.name, frame=args.frame, instance_id=args.id, phase=args.phase, around=args.around)
    if args.contains:
        needle = args.contains.lower()
        matches = [record for record in matches if needle in as_text(record).lower()]
    rows = [
        {
            "line": record.get("_line"),
            "frame": record.get("f"),
            "time": record.get("t"),
            "id": record.get("id"),
            "phase": record.get("ph"),
            "fields": interesting_fields(record, IMPORTANT_FIELD_ORDER, args.fields),
        }
        for record in matches[: args.limit]
    ]
    data = {
        "source": str(input_path),
        "provider": args.name,
        "filters": {"frame": args.frame, "around": args.around, "id": args.id, "phase": args.phase, "contains": args.contains},
        "total_matches": len(matches),
        "records": rows,
    }

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_provider_records")
        print(f"source: {data['source']}")
        print(f"provider: {args.name}")
        print(
            f"filters: frame={args.frame if args.frame is not None else '-'} around={args.around} "
            f"id={args.id or '-'} phase={args.phase or '-'} contains={args.contains or '-'}"
        )
        print(f"matches: {data['total_matches']}")
        print()
        print_table(
            ["line", "frame", "time", "id", "phase", "fields"],
            [[row["line"], row["frame"], row["time"], row["id"], row["phase"], row["fields"]] for row in data["records"]],
        )
        if data["total_matches"] > args.limit:
            print(f"... {data['total_matches'] - args.limit} more records")

    emit_or_print(args, data, text)


def cmd_resource_overview(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    rows = collect_resource_rows(records)
    if args.query:
        query = args.query.lower()
        rows = [row for row in rows if query in row["resource"].lower()]
    if args.unbound:
        rows = [row for row in rows if False in row["bound"]]

    data_rows = [
        {
            "resource": row["resource"],
            "frames": frame_span(row["frames"]),
            "bound": value_set_summary(row["bound"]),
            "count": value_set_summary(row["count"]),
            "stride": value_set_summary(row["stride"]),
            "lines": value_set_summary(row["lines"], 3),
        }
        for row in rows[: args.limit]
    ]
    data = {
        "source": str(input_path),
        "filters": {"query": args.query, "unbound": args.unbound},
        "total_resources": len(rows),
        "resources": data_rows,
    }

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_resource_overview")
        print(f"source: {data['source']}")
        print(f"filters: query={args.query or '-'} unbound={args.unbound}")
        print(f"resources: {data['total_resources']}")
        print()
        print_table(
            ["resource", "frames", "bound", "count", "stride", "lines"],
            [[row["resource"], row["frames"], row["bound"], row["count"], row["stride"], row["lines"]] for row in data["resources"]],
        )
        if data["total_resources"] > args.limit:
            print(f"... {data['total_resources'] - args.limit} more resources")

    emit_or_print(args, data, text)


def cmd_repro_overview(args: argparse.Namespace) -> None:
    path = resolve_repro_trace(args)
    if path is None:
        raise FileNotFoundError("No Crowd AI Debug repro trace found under .workspace/artifacts/ai-debug/repro-traces.")
    data = load_json(path)
    pose_samples = data.get("poseSamples") if isinstance(data.get("poseSamples"), list) else []
    command_events = data.get("commandEvents") if isinstance(data.get("commandEvents"), list) else []

    command_rows = []
    for event in command_events[: args.limit]:
        point = event.get("worldPoint") if isinstance(event, dict) else None
        command_rows.append(
            {
                "time": event.get("time") if isinstance(event, dict) else None,
                "type": event.get("type") if isinstance(event, dict) else None,
                "squad": event.get("squadIndex") if isinstance(event, dict) else None,
                "command": event.get("commandType") if isinstance(event, dict) else None,
                "updateFacing": event.get("updateFacing") if isinstance(event, dict) else None,
                "worldPoint": point,
            }
        )

    result = {
        "source": str(path),
        "version": data.get("version"),
        "sessionName": data.get("sessionName"),
        "scenePath": data.get("scenePath"),
        "initialSelectedSquadIndex": data.get("initialSelectedSquadIndex"),
        "duration": data.get("duration"),
        "poseSamples": len(pose_samples),
        "commandEvents": len(command_events),
        "firstPose": pose_samples[0] if pose_samples else None,
        "lastPose": pose_samples[-1] if pose_samples else None,
        "commands": command_rows,
    }

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_repro_overview")
        for key in ["source", "version", "sessionName", "scenePath", "initialSelectedSquadIndex", "duration", "poseSamples", "commandEvents"]:
            print(f"{key}: {format_value(data.get(key))}")
        print()
        print("pose_range")
        print(f"firstPose: {format_value(data.get('firstPose'), 180)}")
        print(f"lastPose: {format_value(data.get('lastPose'), 180)}")
        print()
        print("command_events")
        print_table(
            ["time", "type", "squad", "command", "updateFacing", "worldPoint"],
            [[row["time"], row["type"], row["squad"], row["command"], row["updateFacing"], row["worldPoint"]] for row in data["commands"]],
        )
        if data["commandEvents"] > args.limit:
            print(f"... {data['commandEvents'] - args.limit} more command events")

    emit_or_print(args, result, text)


def cmd_raw_show(args: argparse.Namespace) -> None:
    input_path, records, summary = base_context(args)
    matches = filter_records(records, provider=args.provider, frame=args.frame, instance_id=args.id, phase=args.phase, around=args.around)
    if args.contains:
        needle = args.contains.lower()
        matches = [record for record in matches if needle in as_text(record).lower()]
    limited = matches[: args.limit]
    data = {
        "source": str(input_path),
        "filters": {"provider": args.provider, "frame": args.frame, "around": args.around, "id": args.id, "phase": args.phase, "contains": args.contains},
        "total_matches": len(matches),
        "records": limited,
    }

    def text(data: dict[str, Any]) -> None:
        print(f"source: {data['source']}")
        print(
            f"filters: provider={args.provider or '-'} frame={args.frame if args.frame is not None else '-'} "
            f"around={args.around} id={args.id or '-'} phase={args.phase or '-'} contains={args.contains or '-'}"
        )
        print(f"matches: {data['total_matches']}")
        print()
        for record in data["records"]:
            print(json.dumps(record, ensure_ascii=False, separators=(",", ":")))
        if data["total_matches"] > args.limit:
            print(f"... {data['total_matches'] - args.limit} more records")

    emit_or_print(args, data, text)


def cmd_raw_grep(args: argparse.Namespace) -> None:
    input_path = resolve_input(args)
    needle = args.query if args.case_sensitive else args.query.lower()
    matches = []
    with input_path.open("r", encoding="utf-8-sig") as handle:
        for line_number, line in enumerate(handle, start=1):
            haystack = line if args.case_sensitive else line.lower()
            if needle in haystack:
                matches.append({"line": line_number, "text": line.rstrip("\n")})
                if len(matches) >= args.limit:
                    break
    data = {"source": str(input_path), "query": args.query, "case_sensitive": args.case_sensitive, "matches": matches}

    def text(data: dict[str, Any]) -> None:
        print("ai_debug_raw_grep")
        print(f"source: {data['source']}")
        print(f"query: {args.query}")
        print(f"case_sensitive: {args.case_sensitive}")
        print()
        for item in data["matches"]:
            print(f"{item['line']}: {item['text']}")

    emit_or_print(args, data, text)


def add_common_limits(parser: argparse.ArgumentParser, default: int = 20) -> None:
    parser.add_argument("--limit", type=int, default=default, help="Maximum rows to print.")


def add_record_filters(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--frame", type=int, help="Filter to a frame.")
    parser.add_argument("--around", type=int, default=0, help="Include +/- N frames around --frame.")
    parser.add_argument("--id", help="Filter to an instance/squad id.")
    parser.add_argument("--phase", help="Filter to a gpu.stage ph value.")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=str(repo_root_from_here()), help="Repository root. Defaults to the current tool's repo.")
    parser.add_argument("--input", help="AI Debug JSONL file. Defaults to latest .workspace/artifacts/ai-debug/crowd_ai_debug_*.jsonl.")
    parser.add_argument("--profile", help="AI Debug external-player-profile.json sidecar.")
    parser.add_argument("--repro-trace", help="Crowd AI Debug repro trace JSON sidecar.")
    parser.add_argument("--format", choices=["text", "json"], default="text", help="Output format.")

    subparsers = parser.add_subparsers(dest="area", required=True)

    session_parser = subparsers.add_parser("session", help="Session-level state views.")
    session_sub = session_parser.add_subparsers(dest="command", required=True)
    session_map = session_sub.add_parser("map", help="Layer 0 cognitive map of AI Debug runtime evidence.")
    add_common_limits(session_map, 12)
    session_map.add_argument("--signal-limit", type=int, default=20, help="Maximum evidence signals to print.")
    session_map.set_defaults(func=cmd_session_map)
    session_overview = session_sub.add_parser("overview", help="Layer 1 recording overview.")
    add_common_limits(session_overview, 20)
    session_overview.set_defaults(func=cmd_session_overview)

    runtime_parser = subparsers.add_parser("runtime", help="Alias for session-level runtime map.")
    runtime_sub = runtime_parser.add_subparsers(dest="command", required=True)
    runtime_map = runtime_sub.add_parser("map", help="Alias of session map.")
    add_common_limits(runtime_map, 12)
    runtime_map.add_argument("--signal-limit", type=int, default=20, help="Maximum evidence signals to print.")
    runtime_map.set_defaults(func=cmd_session_map)

    discovery_parser = subparsers.add_parser("discovery", help="GPU discovery candidate queries.")
    discovery_sub = discovery_parser.add_subparsers(dest="command", required=True)
    discovery_overview = discovery_sub.add_parser("overview", help="Candidate pool and discovered instances.")
    add_common_limits(discovery_overview, 20)
    discovery_overview.set_defaults(func=cmd_discovery_overview)

    stage_parser = subparsers.add_parser("stage", help="GPU stage snapshot queries.")
    stage_sub = stage_parser.add_subparsers(dest="command", required=True)
    stage_phases = stage_sub.add_parser("phases", help="Break down gpu.stage records by ph.")
    add_record_filters(stage_phases)
    add_common_limits(stage_phases, 20)
    stage_phases.set_defaults(func=cmd_stage_phases)

    agent_parser = subparsers.add_parser("agent", help="Per-instance timeline queries.")
    agent_sub = agent_parser.add_subparsers(dest="command", required=True)
    agent_timeline = agent_sub.add_parser("timeline", help="Sparse timeline for one runtime instance id.")
    add_record_filters(agent_timeline)
    agent_timeline.add_argument("--provider", help="Optional provider filter.")
    agent_timeline.add_argument("--fields", type=int, default=10, help="Changed fields shown per row.")
    agent_timeline.add_argument("--state-fields", type=int, default=8, help="Running state fields shown per row.")
    add_common_limits(agent_timeline, 40)
    agent_timeline.set_defaults(func=cmd_agent_timeline)

    frame_parser = subparsers.add_parser("frame", help="Frame-local evidence queries.")
    frame_sub = frame_parser.add_subparsers(dest="command", required=True)
    frame_inspect = frame_sub.add_parser("inspect", help="Inspect providers, ids, and samples in a frame.")
    frame_inspect.add_argument("--frame", type=int, required=True, help="Frame to inspect.")
    frame_inspect.add_argument("--around", type=int, default=0, help="Include +/- N frames.")
    add_common_limits(frame_inspect, 24)
    frame_inspect.set_defaults(func=cmd_frame_inspect)

    provider_parser = subparsers.add_parser("provider", help="Provider-local records.")
    provider_sub = provider_parser.add_subparsers(dest="command", required=True)
    provider_records = provider_sub.add_parser("records", help="List records from one provider.")
    provider_records.add_argument("--name", required=True, help="Provider name, e.g. gpu.stage.")
    add_record_filters(provider_records)
    provider_records.add_argument("--contains", help="Case-insensitive substring filter over raw record JSON.")
    provider_records.add_argument("--fields", type=int, default=12, help="Fields shown per row.")
    add_common_limits(provider_records, 40)
    provider_records.set_defaults(func=cmd_provider_records)

    resource_parser = subparsers.add_parser("resource", help="GPU resource binding state.")
    resource_sub = resource_parser.add_subparsers(dest="command", required=True)
    resource_overview = resource_sub.add_parser("overview", help="Summarize gpu.resource bound/count/stride records.")
    resource_overview.add_argument("--query", help="Filter resource name.")
    resource_overview.add_argument("--unbound", action="store_true", help="Only show resources with bound=false.")
    add_common_limits(resource_overview, 80)
    resource_overview.set_defaults(func=cmd_resource_overview)

    resources_parser = subparsers.add_parser("resources", help="Alias for resource queries.")
    resources_sub = resources_parser.add_subparsers(dest="command", required=True)
    resources_overview = resources_sub.add_parser("overview", help="Alias of resource overview.")
    resources_overview.add_argument("--query", help="Filter resource name.")
    resources_overview.add_argument("--unbound", action="store_true", help="Only show resources with bound=false.")
    add_common_limits(resources_overview, 80)
    resources_overview.set_defaults(func=cmd_resource_overview)

    repro_parser = subparsers.add_parser("repro", help="Repro trace sidecar queries.")
    repro_sub = repro_parser.add_subparsers(dest="command", required=True)
    repro_overview = repro_sub.add_parser("overview", help="Summarize latest or selected repro trace.")
    add_common_limits(repro_overview, 20)
    repro_overview.set_defaults(func=cmd_repro_overview)

    raw_parser = subparsers.add_parser("raw", help="Raw evidence commands for hypothesis verification.")
    raw_sub = raw_parser.add_subparsers(dest="command", required=True)
    raw_show = raw_sub.add_parser("show", help="Print matching raw JSONL records.")
    add_record_filters(raw_show)
    raw_show.add_argument("--provider", help="Provider filter.")
    raw_show.add_argument("--contains", help="Case-insensitive substring filter over raw record JSON.")
    add_common_limits(raw_show, 20)
    raw_show.set_defaults(func=cmd_raw_show)

    raw_grep = raw_sub.add_parser("grep", help="Grep raw JSONL lines.")
    raw_grep.add_argument("query", help="Substring to search for.")
    raw_grep.add_argument("--case-sensitive", action="store_true", help="Use case-sensitive matching.")
    add_common_limits(raw_grep, 40)
    raw_grep.set_defaults(func=cmd_raw_grep)

    return parser


def normalize_global_options(argv: list[str]) -> list[str]:
    global_options = {"--repo", "--input", "--profile", "--repro-trace", "--format"}
    moved: list[str] = []
    remaining: list[str] = []
    index = 0
    while index < len(argv):
        token = argv[index]
        matched_equals = next((option for option in global_options if token.startswith(option + "=")), None)
        if matched_equals:
            moved.append(token)
            index += 1
        elif token in global_options and index + 1 < len(argv):
            moved.extend([token, argv[index + 1]])
            index += 2
        else:
            remaining.append(token)
            index += 1
    return moved + remaining


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    normalized = normalize_global_options(list(sys.argv[1:] if argv is None else argv))
    args = parser.parse_args(normalized)
    try:
        args.func(args)
    except (FileNotFoundError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
