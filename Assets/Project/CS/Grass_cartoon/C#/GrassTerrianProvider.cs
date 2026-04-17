using UnityEngine;

[ExecuteAlways] // 在编辑器下也会更新地形数据，方便直接预览草地
public class GrassTerrainProvider : MonoBehaviour
{
    // 全局访问入口
    public static GrassTerrainProvider Instance { get; private set; }

    public Terrain terrain;

    // 草地系统依赖的地形贴图
    public Texture2D HeightMap { get; private set; }
    public Texture2D NormalMap { get; private set; }

    private void OnEnable()
    {
        Instance = this;
        if (terrain == null)
            terrain = GetComponent<Terrain>();

        if (terrain != null)
            GenerateTerrainMaps();
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;

        if (HeightMap != null)
            DestroyImmediate(HeightMap);
        if (NormalMap != null)
            DestroyImmediate(NormalMap);
    }

    private void GenerateTerrainMaps()
    {
        TerrainData terrainData = terrain.terrainData;
        int heightmapResolution = terrainData.heightmapResolution;

        // 1) 生成高度图
        HeightMap = new Texture2D(heightmapResolution, heightmapResolution, TextureFormat.RFloat, false);
        float[,] heights = terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);
        Color[] pixels = new Color[heightmapResolution * heightmapResolution];

        for (int y = 0; y < heightmapResolution; y++)
        {
            for (int x = 0; x < heightmapResolution; x++)
            {
                int index = y * heightmapResolution + x;
                float height = heights[y, x];
                pixels[index] = new Color(height, height, height, 1);
            }
        }

        HeightMap.SetPixels(pixels);
        HeightMap.Apply();
        HeightMap.wrapMode = TextureWrapMode.Clamp;

        // 2) 由高度图计算法线图
        NormalMap = GenerateNormalMap(heights, heightmapResolution, terrainData.size);
    }

    private static Texture2D GenerateNormalMap(float[,] heights, int resolution, Vector3 terrainSize)
    {
        Texture2D normalTex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        Color[] pixels = new Color[resolution * resolution];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int xm1 = Mathf.Max(0, x - 1);
                int xp1 = Mathf.Min(resolution - 1, x + 1);
                int ym1 = Mathf.Max(0, y - 1);
                int yp1 = Mathf.Min(resolution - 1, y + 1);

                float dx = (heights[y, xp1] - heights[y, xm1]) * terrainSize.y / (terrainSize.x / resolution * 2);
                float dy = (heights[yp1, x] - heights[ym1, x]) * terrainSize.y / (terrainSize.z / resolution * 2);

                Vector3 normal = new Vector3(-dx, 1, -dy).normalized;
                pixels[y * resolution + x] = new Color(
                    normal.x * 0.5f + 0.5f,
                    normal.y * 0.5f + 0.5f,
                    normal.z * 0.5f + 0.5f,
                    1);
            }
        }

        normalTex.SetPixels(pixels);
        normalTex.Apply();
        normalTex.wrapMode = TextureWrapMode.Clamp;
        return normalTex;
    }
}
