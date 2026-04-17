using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class CrowdVatGlobalGizmoSystem
{
    private const string PrefKeyPrefix = "Qianxia.CrowdVatGlobalGizmo.";
    private static readonly Color TotalAreaColor = new Color(1.0f, 1.0f, 1.0f, 0.85f);
    private static readonly Color CampAColor = new Color(0.92f, 0.35f, 0.26f, 0.95f);
    private static readonly Color CampBColor = new Color(0.18f, 0.66f, 0.95f, 0.95f);
    private static readonly Color LinkColor = new Color(1.0f, 0.92f, 0.24f, 0.92f);
    private static readonly Color GridColor = new Color(0.26f, 1.0f, 0.62f, 0.9f);
    private static readonly Color ActiveBubbleColor = new Color(0.18f, 0.92f, 1.0f, 0.9f);
    private static readonly Color QuerySphereColor = new Color(1.0f, 0.7f, 0.18f, 0.95f);
    private static readonly Color QueryCapsuleColor = new Color(1.0f, 0.28f, 0.8f, 0.95f);
    private static readonly List<CrowdVatIndirectRenderer> RendererCache = new List<CrowdVatIndirectRenderer>(8);
    private static readonly HashSet<CrowdVatIndirectRenderer> RendererSet = new HashSet<CrowdVatIndirectRenderer>();
    private static readonly List<CrowdVatSpatialQueryRequest> SpatialQueryCache = new List<CrowdVatSpatialQueryRequest>(32);

    public static bool SuppressSelectedFactionGizmos => GlobalEnabled && (ShowSpawnArea || ShowFactionAreas);

    private static bool GlobalEnabled
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "Enabled", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "Enabled", value);
    }

    private static bool OverlayExpanded
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "OverlayExpanded", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "OverlayExpanded", value);
    }

    private static bool OnlySelectedCrowd
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "OnlySelectedCrowd", false);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "OnlySelectedCrowd", value);
    }

    private static bool ShowSpawnArea
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowSpawnArea", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowSpawnArea", value);
    }

    private static bool ShowFactionAreas
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowFactionAreas", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowFactionAreas", value);
    }

    private static bool ShowGridBounds
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowGridBounds", false);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowGridBounds", value);
    }

    private static bool ShowActiveBubble
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowActiveBubble", false);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowActiveBubble", value);
    }

    private static bool ShowSpatialQueries
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowSpatialQueries", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowSpatialQueries", value);
    }

    private static bool ShowLabels
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowLabels", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowLabels", value);
    }

    static CrowdVatGlobalGizmoSystem()
    {
        SceneView.duringSceneGui += OnSceneGui;
        EditorApplication.playModeStateChanged += _ => SceneView.RepaintAll();
        EditorApplication.hierarchyChanged += SceneView.RepaintAll;
    }

    [MenuItem("Tools/Qianxia/Crowd/\u5207\u6362\u5168\u5C40 Gizmos")]
    private static void ToggleGlobalGizmosMenu()
    {
        GlobalEnabled = !GlobalEnabled;
        EnsureAllSceneViewGizmosEnabled();
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Qianxia/Crowd/\u5207\u6362\u5168\u5C40 Gizmos", true)]
    private static bool ToggleGlobalGizmosMenuValidate()
    {
        Menu.SetChecked("Tools/Qianxia/Crowd/\u5207\u6362\u5168\u5C40 Gizmos", GlobalEnabled);
        return true;
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        if (sceneView == null)
            return;

        EnsureAllSceneViewGizmosEnabled();
        DrawOverlay(sceneView);
        if (!GlobalEnabled)
            return;

        GatherTargetRenderers();
        if (RendererCache.Count <= 0)
            return;

        CompareFunction previousZTest = Handles.zTest;
        Color previousColor = Handles.color;
        Handles.zTest = CompareFunction.Always;
        try
        {
            foreach (CrowdVatIndirectRenderer renderer in RendererCache)
            {
                if (renderer == null)
                    continue;

                DrawRendererGizmos(renderer);
            }
        }
        finally
        {
            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }
    }
    private static void DrawOverlay(SceneView sceneView)
    {
        Handles.BeginGUI();
        float panelWidth = 268.0f;
        float panelHeight = OverlayExpanded ? 252.0f : 70.0f;
        Rect panelRect = new Rect(sceneView.position.width - panelWidth - 24.0f, 16.0f, panelWidth, panelHeight);
        GUILayout.BeginArea(panelRect, EditorStyles.helpBox);

        bool settingsChanged = false;
        bool nextExpanded = EditorGUILayout.Foldout(OverlayExpanded, "Crowd \u5168\u5C40 Gizmos", true);
        if (nextExpanded != OverlayExpanded)
        {
            OverlayExpanded = nextExpanded;
            settingsChanged = true;
        }

        if (OverlayExpanded)
        {
            bool nextGlobalEnabled = EditorGUILayout.ToggleLeft("\u542F\u7528\u5168\u5C40\u663E\u793A", GlobalEnabled);
            if (nextGlobalEnabled != GlobalEnabled)
            {
                GlobalEnabled = nextGlobalEnabled;
                settingsChanged = true;
            }

            EditorGUI.BeginDisabledGroup(!GlobalEnabled);
            settingsChanged |= DrawToggleLeft("\u4EC5\u663E\u793A\u9009\u4E2D Crowd", OnlySelectedCrowd, value => OnlySelectedCrowd = value);
            settingsChanged |= DrawToggleLeft("\u663E\u793A\u603B\u51FA\u751F\u8303\u56F4", ShowSpawnArea, value => ShowSpawnArea = value);
            settingsChanged |= DrawToggleLeft("\u663E\u793A\u53CC\u65B9\u9635\u8425\u8303\u56F4", ShowFactionAreas, value => ShowFactionAreas = value);
            settingsChanged |= DrawToggleLeft("\u663E\u793A\u7A7A\u95F4\u7F51\u683C\u8303\u56F4", ShowGridBounds, value => ShowGridBounds = value);
            settingsChanged |= DrawToggleLeft("\u663E\u793A Active Bubble", ShowActiveBubble, value => ShowActiveBubble = value);
            settingsChanged |= DrawToggleLeft("\u663E\u793A\u7A7A\u95F4\u67E5\u8BE2\u4F53", ShowSpatialQueries, value => ShowSpatialQueries = value);
            settingsChanged |= DrawToggleLeft("\u663E\u793A\u6587\u5B57\u6807\u7B7E", ShowLabels, value => ShowLabels = value);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4.0f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("\u5168\u90E8\u5F00\u542F"))
                {
                    SetAllVisualizationToggles(true);
                    settingsChanged = true;
                }

                if (GUILayout.Button("\u4EC5\u663E\u793A\u9635\u8425"))
                {
                    ShowSpawnArea = true;
                    ShowFactionAreas = true;
                    ShowGridBounds = false;
                    ShowActiveBubble = false;
                    ShowSpatialQueries = false;
                    ShowLabels = true;
                    settingsChanged = true;
                }

                if (GUILayout.Button("\u5237\u65B0"))
                {
                    settingsChanged = true;
                }
            }

            EditorGUILayout.HelpBox("\u5168\u5C40\u663E\u793A\u5F00\u542F\u540E\uFF0C\u4E0D\u9700\u8981\u9009\u4E2D QianxiaCrowdIndirect\uFF0C\u4E5F\u80FD\u5728 Scene \u91CC\u6301\u7EED\u770B\u5230\u5F53\u524D crowd \u7684\u8C03\u8BD5\u8303\u56F4\u3002", MessageType.None);
        }
        else
        {
            GUILayout.Label(GlobalEnabled ? "\u5168\u5C40\u663E\u793A\u5DF2\u5F00\u542F" : "\u5168\u5C40\u663E\u793A\u5DF2\u5173\u95ED");
        }

        GUILayout.EndArea();
        Handles.EndGUI();

        if (!settingsChanged)
            return;

        EnsureAllSceneViewGizmosEnabled();
        SceneView.RepaintAll();
    }

    private static bool DrawToggleLeft(string label, bool currentValue, System.Action<bool> setter)
    {
        bool nextValue = EditorGUILayout.ToggleLeft(label, currentValue);
        if (nextValue == currentValue)
            return false;

        setter(nextValue);
        return true;
    }

    private static void SetAllVisualizationToggles(bool enabled)
    {
        ShowSpawnArea = enabled;
        ShowFactionAreas = enabled;
        ShowGridBounds = enabled;
        ShowActiveBubble = enabled;
        ShowSpatialQueries = enabled;
        ShowLabels = enabled;
    }

    private static void EnsureAllSceneViewGizmosEnabled()
    {
        foreach (SceneView view in SceneView.sceneViews)
        {
            if (view == null)
                continue;

            view.drawGizmos = true;
        }

        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.drawGizmos = true;
    }

    private static void GatherTargetRenderers()
    {
        RendererCache.Clear();
        RendererSet.Clear();

        if (OnlySelectedCrowd)
        {
            foreach (Transform selectedTransform in Selection.transforms)
            {
                if (selectedTransform == null)
                    continue;

                CrowdVatIndirectRenderer renderer = selectedTransform.GetComponentInParent<CrowdVatIndirectRenderer>();
                if (renderer == null || !RendererSet.Add(renderer))
                    continue;

                RendererCache.Add(renderer);
            }

            return;
        }

        CrowdVatIndirectRenderer[] renderers = Object.FindObjectsByType<CrowdVatIndirectRenderer>(FindObjectsSortMode.None);
        foreach (CrowdVatIndirectRenderer renderer in renderers)
        {
            if (renderer == null || !RendererSet.Add(renderer))
                continue;

            RendererCache.Add(renderer);
        }
    }

    private static void DrawRendererGizmos(CrowdVatIndirectRenderer renderer)
    {
        SerializedObject serializedRenderer = new SerializedObject(renderer);
        Terrain terrain = ResolveTerrain(serializedRenderer, renderer);
        float handleHeightOffset = ResolveHandleHeightOffset(serializedRenderer);
        float areaHeightOffset = Mathf.Max(0.08f, handleHeightOffset * 0.08f);

        if (ShowSpawnArea && renderer.TryGetDebugSpawnArea(out Vector2 spawnCenterXZ, out Vector2 spawnSizeXZ))
        {
            DrawRectByCenterAndSize(
                renderer.transform,
                terrain,
                spawnCenterXZ,
                spawnSizeXZ,
                TotalAreaColor,
                areaHeightOffset * 0.65f,
                ShowLabels ? $"{renderer.name} 鎬诲嚭鐢熻寖鍥碶n{spawnSizeXZ.x:F1} x {spawnSizeXZ.y:F1}" : null);
        }

        if (ShowFactionAreas && renderer.FactionLayoutMode == CrowdVatFactionLayoutMode.TwoOpposingFactions)
        {
            if (renderer.TryGetDebugFactionArea(CrowdVatFaction.CampA, out Vector2 campACenterXZ, out Vector2 campASizeXZ))
            {
                DrawRectByCenterAndSize(
                    renderer.transform,
                    terrain,
                    campACenterXZ,
                    campASizeXZ,
                    CampAColor,
                    areaHeightOffset,
                    ShowLabels ? "CampA 鑼冨洿" : null);

                Vector3 campAWorld = BuildWorldScenePoint(renderer.transform, terrain, campACenterXZ, handleHeightOffset);
                Handles.color = CampAColor;
                Handles.SphereHandleCap(0, campAWorld, Quaternion.identity, Mathf.Max(0.35f, handleHeightOffset * 0.22f), EventType.Repaint);
            }

            if (renderer.TryGetDebugFactionArea(CrowdVatFaction.CampB, out Vector2 campBCenterXZ, out Vector2 campBSizeXZ))
            {
                DrawRectByCenterAndSize(
                    renderer.transform,
                    terrain,
                    campBCenterXZ,
                    campBSizeXZ,
                    CampBColor,
                    areaHeightOffset,
                    ShowLabels ? "CampB 鑼冨洿" : null);

                Vector3 campBWorld = BuildWorldScenePoint(renderer.transform, terrain, campBCenterXZ, handleHeightOffset);
                Handles.color = CampBColor;
                Handles.SphereHandleCap(0, campBWorld, Quaternion.identity, Mathf.Max(0.35f, handleHeightOffset * 0.22f), EventType.Repaint);

                if (renderer.TryGetDebugFactionArea(CrowdVatFaction.CampA, out Vector2 campAForLink, out _))
                {
                    Handles.color = LinkColor;
                    Handles.DrawDottedLine(
                        BuildWorldScenePoint(renderer.transform, terrain, campAForLink, handleHeightOffset),
                        campBWorld,
                        6.0f);
                }
            }
        }

        if (ShowGridBounds && renderer.TryGetDebugGridBounds(out Vector2 gridMinXZ, out Vector2 gridMaxXZ, out Vector2Int gridDimensions, out float gridCellSize))
        {
            DrawGridByMinMax(
                renderer.transform,
                terrain,
                gridMinXZ,
                gridMaxXZ,
                gridDimensions,
                gridCellSize,
                GridColor,
                areaHeightOffset * 0.55f,
                ShowLabels ? $"{renderer.name} \u7A7A\u95F4\u7F51\u683C\n{gridDimensions.x} x {gridDimensions.y}  cell={gridCellSize:F2}" : null);
        }

        if (ShowActiveBubble && renderer.TryGetDebugActiveBubble(out Vector3 activeBubbleWorldCenter, out float activeBubbleRadius, out float retentionRadius))
        {
            Handles.color = ActiveBubbleColor;
            DrawWireSphere(activeBubbleWorldCenter, activeBubbleRadius);
            Handles.color = new Color(ActiveBubbleColor.r, ActiveBubbleColor.g, ActiveBubbleColor.b, 0.45f);
            DrawWireSphere(activeBubbleWorldCenter, retentionRadius);

            if (ShowLabels)
            {
                Handles.Label(
                    activeBubbleWorldCenter + Vector3.up * Mathf.Max(0.6f, retentionRadius * 0.3f),
                    $"Active Bubble\nR={activeBubbleRadius:F1} / {retentionRadius:F1}");
            }
        }

        if (!ShowSpatialQueries)
            return;

        SpatialQueryCache.Clear();
        int spatialQueryCount = renderer.GetDebugSpatialQueries(SpatialQueryCache);
        for (int queryIndex = 0; queryIndex < spatialQueryCount; queryIndex++)
            DrawSpatialQuery(SpatialQueryCache[queryIndex]);
    }

    private static void DrawRectByCenterAndSize(
        Transform rendererTransform,
        Terrain terrain,
        Vector2 centerXZ,
        Vector2 sizeXZ,
        Color color,
        float verticalOffset,
        string label)
    {
        Vector2 halfSize = sizeXZ * 0.5f;
        Vector3 a = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(-halfSize.x, -halfSize.y), verticalOffset);
        Vector3 b = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(-halfSize.x, halfSize.y), verticalOffset);
        Vector3 c = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(halfSize.x, halfSize.y), verticalOffset);
        Vector3 d = BuildWorldScenePoint(rendererTransform, terrain, centerXZ + new Vector2(halfSize.x, -halfSize.y), verticalOffset);
        Vector3 center = BuildWorldScenePoint(rendererTransform, terrain, centerXZ, verticalOffset);

        Handles.color = color;
        Handles.DrawAAPolyLine(3.0f, new[] { a, b, c, d, a });
        Handles.color = new Color(color.r, color.g, color.b, 0.32f);
        Handles.DrawDottedLine(a, c, 5.0f);
        Handles.DrawDottedLine(b, d, 5.0f);

        if (!string.IsNullOrEmpty(label))
            Handles.Label(center + rendererTransform.up * Mathf.Max(0.45f, verticalOffset * 5.0f), label);
    }

    private static void DrawGridByMinMax(
        Transform rendererTransform,
        Terrain terrain,
        Vector2 minXZ,
        Vector2 maxXZ,
        Vector2Int dimensions,
        float cellSize,
        Color color,
        float verticalOffset,
        string label)
    {
        Vector3 a = BuildWorldScenePoint(rendererTransform, terrain, new Vector2(minXZ.x, minXZ.y), verticalOffset);
        Vector3 b = BuildWorldScenePoint(rendererTransform, terrain, new Vector2(minXZ.x, maxXZ.y), verticalOffset);
        Vector3 c = BuildWorldScenePoint(rendererTransform, terrain, new Vector2(maxXZ.x, maxXZ.y), verticalOffset);
        Vector3 d = BuildWorldScenePoint(rendererTransform, terrain, new Vector2(maxXZ.x, minXZ.y), verticalOffset);
        Vector2 centerXZ = (minXZ + maxXZ) * 0.5f;
        Vector3 center = BuildWorldScenePoint(rendererTransform, terrain, centerXZ, verticalOffset);

        Handles.color = color;
        Handles.DrawAAPolyLine(2.5f, new[] { a, b, c, d, a });

        int gridX = Mathf.Max(1, dimensions.x);
        int gridY = Mathf.Max(1, dimensions.y);
        float gridWidth = Mathf.Max(0.0001f, maxXZ.x - minXZ.x);
        float gridDepth = Mathf.Max(0.0001f, maxXZ.y - minXZ.y);

        Handles.color = new Color(color.r, color.g, color.b, 0.22f);
        for (int xIndex = 1; xIndex < gridX; xIndex++)
        {
            float xCoord = minXZ.x + gridWidth * (xIndex / (float)gridX);
            Vector3 lineStart = BuildWorldScenePoint(rendererTransform, terrain, new Vector2(xCoord, minXZ.y), verticalOffset * 0.8f);
            Vector3 lineEnd = BuildWorldScenePoint(rendererTransform, terrain, new Vector2(xCoord, maxXZ.y), verticalOffset * 0.8f);
            Handles.DrawLine(lineStart, lineEnd);
        }

        for (int yIndex = 1; yIndex < gridY; yIndex++)
        {
            float zCoord = minXZ.y + gridDepth * (yIndex / (float)gridY);
            Vector3 lineStart = BuildWorldScenePoint(rendererTransform, terrain, new Vector2(minXZ.x, zCoord), verticalOffset * 0.8f);
            Vector3 lineEnd = BuildWorldScenePoint(rendererTransform, terrain, new Vector2(maxXZ.x, zCoord), verticalOffset * 0.8f);
            Handles.DrawLine(lineStart, lineEnd);
        }

        if (cellSize > 0.0f)
        {
            Handles.color = new Color(color.r, color.g, color.b, 0.35f);
            Handles.DrawDottedLine(
                center,
                center + rendererTransform.right * Mathf.Min(cellSize, gridWidth) * 0.5f,
                4.0f);
        }

        if (!string.IsNullOrEmpty(label))
            Handles.Label(center + rendererTransform.up * Mathf.Max(0.45f, verticalOffset * 5.0f), label);
    }

    private static void DrawWireSphere(Vector3 worldCenter, float radius)
    {
        Handles.DrawWireDisc(worldCenter, Vector3.up, radius);
        Handles.DrawWireDisc(worldCenter, Vector3.right, radius);
        Handles.DrawWireDisc(worldCenter, Vector3.forward, radius);
    }

    private static void DrawSpatialQuery(CrowdVatSpatialQueryRequest query)
    {
        string labelPrefix = BuildSpatialQueryLabelPrefix(query);
        switch (query.shape)
        {
            case CrowdVatSpatialQueryShape.OverlapSphere:
                Handles.color = QuerySphereColor;
                DrawWireSphere(query.worldStart, query.radius);
                if (ShowLabels)
                {
                    Handles.Label(
                        query.worldStart + Vector3.up * Mathf.Max(0.4f, query.radius * 0.5f),
                        $"{labelPrefix}\nSphere r={query.radius:F1}");
                }

                return;

            case CrowdVatSpatialQueryShape.SweepCapsule:
                Handles.color = QueryCapsuleColor;
                DrawWireCapsule(query.worldStart, query.worldEnd, query.radius);
                if (ShowLabels)
                {
                    Vector3 labelCenter = Vector3.Lerp(query.worldStart, query.worldEnd, 0.5f);
                    Handles.Label(
                        labelCenter + Vector3.up * Mathf.Max(0.45f, query.radius * 0.5f),
                        $"{labelPrefix}\nCapsule r={query.radius:F1}");
                }

                return;
        }
    }

    private static string BuildSpatialQueryLabelPrefix(CrowdVatSpatialQueryRequest query)
    {
        if (query.queryId < 0)
            return query.flags == CrowdVatSpatialQueryFlags.ActiveOnly ? "\u5185\u5EFA\u4EA4\u4E92\u7403" : "\u5185\u5EFA\u4F5C\u7528\u7403";

        return $"Query {query.queryId}";
    }

    private static void DrawWireCapsule(Vector3 start, Vector3 end, float radius)
    {
        Vector3 axis = end - start;
        if (axis.sqrMagnitude <= 0.0001f)
        {
            DrawWireSphere(start, radius);
            return;
        }

        Vector3 axisDirection = axis.normalized;
        Vector3 referenceDirection = Mathf.Abs(Vector3.Dot(axisDirection, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
        Vector3 right = Vector3.Cross(axisDirection, referenceDirection).normalized * radius;
        Vector3 forward = Vector3.Cross(axisDirection, right.normalized).normalized * radius;

        Handles.DrawWireDisc(start, axisDirection, radius);
        Handles.DrawWireDisc(end, axisDirection, radius);
        Handles.DrawLine(start + right, end + right);
        Handles.DrawLine(start - right, end - right);
        Handles.DrawLine(start + forward, end + forward);
        Handles.DrawLine(start - forward, end - forward);
        DrawWireSphere(start, radius);
        DrawWireSphere(end, radius);
    }

    private static float ResolveHandleHeightOffset(SerializedObject serializedRenderer)
    {
        SerializedProperty collisionHeightProperty = serializedRenderer.FindProperty("_collisionHeight");
        float collisionHeight = collisionHeightProperty != null ? collisionHeightProperty.floatValue : 1.65f;
        return Mathf.Max(0.8f, collisionHeight + 0.15f);
    }

    private static Terrain ResolveTerrain(SerializedObject serializedRenderer, CrowdVatIndirectRenderer renderer)
    {
        if (serializedRenderer == null)
            return Terrain.activeTerrain;

        SerializedProperty terrainProperty = serializedRenderer.FindProperty("_terrain");
        Terrain terrain = terrainProperty != null ? terrainProperty.objectReferenceValue as Terrain : null;
        if (terrain != null && terrain.terrainData != null)
            return terrain;

        SerializedProperty autoResolveTerrainProperty = serializedRenderer.FindProperty("_autoResolveTerrain");
        if (autoResolveTerrainProperty != null && autoResolveTerrainProperty.boolValue)
        {
            terrain = Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
                return terrain;

            terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain != null && terrain.terrainData != null)
                return terrain;
        }

        return null;
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
}
