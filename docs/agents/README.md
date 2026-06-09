# 多 agent 协作入口

本文档约定本仓库里主 agent 与子 agent 的基本分工，重点用于检索、设计整理和 RenderDoc 分析。

## 何时适合用子 agent

- 需要并行检索多个相互独立的问题。
- 需要先生成索引、摘要或对比表，再由主 agent 汇总。
- 需要把长材料压缩成短结论。

如果当前下一步强依赖某个结果，且问题本身很小，优先由主 agent 直接处理。

## 分工原则

### 主 agent

负责：

- 明确目标与边界
- 拆分任务
- 决定需要哪些证据
- 汇总子 agent 结果
- 做最终判断、改代码或写正式文档

### 子 agent

负责：

- 局部检索
- 单主题摘要
- 构建索引
- 列出候选风险或热点

默认不负责：

- 把大量原始材料全量转抄到仓库
- 在没有边界的情况下无限展开分析

## 推荐输入格式

给子 agent 的任务描述尽量包含：

- 要解决的具体问题
- 允许读取的范围
- 期望输出格式
- 不需要做的事

例如：

> 只分析 `RenderDoc` 抓帧里 GPU 时间最高的前 10 个 event，输出 `event_id`、marker、耗时和初步怀疑点，不要导出全量 draw call 列表。

## 推荐输出格式

子 agent 返回结果时，优先包含：

- 一段结论摘要
- 证据路径
- 关键编号或关键对象名
- 推荐下一步

如果需要落地到文件，优先写入 `.workspace/artifacts/`，由主 agent 只读取摘要和索引。

## RenderDoc 协作建议

RenderDoc 场景建议分成两个角色：

1. 子 agent
   - 通过 rdoc-agent qrenderdoc CLI 读取 frame summary、draw/dispatch list、GPU timing、draw details 和 pipeline/resource 事实。
   - 普通 MeshRenderer + Material 分析只产出轻量索引和少量目标 event 详情；只有 Crowd/VAT marker + sidecar 任务才产出 `gpu_frame_context.json`、`event_index.csv`、`gpu_pass_events.json`、`gpu_pass_overview.json`、`gpu_pass_resources.json`、`gpu_pass_catalog.json`。
2. 主 agent
   - 在 rdoc-agent event/draw 证据或 Crowd 专用 fact package 上排序、分组和比较后继续分析。
   - 只在必要时拉取完整 `pipeline state`、shader 或资源数据。

核心原则只有一句话：

子 agent 负责整理足够支撑结论的证据索引，不能用早期热点判断替代完整证据；`renderdoc_gpu_context_cli.py` 只属于 Crowd/VAT 专用隐藏链路。
