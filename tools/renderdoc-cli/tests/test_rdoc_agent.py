from __future__ import annotations

import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "rdoc_agent.py"
SPEC = importlib.util.spec_from_file_location("rdoc_agent", SCRIPT)
rdoc_agent = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(rdoc_agent)


def write_fact_db(path: Path, *, two_captures: bool = False, include_gpu_us: bool = True) -> None:
    connection = sqlite3.connect(str(path))
    try:
        gpu_column = "gpu_us REAL" if include_gpu_us else "gpu_ms REAL"
        connection.executescript(
            f"""
            CREATE TABLE capture (
                capture_id INTEGER PRIMARY KEY,
                project_name TEXT,
                build_id TEXT,
                scene_name TEXT,
                frame_index INTEGER,
                rdc_file_path TEXT,
                graphics_api TEXT,
                platform TEXT,
                resolution_width INTEGER,
                resolution_height INTEGER,
                captured_at TEXT
            );
            CREATE TABLE draw_event (
                event_pk INTEGER PRIMARY KEY,
                capture_id INTEGER NOT NULL,
                event_id INTEGER NOT NULL,
                parent_event_id INTEGER,
                event_name TEXT,
                marker_path TEXT,
                drawcall_type TEXT,
                index_count INTEGER,
                instance_count INTEGER,
                primitive_count INTEGER,
                render_target TEXT,
                depth_target TEXT,
                {gpu_column}
            );
            CREATE TABLE texture (
                texture_pk INTEGER PRIMARY KEY,
                capture_id INTEGER NOT NULL,
                rdc_texture_id TEXT NOT NULL,
                texture_name TEXT,
                byte_size INTEGER,
                width INTEGER,
                height INTEGER,
                depth INTEGER,
                mip_count INTEGER,
                array_size INTEGER,
                format TEXT
            );
            CREATE TABLE model (
                model_pk INTEGER PRIMARY KEY,
                capture_id INTEGER NOT NULL,
                rdc_model_id TEXT NOT NULL,
                model_name TEXT,
                vertex_buffer_ids TEXT,
                index_buffer_id TEXT,
                index_count INTEGER,
                vertex_count INTEGER,
                instance_count INTEGER,
                primitive_count INTEGER
            );
            CREATE TABLE event_resource_binding (
                binding_id INTEGER PRIMARY KEY,
                event_pk INTEGER NOT NULL,
                texture_pk INTEGER NOT NULL,
                model_pk INTEGER
            );
            """
        )
        connection.execute(
            "INSERT INTO capture (capture_id, project_name, scene_name, frame_index, rdc_file_path, graphics_api, platform) VALUES (1, 'Unity6', 'Meadow', 7, 'C:/captures/frame.rdc', 'D3D12', 'Windows')"
        )
        if two_captures:
            connection.execute(
                "INSERT INTO capture (capture_id, project_name, scene_name, frame_index, rdc_file_path, graphics_api, platform) VALUES (2, 'Unity6', 'Second', 8, 'C:/captures/frame2.rdc', 'D3D12', 'Windows')"
            )

        gpu_field = "gpu_us" if include_gpu_us else "gpu_ms"
        connection.executemany(
            f"""
            INSERT INTO draw_event (
                event_pk, capture_id, event_id, event_name, marker_path, drawcall_type,
                index_count, instance_count, primitive_count, {gpu_field}
            )
            VALUES (?, 1, ?, 'DrawIndexed', ?, 'draw', ?, ?, ?, ?)
            """,
            [
                (1, 101, "Frame/Opaque", 300, 1, 100, 10.0),
                (2, 102, "Frame/Opaque", 150, 2, 50, 5.0),
                (3, 103, "Frame/Shadow", 150, 1, 50, 3.0),
            ],
        )
        connection.executemany(
            "INSERT INTO texture (texture_pk, capture_id, rdc_texture_id, texture_name) VALUES (?, 1, ?, ?)",
            [
                (1, "ResourceId::10", "T_Grass_BaseColor"),
                (2, "ResourceId::11", "T_Grass_Normal"),
            ],
        )
        connection.executemany(
            "INSERT INTO model (model_pk, capture_id, rdc_model_id, model_name, vertex_count) VALUES (?, 1, ?, ?, ?)",
            [
                (1, "ResourceId::20", "SM_Grass_01_02_LOD0", 1200),
                (2, "ResourceId::21", "SM_Grass_01_02_LOD0", 1200),
                (3, "ResourceId::22", "SM_Rock_01_LOD2", 300),
            ],
        )
        connection.executemany(
            "INSERT INTO event_resource_binding (binding_id, event_pk, texture_pk, model_pk) VALUES (?, ?, ?, ?)",
            [
                (1, 1, 1, 1),
                (2, 1, 2, 1),
                (3, 1, 1, 2),
                (4, 2, 1, 1),
                (5, 3, 1, 3),
            ],
        )
        connection.commit()
    finally:
        connection.close()


def write_prefab_signature_db(path: Path) -> None:
    connection = sqlite3.connect(str(path))
    try:
        connection.execute(
            """
            CREATE TABLE prefab_draw_signatures (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                prefab_path TEXT,
                prefab_name TEXT,
                prefab_guid TEXT,
                scene_path TEXT,
                scene_name TEXT,
                scene_instance_path TEXT,
                scene_instance_name TEXT,
                prefab_lod_key TEXT,
                prefab_inner_path TEXT,
                scene_renderer_path TEXT,
                lod_index INTEGER,
                mesh_name TEXT,
                mesh_base_name TEXT,
                texture_names TEXT
            )
            """
        )
        connection.executemany(
            """
            INSERT INTO prefab_draw_signatures (
                prefab_path,
                prefab_name,
                prefab_guid,
                scene_path,
                scene_name,
                scene_instance_path,
                scene_instance_name,
                prefab_lod_key,
                prefab_inner_path,
                scene_renderer_path,
                lod_index,
                mesh_name,
                mesh_base_name,
                texture_names
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (
                    "Assets/Prefabs/P_GrassPatch_01.prefab",
                    "P_GrassPatch_01",
                    "grass-guid",
                    "Assets/Scenes/Meadow.unity",
                    "Meadow",
                    "Environment/P_GrassPatch_01",
                    "P_GrassPatch_01",
                    "Assets/Prefabs/P_GrassPatch_01.prefab#lod:0",
                    "LOD0/Grass",
                    "Environment/P_GrassPatch_01/LOD0/Grass",
                    0,
                    "SM_Grass_01_02_LOD0",
                    "SM_Grass_01_02",
                    '["T_Grass_BaseColor", "T_Grass_Normal"]',
                ),
                (
                    "Assets/Prefabs/P_Rock_01.prefab",
                    "P_Rock_01",
                    "rock-guid",
                    "Assets/Scenes/Meadow.unity",
                    "Meadow",
                    "Environment/P_Rock_01",
                    "P_Rock_01",
                    "Assets/Prefabs/P_Rock_01.prefab#lod:2",
                    "LOD2/Rock",
                    "Environment/P_Rock_01/LOD2/Rock",
                    2,
                    "SM_Rock_01_LOD2",
                    "SM_Rock_01",
                    '["T_Grass_BaseColor"]',
                ),
            ],
        )
        connection.commit()
    finally:
        connection.close()


class RdocAgentDbTests(unittest.TestCase):
    def run_cli(self, argv: list[str]) -> tuple[int, dict]:
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            exit_code = rdoc_agent.main(argv)
        return exit_code, json.loads(stdout.getvalue())

    def test_db_instance_cost_deduplicates_binding_rows(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path)
            exit_code, payload = self.run_cli(
                [
                    "db",
                    "instance-cost",
                    "--sqlite",
                    str(db_path),
                    "--capture-id",
                    "1",
                    "--limit",
                    "10",
                    "--event-limit",
                    "1",
                    "--json",
                ]
            )

        instance = payload["data"]["instances"][0]
        self.assertEqual(exit_code, 0)
        self.assertEqual(payload["data"]["unit"], "microseconds")
        self.assertEqual(payload["data"]["instanceCount"], 2)
        self.assertEqual(instance["modelName"], "SM_Grass_01_02_LOD0")
        self.assertEqual(instance["eventCount"], 2)
        self.assertEqual(instance["totalGpuUs"], 15.0)
        self.assertEqual(instance["avgGpuUs"], 7.5)
        self.assertEqual(instance["maxGpuUs"], 10.0)
        self.assertEqual(instance["textureCount"], 2)
        self.assertEqual(instance["modelResourceCount"], 2)
        self.assertEqual(instance["vertexCount"], 1200)
        self.assertEqual(instance["topEvents"][0]["eventId"], 101)
        self.assertEqual(instance["topEvents"][0]["gpuUs"], 10.0)

    def test_db_instance_cost_filters_model_name(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path)
            exit_code, payload = self.run_cli(
                [
                    "db",
                    "instance-cost",
                    "--sqlite",
                    str(db_path),
                    "--model-name",
                    "Rock",
                    "--json",
                ]
            )

        self.assertEqual(exit_code, 0)
        self.assertEqual(payload["data"]["instanceCount"], 1)
        self.assertEqual(payload["data"]["instances"][0]["modelName"], "SM_Rock_01_LOD2")
        self.assertEqual(payload["data"]["instances"][0]["totalGpuUs"], 3.0)

    def test_db_instance_cost_q_expands_prefab_name_to_model_family(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path)
            connection = sqlite3.connect(str(db_path))
            try:
                connection.execute(
                    """
                    INSERT INTO draw_event (
                        event_pk, capture_id, event_id, event_name, marker_path, drawcall_type,
                        index_count, instance_count, primitive_count, gpu_us
                    )
                    VALUES (4, 1, 104, 'DrawIndexed', 'Frame/Opaque', 'draw', 600, 1, 200, 12.5)
                    """
                )
                connection.execute(
                    "INSERT INTO model (model_pk, capture_id, rdc_model_id, model_name, vertex_count) VALUES (4, 1, 'ResourceId::23', 'SM_OakTree_01_Ivy_LOD1', 900)"
                )
                connection.execute(
                    "INSERT INTO event_resource_binding (binding_id, event_pk, texture_pk, model_pk) VALUES (6, 4, 1, 4)"
                )
                connection.commit()
            finally:
                connection.close()

            exit_code, payload = self.run_cli(
                [
                    "db",
                    "instance-cost",
                    "--sqlite",
                    str(db_path),
                    "--q",
                    "P_OakTree_01_Summer",
                    "--json",
                ]
            )

        self.assertEqual(exit_code, 0)
        self.assertEqual(payload["data"]["instanceCount"], 1)
        self.assertEqual(payload["data"]["instances"][0]["modelName"], "SM_OakTree_01_Ivy_LOD1")
        self.assertEqual(payload["data"]["instances"][0]["totalGpuUs"], 12.5)
        self.assertIn("OakTree_01", payload["data"]["query"]["expandedTerms"])

    def test_db_import_prefabs_then_instance_cost_prefab_name_uses_prefab_table(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            prefab_db_path = Path(temp_dir) / "unity_prefab_signatures.sqlite"
            write_fact_db(db_path)
            write_prefab_signature_db(prefab_db_path)

            import_exit_code, import_payload = self.run_cli(
                [
                    "db",
                    "import-prefabs",
                    "--sqlite",
                    str(db_path),
                    "--capture-id",
                    "1",
                    "--prefab-signatures",
                    str(prefab_db_path),
                    "--json",
                ]
            )

            exit_code, payload = self.run_cli(
                [
                    "db",
                    "instance-cost",
                    "--sqlite",
                    str(db_path),
                    "--capture-id",
                    "1",
                    "--prefab-name",
                    "P_GrassPatch_01",
                    "--json",
                ]
            )

        self.assertEqual(import_exit_code, 0)
        self.assertEqual(import_payload["data"]["prefabCount"], 2)
        self.assertEqual(import_payload["data"]["prefabModelRowCount"], 2)
        self.assertEqual(import_payload["data"]["linkedModelRowCount"], 2)
        self.assertEqual(exit_code, 0)
        self.assertEqual(payload["data"]["instanceCount"], 1)
        self.assertEqual(payload["data"]["instances"][0]["modelName"], "SM_Grass_01_02_LOD0")
        self.assertEqual(payload["data"]["instances"][0]["totalGpuUs"], 15.0)
        self.assertEqual(payload["data"]["prefabLookup"]["matchedPrefabCount"], 1)
        self.assertEqual(payload["data"]["prefabLookup"]["modelNames"], ["SM_Grass_01_02_LOD0"])

    def test_db_instance_cost_prefab_name_requires_imported_prefab_tables(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path)
            exit_code, payload = self.run_cli(
                [
                    "db",
                    "instance-cost",
                    "--sqlite",
                    str(db_path),
                    "--capture-id",
                    "1",
                    "--prefab-name",
                    "P_GrassPatch_01",
                    "--json",
                ]
            )

        self.assertEqual(exit_code, 2)
        self.assertEqual(payload["errors"][0]["code"], "prefab_tables_missing")

    def test_db_instance_cost_splits_by_marker(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path)
            exit_code, payload = self.run_cli(
                [
                    "db",
                    "instance-cost",
                    "--sqlite",
                    str(db_path),
                    "--group-by",
                    "model-marker",
                    "--model-name",
                    "Grass",
                    "--sort",
                    "model-name",
                    "--json",
                ]
            )

        self.assertEqual(exit_code, 0)
        self.assertEqual(payload["data"]["instanceCount"], 1)
        self.assertEqual(payload["data"]["instances"][0]["markerPath"], "Frame/Opaque")

    def test_db_instance_cost_supports_default_database(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path)
            with patch.object(rdoc_agent, "default_fact_db_path", lambda: db_path):
                exit_code, payload = self.run_cli(["db", "instance-cost", "--limit", "1", "--json"])

        self.assertEqual(exit_code, 0)
        self.assertEqual(payload["data"]["database"], str(db_path))
        self.assertEqual(payload["data"]["returnedInstanceCount"], 1)

    def test_db_instance_cost_requires_capture_when_ambiguous(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path, two_captures=True)
            exit_code, payload = self.run_cli(["db", "instance-cost", "--sqlite", str(db_path), "--json"])

        self.assertEqual(exit_code, 2)
        self.assertEqual(payload["errors"][0]["code"], "ambiguous_capture")

    def test_db_instance_cost_rejects_old_gpu_ms_schema(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path, include_gpu_us=False)
            exit_code, payload = self.run_cli(["db", "instance-cost", "--sqlite", str(db_path), "--json"])

        self.assertEqual(exit_code, 2)
        self.assertEqual(payload["errors"][0]["code"], "invalid_fact_db_schema")
        self.assertIn("draw_event.gpu_us", payload["errors"][0]["message"])

    def test_fields_filter_limits_top_level_data(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            db_path = Path(temp_dir) / "facts.sqlite"
            write_fact_db(db_path)
            exit_code, payload = self.run_cli(
                [
                    "db",
                    "instance-cost",
                    "--sqlite",
                    str(db_path),
                    "--fields",
                    "unit,instanceCount",
                    "--json",
                ]
            )

        self.assertEqual(exit_code, 0)
        self.assertEqual(set(payload["data"].keys()), {"unit", "instanceCount"})


if __name__ == "__main__":
    unittest.main()
