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
    private static readonly Color EnvironmentTerrainColor = new Color(0.18f, 0.86f, 1.0f, 0.95f);
    private static readonly Color EnvironmentObstacleColor = new Color(0.24f, 1.0f, 0.56f, 0.95f);
    private static readonly Color EnvironmentBakedColor = new Color(1.0f, 0.84f, 0.18f, 0.95f);
    private static readonly Color EnvironmentInsideColor = new Color(1.0f, 0.28f, 0.22f, 0.95f);
    private static readonly Color EnvironmentBakedBoundsColor = new Color(1.0f, 0.82f, 0.24f, 0.88f);
    private static readonly Color EnvironmentSdfBoundsColor = new Color(1.0f, 0.62f, 0.16f, 0.88f);
    private static readonly Color EnvironmentNearestSurfaceColor = new Color(1.0f, 0.97f, 0.34f, 0.95f);
    private static readonly Color EnvironmentSurfaceMeshColor = new Color(1.0f, 0.86f, 0.18f, 1.0f);
    private static readonly Color EnvironmentSurfaceWireColor = new Color(1.0f, 0.96f, 0.26f, 0.62f);
    private static readonly List<CrowdVatIndirectRenderer> RendererCache = new List<CrowdVatIndirectRenderer>(8);
    private static readonly HashSet<CrowdVatIndirectRenderer> RendererSet = new HashSet<CrowdVatIndirectRenderer>();
    private static readonly List<CrowdVatSpatialQueryRequest> SpatialQueryCache = new List<CrowdVatSpatialQueryRequest>(32);
    private static readonly Dictionary<int, EnvironmentSurfaceMeshCache> EnvironmentSurfaceMeshCaches = new Dictionary<int, EnvironmentSurfaceMeshCache>();
    private static readonly int[][] EnvironmentSurfaceTetrahedra =
    {
        new[] { 0, 5, 1, 6 },
        new[] { 0, 1, 2, 6 },
        new[] { 0, 2, 3, 6 },
        new[] { 0, 3, 7, 6 },
        new[] { 0, 7, 4, 6 },
        new[] { 0, 4, 5, 6 }
    };
    private const int EnvironmentDistanceMaxSamplesPerAxis = 18;
    private const int EnvironmentDistanceMinSliceCount = 1;
    private const int EnvironmentDistanceMaxSliceCount = 6;
    private const int EnvironmentSurfaceMinCellsPerAxis = 8;
    private const int EnvironmentSurfaceMaxCellsPerAxis = 96;
    private const int EnvironmentSurfaceMaxTriangleCount = 240000;
    private const int GridCellBoxMaxDrawCount = 512;
    private const float EnvironmentDistanceMinSampleSpacing = 0.5f;
    private const float EnvironmentDistanceMaxSampleSpacing = 8.0f;
    private const float EnvironmentDistanceMinHeightOffset = 0.0f;
    private const float EnvironmentDistanceMaxHeightOffset = 4.0f;
    private const float EnvironmentDistanceMinNearSurfaceThreshold = 0.05f;
    private const float EnvironmentDistanceMaxNearSurfaceThreshold = 4.0f;
    private const float EnvironmentDistanceMinNearestSurfaceDistance = 0.05f;
    private const float EnvironmentDistanceMaxNearestSurfaceDistance = 12.0f;

    private struct EnvironmentDistanceVolumeInfo
    {
        public bool valid;
        public bool isBaked;
        public Vector3 worldCenter;
        public Vector3 worldSize;
        public Vector3 worldMin;
        public Vector3 worldMax;
        public Vector3Int resolution;
        public Vector3 sampleStep;
    }

    private struct EnvironmentDistanceDebugStats
    {
        public int totalSampleCount;
        public int validSampleCount;
        public int displayedSampleCount;
        public int nearSurfaceSampleCount;
        public int insideSampleCount;
        public int nearestSurfaceProjectionCount;
        public int terrainDominantCount;
        public int obstacleDominantCount;
        public int bakedDominantCount;
        public float minCombinedDistance;
        public float maxCombinedDistance;

        public void RegisterValidSample(CrowdVatEnvironmentDistanceQueryResult result, float nearSurfaceThreshold)
        {
            validSampleCount++;
            if (Mathf.Abs(result.combinedSignedDistance) <= nearSurfaceThreshold)
                nearSurfaceSampleCount++;

            if (result.inside)
                insideSampleCount++;

            if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.BakedEnvironmentField) != 0)
            {
                bakedDominantCount++;
            }
            else if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.CombinedFromTerrain) != 0)
            {
                terrainDominantCount++;
            }
            else if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.CombinedFromObstacle) != 0)
            {
                obstacleDominantCount++;
            }

            minCombinedDistance = Mathf.Min(minCombinedDistance, result.combinedSignedDistance);
            maxCombinedDistance = Mathf.Max(maxCombinedDistance, result.combinedSignedDistance);
        }
    }

    private sealed class EnvironmentSurfaceMeshCache
    {
        public int descriptorHash;
        public Mesh mesh;
        public Vector3Int cellCount;
        public int triangleCount;
    }

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

    private static bool ShowGridCellBoxes
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowGridCellBoxes", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowGridCellBoxes", value);
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

    private static bool ShowEnvironmentDistanceField
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowEnvironmentDistanceField", false);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowEnvironmentDistanceField", value);
    }

    private static bool ShowEnvironmentDistanceGradient
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowEnvironmentDistanceGradient", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowEnvironmentDistanceGradient", value);
    }

    private static bool ShowEnvironmentDistanceNearestSurface
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowEnvironmentDistanceNearestSurface", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowEnvironmentDistanceNearestSurface", value);
    }

    private static bool ShowEnvironmentDistanceVolumeInfo
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowEnvironmentDistanceVolumeInfo", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowEnvironmentDistanceVolumeInfo", value);
    }

    private static bool ShowEnvironmentDistanceSampleStats
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowEnvironmentDistanceSampleStats", true);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowEnvironmentDistanceSampleStats", value);
    }

    private static bool ShowEnvironmentDistanceNearSurfaceOnly
    {
        get => EditorPrefs.GetBool(PrefKeyPrefix + "ShowEnvironmentDistanceNearSurfaceOnly", false);
        set => EditorPrefs.SetBool(PrefKeyPrefix + "ShowEnvironmentDistanceNearSurfaceOnly", value);
    }

    private static int EnvironmentDistanceSliceCount
    {
        get => EditorPrefs.GetInt(PrefKeyPrefix + "EnvironmentDistanceSliceCount", 3);
        set => EditorPrefs.SetInt(
            PrefKeyPrefix + "EnvironmentDistanceSliceCount",
            Mathf.Clamp(value, EnvironmentDistanceMinSliceCount, EnvironmentDistanceMaxSliceCount));
    }

    private static float EnvironmentDistanceSampleSpacing
    {
        get => EditorPrefs.GetFloat(PrefKeyPrefix + "ShowEnvironmentDistanceSampleSpacing", 2.0f);
        set => EditorPrefs.SetFloat(
            PrefKeyPrefix + "ShowEnvironmentDistanceSampleSpacing",
            Mathf.Clamp(value, EnvironmentDistanceMinSampleSpacing, EnvironmentDistanceMaxSampleSpacing));
    }

    private static float EnvironmentDistanceHeightOffset
    {
        get => EditorPrefs.GetFloat(PrefKeyPrefix + "ShowEnvironmentDistanceHeightOffset", 1.0f);
        set => EditorPrefs.SetFloat(
            PrefKeyPrefix + "ShowEnvironmentDistanceHeightOffset",
            Mathf.Clamp(value, EnvironmentDistanceMinHeightOffset, EnvironmentDistanceMaxHeightOffset));
    }

    private static float EnvironmentDistanceNearSurfaceThreshold
    {
        get => EditorPrefs.GetFloat(PrefKeyPrefix + "EnvironmentDistanceNearSurfaceThreshold", 0.5f);
        set => EditorPrefs.SetFloat(
            PrefKeyPrefix + "EnvironmentDistanceNearSurfaceThreshold",
            Mathf.Clamp(value, EnvironmentDistanceMinNearSurfaceThreshold, EnvironmentDistanceMaxNearSurfaceThreshold));
    }

    private static float EnvironmentDistanceNearestSurfaceMaxDistance
    {
        get => EditorPrefs.GetFloat(PrefKeyPrefix + "EnvironmentDistanceNearestSurfaceMaxDistance", 4.0f);
        set => EditorPrefs.SetFloat(
            PrefKeyPrefix + "EnvironmentDistanceNearestSurfaceMaxDistance",
            Mathf.Clamp(value, EnvironmentDistanceMinNearestSurfaceDistance, EnvironmentDistanceMaxNearestSurfaceDistance));
    }

    private static int EnvironmentSurfaceCellsPerAxis
    {
        get => EditorPrefs.GetInt(PrefKeyPrefix + "EnvironmentSurfaceCellsPerAxis", 72);
        set => EditorPrefs.SetInt(
            PrefKeyPrefix + "EnvironmentSurfaceCellsPerAxis",
            Mathf.Clamp(value, EnvironmentSurfaceMinCellsPerAxis, EnvironmentSurfaceMaxCellsPerAxis));
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

    private static Material EnvironmentSurfaceMaterial
    {
        get
        {
            if (_environmentSurfaceMaterial != null)
                return _environmentSurfaceMaterial;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                return null;

            _environmentSurfaceMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _environmentSurfaceMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
            _environmentSurfaceMaterial.SetInt("_DstBlend", (int)BlendMode.Zero);
            _environmentSurfaceMaterial.SetInt("_Cull", (int)CullMode.Off);
            _environmentSurfaceMaterial.SetInt("_ZWrite", 1);
            _environmentSurfaceMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            return _environmentSurfaceMaterial;
        }
    }

    private static Material _environmentSurfaceMaterial;

    private static void DrawOverlay(SceneView sceneView)
    {
        Handles.BeginGUI();
        float panelWidth = 268.0f;
        float panelHeight = OverlayExpanded ? 610.0f : 70.0f;
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
            settingsChanged |= DrawToggleLeft("显示双方初始生成范围", ShowFactionAreas, value => ShowFactionAreas = value);
            settingsChanged |= DrawToggleLeft("\u663E\u793A\u7A7A\u95F4\u7F51\u683C\u8303\u56F4", ShowGridBounds, value => ShowGridBounds = value);
            EditorGUI.BeginDisabledGroup(!ShowGridBounds);
            settingsChanged |= DrawToggleLeft("显示空间网格 Cell 盒", ShowGridCellBoxes, value => ShowGridCellBoxes = value);
            EditorGUI.EndDisabledGroup();
            settingsChanged |= DrawToggleLeft("\u663E\u793A Active Bubble", ShowActiveBubble, value => ShowActiveBubble = value);
            settingsChanged |= DrawToggleLeft("\u663E\u793A\u7A7A\u95F4\u67E5\u8BE2\u4F53", ShowSpatialQueries, value => ShowSpatialQueries = value);
            settingsChanged |= DrawToggleLeft("显示环境距离场", ShowEnvironmentDistanceField, value => ShowEnvironmentDistanceField = value);
            EditorGUI.BeginDisabledGroup(!ShowEnvironmentDistanceField);
            settingsChanged |= DrawToggleLeft("显示距离场梯度", ShowEnvironmentDistanceGradient, value => ShowEnvironmentDistanceGradient = value);
            settingsChanged |= DrawToggleLeft("显示完整 SDF 表面", ShowEnvironmentDistanceNearestSurface, value => ShowEnvironmentDistanceNearestSurface = value);
            settingsChanged |= DrawToggleLeft("显示体积元信息", ShowEnvironmentDistanceVolumeInfo, value => ShowEnvironmentDistanceVolumeInfo = value);
            settingsChanged |= DrawToggleLeft("显示采样统计", ShowEnvironmentDistanceSampleStats, value => ShowEnvironmentDistanceSampleStats = value);
            settingsChanged |= DrawToggleLeft("仅显示近表面", ShowEnvironmentDistanceNearSurfaceOnly, value => ShowEnvironmentDistanceNearSurfaceOnly = value);
            settingsChanged |= DrawIntSlider(
                "距离场切片层数",
                EnvironmentDistanceSliceCount,
                EnvironmentDistanceMinSliceCount,
                EnvironmentDistanceMaxSliceCount,
                value => EnvironmentDistanceSliceCount = value);
            settingsChanged |= DrawSlider(
                "距离场采样间距",
                EnvironmentDistanceSampleSpacing,
                EnvironmentDistanceMinSampleSpacing,
                EnvironmentDistanceMaxSampleSpacing,
                value => EnvironmentDistanceSampleSpacing = value);
            settingsChanged |= DrawSlider(
                "距离场离地高度",
                EnvironmentDistanceHeightOffset,
                EnvironmentDistanceMinHeightOffset,
                EnvironmentDistanceMaxHeightOffset,
                value => EnvironmentDistanceHeightOffset = value);
            EditorGUI.BeginDisabledGroup(!ShowEnvironmentDistanceNearestSurface);
            settingsChanged |= DrawIntSlider(
                "表面网格最大轴向格数",
                EnvironmentSurfaceCellsPerAxis,
                EnvironmentSurfaceMinCellsPerAxis,
                EnvironmentSurfaceMaxCellsPerAxis,
                value => EnvironmentSurfaceCellsPerAxis = value);
            settingsChanged |= DrawSlider(
                "采样投影最大距离",
                EnvironmentDistanceNearestSurfaceMaxDistance,
                EnvironmentDistanceMinNearestSurfaceDistance,
                EnvironmentDistanceMaxNearestSurfaceDistance,
                value => EnvironmentDistanceNearestSurfaceMaxDistance = value);
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!ShowEnvironmentDistanceNearSurfaceOnly);
            settingsChanged |= DrawSlider(
                "近表面阈值",
                EnvironmentDistanceNearSurfaceThreshold,
                EnvironmentDistanceMinNearSurfaceThreshold,
                EnvironmentDistanceMaxNearSurfaceThreshold,
                value => EnvironmentDistanceNearSurfaceThreshold = value);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.HelpBox("完整 SDF 表面会从 baked Texture3D 降采样重建零等值面；切片层数大于 1 时，会优先沿 baked/SDF 体积高度均匀切片。", MessageType.None);
            EditorGUI.EndDisabledGroup();
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
                    ShowGridCellBoxes = true;
                    ShowActiveBubble = false;
                    ShowSpatialQueries = false;
                    ShowEnvironmentDistanceField = false;
                    ShowEnvironmentDistanceGradient = true;
                    ShowEnvironmentDistanceNearestSurface = true;
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

    private static bool DrawSlider(string label, float currentValue, float minValue, float maxValue, System.Action<float> setter)
    {
        float nextValue = EditorGUILayout.Slider(label, currentValue, minValue, maxValue);
        if (Mathf.Abs(nextValue - currentValue) <= 1e-4f)
            return false;

        setter(nextValue);
        return true;
    }

    private static bool DrawIntSlider(string label, int currentValue, int minValue, int maxValue, System.Action<int> setter)
    {
        int nextValue = EditorGUILayout.IntSlider(label, currentValue, minValue, maxValue);
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
        ShowGridCellBoxes = enabled;
        ShowActiveBubble = enabled;
        ShowSpatialQueries = enabled;
        ShowEnvironmentDistanceField = enabled;
        ShowEnvironmentDistanceGradient = enabled;
        ShowEnvironmentDistanceNearestSurface = enabled;
        ShowEnvironmentDistanceVolumeInfo = enabled;
        ShowEnvironmentDistanceSampleStats = enabled;
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
                ShowLabels ? $"{renderer.name} 总出生范围\n{spawnSizeXZ.x:F1} x {spawnSizeXZ.y:F1}" : null);
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
                    ShowLabels ? "CampA 初始生成范围" : null);

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
                    ShowLabels ? "CampB 初始生成范围" : null);

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
                ShowGridCellBoxes,
                ShowLabels ? $"{renderer.name} \u7A7A\u95F4\u7F51\u683C\n{gridDimensions.x} x {gridDimensions.y}  cell XZ={gridCellSize:F2}" : null);
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

        if (ShowEnvironmentDistanceField)
            DrawEnvironmentDistanceField(renderer, terrain, handleHeightOffset);

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
        bool drawCellBoxes,
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
        int cellBoxStride = drawCellBoxes ? ComputeGridCellBoxStride(gridX, gridY) : 0;

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

        if (drawCellBoxes)
        {
            DrawGridCellBoxes(
                rendererTransform,
                terrain,
                minXZ,
                gridWidth,
                gridDepth,
                gridX,
                gridY,
                cellBoxStride,
                Mathf.Max(0.05f, cellSize),
                color,
                verticalOffset);
        }

        if (!string.IsNullOrEmpty(label))
        {
            string resolvedLabel = label;
            if (drawCellBoxes)
            {
                resolvedLabel += $"\nCell 盒 {cellSize:F2} x {cellSize:F2} x {cellSize:F2}（Y 仅辅助）";
                if (cellBoxStride > 1)
                    resolvedLabel += $"\n盒子抽样显示：每 {cellBoxStride} 格";
            }

            Handles.Label(center + rendererTransform.up * Mathf.Max(0.45f, verticalOffset * 5.0f), resolvedLabel);
        }
    }

    private static int ComputeGridCellBoxStride(int gridX, int gridY)
    {
        int totalCellCount = Mathf.Max(1, gridX * gridY);
        return Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(totalCellCount / (float)GridCellBoxMaxDrawCount)));
    }

    private static void DrawGridCellBoxes(
        Transform rendererTransform,
        Terrain terrain,
        Vector2 minXZ,
        float gridWidth,
        float gridDepth,
        int gridX,
        int gridY,
        int stride,
        float boxHeight,
        Color color,
        float verticalOffset)
    {
        if (stride <= 0)
            return;

        float cellWidth = gridWidth / Mathf.Max(1, gridX);
        float cellDepth = gridDepth / Mathf.Max(1, gridY);
        Handles.color = new Color(color.r, color.g, color.b, 0.38f);
        for (int yIndex = 0; yIndex < gridY; yIndex += stride)
        {
            for (int xIndex = 0; xIndex < gridX; xIndex += stride)
            {
                Vector2 cellMin = new Vector2(
                    minXZ.x + xIndex * cellWidth,
                    minXZ.y + yIndex * cellDepth);
                DrawGridCellBox(
                    rendererTransform,
                    terrain,
                    cellMin,
                    cellWidth,
                    cellDepth,
                    boxHeight,
                    verticalOffset);
            }
        }
    }

    private static void DrawGridCellBox(
        Transform rendererTransform,
        Terrain terrain,
        Vector2 cellMin,
        float cellWidth,
        float cellDepth,
        float boxHeight,
        float verticalOffset)
    {
        Vector2 cellMax = cellMin + new Vector2(cellWidth, cellDepth);
        Vector2 cellCenterXZ = (cellMin + cellMax) * 0.5f;
        float baseY = BuildWorldScenePoint(rendererTransform, terrain, cellCenterXZ, verticalOffset).y;

        Vector3 lbf = rendererTransform.TransformPoint(new Vector3(cellMin.x, 0.0f, cellMin.y));
        Vector3 lbb = rendererTransform.TransformPoint(new Vector3(cellMin.x, 0.0f, cellMax.y));
        Vector3 rbb = rendererTransform.TransformPoint(new Vector3(cellMax.x, 0.0f, cellMax.y));
        Vector3 rbf = rendererTransform.TransformPoint(new Vector3(cellMax.x, 0.0f, cellMin.y));
        lbf.y = baseY;
        lbb.y = baseY;
        rbb.y = baseY;
        rbf.y = baseY;

        Vector3 up = Vector3.up * boxHeight;
        Vector3 ltf = lbf + up;
        Vector3 ltb = lbb + up;
        Vector3 rtb = rbb + up;
        Vector3 rtf = rbf + up;

        Handles.DrawLine(lbf, lbb);
        Handles.DrawLine(lbb, rbb);
        Handles.DrawLine(rbb, rbf);
        Handles.DrawLine(rbf, lbf);
        Handles.DrawLine(ltf, ltb);
        Handles.DrawLine(ltb, rtb);
        Handles.DrawLine(rtb, rtf);
        Handles.DrawLine(rtf, ltf);
        Handles.DrawLine(lbf, ltf);
        Handles.DrawLine(lbb, ltb);
        Handles.DrawLine(rbb, rtb);
        Handles.DrawLine(rbf, rtf);
    }

    private static void DrawEnvironmentDistanceField(CrowdVatIndirectRenderer renderer, Terrain terrain, float handleHeightOffset)
    {
        if (!renderer.TryGetEnvironmentDistanceFieldDescriptor(out CrowdVatEnvironmentDistanceFieldDescriptor descriptor))
            return;

        if (!TryResolveEnvironmentSamplingRegion(renderer, descriptor, out Vector2 minXZ, out Vector2 maxXZ))
            return;

        TryGetEnvironmentVolumeInfo(descriptor, out EnvironmentDistanceVolumeInfo volumeInfo);
        DrawEnvironmentFieldBounds(volumeInfo);
        if (ShowEnvironmentDistanceNearestSurface)
            DrawEnvironmentSurfaceMesh(descriptor, volumeInfo);

        float width = Mathf.Max(0.01f, maxXZ.x - minXZ.x);
        float depth = Mathf.Max(0.01f, maxXZ.y - minXZ.y);
        float sampleSpacing = Mathf.Max(EnvironmentDistanceMinSampleSpacing, EnvironmentDistanceSampleSpacing);
        int sampleCountX = Mathf.Clamp(Mathf.CeilToInt(width / sampleSpacing) + 1, 2, EnvironmentDistanceMaxSamplesPerAxis);
        int sampleCountZ = Mathf.Clamp(Mathf.CeilToInt(depth / sampleSpacing) + 1, 2, EnvironmentDistanceMaxSamplesPerAxis);
        int resolvedSliceCount = Mathf.Clamp(EnvironmentDistanceSliceCount, EnvironmentDistanceMinSliceCount, EnvironmentDistanceMaxSliceCount);
        float gradientSampleDistance = Mathf.Max(0.05f, sampleSpacing * 0.35f);
        float nearSurfaceThreshold = Mathf.Max(EnvironmentDistanceMinNearSurfaceThreshold, EnvironmentDistanceNearSurfaceThreshold);
        float nearestSurfaceMaxDistance = Mathf.Max(EnvironmentDistanceMinNearestSurfaceDistance, EnvironmentDistanceNearestSurfaceMaxDistance);
        bool useVolumeSliceHeights = volumeInfo.valid && resolvedSliceCount > 1;
        float[] sliceHeights = useVolumeSliceHeights
            ? ResolveEnvironmentSliceHeights(volumeInfo, resolvedSliceCount)
            : null;
        float relativeSliceOffsetStep = resolvedSliceCount > 1 && !useVolumeSliceHeights
            ? Mathf.Max(0.25f, sampleSpacing)
            : 0.0f;
        Vector2 centerXZ = (minXZ + maxXZ) * 0.5f;
        Vector3 labelWorldPosition = renderer.transform.TransformPoint(new Vector3(centerXZ.x, 0.0f, centerXZ.y));
        if (volumeInfo.valid)
        {
            float volumeLabelOffset = ShowEnvironmentDistanceVolumeInfo
                ? Mathf.Max(1.45f, handleHeightOffset * 1.1f)
                : Mathf.Max(0.55f, handleHeightOffset * 0.36f);
            labelWorldPosition.y = volumeInfo.worldMax.y + volumeLabelOffset;
        }
        else
        {
            labelWorldPosition.y = ResolveEnvironmentSampleWorldY(descriptor, terrain, labelWorldPosition) + Mathf.Max(0.4f, handleHeightOffset * 0.28f);
        }

        EnvironmentDistanceDebugStats stats = new EnvironmentDistanceDebugStats
        {
            minCombinedDistance = float.PositiveInfinity,
            maxCombinedDistance = float.NegativeInfinity
        };

        for (int sliceIndex = 0; sliceIndex < resolvedSliceCount; sliceIndex++)
        {
            if (useVolumeSliceHeights)
            {
                Color sliceGuideColor = volumeInfo.isBaked ? EnvironmentBakedBoundsColor : EnvironmentSdfBoundsColor;
                float sliceGuideAlpha = Mathf.Lerp(
                    0.14f,
                    0.32f,
                    resolvedSliceCount <= 1 ? 1.0f : sliceIndex / (float)(resolvedSliceCount - 1));
                DrawEnvironmentSliceGuide(
                    renderer.transform,
                    minXZ,
                    maxXZ,
                    sliceHeights[sliceIndex],
                    new Color(sliceGuideColor.r, sliceGuideColor.g, sliceGuideColor.b, sliceGuideAlpha),
                    ShowLabels && ShowEnvironmentDistanceVolumeInfo
                        ? $"切片 {sliceIndex + 1}/{resolvedSliceCount}\nY={sliceHeights[sliceIndex]:F2}"
                        : null);
            }

            for (int sampleZ = 0; sampleZ < sampleCountZ; sampleZ++)
            {
                float v = sampleCountZ <= 1 ? 0.5f : sampleZ / (float)(sampleCountZ - 1);
                for (int sampleX = 0; sampleX < sampleCountX; sampleX++)
                {
                    stats.totalSampleCount++;

                    float u = sampleCountX <= 1 ? 0.5f : sampleX / (float)(sampleCountX - 1);
                    Vector2 localXZ = new Vector2(
                        Mathf.Lerp(minXZ.x, maxXZ.x, u),
                        Mathf.Lerp(minXZ.y, maxXZ.y, v));
                    Vector3 worldSamplePosition = renderer.transform.TransformPoint(new Vector3(localXZ.x, 0.0f, localXZ.y));
                    float baseSampleWorldY = ResolveEnvironmentSampleWorldY(descriptor, terrain, worldSamplePosition);
                    if (useVolumeSliceHeights)
                    {
                        worldSamplePosition.y = sliceHeights[sliceIndex];
                    }
                    else if (resolvedSliceCount > 1)
                    {
                        float centeredSliceOffset = sliceIndex - 0.5f * (resolvedSliceCount - 1);
                        worldSamplePosition.y = baseSampleWorldY + centeredSliceOffset * relativeSliceOffsetStep;
                    }
                    else
                    {
                        worldSamplePosition.y = baseSampleWorldY;
                    }

                    CrowdVatEnvironmentDistanceQueryRequest request = new CrowdVatEnvironmentDistanceQueryRequest
                    {
                        queryId = sliceIndex * sampleCountX * sampleCountZ + sampleZ * sampleCountX + sampleX,
                        queryType = ShowEnvironmentDistanceGradient || ShowEnvironmentDistanceNearestSurface
                            ? CrowdVatEnvironmentDistanceQueryType.SampleDetailed
                            : CrowdVatEnvironmentDistanceQueryType.SampleCombinedSignedDistance,
                        worldPosition = worldSamplePosition,
                        gradientSampleDistance = gradientSampleDistance
                    };

                    if (!CrowdVatSceneQueryUtility.TrySampleEnvironmentDistance(descriptor, request, out CrowdVatEnvironmentDistanceQueryResult result) ||
                        !result.valid)
                    {
                        continue;
                    }

                    stats.RegisterValidSample(result, nearSurfaceThreshold);

                    bool nearSurface = Mathf.Abs(result.combinedSignedDistance) <= nearSurfaceThreshold;
                    if (ShowEnvironmentDistanceNearSurfaceOnly && !nearSurface && !result.inside)
                        continue;

                    Color sampleColor = EvaluateEnvironmentDistanceColor(result, sampleSpacing);
                    if (resolvedSliceCount > 1)
                    {
                        float sliceFade = Mathf.Lerp(
                            1.0f,
                            0.72f,
                            resolvedSliceCount <= 1 ? 0.0f : sliceIndex / (float)(resolvedSliceCount - 1));
                        sampleColor = new Color(sampleColor.r, sampleColor.g, sampleColor.b, sampleColor.a * sliceFade);
                    }

                    if (sampleColor.a <= 0.01f)
                        continue;

                    stats.displayedSampleCount++;

                    float sampleRadius = Mathf.Clamp(HandleUtility.GetHandleSize(worldSamplePosition) * 0.03f, 0.04f, sampleSpacing * 0.2f);
                    if (resolvedSliceCount > 1)
                        sampleRadius *= 0.88f;

                    Handles.color = new Color(sampleColor.r, sampleColor.g, sampleColor.b, sampleColor.a * 0.6f);
                    Handles.DrawSolidDisc(worldSamplePosition, Vector3.up, sampleRadius);
                    Handles.color = sampleColor;
                    Handles.DrawWireDisc(worldSamplePosition, Vector3.up, sampleRadius * 1.45f);

                    if (ShowEnvironmentDistanceGradient && result.combinedGradient.sqrMagnitude > 1e-8f)
                    {
                        Vector3 gradientDirection = result.combinedGradient.normalized;
                        float gradientLength = Mathf.Max(sampleRadius * 4.5f, sampleSpacing * 0.28f);
                        Vector3 gradientEnd = worldSamplePosition + gradientDirection * gradientLength;
                        Handles.DrawLine(worldSamplePosition, gradientEnd);
                        Handles.DrawWireDisc(gradientEnd, gradientDirection, sampleRadius * 0.5f);
                    }

                    if (ShowEnvironmentDistanceNearestSurface &&
                        TryResolveEnvironmentNearestSurfaceProjection(
                            result,
                            worldSamplePosition,
                            nearestSurfaceMaxDistance,
                            out Vector3 nearestSurfacePosition,
                            out Vector3 nearestSurfaceNormal))
                    {
                        stats.nearestSurfaceProjectionCount++;
                        DrawEnvironmentNearestSurfaceProjection(
                            worldSamplePosition,
                            nearestSurfacePosition,
                            nearestSurfaceNormal,
                            sampleRadius,
                            sampleSpacing);
                    }

                    if (ShowLabels &&
                        ShouldDrawEnvironmentDistanceLabel(
                            sampleX,
                            sampleZ,
                            sampleCountX,
                            sampleCountZ,
                            sliceIndex,
                            resolvedSliceCount,
                            result,
                            sampleSpacing))
                    {
                        Handles.Label(
                            worldSamplePosition + Vector3.up * Mathf.Max(sampleRadius * 3.2f, 0.1f),
                            BuildEnvironmentDistanceLabel(result, worldSamplePosition.y));
                    }
                }
            }
        }

        if (ShowLabels)
        {
            Handles.color = Color.white;
            Handles.Label(
                labelWorldPosition,
                BuildEnvironmentDistanceSummaryLabel(
                    descriptor,
                    stats,
                    volumeInfo,
                    resolvedSliceCount,
                    useVolumeSliceHeights,
                    sliceHeights,
                    sampleSpacing,
                    nearSurfaceThreshold));
        }
    }

    private static void DrawEnvironmentSurfaceMesh(
        CrowdVatEnvironmentDistanceFieldDescriptor descriptor,
        EnvironmentDistanceVolumeInfo volumeInfo)
    {
        if (!volumeInfo.valid || !volumeInfo.isBaked)
            return;

        if (!TryGetOrBuildEnvironmentSurfaceMesh(descriptor.bakedField, out EnvironmentSurfaceMeshCache cache) ||
            cache.mesh == null ||
            cache.triangleCount <= 0)
        {
            if (ShowLabels && volumeInfo.valid)
            {
                Handles.color = EnvironmentSurfaceWireColor;
                Handles.Label(
                    volumeInfo.worldCenter + Vector3.up * Mathf.Max(0.6f, volumeInfo.worldSize.y * 0.58f),
                    "Baked SDF 表面网格不可用\n需要可读 Texture3D 且体积内存在零面");
            }

            return;
        }

        Material material = EnvironmentSurfaceMaterial;
        if (material != null)
        {
            material.SetColor("_Color", EnvironmentSurfaceMeshColor);
            material.SetPass(0);
            Graphics.DrawMeshNow(cache.mesh, Matrix4x4.identity, 0);

            material.SetColor("_Color", EnvironmentSurfaceWireColor);
            material.SetPass(0);
            Graphics.DrawMeshNow(cache.mesh, Matrix4x4.identity, 1);
        }

        if (ShowLabels && ShowEnvironmentDistanceVolumeInfo)
        {
            Handles.Label(
                volumeInfo.worldCenter + Vector3.up * Mathf.Max(0.75f, volumeInfo.worldSize.y * 0.62f),
                $"Baked SDF 零面\n调试网格 {cache.cellCount.x} x {cache.cellCount.y} x {cache.cellCount.z}\n三角形 {cache.triangleCount}");
        }
    }

    private static bool TryGetOrBuildEnvironmentSurfaceMesh(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        out EnvironmentSurfaceMeshCache cache)
    {
        cache = null;
        Texture3D texture = descriptor.sdfTexture;
        if (texture == null || !texture.isReadable)
            return false;

        int cacheKey = texture.GetInstanceID();
        int descriptorHash = ComputeEnvironmentSurfaceDescriptorHash(descriptor, EnvironmentSurfaceCellsPerAxis);
        if (!EnvironmentSurfaceMeshCaches.TryGetValue(cacheKey, out cache))
        {
            cache = new EnvironmentSurfaceMeshCache();
            EnvironmentSurfaceMeshCaches.Add(cacheKey, cache);
        }

        if (cache.mesh != null && cache.descriptorHash == descriptorHash)
            return true;

        if (cache.mesh != null)
            Object.DestroyImmediate(cache.mesh);

        cache.descriptorHash = descriptorHash;
        cache.mesh = BuildEnvironmentSurfaceMesh(descriptor, EnvironmentSurfaceCellsPerAxis, out cache.cellCount, out cache.triangleCount);
        return cache.mesh != null;
    }

    private static Mesh BuildEnvironmentSurfaceMesh(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        int maxCellsPerAxis,
        out Vector3Int cellCount,
        out int triangleCount)
    {
        Texture3D texture = descriptor.sdfTexture;
        cellCount = ResolveEnvironmentSurfaceCellCount(texture, maxCellsPerAxis);
        triangleCount = 0;
        if (cellCount.x <= 0 || cellCount.y <= 0 || cellCount.z <= 0)
            return null;

        int sampleCountX = cellCount.x + 1;
        int sampleCountY = cellCount.y + 1;
        int sampleCountZ = cellCount.z + 1;
        float[] distances = new float[sampleCountX * sampleCountY * sampleCountZ];

        int SampleIndex(int x, int y, int z)
        {
            return (z * sampleCountY + y) * sampleCountX + x;
        }

        for (int z = 0; z < sampleCountZ; z++)
        {
            float w = z / (float)cellCount.z;
            for (int y = 0; y < sampleCountY; y++)
            {
                float v = y / (float)cellCount.y;
                for (int x = 0; x < sampleCountX; x++)
                {
                    float u = x / (float)cellCount.x;
                    distances[SampleIndex(x, y, z)] = SampleBakedEnvironmentSurfaceDistance(descriptor, new Vector3(u, v, w));
                }
            }
        }

        List<Vector3> vertices = new List<Vector3>(8192);
        List<int> indices = new List<int>(8192);
        List<Color> colors = new List<Color>(8192);
        Vector3[] cubePositions = new Vector3[8];
        float[] cubeDistances = new float[8];
        Vector3 volumeMin = descriptor.worldCenter - descriptor.worldSize * 0.5f;

        for (int z = 0; z < cellCount.z; z++)
        {
            for (int y = 0; y < cellCount.y; y++)
            {
                for (int x = 0; x < cellCount.x; x++)
                {
                    FillEnvironmentSurfaceCube(
                        descriptor,
                        volumeMin,
                        cellCount,
                        distances,
                        SampleIndex,
                        x,
                        y,
                        z,
                        cubePositions,
                        cubeDistances);

                    for (int tetraIndex = 0; tetraIndex < EnvironmentSurfaceTetrahedra.Length; tetraIndex++)
                    {
                        AddEnvironmentSurfaceTetrahedron(
                            cubePositions,
                            cubeDistances,
                            EnvironmentSurfaceTetrahedra[tetraIndex],
                            vertices,
                            indices,
                            colors);
                        if (indices.Count / 3 >= EnvironmentSurfaceMaxTriangleCount)
                            break;
                    }

                    if (indices.Count / 3 >= EnvironmentSurfaceMaxTriangleCount)
                        break;
                }

                if (indices.Count / 3 >= EnvironmentSurfaceMaxTriangleCount)
                    break;
            }

            if (indices.Count / 3 >= EnvironmentSurfaceMaxTriangleCount)
                break;
        }

        Mesh mesh = new Mesh
        {
            name = "Baked Environment SDF Surface Gizmo",
            hideFlags = HideFlags.HideAndDontSave,
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        List<int> lineIndices = BuildEnvironmentSurfaceLineIndices(indices);
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(indices, 0);
        mesh.SetIndices(lineIndices, MeshTopology.Lines, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        triangleCount = indices.Count / 3;
        return mesh;
    }

    private static List<int> BuildEnvironmentSurfaceLineIndices(List<int> triangleIndices)
    {
        List<int> lineIndices = new List<int>(triangleIndices.Count * 2);
        for (int index = 0; index + 2 < triangleIndices.Count; index += 3)
        {
            int a = triangleIndices[index];
            int b = triangleIndices[index + 1];
            int c = triangleIndices[index + 2];
            lineIndices.Add(a);
            lineIndices.Add(b);
            lineIndices.Add(b);
            lineIndices.Add(c);
            lineIndices.Add(c);
            lineIndices.Add(a);
        }

        return lineIndices;
    }

    private delegate int EnvironmentSurfaceSampleIndex(int x, int y, int z);

    private static void FillEnvironmentSurfaceCube(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 volumeMin,
        Vector3Int cellCount,
        float[] distances,
        EnvironmentSurfaceSampleIndex sampleIndex,
        int x,
        int y,
        int z,
        Vector3[] cubePositions,
        float[] cubeDistances)
    {
        FillEnvironmentSurfaceCorner(descriptor, volumeMin, cellCount, distances, sampleIndex, x, y, z, 0, cubePositions, cubeDistances);
        FillEnvironmentSurfaceCorner(descriptor, volumeMin, cellCount, distances, sampleIndex, x + 1, y, z, 1, cubePositions, cubeDistances);
        FillEnvironmentSurfaceCorner(descriptor, volumeMin, cellCount, distances, sampleIndex, x + 1, y + 1, z, 2, cubePositions, cubeDistances);
        FillEnvironmentSurfaceCorner(descriptor, volumeMin, cellCount, distances, sampleIndex, x, y + 1, z, 3, cubePositions, cubeDistances);
        FillEnvironmentSurfaceCorner(descriptor, volumeMin, cellCount, distances, sampleIndex, x, y, z + 1, 4, cubePositions, cubeDistances);
        FillEnvironmentSurfaceCorner(descriptor, volumeMin, cellCount, distances, sampleIndex, x + 1, y, z + 1, 5, cubePositions, cubeDistances);
        FillEnvironmentSurfaceCorner(descriptor, volumeMin, cellCount, distances, sampleIndex, x + 1, y + 1, z + 1, 6, cubePositions, cubeDistances);
        FillEnvironmentSurfaceCorner(descriptor, volumeMin, cellCount, distances, sampleIndex, x, y + 1, z + 1, 7, cubePositions, cubeDistances);
    }

    private static void FillEnvironmentSurfaceCorner(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 volumeMin,
        Vector3Int cellCount,
        float[] distances,
        EnvironmentSurfaceSampleIndex sampleIndex,
        int x,
        int y,
        int z,
        int cornerIndex,
        Vector3[] cubePositions,
        float[] cubeDistances)
    {
        Vector3 uvw = new Vector3(
            x / (float)cellCount.x,
            y / (float)cellCount.y,
            z / (float)cellCount.z);
        cubePositions[cornerIndex] = volumeMin + new Vector3(
            descriptor.worldSize.x * uvw.x,
            descriptor.worldSize.y * uvw.y,
            descriptor.worldSize.z * uvw.z);
        cubeDistances[cornerIndex] = distances[sampleIndex(x, y, z)];
    }

    private static void AddEnvironmentSurfaceTetrahedron(
        Vector3[] cubePositions,
        float[] cubeDistances,
        int[] tetrahedron,
        List<Vector3> vertices,
        List<int> indices,
        List<Color> colors)
    {
        int[] inside = new int[4];
        int[] outside = new int[4];
        int insideCount = 0;
        int outsideCount = 0;
        for (int index = 0; index < 4; index++)
        {
            int corner = tetrahedron[index];
            if (cubeDistances[corner] <= 0.0f)
                inside[insideCount++] = corner;
            else
                outside[outsideCount++] = corner;
        }

        if (insideCount == 0 || insideCount == 4)
            return;

        if (insideCount == 1)
        {
            AddEnvironmentSurfaceTriangle(
                InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, inside[0], outside[0]),
                InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, inside[0], outside[1]),
                InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, inside[0], outside[2]),
                vertices,
                indices,
                colors);
            return;
        }

        if (insideCount == 3)
        {
            AddEnvironmentSurfaceTriangle(
                InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, outside[0], inside[2]),
                InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, outside[0], inside[1]),
                InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, outside[0], inside[0]),
                vertices,
                indices,
                colors);
            return;
        }

        Vector3 a = InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, inside[0], outside[0]);
        Vector3 b = InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, inside[0], outside[1]);
        Vector3 c = InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, inside[1], outside[0]);
        Vector3 d = InterpolateEnvironmentSurfaceEdge(cubePositions, cubeDistances, inside[1], outside[1]);
        AddEnvironmentSurfaceTriangle(a, b, c, vertices, indices, colors);
        AddEnvironmentSurfaceTriangle(c, b, d, vertices, indices, colors);
    }

    private static Vector3 InterpolateEnvironmentSurfaceEdge(
        Vector3[] cubePositions,
        float[] cubeDistances,
        int a,
        int b)
    {
        float distanceA = cubeDistances[a];
        float distanceB = cubeDistances[b];
        float denominator = distanceA - distanceB;
        float t = Mathf.Abs(denominator) <= 1e-6f ? 0.5f : Mathf.Clamp01(distanceA / denominator);
        return Vector3.Lerp(cubePositions[a], cubePositions[b], t);
    }

    private static void AddEnvironmentSurfaceTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        List<Vector3> vertices,
        List<int> indices,
        List<Color> colors)
    {
        if (indices.Count / 3 >= EnvironmentSurfaceMaxTriangleCount)
            return;

        int vertexIndex = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        indices.Add(vertexIndex);
        indices.Add(vertexIndex + 1);
        indices.Add(vertexIndex + 2);
        colors.Add(EnvironmentSurfaceMeshColor);
        colors.Add(EnvironmentSurfaceMeshColor);
        colors.Add(EnvironmentSurfaceMeshColor);
    }

    private static Vector3Int ResolveEnvironmentSurfaceCellCount(Texture3D texture, int maxCellsPerAxis)
    {
        int sourceX = Mathf.Max(1, texture.width - 1);
        int sourceY = Mathf.Max(1, texture.height - 1);
        int sourceZ = Mathf.Max(1, texture.depth - 1);
        int sourceMax = Mathf.Max(sourceX, Mathf.Max(sourceY, sourceZ));
        float scale = Mathf.Clamp(maxCellsPerAxis, EnvironmentSurfaceMinCellsPerAxis, EnvironmentSurfaceMaxCellsPerAxis) / (float)sourceMax;
        return new Vector3Int(
            Mathf.Clamp(Mathf.RoundToInt(sourceX * scale), 2, EnvironmentSurfaceMaxCellsPerAxis),
            Mathf.Clamp(Mathf.RoundToInt(sourceY * scale), 2, EnvironmentSurfaceMaxCellsPerAxis),
            Mathf.Clamp(Mathf.RoundToInt(sourceZ * scale), 2, EnvironmentSurfaceMaxCellsPerAxis));
    }

    private static float SampleBakedEnvironmentSurfaceDistance(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 uvw)
    {
        Color sample = SampleTexture3DTrilinear(descriptor.sdfTexture, uvw);
        return sample.r * descriptor.distanceScale + descriptor.distanceBias;
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

        Color c00 = Color.Lerp(c000, c100, tx);
        Color c10 = Color.Lerp(c010, c110, tx);
        Color c01 = Color.Lerp(c001, c101, tx);
        Color c11 = Color.Lerp(c011, c111, tx);
        Color c0 = Color.Lerp(c00, c10, ty);
        Color c1 = Color.Lerp(c01, c11, ty);
        return Color.Lerp(c0, c1, tz);
    }

    private static int ComputeEnvironmentSurfaceDescriptorHash(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        int maxCellsPerAxis)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + descriptor.sdfTexture.GetInstanceID();
            hash = hash * 31 + descriptor.sdfTexture.width;
            hash = hash * 31 + descriptor.sdfTexture.height;
            hash = hash * 31 + descriptor.sdfTexture.depth;
            hash = hash * 31 + descriptor.worldCenter.GetHashCode();
            hash = hash * 31 + descriptor.worldSize.GetHashCode();
            hash = hash * 31 + descriptor.distanceScale.GetHashCode();
            hash = hash * 31 + descriptor.distanceBias.GetHashCode();
            hash = hash * 31 + maxCellsPerAxis;
            return hash;
        }
    }

    private static bool TryResolveEnvironmentNearestSurfaceProjection(
        CrowdVatEnvironmentDistanceQueryResult result,
        Vector3 worldSamplePosition,
        float maxProjectionDistance,
        out Vector3 nearestSurfacePosition,
        out Vector3 nearestSurfaceNormal)
    {
        nearestSurfacePosition = worldSamplePosition;
        nearestSurfaceNormal = Vector3.up;
        if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.BakedEnvironmentField) == 0)
            return false;

        if (float.IsNaN(result.combinedSignedDistance) || float.IsInfinity(result.combinedSignedDistance))
            return false;

        if (Mathf.Abs(result.combinedSignedDistance) > maxProjectionDistance)
            return false;

        if (result.combinedGradient.sqrMagnitude <= 1e-8f)
            return false;

        nearestSurfaceNormal = result.combinedGradient.normalized;
        nearestSurfacePosition = worldSamplePosition - nearestSurfaceNormal * result.combinedSignedDistance;
        return true;
    }

    private static void DrawEnvironmentNearestSurfaceProjection(
        Vector3 worldSamplePosition,
        Vector3 nearestSurfacePosition,
        Vector3 nearestSurfaceNormal,
        float sampleRadius,
        float sampleSpacing)
    {
        float surfaceRadius = Mathf.Clamp(
            HandleUtility.GetHandleSize(nearestSurfacePosition) * 0.025f,
            sampleRadius * 0.8f,
            sampleSpacing * 0.16f);
        Color lineColor = new Color(
            EnvironmentNearestSurfaceColor.r,
            EnvironmentNearestSurfaceColor.g,
            EnvironmentNearestSurfaceColor.b,
            0.48f);
        Handles.color = lineColor;
        Handles.DrawDottedLine(worldSamplePosition, nearestSurfacePosition, 3.0f);
        Handles.DrawWireDisc(nearestSurfacePosition, nearestSurfaceNormal, surfaceRadius * 1.45f);
        Handles.DrawWireDisc(nearestSurfacePosition, Vector3.up, surfaceRadius * 0.85f);
        Handles.color = EnvironmentNearestSurfaceColor;
        Handles.DrawSolidDisc(nearestSurfacePosition, nearestSurfaceNormal, surfaceRadius);
    }

    private static void DrawWireSphere(Vector3 worldCenter, float radius)
    {
        Handles.DrawWireDisc(worldCenter, Vector3.up, radius);
        Handles.DrawWireDisc(worldCenter, Vector3.right, radius);
        Handles.DrawWireDisc(worldCenter, Vector3.forward, radius);
    }

    private static bool TryResolveEnvironmentSamplingRegion(
        CrowdVatIndirectRenderer renderer,
        CrowdVatEnvironmentDistanceFieldDescriptor descriptor,
        out Vector2 minXZ,
        out Vector2 maxXZ)
    {
        minXZ = Vector2.zero;
        maxXZ = Vector2.zero;
        if (renderer.TryGetDebugGridBounds(out minXZ, out maxXZ, out _, out _))
            return true;

        if (renderer.TryGetDebugSpawnArea(out Vector2 centerXZ, out Vector2 sizeXZ))
        {
            Vector2 halfSize = sizeXZ * 0.5f;
            minXZ = centerXZ - halfSize;
            maxXZ = centerXZ + halfSize;
            return sizeXZ.x > 0.0f && sizeXZ.y > 0.0f;
        }

        if (TryProjectWorldBoundsToRendererLocalXZ(
                renderer.transform,
                descriptor.bakedField.worldCenter,
                descriptor.bakedField.worldSize,
                out minXZ,
                out maxXZ))
        {
            return true;
        }

        if (TryProjectWorldBoundsToRendererLocalXZ(
                renderer.transform,
                descriptor.obstacleField.worldCenter,
                descriptor.obstacleField.worldSize,
                out minXZ,
                out maxXZ))
        {
            return true;
        }

        return false;
    }

    private static float ResolveEnvironmentSampleWorldY(
        CrowdVatEnvironmentDistanceFieldDescriptor descriptor,
        Terrain terrain,
        Vector3 worldPosition)
    {
        Terrain terrainSource = descriptor.groundField.terrain != null ? descriptor.groundField.terrain : terrain;
        if (terrainSource != null && terrainSource.terrainData != null)
        {
            Vector3 terrainMin = terrainSource.transform.position;
            Vector3 terrainMax = terrainMin + terrainSource.terrainData.size;
            if (worldPosition.x >= terrainMin.x &&
                worldPosition.x <= terrainMax.x &&
                worldPosition.z >= terrainMin.z &&
                worldPosition.z <= terrainMax.z)
            {
                return terrainSource.SampleHeight(worldPosition) +
                    terrainMin.y +
                    descriptor.groundField.terrainHeightOffset +
                    EnvironmentDistanceHeightOffset;
            }
        }

        if (TryGetWorldBounds(descriptor.bakedField.worldCenter, descriptor.bakedField.worldSize, out Vector3 bakedMin, out _))
            return bakedMin.y + EnvironmentDistanceHeightOffset;

        if (TryGetWorldBounds(descriptor.obstacleField.worldCenter, descriptor.obstacleField.worldSize, out Vector3 obstacleMin, out _))
            return obstacleMin.y + EnvironmentDistanceHeightOffset;

        return worldPosition.y + EnvironmentDistanceHeightOffset;
    }

    private static bool TryGetEnvironmentVolumeInfo(
        CrowdVatEnvironmentDistanceFieldDescriptor descriptor,
        out EnvironmentDistanceVolumeInfo volumeInfo)
    {
        volumeInfo = default;

        Texture3D bakedTexture = descriptor.bakedField.sdfTexture;
        if (bakedTexture != null &&
            TryGetWorldBounds(descriptor.bakedField.worldCenter, descriptor.bakedField.worldSize, out Vector3 bakedMin, out Vector3 bakedMax))
        {
            volumeInfo.valid = true;
            volumeInfo.isBaked = true;
            volumeInfo.worldCenter = descriptor.bakedField.worldCenter;
            volumeInfo.worldSize = descriptor.bakedField.worldSize;
            volumeInfo.worldMin = bakedMin;
            volumeInfo.worldMax = bakedMax;
            volumeInfo.resolution = new Vector3Int(
                Mathf.Max(1, bakedTexture.width),
                Mathf.Max(1, bakedTexture.height),
                Mathf.Max(1, bakedTexture.depth));
            volumeInfo.sampleStep = ComputeTextureSampleStep(volumeInfo.worldSize, volumeInfo.resolution);
            return true;
        }

        Texture3D obstacleTexture = descriptor.obstacleField.sdfTexture;
        if (obstacleTexture != null &&
            TryGetWorldBounds(descriptor.obstacleField.worldCenter, descriptor.obstacleField.worldSize, out Vector3 obstacleMin, out Vector3 obstacleMax))
        {
            volumeInfo.valid = true;
            volumeInfo.isBaked = false;
            volumeInfo.worldCenter = descriptor.obstacleField.worldCenter;
            volumeInfo.worldSize = descriptor.obstacleField.worldSize;
            volumeInfo.worldMin = obstacleMin;
            volumeInfo.worldMax = obstacleMax;
            volumeInfo.resolution = new Vector3Int(
                Mathf.Max(1, obstacleTexture.width),
                Mathf.Max(1, obstacleTexture.height),
                Mathf.Max(1, obstacleTexture.depth));
            volumeInfo.sampleStep = ComputeTextureSampleStep(volumeInfo.worldSize, volumeInfo.resolution);
            return true;
        }

        return false;
    }

    private static float[] ResolveEnvironmentSliceHeights(EnvironmentDistanceVolumeInfo volumeInfo, int sliceCount)
    {
        int resolvedSliceCount = Mathf.Clamp(sliceCount, EnvironmentDistanceMinSliceCount, EnvironmentDistanceMaxSliceCount);
        float[] heights = new float[resolvedSliceCount];
        if (!volumeInfo.valid)
            return heights;

        float volumeMargin = Mathf.Min(
            EnvironmentDistanceHeightOffset,
            Mathf.Max(0.0f, volumeInfo.worldSize.y * 0.5f - 0.01f));
        float minY = volumeInfo.worldMin.y + volumeMargin;
        float maxY = volumeInfo.worldMax.y - volumeMargin;
        if (maxY < minY)
        {
            float midY = (volumeInfo.worldMin.y + volumeInfo.worldMax.y) * 0.5f;
            minY = midY;
            maxY = midY;
        }

        for (int sliceIndex = 0; sliceIndex < resolvedSliceCount; sliceIndex++)
        {
            float t = resolvedSliceCount <= 1 ? 0.5f : sliceIndex / (float)(resolvedSliceCount - 1);
            heights[sliceIndex] = Mathf.Lerp(minY, maxY, t);
        }

        return heights;
    }

    private static void DrawEnvironmentSliceGuide(
        Transform rendererTransform,
        Vector2 minXZ,
        Vector2 maxXZ,
        float worldY,
        Color color,
        string label)
    {
        Vector3 a = rendererTransform.TransformPoint(new Vector3(minXZ.x, 0.0f, minXZ.y));
        Vector3 b = rendererTransform.TransformPoint(new Vector3(minXZ.x, 0.0f, maxXZ.y));
        Vector3 c = rendererTransform.TransformPoint(new Vector3(maxXZ.x, 0.0f, maxXZ.y));
        Vector3 d = rendererTransform.TransformPoint(new Vector3(maxXZ.x, 0.0f, minXZ.y));
        Vector3 center = rendererTransform.TransformPoint(new Vector3((minXZ.x + maxXZ.x) * 0.5f, 0.0f, (minXZ.y + maxXZ.y) * 0.5f));

        a.y = worldY;
        b.y = worldY;
        c.y = worldY;
        d.y = worldY;
        center.y = worldY;

        Handles.color = color;
        Handles.DrawAAPolyLine(1.6f, new[] { a, b, c, d, a });
        Handles.DrawDottedLine(a, c, 3.0f);
        Handles.DrawDottedLine(b, d, 3.0f);

        if (!string.IsNullOrEmpty(label))
            Handles.Label(center + Vector3.up * 0.06f, label);
    }

    private static Color EvaluateEnvironmentDistanceColor(CrowdVatEnvironmentDistanceQueryResult result, float sampleSpacing)
    {
        Color baseColor;
        if (result.inside)
        {
            baseColor = EnvironmentInsideColor;
        }
        else if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.BakedEnvironmentField) != 0)
        {
            baseColor = EnvironmentBakedColor;
        }
        else if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.CombinedFromTerrain) != 0)
        {
            baseColor = EnvironmentTerrainColor;
        }
        else if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.CombinedFromObstacle) != 0)
        {
            baseColor = EnvironmentObstacleColor;
        }
        else
        {
            baseColor = Color.white;
        }

        float fadeDistance = Mathf.Max(sampleSpacing * 2.5f, 0.01f);
        float normalizedDistance = 1.0f - Mathf.Clamp01(Mathf.Abs(result.combinedSignedDistance) / fadeDistance);
        float alpha = result.inside
            ? Mathf.Lerp(0.52f, 0.92f, normalizedDistance)
            : Mathf.Lerp(0.06f, 0.82f, normalizedDistance);
        if (!result.inside && alpha <= 0.08f)
            return Color.clear;

        return new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
    }

    private static bool ShouldDrawEnvironmentDistanceLabel(
        int sampleX,
        int sampleZ,
        int sampleCountX,
        int sampleCountZ,
        int sliceIndex,
        int sliceCount,
        CrowdVatEnvironmentDistanceQueryResult result,
        float sampleSpacing)
    {
        bool highlightSlice = sliceCount <= 1 ||
            sliceIndex == 0 ||
            sliceIndex == sliceCount - 1 ||
            sliceIndex == sliceCount / 2;
        if (!highlightSlice && !result.inside)
            return false;

        int stride = sampleCountX > 10 || sampleCountZ > 10 ? 4 : 2;
        if (result.inside)
            return sampleX % stride == 0 && sampleZ % stride == 0;

        return sampleX % stride == 0 &&
            sampleZ % stride == 0 &&
            Mathf.Abs(result.combinedSignedDistance) <= sampleSpacing * 0.7f;
    }

    private static string BuildEnvironmentDistanceLabel(CrowdVatEnvironmentDistanceQueryResult result, float worldY)
    {
        string header = ShowEnvironmentDistanceGradient
            ? $"Y {worldY:F2}  C {FormatDistance(result.combinedSignedDistance)}  |G| {result.combinedGradient.magnitude:F2}"
            : $"Y {worldY:F2}  C {FormatDistance(result.combinedSignedDistance)}";
        string flagSummary = BuildEnvironmentDistanceFlagSummary(result);

        if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.BakedEnvironmentField) != 0)
        {
            return string.IsNullOrEmpty(flagSummary)
                ? $"{header}\n来源 Baked 统一环境场"
                : $"{header}\n来源 Baked 统一环境场\n{flagSummary}";
        }

        string terrainDistance = result.hasTerrain ? FormatDistance(result.terrainSignedDistance) : "--";
        string obstacleDistance = result.hasObstacle ? FormatDistance(result.obstacleSignedDistance) : "--";
        string sourceName = BuildEnvironmentCombinedSourceLabel(result);
        return string.IsNullOrEmpty(flagSummary)
            ? $"{header}\nT {terrainDistance}  O {obstacleDistance}\n来源 {sourceName}"
            : $"{header}\nT {terrainDistance}  O {obstacleDistance}\n来源 {sourceName}  {flagSummary}";
    }

    private static string BuildEnvironmentDistanceSummaryLabel(
        CrowdVatEnvironmentDistanceFieldDescriptor descriptor,
        EnvironmentDistanceDebugStats stats,
        EnvironmentDistanceVolumeInfo volumeInfo,
        int resolvedSliceCount,
        bool useVolumeSliceHeights,
        float[] sliceHeights,
        float sampleSpacing,
        float nearSurfaceThreshold)
    {
        string title = BuildEnvironmentDistanceTitle(descriptor, volumeInfo);
        if (!ShowEnvironmentDistanceSampleStats)
            return title;

        string distanceRange = stats.validSampleCount > 0
            ? $"{FormatDistance(stats.minCombinedDistance)} .. {FormatDistance(stats.maxCombinedDistance)}"
            : "--";
        string sliceMode = resolvedSliceCount <= 1
            ? $"单层  离地 {EnvironmentDistanceHeightOffset:F1}"
            : useVolumeSliceHeights
                ? $"体积切片  {BuildEnvironmentSliceSummary(sliceHeights)}"
                : $"围绕地表  层间距 {sampleSpacing:F1}";
        string filterMode = ShowEnvironmentDistanceNearSurfaceOnly
            ? $"显示近表面 <= {nearSurfaceThreshold:F2}"
            : "显示完整采样";
        return
            $"{title}\n" +
            $"采样 {stats.validSampleCount}/{stats.totalSampleCount}  显示 {stats.displayedSampleCount}  近表面 {stats.nearSurfaceSampleCount}  表面投影 {stats.nearestSurfaceProjectionCount}  内部 {stats.insideSampleCount}\n" +
            $"切片 {resolvedSliceCount}  {sliceMode}\n" +
            $"主导 Baked {stats.bakedDominantCount}  地形 {stats.terrainDominantCount}  障碍 {stats.obstacleDominantCount}  距离域 {distanceRange}\n" +
            $"{filterMode}  采样间距 {sampleSpacing:F1}\n" +
            "颜色: 红=内部  青=地形  绿=障碍  黄=Baked/零面";
    }

    private static string BuildEnvironmentDistanceTitle(
        CrowdVatEnvironmentDistanceFieldDescriptor descriptor,
        EnvironmentDistanceVolumeInfo volumeInfo)
    {
        if (volumeInfo.valid)
        {
            string sourceName = volumeInfo.isBaked ? "Baked" : "地形 + 障碍";
            return $"环境距离场（{sourceName} {volumeInfo.resolution.x}x{volumeInfo.resolution.y}x{volumeInfo.resolution.z}）";
        }

        if (descriptor.obstacleField.sdfTexture != null)
            return "环境距离场（地形 + 障碍）";

        if (descriptor.groundField.terrain != null)
            return "环境距离场（地形）";

        return "环境距离场";
    }

    private static string BuildEnvironmentCombinedSourceLabel(CrowdVatEnvironmentDistanceQueryResult result)
    {
        if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.CombinedFromTerrain) != 0)
            return "地形主导";

        if ((result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.CombinedFromObstacle) != 0)
            return "障碍主导";

        if (result.hasTerrain && result.hasObstacle)
            return "双源";

        if (result.hasTerrain)
            return "地形";

        if (result.hasObstacle)
            return "障碍";

        return "环境";
    }

    private static string BuildEnvironmentDistanceFlagSummary(CrowdVatEnvironmentDistanceQueryResult result)
    {
        string flags = string.Empty;
        AppendEnvironmentLabelToken(ref flags, result.inside, "内部");
        AppendEnvironmentLabelToken(
            ref flags,
            (result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.TerrainBelowSurface) != 0,
            "地表下");
        AppendEnvironmentLabelToken(
            ref flags,
            (result.sourceFlags & CrowdVatEnvironmentDistanceSourceFlags.InsideObstacle) != 0,
            "障碍内");
        return string.IsNullOrEmpty(flags) ? string.Empty : $"标记 {flags}";
    }

    private static void AppendEnvironmentLabelToken(ref string label, bool condition, string token)
    {
        if (!condition)
            return;

        label = string.IsNullOrEmpty(label) ? token : $"{label}/{token}";
    }

    private static string BuildEnvironmentSliceSummary(float[] sliceHeights)
    {
        if (sliceHeights == null || sliceHeights.Length <= 0)
            return "Y --";

        if (sliceHeights.Length == 1)
            return $"Y {sliceHeights[0]:F2}";

        if (sliceHeights.Length <= 4)
        {
            string label = "Y ";
            for (int index = 0; index < sliceHeights.Length; index++)
            {
                if (index > 0)
                    label += "/";

                label += sliceHeights[index].ToString("F1");
            }

            return label;
        }

        return $"Y {sliceHeights[0]:F1}..{sliceHeights[sliceHeights.Length - 1]:F1}";
    }

    private static string BuildEnvironmentVolumeInfoLabel(EnvironmentDistanceVolumeInfo volumeInfo)
    {
        string volumeName = volumeInfo.isBaked ? "Baked 环境场" : "静态障碍 SDF";
        return
            $"{volumeName}\n" +
            $"中心 {FormatVector3Compact(volumeInfo.worldCenter)}\n" +
            $"尺寸 {FormatVector3Compact(volumeInfo.worldSize)}\n" +
            $"最小 {FormatVector3Compact(volumeInfo.worldMin)}\n" +
            $"最大 {FormatVector3Compact(volumeInfo.worldMax)}\n" +
            $"分辨率 {volumeInfo.resolution.x} x {volumeInfo.resolution.y} x {volumeInfo.resolution.z}\n" +
            $"采样步长 {FormatVector3Compact(volumeInfo.sampleStep)}";
    }

    private static void DrawEnvironmentFieldBounds(EnvironmentDistanceVolumeInfo volumeInfo)
    {
        if (!volumeInfo.valid)
            return;

        DrawWorldBoundsBox(
            volumeInfo.worldCenter,
            volumeInfo.worldSize,
            volumeInfo.isBaked ? EnvironmentBakedBoundsColor : EnvironmentSdfBoundsColor,
            ShowLabels && ShowEnvironmentDistanceVolumeInfo
                ? BuildEnvironmentVolumeInfoLabel(volumeInfo)
                : null);
    }

    private static Vector3 ComputeTextureSampleStep(Vector3 worldSize, Vector3Int resolution)
    {
        return new Vector3(
            worldSize.x / Mathf.Max(resolution.x - 1, 1),
            worldSize.y / Mathf.Max(resolution.y - 1, 1),
            worldSize.z / Mathf.Max(resolution.z - 1, 1));
    }

    private static bool TryProjectWorldBoundsToRendererLocalXZ(
        Transform rendererTransform,
        Vector3 worldCenter,
        Vector3 worldSize,
        out Vector2 minXZ,
        out Vector2 maxXZ)
    {
        minXZ = Vector2.zero;
        maxXZ = Vector2.zero;
        if (rendererTransform == null || !TryGetWorldBounds(worldCenter, worldSize, out Vector3 min, out Vector3 max))
            return false;

        Matrix4x4 worldToLocal = rendererTransform.worldToLocalMatrix;
        bool hasCorner = false;
        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            Vector3 corner = new Vector3(
                (cornerIndex & 1) == 0 ? min.x : max.x,
                (cornerIndex & 2) == 0 ? min.y : max.y,
                (cornerIndex & 4) == 0 ? min.z : max.z);
            Vector3 localCorner = worldToLocal.MultiplyPoint3x4(corner);
            Vector2 localXZ = new Vector2(localCorner.x, localCorner.z);
            if (!hasCorner)
            {
                minXZ = localXZ;
                maxXZ = localXZ;
                hasCorner = true;
                continue;
            }

            minXZ = Vector2.Min(minXZ, localXZ);
            maxXZ = Vector2.Max(maxXZ, localXZ);
        }

        return hasCorner && maxXZ.x > minXZ.x && maxXZ.y > minXZ.y;
    }

    private static bool TryGetWorldBounds(Vector3 worldCenter, Vector3 worldSize, out Vector3 min, out Vector3 max)
    {
        min = Vector3.zero;
        max = Vector3.zero;
        if (worldSize.x <= 0.0f || worldSize.y <= 0.0f || worldSize.z <= 0.0f)
            return false;

        Vector3 clampedSize = new Vector3(
            Mathf.Max(0.01f, worldSize.x),
            Mathf.Max(0.01f, worldSize.y),
            Mathf.Max(0.01f, worldSize.z));
        Vector3 halfSize = clampedSize * 0.5f;
        min = worldCenter - halfSize;
        max = worldCenter + halfSize;
        return true;
    }

    private static string FormatDistance(float value)
    {
        return float.IsFinite(value) ? $"{value:+0.00;-0.00;0.00}" : "--";
    }

    private static string FormatVector3Compact(Vector3 value)
    {
        return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
    }

    private static void DrawWorldBoundsBox(Vector3 center, Vector3 size, Color color, string label)
    {
        Vector3 halfSize = size * 0.5f;
        Vector3 lbf = center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
        Vector3 lbb = center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);
        Vector3 rbb = center + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
        Vector3 rbf = center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
        Vector3 ltf = center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z);
        Vector3 ltb = center + new Vector3(-halfSize.x, halfSize.y, halfSize.z);
        Vector3 rtb = center + new Vector3(halfSize.x, halfSize.y, halfSize.z);
        Vector3 rtf = center + new Vector3(halfSize.x, halfSize.y, -halfSize.z);

        Handles.color = color;
        Handles.DrawAAPolyLine(2.4f, new[] { lbf, lbb, rbb, rbf, lbf });
        Handles.DrawAAPolyLine(2.4f, new[] { ltf, ltb, rtb, rtf, ltf });
        Handles.DrawLine(lbf, ltf);
        Handles.DrawLine(lbb, ltb);
        Handles.DrawLine(rbb, rtb);
        Handles.DrawLine(rbf, rtf);

        if (!string.IsNullOrEmpty(label))
            Handles.Label(center + Vector3.up * Mathf.Max(0.4f, size.y * 0.55f), label);
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
