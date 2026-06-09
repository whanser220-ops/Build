#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(PerformancePoiPoint))]
public sealed class PerformancePoiPointEditor : Editor
{
    private static readonly string[] HiddenProperties =
    {
        "_sampleFramesOverride",
        "_usePointForwardForCharacter",
        "_characterYawOffset",
        "_projectCharacterToGround",
        "_groundProbeHeight",
        "_groundProbeDistance",
        "_groundLayers",
        "_cameraPoseMode",
        "_cameraPoseTransform",
        "_lookAtLocalOffset",
        "_cameraLocalEulerAngles"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, HiddenProperties);
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "POI 现在分两种模式：`RecordedReplay` 用来录制你的角色和相机操作，`TurntableSweep` 则不需要录制，会在运行时按设定速率原地慢慢转一圈采样。",
            MessageType.Info);
    }
}

[CustomEditor(typeof(PerformancePoiSampler))]
public sealed class PerformancePoiSamplerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PerformancePoiSampler sampler = (PerformancePoiSampler)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(sampler.StatusMessage, MessageType.None);
        EditorGUILayout.HelpBox(
            "编辑器内直接跑 POI 采样的入口已经移除。请使用 POI Tool 在 Development Build 里运行整轮采样。",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(sampler.LastArtifactDirectory)))
            {
                if (GUILayout.Button("Open Last Artifacts"))
                    EditorUtility.RevealInFinder(sampler.LastArtifactDirectory);
            }

            if (GUILayout.Button("Log Player Args"))
                Debug.Log("[PerformancePoiSampler] " + sampler.BuildSuggestedCommandLineArgs(), sampler);
        }

        if (GUILayout.Button("Open POI Tool"))
            PerformancePoiSamplerWindow.OpenWithSampler(sampler);
    }
}

public sealed class PerformancePoiSamplerWindow : EditorWindow
{
    private const string WindowTitle = "POI Sampler";
    private const string PerformancePoiSamplerScriptPath = "Assets/Project/Tools/Performance/Runtime/PerformancePoiSampler.cs";
    private const string PerformancePoiPointScriptPath = "Assets/Project/Tools/Performance/Runtime/PerformancePoiPoint.cs";

    [SerializeField] private PerformancePoiSampler _sampler;
    [SerializeField] private PerformancePoiPoint _tracePoint;
    [SerializeField] private string _runLabelOverride = string.Empty;
    [SerializeField] private string _externalPlayerExecutablePath = string.Empty;
    [SerializeField] private string _traceStatusMessage = string.Empty;
    [SerializeField] private bool _externalPlayerAutoQuit = true;
    [SerializeField] private bool _externalPlayerForceD3D12 = true;
    [SerializeField] private string _externalPlayerStatusMessage = string.Empty;
    [SerializeField] private int _pendingExternalPlayerProcessId;
    [SerializeField] private long _pendingExternalPlayerLaunchUtcTicks;
    [SerializeField] private string _pendingExternalPlayerArtifactsRoot = string.Empty;
    [SerializeField] private string _pendingExternalPlayerRunLabel = string.Empty;
    [SerializeField] private bool _pendingExternalPlayerMaterialization;
    [SerializeField] private Vector2 _scrollPosition;

    private int _cachedTotalPoiCount;
    private int _cachedAutomaticPoiCount;
    private string _cachedDuplicatePoiIdWarning = string.Empty;
    private string _cachedPoiKeyStatusMessage = string.Empty;
    private string _cachedSamplerHierarchyPath = string.Empty;
    private string _cachedSamplerSceneName = string.Empty;
    private bool _savedSceneAnalysisCached;
    private string _savedSceneAnalysisPath = string.Empty;
    private bool _savedSceneHasSampler;
    private bool _savedSceneHasPoint;
    private static GUIStyle _metricsRowTitleStyle;
    private static GUIStyle _metricsFocusedTitleStyle;
    private static GUIStyle _metricsValueStyle;

    [MenuItem("Tools/Qianxia/Performance/POI Sampler")]
    public static void Open()
    {
        PerformancePoiSamplerWindow window = GetWindow<PerformancePoiSamplerWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(460.0f, 280.0f);
        window.Show();
    }

    public static void OpenWithSampler(PerformancePoiSampler sampler)
    {
        PerformancePoiSamplerWindow window = GetWindow<PerformancePoiSamplerWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(460.0f, 280.0f);
        window.SetSampler(sampler);
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent(WindowTitle);
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        Selection.selectionChanged += OnSelectionChanged;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        EditorSceneManager.sceneSaved += OnSceneSaved;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChangedInEditMode;
        EditorApplication.update += OnEditorUpdate;
        EnsureExternalPlayerDefaults();
        SyncTracePointSelectionState();
        RefreshWindowState(resolveSampler: true, invalidateSavedSceneAnalysis: true);
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        Selection.selectionChanged -= OnSelectionChanged;
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        EditorSceneManager.sceneSaved -= OnSceneSaved;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChangedInEditMode;
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        DrawSamplerSection();
        EditorGUILayout.Space();
        DrawSceneSummary();
        EditorGUILayout.Space();
        DrawLatestMetricsSection();
        EditorGUILayout.Space();
        DrawGizmoOverlaySection();
        EditorGUILayout.Space();
        DrawProfilerRawExportSection();
        EditorGUILayout.Space();
        DrawTraceAuthoringSection();
        EditorGUILayout.Space();
        DrawAutomaticPoiTraceStatusSection();
        EditorGUILayout.Space();
        DrawRunConfigSection();
        EditorGUILayout.Space();
        DrawExternalPlayerSection();
        EditorGUILayout.EndScrollView();
    }

    private void DrawSamplerSection()
    {
        EditorGUILayout.LabelField("Sampler", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        PerformancePoiSampler newSampler = (PerformancePoiSampler)EditorGUILayout.ObjectField(
            "Target Sampler",
            _sampler,
            typeof(PerformancePoiSampler),
            true);
        if (EditorGUI.EndChangeCheck())
            SetSampler(newSampler);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Selected"))
                SetSampler(GetSelectedSampler());

            using (new EditorGUI.DisabledScope(_sampler == null))
            {
                if (GUILayout.Button("Ping Sampler"))
                    EditorGUIUtility.PingObject(_sampler);
            }

            if (GUILayout.Button("Create Sampler"))
                CreateSampler();
        }

        if (_sampler == null)
        {
            EditorGUILayout.HelpBox(
                "鍦烘櫙閲岃繕娌℃湁閫変腑鐨?PerformancePoiSampler銆傚厛鍦?Hierarchy 閲岄€変腑涓€涓?sampler 鍐嶇偣 Use Selected锛屾垨鑰呯洿鎺ョ偣 Create Sampler銆?",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            string.Format(
                "{0}\nScene: {1}",
                _cachedSamplerHierarchyPath,
                _cachedSamplerSceneName),
            MessageType.None);
    }

    private void DrawSceneSummary()
    {
        EditorGUILayout.LabelField("Scene Summary", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Loaded POIs", _cachedTotalPoiCount.ToString());
        EditorGUILayout.LabelField("Automatic POIs", _cachedAutomaticPoiCount.ToString());

        if (!string.IsNullOrWhiteSpace(_cachedDuplicatePoiIdWarning))
            EditorGUILayout.HelpBox(_cachedDuplicatePoiIdWarning, MessageType.Warning);

        if (!string.IsNullOrWhiteSpace(_cachedPoiKeyStatusMessage))
            EditorGUILayout.HelpBox(_cachedPoiKeyStatusMessage, MessageType.Info);

        if (_sampler != null)
        {
            EditorGUILayout.LabelField("Artifacts Root", _sampler.ResolvedArtifactsRoot);
            EditorGUILayout.LabelField("Trace Root", _sampler.ResolvedReplayTraceRoot);
            if (!string.IsNullOrWhiteSpace(_sampler.StatusMessage))
                EditorGUILayout.HelpBox(_sampler.StatusMessage, MessageType.None);
        }
    }

    private void DrawGizmoOverlaySection()
    {
        EditorGUILayout.LabelField("Gizmo Overlay", EditorStyles.boldLabel);

        bool showOverlay = PerformancePoiGizmoOverlaySettings.ShowLatestMetrics;
        float gameThreadBudgetMs = PerformancePoiGizmoOverlaySettings.GameThreadBudgetMs;
        float renderThreadBudgetMs = PerformancePoiGizmoOverlaySettings.RenderThreadBudgetMs;
        float gpuBudgetMs = PerformancePoiGizmoOverlaySettings.GpuBudgetMs;

        EditorGUI.BeginChangeCheck();
        showOverlay = EditorGUILayout.Toggle("Show Latest Metrics", showOverlay);
        gameThreadBudgetMs = EditorGUILayout.FloatField("Game Thread Budget", gameThreadBudgetMs);
        renderThreadBudgetMs = EditorGUILayout.FloatField("Render Thread Budget", renderThreadBudgetMs);
        gpuBudgetMs = EditorGUILayout.FloatField("GPU Budget", gpuBudgetMs);
        if (EditorGUI.EndChangeCheck())
        {
            PerformancePoiGizmoOverlaySettings.ShowLatestMetrics = showOverlay;
            PerformancePoiGizmoOverlaySettings.GameThreadBudgetMs = gameThreadBudgetMs;
            PerformancePoiGizmoOverlaySettings.RenderThreadBudgetMs = renderThreadBudgetMs;
            PerformancePoiGizmoOverlaySettings.GpuBudgetMs = gpuBudgetMs;
            PerformancePoiGizmoOverlayCache.Invalidate();
        }

        EditorGUILayout.HelpBox(
            "Scene 视图里的 POI 会显示当前场景最近一次采样结果的 Game Thread / Render Thread / GPU 三列指标。超出上限显示红色，余量充足显示绿色。",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Latest Results"))
                PerformancePoiGizmoOverlayCache.Invalidate();

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(PerformancePoiGizmoOverlayCache.GetLatestSummaryPath(SceneManager.GetActiveScene()))))
            {
                if (GUILayout.Button("Open Latest Summary"))
                    RevealPath(PerformancePoiGizmoOverlayCache.GetLatestSummaryPath(SceneManager.GetActiveScene()));
            }
        }

        EditorGUILayout.HelpBox(
            PerformancePoiGizmoOverlayCache.GetStatusMessage(SceneManager.GetActiveScene()),
            MessageType.None);
    }

    private void DrawLatestMetricsSection()
    {
        EditorGUILayout.LabelField("Latest POI Metrics", EditorStyles.boldLabel);

        Scene scene = ResolveMetricsScene();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Metrics"))
                PerformancePoiGizmoOverlayCache.Invalidate();

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(PerformancePoiGizmoOverlayCache.GetLatestSummaryPath(scene))))
            {
                if (GUILayout.Button("Open Metrics Summary"))
                    RevealPath(PerformancePoiGizmoOverlayCache.GetLatestSummaryPath(scene));
            }
        }

        EditorGUILayout.HelpBox(
            "面板会显示当前场景最近一次采样结果里的 Game Thread / Render Thread / GPU 三列指标，并沿用预算颜色：超标偏红，余量充足偏绿。点击任意一行会自动选中并定位到对应 POI。",
            MessageType.None);
        EditorGUILayout.HelpBox(
            PerformancePoiGizmoOverlayCache.GetStatusMessage(scene),
            MessageType.None);

        List<PerformancePoiPoint> displayPoints = CollectMetricDisplayPoints(scene);
        if (displayPoints.Count == 0)
        {
            EditorGUILayout.HelpBox("当前场景还没有可显示的 POI 性能结果。先跑一次 Development Build 采样。", MessageType.Info);
            return;
        }

        PerformancePoiPoint focusedPoint = ResolveFocusedMetricPoint(scene);
        if (focusedPoint != null &&
            PerformancePoiGizmoOverlayCache.TryGetMetrics(focusedPoint, out PerformancePoiSampleResult focusedResult))
        {
            EditorGUILayout.LabelField("Focused POI", EditorStyles.miniBoldLabel);
            DrawPoiMetricRow(focusedPoint, focusedResult, focused: true);
            EditorGUILayout.Space(2.0f);
        }

        EditorGUILayout.LabelField("All POIs", EditorStyles.miniBoldLabel);
        for (int index = 0; index < displayPoints.Count; index++)
        {
            PerformancePoiPoint point = displayPoints[index];
            if (point == null ||
                point == focusedPoint ||
                !PerformancePoiGizmoOverlayCache.TryGetMetrics(point, out PerformancePoiSampleResult result))
            {
                continue;
            }

            DrawPoiMetricRow(point, result, focused: false);
        }
    }

    private void DrawProfilerRawExportSection()
    {
        EditorGUILayout.LabelField("Profiler Raw Export", EditorStyles.boldLabel);

        Scene scene = ResolveMetricsScene();
        List<PerformancePoiPoint> points = CollectScenePoiPoints(scene);
        if (points.Count == 0)
        {
            EditorGUILayout.HelpBox("当前场景里没有 POI。", MessageType.None);
            return;
        }

        EditorGUILayout.HelpBox(
            "列表包含当前场景所有 POI。勾选某个 POI 后，Development Build 实际采样该点时会临时打开 Unity binary profiler log，并在本次 run 的 `profiler-raw/` 目录导出一个 `.raw` 文件。只有 Auto 且运行时启用的 POI 会在本轮自动采样中真正导出。",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Enable All"))
                SetProfilerRawExportForPoints(points, true);

            if (GUILayout.Button("Disable All"))
                SetProfilerRawExportForPoints(points, false);
        }

        for (int index = 0; index < points.Count; index++)
            DrawProfilerRawExportRow(points[index]);
    }

    private static void DrawProfilerRawExportRow(PerformancePoiPoint point)
    {
        if (point == null)
            return;

        SerializedObject serializedPoint = new SerializedObject(point);
        SerializedProperty exportProperty = serializedPoint.FindProperty("_exportProfilerRaw");
        if (exportProperty == null)
            return;

        serializedPoint.Update();
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(
                string.Format(CultureInfo.InvariantCulture, "#{0} {1}", point.SortIndex, point.PoiId),
                GUILayout.MinWidth(140.0f));

            EditorGUILayout.LabelField(point.PoiSamplingMode.ToString(), EditorStyles.miniLabel, GUILayout.Width(112.0f));
            EditorGUILayout.LabelField(GetPoiRunStateLabel(point), EditorStyles.miniLabel, GUILayout.Width(64.0f));
            exportProperty.boolValue = EditorGUILayout.ToggleLeft("Export .raw", exportProperty.boolValue, GUILayout.Width(110.0f));

            if (GUILayout.Button("Ping", GUILayout.Width(48.0f)))
            {
                Selection.activeGameObject = point.gameObject;
                EditorGUIUtility.PingObject(point.gameObject);
            }
        }

        if (serializedPoint.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(point);
            if (point.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(point.gameObject.scene);
        }
    }

    private void DrawTraceAuthoringSection()
    {
        EditorGUILayout.LabelField("Replay Trace", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        PerformancePoiPoint newTracePoint = (PerformancePoiPoint)EditorGUILayout.ObjectField(
            "Trace POI",
            _tracePoint,
            typeof(PerformancePoiPoint),
            true);
        if (EditorGUI.EndChangeCheck())
            SetTracePoint(newTracePoint);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Selected POI"))
                SetTracePoint(GetSelectedPoiPoint());

            using (new EditorGUI.DisabledScope(_tracePoint == null))
            {
                if (GUILayout.Button("Ping POI"))
                    EditorGUIUtility.PingObject(_tracePoint);
            }
        }

        if (_tracePoint != null)
        {
            if (_tracePoint.RequiresReplayTrace)
            {
                EditorGUILayout.LabelField("Trace Path", _tracePoint.BuildReplayTracePath());
                if (File.Exists(_tracePoint.BuildReplayTracePath()))
                    EditorGUILayout.HelpBox("当前 POI 已有已保存的 replay trace。", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("当前 POI 还没有 replay trace，外部包运行时会跳过这条轨迹。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "当前 POI 使用 TurntableSweep 模式，不需要 replay trace。运行时会按 {0:0.###} 度/秒原地转一圈。",
                        _tracePoint.ResolveTurntableDegreesPerSecond()),
                    MessageType.Info);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("先选择一个 PerformancePoiPoint。`RecordedReplay` 模式会录制角色和相机轨迹，`TurntableSweep` 模式则不需要 trace。", MessageType.None);
        }

        if (!Application.isPlaying && _tracePoint != null && _tracePoint.RequiresReplayTrace)
            EditorGUILayout.HelpBox("进入 Play Mode 后，可以直接按 Shift+1 开始录制，按 Shift+2 结束并保存。", MessageType.Info);

        PerformancePoiReplayController replayController = ResolvePoiTraceRecorderController(createIfMissing: false);
        if (replayController != null && !string.IsNullOrWhiteSpace(replayController.StatusMessage))
            EditorGUILayout.HelpBox(replayController.StatusMessage, MessageType.None);
        else if (!string.IsNullOrWhiteSpace(_traceStatusMessage))
            EditorGUILayout.HelpBox(_traceStatusMessage, MessageType.None);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                _tracePoint != null && !_tracePoint.RequiresReplayTrace
                    ? "进入 Play Mode 后，可以先把运行时相机移动到当前 POI。TurntableSweep 模式不需要录制，Development Build 运行时会直接按设定速率转一圈采样。"
                    : "进入 Play Mode 后，可以先把运行时相机移动到当前 POI，再开始录制角色和相机轨迹。录完后，Development Build 会回放这条轨迹来做 POI 采样。",
                MessageType.Info);
            return;
        }

        bool isRecording = replayController != null && replayController.IsRecordingTrace;
        using (new EditorGUI.DisabledScope(isRecording || _sampler == null || _tracePoint == null))
        {
            if (GUILayout.Button("Move Runtime Camera To POI"))
                MoveRuntimeViewToPoi();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(isRecording || _sampler == null || _tracePoint == null || (_tracePoint != null && !_tracePoint.RequiresReplayTrace)))
            {
                if (GUILayout.Button("Start Recording"))
                    StartPoiTraceRecording();
            }

            using (new EditorGUI.DisabledScope(!isRecording || _tracePoint == null))
            {
                if (GUILayout.Button("Stop And Save"))
                    StopPoiTraceRecordingAndSave();
            }

            using (new EditorGUI.DisabledScope(!isRecording))
            {
                if (GUILayout.Button("Discard"))
                    DiscardPoiTraceRecording();
            }
        }
    }

    private void DrawAutomaticPoiTraceStatusSection()
    {
        EditorGUILayout.LabelField("Automatic POI Trace Status", EditorStyles.boldLabel);

        if (_sampler == null)
        {
            EditorGUILayout.HelpBox("先选中一个 PerformancePoiSampler，才能检查自动 POI 的 trace 状态。", MessageType.None);
            return;
        }

        if (!TryBuildAutomaticPoiTraceStatusLines(out List<string> statusLines, out MessageType messageType))
        {
            EditorGUILayout.HelpBox("当前场景里没有启用自动采样的 POI。", MessageType.None);
            return;
        }

        EditorGUILayout.HelpBox(string.Join("\n", statusLines), messageType);
    }

    private void DrawRunConfigSection()
    {
        EditorGUILayout.LabelField("Run Config", EditorStyles.boldLabel);
        _runLabelOverride = EditorGUILayout.TextField("Run Label Override", _runLabelOverride);
        EditorGUILayout.HelpBox(
            "POI 工具现在只保留 Development Build 采样链路。下面的配置会用于启动外部包时传入命令行。",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_sampler == null))
            {
                if (GUILayout.Button("Open Output Root"))
                    RevealPath(_sampler.ResolvedArtifactsRoot);
            }

            using (new EditorGUI.DisabledScope(_sampler == null || string.IsNullOrWhiteSpace(_sampler.LastArtifactDirectory)))
            {
                if (GUILayout.Button("Open Last Artifacts"))
                    RevealPath(_sampler.LastArtifactDirectory);
            }
        }
    }

    private void DrawExternalPlayerSection()
    {
        EditorGUILayout.LabelField("External Player", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "导出包里的 POI 数据通常比 Editor PlayMode 更接近真实玩家机器表现，因为不会混入编辑器窗口、Inspector 和 Domain Reload 的额外开销。当前 fixed player 仍然是 Development Build，所以它更适合“比 Editor 更准”的性能采样，而不是最终发行版 benchmark。",
            MessageType.Info);

        if (!TryBuildExternalPlayerPreflightMessage(out string preflightMessage, out MessageType preflightMessageType))
            EditorGUILayout.HelpBox(preflightMessage, preflightMessageType);
        else
            EditorGUILayout.HelpBox(preflightMessage, preflightMessageType);

        _externalPlayerExecutablePath = EditorGUILayout.TextField("Executable", _externalPlayerExecutablePath);
        _externalPlayerAutoQuit = EditorGUILayout.Toggle("Auto Quit When Done", _externalPlayerAutoQuit);
        _externalPlayerForceD3D12 = EditorGUILayout.Toggle("Force D3D12", _externalPlayerForceD3D12);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Default Player"))
            {
                _externalPlayerExecutablePath = RenderDocPlayerBuildUtility.GetDefaultExecutablePath();
                SetExternalPlayerStatus("已切换到 fixed player 默认输出路径。");
            }

            if (GUILayout.Button("Browse Exe"))
            {
                string selectedPath = EditorUtility.OpenFilePanel(
                    "选择要运行的导出包 EXE",
                    GetPreferredExternalPlayerDirectory(),
                    "exe");
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    _externalPlayerExecutablePath = selectedPath;
                    SetExternalPlayerStatus("已选择外部包。");
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Build And Run Fixed Player"))
                BuildAndRunFixedPlayer();

            if (GUILayout.Button("Run Existing Player"))
                RunExistingExternalPlayer();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_externalPlayerExecutablePath)))
            {
                if (GUILayout.Button("Open Player Folder"))
                    RevealPath(Path.GetDirectoryName(_externalPlayerExecutablePath));
            }

            using (new EditorGUI.DisabledScope(_sampler == null))
            {
                if (GUILayout.Button("Copy Launch Args"))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildExternalPlayerArguments();
                    SetExternalPlayerStatus("已复制外部 Player 的 POI 启动参数。");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(_externalPlayerStatusMessage))
            EditorGUILayout.HelpBox(_externalPlayerStatusMessage, MessageType.None);
    }

    private void OnHierarchyChanged()
    {
        RefreshWindowState(resolveSampler: _sampler == null, invalidateSavedSceneAnalysis: false);
    }

    private void OnEditorUpdate()
    {
        if (!_pendingExternalPlayerMaterialization)
            return;

        if (IsPendingExternalPlayerStillRunning())
            return;

        MaterializePendingExternalPlayerProfilerArtifacts();
    }

    private void BuildAndRunFixedPlayer()
    {
        try
        {
            TryRecoverSamplerReference();
            if (!TryValidateExternalPlayerRun(out string validationMessage))
            {
                SetExternalPlayerStatus(validationMessage);
                return;
            }

            SetExternalPlayerStatus("正在构建 fixed Development Player，请稍等...");
            EditorUtility.DisplayProgressBar(
                WindowTitle,
                "Building fixed Development Player...",
                0.1f);

            RenderDocPlayerBuildUtility.BuildDefaultPlayer(revealOutputPath: false);
            _externalPlayerExecutablePath = RenderDocPlayerBuildUtility.GetDefaultExecutablePath();
            RunExternalPlayerPoi(_externalPlayerExecutablePath, "已构建并启动 fixed player POI 采样。");
        }
        catch (Exception exception)
        {
            SetExternalPlayerStatus("构建或启动 fixed player 失败：" + exception.Message);
            UnityEngine.Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void RunExistingExternalPlayer()
    {
        try
        {
            TryRecoverSamplerReference();
            RunExternalPlayerPoi(_externalPlayerExecutablePath, "已启动外部 Player POI 采样。");
        }
        catch (Exception exception)
        {
            SetExternalPlayerStatus("启动外部 Player 失败：" + exception.Message);
            UnityEngine.Debug.LogException(exception);
        }
    }

    private void RunExternalPlayerPoi(string executablePath, string successMessage)
    {
        if (_sampler == null)
        {
            SetExternalPlayerStatus("没有可用的 PerformancePoiSampler。请先选择场景里的 sampler，或点击 Create Sampler。");
            return;
        }

        if (!TryValidateExternalPlayerRun(out string validationMessage))
        {
            SetExternalPlayerStatus(validationMessage);
            return;
        }

        string fullExecutablePath = string.IsNullOrWhiteSpace(executablePath)
            ? string.Empty
            : Path.GetFullPath(executablePath);
        if (string.IsNullOrWhiteSpace(fullExecutablePath) || !File.Exists(fullExecutablePath))
        {
            SetExternalPlayerStatus("未找到外部 Player exe。请先选择已有 exe，或点击 Build And Run Fixed Player。");
            return;
        }

        string startupScenePath = RenderDocPlayerBuildUtility.GetActiveEditorScenePath();
        RenderDocPlayerBuildUtility.RefreshExternalPlayerLaunchArtifacts(
            fullExecutablePath,
            startupScenePath,
            refreshBuildStamp: false);

        string arguments = BuildExternalPlayerArguments();
        string workingDirectory = Path.GetDirectoryName(fullExecutablePath);
        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fullExecutablePath,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory,
            UseShellExecute = false
        };

        System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Process.Start returned null.");

        UnityEngine.Debug.Log(string.Format(
            "[PerformancePoiSamplerWindow] Launched external POI player: {0} {1}",
            fullExecutablePath,
            arguments));
        TrackPendingExternalPlayerRun(process);
        SetExternalPlayerStatus(successMessage + Environment.NewLine + fullExecutablePath);
    }

    private void SetSampler(PerformancePoiSampler sampler)
    {
        _sampler = sampler;
        UpdateSamplerDisplayCache();
        Repaint();
    }

    private void SetTracePoint(PerformancePoiPoint point)
    {
        _tracePoint = point;
        SyncTracePointSelectionState();
        Repaint();
    }

    private void SyncTracePointSelectionState()
    {
        if (_tracePoint == null)
        {
            PlayerPrefs.DeleteKey(PerformancePoiSamplingArtifacts.EditorSelectedPoiKeyPrefsKey);
            PlayerPrefs.DeleteKey(PerformancePoiSamplingArtifacts.EditorSelectedPoiIdPrefsKey);
            PlayerPrefs.DeleteKey(PerformancePoiSamplingArtifacts.EditorSelectedPoiScenePathPrefsKey);
            PlayerPrefs.Save();
            return;
        }

        PlayerPrefs.SetString(PerformancePoiSamplingArtifacts.EditorSelectedPoiKeyPrefsKey, _tracePoint.PoiKey);
        PlayerPrefs.SetString(PerformancePoiSamplingArtifacts.EditorSelectedPoiIdPrefsKey, _tracePoint.PoiId);
        PlayerPrefs.SetString(PerformancePoiSamplingArtifacts.EditorSelectedPoiScenePathPrefsKey, _tracePoint.gameObject.scene.path ?? string.Empty);
        PlayerPrefs.Save();
    }

    private void TryRecoverTracePointReference()
    {
        if (_tracePoint != null)
            return;

        PerformancePoiPoint selectedPoint = GetSelectedPoiPoint();
        if (selectedPoint != null)
            SetTracePoint(selectedPoint);
    }

    private void TryRecoverSamplerReference()
    {
        if (_sampler != null)
            return;

        PerformancePoiSampler selectedSampler = GetSelectedSampler();
        if (selectedSampler != null)
        {
            SetSampler(selectedSampler);
            return;
        }

        PerformancePoiSampler[] samplers = UnityEngine.Object.FindObjectsByType<PerformancePoiSampler>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (samplers.Length > 0)
            SetSampler(samplers[0]);
    }

    private PerformancePoiReplayController ResolvePoiTraceRecorderController(bool createIfMissing)
    {
        if (_sampler == null)
            return null;

        if (!Application.isPlaying && !createIfMissing)
            return null;

        if (!createIfMissing)
            return _sampler.GetComponent<PerformancePoiReplayController>();

        return PerformancePoiReplayController.GetOrCreate(_sampler.gameObject);
    }

    private void StartPoiTraceRecording()
    {
        if (_sampler == null || _tracePoint == null)
        {
            SetTraceStatus("鍏堥€夊ソ sampler 鍜岀洰鏍?POI銆?");
            return;
        }

        if (!_tracePoint.RequiresReplayTrace)
        {
            SetTraceStatus("当前 POI 使用 TurntableSweep 模式，不需要录制 replay trace。");
            return;
        }

        if (!_sampler.TryMoveRuntimeViewToPoint(_tracePoint, out string moveStatus))
        {
            SetTraceStatus(moveStatus);
            return;
        }

        PerformancePoiReplayController replayController = ResolvePoiTraceRecorderController(createIfMissing: true);
        if (replayController == null)
        {
            SetTraceStatus("未能创建 POI replay controller。");
            return;
        }

        if (!replayController.StartTraceRecording())
        {
            SetTraceStatus(replayController.StatusMessage);
            return;
        }

        SetTraceStatus("POI 轨迹录制已开始。现在直接操作角色和相机，完成后点 Stop And Save。");
    }

    private void MoveRuntimeViewToPoi()
    {
        if (_sampler == null || _tracePoint == null)
        {
            SetTraceStatus("先选好 sampler 和目标 POI。");
            return;
        }

        if (!_sampler.TryMoveRuntimeViewToPoint(_tracePoint, out string statusMessage))
        {
            SetTraceStatus(statusMessage);
            return;
        }

        SetTraceStatus(statusMessage);
    }

    private void StopPoiTraceRecordingAndSave()
    {
        if (_tracePoint == null)
        {
            SetTraceStatus("没有目标 POI，无法保存录制。");
            return;
        }

        PerformancePoiReplayController replayController = ResolvePoiTraceRecorderController(createIfMissing: false);
        if (replayController == null)
        {
            SetTraceStatus("当前没有正在录制的 POI trace。");
            return;
        }

        string tracePath = _tracePoint.BuildReplayTracePath();
        if (!replayController.StopTraceRecordingAndSaveToPath(tracePath))
        {
            SetTraceStatus(replayController.StatusMessage);
            return;
        }

        if (!_tracePoint.TryDeleteObsoleteReplayTraces(string.Empty, out string deletedTracePaths, out string deleteErrorMessage))
        {
            RefreshWindowState(resolveSampler: _sampler == null, invalidateSavedSceneAnalysis: false);
            SetTraceStatus("POI 轨迹已保存到: " + tracePath + " | " + deleteErrorMessage);
            return;
        }

        RefreshWindowState(resolveSampler: _sampler == null, invalidateSavedSceneAnalysis: false);
        SetTraceStatus(string.IsNullOrWhiteSpace(deletedTracePaths)
            ? "POI 轨迹已保存到: " + tracePath
            : "POI 轨迹已保存到: " + tracePath + " | 已删除旧 trace: " + deletedTracePaths);
    }

    private void DiscardPoiTraceRecording()
    {
        PerformancePoiReplayController replayController = ResolvePoiTraceRecorderController(createIfMissing: false);
        if (replayController == null)
        {
            SetTraceStatus("当前没有正在录制的 POI trace。");
            return;
        }

        replayController.DiscardTraceRecording();
        SetTraceStatus("已丢弃当前 POI 轨迹录制。");
    }

    private void OnSelectionChanged()
    {
        PerformancePoiSampler selectedSampler = GetSelectedSampler();
        if (selectedSampler != null && selectedSampler != _sampler)
            SetSampler(selectedSampler);

        PerformancePoiPoint selectedPoint = GetSelectedPoiPoint();
        if (selectedPoint != null)
            SetTracePoint(selectedPoint);

        RefreshWindowState(resolveSampler: _sampler == null, invalidateSavedSceneAnalysis: false);
    }

    private void OnUndoRedoPerformed()
    {
        RefreshWindowState(resolveSampler: _sampler == null, invalidateSavedSceneAnalysis: false);
    }

    private void OnSceneSaved(Scene scene)
    {
        RefreshWindowState(resolveSampler: _sampler == null, invalidateSavedSceneAnalysis: true);
    }

    private void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        RefreshWindowState(resolveSampler: true, invalidateSavedSceneAnalysis: true);
    }

    private void OnActiveSceneChangedInEditMode(Scene previousScene, Scene newScene)
    {
        RefreshWindowState(resolveSampler: true, invalidateSavedSceneAnalysis: true);
    }

    private void EnsureExternalPlayerDefaults()
    {
        if (string.IsNullOrWhiteSpace(_externalPlayerExecutablePath))
            _externalPlayerExecutablePath = RenderDocPlayerBuildUtility.GetDefaultExecutablePath();
    }

    private static PerformancePoiSampler GetSelectedSampler()
    {
        if (Selection.activeGameObject == null)
            return null;

        return Selection.activeGameObject.GetComponent<PerformancePoiSampler>();
    }

    private static PerformancePoiPoint GetSelectedPoiPoint()
    {
        if (Selection.activeGameObject == null)
            return null;

        return Selection.activeGameObject.GetComponent<PerformancePoiPoint>();
    }

    private Scene ResolveMetricsScene()
    {
        if (_tracePoint != null && _tracePoint.gameObject.scene.IsValid())
            return _tracePoint.gameObject.scene;

        if (_sampler != null && _sampler.gameObject.scene.IsValid())
            return _sampler.gameObject.scene;

        return SceneManager.GetActiveScene();
    }

    private PerformancePoiPoint ResolveFocusedMetricPoint(Scene scene)
    {
        if (_tracePoint != null && _tracePoint.gameObject.scene == scene)
            return _tracePoint;

        PerformancePoiPoint selectedPoint = GetSelectedPoiPoint();
        if (selectedPoint != null && selectedPoint.gameObject.scene == scene)
            return selectedPoint;

        return null;
    }

    private static List<PerformancePoiPoint> CollectMetricDisplayPoints(Scene scene)
    {
        List<PerformancePoiPoint> pointsWithMetrics = new List<PerformancePoiPoint>();
        PerformancePoiPoint[] points = UnityEngine.Object.FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiPoint point = points[index];
            if (point == null || point.gameObject.scene != scene)
                continue;

            if (!PerformancePoiGizmoOverlayCache.TryGetMetrics(point, out _))
                continue;

            pointsWithMetrics.Add(point);
        }

        pointsWithMetrics.Sort(ComparePoiMetricDisplayOrder);
        return pointsWithMetrics;
    }

    private static List<PerformancePoiPoint> CollectScenePoiPoints(Scene scene)
    {
        List<PerformancePoiPoint> points = new List<PerformancePoiPoint>();
        PerformancePoiPoint[] allPoints = UnityEngine.Object.FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < allPoints.Length; index++)
        {
            PerformancePoiPoint point = allPoints[index];
            if (point == null || point.gameObject.scene != scene)
                continue;

            points.Add(point);
        }

        points.Sort(ComparePoiMetricDisplayOrder);
        return points;
    }

    private static string GetPoiRunStateLabel(PerformancePoiPoint point)
    {
        if (point == null || !point.isActiveAndEnabled)
            return "Inactive";

        return point.IncludeInAutomaticRuns ? "Auto" : "Manual";
    }

    private static void SetProfilerRawExportForPoints(List<PerformancePoiPoint> points, bool enabled)
    {
        if (points == null || points.Count == 0)
            return;

        Undo.RecordObjects(points.ToArray(), enabled ? "Enable POI Profiler Raw Export" : "Disable POI Profiler Raw Export");

        for (int index = 0; index < points.Count; index++)
        {
            PerformancePoiPoint point = points[index];
            if (point == null)
                continue;

            SerializedObject serializedPoint = new SerializedObject(point);
            SerializedProperty exportProperty = serializedPoint.FindProperty("_exportProfilerRaw");
            if (exportProperty == null)
                continue;

            serializedPoint.Update();
            exportProperty.boolValue = enabled;
            serializedPoint.ApplyModifiedProperties();
            EditorUtility.SetDirty(point);
            if (point.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(point.gameObject.scene);
        }
    }

    private static int ComparePoiMetricDisplayOrder(PerformancePoiPoint left, PerformancePoiPoint right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        int sortIndexCompare = left.SortIndex.CompareTo(right.SortIndex);
        if (sortIndexCompare != 0)
            return sortIndexCompare;

        return string.Compare(left.PoiId, right.PoiId, StringComparison.OrdinalIgnoreCase);
    }

    private static void DrawPoiMetricRow(PerformancePoiPoint point, PerformancePoiSampleResult result, bool focused)
    {
        Rect rowRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        try
        {
            GUIStyle titleStyle = focused ? MetricsFocusedTitleStyle : MetricsRowTitleStyle;
            string prefix = focused ? ">> " : string.Empty;
            EditorGUILayout.LabelField(
                string.Format(CultureInfo.InvariantCulture, "{0}#{1} {2}", prefix, point.SortIndex, point.PoiId),
                titleStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMetricField("Game", result.averageMainThreadMs, PerformancePoiGizmoOverlaySettings.GameThreadBudgetMs);
                DrawMetricField("Render", result.averageRenderThreadMs, PerformancePoiGizmoOverlaySettings.RenderThreadBudgetMs);
                DrawMetricField("GPU", result.averageGpuFrameMs, PerformancePoiGizmoOverlaySettings.GpuBudgetMs);
            }
        }
        finally
        {
            EditorGUILayout.EndVertical();
        }

        EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
        if (Event.current.type == EventType.MouseUp &&
            Event.current.button == 0 &&
            rowRect.Contains(Event.current.mousePosition))
        {
            FocusPoiInEditor(point);
            Event.current.Use();
        }
    }

    private static void DrawMetricField(string header, float metricMs, float budgetMs)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(72.0f)))
        {
            EditorGUILayout.LabelField(header, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(72.0f));
            Color previousColor = GUI.contentColor;
            GUI.contentColor = PerformancePoiMetricDisplayUtility.EvaluateMetricColor(metricMs, budgetMs);
            EditorGUILayout.LabelField(
                PerformancePoiMetricDisplayUtility.FormatMetric(metricMs),
                MetricsValueStyle,
                GUILayout.Width(72.0f));
            GUI.contentColor = previousColor;
        }
    }

    private static GUIStyle MetricsRowTitleStyle
    {
        get
        {
            if (_metricsRowTitleStyle == null)
            {
                _metricsRowTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.UpperLeft
                };
            }

            return _metricsRowTitleStyle;
        }
    }

    private static GUIStyle MetricsFocusedTitleStyle
    {
        get
        {
            if (_metricsFocusedTitleStyle == null)
            {
                _metricsFocusedTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.UpperLeft
                };
            }

            return _metricsFocusedTitleStyle;
        }
    }

    private static GUIStyle MetricsValueStyle
    {
        get
        {
            if (_metricsValueStyle == null)
            {
                _metricsValueStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.UpperCenter
                };
            }

            return _metricsValueStyle;
        }
    }

    private static void FocusPoiInEditor(PerformancePoiPoint point)
    {
        if (point == null || point.gameObject == null)
            return;

        Selection.activeGameObject = point.gameObject;
        EditorGUIUtility.PingObject(point.gameObject);

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
            return;

        sceneView.FrameSelected();
        sceneView.Repaint();
    }

    private void CreateSampler()
    {
        GameObject gameObject = new GameObject("PerformancePoiSampler");
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Performance POI Sampler");
        gameObject.AddComponent<PerformancePoiSampler>();
        PlaceObjectInScene(gameObject);
        Selection.activeGameObject = gameObject;
        SetSampler(gameObject.GetComponent<PerformancePoiSampler>());
    }

    private static void CountPoiPoints(out int totalCount, out int automaticCount)
    {
        totalCount = 0;
        automaticCount = 0;

        PerformancePoiPoint[] points = UnityEngine.Object.FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiPoint point = points[index];
            if (point == null)
                continue;

            totalCount++;
            if (point.isActiveAndEnabled && point.IncludeInAutomaticRuns)
                automaticCount++;
        }
    }

    private void RefreshWindowState(bool resolveSampler, bool invalidateSavedSceneAnalysis)
    {
        if (resolveSampler)
            TryRecoverSamplerReference();

        TryRecoverTracePointReference();

        int assignedPoiKeyCount = EnsurePoiKeysSerializedInLoadedScenes();
        _cachedPoiKeyStatusMessage = assignedPoiKeyCount > 0
            ? string.Format(
                CultureInfo.InvariantCulture,
                "已为 {0} 个旧 POI 实例补写内部 PoiKey。请先保存场景，再重新录制缺失的 trace，然后再运行 Development Build。",
                assignedPoiKeyCount)
            : string.Empty;

        if (invalidateSavedSceneAnalysis)
            InvalidateSavedSceneAnalysis();

        UpdateSamplerDisplayCache();
        CountPoiPoints(out _cachedTotalPoiCount, out _cachedAutomaticPoiCount);
        _cachedDuplicatePoiIdWarning = BuildDuplicatePoiIdWarning();
        Repaint();
    }

    private void UpdateSamplerDisplayCache()
    {
        if (_sampler == null)
        {
            _cachedSamplerHierarchyPath = string.Empty;
            _cachedSamplerSceneName = string.Empty;
            return;
        }

        _cachedSamplerHierarchyPath = BuildHierarchyPath(_sampler.transform);
        _cachedSamplerSceneName = _sampler.gameObject.scene.name;
    }

    private static int EnsurePoiKeysSerializedInLoadedScenes()
    {
        int assignedCount = 0;
        PerformancePoiPoint[] points = UnityEngine.Object.FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiPoint point = points[index];
            if (point == null)
                continue;

            SerializedObject serializedPoint = new SerializedObject(point);
            SerializedProperty poiKeyProperty = serializedPoint.FindProperty("_poiKey");
            if (poiKeyProperty == null)
                continue;

            serializedPoint.Update();
            string currentPoiKey = poiKeyProperty.stringValue;
            string desiredPoiKey = point.BuildEditorPersistentPoiKey();
            bool shouldAssign = string.IsNullOrWhiteSpace(currentPoiKey) || !LooksLikeEditorGlobalPoiKey(currentPoiKey);
            if (!shouldAssign ||
                string.IsNullOrWhiteSpace(desiredPoiKey) ||
                string.Equals(currentPoiKey, desiredPoiKey, StringComparison.Ordinal))
                continue;

            poiKeyProperty.stringValue = desiredPoiKey;
            serializedPoint.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(point);
            if (point.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(point.gameObject.scene);

            assignedCount++;
        }

        return assignedCount;
    }

    private static bool LooksLikeEditorGlobalPoiKey(string poiKey)
    {
        return !string.IsNullOrWhiteSpace(poiKey) &&
            poiKey.StartsWith("GlobalObjectId_V1-", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDuplicatePoiIdWarning()
    {
        Dictionary<string, int> countsByPoiId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        PerformancePoiPoint[] points = UnityEngine.Object.FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiPoint point = points[index];
            if (point == null)
                continue;

            string poiId = point.PoiId;
            if (string.IsNullOrWhiteSpace(poiId))
                continue;

            if (!countsByPoiId.TryGetValue(poiId, out int count))
                count = 0;

            countsByPoiId[poiId] = count + 1;
        }

        List<string> duplicatePoiIds = new List<string>();
        foreach (KeyValuePair<string, int> entry in countsByPoiId)
        {
            if (entry.Value > 1)
                duplicatePoiIds.Add(string.Format(CultureInfo.InvariantCulture, "{0} x{1}", entry.Key, entry.Value));
        }

        if (duplicatePoiIds.Count == 0)
            return string.Empty;

        duplicatePoiIds.Sort(StringComparer.OrdinalIgnoreCase);
        return "检测到重复的 POI Id。旧结果和旧 trace 可能会发生冲突；建议重新录制这些 POI。重复项: " +
               string.Join(", ", duplicatePoiIds);
    }

    private static void PlaceObjectInScene(GameObject gameObject)
    {
        if (gameObject == null)
            return;

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
            gameObject.transform.position = sceneView.pivot;

        GameObject context = Selection.activeGameObject;
        if (context != null)
            GameObjectUtility.SetParentAndAlign(gameObject, context);
    }

    private string BuildExternalPlayerArguments()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);

        if (_externalPlayerForceD3D12)
            AppendCommandArgument(builder, "-force-d3d12");

        string startupScenePath = RenderDocPlayerBuildUtility.GetActiveEditorScenePath();
        if (!string.IsNullOrWhiteSpace(startupScenePath))
            AppendCommandArgument(builder, "--renderdoc-startup-scene", startupScenePath);

        AppendCommandArgument(builder, "--perf-poi-auto-run");
        AppendCommandArgument(builder, "--perf-poi-runner", _sampler != null ? _sampler.gameObject.name : "PerformancePoiSampler");
        AppendCommandArgument(builder, "--perf-poi-artifacts-root", _sampler != null ? _sampler.ResolvedArtifactsRoot : string.Empty);
        AppendCommandArgument(builder, "--perf-poi-trace-root", _sampler != null ? _sampler.ResolvedReplayTraceRoot : string.Empty);

        string runLabelOverride = string.IsNullOrWhiteSpace(_runLabelOverride) ? string.Empty : _runLabelOverride.Trim();
        if (!string.IsNullOrWhiteSpace(runLabelOverride))
            AppendCommandArgument(builder, "--perf-poi-run-label", runLabelOverride);

        if (_externalPlayerAutoQuit)
            AppendCommandArgument(builder, "--perf-poi-auto-quit");

        return builder.ToString().Trim();
    }

    private static void AppendCommandArgument(System.Text.StringBuilder builder, string argumentName, string argumentValue = null)
    {
        if (builder == null || string.IsNullOrWhiteSpace(argumentName))
            return;

        if (builder.Length > 0)
            builder.Append(' ');

        builder.Append(argumentName);
        if (argumentValue == null)
            return;

        builder.Append(' ');
        builder.Append(QuoteArgument(argumentValue));
    }

    private bool TryValidateExternalPlayerRun(out string message)
    {
        if (!TryBuildExternalPlayerPreflightMessage(out string preflightMessage, out MessageType preflightMessageType))
        {
            message = preflightMessage;
            return false;
        }

        if (preflightMessageType == MessageType.Warning)
        {
            message = preflightMessage;
            return false;
        }

        message = string.Empty;
        return true;
    }

    private bool TryBuildExternalPlayerPreflightMessage(out string message, out MessageType messageType)
    {
        if (_sampler == null)
        {
            message = "当前没有选中可用的 PerformancePoiSampler。";
            messageType = MessageType.Warning;
            return false;
        }

        SceneSetupValidation validation = ValidateSavedStartupSceneForPoi();
        message = validation.message;
        messageType = validation.messageType;
        return validation.isValid;
    }

    private SceneSetupValidation ValidateSavedStartupSceneForPoi()
    {
        var activeScene = EditorSceneManager.GetActiveScene();
        if (!activeScene.IsValid() || string.IsNullOrWhiteSpace(activeScene.path))
        {
            return new SceneSetupValidation(
                false,
                MessageType.Warning,
                "当前启动场景还没有保存成 .unity 文件。外部 Development Build 无法稳定包含这些 POI。请先保存场景，再 Build/Run。");
        }

        if (activeScene.isDirty)
        {
            return new SceneSetupValidation(
                false,
                MessageType.Warning,
                string.Format(
                    "当前启动场景 `{0}` 还有未保存修改。外部包不会包含这些临时 POI / sampler 变更。请先保存场景，再运行 Development Build。",
                    activeScene.path));
        }

        if (!File.Exists(activeScene.path))
        {
            return new SceneSetupValidation(
                false,
                MessageType.Warning,
                "当前启动场景文件不存在，无法校验 Development Build 是否包含 POI。");
        }

        EnsureSavedSceneAnalysis(activeScene.path);

        if (!_savedSceneHasSampler)
        {
            return new SceneSetupValidation(
                false,
                MessageType.Error,
                string.Format(
                    "当前保存到磁盘的启动场景 `{0}` 里没有 `PerformancePoiSampler`。外部包启动后不会触发 POI 自动采样。",
                    activeScene.path));
        }

        if (!_savedSceneHasPoint)
        {
            return new SceneSetupValidation(
                false,
                MessageType.Error,
                string.Format(
                    "当前保存到磁盘的启动场景 `{0}` 里没有 `PerformancePoiPoint`。外部包即使启动了 sampler，也不会采到任何点位。",
                    activeScene.path));
        }

        if (TryBuildMissingTraceSummary(out string missingTraceSummary))
        {
            return new SceneSetupValidation(
                false,
                MessageType.Warning,
                "以下自动 POI 还没有已保存的 replay trace。Development Build 启动后会跳过这些点位：\n" + missingTraceSummary);
        }

        return new SceneSetupValidation(
            true,
            MessageType.Info,
            string.Format(
                "启动预检通过：`{0}` 已保存，而且磁盘上的场景文件里包含 `PerformancePoiSampler` 和 `PerformancePoiPoint`。外部包会按当前场景做 POI 自动采样。",
                activeScene.path));
    }

    private bool TryBuildMissingTraceSummary(out string summary)
    {
        summary = string.Empty;

        PerformancePoiPoint[] points = UnityEngine.Object.FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (points == null || points.Length == 0)
            return false;

        List<string> missingPoiIds = new List<string>();
        Dictionary<string, int> poiIdCounts = BuildPoiIdCounts(points);
        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiPoint point = points[index];
            if (point == null || !point.IncludeInAutomaticRuns || !point.gameObject.activeInHierarchy)
                continue;

            if (!point.RequiresReplayTrace)
                continue;

            string tracePath = point.BuildReplayTracePath();
            if (File.Exists(tracePath))
                continue;

            string legacyTracePath = point.BuildLegacyReplayTracePath();
            bool hasLegacyTrace = !string.IsNullOrWhiteSpace(legacyTracePath) && File.Exists(legacyTracePath);
            bool hasDuplicatePoiId = poiIdCounts.TryGetValue(point.PoiId, out int poiIdCount) && poiIdCount > 1;
            if (hasLegacyTrace && hasDuplicatePoiId)
            {
                missingPoiIds.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} -> {1} (发现旧 trace {2}，但当前场景里这个 PoiId 重复，不能安全复用；请重新录制)",
                    point.PoiId,
                    tracePath,
                    legacyTracePath));
                continue;
            }

            if (hasLegacyTrace)
            {
                missingPoiIds.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} -> {1} (发现旧 trace {2}；建议重新录制并保存到新路径)",
                    point.PoiId,
                    tracePath,
                    legacyTracePath));
                continue;
            }

            missingPoiIds.Add(string.Format("{0} -> {1}", point.PoiId, tracePath));
        }

        if (missingPoiIds.Count == 0)
            return false;

        summary = string.Join("\n", missingPoiIds);
        return true;
    }

    private bool TryBuildAutomaticPoiTraceStatusLines(out List<string> statusLines, out MessageType messageType)
    {
        statusLines = new List<string>();
        messageType = MessageType.Info;

        PerformancePoiPoint[] points = UnityEngine.Object.FindObjectsByType<PerformancePoiPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (points == null || points.Length == 0)
            return false;

        Dictionary<string, int> poiIdCounts = BuildPoiIdCounts(points);
        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiPoint point = points[index];
            if (point == null || !point.IncludeInAutomaticRuns || !point.gameObject.activeInHierarchy)
                continue;

            string tracePath = point.BuildReplayTracePath();
            string legacyTracePath = point.BuildLegacyReplayTracePath();
            bool hasNewTrace = File.Exists(tracePath);
            bool hasLegacyTrace = !string.IsNullOrWhiteSpace(legacyTracePath) && File.Exists(legacyTracePath);
            bool hasDuplicatePoiId = poiIdCounts.TryGetValue(point.PoiId, out int poiIdCount) && poiIdCount > 1;
            string poiLabel = point.BuildPoiStorageLabel();

            if (!point.RequiresReplayTrace)
            {
                statusLines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "[转圈采样] {0}\nTurntableSweep: {1:0.###} 度/秒，自动转一圈，不需要 replay trace",
                    poiLabel,
                    point.ResolveTurntableDegreesPerSecond()));
                continue;
            }

            if (hasNewTrace)
            {
                statusLines.Add(string.Format(CultureInfo.InvariantCulture, "[就绪] {0}\n新 trace: {1}", poiLabel, tracePath));
                continue;
            }

            if (hasLegacyTrace)
            {
                messageType = MessageType.Warning;
                if (hasDuplicatePoiId)
                {
                    statusLines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "[仅旧版 / 冲突] {0}\n期望新 trace: {1}\n发现旧 trace: {2}\n当前场景里存在重复 PoiId，不能安全复用旧 trace",
                        poiLabel,
                        tracePath,
                        legacyTracePath));
                }
                else
                {
                    statusLines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "[仅旧版] {0}\n期望新 trace: {1}\n发现旧 trace: {2}",
                        poiLabel,
                        tracePath,
                        legacyTracePath));
                }

                continue;
            }

            messageType = MessageType.Warning;
            statusLines.Add(string.Format(CultureInfo.InvariantCulture, "[缺失] {0}\n期望新 trace: {1}", poiLabel, tracePath));
        }

        return statusLines.Count > 0;
    }

    private static Dictionary<string, int> BuildPoiIdCounts(PerformancePoiPoint[] points)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (points == null)
            return counts;

        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiPoint point = points[index];
            if (point == null || string.IsNullOrWhiteSpace(point.PoiId))
                continue;

            if (!counts.TryGetValue(point.PoiId, out int count))
                count = 0;

            counts[point.PoiId] = count + 1;
        }

        return counts;
    }

    private void EnsureSavedSceneAnalysis(string scenePath)
    {
        if (_savedSceneAnalysisCached &&
            string.Equals(_savedSceneAnalysisPath, scenePath, StringComparison.OrdinalIgnoreCase))
            return;

        string sceneYaml = File.ReadAllText(scenePath);
        string samplerGuid = AssetDatabase.AssetPathToGUID(PerformancePoiSamplerScriptPath);
        string pointGuid = AssetDatabase.AssetPathToGUID(PerformancePoiPointScriptPath);

        _savedSceneAnalysisPath = scenePath;
        _savedSceneAnalysisCached = true;
        _savedSceneHasSampler = !string.IsNullOrWhiteSpace(samplerGuid) &&
            sceneYaml.Contains(samplerGuid, StringComparison.OrdinalIgnoreCase);
        _savedSceneHasPoint = !string.IsNullOrWhiteSpace(pointGuid) &&
            sceneYaml.Contains(pointGuid, StringComparison.OrdinalIgnoreCase);
    }

    private void InvalidateSavedSceneAnalysis()
    {
        _savedSceneAnalysisCached = false;
        _savedSceneAnalysisPath = string.Empty;
        _savedSceneHasSampler = false;
        _savedSceneHasPoint = false;
    }

    private static void RevealPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!Directory.Exists(path) && !File.Exists(path))
            return;

        EditorUtility.RevealInFinder(path);
    }

    private string GetPreferredExternalPlayerDirectory()
    {
        string executablePath = string.IsNullOrWhiteSpace(_externalPlayerExecutablePath)
            ? RenderDocPlayerBuildUtility.GetDefaultExecutablePath()
            : _externalPlayerExecutablePath;
        string directory = Path.GetDirectoryName(executablePath);
        return string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
    }

    private void TrackPendingExternalPlayerRun(System.Diagnostics.Process process)
    {
        _pendingExternalPlayerProcessId = process != null ? process.Id : 0;
        _pendingExternalPlayerLaunchUtcTicks = DateTime.UtcNow.Ticks;
        _pendingExternalPlayerArtifactsRoot = _sampler != null ? _sampler.ResolvedArtifactsRoot : string.Empty;
        _pendingExternalPlayerRunLabel = string.IsNullOrWhiteSpace(_runLabelOverride) ? string.Empty : _runLabelOverride.Trim();
        _pendingExternalPlayerMaterialization = _pendingExternalPlayerProcessId > 0;
    }

    private bool IsPendingExternalPlayerStillRunning()
    {
        if (_pendingExternalPlayerProcessId <= 0)
            return false;

        try
        {
            System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(_pendingExternalPlayerProcessId);
            return process != null && !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void MaterializePendingExternalPlayerProfilerArtifacts()
    {
        _pendingExternalPlayerMaterialization = false;

        try
        {
            string summaryPath = FindPendingRunSummaryPath();
            if (string.IsNullOrWhiteSpace(summaryPath))
            {
                SetExternalPlayerStatus("外部 Player 已退出，但没有找到本次 run 的 `poi_run_summary.json`。");
                return;
            }

            SetExternalPlayerStatus("外部 Player 已退出，正在将 `.raw` 物化为 AI 数据包...");
            EditorUtility.DisplayProgressBar(WindowTitle, "Materializing profiler AI packages...", 0.1f);
            PerformancePoiProfilerAiPackageBuilder.MaterializationReport report =
                PerformancePoiProfilerAiPackageBuilder.MaterializeFromRunSummary(
                    summaryPath,
                    (message, progress) => EditorUtility.DisplayProgressBar(WindowTitle, message, Mathf.Clamp01(progress)));

            StringBuilder statusBuilder = new StringBuilder(256);
            statusBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "已完成 profiler AI 数据包物化：{0}/{1}",
                report.generatedPackageCount,
                report.rawExportCount);
            statusBuilder.AppendLine();
            statusBuilder.Append(summaryPath);

            if (report.warnings != null && report.warnings.Length > 0)
            {
                statusBuilder.AppendLine();
                statusBuilder.Append("Warnings: ");
                statusBuilder.Append(string.Join(" | ", report.warnings));
            }

            SetExternalPlayerStatus(statusBuilder.ToString());
            PerformancePoiGizmoOverlayCache.Invalidate();
        }
        catch (Exception exception)
        {
            SetExternalPlayerStatus("Profiler AI 数据包物化失败：" + exception.Message);
            UnityEngine.Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            ClearPendingExternalPlayerRun();
        }
    }

    private string FindPendingRunSummaryPath()
    {
        string artifactsRoot = string.IsNullOrWhiteSpace(_pendingExternalPlayerArtifactsRoot)
            ? (_sampler != null ? _sampler.ResolvedArtifactsRoot : string.Empty)
            : _pendingExternalPlayerArtifactsRoot;
        if (string.IsNullOrWhiteSpace(artifactsRoot) || !Directory.Exists(artifactsRoot))
            return string.Empty;

        string[] summaryPaths = Directory.GetFiles(
            artifactsRoot,
            PerformancePoiSamplingArtifacts.SummaryJsonFileName,
            SearchOption.AllDirectories);
        if (summaryPaths == null || summaryPaths.Length == 0)
            return string.Empty;

        DateTime launchTimeUtc = _pendingExternalPlayerLaunchUtcTicks > 0
            ? new DateTime(_pendingExternalPlayerLaunchUtcTicks, DateTimeKind.Utc)
            : DateTime.MinValue;
        string latestPath = string.Empty;
        DateTime latestWriteTimeUtc = DateTime.MinValue;

        for (int index = 0; index < summaryPaths.Length; index++)
        {
            string path = summaryPaths[index];
            DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
            if (launchTimeUtc > DateTime.MinValue && writeTimeUtc < launchTimeUtc.AddMinutes(-5.0))
                continue;

            if (!string.IsNullOrWhiteSpace(_pendingExternalPlayerRunLabel))
            {
                string directoryName = Path.GetFileName(Path.GetDirectoryName(path));
                if (directoryName != null &&
                    directoryName.IndexOf(_pendingExternalPlayerRunLabel, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
            }

            if (writeTimeUtc <= latestWriteTimeUtc)
                continue;

            latestWriteTimeUtc = writeTimeUtc;
            latestPath = path;
        }

        if (!string.IsNullOrWhiteSpace(latestPath))
            return latestPath;

        return summaryPaths
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private void ClearPendingExternalPlayerRun()
    {
        _pendingExternalPlayerProcessId = 0;
        _pendingExternalPlayerLaunchUtcTicks = 0L;
        _pendingExternalPlayerArtifactsRoot = string.Empty;
        _pendingExternalPlayerRunLabel = string.Empty;
        _pendingExternalPlayerMaterialization = false;
    }

    private void SetExternalPlayerStatus(string message)
    {
        _externalPlayerStatusMessage = message ?? string.Empty;
        Repaint();
    }

    private void SetTraceStatus(string message)
    {
        _traceStatusMessage = message ?? string.Empty;
        Repaint();
    }

    private static string QuoteArgument(string value)
    {
        string safeValue = value ?? string.Empty;
        return string.Format("\"{0}\"", safeValue.Replace("\"", "\\\""));
    }

    private readonly struct SceneSetupValidation
    {
        public SceneSetupValidation(bool isValid, MessageType messageType, string message)
        {
            this.isValid = isValid;
            this.messageType = messageType;
            this.message = message ?? string.Empty;
        }

        public readonly bool isValid;
        public readonly MessageType messageType;
        public readonly string message;
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = string.Format("{0}/{1}", transform.name, path);
        }

        return path;
    }
}

internal static class PerformancePoiGizmoOverlaySettings
{
    private const string ShowLatestMetricsKey = "PerformancePoi.GizmoOverlay.ShowLatestMetrics";
    private const string GameThreadBudgetKey = "PerformancePoi.GizmoOverlay.GameThreadBudgetMs";
    private const string RenderThreadBudgetKey = "PerformancePoi.GizmoOverlay.RenderThreadBudgetMs";
    private const string GpuBudgetKey = "PerformancePoi.GizmoOverlay.GpuBudgetMs";
    private const float DefaultBudgetMs = 16.67f;

    public static bool ShowLatestMetrics
    {
        get => EditorPrefs.GetBool(ShowLatestMetricsKey, true);
        set
        {
            EditorPrefs.SetBool(ShowLatestMetricsKey, value);
            SceneView.RepaintAll();
        }
    }

    public static float GameThreadBudgetMs
    {
        get => GetBudget(GameThreadBudgetKey);
        set => SetBudget(GameThreadBudgetKey, value);
    }

    public static float RenderThreadBudgetMs
    {
        get => GetBudget(RenderThreadBudgetKey);
        set => SetBudget(RenderThreadBudgetKey, value);
    }

    public static float GpuBudgetMs
    {
        get => GetBudget(GpuBudgetKey);
        set => SetBudget(GpuBudgetKey, value);
    }

    private static float GetBudget(string key)
    {
        return Mathf.Max(0.01f, EditorPrefs.GetFloat(key, DefaultBudgetMs));
    }

    private static void SetBudget(string key, float value)
    {
        EditorPrefs.SetFloat(key, Mathf.Max(0.01f, value));
        SceneView.RepaintAll();
    }
}

internal static class PerformancePoiGizmoOverlayCache
{
    private const double RefreshIntervalSeconds = 1.0;

    private static readonly Dictionary<string, PerformancePoiSampleResult> LatestResultsByPoiKey =
        new Dictionary<string, PerformancePoiSampleResult>(StringComparer.OrdinalIgnoreCase);

    private static string _cachedScenePath = string.Empty;
    private static string _cachedSummaryPath = string.Empty;
    private static string _statusMessage = "No POI metrics loaded yet.";
    private static double _nextRefreshTime;

    public static bool TryGetMetrics(PerformancePoiPoint point, out PerformancePoiSampleResult result)
    {
        result = null;
        if (point == null)
            return false;

        EnsureLoaded(point.gameObject.scene);
        if (LatestResultsByPoiKey.TryGetValue(point.PoiKey, out result))
            return true;

        return LatestResultsByPoiKey.TryGetValue(point.PoiId, out result);
    }

    public static string GetLatestSummaryPath(Scene scene)
    {
        EnsureLoaded(scene);
        return _cachedSummaryPath;
    }

    public static string GetStatusMessage(Scene scene)
    {
        EnsureLoaded(scene);
        return _statusMessage;
    }

    public static void Invalidate()
    {
        _cachedScenePath = string.Empty;
        _cachedSummaryPath = string.Empty;
        _statusMessage = "POI gizmo results will refresh on next draw.";
        _nextRefreshTime = 0.0;
        LatestResultsByPoiKey.Clear();
        SceneView.RepaintAll();
    }

    private static void EnsureLoaded(Scene scene)
    {
        string scenePath = scene.path ?? string.Empty;
        double now = EditorApplication.timeSinceStartup;
        if (string.Equals(_cachedScenePath, scenePath, StringComparison.OrdinalIgnoreCase) &&
            now < _nextRefreshTime)
        {
            return;
        }

        _cachedScenePath = scenePath;
        _nextRefreshTime = now + RefreshIntervalSeconds;
        LoadLatestSummary(scene);
    }

    private static void LoadLatestSummary(Scene scene)
    {
        LatestResultsByPoiKey.Clear();
        _cachedSummaryPath = string.Empty;

        string artifactsRoot = PerformancePoiSamplingArtifacts.ResolveArtifactsRoot(
            string.Empty,
            PerformancePoiSamplingArtifacts.DefaultRelativeArtifactsRoot);
        if (!Directory.Exists(artifactsRoot))
        {
            _statusMessage = "No POI artifacts directory found yet.";
            return;
        }

        string[] summaryPaths = Directory.GetFiles(
            artifactsRoot,
            PerformancePoiSamplingArtifacts.SummaryJsonFileName,
            SearchOption.AllDirectories);
        Array.Sort(summaryPaths, CompareSummaryPathsByModifiedTimeDescending);

        for (int index = 0; index < summaryPaths.Length; index++)
        {
            string summaryPath = summaryPaths[index];
            if (!TryLoadSummary(summaryPath, out PerformancePoiRunSummary summary))
                continue;

            if (!SummaryMatchesScene(scene, summary))
                continue;

            _cachedSummaryPath = summaryPath;
            CacheSummaryResults(summary);
            _statusMessage = LatestResultsByPoiKey.Count > 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Loaded latest POI summary: {0}",
                    summaryPath)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Latest POI summary has no point data: {0}",
                    summaryPath);
            return;
        }

        _statusMessage = "No POI summary found for the current scene.";
    }

    private static void CacheSummaryResults(PerformancePoiRunSummary summary)
    {
        if (summary == null || summary.points == null)
            return;

        for (int index = 0; index < summary.points.Length; index++)
        {
            PerformancePoiSampleResult pointResult = summary.points[index];
            if (pointResult == null)
                continue;

            string key = !string.IsNullOrWhiteSpace(pointResult.poiKey)
                ? pointResult.poiKey.Trim()
                : pointResult.poiId?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            LatestResultsByPoiKey[key] = pointResult;
        }
    }

    private static bool TryLoadSummary(string summaryPath, out PerformancePoiRunSummary summary)
    {
        summary = null;

        try
        {
            string json = File.ReadAllText(summaryPath);
            summary = JsonUtility.FromJson<PerformancePoiRunSummary>(json);
            return summary != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool SummaryMatchesScene(Scene scene, PerformancePoiRunSummary summary)
    {
        if (summary == null)
            return false;

        if (!string.IsNullOrWhiteSpace(scene.path) &&
            !string.IsNullOrWhiteSpace(summary.scenePath) &&
            string.Equals(scene.path, summary.scenePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(scene.name, summary.sceneName, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareSummaryPathsByModifiedTimeDescending(string leftPath, string rightPath)
    {
        DateTime leftTime = File.GetLastWriteTimeUtc(leftPath);
        DateTime rightTime = File.GetLastWriteTimeUtc(rightPath);
        return rightTime.CompareTo(leftTime);
    }
}

internal static class PerformancePoiMetricDisplayUtility
{
    private static readonly Color GoodColor = new Color(0.27f, 0.82f, 0.38f, 1.0f);
    private static readonly Color WarningColor = new Color(0.95f, 0.76f, 0.20f, 1.0f);
    private static readonly Color BadColor = new Color(0.92f, 0.29f, 0.25f, 1.0f);

    public static Color EvaluateMetricColor(float metricMs, float budgetMs)
    {
        if (!float.IsFinite(metricMs) || metricMs < 0.0f)
            return Color.gray;

        float safeBudget = Mathf.Max(0.01f, budgetMs);
        float normalized = metricMs / safeBudget;
        if (normalized <= 0.65f)
            return GoodColor;

        if (normalized <= 1.0f)
            return Color.Lerp(GoodColor, WarningColor, Mathf.InverseLerp(0.65f, 1.0f, normalized));

        return Color.Lerp(WarningColor, BadColor, Mathf.Clamp01((normalized - 1.0f) / 0.35f));
    }

    public static string FormatMetric(float metricMs)
    {
        if (!float.IsFinite(metricMs) || metricMs < 0.0f)
            return "n/a";

        return metricMs.ToString("F2", CultureInfo.InvariantCulture);
    }
}

internal static class PerformancePoiGizmoOverlayDrawer
{
    private const float PanelWidth = 278.0f;
    private const float PanelHeight = 48.0f;
    private const float VerticalOffset = 0.56f;

    private static GUIStyle _panelStyle;
    private static GUIStyle _titleStyle;
    private static GUIStyle _headerStyle;
    private static GUIStyle _valueStyle;

    public static bool TryDraw(PerformancePoiPoint point)
    {
        if (point == null || !PerformancePoiGizmoOverlaySettings.ShowLatestMetrics)
            return false;

        if (!PerformancePoiGizmoOverlayCache.TryGetMetrics(point, out PerformancePoiSampleResult result))
            return false;

        SceneView sceneView = SceneView.currentDrawingSceneView ?? SceneView.lastActiveSceneView;
        if (sceneView == null || sceneView.camera == null)
            return false;

        Vector3 worldPosition = point.transform.position + Vector3.up * VerticalOffset;
        Vector3 viewportPoint = sceneView.camera.WorldToViewportPoint(worldPosition);
        if (viewportPoint.z <= 0.0f)
            return false;

        Vector2 guiPoint = HandleUtility.WorldToGUIPoint(worldPosition);
        Rect panelRect = new Rect(
            guiPoint.x - (PanelWidth * 0.5f),
            guiPoint.y - PanelHeight - 18.0f,
            PanelWidth,
            PanelHeight);

        Handles.BeginGUI();
        GUI.Box(panelRect, GUIContent.none, PanelStyle);

        Rect titleRect = new Rect(panelRect.x + 6.0f, panelRect.y + 4.0f, panelRect.width - 12.0f, 14.0f);
        GUI.Label(titleRect, string.Format("#{0} {1}", point.SortIndex, point.PoiId), TitleStyle);

        float columnsY = panelRect.y + 22.0f;
        float columnWidth = (panelRect.width - 12.0f) / 3.0f;
        DrawMetricColumn(
            new Rect(panelRect.x + 4.0f, columnsY, columnWidth, 20.0f),
            "Game",
            result.averageMainThreadMs,
            PerformancePoiGizmoOverlaySettings.GameThreadBudgetMs);
        DrawMetricColumn(
            new Rect(panelRect.x + 4.0f + columnWidth, columnsY, columnWidth, 20.0f),
            "Render",
            result.averageRenderThreadMs,
            PerformancePoiGizmoOverlaySettings.RenderThreadBudgetMs);
        DrawMetricColumn(
            new Rect(panelRect.x + 4.0f + (columnWidth * 2.0f), columnsY, columnWidth, 20.0f),
            "GPU",
            result.averageGpuFrameMs,
            PerformancePoiGizmoOverlaySettings.GpuBudgetMs);

        Handles.EndGUI();
        return true;
    }

    private static void DrawMetricColumn(Rect rect, string header, float metricMs, float budgetMs)
    {
        Rect headerRect = new Rect(rect.x, rect.y, rect.width, 10.0f);
        Rect valueRect = new Rect(rect.x, rect.y + 9.0f, rect.width, 12.0f);

        GUI.Label(headerRect, header, HeaderStyle);

        Color previousContentColor = GUI.contentColor;
        GUI.contentColor = PerformancePoiMetricDisplayUtility.EvaluateMetricColor(metricMs, budgetMs);
        GUI.Label(valueRect, PerformancePoiMetricDisplayUtility.FormatMetric(metricMs), ValueStyle);
        GUI.contentColor = previousContentColor;
    }

    private static GUIStyle PanelStyle
    {
        get
        {
            if (_panelStyle == null)
            {
                _panelStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(6, 6, 4, 4)
                };
            }

            return _panelStyle;
        }
    }

    private static GUIStyle TitleStyle
    {
        get
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.UpperLeft
                };
            }

            return _titleStyle;
        }
    }

    private static GUIStyle HeaderStyle
    {
        get
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    alignment = TextAnchor.UpperCenter
                };
            }

            return _headerStyle;
        }
    }

    private static GUIStyle ValueStyle
    {
        get
        {
            if (_valueStyle == null)
            {
                _valueStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.UpperCenter
                };
            }

            return _valueStyle;
        }
    }
}

public static class PerformancePoiEditorMenus
{
    [MenuItem("GameObject/Qianxia/Performance/POI Point", false, 10)]
    private static void CreatePoiPoint()
    {
        GameObject gameObject = new GameObject("Performance POI");
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Performance POI");
        gameObject.AddComponent<PerformancePoiPoint>();
        PlaceObjectInScene(gameObject);
        Selection.activeGameObject = gameObject;
    }

    [MenuItem("GameObject/Qianxia/Performance/POI Sampler", false, 11)]
    private static void CreatePoiSampler()
    {
        GameObject gameObject = new GameObject("PerformancePoiSampler");
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Performance POI Sampler");
        gameObject.AddComponent<PerformancePoiSampler>();
        PlaceObjectInScene(gameObject);
        Selection.activeGameObject = gameObject;
    }

    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Active)]
    private static void DrawPoiLabels(PerformancePoiPoint point, GizmoType gizmoType)
    {
        if (point == null)
            return;

        if (PerformancePoiGizmoOverlayDrawer.TryDraw(point))
            return;

        Handles.color = Color.white;
        Handles.Label(
            point.transform.position + Vector3.up * 0.45f,
            string.Format("#{0} {1} [{2}]", point.SortIndex, point.PoiId, point.Category));
    }

    private static void PlaceObjectInScene(GameObject gameObject)
    {
        if (gameObject == null)
            return;

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
            gameObject.transform.position = sceneView.pivot;

        GameObject context = Selection.activeGameObject;
        if (context != null)
            GameObjectUtility.SetParentAndAlign(gameObject, context);
    }
}

public static class PerformancePoiProfilerAiPackageBuilder
{
    private const string SessionManifestFileName = "session_manifest.json";
    private const string FrameIndexFileName = "frame_index.csv";
    private const string ThreadIndexFileName = "thread_index.csv";
    private const string ThreadOverviewFileName = "thread_overview.json";
    private const string DefaultRelativeProfilerAnalysisRoot = ".workspace/artifacts/profiler-analysis";

    [Serializable]
    public sealed class MaterializationReport
    {
        public string summaryJsonPath = string.Empty;
        public string artifactDirectory = string.Empty;
        public int rawExportCount;
        public int generatedPackageCount;
        public int skippedPackageCount;
        public string[] packageDirectories = Array.Empty<string>();
        public string[] warnings = Array.Empty<string>();
    }

    [Serializable]
    private sealed class SessionManifestEnvelope
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
        public string frameIndexPath = string.Empty;
        public string threadIndexPath = string.Empty;
        public string threadOverviewPath = string.Empty;
    }

    [Serializable]
    private sealed class AiPackageManifest
    {
        public int version = 1;
        public string packageType = "unity-profiler-ai-package";
        public string generatedAtLocal = string.Empty;
        public string generatedAtUtc = string.Empty;
        public string runId = string.Empty;
        public string runLabel = string.Empty;
        public string artifactDirectory = string.Empty;
        public string summaryJsonPath = string.Empty;
        public string summaryMarkdownPath = string.Empty;
        public string summaryCsvPath = string.Empty;
        public int orderIndex;
        public string poiId = string.Empty;
        public string poiName = string.Empty;
        public string category = string.Empty;
        public string runMode = string.Empty;
        public string notes = string.Empty;
        public string packageDirectory = string.Empty;
        public string sourceRawPath = string.Empty;
        public long sourceRawSizeBytes;
        public string screenshotPath = string.Empty;
        public string renderDocCapturePath = string.Empty;
        public string sessionId = string.Empty;
        public string sessionArtifactDirectory = string.Empty;
        public string sessionManifestPath = string.Empty;
        public string frameIndexPath = string.Empty;
        public string threadIndexPath = string.Empty;
        public string threadOverviewPath = string.Empty;
        public string captureSummaryPath = string.Empty;
        public int firstFrameIndex = -1;
        public int lastFrameIndex = -1;
        public int frameCount;
        public string[] availableModules = Array.Empty<string>();
        public float averageFrameMs = -1.0f;
        public float p95FrameMs = -1.0f;
        public float p99FrameMs = -1.0f;
        public float averageCpuFrameMs = -1.0f;
        public float averageGpuFrameMs = -1.0f;
        public float averageMainThreadMs = -1.0f;
        public float averageRenderThreadMs = -1.0f;
        public float averageDrawCalls = -1.0f;
        public string dataQuality = string.Empty;
    }

    [Serializable]
    private sealed class CaptureSummary
    {
        public int version = 1;
        public string sessionId = string.Empty;
        public string sourceRawPath = string.Empty;
        public int firstFrameIndex = -1;
        public int lastFrameIndex = -1;
        public int frameCount;
        public string primaryFrameMetric = "cpu_frame_ms";
        public FloatStats cpuFrameMs = FloatStats.Empty();
        public FloatStats mainThreadMs = FloatStats.Empty();
        public FloatStats renderThreadMs = FloatStats.Empty();
        public FloatStats gpuFrameMs = FloatStats.Empty();
        public FloatStats fps = FloatStats.Empty();
        public long totalGcAllocBytes;
        public int framesOver16_67Ms;
        public int framesOver33_33Ms;
        public BudgetRun[] thresholdRunsOver16_67Ms = Array.Empty<BudgetRun>();
        public BudgetRun[] thresholdRunsOver33_33Ms = Array.Empty<BudgetRun>();
        public SlowFrame[] slowestFrames = Array.Empty<SlowFrame>();
    }

    [Serializable]
    private sealed class FloatStats
    {
        public int count;
        public float min = -1.0f;
        public float max = -1.0f;
        public float average = -1.0f;
        public float median = -1.0f;
        public float p95 = -1.0f;

        public static FloatStats Empty()
        {
            return new FloatStats();
        }
    }

    [Serializable]
    private sealed class BudgetRun
    {
        public int startFrameIndex;
        public int endFrameIndex;
        public int frameCount;
        public float averageFrameMs;
        public float maxFrameMs;
        public int representativeFrameIndex;
    }

    [Serializable]
    private sealed class SlowFrame
    {
        public int frameIndex;
        public float cpuFrameMs = -1.0f;
        public float mainThreadMs = -1.0f;
        public float renderThreadMs = -1.0f;
        public float gpuFrameMs = -1.0f;
        public long gcAllocBytes = -1L;
    }

    private sealed class FrameRow
    {
        public int frameIndex;
        public float cpuFrameMs = -1.0f;
        public float gpuFrameMs = -1.0f;
        public float fps = -1.0f;
        public float mainThreadMs = -1.0f;
        public float renderThreadMs = -1.0f;
        public long gcAllocBytes = -1L;

        public float PrimaryFrameMetric => cpuFrameMs >= 0.0f ? cpuFrameMs : mainThreadMs;
    }

    public static MaterializationReport MaterializeFromRunSummary(string summaryJsonPath, Action<string, float> progress = null)
    {
        string resolvedSummaryPath = RequireExistingFile(summaryJsonPath, "POI summary json");
        PerformancePoiRunSummary summary = ReadJson<PerformancePoiRunSummary>(resolvedSummaryPath);
        if (summary == null)
            throw new InvalidOperationException("Failed to read POI summary json.");

        summary.summaryJsonPath = resolvedSummaryPath;
        summary.artifactDirectory = ResolveRunArtifactDirectory(summary, resolvedSummaryPath);
        summary.summaryMarkdownPath = ResolveSummaryPath(summary.summaryMarkdownPath, summary.artifactDirectory, PerformancePoiSamplingArtifacts.BuildSummaryMarkdownPath);
        summary.summaryCsvPath = ResolveSummaryPath(summary.summaryCsvPath, summary.artifactDirectory, PerformancePoiSamplingArtifacts.BuildSummaryCsvPath);

        PerformancePoiSampleResult[] points = summary.points ?? Array.Empty<PerformancePoiSampleResult>();
        string profilerAnalysisRoot = ResolveProfilerAnalysisRoot();
        Directory.CreateDirectory(profilerAnalysisRoot);

        int rawExportCount = points.Count(point => point != null && point.profilerRawExported && !string.IsNullOrWhiteSpace(point.profilerRawPath));
        int processedRawCount = 0;
        int generatedPackageCount = 0;
        List<string> packageDirectories = new List<string>(Math.Max(1, rawExportCount));
        List<string> warnings = new List<string>();

        for (int index = 0; index < points.Length; index++)
        {
            PerformancePoiSampleResult point = points[index];
            if (point == null)
                continue;

            if (!point.profilerRawExported || string.IsNullOrWhiteSpace(point.profilerRawPath))
            {
                ClearAiPackageFields(point);
                continue;
            }

            processedRawCount++;
            progress?.Invoke(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Materializing profiler AI package {0}/{1}: {2}",
                    processedRawCount,
                    Math.Max(1, rawExportCount),
                    string.IsNullOrWhiteSpace(point.poiId) ? "poi" : point.poiId),
                rawExportCount <= 0 ? 1.0f : (float)processedRawCount / rawExportCount);

            try
            {
                MaterializePointPackage(summary, point, index, profilerAnalysisRoot, packageDirectories);
                generatedPackageCount++;
            }
            catch (Exception exception)
            {
                ClearAiPackageFields(point);
                string warning = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1}",
                    string.IsNullOrWhiteSpace(point.poiId) ? "poi" : point.poiId,
                    exception.Message);
                warnings.Add(warning);
                Debug.LogException(exception);
            }
        }

        summary.points = points;
        summary.completedProfilerAiPackageCount = generatedPackageCount;
        PerformancePoiSamplingArtifacts.WriteRunSummaryJson(summary.summaryJsonPath, summary);
        PerformancePoiSamplingArtifacts.WriteRunSummaryMarkdown(summary.summaryMarkdownPath, summary);
        PerformancePoiSamplingArtifacts.WriteRunSummaryCsv(summary.summaryCsvPath, summary);

        return new MaterializationReport
        {
            summaryJsonPath = summary.summaryJsonPath,
            artifactDirectory = summary.artifactDirectory,
            rawExportCount = rawExportCount,
            generatedPackageCount = generatedPackageCount,
            skippedPackageCount = Math.Max(0, rawExportCount - generatedPackageCount),
            packageDirectories = packageDirectories.ToArray(),
            warnings = warnings.ToArray()
        };
    }

    private static void MaterializePointPackage(
        PerformancePoiRunSummary summary,
        PerformancePoiSampleResult point,
        int pointIndex,
        string profilerAnalysisRoot,
        List<string> packageDirectories)
    {
        string rawPath = RequireExistingFile(point.profilerRawPath, "Profiler raw");
        string packageDirectory = PerformancePoiSamplingArtifacts.BuildProfilerAiPackageDirectoryPath(
            summary.artifactDirectory,
            pointIndex + 1,
            string.IsNullOrWhiteSpace(point.poiId) ? "poi" : point.poiId);
        Directory.CreateDirectory(packageDirectory);

        ProfilerAnalysisCli.ImportedSessionArtifact importedArtifact = ResolveOrImportSessionArtifact(rawPath, profilerAnalysisRoot);
        string sessionManifestPath = Path.Combine(packageDirectory, SessionManifestFileName);
        string frameIndexPath = Path.Combine(packageDirectory, FrameIndexFileName);
        string threadIndexPath = Path.Combine(packageDirectory, ThreadIndexFileName);
        string threadOverviewPath = Path.Combine(packageDirectory, ThreadOverviewFileName);
        string captureSummaryPath = Path.Combine(packageDirectory, PerformancePoiSamplingArtifacts.ProfilerCaptureSummaryFileName);
        string packageManifestPath = Path.Combine(packageDirectory, PerformancePoiSamplingArtifacts.ProfilerAiPackageManifestFileName);

        CopyArtifactFile(importedArtifact.manifestPath, sessionManifestPath);
        CopyArtifactFile(importedArtifact.frameIndexPath, frameIndexPath);
        CopyArtifactFile(importedArtifact.threadIndexPath, threadIndexPath);
        CopyArtifactFile(importedArtifact.threadOverviewPath, threadOverviewPath);

        SessionManifestEnvelope manifest = ReadJson<SessionManifestEnvelope>(sessionManifestPath);
        CaptureSummary captureSummary = BuildCaptureSummary(manifest, frameIndexPath);
        captureSummary.sourceRawPath = rawPath;
        WriteJson(captureSummaryPath, captureSummary);

        FileInfo rawFile = new FileInfo(rawPath);
        AiPackageManifest packageManifest = new AiPackageManifest
        {
            generatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            runId = summary.runId ?? string.Empty,
            runLabel = summary.runLabel ?? string.Empty,
            artifactDirectory = summary.artifactDirectory ?? string.Empty,
            summaryJsonPath = summary.summaryJsonPath ?? string.Empty,
            summaryMarkdownPath = summary.summaryMarkdownPath ?? string.Empty,
            summaryCsvPath = summary.summaryCsvPath ?? string.Empty,
            orderIndex = pointIndex + 1,
            poiId = point.poiId ?? string.Empty,
            poiName = point.poiName ?? string.Empty,
            category = point.category ?? string.Empty,
            runMode = point.runMode ?? string.Empty,
            notes = point.notes ?? string.Empty,
            packageDirectory = packageDirectory,
            sourceRawPath = rawPath,
            sourceRawSizeBytes = rawFile.Exists ? rawFile.Length : point.profilerRawFileSizeBytes,
            screenshotPath = point.screenshotPath ?? string.Empty,
            renderDocCapturePath = point.renderDocCapturePath ?? string.Empty,
            sessionId = importedArtifact.sessionId ?? string.Empty,
            sessionArtifactDirectory = importedArtifact.artifactDir ?? string.Empty,
            sessionManifestPath = sessionManifestPath,
            frameIndexPath = frameIndexPath,
            threadIndexPath = threadIndexPath,
            threadOverviewPath = threadOverviewPath,
            captureSummaryPath = captureSummaryPath,
            firstFrameIndex = manifest.firstFrameIndex,
            lastFrameIndex = manifest.lastFrameIndex,
            frameCount = manifest.frameCount,
            availableModules = manifest.availableModules ?? Array.Empty<string>(),
            averageFrameMs = point.averageFrameMs,
            p95FrameMs = point.p95FrameMs,
            p99FrameMs = point.p99FrameMs,
            averageCpuFrameMs = point.averageCpuFrameMs,
            averageGpuFrameMs = point.averageGpuFrameMs,
            averageMainThreadMs = point.averageMainThreadMs,
            averageRenderThreadMs = point.averageRenderThreadMs,
            averageDrawCalls = point.averageDrawCalls,
            dataQuality = point.dataQuality ?? string.Empty
        };
        WriteJson(packageManifestPath, packageManifest);

        point.profilerAiPackageGenerated = true;
        point.profilerAiPackageDirectory = packageDirectory;
        point.profilerAiPackageManifestPath = packageManifestPath;
        point.profilerCaptureSummaryPath = captureSummaryPath;
        point.profilerSessionId = importedArtifact.sessionId ?? string.Empty;
        point.profilerSessionArtifactDirectory = importedArtifact.artifactDir ?? string.Empty;
        packageDirectories.Add(packageDirectory);
    }

    private static void ClearAiPackageFields(PerformancePoiSampleResult point)
    {
        point.profilerAiPackageGenerated = false;
        point.profilerAiPackageDirectory = string.Empty;
        point.profilerAiPackageManifestPath = string.Empty;
        point.profilerCaptureSummaryPath = string.Empty;
        point.profilerSessionId = string.Empty;
        point.profilerSessionArtifactDirectory = string.Empty;
    }

    private static ProfilerAnalysisCli.ImportedSessionArtifact ResolveOrImportSessionArtifact(string rawPath, string profilerAnalysisRoot)
    {
        string existingArtifactDirectory = TryFindExistingSessionArtifactDirectory(rawPath, profilerAnalysisRoot);
        if (!string.IsNullOrWhiteSpace(existingArtifactDirectory))
            return ReadImportedSessionArtifact(existingArtifactDirectory);

        string sessionId = BuildSessionArtifactId(rawPath);
        string artifactDirectory = Path.Combine(profilerAnalysisRoot, sessionId);
        return ProfilerAnalysisCli.ImportSessionArtifact(rawPath, artifactDirectory, sessionId);
    }

    private static string TryFindExistingSessionArtifactDirectory(string rawPath, string profilerAnalysisRoot)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || !Directory.Exists(profilerAnalysisRoot))
            return string.Empty;

        string normalizedRawPath = NormalizePath(rawPath);
        string[] manifestPaths = Directory.GetFiles(profilerAnalysisRoot, SessionManifestFileName, SearchOption.AllDirectories);
        string latestDirectory = string.Empty;
        DateTime latestWriteTimeUtc = DateTime.MinValue;

        for (int index = 0; index < manifestPaths.Length; index++)
        {
            string manifestPath = manifestPaths[index];
            try
            {
                SessionManifestEnvelope manifest = ReadJson<SessionManifestEnvelope>(manifestPath);
                if (!string.Equals(NormalizePath(manifest.sourcePath), normalizedRawPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime manifestWriteTimeUtc = File.GetLastWriteTimeUtc(manifestPath);
                if (manifestWriteTimeUtc <= latestWriteTimeUtc)
                    continue;

                latestWriteTimeUtc = manifestWriteTimeUtc;
                latestDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            }
            catch
            {
            }
        }

        return latestDirectory;
    }

    private static ProfilerAnalysisCli.ImportedSessionArtifact ReadImportedSessionArtifact(string artifactDirectory)
    {
        string sessionManifestPath = Path.Combine(artifactDirectory, SessionManifestFileName);
        SessionManifestEnvelope manifest = ReadJson<SessionManifestEnvelope>(sessionManifestPath);
        if (manifest == null)
            throw new InvalidOperationException("Profiler session manifest is missing or invalid.");

        return new ProfilerAnalysisCli.ImportedSessionArtifact
        {
            sessionId = manifest.sessionId ?? string.Empty,
            artifactDir = artifactDirectory,
            sourcePath = manifest.sourcePath ?? string.Empty,
            sourceType = manifest.sourceType ?? string.Empty,
            firstFrameIndex = manifest.firstFrameIndex,
            lastFrameIndex = manifest.lastFrameIndex,
            frameCount = manifest.frameCount,
            availableModules = manifest.availableModules ?? Array.Empty<string>(),
            manifestPath = sessionManifestPath,
            frameIndexPath = Path.Combine(artifactDirectory, FrameIndexFileName),
            threadIndexPath = Path.Combine(artifactDirectory, ThreadIndexFileName),
            threadOverviewPath = Path.Combine(artifactDirectory, ThreadOverviewFileName)
        };
    }

    private static CaptureSummary BuildCaptureSummary(SessionManifestEnvelope manifest, string frameIndexPath)
    {
        List<FrameRow> rows = ReadFrameRows(frameIndexPath);
        List<float> cpuSamples = new List<float>(rows.Count);
        List<float> mainSamples = new List<float>(rows.Count);
        List<float> renderSamples = new List<float>(rows.Count);
        List<float> gpuSamples = new List<float>(rows.Count);
        List<float> fpsSamples = new List<float>(rows.Count);
        long totalGcAllocBytes = 0L;
        int framesOver16_67Ms = 0;
        int framesOver33_33Ms = 0;

        for (int index = 0; index < rows.Count; index++)
        {
            FrameRow row = rows[index];
            AddSample(cpuSamples, row.cpuFrameMs);
            AddSample(mainSamples, row.mainThreadMs);
            AddSample(renderSamples, row.renderThreadMs);
            AddSample(gpuSamples, row.gpuFrameMs);
            AddSample(fpsSamples, row.fps);

            if (row.gcAllocBytes > 0)
                totalGcAllocBytes += row.gcAllocBytes;

            float frameMetric = row.PrimaryFrameMetric;
            if (frameMetric > 16.67f)
                framesOver16_67Ms++;
            if (frameMetric > 33.33f)
                framesOver33_33Ms++;
        }

        return new CaptureSummary
        {
            sessionId = manifest.sessionId ?? string.Empty,
            firstFrameIndex = manifest.firstFrameIndex,
            lastFrameIndex = manifest.lastFrameIndex,
            frameCount = manifest.frameCount,
            cpuFrameMs = BuildFloatStats(cpuSamples),
            mainThreadMs = BuildFloatStats(mainSamples),
            renderThreadMs = BuildFloatStats(renderSamples),
            gpuFrameMs = BuildFloatStats(gpuSamples),
            fps = BuildFloatStats(fpsSamples),
            totalGcAllocBytes = totalGcAllocBytes,
            framesOver16_67Ms = framesOver16_67Ms,
            framesOver33_33Ms = framesOver33_33Ms,
            thresholdRunsOver16_67Ms = BuildBudgetRuns(rows, 16.67f),
            thresholdRunsOver33_33Ms = BuildBudgetRuns(rows, 33.33f),
            slowestFrames = rows
                .OrderByDescending(item => item.PrimaryFrameMetric)
                .ThenByDescending(item => item.mainThreadMs)
                .Take(5)
                .Select(item => new SlowFrame
                {
                    frameIndex = item.frameIndex,
                    cpuFrameMs = item.cpuFrameMs,
                    mainThreadMs = item.mainThreadMs,
                    renderThreadMs = item.renderThreadMs,
                    gpuFrameMs = item.gpuFrameMs,
                    gcAllocBytes = item.gcAllocBytes
                })
                .ToArray()
        };
    }

    private static BudgetRun[] BuildBudgetRuns(List<FrameRow> rows, float thresholdMs)
    {
        List<BudgetRun> runs = new List<BudgetRun>();
        int index = 0;
        while (index < rows.Count)
        {
            if (rows[index].PrimaryFrameMetric <= thresholdMs)
            {
                index++;
                continue;
            }

            int startIndex = index;
            int representativeFrameIndex = rows[index].frameIndex;
            float maxFrameMs = rows[index].PrimaryFrameMetric;
            float totalFrameMs = 0.0f;
            int frameCount = 0;

            while (index < rows.Count && rows[index].PrimaryFrameMetric > thresholdMs)
            {
                float frameMetric = rows[index].PrimaryFrameMetric;
                totalFrameMs += frameMetric;
                frameCount++;

                if (frameMetric > maxFrameMs)
                {
                    maxFrameMs = frameMetric;
                    representativeFrameIndex = rows[index].frameIndex;
                }

                index++;
            }

            FrameRow startRow = rows[startIndex];
            FrameRow endRow = rows[Math.Max(startIndex, index - 1)];
            runs.Add(new BudgetRun
            {
                startFrameIndex = startRow.frameIndex,
                endFrameIndex = endRow.frameIndex,
                frameCount = frameCount,
                averageFrameMs = frameCount > 0 ? totalFrameMs / frameCount : 0.0f,
                maxFrameMs = maxFrameMs,
                representativeFrameIndex = representativeFrameIndex
            });
        }

        return runs.ToArray();
    }

    private static List<FrameRow> ReadFrameRows(string frameIndexPath)
    {
        string[] lines = File.ReadAllLines(frameIndexPath, Encoding.UTF8);
        List<FrameRow> rows = new List<FrameRow>(Math.Max(0, lines.Length - 1));
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] cells = line.Split(',');
            if (cells.Length < 9)
                continue;

            rows.Add(new FrameRow
            {
                frameIndex = ParseInt(cells, 0),
                cpuFrameMs = ParseFloat(cells, 1),
                gpuFrameMs = ParseFloat(cells, 2),
                fps = ParseFloat(cells, 3),
                mainThreadMs = ParseFloat(cells, 4),
                renderThreadMs = ParseFloat(cells, 5),
                gcAllocBytes = ParseLong(cells, 8)
            });
        }

        return rows;
    }

    private static FloatStats BuildFloatStats(List<float> samples)
    {
        if (samples == null || samples.Count == 0)
            return FloatStats.Empty();

        List<float> ordered = new List<float>(samples);
        ordered.Sort();

        float total = 0.0f;
        for (int index = 0; index < ordered.Count; index++)
            total += ordered[index];

        return new FloatStats
        {
            count = ordered.Count,
            min = ordered[0],
            max = ordered[ordered.Count - 1],
            average = total / ordered.Count,
            median = Percentile(ordered, 0.5f),
            p95 = Percentile(ordered, 0.95f)
        };
    }

    private static float Percentile(List<float> ordered, float percentile)
    {
        if (ordered == null || ordered.Count == 0)
            return -1.0f;

        if (ordered.Count == 1)
            return ordered[0];

        float clamped = Mathf.Clamp01(percentile);
        float scaledIndex = (ordered.Count - 1) * clamped;
        int lowerIndex = Mathf.FloorToInt(scaledIndex);
        int upperIndex = Mathf.CeilToInt(scaledIndex);
        if (lowerIndex == upperIndex)
            return ordered[lowerIndex];

        float t = scaledIndex - lowerIndex;
        return Mathf.Lerp(ordered[lowerIndex], ordered[upperIndex], t);
    }

    private static void AddSample(List<float> samples, float value)
    {
        if (samples == null || value < 0.0f || !float.IsFinite(value))
            return;

        samples.Add(value);
    }

    private static void CopyArtifactFile(string sourcePath, string destinationPath)
    {
        string resolvedSourcePath = RequireExistingFile(sourcePath, "Profiler artifact file");
        string directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.Copy(resolvedSourcePath, destinationPath, true);
    }

    private static string ResolveProfilerAnalysisRoot()
    {
        return RenderDocCaptureArtifacts.ResolveArtifactsRoot(string.Empty, DefaultRelativeProfilerAnalysisRoot);
    }

    private static string ResolveRunArtifactDirectory(PerformancePoiRunSummary summary, string summaryJsonPath)
    {
        if (!string.IsNullOrWhiteSpace(summary.artifactDirectory))
            return Path.GetFullPath(summary.artifactDirectory);

        string directory = Path.GetDirectoryName(summaryJsonPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Unable to resolve POI run artifact directory.");

        return directory;
    }

    private static string ResolveSummaryPath(string existingPath, string artifactDirectory, Func<string, string> fallbackBuilder)
    {
        if (!string.IsNullOrWhiteSpace(existingPath))
            return Path.GetFullPath(existingPath);

        return fallbackBuilder(artifactDirectory);
    }

    private static string BuildSessionArtifactId(string rawPath)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string fileName = Path.GetFileNameWithoutExtension(rawPath);
        StringBuilder builder = new StringBuilder(fileName.Length);
        for (int index = 0; index < fileName.Length; index++)
        {
            char character = fileName[index];
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
            else if (character == '-' || character == '_' || character == '.')
                builder.Append(character);
            else
                builder.Append('_');
        }

        string sanitized = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "session";

        return string.Format(CultureInfo.InvariantCulture, "{0}_{1}", timestamp, sanitized);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }

    private static int ParseInt(string[] cells, int index)
    {
        if (cells == null || index < 0 || index >= cells.Length)
            return -1;

        return int.TryParse(cells[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : -1;
    }

    private static long ParseLong(string[] cells, int index)
    {
        if (cells == null || index < 0 || index >= cells.Length)
            return -1L;

        return long.TryParse(cells[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : -1L;
    }

    private static float ParseFloat(string[] cells, int index)
    {
        if (cells == null || index < 0 || index >= cells.Length)
            return -1.0f;

        if (string.IsNullOrWhiteSpace(cells[index]))
            return -1.0f;

        return float.TryParse(cells[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : -1.0f;
    }

    private static string RequireExistingFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "{0} is required.", label));

        string resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException(string.Format(CultureInfo.InvariantCulture, "{0} was not found.", label), resolvedPath);

        return resolvedPath;
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

        File.WriteAllText(path, JsonUtility.ToJson(value, true) + Environment.NewLine, Encoding.UTF8);
    }
}
#endif
