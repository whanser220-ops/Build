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
public sealed class ProjectAssetImportSetting
{
    public bool overrideEnabled;
    public ProjectAssetPropertyValueKind valueKind;
    public string value;

    public void Set(ProjectAssetPropertyValueKind kind, string settingValue, bool enabled = true)
    {
        overrideEnabled = enabled;
        valueKind = kind;
        value = settingValue;
    }

    public ProjectAssetPropertyItem ToPropertyItem(ProjectAssetImportSettingDefinition definition)
    {
        return new ProjectAssetPropertyItem
        {
            propertyPath = definition.propertyPath,
            valueKind = definition.valueKind,
            value = string.IsNullOrEmpty(value) ? definition.defaultValue : value
        };
    }
}

[Serializable]
public sealed class ProjectModelImportSettings
{
    public ProjectModelSceneImportSettings model = new ProjectModelSceneImportSettings();
    public ProjectModelMeshImportSettings meshes = new ProjectModelMeshImportSettings();
    public ProjectModelRigImportSettings rig = new ProjectModelRigImportSettings();
    public ProjectModelAnimationImportSettings animation = new ProjectModelAnimationImportSettings();
    public ProjectModelMaterialImportSettings materials = new ProjectModelMaterialImportSettings();
}

[Serializable]
public sealed class ProjectModelSceneImportSettings
{
    public ProjectAssetImportSetting globalScale = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting useFileScale = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting useFileUnits = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting bakeAxisConversion = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importBlendShapes = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importBlendShapeNormals = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importBlendShapeDeformPercent = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importVisibility = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importCameras = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importLights = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting preserveHierarchy = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting sortHierarchyByName = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting extraUserProperties = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectModelMeshImportSettings
{
    public ProjectAssetImportSetting meshCompression = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting isReadable = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting optimizeMesh = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting optimizeMeshPolygons = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting optimizeMeshVertices = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting meshOptimizationFlags = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting addCollider = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting keepQuads = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting weldVertices = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting indexFormat = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importNormals = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting normalImportMode = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting normalCalculationMode = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting normalSmoothingSource = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting normalSmoothingAngle = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importTangents = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting tangentImportMode = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting splitTangentsAcrossSeams = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting swapUVChannels = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting strictVertexDataChecks = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting generateSecondaryUV = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting secondaryUVMarginMethod = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting secondaryUVHardAngle = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting secondaryUVPackMargin = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting secondaryUVAngleDistortion = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting secondaryUVAreaDistortion = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting secondaryUVMinLightmapResolution = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting secondaryUVMinObjectScale = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectModelRigImportSettings
{
    public ProjectAssetImportSetting animationType = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting avatarSetup = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting skinWeights = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting maxBonesPerVertex = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting minBoneWeight = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting optimizeGameObjects = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting optimizeBones = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting autoGenerateAvatarMappingIfUnspecified = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting humanoidOversampling = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting bakeIK = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectModelAnimationImportSettings
{
    public ProjectAssetImportSetting importAnimation = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting animationCompression = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting animationRotationError = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting animationPositionError = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting animationScaleError = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting animationWrapMode = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting generateAnimations = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importConstraints = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting importAnimatedCustomProperties = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting resampleCurves = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting resampleRotations = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting removeConstantScaleCurves = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting splitAnimations = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting motionNodeName = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting clipNameFromAsset = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectModelMaterialImportSettings
{
    public ProjectAssetImportSetting materialImportMode = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting materialLocation = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting materialName = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting materialSearch = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting useSRGBMaterialColor = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectTextureImportSettings
{
    public ProjectTextureMainImportSettings texture = new ProjectTextureMainImportSettings();
    public ProjectTextureAdvancedImportSettings advanced = new ProjectTextureAdvancedImportSettings();
    public ProjectTextureSpriteImportSettings sprite = new ProjectTextureSpriteImportSettings();
    public ProjectTextureSwizzleImportSettings swizzle = new ProjectTextureSwizzleImportSettings();
}

[Serializable]
public sealed class ProjectTextureMainImportSettings
{
    public ProjectAssetImportSetting textureType = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting textureShape = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting sRGBTexture = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting alphaSource = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting alphaIsTransparency = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting ignorePngGamma = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting isReadable = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting vtOnly = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting maxTextureSize = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting textureCompression = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting compressionQuality = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting crunchedCompression = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting textureFormat = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting androidETC2FallbackOverride = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting allowAlphaSplitting = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectTextureAdvancedImportSettings
{
    public ProjectAssetImportSetting mipmapEnabled = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting borderMipmap = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting mipmapFilter = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting mipMapBias = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting mipMapsPreserveCoverage = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting alphaTestReferenceValue = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting fadeout = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting mipmapFadeDistanceStart = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting mipmapFadeDistanceEnd = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting generateMipsInLinearSpace = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting mipmapLimitGroupName = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting ignoreMipmapLimit = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting streamingMipmaps = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting streamingMipmapsPriority = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting convertToNormalmap = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting heightmapScale = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting normalmapFilter = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting flipGreenChannel = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting wrapMode = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting wrapModeU = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting wrapModeV = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting wrapModeW = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting filterMode = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting anisoLevel = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting npotScale = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting generateCubemap = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting correctGamma = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting grayscaleToAlpha = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting lightmap = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting linearTexture = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting normalmap = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectTextureSpriteImportSettings
{
    public ProjectAssetImportSetting spriteImportMode = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting spritePixelsPerUnit = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting spritePixelsToUnits = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting spritePackingTag = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectTextureSwizzleImportSettings
{
    public ProjectAssetImportSetting swizzleR = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting swizzleG = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting swizzleB = new ProjectAssetImportSetting();
    public ProjectAssetImportSetting swizzleA = new ProjectAssetImportSetting();
}

[Serializable]
public sealed class ProjectCustomImportSettings
{
    public ProjectAssetPropertyItem[] propertyItems = Array.Empty<ProjectAssetPropertyItem>();

    public void AddOrReplace(ProjectAssetPropertyItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.propertyPath))
            return;

        propertyItems ??= Array.Empty<ProjectAssetPropertyItem>();
        for (int i = 0; i < propertyItems.Length; i++)
        {
            if (string.Equals(propertyItems[i].propertyPath, item.propertyPath, StringComparison.OrdinalIgnoreCase))
            {
                propertyItems[i] = CloneItem(item);
                return;
            }
        }

        Array.Resize(ref propertyItems, propertyItems.Length + 1);
        propertyItems[propertyItems.Length - 1] = CloneItem(item);
    }

    private static ProjectAssetPropertyItem CloneItem(ProjectAssetPropertyItem item)
    {
        return new ProjectAssetPropertyItem
        {
            propertyPath = item.propertyPath,
            valueKind = item.valueKind,
            value = item.value
        };
    }
}

public sealed class ProjectAssetImportSettingDefinition
{
    public readonly bool isSection;
    public readonly ProjectAssetClass assetClass;
    public readonly string tab;
    public readonly string settingPath;
    public readonly string propertyPath;
    public readonly string label;
    public readonly ProjectAssetPropertyValueKind valueKind;
    public readonly string defaultValue;
    public readonly string[] options;
    public readonly string visibleWhenSettingPath;
    public readonly string[] visibleWhenValues;

    private ProjectAssetImportSettingDefinition(
        bool isSection,
        ProjectAssetClass assetClass,
        string tab,
        string settingPath,
        string propertyPath,
        string label,
        ProjectAssetPropertyValueKind valueKind,
        string defaultValue,
        string[] options,
        string visibleWhenSettingPath = null,
        string[] visibleWhenValues = null)
    {
        this.isSection = isSection;
        this.assetClass = assetClass;
        this.tab = tab;
        this.settingPath = settingPath;
        this.propertyPath = propertyPath;
        this.label = label;
        this.valueKind = valueKind;
        this.defaultValue = defaultValue;
        this.options = options ?? Array.Empty<string>();
        this.visibleWhenSettingPath = visibleWhenSettingPath;
        this.visibleWhenValues = visibleWhenValues ?? Array.Empty<string>();
    }

    public static ProjectAssetImportSettingDefinition Section(ProjectAssetClass assetClass, string tab, string label)
    {
        return new ProjectAssetImportSettingDefinition(
            true,
            assetClass,
            tab,
            string.Empty,
            string.Empty,
            label,
            ProjectAssetPropertyValueKind.String,
            string.Empty,
            Array.Empty<string>());
    }

    public static ProjectAssetImportSettingDefinition Bool(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
    {
        return Value(assetClass, tab, settingPath, propertyPath, label, ProjectAssetPropertyValueKind.Bool, defaultValue, Array.Empty<string>());
    }

    public static ProjectAssetImportSettingDefinition Int(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
    {
        return Value(assetClass, tab, settingPath, propertyPath, label, ProjectAssetPropertyValueKind.Int, defaultValue, Array.Empty<string>());
    }

    public static ProjectAssetImportSettingDefinition Float(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
    {
        return Value(assetClass, tab, settingPath, propertyPath, label, ProjectAssetPropertyValueKind.Float, defaultValue, Array.Empty<string>());
    }

    public static ProjectAssetImportSettingDefinition String(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
    {
        return Value(assetClass, tab, settingPath, propertyPath, label, ProjectAssetPropertyValueKind.String, defaultValue, Array.Empty<string>());
    }

    public static ProjectAssetImportSettingDefinition Enum(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue, params string[] options)
    {
        return Value(assetClass, tab, settingPath, propertyPath, label, ProjectAssetPropertyValueKind.Enum, defaultValue, options);
    }

    public static ProjectAssetImportSettingDefinition Enum<TEnum>(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
        where TEnum : struct
    {
        return Enum(assetClass, tab, settingPath, propertyPath, label, defaultValue, System.Enum.GetNames(typeof(TEnum)));
    }

    private static ProjectAssetImportSettingDefinition Value(
        ProjectAssetClass assetClass,
        string tab,
        string settingPath,
        string propertyPath,
        string label,
        ProjectAssetPropertyValueKind valueKind,
        string defaultValue,
        string[] options)
    {
        return new ProjectAssetImportSettingDefinition(
            false,
            assetClass,
            tab,
            settingPath,
            propertyPath,
            label,
            valueKind,
            defaultValue,
            options);
    }

    public ProjectAssetImportSettingDefinition VisibleWhen(string dependencySettingPath, params string[] acceptedValues)
    {
        return new ProjectAssetImportSettingDefinition(
            isSection,
            assetClass,
            tab,
            settingPath,
            propertyPath,
            label,
            valueKind,
            defaultValue,
            options,
            dependencySettingPath,
            acceptedValues);
    }
}

public static class ProjectAssetImportSettingCatalog
{
    public static readonly string[] ModelTabs = { "Model", "Rig", "Animation", "Materials", "Custom" };
    public static readonly string[] TextureTabs = { "Texture", "Advanced", "Sprite", "Swizzle", "Custom" };
    public static readonly string[] AllTabs = { "Model", "Rig", "Animation", "Materials", "Texture", "Advanced", "Sprite", "Swizzle", "Custom" };

    private static readonly ProjectAssetImportSettingDefinition[] Definitions =
    {
        Section(ProjectAssetClass.Model, "Model", "场景"),
        Float(ProjectAssetClass.Model, "Model", "modelImportSettings.model.globalScale", "ModelImporter.globalScale", "缩放系数", "1"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.useFileScale", "ModelImporter.useFileScale", "使用文件缩放", "true"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.useFileUnits", "ModelImporter.useFileUnits", "转换单位", "true"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.bakeAxisConversion", "ModelImporter.bakeAxisConversion", "烘焙轴转换", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.importBlendShapes", "ModelImporter.importBlendShapes", "导入 BlendShapes", "false"),
        Enum<ModelImporterNormals>(ProjectAssetClass.Model, "Model", "modelImportSettings.model.importBlendShapeNormals", "ModelImporter.importBlendShapeNormals", "BlendShape 法线", "None")
            .VisibleWhen("modelImportSettings.model.importBlendShapes", "true"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.importBlendShapeDeformPercent", "ModelImporter.importBlendShapeDeformPercent", "BlendShape Deform Percent", "false")
            .VisibleWhen("modelImportSettings.model.importBlendShapes", "true"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.importVisibility", "ModelImporter.importVisibility", "导入可见性", "true"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.importCameras", "ModelImporter.importCameras", "导入相机", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.importLights", "ModelImporter.importLights", "导入灯光", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.preserveHierarchy", "ModelImporter.preserveHierarchy", "保持层次结构", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.model.sortHierarchyByName", "ModelImporter.sortHierarchyByName", "按名称对层级排序", "true"),
        String(ProjectAssetClass.Model, "Model", "modelImportSettings.model.extraUserProperties", "ModelImporter.extraUserProperties", "额外用户属性", string.Empty),
        Section(ProjectAssetClass.Model, "Model", "网格"),
        Enum<ModelImporterMeshCompression>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.meshCompression", "ModelImporter.meshCompression", "网格压缩", "Low"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.isReadable", "ModelImporter.isReadable", "读取/写入", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.optimizeMesh", "ModelImporter.optimizeMesh", "优化网格", "true"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.optimizeMeshPolygons", "ModelImporter.optimizeMeshPolygons", "优化多边形顺序", "true"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.optimizeMeshVertices", "ModelImporter.optimizeMeshVertices", "优化顶点顺序", "true"),
        Enum<MeshOptimizationFlags>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.meshOptimizationFlags", "ModelImporter.meshOptimizationFlags", "优化网格范围", "Everything"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.addCollider", "ModelImporter.addCollider", "生成碰撞器", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.keepQuads", "ModelImporter.keepQuads", "保留四边形", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.weldVertices", "ModelImporter.weldVertices", "焊接顶点", "true"),
        Enum<ModelImporterIndexFormat>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.indexFormat", "ModelImporter.indexFormat", "索引格式", "Auto"),
        Enum<ModelImporterNormals>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.importNormals", "ModelImporter.importNormals", "法线", "Import"),
        Enum<ModelImporterTangentSpaceMode>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.normalImportMode", "ModelImporter.normalImportMode", "法线模式", "Import"),
        Enum<ModelImporterNormalCalculationMode>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.normalCalculationMode", "ModelImporter.normalCalculationMode", "法线计算模式", "AreaAndAngleWeighted"),
        Enum<ModelImporterNormalSmoothingSource>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.normalSmoothingSource", "ModelImporter.normalSmoothingSource", "平滑度源", "PreferSmoothingGroups"),
        Float(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.normalSmoothingAngle", "ModelImporter.normalSmoothingAngle", "平滑角度", "60"),
        Enum<ModelImporterTangents>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.importTangents", "ModelImporter.importTangents", "切线", "CalculateMikk"),
        Enum<ModelImporterTangentSpaceMode>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.tangentImportMode", "ModelImporter.tangentImportMode", "切线模式", "Calculate"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.splitTangentsAcrossSeams", "ModelImporter.splitTangentsAcrossSeams", "跨接缝拆分切线", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.swapUVChannels", "ModelImporter.swapUVChannels", "交换 UVs", "false"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.strictVertexDataChecks", "ModelImporter.strictVertexDataChecks", "严格顶点数据检查", "false"),
        Section(ProjectAssetClass.Model, "Model", "光照贴图 UV 设置"),
        Bool(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.generateSecondaryUV", "ModelImporter.generateSecondaryUV", "生成光照贴图 UVs", "false"),
        Enum<ModelImporterSecondaryUVMarginMethod>(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.secondaryUVMarginMethod", "ModelImporter.secondaryUVMarginMethod", "边距方法", "Calculate")
            .VisibleWhen("modelImportSettings.meshes.generateSecondaryUV", "true"),
        Float(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.secondaryUVHardAngle", "ModelImporter.secondaryUVHardAngle", "硬角度", "88")
            .VisibleWhen("modelImportSettings.meshes.generateSecondaryUV", "true"),
        Float(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.secondaryUVPackMargin", "ModelImporter.secondaryUVPackMargin", "打包边距", "4")
            .VisibleWhen("modelImportSettings.meshes.generateSecondaryUV", "true"),
        Float(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.secondaryUVAngleDistortion", "ModelImporter.secondaryUVAngleDistortion", "角度误差", "8")
            .VisibleWhen("modelImportSettings.meshes.generateSecondaryUV", "true"),
        Float(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.secondaryUVAreaDistortion", "ModelImporter.secondaryUVAreaDistortion", "面积误差", "15")
            .VisibleWhen("modelImportSettings.meshes.generateSecondaryUV", "true"),
        Float(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.secondaryUVMinLightmapResolution", "ModelImporter.secondaryUVMinLightmapResolution", "最小光照贴图分辨率", "40")
            .VisibleWhen("modelImportSettings.meshes.generateSecondaryUV", "true"),
        Float(ProjectAssetClass.Model, "Model", "modelImportSettings.meshes.secondaryUVMinObjectScale", "ModelImporter.secondaryUVMinObjectScale", "最小物体缩放", "1")
            .VisibleWhen("modelImportSettings.meshes.generateSecondaryUV", "true"),

        Enum<ModelImporterAnimationType>(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.animationType", "ModelImporter.animationType", "动画类型", "None"),
        Enum<ModelImporterAvatarSetup>(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.avatarSetup", "ModelImporter.avatarSetup", "Avatar 定义", "CreateFromThisModel")
            .VisibleWhen("modelImportSettings.rig.animationType", "Generic", "Human"),
        Enum<ModelImporterSkinWeights>(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.skinWeights", "ModelImporter.skinWeights", "蒙皮权重", "Standard")
            .VisibleWhen("modelImportSettings.rig.animationType", "Generic", "Human"),
        Int(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.maxBonesPerVertex", "ModelImporter.maxBonesPerVertex", "每顶点最大骨骼数", "4")
            .VisibleWhen("modelImportSettings.rig.animationType", "Generic", "Human"),
        Float(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.minBoneWeight", "ModelImporter.minBoneWeight", "最小骨骼权重", "0.001")
            .VisibleWhen("modelImportSettings.rig.animationType", "Generic", "Human"),
        Bool(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.optimizeGameObjects", "ModelImporter.optimizeGameObjects", "优化游戏对象", "false")
            .VisibleWhen("modelImportSettings.rig.animationType", "Human"),
        Bool(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.optimizeBones", "ModelImporter.optimizeBones", "优化骨骼", "true")
            .VisibleWhen("modelImportSettings.rig.animationType", "Generic", "Human"),
        Bool(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.autoGenerateAvatarMappingIfUnspecified", "ModelImporter.autoGenerateAvatarMappingIfUnspecified", "自动生成 Avatar 映射", "true")
            .VisibleWhen("modelImportSettings.rig.animationType", "Human"),
        Enum<ModelImporterHumanoidOversampling>(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.humanoidOversampling", "ModelImporter.humanoidOversampling", "人形采样", "X1")
            .VisibleWhen("modelImportSettings.rig.animationType", "Human"),
        Bool(ProjectAssetClass.Model, "Rig", "modelImportSettings.rig.bakeIK", "ModelImporter.bakeIK", "烘焙 IK", "false")
            .VisibleWhen("modelImportSettings.rig.animationType", "Human"),

        Bool(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.importAnimation", "ModelImporter.importAnimation", "导入动画", "false"),
        Enum<ModelImporterAnimationCompression>(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.animationCompression", "ModelImporter.animationCompression", "动画压缩", "Optimal")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Float(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.animationRotationError", "ModelImporter.animationRotationError", "旋转误差", "0.5")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Float(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.animationPositionError", "ModelImporter.animationPositionError", "位置误差", "0.5")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Float(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.animationScaleError", "ModelImporter.animationScaleError", "缩放误差", "0.5")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Enum<WrapMode>(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.animationWrapMode", "ModelImporter.animationWrapMode", "循环模式", "Default")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Enum<ModelImporterGenerateAnimations>(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.generateAnimations", "ModelImporter.generateAnimations", "生成动画", "None")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Bool(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.importConstraints", "ModelImporter.importConstraints", "导入约束", "false")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Bool(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.importAnimatedCustomProperties", "ModelImporter.importAnimatedCustomProperties", "导入动画自定义属性", "false")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Bool(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.resampleCurves", "ModelImporter.resampleCurves", "重采样曲线", "true")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Bool(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.resampleRotations", "ModelImporter.resampleRotations", "重采样旋转", "true")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Bool(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.removeConstantScaleCurves", "ModelImporter.removeConstantScaleCurves", "移除常量缩放曲线", "true")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Bool(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.splitAnimations", "ModelImporter.splitAnimations", "分割动画", "false")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        String(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.motionNodeName", "ModelImporter.motionNodeName", "Motion Node", string.Empty)
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),
        Bool(ProjectAssetClass.Model, "Animation", "modelImportSettings.animation.clipNameFromAsset", "ModelImporter.clipNameFromAsset", "Clip Name From Asset", "false")
            .VisibleWhen("modelImportSettings.animation.importAnimation", "true"),

        Enum<ModelImporterMaterialImportMode>(ProjectAssetClass.Model, "Materials", "modelImportSettings.materials.materialImportMode", "ModelImporter.materialImportMode", "材质创建模式", "ImportStandard"),
        Enum<ModelImporterMaterialLocation>(ProjectAssetClass.Model, "Materials", "modelImportSettings.materials.materialLocation", "ModelImporter.materialLocation", "位置", "InPrefab")
            .VisibleWhen("modelImportSettings.materials.materialImportMode", "ImportStandard", "ImportViaMaterialDescription"),
        Enum<ModelImporterMaterialName>(ProjectAssetClass.Model, "Materials", "modelImportSettings.materials.materialName", "ModelImporter.materialName", "命名", "BasedOnMaterialName")
            .VisibleWhen("modelImportSettings.materials.materialImportMode", "ImportStandard", "ImportViaMaterialDescription"),
        Enum<ModelImporterMaterialSearch>(ProjectAssetClass.Model, "Materials", "modelImportSettings.materials.materialSearch", "ModelImporter.materialSearch", "搜索", "Local")
            .VisibleWhen("modelImportSettings.materials.materialImportMode", "ImportStandard", "ImportViaMaterialDescription"),
        Bool(ProjectAssetClass.Model, "Materials", "modelImportSettings.materials.useSRGBMaterialColor", "ModelImporter.useSRGBMaterialColor", "使用 sRGB 材质颜色", "true")
            .VisibleWhen("modelImportSettings.materials.materialImportMode", "ImportStandard", "ImportViaMaterialDescription"),

        Section(ProjectAssetClass.Texture2D, "Texture", "纹理"),
        Enum<TextureImporterType>(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.textureType", "TextureImporter.textureType", "纹理类型", "Default"),
        Enum<TextureImporterShape>(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.textureShape", "TextureImporter.textureShape", "纹理形状", "Texture2D"),
        Bool(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.sRGBTexture", "TextureImporter.sRGBTexture", "sRGB 颜色纹理", "true"),
        Enum<TextureImporterAlphaSource>(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.alphaSource", "TextureImporter.alphaSource", "Alpha 源", "FromInput"),
        Bool(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.alphaIsTransparency", "TextureImporter.alphaIsTransparency", "Alpha 是透明度", "false")
            .VisibleWhen("textureImportSettings.texture.alphaSource", "FromInput"),
        Bool(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.ignorePngGamma", "TextureImporter.ignorePngGamma", "忽略 PNG Gamma", "false"),
        Bool(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.isReadable", "TextureImporter.isReadable", "读取/写入", "false"),
        Bool(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.vtOnly", "TextureImporter.vtOnly", "仅虚拟纹理", "false"),
        Section(ProjectAssetClass.Texture2D, "Texture", "压缩"),
        Int(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.maxTextureSize", "TextureImporter.maxTextureSize", "最大尺寸", "2048"),
        Enum<TextureImporterCompression>(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.textureCompression", "TextureImporter.textureCompression", "压缩", "Compressed"),
        Int(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.compressionQuality", "TextureImporter.compressionQuality", "压缩质量", "50"),
        Bool(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.crunchedCompression", "TextureImporter.crunchedCompression", "使用 Crunch 压缩", "false"),
        Enum<TextureImporterFormat>(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.textureFormat", "TextureImporter.textureFormat", "格式", "Automatic"),
        Enum<AndroidETC2FallbackOverride>(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.androidETC2FallbackOverride", "TextureImporter.androidETC2FallbackOverride", "ETC2 回退", "UseBuildSettings"),
        Bool(ProjectAssetClass.Texture2D, "Texture", "textureImportSettings.texture.allowAlphaSplitting", "TextureImporter.allowAlphaSplitting", "允许 Alpha 分离", "false"),

        Section(ProjectAssetClass.Texture2D, "Advanced", "Mip Maps"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.mipmapEnabled", "TextureImporter.mipmapEnabled", "生成 Mip Maps", "true"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.borderMipmap", "TextureImporter.borderMipmap", "Border Mip Maps", "false")
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        Enum<TextureImporterMipFilter>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.mipmapFilter", "TextureImporter.mipmapFilter", "Mip Map 过滤", "BoxFilter")
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        Float(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.mipMapBias", "TextureImporter.mipMapBias", "Mip Map Bias", "0")
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.mipMapsPreserveCoverage", "TextureImporter.mipMapsPreserveCoverage", "保持覆盖率", "false")
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        Float(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.alphaTestReferenceValue", "TextureImporter.alphaTestReferenceValue", "Alpha Cutoff", "0.5")
            .VisibleWhen("textureImportSettings.advanced.mipMapsPreserveCoverage", "true"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.fadeout", "TextureImporter.fadeout", "Fadeout Mip Maps", "false")
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        Int(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.mipmapFadeDistanceStart", "TextureImporter.mipmapFadeDistanceStart", "Fade Range Start", "1")
            .VisibleWhen("textureImportSettings.advanced.fadeout", "true"),
        Int(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.mipmapFadeDistanceEnd", "TextureImporter.mipmapFadeDistanceEnd", "Fade Range End", "3")
            .VisibleWhen("textureImportSettings.advanced.fadeout", "true"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.generateMipsInLinearSpace", "TextureImporter.generateMipsInLinearSpace", "在线性空间生成 Mips", "false")
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        String(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.mipmapLimitGroupName", "TextureImporter.mipmapLimitGroupName", "Mipmap Limit Group", string.Empty)
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.ignoreMipmapLimit", "TextureImporter.ignoreMipmapLimit", "忽略 Mipmap Limit", "false")
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.streamingMipmaps", "TextureImporter.streamingMipmaps", "Streaming Mip Maps", "false")
            .VisibleWhen("textureImportSettings.advanced.mipmapEnabled", "true"),
        Int(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.streamingMipmapsPriority", "TextureImporter.streamingMipmapsPriority", "Streaming Priority", "0")
            .VisibleWhen("textureImportSettings.advanced.streamingMipmaps", "true"),
        Section(ProjectAssetClass.Texture2D, "Advanced", "法线贴图"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.convertToNormalmap", "TextureImporter.convertToNormalmap", "从灰度创建", "false"),
        Float(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.heightmapScale", "TextureImporter.heightmapScale", "凹凸强度", "0.25")
            .VisibleWhen("textureImportSettings.advanced.convertToNormalmap", "true"),
        Enum<TextureImporterNormalFilter>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.normalmapFilter", "TextureImporter.normalmapFilter", "过滤", "Standard")
            .VisibleWhen("textureImportSettings.advanced.convertToNormalmap", "true"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.flipGreenChannel", "TextureImporter.flipGreenChannel", "翻转绿色通道", "false"),
        Section(ProjectAssetClass.Texture2D, "Advanced", "平铺和过滤"),
        Enum<TextureWrapMode>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.wrapMode", "TextureImporter.wrapMode", "平铺模式", "Repeat"),
        Enum<TextureWrapMode>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.wrapModeU", "TextureImporter.wrapModeU", "U 轴平铺", "Repeat"),
        Enum<TextureWrapMode>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.wrapModeV", "TextureImporter.wrapModeV", "V 轴平铺", "Repeat"),
        Enum<TextureWrapMode>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.wrapModeW", "TextureImporter.wrapModeW", "W 轴平铺", "Repeat"),
        Enum<FilterMode>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.filterMode", "TextureImporter.filterMode", "过滤模式", "Bilinear"),
        Int(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.anisoLevel", "TextureImporter.anisoLevel", "各向异性等级", "1"),
        Enum<TextureImporterNPOTScale>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.npotScale", "TextureImporter.npotScale", "非 2 次幂", "ToNearest"),
        Section(ProjectAssetClass.Texture2D, "Advanced", "形状"),
        Enum<TextureImporterGenerateCubemap>(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.generateCubemap", "TextureImporter.generateCubemap", "映射", "AutoCubemap"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.correctGamma", "TextureImporter.correctGamma", "校正 Gamma", "false"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.grayscaleToAlpha", "TextureImporter.grayscaleToAlpha", "灰度转 Alpha", "false"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.lightmap", "TextureImporter.lightmap", "Lightmap", "false"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.linearTexture", "TextureImporter.linearTexture", "线性纹理", "false"),
        Bool(ProjectAssetClass.Texture2D, "Advanced", "textureImportSettings.advanced.normalmap", "TextureImporter.normalmap", "Normal Map", "false"),

        Enum<SpriteImportMode>(ProjectAssetClass.Texture2D, "Sprite", "textureImportSettings.sprite.spriteImportMode", "TextureImporter.spriteImportMode", "Sprite 模式", "None")
            .VisibleWhen("textureImportSettings.texture.textureType", "Sprite"),
        Float(ProjectAssetClass.Texture2D, "Sprite", "textureImportSettings.sprite.spritePixelsPerUnit", "TextureImporter.spritePixelsPerUnit", "每单位像素", "100")
            .VisibleWhen("textureImportSettings.sprite.spriteImportMode", "Single", "Multiple"),
        Float(ProjectAssetClass.Texture2D, "Sprite", "textureImportSettings.sprite.spritePixelsToUnits", "TextureImporter.spritePixelsToUnits", "Pixels To Units", "100")
            .VisibleWhen("textureImportSettings.sprite.spriteImportMode", "Single", "Multiple"),
        String(ProjectAssetClass.Texture2D, "Sprite", "textureImportSettings.sprite.spritePackingTag", "TextureImporter.spritePackingTag", "Packing Tag", string.Empty)
            .VisibleWhen("textureImportSettings.sprite.spriteImportMode", "Single", "Multiple"),

        Enum<TextureImporterSwizzle>(ProjectAssetClass.Texture2D, "Swizzle", "textureImportSettings.swizzle.swizzleR", "TextureImporter.swizzleR", "R 通道", "R"),
        Enum<TextureImporterSwizzle>(ProjectAssetClass.Texture2D, "Swizzle", "textureImportSettings.swizzle.swizzleG", "TextureImporter.swizzleG", "G 通道", "G"),
        Enum<TextureImporterSwizzle>(ProjectAssetClass.Texture2D, "Swizzle", "textureImportSettings.swizzle.swizzleB", "TextureImporter.swizzleB", "B 通道", "B"),
        Enum<TextureImporterSwizzle>(ProjectAssetClass.Texture2D, "Swizzle", "textureImportSettings.swizzle.swizzleA", "TextureImporter.swizzleA", "A 通道", "A")
    };

    public static string[] GetTabs(ProjectAssetClass assetClass)
    {
        return assetClass switch
        {
            ProjectAssetClass.Model => ModelTabs,
            ProjectAssetClass.Texture2D => TextureTabs,
            _ => AllTabs
        };
    }

    public static ProjectAssetImportSettingDefinition[] GetDefinitions(ProjectAssetClass assetClass, string tab)
    {
        List<ProjectAssetImportSettingDefinition> result = new List<ProjectAssetImportSettingDefinition>();
        foreach (ProjectAssetImportSettingDefinition definition in Definitions)
        {
            if (!AssetClassMatches(assetClass, definition.assetClass))
                continue;
            if (!string.Equals(definition.tab, tab, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(definition);
        }

        return result.ToArray();
    }

    public static ProjectAssetImportSettingDefinition[] GetDefinitionsForAssetClass(ProjectAssetClass assetClass)
    {
        List<ProjectAssetImportSettingDefinition> result = new List<ProjectAssetImportSettingDefinition>();
        foreach (ProjectAssetImportSettingDefinition definition in Definitions)
        {
            if (definition.isSection || !AssetClassMatches(assetClass, definition.assetClass))
                continue;

            result.Add(definition);
        }

        return result.ToArray();
    }

    public static ProjectAssetImportSettingDefinition FindByPropertyPath(string propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
            return null;

        foreach (ProjectAssetImportSettingDefinition definition in Definitions)
        {
            if (!definition.isSection && string.Equals(definition.propertyPath, propertyPath, StringComparison.OrdinalIgnoreCase))
                return definition;
        }

        return null;
    }

    public static ProjectAssetImportSettingDefinition FindBySettingPath(string settingPath)
    {
        if (string.IsNullOrWhiteSpace(settingPath))
            return null;

        foreach (ProjectAssetImportSettingDefinition definition in Definitions)
        {
            if (!definition.isSection && string.Equals(definition.settingPath, settingPath, StringComparison.OrdinalIgnoreCase))
                return definition;
        }

        return null;
    }

    private static bool AssetClassMatches(ProjectAssetClass requested, ProjectAssetClass definitionClass)
    {
        return requested == ProjectAssetClass.Any || requested == definitionClass;
    }

    private static ProjectAssetImportSettingDefinition Section(ProjectAssetClass assetClass, string tab, string label)
    {
        return ProjectAssetImportSettingDefinition.Section(assetClass, tab, label);
    }

    private static ProjectAssetImportSettingDefinition Bool(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
    {
        return ProjectAssetImportSettingDefinition.Bool(assetClass, tab, settingPath, propertyPath, label, defaultValue);
    }

    private static ProjectAssetImportSettingDefinition Int(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
    {
        return ProjectAssetImportSettingDefinition.Int(assetClass, tab, settingPath, propertyPath, label, defaultValue);
    }

    private static ProjectAssetImportSettingDefinition Float(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
    {
        return ProjectAssetImportSettingDefinition.Float(assetClass, tab, settingPath, propertyPath, label, defaultValue);
    }

    private static ProjectAssetImportSettingDefinition String(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
    {
        return ProjectAssetImportSettingDefinition.String(assetClass, tab, settingPath, propertyPath, label, defaultValue);
    }

    private static ProjectAssetImportSettingDefinition Enum<TEnum>(ProjectAssetClass assetClass, string tab, string settingPath, string propertyPath, string label, string defaultValue)
        where TEnum : struct
    {
        return ProjectAssetImportSettingDefinition.Enum<TEnum>(assetClass, tab, settingPath, propertyPath, label, defaultValue);
    }
}

public sealed class ProjectEffectiveImportSetting
{
    public string propertyPath;
    public ProjectAssetPropertyValueKind valueKind;
    public string value;
    public string sourceRuleName;

    public ProjectAssetPropertyItem ToPropertyItem()
    {
        return new ProjectAssetPropertyItem
        {
            propertyPath = propertyPath,
            valueKind = valueKind,
            value = value
        };
    }
}

public sealed class ProjectEffectiveImportSettings
{
    public ProjectAssetImportRule[] matchedRules = Array.Empty<ProjectAssetImportRule>();
    public ProjectEffectiveImportSetting[] settings = Array.Empty<ProjectEffectiveImportSetting>();

    public ProjectAssetPropertyItem[] ToPropertyItems()
    {
        ProjectAssetPropertyItem[] items = new ProjectAssetPropertyItem[settings?.Length ?? 0];
        for (int i = 0; i < items.Length; i++)
            items[i] = settings[i]?.ToPropertyItem();

        return items;
    }
}

[Serializable]
public sealed class ProjectAssetImportRule
{
    public string name;
    public bool enabled = true;
    public ProjectAssetRuleFilter filter = new ProjectAssetRuleFilter();
    public ProjectModelImportSettings modelImportSettings = new ProjectModelImportSettings();
    public ProjectTextureImportSettings textureImportSettings = new ProjectTextureImportSettings();
    public ProjectCustomImportSettings customImportSettings = new ProjectCustomImportSettings();
    [HideInInspector]
    public ProjectAssetPropertyItem[] propertyItems = Array.Empty<ProjectAssetPropertyItem>();
    [HideInInspector]
    public bool legacyPropertyItemsMigrated;

    public bool Matches(ProjectAssetRuleContext context)
    {
        return enabled && filter != null && filter.Matches(context);
    }

    public void EnsureMigrated()
    {
        EnsureInitialized();
        if (legacyPropertyItemsMigrated)
            return;

        MigrateLegacyPropertyItems(false);
    }

    public void MigrateLegacyPropertyItems(bool clearLegacyItems)
    {
        EnsureInitialized();
        ProjectAssetPropertyItem[] legacyItems = propertyItems ?? Array.Empty<ProjectAssetPropertyItem>();
        foreach (ProjectAssetPropertyItem item in legacyItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.propertyPath))
                continue;

            ProjectAssetImportSettingDefinition definition = ProjectAssetImportSettingCatalog.FindByPropertyPath(item.propertyPath);
            ProjectAssetImportSetting setting = definition != null ? FindSetting(definition.settingPath) : null;
            if (setting != null)
                setting.Set(definition.valueKind, item.value);
            else
                customImportSettings.AddOrReplace(item);
        }

        legacyPropertyItemsMigrated = true;
        if (clearLegacyItems)
            propertyItems = Array.Empty<ProjectAssetPropertyItem>();
    }

    public ProjectAssetImportSetting FindSetting(string settingPath)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(settingPath))
            return null;

        object current = this;
        foreach (string segment in settingPath.Split('.'))
        {
            if (current == null)
                return null;

            FieldInfo field = current.GetType().GetField(segment, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
                return null;

            object value = field.GetValue(current);
            if (value == null)
            {
                value = Activator.CreateInstance(field.FieldType);
                field.SetValue(current, value);
            }

            current = value;
        }

        return current as ProjectAssetImportSetting;
    }

    public bool ShouldIncludeSetting(ProjectAssetImportSettingDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.visibleWhenSettingPath))
            return true;

        string currentValue = ResolveSettingValue(definition.visibleWhenSettingPath);
        foreach (string acceptedValue in definition.visibleWhenValues)
        {
            if (string.Equals(currentValue, acceptedValue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public string ResolveSettingValue(string settingPath)
    {
        ProjectAssetImportSetting dependency = FindSetting(settingPath);
        ProjectAssetImportSettingDefinition dependencyDefinition = ProjectAssetImportSettingCatalog.FindBySettingPath(settingPath);
        string value = dependency != null && dependency.overrideEnabled
            ? dependency.value
            : dependencyDefinition?.defaultValue;

        return string.IsNullOrEmpty(value) ? dependencyDefinition?.defaultValue ?? string.Empty : value;
    }

    private void EnsureInitialized()
    {
        filter ??= new ProjectAssetRuleFilter();
        modelImportSettings ??= new ProjectModelImportSettings();
        textureImportSettings ??= new ProjectTextureImportSettings();
        customImportSettings ??= new ProjectCustomImportSettings();
        EnsureSettingObjects(modelImportSettings);
        EnsureSettingObjects(textureImportSettings);
        customImportSettings.propertyItems ??= Array.Empty<ProjectAssetPropertyItem>();
        propertyItems ??= Array.Empty<ProjectAssetPropertyItem>();
    }

    private static void EnsureSettingObjects(object target)
    {
        if (target == null)
            return;

        foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.FieldType.IsArray || field.FieldType == typeof(string))
                continue;

            object value = field.GetValue(target);
            if (value == null)
            {
                value = Activator.CreateInstance(field.FieldType);
                field.SetValue(target, value);
            }

            if (field.FieldType != typeof(ProjectAssetImportSetting) && field.FieldType.IsClass)
                EnsureSettingObjects(value);
        }
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

    [HideInInspector]
    public bool applyAllMatchingRules = true;
    public ProjectAssetImportRule[] rules = Array.Empty<ProjectAssetImportRule>();

    public static ProjectAssetImportRuleSet LoadDefault()
    {
        ProjectAssetImportRuleSet ruleSet = AssetDatabase.LoadAssetAtPath<ProjectAssetImportRuleSet>(DefaultAssetPath);
        if (ruleSet == null)
            return CreateDefaultInstance();

        ruleSet.EnsureMigrated();
        return ruleSet;
    }

    public static ProjectAssetImportRuleSet CreateDefaultInstance()
    {
        ProjectAssetImportRuleSet ruleSet = CreateInstance<ProjectAssetImportRuleSet>();
        ruleSet.applyAllMatchingRules = true;
        ruleSet.rules = CreateDefaultRules();
        ruleSet.EnsureMigrated();
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
        }

        return matches.ToArray();
    }

    public ProjectEffectiveImportSettings BuildEffectiveImportSettings(ProjectAssetRuleContext context)
    {
        ProjectAssetImportRule[] matchedRules = ResolveRules(context);
        List<ProjectEffectiveImportSetting> effectiveSettings = new List<ProjectEffectiveImportSetting>();
        Dictionary<string, int> indexByPropertyPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (ProjectAssetImportRule rule in matchedRules)
        {
            if (rule == null)
                continue;

            rule.EnsureMigrated();
            MergeStrongSettings(rule, context, effectiveSettings, indexByPropertyPath);
            MergeCustomSettings(rule, effectiveSettings, indexByPropertyPath);
        }

        return new ProjectEffectiveImportSettings
        {
            matchedRules = matchedRules,
            settings = effectiveSettings.ToArray()
        };
    }

    public ProjectEffectiveImportSettings BuildRuleImportSettings(ProjectAssetImportRule rule, ProjectAssetRuleContext context)
    {
        List<ProjectEffectiveImportSetting> effectiveSettings = new List<ProjectEffectiveImportSetting>();
        Dictionary<string, int> indexByPropertyPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (rule != null)
        {
            rule.EnsureMigrated();
            MergeStrongSettings(rule, context, effectiveSettings, indexByPropertyPath);
            MergeCustomSettings(rule, effectiveSettings, indexByPropertyPath);
        }

        return new ProjectEffectiveImportSettings
        {
            matchedRules = rule != null ? new[] { rule } : Array.Empty<ProjectAssetImportRule>(),
            settings = effectiveSettings.ToArray()
        };
    }

    public void Apply(AssetImporter importer, string assetPath)
    {
        if (importer == null)
            return;

        ProjectAssetRuleContext context = ProjectAssetRuleContext.FromImporter(assetPath, importer);
        ProjectEffectiveImportSettings settings = BuildEffectiveImportSettings(context);
        ProjectAssetPropertyApplier.Apply(importer, assetPath, settings.ToPropertyItems());
    }

    public void EnsureMigrated()
    {
        if (rules == null)
        {
            rules = Array.Empty<ProjectAssetImportRule>();
            return;
        }

        foreach (ProjectAssetImportRule rule in rules)
            rule?.EnsureMigrated();
    }

    private void OnValidate()
    {
        EnsureMigrated();
    }

    private static void MergeStrongSettings(
        ProjectAssetImportRule rule,
        ProjectAssetRuleContext context,
        List<ProjectEffectiveImportSetting> effectiveSettings,
        Dictionary<string, int> indexByPropertyPath)
    {
        ProjectAssetClass assetClass = context?.assetClass ?? ProjectAssetClass.Any;
        foreach (ProjectAssetImportSettingDefinition definition in ProjectAssetImportSettingCatalog.GetDefinitionsForAssetClass(assetClass))
        {
            if (!rule.ShouldIncludeSetting(definition))
                continue;

            ProjectAssetImportSetting setting = rule.FindSetting(definition.settingPath);
            if (setting == null || !setting.overrideEnabled)
                continue;

            UpsertEffectiveSetting(
                effectiveSettings,
                indexByPropertyPath,
                definition.propertyPath,
                definition.valueKind,
                string.IsNullOrEmpty(setting.value) ? definition.defaultValue : setting.value,
                rule.name);
        }
    }

    private static void MergeCustomSettings(
        ProjectAssetImportRule rule,
        List<ProjectEffectiveImportSetting> effectiveSettings,
        Dictionary<string, int> indexByPropertyPath)
    {
        ProjectAssetPropertyItem[] customItems = rule.customImportSettings?.propertyItems ?? Array.Empty<ProjectAssetPropertyItem>();
        foreach (ProjectAssetPropertyItem item in customItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.propertyPath))
                continue;

            UpsertEffectiveSetting(
                effectiveSettings,
                indexByPropertyPath,
                item.propertyPath,
                item.valueKind,
                item.value,
                rule.name);
        }
    }

    private static void UpsertEffectiveSetting(
        List<ProjectEffectiveImportSetting> effectiveSettings,
        Dictionary<string, int> indexByPropertyPath,
        string propertyPath,
        ProjectAssetPropertyValueKind valueKind,
        string value,
        string sourceRuleName)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
            return;

        ProjectEffectiveImportSetting setting = new ProjectEffectiveImportSetting
        {
            propertyPath = propertyPath,
            valueKind = valueKind,
            value = value,
            sourceRuleName = sourceRuleName
        };

        if (indexByPropertyPath.TryGetValue(propertyPath, out int existingIndex))
        {
            effectiveSettings[existingIndex] = setting;
            return;
        }

        indexByPropertyPath[propertyPath] = effectiveSettings.Count;
        effectiveSettings.Add(setting);
    }

    private static ProjectAssetImportRule Rule(
        string name,
        ProjectAssetRuleFilter filter,
        ProjectAssetPropertyItem[] propertyItems)
    {
        ProjectAssetImportRule rule = new ProjectAssetImportRule
        {
            name = name,
            enabled = true,
            filter = filter,
            propertyItems = propertyItems
        };
        rule.MigrateLegacyPropertyItems(true);
        return rule;
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
