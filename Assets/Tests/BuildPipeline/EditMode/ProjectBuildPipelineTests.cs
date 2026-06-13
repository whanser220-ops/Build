using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

public sealed class ProjectBuildPipelineTests
{
    private const string WorkflowPath = ".github/workflows/unity-ci.yml";
    private const string WaitCiOutputScriptPath = "tools/Wait-CiOutput.ps1";
    private const string AssertUnityTestResultsScriptPath = "tools/Assert-UnityTestResults.ps1";
    private const string UnityLibraryCacheScriptPath = "tools/Use-UnityLibraryCache.ps1";
    private const string PlayerBuildScriptPath = "Assets/Editor/BuildPipeline/CiPlayerBuild.cs";
    private const string AddressablesBuildScriptPath = "Assets/Editor/BuildPipeline/Addressables/ProjectAddressablesBuild.cs";
    private const string AddressablesConverterScriptPath = "Assets/Editor/BuildPipeline/Addressables/AngryMeshAddressablesReferenceConverter.cs";
    private const string AddressablesRuntimeLoaderPath = "Assets/Scripts/Addressables/AngryMeshAddressablePrefabInstance.cs";
    private const string SmokeScenePath = "Assets/Tests/BuildPipeline/Fixtures/BuildPipelineSmoke.unity";
    private const string AndroidOutputPath = ".workspace/builds/android/Unity6-Android-Development.apk";
    private const string WindowsOutputPath = ".workspace/builds/windows/Unity6-Windows-Development/Unity6.exe";
    private const string WindowsArchivePath = ".workspace/builds/windows/Unity6-Windows-Development.zip";

    [Test]
    public void CiRunsOnlyBuildPipelineEditModeAssembly()
    {
        string workflow = ReadRequiredText(WorkflowPath);

        StringAssert.Contains("-testPlatform EditMode", workflow);
        StringAssert.Contains("-assemblyNames Project.BuildPipeline.EditMode.Tests", workflow);
        StringAssert.Contains("Assert-UnityTestResults.ps1", workflow);
        Assert.That(workflow, Does.Not.Contain("-testPlatform PlayMode"));
    }

    [Test]
    public void CiBuildArtifactsUseExpectedOutputPaths()
    {
        string workflow = ReadRequiredText(WorkflowPath);

        StringAssert.Contains(AndroidOutputPath, workflow);
        StringAssert.Contains(WindowsOutputPath, workflow);
        StringAssert.Contains(WindowsArchivePath, workflow);
        StringAssert.Contains(SmokeScenePath, workflow);
        StringAssert.Contains("Wait-CiOutput.ps1", workflow);
        StringAssert.Contains("actions/upload-artifact", workflow);
        ReadRequiredText(WaitCiOutputScriptPath);
        ReadRequiredText(AssertUnityTestResultsScriptPath);
    }

    [Test]
    public void CiCheckoutUsesBuildPipelineSparseSourceSet()
    {
        string workflow = ReadRequiredText(WorkflowPath);

        StringAssert.Contains("sparse-checkout:", workflow);
        StringAssert.Contains("lfs: false", workflow);
        StringAssert.Contains("ProjectSettings/**", workflow);
        StringAssert.Contains("Packages/**", workflow);
        StringAssert.Contains("Assets/Editor/**", workflow);
        StringAssert.Contains("Assets/Scripts.meta", workflow);
        StringAssert.Contains("Assets/Scripts/Addressables.meta", workflow);
        StringAssert.Contains("Assets/Scripts/Addressables/**", workflow);
        StringAssert.Contains("Assets/Settings/**", workflow);
        StringAssert.Contains("Assets/Tests/BuildPipeline/**", workflow);
        StringAssert.Contains("tools/Assert-UnityTestResults.ps1", workflow);
        StringAssert.Contains("tools/Use-UnityLibraryCache.ps1", workflow);
        Assert.That(workflow, Does.Not.Contain("lfs: true"));
    }

    [Test]
    public void CiUsesSinglePersistentUnityLibraryCache()
    {
        string workflow = ReadRequiredText(WorkflowPath);
        string cacheScript = ReadRequiredText(UnityLibraryCacheScriptPath);

        StringAssert.Contains("Attach Unity Library cache", workflow);
        StringAssert.Contains("Use-UnityLibraryCache.ps1 -ProjectPath .", workflow);
        Assert.That(workflow, Does.Not.Contain("Remove-Item -LiteralPath \"Library\""));
        Assert.That(workflow, Does.Not.Contain("Remove-Item -LiteralPath \"Library/\""));
        StringAssert.Contains("Unity6Ci\\LibraryCache", cacheScript);
        StringAssert.Contains("single-library-v1", cacheScript);
        StringAssert.Contains("cache-info.json", cacheScript);
        StringAssert.Contains("Library.stale.", cacheScript);
        StringAssert.Contains("New-Item -ItemType Junction", cacheScript);
        Assert.That(cacheScript, Does.Not.Contain("sourceHash"));
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
        StringAssert.Contains("--ci-scenes", playerBuildScript);
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
    public void AddressablesBuildToolsAreOutsideLegacyContentRoots()
    {
        string buildScript = ReadRequiredText(AddressablesBuildScriptPath);
        string converterScript = ReadRequiredText(AddressablesConverterScriptPath);
        string runtimeLoaderScript = ReadRequiredText(AddressablesRuntimeLoaderPath);

        StringAssert.Contains("public static void BuildFromCommandLine()", buildScript);
        StringAssert.Contains("AddressableAssetSettings.BuildPlayerContent", buildScript);
        StringAssert.Contains("public static void ConvertEnabledScenePrefabsFromCommandLine()", converterScript);
        StringAssert.Contains("AngryMeshAddressablePrefabInstance", converterScript);
        StringAssert.Contains("public sealed class AngryMeshAddressablePrefabInstance", runtimeLoaderScript);

        Assert.That(File.Exists("Assets/Project/Tools/Build/Editor/ProjectAddressablesBuild.cs"), Is.False);
        Assert.That(File.Exists("Assets/Project/Tools/Build/Editor/AngryMeshAddressablesReferenceConverter.cs"), Is.False);
        Assert.That(File.Exists("Assets/Project/Tools/Build/Runtime/AngryMeshAddressablePrefabInstance.cs"), Is.False);
    }

    [Test]
    public void CiSmokeSceneFixtureExists()
    {
        Assert.That(File.Exists(SmokeScenePath), Is.True, "Missing CI build smoke scene fixture.");
        Assert.That(
            AssetDatabase.LoadAssetAtPath<SceneAsset>(SmokeScenePath),
            Is.Not.Null,
            "CI build smoke scene is missing or is not a SceneAsset: " + SmokeScenePath);
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
