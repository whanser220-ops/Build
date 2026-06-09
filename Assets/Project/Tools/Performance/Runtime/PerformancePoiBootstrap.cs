using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PerformancePoiBootstrap
{
    private const string AutoRunArg = "--perf-poi-auto-run";
    private const string RunnerNameArg = "--perf-poi-runner";
    private const string RunLabelArg = "--perf-poi-run-label";
    private const string ArtifactsRootArg = "--perf-poi-artifacts-root";
    private const string TraceRootArg = "--perf-poi-trace-root";
    private const string AutoQuitArg = "--perf-poi-auto-quit";

    private static RuntimeOptions _options;
    private static bool _parsed;
    private static bool _started;

    private struct RuntimeOptions
    {
        public bool autoRun;
        public string runnerName;
        public string runLabel;
        public string artifactsRootOverride;
        public string replayTraceRootOverride;
        public bool autoQuitPlayerWhenFinished;
        public bool hasAutoQuitOverride;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeBeforeSceneLoad()
    {
        if (_parsed)
            return;

        _parsed = true;
        _options = ParseRuntimeOptions(Environment.GetCommandLineArgs());
        if (_options.autoRun)
            SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeAfterSceneLoad()
    {
        TryStartAutoRun();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryStartAutoRun();
    }

    private static void TryStartAutoRun()
    {
        if (_started || !_options.autoRun)
            return;

        PerformancePoiSampler sampler = ResolveSampler(_options.runnerName);
        if (sampler == null)
        {
            Debug.LogWarning("[PerformancePoiBootstrap] Auto-run requested, but no active PerformancePoiSampler was found in the loaded player scene.");
            return;
        }

        sampler.ApplyRuntimeOverrides(
            _options.artifactsRootOverride,
            _options.replayTraceRootOverride,
            _options.runLabel,
            _options.hasAutoQuitOverride ? _options.autoQuitPlayerWhenFinished : (bool?)null);

        Debug.Log(string.Format(
            "[PerformancePoiBootstrap] Starting POI auto-run. Sampler={0}, RunLabel={1}, ArtifactsRoot={2}, TraceRoot={3}",
            sampler.gameObject.name,
            string.IsNullOrWhiteSpace(_options.runLabel) ? "<default>" : _options.runLabel,
            string.IsNullOrWhiteSpace(_options.artifactsRootOverride) ? "<default>" : _options.artifactsRootOverride,
            string.IsNullOrWhiteSpace(_options.replayTraceRootOverride) ? "<default>" : _options.replayTraceRootOverride));

        if (!sampler.BeginRun(_options.runLabel))
        {
            Debug.LogWarning("[PerformancePoiBootstrap] Failed to start POI sampler: " + sampler.StatusMessage);
            return;
        }

        Debug.Log("[PerformancePoiBootstrap] POI auto-run started successfully.");
        _started = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private static PerformancePoiSampler ResolveSampler(string runnerName)
    {
        PerformancePoiSampler[] samplers = UnityEngine.Object.FindObjectsByType<PerformancePoiSampler>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        if (samplers == null || samplers.Length == 0)
            return null;

        if (string.IsNullOrWhiteSpace(runnerName))
            return samplers[0];

        for (int index = 0; index < samplers.Length; index++)
        {
            PerformancePoiSampler sampler = samplers[index];
            if (sampler != null && string.Equals(sampler.gameObject.name, runnerName, StringComparison.OrdinalIgnoreCase))
                return sampler;
        }

        Debug.LogWarning("[PerformancePoiBootstrap] No PerformancePoiSampler matched runner name: " + runnerName);
        return null;
    }

    private static RuntimeOptions ParseRuntimeOptions(string[] args)
    {
        RuntimeOptions options = default;
        options.autoRun = HasArgument(args, AutoRunArg);

        if (TryGetArgumentValue(args, RunnerNameArg, out string runnerName))
            options.runnerName = runnerName;

        if (TryGetArgumentValue(args, RunLabelArg, out string runLabel))
            options.runLabel = runLabel;

        if (TryGetArgumentValue(args, ArtifactsRootArg, out string artifactsRootOverride))
            options.artifactsRootOverride = artifactsRootOverride;

        if (TryGetArgumentValue(args, TraceRootArg, out string replayTraceRootOverride))
            options.replayTraceRootOverride = replayTraceRootOverride;

        if (HasArgument(args, AutoQuitArg))
        {
            options.hasAutoQuitOverride = true;
            options.autoQuitPlayerWhenFinished = true;
        }

        return options;
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
