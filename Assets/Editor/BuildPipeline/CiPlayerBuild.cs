using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Unity6.Ci
{
    public static class CiPlayerBuild
    {
        private const string OutputOptionName = "--ci-output";
        private const string SceneOptionName = "--ci-scenes";
        private const string DefaultAndroidOutputPath = ".workspace/builds/android/Unity6-Android-Development.apk";
        private const string DefaultWindowsOutputPath = ".workspace/builds/windows/Unity6-Windows-Development/Unity6.exe";

        public static void BuildAndroidDevelopment()
        {
            BuildDevelopmentPlayer(BuildTarget.Android, DefaultAndroidOutputPath);
        }

        public static void BuildWindowsDevelopment()
        {
            BuildDevelopmentPlayer(BuildTarget.StandaloneWindows64, DefaultWindowsOutputPath);
        }

        private static void BuildDevelopmentPlayer(BuildTarget buildTarget, string defaultOutputPath)
        {
            string outputPath = GetCommandLineOption(OutputOptionName, defaultOutputPath);
            string[] scenes = GetBuildScenes();
            EnsureOutputDirectory(outputPath);

            BuildTargetGroup buildTargetGroup = UnityEditor.BuildPipeline.GetBuildTargetGroup(buildTarget);
            EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);

            var options = new BuildPlayerOptions
            {
                target = buildTarget,
                scenes = scenes,
                locationPathName = outputPath,
                options = BuildOptions.Development
            };

            BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Player build failed for {buildTarget}: {summary.result} ({summary.totalErrors} errors, {summary.totalWarnings} warnings)");
            }
        }

        private static string[] GetBuildScenes()
        {
            string sceneOverride = GetCommandLineOption(SceneOptionName, string.Empty);
            if (!string.IsNullOrWhiteSpace(sceneOverride))
            {
                string[] overrideScenes = sceneOverride
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(scene => scene.Trim())
                    .Where(scene => !string.IsNullOrWhiteSpace(scene))
                    .ToArray();

                if (overrideScenes.Length == 0)
                {
                    throw new InvalidOperationException("No scenes were provided through --ci-scenes.");
                }

                return overrideScenes;
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");
            }

            return scenes;
        }

        private static void EnsureOutputDirectory(string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string GetCommandLineOption(string optionName, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, optionName, StringComparison.Ordinal) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                string prefix = optionName + "=";
                if (arg.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return arg.Substring(prefix.Length);
                }
            }

            return fallback;
        }
    }
}
