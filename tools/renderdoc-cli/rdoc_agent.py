#!/usr/bin/env python3
"""Database-only RenderDoc fact CLI.

The old qrenderdoc-backed analysis commands were intentionally removed. This
client now queries imported fact databases only.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import sys
from typing import Any


TOOL_NAME = "rdoc-agent"


class AgentCommandError(Exception):
    def __init__(self, code: str, message: str, *, recoverable: bool = True, detail: Any = None) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.recoverable = recoverable
        self.detail = detail


def repo_root_from_here() -> Path:
    return Path(__file__).resolve().parents[2]


def default_fact_db_path() -> Path:
    return repo_root_from_here() / ".workspace" / "artifacts" / "renderdoc-analysis" / "rdc-fact-database" / "rdc_capture_four_table.sqlite"


def add_common_options(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--json", action="store_true", help="Emit JSON. This is the default output format.")
    parser.add_argument("--pretty", action="store_true", help="Pretty-print JSON output.")
    parser.add_argument("--fields", help="Comma-separated data fields to include.")
    parser.add_argument("--limit", type=int, help="Maximum instance rows to include. Defaults to 20; use 0 for all.")
    parser.add_argument("--output", help="Write JSON output to this file instead of stdout.")
    parser.add_argument("--verbose", action="store_true", help="Include diagnostic error details when available.")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog=TOOL_NAME,
        description="Query imported RenderDoc fact databases.",
    )
    domains = parser.add_subparsers(dest="domain", required=True)

    db = domains.add_parser("db", help="Query imported RenderDoc fact databases.")
    db_actions = db.add_subparsers(dest="action", required=True)
    db_instance_cost = db_actions.add_parser(
        "instance-cost",
        help="Get GPU microsecond cost aggregated from the imported fact database.",
    )
    add_common_options(db_instance_cost)
    db_instance_cost.add_argument(
        "--sqlite",
        "--db",
        dest="fact_db",
        help="SQLite fact database. Defaults to .workspace/artifacts/renderdoc-analysis/rdc-fact-database/rdc_capture_four_table.sqlite.",
    )
    db_instance_cost.add_argument("--capture-id", type=int, help="capture.capture_id to query. Required when the database has multiple captures.")
    db_instance_cost.add_argument("--capture", help="rdc_file_path value to select from capture when capture_id is not provided.")
    db_instance_cost.add_argument(
        "--group-by",
        choices=("model", "model-marker"),
        default="model",
        help="Aggregation key. model deduplicates resource streams by model_name; model-marker also splits by marker_path.",
    )
    db_instance_cost.add_argument("--q", help="Substring filter applied to model_name or marker_path.")
    db_instance_cost.add_argument("--prefab-name", help="Resolve model names from prefab table, then aggregate those models.")
    db_instance_cost.add_argument("--model-name", help="Substring filter applied to model_name.")
    db_instance_cost.add_argument("--marker-prefix", help="Prefix filter applied to draw_event.marker_path.")
    db_instance_cost.add_argument(
        "--sort",
        choices=("total-gpu-us", "avg-gpu-us", "max-gpu-us", "event-count", "model-name"),
        default="total-gpu-us",
        help="Sort order for instances.",
    )
    db_instance_cost.add_argument(
        "--event-limit",
        type=int,
        default=3,
        help="Top GPU events to include per instance. Defaults to 3; use 0 to omit topEvents.",
    )
    db_instance_cost.set_defaults(handler=command_db_instance_cost)

    db_import_prefabs = db_actions.add_parser(
        "import-prefabs",
        help="Import Unity prefab->model/texture facts from unity_prefab_signatures.sqlite into the fact database.",
    )
    add_common_options(db_import_prefabs)
    db_import_prefabs.add_argument(
        "--sqlite",
        "--db",
        dest="fact_db",
        help="SQLite fact database. Defaults to .workspace/artifacts/renderdoc-analysis/rdc-fact-database/rdc_capture_four_table.sqlite.",
    )
    db_import_prefabs.add_argument("--capture-id", type=int, help="capture.capture_id to update. Required when the database has multiple captures.")
    db_import_prefabs.add_argument("--capture", help="rdc_file_path value to select from capture when capture_id is not provided.")
    db_import_prefabs.add_argument(
        "--prefab-signatures",
        help="Unity prefab signature SQLite. Defaults to unity_prefab_signatures.sqlite next to capture.rdc.",
    )
    db_import_prefabs.add_argument("--append", action="store_true", help="Append prefab rows instead of replacing rows for the selected capture.")
    db_import_prefabs.set_defaults(handler=command_db_import_prefabs)

    return parser


def error_entry(code: str, message: str, *, recoverable: bool = True, detail: Any = None) -> dict[str, Any]:
    entry: dict[str, Any] = {
        "code": code,
        "message": message,
        "recoverable": recoverable,
    }
    if detail is not None:
        entry["detail"] = detail
    return entry


def envelope(
    *,
    data: Any = None,
    warnings: list[str] | None = None,
    errors: list[dict[str, Any]] | None = None,
    diagnostics: dict[str, Any] | None = None,
) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "data": data if data is not None else {},
        "warnings": warnings or [],
        "errors": errors or [],
    }
    if diagnostics:
        payload["diagnostics"] = diagnostics
    return payload


def emit(args: argparse.Namespace, payload: dict[str, Any]) -> None:
    indent = 2 if args.pretty else None
    text = json.dumps(payload, ensure_ascii=False, indent=indent)
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(text + "\n", encoding="utf-8")
        return
    print(text)


def parse_fields(value: str | None) -> list[str]:
    if not value:
        return []
    return [field.strip() for field in value.split(",") if field.strip()]


def apply_fields(data: dict[str, Any], fields_value: str | None, warnings: list[str]) -> dict[str, Any]:
    fields = parse_fields(fields_value)
    if not fields:
        return data

    filtered: dict[str, Any] = {}
    missing: list[str] = []
    for field in fields:
        if field in data:
            filtered[field] = data[field]
        else:
            missing.append(field)
    if missing:
        warnings.append("Unknown field(s) ignored: " + ",".join(missing))
    return filtered


def validate_fact_db_path(args: argparse.Namespace) -> Path:
    db_path = Path(args.fact_db).resolve() if args.fact_db else default_fact_db_path()
    if not db_path.exists():
        raise AgentCommandError("db_not_found", f"SQLite fact database not found: {db_path}")
    if not db_path.is_file():
        raise AgentCommandError("db_not_file", f"SQLite fact database path is not a file: {db_path}")
    return db_path


def require_fact_db_schema(connection: sqlite3.Connection) -> None:
    tables = {row["name"] for row in connection.execute("SELECT name FROM sqlite_master WHERE type = 'table'").fetchall()}
    required_tables = {"capture", "draw_event", "texture", "model", "event_resource_binding"}
    missing_tables = sorted(required_tables - tables)
    if missing_tables:
        raise AgentCommandError("invalid_fact_db_schema", "SQLite fact database is missing required table(s): " + ", ".join(missing_tables))

    draw_columns = {row["name"] for row in connection.execute("PRAGMA table_info(draw_event)").fetchall()}
    if "gpu_us" not in draw_columns:
        raise AgentCommandError("invalid_fact_db_schema", "draw_event.gpu_us is required. Reimport the capture with the current fact schema.")

    binding_columns = {row["name"] for row in connection.execute("PRAGMA table_info(event_resource_binding)").fetchall()}
    required_binding_columns = {"event_pk", "texture_pk", "model_pk"}
    missing_binding_columns = sorted(required_binding_columns - binding_columns)
    if missing_binding_columns:
        raise AgentCommandError(
            "invalid_fact_db_schema",
            "event_resource_binding is missing required column(s): " + ", ".join(missing_binding_columns),
        )


def prefab_tables_exist(connection: sqlite3.Connection) -> bool:
    tables = {row["name"] for row in connection.execute("SELECT name FROM sqlite_master WHERE type = 'table'").fetchall()}
    return {"prefab", "prefab_model", "prefab_texture"}.issubset(tables)


def ensure_prefab_schema(connection: sqlite3.Connection) -> None:
    connection.executescript(
        """
        CREATE TABLE IF NOT EXISTS prefab (
            prefab_pk INTEGER PRIMARY KEY,
            capture_id INTEGER NOT NULL,
            prefab_name TEXT NOT NULL,
            prefab_path TEXT,
            prefab_guid TEXT,
            source TEXT,
            FOREIGN KEY (capture_id) REFERENCES capture(capture_id)
        );

        CREATE TABLE IF NOT EXISTS prefab_model (
            prefab_model_pk INTEGER PRIMARY KEY,
            prefab_pk INTEGER NOT NULL,
            model_name TEXT NOT NULL,
            model_pk INTEGER,
            lod_index INTEGER,
            prefab_inner_path TEXT,
            scene_instance_path TEXT,
            scene_renderer_path TEXT,
            FOREIGN KEY (prefab_pk) REFERENCES prefab(prefab_pk) ON DELETE CASCADE,
            FOREIGN KEY (model_pk) REFERENCES model(model_pk)
        );

        CREATE TABLE IF NOT EXISTS prefab_texture (
            prefab_texture_pk INTEGER PRIMARY KEY,
            prefab_pk INTEGER NOT NULL,
            texture_name TEXT NOT NULL,
            texture_pk INTEGER,
            FOREIGN KEY (prefab_pk) REFERENCES prefab(prefab_pk) ON DELETE CASCADE,
            FOREIGN KEY (texture_pk) REFERENCES texture(texture_pk)
        );

        CREATE INDEX IF NOT EXISTS idx_prefab_name
            ON prefab(capture_id, prefab_name);
        CREATE INDEX IF NOT EXISTS idx_prefab_path
            ON prefab(capture_id, prefab_path);
        CREATE INDEX IF NOT EXISTS idx_prefab_model_name
            ON prefab_model(model_name);
        CREATE INDEX IF NOT EXISTS idx_prefab_model_model
            ON prefab_model(model_pk);
        CREATE INDEX IF NOT EXISTS idx_prefab_texture_name
            ON prefab_texture(texture_name);
        CREATE INDEX IF NOT EXISTS idx_prefab_texture_texture
            ON prefab_texture(texture_pk);
        """
    )


def prefab_name_from_path(prefab_path: str | None) -> str:
    if not prefab_path:
        return ""
    normalized = prefab_path.replace("\\", "/").rsplit("/", 1)[-1]
    if normalized.lower().endswith(".prefab"):
        normalized = normalized[:-7]
    return normalized


def resolve_fact_db_capture(connection: sqlite3.Connection, args: argparse.Namespace) -> dict[str, Any]:
    if args.capture_id is not None:
        row = connection.execute("SELECT * FROM capture WHERE capture_id = ?", (args.capture_id,)).fetchone()
        if row is None:
            raise AgentCommandError("capture_not_found", f"capture_id not found in fact database: {args.capture_id}")
        return dict(row)

    if args.capture:
        capture_path = str(Path(args.capture).resolve())
        row = connection.execute("SELECT * FROM capture WHERE rdc_file_path = ?", (capture_path,)).fetchone()
        if row is None:
            raise AgentCommandError("capture_not_found", f"Capture path not found in fact database: {capture_path}")
        return dict(row)

    rows = [dict(row) for row in connection.execute("SELECT * FROM capture ORDER BY capture_id").fetchall()]
    if not rows:
        raise AgentCommandError("capture_not_found", "No capture rows exist in the fact database.")
    if len(rows) > 1:
        raise AgentCommandError("ambiguous_capture", "Fact database has multiple captures. Provide --capture-id or --capture.")
    return rows[0]


def sqlite_like_contains(value: str) -> str:
    escaped = value.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")
    return f"%{escaped}%"


def sqlite_like_prefix(value: str) -> str:
    escaped = value.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")
    return f"{escaped}%"


def compact_unique(values: list[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for value in values:
        clean_value = value.strip()
        if not clean_value:
            continue
        key = clean_value.lower()
        if key in seen:
            continue
        seen.add(key)
        result.append(clean_value)
    return result


def split_asset_name(value: str) -> list[str]:
    normalized = value.replace("\\", "/").rsplit("/", 1)[-1]
    normalized = normalized.rsplit(".", 1)[0]
    normalized = normalized.replace("-", "_").replace(" ", "_")
    return [token for token in normalized.split("_") if token]


def expanded_query_terms(value: str) -> list[str]:
    """Build model/marker search terms for Unity prefab-style names.

    RenderDoc facts usually contain mesh/model names such as
    SM_OakTree_01_Ivy_LOD1, while Unity users often ask about prefab names such
    as P_OakTree_01_Summer. Search by the stable family root first so callers do
    not need to invent LOD names that may not exist in the capture.
    """

    if not value:
        return []

    terms: list[str] = [value]
    tokens = split_asset_name(value)
    if not tokens:
        return compact_unique(terms)

    prefix_tokens = {"p", "sm", "m", "t"}
    season_tokens = {"spring", "summer", "autumn", "fall", "winter"}
    content_tokens = tokens[1:] if tokens[0].lower() in prefix_tokens else tokens

    if content_tokens:
        terms.append("_".join(content_tokens))

    without_season = [token for token in content_tokens if token.lower() not in season_tokens]
    if without_season:
        terms.append("_".join(without_season))

    before_season: list[str] = []
    for token in content_tokens:
        if token.lower() in season_tokens:
            break
        before_season.append(token)
    if before_season:
        terms.append("_".join(before_season))

    family_tokens: list[str] = []
    for token in before_season or without_season or content_tokens:
        family_tokens.append(token)
        if token.isdigit():
            break
    if family_tokens:
        family = "_".join(family_tokens)
        terms.append(family)
        terms.append("SM_" + family)
        terms.append("P_" + family)

    return compact_unique([term for term in terms if len(term) >= 2])


def append_model_or_marker_filter(filters: list[str], params: list[Any], terms: list[str]) -> None:
    clauses: list[str] = []
    for term in terms:
        pattern = sqlite_like_contains(term)
        clauses.append("(m.model_name LIKE ? ESCAPE '\\' OR COALESCE(d.marker_path, '') LIKE ? ESCAPE '\\')")
        params.extend([pattern, pattern])
    if clauses:
        filters.append("(" + " OR ".join(clauses) + ")")


def append_exact_or_contains_filter(
    filters: list[str],
    params: list[Any],
    columns: list[str],
    value: str,
    expanded_terms: list[str] | None = None,
) -> None:
    terms = compact_unique([value] + (expanded_terms or []))
    clauses: list[str] = []
    for column in columns:
        clauses.append(f"{column} = ?")
        params.append(value)
    for term in terms:
        pattern = sqlite_like_contains(term)
        for column in columns:
            clauses.append(f"COALESCE({column}, '') LIKE ? ESCAPE '\\'")
            params.append(pattern)
    if clauses:
        filters.append("(" + " OR ".join(clauses) + ")")


def parse_texture_names(value: Any) -> list[str]:
    if value in (None, ""):
        return []
    if isinstance(value, list):
        return compact_unique([str(item) for item in value])

    text = str(value).strip()
    if not text:
        return []
    try:
        parsed = json.loads(text)
        if isinstance(parsed, list):
            return compact_unique([str(item) for item in parsed])
    except json.JSONDecodeError:
        pass

    return compact_unique([item.strip() for item in text.split(",")])


def signature_prefab_name(row: sqlite3.Row, columns: set[str]) -> str:
    if "prefab_name" in columns and row["prefab_name"]:
        return str(row["prefab_name"])
    return prefab_name_from_path(str(row["prefab_path"] or ""))


def default_prefab_signatures_path(capture: dict[str, Any]) -> Path:
    rdc_file_path = capture.get("rdc_file_path")
    if not rdc_file_path:
        return Path("unity_prefab_signatures.sqlite")
    return Path(str(rdc_file_path)).resolve().parent / "unity_prefab_signatures.sqlite"


def rounded_float(value: float | None, digits: int = 3) -> float | None:
    if value is None:
        return None
    return round(float(value), digits)


def compact_sorted(values: set[Any], limit: int = 8) -> list[Any]:
    clean_values = [value for value in values if value not in (None, "")]
    return sorted(clean_values, key=lambda item: str(item))[:limit]


def compact_sorted_strings(values: set[str], limit: int = 20) -> list[str]:
    clean_values = [value for value in values if value]
    return sorted(clean_values, key=lambda item: item.lower())[:limit]


def stable_json_hash(prefix: str, value: Any, length: int = 12) -> str:
    payload = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return prefix + hashlib.sha1(payload.encode("utf-8")).hexdigest()[:length]


def resolve_prefab_lookup(connection: sqlite3.Connection, capture_id: int, prefab_name: str | None) -> dict[str, Any] | None:
    if not prefab_name:
        return None
    if not prefab_tables_exist(connection):
        raise AgentCommandError(
            "prefab_tables_missing",
            "Prefab lookup requires prefab database tables. Run `rdoc-agent db import-prefabs` for this capture first.",
        )

    query = prefab_name.strip()
    rows = connection.execute(
        """
        SELECT
            p.prefab_pk,
            p.prefab_name,
            p.prefab_path,
            p.prefab_guid,
            pm.model_name,
            pm.model_pk,
            pm.lod_index,
            pm.prefab_inner_path,
            pm.scene_instance_path,
            pm.scene_renderer_path,
            CASE
                WHEN LOWER(p.prefab_name) = LOWER(?) THEN 1
                WHEN LOWER(COALESCE(p.prefab_path, '')) = LOWER(?) THEN 1
                ELSE 0
            END AS exact_match
        FROM prefab AS p
        LEFT JOIN prefab_model AS pm
          ON pm.prefab_pk = p.prefab_pk
        WHERE p.capture_id = ?
          AND (
            LOWER(p.prefab_name) = LOWER(?)
            OR LOWER(COALESCE(p.prefab_path, '')) = LOWER(?)
            OR p.prefab_name LIKE ? ESCAPE '\\'
            OR COALESCE(p.prefab_path, '') LIKE ? ESCAPE '\\'
          )
        ORDER BY exact_match DESC, p.prefab_name, p.prefab_path, pm.lod_index, pm.model_name
        """,
        (
            query,
            query,
            capture_id,
            query,
            query,
            sqlite_like_contains(query),
            sqlite_like_contains(query),
        ),
    ).fetchall()

    prefabs: dict[int, dict[str, Any]] = {}
    model_names: set[str] = set()
    model_pks: set[int] = set()
    exact_prefab_pks: set[int] = set()

    for row in rows:
        prefab_pk = int(row["prefab_pk"])
        prefab = prefabs.get(prefab_pk)
        if prefab is None:
            prefab = {
                "prefabPk": prefab_pk,
                "prefabName": row["prefab_name"],
                "prefabPath": row["prefab_path"],
                "prefabGuid": row["prefab_guid"],
                "exactMatch": bool(row["exact_match"]),
                "_modelNames": set(),
                "_lodIndices": set(),
                "_sceneInstancePaths": set(),
            }
            prefabs[prefab_pk] = prefab

        if row["exact_match"]:
            exact_prefab_pks.add(prefab_pk)
        if row["model_name"]:
            model_name = str(row["model_name"])
            model_names.add(model_name)
            prefab["_modelNames"].add(model_name)
        if row["model_pk"] is not None:
            model_pks.add(int(row["model_pk"]))
        if row["lod_index"] is not None:
            prefab["_lodIndices"].add(int(row["lod_index"]))
        if row["scene_instance_path"]:
            prefab["_sceneInstancePaths"].add(str(row["scene_instance_path"]))

    matched_prefabs: list[dict[str, Any]] = []
    for prefab in prefabs.values():
        matched_prefabs.append(
            {
                "prefabPk": prefab["prefabPk"],
                "prefabName": prefab["prefabName"],
                "prefabPath": prefab["prefabPath"],
                "prefabGuid": prefab["prefabGuid"],
                "exactMatch": prefab["exactMatch"],
                "modelNames": compact_sorted_strings(prefab["_modelNames"], limit=12),
                "lodIndices": compact_sorted(prefab["_lodIndices"], limit=8),
                "sceneInstancePaths": compact_sorted_strings(prefab["_sceneInstancePaths"], limit=8),
            }
        )

    matched_prefabs.sort(
        key=lambda item: (
            0 if item["exactMatch"] else 1,
            str(item.get("prefabName") or "").lower(),
            str(item.get("prefabPath") or "").lower(),
        )
    )

    return {
        "prefabName": query,
        "matchedPrefabCount": len(matched_prefabs),
        "exactMatchCount": len(exact_prefab_pks),
        "matchedPrefabs": matched_prefabs,
        "modelNames": compact_sorted_strings(model_names, limit=50),
        "modelPks": sorted(model_pks),
        "modelNameCount": len(model_names),
        "modelPkCount": len(model_pks),
        "matchRule": "prefab.prefab_name/path exact or contains; then prefab_model model names feed RenderDoc model GPU aggregation",
    }


def append_prefab_model_filter(filters: list[str], params: list[Any], prefab_lookup: dict[str, Any] | None) -> None:
    if not prefab_lookup:
        return

    model_pks = [int(value) for value in prefab_lookup.get("modelPks") or []]
    model_names = [str(value) for value in prefab_lookup.get("modelNames") or [] if value]
    clauses: list[str] = []
    if model_pks:
        placeholders = ",".join("?" for _ in model_pks)
        clauses.append(f"m.model_pk IN ({placeholders})")
        params.extend(model_pks)
    if model_names:
        placeholders = ",".join("?" for _ in model_names)
        clauses.append(f"m.model_name IN ({placeholders})")
        params.extend(model_names)

    if clauses:
        filters.append("(" + " OR ".join(clauses) + ")")
    else:
        filters.append("0 = 1")


def fetch_instance_cost_rows(
    connection: sqlite3.Connection,
    capture_id: int,
    args: argparse.Namespace,
    prefab_lookup: dict[str, Any] | None = None,
) -> list[sqlite3.Row]:
    filters = ["d.capture_id = ?", "b.model_pk IS NOT NULL"]
    params: list[Any] = [capture_id]

    append_prefab_model_filter(filters, params, prefab_lookup)
    if args.q:
        append_model_or_marker_filter(filters, params, expanded_query_terms(args.q))
    if args.model_name:
        filters.append("m.model_name LIKE ? ESCAPE '\\'")
        params.append(sqlite_like_contains(args.model_name))
    if args.marker_prefix:
        filters.append("COALESCE(d.marker_path, '') LIKE ? ESCAPE '\\'")
        params.append(sqlite_like_prefix(args.marker_prefix))

    sql = f"""
        SELECT
            b.event_pk,
            b.texture_pk,
            d.event_id,
            d.event_name,
            d.marker_path,
            d.drawcall_type,
            d.gpu_us,
            d.index_count AS event_index_count,
            d.instance_count AS event_instance_count,
            d.primitive_count AS event_primitive_count,
            m.model_pk,
            m.rdc_model_id,
            m.model_name,
            m.vertex_count AS model_vertex_count,
            m.index_count AS model_index_count,
            m.instance_count AS model_instance_count,
            m.primitive_count AS model_primitive_count
        FROM event_resource_binding AS b
        JOIN draw_event AS d
          ON d.event_pk = b.event_pk
        JOIN model AS m
          ON m.model_pk = b.model_pk
        WHERE {" AND ".join(filters)}
        ORDER BY d.event_id, m.model_name, m.model_pk, b.texture_pk
    """
    return connection.execute(sql, params).fetchall()


def aggregate_instance_cost_rows(rows: list[sqlite3.Row], group_by: str, event_limit: int) -> list[dict[str, Any]]:
    groups: dict[tuple[Any, ...], dict[str, Any]] = {}

    for row in rows:
        model_name = row["model_name"] or "(unnamed model)"
        marker_path = row["marker_path"]
        if group_by == "model-marker":
            key = (model_name, marker_path or "")
            instance_key = stable_json_hash("instance:", {"modelName": model_name, "markerPath": marker_path})
        else:
            key = (model_name,)
            instance_key = f"model:{model_name}"

        group = groups.get(key)
        if group is None:
            group = {
                "instanceKey": instance_key,
                "modelName": model_name,
                "markerPath": marker_path if group_by == "model-marker" else None,
                "_events": {},
                "_textures": set(),
                "_modelPks": set(),
                "_rdcModelIds": set(),
                "_markerPaths": set(),
                "_vertexCounts": set(),
                "_indexCounts": set(),
                "_instanceCounts": set(),
                "_primitiveCounts": set(),
            }
            groups[key] = group

        group["_modelPks"].add(row["model_pk"])
        group["_rdcModelIds"].add(row["rdc_model_id"])
        group["_markerPaths"].add(marker_path)
        if row["texture_pk"] is not None:
            group["_textures"].add(row["texture_pk"])
        for field, set_name in (
            ("model_vertex_count", "_vertexCounts"),
            ("model_index_count", "_indexCounts"),
            ("model_instance_count", "_instanceCounts"),
            ("model_primitive_count", "_primitiveCounts"),
        ):
            if row[field] is not None:
                group[set_name].add(row[field])

        events = group["_events"]
        event_pk = row["event_pk"]
        if event_pk not in events:
            events[event_pk] = {
                "eventPk": event_pk,
                "eventId": row["event_id"],
                "eventName": row["event_name"],
                "markerPath": marker_path,
                "drawcallType": row["drawcall_type"],
                "gpuUs": rounded_float(row["gpu_us"]),
                "indexCount": row["event_index_count"],
                "instanceCount": row["event_instance_count"],
                "primitiveCount": row["event_primitive_count"],
            }

    instances: list[dict[str, Any]] = []
    for group in groups.values():
        events = list(group["_events"].values())
        timed_events = [event for event in events if event["gpuUs"] is not None]
        total_gpu_us = sum(float(event["gpuUs"]) for event in timed_events)
        max_gpu_us = max((float(event["gpuUs"]) for event in timed_events), default=None)
        avg_gpu_us = total_gpu_us / len(timed_events) if timed_events else None
        top_events = sorted(timed_events, key=lambda event: (event["gpuUs"] or 0.0, event["eventId"] or 0), reverse=True)
        top_events = top_events[:event_limit] if event_limit > 0 else []

        vertex_counts = compact_sorted(group["_vertexCounts"], limit=4)
        index_counts = compact_sorted(group["_indexCounts"], limit=4)
        instance_counts = compact_sorted(group["_instanceCounts"], limit=4)
        primitive_counts = compact_sorted(group["_primitiveCounts"], limit=4)
        marker_paths = compact_sorted(group["_markerPaths"], limit=5)

        item: dict[str, Any] = {
            "instanceKey": group["instanceKey"],
            "modelName": group["modelName"],
            "eventCount": len(events),
            "timedEventCount": len(timed_events),
            "totalGpuUs": rounded_float(total_gpu_us),
            "avgGpuUs": rounded_float(avg_gpu_us),
            "maxGpuUs": rounded_float(max_gpu_us),
            "textureCount": len(group["_textures"]),
            "modelResourceCount": len(group["_modelPks"]),
            "rdcModelIds": compact_sorted(group["_rdcModelIds"]),
            "markerPathCount": len([value for value in group["_markerPaths"] if value]),
            "markerPathSamples": marker_paths,
            "vertexCount": vertex_counts[0] if len(vertex_counts) == 1 else None,
            "indexCount": index_counts[0] if len(index_counts) == 1 else None,
            "instanceCount": instance_counts[0] if len(instance_counts) == 1 else None,
            "primitiveCount": primitive_counts[0] if len(primitive_counts) == 1 else None,
            "topEvents": top_events,
        }
        if group["markerPath"] is not None:
            item["markerPath"] = group["markerPath"]
        if len(vertex_counts) > 1:
            item["vertexCountCandidates"] = vertex_counts
        if len(index_counts) > 1:
            item["indexCountCandidates"] = index_counts
        instances.append(item)

    return instances


def sort_instance_costs(instances: list[dict[str, Any]], sort_key: str) -> list[dict[str, Any]]:
    if sort_key == "model-name":
        return sorted(instances, key=lambda item: (str(item.get("modelName") or ""), str(item.get("instanceKey") or "")))
    key_map = {
        "total-gpu-us": "totalGpuUs",
        "avg-gpu-us": "avgGpuUs",
        "max-gpu-us": "maxGpuUs",
        "event-count": "eventCount",
    }
    field = key_map[sort_key]
    return sorted(instances, key=lambda item: (float(item.get(field) or 0.0), int(item.get("eventCount") or 0)), reverse=True)


def command_db_instance_cost(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    warnings: list[str] = []
    diagnostics: dict[str, Any] = {}

    try:
        if args.limit is not None and args.limit < 0:
            raise AgentCommandError("invalid_limit", "--limit must be >= 0.")
        if args.event_limit < 0:
            raise AgentCommandError("invalid_event_limit", "--event-limit must be >= 0.")

        limit = 20 if args.limit is None else args.limit
        db_path = validate_fact_db_path(args)
        connection = sqlite3.connect(str(db_path))
        connection.row_factory = sqlite3.Row
        try:
            require_fact_db_schema(connection)
            capture = resolve_fact_db_capture(connection, args)
            capture_id = int(capture["capture_id"])
            prefab_lookup = resolve_prefab_lookup(connection, capture_id, args.prefab_name)
            if prefab_lookup and prefab_lookup["matchedPrefabCount"] == 0:
                warnings.append(f"No prefab database row matched `{args.prefab_name}`.")
            elif prefab_lookup and prefab_lookup["modelNameCount"] == 0 and prefab_lookup["modelPkCount"] == 0:
                warnings.append(f"Prefab `{args.prefab_name}` matched, but it has no model rows in prefab_model.")

            rows = fetch_instance_cost_rows(connection, capture_id, args, prefab_lookup)
            instances = aggregate_instance_cost_rows(rows, args.group_by, args.event_limit)
            instances = sort_instance_costs(instances, args.sort)
            returned_instances = instances if limit == 0 else instances[:limit]

            capture_gpu_us = connection.execute(
                "SELECT SUM(gpu_us) FROM draw_event WHERE capture_id = ? AND gpu_us IS NOT NULL",
                (capture_id,),
            ).fetchone()[0]
            covered_events: dict[int, float] = {}
            for row in rows:
                event_pk = int(row["event_pk"])
                if event_pk not in covered_events:
                    covered_events[event_pk] = float(row["gpu_us"] or 0.0)
            covered_gpu_us = sum(covered_events.values())

            if not instances:
                warnings.append("No model-bound events matched the query filters.")
            if limit != 0 and len(instances) > limit:
                warnings.append(f"Instance cost rows truncated to {limit} of {len(instances)}; use --limit 0 for all rows.")

            data: dict[str, Any] = {
                "database": str(db_path),
                "unit": "microseconds",
                "groupBy": args.group_by,
                "sort": args.sort,
                "capture": {
                    "captureId": capture_id,
                    "projectName": capture.get("project_name"),
                    "buildId": capture.get("build_id"),
                    "sceneName": capture.get("scene_name"),
                    "frameIndex": capture.get("frame_index"),
                    "graphicsApi": capture.get("graphics_api"),
                    "platform": capture.get("platform"),
                    "rdcFilePath": capture.get("rdc_file_path"),
                },
                "captureTotalGpuUs": rounded_float(capture_gpu_us),
                "coveredGpuUs": rounded_float(covered_gpu_us),
                "coveredEventCount": len(covered_events),
                "instanceCount": len(instances),
                "returnedInstanceCount": len(returned_instances),
                "instances": returned_instances,
            }
            if args.q:
                data["query"] = {
                    "q": args.q,
                    "expandedTerms": expanded_query_terms(args.q),
                    "matchRule": "model_name or marker_path contains any expanded term",
                }
            if prefab_lookup:
                data["prefabLookup"] = prefab_lookup
            if capture_gpu_us:
                data["coveragePercent"] = rounded_float((covered_gpu_us / float(capture_gpu_us)) * 100.0)

            data = apply_fields(data, args.fields, warnings)
            return 0, envelope(data=data, warnings=warnings, diagnostics=diagnostics)
        finally:
            connection.close()
    except AgentCommandError as exc:
        payload = envelope(
            warnings=warnings,
            errors=[error_entry(exc.code, exc.message, recoverable=exc.recoverable, detail=exc.detail if args.verbose else None)],
            diagnostics=diagnostics,
        )
        return 2 if exc.code.startswith(("missing_", "capture_", "invalid_", "db_", "ambiguous_", "prefab_")) else 1, payload


def row_value(row: sqlite3.Row, columns: set[str], name: str, default: Any = None) -> Any:
    return row[name] if name in columns else default


def clear_prefab_rows(connection: sqlite3.Connection, capture_id: int) -> None:
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


def load_name_pk_lookup(connection: sqlite3.Connection, capture_id: int, table: str, name_column: str, pk_column: str) -> dict[str, int]:
    lookup: dict[str, int] = {}
    rows = connection.execute(
        f"SELECT {pk_column}, {name_column} FROM {table} WHERE capture_id = ? AND {name_column} IS NOT NULL",
        (capture_id,),
    ).fetchall()
    for row in rows:
        name = str(row[name_column] or "").strip()
        if name:
            lookup.setdefault(name.lower(), int(row[pk_column]))
    return lookup


def read_prefab_signature_rows(signature_db_path: Path) -> tuple[list[sqlite3.Row], set[str]]:
    if not signature_db_path.exists():
        raise AgentCommandError("prefab_signatures_not_found", f"Unity prefab signature SQLite not found: {signature_db_path}")
    if not signature_db_path.is_file():
        raise AgentCommandError("prefab_signatures_not_file", f"Unity prefab signature path is not a file: {signature_db_path}")

    connection = sqlite3.connect(str(signature_db_path))
    connection.row_factory = sqlite3.Row
    try:
        table = connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'prefab_draw_signatures'"
        ).fetchone()
        if table is None:
            raise AgentCommandError(
                "invalid_prefab_signatures",
                "Unity prefab signature SQLite is missing table: prefab_draw_signatures",
            )

        columns = {row["name"] for row in connection.execute("PRAGMA table_info(prefab_draw_signatures)").fetchall()}
        required_columns = {"prefab_path", "mesh_name"}
        missing_columns = sorted(required_columns - columns)
        if missing_columns:
            raise AgentCommandError(
                "invalid_prefab_signatures",
                "prefab_draw_signatures is missing required column(s): " + ", ".join(missing_columns),
            )

        rows = connection.execute("SELECT * FROM prefab_draw_signatures ORDER BY id").fetchall()
        return rows, columns
    finally:
        connection.close()


def get_or_create_prefab_pk(
    connection: sqlite3.Connection,
    cache: dict[tuple[str, str, str], int],
    capture_id: int,
    prefab_name: str,
    prefab_path: str,
    prefab_guid: str,
) -> int:
    key = (prefab_name.lower(), prefab_path.lower(), prefab_guid.lower())
    if key in cache:
        return cache[key]

    cursor = connection.execute(
        """
        INSERT INTO prefab (
            capture_id,
            prefab_name,
            prefab_path,
            prefab_guid,
            source
        )
        VALUES (?, ?, ?, ?, ?)
        """,
        (capture_id, prefab_name, prefab_path, prefab_guid, "unity_prefab_signatures.sqlite"),
    )
    prefab_pk = int(cursor.lastrowid)
    cache[key] = prefab_pk
    return prefab_pk


def command_db_import_prefabs(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    warnings: list[str] = []
    diagnostics: dict[str, Any] = {}

    try:
        db_path = validate_fact_db_path(args)
        connection = sqlite3.connect(str(db_path))
        connection.row_factory = sqlite3.Row
        try:
            require_fact_db_schema(connection)
            ensure_prefab_schema(connection)
            capture = resolve_fact_db_capture(connection, args)
            capture_id = int(capture["capture_id"])
            signature_db_path = Path(args.prefab_signatures).resolve() if args.prefab_signatures else default_prefab_signatures_path(capture)
            signature_rows, signature_columns = read_prefab_signature_rows(signature_db_path)

            if not args.append:
                clear_prefab_rows(connection, capture_id)

            model_pk_by_name = load_name_pk_lookup(connection, capture_id, "model", "model_name", "model_pk")
            texture_pk_by_name = load_name_pk_lookup(connection, capture_id, "texture", "texture_name", "texture_pk")
            prefab_cache: dict[tuple[str, str, str], int] = {}
            inserted_model_keys: set[tuple[Any, ...]] = set()
            inserted_texture_keys: set[tuple[Any, ...]] = set()
            unlinked_model_names: set[str] = set()
            unlinked_texture_names: set[str] = set()
            model_row_count = 0
            linked_model_row_count = 0
            texture_row_count = 0
            linked_texture_row_count = 0

            for row in signature_rows:
                prefab_path = str(row_value(row, signature_columns, "prefab_path", "") or "").strip()
                prefab_name = str(row_value(row, signature_columns, "prefab_name", "") or "").strip()
                prefab_guid = str(row_value(row, signature_columns, "prefab_guid", "") or "").strip()
                if not prefab_name:
                    prefab_name = prefab_name_from_path(prefab_path)
                if not prefab_name:
                    continue

                prefab_pk = get_or_create_prefab_pk(
                    connection,
                    prefab_cache,
                    capture_id,
                    prefab_name,
                    prefab_path,
                    prefab_guid,
                )

                model_name = str(row_value(row, signature_columns, "mesh_name", "") or "").strip()
                if model_name:
                    model_pk = model_pk_by_name.get(model_name.lower())
                    if model_pk is None:
                        unlinked_model_names.add(model_name)
                    lod_index = row_value(row, signature_columns, "lod_index")
                    model_key = (
                        prefab_pk,
                        model_name.lower(),
                        model_pk,
                        lod_index,
                        str(row_value(row, signature_columns, "prefab_inner_path", "") or ""),
                        str(row_value(row, signature_columns, "scene_instance_path", "") or ""),
                        str(row_value(row, signature_columns, "scene_renderer_path", "") or ""),
                    )
                    if model_key not in inserted_model_keys:
                        inserted_model_keys.add(model_key)
                        connection.execute(
                            """
                            INSERT INTO prefab_model (
                                prefab_pk,
                                model_name,
                                model_pk,
                                lod_index,
                                prefab_inner_path,
                                scene_instance_path,
                                scene_renderer_path
                            )
                            VALUES (?, ?, ?, ?, ?, ?, ?)
                            """,
                            (
                                prefab_pk,
                                model_name,
                                model_pk,
                                lod_index,
                                row_value(row, signature_columns, "prefab_inner_path", ""),
                                row_value(row, signature_columns, "scene_instance_path", ""),
                                row_value(row, signature_columns, "scene_renderer_path", ""),
                            ),
                        )
                        model_row_count += 1
                        if model_pk is not None:
                            linked_model_row_count += 1

                for texture_name in parse_texture_names(row_value(row, signature_columns, "texture_names")):
                    texture_pk = texture_pk_by_name.get(texture_name.lower())
                    if texture_pk is None:
                        unlinked_texture_names.add(texture_name)
                    texture_key = (prefab_pk, texture_name.lower(), texture_pk)
                    if texture_key in inserted_texture_keys:
                        continue

                    inserted_texture_keys.add(texture_key)
                    connection.execute(
                        """
                        INSERT INTO prefab_texture (
                            prefab_pk,
                            texture_name,
                            texture_pk
                        )
                        VALUES (?, ?, ?)
                        """,
                        (prefab_pk, texture_name, texture_pk),
                    )
                    texture_row_count += 1
                    if texture_pk is not None:
                        linked_texture_row_count += 1

            connection.commit()

            if unlinked_model_names:
                warnings.append(
                    f"{len(unlinked_model_names)} prefab model name(s) were not linked to RenderDoc model rows by exact name."
                )
            if unlinked_texture_names:
                warnings.append(
                    f"{len(unlinked_texture_names)} prefab texture name(s) were not linked to RenderDoc texture rows by exact name."
                )

            data = {
                "database": str(db_path),
                "prefabSignatures": str(signature_db_path),
                "capture": {
                    "captureId": capture_id,
                    "sceneName": capture.get("scene_name"),
                    "rdcFilePath": capture.get("rdc_file_path"),
                },
                "sourceRowCount": len(signature_rows),
                "prefabCount": len(prefab_cache),
                "prefabModelRowCount": model_row_count,
                "prefabTextureRowCount": texture_row_count,
                "linkedModelRowCount": linked_model_row_count,
                "unlinkedModelNameCount": len(unlinked_model_names),
                "unlinkedModelNameSamples": compact_sorted_strings(unlinked_model_names, limit=12),
                "linkedTextureRowCount": linked_texture_row_count,
                "unlinkedTextureNameCount": len(unlinked_texture_names),
                "unlinkedTextureNameSamples": compact_sorted_strings(unlinked_texture_names, limit=12),
                "append": bool(args.append),
            }
            data = apply_fields(data, args.fields, warnings)
            return 0, envelope(data=data, warnings=warnings, diagnostics=diagnostics)
        finally:
            connection.close()
    except AgentCommandError as exc:
        payload = envelope(
            warnings=warnings,
            errors=[error_entry(exc.code, exc.message, recoverable=exc.recoverable, detail=exc.detail if args.verbose else None)],
            diagnostics=diagnostics,
        )
        return 2 if exc.code.startswith(("missing_", "capture_", "invalid_", "db_", "ambiguous_", "prefab_")) else 1, payload


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    exit_code, payload = args.handler(args)
    emit(args, payload)
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
