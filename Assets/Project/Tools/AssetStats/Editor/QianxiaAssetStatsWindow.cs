using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class QianxiaAssetStatsWindow : EditorWindow
{
    private enum PrefabAuditTopMetric
    {
        TotalVertexCount = 0,
        TrianglesPerUnitSurfaceArea = 1
    }

    [Serializable]
    private sealed class DerivedPrefabMetricsReport
    {
        public List<DerivedPrefabMetricsRecord> prefabs = new List<DerivedPrefabMetricsRecord>();
    }

    [Serializable]
    private sealed class DerivedPrefabMetricsRecord
    {
        public string prefabName = string.Empty;
        public string prefabPath = string.Empty;
        public long totalVertexCount;
        public List<DerivedLodTriangleRatioRecord> lodTriangleRatios = new List<DerivedLodTriangleRatioRecord>();
        public float trianglesPerUnitSurfaceArea;
    }

    [Serializable]
    private sealed class DerivedLodTriangleRatioRecord
    {
        public int lodIndex;
        public long triangleCount;
        public float ratioToHighestLod;
    }

    private sealed class TopPrefabEntry
    {
        public DerivedPrefabMetricsRecord Metrics;
        public List<GameObject> SceneInstanceRoots = new List<GameObject>();
        public int CurrentInstanceIndex;
        public int SceneInstanceCount;
        public long? HighestLodVertexCount;
    }

    private const string WindowTitle = "Loaded Scene Prefab Audit";

    [SerializeField] private QianxiaRuntimeAssetInstanceScanScope _sceneScanScope =
        QianxiaRuntimeAssetInstanceScanScope.AllLoadedScenes;
    [SerializeField] private bool _includeInactiveRenderers = true;
    [SerializeField] private bool _showSelectionBoundsOverlay = true;
    [SerializeField] private PrefabAuditTopMetric _topMetric = PrefabAuditTopMetric.TotalVertexCount;
    [SerializeField] private string _lastDerivedJsonPath = string.Empty;
    [SerializeField] private Vector2 _scrollPosition = Vector2.zero;
    [SerializeField] private string _statusMessage =
        "Scan currently loaded scenes and export prefab audit JSON.";
    [SerializeField] private string _topFiveStatusMessage =
        "Run Analyze Top 5 to compute the prefab ranking from the derived metrics JSON.";
    [SerializeField] private string _prefabNameQuery = string.Empty;
    [SerializeField] private string _prefabLookupStatusMessage =
        "Input a prefab name to inspect its model and texture cost.";

    private QianxiaRuntimeAssetInstanceSnapshot _lastSnapshot;
    private DerivedPrefabMetricsReport _lastDerivedReport;
    private QianxiaAssetStatsAnalyzer.PrefabLookupResult _prefabLookupResult;
    private readonly List<TopPrefabEntry> _topPrefabEntries = new List<TopPrefabEntry>();
    private readonly Dictionary<string, int> _selectedInstanceIndexByPrefabPath =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    [MenuItem("Tools/Qianxia/Asset Stats/Open Window")]
    public static void Open()
    {
        QianxiaAssetStatsWindow window = GetWindow<QianxiaAssetStatsWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(760.0f, 520.0f);
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent(WindowTitle);
        SceneView.duringSceneGui += OnSceneViewGui;
        RefreshSnapshot();
        TryReloadLastDerivedReport();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneViewGui;
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();

        using (EditorGUILayout.ScrollViewScope scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition))
        {
            _scrollPosition = scrollView.scrollPosition;
            DrawSettings();
            DrawSummary();
            DrawPrefabLookup();
            DrawTopPrefabs();
        }
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                RefreshSnapshot();

            using (new EditorGUI.DisabledScope(_lastSnapshot == null || _lastSnapshot.scannedSceneCount == 0))
            {
                if (GUILayout.Button("Analyze Top 5", EditorStyles.toolbarButton))
                    AnalyzeTopPrefabs();

                if (GUILayout.Button("Export Prefab JSON", EditorStyles.toolbarButton))
                    ExportPrefabJson();
            }
        }
    }

    private void DrawSettings()
    {
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Scan Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            QianxiaRuntimeAssetInstanceScanScope newSceneScanScope =
                (QianxiaRuntimeAssetInstanceScanScope)EditorGUILayout.EnumPopup(
                    "Scene Scope",
                    _sceneScanScope);
            bool newIncludeInactiveRenderers = EditorGUILayout.Toggle(
                "Include Inactive Renderers",
                _includeInactiveRenderers);
            bool newShowSelectionBoundsOverlay = EditorGUILayout.Toggle(
                "Show Bounds Overlay",
                _showSelectionBoundsOverlay);

            EditorGUILayout.HelpBox(
                "This unified tool scans Renderer components in loaded scenes, counts prefab usage, " +
                "and exports one raw JSON file where each prefab contains a compact prefab-level LOD summary. " +
                "After export, it also runs the Python post-process automatically and generates a separate *.derived.json metrics file. " +
                "When a scene instance is selected, the window can also draw its bounding box in Scene View.",
                MessageType.Info);

            if (EditorGUI.EndChangeCheck())
            {
                _sceneScanScope = newSceneScanScope;
                _includeInactiveRenderers = newIncludeInactiveRenderers;
                _showSelectionBoundsOverlay = newShowSelectionBoundsOverlay;
                RefreshSnapshot();
            }
        }
    }

    private void DrawSummary()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(_statusMessage, MessageType.None);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);

            if (_lastSnapshot == null)
            {
                EditorGUILayout.LabelField("No snapshot captured yet.");
                return;
            }

            int prefabAssetCount = _lastSnapshot.prefabs.Count(record =>
                record != null &&
                string.Equals(record.assetKind, "Prefab", StringComparison.OrdinalIgnoreCase));

            EditorGUILayout.LabelField(
                $"Loaded Scenes: {_lastSnapshot.loadedSceneCount:N0}    Scanned Scenes: {_lastSnapshot.scannedSceneCount:N0}");
            EditorGUILayout.LabelField(
                $"Renderers: {_lastSnapshot.rendererCount:N0}    Prefab Instances: {_lastSnapshot.prefabInstanceCount:N0}    Unique Prefabs: {prefabAssetCount:N0}");

            if (_lastSnapshot.scenes != null && _lastSnapshot.scenes.Count > 0)
            {
                string sceneSummary = string.Join(
                    " | ",
                    _lastSnapshot.scenes
                        .Select(scene => scene.sceneName)
                        .Where(sceneName => !string.IsNullOrWhiteSpace(sceneName))
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                EditorGUILayout.LabelField("Scenes", string.IsNullOrWhiteSpace(sceneSummary) ? "<None>" : sceneSummary);
            }

            EditorGUILayout.HelpBox(
                "The window only shows scan settings and summary. Detailed prefab, mesh, material, and texture data are exported through Prefab JSON.",
                MessageType.None);
        }
    }

    private void DrawPrefabLookup()
    {
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Prefab Lookup", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _prefabNameQuery = EditorGUILayout.TextField("Prefab Name", _prefabNameQuery);
                if (GUILayout.Button("Analyze", GUILayout.Width(96.0f)))
                    AnalyzePrefabLookup();
            }

            EditorGUILayout.LabelField(_prefabLookupStatusMessage, EditorStyles.wordWrappedLabel);

            if (_prefabLookupResult == null || _prefabLookupResult.Records.Count == 0)
                return;

            foreach (QianxiaAssetStatsAnalyzer.PrefabAuditJsonRecord record in _prefabLookupResult.Records)
                DrawPrefabLookupRecord(record);
        }
    }

    private void DrawPrefabLookupRecord(QianxiaAssetStatsAnalyzer.PrefabAuditJsonRecord record)
    {
        if (record == null)
            return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(record.prefabName) ? "<Unnamed Prefab>" : record.prefabName,
                    EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(record.prefabPath)))
                {
                    if (GUILayout.Button("Select", GUILayout.Width(72.0f)))
                        SelectProjectAsset(record.prefabPath);
                }
            }

            EditorGUILayout.LabelField("Path", string.IsNullOrWhiteSpace(record.prefabPath) ? "N/A" : record.prefabPath);
            EditorGUILayout.LabelField(
                "Cost",
                $"Scene x{record.sceneUsageCount:N0} | Renderers {record.rendererCount:N0} | Meshes {record.uniqueMeshCount:N0} | " +
                $"Tris {record.totalTriangleCount:N0} | Verts {record.totalVertexCount:N0} | Materials {record.uniqueMaterialCount:N0} | " +
                $"Textures {record.uniqueTextureCount:N0} | Texture Storage {record.totalTextureStorageSize}");
            EditorGUILayout.LabelField(
                "Disk",
                $"Mesh Files {record.totalMeshDiskSize} | Texture Files {record.totalTextureDiskSize}");

            if (record.notes.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", record.notes), MessageType.None);

            DrawPrefabMeshCostTable(record.meshes);
            DrawPrefabTextureCostTable(record.textures);
        }
    }

    private static void DrawPrefabMeshCostTable(IReadOnlyList<QianxiaAssetStatsAnalyzer.PrefabMeshAuditJsonRecord> meshes)
    {
        EditorGUILayout.Space(4.0f);
        EditorGUILayout.LabelField("Models", EditorStyles.boldLabel);

        if (meshes == null || meshes.Count == 0)
        {
            EditorGUILayout.LabelField("None");
            return;
        }

        foreach (QianxiaAssetStatsAnalyzer.PrefabMeshAuditJsonRecord mesh in meshes
                     .Where(mesh => mesh != null)
                     .OrderByDescending(mesh => mesh.triangleCount)
                     .ThenBy(mesh => mesh.meshName, StringComparer.OrdinalIgnoreCase)
                     .Take(12))
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        string.IsNullOrWhiteSpace(mesh.meshName) ? "<Unnamed Mesh>" : mesh.meshName,
                        EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        $"Tris {mesh.triangleCount:N0} | Verts {mesh.vertexCount:N0} | Scene x{mesh.currentSceneUsageCount:N0}",
                        GUILayout.Width(320.0f));
                }

                EditorGUILayout.LabelField(
                    "Materials",
                    mesh.materialNames.Count == 0 ? "N/A" : string.Join(" | ", mesh.materialNames.Take(6)));
                EditorGUILayout.LabelField(
                    "Path",
                    string.IsNullOrWhiteSpace(mesh.meshPath) ? "N/A" : mesh.meshPath);
            }
        }

        if (meshes.Count > 12)
            EditorGUILayout.LabelField($"+{meshes.Count - 12:N0} more model(s)");
    }

    private static void DrawPrefabTextureCostTable(IReadOnlyList<QianxiaAssetStatsAnalyzer.PrefabTextureAuditJsonRecord> textures)
    {
        EditorGUILayout.Space(4.0f);
        EditorGUILayout.LabelField("Textures", EditorStyles.boldLabel);

        if (textures == null || textures.Count == 0)
        {
            EditorGUILayout.LabelField("None");
            return;
        }

        foreach (QianxiaAssetStatsAnalyzer.PrefabTextureAuditJsonRecord texture in textures
                     .Where(texture => texture != null)
                     .OrderByDescending(texture => texture.storageSizeBytes)
                     .ThenBy(texture => texture.textureName, StringComparer.OrdinalIgnoreCase)
                     .Take(12))
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        string.IsNullOrWhiteSpace(texture.textureName) ? "<Unnamed Texture>" : texture.textureName,
                        EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        $"{texture.resolution} | {texture.format} | Storage {texture.storageSize} | Disk {texture.diskSize}",
                        GUILayout.Width(420.0f));
                }

                EditorGUILayout.LabelField(
                    "Roles",
                    texture.roles.Count == 0 ? "N/A" : string.Join(" | ", texture.roles));
                EditorGUILayout.LabelField(
                    "Materials",
                    texture.usedByMaterials.Count == 0 ? "N/A" : string.Join(" | ", texture.usedByMaterials.Take(6)));
                EditorGUILayout.LabelField(
                    "Path",
                    string.IsNullOrWhiteSpace(texture.texturePath) ? "N/A" : texture.texturePath);
            }
        }

        if (textures.Count > 12)
            EditorGUILayout.LabelField($"+{textures.Count - 12:N0} more texture(s)");
    }

    private void DrawTopPrefabs()
    {
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Top 5 Prefabs", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            PrefabAuditTopMetric newTopMetric = (PrefabAuditTopMetric)EditorGUILayout.EnumPopup(
                "Ranking Metric",
                _topMetric);
            if (EditorGUI.EndChangeCheck())
            {
                _topMetric = newTopMetric;
                RebuildTopPrefabEntries();
            }

            EditorGUILayout.HelpBox(
                "Analyze Top 5 uses the Python-derived metrics file. Click a prefab row to select one scene instance and frame it in Scene View.",
                MessageType.Info);

            EditorGUILayout.LabelField(_topFiveStatusMessage, EditorStyles.wordWrappedLabel);

            if (_topPrefabEntries.Count <= 0)
                return;

            for (int entryIndex = 0; entryIndex < _topPrefabEntries.Count; entryIndex++)
                DrawTopPrefabEntry(entryIndex, _topPrefabEntries[entryIndex]);
        }
    }

    private void DrawTopPrefabEntry(int entryIndex, TopPrefabEntry entry)
    {
        if (entry == null || entry.Metrics == null)
            return;

        string prefabName = string.IsNullOrWhiteSpace(entry.Metrics.prefabName)
            ? Path.GetFileNameWithoutExtension(entry.Metrics.prefabPath)
            : entry.Metrics.prefabName;
        string metricLabel = GetTopMetricLabel(_topMetric);
        string metricValue = FormatTopMetricValue(entry.Metrics, _topMetric);
        GameObject focusTarget = ResolveCurrentFocusTarget(entry);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"#{entryIndex + 1}", GUILayout.Width(28.0f));

                using (new EditorGUI.DisabledScope(focusTarget == null))
                {
                    if (GUILayout.Button(prefabName, EditorStyles.linkLabel))
                        FocusSceneObject(focusTarget);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(metricLabel, metricValue, GUILayout.Width(280.0f));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Scene Instances", entry.SceneInstanceCount.ToString("N0"));

                if (entry.SceneInstanceCount > 1)
                {
                    GUILayout.Space(8.0f);
                    GUILayout.Label(BuildInstanceIndexLabel(entry), GUILayout.Width(72.0f));

                    if (GUILayout.Button("Prev", GUILayout.Width(52.0f)))
                        ShiftFocusTarget(entry, -1);

                    if (GUILayout.Button("Next", GUILayout.Width(52.0f)))
                        ShiftFocusTarget(entry, 1);
                }
            }

            EditorGUILayout.LabelField(
                "Single Highest LOD Vertex Count",
                entry.HighestLodVertexCount.HasValue
                    ? entry.HighestLodVertexCount.Value.ToString("N0")
                    : "N/A");
        }
    }

    private void RefreshSnapshot()
    {
        _lastSnapshot = QianxiaRuntimeAssetInstanceScanner.Capture(new QianxiaRuntimeAssetInstanceScanOptions
        {
            scanScope = _sceneScanScope,
            includeInactiveRenderers = _includeInactiveRenderers
        });

        _statusMessage = BuildStatusMessage(_lastSnapshot);
        if (_lastDerivedReport != null)
            RebuildTopPrefabEntries();
        Repaint();
    }

    private string BuildStatusMessage(QianxiaRuntimeAssetInstanceSnapshot snapshot)
    {
        if (snapshot == null)
            return "No data was generated.";

        if (snapshot.scannedSceneCount == 0)
            return "No loaded scene matched the current scan scope.";

        int prefabAssetCount = snapshot.prefabs.Count(record =>
            record != null &&
            string.Equals(record.assetKind, "Prefab", StringComparison.OrdinalIgnoreCase));

        return
            $"Scanned {snapshot.scannedSceneCount:N0} loaded scene(s), {snapshot.rendererCount:N0} renderer(s), " +
            $"{snapshot.prefabInstanceCount:N0} prefab instance(s), and {prefabAssetCount:N0} unique prefab asset(s).";
    }

    private void ExportPrefabJson()
    {
        string directory = ResolveDefaultExportDirectory();
        string exportPath = EditorUtility.SaveFilePanel(
            "Export Loaded Scene Prefab Audit JSON",
            directory,
            BuildDefaultJsonFileName(),
            "json");

        if (string.IsNullOrWhiteSpace(exportPath))
            return;

        try
        {
            QianxiaAssetStatsAnalyzer.PrefabAuditExportResult exportResult =
                QianxiaAssetStatsAnalyzer.ExportPrefabAuditJsonWithDerivedMetrics(BuildQueryOptions(), exportPath);
            _statusMessage = exportResult.StatusMessage;
            if (exportResult.DerivedMetricsGenerated)
                LoadDerivedReport(exportResult.DerivedJsonPath);
            Repaint();
            EditorUtility.RevealInFinder(exportResult.DerivedMetricsGenerated
                ? exportResult.DerivedJsonPath
                : exportResult.RawJsonPath);
        }
        catch (Exception exception)
        {
            _statusMessage = $"Prefab audit JSON export failed: {exception.Message}";
            Debug.LogException(exception);
        }
    }

    private void AnalyzeTopPrefabs()
    {
        string analysisDirectory = ResolveAnalysisDirectory();
        Directory.CreateDirectory(analysisDirectory);
        string rawJsonPath = Path.Combine(
            analysisDirectory,
            $"{Path.GetFileNameWithoutExtension(BuildDefaultJsonFileName())}.analysis.json");

        try
        {
            QianxiaAssetStatsAnalyzer.PrefabAuditExportResult exportResult =
                QianxiaAssetStatsAnalyzer.ExportPrefabAuditJsonWithDerivedMetrics(BuildQueryOptions(), rawJsonPath);
            _statusMessage = exportResult.StatusMessage;

            if (!exportResult.DerivedMetricsGenerated)
            {
                _topPrefabEntries.Clear();
                _topFiveStatusMessage = "Top 5 analysis failed because the Python derived metrics file was not generated.";
                Repaint();
                return;
            }

            LoadDerivedReport(exportResult.DerivedJsonPath);
            _topFiveStatusMessage =
                $"Top 5 refreshed from {Path.GetFileName(exportResult.DerivedJsonPath)}. Run Analyze Top 5 again after scene content changes.";
            Repaint();
        }
        catch (Exception exception)
        {
            _topPrefabEntries.Clear();
            _topFiveStatusMessage = $"Top 5 analysis failed: {exception.Message}";
            Debug.LogException(exception);
        }
    }

    private void AnalyzePrefabLookup()
    {
        try
        {
            _prefabLookupResult = QianxiaAssetStatsAnalyzer.AnalyzePrefabByName(_prefabNameQuery, BuildQueryOptions());
            _prefabLookupStatusMessage = _prefabLookupResult != null
                ? _prefabLookupResult.StatusMessage
                : "Prefab lookup did not return a result.";
            Repaint();
        }
        catch (Exception exception)
        {
            _prefabLookupResult = null;
            _prefabLookupStatusMessage = $"Prefab lookup failed: {exception.Message}";
            Debug.LogException(exception);
        }
    }

    private void TryReloadLastDerivedReport()
    {
        if (string.IsNullOrWhiteSpace(_lastDerivedJsonPath) || !File.Exists(_lastDerivedJsonPath))
            return;

        LoadDerivedReport(_lastDerivedJsonPath);
    }

    private void LoadDerivedReport(string derivedJsonPath)
    {
        if (string.IsNullOrWhiteSpace(derivedJsonPath) || !File.Exists(derivedJsonPath))
        {
            _topPrefabEntries.Clear();
            _topFiveStatusMessage = "No derived metrics JSON file was found.";
            return;
        }

        string json = File.ReadAllText(derivedJsonPath);
        DerivedPrefabMetricsReport report = JsonUtility.FromJson<DerivedPrefabMetricsReport>(json);
        if (report == null)
        {
            _topPrefabEntries.Clear();
            _topFiveStatusMessage = "Failed to parse the derived metrics JSON.";
            return;
        }

        _lastDerivedReport = report;
        _lastDerivedJsonPath = derivedJsonPath;
        RebuildTopPrefabEntries();
    }

    private void RebuildTopPrefabEntries()
    {
        _topPrefabEntries.Clear();

        if (_lastDerivedReport == null || _lastDerivedReport.prefabs == null || _lastDerivedReport.prefabs.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(_topFiveStatusMessage))
            {
                _topFiveStatusMessage =
                    "Run Analyze Top 5 to compute the prefab ranking from the derived metrics JSON.";
            }

            return;
        }

        Dictionary<string, List<GameObject>> instanceRootsByPrefabPath = BuildScenePrefabInstanceRootsByPath();
        IEnumerable<DerivedPrefabMetricsRecord> orderedPrefabs = _lastDerivedReport.prefabs
            .Where(record => record != null)
            .OrderByDescending(record => ResolveMetricSortValue(record, _topMetric))
            .ThenBy(record => record.prefabName, StringComparer.OrdinalIgnoreCase)
            .Take(5);

        foreach (DerivedPrefabMetricsRecord metrics in orderedPrefabs)
        {
            string prefabPath = NormalizeAssetPath(metrics.prefabPath);
            instanceRootsByPrefabPath.TryGetValue(prefabPath, out List<GameObject> instanceRoots);
            List<GameObject> sanitizedInstanceRoots = (instanceRoots ?? new List<GameObject>())
                .Where(instanceRoot => instanceRoot != null)
                .OrderBy(instanceRoot => instanceRoot.scene.name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(BuildHierarchyPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int preferredIndex = ResolvePreferredInstanceIndex(sanitizedInstanceRoots);
            int selectedIndex = ResolveSelectedInstanceIndex(prefabPath, sanitizedInstanceRoots, preferredIndex);
            _topPrefabEntries.Add(new TopPrefabEntry
            {
                Metrics = metrics,
                SceneInstanceRoots = sanitizedInstanceRoots,
                SceneInstanceCount = sanitizedInstanceRoots.Count,
                CurrentInstanceIndex = selectedIndex,
                HighestLodVertexCount = ResolveSingleHighestLodVertexCount(metrics, sanitizedInstanceRoots.Count)
            });
        }

        if (_topPrefabEntries.Count > 0)
        {
            _topFiveStatusMessage =
                $"Showing top {_topPrefabEntries.Count} prefabs ranked by {GetTopMetricLabel(_topMetric)}.";
        }
        else
        {
            _topFiveStatusMessage = "The derived metrics JSON did not contain any prefab records.";
        }
    }

    private Dictionary<string, List<GameObject>> BuildScenePrefabInstanceRootsByPath()
    {
        Dictionary<string, List<GameObject>> lookup =
            new Dictionary<string, List<GameObject>>(StringComparer.OrdinalIgnoreCase);

        foreach (Scene scene in ResolveTargetScenes(_sceneScanScope))
        {
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            foreach (GameObject rootObject in scene.GetRootGameObjects())
                CollectPrefabInstanceRoots(rootObject.transform, lookup);
        }

        return lookup;
    }

    private static IEnumerable<Scene> ResolveTargetScenes(QianxiaRuntimeAssetInstanceScanScope scanScope)
    {
        if (scanScope == QianxiaRuntimeAssetInstanceScanScope.ActiveSceneOnly)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
                yield return activeScene;

            yield break;
        }

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (scene.IsValid() && scene.isLoaded)
                yield return scene;
        }
    }

    private static void CollectPrefabInstanceRoots(
        Transform transform,
        IDictionary<string, List<GameObject>> lookup)
    {
        if (transform == null || lookup == null)
            return;

        GameObject gameObject = transform.gameObject;
        if (PrefabUtility.IsAnyPrefabInstanceRoot(gameObject))
        {
            UnityEngine.Object sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            string prefabPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(sourceObject));
            if (!string.IsNullOrWhiteSpace(prefabPath))
            {
                if (!lookup.TryGetValue(prefabPath, out List<GameObject> instanceRoots))
                {
                    instanceRoots = new List<GameObject>();
                    lookup.Add(prefabPath, instanceRoots);
                }

                if (!instanceRoots.Contains(gameObject))
                    instanceRoots.Add(gameObject);
            }
        }

        foreach (Transform child in transform)
            CollectPrefabInstanceRoots(child, lookup);
    }

    private int ResolveSelectedInstanceIndex(
        string prefabPath,
        IReadOnlyList<GameObject> instanceRoots,
        int fallbackIndex)
    {
        if (instanceRoots == null || instanceRoots.Count == 0)
            return 0;

        int clampedFallback = Mathf.Clamp(fallbackIndex, 0, instanceRoots.Count - 1);
        if (string.IsNullOrWhiteSpace(prefabPath))
            return clampedFallback;

        if (_selectedInstanceIndexByPrefabPath.TryGetValue(prefabPath, out int selectedIndex))
            return Mathf.Clamp(selectedIndex, 0, instanceRoots.Count - 1);

        _selectedInstanceIndexByPrefabPath[prefabPath] = clampedFallback;
        return clampedFallback;
    }

    private static int ResolvePreferredInstanceIndex(IReadOnlyList<GameObject> instanceRoots)
    {
        if (instanceRoots == null || instanceRoots.Count == 0)
            return 0;

        Scene activeScene = SceneManager.GetActiveScene();
        for (int instanceIndex = 0; instanceIndex < instanceRoots.Count; instanceIndex++)
        {
            GameObject instanceRoot = instanceRoots[instanceIndex];
            if (instanceRoot != null && instanceRoot.scene == activeScene)
                return instanceIndex;
        }

        return 0;
    }

    private static GameObject ResolveCurrentFocusTarget(TopPrefabEntry entry)
    {
        if (entry == null || entry.SceneInstanceRoots == null || entry.SceneInstanceRoots.Count == 0)
            return null;

        int clampedIndex = Mathf.Clamp(entry.CurrentInstanceIndex, 0, entry.SceneInstanceRoots.Count - 1);
        entry.CurrentInstanceIndex = clampedIndex;
        return entry.SceneInstanceRoots[clampedIndex];
    }

    private void ShiftFocusTarget(TopPrefabEntry entry, int delta)
    {
        if (entry == null || entry.SceneInstanceRoots == null || entry.SceneInstanceRoots.Count <= 1)
            return;

        int count = entry.SceneInstanceRoots.Count;
        int nextIndex = (entry.CurrentInstanceIndex + delta) % count;
        if (nextIndex < 0)
            nextIndex += count;

        entry.CurrentInstanceIndex = nextIndex;
        string prefabPath = NormalizeAssetPath(entry.Metrics != null ? entry.Metrics.prefabPath : string.Empty);
        if (!string.IsNullOrWhiteSpace(prefabPath))
            _selectedInstanceIndexByPrefabPath[prefabPath] = nextIndex;

        FocusSceneObject(ResolveCurrentFocusTarget(entry));
    }

    private static string BuildInstanceIndexLabel(TopPrefabEntry entry)
    {
        if (entry == null || entry.SceneInstanceCount <= 1)
            return string.Empty;

        int displayIndex = Mathf.Clamp(entry.CurrentInstanceIndex, 0, entry.SceneInstanceCount - 1) + 1;
        return $"{displayIndex}/{entry.SceneInstanceCount}";
    }

    private static string BuildHierarchyPath(GameObject gameObject)
    {
        if (gameObject == null)
            return string.Empty;

        List<string> parts = new List<string>();
        Transform current = gameObject.transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private static long? ResolveSingleHighestLodVertexCount(
        DerivedPrefabMetricsRecord metrics,
        int sceneInstanceCount)
    {
        if (metrics == null || sceneInstanceCount <= 0)
            return null;

        return metrics.totalVertexCount / sceneInstanceCount;
    }

    private static void FocusSceneObject(GameObject target)
    {
        if (target == null)
            return;

        Selection.activeGameObject = target;
        EditorGUIUtility.PingObject(target);

        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
            SceneView.lastActiveSceneView.Focus();
        }
    }

    private static void SelectProjectAsset(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (asset == null)
            return;

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private void OnSceneViewGui(SceneView sceneView)
    {
        if (!_showSelectionBoundsOverlay)
            return;

        GameObject selection = Selection.activeGameObject;
        if (selection == null || !selection.scene.IsValid() || !selection.scene.isLoaded)
            return;

        if (!TryResolveWorldBounds(selection, out Bounds bounds))
            return;

        Color previousColor = Handles.color;
        CompareFunction previousZTest = Handles.zTest;
        Handles.color = new Color(0.18f, 0.85f, 1.0f, 0.95f);
        Handles.zTest = CompareFunction.LessEqual;
        Handles.DrawWireCube(bounds.center, bounds.size);

        Vector3 labelPosition = bounds.center + Vector3.up * Mathf.Max(0.08f, bounds.extents.y + 0.05f);
        string label = BuildBoundsLabel(bounds);
        Handles.Label(labelPosition, label, BuildBoundsLabelStyle());

        Handles.color = previousColor;
        Handles.zTest = previousZTest;
    }

    private string BuildDefaultJsonFileName()
    {
        string scope = _sceneScanScope == QianxiaRuntimeAssetInstanceScanScope.ActiveSceneOnly
            ? "active-scene"
            : "all-loaded-scenes";
        return $"loaded-scene-prefab-audit-{scope}.json";
    }

    private QianxiaAssetStatsAnalyzer.AssetStatsQueryOptions BuildQueryOptions()
    {
        return new QianxiaAssetStatsAnalyzer.AssetStatsQueryOptions
        {
            LoadedSceneScanScope = _sceneScanScope,
            IncludeInactiveRenderers = _includeInactiveRenderers
        };
    }

    private static string ResolveDefaultExportDirectory()
    {
        string projectRoot = Directory.GetCurrentDirectory();
        string workspaceDirectory = Path.Combine(projectRoot, ".workspace");
        return Directory.Exists(workspaceDirectory) ? workspaceDirectory : projectRoot;
    }

    private static string ResolveAnalysisDirectory()
    {
        return Path.Combine(ResolveDefaultExportDirectory(), "asset-stats-cache");
    }

    private static string GetTopMetricLabel(PrefabAuditTopMetric metric)
    {
        return metric switch
        {
            PrefabAuditTopMetric.TrianglesPerUnitSurfaceArea => "Triangles Per Unit Surface Area",
            _ => "Total Vertex Count"
        };
    }

    private static double ResolveMetricSortValue(DerivedPrefabMetricsRecord record, PrefabAuditTopMetric metric)
    {
        if (record == null)
            return double.MinValue;

        return metric switch
        {
            PrefabAuditTopMetric.TrianglesPerUnitSurfaceArea => record.trianglesPerUnitSurfaceArea,
            _ => record.totalVertexCount
        };
    }

    private static string FormatTopMetricValue(DerivedPrefabMetricsRecord record, PrefabAuditTopMetric metric)
    {
        if (record == null)
            return "N/A";

        return metric switch
        {
            PrefabAuditTopMetric.TrianglesPerUnitSurfaceArea => record.trianglesPerUnitSurfaceArea > 0.0f
                ? record.trianglesPerUnitSurfaceArea.ToString("0.###")
                : "N/A",
            _ => record.totalVertexCount.ToString("N0")
        };
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath)
            ? string.Empty
            : assetPath.Replace('\\', '/').Trim();
    }

    private static bool TryResolveWorldBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        bool hasBounds = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;

            Bounds rendererBounds = renderer.bounds;
            if (!hasBounds)
            {
                bounds = rendererBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds);
            }
        }

        return hasBounds;
    }

    private static string BuildBoundsLabel(Bounds bounds)
    {
        Vector3 size = bounds.size;
        float surfaceArea = 2.0f * ((size.x * size.y) + (size.x * size.z) + (size.y * size.z));
        return
            $"Bounds {size.x:0.###} x {size.y:0.###} x {size.z:0.###}\n" +
            $"Surface Area {surfaceArea:0.###}";
    }

    private static GUIStyle BuildBoundsLabelStyle()
    {
        GUIStyle style = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 11,
            richText = false,
            normal =
            {
                textColor = Color.white
            }
        };
        style.padding = new RectOffset(8, 8, 6, 6);
        return style;
    }
}
