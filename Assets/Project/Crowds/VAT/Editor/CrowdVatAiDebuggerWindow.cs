using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class CrowdVatAiDebuggerWindow : EditorWindow
{
    private const string WindowTitle = "AI Debugger V1.2";
    private const string ArtifactDirectoryName = ".workspace/artifacts/ai-debug";
    private static readonly string[] TargetKindLabels = { "Runtime ID", "Squad ID", "GPU Discovery" };
    private static readonly string[] DiscoveryModeLabels =
    {
        "异常自动发现",
        "区域录制 Sphere",
        "区域录制 Box",
        "屏幕矩形录制",
        "小队全量扩展",
        "小队异常抽样"
    };

    private readonly List<CrowdVatAiDebugTarget> _targets = new List<CrowdVatAiDebugTarget>();
    private readonly CrowdVatAiDebugRecorder _recorder = new CrowdVatAiDebugRecorder();

    private CrowdVatIndirectRenderer _renderer;
    private CrowdVatAiDebugDiscoverySettings _discoverySettings = CrowdVatAiDebugDiscoverySettings.CreateDefault();
    private Camera _screenRectCamera;
    private Vector2 _scrollPosition;
    private string _phenomenon = string.Empty;
    private string _reproductionSteps = string.Empty;
    private string _expectedBehavior = string.Empty;
    private string _status = "未开始录制。";
    private string _lastExportPath = string.Empty;
    private double _lastRepaintTime;
    private bool _showDiscoveryAdvanced;
    private bool _showDiscoveryRegionNumbers;
    private bool _isPickingDiscoveryCenter;
    private bool _isUpdateSubscribed;

    [MenuItem("Tools/Qianxia/AI Debugger")]
    public static void Open()
    {
        CrowdVatAiDebuggerWindow window = GetWindow<CrowdVatAiDebuggerWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(620.0f, 520.0f);
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent(WindowTitle);
        if (_targets.Count == 0)
        {
            _targets.Add(new CrowdVatAiDebugTarget { kind = CrowdVatAiDebugTargetKind.GpuDiscovery, id = 0 });
        }

        TryAutoAssignRenderer();
        SceneView.duringSceneGui -= OnSceneViewGui;
        SceneView.duringSceneGui += OnSceneViewGui;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneViewGui;
        _isPickingDiscoveryCenter = false;
        UnsubscribeEditorUpdate();
        if (_renderer != null)
            _renderer.ClearAiDebugRuntimeRecorder(_recorder);
    }

    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        DrawHeader();
        DrawRendererField();
        DrawTargets();
        DrawGpuDiscoverySettings();
        DrawBugDescription();
        DrawControls();
        DrawStatus();
        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1) 指定 CrowdVatIndirectRenderer 与目标实例。\n" +
            "2) 填写异常现象、复现步骤、期望表现。\n" +
            "3) Play 模式下复现问题，点击开始录制。\n" +
            "4) 复现到问题点后结束并导出，生成 JSONL + Markdown。\n" +
            "5) 把导出的 .md 和 .jsonl 提供给 AI 继续分析。",
            MessageType.Info);
    }

    private void DrawRendererField()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("录制对象", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            _renderer = (CrowdVatIndirectRenderer)EditorGUILayout.ObjectField(
                "人群 Renderer",
                _renderer,
                typeof(CrowdVatIndirectRenderer),
                true);

            if (GUILayout.Button("使用选中", GUILayout.Width(90.0f)))
                TryUseSelectedRenderer();

            if (GUILayout.Button("自动查找", GUILayout.Width(90.0f)))
                TryAutoAssignRenderer();
        }

        if (_renderer == null)
            EditorGUILayout.HelpBox("请指定场景中的 CrowdVatIndirectRenderer。", MessageType.Warning);
        else if (!Application.isPlaying)
            EditorGUILayout.HelpBox("当前不在 Play 模式。可以配置面板，但录制到的运行时轨迹需要 Play 模式。", MessageType.None);
    }

    private void DrawTargets()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("录制目标", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("支持 RuntimeID 和 SquadID。RuntimeID 对应 Crowd VAT 实例下标；SquadID 对应运行时小队下标。", EditorStyles.wordWrappedMiniLabel);

        for (int index = 0; index < _targets.Count; index++)
        {
            CrowdVatAiDebugTarget target = _targets[index];
            using (new EditorGUILayout.HorizontalScope())
            {
                target.kind = (CrowdVatAiDebugTargetKind)EditorGUILayout.Popup(
                    (int)target.kind,
                    TargetKindLabels,
                    GUILayout.Width(130.0f));
                using (new EditorGUI.DisabledScope(target.kind == CrowdVatAiDebugTargetKind.GpuDiscovery))
                    target.id = EditorGUILayout.IntField(target.id);
                if (GUILayout.Button("-", GUILayout.Width(28.0f)))
                {
                    _targets.RemoveAt(index);
                    GUIUtility.ExitGUI();
                }
            }

            target.id = Mathf.Max(0, target.id);
            if (index < _targets.Count)
                _targets[index] = target;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ 添加目标", GUILayout.Width(140.0f)))
                _targets.Add(new CrowdVatAiDebugTarget { kind = CrowdVatAiDebugTargetKind.RuntimeId, id = 0 });

            if (GUILayout.Button("+ GPU Discovery", GUILayout.Width(150.0f)))
                _targets.Add(new CrowdVatAiDebugTarget { kind = CrowdVatAiDebugTargetKind.GpuDiscovery, id = 0 });

            GUILayout.FlexibleSpace();
        }
    }

    private void DrawGpuDiscoverySettings()
    {
        if (!HasGpuDiscoveryTarget())
            return;

        _discoverySettings = _discoverySettings.Sanitized();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("GPU Discovery 目标模式", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("这些模式只在录制期启用；未录制时不会分配 discovery buffer，也不会 dispatch GPU 探针。", EditorStyles.wordWrappedMiniLabel);

        _discoverySettings.mode = (CrowdVatAiDebugDiscoveryMode)EditorGUILayout.Popup(
            "模式",
            (int)_discoverySettings.mode,
            DiscoveryModeLabels);
        _discoverySettings.candidateCapacity = EditorGUILayout.IntSlider(
            "候选容量",
            _discoverySettings.candidateCapacity,
            1,
            CrowdVatAiDebugDiscoverySettings.MaxCandidateCapacity);

        switch (_discoverySettings.mode)
        {
            case CrowdVatAiDebugDiscoveryMode.RegionSphere:
                DrawDiscoveryRegionSphere();
                break;
            case CrowdVatAiDebugDiscoveryMode.RegionBox:
                DrawDiscoveryRegionBox();
                break;
            case CrowdVatAiDebugDiscoveryMode.ScreenRect:
                DrawDiscoveryScreenRect();
                break;
            case CrowdVatAiDebugDiscoveryMode.SquadAll:
            case CrowdVatAiDebugDiscoveryMode.SquadAnomaly:
                DrawDiscoverySquad();
                break;
        }

        DrawDiscoveryAdvanced();
    }

    private void DrawDiscoveryRegionSphere()
    {
        EditorGUILayout.HelpBox("在 SceneView 中拖动中心手柄移动区域，拖动球形半径手柄调整范围。", MessageType.None);
        DrawDiscoveryPointButtons();
        DrawDiscoveryRegionNumberFoldout();
    }

    private void DrawDiscoveryRegionBox()
    {
        EditorGUILayout.HelpBox("在 SceneView 中拖动中心手柄移动盒子，拖动缩放手柄调整盒子尺寸。", MessageType.None);
        DrawDiscoveryPointButtons();
        DrawDiscoveryRegionNumberFoldout();
    }

    private void DrawDiscoveryRegionNumberFoldout()
    {
        _showDiscoveryRegionNumbers = EditorGUILayout.Foldout(_showDiscoveryRegionNumbers, "精确数值", true);
        if (!_showDiscoveryRegionNumbers)
            return;

        EditorGUI.indentLevel++;
        if (_discoverySettings.mode == CrowdVatAiDebugDiscoveryMode.RegionSphere)
        {
            _discoverySettings.worldCenter = EditorGUILayout.Vector3Field("世界中心", _discoverySettings.worldCenter);
            _discoverySettings.radius = Mathf.Max(0.0f, EditorGUILayout.FloatField("半径", _discoverySettings.radius));
        }
        else if (_discoverySettings.mode == CrowdVatAiDebugDiscoveryMode.RegionBox)
        {
            _discoverySettings.worldBoxCenter = EditorGUILayout.Vector3Field("世界中心", _discoverySettings.worldBoxCenter);
            _discoverySettings.worldBoxExtents = EditorGUILayout.Vector3Field("半尺寸", _discoverySettings.worldBoxExtents);
        }

        EditorGUI.indentLevel--;
    }

    private void DrawDiscoveryScreenRect()
    {
        _screenRectCamera = (Camera)EditorGUILayout.ObjectField("投影相机", _screenRectCamera, typeof(Camera), true);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("使用 Main Camera", GUILayout.Width(140.0f)))
                _screenRectCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (GUILayout.Button("全屏", GUILayout.Width(80.0f)))
                _discoverySettings.screenRect01 = Rect.MinMaxRect(0.0f, 0.0f, 1.0f, 1.0f);
            if (GUILayout.Button("中心 50%", GUILayout.Width(90.0f)))
                _discoverySettings.screenRect01 = Rect.MinMaxRect(0.25f, 0.25f, 0.75f, 0.75f);
        }

        Vector2 min = EditorGUILayout.Vector2Field("Rect Min 0-1", new Vector2(_discoverySettings.screenRect01.xMin, _discoverySettings.screenRect01.yMin));
        Vector2 max = EditorGUILayout.Vector2Field("Rect Max 0-1", new Vector2(_discoverySettings.screenRect01.xMax, _discoverySettings.screenRect01.yMax));
        _discoverySettings.screenRect01 = Rect.MinMaxRect(min.x, min.y, max.x, max.y);

        if (_screenRectCamera == null)
            EditorGUILayout.HelpBox("屏幕矩形录制需要指定投影相机。开始录制时会把该相机的 worldToClip 矩阵传给 GPU。", MessageType.Warning);
    }

    private void DrawDiscoverySquad()
    {
        _discoverySettings.squadId = Mathf.Max(0, EditorGUILayout.IntField("Squad ID", _discoverySettings.squadId));
        if (_discoverySettings.mode == CrowdVatAiDebugDiscoveryMode.SquadAnomaly)
            EditorGUILayout.HelpBox("小队异常抽样会先在 GPU 侧过滤 Squad，再只记录有异常 reason 的成员。", MessageType.None);
        else
            EditorGUILayout.HelpBox("小队全量扩展会记录该 Squad 内 alive 成员；候选超过容量时只保留 bounded candidates。", MessageType.None);
    }

    private void DrawDiscoveryPointButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("场景点选", GUILayout.Width(90.0f)))
            {
                _isPickingDiscoveryCenter = true;
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("选中物体", GUILayout.Width(90.0f)))
                UseSelectedObjectAsDiscoveryCenter();

            if (GUILayout.Button("Renderer 中心", GUILayout.Width(110.0f)))
                UseRendererAsDiscoveryCenter();
        }

        if (_isPickingDiscoveryCenter)
            EditorGUILayout.HelpBox("在 SceneView 左键点选一个位置，Esc 取消。", MessageType.Info);
    }

    private void DrawDiscoveryAdvanced()
    {
        _showDiscoveryAdvanced = EditorGUILayout.Foldout(_showDiscoveryAdvanced, "异常阈值", true);
        if (!_showDiscoveryAdvanced)
            return;

        EditorGUI.indentLevel++;
        _discoverySettings.highSpeedThreshold = Mathf.Max(0.0f, EditorGUILayout.FloatField("高速阈值", _discoverySettings.highSpeedThreshold));
        _discoverySettings.inactiveMovingSpeedThreshold = Mathf.Max(0.0f, EditorGUILayout.FloatField("inactive 移动阈值", _discoverySettings.inactiveMovingSpeedThreshold));
        _discoverySettings.speedSpikeThreshold = Mathf.Max(0.0f, EditorGUILayout.FloatField("速度突变阈值", _discoverySettings.speedSpikeThreshold));
        _discoverySettings.positionJumpThreshold = Mathf.Max(0.0f, EditorGUILayout.FloatField("位移跳变阈值", _discoverySettings.positionJumpThreshold));
        _discoverySettings.stuckIntentSpeedThreshold = Mathf.Max(0.0f, EditorGUILayout.FloatField("卡住前移动记忆", _discoverySettings.stuckIntentSpeedThreshold));
        _discoverySettings.stuckSpeedThreshold = Mathf.Max(0.0f, EditorGUILayout.FloatField("卡住速度阈值", _discoverySettings.stuckSpeedThreshold));
        _discoverySettings.stuckMoveThreshold = Mathf.Max(0.0f, EditorGUILayout.FloatField("卡住位移阈值", _discoverySettings.stuckMoveThreshold));
        _discoverySettings.stuckFrameThreshold = EditorGUILayout.IntSlider("卡住帧数", _discoverySettings.stuckFrameThreshold, 1, 600);
        EditorGUI.indentLevel--;
    }

    private void DrawBugDescription()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("异常描述", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("现象：");
        _phenomenon = EditorGUILayout.TextArea(_phenomenon, GUILayout.MinHeight(56.0f));

        EditorGUILayout.LabelField("复现步骤：");
        _reproductionSteps = EditorGUILayout.TextArea(_reproductionSteps, GUILayout.MinHeight(56.0f));

        EditorGUILayout.LabelField("期望表现：");
        _expectedBehavior = EditorGUILayout.TextArea(_expectedBehavior, GUILayout.MinHeight(56.0f));
    }

    private void DrawControls()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("录制控制", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_recorder.IsRecording || _renderer == null || _targets.Count == 0 || !Application.isPlaying))
            {
                if (GUILayout.Button("开始录制", GUILayout.Height(32.0f)))
                    StartRecording();
            }

            using (new EditorGUI.DisabledScope(_recorder.RecordCount == 0))
            {
                string exportLabel = _recorder.IsRecording ? "结束并导出" : "导出已有数据";
                if (GUILayout.Button(exportLabel, GUILayout.Height(32.0f)))
                    StopAndExport();
            }

            using (new EditorGUI.DisabledScope(!_recorder.IsRecording && _recorder.RecordCount == 0))
            {
                if (GUILayout.Button("丢弃", GUILayout.Width(84.0f), GUILayout.Height(32.0f)))
                    DiscardRecording();
            }
        }
    }

    private void DrawStatus()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("会话状态", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(_status, _recorder.IsRecording ? MessageType.Info : MessageType.None);

        if (!string.IsNullOrEmpty(_lastExportPath))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(_lastExportPath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("定位", GUILayout.Width(70.0f)))
                    EditorUtility.RevealInFinder(_lastExportPath);
            }
        }
    }

    private void StartRecording()
    {
        if (_renderer == null)
        {
            _status = "无法开始：未指定 CrowdVatIndirectRenderer。";
            return;
        }

        if (!Application.isPlaying)
        {
            _status = "无法开始：请先进入 Play 模式。录制必须由运行时 renderer 在 UpdateGpuBuffers 后推送。";
            return;
        }

        if (_targets.Count == 0)
        {
            _status = "无法开始：至少需要一个录制目标。";
            return;
        }

        CrowdVatAiDebugDiscoverySettings discoverySettings = PrepareDiscoverySettingsForRecording();
        if (discoverySettings.RequiresCameraMatrix() && !discoverySettings.hasWorldToClip)
        {
            _status = "无法开始：屏幕矩形录制需要指定可用相机。";
            return;
        }

        _recorder.Start(_renderer, _targets, _phenomenon, _reproductionSteps, _expectedBehavior, discoverySettings);
        _renderer.SetAiDebugRuntimeRecorder(_recorder);
        SubscribeEditorUpdate();
        _lastExportPath = string.Empty;
        _status = "录制中：" + _recorder.SessionName;
    }

    private void StopAndExport()
    {
        if (_renderer != null)
            _renderer.ClearAiDebugRuntimeRecorder(_recorder);
        _recorder.Stop();
        UnsubscribeEditorUpdate();
        string directory = GetArtifactDirectory();
        _lastExportPath = _recorder.Export(directory);
        _status = "已导出：" + _lastExportPath;
        EditorUtility.RevealInFinder(_lastExportPath);
    }

    private void DiscardRecording()
    {
        if (_renderer != null)
            _renderer.ClearAiDebugRuntimeRecorder(_recorder);
        _recorder.Discard();
        UnsubscribeEditorUpdate();
        _lastExportPath = string.Empty;
        _status = "已丢弃当前录制。";
    }

    private void OnEditorUpdate()
    {
        if (_recorder.IsRecording && !Application.isPlaying)
        {
            if (_renderer != null)
                _renderer.ClearAiDebugRuntimeRecorder(_recorder);
            _recorder.Stop();
            UnsubscribeEditorUpdate();
            _status = "Play 模式已结束，录制已停止；可以导出已有数据或丢弃。";
        }
        else if (_recorder.IsRecording)
        {
            _status = "录制中：" + _recorder.SessionName +
                "，记录 " + _recorder.RecordCount +
                " 条，未变化帧 " + _recorder.DroppedUnchangedFrames + "。";
        }

        double now = EditorApplication.timeSinceStartup;
        if (now - _lastRepaintTime > 0.25)
        {
            _lastRepaintTime = now;
            Repaint();
        }
    }

    private void SubscribeEditorUpdate()
    {
        if (_isUpdateSubscribed)
            return;

        EditorApplication.update += OnEditorUpdate;
        _isUpdateSubscribed = true;
    }

    private void UnsubscribeEditorUpdate()
    {
        if (!_isUpdateSubscribed)
            return;

        EditorApplication.update -= OnEditorUpdate;
        _isUpdateSubscribed = false;
    }

    private bool HasGpuDiscoveryTarget()
    {
        for (int index = 0; index < _targets.Count; index++)
        {
            if (_targets[index].kind == CrowdVatAiDebugTargetKind.GpuDiscovery)
                return true;
        }

        return false;
    }

    private CrowdVatAiDebugDiscoverySettings PrepareDiscoverySettingsForRecording()
    {
        CrowdVatAiDebugDiscoverySettings settings = _discoverySettings.Sanitized();
        settings.hasWorldToClip = false;
        if (settings.mode == CrowdVatAiDebugDiscoveryMode.ScreenRect)
        {
            Camera camera = _screenRectCamera != null
                ? _screenRectCamera
                : Camera.main != null
                    ? Camera.main
                    : FindFirstObjectByType<Camera>();
            if (camera != null)
            {
                _screenRectCamera = camera;
                settings.worldToClip = camera.projectionMatrix * camera.worldToCameraMatrix;
                settings.hasWorldToClip = true;
            }
        }

        _discoverySettings = settings;
        return settings;
    }

    private void UseSelectedObjectAsDiscoveryCenter()
    {
        if (Selection.activeTransform == null)
            return;

        SetDiscoveryCenter(Selection.activeTransform.position);
    }

    private void UseRendererAsDiscoveryCenter()
    {
        if (_renderer == null)
            return;

        SetDiscoveryCenter(_renderer.transform.position);
    }

    private void SetDiscoveryCenter(Vector3 worldPoint)
    {
        _discoverySettings.worldCenter = worldPoint;
        _discoverySettings.worldBoxCenter = worldPoint;
        Repaint();
    }

    private void OnSceneViewGui(SceneView sceneView)
    {
        DrawDiscoveryRegionSceneHandles();

        if (!_isPickingDiscoveryCenter)
            return;

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(12.0f, 12.0f, 260.0f, 48.0f), EditorStyles.helpBox);
        GUILayout.Label("AI Debug: 左键点选录制区域中心，Esc 取消。", EditorStyles.wordWrappedMiniLabel);
        GUILayout.EndArea();
        Handles.EndGUI();

        Event current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            _isPickingDiscoveryCenter = false;
            current.Use();
            Repaint();
            return;
        }

        if (current.type != EventType.MouseDown || current.button != 0 || current.alt)
            return;

        if (TryPickScenePoint(current.mousePosition, out Vector3 worldPoint))
        {
            SetDiscoveryCenter(worldPoint);
            _isPickingDiscoveryCenter = false;
            current.Use();
        }
    }

    private bool TryPickScenePoint(Vector2 guiPosition, out Vector3 worldPoint)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100000.0f))
        {
            worldPoint = hit.point;
            return true;
        }

        float planeY = _renderer != null ? _renderer.transform.position.y : 0.0f;
        Plane plane = new Plane(Vector3.up, new Vector3(0.0f, planeY, 0.0f));
        if (plane.Raycast(ray, out float distance))
        {
            worldPoint = ray.GetPoint(distance);
            return true;
        }

        worldPoint = Vector3.zero;
        return false;
    }

    private void DrawDiscoveryRegionSceneHandles()
    {
        if (!HasGpuDiscoveryTarget())
            return;

        switch (_discoverySettings.mode)
        {
            case CrowdVatAiDebugDiscoveryMode.RegionSphere:
                DrawDiscoverySphereSceneHandles();
                break;
            case CrowdVatAiDebugDiscoveryMode.RegionBox:
                DrawDiscoveryBoxSceneHandles();
                break;
        }
    }

    private void DrawDiscoverySphereSceneHandles()
    {
        Vector3 center = _discoverySettings.worldCenter;
        float radius = Mathf.Max(_discoverySettings.radius, 0.05f);
        float handleSize = HandleUtility.GetHandleSize(center);

        Color previousColor = Handles.color;
        Handles.color = new Color(0.15f, 0.65f, 1.0f, 0.9f);
        Handles.DrawWireDisc(center, Vector3.up, radius);
        Handles.DrawWireDisc(center, Vector3.right, radius);
        Handles.DrawWireDisc(center, Vector3.forward, radius);
        Handles.Label(center + Vector3.up * (radius + handleSize * 0.2f), "GPU Discovery Sphere");

        EditorGUI.BeginChangeCheck();
        Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);
        float newRadius = Handles.RadiusHandle(Quaternion.identity, newCenter, radius);
        if (EditorGUI.EndChangeCheck())
        {
            _discoverySettings.worldCenter = newCenter;
            _discoverySettings.radius = Mathf.Max(newRadius, 0.05f);
            Repaint();
        }

        Handles.color = previousColor;
    }

    private void DrawDiscoveryBoxSceneHandles()
    {
        Vector3 center = _discoverySettings.worldBoxCenter;
        Vector3 extents = ClampPositiveVector(_discoverySettings.worldBoxExtents, 0.05f);
        Vector3 size = extents * 2.0f;
        float handleSize = HandleUtility.GetHandleSize(center);

        Color previousColor = Handles.color;
        Handles.color = new Color(0.15f, 0.9f, 0.55f, 0.9f);
        Handles.DrawWireCube(center, size);
        Handles.Label(center + Vector3.up * (extents.y + handleSize * 0.2f), "GPU Discovery Box");

        EditorGUI.BeginChangeCheck();
        Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);
        Vector3 newSize = Handles.ScaleHandle(size, newCenter, Quaternion.identity, handleSize);
        if (EditorGUI.EndChangeCheck())
        {
            _discoverySettings.worldBoxCenter = newCenter;
            _discoverySettings.worldBoxExtents = ClampPositiveVector(AbsVector(newSize) * 0.5f, 0.05f);
            Repaint();
        }

        Handles.color = previousColor;
    }

    private static Vector3 ClampPositiveVector(Vector3 value, float minValue)
    {
        return new Vector3(
            Mathf.Max(Mathf.Abs(value.x), minValue),
            Mathf.Max(Mathf.Abs(value.y), minValue),
            Mathf.Max(Mathf.Abs(value.z), minValue));
    }

    private static Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private void TryUseSelectedRenderer()
    {
        if (Selection.activeGameObject == null)
            return;

        CrowdVatIndirectRenderer selectedRenderer = Selection.activeGameObject.GetComponentInParent<CrowdVatIndirectRenderer>();
        if (selectedRenderer == null)
            selectedRenderer = Selection.activeGameObject.GetComponentInChildren<CrowdVatIndirectRenderer>();

        if (selectedRenderer != null)
            _renderer = selectedRenderer;
    }

    private void TryAutoAssignRenderer()
    {
        if (_renderer != null)
            return;

        _renderer = FindFirstObjectByType<CrowdVatIndirectRenderer>();
    }

    private static string GetArtifactDirectory()
    {
        DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
        string rootPath = projectRoot != null ? projectRoot.FullName : Application.dataPath;
        return Path.Combine(rootPath, ArtifactDirectoryName);
    }
}
