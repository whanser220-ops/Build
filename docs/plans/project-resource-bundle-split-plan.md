# ANGRY MESH Addressables 构建方案

- 状态：迁移中
- 同步级别：弱同步
- 当前实现锚点：
  - `Assets/Project/Tools/Build/Editor/ProjectAddressablesBuild.cs`
  - `Assets/Project/Tools/Build/Editor/AngryMeshAddressablesReferenceConverter.cs`
  - `Assets/Project/Tools/Build/Runtime/AngryMeshAddressablePrefabInstance.cs`
- 最后实现核对：2026-06-09

## 当前方向

项目资源分包不再维护自研 `BuildPipeline.BuildAssetBundles` 管线、`project_asset_bundle_manifest.json` 反查表和 `AngryMeshAssetBundleManager` 运行时加载器。

新的主线转向 Unity Addressables：

1. 由 `ProjectAddressablesBuild` 收集 `Assets/GameAssets` 构建区资源并生成 `addressables_build_plan.json`。
2. 根据计划创建或刷新 Addressables group。
3. 使用 Addressables 的 packed content build 执行资源构建。
4. 运行时占位组件通过 Addressables address 异步加载 prefab。

当前构建主线改为顶层构建区和源产区：

```text
Assets/GameAssets
Assets/GameResources
```

其中 `Assets/GameAssets` 是运行时资产构建白名单，打包脚本默认只扫描该目录；`Assets/GameResources` 是源素材产区，只参与资源质量检查，只有显式传入 `--addressables-include-source-assets` 时才会额外生成源素材 Addressables 组。

`Assets/GameAssets` 下的包目录直接映射为业务 AssetBundle / Addressables group 颗粒度。当前规则会把叶子目录，或包含直接资源文件的中间目录，作为一个业务 group：

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
│   ├── Monster/
│   ├── Meadow Environment/
│   └── Meadow Terrain Details/
└── UIModules/
    ├── UILogin/
    └── UIMain/
```

旧 `Assets/ANGRY MESH` 插件目录不再作为构建白名单；构建相关运行时资源已经迁入 `Assets/GameAssets`。

当前已迁移的 ANGRY MESH 运行时资源：

```text
Assets/GameAssets/Common/ASP Global Settings
Assets/GameAssets/Common/Functions
Assets/GameAssets/Common/Shaders
Assets/GameAssets/Configs/Post Processing/Meadow Environment
Assets/GameAssets/Prefabs/Meadow Environment
Assets/GameAssets/Prefabs/Meadow Terrain Details
Assets/GameAssets/Scenes/Meadow Environment
```

当前已迁移的 ANGRY MESH 源素材：

```text
Assets/GameResources/Stylized Pack - Common/Sources
Assets/GameResources/Stylized Pack - Meadow Environment/Sources
```

## 默认 Addressables Group

当前默认只纳入 `Assets/GameAssets` 下的运行时产成品资源：

| Group | 当前输入范围 |
|---|---|
| `angrymesh.gameassets.common.asp.global.settings` | `Assets/GameAssets/Common/ASP Global Settings` |
| `angrymesh.gameassets.common.functions` | `Assets/GameAssets/Common/Functions` |
| `angrymesh.gameassets.common.shaders` | `Assets/GameAssets/Common/Shaders` |
| `angrymesh.gameassets.common.fonts` | `Assets/GameAssets/Common/Fonts` |
| `angrymesh.gameassets.scenes.meadow.environment` | `Assets/GameAssets/Scenes/Meadow Environment` |
| `angrymesh.gameassets.configs.post.processing.meadow.environment.urp` | `Assets/GameAssets/Configs/Post Processing/Meadow Environment/URP` |
| `angrymesh.gameassets.textures` | `Assets/GameAssets/Textures` |
| `angrymesh.gameassets.prefabs.hero` | `Assets/GameAssets/Prefabs/Hero` |
| `angrymesh.gameassets.prefabs.monster` | `Assets/GameAssets/Prefabs/Monster` |
| `angrymesh.gameassets.prefabs.meadow.environment.*` | `Assets/GameAssets/Prefabs/Meadow Environment/...` |
| `angrymesh.gameassets.prefabs.meadow.terrain.details.*` | `Assets/GameAssets/Prefabs/Meadow Terrain Details/...` |
| `angrymesh.gameassets.uimodules.uilogin` | `Assets/GameAssets/UIModules/UILogin` |
| `angrymesh.gameassets.uimodules.uimain` | `Assets/GameAssets/UIModules/UIMain` |

实际 group 由 `Assets/GameAssets` 下的包目录自动生成，命名格式为 `angrymesh.gameassets.<相对目录路径>`，路径分隔符会转换为 `.`。

只有显式传入 `--addressables-include-source-assets` 时，才会额外从 `Assets/GameResources` 生成 `angrymesh.gameresources.*` 组。

## 自动公共依赖拆包

`ProjectAddressablesBuild` 会在业务 group 收集完成后执行一次共享依赖拆分：

1. 对每个业务 group 内的资源调用 `AssetDatabase.GetDependencies(asset, true)`。
2. 统计每个依赖被哪些业务 group 引用。
3. 当某个依赖被两个或以上业务 group 引用，并且它可以安全成为 Addressables entry 时，自动提升到 `angrymesh.shared.*` 公共组。
4. 如果共享依赖已经被某个业务 group 收集为普通 asset，会先从原业务 group 移出，再写入公共依赖组。
5. 公共依赖组会和业务组一起写入 `addressables_build_plan.json`，随后由 Addressables settings 创建或刷新。

当前自动公共组按主资源类型和扩展名分类：

| Group | 典型内容 |
|---|---|
| `angrymesh.shared.shaders` | Shader、Shader Variant Collection |
| `angrymesh.shared.materials` | Material |
| `angrymesh.shared.textures` | PNG、TGA、TIF、PSD、EXR、HDR 等贴图 |
| `angrymesh.shared.models` | FBX、OBJ、Blend、DAE 等模型资源 |
| `angrymesh.shared.animations` | AnimationClip、Animator Controller |
| `angrymesh.shared.audio` | 音频资源 |
| `angrymesh.shared.assets` | 其他可 Addressable 化的共享 `.asset` 等资源 |

自动提升规则保持保守：

- 只处理 `Assets/` 下的依赖，不处理 `Packages/` 依赖。
- 不提升目录、脚本、未知 `DefaultAsset`、Prefab、Scene、Editor 目录资源、Resources 目录资源和被忽略扩展名。
- 不直接修改源 prefab / material 引用；公共依赖通过 Addressables entry 所在 group 参与 packed content build，由 Addressables/SBP 建立 bundle 依赖关系。

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
- 自动公共依赖数量
- group 角色：`primary` 或 `shared-dependency`
- 自动公共依赖组的 owner group 列表
- 拆分后仍残留的跨 group 共享依赖摘要
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
| 重复资源 | Addressables BuildLayout `DuplicatedAssets` | error |
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
