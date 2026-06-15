using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class QianxiaMeadowSceneMigrationTests
{
    private const string MeadowSummerScenePath = "Assets/GameAssets/Worlds/Meadow/Scenes/Scene_MeadowEnvironment_01_Summer.unity";
    private const string CharacterPrefabPath = "Assets/Project/Characters/Qianxia/Generated/QianxiaThirdPerson.prefab";
    private const string CharacterObjectName = "QianxiaThirdPerson";
    private const string MigrationUtilityPath = "Assets/Project/Characters/Qianxia/Editor/QianxiaMeadowSceneMigrationUtility.cs";

    [Test]
    public void MeadowSummerSceneContainsQianxiaThirdPersonPrefabInstance()
    {
        EditorSceneManager.OpenScene(MeadowSummerScenePath, OpenSceneMode.Single);

        GameObject character = GameObject.Find(CharacterObjectName);

        Assert.That(character, Is.Not.Null, "Meadow Summer scene should contain QianxiaThirdPerson.");
        Assert.That(
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(character),
            Is.EqualTo(CharacterPrefabPath));
        Assert.That(character.GetComponent<CharacterController>(), Is.Not.Null);
        Assert.That(character.GetComponent("QianxiaGenshinCharacterController"), Is.Not.Null);
        Assert.That(character.GetComponent("QianxiaGenshinCameraController"), Is.Not.Null);
    }

    [Test]
    public void MeadowSummerSceneMainCameraIsResolvableByCharacterControllers()
    {
        EditorSceneManager.OpenScene(MeadowSummerScenePath, OpenSceneMode.Single);

        Camera sceneCamera = Camera.main;

        Assert.That(sceneCamera, Is.Not.Null, "Meadow Summer scene should expose a MainCamera-tagged camera.");
        Assert.That(sceneCamera.gameObject.name, Is.EqualTo("Main Camera"));
        Assert.That(sceneCamera.gameObject.CompareTag("MainCamera"), Is.True);
    }

    [Test]
    public void MigrationEntrypointsRemainAvailableForCurrentBuildScene()
    {
        string script = System.IO.File.ReadAllText(MigrationUtilityPath);

        StringAssert.Contains("MigrateThirdPersonToCurrentBuildSceneFromCommandLine", script);
        StringAssert.Contains("MigrateThirdPersonToMeadowSummerSceneFromCommandLine", script);
        StringAssert.Contains(MeadowSummerScenePath, script);
    }
}
