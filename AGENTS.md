# Repository Guidelines

## 入口说明

本文件只保留 Codex 在本仓库开工前需要先读的短规则。
详细背景、上下文工程方法和 Unity 工作流说明放在 `docs/`：

- `docs/README.md`：详细文档索引
- `docs/codex-unity-workflow.md`：适合本仓库的 Codex 日常工作方式
- `docs/codex-agent-workflow-template.md`：任务卡、执行计划、验证与交付模板
- `docs/codex-context-engineering.md`：上下文卸载、artifact、工作记忆与检索约定
- `docs/agents/README.md`：多 agent 检索资料、对比分析并产出 PRD 的复用入口

## 仓库范围

- 本仓库是 Unity 6 项目，版本为 `6000.0.46f1`。
- 核心玩法与渲染代码位于 `Assets/Project`。
- 场景位于 `Assets/Scenes`。
- 渲染管线与项目级配置位于 `Assets/Settings/Default`。
- `Library/`、`Temp/`、`Logs/`、`obj/` 为生成目录，不作为源码修改或评审对象。
- `.workspace/artifacts/` 与 `.workspace/summaries/` 是协作辅助目录，不作为功能源码评审对象，除非任务明确要求。

## 快速工作规则

- 先搜索，后展开。优先使用 `rg`、`Select-String` 和局部读取，不先通读整个仓库。
- 先看最小相关文件集：入口脚本、`ScriptableRendererFeature`、Pass、Shader、测试文件和受影响场景。
- 长日志、网页正文、大型 JSON、阶段性长分析先写入 `.workspace/artifacts/`，对话中只保留摘要、路径、重要性和下一步。
- 当前任务状态写入 `.workspace/memory/task_state.md`，关键决策写入 `.workspace/memory/decisions.md`，待确认问题写入 `.workspace/memory/open_questions.md`。
- 摘要只是索引，不替代原始证据；需要精确信息时，重新检索或重新打开文件。
- 代码改动优先最小化，不做无关重构，不修改生成目录。

## Unity 代码约定

- 使用 4 空格缩进，文本编码为 UTF-8。
- C# 命名：类型、方法、属性使用 `PascalCase`，局部变量和参数使用 `camelCase`，私有序列化字段使用 `_camelCase`。
- 每个渲染功能保持独立目录，Feature 和 Pass 分职责组织。
- 优先使用小而单一职责的 `MonoBehaviour`，渲染逻辑集中在 URP `ScriptableRendererFeature` / Pass 类中。
- 不要无意义修改 `.meta` 文件；只有资源关系或导入设置确实需要时才修改。
- 不要顺手保存无关场景、预制体或项目设置。

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
