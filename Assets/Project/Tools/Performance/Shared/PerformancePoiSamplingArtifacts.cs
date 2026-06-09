using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class PerformancePoiRunSummary
{
    public string runId;
    public string runLabel;
    public string status;
    public string failureMessage;
    public string timestampLocal;
    public string timestampUtc;
    public string unityVersion;
    public string applicationPlatform;
    public string graphicsDeviceType;
    public string graphicsDeviceName;
    public string sceneName;
    public string scenePath;
    public int screenWidth;
    public int screenHeight;
    public string targetCameraName;
    public string targetCharacterName;
    public string artifactDirectory;
    public string summaryJsonPath;
    public string summaryMarkdownPath;
    public string summaryCsvPath;
    public int totalPoiCount;
    public int completedPoiCount;
    public int completedRenderDocCaptureCount;
    public int completedProfilerRawExportCount;
    public int completedProfilerAiPackageCount;
    public bool autoQuitPlayerWhenFinished;
    public PerformancePoiSampleResult[] points = Array.Empty<PerformancePoiSampleResult>();
}

[Serializable]
public sealed class PerformancePoiSampleResult
{
    public string poiKey;
    public string poiId;
    public string poiName;
    public string runMode;
    public string category;
    public string notes;
    public int sortIndex;
    public int settleFrames;
    public int warmupFrames;
    public int sampleFrames;
    public Vector3 characterPosition;
    public Vector3 characterForward;
    public Vector3 cameraPosition;
    public Vector3 cameraForward;
    public bool screenshotCaptured;
    public string screenshotPath;
    public bool renderDocCaptureRequested;
    public string renderDocCapturePath;
    public string renderDocScreenshotPath;
    public bool profilerRawExportRequested;
    public bool profilerRawExported;
    public string profilerRawPath;
    public long profilerRawFileSizeBytes;
    public bool profilerAiPackageGenerated;
    public string profilerAiPackageDirectory;
    public string profilerAiPackageManifestPath;
    public string profilerCaptureSummaryPath;
    public string profilerSessionId;
    public string profilerSessionArtifactDirectory;
    public float averageFrameMs;
    public float p95FrameMs;
    public float p99FrameMs;
    public float minFrameMs;
    public float maxFrameMs;
    public float averageCpuFrameMs;
    public float averageGpuFrameMs;
    public float averageMainThreadMs;
    public float averageRenderThreadMs;
    public float averageBatches;
    public float averageSetPassCalls;
    public float averageDrawCalls;
    public string dataQuality;
}

public static class PerformancePoiSamplingArtifacts
{
    public const string DefaultRelativeArtifactsRoot = ".workspace/artifacts/performance-poi";
    public const string DefaultRelativeReplayTraceRoot = ".workspace/artifacts/performance-poi/repro-traces";
    public const string EditorSelectedPoiKeyPrefsKey = "PerformancePoi.EditorSelectedPoiKey";
    public const string EditorSelectedPoiIdPrefsKey = "PerformancePoi.EditorSelectedPoiId";
    public const string EditorSelectedPoiScenePathPrefsKey = "PerformancePoi.EditorSelectedPoiScenePath";
    public const string SummaryJsonFileName = "poi_run_summary.json";
    public const string SummaryMarkdownFileName = "poi_run_summary.md";
    public const string SummaryCsvFileName = "poi_metrics.csv";
    public const string ScreenshotDirectoryName = "screenshots";
    public const string ProfilerRawDirectoryName = "profiler-raw";
    public const string ProfilerAiPackageDirectoryName = "profiler-ai";
    public const string ProfilerAiPackageManifestFileName = "ai_package_manifest.json";
    public const string ProfilerCaptureSummaryFileName = "capture_summary.json";

    public static string ResolveArtifactsRoot(string overridePath, string relativePath)
    {
        return RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            overridePath,
            string.IsNullOrWhiteSpace(relativePath) ? DefaultRelativeArtifactsRoot : relativePath);
    }

    public static string ResolveReplayTraceRoot(string overridePath)
    {
        return RenderDocCaptureArtifacts.ResolveArtifactsRoot(
            overridePath,
            DefaultRelativeReplayTraceRoot);
    }

    public static string BuildRunDirectoryName(DateTime localTimestamp, string sceneName, string runLabel)
    {
        string timestamp = localTimestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string sanitizedSceneName = RenderDocCaptureArtifacts.SanitizeLabel(sceneName);
        string sanitizedLabel = RenderDocCaptureArtifacts.SanitizeLabel(runLabel);

        if (string.IsNullOrWhiteSpace(sanitizedSceneName))
            sanitizedSceneName = "scene";

        if (string.IsNullOrWhiteSpace(sanitizedLabel))
            sanitizedLabel = "poi-run";

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}_{1}-{2}",
            timestamp,
            sanitizedSceneName,
            sanitizedLabel);
    }

    public static string CreateRunDirectory(string artifactsRoot, DateTime localTimestamp, string sceneName, string runLabel)
    {
        string root = ResolveArtifactsRoot(artifactsRoot, DefaultRelativeArtifactsRoot);
        Directory.CreateDirectory(root);

        string directoryPath = Path.Combine(root, BuildRunDirectoryName(localTimestamp, sceneName, runLabel));
        Directory.CreateDirectory(directoryPath);
        Directory.CreateDirectory(BuildScreenshotDirectoryPath(directoryPath));
        Directory.CreateDirectory(BuildProfilerRawDirectoryPath(directoryPath));
        Directory.CreateDirectory(BuildProfilerAiPackageRootPath(directoryPath));
        return directoryPath;
    }

    public static string BuildSummaryJsonPath(string runDirectory)
    {
        return Path.Combine(runDirectory, SummaryJsonFileName);
    }

    public static string BuildSummaryMarkdownPath(string runDirectory)
    {
        return Path.Combine(runDirectory, SummaryMarkdownFileName);
    }

    public static string BuildSummaryCsvPath(string runDirectory)
    {
        return Path.Combine(runDirectory, SummaryCsvFileName);
    }

    public static string BuildScreenshotDirectoryPath(string runDirectory)
    {
        return Path.Combine(runDirectory, ScreenshotDirectoryName);
    }

    public static string BuildProfilerRawDirectoryPath(string runDirectory)
    {
        return Path.Combine(runDirectory, ProfilerRawDirectoryName);
    }

    public static string BuildProfilerAiPackageRootPath(string runDirectory)
    {
        return Path.Combine(runDirectory, ProfilerAiPackageDirectoryName);
    }

    public static string BuildScreenshotPath(string runDirectory, int orderIndex, string poiId)
    {
        string screenshotDirectory = BuildScreenshotDirectoryPath(runDirectory);
        Directory.CreateDirectory(screenshotDirectory);

        return Path.Combine(
            screenshotDirectory,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:D2}_{1}.png",
                Mathf.Max(0, orderIndex),
                RenderDocCaptureArtifacts.SanitizeLabel(poiId)));
    }

    public static string BuildProfilerRawPath(string runDirectory, int orderIndex, string poiId)
    {
        string profilerRawDirectory = BuildProfilerRawDirectoryPath(runDirectory);
        Directory.CreateDirectory(profilerRawDirectory);

        return Path.Combine(
            profilerRawDirectory,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:D2}_{1}.raw",
                Mathf.Max(0, orderIndex),
                RenderDocCaptureArtifacts.SanitizeLabel(poiId)));
    }

    public static string BuildProfilerAiPackageDirectoryPath(string runDirectory, int orderIndex, string poiId)
    {
        string packageRoot = BuildProfilerAiPackageRootPath(runDirectory);
        Directory.CreateDirectory(packageRoot);

        return Path.Combine(
            packageRoot,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:D2}_{1}",
                Mathf.Max(0, orderIndex),
                RenderDocCaptureArtifacts.SanitizeLabel(poiId)));
    }

    public static void WriteRunSummaryJson(string path, PerformancePoiRunSummary summary)
    {
        if (summary == null)
            throw new ArgumentNullException(nameof(summary));

        File.WriteAllText(path, JsonUtility.ToJson(summary, true), Encoding.UTF8);
    }

    public static void WriteRunSummaryMarkdown(string path, PerformancePoiRunSummary summary)
    {
        File.WriteAllText(path, BuildMarkdownSummary(summary), Encoding.UTF8);
    }

    public static void WriteRunSummaryCsv(string path, PerformancePoiRunSummary summary)
    {
        File.WriteAllText(path, BuildCsvSummary(summary), Encoding.UTF8);
    }

    public static string BuildMarkdownSummary(PerformancePoiRunSummary summary)
    {
        if (summary == null)
            throw new ArgumentNullException(nameof(summary));

        StringBuilder builder = new StringBuilder(2048);
        builder.AppendLine("# 场景 POI 性能采样");
        builder.AppendLine();
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Run Id: {0}", summary.runId));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Run Label: {0}", summary.runLabel));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 状态: {0}", summary.status));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 时间(本地): {0}", summary.timestampLocal));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 场景: {0}", summary.sceneName));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Scene Path: {0}", summary.scenePath));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Unity: {0}", summary.unityVersion));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 平台: {0}", summary.applicationPlatform));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 图形后端: {0}", summary.graphicsDeviceType));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- GPU: {0}", summary.graphicsDeviceName));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 分辨率: {0} x {1}", summary.screenWidth, summary.screenHeight));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 采样点: {0}", summary.totalPoiCount));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 已完成: {0}", summary.completedPoiCount));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- RenderDoc 抓帧完成数: {0}", summary.completedRenderDocCaptureCount));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Profiler .raw exports: {0}", summary.completedProfilerRawExportCount));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Profiler AI packages: {0}", summary.completedProfilerAiPackageCount));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Artifact Directory: {0}", summary.artifactDirectory));

        if (!string.IsNullOrWhiteSpace(summary.failureMessage))
        {
            builder.AppendLine();
            builder.AppendLine("## 失败信息");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(summary.failureMessage.Trim());
            builder.AppendLine("```");
        }

        builder.AppendLine();
        builder.AppendLine("## 采样结果");
        builder.AppendLine();
        builder.AppendLine("| 序号 | POI | 运行模式 | 分类 | Avg ms | P95 ms | GPU Avg ms | DrawCalls Avg | RenderDoc | Profiler .raw | 截图 |");
        builder.AppendLine("| ---: | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |");

        if (summary.points != null)
        {
            for (int index = 0; index < summary.points.Length; index++)
            {
                PerformancePoiSampleResult point = summary.points[index];
                string renderDocCell = string.IsNullOrWhiteSpace(point.renderDocCapturePath) ? "No" : "Yes";
                string profilerRawCell = point.profilerRawExported ? "Yes" : (point.profilerRawExportRequested ? "Requested" : "No");
                string screenshotCell = point.screenshotCaptured ? "Yes" : "No";
                builder.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} |",
                    index + 1,
                    SafeMarkdown(point.poiId),
                    SafeMarkdown(point.runMode),
                    SafeMarkdown(point.category),
                    FormatMetric(point.averageFrameMs),
                    FormatMetric(point.p95FrameMs),
                    FormatMetric(point.averageGpuFrameMs),
                    FormatMetric(point.averageDrawCalls),
                    renderDocCell,
                    profilerRawCell,
                    screenshotCell));
            }
        }

        if (summary.points != null && summary.points.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## POI 详情");
            builder.AppendLine();

            for (int index = 0; index < summary.points.Length; index++)
            {
                PerformancePoiSampleResult point = summary.points[index];
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "### {0}. {1}", index + 1, point.poiId));
                builder.AppendLine();
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 名称: {0}", point.poiName));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 运行模式: {0}", point.runMode));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 分类: {0}", point.category));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 角色位置: {0}", point.characterPosition));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 相机位置: {0}", point.cameraPosition));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Settle / Warmup / Sample: {0} / {1} / {2}", point.settleFrames, point.warmupFrames, point.sampleFrames));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 平均帧时: {0} ms", FormatMetric(point.averageFrameMs)));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- P95 / P99: {0} / {1} ms", FormatMetric(point.p95FrameMs), FormatMetric(point.p99FrameMs)));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- CPU / GPU 平均: {0} / {1} ms", FormatMetric(point.averageCpuFrameMs), FormatMetric(point.averageGpuFrameMs)));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- MainThread / RenderThread: {0} / {1} ms", FormatMetric(point.averageMainThreadMs), FormatMetric(point.averageRenderThreadMs)));
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Batches / SetPass / DrawCalls: {0} / {1} / {2}", FormatMetric(point.averageBatches), FormatMetric(point.averageSetPassCalls), FormatMetric(point.averageDrawCalls)));

                if (!string.IsNullOrWhiteSpace(point.screenshotPath))
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 截图: {0}", point.screenshotPath));

                if (!string.IsNullOrWhiteSpace(point.renderDocCapturePath))
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- RenderDoc: {0}", point.renderDocCapturePath));

                if (!string.IsNullOrWhiteSpace(point.profilerRawPath))
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Profiler .raw: {0}", point.profilerRawPath));

                if (!string.IsNullOrWhiteSpace(point.profilerAiPackageDirectory))
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Profiler AI Package: {0}", point.profilerAiPackageDirectory));

                if (!string.IsNullOrWhiteSpace(point.profilerSessionArtifactDirectory))
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- Profiler Session Artifact: {0}", point.profilerSessionArtifactDirectory));

                if (!string.IsNullOrWhiteSpace(point.dataQuality))
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 数据质量: {0}", point.dataQuality));

                if (!string.IsNullOrWhiteSpace(point.notes))
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 备注: {0}", point.notes));

                builder.AppendLine();
            }
        }

        builder.AppendLine("说明: POI 现在支持 RecordedReplay 与 TurntableSweep 两种运行模式。前者回放录制的角色和相机操作，后者不依赖 trace，而是在 POI 位置按设定角速度转完一整圈。");
        return builder.ToString();
    }

    public static string BuildCsvSummary(PerformancePoiRunSummary summary)
    {
        if (summary == null)
            throw new ArgumentNullException(nameof(summary));

        StringBuilder builder = new StringBuilder(2048);
        builder.AppendLine("order,poi_id,poi_name,run_mode,category,avg_frame_ms,p95_frame_ms,p99_frame_ms,min_frame_ms,max_frame_ms,avg_cpu_frame_ms,avg_gpu_frame_ms,avg_main_thread_ms,avg_render_thread_ms,avg_batches,avg_setpass_calls,avg_draw_calls,renderdoc_capture_path,profiler_raw_requested,profiler_raw_exported,profiler_raw_path,profiler_raw_size_bytes,profiler_ai_package_generated,profiler_ai_package_directory,profiler_ai_package_manifest_path,profiler_capture_summary_path,profiler_session_id,profiler_session_artifact_directory,screenshot_path,data_quality");

        if (summary.points == null)
            return builder.ToString();

        for (int index = 0; index < summary.points.Length; index++)
        {
            PerformancePoiSampleResult point = summary.points[index];
            builder.Append(index + 1);
            builder.Append(',');
            builder.Append(EscapeCsv(point.poiId));
            builder.Append(',');
            builder.Append(EscapeCsv(point.poiName));
            builder.Append(',');
            builder.Append(EscapeCsv(point.runMode));
            builder.Append(',');
            builder.Append(EscapeCsv(point.category));
            builder.Append(',');
            builder.Append(FormatMetric(point.averageFrameMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.p95FrameMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.p99FrameMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.minFrameMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.maxFrameMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.averageCpuFrameMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.averageGpuFrameMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.averageMainThreadMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.averageRenderThreadMs));
            builder.Append(',');
            builder.Append(FormatMetric(point.averageBatches));
            builder.Append(',');
            builder.Append(FormatMetric(point.averageSetPassCalls));
            builder.Append(',');
            builder.Append(FormatMetric(point.averageDrawCalls));
            builder.Append(',');
            builder.Append(EscapeCsv(point.renderDocCapturePath));
            builder.Append(',');
            builder.Append(point.profilerRawExportRequested ? "true" : "false");
            builder.Append(',');
            builder.Append(point.profilerRawExported ? "true" : "false");
            builder.Append(',');
            builder.Append(EscapeCsv(point.profilerRawPath));
            builder.Append(',');
            builder.Append(point.profilerRawFileSizeBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(point.profilerAiPackageGenerated ? "true" : "false");
            builder.Append(',');
            builder.Append(EscapeCsv(point.profilerAiPackageDirectory));
            builder.Append(',');
            builder.Append(EscapeCsv(point.profilerAiPackageManifestPath));
            builder.Append(',');
            builder.Append(EscapeCsv(point.profilerCaptureSummaryPath));
            builder.Append(',');
            builder.Append(EscapeCsv(point.profilerSessionId));
            builder.Append(',');
            builder.Append(EscapeCsv(point.profilerSessionArtifactDirectory));
            builder.Append(',');
            builder.Append(EscapeCsv(point.screenshotPath));
            builder.Append(',');
            builder.Append(EscapeCsv(point.dataQuality));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatMetric(float value)
    {
        if (!float.IsFinite(value) || value < 0.0f)
            return string.Empty;

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string SafeMarkdown(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        return value.Replace("|", "\\|");
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        bool requiresQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
        if (!requiresQuotes)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
