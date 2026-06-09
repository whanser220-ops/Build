# 千夏 Crowd VAT / Indirect 技术设计文档

## 文档信息

- 版本：v0.5
- 日期：2026-04-16
- 状态：已偏离实现（保留为 runtime squad / scene query 扩展前的 crowd VAT 历史基线）
- 同步级别：归档
- 作者：Codex
- 适用项目：Unity 6 `6000.0.46f1`、URP
- 当前实现锚点：
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatSquadController.cs`
  - `Assets/Project/Crowds/VAT/Runtime/CrowdVatSceneQueryFieldAsset.cs`
- 最后实现核对：2026-05-06
- 替代关系：
  - 替代：
  - 被替代：
    - `docs/plans/qianxia-crowd-current-implementation-baseline.md`

## 1. 当前目标

千夏 crowd 当前已经完成两层基础能力：

1. `LOD2.fbx` 走 VAT / GPU 骨骼动画贴图。
2. `Graphics.RenderMeshIndirect + ComputeShader` 负责大规模批量提交。

本轮新增的目标是把 crowd 从“静态散布的批量回放”推进到“具备基础空间查询与近场位置修正的 GPU crowd”：

1. 在 GPU 上维护每个实例的运行时位置状态，而不是每帧只从静态 spawn 数据直接写矩阵。
2. 建立 crowd 自己的二维空间查询结构，作为后续近场交互和 active bubble 的基础。
3. 用 PBD 风格的近似位置修正做 XZ 平面分离，先解决 crowd 个体之间的穿插和后续驱散入口。
4. 建立 active bubble / 激活集入口，让后续玩家近场驱散不再对全量 crowd 求解。

## 2. 本版范围

### 2.1 已实现

- Crowd 仍然使用 `LOD2` VAT 资源链路。
- 每个实例新增 GPU 运行时状态：
  - 本地位置
  - yaw 朝向
  - uniform scale
  - XZ 平面速度
- Compute 更新流程从单 kernel 扩展为五段：
  - `PredictInstances`
  - `ClearGrid`
  - `BuildGrid`
  - `SolveCrowdCollisions`
  - `FinalizeInstances`
- 新增二维 uniform grid：
  - 以 crowd 本地空间 XZ 为查询平面
  - 固定 cell size
  - 固定每格最大 occupancy
- 新增 PBD 风格近似分离：
  - 只处理 XZ 平面
  - 只做位置修正，不做完整速度约束和行为规划
  - 支持多次 solver iteration
- 新增可选“交互球源”入口：
  - 通过 Transform + 半径 + 强度上传到 GPU
  - 只影响 crowd 的 XZ 位置
  - 不要求玩家角色进入 VAT crowd 链路
- 新增 active bubble：
  - 可绑定显式目标 Transform
  - 也可自动解析场景中的 `CharacterController`
  - 支持激活半径和保活半径
- 新增玩家近场驱散自动入口：
  - 当 crowd 绑定到 `QianxiaThirdPerson` 或其他 `CharacterController` 时，可自动生成一个角色交互球源
  - 用于玩家附近的人群让位和局部活跃求解

### 2.2 本版仍不做

- 玩家控制角色并入 crowd VAT 渲染或动画链路
- 完整 crowd 行为系统
- 路径跟随、寻路、状态机
- 真实胶囊体或骨骼级碰撞
- GPU 细粒度可见性剔除
- 多 LOD 分桶提交
- 多 clip 状态机和复杂动画混合

## 3. 关键设计

## 3.1 为什么先做二维空间查询

当前 crowd 主要目标是“大规模远景 / 中景人群”，第一版近场交互只需要解决：

- 实例互相穿插
- 后续近场驱散没有状态承载
- crowd 完全静态导致交互入口很难接

对这一阶段来说，XZ 平面近似已经足够：

- 复杂度明显低于三维粒子或完整刚体
- 和当前“角色站地面上行走”的场景语义一致
- 能直接服务后续 active bubble、玩家驱散、局部交互代理

## 3.2 为什么不用排序型 broadphase

本版没有直接搬瓶堆系统里的完整排序链，而是选择了更轻量的固定容量 uniform grid：

- 当前 crowd 主要是规则散布，局部密度相对可控
- 首先需要的是“结构稳定、容易调试、能快速迭代”的版本
- 固定容量 cell 在 `1k / 5k / 10k` 人群上更容易先落地

这也意味着本版有一个明确限制：

- 单个 cell 超过 `_maxCellOccupancy` 后，超出的实例不会进入本轮近邻求解

这是可接受的第一版权衡，后续如果真实密度继续提高，再升级成排序 / radix / append 方案。

## 3.3 为什么保留锚点回拉

Crowd 当前还没有路径规划或目标速度，因此单靠碰撞修正会让实例持续漂走。

所以本版在 `PredictInstances` 中加入了对 spawn 位置的回拉：

- `_anchorStiffness` 控制向初始散布点回归的强度
- `_maxDisplacementFromSpawn` 限制实例离初始点最远可偏移的距离

这样做的结果是：

- crowd 仍然整体保持原有布阵
- 但已经可以被近场驱散源或自碰撞修正短暂推开
- 不需要完整行为系统，也能稳定作为下一阶段的底座

## 4. 数据设计

## 4.1 静态数据

`InstanceSpawnData`

- `localPosition`
- `yawRadians`
- `uniformScale`
- `normalizedTimeOffset`
- `playbackSpeedMultiplier`

这些数据仍然只在布局重建时生成一次。

## 4.2 运行时状态

`InstanceSimulationState`

- `localPositionAndYaw`
- `scaleAndVelocity`

这部分数据现在由 GPU 每帧更新，是 crowd 空间查询和近场修正的状态源。

`_ActiveStateBuffer`

- 记录实例当前是否属于 active bubble 或近场交互集
- far crowd 可以不进入本轮网格构建和碰撞求解
- near crowd 继续保持高频位置修正

## 4.3 交互源

`InteractionSphereData`

- 本地空间中心
- 半径
- 强度

目前只支持球体近似，主要用于后续挂玩家或其他近场驱散源。

## 4.4 网格结构

- `_GridCounterBuffer`
- `_GridOccupantBuffer`

规则：

- 每个 cell 记录当前写入数量
- 占位数组按 `cellIndex * maxCellOccupancy + slot` 线性展开
- Solver 只检查自身 cell 及其 8 邻域

## 5. 每帧流程

1. `PredictInstances`
   - 从上一帧状态出发
   - 加入锚点回拉
   - 应用交互球源推挤
   - 得到预测位置和预测速度
2. `ClearGrid`
   - 清空 cell 计数
3. `BuildGrid`
   - 仅把 active 实例写入二维 uniform grid
4. `SolveCrowdCollisions`
   - 查询 9 宫格邻居
   - 对重叠实例做 PBD 风格 XZ 分离修正
   - 写回新的位置 / 速度 / yaw
5. `FinalizeInstances`
   - 根据最新状态写 `_InstanceTransforms`
   - 根据静态动画参数写 `_InstanceFrameData`

## 6. 当前可调参数

运行时重点参数位于 `CrowdVatIndirectRenderer`：

- `_enableApproximateCollision`
- `_collisionRadius`
- `_queryCellSize`
- `_maxCellOccupancy`
- `_solverIterations`
- `_selfCollisionStrength`
- `_anchorStiffness`
- `_velocityDamping`
- `_maxPushPerStep`
- `_maxDisplacementFromSpawn`
- `_interactionSpheres`
- `_enableActiveBubble`
- `_activeBubbleTarget`
- `_characterController`
- `_activeBubbleRadius`
- `_activeBubbleRetentionRadius`
- `_useCharacterAsInteractionSphere`
- `_characterInteractionRadiusMultiplier`
- `_characterInteractionStrength`
- `_inactiveReturnStrength`

推荐理解方式：

- 先用 `_collisionRadius` 和 `_queryCellSize` 定义近邻范围
- 再用 `_solverIterations` 和 `_selfCollisionStrength` 决定分离力度
- 最后用 `_anchorStiffness`、`_velocityDamping` 和 `_maxDisplacementFromSpawn` 收敛整体稳定性

## 7. 本版产物

### 7.1 代码

- `Assets/Project/Crowds/VAT/Runtime/CrowdVatIndirectRenderer.cs`
- `Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute`
- `Assets/Tests/PlayMode/CrowdVatIndirectCollisionApproximationTests.cs`
- `Assets/Tests/PlayMode/CrowdVatIndirectActiveBubbleTests.cs`
- `Assets/Project/Crowds/VAT/Editor/QianxiaCrowdSceneAutomation.cs`

### 7.2 仍在复用的既有产物

- `Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader`
- `Assets/Project/Crowds/VAT/Runtime/CrowdVatAnimationAsset.cs`
- `Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab`

## 8. 验证记录

### 8.1 已完成

- `dotnet build C:\unity\Unity6\Assembly-CSharp.csproj`
  - 结果：通过
  - 备注：仅保留仓库原有 `log4net` 警告
- Unity batchmode 真实导入
  - 日志：`Logs/CrowdVatIndirectCollisionApproximationTests.log`
  - 结果：脚本与 `CrowdVatIndirect.compute` 已成功导入，没有新的脚本编译错误或 compute import error

### 8.2 当前阻塞

- 本轮尝试用 batchmode 跑最小 PlayMode 测试时，没有产出 `testResults.xml`
- 从日志看，Unity 完成了导入和脚本重载，但测试步骤没有真正执行
- 当前判断更像是本机 batchmode 测试环境问题，而不是这次 crowd 改动直接导致的脚本编译错误

## 9. 后续建议顺序

1. 在编辑器里把一个玩家或代理物体挂到 `_interactionSpheres`，直接验证 crowd 近场驱散观感。
2. 基于这套 grid / state 结构继续做 active bubble。
3. 如果密度继续升高，再把 fixed-capacity grid 升级成排序型 broadphase。
4. 等近场逻辑稳定后，再接更细的 chunk 剔除和多 LOD 提交。

## 10. 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| v0.5 | 2026-04-16 | 新增 active bubble、玩家 `CharacterController` 自动解析、近场驱散自动球源和激活集裁剪。 |
| v0.4 | 2026-04-16 | 新增 GPU 运行时位置状态、二维空间查询、XZ 平面 PBD 近似分离和交互球源入口。 |
| v0.3 | 2026-04-16 | 完成 `Graphics.RenderMeshIndirect + ComputeShader` crowd 批量提交与 benchmark。 |
| v0.2 | 2026-04-16 | 完成 VAT prefab、间接渲染材质和 runtime renderer 第一版。 |
| v0.1 | 2026-04-16 | 建立 `LOD2` 的 VAT / BAT 烘焙链路与单体回放。 |
