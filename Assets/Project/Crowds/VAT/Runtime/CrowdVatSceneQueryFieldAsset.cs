using UnityEngine;

[CreateAssetMenu(menuName = "Qianxia/Crowd/Scene Query Field Asset", fileName = "CrowdSceneQueryField")]
public sealed class CrowdVatSceneQueryFieldAsset : ScriptableObject
{
    [Header("Ground Query")]
    [Tooltip("可选的可行走遮罩贴图，按世界 XZ 平面采样。当前默认使用 R 通道，>= 0.5 视为可行走。")]
    [SerializeField] private Texture2D _walkableMaskTexture;
    [Tooltip("可选的地面标记贴图。当前 CPU 低频查询会把 R 通道量化到 0~255，写回 authoredFlags。")]
    [SerializeField] private Texture2D _groundFlagsTexture;
    [Tooltip("开启后，Ground Query 的 2D 贴图默认复用 Terrain 世界范围做 UV 映射。关闭后使用下面的显式中心和尺寸。")]
    [SerializeField] private bool _useTerrainBoundsForGroundTextures = true;
    [Tooltip("Ground Query 贴图覆盖的世界空间中心。仅在不复用 Terrain 范围时生效。")]
    [SerializeField] private Vector3 _groundTextureWorldCenter = Vector3.zero;
    [Tooltip("Ground Query 贴图覆盖的世界空间尺寸，X 对应世界 X，Y 对应世界 Z。")]
    [SerializeField] private Vector2 _groundTextureWorldSize = new Vector2(64.0f, 64.0f);

    [Header("Obstacle Distance Query")]
    [Tooltip("静态场景的 3D SDF 贴图。用于 Obstacle Distance Query 的距离与梯度采样。")]
    [SerializeField] private Texture3D _staticObstacleSdfTexture;
    [Tooltip("静态障碍 SDF 体积的世界空间中心。")]
    [SerializeField] private Vector3 _staticObstacleSdfWorldCenter = new Vector3(0.0f, 1.0f, 0.0f);
    [Tooltip("静态障碍 SDF 体积的世界空间尺寸。")]
    [SerializeField] private Vector3 _staticObstacleSdfWorldSize = new Vector3(8.0f, 4.0f, 8.0f);
    [Tooltip("SDF 采样值到世界距离的缩放。默认假设贴图里直接存世界单位距离。")]
    [SerializeField] private float _staticObstacleDistanceScale = 1.0f;
    [Tooltip("SDF 采样值到世界距离的偏移。可用于适配非零等值面的距离场。")]
    [SerializeField] private float _staticObstacleDistanceBias = 0.0f;

    [Header("Baked Environment Distance Field")]
    [Tooltip("启用后，运行时环境约束优先采样这个统一环境距离场，而不是再把 terrain / obstacle 在运行时现场拼起来。")]
    [SerializeField] private bool _preferBakedEnvironmentDistanceField = true;
    [Tooltip("统一环境距离场覆盖的世界空间中心。")]
    [SerializeField] private Vector3 _bakedEnvironmentDistanceWorldCenter = new Vector3(0.0f, 1.0f, 0.0f);
    [Tooltip("统一环境距离场覆盖的世界空间尺寸。")]
    [SerializeField] private Vector3 _bakedEnvironmentDistanceWorldSize = new Vector3(32.0f, 8.0f, 32.0f);
    [Tooltip("统一环境距离场的规则采样分辨率。这个分辨率会直接决定运行时环境约束的空间精度。")]
    [SerializeField] private Vector3Int _bakedEnvironmentDistanceResolution = new Vector3Int(64, 32, 64);
    [Tooltip("运行时使用的统一环境距离场 3D 贴图。通常由编辑器烘焙生成。")]
    [SerializeField] private Texture3D _bakedEnvironmentDistanceTexture;
    [Tooltip("环境距离场采样值到世界距离的缩放。默认假设贴图里直接存世界单位距离。")]
    [SerializeField] private float _bakedEnvironmentDistanceScale = 1.0f;
    [Tooltip("环境距离场采样值到世界距离的偏移。可用于适配非零等值面。")]
    [SerializeField] private float _bakedEnvironmentDistanceBias = 0.0f;

    public Texture2D WalkableMaskTexture => _walkableMaskTexture;
    public Texture2D GroundFlagsTexture => _groundFlagsTexture;
    public bool UseTerrainBoundsForGroundTextures => _useTerrainBoundsForGroundTextures;
    public Vector3 GroundTextureWorldCenter => _groundTextureWorldCenter;
    public Vector2 GroundTextureWorldSize => _groundTextureWorldSize;
    public Texture3D StaticObstacleSdfTexture => _staticObstacleSdfTexture;
    public Vector3 StaticObstacleSdfWorldCenter => _staticObstacleSdfWorldCenter;
    public Vector3 StaticObstacleSdfWorldSize => _staticObstacleSdfWorldSize;
    public float StaticObstacleDistanceScale => _staticObstacleDistanceScale;
    public float StaticObstacleDistanceBias => _staticObstacleDistanceBias;
    public bool PreferBakedEnvironmentDistanceField => _preferBakedEnvironmentDistanceField;
    public Vector3 BakedEnvironmentDistanceWorldCenter => _bakedEnvironmentDistanceWorldCenter;
    public Vector3 BakedEnvironmentDistanceWorldSize => _bakedEnvironmentDistanceWorldSize;
    public Vector3Int BakedEnvironmentDistanceResolution => _bakedEnvironmentDistanceResolution;
    public Texture3D BakedEnvironmentDistanceTexture => _bakedEnvironmentDistanceTexture;
    public float BakedEnvironmentDistanceScale => _bakedEnvironmentDistanceScale;
    public float BakedEnvironmentDistanceBias => _bakedEnvironmentDistanceBias;

    private void OnValidate()
    {
        _groundTextureWorldSize.x = Mathf.Max(0.01f, _groundTextureWorldSize.x);
        _groundTextureWorldSize.y = Mathf.Max(0.01f, _groundTextureWorldSize.y);
        _staticObstacleSdfWorldSize.x = Mathf.Max(0.01f, _staticObstacleSdfWorldSize.x);
        _staticObstacleSdfWorldSize.y = Mathf.Max(0.01f, _staticObstacleSdfWorldSize.y);
        _staticObstacleSdfWorldSize.z = Mathf.Max(0.01f, _staticObstacleSdfWorldSize.z);
        _bakedEnvironmentDistanceWorldSize.x = Mathf.Max(0.01f, _bakedEnvironmentDistanceWorldSize.x);
        _bakedEnvironmentDistanceWorldSize.y = Mathf.Max(0.01f, _bakedEnvironmentDistanceWorldSize.y);
        _bakedEnvironmentDistanceWorldSize.z = Mathf.Max(0.01f, _bakedEnvironmentDistanceWorldSize.z);
        _bakedEnvironmentDistanceResolution.x = Mathf.Max(2, _bakedEnvironmentDistanceResolution.x);
        _bakedEnvironmentDistanceResolution.y = Mathf.Max(2, _bakedEnvironmentDistanceResolution.y);
        _bakedEnvironmentDistanceResolution.z = Mathf.Max(2, _bakedEnvironmentDistanceResolution.z);
    }

    public bool TryGetGroundFieldDescriptor(out CrowdVatGroundFieldDescriptor descriptor)
    {
        descriptor = new CrowdVatGroundFieldDescriptor
        {
            terrain = null,
            walkableMaskTexture = _walkableMaskTexture,
            groundFlagsTexture = _groundFlagsTexture,
            worldCenter = _groundTextureWorldCenter,
            worldSize = _groundTextureWorldSize,
            useTerrainBoundsForTextures = _useTerrainBoundsForGroundTextures,
            terrainHeightOffset = 0.0f
        };
        return _walkableMaskTexture != null || _groundFlagsTexture != null;
    }

    public bool TryGetObstacleDistanceFieldDescriptor(out CrowdVatObstacleDistanceFieldDescriptor descriptor)
    {
        descriptor = new CrowdVatObstacleDistanceFieldDescriptor
        {
            sdfTexture = _staticObstacleSdfTexture,
            worldCenter = _staticObstacleSdfWorldCenter,
            worldSize = _staticObstacleSdfWorldSize,
            distanceScale = _staticObstacleDistanceScale,
            distanceBias = _staticObstacleDistanceBias
        };
        return _staticObstacleSdfTexture != null
            && _staticObstacleSdfWorldSize.x > 0.0f
            && _staticObstacleSdfWorldSize.y > 0.0f
            && _staticObstacleSdfWorldSize.z > 0.0f;
    }

    public bool TryGetBakedEnvironmentDistanceFieldDescriptor(out CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor)
    {
        descriptor = new CrowdVatBakedEnvironmentDistanceFieldDescriptor
        {
            sdfTexture = _bakedEnvironmentDistanceTexture,
            worldCenter = _bakedEnvironmentDistanceWorldCenter,
            worldSize = _bakedEnvironmentDistanceWorldSize,
            distanceScale = _bakedEnvironmentDistanceScale,
            distanceBias = _bakedEnvironmentDistanceBias
        };
        return _preferBakedEnvironmentDistanceField &&
            _bakedEnvironmentDistanceTexture != null &&
            _bakedEnvironmentDistanceWorldSize.x > 0.0f &&
            _bakedEnvironmentDistanceWorldSize.y > 0.0f &&
            _bakedEnvironmentDistanceWorldSize.z > 0.0f;
    }

    public void SetBakedEnvironmentDistanceField(
        Texture3D texture,
        Vector3 worldCenter,
        Vector3 worldSize,
        float distanceScale,
        float distanceBias)
    {
        _bakedEnvironmentDistanceTexture = texture;
        _bakedEnvironmentDistanceWorldCenter = worldCenter;
        _bakedEnvironmentDistanceWorldSize = new Vector3(
            Mathf.Max(0.01f, worldSize.x),
            Mathf.Max(0.01f, worldSize.y),
            Mathf.Max(0.01f, worldSize.z));
        _bakedEnvironmentDistanceScale = distanceScale;
        _bakedEnvironmentDistanceBias = distanceBias;
        if (texture != null)
        {
            _bakedEnvironmentDistanceResolution = new Vector3Int(
                Mathf.Max(2, texture.width),
                Mathf.Max(2, texture.height),
                Mathf.Max(2, texture.depth));
        }
    }
}
