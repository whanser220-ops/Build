using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RenderDocPlayerBuildUtility
{
    private const string DefaultBuildDirectory = ".workspace/artifacts/builds/renderdoc-d3d12";
    private const string DefaultExecutableName = "QianxiaRenderDocD3D12.exe";
    private const string DefaultAiDebugArtifactsDirectory = ".workspace/artifacts/ai-debug";
    private const string DefaultAiDebugTraceDirectoryName = "repro-traces";
    private const string DefaultAiDebugProfileFileName = "external-player-profile.json";
    private const string LaunchScriptFileName = "Launch-Under-RenderDoc.cmd";
    private const string StartupScenePathFileName = "renderdoc-startup-scene.txt";
    private const string DefaultRenderDocInstallDirectory = @"C:\Program Files\RenderDoc";
    private const string BeeTransientLogPath = "Library/Bee/tundra.log.json";
    private const int MaxBuildAttempts = 2;
    private const int BeeBackendExitWaitMilliseconds = 5000;
    private const int BeeRetryDelayMilliseconds = 250;

    [MenuItem("Tools/Qianxia/RenderDoc Capture/Build Fixed Player")]
    public static void BuildFromMenu()
    {
        BuildDefaultPlayer(revealOutputPath: true);
    }

    public static void BuildFromCommandLine()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string outputPath = GetArgumentValue(arguments, "--renderdoc-build-output");
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = GetDefaultExecutablePath();

        BuildDevelopmentPlayer(outputPath);
    }

    public static void BuildDefaultPlayer(bool revealOutputPath = false)
    {
        string executablePath = GetDefaultExecutablePath();
        BuildDevelopmentPlayer(executablePath);
        if (revealOutputPath)
            EditorUtility.RevealInFinder(executablePath);
    }

    public static string GetDefaultExecutablePath()
    {
        string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        return Path.Combine(projectRoot, DefaultBuildDirectory, DefaultExecutableName);
    }

    private static void BuildDevelopmentPlayer(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path is empty.", nameof(executablePath));

        string fullExecutablePath = Path.GetFullPath(executablePath);
        string buildDirectory = Path.GetDirectoryName(fullExecutablePath);
        if (string.IsNullOrEmpty(buildDirectory))
            throw new InvalidOperationException("Build directory could not be resolved.");

        Directory.CreateDirectory(buildDirectory);

        string activeScenePath = GetActiveEditorScenePath();
        string[] enabledScenePaths = EditorBuildSettings.scenes
            .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
            .Select(scene => scene.path)
            .ToArray();
        string[] scenes = RenderDocStartupSceneUtility.ResolveBuildScenePaths(activeScenePath, enabledScenePaths);

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes were found in EditorBuildSettings.");

        PrepareScenesForRenderDocBuild(scenes);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = fullExecutablePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        BuildReport report = BuildPlayerWithTransientBeeRetry(options);
        if (report == null)
            throw new InvalidOperationException("BuildPipeline.BuildPlayer returned null.");

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(string.Format(
                "RenderDoc player build failed. Result={0}, TotalErrors={1}, OutputPath={2}",
                report.summary.result,
                report.summary.totalErrors,
                fullExecutablePath));
        }

        UnityEngine.Debug.Log(string.Format(
            "RenderDoc player build succeeded. Output={0}, Size={1} bytes, Duration={2}",
            fullExecutablePath,
            report.summary.totalSize,
            report.summary.totalTime));

        RefreshExternalPlayerLaunchArtifacts(fullExecutablePath, activeScenePath, refreshBuildStamp: true);
    }

    internal static string GetActiveEditorScenePath()
    {
        return RenderDocStartupSceneUtility.NormalizeScenePath(EditorSceneManager.GetActiveScene().path);
    }

    internal static bool IsSceneIncludedInEnabledBuildSettings(string scenePath)
    {
        string normalizedScenePath = RenderDocStartupSceneUtility.NormalizeScenePath(scenePath);
        if (string.IsNullOrWhiteSpace(normalizedScenePath))
            return false;

        EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
        for (int index = 0; index < buildScenes.Length; index++)
        {
            EditorBuildSettingsScene buildScene = buildScenes[index];
            if (buildScene == null || !buildScene.enabled)
                continue;

            if (RenderDocStartupSceneUtility.ScenePathsMatch(buildScene.path, normalizedScenePath))
                return true;
        }

        return false;
    }

    internal static void RefreshExternalPlayerLaunchArtifacts(string executablePath, string startupScenePath, bool refreshBuildStamp)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return;

        string fullExecutablePath = Path.GetFullPath(executablePath);
        string buildDirectory = Path.GetDirectoryName(fullExecutablePath);
        if (string.IsNullOrWhiteSpace(buildDirectory))
            return;

        string applicationRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        string renderDocArtifactsRoot = Path.Combine(applicationRoot, RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot);
        string aiDebugRoot = Path.Combine(applicationRoot, DefaultAiDebugArtifactsDirectory);
        string aiDebugTraceDirectory = Path.Combine(aiDebugRoot, DefaultAiDebugTraceDirectoryName);
        string aiDebugProfilePath = Path.Combine(aiDebugRoot, DefaultAiDebugProfileFileName);
        string buildStampPath = Path.Combine(buildDirectory, CrowdVatAiDebugExternalPlayerBootstrap.BuildStampFileName);
        string launchScriptPath = Path.Combine(buildDirectory, LaunchScriptFileName);
        string startupScenePathFile = Path.Combine(buildDirectory, StartupScenePathFileName);

        Directory.CreateDirectory(aiDebugTraceDirectory);
        Directory.CreateDirectory(renderDocArtifactsRoot);

        if (!File.Exists(aiDebugProfilePath))
        {
            CrowdVatAiDebugExternalPlayerProfile defaultProfile = CrowdVatAiDebugExternalPlayerProfile.CreateDefault();
            string defaultProfileJson = JsonUtility.ToJson(defaultProfile, true);
            File.WriteAllText(aiDebugProfilePath, defaultProfileJson, new UTF8Encoding(false));
        }

        if (refreshBuildStamp)
            File.WriteAllText(buildStampPath, DateTime.UtcNow.ToString("o"), new UTF8Encoding(false));

        WriteStartupScenePathFile(startupScenePathFile, startupScenePath);

        string launchScript = BuildLaunchScript(
            fullExecutablePath,
            renderDocArtifactsRoot,
            aiDebugTraceDirectory,
            aiDebugProfilePath,
            buildStampPath,
            startupScenePathFile);
        File.WriteAllText(launchScriptPath, launchScript, new UTF8Encoding(false));
    }

    private static BuildReport BuildPlayerWithTransientBeeRetry(BuildPlayerOptions options)
    {
        for (int attempt = 1; attempt <= MaxBuildAttempts; attempt++)
        {
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null || report.summary.result == BuildResult.Succeeded)
                return report;

            if (attempt >= MaxBuildAttempts || !HasTransientBeeStructuredLoggingFailure())
                return report;

            UnityEngine.Debug.LogWarning(
                "RenderDoc fixed-player build hit a transient Bee structured logging failure. " +
                "Waiting for stale bee_backend processes, clearing Bee transient logs, and retrying once.");

            RecoverFromTransientBeeStructuredLoggingFailure();
        }

        return null;
    }

    private static void PrepareScenesForRenderDocBuild(string[] scenePaths)
    {
        if (scenePaths == null || scenePaths.Length == 0)
            return;

        SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();

        try
        {
            for (int sceneIndex = 0; sceneIndex < scenePaths.Length; sceneIndex++)
            {
                string scenePath = scenePaths[sceneIndex];
                if (string.IsNullOrWhiteSpace(scenePath))
                    continue;

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                bool sceneDirty = false;

                NewGrassIndirectRenderer[] grassRenderers = UnityEngine.Object.FindObjectsByType<NewGrassIndirectRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int rendererIndex = 0; rendererIndex < grassRenderers.Length; rendererIndex++)
                {
                    NewGrassIndirectRenderer renderer = grassRenderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    if (!EnsureGrassBakeAssetAssigned(renderer))
                    {
                        UnityEngine.Debug.LogWarning($"RenderDoc build prepare skipped grass bake assignment for {renderer.name} in scene {scene.path}.");
                        continue;
                    }

                    if (renderer.BakeDistributionDataAsset(true))
                    {
                        renderer.RebuildGrass();
                        EditorUtility.SetDirty(renderer);
                        sceneDirty = true;
                    }
                }

                CrowdVatIndirectRenderer[] crowdRenderers = UnityEngine.Object.FindObjectsByType<CrowdVatIndirectRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int rendererIndex = 0; rendererIndex < crowdRenderers.Length; rendererIndex++)
                {
                    CrowdVatIndirectRenderer renderer = crowdRenderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    if (EnsureCombatTracerShaderAssigned(renderer))
                    {
                        EditorUtility.SetDirty(renderer);
                        sceneDirty = true;
                    }
                }

                if (sceneDirty)
                    EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally
        {
            if (!Application.isBatchMode && originalSetup != null && originalSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }
    }

    private static bool EnsureGrassBakeAssetAssigned(NewGrassIndirectRenderer renderer)
    {
        if (renderer == null)
            return false;

        SerializedObject serializedRenderer = new SerializedObject(renderer);
        SerializedProperty distributionMapProperty = serializedRenderer.FindProperty("_distributionMap");
        SerializedProperty bakedPlacementAssetProperty = serializedRenderer.FindProperty("_bakedPlacementAsset");
        if (distributionMapProperty == null || bakedPlacementAssetProperty == null)
            return false;

        if (bakedPlacementAssetProperty.objectReferenceValue != null)
            return true;

        Texture2D distributionMap = distributionMapProperty.objectReferenceValue as Texture2D;
        if (distributionMap == null)
            return false;

        string distributionMapPath = AssetDatabase.GetAssetPath(distributionMap);
        if (string.IsNullOrWhiteSpace(distributionMapPath))
            return false;

        string directory = Path.GetDirectoryName(distributionMapPath);
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        string assetName = Path.GetFileNameWithoutExtension(distributionMapPath) + "_Bake.asset";
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(directory, assetName).Replace("\\", "/"));
        NewGrassBakedDataAsset bakeAsset = ScriptableObject.CreateInstance<NewGrassBakedDataAsset>();
        bakeAsset.name = Path.GetFileNameWithoutExtension(assetName);
        AssetDatabase.CreateAsset(bakeAsset, assetPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        bakedPlacementAssetProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<NewGrassBakedDataAsset>(assetPath);
        serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(renderer);
        return bakedPlacementAssetProperty.objectReferenceValue != null;
    }

    private static bool EnsureCombatTracerShaderAssigned(CrowdVatIndirectRenderer renderer)
    {
        if (renderer == null)
            return false;

        SerializedObject serializedRenderer = new SerializedObject(renderer);
        SerializedProperty enableCombatTracerProperty = serializedRenderer.FindProperty("_enableCombatTracer");
        SerializedProperty combatTracerShaderProperty = serializedRenderer.FindProperty("_combatTracerShader");
        if (combatTracerShaderProperty == null)
            return false;

        if (enableCombatTracerProperty != null && !enableCombatTracerProperty.boolValue)
            return false;

        if (combatTracerShaderProperty.objectReferenceValue != null)
            return false;

        Shader tracerShader = Shader.Find("Project/Crowd/VATCombatTracer");
        if (tracerShader == null)
        {
            UnityEngine.Debug.LogWarning($"RenderDoc build prepare could not find Project/Crowd/VATCombatTracer for {renderer.name}.");
            return false;
        }

        combatTracerShaderProperty.objectReferenceValue = tracerShader;
        serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static bool HasTransientBeeStructuredLoggingFailure()
    {
        string editorLogPath = GetEditorLogPath();
        if (string.IsNullOrWhiteSpace(editorLogPath) || !File.Exists(editorLogPath))
            return false;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(editorLogPath);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning($"Failed to inspect Editor.log for Bee retry detection: {exception.Message}");
            return false;
        }

        bool sawStructuredLoggingError = false;
        bool sawMissingBuildFinishedMessage = false;
        int startIndex = Math.Max(0, lines.Length - 200);
        for (int index = startIndex; index < lines.Length; index++)
        {
            string line = lines[index] ?? string.Empty;
            if (!sawStructuredLoggingError &&
                line.IndexOf($"Failed to open file \"{BeeTransientLogPath}\" for structured logging", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sawStructuredLoggingError = true;
            }

            if (!sawMissingBuildFinishedMessage &&
                line.IndexOf("Internal build system error. Read the full binlog without getting a BuildFinishedMessage.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sawMissingBuildFinishedMessage = true;
            }

            if (sawStructuredLoggingError && sawMissingBuildFinishedMessage)
                return true;
        }

        return false;
    }

    private static void RecoverFromTransientBeeStructuredLoggingFailure()
    {
        WaitForBeeBackendProcessesToExit(BeeBackendExitWaitMilliseconds);
        Thread.Sleep(BeeRetryDelayMilliseconds);
        ClearBeeTransientLogFiles();
    }

    private static void WaitForBeeBackendProcessesToExit(int timeoutMilliseconds)
    {
        Process[] processes = Process.GetProcessesByName("bee_backend");
        if (processes == null || processes.Length == 0)
            return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < processes.Length; index++)
        {
            Process process = processes[index];
            if (process == null)
                continue;

            try
            {
                if (process.HasExited)
                    continue;

                int remainingMilliseconds = Math.Max(0, timeoutMilliseconds - (int)stopwatch.ElapsedMilliseconds);
                process.WaitForExit(remainingMilliseconds);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"Failed while waiting for bee_backend exit: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void ClearBeeTransientLogFiles()
    {
        string beeDirectory = Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory(),
            "Library",
            "Bee");

        if (!Directory.Exists(beeDirectory))
            return;

        TryDeleteBeeFile(Path.Combine(beeDirectory, Path.GetFileName(BeeTransientLogPath)));
        TryDeleteBeeFile(Path.Combine(beeDirectory, "buildreport.json"));

        DeleteBeeFilesMatching(beeDirectory, "backend*.traceevents");
        DeleteBeeFilesMatching(beeDirectory, "buildreport.json*.traceevents");
    }

    private static void DeleteBeeFilesMatching(string beeDirectory, string searchPattern)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(beeDirectory, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning($"Failed to enumerate Bee files for cleanup ({searchPattern}): {exception.Message}");
            return;
        }

        for (int index = 0; index < files.Length; index++)
            TryDeleteBeeFile(files[index]);
    }

    private static void TryDeleteBeeFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        try
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
            File.Delete(filePath);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning($"Failed to delete transient Bee file '{filePath}': {exception.Message}");
        }
    }

    private static string GetEditorLogPath()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsEditor:
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Unity",
                    "Editor",
                    "Editor.log");

            case RuntimePlatform.OSXEditor:
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    "Library",
                    "Logs",
                    "Unity",
                    "Editor.log");

            case RuntimePlatform.LinuxEditor:
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    ".config",
                    "unity3d",
                    "Editor.log");

            default:
                return string.Empty;
        }
    }

    private static string BuildLaunchScript(
        string executablePath,
        string renderDocArtifactsRoot,
        string aiDebugTraceDirectory,
        string aiDebugProfilePath,
        string buildStampPath,
        string startupScenePathFile)
    {
        string renderDocCmdPath = Path.Combine(DefaultRenderDocInstallDirectory, "renderdoccmd.exe");
        string renderDocDllPath = Path.Combine(DefaultRenderDocInstallDirectory, "renderdoc.dll");
        string playerExecutableName = Path.GetFileName(executablePath);

        StringBuilder builder = new StringBuilder(2048);
        builder.AppendLine("@echo off");
        builder.AppendLine("setlocal");
        builder.AppendLine();
        builder.AppendLine("set \"SCRIPT_DIR=%~dp0\"");
        builder.AppendLine($"set \"PLAYER_EXE=%SCRIPT_DIR%{playerExecutableName}\"");
        builder.AppendLine($"set \"PLAYER_EXE_NAME={playerExecutableName}\"");
        builder.AppendLine($"set \"ARTIFACTS_ROOT={renderDocArtifactsRoot}\"");
        builder.AppendLine($"set \"AI_TRACE_DIR={aiDebugTraceDirectory}\"");
        builder.AppendLine($"set \"AI_SETTINGS_PATH={aiDebugProfilePath}\"");
        builder.AppendLine($"set \"BUILD_STAMP_PATH={buildStampPath}\"");
        builder.AppendLine($"set \"STARTUP_SCENE_PATH_FILE={startupScenePathFile}\"");
        builder.AppendLine($"set \"RENDERDOC_CMD={renderDocCmdPath}\"");
        builder.AppendLine($"set \"RENDERDOC_DLL={renderDocDllPath}\"");
        builder.AppendLine();
        builder.AppendLine("if not exist \"%PLAYER_EXE%\" (");
        builder.AppendLine("    echo Player exe not found:");
        builder.AppendLine("    echo %PLAYER_EXE%");
        builder.AppendLine("    exit /b 1");
        builder.AppendLine(")");
        builder.AppendLine();
        builder.AppendLine("if not exist \"%RENDERDOC_CMD%\" (");
        builder.AppendLine("    echo RenderDoc command line tool not found:");
        builder.AppendLine("    echo %RENDERDOC_CMD%");
        builder.AppendLine("    exit /b 1");
        builder.AppendLine(")");
        builder.AppendLine();
        builder.AppendLine("if not exist \"%ARTIFACTS_ROOT%\" mkdir \"%ARTIFACTS_ROOT%\"");
        builder.AppendLine("if not exist \"%AI_TRACE_DIR%\" mkdir \"%AI_TRACE_DIR%\"");
        builder.AppendLine();
        builder.AppendLine("echo Launching RenderDoc capture session...");
        builder.AppendLine("echo Auto repro mode:");
        builder.AppendLine("echo - First run after a fresh build records the manual path when you capture a frame.");
        builder.AppendLine("echo - Later runs replay the newest trace and export AI Debug before the hotspot.");
        builder.AppendLine("echo.");
        builder.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"& { $playerArgs = [System.Collections.Generic.List[string]]::new(); $playerArgs.Add('-force-d3d12'); $playerArgs.Add('--in-game-perf-monitor'); $playerArgs.Add('--renderdoc-artifacts-root'); $playerArgs.Add($env:ARTIFACTS_ROOT); $playerArgs.Add('--renderdoc-label'); $playerArgs.Add('external-player'); $playerArgs.Add('--renderdoc-dll'); $playerArgs.Add($env:RENDERDOC_DLL); $playerArgs.Add('--crowd-ai-repro-mode'); $playerArgs.Add('auto'); $playerArgs.Add('--crowd-ai-build-stamp-path'); $playerArgs.Add($env:BUILD_STAMP_PATH); $playerArgs.Add('--crowd-ai-trace-directory'); $playerArgs.Add($env:AI_TRACE_DIR); $playerArgs.Add('--crowd-ai-settings-path'); $playerArgs.Add($env:AI_SETTINGS_PATH); if (Test-Path -LiteralPath $env:STARTUP_SCENE_PATH_FILE) { $startupScenePath = (Get-Content -LiteralPath $env:STARTUP_SCENE_PATH_FILE -TotalCount 1 | Select-Object -First 1).Trim(); if ($startupScenePath) { $playerArgs.Add('--renderdoc-startup-scene'); $playerArgs.Add($startupScenePath) } }; & $env:RENDERDOC_CMD capture -d $env:SCRIPT_DIR $env:PLAYER_EXE -- $playerArgs; exit $LASTEXITCODE }\"");
        builder.AppendLine("set \"EXIT_CODE=%ERRORLEVEL%\"");
        builder.AppendLine();
        builder.AppendLine("timeout /t 2 >nul");
        builder.AppendLine("tasklist /FI \"IMAGENAME eq %PLAYER_EXE_NAME%\" | find /I \"%PLAYER_EXE_NAME%\" >nul");
        builder.AppendLine("if errorlevel 1 (");
        builder.AppendLine("    echo.");
        builder.AppendLine("    echo Player did not appear to start.");
        builder.AppendLine("    echo RenderDoc exit code: %EXIT_CODE%");
        builder.AppendLine("    echo.");
        builder.AppendLine("    echo If RenderDoc reported an error above, fix that first.");
        builder.AppendLine("    pause");
        builder.AppendLine("    exit /b %EXIT_CODE%");
        builder.AppendLine(")");
        builder.AppendLine();
        builder.AppendLine("echo.");
        builder.AppendLine("echo RenderDoc launch requested successfully.");
        builder.AppendLine("echo Player: %PLAYER_EXE%");
        builder.AppendLine("echo RenderDoc artifacts: %ARTIFACTS_ROOT%");
        builder.AppendLine("echo Build stamp: %BUILD_STAMP_PATH%");
        builder.AppendLine("if exist \"%STARTUP_SCENE_PATH_FILE%\" echo Startup scene: %STARTUP_SCENE_PATH_FILE%");
        builder.AppendLine("echo AI Debug trace dir: %AI_TRACE_DIR%");
        builder.AppendLine("echo AI Debug profile: %AI_SETTINGS_PATH%");
        builder.AppendLine("echo Press Shift+F8 inside the player window when you want a RenderDoc capture.");
        builder.AppendLine("timeout /t 3 >nul");
        return builder.ToString();
    }

    private static void WriteStartupScenePathFile(string startupScenePathFile, string startupScenePath)
    {
        if (string.IsNullOrWhiteSpace(startupScenePathFile))
            return;

        string normalizedScenePath = RenderDocStartupSceneUtility.NormalizeScenePath(startupScenePath);
        if (string.IsNullOrWhiteSpace(normalizedScenePath))
        {
            if (File.Exists(startupScenePathFile))
                File.Delete(startupScenePathFile);

            return;
        }

        File.WriteAllText(startupScenePathFile, normalizedScenePath, new UTF8Encoding(false));
    }

    private static string GetArgumentValue(string[] arguments, string argumentName)
    {
        if (arguments == null || arguments.Length == 0 || string.IsNullOrWhiteSpace(argumentName))
            return string.Empty;

        string prefix = argumentName + "=";
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (string.IsNullOrWhiteSpace(argument))
                continue;

            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return argument.Substring(prefix.Length).Trim('"');

            if (!string.Equals(argument, argumentName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= arguments.Length)
                return string.Empty;

            return (arguments[index + 1] ?? string.Empty).Trim('"');
        }

        return string.Empty;
    }
}
