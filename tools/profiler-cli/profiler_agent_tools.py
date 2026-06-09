#!/usr/bin/env python3
"""Agent-friendly Profiler analysis tools."""

from __future__ import annotations

import argparse
import csv
from datetime import datetime
import json
import os
from pathlib import Path
import re
import shutil
import statistics
import subprocess
import sys
import tempfile
from typing import Any


SESSION_MANIFEST_FILE = "session_manifest.json"
FRAME_INDEX_FILE = "frame_index.csv"
THREAD_INDEX_FILE = "thread_index.csv"
THREAD_OVERVIEW_FILE = "thread_overview.json"
SUPPORTED_THREAD_NAMES = ("Main Thread", "Render Thread")
HIERARCHY_VIEW_CACHE_VERSION = 2


def repo_root_from_here() -> Path:
    return Path(__file__).resolve().parents[2]


def profiler_artifact_root(repo: Path) -> Path:
    return repo / ".workspace" / "artifacts" / "profiler-analysis"


def timestamp() -> str:
    return datetime.now().strftime("%Y%m%d-%H%M%S")


def sanitize_label(value: str) -> str:
    cleaned = []
    for char in value:
        if char.isalnum():
            cleaned.append(char.lower())
        elif char in "-_.":
            cleaned.append(char)
        else:
            cleaned.append("_")
    return "".join(cleaned).strip("_") or "item"


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def print_json(data: Any) -> None:
    print(json.dumps(data, ensure_ascii=False, indent=2))


def load_csv_rows(path: Path) -> list[dict[str, Any]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        return [dict(row) for row in reader]


def parse_number(value: str | None) -> float | None:
    if value is None:
        return None
    stripped = value.strip()
    if not stripped:
        return None
    try:
        return float(stripped)
    except ValueError:
        return None


def parse_int(value: str | None) -> int | None:
    if value is None:
        return None
    stripped = value.strip()
    if not stripped:
        return None
    try:
        return int(float(stripped))
    except ValueError:
        return None


def resolve_session_dir(repo: Path, selector: str) -> Path:
    candidate = Path(selector)
    if candidate.exists():
        if candidate.is_dir():
            manifest = candidate / SESSION_MANIFEST_FILE
            if manifest.exists():
                return candidate.resolve()
            raise FileNotFoundError(f"Profiler session manifest missing under directory: {candidate}")
        if candidate.name == SESSION_MANIFEST_FILE:
            return candidate.resolve().parent
        raise FileNotFoundError(f"Profiler session path is not a directory or manifest: {candidate}")

    root = profiler_artifact_root(repo)
    if not root.exists():
        raise FileNotFoundError("No profiler-analysis artifact root found.")

    exact = root / selector
    if (exact / SESSION_MANIFEST_FILE).exists():
        return exact.resolve()

    for manifest_path in root.rglob(SESSION_MANIFEST_FILE):
        manifest = load_json(manifest_path)
        if manifest.get("sessionId") == selector:
            return manifest_path.parent.resolve()

    raise FileNotFoundError(f"Profiler session not found: {selector}")


def resolve_session_dir_for_view(repo: Path, session_id: str | None, frame_index: int) -> Path:
    if session_id:
        return resolve_session_dir(repo, session_id)

    root = profiler_artifact_root(repo)
    if not root.exists():
        raise FileNotFoundError("No profiler-analysis artifact root found.")

    candidates: list[Path] = []
    for manifest_path in root.rglob(SESSION_MANIFEST_FILE):
        manifest = load_json(manifest_path)
        first_frame = manifest.get("firstFrameIndex")
        last_frame = manifest.get("lastFrameIndex")
        if first_frame is None or last_frame is None:
            continue
        if int(first_frame) <= frame_index <= int(last_frame):
            candidates.append(manifest_path.parent.resolve())

    if len(candidates) == 1:
        return candidates[0]
    if not candidates:
        raise FileNotFoundError(f"No profiler session contains frame {frame_index}. Pass --session-id explicitly.")

    labels = ", ".join(path.name for path in candidates[:8])
    raise ValueError(
        f"Multiple profiler sessions contain frame {frame_index}; pass --session-id explicitly. Candidates: {labels}"
    )


def resolve_session_dir_for_source_path(repo: Path, source_path: Path) -> Path:
    root = profiler_artifact_root(repo)
    if not root.exists():
        raise FileNotFoundError(
            "No profiler-analysis artifact root found. Offline profiler tools require an existing indexed artifact."
        )

    source_resolved = str(source_path.resolve()).lower()
    matches: list[Path] = []
    for manifest_path in root.rglob(SESSION_MANIFEST_FILE):
        manifest = load_json(manifest_path)
        manifest_source = manifest.get("sourcePath")
        if not manifest_source:
            continue
        manifest_path_obj = Path(str(manifest_source))
        manifest_resolved = (
            str(manifest_path_obj.resolve()).lower()
            if manifest_path_obj.exists()
            else str(manifest_source).lower()
        )
        if manifest_resolved == source_resolved:
            matches.append(manifest_path.parent.resolve())

    if matches:
        return max(matches, key=lambda item: item.stat().st_mtime)

    raise FileNotFoundError(
        "Profiler source has no existing offline artifact. Use profiler.session.open --source file to import .raw/.data first. "
        f"source={source_path}"
    )


def detect_project_version(repo: Path) -> str | None:
    project_version = repo / "ProjectSettings" / "ProjectVersion.txt"
    if not project_version.exists():
        return None
    for line in project_version.read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("m_EditorVersion:"):
            return line.split(":", 1)[1].strip()
    return None


def detect_unity_executable(repo: Path, explicit: str | None) -> Path:
    candidates: list[Path] = []
    if explicit:
        candidates.append(Path(explicit))

    env_path = os.environ.get("UNITY_EDITOR_PATH")
    if env_path:
        candidates.append(Path(env_path))

    version = detect_project_version(repo)
    if version:
        candidates.append(Path(r"C:\Program Files\Unity\Hub\Editor") / version / "Editor" / "Unity.exe")
        candidates.append(Path(r"C:\Program Files\Unity\Editor") / version / "Editor" / "Unity.exe")

    for candidate in candidates:
        resolved = candidate.expanduser().resolve()
        if resolved.exists():
            return resolved

    raise FileNotFoundError(
        "Unity.exe not found. Pass --unity or set UNITY_EDITOR_PATH. "
        f"Checked project version {version!r} under common install paths."
    )


def is_unity_project_open_lock(stdout: str, stderr: str) -> bool:
    output = f"{stdout}\n{stderr}".lower()
    return (
        "another unity instance is running with this project open" in output
        or "multiple unity instances cannot open the same project" in output
    )


def format_unity_missing_response_error(
    repo: Path,
    request: dict[str, Any],
    temp_path: Path,
    stdout: str,
    stderr: str,
) -> str:
    if is_unity_project_open_lock(stdout, stderr):
        return (
            "Unity project-open lock: batchmode cannot import this Profiler capture because another Unity instance "
            "has the same project open.\n"
            f"project={repo}\n"
            f"request={request.get('command')}\n"
            "hint=Close the Unity Editor using this project, or pass --repo to a project copy that is not open, then rerun.\n"
            f"temp_files_kept={temp_path}\n"
            f"stdout:\n{stdout}\n\nstderr:\n{stderr}"
        )

    return (
        "Unity profiler CLI did not produce a response file.\n"
        f"request={request.get('command')}\n"
        f"temp_files_kept={temp_path}\n"
        f"stdout:\n{stdout}\n\nstderr:\n{stderr}"
    )


def run_unity_cli(repo: Path, request: dict[str, Any], unity: str | None) -> dict[str, Any]:
    unity_exe = detect_unity_executable(repo, unity)
    workspace_temp = repo / ".workspace"
    workspace_temp.mkdir(parents=True, exist_ok=True)

    temp_path = Path(tempfile.mkdtemp(prefix="profiler_cli_", dir=str(workspace_temp)))
    keep_temp_files = True
    try:
        request_path = temp_path / "request.json"
        response_path = temp_path / "response.json"
        log_path = temp_path / "unity_profiler_cli.log"
        request_path.write_text(json.dumps(request, ensure_ascii=False, indent=2), encoding="utf-8")

        command = [
            str(unity_exe),
            "-batchmode",
            "-quit",
            "-projectPath",
            str(repo),
            "-executeMethod",
            "ProfilerAnalysisCli.Execute",
            "--profiler-cli-request",
            str(request_path),
            "--profiler-cli-response",
            str(response_path),
            "-logFile",
            str(log_path),
        ]
        completed = subprocess.run(command, cwd=str(repo), text=True, capture_output=True)
        stdout = completed.stdout or ""
        stderr = completed.stderr or ""

        if not response_path.exists():
            raise RuntimeError(format_unity_missing_response_error(repo, request, temp_path, stdout, stderr))

        response = load_json(response_path)
        if completed.returncode != 0 or not response.get("success"):
            raise RuntimeError(
                "Unity profiler CLI failed.\n"
                f"request={request.get('command')}\n"
                f"response={json.dumps(response, ensure_ascii=False, indent=2)}\n"
                f"temp_files_kept={temp_path}\n"
                f"stdout:\n{stdout}\n\nstderr:\n{stderr}"
            )

        keep_temp_files = False
        return response
    finally:
        if not keep_temp_files:
            shutil.rmtree(temp_path, ignore_errors=True)


def choose_session_artifact_dir(repo: Path, source_path: Path) -> Path:
    root = profiler_artifact_root(repo)
    root.mkdir(parents=True, exist_ok=True)
    name = sanitize_label(source_path.stem)
    return root / f"{timestamp()}_{name}"


def load_manifest(session_dir: Path) -> dict[str, Any]:
    manifest_path = session_dir / SESSION_MANIFEST_FILE
    if not manifest_path.exists():
        raise FileNotFoundError(f"Session manifest missing: {manifest_path}")
    return load_json(manifest_path)


def load_frame_rows(session_dir: Path) -> list[dict[str, Any]]:
    rows = load_csv_rows(session_dir / FRAME_INDEX_FILE)
    for row in rows:
        row["frame_index"] = parse_int(row.get("frame_index"))
        row["cpu_frame_ms"] = parse_number(row.get("cpu_frame_ms"))
        row["gpu_frame_ms"] = parse_number(row.get("gpu_frame_ms"))
        row["fps"] = parse_number(row.get("fps"))
        row["main_thread_ms"] = parse_number(row.get("main_thread_ms"))
        row["render_thread_ms"] = parse_number(row.get("render_thread_ms"))
        row["draw_calls"] = parse_int(row.get("draw_calls"))
        row["batches"] = parse_int(row.get("batches"))
        row["gc_alloc_bytes"] = parse_int(row.get("gc_alloc_bytes"))
        row["thread_count"] = parse_int(row.get("thread_count"))
    return rows


def load_thread_rows(session_dir: Path) -> list[dict[str, Any]]:
    rows = load_csv_rows(session_dir / THREAD_INDEX_FILE)
    for row in rows:
        row["frame_index"] = parse_int(row.get("frame_index"))
        row["thread_index"] = parse_int(row.get("thread_index"))
        row["thread_id"] = parse_int(row.get("thread_id"))
        row["thread_name"] = str(row.get("thread_name") or "")
        row["thread_group_name"] = str(row.get("thread_group_name") or "")
        row["thread_frame_ms"] = parse_number(row.get("thread_frame_ms"))
        row["sample_count"] = parse_int(row.get("sample_count"))
        row["max_depth"] = parse_int(row.get("max_depth"))
    return rows


def percentile(values: list[float], ratio: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    index = max(0, min(len(ordered) - 1, int(round((len(ordered) - 1) * ratio))))
    return ordered[index]


def filter_frame_rows(rows: list[dict[str, Any]], frame_min: int | None, frame_max: int | None) -> list[dict[str, Any]]:
    filtered = rows
    if frame_min is not None:
        filtered = [row for row in filtered if row["frame_index"] is not None and row["frame_index"] >= frame_min]
    if frame_max is not None:
        filtered = [row for row in filtered if row["frame_index"] is not None and row["frame_index"] <= frame_max]
    return filtered


def resolve_frame_metric(metric: str) -> str:
    aliases = {
        "frame_time_ms": "cpu_frame_ms",
        "cpu_frame_ms": "cpu_frame_ms",
        "gpu_frame_ms": "gpu_frame_ms",
        "main_thread_ms": "main_thread_ms",
        "render_thread_ms": "render_thread_ms",
    }
    normalized = metric.strip().lower()
    if normalized not in aliases:
        raise ValueError(f"Unsupported frame metric for threshold runs: {metric}")
    return aliases[normalized]


def is_over_threshold(value: float | int | None, threshold: float, comparison: str) -> bool:
    if value is None:
        return False
    if comparison == "greater_than":
        return float(value) > threshold
    raise ValueError(f"Unsupported threshold comparison: {comparison}")


def round_float(value: float | None, digits: int = 3) -> float | None:
    if value is None:
        return None
    return round(float(value), digits)


def median_absolute_deviation(values: list[float], median_value: float) -> float:
    if not values:
        return 0.0
    deviations = [abs(value - median_value) for value in values]
    return float(statistics.median(deviations))


def infer_platform_from_source_summary(source_path: str | None) -> str | None:
    if not source_path:
        return None

    source = Path(source_path)
    candidate_paths = [
        source.parent / "poi_run_summary.json",
        source.parent.parent / "poi_run_summary.json",
    ]
    source_resolved = str(source.resolve()).lower() if source.exists() else str(source).lower()

    for candidate in candidate_paths:
        if not candidate.exists():
            continue

        summary = load_json(candidate)
        points = summary.get("points")
        if isinstance(points, list):
            for point in points:
                raw_path = str(point.get("profilerRawPath") or "")
                raw_resolved = str(Path(raw_path).resolve()).lower() if raw_path and Path(raw_path).exists() else raw_path.lower()
                if raw_resolved == source_resolved:
                    platform = point.get("applicationPlatform") or summary.get("applicationPlatform")
                    if platform:
                        return str(platform)

        platform = summary.get("applicationPlatform")
        if platform:
            return str(platform)

    return None


def has_gpu_data(manifest: dict[str, Any]) -> bool:
    modules = {str(module).strip().lower() for module in manifest.get("availableModules", [])}
    return "gpu usage" in modules


def profiler_session_open_response(manifest: dict[str, Any]) -> dict[str, Any]:
    platform = (
        manifest.get("platform")
        or manifest.get("applicationPlatform")
        or infer_platform_from_source_summary(manifest.get("sourcePath"))
        or "unknown"
    )
    return {
        "session_id": manifest.get("sessionId"),
        "frame_count": manifest.get("frameCount"),
        "unity_version": manifest.get("unityVersion") or "unknown",
        "platform": platform,
        "has_cpu_data": "CPU Usage" in set(manifest.get("availableModules", [])),
        "has_gpu_data": has_gpu_data(manifest),
    }


def profiler_session_describe_response(manifest: dict[str, Any]) -> dict[str, Any]:
    observed_threads: set[str] = set()
    for thread in manifest.get("threadOverview", []):
        if not isinstance(thread, dict):
            continue
        thread_name = str(thread.get("threadName") or "").strip()
        if thread_name in SUPPORTED_THREAD_NAMES:
            observed_threads.add(thread_name)

    return {
        "frames": {
            "first": manifest.get("firstFrameIndex"),
            "last": manifest.get("lastFrameIndex"),
            "count": manifest.get("frameCount"),
        },
        "modules": manifest.get("availableModules", []),
        "threads_observed": [name for name in SUPPORTED_THREAD_NAMES if name in observed_threads],
    }


def open_profiler_session(args: argparse.Namespace) -> tuple[Path, dict[str, Any]]:
    repo = Path(args.repo).resolve()
    source = getattr(args, "source", None)
    session_id = getattr(args, "session_id", None) or getattr(args, "run_id", None)
    path_arg = getattr(args, "path", None)

    if source == "connected":
        raise ValueError("profiler.session.open source=connected is not implemented by the local CLI adapter yet.")

    if source == "session" or session_id:
        if not session_id:
            raise SystemExit("profiler.session.open source=session requires --session-id or --run-id.")
        session_dir = resolve_session_dir(repo, session_id)
        manifest = load_manifest(session_dir)
        return session_dir, manifest

    if not path_arg:
        raise SystemExit("profiler.session.open requires --path, --session-id, or --run-id.")

    path = Path(path_arg)
    if path.exists():
        if path.is_dir() or path.name == SESSION_MANIFEST_FILE:
            session_dir = resolve_session_dir(repo, str(path))
            manifest = load_manifest(session_dir)
            return session_dir, manifest

        session_dir = choose_session_artifact_dir(repo, path.resolve())
        request = {
            "command": "open_session",
            "sourcePath": str(path.resolve()),
            "artifactDir": str(session_dir),
            "sessionId": session_dir.name,
        }
        response = run_unity_cli(repo, request, args.unity)
        imported_dir = Path(response.get("artifactDir") or session_dir)
        manifest = load_manifest(imported_dir)
        return imported_dir, manifest

    raise FileNotFoundError(f"Profiler session path not found: {path}")


def command_open_profiler_session(args: argparse.Namespace) -> None:
    _, manifest = open_profiler_session(args)
    print_json(profiler_session_open_response(manifest))


def command_profiler_session_describe(args: argparse.Namespace) -> None:
    repo = Path(args.repo).resolve()
    session_dir = resolve_session_dir(repo, args.session_id)
    manifest = load_manifest(session_dir)
    print_json(profiler_session_describe_response(manifest))


def build_threshold_run_segment(
    segment_index: int,
    rows: list[dict[str, Any]],
    metric: str,
    metric_key: str,
    threshold_ms: float,
    comparison: str,
) -> dict[str, Any]:
    values = [float(row[metric_key]) for row in rows if row.get(metric_key) is not None]
    if not values:
        raise ValueError("Cannot build a threshold run segment without metric values.")

    over_budget_rows = [
        row for row in rows if is_over_threshold(row.get(metric_key), threshold_ms, comparison)
    ]
    median_value = statistics.median(values)
    worst_row = max(rows, key=lambda row: float(row.get(metric_key) or float("-inf")))

    return {
        "segment_id": f"seg_{segment_index:03d}",
        "type": "stable_low_fps_region",
        "start_frame": rows[0]["frame_index"],
        "end_frame": rows[-1]["frame_index"],
        "frame_count": len(rows),
        "duration_ms": round_float(sum(values)),
        "metric": metric,
        "threshold_ms": threshold_ms,
        "over_budget_frames": len(over_budget_rows),
        "over_budget_ratio": round_float(len(over_budget_rows) / len(rows)),
        "stats": {
            "min_ms": round_float(min(values)),
            "max_ms": round_float(max(values)),
            "mean_ms": round_float(statistics.fmean(values)),
            "median_ms": round_float(median_value),
            "p95_ms": round_float(percentile(values, 0.95)),
        },
        "representative_frames": {
            "first": rows[0]["frame_index"],
            "median_frame": rows[len(rows) // 2]["frame_index"],
            "worst": worst_row["frame_index"],
            "last": rows[-1]["frame_index"],
        },
        "severity": {
            "budget_overrun_ms_median": round_float(median_value - threshold_ms),
            "budget_overrun_ratio_median": round_float(median_value / threshold_ms),
        },
    }


def find_threshold_run_segments(
    rows: list[dict[str, Any]],
    metric: str,
    threshold_ms: float,
    comparison: str,
    min_frames: int,
    min_duration_ms: float,
    allowed_gap_frames: int,
    min_over_budget_ratio: float,
) -> list[dict[str, Any]]:
    metric_key = resolve_frame_metric(metric)
    sorted_rows = sorted(
        [row for row in rows if row.get("frame_index") is not None and row.get(metric_key) is not None],
        key=lambda row: row["frame_index"],
    )

    raw_segments: list[list[dict[str, Any]]] = []
    current: list[dict[str, Any]] = []
    pending_gap: list[dict[str, Any]] = []

    for row in sorted_rows:
        if is_over_threshold(row.get(metric_key), threshold_ms, comparison):
            if current:
                current.extend(pending_gap)
                pending_gap = []
            current.append(row)
            continue

        if not current:
            continue

        pending_gap.append(row)
        if len(pending_gap) > allowed_gap_frames:
            raw_segments.append(current)
            current = []
            pending_gap = []

    if current:
        raw_segments.append(current)

    segments: list[dict[str, Any]] = []
    for raw_segment in raw_segments:
        values = [float(row[metric_key]) for row in raw_segment if row.get(metric_key) is not None]
        if not values:
            continue

        over_budget_count = sum(
            1 for row in raw_segment if is_over_threshold(row.get(metric_key), threshold_ms, comparison)
        )
        over_budget_ratio = over_budget_count / len(raw_segment)
        duration_ms = sum(values)

        if len(raw_segment) < min_frames:
            continue
        if duration_ms < min_duration_ms:
            continue
        if over_budget_ratio < min_over_budget_ratio:
            continue

        segments.append(
            build_threshold_run_segment(
                len(segments) + 1,
                raw_segment,
                metric,
                metric_key,
                threshold_ms,
                comparison,
            )
        )

    return segments


def command_profiler_frame_threshold_runs(args: argparse.Namespace) -> None:
    repo = Path(args.repo).resolve()
    session_dir = resolve_session_dir(repo, args.session_id)
    frame_min = args.frame_range[0] if args.frame_range else None
    frame_max = args.frame_range[1] if args.frame_range else None
    rows = filter_frame_rows(load_frame_rows(session_dir), frame_min, frame_max)
    segments = find_threshold_run_segments(
        rows,
        args.metric,
        args.threshold_ms,
        args.comparison,
        args.min_frames,
        args.min_duration_ms,
        args.allowed_gap_frames,
        args.min_over_budget_ratio,
    )
    print_json({"segments": segments})


def build_neighbor_frame(
    row: dict[str, Any] | None,
    metric: str,
    metric_key: str,
) -> dict[str, Any] | None:
    if row is None or row.get(metric_key) is None:
        return None
    return {
        "frame_index": row["frame_index"],
        metric: round_float(float(row[metric_key])),
    }


def build_outlier_spike(
    spike_index: int,
    group_rows: list[dict[str, Any]],
    candidate_data: dict[int, dict[str, Any]],
    row_by_frame: dict[int, dict[str, Any]],
    metric: str,
    metric_key: str,
    min_absolute_ms: float,
) -> dict[str, Any]:
    peak_row = max(group_rows, key=lambda row: float(row.get(metric_key) or float("-inf")))
    peak_frame = int(peak_row["frame_index"])
    peak_data = candidate_data[peak_frame]
    peak_ms = float(peak_row[metric_key])
    baseline_ms = float(peak_data["local_median_ms"])
    local_mad_ms = float(peak_data["local_mad_ms"])
    relative_factor = peak_ms / baseline_ms if baseline_ms > 0.0 else None
    start_frame = int(group_rows[0]["frame_index"])
    end_frame = int(group_rows[-1]["frame_index"])

    flags = ["candidate_hitch"]
    if len(group_rows) <= 2:
        flags.insert(0, "isolated")
    if peak_ms >= min_absolute_ms * 2.0 or (relative_factor is not None and relative_factor >= 4.0):
        flags.insert(-1, "severe")

    return {
        "spike_id": f"spike_{spike_index:03d}",
        "type": "hitch",
        "peak_frame": peak_frame,
        "start_frame": start_frame,
        "end_frame": end_frame,
        "width_frames": len(group_rows),
        "metric": metric,
        "peak_ms": round_float(peak_ms),
        "local_baseline_ms": round_float(baseline_ms),
        "over_baseline_ms": round_float(peak_ms - baseline_ms),
        "relative_factor": round_float(relative_factor),
        "local_median_ms": round_float(baseline_ms),
        "local_mad_ms": round_float(local_mad_ms),
        "context_range": [peak_data["context_start"], peak_data["context_end"]],
        "neighbor_frames": {
            "previous": build_neighbor_frame(row_by_frame.get(start_frame - 1), metric, metric_key),
            "next": build_neighbor_frame(row_by_frame.get(end_frame + 1), metric, metric_key),
        },
        "flags": flags,
    }


def find_frame_outlier_spikes(
    rows: list[dict[str, Any]],
    metric: str,
    method: str,
    window_radius_frames: int,
    min_absolute_ms: float,
    min_relative_factor: float,
    mad_multiplier: float,
    max_width_frames: int,
    merge_distance_frames: int,
) -> list[dict[str, Any]]:
    if method != "rolling_median_mad":
        raise ValueError(f"Unsupported outlier detection method: {method}")

    metric_key = resolve_frame_metric(metric)
    sorted_rows = sorted(
        [row for row in rows if row.get("frame_index") is not None and row.get(metric_key) is not None],
        key=lambda row: row["frame_index"],
    )
    if not sorted_rows:
        return []

    row_by_frame = {int(row["frame_index"]): row for row in sorted_rows}
    frame_indices = [int(row["frame_index"]) for row in sorted_rows]
    first_frame = frame_indices[0]
    last_frame = frame_indices[-1]
    candidate_data: dict[int, dict[str, Any]] = {}
    candidate_frames: list[int] = []

    for row in sorted_rows:
        frame_index = int(row["frame_index"])
        context_start = max(first_frame, frame_index - window_radius_frames)
        context_end = min(last_frame, frame_index + window_radius_frames)
        neighbor_values = [
            float(other[metric_key])
            for other in sorted_rows
            if other["frame_index"] is not None
            and context_start <= int(other["frame_index"]) <= context_end
            and int(other["frame_index"]) != frame_index
            and other.get(metric_key) is not None
        ]
        if not neighbor_values:
            continue

        value = float(row[metric_key])
        local_median = float(statistics.median(neighbor_values))
        local_mad = median_absolute_deviation(neighbor_values, local_median)
        relative_factor = value / local_median if local_median > 0.0 else float("inf")
        mad_gate = value > local_median + mad_multiplier * local_mad if local_mad > 0.0 else value > local_median
        is_candidate = (
            value >= min_absolute_ms
            and relative_factor >= min_relative_factor
            and mad_gate
        )
        if not is_candidate:
            continue

        candidate_frames.append(frame_index)
        candidate_data[frame_index] = {
            "local_median_ms": local_median,
            "local_mad_ms": local_mad,
            "context_start": context_start,
            "context_end": context_end,
        }

    if not candidate_frames:
        return []

    groups: list[list[int]] = []
    current_group = [candidate_frames[0]]
    for frame_index in candidate_frames[1:]:
        if frame_index - current_group[-1] - 1 <= merge_distance_frames:
            current_group.append(frame_index)
        else:
            groups.append(current_group)
            current_group = [frame_index]
    groups.append(current_group)

    spikes: list[dict[str, Any]] = []
    for group in groups:
        start_frame = group[0]
        end_frame = group[-1]
        group_rows = [
            row
            for row in sorted_rows
            if start_frame <= int(row["frame_index"]) <= end_frame
        ]
        if not group_rows or len(group_rows) > max_width_frames:
            continue
        spikes.append(
            build_outlier_spike(
                len(spikes) + 1,
                group_rows,
                candidate_data,
                row_by_frame,
                metric,
                metric_key,
                min_absolute_ms,
            )
        )

    return spikes


def command_profiler_frame_outliers(args: argparse.Namespace) -> None:
    repo = Path(args.repo).resolve()
    session_dir = resolve_session_dir(repo, args.session_id)
    frame_min = args.frame_range[0] if args.frame_range else None
    frame_max = args.frame_range[1] if args.frame_range else None
    rows = filter_frame_rows(load_frame_rows(session_dir), frame_min, frame_max)
    spikes = find_frame_outlier_spikes(
        rows,
        args.metric,
        args.method,
        args.window_radius_frames,
        args.min_absolute_ms,
        args.min_relative_factor,
        args.mad_multiplier,
        args.max_width_frames,
        args.merge_distance_frames,
    )
    print_json({"spikes": spikes})


def normalize_thread_group(thread_name: str, thread_group: str) -> str:
    if thread_name == "Main Thread":
        return thread_group or "Main"
    if thread_name == "Render Thread":
        return thread_group or "Render"
    return thread_group


def parse_hierarchy_view_id(view_id: str) -> tuple[int, str, str]:
    match = re.fullmatch(r"hview_(\d+)_([^_]+)_(.+)", view_id.strip())
    if not match:
        raise ValueError(f"Invalid hierarchy view_id: {view_id}")
    frame_index = int(match.group(1))
    thread_selector = match.group(2)
    view = match.group(3)
    return frame_index, thread_selector, view


def format_hierarchy_child(row: dict[str, Any]) -> dict[str, Any]:
    return {
        "item_id": row.get("item_id", row.get("itemId")),
        "name": row.get("name"),
        **({"path": row.get("path")} if row.get("path") is not None else {}),
        "total_ms": round_float(row.get("total_ms", row.get("totalMs"))),
        "self_ms": round_float(row.get("self_ms", row.get("selfMs"))),
        "calls": row.get("calls"),
        "gc_alloc_bytes": row.get("gc_alloc_bytes", row.get("gcAllocBytes")),
        "children_count": row.get("children_count", row.get("childrenCount", 1 if row.get("hasChildren") else 0)),
    }


def format_hierarchy_ancestor(row: dict[str, Any]) -> dict[str, Any]:
    return {
        "item_id": row.get("item_id", row.get("itemId")),
        "name": row.get("name"),
    }


def hierarchy_view_cache_path(session_dir: Path, view_id: str) -> Path:
    safe_view_id = sanitize_label(view_id)
    cache_dir = session_dir / "queries"
    cache_dir.mkdir(parents=True, exist_ok=True)
    return cache_dir / f"{safe_view_id}_stable_tree.json"


def normalize_hierarchy_rows(view_id: str, rows: list[dict[str, Any]]) -> dict[str, Any]:
    nodes: list[dict[str, Any]] = []
    stack: list[int] = []

    for source_row in rows:
        name = str(source_row.get("name") or "")
        path = str(source_row.get("path") or "")
        depth = int(source_row.get("depth") if source_row.get("depth") is not None else -1)
        if depth < 0 or (not name and not path):
            continue

        while len(stack) > depth:
            stack.pop()
        parent_id = stack[depth - 1] if depth > 0 and len(stack) >= depth else None
        item_id = len(nodes) + 1
        node = {
            "item_id": item_id,
            "source_item_id": source_row.get("itemId"),
            "parent_id": parent_id,
            "name": name,
            "path": path,
            "total_ms": source_row.get("totalMs"),
            "self_ms": source_row.get("selfMs"),
            "calls": source_row.get("calls"),
            "gc_alloc_bytes": source_row.get("gcAllocBytes"),
            "children_count": 0,
        }
        nodes.append(node)
        if len(stack) == depth:
            stack.append(item_id)
        else:
            stack[depth] = item_id

    child_counts: dict[int, int] = {}
    for node in nodes:
        parent_id = node.get("parent_id")
        if parent_id is not None:
            child_counts[parent_id] = child_counts.get(parent_id, 0) + 1
    for node in nodes:
        node["children_count"] = child_counts.get(node["item_id"], 0)

    return {"schema_version": HIERARCHY_VIEW_CACHE_VERSION, "view_id": view_id, "nodes": nodes}


def is_hierarchy_view_cache_current(cache: dict[str, Any]) -> bool:
    return cache.get("schema_version") == HIERARCHY_VIEW_CACHE_VERSION


def load_hierarchy_view_cache(session_dir: Path, view_id: str) -> dict[str, Any]:
    cache_path = hierarchy_view_cache_path(session_dir, view_id)
    if not cache_path.exists():
        raise FileNotFoundError(
            "Hierarchy stable tree cache is missing. "
            f"Expected cache: {cache_path}"
        )

    cache = load_json(cache_path)
    if not is_hierarchy_view_cache_current(cache):
        raise ValueError(
            "Hierarchy stable tree cache is outdated. "
            f"Expected schema_version={HIERARCHY_VIEW_CACHE_VERSION}, cache={cache_path}"
        )
    return cache


def load_or_create_hierarchy_view_cache(
    repo: Path,
    session_dir: Path,
    manifest: dict[str, Any],
    view_id: str,
    frame_index: int,
    thread_selector: str,
    view: str,
    unity: str | None,
) -> dict[str, Any]:
    try:
        return load_hierarchy_view_cache(session_dir, view_id)
    except (FileNotFoundError, ValueError):
        pass

    request = {
        "command": "export_hierarchy_view",
        "sourcePath": manifest["sourcePath"],
        "artifactDir": str(session_dir),
        "sessionId": manifest["sessionId"],
        "frameIndex": frame_index,
        "thread": thread_selector,
        "view": view,
        "topN": 0,
    }
    response = run_unity_cli(repo, request, unity)
    output_path = response.get("outputPath")
    if not output_path:
        raise RuntimeError("Unity profiler CLI did not return outputPath for command export_hierarchy_view.")

    payload = load_json(Path(output_path))
    rows = payload.get("rows")
    if not isinstance(rows, list):
        rows = []
    cache = normalize_hierarchy_rows(view_id, rows)
    hierarchy_view_cache_path(session_dir, view_id).write_text(
        json.dumps(cache, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return cache


def sort_hierarchy_nodes(nodes: list[dict[str, Any]], sort_by: str, order: str) -> list[dict[str, Any]]:
    key_map = {
        "total_ms": "total_ms",
        "total": "total_ms",
        "self_ms": "self_ms",
        "self": "self_ms",
        "calls": "calls",
        "gc_alloc_bytes": "gc_alloc_bytes",
        "gc": "gc_alloc_bytes",
        "children_count": "children_count",
    }
    key_name = key_map.get(sort_by.strip().lower(), "total_ms")
    descending = order.strip().lower() != "asc"

    def node_key(node: dict[str, Any]) -> tuple[float, str]:
        value = float(node.get(key_name) or 0.0)
        return ((-value if descending else value), str(node.get("name") or ""))

    return sorted(
        nodes,
        key=node_key,
    )


def infer_hierarchy_category(node: dict[str, Any]) -> str:
    text = f"{node.get('name') or ''} {node.get('path') or ''}".lower()
    if "gc.alloc" in text or "garbagecollector" in text:
        return "GC"
    if "physics" in text:
        return "Physics"
    if "wait" in text or "present" in text:
        return "Wait"
    if "render" in text or "gfx" in text or "camera" in text or "urp" in text:
        return "Rendering"
    if "script" in text or "assembly-csharp" in text or "behaviour" in text or "[invoke]" in text:
        return "Scripts"
    return "Other"


def node_matches_query(node: dict[str, Any], query: str | None) -> bool:
    if not query:
        return True
    pattern = query.strip().lower()
    if not pattern:
        return True
    if pattern.endswith("*"):
        prefix = pattern[:-1]
        if "/" in prefix or "\\" in prefix:
            return (node.get("path") or "").lower().startswith(prefix)
        return (node.get("name") or "").lower().startswith(prefix)
    return pattern in (node.get("name") or "").lower() or pattern in (node.get("path") or "").lower()


def node_passes_numeric_filters(node: dict[str, Any], args: argparse.Namespace) -> bool:
    checks = [
        ("total_ms", args.min_total_ms),
        ("self_ms", args.min_self_ms),
        ("gc_alloc_bytes", args.min_gc_alloc_bytes),
        ("calls", args.min_calls),
    ]
    for key, minimum in checks:
        if minimum is None:
            continue
        value = node.get(key)
        if value is None or float(value) < float(minimum):
            return False
    return True


def command_profiler_hierarchy_ancestors(args: argparse.Namespace) -> None:
    repo = Path(args.repo).resolve()
    frame_index, thread_selector, view = parse_hierarchy_view_id(args.view_id)
    session_dir = resolve_session_dir_for_view(repo, args.session_id, frame_index)
    manifest = load_manifest(session_dir)
    cache = load_or_create_hierarchy_view_cache(
        repo,
        session_dir,
        manifest,
        args.view_id,
        frame_index,
        thread_selector,
        view,
        args.unity,
    )

    nodes = [node for node in cache.get("nodes", []) if isinstance(node, dict)]
    nodes_by_id = {int(node["item_id"]): node for node in nodes if node.get("item_id") is not None}
    if args.item_id not in nodes_by_id:
        raise ValueError(f"Hierarchy item_id not found in {args.view_id}: {args.item_id}")

    ancestors = []
    visited = set()
    node = nodes_by_id[args.item_id]
    while node is not None:
        item_id = node.get("item_id")
        if item_id in visited:
            raise ValueError(f"Cycle detected while resolving ancestors for item_id {args.item_id}")
        visited.add(item_id)
        ancestors.append(node)

        parent_id = node.get("parent_id")
        node = nodes_by_id.get(int(parent_id)) if parent_id is not None else None

    ancestors.reverse()
    print_json({"ancestors": [format_hierarchy_ancestor(row) for row in ancestors]})


def command_profiler_hierarchy_search(args: argparse.Namespace) -> None:
    repo = Path(args.repo).resolve()
    frame_index, thread_selector, view = parse_hierarchy_view_id(args.view_id)
    session_dir = resolve_session_dir_for_view(repo, args.session_id, frame_index)
    manifest = load_manifest(session_dir)
    cache = load_or_create_hierarchy_view_cache(
        repo,
        session_dir,
        manifest,
        args.view_id,
        frame_index,
        thread_selector,
        view,
        args.unity,
    )

    category_filter = args.category.strip().lower() if args.category else None
    matched = []
    for node in cache.get("nodes", []):
        if not isinstance(node, dict):
            continue
        if not node_matches_query(node, args.query):
            continue
        if category_filter and infer_hierarchy_category(node).lower() != category_filter:
            continue
        if not node_passes_numeric_filters(node, args):
            continue
        matched.append(node)

    matched = sort_hierarchy_nodes(matched, args.sort_by, args.order)[: max(1, args.limit)]
    items = []
    for node in matched:
        item = format_hierarchy_child(node)
        item.pop("children_count", None)
        items.append(item)
    print_json({"items": items})


def command_profiler_hierarchy_children(args: argparse.Namespace) -> None:
    repo = Path(args.repo).resolve()
    frame_index, thread_selector, view = parse_hierarchy_view_id(args.view_id)
    session_dir = resolve_session_dir_for_view(repo, args.session_id, frame_index)
    manifest = load_manifest(session_dir)
    cache = load_or_create_hierarchy_view_cache(
        repo,
        session_dir,
        manifest,
        args.view_id,
        frame_index,
        thread_selector,
        view,
        args.unity,
    )
    nodes = cache.get("nodes", [])
    children = [
        node for node in nodes
        if isinstance(node, dict) and node.get("parent_id") == args.item_id
    ]
    children = sort_hierarchy_nodes(children, args.sort_by, args.order)[: max(1, args.limit)]
    print_json({"children": [format_hierarchy_child(row) for row in children]})


def command_profiler_thread_list(args: argparse.Namespace) -> None:
    repo = Path(args.repo).resolve()
    session_dir = resolve_session_dir(repo, args.session_id)
    rows = [
        row
        for row in load_thread_rows(session_dir)
        if row.get("frame_index") == args.frame_index
        and row.get("thread_name") in SUPPORTED_THREAD_NAMES
    ]
    rows.sort(key=lambda row: SUPPORTED_THREAD_NAMES.index(row["thread_name"]))

    print_json(
        {
            "threads": [
                {
                    "thread_index": row.get("thread_index"),
                    "thread_name": row.get("thread_name"),
                    "thread_group": normalize_thread_group(
                        str(row.get("thread_name") or ""),
                        str(row.get("thread_group_name") or ""),
                    ),
                    "sample_count": row.get("sample_count"),
                    "max_depth": row.get("max_depth"),
                    "frame_time_ms": round_float(row.get("thread_frame_ms")),
                }
                for row in rows
            ]
        }
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Profiler analysis agent tools.")
    parser.add_argument("--repo", default=str(repo_root_from_here()), help="Unity repository root.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    open_parsers = [
        subparsers.add_parser("profiler.session.open", help="Open or import a profiler session."),
        subparsers.add_parser("open_profiler_session", help="Compatibility alias for profiler.session.open."),
    ]
    for open_parser in open_parsers:
        open_parser.add_argument("--source", choices=["file", "connected", "artifact", "session"], help="Profiler source kind.")
        open_parser.add_argument("--path", help="Profiler source path (.raw/.data) or existing session directory.")
        open_parser.add_argument("--session-id", help="Existing profiler session id.")
        open_parser.add_argument("--run-id", help="Compatibility alias for --session-id.")
        open_parser.add_argument("--unity", help="Unity.exe path override.")
        open_parser.set_defaults(func=command_open_profiler_session)

    describe_parser = subparsers.add_parser("profiler.session.describe", help="Describe available data in a profiler session.")
    describe_parser.add_argument("--session-id", required=True)
    describe_parser.set_defaults(func=command_profiler_session_describe)

    threshold_parser = subparsers.add_parser("profiler.frame.threshold_runs", help="Find stable frame ranges above a time budget.")
    threshold_parser.add_argument("--session-id", required=True)
    threshold_parser.add_argument("--metric", default="frame_time_ms")
    threshold_parser.add_argument("--range", dest="frame_range", nargs=2, type=int, metavar=("START", "END"))
    threshold_parser.add_argument("--threshold-ms", required=True, type=float)
    threshold_parser.add_argument("--comparison", choices=["greater_than"], default="greater_than")
    threshold_parser.add_argument("--min-frames", type=int, default=30)
    threshold_parser.add_argument("--min-duration-ms", type=float, default=500.0)
    threshold_parser.add_argument("--allowed-gap-frames", type=int, default=2)
    threshold_parser.add_argument("--min-over-budget-ratio", type=float, default=0.85)
    threshold_parser.set_defaults(func=command_profiler_frame_threshold_runs)

    outliers_parser = subparsers.add_parser("profiler.frame.outliers", help="Find isolated frame hitches above a local baseline.")
    outliers_parser.add_argument("--session-id", required=True)
    outliers_parser.add_argument("--metric", default="frame_time_ms")
    outliers_parser.add_argument("--range", dest="frame_range", nargs=2, type=int, metavar=("START", "END"))
    outliers_parser.add_argument("--method", choices=["rolling_median_mad"], default="rolling_median_mad")
    outliers_parser.add_argument("--window-radius-frames", type=int, default=30)
    outliers_parser.add_argument("--min-absolute-ms", type=float, default=33.33)
    outliers_parser.add_argument("--min-relative-factor", type=float, default=2.0)
    outliers_parser.add_argument("--mad-multiplier", type=float, default=6.0)
    outliers_parser.add_argument("--max-width-frames", type=int, default=5)
    outliers_parser.add_argument("--merge-distance-frames", type=int, default=3)
    outliers_parser.set_defaults(func=command_profiler_frame_outliers)

    thread_list_parser = subparsers.add_parser("profiler.thread.list", help="List supported threads for a profiler frame.")
    thread_list_parser.add_argument("--session-id", required=True)
    thread_list_parser.add_argument("--frame-index", required=True, type=int)
    thread_list_parser.set_defaults(func=command_profiler_thread_list)

    hierarchy_children_parser = subparsers.add_parser("profiler.hierarchy.children", help="Read direct children of a hierarchy item.")
    hierarchy_children_parser.add_argument("--session-id", help="Existing profiler session id. Optional only when view_id resolves uniquely.")
    hierarchy_children_parser.add_argument("--view-id", required=True)
    hierarchy_children_parser.add_argument("--item-id", required=True, type=int)
    hierarchy_children_parser.add_argument("--sort-by", default="total_ms")
    hierarchy_children_parser.add_argument("--order", choices=["asc", "desc"], default="desc")
    hierarchy_children_parser.add_argument("--limit", type=int, default=20)
    hierarchy_children_parser.add_argument("--unity", help="Unity.exe path override.")
    hierarchy_children_parser.set_defaults(func=command_profiler_hierarchy_children)

    hierarchy_ancestors_parser = subparsers.add_parser("profiler.hierarchy.ancestors", help="Read the ancestor path for a hierarchy item.")
    hierarchy_ancestors_parser.add_argument("--session-id", help="Existing profiler session id. Optional only when view_id resolves uniquely.")
    hierarchy_ancestors_parser.add_argument("--view-id", required=True)
    hierarchy_ancestors_parser.add_argument("--item-id", required=True, type=int)
    hierarchy_ancestors_parser.add_argument("--unity", help="Unity.exe path override.")
    hierarchy_ancestors_parser.set_defaults(func=command_profiler_hierarchy_ancestors)

    hierarchy_search_parser = subparsers.add_parser("profiler.hierarchy.search", help="Search hierarchy nodes by query and thresholds.")
    hierarchy_search_parser.add_argument("--session-id", help="Existing profiler session id. Optional only when view_id resolves uniquely.")
    hierarchy_search_parser.add_argument("--view-id", required=True)
    hierarchy_search_parser.add_argument("--query", default="")
    hierarchy_search_parser.add_argument("--category")
    hierarchy_search_parser.add_argument("--min-total-ms", type=float)
    hierarchy_search_parser.add_argument("--min-self-ms", type=float)
    hierarchy_search_parser.add_argument("--min-gc-alloc-bytes", type=float)
    hierarchy_search_parser.add_argument("--min-calls", type=float)
    hierarchy_search_parser.add_argument("--sort-by", default="total_ms")
    hierarchy_search_parser.add_argument("--order", choices=["asc", "desc"], default="desc")
    hierarchy_search_parser.add_argument("--limit", type=int, default=50)
    hierarchy_search_parser.add_argument("--unity", help="Unity.exe path override.")
    hierarchy_search_parser.set_defaults(func=command_profiler_hierarchy_search)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    try:
        args.func(args)
    except (RuntimeError, FileNotFoundError, ValueError, OSError) as exc:
        print(
            json.dumps(
                {
                    "success": False,
                    "error_type": type(exc).__name__,
                    "error": str(exc),
                },
                ensure_ascii=False,
                indent=2,
            ),
            file=sys.stderr,
        )
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
