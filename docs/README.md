# 文档总索引

本文档用于帮助 agent 先定位，再展开，不建议一次性顺序通读整个 `docs/`。

## 推荐读取顺序

1. 先读根级 `AGENTS.md`。
2. 需要仓库级背景时读 `../ARCHITECTURE.md`。
3. 需要找方案入口时读 `PLANS.md`。
4. 需要找规范时读 `DESIGN.md`。
5. 需要处理长日志、RenderDoc 或多 agent 协作时，再读专项文档。

## 核心索引

- `PLANS.md`
  - 当前设计稿、执行稿与专题方案入口。
- `DESIGN.md`
  - 仓库级实现规范、文档约定与验证基线。
- `assets-directory-layout.md`
  - `Assets/` 目录组织规则、例外目录与后续资源落位约定。
- `angry-mesh-sources-import-workflow.md`
  - `Assets/GameResources` 源素材导入检查、规则匹配与持久化工作流。
- `svn-art-assets-workflow.md`
  - Git 管代码、SVN 管美术资产的本地同步、lock 文件与提交流程。
- `codex-context-engineering.md`
  - 上下文卸载、artifact、记忆文件和 RenderDoc 分析分层约定。
- `agents/README.md`
  - 多 agent 协作分工、输入输出契约与 RenderDoc 分析建议。
- `gpu-pass-catalog/README.md`
  - GPU Pass 稳定 ID、AI 填充 Catalog、Compute Shader 描述文件、强同步规则与 Joined Context 约定。

## 方案文档

当前主要方案集中在 `plans/`：

- Crowd / Scene Query / 大规模人群
- 角色移动
- 可乐瓶 PBD 技术设计
- RenderDoc 抓帧与性能采样工具设计
- GPU Pass 语义索引与 AI Joined Context

具体条目请查看 `PLANS.md`，不要直接整目录全读。

## 与 `.workspace/` 的关系

- `docs/` 存放可复用规则和长期文档。
- `.workspace/artifacts/` 存放本地分析产物、长日志、抓帧摘要和中间结果。
- `.workspace/memory/` 存放当前任务状态、关键决策和待确认问题。

一句话原则：长期规则进 `docs/`，任务中间产物进 `.workspace/`。
