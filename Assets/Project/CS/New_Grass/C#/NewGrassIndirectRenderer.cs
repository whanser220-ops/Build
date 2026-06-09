using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class NewGrassIndirectRenderer : MonoBehaviour, IGpuSemanticDrawProvider, IGpuSemanticDrawCommandProvider
{
    private const int k_KernelThreadSize = 64;
    private const int k_ArgsCount = 5;
    private const int k_DispatchIndirectArgsCount = 4;
    private const int k_DistributionChannelCount = 4;
    private const int k_FrustumPlaneCount = 6;
    private const int k_MaxComputeDispatchGroupsPerDimension = 65535;
    private const int k_MaxAdaptiveCellSplitDepth = 8;
    private const int k_GeneratedGrassBladeStride = 9 * sizeof(float);
    private const int k_VisibleGrassInstanceStride = sizeof(uint) + (3 * sizeof(float));
    private const float k_MaxBladeRootRadiusScale = 1.08f;
    private const float k_MinAdaptiveCellExtent = 0.5f;
    private const float k_GoldenAngle = 2.39996323f;
    private const string GrassRendererCppFile = "Assets/Project/CS/New_Grass/C#/NewGrassIndirectRenderer.cs";
    private const string GrassShaderLod0File = "Assets/Project/CS/New_Grass/shader/NewGrassBezierBladeToon.shader";
    private const string GrassShaderLod1File = "Assets/Project/CS/New_Grass/shader/NewGrassBezierBladeToonMid.shader";
    private const string GrassShaderLod2File = "Assets/Project/CS/New_Grass/shader/NewGrassBezierBladeToonFar.shader";
    private const string GrassRenderShaderEntry = "Forward";
    private const string GrassRenderLod0PassId = "grass.render.lod0";
    private const string GrassRenderLod1PassId = "grass.render.lod1";
    private const string GrassRenderLod2PassId = "grass.render.lod2";
    private static readonly string GrassRenderLod0Marker =
        GpuPassMarkerUtility.BuildMarkerLabel(GrassRenderLod0PassId, "Grass / Render / LOD0");
    private static readonly string GrassRenderLod1Marker =
        GpuPassMarkerUtility.BuildMarkerLabel(GrassRenderLod1PassId, "Grass / Render / LOD1");
    private static readonly string GrassRenderLod2Marker =
        GpuPassMarkerUtility.BuildMarkerLabel(GrassRenderLod2PassId, "Grass / Render / LOD2");

    private static readonly int GrassBladesId = Shader.PropertyToID("_GrassBlades");
    private static readonly int GrassBladesLod0Id = Shader.PropertyToID("_GrassBladesLod0");
    private static readonly int GrassBladesLod1Id = Shader.PropertyToID("_GrassBladesLod1");
    private static readonly int GrassBladesLod2Id = Shader.PropertyToID("_GrassBladesLod2");
    private static readonly int ClusterInstancesBufferId = Shader.PropertyToID("_ClusterInstancesBuffer");
    private static readonly int ClusterCullingDataBufferId = Shader.PropertyToID("_ClusterCullingDataBuffer");
    private static readonly int ClusterDispatchBufferId = Shader.PropertyToID("_ClusterDispatchBuffer");
    private static readonly int VisibleClusterDispatchBufferId = Shader.PropertyToID("_VisibleClusterDispatchBuffer");
    private static readonly int VisibleClusterDispatchAppendBufferId = Shader.PropertyToID("_VisibleClusterDispatchAppendBuffer");
    private static readonly int ClumpParametersBufferId = Shader.PropertyToID("_ClumpParametersBuffer");
    private static readonly int ClusterCountId = Shader.PropertyToID("_ClusterCount");
    private static readonly int ClusterCullingCountId = Shader.PropertyToID("_ClusterCullingCount");
    private static readonly int ClusterDispatchCountId = Shader.PropertyToID("_ClusterDispatchCount");
    private static readonly int ClumpParameterCountId = Shader.PropertyToID("_ClumpParameterCount");
    private static readonly int GeneratedGrassBladesId = Shader.PropertyToID("_GeneratedGrassBlades");
    private static readonly int GeneratedGrassBladeCountId = Shader.PropertyToID("_GeneratedGrassBladeCount");
    private static readonly int TerrainPositionId = Shader.PropertyToID("_TerrainPosition");
    private static readonly int TerrainSizeId = Shader.PropertyToID("_TerrainSize");
    private static readonly int HeightMapId = Shader.PropertyToID("_HeightMap");
    private static readonly int HeightMapMultiplierId = Shader.PropertyToID("_HeightMapMultiplier");
    private static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WSpaceCameraPos");
    private static readonly int DistanceCullStartDistId = Shader.PropertyToID("_DistanceCullStartDist");
    private static readonly int DistanceCullEndDistId = Shader.PropertyToID("_DistanceCullEndDist");
    private static readonly int DistanceCullMinimumGrassAmountId = Shader.PropertyToID("_DistanceCullMinimumGrassAmount");
    private static readonly int VpMatrixId = Shader.PropertyToID("_VP_MATRIX");
    private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");
    private static readonly int FrustumCullNearOffsetId = Shader.PropertyToID("_FrustumCullNearOffset");
    private static readonly int FrustumCullEdgeOffsetId = Shader.PropertyToID("_FrustumCullEdgeOffset");
    private static readonly int EnableDistanceCullId = Shader.PropertyToID("_EnableDistanceCull");
    private static readonly int EnableFrustumCullId = Shader.PropertyToID("_EnableFrustumCull");
    private static readonly int LocalWindTexId = Shader.PropertyToID("_LocalWindTex");
    private static readonly int LocalWindScaleId = Shader.PropertyToID("_LocalWindScale");
    private static readonly int LocalWindSpeedId = Shader.PropertyToID("_LocalWindSpeed");
    private static readonly int LocalWindStrengthId = Shader.PropertyToID("_LocalWindStrength");
    private static readonly int LocalWindRotateAmountId = Shader.PropertyToID("_LocalWindRotateAmount");
    private static readonly int Lod0EndDistanceId = Shader.PropertyToID("_Lod0EndDistance");
    private static readonly int Lod1EndDistanceId = Shader.PropertyToID("_Lod1EndDistance");
    private static readonly int LodTransitionWidthId = Shader.PropertyToID("_LodTransitionWidth");
    private static readonly int DispatchGridWidthId = Shader.PropertyToID("_DispatchGridWidth");
    private static readonly int TimeId = Shader.PropertyToID("_Time");
    private static readonly int CullDispatchArgsBufferId = Shader.PropertyToID("_CullDispatchArgsBuffer");

    private static readonly int[] k_SharedLodPosePropertyIds =
    {
        Shader.PropertyToID("_Height"),
        Shader.PropertyToID("_BladeWidth"),
        Shader.PropertyToID("_Tilt"),
        Shader.PropertyToID("_TaperAmount"),
        Shader.PropertyToID("_p1Offset"),
        Shader.PropertyToID("_p2Offset"),
        Shader.PropertyToID("_WaveAmplitude"),
        Shader.PropertyToID("_WaveSpeed"),
        Shader.PropertyToID("_SinOffsetRange"),
        Shader.PropertyToID("_PushTipForward"),
        Shader.PropertyToID("_CurvedNormalAmount")
    };

    [Header("Resources")]
    [Tooltip("Compute shader used for grass generation and culling.")]
    [SerializeField] private ComputeShader _scatterCompute;
    [Tooltip("Material used to draw grass blades.")]
    [SerializeField] private Material _grassMaterial;
    [SerializeField] private Material _grassMaterialLod1;
    [SerializeField] private Material _grassMaterialLod2;
    [Tooltip("Base mesh used for a single grass blade.")]
    [FormerlySerializedAs("_grassMesh")]
    [SerializeField] private Mesh _grassMeshLod0;
    [SerializeField] private Mesh _grassMeshLod1;
    [SerializeField] private Mesh _grassMeshLod2;

    [Header("Rendering")]
    [Tooltip("Height of the overall grass render bounds.")]
    [SerializeField] [Min(0.1f)] private float _boundsHeight = 8.0f;
    [Header("LOD")]
    [SerializeField] [Min(0.0f)] private float _lod0EndDistance = 14.0f;
    [SerializeField] [Min(0.0f)] private float _lod1EndDistance = 28.0f;
    [Tooltip("(Deprecated) LOD cross-fade has been removed. This value is no longer used.")]
    [SerializeField] [Min(0.0f)] private float _lodTransitionWidth = 4.0f;
    [Tooltip("Regenerate grass blades every frame in the editor.")]
    [SerializeField] private bool _regenerateEveryFrame = false;

    [Header("Distribution Map")]
    [Tooltip("RGBA distribution map aligned to terrain XY/XZ. Each channel drives one grass clump shape.")]
    [SerializeField] private Texture2D _distributionMap;
    [Tooltip("Editor-baked placement data. Runtime uploads grass from this asset instead of rescanning the distribution map.")]
    [SerializeField] private NewGrassBakedDataAsset _bakedPlacementAsset;
    [Tooltip("Minimum channel value required before a texel spawns grass for that channel.")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _distributionThreshold = 0.05f;
    [Tooltip("Random in-texel offset applied to painted patches to avoid a visible texel grid.")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _distributionJitter = 0.75f;
    [Tooltip("World-space size of the initial coarse cell before adaptive splitting.")]
    [SerializeField] [Min(0.5f)] private float _cullingCellSize = 12.0f;
    [Tooltip("Minimum grass-count scale kept after remapping painted grayscale density.")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _distributionMinimumGrassCountScale = 0.15f;
    [Tooltip("Minimum cluster-radius scale kept after remapping painted grayscale density.")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _distributionMinimumClusterRadiusScale = 0.35f;
    [Tooltip("Random variance applied to cluster radius.")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _clusterRadiusVariance = 0.4f;
    [Tooltip("Random seed for per-patch jitter and shape variation.")]
    [SerializeField] private int _clusterSeed = 12345;
    [Tooltip("Authoring templates used for generated grass clumps.")]
    [SerializeField] private List<ClumpParameters> _clumpParameters = new List<ClumpParameters> { ClumpParameters.Default };
    [Header("Adaptive Cell")]
    [Tooltip("Maximum number of generated clusters allowed in one coarse culling cell before it is split.")]
    [SerializeField] [Min(1)] private int _maxClustersPerCell = 64;
    [Tooltip("Maximum number of cluster dispatch entries allowed in one coarse culling cell before it is split.")]
    [SerializeField] [Min(1)] private int _maxDispatchesPerCell = 256;
    [Header("Large Cluster Guard")]
    [Tooltip("Maximum generated cluster radius before the cluster is split into smaller children.")]
    [SerializeField] [Min(0.01f)] private float _maxGeneratedClusterRadius = 8.0f;
    [Tooltip("Maximum grass count per generated cluster before the cluster is split.")]
    [SerializeField] [Min(1)] private int _maxGrassPerGeneratedCluster = k_KernelThreadSize * 8;
    [Header("Culling")]
    [Tooltip("Enable distance-based grass culling.")]
    [SerializeField] private bool _enableDistanceCull = true;
    [Tooltip("Distance where distance culling begins.")]
    [SerializeField] [Min(0.0f)] private float _distanceCullStartDist = 40.0f;
    [Tooltip("Distance where distance culling reaches its minimum density.")]
    [SerializeField] [Min(0.0f)] private float _distanceCullEndDist = 100.0f;
    [Tooltip("Minimum grass ratio kept after distance culling.")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _distanceCullMinimumGrassAmount = 0.2f;
    [Tooltip("Enable frustum culling for grass.")]
    [SerializeField] private bool _enableFrustumCull = true;
    [Tooltip("Extra near-plane offset for frustum culling.")]
    [SerializeField] private float _frustumCullNearOffset = 0.0f;
    [Tooltip("Extra edge padding for frustum culling.")]
    [SerializeField] private float _frustumCullEdgeOffset = 0.1f;

    [Header("Wind")]
    [Tooltip("Overall strength of the local wind effect.")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _localWindStrength = 0.5f;
    [Tooltip("World-space scale of the local wind texture.")]
    [SerializeField] private float _localWindScale = 0.01f;
    [Tooltip("Scrolling speed of the local wind texture.")]
    [SerializeField] private float _localWindSpeed = 0.1f;
    [Tooltip("How much local wind rotates grass facing.")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _localWindRotateAmount = 0.3f;

    private ComputeBuffer _grassBladesBufferLod0;
    private ComputeBuffer _grassBladesBufferLod1;
    private ComputeBuffer _grassBladesBufferLod2;
    private ComputeBuffer _generatedGrassBladesBuffer;
    private ComputeBuffer _clusterInstancesBuffer;
    private ComputeBuffer _clusterCullingDataBuffer;
    private ComputeBuffer _clusterDispatchBuffer;
    private ComputeBuffer _visibleClusterDispatchBuffer;
    private ComputeBuffer _clumpParametersBuffer;
    private ComputeBuffer _cullDispatchArgsBuffer;
    private ComputeBuffer _argsBufferLod0;
    private ComputeBuffer _argsBufferLod1;
    private ComputeBuffer _argsBufferLod2;
    private MaterialPropertyBlock _propertyBlock;
    private Mesh _runtimeMeshLod0;
    private Mesh _runtimeMeshLod1;
    private Mesh _runtimeMeshLod2;
    private Mesh _drawMeshLod0;
    private Mesh _drawMeshLod1;
    private Mesh _drawMeshLod2;
    private int _generateKernel = -1;
    private int _clusterCullKernel = -1;
    private int _cullKernel = -1;
    private int _fixupCullDispatchArgsKernel = -1;
    private bool _clusterDataDirty = true;
    private bool _generatedGrassDirty = true;
    private int _clusterCount;
    private int _clusterCullingCount;
    private int _clusterDispatchCount;
    private int _generatedBladeCount;

    private Terrain _terrain;
    private Texture2D _heightMap;
    private SeamlessNoiseGenerator _localWindGenerator;
    private int _lastTerrainInstanceId = -1;
    private Vector3 _lastTerrainSize;

    private Camera _activeCullCamera;
    private int _lastCullCameraInstanceId = -1;
    private Vector3 _lastCullCameraPosition;
    private Quaternion _lastCullCameraRotation;
    private Matrix4x4 _lastCullViewProjectionMatrix;
    private bool _hasCullState;
    private readonly Vector4[] _coarseCullFrustumPlanes = new Vector4[k_FrustumPlaneCount];
    private bool _hasLoggedMissingBakeWarning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterGrassGpuPassDebugInfos()
    {
        RegisterGrassRenderGpuPass(GrassRenderLod0Marker, GrassShaderLod0File);
        RegisterGrassRenderGpuPass(GrassRenderLod1Marker, GrassShaderLod1File);
        RegisterGrassRenderGpuPass(GrassRenderLod2Marker, GrassShaderLod2File);
    }

    private static void RegisterGrassRenderGpuPass(string passName, string shaderFile)
    {
        GpuPassDebugRegistry.Register(new GpuPassDebugInfo
        {
            passName = passName,
            cppFile = GrassRendererCppFile,
            cppFunction = "NewGrassIndirectRenderer.DrawGrassLod",
            shaderFile = shaderFile,
            shaderEntry = GrassRenderShaderEntry,
            passType = "indirect_draw",
            dispatchKind = "draw_mesh_instanced_indirect"
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClumpParametersGPU
    {
        public float pullToCentre;
        public float pointInSameDirection;
        public float baseHeight;
        public float heightRandom;
        public float baseWidth;
        public float widthRandom;
        public float baseTilt;
        public float tiltRandom;
        public float baseBend;
        public float bendRandom;
        public float clusterRadius;
        public float grassPerCluster;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClusterInstanceGPU
    {
        public Vector3 centerWS;
        public float sigma;
        public uint grassCount;
        public uint clumpTypeIndex;
        public float sharedFacingAngle;
        public uint startIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClusterDispatchGPU
    {
        public uint clusterIndex;
        public uint sampleOffset;
    }

    private struct ClusterCullingData
    {
        public Bounds boundsWS;
        public int dispatchStartIndex;
        public int dispatchCount;
    }

    private struct ClusterSeedBuildData
    {
        public Vector2 centerXZLocal;
        public Vector3 centerWS;
        public float sigma;
        public int grassCount;
        public int dispatchGroupCount;
        public int clumpTypeIndex;
        public float sharedFacingAngle;
        public ClumpParameters clumpTemplate;
        public Bounds boundsWS;
    }

    private struct AdaptiveCellBuildData
    {
        public Rect localRect;
        public List<ClusterSeedBuildData> seeds;
        public int dispatchCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClusterCullingDataGPU
    {
        public Vector3 minWS;
        public uint dispatchStartIndex;
        public Vector3 maxWS;
        public uint dispatchCount;
    }

    private void OnEnable()
    {
        GpuSemanticDrawRegistry.Register(this);
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        _clusterDataDirty = true;
        _generatedGrassDirty = true;
        EnsureResources();
        RebuildGrass();
    }

    private void OnValidate()
    {
        _boundsHeight = Mathf.Max(0.1f, _boundsHeight);
        _lod0EndDistance = Mathf.Max(0.0f, _lod0EndDistance);
        _lod1EndDistance = Mathf.Max(_lod0EndDistance, _lod1EndDistance);
        _lodTransitionWidth = Mathf.Max(0.0f, _lodTransitionWidth);
        _distributionThreshold = Mathf.Clamp01(_distributionThreshold);
        _distributionJitter = Mathf.Clamp01(_distributionJitter);
        _cullingCellSize = Mathf.Max(0.5f, _cullingCellSize);
        _distributionMinimumGrassCountScale = Mathf.Clamp01(_distributionMinimumGrassCountScale);
        _distributionMinimumClusterRadiusScale = Mathf.Clamp01(_distributionMinimumClusterRadiusScale);
        _clusterRadiusVariance = Mathf.Clamp01(_clusterRadiusVariance);
        _maxClustersPerCell = Mathf.Max(1, _maxClustersPerCell);
        _maxDispatchesPerCell = Mathf.Max(1, _maxDispatchesPerCell);
        _maxGeneratedClusterRadius = Mathf.Max(0.01f, _maxGeneratedClusterRadius);
        _maxGrassPerGeneratedCluster = Mathf.Max(1, _maxGrassPerGeneratedCluster);
        _distanceCullStartDist = Mathf.Max(0.0f, _distanceCullStartDist);
        _distanceCullEndDist = Mathf.Max(_distanceCullStartDist, _distanceCullEndDist);
        _distanceCullMinimumGrassAmount = Mathf.Clamp01(_distanceCullMinimumGrassAmount);
        _localWindStrength = Mathf.Clamp01(_localWindStrength);
        _localWindScale = Mathf.Max(0.0f, _localWindScale);
        _localWindSpeed = Mathf.Max(0.0f, _localWindSpeed);
        _localWindRotateAmount = Mathf.Clamp01(_localWindRotateAmount);
        EnsureDefaultClumpParameters();
        SanitizeClumpParameters();
        _clusterDataDirty = true;
        _generatedGrassDirty = true;

        if (isActiveAndEnabled)
        {
            EnsureResources();
            RebuildGrass();
        }
    }

    private void Update()
    {
        if (!EnsureResources())
            return;

        if (_regenerateEveryFrame && !Application.isPlaying)
            _generatedGrassDirty = true;

        if (_generatedGrassDirty)
            GenerateGrassGeometry();

        UpdateVisibleGrass();
        if (!ShouldUseGpuSemanticDrawPass())
            DrawGrass();
    }

    private void OnDisable()
    {
        GpuSemanticDrawRegistry.Unregister(this);
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        ReleaseResources();
    }

    [ContextMenu("Rebuild Grass")]
    public void RebuildGrass()
    {
        if (!EnsureResources())
            return;

        _clusterDataDirty = true;
        _generatedGrassDirty = true;
        EnsureClusterData();
        GenerateGrassGeometry();
        UpdateVisibleGrass();
    }

    private bool EnsureResources()
    {
        EnsureDefaultClumpParameters();
        SanitizeClumpParameters();

        if (_scatterCompute == null || _grassMaterial == null)
            return false;

        if (_generateKernel < 0)
            _generateKernel = _scatterCompute.FindKernel("GenerateBlades");

        if (_clusterCullKernel < 0)
            _clusterCullKernel = _scatterCompute.FindKernel("CullClusters");

        if (_cullKernel < 0)
            _cullKernel = _scatterCompute.FindKernel("CullAndLod");

        if (_fixupCullDispatchArgsKernel < 0)
            _fixupCullDispatchArgsKernel = _scatterCompute.FindKernel("FixupCullDispatchArgs");

        NewGrassTerrainProvider provider = NewGrassTerrainProvider.Instance;
        _terrain = provider != null ? provider.Terrain : null;
        _heightMap = provider != null ? provider.HeightMap : null;

        if (_terrain == null || _terrain.terrainData == null || _heightMap == null)
            return false;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        _drawMeshLod0 = ResolveGrassMesh(_grassMeshLod0, ref _runtimeMeshLod0, NewGrassBladeMeshFactory.CreateHighLODMesh);
        _drawMeshLod1 = ResolveGrassMesh(_grassMeshLod1, ref _runtimeMeshLod1, NewGrassBladeMeshFactory.CreateMidLODMesh);
        _drawMeshLod2 = ResolveGrassMesh(_grassMeshLod2, ref _runtimeMeshLod2, NewGrassBladeMeshFactory.CreateLowLODMesh);

        int terrainInstanceId = _terrain.GetInstanceID();
        Vector3 terrainSize = _terrain.terrainData.size;
        if (terrainInstanceId != _lastTerrainInstanceId || !Approximately(_lastTerrainSize, terrainSize))
        {
            _lastTerrainInstanceId = terrainInstanceId;
            _lastTerrainSize = terrainSize;
            _clusterDataDirty = true;
            _generatedGrassDirty = true;
        }

        EnsureClusterData();

        int bladeCapacity = Mathf.Max(1, _generatedBladeCount);
        EnsureGeneratedGrassBuffer(ref _generatedGrassBladesBuffer, bladeCapacity);
        EnsureGrassBladeBuffer(ref _grassBladesBufferLod0, bladeCapacity);
        EnsureGrassBladeBuffer(ref _grassBladesBufferLod1, bladeCapacity);
        EnsureGrassBladeBuffer(ref _grassBladesBufferLod2, bladeCapacity);
        EnsureVisibleClusterDispatchBuffer(Mathf.Max(1, _clusterDispatchCount));
        EnsureCullDispatchArgsBuffer(ref _cullDispatchArgsBuffer);
        EnsureArgsBuffer(ref _argsBufferLod0);
        EnsureArgsBuffer(ref _argsBufferLod1);
        EnsureArgsBuffer(ref _argsBufferLod2);

        return _drawMeshLod0 != null && _drawMeshLod1 != null && _drawMeshLod2 != null;
    }

    private Mesh ResolveGrassMesh(Mesh assignedMesh, ref Mesh runtimeMesh, Func<Mesh> factory)
    {
        if (assignedMesh != null)
            return assignedMesh;

        if (runtimeMesh == null)
            runtimeMesh = factory();

        return runtimeMesh;
    }

    private void EnsureGrassBladeBuffer(ref ComputeBuffer buffer, int bladeCapacity)
    {
        if (buffer != null && buffer.count == bladeCapacity)
            return;

        buffer?.Release();
        buffer = new ComputeBuffer(bladeCapacity, k_VisibleGrassInstanceStride, ComputeBufferType.Append);
    }

    private void EnsureGeneratedGrassBuffer(ref ComputeBuffer buffer, int bladeCapacity)
    {
        if (buffer != null && buffer.count == bladeCapacity)
            return;

        buffer?.Release();
        buffer = new ComputeBuffer(bladeCapacity, k_GeneratedGrassBladeStride);
        _generatedGrassDirty = true;
    }

    private void EnsureVisibleClusterDispatchBuffer(int dispatchCapacity)
    {
        if (_visibleClusterDispatchBuffer != null && _visibleClusterDispatchBuffer.count == dispatchCapacity)
            return;

        _visibleClusterDispatchBuffer?.Release();
        _visibleClusterDispatchBuffer = new ComputeBuffer(dispatchCapacity, Marshal.SizeOf<ClusterDispatchGPU>(), ComputeBufferType.Append);
    }

    private void EnsureCullDispatchArgsBuffer(ref ComputeBuffer buffer)
    {
        if (buffer != null)
            return;

        buffer = new ComputeBuffer(1, k_DispatchIndirectArgsCount * sizeof(uint), ComputeBufferType.IndirectArguments | ComputeBufferType.Raw);
        buffer.SetData(new uint[] { 0u, 1u, 1u, 0u });
    }

    private void EnsureArgsBuffer(ref ComputeBuffer buffer)
    {
        if (buffer != null)
            return;

        buffer = new ComputeBuffer(1, k_ArgsCount * sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    private void EnsureClusterData()
    {
        if (!_clusterDataDirty
            && _clusterInstancesBuffer != null
            && _clusterCullingDataBuffer != null
            && _clusterDispatchBuffer != null
            && _visibleClusterDispatchBuffer != null
            && _clumpParametersBuffer != null)
            return;

        ClusterInstanceGPU[] clusters = Array.Empty<ClusterInstanceGPU>();
        ClusterCullingDataGPU[] clusterCullingData = Array.Empty<ClusterCullingDataGPU>();
        ClusterDispatchGPU[] dispatches = Array.Empty<ClusterDispatchGPU>();
        int generatedBladeCount = 0;

#if UNITY_EDITOR
        if (!Application.isPlaying && ShouldAutoBakePlacementData())
            BakeDistributionDataAsset(false);
#endif

        if (!TryLoadBakedPlacementData(out clusters, out clusterCullingData, out dispatches, out generatedBladeCount))
        {
            clusters = Array.Empty<ClusterInstanceGPU>();
            clusterCullingData = Array.Empty<ClusterCullingDataGPU>();
            dispatches = Array.Empty<ClusterDispatchGPU>();
            generatedBladeCount = 0;
        }

        _clusterCount = clusters.Length;
        _clusterCullingCount = clusterCullingData.Length;
        _generatedBladeCount = generatedBladeCount;
        _clusterDispatchCount = dispatches.Length;

        int clusterBufferCount = Mathf.Max(1, _clusterCount);
        int clusterStride = Marshal.SizeOf<ClusterInstanceGPU>();
        if (_clusterInstancesBuffer == null || _clusterInstancesBuffer.count != clusterBufferCount)
        {
            _clusterInstancesBuffer?.Release();
            _clusterInstancesBuffer = new ComputeBuffer(clusterBufferCount, clusterStride);
        }

        ClusterInstanceGPU[] clusterArray = _clusterCount > 0 ? clusters : new ClusterInstanceGPU[1];
        _clusterInstancesBuffer.SetData(clusterArray);

        int clusterCullingBufferCount = Mathf.Max(1, _clusterCullingCount);
        int clusterCullingStride = Marshal.SizeOf<ClusterCullingDataGPU>();
        if (_clusterCullingDataBuffer == null || _clusterCullingDataBuffer.count != clusterCullingBufferCount)
        {
            _clusterCullingDataBuffer?.Release();
            _clusterCullingDataBuffer = new ComputeBuffer(clusterCullingBufferCount, clusterCullingStride);
        }

        ClusterCullingDataGPU[] clusterCullingArray = _clusterCullingCount > 0 ? clusterCullingData : new ClusterCullingDataGPU[1];
        _clusterCullingDataBuffer.SetData(clusterCullingArray);

        int dispatchBufferCount = Mathf.Max(1, _clusterDispatchCount);
        int dispatchStride = Marshal.SizeOf<ClusterDispatchGPU>();
        if (_clusterDispatchBuffer == null || _clusterDispatchBuffer.count != dispatchBufferCount)
        {
            _clusterDispatchBuffer?.Release();
            _clusterDispatchBuffer = new ComputeBuffer(dispatchBufferCount, dispatchStride);
        }

        ClusterDispatchGPU[] dispatchArray = _clusterDispatchCount > 0 ? dispatches : new ClusterDispatchGPU[1];
        _clusterDispatchBuffer.SetData(dispatchArray);
        EnsureVisibleClusterDispatchBuffer(dispatchBufferCount);

        int clumpCount = Mathf.Max(1, _clumpParameters.Count);
        int clumpStride = Marshal.SizeOf<ClumpParametersGPU>();
        if (_clumpParametersBuffer == null || _clumpParametersBuffer.count != clumpCount)
        {
            _clumpParametersBuffer?.Release();
            _clumpParametersBuffer = new ComputeBuffer(clumpCount, clumpStride);
        }

        UpdateClumpParametersBuffer();
        _clusterDataDirty = false;
        _generatedGrassDirty = true;
    }

    private bool TryLoadBakedPlacementData(
        out ClusterInstanceGPU[] clusters,
        out ClusterCullingDataGPU[] clusterCullingData,
        out ClusterDispatchGPU[] dispatches,
        out int generatedBladeCount)
    {
        clusters = Array.Empty<ClusterInstanceGPU>();
        clusterCullingData = Array.Empty<ClusterCullingDataGPU>();
        dispatches = Array.Empty<ClusterDispatchGPU>();
        generatedBladeCount = 0;

        if (_bakedPlacementAsset == null || !_bakedPlacementAsset.HasData)
        {
            LogMissingBakeWarning("Grass bake asset is missing or empty. Runtime grass generation now expects baked placement data.");
            return false;
        }

        if (_bakedPlacementAsset.SourceDistributionMap != null && _distributionMap != null && _bakedPlacementAsset.SourceDistributionMap != _distributionMap)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return false;
#endif
            LogMissingBakeWarning("Grass bake asset was generated from a different distribution map. Using the baked data anyway.");
        }

        int currentSettingsHash = ComputePlacementSettingsHash();
        if (_bakedPlacementAsset.SourceSettingsHash != 0 && _bakedPlacementAsset.SourceSettingsHash != currentSettingsHash)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return false;
#endif
            LogMissingBakeWarning("Grass bake asset settings do not match the current renderer settings. Using the baked data anyway.");
        }

        NewGrassBakedClusterInstanceData[] bakedClusters = _bakedPlacementAsset.Clusters;
        NewGrassBakedClusterCullingCellData[] bakedCells = _bakedPlacementAsset.CullingCells;
        NewGrassBakedClusterDispatchData[] bakedDispatches = _bakedPlacementAsset.Dispatches;
        generatedBladeCount = Mathf.Max(0, _bakedPlacementAsset.TotalBladeCount);

        Vector3 terrainPosition = _terrain != null ? _terrain.transform.position : transform.position;

        clusters = new ClusterInstanceGPU[bakedClusters.Length];
        for (int i = 0; i < bakedClusters.Length; i++)
        {
            NewGrassBakedClusterInstanceData bakedCluster = bakedClusters[i];
            clusters[i] = new ClusterInstanceGPU
            {
                centerWS = terrainPosition + new Vector3(bakedCluster.centerLocalXZ.x, 0.0f, bakedCluster.centerLocalXZ.y),
                sigma = Mathf.Max(0.01f, bakedCluster.sigma),
                grassCount = (uint)Mathf.Max(0, bakedCluster.grassCount),
                clumpTypeIndex = (uint)Mathf.Max(0, bakedCluster.clumpTypeIndex),
                sharedFacingAngle = bakedCluster.sharedFacingAngle,
                startIndex = (uint)Mathf.Max(0, bakedCluster.startIndex)
            };
        }

        clusterCullingData = new ClusterCullingDataGPU[bakedCells.Length];
        for (int i = 0; i < bakedCells.Length; i++)
        {
            NewGrassBakedClusterCullingCellData bakedCell = bakedCells[i];
            clusterCullingData[i] = new ClusterCullingDataGPU
            {
                minWS = terrainPosition + bakedCell.minLocal,
                dispatchStartIndex = (uint)Mathf.Max(0, bakedCell.dispatchStartIndex),
                maxWS = terrainPosition + bakedCell.maxLocal,
                dispatchCount = (uint)Mathf.Max(0, bakedCell.dispatchCount)
            };
        }

        dispatches = new ClusterDispatchGPU[bakedDispatches.Length];
        for (int i = 0; i < bakedDispatches.Length; i++)
        {
            NewGrassBakedClusterDispatchData bakedDispatch = bakedDispatches[i];
            dispatches[i] = new ClusterDispatchGPU
            {
                clusterIndex = (uint)Mathf.Max(0, bakedDispatch.clusterIndex),
                sampleOffset = (uint)Mathf.Max(0, bakedDispatch.sampleOffset)
            };
        }

        _hasLoggedMissingBakeWarning = false;
        return true;
    }

    private int ComputePlacementSettingsHash()
    {
        int hash = 17;
        hash = CombineHash(hash, _distributionMap != null ? GetDeterministicStringHash(_distributionMap.name) : 0);
        hash = CombineHash(hash, _distributionMap != null ? _distributionMap.width : 0);
        hash = CombineHash(hash, _distributionMap != null ? _distributionMap.height : 0);
        hash = CombineHash(hash, _distributionThreshold.GetHashCode());
        hash = CombineHash(hash, _distributionJitter.GetHashCode());
        hash = CombineHash(hash, _cullingCellSize.GetHashCode());
        hash = CombineHash(hash, _distributionMinimumGrassCountScale.GetHashCode());
        hash = CombineHash(hash, _distributionMinimumClusterRadiusScale.GetHashCode());
        hash = CombineHash(hash, _clusterRadiusVariance.GetHashCode());
        hash = CombineHash(hash, _clusterSeed);
        hash = CombineHash(hash, _maxClustersPerCell);
        hash = CombineHash(hash, _maxDispatchesPerCell);
        hash = CombineHash(hash, _maxGeneratedClusterRadius.GetHashCode());
        hash = CombineHash(hash, _maxGrassPerGeneratedCluster);

        Vector3 terrainSize = _terrain != null && _terrain.terrainData != null ? _terrain.terrainData.size : Vector3.zero;
        hash = CombineHash(hash, terrainSize.x.GetHashCode());
        hash = CombineHash(hash, terrainSize.y.GetHashCode());
        hash = CombineHash(hash, terrainSize.z.GetHashCode());

        if (_clumpParameters != null)
        {
            hash = CombineHash(hash, _clumpParameters.Count);
            for (int i = 0; i < _clumpParameters.Count; i++)
            {
                ClumpParameters clump = _clumpParameters[i];
                hash = CombineHash(hash, clump.pullToCentre.GetHashCode());
                hash = CombineHash(hash, clump.pointInSameDirection.GetHashCode());
                hash = CombineHash(hash, clump.baseHeight.GetHashCode());
                hash = CombineHash(hash, clump.heightRandom.GetHashCode());
                hash = CombineHash(hash, clump.baseWidth.GetHashCode());
                hash = CombineHash(hash, clump.widthRandom.GetHashCode());
                hash = CombineHash(hash, clump.baseTilt.GetHashCode());
                hash = CombineHash(hash, clump.tiltRandom.GetHashCode());
                hash = CombineHash(hash, clump.baseBend.GetHashCode());
                hash = CombineHash(hash, clump.bendRandom.GetHashCode());
                hash = CombineHash(hash, clump.clusterRadius.GetHashCode());
                hash = CombineHash(hash, clump.grassPerCluster);
            }
        }

        return hash;
    }

    private static int CombineHash(int hash, int value)
    {
        unchecked
        {
            return (hash * 397) ^ value;
        }
    }

    private static int GetDeterministicStringHash(string value)
    {
        unchecked
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            int hash = 23;
            for (int i = 0; i < value.Length; i++)
                hash = (hash * 31) + value[i];

            return hash;
        }
    }

    private void LogMissingBakeWarning(string message)
    {
        if (_hasLoggedMissingBakeWarning)
            return;

        Debug.LogWarning(message, this);
        _hasLoggedMissingBakeWarning = true;
    }

#if UNITY_EDITOR
    public bool BakeDistributionDataAsset(bool saveAsset)
    {
        EnsureDefaultClumpParameters();
        SanitizeClumpParameters();

        if (_bakedPlacementAsset == null)
            return false;

        _terrain = ResolveAvailableTerrain();
        if (_terrain == null || _terrain.terrainData == null || _distributionMap == null)
            return false;

        List<ClusterCullingData> clusterCullingData = new List<ClusterCullingData>();
        List<ClusterDispatchGPU> dispatches;
        List<ClusterInstanceGPU> clusters = GenerateClusterInstances(out int generatedBladeCount, out int clusterCullingCount, clusterCullingData, out dispatches);

        Vector3 terrainPosition = _terrain.transform.position;
        NewGrassBakedClusterInstanceData[] bakedClusters = new NewGrassBakedClusterInstanceData[clusters.Count];
        for (int i = 0; i < clusters.Count; i++)
        {
            ClusterInstanceGPU cluster = clusters[i];
            Vector3 localCenter = cluster.centerWS - terrainPosition;
            bakedClusters[i] = new NewGrassBakedClusterInstanceData
            {
                centerLocalXZ = new Vector2(localCenter.x, localCenter.z),
                sigma = cluster.sigma,
                grassCount = Mathf.Max(0, (int)cluster.grassCount),
                clumpTypeIndex = Mathf.Max(0, (int)cluster.clumpTypeIndex),
                sharedFacingAngle = cluster.sharedFacingAngle,
                startIndex = Mathf.Max(0, (int)cluster.startIndex)
            };
        }

        NewGrassBakedClusterCullingCellData[] bakedCells = new NewGrassBakedClusterCullingCellData[clusterCullingCount];
        for (int i = 0; i < clusterCullingCount; i++)
        {
            ClusterCullingData cullingData = clusterCullingData[i];
            bakedCells[i] = new NewGrassBakedClusterCullingCellData
            {
                minLocal = cullingData.boundsWS.min - terrainPosition,
                dispatchStartIndex = Mathf.Max(0, cullingData.dispatchStartIndex),
                maxLocal = cullingData.boundsWS.max - terrainPosition,
                dispatchCount = Mathf.Max(0, cullingData.dispatchCount)
            };
        }

        NewGrassBakedClusterDispatchData[] bakedDispatches = new NewGrassBakedClusterDispatchData[dispatches.Count];
        for (int i = 0; i < dispatches.Count; i++)
        {
            ClusterDispatchGPU dispatch = dispatches[i];
            bakedDispatches[i] = new NewGrassBakedClusterDispatchData
            {
                clusterIndex = Mathf.Max(0, (int)dispatch.clusterIndex),
                sampleOffset = Mathf.Max(0, (int)dispatch.sampleOffset)
            };
        }

        _bakedPlacementAsset.SetData(
            _distributionMap,
            ComputePlacementSettingsHash(),
            generatedBladeCount,
            bakedClusters,
            bakedCells,
            bakedDispatches);
        EditorUtility.SetDirty(_bakedPlacementAsset);
        if (saveAsset)
            AssetDatabase.SaveAssetIfDirty(_bakedPlacementAsset);

        _clusterDataDirty = true;
        _generatedGrassDirty = true;
        _hasLoggedMissingBakeWarning = false;
        return true;
    }

    private bool ShouldAutoBakePlacementData()
    {
        if (_bakedPlacementAsset == null || _distributionMap == null)
            return false;

        if (!_bakedPlacementAsset.HasData)
            return true;

        if (_bakedPlacementAsset.SourceDistributionMap != _distributionMap)
            return true;

        return _bakedPlacementAsset.SourceSettingsHash != ComputePlacementSettingsHash();
    }
#endif

    private List<ClusterInstanceGPU> GenerateClusterInstances(
        out int totalBladeCount,
        out int clusterCullingCount,
        List<ClusterCullingData> clusterCullingData,
        out List<ClusterDispatchGPU> dispatches)
    {
        totalBladeCount = 0;
        clusterCullingCount = 0;
        dispatches = new List<ClusterDispatchGPU>();

        if (_terrain == null || _terrain.terrainData == null || _distributionMap == null)
            return new List<ClusterInstanceGPU>();

        if (!TryGetDistributionMapPixels(out Color32[] pixels, out int width, out int height))
            return new List<ClusterInstanceGPU>();

        TerrainData terrainData = _terrain.terrainData;
        Vector3 terrainPosition = _terrain.transform.position;
        Vector3 terrainSize = terrainData.size;
        int clumpTypeCount = Mathf.Max(1, _clumpParameters.Count);
        int supportedChannelCount = Mathf.Min(k_DistributionChannelCount, clumpTypeCount);
        if (supportedChannelCount <= 0)
            return new List<ClusterInstanceGPU>();

        float texelWidth = terrainSize.x / Mathf.Max(1, width);
        float texelLength = terrainSize.z / Mathf.Max(1, height);
        List<ClusterSeedBuildData> allSeeds = new List<ClusterSeedBuildData>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color32 pixel = pixels[(y * width) + x];
                for (int channelIndex = 0; channelIndex < supportedChannelCount; channelIndex++)
                {
                    float density01 = RemapDistributionDensity01(GetDistributionChannelValue01(pixel, channelIndex));
                    if (density01 <= 0.0f)
                        continue;

                    AppendDistributionClusterSeeds(
                        x,
                        y,
                        texelWidth,
                        texelLength,
                        channelIndex,
                        density01,
                        terrainData,
                        terrainPosition,
                        terrainSize,
                        allSeeds);
                }
            }
        }

        List<AdaptiveCellBuildData> adaptiveCells = BuildAdaptiveCells(allSeeds, terrainSize);
        List<ClusterInstanceGPU> clusters = FlattenAdaptiveCells(adaptiveCells, clusterCullingData, out totalBladeCount, out dispatches);
        clusterCullingCount = clusterCullingData.Count;
        return clusters;
    }

    private float RemapDistributionDensity01(float channelValue)
    {
        if (channelValue <= _distributionThreshold)
            return 0.0f;

        return Mathf.InverseLerp(_distributionThreshold, 1.0f, channelValue);
    }

    private int CalculateGeneratedClusterSplitCount(float sigma, int grassCount)
    {
        float safeMaxClusterRadius = Mathf.Max(0.01f, _maxGeneratedClusterRadius);
        int safeMaxGrassPerCluster = Mathf.Max(1, _maxGrassPerGeneratedCluster);
        float radiusSplitCount = (sigma * sigma) / (safeMaxClusterRadius * safeMaxClusterRadius);
        float grassSplitCount = grassCount / (float)safeMaxGrassPerCluster;
        int splitCount = Mathf.CeilToInt(Mathf.Max(1.0f, Mathf.Max(radiusSplitCount, grassSplitCount)));
        return Mathf.Clamp(splitCount, 1, Mathf.Max(1, grassCount));
    }

    private static int DistributeGrassCount(int totalGrassCount, int splitCount, int splitIndex)
    {
        int baseGrassCount = totalGrassCount / splitCount;
        int remainder = totalGrassCount % splitCount;
        return baseGrassCount + (splitIndex < remainder ? 1 : 0);
    }

    private static Vector2 CalculateSplitClusterOffset(int splitIndex, int splitCount, float splitRadius, float splitRotation)
    {
        if (splitCount <= 1 || splitRadius <= 0.0f)
            return Vector2.zero;

        float radius01 = Mathf.Sqrt((splitIndex + 0.5f) / splitCount);
        float theta = splitRotation + splitIndex * k_GoldenAngle;
        return new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * (splitRadius * radius01);
    }

    private void AppendDistributionClusterSeeds(
        int texelX,
        int texelY,
        float texelWidth,
        float texelLength,
        int channelIndex,
        float density01,
        TerrainData terrainData,
        Vector3 terrainPosition,
        Vector3 terrainSize,
        List<ClusterSeedBuildData> allSeeds)
    {
        if (density01 <= 0.0f)
            return;

        if (density01 < 1.0f && Hash01(texelX, texelY, channelIndex, 59) > density01)
            return;

        int clumpTypeIndex = Mathf.Clamp(channelIndex, 0, Mathf.Max(0, _clumpParameters.Count - 1));
        ClumpParameters clumpTemplate = _clumpParameters[clumpTypeIndex];
        float varianceScale = Mathf.Lerp(1.0f - (_clusterRadiusVariance * 0.5f), 1.0f + (_clusterRadiusVariance * 0.5f), Hash01(texelX, texelY, channelIndex, 11));
        float clusterRadiusScale = Mathf.Lerp(_distributionMinimumClusterRadiusScale, 1.0f, density01);
        float sigma = Mathf.Max(0.01f, clumpTemplate.clusterRadius * varianceScale * clusterRadiusScale);
        float grassCountScale = Mathf.Lerp(_distributionMinimumGrassCountScale, 1.0f, density01);
        float scaledGrassCount = Mathf.Max(0.0f, clumpTemplate.grassPerCluster * grassCountScale);
        int grassCount = StochasticRoundToInt(scaledGrassCount, Hash01(texelX, texelY, channelIndex, 71));
        if (grassCount <= 0)
            return;

        float sharedFacingAngle = Hash01(texelX, texelY, channelIndex, 23) * Mathf.PI * 2.0f;

        Vector2 localCenter = new Vector2(
            (texelX + 0.5f) * texelWidth,
            (texelY + 0.5f) * texelLength);
        Vector2 jitter = new Vector2(
            (Hash01(texelX, texelY, channelIndex, 31) - 0.5f) * texelWidth,
            (Hash01(texelX, texelY, channelIndex, 47) - 0.5f) * texelLength) * _distributionJitter;
        localCenter += jitter;

        int splitCount = CalculateGeneratedClusterSplitCount(sigma, grassCount);
        if (splitCount <= 1)
        {
            if (TryBuildGeneratedClusterSeed(
                localCenter,
                sigma,
                grassCount,
                clumpTypeIndex,
                sharedFacingAngle,
                clumpTemplate,
                terrainData,
                terrainPosition,
                terrainSize,
                out ClusterSeedBuildData seed))
            {
                allSeeds.Add(seed);
            }

            return;
        }

        float childSigma = Mathf.Max(0.01f, sigma / Mathf.Sqrt(splitCount));
        float splitRadius = Mathf.Max(0.0f, sigma - childSigma);
        for (int splitIndex = 0; splitIndex < splitCount; splitIndex++)
        {
            int childGrassCount = DistributeGrassCount(grassCount, splitCount, splitIndex);
            if (childGrassCount <= 0)
                continue;

            Vector2 childOffset = CalculateSplitClusterOffset(splitIndex, splitCount, splitRadius, sharedFacingAngle);
            Vector2 childLocalCenter = localCenter + childOffset;
            float childFacingAngle = Mathf.Repeat(sharedFacingAngle + splitIndex * k_GoldenAngle, Mathf.PI * 2.0f);
            if (!TryBuildGeneratedClusterSeed(
                childLocalCenter,
                childSigma,
                childGrassCount,
                clumpTypeIndex,
                childFacingAngle,
                clumpTemplate,
                terrainData,
                terrainPosition,
                terrainSize,
                out ClusterSeedBuildData childSeed))
            {
                continue;
            }

            allSeeds.Add(childSeed);
        }
    }

    private bool TryBuildGeneratedClusterSeed(
        Vector2 localCenter,
        float sigma,
        int grassCount,
        int clumpTypeIndex,
        float sharedFacingAngle,
        ClumpParameters clumpTemplate,
        TerrainData terrainData,
        Vector3 terrainPosition,
        Vector3 terrainSize,
        out ClusterSeedBuildData seed)
    {
        seed = default;
        if (grassCount <= 0)
            return false;

        float maxBladeRootRadius = Mathf.Max(0.01f, sigma) * k_MaxBladeRootRadiusScale;
        localCenter = ClampClusterCenterInsideTerrain(localCenter, maxBladeRootRadius, terrainSize);
        Vector3 clusterCenterWS = new Vector3(
            terrainPosition.x + localCenter.x,
            terrainPosition.y,
            terrainPosition.z + localCenter.y);

        ClusterInstanceGPU clusterInstance = new ClusterInstanceGPU
        {
            centerWS = clusterCenterWS,
            sigma = Mathf.Max(0.01f, sigma),
            grassCount = (uint)Mathf.Max(1, grassCount),
            clumpTypeIndex = (uint)clumpTypeIndex,
            sharedFacingAngle = sharedFacingAngle,
            startIndex = 0u
        };

        seed = new ClusterSeedBuildData
        {
            centerXZLocal = localCenter,
            centerWS = clusterCenterWS,
            sigma = clusterInstance.sigma,
            grassCount = (int)clusterInstance.grassCount,
            dispatchGroupCount = GetDispatchGroupCount((int)clusterInstance.grassCount),
            clumpTypeIndex = clumpTypeIndex,
            sharedFacingAngle = sharedFacingAngle,
            clumpTemplate = clumpTemplate,
            boundsWS = BuildClusterBounds(clusterInstance, clumpTemplate, terrainData, terrainPosition, terrainSize)
        };
        return true;
    }

    private static int StochasticRoundToInt(float value, float random01)
    {
        if (value <= 0.0f)
            return 0;

        int floorValue = Mathf.FloorToInt(value);
        float fractional = value - floorValue;
        return floorValue + (random01 < fractional ? 1 : 0);
    }

    private static Vector2 ClampClusterCenterInsideTerrain(Vector2 localCenter, float maxBladeRootRadius, Vector3 terrainSize)
    {
        // Keep the entire blade root distribution inside the terrain so we do not
        // collapse a large number of out-of-bounds blades onto the terrain edge later.
        float minX = Mathf.Clamp(maxBladeRootRadius, 0.0f, terrainSize.x * 0.5f);
        float minZ = Mathf.Clamp(maxBladeRootRadius, 0.0f, terrainSize.z * 0.5f);
        float maxX = Mathf.Max(minX, terrainSize.x - minX);
        float maxZ = Mathf.Max(minZ, terrainSize.z - minZ);
        return new Vector2(
            Mathf.Clamp(localCenter.x, minX, maxX),
            Mathf.Clamp(localCenter.y, minZ, maxZ));
    }

    private Terrain ResolveAvailableTerrain()
    {
        NewGrassTerrainProvider provider = NewGrassTerrainProvider.Instance;
        if (provider != null && provider.Terrain != null)
            return provider.Terrain;

        Terrain terrain = GetComponent<Terrain>();
        if (terrain != null)
            return terrain;

        return Terrain.activeTerrain;
    }

    private List<AdaptiveCellBuildData> BuildAdaptiveCells(List<ClusterSeedBuildData> allSeeds, Vector3 terrainSize)
    {
        List<AdaptiveCellBuildData> result = new List<AdaptiveCellBuildData>();
        if (allSeeds == null || allSeeds.Count == 0)
            return result;

        int cellCountX = Mathf.Max(1, Mathf.CeilToInt(terrainSize.x / Mathf.Max(0.5f, _cullingCellSize)));
        int cellCountZ = Mathf.Max(1, Mathf.CeilToInt(terrainSize.z / Mathf.Max(0.5f, _cullingCellSize)));
        Dictionary<int, List<ClusterSeedBuildData>> baseBuckets = new Dictionary<int, List<ClusterSeedBuildData>>();

        for (int i = 0; i < allSeeds.Count; i++)
        {
            ClusterSeedBuildData seed = allSeeds[i];
            float localX = Mathf.Clamp(seed.centerXZLocal.x, 0.0f, Mathf.Max(0.0f, terrainSize.x - 0.0001f));
            float localZ = Mathf.Clamp(seed.centerXZLocal.y, 0.0f, Mathf.Max(0.0f, terrainSize.z - 0.0001f));
            int cellX = Mathf.Clamp(Mathf.FloorToInt(localX / Mathf.Max(0.5f, _cullingCellSize)), 0, Mathf.Max(0, cellCountX - 1));
            int cellZ = Mathf.Clamp(Mathf.FloorToInt(localZ / Mathf.Max(0.5f, _cullingCellSize)), 0, Mathf.Max(0, cellCountZ - 1));
            int cellKey = (cellZ * cellCountX) + cellX;

            if (!baseBuckets.TryGetValue(cellKey, out List<ClusterSeedBuildData> bucket))
            {
                bucket = new List<ClusterSeedBuildData>();
                baseBuckets.Add(cellKey, bucket);
            }

            bucket.Add(seed);
        }

        List<int> sortedCellKeys = new List<int>(baseBuckets.Keys);
        sortedCellKeys.Sort();

        foreach (int cellKey in sortedCellKeys)
        {
            if (!baseBuckets.TryGetValue(cellKey, out List<ClusterSeedBuildData> seeds) || seeds.Count == 0)
                continue;

            int cellX = cellKey % cellCountX;
            int cellZ = cellKey / cellCountX;
            float minX = cellX * _cullingCellSize;
            float minZ = cellZ * _cullingCellSize;
            Rect cellRect = new Rect(
                minX,
                minZ,
                Mathf.Max(k_MinAdaptiveCellExtent, Mathf.Min(_cullingCellSize, terrainSize.x - minX)),
                Mathf.Max(k_MinAdaptiveCellExtent, Mathf.Min(_cullingCellSize, terrainSize.z - minZ)));
            SplitAdaptiveCellRecursive(cellRect, seeds, result, 0);
        }

        return result;
    }

    private void SplitAdaptiveCellRecursive(Rect localRect, List<ClusterSeedBuildData> seeds, List<AdaptiveCellBuildData> result, int depth)
    {
        int dispatchCount = GetCellDispatchCount(seeds);
        if (!ShouldSplitCell(localRect, seeds.Count, dispatchCount, depth))
        {
            result.Add(new AdaptiveCellBuildData
            {
                localRect = localRect,
                seeds = new List<ClusterSeedBuildData>(seeds),
                dispatchCount = dispatchCount
            });
            return;
        }

        if (TrySplitCellIntoQuadrants(localRect, seeds, out Rect[] childRects, out List<ClusterSeedBuildData>[] childSeeds))
        {
            for (int i = 0; i < childRects.Length; i++)
            {
                if (childSeeds[i] == null || childSeeds[i].Count == 0)
                    continue;

                SplitAdaptiveCellRecursive(childRects[i], childSeeds[i], result, depth + 1);
            }

            return;
        }

        if (TrySplitCellByLongestAxis(localRect, seeds, out Rect childRectA, out List<ClusterSeedBuildData> childSeedsA, out Rect childRectB, out List<ClusterSeedBuildData> childSeedsB))
        {
            SplitAdaptiveCellRecursive(childRectA, childSeedsA, result, depth + 1);
            SplitAdaptiveCellRecursive(childRectB, childSeedsB, result, depth + 1);
            return;
        }

        result.Add(new AdaptiveCellBuildData
        {
            localRect = localRect,
            seeds = new List<ClusterSeedBuildData>(seeds),
            dispatchCount = dispatchCount
        });
    }

    private bool ShouldSplitCell(Rect localRect, int clusterCount, int dispatchCount, int depth)
    {
        if (depth >= k_MaxAdaptiveCellSplitDepth)
            return false;

        if (clusterCount <= 1)
            return false;

        if (clusterCount <= _maxClustersPerCell && dispatchCount <= _maxDispatchesPerCell)
            return false;

        return localRect.width > k_MinAdaptiveCellExtent || localRect.height > k_MinAdaptiveCellExtent;
    }

    private static bool TrySplitCellIntoQuadrants(
        Rect localRect,
        List<ClusterSeedBuildData> seeds,
        out Rect[] childRects,
        out List<ClusterSeedBuildData>[] childSeeds)
    {
        childRects = Array.Empty<Rect>();
        childSeeds = Array.Empty<List<ClusterSeedBuildData>>();

        float halfWidth = localRect.width * 0.5f;
        float halfHeight = localRect.height * 0.5f;
        if (halfWidth <= 0.0f || halfHeight <= 0.0f)
            return false;

        float midX = localRect.xMin + halfWidth;
        float midY = localRect.yMin + halfHeight;

        childRects = new[]
        {
            new Rect(localRect.xMin, localRect.yMin, halfWidth, halfHeight),
            new Rect(midX, localRect.yMin, localRect.xMax - midX, halfHeight),
            new Rect(localRect.xMin, midY, halfWidth, localRect.yMax - midY),
            new Rect(midX, midY, localRect.xMax - midX, localRect.yMax - midY)
        };
        childSeeds = new[]
        {
            new List<ClusterSeedBuildData>(),
            new List<ClusterSeedBuildData>(),
            new List<ClusterSeedBuildData>(),
            new List<ClusterSeedBuildData>()
        };

        for (int i = 0; i < seeds.Count; i++)
        {
            ClusterSeedBuildData seed = seeds[i];
            int childX = seed.centerXZLocal.x >= midX ? 1 : 0;
            int childY = seed.centerXZLocal.y >= midY ? 1 : 0;
            int childIndex = childX + (childY * 2);
            childSeeds[childIndex].Add(seed);
        }

        int nonEmptyChildCount = 0;
        int largestChildCount = 0;
        for (int i = 0; i < childSeeds.Length; i++)
        {
            int childCount = childSeeds[i].Count;
            if (childCount <= 0)
                continue;

            nonEmptyChildCount++;
            largestChildCount = Mathf.Max(largestChildCount, childCount);
        }

        return nonEmptyChildCount > 1 && largestChildCount < seeds.Count;
    }

    private static bool TrySplitCellByLongestAxis(
        Rect localRect,
        List<ClusterSeedBuildData> seeds,
        out Rect childRectA,
        out List<ClusterSeedBuildData> childSeedsA,
        out Rect childRectB,
        out List<ClusterSeedBuildData> childSeedsB)
    {
        childRectA = default;
        childSeedsA = null;
        childRectB = default;
        childSeedsB = null;

        bool splitAlongX = localRect.width >= localRect.height;
        List<ClusterSeedBuildData> sortedSeeds = new List<ClusterSeedBuildData>(seeds);
        sortedSeeds.Sort((a, b) =>
        {
            float aCoord = splitAlongX ? a.centerXZLocal.x : a.centerXZLocal.y;
            float bCoord = splitAlongX ? b.centerXZLocal.x : b.centerXZLocal.y;
            return aCoord.CompareTo(bCoord);
        });

        int rightStartIndex = sortedSeeds.Count / 2;
        if (rightStartIndex <= 0 || rightStartIndex >= sortedSeeds.Count)
            return false;

        float leftCoord = splitAlongX ? sortedSeeds[rightStartIndex - 1].centerXZLocal.x : sortedSeeds[rightStartIndex - 1].centerXZLocal.y;
        float rightCoord = splitAlongX ? sortedSeeds[rightStartIndex].centerXZLocal.x : sortedSeeds[rightStartIndex].centerXZLocal.y;
        float splitCoord = (leftCoord + rightCoord) * 0.5f;

        if (splitAlongX)
        {
            if (splitCoord <= localRect.xMin || splitCoord >= localRect.xMax)
                return false;

            childRectA = new Rect(localRect.xMin, localRect.yMin, splitCoord - localRect.xMin, localRect.height);
            childRectB = new Rect(splitCoord, localRect.yMin, localRect.xMax - splitCoord, localRect.height);
        }
        else
        {
            if (splitCoord <= localRect.yMin || splitCoord >= localRect.yMax)
                return false;

            childRectA = new Rect(localRect.xMin, localRect.yMin, localRect.width, splitCoord - localRect.yMin);
            childRectB = new Rect(localRect.xMin, splitCoord, localRect.width, localRect.yMax - splitCoord);
        }

        childSeedsA = new List<ClusterSeedBuildData>(rightStartIndex);
        childSeedsB = new List<ClusterSeedBuildData>(sortedSeeds.Count - rightStartIndex);

        for (int i = 0; i < sortedSeeds.Count; i++)
        {
            ClusterSeedBuildData seed = sortedSeeds[i];
            float coord = splitAlongX ? seed.centerXZLocal.x : seed.centerXZLocal.y;
            if (coord <= splitCoord)
                childSeedsA.Add(seed);
            else
                childSeedsB.Add(seed);
        }

        return childSeedsA.Count > 0 && childSeedsB.Count > 0;
    }

    private static int GetCellDispatchCount(List<ClusterSeedBuildData> seeds)
    {
        int dispatchCount = 0;
        for (int i = 0; i < seeds.Count; i++)
            dispatchCount += Mathf.Max(0, seeds[i].dispatchGroupCount);

        return dispatchCount;
    }

    private static int GetDispatchGroupCount(int grassCount)
    {
        return Mathf.CeilToInt(Mathf.Max(0, grassCount) / (float)k_KernelThreadSize);
    }

    private List<ClusterInstanceGPU> FlattenAdaptiveCells(
        List<AdaptiveCellBuildData> adaptiveCells,
        List<ClusterCullingData> clusterCullingData,
        out int totalBladeCount,
        out List<ClusterDispatchGPU> dispatches)
    {
        List<ClusterInstanceGPU> clusters = new List<ClusterInstanceGPU>();
        dispatches = new List<ClusterDispatchGPU>();
        uint bladeStartIndex = 0u;

        if (adaptiveCells == null || adaptiveCells.Count == 0)
        {
            totalBladeCount = 0;
            return clusters;
        }

        for (int cellIndex = 0; cellIndex < adaptiveCells.Count; cellIndex++)
        {
            AdaptiveCellBuildData adaptiveCell = adaptiveCells[cellIndex];
            if (adaptiveCell.seeds == null || adaptiveCell.seeds.Count == 0)
                continue;

            int dispatchStartIndex = dispatches.Count;
            Bounds cellBounds = default;
            bool hasCellBounds = false;

            for (int seedIndex = 0; seedIndex < adaptiveCell.seeds.Count; seedIndex++)
            {
                ClusterSeedBuildData seed = adaptiveCell.seeds[seedIndex];
                int clumpTypeIndex = Mathf.Clamp(seed.clumpTypeIndex, 0, Mathf.Max(0, _clumpParameters.Count - 1));
                int grassCount = Mathf.Max(1, seed.grassCount);
                ClusterInstanceGPU cluster = new ClusterInstanceGPU
                {
                    centerWS = seed.centerWS,
                    sigma = Mathf.Max(0.01f, seed.sigma),
                    grassCount = (uint)grassCount,
                    clumpTypeIndex = (uint)clumpTypeIndex,
                    sharedFacingAngle = seed.sharedFacingAngle,
                    startIndex = bladeStartIndex
                };
                int clusterIndex = clusters.Count;
                clusters.Add(cluster);
                bladeStartIndex += (uint)grassCount;

                uint groupCount = (uint)Mathf.Max(0, seed.dispatchGroupCount);
                for (uint groupIndex = 0u; groupIndex < groupCount; groupIndex++)
                {
                    dispatches.Add(new ClusterDispatchGPU
                    {
                        clusterIndex = (uint)clusterIndex,
                        sampleOffset = groupIndex * (uint)k_KernelThreadSize
                    });
                }

                if (!hasCellBounds)
                {
                    cellBounds = seed.boundsWS;
                    hasCellBounds = true;
                    continue;
                }

                cellBounds.Encapsulate(seed.boundsWS.min);
                cellBounds.Encapsulate(seed.boundsWS.max);
            }

            int dispatchCount = dispatches.Count - dispatchStartIndex;
            if (!hasCellBounds || dispatchCount <= 0)
                continue;

            clusterCullingData.Add(new ClusterCullingData
            {
                boundsWS = cellBounds,
                dispatchStartIndex = dispatchStartIndex,
                dispatchCount = dispatchCount
            });
        }

        totalBladeCount = (int)bladeStartIndex;
        return clusters;
    }

    private bool TryGetDistributionMapPixels(out Color32[] pixels, out int width, out int height)
    {
        pixels = null;
        width = 0;
        height = 0;

        if (_distributionMap == null)
            return false;

        width = _distributionMap.width;
        height = _distributionMap.height;
        if (width <= 0 || height <= 0)
            return false;

        try
        {
            pixels = _distributionMap.GetPixels32();
        }
        catch (UnityException)
        {
            return false;
        }

        return pixels != null && pixels.Length == width * height;
    }

    private static float GetDistributionChannelValue01(Color32 pixel, int channelIndex)
    {
        byte channelValue = channelIndex switch
        {
            0 => pixel.r,
            1 => pixel.g,
            2 => pixel.b,
            _ => pixel.a
        };

        return channelValue / 255.0f;
    }

    private float Hash01(int x, int y, int channelIndex, int salt)
    {
        unchecked
        {
            uint hash = (uint)_clusterSeed;
            hash ^= (uint)(x * 73856093);
            hash ^= (uint)(y * 19349663);
            hash ^= (uint)(channelIndex * 83492791);
            hash ^= (uint)salt * 2654435761u;
            hash ^= hash >> 16;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777215.0f;
        }
    }

    private void EnsureDefaultClumpParameters()
    {
        if (_clumpParameters == null)
            _clumpParameters = new List<ClumpParameters>();

        if (_clumpParameters.Count == 0)
            _clumpParameters.Add(ClumpParameters.Default);
    }

    private void SanitizeClumpParameters()
    {
        if (_clumpParameters == null)
            return;

        for (int i = 0; i < _clumpParameters.Count; i++)
        {
            ClumpParameters clump = _clumpParameters[i];
            clump.pullToCentre = Mathf.Clamp01(clump.pullToCentre);
            clump.pointInSameDirection = Mathf.Clamp01(clump.pointInSameDirection);
            clump.clusterRadius = Mathf.Max(0.01f, clump.clusterRadius);
            clump.grassPerCluster = Mathf.Max(1, clump.grassPerCluster);
            _clumpParameters[i] = clump;
        }
    }

    private void UpdateClumpParametersBuffer()
    {
        if (_clumpParametersBuffer == null)
            return;

        ClumpParametersGPU[] clumpData = new ClumpParametersGPU[_clumpParameters.Count];
        for (int i = 0; i < _clumpParameters.Count; i++)
        {
            ClumpParameters clump = _clumpParameters[i];
            clumpData[i] = new ClumpParametersGPU
            {
                pullToCentre = Mathf.Clamp01(clump.pullToCentre),
                pointInSameDirection = Mathf.Clamp01(clump.pointInSameDirection),
                baseHeight = clump.baseHeight,
                heightRandom = clump.heightRandom,
                baseWidth = clump.baseWidth,
                widthRandom = clump.widthRandom,
                baseTilt = clump.baseTilt,
                tiltRandom = clump.tiltRandom,
                baseBend = clump.baseBend,
                bendRandom = clump.bendRandom,
                clusterRadius = Mathf.Max(0.01f, clump.clusterRadius),
                grassPerCluster = Mathf.Max(1, clump.grassPerCluster)
            };
        }

        _clumpParametersBuffer.SetData(clumpData);
    }

    private void GenerateGrassGeometry()
    {
        if (_generatedGrassBladesBuffer == null || _clusterInstancesBuffer == null || _clusterDispatchBuffer == null || _clumpParametersBuffer == null)
            return;

        if (_generatedBladeCount <= 0 || _clusterCount <= 0 || _clusterDispatchCount <= 0)
        {
            _generatedGrassDirty = false;
            ClearVisibleGrassBuffers();
            return;
        }

        TerrainData terrainData = _terrain != null ? _terrain.terrainData : null;
        if (terrainData == null || _heightMap == null)
            return;

        Vector3 terrainPosition = _terrain.transform.position;
        Vector3 terrainSize = terrainData.size;

        _scatterCompute.SetInt(ClusterCountId, _clusterCount);
        _scatterCompute.SetInt(ClusterCullingCountId, _clusterCullingCount);
        _scatterCompute.SetInt(ClusterDispatchCountId, _clusterDispatchCount);
        _scatterCompute.SetInt(ClumpParameterCountId, _clumpParameters.Count);
        _scatterCompute.SetInt(GeneratedGrassBladeCountId, _generatedBladeCount);
        _scatterCompute.SetVector(TerrainPositionId, terrainPosition);
        _scatterCompute.SetVector(TerrainSizeId, terrainSize);
        _scatterCompute.SetTexture(_generateKernel, HeightMapId, _heightMap);
        _scatterCompute.SetFloat(HeightMapMultiplierId, terrainSize.y);
        _scatterCompute.SetBuffer(_generateKernel, ClusterInstancesBufferId, _clusterInstancesBuffer);
        _scatterCompute.SetBuffer(_generateKernel, ClusterDispatchBufferId, _clusterDispatchBuffer);
        _scatterCompute.SetBuffer(_generateKernel, ClumpParametersBufferId, _clumpParametersBuffer);
        _scatterCompute.SetBuffer(_generateKernel, GeneratedGrassBladesId, _generatedGrassBladesBuffer);
        Vector2Int generateDispatchGrid = BuildDispatchGrid(_clusterDispatchCount);
        _scatterCompute.SetInt(DispatchGridWidthId, generateDispatchGrid.x);
        _scatterCompute.Dispatch(_generateKernel, generateDispatchGrid.x, generateDispatchGrid.y, 1);

        _generatedGrassDirty = false;
    }

    private void UpdateVisibleGrass()
    {
        if (_grassBladesBufferLod0 == null || _grassBladesBufferLod1 == null || _grassBladesBufferLod2 == null
            || _argsBufferLod0 == null || _argsBufferLod1 == null || _argsBufferLod2 == null
            || _visibleClusterDispatchBuffer == null || _cullDispatchArgsBuffer == null)
            return;

        ClearVisibleGrassBuffers();

        if (_generatedGrassBladesBuffer == null || _generatedBladeCount <= 0
            || _clusterInstancesBuffer == null || _clusterCullingDataBuffer == null || _clusterDispatchBuffer == null
            || _clusterCount <= 0 || _clusterCullingCount <= 0 || _clusterDispatchCount <= 0)
        {
            ZeroArgsBuffers();
            return;
        }

        ConfigureCullData();
        ConfigureWindData();

        _scatterCompute.SetInt(ClusterCountId, _clusterCount);
        _scatterCompute.SetInt(ClusterCullingCountId, _clusterCullingCount);
        _scatterCompute.SetInt(ClusterDispatchCountId, _clusterDispatchCount);
        _scatterCompute.SetFloat(Lod0EndDistanceId, _lod0EndDistance);
        _scatterCompute.SetFloat(Lod1EndDistanceId, _lod1EndDistance);
        _scatterCompute.SetInt(GeneratedGrassBladeCountId, _generatedBladeCount);

        _visibleClusterDispatchBuffer.SetCounterValue(0);
        _scatterCompute.SetBuffer(_clusterCullKernel, ClusterCullingDataBufferId, _clusterCullingDataBuffer);
        _scatterCompute.SetBuffer(_clusterCullKernel, ClusterDispatchBufferId, _clusterDispatchBuffer);
        _scatterCompute.SetBuffer(_clusterCullKernel, VisibleClusterDispatchAppendBufferId, _visibleClusterDispatchBuffer);
        int clusterCullThreadGroupCount = Mathf.CeilToInt(_clusterCullingCount / (float)k_KernelThreadSize);
        Vector2Int clusterCullDispatchGrid = BuildDispatchGrid(clusterCullThreadGroupCount);
        _scatterCompute.SetInt(DispatchGridWidthId, clusterCullDispatchGrid.x);
        _scatterCompute.Dispatch(_clusterCullKernel, clusterCullDispatchGrid.x, clusterCullDispatchGrid.y, 1);
        ComputeBuffer.CopyCount(_visibleClusterDispatchBuffer, _cullDispatchArgsBuffer, 0);
        _scatterCompute.SetBuffer(_fixupCullDispatchArgsKernel, CullDispatchArgsBufferId, _cullDispatchArgsBuffer);
        _scatterCompute.Dispatch(_fixupCullDispatchArgsKernel, 1, 1, 1);

        _scatterCompute.SetBuffer(_cullKernel, ClusterInstancesBufferId, _clusterInstancesBuffer);
        _scatterCompute.SetBuffer(_cullKernel, GeneratedGrassBladesId, _generatedGrassBladesBuffer);
        _scatterCompute.SetBuffer(_cullKernel, VisibleClusterDispatchBufferId, _visibleClusterDispatchBuffer);
        _scatterCompute.SetBuffer(_cullKernel, CullDispatchArgsBufferId, _cullDispatchArgsBuffer);
        _scatterCompute.SetBuffer(_cullKernel, GrassBladesLod0Id, _grassBladesBufferLod0);
        _scatterCompute.SetBuffer(_cullKernel, GrassBladesLod1Id, _grassBladesBufferLod1);
        _scatterCompute.SetBuffer(_cullKernel, GrassBladesLod2Id, _grassBladesBufferLod2);

        _scatterCompute.DispatchIndirect(_cullKernel, _cullDispatchArgsBuffer, 0);

        UpdateAllArgsBuffers();
    }

    private Bounds BuildClusterBounds(ClusterInstanceGPU cluster, ClumpParameters clumpTemplate, TerrainData terrainData, Vector3 terrainPosition, Vector3 terrainSize)
    {
        float clusterRadius = Mathf.Max(0.01f, cluster.sigma);
        float maxBladeHeight = Mathf.Max(0.05f, clumpTemplate.baseHeight + Mathf.Abs(clumpTemplate.heightRandom));
        float maxBladeWidth = Mathf.Max(0.005f, clumpTemplate.baseWidth + Mathf.Abs(clumpTemplate.widthRandom));
        float maxBladeBend = Mathf.Abs(clumpTemplate.baseBend) + Mathf.Abs(clumpTemplate.bendRandom);

        // Cluster footprint and blade height are already predefined, so bounds should be
        // anchored to the sampled ground range under that footprint instead of the whole terrain height.
        float maxBladeRootRadius = clusterRadius * k_MaxBladeRootRadiusScale;
        float horizontalPadding = Mathf.Max(0.05f, maxBladeHeight + maxBladeWidth + maxBladeBend);
        float horizontalExtent = maxBladeRootRadius + horizontalPadding;
        SampleClusterGroundHeightRange(cluster.centerWS, clusterRadius, terrainData, terrainPosition, terrainSize, out float minGroundHeightWS, out float maxGroundHeightWS);

        float topPadding = Mathf.Max(0.05f, maxBladeWidth);
        Vector3 min = new Vector3(
            cluster.centerWS.x - horizontalExtent,
            minGroundHeightWS,
            cluster.centerWS.z - horizontalExtent);
        Vector3 max = new Vector3(
            cluster.centerWS.x + horizontalExtent,
            maxGroundHeightWS + maxBladeHeight + topPadding,
            cluster.centerWS.z + horizontalExtent);

        Bounds bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    private static void SampleClusterGroundHeightRange(
        Vector3 clusterCenterWS,
        float clusterRadius,
        TerrainData terrainData,
        Vector3 terrainPosition,
        Vector3 terrainSize,
        out float minGroundHeightWS,
        out float maxGroundHeightWS)
    {
        if (terrainData == null)
        {
            minGroundHeightWS = terrainPosition.y;
            maxGroundHeightWS = terrainPosition.y;
            return;
        }

        Vector2[] sampleOffsets =
        {
            Vector2.zero,
            new Vector2(-clusterRadius, 0.0f),
            new Vector2(clusterRadius, 0.0f),
            new Vector2(0.0f, -clusterRadius),
            new Vector2(0.0f, clusterRadius),
            new Vector2(-clusterRadius, -clusterRadius),
            new Vector2(-clusterRadius, clusterRadius),
            new Vector2(clusterRadius, -clusterRadius),
            new Vector2(clusterRadius, clusterRadius)
        };

        minGroundHeightWS = float.PositiveInfinity;
        maxGroundHeightWS = float.NegativeInfinity;

        float safeTerrainWidth = Mathf.Max(terrainSize.x, 0.0001f);
        float safeTerrainLength = Mathf.Max(terrainSize.z, 0.0001f);

        for (int i = 0; i < sampleOffsets.Length; i++)
        {
            Vector2 sampleOffset = sampleOffsets[i];
            float normalizedX = Mathf.Clamp01((clusterCenterWS.x + sampleOffset.x - terrainPosition.x) / safeTerrainWidth);
            float normalizedZ = Mathf.Clamp01((clusterCenterWS.z + sampleOffset.y - terrainPosition.z) / safeTerrainLength);
            float groundHeightWS = terrainPosition.y + terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
            minGroundHeightWS = Mathf.Min(minGroundHeightWS, groundHeightWS);
            maxGroundHeightWS = Mathf.Max(maxGroundHeightWS, groundHeightWS);
        }

        if (float.IsInfinity(minGroundHeightWS) || float.IsInfinity(maxGroundHeightWS))
        {
            minGroundHeightWS = terrainPosition.y;
            maxGroundHeightWS = terrainPosition.y;
        }
    }

    private static Vector2Int BuildDispatchGrid(int groupCount)
    {
        if (groupCount <= 0)
            return Vector2Int.zero;

        int groupsX = Mathf.Min(k_MaxComputeDispatchGroupsPerDimension, groupCount);
        int groupsY = Mathf.CeilToInt(groupCount / (float)groupsX);
        return new Vector2Int(groupsX, Mathf.Max(1, groupsY));
    }

    private void ClearVisibleGrassBuffers()
    {
        _grassBladesBufferLod0?.SetCounterValue(0);
        _grassBladesBufferLod1?.SetCounterValue(0);
        _grassBladesBufferLod2?.SetCounterValue(0);
    }

    private void UpdateAllArgsBuffers()
    {
        UpdateArgsBuffer(_drawMeshLod0, _grassBladesBufferLod0, _argsBufferLod0);
        UpdateArgsBuffer(_drawMeshLod1, _grassBladesBufferLod1, _argsBufferLod1);
        UpdateArgsBuffer(_drawMeshLod2, _grassBladesBufferLod2, _argsBufferLod2);
    }

    private void ZeroArgsBuffers()
    {
        SetArgsBufferInstanceCount(_drawMeshLod0, _argsBufferLod0, 0u);
        SetArgsBufferInstanceCount(_drawMeshLod1, _argsBufferLod1, 0u);
        SetArgsBufferInstanceCount(_drawMeshLod2, _argsBufferLod2, 0u);
    }

    private void ConfigureCullData()
    {
        Camera cullCamera = GetCullCamera();
        bool hasCamera = cullCamera != null;

        _scatterCompute.SetInt(EnableDistanceCullId, hasCamera && _enableDistanceCull ? 1 : 0);
        _scatterCompute.SetInt(EnableFrustumCullId, hasCamera && _enableFrustumCull ? 1 : 0);
        _scatterCompute.SetFloat(DistanceCullStartDistId, _distanceCullStartDist);
        _scatterCompute.SetFloat(DistanceCullEndDistId, _distanceCullEndDist);
        _scatterCompute.SetFloat(DistanceCullMinimumGrassAmountId, _distanceCullMinimumGrassAmount);
        _scatterCompute.SetFloat(FrustumCullNearOffsetId, _frustumCullNearOffset);
        _scatterCompute.SetFloat(FrustumCullEdgeOffsetId, _frustumCullEdgeOffset);
        _scatterCompute.SetVector(WorldSpaceCameraPosId, hasCamera ? cullCamera.transform.position : transform.position);

        if (!hasCamera)
        {
            Array.Clear(_coarseCullFrustumPlanes, 0, _coarseCullFrustumPlanes.Length);
            _scatterCompute.SetVectorArray(FrustumPlanesId, _coarseCullFrustumPlanes);
            _hasCullState = false;
            return;
        }

        Matrix4x4 projection = GL.GetGPUProjectionMatrix(cullCamera.projectionMatrix, false);
        Matrix4x4 view = cullCamera.worldToCameraMatrix;
        Matrix4x4 viewProjection = projection * view;
        _scatterCompute.SetMatrix(VpMatrixId, viewProjection);
        BuildCoarseCullFrustumPlanes(viewProjection, _frustumCullNearOffset, _frustumCullEdgeOffset, _coarseCullFrustumPlanes);
        _scatterCompute.SetVectorArray(FrustumPlanesId, _coarseCullFrustumPlanes);
        CacheCullState(cullCamera, viewProjection);
    }

    private static void BuildCoarseCullFrustumPlanes(Matrix4x4 viewProjection, float nearOffset, float edgeOffset, Vector4[] planes)
    {
        if (planes == null || planes.Length < k_FrustumPlaneCount)
            return;

        Vector4 row0 = new Vector4(viewProjection.m00, viewProjection.m01, viewProjection.m02, viewProjection.m03);
        Vector4 row1 = new Vector4(viewProjection.m10, viewProjection.m11, viewProjection.m12, viewProjection.m13);
        Vector4 row2 = new Vector4(viewProjection.m20, viewProjection.m21, viewProjection.m22, viewProjection.m23);
        Vector4 row3 = new Vector4(viewProjection.m30, viewProjection.m31, viewProjection.m32, viewProjection.m33);

        planes[0] = row3 + row0;
        planes[0].w += edgeOffset;
        planes[1] = row3 - row0;
        planes[1].w += edgeOffset;
        planes[2] = row3 + row1;
        planes[2].w -= nearOffset;
        planes[3] = row3 - row1;
        planes[4] = row2;
        planes[5] = row3 - row2;

        for (int i = 0; i < k_FrustumPlaneCount; i++)
            NormalizePlane(ref planes[i]);
    }

    private static void NormalizePlane(ref Vector4 plane)
    {
        float magnitude = Mathf.Sqrt((plane.x * plane.x) + (plane.y * plane.y) + (plane.z * plane.z));
        if (magnitude <= 0.000001f)
            return;

        plane /= magnitude;
    }

    private void ConfigureWindData()
    {
        Texture2D windTexture = ResolveLocalWindTexture();
        if (windTexture != null)
        {
            windTexture.wrapMode = TextureWrapMode.Repeat;
            windTexture.filterMode = FilterMode.Bilinear;
        }

        Texture2D boundWindTexture = windTexture != null ? windTexture : Texture2D.blackTexture;
        float windStrength = windTexture != null ? _localWindStrength : 0.0f;
        float windRotateAmount = windTexture != null ? _localWindRotateAmount : 0.0f;
        float timeValue = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

        _scatterCompute.SetTexture(_cullKernel, LocalWindTexId, boundWindTexture);
        _scatterCompute.SetFloat(LocalWindScaleId, _localWindScale);
        _scatterCompute.SetFloat(LocalWindSpeedId, _localWindSpeed);
        _scatterCompute.SetFloat(LocalWindStrengthId, windStrength);
        _scatterCompute.SetFloat(LocalWindRotateAmountId, windRotateAmount);
        _scatterCompute.SetFloat(TimeId, timeValue);
    }

    private Texture2D ResolveLocalWindTexture()
    {
        if (_localWindGenerator == null)
        {
            _localWindGenerator = FindFirstObjectByType<SeamlessNoiseGenerator>();
        }

        if (_localWindGenerator == null)
            return null;

        if (_localWindGenerator.generatedTexture == null)
        {
            _localWindGenerator.GenerateNoiseTexture();
        }

        return _localWindGenerator.generatedTexture;
    }

    private bool HasCullStateChanged()
    {
        Camera cullCamera = GetCullCamera();
        if (cullCamera == null)
        {
            bool hadCullState = _hasCullState;
            _hasCullState = false;
            _lastCullCameraInstanceId = -1;
            return hadCullState;
        }

        Matrix4x4 projection = GL.GetGPUProjectionMatrix(cullCamera.projectionMatrix, false);
        Matrix4x4 viewProjection = projection * cullCamera.worldToCameraMatrix;
        int instanceId = cullCamera.GetInstanceID();

        if (!_hasCullState || instanceId != _lastCullCameraInstanceId)
            return true;

        return !Approximately(_lastCullCameraPosition, cullCamera.transform.position)
            || !Approximately(_lastCullCameraRotation, cullCamera.transform.rotation)
            || !Approximately(_lastCullViewProjectionMatrix, viewProjection);
    }

    private Camera GetCullCamera()
    {
        if (_activeCullCamera != null)
            return _activeCullCamera;

        if (Camera.main != null)
            return Camera.main;

#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
            return SceneView.lastActiveSceneView.camera;
#endif

        return Camera.current;
    }

    private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null)
            return;

        if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            return;

        _activeCullCamera = camera;
    }

    private void CacheCullState(Camera cullCamera, Matrix4x4 viewProjection)
    {
        _lastCullCameraInstanceId = cullCamera.GetInstanceID();
        _lastCullCameraPosition = cullCamera.transform.position;
        _lastCullCameraRotation = cullCamera.transform.rotation;
        _lastCullViewProjectionMatrix = viewProjection;
        _hasCullState = true;
    }

    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude < 0.0001f;
    }

    private static bool Approximately(Quaternion a, Quaternion b)
    {
        return 1.0f - Mathf.Abs(Quaternion.Dot(a, b)) < 0.0001f;
    }

    private static bool Approximately(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++)
        {
            if (Mathf.Abs(a[i] - b[i]) > 0.0001f)
                return false;
        }

        return true;
    }

    private void UpdateArgsBuffer(Mesh mesh, ComputeBuffer grassBladesBuffer, ComputeBuffer argsBuffer)
    {
        if (argsBuffer == null)
            return;

        SetArgsBufferInstanceCount(mesh, argsBuffer, 0u);

        if (grassBladesBuffer != null)
            ComputeBuffer.CopyCount(grassBladesBuffer, argsBuffer, sizeof(uint));
    }

    private void SetArgsBufferInstanceCount(Mesh mesh, ComputeBuffer argsBuffer, uint instanceCount)
    {
        if (argsBuffer == null)
            return;

        uint[] args =
        {
            mesh != null ? mesh.GetIndexCount(0) : 0u,
            instanceCount,
            mesh != null ? mesh.GetIndexStart(0) : 0u,
            mesh != null ? (uint)mesh.GetBaseVertex(0) : 0u,
            0u
        };

        argsBuffer.SetData(args);
    }

    private void DrawGrass()
    {
        Material grassMaterialLod0 = ResolveGrassMaterial(0);
        Material grassMaterialLod1 = ResolveGrassMaterial(1);
        Material grassMaterialLod2 = ResolveGrassMaterial(2);
        if (grassMaterialLod0 == null || grassMaterialLod1 == null || grassMaterialLod2 == null)
            return;

        Bounds bounds = BuildRenderBounds();

        DrawGrassLod(_drawMeshLod0, _grassBladesBufferLod0, _argsBufferLod0, bounds, grassMaterialLod0, grassMaterialLod0, ShadowCastingMode.On, true);
        DrawGrassLod(_drawMeshLod1, _grassBladesBufferLod1, _argsBufferLod1, bounds, grassMaterialLod1, grassMaterialLod0, ShadowCastingMode.Off, false);
        DrawGrassLod(_drawMeshLod2, _grassBladesBufferLod2, _argsBufferLod2, bounds, grassMaterialLod2, grassMaterialLod0, ShadowCastingMode.Off, false);
    }

    private Material ResolveGrassMaterial(int lodIndex)
    {
        return lodIndex switch
        {
            1 => _grassMaterialLod1 != null ? _grassMaterialLod1 : _grassMaterial,
            2 => _grassMaterialLod2 != null ? _grassMaterialLod2 : (_grassMaterialLod1 != null ? _grassMaterialLod1 : _grassMaterial),
            _ => _grassMaterial
        };
    }

    private void DrawGrassLod(
        Mesh mesh,
        ComputeBuffer grassBladesBuffer,
        ComputeBuffer argsBuffer,
        Bounds bounds,
        Material material,
        Material poseSourceMaterial,
        ShadowCastingMode shadowCastingMode,
        bool receiveShadows)
    {
        if (!PrepareGrassLodDraw(mesh, grassBladesBuffer, argsBuffer, material, poseSourceMaterial))
            return;

        DrawGrassLodIndirectNow(mesh, material, bounds, argsBuffer, shadowCastingMode, receiveShadows);
    }

    private void DrawGrassLodIndirectNow(
        Mesh mesh,
        Material material,
        Bounds bounds,
        ComputeBuffer argsBuffer,
        ShadowCastingMode shadowCastingMode,
        bool receiveShadows)
    {
        Graphics.DrawMeshInstancedIndirect(
            mesh,
            0,
            material,
            bounds,
            argsBuffer,
            0,
            _propertyBlock,
            shadowCastingMode,
            receiveShadows,
            gameObject.layer);
    }

    public void RecordGpuSemanticDraws(RasterCommandBuffer commandBuffer, Camera camera, GpuSemanticDrawPhase phase)
    {
        if (phase != GpuSemanticDrawPhase.Opaque ||
            commandBuffer == null ||
            !ShouldUseGpuSemanticDrawPass(camera))
        {
            return;
        }

        RecordGrassSemanticOpaqueDraws(commandBuffer);
    }

    public void CollectGpuSemanticDrawCommands(List<GpuSemanticDrawCommand> commands, Camera camera, GpuSemanticDrawPhase phase)
    {
        if (commands == null ||
            phase != GpuSemanticDrawPhase.Opaque ||
            !ShouldUseGpuSemanticDrawPass(camera))
        {
            return;
        }

        commands.Add(new GpuSemanticDrawCommand(GrassRenderLod0Marker, RecordGrassLod0SemanticDraw));
        commands.Add(new GpuSemanticDrawCommand(GrassRenderLod1Marker, RecordGrassLod1SemanticDraw));
        commands.Add(new GpuSemanticDrawCommand(GrassRenderLod2Marker, RecordGrassLod2SemanticDraw));
    }

    private void RecordGrassSemanticOpaqueDraws(RasterCommandBuffer commandBuffer)
    {
        Material grassMaterialLod0 = ResolveGrassMaterial(0);
        Material grassMaterialLod1 = ResolveGrassMaterial(1);
        Material grassMaterialLod2 = ResolveGrassMaterial(2);
        if (grassMaterialLod0 == null || grassMaterialLod1 == null || grassMaterialLod2 == null)
            return;

        RecordGrassLodSemanticDraw(commandBuffer, GrassRenderLod0Marker, _drawMeshLod0, _grassBladesBufferLod0, _argsBufferLod0, grassMaterialLod0, grassMaterialLod0, true);
        RecordGrassLodSemanticDraw(commandBuffer, GrassRenderLod1Marker, _drawMeshLod1, _grassBladesBufferLod1, _argsBufferLod1, grassMaterialLod1, grassMaterialLod0, true);
        RecordGrassLodSemanticDraw(commandBuffer, GrassRenderLod2Marker, _drawMeshLod2, _grassBladesBufferLod2, _argsBufferLod2, grassMaterialLod2, grassMaterialLod0, true);
    }

    private void RecordGrassLod0SemanticDraw(RasterCommandBuffer commandBuffer, Camera camera)
    {
        RecordGrassLodSemanticDraw(
            commandBuffer,
            GrassRenderLod0Marker,
            _drawMeshLod0,
            _grassBladesBufferLod0,
            _argsBufferLod0,
            ResolveGrassMaterial(0),
            ResolveGrassMaterial(0),
            false);
    }

    private void RecordGrassLod1SemanticDraw(RasterCommandBuffer commandBuffer, Camera camera)
    {
        RecordGrassLodSemanticDraw(
            commandBuffer,
            GrassRenderLod1Marker,
            _drawMeshLod1,
            _grassBladesBufferLod1,
            _argsBufferLod1,
            ResolveGrassMaterial(1),
            ResolveGrassMaterial(0),
            false);
    }

    private void RecordGrassLod2SemanticDraw(RasterCommandBuffer commandBuffer, Camera camera)
    {
        RecordGrassLodSemanticDraw(
            commandBuffer,
            GrassRenderLod2Marker,
            _drawMeshLod2,
            _grassBladesBufferLod2,
            _argsBufferLod2,
            ResolveGrassMaterial(2),
            ResolveGrassMaterial(0),
            false);
    }

    private void RecordGrassLodSemanticDraw(
        RasterCommandBuffer commandBuffer,
        string markerLabel,
        Mesh mesh,
        ComputeBuffer grassBladesBuffer,
        ComputeBuffer argsBuffer,
        Material material,
        Material poseSourceMaterial,
        bool wrapMarker)
    {
        if (string.IsNullOrEmpty(markerLabel) ||
            !PrepareGrassLodDraw(mesh, grassBladesBuffer, argsBuffer, material, poseSourceMaterial))
        {
            return;
        }

        int forwardPassIndex = GpuSemanticDrawPassUtility.ResolveMaterialPassIndex(material, GrassRenderShaderEntry);
        if (wrapMarker)
            commandBuffer.BeginSample(markerLabel);

        commandBuffer.DrawMeshInstancedIndirect(mesh, 0, material, forwardPassIndex, argsBuffer, 0, _propertyBlock);

        if (wrapMarker)
            commandBuffer.EndSample(markerLabel);
    }

    private bool PrepareGrassLodDraw(
        Mesh mesh,
        ComputeBuffer grassBladesBuffer,
        ComputeBuffer argsBuffer,
        Material material,
        Material poseSourceMaterial)
    {
        if (mesh == null || grassBladesBuffer == null || argsBuffer == null || material == null)
            return false;

        _propertyBlock.Clear();
        _propertyBlock.SetBuffer(GrassBladesId, grassBladesBuffer);
        _propertyBlock.SetBuffer(GeneratedGrassBladesId, _generatedGrassBladesBuffer);
        ApplySharedLodPoseProperties(poseSourceMaterial, material);
        material.enableInstancing = true;
        return true;
    }

    private bool ShouldUseGpuSemanticDrawPass()
    {
        return GpuSemanticDrawFeature.IsInstalled &&
            GpuPassDebugRuntime.CaptureMetadataEnabled;
    }

    private bool ShouldUseGpuSemanticDrawPass(Camera camera)
    {
        return ShouldUseGpuSemanticDrawPass() &&
            camera != null &&
            camera.cameraType != CameraType.Preview &&
            camera.cameraType != CameraType.Reflection;
    }

    private void ApplySharedLodPoseProperties(Material sourceMaterial, Material drawMaterial)
    {
        if (sourceMaterial == null || drawMaterial == null || ReferenceEquals(sourceMaterial, drawMaterial))
            return;

        foreach (int propertyId in k_SharedLodPosePropertyIds)
        {
            if (sourceMaterial.HasProperty(propertyId) && drawMaterial.HasProperty(propertyId))
                _propertyBlock.SetFloat(propertyId, sourceMaterial.GetFloat(propertyId));
        }
    }

    private Bounds BuildRenderBounds()
    {
        if (_terrain != null && _terrain.terrainData != null)
        {
            Vector3 terrainSize = _terrain.terrainData.size;
            float height = Mathf.Max(_boundsHeight, terrainSize.y + _boundsHeight);
            Vector3 center = _terrain.transform.position + new Vector3(terrainSize.x * 0.5f, height * 0.5f, terrainSize.z * 0.5f);
            return new Bounds(center, new Vector3(terrainSize.x, height, terrainSize.z));
        }

        return new Bounds(transform.position, new Vector3(1.0f, _boundsHeight, 1.0f));
    }

    private void ReleaseResources()
    {
        _grassBladesBufferLod0?.Release();
        _grassBladesBufferLod0 = null;
        _grassBladesBufferLod1?.Release();
        _grassBladesBufferLod1 = null;
        _grassBladesBufferLod2?.Release();
        _grassBladesBufferLod2 = null;
        _generatedGrassBladesBuffer?.Release();
        _generatedGrassBladesBuffer = null;

        _clusterInstancesBuffer?.Release();
        _clusterInstancesBuffer = null;
        _clusterCullingDataBuffer?.Release();
        _clusterCullingDataBuffer = null;
        _clusterDispatchBuffer?.Release();
        _clusterDispatchBuffer = null;
        _visibleClusterDispatchBuffer?.Release();
        _visibleClusterDispatchBuffer = null;

        _clumpParametersBuffer?.Release();
        _clumpParametersBuffer = null;

        _cullDispatchArgsBuffer?.Release();
        _cullDispatchArgsBuffer = null;
        _argsBufferLod0?.Release();
        _argsBufferLod0 = null;
        _argsBufferLod1?.Release();
        _argsBufferLod1 = null;
        _argsBufferLod2?.Release();
        _argsBufferLod2 = null;

        DestroyRuntimeMesh(ref _runtimeMeshLod0);
        DestroyRuntimeMesh(ref _runtimeMeshLod1);
        DestroyRuntimeMesh(ref _runtimeMeshLod2);
        _drawMeshLod0 = null;
        _drawMeshLod1 = null;
        _drawMeshLod2 = null;
        _hasCullState = false;
    }

    private void DestroyRuntimeMesh(ref Mesh runtimeMesh)
    {
        if (runtimeMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeMesh);
        else
            DestroyImmediate(runtimeMesh);

        runtimeMesh = null;
    }
}
