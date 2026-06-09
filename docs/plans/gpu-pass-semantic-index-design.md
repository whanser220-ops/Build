# GPU Pass 语义索引与 Joined Context 设计

## 文档信息

- 版本：v0.3
- 日期：2026-05-26
- 状态：Crowd/VAT 专用隐藏链路；不再作为 Unity6 普通 RenderDoc 离线分析主路径
- 同步级别：强同步
- 适用范围：只接管 Crowd/VAT 等显式写入 `BeginSample` / `ProfilingScope` / `CommandBuffer` marker / 自定义 draw label 的“事件与本地代码语义对齐”职责；普通 MeshRenderer + Material 的正式离线分析走 rdoc-agent qrenderdoc CLI。
- 当前实现锚点：
  - `docs/gpu-pass-catalog/gpu_pass_catalog.yaml`
  - `docs/gpu-pass-catalog/shaders/CrowdVatIndirect.compute.ai.yaml`
  - `tools/renderdoc-cli/run_gpu_context.sh`
  - `tools/renderdoc-cli/renderdoc_gpu_context_cli.py`
  - `tools/renderdoc-cli/renderdoc_gpu_context_query.py`
  - `tools/renderdoc-cli/extract_gpu_passes_qrenderdoc.py`
  - `renderdoc-mcp/RenderDocMCP-main/scripts/build_gpu_context_package.py`
  - `Assets/Project/Tools/Performance/Shared/GpuPassMarkerScope.cs`

## 目标

不要让 AI 在 Crowd/VAT 的 RenderDoc 原始事件、运行时代码和 Shader 源码之间猜关系。每个带业务 marker 的 GPU event 必须尽早对齐到稳定 `pass_id`，`gpu_frame_context.json` 是已经 join 好的证据底座；默认不要求一次性把它完整展示给 AI，而是通过查询 CLI 按问题逐步取片。这套 Crowd 机制回答的是：

这套机制不适用于普通 MeshRenderer + Material 的默认绘制。普通绘制会自动进入 URP 默认渲染流程，通常没有可维护的业务 label；这类 `.rdc` 应通过 `tools/renderdoc-cli/rdoc_agent.py` 先读取 capture info，并随着后续 rdoc-agent 子命令补齐 draw list、pipeline state、shader、texture 和 buffer 证据。

- 这个 GPU event 的稳定 `pass_id` 是什么。
- 它来自哪个 marker、哪个 C# scope、哪个 shader/kernel。
- 它读写哪些资源，资源语义是什么。
- 它的设计意图、数据规模、理论瓶颈和已知风险是什么。
- 当前截帧里它的 event id、dispatch/draw 参数和 GPU timing 是什么。

## 分层

```text
本地代码 / Shader
  -> GPU Marker: [{pass_id}] Display Name
  -> Crowd-only qrenderdoc CLI 导出事件事实
  -> GPU Pass Catalog / Compute Shader AI 描述文件
  -> AI 友好的 Joined Context
```

关键原则：导出更多字段不是目标，稳定 ID 和可维护语义索引才是目标。

## 稳定 ID

`pass_id` 使用小写点分层级：

```text
crowd.workset.build_alive_list
crowd.sim.solve_crowd
crowd.scene_query.resolve
crowd.render.instance_culling
```

RenderDoc marker 建议格式：

```text
[crowd.sim.solve_crowd] Crowd / Sim / Solve Crowd
```

Crowd-only qrenderdoc 导出层保留原始 marker；Python 处理层会优先从 `[...]` 中提取 `pass_id`。旧 marker 可以保留在 catalog 的 `aliases` 中用于过渡，但不能反向决定新 ID。

## Catalog

总 catalog 位于：

```text
docs/gpu-pass-catalog/gpu_pass_catalog.yaml
```

Catalog 是 AI/人协作维护的结构化源文件，不是 Python 按固定规则生成的派生文件。AI 在阅读本地代码、Shader 和相关设计文档后填写 `purpose`、资源语义、dispatch 口径和性能风险；Python 只读取、校验、转写和 join。

它按 `pass_id` 维护：

- `display_name`
- `type`
- `shader` / `kernel`
- `purpose`
- `inputs` / `outputs`
- `dispatch`
- `performance_hints`
- `source`
- `aliases`

单个 compute shader 的长期说明位于：

```text
docs/gpu-pass-catalog/shaders/*.compute.ai.yaml
```

这些文件描述 kernel 职责、并行粒度、输入规模、访问模式、已知风险和优化备注。它们是 AI 分析前的语义基线，不承载某次截帧的结论。

## Crowd-only CLI Join

仅当分析目标明确是 Crowd/VAT 且抓帧里存在稳定 `Crowd.*` marker 证据时，才从 `renderdoc-cli` skill 进入这条隐藏链路：

```bash
bash tools/renderdoc-cli/run_gpu_context.sh --repo . --capture "<capture.rdc>" --marker-filter "Crowd."
```

新增行为：

- `extract_gpu_passes_qrenderdoc.py` 只导出 RenderDoc 原始事件事实，不把 `[pass_id] Display` marker 改写成 `pass_id`；在 `ExecuteIndirect(...)` 这类 RenderDoc 合成技术 marker 下会回退到最近的逻辑 marker 作为 pass 名。
- `renderdoc_gpu_context_cli.py` 会把 `--catalog` 透传给 context builder；不传时默认发现 `docs/gpu-pass-catalog/gpu_pass_catalog.yaml`。
- `build_gpu_context_package.py` 会输出规范化后的 `gpu_pass_catalog.json`；该文件只是 artifact 转写，不生成语义字段。
- `gpu_pass_events.json` 保留 qrenderdoc 直接导出的原始事件包，不做 catalog join，不改写字段语义。
- `gpu_pass_overview.json` 会按 `pass_id` 输出本帧 GPU pass 总览：`event_id`、`name`、`pass_type`、`event_type`、`duration_ms`、compute 的 `dispatch`，以及 draw / indirect draw 的 `draw`、`pipeline` 和资源摘要。
- `gpu_pass_resources.json` 会按 `event_id + pass_id` 输出资源明细：SRV / UAV / CBV、`resource_type`、`size_bytes`、`stride_bytes`、`element_count`、访问模式和来源；同时用 catalog 语义资源补足 producer / consumer 边。
- `gpu_frame_context.json` 的每个 `gpu_passes[]` 会包含 `pass_id`、`matched_pass_id`、`pass_type`、`display_name`、`resource_flow` 和 `catalog` 摘要。
- 如果 event 没有 catalog 匹配，会进入 `data_quality.warnings[]`，类型为 `missing_catalog_entry`。

## CLI Query Layer

`renderdoc_gpu_context_cli.py` 负责生成 Crowd 专用事实包；`renderdoc_gpu_context_query.py` 负责把事实包暴露成可组合的只读探针。查询层不重新导出 `.rdc`，不生成性能结论，也不改写 catalog 语义，只降低“从 `.rdc` marker、catalog 与 AI Debug 旁证反推 Crowd 状态”的成本。

普通 MeshRenderer + Material 分析不要先跑 `renderdoc_gpu_context_cli.py`，也不要强行构造 `pass_id`。

查询层按四层组织：

- 第零层认知地图：`gpu architecture map`。先按 domain 建立模块分工、协作依赖、关键资源 handoff 和架构审查信号。
- 第一层粗粒度状态视图：`gpu frame overview`、`gpu pass inspect --id ...`、`gpu pass explain --id ...`、`gpu resource explain --name ...`、`shader kernel explain --file ... --kernel ...`。
- 第二层中粒度追问工具：`gpu pass resources --id ...`、`gpu pass timeline --id ...`、`gpu pass dependencies --id ...`、`shader hotspots --kernel ...`、`shader memory-pattern --kernel ...`。
- 第三层细粒度原始证据工具：`renderdoc event show --id ...`、`renderdoc resource raw --id ...`、`code show --file ... --line ...`、`log grep "..."`。

第零层用于先建立系统认知地图；第一层用于第一眼理解局部真实状态；第二层用于沿问题追问；第三层只在已经有假设时验证原始证据。

兼容入口 `summary`、`passes`、`show`、`resources`、`trace`、`grep` 仍保留，但它们是低层查询/调试入口，不应替代分层语义命令。

示例：

```bash
python tools/renderdoc-cli/renderdoc_gpu_context_query.py \
  --artifact-dir ".workspace/artifacts/renderdoc-analysis/<run>" \
  gpu architecture map --limit 10
```

```bash
python tools/renderdoc-cli/renderdoc_gpu_context_query.py \
  --artifact-dir ".workspace/artifacts/renderdoc-analysis/<run>" \
  gpu pass explain --id 1708
```

## RenderDoc 导出层结构

新的分析链路不再生成或消费旧 `gpu_pass_bindings.json` 运行时 sidecar。RenderDoc 导出与 Python 处理明确分层：

- `gpu_pass_events.json`：qrenderdoc 直接导出的原始事件包，不做语义 join。
- `gpu_pass_overview.json`：Python 处理后的每个 GPU pass 总览，主键为 `event_id`，稳定关联键为 `pass_id`。
- `gpu_pass_resources.json`：Python 处理后的每个 GPU pass 资源明细，主键为 `event_id + pass_id`。
- `gpu_frame_context.json`：把 events、resources、shader facts 和 catalog 进一步 join 后给 AI 的主上下文。

`gpu_pass_overview.json` 的核心形态：

```json
{
  "events": [
    {
      "event_id": 1842,
      "name": "[crowd.sim.integrate_velocity] CrowdSim.compute::IntegrateVelocity",
      "pass_id": "crowd.sim.integrate_velocity",
      "matched_pass_id": "crowd.sim.integrate_velocity",
      "pass_type": "compute",
      "type": "dispatch",
      "event_type": "dispatch",
      "duration_ms": 0.43,
      "dispatch": {
        "groups": [3907, 1, 1],
        "threads_per_group": [256, 1, 1],
        "estimated_threads": 1000192
      }
    }
  ]
}
```

`gpu_pass_resources.json` 中单个资源保留 RenderDoc descriptor/resource 与 catalog 能确认的事实：

```json
{
  "type": "UAV",
  "resource_type": "RWStructuredBuffer<uint>",
  "size_bytes": 4096,
  "stride_bytes": 4,
  "element_count": 1024
}
```

## Joined Context 字段边界

`gpu_frame_context.json` 可以包含机械事实和人工维护语义：

- 可以写：`pass_id`、`event_id`、`gpu_time_ms`、`dispatch`、`shader.numthreads`、资源绑定、catalog 的 `purpose` 和 `performance_hints`。
- 可以写：由 dispatch 参数机械推导的 `total_invocations` 等 workload 指标。
- 不应写：本次性能结论、主观瓶颈判断、没有证据的优化建议。

换句话说，catalog 可以告诉 AI “理论上可能受哪些风险影响”；最终分析仍要结合当前帧 timing、资源规模和代码证据。

Python join 层不得自动补全 `purpose`、`meaning`、`performance_hints`、`display_name` 或 `source`。如果这些字段缺失，应保留缺失状态，让后续 AI 回到本地代码中补 catalog，而不是在分析产物里临时猜一个答案。

## 代码侧 Marker 约定

不要在业务代码里到处手写散乱字符串。后续 GPU dispatch 应收敛到统一 wrapper：

```csharp
using (new GpuPassMarkerScope(cmd, "crowd.sim.solve_crowd", "Crowd / Sim / Solve Crowd"))
{
    cmd.DispatchCompute(shader, kernel, groupsX, groupsY, groupsZ);
}
```

更理想的长期接口是 descriptor 式 dispatch：

```text
DispatchComputePass(pass_id, shader, kernel, groups, resources)
```

这个入口同时负责：

- 写 GPU debug marker。
- 执行 dispatch。
- 让离线工具依据 `.rdc` 资源绑定和 catalog 生成按需事实包。
- 校验 catalog 是否存在同名 `pass_id`。

本轮先落 catalog、CLI join 和最小 marker scope 工具，避免在现有业务代码里扩大改动面；后续再把 runtime dispatch 入口统一迁移。

## 维护规则

- 新增 GPU pass 必须先分配 `pass_id`。
- `pass_id` 一旦进入 catalog，不因重构、文件移动或 display name 改动而改变。
- 一个 pass 被拆分时，新 pass 新增 ID，旧 ID 标注替代关系或从代码 marker 中移除。
- 如果 `gpu_frame_context.json` 出现 `missing_catalog_entry`，优先补 catalog，而不是让 AI 继续猜。
- 如果 shader kernel 语义变化，必须同步更新总 catalog 和对应 `*.compute.ai.yaml`。
- 如果修改 `#pragma kernel`、kernel 读写资源、并行粒度、线程组、dispatch 公式或 C# GPU marker 注册，必须在同一轮任务同步维护 catalog。
- 提交前运行 `python tools/renderdoc-cli/validate_gpu_pass_catalog.py --repo .`；该脚本只做覆盖率和结构校验，不生成语义字段。
