# `docs/plans` 旧方案清点（2026-05-06）

本文档用于回答一个很具体的问题：

当前 `docs/plans/` 里，哪些文档已经不适合再被当成“当前实现方案”读取。

本轮判断基于两类证据：

- 文档头部状态、阶段描述、关联实现入口
- 当前仓库实际存在的脚本、Prefab、测试和工具入口

## 结论总览

### 建议标记为 `已偏离实现`

- `qianxia-crowd-vat-technical-design.md`
- `qianxia-crowd-runtime-tactical-squad-design.md`
- `qianxia-3d-walk-movement-design.md`

### 建议按“历史基线”处理

- `cola-bottle-pile-collision-prd.md`

### 当前仍可保留为“当前实现基线”

- `renderdoc-mcp-performance-sampling-tool-design.md`
- `renderdoc-mcp-d3d12-external-capture-workflow.md`
- `cola-bottle-pile-collision-technical-design.md`

### 当前更适合作为“候选方案 / 扩展设计”

- `qianxia-large-scale-crowd-physics-design.md`
- `qianxia-scene-query-technical-design.md`

## 逐篇判断

## 1. `qianxia-crowd-vat-technical-design.md`

- 建议状态：`已偏离实现`
- 建议同步级别：`归档`
- 判断原因：
  - 文档头部仍写“第四版已实现”，正文主目标仍停留在“二维空间查询 + active bubble + 基础 crowd PBD”。
  - 当前仓库已经存在 `CrowdVatSquadController.cs`、`CrowdVatSquadCommandController.cs`、`CrowdVatSceneQueryFieldAsset.cs`，说明 runtime squad 与 scene query 静态世界层已经继续往前演化。
  - 文档产物里仍引用 `CrowdVatIndirectActiveBubbleTests.cs`，但该测试文件当前已不存在。
- 处理建议：
  - 保留为 runtime squad / scene query 扩展之前的 crowd VAT 历史基线。
  - 后续若需要“当前 crowd 总设计”，应新建或重写一篇覆盖 squad、scene query、调试和当前测试面的新版文档。

## 2. `qianxia-crowd-runtime-tactical-squad-design.md`

- 建议状态：`已偏离实现`
- 建议同步级别：`弱同步`
- 判断原因：
  - 文档头部仍写“可开工实现方案”。
  - 当前仓库已经存在 `CrowdVatSquadController.cs`、`CrowdVatSquadCommandController.cs`，并且 `CrowdVatIndirect.compute` 中已有 `SquadStateData`、`AgentSquadData`、runtime squad 相关逻辑。
  - 也就是说，它已经不是“还没开工的方案”，而是“已有实现但文档还没追上”。
- 处理建议：
  - 短期先标成 `已偏离实现`，避免继续被误读为纯提案。
  - 后续按当前代码拆成“runtime squad 当前实现基线”和“后续战术扩展”两层文档会更清晰。

## 3. `qianxia-3d-walk-movement-design.md`

- 建议状态：`已偏离实现`
- 建议同步级别：`弱同步`
- 判断原因：
  - 文档头部仍写“方案稿”。
  - 正文里明确写过“未看到稳定落库的角色 prefab / 安装入口”，但当前仓库已存在 `Assets/Project/Characters/Qianxia/Generated/QianxiaThirdPerson.prefab` 与 `QianxiaCharacterSetupUtility.cs`。
  - 当前角色移动和镜头脚本也已明确落在 `QianxiaGenshinCharacterController.cs` 与 `QianxiaGenshinCameraController.cs`。
- 处理建议：
  - 这篇不适合归档，因为角色移动主路径仍在用。
  - 更合理的是把状态改成 `已偏离实现`，后续补一版“当前角色 prefab / 安装工具 / Animator 接线基线”。

## 4. `cola-bottle-pile-collision-prd.md`

- 建议状态：按“历史需求基线”处理
- 建议同步级别：`归档`
- 判断原因：
  - 文档头部仍写“待以占位模型推进技术样机”。
  - 当前仓库已经存在 `BottlePileSystem.cs`、`BottlePileSceneAutomation.cs`、`BottleSimulationConfig.cs`，技术样机显然已经不是“待推进”状态。
  - 这篇文档更适合保留为原始需求和约束背景，而不是继续被当成当前阶段说明。
- 处理建议：
  - 不建议删除。
  - 建议把头部状态显式改成“历史需求基线”或等价语义。

## 5. `qianxia-large-scale-crowd-physics-design.md`

- 建议状态：继续保留为“候选方案 / 扩展设计”
- 判断原因：
  - 文档头部本来就是“设计稿”。
  - 它的重点是 `Simulation LOD + XPBD + 流场` 的下一阶段路线，并没有伪装成当前实现。
  - 当前代码虽已继续扩展 crowd，但并没有完整落到这篇文档描述的总体目标上。
- 处理建议：
  - 不需要标成历史方案。
  - 建议后续只补 `同步级别：弱同步` 与 `最后实现核对` 即可。

## 6. `qianxia-scene-query-technical-design.md`

- 建议状态：继续保留为“候选方案 / 扩展设计”
- 判断原因：
  - 文档头部是“设计稿”，定位本来就偏扩展设计。
  - 当前代码已有 `CrowdVatSceneQueryFieldAsset`、环境距离场烘焙和 `ResolveSpatialQueries`，但文档讨论的统一 Scene Query 体系仍没有完全等价地被独立实现为最终形态。
- 处理建议：
  - 不需要直接标成历史方案。
  - 更适合保留为下一阶段 query 体系演化稿。

## 7. `renderdoc-mcp-performance-sampling-tool-design.md`

- 建议状态：保持当前实现基线
- 判断原因：
  - 代码中存在 `RenderDocCaptureController.cs`、`RenderDocCaptureArtifacts.cs`、`RenderDocCaptureWindow.cs`。
  - 文档里的 `.rdc + capture_context.md + gameview.png` 三件套和“不要额外 JSON sidecar”的约定与当前代码一致。

## 8. `renderdoc-mcp-d3d12-external-capture-workflow.md`

- 建议状态：保持当前实现基线
- 判断原因：
  - `RenderDocCaptureWindow.cs` 中存在 `Launch Under RenderDoc`、`Inject Into PID`、`--renderdoc-artifacts-root` 等当前工作流入口。
  - 文档本身是操作工作流说明，不是历史提案。

## 9. `cola-bottle-pile-collision-technical-design.md`

- 建议状态：保持当前实现基线
- 判断原因：
  - 文档头部已经明确写到当前主路径收敛到 `block-local sort + merge + directed cell-pair list + bottle-level solver dispatch + 单 pile RenderMeshIndirect`。
  - 代码中的 `BottlePileSystem.cs`、`BottlePilePbd.compute`、`BottleSimulationConfig.cs` 仍与这条主路径一致。

## 推荐下一步

1. 先把明显过时的 4 篇文档头部状态改掉，避免继续误导。
2. 再决定是否要为 crowd 和角色移动各补一篇新的“当前实现基线”。
3. 后续每次改主流程时，优先更新 `docs/PLANS.md` 的角色判断，再回写具体正文。
