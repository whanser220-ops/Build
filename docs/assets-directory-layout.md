# Assets 目录组织规范

本文记录当前仓库的 `Assets/` 资源落位规则。新的主路径放弃顶层 `GameAssets` / `GameResources` 二分法，改为按内容模块组织，并在模块内部区分 `Art` 与 `Runtime`。

## 核心原则

物理目录优先表达“这个资源属于哪个内容模块”，而不是优先表达“它是构建入口还是依赖素材”。

```text
Assets/Game/<内容域>/<模块>/
├── Art/          # 美术依赖资产：模型、贴图、材质、动画、音频、VFX 源输入等
├── Runtime/      # 运行时入口资产：Prefab、Scene、ScriptableObject、Timeline、Animator、直接加载资源等
├── Editor/       # 只服务该模块的编辑器工具
├── Generated/    # 可再生成产物；需要进入构建时优先放 Runtime/Generated
└── Docs/         # 模块内短说明，可选
```

构建系统扫描 `Runtime`，导入检查和依赖分组扫描 `Art`。普通美术人员进入一个模块目录即可看到该模块的生产素材和运行时装配结果，不需要在两个顶层大目录之间来回跳转。

## 根级目录

- `Assets/Game/`：新增游戏内容主目录，按内容域和模块组织。
- `Assets/Scripts/`：跨模块运行时代码主入口。
- `Assets/Editor/`：跨模块构建脚本、管线工具与编辑器工具入口。
- `Assets/Settings/`：Unity / URP / 项目级配置。
- `Assets/Tests/`：EditMode / PlayMode 测试。
- `Assets/ThirdParty/`：外部资源包、Asset Store 包与供应商原始内容。后续第三方插件和 SDK 也可按需要迁到 `Assets/3rd/`。
- `Assets/Resources/`、`Assets/StreamingAssets/`：仅在确实需要 Unity 特殊目录语义时使用。

当前存量兼容说明：

- `Assets/GameAssets/`：旧构建区，物理目录已移除。部分工具仍能识别旧路径，便于处理未迁移分支或历史引用；新增资源不要继续放入。
- `Assets/GameResources/`：旧源素材区，物理目录已移除。资产版本流已改为 Perforce 管理模块 `Art` / `Runtime` 子树；新增模块素材进入对应模块的 `Art`。
- `Assets/Project/`：历史代码、渲染实验和工具目录。已有硬编码路径较多，按模块分批迁移。
- `Assets/ANGRY MESH/`：旧插件残留区，仅保留脚本、模板包和说明文件。
- `Assets/Adaptive Performance/`、`Assets/URP/`、`Assets/Samples/`：Unity 或包导入产生的目录，迁移前需要专项确认。

## 推荐结构

```text
Assets/Game/
├── Characters/
│   └── Qianxia/
│       ├── Art/
│       │   ├── Models/
│       │   ├── Textures/
│       │   ├── Materials/
│       │   └── Animations/
│       ├── Runtime/
│       │   ├── Prefabs/
│       │   ├── Data/
│       │   └── Generated/
│       └── Editor/
│
├── Worlds/
│   └── Meadow/
│       ├── Art/
│       │   ├── Models/
│       │   ├── Textures/
│       │   ├── Materials/
│       │   ├── TerrainData/
│       │   └── TerrainLayers/
│       ├── Runtime/
│       │   ├── Shared/
│       │   ├── Seasons/
│       │   ├── Scenes/
│       │   └── Configs/
│       └── Generated/
│
├── UI/
│   └── UILogin/
│       ├── Art/
│       │   ├── Sprites/
│       │   ├── Materials/
│       │   └── Animations/
│       └── Runtime/
│           ├── Prefabs/
│           └── Data/
│
├── VFX/
│   └── Fireball/
│       ├── Art/
│       └── Runtime/
│
├── Shared/
│   └── StylizedPackCommon/
│       ├── Art/
│       └── Runtime/
│           ├── Shaders/
│           ├── ShaderVariants/
│           ├── Fonts/
│           └── Functions/
│
└── Core/
    └── Input/
        └── Runtime/
```

## 构建收集规则

- 主资源从 `Assets/Game/**/Runtime/**` 收集。
- `Art` 默认没有运行时地址，不作为代码直接加载入口。
- `Art` 中的模型、材质、贴图、动画、音频等默认不生成独立 Collector，由 Unity/SBP 按引用关系内联进引用它们的 Runtime / Prefab bundle。
- 只有明确需要公共化、跨包共享或单独更新的 `Art` 子目录才生成独立依赖 Collector；当前默认保留 `Assets/Game/Characters/Qianxia/Art/Meshs` 与 `Assets/Game/Characters/Qianxia/Art/Textures`。
- Meadow 的 Post Processing 配置 Collector 指向 `Configs/Post Processing/URP`；旧 `Standard` 配置不进入 YooAsset 包。
- 目录提供默认模块边界，最终 AssetBundle 粒度仍由加载生命周期、更新频率、依赖关系和显式 Pack Rule 决定。
- `Runtime` 中可以有 `Prefabs/`、`Scenes/`、`Data/`、`Configs/`、`Generated/` 等子目录，但不要把底层 DCC 源文件放入 `Runtime`。

YooAsset 当前已支持：

```text
Assets/Game/**/Runtime/**       -> 主资源 Collector
Assets/Game/**/Art/**           -> 默认不收集，作为 Runtime/Prefab 引用依赖内联
显式白名单 Art 子目录             -> 独立依赖资源 Collector
历史 `Assets/GameAssets/**` / `Assets/GameResources/**` 路径只作为工具兼容入口，不作为当前物理目录。
```

## 依赖方向

允许：

```text
Runtime -> Art
Runtime -> Shared/*/Runtime
Runtime -> Shared/*/Art
```

禁止：

```text
Art -> Runtime
Art -> 其他模块 Runtime
```

如果 `Art` 资源必须被运行时代码直接加载，应将它提升到同模块的 `Runtime`，并明确其加载语义。

## 存量迁移映射

后续迁移按模块分批执行，不做一次性全仓搬迁。

```text
Assets/Game/Characters/Qianxia/Art
  -> Assets/Game/Characters/Qianxia/Art

Assets/Game/Characters/Qianxia/Runtime
  -> Assets/Game/Characters/Qianxia/Runtime

Assets/Game/Worlds/Meadow/Art/Sources
  -> Assets/Game/Worlds/Meadow/Art/Sources

Assets/Game/Worlds/Meadow/Runtime
  -> Assets/Game/Worlds/Meadow/Runtime

Assets/Game/Shared/StylizedPackCommon/Art/Sources
  -> Assets/Game/Shared/StylizedPackCommon/Art/Sources

Assets/Game/Shared/StylizedPackCommon/Runtime
  -> Assets/Game/Shared/StylizedPackCommon/Runtime

Assets/Game/Core/Input/Runtime
  -> Assets/Game/Core/Input/Runtime
```

每个迁移批次都需要同步：

- `.meta` 文件和资源 GUID。
- Editor 工具中的硬编码路径。
- YooAsset 构建计划和 `BundleCollectorSetting.asset`。
- 场景、Prefab、ScriptableObject 中保存的字符串路径。
- 相关方案文档和测试。

## `Assets/Project/` 历史目录

`Assets/Project` 仍是当前 URP / RenderGraph、Crowd VAT、Scene Query 和 RenderDoc 工具链的存量实现目录。已有模块包含大量硬编码路径、测试和方案文档引用，迁移时按模块单独执行。

新增内容规则：

- 新增游戏内容资源进入 `Assets/Game/<内容域>/<模块>/Art` 或 `Runtime`。
- 新增跨模块运行时代码进入 `Assets/Scripts/`。
- 新增跨模块编辑器工具进入 `Assets/Editor/`。
- 与旧模块强绑定的临时改动可以先留在 `Assets/Project/`，但需要在对应方案文档中标明迁移状态。

## Perforce 与外部源文件

真正的 DCC 工程文件（如 `.blend`、`.ma`、`.spp`、`.psd`）优先放在仓库外部美术源资产库。进入 Unity 的交换资产和导入后依赖资产放到模块 `Art`。

Unity 内容资产由 Perforce 管理，当前 view 见 `tools/perforce-assets.p4view`。Git 只管理模块根目录、`Art.meta`、`Runtime.meta` 等目录级 `.meta`，`Art` / `Runtime` 内部资源和内部 `.meta` 不进 Git。

当前 Perforce 管理范围包括：

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

同步和提交流程见 `docs/perforce-art-assets-workflow.md`。
