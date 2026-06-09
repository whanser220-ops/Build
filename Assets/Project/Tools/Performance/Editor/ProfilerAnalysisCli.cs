#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

public static class ProfilerAnalysisCli
{
    private const string RequestArg = "--profiler-cli-request";
    private const string ResponseArg = "--profiler-cli-response";
    private const string SessionManifestFileName = "session_manifest.json";
    private const string FrameIndexFileName = "frame_index.csv";
    private const string ThreadIndexFileName = "thread_index.csv";
    private const string ThreadOverviewFileName = "thread_overview.json";
    private const string QueriesDirectoryName = "queries";

    public sealed class ImportedSessionArtifact
    {
        public string sessionId = string.Empty;
        public string artifactDir = string.Empty;
        public string sourcePath = string.Empty;
        public string sourceType = string.Empty;
        public int firstFrameIndex = -1;
        public int lastFrameIndex = -1;
        public int frameCount;
        public string[] availableModules = Array.Empty<string>();
        public string manifestPath = string.Empty;
        public string frameIndexPath = string.Empty;
        public string threadIndexPath = string.Empty;
        public string threadOverviewPath = string.Empty;
    }

    [Serializable]
    private sealed class CliRequest
    {
        public string command = string.Empty;
        public string sourcePath = string.Empty;
        public string artifactDir = string.Empty;
        public string sessionId = string.Empty;
        public int frameIndex = -1;
        public string thread = string.Empty;
        public string view = string.Empty;
        public int topN;
        public int itemId = -1;
        public string sortBy = string.Empty;
        public string order = string.Empty;
        public string markerName = string.Empty;
        public string markerPath = string.Empty;
    }

    [Serializable]
    private sealed class CliResponse
    {
        public bool success;
        public string error = string.Empty;
        public string command = string.Empty;
        public string sessionId = string.Empty;
        public string artifactDir = string.Empty;
        public string sourcePath = string.Empty;
        public string sourceType = string.Empty;
        public int firstFrameIndex = -1;
        public int lastFrameIndex = -1;
        public int frameCount;
        public string[] availableModules = Array.Empty<string>();
        public ThreadOverviewRecord[] threads = Array.Empty<ThreadOverviewRecord>();
        public string outputPath = string.Empty;
    }

    [Serializable]
    private sealed class SessionManifest
    {
        public string sessionId = string.Empty;
        public string sourcePath = string.Empty;
        public string sourceType = string.Empty;
        public string unityVersion = string.Empty;
        public string importedAtLocal = string.Empty;
        public string importedAtUtc = string.Empty;
        public int firstFrameIndex = -1;
        public int lastFrameIndex = -1;
        public int frameCount;
        public string[] availableModules = Array.Empty<string>();
        public ThreadOverviewRecord[] threadOverview = Array.Empty<ThreadOverviewRecord>();
        public string frameIndexPath = string.Empty;
        public string threadIndexPath = string.Empty;
        public string threadOverviewPath = string.Empty;
    }

    [Serializable]
    private sealed class ThreadOverviewRecord
    {
        public ulong threadId;
        public string threadName = string.Empty;
        public string threadGroupName = string.Empty;
        public int frameCount;
        public double averageFrameMs;
        public double maxFrameMs;
        public int maxSampleCount;
    }

    [Serializable]
    private sealed class ThreadOverviewPayload
    {
        public ThreadOverviewRecord[] threads = Array.Empty<ThreadOverviewRecord>();
    }

    private sealed class ThreadAggregate
    {
        public ulong threadId;
        public string threadName = string.Empty;
        public string threadGroupName = string.Empty;
        public int frameCount;
        public double totalFrameMs;
        public double maxFrameMs;
        public int maxSampleCount;
    }

    private sealed class FrameSummaryRecord
    {
        public int frameIndex;
        public double cpuFrameMs = -1.0;
        public double gpuFrameMs = -1.0;
        public double fps = -1.0;
        public double mainThreadMs = -1.0;
        public double renderThreadMs = -1.0;
        public long drawCalls = -1;
        public long batches = -1;
        public long gcAllocBytes = -1;
        public int threadCount;
    }

    private sealed class ThreadFrameRecord
    {
        public int frameIndex;
        public int threadIndex;
        public ulong threadId;
        public string threadName = string.Empty;
        public string threadGroupName = string.Empty;
        public double frameTimeMs;
        public int sampleCount;
        public int maxDepth;
    }

    [Serializable]
    private sealed class ThreadReference
    {
        public int threadIndex;
        public ulong threadId;
        public string threadName = string.Empty;
        public string threadGroupName = string.Empty;
        public double frameTimeMs;
        public int sampleCount;
        public int maxDepth;
    }

    [Serializable]
    private sealed class HotspotRow
    {
        public int itemId;
        public int markerId;
        public string name = string.Empty;
        public string path = string.Empty;
        public int depth;
        public double totalMs;
        public double selfMs;
        public double startMs;
        public double totalPercent;
        public double selfPercent;
        public int calls;
        public long gcAllocBytes;
        public bool hasChildren;
        public int childrenCount;
    }

    [Serializable]
    private sealed class ThreadHotspotsPayload
    {
        public int frameIndex;
        public string view = string.Empty;
        public ThreadReference thread = new ThreadReference();
        public HotspotRow[] rows = Array.Empty<HotspotRow>();
    }

    [Serializable]
    private sealed class HierarchyChildrenPayload
    {
        public int frameIndex;
        public string view = string.Empty;
        public int itemId;
        public ThreadReference thread = new ThreadReference();
        public HotspotRow[] children = Array.Empty<HotspotRow>();
    }

    [Serializable]
    private sealed class NeighborSample
    {
        public int sampleIndex;
        public string name = string.Empty;
        public double startMs;
        public double durationMs;
    }

    [Serializable]
    private sealed class MarkerContextPayload
    {
        public int frameIndex;
        public string selector = string.Empty;
        public ThreadReference thread = new ThreadReference();
        public HotspotRow match = new HotspotRow();
        public HotspotRow[] parents = Array.Empty<HotspotRow>();
        public HotspotRow[] children = Array.Empty<HotspotRow>();
        public NeighborSample[] neighborSamples = Array.Empty<NeighborSample>();
    }

    [Serializable]
    private sealed class GcAllocSample
    {
        public int sampleIndex;
        public string markerName = string.Empty;
        public double startMs;
        public double durationMs;
        public long gcAllocBytes;
        public string[] callstack = Array.Empty<string>();
    }

    [Serializable]
    private sealed class GcAllocPayload
    {
        public int frameIndex;
        public long totalGcAllocBytes;
        public ThreadReference thread = new ThreadReference();
        public GcAllocSample[] samples = Array.Empty<GcAllocSample>();
    }

    public static void Execute()
    {
        string responsePath = FindCommandLineValue(ResponseArg);
        CliResponse response = new CliResponse();

        try
        {
            string requestPath = FindCommandLineValue(RequestArg);
            if (string.IsNullOrWhiteSpace(requestPath))
                throw new InvalidOperationException("Missing --profiler-cli-request argument.");

            CliRequest request = ReadJson<CliRequest>(requestPath);
            if (request == null || string.IsNullOrWhiteSpace(request.command))
                throw new InvalidOperationException("Profiler CLI request is empty or invalid.");

            response.command = request.command;

            switch (request.command)
            {
                case "open_session":
                    ExecuteOpenSession(request, response);
                    break;
                case "export_thread_hotspots":
                    ExecuteExportThreadHotspots(request, response);
                    break;
                case "export_hierarchy_view":
                    ExecuteExportHierarchyView(request, response);
                    break;
                case "export_hierarchy_children":
                    ExecuteExportHierarchyChildren(request, response);
                    break;
                case "export_marker_context":
                    ExecuteExportMarkerContext(request, response);
                    break;
                case "export_gc_allocs":
                    ExecuteExportGcAllocs(request, response);
                    break;
                default:
                    throw new InvalidOperationException(string.Format("Unsupported profiler CLI command: {0}", request.command));
            }

            response.success = true;
        }
        catch (Exception exception)
        {
            response.success = false;
            response.error = exception.ToString();
            Debug.LogException(exception);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(responsePath))
                WriteJson(responsePath, response);

            EditorApplication.Exit(response.success ? 0 : 1);
        }
    }

    private static void ExecuteOpenSession(CliRequest request, CliResponse response)
    {
        ImportedSessionArtifact imported = ImportSessionArtifact(request.sourcePath, request.artifactDir, request.sessionId);
        SessionManifest manifest = ReadJson<SessionManifest>(imported.manifestPath);

        response.sessionId = imported.sessionId;
        response.artifactDir = imported.artifactDir;
        response.sourcePath = imported.sourcePath;
        response.sourceType = imported.sourceType;
        response.firstFrameIndex = imported.firstFrameIndex;
        response.lastFrameIndex = imported.lastFrameIndex;
        response.frameCount = imported.frameCount;
        response.availableModules = imported.availableModules ?? Array.Empty<string>();
        response.threads = BuildCompactThreadOverview(manifest.threadOverview ?? Array.Empty<ThreadOverviewRecord>(), 8);
    }

    public static string BuildSessionIdFromSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("sourcePath is required.", nameof(sourcePath));

        return BuildSessionIdFromSource(sourcePath);
    }

    public static ImportedSessionArtifact ImportSessionArtifact(string sourcePath, string artifactDir, string sessionId = null)
    {
        string resolvedSourcePath = RequireExistingFile(sourcePath, "Profiler source file");
        string resolvedArtifactDir = RequireDirectory(artifactDir, "artifactDir");
        string resolvedSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? BuildSessionIdFromSource(resolvedSourcePath)
            : sessionId.Trim();

        Directory.CreateDirectory(resolvedArtifactDir);
        Directory.CreateDirectory(Path.Combine(resolvedArtifactDir, QueriesDirectoryName));

        ProfilerDriver.ClearAllFrames();
        try
        {
            Profiler.AddFramesFromFile(resolvedSourcePath);
            return ExportLoadedSessionArtifact(resolvedSourcePath, resolvedArtifactDir, resolvedSessionId);
        }
        finally
        {
            ProfilerDriver.ClearAllFrames();
        }
    }

    private static ImportedSessionArtifact ExportLoadedSessionArtifact(string sourcePath, string artifactDir, string sessionId)
    {
        int firstFrameIndex = ProfilerDriver.firstFrameIndex;
        int lastFrameIndex = ProfilerDriver.lastFrameIndex;
        if (lastFrameIndex < firstFrameIndex)
            throw new InvalidOperationException("Profiler capture did not produce any readable frames.");

        List<FrameSummaryRecord> frames = new List<FrameSummaryRecord>(Math.Max(0, lastFrameIndex - firstFrameIndex + 1));
        List<ThreadFrameRecord> threadRows = new List<ThreadFrameRecord>(256);
        Dictionary<string, ThreadAggregate> threadAggregates = new Dictionary<string, ThreadAggregate>(StringComparer.Ordinal);

        bool hasGpuData = false;
        bool hasRenderCounters = false;
        bool hasGcData = false;

        for (int frameIndex = firstFrameIndex; frameIndex <= lastFrameIndex; frameIndex++)
        {
            List<ThreadFrameRecord> threads = ExtractThreads(frameIndex);
            if (threads.Count == 0)
                continue;

            threadRows.AddRange(threads);
            AggregateThreads(threads, threadAggregates);

            FrameSummaryRecord frame = BuildFrameSummary(frameIndex, threads);
            frames.Add(frame);

            if (frame.gpuFrameMs > 0.0)
                hasGpuData = true;
            if (frame.drawCalls >= 0 || frame.batches >= 0)
                hasRenderCounters = true;
            if (frame.gcAllocBytes > 0)
                hasGcData = true;
        }

        ThreadOverviewRecord[] threadOverview = threadAggregates
            .Values
            .OrderByDescending(item => item.maxFrameMs)
            .ThenBy(item => item.threadName, StringComparer.Ordinal)
            .Select(item => new ThreadOverviewRecord
            {
                threadId = item.threadId,
                threadName = item.threadName,
                threadGroupName = item.threadGroupName,
                frameCount = item.frameCount,
                averageFrameMs = item.frameCount > 0 ? item.totalFrameMs / item.frameCount : 0.0,
                maxFrameMs = item.maxFrameMs,
                maxSampleCount = item.maxSampleCount
            })
            .ToArray();

        List<string> modules = new List<string>(4) { "CPU Usage" };
        if (hasGpuData)
            modules.Add("GPU Usage");
        if (hasRenderCounters)
            modules.Add("Rendering");
        if (hasGcData)
            modules.Add("Memory");

        string frameIndexPath = Path.Combine(artifactDir, FrameIndexFileName);
        string threadIndexPath = Path.Combine(artifactDir, ThreadIndexFileName);
        string threadOverviewPath = Path.Combine(artifactDir, ThreadOverviewFileName);
        string manifestPath = Path.Combine(artifactDir, SessionManifestFileName);

        WriteFrameIndexCsv(frameIndexPath, frames);
        WriteThreadIndexCsv(threadIndexPath, threadRows);
        WriteJson(threadOverviewPath, new ThreadOverviewPayload { threads = threadOverview });
        WriteJson(
            manifestPath,
            new SessionManifest
            {
                sessionId = sessionId,
                sourcePath = sourcePath,
                sourceType = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
                unityVersion = Application.unityVersion,
                importedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                importedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                firstFrameIndex = firstFrameIndex,
                lastFrameIndex = lastFrameIndex,
                frameCount = frames.Count,
                availableModules = modules.ToArray(),
                threadOverview = threadOverview,
                frameIndexPath = frameIndexPath,
                threadIndexPath = threadIndexPath,
                threadOverviewPath = threadOverviewPath
            });

        return new ImportedSessionArtifact
        {
            sessionId = sessionId,
            artifactDir = artifactDir,
            sourcePath = sourcePath,
            sourceType = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
            firstFrameIndex = firstFrameIndex,
            lastFrameIndex = lastFrameIndex,
            frameCount = frames.Count,
            availableModules = modules.ToArray(),
            manifestPath = manifestPath,
            frameIndexPath = frameIndexPath,
            threadIndexPath = threadIndexPath,
            threadOverviewPath = threadOverviewPath
        };
    }

    private static ThreadOverviewRecord[] BuildCompactThreadOverview(ThreadOverviewRecord[] threads, int limit)
    {
        return threads
            .Where(item =>
                item.maxSampleCount > 1 ||
                string.Equals(item.threadName, "Main Thread", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.threadName, "Render Thread", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.threadGroupName, "D3D12 Task", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item =>
                string.Equals(item.threadName, "Main Thread", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.threadName, "Render Thread", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0)
            .ThenByDescending(item => item.maxSampleCount)
            .ThenByDescending(item => item.maxFrameMs)
            .Take(limit)
            .ToArray();
    }

    private static void ExecuteExportThreadHotspots(CliRequest request, CliResponse response)
    {
        string sourcePath = RequireExistingFile(request.sourcePath, "Profiler source file");
        string artifactDir = RequireDirectory(request.artifactDir, "artifactDir");
        if (request.frameIndex < 0)
            throw new InvalidOperationException("frameIndex must be >= 0.");

        string sessionId = string.IsNullOrWhiteSpace(request.sessionId) ? BuildSessionIdFromSource(sourcePath) : request.sessionId.Trim();
        Profiler.AddFramesFromFile(sourcePath);

        ThreadResolution thread = ResolveThread(request.frameIndex, request.thread);
        HierarchyFrameDataView.ViewModes viewMode = ResolveViewMode(request.view);

        using (HierarchyFrameDataView frameData = ProfilerDriver.GetHierarchyFrameDataView(
                   request.frameIndex,
                   thread.reference.threadIndex,
                   viewMode,
                   HierarchyFrameDataView.columnSelfTime,
                   false))
        {
            if (!frameData.valid)
                throw new InvalidOperationException("Unable to open hierarchy frame data view.");

            List<HotspotRow> rows = ExtractHotspotRows(frameData, Math.Max(1, request.topN));
            ThreadHotspotsPayload payload = new ThreadHotspotsPayload
            {
                frameIndex = request.frameIndex,
                view = request.view,
                thread = thread.reference,
                rows = rows.ToArray()
            };

            string outputPath = BuildQueryPath(
                artifactDir,
                "thread_hotspots",
                sessionId,
                request.frameIndex,
                thread.reference.threadName,
                request.view,
                "self-desc-v1",
                request.topN);
            WriteJson(outputPath, payload);
            response.outputPath = outputPath;
            response.sessionId = sessionId;
            response.artifactDir = artifactDir;
            response.sourcePath = sourcePath;
        }
    }

    private static void ExecuteExportHierarchyView(CliRequest request, CliResponse response)
    {
        string sourcePath = RequireExistingFile(request.sourcePath, "Profiler source file");
        string artifactDir = RequireDirectory(request.artifactDir, "artifactDir");
        if (request.frameIndex < 0)
            throw new InvalidOperationException("frameIndex must be >= 0.");

        string sessionId = string.IsNullOrWhiteSpace(request.sessionId) ? BuildSessionIdFromSource(sourcePath) : request.sessionId.Trim();
        Profiler.AddFramesFromFile(sourcePath);

        ThreadResolution thread = ResolveThread(request.frameIndex, request.thread);
        HierarchyFrameDataView.ViewModes viewMode = ResolveViewMode(request.view);

        using (HierarchyFrameDataView frameData = ProfilerDriver.GetHierarchyFrameDataView(
                   request.frameIndex,
                   thread.reference.threadIndex,
                   viewMode,
                   HierarchyFrameDataView.columnTotalTime,
                   false))
        {
            if (!frameData.valid)
                throw new InvalidOperationException("Unable to open hierarchy frame data view.");

            List<HotspotRow> rows = new List<HotspotRow>(1024);
            TraverseHierarchy(frameData, frameData.GetRootItemID(), rows);
            ThreadHotspotsPayload payload = new ThreadHotspotsPayload
            {
                frameIndex = request.frameIndex,
                view = request.view,
                thread = thread.reference,
                rows = rows.ToArray()
            };

            string outputPath = BuildQueryPath(
                artifactDir,
                "hierarchy_view",
                sessionId,
                request.frameIndex,
                thread.reference.threadName,
                request.view,
                "preorder-v1");
            WriteJson(outputPath, payload);
            response.outputPath = outputPath;
            response.sessionId = sessionId;
            response.artifactDir = artifactDir;
            response.sourcePath = sourcePath;
        }
    }

    private static void ExecuteExportHierarchyChildren(CliRequest request, CliResponse response)
    {
        string sourcePath = RequireExistingFile(request.sourcePath, "Profiler source file");
        string artifactDir = RequireDirectory(request.artifactDir, "artifactDir");
        if (request.frameIndex < 0)
            throw new InvalidOperationException("frameIndex must be >= 0.");
        if (request.itemId < 0)
            throw new InvalidOperationException("Hierarchy children export requires itemId >= 0.");

        string sessionId = string.IsNullOrWhiteSpace(request.sessionId) ? BuildSessionIdFromSource(sourcePath) : request.sessionId.Trim();
        Profiler.AddFramesFromFile(sourcePath);

        ThreadResolution thread = ResolveThread(request.frameIndex, request.thread);
        HierarchyFrameDataView.ViewModes viewMode = ResolveViewMode(request.view);

        using (HierarchyFrameDataView frameData = ProfilerDriver.GetHierarchyFrameDataView(
                   request.frameIndex,
                   thread.reference.threadIndex,
                   viewMode,
                   HierarchyFrameDataView.columnTotalTime,
                   false))
        {
            if (!frameData.valid)
                throw new InvalidOperationException("Unable to open hierarchy frame data view.");

            List<int> childIds = new List<int>();
            frameData.GetItemChildren(request.itemId, childIds);
            List<HotspotRow> children = childIds
                .Select(id => BuildHotspotRow(frameData, id))
                .Where(row => row != null)
                .ToList();

            children = SortHotspotRows(children, request.sortBy, request.order)
                .Take(Math.Max(1, request.topN))
                .ToList();

            HierarchyChildrenPayload payload = new HierarchyChildrenPayload
            {
                frameIndex = request.frameIndex,
                view = request.view,
                itemId = request.itemId,
                thread = thread.reference,
                children = children.ToArray()
            };

            string outputPath = BuildQueryPath(
                artifactDir,
                "hierarchy_children",
                sessionId,
                request.frameIndex,
                thread.reference.threadName,
                request.view,
                request.itemId,
                request.sortBy,
                request.order,
                request.topN);
            WriteJson(outputPath, payload);
            response.outputPath = outputPath;
            response.sessionId = sessionId;
            response.artifactDir = artifactDir;
            response.sourcePath = sourcePath;
        }
    }

    private static void ExecuteExportMarkerContext(CliRequest request, CliResponse response)
    {
        string sourcePath = RequireExistingFile(request.sourcePath, "Profiler source file");
        string artifactDir = RequireDirectory(request.artifactDir, "artifactDir");
        if (request.frameIndex < 0)
            throw new InvalidOperationException("frameIndex must be >= 0.");

        string selector = !string.IsNullOrWhiteSpace(request.markerPath) ? request.markerPath : request.markerName;
        if (string.IsNullOrWhiteSpace(selector))
            throw new InvalidOperationException("markerPath or markerName must be provided.");

        string sessionId = string.IsNullOrWhiteSpace(request.sessionId) ? BuildSessionIdFromSource(sourcePath) : request.sessionId.Trim();
        Profiler.AddFramesFromFile(sourcePath);

        ThreadResolution thread = ResolveThread(request.frameIndex, request.thread);
        using (RawFrameDataView rawFrameData = ProfilerDriver.GetRawFrameDataView(request.frameIndex, thread.reference.threadIndex))
        using (HierarchyFrameDataView hierarchy = ProfilerDriver.GetHierarchyFrameDataView(
                   request.frameIndex,
                   thread.reference.threadIndex,
                   HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                   HierarchyFrameDataView.columnTotalTime,
                   false))
        {
            if (!rawFrameData.valid || !hierarchy.valid)
                throw new InvalidOperationException("Unable to open profiler frame data for marker context.");

            MarkerMatch match = FindBestMarkerMatch(hierarchy, selector, request.markerPath);
            if (match.itemId == HierarchyFrameDataView.invalidSampleId)
                throw new InvalidOperationException(string.Format("Marker not found in frame {0}: {1}", request.frameIndex, selector));

            List<int> ancestorIds = new List<int>();
            hierarchy.GetItemAncestors(match.itemId, ancestorIds);
            List<HotspotRow> parents = ancestorIds
                .Select(id => BuildHotspotRow(hierarchy, id))
                .Where(row => row != null)
                .ToList();

            List<int> childIds = new List<int>();
            hierarchy.GetItemChildren(match.itemId, childIds);
            List<HotspotRow> children = childIds
                .Select(id => BuildHotspotRow(hierarchy, id))
                .Where(row => row != null)
                .OrderByDescending(row => row.totalMs)
                .Take(Math.Max(1, request.topN))
                .ToList();

            NeighborSample[] neighbors = BuildNeighborSamples(rawFrameData, match.name, match.startMs, 3);

            MarkerContextPayload payload = new MarkerContextPayload
            {
                frameIndex = request.frameIndex,
                selector = selector,
                thread = thread.reference,
                match = BuildHotspotRow(hierarchy, match.itemId),
                parents = parents.ToArray(),
                children = children.ToArray(),
                neighborSamples = neighbors
            };

            string outputPath = BuildQueryPath(
                artifactDir,
                "marker_context",
                sessionId,
                request.frameIndex,
                thread.reference.threadName,
                selector,
                request.topN);
            WriteJson(outputPath, payload);
            response.outputPath = outputPath;
            response.sessionId = sessionId;
            response.artifactDir = artifactDir;
            response.sourcePath = sourcePath;
        }
    }

    private static void ExecuteExportGcAllocs(CliRequest request, CliResponse response)
    {
        string sourcePath = RequireExistingFile(request.sourcePath, "Profiler source file");
        string artifactDir = RequireDirectory(request.artifactDir, "artifactDir");
        if (request.frameIndex < 0)
            throw new InvalidOperationException("frameIndex must be >= 0.");

        string sessionId = string.IsNullOrWhiteSpace(request.sessionId) ? BuildSessionIdFromSource(sourcePath) : request.sessionId.Trim();
        Profiler.AddFramesFromFile(sourcePath);

        ThreadResolution thread = ResolveThread(request.frameIndex, request.thread);
        using (RawFrameDataView rawFrameData = ProfilerDriver.GetRawFrameDataView(request.frameIndex, thread.reference.threadIndex))
        {
            if (!rawFrameData.valid)
                throw new InvalidOperationException("Unable to open raw frame data view for GC.Alloc export.");

            List<GcAllocSample> samples = ExtractGcAllocSamples(rawFrameData, Math.Max(1, request.topN), out long totalGcAllocBytes);
            GcAllocPayload payload = new GcAllocPayload
            {
                frameIndex = request.frameIndex,
                totalGcAllocBytes = totalGcAllocBytes,
                thread = thread.reference,
                samples = samples.ToArray()
            };

            string outputPath = BuildQueryPath(
                artifactDir,
                "gc_allocs",
                sessionId,
                request.frameIndex,
                thread.reference.threadName,
                request.topN.ToString(CultureInfo.InvariantCulture));
            WriteJson(outputPath, payload);
            response.outputPath = outputPath;
            response.sessionId = sessionId;
            response.artifactDir = artifactDir;
            response.sourcePath = sourcePath;
        }
    }

    private static List<ThreadFrameRecord> ExtractThreads(int frameIndex)
    {
        List<ThreadFrameRecord> threads = new List<ThreadFrameRecord>(8);
        for (int threadIndex = 0; ; threadIndex++)
        {
            using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (!frameData.valid)
                    break;

                threads.Add(new ThreadFrameRecord
                {
                    frameIndex = frameIndex,
                    threadIndex = frameData.threadIndex,
                    threadId = frameData.threadId,
                    threadName = frameData.threadName ?? string.Empty,
                    threadGroupName = frameData.threadGroupName ?? string.Empty,
                    frameTimeMs = frameData.frameTimeMs,
                    sampleCount = frameData.sampleCount,
                    maxDepth = frameData.maxDepth
                });
            }
        }

        return threads;
    }

    private static FrameSummaryRecord BuildFrameSummary(int frameIndex, List<ThreadFrameRecord> threads)
    {
        ThreadFrameRecord firstThread = threads[0];
        FrameSummaryRecord summary = new FrameSummaryRecord
        {
            frameIndex = frameIndex,
            cpuFrameMs = firstThread.frameTimeMs,
            threadCount = threads.Count
        };

        using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(frameIndex, firstThread.threadIndex))
        {
            if (frameData.valid)
            {
                summary.gpuFrameMs = frameData.frameGpuTimeMs;
                summary.fps = frameData.frameFps;
            }
        }

        ThreadFrameRecord mainThread = threads.FirstOrDefault(item => string.Equals(item.threadName, "Main Thread", StringComparison.OrdinalIgnoreCase));
        if (mainThread != null)
            summary.mainThreadMs = mainThread.frameTimeMs;

        ThreadFrameRecord renderThread = threads.FirstOrDefault(item => item.threadName.IndexOf("Render Thread", StringComparison.OrdinalIgnoreCase) >= 0);
        if (renderThread != null)
            summary.renderThreadMs = renderThread.frameTimeMs;

        summary.drawCalls = TryGetCounterValueAsLong(frameIndex, "Draw Calls Count");
        summary.batches = TryGetCounterValueAsLong(frameIndex, "Batches Count");
        summary.gcAllocBytes = SumGcAllocForFrame(frameIndex);
        return summary;
    }

    private static void AggregateThreads(List<ThreadFrameRecord> threads, Dictionary<string, ThreadAggregate> aggregates)
    {
        for (int index = 0; index < threads.Count; index++)
        {
            ThreadFrameRecord thread = threads[index];
            string key = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}",
                thread.threadId,
                thread.threadName,
                thread.threadGroupName);

            if (!aggregates.TryGetValue(key, out ThreadAggregate aggregate))
            {
                aggregate = new ThreadAggregate
                {
                    threadId = thread.threadId,
                    threadName = thread.threadName,
                    threadGroupName = thread.threadGroupName
                };
                aggregates.Add(key, aggregate);
            }

            aggregate.frameCount++;
            aggregate.totalFrameMs += thread.frameTimeMs;
            aggregate.maxFrameMs = Math.Max(aggregate.maxFrameMs, thread.frameTimeMs);
            aggregate.maxSampleCount = Math.Max(aggregate.maxSampleCount, thread.sampleCount);
        }
    }

    private static long TryGetCounterValueAsLong(int frameIndex, string counterName)
    {
        for (int threadIndex = 0; ; threadIndex++)
        {
            using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (!frameData.valid)
                    break;

                int markerId = frameData.GetMarkerId(counterName);
                if (markerId == FrameDataView.invalidMarkerId)
                    continue;
                if (!frameData.HasCounterValue(markerId))
                    continue;

                return frameData.GetCounterValueAsLong(markerId);
            }
        }

        return -1;
    }

    private static long SumGcAllocForFrame(int frameIndex)
    {
        long totalGcAllocBytes = 0L;
        int gcAllocMarkerId = FrameDataView.invalidMarkerId;

        for (int threadIndex = 0; ; threadIndex++)
        {
            using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
            {
                if (!frameData.valid)
                    break;

                if (gcAllocMarkerId == FrameDataView.invalidMarkerId)
                    gcAllocMarkerId = frameData.GetMarkerId("GC.Alloc");

                if (gcAllocMarkerId == FrameDataView.invalidMarkerId)
                    continue;

                for (int sampleIndex = 0; sampleIndex < frameData.sampleCount; sampleIndex++)
                {
                    if (frameData.GetSampleMarkerId(sampleIndex) != gcAllocMarkerId)
                        continue;

                    totalGcAllocBytes += frameData.GetSampleMetadataAsLong(sampleIndex, 0);
                }
            }
        }

        return totalGcAllocBytes;
    }

    private static ThreadResolution ResolveThread(int frameIndex, string selector)
    {
        List<ThreadFrameRecord> threads = ExtractThreads(frameIndex);
        if (threads.Count == 0)
            throw new InvalidOperationException(string.Format("No threads found for frame {0}.", frameIndex));

        if (string.IsNullOrWhiteSpace(selector))
        {
            ThreadFrameRecord defaultThread = threads.FirstOrDefault(item => string.Equals(item.threadName, "Main Thread", StringComparison.OrdinalIgnoreCase))
                ?? threads[0];
            return new ThreadResolution(defaultThread);
        }

        string trimmed = selector.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
        {
            ThreadFrameRecord byIndex = threads.FirstOrDefault(item => item.threadIndex == numeric);
            if (byIndex != null)
                return new ThreadResolution(byIndex);

            ThreadFrameRecord byId = numeric >= 0 ? threads.FirstOrDefault(item => item.threadId == (ulong)numeric) : null;
            if (byId != null)
                return new ThreadResolution(byId);
        }

        ThreadFrameRecord exact = threads.FirstOrDefault(item =>
            string.Equals(item.threadName, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.threadGroupName, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return new ThreadResolution(exact);

        ThreadFrameRecord partial = threads.FirstOrDefault(item =>
            item.threadName.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0 ||
            item.threadGroupName.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0);
        if (partial != null)
            return new ThreadResolution(partial);

        throw new InvalidOperationException(string.Format("Thread selector did not match frame {0}: {1}", frameIndex, selector));
    }

    private static HierarchyFrameDataView.ViewModes ResolveViewMode(string view)
    {
        string normalized = string.IsNullOrWhiteSpace(view) ? "merged" : view.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "default":
            case "raw":
            case "hierarchy":
                return HierarchyFrameDataView.ViewModes.Default;
            case "merged":
            case "merge":
            case "aggregated":
                return HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName;
            default:
                return HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName;
        }
    }

    private static List<HotspotRow> ExtractHotspotRows(HierarchyFrameDataView frameData, int topN)
    {
        List<HotspotRow> rows = new List<HotspotRow>(128);
        TraverseHierarchy(frameData, frameData.GetRootItemID(), rows);
        return rows
            .Where(item => item.itemId != frameData.GetRootItemID())
            .OrderByDescending(item => item.selfMs)
            .ThenByDescending(item => item.totalMs)
            .Take(topN)
            .ToList();
    }

    private static IEnumerable<HotspotRow> SortHotspotRows(List<HotspotRow> rows, string sortBy, string order)
    {
        string normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "total_ms" : sortBy.Trim().ToLowerInvariant();
        bool descending = !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase);

        Func<HotspotRow, double> keySelector;
        switch (normalizedSort)
        {
            case "self_ms":
            case "self":
                keySelector = row => row.selfMs;
                break;
            case "calls":
                keySelector = row => row.calls;
                break;
            case "gc_alloc_bytes":
            case "gc":
                keySelector = row => row.gcAllocBytes;
                break;
            case "children_count":
                keySelector = row => row.childrenCount;
                break;
            case "total_ms":
            case "total":
            default:
                keySelector = row => row.totalMs;
                break;
        }

        return descending
            ? rows.OrderByDescending(keySelector).ThenBy(row => row.name, StringComparer.Ordinal)
            : rows.OrderBy(keySelector).ThenBy(row => row.name, StringComparer.Ordinal);
    }

    private static void TraverseHierarchy(HierarchyFrameDataView frameData, int itemId, List<HotspotRow> output)
    {
        HotspotRow row = BuildHotspotRow(frameData, itemId);
        if (row != null)
            output.Add(row);

        List<int> children = new List<int>();
        frameData.GetItemChildren(itemId, children);
        for (int index = 0; index < children.Count; index++)
            TraverseHierarchy(frameData, children[index], output);
    }

    private static HotspotRow BuildHotspotRow(HierarchyFrameDataView frameData, int itemId)
    {
        if (itemId == HierarchyFrameDataView.invalidSampleId)
            return null;

        List<int> children = new List<int>();
        frameData.GetItemChildren(itemId, children);
        return new HotspotRow
        {
            itemId = itemId,
            markerId = frameData.GetItemMarkerID(itemId),
            name = frameData.GetItemName(itemId) ?? string.Empty,
            path = frameData.GetItemPath(itemId) ?? string.Empty,
            depth = frameData.GetItemDepth(itemId),
            totalMs = frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnTotalTime),
            selfMs = frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnSelfTime),
            startMs = frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnStartTime),
            totalPercent = frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnTotalPercent),
            selfPercent = frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnSelfPercent),
            calls = (int)Math.Round(frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnCalls)),
            gcAllocBytes = (long)Math.Round(frameData.GetItemColumnDataAsDouble(itemId, HierarchyFrameDataView.columnGcMemory)),
            hasChildren = children.Count > 0,
            childrenCount = children.Count
        };
    }

    private static MarkerMatch FindBestMarkerMatch(HierarchyFrameDataView frameData, string selector, string requestedPath)
    {
        string normalizedSelector = selector.Trim();
        List<MarkerMatch> matches = new List<MarkerMatch>(8);
        CollectMarkerMatches(frameData, frameData.GetRootItemID(), normalizedSelector, requestedPath, matches);
        if (matches.Count == 0)
            return default;

        return matches
            .OrderByDescending(item => item.score)
            .ThenByDescending(item => item.totalMs)
            .First();
    }

    private static void CollectMarkerMatches(
        HierarchyFrameDataView frameData,
        int itemId,
        string selector,
        string requestedPath,
        List<MarkerMatch> matches)
    {
        HotspotRow row = BuildHotspotRow(frameData, itemId);
        if (row != null)
        {
            int score = 0;
            if (!string.IsNullOrWhiteSpace(requestedPath) && string.Equals(row.path, requestedPath, StringComparison.OrdinalIgnoreCase))
                score = 400;
            else if (string.Equals(row.name, selector, StringComparison.OrdinalIgnoreCase))
                score = 300;
            else if (row.path.IndexOf(selector, StringComparison.OrdinalIgnoreCase) >= 0)
                score = 200;
            else if (row.name.IndexOf(selector, StringComparison.OrdinalIgnoreCase) >= 0)
                score = 100;

            if (score > 0)
            {
                matches.Add(new MarkerMatch
                {
                    itemId = row.itemId,
                    name = row.name,
                    path = row.path,
                    startMs = row.startMs,
                    totalMs = row.totalMs,
                    score = score
                });
            }
        }

        List<int> children = new List<int>();
        frameData.GetItemChildren(itemId, children);
        for (int index = 0; index < children.Count; index++)
            CollectMarkerMatches(frameData, children[index], selector, requestedPath, matches);
    }

    private static NeighborSample[] BuildNeighborSamples(RawFrameDataView frameData, string markerName, double markerStartMs, int radius)
    {
        int closestSampleIndex = RawFrameDataView.invalidSampleIndex;
        double closestDistance = double.MaxValue;

        for (int sampleIndex = 0; sampleIndex < frameData.sampleCount; sampleIndex++)
        {
            string sampleName = frameData.GetSampleName(sampleIndex);
            if (!string.Equals(sampleName, markerName, StringComparison.Ordinal))
                continue;

            double startMs = frameData.GetSampleStartTimeMs(sampleIndex);
            double distance = Math.Abs(startMs - markerStartMs);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestSampleIndex = sampleIndex;
            }
        }

        if (closestSampleIndex == RawFrameDataView.invalidSampleIndex)
            return Array.Empty<NeighborSample>();

        List<NeighborSample> neighbors = new List<NeighborSample>(radius * 2 + 1);
        int startIndex = Math.Max(0, closestSampleIndex - radius);
        int endIndex = Math.Min(frameData.sampleCount - 1, closestSampleIndex + radius);

        for (int sampleIndex = startIndex; sampleIndex <= endIndex; sampleIndex++)
        {
            neighbors.Add(new NeighborSample
            {
                sampleIndex = sampleIndex,
                name = frameData.GetSampleName(sampleIndex) ?? string.Empty,
                startMs = frameData.GetSampleStartTimeMs(sampleIndex),
                durationMs = frameData.GetSampleTimeMs(sampleIndex)
            });
        }

        return neighbors.ToArray();
    }

    private static List<GcAllocSample> ExtractGcAllocSamples(RawFrameDataView frameData, int topN, out long totalGcAllocBytes)
    {
        totalGcAllocBytes = 0L;
        List<GcAllocSample> samples = new List<GcAllocSample>(32);

        int gcAllocMarkerId = frameData.GetMarkerId("GC.Alloc");
        if (gcAllocMarkerId == FrameDataView.invalidMarkerId)
            return samples;

        for (int sampleIndex = 0; sampleIndex < frameData.sampleCount; sampleIndex++)
        {
            if (frameData.GetSampleMarkerId(sampleIndex) != gcAllocMarkerId)
                continue;

            long gcAllocBytes = frameData.GetSampleMetadataAsLong(sampleIndex, 0);
            totalGcAllocBytes += gcAllocBytes;

            List<ulong> callstackPointers = new List<ulong>();
            frameData.GetSampleCallstack(sampleIndex, callstackPointers);

            samples.Add(new GcAllocSample
            {
                sampleIndex = sampleIndex,
                markerName = frameData.GetSampleName(sampleIndex) ?? "GC.Alloc",
                startMs = frameData.GetSampleStartTimeMs(sampleIndex),
                durationMs = frameData.GetSampleTimeMs(sampleIndex),
                gcAllocBytes = gcAllocBytes,
                callstack = callstackPointers.Select(pointer => string.Format(CultureInfo.InvariantCulture, "0x{0:X}", pointer)).ToArray()
            });
        }

        return samples
            .OrderByDescending(item => item.gcAllocBytes)
            .ThenBy(item => item.startMs)
            .Take(topN)
            .ToList();
    }

    private static string BuildQueryPath(string artifactDir, string prefix, params object[] tokens)
    {
        StringBuilder builder = new StringBuilder(256);
        builder.Append(prefix);
        for (int index = 0; index < tokens.Length; index++)
        {
            object token = tokens[index];
            if (token == null)
                continue;

            string text = token.ToString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            builder.Append('_');
            builder.Append(SanitizeLabel(text));
        }

        string fileName = builder.ToString();
        if (fileName.Length > 48)
            fileName = fileName.Substring(0, 32).TrimEnd('_', '.', '-') + "_" + StableHash8(fileName);

        return Path.Combine(artifactDir, QueriesDirectoryName, fileName + ".json");
    }

    private static string StableHash8(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            for (int index = 0; index < value.Length; index++)
            {
                hash ^= value[index];
                hash *= 16777619u;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    private static string SanitizeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "item";

        StringBuilder builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
            else if (character == '-' || character == '_' || character == '.')
                builder.Append(character);
            else
                builder.Append('_');
        }

        return builder.ToString().Trim('_');
    }

    private static void WriteFrameIndexCsv(string path, List<FrameSummaryRecord> frames)
    {
        StringBuilder builder = new StringBuilder(Math.Max(2048, frames.Count * 64));
        builder.AppendLine("frame_index,cpu_frame_ms,gpu_frame_ms,fps,main_thread_ms,render_thread_ms,draw_calls,batches,gc_alloc_bytes,thread_count");
        for (int index = 0; index < frames.Count; index++)
        {
            FrameSummaryRecord frame = frames[index];
            builder.Append(frame.frameIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(FormatCsvNumber(frame.cpuFrameMs));
            builder.Append(',');
            builder.Append(FormatCsvNumber(frame.gpuFrameMs));
            builder.Append(',');
            builder.Append(FormatCsvNumber(frame.fps));
            builder.Append(',');
            builder.Append(FormatCsvNumber(frame.mainThreadMs));
            builder.Append(',');
            builder.Append(FormatCsvNumber(frame.renderThreadMs));
            builder.Append(',');
            builder.Append(frame.drawCalls.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(frame.batches.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(frame.gcAllocBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(frame.threadCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static void WriteThreadIndexCsv(string path, List<ThreadFrameRecord> rows)
    {
        StringBuilder builder = new StringBuilder(Math.Max(2048, rows.Count * 72));
        builder.AppendLine("frame_index,thread_index,thread_id,thread_name,thread_group_name,thread_frame_ms,sample_count,max_depth");
        for (int index = 0; index < rows.Count; index++)
        {
            ThreadFrameRecord row = rows[index];
            builder.Append(row.frameIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(row.threadIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(row.threadId.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(EscapeCsv(row.threadName));
            builder.Append(',');
            builder.Append(EscapeCsv(row.threadGroupName));
            builder.Append(',');
            builder.Append(FormatCsvNumber(row.frameTimeMs));
            builder.Append(',');
            builder.Append(row.sampleCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(row.maxDepth.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
    {
        string text = value ?? string.Empty;
        if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatCsvNumber(double value)
    {
        if (value < 0.0)
            return string.Empty;
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string BuildSessionIdFromSource(string sourcePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return SanitizeLabel(fileName);
    }

    private static string RequireExistingFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(string.Format("{0} is required.", label));

        string resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
            throw new FileNotFoundException(string.Format("{0} was not found.", label), resolved);
        return resolved;
    }

    private static string RequireDirectory(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(string.Format("{0} is required.", label));

        string resolved = Path.GetFullPath(path);
        Directory.CreateDirectory(resolved);
        return resolved;
    }

    private static string FindCommandLineValue(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return string.Empty;
    }

    private static T ReadJson<T>(string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        return JsonUtility.FromJson<T>(json);
    }

    private static void WriteJson<T>(string path, T value)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string json = JsonUtility.ToJson(value, true);
        File.WriteAllText(path, json + Environment.NewLine, Encoding.UTF8);
    }

    private struct ThreadResolution
    {
        public ThreadResolution(ThreadFrameRecord thread)
        {
            reference = new ThreadReference
            {
                threadIndex = thread.threadIndex,
                threadId = thread.threadId,
                threadName = thread.threadName,
                threadGroupName = thread.threadGroupName,
                frameTimeMs = thread.frameTimeMs,
                sampleCount = thread.sampleCount,
                maxDepth = thread.maxDepth
            };
        }

        public readonly ThreadReference reference;
    }

    private struct MarkerMatch
    {
        public int itemId;
        public string name;
        public string path;
        public double startMs;
        public double totalMs;
        public int score;
    }
}
#endif
