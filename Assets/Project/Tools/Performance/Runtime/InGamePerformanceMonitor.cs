using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

public static class InGamePerformanceMonitorBootstrap
{
    public const string EnableArg = "--in-game-perf-monitor";
    public const string DisableArg = "--disable-in-game-perf-monitor";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMonitor()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (HasArgument(args, DisableArg))
            return;

        if (!Application.isEditor || HasArgument(args, EnableArg))
            InGamePerformanceMonitor.GetOrCreate();
    }

    private static bool HasArgument(string[] args, string argumentName)
    {
        if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(argumentName))
            return false;

        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], argumentName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

[DisallowMultipleComponent]
public sealed class InGamePerformanceMonitor : MonoBehaviour
{
    private const int FrameSampleCapacity = 120;
    private const float DisplayRefreshIntervalSeconds = 0.25f;
    private const float Target60FpsFrameMs = 16.67f;
    private const float Target40FpsFrameMs = 25.0f;

    private static readonly Color BarColor = new Color(0.025f, 0.027f, 0.035f, 0.84f);
    private static readonly Color ChipColor = new Color(0.10f, 0.11f, 0.14f, 0.86f);
    private static readonly Color ChipBorderColor = new Color(1.0f, 1.0f, 1.0f, 0.10f);
    private static readonly Color TextColor = new Color(0.90f, 0.92f, 0.95f, 1.0f);
    private static readonly Color MutedTextColor = new Color(0.58f, 0.63f, 0.70f, 1.0f);
    private static readonly Color GoodColor = new Color(0.31f, 0.95f, 0.55f, 1.0f);
    private static readonly Color WarningColor = new Color(1.0f, 0.76f, 0.27f, 1.0f);
    private static readonly Color BadColor = new Color(1.0f, 0.32f, 0.28f, 1.0f);

    [SerializeField] private KeyCode _toggleKey = KeyCode.F9;
    [SerializeField] private bool _visible = true;

    private readonly FrameTiming[] _frameTimingBuffer = new FrameTiming[1];
    private readonly float[] _frameMsSamples = new float[FrameSampleCapacity];

    private ProfilerRecorder _batchRecorder;
    private ProfilerRecorder _drawCallRecorder;
    private ProfilerRecorder _setPassRecorder;
    private ProfilerRecorder _gcAllocatedInFrameRecorder;

    private UIDocument _uiDocument;
    private PanelSettings _panelSettings;
    private Font _runtimeFont;
    private bool _ownsUidocument;
    private bool _ownsPanelSettings;
    private VisualElement _bar;
    private VisualElement _chipRow;
    private Label _hintLabel;
    private MetricChip _titleChip;
    private MetricChip _fpsChip;
    private MetricChip _frameChip;
    private MetricChip _worstChip;
    private MetricChip _mainChip;
    private MetricChip _renderChip;
    private MetricChip _gpuChip;
    private MetricChip _drawChip;
    private MetricChip _batchesChip;
    private MetricChip _setPassChip;
    private MetricChip _gcChip;
    private MetricChip _memoryChip;

    private int _frameSampleCursor;
    private int _frameSampleCount;
    private float _refreshTimer;
    private float _averageFrameMs;
    private float _worstFrameMs;
    private float _fps;
    private float _cpuFrameMs = -1.0f;
    private float _gpuFrameMs = -1.0f;
    private float _mainThreadMs = -1.0f;
    private float _renderThreadMs = -1.0f;
    private long _batchCount = -1;
    private long _drawCallCount = -1;
    private long _setPassCount = -1;
    private long _gcAllocatedBytes = -1;
    private long _totalAllocatedBytes = -1;
    private long _monoUsedBytes = -1;
    private bool _legacyInputUnavailable;
    private bool _lastCompact;
    private bool _lastTiny;
    private int _lastScreenWidth;
    private int _lastScreenHeight;

    private sealed class MetricChip
    {
        public VisualElement Root;
        public Label Label;
        public Label Value;
        public bool FixedWidth;
    }

    public static InGamePerformanceMonitor GetOrCreate()
    {
        InGamePerformanceMonitor existing = FindFirstObjectByType<InGamePerformanceMonitor>();
        if (existing != null)
        {
            if (!Application.isEditor)
                DontDestroyOnLoad(existing.gameObject);

            return existing;
        }

        GameObject gameObject = new GameObject("InGamePerformanceMonitor (Auto)");
        DontDestroyOnLoad(gameObject);
        return gameObject.AddComponent<InGamePerformanceMonitor>();
    }

    private void OnEnable()
    {
        EnsureUi();
        SetUiVisible(_visible);
        _batchRecorder = StartRecorder(ProfilerCategory.Render, "Batches Count");
        _drawCallRecorder = StartRecorder(ProfilerCategory.Render, "Draw Calls Count");
        _setPassRecorder = StartRecorder(ProfilerCategory.Render, "SetPass Calls Count");
        _gcAllocatedInFrameRecorder = StartRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
    }

    private void OnDisable()
    {
        DisposeRecorder(ref _batchRecorder);
        DisposeRecorder(ref _drawCallRecorder);
        DisposeRecorder(ref _setPassRecorder);
        DisposeRecorder(ref _gcAllocatedInFrameRecorder);
        SetUiVisible(false);
    }

    private void Update()
    {
        EnsureUi();
        HandleToggleInput();

        float deltaSeconds = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        AddFrameSample(deltaSeconds * 1000.0f);
        CaptureFrameTiming();

        RefreshResponsiveLayoutIfNeeded();

        _refreshTimer += deltaSeconds;
        if (_refreshTimer < DisplayRefreshIntervalSeconds)
            return;

        _refreshTimer = 0.0f;
        RefreshDisplayMetrics();
        RefreshUiValues();
    }

    private void HandleToggleInput()
    {
        if (_legacyInputUnavailable || _toggleKey == KeyCode.None)
            return;

        try
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _visible = !_visible;
                SetUiVisible(_visible);
            }
        }
        catch (InvalidOperationException)
        {
            _legacyInputUnavailable = true;
        }
    }

    private void AddFrameSample(float frameMs)
    {
        _frameMsSamples[_frameSampleCursor] = frameMs;
        _frameSampleCursor = (_frameSampleCursor + 1) % FrameSampleCapacity;
        if (_frameSampleCount < FrameSampleCapacity)
            _frameSampleCount++;
    }

    private void CaptureFrameTiming()
    {
        FrameTimingManager.CaptureFrameTimings();
        uint timingCount = FrameTimingManager.GetLatestTimings(1, _frameTimingBuffer);
        if (timingCount == 0)
            return;

        FrameTiming timing = _frameTimingBuffer[0];
        if (timing.cpuFrameTime > 0.0)
            _cpuFrameMs = (float)timing.cpuFrameTime;

        if (timing.gpuFrameTime > 0.0)
            _gpuFrameMs = (float)timing.gpuFrameTime;

        if (timing.cpuMainThreadFrameTime > 0.0)
            _mainThreadMs = (float)timing.cpuMainThreadFrameTime;

        if (timing.cpuRenderThreadFrameTime > 0.0)
            _renderThreadMs = (float)timing.cpuRenderThreadFrameTime;
    }

    private void RefreshDisplayMetrics()
    {
        if (_frameSampleCount > 0)
        {
            float totalFrameMs = 0.0f;
            float worstFrameMs = 0.0f;
            for (int index = 0; index < _frameSampleCount; index++)
            {
                float frameMs = _frameMsSamples[index];
                totalFrameMs += frameMs;
                worstFrameMs = Mathf.Max(worstFrameMs, frameMs);
            }

            _averageFrameMs = totalFrameMs / _frameSampleCount;
            _worstFrameMs = worstFrameMs;
            _fps = _averageFrameMs > 0.0f ? 1000.0f / _averageFrameMs : 0.0f;
        }

        _batchCount = ReadRecorderValue(_batchRecorder);
        _drawCallCount = ReadRecorderValue(_drawCallRecorder);
        _setPassCount = ReadRecorderValue(_setPassRecorder);
        _gcAllocatedBytes = ReadRecorderValue(_gcAllocatedInFrameRecorder);
        _monoUsedBytes = Profiler.GetMonoUsedSizeLong();
        _totalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();

        if (_mainThreadMs <= 0.0f && _cpuFrameMs > 0.0f)
            _mainThreadMs = _cpuFrameMs;
    }

    private void EnsureUi()
    {
        if (_uiDocument == null)
        {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null)
            {
                _uiDocument = gameObject.AddComponent<UIDocument>();
                _ownsUidocument = true;
            }
        }

        if (_panelSettings == null)
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.hideFlags = HideFlags.HideAndDontSave;
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _ownsPanelSettings = true;
        }

        if (_uiDocument.panelSettings != _panelSettings)
            _uiDocument.panelSettings = _panelSettings;

        _uiDocument.sortingOrder = 1000;
        _uiDocument.enabled = true;

        if (_bar != null)
            return;

        VisualElement root = _uiDocument.rootVisualElement;
        root.Clear();
        root.pickingMode = PickingMode.Ignore;
        root.style.flexGrow = 1.0f;

        _bar = new VisualElement();
        _bar.name = "perf-monitor-bar";
        _bar.pickingMode = PickingMode.Ignore;
        _bar.style.position = Position.Absolute;
        _bar.style.left = 0.0f;
        _bar.style.right = 0.0f;
        _bar.style.bottom = 0.0f;
        _bar.style.flexDirection = FlexDirection.Row;
        _bar.style.alignItems = Align.Center;
        _bar.style.backgroundColor = BarColor;

        _chipRow = new VisualElement();
        _chipRow.style.flexDirection = FlexDirection.Row;
        _chipRow.style.alignItems = Align.Center;
        _chipRow.style.flexShrink = 1.0f;

        VisualElement spacer = new VisualElement();
        spacer.style.flexGrow = 1.0f;

        _hintLabel = new Label();
        _hintLabel.pickingMode = PickingMode.Ignore;
        _hintLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        _hintLabel.style.color = MutedTextColor;
        _hintLabel.style.whiteSpace = WhiteSpace.NoWrap;
        ApplyRuntimeFont(_hintLabel);
        _hintLabel.text = $"{_toggleKey} hide";

        _titleChip = CreateTitleChip("BUILD PERF");
        _fpsChip = CreateMetricChip("FPS");
        _frameChip = CreateMetricChip("Frame");
        _worstChip = CreateMetricChip("Worst");
        _mainChip = CreateMetricChip("Main");
        _renderChip = CreateMetricChip("Render");
        _gpuChip = CreateMetricChip("GPU");
        _drawChip = CreateMetricChip("Draw");
        _batchesChip = CreateMetricChip("Batches");
        _setPassChip = CreateMetricChip("SetPass");
        _gcChip = CreateMetricChip("GC/f");
        _memoryChip = CreateMetricChip("Mem");

        AddChip(_titleChip);
        AddChip(_fpsChip);
        AddChip(_frameChip);
        AddChip(_worstChip);
        AddChip(_mainChip);
        AddChip(_renderChip);
        AddChip(_gpuChip);
        AddChip(_drawChip);
        AddChip(_batchesChip);
        AddChip(_setPassChip);
        AddChip(_gcChip);
        AddChip(_memoryChip);

        _bar.Add(_chipRow);
        _bar.Add(spacer);
        _bar.Add(_hintLabel);
        root.Add(_bar);

        RefreshResponsiveLayout(force: true);
        RefreshUiValues();
    }

    private void AddChip(MetricChip chip)
    {
        _chipRow.Add(chip.Root);
    }

    private MetricChip CreateTitleChip(string title)
    {
        MetricChip chip = CreateMetricChip(title, isTitle: true);
        chip.FixedWidth = true;
        chip.Root.style.justifyContent = Justify.Center;
        chip.Label.style.flexGrow = 1.0f;
        chip.Label.style.unityTextAlign = TextAnchor.MiddleCenter;
        chip.Label.style.color = TextColor;
        chip.Root.style.borderBottomWidth = 2.0f;
        chip.Root.style.borderBottomColor = GetFrameBudgetColor(_averageFrameMs);
        chip.Value.style.display = DisplayStyle.None;
        return chip;
    }

    private MetricChip CreateMetricChip(string labelText, bool isTitle = false)
    {
        VisualElement root = new VisualElement();
        root.pickingMode = PickingMode.Ignore;
        root.style.flexDirection = FlexDirection.Row;
        root.style.alignItems = Align.Center;
        root.style.backgroundColor = ChipColor;
        root.style.borderTopWidth = isTitle ? 0.0f : 1.0f;
        root.style.borderTopColor = ChipBorderColor;

        Label label = new Label(labelText);
        label.pickingMode = PickingMode.Ignore;
        label.style.unityTextAlign = isTitle ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
        label.style.color = isTitle ? TextColor : MutedTextColor;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        ApplyRuntimeFont(label);

        Label value = new Label("--");
        value.pickingMode = PickingMode.Ignore;
        value.style.unityTextAlign = TextAnchor.MiddleLeft;
        value.style.color = TextColor;
        value.style.whiteSpace = WhiteSpace.NoWrap;
        ApplyRuntimeFont(value);

        root.Add(label);
        root.Add(value);

        return new MetricChip
        {
            Root = root,
            Label = label,
            Value = value,
        };
    }

    private void RefreshResponsiveLayoutIfNeeded()
    {
        if (_bar == null)
            return;

        bool compact = Screen.width < 1060;
        bool tiny = Screen.width < 760;
        if (compact == _lastCompact &&
            tiny == _lastTiny &&
            Screen.width == _lastScreenWidth &&
            Screen.height == _lastScreenHeight)
        {
            return;
        }

        RefreshResponsiveLayout(force: true);
    }

    private void RefreshResponsiveLayout(bool force)
    {
        if (_bar == null)
            return;

        _lastCompact = Screen.width < 1060;
        _lastTiny = Screen.width < 760;
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;

        float scale = Mathf.Clamp(Screen.height / 1080.0f, 0.78f, 1.25f);
        float height = Mathf.Round(34.0f * scale);
        float chipHeight = height - Mathf.Round(10.0f * scale);
        float horizontalPadding = Mathf.Round(8.0f * scale);
        float verticalPadding = Mathf.Round(5.0f * scale);
        float chipPadding = Mathf.Round(7.0f * scale);
        float chipSpacing = Mathf.Round(6.0f * scale);
        int titleFontSize = Mathf.RoundToInt(12.0f * scale);
        int labelFontSize = Mathf.RoundToInt(11.0f * scale);
        int valueFontSize = Mathf.RoundToInt(12.0f * scale);
        int hintFontSize = Mathf.RoundToInt(10.0f * scale);

        _bar.style.height = height;
        _bar.style.paddingLeft = horizontalPadding;
        _bar.style.paddingRight = horizontalPadding;
        _bar.style.paddingTop = verticalPadding;
        _bar.style.paddingBottom = verticalPadding;
        _chipRow.style.height = chipHeight;
        _hintLabel.style.fontSize = hintFontSize;
        _hintLabel.text = $"{_toggleKey} hide";

        ApplyChipLayout(_titleChip, chipHeight, chipPadding, chipSpacing, titleFontSize, valueFontSize, scale);
        ApplyChipLayout(_fpsChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_frameChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_worstChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_mainChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_renderChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_gpuChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_drawChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_batchesChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_setPassChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_gcChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);
        ApplyChipLayout(_memoryChip, chipHeight, chipPadding, chipSpacing, labelFontSize, valueFontSize, scale);

        _titleChip.Root.style.width = Mathf.Round(94.0f * scale);

        SetChipVisible(_renderChip, !_lastTiny);
        SetChipVisible(_gpuChip, !_lastTiny);
        SetChipVisible(_drawChip, !_lastCompact);
        SetChipVisible(_batchesChip, !_lastCompact);
        SetChipVisible(_setPassChip, !_lastCompact);
        SetChipVisible(_gcChip, !_lastCompact);
        SetChipVisible(_memoryChip, !_lastCompact);

        if (force)
            SetUiVisible(_visible);
    }

    private static ProfilerRecorder StartRecorder(ProfilerCategory category, string statName)
    {
        try
        {
            return ProfilerRecorder.StartNew(category, statName, 1);
        }
        catch
        {
            return default;
        }
    }

    private static void DisposeRecorder(ref ProfilerRecorder recorder)
    {
        if (recorder.Valid)
            recorder.Dispose();

        recorder = default;
    }

    private static long ReadRecorderValue(ProfilerRecorder recorder)
    {
        return recorder.Valid ? recorder.LastValue : -1L;
    }

    private void ApplyRuntimeFont(TextElement textElement)
    {
        Font runtimeFont = GetRuntimeFont();
        if (runtimeFont != null)
            textElement.style.unityFont = runtimeFont;
    }

    private Font GetRuntimeFont()
    {
        if (_runtimeFont != null)
            return _runtimeFont;

        _runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_runtimeFont == null)
            _runtimeFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_runtimeFont == null)
            _runtimeFont = Font.CreateDynamicFontFromOSFont("Arial", 14);

        return _runtimeFont;
    }

    private void ApplyChipLayout(
        MetricChip chip,
        float chipHeight,
        float horizontalPadding,
        float chipSpacing,
        int labelFontSize,
        int valueFontSize,
        float scale)
    {
        chip.Root.style.height = chipHeight;
        chip.Root.style.marginRight = chipSpacing;
        chip.Root.style.paddingLeft = horizontalPadding;
        chip.Root.style.paddingRight = horizontalPadding;
        chip.Root.style.paddingTop = 0.0f;
        chip.Root.style.paddingBottom = 1.0f;
        if (!chip.FixedWidth)
            chip.Root.style.width = StyleKeyword.Auto;
        chip.Label.style.fontSize = labelFontSize;
        chip.Value.style.fontSize = valueFontSize;

        if (chip.FixedWidth)
        {
            chip.Label.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.Value.style.display = DisplayStyle.None;
        }
        else
        {
            chip.Label.style.unityFontStyleAndWeight = FontStyle.Normal;
            chip.Value.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.Value.style.display = DisplayStyle.Flex;
            chip.Value.style.marginLeft = Mathf.Round(5.0f * scale);
        }
    }

    private void RefreshUiValues()
    {
        if (_bar == null)
            return;

        _titleChip.Root.style.borderBottomColor = GetFrameBudgetColor(_averageFrameMs);
        UpdateMetricChip(_fpsChip, FormatFloat(_fps, 0), GetFrameBudgetColor(_averageFrameMs));
        UpdateMetricChip(_frameChip, FormatMilliseconds(_averageFrameMs), GetFrameBudgetColor(_averageFrameMs));
        UpdateMetricChip(_worstChip, FormatMilliseconds(_worstFrameMs), MutedTextColor);
        UpdateMetricChip(_mainChip, FormatOptionalMilliseconds(_mainThreadMs), GetThreadColor(_mainThreadMs));
        UpdateMetricChip(_renderChip, FormatOptionalMilliseconds(_renderThreadMs), GetThreadColor(_renderThreadMs));
        UpdateMetricChip(_gpuChip, FormatOptionalMilliseconds(_gpuFrameMs), GetThreadColor(_gpuFrameMs));
        UpdateMetricChip(_drawChip, FormatOptionalCount(_drawCallCount), TextColor);
        UpdateMetricChip(_batchesChip, FormatOptionalCount(_batchCount), TextColor);
        UpdateMetricChip(_setPassChip, FormatOptionalCount(_setPassCount), TextColor);
        UpdateMetricChip(_gcChip, FormatOptionalBytes(_gcAllocatedBytes), GetGcColor(_gcAllocatedBytes));
        UpdateMetricChip(_memoryChip, FormatMemoryPair(_monoUsedBytes, _totalAllocatedBytes), TextColor);
    }

    private static void UpdateMetricChip(MetricChip chip, string value, Color valueColor)
    {
        chip.Value.text = value;
        chip.Value.style.color = valueColor;
    }

    private static void SetChipVisible(MetricChip chip, bool visible)
    {
        chip.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void SetUiVisible(bool visible)
    {
        if (_uiDocument != null)
            _uiDocument.enabled = true;

        if (_bar != null)
            _bar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnDestroy()
    {
        if (_ownsUidocument && _uiDocument != null)
            Destroy(_uiDocument);

        if (_ownsPanelSettings && _panelSettings != null)
            Destroy(_panelSettings);
    }

    private static Color GetFrameBudgetColor(float frameMs)
    {
        if (frameMs <= 0.0f)
            return MutedTextColor;

        if (frameMs <= Target60FpsFrameMs)
            return GoodColor;

        if (frameMs <= Target40FpsFrameMs)
            return WarningColor;

        return BadColor;
    }

    private static Color GetThreadColor(float frameMs)
    {
        return frameMs > 0.0f ? GetFrameBudgetColor(frameMs) : MutedTextColor;
    }

    private static Color GetGcColor(long bytes)
    {
        if (bytes <= 0L)
            return GoodColor;

        return bytes < 64L * 1024L ? WarningColor : BadColor;
    }

    private static string FormatFloat(float value, int decimals)
    {
        if (value <= 0.0f)
            return "--";

        return decimals <= 0 ? value.ToString("0") : value.ToString("0.0");
    }

    private static string FormatMilliseconds(float value)
    {
        return value > 0.0f ? value.ToString("0.0") + "ms" : "--";
    }

    private static string FormatOptionalMilliseconds(float value)
    {
        return value > 0.0f ? value.ToString("0.0") + "ms" : "--";
    }

    private static string FormatOptionalCount(long value)
    {
        if (value < 0L)
            return "--";

        if (value >= 1000000L)
            return (value / 1000000.0).ToString("0.0") + "M";

        if (value >= 10000L)
            return (value / 1000.0).ToString("0.0") + "K";

        return value.ToString();
    }

    private static string FormatOptionalBytes(long bytes)
    {
        if (bytes < 0L)
            return "--";

        if (bytes < 1024L)
            return bytes + "B";

        if (bytes < 1024L * 1024L)
            return (bytes / 1024.0).ToString("0.0") + "KB";

        return (bytes / (1024.0 * 1024.0)).ToString("0.0") + "MB";
    }

    private static string FormatMemoryPair(long monoUsedBytes, long totalAllocatedBytes)
    {
        if (monoUsedBytes < 0L && totalAllocatedBytes < 0L)
            return "--";

        return FormatMemory(monoUsedBytes) + "/" + FormatMemory(totalAllocatedBytes);
    }

    private static string FormatMemory(long bytes)
    {
        if (bytes < 0L)
            return "--";

        if (bytes < 1024L * 1024L * 1024L)
            return (bytes / (1024.0 * 1024.0)).ToString("0") + "MB";

        return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0") + "GB";
    }
}
