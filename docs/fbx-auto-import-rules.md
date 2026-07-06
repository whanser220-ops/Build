# 资产导入规则与 FBX DCC JSON 装配

本文档说明当前资产处理器的两层职责：

- 导入设置：由项目级规则资产控制，在资源导入时写入 `ModelImporter` / `TextureImporter`。
- 导入后装配：由 DCC 导出的 sidecar JSON 控制，只负责 FBX 导入后的 Prefab、LODGroup、Collider、材质绑定。

旧的“从 FBX 节点名、路径、材质槽自动猜测装配”的逻辑已经停用。LOD、Collider、Prefab 输出等装配行为必须由 JSON 明确描述。

## 在哪看规则

Unity 菜单：

```text
Tools/Asset Import/Asset Processor
```

也可以在 Project 面板选中：

```text
Assets/Editor/AssetImport/ProjectAssetImportRuleSet.asset
```

然后在 Inspector 点击 `Open Asset Processor`。

窗口打开后直接显示 `Rule Items`。每条规则由两部分组成：

- 资产筛选器：`Directory`、`Package Name`、`Asset Class`。
- 导入设置：按 Unity 导入器习惯分成页签，字段左侧勾选表示这条规则负责写入该字段。

每条规则标题右侧有三颗执行按钮：

| 按钮 | 作用 |
|---|---|
| `列` | 列出符合当前规则筛选条件的资产 |
| `查` | 检查匹配资产中哪些导入属性不符合当前规则，并列出将被修改的字段 |
| `改` | 应用当前规则，重导入被修改的资产，并生成执行报告 |

这些按钮用于在配置面板里主动执行规则；资源导入时仍会自动应用所有命中规则合并后的 Effective Import Settings。

顶部的 Global Settings、Regex Tester、Export、Import 已移除，避免把规则编辑入口藏得太深。

## 规则匹配

筛选器字段：

| 字段 | 作用 |
|---|---|
| `Directory` | 按资源目录匹配，统一使用 `/` 路径分隔 |
| `Package Name` | 按资源名匹配，不含扩展名 |
| `Asset Class` | 按资产类型匹配，如 `Model`、`Texture2D` |

`Directory` 和 `Package Name` 支持：

- `Any`
- `Contains`
- `StartsWith`
- `EndsWith`
- `Equals`
- `Regex`
- `Glob`

例如筛选全项目所有以 `_N` 结尾的贴图：

```text
Directory: Any
Package Name: EndsWith _N
Asset Class: Texture2D
```

## 导入设置面板

规则的导入设置不是裸 `propertyItems` 列表，而是类似 Unity Import Settings 的面板。

`Model` 资产页签：

- `Model`
- `Rig`
- `Animation`
- `Materials`
- `Custom`

`Texture2D` 资产页签：

- `Texture`
- `Advanced`
- `Sprite`
- `Swizzle`
- `Custom`

每个字段都有：

- 左侧 override 勾选：勾选后该字段进入最终导入设置。
- 右侧目标值：写入 Unity importer 的值。

没有勾选的字段不会参与规则合并，也不会写入 importer。`Custom` 页签保留 property path 写入能力，用于迁移旧字段或处理暂未做成强类型 UI 的字段。

## Effective Import Settings

同一个资源可以命中多条规则。当前行为是：

1. 找到所有启用且匹配的规则。
2. 按规则列表顺序逐条合并字段。
3. 同一个字段被多条规则勾选时，后面的规则覆盖前面的规则。
4. 最终只把合并后的 Effective Import Settings 写入 importer。

窗口底部的 `Effective Import Settings Preview` 可输入资产路径和资产类型，查看：

- 命中的规则列表。
- 最终字段值。
- 每个字段来自哪条规则。

## 默认规则

默认规则资产位于：

```text
Assets/Editor/AssetImport/ProjectAssetImportRuleSet.asset
```

当前默认规则覆盖：

- `Common Model Defaults`：模型通用默认设置。
- `Characters`：角色模型路径规则。
- `Props`：道具模型路径规则。
- `Meadow Source Meshes`：Meadow 源模型保留层级。
- `LOD Filename`：文件名包含 `LOD` 时保留层级。
- `At-Sign Animations`：文件名包含 `@` 的动画 FBX。
- `Character At-Sign Animations`：角色路径下文件名包含 `@` 的动画 FBX。
- `Character Source Animations`：角色 `SourceAnimations` 路径下的动画 FBX。
- `Normal Textures`：以 `_N` 结尾的 `Texture2D` 设置为 NormalMap，并将 U/V wrap 设为 Clamp。

旧资产中的 `propertyItems` 会自动迁移到强类型导入设置字段；无法识别的字段会进入 `Custom` 页签，避免丢失数据。

## FBX DCC JSON

FBX 旁边可以放同名 JSON：

```text
Assets/.../zzz.fbx
Assets/.../zzz.fbx.json
```

缺少 JSON 时，导入规则仍会执行；导入器只记录 warning 并跳过装配。

JSON v1 只描述装配，不描述导入设置。旧 JSON 如果仍带 `importSettings` 字段，Unity 会忽略它。

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

`nodePath` 使用 Unity 导入后的 Transform 层级路径。节点名里包含 `LOD0`、`UCX_` 等字样不会自动触发装配，必须由 JSON 明确引用。
