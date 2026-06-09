using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum QianxiaRuntimeAssetInstanceScanScope
{
    ActiveSceneOnly = 0,
    AllLoadedScenes = 1
}

[Serializable]
public sealed class QianxiaRuntimeAssetInstanceScanOptions
{
    public QianxiaRuntimeAssetInstanceScanScope scanScope = QianxiaRuntimeAssetInstanceScanScope.AllLoadedScenes;
    public bool includeInactiveRenderers = true;
    public int prefabBreakdownLimit = 8;
}

[Serializable]
public sealed class QianxiaRuntimeAssetInstanceBreakdownRecord
{
    public string key = string.Empty;
    public string assetName = string.Empty;
    public string assetPath = string.Empty;
    public string assetKind = string.Empty;
    public int usageCount;
}

[Serializable]
public sealed class QianxiaRuntimeAssetInstanceAggregateRecord
{
    public string key = string.Empty;
    public string assetName = string.Empty;
    public string assetPath = string.Empty;
    public string assetKind = string.Empty;
    public int usageCount;
    public int uniqueSceneCount;
    public int uniquePrefabTypeCount;
    public int uniquePrefabInstanceCount;
    public List<string> sceneNames = new List<string>();
    public List<QianxiaRuntimeAssetInstanceBreakdownRecord> prefabBreakdown = new List<QianxiaRuntimeAssetInstanceBreakdownRecord>();
}

[Serializable]
public sealed class QianxiaRuntimeAssetInstanceSceneRecord
{
    public string sceneName = string.Empty;
    public string scenePath = string.Empty;
    public int rendererCount;
    public int prefabInstanceCount;
}

[Serializable]
public sealed class QianxiaRuntimeAssetInstanceSnapshot
{
    public int schemaVersion = 1;
    public string generatedAtUtc = string.Empty;
    public string generatedAtLocal = string.Empty;
    public int frameCount = -1;
    public bool isPlaying;
    public string scanScope = string.Empty;
    public bool includeInactiveRenderers;
    public bool prefabPathResolutionAvailable;
    public bool usedRuntimeRootFallback;
    public int loadedSceneCount;
    public int scannedSceneCount;
    public int rendererCount;
    public int prefabInstanceCount;
    public int meshRecordCount;
    public int materialRecordCount;
    public int prefabRecordCount;
    public List<string> notes = new List<string>();
    public List<QianxiaRuntimeAssetInstanceSceneRecord> scenes = new List<QianxiaRuntimeAssetInstanceSceneRecord>();
    public List<QianxiaRuntimeAssetInstanceAggregateRecord> meshes = new List<QianxiaRuntimeAssetInstanceAggregateRecord>();
    public List<QianxiaRuntimeAssetInstanceAggregateRecord> materials = new List<QianxiaRuntimeAssetInstanceAggregateRecord>();
    public List<QianxiaRuntimeAssetInstanceAggregateRecord> prefabs = new List<QianxiaRuntimeAssetInstanceAggregateRecord>();
}

public static class QianxiaRuntimeAssetInstanceScanner
{
    private sealed class AssetDescriptor
    {
        public AssetDescriptor(string key, string assetName, string assetPath, string assetKind)
        {
            Key = key ?? string.Empty;
            AssetName = assetName ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            AssetKind = assetKind ?? string.Empty;
        }

        public string Key { get; }
        public string AssetName { get; }
        public string AssetPath { get; }
        public string AssetKind { get; }
    }

    private sealed class PrefabInstanceContext
    {
        public PrefabInstanceContext(AssetDescriptor descriptor, int instanceId)
        {
            Descriptor = descriptor;
            InstanceId = instanceId;
        }

        public AssetDescriptor Descriptor { get; }
        public int InstanceId { get; }
    }

    private sealed class PrefabContributionAccumulator
    {
        public PrefabContributionAccumulator(AssetDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public AssetDescriptor Descriptor { get; }
        public int UsageCount;
    }

    private sealed class UsageAccumulator
    {
        public UsageAccumulator(AssetDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public AssetDescriptor Descriptor { get; }
        public int UsageCount;
        public readonly HashSet<string> SceneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> PrefabKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<int> PrefabInstanceIds = new HashSet<int>();
        public readonly Dictionary<string, PrefabContributionAccumulator> PrefabContributions =
            new Dictionary<string, PrefabContributionAccumulator>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SimpleAggregateAccumulator
    {
        public SimpleAggregateAccumulator(AssetDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public AssetDescriptor Descriptor { get; }
        public int UsageCount;
        public readonly HashSet<string> SceneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static QianxiaRuntimeAssetInstanceSnapshot Capture(QianxiaRuntimeAssetInstanceScanOptions options = null)
    {
        QianxiaRuntimeAssetInstanceScanOptions sanitizedOptions = SanitizeOptions(options);
        Dictionary<string, UsageAccumulator> meshUsage = new Dictionary<string, UsageAccumulator>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, UsageAccumulator> materialUsage = new Dictionary<string, UsageAccumulator>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SimpleAggregateAccumulator> prefabUsage = new Dictionary<string, SimpleAggregateAccumulator>(StringComparer.OrdinalIgnoreCase);

        DateTime now = DateTime.Now;
        QianxiaRuntimeAssetInstanceSnapshot snapshot = new QianxiaRuntimeAssetInstanceSnapshot
        {
            generatedAtLocal = now.ToString("O"),
            generatedAtUtc = now.ToUniversalTime().ToString("O"),
            frameCount = Application.isPlaying ? Time.frameCount : -1,
            isPlaying = Application.isPlaying,
            scanScope = sanitizedOptions.scanScope.ToString(),
            includeInactiveRenderers = sanitizedOptions.includeInactiveRenderers,
            loadedSceneCount = SceneManager.sceneCount,
            prefabPathResolutionAvailable = Application.isEditor,
            usedRuntimeRootFallback = !Application.isEditor
        };

        if (!snapshot.prefabPathResolutionAvailable)
        {
            snapshot.notes.Add(
                "Player build fallback is active. Prefab ownership groups by top-level runtime roots because prefab asset paths are not available outside the Editor.");
        }

        foreach (Scene scene in ResolveScenes(sanitizedOptions.scanScope))
        {
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            QianxiaRuntimeAssetInstanceSceneRecord sceneRecord = new QianxiaRuntimeAssetInstanceSceneRecord
            {
                sceneName = BuildSceneLabel(scene),
                scenePath = scene.path ?? string.Empty
            };

            snapshot.scannedSceneCount++;

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                RegisterPrefabRoots(rootObject.transform, sceneRecord, prefabUsage);

                foreach (Renderer renderer in rootObject.GetComponentsInChildren<Renderer>(sanitizedOptions.includeInactiveRenderers))
                {
                    RegisterRendererUsage(renderer, sceneRecord, meshUsage, materialUsage);
                }
            }

            snapshot.scenes.Add(sceneRecord);
            snapshot.rendererCount += sceneRecord.rendererCount;
            snapshot.prefabInstanceCount += sceneRecord.prefabInstanceCount;
        }

        snapshot.meshes = BuildUsageRecords(meshUsage, sanitizedOptions.prefabBreakdownLimit);
        snapshot.materials = BuildUsageRecords(materialUsage, sanitizedOptions.prefabBreakdownLimit);
        snapshot.prefabs = BuildSimpleRecords(prefabUsage);
        snapshot.meshRecordCount = snapshot.meshes.Count;
        snapshot.materialRecordCount = snapshot.materials.Count;
        snapshot.prefabRecordCount = snapshot.prefabs.Count;

        return snapshot;
    }

    private static QianxiaRuntimeAssetInstanceScanOptions SanitizeOptions(QianxiaRuntimeAssetInstanceScanOptions options)
    {
        QianxiaRuntimeAssetInstanceScanOptions sanitized = options ?? new QianxiaRuntimeAssetInstanceScanOptions();
        sanitized.prefabBreakdownLimit = Mathf.Max(1, sanitized.prefabBreakdownLimit);
        return sanitized;
    }

    private static IEnumerable<Scene> ResolveScenes(QianxiaRuntimeAssetInstanceScanScope scanScope)
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

    private static void RegisterPrefabRoots(
        Transform transform,
        QianxiaRuntimeAssetInstanceSceneRecord sceneRecord,
        IDictionary<string, SimpleAggregateAccumulator> prefabUsage)
    {
        if (transform == null)
            return;

#if UNITY_EDITOR
        if (TryResolvePrefabCountDescriptor(transform.gameObject, out AssetDescriptor prefabDescriptor))
        {
            RegisterSimpleUsage(prefabUsage, prefabDescriptor, sceneRecord.sceneName);
            sceneRecord.prefabInstanceCount++;
        }
        else if (transform.parent == null)
        {
            RegisterSimpleUsage(prefabUsage, BuildSceneRootDescriptor(transform.gameObject), sceneRecord.sceneName);
            sceneRecord.prefabInstanceCount++;
        }

        foreach (Transform child in transform)
            RegisterPrefabRoots(child, sceneRecord, prefabUsage);
#else
        if (transform.parent == null)
        {
            RegisterSimpleUsage(prefabUsage, BuildRuntimeRootDescriptor(transform.gameObject), sceneRecord.sceneName);
            sceneRecord.prefabInstanceCount++;
        }
#endif
    }

    private static void RegisterRendererUsage(
        Renderer renderer,
        QianxiaRuntimeAssetInstanceSceneRecord sceneRecord,
        IDictionary<string, UsageAccumulator> meshUsage,
        IDictionary<string, UsageAccumulator> materialUsage)
    {
        if (renderer == null || sceneRecord == null)
            return;

        sceneRecord.rendererCount++;
        PrefabInstanceContext ownerContext = ResolveOwnerContext(renderer.gameObject);

        Mesh sharedMesh = ResolveSharedMesh(renderer);
        if (sharedMesh != null)
        {
            RegisterUsage(
                meshUsage,
                BuildAssetDescriptor("Mesh", sharedMesh, sharedMesh.name),
                sceneRecord.sceneName,
                ownerContext);
        }

        Material[] sharedMaterials = renderer.sharedMaterials;
        if (sharedMaterials == null)
            return;

        for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
        {
            Material material = sharedMaterials[materialIndex];
            if (material == null)
                continue;

            RegisterUsage(
                materialUsage,
                BuildAssetDescriptor("Material", material, material.name),
                sceneRecord.sceneName,
                ownerContext);
        }
    }

    private static void RegisterUsage(
        IDictionary<string, UsageAccumulator> usage,
        AssetDescriptor descriptor,
        string sceneName,
        PrefabInstanceContext ownerContext)
    {
        if (usage == null || descriptor == null || string.IsNullOrWhiteSpace(descriptor.Key))
            return;

        if (!usage.TryGetValue(descriptor.Key, out UsageAccumulator accumulator))
        {
            accumulator = new UsageAccumulator(descriptor);
            usage.Add(descriptor.Key, accumulator);
        }

        accumulator.UsageCount++;
        AddSceneName(accumulator.SceneNames, sceneName);

        if (ownerContext?.Descriptor == null || string.IsNullOrWhiteSpace(ownerContext.Descriptor.Key))
            return;

        accumulator.PrefabKeys.Add(ownerContext.Descriptor.Key);
        accumulator.PrefabInstanceIds.Add(ownerContext.InstanceId);

        if (!accumulator.PrefabContributions.TryGetValue(ownerContext.Descriptor.Key, out PrefabContributionAccumulator contribution))
        {
            contribution = new PrefabContributionAccumulator(ownerContext.Descriptor);
            accumulator.PrefabContributions.Add(ownerContext.Descriptor.Key, contribution);
        }

        contribution.UsageCount++;
    }

    private static void RegisterSimpleUsage(
        IDictionary<string, SimpleAggregateAccumulator> usage,
        AssetDescriptor descriptor,
        string sceneName)
    {
        if (usage == null || descriptor == null || string.IsNullOrWhiteSpace(descriptor.Key))
            return;

        if (!usage.TryGetValue(descriptor.Key, out SimpleAggregateAccumulator accumulator))
        {
            accumulator = new SimpleAggregateAccumulator(descriptor);
            usage.Add(descriptor.Key, accumulator);
        }

        accumulator.UsageCount++;
        AddSceneName(accumulator.SceneNames, sceneName);
    }

    private static List<QianxiaRuntimeAssetInstanceAggregateRecord> BuildUsageRecords(
        IDictionary<string, UsageAccumulator> usage,
        int prefabBreakdownLimit)
    {
        List<QianxiaRuntimeAssetInstanceAggregateRecord> records = new List<QianxiaRuntimeAssetInstanceAggregateRecord>();
        if (usage == null)
            return records;

        foreach (UsageAccumulator accumulator in usage.Values)
        {
            QianxiaRuntimeAssetInstanceAggregateRecord record = new QianxiaRuntimeAssetInstanceAggregateRecord
            {
                key = accumulator.Descriptor.Key,
                assetName = accumulator.Descriptor.AssetName,
                assetPath = accumulator.Descriptor.AssetPath,
                assetKind = accumulator.Descriptor.AssetKind,
                usageCount = accumulator.UsageCount,
                uniqueSceneCount = accumulator.SceneNames.Count,
                uniquePrefabTypeCount = accumulator.PrefabKeys.Count,
                uniquePrefabInstanceCount = accumulator.PrefabInstanceIds.Count,
                sceneNames = accumulator.SceneNames
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            IEnumerable<PrefabContributionAccumulator> sortedContributions = accumulator.PrefabContributions.Values
                .OrderByDescending(contribution => contribution.UsageCount)
                .ThenBy(contribution => contribution.Descriptor.AssetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(contribution => contribution.Descriptor.AssetPath, StringComparer.OrdinalIgnoreCase)
                .Take(prefabBreakdownLimit);

            foreach (PrefabContributionAccumulator contribution in sortedContributions)
            {
                record.prefabBreakdown.Add(new QianxiaRuntimeAssetInstanceBreakdownRecord
                {
                    key = contribution.Descriptor.Key,
                    assetName = contribution.Descriptor.AssetName,
                    assetPath = contribution.Descriptor.AssetPath,
                    assetKind = contribution.Descriptor.AssetKind,
                    usageCount = contribution.UsageCount
                });
            }

            records.Add(record);
        }

        records.Sort(CompareAggregateRecords);
        return records;
    }

    private static List<QianxiaRuntimeAssetInstanceAggregateRecord> BuildSimpleRecords(
        IDictionary<string, SimpleAggregateAccumulator> usage)
    {
        List<QianxiaRuntimeAssetInstanceAggregateRecord> records = new List<QianxiaRuntimeAssetInstanceAggregateRecord>();
        if (usage == null)
            return records;

        foreach (SimpleAggregateAccumulator accumulator in usage.Values)
        {
            records.Add(new QianxiaRuntimeAssetInstanceAggregateRecord
            {
                key = accumulator.Descriptor.Key,
                assetName = accumulator.Descriptor.AssetName,
                assetPath = accumulator.Descriptor.AssetPath,
                assetKind = accumulator.Descriptor.AssetKind,
                usageCount = accumulator.UsageCount,
                uniqueSceneCount = accumulator.SceneNames.Count,
                sceneNames = accumulator.SceneNames
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        records.Sort(CompareAggregateRecords);
        return records;
    }

    private static int CompareAggregateRecords(
        QianxiaRuntimeAssetInstanceAggregateRecord left,
        QianxiaRuntimeAssetInstanceAggregateRecord right)
    {
        int usageCompare = right.usageCount.CompareTo(left.usageCount);
        if (usageCompare != 0)
            return usageCompare;

        int nameCompare = StringComparer.OrdinalIgnoreCase.Compare(left.assetName, right.assetName);
        if (nameCompare != 0)
            return nameCompare;

        return StringComparer.OrdinalIgnoreCase.Compare(left.assetPath, right.assetPath);
    }

    private static PrefabInstanceContext ResolveOwnerContext(GameObject gameObject)
    {
        if (gameObject == null)
            return null;

#if UNITY_EDITOR
        GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
        if (prefabRoot != null)
            return new PrefabInstanceContext(BuildPrefabDescriptor(prefabRoot), prefabRoot.GetInstanceID());

        GameObject sceneRoot = gameObject.transform.root != null ? gameObject.transform.root.gameObject : gameObject;
        return new PrefabInstanceContext(BuildSceneRootDescriptor(sceneRoot), sceneRoot.GetInstanceID());
#else
        GameObject runtimeRoot = gameObject.transform.root != null ? gameObject.transform.root.gameObject : gameObject;
        return new PrefabInstanceContext(BuildRuntimeRootDescriptor(runtimeRoot), runtimeRoot.GetInstanceID());
#endif
    }

#if UNITY_EDITOR
    private static bool TryResolvePrefabCountDescriptor(GameObject gameObject, out AssetDescriptor descriptor)
    {
        descriptor = null;
        if (gameObject == null || !PrefabUtility.IsAnyPrefabInstanceRoot(gameObject))
            return false;

        descriptor = BuildPrefabDescriptor(gameObject);
        return descriptor != null;
    }
#endif

    private static AssetDescriptor BuildPrefabDescriptor(GameObject prefabInstanceRoot)
    {
#if UNITY_EDITOR
        UnityEngine.Object sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(prefabInstanceRoot);
        string fallbackName = prefabInstanceRoot != null ? prefabInstanceRoot.name : "Prefab";
        string assetPath = ResolveAssetPath(sourceObject);
        string assetName = ResolveObjectName(sourceObject, fallbackName);
        int fallbackId = sourceObject != null ? sourceObject.GetInstanceID() : prefabInstanceRoot != null ? prefabInstanceRoot.GetInstanceID() : 0;
        return new AssetDescriptor(
            BuildStableKey("Prefab", assetPath, assetName, fallbackId),
            assetName,
            assetPath,
            "Prefab");
#else
        return BuildRuntimeRootDescriptor(prefabInstanceRoot);
#endif
    }

    private static AssetDescriptor BuildSceneRootDescriptor(GameObject sceneRoot)
    {
        Scene scene = sceneRoot != null ? sceneRoot.scene : default;
        string sceneDisplayPath = scene.path ?? string.Empty;
        string sceneIdentity = ResolveSceneIdentity(scene);
        string rootName = sceneRoot != null && !string.IsNullOrWhiteSpace(sceneRoot.name)
            ? sceneRoot.name
            : "<Scene Root>";
        string assetPath = !string.IsNullOrWhiteSpace(sceneDisplayPath) ? NormalizePath(sceneDisplayPath) : BuildSceneLabel(scene);
        return new AssetDescriptor(
            $"SceneRoot|{sceneIdentity}|{rootName}",
            rootName,
            assetPath,
            "SceneRoot");
    }

    private static AssetDescriptor BuildRuntimeRootDescriptor(GameObject runtimeRoot)
    {
        Scene scene = runtimeRoot != null ? runtimeRoot.scene : default;
        string sceneIdentity = ResolveSceneIdentity(scene);
        string rootName = runtimeRoot != null && !string.IsNullOrWhiteSpace(runtimeRoot.name)
            ? runtimeRoot.name
            : "<Runtime Root>";
        string assetPath = !string.IsNullOrWhiteSpace(scene.path)
            ? NormalizePath(scene.path)
            : BuildSceneLabel(scene);
        return new AssetDescriptor(
            $"RuntimeRoot|{sceneIdentity}|{rootName}",
            rootName,
            assetPath,
            "RuntimeRoot");
    }

    private static AssetDescriptor BuildAssetDescriptor(string assetKind, UnityEngine.Object asset, string fallbackName)
    {
        string assetPath = ResolveAssetPath(asset);
        string assetName = ResolveObjectName(asset, fallbackName);
        int fallbackId = asset != null ? asset.GetInstanceID() : 0;
        return new AssetDescriptor(
            BuildStableKey(assetKind, assetPath, assetName, fallbackId),
            assetName,
            assetPath,
            assetKind);
    }

    private static string BuildStableKey(string assetKind, string assetPath, string assetName, int fallbackId)
    {
        string normalizedPath = NormalizePath(assetPath);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
            return $"{assetKind}|{normalizedPath}|{assetName}";

        return $"{assetKind}|runtime:{fallbackId}|{assetName}";
    }

    private static string ResolveObjectName(UnityEngine.Object asset, string fallbackName)
    {
        if (asset != null && !string.IsNullOrWhiteSpace(asset.name))
            return asset.name;

        return string.IsNullOrWhiteSpace(fallbackName) ? "<Unnamed>" : fallbackName;
    }

    private static string ResolveAssetPath(UnityEngine.Object asset)
    {
#if UNITY_EDITOR
        if (asset == null)
            return string.Empty;

        return NormalizePath(AssetDatabase.GetAssetPath(asset));
#else
        return string.Empty;
#endif
    }

    private static string ResolveSceneIdentity(Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene.path))
            return NormalizePath(scene.path);

        string sceneName = BuildSceneLabel(scene);
        return $"{sceneName}#{scene.handle}";
    }

    private static string BuildSceneLabel(Scene scene)
    {
        if (!scene.IsValid())
            return "<Invalid Scene>";

        return string.IsNullOrWhiteSpace(scene.name) ? "<Unnamed Scene>" : scene.name;
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim();
    }

    private static void AddSceneName(ICollection<string> sceneNames, string sceneName)
    {
        if (sceneNames == null || string.IsNullOrWhiteSpace(sceneName))
            return;

        sceneNames.Add(sceneName);
    }

    private static Mesh ResolveSharedMesh(Renderer renderer)
    {
        switch (renderer)
        {
            case SkinnedMeshRenderer skinnedMeshRenderer:
                return skinnedMeshRenderer.sharedMesh;
            case MeshRenderer meshRenderer:
                MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
                return meshFilter != null ? meshFilter.sharedMesh : null;
            case ParticleSystemRenderer particleSystemRenderer:
                return particleSystemRenderer.mesh;
            default:
                return null;
        }
    }
}

[AddComponentMenu("Qianxia/Tools/Runtime Asset Instance Counter")]
[DisallowMultipleComponent]
public sealed class QianxiaRuntimeAssetInstanceCounter : MonoBehaviour
{
    [Header("Scan")]
    [SerializeField] private QianxiaRuntimeAssetInstanceScanScope _scanScope = QianxiaRuntimeAssetInstanceScanScope.AllLoadedScenes;
    [SerializeField] private bool _includeInactiveRenderers = true;
    [SerializeField] private int _prefabBreakdownLimit = 8;

    [Header("Automation")]
    [SerializeField] private bool _captureOnStart = true;
    [SerializeField] private bool _captureOnSceneLoaded;
    [SerializeField] private bool _logSummaryAfterCapture = true;
    [SerializeField] private bool _exportJsonAfterCapture;

    [Header("Export")]
    [SerializeField] private string _exportDirectoryOverride = string.Empty;
    [SerializeField] private string _exportFilePrefix = "runtime_asset_instances";
    [SerializeField] private int _summaryRowLimit = 10;

    [NonSerialized] private QianxiaRuntimeAssetInstanceSnapshot _lastSnapshot;
    [NonSerialized] private string _lastSummary = "No snapshot captured yet.";
    [NonSerialized] private string _lastExportPath = string.Empty;

    public QianxiaRuntimeAssetInstanceSnapshot LastSnapshot => _lastSnapshot;
    public string LastSummary => string.IsNullOrWhiteSpace(_lastSummary) ? "No snapshot captured yet." : _lastSummary;
    public string LastExportPath => _lastExportPath ?? string.Empty;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (_captureOnStart)
            CaptureAndHandleOutputs("start");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (!_captureOnSceneLoaded || !Application.isPlaying)
            return;

        CaptureAndHandleOutputs($"scene-loaded:{scene.name}");
    }

    [ContextMenu("Capture Runtime Asset Snapshot")]
    public void CaptureSnapshot()
    {
        _lastSnapshot = QianxiaRuntimeAssetInstanceScanner.Capture(BuildScanOptions());
        _lastSummary = BuildSummary(_lastSnapshot, Mathf.Max(1, _summaryRowLimit));
    }

    [ContextMenu("Log Runtime Asset Summary")]
    public void LogLastSummary()
    {
        EnsureSnapshot();
        Debug.Log(LastSummary, this);
    }

    [ContextMenu("Export Runtime Asset Snapshot Json")]
    public void ExportLastSnapshotJson()
    {
        if (TryExportLastSnapshot(out string exportPath, out string errorMessage))
        {
            Debug.Log($"Runtime asset snapshot exported to: {exportPath}", this);
            return;
        }

        Debug.LogError(errorMessage, this);
    }

    public bool TryExportLastSnapshot(out string exportPath, out string errorMessage)
    {
        EnsureSnapshot();

        exportPath = string.Empty;
        errorMessage = string.Empty;
        if (_lastSnapshot == null)
        {
            errorMessage = "Runtime asset snapshot is unavailable.";
            return false;
        }

        string directory = ResolveExportDirectory();
        Directory.CreateDirectory(directory);

        string fileName = BuildExportFileName(_lastSnapshot);
        exportPath = Path.Combine(directory, fileName);
        File.WriteAllText(exportPath, JsonUtility.ToJson(_lastSnapshot, true), Encoding.UTF8);
        _lastExportPath = exportPath;
        return true;
    }

    public QianxiaRuntimeAssetInstanceScanOptions BuildScanOptions()
    {
        return new QianxiaRuntimeAssetInstanceScanOptions
        {
            scanScope = _scanScope,
            includeInactiveRenderers = _includeInactiveRenderers,
            prefabBreakdownLimit = Mathf.Max(1, _prefabBreakdownLimit)
        };
    }

    public static string BuildSummary(QianxiaRuntimeAssetInstanceSnapshot snapshot, int rowLimit)
    {
        if (snapshot == null)
            return "No snapshot captured yet.";

        int safeRowLimit = Mathf.Max(1, rowLimit);
        StringBuilder builder = new StringBuilder(512);
        builder.AppendLine(
            $"Scenes {snapshot.scannedSceneCount}/{snapshot.loadedSceneCount} | Renderers {snapshot.rendererCount} | Prefab Roots {snapshot.prefabInstanceCount}");
        builder.AppendLine(
            $"Meshes {snapshot.meshRecordCount} | Materials {snapshot.materialRecordCount} | Prefabs {snapshot.prefabRecordCount}");

        if (snapshot.notes.Count > 0)
        {
            for (int noteIndex = 0; noteIndex < snapshot.notes.Count; noteIndex++)
                builder.AppendLine($"Note: {snapshot.notes[noteIndex]}");
        }

        AppendRecordSection(builder, "Top Meshes", snapshot.meshes, safeRowLimit, true);
        AppendRecordSection(builder, "Top Materials", snapshot.materials, safeRowLimit, true);
        AppendRecordSection(builder, "Top Prefabs", snapshot.prefabs, safeRowLimit, false);
        return builder.ToString().TrimEnd();
    }

    private static void AppendRecordSection(
        StringBuilder builder,
        string title,
        IReadOnlyList<QianxiaRuntimeAssetInstanceAggregateRecord> records,
        int rowLimit,
        bool includeOwnerCount)
    {
        builder.AppendLine(title);
        if (records == null || records.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        int count = Mathf.Min(rowLimit, records.Count);
        for (int index = 0; index < count; index++)
        {
            QianxiaRuntimeAssetInstanceAggregateRecord record = records[index];
            string pathSuffix = string.IsNullOrWhiteSpace(record.assetPath) ? string.Empty : $" | {record.assetPath}";
            string ownerSuffix = includeOwnerCount ? $" | owners {record.uniquePrefabTypeCount}" : string.Empty;
            builder.AppendLine(
                $"  {index + 1}. {record.assetName} x{record.usageCount} | scenes {record.uniqueSceneCount}{ownerSuffix}{pathSuffix}");
        }
    }

    private void CaptureAndHandleOutputs(string reason)
    {
        CaptureSnapshot();

        if (_logSummaryAfterCapture)
            Debug.Log($"[{name}] runtime asset snapshot ({reason})\n{LastSummary}", this);

        if (!_exportJsonAfterCapture)
            return;

        bool exported = TryExportLastSnapshot(out string exportPath, out string errorMessage);
        if (exported)
            Debug.Log($"[{name}] runtime asset snapshot exported to: {exportPath}", this);
        else
            Debug.LogError($"[{name}] failed to export runtime asset snapshot: {errorMessage}", this);
    }

    private void EnsureSnapshot()
    {
        if (_lastSnapshot == null)
            CaptureSnapshot();
    }

    private string ResolveExportDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_exportDirectoryOverride))
            return Path.GetFullPath(_exportDirectoryOverride);

#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        return Path.Combine(projectRoot, ".workspace", "artifacts", "runtime-asset-stats");
#else
        return Path.Combine(Application.persistentDataPath, "runtime-asset-stats");
#endif
    }

    private string BuildExportFileName(QianxiaRuntimeAssetInstanceSnapshot snapshot)
    {
        string prefix = string.IsNullOrWhiteSpace(_exportFilePrefix) ? "runtime_asset_instances" : _exportFilePrefix.Trim();
        string sceneToken = snapshot != null && snapshot.scenes.Count > 0
            ? SanitizeToken(snapshot.scenes[0].sceneName)
            : "noscene";
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"{prefix}_{timestamp}_{sceneToken}.json";
    }

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        StringBuilder builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length == 0 || builder[builder.Length - 1] != '-')
                builder.Append('-');
        }

        string sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
