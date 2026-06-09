from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "rdc_capture_postprocess.py"
SPEC = importlib.util.spec_from_file_location("rdc_capture_postprocess", SCRIPT)
rdc_capture_postprocess = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(rdc_capture_postprocess)


class RdcCapturePostprocessTests(unittest.TestCase):
    def test_find_latest_capture_recurses_by_mtime(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            old_capture = root / "old" / "capture_frame1.rdc"
            new_capture = root / "new" / "capture_frame2.rdc"
            old_capture.parent.mkdir()
            new_capture.parent.mkdir()
            old_capture.write_text("old", encoding="utf-8")
            new_capture.write_text("new", encoding="utf-8")
            os.utime(old_capture, (1000, 1000))
            os.utime(new_capture, (2000, 2000))

            self.assertEqual(rdc_capture_postprocess.find_latest_capture(root), new_capture.resolve())

    def test_fact_import_command_infers_scene_and_frame(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            capture = (
                Path(temp_dir)
                / "20260529_224927_scene_meadowenvironment_01_summer-external-player"
                / "capture_frame2051.rdc"
            )
            capture.parent.mkdir()
            capture.write_text("rdc", encoding="utf-8")
            args = rdc_capture_postprocess.build_parser().parse_args(
                [
                    "--capture",
                    str(capture),
                    "--sqlite",
                    str(Path(temp_dir) / "facts.sqlite"),
                    "--timeout",
                    "60",
                ]
            )

            command = rdc_capture_postprocess.build_fact_import_command(
                args,
                capture.resolve(),
                Path(temp_dir) / "facts.sqlite",
                Path(temp_dir) / "postprocess",
            )

        self.assertIn("--scene-name", command)
        self.assertEqual(command[command.index("--scene-name") + 1], "scene_meadowenvironment_01_summer")
        self.assertIn("--frame-index", command)
        self.assertEqual(command[command.index("--frame-index") + 1], "2051")

    def test_main_runs_fact_import_then_prefab_import(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_root = Path(temp_dir)
            capture = temp_root / "capture_frame42.rdc"
            prefab_signatures = temp_root / "unity_prefab_signatures.sqlite"
            db_path = temp_root / "facts.sqlite"
            artifact_dir = temp_root / "postprocess"
            output_path = temp_root / "result.json"
            capture.write_text("rdc", encoding="utf-8")
            prefab_signatures.write_text("sqlite", encoding="utf-8")

            calls: list[list[str]] = []

            def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
                calls.append(command)
                if "rdc_capture_fact_import.py" in command[1]:
                    return subprocess.CompletedProcess(
                        command,
                        0,
                        stdout=json.dumps(
                            {
                                "ok": True,
                                "capture_id": 7,
                                "manifest": str(artifact_dir / "import_manifest.json"),
                                "row_counts": {"capture": 1},
                            }
                        ),
                        stderr="",
                    )
                return subprocess.CompletedProcess(
                    command,
                    0,
                    stdout=json.dumps(
                        {
                            "data": {"prefabCount": 1, "prefabModelRowCount": 2},
                            "warnings": [],
                            "errors": [],
                        }
                    ),
                    stderr="",
                )

            stdout = io.StringIO()
            with patch.object(rdc_capture_postprocess.subprocess, "run", side_effect=fake_run):
                with contextlib.redirect_stdout(stdout):
                    exit_code = rdc_capture_postprocess.main(
                        [
                            "--capture",
                            str(capture),
                            "--sqlite",
                            str(db_path),
                            "--prefab-signatures",
                            str(prefab_signatures),
                            "--postprocess-artifact-dir",
                            str(artifact_dir),
                            "--output",
                            str(output_path),
                            "--json",
                        ]
                    )

            payload = json.loads(stdout.getvalue())
            output_exists = output_path.exists()
            manifest_exists = (artifact_dir / "postprocess_manifest.json").exists()

        self.assertEqual(exit_code, 0)
        self.assertEqual(len(calls), 2)
        self.assertIn("rdc_capture_fact_import.py", calls[0][1])
        self.assertEqual(calls[1][3], "import-prefabs")
        self.assertIn("--capture-id", calls[1])
        self.assertEqual(calls[1][calls[1].index("--capture-id") + 1], "7")
        self.assertTrue(output_exists)
        self.assertTrue(manifest_exists)
        self.assertEqual(payload["data"]["captureId"], 7)
        self.assertEqual(payload["data"]["prefabImport"]["prefabCount"], 1)


if __name__ == "__main__":
    unittest.main()
