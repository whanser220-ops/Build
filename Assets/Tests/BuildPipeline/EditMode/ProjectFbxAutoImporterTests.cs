using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public sealed class ProjectFbxAutoImporterTests
{
    [Test]
    public void Evaluate_CharactersPathUsesHumanoidCharacterProfile()
    {
        object profile = Evaluate(
            "Assets/GameResources/Characters/Hero/HeroBody.fbx",
            CreateSignals(hasSkeletonOrSkinning: true));

        AssertProfile(profile, "CharacterModel");
        AssertBoolean(profile, "IsCharacterPath", true);
        AssertBoolean(profile, "UseHumanoidRig", true);
        AssertBoolean(profile, "HasSkeletonOrSkinning", true);
    }

    [Test]
    public void Evaluate_SourceAnimationsPathUsesAnimationProfile()
    {
        object profile = Evaluate(
            "Assets/GameResources/Characters/Hero/SourceAnimations/Walk/Hero_Walk.fbx",
            CreateSignals(hasSkeletonOrSkinning: true, hasAnimationStacks: true, hasMultipleAnimationStacks: true));

        AssertProfile(profile, "AnimationAsset");
        AssertBoolean(profile, "HasMultipleAnimationStacks", true);
        AssertBoolean(profile, "UseHumanoidRig", true);
    }

    [Test]
    public void Evaluate_PropsPathEnablesStaticColliderAndLightmapUvRules()
    {
        object profile = Evaluate(
            "Assets/GameResources/Props/Crates/SM_Crate.fbx",
            CreateSignals());

        AssertProfile(profile, "StaticModel");
        AssertBoolean(profile, "IsPropsPath", true);
        AssertBoolean(profile, "GenerateColliders", true);
        AssertBoolean(profile, "GenerateLightmapUv", true);
    }

    [Test]
    public void Evaluate_LodSignalUsesLodProfileAndKeepsColliderSignal()
    {
        object profile = Evaluate(
            "Assets/GameResources/Environment/Trees/SM_Tree.fbx",
            CreateSignals(hasLodNodes: true, hasCollisionNodes: true));

        AssertProfile(profile, "LodModel");
        AssertBoolean(profile, "HasLodNodes", true);
        AssertBoolean(profile, "GenerateColliders", true);
    }

    [Test]
    public void NamingRulesRecognizeLodAndSimpleCollisionPrefixes()
    {
        Assert.That(TryGetLodIndex("SM_Tree_LOD2", out int lodIndex), Is.True);
        Assert.That(lodIndex, Is.EqualTo(2));

        Assert.That(TryGetCollisionKind("UBX_SM_Crate_01", out string collisionKind), Is.True);
        Assert.That(collisionKind, Is.EqualTo("Box"));
    }

    [Test]
    public void NamingRulesUseLastLodTokenWhenExporterRepeatsBaseLodName()
    {
        Assert.That(TryGetLodIndex("zz_LOD0_LOD0", out int lod0), Is.True);
        Assert.That(TryGetLodIndex("zz_LOD0_LOD1", out int lod1), Is.True);
        Assert.That(TryGetLodIndex("zz_LOD0_LOD2", out int lod2), Is.True);

        Assert.That(lod0, Is.EqualTo(0));
        Assert.That(lod1, Is.EqualTo(1));
        Assert.That(lod2, Is.EqualTo(2));
    }

    [Test]
    public void MeadowPrefabRules_MapMeshFolderToSharedPrefabFolder()
    {
        Assert.That(TryGetMeadowPrefabPath(
            "Assets/GameResources/Stylized Pack - Meadow Environment/Sources/Meshes/Flowers/SM_ZZ_1.fbx",
            out string prefabPath), Is.True);

        Assert.That(prefabPath, Is.EqualTo(
            "Assets/GameAssets/Worlds/Meadow/Shared/Prefabs/Flowers/P_ZZ_1.prefab"));
    }

    [Test]
    public void MeadowPrefabRules_RemovesSmPrefixWhenMakingPrefabName()
    {
        Assert.That(MakeMeadowPrefabName("SM_Hill_01"), Is.EqualTo("P_Hill_01"));
    }

    [Test]
    public void MeadowPrefabRules_AddsPPrefixForNonSmModelNames()
    {
        Assert.That(MakeMeadowPrefabName("zzz"), Is.EqualTo("P_zzz"));
    }

    [Test]
    public void MeadowPrefabRules_IgnoreFbxOutsideMeadowSourceMeshes()
    {
        Assert.That(TryGetMeadowPrefabPath(
            "Assets/GameResources/Characters/Qianxia/Meshs/Qianxia_Rokoko_BlenderClean.fbx",
            out string prefabPath), Is.False);
        Assert.That(prefabPath, Is.Null);
    }

    [Test]
    public void MeadowPrefabRules_SkipWhenTargetPrefabAlreadyExists()
    {
        Assert.That(ShouldCreateMeadowPrefab(
            "Assets/GameResources/Stylized Pack - Meadow Environment/Sources/Meshes/Flowers/SM_Flower_10_03.fbx"), Is.False);
    }

    private static object Evaluate(string assetPath, object signals)
    {
        Type rulesType = GetRequiredType("ProjectFbxImportRules");
        MethodInfo method = rulesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(item =>
                item.Name == "Evaluate" &&
                item.GetParameters().Length == 2 &&
                item.GetParameters()[1].ParameterType.Name == "ProjectFbxFileSignals");

        return method.Invoke(null, new[] { assetPath, signals });
    }

    private static object CreateSignals(
        bool hasSkeletonOrSkinning = false,
        bool hasAnimationStacks = false,
        bool hasMultipleAnimationStacks = false,
        bool hasLodNodes = false,
        bool hasCollisionNodes = false,
        bool hasCustomCollisionHint = false)
    {
        Type signalsType = GetRequiredType("ProjectFbxFileSignals");
        object signals = Activator.CreateInstance(signalsType);
        SetBoolean(signals, "HasSkeletonOrSkinning", hasSkeletonOrSkinning);
        SetBoolean(signals, "HasAnimationStacks", hasAnimationStacks);
        SetBoolean(signals, "HasMultipleAnimationStacks", hasMultipleAnimationStacks);
        SetBoolean(signals, "HasLodNodes", hasLodNodes);
        SetBoolean(signals, "HasCollisionNodes", hasCollisionNodes);
        SetBoolean(signals, "HasCustomCollisionHint", hasCustomCollisionHint);
        return signals;
    }

    private static bool TryGetLodIndex(string nodeName, out int index)
    {
        Type namingType = GetRequiredType("ProjectFbxNaming");
        object[] args = { nodeName, null };
        bool result = (bool)namingType.GetMethod("TryGetLodIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, args);
        index = (int)args[1];
        return result;
    }

    private static bool TryGetCollisionKind(string nodeName, out string kind)
    {
        Type namingType = GetRequiredType("ProjectFbxNaming");
        object[] args = { nodeName, null };
        bool result = (bool)namingType.GetMethod("TryGetCollisionKind", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, args);
        kind = args[1].ToString();
        return result;
    }

    private static bool TryGetMeadowPrefabPath(string assetPath, out string prefabPath)
    {
        Type rulesType = GetRequiredType("ProjectFbxMeadowPrefabGenerationRules");
        object[] args = { assetPath, null };
        bool result = (bool)rulesType.GetMethod("TryGetPrefabPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, args);
        prefabPath = args[1] as string;
        return result;
    }

    private static string MakeMeadowPrefabName(string modelName)
    {
        Type rulesType = GetRequiredType("ProjectFbxMeadowPrefabGenerationRules");
        return (string)rulesType.GetMethod("MakePrefabName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { modelName });
    }

    private static bool ShouldCreateMeadowPrefab(string assetPath)
    {
        Type rulesType = GetRequiredType("ProjectFbxMeadowPrefabGenerationRules");
        return (bool)rulesType.GetMethod("ShouldCreatePrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { assetPath });
    }

    private static void AssertProfile(object profile, string expectedKind)
    {
        Assert.That(GetProperty(profile, "Kind").ToString(), Is.EqualTo(expectedKind));
    }

    private static void AssertBoolean(object target, string propertyName, bool expected)
    {
        Assert.That((bool)GetProperty(target, propertyName), Is.EqualTo(expected), propertyName);
    }

    private static void SetBoolean(object target, string propertyName, bool value)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(property, Is.Not.Null, "Missing reflected property: " + propertyName);
        property.SetValue(target, value);
    }

    private static object GetProperty(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(property, Is.Not.Null, "Missing reflected property: " + propertyName);
        return property.GetValue(target);
    }

    private static Type GetRequiredType(string typeName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(typeName, false))
            .FirstOrDefault(candidate => candidate != null);

        Assert.That(type, Is.Not.Null, "Unable to find editor type: " + typeName);
        return type;
    }
}
