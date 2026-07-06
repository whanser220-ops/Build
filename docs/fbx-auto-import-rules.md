# FBX 自动导入规则与 DCC JSON 装配

本文档说明 `ProjectFbxAutoImporter` 的当前职责边界。入口：

- 导入器脚本：`Assets/Editor/AssetImport/ProjectFbxAutoImporter.cs`
- 规则资产类型：`Assets/Editor/AssetImport/ProjectFbxImportRuleSet.cs`
- 默认规则资产：`Assets/Editor/AssetImport/ProjectFbxImportRuleSet.asset`
- Unity 菜单：`Tools/Asset Import/FBX Auto Import Rules`

## 职责边界

`Pre-process` 负责模型导入设置。它不读取 DCC JSON，只根据项目规则资产命中路径或文件名，再写入 `ModelImporter`。

`Post-process` 负责导入后的资产装配。它读取 FBX 旁边的 `xxx.fbx.json`，只处理 Prefab、LODGroup、Collider 和材质槽绑定。

缺少 sidecar JSON 时，导入器会记录 warning，并跳过装配；导入设置仍按规则资产正常执行。

## 导入规则资产

默认规则资产是：

```text
Assets/Editor/AssetImport/ProjectFbxImportRuleSet.asset
```

规则按列表顺序匹配，命中第一条后停止。`glob` 大小写不敏感，路径统一使用 `/`。带 `/` 的 glob 匹配完整 asset path；不带 `/` 的 glob 匹配文件名。没有以 `Assets/`、`**/` 或 `*` 开头的路径 glob 会自动按“项目路径中任意位置”匹配。

规则项包含：

| 字段 | 用途 |
|---|---|
| `name` | 规则显示名 |
| `glob` | 路径或文件名通配 |
| `kind` | `StaticModel`、`CharacterModel`、`AnimationAsset` |
| `rig` | `None`、`Generic`、`Humanoid` |
| `preserveHierarchy` | 写入 `ModelImporter.preserveHierarchy` |
| `importAnimation` | 写入 `ModelImporter.importAnimation` |
| `importBlendShapes` | 写入 `ModelImporter.importBlendShapes` |
| `generateLightmapUv` | 写入 `ModelImporter.generateSecondaryUV` |
| `generateColliders` | 写入 `ModelImporter.addCollider` |
| `readWrite` | 写入 `ModelImporter.isReadable` |
| `meshCompression` | 写入 `ModelImporter.meshCompression` |
| `animationCompression` | 写入 `ModelImporter.animationCompression` |

当前默认规则覆盖：

- `Characters/**/SourceAnimations/**/*.fbx`
- `Characters/**/*@*.fbx`
- `*@*.fbx`
- `Characters/**/*.fbx`
- `Props/**/*.fbx`
- `Stylized Pack - Meadow Environment/Sources/Meshes/**/*.fbx`
- `*LOD*.fbx`
- fallback `*.fbx`

通用导入设置会额外关闭 Cameras/Lights，开启 Visibility 和 Sort Hierarchy By Name，并保留 `Collision`、`Collider` 自定义属性读取。

## Sidecar JSON

需要装配的 FBX 可以在同目录放同名 JSON：

```text
Assets/.../zzz.fbx
Assets/.../zzz.fbx.json
```

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

`sourceFbx` 必须等于当前 FBX 文件名，例如 `zzz.fbx`，用于防止 JSON 被复制到错误模型旁边。

`assembly.prefab` 控制 Prefab 输出。`enabled = true` 时，`outputPath` 必须是 `Assets/.../*.prefab`。`overwrite = false` 时，如果目标 Prefab 已存在，则跳过生成。

`assembly.lodGroup` 控制单个主 `LODGroup`。`rootPath` 是放置 `LODGroup` 的节点路径，空字符串表示导入根节点。`levels` 会按 `index` 排序，并使用每个 `nodePath` 下的 Renderer 生成 LOD。

`assembly.colliders` 控制碰撞体生成。`type` 支持 `Box`、`Mesh`、`Sphere`、`Capsule`。`disableRenderer = true` 时，会禁用该节点及子节点的 Renderer。

`assembly.materials` 控制材质槽绑定。`slotName` 匹配 FBX 材质槽名，`materialPath` 指向项目内已有 `.mat` 资源。

## 节点路径

`nodePath` 使用 Unity 导入后的 Transform 层级路径：

- 根节点使用空字符串 `""`。
- 子节点使用 `/` 分隔，例如 `Root/Body/UCX_body`。
- 如果路径第一段等于导入根节点名，导入器会自动跳过这一段，因此 `Root/Body` 和 `Body` 都可以从根节点下查找 `Body`。

节点名中包含 `LOD0`、`LOD1`、`UCX_`、`UBX_` 等字样不会自动触发装配；只有 JSON 明确引用这些节点路径时，导入器才会处理它们。

## 已废弃逻辑

以下逻辑不再作为装配依据：

- 扫描 FBX 内容判断 Skeleton、Skin Weights、Bind Pose、Animation Stack。
- 根据节点名自动生成 LODGroup 或碰撞体。
- 根据材质槽名在固定目录里搜索项目材质。
- Meadow 环境包 FBX 按路径自动生成 Prefab。

路径和命名仍可用于导入设置，但必须写在 `ProjectFbxImportRuleSet.asset` 中。
