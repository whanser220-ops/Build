from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "build_gpu_context_package.py"


def load_script_module():
    spec = importlib.util.spec_from_file_location("build_gpu_context_package", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class BuildGpuContextPackageTests(unittest.TestCase):
    def test_cli_writes_raw_events_and_derived_context_files(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp) / "repo"
            out_dir = Path(temp) / "out"
            shader_path = repo / "Assets" / "Project" / "Shaders" / "CrowdAnimCS.compute"
            shader_path.parent.mkdir(parents=True)
            shader_path.write_text(
                """
struct CrowdState
{
    uint clipId;
    uint lodLevel;
};

StructuredBuffer<CrowdState> CrowdStateBuffer : register(t0);
StructuredBuffer<float4> AnimationClipBuffer : register(t1);
RWStructuredBuffer<float4> CrowdBoneBuffer : register(u0);
RWStructuredBuffer<uint> CounterBuffer : register(u1);
groupshared uint LocalCounter[64];

[numthreads(64, 1, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint entityId = dispatchThreadId.x;
    uint bonesPerEntity = 32u;
    uint frameCount = 8u;
    uint frameIndex = 2u;
    CrowdState state = CrowdStateBuffer[entityId];
    for (uint boneId = 0u; boneId < bonesPerEntity; boneId++)
    {
        float4 clip = AnimationClipBuffer[state.clipId * frameCount + frameIndex];
        if (state.lodLevel == 0)
        {
            CrowdBoneBuffer[entityId * bonesPerEntity + boneId] = clip;
        }
    }

    uint previous;
    InterlockedAdd(CounterBuffer[0], 1u, previous);
    GroupMemoryBarrierWithGroupSync();
}
""",
                encoding="utf-8",
            )

            catalog_path = Path(temp) / "gpu_pass_catalog.json"
            catalog_path.write_text(
                json.dumps(
                    {
                        "schema_version": "1",
                        "passes": [
                            {
                                "pass_id": "crowd.update.animation",
                                "display_name": "Crowd / Update / Animation",
                                "type": "compute",
                                "owner": "crowd_system",
                                "shader": "Assets/Project/Shaders/CrowdAnimCS.compute",
                                "kernel": "CSMain",
                                "purpose": "Update crowd animation output buffers for visible entities.",
                                "inputs": [
                                    {
                                        "name": "CrowdStateBuffer",
                                        "type": "StructuredBuffer<CrowdState>",
                                        "access": "read",
                                        "meaning": "Per-entity crowd animation state.",
                                    }
                                ],
                                "outputs": [
                                    {
                                        "name": "CrowdBoneBuffer",
                                        "type": "RWStructuredBuffer<float4>",
                                        "access": "write",
                                        "meaning": "Skinned bone transforms written by the animation pass.",
                                    }
                                ],
                                "dispatch": {
                                    "thread_group_size": [64, 1, 1],
                                    "data_parallel_unit": "one_thread_per_instance",
                                    "group_count_formula": "ceil(entity_count / 64)",
                                },
                                "performance_hints": {
                                    "memory_pattern": "linear_per_entity",
                                    "expected_bottleneck": "memory_bandwidth",
                                    "has_atomics": True,
                                    "has_group_shared_memory": True,
                                    "branch_divergence_risk": "medium",
                                },
                                "source": {
                                    "file": "Assets/Project/Shaders/CrowdAnimCS.compute",
                                    "function": "CSMain",
                                },
                                "aliases": ["Crowd.Update.Animation"],
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            events_path = Path(temp) / "renderdoc_gpu_passes.raw.json"
            raw_events_input = {
                "version": 1,
                "gpu": {
                    "passes": [
                        {
                            "name": "Crowd.Update.Animation",
                            "event_name": "Dispatch(512, 1, 1)",
                            "type": "compute",
                            "event_id": 1842,
                            "dispatch": [512, 1, 1],
                            "gpu_ms": 3.2,
                            "shader": "Assets/Project/Shaders/CrowdAnimCS.compute",
                            "shader_entry": "main",
                            "shader_hash": "0xABCD1234",
                            "pipeline_hash": "0x91AF20CC",
                            "marker_path": ["Crowd.Update.Animation"],
                            "resources": {
                                "srv": [
                                    {
                                        "slot": 0,
                                        "name": "CrowdStateBuffer",
                                        "resource_name": "CrowdStateBuffer",
                                        "resource_id": "ResourceId::1",
                                        "resource_type": "buffer",
                                        "length": 2097152,
                                    }
                                ],
                                "uav": [
                                    {
                                        "slot": 0,
                                        "name": "CrowdBoneBuffer",
                                        "resource_name": "CrowdBoneBuffer",
                                        "resource_id": "ResourceId::2",
                                        "resource_type": "buffer",
                                        "length": 16777216,
                                    }
                                ],
                                "cbv": [],
                            },
                        }
                    ]
                },
            }
            events_path.write_text(
                json.dumps(raw_events_input),
                encoding="utf-8",
            )

            completed = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--repo",
                    str(repo),
                    "--events-in",
                    str(events_path),
                    "--catalog",
                    str(catalog_path),
                    "--artifact-dir",
                    str(out_dir),
                    "--frame",
                    "8123",
                    "--no-fetch-events",
                ],
                text=True,
                capture_output=True,
                check=True,
            )

            self.assertIn("GPU frame context ok", completed.stdout)
            self.assertFalse((out_dir / "gpu_pass_debug_map.json").exists())
            self.assertFalse((out_dir / "gpu_pass_bindings.json").exists())
            self.assertFalse((out_dir / "shader_facts.json").exists())

            raw_events = json.loads((out_dir / "gpu_pass_events.json").read_text(encoding="utf-8"))
            self.assertEqual(raw_events, raw_events_input)

            catalog = json.loads((out_dir / "gpu_pass_catalog.json").read_text(encoding="utf-8"))
            self.assertEqual(catalog["schema_version"], "1")
            self.assertEqual(catalog["passes"][0]["pass_id"], "crowd.update.animation")

            overview = json.loads((out_dir / "gpu_pass_overview.json").read_text(encoding="utf-8"))
            self.assertEqual(overview["schema_version"], "1.6")
            self.assertEqual(overview["frame"], 8123)
            overview_event = overview["events"][0]
            self.assertEqual(overview_event["event_id"], 1842)
            self.assertEqual(overview_event["name"], "Crowd.Update.Animation")
            self.assertEqual(overview_event["pass_id"], "crowd.update.animation")
            self.assertEqual(overview_event["matched_pass_id"], "crowd.update.animation")
            self.assertEqual(overview_event["display_name"], "Crowd / Update / Animation")
            self.assertEqual(overview_event["type"], "dispatch")
            self.assertEqual(overview_event["event_type"], "dispatch")
            self.assertEqual(overview_event["pass_type"], "compute")
            self.assertEqual(overview_event["duration_ms"], 3.2)
            self.assertEqual(
                overview_event["dispatch"],
                {
                    "groups": [512, 1, 1],
                    "threads_per_group": [64, 1, 1],
                    "estimated_threads": 32768,
                },
            )
            self.assertEqual(overview_event["shader"]["entry"], "CSMain")

            resources = json.loads((out_dir / "gpu_pass_resources.json").read_text(encoding="utf-8"))
            self.assertEqual(resources["schema_version"], "1.6")
            self.assertEqual(resources["frame"], 8123)
            self.assertEqual(resources["event_resource_bindings"][0]["event_id"], 1842)
            self.assertEqual(resources["event_resource_bindings"][0]["pass_id"], "crowd.update.animation")
            pass_resources = resources["event_resource_bindings"][0]["bindings"]
            self.assertEqual(len(pass_resources), 2)

            srv_binding = next(binding for binding in pass_resources if binding["slot"] == "t0")
            self.assertEqual(srv_binding["resource_id"], "ResourceId::1")
            self.assertEqual(srv_binding["name"], "CrowdStateBuffer")
            self.assertEqual(srv_binding["bind_type"], "SRV")
            self.assertEqual(srv_binding["access"], "read")
            self.assertEqual(srv_binding["type"], "buffer")
            self.assertEqual(srv_binding["hlsl_type"], "StructuredBuffer<CrowdState>")
            self.assertEqual(srv_binding["byte_size"], 2097152)
            self.assertGreater(srv_binding["stride"], 0)
            self.assertEqual(srv_binding["byte_size"] // srv_binding["stride"], srv_binding["estimated_element_count"])

            uav_binding = next(binding for binding in pass_resources if binding["slot"] == "u0")
            self.assertEqual(uav_binding["resource_id"], "ResourceId::2")
            self.assertEqual(uav_binding["name"], "CrowdBoneBuffer")
            self.assertEqual(uav_binding["bind_type"], "UAV")
            self.assertEqual(uav_binding["access"], "write")
            self.assertEqual(uav_binding["type"], "buffer")
            self.assertEqual(uav_binding["hlsl_type"], "RWStructuredBuffer<float4>")
            self.assertEqual(uav_binding["byte_size"], 16777216)
            self.assertEqual(uav_binding["stride"], 16)
            self.assertEqual(uav_binding["estimated_element_count"], 1048576)

            self.assertIn(
                {
                    "resource_id": "ResourceId::1",
                    "name": "CrowdStateBuffer",
                    "type": "buffer",
                    "hlsl_type": "StructuredBuffer<CrowdState>",
                    "consumer_passes": ["crowd.update.animation"],
                },
                resources["resource_usages"],
            )
            self.assertIn(
                {
                    "resource_id": "ResourceId::2",
                    "name": "CrowdBoneBuffer",
                    "type": "buffer",
                    "hlsl_type": "RWStructuredBuffer<float4>",
                    "producer_passes": ["crowd.update.animation"],
                },
                resources["resource_usages"],
            )

            context_text = (out_dir / "gpu_frame_context.json").read_text(encoding="utf-8")
            self.assertIn("total_invocations", context_text)
            context = json.loads(context_text)
            self.assertEqual(context["schema_version"], "1.6")
            self.assertEqual(context["frame"], 8123)
            self.assertEqual(
                context["context_sources"],
                {
                    "events": "gpu_pass_events.json",
                    "overview": "gpu_pass_overview.json",
                    "resources": "gpu_pass_resources.json",
                    "catalog": "gpu_pass_catalog.json",
                },
            )

            pass_context = context["gpu_passes"][0]
            self.assertEqual(pass_context["pass_id"], "crowd.update.animation")
            self.assertEqual(pass_context["matched_pass_id"], "crowd.update.animation")
            self.assertEqual(pass_context["pass_name"], "Crowd.Update.Animation")
            self.assertEqual(pass_context["display_name"], "Crowd / Update / Animation")
            self.assertEqual(pass_context["event_id"], 1842)
            self.assertEqual(pass_context["event_type"], "dispatch")
            self.assertEqual(pass_context["pass_type"], "compute")
            self.assertEqual(pass_context["pipeline_hash"], "0x91AF20CC")
            self.assertEqual(pass_context["shader"]["shader"], "CrowdAnimCS.compute")
            self.assertEqual(pass_context["shader"]["shader_path"], "Assets/Project/Shaders/CrowdAnimCS.compute")
            self.assertEqual(pass_context["shader"]["entry"], "CSMain")
            self.assertEqual(pass_context["shader"]["shader_hash"], "0xABCD1234")
            self.assertEqual(pass_context["shader"]["numthreads"], [64, 1, 1])
            self.assertEqual(pass_context["derived_metrics"]["dispatch_groups"], [512, 1, 1])
            self.assertEqual(pass_context["derived_metrics"]["thread_group_count"], 512)
            self.assertEqual(pass_context["derived_metrics"]["threads_per_group"], 64)
            self.assertEqual(pass_context["derived_metrics"]["total_invocations"], 32768)
            self.assertEqual(pass_context["derived_metrics"]["us_per_invocation"], 0.1)
            self.assertEqual(
                pass_context["performance_relevant_facts"]["possible_risk_tags"],
                [
                    "has_atomics",
                    "has_group_shared_memory",
                    "branch_risk:medium",
                    "memory_bandwidth",
                ],
            )
            self.assertEqual(pass_context["performance_relevant_facts"]["dominant_read_pattern"], "data_parallel_read")
            self.assertEqual(pass_context["performance_relevant_facts"]["dominant_write_pattern"], "atomic_scatter")
            self.assertEqual(pass_context["source_ids"]["shader_fact_id"], "Assets/Project/Shaders/CrowdAnimCS.compute:CSMain")
            self.assertEqual(pass_context["data_quality"]["linked"], True)
            self.assertEqual(pass_context["data_quality"]["warnings"], [])
            self.assertEqual(len(pass_context["resources"]), 2)
            self.assertNotIn("shader_facts", pass_context)
            self.assertNotIn("bindings", pass_context)
            self.assertNotIn("raw_sources", pass_context)

    def test_indirect_draw_context_links_producer_compute_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp) / "repo"
            out_dir = Path(temp) / "out"
            catalog_path = Path(temp) / "gpu_pass_catalog.json"
            catalog_path.write_text(
                json.dumps(
                    {
                        "schema_version": "1",
                        "passes": [
                            {
                                "pass_id": "crowd.visibility.cull",
                                "display_name": "Crowd / Visibility / Cull",
                                "type": "compute",
                                "owner": "crowd_system",
                                "shader": "Assets/Project/Shaders/CrowdCull.compute",
                                "kernel": "CullVisible",
                                "purpose": "Build visible instance list.",
                                "inputs": [{"name": "_Agents", "type": "StructuredBuffer<Agent>", "access": "read"}],
                                "outputs": [
                                    {
                                        "name": "_VisibleInstanceIndices",
                                        "type": "RWStructuredBuffer<uint>",
                                        "access": "write",
                                    },
                                    {
                                        "name": "_VisibleInstanceCounterBuffer",
                                        "type": "RWStructuredBuffer<uint>",
                                        "access": "read_write",
                                    },
                                ],
                                "dispatch": {"thread_group_size": [64, 1, 1]},
                                "performance_hints": {},
                                "source": {"file": "Assets/Project/Shaders/CrowdCull.compute", "function": "CullVisible"},
                                "aliases": ["Crowd.Visibility.Cull"],
                            },
                            {
                                "pass_id": "crowd.render.build_indirect_args",
                                "display_name": "Crowd / Render / Build Args",
                                "type": "compute",
                                "owner": "crowd_system",
                                "shader": "Assets/Project/Shaders/CrowdCull.compute",
                                "kernel": "BuildArgs",
                                "purpose": "Build indirect draw args from visible count.",
                                "inputs": [
                                    {
                                        "name": "_VisibleInstanceCounterBuffer",
                                        "type": "StructuredBuffer<uint>",
                                        "access": "read",
                                    }
                                ],
                                "outputs": [
                                    {
                                        "name": "_VisibleRenderArgsBuffer",
                                        "type": "RWStructuredBuffer<IndirectDrawIndexedArgsData>",
                                        "access": "write",
                                    }
                                ],
                                "dispatch": {"thread_group_size": [1, 1, 1]},
                                "performance_hints": {},
                                "source": {"file": "Assets/Project/Shaders/CrowdCull.compute", "function": "BuildArgs"},
                                "aliases": ["Crowd.Render.BuildArgs"],
                            },
                            {
                                "pass_id": "crowd.render.indirect",
                                "display_name": "Crowd / Render / Indirect",
                                "type": "indirect_draw",
                                "owner": "crowd_system",
                                "shader": "Assets/Project/Shaders/CrowdVat.shader",
                                "kernel": "UniversalForward",
                                "shader_pass": "UniversalForward",
                                "purpose": "Render visible instances from indirect args.",
                                "inputs": [
                                    {
                                        "name": "_VisibleInstanceIndices",
                                        "type": "StructuredBuffer<uint>",
                                        "access": "read",
                                    },
                                    {
                                        "name": "_VisibleRenderArgsBuffer",
                                        "type": "IndirectDrawIndexedArgs",
                                        "access": "read",
                                    },
                                    {
                                        "name": "_VatPositionTexture",
                                        "type": "Texture2D",
                                        "access": "read",
                                    },
                                ],
                                "indirect_draw": {
                                    "draw_kind": "render_mesh_indirect",
                                    "args_buffer": "_VisibleRenderArgsBuffer",
                                    "visible_instance_buffer": "_VisibleInstanceIndices",
                                },
                                "performance_hints": {},
                                "source": {"file": "Assets/Project/Shaders/CrowdVat.shader", "function": "UniversalForward"},
                                "aliases": ["Crowd.Render.Indirect"],
                            },
                        ],
                    }
                ),
                encoding="utf-8",
            )

            events_path = Path(temp) / "renderdoc_gpu_passes.raw.json"
            raw_events_input = {
                "version": 1,
                "gpu": {
                    "passes": [
                        {
                            "name": "[crowd.visibility.cull] Crowd / Visibility / Cull",
                            "event_name": "Dispatch(16, 1, 1)",
                            "type": "compute",
                            "event_id": 10,
                            "marker_path": ["[crowd.visibility.cull] Crowd / Visibility / Cull"],
                            "dispatch": [16, 1, 1],
                            "gpu_ms": 0.2,
                            "resources": {
                                "srv": [],
                                "uav": [
                                    {
                                        "slot": 0,
                                        "name": "_VisibleInstanceIndices",
                                        "resource_id": "ResourceId::visible",
                                        "resource_type": "buffer",
                                        "length": 4096,
                                    }
                                ],
                                "cbv": [],
                            },
                        },
                        {
                            "name": "[crowd.render.build_indirect_args] Crowd / Render / Build Indirect Args",
                            "event_name": "Dispatch(1, 1, 1)",
                            "type": "compute",
                            "event_id": 20,
                            "marker_path": ["[crowd.render.build_indirect_args] Crowd / Render / Build Indirect Args"],
                            "dispatch": [1, 1, 1],
                            "gpu_ms": 0.01,
                            "resources": {"srv": [], "uav": [], "cbv": []},
                        },
                        {
                            "name": "[crowd.render.indirect] Crowd / Render / Indirect",
                            "event_name": "DrawIndexedInstancedIndirect",
                            "type": "indirect_draw",
                            "event_type": "draw_indirect",
                            "event_id": 30,
                            "marker_path": ["[crowd.render.indirect] Crowd / Render / Indirect"],
                            "draw": {"num_indices": 1200, "num_instances": 64},
                            "indirect": True,
                            "gpu_ms": 0.4,
                            "pipeline": {
                                "vs": "CrowdVatVertex",
                                "ps": "CrowdVatFragment",
                                "blend": "off",
                                "zwrite": True,
                                "ztest": "LessEqual",
                            },
                            "draw_resources": {
                                "vertex_inputs": [],
                                "structured_buffers": ["_VisibleInstanceIndices"],
                                "textures": ["_VatPositionTexture"],
                                "render_targets": ["camera_color", "camera_depth"],
                            },
                            "resources": {
                                "srv": [
                                    {
                                        "slot": 0,
                                        "name": "_VisibleInstanceIndices",
                                        "resource_id": "ResourceId::visible",
                                        "resource_type": "buffer",
                                        "length": 4096,
                                    },
                                    {
                                        "slot": 1,
                                        "name": "_VatPositionTexture",
                                        "resource_id": "ResourceId::vat",
                                        "resource_type": "texture",
                                        "width": 1024,
                                        "height": 1024,
                                    },
                                ],
                                "uav": [],
                                "cbv": [],
                            },
                        },
                    ]
                },
            }
            events_path.write_text(json.dumps(raw_events_input), encoding="utf-8")

            subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--repo",
                    str(repo),
                    "--events-in",
                    str(events_path),
                    "--catalog",
                    str(catalog_path),
                    "--artifact-dir",
                    str(out_dir),
                    "--no-fetch-events",
                ],
                text=True,
                capture_output=True,
                check=True,
            )

            overview = json.loads((out_dir / "gpu_pass_overview.json").read_text(encoding="utf-8"))
            draw_overview = next(event for event in overview["events"] if event["event_id"] == 30)
            self.assertEqual(draw_overview["event_type"], "draw_indirect")
            self.assertEqual(draw_overview["pass_type"], "indirect_draw")
            self.assertEqual(draw_overview["matched_pass_id"], "crowd.render.indirect")
            self.assertEqual(draw_overview["pipeline"]["vs"], "CrowdVatVertex")
            self.assertEqual(draw_overview["resources"]["render_targets"], ["camera_color", "camera_depth"])

            resources = json.loads((out_dir / "gpu_pass_resources.json").read_text(encoding="utf-8"))
            self.assertIn(
                {
                    "name": "_VisibleRenderArgsBuffer",
                    "hlsl_type": "RWStructuredBuffer<IndirectDrawIndexedArgsData>",
                    "source": "catalog",
                    "producer_passes": ["crowd.render.build_indirect_args"],
                    "consumer_passes": ["crowd.render.indirect"],
                },
                resources["resource_usages"],
            )
            self.assertIn(
                {
                    "name": "_VisibleInstanceIndices",
                    "hlsl_type": "RWStructuredBuffer<uint>",
                    "source": "catalog",
                    "producer_passes": ["crowd.visibility.cull"],
                    "consumer_passes": ["crowd.render.indirect"],
                },
                resources["resource_usages"],
            )

            context = json.loads((out_dir / "gpu_frame_context.json").read_text(encoding="utf-8"))
            draw_context = next(pass_context for pass_context in context["gpu_passes"] if pass_context["event_id"] == 30)
            self.assertEqual(draw_context["pass_type"], "indirect_draw")
            self.assertEqual(draw_context["event_type"], "draw_indirect")
            self.assertEqual(draw_context["resources"]["structured_buffers"], ["_VisibleInstanceIndices", "_VisibleRenderArgsBuffer"])
            self.assertIn(
                {
                    "pass_id": "crowd.render.build_indirect_args",
                    "resource": "_VisibleRenderArgsBuffer",
                    "hlsl_type": "RWStructuredBuffer<IndirectDrawIndexedArgsData>",
                    "source": "catalog",
                },
                draw_context["resource_flow"]["inputs_from"],
            )
            self.assertIn(
                {
                    "pass_id": "crowd.visibility.cull",
                    "resource": "_VisibleInstanceIndices",
                    "hlsl_type": "RWStructuredBuffer<uint>",
                    "source": "catalog",
                },
                draw_context["resource_flow"]["inputs_from"],
            )
            self.assertIn(
                {
                    "from": "crowd.render.build_indirect_args",
                    "to": "crowd.render.indirect",
                    "resource": "_VisibleRenderArgsBuffer",
                    "size_bytes": None,
                },
                context["pass_graph"]["edges"],
            )

    def test_parse_shader_file_resolves_includes_and_reports_original_locations(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp) / "repo"
            shader_dir = repo / "Assets" / "Project" / "Shaders"
            shader_dir.mkdir(parents=True)
            main_path = shader_dir / "CrowdMain.compute"
            common_path = shader_dir / "CrowdCommon.hlsl"
            kernel_path = shader_dir / "CrowdAnimKernels.hlsl"
            main_path.write_text(
                """
#pragma kernel CSMain
#include "Assets/Project/Shaders/CrowdCommon.hlsl"
#include "CrowdAnimKernels.hlsl"
""",
                encoding="utf-8",
            )
            common_path.write_text(
                """
StructuredBuffer<uint> CrowdStateBuffer : register(t0);
RWStructuredBuffer<uint> CrowdBoneBuffer : register(u0);
""",
                encoding="utf-8",
            )
            kernel_path.write_text(
                """
[numthreads(64, 1, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint entityId = CrowdStateBuffer[dispatchThreadId.x];
    CrowdBoneBuffer[entityId] = entityId;
}
""",
                encoding="utf-8",
            )

            module = load_script_module()
            parsed = module.parse_shader_file(main_path, repo)

            entry = parsed["entries"][0]
            self.assertEqual(entry["entry"], "CSMain")
            self.assertEqual(entry["source_location"], "Assets/Project/Shaders/CrowdAnimKernels.hlsl:2")
            self.assertEqual(parsed["resources_declared"][0]["source_location"], "Assets/Project/Shaders/CrowdCommon.hlsl:2")
            self.assertTrue(
                any(
                    record.get("resource") == "CrowdBoneBuffer" and
                    record.get("access") == "write" and
                    record.get("source_location") == "Assets/Project/Shaders/CrowdAnimKernels.hlsl:6"
                    for record in entry["resource_accesses"]
                )
            )

    def test_parse_shader_file_scopes_entry_resources_and_follows_helper_accesses(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp) / "repo"
            shader_dir = repo / "Assets" / "Project" / "Shaders"
            shader_dir.mkdir(parents=True)
            shader_path = shader_dir / "CrowdMain.compute"
            shader_path.write_text(
                """
StructuredBuffer<uint> AliveBuffer : register(t0);
StructuredBuffer<uint> DeathStateBuffer : register(t1);
StructuredBuffer<uint> UnusedBuffer : register(t2);
RWStructuredBuffer<uint> OutputBuffer : register(u0);

bool IsDead(uint instanceIndex)
{
    return DeathStateBuffer[instanceIndex] != 0u;
}

[numthreads(64, 1, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex = dispatchThreadId.x;
    if (IsDead(instanceIndex))
        return;

    OutputBuffer[instanceIndex] = AliveBuffer[instanceIndex];
}
""",
                encoding="utf-8",
            )

            module = load_script_module()
            parsed = module.parse_shader_file(shader_path, repo)

            entry = parsed["entries"][0]
            self.assertEqual(
                {record["name"] for record in entry["resources_declared"]},
                {"AliveBuffer", "DeathStateBuffer", "OutputBuffer"},
            )
            self.assertEqual(
                {record["name"] for record in parsed["resources_declared"]},
                {"AliveBuffer", "DeathStateBuffer", "UnusedBuffer", "OutputBuffer"},
            )
            self.assertEqual(
                {record["resource"] for record in entry["resource_accesses_direct"]},
                {"AliveBuffer", "OutputBuffer"},
            )
            self.assertTrue(
                any(
                    record.get("resource") == "DeathStateBuffer" and
                    record.get("access_scope") == "indirect" and
                    record.get("source_function") == "IsDead"
                    for record in entry["resource_accesses"]
                )
            )
            self.assertTrue(
                any(
                    record.get("resource") == "AliveBuffer" and
                    record.get("access_scope") == "direct" and
                    record.get("source_function") == "CSMain"
                    for record in entry["resource_accesses"]
                )
            )
            public_fact = module.serialize_shader_fact_public(
                {
                    "shader": "CrowdMain.compute",
                    "entry": entry["entry"],
                    "stage": entry["stage"],
                    "thread_group": entry["thread_group"],
                    "resources_declared": entry["resources_declared"],
                    "resource_accesses": entry["resource_accesses"],
                    "loops": entry["loops"],
                    "branches": entry["branches"],
                    "barriers": entry["barriers"],
                    "atomics": entry["atomics"],
                    "groupshared": entry["groupshared"],
                }
            )
            self.assertEqual(
                {record["name"] for record in public_fact["resources_declared"]},
                {"AliveBuffer", "DeathStateBuffer", "OutputBuffer"},
            )
            self.assertTrue(
                any(
                    record.get("name") == "DeathStateBuffer" and
                    record.get("declared_capability") == "read_only" and
                    record.get("actual_access") == ["read"]
                    for record in public_fact["resources_declared"]
                )
            )
            self.assertNotIn("UnusedBuffer", {record["name"] for record in public_fact["resources_declared"]})
            self.assertNotIn("resource_accesses_direct", public_fact)

    def test_build_constants_view_hides_unknown_layout_values_from_ai(self) -> None:
        module = load_script_module()
        constants = module.build_constants_view(
            [
                {
                    "slot": "b0",
                    "logical_name": "CrowdParams",
                    "shader_symbol": "cbuffer0",
                    "type": "CBV",
                    "size_bytes": 16,
                }
            ],
            {
                "cbuffers_by_slot": {
                    "b0": {
                        "variables": [
                            {"name": "cb0_v0", "declared_type": "float", "value": 1.4349296274686127e-42},
                            {"name": "cb0_v1", "declared_type": "float", "value": 1.401298464324817e-45},
                            {"name": "cb0_v2", "declared_type": "float", "value": -2.9261550903320312},
                            {"name": "cb0_v3", "declared_type": "float", "value": 9.682972388484486e-43},
                        ]
                    }
                }
            },
        )
        self.assertEqual(
            constants,
            {
                "decode_status": "unknown_layout",
                "raw_values_hidden_from_ai": True,
            },
        )

    def test_build_merged_resources_does_not_duplicate_combined_runtime_renderdoc_binding(self) -> None:
        module = load_script_module()
        merged, unresolved = module.build_merged_resources_for_event(
            [
                {
                    "slot": "u2",
                    "logical_name": "Buffer-16-4",
                    "renderdoc_name": "Buffer-16-4",
                    "type": "UAV",
                    "resource_type": "buffer",
                    "size_bytes": 4,
                    "actual_access": ["atomic_add"],
                    "source": "renderdoc",
                },
                {
                    "slot": "u2",
                    "logical_name": "aliveInstanceCounter",
                    "shader_symbol": "_AliveInstanceCounterBuffer",
                    "renderdoc_name": "Buffer-16-4",
                    "type": "UAV",
                    "resource_type": "RWStructuredBuffer<uint>",
                    "size_bytes": 4,
                    "stride_bytes": 4,
                    "element_count": 1,
                    "source": "renderdoc+runtime_sidecar",
                }
            ],
            {
                "resources_declared": [
                    {
                        "name": "_AliveInstanceCounterBuffer",
                        "type": "RWStructuredBuffer<uint>",
                        "bind_type": "UAV",
                        "slot": "u2",
                    }
                ],
                "resource_accesses": [
                    {
                        "resource": "_AliveInstanceCounterBuffer",
                        "access": "write",
                    }
                ],
                "atomics": [
                    {
                        "function": "InterlockedAdd",
                        "arguments": "_AliveInstanceCounterBuffer[0], 1u, writeIndex",
                    }
                ],
            },
            {},
        )
        self.assertEqual(len(merged), 1)
        self.assertEqual(unresolved, [])
        self.assertEqual(merged[0]["slot"], "u2")
        self.assertEqual(merged[0]["logical_name"], "aliveInstanceCounter")
        self.assertEqual(merged[0]["renderdoc_name"], "Buffer-16-4")
        self.assertEqual(merged[0]["actual_access"], ["atomic_add"])
        self.assertEqual(merged[0]["sources"], ["runtime_sidecar", "renderdoc", "shader_reflection"])

    def test_build_pass_graph_uses_runtime_resource_edges(self) -> None:
        module = load_script_module()
        pass_graph = module.build_pass_graph(
            [
                {"pass_name": "Crowd.Update.BuildAliveList"},
                {"pass_name": "Crowd.Update.BuildAliveDispatchArgs"},
            ],
            [
                {
                    "logical_name": "AliveCountBuffer",
                    "resource_type": "RWStructuredBuffer<uint>",
                    "byte_size": 4,
                    "stride": 4,
                    "element_count": 1,
                    "producer_passes": ["Crowd.Update.BuildAliveList"],
                    "consumer_passes": ["Crowd.Update.BuildAliveDispatchArgs"],
                }
            ],
        )
        self.assertEqual(
            pass_graph["edges"],
            [
                {
                    "from": "Crowd.Update.BuildAliveList",
                    "to": "Crowd.Update.BuildAliveDispatchArgs",
                    "resource": "AliveCountBuffer",
                    "resource_type": "RWStructuredBuffer<uint>",
                    "size_bytes": 4,
                    "stride_bytes": 4,
                    "element_count": 1,
                }
            ],
        )


if __name__ == "__main__":
    unittest.main()
