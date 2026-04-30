#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CrowdVatSampleScenePerformanceBatchRunner
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SceneName = "SampleScene";
    private const string CrowdObjectName = "QianxiaCrowdIndirect";
    private const int WarmupFrames = 120;
    private const int SampleFrames = 300;
    private const int ScreenshotWidth = 1280;
    private const int ScreenshotHeight = 720;
    private const string SessionPrefix = "QianxiaCrowd.SampleScenePerformance.";
    private const string RunnerActiveKey = SessionPrefix + "Active";
    private const string PhaseKey = SessionPrefix + "Phase";
    private const string FailureMessageKey = SessionPrefix + "FailureMessage";

    private static readonly string ArtifactDirectory =
        Path.Combine(ProjectRoot, ".workspace", "artifacts", "qianxia-crowd-sample-scene-performance");

    private static bool _hooksInstalled;
    private static bool _runtimeInitialized;
    private static int _lastObservedFrame = -1;
    private static int _warmupFrameCounter;
    private static int _missingReferenceFrames;
    private static int _previousVsyncCount;
    private static int _previousTargetFrameRate;
    private static string _screenshotPath;
    private static Component _renderer;
    private static Component _squadController;
    private static Camera _mainCamera;
    private static CrowdConfig _crowdConfig;
    private static List<float> _frameTimesMs;
    private static List<int> _visibleInstanceCounts;
    private static List<CounterRecorder> _counters;

    [Serializable]
    private class ProbeResult
    {
        public string timestamp;
        public string unityVersion;
        public string operatingSystem;
        public string processorType;
        public string graphicsDeviceName;
        public string sceneName;
        public string crowdObjectName;
        public int warmupFrames;
        public int sampleFrames;
        public string screenshotPath;
        public CrowdConfig crowdConfig;
        public CounterSummary[] counters;
        public float averageFrameMs;
        public float p95FrameMs;
        public float minFrameMs;
        public float maxFrameMs;
        public float averageVisibleInstanceCount;
        public int minVisibleInstanceCount;
        public int maxVisibleInstanceCount;
    }

    [Serializable]
    private class CrowdConfig
    {
        public int instanceCount;
        public bool enableTerrainCollision;
        public bool enableStaticSdfCollision;
        public bool enableApproximateCollision;
        public bool enableActiveBubble;
        public float collisionRadius;
        public float collisionHeight;
        public float queryCellSize;
        public int maxCellOccupancy;
        public int maxSpatialQueries;
        public int maxHitsPerSpatialQuery;
        public int solverIterations;
        public int capsulePbdSampleCount;
        public float renderChunkWorldSize;
        public bool runtimeSquadEnabled;
        public int runtimeSquadStateCount;
        public int runtimeAgentSquadDataCount;
        public int runtimeFormationSlotCount;
        public int authoredSquadCount;
    }

    [Serializable]
    private class CounterSummary
    {
        public string name;
        public string unit;
        public double average;
        public double p95;
        public double min;
        public double max;
    }

    private sealed class CounterRecorder : IDisposable
    {
        public CounterRecorder(string name, ProfilerCategory category, string statName, string unit, double scale)
        {
            Name = name;
            Unit = unit;
            Scale = scale;

            try
            {
                Recorder = ProfilerRecorder.StartNew(category, statName, 1);
            }
            catch
            {
                Recorder = default;
            }
        }

        public string Name { get; }
        public string Unit { get; }
        public double Scale { get; }
        public ProfilerRecorder Recorder { get; private set; }
        public List<double> Samples { get; } = new List<double>(SampleFrames);

        public void Sample()
        {
            if (!Recorder.Valid)
                return;

            Samples.Add(Recorder.LastValue * Scale);
        }

        public CounterSummary BuildSummary()
        {
            if (Samples.Count == 0)
                return null;

            List<double> sorted = Samples.OrderBy(value => value).ToList();
            int p95Index = Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.95f) - 1, 0, sorted.Count - 1);

            return new CounterSummary
            {
                name = Name,
                unit = Unit,
                average = Samples.Average(),
                p95 = sorted[p95Index],
                min = sorted[0],
                max = sorted[sorted.Count - 1]
            };
        }

        public void Dispose()
        {
            if (Recorder.Valid)
                Recorder.Dispose();

            Recorder = default;
        }
    }

    private enum RunnerPhase
    {
        Idle = 0,
        WaitingForPlayMode = 1,
        Warmup = 2,
        Sampling = 3
    }

    [InitializeOnLoadMethod]
    private static void InitializeOnLoad()
    {
        EnsureHooks();
    }

    [MenuItem("Tools/Qianxia/Run SampleScene Crowd Performance Capture")]
    public static void RunFromMenu()
    {
        RunInternal();
    }

    public static void RunFromCommandLine()
    {
        RunInternal();
    }

    private static void RunInternal()
    {
        try
        {
            EnsureHooks();

            if (SessionState.GetBool(RunnerActiveKey, false))
            {
                Debug.LogWarning("SampleScene crowd 性能采样已经在运行中。");
                return;
            }

            ClearSessionState();
            SessionState.SetBool(RunnerActiveKey, true);
            SetPhase(RunnerPhase.WaitingForPlayMode);

            Directory.CreateDirectory(ArtifactDirectory);
            _screenshotPath = Path.Combine(ArtifactDirectory, "sample-scene-crowd-performance.png");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (EditorApplication.isPlaying)
            {
                BeginRuntimeSampling();
                return;
            }

            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void EnsureHooks()
    {
        if (_hooksInstalled)
            return;

        _hooksInstalled = true;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
    {
        if (!SessionState.GetBool(RunnerActiveKey, false))
            return;

        if (stateChange == PlayModeStateChange.EnteredPlayMode)
        {
            try
            {
                BeginRuntimeSampling();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }
    }

    private static void OnEditorUpdate()
    {
        if (!SessionState.GetBool(RunnerActiveKey, false))
            return;

        try
        {
            RunnerPhase phase = GetPhase();
            if (phase == RunnerPhase.WaitingForPlayMode && EditorApplication.isPlaying)
                BeginRuntimeSampling();

            if (!EditorApplication.isPlaying)
                return;

            if (GetPhase() != RunnerPhase.Warmup && GetPhase() != RunnerPhase.Sampling)
                return;

            if (Time.frameCount == _lastObservedFrame)
                return;

            _lastObservedFrame = Time.frameCount;

            if (!TryEnsureRuntimeReferences())
            {
                _missingReferenceFrames++;
                if (_missingReferenceFrames > 600)
                    Fail(new InvalidOperationException("进入 PlayMode 后长时间未能找到 SampleScene crowd 运行时对象。"));

                return;
            }

            if (GetPhase() == RunnerPhase.Warmup)
            {
                _warmupFrameCounter++;
                if (_warmupFrameCounter >= WarmupFrames)
                {
                    if (File.Exists(_screenshotPath))
                        File.Delete(_screenshotPath);

                    CaptureCameraImage(_mainCamera, _screenshotPath);
                    SetPhase(RunnerPhase.Sampling);
                }

                return;
            }

            _frameTimesMs.Add(Time.unscaledDeltaTime * 1000.0f);
            _visibleInstanceCounts.Add(GetPrivateFieldValue<int>(_renderer, "_visibleInstanceCount"));
            for (int recorderIndex = 0; recorderIndex < _counters.Count; recorderIndex++)
                _counters[recorderIndex].Sample();

            if (_frameTimesMs.Count >= SampleFrames)
                FinishSuccess();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void BeginRuntimeSampling()
    {
        if (_runtimeInitialized)
            return;

        _runtimeInitialized = true;
        Directory.CreateDirectory(ArtifactDirectory);
        _screenshotPath = Path.Combine(ArtifactDirectory, "sample-scene-crowd-performance.png");
        _lastObservedFrame = -1;
        _warmupFrameCounter = 0;
        _missingReferenceFrames = 0;
        _renderer = null;
        _squadController = null;
        _mainCamera = null;
        _crowdConfig = null;
        _frameTimesMs = new List<float>(SampleFrames);
        _visibleInstanceCounts = new List<int>(SampleFrames);
        _counters = CreateCounterRecorders();

        _previousVsyncCount = QualitySettings.vSyncCount;
        _previousTargetFrameRate = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        SetPhase(RunnerPhase.Warmup);
        Debug.Log("开始执行 SampleScene crowd 性能采样。");
    }

    private static bool TryEnsureRuntimeReferences()
    {
        if (_renderer != null && _mainCamera != null)
            return true;

        GameObject crowdObject = GameObject.Find(CrowdObjectName);
        if (crowdObject == null)
            return false;

        _renderer = crowdObject.GetComponent("CrowdVatIndirectRenderer");
        if (_renderer == null)
            return false;

        _squadController = crowdObject.GetComponent("CrowdVatSquadController");
        _mainCamera = Camera.main;
        if (_mainCamera == null)
            _mainCamera = UnityEngine.Object.FindFirstObjectByType<Camera>();

        if (_mainCamera == null)
            return false;

        _crowdConfig = ReadCrowdConfig(_renderer, _squadController);
        return true;
    }

    private static void FinishSuccess()
    {
        if (_frameTimesMs == null || _frameTimesMs.Count == 0)
            throw new InvalidOperationException("没有采集到有效的帧时间样本。");

        if (_visibleInstanceCounts == null || _visibleInstanceCounts.Count == 0)
            throw new InvalidOperationException("没有采集到可见实例数量样本。");

        ProbeResult result = BuildProbeResult();
        WriteArtifacts(result);

        Debug.Log(
            $"SampleScene crowd 性能快照: avg {result.averageFrameMs:F2} ms, p95 {result.p95FrameMs:F2} ms, visible avg {result.averageVisibleInstanceCount:F1}/{result.crowdConfig.instanceCount}");

        CleanupRuntimeState();
        ClearSessionState();
        EditorApplication.Exit(0);
    }

    private static void Fail(Exception exception)
    {
        string failureMessage = exception != null ? exception.ToString() : "未知错误";
        SessionState.SetString(FailureMessageKey, failureMessage);
        Debug.LogError($"SampleScene crowd 性能采样失败:\n{failureMessage}");
        CleanupRuntimeState();
        ClearSessionState();
        EditorApplication.Exit(1);
    }

    private static void CleanupRuntimeState()
    {
        if (_counters != null)
        {
            for (int recorderIndex = 0; recorderIndex < _counters.Count; recorderIndex++)
                _counters[recorderIndex].Dispose();
        }

        _counters = null;
        _frameTimesMs = null;
        _visibleInstanceCounts = null;
        _renderer = null;
        _squadController = null;
        _mainCamera = null;
        _crowdConfig = null;
        _lastObservedFrame = -1;
        _warmupFrameCounter = 0;
        _missingReferenceFrames = 0;
        _runtimeInitialized = false;

        QualitySettings.vSyncCount = _previousVsyncCount;
        Application.targetFrameRate = _previousTargetFrameRate;
    }

    private static ProbeResult BuildProbeResult()
    {
        List<float> sortedFrameTimes = _frameTimesMs.OrderBy(value => value).ToList();
        int p95Index = Mathf.Clamp(Mathf.CeilToInt(sortedFrameTimes.Count * 0.95f) - 1, 0, sortedFrameTimes.Count - 1);

        List<CounterSummary> counterSummaries = new List<CounterSummary>();
        for (int recorderIndex = 0; recorderIndex < _counters.Count; recorderIndex++)
        {
            CounterSummary summary = _counters[recorderIndex].BuildSummary();
            if (summary != null)
                counterSummaries.Add(summary);
        }

        return new ProbeResult
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            unityVersion = Application.unityVersion,
            operatingSystem = SystemInfo.operatingSystem,
            processorType = SystemInfo.processorType,
            graphicsDeviceName = SystemInfo.graphicsDeviceName,
            sceneName = SceneName,
            crowdObjectName = CrowdObjectName,
            warmupFrames = WarmupFrames,
            sampleFrames = SampleFrames,
            screenshotPath = _screenshotPath,
            crowdConfig = _crowdConfig,
            counters = counterSummaries.ToArray(),
            averageFrameMs = _frameTimesMs.Average(),
            p95FrameMs = sortedFrameTimes[p95Index],
            minFrameMs = sortedFrameTimes[0],
            maxFrameMs = sortedFrameTimes[sortedFrameTimes.Count - 1],
            averageVisibleInstanceCount = (float)_visibleInstanceCounts.Average(),
            minVisibleInstanceCount = _visibleInstanceCounts.Min(),
            maxVisibleInstanceCount = _visibleInstanceCounts.Max()
        };
    }

    private static CrowdConfig ReadCrowdConfig(Component renderer, Component squadController)
    {
        int authoredSquadCount = 0;
        if (squadController != null)
        {
            Array squads = GetPrivateFieldValue<Array>(squadController, "_squads");
            authoredSquadCount = squads != null ? squads.Length : 0;
        }

        return new CrowdConfig
        {
            instanceCount = GetPrivateFieldValue<int>(renderer, "_instanceCount"),
            enableTerrainCollision = GetPrivateFieldValue<bool>(renderer, "_enableTerrainCollision"),
            enableStaticSdfCollision = GetPrivateFieldValue<bool>(renderer, "_enableStaticSdfCollision"),
            enableApproximateCollision = GetPrivateFieldValue<bool>(renderer, "_enableApproximateCollision"),
            enableActiveBubble = GetPrivateFieldValue<bool>(renderer, "_enableActiveBubble"),
            collisionRadius = GetPrivateFieldValue<float>(renderer, "_collisionRadius"),
            collisionHeight = GetPrivateFieldValue<float>(renderer, "_collisionHeight"),
            queryCellSize = GetPrivateFieldValue<float>(renderer, "_queryCellSize"),
            maxCellOccupancy = GetPrivateFieldValue<int>(renderer, "_maxCellOccupancy"),
            maxSpatialQueries = GetPrivateFieldValue<int>(renderer, "_maxSpatialQueries"),
            maxHitsPerSpatialQuery = GetPrivateFieldValue<int>(renderer, "_maxHitsPerSpatialQuery"),
            solverIterations = GetPrivateFieldValue<int>(renderer, "_solverIterations"),
            capsulePbdSampleCount = GetPrivateFieldValue<int>(renderer, "_capsulePbdSampleCount"),
            renderChunkWorldSize = GetPrivateFieldValue<float>(renderer, "_renderChunkWorldSize"),
            runtimeSquadEnabled = GetPublicPropertyValue<bool>(renderer, "HasRuntimeSquadAnchors"),
            runtimeSquadStateCount = GetPrivateFieldValue<int>(renderer, "_activeSquadStateCount"),
            runtimeAgentSquadDataCount = GetPrivateFieldValue<int>(renderer, "_activeAgentSquadDataCount"),
            runtimeFormationSlotCount = GetPrivateFieldValue<int>(renderer, "_activeFormationSlotCount"),
            authoredSquadCount = authoredSquadCount
        };
    }

    private static void WriteArtifacts(ProbeResult result)
    {
        Directory.CreateDirectory(ArtifactDirectory);

        string jsonPath = Path.Combine(ArtifactDirectory, "sample-scene-crowd-performance.json");
        string reportPath = Path.Combine(ArtifactDirectory, "sample-scene-crowd-performance.md");

        File.WriteAllText(jsonPath, JsonUtility.ToJson(result, true), Encoding.UTF8);
        File.WriteAllText(reportPath, BuildMarkdownReport(result), Encoding.UTF8);
    }

    private static string BuildMarkdownReport(ProbeResult result)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# SampleScene Crowd 性能采样");
        builder.AppendLine();
        builder.AppendLine($"- 时间：{result.timestamp}");
        builder.AppendLine($"- 场景：{result.sceneName}");
        builder.AppendLine($"- 对象：{result.crowdObjectName}");
        builder.AppendLine($"- Unity：{result.unityVersion}");
        builder.AppendLine($"- 操作系统：{result.operatingSystem}");
        builder.AppendLine($"- CPU：{result.processorType}");
        builder.AppendLine($"- GPU：{result.graphicsDeviceName}");
        builder.AppendLine($"- 预热帧：{result.warmupFrames}");
        builder.AppendLine($"- 采样帧：{result.sampleFrames}");
        builder.AppendLine($"- 截图：{result.screenshotPath}");
        builder.AppendLine();
        builder.AppendLine("## Crowd 配置");
        builder.AppendLine();
        builder.AppendLine($"- 实例数：{result.crowdConfig.instanceCount}");
        builder.AppendLine($"- 平均可见实例：{result.averageVisibleInstanceCount:F1}");
        builder.AppendLine($"- 可见实例范围：{result.minVisibleInstanceCount} - {result.maxVisibleInstanceCount}");
        builder.AppendLine($"- 地形碰撞：{result.crowdConfig.enableTerrainCollision}");
        builder.AppendLine($"- Static SDF：{result.crowdConfig.enableStaticSdfCollision}");
        builder.AppendLine($"- 近似碰撞：{result.crowdConfig.enableApproximateCollision}");
        builder.AppendLine($"- Active Bubble：{result.crowdConfig.enableActiveBubble}");
        builder.AppendLine($"- 碰撞半径：{result.crowdConfig.collisionRadius:F2}");
        builder.AppendLine($"- 碰撞高度：{result.crowdConfig.collisionHeight:F2}");
        builder.AppendLine($"- Cell Size：{result.crowdConfig.queryCellSize:F2}");
        builder.AppendLine($"- Max Cell Occupancy：{result.crowdConfig.maxCellOccupancy}");
        builder.AppendLine($"- Max Spatial Queries：{result.crowdConfig.maxSpatialQueries}");
        builder.AppendLine($"- Max Hits Per Query：{result.crowdConfig.maxHitsPerSpatialQuery}");
        builder.AppendLine($"- Solver Iterations：{result.crowdConfig.solverIterations}");
        builder.AppendLine($"- Capsule PBD Sample Count：{result.crowdConfig.capsulePbdSampleCount}");
        builder.AppendLine($"- Render Chunk World Size：{result.crowdConfig.renderChunkWorldSize:F2}");
        builder.AppendLine($"- Runtime Squad：{result.crowdConfig.runtimeSquadEnabled}");
        builder.AppendLine($"- Runtime Squad States：{result.crowdConfig.runtimeSquadStateCount}");
        builder.AppendLine($"- Runtime Agent Squad Data：{result.crowdConfig.runtimeAgentSquadDataCount}");
        builder.AppendLine($"- Runtime Formation Slots：{result.crowdConfig.runtimeFormationSlotCount}");
        builder.AppendLine($"- Authored Squad Count：{result.crowdConfig.authoredSquadCount}");
        builder.AppendLine();
        builder.AppendLine("## 帧时间");
        builder.AppendLine();
        builder.AppendLine($"- 平均帧时：{result.averageFrameMs:F2} ms");
        builder.AppendLine($"- P95 帧时：{result.p95FrameMs:F2} ms");
        builder.AppendLine($"- 最快帧时：{result.minFrameMs:F2} ms");
        builder.AppendLine($"- 最慢帧时：{result.maxFrameMs:F2} ms");
        builder.AppendLine();

        if (result.counters != null && result.counters.Length > 0)
        {
            builder.AppendLine("## 计数器");
            builder.AppendLine();
            builder.AppendLine("| 计数器 | 平均 | P95 | 最小 | 最大 | 单位 |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | --- |");
            for (int counterIndex = 0; counterIndex < result.counters.Length; counterIndex++)
            {
                CounterSummary counter = result.counters[counterIndex];
                builder.AppendLine(
                    $"| {counter.name} | {counter.average:F2} | {counter.p95:F2} | {counter.min:F2} | {counter.max:F2} | {counter.unit} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("说明：该结果来自当前 SampleScene 的编辑器 PlayMode 批处理采样，用于评估真实场景配置下的人群系统开销。");
        return builder.ToString();
    }

    private static List<CounterRecorder> CreateCounterRecorders()
    {
        return new List<CounterRecorder>
        {
            new CounterRecorder("Batches", ProfilerCategory.Render, "Batches Count", "count", 1.0),
            new CounterRecorder("SetPass", ProfilerCategory.Render, "SetPass Calls Count", "count", 1.0),
            new CounterRecorder("DrawCalls", ProfilerCategory.Render, "Draw Calls Count", "count", 1.0),
            new CounterRecorder("MainThread", ProfilerCategory.Internal, "Main Thread", "ms", 1e-6),
            new CounterRecorder("RenderThread", ProfilerCategory.Internal, "Render Thread", "ms", 1e-6)
        };
    }

    private static void CaptureCameraImage(Camera camera, string screenshotPath)
    {
        RenderTexture renderTexture = RenderTexture.GetTemporary(ScreenshotWidth, ScreenshotHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        Texture2D texture = null;

        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture.active = renderTexture;
            texture = new Texture2D(ScreenshotWidth, ScreenshotHeight, TextureFormat.RGBA32, false, false);
            texture.ReadPixels(new Rect(0, 0, ScreenshotWidth, ScreenshotHeight), 0, 0);
            texture.Apply(false, false);

            byte[] pngBytes = texture.EncodeToPNG();
            File.WriteAllBytes(screenshotPath, pngBytes);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);

            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
    }

    private static T GetPrivateFieldValue<T>(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(instance.GetType().FullName, fieldName);

        return (T)field.GetValue(instance);
    }

    private static T GetPublicPropertyValue<T>(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property == null)
            throw new MissingMemberException(instance.GetType().FullName, propertyName);

        return (T)property.GetValue(instance);
    }

    private static RunnerPhase GetPhase()
    {
        return (RunnerPhase)SessionState.GetInt(PhaseKey, (int)RunnerPhase.Idle);
    }

    private static void SetPhase(RunnerPhase phase)
    {
        SessionState.SetInt(PhaseKey, (int)phase);
    }

    private static void ClearSessionState()
    {
        SessionState.SetBool(RunnerActiveKey, false);
        SessionState.SetInt(PhaseKey, (int)RunnerPhase.Idle);
        SessionState.SetString(FailureMessageKey, string.Empty);
    }

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
}
#endif
