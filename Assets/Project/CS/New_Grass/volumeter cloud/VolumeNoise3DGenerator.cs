using System;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class VolumeNoise3DGenerator : MonoBehaviour
{
    private const string DefaultOutputPath = "Assets/Project/CS/New_Grass/volumeter cloud/GeneratedVolumeNoise.asset";
    private const string DefaultMaterialPath = "Assets/Project/CS/New_Grass/volumeter cloud/LearningVolumeCloud.mat";
    private static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex3D");

    [Header("三维噪声设置")]
    [Tooltip("三维噪声图的边长。教学版建议先用 32 或 64。")]
    [SerializeField, Range(32, 64)]
    private int _textureSize = 32;

    [Tooltip("改变种子会直接改变整张三维噪声体的形状。")]
    [SerializeField]
    private int _seed = 42;

    [Tooltip("控制噪声的大尺度频率。值越大，体积结构越碎。")]
    [SerializeField, Min(0.25f)]
    private float _frequency = 4.0f;

    [Tooltip("FBM 的叠加层数。层数越高，细节越多。")]
    [SerializeField, Range(1, 6)]
    private int _octaves = 4;

    [Tooltip("每一层细节衰减的幅度。值越低，高频细节越弱。")]
    [SerializeField, Range(0.1f, 0.95f)]
    private float _persistence = 0.5f;

    [Header("输出设置")]
    [Tooltip("生成并保存三维噪声资产时使用的输出路径。")]
    [SerializeField]
    private string _outputAssetPath = DefaultOutputPath;

    [SerializeField]
    private Texture3D _savedTextureAsset;

    [Header("预览目标")]
    [Tooltip("会把生成的三维噪声临时塞进这个渲染器，方便直接预览体积效果。")]
    [SerializeField]
    private Renderer _targetRenderer;

    [Tooltip("在编辑器里修改参数时，是否立即重生成预览。")]
    [SerializeField]
    private bool _regenerateInEditMode = true;

    private MaterialPropertyBlock _propertyBlock;

    [NonSerialized]
    private Texture3D _previewTexture;

    private void Reset()
    {
        FindTargetRenderer();
    }

    private void OnEnable()
    {
        FindTargetRenderer();
        RefreshPreviewTexture();
    }

    private void OnValidate()
    {
        _textureSize = Mathf.Clamp(_textureSize, 32, 64);
        _octaves = Mathf.Clamp(_octaves, 1, 6);
        _persistence = Mathf.Clamp(_persistence, 0.1f, 0.95f);
        _frequency = Mathf.Max(0.25f, _frequency);

        if (!isActiveAndEnabled)
        {
            return;
        }

        FindTargetRenderer();
        if (_regenerateInEditMode || Application.isPlaying)
        {
            RefreshPreviewTexture();
        }
        else if (_savedTextureAsset != null)
        {
            ApplyTextureToRenderer(_savedTextureAsset);
        }
    }

    private void OnDisable()
    {
        ReleasePreviewTexture();
    }

    [ContextMenu("重新生成预览三维噪声")]
    public void RefreshPreviewTexture()
    {
        ReleasePreviewTexture();
        _previewTexture = CreateNoiseTexture(
            _textureSize,
            _seed,
            _frequency,
            _octaves,
            _persistence,
            "PreviewVolumeNoise");
        _previewTexture.hideFlags = HideFlags.HideAndDontSave;

        ApplyTextureToRenderer(_previewTexture);
    }

#if UNITY_EDITOR
    [ContextMenu("生成并保存三维噪声资产")]
    public void SaveNoiseAsset()
    {
        string assetPath = SanitizeAssetPath(_outputAssetPath);
        EnsureParentFolder(assetPath);

        Texture3D sourceTexture = CreateNoiseTexture(
            _textureSize,
            _seed,
            _frequency,
            _octaves,
            _persistence,
            Path.GetFileNameWithoutExtension(assetPath));

        Texture3D existingAsset = AssetDatabase.LoadAssetAtPath<Texture3D>(assetPath);
        if (existingAsset == null)
        {
            AssetDatabase.CreateAsset(sourceTexture, assetPath);
            _savedTextureAsset = AssetDatabase.LoadAssetAtPath<Texture3D>(assetPath);
        }
        else
        {
            EditorUtility.CopySerialized(sourceTexture, existingAsset);
            existingAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            EditorUtility.SetDirty(existingAsset);
            DestroyImmediate(sourceTexture);
            _savedTextureAsset = existingAsset;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (_savedTextureAsset != null)
        {
            AssignTextureToSharedMaterial(_savedTextureAsset);
            ApplyTextureToRenderer(_savedTextureAsset);
        }

        EditorUtility.SetDirty(this);
    }

    public static void GenerateDefaultAsset()
    {
        const int defaultTextureSize = 32;
        const int defaultSeed = 42;
        const float defaultFrequency = 4.0f;
        const int defaultOctaves = 4;
        const float defaultPersistence = 0.5f;

        EnsureParentFolder(DefaultOutputPath);

        Texture3D sourceTexture = CreateNoiseTexture(
            defaultTextureSize,
            defaultSeed,
            defaultFrequency,
            defaultOctaves,
            defaultPersistence,
            "GeneratedVolumeNoise");

        Texture3D existingAsset = AssetDatabase.LoadAssetAtPath<Texture3D>(DefaultOutputPath);
        Texture3D targetAsset;
        if (existingAsset == null)
        {
            AssetDatabase.CreateAsset(sourceTexture, DefaultOutputPath);
            targetAsset = AssetDatabase.LoadAssetAtPath<Texture3D>(DefaultOutputPath);
        }
        else
        {
            EditorUtility.CopySerialized(sourceTexture, existingAsset);
            existingAsset.name = "GeneratedVolumeNoise";
            EditorUtility.SetDirty(existingAsset);
            DestroyImmediate(sourceTexture);
            targetAsset = existingAsset;
        }

        Material learningMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        if (learningMaterial != null && targetAsset != null)
        {
            learningMaterial.SetTexture(NoiseTexId, targetAsset);
            EditorUtility.SetDirty(learningMaterial);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static string SanitizeAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return DefaultOutputPath;
        }

        string normalizedPath = assetPath.Replace('\\', '/').Trim();
        if (!normalizedPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            return DefaultOutputPath;
        }

        if (!normalizedPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath += ".asset";
        }

        return normalizedPath;
    }

    private static void EnsureParentFolder(string assetPath)
    {
        string fullFolderPath = Path.GetDirectoryName(assetPath);
        if (string.IsNullOrEmpty(fullFolderPath))
        {
            return;
        }

        string[] parts = fullFolderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
#endif

    private void FindTargetRenderer()
    {
        if (_targetRenderer == null)
        {
            _targetRenderer = GetComponentInChildren<Renderer>();
        }
    }

    private void ApplyTextureToRenderer(Texture texture)
    {
        if (_targetRenderer == null || texture == null)
        {
            return;
        }

        _propertyBlock ??= new MaterialPropertyBlock();
        _targetRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetTexture(NoiseTexId, texture);
        _targetRenderer.SetPropertyBlock(_propertyBlock);
    }

    private void AssignTextureToSharedMaterial(Texture texture)
    {
        if (_targetRenderer == null || texture == null)
        {
            return;
        }

        Material sharedMaterial = _targetRenderer.sharedMaterial;
        if (sharedMaterial == null)
        {
            return;
        }

        sharedMaterial.SetTexture(NoiseTexId, texture);

#if UNITY_EDITOR
        EditorUtility.SetDirty(sharedMaterial);
#endif
    }

    private void ReleasePreviewTexture()
    {
        if (_previewTexture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(_previewTexture);
        }
        else
        {
            DestroyImmediate(_previewTexture);
        }

        _previewTexture = null;
    }

    private static Texture3D CreateNoiseTexture(
        int textureSize,
        int seed,
        float frequency,
        int octaves,
        float persistence,
        string textureName)
    {
        int clampedSize = Mathf.Clamp(textureSize, 32, 64);
        Texture3D texture = new Texture3D(clampedSize, clampedSize, clampedSize, TextureFormat.RGBA32, false)
        {
            name = textureName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear
        };

        Color[] colors = new Color[clampedSize * clampedSize * clampedSize];
        Vector3 seedOffset = new Vector3(
            HashToUnitFloat(seed, 11, 23) * 100.0f,
            HashToUnitFloat(seed, 37, 53) * 100.0f,
            HashToUnitFloat(seed, 71, 97) * 100.0f);

        int index = 0;
        for (int z = 0; z < clampedSize; z++)
        {
            for (int y = 0; y < clampedSize; y++)
            {
                for (int x = 0; x < clampedSize; x++)
                {
                    Vector3 uvw = new Vector3(
                        clampedSize > 1 ? x / (float)(clampedSize - 1) : 0.0f,
                        clampedSize > 1 ? y / (float)(clampedSize - 1) : 0.0f,
                        clampedSize > 1 ? z / (float)(clampedSize - 1) : 0.0f);

                    float noise = SampleFbm(uvw * frequency + seedOffset, octaves, persistence);
                    colors[index++] = new Color(noise, noise, noise, 1.0f);
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply(false, false);
        return texture;
    }

    private static float SampleFbm(Vector3 samplePosition, int octaves, float persistence)
    {
        float sum = 0.0f;
        float amplitude = 0.5f;
        float amplitudeSum = 0.0f;
        Vector3 currentSample = samplePosition;

        for (int octave = 0; octave < octaves; octave++)
        {
            sum += SampleValueNoise(currentSample) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= persistence;
            currentSample = currentSample * 2.01f + new Vector3(13.1f, 17.7f, 19.9f);
        }

        if (amplitudeSum <= 0.0f)
        {
            return 0.0f;
        }

        return sum / amplitudeSum;
    }

    private static float SampleValueNoise(Vector3 samplePosition)
    {
        Vector3Int cell = new Vector3Int(
            Mathf.FloorToInt(samplePosition.x),
            Mathf.FloorToInt(samplePosition.y),
            Mathf.FloorToInt(samplePosition.z));

        Vector3 frac = new Vector3(
            samplePosition.x - cell.x,
            samplePosition.y - cell.y,
            samplePosition.z - cell.z);

        frac = new Vector3(
            SmoothQuintic(frac.x),
            SmoothQuintic(frac.y),
            SmoothQuintic(frac.z));

        float n000 = HashToUnitFloat(cell.x, cell.y, cell.z);
        float n100 = HashToUnitFloat(cell.x + 1, cell.y, cell.z);
        float n010 = HashToUnitFloat(cell.x, cell.y + 1, cell.z);
        float n110 = HashToUnitFloat(cell.x + 1, cell.y + 1, cell.z);
        float n001 = HashToUnitFloat(cell.x, cell.y, cell.z + 1);
        float n101 = HashToUnitFloat(cell.x + 1, cell.y, cell.z + 1);
        float n011 = HashToUnitFloat(cell.x, cell.y + 1, cell.z + 1);
        float n111 = HashToUnitFloat(cell.x + 1, cell.y + 1, cell.z + 1);

        float nx00 = Mathf.Lerp(n000, n100, frac.x);
        float nx10 = Mathf.Lerp(n010, n110, frac.x);
        float nx01 = Mathf.Lerp(n001, n101, frac.x);
        float nx11 = Mathf.Lerp(n011, n111, frac.x);
        float nxy0 = Mathf.Lerp(nx00, nx10, frac.y);
        float nxy1 = Mathf.Lerp(nx01, nx11, frac.y);
        return Mathf.Lerp(nxy0, nxy1, frac.z);
    }

    private static float SmoothQuintic(float value)
    {
        return value * value * value * (value * (value * 6.0f - 15.0f) + 10.0f);
    }

    private static float HashToUnitFloat(int x, int y, int z)
    {
        unchecked
        {
            uint hash = (uint)x * 374761393u;
            hash += (uint)y * 668265263u;
            hash += (uint)z * 2147483647u;
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            hash ^= hash >> 16;
            return hash / 4294967295.0f;
        }
    }
}
