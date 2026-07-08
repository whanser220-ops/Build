# Git 代码 + SVN 美术资产工作流

本文记录 Unity6 项目当前的双版本库约定：Git 管代码与工程配置，SVN 管美术资产。

## 分工

- Git 管理：代码、Editor 工具、测试、`Packages/`、`ProjectSettings/`、自动化脚本、文档、顶层 Unity 目录 `.meta`。
- SVN 管理：仅 `Assets/GameResources` 目录内容及其内部 `.meta`。
- Git 通过根目录 `svn-assets.lock.json` 固定 SVN revision。代码提交依赖新资产时，必须同步更新该 lock 文件。

## 本地同步

首次 clone Git 仓库后，在仓库根目录运行：

```powershell
.\tools\Sync-SvnAssets.ps1 -ProjectPath .
```

如果本机没有缓存 SVN 凭据，可以显式传入账号：

```powershell
.\tools\Sync-SvnAssets.ps1 -ProjectPath . -Username unity6_art -Password "<password>"
```

同步脚本会读取 `svn-assets.lock.json`，把每个资产根目录 checkout / update 到指定 revision。若 SVN working copy 有本地修改，脚本会停止，避免覆盖美术改动。

## 提交流程

代码改动：

1. 修改 Git 管理的代码/配置/文档。
2. 如果不依赖新资产，不需要改 `svn-assets.lock.json`。
3. 正常 Git commit / push。

资产改动：

1. 在 `Assets/GameResources` 中修改资源。
2. 用 TortoiseSVN 或 `svn commit` 提交资产。
3. 记录新的 SVN revision。
4. 更新 `svn-assets.lock.json` 的 `revision` 字段。
5. 将 lock 文件随相关 Git 代码提交。

## 注意事项

- 不要把 SVN 管理目录重新加入 Git。
- 不要提交 `.svn/`。
- 顶层 sibling `.meta`，例如 `Assets/GameResources.meta`，仍由 Git 管理。
- 修改 lock 后，建议运行一次 `.\tools\Sync-SvnAssets.ps1 -ProjectPath .` 验证 revision 可同步。
