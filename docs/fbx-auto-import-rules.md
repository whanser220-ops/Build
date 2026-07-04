# FBX 自动导入规则

本文档是 `ProjectFbxAutoImporter` 当前规则的可读索引。规则实现位于：

- `Assets/Editor/AssetImport/ProjectFbxAutoImporter.cs`
- Unity 菜单入口：`Tools/Asset Import/FBX Auto Import Rules`

修改 FBX 自动导入逻辑时，应同步更新本文档。

## 适用范围

- 仅处理扩展名为 `.fbx` 的资产。
- 规则在 Unity `AssetPostprocessor` 流程中自动执行。
- 预导入阶段写入 `ModelImporter` 设置。
- 导入后阶段根据节点命名补充 `LODGroup`、碰撞体和材质映射。

## FBX 信号扫描

导入前会扫描 FBX 文件前 16 MB 的文本/二进制片段，识别以下信号：

| 检测信号 | 识别方式 | 用途 |
|---|---|---|
| Skeleton / LimbNode | 文件中出现 `Skeleton` 或 `LimbNode` | 判断是否有骨骼层级 |
| Skin Weights / Deformer | 文件中出现 `Skin` 且出现 `Cluster` 或 `Deformer` | 判断是否为蒙皮模型 |
| Bind Pose | 文件中出现 `BindPose` 或 `PoseNode` | 判断是否有绑定姿态 |
| Animation Stack | 文件中出现 `AnimationStack` 或 `AnimStack` | 判断是否包含动画 |
| 多个 Animation Stack | Animation Stack 信号数量大于 1 | 优先判定为动画资产 |
| LOD 节点 | 文件中出现 `LOD0`、`LOD1` 或 `LOD2` | 判定为 LOD 模型 |
| 碰撞节点 | 文件中出现 `UCX_`、`UBX_`、`USP_` 或 `UCP_` | 判定为自带碰撞约定 |
| 自定义碰撞属性 | 文件中出现 `Collision` 或 `Collider` | 让 Unity 读取自定义属性 |

## 路径规则

| 路径或文件名 | 判定 |
|---|---|
| 路径包含 `/Characters/` | 角色资产路径，优先使用 Humanoid Rig |
| 路径包含 `/Props/` | 道具资产路径，默认生成碰撞和 Lightmap UV |
| 路径包含 `/SourceAnimations/` | 动画资产路径 |
| 路径包含 `/Animations/` | 动画资产路径 |
| 文件名包含 `@` | 动画资产路径 |

## 类型判定优先级

| 优先级 | 条件 | 导入类型 |
|---|---|---|
| 1 | 动画路径，或检测到多个 Animation Stack | `AnimationAsset` |
| 2 | `/Characters/` 路径，或检测到 Skeleton / Skin / Bind Pose | `CharacterModel` |
| 3 | 检测到 LOD 节点 | `LodModel` |
| 4 | 以上都不命中 | `StaticModel` |

## 通用导入设置

所有命中的 FBX 都会应用：

| 设置 | 值 |
|---|---|
| Import Cameras | 关闭 |
| Import Lights | 关闭 |
| Import Visibility | 开启 |
| Sort Hierarchy By Name | 开启 |
| Extra User Properties | 添加 `Collision`、`Collider` |
| Preserve Hierarchy | 仅在检测到 LOD 或碰撞节点时开启 |

## 角色模型规则

适用于 `CharacterModel`：

| 设置 | 值 |
|---|---|
| Rig | `/Characters/` 路径使用 Humanoid，否则 Generic |
| Avatar | Create From This Model |
| Import Animation | 开启 |
| Import BlendShapes | 开启 |
| Generate Colliders | 关闭 |
| Read/Write | 关闭 |
| Mesh Compression | Off |

## 动画资产规则

适用于 `AnimationAsset`：

| 设置 | 值 |
|---|---|
| Rig | `/Characters/` 路径使用 Humanoid，否则 Generic |
| Avatar | Create From This Model |
| Import Animation | 开启 |
| Import BlendShapes | 关闭 |
| Generate Colliders | 关闭 |
| Read/Write | 关闭 |
| Animation Compression | Optimal |

Clip 设置：

| 设置 | 值 |
|---|---|
| Clip 名称 | 若为空，使用 FBX 文件名；多 Clip 时追加序号 |
| Loop Time / Loop Pose | 默认开启 |
| Root Rotation / Height / XZ | 锁定 |
| Keep Original Orientation / Position | 关闭 |
| Height From Feet | 开启 |

以下路径默认不循环：

- `/OneShot/`
- `/OneShots/`
- `/Cinematic/`

## 静态模型规则

适用于 `StaticModel`：

| 设置 | 值 |
|---|---|
| Rig | None |
| Import Animation | 关闭 |
| Import BlendShapes | 关闭 |
| Generate Colliders | `/Props/`、碰撞节点或碰撞自定义属性命中时开启 |
| Generate Lightmap UV | `/Props/` 路径开启 |
| Read/Write | 关闭 |
| Mesh Compression | Low |

## LOD 规则

节点名匹配以下模式时识别为 LOD：

- `LOD0`
- `LOD1`
- `LOD2`
- 也支持以下分隔形式：`SM_Tree_LOD0`、`SM_Tree-LOD1`、`SM_Tree.LOD2`

导入后，如果至少找到 2 个 LOD 渲染组，会在根对象上生成或更新 `LODGroup`。

默认屏幕高度：

| LOD | Screen Relative Transition Height |
|---|---|
| LOD0 | 0.6 |
| LOD1 | 0.35 |
| LOD2 | 0.18 |
| LOD3 | 0.08 |
| LOD4+ | 在 0.08 基础上继续按 0.5 衰减，最低 0.01 |

## 碰撞规则

导入后会识别 Unreal 风格碰撞节点前缀：

| 节点前缀 | Unity 组件 |
|---|---|
| `UBX_` | `BoxCollider` |
| `UCX_` | `MeshCollider`，默认 `convex = true` |
| `USP_` | `SphereCollider` |
| `UCP_` | `CapsuleCollider` |

碰撞辅助节点上的 Renderer 会自动禁用。

自定义属性也可触发碰撞生成：

| 属性名 | 支持值 |
|---|---|
| `Collision` | `Box`、`Mesh`、`ConvexMesh`、`Sphere`、`Capsule`、`UBX`、`UCX`、`USP`、`UCP` |
| `Collider` | 同上 |

## 材质槽规则

当 Unity 为 FBX 分配材质时，会尝试按材质槽名查找项目内已有材质：

| 查找范围 |
|---|
| `Assets/GameAssets` |
| `Assets/GameResources` |
| `Assets/ANGRY MESH` |
| `Assets/ThirdParty` |

匹配规则：

- 先按材质槽名精确匹配材质资产名。
- 如果材质槽名以 `M_` 开头，再尝试去掉 `M_` 后匹配。
- 找不到时保留 Unity 原始材质。

## 当前特例

`Assets/Editor/Characters/Qianxia/QianxiaFbxImportConfigurator.cs` 仍保留千夏角色的专用导入设置。该特例与通用 FBX 自动导入器并存；如果两边规则都命中，应优先检查千夏专用脚本和本文档是否需要同步。
