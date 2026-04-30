using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NewGrassIndirectRenderer))]
public class NewGrassIndirectRendererEditor : Editor
{
    private const int k_DefaultDistributionMapSize = 512;
    private static readonly string[] s_ChannelLabels = { "R", "G", "B", "A" };

    private SerializedProperty _distributionMapProperty;
    private SerializedProperty _bakedPlacementAssetProperty;
    private SerializedProperty _clumpParametersProperty;

    private bool _paintModeEnabled;
    private bool _eraseModeEnabled;
    private int _paintChannelIndex;
    private float _brushRadius = 6.0f;
    private float _brushOpacity = 0.35f;
    private float _brushFalloff = 0.7f;

    private Texture2D _workingTexture;
    private string _workingTexturePath;
    private Color32[] _workingPixels;
    private int _workingWidth;
    private int _workingHeight;
    private bool _textureDirty;

    private void OnEnable()
    {
        _distributionMapProperty = serializedObject.FindProperty("_distributionMap");
        _bakedPlacementAssetProperty = serializedObject.FindProperty("_bakedPlacementAsset");
        _clumpParametersProperty = serializedObject.FindProperty("_clumpParameters");
    }

    private void OnDisable()
    {
        SaveWorkingTextureIfNeeded();
        _paintModeEnabled = false;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        EditorGUILayout.Space();
        DrawDistributionMapTools();

        if (serializedObject.ApplyModifiedProperties())
        {
            SaveWorkingTextureIfNeeded();
            BakeAndRebuild((NewGrassIndirectRenderer)target, true);
        }
    }

    private void DrawDistributionMapTools()
    {
        NewGrassIndirectRenderer renderer = (NewGrassIndirectRenderer)target;
        Texture2D distributionMap = _distributionMapProperty.objectReferenceValue as Texture2D;
        int clumpCount = _clumpParametersProperty != null ? _clumpParametersProperty.arraySize : 0;

        EditorGUILayout.LabelField("Distribution Map Tools", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("RGBA channels map to the first four clump templates. Texture UV maps directly to terrain XZ.", MessageType.Info);

        if (clumpCount < 4)
        {
            EditorGUILayout.HelpBox("Fewer than four clump templates are configured. Unmapped channels will be ignored.", MessageType.Warning);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create Map"))
                CreateDistributionMapAsset(renderer);

            if (GUILayout.Button("Create Bake"))
                CreateBakeAsset(renderer);

            using (new EditorGUI.DisabledScope(distributionMap == null))
            {
                bool nextPaintMode = GUILayout.Toggle(_paintModeEnabled, _paintModeEnabled ? "Stop Paint" : "Scene Paint", "Button");
                if (nextPaintMode != _paintModeEnabled)
                {
                    _paintModeEnabled = nextPaintMode;
                    if (!_paintModeEnabled)
                    {
                        SaveWorkingTextureIfNeeded();
                        BakeAndRebuild(renderer, true);
                    }

                    SceneView.RepaintAll();
                }
            }
        }

        if (_bakedPlacementAssetProperty != null && _bakedPlacementAssetProperty.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("Bake asset is missing. Runtime now consumes baked placement data instead of rescanning the distribution map.", MessageType.Warning);
        }
        else
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bake Now"))
                    BakeAndRebuild(renderer, true);
            }
        }

        if (distributionMap == null)
            return;

        string path = AssetDatabase.GetAssetPath(distributionMap);
        bool isPng = IsPngAssetPath(path);
        bool isReadable = IsTextureReadable(path);

        if (!isPng)
        {
            EditorGUILayout.HelpBox("Scene painting currently supports PNG distribution maps only. Create a new PNG map first.", MessageType.Error);
        }
        else if (!isReadable)
        {
            EditorGUILayout.HelpBox("Read/Write is disabled on this texture. Enable it before painting in SceneView.", MessageType.Warning);
            if (GUILayout.Button("Fix Import Settings"))
                ConfigureDistributionTextureImporter(path);
        }

        using (new EditorGUI.DisabledScope(!_paintModeEnabled || !isPng || !isReadable))
        {
            _paintChannelIndex = GUILayout.Toolbar(_paintChannelIndex, s_ChannelLabels);
            _eraseModeEnabled = EditorGUILayout.Toggle("Erase Mode", _eraseModeEnabled);
            _brushRadius = EditorGUILayout.Slider("Brush Radius", _brushRadius, 0.5f, 32.0f);
            _brushOpacity = EditorGUILayout.Slider("Brush Strength", _brushOpacity, 0.01f, 1.0f);
            _brushFalloff = EditorGUILayout.Slider("Brush Falloff", _brushFalloff, 0.0f, 1.0f);
        }
    }

    private void OnSceneGUI()
    {
        if (!_paintModeEnabled)
            return;

        NewGrassIndirectRenderer renderer = (NewGrassIndirectRenderer)target;
        Terrain terrain = ResolveTerrain(renderer);
        Texture2D distributionMap = _distributionMapProperty.objectReferenceValue as Texture2D;
        if (terrain == null || terrain.terrainData == null || distributionMap == null)
            return;

        if (!TryPrepareWorkingTexture(distributionMap))
            return;

        Event currentEvent = Event.current;
        if (!TryGetTerrainHit(terrain, currentEvent.mousePosition, out RaycastHit hit))
            return;

        Handles.color = _eraseModeEnabled ? new Color(1.0f, 0.35f, 0.35f, 0.9f) : GetChannelPreviewColor(_paintChannelIndex);
        Handles.DrawWireDisc(hit.point, hit.normal, _brushRadius);
        Handles.DrawSolidDisc(hit.point, hit.normal, Mathf.Max(0.05f, _brushRadius * 0.08f));

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        bool isPaintEvent = (currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag)
            && currentEvent.button == 0
            && !currentEvent.alt;
        if (isPaintEvent)
        {
            if (PaintDistributionMapAt(renderer, terrain, hit.point))
            {
                currentEvent.Use();
                SceneView.RepaintAll();
            }

            return;
        }

        if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
        {
            SaveWorkingTextureIfNeeded();
            BakeAndRebuild(renderer, true);
            currentEvent.Use();
        }
    }

    private void CreateDistributionMapAsset(NewGrassIndirectRenderer renderer)
    {
        string defaultName = renderer.name + "_GrassDistribution.png";
        string path = EditorUtility.SaveFilePanelInProject("Create Grass Distribution Map", defaultName, "png", "Choose where to save the distribution map.");
        if (string.IsNullOrEmpty(path))
            return;

        Texture2D texture = new Texture2D(k_DefaultDistributionMapSize, k_DefaultDistributionMapSize, TextureFormat.RGBA32, false, true)
        {
            name = Path.GetFileNameWithoutExtension(path),
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        Color32[] pixels = new Color32[k_DefaultDistributionMapSize * k_DefaultDistributionMapSize];
        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        File.WriteAllBytes(GetAbsoluteAssetPath(path), texture.EncodeToPNG());
        DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        ConfigureDistributionTextureImporter(path);

        Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        _distributionMapProperty.objectReferenceValue = importedTexture;
        serializedObject.ApplyModifiedProperties();
        ResetWorkingTextureCache();
        EnsureBakeAssetAssigned(renderer);
        BakeAndRebuild(renderer, true);
        EditorUtility.SetDirty(renderer);
    }

    private bool PaintDistributionMapAt(NewGrassIndirectRenderer renderer, Terrain terrain, Vector3 hitPoint)
    {
        if (_workingTexture == null || _workingPixels == null || _workingPixels.Length == 0)
            return false;

        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;
        if (terrainSize.x <= 0.0f || terrainSize.z <= 0.0f)
            return false;

        float normalizedX = Mathf.Clamp01((hitPoint.x - terrainPosition.x) / terrainSize.x);
        float normalizedZ = Mathf.Clamp01((hitPoint.z - terrainPosition.z) / terrainSize.z);
        float brushRadiusNormalizedX = _brushRadius / terrainSize.x;
        float brushRadiusNormalizedZ = _brushRadius / terrainSize.z;

        int minX = Mathf.Clamp(Mathf.FloorToInt((normalizedX - brushRadiusNormalizedX) * _workingWidth), 0, _workingWidth - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((normalizedX + brushRadiusNormalizedX) * _workingWidth), 0, _workingWidth - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((normalizedZ - brushRadiusNormalizedZ) * _workingHeight), 0, _workingHeight - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt((normalizedZ + brushRadiusNormalizedZ) * _workingHeight), 0, _workingHeight - 1);

        bool modified = false;
        float falloffExponent = Mathf.Lerp(8.0f, 1.0f, _brushFalloff);
        Vector2 brushCenterXZ = new Vector2(hitPoint.x, hitPoint.z);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float texelU = (x + 0.5f) / _workingWidth;
                float texelV = (y + 0.5f) / _workingHeight;
                Vector2 texelXZ = new Vector2(
                    terrainPosition.x + (texelU * terrainSize.x),
                    terrainPosition.z + (texelV * terrainSize.z));
                float distance = Vector2.Distance(texelXZ, brushCenterXZ);
                if (distance > _brushRadius)
                    continue;

                float normalizedDistance = 1.0f - Mathf.Clamp01(distance / Mathf.Max(0.0001f, _brushRadius));
                float strength = Mathf.Pow(normalizedDistance, falloffExponent) * _brushOpacity;
                if (strength <= 0.0001f)
                    continue;

                int pixelIndex = (y * _workingWidth) + x;
                Color32 pixel = _workingPixels[pixelIndex];
                float channelValue = GetChannel01(pixel, _paintChannelIndex);
                float nextValue = _eraseModeEnabled
                    ? Mathf.Lerp(channelValue, 0.0f, strength)
                    : Mathf.Lerp(channelValue, 1.0f, strength);
                byte nextByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(nextValue) * 255.0f);
                if (SetChannel(ref pixel, _paintChannelIndex, nextByte))
                {
                    _workingPixels[pixelIndex] = pixel;
                    modified = true;
                }
            }
        }

        if (!modified)
            return false;

        _workingTexture.SetPixels32(_workingPixels);
        _workingTexture.Apply(false, false);
        _textureDirty = true;
        BakeAndRebuild(renderer, false);
        EditorUtility.SetDirty(renderer);
        return true;
    }

    private bool TryPrepareWorkingTexture(Texture2D distributionMap)
    {
        string path = AssetDatabase.GetAssetPath(distributionMap);
        if (!IsPngAssetPath(path) || !IsTextureReadable(path))
            return false;

        if (_workingTexture == distributionMap && _workingPixels != null && _workingPixels.Length == distributionMap.width * distributionMap.height)
            return true;

        try
        {
            _workingTexture = distributionMap;
            _workingTexturePath = path;
            _workingWidth = distributionMap.width;
            _workingHeight = distributionMap.height;
            _workingPixels = distributionMap.GetPixels32();
            _textureDirty = false;
            return true;
        }
        catch (UnityException)
        {
            ResetWorkingTextureCache();
            return false;
        }
    }

    private void SaveWorkingTextureIfNeeded()
    {
        if (!_textureDirty || _workingTexture == null || string.IsNullOrEmpty(_workingTexturePath))
            return;

        File.WriteAllBytes(GetAbsoluteAssetPath(_workingTexturePath), _workingTexture.EncodeToPNG());
        AssetDatabase.ImportAsset(_workingTexturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        ConfigureDistributionTextureImporter(_workingTexturePath);
        Texture2D reloadedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_workingTexturePath);
        if (reloadedTexture != null)
        {
            _distributionMapProperty.objectReferenceValue = reloadedTexture;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            _workingTexture = reloadedTexture;
            _workingWidth = reloadedTexture.width;
            _workingHeight = reloadedTexture.height;
            _workingPixels = reloadedTexture.GetPixels32();
        }

        _textureDirty = false;
    }

    private void ResetWorkingTextureCache()
    {
        _workingTexture = null;
        _workingTexturePath = null;
        _workingPixels = null;
        _workingWidth = 0;
        _workingHeight = 0;
        _textureDirty = false;
    }

    private void BakeAndRebuild(NewGrassIndirectRenderer renderer, bool saveAsset)
    {
        if (renderer == null)
            return;

        if (EnsureBakeAssetAssigned(renderer))
            renderer.BakeDistributionDataAsset(saveAsset);

        renderer.RebuildGrass();
    }

    private void CreateBakeAsset(NewGrassIndirectRenderer renderer)
    {
        if (renderer == null)
            return;

        if (EnsureBakeAssetAssigned(renderer))
            EditorUtility.SetDirty(renderer);
    }

    private bool EnsureBakeAssetAssigned(NewGrassIndirectRenderer renderer)
    {
        if (_bakedPlacementAssetProperty == null)
            return false;

        if (_bakedPlacementAssetProperty.objectReferenceValue != null)
            return true;

        Texture2D distributionMap = _distributionMapProperty != null ? _distributionMapProperty.objectReferenceValue as Texture2D : null;
        if (distributionMap == null)
            return false;

        string distributionMapPath = AssetDatabase.GetAssetPath(distributionMap);
        if (string.IsNullOrEmpty(distributionMapPath))
            return false;

        string directory = Path.GetDirectoryName(distributionMapPath);
        if (string.IsNullOrEmpty(directory))
            return false;

        string normalizedDirectory = directory.Replace("\\", "/");
        string assetName = Path.GetFileNameWithoutExtension(distributionMapPath) + "_Bake.asset";
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(normalizedDirectory, assetName).Replace("\\", "/"));

        NewGrassBakedDataAsset bakeAsset = ScriptableObject.CreateInstance<NewGrassBakedDataAsset>();
        bakeAsset.name = Path.GetFileNameWithoutExtension(assetName);
        AssetDatabase.CreateAsset(bakeAsset, assetPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        _bakedPlacementAssetProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<NewGrassBakedDataAsset>(assetPath);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(renderer);
        return _bakedPlacementAssetProperty.objectReferenceValue != null;
    }

    private static Terrain ResolveTerrain(NewGrassIndirectRenderer renderer)
    {
        if (NewGrassTerrainProvider.Instance != null && NewGrassTerrainProvider.Instance.Terrain != null)
            return NewGrassTerrainProvider.Instance.Terrain;

        Terrain terrain = renderer.GetComponent<Terrain>();
        if (terrain != null)
            return terrain;

        return Terrain.activeTerrain;
    }

    private static bool TryGetTerrainHit(Terrain terrain, Vector2 mousePosition, out RaycastHit hit)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider != null && terrainCollider.Raycast(ray, out hit, 100000.0f))
            return true;

        if (Physics.Raycast(ray, out hit, 100000.0f))
            return hit.collider != null && hit.collider.GetComponent<Terrain>() == terrain;

        return false;
    }

    private static bool ConfigureDistributionTextureImporter(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return false;

        importer.textureType = TextureImporterType.Default;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Point;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.isReadable = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
        return true;
    }

    private static bool IsTextureReadable(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        return importer != null && importer.isReadable;
    }

    private static string GetAbsoluteAssetPath(string assetPath)
    {
        return Path.GetFullPath(assetPath);
    }

    private static bool IsPngAssetPath(string path)
    {
        return !string.IsNullOrEmpty(path) && string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);
    }

    private static Color GetChannelPreviewColor(int channelIndex)
    {
        return channelIndex switch
        {
            0 => new Color(1.0f, 0.35f, 0.35f, 0.9f),
            1 => new Color(0.35f, 1.0f, 0.45f, 0.9f),
            2 => new Color(0.35f, 0.65f, 1.0f, 0.9f),
            _ => new Color(1.0f, 0.9f, 0.35f, 0.9f)
        };
    }

    private static float GetChannel01(Color32 pixel, int channelIndex)
    {
        return channelIndex switch
        {
            0 => pixel.r / 255.0f,
            1 => pixel.g / 255.0f,
            2 => pixel.b / 255.0f,
            _ => pixel.a / 255.0f
        };
    }

    private static bool SetChannel(ref Color32 pixel, int channelIndex, byte value)
    {
        switch (channelIndex)
        {
            case 0:
                if (pixel.r == value)
                    return false;

                pixel.r = value;
                return true;
            case 1:
                if (pixel.g == value)
                    return false;

                pixel.g = value;
                return true;
            case 2:
                if (pixel.b == value)
                    return false;

                pixel.b = value;
                return true;
            default:
                if (pixel.a == value)
                    return false;

                pixel.a = value;
                return true;
        }
    }
}
