using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public sealed class ProjectYooAssetCollectorRuleUtilityTests
{
    [Test]
    public void GetDependencyBucket_TextureSkipsTextureTypePrefix()
    {
        Assert.That(
            GetDependencyBucket("Assets/Game/Worlds/Meadow/Art/Textures/Background/T_Mountain_01_A.tif"),
            Is.EqualTo("m"));
    }

    [Test]
    public void GetDependencyBucket_TextureNumericNameAfterPrefixUsesNumBucket()
    {
        Assert.That(
            GetDependencyBucket("Assets/Game/Worlds/Meadow/Art/Textures/Debug/T_01_Debug.tif"),
            Is.EqualTo("num"));
    }

    [Test]
    public void GetDependencyBucket_NonTextureDependencyKeepsOriginalFirstLetter()
    {
        Assert.That(
            GetDependencyBucket("Assets/Game/Worlds/Meadow/Art/Meshes/T_Rock.fbx"),
            Is.EqualTo("t"));
    }

    private static string GetDependencyBucket(string assetPath)
    {
        Type utilityType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("ProjectYooAssetCollectorRuleUtility", false))
            .FirstOrDefault(candidate => candidate != null);

        Assert.That(utilityType, Is.Not.Null, "Unable to find ProjectYooAssetCollectorRuleUtility.");

        MethodInfo method = utilityType.GetMethod("GetDependencyBucket", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "Unable to find GetDependencyBucket.");

        return (string)method.Invoke(null, new object[] { assetPath });
    }
}
