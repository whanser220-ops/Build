# RenderDoc CLI 性能采样工具设计

## 文档信息

- 版本：v0.18
- 日期：2026-06-01
- 状态：正式离线分析默认走 rdoc-agent qrenderdoc CLI；`renderdoc_gpu_context_cli.py` 已收敛为 Crowd/VAT 专用隐藏入口。当前在抓帧链路前新增“场景 POI 批量稳态采样”层，GPU 事件语义对齐只在 `.rdc` 中存在显式 Crowd marker 时使用。
- 同步级别：强同步
- 适用项目：Unity 6 `6000.0.46f1`
- 适用平台：Windows Development Player，优先 `D3D12`
- 当前实现锚点：
  - `Assets/Project/Tools/Performance/Runtime/RenderDocCaptureController.cs`
  - `Assets/Project/Tools/Performance/Runtime/PerformancePoiPoint.cs`
  - `Assets/Project/Tools/Performance/Runtime/PerformancePoiSampler.cs`
  - `Assets/Project/Tools/Performance/Runtime/PerformancePoiBootstrap.cs`
  - `Assets/Project/Tools/Performance/Runtime/InGamePerformanceMonitor.cs`
  - `Assets/Project/Tools/Performance/Shared/RenderDocCaptureArtifacts.cs`
  - `Assets/Project/Tools/Performance/Shared/PerformancePoiSamplingArtifacts.cs`
  - `Assets/Project/Tools/Performance/Shared/GpuPassMarkerScope.cs`
  - `Assets/Project/Tools/Performance/Shared/GpuPassDebugInfo.cs`
  - `Assets/Project/Tools/Performance/Shared/GpuPassBindingDebugInfo.cs`
  - `Assets/Project/Tools/Performance/Editor/RenderDocCaptureWindow.cs`
  - `Assets/Project/Tools/Performance/Editor/PerformancePoiEditors.cs`
  - `tools/renderdoc-cli/validate_gpu_pass_catalog.py`
  - `tools/renderdoc-cli/rdoc_agent.py`
  - `tools/renderdoc-cli/rdoc_agent.py`
  - `docs/gpu-pass-catalog/gpu_pass_catalog.yaml`
- 最后实现核对：2026-06-01

## 目标

这套工具的目标不是单纯“按一下抓一帧”，而是建立一条可复盘的 GPU 证据链：

1. 在游戏运行到目标场景时抓到一份 `RenderDoc` 捕获。
2. 同时保存最少但够用的视觉对照，并把可检索的 GPU 事件语义保留在 `.rdc` 中。
3. 让 AI 基于 `.rdc + gameview.png` 做分析；默认通过 rdoc-agent qrenderdoc CLI 渐进读取 event / draw / timing / pipeline state / resource 事实，而不是一次性生成完整上下文包。
4. 对普通 MeshRenderer + Material，直接沿 RenderDoc 默认 event/draw 流程查 shader、材质、pipeline state、texture 和 buffer，不要求业务 label。
5. 只有 Crowd/VAT 这类已经显式写入 `BeginSample` / `ProfilingScope` / `CommandBuffer` marker / 自定义 draw label 的路径，才额外使用 GPU Pass Catalog 和 AI Debug 旁证做稳定 `pass_id` 对齐。
6. 当 ComputeShader 或 GPU marker 注册改变时，使用 `python tools/renderdoc-cli/validate_gpu_pass_catalog.py --repo .` 校验长期文档是否同步。

## 当前结论

### 2026-05-20 实现更新

- POI authoring 已从“摆相机 pose”切换为“录一次角色/相机操作轨迹，再在采样时完整回放”
- `PerformancePoiPoint` 现在主要承担点位命名、分类、采样覆盖和 trace 路径归属，不再要求用 Scene Gizmo author 相机位置与朝向
- 轨迹录制入口位于 `Tools/Qianxia/Performance/POI Sampler`，只记录角色与相机的时序位姿，不包含小队命令
- 在 `POI Sampler` 中选中目标 POI 后，进入 Play Mode 可直接按 `Shift+1` 开始录制，按 `Shift+2` 结束并保存
- 轨迹文件落盘到 `.workspace/artifacts/performance-poi/repro-traces/<scene>/<poi>.json`
- Development Build 的 `PerformancePoiSampler` 会按 POI 加载对应 trace，执行 settle / warmup 后回放整条轨迹，并在回放期间采集性能指标
- 外部包启动前，工具窗口会额外检查自动 POI 是否都已有 trace，避免启动后静默跳点

当前方案采用三层结构：

1. 场景 POI authoring + 批量稳态采样
2. 运行时抓帧
3. 离线 AI 分析

Unity 现在先负责把“人定义的重要位置”稳定转成一组可复跑的采样点，再在指定 POI 上抓到正确的一帧，AI 负责把这些点位数据和 RenderDoc 证据解释成可执行的性能结论。

## 系统结构

### 0.5 POI Batch Sampler

职责：

- 让团队在场景里直接放置 `PerformancePoiPoint`
- `PerformancePoiPoint` 的 Transform 位置就是相机采样位置；在 Scene 视图里通过移动 POI 本体改采样位置，再通过拖拽朝向点 Gizmo 改视角方向
- 三种相机模式的区别只在于“方向数据”最终写回 LookAt 偏移、显式 Euler 旋转或 `POI Camera Pose` 子节点，不再单独 author 相机位置偏移
- 默认 authoring 入口收敛为 Scene Gizmo，本轮不再保留额外的 Inspector 按钮式视角编辑入口
- 如需和其它物体共用或长期维护一个明确的相机参考点，也可以额外挂接 `POI Camera Pose` 子节点；运行时会优先使用该子节点的世界位置与朝向
- 用 `PerformancePoiSampler` 统一遍历点位，摆放角色与相机，执行 settle / warmup / sample
- 采集 `Time.unscaledDeltaTime`、`FrameTimingManager`、`ProfilerRecorder` 计数器，并输出 JSON / Markdown / CSV / 截图
- 在需要时对接 `RenderDocCaptureController`，让某些 POI 同时补一帧 RenderDoc 证据
- 通过 `PerformancePoiBootstrap` 解析 `--perf-poi-*` 命令行参数，支持构建机直接启动自动采样
- 编辑器侧提供 `Tools/Qianxia/Performance/POI Sampler` 独立窗口，但入口已收敛为“只跑 Development Build”：窗口只负责重建 fixed player 或启动已有导出包，并通过命令行自动执行一整轮 POI 采样；不再支持 Editor PlayMode 直接跑 POI。窗口会在启动前预检当前保存到磁盘的启动场景是否真的包含 `PerformancePoiSampler` 和 `PerformancePoiPoint`，避免外部包静默跑空

当前边界：

- 第一版默认走“定点摆位 + 稳态采样”，而不是连续导航或全自动寻路
- 重点是先得到可对比、可批处理、可落盘的点位性能地图数据集
- 后续如果要扩成“路线回放采样”或“导航链路采样”，应复用当前 artifact 契约，而不是再起一套旁路格式

### 1. Runtime Capture Controller

职责：

- 接收抓帧请求
- 决定下一帧是否需要抓取
- 在抓帧完成后收集上下文摘要
- 驱动 artifact 落盘

当前核心入口：

- `Assets/Project/Tools/Performance/Runtime/RenderDocCaptureController.cs`

### 2. RenderDoc Bridge

职责：

- 判断当前进程里是否已经注入 RenderDoc
- 调用 `BeginFrameCapture / EndFrameCapture`
- 获取最近一次捕获文件路径

当前实现目标是最小桥接，不把复杂分析逻辑塞进运行时。

### 3. Capture Context Provider

职责：

- 让不同系统按需补充自己的上下文摘要
- 例如 crowd、grass、battle 或其他运行时系统

注意：

- Provider 现在只提供摘要文本
- 不再单独落盘 provider JSON

### 3.5 GPU Pass Debug Registries

职责：

- 在运行时维护逻辑 GPU Pass 名称到本地源码和 Shader 入口的映射。
- 在抓帧窗口内写 RenderDoc marker，帮助 AI 从 `.rdc` 事件树把 compute/draw 事件对应回源码和 Shader 入口。
- 绑定记录能力仍保留在代码路径中用于调试扩展，但当前抓帧工作流不再把它序列化为 JSON artifact。
- GPU Pass Catalog 只承担“语义索引”职责，不承载场景状态或 provider payload。

当前 Crowd VAT 已注册 `Crowd.Update.*`、`Crowd.QueryGrid.*`、`Crowd.SceneQuery.*`、`Crowd.Combat.*`、`Crowd.Culling.*`、`Crowd.Render.*` 与 `Crowd.Debug.*`，抓帧分析以这些 `.rdc` marker 与 AI Debug runtime 记录为补充证据。

性能边界：

- GPU Pass scope 是抓帧元数据，不是常驻运行逻辑。
- `RenderDocCaptureController` 只在触发 capture 后到捕获完成前打开 `GpuPassDebugRuntime.CaptureMetadataEnabled`。
- 普通运行帧必须直接走原始 `ComputeShader.Dispatch / DispatchIndirect`，不创建逐 pass `CommandBuffer`，不持续更新 `GpuPassBindingDebugRegistry`，也不在每次 bind / dispatch 上读取全局 capture gate。
- Crowd renderer 在帧入口只 latch 一次“是否处于 capture/debug 采样窗口”；只有该状态为真时才解析 `Crowd.*` pass name 或写 RenderDoc marker。Indirect draw marker 由 `GpuSemanticDrawFeature` 在 RenderGraph 中按语义 marker 拆成独立 raster pass，颜色目标以 `ReadWrite` 访问，避免抓帧专用 pass discard 已渲染画面。
- Crowd renderer 上的 `_enableGpuPassDebugScopes / _enableGpuPassBindingSidecar` 是“允许抓帧时采集”的能力开关，不表示普通帧常开；其中绑定记录不再形成抓帧输出文件。

### 3.6 Crowd-only RenderDoc GPU Pass Extractor

职责：

- 作为隐藏工具，通过 `qrenderdoc.exe --python tools/renderdoc-cli/extract_gpu_passes_qrenderdoc.py` 离线打开 Crowd/VAT `.rdc`。
- 输出未做语义处理的 `gpu_pass_events.json`，保留 qrenderdoc 直接抽取的事件事实。
- 默认按稳定 marker 抽取 compute；需要 draw / indirect draw 时通过 `--include-draws` 纳入同一套 pass 语义索引，并优先回退到最近的逻辑 RenderDoc marker 作为 Pass 名称，跳过 `ExecuteIndirect(...)`、`ID3D...` 这类技术节点。
- 可选附带 `EventGPUDuration` GPU timing、当前绑定 shader、SRV / UAV / CBV 资源摘要和 dispatch 参数。
- 支持 `marker_filter="Crowd."`、`exclude_markers`、event id 范围和 `max_passes`，避免再次把整棵 event tree 塞进上下文。
- 不作为 Unity6 正式离线分析默认入口。它依赖业务代码主动加 `BeginSample` / `ProfilingScope` / `CommandBuffer` marker / 自定义 draw label；普通 MeshRenderer + Material 会自动进入 URP 默认渲染流程，不适合强行套用这条链路。
- 不依赖 RenderDoc MCP server、RenderDoc qrenderdoc python helper或 `%TEMP%/renderdoc_mcp` 文件 IPC，但这只是 Crowd 专用补充路径的技术实现差异。

当前输出会落成类似结构：

```json
{
  "version": 1,
  "gpu": {
    "passes": [
      {
        "name": "Crowd.Update.PredictAnimation",
        "event_name": "Dispatch(512, 1, 1)",
        "type": "compute",
        "event_id": 1234,
        "dispatch": [512, 1, 1],
        "gpu_ms": 0.31,
        "shader": "CrowdVatIndirect.compute",
        "shader_entry": "CSMain",
        "resources": {
          "srv": [],
          "uav": [],
          "cbv": []
        },
        "sync_primitives": []
      }
    ]
  }
}
```

说明：`sync_primitives` 记录 shader 里的 barrier / sync 调用点，先不做 D3D12/Vulkan event details 的深挖；需要定位资源状态问题时，再对少量目标 event 做定向查询。

### 3.7 rdoc-agent 高层 CLI

职责：

- `tools/renderdoc-cli/rdoc_agent.py` 是新的统一高层入口，命令结构为 `rdoc-agent <domain> <action> [options]`。
- 第一阶段已落地 `capture info`，通过 `qrenderdoc.exe --python` 直接读取 `.rdc` 并输出 `data/warnings/errors`。
- 第二阶段已落地 `events overview`，默认只返回有 draw/dispatch 或 GPU timing 的顶层块，避免直接暴露几千条 event。
- 第三阶段已落地 `events expand`，按指定 `eventId` 返回 root 与直接 children，每个 child 附带子树聚合统计；`--depth` 大于 1 时才递归展开。
- 第四阶段已将顶层 `find` 收敛为低 token 分支索引查找：调用方显式传入 `--scope event:<eventId>.branches` 作为 root，对每个直接 child 分支的后代 draw/dispatch leaf 搜索捕获可见的 Mesh、Material、Shader 与 Texture 名称；优先使用 RenderDoc resource usage 索引命中资源名，无法命中时才回退到轻量 pipeline 名称扫描；返回命中分支的 `hitCount`、匹配 GPU ms 与少量最贵 leaf 摘要，不返回完整树。功能重叠的 `events search` 已移除，不再作为 CLI 动作暴露。
- 第五阶段将 `entity list --from-search <searchId>` 破坏式收敛为 capture-visible 实体枚举：`find` 保存命中的 event id 集并返回 `searchId`；实体查询先读取 RenderDoc draw 证据，把本质相同的 draw/event signature 聚合成实体；如果 capture 同目录存在 `unity_prefab_signatures.sqlite`，再追加 Prefab 候选与 Prefab/LOD GPU timing。
- `entity list` 输出实体 schema：`entityCount`、`entities[]` 与 `aggregationSummary`。每个实体提供稳定 `entityId`、总 GPU 时间、event 数、`representativeEventId`、event id 样本、签名、命中的 pass/LOD/shader/texture 摘要；有 SQLite 时额外输出 `prefabCandidates[]`、`bestPrefabPath`、`prefabTimingSummary` 与 `prefabMatchSummary`；仍不输出 `eventIdentityCards[]` 或完整 `boundResources` 详情。
- 当前 `rdoc-agent` 不再暴露 `events inspect --event <eventId>`；从实体列表选出的 `representativeEventId` 只作为后续回查锚点，完整 pipeline / draw / shader / resource 细节不再由该 CLI 直接返回。
- `entity list` 不接受 `--metadata`；`--unity-prefab-metadata` 只作为旧参数兼容并被忽略。没有 `unity_prefab_metadata.json` 时仍应正常聚合 capture-visible 实体；Prefab 关联只来自 SQLite 候选，且禁止用文件名或 GUID 搜索手工猜 Prefab。
- `entity match-prefabs` 仍保留为独立 Unity 查询层：它读取 `unity_prefab_signatures.sqlite` 的 `prefab_draw_signatures` 表，用实体的 mesh / LOD / texture signature 给出当前加载场景中 Prefab 实例对应的候选、score、confidence、renderer 行证据与 scene instance 路径；`entity list` 复用同一套候选逻辑生成 Prefab timing。

边界：

- 当前 Unity6 正式离线分析链路以 `tools/renderdoc-cli/rdoc_agent.py` 为主，先从 `capture info` 建立捕获可用性与语义风险判断，再通过 `events overview` 识别顶层大块与热点，用 `events expand` 选择分析 root，再显式传入 `find --scope event:<eventId>.branches` 扇出 child 分支并生成 evidence search id，再用 `entity list` 比较 capture-visible 实体耗时与 Prefab/LOD timing；需要单独查看候选细节时再调用 `entity match-prefabs` 查询 SQLite。`representativeEventId` 仅保留为后续手动 qrenderdoc 或专用下钻工具的回查锚点。
- `tools/renderdoc-cli/renderdoc_gpu_context_cli.py` 仅适合 Crowd/VAT 专用 marker、catalog 与 AI Debug 旁证，不作为普通 MeshRenderer + Material 抓帧的默认路径。

### 4. Artifact Writer

职责：

- 统一写出抓帧产物
- 统一写出 POI 采样产物
- 维护固定目录结构
- 给 AI 提供稳定的输入约定

### 5. AI Analysis Runner

职责：

- 打开最新 `.rdc`
- 拼装分析 prompt
- 引导 AI 使用 `rdoc_agent.py` 从 `.rdc + gameview.png` 中逐步取证；只有明确分析 Crowd/VAT marker 时，才结合 GPU Pass Catalog 与配对后的 AI Debug runtime 旁证

## Artifact 设计

### POI Replay Trace

除了每轮采样产物外，POI 系统现在还会持久化录制好的回放轨迹：

```text
.workspace/artifacts/performance-poi/
  repro-traces/
    SampleScene/
      town-center.json
      combat-zone.json
```

说明：
- 每个 trace 对应一个 `PerformancePoiPoint`
- trace 只保存角色与相机的时间序列位姿，不保存 squad command
- Development Build 采样时通过 `--perf-poi-trace-root` 指向这组 trace 根目录，因此同一个导出包可以复用新录制的轨迹

POI 批量采样会额外产出：

```text
.workspace/artifacts/performance-poi/
  20260520_211530_samplescene-poi-auto/
    poi_run_summary.json
    poi_run_summary.md
    poi_metrics.csv
    profiler-raw/
      01_town-center.raw
    profiler-ai/
      01_town-center/
        ai_package_manifest.json
        capture_summary.json
        session_manifest.json
        frame_index.csv
        thread_index.csv
        thread_overview.json
    screenshots/
      01_town-center.png
      02_combat-zone.png
```

说明：

- `poi_run_summary.json`：机器可读的 POI run 摘要，包含点位位置、帧时统计、CPU/GPU 平均值、DrawCalls/Batches 平均值、可选 RenderDoc 路径和 per-POI Profiler `.raw` 导出状态
- `poi_run_summary.md`：面向人阅读的采样报告，用于快速看热点点位、P95、截图、Profiler AI package 路径和数据质量提示
- `poi_metrics.csv`：面向表格和后续可视化工具的平铺数据集，包含 `profiler_raw_requested / profiler_raw_exported / profiler_raw_path / profiler_raw_size_bytes`，以及 `profiler_ai_package_generated / profiler_ai_package_directory / profiler_ai_package_manifest_path / profiler_capture_summary_path / profiler_session_id / profiler_session_artifact_directory`
- `profiler-raw/*.raw`：仅对勾选了 `Export .raw` 的 POI 导出 Unity binary profiler log，作为后续 profiler Agent / AI 下钻分析输入
- `profiler-ai/<order>_<poi>/`：由编辑器在外部 Player run 结束后自动物化的统一 AI 数据包。它复制离线 session 核心索引，并额外附带 `ai_package_manifest.json` 与 `capture_summary.json`，让 AI tool、脚本或人工排查可以不重新理解散落的 run 结构
- `screenshots/*.png`：每个 POI 的视角留档，便于把数字对应回具体画面

一次成功抓帧，当前最少会落盘以下 2 类产物：

```text
.workspace/artifacts/renderdoc-captures/
  20260505_210000_sample-scene-manual/
    capture.rdc
    gameview.png
```

说明：

- `capture.rdc`：GPU 帧主证据
- `gameview.png`：快速确认画面的视觉对照

当前方案明确不再输出：

- `capture_context.md`
- `gpu_pass_debug_map.json`
- `gpu_pass_bindings.json`
- provider payload `.json`
- 结构化 findings JSON

注意：Crowd/VAT 的显式 marker 仍写入 `.rdc`，并可通过 GPU Pass Catalog 回查代码语义；AI Debug JSONL 提供配对后的 runtime 旁证。上述两份 GPU Pass JSON 已不属于抓帧 artifact 契约。

Crowd/VAT 专用分析阶段可以额外生成：

```text
.workspace/artifacts/renderdoc-analysis/
  gpu-context-YYYYMMDD-HHMMSS/
    gpu_pass_events.json
    gpu_pass_overview.json
    gpu_pass_resources.json
    gpu_pass_catalog.json
    gpu_frame_context.json
```

这些文件由 `tools/renderdoc-cli/run_gpu_context.sh` 编排写出，不是运行时抓帧 artifact 的必备文件，也不是 Unity6 普通离线分析的默认输入。它们只在分析目标明确落在 Crowd/VAT 且抓帧里存在稳定 `Crowd.*` marker 证据时使用。

- `gpu_pass_events.json`：RenderDoc/qrenderdoc 原始导出，保留未做 catalog join 的事件事实，便于回查。
- `gpu_pass_overview.json`：Python 按稳定 `pass_id` 输出本帧 GPU pass 总览；每个 event 保留 `event_id`、`name`、`pass_type`、`event_type`、`duration_ms`、compute 的 `dispatch`、draw / indirect draw 的 `draw`、`pipeline`、资源摘要、shader 标识和 catalog 引用。
- `gpu_pass_resources.json`：Python 从 `gpu_pass_events.json` 和 catalog 派生的资源事实。当前公开两层结构：`event_resource_bindings[]` 按 `event_id + pass_id` 列出每个 event 的 `bindings[]`，每条 binding 至少保留 `resource_id`、`name`、`slot`、`bind_type`、`access`、`type`、可选 `hlsl_type`、`byte_size`、`stride` 和 `estimated_element_count`；`resource_usages[]` 同时聚合 RenderDoc 绑定资源和 catalog 语义资源的 `producer_passes` / `consumer_passes`，让 indirect draw 能直接看到 visible buffer / args buffer 的上游 producer pass。
- `gpu_frame_context.json`：以本帧真实 GPU event 为中心，按 `frame + event_id + pass_id` 组织扁平化 pass 结构，公开 `pass_id`、`matched_pass_id`、`pass_name`、`event_id`、`pass_type`、`event_type`、`gpu_time_ms`、`dispatch/draw`、draw pipeline、draw 资源摘要、`resource_flow`、`shader.numthreads`、`derived_metrics`、`performance_relevant_facts`、安全的 `constants`、`pass_graph.edges` 与必要的 `data_quality`，作为 AI 分析的主结构化输入。

Crowd/VAT 专用分析阶段还有一个只读查询入口：

```text
tools/renderdoc-cli/renderdoc_gpu_context_query.py
```

它把 Crowd 专用事实包拆成多层可组合命令，让 LLM 自己根据问题选择关键词、pass、event、resource 和源码线索。第零层是认知地图，例如 `gpu architecture map`，用于先理解模块分工、跨域依赖、关键资源 handoff 和架构审查信号；第一层是 AI 友好的状态视图，例如 `gpu frame overview`、`gpu pass explain --id ...`、`gpu resource explain --name ...`；第二层是局部追问，例如 `gpu pass resources --id ...`、`gpu pass dependencies --id ...`、`shader memory-pattern --kernel ...`；第三层是原始证据验证，例如 `renderdoc event show --id ...`、`code show --file ... --line ...`、`log grep "..."`。它不是新的导出器，也不写分析结论；它只是降低从 `.rdc` marker、catalog 与 AI Debug 旁证中反推 Crowd 状态的成本。

Crowd 专用事实源边界：

- `gpu_pass_events.json`：RenderDoc/qrenderdoc 原始事件事实，只作为证据原文，不做稳定 ID join。
- `gpu_pass_overview.json`：GPU pass 总览事实，主键是 `event_id`，稳定关联键是 `pass_id`，包含 `name`、`pass_type`、`event_type`、`duration_ms`、`dispatch/draw`、pipeline、shader 和 catalog 引用。
- `gpu_pass_resources.json`：资源绑定事实，使用 `event_resource_bindings[]` 绑定到 `event_id + pass_id`，并通过 `resource_usages[]` 聚合资源级 producer / consumer；同一个底层资源只保留一份 `resource_id` 主线，避免 AI 把同一个 buffer 看成两套资源。
- `gpu_frame_context.json`：只负责按同一个 pass instance 合并事实；默认只保留 AI 真正需要的字段，不再把 `event_name`、重复的 debug label、`linking.matched=true` 或逐 pass `raw_sources` 放进主结构。缺失或冲突信息统一进入 `data_quality`，而 pass 级的数学派生值集中进入 `derived_metrics`。

## 为什么取消额外抓帧 sidecar

之前的设计里，上下文 Markdown 与 GPU Pass JSON sidecar 承担补充语境和语义索引的角色，但在当前工作流里它们带来的价值不够高，反而会增加几类成本：

1. 用户会误以为必须先整理 JSON，AI 才能分析。
2. Provider 还要维护额外的序列化格式，链路更重。
3. 文档、工具窗口和分析 prompt 都会出现“双入口”，反而增加使用门槛。

现在的原则更直接：

- `.rdc` 是主证据
- `gameview.png` 是视觉对照
- `.rdc` 中的显式 Crowd marker 与 GPU Pass Catalog 提供语义对齐
- 配对后的 AI Debug 数据只在 Crowd/VAT 分析时提供运行时旁证

只要 `.rdc + gameview.png` 齐全，AI 就可以通过 `rdoc-agent` 开始普通 RenderDoc 分析；Crowd/VAT marker 与 AI Debug 旁证齐全时再补充语义 Pass 分析。

## 捕获上下文承载位置

当前不再单独生成 `capture_context.md`。所需上下文按来源读取：

- `.rdc` 及其 capture comments 提供捕获事件、标签和可查询 GPU 状态。
- `gameview.png` 提供抓帧画面的视觉对照。
- AI Debug runtime 记录在 Crowd/VAT 问题中提供工作集、实例和阶段旁证。
- Editor 导出的 `unity_prefab_metadata.json` 仍是本地 Prefab 资产清单；`unity_prefab_signatures.sqlite` 改为当前加载场景中 Prefab 实例对应的签名索引。`entity list` 不读取 JSON sidecar，但会在 SQLite 存在时读取 `unity_prefab_signatures.sqlite` 生成 Prefab/LOD timing；`unity_material_metadata.json` 仍是独立材质清单。

换句话说，我们不是不要上下文，而是不再让每次抓帧都携带额外 Markdown 或 GPU Pass sidecar。

## AI 分析输入约定

AI 侧的固定输入现在是：

1. `capture.rdc`
2. `gameview.png`
3. 可选的 `unity_prefab_metadata.json` 与 `unity_prefab_signatures.sqlite`
4. 可选手动导出的 `unity_material_metadata.json`
5. 可配对的 Crowd-only AI Debug runtime 记录

建议步骤：

1. 运行 `python tools/renderdoc-cli/rdoc_agent.py capture info --capture "<capture.rdc>" --json`
2. 根据 `hasDebugMarkers`、draw/dispatch/resource 计数判断后续语义风险。
3. 运行 `python tools/renderdoc-cli/rdoc_agent.py events overview --capture "<capture.rdc>" --json`
4. 运行 `python tools/renderdoc-cli/rdoc_agent.py events expand --capture "<capture.rdc>" --event <eventId> --depth 1 --json`
5. 如果已有 Mesh、Material、Shader 或 Texture 名称关键词，运行 `python tools/renderdoc-cli/rdoc_agent.py find --capture "<capture.rdc>" --q "<text>" --scope event:<eventId>.branches --json`，显式跨该 root 的直接 child 分支搜索后代 leaf。
6. 对返回的 `searchId` 运行 `python tools/renderdoc-cli/rdoc_agent.py entity list --capture "<capture.rdc>" --from-search <searchId> --json`，获得 capture-visible `entities[]`、`aggregationSummary`、代表 event id；如果同目录有 `unity_prefab_signatures.sqlite`，还会获得 `prefabTimingSummary`。
7. 如需单独查看 Prefab 候选细节，运行 `python tools/renderdoc-cli/rdoc_agent.py entity match-prefabs --capture "<capture.rdc>" --from-search <searchId> --prefab-signatures "<capture-folder>/unity_prefab_signatures.sqlite" --json`，获得每个实体的 `prefabCandidates[]`。
8. 从实体的 `totalDurationMs`、`eventCount` 与 `representativeEventId` 选择目标，并把该 `eventId` 作为后续手动 qrenderdoc 或专用下钻工具的回查锚点；`rdoc-agent` 不再提供对应的 `events inspect` 命令。
9. 如果目标明确是 Crowd/VAT，再读取 `.rdc` marker、catalog 和 AI Debug JSONL 作为旁证。普通 MeshRenderer + Material 分析不通过本地文件名、GUID 或源码搜索把 RenderDoc draw 断言为 Prefab。

输出目标：

- 帧概览
- 最重的 draw / dispatch
- 与项目业务最相关的热点
- 最高优先级的优化建议

## 运行模式建议

### 普通运行模式

未触发 RenderDoc capture 时，采样工具链应保持近似零侵入：

- 不包裹 compute dispatch marker。
- 不落盘 binding sidecar。
- 不执行 provider 摘要和同步 GPU counter readback。
- 不自动创建 `RenderDocCaptureController` 做每帧热键轮询；只有命令行传入 RenderDoc 参数，或场景中显式放置 controller，运行时抓帧入口才启用。

Capture 完成阶段只保留 `.rdc`、按设置生成的 `gameview.png` 以及写入 `.rdc` 的 capture comments；Crowd/VAT 场景通过 marker 与 AI Debug 旁证继续分析。

### Editor 模式

只用于：

- 快速验证抓帧链路
- 排查工具接线问题

不建议把 Editor 抓帧当成最终性能口径。

### Development Player 模式

建议作为正式采样路径，原因是：

- 更接近真实运行时
- 更适合 `D3D12 + RenderDoc launcher / attach`
- 能减少 Unity Editor 自身开销的干扰

## 当前 D3D12 路线

当前主路径已经调整为：

1. 构建 `Windows Development Player`
2. 使用 RenderDoc launcher 或 attach 注入 Player
3. 在 Player 内通过热键触发抓帧
4. 自动落盘 `.rdc + gameview.png`，并将可用 marker 保留在 `.rdc` 中
5. 通过 rdoc-agent qrenderdoc CLI 直接分析

## 里程碑状态

### M0 最小闭环

已完成的方向：

- 能在运行时触发 RenderDoc 抓帧
- 能写出 `.rdc`
- 能保存 `gameview.png`
- Crowd/VAT 抓帧能在 `.rdc` 事件树中保留语义 marker
- 普通离线分析能通过 rdoc-agent qrenderdoc CLI 读取 frame summary、draw list、timing、draw details 与 pipeline state
- Crowd/VAT 专用隐藏链路仍可通过 `qrenderdoc.exe --python` 输出规范化的 `gpu_pass_events.json`、`gpu_pass_overview.json`、`gpu_pass_resources.json`、`gpu_pass_catalog.json` 与 `gpu_frame_context.json`
- 能从工具窗口生成直接分析 prompt

### M1 项目化闭环

已部分完成：

- Editor 工具窗口
- 外部 D3D12 Player 启动/注入链路
- Capture comments 与可选 AI Debug 旁证联动

后续可以继续增强：

- marker 定点抓帧
- 批量抓帧
- 同类 capture 横向对比

## 风险与边界

- RenderDoc 抓帧本身会扰动性能，因此更适合定位结构性热点，不适合直接当 benchmark 数字。
- AI 分析质量仍然依赖抓帧时机、命名习惯和项目特定摘要质量。
- 如果项目后续需要批量自动对比，再引入结构化 findings JSON 报告也可以，但那应该是“分析产物层”的增量能力，而不是当前抓帧链路的硬依赖。

## 最终设计结论

这套工具当前的设计原则可以压缩成一句话：

> 抓一份带可查询 marker 的 `.rdc` 与视觉对照，让 AI 直接沿 RenderDoc 事件、本地代码和按需旁证定位问题，不把额外 sidecar 作为前提。

## 相关文件

- `Assets/Project/Tools/Performance/Runtime/RenderDocCaptureController.cs`
- `Assets/Project/Tools/Performance/Shared/RenderDocCaptureArtifacts.cs`
- `Assets/Project/Tools/Performance/Shared/GpuPassDebugInfo.cs`
- `Assets/Project/Tools/Performance/Shared/GpuPassBindingDebugInfo.cs`
- `Assets/Project/Tools/Performance/Editor/RenderDocCaptureWindow.cs`
- `tools/renderdoc-cli/rdoc_agent.py`
- `tools/renderdoc-cli/rdoc_agent.py`
- Crowd/VAT 隐藏分析入口：`tools/renderdoc-cli/run_gpu_context.sh`、`tools/renderdoc-cli/renderdoc_gpu_context_cli.py`、`tools/renderdoc-cli/extract_gpu_passes_qrenderdoc.py`、`renderdoc-mcp/RenderDocMCP-main/scripts/build_gpu_context_package.py`

### 2026-05-21 Runtime POI 对位补充

- `Tools/Qianxia/Performance/POI Sampler` 在 Play Mode 下新增 `Move Runtime Camera To POI`
- 该入口会复用 `PerformancePoiPoint.ResolveCharacterPose / ResolveCameraPose`，把运行时角色与相机先摆到当前选中 POI，再开始录制
- `Start Recording` 现在会先自动执行一次上述对位，确保录制轨迹的起点真正使用编辑器阶段摆好的 POI 位置
- Scene 视图里的 POI Gizmo 会读取当前场景最近一次 `poi_run_summary.json`，按 `Game Thread / Render Thread / GPU` 三列显示关键时间指标
- `POI Sampler` 工具窗口也会读取同一份最近结果，在面板里按 POI 列出 `Game / Render / GPU` 三列，并按预算阈值着色；点击任意一行会直接选中并定位到对应 POI，便于不切回 Scene 视图时快速对位
- Gizmo 颜色按可配置预算着色：明显低于预算为绿色，接近预算转黄橙，超出预算转红，便于直接在场景里看热点分布

## 实现补记（2026-05-21）

- `PerformancePoiPoint` 现在会持久化一个内部稳定 `PoiKey`，用于 trace 路径、运行时选中态和最新结果回显的唯一键；`PoiId` 继续作为给团队看的展示名，但不再承担唯一标识职责。
- 如果场景里有多个 POI 使用同一个 `PoiId`，`POI Sampler` 工具窗口会给出重复告警；这类 POI 建议重新录制 trace，避免继续复用旧的同名轨迹文件。
- `POI Sampler` 窗口会在刷新时为旧 POI 实例补写缺失的 `PoiKey`。编辑器内默认优先使用 Unity `GlobalObjectId` 作为稳定 key，只在运行时无法拿到编辑器对象标识时才退回到“场景路径 + 层级路径”的 fallback；这样即使作者调整层级结构，录制 trace 与工具面板预检也不会再因为 key 漂移互相对不上。用户仍应在重录后保存场景，再跑 Development Build。
- 缺 trace 的预检提示现在会区分“完全没有 trace”和“只发现旧版按 `PoiId` 命名的 legacy trace 但当前场景存在重名 POI，不能安全复用”。
- 当同一个 POI 重新录制并保存新 trace 时，工具会自动删除旧版按 `PoiId` 命名的 legacy trace，避免目录里长期同时残留新旧两份轨迹。
- Scene Gizmo 的 `Game Thread / Render Thread / GPU` 三列现在会在数据缺失时显示 `n/a`，不再把缺失线程 timing 误显示成 `0.00 ms`。

## 2026-05-21 Build Player in-game 性能监控条补充

- Development Player 新增 `InGamePerformanceMonitor` 底部 HUD，服务于人工跑位和抓帧前的快速肉眼判断：确认 FPS、Frame/Main/Render/GPU 和 Draw/Batches/SetPass 是否已经接近目标状态。
- HUD 默认在非 Editor Player 中启用，RenderDoc 外部启动链路会显式带上 `--in-game-perf-monitor`；如需无 HUD 截图或减少现场扰动，可用 `--disable-in-game-perf-monitor` 关闭。
- HUD 数据来自 `Time.unscaledDeltaTime`、`FrameTimingManager`、`ProfilerRecorder` 与 `Profiler` 内存接口，仅作为现场提示。批量性能地图、回归对比和文档化结论仍以 `PerformancePoiSampler` 生成的 JSON/Markdown/CSV 为准。
- 运行中可按 `F9` 临时隐藏或恢复 HUD，避免遮挡底部 UI 或截图构图。

## 2026-05-22 POI Profiler .raw 导出补充

- `PerformancePoiPoint` 新增 per-POI `Export .raw` 开关，`Tools/Qianxia/Performance/POI Sampler` 会在工具面板中列出当前场景所有 POI，并支持批量启用 / 关闭；面板会标出 `Auto / Manual / Inactive`，实际只有本轮采样到的 POI 会落盘。
- `PerformancePoiSampler` 只会在被勾选的 POI 采样窗口内临时启用 Unity binary profiler log，采样结束后恢复进入该 POI 前的 Profiler 状态，避免整轮 run 都持续写大文件。
- `.raw` 文件落盘到本次 run 的 `profiler-raw/` 子目录，`poi_run_summary.json`、`poi_run_summary.md` 和 `poi_metrics.csv` 都会记录请求状态、导出成功状态、路径与文件大小，方便后续 AI 从 POI 性能地图直接跳到 profiler session 分析。
- `Build And Run Fixed Player` 与 `Run Existing Player` 按钮保持可点击；如果缺 sampler、场景未保存、磁盘场景缺 POI 组件或 trace 预检失败，面板会直接显示阻塞原因，而不是表现成无响应。
## 2026-05-29 补充：Unity Prefab metadata 与实体聚合边界

`entity list` 现在破坏式改为 capture-visible 实体输出。它读取 `find` 保存的 event 证据和 `.rdc` 中可见的 draw signature，把 mesh 基名、material、shader、texture、draw type、topology 等本质相同的 event 聚合为 `entities[]`；不读取 `unity_prefab_metadata.json`。如果同目录存在 `unity_prefab_signatures.sqlite` 或显式传入 `--prefab-signatures`，会把实体映射为 Prefab 候选，并输出 Prefab 总耗时与按 LOD 拆分的 GPU 耗时。

Unity Editor 侧仍保留 `unity_prefab_metadata.json` 与 `unity_prefab_signatures.sqlite` 导出。Prefab metadata 默认扫描 `Assets/ANGRY MESH` 下的 Prefab 资产，并记录 Prefab 身份、renderer path、LOD、mesh guid/fileId、material/shader/texture 与 shadow flag。SQLite signature 不再扫描整套 Prefab 资产库，而是遍历当前加载场景中的 `Assets/ANGRY MESH` Prefab 实例，记录其源 Prefab、场景实例路径、场景 renderer 路径、LOD、mesh 与 texture 签名。RenderDoc Capture 窗口的 `Export ANGRY MESH Metadata` 会把 `unity_material_metadata.json`、`unity_prefab_metadata.json` 与 Unity 侧直接生成的 `unity_prefab_signatures.sqlite` 一起导出到 capture 同目录。

材质清单仍可用 `rdoc-agent unity materials` 独立导出：

```powershell
.\tools\renderdoc-cli\rdoc-agent.cmd unity materials `
  --capture "<capture.rdc>" `
  --shader-root "Assets/ANGRY MESH" `
  --material-root Assets `
  --metadata-output "<capture-folder>\unity_material_metadata.json" `
  --json --pretty
```

输出的 raw material sidecar 根结构为 `materials` 数组，字段包含 `materialId`、`name`、`assetPath`、`shader`、`shaderPath`、`textures`、`textureBindings`。Prefab sidecar 根结构为 `prefabs` 数组，字段包含 `prefabId/guid/name/assetPath` 与 renderer 级签名。

如果后续 Agent 查询工具需要 SQL 入口，优先使用 Unity Editor 直接导出的 SQLite 签名表。也可以单独运行菜单 `Tools/Qianxia/RenderDoc Capture/Export ANGRY MESH Prefab SQLite`，或在 batchmode 调用 `RenderDocUnityPrefabMetadataExporter.ExportAngryMeshPrefabSignaturesForBatchmode` 并传入 `--unity-prefab-signatures-output <path>`。

当前数据库查询主路径为 `rdoc-agent db import-prefabs` + `rdoc-agent db instance-cost --prefab-name <PrefabName>`：先把 `unity_prefab_signatures.sqlite` 的 `prefab_draw_signatures` 导入 RenderDoc fact DB 的 `prefab`、`prefab_model`、`prefab_texture` 表，再由 `--prefab-name` 通过 `prefab_model` 解析模型名 / `model_pk`，最后聚合 `draw_event.gpu_us`。当 prefab 表已导入时，不再用 `--q` 猜 LOD 模型名作为 prefab 查询主路径。

该 Unity 侧导出器写入 `prefab_draw_signatures(id, prefab_path, prefab_name, prefab_guid, scene_path, scene_name, scene_instance_path, scene_instance_name, prefab_lod_key, prefab_inner_path, scene_renderer_path, lod_index, mesh_name, mesh_base_name, texture_names)`；`texture_names` 存 JSON 数组字符串，`prefab_lod_key` 用于表达同一 Prefab 下不同 LOD 的组织关系。`entity list` 与 `entity match-prefabs` 都会读取该表，用实体的 mesh 基名 / LOD / texture 交集查询当前场景 Prefab 实例候选；SQLite 表是 Unity 场景实例侧证据索引，不参与 `.rdc` draw 聚合本身。

`entity list` 的输出不再包含旧式 `prefabs[]`、`ambiguousMatches[]`、`unmatchedDraws[]` 或 `mappingSummary`。后续如果需要从 RenderDoc 实体反查本地 Prefab，优先查看 `entity list` 的 `prefabTimingSummary` 与每个实体的 `prefabCandidates[]`；需要完整候选细节时再使用 `entity match-prefabs`，并在结论中明确这是 Unity 场景实例侧旁证。
