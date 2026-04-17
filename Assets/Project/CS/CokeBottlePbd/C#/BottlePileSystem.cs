using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class BottlePileSystem : MonoBehaviour
{
    private const string CharacterProxyObjectName = "BottlePileCharacterProxyBox";

    public struct DebugStatsSnapshot
    {
        public uint maxCellOccupancy;
        public uint occupiedCellCount;
        public uint gridBuildCount;
        public uint contactPairCount;
    }

    private const int ParticlesPerBottle = 6;
    private const int ThreadGroupSize = 64;
    private const int MaxCellPairsPerOccupiedCell = 27;
    private const int RadixBucketCount = 256;
    private const int DebugStatsCount = 4;

    private static readonly int ParticleCountId = Shader.PropertyToID("_ParticleCount");
    private static readonly int BottleCountId = Shader.PropertyToID("_BottleCount");
    private static readonly int ParticlesPerBottleId = Shader.PropertyToID("_ParticlesPerBottle");
    private static readonly int PaddedParticleCountId = Shader.PropertyToID("_PaddedParticleCount");
    private static readonly int GridDimId = Shader.PropertyToID("_GridDim");
    private static readonly int GridCellCountId = Shader.PropertyToID("_GridCellCount");
    private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
    private static readonly int InvCellSizeId = Shader.PropertyToID("_InvCellSize");
    private static readonly int SortMergeRunWidthId = Shader.PropertyToID("_SortMergeRunWidth");
    private static readonly int RadixShiftId = Shader.PropertyToID("_RadixShift");
    private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
    private static readonly int SubstepDeltaTimeId = Shader.PropertyToID("_SubstepDeltaTime");
    private static readonly int GravityId = Shader.PropertyToID("_Gravity");
    private static readonly int VelocityDampingId = Shader.PropertyToID("_VelocityDamping");
    private static readonly int ShapeStiffnessId = Shader.PropertyToID("_ShapeStiffness");
    private static readonly int ContactSlopId = Shader.PropertyToID("_ContactSlop");
    private static readonly int MaxSpeedId = Shader.PropertyToID("_MaxSpeed");
    private static readonly int SleepFrameThresholdId = Shader.PropertyToID("_SleepFrameThreshold");
    private static readonly int SleepSpeedThresholdId = Shader.PropertyToID("_SleepSpeedThreshold");
    private static readonly int SleepCharacterWakeRadiusId = Shader.PropertyToID("_SleepCharacterWakeRadius");
    private static readonly int SleepActiveNeighborWakeRadiusId = Shader.PropertyToID("_SleepActiveNeighborWakeRadius");
    private static readonly int UseCollisionCellFilterId = Shader.PropertyToID("_UseCollisionCellFilter");
    private static readonly int GridBoundsMinId = Shader.PropertyToID("_GridBoundsMin");
    private static readonly int GridBoundsMaxId = Shader.PropertyToID("_GridBoundsMax");
    private static readonly int SimulationBoundsMinId = Shader.PropertyToID("_SimulationBoundsMin");
    private static readonly int SimulationBoundsMaxId = Shader.PropertyToID("_SimulationBoundsMax");
    private static readonly int RenderScaleId = Shader.PropertyToID("_RenderScale");
    private static readonly int MeshOffsetId = Shader.PropertyToID("_MeshOffset");
    private static readonly int EnableDebugStatsId = Shader.PropertyToID("_EnableDebugStats");
    private static readonly int ParticleStaticBufferId = Shader.PropertyToID("_ParticleStaticBuffer");
    private static readonly int ParticlePositionBufferId = Shader.PropertyToID("_ParticlePositionBuffer");
    private static readonly int ParticleVelocityBufferId = Shader.PropertyToID("_ParticleVelocityBuffer");
    private static readonly int ParticlePositionReadBufferId = Shader.PropertyToID("_ParticlePositionReadBuffer");
    private static readonly int ParticleVelocityReadBufferId = Shader.PropertyToID("_ParticleVelocityReadBuffer");
    private static readonly int PredictedReadBufferId = Shader.PropertyToID("_PredictedReadBuffer");
    private static readonly int PredictedWriteBufferId = Shader.PropertyToID("_PredictedWriteBuffer");
    private static readonly int GridKeyIndexBufferId = Shader.PropertyToID("_GridKeyIndexBuffer");
    private static readonly int GridKeyIndexReadBufferId = Shader.PropertyToID("_GridKeyIndexReadBuffer");
    private static readonly int RadixInputBufferId = Shader.PropertyToID("_RadixInputBuffer");
    private static readonly int RadixOutputBufferId = Shader.PropertyToID("_RadixOutputBuffer");
    private static readonly int RadixHistogramBufferId = Shader.PropertyToID("_RadixHistogramBuffer");
    private static readonly int RadixOffsetBufferId = Shader.PropertyToID("_RadixOffsetBuffer");
    private static readonly int CellStartBufferId = Shader.PropertyToID("_CellStartBuffer");
    private static readonly int CellEndBufferId = Shader.PropertyToID("_CellEndBuffer");
    private static readonly int CellStartReadBufferId = Shader.PropertyToID("_CellStartReadBuffer");
    private static readonly int CellEndReadBufferId = Shader.PropertyToID("_CellEndReadBuffer");
    private static readonly int OccupiedCellBufferId = Shader.PropertyToID("_OccupiedCellBuffer");
    private static readonly int OccupiedCellCounterBufferId = Shader.PropertyToID("_OccupiedCellCounterBuffer");
    private static readonly int OccupiedCellReadBufferId = Shader.PropertyToID("_OccupiedCellReadBuffer");
    private static readonly int OccupiedCellCounterReadBufferId = Shader.PropertyToID("_OccupiedCellCounterReadBuffer");
    private static readonly int CellPairBufferId = Shader.PropertyToID("_CellPairBuffer");
    private static readonly int CellPairCountBufferId = Shader.PropertyToID("_CellPairCountBuffer");
    private static readonly int CellPairReadBufferId = Shader.PropertyToID("_CellPairReadBuffer");
    private static readonly int CellPairCountReadBufferId = Shader.PropertyToID("_CellPairCountReadBuffer");
    private static readonly int CollisionCellBufferId = Shader.PropertyToID("_CollisionCellBuffer");
    private static readonly int CollisionCellCounterBufferId = Shader.PropertyToID("_CollisionCellCounterBuffer");
    private static readonly int CollisionCellFlagBufferId = Shader.PropertyToID("_CollisionCellFlagBuffer");
    private static readonly int CollisionCellReadBufferId = Shader.PropertyToID("_CollisionCellReadBuffer");
    private static readonly int CollisionCellCounterReadBufferId = Shader.PropertyToID("_CollisionCellCounterReadBuffer");
    private static readonly int CollisionCellFlagReadBufferId = Shader.PropertyToID("_CollisionCellFlagReadBuffer");
    private static readonly int BottlePoseBufferId = Shader.PropertyToID("_BottlePoseBuffer");
    private static readonly int BottlePoseReadBufferId = Shader.PropertyToID("_BottlePoseReadBuffer");
    private static readonly int BottleFlagsBufferId = Shader.PropertyToID("_BottleFlagsBuffer");
    private static readonly int BottleSleepCounterBufferId = Shader.PropertyToID("_BottleSleepCounterBuffer");
    private static readonly int BottleWakeRequestBufferId = Shader.PropertyToID("_BottleWakeRequestBuffer");
    private static readonly int ActiveBottleListBufferId = Shader.PropertyToID("_ActiveBottleListBuffer");
    private static readonly int ActiveBottleCounterBufferId = Shader.PropertyToID("_ActiveBottleCounterBuffer");
    private static readonly int ActiveBottleListReadBufferId = Shader.PropertyToID("_ActiveBottleListReadBuffer");
    private static readonly int ActiveBottleCounterReadBufferId = Shader.PropertyToID("_ActiveBottleCounterReadBuffer");
    private static readonly int ActiveBottleDispatchArgsBufferId = Shader.PropertyToID("_ActiveBottleDispatchArgsBuffer");
    private static readonly int CharacterColliderCountId = Shader.PropertyToID("_CharacterColliderCount");
    private static readonly int CharacterColliderBufferId = Shader.PropertyToID("_CharacterColliderBuffer");
    private static readonly int InstanceTransformBufferId = Shader.PropertyToID("_InstanceTransformBuffer");
    private static readonly int ContactAccumBufferId = Shader.PropertyToID("_ContactAccumBuffer");
    private static readonly int ContactAccumReadBufferId = Shader.PropertyToID("_ContactAccumReadBuffer");
    private static readonly int DebugStatsBufferId = Shader.PropertyToID("_DebugStatsBuffer");
    private static readonly int InstanceTransformsId = Shader.PropertyToID("_InstanceTransforms");

    [Header("资源引用")]
    [Tooltip("瓶堆求解使用的 Compute Shader。")]
    [SerializeField] private ComputeShader _solverCompute;
    [Tooltip("实例化渲染材质。通常由 Archetype 自动带入。")]
    [SerializeField] private Material _instancedMaterial;
    [Tooltip("瓶子占位网格。为空时运行时会使用内置 Capsule。")]
    [SerializeField] private Mesh _bottleMesh;
    [Tooltip("玩家的盒体代理碰撞器，用于把角色推挤写入 solver。")]
    [SerializeField] private BoxCollider _characterBoxCollider;
    [Tooltip("玩家 CharacterController，用于自动查找角色。")]
    [SerializeField] private CharacterController _characterController;
    [Tooltip("角色根节点。找不到引用时会尝试自动补。")]
    [SerializeField] private Transform _characterTransform;

    [Header("资产与作者参数")]
    [Tooltip("瓶子形体与渲染资源配置。")]
    [SerializeField] private BottleArchetypeAsset _archetype;
    [Tooltip("瓶堆生成范围与初始分布参数。")]
    [SerializeField] private BottlePileVolume _pileVolume;
    [Tooltip("角色推挤与代理盒体参数。")]
    [SerializeField] private BottleInteractionProfile _interactionProfile;
    [Tooltip("求解器全局配置。")]
    [SerializeField] private BottleSimulationConfig _simulationConfig;
    [Tooltip("启用后，瓶子允许活动的 XZ 范围会对齐整个 Terrain；Y 仍使用当前模拟高度。")]
    [SerializeField] private bool _useTerrainBoundsXZ = true;
    [Tooltip("用于扩展活动范围的 Terrain。为空时自动取场景里的 Terrain。")]
    [SerializeField] private Terrain _simulationTerrain;

    [Header("瓶体")]
    [Tooltip("瓶子占位高度。")]
    [SerializeField] [Min(0.05f)] private float _bottleHeight = 0.24f;
    [Tooltip("瓶子占位半径。")]
    [SerializeField] [Min(0.01f)] private float _bottleRadius = 0.04f;
    [Tooltip("单个代理粒子的碰撞半径。")]
    [SerializeField] [Min(0.005f)] private float _particleRadius = 0.028f;
    [Tooltip("自定义网格缩放。")]
    [SerializeField] private Vector3 _meshScale = Vector3.one;
    [Tooltip("网格相对瓶体姿态中心的局部偏移。")]
    [SerializeField] private Vector3 _meshOffset = Vector3.zero;
    [FormerlySerializedAs("_autoScaleBuiltInCylinder")]
    [Tooltip("未指定网格时，是否自动缩放内置 Capsule。")]
    [SerializeField] private bool _autoScaleBuiltInCapsule = true;

    [Header("瓶堆生成")]
    [Tooltip("生成的瓶子总数。")]
    [SerializeField] [Min(1)] private int _bottleCount = 512;
    [Tooltip("瓶堆初始排布体积。")]
    [SerializeField] private Vector3 _pileSize = new Vector3(2.8f, 2.4f, 2.8f);
    [Tooltip("随机种子。相同种子可复现布局。")]
    [SerializeField] private int _randomSeed = 12345;
    [Tooltip("水平方向随机扰动比例。")]
    [SerializeField] [Range(0.0f, 0.5f)] private float _horizontalJitterFraction = 0.12f;
    [Tooltip("初始最大倾斜角度。")]
    [SerializeField] [Min(0.0f)] private float _maxTiltDegrees = 6.0f;

    [Header("模拟")]
    [Tooltip("每个 substep 内的求解迭代次数。")]
    [SerializeField] [Min(1)] private int _solverIterations = 1;
    [Tooltip("每帧拆分的物理小步数。")]
    [SerializeField] [Min(1)] private int _substeps = 2;
    [Tooltip("邻域网格单元尺寸。")]
    [SerializeField] [Min(0.01f)] private float _cellSize = 0.09f;
    [Tooltip("形状匹配强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _shapeStiffness = 0.78f;
    [Tooltip("速度阻尼。越接近 1，能量保留越多。")]
    [SerializeField] [Range(0.9f, 1.0f)] private float _velocityDamping = 0.992f;
    [Tooltip("重力加速度。")]
    [SerializeField] private float _gravity = -9.81f;
    [Tooltip("接触容差。")]
    [SerializeField] [Min(0.0f)] private float _contactSlop = 0.0015f;
    [Tooltip("最大速度限制。")]
    [SerializeField] [Min(0.1f)] private float _maxSpeed = 15.0f;
    [Tooltip("连续静止多少帧后进入休眠。")]
    [SerializeField] [Min(1)] private int _sleepFrameThreshold = 18;
    [Tooltip("低于该速度阈值时可累计休眠。")]
    [SerializeField] [Min(0.0f)] private float _sleepSpeedThreshold = 0.05f;
    [Tooltip("角色接近该半径内时，休眠瓶会被唤醒。")]
    [SerializeField] [Min(0.0f)] private float _sleepCharacterWakeRadius = 1.4f;
    [Tooltip("活跃瓶邻近传播的预唤醒半径。")]
    [SerializeField] [Min(0.0f)] private float _sleepActiveNeighborWakeRadius = 0.4f;
    [Tooltip("局部 broadphase 窗口尺寸。未启用 Terrain 对齐时也作为完整模拟边界。")]
    [SerializeField] private Vector3 _simulationBoundsSize = new Vector3(14.0f, 8.0f, 14.0f);
    [Tooltip("邻域网格刷新频率。PerSubstep 更省，PerIteration 更稳。")]
    [SerializeField] private BottleSimulationConfig.GridRefreshMode _gridRefreshMode = BottleSimulationConfig.GridRefreshMode.PerSubstep;

    [Header("角色交互")]
    [Tooltip("角色基础推挤强度。")]
    [SerializeField] [Min(0.0f)] private float _characterPushBase = 0.12f;
    [Tooltip("角色速度转成额外推挤的比例。")]
    [SerializeField] [Min(0.0f)] private float _characterPushFromSpeed = 0.18f;
    [Tooltip("角色速度低于该阈值时，不主动推瓶，只保留盒体阻挡。")]
    [SerializeField] [Min(0.0f)] private float _characterPushActivationSpeed = 0.08f;
    [Tooltip("角色切向阻尼。")]
    [SerializeField] [Min(0.0f)] private float _characterTangentialDamping = 0.08f;
    [Tooltip("优先查找带此前缀的骨骼交互代理碰撞体，例如四肢与躯干代理。")]
    [SerializeField] private string _characterProxyColliderPrefix = "BottleInteractionProxy_";
    [Tooltip("每帧写入 solver 的角色交互碰撞体上限。")]
    [SerializeField] [Min(1)] private int _maxCharacterInteractionColliders = 12;
    [Tooltip("交互碰撞体任一轴长度低于该值时会被忽略，避免碎小配件把球抖动得太碎。")]
    [SerializeField] [Min(0.0f)] private float _minCharacterInteractionColliderSize = 0.05f;
    [Tooltip("找不到真实角色代理时，备用盒体中心。")]
    [SerializeField] private Vector3 _fallbackCharacterBoxCenter = new Vector3(0.0f, 0.5f, 0.0f);
    [FormerlySerializedAs("_fallbackCharacterRadius")]
    [FormerlySerializedAs("_fallbackCharacterHeight")]
    [Tooltip("找不到真实角色代理时，备用盒体尺寸。")]
    [SerializeField] private Vector3 _fallbackCharacterBoxSize = Vector3.one;

    [Header("渲染")]
    [Tooltip("实例阴影模式。当前默认关闭以压低成本。")]
    [SerializeField] private ShadowCastingMode _shadowCastingMode = ShadowCastingMode.Off;
    [Tooltip("是否接收阴影。")]
    [SerializeField] private bool _receiveShadows = false;
    [Tooltip("渲染 bounds 额外外扩，避免整 pile 被裁得过紧。")]
    [SerializeField] [Min(0.0f)] private float _renderBoundsPadding = 1.5f;
    [Tooltip("是否启用单 pile 级 CullingGroup 粗剔除。")]
    [SerializeField] private bool _enableCullingGroup = true;
    [Tooltip("CullingGroup 球体半径的额外外扩。")]
    [SerializeField] [Min(0.0f)] private float _cullingSpherePadding = 2.0f;
    [Tooltip("每个渲染 chunk 包含的瓶子数量。用于多命令 indirect 提交与更细粒度剔除。")]
    [SerializeField] [Min(1)] private int _renderChunkBottleCount = 64;
    [Tooltip("chunk 渲染 bounds 的额外外扩，避免瓶子稍微散开就被裁掉。")]
    [SerializeField] [Min(0.0f)] private float _chunkBoundsPadding = 2.5f;
    [Tooltip("chunk bounds 从 GPU 实例矩阵回读刷新的间隔帧数。值越小越准，但固定成本更高。")]
    [SerializeField] [Min(1)] private int _chunkBoundsReadbackInterval = 6;

    [Header("调试")]
    [Tooltip("在场景视图中绘制模拟边界。")]
    [SerializeField] private bool _drawSimulationBounds = true;
    [Tooltip("在场景视图中绘制瓶堆初始范围。")]
    [SerializeField] private bool _drawPileBounds = true;
    [Tooltip("是否开启 GPU 调试统计回读。开启后会影响性能基线。")]
    [SerializeField] private bool _enableDebugStatsReadback = false;
    [Tooltip("调试统计回读间隔帧数。")]
    [SerializeField] [Min(1)] private int _debugReadbackInterval = 12;

    private ComputeBuffer _particleStaticBuffer;
    private ComputeBuffer _particlePositionBuffer;
    private ComputeBuffer _particleVelocityBuffer;
    private ComputeBuffer _predictedBufferA;
    private ComputeBuffer _predictedBufferB;
    private ComputeBuffer _gridKeyIndexBuffer;
    private ComputeBuffer _gridKeyIndexTempBuffer;
    private ComputeBuffer _radixHistogramBuffer;
    private ComputeBuffer _radixOffsetBuffer;
    private ComputeBuffer _cellStartBuffer;
    private ComputeBuffer _cellEndBuffer;
    private ComputeBuffer _occupiedCellBuffer;
    private ComputeBuffer _occupiedCellCounterBuffer;
    private ComputeBuffer _cellPairBuffer;
    private ComputeBuffer _cellPairCountBuffer;
    private ComputeBuffer _collisionCellBuffer;
    private ComputeBuffer _collisionCellCounterBuffer;
    private ComputeBuffer _collisionCellFlagBuffer;
    private ComputeBuffer _bottlePoseBuffer;
    private ComputeBuffer _bottleFlagsBuffer;
    private ComputeBuffer _bottleSleepCounterBuffer;
    private ComputeBuffer _bottleWakeRequestBuffer;
    private ComputeBuffer _activeBottleListBuffer;
    private ComputeBuffer _activeBottleCounterBuffer;
    private ComputeBuffer _activeBottleDispatchArgsBuffer;
    private ComputeBuffer _characterColliderBuffer;
    private ComputeBuffer _instanceTransformBuffer;
    private ComputeBuffer _contactAccumBuffer;
    private ComputeBuffer _debugStatsBuffer;
    private GraphicsBuffer _indirectCommandBuffer;

    private Material _runtimeMaterial;
    private Mesh _runtimeMesh;
    private CullingGroup _cullingGroup;
    private readonly BoundingSphere[] _cullingSpheres = new BoundingSphere[1];
    private bool _isVisibleByCulling = true;
    private Bounds[] _chunkBounds = Array.Empty<Bounds>();
    private GraphicsBuffer.IndirectDrawIndexedArgs[] _indirectCommands = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
    private MatrixRows[] _chunkBoundsTransformCache = Array.Empty<MatrixRows>();
    private bool _chunkBoundsReadbackPending;
    private int _framesUntilChunkBoundsReadback;
    private int _chunkCount;

    private int _clearOccupiedCellRangesKernel;
    private int _resetOccupiedCellCounterKernel;
    private int _clearCollisionCellFlagsKernel;
    private int _resetCollisionCellCounterKernel;
    private int _resetActiveBottleCounterKernel;
    private int _buildActiveBottleListKernel;
    private int _finalizeActiveBottleDispatchArgsKernel;
    private int _predictKernel;
    private int _buildGridKeysKernel;
    private int _clearContactAccumKernel;
    private int _blockLocalSortKernel;
    private int _mergeSortedBlocksKernel;
    private int _clearRadixHistogramKernel;
    private int _buildRadixHistogramKernel;
    private int _prefixRadixHistogramKernel;
    private int _scatterRadixPairsKernel;
    private int _buildCellRangesKernel;
    private int _buildCellPairListKernel;
    private int _finalizeCellRangesStatsKernel;
    private int _solveStaticKernel;
    private int _solveCharacterKernel;
    private int _solveContactsByCellPairListKernel;
    private int _solveContactsKernel;
    private int _applyContactCorrectionsKernel;
    private int _applyShapeKernel;
    private int _finalizeKernel;
    private int _gatherStateKernel;
    private int _updateBottleSleepStatesKernel;
    private int _gridDimX;
    private int _gridDimY;
    private int _gridDimZ;
    private int _gridCellCount;
    private int _maxOccupiedCellCount;
    private int _paddedParticleCount;
    private int _characterColliderCount;

    private readonly List<BoxCollider> _resolvedCharacterInteractionColliders = new List<BoxCollider>(16);
    private readonly Dictionary<int, Vector3> _lastCharacterColliderCenters = new Dictionary<int, Vector3>(16);
    private CharacterColliderData[] _characterColliderUploadData = Array.Empty<CharacterColliderData>();
    private bool _resourcesDirty = true;
    private bool _debugReadbackPending;
    private int _framesUntilDebugReadback;
    private string _latestDebugStatsSummary = "未采样";
    private DebugStatsSnapshot _latestDebugStats;
    private readonly uint[] _debugStatsReset = new uint[DebugStatsCount];

    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleStaticData
    {
        public Vector3 localRestPosition;
        public float radius;
        public uint bottleIndex;
        public uint localParticleIndex;
        public float inverseMass;
        public float padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CharacterColliderData
    {
        public Vector3 center;
        public float pushStrength;
        public Vector3 halfExtents;
        public float tangentialDamping;
        public Vector3 axisX;
        public float padding0;
        public Vector3 axisY;
        public float padding1;
        public Vector3 axisZ;
        public float padding2;
        public Vector3 deltaPosition;
        public float padding3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BottlePoseData
    {
        public Vector3 center;
        public float padding0;
        public Vector3 axisX;
        public float padding1;
        public Vector3 axisY;
        public float padding2;
        public Vector3 axisZ;
        public float padding3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MatrixRows
    {
        public Vector4 row0;
        public Vector4 row1;
        public Vector4 row2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UInt2Data
    {
        public uint x;
        public uint y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ContactAccumData
    {
        public Vector3 correction;
        public float count;
    }

    public DebugStatsSnapshot LatestDebugStats => _latestDebugStats;
    public string LatestDebugStatsSummary => _latestDebugStatsSummary;

    private void OnEnable()
    {
        ResolveCharacterReference();
        ApplyConfigurationSources();
        InitializeOrRebuild();
    }

    private void OnDisable()
    {
        ReleaseResources();
    }

    private void OnValidate()
    {
        ApplyConfigurationSources();
        ResolveSimulationTerrain();

        _bottleCount = Mathf.Max(1, _bottleCount);
        _bottleHeight = Mathf.Max(0.05f, _bottleHeight);
        _bottleRadius = Mathf.Max(0.01f, _bottleRadius);
        _particleRadius = Mathf.Clamp(_particleRadius, 0.005f, _bottleRadius);
        _pileSize.x = Mathf.Max(_bottleRadius * 2.5f, _pileSize.x);
        _pileSize.y = Mathf.Max(_bottleHeight * 1.5f, _pileSize.y);
        _pileSize.z = Mathf.Max(_bottleRadius * 2.5f, _pileSize.z);
        _horizontalJitterFraction = Mathf.Clamp(_horizontalJitterFraction, 0.0f, 0.5f);
        _maxTiltDegrees = Mathf.Max(0.0f, _maxTiltDegrees);
        _cellSize = Mathf.Max(_particleRadius * 1.25f, _cellSize);
        _solverIterations = Mathf.Max(1, _solverIterations);
        _substeps = Mathf.Max(1, _substeps);
        _sleepFrameThreshold = Mathf.Max(1, _sleepFrameThreshold);
        _sleepSpeedThreshold = Mathf.Max(0.0f, _sleepSpeedThreshold);
        _sleepCharacterWakeRadius = Mathf.Max(0.0f, _sleepCharacterWakeRadius);
        _sleepActiveNeighborWakeRadius = Mathf.Max(0.0f, _sleepActiveNeighborWakeRadius);
        _maxCharacterInteractionColliders = Mathf.Max(1, _maxCharacterInteractionColliders);
        _minCharacterInteractionColliderSize = Mathf.Max(0.0f, _minCharacterInteractionColliderSize);
        _fallbackCharacterBoxSize.x = Mathf.Max(0.1f, _fallbackCharacterBoxSize.x);
        _fallbackCharacterBoxSize.y = Mathf.Max(0.1f, _fallbackCharacterBoxSize.y);
        _fallbackCharacterBoxSize.z = Mathf.Max(0.1f, _fallbackCharacterBoxSize.z);
        _fallbackCharacterBoxCenter.y = Mathf.Max(0.0f, _fallbackCharacterBoxCenter.y);
        _renderChunkBottleCount = Mathf.Max(1, _renderChunkBottleCount);
        _chunkBoundsReadbackInterval = Mathf.Max(1, _chunkBoundsReadbackInterval);
        _simulationBoundsSize.x = Mathf.Max(_pileSize.x + 1.0f, _simulationBoundsSize.x);
        _simulationBoundsSize.y = Mathf.Max(_pileSize.y + 1.0f, _simulationBoundsSize.y);
        _simulationBoundsSize.z = Mathf.Max(_pileSize.z + 1.0f, _simulationBoundsSize.z);
        _debugReadbackInterval = Mathf.Max(1, _debugReadbackInterval);
        _resourcesDirty = true;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        if (!EnsureResources())
            return;

        Simulate(Time.deltaTime);
        Draw();
    }

    [ContextMenu("Rebuild Bottle Pile")]
    public void InitializeOrRebuild()
    {
        ReleaseSimulationBuffers();
        _resourcesDirty = true;
        EnsureResources();
    }

    private bool EnsureResources()
    {
        if (_solverCompute == null)
            return false;

        ApplyConfigurationSources();
        ResolveSimulationTerrain();
        ResolveCharacterReference();
        EnsureRuntimeMaterial();
        EnsureRuntimeMesh();
        EnsureKernelIds();

        if (!_resourcesDirty &&
            _particlePositionBuffer != null &&
            _particleVelocityBuffer != null &&
            _instanceTransformBuffer != null &&
            _indirectCommandBuffer != null &&
            _runtimeMaterial != null &&
            _runtimeMesh != null &&
            !NeedsResourceRebuild())
        {
            return true;
        }

        BuildSimulationBuffers();
        _resourcesDirty = false;
        return _particlePositionBuffer != null;
    }

    private bool NeedsResourceRebuild()
    {
        int particleCount = _bottleCount * ParticlesPerBottle;
        int desiredPaddedParticleCount = Mathf.NextPowerOfTwo(Mathf.Max(1, particleCount));
        Vector3 boundsSize = GetBroadphaseBoundsMax() - GetBroadphaseBoundsMin();
        int desiredGridDimX = Mathf.Max(1, Mathf.CeilToInt(boundsSize.x / _cellSize));
        int desiredGridDimY = Mathf.Max(1, Mathf.CeilToInt(boundsSize.y / _cellSize));
        int desiredGridDimZ = Mathf.Max(1, Mathf.CeilToInt(boundsSize.z / _cellSize));
        int desiredGridCellCount = Mathf.Max(1, desiredGridDimX * desiredGridDimY * desiredGridDimZ);
        int desiredMaxOccupiedCellCount = Mathf.Max(1, Mathf.Min(desiredGridCellCount, particleCount));

        return _paddedParticleCount != desiredPaddedParticleCount
            || _gridDimX != desiredGridDimX
            || _gridDimY != desiredGridDimY
            || _gridDimZ != desiredGridDimZ
            || _gridCellCount != desiredGridCellCount
            || _maxOccupiedCellCount != desiredMaxOccupiedCellCount;
    }

    private void ApplyConfigurationSources()
    {
        if (_pileVolume == null)
            _pileVolume = GetComponent<BottlePileVolume>();

        if (_archetype != null)
        {
            _bottleMesh = _archetype.BottleMesh;
            _instancedMaterial = _archetype.InstancedMaterial;
            _bottleHeight = _archetype.BottleHeight;
            _bottleRadius = _archetype.BottleRadius;
            _particleRadius = _archetype.ParticleRadius;
            _autoScaleBuiltInCapsule = _archetype.AutoScaleBuiltInCapsule;
            _meshScale = _archetype.MeshScale;
            _meshOffset = _archetype.MeshOffset;
        }

        if (_pileVolume != null)
        {
            _bottleCount = _pileVolume.BottleCount;
            _pileSize = _pileVolume.PileSize;
            _randomSeed = _pileVolume.RandomSeed;
            _horizontalJitterFraction = _pileVolume.HorizontalJitterFraction;
            _maxTiltDegrees = _pileVolume.MaxTiltDegrees;
        }

        if (_interactionProfile != null)
        {
            _characterPushBase = _interactionProfile.CharacterPushBase;
            _characterPushFromSpeed = _interactionProfile.CharacterPushFromSpeed;
            _characterPushActivationSpeed = _interactionProfile.CharacterPushActivationSpeed;
            _characterTangentialDamping = _interactionProfile.CharacterTangentialDamping;
            _fallbackCharacterBoxCenter = _interactionProfile.FallbackCharacterBoxCenter;
            _fallbackCharacterBoxSize = _interactionProfile.FallbackCharacterBoxSize;
        }

        if (_simulationConfig != null)
        {
            _solverIterations = _simulationConfig.SolverIterations;
            _substeps = _simulationConfig.Substeps;
            _cellSize = _simulationConfig.CellSize;
            _shapeStiffness = _simulationConfig.ShapeStiffness;
            _velocityDamping = _simulationConfig.VelocityDamping;
            _gravity = _simulationConfig.Gravity;
            _contactSlop = _simulationConfig.ContactSlop;
            _maxSpeed = _simulationConfig.MaxSpeed;
            _sleepFrameThreshold = _simulationConfig.SleepFrameThreshold;
            _sleepSpeedThreshold = _simulationConfig.SleepSpeedThreshold;
            _sleepCharacterWakeRadius = _simulationConfig.SleepCharacterWakeRadius;
            _sleepActiveNeighborWakeRadius = _simulationConfig.SleepActiveNeighborWakeRadius;
            _simulationBoundsSize = _simulationConfig.SimulationBoundsSize;
            _gridRefreshMode = _simulationConfig.GridRefresh;
        }
    }

    private void ResolveCharacterReference()
    {
        if (_characterController != null && !_characterController.gameObject.activeInHierarchy)
            _characterController = null;

        if (_characterTransform != null && !_characterTransform.gameObject.activeInHierarchy)
            _characterTransform = null;

        if (_characterBoxCollider != null && !_characterBoxCollider.gameObject.activeInHierarchy)
            _characterBoxCollider = null;

        if (_characterTransform == null && _characterBoxCollider != null)
            _characterTransform = _characterBoxCollider.transform.root;

        if (_characterTransform != null)
        {
            if (_characterController == null)
                _characterController = _characterTransform.GetComponent<CharacterController>();

            if (_characterBoxCollider == null)
                _characterBoxCollider = FindCharacterProxyBoxCollider(_characterTransform);
        }

        if (_characterController == null)
            _characterController = FindPreferredCharacterController();

        if (_characterTransform == null && _characterController != null)
            _characterTransform = _characterController.transform;

        if (_characterTransform == null && Camera.main != null)
            _characterTransform = Camera.main.transform;

        if (_characterTransform != null)
        {
            if (_characterController == null)
                _characterController = _characterTransform.GetComponent<CharacterController>();

            if (_characterController == null)
                _characterController = _characterTransform.GetComponent<CharacterController>();
        }

        if (_characterController != null)
        {
            _characterTransform = _characterController.transform;
            _characterBoxCollider = EnsureCharacterProxyBoxCollider(_characterController);
        }
        else if (_characterTransform != null && _characterBoxCollider == null)
        {
            _characterBoxCollider = FindCharacterProxyBoxCollider(_characterTransform);
        }
    }

    private CharacterController FindPreferredCharacterController()
    {
        CharacterController[] controllers = FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            CharacterController controller = controllers[i];
            if (controller != null && controller.CompareTag("Player"))
                return controller;
        }

        return controllers.Length > 0 ? controllers[0] : null;
    }

    private BoxCollider FindCharacterProxyBoxCollider(Transform root)
    {
        if (root == null)
            return null;

        Transform proxyTransform = root.Find(CharacterProxyObjectName);
        if (proxyTransform != null)
            return proxyTransform.GetComponent<BoxCollider>();

        BoxCollider[] colliders = root.GetComponentsInChildren<BoxCollider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider collider = colliders[i];
            if (collider != null && string.Equals(collider.name, CharacterProxyObjectName, StringComparison.Ordinal))
                return collider;
        }

        return null;
    }

    private BoxCollider EnsureCharacterProxyBoxCollider(CharacterController characterController)
    {
        if (characterController == null)
            return null;

        BoxCollider boxCollider = FindCharacterProxyBoxCollider(characterController.transform);
        if (boxCollider == null)
        {
            GameObject proxyObject = new GameObject(CharacterProxyObjectName);
            proxyObject.transform.SetParent(characterController.transform, false);
            boxCollider = proxyObject.AddComponent<BoxCollider>();
        }

        boxCollider.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        boxCollider.transform.localScale = Vector3.one;
        boxCollider.center = _fallbackCharacterBoxCenter;
        boxCollider.size = _fallbackCharacterBoxSize;
        boxCollider.isTrigger = true;
        boxCollider.enabled = true;
        return boxCollider;
    }

    private void EnsureRuntimeMaterial()
    {
        Shader targetShader = _instancedMaterial != null
            ? _instancedMaterial.shader
            : Shader.Find("Project/CokeBottlePbd/InstancedLit");
        if (targetShader == null)
            return;

        if (_runtimeMaterial != null && _runtimeMaterial.shader == targetShader)
            return;

        if (_runtimeMaterial != null)
            DestroyRuntimeMaterial();

        _runtimeMaterial = _instancedMaterial != null
            ? new Material(_instancedMaterial)
            : new Material(targetShader);
        _runtimeMaterial.enableInstancing = true;
    }

    private void EnsureRuntimeMesh()
    {
        if (_bottleMesh != null)
        {
            _runtimeMesh = _bottleMesh;
            return;
        }

        if (_runtimeMesh != null)
            return;

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        primitive.hideFlags = HideFlags.HideAndDontSave;
        _runtimeMesh = primitive.GetComponent<MeshFilter>().sharedMesh;

        if (Application.isPlaying)
            Destroy(primitive);
        else
            DestroyImmediate(primitive);
    }

    private void EnsureKernelIds()
    {
        _clearOccupiedCellRangesKernel = _solverCompute.FindKernel("ClearOccupiedCellRanges");
        _resetOccupiedCellCounterKernel = _solverCompute.FindKernel("ResetOccupiedCellCounter");
        _clearCollisionCellFlagsKernel = _solverCompute.FindKernel("ClearCollisionCellFlags");
        _resetCollisionCellCounterKernel = _solverCompute.FindKernel("ResetCollisionCellCounter");
        _resetActiveBottleCounterKernel = _solverCompute.FindKernel("ResetActiveBottleCounter");
        _buildActiveBottleListKernel = _solverCompute.FindKernel("BuildActiveBottleList");
        _finalizeActiveBottleDispatchArgsKernel = _solverCompute.FindKernel("FinalizeActiveBottleDispatchArgs");
        _predictKernel = _solverCompute.FindKernel("Predict");
        _buildGridKeysKernel = _solverCompute.FindKernel("BuildGridKeys");
        _clearContactAccumKernel = _solverCompute.FindKernel("ClearContactAccum");
        _blockLocalSortKernel = _solverCompute.FindKernel("BlockLocalSort");
        _mergeSortedBlocksKernel = _solverCompute.FindKernel("MergeSortedBlocks");
        _clearRadixHistogramKernel = _solverCompute.FindKernel("ClearRadixHistogram");
        _buildRadixHistogramKernel = _solverCompute.FindKernel("BuildRadixHistogram");
        _prefixRadixHistogramKernel = _solverCompute.FindKernel("PrefixRadixHistogram");
        _scatterRadixPairsKernel = _solverCompute.FindKernel("ScatterRadixPairs");
        _buildCellRangesKernel = _solverCompute.FindKernel("BuildCellRanges");
        _buildCellPairListKernel = _solverCompute.FindKernel("BuildCellPairList");
        _finalizeCellRangesStatsKernel = _solverCompute.FindKernel("FinalizeCellRangesStats");
        _solveStaticKernel = _solverCompute.FindKernel("SolveStaticColliders");
        _solveCharacterKernel = _solverCompute.FindKernel("SolveCharacterCollider");
        _solveContactsByCellPairListKernel = _solverCompute.FindKernel("SolveContactsByCellPairList");
        _solveContactsKernel = _solverCompute.FindKernel("SolveParticleContacts");
        _applyContactCorrectionsKernel = _solverCompute.FindKernel("ApplyContactCorrections");
        _applyShapeKernel = _solverCompute.FindKernel("ApplyShapeMatching");
        _finalizeKernel = _solverCompute.FindKernel("FinalizeParticles");
        _gatherStateKernel = _solverCompute.FindKernel("UpdateBottlePosesFromState");
        _updateBottleSleepStatesKernel = _solverCompute.FindKernel("UpdateBottleSleepStates");
    }

    private void BuildSimulationBuffers()
    {
        ReleaseSimulationBuffers();

        Vector3 boundsMin = GetBroadphaseBoundsMin();
        Vector3 boundsMax = GetBroadphaseBoundsMax();
        Vector3 boundsSize = boundsMax - boundsMin;

        _gridDimX = Mathf.Max(1, Mathf.CeilToInt(boundsSize.x / _cellSize));
        _gridDimY = Mathf.Max(1, Mathf.CeilToInt(boundsSize.y / _cellSize));
        _gridDimZ = Mathf.Max(1, Mathf.CeilToInt(boundsSize.z / _cellSize));
        _gridCellCount = Mathf.Max(1, _gridDimX * _gridDimY * _gridDimZ);

        int particleCount = _bottleCount * ParticlesPerBottle;
        _paddedParticleCount = Mathf.NextPowerOfTwo(Mathf.Max(1, particleCount));

        ParticleStaticData[] staticData = new ParticleStaticData[particleCount];
        Vector4[] initialPositions = new Vector4[particleCount];
        Vector4[] initialVelocities = new Vector4[particleCount];
        MatrixRows[] transformRows = new MatrixRows[_bottleCount];
        BottlePoseData[] initialBottlePoses = new BottlePoseData[_bottleCount];

        BuildInitialState(staticData, initialPositions, initialVelocities, transformRows, initialBottlePoses);

        _particleStaticBuffer = new ComputeBuffer(particleCount, Marshal.SizeOf<ParticleStaticData>());
        _particleStaticBuffer.SetData(staticData);

        _particlePositionBuffer = new ComputeBuffer(particleCount, Marshal.SizeOf<Vector4>());
        _particlePositionBuffer.SetData(initialPositions);
        _particleVelocityBuffer = new ComputeBuffer(particleCount, Marshal.SizeOf<Vector4>());
        _particleVelocityBuffer.SetData(initialVelocities);

        _predictedBufferA = new ComputeBuffer(particleCount, Marshal.SizeOf<Vector4>());
        _predictedBufferB = new ComputeBuffer(particleCount, Marshal.SizeOf<Vector4>());
        _predictedBufferA.SetData(initialPositions);
        _predictedBufferB.SetData(initialPositions);

        UInt2Data[] initialGridPairs = new UInt2Data[_paddedParticleCount];
        for (int i = 0; i < initialGridPairs.Length; i++)
        {
            initialGridPairs[i].x = uint.MaxValue;
            initialGridPairs[i].y = uint.MaxValue;
        }

        _gridKeyIndexBuffer = new ComputeBuffer(_paddedParticleCount, Marshal.SizeOf<UInt2Data>());
        _gridKeyIndexBuffer.SetData(initialGridPairs);
        _gridKeyIndexTempBuffer = new ComputeBuffer(_paddedParticleCount, Marshal.SizeOf<UInt2Data>());
        _gridKeyIndexTempBuffer.SetData(initialGridPairs);
        _radixHistogramBuffer = new ComputeBuffer(RadixBucketCount, sizeof(uint));
        _radixHistogramBuffer.SetData(new uint[RadixBucketCount]);
        _radixOffsetBuffer = new ComputeBuffer(RadixBucketCount, sizeof(uint));
        _radixOffsetBuffer.SetData(new uint[RadixBucketCount]);

        uint[] initialCellRange = new uint[_gridCellCount];
        for (int i = 0; i < initialCellRange.Length; i++)
            initialCellRange[i] = uint.MaxValue;

        int maxOccupiedCells = Mathf.Max(1, Mathf.Min(_gridCellCount, particleCount));
        _maxOccupiedCellCount = maxOccupiedCells;
        _cellStartBuffer = new ComputeBuffer(_gridCellCount, sizeof(uint));
        _cellStartBuffer.SetData(initialCellRange);
        _cellEndBuffer = new ComputeBuffer(_gridCellCount, sizeof(uint));
        _cellEndBuffer.SetData(initialCellRange);
        _occupiedCellBuffer = new ComputeBuffer(maxOccupiedCells, sizeof(uint));
        _occupiedCellCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _occupiedCellCounterBuffer.SetData(new uint[1]);
        _cellPairBuffer = new ComputeBuffer(maxOccupiedCells * MaxCellPairsPerOccupiedCell, Marshal.SizeOf<UInt2Data>());
        _cellPairBuffer.SetData(new UInt2Data[maxOccupiedCells * MaxCellPairsPerOccupiedCell]);
        _cellPairCountBuffer = new ComputeBuffer(maxOccupiedCells, sizeof(uint));
        _cellPairCountBuffer.SetData(new uint[maxOccupiedCells]);
        _collisionCellBuffer = new ComputeBuffer(maxOccupiedCells, sizeof(uint));
        _collisionCellCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _collisionCellCounterBuffer.SetData(new uint[1]);
        _collisionCellFlagBuffer = new ComputeBuffer(_gridCellCount, sizeof(uint));
        _collisionCellFlagBuffer.SetData(new uint[_gridCellCount]);
        _bottlePoseBuffer = new ComputeBuffer(_bottleCount, Marshal.SizeOf<BottlePoseData>());
        _bottlePoseBuffer.SetData(initialBottlePoses);
        _bottleFlagsBuffer = new ComputeBuffer(_bottleCount, sizeof(uint));
        _bottleFlagsBuffer.SetData(new uint[_bottleCount]);
        _bottleSleepCounterBuffer = new ComputeBuffer(_bottleCount, sizeof(uint));
        _bottleSleepCounterBuffer.SetData(new uint[_bottleCount]);
        _bottleWakeRequestBuffer = new ComputeBuffer(_bottleCount, sizeof(uint));
        _bottleWakeRequestBuffer.SetData(new uint[_bottleCount]);
        uint[] initialActiveBottles = new uint[_bottleCount];
        for (int bottleIndex = 0; bottleIndex < _bottleCount; bottleIndex++)
            initialActiveBottles[bottleIndex] = (uint)bottleIndex;
        _activeBottleListBuffer = new ComputeBuffer(Mathf.Max(1, _bottleCount), sizeof(uint));
        _activeBottleListBuffer.SetData(initialActiveBottles);
        _activeBottleCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _activeBottleCounterBuffer.SetData(new[] { (uint)_bottleCount });
        _activeBottleDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _activeBottleDispatchArgsBuffer.SetData(new[]
        {
            (uint)Mathf.Max(1, Mathf.CeilToInt(_bottleCount / (float)ThreadGroupSize)),
            1u,
            1u
        });
        int characterColliderCapacity = Mathf.Max(1, _maxCharacterInteractionColliders);
        _characterColliderBuffer = new ComputeBuffer(characterColliderCapacity, Marshal.SizeOf<CharacterColliderData>());
        _characterColliderUploadData = new CharacterColliderData[characterColliderCapacity];
        _characterColliderBuffer.SetData(_characterColliderUploadData);
        _instanceTransformBuffer = new ComputeBuffer(_bottleCount, Marshal.SizeOf<MatrixRows>());
        _instanceTransformBuffer.SetData(transformRows);
        _contactAccumBuffer = new ComputeBuffer(particleCount, Marshal.SizeOf<ContactAccumData>());
        _contactAccumBuffer.SetData(new ContactAccumData[particleCount]);
        _debugStatsBuffer = new ComputeBuffer(DebugStatsCount, sizeof(uint));
        ResetDebugStatsBuffer();

        _chunkCount = 1;
        _chunkBounds = Array.Empty<Bounds>();
        _indirectCommands = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        _chunkBoundsTransformCache = Array.Empty<MatrixRows>();
        _chunkBoundsReadbackPending = false;
        _framesUntilChunkBoundsReadback = 0;

        uint indexCount = _runtimeMesh != null ? _runtimeMesh.GetIndexCount(0) : 0u;
        uint startIndex = _runtimeMesh != null ? _runtimeMesh.GetIndexStart(0) : 0u;
        uint baseVertexIndex = _runtimeMesh != null ? (uint)_runtimeMesh.GetBaseVertex(0) : 0u;
        _indirectCommands[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
        {
            indexCountPerInstance = indexCount,
            instanceCount = (uint)_bottleCount,
            startIndex = startIndex,
            baseVertexIndex = baseVertexIndex,
            startInstance = 0u
        };

        _indirectCommandBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments,
            1,
            GraphicsBuffer.IndirectDrawIndexedArgs.size);
        _indirectCommandBuffer.SetData(_indirectCommands);

        BindStaticBuffers();
        _framesUntilDebugReadback = _debugReadbackInterval;
        UpdateCharacterCollider(Time.deltaTime);
    }

    private void BindStaticBuffers()
    {
        int[] kernels =
        {
            _buildActiveBottleListKernel,
            _predictKernel,
            _buildGridKeysKernel,
            _buildCellRangesKernel,
            _solveStaticKernel,
            _solveCharacterKernel,
            _solveContactsByCellPairListKernel,
            _solveContactsKernel,
            _applyContactCorrectionsKernel,
            _applyShapeKernel,
            _finalizeKernel,
            _gatherStateKernel
        };

        for (int i = 0; i < kernels.Length; i++)
        {
            int kernel = kernels[i];
            _solverCompute.SetBuffer(kernel, ParticleStaticBufferId, _particleStaticBuffer);
            _solverCompute.SetBuffer(kernel, ParticlePositionBufferId, _particlePositionBuffer);
            _solverCompute.SetBuffer(kernel, ParticleVelocityBufferId, _particleVelocityBuffer);
            _solverCompute.SetBuffer(kernel, ParticlePositionReadBufferId, _particlePositionBuffer);
            _solverCompute.SetBuffer(kernel, ParticleVelocityReadBufferId, _particleVelocityBuffer);
            _solverCompute.SetBuffer(kernel, GridKeyIndexBufferId, _gridKeyIndexBuffer);
            _solverCompute.SetBuffer(kernel, GridKeyIndexReadBufferId, _gridKeyIndexBuffer);
            _solverCompute.SetBuffer(kernel, CellStartBufferId, _cellStartBuffer);
            _solverCompute.SetBuffer(kernel, CellEndBufferId, _cellEndBuffer);
            _solverCompute.SetBuffer(kernel, CellStartReadBufferId, _cellStartBuffer);
            _solverCompute.SetBuffer(kernel, CellEndReadBufferId, _cellEndBuffer);
            _solverCompute.SetBuffer(kernel, OccupiedCellBufferId, _occupiedCellBuffer);
            _solverCompute.SetBuffer(kernel, OccupiedCellCounterBufferId, _occupiedCellCounterBuffer);
            _solverCompute.SetBuffer(kernel, OccupiedCellReadBufferId, _occupiedCellBuffer);
            _solverCompute.SetBuffer(kernel, OccupiedCellCounterReadBufferId, _occupiedCellCounterBuffer);
            _solverCompute.SetBuffer(kernel, CellPairBufferId, _cellPairBuffer);
            _solverCompute.SetBuffer(kernel, CellPairCountBufferId, _cellPairCountBuffer);
            _solverCompute.SetBuffer(kernel, CellPairReadBufferId, _cellPairBuffer);
            _solverCompute.SetBuffer(kernel, CellPairCountReadBufferId, _cellPairCountBuffer);
            _solverCompute.SetBuffer(kernel, CollisionCellBufferId, _collisionCellBuffer);
            _solverCompute.SetBuffer(kernel, CollisionCellCounterBufferId, _collisionCellCounterBuffer);
            _solverCompute.SetBuffer(kernel, CollisionCellFlagBufferId, _collisionCellFlagBuffer);
            _solverCompute.SetBuffer(kernel, CollisionCellReadBufferId, _collisionCellBuffer);
            _solverCompute.SetBuffer(kernel, CollisionCellCounterReadBufferId, _collisionCellCounterBuffer);
            _solverCompute.SetBuffer(kernel, CollisionCellFlagReadBufferId, _collisionCellFlagBuffer);
            _solverCompute.SetBuffer(kernel, BottlePoseBufferId, _bottlePoseBuffer);
            _solverCompute.SetBuffer(kernel, BottlePoseReadBufferId, _bottlePoseBuffer);
            _solverCompute.SetBuffer(kernel, BottleFlagsBufferId, _bottleFlagsBuffer);
            _solverCompute.SetBuffer(kernel, BottleSleepCounterBufferId, _bottleSleepCounterBuffer);
            _solverCompute.SetBuffer(kernel, BottleWakeRequestBufferId, _bottleWakeRequestBuffer);
            _solverCompute.SetBuffer(kernel, ActiveBottleListBufferId, _activeBottleListBuffer);
            _solverCompute.SetBuffer(kernel, ActiveBottleCounterBufferId, _activeBottleCounterBuffer);
            _solverCompute.SetBuffer(kernel, ActiveBottleListReadBufferId, _activeBottleListBuffer);
            _solverCompute.SetBuffer(kernel, ActiveBottleCounterReadBufferId, _activeBottleCounterBuffer);
            _solverCompute.SetBuffer(kernel, CharacterColliderBufferId, _characterColliderBuffer);
            _solverCompute.SetBuffer(kernel, InstanceTransformBufferId, _instanceTransformBuffer);
            _solverCompute.SetBuffer(kernel, ContactAccumBufferId, _contactAccumBuffer);
            _solverCompute.SetBuffer(kernel, ContactAccumReadBufferId, _contactAccumBuffer);
        }

        _solverCompute.SetBuffer(_clearOccupiedCellRangesKernel, CellStartBufferId, _cellStartBuffer);
        _solverCompute.SetBuffer(_clearOccupiedCellRangesKernel, CellEndBufferId, _cellEndBuffer);
        _solverCompute.SetBuffer(_clearOccupiedCellRangesKernel, OccupiedCellBufferId, _occupiedCellBuffer);
        _solverCompute.SetBuffer(_clearOccupiedCellRangesKernel, OccupiedCellCounterBufferId, _occupiedCellCounterBuffer);
        _solverCompute.SetBuffer(_clearOccupiedCellRangesKernel, OccupiedCellReadBufferId, _occupiedCellBuffer);
        _solverCompute.SetBuffer(_clearOccupiedCellRangesKernel, OccupiedCellCounterReadBufferId, _occupiedCellCounterBuffer);
        _solverCompute.SetBuffer(_resetOccupiedCellCounterKernel, OccupiedCellCounterBufferId, _occupiedCellCounterBuffer);
        _solverCompute.SetBuffer(_clearCollisionCellFlagsKernel, CollisionCellBufferId, _collisionCellBuffer);
        _solverCompute.SetBuffer(_clearCollisionCellFlagsKernel, CollisionCellCounterBufferId, _collisionCellCounterBuffer);
        _solverCompute.SetBuffer(_clearCollisionCellFlagsKernel, CollisionCellFlagBufferId, _collisionCellFlagBuffer);
        _solverCompute.SetBuffer(_clearCollisionCellFlagsKernel, CollisionCellReadBufferId, _collisionCellBuffer);
        _solverCompute.SetBuffer(_clearCollisionCellFlagsKernel, CollisionCellCounterReadBufferId, _collisionCellCounterBuffer);
        _solverCompute.SetBuffer(_resetCollisionCellCounterKernel, CollisionCellCounterBufferId, _collisionCellCounterBuffer);
        _solverCompute.SetBuffer(_resetActiveBottleCounterKernel, ActiveBottleCounterBufferId, _activeBottleCounterBuffer);
        _solverCompute.SetBuffer(_buildActiveBottleListKernel, BottlePoseBufferId, _bottlePoseBuffer);
        _solverCompute.SetBuffer(_buildActiveBottleListKernel, BottlePoseReadBufferId, _bottlePoseBuffer);
        _solverCompute.SetBuffer(_buildActiveBottleListKernel, BottleFlagsBufferId, _bottleFlagsBuffer);
        _solverCompute.SetBuffer(_buildActiveBottleListKernel, BottleSleepCounterBufferId, _bottleSleepCounterBuffer);
        _solverCompute.SetBuffer(_buildActiveBottleListKernel, BottleWakeRequestBufferId, _bottleWakeRequestBuffer);
        _solverCompute.SetBuffer(_buildActiveBottleListKernel, ActiveBottleListBufferId, _activeBottleListBuffer);
        _solverCompute.SetBuffer(_buildActiveBottleListKernel, ActiveBottleCounterBufferId, _activeBottleCounterBuffer);
        _solverCompute.SetBuffer(_buildActiveBottleListKernel, CharacterColliderBufferId, _characterColliderBuffer);
        _solverCompute.SetBuffer(_finalizeActiveBottleDispatchArgsKernel, ActiveBottleCounterBufferId, _activeBottleCounterBuffer);
        _solverCompute.SetBuffer(_finalizeActiveBottleDispatchArgsKernel, ActiveBottleCounterReadBufferId, _activeBottleCounterBuffer);
        _solverCompute.SetBuffer(_finalizeActiveBottleDispatchArgsKernel, ActiveBottleDispatchArgsBufferId, _activeBottleDispatchArgsBuffer);
        _solverCompute.SetBuffer(_clearContactAccumKernel, ContactAccumBufferId, _contactAccumBuffer);
        _solverCompute.SetBuffer(_blockLocalSortKernel, GridKeyIndexBufferId, _gridKeyIndexBuffer);
        _solverCompute.SetBuffer(_blockLocalSortKernel, GridKeyIndexReadBufferId, _gridKeyIndexBuffer);
        _solverCompute.SetBuffer(_mergeSortedBlocksKernel, GridKeyIndexBufferId, _gridKeyIndexBuffer);
        _solverCompute.SetBuffer(_mergeSortedBlocksKernel, GridKeyIndexReadBufferId, _gridKeyIndexBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, CellStartBufferId, _cellStartBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, CellEndBufferId, _cellEndBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, CellStartReadBufferId, _cellStartBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, CellEndReadBufferId, _cellEndBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, OccupiedCellBufferId, _occupiedCellBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, OccupiedCellCounterBufferId, _occupiedCellCounterBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, OccupiedCellReadBufferId, _occupiedCellBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, OccupiedCellCounterReadBufferId, _occupiedCellCounterBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, CellPairBufferId, _cellPairBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, CellPairCountBufferId, _cellPairCountBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, CellPairReadBufferId, _cellPairBuffer);
        _solverCompute.SetBuffer(_buildCellPairListKernel, CellPairCountReadBufferId, _cellPairCountBuffer);
        _solverCompute.SetBuffer(_clearRadixHistogramKernel, RadixHistogramBufferId, _radixHistogramBuffer);
        _solverCompute.SetBuffer(_clearRadixHistogramKernel, RadixOffsetBufferId, _radixOffsetBuffer);
        _solverCompute.SetBuffer(_buildRadixHistogramKernel, RadixHistogramBufferId, _radixHistogramBuffer);
        _solverCompute.SetBuffer(_prefixRadixHistogramKernel, RadixHistogramBufferId, _radixHistogramBuffer);
        _solverCompute.SetBuffer(_prefixRadixHistogramKernel, RadixOffsetBufferId, _radixOffsetBuffer);
        _solverCompute.SetBuffer(_scatterRadixPairsKernel, RadixHistogramBufferId, _radixHistogramBuffer);
        _solverCompute.SetBuffer(_scatterRadixPairsKernel, RadixOffsetBufferId, _radixOffsetBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, CellStartBufferId, _cellStartBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, CellEndBufferId, _cellEndBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, CellStartReadBufferId, _cellStartBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, CellEndReadBufferId, _cellEndBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, OccupiedCellBufferId, _occupiedCellBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, OccupiedCellCounterBufferId, _occupiedCellCounterBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, OccupiedCellReadBufferId, _occupiedCellBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, OccupiedCellCounterReadBufferId, _occupiedCellCounterBuffer);
        _solverCompute.SetBuffer(_buildGridKeysKernel, DebugStatsBufferId, _debugStatsBuffer);
        _solverCompute.SetBuffer(_finalizeCellRangesStatsKernel, DebugStatsBufferId, _debugStatsBuffer);
        _solverCompute.SetBuffer(_solveContactsByCellPairListKernel, DebugStatsBufferId, _debugStatsBuffer);
        _solverCompute.SetBuffer(_solveContactsKernel, DebugStatsBufferId, _debugStatsBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, ParticleStaticBufferId, _particleStaticBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, GridKeyIndexBufferId, _gridKeyIndexBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, GridKeyIndexReadBufferId, _gridKeyIndexBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, CellStartBufferId, _cellStartBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, CellEndBufferId, _cellEndBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, CellStartReadBufferId, _cellStartBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, CellEndReadBufferId, _cellEndBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, ParticlePositionBufferId, _particlePositionBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, ParticleVelocityBufferId, _particleVelocityBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, BottlePoseBufferId, _bottlePoseBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, BottlePoseReadBufferId, _bottlePoseBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, BottleFlagsBufferId, _bottleFlagsBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, BottleSleepCounterBufferId, _bottleSleepCounterBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, BottleWakeRequestBufferId, _bottleWakeRequestBuffer);
        _solverCompute.SetBuffer(_updateBottleSleepStatesKernel, CharacterColliderBufferId, _characterColliderBuffer);

        if (_runtimeMaterial != null)
            _runtimeMaterial.SetBuffer(InstanceTransformsId, _instanceTransformBuffer);
    }

    private void BuildInitialState(
        ParticleStaticData[] staticData,
        Vector4[] positions,
        Vector4[] velocities,
        MatrixRows[] transformRows,
        BottlePoseData[] bottlePoses)
    {
        Vector3[] restOffsets = GetRestOffsets();
        System.Random random = new System.Random(_randomSeed);

        float spacingX = _bottleRadius * 2.2f;
        float spacingZ = _bottleRadius * 2.2f;
        float spacingY = Mathf.Max(_bottleHeight * 0.85f, _particleRadius * 2.5f);
        int countX = Mathf.Max(1, Mathf.FloorToInt(_pileSize.x / spacingX));
        int countZ = Mathf.Max(1, Mathf.FloorToInt(_pileSize.z / spacingZ));
        int perLayer = Mathf.Max(1, countX * countZ);

        for (int bottleIndex = 0; bottleIndex < _bottleCount; bottleIndex++)
        {
            int layer = bottleIndex / perLayer;
            int layerIndex = bottleIndex % perLayer;
            int xIndex = layerIndex % countX;
            int zIndex = layerIndex / countX;

            float jitterX = (float)(random.NextDouble() * 2.0 - 1.0) * spacingX * _horizontalJitterFraction;
            float jitterZ = (float)(random.NextDouble() * 2.0 - 1.0) * spacingZ * _horizontalJitterFraction;
            float tiltX = (float)(random.NextDouble() * 2.0 - 1.0) * _maxTiltDegrees;
            float tiltZ = (float)(random.NextDouble() * 2.0 - 1.0) * _maxTiltDegrees;
            float yaw = (float)(random.NextDouble() * 360.0);

            Vector3 centerLocal = new Vector3(
                (xIndex - (countX - 1) * 0.5f) * spacingX + jitterX,
                _particleRadius + _bottleHeight * 0.5f + layer * spacingY,
                (zIndex - (countZ - 1) * 0.5f) * spacingZ + jitterZ);

            Quaternion rotation = Quaternion.Euler(tiltX, yaw, tiltZ);
            Vector3 centerWorld = transform.position + centerLocal;
            Vector3 axisX = rotation * Vector3.right;
            Vector3 axisY = rotation * Vector3.up;
            Vector3 axisZ = rotation * Vector3.forward;

            transformRows[bottleIndex] = BuildMatrixRows(centerWorld, axisX, axisY, axisZ);
            bottlePoses[bottleIndex] = BuildBottlePoseData(centerWorld, axisX, axisY, axisZ);

            int particleStart = bottleIndex * ParticlesPerBottle;
            for (int localParticleIndex = 0; localParticleIndex < ParticlesPerBottle; localParticleIndex++)
            {
                Vector3 localRest = restOffsets[localParticleIndex];
                Vector3 worldPosition = centerWorld + rotation * localRest;
                int particleIndex = particleStart + localParticleIndex;

                staticData[particleIndex] = new ParticleStaticData
                {
                    localRestPosition = localRest,
                    radius = _particleRadius,
                    bottleIndex = (uint)bottleIndex,
                    localParticleIndex = (uint)localParticleIndex,
                    inverseMass = 1.0f,
                    padding = 0.0f
                };

                positions[particleIndex] = new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, 1.0f);
                velocities[particleIndex] = Vector4.zero;
            }
        }
    }

    private Vector3[] GetRestOffsets()
    {
        if (_archetype != null && _archetype.TryGetParticleRestOffsets(ParticlesPerBottle, out Vector3[] customOffsets))
            return customOffsets;

        float halfHeight = Mathf.Max(_particleRadius, _bottleHeight * 0.5f - _particleRadius * 0.25f);
        float sideRadius = Mathf.Max(_particleRadius * 0.75f, _bottleRadius - _particleRadius * 0.15f);

        return new[]
        {
            new Vector3(0.0f, halfHeight, 0.0f),
            new Vector3(0.0f, -halfHeight, 0.0f),
            new Vector3(sideRadius, 0.0f, 0.0f),
            new Vector3(-sideRadius, 0.0f, 0.0f),
            new Vector3(0.0f, 0.0f, sideRadius),
            new Vector3(0.0f, 0.0f, -sideRadius)
        };
    }

    private void Simulate(float deltaTime)
    {
        float clampedDelta = Mathf.Min(0.033f, Mathf.Max(0.0001f, deltaTime));
        float substepDelta = clampedDelta / _substeps;

        ResetDebugStatsBuffer();
        UpdateCharacterCollider(clampedDelta);
        SetSimulationConstants(clampedDelta, substepDelta);

        int particleCount = _bottleCount * ParticlesPerBottle;
        int particleGroups = Mathf.CeilToInt(particleCount / (float)ThreadGroupSize);
        int bottleGroups = Mathf.CeilToInt(_bottleCount / (float)ThreadGroupSize);
        int paddedParticleGroups = Mathf.CeilToInt(_paddedParticleCount / (float)ThreadGroupSize);
        int occupiedCellGroups = Mathf.CeilToInt(_maxOccupiedCellCount / (float)ThreadGroupSize);
        bool rebuildGridEachIteration = _gridRefreshMode == BottleSimulationConfig.GridRefreshMode.PerIteration;

        for (int substep = 0; substep < _substeps; substep++)
        {
            BindPredictedBuffers(_predictKernel, null, _predictedBufferA);
            DispatchBottleKernel(_predictKernel, bottleGroups);

            ComputeBuffer readBuffer = _predictedBufferA;
            ComputeBuffer writeBuffer = _predictedBufferB;

            for (int iteration = 0; iteration < _solverIterations; iteration++)
            {
                DispatchPositionKernel(_solveStaticKernel, ref readBuffer, ref writeBuffer, bottleGroups);
                DispatchPositionKernel(_solveCharacterKernel, ref readBuffer, ref writeBuffer, bottleGroups);

                if (rebuildGridEachIteration || iteration == 0)
                {
                    _solverCompute.Dispatch(_clearOccupiedCellRangesKernel, occupiedCellGroups, 1, 1);
                    _solverCompute.Dispatch(_resetOccupiedCellCounterKernel, 1, 1, 1);
                    BuildAndSortGridKeys(readBuffer, paddedParticleGroups);
                    _solverCompute.Dispatch(_buildCellRangesKernel, paddedParticleGroups, 1, 1);
                    _solverCompute.Dispatch(_buildCellPairListKernel, occupiedCellGroups, 1, 1);

                    if (_enableDebugStatsReadback)
                        _solverCompute.Dispatch(_finalizeCellRangesStatsKernel, occupiedCellGroups, 1, 1);
                }

                _solverCompute.Dispatch(_clearContactAccumKernel, particleGroups, 1, 1);
                BindPredictedBuffers(_solveContactsByCellPairListKernel, readBuffer, null);
                _solverCompute.Dispatch(_solveContactsByCellPairListKernel, occupiedCellGroups, 1, 1);
                DispatchPositionKernel(_applyContactCorrectionsKernel, ref readBuffer, ref writeBuffer, bottleGroups);
                DispatchPositionKernel(_applyShapeKernel, ref readBuffer, ref writeBuffer, bottleGroups);
                DispatchPositionKernel(_solveStaticKernel, ref readBuffer, ref writeBuffer, bottleGroups);
            }

            BindPredictedBuffers(_finalizeKernel, readBuffer, null);
            DispatchBottleKernel(_finalizeKernel, bottleGroups);
        }

        DispatchBottleKernel(_gatherStateKernel, bottleGroups);
        // Sleep / wake 统一在帧尾提交，下一帧生效，避免帧头读到未初始化或上一阶段尚未生产完的数据。
        _solverCompute.Dispatch(_updateBottleSleepStatesKernel, bottleGroups, 1, 1);
        TryQueueDebugStatsReadback();
    }

    private void DispatchBottleKernel(int kernel, int bottleGroups)
    {
        _solverCompute.Dispatch(kernel, bottleGroups, 1, 1);
    }

    private void DispatchPositionKernel(int kernel, ref ComputeBuffer readBuffer, ref ComputeBuffer writeBuffer, int bottleGroups)
    {
        BindPredictedBuffers(kernel, readBuffer, writeBuffer);
        DispatchBottleKernel(kernel, bottleGroups);

        ComputeBuffer temp = readBuffer;
        readBuffer = writeBuffer;
        writeBuffer = temp;
    }

    private void BuildAndSortGridKeys(ComputeBuffer predictedReadBuffer, int paddedParticleGroups)
    {
        int mergePassCount = 0;
        for (int runWidth = ThreadGroupSize; runWidth < _paddedParticleCount; runWidth <<= 1)
        {
            mergePassCount++;
        }

        // 保证 merge ping-pong 结束后，排序结果稳定落回主 buffer，便于后续 cell range / contact 复用既有绑定。
        bool buildGridKeysWritesToMain = (mergePassCount & 1) == 1;
        ComputeBuffer readBuffer = buildGridKeysWritesToMain ? _gridKeyIndexBuffer : _gridKeyIndexTempBuffer;
        ComputeBuffer writeBuffer = buildGridKeysWritesToMain ? _gridKeyIndexTempBuffer : _gridKeyIndexBuffer;

        BindPredictedBuffers(_buildGridKeysKernel, predictedReadBuffer, null);
        _solverCompute.SetBuffer(_buildGridKeysKernel, GridKeyIndexBufferId, readBuffer);
        _solverCompute.Dispatch(_buildGridKeysKernel, paddedParticleGroups, 1, 1);

        _solverCompute.SetBuffer(_blockLocalSortKernel, GridKeyIndexReadBufferId, readBuffer);
        _solverCompute.SetBuffer(_blockLocalSortKernel, GridKeyIndexBufferId, writeBuffer);
        _solverCompute.Dispatch(_blockLocalSortKernel, paddedParticleGroups, 1, 1);

        ComputeBuffer temp = readBuffer;
        readBuffer = writeBuffer;
        writeBuffer = temp;

        for (int runWidth = ThreadGroupSize; runWidth < _paddedParticleCount; runWidth <<= 1)
        {
            _solverCompute.SetInt(SortMergeRunWidthId, runWidth);
            _solverCompute.SetBuffer(_mergeSortedBlocksKernel, GridKeyIndexReadBufferId, readBuffer);
            _solverCompute.SetBuffer(_mergeSortedBlocksKernel, GridKeyIndexBufferId, writeBuffer);
            _solverCompute.Dispatch(_mergeSortedBlocksKernel, paddedParticleGroups, 1, 1);

            temp = readBuffer;
            readBuffer = writeBuffer;
            writeBuffer = temp;
        }
    }

    private void BindPredictedBuffers(int kernel, ComputeBuffer readBuffer, ComputeBuffer writeBuffer)
    {
        if (readBuffer != null)
            _solverCompute.SetBuffer(kernel, PredictedReadBufferId, readBuffer);

        if (writeBuffer != null)
            _solverCompute.SetBuffer(kernel, PredictedWriteBufferId, writeBuffer);
    }

    private void SetSimulationConstants(float deltaTime, float substepDelta)
    {
        Vector3 gridBoundsMin = GetBroadphaseBoundsMin();
        Vector3 gridBoundsMax = GetBroadphaseBoundsMax();
        Vector3 boundsMin = GetSimulationBoundsMin();
        Vector3 boundsMax = GetSimulationBoundsMax();
        Vector3 renderScale = ResolveRenderScale();

        _solverCompute.SetInt(ParticleCountId, _bottleCount * ParticlesPerBottle);
        _solverCompute.SetInt(BottleCountId, _bottleCount);
        _solverCompute.SetInt(ParticlesPerBottleId, ParticlesPerBottle);
        _solverCompute.SetInt(PaddedParticleCountId, _paddedParticleCount);
        _solverCompute.SetInts(GridDimId, _gridDimX, _gridDimY, _gridDimZ);
        _solverCompute.SetInt(GridCellCountId, _gridCellCount);
        _solverCompute.SetInt(CharacterColliderCountId, _characterColliderCount);
        _solverCompute.SetFloat(CellSizeId, _cellSize);
        _solverCompute.SetFloat(InvCellSizeId, 1.0f / _cellSize);
        _solverCompute.SetFloat(DeltaTimeId, deltaTime);
        _solverCompute.SetFloat(SubstepDeltaTimeId, substepDelta);
        _solverCompute.SetVector(GravityId, new Vector4(0.0f, _gravity, 0.0f, 0.0f));
        _solverCompute.SetFloat(VelocityDampingId, _velocityDamping);
        _solverCompute.SetFloat(ShapeStiffnessId, _shapeStiffness);
        _solverCompute.SetFloat(ContactSlopId, _contactSlop);
        _solverCompute.SetFloat(MaxSpeedId, _maxSpeed);
        _solverCompute.SetInt(SleepFrameThresholdId, _sleepFrameThreshold);
        _solverCompute.SetFloat(SleepSpeedThresholdId, _sleepSpeedThreshold);
        _solverCompute.SetFloat(SleepCharacterWakeRadiusId, _sleepCharacterWakeRadius);
        _solverCompute.SetFloat(SleepActiveNeighborWakeRadiusId, _sleepActiveNeighborWakeRadius);
        _solverCompute.SetVector(GridBoundsMinId, gridBoundsMin);
        _solverCompute.SetVector(GridBoundsMaxId, gridBoundsMax);
        _solverCompute.SetVector(SimulationBoundsMinId, boundsMin);
        _solverCompute.SetVector(SimulationBoundsMaxId, boundsMax);
        _solverCompute.SetVector(RenderScaleId, renderScale);
        _solverCompute.SetVector(MeshOffsetId, _meshOffset);
        _solverCompute.SetInt(EnableDebugStatsId, _enableDebugStatsReadback ? 1 : 0);
    }

    private void UpdateCharacterCollider(float deltaTime)
    {
        if (_characterColliderBuffer == null)
            return;

        if (_characterColliderUploadData == null || _characterColliderUploadData.Length != Mathf.Max(1, _maxCharacterInteractionColliders))
            _characterColliderUploadData = new CharacterColliderData[Mathf.Max(1, _maxCharacterInteractionColliders)];

        Array.Clear(_characterColliderUploadData, 0, _characterColliderUploadData.Length);
        _characterColliderCount = 0;
        float iterationScale = 1.0f / Mathf.Max(1, _substeps * _solverIterations);
        int colliderCount = 0;
        HashSet<int> currentColliderIds = new HashSet<int>();

        if (_characterBoxCollider != null &&
            TryBuildCharacterColliderData(_characterBoxCollider, deltaTime, iterationScale, out CharacterColliderData boxColliderData))
        {
            _characterColliderUploadData[0] = boxColliderData;
            currentColliderIds.Add(_characterBoxCollider.GetInstanceID());
            colliderCount++;
        }
        else
        {
            CollectCharacterInteractionColliders(_resolvedCharacterInteractionColliders);

            for (int i = 0; i < _resolvedCharacterInteractionColliders.Count && colliderCount < _characterColliderUploadData.Length; i++)
            {
                BoxCollider boxCollider = _resolvedCharacterInteractionColliders[i];
                if (boxCollider == null)
                    continue;

                if (!TryBuildCharacterColliderData(boxCollider, deltaTime, iterationScale, out CharacterColliderData colliderData))
                    continue;

                _characterColliderUploadData[colliderCount] = colliderData;
                currentColliderIds.Add(boxCollider.GetInstanceID());
                colliderCount++;
            }
        }

        if (colliderCount == 0 && TryBuildFallbackCharacterColliderData(deltaTime, iterationScale, out CharacterColliderData fallbackColliderData))
        {
            _characterColliderUploadData[0] = fallbackColliderData;
            colliderCount = 1;
        }

        _characterColliderCount = colliderCount;
        PruneMissingCharacterColliderHistory(currentColliderIds);
        _characterColliderBuffer.SetData(_characterColliderUploadData);
    }

    private void CollectCharacterInteractionColliders(List<BoxCollider> results)
    {
        results.Clear();
        if (_characterTransform == null)
            return;

        if (TryCollectCharacterProxyColliders(results))
            return;

        if (TryEnsureCharacterInteractionProxies() && TryCollectCharacterProxyColliders(results))
            return;

        if (_characterBoxCollider != null && IsUsableInteractionCollider(_characterBoxCollider))
            results.Add(_characterBoxCollider);
    }

    private bool TryCollectCharacterProxyColliders(List<BoxCollider> results)
    {
        if (_characterTransform == null)
            return false;

        BoxCollider[] colliders = _characterTransform.GetComponentsInChildren<BoxCollider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider collider = colliders[i];
            if (collider == null)
                continue;

            if (!string.IsNullOrEmpty(_characterProxyColliderPrefix) &&
                collider.name.StartsWith(_characterProxyColliderPrefix, StringComparison.Ordinal))
            {
                if (!IsUsableInteractionCollider(collider))
                    continue;

                results.Add(collider);
            }
        }

        if (results.Count <= 0)
            return false;

        results.Sort((left, right) => EstimateColliderVolume(right).CompareTo(EstimateColliderVolume(left)));
        return true;
    }

    private bool TryEnsureCharacterInteractionProxies()
    {
        if (_characterTransform == null)
            return false;

        QianxiaRigidBodyProxySetup proxySetup = _characterTransform.GetComponent<QianxiaRigidBodyProxySetup>();
        if (proxySetup == null)
            proxySetup = _characterTransform.GetComponentInParent<QianxiaRigidBodyProxySetup>();

        if (proxySetup == null)
            proxySetup = _characterTransform.GetComponentInChildren<QianxiaRigidBodyProxySetup>(true);

        if (proxySetup == null || !proxySetup.isActiveAndEnabled)
            return false;

        proxySetup.Apply();
        return true;
    }

    private bool TryBuildCharacterColliderData(BoxCollider boxCollider, float deltaTime, float iterationScale, out CharacterColliderData colliderData)
    {
        colliderData = default;
        if (boxCollider == null)
            return false;

        Vector3 worldSize = Vector3.Scale(boxCollider.size, AbsVector3(boxCollider.transform.lossyScale));
        if (worldSize.x < _minCharacterInteractionColliderSize ||
            worldSize.y < _minCharacterInteractionColliderSize ||
            worldSize.z < _minCharacterInteractionColliderSize)
        {
            return false;
        }

        Vector3 boxCenter = boxCollider.transform.TransformPoint(boxCollider.center);
        Vector3 halfExtents = Vector3.Max(worldSize * 0.5f, Vector3.one * 0.01f);
        Vector3 axisX = boxCollider.transform.right.normalized;
        Vector3 axisY = boxCollider.transform.up.normalized;
        Vector3 axisZ = boxCollider.transform.forward.normalized;

        int colliderId = boxCollider.GetInstanceID();
        Vector3 previousCenter;
        bool hadPreviousCenter = _lastCharacterColliderCenters.TryGetValue(colliderId, out previousCenter);
        Vector3 delta = hadPreviousCenter ? boxCenter - previousCenter : Vector3.zero;
        float speed = deltaTime > 0.0f ? delta.magnitude / deltaTime : 0.0f;
        float pushStrength = speed >= _characterPushActivationSpeed
            ? _characterPushBase + speed * _characterPushFromSpeed
            : 0.0f;

        colliderData.center = boxCenter;
        colliderData.pushStrength = pushStrength;
        colliderData.halfExtents = halfExtents;
        colliderData.tangentialDamping = _characterTangentialDamping;
        colliderData.axisX = axisX;
        colliderData.padding0 = 0.0f;
        colliderData.axisY = axisY;
        colliderData.padding1 = 0.0f;
        colliderData.axisZ = axisZ;
        colliderData.padding2 = 0.0f;
        colliderData.deltaPosition = delta * iterationScale;
        colliderData.padding3 = 0.0f;

        _lastCharacterColliderCenters[colliderId] = boxCenter;
        return true;
    }

    private bool TryBuildFallbackCharacterColliderData(float deltaTime, float iterationScale, out CharacterColliderData colliderData)
    {
        colliderData = default;

        Transform characterTarget = _characterController != null ? _characterController.transform : _characterTransform;
        if (characterTarget == null)
            return false;

        Vector3 fallbackSize = Vector3.Max(_fallbackCharacterBoxSize, Vector3.one * 0.1f);
        Vector3 boxCenter = _characterController != null
            ? _characterController.transform.TransformPoint(_characterController.center)
            : characterTarget.TransformPoint(_fallbackCharacterBoxCenter);

        const int fallbackColliderId = int.MinValue;
        Vector3 previousCenter;
        bool hadPreviousCenter = _lastCharacterColliderCenters.TryGetValue(fallbackColliderId, out previousCenter);
        Vector3 delta = hadPreviousCenter ? boxCenter - previousCenter : Vector3.zero;
        float speed = deltaTime > 0.0f ? delta.magnitude / deltaTime : 0.0f;
        float pushStrength = speed >= _characterPushActivationSpeed
            ? _characterPushBase + speed * _characterPushFromSpeed
            : 0.0f;

        colliderData.center = boxCenter;
        colliderData.pushStrength = pushStrength;
        colliderData.halfExtents = fallbackSize * 0.5f;
        colliderData.tangentialDamping = _characterTangentialDamping;
        colliderData.axisX = characterTarget.right.normalized;
        colliderData.padding0 = 0.0f;
        colliderData.axisY = characterTarget.up.normalized;
        colliderData.padding1 = 0.0f;
        colliderData.axisZ = characterTarget.forward.normalized;
        colliderData.padding2 = 0.0f;
        colliderData.deltaPosition = delta * iterationScale;
        colliderData.padding3 = 0.0f;

        _lastCharacterColliderCenters[fallbackColliderId] = boxCenter;
        return true;
    }

    private void PruneMissingCharacterColliderHistory(HashSet<int> currentColliderIds)
    {
        if (_lastCharacterColliderCenters.Count == 0)
            return;

        List<int> removedIds = null;
        foreach (int colliderId in _lastCharacterColliderCenters.Keys)
        {
            if (currentColliderIds.Contains(colliderId) || colliderId == int.MinValue)
                continue;

            removedIds ??= new List<int>();
            removedIds.Add(colliderId);
        }

        if (removedIds == null)
            return;

        for (int i = 0; i < removedIds.Count; i++)
            _lastCharacterColliderCenters.Remove(removedIds[i]);
    }

    private bool IsUsableInteractionCollider(BoxCollider collider)
    {
        if (collider == null || !collider.enabled)
            return false;

        if (!string.IsNullOrEmpty(_characterProxyColliderPrefix) &&
            collider.name.StartsWith(_characterProxyColliderPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        return !collider.isTrigger;
    }

    private static float EstimateColliderVolume(BoxCollider collider)
    {
        Vector3 worldSize = Vector3.Scale(collider.size, AbsVector3(collider.transform.lossyScale));
        return worldSize.x * worldSize.y * worldSize.z;
    }

    private void Draw()
    {
        if (_runtimeMaterial == null || _runtimeMesh == null || _indirectCommandBuffer == null)
            return;

        Bounds coarseBounds = GetRenderBounds();
        if (!UpdateCullingState(coarseBounds))
            return;

        RenderParams renderParams = new RenderParams(_runtimeMaterial)
        {
            worldBounds = coarseBounds,
            shadowCastingMode = _shadowCastingMode,
            receiveShadows = _receiveShadows,
            layer = gameObject.layer
        };

        Graphics.RenderMeshIndirect(renderParams, _runtimeMesh, _indirectCommandBuffer, 1, 0);
    }

    private Bounds GetRenderBounds()
    {
        Vector3 boundsMin = GetSimulationBoundsMin();
        Vector3 boundsMax = GetSimulationBoundsMax();
        Vector3 center = (boundsMin + boundsMax) * 0.5f;
        Vector3 size = boundsMax - boundsMin + Vector3.one * (_renderBoundsPadding * 2.0f);
        return new Bounds(center, size);
    }

    private bool UpdateCullingState(Bounds bounds)
    {
        if (!_enableCullingGroup)
        {
            DisposeCullingGroup();
            _isVisibleByCulling = true;
            return true;
        }

        EnsureCullingGroup();
        if (_cullingGroup == null)
        {
            _isVisibleByCulling = true;
            return true;
        }

        Camera targetCamera = Camera.main;
        if (_cullingGroup.targetCamera != targetCamera)
        {
            _cullingGroup.targetCamera = targetCamera;
            _isVisibleByCulling = true;
        }

        if (targetCamera == null)
        {
            _isVisibleByCulling = true;
            return true;
        }

        _cullingSpheres[0] = new BoundingSphere(bounds.center, bounds.extents.magnitude + _cullingSpherePadding);
        _cullingGroup.SetBoundingSpheres(_cullingSpheres);
        _cullingGroup.SetBoundingSphereCount(1);

        // CullingGroup 的事件结果可能晚一帧到达，这里用当前帧的 frustum 测试兜底，避免误剔除。
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(targetCamera);
        bool visibleThisFrame = GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
        return visibleThisFrame || _isVisibleByCulling;
    }

    private void EnsureCullingGroup()
    {
        if (_cullingGroup != null)
            return;

        _cullingGroup = new CullingGroup();
        _cullingGroup.onStateChanged += OnCullingStateChanged;
        _cullingGroup.SetBoundingSpheres(_cullingSpheres);
        _cullingGroup.SetBoundingSphereCount(1);
    }

    private void DisposeCullingGroup()
    {
        if (_cullingGroup == null)
            return;

        _cullingGroup.onStateChanged -= OnCullingStateChanged;
        _cullingGroup.Dispose();
        _cullingGroup = null;
    }

    private void OnCullingStateChanged(CullingGroupEvent sphereEvent)
    {
        if (sphereEvent.index != 0)
            return;

        _isVisibleByCulling = sphereEvent.isVisible;
    }

    private Vector3 ResolveRenderScale()
    {
        if (_bottleMesh == null && _autoScaleBuiltInCapsule)
            return new Vector3(_bottleRadius * 2.0f, _bottleHeight * 0.5f, _bottleRadius * 2.0f);

        return _meshScale;
    }

    private void ResetDebugStatsBuffer()
    {
        if (!_enableDebugStatsReadback || _debugStatsBuffer == null)
            return;

        _debugStatsBuffer.SetData(_debugStatsReset);
    }

    private void TryQueueDebugStatsReadback()
    {
        if (!_enableDebugStatsReadback || _debugStatsBuffer == null)
            return;

        if (!SystemInfo.supportsAsyncGPUReadback)
            return;

        if (_debugReadbackPending)
            return;

        _framesUntilDebugReadback = Mathf.Max(0, _framesUntilDebugReadback - 1);
        if (_framesUntilDebugReadback > 0)
            return;

        _framesUntilDebugReadback = _debugReadbackInterval;
        _debugReadbackPending = true;
        AsyncGPUReadback.Request(_debugStatsBuffer, OnDebugStatsReadbackCompleted);
    }

    private void OnDebugStatsReadbackCompleted(AsyncGPUReadbackRequest request)
    {
        _debugReadbackPending = false;
        if (!this || request.hasError)
            return;

        NativeArray<uint> data = request.GetData<uint>();
        if (data.Length < DebugStatsCount)
            return;

        _latestDebugStats = new DebugStatsSnapshot
        {
            maxCellOccupancy = data[0],
            occupiedCellCount = data[1],
            gridBuildCount = data[2],
            contactPairCount = data[3]
        };

        _latestDebugStatsSummary =
            $"maxCell={_latestDebugStats.maxCellOccupancy}, occupiedCells={_latestDebugStats.occupiedCellCount}, " +
            $"gridBuilds={_latestDebugStats.gridBuildCount}, contacts={_latestDebugStats.contactPairCount}";
    }

    private static Vector3 AbsVector3(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private Terrain ResolveSimulationTerrain()
    {
        if (_simulationTerrain != null && _simulationTerrain.terrainData != null)
            return _simulationTerrain;

        if (Terrain.activeTerrain != null && Terrain.activeTerrain.terrainData != null)
        {
            _simulationTerrain = Terrain.activeTerrain;
            return _simulationTerrain;
        }

        Terrain terrain = FindFirstObjectByType<Terrain>();
        if (terrain != null && terrain.terrainData != null)
            _simulationTerrain = terrain;

        return _simulationTerrain;
    }

    private bool TryGetTerrainBounds(out Vector3 min, out Vector3 max)
    {
        min = default;
        max = default;

        if (!_useTerrainBoundsXZ)
            return false;

        Terrain terrain = ResolveSimulationTerrain();
        if (terrain == null || terrain.terrainData == null)
            return false;

        min = terrain.transform.position;
        max = min + terrain.terrainData.size;
        return true;
    }

    private Vector3 ResolveGridAnchorPosition()
    {
        if (_characterTransform != null)
            return _characterTransform.position;

        if (_characterController != null)
            return _characterController.transform.position;

        if (_characterBoxCollider != null)
            return _characterBoxCollider.transform.position;

        return transform.position;
    }

    private float ClampGridCenterAxis(float anchorAxis, float terrainMinAxis, float terrainMaxAxis, float windowSizeAxis)
    {
        float terrainSpan = terrainMaxAxis - terrainMinAxis;
        if (terrainSpan <= windowSizeAxis)
            return (terrainMinAxis + terrainMaxAxis) * 0.5f;

        float halfWindow = windowSizeAxis * 0.5f;
        return Mathf.Clamp(anchorAxis, terrainMinAxis + halfWindow, terrainMaxAxis - halfWindow);
    }

    private Vector3 GetBroadphaseBoundsMin()
    {
        if (TryGetTerrainBounds(out Vector3 terrainMin, out Vector3 terrainMax))
        {
            Vector3 anchor = ResolveGridAnchorPosition();
            float centerX = ClampGridCenterAxis(anchor.x, terrainMin.x, terrainMax.x, _simulationBoundsSize.x);
            float centerZ = ClampGridCenterAxis(anchor.z, terrainMin.z, terrainMax.z, _simulationBoundsSize.z);
            return new Vector3(
                centerX - _simulationBoundsSize.x * 0.5f,
                transform.position.y,
                centerZ - _simulationBoundsSize.z * 0.5f);
        }

        return transform.position + new Vector3(-_simulationBoundsSize.x * 0.5f, 0.0f, -_simulationBoundsSize.z * 0.5f);
    }

    private Vector3 GetBroadphaseBoundsMax()
    {
        return GetBroadphaseBoundsMin() + _simulationBoundsSize;
    }

    private Vector3 GetSimulationBoundsMin()
    {
        if (TryGetTerrainBounds(out Vector3 terrainMin, out _))
            return new Vector3(terrainMin.x, transform.position.y, terrainMin.z);

        return transform.position + new Vector3(-_simulationBoundsSize.x * 0.5f, 0.0f, -_simulationBoundsSize.z * 0.5f);
    }

    private Vector3 GetSimulationBoundsMax()
    {
        if (TryGetTerrainBounds(out _, out Vector3 terrainMax))
            return new Vector3(terrainMax.x, transform.position.y + _simulationBoundsSize.y, terrainMax.z);

        return transform.position + new Vector3(_simulationBoundsSize.x * 0.5f, _simulationBoundsSize.y, _simulationBoundsSize.z * 0.5f);
    }

    private MatrixRows BuildMatrixRows(Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ)
    {
        Vector3 scale = ResolveRenderScale();
        Vector3 worldCenter = center + axisX * _meshOffset.x + axisY * _meshOffset.y + axisZ * _meshOffset.z;
        return new MatrixRows
        {
            row0 = new Vector4(axisX.x * scale.x, axisX.y * scale.x, axisX.z * scale.x, worldCenter.x),
            row1 = new Vector4(axisY.x * scale.y, axisY.y * scale.y, axisY.z * scale.y, worldCenter.y),
            row2 = new Vector4(axisZ.x * scale.z, axisZ.y * scale.z, axisZ.z * scale.z, worldCenter.z)
        };
    }

    private static BottlePoseData BuildBottlePoseData(Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ)
    {
        return new BottlePoseData
        {
            center = center,
            padding0 = 0.0f,
            axisX = axisX,
            padding1 = 0.0f,
            axisY = axisY,
            padding2 = 0.0f,
            axisZ = axisZ,
            padding3 = 0.0f
        };
    }

    private void BuildChunkCommandsAndBounds(MatrixRows[] transformRows)
    {
        _chunkCount = Mathf.Max(1, Mathf.CeilToInt(_bottleCount / (float)Mathf.Max(1, _renderChunkBottleCount)));
        _chunkBounds = new Bounds[_chunkCount];
        _indirectCommands = new GraphicsBuffer.IndirectDrawIndexedArgs[_chunkCount];
        _chunkBoundsTransformCache = new MatrixRows[_bottleCount];
        Array.Copy(transformRows, _chunkBoundsTransformCache, Mathf.Min(transformRows.Length, _chunkBoundsTransformCache.Length));
        _framesUntilChunkBoundsReadback = _chunkBoundsReadbackInterval;

        uint indexCount = _runtimeMesh != null ? _runtimeMesh.GetIndexCount(0) : 0u;
        uint startIndex = _runtimeMesh != null ? _runtimeMesh.GetIndexStart(0) : 0u;
        uint baseVertexIndex = _runtimeMesh != null ? (uint)_runtimeMesh.GetBaseVertex(0) : 0u;

        for (int chunkIndex = 0; chunkIndex < _chunkCount; chunkIndex++)
        {
            int startBottle = chunkIndex * _renderChunkBottleCount;
            int chunkBottleCount = Mathf.Min(_renderChunkBottleCount, _bottleCount - startBottle);
            _indirectCommands[chunkIndex] = new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = indexCount,
                instanceCount = (uint)chunkBottleCount,
                startIndex = startIndex,
                baseVertexIndex = baseVertexIndex,
                startInstance = (uint)startBottle
            };
        }

        RebuildChunkBoundsFromInstanceRows(_chunkBoundsTransformCache);
    }

    private float GetChunkBoundsTrackingPadding()
    {
        float readbackTravelPadding = _maxSpeed * Mathf.Max(1, _chunkBoundsReadbackInterval) * 0.033f;
        return _chunkBoundsPadding + readbackTravelPadding;
    }

    private void RebuildChunkBoundsFromInstanceRows(MatrixRows[] transformRows)
    {
        if (_chunkCount <= 0 || _chunkBounds.Length == 0 || transformRows == null || transformRows.Length < _bottleCount)
            return;

        Vector3 localCenter;
        Vector3 localExtents;
        if (_runtimeMesh != null)
        {
            Bounds localBounds = _runtimeMesh.bounds;
            localCenter = localBounds.center;
            localExtents = localBounds.extents;
        }
        else
        {
            localCenter = Vector3.zero;
            localExtents = new Vector3(_bottleRadius, _bottleHeight * 0.5f, _bottleRadius);
        }

        float trackingPadding = GetChunkBoundsTrackingPadding();

        for (int chunkIndex = 0; chunkIndex < _chunkCount; chunkIndex++)
        {
            int startBottle = chunkIndex * _renderChunkBottleCount;
            int chunkBottleCount = Mathf.Min(_renderChunkBottleCount, _bottleCount - startBottle);
            bool hasBounds = false;
            Bounds chunkBounds = default;

            for (int bottleOffset = 0; bottleOffset < chunkBottleCount; bottleOffset++)
            {
                MatrixRows rows = transformRows[startBottle + bottleOffset];
                Vector3 translation = new Vector3(rows.row0.w, rows.row1.w, rows.row2.w);
                Vector3 basisX = new Vector3(rows.row0.x, rows.row0.y, rows.row0.z);
                Vector3 basisY = new Vector3(rows.row1.x, rows.row1.y, rows.row1.z);
                Vector3 basisZ = new Vector3(rows.row2.x, rows.row2.y, rows.row2.z);
                Vector3 center = translation
                    + basisX * localCenter.x
                    + basisY * localCenter.y
                    + basisZ * localCenter.z;
                Vector3 extents =
                    AbsVector3(basisX) * localExtents.x +
                    AbsVector3(basisY) * localExtents.y +
                    AbsVector3(basisZ) * localExtents.z;
                Bounds bottleBounds = new Bounds(center, extents * 2.0f);
                if (!hasBounds)
                {
                    chunkBounds = bottleBounds;
                    hasBounds = true;
                }
                else
                {
                    chunkBounds.Encapsulate(bottleBounds);
                }
            }

            chunkBounds.Expand(trackingPadding * 2.0f);
            _chunkBounds[chunkIndex] = chunkBounds;
        }
    }

    private void TryQueueChunkBoundsReadback()
    {
        if (_instanceTransformBuffer == null || _chunkBoundsReadbackPending || !SystemInfo.supportsAsyncGPUReadback)
            return;

        _framesUntilChunkBoundsReadback = Mathf.Max(0, _framesUntilChunkBoundsReadback - 1);
        if (_framesUntilChunkBoundsReadback > 0)
            return;

        _framesUntilChunkBoundsReadback = _chunkBoundsReadbackInterval;
        _chunkBoundsReadbackPending = true;
        AsyncGPUReadback.Request(_instanceTransformBuffer, OnChunkBoundsReadbackCompleted);
    }

    private void OnChunkBoundsReadbackCompleted(AsyncGPUReadbackRequest request)
    {
        _chunkBoundsReadbackPending = false;
        if (!this || request.hasError)
            return;

        NativeArray<MatrixRows> data = request.GetData<MatrixRows>();
        if (!data.IsCreated || data.Length < _bottleCount)
            return;

        if (_chunkBoundsTransformCache == null || _chunkBoundsTransformCache.Length != _bottleCount)
            _chunkBoundsTransformCache = new MatrixRows[_bottleCount];

        data.CopyTo(_chunkBoundsTransformCache);
        RebuildChunkBoundsFromInstanceRows(_chunkBoundsTransformCache);
    }

    private void ReleaseResources()
    {
        ReleaseSimulationBuffers();
        DisposeCullingGroup();

        if (_runtimeMaterial != null)
            DestroyRuntimeMaterial();

        _runtimeMesh = null;
        _chunkBounds = Array.Empty<Bounds>();
        _indirectCommands = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
        _chunkBoundsTransformCache = Array.Empty<MatrixRows>();
        _chunkBoundsReadbackPending = false;
        _framesUntilChunkBoundsReadback = 0;
        _chunkCount = 0;
        _characterColliderCount = 0;
        _lastCharacterColliderCenters.Clear();
        _debugReadbackPending = false;
        _isVisibleByCulling = true;
    }

    private void ReleaseSimulationBuffers()
    {
        ReleaseBuffer(ref _particleStaticBuffer);
        ReleaseBuffer(ref _particlePositionBuffer);
        ReleaseBuffer(ref _particleVelocityBuffer);
        ReleaseBuffer(ref _predictedBufferA);
        ReleaseBuffer(ref _predictedBufferB);
        ReleaseBuffer(ref _gridKeyIndexBuffer);
        ReleaseBuffer(ref _gridKeyIndexTempBuffer);
        ReleaseBuffer(ref _radixHistogramBuffer);
        ReleaseBuffer(ref _radixOffsetBuffer);
        ReleaseBuffer(ref _cellStartBuffer);
        ReleaseBuffer(ref _cellEndBuffer);
        ReleaseBuffer(ref _occupiedCellBuffer);
        ReleaseBuffer(ref _occupiedCellCounterBuffer);
        ReleaseBuffer(ref _cellPairBuffer);
        ReleaseBuffer(ref _cellPairCountBuffer);
        ReleaseBuffer(ref _collisionCellBuffer);
        ReleaseBuffer(ref _collisionCellCounterBuffer);
        ReleaseBuffer(ref _collisionCellFlagBuffer);
        ReleaseBuffer(ref _bottlePoseBuffer);
        ReleaseBuffer(ref _bottleFlagsBuffer);
        ReleaseBuffer(ref _bottleSleepCounterBuffer);
        ReleaseBuffer(ref _bottleWakeRequestBuffer);
        ReleaseBuffer(ref _activeBottleListBuffer);
        ReleaseBuffer(ref _activeBottleCounterBuffer);
        ReleaseBuffer(ref _activeBottleDispatchArgsBuffer);
        ReleaseBuffer(ref _characterColliderBuffer);
        ReleaseBuffer(ref _instanceTransformBuffer);
        ReleaseBuffer(ref _contactAccumBuffer);
        ReleaseBuffer(ref _debugStatsBuffer);
        ReleaseBuffer(ref _indirectCommandBuffer);
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

    private void DestroyRuntimeMaterial()
    {
        if (_runtimeMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(_runtimeMaterial);
        else
            DestroyImmediate(_runtimeMaterial);

        _runtimeMaterial = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (_drawSimulationBounds)
        {
            Gizmos.color = new Color(0.15f, 0.8f, 1.0f, 0.6f);
            Vector3 boundsMin = GetSimulationBoundsMin();
            Vector3 boundsMax = GetSimulationBoundsMax();
            Vector3 center = (boundsMin + boundsMax) * 0.5f;
            Gizmos.DrawWireCube(center, boundsMax - boundsMin);
        }

        if (_drawPileBounds)
        {
            Gizmos.color = new Color(1.0f, 0.65f, 0.2f, 0.5f);
            Vector3 center = transform.position + Vector3.up * (_pileSize.y * 0.5f);
            Gizmos.DrawWireCube(center, _pileSize);
        }
    }
}
