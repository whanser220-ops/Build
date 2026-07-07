# Unity 双端 CI 构建说明

本文记录本仓库 GitHub Actions 双端开发包构建约定。当前目标是先稳定产出 Android APK 与 Windows PC 包，不在主流程中执行 Addressables 内容构建。

## Runner 前置条件

- 使用 Windows 自托管 GitHub Actions runner，标签为 `self-hosted`、`Windows`、`unity6`。
- Runner 需要安装 Unity `6000.0.46f1`，并包含以下模块：
  - Windows Build Support
  - Android Build Support
  - Android SDK / NDK / OpenJDK
- Unity 许可证需要提前在 runner 机器上完成激活。
- `tools/Invoke-Unity.ps1` 会优先读取 `UNITY_EXE` 或 `UNITY_EDITOR_PATH`，否则按 `ProjectSettings/ProjectVersion.txt` 查找 Unity Hub 安装路径。
- 当前工作流使用 `actions/checkout@v5` 与 `actions/upload-artifact@v7`，runner 版本需要满足这些 action 的 Node 运行时要求；如果 GitHub 后续废弃某个主版本，应先调整 workflow 再恢复 CI。

## 触发方式

开发包工作流文件为 `.github/workflows/unity-ci.yml`，PR 快检工作流文件为 `.github/workflows/unity-pr-check.yml`。

- 推送到 `Main` 时自动触发开发包工作流。
- 也可以在 GitHub Actions 页面手动执行 `workflow_dispatch`。
- 工作流使用固定 concurrency group `unity6-player-build`，避免同一个 Unity 项目被同一台构建机并发打开。
- 任意 Pull Request 都会触发 PR 快检。该工作流显式 checkout `refs/pull/${{ github.event.pull_request.number }}/merge`，也就是 GitHub 针对当前 PR 目标分支临时生成的虚拟合并提交，用来验证“如果当前 feature 分支合进它声明的目标分支，会变成什么样”。
- PR 快检启用 `cancel-in-progress: true`，同一个 PR 的新提交会取消旧 run，避免多人频繁小改动时排队。

## 构建流程

1. Checkout 仓库。
   - CI 使用 sparse checkout，只拉取 `ProjectSettings/`、`Packages/`、`Assets/Editor/`、`Assets/Scripts/Addressables/`、`Assets/Settings/`、`Assets/Tests/BuildPipeline/`、`svn-assets.lock.json` 与 `tools/` 中的构建脚本。
   - 美术资产由 SVN 管理，Git checkout 只保留 `Assets/GameResources.meta` 这个 SVN 根目录的顶层 sibling `.meta`。
2. 挂载本机持久 `Library/` cache。
3. 清理 `.workspace/builds` 与本轮 CI 日志；不要删除 `Library/`。
4. 运行构建链路 EditMode 测试：
   ```powershell
   .\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode `
     -runTests -testPlatform EditMode `
     -assemblyNames Project.BuildPipeline.EditMode.Tests `
     -testResults Logs/EditMode.xml `
     -logFile Logs/EditMode.log
   ```
5. 构建 Android 开发 APK：
   ```powershell
   .\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode -quit `
     -executeMethod Unity6.Ci.CiPlayerBuild.BuildAndroidDevelopment `
     -logFile Logs/build-android.log `
     --ci-output .workspace/builds/android/Unity6-Android-Development.apk `
     --ci-scenes Assets/Tests/BuildPipeline/Fixtures/BuildPipelineSmoke.unity
   ```
6. 构建 Windows 开发 Player：
   ```powershell
   .\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode -quit `
     -executeMethod Unity6.Ci.CiPlayerBuild.BuildWindowsDevelopment `
     -logFile Logs/build-windows.log `
     --ci-output .workspace/builds/windows/Unity6-Windows-Development/Unity6.exe `
     --ci-scenes Assets/Tests/BuildPipeline/Fixtures/BuildPipelineSmoke.unity
   ```
7. 压缩 Windows Player 目录并上传产物与日志。

## PR 快检流程

PR 快检只服务“能不能合并”，不出完整包：

1. Checkout 虚拟合并提交 `refs/pull/<PR>/merge`，该 ref 对应 PR head 合入 PR base/目标分支后的临时结果。
2. 根据 `svn-assets.lock.json` 运行 `tools/Sync-SvnAssets.ps1`，把 SVN 美术资产同步到锁定 revision。
3. 挂载同一个本机持久 `Library/` cache。
4. 运行构建管线 EditMode Tests。
5. 运行当前 feature 相关的 EditMode 单元测试，例如 `Project.Qianxia.EditMode.Tests`。
6. 上传测试日志，不上传 Android APK 或 Windows ZIP。

## SVN 美术资产

Git 仓库不再跟踪运行时和源美术资产内容。当前 SVN 仓库为 `https://localhost/svn/Unity6-ArtAssets`，Git 通过根目录 `svn-assets.lock.json` 固定 exact revision。

任何依赖完整美术资产的 CI job 都应在 Unity 启动前运行：

```powershell
.\tools\Sync-SvnAssets.ps1 -ProjectPath .
```

自托管 runner 需要通过凭据缓存或 `SVN_USERNAME` / `SVN_PASSWORD` secret 提供 SVN 访问凭据。

## Library cache

CI 使用单个本机持久 `Library/` cache，默认路径为 `%LOCALAPPDATA%\Unity6Ci\LibraryCache\<repo>\Library`。`tools/Use-UnityLibraryCache.ps1` 会在 checkout 后把当前工作区的 `Library/` 建成指向该目录的 junction。

这个 cache 不按源码 hash 或构建平台拆分；普通资源、脚本或构建夹具变更时，由 Unity 的 `ArtifactDB`、`Artifacts`、`Bee`、`ShaderCache` 与 `PlayerDataCache` 在同一个 `Library/` 内做增量更新。只有 Unity 版本或 cache schema 不兼容时，旧 cache 会被移动为 `Library.stale.<timestamp>`，下一轮重新冷建。

## 产物

- Android：`.workspace/builds/android/Unity6-Android-Development.apk`
- Windows：`.workspace/builds/windows/Unity6-Windows-Development.zip`
- 日志与测试结果：
  - `Logs/EditMode.xml`
  - `Logs/EditMode.log`
  - `Logs/build-android.log`
  - `Logs/build-windows.log`

## 构建入口

统一 Player 构建入口为 `Unity6.Ci.CiPlayerBuild`，入口文件位于 `Assets/Editor/BuildPipeline/CiPlayerBuild.cs`。构建管线不以 `Assets/Project/` 或 `Assets/ThirdParty/` 下的旧工具脚本作为主入口。

- `Unity6.Ci.CiPlayerBuild.BuildAndroidDevelopment`
- `Unity6.Ci.CiPlayerBuild.BuildWindowsDevelopment`

支持命令行参数：

- `--ci-output <path>`：覆盖默认输出路径。
- `--ci-scenes <scene1;scene2>`：覆盖 `EditorBuildSettings` 中启用的场景列表。CI 主流程使用 `Assets/Tests/BuildPipeline/Fixtures/BuildPipelineSmoke.unity` 作为构建 smoke scene，避免依赖游戏内容目录的场景配置。

## 常见失败点

- `No enabled scenes found in EditorBuildSettings.`：构建场景清单为空。
- `Missing or invalid scene assets`：场景路径不存在或不是有效 `SceneAsset`。
- Android SDK/NDK/JDK 找不到：检查 Unity Android 模块安装，或通过命令行参数显式传入路径。
- Windows artifact 缺少 `Unity6.exe`：先查看 `Logs/build-windows.log` 中的 BuildPipeline 错误。
- EditMode 失败：CI 不会继续产出 Player 包，优先查看 `Logs/EditMode.xml` 和 `Logs/EditMode.log`。
- EditMode 没生成 XML：确认测试命令没有带 `-quit`；Unity Test Framework 会自行退出 Editor。

## Addressables

本轮 CI 主流程不运行 Addressables 内容构建，避免构建过程中刷新 Addressables group 或生成配置导致工作区变脏。Addressables 构建工具已从 `Assets/Project/` 拆到 `Assets/Editor/BuildPipeline/Addressables/ProjectAddressablesBuild.cs`，运行时占位组件位于 `Assets/Scripts/Addressables/AngryMeshAddressablePrefabInstance.cs`。后续需要时，建议新增独立 job 调用 `ProjectAddressablesBuild.BuildFromCommandLine`，并明确它是验证型任务还是发布型任务。
