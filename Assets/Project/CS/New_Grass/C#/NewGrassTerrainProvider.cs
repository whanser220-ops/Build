using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class NewGrassTerrainProvider : MonoBehaviour
{
    public static NewGrassTerrainProvider Instance { get; private set; }

    [Header("Terrain Source")]
    [Tooltip("提供给 New_Grass 系统采样高度图的 Terrain。为空时会尝试读取同物体上的 Terrain 组件。")]
    [SerializeField] private Terrain _terrain;

    public Terrain Terrain => _terrain;
    public Texture2D HeightMap { get; private set; }

    private void OnEnable()
    {
        Instance = this;

        if (_terrain == null)
            _terrain = GetComponent<Terrain>();

        RefreshTerrainMaps();
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;

        ReleaseMaps();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;

        if (_terrain == null)
            _terrain = GetComponent<Terrain>();

        RefreshTerrainMaps();
    }

    [ContextMenu("Refresh Terrain Maps")]
    public void RefreshTerrainMaps()
    {
        ReleaseMaps();

        if (_terrain == null || _terrain.terrainData == null)
            return;

        TerrainData terrainData = _terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = terrainData.GetHeights(0, 0, resolution, resolution);

        HeightMap = new Texture2D(resolution, resolution, TextureFormat.RFloat, false, true)
        {
            name = "NewGrass_HeightMap",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = y * resolution + x;
                float height = heights[y, x];
                pixels[index] = new Color(height, 0.0f, 0.0f, 1.0f);
            }
        }

        HeightMap.SetPixels(pixels);
        HeightMap.Apply(false, false);
    }

    private void ReleaseMaps()
    {
        if (HeightMap == null)
            return;

        if (Application.isPlaying)
            Destroy(HeightMap);
        else
            DestroyImmediate(HeightMap);

        HeightMap = null;
    }
}
