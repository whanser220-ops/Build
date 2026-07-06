# FBX DCC JSON 自动导入与装配规则

本文档是 `ProjectFbxAutoImporter` 当前规则源说明。查看规则的入口有两个：

- 实现脚本：`Assets/Editor/AssetImport/ProjectFbxAutoImporter.cs`
- Unity 菜单：`Tools/Asset Import/FBX Auto Import Rules`

当前版本已经废弃旧的 FBX 内容、路径、节点名推断逻辑。Unity 不再自行判断 Skeleton、LOD、碰撞体、资源类型或输出 Prefab 路径；这些信息必须由 DCC 导出的 sidecar JSON 明确提供。

## 规则定位

这套脚本对应“资源导入配置工具 + 导入后装配工具”：

- `OnPreprocessModel()`：导入前读取 JSON，并写入 `ModelImporter` 设置。
- `OnPostprocessModel()`：导入后读取 JSON，并在导入产物上生成或修正 `LODGroup`、Collider、材质绑定。
- `OnPostprocessAllAssets()`：导入完成后读取 JSON，并按配置生成 Prefab。
- `OnAssignMaterialModel()`：Unity 分配 FBX 材质槽时，按 JSON 中的材质映射绑定项目材质。

合规性检查脚本是另一类工具，适合放在 DCC 导出、pre-commit、post-commit 或周期性检查链路中。本导入器只负责“按已声明的 JSON 执行”，并在 JSON 缺失或非法时报错后跳过自动配置和装配。

## Sidecar 文件

每个需要自动导入或装配的 FBX 必须有同名 JSON：

```text
Assets/.../zzz.fbx
Assets/.../zzz.fbx.json
```

缺失 `xxx.fbx.json`、JSON 解析失败或字段校验失败时，导入器会输出错误，并跳过自动导入配置、LOD、Collider、材质绑定和 Prefab 生成。不会回退到旧推断规则。

## JSON v1 示例

```json
{
  "schemaVersion": 1,
  "sourceFbx": "zzz.fbx",
  "importSettings": {
    "kind": "StaticModel",
    "rig": "None",
    "preserveHierarchy": true,
    "importAnimation": false,
    "importBlendShapes": false,
    "generateLightmapUv": false,
    "generateColliders": false,
    "readWrite": false,
    "meshCompression": "Low",
    "animationCompression": "Optimal"
  },
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

## 枚举值

`kind` 只能使用：

- `StaticModel`
- `CharacterModel`
- `AnimationAsset`

`rig` 只能使用：

- `None`
- `Generic`
- `Humanoid`

`meshCompression` 只能使用：

- `Off`
- `Low`
- `Medium`
- `High`

`animationCompression` 只能使用：

- `Off`
- `KeyframeReduction`
- `Optimal`

`collider.type` 只能使用：

- `Box`
- `Mesh`
- `Sphere`
- `Capsule`

## 字段说明

`schemaVersion` 当前必须为 `1`。

`sourceFbx` 必须等于当前 FBX 文件名，例如 `zzz.fbx`。这是为了防止 JSON 被复制到错误的 FBX 旁边。

`importSettings` 直接映射到 `ModelImporter`：

| JSON 字段 | Unity 设置 |
|---|---|
| `rig: None` | `animationType = None` |
| `rig: Generic` | `animationType = Generic`，`avatarSetup = CreateFromThisModel` |
| `rig: Humanoid` | `animationType = Human`，`avatarSetup = CreateFromThisModel` |
| `importAnimation` | `ModelImporter.importAnimation` |
| `importBlendShapes` | `ModelImporter.importBlendShapes` |
| `preserveHierarchy` | `ModelImporter.preserveHierarchy` |
| `generateLightmapUv` | `ModelImporter.generateSecondaryUV` |
| `generateColliders` | `ModelImporter.addCollider` |
| `readWrite` | `ModelImporter.isReadable` |
| `meshCompression` | `ModelImporter.meshCompression` |
| `animationCompression` | `ModelImporter.animationCompression` |

`assembly.prefab` 控制 Prefab 输出。`enabled = true` 时，`outputPath` 必须是 `Assets/.../*.prefab`。`overwrite = false` 时，如果目标 Prefab 已存在，则跳过生成，避免覆盖人工编辑内容。

`assembly.lodGroup` 控制单个主 `LODGroup`。`rootPath` 是放置 `LODGroup` 的节点路径，空字符串表示导入根节点。`levels` 必须至少包含一个元素，导入器会按 `index` 排序，并用每个 `nodePath` 下的 Renderer 生成对应 LOD。

`assembly.colliders` 控制碰撞体生成。`nodePath` 必须指向 Unity 导入后的节点路径。`disableRenderer = true` 时，会禁用该节点及子节点的 Renderer。

`assembly.materials` 控制材质槽绑定。`slotName` 必须和 FBX 材质槽名一致，`materialPath` 必须指向项目内已有的 `.mat` 资源。找不到目标材质时会报错并保留原材质。

## 节点路径规则

`nodePath` 使用 Unity 导入后的 Transform 层级路径：

- 根节点使用空字符串 `""`。
- 子节点使用 `/` 分隔，例如 `Root/Body/UCX_body`。
- 如果路径第一段等于导入根节点名，导入器会自动跳过这一段，因此 `Root/Body` 和 `Body` 都可以从根节点下查找 `Body`。

节点名中包含 `LOD0`、`LOD1`、`UCX_`、`UBX_` 等字样不会自动触发任何规则；只有 JSON 明确引用这些节点路径时，导入器才会处理它们。

## 旧规则废弃

以下旧逻辑已经停用：

- 扫描 FBX 文件内容判断 Skeleton、Skin Weights、Bind Pose、Animation Stack。
- 根据 `/Characters/`、`/Props/`、`/SourceAnimations/`、`/Animations/` 或文件名中的 `@` 推断资源类型。
- 根据节点名自动推断 LOD 或 Unreal 风格碰撞体。
- 根据材质槽名在固定目录里搜索项目材质。
- Meadow 环境包 FBX 按路径自动生成 Prefab。

后续如果需要新增规则，应优先扩展 JSON schema，而不是恢复 Unity 侧推断。
