#!/usr/bin/env python3
"""Materialize a RenderDoc capture into the fact DB and import prefab facts.

This is the orchestration entry point for the post-capture workflow:

1. Export/import the selected .rdc into the SQLite fact database.
2. Import unity_prefab_signatures.sqlite into the same capture row.

The heavy .rdc read still lives in rdc_capture_fact_import.py. This script keeps
the Agent/UI path to one command so prefab-name queries can run immediately.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
import sys
from typing import Any


class PostprocessError(Exception):
    def __init__(self, code: str, message: str, *, recoverable: bool = True, detail: Any = None) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.recoverable = recoverable
        self.detail = detail


def repo_root_from_here() -> Path:
    return Path(__file__).resolve().parents[2]


def default_capture_root() -> Path:
    return repo_root_from_here() / ".workspace" / "artifacts" / "renderdoc-captures"


def default_fact_db_path() -> Path:
    return repo_root_from_here() / ".workspace" / "artifacts" / "renderdoc-analysis" / "rdc-fact-database" / "rdc_capture_four_table.sqlite"


def timestamp() -> str:
    return datetime.now().strftime("%Y%m%d-%H%M%S-%f")


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def read_subprocess_json(completed: subprocess.CompletedProcess[str], command_name: str) -> dict[str, Any]:
    stdout = completed.stdout or ""
    try:
        return json.loads(stdout)
    except json.JSONDecodeError as exc:
        raise PostprocessError(
            "invalid_subprocess_json",
            f"{command_name} did not return valid JSON.",
            detail={"stdout": stdout[-4000:], "stderr": (completed.stderr or "")[-4000:]},
        ) from exc


def run_json_command(command: list[str], *, cwd: Path, timeout: int, command_name: str) -> dict[str, Any]:
    try:
        completed = subprocess.run(
            command,
            cwd=str(cwd),
            text=True,
            capture_output=True,
            timeout=timeout,
        )
    except subprocess.TimeoutExpired as exc:
        raise PostprocessError(
            "subprocess_timeout",
            f"{command_name} timed out after {timeout} seconds.",
            detail={"command": command, "stdout": exc.stdout, "stderr": exc.stderr},
        ) from exc

    if completed.returncode != 0:
        payload: dict[str, Any] | None = None
        if completed.stdout:
            try:
                payload = json.loads(completed.stdout)
            except json.JSONDecodeError:
                payload = None
        raise PostprocessError(
            "subprocess_failed",
            f"{command_name} failed with exit code {completed.returncode}.",
            detail={
                "command": command,
                "payload": payload,
                "stdout": (completed.stdout or "")[-4000:],
                "stderr": (completed.stderr or "")[-4000:],
            },
        )

    payload = read_subprocess_json(completed, command_name)
    return payload


def find_latest_capture(root: Path) -> Path:
    if not root.exists():
        raise PostprocessError("capture_root_not_found", f"RenderDoc capture root not found: {root}")

    captures = [path for path in root.rglob("*.rdc") if path.is_file()]
    if not captures:
        raise PostprocessError("capture_not_found", f"No .rdc capture was found under: {root}")

    captures.sort(key=lambda path: (path.stat().st_mtime, str(path).lower()))
    return captures[-1].resolve()


def resolve_capture(args: argparse.Namespace) -> Path:
    if args.capture:
        capture = Path(args.capture).resolve()
        if not capture.exists():
            raise PostprocessError("capture_not_found", f"Capture not found: {capture}")
        if capture.suffix.lower() != ".rdc":
            raise PostprocessError("capture_not_rdc", f"Expected a .rdc capture: {capture}")
        return capture

    if args.artifact_dir:
        artifact_dir = Path(args.artifact_dir).resolve()
        return find_latest_capture(artifact_dir)

    root = Path(args.artifacts_root).resolve() if args.artifacts_root else default_capture_root()
    return find_latest_capture(root)


def infer_frame_index(capture: Path) -> int | None:
    match = re.search(r"frame[_-]?(\d+)", capture.stem, re.IGNORECASE)
    if not match:
        return None
    return int(match.group(1))


def infer_scene_name(capture: Path) -> str | None:
    directory_name = capture.parent.name
    match = re.match(r"^\d{8}_\d{6}_(?P<name>.+)$", directory_name)
    if not match:
        return None

    name = match.group("name")
    for suffix in ("-external-player", "-manual", "-hotkey", "-context-menu", "-capture"):
        if name.endswith(suffix):
            name = name[: -len(suffix)]
            break
    return name or None


def infer_platform() -> str:
    if sys.platform.startswith("win"):
        return "Windows"
    if sys.platform == "darwin":
        return "macOS"
    if sys.platform.startswith("linux"):
        return "Linux"
    return sys.platform


def resolve_prefab_signatures(args: argparse.Namespace, capture: Path) -> Path:
    path = Path(args.prefab_signatures).resolve() if args.prefab_signatures else capture.parent / "unity_prefab_signatures.sqlite"
    if not path.exists():
        raise PostprocessError(
            "prefab_signatures_not_found",
            f"Unity prefab signature SQLite not found: {path}",
        )
    return path


def resolve_postprocess_artifact_dir(args: argparse.Namespace, capture: Path) -> Path:
    if args.postprocess_artifact_dir:
        return Path(args.postprocess_artifact_dir).resolve()

    safe_capture_name = re.sub(r"[^A-Za-z0-9_.-]+", "_", capture.stem).strip("_") or "capture"
    return (
        repo_root_from_here()
        / ".workspace"
        / "artifacts"
        / "renderdoc-analysis"
        / "rdc-fact-postprocess"
        / f"{safe_capture_name}_{timestamp()}"
    )


def build_fact_import_command(args: argparse.Namespace, capture: Path, db_path: Path, artifact_dir: Path) -> list[str]:
    command = [
        sys.executable,
        str(repo_root_from_here() / "tools" / "renderdoc-db" / "rdc_capture_fact_import.py"),
        "--capture",
        str(capture),
        "--sqlite",
        str(db_path),
        "--artifact-dir",
        str(artifact_dir),
    ]

    project_name = args.project_name if args.project_name is not None else repo_root_from_here().name
    scene_name = args.scene_name if args.scene_name is not None else infer_scene_name(capture)
    frame_index = args.frame_index if args.frame_index is not None else infer_frame_index(capture)
    platform = args.platform if args.platform is not None else infer_platform()

    optional_values: list[tuple[str, Any]] = [
        ("--project-name", project_name),
        ("--scene-name", scene_name),
        ("--frame-index", frame_index),
        ("--platform", platform),
        ("--resolution-width", args.resolution_width),
        ("--resolution-height", args.resolution_height),
        ("--captured-at", args.captured_at),
        ("--qrenderdoc", args.qrenderdoc),
        ("--event-min", args.event_min if args.event_min else None),
        ("--event-max", args.event_max if args.event_max else None),
    ]
    for flag, value in optional_values:
        if value is None or value == "":
            continue
        command.extend([flag, str(value)])

    command.extend(["--timeout", str(args.timeout)])
    if args.append:
        command.append("--append")
    if args.keep_export:
        command.append("--keep-export")
    return command


def build_prefab_import_command(db_path: Path, capture_id: int, prefab_signatures: Path, *, append: bool) -> list[str]:
    command = [
        sys.executable,
        str(repo_root_from_here() / "tools" / "renderdoc-cli" / "rdoc_agent.py"),
        "db",
        "import-prefabs",
        "--sqlite",
        str(db_path),
        "--capture-id",
        str(capture_id),
        "--prefab-signatures",
        str(prefab_signatures),
        "--json",
    ]
    if append:
        command.append("--append")
    return command


def materialize_capture(args: argparse.Namespace) -> dict[str, Any]:
    repo = repo_root_from_here()
    capture = resolve_capture(args)
    db_path = Path(args.fact_db).resolve() if args.fact_db else default_fact_db_path()
    prefab_signatures = resolve_prefab_signatures(args, capture)
    artifact_dir = resolve_postprocess_artifact_dir(args, capture)
    artifact_dir.mkdir(parents=True, exist_ok=True)

    fact_command = build_fact_import_command(args, capture, db_path, artifact_dir)
    fact_payload = run_json_command(
        fact_command,
        cwd=repo,
        timeout=max(args.timeout + 60, args.timeout),
        command_name="rdc_capture_fact_import",
    )
    if not fact_payload.get("ok"):
        raise PostprocessError("fact_import_failed", "RDC fact import returned ok=false.", detail=fact_payload)

    capture_id = fact_payload.get("capture_id")
    if capture_id is None:
        raise PostprocessError("missing_capture_id", "RDC fact import did not return capture_id.", detail=fact_payload)
    capture_id = int(capture_id)

    prefab_command = build_prefab_import_command(db_path, capture_id, prefab_signatures, append=args.prefab_append)
    prefab_payload = run_json_command(
        prefab_command,
        cwd=repo,
        timeout=120,
        command_name="rdoc-agent db import-prefabs",
    )
    if prefab_payload.get("errors"):
        raise PostprocessError("prefab_import_failed", "Prefab import returned errors.", detail=prefab_payload)

    manifest = {
        "postprocessed_at_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "capture": str(capture),
        "artifactDirectory": str(capture.parent),
        "sqlite": str(db_path),
        "postprocessArtifactDirectory": str(artifact_dir),
        "prefabSignatures": str(prefab_signatures),
        "captureId": capture_id,
        "factImport": fact_payload,
        "prefabImport": prefab_payload,
        "commands": {
            "factImport": fact_command,
            "prefabImport": prefab_command,
        },
    }
    manifest_path = artifact_dir / "postprocess_manifest.json"
    write_json(manifest_path, manifest)

    return {
        "capture": str(capture),
        "sqlite": str(db_path),
        "captureId": capture_id,
        "postprocessManifest": str(manifest_path),
        "factImportManifest": fact_payload.get("manifest"),
        "factImportRowCounts": fact_payload.get("row_counts"),
        "prefabImport": prefab_payload.get("data"),
        "warnings": (prefab_payload.get("warnings") or []),
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Export a RenderDoc .rdc capture into the fact DB and import the matching Unity prefab table.",
    )
    parser.add_argument("--capture", help="Input .rdc capture path. Defaults to the latest capture under --artifacts-root.")
    parser.add_argument("--artifact-dir", help="Capture artifact directory. Used to find the latest .rdc inside that directory.")
    parser.add_argument(
        "--artifacts-root",
        help="Root used when --capture and --artifact-dir are omitted. Defaults to .workspace/artifacts/renderdoc-captures.",
    )
    parser.add_argument(
        "--sqlite",
        "--db",
        dest="fact_db",
        help="SQLite fact DB to create/update. Defaults to .workspace/artifacts/renderdoc-analysis/rdc-fact-database/rdc_capture_four_table.sqlite.",
    )
    parser.add_argument("--prefab-signatures", help="Unity prefab signature SQLite. Defaults to unity_prefab_signatures.sqlite next to the .rdc.")
    parser.add_argument("--postprocess-artifact-dir", help="Directory for qrenderdoc logs and postprocess_manifest.json.")
    parser.add_argument("--project-name", help="capture.project_name. Defaults to the repository directory name.")
    parser.add_argument("--scene-name", help="capture.scene_name. Defaults to an inferred name from the capture artifact directory.")
    parser.add_argument("--frame-index", type=int, help="capture.frame_index. Defaults to frame number parsed from capture_frameNNNN.rdc.")
    parser.add_argument("--platform", help="capture.platform. Defaults to the host platform.")
    parser.add_argument("--resolution-width", type=int, help="capture.resolution_width.")
    parser.add_argument("--resolution-height", type=int, help="capture.resolution_height.")
    parser.add_argument("--captured-at", help="capture.captured_at. Defaults to the .rdc modified time in UTC.")
    parser.add_argument("--qrenderdoc", help="qrenderdoc.exe path. Defaults to RENDERDOC_QRENDERDOC or the importer default.")
    parser.add_argument("--timeout", type=int, default=900, help="qrenderdoc timeout in seconds. Defaults to 900.")
    parser.add_argument("--event-min", type=int, default=0, help="Only inspect resource bindings for events >= this id.")
    parser.add_argument("--event-max", type=int, default=0, help="Only inspect resource bindings for events <= this id.")
    parser.add_argument("--append", action="store_true", help="Append fact rows instead of replacing rows for the same rdc_file_path.")
    parser.add_argument("--keep-export", action="store_true", help="Keep the raw qrenderdoc export JSON path in the fact import manifest.")
    parser.add_argument("--prefab-append", action="store_true", help="Append prefab rows instead of replacing rows for the imported capture.")
    parser.add_argument("--json", action="store_true", help="Emit JSON. This is the default format.")
    parser.add_argument("--pretty", action="store_true", help="Pretty-print JSON output.")
    parser.add_argument("--output", help="Write the JSON result to this file as well as stdout.")
    parser.add_argument("--verbose", action="store_true", help="Include diagnostic error details.")
    return parser


def emit(payload: dict[str, Any], args: argparse.Namespace) -> None:
    indent = 2 if args.pretty else None
    text = json.dumps(payload, ensure_ascii=False, indent=indent)
    if args.output:
        output_path = Path(args.output).resolve()
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(text + "\n", encoding="utf-8")
    print(text)


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        data = materialize_capture(args)
        payload = {
            "ok": True,
            "data": data,
            "warnings": data.get("warnings") or [],
            "errors": [],
        }
        emit(payload, args)
        return 0
    except PostprocessError as exc:
        error: dict[str, Any] = {
            "code": exc.code,
            "message": exc.message,
            "recoverable": exc.recoverable,
        }
        if args.verbose and exc.detail is not None:
            error["detail"] = exc.detail
        emit({"ok": False, "data": {}, "warnings": [], "errors": [error]}, args)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
