using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-150)]
public sealed partial class CrowdVatSquadController : MonoBehaviour
{
    private const float MinimumFormationSpacing = 0.1f;
    private const float DefaultFormationSpacing = 1.6f;
    private const float DefaultFormationStrength = 0.35f;
    private const float FormationCollisionSpacingPadding = 0.08f;
    private const float SquadPbdPackingDensity = 0.72f;
    private const float SquadPbdRadiusPaddingInAgents = 1.5f;

    [Serializable]
    private sealed class SquadAuthoring
    {
        [Tooltip("仅用于面板识别的小队名称，不参与运行时逻辑。")]
        public string name = "Squad_00";

        [Tooltip("关闭后，这个小队不会写入 renderer 的 runtime squad buffer。")]
        public bool enabled = true;

        [Tooltip("小队中心空物体。直接拖动它，就能整体移动这支小队。")]
        public Transform centerTransform;

        [Tooltip("小队朝向空物体。留空时回退到中心空物体自身的 forward。")]
        public Transform forwardTransform;

        [Tooltip("小队目标点空物体。MoveTo / Advance / Charge / Regroup 会用它决定目标方向。")]
        public Transform targetTransform;

        [Tooltip("从 crowd 实例数组的哪个索引开始归属到这个小队。")]
        [Min(0)]
        public int memberStartIndex;

        [Tooltip("这个小队控制多少个成员。成员按 crowd 实例顺序连续分配。")]
        [Min(1)]
        public int memberCount = 32;

        [InspectorName("队形")]
        [Tooltip("内置队形。None 表示不生成编队槽位；启用自定义槽位时优先使用自定义槽位。")]
        public CrowdVatSquadFormationType formationType = CrowdVatSquadFormationType.Block;

        [Tooltip("当前小队命令。")]
        public CrowdVatSquadCommandType commandType = CrowdVatSquadCommandType.Hold;

        [Tooltip("小队归属阵营掩码。")]
        public CrowdVatFactionMask factionMask = CrowdVatFactionMask.All;

        [Tooltip("控制朝向和局部形变语义的 squad flags。")]
        public CrowdVatSquadFlags flags = CrowdVatSquadFlags.FaceTarget;

        [InspectorName("队形间距")]
        [Tooltip("内置队形的局部 XZ 间距：X 是小队右侧间距，Y 是小队前后间距。")]
        public Vector2 formationSpacing = new Vector2(DefaultFormationSpacing, DefaultFormationSpacing);

        [Tooltip("启用后，这个小队会按下面的自定义槽位数组形成队形。槽位是局部 XZ 平面偏移：X 为小队右侧，Y 为小队前方。")]
        public bool useCustomFormationSlots;

        [Tooltip("自定义队形槽位。第 0 个槽位对应小队第 0 个成员，数量不足时剩余成员会使用中心点。")]
        public Vector2[] customFormationSlots = Array.Empty<Vector2>();

        [InspectorName("编队强度")]
        [Tooltip("成员被拉向编队槽位的软约束强度。0 表示不跟随队形，1 表示每步尽量贴近槽位。")]
        [Range(0.0f, 1.0f)]
        public float customFormationStrength = DefaultFormationStrength;

        [Tooltip("运行时推进中心点时使用的小队速度。")]
        [Min(0.0f)]
        public float moveSpeed = 2.0f;

        [HideInInspector]
        public float anchorBlend = 0.0f;

        [HideInInspector]
        public float cohesionRadius = 0.0f;

        [HideInInspector]
        public float cohesionStrength = 0.0f;

        [Tooltip("这个小队成员的默认角色掩码。当前主要作为后续扩展入口。")]
        public CrowdVatSquadRoleMask memberRoleMask = CrowdVatSquadRoleMask.All;

        [Tooltip("成员默认 flags。")]
        public CrowdVatSquadMemberFlags memberFlags = CrowdVatSquadMemberFlags.None;

        [Tooltip("成员默认权重。")]
        [Min(0.0f)]
        public float memberWeight = 1.0f;
    }

    private struct SquadMotionState
    {
        public Vector3 lastCenter;
        public Vector3 worldVelocity;
        public Vector3 worldForward;
        public bool initialized;
    }

    private enum SquadMovementAnimationState
    {
        Unknown = 0,
        Idle = 1,
        Walk = 2
    }

    [Header("运行时绑定")]
    [Tooltip("要写入 runtime squad buffer 的 crowd renderer。留空时会尝试在自己或父节点上自动查找。")]
    [SerializeField] private CrowdVatIndirectRenderer _renderer;
    [Tooltip("启用后，如果未手动指定 renderer，会自动在自己或父节点上查找。")]
    [SerializeField] private bool _autoResolveRenderer = true;

    [Header("运行时驱动")]
    [InspectorName("运行时推动中心")]
    [Tooltip("运行时如果命令不是 Hold，且存在目标点，会按 moveSpeed 推动中心空物体朝目标移动。")]
    [SerializeField] private bool _driveCenterToTargetInPlayMode = true;
    [InspectorName("默认目标距离")]
    [Tooltip("没有显式目标点时，默认沿 forward 推出的参考距离。")]
    [SerializeField] [Min(0.0f)] private float _defaultTargetDistance = 6.0f;
    [InspectorName("到达距离")]
    [Tooltip("MoveTo / Advance 接近目标点后，中心停止推进的剩余距离。")]
    [SerializeField] [Min(0.0f)] private float _arrivalDistance = 0.2f;
    [InspectorName("小队转向速度")]
    [Tooltip("小队编队朝向每秒最多可旋转多少度。值越小，转向越像真实惯性运动，而不是瞬时翻面。")]
    [SerializeField] [Min(0.0f)] private float _formationTurnSpeedDegreesPerSecond = 240.0f;

    [Header("自动动画")]
    [Tooltip("启用后，按小队实际移动速度在 idle 和 walk 之间自动切换成员动画。")]
    [SerializeField] private bool _enableMovementAnimationBlending = true;
    [Tooltip("静止时切回的 clip 名称。留空时会尝试自动匹配包含 Idle 的 clip。")]
    [SerializeField] private string _idleClipName = "";
    [Tooltip("移动时切到的 clip 名称。留空时会尝试自动匹配包含 Walk 的 clip。")]
    [SerializeField] private string _walkClipName = "Qianxia_Walk_Slow";
    [Tooltip("速度达到这个阈值后，从 idle 混到 walk。")]
    [SerializeField] [Min(0.0f)] private float _moveAnimationEnterSpeed = 0.15f;
    [Tooltip("速度低于这个阈值后，从 walk 混回 idle。应小于或等于进入阈值，以减少抖动。")]
    [SerializeField] [Min(0.0f)] private float _moveAnimationExitSpeed = 0.05f;
    [Tooltip("自动切换 idle 和 walk 时使用的过渡时长。")]
    [SerializeField] [Min(0.0f)] private float _movementAnimationTransitionDuration = 0.2f;

    [Header("场景预览")]
    [Tooltip("选中 controller 时，是否绘制小队中心、朝向和目标线。")]
    [SerializeField] private bool _showFormationPreview = true;
    [Tooltip("选中 controller 时，是否绘制每支小队的 combat squad 候选半径。该半径是 squad broadphase 保守超集，不代表单个 agent 的最终索敌结果。")]
    [SerializeField] private bool _showCombatCandidateRadius = true;
    [Tooltip("场景预览最多绘制多少个小队，避免 Scene 视图太吵。")]
    [SerializeField] [Min(1)] private int _maxPreviewSlots = 96;

    public bool ShowCombatCandidateRadiusPreview => _showCombatCandidateRadius;

    [Header("选择辅助")]
    [Tooltip("运行时瞄准选择时，给每个小队补上的竖向高度。")]
    [SerializeField] [Min(0.1f)] private float _selectionHeight = 2.2f;
    [Tooltip("运行时瞄准选择时，给每个小队额外扩出来的包围半径。")]
    [SerializeField] [Min(0.0f)] private float _selectionPadding = 0.8f;

    [Header("默认生成")]
    [Tooltip("自动生成默认战术小队时，每个阵营拆成多少支小队。单阵营模式也会使用这个数量。")]
    [SerializeField] [Min(1)] private int _defaultSquadsPerFaction = 4;
    [Tooltip("自动生成默认小队锚点时，同阵营小队之间的间距。")]
    [SerializeField] [Min(0.5f)] private float _defaultAnchorSpacing = 6.0f;

    [Header("小队列表")]
    [Tooltip("每个条目对应一个 squad authoring。中心、朝向和目标都建议直接用空物体。")]
    [SerializeField] private SquadAuthoring[] _squads = { new SquadAuthoring() };

    private readonly List<int> _activeSquadSourceIndices = new List<int>(8);
    private readonly CrowdVatWorld _world = new CrowdVatWorld();
    private readonly CrowdVatFramePacket _framePacket = new CrowdVatFramePacket();
    private readonly CrowdVatFrameObservation _frameObservation = new CrowdVatFrameObservation();
    private CrowdVatSquadState[] _squadStateCache = Array.Empty<CrowdVatSquadState>();
    private CrowdVatAgentSquadAssignment[] _agentAssignmentCache = Array.Empty<CrowdVatAgentSquadAssignment>();
    private CrowdVatFormationSlot[] _formationSlotCache = Array.Empty<CrowdVatFormationSlot>();
    private CrowdVatWorldSquadTruth[] _worldSquadTruthCache = Array.Empty<CrowdVatWorldSquadTruth>();
    private int[] _sourceSquadToRuntimeIndexCache = Array.Empty<int>();
    private int[] _sourceSquadSelectableFrameCache = Array.Empty<int>();
    private bool[] _sourceSquadSelectableValueCache = Array.Empty<bool>();
    private int[] _runtimeSquadInitialAliveCountCache = Array.Empty<int>();
    private int[] _lastAppliedRuntimeSquadSourceIndices = Array.Empty<int>();
    private float[] _selectionRadiusCache = Array.Empty<float>();
    private int[] _selectionRadiusMemberCountCache = Array.Empty<int>();
    private SquadMotionState[] _motionStateCache = Array.Empty<SquadMotionState>();
    private SquadMovementAnimationState[] _movementAnimationStateCache = Array.Empty<SquadMovementAnimationState>();
    private readonly List<int> _movementAnimationInstanceIndexScratch = new List<int>(64);
    private CrowdVatRendererFrameBridge _frameBridge;
    private int _lastAliveObservationCaptureFrame = int.MinValue;
    private bool _hasLoggedMovementAnimationConfigurationWarning;
    private bool _hasAliveObservationSnapshot;
    private bool _layoutDirty = true;
    private bool _hasAppliedData;
}
