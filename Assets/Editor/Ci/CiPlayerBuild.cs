using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity6.Ci
{
    public static class CiPlayerBuild
    {
        private const string OutputArgument = "--ci-output";
        private const string DefaultWindowsOutput = ".workspace/builds/windows/Unity6-Windows-Development/Unity6.exe";

        public static void BuildWindowsDevelopment()
        {
            Stopwatch runStopwatch = Stopwatch.StartNew();
            try
            {
                CiBuildMetricsReporter.CaptureEditorContext();
                ConfigureBatchmodeLogging();
                CiBuildMetricsReporter.ReportRunStarted("Unity editor batchmode started.");
                CiBuildMetricsReporter.ReportStageStarted("unity-editor-start", "Unity editor start", "Unity editor batchmode started.");
                CiBuildMetricsReporter.ReportStageFinished("unity-editor-start", "Unity editor start", 0L, message: "Unity editor is ready.");

                string outputPath = ResolveOutputPath(Environment.GetCommandLineArgs());
                string[] scenes = GetEnabledScenePaths();

                BuildWindowsDevelopment(outputPath, scenes);
                CiBuildMetricsReporter.ReportRunFinished(
                    "success",
                    "SUCCESS",
                    runStopwatch.ElapsedMilliseconds,
                    "Unity editor build completed successfully.");
            }
            catch (Exception exception)
            {
                CiBuildMetricsReporter.ReportFailure("unity-editor", "Unity editor build", exception);
                CiBuildMetricsReporter.ReportRunFinished(
                    "failure",
                    "FAILURE",
                    runStopwatch.ElapsedMilliseconds,
                    exception.GetType().Name + ": " + exception.Message);
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                CiBuildMetricsReporter.Flush();
            }
        }

        private static void BuildWindowsDevelopment(string outputPath, string[] scenes)
        {
            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            Stopwatch targetSwitchStopwatch = Stopwatch.StartNew();
            CiBuildMetricsReporter.ReportStageStarted("build-target-switch", "Switch build target", "Switching Unity build target.");
            SwitchBuildTargetIfNeeded(BuildTarget.StandaloneWindows64);
            CiBuildMetricsReporter.ReportStageFinished(
                "build-target-switch",
                "Switch build target",
                targetSwitchStopwatch.ElapsedMilliseconds,
                message: "Unity build target is ready.");

            Debug.Log("Building YooAsset content for CI player build.");
            ProjectYooAssetBuild.BuildFromCommandLine();

            Debug.Log("Starting Windows development player build.");
            Debug.Log("Output: " + outputPath);
            Debug.Log("Scenes:\n" + string.Join("\n", scenes.Select(scene => " - " + scene)));

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            Stopwatch buildPlayerStopwatch = Stopwatch.StartNew();
            CiBuildMetricsReporter.ReportStageStarted("build-player", "BuildPipeline.BuildPlayer", "Building Windows development player.");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Windows player build failed. Result=" + summary.result +
                    ", Errors=" + summary.totalErrors +
                    ", Warnings=" + summary.totalWarnings);
            }

            Debug.Log(
                "Windows player build succeeded. Output=" + summary.outputPath +
                ", SizeBytes=" + summary.totalSize +
                ", Warnings=" + summary.totalWarnings);
            CiBuildMetricsReporter.ReportStageFinished(
                "build-player",
                "BuildPipeline.BuildPlayer",
                buildPlayerStopwatch.ElapsedMilliseconds,
                message: "Windows player build succeeded.");
        }

        private static void ConfigureBatchmodeLogging()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        }

        private static string ResolveOutputPath(string[] args)
        {
            string outputPath = GetArgumentValue(args, OutputArgument);
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = DefaultWindowsOutput;

            return Path.GetFullPath(outputPath);
        }

        private static string[] GetEnabledScenePaths()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes were found in EditorBuildSettings.");

            return scenes;
        }

        private static void SwitchBuildTargetIfNeeded(BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException("Failed to switch Unity build target to " + target + ".");
        }

        private static string GetArgumentValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length ? args[i + 1] : string.Empty;

                string prefix = name + "=";
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(prefix.Length);
            }

            return string.Empty;
        }
    }
}
