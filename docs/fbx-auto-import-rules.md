# 资产导入规则与 FBX DCC JSON 装配

本文档说明当前资产规范工具的两层职责：

- 通用资产导入规则：按“资产筛选器 + 属性修改列表”自动设置 importer。
- FBX DCC JSON 装配：只处理模型导入后的 Prefab、LODGroup、Collider、材质绑定。

入口：

- 导入器脚本：`Assets/Editor/AssetImport/ProjectFbxAutoImporter.cs`
- 通用规则资产：`Assets/Editor/AssetImport/ProjectAssetImportRuleSet.asset`
- 规则资产类型：`Assets/Editor/AssetImport/ProjectAssetImportRuleSet.cs`
- 规则编辑器 UI：`Tools/Asset Import/Asset Processor`
- 说明文档菜单：`Tools/Asset Import/FBX Auto Import Rules`

## 编辑器 UI

在 Unity 菜单打开：

```text
Tools/Asset Import/Asset Processor
```

也可以在 Project 面板选中 `ProjectAssetImportRuleSet.asset`，Inspector 顶部点击 `Open Asset Processor`。

窗口直接显示 `Rule Items`。每条规则展开后先编辑 `Directory`、`Package Name`、`Asset Class`，再用类似 Unity 导入设置的页签勾选并配置需要写入的 importer 属性。内置页签覆盖 Unity `ModelImporter` / `TextureImporter` 的基础可写导入设置；复杂对象或数组类设置可在 `Custom` 页签里手动填写 property path。

## 通用规则结构

每条规则由两部分组成：

- `filter`：资产筛选器。
- `propertyItems`：属性修改列表。

规则资产中的 `applyAllMatchingRules = true` 表示同一资产可以命中多条规则，并按列表顺序依次应用。建议把通用默认规则放前面，把更具体的项目规则放后面，让后者覆盖同名属性。

## 资产筛选器

筛选器包含：

| 字段 | 说明 |
|---|---|
| `directoryMatch` + `directoryPattern` | 按资产目录筛选 |
| `packageNameMatch` + `packageNamePattern` | 按资产名筛选，不含扩展名 |
| `assetClass` | 按资产类型筛选，例如 `Model`、`Texture2D` |

`Directory` 和 `Package Name` 支持：

- `Any`
- `Contains`
- `StartsWith`
- `EndsWith`
- `Equals`
- `Regex`
- `Glob`

示例：筛选整个项目里以 `_N` 结尾的贴图：

```text
directoryMatch: Any
packageNameMatch: EndsWith
packageNamePattern: _N
assetClass: Texture2D
```

## 属性修改列表

每个属性项包含：

| 字段 | 说明 |
|---|---|
| `propertyPath` | 要修改的 importer 属性 |
| `valueKind` | `Bool`、`Int`、`Float`、`String`、`Enum` |
| `value` | 目标值 |

法线贴图示例：

```text
TextureImporter.textureType = NormalMap
TextureImporter.wrapModeU = Clamp
TextureImporter.wrapModeV = Clamp
```

FBX 模型示例：

```text
ModelImporter.animationType = Human
ModelImporter.avatarSetup = CreateFromThisModel
ModelImporter.importAnimation = true
ModelImporter.importBlendShapes = true
```

当前代码对常用 `ModelImporter` 和 `TextureImporter` 属性有显式映射；找不到显式映射时，会尝试按 Unity serialized property path 写入。找不到属性或类型不支持时会记录 warning 并跳过该属性。

## 默认规则

`ProjectAssetImportRuleSet.asset` 当前内置：

- `Common Model Defaults`：模型通用默认设置。
- `Characters`：角色模型路径规则。
- `Props`：道具模型路径规则。
- `Meadow Source Meshes`：Meadow 源模型保留层级。
- `LOD Filename`：文件名包含 `LOD` 时保留层级。
- `At-Sign Animations`：文件名包含 `@` 的动画 FBX。
- `Character At-Sign Animations`：角色路径下文件名包含 `@` 的动画 FBX。
- `Character Source Animations`：角色 `SourceAnimations` 路径下的动画 FBX。
- `Normal Textures`：以 `_N` 结尾的 `Texture2D` 设置为 NormalMap，并将 U/V wrap 设为 Clamp。

## FBX DCC JSON 装配

需要导入后装配的 FBX 可以在同目录放同名 JSON：

```text
Assets/.../zzz.fbx
Assets/.../zzz.fbx.json
```

缺少 sidecar JSON 时，导入器只记录 warning 并跳过装配；通用导入规则仍会执行。

JSON v1 只描述装配，不描述导入设置。旧 JSON 中如果仍带 `importSettings` 字段，Unity 会忽略它。

```json
{
  "schemaVersion": 1,
  "sourceFbx": "zzz.fbx",
  "assembly": {
    "prefab": {
      "enabled": true,
      "outputPath": "Assets/GameAssets/Worlds/Meadow/Shared/Prefabs/P_zzz.prefab",
      "overwrite": false
    },
    "lodGroup": {
      "enabled": true,
      "rootPath": "",
      "levels": [
        { "index": 0, "nodePath": "zz_LOD0_LOD0", "screenRelativeHeight": 0.6 },
        { "index": 1, "nodePath": "zz_LOD0_LOD1", "screenRelativeHeight": 0.35 },
        { "index": 2, "nodePath": "zz_LOD0_LOD2", "screenRelativeHeight": 0.18 }
      ]
    },
    "colliders": [
      { "nodePath": "UCX_body", "type": "Mesh", "convex": true, "disableRenderer": true }
    ],
    "materials": [
      { "slotName": "M_Wood", "materialPath": "Assets/GameAssets/Materials/M_Wood.mat" }
    ]
  }
}
```

## 装配字段

`schemaVersion` 当前必须为 `1`。

`sourceFbx` 必须等于当前 FBX 文件名，例如 `zzz.fbx`。

`assembly.prefab` 控制 Prefab 输出。`enabled = true` 时，`outputPath` 必须是 `Assets/.../*.prefab`。`overwrite = false` 时，如果目标 Prefab 已存在，则跳过生成。

`assembly.lodGroup` 控制单个主 `LODGroup`。`rootPath` 是放置 `LODGroup` 的节点路径，空字符串表示导入根节点。`levels` 会按 `index` 排序，并使用每个 `nodePath` 下的 Renderer 生成 LOD。

`assembly.colliders` 控制碰撞体生成。`type` 支持 `Box`、`Mesh`、`Sphere`、`Capsule`。`disableRenderer = true` 时，会禁用该节点及子节点的 Renderer。

`assembly.materials` 控制材质槽绑定。`slotName` 匹配 FBX 材质槽名，`materialPath` 指向项目内已有 `.mat` 资源。

`nodePath` 使用 Unity 导入后的 Transform 层级路径。节点名中包含 `LOD0`、`UCX_` 等字样不会自动触发装配，必须由 JSON 明确引用。
