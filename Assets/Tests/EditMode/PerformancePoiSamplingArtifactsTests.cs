using System;
using NUnit.Framework;
using UnityEngine;

public class PerformancePoiSamplingArtifactsTests
{
    [Test]
    public void BuildRunDirectoryName_IncludesTimestampSceneAndLabel()
    {
        DateTime timestamp = new DateTime(2026, 5, 20, 21, 15, 30);
        string directoryName = PerformancePoiSamplingArtifacts.BuildRunDirectoryName(timestamp, "SampleScene", "Town Center Sweep");
        Assert.That(directoryName, Is.EqualTo("20260520_211530_samplescene-town-center-sweep"));
    }

    [Test]
    public void BuildCsvSummary_EmitsPoiMetrics()
    {
        PerformancePoiRunSummary summary = new PerformancePoiRunSummary
        {
            runId = "run-001",
            runLabel = "poi-auto",
            status = "Completed",
            points = new[]
            {
                new PerformancePoiSampleResult
                {
                    poiId = "town-center",
                    poiName = "Town Center",
                    category = "Town",
                    averageFrameMs = 16.42f,
                    p95FrameMs = 22.57f,
                    averageGpuFrameMs = 12.31f,
                    averageDrawCalls = 304.0f,
                    renderDocCapturePath = @"C:\captures\capture_0001.rdc",
                    profilerAiPackageGenerated = true,
                    profilerAiPackageDirectory = @"C:\captures\profiler-ai\01_town-center",
                    profilerAiPackageManifestPath = @"C:\captures\profiler-ai\01_town-center\ai_package_manifest.json",
                    profilerCaptureSummaryPath = @"C:\captures\profiler-ai\01_town-center\capture_summary.json",
                    profilerSessionId = "20260524-101500_01_town-center",
                    profilerSessionArtifactDirectory = @"C:\captures\profiler-analysis\20260524-101500_01_town-center",
                    screenshotPath = @"C:\captures\town-center.png",
                    dataQuality = "gpu timing ok"
                }
            }
        };

        string csv = PerformancePoiSamplingArtifacts.BuildCsvSummary(summary);

        Assert.That(csv, Does.Contain("town-center"));
        Assert.That(csv, Does.Contain("16.42"));
        Assert.That(csv, Does.Contain("capture_0001.rdc"));
        Assert.That(csv, Does.Contain("profiler_ai_package_directory"));
        Assert.That(csv, Does.Contain("20260524-101500_01_town-center"));
        Assert.That(csv, Does.Contain("town-center.png"));
    }

    [Test]
    public void BuildMarkdownSummary_EmitsPoiTableAndNotes()
    {
        PerformancePoiRunSummary summary = new PerformancePoiRunSummary
        {
            runId = "run-002",
            runLabel = "poi-auto",
            status = "Completed",
            timestampLocal = "2026-05-20 21:15:30",
            sceneName = "SampleScene",
            scenePath = "Assets/Scenes/SampleScene.unity",
            unityVersion = "6000.0.46f1",
            applicationPlatform = RuntimePlatform.WindowsPlayer.ToString(),
            graphicsDeviceType = "Direct3D12",
            graphicsDeviceName = "Test GPU",
            screenWidth = 1920,
            screenHeight = 1080,
            artifactDirectory = @"C:\captures\poi-run",
            totalPoiCount = 1,
            completedPoiCount = 1,
            completedRenderDocCaptureCount = 1,
            completedProfilerAiPackageCount = 1,
            points = new[]
            {
                new PerformancePoiSampleResult
                {
                    poiId = "combat-zone",
                    poiName = "Combat Zone",
                    category = "Combat",
                    characterPosition = new Vector3(4.0f, 0.0f, 8.0f),
                    cameraPosition = new Vector3(4.0f, 2.0f, 4.0f),
                    settleFrames = 20,
                    warmupFrames = 60,
                    sampleFrames = 180,
                    averageFrameMs = 19.5f,
                    p95FrameMs = 24.2f,
                    p99FrameMs = 29.9f,
                    averageCpuFrameMs = 16.1f,
                    averageGpuFrameMs = 14.7f,
                    averageDrawCalls = 412.0f,
                    screenshotCaptured = true,
                    screenshotPath = @"C:\captures\combat-zone.png",
                    renderDocCapturePath = @"C:\captures\capture_0002.rdc",
                    profilerAiPackageGenerated = true,
                    profilerAiPackageDirectory = @"C:\captures\profiler-ai\01_combat-zone",
                    profilerSessionArtifactDirectory = @"C:\captures\profiler-analysis\20260524-101500_01_combat-zone",
                    dataQuality = "gpu timing ok"
                }
            }
        };

        string markdown = PerformancePoiSamplingArtifacts.BuildMarkdownSummary(summary);

        Assert.That(markdown, Does.Contain("| 1 | combat-zone | Combat |"));
        Assert.That(markdown, Does.Contain("RenderDoc"));
        Assert.That(markdown, Does.Contain("Profiler AI packages: 1"));
        Assert.That(markdown, Does.Contain(@"Profiler AI Package: C:\captures\profiler-ai\01_combat-zone"));
    }

    [Test]
    public void BuildProfilerAiPackageDirectoryPath_UsesStableFolderLayout()
    {
        string packageDirectory = PerformancePoiSamplingArtifacts.BuildProfilerAiPackageDirectoryPath(
            @"C:\captures\poi-run",
            3,
            "Town Center");

        Assert.That(packageDirectory, Is.EqualTo(@"C:\captures\poi-run\profiler-ai\03_town-center"));
    }
}
