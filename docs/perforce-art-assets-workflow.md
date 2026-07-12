# Git 代码 + Perforce 美术资产工作流

本文记录 Unity6 项目当前的双版本库约定：Git 管代码与工程配置，Perforce 管 Unity 内容资产。

## 分工

- Git 管理：代码、Editor 工具、测试、`Packages/`、`ProjectSettings/`、自动化脚本、文档、模块根目录与 `Art` / `Runtime` 目录本身的 `.meta`。
- Perforce 管理：各内容模块 `Art` 与 `Runtime` 目录内部的资源和内部 `.meta`。
- 根目录 `p4-assets.lock.json` 记录默认 Perforce port、view 文件和可选 pinned changelist；`tools/perforce-assets.p4view` 记录当前资产 view。

当前 Perforce 管理范围：

```text
Assets/Game/Characters/Qianxia/Art/**
Assets/Game/Characters/Qianxia/Runtime/**
Assets/Game/Core/Input/Runtime/**
Assets/Game/Shared/StylizedPackCommon/Art/**
Assets/Game/Shared/StylizedPackCommon/Runtime/**
Assets/Game/UI/UILogin/Runtime/**
Assets/Game/UI/UIMain/Runtime/**
Assets/Game/Worlds/Meadow/Art/**
Assets/Game/Worlds/Meadow/Runtime/**
```

## 本地同步

首次 clone Git 仓库后，先配置 `P4_USERNAME` 与 `P4_PASSWORD`，然后运行：

```powershell
$env:P4_USERNAME = "<user>"
$env:P4_PASSWORD = "<ticket-or-password>"
.\tools\Sync-PerforceAssets.ps1 -Client "<workspace-client>" -Root .
```

脚本默认读取：

```text
p4-assets.lock.json
tools/perforce-assets.p4view
```

如果需要临时覆盖端口、view 或 changelist：

```powershell
.\tools\Sync-PerforceAssets.ps1 -Port "ssl:perforce.example.com:1666" -Client "<workspace-client>" -Root . -ViewFile tools\perforce-assets.p4view -Changelist 12345
```

## 提交流程

代码改动：

1. 修改 Git 管理的代码、配置、文档或目录 `.meta`。
2. 如果不依赖新的 Perforce 资产版本，不需要改 `p4-assets.lock.json`。
3. 正常 Git commit / push。

资产改动：

1. 在对应模块的 `Assets/Game/**/Art` 或 `Assets/Game/**/Runtime` 中修改资源。
2. 用 P4V 或 `p4 submit` 提交资产。
3. 如果本次 Git 代码依赖特定资产版本，记录 submitted changelist。
4. 更新 `p4-assets.lock.json` 的 `changelist` 字段，或在 CI 中设置 `P4_ASSET_CL`。
5. 将 lock 文件随相关 Git 代码提交。

## 注意事项

- 不要把 Perforce 管理的资源内容重新加入 Git。
- 不要提交 Perforce ticket、临时 client spec 或 `.workspace/` 下同步产物。
- `Assets/Game/`、内容域、模块根目录、`Art.meta`、`Runtime.meta` 由 Git 管理，保证 Unity 目录 GUID 稳定。
- `Art` / `Runtime` 内部资源和内部 `.meta` 由 Perforce 管理，避免 Git 仓库膨胀。
- 修改 lock 或 view 后，建议运行一次 `.\tools\Sync-PerforceAssets.ps1 -Client "<workspace-client>" -Root .` 验证可同步。
