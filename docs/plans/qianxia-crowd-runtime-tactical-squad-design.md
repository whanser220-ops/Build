# 千夏 Crowd 运行时战术小队系统实现方案
## 文档信息

- 版本：v0.1
- 日期：2026-04-18
- 状态：可开工实现方案
- 适用项目：Unity 6 `6000.0.46f1`、URP
- 作者：Codex
- 上游参考：
  - [现有人群 VAT / Indirect 技术设计](C:/unity/Unity6/docs/plans/qianxia-crowd-vat-technical-design.md)
  - [大规模人群物理系统技术设计](C:/unity/Unity6/docs/plans/qianxia-large-scale-crowd-physics-design.md)
  - [人群组织与战术小队方法调研](C:/unity/Unity6/.workspace/artifacts/web/20260418-crowd-organization-and-tactical-squad-research.md)

## 当前假设

- 当前 crowd 渲染继续固定使用单 `LOD2 VAT + Graphics.RenderMeshIndirect + ComputeShader`。
- 当前 crowd 近场物理继续沿用现有 `terrain/static SDF + capsule PBD` 方向，不回退到逐单位 `NavMeshAgent + Rigidbody`。
- v1 目标是“运行时控制小队形式与编组动作”，不是一次性做完整掩体战术 AI。
- v1 仍按当前项目的单层 outdoor crowd 语义设计，不覆盖桥上桥下、楼上楼下的完整多层 crowd。
- 默认目标平台按 PC / 主机档设计，优先保证 `60 FPS` 下的稳定性与可调试性。

## 1. 目标

- 效果目标：让当前 GPU-Driven crowd 在运行时具备“按小队组织并切换阵型、推进、集结、冲锋、重组”的能力，同时保留现有大规模渲染与近场碰撞优势。
- 核心路线：`CPU 小队级决策 + GPU 个体级执行 + GPU PBD 局部修正 + Indirect 渲染不变`。
- 与现有系统关系：
  - 保留 [CrowdVatIndirectRenderer.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.cs) 作为总控。
  - 保留 [CrowdVatIndirect.compute](C:/unity/Unity6/Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute) 作为逐实例预测、空间网格、碰撞和最终写回的主 compute。
  - 新增“小队状态”和“成员归属”两层数据，不重写整条 crowd 渲染链。

## 2. 这套架构为什么适合做运行时战术小队

当前代码已经具备三个关键前提：

1. Crowd 的主更新已经在 GPU 上完成：`PredictInstances -> BuildGrid -> SolveCrowdCollisions -> FinalizeInstances`。
2. 个体本来就有“朝锚点回拉”的运动语义，只是当前锚点还是静态出生点。
3. 近场已经有 crowd-crowd、terrain、static SDF 和 capsule 近似碰撞能力。

因此，运行时战术小队最合适的接法不是“让 CPU 每帧控制每个人”，而是：

1. CPU 只更新每个小队的少量状态。
2. GPU 根据小队状态为每个成员计算运行时目标槽位。
3. GPU 再用现有 PBD 和局部碰撞把队形挤压成合理结果。

这和当前系统天然兼容，也比“全人群 CPU 行为控制”更友好。

## 3. v1 范围

### 3.1 v1 必须支持

- 运行时创建或配置多个 squad。
- 每个 squad 维护自己的中心、朝向、目标点、速度和阵型。
- 每个 crowd agent 归属到一个 squad，并持有稳定的 slot。
- 支持以下 squad command：
  - `Hold`
  - `MoveTo`
  - `Advance`
  - `Charge`
  - `Retreat`
  - `Regroup`
- 支持以下 formation：
  - `Loose`
  - `Line`
  - `Column`
  - `Wedge`
  - `Block`
  - `Ring`
- 运行时切换阵型时，不重建整批 crowd layout。
- 继续保留现有 active bubble、terrain collision、static SDF collision 和 indirect render。

### 3.2 v1 明确不做

- 每个小兵完整 `BT/GOAP/HTN`。
- 掩体搜索、视线分析、flank path、suppress fire 语义。
- 高频 `GPU -> CPU` 读回 query 结果驱动每帧决策。
- 运行时频繁 `RebuildCrowdLayout()`。
- 全量 3D 多层 crowd 编组。

## 4. 总体架构

### 4.1 四层结构

1. 战术命令层：CPU 上的 squad controller。
2. 编组执行层：GPU 上的小队锚点和阵型槽位计算。
3. 近场物理层：GPU 上的 capsule PBD、terrain/static SDF 和 crowd-crowd 修正。
4. 渲染层：现有 VAT / Indirect 实例渲染。

### 4.2 CPU / GPU 分工

CPU 负责：

- 小队高层命令。
- 小队中心移动、朝向、目标更新。
- 阵型切换和角色分工。
- 少量小队状态上传。

GPU 负责：

- 根据 squad 状态和成员 slot 计算每个 agent 的运行时目标锚点。
- 个体速度预测。
- 局部 crowd-crowd 挤压。
- terrain / static SDF / capsule 约束修正。
- 最终实例矩阵和 VAT 帧参数写回。

边界原则：

- CPU 不做逐 agent 的每帧路径和局部避让。
- GPU 不做高层战术判断。

## 5. 数据设计

### 5.1 新增 GPU Buffer 一览

建议新增三类结构化缓冲：

1. `SquadStateBuffer`
2. `AgentSquadDataBuffer`
3. `FormationSlotBuffer`

### 5.2 `SquadState`

建议字段：

```csharp
public struct CrowdVatSquadState
{
    public Vector3 worldCenter;
    public Vector3 worldForward;
    public Vector3 worldTarget;
    public Vector2 formationSpacing;
    public float moveSpeed;
    public float anchorBlend;
    public uint formationType;
    public uint commandType;
    public uint factionMask;
    public uint flags;
}
```

字段语义：

- `worldCenter`：小队中心。
- `worldForward`：小队正面方向。
- `worldTarget`：当前目标点。
- `formationSpacing`：横向与纵向间距。
- `moveSpeed`：队伍整体速度。
- `anchorBlend`：从自由状态收回到槽位的强度。
- `formationType`：阵型类型。
- `commandType`：命令状态。
- `factionMask`：阵营过滤与后续战术扩展入口。
- `flags`：例如允许松散、允许挤压变形、强制朝向目标等。

### 5.3 `AgentSquadData`

建议字段：

```csharp
public struct CrowdVatAgentSquadData
{
    public uint squadId;
    public uint slotIndex;
    public uint role;
    public uint flags;
    public Vector2 slotOffsetOverride;
    public float weight;
}
```

字段语义：

- `squadId`：该成员属于哪个小队。
- `slotIndex`：稳定槽位编号。
- `role`：角色分工，如前排、后排、旗手、重装、跟随者。
- `flags`：是否允许临时脱队、是否优先维持槽位等。
- `slotOffsetOverride`：少量特殊单位可覆盖预设槽位。
- `weight`：给后续阵型重分配与拥堵下的优先级使用。

### 5.4 `FormationSlot`

建议字段：

```csharp
public struct CrowdVatFormationSlot
{
    public Vector2 localOffset;
    public uint roleMask;
    public uint rank;
    public float preferredDistance;
}
```

语义：

- 每种 formation 预烘焙一组标准槽位。
- 每个小队成员通过 `slotIndex` 读取对应槽位。
- 槽位坐标定义在 squad 本地空间，再由 GPU 旋转到世界或 crowd 本地空间。

### 5.5 和现有 `spawnData` 的关系

当前 [CrowdVatIndirect.compute](C:/unity/Unity6/Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute) 里，agent 会把 `spawnData.localPosition.xz` 当成锚点回拉。

v1 需要把“静态出生点”和“运行时编组锚点”拆开：

- `spawnData.localPosition`：保留为原始出生点、回收点或 fallback anchor。
- `runtime squad anchor`：每帧根据 `SquadState + AgentSquadData + FormationSlot` 计算。

设计要求：

- 小队控制不能通过重新生成 `spawnData` 实现。
- `spawnData` 只用于初始化和失效 fallback。

## 6. Compute 接入方案

### 6.1 最小改动路线

不新增整套 compute 管线，优先直接扩展 `PredictInstances`：

1. 读取 agent 的 `squadId` 和 `slotIndex`。
2. 读取对应 `SquadState`。
3. 读取对应 `FormationSlot`。
4. 计算该 agent 的运行时 `anchorXZ`。
5. 用 `anchorXZ` 替换当前基于出生点的静态 anchor。
6. 后续 crowd PBD、terrain、SDF 和 finalize 保持不变。

### 6.2 新的锚点计算

当前逻辑：

```hlsl
float2 anchorXZ = spawnData.localPosition.xz;
```

目标逻辑：

```hlsl
float2 squadCenterXZ;
float2 slotOffsetXZ;
float2 runtimeAnchorXZ = squadCenterXZ + Rotate2D(slotOffsetXZ, squadForwardXZ);
float2 anchorXZ = lerp(spawnData.localPosition.xz, runtimeAnchorXZ, squad.anchorBlend);
```

这样做的好处：

- 旧 crowd 和新 squad crowd 可以共存。
- squad 失效时可快速退回静态 anchor。
- 不需要重建整批 buffer。

### 6.3 预测阶段新增逻辑

建议在 `PredictInstances` 中新增：

1. `ResolveSquadAnchor(instanceIndex)`
2. `ResolveDesiredHeading(instanceIndex)`
3. `ResolveFormationTightness(instanceIndex)`

其中：

- `ResolveSquadAnchor` 负责算槽位目标。
- `ResolveDesiredHeading` 负责决定队形朝向是否跟随 squad forward、movement direction 或 face target。
- `ResolveFormationTightness` 负责根据命令状态决定阵型紧凑程度。

### 6.4 不建议的接法

以下做法不建议采用：

- 每帧在 CPU 上给每个 agent 重新算目标点，然后整批上传。
- 每次阵型变化都 `RebuildCrowdLayout()`。
- 每帧读取 `ReadSpatialQueryResults()` 再逐人回写命令。

这些路线都会明显削弱当前 GPU-Driven 架构的价值。

## 7. 运行时战术控制模型

### 7.1 Command 状态机

v1 建议 squad 只维护一个轻量命令状态机：

- `Hold`
  - 原地维持阵型。
  - 只允许局部 PBD 和 terrain/SDF 修正。
- `MoveTo`
  - 朝目标点整体位移。
  - 阵型尽量保持。
- `Advance`
  - 朝目标方向推进，优先保持正面朝向。
- `Charge`
  - 阵型放松，速度更高，允许更大局部形变。
- `Retreat`
  - 反向回撤，朝向可保持敌方方向。
- `Regroup`
  - 优先重建槽位，缩回到中心附近。

### 7.2 Formation 策略

v1 推荐这样定义：

- `Loose`
  - 最稳妥，默认阵型。
  - 高密度和复杂地形时的降级目标。
- `Line`
  - 用于迎敌、展示阵列宽度。
- `Column`
  - 用于通过狭窄区域。
- `Wedge`
  - 用于冲锋、破阵、视觉上更有战术意味。
- `Block`
  - 用于密集军阵或方阵。
- `Ring`
  - 用于保护中心对象或包围表演。

### 7.3 阵型降级原则

terrain 和局部挤压会让严格阵型变形，因此 v1 必须允许阵型降级：

1. 首先保持 squad center 和整体朝向。
2. 其次保持 rank 关系。
3. 最后才保持绝对 slot 精度。

也就是说：

- 队伍可以“松散但不散架”。
- 不追求每个成员永远精确站在数学槽位上。

## 8. 性能设计

### 8.1 为什么这套方案性能友好

因为 CPU 每帧只上传“小队状态”，而不是“每个体目标点”。

成本结构应该是：

- CPU：`O(小队数)`
- GPU：`O(agent 数)`，但 agent 本来就在跑 compute 主循环

只要小队数量明显小于 agent 数量，这套方案天然有利。

### 8.2 推荐规模

在当前路线下，建议按以下量级设计：

| 层级 | 推荐规模 | 说明 |
| --- | --- | --- |
| 总同屏 crowd | `10k - 50k` | 继续主要服务画面规模感 |
| 参与 squad 控制的中近场 crowd | `500 - 4000` | 视场景和 GPU 预算而定 |
| 真正进入高频 PBD 的 L2 crowd | `300 - 1500` | 保持局部接触质量 |
| squad 数量 | `16 - 128` | 远小于 crowd 总数 |

### 8.3 必须避免的性能坑

- 高频 `ReadSpatialQueryResults()`。
- 每帧整批上传逐人目标点。
- 每帧频繁调整所有成员 slot。
- 阵型切换时重建全 crowd 缓冲。
- 把所有远场 crowd 都塞进 L2 PBD solver。

### 8.4 Query 的正确定位

当前 query 更适合：

- 调试。
- 低频玩法判定。
- 事件触发。
- 小队级状态切换的补充信息。

不适合：

- 作为每帧逐 agent 战术大脑输入。

如果未来确实需要 query 参与战术决策，建议：

- 降频到 `5-10 Hz`。
- 只对关键 squad 或 hero proxy 周围启用。
- 尽量走异步或分帧，不走全量同步读回。

## 9. 具体实现清单

### 9.1 新增文件

建议新增：

- `Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadTypes.cs`
- `Assets/Project/Crowds/VAT/Runtime/CrowdVatFormationAsset.cs`
- `Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadController.cs`
- `Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadRuntimeAuthoring.cs`

职责建议：

- `CrowdVatSquadTypes.cs`
  - 定义 command、formation、role、GPU struct 对齐字段。
- `CrowdVatFormationAsset.cs`
  - 提供阵型预设和 slot 表。
- `CrowdVatSquadController.cs`
  - 维护 squad 状态、命令切换、运行时上传。
- `CrowdVatSquadRuntimeAuthoring.cs`
  - 给场景里的空物体和小队入口提供桥接。

### 9.2 修改现有文件

需要修改：

- [CrowdVatIndirectRenderer.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.cs)
- [CrowdVatIndirect.compute](C:/unity/Unity6/Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute)
- [CrowdVatSpatialQueryTypes.cs](C:/unity/Unity6/Assets/Project/Crowds/VAT/Runtime/CrowdVatSpatialQueryTypes.cs)

#### `CrowdVatIndirectRenderer.cs` 需要新增

- `ComputeBuffer _squadStateBuffer`
- `ComputeBuffer _agentSquadDataBuffer`
- `ComputeBuffer _formationSlotBuffer`
- 上传缓存数组
- 新的 public API：
  - `SetSquadStates(...)`
  - `SetAgentSquadData(...)`
  - `SetFormationSlots(...)`
  - `ClearSquadRuntimeData()`
- 初始化和释放逻辑
- `ConfigureCommonComputeParameters()` 中的小队参数绑定
- 场景调试接口，例如：
  - `TryGetDebugSquadStates(...)`
  - `TryGetDebugFormation(...)`

#### `CrowdVatIndirect.compute` 需要新增

- `SquadState`、`AgentSquadData`、`FormationSlot` 对应 HLSL struct。
- `StructuredBuffer` 绑定。
- `ResolveSquadAnchor()`。
- `Rotate2D()` 或等价辅助函数。
- `command -> anchor / speed / tightness` 解析逻辑。
- `PredictInstances` 中的锚点替换逻辑。

### 9.3 编辑器与场景控制

因为你前面已经明确偏好“直接在场景里控制”，所以 v1 的 authoring 不建议只靠 Inspector。

推荐做法：

- 每个 squad 一个空物体作为中心点。
- 一个朝向空物体或直接用 Transform forward 表示队伍朝向。
- 若要显式展示阵型宽度，可额外给一个 formation preview gizmo。

这样运行时就可以直接：

- 拖 squad center。
- 转 squad forward。
- 在面板里切换 formation 和 command。

## 10. 分阶段开工顺序

### Phase 1：基础数据接入

目标：先让当前 crowd 具备“从 squad buffer 读取运行时锚点”的能力。

实现项：

- 新增 squad/formation/runtime 数据结构。
- 给 renderer 补三类 buffer 和 upload 通路。
- 在 compute 里替换静态 anchor。
- 先只做 `Hold + MoveTo + Loose + Line`。

验收：

- 不重建 crowd layout 的情况下，运行时能移动一个小队。
- 小队能维持基本队形并继续被 PBD 修正。

### Phase 2：阵型与命令扩展

目标：把小队形式做成真正可切换系统，而不是单一 follow。

实现项：

- 加 `Column / Wedge / Block / Ring`。
- 加 `Advance / Charge / Retreat / Regroup`。
- 加 tightness、speed、turn rate 等参数。

验收：

- 阵型切换不会触发明显爆散。
- 冲锋、回撤、重组在场景里可直接观察。

### Phase 3：和现有 active bubble / PBD 统一

目标：让小队系统真正和现有近场物理层协同，而不是互相打架。

实现项：

- squad 内成员默认按 `active bubble` 或事件强度进入 L2。
- 远场 squad 允许低频更新。
- 小队命令影响 `anchorBlend`、tightness、max displacement。

验收：

- 近场密集队伍仍稳定。
- 远场 squad 不会为保持阵型白白吃掉太多 compute 预算。

### Phase 4：低频战术语义扩展

目标：为后续真正战术小队 AI 留接口，但不破坏 v1 性能。

实现项：

- squad 级低频 query。
- 角色 role 权重。
- 基于 event 的 command 切换。

验收：

- 可以在低频逻辑里根据触发事件切换 `Advance / Regroup / Charge`。
- 不需要高频 GPU 读回也能表现出“像战术小队”。

## 11. 风险与反问清单

### 11.1 已知风险

- 当前 crowd 的锚点语义仍偏“出生点回拉”，改成 runtime squad anchor 后，老逻辑里与 `_maxDisplacementFromSpawn`、`_inactiveReturnStrength` 的关系要重新校准。
- 阵型在狭窄地形下会天然和 PBD 冲突，如果不做降级，表现会变硬。
- 当前 crowd 的 broadphase 仍主要服务 crowd 局部碰撞，如果后续战术逻辑开始依赖大量空间评分，需要单独规划 query 预算。
- 动画层如果没有至少“普通移动 / 紧张移动 / 冲锋”几类状态，阵型虽然成立，视觉上仍可能显得像滑动。

### 11.2 这次方案主动暴露的盲区

以下问题如果后续不回答清楚，v2 很容易返工：

- 小队切换阵型时，旧 slot 如何过渡到新 slot。
- 成员是否允许临时脱队再回队。
- 小队穿过狭窄地形时，是改阵型、松阵型，还是允许队形断裂。
- 小队 command 的切换频率上限是多少。
- 后续如果要做真正掩体战术，cover 数据从哪里来。

### 11.3 暂时搁置

- 真正 cover-based tactics。
- 复杂 LOS / threat heatmap。
- 多层 crowd volume。
- 网络同步级别确定性。

## 12. 推荐默认参数

为了让第一版更容易落地，建议先用以下保守默认：

- squad 状态 CPU 更新频率：`10-20 Hz`
- renderer buffer 上传频率：每帧，但只传 squad 状态，不传逐 agent 目标
- formation 切换过渡时间：`0.25 - 0.6 s`
- squad 最大成员数：`8 - 32`
- 初版 squad 数量：`<= 32`
- 初版 L2 active tactical crowd：`<= 512`
- query 读回频率：默认关闭，只在调试或低频逻辑下开启

## 13. 最终建议

对当前项目，最值得直接开工的路线不是“给每个人上完整战术 AI”，而是：

1. 保留现有 GPU crowd 渲染和 PBD 主体。
2. 把静态出生锚点升级成“运行时小队锚点”。
3. 让 CPU 只负责 squad 级命令和阵型状态。
4. 让 GPU 负责逐成员槽位执行和局部碰撞修正。
5. 把真正高成本的战术判断控制在 squad 级低频层。

这条路最符合你当前仓库的代码基础，也最可能在保持性能的同时，把“人群”推进到“有组织的小队”。

## 14. 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| v0.1 | 2026-04-18 | 初版运行时战术小队实现方案，明确 CPU/GPU 分工、数据结构、compute 接入点和阶段拆解。 |
