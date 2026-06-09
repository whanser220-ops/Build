# RenderDoc RDC 事实库表结构设计

## 文档信息

- 版本：v0.1
- 日期：2026-06-04
- 状态：可开工
- 同步级别：弱同步
- 适用项目：Unity 6 `6000.0.46f1`
- 当前实现锚点：
  - `tools/renderdoc-db/rdc_capture_fact_import.py`
  - `tools/renderdoc-db/rdc_capture_fact_export_qrenderdoc.py`
  - `tools/renderdoc-db/rdc_capture_fact_schema.sqlite.sql`
  - `tools/renderdoc-cli/sql/rdc_capture_fact_schema.sql`
  - `tools/renderdoc-cli/sql/rdc_capture_fact_queries.sql`
- 最后实现核对：2026-06-04
- 替代关系：
  - 替代：无
  - 被替代：无

## 目标

为 RenderDoc `.rdc` 离线导入准备一套轻量关系表结构，把 capture、draw/event、Texture、Model 和 event-texture-model binding 拆成可查询的事实表。

当前分析入口已经切换为数据库查询路径；`tools/renderdoc-cli` 不再保留 `rdoc-agent session`、`capture`、`events`、`find`、`entity`、`batch` 等直接打开 `.rdc` 的旧工具。
这套 schema 是后续批量导入、跨 capture 检索、重复分析和报表统计的数据库底座。

实际导入入口已独立放在 `tools/renderdoc-db/`，不调用 `rdoc-agent` 或现有 Agent CLI。入口脚本负责数据库写入，qrenderdoc helper 只负责通过 RenderDoc Python API 读取 `.rdc`。

## 当前结论

当前只保留五张表：

- `capture`：一条记录对应一个 `.rdc` 文件。
- `draw_event`：RenderDoc 中的 marker、drawcall、dispatch、clear、copy、present 等事件，并记录单 event 的 `gpu_us`（GPU 微秒）。
- `texture`：RenderDoc 中带真实名称的 Texture 资源。
- `model`：RenderDoc pipeline 中可解析到真实名称的模型/mesh 资源。
- `event_resource_binding`：某个 event 绑定或使用了哪些 Texture，并在可解析时关联该 event 绘制的 model。

旧表 `capture_file`、`frame_event`、`gpu_resource`、`event_resource_usage`、`event_pipeline_state`、`event_gpu_counter`、`event_unity_object` 已从 schema 和导入写入逻辑中删除。

## 范围

### 本文覆盖

- PostgreSQL / SQLite 事实表关系模型。
- 常用查询入口。
- 后续 importer 需要遵守的导入顺序。

### 本文不覆盖

- 具体 `.rdc` 解析实现。
- `qrenderdoc.exe --python` helper 的调用方式。
- GPU counter 历史数据表。
- pipeline state 独立表。
- Unity 侧 Prefab 候选表；Prefab 归因优先依赖 `marker_path` 或后续单独扩展。

## 建议导入顺序

1. 写入 `capture`。
2. 递归导入 RenderDoc action tree 到 `draw_event`，同时计算 `marker_path`。
3. 只导入资源类型为 Texture 且存在真实名称的资源到 `texture`；如果名称为空、等于资源 id，或类似 `ResourceId::346`，则跳过。
4. 通过 RenderDoc `GetUsage(resourceId)` 离线索引读取 Texture 和模型 buffer 的 event usage，不逐 event 调用 `SetFrameEvent` / `GetPipelineState`。
5. 将可见模型/mesh buffer 写入 `model`；底层 scratch/internal buffer 不写入模型表。
6. 将同一 event 上的 Texture usage 和 Model usage 合并写入 `event_resource_binding`。绑定表通过 `texture_pk` 关联 Texture，通过可空的 `model_pk` 关联 Model。

## 事实边界

`texture.texture_name` 不作为主键，也不假设跨 capture 稳定。资源关联以 `rdc_texture_id` 在单个 capture 内连接。当前导入器不保留匿名资源名，避免把 `ResourceId::346` 这类 fallback 当作可读资源名。

`model.model_name` 来自 RenderDoc resource usage 可见的 mesh/buffer 名称。没有真实 mesh 名时，`event_resource_binding.model_pk` 允许为空，不硬造模型关系。

当前导入器只保留快速 usage-index 模式。`event_resource_binding` 不记录 `bind_stage`、`bind_type`、`slot_index`、`usage_role`；需要精确 SRV/UAV stage/slot 时应另建慢速诊断工具，不回写当前事实库。

Unity 侧 Prefab / Renderer 归因先依赖 `draw_event.marker_path`。如果 Unity marker 能写成 `Scene/BossRoom/EnemyPrefab_01/Renderer_Body` 这类路径，后续归因会比从资源名猜测可靠得多。

## 常用查询

查某个 event 使用的 Texture 和模型：

```sql
SELECT d.event_id, d.event_name, m.model_name,
       t.rdc_texture_id, t.texture_name
FROM event_resource_binding AS b
JOIN draw_event AS d ON d.event_pk = b.event_pk
JOIN texture AS t ON t.texture_pk = b.texture_pk
LEFT JOIN model AS m ON m.model_pk = b.model_pk
WHERE d.capture_id = 1
  AND d.event_id = 3298
ORDER BY t.texture_name, m.model_name;
```

查某个模型使用的 Texture：

```sql
SELECT m.model_name, t.texture_name,
       COUNT(*) AS event_binding_count
FROM event_resource_binding AS b
JOIN model AS m ON m.model_pk = b.model_pk
JOIN texture AS t ON t.texture_pk = b.texture_pk
WHERE m.capture_id = 1
  AND m.model_name = 'SM_Flower_03_02_LOD0'
GROUP BY m.model_name, t.texture_name
ORDER BY event_binding_count DESC;
```

查某个 marker path 下的 draw：

```sql
SELECT event_id, marker_path, drawcall_type, index_count, instance_count, render_target, depth_target, gpu_us
FROM draw_event
WHERE capture_id = 1
  AND marker_path LIKE 'FrameTime.GPU/%'
ORDER BY event_id;
```

更多示例见 `tools/renderdoc-cli/sql/rdc_capture_fact_queries.sql`。

## CLI 查询入口

导入后的分析入口只走数据库查询，不再通过 `rdoc-agent session` 打开 `.rdc` 逐 event 读取 pipeline。

当前第一个查询工具：

```powershell
.\tools\renderdoc-cli\rdoc-agent.cmd db instance-cost `
  --sqlite ".workspace\artifacts\renderdoc-analysis\rdc-fact-database\rdc_capture_four_table.sqlite" `
  --capture-id 1 `
  --limit 20 `
  --json --pretty
```

`db instance-cost` 输出按实例/模型聚合的 GPU 微秒消耗，核心字段为 `totalGpuUs`、`avgGpuUs`、`maxGpuUs`、`eventCount`、`textureCount` 和 `topEvents`。默认 `--group-by model` 会按 `model_name + event_pk` 去重后求和，避免 `event_resource_binding` 中多个 Texture 或多个 RenderDoc buffer resource 把同一个 draw 的 `gpu_us` 重复累计。需要按 render marker 进一步拆分时使用 `--group-by model-marker`。

## 后续实现备注

当前 SQLite 导入命令：

```powershell
python tools\renderdoc-db\rdc_capture_fact_import.py `
  --capture "<capture.rdc>" `
  --sqlite ".workspace\artifacts\renderdoc-analysis\rdc-fact-database\rdc_capture_four_table.sqlite" `
  --project-name Unity6 `
  --scene-name scene_meadowenvironment_01_summer `
  --platform Windows
```

导入命令会写 `.workspace/artifacts/renderdoc-analysis/rdc-fact-import/<timestamp>/import_manifest.json`，记录 capture hash、导入表行数、qrenderdoc 路径、导入参数和 schema 结果。长原始 JSON 仍落 artifact 文件，不直接塞入对话上下文。

PostgreSQL 正式表结构已在 `tools/renderdoc-cli/sql/rdc_capture_fact_schema.sql` 中；当前新工具先实现 SQLite writer，用于本地验证真实 `.rdc` 数据导入。后续如接 PostgreSQL，应在同一工具中新增 PostgreSQL writer，而不是改回 `rdoc-agent` 命令。

## 风险与边界

- `event_resource_binding` 可以非常大，导入器应支持按 event 范围分批导入；当前写入范围仅限 Texture usage，且 `model_pk` 仅在捕获暴露真实模型名时非空。
- RenderDoc resource id 是否稳定只在单个 capture 内成立，跨 capture 查询必须通过名称、hash、尺寸、格式、shader hash 或 Unity 侧旁证另行聚合。
- `marker_path` 的质量决定 Prefab / Renderer 归因质量。没有 Unity GPU marker 时，不要从 resource name 硬猜 Prefab。

## 文档同步备注

如果新增或修改 `.rdc` 数据库 importer、schema 或 binding 角色推断逻辑，必须同步本文、`tools/renderdoc-db/rdc_capture_fact_import.py`、`tools/renderdoc-db/rdc_capture_fact_export_qrenderdoc.py` 与 `tools/renderdoc-cli/sql/rdc_capture_fact_schema.sql`。
 
## 2026-06-05 补充：截帧后数据库后处理入口

截帧完成后，推荐使用 `tools/renderdoc-db/rdc_capture_postprocess.py` 作为统一后处理入口，而不是让 Agent 手动寻找 `.rdc`、单独运行 fact importer、再单独导入 Prefab 表。

该脚本会执行两步：

1. 调用 `tools/renderdoc-db/rdc_capture_fact_import.py`，把目标 `.rdc` 导入 SQLite fact DB。
2. 调用 `tools/renderdoc-cli/rdoc_agent.py db import-prefabs`，把同目录的 `unity_prefab_signatures.sqlite` 导入刚生成或更新的 `capture_id`。

默认命令：

```powershell
python tools\renderdoc-db\rdc_capture_postprocess.py `
  --capture "<capture-folder>\capture_frame2051.rdc" `
  --sqlite ".workspace\artifacts\renderdoc-analysis\rdc-fact-database\rdc_capture_four_table.sqlite" `
  --prefab-signatures "<capture-folder>\unity_prefab_signatures.sqlite" `
  --json --pretty
```

如果省略 `--capture`，脚本会从 `.workspace/artifacts/renderdoc-captures` 下递归选择最新 `.rdc`。输出会写入 `.workspace/artifacts/renderdoc-analysis/rdc-fact-postprocess/<capture>_<timestamp>/postprocess_manifest.json`，其中记录 fact import manifest、Prefab import 统计、最终 `captureId` 和可复现命令。

Unity Editor 的 `Tools/Qianxia/RenderDoc Capture/Open Window` 也新增 `Export Latest Fact DB + Prefabs` 按钮。该按钮会先刷新最新 capture 目录里的 `unity_prefab_signatures.sqlite`，再后台启动同一个 postprocess 脚本，并把结果 JSON 写到 capture 目录的 `rdc_fact_postprocess_result.json`。后续 Agent 可直接用 `rdoc-agent.cmd db instance-cost --prefab-name <PrefabName>` 查询。
