using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class ProjectFbxAutoImporter : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        ProjectFbxDccPrefabGenerator.EnqueueImportedAssets(importedAssets);
    }

    private void OnPreprocessModel()
    {
        if (assetImporter is not ModelImporter importer || !ProjectFbxDccSidecar.IsFbx(assetPath))
            return;

        if (!ProjectFbxDccSidecar.TryLoad(assetPath, out ProjectFbxDccManifest manifest, out string error))
        {
            Debug.LogError(error);
            return;
        }

        ProjectFbxDccImportSettingsApplier.Apply(importer, manifest);
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (root == null || !ProjectFbxDccSidecar.IsFbx(assetPath))
            return;

        if (!ProjectFbxDccSidecar.TryLoad(assetPath, out ProjectFbxDccManifest manifest, out string error))
        {
            Debug.LogError(error);
            return;
        }

        ProjectFbxDccAssemblyProcessor.Apply(root, manifest);
    }

    private Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        if (material == null || !ProjectFbxDccSidecar.IsFbx(assetPath))
            return material;

        if (!ProjectFbxDccSidecar.TryLoad(assetPath, out ProjectFbxDccManifest manifest, out _))
            return material;

        Material resolved = ProjectFbxDccMaterialResolver.Resolve(manifest, material.name);
        return resolved != null ? resolved : material;
    }
}

internal static class ProjectFbxAutoImporterRuleDocs
{
    private const string RulesDocumentPath = "docs/fbx-auto-import-rules.md";
    private const string MenuPath = "Tools/Asset Import/FBX Auto Import Rules";

    [MenuItem(MenuPath)]
    private static void OpenRulesDocument()
    {
        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", RulesDocumentPath));
        if (!File.Exists(fullPath))
        {
            Debug.LogWarning($"FBX auto import rules document not found: {RulesDocumentPath}");
            return;
        }

        EditorUtility.OpenWithDefaultApp(fullPath);
    }
}

internal enum ProjectFbxDccImportKind
{
    StaticModel,
    CharacterModel,
    AnimationAsset
}

internal enum ProjectFbxDccRig
{
    None,
    Generic,
    Humanoid
}

internal enum ProjectFbxDccColliderType
{
    Box,
    Mesh,
    Sphere,
    Capsule
}

[Serializable]
internal sealed class ProjectFbxDccManifest
{
    public int schemaVersion;
    public string sourceFbx;
    public ProjectFbxDccImportSettings importSettings;
    public ProjectFbxDccAssembly assembly;
}

[Serializable]
internal sealed class ProjectFbxDccImportSettings
{
    public string kind;
    public string rig;
    public bool preserveHierarchy;
    public bool importAnimation;
    public bool importBlendShapes;
    public bool generateLightmapUv;
    public bool generateColliders;
    public bool readWrite;
    public string meshCompression;
    public string animationCompression;
}

[Serializable]
internal sealed class ProjectFbxDccAssembly
{
    public ProjectFbxDccPrefabConfig prefab;
    public ProjectFbxDccLodGroupConfig lodGroup;
    public ProjectFbxDccColliderConfig[] colliders;
    public ProjectFbxDccMaterialConfig[] materials;
}

[Serializable]
internal sealed class ProjectFbxDccPrefabConfig
{
    public bool enabled;
    public string outputPath;
    public bool overwrite;
}

[Serializable]
internal sealed class ProjectFbxDccLodGroupConfig
{
    public bool enabled;
    public string rootPath;
    public ProjectFbxDccLodLevelConfig[] levels;
}

[Serializable]
internal sealed class ProjectFbxDccLodLevelConfig
{
    public int index;
    public string nodePath;
    public float screenRelativeHeight;
}

[Serializable]
internal sealed class ProjectFbxDccColliderConfig
{
    public string nodePath;
    public string type;
    public bool convex;
    public bool disableRenderer;
}

[Serializable]
internal sealed class ProjectFbxDccMaterialConfig
{
    public string slotName;
    public string materialPath;
}

internal static class ProjectFbxDccSidecar
{
    public const int SupportedSchemaVersion = 1;

    public static bool IsFbx(string assetPath)
    {
        return !string.IsNullOrWhiteSpace(assetPath) &&
            assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSidecarAssetPath(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath)
            ? null
            : NormalizeAssetPath(assetPath) + ".json";
    }

    public static bool TryLoad(string fbxAssetPath, out ProjectFbxDccManifest manifest, out string error)
    {
        manifest = null;
        error = null;

        if (!IsFbx(fbxAssetPath))
        {
            error = $"DCC FBX sidecar can only be loaded for .fbx assets: {fbxAssetPath}";
            return false;
        }

        string sidecarAssetPath = GetSidecarAssetPath(fbxAssetPath);
        string sidecarFullPath = ToProjectAbsolutePath(sidecarAssetPath);
        if (string.IsNullOrWhiteSpace(sidecarFullPath) || !File.Exists(sidecarFullPath))
        {
            error = $"Missing DCC FBX sidecar JSON: {sidecarAssetPath}";
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(sidecarFullPath, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            error = $"Failed to read DCC FBX sidecar JSON: {sidecarAssetPath}\n{exception.Message}";
            return false;
        }

        return TryParseJson(json, fbxAssetPath, out manifest, out error);
    }

    public static bool TryParseJson(string json, string fbxAssetPath, out ProjectFbxDccManifest manifest, out string error)
    {
        manifest = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = $"DCC FBX sidecar JSON is empty for: {fbxAssetPath}";
            return false;
        }

        try
        {
            manifest = JsonUtility.FromJson<ProjectFbxDccManifest>(json);
        }
        catch (Exception exception)
        {
            error = $"Failed to parse DCC FBX sidecar JSON for {fbxAssetPath}: {exception.Message}";
            return false;
        }

        if (manifest == null)
        {
            error = $"Failed to parse DCC FBX sidecar JSON for: {fbxAssetPath}";
            return false;
        }

        List<string> errors = new List<string>();
        ValidateManifest(manifest, fbxAssetPath, errors);
        if (errors.Count > 0)
        {
            error = $"Invalid DCC FBX sidecar JSON for {fbxAssetPath}:\n- " + string.Join("\n- ", errors);
            return false;
        }

        return true;
    }

    private static void ValidateManifest(ProjectFbxDccManifest manifest, string fbxAssetPath, List<string> errors)
    {
        if (manifest.schemaVersion != SupportedSchemaVersion)
            errors.Add($"schemaVersion must be {SupportedSchemaVersion}.");

        string expectedSourceFbx = Path.GetFileName(NormalizeAssetPath(fbxAssetPath));
        if (string.IsNullOrWhiteSpace(manifest.sourceFbx))
            errors.Add("sourceFbx is required.");
        else if (!string.IsNullOrWhiteSpace(expectedSourceFbx) &&
            !string.Equals(manifest.sourceFbx, expectedSourceFbx, StringComparison.OrdinalIgnoreCase))
            errors.Add($"sourceFbx must match the FBX file name: {expectedSourceFbx}.");

        if (manifest.importSettings == null)
        {
            errors.Add("importSettings is required.");
        }
        else
        {
            ValidateImportSettings(manifest.importSettings, errors);
        }

        if (manifest.assembly == null)
        {
            errors.Add("assembly is required.");
        }
        else
        {
            ValidateAssembly(manifest.assembly, errors);
        }
    }

    private static void ValidateImportSettings(ProjectFbxDccImportSettings settings, List<string> errors)
    {
        if (!TryParseEnum(settings.kind, out ProjectFbxDccImportKind _))
            errors.Add("importSettings.kind must be StaticModel, CharacterModel, or AnimationAsset.");

        if (!TryParseEnum(settings.rig, out ProjectFbxDccRig _))
            errors.Add("importSettings.rig must be None, Generic, or Humanoid.");

        if (!TryParseEnum(settings.meshCompression, out ModelImporterMeshCompression _))
            errors.Add("importSettings.meshCompression must be Off, Low, Medium, or High.");

        if (!TryParseEnum(settings.animationCompression, out ModelImporterAnimationCompression _))
            errors.Add("importSettings.animationCompression must be Off, KeyframeReduction, or Optimal.");
    }

    private static void ValidateAssembly(ProjectFbxDccAssembly assembly, List<string> errors)
    {
        if (assembly.prefab != null && assembly.prefab.enabled)
        {
            if (string.IsNullOrWhiteSpace(assembly.prefab.outputPath))
                errors.Add("assembly.prefab.outputPath is required when prefab.enabled is true.");
            else if (!NormalizeAssetPath(assembly.prefab.outputPath).StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                !assembly.prefab.outputPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                errors.Add("assembly.prefab.outputPath must be an Assets/... .prefab path.");
        }

        if (assembly.lodGroup != null && assembly.lodGroup.enabled)
        {
            if (assembly.lodGroup.levels == null || assembly.lodGroup.levels.Length == 0)
            {
                errors.Add("assembly.lodGroup.levels must contain at least one level when lodGroup.enabled is true.");
            }
            else
            {
                foreach (ProjectFbxDccLodLevelConfig level in assembly.lodGroup.levels)
                {
                    if (level == null)
                    {
                        errors.Add("assembly.lodGroup.levels contains a null level.");
                        continue;
                    }

                    if (level.index < 0)
                        errors.Add("assembly.lodGroup.levels.index must be greater than or equal to 0.");
                    if (string.IsNullOrWhiteSpace(level.nodePath))
                        errors.Add("assembly.lodGroup.levels.nodePath is required.");
                    if (level.screenRelativeHeight <= 0.0f)
                        errors.Add("assembly.lodGroup.levels.screenRelativeHeight must be greater than 0.");
                }
            }
        }

        ValidateColliders(assembly.colliders, errors);
        ValidateMaterials(assembly.materials, errors);
    }

    private static void ValidateColliders(ProjectFbxDccColliderConfig[] colliders, List<string> errors)
    {
        if (colliders == null)
            return;

        foreach (ProjectFbxDccColliderConfig collider in colliders)
        {
            if (collider == null)
            {
                errors.Add("assembly.colliders contains a null collider.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(collider.nodePath))
                errors.Add("assembly.colliders.nodePath is required.");

            if (!TryParseEnum(collider.type, out ProjectFbxDccColliderType _))
                errors.Add("assembly.colliders.type must be Box, Mesh, Sphere, or Capsule.");
        }
    }

    private static void ValidateMaterials(ProjectFbxDccMaterialConfig[] materials, List<string> errors)
    {
        if (materials == null)
            return;

        foreach (ProjectFbxDccMaterialConfig material in materials)
        {
            if (material == null)
            {
                errors.Add("assembly.materials contains a null material.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(material.slotName))
                errors.Add("assembly.materials.slotName is required.");
            if (string.IsNullOrWhiteSpace(material.materialPath))
                errors.Add("assembly.materials.materialPath is required.");
        }
    }

    public static bool TryParseEnum<TEnum>(string value, out TEnum result)
        where TEnum : struct
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, true, out result) &&
            Enum.IsDefined(typeof(TEnum), result);
    }

    private static string ToProjectAbsolutePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        if (Path.IsPathRooted(assetPath))
            return Path.GetFullPath(assetPath);

        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return (assetPath ?? string.Empty).Replace('\\', '/');
    }
}

internal static class ProjectFbxDccImportSettingsApplier
{
    public static void Apply(ModelImporter importer, ProjectFbxDccManifest manifest)
    {
        ProjectFbxDccImportSettings settings = manifest.importSettings;

        importer.preserveHierarchy = settings.preserveHierarchy;
        importer.importAnimation = settings.importAnimation;
        importer.importBlendShapes = settings.importBlendShapes;
        importer.addCollider = settings.generateColliders;
        importer.generateSecondaryUV = settings.generateLightmapUv;
        importer.isReadable = settings.readWrite;

        if (ProjectFbxDccSidecar.TryParseEnum(settings.meshCompression, out ModelImporterMeshCompression meshCompression))
            importer.meshCompression = meshCompression;

        if (ProjectFbxDccSidecar.TryParseEnum(settings.animationCompression, out ModelImporterAnimationCompression animationCompression))
            importer.animationCompression = animationCompression;

        ApplyRig(importer, settings.rig);

        if (ProjectFbxDccSidecar.TryParseEnum(settings.kind, out ProjectFbxDccImportKind kind) &&
            kind == ProjectFbxDccImportKind.AnimationAsset)
            ApplyAnimationClipDefaults(importer, manifest.sourceFbx);
    }

    private static void ApplyRig(ModelImporter importer, string rig)
    {
        if (!ProjectFbxDccSidecar.TryParseEnum(rig, out ProjectFbxDccRig parsedRig))
            return;

        importer.animationType = ResolveAnimationType(parsedRig);
        if (TryResolveAvatarSetup(parsedRig, out ModelImporterAvatarSetup avatarSetup))
            importer.avatarSetup = avatarSetup;
    }

    public static ModelImporterAnimationType ResolveAnimationType(string rig)
    {
        return ProjectFbxDccSidecar.TryParseEnum(rig, out ProjectFbxDccRig parsedRig)
            ? ResolveAnimationType(parsedRig)
            : ModelImporterAnimationType.None;
    }

    private static ModelImporterAnimationType ResolveAnimationType(ProjectFbxDccRig rig)
    {
        return rig switch
        {
            ProjectFbxDccRig.Humanoid => ModelImporterAnimationType.Human,
            ProjectFbxDccRig.Generic => ModelImporterAnimationType.Generic,
            _ => ModelImporterAnimationType.None
        };
    }

    private static bool TryResolveAvatarSetup(ProjectFbxDccRig rig, out ModelImporterAvatarSetup avatarSetup)
    {
        if (rig == ProjectFbxDccRig.Humanoid || rig == ProjectFbxDccRig.Generic)
        {
            avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            return true;
        }

        avatarSetup = default;
        return false;
    }

    private static void ApplyAnimationClipDefaults(ModelImporter importer, string sourceFbx)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
            return;

        string clipBaseName = Path.GetFileNameWithoutExtension(sourceFbx);
        if (string.IsNullOrWhiteSpace(clipBaseName))
            clipBaseName = "ImportedClip";

        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];
            if (string.IsNullOrWhiteSpace(clip.name))
                clip.name = clips.Length > 1 ? $"{clipBaseName}_{i + 1:00}" : clipBaseName;

            clips[i] = clip;
        }

        importer.clipAnimations = clips;
    }
}

internal static class ProjectFbxDccAssemblyProcessor
{
    public static void Apply(GameObject root, ProjectFbxDccManifest manifest)
    {
        if (root == null || manifest?.assembly == null)
            return;

        ApplyLodGroup(root, manifest.assembly.lodGroup);
        ApplyColliders(root, manifest.assembly.colliders);
        ApplyMaterials(root, manifest.assembly.materials);
    }

    private static void ApplyLodGroup(GameObject root, ProjectFbxDccLodGroupConfig lodGroupConfig)
    {
        if (lodGroupConfig == null || !lodGroupConfig.enabled)
            return;

        Transform lodRoot = FindTransform(root.transform, lodGroupConfig.rootPath);
        if (lodRoot == null)
        {
            Debug.LogError($"DCC FBX LOD root not found: {lodGroupConfig.rootPath}");
            return;
        }

        List<LOD> lods = new List<LOD>();
        bool hasError = false;
        foreach (ProjectFbxDccLodLevelConfig level in lodGroupConfig.levels.OrderBy(item => item.index))
        {
            Transform levelRoot = FindTransform(root.transform, level.nodePath);
            if (levelRoot == null)
            {
                Debug.LogError($"DCC FBX LOD node not found: {level.nodePath}");
                hasError = true;
                continue;
            }

            Renderer[] renderers = levelRoot.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && renderer.enabled)
                .ToArray();
            if (renderers.Length == 0)
            {
                Debug.LogError($"DCC FBX LOD node has no enabled renderers: {level.nodePath}");
                hasError = true;
                continue;
            }

            lods.Add(new LOD(level.screenRelativeHeight, renderers));
        }

        if (hasError || lods.Count == 0)
            return;

        LODGroup lodGroup = lodRoot.GetComponent<LODGroup>();
        if (lodGroup == null)
            lodGroup = lodRoot.gameObject.AddComponent<LODGroup>();

        lodGroup.SetLODs(lods.ToArray());
        lodGroup.RecalculateBounds();
    }

    private static void ApplyColliders(GameObject root, ProjectFbxDccColliderConfig[] colliders)
    {
        if (colliders == null)
            return;

        foreach (ProjectFbxDccColliderConfig colliderConfig in colliders)
        {
            Transform target = FindTransform(root.transform, colliderConfig.nodePath);
            if (target == null)
            {
                Debug.LogError($"DCC FBX collider node not found: {colliderConfig.nodePath}");
                continue;
            }

            if (!ProjectFbxDccSidecar.TryParseEnum(colliderConfig.type, out ProjectFbxDccColliderType colliderType))
                continue;

            AddCollider(target.gameObject, colliderType, colliderConfig);

            if (colliderConfig.disableRenderer)
                DisableRenderers(target.gameObject);
        }
    }

    private static void ApplyMaterials(GameObject root, ProjectFbxDccMaterialConfig[] materialConfigs)
    {
        if (materialConfigs == null || materialConfigs.Length == 0)
            return;

        Dictionary<string, Material> materialsBySlot = ProjectFbxDccMaterialResolver.LoadMaterialsBySlot(materialConfigs);
        if (materialsBySlot.Count == 0)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                string slotName = ProjectFbxDccMaterialResolver.NormalizeSlotName(sharedMaterials[i] != null ? sharedMaterials[i].name : null);
                if (string.IsNullOrWhiteSpace(slotName))
                    continue;

                if (!materialsBySlot.TryGetValue(slotName, out Material replacement))
                    continue;

                sharedMaterials[i] = replacement;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = sharedMaterials;
        }
    }

    private static void AddCollider(GameObject go, ProjectFbxDccColliderType colliderType, ProjectFbxDccColliderConfig colliderConfig)
    {
        if (go.GetComponent<Collider>() != null)
            return;

        switch (colliderType)
        {
            case ProjectFbxDccColliderType.Box:
                AddBoxCollider(go);
                break;
            case ProjectFbxDccColliderType.Mesh:
                AddMeshCollider(go, colliderConfig.convex);
                break;
            case ProjectFbxDccColliderType.Sphere:
                AddSphereCollider(go);
                break;
            case ProjectFbxDccColliderType.Capsule:
                AddCapsuleCollider(go);
                break;
        }
    }

    private static void AddBoxCollider(GameObject go)
    {
        BoxCollider collider = go.AddComponent<BoxCollider>();
        Bounds bounds = GetLocalBounds(go);
        collider.center = bounds.center;
        collider.size = bounds.size;
    }

    private static void AddMeshCollider(GameObject go, bool convex)
    {
        MeshCollider collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = GetSharedMesh(go);
        collider.convex = convex;
    }

    private static void AddSphereCollider(GameObject go)
    {
        SphereCollider collider = go.AddComponent<SphereCollider>();
        Bounds bounds = GetLocalBounds(go);
        collider.center = bounds.center;
        collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
    }

    private static void AddCapsuleCollider(GameObject go)
    {
        CapsuleCollider collider = go.AddComponent<CapsuleCollider>();
        Bounds bounds = GetLocalBounds(go);
        collider.center = bounds.center;
        collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        collider.height = Mathf.Max(bounds.size.y, collider.radius * 2.0f);
        collider.direction = 1;
    }

    private static void DisableRenderers(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
            renderer.enabled = false;
    }

    internal static Transform FindTransform(Transform root, string nodePath)
    {
        if (root == null)
            return null;

        string normalizedPath = (nodePath ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return root;

        string[] segments = normalizedPath.Split('/');
        int startIndex = segments.Length > 0 && string.Equals(segments[0], root.name, StringComparison.Ordinal)
            ? 1
            : 0;

        Transform current = root;
        for (int i = startIndex; i < segments.Length; i++)
        {
            current = FindDirectChild(current, segments[i]);
            if (current == null)
                return null;
        }

        return current;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private static Bounds GetLocalBounds(GameObject go)
    {
        Mesh mesh = GetSharedMesh(go);
        if (mesh != null)
            return mesh.bounds;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
            return new Bounds(Vector3.zero, go.transform.InverseTransformVector(renderer.bounds.size));

        return new Bounds(Vector3.zero, Vector3.one);
    }

    private static Mesh GetSharedMesh(GameObject go)
    {
        MeshFilter meshFilter = go.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
            return meshFilter.sharedMesh;

        SkinnedMeshRenderer skinnedMeshRenderer = go.GetComponent<SkinnedMeshRenderer>();
        return skinnedMeshRenderer != null ? skinnedMeshRenderer.sharedMesh : null;
    }
}

internal static class ProjectFbxDccMaterialResolver
{
    public static Material Resolve(ProjectFbxDccManifest manifest, string slotName)
    {
        if (manifest?.assembly?.materials == null)
            return null;

        string normalizedSlotName = NormalizeSlotName(slotName);
        if (string.IsNullOrWhiteSpace(normalizedSlotName))
            return null;

        foreach (ProjectFbxDccMaterialConfig materialConfig in manifest.assembly.materials)
        {
            if (materialConfig == null)
                continue;

            if (!string.Equals(NormalizeSlotName(materialConfig.slotName), normalizedSlotName, StringComparison.OrdinalIgnoreCase))
                continue;

            return LoadMaterial(materialConfig.materialPath);
        }

        return null;
    }

    public static Dictionary<string, Material> LoadMaterialsBySlot(ProjectFbxDccMaterialConfig[] materialConfigs)
    {
        Dictionary<string, Material> materialsBySlot = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        if (materialConfigs == null)
            return materialsBySlot;

        foreach (ProjectFbxDccMaterialConfig materialConfig in materialConfigs)
        {
            if (materialConfig == null)
                continue;

            string slotName = NormalizeSlotName(materialConfig.slotName);
            Material material = LoadMaterial(materialConfig.materialPath);
            if (string.IsNullOrWhiteSpace(slotName) || material == null)
                continue;

            materialsBySlot[slotName] = material;
        }

        return materialsBySlot;
    }

    public static string NormalizeSlotName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string name = value.Trim();
        int instanceIndex = name.IndexOf(" (Instance)", StringComparison.OrdinalIgnoreCase);
        if (instanceIndex >= 0)
            name = name.Substring(0, instanceIndex);

        return name;
    }

    private static Material LoadMaterial(string materialPath)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
            return null;

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
            Debug.LogError($"DCC FBX material mapping target not found: {materialPath}");

        return material;
    }
}

internal static class ProjectFbxDccPrefabGenerator
{
    private static readonly HashSet<string> PendingAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool s_DelayCallRegistered;

    public static void EnqueueImportedAssets(IEnumerable<string> assetPaths)
    {
        if (assetPaths == null)
            return;

        bool addedAny = false;
        foreach (string assetPath in assetPaths)
        {
            if (!ProjectFbxDccSidecar.IsFbx(assetPath))
                continue;

            if (!ProjectFbxDccSidecar.TryLoad(assetPath, out ProjectFbxDccManifest manifest, out _))
                continue;

            if (manifest.assembly?.prefab == null || !manifest.assembly.prefab.enabled)
                continue;

            PendingAssetPaths.Add(NormalizeAssetPath(assetPath));
            addedAny = true;
        }

        if (!addedAny || s_DelayCallRegistered)
            return;

        s_DelayCallRegistered = true;
        EditorApplication.delayCall += ProcessPendingAssets;
    }

    private static void ProcessPendingAssets()
    {
        s_DelayCallRegistered = false;

        string[] assetPaths = PendingAssetPaths.ToArray();
        PendingAssetPaths.Clear();

        foreach (string assetPath in assetPaths)
            CreatePrefab(assetPath);
    }

    private static void CreatePrefab(string assetPath)
    {
        if (!ProjectFbxDccSidecar.TryLoad(assetPath, out ProjectFbxDccManifest manifest, out string error))
        {
            Debug.LogError(error);
            return;
        }

        ProjectFbxDccPrefabConfig prefabConfig = manifest.assembly?.prefab;
        if (prefabConfig == null || !prefabConfig.enabled)
            return;

        if (!ShouldCreatePrefab(prefabConfig))
        {
            Debug.Log($"DCC FBX prefab already exists, skip auto-create: {prefabConfig.outputPath}");
            return;
        }

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (modelAsset == null)
        {
            Debug.LogWarning($"Unable to load DCC FBX model asset for prefab generation: {assetPath}");
            return;
        }

        EnsureFolder(Path.GetDirectoryName(prefabConfig.outputPath)?.Replace('\\', '/'));

        GameObject instance = UnityEngine.Object.Instantiate(modelAsset);
        instance.name = Path.GetFileNameWithoutExtension(prefabConfig.outputPath);

        try
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabConfig.outputPath);
            if (prefab == null)
                Debug.LogWarning($"Failed to auto-create DCC FBX prefab: {prefabConfig.outputPath}");
            else
                Debug.Log($"Auto-created DCC FBX prefab: {prefabConfig.outputPath}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    public static bool ShouldCreatePrefab(ProjectFbxDccPrefabConfig prefabConfig)
    {
        if (prefabConfig == null || !prefabConfig.enabled)
            return false;

        if (prefabConfig.overwrite)
            return true;

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabConfig.outputPath) == null;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] segments = folderPath.Split('/');
        if (segments.Length == 0)
            return;

        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);

            current = next;
        }
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return (assetPath ?? string.Empty).Replace('\\', '/');
    }
}
