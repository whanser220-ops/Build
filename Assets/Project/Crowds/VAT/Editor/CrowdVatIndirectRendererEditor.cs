using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
    private static readonly Color CollisionCapsuleColor = new Color(0.16f, 0.82f, 1.0f, 0.92f);
    private static readonly Color CombatHitCapsuleColor = new Color(1.0f, 0.56f, 0.14f, 0.95f);

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
    private SerializedProperty _collisionRadiusProperty;
    private SerializedProperty _collisionHeightProperty;
    private SerializedProperty _enableGpuInstanceCombatProperty;
    private SerializedProperty _combatHitRadiusPaddingProperty;
    private SerializedProperty _combatHitHeightPaddingProperty;

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
        _collisionRadiusProperty = serializedObject.FindProperty("_collisionRadius");
        _collisionHeightProperty = serializedObject.FindProperty("_collisionHeight");
        _enableGpuInstanceCombatProperty = serializedObject.FindProperty("_enableGpuInstanceCombat");
        _combatHitRadiusPaddingProperty = serializedObject.FindProperty("_combatHitRadiusPadding");
        _combatHitHeightPaddingProperty = serializedObject.FindProperty("_combatHitHeightPadding");
        EnsureSceneViewGizmosEnabled();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        DrawCombatHitCapsuleTools();
        DrawEnvironmentDistanceBakeTools();
        DrawSceneControlTips();
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawCombatHitCapsuleTools()
    {
        CrowdVatIndirectRenderer renderer = target as CrowdVatIndirectRenderer;
        if (renderer == null)
            return;

        float collisionRadius = _collisionRadiusProperty != null ? _collisionRadiusProperty.floatValue : 0.35f;
        float collisionHeight = _collisionHeightProperty != null ? _collisionHeightProperty.floatValue : 1.65f;
        float combatHitRadiusPadding = _combatHitRadiusPaddingProperty != null ? _combatHitRadiusPaddingProperty.floatValue : 0.18f;
        float combatHitHeightPadding = _combatHitHeightPaddingProperty != null ? _combatHitHeightPaddingProperty.floatValue : 0.2f;
        float baseCapsuleHeight = ComputeCapsuleHeight(collisionRadius, collisionHeight);
        float combatHitRadius = ComputeExpandedRadius(collisionRadius, combatHitRadiusPadding);
        float combatHitHeight = ComputeExpandedCapsuleHeight(collisionRadius, collisionHeight, combatHitRadiusPadding, combatHitHeightPadding);
        bool combatEnabled = _enableGpuInstanceCombatProperty == null || _enableGpuInstanceCombatProperty.boolValue;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Combat 命中胶囊预览", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            combatEnabled
                ? "选中当前 CrowdVatIndirectRenderer 后，SceneView 会显示两层竖直胶囊：青色=基础碰撞胶囊，橙色=Combat 命中胶囊。拖拽橙色侧向手柄调命中半径补偿，拖拽橙色顶部手柄调命中高度补偿。"
                : "当前 GPU Combat 处于关闭状态，但仍可在 SceneView 里预览并调整命中胶囊参数；启用 Combat 后会直接使用这些数值。",
            MessageType.None);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("基础胶囊半径", collisionRadius);
            EditorGUILayout.FloatField("基础胶囊总高度", baseCapsuleHeight);
            EditorGUILayout.FloatField("命中胶囊半径", combatHitRadius);
            EditorGUILayout.FloatField("命中胶囊总高度", combatHitHeight);
        }

        if (GUILayout.Button("刷新 Scene 命中胶囊预览"))
        {
            EnsureSceneViewGizmosEnabled();
            SceneView.RepaintAll();
        }
    }

    private void DrawEnvironmentDistanceBakeTools()
    {
        CrowdVatIndirectRenderer renderer = target as CrowdVatIndirectRenderer;
        if (renderer == null)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("环境距离场资产", EditorStyles.boldLabel);

        if (renderer.SceneQueryFieldAsset == null)
        {
            EditorGUILayout.HelpBox("如果要把 terrain + obstacle 统一烘成运行时环境距离场，请先指定 Scene Query Field Asset。", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox("会把当前 renderer 可解析到的 terrain 与 static obstacle SDF 源，烘焙成统一 3D 环境距离场并写回 Scene Query Field Asset。运行时 crowd PBD 会优先采样这个 baked field。", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("烘焙统一环境距离场"))
            {
                CrowdVatSceneQueryFieldBaker.BakeEnvironmentDistanceField(renderer);
            }

            if (GUILayout.Button("选中 Scene Query Asset"))
            {
                Selection.activeObject = renderer.SceneQueryFieldAsset;
                EditorGUIUtility.PingObject(renderer.SceneQueryFieldAsset);
            }
        }
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
            EditorGUILayout.HelpBox("当前使用空物体控制两边阵营。直接在 Hierarchy 里展开 CampA_Control / CampB_Control：移动根空物体调整中心，拖拽 Range_NW / NE / SE / SW 四个角点调整初始生成范围。这个范围只影响出生分布，不限制后续运行时移动。", MessageType.Info);
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
        if (targets.Length != 1)
            return;

        CrowdVatIndirectRenderer renderer = target as CrowdVatIndirectRenderer;
        if (renderer == null)
            return;

        serializedObject.Update();
        EnsureSceneViewGizmosEnabled();
        DrawCombatHitCapsuleSceneHandles(renderer);
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

    private static void MarkRendererPropertiesDirty(CrowdVatIndirectRenderer renderer)
    {
        if (renderer == null)
            return;

        EditorUtility.SetDirty(renderer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        if (renderer.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
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
        GUILayout.Label("白框=总出生范围，红/蓝框=各阵营初始生成范围，不是运行时限制。");
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

    private void DrawCombatHitCapsuleSceneHandles(CrowdVatIndirectRenderer renderer)
    {
        if (_collisionRadiusProperty == null ||
            _collisionHeightProperty == null ||
            _combatHitRadiusPaddingProperty == null ||
            _combatHitHeightPaddingProperty == null)
        {
            return;
        }

        SpawnAreaContext spawnArea = ResolveSpawnAreaContext(renderer);
        TryResolveTerrain(renderer, out Terrain terrain);

        float collisionRadius = _collisionRadiusProperty.floatValue;
        float collisionHeight = _collisionHeightProperty.floatValue;
        float combatHitRadiusPadding = _combatHitRadiusPaddingProperty.floatValue;
        float combatHitHeightPadding = _combatHitHeightPaddingProperty.floatValue;
        float baseCapsuleHeight = ComputeCapsuleHeight(collisionRadius, collisionHeight);
        float combatHitRadius = ComputeExpandedRadius(collisionRadius, combatHitRadiusPadding);
        float combatHitHeight = ComputeExpandedCapsuleHeight(collisionRadius, collisionHeight, combatHitRadiusPadding, combatHitHeightPadding);

        Transform rendererTransform = renderer.transform;
        Vector3 capsuleBaseWorld = BuildWorldScenePoint(rendererTransform, terrain, spawnArea.centerXZ, 0.0f);
        Vector3 capsuleAxis = rendererTransform.up.sqrMagnitude > 1e-6f ? rendererTransform.up.normalized : Vector3.up;
        Vector3 capsuleRight = rendererTransform.right.sqrMagnitude > 1e-6f ? rendererTransform.right.normalized : Vector3.right;
        Vector3 capsuleForward = rendererTransform.forward.sqrMagnitude > 1e-6f ? rendererTransform.forward.normalized : Vector3.forward;
        Vector3 baseCapsuleCenter = capsuleBaseWorld + capsuleAxis * baseCapsuleHeight * 0.5f;
        Vector3 combatCapsuleCenter = capsuleBaseWorld + capsuleAxis * combatHitHeight * 0.5f;
        float labelHandleSize = Mathf.Max(HandleUtility.GetHandleSize(combatCapsuleCenter) * HandleSizeFactor, 0.35f);

        DrawVerticalCapsule(
            capsuleBaseWorld,
            capsuleAxis,
            capsuleRight,
            capsuleForward,
            baseCapsuleHeight,
            collisionRadius,
            CollisionCapsuleColor);
        DrawVerticalCapsule(
            capsuleBaseWorld,
            capsuleAxis,
            capsuleRight,
            capsuleForward,
            combatHitHeight,
            combatHitRadius,
            CombatHitCapsuleColor);

        Handles.color = CollisionCapsuleColor;
        Handles.Label(
            baseCapsuleCenter + capsuleForward * Mathf.Max(collisionRadius + 0.08f, labelHandleSize * 0.3f),
            $"基础碰撞胶囊\nr={collisionRadius:F2}  h={baseCapsuleHeight:F2}");

        Handles.color = CombatHitCapsuleColor;
        Handles.Label(
            combatCapsuleCenter + capsuleForward * Mathf.Max(combatHitRadius + 0.12f, labelHandleSize * 0.35f),
            $"Combat 命中胶囊\n拖侧边=半径补偿  拖顶部=高度补偿\nr={combatHitRadius:F2}  h={combatHitHeight:F2}");

        Vector3 radiusHandleCenter = capsuleBaseWorld + capsuleAxis * Mathf.Max(combatHitRadius, combatHitHeight * 0.45f);
        Vector3 radiusHandlePosition = radiusHandleCenter + capsuleRight * combatHitRadius;
        Vector3 topHandlePosition = capsuleBaseWorld + capsuleAxis * combatHitHeight;

        EditorGUI.BeginChangeCheck();
        Handles.color = CombatHitCapsuleColor;
        Vector3 movedRadiusHandle = Handles.Slider(
            radiusHandlePosition,
            capsuleRight,
            labelHandleSize * 0.95f,
            Handles.SphereHandleCap,
            0.0f);
        float adjustedCombatHitRadius = Mathf.Max(
            collisionRadius,
            Vector3.Dot(movedRadiusHandle - radiusHandleCenter, capsuleRight));

        Handles.color = new Color(CombatHitCapsuleColor.r, CombatHitCapsuleColor.g, CombatHitCapsuleColor.b, 0.82f);
        Vector3 movedTopHandle = Handles.Slider(
            topHandlePosition,
            capsuleAxis,
            labelHandleSize,
            Handles.CubeHandleCap,
            0.0f);
        float minimumCombatHitHeight = Mathf.Max(collisionHeight, adjustedCombatHitRadius * 2.0f);
        float adjustedCombatHitHeight = Mathf.Max(
            minimumCombatHitHeight,
            Vector3.Dot(movedTopHandle - capsuleBaseWorld, capsuleAxis));

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(renderer, "Adjust Combat Hit Capsule");
            _combatHitRadiusPaddingProperty.floatValue = Mathf.Max(0.0f, adjustedCombatHitRadius - collisionRadius);
            _combatHitHeightPaddingProperty.floatValue = Mathf.Max(0.0f, adjustedCombatHitHeight - collisionHeight);
            serializedObject.ApplyModifiedProperties();
            MarkRendererPropertiesDirty(renderer);
        }
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

    private static float ComputeCapsuleHeight(float radius, float requestedHeight)
    {
        return Mathf.Max(Mathf.Max(radius, 0.01f) * 2.0f, Mathf.Max(requestedHeight, 0.02f));
    }

    private static float ComputeExpandedRadius(float baseRadius, float extraRadiusPadding)
    {
        return Mathf.Max(0.01f, Mathf.Max(baseRadius, baseRadius + Mathf.Max(0.0f, extraRadiusPadding)));
    }

    private static float ComputeExpandedCapsuleHeight(float baseRadius, float baseHeight, float extraRadiusPadding, float extraHeightPadding)
    {
        float expandedRadius = ComputeExpandedRadius(baseRadius, extraRadiusPadding);
        float expandedHeight = Mathf.Max(baseHeight, baseHeight + Mathf.Max(0.0f, extraHeightPadding));
        return Mathf.Max(expandedRadius * 2.0f, expandedHeight);
    }

    private static void DrawVerticalCapsule(
        Vector3 baseWorldPosition,
        Vector3 axis,
        Vector3 right,
        Vector3 forward,
        float totalHeight,
        float radius,
        Color color)
    {
        float clampedRadius = Mathf.Max(0.01f, radius);
        float clampedHeight = Mathf.Max(clampedRadius * 2.0f, totalHeight);
        Vector3 bottom = baseWorldPosition + axis * clampedRadius;
        Vector3 top = baseWorldPosition + axis * (clampedHeight - clampedRadius);
        Color previousColor = Handles.color;
        Handles.color = color;
        DrawWireCapsule(bottom, top, clampedRadius, right, forward);
        Handles.color = previousColor;
    }

    private static void DrawWireCapsule(Vector3 start, Vector3 end, float radius, Vector3 right, Vector3 forward)
    {
        Vector3 axis = end - start;
        if (axis.sqrMagnitude <= 0.0001f)
        {
            DrawWireSphere(start, radius);
            return;
        }

        Vector3 axisDirection = axis.normalized;
        Vector3 resolvedRight = Vector3.ProjectOnPlane(right, axisDirection).normalized;
        if (resolvedRight.sqrMagnitude <= 1e-6f)
            resolvedRight = Vector3.ProjectOnPlane(Vector3.right, axisDirection).normalized;

        Vector3 resolvedForward = Vector3.ProjectOnPlane(forward, axisDirection).normalized;
        if (resolvedForward.sqrMagnitude <= 1e-6f)
            resolvedForward = Vector3.Cross(axisDirection, resolvedRight).normalized;

        resolvedRight *= radius;
        resolvedForward *= radius;

        Handles.DrawWireDisc(start, axisDirection, radius);
        Handles.DrawWireDisc(end, axisDirection, radius);
        Handles.DrawLine(start + resolvedRight, end + resolvedRight);
        Handles.DrawLine(start - resolvedRight, end - resolvedRight);
        Handles.DrawLine(start + resolvedForward, end + resolvedForward);
        Handles.DrawLine(start - resolvedForward, end - resolvedForward);
        DrawWireSphere(start, radius);
        DrawWireSphere(end, radius);
    }

    private static void DrawWireSphere(Vector3 center, float radius)
    {
        Handles.DrawWireDisc(center, Vector3.up, radius);
        Handles.DrawWireDisc(center, Vector3.right, radius);
        Handles.DrawWireDisc(center, Vector3.forward, radius);
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

        terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
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

        terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
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

public static class CrowdVatSceneQueryFieldBaker
{
    private const TextureFormat BakedEnvironmentTextureFormat = TextureFormat.RFloat;
    private const float HighPrecisionHorizontalVoxelSize = 0.25f;
    private const float HighPrecisionVerticalVoxelSize = 0.2f;
    private const int MinHighPrecisionHorizontalResolution = 64;
    private const int MinHighPrecisionVerticalResolution = 32;
    private const int MaxHighPrecisionHorizontalResolution = 512;
    private const int MaxHighPrecisionVerticalResolution = 160;

    public static bool BakeEnvironmentDistanceField(CrowdVatIndirectRenderer renderer)
    {
        if (renderer == null)
            throw new ArgumentNullException(nameof(renderer));

        CrowdVatSceneQueryFieldAsset fieldAsset = renderer.SceneQueryFieldAsset;
        if (fieldAsset == null)
        {
            EditorUtility.DisplayDialog("环境距离场烘焙", "请先给 CrowdVatIndirectRenderer 指定 Scene Query Field Asset。", "确定");
            return false;
        }

        bool hasGround = renderer.TryGetGroundQueryFieldDescriptor(out CrowdVatGroundFieldDescriptor groundDescriptor);
        bool hasObstacleField = renderer.TryGetObstacleDistanceFieldDescriptor(out CrowdVatObstacleDistanceFieldDescriptor obstacleDescriptor);
        Collider[] obstacleColliders = ResolveObstacleColliders(renderer);
        bool hasObstacleColliderFallback = obstacleColliders.Length > 0;
        if (!hasGround && !hasObstacleField && !hasObstacleColliderFallback)
        {
            EditorUtility.DisplayDialog("环境距离场烘焙", "当前既没有可用地形，也没有可用的静态 obstacle SDF，无法生成统一环境距离场。", "确定");
            return false;
        }

        string assetPath = AssetDatabase.GetAssetPath(fieldAsset);
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog("环境距离场烘焙", "Scene Query Field Asset 还没有有效的 Asset 路径。", "确定");
            return false;
        }

        Vector3 worldCenter = fieldAsset.BakedEnvironmentDistanceWorldCenter;
        Vector3 worldSize = fieldAsset.BakedEnvironmentDistanceWorldSize;
        Vector3Int resolution = BuildHighPrecisionResolution(worldSize);
        CrowdVatEnvironmentDistanceFieldDescriptor sourceDescriptor = new CrowdVatEnvironmentDistanceFieldDescriptor
        {
            bakedField = default,
            groundField = hasGround ? groundDescriptor : default,
            obstacleField = hasObstacleField ? obstacleDescriptor : default
        };

        Texture3D bakedTexture;
        try
        {
            bakedTexture = BuildEnvironmentDistanceTexture(
                fieldAsset.name,
                sourceDescriptor,
                obstacleColliders,
                resolution,
                worldCenter,
                worldSize);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Texture3D previousTexture = fieldAsset.BakedEnvironmentDistanceTexture;
        Undo.RecordObject(fieldAsset, "Bake Crowd Environment Distance Field");
        fieldAsset.SetBakedEnvironmentDistanceField(bakedTexture, worldCenter, worldSize, 1.0f, 0.0f);
        EditorUtility.SetDirty(fieldAsset);
        LogBakePrecision(worldSize, resolution, bakedTexture);

        AssetDatabase.StartAssetEditing();
        try
        {
            if (previousTexture != null &&
                previousTexture != bakedTexture &&
                AssetDatabase.GetAssetPath(previousTexture) == assetPath)
            {
                AssetDatabase.RemoveObjectFromAsset(previousTexture);
                UnityEngine.Object.DestroyImmediate(previousTexture, true);
            }

            AssetDatabase.AddObjectToAsset(bakedTexture, fieldAsset);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        EditorUtility.SetDirty(bakedTexture);
        AssetDatabase.SaveAssets();
        return true;
    }

    private static Texture3D BuildEnvironmentDistanceTexture(
        string assetName,
        CrowdVatEnvironmentDistanceFieldDescriptor sourceDescriptor,
        Collider[] obstacleColliders,
        Vector3Int resolution,
        Vector3 worldCenter,
        Vector3 worldSize)
    {
        int width = Mathf.Max(2, resolution.x);
        int height = Mathf.Max(2, resolution.y);
        int depth = Mathf.Max(2, resolution.z);
        Vector3 clampedWorldSize = new Vector3(
            Mathf.Max(0.01f, worldSize.x),
            Mathf.Max(0.01f, worldSize.y),
            Mathf.Max(0.01f, worldSize.z));
        Vector3 worldMin = worldCenter - clampedWorldSize * 0.5f;
        Vector3 voxelSize = new Vector3(
            clampedWorldSize.x / Mathf.Max(width - 1, 1),
            clampedWorldSize.y / Mathf.Max(height - 1, 1),
            clampedWorldSize.z / Mathf.Max(depth - 1, 1));
        float obstacleProbeRadius = Mathf.Max(0.01f, Mathf.Min(voxelSize.x, Mathf.Min(voxelSize.y, voxelSize.z)) * 0.25f);
        float fallbackDistance = Mathf.Max(clampedWorldSize.magnitude, 1.0f);
        float[] distances = new float[width * height * depth];

        CrowdVatEnvironmentDistanceQueryRequest request = new CrowdVatEnvironmentDistanceQueryRequest
        {
            queryId = 0,
            queryType = CrowdVatEnvironmentDistanceQueryType.SampleCombinedSignedDistance,
            gradientSampleDistance = 0.0f
        };

        using (ObstacleDistanceProbe obstacleProbe = obstacleColliders.Length > 0
                   ? new ObstacleDistanceProbe(obstacleProbeRadius)
                   : null)
        {
            for (int z = 0; z < depth; z++)
            {
                float w = depth <= 1 ? 0.0f : z / (float)(depth - 1);
                float progress = (z + 1) / (float)depth;
                EditorUtility.DisplayProgressBar("烘焙统一环境距离场", $"正在写入 Z Slice {z + 1}/{depth}", progress);

                for (int y = 0; y < height; y++)
                {
                    float v = height <= 1 ? 0.0f : y / (float)(height - 1);
                    for (int x = 0; x < width; x++)
                    {
                        float u = width <= 1 ? 0.0f : x / (float)(width - 1);
                        request.worldPosition = worldMin + Vector3.Scale(clampedWorldSize, new Vector3(u, v, w));

                        bool hasSignedDistance = false;
                        float signedDistance = fallbackDistance;
                        if (CrowdVatSceneQueryUtility.TrySampleEnvironmentDistance(
                                sourceDescriptor,
                                request,
                                out CrowdVatEnvironmentDistanceQueryResult result) &&
                            result.valid)
                        {
                            signedDistance = result.combinedSignedDistance;
                            hasSignedDistance = true;
                        }

                        if (TrySampleObstacleColliderDistance(
                                obstacleColliders,
                                obstacleProbe,
                                request.worldPosition,
                                out float obstacleSignedDistance))
                        {
                            signedDistance = hasSignedDistance
                                ? Mathf.Min(signedDistance, obstacleSignedDistance)
                                : obstacleSignedDistance;
                            hasSignedDistance = true;
                        }

                        if (!hasSignedDistance)
                            signedDistance = fallbackDistance;

                        distances[x + y * width + z * width * height] = signedDistance;
                    }
                }
            }
        }

        Texture3D texture = new Texture3D(width, height, depth, BakedEnvironmentTextureFormat, false)
        {
            name = $"{assetName}_BakedEnvironmentDistanceField",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixelData(distances, 0);
        texture.Apply(false, false);
        return texture;
    }

    public static Vector3Int BuildHighPrecisionResolution(Vector3 worldSize)
    {
        return new Vector3Int(
            BuildResolutionAxis(worldSize.x, HighPrecisionHorizontalVoxelSize, MinHighPrecisionHorizontalResolution, MaxHighPrecisionHorizontalResolution),
            BuildResolutionAxis(worldSize.y, HighPrecisionVerticalVoxelSize, MinHighPrecisionVerticalResolution, MaxHighPrecisionVerticalResolution),
            BuildResolutionAxis(worldSize.z, HighPrecisionHorizontalVoxelSize, MinHighPrecisionHorizontalResolution, MaxHighPrecisionHorizontalResolution));
    }

    private static int BuildResolutionAxis(float worldAxisSize, float targetVoxelSize, int minResolution, int maxResolution)
    {
        float safeWorldAxisSize = Mathf.Max(0.01f, worldAxisSize);
        float safeTargetVoxelSize = Mathf.Max(0.01f, targetVoxelSize);
        int unclampedResolution = Mathf.CeilToInt(safeWorldAxisSize / safeTargetVoxelSize) + 1;
        return Mathf.Clamp(unclampedResolution, minResolution, maxResolution);
    }

    private static void LogBakePrecision(Vector3 worldSize, Vector3Int resolution, Texture3D bakedTexture)
    {
        Vector3 voxelSize = new Vector3(
            worldSize.x / Mathf.Max(resolution.x - 1, 1),
            worldSize.y / Mathf.Max(resolution.y - 1, 1),
            worldSize.z / Mathf.Max(resolution.z - 1, 1));
        float dataSizeMb = bakedTexture != null ? bakedTexture.width * bakedTexture.height * bakedTexture.depth * 4.0f / (1024.0f * 1024.0f) : 0.0f;
        Debug.Log(
            $"Crowd environment SDF baked: resolution={resolution.x}x{resolution.y}x{resolution.z}, " +
            $"voxelSize=({voxelSize.x:F3}, {voxelSize.y:F3}, {voxelSize.z:F3})m, format={BakedEnvironmentTextureFormat}, rawData~{dataSizeMb:F1}MB.");
    }

    private static Collider[] ResolveObstacleColliders(CrowdVatIndirectRenderer renderer)
    {
        if (renderer == null)
            return Array.Empty<Collider>();

        SerializedObject serializedRenderer = new SerializedObject(renderer);
        SerializedProperty obstacleCollidersProperty = serializedRenderer.FindProperty("_environmentSdfObstacleColliders");
        if (obstacleCollidersProperty == null || !obstacleCollidersProperty.isArray || obstacleCollidersProperty.arraySize <= 0)
            return Array.Empty<Collider>();

        List<Collider> colliders = new List<Collider>(obstacleCollidersProperty.arraySize);
        for (int index = 0; index < obstacleCollidersProperty.arraySize; index++)
        {
            Collider collider = obstacleCollidersProperty.GetArrayElementAtIndex(index).objectReferenceValue as Collider;
            if (collider != null)
                colliders.Add(collider);
        }

        return colliders.ToArray();
    }

    private static bool TrySampleObstacleColliderDistance(
        Collider[] obstacleColliders,
        ObstacleDistanceProbe obstacleProbe,
        Vector3 worldPosition,
        out float signedDistance)
    {
        signedDistance = float.PositiveInfinity;
        if (obstacleProbe == null || obstacleColliders == null || obstacleColliders.Length <= 0)
            return false;

        bool hasObstacle = false;
        for (int index = 0; index < obstacleColliders.Length; index++)
        {
            Collider obstacleCollider = obstacleColliders[index];
            if (obstacleCollider == null || !obstacleCollider.enabled || !obstacleCollider.gameObject.activeInHierarchy)
                continue;

            if (!obstacleProbe.TrySampleSignedDistance(obstacleCollider, worldPosition, out float obstacleSignedDistance))
                continue;

            signedDistance = hasObstacle
                ? Mathf.Min(signedDistance, obstacleSignedDistance)
                : obstacleSignedDistance;
            hasObstacle = true;
        }

        return hasObstacle;
    }

    private sealed class ObstacleDistanceProbe : IDisposable
    {
        private readonly GameObject _probeObject;
        private readonly SphereCollider _probeCollider;
        private readonly float _probeRadius;

        public ObstacleDistanceProbe(float probeRadius)
        {
            _probeRadius = Mathf.Max(0.005f, probeRadius);
            _probeObject = new GameObject("CrowdVatEnvironmentDistanceProbe")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _probeCollider = _probeObject.AddComponent<SphereCollider>();
            _probeCollider.hideFlags = HideFlags.HideAndDontSave;
            _probeCollider.isTrigger = true;
            _probeCollider.radius = _probeRadius;
        }

        public bool TrySampleSignedDistance(Collider obstacleCollider, Vector3 worldPosition, out float signedDistance)
        {
            signedDistance = float.PositiveInfinity;
            if (_probeCollider == null || obstacleCollider == null)
                return false;

            Vector3 closestPoint = obstacleCollider.ClosestPoint(worldPosition);
            signedDistance = Vector3.Distance(worldPosition, closestPoint);

            if (Physics.ComputePenetration(
                    _probeCollider,
                    worldPosition,
                    Quaternion.identity,
                    obstacleCollider,
                    obstacleCollider.transform.position,
                    obstacleCollider.transform.rotation,
                    out _,
                    out float separationDistance))
            {
                signedDistance = _probeRadius - separationDistance;
            }

            return true;
        }

        public void Dispose()
        {
            if (_probeObject == null)
                return;

            UnityEngine.Object.DestroyImmediate(_probeObject);
        }
    }
}

public static class CrowdVatSceneQueryFieldBatchBuilder
{
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SampleSceneFieldAssetPath = "Assets/Project/Crowds/VAT/Generated/CrowdSceneQueryField_SampleScene.asset";

    public static void BakeSampleSceneEnvironmentDistanceField()
    {
        Scene scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        CrowdVatIndirectRenderer renderer = FindFirstRenderer(scene);
        if (renderer == null)
            throw new InvalidOperationException($"场景 {SampleScenePath} 中没有找到 CrowdVatIndirectRenderer。");

        CrowdVatSceneQueryFieldAsset fieldAsset = EnsureSceneQueryFieldAsset(renderer, SampleSceneFieldAssetPath);
        ConfigureSampleSceneBakeVolume(renderer, fieldAsset);

        if (!CrowdVatSceneQueryFieldBaker.BakeEnvironmentDistanceField(renderer))
            throw new InvalidOperationException("统一环境距离场烘焙失败，请检查 terrain / obstacle 源是否有效。");

        EditorUtility.SetDirty(renderer);
        EditorUtility.SetDirty(fieldAsset);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    public static void DiagnoseSampleSceneEnvironmentHeightMismatch()
    {
        Scene scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        CrowdVatIndirectRenderer renderer = FindFirstRenderer(scene);
        if (renderer == null)
            throw new InvalidOperationException($"场景 {SampleScenePath} 中没有找到 CrowdVatIndirectRenderer。");

        if (!renderer.TryGetGroundQueryFieldDescriptor(out CrowdVatGroundFieldDescriptor groundDescriptor) ||
            groundDescriptor.terrain == null ||
            groundDescriptor.terrain.terrainData == null)
        {
            throw new InvalidOperationException("无法解析 Terrain，不能比较人群贴地高度。");
        }

        if (!renderer.TryGetEnvironmentDistanceFieldDescriptor(out CrowdVatEnvironmentDistanceFieldDescriptor environmentDescriptor))
            throw new InvalidOperationException("无法解析环境距离场描述符。");

        Terrain terrain = groundDescriptor.terrain;
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrainData.size;

        bool hasBakedField = environmentDescriptor.bakedField.sdfTexture != null;
        SerializedObject serializedRenderer = new SerializedObject(renderer);
        bool enableTerrainCollision = ReadSerializedBool(serializedRenderer, "_enableTerrainCollision", false);
        bool enableStaticSdfCollision = ReadSerializedBool(serializedRenderer, "_enableStaticSdfCollision", false);
        float terrainHeightOffset = ReadSerializedFloat(serializedRenderer, "_terrainHeightOffset", 0.0f);
        float collisionRadius = ReadSerializedFloat(serializedRenderer, "_collisionRadius", 0.0f);
        float collisionHeight = ReadSerializedFloat(serializedRenderer, "_collisionHeight", 0.0f);
        int capsuleSampleCount = ReadSerializedInt(serializedRenderer, "_capsulePbdSampleCount", 0);

        Vector2 spawnCenterXZ;
        Vector2 spawnSizeXZ;
        if (!renderer.TryGetDebugSpawnArea(out spawnCenterXZ, out spawnSizeXZ))
        {
            spawnCenterXZ = new Vector2(
                terrainPosition.x + terrainSize.x * 0.5f,
                terrainPosition.z + terrainSize.z * 0.5f);
            spawnSizeXZ = new Vector2(terrainSize.x, terrainSize.z);
        }

        int sampleAxisCount = 17;
        Vector2 spawnMinXZ = spawnCenterXZ - spawnSizeXZ * 0.5f;
        Vector2 spawnMaxXZ = spawnCenterXZ + spawnSizeXZ * 0.5f;
        Bounds spawnWorldBounds = BuildWorldXZBounds(renderer.transform, spawnMinXZ, spawnMaxXZ);
        int terrainSamples = 0;
        int terrainOutOfBoundsSamples = 0;
        int bakedGroundSamples = 0;
        int bakedGroundOutOfBoundsSamples = 0;
        int zeroFoundSamples = 0;
        int zeroMissingSamples = 0;

        float maxAbsZeroOffset = 0.0f;
        Vector3 worstZeroOffsetPoint = Vector3.zero;
        float worstZeroHeight = 0.0f;
        float maxAbsGroundSignedDistance = 0.0f;
        Vector3 worstGroundDistancePoint = Vector3.zero;
        float worstGroundSignedDistance = 0.0f;
        float maxAbsGpuTerrainHeightBias = 0.0f;
        Vector3 worstGpuTerrainHeightBiasPoint = Vector3.zero;
        float worstGpuTerrainHeightBias = 0.0f;
        float minTerrainHeight = float.PositiveInfinity;
        float maxTerrainHeight = float.NegativeInfinity;

        for (int zIndex = 0; zIndex < sampleAxisCount; zIndex++)
        {
            float zT = zIndex / (float)(sampleAxisCount - 1);
            float localZ = Mathf.Lerp(spawnMinXZ.y, spawnMaxXZ.y, zT);
            for (int xIndex = 0; xIndex < sampleAxisCount; xIndex++)
            {
                float xT = xIndex / (float)(sampleAxisCount - 1);
                float localX = Mathf.Lerp(spawnMinXZ.x, spawnMaxXZ.x, xT);
                Vector3 worldXZPoint = renderer.transform.TransformPoint(new Vector3(localX, 0.0f, localZ));
                float worldX = worldXZPoint.x;
                float worldZ = worldXZPoint.z;
                if (!IsInsideTerrainXZ(terrain, worldX, worldZ))
                {
                    terrainOutOfBoundsSamples++;
                    continue;
                }

                Vector3 terrainQueryPoint = new Vector3(worldX, terrainPosition.y, worldZ);
                float terrainRelativeHeight = terrain.SampleHeight(terrainQueryPoint);
                float terrainHeight = terrainRelativeHeight + terrainPosition.y + terrainHeightOffset;
                Vector3 terrainWorldPoint = new Vector3(worldX, terrainHeight, worldZ);
                terrainSamples++;
                minTerrainHeight = Mathf.Min(minTerrainHeight, terrainHeight);
                maxTerrainHeight = Mathf.Max(maxTerrainHeight, terrainHeight);

                float gpuEquivalentHeight = terrainPosition.y + terrainRelativeHeight + terrainHeightOffset;
                float gpuTerrainHeightBias = gpuEquivalentHeight - terrainHeight;
                if (Mathf.Abs(gpuTerrainHeightBias) > maxAbsGpuTerrainHeightBias)
                {
                    maxAbsGpuTerrainHeightBias = Mathf.Abs(gpuTerrainHeightBias);
                    worstGpuTerrainHeightBiasPoint = terrainWorldPoint;
                    worstGpuTerrainHeightBias = gpuTerrainHeightBias;
                }

                if (!hasBakedField)
                    continue;

                if (TrySampleBakedSignedDistance(environmentDescriptor.bakedField, terrainWorldPoint, out float groundSignedDistance))
                {
                    bakedGroundSamples++;
                    if (Mathf.Abs(groundSignedDistance) > maxAbsGroundSignedDistance)
                    {
                        maxAbsGroundSignedDistance = Mathf.Abs(groundSignedDistance);
                        worstGroundDistancePoint = terrainWorldPoint;
                        worstGroundSignedDistance = groundSignedDistance;
                    }
                }
                else
                {
                    bakedGroundOutOfBoundsSamples++;
                }

                if (TryFindNearestBakedZeroHeight(
                        environmentDescriptor.bakedField,
                        worldX,
                        worldZ,
                        terrainHeight,
                        out float zeroHeight))
                {
                    zeroFoundSamples++;
                    float zeroOffset = zeroHeight - terrainHeight;
                    if (Mathf.Abs(zeroOffset) > maxAbsZeroOffset)
                    {
                        maxAbsZeroOffset = Mathf.Abs(zeroOffset);
                        worstZeroOffsetPoint = terrainWorldPoint;
                        worstZeroHeight = zeroHeight;
                    }
                }
                else
                {
                    zeroMissingSamples++;
                }
            }
        }

        List<string> lines = new List<string>
        {
            "[CrowdVat SDF Diagnose] SampleScene 环境高度诊断",
            $"renderer={renderer.name}, terrain={terrain.name}",
            $"spawn localXZ center={spawnCenterXZ}, size={spawnSizeXZ}, worldBoundsCenter={spawnWorldBounds.center}, worldBoundsSize={spawnWorldBounds.size}, samples={sampleAxisCount}x{sampleAxisCount}",
            $"terrain collision setting={enableTerrainCollision}, runtime GPU Terrain grounding=False, static sdf enabled={enableStaticSdfCollision}, baked field resolved={hasBakedField}",
            $"runtime priority: 人物高度校正只由 baked/environment SDF 或 static SDF 胶囊约束负责；GPU Terrain heightmap 不参与贴地。",
            $"terrainHeightOffset={terrainHeightOffset:F4}, collisionRadius={collisionRadius:F4}, collisionHeight={collisionHeight:F4}, capsuleSamples={capsuleSampleCount}",
            $"terrain world min={terrainPosition}, size={terrainSize}, sampledHeightRange=({minTerrainHeight:F4}, {maxTerrainHeight:F4})",
            $"terrain samples={terrainSamples}, terrainOutOfBounds={terrainOutOfBoundsSamples}",
            $"GPU terrain formula diagnostic: shader 当前等效高度 = terrainPosition.y + normalizedHeight * terrainSize.y + offset；最大偏差={worstGpuTerrainHeightBias:F4}m at {worstGpuTerrainHeightBiasPoint}"
        };

        if (hasBakedField)
        {
            CrowdVatBakedEnvironmentDistanceFieldDescriptor baked = environmentDescriptor.bakedField;
            Texture3D texture = baked.sdfTexture;
            Vector3 bakedMin = baked.worldCenter - baked.worldSize * 0.5f;
            Vector3 bakedMax = baked.worldCenter + baked.worldSize * 0.5f;
            lines.Add($"baked texture={texture.name}, format={texture.format}, resolution={texture.width}x{texture.height}x{texture.depth}");
            lines.Add($"baked bounds min={bakedMin}, max={bakedMax}, scale={baked.distanceScale:F4}, bias={baked.distanceBias:F4}");
            lines.Add($"baked ground samples={bakedGroundSamples}, bakedGroundOutOfBounds={bakedGroundOutOfBoundsSamples}");
            lines.Add($"baked signed distance at Terrain+offset: maxAbs={maxAbsGroundSignedDistance:F4}m, worst={worstGroundSignedDistance:F4}m at {worstGroundDistancePoint}");
            lines.Add($"baked nearest zero height: found={zeroFoundSamples}, missing={zeroMissingSamples}, maxAbsOffset={maxAbsZeroOffset:F4}m, zeroHeight={worstZeroHeight:F4}, terrainPoint={worstZeroOffsetPoint}");
        }

        Debug.Log(string.Join("\n", lines));
    }

    private static CrowdVatIndirectRenderer FindFirstRenderer(Scene scene)
    {
        if (!scene.IsValid())
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            CrowdVatIndirectRenderer renderer = roots[rootIndex].GetComponentInChildren<CrowdVatIndirectRenderer>(true);
            if (renderer != null)
                return renderer;
        }

        return null;
    }

    private static bool ReadSerializedBool(SerializedObject serializedObject, string propertyName, bool fallback)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.boolValue : fallback;
    }

    private static float ReadSerializedFloat(SerializedObject serializedObject, string propertyName, float fallback)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.floatValue : fallback;
    }

    private static int ReadSerializedInt(SerializedObject serializedObject, string propertyName, int fallback)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.intValue : fallback;
    }

    private static bool IsInsideTerrainXZ(Terrain terrain, float worldX, float worldZ)
    {
        if (terrain == null || terrain.terrainData == null)
            return false;

        Vector3 terrainMin = terrain.transform.position;
        Vector3 terrainMax = terrainMin + terrain.terrainData.size;
        return worldX >= terrainMin.x &&
            worldX <= terrainMax.x &&
            worldZ >= terrainMin.z &&
            worldZ <= terrainMax.z;
    }

    private static bool TryFindNearestBakedZeroHeight(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        float worldX,
        float worldZ,
        float referenceHeight,
        out float zeroHeight)
    {
        zeroHeight = referenceHeight;
        if (descriptor.sdfTexture == null)
            return false;

        Vector3 fieldMin = descriptor.worldCenter - descriptor.worldSize * 0.5f;
        Vector3 fieldMax = descriptor.worldCenter + descriptor.worldSize * 0.5f;
        if (worldX < fieldMin.x || worldX > fieldMax.x || worldZ < fieldMin.z || worldZ > fieldMax.z)
            return false;

        int sampleCount = Mathf.Clamp(descriptor.sdfTexture.height * 2, 32, 512);
        bool hasPrevious = false;
        float previousHeight = fieldMin.y;
        float previousDistance = 0.0f;
        bool foundZero = false;
        float bestAbsOffset = float.PositiveInfinity;

        for (int index = 0; index < sampleCount; index++)
        {
            float t = sampleCount <= 1 ? 0.0f : index / (float)(sampleCount - 1);
            float currentHeight = Mathf.Lerp(fieldMin.y, fieldMax.y, t);
            Vector3 samplePoint = new Vector3(worldX, currentHeight, worldZ);
            if (!TrySampleBakedSignedDistance(descriptor, samplePoint, out float currentDistance))
                continue;

            if (Mathf.Abs(currentDistance) <= 1e-4f)
            {
                float absOffset = Mathf.Abs(currentHeight - referenceHeight);
                if (absOffset < bestAbsOffset)
                {
                    bestAbsOffset = absOffset;
                    zeroHeight = currentHeight;
                    foundZero = true;
                }
            }

            if (hasPrevious && HasSignCrossing(previousDistance, currentDistance))
            {
                float crossingT = previousDistance / Mathf.Max(previousDistance - currentDistance, 1e-6f);
                float crossingHeight = Mathf.Lerp(previousHeight, currentHeight, Mathf.Clamp01(crossingT));
                float absOffset = Mathf.Abs(crossingHeight - referenceHeight);
                if (absOffset < bestAbsOffset)
                {
                    bestAbsOffset = absOffset;
                    zeroHeight = crossingHeight;
                    foundZero = true;
                }
            }

            previousHeight = currentHeight;
            previousDistance = currentDistance;
            hasPrevious = true;
        }

        return foundZero;
    }

    private static bool HasSignCrossing(float a, float b)
    {
        return (a < 0.0f && b > 0.0f) || (a > 0.0f && b < 0.0f);
    }

    private static bool TrySampleBakedSignedDistance(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 worldPosition,
        out float signedDistance)
    {
        signedDistance = float.PositiveInfinity;
        if (descriptor.sdfTexture == null)
            return false;

        if (!TryWorldToBakedUv(descriptor, worldPosition, out Vector3 uvw))
            return false;

        Color sample = SampleTexture3DTrilinear(descriptor.sdfTexture, uvw);
        signedDistance = sample.r * descriptor.distanceScale + descriptor.distanceBias;
        return true;
    }

    private static bool TryWorldToBakedUv(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 worldPosition,
        out Vector3 uvw)
    {
        uvw = Vector3.zero;
        if (descriptor.sdfTexture == null ||
            descriptor.worldSize.x <= 0.0f ||
            descriptor.worldSize.y <= 0.0f ||
            descriptor.worldSize.z <= 0.0f)
        {
            return false;
        }

        Vector3 fieldMin = descriptor.worldCenter - descriptor.worldSize * 0.5f;
        uvw = new Vector3(
            (worldPosition.x - fieldMin.x) / descriptor.worldSize.x,
            (worldPosition.y - fieldMin.y) / descriptor.worldSize.y,
            (worldPosition.z - fieldMin.z) / descriptor.worldSize.z);
        return uvw.x >= 0.0f && uvw.x <= 1.0f &&
            uvw.y >= 0.0f && uvw.y <= 1.0f &&
            uvw.z >= 0.0f && uvw.z <= 1.0f;
    }

    private static Color SampleTexture3DTrilinear(Texture3D texture, Vector3 uvw)
    {
        float x = Mathf.Clamp01(uvw.x) * Mathf.Max(texture.width - 1, 0);
        float y = Mathf.Clamp01(uvw.y) * Mathf.Max(texture.height - 1, 0);
        float z = Mathf.Clamp01(uvw.z) * Mathf.Max(texture.depth - 1, 0);

        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, texture.width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, texture.height - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, texture.depth - 1);
        int x1 = Mathf.Min(x0 + 1, texture.width - 1);
        int y1 = Mathf.Min(y0 + 1, texture.height - 1);
        int z1 = Mathf.Min(z0 + 1, texture.depth - 1);

        float tx = x - x0;
        float ty = y - y0;
        float tz = z - z0;

        Color c000 = texture.GetPixel(x0, y0, z0);
        Color c100 = texture.GetPixel(x1, y0, z0);
        Color c010 = texture.GetPixel(x0, y1, z0);
        Color c110 = texture.GetPixel(x1, y1, z0);
        Color c001 = texture.GetPixel(x0, y0, z1);
        Color c101 = texture.GetPixel(x1, y0, z1);
        Color c011 = texture.GetPixel(x0, y1, z1);
        Color c111 = texture.GetPixel(x1, y1, z1);

        Color c00 = Color.LerpUnclamped(c000, c100, tx);
        Color c10 = Color.LerpUnclamped(c010, c110, tx);
        Color c01 = Color.LerpUnclamped(c001, c101, tx);
        Color c11 = Color.LerpUnclamped(c011, c111, tx);
        Color c0 = Color.LerpUnclamped(c00, c10, ty);
        Color c1 = Color.LerpUnclamped(c01, c11, ty);
        return Color.LerpUnclamped(c0, c1, tz);
    }

    private static CrowdVatSceneQueryFieldAsset EnsureSceneQueryFieldAsset(CrowdVatIndirectRenderer renderer, string assetPath)
    {
        CrowdVatSceneQueryFieldAsset fieldAsset = renderer.SceneQueryFieldAsset;
        if (fieldAsset == null)
        {
            EnsureAssetDirectory(assetPath);
            fieldAsset = AssetDatabase.LoadAssetAtPath<CrowdVatSceneQueryFieldAsset>(assetPath);
            if (fieldAsset == null)
            {
                fieldAsset = ScriptableObject.CreateInstance<CrowdVatSceneQueryFieldAsset>();
                AssetDatabase.CreateAsset(fieldAsset, assetPath);
            }

            SerializedObject serializedRenderer = new SerializedObject(renderer);
            SerializedProperty fieldAssetProperty = serializedRenderer.FindProperty("_sceneQueryFieldAsset");
            if (fieldAssetProperty == null)
                throw new InvalidOperationException("找不到 _sceneQueryFieldAsset，无法把环境距离场资产绑定回 renderer。");

            fieldAssetProperty.objectReferenceValue = fieldAsset;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        }

        return fieldAsset;
    }

    private static void ConfigureSampleSceneBakeVolume(CrowdVatIndirectRenderer renderer, CrowdVatSceneQueryFieldAsset fieldAsset)
    {
        Bounds suggestedBounds = BuildSuggestedBakeBounds(renderer);
        Vector3Int suggestedResolution = CrowdVatSceneQueryFieldBaker.BuildHighPrecisionResolution(suggestedBounds.size);

        SerializedObject serializedFieldAsset = new SerializedObject(fieldAsset);
        serializedFieldAsset.FindProperty("_preferBakedEnvironmentDistanceField").boolValue = true;
        serializedFieldAsset.FindProperty("_bakedEnvironmentDistanceWorldCenter").vector3Value = suggestedBounds.center;
        serializedFieldAsset.FindProperty("_bakedEnvironmentDistanceWorldSize").vector3Value = suggestedBounds.size;
        serializedFieldAsset.FindProperty("_bakedEnvironmentDistanceResolution").vector3IntValue = suggestedResolution;
        serializedFieldAsset.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Bounds BuildSuggestedBakeBounds(CrowdVatIndirectRenderer renderer)
    {
        bool hasBounds = false;
        Bounds horizontalBounds = default;
        Terrain terrain = null;

        if (renderer.TryGetGroundQueryFieldDescriptor(out CrowdVatGroundFieldDescriptor groundDescriptor) &&
            groundDescriptor.terrain != null &&
            groundDescriptor.terrain.terrainData != null)
        {
            terrain = groundDescriptor.terrain;
            Vector3 terrainSize = terrain.terrainData.size;
            Vector3 terrainPosition = terrain.transform.position;
            horizontalBounds = new Bounds(
                new Vector3(terrainPosition.x + terrainSize.x * 0.5f, renderer.transform.position.y, terrainPosition.z + terrainSize.z * 0.5f),
                new Vector3(terrainSize.x, 1.0f, terrainSize.z));
            hasBounds = true;
        }

        if (renderer.TryGetDebugSpawnArea(out Vector2 centerXZ, out Vector2 sizeXZ))
        {
            Vector2 halfSizeXZ = sizeXZ * 0.5f;
            Bounds spawnBounds = BuildWorldXZBounds(
                renderer.transform,
                centerXZ - halfSizeXZ,
                centerXZ + halfSizeXZ);
            if (hasBounds)
                horizontalBounds.Encapsulate(spawnBounds);
            else
            {
                horizontalBounds = spawnBounds;
                hasBounds = true;
            }
        }

        Collider[] obstacleColliders = ResolveObstacleCollidersFromRenderer(renderer);
        for (int index = 0; index < obstacleColliders.Length; index++)
        {
            Collider obstacleCollider = obstacleColliders[index];
            if (obstacleCollider == null || !obstacleCollider.enabled)
                continue;

            Bounds obstacleBounds = obstacleCollider.bounds;
            Bounds obstacleHorizontalBounds = new Bounds(
                new Vector3(obstacleBounds.center.x, renderer.transform.position.y, obstacleBounds.center.z),
                new Vector3(Mathf.Max(0.01f, obstacleBounds.size.x), 1.0f, Mathf.Max(0.01f, obstacleBounds.size.z)));
            if (hasBounds)
                horizontalBounds.Encapsulate(obstacleHorizontalBounds);
            else
            {
                horizontalBounds = obstacleHorizontalBounds;
                hasBounds = true;
            }
        }

        if (!hasBounds)
            horizontalBounds = new Bounds(renderer.transform.position + Vector3.up, new Vector3(64.0f, 1.0f, 64.0f));

        float minY = renderer.transform.position.y - 1.0f;
        float maxY = renderer.transform.position.y + 5.0f;
        bool hasVerticalRange = false;
        if (terrain != null && TrySampleTerrainHeightRange(terrain, horizontalBounds, 9, out float terrainMinHeight, out float terrainMaxHeight))
        {
            minY = terrainMinHeight - 2.0f;
            maxY = terrainMaxHeight + 6.0f;
            hasVerticalRange = true;
        }

        for (int index = 0; index < obstacleColliders.Length; index++)
        {
            Collider obstacleCollider = obstacleColliders[index];
            if (obstacleCollider == null || !obstacleCollider.enabled)
                continue;

            Bounds obstacleBounds = obstacleCollider.bounds;
            minY = hasVerticalRange
                ? Mathf.Min(minY, obstacleBounds.min.y - 1.0f)
                : obstacleBounds.min.y - 1.0f;
            maxY = hasVerticalRange
                ? Mathf.Max(maxY, obstacleBounds.max.y + 2.0f)
                : obstacleBounds.max.y + 2.0f;
            hasVerticalRange = true;
        }

        Vector3 horizontalSize = horizontalBounds.size;
        Vector3 horizontalCenter = horizontalBounds.center;
        Bounds bounds = new Bounds(
            new Vector3(horizontalCenter.x, (minY + maxY) * 0.5f, horizontalCenter.z),
            new Vector3(horizontalSize.x, Mathf.Max(6.0f, maxY - minY), horizontalSize.z));
        bounds.Expand(new Vector3(2.0f, 0.0f, 2.0f));
        Vector3 clampedSize = bounds.size;
        clampedSize.x = Mathf.Max(8.0f, clampedSize.x);
        clampedSize.y = Mathf.Max(6.0f, clampedSize.y);
        clampedSize.z = Mathf.Max(8.0f, clampedSize.z);
        bounds.size = clampedSize;
        return bounds;
    }

    private static Bounds BuildWorldXZBounds(
        Transform rendererTransform,
        Vector2 minXZ,
        Vector2 maxXZ)
    {
        Vector3[] corners =
        {
            rendererTransform.TransformPoint(new Vector3(minXZ.x, 0.0f, minXZ.y)),
            rendererTransform.TransformPoint(new Vector3(minXZ.x, 0.0f, maxXZ.y)),
            rendererTransform.TransformPoint(new Vector3(maxXZ.x, 0.0f, minXZ.y)),
            rendererTransform.TransformPoint(new Vector3(maxXZ.x, 0.0f, maxXZ.y))
        };

        Bounds bounds = new Bounds(new Vector3(corners[0].x, rendererTransform.position.y, corners[0].z), Vector3.zero);
        for (int index = 0; index < corners.Length; index++)
            bounds.Encapsulate(new Vector3(corners[index].x, rendererTransform.position.y, corners[index].z));

        return bounds;
    }

    private static bool TrySampleTerrainHeightRange(Terrain terrain, Bounds horizontalBounds, int samplesPerAxis, out float minHeight, out float maxHeight)
    {
        minHeight = float.PositiveInfinity;
        maxHeight = float.NegativeInfinity;
        if (terrain == null || terrain.terrainData == null)
            return false;

        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;
        int clampedSamples = Mathf.Max(2, samplesPerAxis);
        for (int sampleZ = 0; sampleZ < clampedSamples; sampleZ++)
        {
            float normalizedZ = sampleZ / (float)(clampedSamples - 1);
            float worldZ = Mathf.Lerp(horizontalBounds.min.z, horizontalBounds.max.z, normalizedZ);
            for (int sampleX = 0; sampleX < clampedSamples; sampleX++)
            {
                float normalizedX = sampleX / (float)(clampedSamples - 1);
                float worldX = Mathf.Lerp(horizontalBounds.min.x, horizontalBounds.max.x, normalizedX);
                if (worldX < terrainPosition.x ||
                    worldX > terrainPosition.x + terrainSize.x ||
                    worldZ < terrainPosition.z ||
                    worldZ > terrainPosition.z + terrainSize.z)
                {
                    continue;
                }

                float sampledHeight = terrain.SampleHeight(new Vector3(worldX, terrainPosition.y, worldZ)) + terrainPosition.y;
                minHeight = Mathf.Min(minHeight, sampledHeight);
                maxHeight = Mathf.Max(maxHeight, sampledHeight);
            }
        }

        return float.IsFinite(minHeight) && float.IsFinite(maxHeight);
    }

    private static Collider[] ResolveObstacleCollidersFromRenderer(CrowdVatIndirectRenderer renderer)
    {
        if (renderer == null)
            return Array.Empty<Collider>();

        SerializedObject serializedRenderer = new SerializedObject(renderer);
        SerializedProperty obstacleCollidersProperty = serializedRenderer.FindProperty("_environmentSdfObstacleColliders");
        if (obstacleCollidersProperty == null || !obstacleCollidersProperty.isArray || obstacleCollidersProperty.arraySize <= 0)
            return Array.Empty<Collider>();

        List<Collider> colliders = new List<Collider>(obstacleCollidersProperty.arraySize);
        for (int index = 0; index < obstacleCollidersProperty.arraySize; index++)
        {
            Collider collider = obstacleCollidersProperty.GetArrayElementAtIndex(index).objectReferenceValue as Collider;
            if (collider != null)
                colliders.Add(collider);
        }

        return colliders.ToArray();
    }

    private static void EnsureAssetDirectory(string assetPath)
    {
        string directoryPath = Path.GetDirectoryName(assetPath);
        if (string.IsNullOrEmpty(directoryPath) || AssetDatabase.IsValidFolder(directoryPath))
            return;

        directoryPath = directoryPath.Replace('\\', '/');
        string[] folders = directoryPath.Split('/');
        string currentPath = folders[0];
        for (int index = 1; index < folders.Length; index++)
        {
            string nextPath = $"{currentPath}/{folders[index]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
                AssetDatabase.CreateFolder(currentPath, folders[index]);

            currentPath = nextPath;
        }
    }
}
