using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class QianxiaRuntimeAssetInstanceCounterTests
{
    private Scene _testScene;
    private Scene _previousActiveScene;
    private string _tempAssetFolder;
    private Type _scannerType;
    private Type _optionsType;
    private Type _scopeType;
    private Type _assetStatsAnalyzerType;

    [SetUp]
    public void SetUp()
    {
        _scannerType = Type.GetType("QianxiaRuntimeAssetInstanceScanner, Assembly-CSharp");
        _optionsType = Type.GetType("QianxiaRuntimeAssetInstanceScanOptions, Assembly-CSharp");
        _scopeType = Type.GetType("QianxiaRuntimeAssetInstanceScanScope, Assembly-CSharp");
        _assetStatsAnalyzerType = Type.GetType("QianxiaAssetStatsAnalyzer, Assembly-CSharp-Editor");

        Assert.That(_scannerType, Is.Not.Null);
        Assert.That(_optionsType, Is.Not.Null);
        Assert.That(_scopeType, Is.Not.Null);
        Assert.That(_assetStatsAnalyzerType, Is.Not.Null);

        _previousActiveScene = SceneManager.GetActiveScene();
        _testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(_testScene);
    }

    [TearDown]
    public void TearDown()
    {
        if (_testScene.IsValid())
            EditorSceneManager.CloseScene(_testScene, true);

        if (_previousActiveScene.IsValid())
            EditorSceneManager.SetActiveScene(_previousActiveScene);

        if (!string.IsNullOrWhiteSpace(_tempAssetFolder))
            AssetDatabase.DeleteAsset(_tempAssetFolder);
    }

    [Test]
    public void Capture_AggregatesRepeatedMeshAndMaterialUsage()
    {
        GameObject cubeA = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(cubeA, _testScene);
        cubeA.name = "CubeA";

        GameObject cubeB = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(cubeB, _testScene);
        cubeB.name = "CubeB";

        Material sharedMaterial = cubeA.GetComponent<Renderer>().sharedMaterial;
        cubeB.GetComponent<Renderer>().sharedMaterial = sharedMaterial;

        object snapshot = CaptureSnapshot(includeInactiveRenderers: true);

        Assert.That(GetIntField(snapshot, "scannedSceneCount"), Is.EqualTo(1));
        Assert.That(GetIntField(snapshot, "rendererCount"), Is.EqualTo(2));
        Assert.That(AnyUsageCount(GetEnumerableField(snapshot, "meshes"), 2), Is.True);
        Assert.That(AnyUsageCount(GetEnumerableField(snapshot, "materials"), 2), Is.True);
    }

    [Test]
    public void Capture_SkipsInactiveRendererWhenConfigured()
    {
        GameObject activeCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(activeCube, _testScene);

        GameObject inactiveCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(inactiveCube, _testScene);
        inactiveCube.SetActive(false);

        object snapshot = CaptureSnapshot(includeInactiveRenderers: false);

        Assert.That(GetIntField(snapshot, "rendererCount"), Is.EqualTo(1));
        Assert.That(SumUsageCount(GetEnumerableField(snapshot, "meshes")), Is.EqualTo(1));
        Assert.That(SumUsageCount(GetEnumerableField(snapshot, "materials")), Is.EqualTo(1));
    }

    [Test]
    public void Capture_TracksPrefabInstancesByAssetPath()
    {
        _tempAssetFolder = $"Assets/Tests/Temp_RuntimeAssetCounter_{Guid.NewGuid():N}";
        string parentFolder = Path.GetDirectoryName(_tempAssetFolder)?.Replace('\\', '/') ?? "Assets/Tests";
        string folderName = Path.GetFileName(_tempAssetFolder);
        AssetDatabase.CreateFolder(parentFolder, folderName);

        string prefabPath = $"{_tempAssetFolder}/RuntimeAssetCounter.prefab";
        GameObject prefabRoot = new GameObject("RuntimeAssetCounter");
        GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.transform.SetParent(prefabRoot.transform, false);
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        UnityEngine.Object.DestroyImmediate(prefabRoot);

        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefabAsset, Is.Not.Null);

        PrefabUtility.InstantiatePrefab(prefabAsset, _testScene);
        PrefabUtility.InstantiatePrefab(prefabAsset, _testScene);

        object snapshot = CaptureSnapshot(includeInactiveRenderers: true);
        object prefabRecord = FindByAssetPath(GetEnumerableField(snapshot, "prefabs"), prefabPath);
        Assert.That(prefabRecord, Is.Not.Null);
        Assert.That(GetIntField(prefabRecord, "usageCount"), Is.EqualTo(2));
    }

    [Test]
    public void AnalyzePrefabByName_ReturnsMeshAndTextureCost()
    {
        _tempAssetFolder = $"Assets/Tests/Temp_PrefabLookup_{Guid.NewGuid():N}";
        string parentFolder = Path.GetDirectoryName(_tempAssetFolder)?.Replace('\\', '/') ?? "Assets/Tests";
        string folderName = Path.GetFileName(_tempAssetFolder);
        AssetDatabase.CreateFolder(parentFolder, folderName);

        Mesh mesh = new Mesh
        {
            name = "LookupTriangleMesh",
            vertices = new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f)
            },
            triangles = new[] { 0, 1, 2 }
        };
        mesh.RecalculateBounds();
        string meshPath = $"{_tempAssetFolder}/LookupTriangleMesh.asset";
        AssetDatabase.CreateAsset(mesh, meshPath);

        Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
        {
            name = "LookupTinyTexture"
        };
        string texturePath = $"{_tempAssetFolder}/LookupTinyTexture.asset";
        AssetDatabase.CreateAsset(texture, texturePath);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard") ??
            Shader.Find("Sprites/Default");
        Assert.That(shader, Is.Not.Null);

        Material material = new Material(shader)
        {
            name = "LookupMaterial"
        };
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);
        else if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", texture);

        string materialPath = $"{_tempAssetFolder}/LookupMaterial.mat";
        AssetDatabase.CreateAsset(material, materialPath);

        string prefabPath = $"{_tempAssetFolder}/LookupCostPrefab.prefab";
        GameObject prefabRoot = new GameObject("LookupCostPrefab");
        MeshFilter meshFilter = prefabRoot.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;
        MeshRenderer meshRenderer = prefabRoot.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        UnityEngine.Object.DestroyImmediate(prefabRoot);
        AssetDatabase.SaveAssets();

        MethodInfo lookupMethod = _assetStatsAnalyzerType.GetMethod("AnalyzePrefabByName", BindingFlags.Public | BindingFlags.Static);
        Assert.That(lookupMethod, Is.Not.Null);

        object result = lookupMethod.Invoke(null, new object[] { "LookupCostPrefab", null });
        Assert.That(GetIntField(result, "MatchCount"), Is.EqualTo(1));
        Assert.That(GetIntField(result, "ExactMatchCount"), Is.EqualTo(1));

        IList records = GetFieldValue(result, "Records") as IList;
        Assert.That(records, Is.Not.Null);
        Assert.That(records.Count, Is.EqualTo(1));

        object prefabRecord = records[0];
        Assert.That(GetIntField(prefabRecord, "uniqueMeshCount"), Is.EqualTo(1));
        Assert.That(GetIntField(prefabRecord, "uniqueTextureCount"), Is.EqualTo(1));
        Assert.That(GetLongField(prefabRecord, "totalTriangleCount"), Is.EqualTo(1L));
        Assert.That(GetLongField(prefabRecord, "totalVertexCount"), Is.EqualTo(3L));

        IList meshes = GetFieldValue(prefabRecord, "meshes") as IList;
        Assert.That(meshes, Is.Not.Null);
        Assert.That(meshes.Count, Is.EqualTo(1));
        Assert.That(GetLongField(meshes[0], "triangleCount"), Is.EqualTo(1L));
        Assert.That(GetLongField(meshes[0], "vertexCount"), Is.EqualTo(3L));

        IList textures = GetFieldValue(prefabRecord, "textures") as IList;
        Assert.That(textures, Is.Not.Null);
        Assert.That(textures.Count, Is.EqualTo(1));
        Assert.That(GetIntField(textures[0], "width"), Is.EqualTo(4));
        Assert.That(GetIntField(textures[0], "height"), Is.EqualTo(4));
    }

    private object CaptureSnapshot(bool includeInactiveRenderers)
    {
        object options = Activator.CreateInstance(_optionsType);
        SetField(options, "scanScope", Enum.Parse(_scopeType, "ActiveSceneOnly"));
        SetField(options, "includeInactiveRenderers", includeInactiveRenderers);
        SetField(options, "prefabBreakdownLimit", 4);

        MethodInfo captureMethod = _scannerType.GetMethod("Capture", BindingFlags.Public | BindingFlags.Static);
        Assert.That(captureMethod, Is.Not.Null);
        return captureMethod.Invoke(null, new[] { options });
    }

    private static IEnumerable GetEnumerableField(object instance, string fieldName)
    {
        return GetFieldValue(instance, fieldName) as IEnumerable;
    }

    private static object FindByAssetPath(IEnumerable records, string assetPath)
    {
        if (records == null)
            return null;

        foreach (object record in records)
        {
            string recordPath = GetFieldValue(record, "assetPath") as string;
            if (string.Equals(recordPath, assetPath, StringComparison.OrdinalIgnoreCase))
                return record;
        }

        return null;
    }

    private static bool AnyUsageCount(IEnumerable records, int expectedUsageCount)
    {
        if (records == null)
            return false;

        foreach (object record in records)
        {
            if (GetIntField(record, "usageCount") == expectedUsageCount)
                return true;
        }

        return false;
    }

    private static int SumUsageCount(IEnumerable records)
    {
        int total = 0;
        if (records == null)
            return total;

        foreach (object record in records)
            total += GetIntField(record, "usageCount");

        return total;
    }

    private static int GetIntField(object instance, string fieldName)
    {
        object value = GetFieldValue(instance, fieldName);
        return value is int intValue ? intValue : 0;
    }

    private static long GetLongField(object instance, string fieldName)
    {
        object value = GetFieldValue(instance, fieldName);
        return value is long longValue ? longValue : 0L;
    }

    private static object GetFieldValue(object instance, string fieldName)
    {
        Assert.That(instance, Is.Not.Null);
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null);
        return field.GetValue(instance);
    }

    private static void SetField(object instance, string fieldName, object value)
    {
        Assert.That(instance, Is.Not.Null);
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null);
        field.SetValue(instance, value);
    }
}
