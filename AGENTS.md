# Repository Guidelines

## 入口说明

本文件只保留 Codex 在本仓库开工前必须先读的短规则。

本仓库是一个基于 Unity 6 的技术验证仓库，当前重点集中在以下方向：

- URP / RenderGraph 渲染实验
- Crowd VAT / Scene Query / 大规模人群系统
- RenderDoc 抓帧、性能采样与 AI 分析链路

需要更多背景时，先看索引，再按需展开，不要一次性加载全部文档：

- 仓库架构入口：`ARCHITECTURE.md`
- 文档总索引：`docs/README.md`
- 当前方案与执行稿索引：`docs/PLANS.md`
- 设计约束与实现规范：`docs/DESIGN.md`
- 上下文管理与 artifact 约定：`docs/codex-context-engineering.md`
- 多 agent 协作入口：`docs/agents/README.md`

## 使用规则

- 只读取与当前任务直接相关的文档和源码。
- 先搜索，后展开。优先使用 `rg`、`Select-String` 和局部读取，不先通读整个仓库。
- 需要设计背景时，先打开索引页，再定位到单篇文档。
- 不要一次性通读 `docs/plans/`、`renderdoc-mcp/`、大型日志目录或完整抓帧目录。
- 长日志、网页正文、大型 JSON、RenderDoc 分析中间结果优先卸载到 `.workspace/`，对话里只保留摘要、路径、关键证据 id 和下一步。

## 仓库范围

- 本仓库是 Unity 6 项目，版本为 `6000.0.46f1`。
- 核心玩法与渲染代码位于 `Assets/Project`。
- 场景位于 `Assets/Scenes`。
- 渲染管线与项目级配置位于 `Assets/Settings/Default`。
- `Library/`、`Temp/`、`Logs/`、`obj/`、`.workspace/` 为生成或临时目录，不作为源码修改或评审对象，除非任务明确要求。

## 上下文管理

- 当前任务状态优先记录到 `.workspace/memory/task_state.md`。
- 关键决策优先记录到 `.workspace/memory/decisions.md`。
- 待确认问题优先记录到 `.workspace/memory/open_questions.md`。
- 摘要只是索引，不替代原始证据；需要精确信息时，重新检索或重新打开原文件。
- 子 agent 默认输出“摘要 + 索引 + 定位信息”，不要把整份原始材料直接塞回主对话。
- RenderDoc MCP 分析禁止默认全量 dump：先做 `frame_summary`、`event_index`、`hot_events`，锁定可疑 event 后再回查原始 `pipeline state`、shader、buffer 或 texture 数据。
- RenderDoc 相关分析产物优先放在 `.workspace/artifacts/renderdoc-analysis/` 或任务专属子目录。

## 方案同步

- `docs/plans/` 里的文档不默认等于“当前实现”；读取前先看 `docs/PLANS.md` 和文档头部状态。
- 如果代码改动影响某篇“当前实现基线”或“强同步”文档覆盖的系统，同一轮任务里应同步更新对应方案文档。
- 如果本轮不适合完整改文档，至少要把该文档状态改成“已偏离实现”并写明偏离点，避免后续继续误读。
- 新方案优先新增文档或在原文档中明确“替代关系”，不要让多个文档都看起来像当前主路径。

## 快速工作规则

- 先看最小相关文件集：入口脚本、`ScriptableRendererFeature`、Pass、Shader、测试文件和受影响场景。
- 代码改动优先最小化，不做无关重构，不修改生成目录。
- 当任务明确要求启用子 agent / 多 agent 协作时，默认通过 `spawn_agent` 使用 `gpt-5.4` 与 `xhigh`；除非用户明确指定其他模型或推理强度，或更高优先级指令另有要求。

## Unity 代码约定

- 使用 4 空格缩进，文本编码为 UTF-8。
- C# 命名：类型、方法、属性使用 `PascalCase`，局部变量和参数使用 `camelCase`，私有序列化字段使用 `_camelCase`。
- 每个渲染功能保持独立目录，Feature 和 Pass 分职责组织。
- 优先使用小而单一职责的 `MonoBehaviour`，渲染逻辑集中在 URP `ScriptableRendererFeature` / Pass 类中。
- 不要无意义修改 `.meta` 文件；只有资源关系或导入设置确实需要时才修改。
- 不要顺手保存无关场景、预制体或项目设置。
- 新增或修改 Markdown 文档时，默认使用中文描述。
- 修改 ComputeShader、GPU dispatch marker、GPU pass 注册、kernel 读写资源或线程组/dispatch 公式时，同轮同步维护 `docs/gpu-pass-catalog/gpu_pass_catalog.yaml` 与对应 `docs/gpu-pass-catalog/shaders/*.compute.ai.yaml`，并运行 `python tools/renderdoc-cli/validate_gpu_pass_catalog.py --repo .`。

## 验证与测试

- 修改完成后运行最小必要验证；如果没有执行验证，必须说明原因和剩余风险。
- 常用命令在仓库根目录执行：
- `Unity.exe -projectPath .`
- `Unity.exe -batchmode -projectPath . -quit -buildTarget Win64 -logFile Logs/build.log`
- `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/EditMode.xml -quit`
- `Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults Logs/PlayMode.xml -quit`
- 新增测试时，优先放在 `Assets/Tests/EditMode` 与 `Assets/Tests/PlayMode`，测试文件命名为 `*Tests.cs`。

## 提交与变更说明

- 建议使用 Conventional Commits，例如 `feat(grass): add RenderGraph interaction mask pass`。
- 单次提交聚焦一个功能或一个修复点。
- 涉及渲染变更时，结果说明应包含影响的场景、资源、验证步骤，以及截图或动图。
