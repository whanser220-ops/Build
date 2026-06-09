# 千夏 Crowd 当前实现基线

## 文档信息

- 版本：v1.9
- 日期：2026-05-26
- 状态：当前主路径已收敛到 `CrowdVatIndirectRenderer GPU 主循环 + CrowdVatSquadController runtime squad 上传 + CrowdVatCodexAgentController CampB Codex CLI 对战 agent + CrowdVatSceneQueryFieldAsset 混合查询 + CrowdVatObservationHub / CrowdVatWorld 观测镜像 + CrowdVatAiDebugRecorder 调试链路 + .rdc 内 GPU Pass marker 语义`
- 同步级别：强同步
- 适用项目：Unity 6 `6000.0.46f1`、URP
- 作者：Codex（基于当前仓库实现整理）
- 当前实现锚点：
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.Simulation.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.GpuDebug.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadController.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadCommandController.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatCodexAgentController.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatFrameProtocol.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatSceneQueryFieldAsset.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatAiDebugRecorder.cs`
  - `Assets/Project/Crowds/VAT/Editor/QianxiaCrowdSceneAutomation.cs`
  - `Assets/Scenes/SampleScene.unity`
- 最后实现核对：2026-05-26
- 替代关系：
  - 替代：
    - `docs/plans/qianxia-crowd-vat-technical-design.md`
    - `docs/plans/qianxia-crowd-runtime-tactical-squad-design.md`
  - 被替代：

## 目标

给当前仓库里已经落地的 crowd 运行时提供单一、可强同步的实现入口。

这篇文档的职责不是继续提下一阶段路线，而是回答三个当前问题：

1. `crowd` 现在真正跑的是哪条主链。
2. CPU / GPU / 场景 / 调试分别由哪些模块负责。
3. 改 crowd 代码时，应该同步哪些入口、测试和样例场景。

## 当前结论

- 当前 crowd 不是单一的“VAT 播放器”，而是一套 `GPU 实例仿真 + CPU runtime squad 编排 + 混合 Scene Query + 观测/调试桥` 组合系统。
- [CrowdVatIndirectRenderer.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.cs) 是运行时总控：负责实例布局、Compute 调度、工作集压缩、空间查询、战斗、可见性与读回。
- [CrowdVatSquadController.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadController.cs) 是当前 CPU 意图层：以 `CrowdVatFramePacket` 上传小队状态、成员归属、编队槽位和保留存活数，不靠频繁 `RebuildCrowdLayout()` 驱动队形。
- [CrowdVatSquadCommandController.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadCommandController.cs) 已经是样例场景中的玩家交互入口：负责屏幕中心选队、点地移动和世界空间选中环反馈。
- [CrowdVatCodexAgentController.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatCodexAgentController.cs) 是当前最小 Codex agent 接入点：默认定频控制 `CampB`，当前主模式通过本机 `codex exec` 启动 Codex CLI agent 做低频战术规划；保留 OpenAI Responses API 在线 LLM 模式作为可选 planner。所有外部 planner 最终只输出 `Hold / Advance / Charge / Retreat / Regroup` 结构化命令，并继续复用 runtime squad 的 `TryCommandMoveTo()` 执行链。
- Scene Query 当前分成两条链：
  - `Crowd actor query` 走 GPU resident 网格与 `CrowdVatSpatialQueryRequest/Result/Hit`。
  - `Ground / Obstacle / Environment query` 走 `CrowdVatSceneQueryFieldAsset + CrowdVatSceneQueryUtility` 的 CPU immediate 采样接口。
- 调试链已经不只停留在 Gizmo：`FrameProtocol -> ObservationHub -> World mirror -> AiDebugRecorder` 这一层能够产出同步读回观测和 `jsonl + markdown` 调试记录。
- GPU Pass 语义映射已经接入 Crowd 主循环：`CrowdVatIndirectRenderer.GpuDebug.cs` 注册 `Crowd.*` 逻辑 Pass 名称；compute 在抓帧窗口内通过单层 `BeginSample(passName)` 写入 RenderDoc marker，indirect draw 则通过 `GpuSemanticDrawFeature` 为每个 `[pass_id]` 建独立 RenderGraph raster pass，让 `rdoc-agent` 可以从 `.rdc` 事件树读取 `crowd.render.*` draw marker。
- 抓帧 artifact 不再输出 GPU Pass 映射或绑定 sidecar；Crowd/VAT 的运行时补充证据改由 `.workspace/artifacts/ai-debug/` 下的 AI Debug 记录与 `.rdc` marker 配对提供。

## 范围

### 本文覆盖

- 当前 crowd GPU 主循环与工作集划分。
- runtime squad 的作者数据、上传协议和场景交互入口。
- crowd actor query 与 ground / obstacle / environment query 的当前实现边界。
- observation / world mirror / AI debug / SampleScene 自动化与当前测试面。

### 本文不覆盖

- [qianxia-large-scale-crowd-physics-design.md](C:/unity/Unity6/docs/plans/qianxia-large-scale-crowd-physics-design.md) 里的 `Simulation LOD / XPBD / 流场` 下一阶段扩展。
- [qianxia-scene-query-technical-design.md](C:/unity/Unity6/docs/plans/qianxia-scene-query-technical-design.md) 里的统一 Scene Query 未来架构。
- 完整掩体战术 AI、跨楼层 crowd volume、NavMesh/路径规划体系。
- 最终美术资产规格与多 LOD 量产方案。

## 当前实现

### 1. 总体分层

当前 crowd 可以按五层理解：

1. `CrowdVatIndirectRenderer`
   - 维护 `spawn data`、仿真状态缓冲、工作集压缩缓冲、query / combat / visibility / AI debug 缓冲。
   - 驱动 `Predict / PhysicsActive / CombatActive / SpatialQuery / Finalize / RenderMeshIndirect` 主流程。
2. `CrowdVatSquadController`
   - 维护 CPU 侧 squad authoring、默认小队生成、运行时中心推进、朝向更新、队形槽位和自动动画。
   - 通过 [CrowdVatFrameProtocol.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatFrameProtocol.cs) 定义的 `CrowdVatFramePacket` 往 renderer 上传增量状态。
3. `CrowdVatSquadCommandController`
   - 维护玩家侧屏幕中心选择、点地命令和世界空间反馈；样例自动化默认把可选阵营限制为 `CampA`。
4. `CrowdVatCodexAgentController`
   - 维护最小运行时 agent：感知 `CampB` 小队与最近 `CampA` 目标，定频规划结构化 squad 命令。
   - 默认 `CodexCliWithLocalFallback`：异步启动 `codex exec`，把压缩后的 `CampB` observation 通过 stdin 交给 Codex CLI agent，并用 `--output-schema` 约束最终 JSON；CLI 未返回、超时或解析失败时继续使用本地规则，不阻塞运行时。
   - 仍保留 `OnlineLlm / OnlineLlmWithLocalFallback`：需要直接请求 OpenAI Responses API 时可手动切回，API key 只从 `OPENAI_API_KEY` 环境变量读取。
5. `CrowdVatSceneQueryFieldAsset + CrowdVatSceneQueryUtility`
   - 提供静态世界侧的 walkable mask、ground flags、static obstacle SDF、baked environment distance field 查询。
6. `CrowdVatObservationHub / CrowdVatWorld / CrowdVatAiDebugRecorder`
   - 提供同步读回、world mirror 和 AI Debug 采样产物。

### 2. GPU 主循环

[CrowdVatIndirectRenderer.Simulation.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.Simulation.cs) 里的 `UpdateGpuBuffers()` 已经不是旧文档里的五段基础链，而是当前这条更完整的顺序：

1. 刷新运行时绑定和上传数据
   - 动画 clip metadata
   - 实例动画状态
   - interaction spheres
   - runtime squad states / alive counts / agent assignments / formation slots
   - spatial queries
2. 重建 `Alive` 工作集
   - 所有未死亡实例先压到 `alive instance list`
3. `Predict`
   - 对 `Alive` 子集做位置、速度、动画相关预测
4. `Wake Grid + PhysicsActive`
   - 先建 wake grid，再执行 `EvaluatePhysicsActive`
   - 然后重建 `PhysicsActive` 工作集，只让近场物理子集进入求解
5. 物理迭代
   - 每次 iteration 清 grid
   - 以 `PhysicsActiveOnly` 模式建 grid
   - 运行 `SolveCrowd`
   - crowd 近似分离、环境约束都在这层发生
6. 查询 / 战斗阶段
   - 需要战斗或 actor query 时，再以 `AllQueryables` 模式重建 grid
   - `BuildSpatialElements`
   - `ClearCombatSquadCandidateCounters`
   - `BuildCombatCandidateClusters`
   - `ResolveTargetAcquisition`
   - `BuildSquadAcquisitionDispatchArgs`
   - `EvaluateSquadAcquisitionCandidates`
   - `BuildTargetAcquisitionLosDispatchArgs`
   - `ResolveTargetAcquisitionLineOfSight`
   - `FinalizeTargetAcquisition`
   - `ResolveInstanceCombat`
   - `ResolveSpatialQueries`
7. `Finalize`
   - 写回实例矩阵、VAT 帧数据和最终渲染输入

当前主循环有四个关键实现特征：

- 工作集已经明确分成 `Alive / PhysicsActive / CombatActive / Visible` 四层，不再把所有实例每步都走完整条链。
- 近场物理求解只对 `PhysicsActive` 子集运行，战斗逻辑只对 `CombatActive` 子集运行。
- crowd actor query 与战斗共享一轮 `AllQueryables` grid，而不是额外维护第二套 crowd broadphase。
- 战斗索敌先按 runtime squad 构建 `CombatSquadCandidateBuffer` 保守候选池；候选构建由 `ClearCombatSquadCandidateCounters` 清零后，C# 侧按每个 squad 的实际 broadphase grid 覆盖生成 `(squadIndex, tileBegin, cellRange)` work item，并通过 indirect `BuildCombatCandidateClusters` 只派发 `sum(perSquadTileCount)` 个 tile group，不再把 `maxCellTileCount` 广播给所有 squad。cluster pass 对命中的敌对实例逐条 `InterlockedAdd` 追加到每 squad 候选池，把目标 instance、faction、命中顶部 local position 和 scale 写入缓冲。`ResolveTargetAcquisition` 只负责当前目标验证、timer / lock 状态和非 squad agent 的 grid fallback；squad 路径由 `EvaluateSquadAcquisitionCandidates` 以一个 agent 一个 group、64 线程固定 8 轮扫描最多 512 个候选，并用 group shared memory 归约 Top-3 后写入 LOS 候选槽。候选池溢出时 squad 路径消费容量限制内的前缀，不再在同一 agent 上回退到逐 agent grid 搜索。`ResolveInstanceCombat` 先直接消费 `FinalizeTargetAcquisition` 产出的目标/LOS 状态：无目标或仅处于 cooldown 跟踪的实例走 fast path，只维护 cooldown、目标快照和视觉残留；只有实际尝试开火的实例才进入 target revalidate、SDF 命中缓存和 shot hit resolve 重路径。
- AI Debug GPU stage capture 已经直接嵌进主循环，可在 `PredictAfter / SolveAfter / GridAfter / TargetAcquisitionAfter / CombatAfter / SpatialQueryAfter / FinalizeAfter` 各阶段取样。
- Crowd compute shader 当前仍以 [CrowdVatIndirect.compute](C:/unity/Unity6/Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute) 作为 Unity 序列化和 C# 引用入口，但实际实现已经拆到同目录 include 模块：`CrowdVatIndirectCommon.hlsl`、`CrowdVatIndirectWorksets.hlsl`、`CrowdVatIndirectCulling.hlsl`、`CrowdVatIndirectSimulation.hlsl`、`CrowdVatIndirectCombat.hlsl`、`CrowdVatIndirectSpatialQuery.hlsl`、`CrowdVatIndirectFinalize.hlsl`、`CrowdVatIndirectAiDebug.hlsl`。
- compute dispatch 已统一经过 GPU Debug wrapper，但 wrapper 只在 `RenderDocCaptureController` 打开的 capture metadata 窗口或 AI Debug GPU stage capture 期间包裹“未命名 `CommandBuffer` + 单层 `BeginSample(passName)`”；indirect draw marker 只在同一 capture metadata 窗口内由 `GpuSemanticDrawFeature` 接管正常 draw，按语义 marker 拆成独立 RenderGraph pass，并用 `ReadWrite` 方式绑定当前颜色目标避免抓帧 pass 丢弃已渲染内容。普通运行帧不读取全局 capture gate，不解析 `Crowd.*` pass name，也不创建逐 pass `CommandBuffer`，避免分析路径常驻扰动 crowd 帧时间。RenderDoc 里优先看稳定 `[pass_id]` marker；CLI 分析输出中用 `gpu_pass_events.json` 核对 RenderDoc 原始导出，用 `gpu_pass_overview.json` 看每个 pass 的处理后总览，用 `gpu_pass_resources.json` 回查 `.rdc` 可观察的 SRV / UAV / CBV 资源绑定，并用 catalog 与本地代码解释其业务语义。

### 3. 渲染与可见性

[CrowdVatIndirectRenderer.Visibility.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.Visibility.cs) 里的当前渲染主路径仍是 `Graphics.RenderMeshIndirect`，但默认已经切到 GPU-Driven 可见性压缩：

- 默认路径不再在 CPU 侧用 whole-crowd bounds 做整团视锥 early-out；只负责上传当前相机 frustum plane，并把可见性裁剪统一交给 GPU 路径。
- GPU 端按帧从 `alive instance list` 重建 runtime squad 级 AABB，先做 squad coarse cull，再只在可见 squad 内做逐实例 frustum 测试，并按相机距离分流到 3 档可见实例列表。
- 旧的 runtime squad chunk / render chunk CPU 收集链路已删除；当 GPU 可见性压缩不可用时，只保留保守的 whole-crowd CPU fallback，把全部存活实例直接送入可见列表。
- 3 档 crowd 都通过 `CrowdVatIndirectLit.shader` 走 `RenderMeshIndirect`，按运行时激活的 LOD tier 分别生成 indirect args；默认资产组合是 `QianxiaCrowdLod0Vat` / `QianxiaCrowdLod1Vat` / `QianxiaCrowdLod2Vat`，三档都由同一条 `Qianxia_Walk_Slow` clip 烘焙，clip 布局保持一致。
- combat tracer 独立于 crowd 主 mesh 提交，但复用 crowd 世界 bounds 做扩张裁剪。

当前渲染基线仍然是：

- crowd 主 mesh 按距离档位走最多 3 次 `RenderMeshIndirect`
- visibility 默认是“GPU runtime squad AABB coarse cull -> GPU per-instance culling / 3 档 compaction”；当 GPU 可见性压缩不可用时，兜底仍是保守的 CPU whole-crowd fallback
- 不回退到逐个 `MeshRenderer`

### 4. Runtime Squad 与命令层

[CrowdVatSquadTypes.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadTypes.cs) 定义了当前 squad 数据契约：

- `CrowdVatSquadState`
- `CrowdVatAgentSquadAssignment`
- `CrowdVatFormationSlot`
- `FormationType`：`Loose / Line / Column / Wedge / Block / Ring`
- `CommandType`：`Hold / MoveTo / Advance / Charge / Retreat / Regroup`

[CrowdVatSquadController.AuthoringAndLifecycle.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadController.AuthoringAndLifecycle.cs) 与 [CrowdVatSquadController.RuntimeSync.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadController.RuntimeSync.cs) 体现的当前主路径是：

- squad authoring 数据由 `center / forward / target transform + member range + formation + command` 构成。
- `CreateDefaultTacticalSquads()` 会按 renderer 的 faction layout 自动拆 squad，并创建默认控制点。
- 每帧由 controller 汇总 active squads，生成：
  - squad states
  - per-agent assignments
  - formation slots
  - preserved squad alive counts
- 然后通过 `CrowdVatFramePacketFlags` 上传到 renderer，而不是直接改 GPU buffer。

[CrowdVatSquadCommandController.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadCommandController.cs) 已经是当前样例场景的操作入口：

- 屏幕中心射线选择小队
- 点地后调用 `TryCommandMoveTo()`
- 显示 HUD 准星与世界空间选中环
- 样例自动化会把 `_selectableFactions` 设为 `CampA`，避免玩家和 Codex agent 同时争夺 `CampB`

[CrowdVatCodexAgentController.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatCodexAgentController.cs) 当前是最小可玩 Codex CLI 对战 agent：

- 默认控制 `CampB`，以 `0.5s` 规划间隔扫描所有 B 阵营小队。
- 每支 B 小队查找最近的 `CampA` 小队，生成结构化 plan。
- 本地规则 planner 保留距离过近 `Retreat`、进入射程 `Hold`、中远距离 `Advance`、远距离 `Charge` 的兜底行为。
- Codex CLI planner 通过 `codex exec --sandbox read-only --ephemeral --output-schema ... -` 低频启动本机 Codex agent，把压缩 observation 作为 stdin 传入；默认 `minRequestInterval = 12s`，避免把 Codex 当作逐帧行为树使用。
- Codex CLI 模式不需要在场景里写 API key；它依赖本机 Codex CLI 的登录状态或配置。直接 OpenAI Responses API planner 仍保留为可选模式，运行时只读环境变量 `OPENAI_API_KEY`。
- 外部 planner 返回会经过命令白名单、阵营归属和最大目标距离校验；非法 plan 会被丢弃。
- 执行层只调用 `CrowdVatSquadController.TryCommandMoveTo()`，不直接改 Transform / GPU buffer；因此它仍然走现有 runtime squad、战斗索敌、移动动画和 AI Debug 链路。
- 当前 `SampleScene` 的 `QianxiaCrowdIndirect` 已直接挂载该组件，玩家命令器只选择 `CampA`，`CampB` 由 Codex agent 定频接管。
- 这一版把 Codex agent 作为低频 commander 接入，不把 Codex 放进逐帧实时环；后续扩展 memory/tool 时仍应保持“agent 产出 plan，执行层消费 plan”的边界。

这说明 runtime squad 当前已经不再只是“方案稿”，而是样例场景主路径的一部分。

### 5. 查询与环境约束

当前 Scene Query 不是单系统单接口，而是两条实现链并存：

#### 5.1 Crowd actor query

[CrowdVatSpatialQueryTypes.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSpatialQueryTypes.cs) 和 renderer API 当前支持：

- `OverlapSphere`
- `SweepCapsule`
- `factionMask`
- `ActiveOnly`
- `maxHits`

接口入口在 [CrowdVatIndirectRenderer.ApiAndLifecycle.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.ApiAndLifecycle.cs)：

- `SetSpatialQueries()`
- `ReadSpatialQueryResults()`
- `ReadSpatialQueryObservations()`

这条链是 `GPU resident` 的 crowd actor query，也是当前 spatial query 自动化测试覆盖的重点。

#### 5.2 Ground / Obstacle / Environment query

[CrowdVatSceneQueryFieldAsset.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSceneQueryFieldAsset.cs) 和 [CrowdVatSceneQueryUtility.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSceneQueryUtility.cs) 当前支持：

- `Ground Query`
  - terrain height / normal
  - walkable mask
  - ground flags texture
- `Obstacle Distance Query`
  - static obstacle 3D SDF
- `Environment Distance Query`
  - baked environment distance field
  - terrain + obstacle 的 CPU 组合采样回退

这条链在 batch descriptor 上明确是 `CpuImmediate`，推荐批量上限是 `32`。

同时要注意两个当前边界：

- 这条 CPU query 接口已经可用，但它不等于“统一 Scene Query 未来终态”。
- renderer 的环境约束主链当前优先走规则 SDF / baked environment distance field，不重新回到“GPU terrain heightmap 贴地就是一切”的旧路径。

### 6. 协议、观测与 world mirror

[CrowdVatFrameProtocol.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatFrameProtocol.cs) 定义了当前 crowd 运行时的协议层：

- `CrowdVatFramePacket`
- `CrowdVatFrameInput`
- `CrowdVatGpuBridgeState`
- `CrowdVatFrameObservation`

[CrowdVatRendererFrameBridge.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatRendererFrameBridge.cs) 负责把协议应用到 renderer，并暴露 `ObservationHub`。

[CrowdVatObservationHub.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatObservationHub.cs) 当前支持三类观测：

- `SquadAliveCounts`
- `Combat`
- `SpatialQueries`

[CrowdVatWorld.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatWorld.cs) 维护的是一个轻量 world mirror：

- `SquadTruth`
- `RuntimeSquadAliveObservation`
- `ObservationFrameInfo`

它当前主要用于把 CPU authoring 的真值和 GPU / readback 的存活观测对齐，而不是完整 gameplay 世界状态容器。

### 7. AI Debug 链路

[CrowdVatAiDebugRecorder.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatAiDebugRecorder.cs) 与 [CrowdVatIndirectRenderer.AiDebug.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.AiDebug.cs) 说明当前 crowd 已经具备一条可落盘的 AI Debug 链路：

- 可按 `RuntimeId / SquadId / GpuDiscovery` 选目标
- 支持 `RegionSphere / RegionBox / ScreenRect / SquadAll` discovery
- 可采集：
  - `frame.dispatch`
  - `gpu.resource`
  - `agent.state`
  - `gpu.phase`
  - `gpu.stage`
  - `gpu.discovery`
  - `spatial.grid`
  - `combat.query`
  - `animation.vat`
  - `visibility.render`
- 可导出：
  - `*.jsonl`
  - `*.md`

因此，当前 crowd 调试基线已经不只是 Inspector 参数排查，而是可复盘的结构化证据流。

- 当前 AI Debug 链路已经补上“复现场景轨迹”层：`CrowdVatIndirectRenderer.AiDebug.cs` 内的 `CrowdVatAiDebugReproController` 可在第一次手动跑时录制角色/相机轨迹与 `CrowdVatSquadCommandController` 的小队命令事件，把 trace 落到 `.workspace/artifacts/ai-debug/repro-traces/`；第二次可自动回放同一条 trace，并在热点前按 lead time 自动启动 `CrowdVatAiDebugRecorder` 导出 `jsonl + md`。

### 8. SampleScene 与自动化安装

[QianxiaCrowdSceneAutomation.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Editor/QianxiaCrowdSceneAutomation.cs) 会把当前 crowd 样例场景收敛到固定安装方式：

- 保证 `SampleScene` 中存在 `QianxiaThirdPerson`
- 保证存在 `QianxiaCrowdIndirect`
- 自动挂 `CrowdVatIndirectRenderer`
- 自动挂 `CrowdVatSquadController`
- 在角色上自动挂 `CrowdVatSquadCommandController`，并默认只允许玩家选择 / 指挥 `CampA`
- 在 crowd 对象上自动挂 `CrowdVatCodexAgentController`，默认让 Codex agent 控制 `CampB`

当前自动化默认基线还会写入一组明确参数，例如：

- `instanceCount = 1024`
- `distributeAcrossWholeTerrain = true`
- `enableApproximateCollision = true`
- `solverIterations = 2`
- `shadowCastingMode = Off`
- `secondaryLodStartDistance = 12`
- `tertiaryLodStartDistance = 24`
- `LOD0 / LOD1` 继续走 `CrowdVatIndirectLit.shader`，`LOD2` 通过 `_tertiaryLodIndirectShader` 显式切到 `CrowdVatIndirectSimple.shader`；即使场景里未手动指定，运行时也会默认按 `Project/Crowd/VATIndirectSimple` 查找并回退接线。

同时，`QianxiaVatBuilder` 当前会把以下 3 份源模型统一烘焙成 VAT 资源，供 3 档 GPU-Driven 间接绘制复用；三档都共享 `Assets/Project/Characters/Qianxia/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx` 作为唯一 bake clip 来源：

- `Assets/Project/Characters/Qianxia/SourceModels/LOD2.fbx -> QianxiaCrowdLod0Vat`
- `Assets/Project/Characters/Qianxia/SourceModels/crowd1000.fbx -> QianxiaCrowdLod1Vat`
- `Assets/Project/Characters/Qianxia/SourceModels/crowd500.fbx -> QianxiaCrowdLod2Vat`
- `crowd1000.fbx` / `crowd500.fbx` 已在 Blender 中从 `LOD2.fbx` 直接减面并导出为真正带 skin weights 的 skinned FBX，可直接进入 `CrowdVatBaker`。

[QianxiaCrowdSampleScenePlayModeSetup.cs](C:/unity/Unity6/Assets/Tests/PlayMode/QianxiaCrowdSampleScenePlayModeSetup.cs) 会在 PlayMode 测试前强制调用这套安装逻辑，并校验场景里确实持久化写入了 `QianxiaCrowdIndirect`。

## 相关文件

- [CrowdVatIndirectRenderer.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.cs)
- [CrowdVatIndirectRenderer.ApiAndLifecycle.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.ApiAndLifecycle.cs)
- [CrowdVatIndirectRenderer.Simulation.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.Simulation.cs)
- [CrowdVatIndirectRenderer.Visibility.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.Visibility.cs)
- [CrowdVatIndirectRenderer.AiDebug.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.AiDebug.cs)
- [CrowdVatFrameProtocol.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatFrameProtocol.cs)
- [CrowdVatSquadController.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadController.cs)
- [CrowdVatSquadCommandController.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadCommandController.cs)
- [CrowdVatSceneQueryFieldAsset.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSceneQueryFieldAsset.cs)
- [CrowdVatSceneQueryUtility.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSceneQueryUtility.cs)
- [CrowdVatAiDebugRecorder.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatAiDebugRecorder.cs)
- [QianxiaCrowdSceneAutomation.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Editor/QianxiaCrowdSceneAutomation.cs)
- [SampleScene.unity](C:/unity/Unity6/Assets/Scenes/SampleScene.unity)

## 当前验证面

当前 crowd 至少有以下测试面已经覆盖主路径的一部分：

- PlayMode
  - [CrowdVatIndirectAnimationSmokeTests.cs](C:/unity/Unity6/Assets/Tests/PlayMode/CrowdVatIndirectAnimationSmokeTests.cs)
  - [CrowdVatIndirectRenderCullingTests.cs](C:/unity/Unity6/Assets/Tests/PlayMode/CrowdVatIndirectRenderCullingTests.cs)
  - [CrowdVatIndirectSpatialQueryTests.cs](C:/unity/Unity6/Assets/Tests/PlayMode/CrowdVatIndirectSpatialQueryTests.cs)
  - [CrowdVatIndirectTerrainCollisionTests.cs](C:/unity/Unity6/Assets/Tests/PlayMode/CrowdVatIndirectTerrainCollisionTests.cs)
  - [QianxiaCrowdSampleScenePlayModeTests.cs](C:/unity/Unity6/Assets/Tests/PlayMode/QianxiaCrowdSampleScenePlayModeTests.cs)
  - [QianxiaCrowdVatIndirectPlayModeTests.cs](C:/unity/Unity6/Assets/Tests/PlayMode/QianxiaCrowdVatIndirectPlayModeTests.cs)
- EditMode
  - [CrowdVatFrameProtocolTests.cs](C:/unity/Unity6/Assets/Tests/EditMode/CrowdVatFrameProtocolTests.cs)
  - [CrowdVatGpuBridgeStateTests.cs](C:/unity/Unity6/Assets/Tests/EditMode/CrowdVatGpuBridgeStateTests.cs)
  - [CrowdVatObservationHubTests.cs](C:/unity/Unity6/Assets/Tests/EditMode/CrowdVatObservationHubTests.cs)
  - [CrowdVatSquadControllerAliveCountTests.cs](C:/unity/Unity6/Assets/Tests/EditMode/CrowdVatSquadControllerAliveCountTests.cs)
  - [CrowdVatCodexAgentControllerTests.cs](C:/unity/Unity6/Assets/Tests/EditMode/CrowdVatCodexAgentControllerTests.cs)
  - [CrowdVatIndirectRendererSquadAliveCountTests.cs](C:/unity/Unity6/Assets/Tests/EditMode/CrowdVatIndirectRendererSquadAliveCountTests.cs)
  - [CrowdVatIndirectRendererCombatOriginTests.cs](C:/unity/Unity6/Assets/Tests/EditMode/CrowdVatIndirectRendererCombatOriginTests.cs)

## 风险与边界

- 当前 crowd 已经是复合系统，改 renderer 并不只影响渲染，还会影响 squad、query、combat、observation 和 AI debug。
- `Ground / Obstacle / Environment` 查询当前是 CPU immediate，`Crowd actor query` 是 GPU resident，这两条语义和成本模型不同，不能混写成一套接口假设。
- runtime squad 当前已经依赖 `FramePacket` 协议层；如果绕过协议直接写 renderer 内部缓存，容易把 observation/world mirror 同步关系打断。
- SampleScene 自动化会持续把场景拉回默认 crowd 安装方式；手工改样例场景前要先确认是否也要改自动化脚本。
- 当前文档只描述现行基线，不自动等于未来扩展路线；若开始落 `XPBD / 流场 / 统一 Scene Query`，应同步相应候选设计文档，而不是把本文扩成大而全路线图。

## 文档同步备注

- 下面这些变化必须同步本文：
  - `UpdateGpuBuffers()` 主顺序变化
  - 工作集划分变化，例如 `Alive / PhysicsActive / CombatActive / Visible`
  - runtime squad 上传协议字段变化
  - spatial query 或 scene query 的执行模式变化
  - SampleScene 自动化安装入口变化
  - AI Debug 导出产物或关键 provider 变化
- 如果某个子系统升级成独立稳定主路径，例如“统一 Scene Query runtime”或“新的 crowd 量产物理链”，应新开基线文档，并在本文头部更新替代关系。
## 2026-05-15 外部 Player 联动补充

- `CrowdVatAiDebugReproController` 不再只是 `Tools/Qianxia/AI Debugger` 的 PlayMode 辅助能力。
- 当前 external player 抓帧链路已经通过命令行 bootstrap 接入同一套 repro trace 逻辑：
  - 第一次外部 Player 手动跑热点并抓帧时，会自动录制角色/相机轨迹和小队命令。
  - `RenderDocCaptureController` 首次抓帧完成后，会自动把 trace 落到 `.workspace/artifacts/ai-debug/repro-traces/`。
  - `Build Fixed Player` 产物目录下会额外写入 `external-player-build-stamp.txt`；`auto` 模式不再依赖 player exe 的修改时间，而是用这个 build stamp 判断“这条 trace 是否属于当前 build”。
  - 第二次再走同一个 `Launch-Under-RenderDoc.cmd` 时，如果最新 trace 比当前 build stamp 更新，则自动回放最近 trace，并在热点前开启 AI Debug；如果 trace 仍旧落后于当前 build stamp，则第一次启动会回到手动录轨迹模式，避免新 build 首跑误进 replay。
- 默认 external player profile 使用 `GpuDiscovery + ScreenRect(0..1)` 作为无需额外配置即可工作的 AI Debug 目标。
