using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class RenderDocUnityMaterialMetadataExporter
{
    public const string AngryMeshShaderRoot = "Assets/ANGRY MESH";
    public const string MaterialSearchRoot = "Assets";

    [Serializable]
    private sealed class MetadataPayload
    {
        public int version = 1;
        public string generatedAtUtc;
        public string sourceRoot;
        public string shaderRoot;
        public string materialSearchRoot;
        public MaterialRecord[] materials;
    }

    [Serializable]
    private sealed class MaterialRecord
    {
        public string materialId;
        public string name;
        public string assetPath;
        public string shader;
        public string shaderPath;
        public string[] textures;
    }

    [MenuItem("Tools/Qianxia/RenderDoc Capture/Export ANGRY MESH Material Metadata")]
    public static void ExportAngryMeshMaterialsToLatestCapture()
    {
        string artifactsRoot = RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            string.Empty,
            RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot);

        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(artifactsRoot, out RenderDocCaptureArtifactInfo artifactInfo))
        {
            Debug.LogWarning("No RenderDoc capture is available for Unity material metadata export.");
            return;
        }

        int materialCount = ExportAngryMeshMaterials(artifactInfo.unityMaterialMetadataPath);
        Debug.Log(string.Format(
            "Exported {0} ANGRY MESH material metadata record(s) to {1}.",
            materialCount,
            artifactInfo.unityMaterialMetadataPath));
    }

    public static void ExportAngryMeshMaterialsForBatchmode()
    {
        try
        {
            string outputPath = ResolveOutputPathFromCommandLine();
            int materialCount = ExportAngryMeshMaterials(outputPath);
            Debug.Log(string.Format(
                "Exported {0} ANGRY MESH material metadata record(s) to {1}.",
                materialCount,
                outputPath));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static int ExportAngryMeshMaterials(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("An output path is required.", nameof(outputPath));

        List<MaterialRecord> records = BuildAngryMeshMaterialRecords();
        MetadataPayload payload = new MetadataPayload
        {
            generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            sourceRoot = AngryMeshShaderRoot,
            shaderRoot = AngryMeshShaderRoot,
            materialSearchRoot = MaterialSearchRoot,
            materials = records.ToArray()
        };

        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, JsonUtility.ToJson(payload, true) + Environment.NewLine, new UTF8Encoding(false));
        return records.Count;
    }

    private static List<MaterialRecord> BuildAngryMeshMaterialRecords()
    {
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialSearchRoot });
        List<MaterialRecord> records = new List<MaterialRecord>(materialGuids.Length);

        for (int index = 0; index < materialGuids.Length; index++)
        {
            string guid = materialGuids[index];
            string materialPath = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null || material.shader == null)
                continue;

            string shaderPath = AssetDatabase.GetAssetPath(material.shader);
            if (!IsUnderAngryMeshRoot(shaderPath))
                continue;

            records.Add(new MaterialRecord
            {
                materialId = "mat:" + guid,
                name = material.name,
                assetPath = materialPath,
                shader = material.shader.name,
                shaderPath = shaderPath,
                textures = CollectTextureNames(material)
            });
        }

        records.Sort((left, right) => string.Compare(left.assetPath, right.assetPath, StringComparison.OrdinalIgnoreCase));
        return records;
    }

    private static string[] CollectTextureNames(Material material)
    {
        List<string> names = new List<string>();
        HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
        int propertyCount = ShaderUtil.GetPropertyCount(material.shader);

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (ShaderUtil.GetPropertyType(material.shader, propertyIndex) != ShaderUtil.ShaderPropertyType.TexEnv)
                continue;

            string propertyName = ShaderUtil.GetPropertyName(material.shader, propertyIndex);
            Texture texture = material.GetTexture(propertyName);
            if (texture == null || !found.Add(texture.name))
                continue;

            names.Add(texture.name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names.ToArray();
    }

    private static bool IsUnderAngryMeshRoot(string assetPath)
    {
        return !string.IsNullOrEmpty(assetPath) &&
            assetPath.StartsWith(AngryMeshShaderRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveOutputPathFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "-unity-material-metadata-output", StringComparison.Ordinal) ||
                string.Equals(args[index], "--unity-material-metadata-output", StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        string artifactsRoot = RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            string.Empty,
            RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot);

        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(artifactsRoot, out RenderDocCaptureArtifactInfo artifactInfo))
            throw new InvalidOperationException("No RenderDoc capture is available for Unity material metadata export.");

        return artifactInfo.unityMaterialMetadataPath;
    }
}

public static class RenderDocUnityPrefabMetadataExporter
{
    public const string AngryMeshPrefabRoot = "Assets/ANGRY MESH";
    private const string PrefabSignatureTableName = "prefab_draw_signatures";
    private static readonly Regex LodTokenRegex = new Regex(
        "(?i)(?:^|[_\\-\\s])lod\\s*\\d+(?:$|[_\\-\\s])",
        RegexOptions.Compiled);

    [Serializable]
    private sealed class MetadataPayload
    {
        public int version = 1;
        public string generatedAtUtc;
        public string sourceRoot;
        public string prefabRoot;
        public PrefabRecord[] prefabs;
    }

    [Serializable]
    private sealed class PrefabRecord
    {
        public string prefabId;
        public string guid;
        public string name;
        public string assetPath;
        public string scenePath;
        public string sceneName;
        public string sceneInstancePath;
        public string sceneInstanceName;
        public RendererRecord[] renderers;
    }

    [Serializable]
    private sealed class RendererRecord
    {
        public string rendererPath;
        public string sceneRendererPath;
        public string rendererType;
        public int lodIndex;
        public MeshRecord mesh;
        public MaterialSignature[] materials;
        public string shader;
        public string[] shaders;
        public string[] textureNames;
        public string shadowCastingMode;
        public bool receiveShadows;
        public bool castsShadows;
    }

    [Serializable]
    private sealed class MeshRecord
    {
        public string name;
        public string guid;
        public long fileId;
    }

    [Serializable]
    private sealed class MaterialSignature
    {
        public string materialId;
        public string guid;
        public string name;
        public string assetPath;
        public string shader;
        public string shaderPath;
        public string[] textures;
        public TextureBindingRecord[] textureBindings;
    }

    [Serializable]
    private sealed class TextureBindingRecord
    {
        public string slot;
        public string name;
        public string assetPath;
    }

    [MenuItem("Tools/Qianxia/RenderDoc Capture/Export ANGRY MESH Prefab Metadata")]
    public static void ExportAngryMeshPrefabsToLatestCapture()
    {
        string artifactsRoot = RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            string.Empty,
            RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot);

        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(artifactsRoot, out RenderDocCaptureArtifactInfo artifactInfo))
        {
            Debug.LogWarning("No RenderDoc capture is available for Unity prefab metadata export.");
            return;
        }

        int prefabCount = ExportAngryMeshPrefabs(artifactInfo.unityPrefabMetadataPath);
        Debug.Log(string.Format(
            "Exported {0} ANGRY MESH prefab metadata record(s) to {1}.",
            prefabCount,
            artifactInfo.unityPrefabMetadataPath));
    }

    [MenuItem("Tools/Qianxia/RenderDoc Capture/Export ANGRY MESH Prefab SQLite")]
    public static void ExportAngryMeshPrefabSignaturesToLatestCapture()
    {
        string artifactsRoot = RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            string.Empty,
            RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot);

        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(artifactsRoot, out RenderDocCaptureArtifactInfo artifactInfo))
        {
            Debug.LogWarning("No RenderDoc capture is available for Unity prefab signature export.");
            return;
        }

        int rowCount = ExportAngryMeshPrefabSignatures(artifactInfo.unityPrefabSignatureDatabasePath);
        Debug.Log(string.Format(
            "Exported {0} ANGRY MESH prefab signature row(s) to {1}.",
            rowCount,
            artifactInfo.unityPrefabSignatureDatabasePath));
    }

    public static void ExportAngryMeshPrefabsForBatchmode()
    {
        try
        {
            string outputPath = ResolveOutputPathFromCommandLine();
            int prefabCount = ExportAngryMeshPrefabs(outputPath);
            Debug.Log(string.Format(
                "Exported {0} ANGRY MESH prefab metadata record(s) to {1}.",
                prefabCount,
                outputPath));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void ExportAngryMeshPrefabSignaturesForBatchmode()
    {
        try
        {
            string outputPath = ResolveSignatureDatabaseOutputPathFromCommandLine();
            int rowCount = ExportAngryMeshPrefabSignatures(outputPath);
            Debug.Log(string.Format(
                "Exported {0} ANGRY MESH prefab signature row(s) to {1}.",
                rowCount,
                outputPath));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static int ExportAngryMeshPrefabs(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("An output path is required.", nameof(outputPath));

        List<PrefabRecord> records = BuildAngryMeshPrefabRecords();
        WritePrefabMetadata(outputPath, records);
        return records.Count;
    }

    public static int ExportAngryMeshPrefabSignatures(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("An output path is required.", nameof(outputPath));

        List<PrefabRecord> records = BuildAngryMeshScenePrefabInstanceRecords();
        WritePrefabSignatureDatabase(outputPath, records);
        return CountPrefabSignatureRows(records);
    }

    public static int ExportAngryMeshPrefabsAndSignatures(string metadataOutputPath, string signatureDatabaseOutputPath)
    {
        if (string.IsNullOrWhiteSpace(metadataOutputPath))
            throw new ArgumentException("A metadata output path is required.", nameof(metadataOutputPath));
        if (string.IsNullOrWhiteSpace(signatureDatabaseOutputPath))
            throw new ArgumentException("A signature database output path is required.", nameof(signatureDatabaseOutputPath));

        List<PrefabRecord> metadataRecords = BuildAngryMeshPrefabRecords();
        List<PrefabRecord> signatureRecords = BuildAngryMeshScenePrefabInstanceRecords();
        WritePrefabMetadata(metadataOutputPath, metadataRecords);
        WritePrefabSignatureDatabase(signatureDatabaseOutputPath, signatureRecords);
        return metadataRecords.Count;
    }

    private static void WritePrefabMetadata(string outputPath, List<PrefabRecord> records)
    {
        MetadataPayload payload = new MetadataPayload
        {
            generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            sourceRoot = AngryMeshPrefabRoot,
            prefabRoot = AngryMeshPrefabRoot,
            prefabs = records.ToArray()
        };

        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, JsonUtility.ToJson(payload, true) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static List<PrefabRecord> BuildAngryMeshPrefabRecords()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { AngryMeshPrefabRoot });
        List<PrefabRecord> records = new List<PrefabRecord>(prefabGuids.Length);

        for (int index = 0; index < prefabGuids.Length; index++)
        {
            string guid = prefabGuids[index];
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                continue;

            RendererRecord[] renderers = BuildRendererRecords(prefab);
            if (renderers.Length == 0)
                continue;

            records.Add(new PrefabRecord
            {
                prefabId = "prefab:" + guid,
                guid = guid,
                name = prefab.name,
                assetPath = prefabPath,
                renderers = renderers
            });
        }

        records.Sort((left, right) => string.Compare(left.assetPath, right.assetPath, StringComparison.OrdinalIgnoreCase));
        return records;
    }

    private static List<PrefabRecord> BuildAngryMeshScenePrefabInstanceRecords()
    {
        List<PrefabRecord> records = new List<PrefabRecord>();
        HashSet<int> visitedInstanceRoots = new HashSet<int>();

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
                continue;

            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
            {
                Transform[] transforms = rootObjects[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    Transform transform = transforms[transformIndex];
                    if (transform == null)
                        continue;

                    GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject);
                    if (instanceRoot == null || instanceRoot != transform.gameObject)
                        continue;

                    int instanceId = instanceRoot.GetInstanceID();
                    if (!visitedInstanceRoots.Add(instanceId))
                        continue;

                    string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
                    if (!IsUnderAssetRoot(prefabPath, AngryMeshPrefabRoot))
                        continue;

                    string guid = AssetDatabase.AssetPathToGUID(prefabPath);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    RendererRecord[] renderers = BuildSceneRendererRecords(instanceRoot, prefab);
                    if (renderers.Length == 0)
                        continue;

                    records.Add(new PrefabRecord
                    {
                        prefabId = "scene-prefab:" + SceneIdentifier(scene) + ":" + BuildSceneObjectPath(instanceRoot),
                        guid = guid,
                        name = prefab != null ? prefab.name : Path.GetFileNameWithoutExtension(prefabPath),
                        assetPath = prefabPath,
                        scenePath = scene.path ?? string.Empty,
                        sceneName = scene.name ?? string.Empty,
                        sceneInstancePath = BuildSceneObjectPath(instanceRoot),
                        sceneInstanceName = instanceRoot.name,
                        renderers = renderers
                    });
                }
            }
        }

        records.Sort((left, right) =>
        {
            int pathCompare = string.Compare(left.assetPath, right.assetPath, StringComparison.OrdinalIgnoreCase);
            if (pathCompare != 0)
                return pathCompare;

            return string.Compare(left.sceneInstancePath, right.sceneInstancePath, StringComparison.OrdinalIgnoreCase);
        });
        return records;
    }

    private static RendererRecord[] BuildRendererRecords(GameObject prefab)
    {
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        Dictionary<Renderer, int> lodIndices = BuildLodIndexMap(prefab);
        List<RendererRecord> records = new List<RendererRecord>(renderers.Length);

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null)
                continue;

            Mesh mesh = ResolveRendererMesh(renderer);
            if (mesh == null)
                continue;

            int lodIndex = -1;
            if (lodIndices.TryGetValue(renderer, out int resolvedLodIndex))
                lodIndex = resolvedLodIndex;

            MaterialSignature[] materialSignatures = BuildMaterialSignatures(renderer.sharedMaterials);
            string[] shaderNames = CollectShaderNames(materialSignatures);
            records.Add(new RendererRecord
            {
                rendererPath = BuildRendererPath(prefab.transform, renderer.transform),
                rendererType = renderer.GetType().Name,
                lodIndex = lodIndex,
                mesh = BuildMeshRecord(mesh),
                materials = materialSignatures,
                shader = shaderNames.Length > 0 ? shaderNames[0] : string.Empty,
                shaders = shaderNames,
                textureNames = CollectTextureNames(materialSignatures),
                shadowCastingMode = renderer.shadowCastingMode.ToString(),
                receiveShadows = renderer.receiveShadows,
                castsShadows = renderer.shadowCastingMode != ShadowCastingMode.Off
            });
        }

        records.Sort((left, right) => string.Compare(left.rendererPath, right.rendererPath, StringComparison.OrdinalIgnoreCase));
        return records.ToArray();
    }

    private static RendererRecord[] BuildSceneRendererRecords(GameObject instanceRoot, GameObject prefab)
    {
        Renderer[] renderers = instanceRoot.GetComponentsInChildren<Renderer>(true);
        Dictionary<Renderer, int> lodIndices = BuildLodIndexMap(instanceRoot);
        List<RendererRecord> records = new List<RendererRecord>(renderers.Length);

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null)
                continue;

            Mesh mesh = ResolveRendererMesh(renderer);
            if (mesh == null)
                continue;

            int lodIndex = -1;
            if (lodIndices.TryGetValue(renderer, out int resolvedLodIndex))
                lodIndex = resolvedLodIndex;

            MaterialSignature[] materialSignatures = BuildMaterialSignatures(renderer.sharedMaterials);
            string[] shaderNames = CollectShaderNames(materialSignatures);
            records.Add(new RendererRecord
            {
                rendererPath = BuildPrefabRendererPath(instanceRoot, prefab, renderer),
                sceneRendererPath = BuildSceneObjectPath(renderer.gameObject),
                rendererType = renderer.GetType().Name,
                lodIndex = lodIndex,
                mesh = BuildMeshRecord(mesh),
                materials = materialSignatures,
                shader = shaderNames.Length > 0 ? shaderNames[0] : string.Empty,
                shaders = shaderNames,
                textureNames = CollectTextureNames(materialSignatures),
                shadowCastingMode = renderer.shadowCastingMode.ToString(),
                receiveShadows = renderer.receiveShadows,
                castsShadows = renderer.shadowCastingMode != ShadowCastingMode.Off
            });
        }

        records.Sort((left, right) => string.Compare(left.sceneRendererPath, right.sceneRendererPath, StringComparison.OrdinalIgnoreCase));
        return records.ToArray();
    }

    private static Dictionary<Renderer, int> BuildLodIndexMap(GameObject prefab)
    {
        LODGroup[] lodGroups = prefab.GetComponentsInChildren<LODGroup>(true);
        Dictionary<Renderer, int> map = new Dictionary<Renderer, int>();
        for (int groupIndex = 0; groupIndex < lodGroups.Length; groupIndex++)
        {
            LODGroup lodGroup = lodGroups[groupIndex];
            if (lodGroup == null)
                continue;

            LOD[] lods = lodGroup.GetLODs();
            for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
            {
                Renderer[] renderers = lods[lodIndex].renderers;
                if (renderers == null)
                    continue;

                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (renderer == null || map.ContainsKey(renderer))
                        continue;

                    map.Add(renderer, lodIndex);
                }
            }
        }

        return map;
    }

    private static Mesh ResolveRendererMesh(Renderer renderer)
    {
        SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
        if (skinnedMeshRenderer != null)
            return skinnedMeshRenderer.sharedMesh;

        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        return meshFilter != null ? meshFilter.sharedMesh : null;
    }

    private static MeshRecord BuildMeshRecord(Mesh mesh)
    {
        string guid = string.Empty;
        long fileId = 0L;
        if (mesh != null)
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out guid, out fileId);

        return new MeshRecord
        {
            name = mesh != null ? mesh.name : string.Empty,
            guid = guid,
            fileId = fileId
        };
    }

    private static MaterialSignature[] BuildMaterialSignatures(Material[] materials)
    {
        if (materials == null || materials.Length == 0)
            return Array.Empty<MaterialSignature>();

        List<MaterialSignature> records = new List<MaterialSignature>(materials.Length);
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < materials.Length; index++)
        {
            Material material = materials[index];
            if (material == null || !seen.Add(material.GetInstanceID().ToString()))
                continue;

            string guid = string.Empty;
            long fileId = 0L;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out guid, out fileId);
            string materialPath = AssetDatabase.GetAssetPath(material);
            string shaderPath = material.shader != null ? AssetDatabase.GetAssetPath(material.shader) : string.Empty;
            TextureBindingRecord[] textureBindings = BuildTextureBindings(material);
            records.Add(new MaterialSignature
            {
                materialId = !string.IsNullOrEmpty(guid) ? "mat:" + guid : "mat:" + material.GetInstanceID(),
                guid = guid,
                name = material.name,
                assetPath = materialPath,
                shader = material.shader != null ? material.shader.name : string.Empty,
                shaderPath = shaderPath,
                textures = CollectTextureNames(textureBindings),
                textureBindings = textureBindings
            });
        }

        records.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
        return records.ToArray();
    }

    private static TextureBindingRecord[] BuildTextureBindings(Material material)
    {
        if (material == null || material.shader == null)
            return Array.Empty<TextureBindingRecord>();

        List<TextureBindingRecord> bindings = new List<TextureBindingRecord>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (ShaderUtil.GetPropertyType(material.shader, propertyIndex) != ShaderUtil.ShaderPropertyType.TexEnv)
                continue;

            string propertyName = ShaderUtil.GetPropertyName(material.shader, propertyIndex);
            Texture texture = material.GetTexture(propertyName);
            if (texture == null)
                continue;

            string texturePath = AssetDatabase.GetAssetPath(texture);
            string key = propertyName + "|" + texture.name + "|" + texturePath;
            if (!seen.Add(key))
                continue;

            bindings.Add(new TextureBindingRecord
            {
                slot = propertyName,
                name = texture.name,
                assetPath = texturePath
            });
        }

        bindings.Sort((left, right) => string.Compare(left.slot, right.slot, StringComparison.OrdinalIgnoreCase));
        return bindings.ToArray();
    }

    private static string[] CollectShaderNames(MaterialSignature[] materials)
    {
        List<string> names = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < materials.Length; index++)
        {
            string shader = materials[index].shader;
            if (!string.IsNullOrWhiteSpace(shader) && seen.Add(shader))
                names.Add(shader);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names.ToArray();
    }

    private static string[] CollectTextureNames(MaterialSignature[] materials)
    {
        List<string> names = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
        {
            string[] textures = materials[materialIndex].textures;
            for (int textureIndex = 0; textureIndex < textures.Length; textureIndex++)
            {
                string texture = textures[textureIndex];
                if (!string.IsNullOrWhiteSpace(texture) && seen.Add(texture))
                    names.Add(texture);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names.ToArray();
    }

    private static string[] CollectTextureNames(TextureBindingRecord[] bindings)
    {
        List<string> names = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < bindings.Length; index++)
        {
            string texture = bindings[index].name;
            if (!string.IsNullOrWhiteSpace(texture) && seen.Add(texture))
                names.Add(texture);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names.ToArray();
    }

    private static string BuildRendererPath(Transform root, Transform rendererTransform)
    {
        if (root == null || rendererTransform == null)
            return string.Empty;

        List<string> parts = new List<string>();
        Transform current = rendererTransform;
        while (current != null)
        {
            parts.Add(current.name);
            if (current == root)
                break;

            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    private static string BuildPrefabRendererPath(GameObject instanceRoot, GameObject prefab, Renderer renderer)
    {
        if (renderer == null)
            return string.Empty;

        Renderer sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(renderer) as Renderer;
        if (sourceRenderer != null && prefab != null && IsChildOf(sourceRenderer.transform, prefab.transform))
            return BuildRendererPath(prefab.transform, sourceRenderer.transform);

        return BuildRendererPath(instanceRoot != null ? instanceRoot.transform : null, renderer.transform);
    }

    private static bool IsChildOf(Transform child, Transform root)
    {
        Transform current = child;
        while (current != null)
        {
            if (current == root)
                return true;

            current = current.parent;
        }

        return false;
    }

    private static bool IsUnderAssetRoot(string assetPath, string assetRoot)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(assetRoot))
            return false;

        string normalizedPath = assetPath.Replace('\\', '/').TrimEnd('/');
        string normalizedRoot = assetRoot.Replace('\\', '/').TrimEnd('/');
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSceneObjectPath(GameObject gameObject)
    {
        if (gameObject == null)
            return string.Empty;

        List<string> parts = new List<string>();
        Transform current = gameObject.transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    private static string SceneIdentifier(Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene.path))
            return scene.path;

        if (!string.IsNullOrWhiteSpace(scene.name))
            return scene.name;

        return "untitled-scene";
    }

    private static int CountPrefabSignatureRows(List<PrefabRecord> records)
    {
        int count = 0;
        for (int prefabIndex = 0; prefabIndex < records.Count; prefabIndex++)
        {
            RendererRecord[] renderers = records[prefabIndex].renderers;
            if (renderers != null)
                count += renderers.Length;
        }

        return count;
    }

    private static void WritePrefabSignatureDatabase(string outputPath, List<PrefabRecord> records)
    {
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        PrefabSignatureSqliteWriter.Write(outputPath, records);
    }

    private sealed class PrefabSignatureSqliteWriter
    {
        private const int PageSize = 32768;
        private const int SchemaRootPage = 1;
        private const int SignatureRootPage = 2;
        private const int SequenceRootPage = 3;

        private sealed class TableRow
        {
            public long rowId;
            public object[] values;
        }

        private sealed class SqlitePage
        {
            public int pageNumber;
            public long maxRowId;
            public byte[] bytes;
        }

        public static void Write(string outputPath, List<PrefabRecord> records)
        {
            List<TableRow> signatureRows = BuildSignatureRows(records);
            List<TableRow> sequenceRows = BuildSequenceRows(signatureRows.Count);
            Dictionary<int, byte[]> pages = new Dictionary<int, byte[]>();

            BuildSignatureTablePages(signatureRows, pages);
            pages[SequenceRootPage] = BuildTableLeafPage(
                SequenceRootPage,
                sequenceRows,
                0);
            pages[SchemaRootPage] = BuildSchemaPage(signatureRows.Count, MaxPageNumber(pages));

            int maxPage = MaxPageNumber(pages);
            using (FileStream stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                for (int pageNumber = 1; pageNumber <= maxPage; pageNumber++)
                {
                    if (pages.TryGetValue(pageNumber, out byte[] page))
                        stream.Write(page, 0, page.Length);
                    else
                        stream.Write(new byte[PageSize], 0, PageSize);
                }
            }
        }

        private static List<TableRow> BuildSignatureRows(List<PrefabRecord> records)
        {
            List<TableRow> rows = new List<TableRow>();
            long rowId = 1L;
            for (int prefabIndex = 0; prefabIndex < records.Count; prefabIndex++)
            {
                PrefabRecord prefab = records[prefabIndex];
                RendererRecord[] renderers = prefab.renderers;
                if (renderers == null)
                    continue;

                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    RendererRecord renderer = renderers[rendererIndex];
                    string meshName = renderer.mesh != null ? renderer.mesh.name ?? string.Empty : string.Empty;
                    rows.Add(new TableRow
                    {
                        rowId = rowId++,
                        values = new object[]
                        {
                            null,
                            prefab.assetPath ?? string.Empty,
                            prefab.name ?? string.Empty,
                            prefab.guid ?? string.Empty,
                            prefab.scenePath ?? string.Empty,
                            prefab.sceneName ?? string.Empty,
                            prefab.sceneInstancePath ?? string.Empty,
                            prefab.sceneInstanceName ?? string.Empty,
                            BuildPrefabLodKey(prefab, renderer),
                            renderer.rendererPath ?? string.Empty,
                            renderer.sceneRendererPath ?? string.Empty,
                            renderer.lodIndex >= 0 ? (object)(long)renderer.lodIndex : null,
                            meshName,
                            StripLodSuffixDisplay(meshName),
                            BuildJsonStringArray(renderer.textureNames)
                        }
                    });
                }
            }

            return rows;
        }

        private static List<TableRow> BuildSequenceRows(int signatureRowCount)
        {
            List<TableRow> rows = new List<TableRow>();
            if (signatureRowCount > 0)
            {
                rows.Add(new TableRow
                {
                    rowId = 1L,
                    values = new object[] { PrefabSignatureTableName, (long)signatureRowCount }
                });
            }

            return rows;
        }

        private static void BuildSignatureTablePages(List<TableRow> rows, Dictionary<int, byte[]> pages)
        {
            List<List<TableRow>> rowGroups = SplitRowsForLeafPages(rows);
            if (rowGroups.Count <= 1)
            {
                List<TableRow> rootRows = rowGroups.Count == 1 ? rowGroups[0] : new List<TableRow>();
                pages[SignatureRootPage] = BuildTableLeafPage(SignatureRootPage, rootRows, 0);
                return;
            }

            List<SqlitePage> childPages = new List<SqlitePage>();
            int nextPageNumber = 4;
            for (int groupIndex = 0; groupIndex < rowGroups.Count; groupIndex++)
            {
                List<TableRow> group = rowGroups[groupIndex];
                SqlitePage page = new SqlitePage
                {
                    pageNumber = nextPageNumber++,
                    maxRowId = group[group.Count - 1].rowId,
                    bytes = BuildTableLeafPage(nextPageNumber - 1, group, 0)
                };
                childPages.Add(page);
                pages[page.pageNumber] = page.bytes;
            }

            pages[SignatureRootPage] = BuildInteriorTablePage(SignatureRootPage, childPages);
        }

        private static List<List<TableRow>> SplitRowsForLeafPages(List<TableRow> rows)
        {
            List<List<TableRow>> groups = new List<List<TableRow>>();
            List<TableRow> current = new List<TableRow>();
            int usedBytes = 8;

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                TableRow row = rows[rowIndex];
                int cellLength = BuildTableLeafCell(row).Length;
                if (cellLength + 2 + 8 > PageSize)
                    throw new InvalidOperationException("A prefab signature row is too large for the SQLite page size.");

                if (current.Count > 0 && usedBytes + cellLength + 2 > PageSize)
                {
                    groups.Add(current);
                    current = new List<TableRow>();
                    usedBytes = 8;
                }

                current.Add(row);
                usedBytes += cellLength + 2;
            }

            if (current.Count > 0)
                groups.Add(current);

            return groups;
        }

        private static byte[] BuildSchemaPage(int signatureRowCount, int databasePageCount)
        {
            List<TableRow> rows = new List<TableRow>
            {
                new TableRow
                {
                    rowId = 1L,
                    values = new object[]
                    {
                        "table",
                        PrefabSignatureTableName,
                        PrefabSignatureTableName,
                        (long)SignatureRootPage,
                        "CREATE TABLE " + PrefabSignatureTableName + " (" +
                        "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                        "prefab_path TEXT, " +
                        "prefab_name TEXT, " +
                        "prefab_guid TEXT, " +
                        "scene_path TEXT, " +
                        "scene_name TEXT, " +
                        "scene_instance_path TEXT, " +
                        "scene_instance_name TEXT, " +
                        "prefab_lod_key TEXT, " +
                        "prefab_inner_path TEXT, " +
                        "scene_renderer_path TEXT, " +
                        "lod_index INTEGER, " +
                        "mesh_name TEXT, " +
                        "mesh_base_name TEXT, " +
                        "texture_names TEXT)"
                    }
                },
                new TableRow
                {
                    rowId = 2L,
                    values = new object[]
                    {
                        "table",
                        "sqlite_sequence",
                        "sqlite_sequence",
                        (long)SequenceRootPage,
                        "CREATE TABLE sqlite_sequence(name,seq)"
                    }
                }
            };

            byte[] page = BuildTableLeafPage(SchemaRootPage, rows, 100);
            WriteDatabaseHeader(page, databasePageCount);
            return page;
        }

        private static byte[] BuildTableLeafPage(int pageNumber, List<TableRow> rows, int pageHeaderOffset)
        {
            byte[] page = new byte[PageSize];
            List<byte[]> cells = new List<byte[]>(rows.Count);
            int cellContentOffset = PageSize;
            int btreeHeaderOffset = pageHeaderOffset;
            int cellPointerOffset = btreeHeaderOffset + 8;

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                byte[] cell = BuildTableLeafCell(rows[rowIndex]);
                cellContentOffset -= cell.Length;
                if (cellContentOffset < cellPointerOffset + (rowIndex + 1) * 2)
                    throw new InvalidOperationException("SQLite leaf page overflow while writing prefab signatures.");

                cells.Add(cell);
                WriteUInt16(page, cellPointerOffset + rowIndex * 2, cellContentOffset);
                Buffer.BlockCopy(cell, 0, page, cellContentOffset, cell.Length);
            }

            page[btreeHeaderOffset] = 0x0d;
            WriteUInt16(page, btreeHeaderOffset + 1, 0);
            WriteUInt16(page, btreeHeaderOffset + 3, rows.Count);
            WriteUInt16(page, btreeHeaderOffset + 5, cellContentOffset);
            page[btreeHeaderOffset + 7] = 0;
            return page;
        }

        private static byte[] BuildInteriorTablePage(int pageNumber, List<SqlitePage> childPages)
        {
            if (childPages.Count < 2)
                throw new InvalidOperationException("Interior SQLite page requires at least two child pages.");

            byte[] page = new byte[PageSize];
            int cellContentOffset = PageSize;
            int cellCount = childPages.Count - 1;
            int cellPointerOffset = 12;

            for (int childIndex = 0; childIndex < cellCount; childIndex++)
            {
                SqlitePage child = childPages[childIndex];
                List<byte> cell = new List<byte>();
                AppendUInt32(cell, (uint)child.pageNumber);
                AppendVarint(cell, (ulong)child.maxRowId);
                byte[] cellBytes = cell.ToArray();
                cellContentOffset -= cellBytes.Length;
                if (cellContentOffset < cellPointerOffset + (childIndex + 1) * 2)
                    throw new InvalidOperationException("SQLite interior page overflow while writing prefab signatures.");

                WriteUInt16(page, cellPointerOffset + childIndex * 2, cellContentOffset);
                Buffer.BlockCopy(cellBytes, 0, page, cellContentOffset, cellBytes.Length);
            }

            page[0] = 0x05;
            WriteUInt16(page, 1, 0);
            WriteUInt16(page, 3, cellCount);
            WriteUInt16(page, 5, cellContentOffset);
            page[7] = 0;
            WriteUInt32(page, 8, (uint)childPages[childPages.Count - 1].pageNumber);
            return page;
        }

        private static byte[] BuildTableLeafCell(TableRow row)
        {
            byte[] payload = BuildRecordPayload(row.values);
            List<byte> cell = new List<byte>(payload.Length + 16);
            AppendVarint(cell, (ulong)payload.Length);
            AppendVarint(cell, (ulong)row.rowId);
            cell.AddRange(payload);
            return cell.ToArray();
        }

        private static byte[] BuildRecordPayload(object[] values)
        {
            List<byte> serialTypes = new List<byte>();
            List<byte[]> bodies = new List<byte[]>();
            for (int index = 0; index < values.Length; index++)
            {
                object value = values[index];
                if (value == null)
                {
                    AppendVarint(serialTypes, 0);
                    bodies.Add(Array.Empty<byte>());
                    continue;
                }

                if (value is long)
                {
                    long number = (long)value;
                    AppendIntegerSerialType(serialTypes, number, out byte[] body);
                    bodies.Add(body);
                    continue;
                }

                string text = value.ToString() ?? string.Empty;
                byte[] textBytes = Encoding.UTF8.GetBytes(text);
                AppendVarint(serialTypes, (ulong)(13 + textBytes.Length * 2));
                bodies.Add(textBytes);
            }

            int headerSize = serialTypes.Count + 1;
            while (VarintLength((ulong)headerSize) + serialTypes.Count != headerSize)
                headerSize = VarintLength((ulong)headerSize) + serialTypes.Count;

            List<byte> payload = new List<byte>(headerSize + 128);
            AppendVarint(payload, (ulong)headerSize);
            payload.AddRange(serialTypes);
            for (int index = 0; index < bodies.Count; index++)
                payload.AddRange(bodies[index]);

            return payload.ToArray();
        }

        private static void AppendIntegerSerialType(List<byte> serialTypes, long number, out byte[] body)
        {
            if (number == 0L)
            {
                AppendVarint(serialTypes, 8);
                body = Array.Empty<byte>();
                return;
            }

            if (number == 1L)
            {
                AppendVarint(serialTypes, 9);
                body = Array.Empty<byte>();
                return;
            }

            if (number >= sbyte.MinValue && number <= sbyte.MaxValue)
            {
                AppendVarint(serialTypes, 1);
                body = IntegerBody(number, 1);
            }
            else if (number >= short.MinValue && number <= short.MaxValue)
            {
                AppendVarint(serialTypes, 2);
                body = IntegerBody(number, 2);
            }
            else if (number >= -8388608L && number <= 8388607L)
            {
                AppendVarint(serialTypes, 3);
                body = IntegerBody(number, 3);
            }
            else if (number >= int.MinValue && number <= int.MaxValue)
            {
                AppendVarint(serialTypes, 4);
                body = IntegerBody(number, 4);
            }
            else if (number >= -140737488355328L && number <= 140737488355327L)
            {
                AppendVarint(serialTypes, 5);
                body = IntegerBody(number, 6);
            }
            else
            {
                AppendVarint(serialTypes, 6);
                body = IntegerBody(number, 8);
            }
        }

        private static byte[] IntegerBody(long number, int byteCount)
        {
            byte[] body = new byte[byteCount];
            for (int index = 0; index < byteCount; index++)
            {
                int shift = (byteCount - index - 1) * 8;
                body[index] = (byte)((number >> shift) & 0xff);
            }

            return body;
        }

        private static void WriteDatabaseHeader(byte[] page, int databasePageCount)
        {
            byte[] signature = Encoding.ASCII.GetBytes("SQLite format 3\0");
            Buffer.BlockCopy(signature, 0, page, 0, signature.Length);
            WriteUInt16(page, 16, PageSize);
            page[18] = 1;
            page[19] = 1;
            page[20] = 0;
            page[21] = 64;
            page[22] = 32;
            page[23] = 32;
            WriteUInt32(page, 24, 1);
            WriteUInt32(page, 28, (uint)databasePageCount);
            WriteUInt32(page, 32, 0);
            WriteUInt32(page, 36, 0);
            WriteUInt32(page, 40, 1);
            WriteUInt32(page, 44, 4);
            WriteUInt32(page, 48, 0);
            WriteUInt32(page, 52, 0);
            WriteUInt32(page, 56, 1);
            WriteUInt32(page, 60, 0);
            WriteUInt32(page, 64, 0);
            WriteUInt32(page, 92, 1);
            WriteUInt32(page, 96, 3045000);
        }

        private static int MaxPageNumber(Dictionary<int, byte[]> pages)
        {
            int maxPage = SequenceRootPage;
            foreach (int pageNumber in pages.Keys)
                maxPage = Math.Max(maxPage, pageNumber);

            return maxPage;
        }

        private static int VarintLength(ulong value)
        {
            int length = 1;
            while (value > 0x7f)
            {
                value >>= 7;
                length++;
            }

            return length;
        }

        private static void AppendVarint(List<byte> output, ulong value)
        {
            byte[] scratch = new byte[10];
            int index = scratch.Length;
            scratch[--index] = (byte)(value & 0x7f);
            value >>= 7;
            while (value > 0)
            {
                scratch[--index] = (byte)((value & 0x7f) | 0x80);
                value >>= 7;
            }

            for (; index < scratch.Length; index++)
                output.Add(scratch[index]);
        }

        private static void WriteUInt16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)((value >> 8) & 0xff);
            buffer[offset + 1] = (byte)(value & 0xff);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xff);
            buffer[offset + 1] = (byte)((value >> 16) & 0xff);
            buffer[offset + 2] = (byte)((value >> 8) & 0xff);
            buffer[offset + 3] = (byte)(value & 0xff);
        }

        private static void AppendUInt32(List<byte> output, uint value)
        {
            output.Add((byte)((value >> 24) & 0xff));
            output.Add((byte)((value >> 16) & 0xff));
            output.Add((byte)((value >> 8) & 0xff));
            output.Add((byte)(value & 0xff));
        }
    }

    private static string BuildJsonStringArray(string[] values)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append('[');
        if (values != null)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (index > 0)
                    builder.Append(',');

                builder.Append('"');
                AppendJsonEscapedString(builder, values[index] ?? string.Empty);
                builder.Append('"');
            }
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildPrefabLodKey(PrefabRecord prefab, RendererRecord renderer)
    {
        string prefabPath = prefab != null ? prefab.assetPath ?? string.Empty : string.Empty;
        int lodIndex = renderer != null ? renderer.lodIndex : -1;
        string lod = lodIndex >= 0 ? lodIndex.ToString() : "unknown";
        return prefabPath + "#lod:" + lod;
    }

    private static string StripLodSuffixDisplay(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string text = LodTokenRegex.Replace(value, " ");
        while (text.Contains("__"))
            text = text.Replace("__", "_");

        return text.Trim(' ', '_', '-');
    }

    private static void AppendJsonEscapedString(StringBuilder builder, string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ')
                        builder.AppendFormat("\\u{0:x4}", (int)character);
                    else
                        builder.Append(character);
                    break;
            }
        }
    }

    private static string ResolveOutputPathFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "-unity-prefab-metadata-output", StringComparison.Ordinal) ||
                string.Equals(args[index], "--unity-prefab-metadata-output", StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        string artifactsRoot = RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            string.Empty,
            RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot);

        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(artifactsRoot, out RenderDocCaptureArtifactInfo artifactInfo))
            throw new InvalidOperationException("No RenderDoc capture is available for Unity prefab metadata export.");

        return artifactInfo.unityPrefabMetadataPath;
    }

    private static string ResolveSignatureDatabaseOutputPathFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "-unity-prefab-signatures-output", StringComparison.Ordinal) ||
                string.Equals(args[index], "--unity-prefab-signatures-output", StringComparison.Ordinal) ||
                string.Equals(args[index], "-unity-prefab-sqlite-output", StringComparison.Ordinal) ||
                string.Equals(args[index], "--unity-prefab-sqlite-output", StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        string artifactsRoot = RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            string.Empty,
            RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot);

        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(artifactsRoot, out RenderDocCaptureArtifactInfo artifactInfo))
            throw new InvalidOperationException("No RenderDoc capture is available for Unity prefab signature export.");

        return artifactInfo.unityPrefabSignatureDatabasePath;
    }
}
