using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ProjectFbxAutoImporterTests
{
    [Test]
    public void SidecarPath_AppendsJsonToFbxAssetPath()
    {
        Assert.That(
            GetSidecarAssetPath("Assets/Game/Props/Generic/Art/zzz.fbx"),
            Is.EqualTo("Assets/Game/Props/Generic/Art/zzz.fbx.json"));
    }

    [Test]
    public void TryLoad_MissingSidecarReturnsAssemblyMissingState()
    {
        bool result = TryLoad(
            "Assets/Game/Props/Generic/Art/Missing.fbx",
            out object manifest,
            out string error);

        Assert.That(result, Is.False);
        Assert.That(manifest, Is.Null);
        Assert.That(error, Does.Contain("Missing DCC FBX sidecar JSON"));
    }

    [Test]
    public void ImportRuleSet_DefaultRulesMatchProjectPaths()
    {
        AssertResolvedProperty(
            "Assets/Game/Characters/Hero/Art/SourceAnimations/Walk/Hero_Walk.fbx",
            "Model",
            "ModelImporter.animationType",
            "Human");
        AssertResolvedProperty(
            "Assets/Game/Characters/Hero/Art/SourceAnimations/Walk/Hero_Walk.fbx",
            "Model",
            "ModelImporter.importBlendShapes",
            "false");
        AssertResolvedProperty(
            "Assets/Game/Characters/Qianxia/Art/Meshs/Ch36_nonPBR@Walking.fbx",
            "Model",
            "ModelImporter.animationType",
            "Human");
        AssertResolvedProperty(
            "Assets/Game/Characters/Qianxia/Art/Meshs/Ch36_nonPBR@Walking.fbx",
            "Model",
            "ModelImporter.clipNameFromAsset",
            "true");
        AssertResolvedProperty(
            "Assets/Game/Characters/Hero/Art/HeroBody.fbx",
            "Model",
            "ModelImporter.importBlendShapes",
            "true");
        AssertResolvedProperty(
            "Assets/Game/Props/Generic/Art/Crates/SM_Crate.fbx",
            "Model",
            "ModelImporter.addCollider",
            "true");
        AssertResolvedProperty(
            "Assets/Game/Props/Generic/Art/Crates/SM_Crate.fbx",
            "Model",
            "ModelImporter.generateSecondaryUV",
            "true");
        AssertResolvedProperty(
            "Assets/Game/Worlds/Meadow/Art/Sources/Meshes/zzz.fbx",
            "Model",
            "ModelImporter.preserveHierarchy",
            "true");
        AssertResolvedProperty(
            "Assets/Game/Environment/Trees/Art/SM_Tree_LOD0.fbx",
            "Model",
            "ModelImporter.preserveHierarchy",
            "true");
        AssertResolvedProperty(
            "Assets/Game/Environment/Rocks/Art/SM_Rock.fbx",
            "Model",
            "ModelImporter.animationType",
            "None");
        AssertResolvedProperty(
            "Assets/Game/Shared/Textures/Art/Wood_N.png",
            "Texture2D",
            "TextureImporter.textureType",
            "NormalMap");
        AssertResolvedProperty(
            "Assets/Game/Shared/Textures/Art/Wood_N.png",
            "Texture2D",
            "TextureImporter.wrapModeU",
            "Clamp");
    }

    [Test]
    public void ImportRuleSet_StringMatchersSupportDirectoryAndPackageName()
    {
        Assert.That(StringMatches("Contains", "Characters/", "Assets/Game/Characters/Hero"), Is.True);
        Assert.That(StringMatches("EndsWith", "_N", "Wood_N"), Is.True);
        Assert.That(StringMatches("Regex", "_N$", "Wood_N"), Is.True);
        Assert.That(StringMatches("Glob", "*LOD*", "SM_Tree_LOD2"), Is.True);
        Assert.That(StringMatches("Contains", "Props/", "Assets/Game/Characters/Hero"), Is.False);
    }

    [Test]
    public void ImportRuleSet_DirectoryRulesOverridePackageNameRulesInBroaderScope()
    {
        object ruleSet = CreateRuleSetWithRules(
            CreateImportRule("Model Defaults", "Model", "ModelImporter.addCollider", "false"),
            CreateImportRule(
                "Character Directory",
                "Model",
                "ModelImporter.addCollider",
                "false",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/Characters/"),
            CreateImportRule(
                "Hero Name",
                "Model",
                "ModelImporter.addCollider",
                "true",
                packageNameMatch: "EndsWith",
                packageNamePattern: "_Hero"));

        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Characters/Main/Art/Player_Hero.fbx",
            "Model",
            "ModelImporter.addCollider",
            "false");
    }

    [Test]
    public void ImportRuleSet_MoreSpecificDirectoryOverridesBroaderDirectoryWithPackageName()
    {
        object ruleSet = CreateRuleSetWithRules(
            CreateImportRule(
                "Game Art SM Models",
                "Model",
                "ModelImporter.addCollider",
                "false",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/",
                packageNameMatch: "Contains",
                packageNamePattern: "SM"),
            CreateImportRule(
                "Meadow Meshes",
                "Model",
                "ModelImporter.addCollider",
                "true",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/Worlds/Meadow/Art/Sources/Meshes/"),
            CreateImportRule(
                "Meadow Props",
                "Model",
                "ModelImporter.addCollider",
                "false",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/Worlds/Meadow/Art/Sources/Meshes/Props/"));

        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Worlds/Meadow/Art/Sources/Meshes/SM_Tree.fbx",
            "Model",
            "ModelImporter.addCollider",
            "true");
        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Worlds/Meadow/Art/Sources/Meshes/Props/SM_Crate.fbx",
            "Model",
            "ModelImporter.addCollider",
            "false");
    }

    [Test]
    public void ImportRuleSet_PackageNameDirectoryRulesUseDirectorySpecificityBeforeListOrder()
    {
        object ruleSet = CreateRuleSetWithRules(
            CreateImportRule(
                "Character At-Sign",
                "Model",
                "ModelImporter.animationType",
                "Human",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/Characters/",
                packageNameMatch: "Contains",
                packageNamePattern: "@"),
            CreateImportRule(
                "Game Art At-Sign",
                "Model",
                "ModelImporter.animationType",
                "Generic",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/",
                packageNameMatch: "Contains",
                packageNamePattern: "@"),
            CreateImportRule(
                "Any At-Sign",
                "Model",
                "ModelImporter.animationType",
                "None",
                packageNameMatch: "Contains",
                packageNamePattern: "@"));

        object effectiveSettings = BuildEffectiveImportSettings(
            ruleSet,
            "Assets/Game/Characters/Qianxia/Art/Meshs/Ch36_nonPBR@Walking.fbx",
            "Model");

        Assert.That(GetField<bool>(effectiveSettings, "hasConflicts"), Is.False);
        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Characters/Qianxia/Art/Meshs/Ch36_nonPBR@Walking.fbx",
            "Model",
            "ModelImporter.animationType",
            "Human");
    }

    [Test]
    public void ImportRuleSet_PackageNameRulesStillRefineSameDirectoryScope()
    {
        object ruleSet = CreateRuleSetWithRules(
            CreateImportRule(
                "Meadow Mesh Defaults",
                "Model",
                "ModelImporter.addCollider",
                "false",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/Worlds/Meadow/Art/Sources/Meshes/"),
            CreateImportRule(
                "Meadow SM Meshes",
                "Model",
                "ModelImporter.addCollider",
                "true",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/Worlds/Meadow/Art/Sources/Meshes/",
                packageNameMatch: "Contains",
                packageNamePattern: "SM"));

        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Worlds/Meadow/Art/Sources/Meshes/SM_Tree.fbx",
            "Model",
            "ModelImporter.addCollider",
            "true");
    }

    [Test]
    public void ImportRuleSet_MoreSpecificDirectoryRulesOverrideBroaderDirectoryRules()
    {
        object ruleSet = CreateRuleSetWithRules(
            CreateImportRule(
                "Characters",
                "Model",
                "ModelImporter.addCollider",
                "false",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/Characters/"),
            CreateImportRule(
                "Hero Characters",
                "Model",
                "ModelImporter.addCollider",
                "true",
                directoryMatch: "Contains",
                directoryPattern: "Assets/Game/Characters/Hero/Art/"));

        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Characters/Hero/Art/Body.fbx",
            "Model",
            "ModelImporter.addCollider",
            "true");
    }

    [Test]
    public void ImportRuleSet_AssetClassDefaultsRemainWhenHigherPriorityRulesDoNotSetProperty()
    {
        object ruleSet = CreateRuleSetWithRules(
            CreateImportRule("Model Defaults", "Model", "ModelImporter.meshCompression", "Low"),
            CreateImportRule(
                "Hero Name",
                "Model",
                "ModelImporter.addCollider",
                "true",
                packageNameMatch: "EndsWith",
                packageNamePattern: "_Hero"));

        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Characters/Main/Art/Player_Hero.fbx",
            "Model",
            "ModelImporter.meshCompression",
            "Low");
        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Characters/Main/Art/Player_Hero.fbx",
            "Model",
            "ModelImporter.addCollider",
            "true");
    }

    [Test]
    public void ImportRuleSet_SamePrioritySameValueDoesNotConflict()
    {
        object ruleSet = CreateRuleSetWithRules(
            CreateImportRule(
                "Rock Contains",
                "Model",
                "ModelImporter.addCollider",
                "true",
                packageNameMatch: "Contains",
                packageNamePattern: "Rock"),
            CreateImportRule(
                "Rock EndsWith",
                "Model",
                "ModelImporter.addCollider",
                "true",
                packageNameMatch: "EndsWith",
                packageNamePattern: "Rock"));

        object effectiveSettings = BuildEffectiveImportSettings(
            ruleSet,
            "Assets/Game/Props/Generic/Art/BigRock.fbx",
            "Model");

        Assert.That(GetField<bool>(effectiveSettings, "hasConflicts"), Is.False);
        AssertResolvedProperty(
            ruleSet,
            "Assets/Game/Props/Generic/Art/BigRock.fbx",
            "Model",
            "ModelImporter.addCollider",
            "true");
    }

    [Test]
    public void ImportRuleSet_SamePriorityDifferentValueCreatesConflictAndNoApplicableItems()
    {
        object ruleSet = CreateRuleSetWithRules(
            CreateImportRule(
                "Rock Contains",
                "Model",
                "ModelImporter.addCollider",
                "true",
                packageNameMatch: "Contains",
                packageNamePattern: "Rock"),
            CreateImportRule(
                "Rock EndsWith",
                "Model",
                "ModelImporter.addCollider",
                "false",
                packageNameMatch: "EndsWith",
                packageNamePattern: "Rock"));

        object effectiveSettings = BuildEffectiveImportSettings(
            ruleSet,
            "Assets/Game/Props/Generic/Art/BigRock.fbx",
            "Model");
        Array conflicts = GetField<Array>(effectiveSettings, "conflicts");
        Array propertyItems = (Array)effectiveSettings.GetType()
            .GetMethod("ToPropertyItems", BindingFlags.Public | BindingFlags.Instance)
            .Invoke(effectiveSettings, Array.Empty<object>());

        Assert.That(GetField<bool>(effectiveSettings, "hasConflicts"), Is.True);
        Assert.That(conflicts.Length, Is.EqualTo(1));
        Assert.That(propertyItems.Length, Is.EqualTo(0));
    }

    [Test]
    public void TryParseJson_AssemblyOnlyManifestParsesAssemblySettings()
    {
        bool result = TryParseJson(ValidJson, "Assets/Game/Props/Generic/Art/zzz.fbx", out object manifest, out string error);

        Assert.That(result, Is.True, error);
        Assert.That(GetField<int>(manifest, "schemaVersion"), Is.EqualTo(1));
        Assert.That(GetField<string>(manifest, "sourceFbx"), Is.EqualTo("zzz.fbx"));

        object assembly = GetField<object>(manifest, "assembly");
        object prefab = GetField<object>(assembly, "prefab");
        Assert.That(GetField<string>(prefab, "outputPath"), Is.EqualTo("Assets/Game/Worlds/Meadow/Runtime/Shared/Prefabs/P_zzz.prefab"));
    }

    [Test]
    public void TryParseJson_LegacyImportSettingsFieldIsIgnored()
    {
        bool result = TryParseJson(LegacyJsonWithImportSettings, "Assets/Game/Props/Generic/Art/zzz.fbx", out object manifest, out string error);

        Assert.That(result, Is.True, error);
        Assert.That(manifest.GetType().GetField("importSettings", BindingFlags.Public | BindingFlags.Instance), Is.Null);
    }

    [Test]
    public void TryParseJson_InvalidSchemaVersionFails()
    {
        string json = ValidJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");

        bool result = TryParseJson(json, "Assets/Game/Props/Generic/Art/zzz.fbx", out _, out string error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("schemaVersion must be 1"));
    }

    [Test]
    public void TryParseJson_EmptyPrefabPathFailsWhenPrefabEnabled()
    {
        string json = ValidJson.Replace(
            "\"outputPath\": \"Assets/Game/Worlds/Meadow/Runtime/Shared/Prefabs/P_zzz.prefab\"",
            "\"outputPath\": \"\"");

        bool result = TryParseJson(json, "Assets/Game/Props/Generic/Art/zzz.fbx", out _, out string error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("assembly.prefab.outputPath is required"));
    }

    [Test]
    public void TryParseJson_MissingLodLevelsFailsWhenLodEnabled()
    {
        string json = ValidJson.Replace(
            @"""levels"": [
        { ""index"": 0, ""nodePath"": ""zz_LOD0_LOD0"", ""screenRelativeHeight"": 0.6 },
        { ""index"": 1, ""nodePath"": ""zz_LOD0_LOD1"", ""screenRelativeHeight"": 0.35 },
        { ""index"": 2, ""nodePath"": ""zz_LOD0_LOD2"", ""screenRelativeHeight"": 0.18 }
      ]",
            @"""levels"": []");

        bool result = TryParseJson(json, "Assets/Game/Props/Generic/Art/zzz.fbx", out _, out string error);

        Assert.That(result, Is.False);
        Assert.That(error, Does.Contain("assembly.lodGroup.levels must contain at least one level"));
    }

    [Test]
    public void AssemblyProcessor_JsonLodLevelsCreateLodGroup()
    {
        Assert.That(TryParseJson(LodOnlyJson(), "Assets/Game/Props/Generic/Art/zzz.fbx", out object manifest, out string error), Is.True, error);

        GameObject root = CreateLodFixture();
        try
        {
            ApplyAssembly(root, manifest);

            LODGroup lodGroup = root.GetComponent<LODGroup>();
            Assert.That(lodGroup, Is.Not.Null);
            Assert.That(lodGroup.GetLODs().Length, Is.EqualTo(3));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void AssemblyProcessor_JsonColliderCreatesRequestedColliderAndDisablesRenderer()
    {
        Assert.That(TryParseJson(WithoutMaterials(ValidJson), "Assets/Game/Props/Generic/Art/zzz.fbx", out object manifest, out string error), Is.True, error);

        GameObject root = CreateLodFixture();
        GameObject colliderNode = new GameObject("UCX_body");
        colliderNode.transform.SetParent(root.transform, false);
        colliderNode.AddComponent<MeshFilter>().sharedMesh = CreateTriangleMesh();
        MeshRenderer renderer = colliderNode.AddComponent<MeshRenderer>();

        try
        {
            ApplyAssembly(root, manifest);

            MeshCollider collider = colliderNode.GetComponent<MeshCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.convex, Is.True);
            Assert.That(renderer.enabled, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void AssemblyProcessor_JsonMaterialSlotBindsConfiguredMaterial()
    {
        const string materialPath = "Assets/Tests/BuildPipeline/EditMode/TempDccJsonMaterial.mat";
        Material targetMaterial = CreateMaterialAsset(materialPath, "M_Wood_Target");
        string json = WithoutColliders(ValidJson).Replace("Assets/Game/Shared/StylizedPackCommon/Runtime/Materials/M_Wood.mat", materialPath);
        Assert.That(TryParseJson(json, "Assets/Game/Props/Generic/Art/zzz.fbx", out object manifest, out string error), Is.True, error);

        GameObject root = CreateLodFixture();
        Renderer renderer = root.transform.Find("zz_LOD0_LOD0").GetComponent<Renderer>();
        renderer.sharedMaterial = CreateTransientMaterial("M_Wood");

        try
        {
            ApplyAssembly(root, manifest);

            Assert.That(renderer.sharedMaterial, Is.EqualTo(targetMaterial));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(materialPath);
        }
    }

    [Test]
    public void PrefabGenerator_OverwriteFalseSkipsExistingPrefab()
    {
        Assert.That(TryParseJson(ValidJson, "Assets/Game/Props/Generic/Art/zzz.fbx", out object manifest, out string error), Is.True, error);
        object prefabConfig = GetField<object>(GetField<object>(manifest, "assembly"), "prefab");
        const string prefabPath = "Assets/Tests/BuildPipeline/EditMode/TempExistingDccPrefab.prefab";
        SetField(prefabConfig, "outputPath", prefabPath);

        GameObject instance = new GameObject("TempExistingDccPrefab");
        try
        {
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);

            bool shouldCreate = ShouldCreatePrefab(prefabConfig);

            Assert.That(shouldCreate, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
            AssetDatabase.DeleteAsset(prefabPath);
        }
    }

    private static GameObject CreateLodFixture()
    {
        GameObject root = new GameObject("Root");
        AddRendererChild(root.transform, "zz_LOD0_LOD0");
        AddRendererChild(root.transform, "zz_LOD0_LOD1");
        AddRendererChild(root.transform, "zz_LOD0_LOD2");
        return root;
    }

    private static void AddRendererChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.AddComponent<MeshFilter>().sharedMesh = CreateTriangleMesh();
        child.AddComponent<MeshRenderer>();
    }

    private static Mesh CreateTriangleMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new[]
        {
            Vector3.zero,
            Vector3.right,
            Vector3.up
        };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateMaterialAsset(string materialPath, string materialName)
    {
        AssetDatabase.DeleteAsset(materialPath);
        Material material = CreateTransientMaterial(materialName);
        AssetDatabase.CreateAsset(material, materialPath);
        AssetDatabase.ImportAsset(materialPath);
        return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
    }

    private static Material CreateTransientMaterial(string materialName)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard") ??
            Shader.Find("Sprites/Default");
        Material material = new Material(shader);
        material.name = materialName;
        return material;
    }

    private static string GetSidecarAssetPath(string assetPath)
    {
        Type sidecarType = GetRequiredType("ProjectFbxDccSidecar");
        return (string)sidecarType.GetMethod("GetSidecarAssetPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { assetPath });
    }

    private static bool TryLoad(string assetPath, out object manifest, out string error)
    {
        Type sidecarType = GetRequiredType("ProjectFbxDccSidecar");
        object[] args = { assetPath, null, null };
        bool result = (bool)sidecarType.GetMethod("TryLoad", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, args);
        manifest = args[1];
        error = args[2] as string;
        return result;
    }

    private static bool TryParseJson(string json, string assetPath, out object manifest, out string error)
    {
        Type sidecarType = GetRequiredType("ProjectFbxDccSidecar");
        object[] args = { json, assetPath, null, null };
        bool result = (bool)sidecarType.GetMethod("TryParseJson", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, args);
        manifest = args[2];
        error = args[3] as string;
        return result;
    }

    private static void ApplyAssembly(GameObject root, object manifest)
    {
        Type processorType = GetRequiredType("ProjectFbxDccAssemblyProcessor");
        processorType.GetMethod("Apply", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { root, manifest });
    }

    private static bool ShouldCreatePrefab(object prefabConfig)
    {
        Type generatorType = GetRequiredType("ProjectFbxDccPrefabGenerator");
        return (bool)generatorType.GetMethod("ShouldCreatePrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { prefabConfig });
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "Missing reflected field: " + fieldName);
        object value = field.GetValue(target);
        if (value == null)
            return default;

        return (T)value;
    }

    private static void SetField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "Missing reflected field: " + fieldName);
        field.SetValue(target, value);
    }

    private static string LodOnlyJson()
    {
        return WithoutMaterials(WithoutColliders(ValidJson));
    }

    private static string WithoutColliders(string json)
    {
        return json.Replace(
            @"    ""colliders"": [
      { ""nodePath"": ""UCX_body"", ""type"": ""Mesh"", ""convex"": true, ""disableRenderer"": true }
    ],",
            @"    ""colliders"": [],");
    }

    private static string WithoutMaterials(string json)
    {
        return json.Replace(
            @"    ""materials"": [
      { ""slotName"": ""M_Wood"", ""materialPath"": ""Assets/Game/Shared/StylizedPackCommon/Runtime/Materials/M_Wood.mat"" }
    ]",
            @"    ""materials"": []");
    }

    private static Type GetRequiredType(string typeName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(typeName, false))
            .FirstOrDefault(candidate => candidate != null);

        Assert.That(type, Is.Not.Null, "Unable to find editor type: " + typeName);
        return type;
    }

    private static void AssertResolvedProperty(
        string assetPath,
        string assetClass,
        string propertyPath,
        string expectedValue)
    {
        string value = ResolvePropertyValue(assetPath, assetClass, propertyPath);
        Assert.That(value, Is.EqualTo(expectedValue), assetPath + " / " + propertyPath);
    }

    private static string ResolvePropertyValue(string assetPath, string assetClass, string propertyPath)
    {
        Type ruleSetType = GetRequiredType("ProjectAssetImportRuleSet");
        object ruleSet = ruleSetType.GetMethod("CreateDefaultInstance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, Array.Empty<object>());
        return ResolvePropertyValue(ruleSet, assetPath, assetClass, propertyPath);
    }

    private static void AssertResolvedProperty(
        object ruleSet,
        string assetPath,
        string assetClass,
        string propertyPath,
        string expectedValue)
    {
        string value = ResolvePropertyValue(ruleSet, assetPath, assetClass, propertyPath);
        Assert.That(value, Is.EqualTo(expectedValue), assetPath + " / " + propertyPath);
    }

    private static string ResolvePropertyValue(object ruleSet, string assetPath, string assetClass, string propertyPath)
    {
        object context = CreateRuleContext(assetPath, assetClass);
        object effectiveSettings = ruleSet.GetType().GetMethod("BuildEffectiveImportSettings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(ruleSet, new[] { context });

        Array settings = GetField<Array>(effectiveSettings, "settings");
        foreach (object setting in settings)
        {
            if (string.Equals(GetField<string>(setting, "propertyPath"), propertyPath, StringComparison.OrdinalIgnoreCase))
                return GetField<string>(setting, "value");
        }

        return null;
    }

    private static object BuildEffectiveImportSettings(object ruleSet, string assetPath, string assetClass)
    {
        object context = CreateRuleContext(assetPath, assetClass);
        return ruleSet.GetType().GetMethod("BuildEffectiveImportSettings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(ruleSet, new[] { context });
    }

    private static object CreateRuleSetWithRules(params object[] rules)
    {
        Type ruleSetType = GetRequiredType("ProjectAssetImportRuleSet");
        Type ruleType = GetRequiredType("ProjectAssetImportRule");
        object ruleSet = ScriptableObject.CreateInstance(ruleSetType);
        Array ruleArray = Array.CreateInstance(ruleType, rules.Length);
        for (int i = 0; i < rules.Length; i++)
            ruleArray.SetValue(rules[i], i);

        SetField(ruleSet, "rules", ruleArray);
        return ruleSet;
    }

    private static object CreateImportRule(
        string name,
        string assetClass,
        string propertyPath,
        string value,
        string directoryMatch = "Any",
        string directoryPattern = null,
        string packageNameMatch = "Any",
        string packageNamePattern = null)
    {
        Type ruleType = GetRequiredType("ProjectAssetImportRule");
        Type filterType = GetRequiredType("ProjectAssetRuleFilter");
        Type classType = GetRequiredType("ProjectAssetClass");
        Type matchModeType = GetRequiredType("ProjectAssetStringMatchMode");
        Type itemType = GetRequiredType("ProjectAssetPropertyItem");
        Type valueKindType = GetRequiredType("ProjectAssetPropertyValueKind");

        object rule = Activator.CreateInstance(ruleType);
        object filter = Activator.CreateInstance(filterType);
        SetField(filter, "assetClass", Enum.Parse(classType, assetClass));
        SetField(filter, "directoryMatch", Enum.Parse(matchModeType, directoryMatch));
        SetField(filter, "directoryPattern", directoryPattern);
        SetField(filter, "packageNameMatch", Enum.Parse(matchModeType, packageNameMatch));
        SetField(filter, "packageNamePattern", packageNamePattern);

        object item = Activator.CreateInstance(itemType);
        SetField(item, "propertyPath", propertyPath);
        SetField(item, "valueKind", Enum.Parse(valueKindType, GuessValueKindName(value)));
        SetField(item, "value", value);

        Array items = Array.CreateInstance(itemType, 1);
        items.SetValue(item, 0);

        SetField(rule, "name", name);
        SetField(rule, "enabled", true);
        SetField(rule, "filter", filter);
        SetField(rule, "propertyItems", items);
        SetField(rule, "legacyPropertyItemsMigrated", false);
        return rule;
    }

    private static string GuessValueKindName(string value)
    {
        if (bool.TryParse(value, out _))
            return "Bool";
        if (int.TryParse(value, out _))
            return "Int";
        if (float.TryParse(value, out _))
            return "Float";

        return "Enum";
    }

    private static object CreateRuleContext(string assetPath, string assetClass)
    {
        Type contextType = GetRequiredType("ProjectAssetRuleContext");
        Type classType = GetRequiredType("ProjectAssetClass");
        object context = Activator.CreateInstance(contextType);
        SetField(context, "assetPath", assetPath.Replace('\\', '/'));
        SetField(context, "directory", System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? string.Empty);
        SetField(context, "packageName", System.IO.Path.GetFileNameWithoutExtension(assetPath));
        SetField(context, "assetClass", Enum.Parse(classType, assetClass));
        return context;
    }

    private static bool StringMatches(string matchMode, string pattern, string value)
    {
        Type matcherType = GetRequiredType("ProjectAssetStringMatcher");
        Type matchModeType = GetRequiredType("ProjectAssetStringMatchMode");
        object parsedMatchMode = Enum.Parse(matchModeType, matchMode);
        return (bool)matcherType.GetMethod("Matches", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new[] { parsedMatchMode, pattern, value });
    }

    private const string ValidJson = @"{
  ""schemaVersion"": 1,
  ""sourceFbx"": ""zzz.fbx"",
  ""assembly"": {
    ""prefab"": {
      ""enabled"": true,
      ""outputPath"": ""Assets/Game/Worlds/Meadow/Runtime/Shared/Prefabs/P_zzz.prefab"",
      ""overwrite"": false
    },
    ""lodGroup"": {
      ""enabled"": true,
      ""rootPath"": """",
      ""levels"": [
        { ""index"": 0, ""nodePath"": ""zz_LOD0_LOD0"", ""screenRelativeHeight"": 0.6 },
        { ""index"": 1, ""nodePath"": ""zz_LOD0_LOD1"", ""screenRelativeHeight"": 0.35 },
        { ""index"": 2, ""nodePath"": ""zz_LOD0_LOD2"", ""screenRelativeHeight"": 0.18 }
      ]
    },
    ""colliders"": [
      { ""nodePath"": ""UCX_body"", ""type"": ""Mesh"", ""convex"": true, ""disableRenderer"": true }
    ],
    ""materials"": [
      { ""slotName"": ""M_Wood"", ""materialPath"": ""Assets/Game/Shared/StylizedPackCommon/Runtime/Materials/M_Wood.mat"" }
    ]
  }
}";

    private const string LegacyJsonWithImportSettings = @"{
  ""schemaVersion"": 1,
  ""sourceFbx"": ""zzz.fbx"",
  ""importSettings"": {
    ""kind"": ""StaticModel"",
    ""rig"": ""None"",
    ""preserveHierarchy"": true,
    ""importAnimation"": false,
    ""importBlendShapes"": false,
    ""generateLightmapUv"": false,
    ""generateColliders"": false,
    ""readWrite"": false,
    ""meshCompression"": ""Low"",
    ""animationCompression"": ""Optimal""
  },
  ""assembly"": {
    ""prefab"": {
      ""enabled"": false,
      ""outputPath"": """",
      ""overwrite"": false
    },
    ""lodGroup"": {
      ""enabled"": false,
      ""rootPath"": """",
      ""levels"": []
    },
    ""colliders"": [],
    ""materials"": []
  }
}";
}
