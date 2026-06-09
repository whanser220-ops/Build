# RenderDoc CLI D3D12 外部抓帧工作流

## 文档信息

- 版本：v0.15
- 日期：2026-06-01
- 状态：当前主工作流；正式离线分析已收敛到 rdoc-agent qrenderdoc CLI。`tools/renderdoc-cli/renderdoc_gpu_context_cli.py` 仅保留为 Crowd/VAT 专用隐藏入口，不用于普通 MeshRenderer + Material 抓帧分析。
- 同步级别：强同步
- 适用项目：Unity 6 `6000.0.46f1`
- 当前实现锚点：
  - `Assets/Project/Tools/Performance/Editor/RenderDocCaptureWindow.cs`
  - `Assets/Project/Tools/Performance/Runtime/RenderDocCaptureController.cs`
  - `Assets/Project/Tools/Performance/Runtime/RenderDocCaptureBootstrap.cs`
  - `Assets/Project/Tools/Performance/Runtime/InGamePerformanceMonitor.cs`
  - `tools/renderdoc-cli/rdoc_agent.py`
  - `tools/renderdoc-cli/rdoc_agent.py`
- 最后实现核对：2026-06-01

## 目标

把原来的“Editor 内部热抓 / attach 多入口”收敛成“固定 Development Player + renderdoccmd launcher + Player 热键抓帧 + Codex 分析提示词”的最小链路。

这条链路的重点是：

1. Unity Editor 只负责构建固定 D3D12 Development Player
2. Unity Editor 执行 `renderdoccmd capture` 启动命令
3. 用户在 Player 运行到目标场所时按 `左 Shift + F8`
4. 抓帧完成后自动落盘最小必要 artifact
5. Codex 通过 `renderdoc-cli` Skill 调用 `tools/renderdoc-cli/rdoc_agent.py`，基于 RenderDoc 原生 event / draw / timing / pipeline state / resource 证据和本地代码上下文分析

## 入口

- `Assets/Project/Tools/Performance/Editor/RenderDocCaptureWindow.cs`
- `Assets/Project/Tools/Performance/Runtime/RenderDocCaptureController.cs`
  - `Assets/Project/Tools/Performance/Runtime/RenderDocCaptureBootstrap.cs`
  - `Assets/Project/Tools/Performance/Shared/GpuPassDebugInfo.cs`
  - `Assets/Project/Tools/Performance/Shared/GpuPassBindingDebugInfo.cs`
  - `tools/renderdoc-cli/rdoc_agent.py`
  - `tools/renderdoc-cli/rdoc_agent.py`

## 使用步骤

1. 打开 Unity 菜单 `Tools/Qianxia/RenderDoc Capture/Open Window`。
2. 点击 `Build Fixed Player`，固定导出路径为：

```text
C:\unity\Unity6\.workspace\artifacts\builds\renderdoc-d3d12\QianxiaRenderDocD3D12.exe
```

3. 点击 `Launch Under RenderDoc` 执行命令行。
4. 进入 Player，跑到目标场景或目标场所。
5. 在 Player 窗口里按 `左 Shift + F8`。
6. 回到 Unity 窗口，按需填写 `Analyze What`，点击 `Copy Latest Codex Prompt`。
7. 在 Codex 对话里粘贴提示词，由 Codex 使用 `[$renderdoc-cli](C:\Users\huang\.codex\skills\renderdoc-cli\SKILL.md)` Skill、`.rdc` 路径和同次导出的 AI Debug runtime 数据继续分析。

## 成功后的产物

抓帧成功后，artifact 会写到：

```text
C:\unity\Unity6\.workspace\artifacts\renderdoc-captures
```

每次成功抓帧至少会有：

- `.rdc`
- `gameview.png`

当前工作流不再生成：

- `capture_context.md`
- `gpu_pass_debug_map.json`
- `gpu_pass_bindings.json`
- 上下文 sidecar JSON
- 其他 provider `.json`

说明：Crowd/VAT 这类路径仍可在 `.rdc` 中保留显式写入的 `BeginSample` / `ProfilingScope` / `CommandBuffer` marker / 自定义 draw label，正式分析通过 `rdoc-agent` 读取这些事件证据，并按需配对 AI Debug runtime 旁证；抓帧阶段不再为其额外落盘 GPU Pass sidecar。普通 MeshRenderer + Material 绘制会自动进入 URP 默认渲染流程，通常没有可维护的业务 label，不应强行进入 GPU Pass Catalog 分析链路。

进入 Codex 分析阶段后，正式流程通过 `tools/renderdoc-cli/rdoc_agent.py` 读取当前 `.rdc`：

```bash
python tools/renderdoc-cli/rdoc_agent.py capture info --capture "<capture.rdc>" --json
python tools/renderdoc-cli/rdoc_agent.py events overview --capture "<capture.rdc>" --json
python tools/renderdoc-cli/rdoc_agent.py events expand --capture "<capture.rdc>" --event <eventId> --depth 1 --json
python tools/renderdoc-cli/rdoc_agent.py find --capture "<capture.rdc>" --q "<name>" --scope event:<eventId>.branches --json
python tools/renderdoc-cli/rdoc_agent.py entity list --capture "<capture.rdc>" --from-search <searchId> --json
python tools/renderdoc-cli/rdoc_agent.py entity match-prefabs --capture "<capture.rdc>" --from-search <searchId> --prefab-signatures "<capture-folder>/unity_prefab_signatures.sqlite" --json
```

如果需要从 Mesh、Material、Shader 或 Texture 名称反查 draw，先通过 `events expand` 选择目标 root，再使用 `rdoc-agent find --scope event:<eventId>.branches` 显式搜索该 root 的各直接 child 子树，并取得 `searchId` 与命中 event id 集。`find` 优先用 RenderDoc resource usage 索引处理资源名命中，只有索引没有结果时才回退到逐 leaf 的轻量 pipeline 名称扫描。随后用 `entity list --from-search` 把本质相同的 capture-visible draw/event signature 聚合成 `entities[]`，比较实体级 GPU 时间，并从最高耗时实体取得 `representativeEventId`；如果 capture 同目录存在 `unity_prefab_signatures.sqlite`，`entity list` 会同时输出 `prefabTimingSummary`，包含 Prefab 总 GPU 时间和不同 LOD 的 GPU 时间。当前 `rdoc-agent` 到这里为止，不再提供 `events inspect` 这类单 event 深挖动作；后续如需 event 级 pipeline/resource 细节，改走手动 qrenderdoc 回查或后续专用工具。`entity list` 不读取 `unity_prefab_metadata.json`。

同次回放/抓帧还会在 `.workspace/artifacts/ai-debug/` 下导出 AI Debug runtime 旁证：

- `crowd_ai_debug_*.jsonl`
- `crowd_ai_debug_*.md`
- `external-player-profile.json`
- `repro-traces/crowd_ai_repro_*.json`

这些数据不替代 RenderDoc event timing，只用于解释抓帧附近的 runtime workset、候选实例、`gpu.stage`、`frame.dispatch`、debug buffer 和 replay trace。Codex 应通过 `tools/ai-debug-cli/crowd_ai_debug_query.py` 分层查询，不默认全文打开 JSONL。

## Unity Editor 窗口职责

窗口只保留以下可见操作：

- 构建固定 D3D12 Development Player。
- 执行 `renderdoccmd capture` 启动命令。
- 打开最新抓帧目录。
- 根据最新 `.rdc`、`gameview.png`、必需 Unity Prefab metadata、可选 Unity Material metadata、AI Debug runtime 旁证路径、`Analyze What` 和“配合本地代码分析”要求生成 Codex 提示词。

窗口不再显示环境路径、命令预览和额外 Player 参数；也不再提供 Editor PlayMode 热抓、PID attach/inject、qrenderdoc 打开和 MCP 扩展安装入口。

## 运行时行为

- 如果场景里没有手动放置 `RenderDocCaptureController`，运行时会自动创建一个全局 controller。
- External Player 启动时会自动带上 `-force-d3d12`、`--renderdoc-artifacts-root`、`--renderdoc-label` 和可用时的 `--renderdoc-dll` 参数，把产物写回仓库里的 `.workspace/artifacts/renderdoc-captures`。
- 抓帧失败时不应再留下一个误导性的孤立 `png` 目录。

## 为什么这么改

`RenderDoc` 本身支持 `D3D12`，但 Unity Editor 内部热抓在这条路径上不够稳定。改成 External Player 后：

- 更接近真实运行时
- 更适合 D3D12
- 更容易与最终 AI 分析链路对齐

## AI 分析约定

AI 直接读取或按需查询：

1. `.rdc`
2. `gameview.png`
3. 可选的 `unity_prefab_metadata.json` 与 `unity_prefab_signatures.sqlite`
4. 按需手动导出的 `unity_material_metadata.json`
5. `.workspace/artifacts/ai-debug/crowd_ai_debug_*.jsonl`
6. `.workspace/artifacts/ai-debug/crowd_ai_debug_*.md`
7. `.workspace/artifacts/ai-debug/repro-traces/crowd_ai_repro_*.json`

不要求旧式上下文 JSON。

提示词必须要求 Codex 使用 `[$renderdoc-cli](C:\Users\huang\.codex\skills\renderdoc-cli\SKILL.md)` Agent Skill，并默认走 rdoc-agent qrenderdoc CLI：

```bash
python tools/renderdoc-cli/rdoc_agent.py capture info --capture "<capture.rdc>" --json
python tools/renderdoc-cli/rdoc_agent.py events overview --capture "<capture.rdc>" --json
python tools/renderdoc-cli/rdoc_agent.py events expand --capture "<capture.rdc>" --event <eventId> --depth 1 --json
python tools/renderdoc-cli/rdoc_agent.py find --capture "<capture.rdc>" --q "<name>" --scope event:<eventId>.branches --json
python tools/renderdoc-cli/rdoc_agent.py entity list --capture "<capture.rdc>" --from-search <searchId> --json
python tools/renderdoc-cli/rdoc_agent.py entity match-prefabs --capture "<capture.rdc>" --from-search <searchId> --prefab-signatures "<capture-folder>/unity_prefab_signatures.sqlite" --json
```

然后先使用 `rdoc-agent find --scope event:<eventId>.branches` 按 Mesh / Material / Shader / Texture 名称跨直接 child 分支建立 event evidence search，再使用 `entity list` 的 `entities[]`、`aggregationSummary` 与可选的 `prefabTimingSummary` 判断真正高耗时的 capture-visible 实体和 Prefab/LOD；需要完整候选细节时，用 `entity match-prefabs` 查询 SQLite 的 `prefab_draw_signatures` 表。当前 `rdoc-agent` 不再提供该实体的单 event pipeline / texture binding / CBV / UAV / descriptor range 深挖动作；如需这些细节，只记录 `representativeEventId` 供 qrenderdoc 或后续专用工具回查。名称检索统一使用 `find`，不再使用已移除的 `events search`。不通过文件名或 GUID 搜索猜 Unity 资产路径。不要默认运行 `tools/renderdoc-cli/renderdoc_gpu_context_cli.py`，也不要把普通 MeshRenderer + Material 的默认渲染事件强行映射成 Crowd 风格 `pass_id`。

如果存在同次 AI Debug 导出，提示词还必须要求 Codex 通过以下命令建立 runtime 认知地图：

```bash
python tools/ai-debug-cli/crowd_ai_debug_query.py --input "<配对的 crowd_ai_debug_*.jsonl>" session map
```

后续按需组合 `discovery overview`、`frame inspect`、`agent timeline`、`stage phases`、`resource overview`、`raw show` 和 `raw grep`。这些 AI Debug 数据只在 Crowd/VAT 相关问题中作为 runtime 旁证；结论中要明确区分“RenderDoc GPU 证据”和“AI Debug runtime 旁证”，并说明 JSONL 与 `.rdc` 的配对依据；无法可靠配对时只能作为弱旁证。

分析前需要先确认：

1. `qrenderdoc.exe` 可用，或通过 `RENDERDOC_QRENDERDOC` 指向可执行文件。
2. 目标 `.rdc` 可以通过 `rdoc_agent.py capture info --capture "<capture.rdc>" --json` 读取基本信息。
3. 如果本次明确是 Crowd/VAT 分析，再确认 `.workspace/artifacts/ai-debug/crowd_ai_debug_*.jsonl` 是否能与本次抓帧配对，并直接从 `.rdc` 读取可用 marker。

这条分析链路直接使用 `qrenderdoc.exe --python` 读取 `.rdc`。仓库内的 rdoc-agent 入口是：

```bash
python tools/renderdoc-cli/rdoc_agent.py capture info --capture "<capture.rdc>" --json --output capture_info.json
```

该入口输出统一 JSON envelope，是当前普通 `.rdc` 离线分析主路径。`tools/renderdoc-cli/renderdoc_gpu_context_cli.py` 仅在明确分析 Crowd marker 与 AI Debug 旁证时由人工点名使用。
## 2026-05-15 外部 Player 复现场景联动补充

- `Build Fixed Player` 现在会同时准备外部 Player 启动脚本 `Launch-Under-RenderDoc.cmd`，并在 `.workspace/artifacts/ai-debug/` 下确保 AI Debug 轨迹目录存在。
- 如果 Unity 编辑器内构建命中已知的 Bee 瞬时故障：`Failed to open file "Library/Bee/tundra.log.json" for structured logging`，`Build Fixed Player` 会等待残留 `bee_backend` 退出、清理 Bee 临时日志，并自动重试一次，避免把这类短暂锁冲突直接暴露给用户。
- External Player 启动参数新增：
- `--crowd-ai-repro-mode auto`
- `--crowd-ai-build-stamp-path <build-dir>/external-player-build-stamp.txt`
- `--crowd-ai-trace-directory <repo>/.workspace/artifacts/ai-debug/repro-traces`
- `--crowd-ai-settings-path <repo>/.workspace/artifacts/ai-debug/external-player-profile.json`
- `auto` 模式的行为：
- `Build Fixed Player` 会在外部 Player 目录写出 `external-player-build-stamp.txt`；`auto` 模式用 build stamp 判断 trace 是否属于当前 build，而不是依赖 `QianxiaRenderDocD3D12.exe` 的修改时间。
- 如果当前 build stamp 之后还没有新的 repro trace，则本次启动自动进入“手动跑 + 录轨迹”模式。
- 当用户在 Player 里按 `Left Shift + F8` 完成第一次 RenderDoc 抓帧后，运行时会把本次角色/相机轨迹与小队命令自动保存到 `.workspace/artifacts/ai-debug/repro-traces/`。
- 如果已经存在比当前 build stamp 更新的 trace，则下次启动自动进入“回放最近轨迹 + AI Debug”模式，并在热点前按 profile 里的 lead time 自动开启 AI Debug 录制。
- 这样就能保持原操作不变：
- 第一次仍然是手动跑到热点并抓一帧，同时顺手录下复现场景轨迹。
- 第二次仍然通过同一个 `Launch-Under-RenderDoc.cmd` 启动，但 Player 会自动复跑最近轨迹，并导出 AI Debug 证据。
- 默认 profile 会使用 `GpuDiscovery + ScreenRect(0..1)` 覆盖当前主相机画面；如果后续需要更窄的 AI Debug 目标，可以直接调整 `external-player-profile.json`。

## 2026-05-21 Build Player 底部性能监控条补充

- 外部 Development Player 现在会在运行时自动创建 `InGamePerformanceMonitor`，屏幕底部显示一条半透明 in-game 性能监控条，用于手动跑热点时快速判断当前帧是否已经进入目标性能区间。
- `Launch Under RenderDoc` 和 `Launch-Under-RenderDoc.cmd` 都会显式传入 `--in-game-perf-monitor`；普通非 Editor Player 也会默认启用。需要录制干净画面时，可通过 `--disable-in-game-perf-monitor` 禁用。
- 监控条显示 FPS、平均帧时、最差帧时、Main Thread、Render Thread、GPU、Draw/Batches/SetPass、GC/frame 与内存摘要；宽度不足时会自动收起后半段计数器。
- 该监控条只承担现场观察职责，不替代 `PerformancePoiSampler` 的落盘采样、Profiler capture 或 RenderDoc event timing。正式性能结论仍以 POI sampler、Unity Profiler 和 RenderDoc 事实包为准。
- 运行时可按 `F9` 隐藏或恢复监控条；隐藏只影响显示，不会销毁采样对象。
## 2026-05-29 补充：Unity Prefab metadata 与实体聚合边界

D3D12 `.rdc` 中 Unity Material、Texture 和 RenderGraph render target 名称不是稳定事实来源；当前普通 MeshRenderer + Material 分析不再从 RenderDoc 原始名称硬猜这些身份。

需要 ANGRY MESH Prefab 证据时，先在 RenderDoc Capture 窗口点击 `Export ANGRY MESH Metadata`，同目录会生成可选的 `unity_prefab_metadata.json`、可选的 `unity_material_metadata.json`，以及 Unity 侧直接写出的 `unity_prefab_signatures.sqlite`。其中 JSON prefab metadata 仍是资产清单；SQLite signature 来自当前加载场景中的 Prefab 实例。材质清单仍可用 CLI 独立导出：

```powershell
.\tools\renderdoc-cli\rdoc-agent.cmd unity materials `
  --capture "<capture.rdc>" `
  --shader-root "Assets/ANGRY MESH" `
  --material-root Assets `
  --metadata-output "<capture-folder>\unity_material_metadata.json" `
  --json --pretty
```

需要给后续 Agent 查询工具准备 SQL 索引时，优先使用 Unity 直接导出的 `unity_prefab_signatures.sqlite`，不需要先落 `unity_prefab_metadata.json` 再二次转换。也可以单独运行 Unity 菜单 `Tools/Qianxia/RenderDoc Capture/Export ANGRY MESH Prefab SQLite`，或在 batchmode 调 `RenderDocUnityPrefabMetadataExporter.ExportAngryMeshPrefabSignaturesForBatchmode --unity-prefab-signatures-output <path>`。该导出会遍历当前加载场景中的 `Assets/ANGRY MESH` Prefab 实例，而不是扫描整套 Prefab 资产库。

该数据库的主表为 `prefab_draw_signatures`，字段为 `id`、`prefab_path`、`prefab_name`、`prefab_guid`、`scene_path`、`scene_name`、`scene_instance_path`、`scene_instance_name`、`prefab_lod_key`、`prefab_inner_path`、`scene_renderer_path`、`lod_index`、`mesh_name`、`mesh_base_name`、`texture_names`；其中 `texture_names` 是 JSON 数组字符串，`prefab_lod_key` 用于表达同一 Prefab 下不同 LOD 的组织关系。`entity list` 读取它来输出 Prefab 候选与 `prefabTimingSummary`；SQL 索引只作为 Unity 场景实例侧证据查询层，不替代 RenderDoc draw 事实。

`unity_material_metadata.json` 是独立材质清单，不由 `entity list` 用来连接 Prefab。`entity list` 读取 `.rdc` 与 `find` event evidence，输出 `entityCount`、`entities[]` 与 `aggregationSummary`；存在 SQLite 时再输出 Prefab candidates 与 `prefabTimingSummary`。`entity match-prefabs` 仍可单独读取 SQLite 输出候选细节。当前 `rdoc-agent` 不再提供 event 级 descriptor/resource 与 shader 细节读取动作；未经 SQLite 候选和人工/后续工具确认，不把 RenderDoc draw 断言为唯一 Unity 资产路径。

## 2026-06-04 补充：RDC 事实库 schema

后续如果需要把 `.rdc` 导入数据库做批量查询、跨 capture 对比或报表统计，使用 `tools/renderdoc-cli/sql/rdc_capture_fact_schema.sql` 作为 PostgreSQL 初始 schema，设计说明见 `docs/plans/renderdoc-rdc-fact-database-design.md`。本地 SQLite 验证导入入口是 `tools/renderdoc-db/rdc_capture_fact_import.py`，它直接启动专用 qrenderdoc helper 读取 `.rdc`，不调用 `rdoc-agent`。

这套表结构当前只保留 `capture`、`draw_event`、`rd_resource` 和 `event_resource_binding` 四张表。`draw_event.marker_path` 是后续归因到 Unity 场景、Prefab 或 Renderer 的关键字段；如果 Unity 侧能写入稳定 GPU marker，优先从 marker path 做归因，而不是依赖 RenderDoc resource name。

当前主工作流仍是 `rdoc-agent session` 按需查询 `.rdc`，不是默认全量落库。只有在明确要做离线导入或批量索引时，才运行 `tools/renderdoc-db/rdc_capture_fact_import.py`。
 
## 2026-06-05 补充：截帧后自动物化数据库

外部 Player 完成 RenderDoc 截帧后，如果后续要让 Agent 通过 Prefab 名称查询 GPU 耗时，应立即物化对应 fact DB 并导入 Prefab 表。当前推荐入口：

```powershell
python tools\renderdoc-db\rdc_capture_postprocess.py `
  --capture "<capture-folder>\capture_frame2051.rdc" `
  --sqlite ".workspace\artifacts\renderdoc-analysis\rdc-fact-database\rdc_capture_four_table.sqlite" `
  --prefab-signatures "<capture-folder>\unity_prefab_signatures.sqlite" `
  --json --pretty
```

Unity Editor 的 RenderDoc Capture 窗口新增 `Export Latest Fact DB + Prefabs`，会对最新截帧刷新 `unity_prefab_signatures.sqlite`，后台启动同一脚本，并把结果写到 capture 目录的 `rdc_fact_postprocess_result.json`。完成后，Prefab 查询走 `rdoc-agent.cmd db instance-cost --prefab-name <PrefabName>`，不再让 Agent 先失败一次再补导入。
