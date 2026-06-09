using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public class RenderDocCaptureArtifactsTests
{
    private const string ExternalPlayerBuildStampFileName = "external-player-build-stamp.txt";

    [Test]
    public void SanitizeLabel_ReplacesInvalidCharactersAndCollapsesSeparators()
    {
        string sanitized = RenderDocCaptureArtifacts.SanitizeLabel(" Sample Scene / Crowd Hotspot #1 ");
        Assert.That(sanitized, Is.EqualTo("sample-scene-crowd-hotspot-1"));
    }

    [Test]
    public void BuildCaptureDirectoryName_IncludesTimestampSceneAndLabel()
    {
        DateTime timestamp = new DateTime(2026, 5, 4, 20, 30, 15);
        string directoryName = RenderDocCaptureArtifacts.BuildCaptureDirectoryName(timestamp, "SampleScene", "Crowd Hotspot");
        Assert.That(directoryName, Is.EqualTo("20260504_203015_samplescene-crowd-hotspot"));
    }

    [Test]
    public void ResolveArtifactsRoot_UsesAbsoluteOverrideWhenProvided()
    {
        string root = RenderDocCaptureArtifacts.ResolveArtifactsRoot(@"C:\captures\renderdoc", ".workspace/artifacts/ignored");
        Assert.That(root, Is.EqualTo(@"C:\captures\renderdoc"));
    }

    [Test]
    public void BuildAnalysisPrompt_ForArtifactInfo_OmitsRemovedCaptureSidecars()
    {
        RenderDocCaptureArtifactInfo artifactInfo = new RenderDocCaptureArtifactInfo
        {
            artifactDirectory = @"C:\captures\artifact",
            captureFilePath = @"C:\captures\artifact\capture_0001.rdc",
            screenshotFilePath = @"C:\captures\artifact\gameview.png",
            unityMaterialMetadataPath = @"C:\captures\artifact\unity_material_metadata.json",
            unityPrefabMetadataPath = @"C:\captures\artifact\unity_prefab_metadata.json",
            unityPrefabSignatureDatabasePath = @"C:\captures\artifact\unity_prefab_signatures.sqlite"
        };

        string prompt = RenderDocCaptureArtifacts.BuildAnalysisPrompt(artifactInfo, "分析该帧里人群系统，并给出优先优化建议。");

        Assert.That(prompt, Does.Not.Contain("capture_context.md"));
        Assert.That(prompt, Does.Not.Contain("gpu_pass_debug_map.json"));
        Assert.That(prompt, Does.Not.Contain("gpu_pass_bindings.json"));
        Assert.That(prompt, Does.Contain("unity_material_metadata.json"));
        Assert.That(prompt, Does.Contain("unity_prefab_metadata.json"));
        Assert.That(prompt, Does.Contain("unity_prefab_signatures.sqlite"));
        Assert.That(prompt, Does.Contain("[$renderdoc-cli]"));
        Assert.That(prompt, Does.Contain("rdoc-agent.cmd capture info"));
        Assert.That(prompt, Does.Contain("entity list"));
        Assert.That(prompt, Does.Contain("entity match-prefabs"));
        Assert.That(prompt, Does.Not.Contain("events inspect"));
        Assert.That(prompt, Does.Contain("qrenderdoc"));
        Assert.That(prompt, Does.Contain("representativeEventId"));
        Assert.That(prompt, Does.Contain("entityCount"));
        Assert.That(prompt, Does.Contain("entities[]"));
        Assert.That(prompt, Does.Contain("draw signature"));
        Assert.That(prompt, Does.Not.Contain("required unity prefab metadata"));
        Assert.That(prompt, Does.Not.Contain("--metadata"));
        Assert.That(prompt, Does.Contain("tools/renderdoc-cli/renderdoc_gpu_context_cli.py"));
        Assert.That(prompt, Does.Contain("tools/ai-debug-cli/crowd_ai_debug_query.py"));
        Assert.That(prompt, Does.Contain("crowd_ai_debug_*.jsonl"));
        Assert.That(prompt, Does.Contain("分析该帧里人群系统，并给出优先优化建议。"));
        Assert.That(prompt, Does.Not.Contain("RenderDoc MCP Bridge"));
        Assert.That(prompt, Does.Not.Contain("renderdoc_mcp_bridge_cli.py"));
        Assert.That(prompt, Does.Not.Contain("context json"));
    }

    [Test]
    public void TryLoadLatestArtifactInfo_PicksNewestCaptureFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RenderDocCaptureArtifactsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string olderDirectory = Path.Combine(tempRoot, "20260505_100000_scene-old");
            string newerDirectory = Path.Combine(tempRoot, "20260505_100100_scene-new");
            Directory.CreateDirectory(olderDirectory);
            Directory.CreateDirectory(newerDirectory);

            string olderCapturePath = Path.Combine(olderDirectory, "capture_0001.rdc");
            string newerCapturePath = Path.Combine(newerDirectory, "capture_0001.rdc");
            File.WriteAllText(olderCapturePath, "older");
            File.WriteAllText(newerCapturePath, "newer");
            File.SetLastWriteTimeUtc(olderCapturePath, new DateTime(2026, 5, 5, 2, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newerCapturePath, new DateTime(2026, 5, 5, 2, 1, 0, DateTimeKind.Utc));

            bool success = RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(tempRoot, out RenderDocCaptureArtifactInfo artifactInfo);

            Assert.That(success, Is.True);
            Assert.That(artifactInfo, Is.Not.Null);
            Assert.That(artifactInfo.captureFilePath, Is.EqualTo(newerCapturePath));
            Assert.That(artifactInfo.screenshotFilePath, Is.EqualTo(Path.Combine(newerDirectory, RenderDocCaptureArtifacts.ScreenshotFileName)));
            Assert.That(artifactInfo.unityMaterialMetadataPath, Is.EqualTo(Path.Combine(newerDirectory, RenderDocCaptureArtifacts.UnityMaterialMetadataFileName)));
            Assert.That(artifactInfo.unityPrefabMetadataPath, Is.EqualTo(Path.Combine(newerDirectory, RenderDocCaptureArtifacts.UnityPrefabMetadataFileName)));
            Assert.That(artifactInfo.unityPrefabSignatureDatabasePath, Is.EqualTo(Path.Combine(newerDirectory, RenderDocCaptureArtifacts.UnityPrefabSignatureDatabaseFileName)));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void ExternalPlayerAutoMode_UsesBuildStampToForceFreshRecord()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CrowdVatExternalPlayerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string playerPath = Path.Combine(tempRoot, "QianxiaRenderDocD3D12.exe");
            string buildStampPath = Path.Combine(tempRoot, ExternalPlayerBuildStampFileName);
            string traceDirectory = Path.Combine(tempRoot, "repro-traces");
            string tracePath = Path.Combine(traceDirectory, "crowd_ai_repro_20260515_104815.json");

            Directory.CreateDirectory(traceDirectory);
            File.WriteAllText(playerPath, "player");
            File.WriteAllText(buildStampPath, "2026-05-16T00:00:00.0000000Z");
            File.WriteAllText(tracePath, "trace");
            File.SetLastWriteTimeUtc(tracePath, new DateTime(2026, 5, 15, 2, 48, 15, DateTimeKind.Utc));

            object session = ResolveExternalPlayerAutoSession(
                playerPath,
                buildStampPath,
                traceDirectory,
                string.Empty);

            Assert.That(GetExternalPlayerSessionModeName(session), Is.EqualTo("Record"));
            Assert.That(GetExternalPlayerSessionTracePath(session), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void ExternalPlayerAutoMode_ReplaysOnlyWhenTraceIsNewerThanBuildStamp()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "CrowdVatExternalPlayerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string playerPath = Path.Combine(tempRoot, "QianxiaRenderDocD3D12.exe");
            string buildStampPath = Path.Combine(tempRoot, ExternalPlayerBuildStampFileName);
            string traceDirectory = Path.Combine(tempRoot, "repro-traces");
            string tracePath = Path.Combine(traceDirectory, "crowd_ai_repro_20260516_101500.json");

            Directory.CreateDirectory(traceDirectory);
            File.WriteAllText(playerPath, "player");
            File.WriteAllText(buildStampPath, "2026-05-16T00:00:00.0000000Z");
            File.WriteAllText(tracePath, "trace");
            File.SetLastWriteTimeUtc(tracePath, new DateTime(2026, 5, 16, 2, 15, 0, DateTimeKind.Utc));

            object session = ResolveExternalPlayerAutoSession(
                playerPath,
                buildStampPath,
                traceDirectory,
                string.Empty);

            Assert.That(GetExternalPlayerSessionModeName(session), Is.EqualTo("Replay"));
            Assert.That(GetExternalPlayerSessionTracePath(session), Is.EqualTo(tracePath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void UnityPrefabMetadataExporter_WritesPrefabRendererLodMeshMaterialTextureAndShadowFlags()
    {
        const string testFolder = "Assets/ANGRY MESH/__RenderDocPrefabMetadataTests";
        string outputPath = Path.Combine(Path.GetTempPath(), "unity_prefab_metadata_" + Guid.NewGuid().ToString("N") + ".json");
        AssetDatabase.DeleteAsset(testFolder);
        EnsureAssetFolder(testFolder);

        try
        {
            string texturePath = testFolder + "/T_TestTree_Summer.asset";
            string materialPath = testFolder + "/M_TestTree_Summer.mat";
            string meshPath = testFolder + "/SM_TestTree_LOD0.asset";
            string prefabPath = testFolder + "/P_TestTree_Summer.prefab";

            Texture2D texture = new Texture2D(4, 4);
            texture.name = "T_TestTree_Summer";
            AssetDatabase.CreateAsset(texture, texturePath);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null, "Test requires a built-in or URP lit shader.");

            Material material = new Material(shader);
            material.name = "M_TestTree_Summer";
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            else if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            AssetDatabase.CreateAsset(material, materialPath);

            Mesh mesh = new Mesh();
            mesh.name = "SM_TestTree_LOD0";
            mesh.vertices = new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            AssetDatabase.CreateAsset(mesh, meshPath);

            GameObject prefabRoot = new GameObject("P_TestTree_Summer");
            GameObject nested = new GameObject("Nested");
            nested.transform.SetParent(prefabRoot.transform, false);
            GameObject rendererObject = new GameObject("Renderer");
            rendererObject.transform.SetParent(nested.transform, false);
            MeshFilter meshFilter = rendererObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            MeshRenderer meshRenderer = rendererObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = false;
            LODGroup lodGroup = prefabRoot.AddComponent<LODGroup>();
            lodGroup.SetLODs(new[] { new LOD(0.5f, new Renderer[] { meshRenderer }) });
            lodGroup.RecalculateBounds();

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            UnityEngine.Object.DestroyImmediate(prefabRoot);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Type exporterType = ResolveLoadedType("RenderDocUnityPrefabMetadataExporter");
            MethodInfo exportMethod = exporterType.GetMethod("ExportAngryMeshPrefabs", BindingFlags.Public | BindingFlags.Static);
            Assert.That(exportMethod, Is.Not.Null);

            int prefabCount = (int)exportMethod.Invoke(null, new object[] { outputPath });
            string json = File.ReadAllText(outputPath);

            Assert.That(prefabCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(json, Does.Contain("\"prefabs\""));
            Assert.That(json, Does.Contain("\"name\": \"P_TestTree_Summer\""));
            Assert.That(json, Does.Contain("\"assetPath\": \"Assets/ANGRY MESH/__RenderDocPrefabMetadataTests/P_TestTree_Summer.prefab\""));
            Assert.That(json, Does.Contain("\"rendererPath\": \"P_TestTree_Summer/Nested/Renderer\""));
            Assert.That(json, Does.Contain("\"lodIndex\": 0"));
            Assert.That(json, Does.Contain("\"name\": \"SM_TestTree_LOD0\""));
            Assert.That(json, Does.Contain("\"guid\": \"" + AssetDatabase.AssetPathToGUID(meshPath) + "\""));
            Assert.That(json, Does.Contain("\"name\": \"M_TestTree_Summer\""));
            Assert.That(json, Does.Contain("\"name\": \"T_TestTree_Summer\""));
            Assert.That(json, Does.Contain("\"shadowCastingMode\": \"On\""));
            Assert.That(json, Does.Contain("\"receiveShadows\": false"));
            Assert.That(json, Does.Contain("\"castsShadows\": true"));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            AssetDatabase.DeleteAsset(testFolder);
        }
    }

    [Test]
    public void UnityPrefabSignatureExporter_WritesCurrentScenePrefabInstancesOnly()
    {
        const string testFolder = "Assets/ANGRY MESH/__RenderDocScenePrefabSignatureTests";
        string outputPath = Path.Combine(Path.GetTempPath(), "unity_prefab_signatures_" + Guid.NewGuid().ToString("N") + ".sqlite");
        AssetDatabase.DeleteAsset(testFolder);
        EnsureAssetFolder(testFolder);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        try
        {
            string texturePath = testFolder + "/T_SceneTree_Summer.asset";
            string materialPath = testFolder + "/M_SceneTree_Summer.mat";
            string meshPath = testFolder + "/SM_SceneTree_LOD0.asset";
            string prefabPath = testFolder + "/P_SceneTree_Summer.prefab";
            string unusedPrefabPath = testFolder + "/P_UnusedSceneTree_Summer.prefab";

            Texture2D texture = new Texture2D(4, 4);
            texture.name = "T_SceneTree_Summer";
            AssetDatabase.CreateAsset(texture, texturePath);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null, "Test requires a built-in or URP lit shader.");

            Material material = new Material(shader);
            material.name = "M_SceneTree_Summer";
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            else if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            AssetDatabase.CreateAsset(material, materialPath);

            Mesh mesh = new Mesh();
            mesh.name = "SM_SceneTree_LOD0";
            mesh.vertices = new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            AssetDatabase.CreateAsset(mesh, meshPath);

            GameObject prefabRoot = CreateRenderablePrefabRoot("P_SceneTree_Summer", mesh, material);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            UnityEngine.Object.DestroyImmediate(prefabRoot);

            GameObject unusedPrefabRoot = CreateRenderablePrefabRoot("P_UnusedSceneTree_Summer", mesh, material);
            PrefabUtility.SaveAsPrefabAsset(unusedPrefabRoot, unusedPrefabPath);
            UnityEngine.Object.DestroyImmediate(unusedPrefabRoot);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject firstInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            GameObject secondInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.That(firstInstance, Is.Not.Null);
            Assert.That(secondInstance, Is.Not.Null);
            firstInstance.name = "SceneInstanceA";
            secondInstance.name = "SceneInstanceB";

            Type exporterType = ResolveLoadedType("RenderDocUnityPrefabMetadataExporter");
            MethodInfo exportMethod = exporterType.GetMethod("ExportAngryMeshPrefabSignatures", BindingFlags.Public | BindingFlags.Static);
            Assert.That(exportMethod, Is.Not.Null);

            int rowCount = (int)exportMethod.Invoke(null, new object[] { outputPath });
            string sqliteText = Encoding.UTF8.GetString(File.ReadAllBytes(outputPath));

            Assert.That(rowCount, Is.EqualTo(2));
            Assert.That(sqliteText, Does.Contain("scene_instance_path"));
            Assert.That(sqliteText, Does.Contain("scene_renderer_path"));
            Assert.That(sqliteText, Does.Contain("Assets/ANGRY MESH/__RenderDocScenePrefabSignatureTests/P_SceneTree_Summer.prefab"));
            Assert.That(sqliteText, Does.Contain("SceneInstanceA"));
            Assert.That(sqliteText, Does.Contain("SceneInstanceB"));
            Assert.That(sqliteText, Does.Contain("SceneInstanceA/Renderer"));
            Assert.That(sqliteText, Does.Contain("SceneInstanceB/Renderer"));
            Assert.That(sqliteText, Does.Not.Contain("P_UnusedSceneTree_Summer.prefab"));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(testFolder);
        }
    }

    private static object ResolveExternalPlayerAutoSession(
        string playerPath,
        string buildStampPath,
        string traceDirectory,
        string explicitTracePath)
    {
        Type bootstrapType = Type.GetType("CrowdVatAiDebugExternalPlayerBootstrap, Assembly-CSharp");
        Assert.That(bootstrapType, Is.Not.Null, "Failed to load CrowdVatAiDebugExternalPlayerBootstrap from Assembly-CSharp.");

        MethodInfo resolveAutoSessionMethod = bootstrapType.GetMethod(
            "ResolveAutoSession",
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(resolveAutoSessionMethod, Is.Not.Null, "Failed to find ResolveAutoSession on CrowdVatAiDebugExternalPlayerBootstrap.");

        return resolveAutoSessionMethod.Invoke(
            null,
            new object[]
            {
                playerPath,
                buildStampPath,
                traceDirectory,
                explicitTracePath
            });
    }

    private static string GetExternalPlayerSessionModeName(object session)
    {
        Assert.That(session, Is.Not.Null);
        FieldInfo modeField = session.GetType().GetField("mode", BindingFlags.Public | BindingFlags.Instance);
        Assert.That(modeField, Is.Not.Null);

        object modeValue = modeField.GetValue(session);
        Assert.That(modeValue, Is.Not.Null);
        return modeValue.ToString();
    }

    private static string GetExternalPlayerSessionTracePath(object session)
    {
        Assert.That(session, Is.Not.Null);
        FieldInfo tracePathField = session.GetType().GetField("tracePath", BindingFlags.Public | BindingFlags.Instance);
        Assert.That(tracePathField, Is.Not.Null);

        return (string)tracePathField.GetValue(session);
    }

    [Test]
    public void GpuPassDebugRegistry_BuildJson_KeysEntriesByPassName()
    {
        GpuPassDebugInfo[] infos =
        {
            new GpuPassDebugInfo
            {
                passName = "Crowd.Update.Animation",
                cppFile = "CrowdSystem.cpp",
                cppFunction = "CrowdSystem::UpdateAnimation",
                shaderFile = "CrowdAnimCS.hlsl",
                shaderEntry = "CSMain",
                threadGroupX = 64,
                threadGroupY = 1,
                threadGroupZ = 1,
                passType = "compute",
                dispatchKind = "direct"
            }
        };

        string json = GpuPassDebugRegistry.BuildJson(infos, new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(json, Does.Contain("\"Crowd.Update.Animation\""));
        Assert.That(json, Does.Contain("\"cpp_file\": \"CrowdSystem.cpp\""));
        Assert.That(json, Does.Contain("\"thread_group\": [64, 1, 1]"));
    }

    [Test]
    public void GpuPassBindingDebugRegistry_BuildJson_GroupsBindingsAndResources()
    {
        GpuPassBindingDebugRegistry.Clear();

        GpuPassBindingDebugRegistry.RecordBufferBinding(
            "Crowd.QueryGrid.Build",
            "_GridCounterBuffer",
            "gridCounter",
            "RWStructuredBuffer<uint>",
            GpuPassBindingAccess.Uav,
            4096,
            4,
            "scatter/interlocked write by spatial grid cell");

        GpuPassBindingDebugRegistry.RecordBufferBinding(
            "Crowd.Simulation.SolveCrowd",
            "_GridCounterBuffer",
            "gridCounter",
            "StructuredBuffer<uint>",
            GpuPassBindingAccess.Srv,
            4096,
            4,
            "random lookup by spatial grid cell");

        string json = GpuPassBindingDebugRegistry.BuildJson(new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(json, Does.Contain("\"Crowd.QueryGrid.Build\""));
        Assert.That(json, Does.Contain("\"shader_name\": \"_GridCounterBuffer\""));
        Assert.That(json, Does.Contain("\"runtime_name\": \"gridCounter\""));
        Assert.That(json, Does.Contain("\"stride_bytes\": 4"));
        Assert.That(json, Does.Contain("\"producer\": \"Crowd.QueryGrid.Build\""));
        Assert.That(json, Does.Contain("\"Crowd.Simulation.SolveCrowd\""));

        GpuPassBindingDebugRegistry.Clear();
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        string[] parts = assetFolder.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
    }

    private static GameObject CreateRenderablePrefabRoot(string name, Mesh mesh, Material material)
    {
        GameObject prefabRoot = new GameObject(name);
        GameObject rendererObject = new GameObject("Renderer");
        rendererObject.transform.SetParent(prefabRoot.transform, false);
        MeshFilter meshFilter = rendererObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;
        MeshRenderer meshRenderer = rendererObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        LODGroup lodGroup = prefabRoot.AddComponent<LODGroup>();
        lodGroup.SetLODs(new[] { new LOD(0.5f, new Renderer[] { meshRenderer }) });
        lodGroup.RecalculateBounds();
        return prefabRoot;
    }

    private static Type ResolveLoadedType(string typeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < assemblies.Length; index++)
        {
            Type type = assemblies[index].GetType(typeName);
            if (type != null)
                return type;
        }

        Assert.Fail("Failed to resolve loaded type: " + typeName);
        return null;
    }
}
