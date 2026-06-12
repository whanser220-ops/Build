using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ProjectPlayerBuild
{
    private const string DefaultAndroidDevelopmentOutput = ".workspace/builds/android/Unity6-Android-Development.apk";
    private const string DefaultWindowsDevelopmentOutput = ".workspace/builds/windows/Unity6-Windows-Development/Unity6.exe";

    public static void BuildAndroidDevelopment()
    {
        BuildAndroidDevelopmentFromCommandLine(DefaultAndroidDevelopmentOutput);
    }

    public static void BuildWindowsDevelopment()
    {
        string[] args = Environment.GetCommandLineArgs();
        string outputPath = ResolveOutputPath(args, DefaultWindowsDevelopmentOutput);
        string[] scenes = ResolveScenes(args);

        BuildDevelopmentPlayer(
            outputPath,
            BuildTarget.StandaloneWindows64,
            BuildTargetGroup.Standalone,
            scenes,
            ConfigureWindowsBuild);
    }

    internal static void BuildAndroidDevelopmentFromCommandLine(string defaultOutputPath)
    {
        string[] args = Environment.GetCommandLineArgs();
        string outputPath = ResolveOutputPath(args, defaultOutputPath);
        string[] scenes = ResolveScenes(args);

        BuildDevelopmentPlayer(
            outputPath,
            BuildTarget.Android,
            BuildTargetGroup.Android,
            scenes,
            ConfigureAndroidBuild);
    }

    private static void BuildDevelopmentPlayer(
        string outputPath,
        BuildTarget target,
        BuildTargetGroup targetGroup,
        string[] scenes,
        Action<string[]> configureTarget)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Build output path is empty.", nameof(outputPath));

        outputPath = Path.GetFullPath(outputPath);
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("Build output directory could not be resolved: " + outputPath);

        Directory.CreateDirectory(outputDirectory);
        ValidateScenesExist(scenes);

        SwitchActiveBuildTarget(targetGroup, target);
        string[] args = Environment.GetCommandLineArgs();
        configureTarget(args);

        BuildOptions options = BuildOptions.Development | BuildOptions.AllowDebugging;
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = target,
            targetGroup = targetGroup,
            options = options
        });

        if (report == null)
            throw new InvalidOperationException("BuildPipeline.BuildPlayer returned null for " + target + ".");

        BuildSummary summary = report.summary;
        Debug.Log(string.Format(
            "{0} development build result: {1}, output: {2}, size: {3} bytes, duration: {4}, errors: {5}",
            target,
            summary.result,
            outputPath,
            summary.totalSize,
            summary.totalTime,
            summary.totalErrors));

        if (summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(string.Format(
                "{0} development build failed. Result={1}, TotalErrors={2}, OutputPath={3}",
                target,
                summary.result,
                summary.totalErrors,
                outputPath));
        }
    }

    private static void ConfigureAndroidBuild(string[] args)
    {
        string sdkRoot = GetArgument(args, "-androidSdkPath");
        string ndkRoot = GetArgument(args, "-androidNdkPath");
        string jdkRoot = GetArgument(args, "-androidJdkPath");

        if (!string.IsNullOrWhiteSpace(sdkRoot))
            AndroidExternalToolsSettings.sdkRootPath = sdkRoot;

        if (!string.IsNullOrWhiteSpace(ndkRoot))
            AndroidExternalToolsSettings.ndkRootPath = ndkRoot;

        if (!string.IsNullOrWhiteSpace(jdkRoot))
            AndroidExternalToolsSettings.jdkRootPath = jdkRoot;

        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
    }

    private static void ConfigureWindowsBuild(string[] args)
    {
    }

    private static void SwitchActiveBuildTarget(BuildTargetGroup targetGroup, BuildTarget target)
    {
        if (EditorUserBuildSettings.activeBuildTarget == target)
            return;

        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
            throw new InvalidOperationException("Failed to switch active build target to " + target + ".");
    }

    private static string ResolveOutputPath(string[] args, string defaultOutputPath)
    {
        string outputPath = GetArgument(args, "--ci-output");
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = GetArgument(args, "-outputPath");

        return string.IsNullOrWhiteSpace(outputPath) ? defaultOutputPath : outputPath;
    }

    private static string[] ResolveScenes(string[] args)
    {
        string scenesArgument = GetArgument(args, "--ci-scenes");
        if (!string.IsNullOrWhiteSpace(scenesArgument))
        {
            string[] explicitScenes = scenesArgument
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(scene => scene.Trim().Replace('\\', '/'))
                .Where(scene => !string.IsNullOrWhiteSpace(scene))
                .ToArray();

            if (explicitScenes.Length == 0)
                throw new InvalidOperationException("--ci-scenes was provided, but no scene paths were parsed.");

            return explicitScenes;
        }

        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
            .Select(scene => scene.path.Replace('\\', '/'))
            .ToArray();

        if (enabledScenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");

        return enabledScenes;
    }

    private static void ValidateScenesExist(string[] scenes)
    {
        if (scenes == null || scenes.Length == 0)
            throw new InvalidOperationException("No scenes were supplied for the player build.");

        List<string> missingScenes = new List<string>();
        for (int index = 0; index < scenes.Length; index++)
        {
            string scenePath = scenes[index];
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                missingScenes.Add("<empty scene path>");
                continue;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                missingScenes.Add(scenePath);
        }

        if (missingScenes.Count == 0)
            return;

        throw new FileNotFoundException(
            "Player build scene validation failed. Missing or invalid scene assets: " +
            string.Join(", ", missingScenes));
    }

    private static string GetArgument(string[] args, string name)
    {
        if (args == null || string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string prefix = name + "=";
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg.Substring(prefix.Length).Trim('"');

            if (!string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                continue;

            return index + 1 < args.Length ? (args[index + 1] ?? string.Empty).Trim('"') : string.Empty;
        }

        return string.Empty;
    }
}
