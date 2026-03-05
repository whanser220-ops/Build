using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GrassFeature : ScriptableRendererFeature
{
    [System.Serializable]
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
    }

    [System.Serializable]
    public class Settings
    {
        [Header("Shader")]
        public ComputeShader cullingCompute;

        [Header("草地配置")]
        public Material grassMaterial;
        public Mesh grassMesh;
        public int grassCount = 100000;
        public Bounds renderBounds = new Bounds(Vector3.zero, Vector3.one * 100);
        public float _width;
        public float _height;
        public float _tilt;
        public float _bend;

        [Header("风场")]
        public Texture2D windMask;
        [Tooltip("风场遮罩平铺系数，1 表示覆盖一整块地形")]
        public float windMaskTiling = 1.0f;

        [Header("距离密度衰减")]
        public float densityFadeStart = 40.0f;
        public float densityFadeEnd = 100.0f;
        public float minDensity = 0.2f;

        [Header("簇采样")]
        [Range(0.0f, 1.0f)]
        [Tooltip("簇中心随机抖动，减少规则网格感")]
        public float clusterSampleJitter = 0.7f;
        [Range(0.1f, 2.0f)]
        [Tooltip("簇内样本密度缩放，值越大草越密")]
        public float clusterDensityScale = 1.0f;

        [Header("簇参数")]
        public List<GrassClumpSettings> ClumpParameters = new List<GrassClumpSettings>();

        [Header("Tile-Based Generation")]
        [Tooltip("Size of each tile in world units")]
        public float tileSize = 8.0f;

        [Tooltip("Resolution of the density map (higher = more accurate)")]
        public int densityMapResolution = 128;

        [Tooltip("Maximum grass blades per tile")]
        public int maxBladesPerTile = 256;

        [Header("渲染")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    [System.Serializable]
    public struct TileInfo
    {
        public Vector2 uvMin;
        public Vector2 uvMax;
        public Vector3 worldMin;
        public Vector3 worldMax;
        public float maxDensity;
        public uint targetCount;
        public uint seed;
    }

    public Settings settings = new Settings();

    private GraphicsBuffer m_SingleGrassBuffer;
    private GraphicsBuffer m_CullingOutputBuffer;
    private GraphicsBuffer m_ArgsBuffer;
    private GraphicsBuffer m_ClumpBuffer;
    private GrassClumpParamsGPU[] m_ClumpDataArray;
    private TileInfo[] m_TileData;
    private int m_TileCountX;
    private int m_TileCountZ;
    private int m_TotalTileCount;
    private Texture2D m_DensityMap;
    private bool m_TilesInitialized = false;
    private GraphicsBuffer m_VisibleTilesBuffer;
    private int m_VisibleTileCount;
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
        private float windForce;
        private float height;
        private float width;
        private float tilt;
        private float bend;
        private float sideBend;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GrassClumpParamsGPU
    {
        public float voronoiScale;
        public float clusterTightness;
        public float orientationUniformity;
        public float baseHeight;
        public float baseWidth;
        public float tilt;
        public float bend;
        public float heightVar;
        public float widthVar;
        public float tiltVar;
        public float bendVar;
    }

    public override void Create()
    {
        EnsureBuffers();
        m_RenderPass = new GrassRenderPass(settings, m_ArgsBuffer, m_CullingOutputBuffer, m_SingleGrassBuffer, m_ClumpBuffer);
        m_RenderPass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_RenderPass == null)
            return;

        if (m_CullingOutputBuffer == null || m_ArgsBuffer == null || m_SingleGrassBuffer == null)
            EnsureBuffers();

        var provider = GrassTerrainProvider.Instance;
        if (provider == null || provider.terrain == null || provider.HeightMap == null || provider.NormalMap == null)
            return;

        m_RenderPass.renderPassEvent = settings.renderPassEvent;
        m_RenderPass.SetupTerrainData(provider.HeightMap, provider.NormalMap, provider.terrain);
        renderer.EnqueuePass(m_RenderPass);
    }

    protected override void Dispose(bool disposing)
    {
        m_RenderPass?.Dispose();
        m_RenderPass = null;

        if (m_DensityMap != null)
        {
            DestroyImmediate(m_DensityMap);
            m_DensityMap = null;
        }
        m_TilesInitialized = false;

        m_CullingOutputBuffer?.Release();
        m_CullingOutputBuffer = null;

        m_ArgsBuffer?.Release();
        m_ArgsBuffer = null;

        m_SingleGrassBuffer?.Release();
        m_SingleGrassBuffer = null;

        m_ClumpBuffer?.Release();
        m_ClumpBuffer = null;

        m_VisibleTilesBuffer?.Release();
        m_VisibleTilesBuffer = null;
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
        {
            m_ArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 5 * sizeof(uint));
        }

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
                baseHeight = src.baseHeight,
                baseWidth = src.baseWidth,
                tilt = src.tilt,
                bend = src.bend,
                heightVar = src.heightVar,
                widthVar = src.widthVar,
                tiltVar = src.tiltVar,
                bendVar = src.bendVar
            };
        }

        m_ClumpBuffer.SetData(m_ClumpDataArray);
    }

    public void InitializeTiles(Vector3 terrainSize, Vector3 terrainPosition)
    {
        if (m_TilesInitialized) return;

        // Generate density map using DensityMapGenerator
        var clumpArray = settings.ClumpParameters?.ToArray() ?? new GrassClumpSettings[0];
        if (clumpArray.Length == 0)
        {
            clumpArray = new GrassClumpSettings[] { new GrassClumpSettings() };
        }

        if (m_DensityMap != null)
        {
            DestroyImmediate(m_DensityMap);
        }
        m_DensityMap = DensityMapGenerator.GenerateDensityMap(
            terrainSize, clumpArray, settings.densityMapResolution);

        // Calculate tile grid
        m_TileCountX = Mathf.Max(1, Mathf.CeilToInt(terrainSize.x / settings.tileSize));
        m_TileCountZ = Mathf.Max(1, Mathf.CeilToInt(terrainSize.z / settings.tileSize));
        m_TotalTileCount = m_TileCountX * m_TileCountZ;

        m_TileData = new TileInfo[m_TotalTileCount];

        // Initialize each tile
        for (int z = 0; z < m_TileCountZ; z++)
        {
            for (int x = 0; x < m_TileCountX; x++)
            {
                int index = z * m_TileCountX + x;
                var tile = new TileInfo();

                // UV bounds
                tile.uvMin = new Vector2(
                    x / (float)m_TileCountX,
                    z / (float)m_TileCountZ);
                tile.uvMax = new Vector2(
                    (x + 1) / (float)m_TileCountX,
                    (z + 1) / (float)m_TileCountZ);

                // World bounds
                tile.worldMin = terrainPosition + new Vector3(
                    tile.uvMin.x * terrainSize.x,
                    0,
                    tile.uvMin.y * terrainSize.z);
                tile.worldMax = terrainPosition + new Vector3(
                    tile.uvMax.x * terrainSize.x,
                    terrainSize.y,
                    tile.uvMax.y * terrainSize.z);

                // Calculate max density within this tile
                tile.maxDensity = CalculateTileMaxDensity(tile);

                // Calculate target count based on density and budget
                uint budgetPerTile = (uint)Mathf.Max(1, settings.grassCount / m_TotalTileCount);
                tile.targetCount = (uint)Mathf.Min(settings.maxBladesPerTile,
                    (int)(budgetPerTile * tile.maxDensity));

                // Random seed for this tile
                tile.seed = (uint)(x * 73856093u) ^ (uint)(z * 19349663u);

                m_TileData[index] = tile;
            }
        }

        m_TilesInitialized = true;
        Debug.Log($"[GrassFeature] Initialized {m_TotalTileCount} tiles ({m_TileCountX}x{m_TileCountZ}), " +
                  $"density map {settings.densityMapResolution}x{settings.densityMapResolution}");
    }

    private float CalculateTileMaxDensity(TileInfo tile)
    {
        int samples = 4; // Sample corners of tile
        float maxDensity = 0;

        for (int i = 0; i < samples; i++)
        {
            for (int j = 0; j < samples; j++)
            {
                float u = Mathf.Lerp(tile.uvMin.x, tile.uvMax.x, i / (float)(samples - 1));
                float v = Mathf.Lerp(tile.uvMin.y, tile.uvMax.y, j / (float)(samples - 1));

                Color pixel = m_DensityMap.GetPixelBilinear(u, v);
                maxDensity = Mathf.Max(maxDensity, pixel.r);
            }
        }

        return maxDensity;
    }

    public Texture2D GetDensityMap() => m_DensityMap;
    public TileInfo[] GetTileData() => m_TileData;
    public int GetTileCountX() => m_TileCountX;
    public int GetTileCountZ() => m_TileCountZ;

    public void UpdateVisibleTiles(Camera camera, Vector3 terrainPosition, Vector3 terrainSize)
    {
        if (!m_TilesInitialized || m_TileData == null) return;

        // Get frustum planes
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);

        // Camera position and cull distance
        Vector3 camPos = camera.transform.position;
        float maxDist = 150.0f; // Match existing _MaxDrawDistance

        var visibleTiles = new System.Collections.Generic.List<TileInfo>();

        for (int i = 0; i < m_TileData.Length; i++)
        {
            var tile = m_TileData[i];

            // Skip empty tiles
            if (tile.maxDensity <= 0.01f || tile.targetCount == 0) continue;

            // Distance culling
            Vector3 tileCenter = (tile.worldMin + tile.worldMax) * 0.5f;
            float distToCamera = Vector3.Distance(camPos, tileCenter);
            if (distToCamera > maxDist + settings.tileSize) continue;

            // Frustum culling
            Bounds tileBounds = new Bounds(
                (tile.worldMin + tile.worldMax) * 0.5f,
                tile.worldMax - tile.worldMin);
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, tileBounds)) continue;

            visibleTiles.Add(tile);
        }

        m_VisibleTileCount = visibleTiles.Count;

        // Update buffer
        if (m_VisibleTileCount > 0)
        {
            if (m_VisibleTilesBuffer == null || !m_VisibleTilesBuffer.IsValid() ||
                m_VisibleTilesBuffer.count < m_VisibleTileCount)
            {
                m_VisibleTilesBuffer?.Release();
                m_VisibleTilesBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    m_VisibleTileCount,
                    System.Runtime.InteropServices.Marshal.SizeOf<TileInfo>());
            }

            m_VisibleTilesBuffer.SetData(visibleTiles);
        }

        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[GrassFeature] Visible tiles: {m_VisibleTileCount} / {m_TotalTileCount} " +
                      $"({(1.0f - (float)m_VisibleTileCount / m_TotalTileCount) * 100:F1}% culled)");
        }
    }

    public GraphicsBuffer GetVisibleTilesBuffer() => m_VisibleTilesBuffer;
    public int GetVisibleTileCount() => m_VisibleTileCount;

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
        args[2] = 0;
        args[3] = 0;
        args[4] = 0;
        m_ArgsBuffer.SetData(args);
    }

    // Public accessors for RenderPass
    public bool AreTilesInitialized() => m_TilesInitialized;

    public void EnsureTilesInitialized(Vector3 terrainSize, Vector3 terrainPosition)
    {
        if (!m_TilesInitialized)
        {
            InitializeTiles(terrainSize, terrainPosition);
        }
    }
}
