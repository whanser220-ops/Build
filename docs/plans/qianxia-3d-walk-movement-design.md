# 千夏 3D 角色走路移动设计文档

## 文档信息

- 版本：v0.2
- 日期：2026-04-15
- 状态：已偏离实现（角色 prefab 与安装工具已落地，正文仍停留在方案稿阶段）
- 同步级别：弱同步
- 作者：Codex
- 适用项目：`Unity 6 6000.0.46f1`
- 当前实现锚点：
  - `Assets/Project/Characters/Qianxia/Scripts/QianxiaGenshinCharacterController.cs`
  - `Assets/Project/Characters/Qianxia/Scripts/QianxiaGenshinCameraController.cs`
  - `Assets/Project/Characters/Qianxia/Generated/QianxiaThirdPerson.prefab`
  - `Assets/Project/Characters/Qianxia/Editor/QianxiaCharacterSetupUtility.cs`
  - `Assets/Project/Characters/Qianxia/Editor/QianxiaMeadowSceneMigrationUtility.cs`
  - `Assets/Game/Worlds/Meadow/Runtime/Scenes/Scene_MeadowEnvironment_01_Summer.unity`
- 最后实现核对：2026-06-15
- 关联实现：
  - `Assets/Project/Configs/Input/InputSystem_Actions.inputactions`
  - `Assets/Project/Characters/Qianxia/Scripts/QianxiaGenshinCharacterController.cs`
  - `Assets/Project/Characters/Qianxia/Scripts/QianxiaGenshinCameraController.cs`
- 外部资源：
  - 角色模型：`D:\mouxin\mhy\qianxia\Qianxia_Rokoko_BlenderClean.fbx`
  - 走路动画：`D:\mouxin\donghua\a_person_is_walking_slowly.fbx`
- 资料索引：`.workspace/artifacts/web/20260415-qianxia-walk-movement-sources.md`

## 1. 目标

本设计文档用于在当前 Unity 6 项目中落一版可运行、可调参、可继续扩展的第三人称 3D 角色走路移动方案。

本次优先目标不是做完整动作系统，而是先把以下闭环做稳：

- 角色可被键鼠与手柄控制。
- 角色按相机朝向在地面上稳定走路移动。
- 镜头可环绕、避障、自动回中。
- 角色能正确播放基础站立与走路表现。
- 后续可以平滑扩展到跑步、跳跃、冲刺、锁定和交互。

## 2. 范围与非目标

### 2.1 本次范围

- 第三人称平面移动。
- 相机驱动的移动方向计算。
- `CharacterController` 碰撞式移动。
- 基础地面检测与坡面适配。
- 最小 Animator 状态机。
- 提供模型与单条走路动画资源的导入接入方案。

### 2.2 本次不做

- 完整战斗状态机。
- 复杂 Root Motion 驱动位移。
- 攀爬、游泳、滑翔、锁敌。
- 网络同步方案。
- 八方向完整步行动画集。
- 正式量产级角色 prefab 自动安装工具。

## 3. 当前项目现状

### 3.1 工程能力现状

当前仓库已经具备本方案需要的关键基础：

- 输入系统：`Packages/manifest.json` 已包含 `com.unity.inputsystem 1.14.0`。
- 相机包：`Packages/manifest.json` 已包含 `com.unity.cinemachine 2.10.3`。
- 现有输入资产：`Assets/Project/Configs/Input/InputSystem_Actions.inputactions` 已有 `Player` Action Map，并包含 `Move`、`Look`、`Jump`、`Sprint`、`Interact` 等 Action。
- 现有角色移动脚本：`QianxiaGenshinCharacterController` 已实现基于 `CharacterController` 的移动、转向、地面检测、重力、`coyote time`、`jump buffer` 和 Animator 参数更新。
- 现有镜头脚本：`QianxiaGenshinCameraController` 已实现第三人称跟随、手动看向输入、镜头避障、滚轮缩放和移动驱动自动回中。

也就是说，本项目并不是从零开始缺方案，而是已经有一条接近可用的第三人称控制路径。本次文档的重点是把这条路径与新的角色模型、走路动画资源和官方资料整理成稳定的设计基线。

### 3.2 当前代码已经体现出的设计方向

从现有脚本看，项目已经事实上选择了以下路线：

- 位移由代码驱动，不依赖 Root Motion。
- 朝向由输入与相机共同决定，而不是固定朝世界前方。
- 地面与空中使用不同加减速度。
- 角色转向使用平滑阻尼，而不是瞬时转身。
- 相机独立于角色控制，但支持移动时慢慢回到角色背后。
- Animator 只吃简化后的运行时参数，不直接吃原始输入。

这条路线和 Unity 官方文档推荐的“`Input System + CharacterController + 第三人称相机 + Animator 表现层`”思路一致，因此应继续沿用，而不是改成全新的 Rigidbody 或 Root Motion 主驱动架构。

### 3.3 当前资源缺口

当前工作区里能直接看到的千夏目录只有脚本，未看到稳定落库的以下资源：

- 角色 prefab
- Animator Controller
- Avatar / 动画导入资源
- 可直接运行的千夏场景安装入口

因此，本次设计文档需要把你提供的外部 FBX 作为新的资源输入基线，明确后续该如何导入和挂接。

## 4. 设计结论

### 4.1 推荐主方案

本项目第一版正式方案建议定为：

- 输入：`Input System`
- 角色控制器：`CharacterController`
- 相机：先沿用当前自定义第三人称相机脚本，结构上对齐 `Cinemachine 3rd Person Follow`
- 位移：代码驱动水平速度、重力和转向
- 动画：`In-Place` 站立/走路表现，Animator 只做表现层，不接管位移

一句话总结：

`Input System + CharacterController + 自定义第三人称相机 + In-Place Walk 动画 + 代码驱动移动`

这是最符合当前项目现实情况的路线。

### 4.2 为什么不是直接强切到 Cinemachine

外部资料显示 Cinemachine 很适合第三人称镜头，项目里也确实已经安装了 `com.unity.cinemachine 2.10.3`。但是从当前仓库状态出发，第一版不建议为了“形式统一”而重写镜头系统，原因如下：

- 现有 `QianxiaGenshinCameraController` 已经具备目标跟随、绕角色旋转、避障、缩放和移动回中能力。
- 当前用户目标是“角色走路移动设计文档”，不是“全面改造镜头框架”。
- 若现在强行切到 Cinemachine，会把问题从“落地移动方案”扩大成“重配虚拟相机、输入桥接、场景接管和参数迁移”。

因此本次建议是：

- 第一版继续沿用现有自定义相机脚本。
- 文档层面对照 `Cinemachine 3rd Person Follow` 的结构来约束参数命名和行为。
- 后续若场景数量变多，再评估是否迁移到 Cinemachine 虚拟相机工作流。

这是基于官方文档能力边界和当前项目代码现状做出的工程判断，不是 Unity 官方的唯一要求。

### 4.3 为什么不推荐 Rigidbody 作为主角移动主路径

第一版不建议改成 `Rigidbody` 主驱动，原因是：

- 当前项目目标是“主角可控感”优先，不是“完全物理真实性”优先。
- `CharacterController` 更适合强控制、清晰可调、少抖动的玩家角色。
- 现有脚本和参数体系已经围绕 `CharacterController` 建立，切到 Rigidbody 会带来一轮额外重写。

只有在以下条件成立时，才值得重新评估 Rigidbody：

- 角色需要频繁被外力击飞或推挤。
- 角色需要和大量动态刚体做连续双向物理反馈。
- 游戏主体验明确要求“角色本身也被物理系统支配”。

### 4.4 为什么不推荐 Root Motion 作为当前位移主路径

当前不建议把位移直接交给 Root Motion，原因是：

- 当前只有一条“慢走”动画，资源不足以支撑完整 Root Motion 移动系统。
- Root Motion 会把速度、方向和动画片段耦合得更紧，降低输入调参与镜头联动的自由度。
- 现有项目代码已经明确是代码驱动位移，再切 Root Motion 会破坏当前实现基线。

Root Motion 可以作为后续升级方向，但前提是先补齐：

- 站立
- 前进走
- 跑步
- 起步
- 刹停
- 转身
- 受击或特殊状态动作

在这之前，推荐继续使用 `In-Place` 动画。

## 5. 资源接入设计

### 5.1 建议的资源落库路径

建议把你提供的两个外部资源收敛到项目内统一目录，便于后续引用、版本管理和自动化安装：

- 角色模型建议落到：`Assets/Project/Characters/Qianxia/SourceModels/`
- 走路动画建议落到：`Assets/Project/Characters/Qianxia/SourceAnimations/Walk/`

推荐命名如下：

- `Qianxia_Rokoko_BlenderClean.fbx`
- `Qianxia_Walk_Slow.fbx`

### 5.2 模型导入设置

角色模型 `Qianxia_Rokoko_BlenderClean.fbx` 建议作为 Humanoid 主 Avatar 来源：

- Rig：`Humanoid`
- Avatar Definition：`Create From This Model`
- Animation Type：若模型本身不需要动作片段，可保留默认或只作为 Avatar 来源
- Materials：按当前项目美术方案决定，不在本次移动设计文档主范围

导入后需要确认：

- 人形骨骼自动映射是否完整。
- 朝向是否正确，默认前向建议统一为 Unity 常用前方。
- 脚底落点是否与地面接触合理。
- 身体比例是否会让 `CharacterController` 胶囊过大或过小。

### 5.3 走路动画导入设置

走路动画 `a_person_is_walking_slowly.fbx` 建议按 Humanoid 动画导入，并复制角色 Avatar：

- Rig：`Humanoid`
- Avatar Definition：`Copy From Other Avatar`
- Source Avatar：指向 `Qianxia_Rokoko_BlenderClean.fbx` 生成的 Avatar
- Loop Time：开启
- Loop Pose：开启

若该动画带有明显前进位移，而项目又采用代码驱动位移，则需要把它处理成尽量接近 `In-Place` 的表现型动画。可接受的处理方式有两种：

- 在导入设置里把 Root Transform 的平移影响尽量收回到 Pose，用这条动画只负责“看起来像在走”。
- 在 DCC 工具里先把位移烘平，再导入 Unity。

本项目优先第一种，因为它能更快验证闭环。

### 5.4 当前资源条件下的最小动画集

由于你当前明确给到的只有一条走路动画，所以第一版动画集建议这样定义：

- `Idle`：临时站立 Pose 或后续补一条正式待机动画
- `Walk`：`a_person_is_walking_slowly.fbx`

这里要明确一个现实约束：

如果没有正式 `Idle` 动画，第一版仍然可以完成技术验证，但视觉上不会是最终品质。建议把“补一条待机动作”列为下一步高优先级资源事项。

## 6. 运行时设计

### 6.1 模块职责拆分

| 模块 | 当前落点 | 职责 |
|------|------|------|
| 输入 | `Assets/Project/Configs/Input/InputSystem_Actions.inputactions` | 提供 `Move`、`Look`、`Jump`、`Sprint` 等 Action |
| 角色移动 | `QianxiaGenshinCharacterController` | 速度解算、重力、转向、地面检测、Animator 参数输出 |
| 第三人称镜头 | `QianxiaGenshinCameraController` | 跟随、绕角色旋转、避障、自动回中、缩放 |
| 动画表现 | 待补 Animator Controller | 根据 `Speed / Grounded / HasMove` 切换表现 |
| 资源挂接 | 待补 prefab 或安装脚本 | 把模型、Animator、控制器和相机关联到同一个角色实例 |

### 6.2 输入设计

当前 `Player` Action Map 已经满足第一版所需：

- `Move(Vector2)`
- `Look(Vector2)`
- `Jump(Button)`
- `Sprint(Button)`

第一版建议策略如下：

- `Move` 正式启用
- `Look` 正式启用
- `Jump` 保留接口，但不纳入“走路移动验收”的核心标准
- `Sprint` 暂时只保留输入，不默认开放，直到拿到跑步动画

原因很简单：

当前资源条件只有慢走动画，没有跑步资源。若在第一版开放冲刺，只会制造更明显的脚滑和表现错位。

这里还要额外记录一个项目级风险：

- 当前角色与镜头脚本是按字符串查找 `Player / Move / Look / Jump / Sprint` 这些 Action 名称的。
- 如果后续有人重命名 `InputSystem_Actions.inputactions` 里的 Action Map 或 Action，但没有同步修改 Inspector 配置，运行时输入会直接失效。

因此，这些名字在第一版里应视为接口名，而不是随手可改的展示文案。

### 6.3 每帧移动流程

当前项目建议沿用下面这条每帧逻辑：

1. 读取 `Move` 输入。
2. 根据主相机朝向，计算相对相机平面的前向和右向。
3. 把输入投影到地面平面，合成期望移动方向。
4. 按当前状态选择目标速度。
5. 使用地面/空中不同的加速度与减速度，平滑更新水平速度。
6. 使用 `CharacterController.Move()` 执行本帧位移。
7. 单独累积重力与垂直速度。
8. 更新朝向、落地状态和 Animator 参数。

当前 `QianxiaGenshinCharacterController` 已经覆盖了这条主逻辑，只需继续围绕它接入新资源与调参。

### 6.4 角色朝向设计

第一版角色朝向规则如下：

- 有移动输入时，角色朝期望移动方向转身。
- 使用平滑阻尼转向，避免瞬转。
- 即使是短促输入，也要让角色把本次转向补完整一小段时间，避免“输入刚松开角色就半转不转”。

当前脚本里的 `_quickTapFacingHoldTime` 正是在解决这个问题，应继续保留。

### 6.5 地面与坡面设计

第一版地面设计建议如下：

- 主判断使用 `CharacterController.isGrounded`。
- 再做一次球形向下探测作为补充校验。
- 角色落地后给一个轻微向下吸附速度，避免下坡与地形接缝抖动。
- 角色移动方向默认投影在世界 `XZ` 平面；后续如果遇到明显斜坡体验问题，再进一步升级为按地表法线投影。

当前脚本已实现：

- `isGrounded + SphereCast` 双重校验
- `_groundedStickForce`
- `_groundProbeDistance`
- 启动时向地面吸附

因此第一版无需推翻现有做法，只需要在实机里验证胶囊尺寸、脚底高度和地面 Layer 设置。

### 6.6 镜头设计

第一版镜头行为建议定义为：

- 镜头围绕角色上方的跟随点旋转。
- 鼠标与手柄分开使用不同灵敏度。
- 玩家手动看镜头时，自动回中暂停一小段时间。
- 玩家持续移动时，镜头逐步回到角色背后。
- 镜头遇到遮挡时使用球形检测缩短距离，而不是简单射线硬裁。

这套设计与现有 `QianxiaGenshinCameraController` 完全一致，因此当前镜头脚本可以视为该设计文档的现有实现基线。

这里还有一个需要写进文档的项目级约束：

- `QianxiaGenshinCharacterController` 通过 `Camera.main` 取得移动参考方向。
- 如果场景主相机没有正确设置 `MainCamera` Tag，脚本会回退到世界前方，角色移动方向就不再跟随镜头。

因此后续 prefab 和场景安装步骤里，必须把“主相机 Tag 正确”当成验收项之一。

### 6.7 动画设计

当前资源条件下，Animator 最小集建议如下：

- 参数：
  - `Speed(float)`
  - `Grounded(bool)`
  - `HasMove(bool)`
- 状态：
  - `Idle`
  - `Walk`

第一版最简单做法：

- 当 `Speed` 低于阈值时进入 `Idle`
- 当 `Speed` 高于阈值时进入 `Walk`

如果后续补到更多动作，再升级为：

- `Idle`
- `Locomotion(1D Blend Tree)`
- 或 `Locomotion(2D Blend Tree)`

当前脚本已经输出 `Speed / Grounded / HasMove`，因此 Animator 设计应围绕这三个参数来建，而不是重新定义另一套参数命名。

## 7. 参数建议

以下参数可直接作为第一版默认值起点，来源于当前代码：

| 参数 | 建议值 | 说明 |
|------|------|------|
| `_walkSpeed` | `2.6` | 与“慢走”资源更匹配，先保证脚步不明显打滑 |
| `_sprintSpeed` | `6.4` | 暂保留，但第一版建议不开放 |
| `_groundAcceleration` | `26.0` | 地面启动要利落 |
| `_groundDeceleration` | `30.0` | 松开输入后尽快收住 |
| `_airAcceleration` | `9.0` | 空中控制弱于地面 |
| `_airDeceleration` | `4.0` | 空中减速慢于地面 |
| `_rotationSmoothTime` | `0.07` | 保持角色转向顺滑 |
| `_quickTapFacingHoldTime` | `0.22` | 保证短促输入也能完成转身 |
| `_gravity` | `-30.0` | 当前代码基线 |
| `_terminalVelocity` | `-36.0` | 当前代码基线 |
| `_groundedStickForce` | `-3.0` | 轻微贴地吸附 |
| `_coyoteTime` | `0.12` | 保留扩展性 |
| `_jumpBufferTime` | `0.12` | 保留扩展性 |

镜头侧建议保留当前默认参数起步，优先验证：

- `_defaultDistance = 4.6`
- `_minDistance = 2.2`
- `_maxDistance = 6.0`
- `_mouseSensitivity = 0.14`
- `_gamepadSensitivity = 150.0`

## 8. 实施步骤

建议的实施顺序如下：

1. 把模型和走路动画复制进项目建议目录。
2. 将模型导入为 Humanoid，并生成 Avatar。
3. 将走路动画导入为 Humanoid，并绑定到同一 Avatar。
4. 创建或补齐最小 Animator Controller：`Idle + Walk`。
5. 创建千夏第三人称角色 prefab，挂上：
   - `CharacterController`
   - `Animator`
   - `QianxiaGenshinCharacterController`
   - `QianxiaGenshinCameraController`
6. 绑定输入资产与 Animator 参数名，并确认 Action 名称与脚本中的 `Player / Move / Look / Jump / Sprint` 一致。
7. 确认运行时主相机带有 `MainCamera` Tag，避免角色移动方向退回到默认世界前方。
8. 在 `SampleScene` 或专用测试场景中做基础走路验证。
9. 完成后再决定是否开放 `Jump` 和 `Sprint`。

## 9. 验收标准

第一版“走路移动”完成的最低验收标准建议如下：

- 键鼠和手柄都能驱动角色移动与转向。
- 角色移动方向相对当前镜头朝向正确。
- 角色不会轻易穿墙或卡进地面。
- 镜头能绕角色旋转，并在遮挡时正确缩近。
- 角色停止输入后能稳定回到站立表现。
- 走路时不存在非常明显的朝向抖动。
- 慢走状态下脚滑控制在可接受范围内。

## 10. 风险与应对

### 10.1 只有一条走路动画

风险：

- 没有待机、后退、侧移、跑步时，角色表现会明显偏“技术验证版”。

应对：

- 第一版只承诺“前向走路”体验。
- `Sprint` 默认关闭。
- 尽快补一条 `Idle` 和一条 `Run`。

### 10.2 骨骼重定向失败

风险：

- 模型 Avatar 与动画 Avatar 不兼容时，会出现手脚错位、脚滑、骨盆漂移甚至动作无法播放。

应对：

- 严格使用 `Copy From Other Avatar`。
- 首次导入后先做 Avatar 映射检查。
- 若映射质量差，优先回 DCC 工具清理骨架，而不是在 Unity 里硬修运行时逻辑。

### 10.3 动画自带位移导致脚滑

风险：

- 若 `a_person_is_walking_slowly.fbx` 带有明显前进根位移，而运行时又由代码推进角色，视觉上会双重前进或出现脚滑。

应对：

- 优先把该动作按 `In-Place` 方式使用。
- 用 `_walkSpeed` 与动画播放速度做联调。
- 第一版先追求“整体自然”，不追求步点级精确贴合。

### 10.4 现有仓库资源不完整

风险：

- 当前工作区里没有看到稳定落库的千夏 prefab、Animator Controller 和动作资源，因此文档能定义方案，但真正接入时仍需补实际资源。

应对：

- 以本次提供的两个 FBX 作为后续资源接入起点。
- 后续如要正式落库，应把外部文件转移到项目内统一目录并纳入版本管理。

### 10.5 主相机 Tag 配置错误

风险：

- 若场景主相机没有 `MainCamera` Tag，角色移动参考方向会退回世界固定方向，导致“镜头在转，但角色仍按世界坐标移动”的错误体验。

应对：

- 把主相机 Tag 检查纳入 prefab 安装与场景验收步骤。
- 后续若继续迭代脚本，可考虑把相机引用改成显式注入，降低对 `Camera.main` 的隐式依赖。

### 10.6 输入 Action 重命名导致运行时失效

风险：

- 当前输入绑定通过字符串查找 `Player / Move / Look / Jump / Sprint`，若资源改名但 Inspector 未同步，角色会出现局部或全部输入失效。

应对：

- 第一版冻结这些输入接口名。
- 后续若要重命名，必须把输入资产和角色/镜头脚本配置一起检查。

## 11. 后续扩展路线

第一版闭环完成后，建议按以下顺序扩展：

1. 补 `Idle` 动画。
2. 补 `Run` 动画并开放 `Sprint`。
3. 把 `Idle/Walk/Run` 升级为 1D Blend Tree。
4. 再补左右/后退动作，升级为 2D Blend Tree。
5. 增加 `Jump/Fall/Land`。
6. 若场景变复杂，再评估接入 Cinemachine 虚拟相机工作流。
7. 若未来追求高动作一致性，再评估 Root Motion 局部接管。

## 12. 外部参考

本方案主要参考 Unity 官方文档，并结合当前项目现有代码做工程收敛。外部资料详见：

- `.workspace/artifacts/web/20260415-qianxia-walk-movement-sources.md`

其中需要特别强调的官方结论有三点：

- `CharacterController.Move()` 不会自动处理重力，垂直速度需要脚本自己维护。
- Unity 6000 文档明确推荐大多数项目优先使用 `Input System Package`。
- `Cinemachine 3rd Person Follow` 提供的“肩部枢轴 + 距离 + 避障”结构，非常适合作为第三人称镜头行为参考。
