using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(CrowdVatSquadController))]
public sealed class CrowdVatSquadControllerEditor : Editor
{
    private const float HandleSize = 0.22f;
    private const float CenterHandleScale = 1.35f;
    private const float AuxiliaryHandleScale = 0.82f;
    private const float LineThickness = 2.5f;
    private const int DefaultPreviewSlotLimit = 96;
    private static readonly Color CenterColor = new Color(0.18f, 0.78f, 1.0f, 1.0f);
    private static readonly Color ForwardColor = new Color(0.38f, 1.0f, 0.42f, 1.0f);
    private static readonly Color TargetColor = new Color(1.0f, 0.68f, 0.16f, 1.0f);
    private static readonly Color SlotColor = new Color(0.95f, 0.95f, 1.0f, 0.72f);
    private static readonly Color CombatCandidateRadiusColor = new Color(1.0f, 0.42f, 0.08f, 0.62f);
    private static readonly Color CombatCandidateRadiusDisabledColor = new Color(0.55f, 0.55f, 0.55f, 0.38f);
    private static readonly Color DisabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);

    private SerializedProperty _rendererProperty;
    private SerializedProperty _autoResolveRendererProperty;
    private SerializedProperty _showFormationPreviewProperty;
    private SerializedProperty _showCombatCandidateRadiusProperty;
    private SerializedProperty _maxPreviewSlotsProperty;
    private SerializedProperty _squadsProperty;

    private void OnEnable()
    {
        _rendererProperty = serializedObject.FindProperty("_renderer");
        _autoResolveRendererProperty = serializedObject.FindProperty("_autoResolveRenderer");
        _showFormationPreviewProperty = serializedObject.FindProperty("_showFormationPreview");
        _showCombatCandidateRadiusProperty = serializedObject.FindProperty("_showCombatCandidateRadius");
        _maxPreviewSlotsProperty = serializedObject.FindProperty("_maxPreviewSlots");
        _squadsProperty = serializedObject.FindProperty("_squads");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        DrawSceneEditingTools();
        serializedObject.ApplyModifiedProperties();
    }

    private void OnSceneGUI()
    {
        if (Application.isPlaying)
            return;

        CrowdVatSquadController controller = target as CrowdVatSquadController;
        if (controller == null)
            return;

        serializedObject.Update();
        bool showFormationPreview = _showFormationPreviewProperty == null || _showFormationPreviewProperty.boolValue;
        bool showCombatCandidateRadius = _showCombatCandidateRadiusProperty == null || _showCombatCandidateRadiusProperty.boolValue;
        if (!showFormationPreview && !showCombatCandidateRadius)
        {
            serializedObject.ApplyModifiedProperties();
            return;
        }

        CrowdVatIndirectRenderer renderer = ResolveRenderer(controller);
        int squadCount = _squadsProperty != null ? _squadsProperty.arraySize : 0;
        int previewBudget = _maxPreviewSlotsProperty != null
            ? Mathf.Max(1, _maxPreviewSlotsProperty.intValue)
            : DefaultPreviewSlotLimit;
        int slotsPerSquad = Mathf.Max(1, previewBudget / Mathf.Max(1, squadCount));

        for (int squadIndex = 0; squadIndex < squadCount; squadIndex++)
        {
            SerializedProperty squadProperty = _squadsProperty.GetArrayElementAtIndex(squadIndex);
            if (squadProperty == null)
                continue;

            bool enabled = GetBool(squadProperty, "enabled", true);
            Transform centerTransform = GetTransform(squadProperty, "centerTransform");
            Transform forwardTransform = GetTransform(squadProperty, "forwardTransform");
            Transform targetTransform = GetTransform(squadProperty, "targetTransform");
            if (centerTransform == null)
            {
                DrawMissingCenterLabel(controller, squadIndex, squadProperty);
                continue;
            }

            Color squadColor = enabled
                ? Color.HSVToRGB(Mathf.Repeat(squadIndex * 0.19f, 1.0f), 0.68f, 1.0f)
                : DisabledColor;
            Vector3 centerPosition = centerTransform.position;
            Vector3 forward = ResolveForward(centerTransform, forwardTransform);
            Vector3 targetPosition = targetTransform != null
                ? targetTransform.position
                : centerPosition + forward * 3.0f;

            if (showCombatCandidateRadius)
                DrawCombatCandidateRadius(renderer, squadProperty, centerPosition, enabled);

            if (showFormationPreview)
            {
                DrawSquadLines(centerPosition, forward, targetPosition, squadColor);
                DrawSquadLabel(centerPosition, squadIndex, squadProperty, enabled);
            }

            if (showFormationPreview)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newCenter = DrawPlanarMoveHandle(centerPosition, CenterColor, "Center", CenterHandleScale);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 delta = newCenter - centerPosition;
                    MoveTransform(centerTransform, newCenter, "Move Squad Center");
                    if (forwardTransform != null)
                        MoveTransform(forwardTransform, forwardTransform.position + delta, "Move Squad Forward");
                    if (targetTransform != null)
                        MoveTransform(targetTransform, targetTransform.position + delta, "Move Squad Target");
                    MarkControllerDirty(controller);
                }

                if (forwardTransform != null)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newForwardPosition = DrawPlanarMoveHandle(forwardTransform.position, ForwardColor, "Forward", AuxiliaryHandleScale);
                    if (EditorGUI.EndChangeCheck())
                    {
                        MoveTransform(forwardTransform, newForwardPosition, "Move Squad Forward");
                        MarkControllerDirty(controller);
                    }
                }

                if (targetTransform != null)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newTargetPosition = DrawPlanarMoveHandle(targetTransform.position, TargetColor, "Target", AuxiliaryHandleScale);
                    if (EditorGUI.EndChangeCheck())
                    {
                        MoveTransform(targetTransform, newTargetPosition, "Move Squad Target");
                        MarkControllerDirty(controller);
                    }
                }

                int memberCount = Mathf.Max(1, GetInt(squadProperty, "memberCount", 1));
                DrawFormationPreview(squadProperty, centerTransform.position, forward, memberCount, slotsPerSquad);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSceneEditingTools()
    {
        CrowdVatSquadController controller = target as CrowdVatSquadController;
        if (controller == null)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("小队场景编辑", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("选中 CrowdVatSquadController 后，可在 Scene 视图直接拖动每支小队的 Center / Forward / Target。拖动 Center 会整体平移该小队的朝向点和目标点。", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("创建/补齐小队锚点"))
            {
                Undo.RecordObject(controller, "Create Squad Anchors");
                controller.CreateDefaultSquadAnchors();
                MarkControllerDirty(controller);
                serializedObject.Update();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("生成默认战术小队"))
            {
                Undo.RecordObject(controller, "Create Tactical Squads");
                controller.CreateDefaultTacticalSquads();
                MarkControllerDirty(controller);
                serializedObject.Update();
                SceneView.RepaintAll();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("选中第一支小队中心"))
                SelectFirstSquadCenter();

            if (GUILayout.Button("刷新 Scene 预览"))
                SceneView.RepaintAll();
        }
    }

    private static Vector3 DrawPlanarMoveHandle(Vector3 position, Color color, string label, float scale)
    {
        float size = HandleUtility.GetHandleSize(position) * HandleSize * Mathf.Max(0.2f, scale);
        Handles.color = color;
        Handles.DrawSolidDisc(position, Vector3.up, size * 0.5f);
        Handles.CircleHandleCap(0, position, Quaternion.LookRotation(Vector3.up), size, EventType.Repaint);
        Handles.Label(position + Vector3.up * size * 1.6f, label);
        Vector3 movedPosition = Handles.Slider2D(
            position,
            Vector3.up,
            Vector3.right,
            Vector3.forward,
            size,
            Handles.CircleHandleCap,
            Vector2.zero);
        movedPosition.y = position.y;
        return movedPosition;
    }

    private static void DrawSquadLines(Vector3 center, Vector3 forward, Vector3 target, Color color)
    {
        Handles.color = color;
        Handles.DrawWireDisc(center, Vector3.up, Mathf.Max(0.6f, HandleUtility.GetHandleSize(center) * 0.18f));
        Handles.DrawAAPolyLine(LineThickness, center, center + forward * 2.0f);
        Handles.DrawAAPolyLine(LineThickness, center, target);
        Handles.color = TargetColor;
        Handles.DrawWireDisc(target, Vector3.up, Mathf.Max(0.35f, HandleUtility.GetHandleSize(target) * 0.12f));
    }

    private static void DrawCombatCandidateRadius(
        CrowdVatIndirectRenderer renderer,
        SerializedProperty squadProperty,
        Vector3 center,
        bool squadEnabled)
    {
        if (renderer == null)
            return;

        float cohesionRadius = Mathf.Max(0.0f, GetFloat(squadProperty, "cohesionRadius", 0.0f));
        if (!renderer.TryGetDebugCombatSquadCandidateRadius(cohesionRadius, out float radius))
            return;

        Color previousColor = Handles.color;
        bool combatEnabled = squadEnabled && renderer.GpuInstanceCombatEnabled;
        Color color = combatEnabled ? CombatCandidateRadiusColor : CombatCandidateRadiusDisabledColor;
        Handles.color = color;
        Handles.DrawWireDisc(center, Vector3.up, radius);
        Handles.DrawDottedLine(center + Vector3.right * radius, center - Vector3.right * radius, 10.0f);
        Handles.DrawDottedLine(center + Vector3.forward * radius, center - Vector3.forward * radius, 10.0f);
        Handles.Label(center + Vector3.up * 1.05f + Vector3.right * Mathf.Min(radius, 3.0f), $"Combat candidate r={radius:F1}");
        Handles.color = previousColor;
    }

    private static void DrawSquadLabel(Vector3 center, int squadIndex, SerializedProperty squadProperty, bool enabled)
    {
        string squadName = GetString(squadProperty, "name", $"Squad_{squadIndex:00}");
        string state = enabled ? string.Empty : " (disabled)";
        Handles.Label(center + Vector3.up * 0.75f, $"{squadIndex:00} {squadName}{state}");
    }

    private static void DrawMissingCenterLabel(CrowdVatSquadController controller, int squadIndex, SerializedProperty squadProperty)
    {
        bool enabled = GetBool(squadProperty, "enabled", true);
        if (!enabled)
            return;

        string squadName = GetString(squadProperty, "name", $"Squad_{squadIndex:00}");
        Handles.color = DisabledColor;
        Handles.Label(controller.transform.position + Vector3.up * (1.0f + squadIndex * 0.18f), $"{squadName}: 缺少 Center Transform");
    }

    private static int DrawFormationPreview(
        SerializedProperty squadProperty,
        Vector3 center,
        Vector3 forward,
        int memberCount,
        int maxSlots)
    {
        if (maxSlots <= 0)
            return 0;

        bool useCustomSlots = GetBool(squadProperty, "useCustomFormationSlots", false);
        float formationStrength = Mathf.Clamp01(GetFloat(squadProperty, "customFormationStrength", 0.0f));
        if (formationStrength <= 0.0f)
            return 0;

        SerializedProperty customSlots = squadProperty.FindPropertyRelative("customFormationSlots");
        int formationType = GetEnumIndex(squadProperty, "formationType");
        if (!useCustomSlots && formationType == (int)CrowdVatSquadFormationType.None)
            return 0;

        Vector2 spacing = GetVector2(squadProperty, "formationSpacing", Vector2.one * 1.6f);
        int slotCount = Mathf.Min(memberCount, maxSlots);
        Handles.color = SlotColor;

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            Vector2 localOffset = useCustomSlots && customSlots != null && slotIndex < customSlots.arraySize
                ? customSlots.GetArrayElementAtIndex(slotIndex).vector2Value
                : ResolveGeneratedSlotOffset((CrowdVatSquadFormationType)formationType, slotIndex, memberCount, spacing);
            Vector3 slotPosition = ResolveSlotWorldPosition(center, forward, localOffset);
            float size = HandleUtility.GetHandleSize(slotPosition) * 0.055f;
            Handles.SphereHandleCap(0, slotPosition, Quaternion.identity, size, EventType.Repaint);
        }

        return slotCount;
    }

    private static Vector2 ResolveGeneratedSlotOffset(
        CrowdVatSquadFormationType formationType,
        int localMemberIndex,
        int memberCount,
        Vector2 spacing)
    {
        int safeMemberCount = Mathf.Max(1, memberCount);
        Vector2 center = Vector2.zero;
        for (int index = 0; index < safeMemberCount; index++)
            center += ResolveGeneratedSlotRawOffset(formationType, index, safeMemberCount, spacing);
        center /= safeMemberCount;
        return ResolveGeneratedSlotRawOffset(formationType, localMemberIndex, safeMemberCount, spacing) - center;
    }

    private static Vector2 ResolveGeneratedSlotRawOffset(
        CrowdVatSquadFormationType formationType,
        int localMemberIndex,
        int memberCount,
        Vector2 spacing)
    {
        spacing = new Vector2(Mathf.Max(0.1f, spacing.x), Mathf.Max(0.1f, spacing.y));
        int index = Mathf.Clamp(localMemberIndex, 0, Mathf.Max(0, memberCount - 1));

        switch (formationType)
        {
            case CrowdVatSquadFormationType.Loose:
                return ResolveGridSlot(index, memberCount, spacing, true);
            case CrowdVatSquadFormationType.Line:
                return new Vector2((index - (memberCount - 1) * 0.5f) * spacing.x, 0.0f);
            case CrowdVatSquadFormationType.Column:
                return new Vector2(0.0f, ((memberCount - 1) * 0.5f - index) * spacing.y);
            case CrowdVatSquadFormationType.Wedge:
                if (index <= 0)
                    return Vector2.zero;
                int rank = (index + 1) / 2;
                float side = (index & 1) == 0 ? 1.0f : -1.0f;
                return new Vector2(side * rank * spacing.x, -rank * spacing.y);
            case CrowdVatSquadFormationType.Ring:
                if (memberCount <= 1)
                    return Vector2.zero;
                float angle = Mathf.PI * 0.5f - index * Mathf.PI * 2.0f / memberCount;
                float radius = Mathf.Max(spacing.x, spacing.y) * Mathf.Max(1.0f, memberCount / 8.0f);
                return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            case CrowdVatSquadFormationType.Block:
                return ResolveGridSlot(index, memberCount, spacing, false);
            case CrowdVatSquadFormationType.None:
            default:
                return Vector2.zero;
        }
    }

    private static Vector2 ResolveGridSlot(int index, int memberCount, Vector2 spacing, bool loose)
    {
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(memberCount)));
        int rows = Mathf.Max(1, Mathf.CeilToInt(memberCount / (float)columns));
        int row = index / columns;
        int column = index % columns;
        int rowStartIndex = row * columns;
        int rowMemberCount = Mathf.Clamp(memberCount - rowStartIndex, 1, columns);
        float stagger = loose && (row & 1) != 0 ? spacing.x * 0.5f : 0.0f;
        float rowSpacing = loose ? spacing.y * 1.25f : spacing.y;
        return new Vector2(
            (column - (rowMemberCount - 1) * 0.5f) * spacing.x + stagger,
            ((rows - 1) * 0.5f - row) * rowSpacing);
    }

    private static Vector3 ResolveSlotWorldPosition(Vector3 center, Vector3 forward, Vector2 localOffset)
    {
        forward.y = 0.0f;
        if (forward.sqrMagnitude <= 1e-6f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude <= 1e-6f)
            right = Vector3.right;
        right.Normalize();

        return center + right * localOffset.x + forward * localOffset.y;
    }

    private static Vector3 ResolveForward(Transform centerTransform, Transform forwardTransform)
    {
        Vector3 forward = forwardTransform != null
            ? forwardTransform.position - centerTransform.position
            : centerTransform.forward;
        forward.y = 0.0f;
        if (forward.sqrMagnitude <= 1e-6f)
            forward = Vector3.forward;
        return forward.normalized;
    }

    private CrowdVatIndirectRenderer ResolveRenderer(CrowdVatSquadController controller)
    {
        CrowdVatIndirectRenderer renderer = _rendererProperty != null
            ? _rendererProperty.objectReferenceValue as CrowdVatIndirectRenderer
            : null;
        if (renderer != null)
            return renderer;

        bool autoResolve = _autoResolveRendererProperty == null || _autoResolveRendererProperty.boolValue;
        if (!autoResolve || controller == null)
            return null;

        renderer = controller.GetComponent<CrowdVatIndirectRenderer>();
        if (renderer == null)
            renderer = controller.GetComponentInParent<CrowdVatIndirectRenderer>();
        return renderer;
    }

    private static void MoveTransform(Transform targetTransform, Vector3 worldPosition, string undoName)
    {
        if (targetTransform == null)
            return;

        Undo.RecordObject(targetTransform, undoName);
        targetTransform.position = worldPosition;
        targetTransform.hasChanged = true;
        EditorUtility.SetDirty(targetTransform);
    }

    private void SelectFirstSquadCenter()
    {
        if (_squadsProperty == null)
            return;

        for (int squadIndex = 0; squadIndex < _squadsProperty.arraySize; squadIndex++)
        {
            Transform centerTransform = GetTransform(_squadsProperty.GetArrayElementAtIndex(squadIndex), "centerTransform");
            if (centerTransform == null)
                continue;

            Selection.activeTransform = centerTransform;
            EditorGUIUtility.PingObject(centerTransform.gameObject);
            return;
        }
    }

    private static void MarkControllerDirty(CrowdVatSquadController controller)
    {
        EditorUtility.SetDirty(controller);
        if (controller.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
    }

    private static Transform GetTransform(SerializedProperty property, string relativeName)
    {
        return property?.FindPropertyRelative(relativeName)?.objectReferenceValue as Transform;
    }

    private static bool GetBool(SerializedProperty property, string relativeName, bool fallback)
    {
        SerializedProperty child = property?.FindPropertyRelative(relativeName);
        return child != null ? child.boolValue : fallback;
    }

    private static int GetInt(SerializedProperty property, string relativeName, int fallback)
    {
        SerializedProperty child = property?.FindPropertyRelative(relativeName);
        return child != null ? child.intValue : fallback;
    }

    private static float GetFloat(SerializedProperty property, string relativeName, float fallback)
    {
        SerializedProperty child = property?.FindPropertyRelative(relativeName);
        return child != null ? child.floatValue : fallback;
    }

    private static string GetString(SerializedProperty property, string relativeName, string fallback)
    {
        SerializedProperty child = property?.FindPropertyRelative(relativeName);
        return child != null && !string.IsNullOrWhiteSpace(child.stringValue) ? child.stringValue : fallback;
    }

    private static Vector2 GetVector2(SerializedProperty property, string relativeName, Vector2 fallback)
    {
        SerializedProperty child = property?.FindPropertyRelative(relativeName);
        return child != null ? child.vector2Value : fallback;
    }

    private static int GetEnumIndex(SerializedProperty property, string relativeName)
    {
        SerializedProperty child = property?.FindPropertyRelative(relativeName);
        return child != null ? child.enumValueIndex : 0;
    }
}
