# Unity Profiler 读取与 Agent Tool 设计

## 文档信息

- 版本：v0.4
- 日期：2026-05-22
- 状态：实施中
- 同步级别：弱同步
- 适用项目：Unity 6 `6000.0.46f1`
- 当前实现锚点：
  - `Assets/Project/Tools/Performance/Editor/ProfilerAnalysisCli.cs`
  - `tools/profiler-cli/profiler_agent_tools.py`
  - `Assets/Project/Tools/Performance/Runtime/PerformancePoiSampler.cs`
  - `Assets/Tests/PlayMode/QianxiaCrowdVatIndirectPlayModeTests.cs`
- 最后实现核对：2026-05-22

## 目标

本文关注的不是“导出一个大文件”，而是给性能分析 Agent 提供一组稳定、可组合、分层的动作：

1. `open_profiler_session`
2. `summarize_session`
3. `get_thread_hotspots`
4. `get_marker_context`
5. `get_gc_allocs`

原则是：

- 先摘要，再下钻
- 先结构化，再解释
- 默认小结果，不默认全量 dump
- Profiler 负责 CPU / 线程 / GC 证据
- RenderDoc 负责 GPU pass / resource / pipeline 深挖

## 当前实现

### POI `.raw` 来源

2026-05-22 起，`Tools/Qianxia/Performance/POI Sampler` 支持为每个 POI 单独勾选 `Export .raw`。Development Build 实际采样该 POI 时会临时启用 Unity binary profiler log，并把结果写入本次 POI run 的 `profiler-raw/` 子目录。

POI summary 会记录：

- `profiler_raw_requested`
- `profiler_raw_exported`
- `profiler_raw_path`
- `profiler_raw_size_bytes`
- `profiler_ai_package_generated`
- `profiler_ai_package_directory`
- `profiler_ai_package_manifest_path`
- `profiler_capture_summary_path`
- `profiler_session_id`
- `profiler_session_artifact_directory`

这让后续 Agent 可以先从 `poi_run_summary.json` 或 `poi_metrics.csv` 找到异常 POI，再把对应 `.raw` 传给 `open_profiler_session` 做线程、marker 和 GC 下钻。

2026-05-24 更新：当 `Tools/Qianxia/Performance/POI Sampler` 从编辑器启动外部 Development Player 完成一次 run 后，编辑器会自动扫描本次 run 的 `profiler-raw/*.raw`，复用 `ProfilerAnalysisCli` 导入成 `.workspace/artifacts/profiler-analysis/<session>/`，再把每个 POI 物化为 run 内统一目录 `profiler-ai/<order>_<poi>/`。该目录至少包含：

- `ai_package_manifest.json`
- `capture_summary.json`
- `session_manifest.json`
- `frame_index.csv`
- `thread_index.csv`
- `thread_overview.json`

### Unity Editor 批处理层

`ProfilerAnalysisCli` 负责两类事情：

1. 打开 `.raw` / `.data` 会话并构建基础 artifact
2. 对指定帧、指定线程按需导出深层查询结果

当前基础 artifact：

- `session_manifest.json`
- `frame_index.csv`
- `thread_index.csv`
- `thread_overview.json`

当前按需查询缓存：

- `queries/thread_hotspots_*.json`
- `queries/marker_context_*.json`
- `queries/gc_allocs_*.json`

`open_profiler_session` 的 stdout 只返回轻量 session 索引和 compact `thread_summary`。完整线程表仍写入 `thread_index.csv` / `thread_overview.json`，避免 Agent 初始上下文被大量低信号后台线程占满。

### Python Tool 层

`tools/profiler-cli/profiler_agent_tools.py` 将这些动作暴露成 Agent 友好的命令。

当前命令：

- `open_profiler_session`
- `summarize_session`
- `get_thread_hotspots`
- `get_marker_context`
- `get_gc_allocs`

## 动作语义

### `open_profiler_session`

职责：

- 打开一次 Profiler 会话
- 返回 session id、帧范围、可用模块、线程概况
- 生成后续查询需要的基础 artifact

### `summarize_session`

> Current implementation note (2026-05-21): returns session-level metrics plus `slowest_frames` only. It no longer accepts `--frame-index` or emits per-frame thread summaries.

职责：

- 读取 session 级摘要
- 输出最慢帧候选、P95 / P99、Main / Render / GPU / GC 概况
- 给出粗粒度可疑模式

### Removed `get_frame_summary`

> Current implementation note (2026-05-21): this command has been removed from the CLI. Use `summarize_session` to find slow frames, then go directly to `get_thread_hotspots`, `get_marker_context`, or `get_gc_allocs` for the chosen frame.

职责：

- 返回单帧 CPU / GPU / Main / Render / DrawCalls / Batches / GC 摘要
- 同时返回该帧线程列表

### `get_thread_hotspots`

> Current implementation note (2026-05-21): hotspot rows are ranked by `selfMs` descending, then `totalMs` descending.

职责：

- 导出某帧某线程的 hierarchy 热点
- 当前默认支持 `merged` 和 `default/raw` 两种视图语义

### `get_marker_context`

职责：

- 围绕某个 marker 返回：
  - 匹配项
  - 父链
  - 主要子项
  - 邻近 raw sample

### `get_gc_allocs`

职责：

- 导出某帧某线程里 `GC.Alloc` 的主要样本
- 当前会返回分配大小、起始时间、持续时间和原始调用栈指针

## 当前边界

- 当前主路径依赖 Unity Editor Profiling API，不是 runtime 内直接解析二进制。
- `open_profiler_session` 已支持从 `.raw` / `.data` 导入，也支持复用已有 session artifact。
- `run_id` 当前先解析为已有 profiler session id，不直接映射 POI run summary；POI 到 `.raw` 的桥接先通过 `poi_run_summary.json` / `poi_metrics.csv` 中的 `profiler_raw_path` 完成。
- `get_gc_allocs` 当前返回的是原始调用栈地址，不做符号解析。
- 深层查询采用“按需缓存”而不是 open 阶段全量导出，以控制体积和 token 压力。
- 同一个 Unity project 的深层查询需要串行执行；并行跑 `get_thread_hotspots`、`get_marker_context`、`get_gc_allocs` 可能触发 Unity project-open lock。
- Python Tool 层会把运行时错误包装成结构化 JSON：`success:false`、`error_type`、`error`。当 Unity batchmode 因 project-open lock 无法写出 response file 时，错误会明确提示关闭占用该 project 的 Editor 或改用未打开的 project copy。
- query cache 文件名会做短 hash，避免深层 `.workspace` 路径在 Windows 上超过路径长度限制。

## 下一步

建议后续按这个顺序继续：

1. 补一个最小自测样本和使用说明
2. 增加 `compare_frames`
3. 增加 `compare_sessions`
4. 把 profiler session 与 `POI` / `RenderDoc` artifact 自动关联起来
