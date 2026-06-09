using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[System.Serializable]
public struct CrowdVatSquadReplayCommand
{
    public int squadIndex;
    public Vector3 worldPoint;
    public CrowdVatSquadCommandType commandType;
    public bool updateFacing;
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class CrowdVatSquadCommandController : MonoBehaviour
{
    private const int GroundHitBufferSize = 16;
    private const int WorldRingGroundProbeIntervalFrames = 6;

    private sealed class WorldRingVisual
    {
        public GameObject root;
        public LineRenderer outlineRenderer;
        public LineRenderer mainRenderer;
    }

    private sealed class WorldRingGroundCache
    {
        public int squadIndex = -1;
        public int lastProbeFrame = -1;
        public float groundY;
    }

    [Header("运行时绑定")]
    [Tooltip("要交互的小队 controller。留空时自动查找场景里的第一个。")]
    [SerializeField] private CrowdVatSquadController _squadController;
    [Tooltip("用于发射屏幕中心射线的相机。留空时使用 Main Camera。")]
    [SerializeField] private Camera _targetCamera;

    [Header("输入")]
    [Tooltip("可选输入表。选择优先读取 Player/Attack，移动优先读取 Player/SecondaryAttack；留空时回退到鼠标左键 / 右键。")]
    [SerializeField] private InputActionAsset _inputActions;
    [InspectorName("Action Map 名称")]
    [Tooltip("输入表中要读取的 Action Map 名称。默认读取 Player。")]
    [SerializeField] private string _actionMapName = "Player";
    [InspectorName("选择 Action 名称")]
    [Tooltip("用于选择小队的输入 Action 名称。默认读取 Attack；没有输入表时回退到鼠标左键。")]
    [SerializeField] private string _selectActionName = "Attack";
    [InspectorName("移动命令 Action 名称")]
    [Tooltip("用于给已选小队下达移动命令的输入 Action 名称。默认读取 SecondaryAttack；没有输入表时回退到鼠标右键。")]
    [SerializeField] private string _moveCommandActionName = "SecondaryAttack";

    [Header("命令")]
    [Tooltip("屏幕中心射线最大距离。")]
    [SerializeField] [Min(1.0f)] private float _maxRayDistance = 300.0f;
    [Tooltip("屏幕中心可选中小队的最大距离。")]
    [SerializeField] [Min(1.0f)] private float _maxSelectionDistance = 120.0f;
    [Tooltip("地面 / 地形命中使用的 LayerMask。")]
    [SerializeField] private LayerMask _groundLayers = ~0;
    [Tooltip("允许被选中的阵营。")]
    [SerializeField] private CrowdVatFactionMask _selectableFactions = CrowdVatFactionMask.All;
    [Tooltip("瞄准地面点击后写入小队的命令类型。")]
    [SerializeField] private CrowdVatSquadCommandType _clickMoveCommand = CrowdVatSquadCommandType.MoveTo;
    [Tooltip("点地面移动时，是否同步更新小队朝向。")]
    [SerializeField] private bool _updateFacingOnMove = true;
    [Tooltip("进入播放后，如果还没有选中小队，是否自动选中第一支有效小队。")]
    [SerializeField] private bool _autoSelectFirstSquadOnStart;

    [Header("HUD")]
    [Tooltip("是否在屏幕中心绘制一个简单准星。")]
    [SerializeField] private bool _drawScreenCenterReticle = true;
    [InspectorName("准星尺寸")]
    [Tooltip("屏幕中心准星的线段长度，单位为像素。")]
    [SerializeField] [Min(2.0f)] private float _reticleSize = 12.0f;
    [InspectorName("准星线宽")]
    [Tooltip("屏幕中心准星的线条宽度，单位为像素。")]
    [SerializeField] [Min(1.0f)] private float _reticleThickness = 2.0f;
    [InspectorName("普通准星颜色")]
    [Tooltip("没有悬停或选中小队时的准星颜色。")]
    [SerializeField] private Color _reticleColor = new Color(1.0f, 1.0f, 1.0f, 0.92f);
    [InspectorName("悬停准星颜色")]
    [Tooltip("屏幕中心射线悬停到可选小队时的准星颜色。")]
    [SerializeField] private Color _hoverReticleColor = new Color(0.35f, 1.0f, 0.45f, 0.96f);
    [InspectorName("选中准星颜色")]
    [Tooltip("已经选中小队时的准星颜色。")]
    [SerializeField] private Color _selectedReticleColor = new Color(1.0f, 0.78f, 0.25f, 0.96f);
    [Tooltip("是否在屏幕下方显示当前选中状态。")]
    [SerializeField] private bool _showStatusLabel = true;

    [Header("世界空间反馈")]
    [Tooltip("是否在场景里显示世界空间选中环。")]
    [SerializeField] private bool _showWorldSelectionRings = true;
    [Tooltip("是否显示悬停小队的世界空间选中环。")]
    [SerializeField] private bool _showHoverWorldRing = true;
    [Tooltip("是否显示当前选中小队的世界空间选中环。")]
    [SerializeField] private bool _showSelectedWorldRing = true;
    [Tooltip("世界空间选中环的细分段数。")]
    [SerializeField] [Min(16)] private int _worldRingSegments = 48;
    [Tooltip("世界空间选中环的主线宽。")]
    [SerializeField] [Min(0.02f)] private float _worldRingWidth = 0.18f;
    [Tooltip("描边层相对主线宽的放大倍数。")]
    [SerializeField] [Min(1.1f)] private float _worldRingOutlineScale = 1.85f;
    [Tooltip("选中环相对小队半径的缩放。")]
    [SerializeField] [Min(0.1f)] private float _selectedRingRadiusScale = 1.05f;
    [Tooltip("悬停环相对小队半径的缩放。")]
    [SerializeField] [Min(0.1f)] private float _hoverRingRadiusScale = 1.0f;
    [Tooltip("世界空间选中环离地面的抬升高度。")]
    [SerializeField] [Min(0.0f)] private float _worldRingHeightOffset = 0.06f;
    [Tooltip("从小队中心向下找地面的探测起始高度。")]
    [SerializeField] [Min(0.5f)] private float _worldRingGroundProbeHeight = 8.0f;
    [Tooltip("世界空间环线的描边颜色。")]
    [SerializeField] private Color _worldRingOutlineColor = new Color(0.0f, 0.0f, 0.0f, 0.72f);
    [Tooltip("悬停小队的世界空间环线颜色。")]
    [SerializeField] private Color _hoverWorldRingColor = new Color(0.35f, 1.0f, 0.45f, 0.96f);
    [Tooltip("选中小队的世界空间环线颜色。")]
    [SerializeField] private Color _selectedWorldRingColor = new Color(1.0f, 0.78f, 0.25f, 0.96f);
    [Tooltip("选中环是否做轻微脉冲。")]
    [SerializeField] private bool _pulseSelectedWorldRing = true;
    [Tooltip("选中环脉冲的半径振幅。")]
    [SerializeField] [Min(0.0f)] private float _selectedWorldRingPulseAmplitude = 0.08f;
    [Tooltip("选中环脉冲速度。")]
    [SerializeField] [Min(0.0f)] private float _selectedWorldRingPulseSpeed = 4.5f;

    private readonly RaycastHit[] _groundHitBuffer = new RaycastHit[GroundHitBufferSize];
    private InputAction _selectAction;
    private InputAction _moveCommandAction;
    private int _selectedSquadIndex = -1;
    private int _hoveredSquadIndex = -1;
    private bool _attemptedAutoSelection;
    private Material _worldRingMaterial;
    private WorldRingVisual _selectedWorldRingVisual;
    private WorldRingVisual _hoverWorldRingVisual;
    private readonly WorldRingGroundCache _selectedWorldRingGroundCache = new WorldRingGroundCache();
    private readonly WorldRingGroundCache _hoverWorldRingGroundCache = new WorldRingGroundCache();

    public int SelectedSquadIndex => _selectedSquadIndex;
    public int HoveredSquadIndex => _hoveredSquadIndex;

    public event System.Action<int> SelectionChanged;
    public event System.Action<CrowdVatSquadReplayCommand> MoveCommandIssued;

    private void Awake()
    {
        ResolveReferences();
        BindActions();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindActions();
        _selectAction?.Enable();
        if (_moveCommandAction != _selectAction)
            _moveCommandAction?.Enable();
    }

    private void OnDisable()
    {
        _selectAction?.Disable();
        if (_moveCommandAction != _selectAction)
            _moveCommandAction?.Disable();
        SetWorldRingVisible(_selectedWorldRingVisual, false);
        SetWorldRingVisible(_hoverWorldRingVisual, false);
    }

    private void OnDestroy()
    {
        DestroyWorldRingVisual(ref _selectedWorldRingVisual);
        DestroyWorldRingVisual(ref _hoverWorldRingVisual);

        if (_worldRingMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_worldRingMaterial);
            else
                DestroyImmediate(_worldRingMaterial);

            _worldRingMaterial = null;
        }
    }

    private void OnValidate()
    {
        _maxRayDistance = Mathf.Max(1.0f, _maxRayDistance);
        _maxSelectionDistance = Mathf.Max(1.0f, _maxSelectionDistance);
        _reticleSize = Mathf.Max(2.0f, _reticleSize);
        _reticleThickness = Mathf.Max(1.0f, _reticleThickness);
        _worldRingSegments = Mathf.Max(16, _worldRingSegments);
        _worldRingWidth = Mathf.Max(0.02f, _worldRingWidth);
        _worldRingOutlineScale = Mathf.Max(1.1f, _worldRingOutlineScale);
        _selectedRingRadiusScale = Mathf.Max(0.1f, _selectedRingRadiusScale);
        _hoverRingRadiusScale = Mathf.Max(0.1f, _hoverRingRadiusScale);
        _worldRingHeightOffset = Mathf.Max(0.0f, _worldRingHeightOffset);
        _worldRingGroundProbeHeight = Mathf.Max(0.5f, _worldRingGroundProbeHeight);
        _selectedWorldRingPulseAmplitude = Mathf.Max(0.0f, _selectedWorldRingPulseAmplitude);
        _selectedWorldRingPulseSpeed = Mathf.Max(0.0f, _selectedWorldRingPulseSpeed);
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        ResolveReferences();
        ValidateSelectedSquadState();
        RefreshHoverState();

        if (_autoSelectFirstSquadOnStart && !_attemptedAutoSelection && _selectedSquadIndex < 0)
        {
            _attemptedAutoSelection = true;
            TrySelectFirstAvailableSquad();
        }

        UpdateWorldSelectionRings();

        if (WasSelectPressedThisFrame())
            HandleSelectionClick();

        if (WasMoveCommandPressedThisFrame())
            HandleMoveCommandClick();
    }

    private void ValidateSelectedSquadState()
    {
        if (_selectedSquadIndex < 0)
            return;

        if (_squadController != null && _squadController.IsSquadSelectable(_selectedSquadIndex))
            return;

        SetSelectedSquadInternal(-1, true);
    }

    private void OnGUI()
    {
        if (!Application.isPlaying)
            return;

        if (_drawScreenCenterReticle)
            DrawReticle();

        if (_showStatusLabel)
            DrawStatusLabel();
    }

    private void HandleSelectionClick()
    {
        if (_squadController == null)
            return;

        if (_hoveredSquadIndex >= 0)
            TrySelectSquad(_hoveredSquadIndex);
    }

    private void HandleMoveCommandClick()
    {
        if (_squadController == null)
            return;

        if (_selectedSquadIndex < 0)
            return;

        Ray ray = BuildScreenCenterRay();
        if (!TryRaycastCommandSurface(ray, out Vector3 hitPoint))
            return;

        TryIssueMoveCommand(_selectedSquadIndex, hitPoint, _clickMoveCommand, _updateFacingOnMove);
    }

    public bool TrySelectSquad(int squadIndex)
    {
        if (_squadController == null || squadIndex < 0 || !_squadController.IsSquadSelectable(squadIndex))
            return false;

        SetSelectedSquadInternal(squadIndex, true);
        return true;
    }

    public void ClearSelectedSquad()
    {
        SetSelectedSquadInternal(-1, true);
    }

    public bool TryIssueMoveCommand(int squadIndex, Vector3 hitPoint)
    {
        return TryIssueMoveCommand(squadIndex, hitPoint, _clickMoveCommand, _updateFacingOnMove);
    }

    public bool TryIssueMoveCommand(int squadIndex, Vector3 hitPoint, CrowdVatSquadCommandType commandType, bool updateFacing)
    {
        if (_squadController == null || squadIndex < 0)
            return false;

        if (!_squadController.TryCommandMoveTo(squadIndex, hitPoint, commandType, updateFacing))
        {
            _hoveredSquadIndex = -1;
            SetSelectedSquadInternal(-1, true);
            return false;
        }

        CrowdVatSquadReplayCommand command = new CrowdVatSquadReplayCommand
        {
            squadIndex = squadIndex,
            worldPoint = hitPoint,
            commandType = commandType,
            updateFacing = updateFacing
        };
        MoveCommandIssued?.Invoke(command);
        return true;
    }

    private void RefreshHoverState()
    {
        _hoveredSquadIndex = -1;
        if (_squadController == null)
            return;

        Ray ray = BuildScreenCenterRay();
        if (_squadController.TryRaycastSquad(ray, _maxSelectionDistance, _selectableFactions, out int squadIndex, out _))
            _hoveredSquadIndex = squadIndex;
    }

    private void UpdateWorldSelectionRings()
    {
        if (!Application.isPlaying || !_showWorldSelectionRings || _squadController == null)
        {
            SetWorldRingVisible(_selectedWorldRingVisual, false);
            SetWorldRingVisible(_hoverWorldRingVisual, false);
            return;
        }

        EnsureWorldRingVisuals();

        int hoverSquadIndex = _hoveredSquadIndex == _selectedSquadIndex ? -1 : _hoveredSquadIndex;

        Vector3 selectedCenter = Vector3.zero;
        float selectedRadius = 0.0f;
        bool showSelected = _showSelectedWorldRing &&
            TryGetWorldRingState(_selectedSquadIndex, _selectedRingRadiusScale, _selectedWorldRingGroundCache, out selectedCenter, out selectedRadius);
        if (showSelected && _pulseSelectedWorldRing)
        {
            float pulse = (Mathf.Sin(Time.time * _selectedWorldRingPulseSpeed) * 0.5f) + 0.5f;
            selectedRadius += pulse * _selectedWorldRingPulseAmplitude;
        }
        UpdateWorldRingVisual(_selectedWorldRingVisual, showSelected, selectedCenter, selectedRadius, _selectedWorldRingColor);

        Vector3 hoverCenter = Vector3.zero;
        float hoverRadius = 0.0f;
        bool showHover = _showHoverWorldRing &&
            TryGetWorldRingState(hoverSquadIndex, _hoverRingRadiusScale, _hoverWorldRingGroundCache, out hoverCenter, out hoverRadius);
        UpdateWorldRingVisual(_hoverWorldRingVisual, showHover, hoverCenter, hoverRadius, _hoverWorldRingColor);
    }

    private bool TryGetWorldRingState(int squadIndex, float radiusScale, WorldRingGroundCache groundCache, out Vector3 ringCenter, out float ringRadius)
    {
        ringCenter = Vector3.zero;
        ringRadius = 0.0f;
        if (squadIndex < 0 || _squadController == null)
            return false;

        if (!_squadController.TryGetSquadSelectionState(squadIndex, out Vector3 squadCenter, out float selectionRadius))
            return false;

        ringRadius = Mathf.Max(0.6f, selectionRadius * Mathf.Max(0.1f, radiusScale));
        float ringY = ResolveWorldRingGroundY(squadIndex, squadCenter, groundCache) + _worldRingHeightOffset;

        ringCenter = new Vector3(squadCenter.x, ringY, squadCenter.z);
        return true;
    }

    private float ResolveWorldRingGroundY(int squadIndex, Vector3 squadCenter, WorldRingGroundCache groundCache)
    {
        if (groundCache != null)
        {
            bool canReuseCache = groundCache.squadIndex == squadIndex &&
                groundCache.lastProbeFrame >= 0 &&
                Time.frameCount - groundCache.lastProbeFrame < WorldRingGroundProbeIntervalFrames;
            if (canReuseCache)
                return groundCache.groundY;
        }

        float resolvedGroundY = squadCenter.y;
        Vector3 probeOrigin = squadCenter + Vector3.up * _worldRingGroundProbeHeight;
        float probeDistance = Mathf.Max(_worldRingGroundProbeHeight + _maxRayDistance, _worldRingGroundProbeHeight * 2.0f);
        if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, probeDistance, _groundLayers, QueryTriggerInteraction.Ignore))
            resolvedGroundY = hit.point.y;

        if (groundCache != null)
        {
            groundCache.squadIndex = squadIndex;
            groundCache.lastProbeFrame = Time.frameCount;
            groundCache.groundY = resolvedGroundY;
        }

        return resolvedGroundY;
    }

    private bool TryRaycastCommandSurface(Ray ray, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            _groundHitBuffer,
            _maxRayDistance,
            _groundLayers,
            QueryTriggerInteraction.Ignore);
        if (hitCount <= 0)
            return false;

        float nearestDistance = float.PositiveInfinity;
        int nearestIndex = -1;
        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            RaycastHit hit = _groundHitBuffer[hitIndex];
            if (hit.collider == null)
                continue;

            if (hit.collider.transform.IsChildOf(transform))
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearestIndex = hitIndex;
            }
        }

        if (nearestIndex < 0)
            return false;

        hitPoint = _groundHitBuffer[nearestIndex].point;
        return true;
    }

    private bool WasSelectPressedThisFrame()
    {
        if (_selectAction != null && _selectAction.WasPressedThisFrame())
            return true;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;

        return Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
    }

    private bool WasMoveCommandPressedThisFrame()
    {
        if (_moveCommandAction != null && _moveCommandAction.WasPressedThisFrame())
            return true;

        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            return true;

        return Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
    }

    private void TrySelectFirstAvailableSquad()
    {
        if (_squadController != null && _squadController.TryGetFirstActiveSquadIndex(out int squadIndex))
            TrySelectSquad(squadIndex);
    }

    private void SetSelectedSquadInternal(int squadIndex, bool notify)
    {
        if (_selectedSquadIndex == squadIndex)
            return;

        _selectedSquadIndex = squadIndex;
        if (notify)
            SelectionChanged?.Invoke(_selectedSquadIndex);
    }

    private Ray BuildScreenCenterRay()
    {
        Camera camera = ResolveTargetCamera();
        if (camera == null)
            return new Ray(transform.position, transform.forward);

        return camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f));
    }

    private Camera ResolveTargetCamera()
    {
        if (_targetCamera != null)
            return _targetCamera;

        _targetCamera = Camera.main;
        if (_targetCamera == null)
            _targetCamera = Object.FindFirstObjectByType<Camera>();
        return _targetCamera;
    }

    private void ResolveReferences()
    {
        ResolveTargetCamera();

        if (_squadController == null)
            _squadController = Object.FindFirstObjectByType<CrowdVatSquadController>();
    }

    private void BindActions()
    {
        _selectAction = null;
        _moveCommandAction = null;
        if (_inputActions == null)
            return;

        InputActionMap actionMap = _inputActions.FindActionMap(_actionMapName, false);
        if (actionMap == null)
            return;

        _selectAction = actionMap.FindAction(_selectActionName, false);
        _moveCommandAction = actionMap.FindAction(_moveCommandActionName, false);
    }

    private void EnsureWorldRingVisuals()
    {
        if (_worldRingMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader != null)
            {
                _worldRingMaterial = new Material(shader)
                {
                    name = "CrowdVatSquadWorldRing_Runtime"
                };
                _worldRingMaterial.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        if (_worldRingMaterial == null)
            return;

        if (_selectedWorldRingVisual == null)
            _selectedWorldRingVisual = CreateWorldRingVisual("CrowdVatSquadWorldRing_Selected");

        if (_hoverWorldRingVisual == null)
            _hoverWorldRingVisual = CreateWorldRingVisual("CrowdVatSquadWorldRing_Hover");
    }

    private WorldRingVisual CreateWorldRingVisual(string objectName)
    {
        GameObject root = new GameObject(objectName);
        root.hideFlags = HideFlags.HideAndDontSave;

        LineRenderer outlineRenderer = root.AddComponent<LineRenderer>();
        ConfigureWorldRingLineRenderer(outlineRenderer, _worldRingMaterial, _worldRingWidth * _worldRingOutlineScale, _worldRingOutlineColor, 0);

        GameObject mainObject = new GameObject("Main");
        mainObject.hideFlags = HideFlags.HideAndDontSave;
        mainObject.transform.SetParent(root.transform, false);
        LineRenderer mainRenderer = mainObject.AddComponent<LineRenderer>();
        ConfigureWorldRingLineRenderer(mainRenderer, _worldRingMaterial, _worldRingWidth, Color.white, 1);

        root.SetActive(false);
        return new WorldRingVisual
        {
            root = root,
            outlineRenderer = outlineRenderer,
            mainRenderer = mainRenderer
        };
    }

    private void ConfigureWorldRingLineRenderer(LineRenderer lineRenderer, Material material, float width, Color color, int sortingOrder)
    {
        lineRenderer.sharedMaterial = material;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.widthMultiplier = Mathf.Max(0.02f, width);
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.numCapVertices = 6;
        lineRenderer.numCornerVertices = 6;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        lineRenderer.lightProbeUsage = LightProbeUsage.Off;
        lineRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        lineRenderer.sortingOrder = sortingOrder;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
    }

    private void UpdateWorldRingVisual(WorldRingVisual visual, bool visible, Vector3 center, float radius, Color color)
    {
        if (visual == null)
            return;

        if (!visible)
        {
            SetWorldRingVisible(visual, false);
            return;
        }

        if (visual.root != null && !visual.root.activeSelf)
            visual.root.SetActive(true);

        ApplyWorldRingStyle(visual, color);
        ApplyRingPoints(visual.outlineRenderer, center, radius, _worldRingSegments);
        ApplyRingPoints(visual.mainRenderer, center, radius, _worldRingSegments);
    }

    private void ApplyWorldRingStyle(WorldRingVisual visual, Color mainColor)
    {
        if (visual == null)
            return;

        if (visual.outlineRenderer != null)
        {
            visual.outlineRenderer.widthMultiplier = Mathf.Max(0.02f, _worldRingWidth * _worldRingOutlineScale);
            visual.outlineRenderer.startColor = _worldRingOutlineColor;
            visual.outlineRenderer.endColor = _worldRingOutlineColor;
        }

        if (visual.mainRenderer != null)
        {
            visual.mainRenderer.widthMultiplier = Mathf.Max(0.02f, _worldRingWidth);
            visual.mainRenderer.startColor = mainColor;
            visual.mainRenderer.endColor = mainColor;
        }
    }

    private static void ApplyRingPoints(LineRenderer lineRenderer, Vector3 center, float radius, int segmentCount)
    {
        if (lineRenderer == null)
            return;

        int safeSegmentCount = Mathf.Max(16, segmentCount);
        float safeRadius = Mathf.Max(0.1f, radius);
        if (lineRenderer.positionCount != safeSegmentCount)
            lineRenderer.positionCount = safeSegmentCount;

        for (int segmentIndex = 0; segmentIndex < safeSegmentCount; segmentIndex++)
        {
            float angleRadians = segmentIndex / (float)safeSegmentCount * Mathf.PI * 2.0f;
            Vector3 offset = new Vector3(Mathf.Cos(angleRadians) * safeRadius, 0.0f, Mathf.Sin(angleRadians) * safeRadius);
            lineRenderer.SetPosition(segmentIndex, center + offset);
        }
    }

    private static void SetWorldRingVisible(WorldRingVisual visual, bool visible)
    {
        if (visual == null || visual.root == null)
            return;

        if (visual.root.activeSelf != visible)
            visual.root.SetActive(visible);
    }

    private static void DestroyWorldRingVisual(ref WorldRingVisual visual)
    {
        if (visual == null)
            return;

        if (visual.root != null)
        {
            if (Application.isPlaying)
                Destroy(visual.root);
            else
                DestroyImmediate(visual.root);
        }

        visual = null;
    }

    private void DrawReticle()
    {
        float halfSize = _reticleSize * 0.5f;
        float thickness = _reticleThickness;
        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;
        Color color = ResolveReticleColor();

        Color previousColor = GUI.color;
        GUI.color = color;
        DrawSolidRect(new Rect(centerX - halfSize, centerY - (thickness * 0.5f), _reticleSize, thickness));
        DrawSolidRect(new Rect(centerX - (thickness * 0.5f), centerY - halfSize, thickness, _reticleSize));
        GUI.color = previousColor;
    }

    private void DrawStatusLabel()
    {
        string statusText = BuildStatusText();
        if (string.IsNullOrEmpty(statusText))
            return;

        Rect rect = new Rect((Screen.width * 0.5f) - 220.0f, Screen.height - 42.0f, 440.0f, 24.0f);
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13
        };
        style.normal.textColor = Color.white;

        Color previousColor = GUI.color;
        GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.45f);
        DrawSolidRect(new Rect(rect.x - 8.0f, rect.y - 2.0f, rect.width + 16.0f, rect.height + 4.0f));
        GUI.color = previousColor;
        GUI.Label(rect, statusText, style);
    }

    private string BuildStatusText()
    {
        if (_squadController == null)
            return "未找到 CrowdVatSquadController";

        if (_selectedSquadIndex >= 0 && _squadController.TryGetSquadName(_selectedSquadIndex, out string selectedName))
        {
            if (_hoveredSquadIndex >= 0 &&
                _hoveredSquadIndex != _selectedSquadIndex &&
                _squadController.TryGetSquadName(_hoveredSquadIndex, out string hoveredName))
            {
                return $"当前选中 {selectedName}，左键可切换到 {hoveredName}，右键可移动到准星位置";
            }

            return $"当前选中 {selectedName}，左键选队，右键移动到准星位置";
        }

        if (_hoveredSquadIndex >= 0 && _squadController.TryGetSquadName(_hoveredSquadIndex, out string hoverName))
            return $"瞄准 {hoverName}，左键即可选中；选中后右键移动";

        return "左键选择准星中的小队，右键移动当前选中小队";
    }

    private Color ResolveReticleColor()
    {
        if (_hoveredSquadIndex >= 0)
            return _hoverReticleColor;

        if (_selectedSquadIndex >= 0)
            return _selectedReticleColor;

        return _reticleColor;
    }

    private static void DrawSolidRect(Rect rect)
    {
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }
}
