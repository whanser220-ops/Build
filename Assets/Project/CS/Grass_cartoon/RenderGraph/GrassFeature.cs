using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GrassFeature : ScriptableRendererFeature
{
    private const int k_DefaultPoissonMaxAttempts = 30;

    [Serializable]
    public class GrassClumpSettings
    {
        [Header("簇分布")]
        [Tooltip("Voronoi 网格缩放，值越小簇越大")]
        public float voronoiScale = 0.1f;

        [Tooltip("簇聚集度：0=均匀，1=强聚集")]
        [Range(0.0f, 1.0f)]
        public float clusterTightness = 0.5f;

        [Header("朝向")]
        [Tooltip("朝向一致性：0=完全随机，1=完全跟随簇方向")]
        [Range(0.0f, 1.0f)]
        public float orientationUniformity = 0.8f;

        [Tooltip("类型级朝向一致性：0=每簇随机朝向，1=同类型簇朝向一致")]
        [Range(0.0f, 1.0f)]
        public float typeOrientationUniformity = 0.0f;

        [Header("基础形态")]
        public float baseHeight = 1.0f;
        public float baseWidth = 0.1f;
        [Range(0, 1)] public float tilt = 0.2f;
        [Range(0, 1)] public float bend = 0.3f;

        [Header("形态变化")]
        [Range(0, 1)] public float heightVar = 0.5f;
        [Range(0, 1)] public float widthVar = 0.3f;
        [Range(0, 1)] public float tiltVar = 0.3f;
        [Range(0, 1)] public float bendVar = 0.3f;

        [Header("聚类分布")]
        [Tooltip("簇半径：草叶分布的范围")]
        public float clusterRadius = 6.0f;

        [Tooltip("高斯分布缩放：控制簇内密度衰减，0.1=非常紧密，1.5=非常分散")]
        [Range(0.1f, 1.5f)]
        public float clusterSigmaScale = 0.5f;

        [Tooltip("每簇最大草叶数")]
        public int maxBladesPerCluster = 512;
    }

    [Serializable]
    public class WindSettings
    {
        public Texture2D windTexture;
        [Range(0.001f, 1f)] public float windScale = 0.1f;
        [Range(0f, 10f)] public float windSpeed = 1f;
        [Range(0f, 1f)] public float windRotateAmount = 0.5f;
        [Range(0f, 2f)] public float windStrength = 0.3f;
    }

    [Serializable]
    public class Settings
    {
        [Header("Shader")]
        public ComputeShader cullingCompute;

        [Header("草地配置")]
        public Material grassMaterial;
        public Mesh grassMesh;
        public int grassCount = 100000;

        [Header("距离密度衰减")]
        public float densityFadeStart = 40.0f;
        public float densityFadeEnd = 100.0f;
        public float minDensity = 0.2f;

        [Header("簇参数")]
        public List<GrassClumpSettings> ClumpParameters = new List<GrassClumpSettings>();

        [Header("聚类中心生成")]
        public int clusterSeed = 12345;
        public float clusterMinDistance = 8.0f;
        public int maxClusterCount = 512;

        [Header("风场设置")]
        public WindSettings windSettings = new WindSettings();

        [Header("弯曲控制")]
        [Range(0f, 2f)] public float p1Weight = 0.1f;
        [Range(0f, 2f)] public float p2Weight = 0.6f;
        [Range(0f, 2f)] public float p3Weight = 1.0f;
        [Range(1f, 4f)] public float stiffnessCurve = 2.5f;
        [Range(0f, 1f)] public float tipBias = 0.3f;
        [Range(0f, 5f)] public float swayFrequency = 1.0f;

        [Header("渲染")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DispatchClusterInfo
    {
        public Vector3 worldCenter;
        public float radius;
        public float sigma;
        public uint targetCount;
        public uint seed;
        public uint sampleOffset;
        public uint clumpIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClusterCenter
    {
        public Vector3 worldCenter;
        public float radius;
        public float sigma;
        public float weight;
        public uint seed;
        public uint clumpIndex;
        public uint targetCount;
    }

    public Settings settings = new Settings();

    private GraphicsBuffer m_SingleGrassBuffer;
    private GraphicsBuffer m_CullingOutputBuffer;
    private GraphicsBuffer m_ArgsBuffer;
    private GraphicsBuffer m_ClumpBuffer;
    private GrassClumpParamsGPU[] m_ClumpDataArray;

    private ClusterCenter[] m_Clusters;
    private bool m_ClustersInitialized;
    private GraphicsBuffer m_DispatchClustersBuffer;
    private int m_DispatchClusterCount;

    private GrassRenderPass m_RenderPass;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct GrassParticle
    {
        public fixed float vertexPositions[42 * 3];
        public fixed float uvs[42 * 2];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GrassBlade
    {
        private float hash;
        private Vector3 position;
        private float facing;
        private float height;
        private float width;
        private float tilt;
        private float bend;
        private float sideBend;
        private float windForce;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GrassClumpParamsGPU
    {
        public float voronoiScale;
        public float clusterTightness;
        public float orientationUniformity;
        public float typeOrientationUniformity;
        public float baseHeight;
        public float baseWidth;
        public float tilt;
        public float bend;
        public float heightVar;
        public float widthVar;
        public float tiltVar;
        public float bendVar;
        public float clusterRadius;
        public float clusterSigmaScale;
        public uint maxBladesPerCluster;
    }

    public override void Create()
    {
        EnsureBuffers();
        m_RenderPass = new GrassRenderPass(this, settings, m_ArgsBuffer, m_CullingOutputBuffer, m_SingleGrassBuffer, m_ClumpBuffer);
        m_RenderPass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_RenderPass == null)
            return;

        if (m_CullingOutputBuffer == null || m_ArgsBuffer == null || m_SingleGrassBuffer == null)
            EnsureBuffers();

        var provider = GrassTerrainProvider.Instance;
        if (provider == null || provider.terrain == null || provider.HeightMap == null)
            return;

        m_RenderPass.renderPassEvent = settings.renderPassEvent;
        m_RenderPass.SetupTerrainData(provider.HeightMap, provider.terrain);
        renderer.EnqueuePass(m_RenderPass);
    }

    protected override void Dispose(bool disposing)
    {
        m_RenderPass?.Dispose();
        m_RenderPass = null;

        m_ClustersInitialized = false;
        m_Clusters = null;

        m_CullingOutputBuffer?.Release();
        m_CullingOutputBuffer = null;

        m_ArgsBuffer?.Release();
        m_ArgsBuffer = null;

        m_SingleGrassBuffer?.Release();
        m_SingleGrassBuffer = null;

        m_ClumpBuffer?.Release();
        m_ClumpBuffer = null;

        m_DispatchClustersBuffer?.Release();
        m_DispatchClustersBuffer = null;
    }

    private void EnsureBuffers()
    {
        if (settings.grassMesh == null || settings.cullingCompute == null || settings.grassMaterial == null)
            return;

        int count = Mathf.Max(1, settings.grassCount);

        if (m_SingleGrassBuffer == null || !m_SingleGrassBuffer.IsValid())
        {
            m_SingleGrassBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<GrassParticle>());
            InitializeSingleGrassBuffer();
        }

        if (m_CullingOutputBuffer == null || !m_CullingOutputBuffer.IsValid() || m_CullingOutputBuffer.count != count)
        {
            m_CullingOutputBuffer?.Release();
            m_CullingOutputBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, count, Marshal.SizeOf<GrassBlade>());
        }

        if (m_ArgsBuffer == null || !m_ArgsBuffer.IsValid())
            m_ArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 5 * sizeof(uint));

        InitializeArgsBuffer(count);
        UpdateClumpBuffer();
    }

    private void UpdateClumpBuffer()
    {
        if (settings.ClumpParameters == null)
            settings.ClumpParameters = new List<GrassClumpSettings>();

        if (settings.ClumpParameters.Count == 0)
            settings.ClumpParameters.Add(new GrassClumpSettings());

        int count = settings.ClumpParameters.Count;
        bool needRealloc = m_ClumpBuffer == null || !m_ClumpBuffer.IsValid() || m_ClumpBuffer.count != count;

        if (needRealloc)
        {
            m_ClumpBuffer?.Release();
            m_ClumpBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<GrassClumpParamsGPU>());
            m_ClumpDataArray = new GrassClumpParamsGPU[count];
        }

        for (int i = 0; i < count; i++)
        {
            var src = settings.ClumpParameters[i];
            m_ClumpDataArray[i] = new GrassClumpParamsGPU
            {
                voronoiScale = src.voronoiScale,
                clusterTightness = src.clusterTightness,
                orientationUniformity = src.orientationUniformity,
                typeOrientationUniformity = src.typeOrientationUniformity,
                baseHeight = src.baseHeight,
                baseWidth = src.baseWidth,
                tilt = src.tilt,
                bend = src.bend,
                heightVar = src.heightVar,
                widthVar = src.widthVar,
                tiltVar = src.tiltVar,
                bendVar = src.bendVar,
                clusterRadius = src.clusterRadius,
                clusterSigmaScale = src.clusterSigmaScale,
                maxBladesPerCluster = (uint)Mathf.Max(1, src.maxBladesPerCluster)
            };
        }

        m_ClumpBuffer.SetData(m_ClumpDataArray);
    }

    public void InitializeClusters(Vector3 terrainSize, Vector3 terrainPosition)
    {
        if (m_ClustersInitialized)
            return;

        float minDistance = Mathf.Max(0.5f, settings.clusterMinDistance);
        int maxClusters = Mathf.Max(1, settings.maxClusterCount);
        int maxAttempts = Mathf.Max(4, k_DefaultPoissonMaxAttempts);

        var points = GeneratePoissonPoints(
            new Rect(terrainPosition.x, terrainPosition.z, terrainSize.x, terrainSize.z),
            minDistance,
            maxClusters,
            maxAttempts,
            settings.clusterSeed);

        if (points.Count == 0)
            points.Add(new Vector2(terrainPosition.x + terrainSize.x * 0.5f, terrainPosition.z + terrainSize.z * 0.5f));

        int clumpCount = Mathf.Max(1, settings.ClumpParameters.Count);
        var rng = new System.Random(settings.clusterSeed * 17 + 13);

        m_Clusters = new ClusterCenter[points.Count];
        float weightSum = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            uint clumpIndex = (uint)rng.Next(0, clumpCount);
            var clumpSettings = settings.ClumpParameters[(int)clumpIndex];

            float weight = 0.7f + (float)rng.NextDouble() * 0.6f;
            float radius = Mathf.Max(0.5f, clumpSettings.clusterRadius);
            float sigma = Mathf.Max(0.05f, clumpSettings.clusterRadius * clumpSettings.clusterSigmaScale);

            var center = new ClusterCenter
            {
                worldCenter = new Vector3(points[i].x, terrainPosition.y, points[i].y),
                radius = radius,
                sigma = sigma,
                weight = weight,
                seed = unchecked((uint)rng.Next()),
                clumpIndex = clumpIndex,
                targetCount = 0u
            };

            m_Clusters[i] = center;
            weightSum += weight;
        }

        AssignClusterBudgetsPerType(weightSum);
        m_ClustersInitialized = true;
    }

    private void AssignClusterBudgetsPerType(float weightSum)
    {
        if (m_Clusters == null || m_Clusters.Length == 0)
            return;

        uint grassBudget = (uint)Mathf.Max(1, settings.grassCount);

        for (int i = 0; i < m_Clusters.Length; i++)
        {
            var c = m_Clusters[i];
            var clumpSettings = settings.ClumpParameters[(int)(c.clumpIndex % settings.ClumpParameters.Count)];

            float normalizedWeight = weightSum > 0.0001f ? c.weight / weightSum : 1.0f / m_Clusters.Length;
            uint target = (uint)Mathf.RoundToInt(grassBudget * normalizedWeight);

            // 使用该类型自己的 maxBladesPerCluster 作为上限
            uint maxPerCluster = (uint)Mathf.Max(1, clumpSettings.maxBladesPerCluster);
            c.targetCount = Math.Min(maxPerCluster, Math.Max(1u, target));
            m_Clusters[i] = c;
        }
    }

    public void UpdateVisibleClusters(Camera camera)
    {
        if (!m_ClustersInitialized || m_Clusters == null)
            return;

        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
        Vector3 camPos = camera.transform.position;
        const float maxDist = 150.0f;

        var dispatchClusters = new List<DispatchClusterInfo>(m_Clusters.Length * 2);

        for (int i = 0; i < m_Clusters.Length; i++)
        {
            var cluster = m_Clusters[i];
            if (cluster.targetCount == 0u)
                continue;

            float distToCamera = Vector3.Distance(camPos, cluster.worldCenter);
            if (distToCamera > maxDist + cluster.radius)
                continue;

            Bounds clusterBounds = new Bounds(cluster.worldCenter, Vector3.one * (cluster.radius * 2.0f));
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, clusterBounds))
                continue;

            uint groupCount = (cluster.targetCount + 63u) / 64u;
            for (uint g = 0; g < groupCount; g++)
            {
                dispatchClusters.Add(new DispatchClusterInfo
                {
                    worldCenter = cluster.worldCenter,
                    radius = cluster.radius,
                    sigma = cluster.sigma,
                    targetCount = cluster.targetCount,
                    seed = cluster.seed,
                    sampleOffset = g * 64u,
                    clumpIndex = cluster.clumpIndex
                });
            }
        }

        m_DispatchClusterCount = dispatchClusters.Count;

        if (m_DispatchClusterCount > 0)
        {
            if (m_DispatchClustersBuffer == null || !m_DispatchClustersBuffer.IsValid() || m_DispatchClustersBuffer.count < m_DispatchClusterCount)
            {
                m_DispatchClustersBuffer?.Release();
                m_DispatchClustersBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_DispatchClusterCount, Marshal.SizeOf<DispatchClusterInfo>());
            }

            m_DispatchClustersBuffer.SetData(dispatchClusters);
        }
    }

    public bool ClustersInitialized => m_ClustersInitialized;
    public GraphicsBuffer GetDispatchClustersBuffer() => m_DispatchClustersBuffer;
    public int GetDispatchClusterCount() => m_DispatchClusterCount;

    private static List<Vector2> GeneratePoissonPoints(Rect area, float minDistance, int maxPoints, int maxAttempts, int seed)
    {
        float cellSize = minDistance / Mathf.Sqrt(2f);
        int gridW = Mathf.Max(1, Mathf.CeilToInt(area.width / cellSize));
        int gridH = Mathf.Max(1, Mathf.CeilToInt(area.height / cellSize));

        int[] grid = new int[gridW * gridH];
        for (int i = 0; i < grid.Length; i++)
            grid[i] = -1;

        var rng = new System.Random(seed);
        var points = new List<Vector2>(maxPoints);
        var active = new List<Vector2>(maxPoints);

        Vector2 first = new Vector2(
            area.xMin + (float)rng.NextDouble() * area.width,
            area.yMin + (float)rng.NextDouble() * area.height);

        points.Add(first);
        active.Add(first);
        SetGrid(first, 0, area, cellSize, gridW, gridH, grid);

        while (active.Count > 0 && points.Count < maxPoints)
        {
            int activeIndex = rng.Next(active.Count);
            Vector2 center = active[activeIndex];
            bool found = false;

            for (int k = 0; k < maxAttempts; k++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float radius = minDistance * (1f + (float)rng.NextDouble());
                Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                if (!area.Contains(candidate))
                    continue;

                if (!IsValidCandidate(candidate, points, area, minDistance, cellSize, gridW, gridH, grid))
                    continue;

                points.Add(candidate);
                active.Add(candidate);
                SetGrid(candidate, points.Count - 1, area, cellSize, gridW, gridH, grid);
                found = true;

                if (points.Count >= maxPoints)
                    break;
            }

            if (!found)
                active.RemoveAt(activeIndex);
        }

        return points;
    }

    private static bool IsValidCandidate(Vector2 candidate, List<Vector2> points, Rect area, float minDistance, float cellSize, int gridW, int gridH, int[] grid)
    {
        int gx = Mathf.Clamp((int)((candidate.x - area.xMin) / cellSize), 0, gridW - 1);
        int gy = Mathf.Clamp((int)((candidate.y - area.yMin) / cellSize), 0, gridH - 1);

        int minX = Mathf.Max(0, gx - 2);
        int maxX = Mathf.Min(gridW - 1, gx + 2);
        int minY = Mathf.Max(0, gy - 2);
        int maxY = Mathf.Min(gridH - 1, gy + 2);

        float minDistSqr = minDistance * minDistance;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int idx = grid[y * gridW + x];
                if (idx < 0)
                    continue;

                if ((points[idx] - candidate).sqrMagnitude < minDistSqr)
                    return false;
            }
        }

        return true;
    }

    private static void SetGrid(Vector2 point, int pointIndex, Rect area, float cellSize, int gridW, int gridH, int[] grid)
    {
        int gx = Mathf.Clamp((int)((point.x - area.xMin) / cellSize), 0, gridW - 1);
        int gy = Mathf.Clamp((int)((point.y - area.yMin) / cellSize), 0, gridH - 1);
        int flat = gy * gridW + gx;
        if (flat >= 0 && flat < grid.Length)
            grid[flat] = pointIndex;
    }

    private unsafe void InitializeSingleGrassBuffer()
    {
        var mesh = settings.grassMesh;
        if (mesh == null)
            return;

        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;

        if (triangles == null || triangles.Length < 42 || vertices == null || vertices.Length == 0 || uvs == null || uvs.Length == 0)
            return;

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (var v in vertices)
        {
            if (v.y < minY) minY = v.y;
            if (v.y > maxY) maxY = v.y;
        }

        float heightRange = Mathf.Max(0.0001f, maxY - minY);
        GrassParticle[] tempParticles = new GrassParticle[1];

        for (int i = 0; i < 42; i++)
        {
            int tri = triangles[i];
            Vector3 pos = vertices[tri];
            pos.y = (pos.y - minY) / heightRange;
            tempParticles[0].vertexPositions[i * 3] = pos.x;
            tempParticles[0].vertexPositions[i * 3 + 1] = pos.y;
            tempParticles[0].vertexPositions[i * 3 + 2] = pos.z;

            Vector2 uv = uvs[tri];
            tempParticles[0].uvs[i * 2] = uv.x;
            tempParticles[0].uvs[i * 2 + 1] = uv.y;
        }

        m_SingleGrassBuffer.SetData(tempParticles);
    }

    private void InitializeArgsBuffer(int count)
    {
        uint[] args = new uint[5];
        args[0] = 42;
        args[1] = (uint)count;
        m_ArgsBuffer.SetData(args);
    }
}
