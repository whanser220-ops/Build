# GPU Pass Catalog

本目录维护显式 GPU Pass 的长期语义索引。它的职责不是替代 RenderDoc 原始证据，而是在 Crowd/VAT 等主动写入 marker / custom draw label / sidecar 的截帧分析前，把“事件、代码、Shader、资源语义、设计意图”绑定到稳定 `pass_id`。普通 MeshRenderer + Material 的默认渲染事件不强行进入本目录，正式离线分析走 rdoc-agent qrenderdoc CLI。

这个目录的源文件由 AI/人协作填写，不由 Python 按固定规则生成。Python 工具只负责读取、校验、规范化转写和 join；`purpose`、`inputs.meaning`、`outputs.meaning`、`performance_hints`、`known_risks` 这类语义字段必须来自 AI 对本地代码、Shader 和设计文档的理解。

## 命名规则

- `pass_id` 使用小写点分层级，例如 `crowd.sim.solve_crowd`。
- RenderDoc marker 建议写成 `[{pass_id}] {display_name}`，例如 `[crowd.sim.solve_crowd] Crowd / Sim / Solve Crowd`。
- 旧 marker 或临时名字只允许放进 `aliases`，不能反向决定新的 `pass_id`。
- 一个 GPU dispatch / draw 只能有一个主 `pass_id`；如果一个 C# 函数连续发多个 dispatch，应拆成多个 pass。

## 文件分工

- `gpu_pass_catalog.yaml` 是跨系统总表，按 `pass_id` 组织，用于 CLI join。
- `shaders/*.compute.ai.yaml` 是单个 compute shader 的长期维护说明，用于解释 kernel 职责、数据规模、访问模式和风险。
- `AI_AUTHORING.md` 约束 AI 如何填充结构化字段。
- Crowd/VAT 专用 CLI 运行后会在分析目录额外写出规范化后的 `gpu_pass_catalog.json`，并把匹配结果 join 到 `gpu_pass_overview.json`、`gpu_pass_resources.json` 和 `gpu_frame_context.json`。这个 JSON 是转写产物，不是语义生成源。
- `type` 固定为 `compute`、`raster_draw`、`indirect_draw` 三类；各类只填写自己的执行字段，例如 compute 使用 `dispatch`，indirect draw 使用 `indirect_draw`。

## 当前覆盖

- `gpu_pass_catalog.yaml` 当前覆盖已注册的 43 个 GPU pass：38 个 Crowd compute pass、2 个 Crowd indirect draw pass 与 3 个 Grass indirect draw pass。
- `shaders/CrowdVatIndirect.compute.ai.yaml` 当前覆盖 `CrowdVatIndirect.compute` 的 38 个 `#pragma kernel`。
- 其他 `.compute` 文件尚未进入本目录；只有当它们接入稳定 GPU marker / pass_id 后再补对应 catalog 记录。

## 使用约定

1. 新增 GPU pass 时，先分配稳定 `pass_id`。
2. 在代码侧 BeginSample / CommandBuffer marker 使用 `[{pass_id}] ...` 格式。
3. 在 `gpu_pass_catalog.yaml` 中补齐 `shader`、`kernel`、`purpose`、资源语义和性能提示。
4. 如果一个 compute shader 有多个 kernel，在对应 `*.compute.ai.yaml` 中同步增加 kernel 条目。
5. 如果 Crowd/VAT 专用 `gpu_frame_context.json` 里出现事件但 catalog 没有匹配，会写入 `missing_catalog_entry`，这应被当作索引债务处理。

## 强同步规则

这些文件是长期维护文档，和 ComputeShader / GPU Pass 注册点强同步：

- 修改 `CrowdVatIndirect.compute` 的 `#pragma kernel` 时，必须同步更新 `shaders/CrowdVatIndirect.compute.ai.yaml`。
- 修改任意 kernel 的职责、读写资源、并行粒度、线程组、访问模式或性能风险时，必须同步更新对应 `*.compute.ai.yaml` 条目。
- 新增、删除、重命名或拆分 GPU pass 时，必须同步更新 `gpu_pass_catalog.yaml`，并保留或调整旧 marker 的 `aliases`。
- 修改 C# dispatch/marker 注册关系时，必须确认 `gpu_pass_catalog.yaml` 仍覆盖所有注册 marker。

提交前运行：

```powershell
python tools/renderdoc-cli/validate_gpu_pass_catalog.py --repo .
```

该脚本只做校验，不生成语义内容。
