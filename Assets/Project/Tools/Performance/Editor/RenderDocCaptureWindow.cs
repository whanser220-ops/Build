using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class RenderDocCaptureWindow : EditorWindow
{
    private const string WindowTitle = "RenderDoc Capture";
    private const string DefaultRenderDocInstallDirectory = @"C:\Program Files\RenderDoc";
    private const string DefaultAiDebugArtifactsDirectory = ".workspace/artifacts/ai-debug";
    private const string DefaultAiDebugTraceDirectoryName = "repro-traces";
    private const string DefaultAiDebugProfileFileName = "external-player-profile.json";
    private const string DefaultBuildStampFileName = "external-player-build-stamp.txt";
    private const string FactDatabaseRelativePath = ".workspace/artifacts/renderdoc-analysis/rdc-fact-database/rdc_capture_four_table.sqlite";
    private const string FactPostprocessResultFileName = "rdc_fact_postprocess_result.json";
    private const string AnalysisRequestPrefKey = "Qianxia.RenderDocCapture.AnalysisRequest";
    private const string ExternalStatusPrefKey = "Qianxia.RenderDocCapture.ExternalStatus";

    [SerializeField] private string _analysisRequest = "分析该帧里最主要的 GPU pass/draw 热点，并给出优先优化建议。";
    [SerializeField] private string _statusMessage = string.Empty;

    private Vector2 _scrollPosition;

    [MenuItem("Tools/Qianxia/RenderDoc Capture/Open Window")]
    public static void Open()
    {
        RenderDocCaptureWindow window = GetWindow<RenderDocCaptureWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(520.0f, 220.0f);
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent(WindowTitle);
        LoadPreferences();
    }

    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        DrawPlayerWorkflow();
        DrawCaptureArtifacts();
        EditorGUILayout.EndScrollView();
    }

    private void DrawPlayerWorkflow()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Build Fixed Player"))
                BuildFixedPlayer();

            if (GUILayout.Button("Launch Under RenderDoc"))
                LaunchUnderRenderDoc();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open Build Folder"))
                RevealPath(Path.GetDirectoryName(GetPlayerExecutablePath()));

            if (GUILayout.Button("Open Capture Root"))
                RevealPath(GetArtifactsRoot());
        }

        if (!string.IsNullOrWhiteSpace(_statusMessage))
            EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
    }

    private void DrawCaptureArtifacts()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Codex Prompt", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("Analyze What");
        _analysisRequest = EditorGUILayout.TextArea(_analysisRequest, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 2.5f));
        if (EditorGUI.EndChangeCheck())
            SavePreferences();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open Latest Capture"))
                OpenLatestCaptureDirectory();

            if (GUILayout.Button("Export ANGRY MESH Metadata"))
                ExportLatestUnityMetadata();

            if (GUILayout.Button("Copy Latest Codex Prompt"))
                CopyLatestCodexPrompt();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Export Latest Fact DB + Prefabs"))
                StartLatestFactDatabasePostprocess();
        }
    }

    private void BuildFixedPlayer()
    {
        try
        {
            RenderDocPlayerBuildUtility.BuildDefaultPlayer(revealOutputPath: true);
            SetStatus("Built fixed Player.");
        }
        catch (Exception exception)
        {
            SetStatus(string.Format("Build failed: {0}", exception.Message));
            UnityEngine.Debug.LogException(exception);
        }
    }

    private void LaunchUnderRenderDoc()
    {
        try
        {
            string renderDocCmdPath = GetRenderDocCmdPath();
            if (!File.Exists(renderDocCmdPath))
            {
                SetStatus("renderdoccmd.exe was not found.");
                return;
            }

            string playerExecutablePath = GetPlayerExecutablePath();
            if (!File.Exists(playerExecutablePath))
            {
                SetStatus("Fixed Player is missing. Build it first.");
                return;
            }

            string startupScenePath = RenderDocPlayerBuildUtility.GetActiveEditorScenePath();
            if (!string.IsNullOrWhiteSpace(startupScenePath) &&
                !RenderDocPlayerBuildUtility.IsSceneIncludedInEnabledBuildSettings(startupScenePath))
            {
                SetStatus("Current scene is not enabled in Build Settings. Rebuilding the fixed player so this scene can be launched.");
                RenderDocPlayerBuildUtility.BuildDefaultPlayer();
                playerExecutablePath = GetPlayerExecutablePath();
            }

            RenderDocPlayerBuildUtility.RefreshExternalPlayerLaunchArtifacts(
                playerExecutablePath,
                startupScenePath,
                refreshBuildStamp: false);

            string arguments = BuildRenderDocCommandArguments();
            if (!TryRunRenderDocCommand(renderDocCmdPath, arguments, 2000, out string output, out int exitCode))
            {
                SetStatus(string.Format(
                    "Failed to start RenderDoc launcher. ExitCode={0}\n{1}",
                    exitCode,
                    output));
                return;
            }

            SetStatus("RenderDoc launcher started. Auto repro mode is active: first run after a fresh build records the manual path when you capture, later runs replay the latest trace and export AI Debug before the hotspot. Press Left Shift + F8 if you still want a RenderDoc capture.");
        }
        catch (Exception exception)
        {
            SetStatus(string.Format("Launch failed: {0}", exception.Message));
            UnityEngine.Debug.LogException(exception);
        }
    }

    private void OpenLatestCaptureDirectory()
    {
        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(GetArtifactsRoot(), out RenderDocCaptureArtifactInfo artifactInfo))
        {
            SetStatus("No .rdc capture was found under the artifacts root.");
            return;
        }

        RevealPath(artifactInfo.artifactDirectory);
        SetStatus("Latest capture folder opened.");
    }

    private void CopyLatestCodexPrompt()
    {
        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(GetArtifactsRoot(), out RenderDocCaptureArtifactInfo artifactInfo))
        {
            SetStatus("No .rdc capture was found under the artifacts root.");
            return;
        }

        EditorGUIUtility.systemCopyBuffer = BuildCodexPrompt(artifactInfo);
        SetStatus("Latest Codex prompt copied.");
    }

    private void ExportLatestUnityMetadata()
    {
        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(GetArtifactsRoot(), out RenderDocCaptureArtifactInfo artifactInfo))
        {
            SetStatus("No .rdc capture was found under the artifacts root.");
            return;
        }

        try
        {
            int materialCount = RenderDocUnityMaterialMetadataExporter.ExportAngryMeshMaterials(artifactInfo.unityMaterialMetadataPath);
            int prefabCount = RenderDocUnityPrefabMetadataExporter.ExportAngryMeshPrefabsAndSignatures(
                artifactInfo.unityPrefabMetadataPath,
                artifactInfo.unityPrefabSignatureDatabasePath);
            SetStatus(string.Format(
                "Exported ANGRY MESH metadata: materials={0}, prefabs={1}. Material={2}; Prefab={3}; PrefabSQLite={4}",
                materialCount,
                prefabCount,
                artifactInfo.unityMaterialMetadataPath,
                artifactInfo.unityPrefabMetadataPath,
                artifactInfo.unityPrefabSignatureDatabasePath));
        }
        catch (Exception exception)
        {
            SetStatus(string.Format("Unity metadata export failed: {0}", exception.Message));
            UnityEngine.Debug.LogException(exception);
        }
    }

    private void StartLatestFactDatabasePostprocess()
    {
        if (!RenderDocCaptureArtifacts.TryLoadLatestArtifactInfo(GetArtifactsRoot(), out RenderDocCaptureArtifactInfo artifactInfo))
        {
            SetStatus("No .rdc capture was found under the artifacts root.");
            return;
        }

        try
        {
            int prefabCount = RenderDocUnityPrefabMetadataExporter.ExportAngryMeshPrefabsAndSignatures(
                artifactInfo.unityPrefabMetadataPath,
                artifactInfo.unityPrefabSignatureDatabasePath);

            string projectRoot = RenderDocCaptureArtifacts.GetApplicationRoot();
            string scriptPath = Path.Combine(projectRoot, "tools", "renderdoc-db", "rdc_capture_postprocess.py");
            if (!File.Exists(scriptPath))
            {
                SetStatus(string.Format("RenderDoc fact postprocess script was not found: {0}", scriptPath));
                return;
            }

            string outputPath = Path.Combine(artifactInfo.artifactDirectory, FactPostprocessResultFileName);
            string factDatabasePath = Path.Combine(projectRoot, FactDatabaseRelativePath);
            StringBuilder arguments = new StringBuilder(512);
            arguments.Append(QuoteArgument(scriptPath));
            AppendArgument(arguments, "--capture", artifactInfo.captureFilePath);
            AppendArgument(arguments, "--sqlite", factDatabasePath);
            AppendArgument(arguments, "--prefab-signatures", artifactInfo.unityPrefabSignatureDatabasePath);
            AppendArgument(arguments, "--project-name", Path.GetFileName(projectRoot));
            AppendArgument(arguments, "--output", outputPath);
            arguments.Append(" --json --pretty");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = arguments.ToString(),
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    SetStatus("Failed to start RenderDoc fact DB postprocess.");
                    return;
                }
            }

            SetStatus(string.Format(
                "Started RenderDoc fact DB postprocess. Prefab signatures refreshed: rows={0}. Result={1}",
                prefabCount,
                outputPath));
        }
        catch (Exception exception)
        {
            SetStatus(string.Format("RenderDoc fact DB postprocess failed to start: {0}", exception.Message));
            UnityEngine.Debug.LogException(exception);
        }
    }

    private string BuildCodexPrompt(RenderDocCaptureArtifactInfo artifactInfo)
    {
        return RenderDocCaptureArtifacts.BuildAnalysisPrompt(artifactInfo, _analysisRequest);
    }

    private string BuildRenderDocCommandArguments()
    {
        string playerExecutablePath = GetPlayerExecutablePath();
        string workingDirectory = Path.GetDirectoryName(playerExecutablePath) ?? RenderDocCaptureArtifacts.GetApplicationRoot();
        string playerArguments = BuildPlayerArguments();

        return string.Format(
            "capture -d {0} {1}{2}",
            QuoteArgument(workingDirectory),
            QuoteArgument(playerExecutablePath),
            string.IsNullOrWhiteSpace(playerArguments) ? string.Empty : " -- " + playerArguments);
    }

    private string BuildPlayerArguments()
    {
        StringBuilder builder = new StringBuilder(256);
        builder.Append("-force-d3d12");
        builder.Append(' ');
        builder.Append(InGamePerformanceMonitorBootstrap.EnableArg);

        AppendArgument(builder, "--renderdoc-artifacts-root", GetArtifactsRoot());
        AppendArgument(builder, "--renderdoc-label", "external-player");
        AppendArgument(builder, CrowdVatAiDebugExternalPlayerBootstrap.ReproModeArg, "auto");
        AppendArgument(builder, CrowdVatAiDebugExternalPlayerBootstrap.BuildStampPathArg, GetBuildStampPath());
        AppendArgument(builder, CrowdVatAiDebugExternalPlayerBootstrap.TraceDirectoryArg, GetAiDebugTraceDirectory());
        AppendArgument(builder, CrowdVatAiDebugExternalPlayerBootstrap.SettingsPathArg, GetAiDebugProfilePath());

        string renderDocDllPath = GetRenderDocDllPath();
        if (File.Exists(renderDocDllPath))
            AppendArgument(builder, "--renderdoc-dll", renderDocDllPath);

        string startupScenePath = RenderDocPlayerBuildUtility.GetActiveEditorScenePath();
        if (!string.IsNullOrWhiteSpace(startupScenePath))
            AppendArgument(builder, "--renderdoc-startup-scene", startupScenePath);

        return builder.ToString();
    }

    private static void AppendArgument(StringBuilder builder, string argumentName, string argumentValue)
    {
        builder.Append(' ');
        builder.Append(argumentName);
        builder.Append(' ');
        builder.Append(QuoteArgument(argumentValue));
    }

    private static string GetPlayerExecutablePath()
    {
        return RenderDocPlayerBuildUtility.GetDefaultExecutablePath();
    }

    private static string GetArtifactsRoot()
    {
        return RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            string.Empty,
            RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot);
    }

    private static string GetAiDebugRoot()
    {
        return RenderDocCaptureArtifacts.ResolveArtifactsRoot(string.Empty, DefaultAiDebugArtifactsDirectory);
    }

    private static string GetAiDebugTraceDirectory()
    {
        return Path.Combine(GetAiDebugRoot(), DefaultAiDebugTraceDirectoryName);
    }

    private static string GetAiDebugProfilePath()
    {
        return Path.Combine(GetAiDebugRoot(), DefaultAiDebugProfileFileName);
    }

    private static string GetBuildStampPath()
    {
        return Path.Combine(Path.GetDirectoryName(GetPlayerExecutablePath()) ?? RenderDocCaptureArtifacts.GetApplicationRoot(), DefaultBuildStampFileName);
    }

    private static string GetRenderDocCmdPath()
    {
        return Path.Combine(DefaultRenderDocInstallDirectory, "renderdoccmd.exe");
    }

    private static string GetRenderDocDllPath()
    {
        return Path.Combine(DefaultRenderDocInstallDirectory, "renderdoc.dll");
    }

    private void SetStatus(string message)
    {
        _statusMessage = message ?? string.Empty;
        SavePreferences();
    }

    private void LoadPreferences()
    {
        _analysisRequest = EditorPrefs.GetString(AnalysisRequestPrefKey, _analysisRequest);
        _statusMessage = EditorPrefs.GetString(ExternalStatusPrefKey, _statusMessage);
    }

    private void SavePreferences()
    {
        EditorPrefs.SetString(AnalysisRequestPrefKey, _analysisRequest ?? string.Empty);
        EditorPrefs.SetString(ExternalStatusPrefKey, _statusMessage ?? string.Empty);
    }

    private static bool TryRunRenderDocCommand(string executablePath, string arguments, int waitMilliseconds, out string output, out int exitCode)
    {
        output = string.Empty;
        exitCode = -1;

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using (Process process = Process.Start(startInfo))
        {
            if (process == null)
            {
                output = "Process.Start returned null.";
                return false;
            }

            if (!process.WaitForExit(waitMilliseconds))
                return true;

            string stdOut = process.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();
            exitCode = process.ExitCode;
            output = string.IsNullOrWhiteSpace(stdErr) ? stdOut : string.Format("{0}\n{1}", stdOut, stdErr);
            return process.ExitCode == 0;
        }
    }

    private static void RevealPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!Directory.Exists(path) && !File.Exists(path))
            return;

        EditorUtility.RevealInFinder(path);
    }

    private static string QuoteArgument(string value)
    {
        string safeValue = value ?? string.Empty;
        return string.Format("\"{0}\"", safeValue.Replace("\"", "\\\""));
    }
}
