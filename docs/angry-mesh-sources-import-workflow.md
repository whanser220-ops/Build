# 模块 Art 资源导入检查工作流

本文记录 `Assets/Game/**/Art` 下源素材的导入/检查工作流。旧 `Assets/GameResources` 已迁移并移除，不再作为新增源素材入口。

当前已纳入：

```text
Assets/Game/Shared/StylizedPackCommon/Art/Sources
Assets/Game/Worlds/Meadow/Art/Sources
```

## 目标

- 用同一套代码支持同类型资源的多套导入配置。
- 规则通过路径、文件名、glob 和正则表达式匹配资源。
- 导入配置持久化在 JSON profile 中，便于版本管理和代码审查。
- 每个资源的已应用规则、源文件大小、修改时间和设置哈希持久化在 state JSON 中。
- 当模块 `Art` 下资源被修改或重新导入时，Editor Postprocessor 会按 profile 自动校正导入设置并更新 state。

## 文件入口

- 工具代码：`Assets/Project/Tools/AngryMeshSourcesImport/Editor`
- Profile：`Assets/Project/Tools/AngryMeshSourcesImport/Profiles/angry_mesh_sources_import_profile.json`
- State：`Assets/Project/Tools/AngryMeshSourcesImport/Profiles/angry_mesh_sources_import_state.json`
- Editor 窗口：`Tools/ANGRY MESH/Sources Import Workflow`

## 规则匹配

规则按 `order` 从小到大匹配，第一条命中的规则生效。每条规则可同时限制：

- `assetKind`：`Texture`、`Model`、`Material`、`TerrainLayer`、`Generic`、`Any`
- `pathGlobs`：相对各模块 `Art` 根的路径通配符，例如 `*/Sources/Textures/**`、`*/Sources/Meshes/Grass/**`
- `fileNameGlobs`：文件名通配符，例如 `*_N.tif`、`*.fbx`
- `pathRegex` / `fileNameRegex`：可选正则表达式
- `excludeGlobs`：排除路径

这种结构允许同样是 `.tif`，根据文件名区分 `_A`、`_N`、`_SMA`、`_H` 等不同导入设置；也允许同样是 `.fbx`，根据 `Meshes/Grass/`、`Meshes/Rocks/` 等路径走不同模型配置。

## 当前默认规则

- `*/Sources/Textures/Skybox/*.HDR`：HDR skybox，Clamp，4096。
- `*/Sources/Textures/**/*_N.tif`：NormalMap，线性空间。
- `*/Sources/Textures/**/*_SMA.tif`、`*_Mask.tif`、`*_H.tif`、`*_O.tif`：线性空间 mask。
- 其他 `*/Sources/Textures/**/*.tif`：默认颜色贴图，sRGB。
- `*/Sources/Meshes/Grass|Flowers|Leaves|Plants|VFX/**/*.fbx`：植被卡片类模型，低 mesh compression，不导入材质/灯光/相机/动画。
- 其他 `*/Sources/Meshes/**/*.fbx`：静态模型，不导入材质/灯光/相机/动画。
- `*/Sources/Materials/**/*.mat`：启用 GPU instancing。
- `*/Sources/Terrain Layers/**/*.terrainlayer` 与 `*/Sources/Terrain Data/**/*.asset`：只纳入匹配与 state 跟踪。

## 批处理入口

可在 Unity batchmode 中调用：

```powershell
Unity.exe -batchmode -projectPath . -executeMethod Project.Tools.AngryMeshSourcesImport.AngryMeshSourcesImportCli.Check -quit -logFile Logs/angry-mesh-sources-check.log
Unity.exe -batchmode -projectPath . -executeMethod Project.Tools.AngryMeshSourcesImport.AngryMeshSourcesImportCli.Apply -quit -logFile Logs/angry-mesh-sources-apply.log
Unity.exe -batchmode -projectPath . -executeMethod Project.Tools.AngryMeshSourcesImport.AngryMeshSourcesImportCli.ForceReimportAll -quit -logFile Logs/angry-mesh-sources-force-reimport.log
```
