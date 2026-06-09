using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PerformancePoiSampler : MonoBehaviour, IRenderDocCaptureContextProvider
{
    private const int DefaultRenderDocCompletionWaitFrames = 180;
    private const float DefaultReplayTimeoutPaddingSeconds = 5.0f;

    private enum SamplerState
    {
        Idle = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    private enum CounterKind
    {
        Batches = 0,
        SetPass = 1,
        DrawCalls = 2
    }

    private struct SampleStats
    {
        public float average;
        public float p95;
        public float p99;
        public float min;
        public float max;
    }

    private struct ProfilerRawCaptureState
    {
        public bool started;
        public string outputPath;
        public string previousLogFile;
        public bool previousEnabled;
        public bool previousBinaryLog;
    }

    private sealed class CounterRecorder : IDisposable
    {
        public CounterRecorder(CounterKind kind, string name, ProfilerCategory category, string statName, double scale)
        {
            Kind = kind;
            Name = name;
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

        public CounterKind Kind { get; }
        public string Name { get; }
        public double Scale { get; }
        public ProfilerRecorder Recorder { get; private set; }
        public List<float> Samples { get; } = new List<float>(256);
        public bool HasSamples => Samples.Count > 0;

        public void Sample()
        {
            if (!Recorder.Valid)
                return;

            Samples.Add((float)(Recorder.LastValue * Scale));
        }

        public float GetAverage()
        {
            if (Samples.Count == 0)
                return -1.0f;

            float total = 0.0f;
            for (int index = 0; index < Samples.Count; index++)
                total += Samples[index];

            return total / Samples.Count;
        }

        public void Dispose()
        {
            if (Recorder.Valid)
                Recorder.Dispose();

            Recorder = default;
        }
    }

    [Header("Output")]
    [SerializeField] private string _artifactsRootOverride = string.Empty;
    [SerializeField] private string _artifactsRelativePath = PerformancePoiSamplingArtifacts.DefaultRelativeArtifactsRoot;
    [SerializeField] private string _defaultRunLabel = "poi-auto";
    [SerializeField] private bool _autoQuitPlayerWhenFinished;

    [Header("Sampling")]
    [SerializeField] private int _defaultSettleFrames = 20;
    [SerializeField] private int _defaultWarmupFrames = 60;
    [SerializeField] private int _defaultSampleFrames = 180;
    [SerializeField] private bool _captureScreenshots = true;
    [SerializeField] private int _screenshotWidth = 1280;
    [SerializeField] private int _screenshotHeight = 720;

    [Header("RenderDoc Integration")]
    [SerializeField] private bool _queueRenderDocForMarkedPoints = true;
    [SerializeField] private int _renderDocCaptureDelayFrames = 8;
    [SerializeField] private int _renderDocCompletionWaitFrames = DefaultRenderDocCompletionWaitFrames;

    [Header("Bindings")]
    [SerializeField] private Camera _targetCamera;
    [SerializeField] private CharacterController _targetCharacterController;
    [SerializeField] private Transform _targetCharacterTransform;
    [SerializeField] private bool _disableQianxiaCameraControllerDuringRun = true;
    [SerializeField] private bool _disableSquadCommandControllerDuringRun = true;

    private readonly Dictionary<string, int> _captureLabelToResultIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingCaptureLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly FrameTiming[] _frameTimingBuffer = new FrameTiming[1];
    private readonly List<PerformancePoiSampleResult> _results = new List<PerformancePoiSampleResult>(32);

    private Coroutine _runCoroutine;
    private SamplerState _state;
    private string _statusMessage = "空闲";
    private string _lastArtifactDirectory = string.Empty;
    private string _runtimeArtifactsRootOverride = string.Empty;
    private string _runtimeReplayTraceRootOverride = string.Empty;
    private string _runtimeRunLabelOverride = string.Empty;
    private bool? _runtimeAutoQuitOverride;
    private bool _cancelRequested;
    private int _activePoiIndex = -1;
    private PerformancePoiPoint _activePoi;
    private PerformancePoiRunSummary _activeRunSummary;
    private RenderDocCaptureController _renderDocCaptureController;
    private PerformancePoiReplayController _poiReplayController;
    private QianxiaGenshinCameraController _qianxiaCameraController;
    private CrowdVatSquadCommandController _squadCommandController;
    private bool _cameraControllerWasEnabled;
    private bool _squadCommandControllerWasEnabled;

    public string ProviderId => "performancePoi";
    public bool IsRunning => _state == SamplerState.Running;
    public string StatusMessage => _statusMessage;
    public string LastArtifactDirectory => _lastArtifactDirectory;
    public string ResolvedArtifactsRoot => PerformancePoiSamplingArtifacts.ResolveArtifactsRoot(
        string.IsNullOrWhiteSpace(_runtimeArtifactsRootOverride) ? _artifactsRootOverride : _runtimeArtifactsRootOverride,
        _artifactsRelativePath);
    public string ResolvedReplayTraceRoot => PerformancePoiSamplingArtifacts.ResolveReplayTraceRoot(_runtimeReplayTraceRootOverride);

    private void Reset()
    {
        _targetCamera = Camera.main;
        _targetCharacterController = FindFirstObjectByType<CharacterController>();
        _targetCharacterTransform = _targetCharacterController != null ? _targetCharacterController.transform : null;
    }

    private void OnDisable()
    {
        if (_runCoroutine != null)
        {
            StopCoroutine(_runCoroutine);
            _runCoroutine = null;
        }

        CleanupAfterRun();
        _state = SamplerState.Idle;
    }

    private void Update()
    {
        if (!Application.isPlaying || !Application.isEditor || IsRunning)
            return;

        HandleEditorTraceRecordingHotkeys();
    }

    public void ApplyRuntimeOverrides(
        string artifactsRootOverride,
        string replayTraceRootOverride,
        string runLabelOverride,
        bool? autoQuitPlayerWhenFinished)
    {
        _runtimeArtifactsRootOverride = artifactsRootOverride ?? string.Empty;
        _runtimeReplayTraceRootOverride = replayTraceRootOverride ?? string.Empty;
        _runtimeRunLabelOverride = runLabelOverride ?? string.Empty;
        _runtimeAutoQuitOverride = autoQuitPlayerWhenFinished;
    }

    public bool BeginRun(string runLabelOverride = null)
    {
        if (_runCoroutine != null)
        {
            _statusMessage = "已有 POI 采样任务在运行。";
            return false;
        }

        ResolveBindings();
        if (_targetCamera == null)
        {
            _statusMessage = "未找到可用相机，无法开始 POI 采样。";
            return false;
        }

        List<PerformancePoiPoint> points = CollectPoints();
        if (points.Count == 0)
        {
            _statusMessage = "场景中没有启用的 PerformancePoiPoint。";
            return false;
        }

        string runLabel = !string.IsNullOrWhiteSpace(runLabelOverride)
            ? runLabelOverride.Trim()
            : (!string.IsNullOrWhiteSpace(_runtimeRunLabelOverride) ? _runtimeRunLabelOverride.Trim() : _defaultRunLabel);

        _cancelRequested = false;
        _runCoroutine = StartCoroutine(RunSampling(points, runLabel));
        return true;
    }

    public void CancelRun()
    {
        if (_runCoroutine == null)
            return;

        _cancelRequested = true;
        _statusMessage = "正在取消 POI 采样任务。";
    }

    public string BuildSuggestedCommandLineArgs()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "--perf-poi-auto-run --perf-poi-runner {0} --perf-poi-trace-root {1} --perf-poi-auto-quit",
            QuoteArgument(gameObject.name),
            QuoteArgument(ResolvedReplayTraceRoot));
    }

    public bool TryMoveRuntimeViewToPoint(PerformancePoiPoint point, out string statusMessage)
    {
        if (!Application.isPlaying)
        {
            statusMessage = "Can only move the runtime camera to a POI during Play Mode.";
            return false;
        }

        if (point == null)
        {
            statusMessage = "Cannot move the runtime camera because no POI is selected.";
            return false;
        }

        if (IsRunning)
        {
            statusMessage = "Cannot move the runtime camera while a POI sampling run is active.";
            return false;
        }

        ResolveBindings();
        if (_targetCamera == null)
        {
            statusMessage = "Cannot move the runtime camera because the target camera could not be resolved.";
            return false;
        }

        PerformancePoiSampleResult previewResult = new PerformancePoiSampleResult();
        MoveToPoint(point, previewResult);

        statusMessage = string.Format(
            CultureInfo.InvariantCulture,
            "Moved runtime camera to POI '{0}'.",
            point.PoiId);

        if (!string.IsNullOrWhiteSpace(previewResult.dataQuality))
            statusMessage = statusMessage + " " + previewResult.dataQuality;

        _statusMessage = statusMessage;
        return true;
    }

    public bool TryBuildPayload(out RenderDocCaptureProviderPayload payload)
    {
        if (!IsRunning || _activePoi == null || _activeRunSummary == null)
        {
            payload = default;
            return false;
        }

        payload = new RenderDocCaptureProviderPayload
        {
            providerId = ProviderId,
            summary = string.Format(
                CultureInfo.InvariantCulture,
                "run={0}, poi={1}, category={2}, order={3}/{4}",
                _activeRunSummary.runId,
                _activePoi.PoiId,
                _activePoi.Category,
                Mathf.Max(0, _activePoiIndex + 1),
                Mathf.Max(0, _activeRunSummary.totalPoiCount)),
            suggestedFileName = "performance-poi.json",
            jsonPayload = string.Empty
        };
        return true;
    }

    private IEnumerator RunSampling(List<PerformancePoiPoint> points, string runLabel)
    {
        _state = SamplerState.Running;
        _results.Clear();
        _captureLabelToResultIndex.Clear();
        _pendingCaptureLabels.Clear();
        _activePoiIndex = -1;
        _activePoi = null;

        DateTime localTimestamp = DateTime.Now;
        DateTime utcTimestamp = DateTime.UtcNow;
        Scene activeScene = SceneManager.GetActiveScene();
        string runDirectory = PerformancePoiSamplingArtifacts.CreateRunDirectory(
            string.IsNullOrWhiteSpace(_runtimeArtifactsRootOverride) ? _artifactsRootOverride : _runtimeArtifactsRootOverride,
            localTimestamp,
            activeScene.name,
            runLabel);

        _lastArtifactDirectory = runDirectory;

        _activeRunSummary = new PerformancePoiRunSummary
        {
            runId = Path.GetFileName(runDirectory),
            runLabel = runLabel,
            status = "Running",
            timestampLocal = localTimestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            timestampUtc = utcTimestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            unityVersion = Application.unityVersion,
            applicationPlatform = Application.platform.ToString(),
            graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
            graphicsDeviceName = SystemInfo.graphicsDeviceName,
            sceneName = activeScene.name,
            scenePath = activeScene.path,
            screenWidth = Screen.width,
            screenHeight = Screen.height,
            targetCameraName = _targetCamera != null ? _targetCamera.name : "None",
            targetCharacterName = _targetCharacterTransform != null ? _targetCharacterTransform.name : "None",
            artifactDirectory = runDirectory,
            summaryJsonPath = PerformancePoiSamplingArtifacts.BuildSummaryJsonPath(runDirectory),
            summaryMarkdownPath = PerformancePoiSamplingArtifacts.BuildSummaryMarkdownPath(runDirectory),
            summaryCsvPath = PerformancePoiSamplingArtifacts.BuildSummaryCsvPath(runDirectory),
            totalPoiCount = points.Count,
            completedPoiCount = 0,
            completedRenderDocCaptureCount = 0,
            completedProfilerRawExportCount = 0,
            autoQuitPlayerWhenFinished = ResolveAutoQuitPlayerWhenFinished()
        };

        SubscribeRenderDocCaptureController();
        ApplyControllerOverrides(enable: true);

        Exception failure = null;

        try
        {
            yield return RunCoroutineSafely(
                RunSamplingBody(points, runDirectory),
                exception =>
                {
                    failure = exception;
                    _state = SamplerState.Failed;
                    _activeRunSummary.status = "Failed";
                    _activeRunSummary.failureMessage = exception.ToString();
                    _statusMessage = "POI 采样失败，详情已写入 artifact。";
                    Debug.LogError("[PerformancePoiSampler] " + exception);
                });

            if (_state == SamplerState.Running)
            {
                _state = SamplerState.Completed;
                _activeRunSummary.status = "Completed";
            }
        }
        finally
        {
            _activePoiIndex = -1;
            _activePoi = null;
            _activeRunSummary.points = _results.ToArray();

            int completedRenderDocCaptures = 0;
            int completedProfilerRawExports = 0;
            for (int index = 0; index < _activeRunSummary.points.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(_activeRunSummary.points[index].renderDocCapturePath))
                    completedRenderDocCaptures++;

                if (_activeRunSummary.points[index].profilerRawExported)
                    completedProfilerRawExports++;
            }

            _activeRunSummary.completedRenderDocCaptureCount = completedRenderDocCaptures;
            _activeRunSummary.completedProfilerRawExportCount = completedProfilerRawExports;
            PerformancePoiSamplingArtifacts.WriteRunSummaryJson(_activeRunSummary.summaryJsonPath, _activeRunSummary);
            PerformancePoiSamplingArtifacts.WriteRunSummaryMarkdown(_activeRunSummary.summaryMarkdownPath, _activeRunSummary);
            PerformancePoiSamplingArtifacts.WriteRunSummaryCsv(_activeRunSummary.summaryCsvPath, _activeRunSummary);

            CleanupAfterRun();
            _runCoroutine = null;

            if (_state == SamplerState.Completed)
            {
                _statusMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "POI 采样完成：{0}",
                    _activeRunSummary.summaryMarkdownPath);
            }
            else if (_state == SamplerState.Cancelled)
            {
                _statusMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "POI 采样已取消，当前结果已写出：{0}",
                    _activeRunSummary.summaryMarkdownPath);
            }
            else if (_state == SamplerState.Failed && failure != null)
            {
                _statusMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "POI 采样失败：{0}",
                    failure.Message);
            }

            if (!Application.isEditor && ResolveAutoQuitPlayerWhenFinished())
                Application.Quit();
        }
    }

    private IEnumerator RunSamplingBody(List<PerformancePoiPoint> points, string runDirectory)
    {
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            if (_cancelRequested)
            {
                _activeRunSummary.status = "Cancelled";
                _state = SamplerState.Cancelled;
                yield break;
            }

            PerformancePoiPoint point = points[pointIndex];
            _activePoiIndex = pointIndex;
            _activePoi = point;

            PerformancePoiSampleResult result = new PerformancePoiSampleResult
            {
                poiKey = point.PoiKey,
                poiId = point.PoiId,
                poiName = point.gameObject.name,
                runMode = point.PoiSamplingMode.ToString(),
                category = point.Category,
                notes = point.Notes,
                sortIndex = point.SortIndex,
                settleFrames = Mathf.Max(0, point.ResolveSettleFrames(_defaultSettleFrames)),
                warmupFrames = Mathf.Max(0, point.ResolveWarmupFrames(_defaultWarmupFrames)),
                sampleFrames = 0,
                profilerRawExportRequested = point.ExportProfilerRaw,
                profilerRawPath = point.ExportProfilerRaw
                    ? PerformancePoiSamplingArtifacts.BuildProfilerRawPath(runDirectory, pointIndex + 1, point.PoiId)
                    : string.Empty,
                profilerRawFileSizeBytes = -1L
            };
            _results.Add(result);

            if (point.UsesTurntableSweep)
            {
                _statusMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "POI 閲囨牱涓細{0} ({1}/{2})",
                    result.poiId,
                    pointIndex + 1,
                    points.Count);

                yield return SampleTurntableSweepPoint(point, pointIndex, result);

                if (result.sampleFrames > 0 && _captureScreenshots && point.CaptureScreenshot)
                {
                    result.screenshotPath = PerformancePoiSamplingArtifacts.BuildScreenshotPath(runDirectory, pointIndex + 1, result.poiId);
                    result.screenshotCaptured = CaptureCameraImage(_targetCamera, result.screenshotPath, _screenshotWidth, _screenshotHeight);
                }

                _activeRunSummary.completedPoiCount++;
                continue;
            }

            _statusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "POI 采样中：{0} ({1}/{2})",
                result.poiId,
                pointIndex + 1,
                points.Count);

            for (int settleFrame = 0; settleFrame < result.settleFrames; settleFrame++)
                yield return null;

            for (int warmupFrame = 0; warmupFrame < result.warmupFrames; warmupFrame++)
                yield return null;

            if (!point.TryLoadReplayTrace(
                    ResolvedReplayTraceRoot,
                    out PerformancePoiReplayTrace trace,
                    out string tracePath,
                    out string traceLoadError))
            {
                result.dataQuality = AppendDataQuality(result.dataQuality, traceLoadError);
                _activeRunSummary.completedPoiCount++;
                continue;
            }

            CaptureReplayTraceMetadata(trace, result);

            PerformancePoiReplayController replayController = EnsureReplayController();
            if (replayController == null)
            {
                result.dataQuality = AppendDataQuality(result.dataQuality, "未能创建 POI replay controller。");
                _activeRunSummary.completedPoiCount++;
                continue;
            }

            if (!replayController.StartReplay(trace, tracePath))
            {
                result.dataQuality = AppendDataQuality(result.dataQuality, replayController.StatusMessage);
                _activeRunSummary.completedPoiCount++;
                continue;
            }

            ProfilerRawCaptureState profilerRawCapture = BeginProfilerRawCaptureIfNeeded(point, pointIndex, runDirectory, result);
            string pendingCaptureLabel = QueueRenderDocCaptureIfNeeded(point, pointIndex, result);

            int estimatedFrameCapacity = Mathf.Max(
                _defaultSampleFrames,
                Mathf.CeilToInt(Mathf.Max(0.0f, trace.duration) * Mathf.Max(10, Application.targetFrameRate > 0 ? Application.targetFrameRate : 60)));
            List<float> frameTimes = new List<float>(estimatedFrameCapacity);
            List<float> cpuFrameTimes = new List<float>(estimatedFrameCapacity);
            List<float> gpuFrameTimes = new List<float>(estimatedFrameCapacity);
            List<float> mainThreadTimes = new List<float>(estimatedFrameCapacity);
            List<float> renderThreadTimes = new List<float>(estimatedFrameCapacity);

            List<CounterRecorder> counters = CreateCounterRecorders();
            try
            {
                float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(
                    DefaultReplayTimeoutPaddingSeconds,
                    replayController.ActiveReplayDuration + DefaultReplayTimeoutPaddingSeconds);

                while (replayController.IsReplayingTrace)
                {
                    yield return null;

                    frameTimes.Add(Time.unscaledDeltaTime * 1000.0f);
                    CollectFrameTiming(cpuFrameTimes, gpuFrameTimes, mainThreadTimes, renderThreadTimes);

                    for (int counterIndex = 0; counterIndex < counters.Count; counterIndex++)
                        counters[counterIndex].Sample();

                    if (Time.realtimeSinceStartup <= timeoutAt)
                        continue;

                    replayController.CancelActiveSession();
                    result.dataQuality = AppendDataQuality(
                        result.dataQuality,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "POI replay 超时中断：{0:0.###}s",
                            Mathf.Max(0.0f, trace.duration) + DefaultReplayTimeoutPaddingSeconds));
                }

                result.sampleFrames = frameTimes.Count;
                FillMetricSummaries(result, frameTimes, cpuFrameTimes, gpuFrameTimes, mainThreadTimes, renderThreadTimes, counters);
            }
            finally
            {
                EndProfilerRawCapture(profilerRawCapture, result);
                DisposeCounterRecorders(counters);
            }

            if (_captureScreenshots && point.CaptureScreenshot)
            {
                result.screenshotPath = PerformancePoiSamplingArtifacts.BuildScreenshotPath(runDirectory, pointIndex + 1, result.poiId);
                result.screenshotCaptured = CaptureCameraImage(_targetCamera, result.screenshotPath, _screenshotWidth, _screenshotHeight);
            }

            if (!string.IsNullOrWhiteSpace(pendingCaptureLabel))
                yield return WaitForRenderDocCompletion(pendingCaptureLabel, result);

            _activeRunSummary.completedPoiCount++;
        }
    }

    private IEnumerator RunCoroutineSafely(IEnumerator routine, Action<Exception> onFailure)
    {
        if (routine == null)
            yield break;

        Stack<IEnumerator> enumeratorStack = new Stack<IEnumerator>();
        enumeratorStack.Push(routine);

        while (enumeratorStack.Count > 0)
        {
            IEnumerator currentEnumerator = enumeratorStack.Peek();
            bool hasNext;
            object currentYield;

            try
            {
                hasNext = currentEnumerator.MoveNext();
                currentYield = hasNext ? currentEnumerator.Current : null;
            }
            catch (Exception exception)
            {
                onFailure?.Invoke(exception);
                yield break;
            }

            if (!hasNext)
            {
                enumeratorStack.Pop();
                continue;
            }

            if (currentYield is IEnumerator nestedEnumerator)
            {
                enumeratorStack.Push(nestedEnumerator);
                continue;
            }

            yield return currentYield;
        }
    }

    private IEnumerator SampleTurntableSweepPoint(
        PerformancePoiPoint point,
        int pointIndex,
        PerformancePoiSampleResult result)
    {
        if (point == null)
            yield break;

        float sweepDurationSeconds = point.ResolveTurntableDurationSeconds();
        if (sweepDurationSeconds <= 0.0f)
        {
            result.dataQuality = AppendDataQuality(
                result.dataQuality,
                "Turntable sweep speed must be non-zero to rotate one full circle.");
            yield break;
        }

        MoveToPoint(point, result);

        for (int settleFrame = 0; settleFrame < result.settleFrames; settleFrame++)
            yield return null;

        for (int warmupFrame = 0; warmupFrame < result.warmupFrames; warmupFrame++)
            yield return null;

        ApplyTurntablePose(point, 0.0f, result);
        ProfilerRawCaptureState profilerRawCapture = BeginProfilerRawCaptureIfNeeded(point, pointIndex, _lastArtifactDirectory, result);
        string pendingCaptureLabel = QueueRenderDocCaptureIfNeeded(point, pointIndex, result);

        int estimatedFrameCapacity = Mathf.Max(
            _defaultSampleFrames,
            Mathf.CeilToInt(sweepDurationSeconds * Mathf.Max(10, Application.targetFrameRate > 0 ? Application.targetFrameRate : 60)));
        List<float> frameTimes = new List<float>(estimatedFrameCapacity);
        List<float> cpuFrameTimes = new List<float>(estimatedFrameCapacity);
        List<float> gpuFrameTimes = new List<float>(estimatedFrameCapacity);
        List<float> mainThreadTimes = new List<float>(estimatedFrameCapacity);
        List<float> renderThreadTimes = new List<float>(estimatedFrameCapacity);

        List<CounterRecorder> counters = CreateCounterRecorders();
        float sweepStartTime = Time.realtimeSinceStartup;

        try
        {
            while (true)
            {
                yield return null;

                float elapsedSeconds = Mathf.Max(0.0f, Time.realtimeSinceStartup - sweepStartTime);
                float clampedElapsedSeconds = Mathf.Min(elapsedSeconds, sweepDurationSeconds);
                ApplyTurntablePose(point, clampedElapsedSeconds, result);

                frameTimes.Add(Time.unscaledDeltaTime * 1000.0f);
                CollectFrameTiming(cpuFrameTimes, gpuFrameTimes, mainThreadTimes, renderThreadTimes);

                for (int counterIndex = 0; counterIndex < counters.Count; counterIndex++)
                    counters[counterIndex].Sample();

                if (elapsedSeconds >= sweepDurationSeconds)
                    break;
            }

            result.sampleFrames = frameTimes.Count;
            FillMetricSummaries(result, frameTimes, cpuFrameTimes, gpuFrameTimes, mainThreadTimes, renderThreadTimes, counters);
        }
        finally
        {
            EndProfilerRawCapture(profilerRawCapture, result);
            DisposeCounterRecorders(counters);
        }

        if (!string.IsNullOrWhiteSpace(pendingCaptureLabel))
            yield return WaitForRenderDocCompletion(pendingCaptureLabel, result);
    }

    private void MoveToPoint(PerformancePoiPoint point, PerformancePoiSampleResult result)
    {
        if (point == null)
            return;

        if (_targetCharacterTransform != null)
        {
            point.ResolveCharacterPose(out Vector3 characterPosition, out Quaternion characterRotation);
            point.ProjectCharacterToGround(ref characterPosition);

            bool wasCharacterControllerEnabled = _targetCharacterController != null && _targetCharacterController.enabled;
            if (_targetCharacterController != null && wasCharacterControllerEnabled)
                _targetCharacterController.enabled = false;

            _targetCharacterTransform.SetPositionAndRotation(characterPosition, characterRotation);

            if (_targetCharacterController != null && wasCharacterControllerEnabled)
                _targetCharacterController.enabled = true;

            result.characterPosition = _targetCharacterTransform.position;
            result.characterForward = _targetCharacterTransform.forward;
        }

        if (_targetCamera != null)
        {
            point.ResolveCameraPose(out Vector3 cameraPosition, out Quaternion cameraRotation);
            _targetCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            result.cameraPosition = _targetCamera.transform.position;
            result.cameraForward = _targetCamera.transform.forward;
        }
        else
        {
            result.dataQuality = AppendDataQuality(result.dataQuality, "未找到目标相机，截图和视角相关指标不可用。");
        }
    }

    private void ApplyTurntablePose(PerformancePoiPoint point, float elapsedSeconds, PerformancePoiSampleResult result)
    {
        if (point == null || _targetCamera == null)
            return;

        point.ResolveTurntableCameraPose(elapsedSeconds, out Vector3 cameraPosition, out Quaternion cameraRotation);
        _targetCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);

        if (result == null)
            return;

        result.cameraPosition = _targetCamera.transform.position;
        result.cameraForward = _targetCamera.transform.forward;
    }

    private void CaptureReplayTraceMetadata(PerformancePoiReplayTrace trace, PerformancePoiSampleResult result)
    {
        if (trace == null || trace.poseSamples == null || trace.poseSamples.Count == 0)
            return;

        PerformancePoiReplayPoseSample firstSample = trace.poseSamples[0];
        result.characterPosition = firstSample.characterPosition;
        result.characterForward = firstSample.characterRotation * Vector3.forward;
        result.cameraPosition = firstSample.cameraPosition;
        result.cameraForward = firstSample.cameraRotation * Vector3.forward;
    }

    private PerformancePoiReplayController EnsureReplayController()
    {
        if (_poiReplayController == null)
            _poiReplayController = PerformancePoiReplayController.GetOrCreate(gameObject);

        return _poiReplayController;
    }

    private string QueueRenderDocCaptureIfNeeded(PerformancePoiPoint point, int pointIndex, PerformancePoiSampleResult result)
    {
        if (!_queueRenderDocForMarkedPoints || point == null || !point.CaptureRenderDoc)
            return string.Empty;

        if (_renderDocCaptureController == null)
        {
            result.dataQuality = AppendDataQuality(result.dataQuality, "场景里没有 RenderDocCaptureController，已跳过抓帧。");
            return string.Empty;
        }

        string captureLabel = string.Format(
            CultureInfo.InvariantCulture,
            "{0}-{1:D2}-{2}",
            _activeRunSummary.runId,
            pointIndex + 1,
            RenderDocCaptureArtifacts.SanitizeLabel(point.PoiId));
        string notes = string.Format(
            CultureInfo.InvariantCulture,
            "poi={0}; category={1}; order={2}/{3}",
            point.PoiId,
            point.Category,
            pointIndex + 1,
            _activeRunSummary.totalPoiCount);

        result.renderDocCaptureRequested = _renderDocCaptureController.QueueCapture(
            captureLabel,
            Mathf.Max(0, _renderDocCaptureDelayFrames),
            "poi-auto",
            notes,
            _targetCamera);

        if (!result.renderDocCaptureRequested)
        {
            result.dataQuality = AppendDataQuality(
                result.dataQuality,
                "RenderDoc 抓帧排队失败: " + _renderDocCaptureController.StatusMessage);
            return string.Empty;
        }

        _captureLabelToResultIndex[captureLabel] = pointIndex;
        _pendingCaptureLabels.Add(captureLabel);
        return captureLabel;
    }

    private ProfilerRawCaptureState BeginProfilerRawCaptureIfNeeded(
        PerformancePoiPoint point,
        int pointIndex,
        string runDirectory,
        PerformancePoiSampleResult result)
    {
        ProfilerRawCaptureState state = default;
        if (point == null || result == null || !point.ExportProfilerRaw)
            return state;

        result.profilerRawExportRequested = true;
        if (string.IsNullOrWhiteSpace(result.profilerRawPath))
            result.profilerRawPath = PerformancePoiSamplingArtifacts.BuildProfilerRawPath(runDirectory, pointIndex + 1, point.PoiId);

        state.outputPath = result.profilerRawPath;
        if (string.IsNullOrWhiteSpace(state.outputPath))
        {
            result.dataQuality = AppendDataQuality(result.dataQuality, "Profiler .raw export skipped because the output path is empty.");
            return state;
        }

        try
        {
            string directory = Path.GetDirectoryName(state.outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(state.outputPath))
                File.Delete(state.outputPath);

            state.previousLogFile = Profiler.logFile ?? string.Empty;
            state.previousEnabled = Profiler.enabled;
            state.previousBinaryLog = Profiler.enableBinaryLog;

            if (Profiler.enabled)
                Profiler.enabled = false;

            Profiler.logFile = state.outputPath;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            state.started = true;
        }
        catch (Exception exception)
        {
            result.dataQuality = AppendDataQuality(
                result.dataQuality,
                "Profiler .raw export failed to start: " + exception.Message);
        }

        return state;
    }

    private void EndProfilerRawCapture(ProfilerRawCaptureState state, PerformancePoiSampleResult result)
    {
        if (!state.started)
            return;

        try
        {
            Profiler.enabled = false;
            Profiler.enableBinaryLog = state.previousBinaryLog;
            Profiler.logFile = state.previousLogFile ?? string.Empty;
            Profiler.enabled = state.previousEnabled;
        }
        catch (Exception exception)
        {
            if (result != null)
            {
                result.dataQuality = AppendDataQuality(
                    result.dataQuality,
                    "Profiler state restore failed after .raw export: " + exception.Message);
            }
        }

        if (result == null || string.IsNullOrWhiteSpace(state.outputPath))
            return;

        try
        {
            FileInfo fileInfo = new FileInfo(state.outputPath);
            if (fileInfo.Exists && fileInfo.Length > 0L)
            {
                result.profilerRawExported = true;
                result.profilerRawPath = state.outputPath;
                result.profilerRawFileSizeBytes = fileInfo.Length;
                return;
            }

            result.profilerRawExported = false;
            result.profilerRawFileSizeBytes = fileInfo.Exists ? fileInfo.Length : -1L;
            result.dataQuality = AppendDataQuality(
                result.dataQuality,
                "Profiler .raw export finished, but no readable .raw file was found.");
        }
        catch (Exception exception)
        {
            result.dataQuality = AppendDataQuality(
                result.dataQuality,
                "Profiler .raw export file check failed: " + exception.Message);
        }
    }

    private IEnumerator WaitForRenderDocCompletion(string captureLabel, PerformancePoiSampleResult result)
    {
        int waitFrames = Mathf.Max(1, _renderDocCompletionWaitFrames);
        int frameCount = 0;

        while (_pendingCaptureLabels.Contains(captureLabel) && frameCount < waitFrames)
        {
            frameCount++;
            yield return null;
        }

        if (_pendingCaptureLabels.Contains(captureLabel))
        {
            _pendingCaptureLabels.Remove(captureLabel);
            result.dataQuality = AppendDataQuality(
                result.dataQuality,
                string.Format(CultureInfo.InvariantCulture, "等待 RenderDoc 抓帧完成超时（{0} frames）。", waitFrames));
        }
    }

    private void FillMetricSummaries(
        PerformancePoiSampleResult result,
        List<float> frameTimes,
        List<float> cpuFrameTimes,
        List<float> gpuFrameTimes,
        List<float> mainThreadTimes,
        List<float> renderThreadTimes,
        List<CounterRecorder> counters)
    {
        SampleStats frameStats = ComputeStats(frameTimes);
        result.averageFrameMs = frameStats.average;
        result.p95FrameMs = frameStats.p95;
        result.p99FrameMs = frameStats.p99;
        result.minFrameMs = frameStats.min;
        result.maxFrameMs = frameStats.max;

        SampleStats cpuStats = ComputeStats(cpuFrameTimes);
        result.averageCpuFrameMs = cpuFrameTimes.Count > 0 ? cpuStats.average : -1.0f;

        SampleStats gpuStats = ComputeStats(gpuFrameTimes);
        result.averageGpuFrameMs = gpuFrameTimes.Count > 0 ? gpuStats.average : -1.0f;
        if (gpuFrameTimes.Count == 0)
            result.dataQuality = AppendDataQuality(result.dataQuality, "当前环境没有返回有效 GPU frame timing。");

        SampleStats mainThreadStats = ComputeStats(mainThreadTimes);
        result.averageMainThreadMs = mainThreadTimes.Count > 0 ? mainThreadStats.average : -1.0f;
        if (mainThreadTimes.Count == 0)
            result.dataQuality = AppendDataQuality(result.dataQuality, "当前环境没有返回有效 Game Thread timing。");

        SampleStats renderThreadStats = ComputeStats(renderThreadTimes);
        result.averageRenderThreadMs = renderThreadTimes.Count > 0 ? renderThreadStats.average : -1.0f;
        if (renderThreadTimes.Count == 0)
            result.dataQuality = AppendDataQuality(result.dataQuality, "当前环境没有返回有效 Render Thread timing。");

        for (int counterIndex = 0; counterIndex < counters.Count; counterIndex++)
        {
            CounterRecorder counter = counters[counterIndex];
            switch (counter.Kind)
            {
                case CounterKind.Batches:
                    result.averageBatches = counter.GetAverage();
                    break;
                case CounterKind.SetPass:
                    result.averageSetPassCalls = counter.GetAverage();
                    break;
                case CounterKind.DrawCalls:
                    result.averageDrawCalls = counter.GetAverage();
                    break;
            }
        }
    }

    private void CollectFrameTiming(
        List<float> cpuFrameTimes,
        List<float> gpuFrameTimes,
        List<float> mainThreadTimes,
        List<float> renderThreadTimes)
    {
        FrameTimingManager.CaptureFrameTimings();
        uint timingCount = FrameTimingManager.GetLatestTimings(1, _frameTimingBuffer);
        if (timingCount == 0)
            return;

        FrameTiming timing = _frameTimingBuffer[0];
        if (timing.cpuFrameTime > 0.0)
            cpuFrameTimes.Add((float)timing.cpuFrameTime);

        if (timing.gpuFrameTime > 0.0)
            gpuFrameTimes.Add((float)timing.gpuFrameTime);

        if (timing.cpuMainThreadFrameTime > 0.0)
            mainThreadTimes.Add((float)timing.cpuMainThreadFrameTime);

        if (timing.cpuRenderThreadFrameTime > 0.0)
            renderThreadTimes.Add((float)timing.cpuRenderThreadFrameTime);
    }

    private static List<CounterRecorder> CreateCounterRecorders()
    {
        return new List<CounterRecorder>
        {
            new CounterRecorder(CounterKind.Batches, "Batches", ProfilerCategory.Render, "Batches Count", 1.0),
            new CounterRecorder(CounterKind.SetPass, "SetPass", ProfilerCategory.Render, "SetPass Calls Count", 1.0),
            new CounterRecorder(CounterKind.DrawCalls, "DrawCalls", ProfilerCategory.Render, "Draw Calls Count", 1.0)
        };
    }

    private static void DisposeCounterRecorders(List<CounterRecorder> counters)
    {
        if (counters == null)
            return;

        for (int index = 0; index < counters.Count; index++)
        {
            CounterRecorder counter = counters[index];
            if (counter != null)
                counter.Dispose();
        }
    }

    private static SampleStats ComputeStats(List<float> samples)
    {
        if (samples == null || samples.Count == 0)
            return default;

        List<float> sorted = new List<float>(samples);
        sorted.Sort();

        float total = 0.0f;
        for (int index = 0; index < samples.Count; index++)
            total += samples[index];

        int lastIndex = sorted.Count - 1;
        int p95Index = Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.95f) - 1, 0, lastIndex);
        int p99Index = Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.99f) - 1, 0, lastIndex);

        return new SampleStats
        {
            average = total / samples.Count,
            p95 = sorted[p95Index],
            p99 = sorted[p99Index],
            min = sorted[0],
            max = sorted[lastIndex]
        };
    }

    private void ResolveBindings()
    {
        if (_qianxiaCameraController == null)
            _qianxiaCameraController = FindFirstObjectByType<QianxiaGenshinCameraController>();

        if (_targetCamera == null && _qianxiaCameraController != null && _qianxiaCameraController.ControlledCamera != null)
            _targetCamera = _qianxiaCameraController.ControlledCamera;

        if (_targetCamera == null)
            _targetCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

        if (_targetCharacterController == null)
            _targetCharacterController = FindFirstObjectByType<CharacterController>();

        if (_targetCharacterTransform == null && _targetCharacterController != null)
            _targetCharacterTransform = _targetCharacterController.transform;

        if (_targetCharacterTransform == null)
        {
            QianxiaGenshinCharacterController qianxiaCharacterController = FindFirstObjectByType<QianxiaGenshinCharacterController>();
            if (qianxiaCharacterController != null)
                _targetCharacterTransform = qianxiaCharacterController.transform;
        }

        if (_targetCharacterTransform == null && _qianxiaCameraController != null && _qianxiaCameraController.FollowTarget != null)
            _targetCharacterTransform = _qianxiaCameraController.FollowTarget;

        FpsCharacterController fpsCharacterController = FindFirstObjectByType<FpsCharacterController>();
        if (_targetCharacterTransform == null && fpsCharacterController != null)
            _targetCharacterTransform = fpsCharacterController.transform;

        if (_targetCharacterController == null && fpsCharacterController != null)
            _targetCharacterController = fpsCharacterController.GetComponent<CharacterController>();

        if (_targetCamera == null && fpsCharacterController != null)
            _targetCamera = fpsCharacterController.GetComponentInChildren<Camera>();

        if (_targetCharacterTransform == null && _targetCamera != null)
            _targetCharacterTransform = ResolveCharacterTransformFromCameraHierarchy(_targetCamera.transform);

        if (_targetCharacterTransform == null && _targetCamera != null)
            _targetCharacterTransform = _targetCamera.transform;

        if (_squadCommandController == null)
            _squadCommandController = FindFirstObjectByType<CrowdVatSquadCommandController>();
    }

    private void PrimeReplayControllerBindings(PerformancePoiReplayController replayController)
    {
        if (replayController == null)
            return;

        QianxiaGenshinCharacterController qianxiaCharacterController = FindFirstObjectByType<QianxiaGenshinCharacterController>();
        replayController.OverrideBindings(
            _targetCharacterTransform,
            _targetCharacterController,
            qianxiaCharacterController,
            _targetCamera,
            _qianxiaCameraController);
    }

    private static Transform ResolveCharacterTransformFromCameraHierarchy(Transform cameraTransform)
    {
        Transform current = cameraTransform;
        while (current != null)
        {
            if (current.GetComponent<CharacterController>() != null ||
                current.GetComponent<QianxiaGenshinCharacterController>() != null ||
                current.GetComponent<FpsCharacterController>() != null)
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private List<PerformancePoiPoint> CollectPoints()
    {
        PerformancePoiPoint[] allPoints = FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        List<PerformancePoiPoint> points = new List<PerformancePoiPoint>(allPoints.Length);
        for (int index = 0; index < allPoints.Length; index++)
        {
            PerformancePoiPoint point = allPoints[index];
            if (point == null || !point.IncludeInAutomaticRuns || !point.gameObject.activeInHierarchy)
                continue;

            points.Add(point);
        }

        points.Sort(ComparePoints);
        return points;
    }

    private static int ComparePoints(PerformancePoiPoint left, PerformancePoiPoint right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        int sortOrder = left.SortIndex.CompareTo(right.SortIndex);
        if (sortOrder != 0)
            return sortOrder;

        return string.Compare(left.PoiId, right.PoiId, StringComparison.OrdinalIgnoreCase);
    }

    private void SubscribeRenderDocCaptureController()
    {
        _renderDocCaptureController = FindFirstObjectByType<RenderDocCaptureController>();
        if (_renderDocCaptureController != null)
            _renderDocCaptureController.CaptureCompleted += OnRenderDocCaptureCompleted;
    }

    private void CleanupAfterRun()
    {
        if (_renderDocCaptureController != null)
            _renderDocCaptureController.CaptureCompleted -= OnRenderDocCaptureCompleted;

        if (_poiReplayController != null)
            _poiReplayController.CancelActiveSession();

        _renderDocCaptureController = null;
        _captureLabelToResultIndex.Clear();
        _pendingCaptureLabels.Clear();
        ApplyControllerOverrides(enable: false);
    }

    private void OnRenderDocCaptureCompleted(RenderDocCaptureManifest manifest)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.label))
            return;

        if (!_captureLabelToResultIndex.TryGetValue(manifest.label, out int resultIndex))
            return;

        if (resultIndex < 0 || resultIndex >= _results.Count)
            return;

        PerformancePoiSampleResult result = _results[resultIndex];
        result.renderDocCapturePath = manifest.captureFilePath;
        result.renderDocScreenshotPath = manifest.screenshotFilePath;
        _pendingCaptureLabels.Remove(manifest.label);
    }

    private void ApplyControllerOverrides(bool enable)
    {
        if (enable)
        {
            if (_disableQianxiaCameraControllerDuringRun && _qianxiaCameraController != null)
            {
                _cameraControllerWasEnabled = _qianxiaCameraController.enabled;
                _qianxiaCameraController.enabled = false;
            }

            if (_disableSquadCommandControllerDuringRun && _squadCommandController != null)
            {
                _squadCommandControllerWasEnabled = _squadCommandController.enabled;
                _squadCommandController.enabled = false;
            }

            return;
        }

        if (_disableQianxiaCameraControllerDuringRun && _qianxiaCameraController != null)
            _qianxiaCameraController.enabled = _cameraControllerWasEnabled;

        if (_disableSquadCommandControllerDuringRun && _squadCommandController != null)
            _squadCommandController.enabled = _squadCommandControllerWasEnabled;
    }

    private bool ResolveAutoQuitPlayerWhenFinished()
    {
        return _runtimeAutoQuitOverride ?? _autoQuitPlayerWhenFinished;
    }

    private static string AppendDataQuality(string existingValue, string nextValue)
    {
        if (string.IsNullOrWhiteSpace(nextValue))
            return existingValue ?? string.Empty;

        if (string.IsNullOrWhiteSpace(existingValue))
            return nextValue.Trim();

        return existingValue.TrimEnd() + " | " + nextValue.Trim();
    }

    private static bool CaptureCameraImage(Camera camera, string screenshotPath, int requestedWidth, int requestedHeight)
    {
        if (camera == null || string.IsNullOrWhiteSpace(screenshotPath))
            return false;

        const int minimumDimension = 64;
        int width = Mathf.Max(minimumDimension, requestedWidth);
        int height = Mathf.Max(minimumDimension, requestedHeight);

        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        Texture2D texture = null;

        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture.active = renderTexture;
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply(false, false);

            byte[] pngBytes = texture.EncodeToPNG();
            File.WriteAllBytes(screenshotPath, pngBytes);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[PerformancePoiSampler] Failed to capture screenshot: " + exception.Message);
            return false;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);

            if (texture != null)
                Destroy(texture);
        }
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    private void HandleEditorTraceRecordingHotkeys()
    {
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!shiftHeld)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            StartEditorSelectedPoiTraceRecording();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
            StopEditorSelectedPoiTraceRecordingAndSave();
    }

    private void StartEditorSelectedPoiTraceRecording()
    {
        if (!TryResolveEditorSelectedTracePoi(out PerformancePoiPoint point, out string failureMessage))
        {
            _statusMessage = failureMessage;
            Debug.LogWarning("[PerformancePoiSampler] " + failureMessage, this);
            return;
        }

        if (!point.RequiresReplayTrace)
        {
            _statusMessage = "The selected POI uses TurntableSweep mode and does not need a recorded replay trace.";
            Debug.LogWarning("[PerformancePoiSampler] " + _statusMessage, point);
            return;
        }

        PerformancePoiReplayController replayController = EnsureReplayController();
        if (replayController == null)
        {
            _statusMessage = "未能创建 POI replay controller。";
            return;
        }

        ResolveBindings();
        PrimeReplayControllerBindings(replayController);

        if (!replayController.StartTraceRecording())
        {
            _statusMessage = replayController.StatusMessage + " | " + replayController.DescribeBindings();
            Debug.LogWarning("[PerformancePoiSampler] " + _statusMessage, this);
            return;
        }

        _statusMessage = string.Format(
            CultureInfo.InvariantCulture,
            "已开始录制 POI 轨迹：{0}。完成后按 Shift+2 保存。",
            point.PoiId);
        Debug.Log("[PerformancePoiSampler] " + _statusMessage, point);
    }

    private void StopEditorSelectedPoiTraceRecordingAndSave()
    {
        PerformancePoiReplayController replayController = EnsureReplayController();
        if (replayController == null || !replayController.IsRecordingTrace)
        {
            _statusMessage = "当前没有正在录制的 POI 轨迹。";
            return;
        }

        if (!TryResolveEditorSelectedTracePoi(out PerformancePoiPoint point, out string failureMessage))
        {
            _statusMessage = failureMessage;
            Debug.LogWarning("[PerformancePoiSampler] " + failureMessage, this);
            return;
        }

        string tracePath = point.BuildReplayTracePath();
        if (!replayController.StopTraceRecordingAndSaveToPath(tracePath))
        {
            _statusMessage = replayController.StatusMessage;
            Debug.LogWarning("[PerformancePoiSampler] " + replayController.StatusMessage, this);
            return;
        }

        if (!point.TryDeleteObsoleteReplayTraces(string.Empty, out string deletedTracePaths, out string deleteErrorMessage))
        {
            _statusMessage = "POI trace saved, but obsolete legacy trace cleanup failed: " + deleteErrorMessage;
            Debug.LogWarning("[PerformancePoiSampler] " + _statusMessage, point);
            return;
        }

        _statusMessage = string.Format(
            CultureInfo.InvariantCulture,
            "已保存 POI 轨迹：{0} -> {1}",
            point.PoiId,
            tracePath);
        if (!string.IsNullOrWhiteSpace(deletedTracePaths))
            _statusMessage += " | deleted legacy trace: " + deletedTracePaths;
        Debug.Log("[PerformancePoiSampler] " + _statusMessage, point);
    }

    private static bool TryResolveEditorSelectedTracePoi(out PerformancePoiPoint point, out string failureMessage)
    {
        point = null;
        failureMessage = string.Empty;

        string poiKey = PlayerPrefs.GetString(PerformancePoiSamplingArtifacts.EditorSelectedPoiKeyPrefsKey, string.Empty);
        string poiId = PlayerPrefs.GetString(PerformancePoiSamplingArtifacts.EditorSelectedPoiIdPrefsKey, string.Empty);
        string scenePath = PlayerPrefs.GetString(PerformancePoiSamplingArtifacts.EditorSelectedPoiScenePathPrefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(poiKey) && string.IsNullOrWhiteSpace(poiId))
        {
            failureMessage = "POI Sampler 工具里还没有选中要录制的 POI。";
            return false;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrWhiteSpace(scenePath) &&
            !string.Equals(scenePath, activeScene.path, StringComparison.Ordinal))
        {
            failureMessage = string.Format(
                CultureInfo.InvariantCulture,
                "当前运行场景与工具里选中的 POI 不一致。目标场景：{0}，当前场景：{1}",
                scenePath,
                activeScene.path);
            return false;
        }

        PerformancePoiPoint[] points = FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiPoint candidate = points[index];
            if (candidate == null)
                continue;

            if (!string.IsNullOrWhiteSpace(poiKey) &&
                string.Equals(candidate.PoiKey, poiKey, StringComparison.OrdinalIgnoreCase))
            {
                point = candidate;
                return true;
            }

            if (string.IsNullOrWhiteSpace(poiId) ||
                !string.Equals(candidate.PoiId, poiId, StringComparison.OrdinalIgnoreCase))
                continue;

            point = candidate;
            return true;
        }

        failureMessage = "当前场景中没有找到工具里选中的 POI： " + poiId;
        return false;
    }
}

[Serializable]
public sealed class PerformancePoiReplayTrace
{
    public int version = 1;
    public string sessionName = string.Empty;
    public string scenePath = string.Empty;
    public float duration;
    public List<PerformancePoiReplayPoseSample> poseSamples = new List<PerformancePoiReplayPoseSample>(1024);
}

[Serializable]
public struct PerformancePoiReplayPoseSample
{
    public float time;
    public Vector3 characterPosition;
    public Quaternion characterRotation;
    public Vector3 cameraPosition;
    public Quaternion cameraRotation;
}

public enum PerformancePoiReplayMode
{
    Idle = 0,
    RecordingTrace = 1,
    ReplayingTrace = 2
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(11000)]
public sealed class PerformancePoiReplayController : MonoBehaviour
{
    private const float ReplayTimeEpsilon = 0.0001f;

    private readonly List<PerformancePoiReplayPoseSample> _recordedPoseSamples =
        new List<PerformancePoiReplayPoseSample>(2048);

    private CharacterController _characterController;
    private Transform _characterTransform;
    private QianxiaGenshinCharacterController _characterMovementController;
    private QianxiaGenshinCameraController _cameraController;
    private Camera _targetCamera;
    private PerformancePoiReplayTrace _activeTrace;
    private PerformancePoiReplayMode _mode;
    private string _statusMessage = "Idle";
    private string _lastTracePath = string.Empty;
    private string _activeTracePath = string.Empty;
    private float _recordingStartTime;
    private float _replayStartTime;
    private int _replayPoseIndex;
    private bool _inputSuppressed;
    private bool _previousCharacterControllerEnabled;
    private bool _previousCharacterMovementEnabled;
    private bool _previousCameraControllerEnabled;
    private bool _usingCameraTransformAsCharacterProxy;

    public PerformancePoiReplayMode Mode => _mode;
    public bool IsRecordingTrace => _mode == PerformancePoiReplayMode.RecordingTrace;
    public bool IsReplayingTrace => _mode == PerformancePoiReplayMode.ReplayingTrace;
    public string StatusMessage => _statusMessage;
    public string LastTracePath => _lastTracePath;
    public string ActiveTracePath => _activeTracePath;
    public float ActiveReplayDuration => _activeTrace != null ? Mathf.Max(0.0f, _activeTrace.duration) : 0.0f;

    public static PerformancePoiReplayController GetOrCreate(GameObject host)
    {
        if (host == null)
            return null;

        PerformancePoiReplayController controller = host.GetComponent<PerformancePoiReplayController>();
        if (controller == null)
            controller = host.AddComponent<PerformancePoiReplayController>();

        controller.hideFlags = HideFlags.HideInInspector | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        return controller;
    }

    public void OverrideBindings(
        Transform characterTransform,
        CharacterController characterController,
        QianxiaGenshinCharacterController characterMovementController,
        Camera targetCamera,
        QianxiaGenshinCameraController cameraController)
    {
        if (characterTransform != null)
            _characterTransform = characterTransform;

        if (characterController != null)
            _characterController = characterController;

        if (characterMovementController != null)
            _characterMovementController = characterMovementController;

        if (targetCamera != null)
            _targetCamera = targetCamera;

        if (cameraController != null)
            _cameraController = cameraController;
    }

    public string DescribeBindings()
    {
        string characterName = _characterTransform != null ? _characterTransform.name : "<null>";
        string controllerName = _characterController != null ? _characterController.name : "<null>";
        string movementControllerName = _characterMovementController != null ? _characterMovementController.name : "<null>";
        string cameraName = _targetCamera != null ? _targetCamera.name : "<null>";
        string cameraControllerName = _cameraController != null ? _cameraController.name : "<null>";
        return string.Format(
            CultureInfo.InvariantCulture,
            "Bindings: character={0}, characterController={1}, movementController={2}, camera={3}, cameraController={4}, mode={5}.",
            characterName,
            controllerName,
            movementControllerName,
            cameraName,
            cameraControllerName,
            _mode);
    }

    public bool StartTraceRecording()
    {
        if (!Application.isPlaying)
        {
            _statusMessage = "Cannot start POI trace recording outside Play Mode.";
            return false;
        }

        if (_mode != PerformancePoiReplayMode.Idle)
        {
            _statusMessage = "Cannot start POI trace recording while another POI trace session is active.";
            return false;
        }

        ResolveBindings();
        if (_targetCamera == null)
        {
            _statusMessage = BuildMissingBindingStatus("Cannot start POI trace recording");
            return false;
        }

        _recordedPoseSamples.Clear();
        _activeTrace = null;
        _activeTracePath = string.Empty;
        _recordingStartTime = Time.realtimeSinceStartup;
        _mode = PerformancePoiReplayMode.RecordingTrace;

        CapturePoseSample(0.0f);
        _statusMessage = "POI trace recording started.";
        return true;
    }

    public bool StopTraceRecordingAndSaveToPath(string tracePath)
    {
        if (_mode != PerformancePoiReplayMode.RecordingTrace)
            return false;

        CapturePoseSample(GetRecordingElapsedTime());

        PerformancePoiReplayTrace trace = BuildTraceFromRecordedSamples();
        string savedTracePath = SaveTraceToPath(tracePath, trace);

        _activeTrace = trace;
        _activeTracePath = savedTracePath;
        _lastTracePath = savedTracePath;
        _mode = PerformancePoiReplayMode.Idle;
        _statusMessage = "POI trace saved: " + savedTracePath;
        return true;
    }

    public void DiscardTraceRecording()
    {
        if (_mode != PerformancePoiReplayMode.RecordingTrace)
            return;

        _recordedPoseSamples.Clear();
        _activeTrace = null;
        _activeTracePath = string.Empty;
        _mode = PerformancePoiReplayMode.Idle;
        _statusMessage = "POI trace recording discarded.";
    }

    public bool StartReplay(string tracePath)
    {
        if (string.IsNullOrWhiteSpace(tracePath) || !File.Exists(tracePath))
        {
            _statusMessage = "Cannot start POI trace replay because the trace file does not exist.";
            return false;
        }

        PerformancePoiReplayTrace trace = LoadTrace(tracePath);
        return StartReplay(trace, tracePath);
    }

    public bool StartReplay(PerformancePoiReplayTrace trace, string traceIdentifier)
    {
        if (!Application.isPlaying)
        {
            _statusMessage = "Cannot start POI trace replay outside Play Mode.";
            return false;
        }

        if (_mode != PerformancePoiReplayMode.Idle)
        {
            _statusMessage = "Cannot start POI trace replay while another POI trace session is active.";
            return false;
        }

        ResolveBindings();
        if (_targetCamera == null)
        {
            _statusMessage = BuildMissingBindingStatus("Cannot start POI trace replay");
            return false;
        }

        if (trace == null || trace.poseSamples == null || trace.poseSamples.Count == 0)
        {
            _statusMessage = "Cannot start POI trace replay because the trace is empty or invalid.";
            return false;
        }

        _activeTrace = trace;
        _activeTracePath = traceIdentifier ?? string.Empty;
        _lastTracePath = _activeTracePath;
        _replayPoseIndex = 0;
        _replayStartTime = Time.realtimeSinceStartup;
        _mode = PerformancePoiReplayMode.ReplayingTrace;

        SuppressRuntimeInput(true);
        ApplyPoseAtTime(0.0f);

        Scene currentScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(trace.scenePath) &&
            !string.Equals(trace.scenePath, currentScene.path, StringComparison.Ordinal))
        {
            _statusMessage = "POI trace replay started, but the current scene does not match the recorded scene.";
        }
        else
        {
            _statusMessage = "POI trace replay started.";
        }

        return true;
    }

    public void CancelActiveSession()
    {
        if (_mode == PerformancePoiReplayMode.RecordingTrace)
        {
            DiscardTraceRecording();
            return;
        }

        if (_mode == PerformancePoiReplayMode.ReplayingTrace)
            FinishReplay(false);
    }

    private void OnDisable()
    {
        if (_mode == PerformancePoiReplayMode.RecordingTrace)
        {
            _recordedPoseSamples.Clear();
            _mode = PerformancePoiReplayMode.Idle;
            _statusMessage = "POI trace recording stopped.";
        }

        if (_mode == PerformancePoiReplayMode.ReplayingTrace)
            FinishReplay(false);
    }

    private void LateUpdate()
    {
        ResolveBindings();

        if (_mode == PerformancePoiReplayMode.RecordingTrace)
        {
            CapturePoseSample(GetRecordingElapsedTime());
            return;
        }

        if (_mode == PerformancePoiReplayMode.ReplayingTrace)
            UpdateReplay();
    }

    private void UpdateReplay()
    {
        if (_activeTrace == null || _activeTrace.poseSamples == null || _activeTrace.poseSamples.Count == 0)
        {
            FinishReplay(false);
            _statusMessage = "POI trace replay stopped because the active trace is empty.";
            return;
        }

        float elapsed = Mathf.Max(0.0f, Time.realtimeSinceStartup - _replayStartTime);
        float clampedTime = Mathf.Min(elapsed, Mathf.Max(_activeTrace.duration, 0.0f));
        ApplyPoseAtTime(clampedTime);

        if (elapsed + ReplayTimeEpsilon < _activeTrace.duration)
            return;

        FinishReplay(true);
    }

    private void FinishReplay(bool completed)
    {
        SuppressRuntimeInput(false);
        _mode = PerformancePoiReplayMode.Idle;
        _statusMessage = completed ? "POI trace replay completed." : "POI trace replay cancelled.";
    }

    private void ResolveBindings()
    {
        _usingCameraTransformAsCharacterProxy = false;

        if (_characterMovementController == null)
            _characterMovementController = FindFirstObjectByType<QianxiaGenshinCharacterController>();

        if (_characterController == null && _characterMovementController != null)
            _characterController = _characterMovementController.GetComponent<CharacterController>();

        if (_characterController == null)
            _characterController = FindFirstObjectByType<CharacterController>();

        if (_characterTransform == null && _characterMovementController != null)
            _characterTransform = _characterMovementController.transform;

        if (_characterTransform == null && _characterController != null)
            _characterTransform = _characterController.transform;

        if (_cameraController == null)
            _cameraController = FindFirstObjectByType<QianxiaGenshinCameraController>();

        if (_cameraController != null)
        {
            if (_characterTransform == null && _cameraController.FollowTarget != null)
                _characterTransform = _cameraController.FollowTarget;

            if (_targetCamera == null && _cameraController.ControlledCamera != null)
                _targetCamera = _cameraController.ControlledCamera;
        }

        FpsCharacterController fpsCharacterController = FindFirstObjectByType<FpsCharacterController>();
        if (_characterTransform == null && fpsCharacterController != null)
            _characterTransform = fpsCharacterController.transform;

        if (_characterController == null && fpsCharacterController != null)
            _characterController = fpsCharacterController.GetComponent<CharacterController>();

        if (_targetCamera == null && fpsCharacterController != null)
            _targetCamera = fpsCharacterController.GetComponentInChildren<Camera>();

        if (_targetCamera == null)
            _targetCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

        if (_characterTransform == null && _targetCamera != null)
            _characterTransform = ResolveCharacterTransformFromCameraHierarchy(_targetCamera.transform);

        if (_characterTransform == null && _targetCamera != null)
        {
            _characterTransform = _targetCamera.transform;
            _usingCameraTransformAsCharacterProxy = true;
        }
    }

    private string BuildMissingBindingStatus(string operationLabel)
    {
        bool missingCharacter = _characterTransform == null && !_usingCameraTransformAsCharacterProxy;
        bool missingCamera = _targetCamera == null;

        if (!missingCharacter && !missingCamera)
            return operationLabel + " failed for an unknown reason.";

        if (missingCharacter && missingCamera)
            return operationLabel + " because neither the character transform nor the target camera could be resolved. Wait until the player finishes spawning, or make sure the scene has an active player and camera controller.";

        if (missingCharacter)
            return operationLabel + " because the character transform could not be resolved. Make sure the active camera controller is following the player, or that the player CharacterController/QianxiaGenshinCharacterController has spawned.";

        return operationLabel + " because the target camera could not be resolved. Make sure the scene has an active Camera or QianxiaGenshinCameraController.";
    }

    private static Transform ResolveCharacterTransformFromCameraHierarchy(Transform cameraTransform)
    {
        Transform current = cameraTransform;
        while (current != null)
        {
            if (current.GetComponent<CharacterController>() != null ||
                current.GetComponent<QianxiaGenshinCharacterController>() != null ||
                current.GetComponent<FpsCharacterController>() != null)
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private float GetRecordingElapsedTime()
    {
        return Mathf.Max(0.0f, Time.realtimeSinceStartup - _recordingStartTime);
    }

    private void CapturePoseSample(float sampleTime)
    {
        if (_characterTransform == null || _targetCamera == null)
            return;

        PerformancePoiReplayPoseSample sample = new PerformancePoiReplayPoseSample
        {
            time = Mathf.Max(0.0f, sampleTime),
            characterPosition = _characterTransform.position,
            characterRotation = _characterTransform.rotation,
            cameraPosition = _targetCamera.transform.position,
            cameraRotation = _targetCamera.transform.rotation
        };

        if (_recordedPoseSamples.Count > 0)
        {
            PerformancePoiReplayPoseSample previousSample = _recordedPoseSamples[_recordedPoseSamples.Count - 1];
            if (sample.time <= previousSample.time + ReplayTimeEpsilon)
                return;
        }

        _recordedPoseSamples.Add(sample);
    }

    private PerformancePoiReplayTrace BuildTraceFromRecordedSamples()
    {
        PerformancePoiReplayTrace trace = new PerformancePoiReplayTrace
        {
            sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture),
            scenePath = SceneManager.GetActiveScene().path,
            duration = _recordedPoseSamples.Count > 0 ? _recordedPoseSamples[_recordedPoseSamples.Count - 1].time : 0.0f
        };

        trace.poseSamples.AddRange(_recordedPoseSamples);
        return trace;
    }

    private void ApplyPoseAtTime(float replayTime)
    {
        List<PerformancePoiReplayPoseSample> poseSamples = _activeTrace.poseSamples;
        if (poseSamples.Count == 1)
        {
            ApplyPoseSample(poseSamples[0]);
            return;
        }

        while (_replayPoseIndex + 1 < poseSamples.Count &&
               poseSamples[_replayPoseIndex + 1].time < replayTime)
        {
            _replayPoseIndex++;
        }

        int nextIndex = Mathf.Min(_replayPoseIndex + 1, poseSamples.Count - 1);
        PerformancePoiReplayPoseSample fromSample = poseSamples[_replayPoseIndex];
        PerformancePoiReplayPoseSample toSample = poseSamples[nextIndex];
        float duration = Mathf.Max(toSample.time - fromSample.time, ReplayTimeEpsilon);
        float interpolation = Mathf.Clamp01((replayTime - fromSample.time) / duration);

        Vector3 characterPosition = Vector3.Lerp(fromSample.characterPosition, toSample.characterPosition, interpolation);
        Quaternion characterRotation = Quaternion.Slerp(fromSample.characterRotation, toSample.characterRotation, interpolation);
        Vector3 cameraPosition = Vector3.Lerp(fromSample.cameraPosition, toSample.cameraPosition, interpolation);
        Quaternion cameraRotation = Quaternion.Slerp(fromSample.cameraRotation, toSample.cameraRotation, interpolation);

        if (_characterTransform != null)
            _characterTransform.SetPositionAndRotation(characterPosition, characterRotation);

        if (_targetCamera != null)
            _targetCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
    }

    private void ApplyPoseSample(PerformancePoiReplayPoseSample sample)
    {
        if (_characterTransform != null)
            _characterTransform.SetPositionAndRotation(sample.characterPosition, sample.characterRotation);

        if (_targetCamera != null)
            _targetCamera.transform.SetPositionAndRotation(sample.cameraPosition, sample.cameraRotation);
    }

    private void SuppressRuntimeInput(bool suppress)
    {
        if (_inputSuppressed == suppress)
            return;

        ResolveBindings();

        if (suppress)
        {
            _previousCharacterMovementEnabled = _characterMovementController != null && _characterMovementController.enabled;
            _previousCameraControllerEnabled = _cameraController != null && _cameraController.enabled;
            _previousCharacterControllerEnabled = _characterController != null && _characterController.enabled;

            if (_characterMovementController != null)
                _characterMovementController.enabled = false;
            if (_cameraController != null)
                _cameraController.enabled = false;
            if (_characterController != null)
                _characterController.enabled = false;
        }
        else
        {
            if (_characterController != null)
                _characterController.enabled = _previousCharacterControllerEnabled;
            if (_characterMovementController != null)
                _characterMovementController.enabled = _previousCharacterMovementEnabled;
            if (_cameraController != null)
                _cameraController.enabled = _previousCameraControllerEnabled;
        }

        _inputSuppressed = suppress;
    }

    public static string SerializeTrace(PerformancePoiReplayTrace trace)
    {
        if (trace == null)
            throw new ArgumentNullException(nameof(trace));

        return JsonUtility.ToJson(trace, true);
    }

    public static PerformancePoiReplayTrace DeserializeTrace(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonUtility.FromJson<PerformancePoiReplayTrace>(json);
    }

    public static PerformancePoiReplayTrace LoadTrace(string tracePath)
    {
        string json = File.ReadAllText(tracePath);
        return DeserializeTrace(json);
    }

    public static string SaveTraceToPath(string tracePath, PerformancePoiReplayTrace trace)
    {
        if (string.IsNullOrWhiteSpace(tracePath))
            throw new ArgumentException("Trace path cannot be empty.", nameof(tracePath));

        if (trace == null)
            throw new ArgumentNullException(nameof(trace));

        string fullTracePath = Path.GetFullPath(tracePath);
        string directory = Path.GetDirectoryName(fullTracePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullTracePath, SerializeTrace(trace));
        return fullTracePath;
    }
}
