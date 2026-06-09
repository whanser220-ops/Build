using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum CrowdVatAiDebugExternalPlayerMode
{
    None = 0,
    Auto = 1,
    Record = 2,
    Replay = 3
}

[Serializable]
public sealed class CrowdVatAiDebugExternalPlayerProfile
{
    public int version = 1;
    public bool enableAiDebugCapture = true;
    public float captureLeadTimeSeconds = 0.5f;
    public string phenomenon = "External player auto replay before the hotspot.";
    public string reproductionSteps = "First run records the manual path. Second run replays the latest trace.";
    public string expectedBehavior = "Replay should reach the same hotspot before AI Debug starts.";
    public CrowdVatAiDebugDiscoverySettings discoverySettings = CrowdVatAiDebugDiscoverySettings.CreateDefault();
    public List<CrowdVatAiDebugTarget> targets = new List<CrowdVatAiDebugTarget>(1);

    public static CrowdVatAiDebugExternalPlayerProfile CreateDefault()
    {
        CrowdVatAiDebugExternalPlayerProfile profile = new CrowdVatAiDebugExternalPlayerProfile();
        profile.discoverySettings.mode = CrowdVatAiDebugDiscoveryMode.ScreenRect;
        profile.discoverySettings.candidateCapacity = 128;
        profile.discoverySettings.screenRect01 = Rect.MinMaxRect(0.0f, 0.0f, 1.0f, 1.0f);
        profile.discoverySettings.hasWorldToClip = false;
        profile.targets.Add(new CrowdVatAiDebugTarget
        {
            kind = CrowdVatAiDebugTargetKind.GpuDiscovery,
            id = 0
        });
        return profile;
    }

    public CrowdVatAiDebugReplayCaptureConfig BuildCaptureConfig(string exportDirectory)
    {
        CrowdVatAiDebugDiscoverySettings settings = discoverySettings.Sanitized();
        settings.hasWorldToClip = false;
        if (settings.mode == CrowdVatAiDebugDiscoveryMode.ScreenRect)
        {
            Camera camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                settings.worldToClip = camera.projectionMatrix * camera.worldToCameraMatrix;
                settings.hasWorldToClip = true;
            }
        }

        List<CrowdVatAiDebugTarget> resolvedTargets = targets != null && targets.Count > 0
            ? new List<CrowdVatAiDebugTarget>(targets)
            : new List<CrowdVatAiDebugTarget>
            {
                new CrowdVatAiDebugTarget
                {
                    kind = CrowdVatAiDebugTargetKind.GpuDiscovery,
                    id = 0
                }
            };

        return new CrowdVatAiDebugReplayCaptureConfig
        {
            enableAiDebugCapture = enableAiDebugCapture,
            captureLeadTimeSeconds = Mathf.Max(0.0f, captureLeadTimeSeconds),
            targets = resolvedTargets,
            discoverySettings = settings,
            phenomenon = phenomenon ?? string.Empty,
            reproductionSteps = reproductionSteps ?? string.Empty,
            expectedBehavior = expectedBehavior ?? string.Empty,
            exportDirectory = exportDirectory ?? string.Empty
        };
    }
}

public struct CrowdVatAiDebugExternalPlayerSession
{
    public CrowdVatAiDebugExternalPlayerMode mode;
    public string traceDirectory;
    public string tracePath;
    public string exportDirectory;
    public string reason;

    public bool IsValid =>
        mode == CrowdVatAiDebugExternalPlayerMode.Record ||
        (mode == CrowdVatAiDebugExternalPlayerMode.Replay && !string.IsNullOrWhiteSpace(tracePath));
}

internal struct CrowdVatAiDebugExternalPlayerRuntimeOptions
{
    public CrowdVatAiDebugExternalPlayerMode mode;
    public string playerExecutablePath;
    public string buildStampPath;
    public string traceDirectory;
    public string tracePath;
    public string settingsPath;
    public string startupScenePath;
}

public static class CrowdVatAiDebugExternalPlayerBootstrap
{
    public const string ReproModeArg = "--crowd-ai-repro-mode";
    public const string BuildStampPathArg = "--crowd-ai-build-stamp-path";
    public const string TraceDirectoryArg = "--crowd-ai-trace-directory";
    public const string TracePathArg = "--crowd-ai-trace-path";
    public const string SettingsPathArg = "--crowd-ai-settings-path";

    private const string DefaultAiDebugArtifactsRelativePath = ".workspace/artifacts/ai-debug";
    private const string DefaultTraceDirectoryName = "repro-traces";
    internal const string LaunchScriptFileName = "Launch-Under-RenderDoc.cmd";
    public const string BuildStampFileName = "external-player-build-stamp.txt";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Application.isEditor)
            return;

        if (UnityEngine.Object.FindFirstObjectByType<CrowdVatAiDebugExternalPlayerRuntimeDriver>() != null)
            return;

        CrowdVatAiDebugExternalPlayerRuntimeOptions options = ParseRuntimeOptions(Environment.GetCommandLineArgs());
        if (options.mode == CrowdVatAiDebugExternalPlayerMode.None)
            return;

        CrowdVatAiDebugExternalPlayerRuntimeDriver.Ensure(options);
    }

    public static CrowdVatAiDebugExternalPlayerMode ParseMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CrowdVatAiDebugExternalPlayerMode.None;

        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":
                return CrowdVatAiDebugExternalPlayerMode.Auto;
            case "record":
                return CrowdVatAiDebugExternalPlayerMode.Record;
            case "replay":
                return CrowdVatAiDebugExternalPlayerMode.Replay;
            case "off":
            case "none":
                return CrowdVatAiDebugExternalPlayerMode.None;
            default:
                return CrowdVatAiDebugExternalPlayerMode.None;
        }
    }

    public static CrowdVatAiDebugExternalPlayerSession ResolveSession(
        CrowdVatAiDebugExternalPlayerMode requestedMode,
        string playerExecutablePath,
        string buildStampPath,
        string traceDirectory,
        string explicitTracePath)
    {
        string resolvedTraceDirectory = ResolveTraceDirectory(traceDirectory);
        string exportDirectory = ResolveExportDirectory(resolvedTraceDirectory);

        switch (requestedMode)
        {
            case CrowdVatAiDebugExternalPlayerMode.Record:
                return new CrowdVatAiDebugExternalPlayerSession
                {
                    mode = CrowdVatAiDebugExternalPlayerMode.Record,
                    traceDirectory = resolvedTraceDirectory,
                    exportDirectory = exportDirectory,
                    reason = "Explicit record mode."
                };

            case CrowdVatAiDebugExternalPlayerMode.Replay:
            {
                string resolvedTracePath = ResolveTracePath(explicitTracePath, resolvedTraceDirectory);
                if (string.IsNullOrWhiteSpace(resolvedTracePath))
                {
                    return new CrowdVatAiDebugExternalPlayerSession
                    {
                        mode = CrowdVatAiDebugExternalPlayerMode.None,
                        traceDirectory = resolvedTraceDirectory,
                        exportDirectory = exportDirectory,
                        reason = "Replay mode requested but no trace file was found."
                    };
                }

                return new CrowdVatAiDebugExternalPlayerSession
                {
                    mode = CrowdVatAiDebugExternalPlayerMode.Replay,
                    traceDirectory = resolvedTraceDirectory,
                    tracePath = resolvedTracePath,
                    exportDirectory = exportDirectory,
                    reason = "Explicit replay mode."
                };
            }

            case CrowdVatAiDebugExternalPlayerMode.Auto:
                return ResolveAutoSession(playerExecutablePath, buildStampPath, resolvedTraceDirectory, explicitTracePath);

            default:
                return new CrowdVatAiDebugExternalPlayerSession
                {
                    mode = CrowdVatAiDebugExternalPlayerMode.None,
                    traceDirectory = resolvedTraceDirectory,
                    exportDirectory = exportDirectory,
                    reason = "No external player repro mode was requested."
                };
        }
    }

    public static CrowdVatAiDebugExternalPlayerSession ResolveAutoSession(
        string playerExecutablePath,
        string buildStampPath,
        string traceDirectory,
        string explicitTracePath)
    {
        string resolvedTraceDirectory = ResolveTraceDirectory(traceDirectory);
        string exportDirectory = ResolveExportDirectory(resolvedTraceDirectory);
        string resolvedTracePath = ResolveTracePath(explicitTracePath, resolvedTraceDirectory);
        if (string.IsNullOrWhiteSpace(resolvedTracePath))
        {
            return new CrowdVatAiDebugExternalPlayerSession
            {
                mode = CrowdVatAiDebugExternalPlayerMode.Record,
                traceDirectory = resolvedTraceDirectory,
                exportDirectory = exportDirectory,
                reason = "No existing trace was found, so this run records the manual path."
            };
        }

        DateTime traceWriteTimeUtc = File.Exists(resolvedTracePath)
            ? File.GetLastWriteTimeUtc(resolvedTracePath)
            : DateTime.MinValue;
        DateTime buildFreshnessUtc = ResolveBuildFreshnessUtc(buildStampPath, playerExecutablePath);
        if (traceWriteTimeUtc < buildFreshnessUtc)
        {
            return new CrowdVatAiDebugExternalPlayerSession
            {
                mode = CrowdVatAiDebugExternalPlayerMode.Record,
                traceDirectory = resolvedTraceDirectory,
                exportDirectory = exportDirectory,
                reason = "The latest trace is older than the current build stamp, so this run records a fresh path."
            };
        }

        return new CrowdVatAiDebugExternalPlayerSession
        {
            mode = CrowdVatAiDebugExternalPlayerMode.Replay,
            traceDirectory = resolvedTraceDirectory,
            tracePath = resolvedTracePath,
            exportDirectory = exportDirectory,
            reason = "A fresh trace already exists for this build, so this run replays it and enables AI Debug."
        };
    }

    public static string FindLatestTrace(string traceDirectory)
    {
        if (string.IsNullOrWhiteSpace(traceDirectory) || !Directory.Exists(traceDirectory))
            return string.Empty;

        string[] tracePaths = Directory.GetFiles(traceDirectory, "*.json", SearchOption.TopDirectoryOnly);
        if (tracePaths == null || tracePaths.Length == 0)
            return string.Empty;

        string latestTracePath = string.Empty;
        DateTime latestWriteTimeUtc = DateTime.MinValue;
        for (int index = 0; index < tracePaths.Length; index++)
        {
            string tracePath = tracePaths[index];
            if (string.IsNullOrWhiteSpace(tracePath))
                continue;

            DateTime writeTimeUtc = File.GetLastWriteTimeUtc(tracePath);
            if (!string.IsNullOrEmpty(latestTracePath) && writeTimeUtc <= latestWriteTimeUtc)
                continue;

            latestTracePath = tracePath;
            latestWriteTimeUtc = writeTimeUtc;
        }

        return latestTracePath;
    }

    public static CrowdVatAiDebugExternalPlayerProfile LoadProfile(string settingsPath)
    {
        if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
        {
            try
            {
                string json = File.ReadAllText(settingsPath);
                CrowdVatAiDebugExternalPlayerProfile profile = JsonUtility.FromJson<CrowdVatAiDebugExternalPlayerProfile>(json);
                if (profile != null)
                    return profile;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(string.Format(
                    "[CrowdVatExternalPlayer] Failed to load AI Debug profile at {0}: {1}",
                    settingsPath,
                    exception.Message));
            }
        }

        CrowdVatAiDebugExternalPlayerProfile defaultProfile = CrowdVatAiDebugExternalPlayerProfile.CreateDefault();
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            try
            {
                string directoryPath = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                string json = JsonUtility.ToJson(defaultProfile, true);
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(string.Format(
                    "[CrowdVatExternalPlayer] Failed to write default AI Debug profile at {0}: {1}",
                    settingsPath,
                    exception.Message));
            }
        }

        return defaultProfile;
    }

    internal static CrowdVatAiDebugExternalPlayerRuntimeOptions ParseRuntimeOptions(string[] args)
    {
        CrowdVatAiDebugExternalPlayerRuntimeOptions options = default;
        options.mode = CrowdVatAiDebugExternalPlayerMode.None;
        options.playerExecutablePath = ResolvePlayerExecutablePath(args);
        options.buildStampPath = Path.Combine(
            Path.GetDirectoryName(options.playerExecutablePath) ?? RenderDocCaptureArtifacts.GetApplicationRoot(),
            BuildStampFileName);
        options.traceDirectory = GetDefaultTraceDirectory();
        options.settingsPath = Path.Combine(GetDefaultAiDebugArtifactsRoot(), "external-player-profile.json");

        if (TryGetArgumentValue(args, ReproModeArg, out string modeValue))
            options.mode = ParseMode(modeValue);

        if (TryGetArgumentValue(args, BuildStampPathArg, out string buildStampPath))
            options.buildStampPath = buildStampPath;

        if (TryGetArgumentValue(args, TraceDirectoryArg, out string traceDirectory))
            options.traceDirectory = traceDirectory;

        if (TryGetArgumentValue(args, TracePathArg, out string tracePath))
            options.tracePath = tracePath;

        if (TryGetArgumentValue(args, SettingsPathArg, out string settingsPath))
            options.settingsPath = settingsPath;

        if (TryGetArgumentValue(args, "--renderdoc-startup-scene", out string startupScenePath))
            options.startupScenePath = startupScenePath;

        return options;
    }

    private static string ResolveTraceDirectory(string traceDirectory)
    {
        string resolvedTraceDirectory = string.IsNullOrWhiteSpace(traceDirectory)
            ? GetDefaultTraceDirectory()
            : Path.GetFullPath(traceDirectory);
        return resolvedTraceDirectory;
    }

    private static string ResolveExportDirectory(string traceDirectory)
    {
        if (!string.IsNullOrWhiteSpace(traceDirectory))
        {
            DirectoryInfo directoryInfo = Directory.GetParent(traceDirectory);
            if (directoryInfo != null)
                return directoryInfo.FullName;
        }

        return GetDefaultAiDebugArtifactsRoot();
    }

    private static string ResolveTracePath(string explicitTracePath, string traceDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitTracePath))
        {
            string fullTracePath = Path.GetFullPath(explicitTracePath);
            return File.Exists(fullTracePath) ? fullTracePath : string.Empty;
        }

        return FindLatestTrace(traceDirectory);
    }

    private static string ResolvePlayerExecutablePath(string[] args)
    {
        if (args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            return Path.GetFullPath(args[0]);

        string applicationRoot = RenderDocCaptureArtifacts.GetApplicationRoot();
        return Path.Combine(applicationRoot, Application.productName + ".exe");
    }

    private static string GetDefaultAiDebugArtifactsRoot()
    {
        return Path.Combine(RenderDocCaptureArtifacts.GetApplicationRoot(), DefaultAiDebugArtifactsRelativePath);
    }

    private static string GetDefaultTraceDirectory()
    {
        return Path.Combine(GetDefaultAiDebugArtifactsRoot(), DefaultTraceDirectoryName);
    }

    public static DateTime ResolveBuildFreshnessUtc(string buildStampPath, string playerExecutablePath)
    {
        DateTime buildStampUtc = TryReadBuildStampUtc(buildStampPath);
        if (buildStampUtc > DateTime.MinValue)
            return buildStampUtc;

        if (!string.IsNullOrWhiteSpace(buildStampPath) && File.Exists(buildStampPath))
            return File.GetLastWriteTimeUtc(buildStampPath);

        if (!string.IsNullOrWhiteSpace(playerExecutablePath) && File.Exists(playerExecutablePath))
            return File.GetLastWriteTimeUtc(playerExecutablePath);

        return DateTime.MinValue;
    }

    private static DateTime TryReadBuildStampUtc(string buildStampPath)
    {
        if (string.IsNullOrWhiteSpace(buildStampPath) || !File.Exists(buildStampPath))
            return DateTime.MinValue;

        try
        {
            string rawValue = File.ReadAllText(buildStampPath).Trim();
            if (string.IsNullOrWhiteSpace(rawValue))
                return DateTime.MinValue;

            if (DateTime.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime buildStampUtc))
            {
                return buildStampUtc;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(string.Format(
                "[CrowdVatExternalPlayer] Failed to read build stamp at {0}: {1}",
                buildStampPath,
                exception.Message));
        }

        return DateTime.MinValue;
    }

    private static bool TryGetArgumentValue(string[] args, string argumentName, out string value)
    {
        value = string.Empty;
        if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(argumentName))
            return false;

        string prefix = argumentName + "=";
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (string.IsNullOrWhiteSpace(argument))
                continue;

            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = argument.Substring(prefix.Length).Trim('"');
                return !string.IsNullOrWhiteSpace(value);
            }

            if (!string.Equals(argument, argumentName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Length)
                return false;

            value = (args[index + 1] ?? string.Empty).Trim('"');
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }
}

[DisallowMultipleComponent]
internal sealed class CrowdVatAiDebugExternalPlayerRuntimeDriver : MonoBehaviour
{
    private CrowdVatAiDebugExternalPlayerRuntimeOptions _options;
    private CrowdVatAiDebugExternalPlayerProfile _profile;
    private CrowdVatAiDebugExternalPlayerSession _session;
    private CrowdVatIndirectRenderer _renderer;
    private CrowdVatAiDebugReproController _reproController;
    private RenderDocCaptureController _captureController;
    private bool _sessionStarted;
    private bool _recordingSaved;
    private bool _replayCompletionLogged;
    private bool _relaunchRequested;

    public static void Ensure(CrowdVatAiDebugExternalPlayerRuntimeOptions options)
    {
        CrowdVatAiDebugExternalPlayerRuntimeDriver driver = UnityEngine.Object.FindFirstObjectByType<CrowdVatAiDebugExternalPlayerRuntimeDriver>();
        if (driver == null)
        {
            GameObject gameObject = new GameObject("CrowdVatAiDebugExternalPlayerRuntimeDriver");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            driver = gameObject.AddComponent<CrowdVatAiDebugExternalPlayerRuntimeDriver>();
        }
        else
        {
            UnityEngine.Object.DontDestroyOnLoad(driver.gameObject);
        }

        driver.Configure(options);
    }

    private void Configure(CrowdVatAiDebugExternalPlayerRuntimeOptions options)
    {
        _options = options;
        _profile = CrowdVatAiDebugExternalPlayerBootstrap.LoadProfile(options.settingsPath);
        _session = CrowdVatAiDebugExternalPlayerBootstrap.ResolveSession(
            options.mode,
            options.playerExecutablePath,
            options.buildStampPath,
            options.traceDirectory,
            options.tracePath);
        _renderer = null;
        _reproController = null;
        _captureController = null;
        _sessionStarted = false;
        _recordingSaved = false;
        _replayCompletionLogged = false;
        enabled = _session.IsValid;

        if (_session.IsValid)
        {
            Debug.Log(string.Format(
                "[CrowdVatExternalPlayer] Mode={0}, TraceDir={1}, TracePath={2}. {3}",
                _session.mode,
                _session.traceDirectory,
                _session.tracePath,
                _session.reason));
        }
        else
        {
            Debug.LogWarning(string.Format("[CrowdVatExternalPlayer] Session disabled. {0}", _session.reason));
        }
    }

    private void Update()
    {
        if (!_session.IsValid)
            return;

        if (_session.mode == CrowdVatAiDebugExternalPlayerMode.Record)
            EnsureCaptureControllerSubscription();

        if (!_sessionStarted)
            TryStartSession();

        if (_session.mode == CrowdVatAiDebugExternalPlayerMode.Replay)
            MonitorReplayCompletion();
    }

    private void OnDisable()
    {
        UnsubscribeCaptureController();
    }

    private void OnApplicationQuit()
    {
        if (_session.mode == CrowdVatAiDebugExternalPlayerMode.Record && !_recordingSaved)
            SaveRecordedTrace("application quit fallback");
    }

    private void TryStartSession()
    {
        if (IsWaitingForStartupScene())
            return;

        _renderer = _renderer != null ? _renderer : UnityEngine.Object.FindFirstObjectByType<CrowdVatIndirectRenderer>();
        if (_renderer == null)
            return;

        _reproController = _reproController != null
            ? _reproController
            : CrowdVatAiDebugReproController.GetOrCreate(_renderer);
        if (_reproController == null)
            return;

        if (_session.mode == CrowdVatAiDebugExternalPlayerMode.Record)
        {
            if (_reproController.StartTraceRecording())
            {
                _sessionStarted = true;
                Debug.Log(string.Format(
                    "[CrowdVatExternalPlayer] Recording manual repro trace to {0}. Save happens automatically when the first RenderDoc capture completes.",
                    _session.traceDirectory));
            }

            return;
        }

        CrowdVatAiDebugReplayCaptureConfig captureConfig = _profile.BuildCaptureConfig(_session.exportDirectory);
        if (captureConfig.discoverySettings.RequiresCameraMatrix() && !captureConfig.discoverySettings.hasWorldToClip)
            return;

        if (_reproController.StartReplay(_session.tracePath, captureConfig))
        {
            _sessionStarted = true;
            Debug.Log(string.Format(
                "[CrowdVatExternalPlayer] Replaying trace {0}. AI Debug will export to {1}.",
                _session.tracePath,
                _session.exportDirectory));
        }
    }

    private void MonitorReplayCompletion()
    {
        if (!_sessionStarted || _replayCompletionLogged || _reproController == null || _reproController.IsReplayingTrace)
            return;

        _replayCompletionLogged = true;
        if (!string.IsNullOrWhiteSpace(_reproController.LastAiDebugExportPath))
        {
            Debug.Log(string.Format(
                "[CrowdVatExternalPlayer] Replay completed. AI Debug export: {0}",
                _reproController.LastAiDebugExportPath));
        }
        else
        {
            Debug.Log("[CrowdVatExternalPlayer] Replay completed.");
        }

        enabled = false;
    }

    private bool IsWaitingForStartupScene()
    {
        string requestedStartupScenePath = RenderDocStartupSceneUtility.NormalizeScenePath(_options.startupScenePath);
        if (string.IsNullOrWhiteSpace(requestedStartupScenePath))
            return false;

        Scene activeScene = SceneManager.GetActiveScene();
        return !RenderDocStartupSceneUtility.ScenePathsMatch(activeScene.path, requestedStartupScenePath);
    }

    private void EnsureCaptureControllerSubscription()
    {
        RenderDocCaptureController captureController = UnityEngine.Object.FindFirstObjectByType<RenderDocCaptureController>();
        if (captureController == null || captureController == _captureController)
            return;

        UnsubscribeCaptureController();
        _captureController = captureController;
        _captureController.CaptureCompleted += OnCaptureCompleted;
    }

    private void UnsubscribeCaptureController()
    {
        if (_captureController == null)
            return;

        _captureController.CaptureCompleted -= OnCaptureCompleted;
        _captureController = null;
    }

    private void OnCaptureCompleted(RenderDocCaptureManifest manifest)
    {
        if (_session.mode != CrowdVatAiDebugExternalPlayerMode.Record || _recordingSaved)
            return;

        SaveRecordedTrace(string.IsNullOrWhiteSpace(manifest?.captureFilePath) ? "renderdoc capture completed" : manifest.captureFilePath);
    }

    private void SaveRecordedTrace(string reason)
    {
        if (_reproController == null || !_reproController.IsRecordingTrace)
            return;

        Directory.CreateDirectory(_session.traceDirectory);
        if (_reproController.StopTraceRecordingAndSave(_session.traceDirectory))
        {
            _recordingSaved = true;
            string tracePath = _reproController.LastTracePath;
            Debug.Log(string.Format(
                "[CrowdVatExternalPlayer] Saved manual repro trace because {0}. Trace: {1}",
                reason,
                tracePath));

            if (TryRelaunchLaunchScript(tracePath))
            {
                StartCoroutine(QuitAfterRelaunch());
                return;
            }

            enabled = false;
            UnsubscribeCaptureController();
        }
    }

    private bool TryRelaunchLaunchScript(string tracePath)
    {
        if (_relaunchRequested)
            return true;

        string executableDirectory = Path.GetDirectoryName(_options.playerExecutablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
        {
            Debug.LogWarning("[CrowdVatExternalPlayer] Could not resolve the player executable directory for auto relaunch.");
            return false;
        }

        string launchScriptPath = Path.Combine(executableDirectory, CrowdVatAiDebugExternalPlayerBootstrap.LaunchScriptFileName);
        if (!File.Exists(launchScriptPath))
        {
            Debug.LogWarning(string.Format(
                "[CrowdVatExternalPlayer] Auto relaunch skipped because the launch script was not found: {0}",
                launchScriptPath));
            return false;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = launchScriptPath,
                WorkingDirectory = executableDirectory,
                UseShellExecute = true
            };

            Process process = Process.Start(startInfo);
            if (process == null)
            {
                Debug.LogWarning(string.Format(
                    "[CrowdVatExternalPlayer] Auto relaunch skipped because Process.Start returned null for {0}.",
                    launchScriptPath));
                return false;
            }

            _relaunchRequested = true;
            Debug.Log(string.Format(
                "[CrowdVatExternalPlayer] Auto relaunched {0} after saving trace {1}.",
                launchScriptPath,
                tracePath));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(string.Format(
                "[CrowdVatExternalPlayer] Failed to auto relaunch {0}: {1}",
                launchScriptPath,
                exception.Message));
            return false;
        }
    }

    private System.Collections.IEnumerator QuitAfterRelaunch()
    {
        UnsubscribeCaptureController();
        enabled = false;
        yield return new WaitForSecondsRealtime(0.5f);
        Application.Quit();
    }
}
