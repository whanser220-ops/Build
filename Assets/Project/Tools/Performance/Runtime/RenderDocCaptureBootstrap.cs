using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RenderDocCaptureBootstrap
{
    private const string ArtifactsRootArg = "--renderdoc-artifacts-root";
    private const string DefaultLabelArg = "--renderdoc-label";
    private const string RenderDocDllArg = "--renderdoc-dll";
    private const string DisableScreenshotArg = "--renderdoc-disable-screenshot";
    private const string StartupSceneArg = "--renderdoc-startup-scene";

    private static bool _startupSceneOverrideApplied;

    private struct RuntimeOptions
    {
        public string artifactsRootOverride;
        public string defaultLabel;
        public string renderDocDllPathOverride;
        public string startupScenePath;
        public bool hasScreenshotOverride;
        public bool includeGameViewScreenshot;

        public bool HasAnyOverride =>
            !string.IsNullOrWhiteSpace(artifactsRootOverride) ||
            !string.IsNullOrWhiteSpace(defaultLabel) ||
            !string.IsNullOrWhiteSpace(renderDocDllPathOverride) ||
            hasScreenshotOverride;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureController()
    {
        RuntimeOptions options = ParseRuntimeOptions();
        TryApplyStartupSceneOverride(options.startupScenePath);

        RenderDocCaptureController controller = UnityEngine.Object.FindFirstObjectByType<RenderDocCaptureController>();
        if (!options.HasAnyOverride)
        {
            if (controller != null && !Application.isEditor)
                UnityEngine.Object.DontDestroyOnLoad(controller.gameObject);

            return;
        }

        if (controller == null)
        {
            GameObject gameObject = new GameObject("RenderDocCaptureController (Auto)");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            controller = gameObject.AddComponent<RenderDocCaptureController>();
        }
        else if (!Application.isEditor)
        {
            UnityEngine.Object.DontDestroyOnLoad(controller.gameObject);
        }

        controller.ApplyRuntimeOverrides(
            options.artifactsRootOverride,
            options.defaultLabel,
            options.renderDocDllPathOverride,
            options.hasScreenshotOverride ? options.includeGameViewScreenshot : (bool?)null);
    }

    private static RuntimeOptions ParseRuntimeOptions()
    {
        RuntimeOptions options = default;
        string[] args = Environment.GetCommandLineArgs();

        if (TryGetArgumentValue(args, ArtifactsRootArg, out string artifactsRootOverride))
            options.artifactsRootOverride = artifactsRootOverride;

        if (TryGetArgumentValue(args, DefaultLabelArg, out string defaultLabel))
            options.defaultLabel = defaultLabel;

        if (TryGetArgumentValue(args, RenderDocDllArg, out string renderDocDllPathOverride))
            options.renderDocDllPathOverride = renderDocDllPathOverride;

        if (TryGetArgumentValue(args, StartupSceneArg, out string startupScenePath))
            options.startupScenePath = startupScenePath;

        if (HasArgument(args, DisableScreenshotArg))
        {
            options.hasScreenshotOverride = true;
            options.includeGameViewScreenshot = false;
        }

        return options;
    }

    private static void TryApplyStartupSceneOverride(string startupScenePath)
    {
        if (_startupSceneOverrideApplied)
            return;

        _startupSceneOverrideApplied = true;
        string normalizedStartupScenePath = RenderDocStartupSceneUtility.NormalizeScenePath(startupScenePath);
        if (string.IsNullOrWhiteSpace(normalizedStartupScenePath))
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (RenderDocStartupSceneUtility.ScenePathsMatch(activeScene.path, normalizedStartupScenePath))
            return;

        int buildIndex = SceneUtility.GetBuildIndexByScenePath(normalizedStartupScenePath);
        if (buildIndex < 0)
        {
            Debug.LogWarning(string.Format(
                "[RenderDocCaptureBootstrap] Startup scene is not included in the player build: {0}",
                normalizedStartupScenePath));
            return;
        }

        SceneManager.LoadScene(buildIndex, LoadSceneMode.Single);
    }

    private static bool TryGetArgumentValue(string[] args, string argumentName, out string value)
    {
        value = string.Empty;
        if (args == null || args.Length == 0)
            return false;

        string prefix = argumentName + "=";
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (string.IsNullOrEmpty(argument))
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

    private static bool HasArgument(string[] args, string argumentName)
    {
        if (args == null || args.Length == 0)
            return false;

        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], argumentName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
