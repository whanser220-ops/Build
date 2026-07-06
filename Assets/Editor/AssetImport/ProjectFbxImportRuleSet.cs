using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public enum ProjectFbxImportKind
{
    StaticModel,
    CharacterModel,
    AnimationAsset
}

public enum ProjectFbxImportRig
{
    None,
    Generic,
    Humanoid
}

[Serializable]
public sealed class ProjectFbxImportRule
{
    public string name;
    public string glob;
    public ProjectFbxImportKind kind;
    public ProjectFbxImportRig rig;
    public bool preserveHierarchy;
    public bool importAnimation;
    public bool importBlendShapes;
    public bool generateLightmapUv;
    public bool generateColliders;
    public bool readWrite;
    public ModelImporterMeshCompression meshCompression;
    public ModelImporterAnimationCompression animationCompression;

    public bool Matches(string assetPath)
    {
        return ProjectFbxImportRuleGlob.Matches(glob, assetPath);
    }
}

[CreateAssetMenu(menuName = "Project/Asset Import/FBX Import Rule Set", fileName = "ProjectFbxImportRuleSet")]
public sealed class ProjectFbxImportRuleSet : ScriptableObject
{
    public const string DefaultAssetPath = "Assets/Editor/AssetImport/ProjectFbxImportRuleSet.asset";

    public ProjectFbxImportRule[] rules = Array.Empty<ProjectFbxImportRule>();

    public static ProjectFbxImportRuleSet LoadDefault()
    {
        ProjectFbxImportRuleSet ruleSet = AssetDatabase.LoadAssetAtPath<ProjectFbxImportRuleSet>(DefaultAssetPath);
        return ruleSet != null ? ruleSet : CreateDefaultInstance();
    }

    public static ProjectFbxImportRuleSet CreateDefaultInstance()
    {
        ProjectFbxImportRuleSet ruleSet = CreateInstance<ProjectFbxImportRuleSet>();
        ruleSet.rules = CreateDefaultRules();
        return ruleSet;
    }

    public static ProjectFbxImportRule[] CreateDefaultRules()
    {
        return new[]
        {
            Rule(
                "Character Source Animations",
                "Characters/**/SourceAnimations/**/*.fbx",
                ProjectFbxImportKind.AnimationAsset,
                ProjectFbxImportRig.Humanoid,
                preserveHierarchy: false,
                importAnimation: true,
                importBlendShapes: false,
                generateLightmapUv: false,
                generateColliders: false,
                readWrite: false,
                meshCompression: ModelImporterMeshCompression.Off,
                animationCompression: ModelImporterAnimationCompression.Optimal),
            Rule(
                "Character At-Sign Animations",
                "Characters/**/*@*.fbx",
                ProjectFbxImportKind.AnimationAsset,
                ProjectFbxImportRig.Humanoid,
                preserveHierarchy: false,
                importAnimation: true,
                importBlendShapes: false,
                generateLightmapUv: false,
                generateColliders: false,
                readWrite: false,
                meshCompression: ModelImporterMeshCompression.Off,
                animationCompression: ModelImporterAnimationCompression.Optimal),
            Rule(
                "At-Sign Animations",
                "*@*.fbx",
                ProjectFbxImportKind.AnimationAsset,
                ProjectFbxImportRig.Generic,
                preserveHierarchy: false,
                importAnimation: true,
                importBlendShapes: false,
                generateLightmapUv: false,
                generateColliders: false,
                readWrite: false,
                meshCompression: ModelImporterMeshCompression.Off,
                animationCompression: ModelImporterAnimationCompression.Optimal),
            Rule(
                "Characters",
                "Characters/**/*.fbx",
                ProjectFbxImportKind.CharacterModel,
                ProjectFbxImportRig.Humanoid,
                preserveHierarchy: false,
                importAnimation: true,
                importBlendShapes: true,
                generateLightmapUv: false,
                generateColliders: false,
                readWrite: false,
                meshCompression: ModelImporterMeshCompression.Off,
                animationCompression: ModelImporterAnimationCompression.Optimal),
            Rule(
                "Props",
                "Props/**/*.fbx",
                ProjectFbxImportKind.StaticModel,
                ProjectFbxImportRig.None,
                preserveHierarchy: false,
                importAnimation: false,
                importBlendShapes: false,
                generateLightmapUv: true,
                generateColliders: true,
                readWrite: false,
                meshCompression: ModelImporterMeshCompression.Low,
                animationCompression: ModelImporterAnimationCompression.Optimal),
            Rule(
                "Meadow Source Meshes",
                "Stylized Pack - Meadow Environment/Sources/Meshes/**/*.fbx",
                ProjectFbxImportKind.StaticModel,
                ProjectFbxImportRig.None,
                preserveHierarchy: true,
                importAnimation: false,
                importBlendShapes: false,
                generateLightmapUv: false,
                generateColliders: false,
                readWrite: false,
                meshCompression: ModelImporterMeshCompression.Low,
                animationCompression: ModelImporterAnimationCompression.Optimal),
            Rule(
                "LOD Filename",
                "*LOD*.fbx",
                ProjectFbxImportKind.StaticModel,
                ProjectFbxImportRig.None,
                preserveHierarchy: true,
                importAnimation: false,
                importBlendShapes: false,
                generateLightmapUv: false,
                generateColliders: false,
                readWrite: false,
                meshCompression: ModelImporterMeshCompression.Low,
                animationCompression: ModelImporterAnimationCompression.Optimal),
            CreateFallbackRule()
        };
    }

    public ProjectFbxImportRule ResolveRule(string assetPath)
    {
        if (rules != null)
        {
            foreach (ProjectFbxImportRule rule in rules)
            {
                if (rule != null && rule.Matches(assetPath))
                    return rule;
            }
        }

        return CreateFallbackRule();
    }

    public static ProjectFbxImportRule CreateFallbackRule()
    {
        return Rule(
            "Fallback Static Model",
            "*.fbx",
            ProjectFbxImportKind.StaticModel,
            ProjectFbxImportRig.None,
            preserveHierarchy: false,
            importAnimation: false,
            importBlendShapes: false,
            generateLightmapUv: false,
            generateColliders: false,
            readWrite: false,
            meshCompression: ModelImporterMeshCompression.Low,
            animationCompression: ModelImporterAnimationCompression.Optimal);
    }

    private static ProjectFbxImportRule Rule(
        string name,
        string glob,
        ProjectFbxImportKind kind,
        ProjectFbxImportRig rig,
        bool preserveHierarchy,
        bool importAnimation,
        bool importBlendShapes,
        bool generateLightmapUv,
        bool generateColliders,
        bool readWrite,
        ModelImporterMeshCompression meshCompression,
        ModelImporterAnimationCompression animationCompression)
    {
        return new ProjectFbxImportRule
        {
            name = name,
            glob = glob,
            kind = kind,
            rig = rig,
            preserveHierarchy = preserveHierarchy,
            importAnimation = importAnimation,
            importBlendShapes = importBlendShapes,
            generateLightmapUv = generateLightmapUv,
            generateColliders = generateColliders,
            readWrite = readWrite,
            meshCompression = meshCompression,
            animationCompression = animationCompression
        };
    }
}

internal static class ProjectFbxImportSettingsApplier
{
    public static void Apply(ModelImporter importer, ProjectFbxImportRule rule, string assetPath)
    {
        if (importer == null)
            return;

        rule ??= ProjectFbxImportRuleSet.CreateFallbackRule();

        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = true;
        importer.sortHierarchyByName = true;
        importer.extraUserProperties = MergeUserProperties(importer.extraUserProperties, "Collision", "Collider");

        importer.preserveHierarchy = rule.preserveHierarchy;
        importer.importAnimation = rule.importAnimation;
        importer.importBlendShapes = rule.importBlendShapes;
        importer.addCollider = rule.generateColliders;
        importer.generateSecondaryUV = rule.generateLightmapUv;
        importer.isReadable = rule.readWrite;
        importer.meshCompression = rule.meshCompression;
        importer.animationCompression = rule.animationCompression;
        ApplyRig(importer, rule.rig);

        if (rule.kind == ProjectFbxImportKind.AnimationAsset)
            ApplyAnimationClipDefaults(importer, assetPath);
    }

    public static ModelImporterAnimationType ResolveAnimationType(ProjectFbxImportRig rig)
    {
        return rig switch
        {
            ProjectFbxImportRig.Humanoid => ModelImporterAnimationType.Human,
            ProjectFbxImportRig.Generic => ModelImporterAnimationType.Generic,
            _ => ModelImporterAnimationType.None
        };
    }

    private static void ApplyRig(ModelImporter importer, ProjectFbxImportRig rig)
    {
        importer.animationType = ResolveAnimationType(rig);
        if (TryResolveAvatarSetup(rig, out ModelImporterAvatarSetup avatarSetup))
            importer.avatarSetup = avatarSetup;
    }

    private static bool TryResolveAvatarSetup(ProjectFbxImportRig rig, out ModelImporterAvatarSetup avatarSetup)
    {
        if (rig == ProjectFbxImportRig.Humanoid || rig == ProjectFbxImportRig.Generic)
        {
            avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            return true;
        }

        avatarSetup = default;
        return false;
    }

    private static void ApplyAnimationClipDefaults(ModelImporter importer, string assetPath)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
            return;

        string clipBaseName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
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

    private static string[] MergeUserProperties(string[] existingProperties, params string[] requiredProperties)
    {
        if (requiredProperties == null || requiredProperties.Length == 0)
            return existingProperties ?? Array.Empty<string>();

        string[] existing = existingProperties ?? Array.Empty<string>();
        string[] merged = new string[existing.Length + requiredProperties.Length];
        int count = 0;

        foreach (string property in existing)
        {
            if (string.IsNullOrWhiteSpace(property) || Contains(merged, count, property))
                continue;

            merged[count++] = property;
        }

        foreach (string property in requiredProperties)
        {
            if (string.IsNullOrWhiteSpace(property) || Contains(merged, count, property))
                continue;

            merged[count++] = property;
        }

        Array.Resize(ref merged, count);
        return merged;
    }

    private static bool Contains(string[] values, int count, string value)
    {
        for (int i = 0; i < count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

internal static class ProjectFbxImportRuleGlob
{
    public static bool Matches(string glob, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(glob) || string.IsNullOrWhiteSpace(assetPath))
            return false;

        string pattern = Normalize(glob).Trim();
        string normalizedPath = Normalize(assetPath);
        bool patternTargetsPath = pattern.IndexOf('/') >= 0;
        string target = patternTargetsPath ? normalizedPath : System.IO.Path.GetFileName(normalizedPath);
        if (patternTargetsPath && ShouldAutoPrefixAnyDirectory(pattern))
            pattern = "**/" + pattern.TrimStart('/');

        string regex = "^" + ToRegex(pattern) + "$";
        return Regex.IsMatch(target, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ShouldAutoPrefixAnyDirectory(string pattern)
    {
        return !pattern.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            !pattern.StartsWith("**/", StringComparison.Ordinal) &&
            !pattern.StartsWith("*", StringComparison.Ordinal);
    }

    private static string ToRegex(string glob)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*')
            {
                bool isDoubleStar = i + 1 < glob.Length && glob[i + 1] == '*';
                if (isDoubleStar)
                {
                    i++;
                    if (i + 1 < glob.Length && glob[i + 1] == '/')
                    {
                        builder.Append("(?:.*/)?");
                        i++;
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        return builder.ToString();
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Replace('\\', '/');
    }
}
