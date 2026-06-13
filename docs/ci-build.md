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

工作流文件为 `.github/workflows/unity-ci.yml`。

- 推送到 `codex/111` 时自动触发。
- 也可以在 GitHub Actions 页面手动执行 `workflow_dispatch`。
- 工作流使用固定 concurrency group `unity6-player-build`，避免同一个 Unity 项目被同一台构建机并发打开。

## 构建流程

1. Checkout 仓库。
2. 清理 `.workspace/builds` 与本轮 CI 日志。
3. 运行构建链路 EditMode 测试：
   ```powershell
   .\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode `
     -runTests -testPlatform EditMode `
     -assemblyNames Project.BuildPipeline.EditMode.Tests `
     -testResults Logs/EditMode.xml `
     -logFile Logs/EditMode.log
   ```
4. 构建 Android 开发 APK：
   ```powershell
   .\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode -quit `
     -executeMethod ProjectPlayerBuild.BuildAndroidDevelopment `
     -logFile Logs/build-android.log `
     --ci-output .workspace/builds/android/Unity6-Android-Development.apk
   ```
5. 构建 Windows 开发 Player：
   ```powershell
   .\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode -quit `
     -executeMethod ProjectPlayerBuild.BuildWindowsDevelopment `
     -logFile Logs/build-windows.log `
     --ci-output .workspace/builds/windows/Unity6-Windows-Development/Unity6.exe
   ```
6. 压缩 Windows Player 目录并上传产物与日志。

## 产物

- Android：`.workspace/builds/android/Unity6-Android-Development.apk`
- Windows：`.workspace/builds/windows/Unity6-Windows-Development.zip`
- 日志与测试结果：
  - `Logs/EditMode.xml`
  - `Logs/EditMode.log`
  - `Logs/build-android.log`
  - `Logs/build-windows.log`

## 构建入口

统一 Player 构建入口为 `ProjectPlayerBuild`。

- `ProjectPlayerBuild.BuildAndroidDevelopment`
- `ProjectPlayerBuild.BuildWindowsDevelopment`

支持命令行参数：

- `--ci-output <path>`：覆盖默认输出路径。
- `--ci-scenes <scene1;scene2>`：覆盖 `EditorBuildSettings` 中启用的场景列表。
- Android 额外支持 `-androidSdkPath`、`-androidNdkPath`、`-androidJdkPath`。

旧入口 `AndroidDeviceBuild.BuildApk` 仍保留，内部复用新的 Android 开发包构建逻辑，默认输出仍为 `.workspace/builds/android/Unity6DeviceTest.apk`。

## 常见失败点

- `No enabled scenes found in EditorBuildSettings.`：构建场景清单为空。
- `Missing or invalid scene assets`：场景路径不存在或不是有效 `SceneAsset`。
- Android SDK/NDK/JDK 找不到：检查 Unity Android 模块安装，或通过命令行参数显式传入路径。
- Windows artifact 缺少 `Unity6.exe`：先查看 `Logs/build-windows.log` 中的 BuildPipeline 错误。
- EditMode 失败：CI 不会继续产出 Player 包，优先查看 `Logs/EditMode.xml` 和 `Logs/EditMode.log`。
- EditMode 没生成 XML：确认测试命令没有带 `-quit`；Unity Test Framework 会自行退出 Editor。

## Addressables

本轮 CI 主流程不运行 Addressables 内容构建，避免构建过程中刷新 Addressables group 或生成配置导致工作区变脏。后续需要时，建议新增独立 job 调用 `ProjectAddressablesBuild.BuildFromCommandLine`，并明确它是验证型任务还是发布型任务。
