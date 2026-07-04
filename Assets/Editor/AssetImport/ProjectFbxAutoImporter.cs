using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public sealed class ProjectFbxAutoImporter : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (assetImporter is not ModelImporter importer || !ProjectFbxImportRules.IsFbx(assetPath))
            return;

        ProjectFbxFileSignals signals = ProjectFbxFileSignals.Scan(assetPath);
        ProjectFbxImportProfile profile = ProjectFbxImportRules.Evaluate(assetPath, signals);
        ProjectFbxImportRules.ApplyPreprocessSettings(importer, profile, assetPath);
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (root == null || !ProjectFbxImportRules.IsFbx(assetPath))
            return;

        ProjectFbxImportedModelInfo info = ProjectFbxImportedModelInfo.FromRoot(root);
        ProjectFbxImportProfile profile = ProjectFbxImportRules.Evaluate(assetPath, info);
        ProjectFbxPostprocessRules.Apply(root, profile);
    }

    private Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        if (material == null || !ProjectFbxImportRules.IsFbx(assetPath))
            return material;

        Material resolved = ProjectFbxMaterialResolver.Resolve(material.name, assetPath);
        return resolved != null ? resolved : material;
    }

    private void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] propNames, object[] values)
    {
        if (go == null || !ProjectFbxImportRules.IsFbx(assetPath))
            return;

        string collisionKind = ProjectFbxUserPropertyReader.FindString(propNames, values, "Collision");
        if (string.IsNullOrWhiteSpace(collisionKind))
            collisionKind = ProjectFbxUserPropertyReader.FindString(propNames, values, "Collider");

        ProjectFbxCollisionBuilder.AddRequestedCollider(go, collisionKind);
    }
}

internal enum ProjectFbxImportKind
{
    StaticModel,
    CharacterModel,
    AnimationAsset,
    LodModel
}

internal sealed class ProjectFbxImportProfile
{
    public ProjectFbxImportKind Kind { get; set; }
    public bool IsCharacterPath { get; set; }
    public bool IsPropsPath { get; set; }
    public bool HasSkeletonOrSkinning { get; set; }
    public bool HasAnimationStacks { get; set; }
    public bool HasMultipleAnimationStacks { get; set; }
    public bool HasLodNodes { get; set; }
    public bool HasCollisionNodes { get; set; }
    public bool HasCustomCollisionHint { get; set; }
    public bool GenerateLightmapUv { get; set; }
    public bool GenerateColliders { get; set; }
    public bool UseHumanoidRig { get; set; }
}

internal sealed class ProjectFbxFileSignals
{
    private const int MaxScannedBytes = 16 * 1024 * 1024;

    public bool HasSkeletonOrSkinning { get; set; }
    public bool HasBindPose { get; set; }
    public bool HasAnimationStacks { get; set; }
    public bool HasMultipleAnimationStacks { get; set; }
    public bool HasLodNodes { get; set; }
    public bool HasCollisionNodes { get; set; }
    public bool HasCustomCollisionHint { get; set; }

    public static ProjectFbxFileSignals Scan(string assetPath)
    {
        ProjectFbxFileSignals signals = new ProjectFbxFileSignals();
        string fullPath = ToProjectAbsolutePath(assetPath);
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            return signals;

        byte[] bytes = ReadScanWindow(fullPath);
        int animationStackCount = CountAscii(bytes, "AnimationStack") + CountAscii(bytes, "AnimStack");
        bool hasSkeletonMarker = ContainsAscii(bytes, "Skeleton") || ContainsAscii(bytes, "LimbNode");
        bool hasSkinMarker = ContainsAscii(bytes, "Skin") && (ContainsAscii(bytes, "Cluster") || ContainsAscii(bytes, "Deformer"));
        bool hasBindPose = ContainsAscii(bytes, "BindPose") || ContainsAscii(bytes, "PoseNode");

        signals.HasSkeletonOrSkinning = hasSkeletonMarker || hasSkinMarker || hasBindPose;
        signals.HasBindPose = hasBindPose;
        signals.HasAnimationStacks = animationStackCount > 0;
        signals.HasMultipleAnimationStacks = animationStackCount > 1;
        signals.HasLodNodes =
            ContainsAscii(bytes, "LOD0") ||
            ContainsAscii(bytes, "LOD1") ||
            ContainsAscii(bytes, "LOD2");
        signals.HasCollisionNodes =
            ContainsAscii(bytes, "UCX_") ||
            ContainsAscii(bytes, "UBX_") ||
            ContainsAscii(bytes, "USP_") ||
            ContainsAscii(bytes, "UCP_");
        signals.HasCustomCollisionHint = ContainsAscii(bytes, "Collision") || ContainsAscii(bytes, "Collider");
        return signals;
    }

    private static byte[] ReadScanWindow(string fullPath)
    {
        FileInfo fileInfo = new FileInfo(fullPath);
        int byteCount = (int)Math.Min(fileInfo.Length, MaxScannedBytes);
        byte[] bytes = new byte[byteCount];

        using (FileStream stream = File.OpenRead(fullPath))
        {
            int offset = 0;
            while (offset < byteCount)
            {
                int read = stream.Read(bytes, offset, byteCount - offset);
                if (read <= 0)
                    break;

                offset += read;
            }
        }

        return bytes;
    }

    private static int CountAscii(byte[] bytes, string token)
    {
        int count = 0;
        int start = 0;
        while (true)
        {
            int index = IndexOfAscii(bytes, token, start);
            if (index < 0)
                return count;

            count++;
            start = index + token.Length;
        }
    }

    private static bool ContainsAscii(byte[] bytes, string token)
    {
        return IndexOfAscii(bytes, token, 0) >= 0;
    }

    private static int IndexOfAscii(byte[] bytes, string token, int start)
    {
        if (bytes == null || bytes.Length == 0 || string.IsNullOrEmpty(token))
            return -1;

        for (int i = Math.Max(0, start); i <= bytes.Length - token.Length; i++)
        {
            bool matches = true;
            for (int j = 0; j < token.Length; j++)
            {
                byte value = bytes[i + j];
                char expected = token[j];
                if (value != expected)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return i;
        }

        return -1;
    }

    private static string ToProjectAbsolutePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        if (Path.IsPathRooted(assetPath))
            return Path.GetFullPath(assetPath);

        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }
}

internal readonly struct ProjectFbxImportedModelInfo
{
    public ProjectFbxImportedModelInfo(
        bool hasSkinnedMeshRenderer,
        bool hasAnimationComponent,
        bool hasLodNodes,
        bool hasCollisionNodes)
    {
        HasSkinnedMeshRenderer = hasSkinnedMeshRenderer;
        HasAnimationComponent = hasAnimationComponent;
        HasLodNodes = hasLodNodes;
        HasCollisionNodes = hasCollisionNodes;
    }

    public bool HasSkinnedMeshRenderer { get; }
    public bool HasAnimationComponent { get; }
    public bool HasLodNodes { get; }
    public bool HasCollisionNodes { get; }

    public static ProjectFbxImportedModelInfo FromRoot(GameObject root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        bool hasLodNodes = transforms.Any(item => ProjectFbxNaming.IsLodNode(item.name));
        bool hasCollisionNodes = transforms.Any(item => ProjectFbxNaming.IsCollisionNode(item.name));
        bool hasSkinnedMeshRenderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
        bool hasAnimationComponent =
            root.GetComponentInChildren<Animator>(true) != null ||
            root.GetComponentInChildren<Animation>(true) != null;

        return new ProjectFbxImportedModelInfo(
            hasSkinnedMeshRenderer,
            hasAnimationComponent,
            hasLodNodes,
            hasCollisionNodes);
    }
}

internal static class ProjectFbxImportRules
{
    private const string CharactersSegment = "/Characters/";
    private const string PropsSegment = "/Props/";
    private const string SourceAnimationsSegment = "/SourceAnimations/";
    private const string AnimationsSegment = "/Animations/";

    public static bool IsFbx(string assetPath)
    {
        return !string.IsNullOrWhiteSpace(assetPath) &&
            assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
    }

    public static ProjectFbxImportProfile Evaluate(string assetPath, ProjectFbxFileSignals signals)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        bool isCharacterPath = normalizedPath.IndexOf(CharactersSegment, StringComparison.OrdinalIgnoreCase) >= 0;
        bool isPropsPath = normalizedPath.IndexOf(PropsSegment, StringComparison.OrdinalIgnoreCase) >= 0;
        bool isAnimationPath =
            normalizedPath.IndexOf(SourceAnimationsSegment, StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalizedPath.IndexOf(AnimationsSegment, StringComparison.OrdinalIgnoreCase) >= 0 ||
            Path.GetFileNameWithoutExtension(normalizedPath).IndexOf("@", StringComparison.OrdinalIgnoreCase) >= 0;

        ProjectFbxImportProfile profile = new ProjectFbxImportProfile
        {
            IsCharacterPath = isCharacterPath,
            IsPropsPath = isPropsPath,
            HasSkeletonOrSkinning = signals.HasSkeletonOrSkinning,
            HasAnimationStacks = signals.HasAnimationStacks,
            HasMultipleAnimationStacks = signals.HasMultipleAnimationStacks,
            HasLodNodes = signals.HasLodNodes,
            HasCollisionNodes = signals.HasCollisionNodes,
            HasCustomCollisionHint = signals.HasCustomCollisionHint
        };

        if (isAnimationPath || signals.HasMultipleAnimationStacks)
        {
            profile.Kind = ProjectFbxImportKind.AnimationAsset;
        }
        else if (isCharacterPath || signals.HasSkeletonOrSkinning)
        {
            profile.Kind = ProjectFbxImportKind.CharacterModel;
        }
        else if (signals.HasLodNodes)
        {
            profile.Kind = ProjectFbxImportKind.LodModel;
        }
        else
        {
            profile.Kind = ProjectFbxImportKind.StaticModel;
        }

        profile.UseHumanoidRig = isCharacterPath;
        profile.GenerateColliders = isPropsPath || signals.HasCollisionNodes || signals.HasCustomCollisionHint;
        profile.GenerateLightmapUv = isPropsPath;
        return profile;
    }

    public static ProjectFbxImportProfile Evaluate(string assetPath, ProjectFbxImportedModelInfo info)
    {
        ProjectFbxFileSignals signals = new ProjectFbxFileSignals
        {
            HasSkeletonOrSkinning = info.HasSkinnedMeshRenderer || info.HasAnimationComponent,
            HasAnimationStacks = info.HasAnimationComponent,
            HasMultipleAnimationStacks = false,
            HasLodNodes = info.HasLodNodes,
            HasCollisionNodes = info.HasCollisionNodes,
            HasCustomCollisionHint = false
        };

        return Evaluate(assetPath, signals);
    }

    public static void ApplyPreprocessSettings(ModelImporter importer, ProjectFbxImportProfile profile, string assetPath)
    {
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = true;
        importer.sortHierarchyByName = true;
        importer.extraUserProperties = MergeUserProperties(importer.extraUserProperties, "Collision", "Collider");

        if (profile.HasLodNodes || profile.HasCollisionNodes)
            importer.preserveHierarchy = true;

        switch (profile.Kind)
        {
            case ProjectFbxImportKind.AnimationAsset:
                ConfigureAnimationAsset(importer, profile, assetPath);
                break;
            case ProjectFbxImportKind.CharacterModel:
                ConfigureCharacterModel(importer, profile);
                break;
            case ProjectFbxImportKind.LodModel:
                ConfigureStaticModel(importer, profile);
                importer.preserveHierarchy = true;
                break;
            default:
                ConfigureStaticModel(importer, profile);
                break;
        }
    }

    private static void ConfigureCharacterModel(ModelImporter importer, ProjectFbxImportProfile profile)
    {
        importer.animationType = profile.UseHumanoidRig
            ? ModelImporterAnimationType.Human
            : ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.importBlendShapes = true;
        importer.addCollider = false;
        importer.isReadable = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
    }

    private static void ConfigureAnimationAsset(ModelImporter importer, ProjectFbxImportProfile profile, string assetPath)
    {
        importer.animationType = profile.UseHumanoidRig
            ? ModelImporterAnimationType.Human
            : ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.importBlendShapes = false;
        importer.addCollider = false;
        importer.isReadable = false;
        importer.animationCompression = ModelImporterAnimationCompression.Optimal;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
            return;

        bool shouldLoop = ShouldLoopAnimation(assetPath);
        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];
            if (string.IsNullOrWhiteSpace(clip.name))
                clip.name = MakeClipName(assetPath, i, clips.Length);

            clip.loopTime = shouldLoop;
            clip.loopPose = shouldLoop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = false;
            clip.keepOriginalPositionY = false;
            clip.keepOriginalPositionXZ = false;
            clip.heightFromFeet = true;
            clips[i] = clip;
        }

        importer.clipAnimations = clips;
    }

    private static void ConfigureStaticModel(ModelImporter importer, ProjectFbxImportProfile profile)
    {
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.addCollider = profile.GenerateColliders;
        importer.generateSecondaryUV = profile.GenerateLightmapUv;
        importer.isReadable = false;
        importer.meshCompression = ModelImporterMeshCompression.Low;
    }

    private static bool ShouldLoopAnimation(string assetPath)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        return normalizedPath.IndexOf("/OneShot/", StringComparison.OrdinalIgnoreCase) < 0 &&
            normalizedPath.IndexOf("/OneShots/", StringComparison.OrdinalIgnoreCase) < 0 &&
            normalizedPath.IndexOf("/Cinematic/", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static string MakeClipName(string assetPath, int index, int totalCount)
    {
        string name = Path.GetFileNameWithoutExtension(assetPath);
        if (string.IsNullOrWhiteSpace(name))
            name = "ImportedClip";

        return totalCount > 1 ? $"{name}_{index + 1:00}" : name;
    }

    private static string[] MergeUserProperties(string[] current, params string[] required)
    {
        HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (current != null)
        {
            foreach (string value in current)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }
        }

        foreach (string value in required)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }

        return values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return (assetPath ?? string.Empty).Replace('\\', '/');
    }
}

internal static class ProjectFbxPostprocessRules
{
    public static void Apply(GameObject root, ProjectFbxImportProfile profile)
    {
        if (root == null)
            return;

        ProjectFbxCollisionBuilder.AddNamingConventionColliders(root);

        if (profile.HasLodNodes)
            ProjectFbxLodGroupBuilder.Build(root);
    }
}

internal static class ProjectFbxNaming
{
    private static readonly Regex LodRegex = new Regex(@"(?:^|[_\-.])LOD(?<index>\d+)(?:$|[_\-.])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsLodNode(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && LodRegex.IsMatch(name);
    }

    public static bool TryGetLodIndex(string name, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        Match match = LodRegex.Match(name);
        return match.Success && int.TryParse(match.Groups["index"].Value, out index);
    }

    public static bool IsCollisionNode(string name)
    {
        return TryGetCollisionKind(name, out _);
    }

    public static bool TryGetCollisionKind(string name, out ProjectFbxCollisionKind kind)
    {
        kind = ProjectFbxCollisionKind.None;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.StartsWith("UBX_", StringComparison.OrdinalIgnoreCase))
        {
            kind = ProjectFbxCollisionKind.Box;
            return true;
        }

        if (name.StartsWith("UCX_", StringComparison.OrdinalIgnoreCase))
        {
            kind = ProjectFbxCollisionKind.Mesh;
            return true;
        }

        if (name.StartsWith("USP_", StringComparison.OrdinalIgnoreCase))
        {
            kind = ProjectFbxCollisionKind.Sphere;
            return true;
        }

        if (name.StartsWith("UCP_", StringComparison.OrdinalIgnoreCase))
        {
            kind = ProjectFbxCollisionKind.Capsule;
            return true;
        }

        return false;
    }
}

internal enum ProjectFbxCollisionKind
{
    None,
    Box,
    Mesh,
    Sphere,
    Capsule
}

internal static class ProjectFbxCollisionBuilder
{
    public static void AddNamingConventionColliders(GameObject root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            if (!ProjectFbxNaming.TryGetCollisionKind(transform.name, out ProjectFbxCollisionKind kind))
                continue;

            AddCollider(transform.gameObject, kind);
            DisableHelperRenderers(transform.gameObject);
        }
    }

    public static void AddRequestedCollider(GameObject go, string requestedKind)
    {
        ProjectFbxCollisionKind kind = ParseKind(requestedKind);
        if (kind == ProjectFbxCollisionKind.None)
            return;

        AddCollider(go, kind);
    }

    private static void AddCollider(GameObject go, ProjectFbxCollisionKind kind)
    {
        if (go == null)
            return;

        switch (kind)
        {
            case ProjectFbxCollisionKind.Box:
                AddBoxCollider(go);
                break;
            case ProjectFbxCollisionKind.Mesh:
                AddMeshCollider(go);
                break;
            case ProjectFbxCollisionKind.Sphere:
                AddSphereCollider(go);
                break;
            case ProjectFbxCollisionKind.Capsule:
                AddCapsuleCollider(go);
                break;
        }
    }

    private static void AddBoxCollider(GameObject go)
    {
        if (go.GetComponent<Collider>() != null)
            return;

        BoxCollider collider = go.AddComponent<BoxCollider>();
        Bounds bounds = GetLocalBounds(go);
        collider.center = bounds.center;
        collider.size = bounds.size;
    }

    private static void AddMeshCollider(GameObject go)
    {
        if (go.GetComponent<Collider>() != null)
            return;

        Mesh mesh = GetSharedMesh(go);
        MeshCollider collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;
    }

    private static void AddSphereCollider(GameObject go)
    {
        if (go.GetComponent<Collider>() != null)
            return;

        SphereCollider collider = go.AddComponent<SphereCollider>();
        Bounds bounds = GetLocalBounds(go);
        collider.center = bounds.center;
        collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
    }

    private static void AddCapsuleCollider(GameObject go)
    {
        if (go.GetComponent<Collider>() != null)
            return;

        CapsuleCollider collider = go.AddComponent<CapsuleCollider>();
        Bounds bounds = GetLocalBounds(go);
        collider.center = bounds.center;
        collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        collider.height = Mathf.Max(bounds.size.y, collider.radius * 2.0f);
        collider.direction = 1;
    }

    private static ProjectFbxCollisionKind ParseKind(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ProjectFbxCollisionKind.None;

        string normalized = value.Trim();
        if (string.Equals(normalized, "Box", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "UBX", StringComparison.OrdinalIgnoreCase))
            return ProjectFbxCollisionKind.Box;

        if (string.Equals(normalized, "Mesh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ConvexMesh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "UCX", StringComparison.OrdinalIgnoreCase))
            return ProjectFbxCollisionKind.Mesh;

        if (string.Equals(normalized, "Sphere", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "USP", StringComparison.OrdinalIgnoreCase))
            return ProjectFbxCollisionKind.Sphere;

        if (string.Equals(normalized, "Capsule", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "UCP", StringComparison.OrdinalIgnoreCase))
            return ProjectFbxCollisionKind.Capsule;

        return ProjectFbxCollisionKind.None;
    }

    private static void DisableHelperRenderers(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
            renderer.enabled = false;
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

internal static class ProjectFbxLodGroupBuilder
{
    private static readonly float[] DefaultTransitionHeights =
    {
        0.6f,
        0.35f,
        0.18f,
        0.08f
    };

    public static void Build(GameObject root)
    {
        Dictionary<int, List<Renderer>> renderersByLod = CollectRenderers(root);
        if (renderersByLod.Count < 2)
            return;

        LOD[] lods = renderersByLod
            .OrderBy(pair => pair.Key)
            .Select(pair => new LOD(GetTransitionHeight(pair.Key), pair.Value.ToArray()))
            .ToArray();

        LODGroup lodGroup = root.GetComponent<LODGroup>();
        if (lodGroup == null)
            lodGroup = root.AddComponent<LODGroup>();

        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();
    }

    private static Dictionary<int, List<Renderer>> CollectRenderers(GameObject root)
    {
        Dictionary<int, List<Renderer>> renderersByLod = new Dictionary<int, List<Renderer>>();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled)
                continue;

            if (!TryFindLodIndex(renderer.transform, root.transform, out int lodIndex))
                continue;

            if (!renderersByLod.TryGetValue(lodIndex, out List<Renderer> group))
            {
                group = new List<Renderer>();
                renderersByLod.Add(lodIndex, group);
            }

            group.Add(renderer);
        }

        return renderersByLod;
    }

    private static bool TryFindLodIndex(Transform transform, Transform stopAt, out int lodIndex)
    {
        Transform current = transform;
        while (current != null)
        {
            if (ProjectFbxNaming.TryGetLodIndex(current.name, out lodIndex))
                return true;

            if (current == stopAt)
                break;

            current = current.parent;
        }

        lodIndex = -1;
        return false;
    }

    private static float GetTransitionHeight(int lodIndex)
    {
        if (lodIndex >= 0 && lodIndex < DefaultTransitionHeights.Length)
            return DefaultTransitionHeights[lodIndex];

        return Mathf.Max(0.01f, DefaultTransitionHeights[DefaultTransitionHeights.Length - 1] * Mathf.Pow(0.5f, lodIndex - DefaultTransitionHeights.Length + 1));
    }
}

internal static class ProjectFbxMaterialResolver
{
    private static readonly string[] SearchRoots =
    {
        "Assets/GameAssets",
        "Assets/GameResources",
        "Assets/ANGRY MESH",
        "Assets/ThirdParty"
    };

    private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

    public static Material Resolve(string materialSlotName, string assetPath)
    {
        string normalizedName = NormalizeMaterialName(materialSlotName);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        string cacheKey = $"{assetPath}|{normalizedName}";
        if (Cache.TryGetValue(cacheKey, out Material cached))
            return cached;

        Material material = FindMaterial(normalizedName);
        if (material == null && normalizedName.StartsWith("M_", StringComparison.OrdinalIgnoreCase))
            material = FindMaterial(normalizedName.Substring(2));

        Cache[cacheKey] = material;
        return material;
    }

    private static Material FindMaterial(string name)
    {
        string[] existingRoots = SearchRoots.Where(AssetDatabase.IsValidFolder).ToArray();
        if (existingRoots.Length == 0)
            return null;

        string[] guids = AssetDatabase.FindAssets($"{name} t:Material", existingRoots);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null && string.Equals(NormalizeMaterialName(material.name), NormalizeMaterialName(name), StringComparison.OrdinalIgnoreCase))
                return material;
        }

        return null;
    }

    private static string NormalizeMaterialName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string name = value.Trim();
        int instanceIndex = name.IndexOf(" (Instance)", StringComparison.OrdinalIgnoreCase);
        if (instanceIndex >= 0)
            name = name.Substring(0, instanceIndex);

        return name;
    }
}

internal static class ProjectFbxUserPropertyReader
{
    public static string FindString(string[] propNames, object[] values, string propertyName)
    {
        if (propNames == null || values == null || string.IsNullOrWhiteSpace(propertyName))
            return null;

        int count = Math.Min(propNames.Length, values.Length);
        for (int i = 0; i < count; i++)
        {
            if (!string.Equals(propNames[i], propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            return values[i]?.ToString();
        }

        return null;
    }
}
