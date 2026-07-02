# Assets 目录组织规范

本文记录当前仓库�?`Assets/` 目录落位规则。目标是让路径可以直接驱动资源检查、导入规则和性能分析，而不是依赖口头约定�?
## 根级目录

- `Assets/3rd/`：第三方插件�?SDK 隔离区�?- `Assets/Editor/`：项目级构建脚本、管线工具与编辑器工具入口�?- `Assets/GameResources/`：源产区，美术原始素材与导入检查白名单�?- `Assets/GameAssets/`：构建区，运行时资产�?Addressables 构建白名单；包目录直接映射为业务分包颗粒度�?- `Assets/Scenes/`：场景区，启动场景、美术用场景、自动化或临时生成场景�?- `Assets/Scripts/`：代码层，C# 源码主入口�?- `Assets/Settings/`：Unity / URP / 项目级配置�?- `Assets/Tests/`：构建链�?EditMode 测试；CI 只运�?`Project.BuildPipeline.EditMode.Tests`�?- `Assets/Samples/`：Unity Package Manager 导入�?Samples，保�?Unity 默认约定�?- `Assets/Resources/`、`Assets/StreamingAssets/`：仅在确实需�?Unity 特殊目录语义时使用�?
当前暂留的根级例外：

- `Assets/Project/`：历史项目代码与工具目录，迁移到 `Assets/Editor` / `Assets/Scripts` / `Assets/GameAssets` 前继续保留�?- `Assets/ThirdParty/`：历史第三方目录，后续第三方插件�?SDK 优先迁入 `Assets/3rd/`�?- `Assets/ANGRY MESH/`：旧插件残留区；当前仅保留脚本、模板包和说明文件。运行时构建资源已迁�?`Assets/GameAssets`，源素材已迁�?`Assets/GameResources`。RenderDoc/性能分析工具链中仍提到此路径的地方需要后续专项迁移�?- `Assets/Adaptive Performance/`、`Assets/URP/`：Unity 或包导入产生的目录，后续如果要迁移，应按包设置专项确认�?
## `Assets/GameAssets/` 构建�?
`Assets/GameAssets` 只放运行时需要被构建管线纳入的产成品资源，例�?Prefab、ScriptableObject、Timeline、Animator、可直接加载的大图和运行时配置�?
当前约定的一级目录如下：

```text
Assets/GameAssets
├── Common/
�?  ├── ASP Global Settings/
�?  ├── Functions/
�?  ├── Shaders/
�?  └── Fonts/
├── Scenes/
├── Configs/
�?  └── Post Processing/
├── Textures/
├── Prefabs/
�?  ├── Hero/
�?  └── Monster/
├── Worlds/
�?  └── Meadow/
�?      ├── Shared/
�?      �?  ├── Materials/
�?      �?  ├── Prefabs/
�?      �?  └── Configs/
�?      ├── Seasons/
�?      �?  ├── Autumn/
�?      �?  ├── Summer/
�?      �?  └── Winter/
�?      ├── Chunks/
�?      �?  ├── Chunk_000_000/
�?      �?  ├── Chunk_000_001/
�?      �?  └── Chunk_001_000/
�?      └── Scenes/
└── UIModules/
    ├── UILogin/
    └── UIMain/
```

打包脚本默认扫描 `Assets/GameAssets`。`Common/`、`Worlds/` 和共�?shader / texture 有显式分组规则；其他目录继续按叶子目录或包含直接资源文件的中间目录生�?`angrymesh.gameassets.<relative.path>` 业务组。源素材、PSD/FBX 原始制作文件和导入中间态不要放进这里�?
## `Assets/GameResources/` 源产�?
`Assets/GameResources` 只放源素材和导入检查对象。资源检查、导入规则和 Shader / Material 风险预检默认只扫描此目录�?
当前已迁移的 ANGRY MESH 源素材：

```text
Assets/GameResources/Stylized Pack - Common/Sources
Assets/GameResources/Stylized Pack - Meadow Environment/Sources
```

当前已迁移的 ANGRY MESH 运行时构建资源：

```text
Assets/GameAssets/Common/ASP Global Settings
Assets/GameAssets/Common/Functions
Assets/GameAssets/Common/Shaders
Assets/GameAssets/Worlds/Meadow/Shared
Assets/GameAssets/Worlds/Meadow/Seasons/Autumn
Assets/GameAssets/Worlds/Meadow/Seasons/Summer
Assets/GameAssets/Worlds/Meadow/Seasons/Winter
Assets/GameAssets/Worlds/Meadow/Chunks
Assets/GameAssets/Worlds/Meadow/Scenes
```

## `Assets/Project/` 历史目录

`Assets/Project` 是当前存量代码和工具目录。迁移完成前，已有模块仍按资源类型划分；新增运行时资源优先进�?`Assets/GameAssets`，新增源素材优先进入 `Assets/GameResources`，新增代码优先进�?`Assets/Scripts` �?`Assets/Editor`�?
- `Configs/`：输入配置、Terrain 数据、Volume Profile、ScriptableObject 配置资产�?- `Materials/`：项目材质和 Terrain Layer�?- `Models/`：项目自研模型源文件�?- `Prefabs/`：项�?prefab�?- `Scripts/`：通用脚本入口；既有模块脚本在迁移前可保留原路径�?- `Shaders/`：Shader、Compute Shader、HLSL include�?- `Textures/`：项目贴图�?- `Tools/`：Editor 工具、性能分析工具、模板与辅助链路�?- `VFX/`、`Audio/`、`Animations/`、`Fonts/`、`Sprites/`、`Timelines/`：有对应资源时再创建或使用�?
当前存在�?`Characters/`、`Crowds/`、`CS/`、`sky/`、`qianxia/` 是历史模块根目录。它们包含大量硬编码路径、测试和方案文档引用，后续若要继续迁移，应按模块分批处理并同步测试与文档�?
## 三级目录原则

- `Textures/`、`Models/`、`Audio/` 的三级目录优先按子类型或导入策略划分，例�?`Textures/Grass/`、`Models/StaticModels/`、`Audio/Streaming/`�?- 其他资源类型可以在三级目录按功能域划分，但不要让功能域回到二级目录�?- 生命周期目录少用；大世界运行时资源例外，`Worlds/<World>/Shared + Seasons + Chunks` �?Addressables 加载/卸载边界。确实需要临时资源时，优先放 `.workspace/`；必须进 Unity 导入链路时再建立明确�?`Generated/` �?`Experimental/` 子目录�?- 目录命名应能服务自动化导入规则和资产检查。例如同�?Terrain 数据放在 `Configs/Terrain/`，Terrain Layer 放在 `Materials/Terrain/`�?
## 本次整理后的关键落点

- 输入资产：`Assets/GameAssets/Configs/Input/InputSystem_Actions.inputactions`
- Sky 配置：`Assets/Project/Configs/Sky/`
- Terrain 数据：`Assets/Project/Configs/Terrain/`
- Terrain Layer：`Assets/Project/Materials/Terrain/`
- Grass 分布贴图：`Assets/Project/Textures/Grass/`
- Sky 资源：`Assets/Project/Scripts/Sky/`、`Assets/Project/Materials/Sky/`、`Assets/Project/Models/Sky/`、`Assets/Project/Shaders/Sky/`、`Assets/Project/Textures/Sky/`
- Crowd 战斗 FX 贴图：`Assets/Project/Textures/Crowds/CombatFx/`
- HLSL include：`Assets/Project/Shaders/Includes/`
- 旧模板：`Assets/Project/Tools/Templates/LegacyRenderTemplates/`
- 外部资源包：`Assets/ThirdParty/ADG_Textures/`、`Assets/ThirdParty/Raygeas/`、`Assets/ThirdParty/Shop/`
- 千夏 MMD 原始包：`Assets/ThirdParty/QianxiaMmdSource/`
- Unity 模板说明资源：`Assets/ThirdParty/UnityTutorialInfo/`

## 千夏第三人称资源落点

`Assets/Project` 不再作为千夏第三人称控制的落点。当前按资源生命周期拆分如下�?
- 运行时代码：`Assets/Scripts/Characters/Qianxia/`
- 编辑器工具：`Assets/Editor/Characters/Qianxia/`
- 输入配置、prefab、材质、Animator 与生成资源：`Assets/GameAssets/Characters/Qianxia/`、`Assets/GameAssets/Configs/Input/`
- 源模型、源动画、参考贴图与参�?FBX：`Assets/GameResources/Characters/Qianxia/`
- VAT 播放运行时代码：`Assets/Scripts/Crowds/VAT/`
- VAT 烘焙编辑器代码：`Assets/Editor/Crowds/VAT/`
- VAT shader 资源：`Assets/GameAssets/Common/Shaders/Crowds/VAT/`

其中 `Assets/GameAssets/**` �?`Assets/GameResources/**` 继续�?SVN 管理，Git 不新�?LFS 规则�?