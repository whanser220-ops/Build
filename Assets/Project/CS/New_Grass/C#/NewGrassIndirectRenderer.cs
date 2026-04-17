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
public class NewGrassIndirectRenderer : MonoBehaviour
{
    private const int k_KernelThreadSize = 64;
    private const int k_ArgsCount = 5;
    private const int k_GrassBladeStride = 14 * sizeof(float);

    private static readonly int GrassBladesId = Shader.PropertyToID("_GrassBlades");
    private static readonly int GrassBladesLod0Id = Shader.PropertyToID("_GrassBladesLod0");
    private static readonly int GrassBladesLod1Id = Shader.PropertyToID("_GrassBladesLod1");
    private static readonly int GrassBladesLod2Id = Shader.PropertyToID("_GrassBladesLod2");
    private static readonly int ClusterInstancesBufferId = Shader.PropertyToID("_ClusterInstancesBuffer");
    private static readonly int ClumpParametersBufferId = Shader.PropertyToID("_ClumpParametersBuffer");
    private static readonly int ClusterCountId = Shader.PropertyToID("_ClusterCount");
    private static readonly int MaxGrassPerClusterId = Shader.PropertyToID("_MaxGrassPerCluster");
    private static readonly int ClumpParameterCountId = Shader.PropertyToID("_ClumpParameterCount");
    private static readonly int TerrainPositionId = Shader.PropertyToID("_TerrainPosition");
    private static readonly int TerrainSizeId = Shader.PropertyToID("_TerrainSize");
    private static readonly int HeightMapId = Shader.PropertyToID("_HeightMap");
    private static readonly int HeightMapMultiplierId = Shader.PropertyToID("_HeightMapMultiplier");
    private static readonly int WorldSpaceCameraPosId = Shader.PropertyToID("_WSpaceCameraPos");
    private static readonly int DistanceCullStartDistId = Shader.PropertyToID("_DistanceCullStartDist");
    private static readonly int DistanceCullEndDistId = Shader.PropertyToID("_DistanceCullEndDist");
    private static readonly int DistanceCullMinimumGrassAmountId = Shader.PropertyToID("_DistanceCullMinimumGrassAmount");
    private static readonly int VpMatrixId = Shader.PropertyToID("_VP_MATRIX");
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
    private static readonly int TimeId = Shader.PropertyToID("_Time");

    [Header("Resources")]
    [Tooltip("负责生成草叶实例数据的 Compute Shader。")]
    [SerializeField] private ComputeShader _scatterCompute;
    [Tooltip("用于最终渲染草叶的材质。")]
    [SerializeField] private Material _grassMaterial;
    [Tooltip("单株草叶使用的基础网格。为空时会自动生成默认草叶网格。")]
    [FormerlySerializedAs("_grassMesh")]
    [SerializeField] private Mesh _grassMeshLod0;
    [SerializeField] private Mesh _grassMeshLod1;
    [SerializeField] private Mesh _grassMeshLod2;

    [Header("Rendering")]
    [Tooltip("用于包围整片草地的渲染包围盒高度。过小会导致剔除过早，过大则增加无效渲染范围。")]
    [SerializeField] [Min(0.1f)] private float _boundsHeight = 8.0f;
    [Header("LOD")]
    [SerializeField] [Min(0.0f)] private float _lod0EndDistance = 14.0f;
    [SerializeField] [Min(0.0f)] private float _lod1EndDistance = 28.0f;
    [Tooltip("是否每帧重新生成草叶实例数据。开启后便于实时预览参数，但会增加运行开销。")]
    [SerializeField] private bool _regenerateEveryFrame = true;

    [Header("Pre-Clustered Generation")]
    [Tooltip("草簇中心之间的最小间距。数值越大，草簇越稀疏。")]
    [SerializeField] [Min(0.1f)] private float _clusterMinDistance = 8.0f;
    [Tooltip("同一种草簇半径的随机波动幅度。0 表示所有簇大小一致。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _clusterRadiusVariance = 0.4f;
    [Tooltip("泊松盘采样为每个活动点尝试生成新簇中心的次数。更高更稳定，但生成更慢。")]
    [SerializeField] [Min(1)] private int _poissonMaxAttempts = 30;
    [Tooltip("控制整片草簇分布的随机种子。修改后会改变草簇中心布局。")]
    [SerializeField] private int _clusterSeed = 12345;
    [Tooltip("可选的草簇类型模板列表。每种模板会决定该簇中的草高、宽度、弯曲和簇半径等形态。")]
    [SerializeField] private List<ClumpParameters> _clumpParameters = new List<ClumpParameters> { ClumpParameters.Default };

    [Header("Culling")]
    [Tooltip("是否启用基于相机距离的草叶剔除。远处会逐渐减少草量。")]
    [SerializeField] private bool _enableDistanceCull = true;
    [Tooltip("距离剔除开始生效的距离。小于该距离时草量保持完整。")]
    [SerializeField] [Min(0.0f)] private float _distanceCullStartDist = 40.0f;
    [Tooltip("距离剔除完成的距离。超过该距离后只保留最小草量。")]
    [SerializeField] [Min(0.0f)] private float _distanceCullEndDist = 100.0f;
    [Tooltip("距离剔除结束后仍然保留的最小草量比例。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _distanceCullMinimumGrassAmount = 0.2f;
    [Tooltip("是否启用视锥剔除，仅保留当前相机视野内的草叶。")]
    [SerializeField] private bool _enableFrustumCull = true;
    [Tooltip("视锥近裁剪面的额外偏移，用于减轻镜头前方草叶的边缘闪烁。")]
    [SerializeField] private float _frustumCullNearOffset = 0.0f;
    [Tooltip("视锥左右上下边缘的额外扩展量。适当增加可减少边缘 popping。")]
    [SerializeField] private float _frustumCullEdgeOffset = 0.1f;

    [Header("Wind")]
    [Tooltip("局部风对草叶弯曲和高光响应的整体强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _localWindStrength = 0.5f;
    [Tooltip("风纹理在世界空间中的缩放。数值越小，风区块越大。")]
    [SerializeField] private float _localWindScale = 0.01f;
    [Tooltip("风纹理滚动的速度。")]
    [SerializeField] private float _localWindSpeed = 0.1f;
    [Tooltip("风对草叶朝向旋转的影响强度。")]
    [SerializeField] [Range(0.0f, 1.0f)] private float _localWindRotateAmount = 0.3f;

    private ComputeBuffer _grassBladesBufferLod0;
    private ComputeBuffer _grassBladesBufferLod1;
    private ComputeBuffer _grassBladesBufferLod2;
    private ComputeBuffer _clusterInstancesBuffer;
    private ComputeBuffer _clumpParametersBuffer;
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
    private int _kernel = -1;
    private bool _buffersDirty = true;
    private bool _clusterDataDirty = true;
    private int _clusterCount;
    private int _maxGrassPerCluster;

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
        public float padding;
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        _buffersDirty = true;
        _clusterDataDirty = true;
        EnsureResources();
        RebuildGrass();
    }

    private void OnValidate()
    {
        _boundsHeight = Mathf.Max(0.1f, _boundsHeight);
        _lod0EndDistance = Mathf.Max(0.0f, _lod0EndDistance);
        _lod1EndDistance = Mathf.Max(_lod0EndDistance, _lod1EndDistance);
        _clusterMinDistance = Mathf.Max(0.1f, _clusterMinDistance);
        _clusterRadiusVariance = Mathf.Clamp01(_clusterRadiusVariance);
        _poissonMaxAttempts = Mathf.Max(1, _poissonMaxAttempts);
        _distanceCullStartDist = Mathf.Max(0.0f, _distanceCullStartDist);
        _distanceCullEndDist = Mathf.Max(_distanceCullStartDist, _distanceCullEndDist);
        _distanceCullMinimumGrassAmount = Mathf.Clamp01(_distanceCullMinimumGrassAmount);
        _localWindStrength = Mathf.Clamp01(_localWindStrength);
        _localWindScale = Mathf.Max(0.0f, _localWindScale);
        _localWindSpeed = Mathf.Max(0.0f, _localWindSpeed);
        _localWindRotateAmount = Mathf.Clamp01(_localWindRotateAmount);
        EnsureDefaultClumpParameters();
        SanitizeClumpParameters();
        _buffersDirty = true;
        _clusterDataDirty = true;

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

        if (_regenerateEveryFrame || _buffersDirty || HasCullStateChanged())
            RebuildGrass();

        DrawGrass();
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        ReleaseResources();
    }

    [ContextMenu("Rebuild Grass")]
    public void RebuildGrass()
    {
        if (!EnsureResources())
            return;

        _grassBladesBufferLod0.SetCounterValue(0);
        _grassBladesBufferLod1.SetCounterValue(0);
        _grassBladesBufferLod2.SetCounterValue(0);
        _scatterCompute.SetBuffer(_kernel, GrassBladesLod0Id, _grassBladesBufferLod0);
        _scatterCompute.SetBuffer(_kernel, GrassBladesLod1Id, _grassBladesBufferLod1);
        _scatterCompute.SetBuffer(_kernel, GrassBladesLod2Id, _grassBladesBufferLod2);
        _scatterCompute.SetBuffer(_kernel, ClusterInstancesBufferId, _clusterInstancesBuffer);
        _scatterCompute.SetBuffer(_kernel, ClumpParametersBufferId, _clumpParametersBuffer);
        _scatterCompute.SetInt(ClusterCountId, _clusterCount);
        _scatterCompute.SetInt(MaxGrassPerClusterId, _maxGrassPerCluster);
        _scatterCompute.SetInt(ClumpParameterCountId, _clumpParameters.Count);
        _scatterCompute.SetFloat(Lod0EndDistanceId, _lod0EndDistance);
        _scatterCompute.SetFloat(Lod1EndDistanceId, _lod1EndDistance);
        _scatterCompute.SetVector(TerrainPositionId, _terrain.transform.position);
        _scatterCompute.SetVector(TerrainSizeId, _terrain.terrainData.size);
        _scatterCompute.SetTexture(_kernel, HeightMapId, _heightMap);
        _scatterCompute.SetFloat(HeightMapMultiplierId, _terrain.terrainData.size.y);
        ConfigureWindData();
        ConfigureCullData();

        int totalBladeCapacity = Mathf.Max(1, _clusterCount * _maxGrassPerCluster);
        int groupCount = Mathf.CeilToInt(totalBladeCapacity / (float)k_KernelThreadSize);
        _scatterCompute.Dispatch(_kernel, groupCount, 1, 1);

        UpdateArgsBuffer(_drawMeshLod0, _grassBladesBufferLod0, _argsBufferLod0);
        UpdateArgsBuffer(_drawMeshLod1, _grassBladesBufferLod1, _argsBufferLod1);
        UpdateArgsBuffer(_drawMeshLod2, _grassBladesBufferLod2, _argsBufferLod2);
        _buffersDirty = false;
    }

    private bool EnsureResources()
    {
        EnsureDefaultClumpParameters();
        SanitizeClumpParameters();

        if (_scatterCompute == null || _grassMaterial == null)
            return false;

        if (_kernel < 0)
            _kernel = _scatterCompute.FindKernel("Main");

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
            _buffersDirty = true;
        }

        EnsureClusterData();

        int bladeCapacity = Mathf.Max(1, _clusterCount * _maxGrassPerCluster);
        EnsureGrassBladeBuffer(ref _grassBladesBufferLod0, bladeCapacity);
        EnsureGrassBladeBuffer(ref _grassBladesBufferLod1, bladeCapacity);
        EnsureGrassBladeBuffer(ref _grassBladesBufferLod2, bladeCapacity);
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
        buffer = new ComputeBuffer(bladeCapacity, k_GrassBladeStride, ComputeBufferType.Append);
        _buffersDirty = true;
    }

    private void EnsureArgsBuffer(ref ComputeBuffer buffer)
    {
        if (buffer != null)
            return;

        buffer = new ComputeBuffer(1, k_ArgsCount * sizeof(uint), ComputeBufferType.IndirectArguments);
        _buffersDirty = true;
    }

    private void EnsureClusterData()
    {
        if (!_clusterDataDirty && _clusterInstancesBuffer != null && _clumpParametersBuffer != null)
            return;

        List<ClusterInstanceGPU> clusters = GenerateClusterInstances();
        _clusterCount = clusters.Count;
        _maxGrassPerCluster = ComputeMaxGrassPerCluster();

        int clusterBufferCount = Mathf.Max(1, _clusterCount);
        int clusterStride = Marshal.SizeOf<ClusterInstanceGPU>();
        if (_clusterInstancesBuffer == null || _clusterInstancesBuffer.count != clusterBufferCount)
        {
            _clusterInstancesBuffer?.Release();
            _clusterInstancesBuffer = new ComputeBuffer(clusterBufferCount, clusterStride);
        }

        ClusterInstanceGPU[] clusterArray = _clusterCount > 0 ? clusters.ToArray() : new ClusterInstanceGPU[1];
        _clusterInstancesBuffer.SetData(clusterArray);

        int clumpCount = Mathf.Max(1, _clumpParameters.Count);
        int clumpStride = Marshal.SizeOf<ClumpParametersGPU>();
        if (_clumpParametersBuffer == null || _clumpParametersBuffer.count != clumpCount)
        {
            _clumpParametersBuffer?.Release();
            _clumpParametersBuffer = new ComputeBuffer(clumpCount, clumpStride);
        }

        UpdateClumpParametersBuffer();
        _clusterDataDirty = false;
        _buffersDirty = true;
    }

    private List<ClusterInstanceGPU> GenerateClusterInstances()
    {
        float terrainWidth = _terrain.terrainData.size.x;
        float terrainLength = _terrain.terrainData.size.z;
        List<Vector2> clusterCenters = GeneratePoissonPoints(terrainWidth, terrainLength, _clusterMinDistance, _poissonMaxAttempts, _clusterSeed);
        List<ClusterInstanceGPU> clusters = new List<ClusterInstanceGPU>(clusterCenters.Count);
        System.Random random = new System.Random(_clusterSeed);
        int clumpTypeCount = Mathf.Max(1, _clumpParameters.Count);
        int maxGrassPerCluster = ComputeMaxGrassPerCluster();

        for (int i = 0; i < clusterCenters.Count; i++)
        {
            Vector2 localCenter = clusterCenters[i];
            int clumpTypeIndex = random.Next(clumpTypeCount);
            ClumpParameters clumpTemplate = _clumpParameters[clumpTypeIndex];

            float baseClusterRadius = Mathf.Max(0.01f, clumpTemplate.clusterRadius);
            int baseGrassCount = Mathf.Max(1, clumpTemplate.grassPerCluster);

            float sigma = Mathf.Max(0.01f, baseClusterRadius * (1.0f + ((float)random.NextDouble() - 0.5f) * _clusterRadiusVariance));
            int grassCount = Mathf.Clamp(
                Mathf.FloorToInt(baseGrassCount * (0.6f + (float)random.NextDouble() * 0.8f)),
                1,
                maxGrassPerCluster);

            clusters.Add(new ClusterInstanceGPU
            {
                centerWS = new Vector3(
                    _terrain.transform.position.x + localCenter.x,
                    _terrain.transform.position.y,
                    _terrain.transform.position.z + localCenter.y),
                sigma = sigma,
                grassCount = (uint)grassCount,
                clumpTypeIndex = (uint)clumpTypeIndex,
                sharedFacingAngle = (float)random.NextDouble() * Mathf.PI * 2.0f,
                padding = 0.0f
            });
        }

        return clusters;
    }

    private List<Vector2> GeneratePoissonPoints(float width, float height, float minDistance, int maxAttempts, int seed)
    {
        float cellSize = minDistance / Mathf.Sqrt(2.0f);
        int gridWidth = Mathf.Max(1, Mathf.CeilToInt(width / cellSize));
        int gridHeight = Mathf.Max(1, Mathf.CeilToInt(height / cellSize));
        int[,] grid = new int[gridWidth, gridHeight];

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
                grid[x, y] = -1;
        }

        List<Vector2> points = new List<Vector2>();
        List<Vector2> active = new List<Vector2>();
        System.Random random = new System.Random(seed);
        Vector2 startPoint = new Vector2(width * 0.5f, height * 0.5f);
        RegisterPoissonPoint(startPoint, cellSize, grid, points, active);

        while (active.Count > 0)
        {
            int activeIndex = random.Next(active.Count);
            Vector2 point = active[activeIndex];
            bool found = false;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2.0f;
                float radius = minDistance * (1.0f + (float)random.NextDouble());
                Vector2 candidate = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                if (candidate.x < 0.0f || candidate.x >= width || candidate.y < 0.0f || candidate.y >= height)
                    continue;

                if (!IsPoissonCandidateValid(candidate, minDistance, cellSize, grid, points))
                    continue;

                RegisterPoissonPoint(candidate, cellSize, grid, points, active);
                found = true;
                break;
            }

            if (!found)
                active.RemoveAt(activeIndex);
        }

        return points;
    }

    private static bool IsPoissonCandidateValid(Vector2 candidate, float minDistance, float cellSize, int[,] grid, List<Vector2> points)
    {
        int gridWidth = grid.GetLength(0);
        int gridHeight = grid.GetLength(1);
        int cellX = Mathf.Clamp(Mathf.FloorToInt(candidate.x / cellSize), 0, gridWidth - 1);
        int cellY = Mathf.Clamp(Mathf.FloorToInt(candidate.y / cellSize), 0, gridHeight - 1);

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                int neighborX = cellX + dx;
                int neighborY = cellY + dy;
                if (neighborX < 0 || neighborX >= gridWidth || neighborY < 0 || neighborY >= gridHeight)
                    continue;

                int pointIndex = grid[neighborX, neighborY];
                if (pointIndex < 0)
                    continue;

                if ((candidate - points[pointIndex]).sqrMagnitude < minDistance * minDistance)
                    return false;
            }
        }

        return true;
    }

    private static void RegisterPoissonPoint(Vector2 point, float cellSize, int[,] grid, List<Vector2> points, List<Vector2> active)
    {
        int pointIndex = points.Count;
        points.Add(point);
        active.Add(point);

        int gridX = Mathf.Clamp(Mathf.FloorToInt(point.x / cellSize), 0, grid.GetLength(0) - 1);
        int gridY = Mathf.Clamp(Mathf.FloorToInt(point.y / cellSize), 0, grid.GetLength(1) - 1);
        grid[gridX, gridY] = pointIndex;
    }

    private int ComputeMaxGrassPerCluster()
    {
        int maxGrassCount = 1;
        for (int i = 0; i < _clumpParameters.Count; i++)
            maxGrassCount = Mathf.Max(maxGrassCount, Mathf.Max(1, _clumpParameters[i].grassPerCluster));

        return Mathf.Max(1, Mathf.CeilToInt(maxGrassCount * 1.4f));
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
            _hasCullState = false;
            return;
        }

        Matrix4x4 projection = GL.GetGPUProjectionMatrix(cullCamera.projectionMatrix, false);
        Matrix4x4 view = cullCamera.worldToCameraMatrix;
        Matrix4x4 viewProjection = projection * view;
        _scatterCompute.SetMatrix(VpMatrixId, viewProjection);
        CacheCullState(cullCamera, viewProjection);
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

        _scatterCompute.SetTexture(_kernel, LocalWindTexId, boundWindTexture);
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
            _hasCullState = false;
            return false;
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
        uint[] args =
        {
            mesh != null ? mesh.GetIndexCount(0) : 0u,
            0u,
            mesh != null ? mesh.GetIndexStart(0) : 0u,
            mesh != null ? (uint)mesh.GetBaseVertex(0) : 0u,
            0u
        };

        argsBuffer.SetData(args);
        ComputeBuffer.CopyCount(grassBladesBuffer, argsBuffer, sizeof(uint));
    }

    private void DrawGrass()
    {
        if (_grassMaterial == null)
            return;

        Texture2D windTexture = ResolveLocalWindTexture();
        Texture boundWindTexture = windTexture != null ? windTexture : Texture2D.blackTexture;
        float windStrength = windTexture != null ? _localWindStrength : 0.0f;
        Bounds bounds = BuildRenderBounds();

        DrawGrassLod(_drawMeshLod0, _grassBladesBufferLod0, _argsBufferLod0, bounds, boundWindTexture, windStrength);
        DrawGrassLod(_drawMeshLod1, _grassBladesBufferLod1, _argsBufferLod1, bounds, boundWindTexture, windStrength);
        DrawGrassLod(_drawMeshLod2, _grassBladesBufferLod2, _argsBufferLod2, bounds, boundWindTexture, windStrength);
    }

    private void DrawGrassLod(Mesh mesh, ComputeBuffer grassBladesBuffer, ComputeBuffer argsBuffer, Bounds bounds, Texture windTexture, float windStrength)
    {
        if (mesh == null || grassBladesBuffer == null || argsBuffer == null)
            return;

        _propertyBlock.Clear();
        _propertyBlock.SetBuffer(GrassBladesId, grassBladesBuffer);
        _propertyBlock.SetTexture(LocalWindTexId, windTexture);
        _propertyBlock.SetFloat(LocalWindScaleId, _localWindScale);
        _propertyBlock.SetFloat(LocalWindSpeedId, _localWindSpeed);
        _propertyBlock.SetFloat(LocalWindStrengthId, windStrength);
        _grassMaterial.SetBuffer(GrassBladesId, grassBladesBuffer);

        _grassMaterial.enableInstancing = true;
        Graphics.DrawMeshInstancedIndirect(
            mesh,
            0,
            _grassMaterial,
            bounds,
            argsBuffer,
            0,
            _propertyBlock,
            ShadowCastingMode.On,
            true,
            gameObject.layer);
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

        _clusterInstancesBuffer?.Release();
        _clusterInstancesBuffer = null;

        _clumpParametersBuffer?.Release();
        _clumpParametersBuffer = null;

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
