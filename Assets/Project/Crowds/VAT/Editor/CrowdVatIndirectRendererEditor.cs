using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(CrowdVatIndirectRenderer))]
public sealed class CrowdVatIndirectRendererEditor : Editor
{
    private const float HandleSizeFactor = 0.12f;
    private const float RangeLineThickness = 3.0f;
    private const float RangeDiagonalScreenSpace = 5.0f;
    private const float HandleSphereScale = 0.28f;
    private const float HandleDiscScale = 0.52f;
    private static readonly Color CampAColor = new Color(0.92f, 0.35f, 0.26f, 1.0f);
    private static readonly Color CampBColor = new Color(0.18f, 0.66f, 0.95f, 1.0f);
    private static readonly Color LinkColor = new Color(1.0f, 0.92f, 0.24f, 0.9f);
    private static readonly Color TotalAreaColor = new Color(1.0f, 1.0f, 1.0f, 0.82f);

    private SerializedProperty _factionLayoutProperty;
    private SerializedProperty _factionSplitAxisProperty;
    private SerializedProperty _factionCenterGapProperty;
    private SerializedProperty _useExplicitFactionCentersProperty;
    private SerializedProperty _campACenterXZProperty;
    private SerializedProperty _campBCenterXZProperty;
    private SerializedProperty _useFactionControlTransformsProperty;
    private SerializedProperty _campAControlTransformProperty;
    private SerializedProperty _campBControlTransformProperty;
    private SerializedProperty _useExplicitFactionAreaSizesProperty;
    private SerializedProperty _campAAreaSizeProperty;
    private SerializedProperty _campBAreaSizeProperty;
    private SerializedProperty _campANorthWestCornerTransformProperty;
    private SerializedProperty _campANorthEastCornerTransformProperty;
    private SerializedProperty _campASouthEastCornerTransformProperty;
    private SerializedProperty _campASouthWestCornerTransformProperty;
    private SerializedProperty _campBNorthWestCornerTransformProperty;
    private SerializedProperty _campBNorthEastCornerTransformProperty;
    private SerializedProperty _campBSouthEastCornerTransformProperty;
    private SerializedProperty _campBSouthWestCornerTransformProperty;
    private SerializedProperty _areaSizeProperty;
    private SerializedProperty _distributeAcrossWholeTerrainProperty;
    private SerializedProperty _terrainProperty;
    private SerializedProperty _autoResolveTerrainProperty;
    private SerializedProperty _collisionHeightProperty;

    private struct SpawnAreaContext
    {
        public Vector2 size;
        public Vector2 centerXZ;
    }

    private struct FactionCornerSet
    {
        public Transform northWest;
        public Transform northEast;
        public Transform southEast;
        public Transform southWest;
    }

    private void OnEnable()
    {
        _factionLayoutProperty = serializedObject.FindProperty("_factionLayout");
        _factionSplitAxisProperty = serializedObject.FindProperty("_factionSplitAxis");
        _factionCenterGapProperty = serializedObject.FindProperty("_factionCenterGap");
        _useExplicitFactionCentersProperty = serializedObject.FindProperty("_useExplicitFactionCenters");
        _campACenterXZProperty = serializedObject.FindProperty("_campACenterXZ");
        _campBCenterXZProperty = serializedObject.FindProperty("_campBCenterXZ");
        _useFactionControlTransformsProperty = serializedObject.FindProperty("_useFactionControlTransforms");
        _campAControlTransformProperty = serializedObject.FindProperty("_campAControlTransform");
        _campBControlTransformProperty = serializedObject.FindProperty("_campBControlTransform");
        _useExplicitFactionAreaSizesProperty = serializedObject.FindProperty("_useExplicitFactionAreaSizes");
        _campAAreaSizeProperty = serializedObject.FindProperty("_campAAreaSize");
        _campBAreaSizeProperty = serializedObject.FindProperty("_campBAreaSize");
        _campANorthWestCornerTransformProperty = serializedObject.FindProperty("_campANorthWestCornerTransform");
        _campANorthEastCornerTransformProperty = serializedObject.FindProperty("_campANorthEastCornerTransform");
        _campASouthEastCornerTransformProperty = serializedObject.FindProperty("_campASouthEastCornerTransform");
        _campASouthWestCornerTransformProperty = serializedObject.FindProperty("_campASouthWestCornerTransform");
        _campBNorthWestCornerTransformProperty = serializedObject.FindProperty("_campBNorthWestCornerTransform");
        _campBNorthEastCornerTransformProperty = serializedObject.FindProperty("_campBNorthEastCornerTransform");
        _campBSouthEastCornerTransformProperty = serializedObject.FindProperty("_campBSouthEastCornerTransform");
        _campBSouthWestCornerTransformProperty = serializedObject.FindProperty("_campBSouthWestCornerTransform");
        _areaSizeProperty = serializedObject.FindProperty("_areaSize");
        _distributeAcrossWholeTerrainProperty = serializedObject.FindProperty("_distributeAcrossWholeTerrain");
        _terrainProperty = serializedObject.FindProperty("_terrain");
        _autoResolveTerrainProperty = serializedObject.FindProperty("_autoResolveTerrain");
        _collisionHeightProperty = serializedObject.FindProperty("_collisionHeight");
        EnsureSceneViewGizmosEnabled();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        DrawSceneControlTips();
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSceneControlTips()
    {
        if (targets.Length != 1)
        {
            EditorGUILayout.HelpBox("多选时不显示阵营场景控制点，请单独选中一个 CrowdVatIndirectRenderer。", MessageType.Info);
            return;
        }

        if ((CrowdVatFactionLayoutMode)_factionLayoutProperty.enumValueIndex != CrowdVatFactionLayoutMode.TwoOpposingFactions)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("场景控制", EditorStyles.boldLabel);
        EnsureSceneViewGizmosEnabled();
        if (_useFactionControlTransformsProperty != null && _useFactionControlTransformsProperty.boolValue)
        {
            EditorGUILayout.HelpBox("当前使用空物体控制两边阵营。直接在 Hierarchy 里展开 CampA_Control / CampB_Control：移动根空物体调整中心，拖拽 Range_NW / NE / SE / SW 四个角点调整范围。", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("推荐改用空物体控制。创建后会自动生成两个阵营根空物体，每个根空物体下面附带四个角点，后续不再依赖 Scene 句柄。", MessageType.Info);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("打开 Gizmos"))
            {
                EnsureSceneViewGizmosEnabled();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("创建/刷新控制空物体"))
            {
                CrowdVatIndirectRenderer renderer = (CrowdVatIndirectRenderer)target;
                CreateOrRefreshFactionControlTransforms(renderer, true);
            }

            if (GUILayout.Button("选中 CampA"))
            {
                SelectFactionControlTransform(_campAControlTransformProperty);
            }

            if (GUILayout.Button("选中 CampB"))
            {
                SelectFactionControlTransform(_campBControlTransformProperty);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("切回仅数值控制"))
            {
                CrowdVatIndirectRenderer renderer = (CrowdVatIndirectRenderer)target;
                Undo.RecordObject(renderer, "Disable Faction Control Transforms");
                _useFactionControlTransformsProperty.boolValue = false;
                serializedObject.ApplyModifiedProperties();
                MarkRendererDirty(renderer);
            }

            if (GUILayout.Button("重建布局"))
            {
                CrowdVatIndirectRenderer renderer = (CrowdVatIndirectRenderer)target;
                renderer.RebuildCrowdLayout();
                EditorUtility.SetDirty(renderer);
            }
        }
    }

    private void SelectFactionControlTransform(SerializedProperty controlTransformProperty)
    {
        Transform controlTransform = controlTransformProperty != null
            ? controlTransformProperty.objectReferenceValue as Transform
            : null;
        if (controlTransform == null)
            return;

        Selection.activeTransform = controlTransform;
        EditorGUIUtility.PingObject(controlTransform.gameObject);
    }

    private void CreateOrRefreshFactionControlTransforms(CrowdVatIndirectRenderer renderer, bool selectCampAAfterCreate)
    {
        if (renderer == null)
            return;

        serializedObject.Update();

        SpawnAreaContext spawnArea = ResolveSpawnAreaContext(renderer);
        Vector2 campACenter = ResolveCurrentFactionCenterXZ(renderer, spawnArea, CrowdVatFaction.CampA);
        Vector2 campBCenter = ResolveCurrentFactionCenterXZ(renderer, spawnArea, CrowdVatFaction.CampB);
        Vector2 campAAreaSize = ResolveCurrentFactionAreaSize(renderer, spawnArea, CrowdVatFaction.CampA);
        Vector2 campBAreaSize = ResolveCurrentFactionAreaSize(renderer, spawnArea, CrowdVatFaction.CampB);
        TryResolveTerrain(renderer, out Terrain terrain);
        float verticalOffset = ResolveHandleHeightOffset();

        Transform campAControlTransform = EnsureFactionControlTransform(renderer, CrowdVatFaction.CampA, campACenter, terrain, verticalOffset);
        Transform campBControlTransform = EnsureFactionControlTransform(renderer, CrowdVatFaction.CampB, campBCenter, terrain, verticalOffset);
        FactionCornerSet campACorners = EnsureFactionCornerTransforms(renderer, CrowdVatFaction.CampA, campAControlTransform, campACenter, campAAreaSize, terrain, verticalOffset);
        FactionCornerSet campBCorners = EnsureFactionCornerTransforms(renderer, CrowdVatFaction.CampB, campBControlTransform, campBCenter, campBAreaSize, terrain, verticalOffset);

        Undo.RecordObject(renderer, "Create Crowd Faction Control Transforms");
        _useFactionControlTransformsProperty.boolValue = true;
        _useExplicitFactionCentersProperty.boolValue = true;
        _useExplicitFactionAreaSizesProperty.boolValue = true;
        _campAControlTransformProperty.objectReferenceValue = campAControlTransform;
        _campBControlTransformProperty.objectReferenceValue = campBControlTransform;
        _campACenterXZProperty.vector2Value = campACenter;
        _campBCenterXZProperty.vector2Value = campBCenter;
        _campAAreaSizeProperty.vector2Value = campAAreaSize;
        _campBAreaSizeProperty.vector2Value = campBAreaSize;
        _campANorthWestCornerTransformProperty.objectReferenceValue = campACorners.northWest;
        _campANorthEastCornerTransformProperty.objectReferenceValue = campACorners.northEast;
        _campASouthEastCornerTransformProperty.objectReferenceValue = campACorners.southEast;
        _campASouthWestCornerTransformProperty.objectReferenceValue = campACorners.southWest;
        _campBNorthWestCornerTransformProperty.objectReferenceValue = campBCorners.northWest;
        _campBNorthEastCornerTransformProperty.objectReferenceValue = campBCorners.northEast;
        _campBSouthEastCornerTransformProperty.objectReferenceValue = campBCorners.southEast;
        _campBSouthWestCornerTransformProperty.objectReferenceValue = campBCorners.southWest;
        serializedObject.ApplyModifiedProperties();

        renderer.SetFactionControlTransforms(campAControlTransform, campBControlTransform);
        renderer.SetFactionRangeControlTransforms(
            campACorners.northWest,
            campACorners.northEast,
            campACorners.southEast,
            campACorners.southWest,
            campBCorners.northWest,
            campBCorners.northEast,
            campBCorners.southEast,
            campBCorners.southWest);
        renderer.SyncExplicitFactionAreasFromControlTransforms();
        renderer.SyncExplicitFactionCentersFromControlTransforms();
        renderer.SyncFactionControlTransformsFromExplicitCenters();
        renderer.SyncFactionRangeControlTransformsFromExplicitAreas();
        MarkRendererDirty(renderer);

        if (selectCampAAfterCreate && campAControlTransform != null)
        {
            Selection.activeTransform = campAControlTransform;
            EditorGUIUtility.PingObject(campAControlTransform.gameObject);
        }
    }

    private Transform EnsureFactionControlTransform(CrowdVatIndirectRenderer renderer, CrowdVatFaction faction, Vector2 centerXZ, Terrain terrain, float verticalOffset)
    {
        SerializedProperty controlTransformProperty = faction == CrowdVatFaction.CampA
            ? _campAControlTransformProperty
            : _campBControlTransformProperty;
        Transform controlTransform = controlTransformProperty != null
            ? controlTransformProperty.objectReferenceValue as Transform
            : null;
        string controlName = faction == CrowdVatFaction.CampA ? "CampA_Control" : "CampB_Control";

        if (controlTransform == null)
        {
            Transform existingTransform = renderer.transform.Find(controlName);
            if (existingTransform != null)
                controlTransform = existingTransform;
        }

        if (controlTransform == null)
        {
            GameObject controlObject = new GameObject(controlName);
            Undo.RegisterCreatedObjectUndo(controlObject, "Create Crowd Faction Control Transform");
            controlTransform = controlObject.transform;
            controlTransform.SetParent(renderer.transform, true);
        }
        else if (controlTransform.parent != renderer.transform)
        {
            Undo.SetTransformParent(controlTransform, renderer.transform, "Parent Crowd Faction Control Transform");
        }

        Vector3 worldPosition = BuildWorldScenePoint(renderer.transform, terrain, centerXZ, verticalOffset);
        Undo.RecordObject(controlTransform, "Move Crowd Faction Control Transform");
        controlTransform.position = worldPosition;

        CrowdVatFactionControlPoint controlPoint = controlTransform.GetComponent<CrowdVatFactionControlPoint>();
        if (controlPoint == null)
            controlPoint = Undo.AddComponent<CrowdVatFactionControlPoint>(controlTransform.gameObject);
        controlPoint.Configure(renderer, faction, CrowdVatFactionControlPointKind.Center);
        EditorUtility.SetDirty(controlTransform);
        return controlTransform;
    }

    private Vector2 ResolveCurrentFactionCenterXZ(CrowdVatIndirectRenderer renderer, SpawnAreaContext spawnArea, CrowdVatFaction faction)
    {
        if (TryResolveFactionCornerBounds(renderer, faction, out Vector2 rangeCenterXZ, out _))
            return rangeCenterXZ;

        SerializedProperty controlTransformProperty = faction == CrowdVatFaction.CampA
            ? _campAControlTransformProperty
            : _campBControlTransformProperty;
        Transform controlTransform = controlTransformProperty != null
            ? controlTransformProperty.objectReferenceValue as Transform
            : null;
        if ((_useFactionControlTransformsProperty?.boolValue ?? false) && controlTransform != null)
        {
            Vector3 localPosition = renderer.transform.InverseTransformPoint(controlTransform.position);
            return new Vector2(localPosition.x, localPosition.z);
        }

        if (_useExplicitFactionCentersProperty.boolValue)
            return faction == CrowdVatFaction.CampA ? _campACenterXZProperty.vector2Value : _campBCenterXZProperty.vector2Value;

        return ResolveAutomaticCampCenterXZ(spawnArea, faction);
    }

    private Vector2 ResolveCurrentFactionAreaSize(CrowdVatIndirectRenderer renderer, SpawnAreaContext spawnArea, CrowdVatFaction faction)
    {
        if (TryResolveFactionCornerBounds(renderer, faction, out _, out Vector2 cornerAreaSize))
            return cornerAreaSize;

        if (_useExplicitFactionAreaSizesProperty != null && _useExplicitFactionAreaSizesProperty.boolValue)
        {
            SerializedProperty areaSizeProperty = faction == CrowdVatFaction.CampA
                ? _campAAreaSizeProperty
                : _campBAreaSizeProperty;
            if (areaSizeProperty != null)
                return ClampAreaSize(areaSizeProperty.vector2Value);
        }

        return ResolveFactionAreaSize(spawnArea.size);
    }

    private FactionCornerSet EnsureFactionCornerTransforms(
        CrowdVatIndirectRenderer renderer,
        CrowdVatFaction faction,
        Transform controlTransform,
        Vector2 centerXZ,
        Vector2 areaSize,
        Terrain terrain,
        float verticalOffset)
    {
        Vector2 clampedAreaSize = ClampAreaSize(areaSize);
        Vector2 halfSize = clampedAreaSize * 0.5f;

        return new FactionCornerSet
        {
            northWest = EnsureFactionCornerTransform(
                renderer,
                controlTransform,
                faction,
                CrowdVatFactionControlPointKind.RangeNorthWest,
                "Range_NW",
                centerXZ + new Vector2(-halfSize.x, halfSize.y),
                terrain,
                verticalOffset),
            northEast = EnsureFactionCornerTransform(
                renderer,
                controlTransform,
                faction,
                CrowdVatFactionControlPointKind.RangeNorthEast,
                "Range_NE",
                centerXZ + new Vector2(halfSize.x, halfSize.y),
                terrain,
                verticalOffset),
            southEast = EnsureFactionCornerTransform(
                renderer,
                controlTransform,
                faction,
                CrowdVatFactionControlPointKind.RangeSouthEast,
                "Range_SE",
                centerXZ + new Vector2(halfSize.x, -halfSize.y),
                terrain,
                verticalOffset),
            southWest = EnsureFactionCornerTransform(
                renderer,
                controlTransform,
                faction,
                CrowdVatFactionControlPointKind.RangeSouthWest,
                "Range_SW",
                centerXZ + new Vector2(-halfSize.x, -halfSize.y),
                terrain,
                verticalOffset)
        };
    }

    private Transform EnsureFactionCornerTransform(
        CrowdVatIndirectRenderer renderer,
        Transform controlTransform,
        CrowdVatFaction faction,
        CrowdVatFactionControlPointKind kind,
        string cornerName,
        Vector2 localXZ,
        Terrain terrain,
        float verticalOffset)
    {
        if (controlTransform == null)
            return null;

        Transform cornerTransform = controlTransform.Find(cornerName);
        if (cornerTransform == null)
        {
            GameObject cornerObject = new GameObject(cornerName);
            Undo.RegisterCreatedObjectUndo(cornerObject, "Create Crowd Faction Range Corner");
            cornerTransform = cornerObject.transform;
            cornerTransform.SetParent(controlTransform, true);
        }
        else if (cornerTransform.parent != controlTransform)
        {
            Undo.SetTransformParent(cornerTransform, controlTransform, "Parent Crowd Faction Range Corner");
        }

        Undo.RecordObject(cornerTransform, "Move Crowd Faction Range Corner");
        cornerTransform.position = BuildWorldScenePoint(renderer.transform, terrain, localXZ, verticalOffset);

        CrowdVatFactionControlPoint controlPoint = cornerTransform.GetComponent<CrowdVatFactionControlPoint>();
        if (controlPoint == null)
            controlPoint = Undo.AddComponent<CrowdVatFactionControlPoint>(cornerTransform.gameObject);
        controlPoint.Configure(renderer, faction, kind);
        EditorUtility.SetDirty(cornerTransform);
        return cornerTransform;
    }

    private void OnSceneGUI()
    {
        return;
    }

    private static void MarkRendererDirty(CrowdVatIndirectRenderer renderer)
    {
        if (renderer == null)
            return;

        EditorUtility.SetDirty(renderer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        if (renderer.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
        renderer.RebuildCrowdLayout();
        SceneView.RepaintAll();
    }

    private static void EnsureSceneViewGizmosEnabled()
    {
        foreach (SceneView sceneView in SceneView.sceneViews)
        {
            if (sceneView == null)
                continue;

            sceneView.drawGizmos = true;
        }

        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.drawGizmos = true;
    }

    private Vector2 DrawFactionHandle(Transform rendererTransform, Terrain terrain, Vector2 centerXZ, Color color, string label, float verticalOffset)
    {
        Vector3 worldPosition = BuildWorldScenePoint(rendererTransform, terrain, centerXZ, verticalOffset);
        float handleSize = Mathf.Max(HandleUtility.GetHandleSize(worldPosition) * HandleSizeFactor, 0.35f);
        Vector3 anchorWorldPosition = BuildWorldScenePoint(rendererTransform, terrain, centerXZ, 0.03f);
        Handles.color = new Color(color.r, color.g, color.b, 0.72f);
        Handles.DrawDottedLine(anchorWorldPosition, worldPosition, 4.0f);
        Handles.DrawSolidDisc(worldPosition, rendererTransform.up, handleSize * HandleDiscScale);
        Handles.color = color;
        Vector3 movedWorldPosition = Handles.FreeMoveHandle(
            worldPosition,
            handleSize * HandleSphereScale,
            Vector3.zero,
            Handles.SphereHandleCap);

        Handles.color = color;
        Handles.DrawWireDisc(worldPosition, rendererTransform.up, handleSize * 2.1f);
        Handles.Label(
            worldPosition + rendererTransform.up * handleSize * 1.45f,
            $"{label}\n拖拽调整中心\nXZ: {centerXZ.x:F1}, {centerXZ.y:F1}");

        Vector3 projectedWorldPosition = ProjectPointToPlane(movedWorldPosition, rendererTransform.position, rendererTransform.up);
        Vector3 movedLocalPosition = rendererTransform.InverseTransformPoint(projectedWorldPosition);
        return new Vector2(movedLocalPosition.x, movedLocalPosition.z);
    }

    private static void DrawRangeOutline(Transform rendererTransform, Terrain terrain, Vector2 centerXZ, Vector2 sizeXZ, Color baseColor, float verticalOffset, string label)
    {
        Vector2 halfSize = sizeXZ * 0.5f;
        Vector3 a = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(-halfSize.x, -halfSize.y), verticalOffset);
        Vector3 b = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(-halfSize.x, halfSize.y), verticalOffset);
        Vector3 c = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(halfSize.x, halfSize.y), verticalOffset);
        Vector3 d = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(halfSize.x, -halfSize.y), verticalOffset);
        Vector3 center = BuildWorldScenePoint(rendererTransform, terrain, centerXZ, verticalOffset);

        Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.95f);
        Handles.DrawAAPolyLine(RangeLineThickness, new[] { a, b, c, d, a });
        Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.35f);
        Handles.DrawDottedLine(a, c, RangeDiagonalScreenSpace);
        Handles.DrawDottedLine(b, d, RangeDiagonalScreenSpace);
        Handles.Label(center + rendererTransform.up * Mathf.Max(0.6f, verticalOffset * 5.0f), $"{label}\n{sizeXZ.x:F1} x {sizeXZ.y:F1}");
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    private static void DrawFactionCenterGizmo(CrowdVatIndirectRenderer renderer, GizmoType gizmoType)
    {
        if (renderer == null)
            return;
        if (CrowdVatGlobalGizmoSystem.SuppressSelectedFactionGizmos)
            return;

        SerializedObject serializedRenderer = new SerializedObject(renderer);
        SerializedProperty factionLayoutProperty = serializedRenderer.FindProperty("_factionLayout");
        if (factionLayoutProperty == null ||
            (CrowdVatFactionLayoutMode)factionLayoutProperty.enumValueIndex != CrowdVatFactionLayoutMode.TwoOpposingFactions)
        {
            return;
        }

        SpawnAreaContext spawnArea = ResolveSpawnAreaContext(serializedRenderer, renderer);
        Vector2 campA = ResolveCurrentFactionCenterXZ(serializedRenderer, renderer, spawnArea, CrowdVatFaction.CampA);
        Vector2 campB = ResolveCurrentFactionCenterXZ(serializedRenderer, renderer, spawnArea, CrowdVatFaction.CampB);
        Vector2 campAAreaSize = ResolveCurrentFactionAreaSize(serializedRenderer, renderer, spawnArea, CrowdVatFaction.CampA);
        Vector2 campBAreaSize = ResolveCurrentFactionAreaSize(serializedRenderer, renderer, spawnArea, CrowdVatFaction.CampB);
        Terrain terrain = ResolveTerrain(serializedRenderer, renderer);
        float handleHeightOffset = ResolveHandleHeightOffset(serializedRenderer);
        float areaHeightOffset = Mathf.Max(0.08f, handleHeightOffset * 0.08f);

        DrawFactionGizmoArea(renderer.transform, terrain, spawnArea.centerXZ, spawnArea.size, TotalAreaColor, areaHeightOffset * 0.65f);
        DrawFactionGizmoArea(renderer.transform, terrain, campA, campAAreaSize, CampAColor, areaHeightOffset);
        DrawFactionGizmoArea(renderer.transform, terrain, campB, campBAreaSize, CampBColor, areaHeightOffset);

        Vector3 campAWorld = BuildWorldScenePoint(renderer.transform, terrain, campA, handleHeightOffset);
        Vector3 campBWorld = BuildWorldScenePoint(renderer.transform, terrain, campB, handleHeightOffset);
        Gizmos.color = LinkColor;
        Gizmos.DrawLine(campAWorld, campBWorld);
        Gizmos.color = CampAColor;
        Gizmos.DrawSphere(campAWorld, Mathf.Max(0.3f, handleHeightOffset * 0.12f));
        Gizmos.color = CampBColor;
        Gizmos.DrawSphere(campBWorld, Mathf.Max(0.3f, handleHeightOffset * 0.12f));
    }

    private static void DrawSceneOverlay(bool useExplicitFactionCenters)
    {
        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(16.0f, 16.0f, 280.0f, 84.0f), EditorStyles.helpBox);
        GUILayout.Label("人群双阵营场景控制", EditorStyles.boldLabel);
        GUILayout.Label(useExplicitFactionCenters
            ? "拖拽红蓝控制点可直接调整两方中心。"
            : "拖拽红蓝控制点后，会自动切换到手动中心模式。");
        GUILayout.Label("白框=总出生范围，红/蓝框=各阵营范围。");
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private static void DrawFactionGizmoArea(Transform rendererTransform, Terrain terrain, Vector2 centerXZ, Vector2 sizeXZ, Color color, float verticalOffset)
    {
        Vector2 halfSize = sizeXZ * 0.5f;
        Vector3 a = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(-halfSize.x, -halfSize.y), verticalOffset);
        Vector3 b = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(-halfSize.x, halfSize.y), verticalOffset);
        Vector3 c = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(halfSize.x, halfSize.y), verticalOffset);
        Vector3 d = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(halfSize.x, -halfSize.y), verticalOffset);

        Color previousColor = Gizmos.color;
        Gizmos.color = new Color(color.r, color.g, color.b, 0.85f);
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
        Gizmos.color = previousColor;
    }

    private float ResolveHandleHeightOffset()
    {
        float collisionHeight = _collisionHeightProperty != null ? _collisionHeightProperty.floatValue : 1.65f;
        return Mathf.Max(0.8f, collisionHeight + 0.15f);
    }

    private static float ResolveHandleHeightOffset(SerializedObject serializedRenderer)
    {
        SerializedProperty collisionHeightProperty = serializedRenderer.FindProperty("_collisionHeight");
        float collisionHeight = collisionHeightProperty != null ? collisionHeightProperty.floatValue : 1.65f;
        return Mathf.Max(0.8f, collisionHeight + 0.15f);
    }

    private static Vector3 BuildWorldScenePoint(Transform rendererTransform, Terrain terrain, Vector2 localXZ, float verticalOffset)
    {
        Vector3 worldPosition = rendererTransform.TransformPoint(new Vector3(localXZ.x, 0.0f, localXZ.y));
        worldPosition.y = ResolveBaseWorldY(rendererTransform.position.y, terrain, worldPosition) + verticalOffset;
        return worldPosition;
    }

    private static float ResolveBaseWorldY(float fallbackY, Terrain terrain, Vector3 worldPosition)
    {
        if (terrain == null || terrain.terrainData == null)
            return fallbackY;

        Vector3 terrainMin = terrain.transform.position;
        Vector3 terrainMax = terrainMin + terrain.terrainData.size;
        if (worldPosition.x < terrainMin.x || worldPosition.x > terrainMax.x ||
            worldPosition.z < terrainMin.z || worldPosition.z > terrainMax.z)
        {
            return fallbackY;
        }

        return terrain.SampleHeight(worldPosition) + terrainMin.y;
    }

    private static Vector3 ProjectPointToPlane(Vector3 worldPosition, Vector3 planeOrigin, Vector3 planeNormal)
    {
        float distanceToPlane = Vector3.Dot(planeNormal, worldPosition - planeOrigin);
        return worldPosition - planeNormal * distanceToPlane;
    }

    private SpawnAreaContext ResolveSpawnAreaContext(CrowdVatIndirectRenderer renderer)
    {
        if (_distributeAcrossWholeTerrainProperty.boolValue && TryResolveTerrain(renderer, out Terrain terrain))
            return BuildTerrainSpawnAreaContext(renderer.transform, terrain);

        Vector2 size = _areaSizeProperty != null
            ? _areaSizeProperty.vector2Value
            : new Vector2(1.0f, 1.0f);
        return new SpawnAreaContext
        {
            size = new Vector2(Mathf.Max(size.x, 0.01f), Mathf.Max(size.y, 0.01f)),
            centerXZ = Vector2.zero
        };
    }

    private bool TryResolveFactionCornerBounds(CrowdVatIndirectRenderer renderer, CrowdVatFaction faction, out Vector2 centerXZ, out Vector2 sizeXZ)
    {
        centerXZ = Vector2.zero;
        sizeXZ = Vector2.zero;
        Transform controlTransform = (faction == CrowdVatFaction.CampA
            ? _campAControlTransformProperty
            : _campBControlTransformProperty)?.objectReferenceValue as Transform;

        Transform northWestCornerTransform = (faction == CrowdVatFaction.CampA
            ? _campANorthWestCornerTransformProperty
            : _campBNorthWestCornerTransformProperty)?.objectReferenceValue as Transform;
        Transform northEastCornerTransform = (faction == CrowdVatFaction.CampA
            ? _campANorthEastCornerTransformProperty
            : _campBNorthEastCornerTransformProperty)?.objectReferenceValue as Transform;
        Transform southEastCornerTransform = (faction == CrowdVatFaction.CampA
            ? _campASouthEastCornerTransformProperty
            : _campBSouthEastCornerTransformProperty)?.objectReferenceValue as Transform;
        Transform southWestCornerTransform = (faction == CrowdVatFaction.CampA
            ? _campASouthWestCornerTransformProperty
            : _campBSouthWestCornerTransformProperty)?.objectReferenceValue as Transform;

        return TryResolveFactionCornerBounds(
            renderer,
            controlTransform,
            northWestCornerTransform,
            northEastCornerTransform,
            southEastCornerTransform,
            southWestCornerTransform,
            out centerXZ,
            out sizeXZ);
    }

    private static bool TryResolveFactionCornerBounds(SerializedObject serializedRenderer, CrowdVatIndirectRenderer renderer, CrowdVatFaction faction, out Vector2 centerXZ, out Vector2 sizeXZ)
    {
        centerXZ = Vector2.zero;
        sizeXZ = Vector2.zero;
        Transform controlTransform = serializedRenderer.FindProperty(faction == CrowdVatFaction.CampA ? "_campAControlTransform" : "_campBControlTransform")?.objectReferenceValue as Transform;

        Transform northWestCornerTransform = serializedRenderer.FindProperty(faction == CrowdVatFaction.CampA ? "_campANorthWestCornerTransform" : "_campBNorthWestCornerTransform")?.objectReferenceValue as Transform;
        Transform northEastCornerTransform = serializedRenderer.FindProperty(faction == CrowdVatFaction.CampA ? "_campANorthEastCornerTransform" : "_campBNorthEastCornerTransform")?.objectReferenceValue as Transform;
        Transform southEastCornerTransform = serializedRenderer.FindProperty(faction == CrowdVatFaction.CampA ? "_campASouthEastCornerTransform" : "_campBSouthEastCornerTransform")?.objectReferenceValue as Transform;
        Transform southWestCornerTransform = serializedRenderer.FindProperty(faction == CrowdVatFaction.CampA ? "_campASouthWestCornerTransform" : "_campBSouthWestCornerTransform")?.objectReferenceValue as Transform;

        return TryResolveFactionCornerBounds(
            renderer,
            controlTransform,
            northWestCornerTransform,
            northEastCornerTransform,
            southEastCornerTransform,
            southWestCornerTransform,
            out centerXZ,
            out sizeXZ);
    }

    private static bool TryResolveFactionCornerBounds(
        CrowdVatIndirectRenderer renderer,
        Transform controlTransform,
        Transform northWestCornerTransform,
        Transform northEastCornerTransform,
        Transform southEastCornerTransform,
        Transform southWestCornerTransform,
        out Vector2 centerXZ,
        out Vector2 sizeXZ)
    {
        centerXZ = Vector2.zero;
        sizeXZ = Vector2.zero;
        if (renderer == null ||
            northWestCornerTransform == null ||
            northEastCornerTransform == null ||
            southEastCornerTransform == null ||
            southWestCornerTransform == null)
        {
            return false;
        }

        Transform rendererTransform = renderer.transform;
        if (controlTransform != null)
        {
            Vector3 controlLocalPosition = rendererTransform.InverseTransformPoint(controlTransform.position);
            Vector3 northWestLocalOffset = controlTransform.InverseTransformPoint(northWestCornerTransform.position);
            Vector3 northEastLocalOffset = controlTransform.InverseTransformPoint(northEastCornerTransform.position);
            Vector3 southEastLocalOffset = controlTransform.InverseTransformPoint(southEastCornerTransform.position);
            Vector3 southWestLocalOffset = controlTransform.InverseTransformPoint(southWestCornerTransform.position);

            float halfWidth = Mathf.Max(
                Mathf.Abs(northWestLocalOffset.x),
                Mathf.Abs(northEastLocalOffset.x),
                Mathf.Abs(southEastLocalOffset.x),
                Mathf.Abs(southWestLocalOffset.x));
            float halfDepth = Mathf.Max(
                Mathf.Abs(northWestLocalOffset.z),
                Mathf.Abs(northEastLocalOffset.z),
                Mathf.Abs(southEastLocalOffset.z),
                Mathf.Abs(southWestLocalOffset.z));

            centerXZ = new Vector2(controlLocalPosition.x, controlLocalPosition.z);
            sizeXZ = ClampAreaSize(new Vector2(halfWidth * 2.0f, halfDepth * 2.0f));
            return true;
        }

        Vector3 northWestLocalPosition = rendererTransform.InverseTransformPoint(northWestCornerTransform.position);
        Vector3 northEastLocalPosition = rendererTransform.InverseTransformPoint(northEastCornerTransform.position);
        Vector3 southEastLocalPosition = rendererTransform.InverseTransformPoint(southEastCornerTransform.position);
        Vector3 southWestLocalPosition = rendererTransform.InverseTransformPoint(southWestCornerTransform.position);

        float minX = Mathf.Min(northWestLocalPosition.x, northEastLocalPosition.x, southEastLocalPosition.x, southWestLocalPosition.x);
        float maxX = Mathf.Max(northWestLocalPosition.x, northEastLocalPosition.x, southEastLocalPosition.x, southWestLocalPosition.x);
        float minZ = Mathf.Min(northWestLocalPosition.z, northEastLocalPosition.z, southEastLocalPosition.z, southWestLocalPosition.z);
        float maxZ = Mathf.Max(northWestLocalPosition.z, northEastLocalPosition.z, southEastLocalPosition.z, southWestLocalPosition.z);

        centerXZ = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        sizeXZ = ClampAreaSize(new Vector2(maxX - minX, maxZ - minZ));
        return true;
    }

    private static Vector2 ResolveCurrentFactionCenterXZ(SerializedObject serializedRenderer, CrowdVatIndirectRenderer renderer, SpawnAreaContext spawnArea, CrowdVatFaction faction)
    {
        if (TryResolveFactionCornerBounds(serializedRenderer, renderer, faction, out Vector2 rangeCenterXZ, out _))
            return rangeCenterXZ;

        bool useControlTransforms = serializedRenderer.FindProperty("_useFactionControlTransforms")?.boolValue ?? false;
        Transform controlTransform = serializedRenderer.FindProperty(faction == CrowdVatFaction.CampA ? "_campAControlTransform" : "_campBControlTransform")?.objectReferenceValue as Transform;
        if (useControlTransforms && controlTransform != null)
        {
            Vector3 localPosition = renderer.transform.InverseTransformPoint(controlTransform.position);
            return new Vector2(localPosition.x, localPosition.z);
        }

        bool useExplicitFactionCenters = serializedRenderer.FindProperty("_useExplicitFactionCenters")?.boolValue ?? false;
        if (useExplicitFactionCenters)
        {
            SerializedProperty centerProperty = serializedRenderer.FindProperty(faction == CrowdVatFaction.CampA ? "_campACenterXZ" : "_campBCenterXZ");
            if (centerProperty != null)
                return centerProperty.vector2Value;
        }

        return ResolveAutomaticCampCenterXZ(serializedRenderer, spawnArea, faction);
    }

    private static Vector2 ResolveCurrentFactionAreaSize(SerializedObject serializedRenderer, CrowdVatIndirectRenderer renderer, SpawnAreaContext spawnArea, CrowdVatFaction faction)
    {
        if (TryResolveFactionCornerBounds(serializedRenderer, renderer, faction, out _, out Vector2 cornerAreaSize))
            return cornerAreaSize;

        bool useExplicitFactionAreaSizes = serializedRenderer.FindProperty("_useExplicitFactionAreaSizes")?.boolValue ?? false;
        if (useExplicitFactionAreaSizes)
        {
            SerializedProperty areaSizeProperty = serializedRenderer.FindProperty(faction == CrowdVatFaction.CampA ? "_campAAreaSize" : "_campBAreaSize");
            if (areaSizeProperty != null)
                return ClampAreaSize(areaSizeProperty.vector2Value);
        }

        return ResolveFactionAreaSize(serializedRenderer, spawnArea.size);
    }

    private static Vector2 ClampAreaSize(Vector2 areaSize)
    {
        return new Vector2(Mathf.Max(areaSize.x, 0.01f), Mathf.Max(areaSize.y, 0.01f));
    }

    private static SpawnAreaContext ResolveSpawnAreaContext(SerializedObject serializedRenderer, CrowdVatIndirectRenderer renderer)
    {
        SerializedProperty distributeAcrossWholeTerrainProperty = serializedRenderer.FindProperty("_distributeAcrossWholeTerrain");
        if (distributeAcrossWholeTerrainProperty != null &&
            distributeAcrossWholeTerrainProperty.boolValue &&
            TryResolveTerrain(serializedRenderer, renderer, out Terrain terrain))
        {
            return BuildTerrainSpawnAreaContext(renderer.transform, terrain);
        }

        SerializedProperty areaSizeProperty = serializedRenderer.FindProperty("_areaSize");
        Vector2 size = areaSizeProperty != null ? areaSizeProperty.vector2Value : new Vector2(1.0f, 1.0f);
        return new SpawnAreaContext
        {
            size = new Vector2(Mathf.Max(size.x, 0.01f), Mathf.Max(size.y, 0.01f)),
            centerXZ = Vector2.zero
        };
    }

    private bool TryResolveTerrain(CrowdVatIndirectRenderer renderer, out Terrain terrain)
    {
        terrain = _terrainProperty != null ? _terrainProperty.objectReferenceValue as Terrain : null;
        if (terrain != null && terrain.terrainData != null)
            return true;

        if (_autoResolveTerrainProperty == null || !_autoResolveTerrainProperty.boolValue)
            return false;

        terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
            return true;

        terrain = Object.FindFirstObjectByType<Terrain>();
        return terrain != null && terrain.terrainData != null;
    }

    private static Terrain ResolveTerrain(SerializedObject serializedRenderer, CrowdVatIndirectRenderer renderer)
    {
        TryResolveTerrain(serializedRenderer, renderer, out Terrain terrain);
        return terrain;
    }

    private static bool TryResolveTerrain(SerializedObject serializedRenderer, CrowdVatIndirectRenderer renderer, out Terrain terrain)
    {
        SerializedProperty terrainProperty = serializedRenderer.FindProperty("_terrain");
        terrain = terrainProperty != null ? terrainProperty.objectReferenceValue as Terrain : null;
        if (terrain != null && terrain.terrainData != null)
            return true;

        SerializedProperty autoResolveTerrainProperty = serializedRenderer.FindProperty("_autoResolveTerrain");
        if (autoResolveTerrainProperty == null || !autoResolveTerrainProperty.boolValue)
            return false;

        terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
            return true;

        terrain = Object.FindFirstObjectByType<Terrain>();
        return terrain != null && terrain.terrainData != null;
    }

    private static SpawnAreaContext BuildTerrainSpawnAreaContext(Transform rendererTransform, Terrain terrain)
    {
        Vector3 terrainMin = terrain.transform.position;
        Vector3 terrainMax = terrainMin + terrain.terrainData.size;
        Matrix4x4 worldToLocal = rendererTransform.worldToLocalMatrix;

        Vector3 localCornerA = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMin.x, rendererTransform.position.y, terrainMin.z));
        Vector3 localCornerB = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMin.x, rendererTransform.position.y, terrainMax.z));
        Vector3 localCornerC = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMax.x, rendererTransform.position.y, terrainMin.z));
        Vector3 localCornerD = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMax.x, rendererTransform.position.y, terrainMax.z));

        float minX = Mathf.Min(localCornerA.x, localCornerB.x, localCornerC.x, localCornerD.x);
        float maxX = Mathf.Max(localCornerA.x, localCornerB.x, localCornerC.x, localCornerD.x);
        float minZ = Mathf.Min(localCornerA.z, localCornerB.z, localCornerC.z, localCornerD.z);
        float maxZ = Mathf.Max(localCornerA.z, localCornerB.z, localCornerC.z, localCornerD.z);

        return new SpawnAreaContext
        {
            size = new Vector2(Mathf.Max(0.01f, maxX - minX), Mathf.Max(0.01f, maxZ - minZ)),
            centerXZ = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f)
        };
    }

    private Vector2 ResolveAutomaticCampCenterXZ(SpawnAreaContext spawnArea, CrowdVatFaction faction)
    {
        float factionOffset = ResolveFactionAxisOffset(spawnArea.size);
        Vector2 factionCenter = spawnArea.centerXZ;
        CrowdVatFactionSplitAxis splitAxis = (CrowdVatFactionSplitAxis)_factionSplitAxisProperty.enumValueIndex;
        if (splitAxis == CrowdVatFactionSplitAxis.Width)
            factionCenter.x += faction == CrowdVatFaction.CampA ? -factionOffset : factionOffset;
        else
            factionCenter.y += faction == CrowdVatFaction.CampA ? -factionOffset : factionOffset;

        return factionCenter;
    }

    private static Vector2 ResolveAutomaticCampCenterXZ(SerializedObject serializedRenderer, SpawnAreaContext spawnArea, CrowdVatFaction faction)
    {
        float factionOffset = ResolveFactionAxisOffset(serializedRenderer, spawnArea.size);
        Vector2 factionCenter = spawnArea.centerXZ;
        SerializedProperty factionSplitAxisProperty = serializedRenderer.FindProperty("_factionSplitAxis");
        CrowdVatFactionSplitAxis splitAxis = factionSplitAxisProperty != null
            ? (CrowdVatFactionSplitAxis)factionSplitAxisProperty.enumValueIndex
            : CrowdVatFactionSplitAxis.Depth;
        if (splitAxis == CrowdVatFactionSplitAxis.Width)
            factionCenter.x += faction == CrowdVatFaction.CampA ? -factionOffset : factionOffset;
        else
            factionCenter.y += faction == CrowdVatFaction.CampA ? -factionOffset : factionOffset;

        return factionCenter;
    }

    private Vector2 ResolveFactionAreaSize(Vector2 totalAreaSize)
    {
        float totalWidth = Mathf.Max(totalAreaSize.x, 0.01f);
        float totalDepth = Mathf.Max(totalAreaSize.y, 0.01f);
        CrowdVatFactionSplitAxis splitAxis = (CrowdVatFactionSplitAxis)_factionSplitAxisProperty.enumValueIndex;
        float splitLength = splitAxis == CrowdVatFactionSplitAxis.Width ? totalWidth : totalDepth;
        float clampedGap = Mathf.Clamp(_factionCenterGapProperty.floatValue, 0.0f, Mathf.Max(0.0f, splitLength - 0.02f));
        float factionLength = Mathf.Max(0.01f, (splitLength - clampedGap) * 0.5f);

        return splitAxis == CrowdVatFactionSplitAxis.Width
            ? new Vector2(factionLength, totalDepth)
            : new Vector2(totalWidth, factionLength);
    }

    private static Vector2 ResolveFactionAreaSize(SerializedObject serializedRenderer, Vector2 totalAreaSize)
    {
        float totalWidth = Mathf.Max(totalAreaSize.x, 0.01f);
        float totalDepth = Mathf.Max(totalAreaSize.y, 0.01f);
        SerializedProperty factionSplitAxisProperty = serializedRenderer.FindProperty("_factionSplitAxis");
        CrowdVatFactionSplitAxis splitAxis = factionSplitAxisProperty != null
            ? (CrowdVatFactionSplitAxis)factionSplitAxisProperty.enumValueIndex
            : CrowdVatFactionSplitAxis.Depth;
        SerializedProperty factionCenterGapProperty = serializedRenderer.FindProperty("_factionCenterGap");
        float factionCenterGap = factionCenterGapProperty != null ? factionCenterGapProperty.floatValue : 0.0f;
        float splitLength = splitAxis == CrowdVatFactionSplitAxis.Width ? totalWidth : totalDepth;
        float clampedGap = Mathf.Clamp(factionCenterGap, 0.0f, Mathf.Max(0.0f, splitLength - 0.02f));
        float factionLength = Mathf.Max(0.01f, (splitLength - clampedGap) * 0.5f);

        return splitAxis == CrowdVatFactionSplitAxis.Width
            ? new Vector2(factionLength, totalDepth)
            : new Vector2(totalWidth, factionLength);
    }

    private float ResolveFactionAxisOffset(Vector2 totalAreaSize)
    {
        CrowdVatFactionSplitAxis splitAxis = (CrowdVatFactionSplitAxis)_factionSplitAxisProperty.enumValueIndex;
        float splitLength = splitAxis == CrowdVatFactionSplitAxis.Width
            ? Mathf.Max(totalAreaSize.x, 0.01f)
            : Mathf.Max(totalAreaSize.y, 0.01f);
        float clampedGap = Mathf.Clamp(_factionCenterGapProperty.floatValue, 0.0f, Mathf.Max(0.0f, splitLength - 0.02f));
        float factionLength = Mathf.Max(0.01f, (splitLength - clampedGap) * 0.5f);
        return clampedGap * 0.5f + factionLength * 0.5f;
    }

    private static float ResolveFactionAxisOffset(SerializedObject serializedRenderer, Vector2 totalAreaSize)
    {
        SerializedProperty factionSplitAxisProperty = serializedRenderer.FindProperty("_factionSplitAxis");
        CrowdVatFactionSplitAxis splitAxis = factionSplitAxisProperty != null
            ? (CrowdVatFactionSplitAxis)factionSplitAxisProperty.enumValueIndex
            : CrowdVatFactionSplitAxis.Depth;
        SerializedProperty factionCenterGapProperty = serializedRenderer.FindProperty("_factionCenterGap");
        float factionCenterGap = factionCenterGapProperty != null ? factionCenterGapProperty.floatValue : 0.0f;
        float splitLength = splitAxis == CrowdVatFactionSplitAxis.Width
            ? Mathf.Max(totalAreaSize.x, 0.01f)
            : Mathf.Max(totalAreaSize.y, 0.01f);
        float clampedGap = Mathf.Clamp(factionCenterGap, 0.0f, Mathf.Max(0.0f, splitLength - 0.02f));
        float factionLength = Mathf.Max(0.01f, (splitLength - clampedGap) * 0.5f);
        return clampedGap * 0.5f + factionLength * 0.5f;
    }
}
