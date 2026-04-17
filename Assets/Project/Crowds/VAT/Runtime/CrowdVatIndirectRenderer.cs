using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class CrowdVatIndirectRenderer : MonoBehaviour
{
    private static readonly int BoneAnimationTextureId = Shader.PropertyToID("_BoneAnimationTex");
    private static readonly int InstanceTransformsId = Shader.PropertyToID("_InstanceTransforms");
    private static readonly int InstanceFrameDataId = Shader.PropertyToID("_InstanceFrameData");
    private static readonly int VisibleInstanceIndicesId = Shader.PropertyToID("_VisibleInstanceIndices");
    private static readonly int CrowdRootLocalRow0Id = Shader.PropertyToID("_CrowdRootLocalRow0");
    private static readonly int CrowdRootLocalRow1Id = Shader.PropertyToID("_CrowdRootLocalRow1");
    private static readonly int CrowdRootLocalRow2Id = Shader.PropertyToID("_CrowdRootLocalRow2");

    private static readonly int InstanceCountId = Shader.PropertyToID("_InstanceCount");
    private static readonly int PlaybackTimeId = Shader.PropertyToID("_PlaybackTime");
    private static readonly int BasePlaybackSpeedId = Shader.PropertyToID("_BasePlaybackSpeed");
    private static readonly int ClipStartFrameId = Shader.PropertyToID("_ClipStartFrame");
    private static readonly int ClipFrameCountId = Shader.PropertyToID("_ClipFrameCount");
    private static readonly int ClipLengthId = Shader.PropertyToID("_ClipLength");
    private static readonly int ClipLoopId = Shader.PropertyToID("_ClipLoop");
    private static readonly int RootPositionId = Shader.PropertyToID("_RootPosition");
    private static readonly int RootRightId = Shader.PropertyToID("_RootRight");
    private static readonly int RootUpId = Shader.PropertyToID("_RootUp");
    private static readonly int RootForwardId = Shader.PropertyToID("_RootForward");
    private static readonly int SpawnDataId = Shader.PropertyToID("_SpawnData");
    private static readonly int SimulationReadBufferId = Shader.PropertyToID("_SimulationReadBuffer");
    private static readonly int SimulationWriteBufferId = Shader.PropertyToID("_SimulationWriteBuffer");
    private static readonly int ActiveStateBufferId = Shader.PropertyToID("_ActiveStateBuffer");
    private static readonly int InteractionSphereBufferId = Shader.PropertyToID("_InteractionSphereBuffer");
    private static readonly int InteractionSphereCountId = Shader.PropertyToID("_InteractionSphereCount");
    private static readonly int GridCounterBufferId = Shader.PropertyToID("_GridCounterBuffer");
    private static readonly int GridOccupantBufferId = Shader.PropertyToID("_GridOccupantBuffer");
    private static readonly int SpatialElementBufferId = Shader.PropertyToID("_SpatialElementBuffer");
    private static readonly int SpatialQueryBufferId = Shader.PropertyToID("_SpatialQueryBuffer");
    private static readonly int SpatialQueryResultBufferId = Shader.PropertyToID("_SpatialQueryResultBuffer");
    private static readonly int SpatialQueryHitBufferId = Shader.PropertyToID("_SpatialQueryHitBuffer");
    private static readonly int SpatialQueryCountId = Shader.PropertyToID("_SpatialQueryCount");
    private static readonly int MaxSpatialQueryHitsId = Shader.PropertyToID("_MaxSpatialQueryHits");
    private static readonly int GridBuildModeId = Shader.PropertyToID("_GridBuildMode");
    private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
    private static readonly int EnableApproximateCollisionId = Shader.PropertyToID("_EnableApproximateCollision");
    private static readonly int UseActiveBubbleId = Shader.PropertyToID("_UseActiveBubble");
    private static readonly int ActiveBubbleCenterId = Shader.PropertyToID("_ActiveBubbleCenter");
    private static readonly int ActiveBubbleRadiusId = Shader.PropertyToID("_ActiveBubbleRadius");
    private static readonly int ActiveBubbleRetentionRadiusId = Shader.PropertyToID("_ActiveBubbleRetentionRadius");
    private static readonly int GridDimId = Shader.PropertyToID("_GridDim");
    private static readonly int GridCellCountId = Shader.PropertyToID("_GridCellCount");
    private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
    private static readonly int InvCellSizeId = Shader.PropertyToID("_InvCellSize");
    private static readonly int GridMinXZId = Shader.PropertyToID("_GridMinXZ");
    private static readonly int GridMaxXZId = Shader.PropertyToID("_GridMaxXZ");
    private static readonly int MaxCellOccupancyId = Shader.PropertyToID("_MaxCellOccupancy");
    private static readonly int CollisionRadiusId = Shader.PropertyToID("_CollisionRadius");
    private static readonly int CapsuleHeightId = Shader.PropertyToID("_CapsuleHeight");
    private static readonly int MaxSpatialQueryElementRadiusId = Shader.PropertyToID("_MaxSpatialQueryElementRadius");
    private static readonly int AnchorStiffnessId = Shader.PropertyToID("_AnchorStiffness");
    private static readonly int VelocityDampingId = Shader.PropertyToID("_VelocityDamping");
    private static readonly int SelfCollisionStrengthId = Shader.PropertyToID("_SelfCollisionStrength");
    private static readonly int MaxPushPerStepId = Shader.PropertyToID("_MaxPushPerStep");
    private static readonly int MaxDisplacementFromSpawnId = Shader.PropertyToID("_MaxDisplacementFromSpawn");
    private static readonly int InactiveReturnStrengthId = Shader.PropertyToID("_InactiveReturnStrength");
    private static readonly int EnableTerrainCollisionId = Shader.PropertyToID("_EnableTerrainCollision");
    private static readonly int TerrainPositionId = Shader.PropertyToID("_TerrainPosition");
    private static readonly int TerrainSizeId = Shader.PropertyToID("_TerrainSize");
    private static readonly int TerrainHeightOffsetId = Shader.PropertyToID("_TerrainHeightOffset");
    private static readonly int TerrainHeightmapId = Shader.PropertyToID("_TerrainHeightmap");
    private static readonly int EnableStaticSdfCollisionId = Shader.PropertyToID("_EnableStaticSdfCollision");
    private static readonly int StaticSdfTextureId = Shader.PropertyToID("_StaticSdfTexture");
    private static readonly int StaticSdfWorldCenterId = Shader.PropertyToID("_StaticSdfWorldCenter");
    private static readonly int StaticSdfWorldSizeId = Shader.PropertyToID("_StaticSdfWorldSize");
    private static readonly int StaticSdfDistanceScaleId = Shader.PropertyToID("_StaticSdfDistanceScale");
    private static readonly int StaticSdfDistanceBiasId = Shader.PropertyToID("_StaticSdfDistanceBias");
    private static readonly int CapsulePbdSampleCountId = Shader.PropertyToID("_CapsulePbdSampleCount");
    private static readonly int WorldToLocalRow0Id = Shader.PropertyToID("_WorldToLocalRow0");
    private static readonly int WorldToLocalRow1Id = Shader.PropertyToID("_WorldToLocalRow1");
    private static readonly int WorldToLocalRow2Id = Shader.PropertyToID("_WorldToLocalRow2");

    private const string DefaultShaderName = "Project/Crowd/VATIndirectLit";
    private const string PredictKernelName = "PredictInstances";
    private const string ClearGridKernelName = "ClearGrid";
    private const string BuildGridKernelName = "BuildGrid";
    private const string SolveCrowdKernelName = "SolveCrowdCollisions";
    private const string ClearSpatialQueriesKernelName = "ClearSpatialQueryResults";
    private const string ResolveSpatialQueriesKernelName = "ResolveSpatialQueries";
    private const string FinalizeKernelName = "FinalizeInstances";
    private const int ThreadGroupSize = 64;
    private const int SpatialQueryThreadGroupSize = 8;
    private const int GridBuildModeActiveOnly = 0;
    private const int GridBuildModeAllQueryables = 1;
    private const int DebugCharacterInteractionQueryId = -10001;
    private const int DebugCustomInteractionQueryIdBase = -11000;
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceSpawnData
    {
        public Vector3 localPosition;
        public float yawRadians;
        public float uniformScale;
        public float normalizedTimeOffset;
        public float playbackSpeedMultiplier;
        public uint faction;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceSimulationState
    {
        public Vector4 localPositionAndYaw;
        public Vector4 scaleAndVelocity;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct InteractionSphereData
    {
        public Vector4 localCenterAndRadius;
        public Vector4 parameters;
    }

    [Serializable]
    private struct InteractionSphere
    {
        public Transform target;
        public float radius;
        public float strength;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct MatrixRows
    {
        public Vector4 row0;
        public Vector4 row1;
        public Vector4 row2;
    }

    private struct RenderChunk
    {
        public uint[] instanceIndices;
        public Bounds localBounds;
    }

    private struct SpawnAreaContext
    {
        public Vector2 size;
        public Vector2 centerXZ;
    }

    private struct RuntimeRenderResource
    {
        public CrowdVatAnimationAsset animationAsset;
        public CrowdVatPlayer templatePrefab;
        public Mesh mesh;
        public Material[] runtimeMaterials;
        public Vector4 crowdRootRow0;
        public Vector4 crowdRootRow1;
        public Vector4 crowdRootRow2;
        public Bounds agentLocalBounds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpatialElementData
    {
        public Vector4 localCapsuleStartAndRadius;
        public Vector4 localCapsuleEndAndHeight;
        public uint ownerIndex;
        public uint targetMask;
        public uint flags;
        public uint faction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpatialQueryData
    {
        public Vector4 localStartAndRadius;
        public Vector4 localEndAndPadding;
        public uint queryId;
        public uint targetMask;
        public uint factionMask;
        public uint shape;
        public uint flags;
        public uint maxHits;
        public uint padding0;
        public uint padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpatialQueryResultData
    {
        public uint queryId;
        public uint totalHits;
        public uint writtenHits;
        public uint overflow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpatialQueryHitData
    {
        public uint queryId;
        public uint ownerIndex;
        public uint targetMask;
        public uint flags;
        public uint faction;
        public float normalizedDistance;
        public uint padding0;
        public uint padding1;
    }

    [Header("资源引用")]
    [Tooltip("推荐直接拖入已生成的 Qianxia crowd VAT prefab 根节点，用来复用动画资产、材质来源和渲染根节点偏移。")]
    [SerializeField] private CrowdVatPlayer _templatePrefab;
    [Tooltip("也可以直接指定动画资产；如果同时指定了模板 prefab，会优先使用模板上的资源。")]
    [SerializeField] private CrowdVatAnimationAsset _animationAsset;
    [Tooltip("负责写入实例矩阵、动画帧和近场求解状态的 Compute Shader。")]
    [SerializeField] private ComputeShader _updateCompute;
    [Tooltip("Indirect 版本的人群 Shader。留空时会尝试按默认名字查找。")]
    [SerializeField] private Shader _indirectShader;
    [Tooltip("可选的 indirect 材质模板；留空时运行时会复制一份默认材质。")]
    [SerializeField] private Material _indirectMaterialTemplate;
    [Tooltip("默认播放的 clip 名称。留空时回退到动画资产里的第一个 clip。")]
    [SerializeField] private string _clipName;

    [Header("实例布局")]
    [InspectorName("实例数量")]
    [Tooltip("人群实例总数。双阵营模式下会自动平均拆到 A / B 两边。")]
    [SerializeField] [Min(1)] private int _instanceCount = 1024;
    [InspectorName("铺满整个地形")]
    [Tooltip("启用后，如果存在可用 Terrain，则出生点会分布到整个地形范围，而不是只落在局部矩形区域。")]
    [SerializeField] private bool _distributeAcrossWholeTerrain = true;
    [InspectorName("出生区域尺寸")]
    [Tooltip("单阵营模式下的整体出生区域尺寸；双阵营模式下会在此基础上继续拆分给两边。X 对应宽度，Y 对应深度。")]
    [SerializeField] private Vector2 _areaSize = new Vector2(48.0f, 48.0f);
    [InspectorName("出生扰动")]
    [Tooltip("出生点在各自网格单元内的随机扰动强度。0 表示整齐排布，1 表示尽量打散。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _cellJitter = 0.7f;
    [InspectorName("基础高度偏移")]
    [Tooltip("出生点基础高度偏移。若开启地形贴地，会在生成后再根据地形重新修正。")]
    [SerializeField] private float _heightOffset;
    [InspectorName("随机种子")]
    [Tooltip("出生布局随机种子。固定后可以稳定复现同一套人群分布。")]
    [SerializeField] private int _randomSeed = 12345;

    [Header("地形贴地 / 碰撞")]
    [Tooltip("启用后，人群会按 Terrain 高度图把脚底吸附回地表，避免角色和地形穿模。")]
    [SerializeField] private bool _enableTerrainCollision = true;
    [Tooltip("未手动指定 Terrain 时，自动使用场景里的 Terrain 作为贴地来源。")]
    [SerializeField] private bool _autoResolveTerrain = true;
    [Tooltip("用于人群贴地的 Terrain。留空时会自动查找。")]
    [SerializeField] private Terrain _terrain;
    [Tooltip("贴地后额外抬高一点点，避免脚底和地表完全重合。")]
    [SerializeField] private float _terrainHeightOffset = 0.02f;
    [Tooltip("启用后，额外使用 3D static SDF 体积参与 capsule PBD 碰撞投影。")]
    [SerializeField] private bool _enableStaticSdfCollision;
    [Tooltip("静态场景的 3D SDF 贴图。当前按轴对齐世界体积采样。")]
    [SerializeField] private Texture3D _staticSdfTexture;
    [Tooltip("static SDF 体积的世界空间中心。")]
    [SerializeField] private Vector3 _staticSdfWorldCenter = new Vector3(0.0f, 1.0f, 0.0f);
    [Tooltip("static SDF 体积的世界空间尺寸。")]
    [SerializeField] private Vector3 _staticSdfWorldSize = new Vector3(8.0f, 4.0f, 8.0f);
    [Tooltip("SDF 采样值到世界距离的缩放。默认假设贴图里直接存世界单位距离。")]
    [SerializeField] private float _staticSdfDistanceScale = 1.0f;
    [Tooltip("SDF 采样值到世界距离的偏移。可用于适配非零等值面的距离场。")]
    [SerializeField] private float _staticSdfDistanceBias = 0.0f;

    [Header("阵营布局")]
    [InspectorName("阵营布局模式")]
    [Tooltip("双阵营模式会把人群自动切成 A / B 两边，作为后续射击、索敌和阵营判定的基础。")]
    [SerializeField] private CrowdVatFactionLayoutMode _factionLayout = CrowdVatFactionLayoutMode.TwoOpposingFactions;
    [InspectorName("阵营分割轴")]
    [Tooltip("Depth 表示沿 Z 轴前后对阵，Width 表示沿 X 轴左右对阵。")]
    [SerializeField] private CrowdVatFactionSplitAxis _factionSplitAxis = CrowdVatFactionSplitAxis.Depth;
    [InspectorName("阵营中线间隔")]
    [Tooltip("两个阵营中心之间的中线宽度，后续可以直接拿来放掩体或留出交战走廊。")]
    [SerializeField] [Min(0.0f)] private float _factionCenterGap = 6.0f;
    [InspectorName("朝向敌方阵营")]
    [Tooltip("开启后，两边默认朝向对方阵营，便于后续直接接射击弹道与目标判定。")]
    [SerializeField] private bool _orientTowardEnemyFaction = true;
    [InspectorName("手动指定双方中心")]
    [Tooltip("启用后，CampA / CampB 的位置直接由下面两个中心点控制，不再由 Split Axis 和 Center Gap 自动推导。")]
    [SerializeField] private bool _useExplicitFactionCenters;
    [InspectorName("CampA 中心 XZ")]
    [Tooltip("CampA 的中心点，使用 CrowdVatIndirectRenderer 本地空间的 XZ 坐标。X 表示左右，Y 表示前后(Z)。")]
    [SerializeField] private Vector2 _campACenterXZ = new Vector2(0.0f, -6.0f);
    [InspectorName("CampB 中心 XZ")]
    [Tooltip("CampB 的中心点，使用 CrowdVatIndirectRenderer 本地空间的 XZ 坐标。X 表示左右，Y 表示前后(Z)。")]
    [SerializeField] private Vector2 _campBCenterXZ = new Vector2(0.0f, 6.0f);
    [InspectorName("使用空物体控制阵营")]
    [Tooltip("启用后，CampA / CampB 的中心和范围优先由控制空物体与四个角点空物体驱动。")]
    [SerializeField] private bool _useFactionControlTransforms = true;
    [InspectorName("CampA 控制空物体")]
    [Tooltip("推荐使用 CrowdVatIndirectRenderer 的子物体。直接移动它即可调整 CampA 中心，展开后拖四个角点即可修改范围。")]
    [SerializeField] private Transform _campAControlTransform;
    [InspectorName("CampB 控制空物体")]
    [Tooltip("推荐使用 CrowdVatIndirectRenderer 的子物体。直接移动它即可调整 CampB 中心，展开后拖四个角点即可修改范围。")]
    [SerializeField] private Transform _campBControlTransform;
    [SerializeField] [HideInInspector] private bool _useExplicitFactionAreaSizes;
    [SerializeField] [HideInInspector] private Vector2 _campAAreaSize = new Vector2(48.0f, 21.0f);
    [SerializeField] [HideInInspector] private Vector2 _campBAreaSize = new Vector2(48.0f, 21.0f);
    [SerializeField] [HideInInspector] private Transform _campANorthWestCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campANorthEastCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campASouthEastCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campASouthWestCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campBNorthWestCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campBNorthEastCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campBSouthEastCornerTransform;
    [SerializeField] [HideInInspector] private Transform _campBSouthWestCornerTransform;
    [Header("角色尺度")]
    [Tooltip("所有角色的基础统一缩放。")]
    [SerializeField] [Min(0.01f)] private float _baseScale = 1.0f;
    [Tooltip("单个角色在基础缩放上的随机波动范围，用来打散整齐感。")]
    [SerializeField] private Vector2 _scaleMultiplierRange = new Vector2(0.95f, 1.05f);

    [Header("动画随机化")]
    [Tooltip("组件启用时自动开始播放默认动画。")]
    [SerializeField] private bool _playOnEnable = true;
    [Tooltip("是否强制把当前 clip 当作循环动画播放。")]
    [SerializeField] private bool _loopOverride = true;
    [Tooltip("整个人群的基础播放速度。")]
    [SerializeField] [Min(0.0f)] private float _playbackSpeed = 1.0f;
    [Tooltip("单个角色播放速度的随机范围。")]
    [SerializeField] private Vector2 _playbackSpeedMultiplierRange = new Vector2(0.9f, 1.1f);
    [Tooltip("起始帧时间偏移的随机强度。1 表示尽量打散不同角色的动画相位。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _normalizedStartOffsetRandom = 1.0f;

    [Header("空间查询 / PBD 近似")]
    [SerializeField] private bool _enableApproximateCollision = true;
    [SerializeField] [Min(0.01f)] private float _collisionRadius = 0.35f;
    [SerializeField] [Min(0.02f)] private float _collisionHeight = 1.65f;
    [SerializeField] [Min(0.05f)] private float _queryCellSize = 0.8f;
    [SerializeField] [Min(4)] private int _maxCellOccupancy = 24;
    [SerializeField] [Min(1)] private int _maxSpatialQueries = 32;
    [SerializeField] [Min(1)] private int _maxHitsPerSpatialQuery = 32;
    [SerializeField] [Min(1)] private int _solverIterations = 2;
    [SerializeField] [Range(2, 8)] private int _capsulePbdSampleCount = 4;
    [SerializeField] [Range(0.0f, 1.0f)] private float _selfCollisionStrength = 0.9f;
    [SerializeField] [Range(0.0f, 1.0f)] private float _anchorStiffness = 0.08f;
    [SerializeField] [Range(0.0f, 1.0f)] private float _velocityDamping = 0.82f;
    [SerializeField] [Min(0.01f)] private float _maxPushPerStep = 0.18f;
    [SerializeField] [Min(0.01f)] private float _maxDisplacementFromSpawn = 1.25f;
    [SerializeField] [Range(0.0f, 1.0f)] private float _inactiveReturnStrength = 0.22f;

    [Header("Active Bubble / 玩家驱散")]
    [SerializeField] private bool _enableActiveBubble = true;
    [SerializeField] private bool _autoResolveCharacterController = true;
    [SerializeField] private CharacterController _characterController;
    [SerializeField] private Transform _activeBubbleTarget;
    [SerializeField] private string _preferredCharacterName = "QianxiaThirdPerson";
    [SerializeField] [Min(0.1f)] private float _activeBubbleRadius = 14.0f;
    [SerializeField] [Min(0.1f)] private float _activeBubbleRetentionRadius = 18.0f;
    [SerializeField] private bool _useCharacterAsInteractionSphere = true;
    [SerializeField] [Min(0.1f)] private float _characterInteractionRadiusMultiplier = 2.2f;
    [SerializeField] [Min(0.0f)] private float _characterInteractionStrength = 0.14f;
    [Tooltip("可选的近场驱散源。第一版先用球体近似，只影响人群的 XZ 平面位置。")]
    [SerializeField] private InteractionSphere[] _interactionSpheres = Array.Empty<InteractionSphere>();

    [Header("模板渲染根节点")]
    [SerializeField] private Vector3 _meshRootLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 _meshRootLocalEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 _meshRootLocalScale = Vector3.one;

    [Header("渲染")]
    [SerializeField] private ShadowCastingMode _shadowCastingMode = ShadowCastingMode.On;
    [SerializeField] private bool _receiveShadows = true;
    [SerializeField] [Min(0.0f)] private float _boundsPadding = 2.0f;
    [SerializeField] private bool _enableFrustumCulling = true;
    [SerializeField] [Min(0.5f)] private float _renderChunkWorldSize = 8.0f;

    private ComputeBuffer _spawnDataBuffer;
    private ComputeBuffer _simulationStateBufferA;
    private ComputeBuffer _simulationStateBufferB;
    private ComputeBuffer _simulationReadBuffer;
    private ComputeBuffer _simulationWriteBuffer;
    private ComputeBuffer _activeStateBuffer;
    private ComputeBuffer _gridCounterBuffer;
    private ComputeBuffer _gridOccupantBuffer;
    private ComputeBuffer _spatialElementBuffer;
    private ComputeBuffer _spatialQueryBuffer;
    private ComputeBuffer _spatialQueryResultBuffer;
    private ComputeBuffer _spatialQueryHitBuffer;
    private ComputeBuffer _interactionSphereBuffer;
    private ComputeBuffer _instanceTransformBuffer;
    private ComputeBuffer _instanceFrameDataBuffer;
    private ComputeBuffer _visibleInstanceIndexBuffer;
    private GraphicsBuffer[] _indirectArgsBuffers = Array.Empty<GraphicsBuffer>();
    private RuntimeRenderResource _runtimeRenderResource;
    private bool _hasRuntimeRenderResource;
    private Bounds _localCrowdBounds = new Bounds(Vector3.zero, Vector3.one);
    private RenderChunk[] _renderChunks = Array.Empty<RenderChunk>();
    private GraphicsBuffer.IndirectDrawIndexedArgs[] _indirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
    private CrowdVatAnimationAsset.ClipInfo _currentClip;
    private InteractionSphereData[] _interactionSphereUploadCache = Array.Empty<InteractionSphereData>();
    private CrowdVatSpatialQueryRequest[] _runtimeSpatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
    private SpatialQueryData[] _spatialQueryUploadCache = Array.Empty<SpatialQueryData>();
    private SpatialQueryResultData[] _spatialQueryResultCache = Array.Empty<SpatialQueryResultData>();
    private SpatialQueryHitData[] _spatialQueryHitCache = Array.Empty<SpatialQueryHitData>();
    private uint[] _visibleInstanceIndexCache = Array.Empty<uint>();
    private Texture _resolvedTerrainHeightmap;
    private Texture3D _resolvedStaticSdfTexture;
    private Texture3D _fallbackStaticSdfTexture;
    private Vector2 _gridMinXZ;
    private Vector2 _gridMaxXZ;
    private Vector2Int _gridDimensions;
    private Transform _resolvedActiveBubbleTarget;
    private bool _hasClip;
    private bool _isPlaying;
    private bool _resourcesDirty = true;
    private float _playbackTime;
    private float _minimumPlaybackSpeedMultiplier = 1.0f;
    private float _maximumQueryAgentRadius = 0.35f;
    private bool _hasResolvedTerrainCollision;
    private bool _hasResolvedStaticSdfCollision;
    private int _gridCellCount;
    private Bounds _visibleWorldBounds;
    private bool _hasVisibleBounds;
    private int _visibleInstanceCount;
    private readonly Plane[] _frustumPlanes = new Plane[6];
    private int _predictKernel = -1;
    private int _clearGridKernel = -1;
    private int _buildGridKernel = -1;
    private int _solveCrowdKernel = -1;
    private int _clearSpatialQueriesKernel = -1;
    private int _resolveSpatialQueriesKernel = -1;
    private int _finalizeKernel = -1;
    private int _activeSpatialQueryCount;

    private void Reset()
    {
        SyncTemplateBindings();
        MarkResourcesDirty();
    }

    private void Awake()
    {
        SyncTemplateBindings();
        InitializeIfNeeded();
    }

    private void OnEnable()
    {
        SubscribeRenderCallbacks();
        SyncTemplateBindings();
        InitializeIfNeeded();

        if (_playOnEnable)
            PlayClip(_clipName);
    }

    private void LateUpdate()
    {
        if (!InitializeIfNeeded())
            return;

        AdvancePlayback(Time.deltaTime);
        UpdateGpuBuffers();
    }

    private void OnDisable()
    {
        UnsubscribeRenderCallbacks();
        ReleaseResources();
    }

    private void OnDestroy()
    {
        UnsubscribeRenderCallbacks();
        ReleaseResources();
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!ShouldRenderForCamera(camera) || !InitializeIfNeeded())
            return;

        if (!Application.isPlaying)
            UpdateGpuBuffers();

        Draw(camera);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_enableActiveBubble || !TryGetActiveBubbleLocalCenter(out Vector3 localCenter))
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.22f, 0.85f, 1.0f, 0.55f);
        Gizmos.DrawWireSphere(localCenter, _activeBubbleRadius);
        Gizmos.color = new Color(0.12f, 0.55f, 1.0f, 0.35f);
        Gizmos.DrawWireSphere(localCenter, _activeBubbleRetentionRadius);
        Gizmos.matrix = previousMatrix;
    }

    private void OnValidate()
    {
        _instanceCount = Mathf.Max(1, _instanceCount);
        _areaSize.x = Mathf.Max(0.01f, _areaSize.x);
        _areaSize.y = Mathf.Max(0.01f, _areaSize.y);
        _campAAreaSize.x = Mathf.Max(0.01f, _campAAreaSize.x);
        _campAAreaSize.y = Mathf.Max(0.01f, _campAAreaSize.y);
        _campBAreaSize.x = Mathf.Max(0.01f, _campBAreaSize.x);
        _campBAreaSize.y = Mathf.Max(0.01f, _campBAreaSize.y);
        _baseScale = Mathf.Max(0.01f, _baseScale);
        _scaleMultiplierRange.x = Mathf.Max(0.01f, _scaleMultiplierRange.x);
        _scaleMultiplierRange.y = Mathf.Max(_scaleMultiplierRange.x, _scaleMultiplierRange.y);
        _playbackSpeedMultiplierRange.x = Mathf.Max(0.01f, _playbackSpeedMultiplierRange.x);
        _playbackSpeedMultiplierRange.y = Mathf.Max(_playbackSpeedMultiplierRange.x, _playbackSpeedMultiplierRange.y);
        _collisionRadius = Mathf.Max(0.01f, _collisionRadius);
        _collisionHeight = Mathf.Max(_collisionRadius * 2.0f, _collisionHeight);
        _staticSdfWorldSize.x = Mathf.Max(0.01f, _staticSdfWorldSize.x);
        _staticSdfWorldSize.y = Mathf.Max(0.01f, _staticSdfWorldSize.y);
        _staticSdfWorldSize.z = Mathf.Max(0.01f, _staticSdfWorldSize.z);
        _queryCellSize = Mathf.Max(0.05f, _queryCellSize);
        _maxCellOccupancy = Mathf.Max(4, _maxCellOccupancy);
        _maxSpatialQueries = Mathf.Max(1, _maxSpatialQueries);
        _maxHitsPerSpatialQuery = Mathf.Max(1, _maxHitsPerSpatialQuery);
        _solverIterations = Mathf.Max(1, _solverIterations);
        _capsulePbdSampleCount = Mathf.Clamp(_capsulePbdSampleCount, 2, 8);
        _maxPushPerStep = Mathf.Max(0.01f, _maxPushPerStep);
        _maxDisplacementFromSpawn = Mathf.Max(0.01f, _maxDisplacementFromSpawn);
        _factionCenterGap = Mathf.Max(0.0f, _factionCenterGap);
        _inactiveReturnStrength = Mathf.Clamp01(_inactiveReturnStrength);
        _activeBubbleRadius = Mathf.Max(0.1f, _activeBubbleRadius);
        _activeBubbleRetentionRadius = Mathf.Max(_activeBubbleRadius, _activeBubbleRetentionRadius);
        _characterInteractionRadiusMultiplier = Mathf.Max(0.1f, _characterInteractionRadiusMultiplier);
        _characterInteractionStrength = Mathf.Max(0.0f, _characterInteractionStrength);
        _meshRootLocalScale.x = Mathf.Max(0.001f, _meshRootLocalScale.x);
        _meshRootLocalScale.y = Mathf.Max(0.001f, _meshRootLocalScale.y);
        _meshRootLocalScale.z = Mathf.Max(0.001f, _meshRootLocalScale.z);
        _renderChunkWorldSize = Mathf.Max(0.5f, _renderChunkWorldSize);
        SyncExplicitFactionAreasFromControlTransforms();
        SyncExplicitFactionCentersFromControlTransforms();

        SyncTemplateBindings();
        MarkResourcesDirty();
    }

    [ContextMenu("Rebuild Crowd Layout")]
    public void RebuildCrowdLayout()
    {
        MarkResourcesDirty();
        InitializeIfNeeded();
    }

    public bool UseFactionControlTransforms => _useFactionControlTransforms;

    public CrowdVatFactionLayoutMode FactionLayoutMode => _factionLayout;

    public Transform CampAControlTransform => _campAControlTransform;

    public Transform CampBControlTransform => _campBControlTransform;

    public bool TryGetDebugSpawnArea(out Vector2 centerXZ, out Vector2 sizeXZ)
    {
        SpawnAreaContext spawnArea = ResolveSpawnAreaContext();
        centerXZ = spawnArea.centerXZ;
        sizeXZ = spawnArea.size;
        return sizeXZ.x > 0.0f && sizeXZ.y > 0.0f;
    }

    public bool TryGetDebugFactionArea(CrowdVatFaction faction, out Vector2 centerXZ, out Vector2 sizeXZ)
    {
        centerXZ = Vector2.zero;
        sizeXZ = Vector2.zero;
        if (_factionLayout != CrowdVatFactionLayoutMode.TwoOpposingFactions)
            return false;

        SpawnAreaContext spawnArea = ResolveSpawnAreaContext();
        centerXZ = GetFactionCenterXZ(spawnArea, faction);
        sizeXZ = GetFactionAreaSize(spawnArea.size, faction);
        return sizeXZ.x > 0.0f && sizeXZ.y > 0.0f;
    }

    public bool TryGetDebugGridBounds(out Vector2 minXZ, out Vector2 maxXZ, out Vector2Int dimensions)
    {
        return TryGetDebugGridBounds(out minXZ, out maxXZ, out dimensions, out _);
    }

    public bool TryGetDebugGridBounds(out Vector2 minXZ, out Vector2 maxXZ, out Vector2Int dimensions, out float cellSize)
    {
        minXZ = _gridMinXZ;
        maxXZ = _gridMaxXZ;
        dimensions = _gridDimensions;
        cellSize = _queryCellSize;
        return _gridCellCount > 0 && dimensions.x > 0 && dimensions.y > 0 && cellSize > 0.0f;
    }

    public bool TryGetDebugActiveBubble(out Vector3 worldCenter, out float radius, out float retentionRadius)
    {
        worldCenter = Vector3.zero;
        radius = 0.0f;
        retentionRadius = 0.0f;
        if (!_enableActiveBubble || !TryGetActiveBubbleLocalCenter(out Vector3 localCenter))
            return false;

        worldCenter = transform.TransformPoint(localCenter);
        radius = _activeBubbleRadius;
        retentionRadius = _activeBubbleRetentionRadius;
        return true;
    }

    public int GetDebugSpatialQueries(List<CrowdVatSpatialQueryRequest> queries)
    {
        if (queries == null)
            throw new ArgumentNullException(nameof(queries));

        queries.Clear();

        AppendDebugInteractionSphereQueries(queries);

        if (_activeSpatialQueryCount <= 0 || _runtimeSpatialQueries == null)
            return queries.Count;

        int count = Mathf.Min(_activeSpatialQueryCount, _runtimeSpatialQueries.Length);
        for (int index = 0; index < count; index++)
            queries.Add(_runtimeSpatialQueries[index]);

        return queries.Count;
    }

    public void SetFactionControlTransforms(Transform campAControlTransform, Transform campBControlTransform)
    {
        _campAControlTransform = campAControlTransform;
        _campBControlTransform = campBControlTransform;
        _useFactionControlTransforms = _campAControlTransform != null || _campBControlTransform != null;
        SyncExplicitFactionCentersFromControlTransforms();
    }

    public void SetFactionRangeControlTransforms(
        Transform campANorthWestCornerTransform,
        Transform campANorthEastCornerTransform,
        Transform campASouthEastCornerTransform,
        Transform campASouthWestCornerTransform,
        Transform campBNorthWestCornerTransform,
        Transform campBNorthEastCornerTransform,
        Transform campBSouthEastCornerTransform,
        Transform campBSouthWestCornerTransform)
    {
        _campANorthWestCornerTransform = campANorthWestCornerTransform;
        _campANorthEastCornerTransform = campANorthEastCornerTransform;
        _campASouthEastCornerTransform = campASouthEastCornerTransform;
        _campASouthWestCornerTransform = campASouthWestCornerTransform;
        _campBNorthWestCornerTransform = campBNorthWestCornerTransform;
        _campBNorthEastCornerTransform = campBNorthEastCornerTransform;
        _campBSouthEastCornerTransform = campBSouthEastCornerTransform;
        _campBSouthWestCornerTransform = campBSouthWestCornerTransform;
        SyncExplicitFactionAreasFromControlTransforms();
    }

    public void SyncExplicitFactionCentersFromControlTransforms()
    {
        bool updated = false;
        if (TryGetFactionControlCenterXZ(CrowdVatFaction.CampA, out Vector2 campACenter))
        {
            _campACenterXZ = campACenter;
            updated = true;
        }
        else if (TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction.CampA, out Vector2 campARangeCenter, out _))
        {
            _campACenterXZ = campARangeCenter;
            updated = true;
        }

        if (TryGetFactionControlCenterXZ(CrowdVatFaction.CampB, out Vector2 campBCenter))
        {
            _campBCenterXZ = campBCenter;
            updated = true;
        }
        else if (TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction.CampB, out Vector2 campBRangeCenter, out _))
        {
            _campBCenterXZ = campBRangeCenter;
            updated = true;
        }

        if (updated)
            _useExplicitFactionCenters = true;
    }

    public void SyncExplicitFactionAreasFromControlTransforms()
    {
        bool updated = false;
        if (TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction.CampA, out _, out Vector2 campAAreaSize))
        {
            _campAAreaSize = campAAreaSize;
            updated = true;
        }

        if (TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction.CampB, out _, out Vector2 campBAreaSize))
        {
            _campBAreaSize = campBAreaSize;
            updated = true;
        }

        if (updated)
            _useExplicitFactionAreaSizes = true;
    }

    public void SyncFactionControlTransformsFromExplicitCenters()
    {
        SyncFactionControlTransform(_campAControlTransform, _campACenterXZ);
        SyncFactionControlTransform(_campBControlTransform, _campBCenterXZ);
    }

    public void SyncFactionRangeControlTransformsFromExplicitAreas()
    {
        SyncFactionRangeCornerTransforms(
            _campAControlTransform,
            _campACenterXZ,
            _campAAreaSize,
            _campANorthWestCornerTransform,
            _campANorthEastCornerTransform,
            _campASouthEastCornerTransform,
            _campASouthWestCornerTransform);
        SyncFactionRangeCornerTransforms(
            _campBControlTransform,
            _campBCenterXZ,
            _campBAreaSize,
            _campBNorthWestCornerTransform,
            _campBNorthEastCornerTransform,
            _campBSouthEastCornerTransform,
            _campBSouthWestCornerTransform);
    }

    public void NotifyFactionControlTransformsChanged()
    {
        SyncExplicitFactionCentersFromControlTransforms();
        SyncExplicitFactionAreasFromControlTransforms();
        SyncFactionControlTransformsFromExplicitCenters();
        SyncFactionRangeControlTransformsFromExplicitAreas();
        MarkFactionControlStateDirtyAndRebuild();
    }

    public void NotifyFactionControlPointChanged(CrowdVatFaction faction, CrowdVatFactionControlPointKind kind, Transform controlPointTransform)
    {
        if (kind == CrowdVatFactionControlPointKind.Center)
        {
            NotifyFactionControlTransformsChanged();
            return;
        }

        SyncExplicitFactionCentersFromControlTransforms();
        if (!TrySyncExplicitFactionAreaFromMovedCorner(faction, controlPointTransform))
            SyncExplicitFactionAreasFromControlTransforms();

        SyncFactionControlTransformsFromExplicitCenters();
        SyncFactionRangeControlTransformsFromExplicitAreas();
        MarkFactionControlStateDirtyAndRebuild();
    }

    private void MarkFactionControlStateDirtyAndRebuild()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        RebuildCrowdLayout();
    }

    public void PlayClip(string clipName)
    {
        SyncTemplateBindings();
        if (!TryResolveClip(clipName, out _currentClip))
        {
            _hasClip = false;
            return;
        }

        _hasClip = true;
        _isPlaying = true;
        _playbackTime = 0.0f;
    }

    public void Stop()
    {
        _isPlaying = false;
    }

    public void Resume()
    {
        if (_hasClip)
            _isPlaying = true;
    }

    public void SetSpatialQueries(CrowdVatSpatialQueryRequest[] queries)
    {
        if (queries == null || queries.Length == 0)
        {
            _runtimeSpatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
            _activeSpatialQueryCount = 0;
            return;
        }

        int count = Mathf.Min(queries.Length, _maxSpatialQueries);
        if (_runtimeSpatialQueries == null || _runtimeSpatialQueries.Length != count)
            _runtimeSpatialQueries = new CrowdVatSpatialQueryRequest[count];

        Array.Copy(queries, _runtimeSpatialQueries, count);
        _activeSpatialQueryCount = count;
    }

    public void ClearSpatialQueries()
    {
        _runtimeSpatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
        _activeSpatialQueryCount = 0;
    }

    private void AppendDebugInteractionSphereQueries(List<CrowdVatSpatialQueryRequest> queries)
    {
        RefreshRuntimeTargets();

        if (_useCharacterAsInteractionSphere &&
            TryBuildCharacterInteractionDebugQuery(out CrowdVatSpatialQueryRequest characterQuery))
        {
            queries.Add(characterQuery);
        }

        if (_interactionSpheres == null)
            return;

        for (int sphereIndex = 0; sphereIndex < _interactionSpheres.Length; sphereIndex++)
        {
            if (TryBuildCustomInteractionDebugQuery(_interactionSpheres[sphereIndex], sphereIndex, out CrowdVatSpatialQueryRequest customQuery))
                queries.Add(customQuery);
        }
    }

    private bool TryBuildCharacterInteractionDebugQuery(out CrowdVatSpatialQueryRequest query)
    {
        query = default;
        if (_characterController == null || _characterInteractionStrength <= 0.0f)
            return false;

        Vector3 worldCenter = _characterController.transform.TransformPoint(_characterController.center);
        float radius = Mathf.Max(_characterController.radius, _characterController.height * 0.25f) * _characterInteractionRadiusMultiplier;
        query = CreateDebugSphereQuery(DebugCharacterInteractionQueryId, worldCenter, radius, CrowdVatSpatialQueryFlags.ActiveOnly);
        return true;
    }

    private bool TryBuildCustomInteractionDebugQuery(InteractionSphere sphere, int sphereIndex, out CrowdVatSpatialQueryRequest query)
    {
        query = default;
        if (sphere.target == null || sphere.radius <= 0.0f || sphere.strength <= 0.0f)
            return false;

        float radiusScale = MaxAbsComponent(sphere.target.lossyScale);
        float radius = sphere.radius * Mathf.Max(radiusScale, 0.0001f);
        query = CreateDebugSphereQuery(DebugCustomInteractionQueryIdBase - sphereIndex, sphere.target.position, radius, CrowdVatSpatialQueryFlags.None);
        return true;
    }

    private static CrowdVatSpatialQueryRequest CreateDebugSphereQuery(int queryId, Vector3 worldCenter, float radius, CrowdVatSpatialQueryFlags flags)
    {
        return new CrowdVatSpatialQueryRequest
        {
            queryId = queryId,
            targetMask = CrowdVatSpatialTargetMask.CrowdAgent,
            factionMask = CrowdVatFactionMask.All,
            shape = CrowdVatSpatialQueryShape.OverlapSphere,
            flags = flags,
            worldStart = worldCenter,
            worldEnd = worldCenter,
            radius = radius,
            maxHits = 0u
        };
    }

    public int ReadSpatialQueryResults(List<CrowdVatSpatialQueryResult> results, List<CrowdVatSpatialQueryHit> hits)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));
        if (hits == null)
            throw new ArgumentNullException(nameof(hits));

        results.Clear();
        hits.Clear();

        if (_activeSpatialQueryCount <= 0 ||
            _spatialQueryResultBuffer == null ||
            _spatialQueryHitBuffer == null)
        {
            return 0;
        }

        EnsureSpatialQueryReadbackCaches();
        _spatialQueryResultBuffer.GetData(_spatialQueryResultCache, 0, 0, _activeSpatialQueryCount);
        _spatialQueryHitBuffer.GetData(_spatialQueryHitCache, 0, 0, _activeSpatialQueryCount * _maxHitsPerSpatialQuery);

        int totalWrittenHits = 0;
        for (int queryIndex = 0; queryIndex < _activeSpatialQueryCount; queryIndex++)
        {
            SpatialQueryResultData resultData = _spatialQueryResultCache[queryIndex];
            int writtenHits = Mathf.Min((int)resultData.writtenHits, _maxHitsPerSpatialQuery);
            totalWrittenHits += writtenHits;

            results.Add(new CrowdVatSpatialQueryResult
            {
                queryId = (int)resultData.queryId,
                totalHits = (int)resultData.totalHits,
                writtenHits = writtenHits,
                overflow = resultData.overflow != 0u
            });

            int hitBaseIndex = queryIndex * _maxHitsPerSpatialQuery;
            List<CrowdVatSpatialQueryHit> sortedHits = new List<CrowdVatSpatialQueryHit>(writtenHits);
            for (int hitIndex = 0; hitIndex < writtenHits; hitIndex++)
            {
                SpatialQueryHitData hitData = _spatialQueryHitCache[hitBaseIndex + hitIndex];
                sortedHits.Add(new CrowdVatSpatialQueryHit
                {
                    queryId = (int)hitData.queryId,
                    ownerIndex = (int)hitData.ownerIndex,
                    targetMask = (CrowdVatSpatialTargetMask)hitData.targetMask,
                    flags = (CrowdVatSpatialQueryFlags)hitData.flags,
                    faction = (CrowdVatFaction)hitData.faction,
                    normalizedDistance = hitData.normalizedDistance
                });
            }

            sortedHits.Sort(static (left, right) => left.normalizedDistance.CompareTo(right.normalizedDistance));
            hits.AddRange(sortedHits);
        }

        return totalWrittenHits;
    }

    private bool InitializeIfNeeded()
    {
        RefreshRuntimeTargets();

        bool needsRebuild = _resourcesDirty || !AreBuffersReady();
        if (needsRebuild)
            ReleaseResources();

        if (!ResolveRuntimeRenderResource(out CrowdVatAnimationAsset primaryAnimationAsset))
            return false;

        if (_updateCompute == null)
            return false;

        Shader shader = ResolveIndirectShader();
        if (shader == null)
            return false;

        if (needsRebuild)
        {
            ResolveKernelHandles();
            BuildRuntimeMaterials(shader);
            BuildInstanceBuffers();
            BuildIndirectArgsBuffers();
            BindStaticResources();
            _resourcesDirty = false;
        }
        else if (_predictKernel < 0 ||
                 _clearGridKernel < 0 ||
                 _buildGridKernel < 0 ||
                 _solveCrowdKernel < 0 ||
                 _clearSpatialQueriesKernel < 0 ||
                 _resolveSpatialQueriesKernel < 0 ||
                 _finalizeKernel < 0)
        {
            ResolveKernelHandles();
        }

        if (!_hasClip && primaryAnimationAsset.TryGetClip(_clipName, out _currentClip))
            _hasClip = true;

        return _hasRuntimeRenderResource &&
            _visibleInstanceIndexBuffer != null &&
            _indirectArgsBuffers.Length > 0;
    }

    private Shader ResolveIndirectShader()
    {
        if (_indirectShader != null)
            return _indirectShader;

        _indirectShader = Shader.Find(DefaultShaderName);
        return _indirectShader;
    }

    private bool ResolveRuntimeRenderResource(out CrowdVatAnimationAsset primaryAnimationAsset)
    {
        CrowdVatAnimationAsset animationAsset = ResolveConfiguredAnimationAsset();
        CrowdVatPlayer templatePrefab = ResolveConfiguredTemplatePrefab();
        if (animationAsset == null || animationAsset.Mesh == null)
        {
            primaryAnimationAsset = null;
            _runtimeRenderResource = default;
            _hasRuntimeRenderResource = false;
            return false;
        }

        RuntimeRenderResource previousResource = _runtimeRenderResource;
        Material[] preservedRuntimeMaterials =
            previousResource.animationAsset == animationAsset &&
            previousResource.mesh == animationAsset.Mesh &&
            previousResource.templatePrefab == templatePrefab
                ? previousResource.runtimeMaterials
                : null;

        ResolveCrowdRootRows(templatePrefab, out Vector4 row0, out Vector4 row1, out Vector4 row2, out Matrix4x4 rootLocalMatrix);
        _runtimeRenderResource = new RuntimeRenderResource
        {
            animationAsset = animationAsset,
            templatePrefab = templatePrefab,
            mesh = animationAsset.Mesh,
            runtimeMaterials = preservedRuntimeMaterials,
            crowdRootRow0 = row0,
            crowdRootRow1 = row1,
            crowdRootRow2 = row2,
            agentLocalBounds = TransformBounds(rootLocalMatrix, animationAsset.MeshBounds)
        };
        _hasRuntimeRenderResource = true;
        primaryAnimationAsset = animationAsset;
        return true;
    }

    private CrowdVatPlayer ResolveConfiguredTemplatePrefab()
    {
        return _templatePrefab;
    }

    private CrowdVatAnimationAsset ResolveConfiguredAnimationAsset()
    {
        if (_templatePrefab != null && _templatePrefab.AnimationAsset != null)
            return _templatePrefab.AnimationAsset;

        return _animationAsset;
    }

    private void ResolveCrowdRootRows(
        CrowdVatPlayer templatePrefab,
        out Vector4 row0,
        out Vector4 row1,
        out Vector4 row2,
        out Matrix4x4 rootLocalMatrix)
    {
        Vector3 localPosition = _meshRootLocalPosition;
        Vector3 localEulerAngles = _meshRootLocalEulerAngles;
        Vector3 localScale = _meshRootLocalScale;

        if (templatePrefab != null && templatePrefab.RenderRoot != null)
        {
            Transform renderRoot = templatePrefab.RenderRoot;
            localPosition = renderRoot.localPosition;
            localEulerAngles = renderRoot.localEulerAngles;
            localScale = renderRoot.localScale;
        }

        rootLocalMatrix = Matrix4x4.TRS(localPosition, Quaternion.Euler(localEulerAngles), localScale);
        row0 = new Vector4(rootLocalMatrix.m00, rootLocalMatrix.m10, rootLocalMatrix.m20, rootLocalMatrix.m03);
        row1 = new Vector4(rootLocalMatrix.m01, rootLocalMatrix.m11, rootLocalMatrix.m21, rootLocalMatrix.m13);
        row2 = new Vector4(rootLocalMatrix.m02, rootLocalMatrix.m12, rootLocalMatrix.m22, rootLocalMatrix.m23);
    }

    private void ResolveKernelHandles()
    {
        _predictKernel = _updateCompute.FindKernel(PredictKernelName);
        _clearGridKernel = _updateCompute.FindKernel(ClearGridKernelName);
        _buildGridKernel = _updateCompute.FindKernel(BuildGridKernelName);
        _solveCrowdKernel = _updateCompute.FindKernel(SolveCrowdKernelName);
        _clearSpatialQueriesKernel = _updateCompute.FindKernel(ClearSpatialQueriesKernelName);
        _resolveSpatialQueriesKernel = _updateCompute.FindKernel(ResolveSpatialQueriesKernelName);
        _finalizeKernel = _updateCompute.FindKernel(FinalizeKernelName);
    }

    private bool AreBuffersReady()
    {
        return _spawnDataBuffer != null &&
            _simulationStateBufferA != null &&
            _simulationStateBufferB != null &&
            _simulationReadBuffer != null &&
            _simulationWriteBuffer != null &&
            _activeStateBuffer != null &&
            _gridCounterBuffer != null &&
            _gridOccupantBuffer != null &&
            _spatialElementBuffer != null &&
            _spatialQueryBuffer != null &&
            _spatialQueryResultBuffer != null &&
            _spatialQueryHitBuffer != null &&
            _interactionSphereBuffer != null &&
            _instanceTransformBuffer != null &&
            _instanceFrameDataBuffer != null &&
            _visibleInstanceIndexBuffer != null &&
            _indirectArgsBuffers.Length > 0;
    }

    private void SyncTemplateBindings()
    {
        if (_templatePrefab == null)
            return;

        if (_templatePrefab.AnimationAsset != null)
            _animationAsset = _templatePrefab.AnimationAsset;

        Transform renderRoot = _templatePrefab.RenderRoot;
        if (renderRoot == null)
            return;

        _meshRootLocalPosition = renderRoot.localPosition;
        _meshRootLocalEulerAngles = renderRoot.localEulerAngles;
        _meshRootLocalScale = renderRoot.localScale;
    }

    private void RefreshRuntimeTargets()
    {
        if (_activeBubbleTarget != null && _activeBubbleTarget.gameObject.activeInHierarchy)
            _resolvedActiveBubbleTarget = _activeBubbleTarget;
        else
            _resolvedActiveBubbleTarget = null;

        if (_characterController != null && !_characterController.gameObject.activeInHierarchy)
            _characterController = null;

        if (_autoResolveCharacterController && _characterController == null)
            _characterController = FindPreferredCharacterController();

        if (_resolvedActiveBubbleTarget == null && _characterController != null)
            _resolvedActiveBubbleTarget = _characterController.transform;

        RefreshTerrainReference();
    }

    private CharacterController FindPreferredCharacterController()
    {
        CharacterController[] controllers = FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        CharacterController fallback = null;

        for (int index = 0; index < controllers.Length; index++)
        {
            CharacterController controller = controllers[index];
            if (controller == null)
                continue;

            if (controller.CompareTag("Player"))
                return controller;

            if (!string.IsNullOrEmpty(_preferredCharacterName) &&
                string.Equals(controller.name, _preferredCharacterName, StringComparison.Ordinal))
            {
                return controller;
            }

            if (fallback == null)
                fallback = controller;
        }

        return fallback;
    }

    private void RefreshTerrainReference()
    {
        Terrain previousTerrain = _terrain;
        if (_terrain != null && (_terrain.terrainData == null || !_terrain.gameObject.activeInHierarchy))
            _terrain = null;

        if (_autoResolveTerrain && _terrain == null)
            _terrain = FindPreferredTerrain();

        if (previousTerrain != _terrain)
            MarkResourcesDirty();
    }

    private Terrain FindPreferredTerrain()
    {
        if (Terrain.activeTerrain != null && Terrain.activeTerrain.terrainData != null)
            return Terrain.activeTerrain;

        Terrain terrain = FindFirstObjectByType<Terrain>();
        return terrain != null && terrain.terrainData != null ? terrain : null;
    }

    private bool TryResolveClip(string clipName, out CrowdVatAnimationAsset.ClipInfo clip)
    {
        CrowdVatAnimationAsset clipSource = ResolveConfiguredAnimationAsset();
        if (clipSource == null)
        {
            clip = default;
            return false;
        }

        return clipSource.TryGetClip(clipName, out clip);
    }

    private void BuildRuntimeMaterials(Shader shader)
    {
        if (!_hasRuntimeRenderResource)
            return;

        RuntimeRenderResource resource = _runtimeRenderResource;
        Material[] sourceMaterials = ResolveSourceMaterials(resource.templatePrefab, resource.animationAsset);
        int subMeshCount = Mathf.Max(1, resource.mesh.subMeshCount);
        int materialCount = Mathf.Max(1, Mathf.Min(subMeshCount, sourceMaterials.Length > 0 ? sourceMaterials.Length : subMeshCount));
        resource.runtimeMaterials = new Material[materialCount];

        for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
        {
            Material runtimeMaterial = _indirectMaterialTemplate != null
                ? new Material(_indirectMaterialTemplate)
                : new Material(shader);

            runtimeMaterial.name = $"{resource.animationAsset.name}_Indirect_{materialIndex:00}";
            runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
            runtimeMaterial.enableInstancing = true;

            Material sourceMaterial = sourceMaterials.Length > 0
                ? sourceMaterials[Mathf.Min(materialIndex, sourceMaterials.Length - 1)]
                : null;
            CopySourceMaterialProperties(sourceMaterial, runtimeMaterial);
            resource.runtimeMaterials[materialIndex] = runtimeMaterial;
        }

        _runtimeRenderResource = resource;
    }

    private Material[] ResolveSourceMaterials(CrowdVatPlayer templatePrefab, CrowdVatAnimationAsset animationAsset)
    {
        if (templatePrefab != null && templatePrefab.MeshRenderer != null)
        {
            Material[] sharedMaterials = templatePrefab.MeshRenderer.sharedMaterials;
            if (sharedMaterials != null && sharedMaterials.Length > 0)
                return sharedMaterials;
        }

        return animationAsset.Materials ?? Array.Empty<Material>();
    }

    private void BuildInstanceBuffers()
    {
        InstanceSpawnData[] spawnData = BuildSpawnData();
        InstanceSimulationState[] simulationState = BuildInitialSimulationState(spawnData);

        _spawnDataBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<InstanceSpawnData>());
        _spawnDataBuffer.SetData(spawnData);

        int simulationStride = Marshal.SizeOf<InstanceSimulationState>();
        _simulationStateBufferA = new ComputeBuffer(_instanceCount, simulationStride);
        _simulationStateBufferB = new ComputeBuffer(_instanceCount, simulationStride);
        _simulationStateBufferA.SetData(simulationState);
        _simulationStateBufferB.SetData(simulationState);
        _simulationReadBuffer = _simulationStateBufferA;
        _simulationWriteBuffer = _simulationStateBufferB;
        _activeStateBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        uint[] activeState = new uint[_instanceCount];
        for (int instanceIndex = 0; instanceIndex < activeState.Length; instanceIndex++)
            activeState[instanceIndex] = 1u;
        _activeStateBuffer.SetData(activeState);

        _instanceTransformBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<MatrixRows>());
        _instanceFrameDataBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());

        BuildSimulationGridLayout(spawnData);
        _gridCounterBuffer = new ComputeBuffer(_gridCellCount, sizeof(uint));
        _gridOccupantBuffer = new ComputeBuffer(_gridCellCount * _maxCellOccupancy, sizeof(uint));
        _spatialElementBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<SpatialElementData>());
        _spatialQueryBuffer = new ComputeBuffer(_maxSpatialQueries, Marshal.SizeOf<SpatialQueryData>());
        _spatialQueryResultBuffer = new ComputeBuffer(_maxSpatialQueries, Marshal.SizeOf<SpatialQueryResultData>());
        _spatialQueryHitBuffer = new ComputeBuffer(_maxSpatialQueries * _maxHitsPerSpatialQuery, Marshal.SizeOf<SpatialQueryHitData>());
        _spatialQueryUploadCache = new SpatialQueryData[_maxSpatialQueries];
        _spatialQueryResultCache = new SpatialQueryResultData[_maxSpatialQueries];
        _spatialQueryHitCache = new SpatialQueryHitData[_maxSpatialQueries * _maxHitsPerSpatialQuery];

        int interactionCapacity = GetInteractionSphereCapacity();
        _interactionSphereBuffer = new ComputeBuffer(interactionCapacity, Marshal.SizeOf<InteractionSphereData>());
        _interactionSphereUploadCache = new InteractionSphereData[interactionCapacity];
        _interactionSphereBuffer.SetData(_interactionSphereUploadCache);

        Bounds referenceAgentLocalBounds = ResolveReferenceAgentLocalBounds();
        _localCrowdBounds = BuildLocalCrowdBounds(spawnData, referenceAgentLocalBounds);
        BuildRenderChunks(spawnData, referenceAgentLocalBounds);
    }

    private Bounds ResolveReferenceAgentLocalBounds()
    {
        return _hasRuntimeRenderResource
            ? _runtimeRenderResource.agentLocalBounds
            : new Bounds(Vector3.zero, Vector3.one);
    }

    private int GetInteractionSphereCapacity()
    {
        int manualCount = _interactionSpheres != null ? _interactionSpheres.Length : 0;
        int automaticCount = _useCharacterAsInteractionSphere ? 1 : 0;
        return Mathf.Max(1, manualCount + automaticCount);
    }

    private void BuildSimulationGridLayout(InstanceSpawnData[] spawnData)
    {
        float padding = Mathf.Max(_maxDisplacementFromSpawn, _collisionRadius) + _queryCellSize;
        GetSpawnExtentsXZ(spawnData, out Vector2 minXZ, out Vector2 maxXZ);
        Vector2 paddedMin = minXZ - Vector2.one * padding;
        Vector2 paddedMax = maxXZ + Vector2.one * padding;
        Vector2 paddedSize = new Vector2(
            Mathf.Max(0.01f, paddedMax.x - paddedMin.x),
            Mathf.Max(0.01f, paddedMax.y - paddedMin.y));
        _gridDimensions = new Vector2Int(
            Mathf.Max(1, Mathf.CeilToInt(paddedSize.x / _queryCellSize)),
            Mathf.Max(1, Mathf.CeilToInt(paddedSize.y / _queryCellSize)));
        Vector2 actualSize = new Vector2(_gridDimensions.x * _queryCellSize, _gridDimensions.y * _queryCellSize);
        Vector2 paddedCenter = (paddedMin + paddedMax) * 0.5f;
        _gridMinXZ = paddedCenter - actualSize * 0.5f;
        _gridMaxXZ = _gridMinXZ + actualSize;
        _gridCellCount = Mathf.Max(1, _gridDimensions.x * _gridDimensions.y);
    }

    private InstanceSpawnData[] BuildSpawnData()
    {
        InstanceSpawnData[] spawnData = new InstanceSpawnData[_instanceCount];
        _minimumPlaybackSpeedMultiplier = float.MaxValue;
        _maximumQueryAgentRadius = 0.0f;

        float clampedJitter = Mathf.Clamp01(_cellJitter);
        float startOffsetStrength = Mathf.Clamp01(_normalizedStartOffsetRandom);
        System.Random random = new System.Random(_randomSeed);
        SpawnAreaContext spawnArea = ResolveSpawnAreaContext();

        for (int instanceIndex = 0; instanceIndex < _instanceCount; instanceIndex++)
        {
            CrowdVatFaction faction = ResolveInstanceFaction(instanceIndex);
            ResolveFactionPlacement(instanceIndex, faction, out int localIndex, out int localCount);

            // 闃佃惀甯冨眬鍙奖鍝嶅嚭鐢熺偣鍜屽垵濮嬫湞鍚戯紝涓嶆敼鍙樺悗缁?GPU 杩愯鏃剁姸鎬佺粨鏋勩€?
            Vector3 localPosition = _factionLayout == CrowdVatFactionLayoutMode.TwoOpposingFactions
                ? BuildTwoFactionSpawnPosition(localIndex, localCount, faction, clampedJitter, random, spawnArea)
                : BuildSingleCrowdSpawnPosition(instanceIndex, clampedJitter, random, spawnArea);
            localPosition = ApplyTerrainHeightToLocalPosition(localPosition);

            float yawRadians = _factionLayout == CrowdVatFactionLayoutMode.TwoOpposingFactions && _orientTowardEnemyFaction
                ? GetOpposingFactionYawRadians(faction)
                : Mathf.Deg2Rad * (float)(random.NextDouble() * 360.0);

            float scaleMultiplier = RandomRange(random, _scaleMultiplierRange.x, _scaleMultiplierRange.y);
            float playbackSpeedMultiplier = RandomRange(random, _playbackSpeedMultiplierRange.x, _playbackSpeedMultiplierRange.y);
            float normalizedStartOffset = (float)random.NextDouble() * startOffsetStrength;

            spawnData[instanceIndex] = new InstanceSpawnData
            {
                localPosition = localPosition,
                yawRadians = yawRadians,
                uniformScale = _baseScale * scaleMultiplier,
                normalizedTimeOffset = normalizedStartOffset,
                playbackSpeedMultiplier = playbackSpeedMultiplier,
                faction = (uint)faction
            };

            _minimumPlaybackSpeedMultiplier = Mathf.Min(_minimumPlaybackSpeedMultiplier, playbackSpeedMultiplier);
            _maximumQueryAgentRadius = Mathf.Max(
                _maximumQueryAgentRadius,
                Mathf.Max(0.01f, _collisionRadius * spawnData[instanceIndex].uniformScale));
        }

        if (_minimumPlaybackSpeedMultiplier == float.MaxValue)
            _minimumPlaybackSpeedMultiplier = 1.0f;

        if (_maximumQueryAgentRadius <= 0.0f)
            _maximumQueryAgentRadius = Mathf.Max(0.01f, _collisionRadius * _baseScale);

        return spawnData;
    }

    private CrowdVatFaction ResolveInstanceFaction(int instanceIndex)
    {
        if (_factionLayout != CrowdVatFactionLayoutMode.TwoOpposingFactions)
            return CrowdVatFaction.CampA;

        int campACount = Mathf.CeilToInt(_instanceCount * 0.5f);
        return instanceIndex < campACount ? CrowdVatFaction.CampA : CrowdVatFaction.CampB;
    }

    private void ResolveFactionPlacement(int instanceIndex, CrowdVatFaction faction, out int localIndex, out int localCount)
    {
        if (_factionLayout != CrowdVatFactionLayoutMode.TwoOpposingFactions)
        {
            localIndex = instanceIndex;
            localCount = _instanceCount;
            return;
        }

        int campACount = Mathf.CeilToInt(_instanceCount * 0.5f);
        int campBCount = Mathf.Max(0, _instanceCount - campACount);

        if (faction == CrowdVatFaction.CampB)
        {
            localIndex = Mathf.Max(0, instanceIndex - campACount);
            localCount = Mathf.Max(1, campBCount);
            return;
        }

        localIndex = instanceIndex;
        localCount = Mathf.Max(1, campACount);
    }

    private Vector3 BuildSingleCrowdSpawnPosition(int instanceIndex, float clampedJitter, System.Random random, SpawnAreaContext spawnArea)
    {
        ResolveGridPlacement(instanceIndex, _instanceCount, spawnArea.size, clampedJitter, random, out float localX, out float localZ);
        localX += spawnArea.centerXZ.x;
        localZ += spawnArea.centerXZ.y;
        return new Vector3(localX, _heightOffset, localZ);
    }

    private Vector3 BuildTwoFactionSpawnPosition(int localIndex, int localCount, CrowdVatFaction faction, float clampedJitter, System.Random random, SpawnAreaContext spawnArea)
    {
        Vector2 campAreaSize = GetFactionAreaSize(spawnArea.size, faction);
        ResolveGridPlacement(localIndex, localCount, campAreaSize, clampedJitter, random, out float localX, out float localZ);

        Vector2 factionCenterXZ = GetFactionCenterXZ(spawnArea, faction);
        localX += factionCenterXZ.x;
        localZ += factionCenterXZ.y;
        return new Vector3(localX, _heightOffset, localZ);
    }

    private bool TryGetFactionControlCenterXZ(CrowdVatFaction faction, out Vector2 centerXZ)
    {
        centerXZ = Vector2.zero;
        if (!_useFactionControlTransforms || !TryGetFactionControlTransform(faction, out Transform controlTransform))
            return false;

        Vector3 localPosition = transform.InverseTransformPoint(controlTransform.position);
        centerXZ = new Vector2(localPosition.x, localPosition.z);
        return true;
    }

    private bool TryGetFactionControlTransform(CrowdVatFaction faction, out Transform controlTransform)
    {
        controlTransform = faction == CrowdVatFaction.CampA ? _campAControlTransform : _campBControlTransform;
        return controlTransform != null;
    }

    private bool TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction faction, out Vector2 centerXZ, out Vector2 sizeXZ)
    {
        centerXZ = Vector2.zero;
        sizeXZ = Vector2.zero;
        TryGetFactionControlTransform(faction, out Transform controlTransform);
        if (!TryGetFactionCornerTransforms(
                faction,
                out Transform northWestCornerTransform,
                out Transform northEastCornerTransform,
                out Transform southEastCornerTransform,
                out Transform southWestCornerTransform))
        {
            return false;
        }

        if (controlTransform != null)
        {
            Vector3 northWestLocalOffset = controlTransform.InverseTransformPoint(northWestCornerTransform.position);
            Vector3 northEastLocalOffset = controlTransform.InverseTransformPoint(northEastCornerTransform.position);
            Vector3 southEastLocalOffset = controlTransform.InverseTransformPoint(southEastCornerTransform.position);
            Vector3 southWestLocalOffset = controlTransform.InverseTransformPoint(southWestCornerTransform.position);

            float halfWidth = Mathf.Max(
                Mathf.Abs(northWestLocalOffset.x),
                Mathf.Abs(northEastLocalOffset.x),
                Mathf.Abs(southEastLocalOffset.x),
                Mathf.Abs(southWestLocalOffset.x));
            float halfDepth = Mathf.Max(
                Mathf.Abs(northWestLocalOffset.z),
                Mathf.Abs(northEastLocalOffset.z),
                Mathf.Abs(southEastLocalOffset.z),
                Mathf.Abs(southWestLocalOffset.z));

            Vector3 controlLocalPosition = transform.InverseTransformPoint(controlTransform.position);
            centerXZ = new Vector2(controlLocalPosition.x, controlLocalPosition.z);
            sizeXZ = new Vector2(Mathf.Max(0.01f, halfWidth * 2.0f), Mathf.Max(0.01f, halfDepth * 2.0f));
            return true;
        }

        Vector3 northWestLocalPosition = transform.InverseTransformPoint(northWestCornerTransform.position);
        Vector3 northEastLocalPosition = transform.InverseTransformPoint(northEastCornerTransform.position);
        Vector3 southEastLocalPosition = transform.InverseTransformPoint(southEastCornerTransform.position);
        Vector3 southWestLocalPosition = transform.InverseTransformPoint(southWestCornerTransform.position);

        float minX = Mathf.Min(northWestLocalPosition.x, northEastLocalPosition.x, southEastLocalPosition.x, southWestLocalPosition.x);
        float maxX = Mathf.Max(northWestLocalPosition.x, northEastLocalPosition.x, southEastLocalPosition.x, southWestLocalPosition.x);
        float minZ = Mathf.Min(northWestLocalPosition.z, northEastLocalPosition.z, southEastLocalPosition.z, southWestLocalPosition.z);
        float maxZ = Mathf.Max(northWestLocalPosition.z, northEastLocalPosition.z, southEastLocalPosition.z, southWestLocalPosition.z);

        centerXZ = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        sizeXZ = new Vector2(Mathf.Max(0.01f, maxX - minX), Mathf.Max(0.01f, maxZ - minZ));
        return true;
    }

    private bool TryGetFactionCornerTransforms(
        CrowdVatFaction faction,
        out Transform northWestCornerTransform,
        out Transform northEastCornerTransform,
        out Transform southEastCornerTransform,
        out Transform southWestCornerTransform)
    {
        if (faction == CrowdVatFaction.CampA)
        {
            northWestCornerTransform = _campANorthWestCornerTransform;
            northEastCornerTransform = _campANorthEastCornerTransform;
            southEastCornerTransform = _campASouthEastCornerTransform;
            southWestCornerTransform = _campASouthWestCornerTransform;
        }
        else
        {
            northWestCornerTransform = _campBNorthWestCornerTransform;
            northEastCornerTransform = _campBNorthEastCornerTransform;
            southEastCornerTransform = _campBSouthEastCornerTransform;
            southWestCornerTransform = _campBSouthWestCornerTransform;
        }

        return northWestCornerTransform != null &&
               northEastCornerTransform != null &&
               southEastCornerTransform != null &&
               southWestCornerTransform != null;
    }

    private void SyncFactionControlTransform(Transform controlTransform, Vector2 centerXZ)
    {
        if (controlTransform == null)
            return;

        Vector3 localPosition = transform.InverseTransformPoint(controlTransform.position);
        localPosition.x = centerXZ.x;
        localPosition.z = centerXZ.y;
        controlTransform.position = transform.TransformPoint(localPosition);
        controlTransform.hasChanged = false;
    }

    private bool TrySyncExplicitFactionAreaFromMovedCorner(CrowdVatFaction faction, Transform movedCornerTransform)
    {
        if (movedCornerTransform == null || !TryGetFactionControlTransform(faction, out Transform controlTransform))
            return false;

        Vector3 localOffset = controlTransform.InverseTransformPoint(movedCornerTransform.position);
        Vector2 areaSize = new Vector2(
            Mathf.Max(0.01f, Mathf.Abs(localOffset.x) * 2.0f),
            Mathf.Max(0.01f, Mathf.Abs(localOffset.z) * 2.0f));

        if (faction == CrowdVatFaction.CampA)
            _campAAreaSize = areaSize;
        else
            _campBAreaSize = areaSize;

        _useExplicitFactionAreaSizes = true;
        return true;
    }

    private void SyncFactionRangeCornerTransforms(
        Transform controlTransform,
        Vector2 centerXZ,
        Vector2 sizeXZ,
        Transform northWestCornerTransform,
        Transform northEastCornerTransform,
        Transform southEastCornerTransform,
        Transform southWestCornerTransform)
    {
        if (controlTransform != null)
            SyncFactionControlTransform(controlTransform, centerXZ);

        if (northWestCornerTransform == null ||
            northEastCornerTransform == null ||
            southEastCornerTransform == null ||
            southWestCornerTransform == null)
        {
            return;
        }

        Vector2 clampedSize = new Vector2(Mathf.Max(0.01f, sizeXZ.x), Mathf.Max(0.01f, sizeXZ.y));
        Vector2 halfSize = clampedSize * 0.5f;
        SyncFactionRangeCornerTransform(northWestCornerTransform, centerXZ + new Vector2(-halfSize.x, halfSize.y));
        SyncFactionRangeCornerTransform(northEastCornerTransform, centerXZ + new Vector2(halfSize.x, halfSize.y));
        SyncFactionRangeCornerTransform(southEastCornerTransform, centerXZ + new Vector2(halfSize.x, -halfSize.y));
        SyncFactionRangeCornerTransform(southWestCornerTransform, centerXZ + new Vector2(-halfSize.x, -halfSize.y));
    }

    private void SyncFactionRangeCornerTransform(Transform cornerTransform, Vector2 cornerXZ)
    {
        if (cornerTransform == null)
            return;

        Vector3 localPosition = transform.InverseTransformPoint(cornerTransform.position);
        localPosition.x = cornerXZ.x;
        localPosition.z = cornerXZ.y;
        cornerTransform.position = transform.TransformPoint(localPosition);
        cornerTransform.hasChanged = false;
    }

    private Vector2 GetFactionCenterXZ(SpawnAreaContext spawnArea, CrowdVatFaction faction)
    {
        if (TryGetFactionControlCenterXZ(faction, out Vector2 controlCenterXZ))
            return controlCenterXZ;

        if (TryGetFactionRangeBoundsFromCornerTransforms(faction, out Vector2 rangeCenterXZ, out _))
            return rangeCenterXZ;

        if (_useExplicitFactionCenters)
            return faction == CrowdVatFaction.CampA ? _campACenterXZ : _campBCenterXZ;

        float factionOffset = GetFactionAxisOffset(spawnArea.size);
        Vector2 factionCenter = spawnArea.centerXZ;
        if (_factionSplitAxis == CrowdVatFactionSplitAxis.Width)
            factionCenter.x += faction == CrowdVatFaction.CampA ? -factionOffset : factionOffset;
        else
            factionCenter.y += faction == CrowdVatFaction.CampA ? -factionOffset : factionOffset;

        return factionCenter;
    }

    private Vector2 GetFactionAreaSize(Vector2 totalAreaSize, CrowdVatFaction faction)
    {
        if (TryGetFactionRangeBoundsFromCornerTransforms(faction, out _, out Vector2 controlAreaSize))
            return controlAreaSize;

        if (_useExplicitFactionAreaSizes)
            return faction == CrowdVatFaction.CampA ? _campAAreaSize : _campBAreaSize;

        float totalWidth = Mathf.Max(totalAreaSize.x, 0.01f);
        float totalDepth = Mathf.Max(totalAreaSize.y, 0.01f);
        float splitLength = _factionSplitAxis == CrowdVatFactionSplitAxis.Width ? totalWidth : totalDepth;
        float clampedGap = Mathf.Clamp(_factionCenterGap, 0.0f, Mathf.Max(0.0f, splitLength - 0.02f));
        float factionLength = Mathf.Max(0.01f, (splitLength - clampedGap) * 0.5f);

        return _factionSplitAxis == CrowdVatFactionSplitAxis.Width
            ? new Vector2(factionLength, totalDepth)
            : new Vector2(totalWidth, factionLength);
    }

    private float GetFactionAxisOffset(Vector2 totalAreaSize)
    {
        float splitLength = _factionSplitAxis == CrowdVatFactionSplitAxis.Width
            ? Mathf.Max(totalAreaSize.x, 0.01f)
            : Mathf.Max(totalAreaSize.y, 0.01f);
        float clampedGap = Mathf.Clamp(_factionCenterGap, 0.0f, Mathf.Max(0.0f, splitLength - 0.02f));
        float factionLength = Mathf.Max(0.01f, (splitLength - clampedGap) * 0.5f);
        return clampedGap * 0.5f + factionLength * 0.5f;
    }

    private float GetOpposingFactionYawRadians(CrowdVatFaction faction)
    {
        if (_factionSplitAxis == CrowdVatFactionSplitAxis.Width)
            return faction == CrowdVatFaction.CampA ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f;

        return faction == CrowdVatFaction.CampA ? 0.0f : Mathf.PI;
    }

    private static void ResolveGridPlacement(
        int instanceIndex,
        int instanceCount,
        Vector2 areaSize,
        float clampedJitter,
        System.Random random,
        out float localX,
        out float localZ)
    {
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(instanceCount * (areaSize.x / Mathf.Max(areaSize.y, 0.01f)))));
        int rows = Mathf.Max(1, Mathf.CeilToInt(instanceCount / (float)columns));
        float cellWidth = columns > 1 ? areaSize.x / (columns - 1) : 0.0f;
        float cellDepth = rows > 1 ? areaSize.y / (rows - 1) : 0.0f;

        int column = instanceIndex % columns;
        int row = instanceIndex / columns;
        float baseX = columns > 1 ? -areaSize.x * 0.5f + column * cellWidth : 0.0f;
        float baseZ = rows > 1 ? -areaSize.y * 0.5f + row * cellDepth : 0.0f;

        float jitterX = (float)(random.NextDouble() * 2.0 - 1.0) * cellWidth * 0.5f * clampedJitter;
        float jitterZ = (float)(random.NextDouble() * 2.0 - 1.0) * cellDepth * 0.5f * clampedJitter;

        localX = baseX + jitterX;
        localZ = baseZ + jitterZ;
    }

    private SpawnAreaContext ResolveSpawnAreaContext()
    {
        if (_distributeAcrossWholeTerrain && TryGetResolvedTerrain(out Terrain terrain))
            return BuildTerrainSpawnAreaContext(terrain);

        return new SpawnAreaContext
        {
            size = new Vector2(Mathf.Max(_areaSize.x, 0.01f), Mathf.Max(_areaSize.y, 0.01f)),
            centerXZ = Vector2.zero
        };
    }

    private SpawnAreaContext BuildTerrainSpawnAreaContext(Terrain terrain)
    {
        Vector3 terrainMin = terrain.transform.position;
        Vector3 terrainMax = terrainMin + terrain.terrainData.size;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

        Vector3 localCornerA = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMin.x, transform.position.y, terrainMin.z));
        Vector3 localCornerB = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMin.x, transform.position.y, terrainMax.z));
        Vector3 localCornerC = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMax.x, transform.position.y, terrainMin.z));
        Vector3 localCornerD = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMax.x, transform.position.y, terrainMax.z));

        float minX = Mathf.Min(localCornerA.x, localCornerB.x, localCornerC.x, localCornerD.x);
        float maxX = Mathf.Max(localCornerA.x, localCornerB.x, localCornerC.x, localCornerD.x);
        float minZ = Mathf.Min(localCornerA.z, localCornerB.z, localCornerC.z, localCornerD.z);
        float maxZ = Mathf.Max(localCornerA.z, localCornerB.z, localCornerC.z, localCornerD.z);

        return new SpawnAreaContext
        {
            size = new Vector2(Mathf.Max(0.01f, maxX - minX), Mathf.Max(0.01f, maxZ - minZ)),
            centerXZ = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f)
        };
    }

    private static void GetSpawnExtentsXZ(InstanceSpawnData[] spawnData, out Vector2 minXZ, out Vector2 maxXZ)
    {
        if (spawnData == null || spawnData.Length == 0)
        {
            minXZ = Vector2.zero;
            maxXZ = Vector2.zero;
            return;
        }

        minXZ = new Vector2(float.MaxValue, float.MaxValue);
        maxXZ = new Vector2(float.MinValue, float.MinValue);
        for (int index = 0; index < spawnData.Length; index++)
        {
            Vector3 localPosition = spawnData[index].localPosition;
            minXZ.x = Mathf.Min(minXZ.x, localPosition.x);
            minXZ.y = Mathf.Min(minXZ.y, localPosition.z);
            maxXZ.x = Mathf.Max(maxXZ.x, localPosition.x);
            maxXZ.y = Mathf.Max(maxXZ.y, localPosition.z);
        }
    }

    private static InstanceSimulationState[] BuildInitialSimulationState(InstanceSpawnData[] spawnData)
    {
        InstanceSimulationState[] simulationState = new InstanceSimulationState[spawnData.Length];
        for (int instanceIndex = 0; instanceIndex < spawnData.Length; instanceIndex++)
        {
            InstanceSpawnData spawn = spawnData[instanceIndex];
            simulationState[instanceIndex] = new InstanceSimulationState
            {
                localPositionAndYaw = new Vector4(
                    spawn.localPosition.x,
                    spawn.localPosition.y,
                    spawn.localPosition.z,
                    spawn.yawRadians),
                scaleAndVelocity = new Vector4(spawn.uniformScale, 0.0f, 0.0f, 0.0f)
            };
        }

        return simulationState;
    }

    private Bounds BuildLocalCrowdBounds(InstanceSpawnData[] spawnData, Bounds agentLocalBounds)
    {
        if (spawnData == null || spawnData.Length == 0)
            return new Bounds(agentLocalBounds.center, agentLocalBounds.size);

        return BuildLocalBoundsForRange(spawnData, 0, spawnData.Length, agentLocalBounds);
    }

    private void BuildIndirectArgsBuffers()
    {
        if (!_hasRuntimeRenderResource)
        {
            _indirectArgsBuffers = Array.Empty<GraphicsBuffer>();
            _indirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
            _visibleInstanceIndexCache = Array.Empty<uint>();
            return;
        }

        RuntimeRenderResource resource = _runtimeRenderResource;
        int subMeshCount = resource.runtimeMaterials != null
            ? Mathf.Min(resource.mesh.subMeshCount, resource.runtimeMaterials.Length)
            : 0;
        _indirectArgsBuffers = new GraphicsBuffer[subMeshCount];
        _indirectArgsCache = new GraphicsBuffer.IndirectDrawIndexedArgs[subMeshCount];

        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            GraphicsBuffer buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(resource.mesh, subMeshIndex, 0u);
            buffer.SetData(new[] { args });
            _indirectArgsBuffers[subMeshIndex] = buffer;
            _indirectArgsCache[subMeshIndex] = args;
        }

        _visibleInstanceIndexBuffer = new ComputeBuffer(Mathf.Max(1, _instanceCount), sizeof(uint));
        _visibleInstanceIndexCache = new uint[Mathf.Max(1, _instanceCount)];
    }

    private void BuildRenderChunks(InstanceSpawnData[] spawnData, Bounds agentLocalBounds)
    {
        if (spawnData == null || spawnData.Length == 0)
        {
            _renderChunks = Array.Empty<RenderChunk>();
            return;
        }

        float chunkSize = Mathf.Max(0.5f, _renderChunkWorldSize);
        Vector2 origin = new Vector2(_localCrowdBounds.min.x, _localCrowdBounds.min.z);
        Dictionary<Vector2Int, List<int>> chunkInstanceLookup = new Dictionary<Vector2Int, List<int>>();
        List<Vector2Int> chunkOrder = new List<Vector2Int>();

        for (int instanceIndex = 0; instanceIndex < spawnData.Length; instanceIndex++)
        {
            Vector2 localXZ = new Vector2(spawnData[instanceIndex].localPosition.x, spawnData[instanceIndex].localPosition.z);
            Vector2Int chunkCoord = new Vector2Int(
                Mathf.FloorToInt((localXZ.x - origin.x) / chunkSize),
                Mathf.FloorToInt((localXZ.y - origin.y) / chunkSize));

            if (!chunkInstanceLookup.TryGetValue(chunkCoord, out List<int> indices))
            {
                indices = new List<int>();
                chunkInstanceLookup.Add(chunkCoord, indices);
                chunkOrder.Add(chunkCoord);
            }

            indices.Add(instanceIndex);
        }

        _renderChunks = new RenderChunk[chunkOrder.Count];
        for (int chunkIndex = 0; chunkIndex < chunkOrder.Count; chunkIndex++)
        {
            List<int> indices = chunkInstanceLookup[chunkOrder[chunkIndex]];
            uint[] instanceIndices = new uint[indices.Count];
            for (int index = 0; index < indices.Count; index++)
                instanceIndices[index] = (uint)indices[index];

            _renderChunks[chunkIndex] = new RenderChunk
            {
                instanceIndices = instanceIndices,
                localBounds = BuildLocalBoundsForIndices(spawnData, indices, agentLocalBounds)
            };
        }
    }

    private Bounds BuildLocalBoundsForRange(InstanceSpawnData[] spawnData, int startInstance, int instanceCount, Bounds agentLocalBounds)
    {
        bool hasBounds = false;
        Bounds bounds = default;

        for (int offset = 0; offset < instanceCount; offset++)
        {
            InstanceSpawnData instance = spawnData[startInstance + offset];
            Matrix4x4 instanceMatrix = Matrix4x4.TRS(
                instance.localPosition,
                Quaternion.Euler(0.0f, instance.yawRadians * Mathf.Rad2Deg, 0.0f),
                Vector3.one * instance.uniformScale);
            Bounds instanceBounds = TransformBounds(instanceMatrix, agentLocalBounds);

            if (!hasBounds)
            {
                bounds = instanceBounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(instanceBounds.min);
            bounds.Encapsulate(instanceBounds.max);
        }

        if (!hasBounds)
            return new Bounds(agentLocalBounds.center, agentLocalBounds.size);

        return ExpandRenderBounds(bounds);
    }

    private Bounds BuildLocalBoundsForIndices(InstanceSpawnData[] spawnData, IReadOnlyList<int> indices, Bounds agentLocalBounds)
    {
        if (indices == null || indices.Count == 0)
            return new Bounds(agentLocalBounds.center, agentLocalBounds.size);

        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < indices.Count; i++)
        {
            InstanceSpawnData instance = spawnData[indices[i]];
            Matrix4x4 instanceMatrix = Matrix4x4.TRS(
                instance.localPosition,
                Quaternion.Euler(0.0f, instance.yawRadians * Mathf.Rad2Deg, 0.0f),
                Vector3.one * instance.uniformScale);
            Bounds instanceBounds = TransformBounds(instanceMatrix, agentLocalBounds);
            if (!hasBounds)
            {
                bounds = instanceBounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(instanceBounds.min);
            bounds.Encapsulate(instanceBounds.max);
        }

        return ExpandRenderBounds(bounds);
    }

    private Bounds ExpandRenderBounds(Bounds bounds)
    {
        float dynamicPadding = _boundsPadding + (_enableApproximateCollision ? _maxDisplacementFromSpawn : 0.0f);
        bounds.Expand(dynamicPadding * 2.0f);
        return bounds;
    }

    private static GraphicsBuffer.IndirectDrawIndexedArgs BuildIndirectArgs(Mesh mesh, int subMeshIndex, uint instanceCount)
    {
        return new GraphicsBuffer.IndirectDrawIndexedArgs
        {
            indexCountPerInstance = mesh.GetIndexCount(subMeshIndex),
            instanceCount = instanceCount,
            startIndex = mesh.GetIndexStart(subMeshIndex),
            baseVertexIndex = (uint)mesh.GetBaseVertex(subMeshIndex),
            startInstance = 0u
        };
    }

    private void BindStaticResources()
    {
        if (!_hasRuntimeRenderResource || _runtimeRenderResource.runtimeMaterials == null)
            return;

        RuntimeRenderResource resource = _runtimeRenderResource;
        Texture2D boneTexture = resource.animationAsset.BoneAnimationTexture;

        for (int materialIndex = 0; materialIndex < resource.runtimeMaterials.Length; materialIndex++)
        {
            Material runtimeMaterial = resource.runtimeMaterials[materialIndex];
            runtimeMaterial.SetTexture(BoneAnimationTextureId, boneTexture);
            runtimeMaterial.SetBuffer(InstanceTransformsId, _instanceTransformBuffer);
            runtimeMaterial.SetBuffer(InstanceFrameDataId, _instanceFrameDataBuffer);
            runtimeMaterial.SetBuffer(VisibleInstanceIndicesId, _visibleInstanceIndexBuffer);
            runtimeMaterial.SetVector(CrowdRootLocalRow0Id, resource.crowdRootRow0);
            runtimeMaterial.SetVector(CrowdRootLocalRow1Id, resource.crowdRootRow1);
            runtimeMaterial.SetVector(CrowdRootLocalRow2Id, resource.crowdRootRow2);
        }
    }

    private void AdvancePlayback(float deltaTime)
    {
        if (!_hasClip || !_isPlaying)
            return;

        float basePlaybackSpeed = Mathf.Max(0.0f, _playbackSpeed);
        if (basePlaybackSpeed <= Mathf.Epsilon)
            return;

        float clipLength = Mathf.Max(_currentClip.LengthSeconds, 0.0f);
        if (clipLength <= Mathf.Epsilon)
            return;

        bool shouldLoop = _loopOverride || _currentClip.Loop;
        _playbackTime += deltaTime;

        if (shouldLoop)
            return;

        float slowestDuration = clipLength / Mathf.Max(basePlaybackSpeed * Mathf.Max(_minimumPlaybackSpeedMultiplier, 0.01f), 0.01f);
        if (_playbackTime >= slowestDuration)
        {
            _playbackTime = slowestDuration;
            _isPlaying = false;
        }
    }

    private void UpdateGpuBuffers()
    {
        if (!_hasClip || _simulationReadBuffer == null || _simulationWriteBuffer == null)
            return;

        RefreshRuntimeTargets();

        float deltaTime = Mathf.Max(Time.deltaTime, 1e-4f);
        int interactionSphereCount = UploadInteractionSpheres();
        ConfigureCommonComputeParameters(deltaTime, interactionSphereCount);

        BindPredictKernel();
        DispatchInstances(_predictKernel);
        SwapSimulationBuffers();

        bool usesCrowdApproximation = _enableApproximateCollision && _instanceCount > 1;
        bool usesEnvironmentPbd = _hasResolvedTerrainCollision || _hasResolvedStaticSdfCollision;
        int solverPassCount = usesCrowdApproximation || usesEnvironmentPbd ? Mathf.Max(1, _solverIterations) : 0;
        for (int iterationIndex = 0; iterationIndex < solverPassCount; iterationIndex++)
        {
            BindClearGridKernel();
            DispatchGrid(_clearGridKernel);

            BindBuildGridKernel();
            DispatchInstances(_buildGridKernel);

            BindSolveKernel();
            DispatchInstances(_solveCrowdKernel);
            SwapSimulationBuffers();
        }

        int spatialQueryCount = UploadSpatialQueries();
        if (spatialQueryCount > 0)
        {
            _updateCompute.SetInt(GridBuildModeId, GridBuildModeAllQueryables);

            BindClearGridKernel();
            DispatchGrid(_clearGridKernel);

            BindBuildGridKernel();
            DispatchInstances(_buildGridKernel);

            BindClearSpatialQueriesKernel();
            DispatchSpatialQueries(_clearSpatialQueriesKernel, spatialQueryCount);

            BindResolveSpatialQueriesKernel();
            DispatchSpatialQueries(_resolveSpatialQueriesKernel, spatialQueryCount);

            _updateCompute.SetInt(GridBuildModeId, GridBuildModeActiveOnly);
        }

        BindFinalizeKernel();
        DispatchInstances(_finalizeKernel);
    }

    private void ConfigureCommonComputeParameters(float deltaTime, int interactionSphereCount)
    {
        Vector3 activeBubbleLocalCenter = Vector3.zero;
        bool useActiveBubble = _enableActiveBubble && TryGetActiveBubbleLocalCenter(out activeBubbleLocalCenter);
        bool useTerrainCollision = TryGetResolvedTerrain(out Terrain terrain) &&
            terrain.terrainData.heightmapTexture != null;
        bool useStaticSdfCollision = TryGetResolvedStaticSdf(out Texture3D staticSdfTexture);

        _resolvedTerrainHeightmap = useTerrainCollision
            ? terrain.terrainData.heightmapTexture
            : Texture2D.blackTexture;
        _resolvedStaticSdfTexture = useStaticSdfCollision
            ? staticSdfTexture
            : GetFallbackStaticSdfTexture();
        _hasResolvedTerrainCollision = useTerrainCollision;
        _hasResolvedStaticSdfCollision = useStaticSdfCollision;

        _updateCompute.SetInt(InstanceCountId, _instanceCount);
        _updateCompute.SetFloat(PlaybackTimeId, _playbackTime);
        _updateCompute.SetFloat(BasePlaybackSpeedId, Mathf.Max(0.0f, _playbackSpeed));
        _updateCompute.SetInt(ClipStartFrameId, _currentClip.StartFrame);
        _updateCompute.SetInt(ClipFrameCountId, _currentClip.FrameCount);
        _updateCompute.SetFloat(ClipLengthId, Mathf.Max(_currentClip.LengthSeconds, 0.0f));
        _updateCompute.SetInt(ClipLoopId, (_loopOverride || _currentClip.Loop) ? 1 : 0);
        _updateCompute.SetFloat(DeltaTimeId, deltaTime);
        _updateCompute.SetInt(EnableApproximateCollisionId, _enableApproximateCollision ? 1 : 0);
        _updateCompute.SetInt(UseActiveBubbleId, useActiveBubble ? 1 : 0);
        _updateCompute.SetInt(InteractionSphereCountId, interactionSphereCount);
        _updateCompute.SetVector(ActiveBubbleCenterId, new Vector4(activeBubbleLocalCenter.x, activeBubbleLocalCenter.y, activeBubbleLocalCenter.z, 0.0f));
        _updateCompute.SetFloat(ActiveBubbleRadiusId, _activeBubbleRadius);
        _updateCompute.SetFloat(ActiveBubbleRetentionRadiusId, _activeBubbleRetentionRadius);
        _updateCompute.SetInts(GridDimId, _gridDimensions.x, _gridDimensions.y);
        _updateCompute.SetInt(GridCellCountId, _gridCellCount);
        _updateCompute.SetInt(MaxCellOccupancyId, _maxCellOccupancy);
        _updateCompute.SetFloat(CellSizeId, _queryCellSize);
        _updateCompute.SetFloat(InvCellSizeId, 1.0f / Mathf.Max(_queryCellSize, 1e-4f));
        _updateCompute.SetVector(GridMinXZId, _gridMinXZ);
        _updateCompute.SetVector(GridMaxXZId, _gridMaxXZ);
        _updateCompute.SetFloat(CollisionRadiusId, _collisionRadius);
        _updateCompute.SetFloat(CapsuleHeightId, _collisionHeight);
        _updateCompute.SetInt(CapsulePbdSampleCountId, _capsulePbdSampleCount);
        _updateCompute.SetFloat(MaxSpatialQueryElementRadiusId, Mathf.Max(_maximumQueryAgentRadius, _collisionRadius));
        _updateCompute.SetFloat(AnchorStiffnessId, _anchorStiffness);
        _updateCompute.SetFloat(VelocityDampingId, _velocityDamping);
        _updateCompute.SetFloat(SelfCollisionStrengthId, _selfCollisionStrength);
        _updateCompute.SetFloat(MaxPushPerStepId, _maxPushPerStep);
        _updateCompute.SetFloat(MaxDisplacementFromSpawnId, _maxDisplacementFromSpawn);
        _updateCompute.SetFloat(InactiveReturnStrengthId, _inactiveReturnStrength);
        _updateCompute.SetInt(EnableTerrainCollisionId, useTerrainCollision ? 1 : 0);
        _updateCompute.SetFloat(TerrainHeightOffsetId, _terrainHeightOffset);
        _updateCompute.SetInt(EnableStaticSdfCollisionId, useStaticSdfCollision ? 1 : 0);
        _updateCompute.SetVector(StaticSdfWorldCenterId, _staticSdfWorldCenter);
        _updateCompute.SetVector(StaticSdfWorldSizeId, _staticSdfWorldSize);
        _updateCompute.SetFloat(StaticSdfDistanceScaleId, _staticSdfDistanceScale);
        _updateCompute.SetFloat(StaticSdfDistanceBiasId, _staticSdfDistanceBias);
        _updateCompute.SetInt(GridBuildModeId, GridBuildModeActiveOnly);
        _updateCompute.SetInt(SpatialQueryCountId, _activeSpatialQueryCount);
        _updateCompute.SetInt(MaxSpatialQueryHitsId, _maxHitsPerSpatialQuery);

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        _updateCompute.SetVector(RootPositionId, new Vector4(localToWorld.m03, localToWorld.m13, localToWorld.m23, 0.0f));
        _updateCompute.SetVector(RootRightId, new Vector4(localToWorld.m00, localToWorld.m10, localToWorld.m20, 0.0f));
        _updateCompute.SetVector(RootUpId, new Vector4(localToWorld.m01, localToWorld.m11, localToWorld.m21, 0.0f));
        _updateCompute.SetVector(RootForwardId, new Vector4(localToWorld.m02, localToWorld.m12, localToWorld.m22, 0.0f));
        _updateCompute.SetVector(WorldToLocalRow0Id, new Vector4(worldToLocal.m00, worldToLocal.m01, worldToLocal.m02, worldToLocal.m03));
        _updateCompute.SetVector(WorldToLocalRow1Id, new Vector4(worldToLocal.m10, worldToLocal.m11, worldToLocal.m12, worldToLocal.m13));
        _updateCompute.SetVector(WorldToLocalRow2Id, new Vector4(worldToLocal.m20, worldToLocal.m21, worldToLocal.m22, worldToLocal.m23));

        if (useTerrainCollision)
        {
            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            _updateCompute.SetVector(TerrainPositionId, new Vector4(terrainPosition.x, terrainPosition.y, terrainPosition.z, 0.0f));
            _updateCompute.SetVector(TerrainSizeId, new Vector4(terrainSize.x, terrainSize.y, terrainSize.z, 0.0f));
        }
        else
        {
            _updateCompute.SetVector(TerrainPositionId, Vector4.zero);
            _updateCompute.SetVector(TerrainSizeId, Vector4.zero);
        }
    }

    private void BindPredictKernel()
    {
        _updateCompute.SetBuffer(_predictKernel, SpawnDataId, _spawnDataBuffer);
        _updateCompute.SetBuffer(_predictKernel, SimulationReadBufferId, _simulationReadBuffer);
        _updateCompute.SetBuffer(_predictKernel, SimulationWriteBufferId, _simulationWriteBuffer);
        _updateCompute.SetBuffer(_predictKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_predictKernel, InteractionSphereBufferId, _interactionSphereBuffer);
        _updateCompute.SetTexture(_predictKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
    }

    private void BindClearGridKernel()
    {
        _updateCompute.SetBuffer(_clearGridKernel, GridCounterBufferId, _gridCounterBuffer);
    }

    private void BindBuildGridKernel()
    {
        _updateCompute.SetBuffer(_buildGridKernel, SpawnDataId, _spawnDataBuffer);
        _updateCompute.SetBuffer(_buildGridKernel, SimulationReadBufferId, _simulationReadBuffer);
        _updateCompute.SetBuffer(_buildGridKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_buildGridKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_buildGridKernel, GridOccupantBufferId, _gridOccupantBuffer);
        _updateCompute.SetBuffer(_buildGridKernel, SpatialElementBufferId, _spatialElementBuffer);
    }

    private void BindSolveKernel()
    {
        _updateCompute.SetBuffer(_solveCrowdKernel, SpawnDataId, _spawnDataBuffer);
        _updateCompute.SetBuffer(_solveCrowdKernel, SimulationReadBufferId, _simulationReadBuffer);
        _updateCompute.SetBuffer(_solveCrowdKernel, SimulationWriteBufferId, _simulationWriteBuffer);
        _updateCompute.SetBuffer(_solveCrowdKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_solveCrowdKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_solveCrowdKernel, GridOccupantBufferId, _gridOccupantBuffer);
        _updateCompute.SetTexture(_solveCrowdKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_solveCrowdKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
    }

    private void BindFinalizeKernel()
    {
        _updateCompute.SetBuffer(_finalizeKernel, SpawnDataId, _spawnDataBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, SimulationReadBufferId, _simulationReadBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, InstanceTransformsId, _instanceTransformBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, InstanceFrameDataId, _instanceFrameDataBuffer);
    }

    private void BindClearSpatialQueriesKernel()
    {
        _updateCompute.SetBuffer(_clearSpatialQueriesKernel, SpatialQueryBufferId, _spatialQueryBuffer);
        _updateCompute.SetBuffer(_clearSpatialQueriesKernel, SpatialQueryResultBufferId, _spatialQueryResultBuffer);
        _updateCompute.SetBuffer(_clearSpatialQueriesKernel, SpatialQueryHitBufferId, _spatialQueryHitBuffer);
    }

    private void BindResolveSpatialQueriesKernel()
    {
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, SimulationReadBufferId, _simulationReadBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, GridOccupantBufferId, _gridOccupantBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, SpatialElementBufferId, _spatialElementBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, SpatialQueryBufferId, _spatialQueryBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, SpatialQueryResultBufferId, _spatialQueryResultBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, SpatialQueryHitBufferId, _spatialQueryHitBuffer);
    }

    private int UploadInteractionSpheres()
    {
        int capacity = GetInteractionSphereCapacity();
        if (_interactionSphereUploadCache == null || _interactionSphereUploadCache.Length != capacity)
            _interactionSphereUploadCache = new InteractionSphereData[capacity];

        if (_interactionSphereBuffer == null || _interactionSphereBuffer.count != capacity)
            return 0;

        int activeCount = 0;
        if (_useCharacterAsInteractionSphere)
            TryAppendCharacterInteractionSphere(ref activeCount);

        if (_interactionSpheres != null)
        {
            for (int sphereIndex = 0; sphereIndex < _interactionSpheres.Length; sphereIndex++)
            {
                InteractionSphere sphere = _interactionSpheres[sphereIndex];
                if (sphere.target == null || sphere.radius <= 0.0f || sphere.strength <= 0.0f)
                    continue;

                Vector3 localCenter = transform.InverseTransformPoint(sphere.target.position);
                float radiusScale = MaxAbsComponent(sphere.target.lossyScale);
                float scaledRadius = sphere.radius * Mathf.Max(radiusScale, 0.0001f);
                _interactionSphereUploadCache[activeCount] = new InteractionSphereData
                {
                    localCenterAndRadius = new Vector4(localCenter.x, localCenter.y, localCenter.z, scaledRadius),
                    parameters = new Vector4(sphere.strength, 0.0f, 0.0f, 0.0f)
                };
                activeCount++;
            }
        }

        if (activeCount == 0)
            _interactionSphereUploadCache[0] = default;

        _interactionSphereBuffer.SetData(_interactionSphereUploadCache, 0, 0, Mathf.Max(1, activeCount));
        return activeCount;
    }

    private int UploadSpatialQueries()
    {
        if (_activeSpatialQueryCount <= 0 ||
            _runtimeSpatialQueries == null ||
            _spatialQueryBuffer == null ||
            _spatialQueryBuffer.count < _activeSpatialQueryCount)
        {
            return 0;
        }

        EnsureSpatialQueryReadbackCaches();

        for (int queryIndex = 0; queryIndex < _activeSpatialQueryCount; queryIndex++)
        {
            CrowdVatSpatialQueryRequest request = _runtimeSpatialQueries[queryIndex];
            Vector3 localStart = transform.InverseTransformPoint(request.worldStart);
            Vector3 localEnd = transform.InverseTransformPoint(request.worldEnd);

            _spatialQueryUploadCache[queryIndex] = new SpatialQueryData
            {
                localStartAndRadius = new Vector4(localStart.x, localStart.y, localStart.z, Mathf.Max(0.0f, request.radius)),
                localEndAndPadding = new Vector4(localEnd.x, localEnd.y, localEnd.z, 0.0f),
                queryId = unchecked((uint)request.queryId),
                targetMask = (uint)request.targetMask,
                factionMask = (uint)request.factionMask,
                shape = (uint)request.shape,
                flags = (uint)request.flags,
                maxHits = (uint)Mathf.Clamp((int)request.maxHits, 0, _maxHitsPerSpatialQuery),
                padding0 = 0u,
                padding1 = 0u
            };
        }

        _spatialQueryBuffer.SetData(_spatialQueryUploadCache, 0, 0, _activeSpatialQueryCount);
        return _activeSpatialQueryCount;
    }

    private void TryAppendCharacterInteractionSphere(ref int activeCount)
    {
        if (_characterController == null || _characterInteractionStrength <= 0.0f || activeCount >= _interactionSphereUploadCache.Length)
            return;

        Vector3 worldCenter = _characterController.transform.TransformPoint(_characterController.center);
        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
        float radius = Mathf.Max(_characterController.radius, _characterController.height * 0.25f) * _characterInteractionRadiusMultiplier;

        _interactionSphereUploadCache[activeCount] = new InteractionSphereData
        {
            localCenterAndRadius = new Vector4(localCenter.x, localCenter.y, localCenter.z, radius),
            parameters = new Vector4(_characterInteractionStrength, 0.0f, 0.0f, 0.0f)
        };
        activeCount++;
    }

    private bool TryGetActiveBubbleLocalCenter(out Vector3 localCenter)
    {
        RefreshRuntimeTargets();

        if (_characterController != null)
        {
            Vector3 worldCenter = _characterController.transform.TransformPoint(_characterController.center);
            localCenter = transform.InverseTransformPoint(worldCenter);
            return true;
        }

        if (_resolvedActiveBubbleTarget != null)
        {
            localCenter = transform.InverseTransformPoint(_resolvedActiveBubbleTarget.position);
            return true;
        }

        localCenter = default;
        return false;
    }

    private void DispatchInstances(int kernel)
    {
        int threadGroupCount = Mathf.CeilToInt(_instanceCount / (float)ThreadGroupSize);
        _updateCompute.Dispatch(kernel, threadGroupCount, 1, 1);
    }

    private void DispatchGrid(int kernel)
    {
        int threadGroupCount = Mathf.CeilToInt(_gridCellCount / (float)ThreadGroupSize);
        _updateCompute.Dispatch(kernel, threadGroupCount, 1, 1);
    }

    private void DispatchSpatialQueries(int kernel, int queryCount)
    {
        int threadGroupCount = Mathf.CeilToInt(queryCount / (float)SpatialQueryThreadGroupSize);
        _updateCompute.Dispatch(kernel, threadGroupCount, 1, 1);
    }

    private void SwapSimulationBuffers()
    {
        ComputeBuffer buffer = _simulationReadBuffer;
        _simulationReadBuffer = _simulationWriteBuffer;
        _simulationWriteBuffer = buffer;
    }

    private void Draw()
    {
        Draw(ResolveRenderCamera());
    }

    private void Draw(Camera targetCamera)
    {
        if (!_hasRuntimeRenderResource || _indirectArgsBuffers.Length == 0)
            return;

        bool isSceneViewCamera = IsSceneViewCamera(targetCamera);
        Bounds worldBounds = TransformBounds(transform.localToWorldMatrix, _localCrowdBounds);
        bool hasFrustumPlanes = false;

        if (_enableFrustumCulling && targetCamera != null && !isSceneViewCamera)
        {
            GeometryUtility.CalculateFrustumPlanes(targetCamera, _frustumPlanes);
            hasFrustumPlanes = true;

            if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, worldBounds))
            {
                ResetVisibleIndirectCommands();
                return;
            }
        }

        BuildVisibleIndirectCommands(targetCamera, hasFrustumPlanes);

        RuntimeRenderResource resource = _runtimeRenderResource;
        if (_visibleInstanceCount <= 0 || !_hasVisibleBounds || resource.mesh == null || resource.runtimeMaterials == null)
            return;

        _visibleInstanceIndexBuffer.SetData(_visibleInstanceIndexCache, 0, 0, _visibleInstanceCount);
        for (int subMeshIndex = 0; subMeshIndex < resource.runtimeMaterials.Length; subMeshIndex++)
        {
            GraphicsBuffer argsBuffer = _indirectArgsBuffers[subMeshIndex];
            if (argsBuffer == null)
                continue;

            GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(resource.mesh, subMeshIndex, (uint)_visibleInstanceCount);
            _indirectArgsCache[subMeshIndex] = args;
            argsBuffer.SetData(new[] { args });

            RenderParams renderParams = new RenderParams(resource.runtimeMaterials[subMeshIndex])
            {
                camera = targetCamera,
                instanceID = gameObject.GetInstanceID(),
                worldBounds = _visibleWorldBounds,
                shadowCastingMode = _shadowCastingMode,
                receiveShadows = _receiveShadows,
                layer = gameObject.layer
            };

            Graphics.RenderMeshIndirect(renderParams, resource.mesh, argsBuffer, 1, 0);
        }
    }

    private Camera ResolveRenderCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        if (Camera.current != null)
            return Camera.current;

        return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
    }

    private void SubscribeRenderCallbacks()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void UnsubscribeRenderCallbacks()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private static bool ShouldRenderForCamera(Camera camera)
    {
        if (camera == null || !camera.enabled)
            return false;

        return camera.cameraType != CameraType.Preview && camera.cameraType != CameraType.Reflection;
    }

    private static bool IsSceneViewCamera(Camera camera)
    {
        return camera != null && camera.cameraType == CameraType.SceneView;
    }

    private void BuildVisibleIndirectCommands(Camera targetCamera, bool hasFrustumPlanes)
    {
        ResetVisibleIndirectCommands();

        if (_renderChunks.Length == 0)
        {
            AddChunkToVisibleCommands(BuildAllInstanceIndices(), TransformBounds(transform.localToWorldMatrix, _localCrowdBounds));
            return;
        }

        Matrix4x4 localToWorldMatrix = transform.localToWorldMatrix;

        for (int chunkIndex = 0; chunkIndex < _renderChunks.Length; chunkIndex++)
        {
            RenderChunk chunk = _renderChunks[chunkIndex];
            Bounds worldChunkBounds = TransformBounds(localToWorldMatrix, chunk.localBounds);

            if (hasFrustumPlanes && !GeometryUtility.TestPlanesAABB(_frustumPlanes, worldChunkBounds))
                continue;

            AddChunkToVisibleCommands(chunk.instanceIndices, worldChunkBounds);
        }
    }

    private void ResetVisibleIndirectCommands()
    {
        _visibleInstanceCount = 0;
        _hasVisibleBounds = false;
        _visibleWorldBounds = default;
    }

    private void AddChunkToVisibleCommands(uint[] instanceIndices, Bounds worldChunkBounds)
    {
        if (instanceIndices == null || instanceIndices.Length == 0)
            return;

        int writeOffset = _visibleInstanceCount;
        if (writeOffset + instanceIndices.Length > _visibleInstanceIndexCache.Length)
            return;

        if (!_hasVisibleBounds)
        {
            _visibleWorldBounds = worldChunkBounds;
            _hasVisibleBounds = true;
        }
        else
        {
            _visibleWorldBounds.Encapsulate(worldChunkBounds.min);
            _visibleWorldBounds.Encapsulate(worldChunkBounds.max);
        }

        Array.Copy(instanceIndices, 0, _visibleInstanceIndexCache, writeOffset, instanceIndices.Length);
        _visibleInstanceCount = writeOffset + instanceIndices.Length;
    }

    private uint[] BuildAllInstanceIndices()
    {
        uint[] indices = new uint[_instanceCount];
        for (uint instanceIndex = 0; instanceIndex < indices.Length; instanceIndex++)
            indices[instanceIndex] = instanceIndex;

        return indices;
    }

    private void MarkResourcesDirty()
    {
        _resourcesDirty = true;
        _predictKernel = -1;
        _clearGridKernel = -1;
        _buildGridKernel = -1;
        _solveCrowdKernel = -1;
        _clearSpatialQueriesKernel = -1;
        _resolveSpatialQueriesKernel = -1;
        _finalizeKernel = -1;
    }

    private void ReleaseResources()
    {
        ReleaseBuffer(ref _spawnDataBuffer);
        ReleaseBuffer(ref _simulationStateBufferA);
        ReleaseBuffer(ref _simulationStateBufferB);
        _simulationReadBuffer = null;
        _simulationWriteBuffer = null;
        ReleaseBuffer(ref _activeStateBuffer);
        ReleaseBuffer(ref _gridCounterBuffer);
        ReleaseBuffer(ref _gridOccupantBuffer);
        ReleaseBuffer(ref _spatialElementBuffer);
        ReleaseBuffer(ref _spatialQueryBuffer);
        ReleaseBuffer(ref _spatialQueryResultBuffer);
        ReleaseBuffer(ref _spatialQueryHitBuffer);
        ReleaseBuffer(ref _interactionSphereBuffer);
        ReleaseBuffer(ref _instanceTransformBuffer);
        ReleaseBuffer(ref _instanceFrameDataBuffer);
        ReleaseBuffer(ref _visibleInstanceIndexBuffer);

        if (_indirectArgsBuffers != null)
        {
            for (int bufferIndex = 0; bufferIndex < _indirectArgsBuffers.Length; bufferIndex++)
                ReleaseBuffer(ref _indirectArgsBuffers[bufferIndex]);
        }

        _indirectArgsBuffers = Array.Empty<GraphicsBuffer>();
        _indirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
        _visibleInstanceIndexCache = Array.Empty<uint>();
        _renderChunks = Array.Empty<RenderChunk>();
        if (_hasRuntimeRenderResource && _runtimeRenderResource.runtimeMaterials != null)
        {
            for (int materialIndex = 0; materialIndex < _runtimeRenderResource.runtimeMaterials.Length; materialIndex++)
                DestroyRuntimeMaterial(_runtimeRenderResource.runtimeMaterials[materialIndex]);
        }

        _runtimeRenderResource = default;
        _hasRuntimeRenderResource = false;
        if (_fallbackStaticSdfTexture != null)
        {
            if (Application.isPlaying)
                Destroy(_fallbackStaticSdfTexture);
            else
                DestroyImmediate(_fallbackStaticSdfTexture);

            _fallbackStaticSdfTexture = null;
        }

        _resolvedStaticSdfTexture = null;
        _hasResolvedTerrainCollision = false;
        _hasResolvedStaticSdfCollision = false;
        _resourcesDirty = true;
    }

    private static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    private static void ReleaseBuffer(ref GraphicsBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    private static void DestroyRuntimeMaterial(Material material)
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
    }

    private static float RandomRange(System.Random random, float minValue, float maxValue)
    {
        if (maxValue <= minValue)
            return minValue;

        return minValue + (float)random.NextDouble() * (maxValue - minValue);
    }

    private static float MaxAbsComponent(Vector3 value)
    {
        Vector3 absolute = Abs(value);
        return Mathf.Max(absolute.x, Mathf.Max(absolute.y, absolute.z));
    }

    private static void CopySourceMaterialProperties(Material sourceMaterial, Material destinationMaterial)
    {
        if (destinationMaterial == null)
            return;

        destinationMaterial.SetColor("_BaseColor", Color.white);
        destinationMaterial.SetColor("_SpecColor", Color.white);
        destinationMaterial.SetFloat("_AlphaClip", 0.0f);
        destinationMaterial.SetFloat("_Cutoff", 0.5f);
        destinationMaterial.SetFloat("_BumpScale", 1.0f);
        destinationMaterial.SetFloat("_Smoothness", 1.0f);
        destinationMaterial.SetFloat("_SpecularStrength", 0.12f);
        destinationMaterial.SetFloat("_UseNormalMap", 0.0f);
        destinationMaterial.SetFloat("_UseSpecGlossMap", 0.0f);

        if (sourceMaterial == null)
            return;

        if (TryGetTexture(sourceMaterial, out Texture texture, out Vector2 scale, out Vector2 offset))
        {
            destinationMaterial.SetTexture("_BaseMap", texture);
            destinationMaterial.SetTextureScale("_BaseMap", scale);
            destinationMaterial.SetTextureOffset("_BaseMap", offset);
        }

        if (TryGetColor(sourceMaterial, out Color baseColor))
            destinationMaterial.SetColor("_BaseColor", baseColor);

        if (sourceMaterial.HasProperty("_Cutoff"))
            destinationMaterial.SetFloat("_Cutoff", sourceMaterial.GetFloat("_Cutoff"));

        if (sourceMaterial.HasProperty("_BumpMap"))
        {
            Texture normalTexture = sourceMaterial.GetTexture("_BumpMap");
            destinationMaterial.SetTexture("_BumpMap", normalTexture);
            destinationMaterial.SetFloat("_UseNormalMap", normalTexture != null ? 1.0f : 0.0f);
        }

        if (sourceMaterial.HasProperty("_BumpScale"))
            destinationMaterial.SetFloat("_BumpScale", sourceMaterial.GetFloat("_BumpScale"));

        if (sourceMaterial.HasProperty("_SpecGlossMap"))
        {
            Texture specGlossTexture = sourceMaterial.GetTexture("_SpecGlossMap");
            destinationMaterial.SetTexture("_SpecGlossMap", specGlossTexture);
            destinationMaterial.SetFloat("_UseSpecGlossMap", specGlossTexture != null ? 1.0f : 0.0f);
        }

        if (sourceMaterial.HasProperty("_SpecColor"))
            destinationMaterial.SetColor("_SpecColor", sourceMaterial.GetColor("_SpecColor"));

        if (sourceMaterial.HasProperty("_Smoothness"))
            destinationMaterial.SetFloat("_Smoothness", sourceMaterial.GetFloat("_Smoothness"));

        if (sourceMaterial.HasProperty("_SpecularStrength"))
            destinationMaterial.SetFloat("_SpecularStrength", sourceMaterial.GetFloat("_SpecularStrength"));

        bool alphaClip = sourceMaterial.HasProperty("_AlphaClip") && sourceMaterial.GetFloat("_AlphaClip") > 0.5f;
        destinationMaterial.SetFloat("_AlphaClip", alphaClip ? 1.0f : 0.0f);
    }

    private static bool TryGetTexture(Material material, out Texture texture, out Vector2 scale, out Vector2 offset)
    {
        string[] candidates =
        {
            "_BaseMap",
            "_MainTex"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string propertyName = candidates[i];
            if (!material.HasProperty(propertyName))
                continue;

            texture = material.GetTexture(propertyName);
            scale = material.GetTextureScale(propertyName);
            offset = material.GetTextureOffset(propertyName);
            return texture != null;
        }

        texture = null;
        scale = Vector2.one;
        offset = Vector2.zero;
        return false;
    }

    private static bool TryGetColor(Material material, out Color color)
    {
        string[] candidates =
        {
            "_BaseColor",
            "_Color"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string propertyName = candidates[i];
            if (!material.HasProperty(propertyName))
                continue;

            color = material.GetColor(propertyName);
            return true;
        }

        color = Color.white;
        return false;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
        Vector3 extents = bounds.extents;
        Vector3 axisX = new Vector3(matrix.m00, matrix.m10, matrix.m20);
        Vector3 axisY = new Vector3(matrix.m01, matrix.m11, matrix.m21);
        Vector3 axisZ = new Vector3(matrix.m02, matrix.m12, matrix.m22);
        Vector3 transformedExtents =
            Abs(axisX) * extents.x +
            Abs(axisY) * extents.y +
            Abs(axisZ) * extents.z;
        return new Bounds(center, transformedExtents * 2.0f);
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private bool TryGetResolvedTerrain(out Terrain terrain)
    {
        terrain = null;
        if (!_enableTerrainCollision)
            return false;

        if (_terrain != null && _terrain.terrainData == null)
            _terrain = null;

        if (_autoResolveTerrain && _terrain == null)
            _terrain = FindPreferredTerrain();

        if (_terrain == null || _terrain.terrainData == null)
            return false;

        terrain = _terrain;
        return true;
    }

    private bool TryGetResolvedStaticSdf(out Texture3D staticSdfTexture)
    {
        staticSdfTexture = null;
        if (!_enableStaticSdfCollision || _staticSdfTexture == null)
            return false;

        if (_staticSdfWorldSize.x <= 0.0f || _staticSdfWorldSize.y <= 0.0f || _staticSdfWorldSize.z <= 0.0f)
            return false;

        staticSdfTexture = _staticSdfTexture;
        return true;
    }

    private Texture3D GetFallbackStaticSdfTexture()
    {
        if (_fallbackStaticSdfTexture != null)
            return _fallbackStaticSdfTexture;

        _fallbackStaticSdfTexture = new Texture3D(1, 1, 1, TextureFormat.RGBAFloat, false)
        {
            name = "CrowdVatIndirectFallbackStaticSdf",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        _fallbackStaticSdfTexture.SetPixels(new[] { new Color(1024.0f, 0.0f, 0.0f, 0.0f) });
        _fallbackStaticSdfTexture.Apply(false, true);
        return _fallbackStaticSdfTexture;
    }

    private Vector3 ApplyTerrainHeightToLocalPosition(Vector3 localPosition)
    {
        if (!TryGetResolvedTerrain(out Terrain terrain))
            return localPosition;

        Vector3 terrainMin = terrain.transform.position;
        Vector3 terrainMax = terrainMin + terrain.terrainData.size;
        Vector3 worldPosition = transform.TransformPoint(localPosition);

        if (worldPosition.x < terrainMin.x || worldPosition.x > terrainMax.x ||
            worldPosition.z < terrainMin.z || worldPosition.z > terrainMax.z)
        {
            return localPosition;
        }

        worldPosition.y = terrain.SampleHeight(worldPosition) + terrainMin.y + _terrainHeightOffset;
        return transform.InverseTransformPoint(worldPosition);
    }

    private void EnsureSpatialQueryReadbackCaches()
    {
        if (_spatialQueryUploadCache == null || _spatialQueryUploadCache.Length != _maxSpatialQueries)
            _spatialQueryUploadCache = new SpatialQueryData[_maxSpatialQueries];

        if (_spatialQueryResultCache == null || _spatialQueryResultCache.Length != _maxSpatialQueries)
            _spatialQueryResultCache = new SpatialQueryResultData[_maxSpatialQueries];

        int hitCapacity = _maxSpatialQueries * _maxHitsPerSpatialQuery;
        if (_spatialQueryHitCache == null || _spatialQueryHitCache.Length != hitCapacity)
            _spatialQueryHitCache = new SpatialQueryHitData[hitCapacity];
    }
}
