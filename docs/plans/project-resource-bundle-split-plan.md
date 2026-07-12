# ANGRY MESH Addressables 构建方案

- 状态：历史方案，已被 `Assets/Game/**/{Art,Runtime}` 目录规范与 YooAsset 构建器替代
- 同步级别：弱同步
- 当前实现锚点：
  - `Assets/Editor/BuildPipeline/Addressables/ProjectAddressablesBuild.cs`
  - `Assets/Editor/BuildPipeline/Addressables/AngryMeshAddressablesReferenceConverter.cs`
  - `Assets/Scripts/Addressables/AngryMeshAddressablePrefabInstance.cs`
- 最后实现核对：2026-07-12

> 当前实现不再以顶层 `Assets/GameAssets` / `Assets/GameResources` 作为主路径。本文以下内容仅保留历史设计背景；新的资源落位以 `docs/assets-directory-layout.md` 为准，构建收集以 `Assets/Editor/BuildPipeline/YooAsset/ProjectYooAssetBuild.cs` 为准。

## 当前方向

项目资源分包不再维护自研 `BuildPipeline.BuildAssetBundles` 管线、`project_asset_bundle_manifest.json` 反查表和 `AngryMeshAssetBundleManager` 运行时加载器。

新的主线转向 Unity Addressables：

1. 由 `ProjectAddressablesBuild` 收集 `Assets/GameAssets` 构建区资源并生成 `addressables_build_plan.json`。
2. 统计跨 group 的共享依赖摘要，供后续离线分析和人工治理使用，但当前不在主流程内自动拆公共依赖组。
3. 根据计划创建或刷新 Addressables group。
4. 使用 Addressables 的 packed content build 执行资源构建。
5. 运行时占位组件通过 Addressables address 异步加载 prefab。

当前构建主线改为顶层构建区和源产区：

```text
Assets/GameAssets
Assets/GameResources
```

其中 `Assets/GameAssets` 是运行时资产构建白名单，打包脚本默认只扫描该目录；`Assets/GameResources` 是源素材产区，只参与资源质量检查，只有显式传入 `--addressables-include-source-assets` 时才会额外生成源素材 Addressables 组。少量跨生态运行时共享贴图可以通过显式 `angrymesh.shared.*` 组纳入构建，例如当前 `Assets/Game/Shared/StylizedPackCommon/Art/Sources/Textures` 下的风噪声贴图。

`Assets/GameAssets` 下的包目录直接映射为业务 AssetBundle / Addressables group 颗粒度。当前规则分为显式生命周期组和通用目录组：

```text
Assets/GameAssets
├── Common/
│   ├── ASP Global Settings/
│   ├── Functions/
│   ├── Shaders/
│   └── Fonts/
├── Scenes/
├── Configs/
│   └── Post Processing/
├── Textures/
├── Prefabs/
│   ├── Hero/
│   └── Monster/
├── Worlds/
│   └── Meadow/
│       ├── Shared/
│       │   ├── Materials/
│       │   ├── Prefabs/
│       │   └── Configs/
│       ├── Seasons/
│       │   ├── Autumn/
│       │   ├── Summer/
│       │   └── Winter/
│       ├── Chunks/
│       │   ├── Chunk_000_000/
│       │   ├── Chunk_000_001/
│       │   └── Chunk_001_000/
│       └── Scenes/
└── UIModules/
    ├── UILogin/
    └── UIMain/
```

旧 `Assets/ANGRY MESH` 插件目录不再作为构建白名单；构建相关运行时资源已经迁入 `Assets/GameAssets`。

当前已迁移的 ANGRY MESH 运行时资源：

```text
Assets/Game/Shared/StylizedPackCommon/Runtime/ASP Global Settings
Assets/Game/Shared/StylizedPackCommon/Runtime/Functions
Assets/Game/Shared/StylizedPackCommon/Runtime/Shaders
Assets/Game/Worlds/Meadow/Runtime/Shared
Assets/Game/Worlds/Meadow/Runtime/Seasons/Autumn
Assets/Game/Worlds/Meadow/Runtime/Seasons/Summer
Assets/Game/Worlds/Meadow/Runtime/Seasons/Winter
Assets/Game/Worlds/Meadow/Runtime/Chunks
Assets/Game/Worlds/Meadow/Runtime/Scenes
```

当前已迁移的 ANGRY MESH 源素材：

```text
Assets/Game/Shared/StylizedPackCommon/Art/Sources
Assets/Game/Worlds/Meadow/Art/Sources
```

## 默认 Addressables Group

当前默认只纳入 `Assets/GameAssets` 下的运行时产成品资源：

| Group | 当前输入范围 |
|---|---|
| `angrymesh.shared.shaders` | `Assets/Game/Shared/StylizedPackCommon/Runtime/Shaders` |
| `angrymesh.shared.textures` | `Assets/Game/Shared/StylizedPackCommon/Art/Sources/Textures` |
| `angrymesh.gameassets.common` | `Assets/Game/Shared/StylizedPackCommon/Runtime`，排除已显式进入 `angrymesh.shared.shaders` 的 shader |
| `angrymesh.worlds.meadow.shared` | `Assets/Game/Worlds/Meadow/Runtime/Shared` |
| `angrymesh.worlds.meadow.season.autumn` | `Assets/Game/Worlds/Meadow/Runtime/Seasons/Autumn` |
| `angrymesh.worlds.meadow.season.summer` | `Assets/Game/Worlds/Meadow/Runtime/Seasons/Summer` |
| `angrymesh.worlds.meadow.season.winter` | `Assets/Game/Worlds/Meadow/Runtime/Seasons/Winter` |
| `angrymesh.worlds.meadow.chunks.chunk_000_000` | `Assets/Game/Worlds/Meadow/Runtime/Chunks/Chunk_000_000` |
| `angrymesh.worlds.meadow.chunks.chunk_000_001` | `Assets/Game/Worlds/Meadow/Runtime/Chunks/Chunk_000_001` |
| `angrymesh.worlds.meadow.chunks.chunk_001_000` | `Assets/Game/Worlds/Meadow/Runtime/Chunks/Chunk_001_000` |
| `angrymesh.worlds.meadow.scenes` | `Assets/Game/Worlds/Meadow/Runtime/Scenes` |
| `angrymesh.gameassets.textures` | `Assets/GameAssets/Textures` |
| `angrymesh.gameassets.prefabs.hero` | `Assets/GameAssets/Prefabs/Hero` |
| `angrymesh.gameassets.prefabs.monster` | `Assets/GameAssets/Prefabs/Monster` |
| `angrymesh.gameassets.uimodules.uilogin` | `Assets/Game/UI/UILogin/Runtime` |
| `angrymesh.gameassets.uimodules.uimain` | `Assets/Game/UI/UIMain/Runtime` |

`Worlds/<World>` 下按生命周期显式生成 `shared`、`season.<Season>`、`chunks.<ChunkId>` 与 `scenes` 组；其他 GameAssets 目录继续按 `angrymesh.gameassets.<相对目录路径>` 自动生成，路径分隔符会转换为 `.`。当 `Assets/Game/Worlds/Meadow/Runtime` 存在时，旧 Meadow prefab、config 和 scene 路径会从默认扫描中排除。

只有显式传入 `--addressables-include-source-assets` 时，才会额外从 `Assets/GameResources` 生成 `angrymesh.gameresources.*` 组。

## 共享依赖处理策略

当前主流程不再在构建阶段自动把共享依赖提升到 `angrymesh.shared.*` 公共组；`angrymesh.shared.shaders` 与 `angrymesh.shared.textures` 是显式维护的稳定公共组。

当前做法是：

1. 先按显式公共组、`Common` 常驻组、`Worlds/<World>` 生命周期组和剩余 `Assets/GameAssets` 目录颗粒度生成业务 group。
2. 继续对每个 group 做 `AssetDatabase.GetDependencies(asset, true)` 统计。
3. 在 `addressables_build_plan.json` 中记录 `sharedDependencyCount` 和各 group 的 `sharedDependencies` 摘要。
4. 先执行 Addressables content build，优先确保包体可构建、可校验、可落盘。
5. 后续再离线分析公共依赖，决定是否做人为抽离、目录调整或额外配置。

这样调整的原因是，当前阶段先保证“目录到包体”的主链路稳定，避免在构建期自动提升公共依赖时引入额外的分组策略、命名规则和误拆分风险。

当前离线治理公共依赖的候选路径包括：

- 使用 [AssetBundleReporter](https://zhida.zhihu.com/search?content_id=254925364&content_type=Article&match_order=1&q=AssetBundleReporter&zhida_source=entity) 对构建结果做包体分析，识别被过多 bundle 引用的资源。
- 基于 `addressables_build_plan.json`、BuildLayout 和资源依赖抽取结果做离线统计，找出高复用公共资源。
- 人工判断哪些资源属于稳定公共依赖，维护额外 JSON 配置，再把这类显式规则接回主流程。

这些离线分析与治理步骤当前不直接修改默认构建入口；主流程只负责把共享依赖摘要暴露出来，供后续工具或人工消费。

## 构建入口

菜单入口：

```text
Tools/ANGRY MESH/Addressables/Prepare Groups
Tools/ANGRY MESH/Addressables/Build Content
```

命令行 dry run：

```powershell
.\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode -quit `
  -executeMethod ProjectAddressablesBuild.BuildFromCommandLine `
  --addressables-dry-run `
  --addressables-target Android `
  -logFile Logs\addressables-dry-run.log
```

命令行构建：

```powershell
.\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode -quit `
  -executeMethod ProjectAddressablesBuild.BuildFromCommandLine `
  --addressables-target Android `
  -logFile Logs\addressables-build.log
```

常用参数：

| 参数 | 作用 |
|---|---|
| `--addressables-target <BuildTarget>` | 指定目标平台，默认使用当前 Editor active build target |
| `--addressables-build-root <path>` | 指定资源根目录，默认 `Assets`；可传 `Assets` 或 `Assets/GameAssets`，最终扫描 `Assets/GameAssets` |
| `--addressables-plan-output <path>` | 指定构建计划输出路径 |
| `--addressables-dry-run` | 只准备 group 与计划，不执行 Addressables content build |
| `--addressables-include-source-assets` | 额外纳入 `Assets/GameResources` 源素材组 |
| `--addressables-keep-stale-groups` | 不自动移除脚本生成但本轮计划中不存在的 `angrymesh.*` 组 |
| `--addressables-validation-output <path>` | 指定构建校验报告输出路径 |
| `--addressables-max-bundle-mb <MB>` | 设置单个 Bundle 大小上限，默认 64 MB |
| `--addressables-max-bundle-bytes <bytes>` | 用字节设置单个 Bundle 大小上限，优先级高于 MB 参数 |
| `--addressables-max-bundle-dependencies <count>` | 设置单个 Bundle 直接依赖数量上限，默认 16 |
| `--addressables-fail-on-validation-warning` | 将校验 warning 也视为构建失败 |

默认计划输出：

```text
.workspace/artifacts/addressables/<BuildTarget>/angrymesh/addressables_build_plan.json
```

默认校验报告输出：

```text
.workspace/artifacts/addressables/<BuildTarget>/angrymesh/addressables_build_validation.json
```

## Build Plan

`addressables_build_plan.json` 是当前管线的中间计划产物，记录：

- build target
- build root
- group 名称
- group roots
- asset 列表
- 估算源文件大小
- 依赖数量
- 跨 group 共享依赖总数
- 各 group 的 `sharedDependencies` 摘要
- warnings / errors

这一步用于把“收集资源、规则检查、依赖分析、生成计划”和真正的 Addressables build 分开，避免一边扫描一边直接打包。

## 构建结果校验

正式执行 `AddressableAssetSettings.BuildPlayerContent` 前，构建脚本会临时打开 Addressables BuildLayout JSON 报告生成：

```csharp
ProjectConfigData.GenerateBuildLayout = true
ProjectConfigData.BuildLayoutReportFileFormat = JSON
```

构建成功后，`ProjectAddressablesBuild` 会读取 `Library/com.unity.addressables/BuildReports/buildlayout_*.json` 中本轮最新报告，并生成 `addressables_build_validation.json`。如果出现 error，构建命令直接失败，不进入发布阶段。

当前校验项：

| 检查项 | 数据来源 | 失败策略 |
|---|---|---|
| 重复资源 | Addressables BuildLayout `DuplicatedAssets` | warning，可通过 `--addressables-fail-on-validation-warning` 提升为失败 |
| 循环依赖 | Bundle `Dependencies` 图 DFS | error |
| 过大的 Bundle | Bundle `FileSize` 与阈值比较 | error |
| 单个 Bundle 依赖过多 | Bundle 直接依赖数量与阈值比较 | error |
| Shader 丢失 | 只扫描 `Assets/GameResources`，用 `AssetDatabase.LoadAssetAtPath<Shader>` 检查 | error |
| 材质紫块风险 | 只扫描 `Assets/GameResources`，检查 Material 无 Shader 或 `Hidden/InternalErrorShader` | error |
| 无用资源进入包体 | 进包资源是否超出 plan 及其依赖闭包 | error |
| 包名 Hash 缺失 | `angrymesh.*` Bundle 名是否包含 AppendHash 风格 hash 段 | error |
| 包 Hash 变化 | 与上一份 `addressables_build_validation.json` 的 Bundle hash 对比 | warning，开启 `--addressables-fail-on-validation-warning` 后变为失败 |

dry run 不会有真实 Bundle，因此只输出 plan 级校验报告，主要覆盖计划重复 entry、plan error/warning，以及 `Assets/GameResources` 下的 Shader / Material 风险预检。

## 运行时加载

场景占位组件为：

```text
AngryMeshAddressablePrefabInstance
```

它序列化 Addressables address，当前默认使用 Unity asset path 作为 address：

```text
Assets/GameAssets/Prefabs/.../P_Grass_01_Summer_01.prefab
```

运行时通过：

```csharp
Addressables.LoadAssetAsync<GameObject>(address)
```

加载 prefab，并按占位物体 transform 实例化。

为避免旧 bundled 场景中已有占位组件变成 Missing Script，新的 Addressables loader 沿用了旧 `AngryMeshAssetBundlePrefabInstance` 脚本 GUID；原有序列化字段 `_assetPath` 会继续作为 Addressables address 使用，`_bundleName` 仅作为迁移残留字段保留。

## 场景转换

新的转换入口：

```text
Tools/ANGRY MESH/Addressables/Convert Enabled Scene Prefabs To Addressable Loaders
```

命令行 dry run：

```powershell
.\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode -quit `
  -executeMethod AngryMeshAddressablesReferenceConverter.ConvertEnabledScenePrefabsFromCommandLine `
  --addressables-convert-dry-run `
  --addressables-convert-enabled-scenes `
  -logFile Logs\angrymesh-addressables-convert-dry-run.log
```

命令行转换到场景副本并更新 Build Settings：

```powershell
.\tools\Invoke-Unity.ps1 -ProjectPath . -batchmode -quit `
  -executeMethod AngryMeshAddressablesReferenceConverter.ConvertEnabledScenePrefabsFromCommandLine `
  --addressables-convert-enabled-scenes `
  --addressables-convert-copy-to "Assets\Scenes\Scenes 1\Scene_MeadowEnvironment_01_Summer_Addressables.unity" `
  --addressables-convert-update-build-settings `
  -logFile Logs\angrymesh-addressables-convert.log
```

## 已移除内容

以下自研 AssetBundle 代码已经退出主线：

- `ProjectAssetBundleBuild`
- `AngryMeshAssetBundleReferenceConverter`
- `AngryMeshAssetBundleManager`
- `AngryMeshAssetBundlePrefabInstance`
- `project_asset_bundle_manifest.json`
- `Assets/StreamingAssets/AssetBundles/<BuildTarget>/angrymesh` 作为默认运行时根目录的约定

后续版本、远程目录、CDN 上传和热更发布应基于 Addressables profile、catalog 和 content update 流程继续扩展。
