# 仓库架构入口

本文档只提供仓库级导航，不展开长篇背景。需要细节时，请继续打开对应索引或具体设计文档。

## 项目定位

本仓库是一个基于 Unity 6 的技术验证仓库，主要承载三类工作：

- URP / RenderGraph 渲染实验与图形功能验证
- Crowd VAT、Scene Query 与大规模人群系统迭代
- RenderDoc 抓帧、性能采样与 AI 分析工具链建设

## 主要目录

- `Assets/Project/CS`
  - 渲染实验、草地系统、角色与摄像机等偏独立的功能模块。
- `Assets/Project/Crowds`
  - Crowd VAT、人群运行时、编辑器工具、调试与查询系统。
- `Assets/Project/Characters`
  - 角色相关脚本、生成资产与运行时接入。
- `Assets/Project/Tools`
  - 性能分析、RenderDoc 抓帧与辅助工具链。
- `Assets/Scenes`
  - 当前主要集成场景位于 `SampleScene.unity`。
- `Assets/Settings/Default`
  - URP 与项目级渲染配置。
- `Assets/ThirdParty`
  - 外部资源包、Asset Store 包与供应商原始内容。

## 文档入口

- 文档总索引：`docs/README.md`
- 当前方案与执行稿索引：`docs/PLANS.md`
- 设计约束与实现规范：`docs/DESIGN.md`
- 上下文管理：`docs/codex-context-engineering.md`
- 多 agent 协作：`docs/agents/README.md`

## RenderDoc 分析链路

当前 RenderDoc 相关工作建议按以下层级理解：

1. 运行时或编辑器侧生成抓帧 artifact。
2. RenderDoc 打开 `.rdc` 并由 MCP 提供按需读取能力。
3. AI 先生成轻量索引和热点摘要，再回查可疑 event 的原始证据。

不要把“完整抓帧数据转存成巨型文本”当作默认流程；那会同时放大磁盘体积、检索成本和对话上下文压力。
