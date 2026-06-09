#!/usr/bin/env python3
"""Import RenderDoc .rdc data into RDC fact tables.

This standalone importer does not call rdoc-agent or any Agent CLI. It launches
qrenderdoc.exe with rdc_capture_fact_export_qrenderdoc.py, then writes:
capture, draw_event, texture, model, and event_resource_binding.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
from typing import Any


CONFIG_ENV = "RDC_FACT_IMPORT_CONFIG"
DEFAULT_QRENDERDOC = r"C:\Program Files\RenderDoc\qrenderdoc.exe"


def repo_root_from_here() -> Path:
    return Path(__file__).resolve().parents[2]


def timestamp() -> str:
    return datetime.now().strftime("%Y%m%d-%H%M%S-%f")


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def file_sha256(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(8 * 1024 * 1024)
            if not chunk:
                break
            hasher.update(chunk)
    return hasher.hexdigest()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Import a RenderDoc .rdc capture using RenderDoc resource usage indexes.")
    parser.add_argument("--capture", required=True, help="Input .rdc capture path.")
    parser.add_argument("--sqlite", required=True, help="SQLite database to create or update.")
    parser.add_argument("--project-name", help="capture.project_name")
    parser.add_argument("--build-id", help="capture.build_id")
    parser.add_argument("--scene-name", help="capture.scene_name")
    parser.add_argument("--frame-index", type=int, default=0, help="capture.frame_index")
    parser.add_argument("--platform", help="capture.platform")
    parser.add_argument("--resolution-width", type=int, help="capture.resolution_width")
    parser.add_argument("--resolution-height", type=int, help="capture.resolution_height")
    parser.add_argument("--captured-at", help="capture.captured_at. Defaults to the .rdc file modified time in UTC.")
    parser.add_argument("--qrenderdoc", default=os.environ.get("RENDERDOC_QRENDERDOC", DEFAULT_QRENDERDOC))
    parser.add_argument("--timeout", type=int, default=900, help="qrenderdoc timeout in seconds.")
    parser.add_argument("--event-min", type=int, default=0, help="Only inspect resource bindings for events >= this id.")
    parser.add_argument("--event-max", type=int, default=0, help="Only inspect resource bindings for events <= this id.")
    parser.add_argument("--append", action="store_true", help="Append instead of replacing rows for the same rdc_file_path.")
    parser.add_argument("--artifact-dir", help="Directory for qrenderdoc export JSON and import manifest.")
    parser.add_argument("--keep-export", action="store_true", help="Keep qrenderdoc export JSON path in the manifest.")
    return parser


def captured_at_from_file(capture: Path) -> str:
    return datetime.fromtimestamp(capture.stat().st_mtime, timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def migrate_optional_columns(connection: sqlite3.Connection) -> None:
    tables = {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type = 'table'").fetchall()}
    draw_columns = {row[1] for row in connection.execute("PRAGMA table_info(draw_event)").fetchall()}
    binding_columns = {row[1] for row in connection.execute("PRAGMA table_info(event_resource_binding)").fetchall()}
    if "rd_resource" in tables or "rd_resource_pk" in binding_columns or "binding_name" in binding_columns:
        connection.execute("PRAGMA foreign_keys = OFF")
        connection.execute("DROP TABLE IF EXISTS event_resource_binding")
        connection.execute("DROP TABLE IF EXISTS rd_resource")
        connection.execute("DROP TABLE IF EXISTS texture")
        connection.execute("DROP TABLE IF EXISTS model")
        connection.execute("PRAGMA foreign_keys = ON")
    if "draw_index" in draw_columns or "vertex_count" in draw_columns or "gpu_ms" in draw_columns:
        connection.execute("PRAGMA foreign_keys = OFF")
        connection.execute("DROP TABLE IF EXISTS event_resource_binding")
        connection.execute("DROP TABLE IF EXISTS draw_event")
        connection.execute("PRAGMA foreign_keys = ON")
    removed_binding_columns = {"bind_stage", "bind_type", "slot_index", "usage_role"}
    if removed_binding_columns.intersection(binding_columns):
        connection.execute("PRAGMA foreign_keys = OFF")
        connection.execute("DROP TABLE IF EXISTS event_resource_binding")
        connection.execute("PRAGMA foreign_keys = ON")
    columns = {row[1] for row in connection.execute("PRAGMA table_info(draw_event)").fetchall()}
    if columns and "gpu_us" not in columns:
        connection.execute("ALTER TABLE draw_event ADD COLUMN gpu_us REAL")


def ensure_sqlite_schema(connection: sqlite3.Connection) -> None:
    schema_path = Path(__file__).resolve().with_name("rdc_capture_fact_schema.sqlite.sql")
    migrate_optional_columns(connection)
    connection.executescript(schema_path.read_text(encoding="utf-8"))
    migrate_optional_columns(connection)
    connection.commit()


def meaningful_resource_name(value: Any, resource_id: Any) -> str | None:
    text = str(value or "").strip()
    if not text:
        return None
    if text == str(resource_id):
        return None
    if text.startswith("ResourceId::"):
        return None
    return text


def run_qrenderdoc_export(args: argparse.Namespace, capture: Path, artifact_dir: Path) -> dict[str, Any]:
    qrenderdoc = Path(args.qrenderdoc)
    if not qrenderdoc.exists():
        raise FileNotFoundError(f"qrenderdoc.exe not found: {qrenderdoc}")
    helper = Path(__file__).resolve().with_name("rdc_capture_fact_export_qrenderdoc.py")
    if not helper.exists():
        raise FileNotFoundError(f"qrenderdoc export helper not found: {helper}")

    output_path = artifact_dir / "rdc_fact_export.json"
    config_path = artifact_dir / "qrenderdoc_config.json"
    stdout_log = artifact_dir / "qrenderdoc_stdout.log"
    stderr_log = artifact_dir / "qrenderdoc_stderr.log"
    write_json(
        config_path,
        {
            "capture": str(capture),
            "output": str(output_path),
            "event_min": max(0, int(args.event_min or 0)),
            "event_max": max(0, int(args.event_max or 0)),
        },
    )

    env = os.environ.copy()
    env[CONFIG_ENV] = str(config_path)
    with stdout_log.open("w", encoding="utf-8") as stdout_handle, stderr_log.open("w", encoding="utf-8") as stderr_handle:
        completed = subprocess.run(
            [str(qrenderdoc), "--python", str(helper)],
            cwd=str(repo_root_from_here()),
            env=env,
            text=True,
            stdout=stdout_handle,
            stderr=stderr_handle,
            timeout=args.timeout,
        )
    if not output_path.exists():
        raise RuntimeError(f"qrenderdoc did not write export JSON. See {artifact_dir}")
    payload = read_json(output_path)
    if completed.returncode != 0 or not payload.get("ok"):
        raise RuntimeError(f"qrenderdoc export failed. See {artifact_dir}")
    payload["_export_path"] = str(output_path)
    return payload


def insert_capture(connection: sqlite3.Connection, args: argparse.Namespace, capture: Path, payload: dict[str, Any]) -> int:
    if not args.append:
        existing_capture_ids = [
            int(row[0])
            for row in connection.execute("SELECT capture_id FROM capture WHERE rdc_file_path = ?", (str(capture),)).fetchall()
        ]
        for capture_id in existing_capture_ids:
            connection.execute(
                """
                DELETE FROM prefab_texture
                WHERE prefab_pk IN (
                    SELECT prefab_pk FROM prefab WHERE capture_id = ?
                )
                """,
                (capture_id,),
            )
            connection.execute(
                """
                DELETE FROM prefab_model
                WHERE prefab_pk IN (
                    SELECT prefab_pk FROM prefab WHERE capture_id = ?
                )
                """,
                (capture_id,),
            )
            connection.execute("DELETE FROM prefab WHERE capture_id = ?", (capture_id,))
            connection.execute(
                """
                DELETE FROM event_resource_binding
                WHERE event_pk IN (
                    SELECT event_pk FROM draw_event WHERE capture_id = ?
                )
                """,
                (capture_id,),
            )
            connection.execute("DELETE FROM draw_event WHERE capture_id = ?", (capture_id,))
            connection.execute("DELETE FROM texture WHERE capture_id = ?", (capture_id,))
            connection.execute("DELETE FROM model WHERE capture_id = ?", (capture_id,))
            connection.execute("DELETE FROM capture WHERE capture_id = ?", (capture_id,))
    capture_info = payload.get("capture") or {}
    cursor = connection.execute(
        """
        INSERT INTO capture (
            project_name,
            build_id,
            scene_name,
            frame_index,
            rdc_file_path,
            graphics_api,
            platform,
            resolution_width,
            resolution_height,
            captured_at
        )
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            args.project_name,
            args.build_id,
            args.scene_name,
            args.frame_index,
            str(capture),
            capture_info.get("graphics_api"),
            args.platform,
            args.resolution_width,
            args.resolution_height,
            args.captured_at or captured_at_from_file(capture),
        ),
    )
    return int(cursor.lastrowid)


def insert_draw_events(connection: sqlite3.Connection, capture_id: int, rows: list[dict[str, Any]]) -> dict[int, int]:
    event_pk_by_event_id: dict[int, int] = {}
    for row in rows:
        cursor = connection.execute(
            """
            INSERT INTO draw_event (
                capture_id,
                event_id,
                parent_event_id,
                event_name,
                marker_path,
                drawcall_type,
                index_count,
                instance_count,
                primitive_count,
                render_target,
                depth_target,
                gpu_us
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                capture_id,
                row.get("event_id"),
                row.get("parent_event_id"),
                row.get("event_name"),
                row.get("marker_path"),
                row.get("drawcall_type"),
                row.get("index_count"),
                row.get("instance_count"),
                row.get("primitive_count"),
                row.get("render_target"),
                row.get("depth_target"),
                row.get("gpu_us"),
            ),
        )
        if row.get("event_id") is not None:
            event_pk_by_event_id[int(row["event_id"])] = int(cursor.lastrowid)
    return event_pk_by_event_id


def insert_textures(connection: sqlite3.Connection, capture_id: int, rows: list[dict[str, Any]]) -> dict[str, int]:
    pk_by_texture_id: dict[str, int] = {}
    for row in rows:
        texture_id = row.get("rdc_texture_id")
        if not texture_id:
            continue
        texture_name = meaningful_resource_name(row.get("texture_name"), texture_id)
        if not texture_name:
            continue
        cursor = connection.execute(
            """
            INSERT INTO texture (
                capture_id,
                rdc_texture_id,
                texture_name,
                byte_size,
                width,
                height,
                depth,
                mip_count,
                array_size,
                format
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                capture_id,
                texture_id,
                texture_name,
                row.get("byte_size"),
                row.get("width"),
                row.get("height"),
                row.get("depth"),
                row.get("mip_count"),
                row.get("array_size"),
                row.get("format"),
            ),
        )
        pk_by_texture_id[str(texture_id)] = int(cursor.lastrowid)
    return pk_by_texture_id


def insert_models(connection: sqlite3.Connection, capture_id: int, rows: list[dict[str, Any]]) -> dict[str, int]:
    pk_by_model_id: dict[str, int] = {}
    for row in rows:
        model_id = row.get("rdc_model_id")
        if not model_id:
            continue
        cursor = connection.execute(
            """
            INSERT INTO model (
                capture_id,
                rdc_model_id,
                model_name,
                vertex_buffer_ids,
                index_buffer_id,
                index_count,
                vertex_count,
                instance_count,
                primitive_count
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                capture_id,
                model_id,
                row.get("model_name"),
                row.get("vertex_buffer_ids"),
                row.get("index_buffer_id"),
                row.get("index_count"),
                row.get("vertex_count"),
                row.get("instance_count"),
                row.get("primitive_count"),
            ),
        )
        pk_by_model_id[str(model_id)] = int(cursor.lastrowid)
    return pk_by_model_id


def insert_bindings(
    connection: sqlite3.Connection,
    event_pk_by_event_id: dict[int, int],
    texture_pk_by_id: dict[str, int],
    model_pk_by_id: dict[str, int],
    rows: list[dict[str, Any]],
) -> int:
    inserted = 0
    seen = set()
    for row in rows:
        event_id = row.get("event_id")
        texture_id = row.get("rdc_texture_id")
        if event_id is None or texture_id is None:
            continue
        event_pk = event_pk_by_event_id.get(int(event_id))
        texture_pk = texture_pk_by_id.get(str(texture_id))
        model_pk = model_pk_by_id.get(str(row.get("rdc_model_id"))) if row.get("rdc_model_id") else None
        if event_pk is None or texture_pk is None:
            continue
        key = (
            event_pk,
            texture_pk,
            model_pk,
        )
        if key in seen:
            continue
        seen.add(key)
        connection.execute(
            """
            INSERT INTO event_resource_binding (
                event_pk,
                texture_pk,
                model_pk
            )
            VALUES (?, ?, ?)
            """,
            key,
        )
        inserted += 1
    return inserted


def import_payload(connection: sqlite3.Connection, args: argparse.Namespace, capture: Path, payload: dict[str, Any]) -> dict[str, Any]:
    connection.execute("PRAGMA foreign_keys = ON")
    ensure_sqlite_schema(connection)
    with connection:
        capture_id = insert_capture(connection, args, capture, payload)
        event_pk_by_event_id = insert_draw_events(connection, capture_id, payload.get("draw_events") or [])
        texture_pk_by_id = insert_textures(connection, capture_id, payload.get("textures") or payload.get("resources") or [])
        model_pk_by_id = insert_models(connection, capture_id, payload.get("models") or [])
        binding_count = insert_bindings(
            connection,
            event_pk_by_event_id,
            texture_pk_by_id,
            model_pk_by_id,
            payload.get("bindings") or [],
        )
    fk_errors = connection.execute("PRAGMA foreign_key_check").fetchall()
    return {
        "capture_id": capture_id,
        "row_counts": {
            "capture": 1,
            "draw_event": len(event_pk_by_event_id),
            "texture": len(texture_pk_by_id),
            "model": len(model_pk_by_id),
            "event_resource_binding": binding_count,
        },
        "foreign_key_errors": len(fk_errors),
    }


def main() -> int:
    args = build_parser().parse_args()
    capture = Path(args.capture).resolve()
    if not capture.exists():
        print(f"Capture not found: {capture}", file=sys.stderr)
        return 2
    if capture.suffix.lower() != ".rdc":
        print(f"Expected a .rdc file: {capture}", file=sys.stderr)
        return 2
    db_path = Path(args.sqlite).resolve()
    artifact_dir = Path(args.artifact_dir).resolve() if args.artifact_dir else (
        repo_root_from_here() / ".workspace" / "artifacts" / "renderdoc-analysis" / "rdc-fact-import" / timestamp()
    )
    artifact_dir.mkdir(parents=True, exist_ok=True)

    digest = file_sha256(capture)
    payload = run_qrenderdoc_export(args, capture, artifact_dir)
    db_path.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(str(db_path))
    try:
        result = import_payload(connection, args, capture, payload)
    finally:
        connection.close()

    manifest = {
        "imported_at_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "capture": str(capture),
        "sqlite": str(db_path),
        "file_sha256": digest,
        "qrenderdoc": str(Path(args.qrenderdoc)),
        "artifact_dir": str(artifact_dir),
        "export_json": payload.get("_export_path") if args.keep_export else None,
        "import_mode": "usage-index",
        "result": result,
        "export_stats": payload.get("stats") or {},
    }
    manifest_path = artifact_dir / "import_manifest.json"
    write_json(manifest_path, manifest)
    print(json.dumps({"ok": True, "manifest": str(manifest_path), **result}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
