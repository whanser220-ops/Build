# Codex 上下文管理约定

本文档描述在本仓库里如何控制 agent 上下文体积，尤其是长日志、RenderDoc 抓帧和多 agent 协作场景。

## 目标

上下文管理的目标不是“把所有信息都写下来”，而是把信息分层，让主对话始终只背最有用的那一小部分。

## 四层上下文

### 1. 当前会话层

只保留：

- 当前问题
- 已确认结论
- 关键证据路径
- 下一步动作

这层内容必须短，便于主 agent 持续推理。

### 2. 索引层

用于告诉 agent 去哪里找，而不是把内容全部抄进来：

- `AGENTS.md`
- `ARCHITECTURE.md`
- `docs/README.md`
- `docs/PLANS.md`

### 3. 规则层

用于提供长期有效的工作方法：

- `docs/DESIGN.md`
- `docs/codex-context-engineering.md`
- `docs/agents/README.md`

### 4. 证据层

只在需要时回查：

- 源码、Shader、场景、测试
- `Logs/` 中的构建与测试日志
- `.workspace/artifacts/` 中的分析产物
- RenderDoc `.rdc` 与按需导出的局部数据

## 读取原则

- 先搜索，再打开局部。
- 先读索引，再读正文。
- 先看摘要，再回查原文。
- 除非明确需要，不要一次性读取整份长日志、整帧 RenderDoc 数据或整个文档目录。

## 写回原则

### `.workspace/memory/`

建议使用以下文件做短期工作记忆：

- `task_state.md`
  - 当前目标、进度、下一步。
- `decisions.md`
  - 已确认技术决策、理由与边界。
- `open_questions.md`
  - 还需确认的问题和阻塞点。

### `.workspace/artifacts/`

适合放：

- 长日志
- 网页正文摘录
- 大型 JSON / CSV
- 单次分析报告
- RenderDoc 热点索引与事件摘要

不要把这些内容原样塞回主对话。

## 子 agent 输出契约

子 agent 的主要作用是压缩和索引，不是扩张主对话。

建议子 agent 输出只包含：

- 结论摘要
- 关键证据路径
- 关键 id / event id / marker 名
- 推荐下一步
- 尚未解决的问题

不建议默认输出：

- 全量长日志
- 完整网页正文
- 全量 RenderDoc draw call 清单
- 全量 shader 反汇编

## RenderDoc CLI 专项规则

RenderDoc 场景最容易发生上下文过载，原因是单帧里可读信息太多，而且 CLI 抽取结果如果不分层落盘，也会迅速变成一大坨难以检索的 JSON。

因此默认采用“rdoc-agent 小步取证 + 按需展开”的分析法：

1. 普通 `.rdc` 先通过 `python tools/renderdoc-cli/rdoc_agent.py capture info --capture "<capture.rdc>" --json` 获取捕获基本信息。
2. 后续先通过 `rdoc-agent events overview` 建立顶层 marker / draw / dispatch / timing 摘要，避免被几千条 event 淹没。
3. 对可疑顶层块使用 `rdoc-agent events expand --event <eventId> --depth 1` 像展开文件夹一样逐层探索；默认只返回直接 children 和每个 child 的聚合统计。
4. 已知关键词时用 `rdoc-agent find --q <text> --scope event:<eventId>.branches`：显式指定 root event，跨其直接 child 分支搜索后代 draw/dispatch leaf 的名称字段；资源名优先走 RenderDoc resource usage 索引，必要时才回退到轻量 pipeline 名称扫描，只返回 `searchId`、命中分支摘要和少量 top leaf hits。
5. 对候选结果运行 `rdoc-agent entity list --from-search <searchId>`：这里“实体”指 capture-visible draw signature 聚合；如果 capture 同目录存在 `unity_prefab_signatures.sqlite`，会同时输出每个实体的 Prefab 候选以及 `prefabTimingSummary`，其中包含 Prefab 总 GPU 耗时与按 LOD 拆分的耗时。
6. 需要单独查看 Prefab 匹配细节时，仍可运行 `rdoc-agent entity match-prefabs --from-search <searchId> --prefab-signatures <unity_prefab_signatures.sqlite>`；该动作读取 SQLite，输出每个实体的 Prefab 候选、score、confidence 和 renderer 行证据。
7. LLM 根据 `entities[]` 的聚合耗时选择深入对象，并记录目标实体的 `representativeEventId` 作为后续回查锚点。当前 `rdoc-agent` 不再提供 `events inspect`；要拆分某个 branch 时继续用 `events expand --event <branchEventId>`，并继续 `find --scope event:<branchEventId>.branches`。
8. 最后按结论需要回查原始证据；不能用摘要或热点列表提前丢弃事件。
9. 只有明确分析 Crowd/VAT 且抓帧里存在稳定 `Crowd.*` marker 证据时，才使用 `renderdoc_gpu_context_cli.py` / `renderdoc_gpu_context_query.py` 生成和查询 GPU fact package，并按需配对 AI Debug runtime 旁证。

建议产物格式：

- `frame_summary.json`
- `draw_calls.json`
- `action_timings.json`
- `event_<id>.md`
- `pipeline_state_<id>.json`
- Crowd/VAT 专用可选：`event_index.csv`、`gpu_frame_context.json`、`gpu_pass_events.json`、`gpu_pass_overview.json`、`gpu_pass_resources.json`、`gpu_pass_catalog.json`

其中：

- `frame_summary.json` 和 `draw_calls.json` 是普通离线分析的轻量起点。
- `event_<id>.md` 才展开单个事件的细节。
- `pipeline_state_<id>.json` 只对少量目标 event 落盘。
- Crowd/VAT 专用 `gpu_frame_context.json` / `gpu_pass_overview.json` / `gpu_pass_resources.json` 依赖显式 marker、custom draw label 与按需取证，不是普通 MeshRenderer + Material 的默认输入。
- 当前抓帧目录不再落盘 `gpu_pass_debug_map.json` 或 `gpu_pass_bindings.json`；Crowd/VAT 语义分析以 `.rdc` 中的 marker、GPU Pass Catalog 与配对后的 AI Debug runtime 旁证为准。

默认禁止的做法：

- 把整帧 draw call、完整 pipeline state、shader、buffer、texture 数据一次性全量落盘。
- 再把这些全量结果重新读回主 agent。

这类做法只会把“对话过载”变成“磁盘与检索过载”。
### Unity Prefab metadata sidecar

普通 MeshRenderer + Material 抓帧不能假设 RenderDoc 会保留 Unity 资产身份。当前 `entity list` 先聚合 RenderDoc 抓帧内可见的 draw signature；缺失 `unity_prefab_metadata.json` 不影响 `entity list`。如果存在 `unity_prefab_signatures.sqlite`，它会把实体匹配到当前加载场景中的 Prefab 实例候选，并输出 Prefab/LOD GPU timing。仍然不要用文件名、GUID 或源码搜索把 capture-visible mesh / shader / texture 手工断言为 Unity 资产。

`unity_material_metadata.json` 仍可作为独立材质清单导出。RenderDoc Capture 窗口的 ANGRY MESH metadata 导出会同时写出 material、prefab sidecar 与 `unity_prefab_signatures.sqlite`。其中 prefab sidecar 是资产清单，Prefab SQLite 是当前场景实例签名索引。Prefab SQLite 由 `entity list` 用来生成 `prefabTimingSummary`，也可由 `entity match-prefabs` 单独查询候选细节；这些候选是场景实例侧旁证，不替代 `.rdc` 中的 event / pipeline / resource 事实。

当前数据库查询主路径应先运行 `tools/renderdoc-cli/rdoc-agent.cmd db import-prefabs --prefab-signatures <capture-folder>/unity_prefab_signatures.sqlite`，把 Prefab SQLite 导入 RenderDoc fact DB 的 `prefab`、`prefab_model`、`prefab_texture` 表。之后用户输入 Prefab 名称时，用 `db instance-cost --prefab-name <PrefabName>`，让 Agent 先查 `prefab_model` 得到模型名 / `model_pk`，再统计对应 `draw_event.gpu_us`；不要把 `--q` 模糊匹配当作 prefab 查询主路径。
 
### RenderDoc fact DB 后处理入口

截帧完成后，如果需要按 Prefab 名称查询 GPU 耗时，优先运行：

```powershell
python tools\renderdoc-db\rdc_capture_postprocess.py `
  --capture "<capture-folder>\capture_frame2051.rdc" `
  --sqlite ".workspace\artifacts\renderdoc-analysis\rdc-fact-database\rdc_capture_four_table.sqlite" `
  --prefab-signatures "<capture-folder>\unity_prefab_signatures.sqlite" `
  --json --pretty
```

该命令会先把 `.rdc` 导入 fact DB，再把 `unity_prefab_signatures.sqlite` 导入同一个 `capture_id` 的 `prefab` / `prefab_model` / `prefab_texture` 表。若省略 `--capture`，脚本默认选择 `.workspace/artifacts/renderdoc-captures` 下最新 `.rdc`。之后查询 Prefab 耗时直接用 `rdoc-agent.cmd db instance-cost --prefab-name <PrefabName>`。
