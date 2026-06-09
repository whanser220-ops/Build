using NUnit.Framework;

public class RenderDocStartupSceneUtilityTests
{
    [Test]
    public void NormalizeScenePath_TrimsWhitespaceAndUsesForwardSlashes()
    {
        string normalized = RenderDocStartupSceneUtility.NormalizeScenePath(@"  Assets\Scenes\TestScene.unity  ");

        Assert.That(normalized, Is.EqualTo("Assets/Scenes/TestScene.unity"));
    }

    [Test]
    public void ScenePathsMatch_IgnoresSlashStyleAndCase()
    {
        bool matches = RenderDocStartupSceneUtility.ScenePathsMatch(
            @"Assets\Scenes\TestScene.unity",
            "assets/scenes/testscene.unity");

        Assert.That(matches, Is.True);
    }

    [Test]
    public void ResolveBuildScenePaths_PrioritizesActiveSceneAndRemovesDuplicates()
    {
        string[] resolved = RenderDocStartupSceneUtility.ResolveBuildScenePaths(
            "Assets/Scenes/Scene_MeadowEnvironment_01_Summer.unity",
            new[]
            {
                "Assets/Scenes/CrowdBattle.unity",
                "Assets/Scenes/Scene_MeadowEnvironment_01_Summer.unity",
                @"Assets\Scenes\CrowdBattle.unity",
                " "
            });

        Assert.That(resolved, Is.EqualTo(new[]
        {
            "Assets/Scenes/Scene_MeadowEnvironment_01_Summer.unity",
            "Assets/Scenes/CrowdBattle.unity"
        }));
    }
}
