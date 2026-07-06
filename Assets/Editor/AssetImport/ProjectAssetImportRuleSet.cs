using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public enum ProjectAssetClass
{
    Any,
    Model,
    Texture2D
}

public enum ProjectAssetStringMatchMode
{
    Any,
    Contains,
    StartsWith,
    EndsWith,
    Equals,
    Regex,
    Glob
}

public enum ProjectAssetPropertyValueKind
{
    Bool,
    Int,
    Float,
    String,
    Enum
}

[Serializable]
public sealed class ProjectAssetRuleFilter
{
    public ProjectAssetStringMatchMode directoryMatch = ProjectAssetStringMatchMode.Any;
    public string directoryPattern;
    public ProjectAssetStringMatchMode packageNameMatch = ProjectAssetStringMatchMode.Any;
    public string packageNamePattern;
    public ProjectAssetClass assetClass = ProjectAssetClass.Any;

    public bool Matches(ProjectAssetRuleContext context)
    {
        if (context == null)
            return false;

        return MatchesAssetClass(context.assetClass) &&
            ProjectAssetStringMatcher.Matches(directoryMatch, directoryPattern, context.directory) &&
            ProjectAssetStringMatcher.Matches(packageNameMatch, packageNamePattern, context.packageName);
    }

    private bool MatchesAssetClass(ProjectAssetClass candidate)
    {
        return assetClass == ProjectAssetClass.Any || assetClass == candidate;
    }
}

[Serializable]
public sealed class ProjectAssetPropertyItem
{
    public string propertyPath;
    public ProjectAssetPropertyValueKind valueKind;
    public string value;
}

[Serializable]
public sealed class ProjectAssetImportRule
{
    public string name;
    public bool enabled = true;
    public ProjectAssetRuleFilter filter = new ProjectAssetRuleFilter();
    public ProjectAssetPropertyItem[] propertyItems = Array.Empty<ProjectAssetPropertyItem>();

    public bool Matches(ProjectAssetRuleContext context)
    {
        return enabled && filter != null && filter.Matches(context);
    }
}

public sealed class ProjectAssetRuleContext
{
    public string assetPath;
    public string directory;
    public string packageName;
    public ProjectAssetClass assetClass;
    public AssetImporter importer;

    public static ProjectAssetRuleContext FromImporter(string assetPath, AssetImporter importer)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        return new ProjectAssetRuleContext
        {
            assetPath = normalizedPath,
            directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? string.Empty,
            packageName = Path.GetFileNameWithoutExtension(normalizedPath),
            assetClass = ResolveAssetClass(importer),
            importer = importer
        };
    }

    private static ProjectAssetClass ResolveAssetClass(AssetImporter importer)
    {
        return importer switch
        {
            ModelImporter => ProjectAssetClass.Model,
            TextureImporter => ProjectAssetClass.Texture2D,
            _ => ProjectAssetClass.Any
        };
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return (assetPath ?? string.Empty).Replace('\\', '/');
    }
}

[CreateAssetMenu(menuName = "Project/Asset Import/Asset Import Rule Set", fileName = "ProjectAssetImportRuleSet")]
public sealed class ProjectAssetImportRuleSet : ScriptableObject
{
    public const string DefaultAssetPath = "Assets/Editor/AssetImport/ProjectAssetImportRuleSet.asset";

    public bool applyAllMatchingRules = true;
    public ProjectAssetImportRule[] rules = Array.Empty<ProjectAssetImportRule>();

    public static ProjectAssetImportRuleSet LoadDefault()
    {
        ProjectAssetImportRuleSet ruleSet = AssetDatabase.LoadAssetAtPath<ProjectAssetImportRuleSet>(DefaultAssetPath);
        return ruleSet != null ? ruleSet : CreateDefaultInstance();
    }

    public static ProjectAssetImportRuleSet CreateDefaultInstance()
    {
        ProjectAssetImportRuleSet ruleSet = CreateInstance<ProjectAssetImportRuleSet>();
        ruleSet.applyAllMatchingRules = true;
        ruleSet.rules = CreateDefaultRules();
        return ruleSet;
    }

    public static ProjectAssetImportRule[] CreateDefaultRules()
    {
        return new[]
        {
            Rule(
                "Common Model Defaults",
                Filter(ProjectAssetClass.Model),
                ModelProperties(
                    ("ModelImporter.importCameras", "false"),
                    ("ModelImporter.importLights", "false"),
                    ("ModelImporter.importVisibility", "true"),
                    ("ModelImporter.sortHierarchyByName", "true"),
                    ("ModelImporter.extraUserProperties", "Collision;Collider"),
                    ("ModelImporter.animationType", "None"),
                    ("ModelImporter.importAnimation", "false"),
                    ("ModelImporter.importBlendShapes", "false"),
                    ("ModelImporter.addCollider", "false"),
                    ("ModelImporter.generateSecondaryUV", "false"),
                    ("ModelImporter.isReadable", "false"),
                    ("ModelImporter.meshCompression", "Low"),
                    ("ModelImporter.animationCompression", "Optimal"))),
            Rule(
                "Characters",
                Filter(
                    ProjectAssetClass.Model,
                    ProjectAssetStringMatchMode.Contains,
                    "Characters/"),
                ModelProperties(
                    ("ModelImporter.animationType", "Human"),
                    ("ModelImporter.avatarSetup", "CreateFromThisModel"),
                    ("ModelImporter.importAnimation", "true"),
                    ("ModelImporter.importBlendShapes", "true"),
                    ("ModelImporter.meshCompression", "Off"))),
            Rule(
                "Props",
                Filter(
                    ProjectAssetClass.Model,
                    ProjectAssetStringMatchMode.Contains,
                    "Props/"),
                ModelProperties(
                    ("ModelImporter.addCollider", "true"),
                    ("ModelImporter.generateSecondaryUV", "true"))),
            Rule(
                "Meadow Source Meshes",
                Filter(
                    ProjectAssetClass.Model,
                    ProjectAssetStringMatchMode.Contains,
                    "Stylized Pack - Meadow Environment/Sources/Meshes/"),
                ModelProperties(
                    ("ModelImporter.preserveHierarchy", "true"))),
            Rule(
                "LOD Filename",
                Filter(
                    ProjectAssetClass.Model,
                    packageNameMatch: ProjectAssetStringMatchMode.Contains,
                    packageNamePattern: "LOD"),
                ModelProperties(
                    ("ModelImporter.preserveHierarchy", "true"))),
            Rule(
                "At-Sign Animations",
                Filter(
                    ProjectAssetClass.Model,
                    packageNameMatch: ProjectAssetStringMatchMode.Contains,
                    packageNamePattern: "@"),
                ModelProperties(
                    ("ModelImporter.animationType", "Generic"),
                    ("ModelImporter.avatarSetup", "CreateFromThisModel"),
                    ("ModelImporter.importAnimation", "true"),
                    ("ModelImporter.importBlendShapes", "false"),
                    ("ModelImporter.meshCompression", "Off"),
                    ("ModelImporter.clipNameFromAsset", "true"))),
            Rule(
                "Character At-Sign Animations",
                Filter(
                    ProjectAssetClass.Model,
                    ProjectAssetStringMatchMode.Contains,
                    "Characters/",
                    ProjectAssetStringMatchMode.Contains,
                    "@"),
                ModelProperties(
                    ("ModelImporter.animationType", "Human"),
                    ("ModelImporter.avatarSetup", "CreateFromThisModel"),
                    ("ModelImporter.importAnimation", "true"),
                    ("ModelImporter.importBlendShapes", "false"),
                    ("ModelImporter.meshCompression", "Off"),
                    ("ModelImporter.clipNameFromAsset", "true"))),
            Rule(
                "Character Source Animations",
                Filter(
                    ProjectAssetClass.Model,
                    ProjectAssetStringMatchMode.Regex,
                    @"Characters/.*/SourceAnimations(/|$)"),
                ModelProperties(
                    ("ModelImporter.animationType", "Human"),
                    ("ModelImporter.avatarSetup", "CreateFromThisModel"),
                    ("ModelImporter.importAnimation", "true"),
                    ("ModelImporter.importBlendShapes", "false"),
                    ("ModelImporter.meshCompression", "Off"),
                    ("ModelImporter.clipNameFromAsset", "true"))),
            Rule(
                "Normal Textures",
                Filter(
                    ProjectAssetClass.Texture2D,
                    packageNameMatch: ProjectAssetStringMatchMode.EndsWith,
                    packageNamePattern: "_N"),
                TextureProperties(
                    ("TextureImporter.textureType", "NormalMap"),
                    ("TextureImporter.wrapModeU", "Clamp"),
                    ("TextureImporter.wrapModeV", "Clamp")))
        };
    }

    public ProjectAssetImportRule[] ResolveRules(ProjectAssetRuleContext context)
    {
        List<ProjectAssetImportRule> matches = new List<ProjectAssetImportRule>();
        if (rules == null)
            return matches.ToArray();

        foreach (ProjectAssetImportRule rule in rules)
        {
            if (rule == null || !rule.Matches(context))
                continue;

            matches.Add(rule);
            if (!applyAllMatchingRules)
                break;
        }

        return matches.ToArray();
    }

    public void Apply(AssetImporter importer, string assetPath)
    {
        if (importer == null)
            return;

        ProjectAssetRuleContext context = ProjectAssetRuleContext.FromImporter(assetPath, importer);
        foreach (ProjectAssetImportRule rule in ResolveRules(context))
            ProjectAssetPropertyApplier.Apply(importer, assetPath, rule.propertyItems);
    }

    private static ProjectAssetImportRule Rule(
        string name,
        ProjectAssetRuleFilter filter,
        ProjectAssetPropertyItem[] propertyItems)
    {
        return new ProjectAssetImportRule
        {
            name = name,
            enabled = true,
            filter = filter,
            propertyItems = propertyItems
        };
    }

    private static ProjectAssetRuleFilter Filter(
        ProjectAssetClass assetClass,
        ProjectAssetStringMatchMode directoryMatch = ProjectAssetStringMatchMode.Any,
        string directoryPattern = null,
        ProjectAssetStringMatchMode packageNameMatch = ProjectAssetStringMatchMode.Any,
        string packageNamePattern = null)
    {
        return new ProjectAssetRuleFilter
        {
            assetClass = assetClass,
            directoryMatch = directoryMatch,
            directoryPattern = directoryPattern,
            packageNameMatch = packageNameMatch,
            packageNamePattern = packageNamePattern
        };
    }

    private static ProjectAssetPropertyItem[] ModelProperties(params (string propertyPath, string value)[] values)
    {
        return Properties(ProjectAssetPropertyValueKind.Enum, values);
    }

    private static ProjectAssetPropertyItem[] TextureProperties(params (string propertyPath, string value)[] values)
    {
        return Properties(ProjectAssetPropertyValueKind.Enum, values);
    }

    private static ProjectAssetPropertyItem[] Properties(
        ProjectAssetPropertyValueKind defaultKind,
        params (string propertyPath, string value)[] values)
    {
        ProjectAssetPropertyItem[] items = new ProjectAssetPropertyItem[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            items[i] = new ProjectAssetPropertyItem
            {
                propertyPath = values[i].propertyPath,
                valueKind = GuessValueKind(values[i].value, defaultKind),
                value = values[i].value
            };
        }

        return items;
    }

    private static ProjectAssetPropertyValueKind GuessValueKind(string value, ProjectAssetPropertyValueKind defaultKind)
    {
        if (bool.TryParse(value, out _))
            return ProjectAssetPropertyValueKind.Bool;
        if (int.TryParse(value, out _))
            return ProjectAssetPropertyValueKind.Int;
        if (float.TryParse(value, out _))
            return ProjectAssetPropertyValueKind.Float;

        return defaultKind;
    }
}

internal static class ProjectAssetStringMatcher
{
    public static bool Matches(ProjectAssetStringMatchMode matchMode, string pattern, string value)
    {
        if (matchMode == ProjectAssetStringMatchMode.Any)
            return true;
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        string candidate = value ?? string.Empty;
        switch (matchMode)
        {
            case ProjectAssetStringMatchMode.Contains:
                return candidate.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            case ProjectAssetStringMatchMode.StartsWith:
                return candidate.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
            case ProjectAssetStringMatchMode.EndsWith:
                return candidate.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
            case ProjectAssetStringMatchMode.Equals:
                return string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase);
            case ProjectAssetStringMatchMode.Regex:
                return Regex.IsMatch(candidate, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            case ProjectAssetStringMatchMode.Glob:
                return ProjectAssetRuleGlob.Matches(pattern, candidate);
            default:
                return false;
        }
    }
}

internal static class ProjectAssetPropertyApplier
{
    public static void Apply(AssetImporter importer, string assetPath, ProjectAssetPropertyItem[] propertyItems)
    {
        if (importer == null || propertyItems == null)
            return;

        foreach (ProjectAssetPropertyItem item in propertyItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.propertyPath))
                continue;

            try
            {
                if (TryApplyKnownProperty(importer, assetPath, item))
                    continue;

                if (!TryApplySerializedProperty(importer, item, out string error))
                    Debug.LogWarning($"Asset import rule skipped property '{item.propertyPath}' on {assetPath}: {error}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Asset import rule skipped property '{item.propertyPath}' on {assetPath}: {exception.Message}");
            }
        }
    }

    private static bool TryApplyKnownProperty(AssetImporter importer, string assetPath, ProjectAssetPropertyItem item)
    {
        string key = NormalizePropertyPath(item.propertyPath);
        if (importer is ModelImporter modelImporter && TryApplyModelImporter(modelImporter, assetPath, key, item))
            return true;
        if (importer is TextureImporter textureImporter && TryApplyTextureImporter(textureImporter, key, item))
            return true;
        if (importer is ModelImporter reflectedModelImporter && TryApplyPublicImporterProperty(reflectedModelImporter, item, "ModelImporter"))
            return true;
        if (importer is TextureImporter reflectedTextureImporter && TryApplyPublicImporterProperty(reflectedTextureImporter, item, "TextureImporter"))
            return true;

        return false;
    }

    private static bool TryApplyPublicImporterProperty(AssetImporter importer, ProjectAssetPropertyItem item, string prefix)
    {
        string propertyPath = item.propertyPath?.Trim();
        string prefixWithDot = prefix + ".";
        if (string.IsNullOrWhiteSpace(propertyPath) ||
            !propertyPath.StartsWith(prefixWithDot, StringComparison.OrdinalIgnoreCase))
            return false;

        string propertyName = propertyPath.Substring(prefixWithDot.Length);
        PropertyInfo property = importer.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property == null || property.SetMethod == null)
            return false;

        if (!TryConvertPublicPropertyValue(property.PropertyType, item.value, out object convertedValue))
            return false;

        property.SetValue(importer, convertedValue);
        return true;
    }

    private static bool TryApplyModelImporter(ModelImporter importer, string assetPath, string key, ProjectAssetPropertyItem item)
    {
        switch (key)
        {
            case "modelimporter.globalscale":
                importer.globalScale = ParseFloat(item.value);
                return true;
            case "modelimporter.usefilescale":
                importer.useFileScale = ParseBool(item.value);
                return true;
            case "modelimporter.bakeaxisconversion":
                importer.bakeAxisConversion = ParseBool(item.value);
                return true;
            case "modelimporter.importcameras":
                importer.importCameras = ParseBool(item.value);
                return true;
            case "modelimporter.importlights":
                importer.importLights = ParseBool(item.value);
                return true;
            case "modelimporter.importvisibility":
                importer.importVisibility = ParseBool(item.value);
                return true;
            case "modelimporter.sorthierarchybyname":
                importer.sortHierarchyByName = ParseBool(item.value);
                return true;
            case "modelimporter.extrauserproperties":
                importer.extraUserProperties = MergeUserProperties(importer.extraUserProperties, item.value);
                return true;
            case "modelimporter.preservehierarchy":
                importer.preserveHierarchy = ParseBool(item.value);
                return true;
            case "modelimporter.importanimation":
                importer.importAnimation = ParseBool(item.value);
                return true;
            case "modelimporter.importblendshapes":
                importer.importBlendShapes = ParseBool(item.value);
                return true;
            case "modelimporter.addcollider":
            case "modelimporter.generatecolliders":
                importer.addCollider = ParseBool(item.value);
                return true;
            case "modelimporter.generateseconduv":
            case "modelimporter.generatelightmapuv":
                importer.generateSecondaryUV = ParseBool(item.value);
                return true;
            case "modelimporter.isreadable":
            case "modelimporter.readwrite":
                importer.isReadable = ParseBool(item.value);
                return true;
            case "modelimporter.meshcompression":
                importer.meshCompression = ParseEnum<ModelImporterMeshCompression>(item.value);
                return true;
            case "modelimporter.animationcompression":
                importer.animationCompression = ParseEnum<ModelImporterAnimationCompression>(item.value);
                return true;
            case "modelimporter.animationtype":
            case "modelimporter.rig":
                importer.animationType = ParseModelAnimationType(item.value);
                return true;
            case "modelimporter.avatarsetup":
                importer.avatarSetup = ParseEnum<ModelImporterAvatarSetup>(item.value);
                return true;
            case "modelimporter.clipnamefromasset":
                if (ParseBool(item.value))
                    ApplyAnimationClipDefaults(importer, assetPath);
                return true;
            default:
                return false;
        }
    }

    private static bool TryApplyTextureImporter(TextureImporter importer, string key, ProjectAssetPropertyItem item)
    {
        switch (key)
        {
            case "textureimporter.texturetype":
                importer.textureType = ParseEnum<TextureImporterType>(item.value);
                return true;
            case "textureimporter.wrapmode":
            case "textureimporter.tilingmethod":
                importer.wrapMode = ParseEnum<TextureWrapMode>(item.value);
                return true;
            case "textureimporter.wrapmodeu":
            case "textureimporter.addressx":
                importer.wrapModeU = ParseEnum<TextureWrapMode>(item.value);
                return true;
            case "textureimporter.wrapmodev":
            case "textureimporter.addressy":
                importer.wrapModeV = ParseEnum<TextureWrapMode>(item.value);
                return true;
            case "textureimporter.wrapmodew":
            case "textureimporter.addressz":
                importer.wrapModeW = ParseEnum<TextureWrapMode>(item.value);
                return true;
            case "textureimporter.texturecompression":
            case "textureimporter.compressionsettings":
                importer.textureCompression = ParseEnum<TextureImporterCompression>(item.value);
                return true;
            case "textureimporter.srgbtexture":
                importer.sRGBTexture = ParseBool(item.value);
                return true;
            case "textureimporter.mipmapenabled":
                importer.mipmapEnabled = ParseBool(item.value);
                return true;
            case "textureimporter.isreadable":
            case "textureimporter.readwrite":
                importer.isReadable = ParseBool(item.value);
                return true;
            default:
                return false;
        }
    }

    private static bool TryApplySerializedProperty(AssetImporter importer, ProjectAssetPropertyItem item, out string error)
    {
        SerializedObject serializedObject = new SerializedObject(importer);
        SerializedProperty property = serializedObject.FindProperty(item.propertyPath);
        if (property == null)
        {
            error = "serialized property was not found";
            return false;
        }

        switch (property.propertyType)
        {
            case SerializedPropertyType.Boolean:
                property.boolValue = ParseBool(item.value);
                break;
            case SerializedPropertyType.Integer:
                property.intValue = ParseInt(item.value);
                break;
            case SerializedPropertyType.Float:
                property.floatValue = ParseFloat(item.value);
                break;
            case SerializedPropertyType.String:
                property.stringValue = item.value ?? string.Empty;
                break;
            case SerializedPropertyType.Enum:
                property.enumValueIndex = ResolveSerializedEnumIndex(property, item.value);
                break;
            default:
                error = $"unsupported serialized property type {property.propertyType}";
                return false;
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        error = null;
        return true;
    }

    private static ModelImporterAnimationType ParseModelAnimationType(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (string.Equals(normalized, "Humanoid", StringComparison.OrdinalIgnoreCase))
            return ModelImporterAnimationType.Human;

        return ParseEnum<ModelImporterAnimationType>(normalized);
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct
    {
        if (Enum.TryParse(value, true, out TEnum result) && Enum.IsDefined(typeof(TEnum), result))
            return result;

        throw new ArgumentException($"Unsupported {typeof(TEnum).Name} value: {value}");
    }

    private static bool TryConvertPublicPropertyValue(Type propertyType, string value, out object convertedValue)
    {
        convertedValue = null;

        try
        {
            if (propertyType == typeof(bool))
            {
                convertedValue = ParseBool(value);
                return true;
            }

            if (propertyType == typeof(int))
            {
                convertedValue = ParseInt(value);
                return true;
            }

            if (propertyType == typeof(float))
            {
                convertedValue = ParseFloat(value);
                return true;
            }

            if (propertyType == typeof(string))
            {
                convertedValue = value ?? string.Empty;
                return true;
            }

            if (propertyType.IsEnum)
            {
                convertedValue = Enum.Parse(propertyType, value ?? string.Empty, true);
                return true;
            }
        }
        catch
        {
            convertedValue = null;
            return false;
        }

        return false;
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out bool result))
            return result;
        if (int.TryParse(value, out int intValue))
            return intValue != 0;

        throw new ArgumentException($"Unsupported bool value: {value}");
    }

    private static int ParseInt(string value)
    {
        if (int.TryParse(value, out int result))
            return result;

        throw new ArgumentException($"Unsupported int value: {value}");
    }

    private static float ParseFloat(string value)
    {
        if (float.TryParse(value, out float result))
            return result;

        throw new ArgumentException($"Unsupported float value: {value}");
    }

    private static int ResolveSerializedEnumIndex(SerializedProperty property, string value)
    {
        for (int i = 0; i < property.enumNames.Length; i++)
        {
            if (string.Equals(property.enumNames[i], value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.enumDisplayNames[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return ParseInt(value);
    }

    private static string[] MergeUserProperties(string[] existingProperties, string value)
    {
        string[] requiredProperties = (value ?? string.Empty)
            .Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        string[] existing = existingProperties ?? Array.Empty<string>();
        string[] merged = new string[existing.Length + requiredProperties.Length];
        int count = 0;

        foreach (string property in existing)
            AddUnique(merged, ref count, property);
        foreach (string property in requiredProperties)
            AddUnique(merged, ref count, property);

        Array.Resize(ref merged, count);
        return merged;
    }

    private static void AddUnique(string[] values, ref int count, string value)
    {
        string trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        for (int i = 0; i < count; i++)
        {
            if (string.Equals(values[i], trimmed, StringComparison.Ordinal))
                return;
        }

        values[count++] = trimmed;
    }

    private static void ApplyAnimationClipDefaults(ModelImporter importer, string assetPath)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        if (clips == null || clips.Length == 0)
            return;

        string clipBaseName = Path.GetFileNameWithoutExtension(assetPath);
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

    private static string NormalizePropertyPath(string propertyPath)
    {
        return (propertyPath ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }
}

internal static class ProjectAssetRuleGlob
{
    public static bool Matches(string glob, string value)
    {
        if (string.IsNullOrWhiteSpace(glob))
            return false;

        string regex = "^" + ToRegex(glob.Replace('\\', '/')) + "$";
        string normalizedValue = (value ?? string.Empty).Replace('\\', '/');
        return Regex.IsMatch(normalizedValue, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
                    builder.Append(".*");
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
}
