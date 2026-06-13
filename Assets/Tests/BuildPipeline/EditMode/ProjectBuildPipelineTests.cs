using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

public sealed class ProjectBuildPipelineTests
{
    private const string WorkflowPath = ".github/workflows/unity-ci.yml";
    private const string WaitCiOutputScriptPath = "tools/Wait-CiOutput.ps1";
    private const string PlayerBuildScriptPath = "Assets/Editor/BuildPipeline/CiPlayerBuild.cs";
    private const string AndroidOutputPath = ".workspace/builds/android/Unity6-Android-Development.apk";
    private const string WindowsOutputPath = ".workspace/builds/windows/Unity6-Windows-Development/Unity6.exe";
    private const string WindowsArchivePath = ".workspace/builds/windows/Unity6-Windows-Development.zip";

    [Test]
    public void CiRunsOnlyBuildPipelineEditModeAssembly()
    {
        string workflow = ReadRequiredText(WorkflowPath);

        StringAssert.Contains("-testPlatform EditMode", workflow);
        StringAssert.Contains("-assemblyNames Project.BuildPipeline.EditMode.Tests", workflow);
        Assert.That(workflow, Does.Not.Contain("-testPlatform PlayMode"));
    }

    [Test]
    public void CiBuildArtifactsUseExpectedOutputPaths()
    {
        string workflow = ReadRequiredText(WorkflowPath);

        StringAssert.Contains(AndroidOutputPath, workflow);
        StringAssert.Contains(WindowsOutputPath, workflow);
        StringAssert.Contains(WindowsArchivePath, workflow);
        StringAssert.Contains("Wait-CiOutput.ps1", workflow);
        StringAssert.Contains("actions/upload-artifact", workflow);
        ReadRequiredText(WaitCiOutputScriptPath);
    }

    [Test]
    public void PlayerBuildCommandLineEntryPointsArePresent()
    {
        string playerBuildScript = ReadRequiredText(PlayerBuildScriptPath);

        StringAssert.Contains("public static void BuildAndroidDevelopment()", playerBuildScript);
        StringAssert.Contains("public static void BuildWindowsDevelopment()", playerBuildScript);
        StringAssert.Contains("Unity6.Ci", playerBuildScript);
        StringAssert.Contains("BuildTarget.StandaloneWindows64", playerBuildScript);
        StringAssert.Contains("BuildTarget.Android", playerBuildScript);
        StringAssert.Contains("BuildPipeline.BuildPlayer", playerBuildScript);
        StringAssert.Contains("--ci-output", playerBuildScript);
    }

    [Test]
    public void BuildPipelineDoesNotTargetLegacyContentRoots()
    {
        string workflow = ReadRequiredText(WorkflowPath).Replace('\\', '/');
        string buildScriptPath = PlayerBuildScriptPath.Replace('\\', '/');
        string legacyProjectRoot = string.Concat("Assets", "/", "Project", "/");
        string legacyThirdPartyRoot = string.Concat("Assets", "/", "ThirdParty", "/");

        Assert.That(buildScriptPath, Does.Not.StartWith(legacyProjectRoot));
        Assert.That(buildScriptPath, Does.Not.StartWith(legacyThirdPartyRoot));
        Assert.That(workflow, Does.Not.Contain(legacyProjectRoot));
        Assert.That(workflow, Does.Not.Contain(legacyThirdPartyRoot));
    }

    [Test]
    public void EditorBuildSettingsContainsBuildableScenes()
    {
        EditorBuildSettingsScene[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
            .ToArray();

        Assert.That(enabledScenes, Is.Not.Empty, "Player builds require at least one enabled scene.");
        foreach (EditorBuildSettingsScene scene in enabledScenes)
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path),
                Is.Not.Null,
                "Enabled build scene is missing or is not a SceneAsset: " + scene.path);
        }
    }

    [Test]
    public void WindowsArchiveNameMatchesWindowsPlayerDirectory()
    {
        string normalizedPlayerOutput = WindowsOutputPath.Replace('\\', '/');
        string normalizedArchiveOutput = WindowsArchivePath.Replace('\\', '/');
        string playerDirectory = Path.GetDirectoryName(normalizedPlayerOutput).Replace('\\', '/');
        string archiveDirectory = Path.GetDirectoryName(normalizedArchiveOutput).Replace('\\', '/');
        string archiveNameWithoutExtension = Path.GetFileNameWithoutExtension(normalizedArchiveOutput);

        Assert.That(Path.GetFileName(normalizedPlayerOutput), Is.EqualTo("Unity6.exe"));
        Assert.That(archiveDirectory, Is.EqualTo(Path.GetDirectoryName(playerDirectory).Replace('\\', '/')));
        Assert.That(archiveNameWithoutExtension, Is.EqualTo(Path.GetFileName(playerDirectory)));
    }

    private static string ReadRequiredText(string relativePath)
    {
        Assert.That(File.Exists(relativePath), Is.True, "Missing required build pipeline file: " + relativePath);
        return File.ReadAllText(relativePath);
    }
}
