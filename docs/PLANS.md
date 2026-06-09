# 当前方案与执行稿索引

本文档不只是列目录，还负责告诉 agent 哪些文档更接近当前实现，哪些只是候选方案或历史参考。

在 `docs/plans/` 中，优先关注两件事：

1. 文档角色：它是“当前实现基线”、还是“候选设计”。
2. 同步要求：它是否需要随代码保持强同步。

详细规则见 `docs/plans/README.md`。

## 当前实现基线

- `plans/qianxia-crowd-current-implementation-baseline.md`
  - 角色：当前 crowd 运行时总基线
  - 同步要求：强同步
- `plans/gpu-pass-semantic-index-design.md`
  - 角色：当前 GPU Pass 语义索引与 Joined Context 主设计
  - 同步要求：强同步
- `plans/renderdoc-mcp-performance-sampling-tool-design.md`
  - 角色：当前 RenderDoc CLI 采样工具总设计；GPU 事件与本地代码语义对齐部分已由 `plans/gpu-pass-semantic-index-design.md` 接管
  - 同步要求：强同步
- `plans/renderdoc-mcp-d3d12-external-capture-workflow.md`
  - 角色：当前 D3D12 外部抓帧与 CLI 分析主工作流
  - 同步要求：强同步
- `plans/cola-bottle-pile-collision-technical-design.md`
  - 角色：当前可乐瓶样机技术主路径
  - 同步要求：强同步

## 候选方案与扩展设计

- `plans/project-resource-bundle-split-plan.md`
  - 角色：ANGRY MESH 资源 AssetBundle 分包候选执行方案
  - 同步要求：弱同步
- `plans/renderdoc-rdc-fact-database-design.md`
  - 角色：RenderDoc `.rdc` 批量导入与跨 capture 查询的 PostgreSQL 事实库设计
  - 同步要求：弱同步
- `plans/unity-profiler-reader-tool-design.md`
  - 角色：Unity Profiler 读取、结构化查询与 Agent tool 设计
  - 同步要求：弱同步
- `plans/qianxia-large-scale-crowd-physics-design.md`
  - 角色：大规模 crowd 物理扩展设计
  - 同步要求：弱同步
- `plans/qianxia-scene-query-technical-design.md`
  - 角色：Scene Query 统一架构设计
  - 同步要求：弱同步

## 待清理与历史基线

- `plans/qianxia-crowd-vat-technical-design.md`
  - 角色：crowd VAT 早期已实现基线
  - 当前判断：已偏离实现
  - 原因：当前代码已经叠加 runtime squad、scene query field、更多调试与测试能力；当前总基线已转到 `plans/qianxia-crowd-current-implementation-baseline.md`，本文保留为更早阶段的历史基线。
- `plans/qianxia-crowd-runtime-tactical-squad-design.md`
  - 角色：runtime squad 方案稿
  - 当前判断：已偏离实现
  - 原因：仓库里已经有 `CrowdVatSquadController` 与 `CrowdVatSquadCommandController` 等基础实现；当前实现说明已并入 `plans/qianxia-crowd-current-implementation-baseline.md`，本文保留为较早的扩展方案稿。
- `plans/qianxia-3d-walk-movement-design.md`
  - 角色：角色移动方案稿
  - 当前判断：已偏离实现
  - 原因：当前仓库已存在 `QianxiaThirdPerson.prefab` 与 `QianxiaCharacterSetupUtility`，正文仍保留“未看到稳定落库 prefab / 安装入口”的阶段性描述。
- `plans/cola-bottle-pile-collision-prd.md`
  - 角色：原始需求基线
  - 当前判断：应按历史需求基线看待
  - 原因：技术样机和场景接入已经落地，文档头部仍写“待以占位模型推进技术样机”。

## 读取建议

- 先根据任务主题选择一个最相关入口，不要同时打开全部文档。
- 如果任务是改当前系统，优先读“当前实现基线”。
- 如果任务是做下一阶段规划、对比路线或扩展设计，再读“候选方案与扩展设计”。
- 如果实际代码已经和文档不一致，先更新 `docs/PLANS.md` 中的角色判断，再回写具体方案正文。
- `待清理与历史基线` 中的文档默认不能直接当成当前实现说明使用，除非本轮任务同时把它们更新回现行状态。
