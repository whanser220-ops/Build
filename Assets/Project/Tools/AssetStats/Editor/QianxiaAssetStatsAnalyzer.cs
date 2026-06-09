using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

internal enum AssetStatsScanScope
{
    Selection,
    AllProjectAssets
}

internal enum AssetStatsReportKind
{
    Overview,
    Mesh,
    Texture,
    Material
}

internal enum AssetStatsTeamFilter
{
    All,
    Environment,
    Character,
    Vfx,
    Level
}

[Flags]
internal enum AssetStatsTeamFlags
{
    None = 0,
    Environment = 1 << 0,
    Character = 1 << 1,
    Vfx = 1 << 2
}

internal static class QianxiaAssetStatsAnalyzer
{
    private const int DefaultDerivedMetricsTopCount = 25;
    private static readonly string[] EnvironmentTokens =
    {
        "scene", "scenes", "map", "maps", "level", "levels", "environment", "terrain", "landscape",
        "building", "buildings", "architecture", "house", "village", "rock", "rocks", "stone", "stones",
        "grass", "tree", "trees", "plant", "plants", "leaf", "leaves", "flower", "flowers", "bush",
        "bushes", "vegetation", "foliage", "forest", "mountain", "mountains", "hill", "hills",
        "mushroom", "mushrooms", "prop", "props", "meadow", "coverplants", "background", "terrainlayer"
    };

    private static readonly string[] CharacterTokens =
    {
        "character", "characters", "crowd", "crowds", "qianxia", "avatar", "avatars", "hero", "heroes",
        "npc", "npcs", "monster", "monsters", "enemy", "enemies", "creature", "creatures", "weapon",
        "weapons", "equip", "equipment", "armor", "armour", "outfit", "outfits", "rig", "rigs",
        "humanoid", "humanoids", "skeleton", "skeletons", "body", "bodies"
    };

    private static readonly string[] VfxTokens =
    {
        "vfx", "fx", "effect", "effects", "particle", "particles", "trail", "trails", "spark",
        "sparks", "smoke", "fire", "flame", "explosion", "explosions", "wind", "lightning", "magic",
        "glow", "glows", "dust", "firefly", "butterfly"
    };

    internal sealed class AssetStatsQueryOptions
    {
        public AssetStatsScanScope ScanScope = AssetStatsScanScope.AllProjectAssets;
        public AssetStatsReportKind ReportKind = AssetStatsReportKind.Overview;
        public AssetStatsTeamFilter TeamFilter = AssetStatsTeamFilter.All;
        public int MinimumSceneUsageCount = 5;
        public QianxiaRuntimeAssetInstanceScanScope LoadedSceneScanScope = QianxiaRuntimeAssetInstanceScanScope.AllLoadedScenes;
        public bool IncludeInactiveRenderers = true;
    }

    internal sealed class AssetStatsAnalysisResult
    {
        public readonly List<AssetStatsRecord> Records = new List<AssetStatsRecord>();
        public AssetStatsQueryOptions Options = new AssetStatsQueryOptions();
        public CurrentSceneUsageData SceneUsage = new CurrentSceneUsageData();
        public int ScannedAssetCount;
    }

    [Serializable]
    internal sealed class PrefabAuditJsonExport
    {
        public int schemaVersion = 6;
        public string generatedAtUtc = string.Empty;
        public string exportKind = "PrefabAssetAudit";
        public string scanScope = string.Empty;
        public bool includeInactiveRenderers;
        public int loadedSceneCount;
        public int scannedSceneCount;
        public int rendererCount;
        public int prefabInstanceCount;
        public int prefabRecordCount;
        public List<string> sceneNames = new List<string>();
        public List<PrefabAuditJsonRecord> prefabs = new List<PrefabAuditJsonRecord>();
    }

    [Serializable]
    internal sealed class PrefabAuditJsonRecord
    {
        public string prefabName = string.Empty;
        public string prefabPath = string.Empty;
        public int sceneUsageCount;
        public int rendererCount;
        public int uniqueMeshCount;
        public int uniqueMaterialCount;
        public int uniqueTextureCount;
        public int materialSlotCount;
        public long totalTriangleCount;
        public long totalVertexCount;
        public long totalMeshDiskSizeBytes;
        public string totalMeshDiskSize = string.Empty;
        public long totalTextureStorageSizeBytes;
        public string totalTextureStorageSize = string.Empty;
        public long totalTextureDiskSizeBytes;
        public string totalTextureDiskSize = string.Empty;
        public float boundsSurfaceArea;
        public bool hasLod;
        public List<PrefabLodAuditJsonRecord> lods = new List<PrefabLodAuditJsonRecord>();
        public List<PrefabMeshAuditJsonRecord> meshes = new List<PrefabMeshAuditJsonRecord>();
        public List<PrefabMaterialAuditJsonRecord> materials = new List<PrefabMaterialAuditJsonRecord>();
        public List<PrefabTextureAuditJsonRecord> textures = new List<PrefabTextureAuditJsonRecord>();
        public List<string> notes = new List<string>();
    }

    internal sealed class PrefabAuditExportResult
    {
        public string RawJsonPath = string.Empty;
        public string DerivedJsonPath = string.Empty;
        public bool DerivedMetricsGenerated;
        public string StatusMessage = string.Empty;
    }

    [Serializable]
    internal sealed class PrefabMeshAuditJsonRecord
    {
        public string meshName = string.Empty;
        public string meshPath = string.Empty;
        public long triangleCount;
        public long vertexCount;
        public long diskSizeBytes;
        public string diskSize = string.Empty;
        public int currentSceneUsageCount;
        public int materialCount;
        public int textureCount;
        public bool hasLod;
        public List<string> materialNames = new List<string>();
        public List<PrefabLodAuditJsonRecord> lods = new List<PrefabLodAuditJsonRecord>();
    }

    [Serializable]
    internal sealed class PrefabLodAuditJsonRecord
    {
        public int lodIndex;
        public long triangleCount;
        public long vertexCount;
    }

    [Serializable]
    internal sealed class PrefabMaterialAuditJsonRecord
    {
        public string materialName = string.Empty;
        public string materialPath = string.Empty;
        public string shaderName = string.Empty;
        public int textureCount;
        public string textureRoleSummary = string.Empty;
        public long diskSizeBytes;
        public string diskSize = string.Empty;
        public int currentSceneUsageCount;
        public List<PrefabMaterialTextureReferenceJsonRecord> textureReferences = new List<PrefabMaterialTextureReferenceJsonRecord>();
        public List<string> notes = new List<string>();
    }

    [Serializable]
    internal sealed class PrefabMaterialTextureReferenceJsonRecord
    {
        public string textureName = string.Empty;
        public string texturePath = string.Empty;
        public string role = string.Empty;
        public string propertyName = string.Empty;
    }

    [Serializable]
    internal sealed class PrefabTextureAuditJsonRecord
    {
        public string textureName = string.Empty;
        public string texturePath = string.Empty;
        public int width;
        public int height;
        public string resolution = string.Empty;
        public string format = string.Empty;
        public int mipCount;
        public bool hasMipmaps;
        public long storageSizeBytes;
        public string storageSize = string.Empty;
        public long diskSizeBytes;
        public string diskSize = string.Empty;
        public List<string> roles = new List<string>();
        public int materialCount;
        public bool is4KOrAbove;
        public bool is8KOrAbove;
        public List<string> usedByMaterials = new List<string>();
        public List<string> materialPropertyUsages = new List<string>();
        public List<string> notes = new List<string>();
    }

    internal sealed class PrefabLookupResult
    {
        public string Query = string.Empty;
        public int MatchCount;
        public int ExactMatchCount;
        public string StatusMessage = string.Empty;
        public readonly List<PrefabAuditJsonRecord> Records = new List<PrefabAuditJsonRecord>();
    }

    internal sealed class AssetStatsRecord
    {
        public UnityEngine.Object Asset;
        public string AssetName = string.Empty;
        public string AssetPath = string.Empty;
        public string AssetKind = string.Empty;
        public long DiskSizeBytes;
        public int UniqueMeshCount;
        public long TriangleCount;
        public long VertexCount;
        public int LodCount;
        public string LodIndicesSummary = string.Empty;
        public int MaterialSlotCount;
        public int UniqueMaterialCount;
        public int TextureCount;
        public int RendererCount;
        public AssetStatsTeamFlags TeamFlags;
        public int CurrentSceneUsageCount;
        public string ShaderName = string.Empty;
        public int TextureWidth;
        public int TextureHeight;
        public string TextureFormat = string.Empty;
        public int TextureMipCount;
        public bool TextureHasMipmaps;
        public long TextureStorageSizeBytes;
        public int AlbedoTextureCount;
        public int NormalTextureCount;
        public int MaskTextureCount;
        public int OtherTextureCount;
        public readonly List<string> MaterialNames = new List<string>();
        public readonly List<TextureStatsRecord> Textures = new List<TextureStatsRecord>();
        public readonly List<string> UsedBySceneNames = new List<string>();
        public readonly List<string> UsedByActorNames = new List<string>();
        public readonly List<string> UsedByAssetNames = new List<string>();
        public readonly List<string> UsedByMaterialNames = new List<string>();
        public readonly List<string> Notes = new List<string>();

        public string DiskSizeLabel => FormatBytes(DiskSizeBytes);
        public string TextureStorageSizeLabel => FormatBytes(TextureStorageSizeBytes);

        public string BuildMaterialSummary()
        {
            return MaterialNames.Count == 0 ? string.Empty : string.Join(" | ", MaterialNames);
        }

        public string BuildTextureSummary()
        {
            if (Textures.Count == 0)
                return string.Empty;

            return string.Join(" | ", Textures.Select(texture => texture.BuildSummary()));
        }

        public string BuildNoteSummary()
        {
            return Notes.Count == 0 ? string.Empty : string.Join(" | ", Notes);
        }

        public string BuildTeamSummary()
        {
            return FormatTeamFlags(TeamFlags);
        }

        public string BuildTextureRoleSummary()
        {
            List<string> roles = new List<string>(4);
            if (AlbedoTextureCount > 0)
                roles.Add($"Albedo:{AlbedoTextureCount}");
            if (NormalTextureCount > 0)
                roles.Add($"Normal:{NormalTextureCount}");
            if (MaskTextureCount > 0)
                roles.Add($"Mask:{MaskTextureCount}");
            if (OtherTextureCount > 0)
                roles.Add($"Other:{OtherTextureCount}");
            return roles.Count == 0 ? string.Empty : string.Join(" | ", roles);
        }

        public string BuildUsedBySceneSummary()
        {
            return BuildCompactListSummary(UsedBySceneNames);
        }

        public string BuildUsedByActorSummary()
        {
            return BuildCompactListSummary(UsedByActorNames);
        }

        public string BuildUsedByAssetSummary()
        {
            return BuildCompactListSummary(UsedByAssetNames);
        }

        public string BuildUsedByMaterialSummary()
        {
            return BuildCompactListSummary(UsedByMaterialNames);
        }
    }

    internal sealed class TextureStatsRecord
    {
        public Texture Texture;
        public string TextureName = string.Empty;
        public string AssetPath = string.Empty;
        public int Width;
        public int Height;
        public string Format = string.Empty;
        public int MipCount;
        public bool HasMipmaps;
        public long DiskSizeBytes;
        public long StorageSizeBytes;
        public string Role = string.Empty;
        public readonly List<string> Usages = new List<string>();

        public string DiskSizeLabel => FormatBytes(DiskSizeBytes);
        public string StorageSizeLabel => FormatBytes(StorageSizeBytes);

        public string BuildSummary()
        {
            string usageSummary = Usages.Count == 0 ? string.Empty : $" [{string.Join(", ", Usages)}]";
            string roleSummary = string.IsNullOrEmpty(Role) ? string.Empty : $" {Role}";
            return $"{TextureName} {Width}x{Height}{roleSummary}{usageSummary}";
        }
    }

    internal sealed class CurrentSceneUsageData
    {
        public string SceneName = string.Empty;
        public string ScenePath = string.Empty;
        public bool IsSceneLoaded;
        public readonly Dictionary<string, int> GameObjectUsageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> MeshUsageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> MaterialUsageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> TextureUsageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _trackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasTrackedUsage =>
            GameObjectUsageCounts.Count > 0 ||
            MeshUsageCounts.Count > 0 ||
            MaterialUsageCounts.Count > 0 ||
            TextureUsageCounts.Count > 0;

        public IEnumerable<string> GetTrackedAssetPaths()
        {
            return _trackedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        public void AddTrackedPath(string assetPath)
        {
            if (!string.IsNullOrWhiteSpace(assetPath))
                _trackedPaths.Add(assetPath);
        }

        public int GetUsageCount(UnityEngine.Object asset, string assetPath)
        {
            if (asset == null || string.IsNullOrWhiteSpace(assetPath))
                return 0;

            switch (asset)
            {
                case GameObject:
                    return GetCount(GameObjectUsageCounts, assetPath);
                case Mesh mesh:
                    return GetCount(MeshUsageCounts, BuildObjectKey(assetPath, mesh.name));
                case Material material:
                    return GetCount(MaterialUsageCounts, BuildObjectKey(assetPath, material.name));
                case Texture:
                    return GetCount(TextureUsageCounts, assetPath);
                case SceneAsset:
                    return string.Equals(ScenePath, assetPath, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                default:
                    return 0;
            }
        }

        private static int GetCount(IReadOnlyDictionary<string, int> counts, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return 0;

            return counts.TryGetValue(key, out int value) ? value : 0;
        }
    }

    private sealed class MeshReferenceContext
    {
        public readonly HashSet<int> MeshIds = new HashSet<int>();
        public readonly HashSet<int> MaterialIds = new HashSet<int>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public readonly List<Material> UniqueMaterials = new List<Material>();
        public int MaterialSlotCount;
        public int RendererCount;
    }

    private sealed class TextureAggregate
    {
        public TextureAggregate(Texture texture, string assetPath, string role)
        {
            Texture = texture;
            AssetPath = assetPath ?? string.Empty;
            Role = role ?? string.Empty;
        }

        public Texture Texture { get; }
        public string AssetPath { get; }
        public string Role { get; }
        public readonly HashSet<string> UsageSet = new HashSet<string>(StringComparer.Ordinal);
    }

    private sealed class UsageInfo
    {
        public readonly HashSet<string> SceneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> ActorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> AssetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> MaterialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<int> LodIndices = new HashSet<int>();
        public int MaxLodCount;
    }

    private sealed class AssetUsageIndex
    {
        public readonly Dictionary<string, UsageInfo> MeshUsage = new Dictionary<string, UsageInfo>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, UsageInfo> MaterialUsage = new Dictionary<string, UsageInfo>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, UsageInfo> TextureUsage = new Dictionary<string, UsageInfo>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class MaterialTextureReference
    {
        public Texture Texture;
        public string TexturePath = string.Empty;
        public string PropertyName = string.Empty;
        public string Role = string.Empty;
    }

    private sealed class MaterialTextureProfile
    {
        public readonly List<MaterialTextureReference> References = new List<MaterialTextureReference>();
        public int AlbedoTextureCount;
        public int NormalTextureCount;
        public int MaskTextureCount;
        public int OtherTextureCount;
    }

    private sealed class PrefabTextureAggregateData
    {
        public Texture Texture;
        public string TexturePath = string.Empty;
        public readonly HashSet<string> Roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> MaterialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> PropertyUsages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PrefabSceneAuditContext
    {
        public QianxiaRuntimeAssetInstanceSnapshot Snapshot;
        public readonly Dictionary<string, QianxiaRuntimeAssetInstanceAggregateRecord> PrefabRecordsByKey =
            new Dictionary<string, QianxiaRuntimeAssetInstanceAggregateRecord>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Dictionary<string, int>> MeshUsageByPrefabKey =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Dictionary<string, int>> MaterialUsageByPrefabKey =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PrefabLodSummary
    {
        public bool HasLod;
        public readonly List<PrefabLodAuditJsonRecord> PrefabLods = new List<PrefabLodAuditJsonRecord>();
        public readonly Dictionary<string, List<PrefabLodAuditJsonRecord>> MeshLodsByMeshKey =
            new Dictionary<string, List<PrefabLodAuditJsonRecord>>(StringComparer.OrdinalIgnoreCase);
    }

    private static System.Reflection.MethodInfo s_textureStorageSizeMethod;

    public static AssetStatsAnalysisResult Analyze(AssetStatsQueryOptions options)
    {
        AssetStatsQueryOptions sanitizedOptions = SanitizeOptions(options);
        CurrentSceneUsageData sceneUsage = BuildCurrentSceneUsage();
        AssetUsageIndex usageIndex = BuildUsageIndex(sanitizedOptions);
        List<UnityEngine.Object> assets = ResolveAssets(sanitizedOptions, sceneUsage);
        List<AssetStatsRecord> records = AnalyzeAssets(assets, sceneUsage, usageIndex);

        records = records
            .Where(record => MatchesTeamFilter(record, sanitizedOptions, sceneUsage))
            .Where(record => MatchesReportKind(record, sanitizedOptions.ReportKind))
            .ToList();

        SortRecords(records, sanitizedOptions);

        AssetStatsAnalysisResult result = new AssetStatsAnalysisResult
        {
            Options = sanitizedOptions,
            SceneUsage = sceneUsage,
            ScannedAssetCount = assets.Count
        };

        result.Records.AddRange(records);
        return result;
    }

    public static string BuildCsv(AssetStatsAnalysisResult result)
    {
        if (result == null)
            return string.Empty;

        return BuildCsv(result.Records, result.Options.ReportKind);
    }

    public static string BuildCsv(IReadOnlyList<AssetStatsRecord> records, AssetStatsReportKind reportKind)
    {
        StringBuilder builder = new StringBuilder(4096);
        builder.AppendLine(BuildCsvHeader(reportKind));

        if (records == null)
            return builder.ToString();

        foreach (AssetStatsRecord record in records)
        {
            builder.AppendLine(BuildCsvRow(record, reportKind));
        }

        return builder.ToString();
    }

    public static void ExportCsv(AssetStatsAnalysisResult result, string absolutePath)
    {
        string csv = BuildCsv(result);
        File.WriteAllText(absolutePath, csv, new UTF8Encoding(true));
    }

    public static void ExportCsv(IReadOnlyList<AssetStatsRecord> records, AssetStatsReportKind reportKind, string absolutePath)
    {
        string csv = BuildCsv(records, reportKind);
        File.WriteAllText(absolutePath, csv, new UTF8Encoding(true));
    }

    public static PrefabAuditJsonExport BuildPrefabAuditJson(AssetStatsQueryOptions options)
    {
        AssetStatsQueryOptions sanitizedOptions = SanitizeOptions(options);
        QianxiaRuntimeAssetInstanceSnapshot snapshot = QianxiaRuntimeAssetInstanceScanner.Capture(
            new QianxiaRuntimeAssetInstanceScanOptions
            {
                scanScope = sanitizedOptions.LoadedSceneScanScope,
                includeInactiveRenderers = sanitizedOptions.IncludeInactiveRenderers
            });
        PrefabSceneAuditContext prefabContext = BuildPrefabSceneAuditContext(snapshot);
        List<GameObject> prefabAssets = ResolveLoadedScenePrefabAssets(prefabContext);

        PrefabAuditJsonExport export = new PrefabAuditJsonExport
        {
            generatedAtUtc = DateTime.UtcNow.ToString("o"),
            scanScope = snapshot.scanScope ?? sanitizedOptions.LoadedSceneScanScope.ToString(),
            includeInactiveRenderers = snapshot.includeInactiveRenderers,
            loadedSceneCount = snapshot.loadedSceneCount,
            scannedSceneCount = snapshot.scannedSceneCount,
            rendererCount = snapshot.rendererCount,
            prefabInstanceCount = snapshot.prefabInstanceCount
        };
        export.sceneNames.AddRange(snapshot.scenes
            .Select(scene => scene.sceneName)
            .Where(sceneName => !string.IsNullOrWhiteSpace(sceneName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sceneName => sceneName, StringComparer.OrdinalIgnoreCase));

        bool showProgress = prefabAssets.Count >= 25;
        try
        {
            for (int prefabIndex = 0; prefabIndex < prefabAssets.Count; prefabIndex++)
            {
                GameObject prefabAsset = prefabAssets[prefabIndex];
                if (showProgress)
                {
                    float progress = prefabAssets.Count <= 1 ? 1.0f : prefabIndex / (float)(prefabAssets.Count - 1);
                    EditorUtility.DisplayProgressBar("Asset Stats JSON", $"Building {prefabAsset.name}", progress);
                }

                string prefabKey = BuildStableAssetKey("Prefab", AssetDatabase.GetAssetPath(prefabAsset), prefabAsset.name, prefabAsset.GetInstanceID());
                prefabContext.PrefabRecordsByKey.TryGetValue(prefabKey, out QianxiaRuntimeAssetInstanceAggregateRecord prefabAggregate);
                PrefabAuditJsonRecord prefabRecord = BuildPrefabAuditRecord(prefabAsset, prefabAggregate, prefabContext);
                if (prefabRecord != null)
                    export.prefabs.Add(prefabRecord);
            }
        }
        finally
        {
            if (showProgress)
                EditorUtility.ClearProgressBar();
        }

        export.prefabRecordCount = export.prefabs.Count;

        return export;
    }

    public static void ExportPrefabAuditJson(AssetStatsQueryOptions options, string absolutePath)
    {
        PrefabAuditJsonExport export = BuildPrefabAuditJson(options);
        string json = JsonUtility.ToJson(export, true);
        File.WriteAllText(absolutePath, json, new UTF8Encoding(true));
    }

    public static PrefabLookupResult AnalyzePrefabByName(string prefabName, AssetStatsQueryOptions options)
    {
        string query = prefabName != null ? prefabName.Trim() : string.Empty;
        PrefabLookupResult result = new PrefabLookupResult
        {
            Query = query
        };

        if (string.IsNullOrWhiteSpace(query))
        {
            result.StatusMessage = "Input a prefab name before running lookup.";
            return result;
        }

        AssetStatsQueryOptions sanitizedOptions = SanitizeOptions(options);
        QianxiaRuntimeAssetInstanceSnapshot snapshot = QianxiaRuntimeAssetInstanceScanner.Capture(
            new QianxiaRuntimeAssetInstanceScanOptions
            {
                scanScope = sanitizedOptions.LoadedSceneScanScope,
                includeInactiveRenderers = sanitizedOptions.IncludeInactiveRenderers
            });
        PrefabSceneAuditContext prefabContext = BuildPrefabSceneAuditContext(snapshot);
        List<GameObject> prefabAssets = ResolvePrefabAssetsByName(query);
        result.MatchCount = prefabAssets.Count;
        result.ExactMatchCount = prefabAssets.Count(prefab => prefab != null &&
            string.Equals(prefab.name, query, StringComparison.OrdinalIgnoreCase));

        foreach (GameObject prefabAsset in prefabAssets)
        {
            string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            string prefabKey = BuildStableAssetKey("Prefab", prefabPath, prefabAsset.name, prefabAsset.GetInstanceID());
            prefabContext.PrefabRecordsByKey.TryGetValue(prefabKey, out QianxiaRuntimeAssetInstanceAggregateRecord prefabAggregate);
            PrefabAuditJsonRecord record = BuildPrefabAuditRecord(prefabAsset, prefabAggregate, prefabContext);
            if (record != null)
                result.Records.Add(record);
        }

        if (result.Records.Count == 0)
        {
            result.StatusMessage = $"No prefab asset matched `{query}`.";
            return result;
        }

        string exactSuffix = result.ExactMatchCount > 0
            ? $" ({result.ExactMatchCount:N0} exact)"
            : string.Empty;
        result.StatusMessage = $"Found {result.Records.Count:N0} prefab asset(s){exactSuffix} for `{query}`.";
        return result;
    }

    public static PrefabAuditExportResult ExportPrefabAuditJsonWithDerivedMetrics(
        AssetStatsQueryOptions options,
        string absolutePath,
        int topCount = DefaultDerivedMetricsTopCount)
    {
        string normalizedRawPath = Path.GetFullPath(absolutePath);
        ExportPrefabAuditJson(options, normalizedRawPath);

        PrefabAuditExportResult result = new PrefabAuditExportResult
        {
            RawJsonPath = normalizedRawPath,
            DerivedJsonPath = BuildDerivedJsonPath(normalizedRawPath)
        };

        if (TryRunPrefabAuditDerivedMetrics(normalizedRawPath, result.DerivedJsonPath, topCount, out string statusMessage))
        {
            result.DerivedMetricsGenerated = true;
            result.StatusMessage = statusMessage;
            return result;
        }

        result.StatusMessage = statusMessage;
        return result;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int suffixIndex = 0;
        while (value >= 1024.0d && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024.0d;
            suffixIndex++;
        }

        return $"{value:0.##} {suffixes[suffixIndex]}";
    }

    public static string FormatTeamFlags(AssetStatsTeamFlags flags)
    {
        if (flags == AssetStatsTeamFlags.None)
            return "Unassigned";

        List<string> labels = new List<string>(3);
        if ((flags & AssetStatsTeamFlags.Environment) != 0)
            labels.Add("Environment");
        if ((flags & AssetStatsTeamFlags.Character) != 0)
            labels.Add("Character");
        if ((flags & AssetStatsTeamFlags.Vfx) != 0)
            labels.Add("VFX");
        return string.Join(" | ", labels);
    }

    public static string FormatReportKind(AssetStatsReportKind reportKind)
    {
        return reportKind switch
        {
            AssetStatsReportKind.Overview => "Overview",
            AssetStatsReportKind.Mesh => "Mesh Audit",
            AssetStatsReportKind.Texture => "Texture Audit",
            AssetStatsReportKind.Material => "Material Audit",
            _ => reportKind.ToString()
        };
    }

    private static string BuildCsvHeader(AssetStatsReportKind reportKind)
    {
        return reportKind switch
        {
            AssetStatsReportKind.Mesh => string.Join(",",
                EscapeCsv("Mesh Name"),
                EscapeCsv("Mesh Path"),
                EscapeCsv("Teams"),
                EscapeCsv("Triangle Count"),
                EscapeCsv("Vertex Count"),
                EscapeCsv("LOD Count"),
                EscapeCsv("LOD Indices"),
                EscapeCsv("Disk Size Bytes"),
                EscapeCsv("Disk Size"),
                EscapeCsv("Current Scene Usage"),
                EscapeCsv("Used By Levels"),
                EscapeCsv("Used By Actors"),
                EscapeCsv("Used By Assets"),
                EscapeCsv("Material Count"),
                EscapeCsv("Materials"),
                EscapeCsv("Notes")),
            AssetStatsReportKind.Texture => string.Join(",",
                EscapeCsv("Texture Name"),
                EscapeCsv("Texture Path"),
                EscapeCsv("Teams"),
                EscapeCsv("Resolution"),
                EscapeCsv("Format"),
                EscapeCsv("Mip Count"),
                EscapeCsv("Has Mipmaps"),
                EscapeCsv("Storage Size Bytes"),
                EscapeCsv("Storage Size"),
                EscapeCsv("Disk Size Bytes"),
                EscapeCsv("Disk Size"),
                EscapeCsv("Role Summary"),
                EscapeCsv("4K Or Above"),
                EscapeCsv("8K Or Above"),
                EscapeCsv("Current Scene Usage"),
                EscapeCsv("Used By Materials"),
                EscapeCsv("Used By Levels"),
                EscapeCsv("Used By Actors"),
                EscapeCsv("Notes")),
            AssetStatsReportKind.Material => string.Join(",",
                EscapeCsv("Material Name"),
                EscapeCsv("Material Path"),
                EscapeCsv("Teams"),
                EscapeCsv("Shader"),
                EscapeCsv("Texture Count"),
                EscapeCsv("Texture Role Summary"),
                EscapeCsv("Disk Size Bytes"),
                EscapeCsv("Disk Size"),
                EscapeCsv("Current Scene Usage"),
                EscapeCsv("Used By Levels"),
                EscapeCsv("Used By Actors"),
                EscapeCsv("Used By Assets"),
                EscapeCsv("Textures"),
                EscapeCsv("Notes")),
            _ => string.Join(",",
                EscapeCsv("Asset Name"),
                EscapeCsv("Asset Path"),
                EscapeCsv("Asset Kind"),
                EscapeCsv("Teams"),
                EscapeCsv("Current Scene Usage"),
                EscapeCsv("Disk Size Bytes"),
                EscapeCsv("Disk Size"),
                EscapeCsv("Renderer Count"),
                EscapeCsv("Unique Mesh Count"),
                EscapeCsv("Triangle Count"),
                EscapeCsv("Vertex Count"),
                EscapeCsv("Material Slot Count"),
                EscapeCsv("Unique Material Count"),
                EscapeCsv("Materials"),
                EscapeCsv("Texture Count"),
                EscapeCsv("Textures"),
                EscapeCsv("Notes"))
        };
    }

    private static string BuildDerivedJsonPath(string rawJsonPath)
    {
        string fileExtension = Path.GetExtension(rawJsonPath);
        if (string.IsNullOrWhiteSpace(fileExtension))
            return $"{rawJsonPath}.derived.json";

        string directory = Path.GetDirectoryName(rawJsonPath) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(rawJsonPath);
        return Path.Combine(directory, $"{fileName}.derived{fileExtension}");
    }

    private static bool TryRunPrefabAuditDerivedMetrics(
        string inputJsonPath,
        string outputJsonPath,
        int topCount,
        out string statusMessage)
    {
        string projectRoot = Directory.GetCurrentDirectory();
        string scriptPath = Path.Combine(projectRoot, "tools", "asset-stats", "prefab_audit_metrics.py");
        if (!File.Exists(scriptPath))
        {
            statusMessage =
                $"Raw JSON exported to: {inputJsonPath}. Python post-process script was not found: {scriptPath}";
            return false;
        }

        List<string> failureMessages = new List<string>();
        foreach ((string executable, string arguments) candidate in BuildPythonProcessCandidates(scriptPath, inputJsonPath, outputJsonPath, topCount))
        {
            if (TryRunProcess(candidate.executable, candidate.arguments, projectRoot, out string processMessage))
            {
                statusMessage = processMessage;
                return true;
            }

            failureMessages.Add(processMessage);
        }

        statusMessage =
            $"Raw JSON exported to: {inputJsonPath}. Python post-process failed. " +
            string.Join(" | ", failureMessages.Where(message => !string.IsNullOrWhiteSpace(message)));
        return false;
    }

    private static IEnumerable<(string executable, string arguments)> BuildPythonProcessCandidates(
        string scriptPath,
        string inputJsonPath,
        string outputJsonPath,
        int topCount)
    {
        string escapedScriptPath = QuoteProcessArgument(scriptPath);
        string escapedInputJsonPath = QuoteProcessArgument(inputJsonPath);
        string escapedOutputJsonPath = QuoteProcessArgument(outputJsonPath);
        string arguments = $"{escapedScriptPath} {escapedInputJsonPath} -o {escapedOutputJsonPath} --top {Mathf.Max(1, topCount)}";

        yield return ("python", arguments);
        yield return ("py", $"-3 {arguments}");
        yield return ("python3", arguments);
    }

    private static bool TryRunProcess(
        string executable,
        string arguments,
        string workingDirectory,
        out string statusMessage)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using Process process = new Process { StartInfo = startInfo };
            process.Start();

            if (!process.HasExited)
            {
                if (!process.WaitForExit(120000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Ignore kill failures and report timeout.
                    }

                    statusMessage = $"`{executable}` timed out while processing prefab audit metrics.";
                    return false;
                }
            }

            string standardOutput = process.StandardOutput.ReadToEnd().Trim();
            string standardError = process.StandardError.ReadToEnd().Trim();

            if (process.ExitCode == 0)
            {
                statusMessage = string.IsNullOrWhiteSpace(standardOutput)
                    ? $"Raw JSON exported and derived metrics generated with `{executable}`."
                    : $"Raw JSON exported. Derived metrics generated: {standardOutput}";
                return true;
            }

            statusMessage = BuildProcessFailureMessage(executable, process.ExitCode, standardError, standardOutput);
            return false;
        }
        catch (Exception exception)
        {
            statusMessage = $"`{executable}` failed to start: {exception.Message}";
            return false;
        }
    }

    private static string BuildProcessFailureMessage(
        string executable,
        int exitCode,
        string standardError,
        string standardOutput)
    {
        string detail = !string.IsNullOrWhiteSpace(standardError) ? standardError : standardOutput;
        if (string.IsNullOrWhiteSpace(detail))
            detail = "No additional error output was provided.";

        return $"`{executable}` exited with code {exitCode}: {detail}";
    }

    private static string QuoteProcessArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static string BuildCsvRow(AssetStatsRecord record, AssetStatsReportKind reportKind)
    {
        return reportKind switch
        {
            AssetStatsReportKind.Mesh => string.Join(",",
                EscapeCsv(record.AssetName),
                EscapeCsv(record.AssetPath),
                EscapeCsv(record.BuildTeamSummary()),
                EscapeCsv(record.TriangleCount.ToString()),
                EscapeCsv(record.VertexCount.ToString()),
                EscapeCsv(record.LodCount.ToString()),
                EscapeCsv(record.LodIndicesSummary),
                EscapeCsv(record.DiskSizeBytes.ToString()),
                EscapeCsv(record.DiskSizeLabel),
                EscapeCsv(record.CurrentSceneUsageCount.ToString()),
                EscapeCsv(record.BuildUsedBySceneSummary()),
                EscapeCsv(record.BuildUsedByActorSummary()),
                EscapeCsv(record.BuildUsedByAssetSummary()),
                EscapeCsv(record.UniqueMaterialCount.ToString()),
                EscapeCsv(record.BuildMaterialSummary()),
                EscapeCsv(record.BuildNoteSummary())),
            AssetStatsReportKind.Texture => string.Join(",",
                EscapeCsv(record.AssetName),
                EscapeCsv(record.AssetPath),
                EscapeCsv(record.BuildTeamSummary()),
                EscapeCsv($"{record.TextureWidth}x{record.TextureHeight}"),
                EscapeCsv(record.TextureFormat),
                EscapeCsv(record.TextureMipCount.ToString()),
                EscapeCsv(record.TextureHasMipmaps ? "Yes" : "No"),
                EscapeCsv(record.TextureStorageSizeBytes.ToString()),
                EscapeCsv(record.TextureStorageSizeLabel),
                EscapeCsv(record.DiskSizeBytes.ToString()),
                EscapeCsv(record.DiskSizeLabel),
                EscapeCsv(record.BuildTextureRoleSummary()),
                EscapeCsv(IsTextureAtLeast(record, 4096) ? "Yes" : "No"),
                EscapeCsv(IsTextureAtLeast(record, 8192) ? "Yes" : "No"),
                EscapeCsv(record.CurrentSceneUsageCount.ToString()),
                EscapeCsv(record.BuildUsedByMaterialSummary()),
                EscapeCsv(record.BuildUsedBySceneSummary()),
                EscapeCsv(record.BuildUsedByActorSummary()),
                EscapeCsv(record.BuildNoteSummary())),
            AssetStatsReportKind.Material => string.Join(",",
                EscapeCsv(record.AssetName),
                EscapeCsv(record.AssetPath),
                EscapeCsv(record.BuildTeamSummary()),
                EscapeCsv(record.ShaderName),
                EscapeCsv(record.TextureCount.ToString()),
                EscapeCsv(record.BuildTextureRoleSummary()),
                EscapeCsv(record.DiskSizeBytes.ToString()),
                EscapeCsv(record.DiskSizeLabel),
                EscapeCsv(record.CurrentSceneUsageCount.ToString()),
                EscapeCsv(record.BuildUsedBySceneSummary()),
                EscapeCsv(record.BuildUsedByActorSummary()),
                EscapeCsv(record.BuildUsedByAssetSummary()),
                EscapeCsv(record.BuildTextureSummary()),
                EscapeCsv(record.BuildNoteSummary())),
            _ => string.Join(",",
                EscapeCsv(record.AssetName),
                EscapeCsv(record.AssetPath),
                EscapeCsv(record.AssetKind),
                EscapeCsv(record.BuildTeamSummary()),
                EscapeCsv(record.CurrentSceneUsageCount.ToString()),
                EscapeCsv(record.DiskSizeBytes.ToString()),
                EscapeCsv(record.DiskSizeLabel),
                EscapeCsv(record.RendererCount.ToString()),
                EscapeCsv(record.UniqueMeshCount.ToString()),
                EscapeCsv(record.TriangleCount.ToString()),
                EscapeCsv(record.VertexCount.ToString()),
                EscapeCsv(record.MaterialSlotCount.ToString()),
                EscapeCsv(record.UniqueMaterialCount.ToString()),
                EscapeCsv(record.BuildMaterialSummary()),
                EscapeCsv(record.TextureCount.ToString()),
                EscapeCsv(record.BuildTextureSummary()),
                EscapeCsv(record.BuildNoteSummary()))
        };
    }

    private static AssetStatsQueryOptions SanitizeOptions(AssetStatsQueryOptions options)
    {
        AssetStatsQueryOptions sanitized = options ?? new AssetStatsQueryOptions();
        sanitized.MinimumSceneUsageCount = Mathf.Max(1, sanitized.MinimumSceneUsageCount);
        return sanitized;
    }

    private static List<UnityEngine.Object> ResolveAssets(AssetStatsQueryOptions options, CurrentSceneUsageData sceneUsage)
    {
        return options.ScanScope switch
        {
            AssetStatsScanScope.Selection => ResolveSelectionAssets(options),
            AssetStatsScanScope.AllProjectAssets => ResolveProjectAssets(options, sceneUsage),
            _ => ResolveSelectionAssets(options)
        };
    }

    private static List<UnityEngine.Object> ResolveSelectionAssets(AssetStatsQueryOptions options)
    {
        UnityEngine.Object[] assets = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets | SelectionMode.TopLevel);
        List<UnityEngine.Object> selectedAssets = assets.Where(asset => asset != null).ToList();
        if (options == null || options.ReportKind == AssetStatsReportKind.Overview)
            return selectedAssets;

        List<UnityEngine.Object> results = new List<UnityEngine.Object>();
        HashSet<int> seenObjectIds = new HashSet<int>();

        foreach (UnityEngine.Object asset in selectedAssets)
            AddSelectionReportAssets(asset, options.ReportKind, results, seenObjectIds);

        return results;
    }

    private static List<UnityEngine.Object> ResolveProjectAssets(AssetStatsQueryOptions options, CurrentSceneUsageData sceneUsage)
    {
        if (options.TeamFilter == AssetStatsTeamFilter.Level)
            return ResolveAssetsFromSceneUsage(sceneUsage, options.ReportKind);

        List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
        HashSet<int> seenObjectIds = new HashSet<int>();
        bool includeOverviewRows = options.ReportKind == AssetStatsReportKind.Overview;
        bool includeMeshRows = options.ReportKind == AssetStatsReportKind.Mesh;
        bool includeTextureRows = options.ReportKind == AssetStatsReportKind.Texture;
        bool includeMaterialRows = options.ReportKind == AssetStatsReportKind.Material;

        if (includeOverviewRows)
        {
            AddSceneAssets(assets, seenObjectIds, options.TeamFilter);
            AddGameObjectAssets(assets, seenObjectIds, options.TeamFilter);
            if (ShouldIncludeStandaloneSupportAssets(options.TeamFilter, options.ReportKind))
            {
                AddMaterialAssets(assets, seenObjectIds, options.TeamFilter);
                AddTextureAssets(assets, seenObjectIds, options.TeamFilter);
            }

            AddMeshAssets(assets, seenObjectIds, options.TeamFilter, includeEmbeddedMeshes: false);
        }
        else
        {
            if (includeMeshRows)
                AddMeshAssets(assets, seenObjectIds, options.TeamFilter, includeEmbeddedMeshes: true);

            if (includeMaterialRows)
                AddMaterialAssets(assets, seenObjectIds, options.TeamFilter);

            if (includeTextureRows)
                AddTextureAssets(assets, seenObjectIds, options.TeamFilter);
        }

        return assets;
    }

    private static List<UnityEngine.Object> ResolveAssetsFromSceneUsage(CurrentSceneUsageData sceneUsage, AssetStatsReportKind reportKind)
    {
        List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
        HashSet<int> seenObjectIds = new HashSet<int>();

        foreach (string assetPath in sceneUsage.GetTrackedAssetPaths())
            AddAssetsFromPath(
                assetPath,
                assets,
                seenObjectIds,
                includeSupportAssets: reportKind != AssetStatsReportKind.Mesh,
                includeEmbeddedMeshes: reportKind == AssetStatsReportKind.Mesh);

        return assets;
    }

    private static List<GameObject> ResolvePrefabAssets(AssetStatsQueryOptions options, CurrentSceneUsageData sceneUsage)
    {
        return options.ScanScope switch
        {
            AssetStatsScanScope.Selection => ResolveSelectedPrefabAssets(options, sceneUsage),
            AssetStatsScanScope.AllProjectAssets => ResolveProjectPrefabAssets(options, sceneUsage),
            _ => ResolveSelectedPrefabAssets(options, sceneUsage)
        };
    }

    private static List<GameObject> ResolveSelectedPrefabAssets(AssetStatsQueryOptions options, CurrentSceneUsageData sceneUsage)
    {
        List<GameObject> prefabs = new List<GameObject>();
        HashSet<int> seenObjectIds = new HashSet<int>();
        UnityEngine.Object[] selectedAssets = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets | SelectionMode.TopLevel);

        foreach (UnityEngine.Object selectedAsset in selectedAssets)
        {
            if (selectedAsset == null)
                continue;

            if (selectedAsset is GameObject selectedPrefab)
            {
                TryAddGameObject(selectedPrefab, prefabs, seenObjectIds);
                continue;
            }

            string assetPath = AssetDatabase.GetAssetPath(selectedAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
                continue;

            GameObject owner = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
            TryAddGameObject(owner, prefabs, seenObjectIds);
        }

        return prefabs
            .Where(prefab => PrefabMatchesTeamFilter(prefab, options, sceneUsage))
            .OrderBy(GetSortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<GameObject> ResolveProjectPrefabAssets(AssetStatsQueryOptions options, CurrentSceneUsageData sceneUsage)
    {
        List<GameObject> prefabs = new List<GameObject>();
        HashSet<int> seenObjectIds = new HashSet<int>();

        foreach (string path in EnumerateAssetPaths("t:GameObject"))
        {
            string assetKind = ResolveAssetKind(path, typeof(GameObject));
            if (!QuickMatchesTeamFilter(path, assetKind, options.TeamFilter))
                continue;

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabAsset == null)
                continue;

            if (options.TeamFilter == AssetStatsTeamFilter.Level)
            {
                int usageCount = sceneUsage != null ? sceneUsage.GetUsageCount(prefabAsset, path) : 0;
                if (usageCount < options.MinimumSceneUsageCount)
                    continue;
            }

            TryAddGameObject(prefabAsset, prefabs, seenObjectIds);
        }

        return prefabs.OrderBy(GetSortKey, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static PrefabSceneAuditContext BuildPrefabSceneAuditContext(QianxiaRuntimeAssetInstanceSnapshot snapshot)
    {
        PrefabSceneAuditContext context = new PrefabSceneAuditContext
        {
            Snapshot = snapshot
        };

        if (snapshot == null)
            return context;

        foreach (QianxiaRuntimeAssetInstanceAggregateRecord prefabRecord in snapshot.prefabs)
        {
            if (prefabRecord == null || !string.Equals(prefabRecord.assetKind, "Prefab", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(prefabRecord.key))
                continue;

            context.PrefabRecordsByKey[prefabRecord.key] = prefabRecord;
        }

        BuildPrefabContributionIndex(snapshot.meshes, context.MeshUsageByPrefabKey);
        BuildPrefabContributionIndex(snapshot.materials, context.MaterialUsageByPrefabKey);
        return context;
    }

    private static void BuildPrefabContributionIndex(
        IReadOnlyList<QianxiaRuntimeAssetInstanceAggregateRecord> aggregates,
        IDictionary<string, Dictionary<string, int>> usageByPrefabKey)
    {
        if (aggregates == null || usageByPrefabKey == null)
            return;

        foreach (QianxiaRuntimeAssetInstanceAggregateRecord aggregate in aggregates)
        {
            if (aggregate == null || string.IsNullOrWhiteSpace(aggregate.key) || aggregate.prefabBreakdown == null)
                continue;

            foreach (QianxiaRuntimeAssetInstanceBreakdownRecord breakdown in aggregate.prefabBreakdown)
            {
                if (breakdown == null || string.IsNullOrWhiteSpace(breakdown.key))
                    continue;

                if (!usageByPrefabKey.TryGetValue(breakdown.key, out Dictionary<string, int> usageByAssetKey))
                {
                    usageByAssetKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    usageByPrefabKey.Add(breakdown.key, usageByAssetKey);
                }

                usageByAssetKey[aggregate.key] = breakdown.usageCount;
            }
        }
    }

    private static List<GameObject> ResolveLoadedScenePrefabAssets(PrefabSceneAuditContext prefabContext)
    {
        List<GameObject> prefabs = new List<GameObject>();
        if (prefabContext == null)
            return prefabs;

        foreach (QianxiaRuntimeAssetInstanceAggregateRecord prefabRecord in prefabContext.PrefabRecordsByKey.Values
                     .OrderByDescending(record => record.usageCount)
                     .ThenBy(record => record.assetPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(record => record.assetName, StringComparer.OrdinalIgnoreCase))
        {
            if (prefabRecord == null || string.IsNullOrWhiteSpace(prefabRecord.assetPath))
                continue;

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabRecord.assetPath);
            if (prefabAsset != null)
                prefabs.Add(prefabAsset);
        }

        return prefabs;
    }

    private static List<GameObject> ResolvePrefabAssetsByName(string query)
    {
        List<GameObject> prefabs = new List<GameObject>();
        if (string.IsNullOrWhiteSpace(query))
            return prefabs;

        foreach (string path in EnumerateAssetPaths("t:GameObject"))
        {
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                continue;

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabAsset == null)
                continue;

            string prefabName = prefabAsset.name ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            bool nameMatches = prefabName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            bool fileMatches = fileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            if (nameMatches || fileMatches)
                prefabs.Add(prefabAsset);
        }

        return prefabs
            .OrderBy(prefab => string.Equals(prefab.name, query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(prefab => prefab.name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(prefab => AssetDatabase.GetAssetPath(prefab), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddSceneAssets(List<UnityEngine.Object> assets, HashSet<int> seenObjectIds, AssetStatsTeamFilter teamFilter)
    {
        foreach (string path in EnumerateAssetPaths("t:SceneAsset"))
        {
            if (!QuickMatchesTeamFilter(path, "Scene", teamFilter))
                continue;

            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            TryAddAsset(sceneAsset, assets, seenObjectIds);
        }
    }

    private static void AddGameObjectAssets(List<UnityEngine.Object> assets, HashSet<int> seenObjectIds, AssetStatsTeamFilter teamFilter)
    {
        foreach (string path in EnumerateAssetPaths("t:GameObject"))
        {
            if (!QuickMatchesTeamFilter(path, ResolveAssetKind(path, typeof(GameObject)), teamFilter))
                continue;

            GameObject gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            TryAddAsset(gameObject, assets, seenObjectIds);
        }
    }

    private static void AddMaterialAssets(List<UnityEngine.Object> assets, HashSet<int> seenObjectIds, AssetStatsTeamFilter teamFilter)
    {
        foreach (string path in EnumerateAssetPaths("t:Material"))
        {
            if (!QuickMatchesTeamFilter(path, "Material", teamFilter))
                continue;

            foreach (Material material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                TryAddAsset(material, assets, seenObjectIds);
        }
    }

    private static void AddTextureAssets(List<UnityEngine.Object> assets, HashSet<int> seenObjectIds, AssetStatsTeamFilter teamFilter)
    {
        foreach (string path in EnumerateAssetPaths("t:Texture"))
        {
            if (!QuickMatchesTeamFilter(path, "Texture", teamFilter))
                continue;

            foreach (Texture texture in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture>())
                TryAddAsset(texture, assets, seenObjectIds);
        }
    }

    private static void AddMeshAssets(
        List<UnityEngine.Object> assets,
        HashSet<int> seenObjectIds,
        AssetStatsTeamFilter teamFilter,
        bool includeEmbeddedMeshes)
    {
        foreach (string path in EnumerateAssetPaths("t:Mesh"))
        {
            if (!QuickMatchesTeamFilter(path, "Mesh", teamFilter))
                continue;

            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (!includeEmbeddedMeshes && mainType != typeof(Mesh))
                continue;

            IEnumerable<Mesh> meshes = includeEmbeddedMeshes
                ? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()
                : AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>();

            foreach (Mesh mesh in meshes)
                TryAddAsset(mesh, assets, seenObjectIds);
        }
    }

    private static IEnumerable<string> EnumerateAssetPaths(string filter)
    {
        foreach (string guid in AssetDatabase.FindAssets(filter))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrWhiteSpace(path))
                yield return path;
        }
    }

    private static void AddAssetsFromPath(
        string assetPath,
        List<UnityEngine.Object> assets,
        HashSet<int> seenObjectIds,
        bool includeSupportAssets,
        bool includeEmbeddedMeshes)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        Type mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
        if (mainType == typeof(SceneAsset))
            TryAddAsset(AssetDatabase.LoadAssetAtPath<SceneAsset>(assetPath), assets, seenObjectIds);

        if (typeof(GameObject).IsAssignableFrom(mainType))
            TryAddAsset(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath), assets, seenObjectIds);

        if (includeSupportAssets)
        {
            foreach (Material material in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>())
                TryAddAsset(material, assets, seenObjectIds);

            foreach (Texture texture in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Texture>())
                TryAddAsset(texture, assets, seenObjectIds);
        }

        if (mainType == typeof(Mesh) || includeEmbeddedMeshes)
        {
            foreach (Mesh mesh in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>())
                TryAddAsset(mesh, assets, seenObjectIds);
        }
    }

    private static bool ShouldIncludeStandaloneSupportAssets(AssetStatsTeamFilter teamFilter, AssetStatsReportKind reportKind)
    {
        if (reportKind == AssetStatsReportKind.Material || reportKind == AssetStatsReportKind.Texture)
            return true;

        return teamFilter switch
        {
            AssetStatsTeamFilter.All => true,
            AssetStatsTeamFilter.Vfx => true,
            AssetStatsTeamFilter.Environment => false,
            AssetStatsTeamFilter.Character => false,
            AssetStatsTeamFilter.Level => false,
            _ => true
        };
    }

    private static void AddSelectionReportAssets(
        UnityEngine.Object asset,
        AssetStatsReportKind reportKind,
        List<UnityEngine.Object> results,
        HashSet<int> seenObjectIds)
    {
        if (asset == null)
            return;

        switch (reportKind)
        {
            case AssetStatsReportKind.Mesh:
                if (asset is Mesh mesh)
                {
                    TryAddAsset(mesh, results, seenObjectIds);
                    return;
                }

                if (asset is GameObject gameObject)
                {
                    foreach (Mesh sharedMesh in CollectMeshReferenceContext(gameObject).Meshes)
                        TryAddAsset(sharedMesh, results, seenObjectIds);
                }
                break;

            case AssetStatsReportKind.Texture:
                if (asset is Texture texture)
                {
                    TryAddAsset(texture, results, seenObjectIds);
                    return;
                }

                if (asset is Material material)
                {
                    foreach (TextureStatsRecord textureRecord in CollectTextureStats(new[] { material }))
                        TryAddAsset(textureRecord.Texture, results, seenObjectIds);
                    return;
                }

                if (asset is GameObject textureRoot)
                {
                    foreach (TextureStatsRecord textureRecord in CollectTextureStats(CollectMeshReferenceContext(textureRoot).UniqueMaterials))
                        TryAddAsset(textureRecord.Texture, results, seenObjectIds);
                }
                break;

            case AssetStatsReportKind.Material:
                if (asset is Material selectedMaterial)
                {
                    TryAddAsset(selectedMaterial, results, seenObjectIds);
                    return;
                }

                if (asset is GameObject materialRoot)
                {
                    foreach (Material uniqueMaterial in CollectMeshReferenceContext(materialRoot).UniqueMaterials)
                        TryAddAsset(uniqueMaterial, results, seenObjectIds);
                }
                break;
        }
    }

    private static AssetUsageIndex BuildUsageIndex(AssetStatsQueryOptions options)
    {
        AssetUsageIndex usageIndex = new AssetUsageIndex();
        if (options == null)
            return usageIndex;

        BuildSceneUsageIndex(usageIndex);
        BuildProjectUsageIndex(options.TeamFilter, usageIndex);
        return usageIndex;
    }

    private static void BuildSceneUsageIndex(AssetUsageIndex usageIndex)
    {
        if (usageIndex == null)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
            return;

        string sceneLabel = string.IsNullOrWhiteSpace(activeScene.name) ? "<Unnamed Scene>" : activeScene.name;
        foreach (Renderer renderer in activeScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)))
            RegisterRendererUsage(renderer, sceneLabel, BuildActorLabel(renderer), string.Empty, usageIndex, renderer.gameObject.scene.IsValid());
    }

    private static void BuildProjectUsageIndex(AssetStatsTeamFilter teamFilter, AssetUsageIndex usageIndex)
    {
        if (usageIndex == null)
            return;

        foreach (string path in EnumerateAssetPaths("t:GameObject"))
        {
            string assetKind = ResolveAssetKind(path, typeof(GameObject));
            if (!QuickMatchesTeamFilter(path, assetKind, teamFilter))
                continue;

            GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (assetRoot == null)
                continue;

            RegisterGameObjectAssetUsage(assetRoot, path, usageIndex);
        }

        foreach (string path in EnumerateAssetPaths("t:Material"))
        {
            if (!QuickMatchesTeamFilter(path, "Material", teamFilter))
                continue;

            foreach (Material material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                RegisterMaterialTextureUsage(material, string.Empty, string.Empty, string.Empty, usageIndex);
        }
    }

    private static void RegisterGameObjectAssetUsage(GameObject assetRoot, string assetPath, AssetUsageIndex usageIndex)
    {
        if (assetRoot == null || usageIndex == null)
            return;

        string assetLabel = string.IsNullOrWhiteSpace(assetRoot.name) ? Path.GetFileNameWithoutExtension(assetPath) : assetRoot.name;
        Dictionary<Renderer, (int lodIndex, int lodCount)> lodInfo = BuildRendererLodInfo(assetRoot);

        foreach (Renderer renderer in assetRoot.GetComponentsInChildren<Renderer>(true))
            RegisterRendererUsage(renderer, string.Empty, string.Empty, assetLabel, usageIndex, false, lodInfo);
    }

    private static Dictionary<Renderer, (int lodIndex, int lodCount)> BuildRendererLodInfo(GameObject root)
    {
        Dictionary<Renderer, (int lodIndex, int lodCount)> lookup = new Dictionary<Renderer, (int lodIndex, int lodCount)>();
        if (root == null)
            return lookup;

        foreach (LODGroup lodGroup in root.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] lods = lodGroup.GetLODs();
            for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
            {
                foreach (Renderer renderer in lods[lodIndex].renderers)
                {
                    if (renderer != null)
                        lookup[renderer] = (lodIndex, lods.Length);
                }
            }
        }

        return lookup;
    }

    private static void RegisterRendererUsage(
        Renderer renderer,
        string sceneLabel,
        string actorLabel,
        string assetLabel,
        AssetUsageIndex usageIndex,
        bool isSceneInstance,
        Dictionary<Renderer, (int lodIndex, int lodCount)> lodInfo = null)
    {
        if (renderer == null || usageIndex == null)
            return;

        Mesh sharedMesh = ResolveSharedMesh(renderer);
        if (sharedMesh != null)
        {
            string meshPath = AssetDatabase.GetAssetPath(sharedMesh);
            UsageInfo meshUsage = GetOrCreateUsageInfo(usageIndex.MeshUsage, BuildObjectKey(meshPath, sharedMesh.name));
            RegisterUsageLabels(meshUsage, sceneLabel, actorLabel, assetLabel);

            if (lodInfo != null && lodInfo.TryGetValue(renderer, out (int lodIndex, int lodCount) lodData))
            {
                meshUsage.LodIndices.Add(lodData.lodIndex);
                meshUsage.MaxLodCount = Mathf.Max(meshUsage.MaxLodCount, lodData.lodCount);
            }
        }

        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;

            string materialPath = AssetDatabase.GetAssetPath(material);
            UsageInfo materialUsage = GetOrCreateUsageInfo(usageIndex.MaterialUsage, BuildObjectKey(materialPath, material.name));
            RegisterUsageLabels(materialUsage, sceneLabel, actorLabel, assetLabel);
            RegisterMaterialTextureUsage(material, sceneLabel, actorLabel, assetLabel, usageIndex);
        }
    }

    private static void RegisterMaterialTextureUsage(
        Material material,
        string sceneLabel,
        string actorLabel,
        string assetLabel,
        AssetUsageIndex usageIndex)
    {
        if (material == null || usageIndex == null)
            return;

        MaterialTextureProfile profile = BuildMaterialTextureProfile(material);
        foreach (MaterialTextureReference reference in profile.References)
        {
            UsageInfo textureUsage = GetOrCreateUsageInfo(usageIndex.TextureUsage, reference.TexturePath);
            RegisterUsageLabels(textureUsage, sceneLabel, actorLabel, assetLabel);
            if (!string.IsNullOrWhiteSpace(material.name))
                textureUsage.MaterialNames.Add(material.name);
        }
    }

    private static UsageInfo GetOrCreateUsageInfo(IDictionary<string, UsageInfo> dictionary, string key)
    {
        if (dictionary == null || string.IsNullOrWhiteSpace(key))
            return new UsageInfo();

        if (!dictionary.TryGetValue(key, out UsageInfo usageInfo))
        {
            usageInfo = new UsageInfo();
            dictionary.Add(key, usageInfo);
        }

        return usageInfo;
    }

    private static void RegisterUsageLabels(UsageInfo usageInfo, string sceneLabel, string actorLabel, string assetLabel)
    {
        if (usageInfo == null)
            return;

        if (!string.IsNullOrWhiteSpace(sceneLabel))
            usageInfo.SceneNames.Add(sceneLabel);
        if (!string.IsNullOrWhiteSpace(actorLabel))
            usageInfo.ActorNames.Add(actorLabel);
        if (!string.IsNullOrWhiteSpace(assetLabel))
            usageInfo.AssetNames.Add(assetLabel);
    }

    private static string BuildActorLabel(Component component)
    {
        if (component == null)
            return string.Empty;

        Transform transform = component.transform;
        if (transform == null)
            return string.Empty;

        string rootName = transform.root != null ? transform.root.name : string.Empty;
        return string.Equals(rootName, transform.name, StringComparison.Ordinal)
            ? transform.name
            : $"{rootName}/{transform.name}";
    }

    private static void TryAddAsset(UnityEngine.Object asset, List<UnityEngine.Object> assets, HashSet<int> seenObjectIds)
    {
        if (asset == null || assets == null || seenObjectIds == null)
            return;

        if (!seenObjectIds.Add(asset.GetInstanceID()))
            return;

        assets.Add(asset);
    }

    private static void TryAddGameObject(GameObject asset, List<GameObject> assets, HashSet<int> seenObjectIds)
    {
        if (asset == null || assets == null || seenObjectIds == null)
            return;

        if (!seenObjectIds.Add(asset.GetInstanceID()))
            return;

        assets.Add(asset);
    }

    private static bool PrefabMatchesTeamFilter(GameObject prefabAsset, AssetStatsQueryOptions options, CurrentSceneUsageData sceneUsage)
    {
        if (prefabAsset == null || options == null)
            return false;

        string assetPath = AssetDatabase.GetAssetPath(prefabAsset);
        string assetKind = ResolveAssetKind(prefabAsset, assetPath);
        AssetStatsTeamFlags teamFlags = ResolveTeamFlags(prefabAsset, assetPath, assetKind);

        if (options.TeamFilter == AssetStatsTeamFilter.Level)
        {
            int usageCount = sceneUsage != null ? sceneUsage.GetUsageCount(prefabAsset, assetPath) : 0;
            return usageCount >= options.MinimumSceneUsageCount;
        }

        return options.TeamFilter switch
        {
            AssetStatsTeamFilter.All => true,
            AssetStatsTeamFilter.Environment => (teamFlags & AssetStatsTeamFlags.Environment) != 0,
            AssetStatsTeamFilter.Character => (teamFlags & AssetStatsTeamFlags.Character) != 0,
            AssetStatsTeamFilter.Vfx => (teamFlags & AssetStatsTeamFlags.Vfx) != 0,
            _ => true
        };
    }

    private static List<AssetStatsRecord> AnalyzeAssets(
        IEnumerable<UnityEngine.Object> assets,
        CurrentSceneUsageData sceneUsage,
        AssetUsageIndex usageIndex)
    {
        List<AssetStatsRecord> results = new List<AssetStatsRecord>();
        if (assets == null)
            return results;

        List<UnityEngine.Object> orderedAssets = assets
            .Where(asset => asset != null)
            .OrderBy(GetSortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool showProgress = orderedAssets.Count >= 50;
        try
        {
            for (int assetIndex = 0; assetIndex < orderedAssets.Count; assetIndex++)
            {
                UnityEngine.Object asset = orderedAssets[assetIndex];
                if (showProgress)
                {
                    float progress = orderedAssets.Count <= 1 ? 1.0f : assetIndex / (float)(orderedAssets.Count - 1);
                    EditorUtility.DisplayProgressBar("Asset Stats", $"Analyzing {asset.name}", progress);
                }

                results.Add(AnalyzeAsset(asset, sceneUsage, usageIndex));
            }
        }
        finally
        {
            if (showProgress)
                EditorUtility.ClearProgressBar();
        }

        return results;
    }

    private static AssetStatsRecord AnalyzeAsset(
        UnityEngine.Object asset,
        CurrentSceneUsageData sceneUsage,
        AssetUsageIndex usageIndex)
    {
        AssetStatsRecord record = new AssetStatsRecord
        {
            Asset = asset,
            AssetName = asset != null ? asset.name : string.Empty
        };

        if (asset == null)
        {
            record.Notes.Add("Null asset.");
            return record;
        }

        record.AssetPath = AssetDatabase.GetAssetPath(asset);
        record.AssetKind = ResolveAssetKind(asset, record.AssetPath);
        record.DiskSizeBytes = ResolveDiskSizeBytes(record.AssetPath);
        record.TeamFlags = ResolveTeamFlags(asset, record.AssetPath, record.AssetKind);
        record.CurrentSceneUsageCount = sceneUsage != null ? sceneUsage.GetUsageCount(asset, record.AssetPath) : 0;

        if (string.IsNullOrEmpty(record.AssetPath))
        {
            record.Notes.Add("Select assets from the Project window.");
            return record;
        }

        if (AssetDatabase.IsValidFolder(record.AssetPath))
        {
            record.Notes.Add("Folders are not analyzed by this window.");
            return record;
        }

        switch (asset)
        {
            case GameObject gameObject:
                AnalyzeGameObjectAsset(gameObject, record, usageIndex);
                break;
            case Mesh mesh:
                AnalyzeMeshAsset(mesh, record, usageIndex);
                break;
            case Material material:
                AnalyzeMaterialAsset(material, record, usageIndex);
                break;
            case Texture texture:
                AnalyzeTextureAsset(texture, record, usageIndex);
                break;
            case SceneAsset sceneAsset:
                AnalyzeSceneAsset(sceneAsset, record);
                break;
            default:
                record.Notes.Add("This asset type only exposes basic file size metadata.");
                break;
        }

        return record;
    }

    private static void AnalyzeSceneAsset(SceneAsset sceneAsset, AssetStatsRecord record)
    {
        if (sceneAsset == null || record == null)
            return;

        record.Notes.Add("Scene files expose file-level stats here. Open the scene and use Level view for heavily used assets.");
    }

    private static PrefabAuditJsonRecord BuildPrefabAuditRecord(
        GameObject prefabAsset,
        QianxiaRuntimeAssetInstanceAggregateRecord prefabAggregate,
        PrefabSceneAuditContext prefabContext)
    {
        if (prefabAsset == null)
            return null;

        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        int sceneUsageCount = prefabAggregate != null ? prefabAggregate.usageCount : 0;
        string prefabKey = BuildStableAssetKey("Prefab", prefabPath, prefabAsset.name, prefabAsset.GetInstanceID());
        PrefabLodSummary lodSummary = BuildPrefabLodSummary(prefabAsset);
        MeshReferenceContext meshContext = CollectMeshReferenceContext(prefabAsset);
        Dictionary<string, List<Material>> materialsByMeshKey = BuildMaterialsByMeshKey(prefabAsset);
        Dictionary<string, PrefabTextureAggregateData> textureAggregates =
            new Dictionary<string, PrefabTextureAggregateData>(StringComparer.OrdinalIgnoreCase);

        PrefabAuditJsonRecord record = new PrefabAuditJsonRecord
        {
            prefabName = prefabAsset.name ?? string.Empty,
            prefabPath = prefabPath ?? string.Empty,
            sceneUsageCount = sceneUsageCount,
            rendererCount = meshContext.RendererCount,
            uniqueMeshCount = meshContext.Meshes.Count,
            uniqueMaterialCount = meshContext.UniqueMaterials.Count,
            materialSlotCount = meshContext.MaterialSlotCount,
            boundsSurfaceArea = ResolvePrefabBoundsSurfaceArea(prefabAsset),
            hasLod = lodSummary.HasLod
        };
        record.lods.AddRange(CloneLodRecords(lodSummary.PrefabLods));

        foreach (Mesh mesh in meshContext.Meshes
                     .Where(mesh => mesh != null)
                     .OrderBy(mesh => AssetDatabase.GetAssetPath(mesh), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(mesh => mesh.name, StringComparer.OrdinalIgnoreCase))
        {
            string meshKey = BuildObjectKey(AssetDatabase.GetAssetPath(mesh), mesh.name);
            materialsByMeshKey.TryGetValue(meshKey, out List<Material> meshMaterials);
            PrefabMeshAuditJsonRecord meshRecord = BuildPrefabMeshAuditRecord(
                mesh,
                lodSummary,
                prefabKey,
                prefabContext,
                meshMaterials);
            if (meshRecord != null)
                record.meshes.Add(meshRecord);
        }

        foreach (Material material in meshContext.UniqueMaterials
                     .Where(material => material != null)
                     .OrderBy(material => AssetDatabase.GetAssetPath(material), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(material => material.name, StringComparer.OrdinalIgnoreCase))
        {
            PrefabMaterialAuditJsonRecord materialRecord = BuildPrefabMaterialAuditRecord(
                material,
                prefabKey,
                prefabContext,
                textureAggregates);
            if (materialRecord != null)
                record.materials.Add(materialRecord);
        }

        record.textures.AddRange(textureAggregates.Values
            .Select(BuildPrefabTextureAuditRecord)
            .Where(texture => texture != null)
            .OrderByDescending(texture => texture.storageSizeBytes)
            .ThenBy(texture => texture.texturePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(texture => texture.textureName, StringComparer.OrdinalIgnoreCase));

        record.uniqueTextureCount = record.textures.Count;
        record.totalTriangleCount = record.meshes.Sum(mesh => mesh.triangleCount);
        record.totalVertexCount = record.meshes.Sum(mesh => mesh.vertexCount);
        record.totalMeshDiskSizeBytes = record.meshes
            .Select(mesh => mesh.meshPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Sum(ResolveDiskSizeBytes);
        record.totalMeshDiskSize = FormatBytes(record.totalMeshDiskSizeBytes);
        record.totalTextureStorageSizeBytes = record.textures.Sum(texture => texture.storageSizeBytes);
        record.totalTextureStorageSize = FormatBytes(record.totalTextureStorageSizeBytes);
        record.totalTextureDiskSizeBytes = record.textures
            .Select(texture => texture.texturePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Sum(ResolveDiskSizeBytes);
        record.totalTextureDiskSize = FormatBytes(record.totalTextureDiskSizeBytes);

        if (record.meshes.Count == 0)
            record.notes.Add("No renderer mesh was found under this prefab.");
        if (record.textures.Count == 0)
            record.notes.Add("No material texture was found under this prefab.");

        return record;
    }

    private static Dictionary<string, List<Material>> BuildMaterialsByMeshKey(GameObject prefabAsset)
    {
        Dictionary<string, List<Material>> materialsByMeshKey =
            new Dictionary<string, List<Material>>(StringComparer.OrdinalIgnoreCase);
        if (prefabAsset == null)
            return materialsByMeshKey;

        foreach (Renderer renderer in prefabAsset.GetComponentsInChildren<Renderer>(true))
        {
            Mesh sharedMesh = ResolveSharedMesh(renderer);
            if (sharedMesh == null)
                continue;

            string meshKey = BuildObjectKey(AssetDatabase.GetAssetPath(sharedMesh), sharedMesh.name);
            if (!materialsByMeshKey.TryGetValue(meshKey, out List<Material> materials))
            {
                materials = new List<Material>();
                materialsByMeshKey.Add(meshKey, materials);
            }

            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && !materials.Contains(material))
                    materials.Add(material);
            }
        }

        return materialsByMeshKey;
    }

    private static PrefabMeshAuditJsonRecord BuildPrefabMeshAuditRecord(
        Mesh mesh,
        PrefabLodSummary lodSummary,
        string prefabKey,
        PrefabSceneAuditContext prefabContext,
        IReadOnlyList<Material> materials)
    {
        if (mesh == null)
            return null;

        string meshPath = AssetDatabase.GetAssetPath(mesh);
        string meshKey = BuildObjectKey(meshPath, mesh.name);
        string stableMeshKey = BuildStableAssetKey("Mesh", meshPath, mesh.name, mesh.GetInstanceID());
        List<PrefabLodAuditJsonRecord> lods = lodSummary != null && lodSummary.MeshLodsByMeshKey.TryGetValue(meshKey, out List<PrefabLodAuditJsonRecord> mappedLods)
            ? CloneLodRecords(mappedLods)
            : new List<PrefabLodAuditJsonRecord> { CreateLodRecord(0, CountTriangles(mesh)) };
        List<Material> uniqueMaterials = (materials ?? Array.Empty<Material>())
            .Where(material => material != null)
            .Distinct()
            .OrderBy(material => material.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<TextureStatsRecord> textureStats = CollectTextureStats(uniqueMaterials);
        long diskSizeBytes = ResolveDiskSizeBytes(meshPath);

        PrefabMeshAuditJsonRecord record = new PrefabMeshAuditJsonRecord
        {
            meshName = mesh.name ?? string.Empty,
            meshPath = meshPath ?? string.Empty,
            triangleCount = CountTriangles(mesh),
            vertexCount = mesh.vertexCount,
            diskSizeBytes = diskSizeBytes,
            diskSize = FormatBytes(diskSizeBytes),
            currentSceneUsageCount = ResolvePrefabAssetUsageCount(prefabContext?.MeshUsageByPrefabKey, prefabKey, stableMeshKey),
            materialCount = uniqueMaterials.Count,
            textureCount = textureStats.Count,
            hasLod = lods.Count > 1 || lods.Any(lod => lod != null && lod.lodIndex > 0)
        };
        record.materialNames.AddRange(uniqueMaterials.Select(material => material.name ?? string.Empty));
        record.lods.AddRange(lods);

        return record;
    }

    private static PrefabMaterialAuditJsonRecord BuildPrefabMaterialAuditRecord(
        Material material,
        string prefabKey,
        PrefabSceneAuditContext prefabContext,
        IDictionary<string, PrefabTextureAggregateData> textureAggregates)
    {
        if (material == null)
            return null;

        string materialPath = AssetDatabase.GetAssetPath(material);
        MaterialTextureProfile profile = BuildMaterialTextureProfile(material);
        long diskSizeBytes = ResolveDiskSizeBytes(materialPath);
        string stableMaterialKey = BuildStableAssetKey("Material", materialPath, material.name, material.GetInstanceID());
        int currentSceneUsageCount = ResolvePrefabAssetUsageCount(prefabContext?.MaterialUsageByPrefabKey, prefabKey, stableMaterialKey);

        PrefabMaterialAuditJsonRecord record = new PrefabMaterialAuditJsonRecord
        {
            materialName = material.name ?? string.Empty,
            materialPath = materialPath ?? string.Empty,
            shaderName = material.shader != null ? material.shader.name : string.Empty,
            textureCount = profile.References
                .Select(reference => BuildTextureAggregateKey(reference.Texture, reference.TexturePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            textureRoleSummary = BuildMaterialTextureRoleSummary(profile),
            diskSizeBytes = diskSizeBytes,
            diskSize = FormatBytes(diskSizeBytes),
            currentSceneUsageCount = currentSceneUsageCount
        };

        foreach (MaterialTextureReference reference in profile.References)
        {
            record.textureReferences.Add(new PrefabMaterialTextureReferenceJsonRecord
            {
                textureName = reference.Texture != null ? reference.Texture.name : string.Empty,
                texturePath = reference.TexturePath ?? string.Empty,
                role = reference.Role ?? string.Empty,
                propertyName = reference.PropertyName ?? string.Empty
            });

            string aggregateKey = BuildTextureAggregateKey(reference.Texture, reference.TexturePath);
            if (!textureAggregates.TryGetValue(aggregateKey, out PrefabTextureAggregateData aggregate))
            {
                aggregate = new PrefabTextureAggregateData
                {
                    Texture = reference.Texture,
                    TexturePath = reference.TexturePath ?? string.Empty
                };
                textureAggregates.Add(aggregateKey, aggregate);
            }

            if (!string.IsNullOrWhiteSpace(reference.Role))
                aggregate.Roles.Add(reference.Role);
            if (!string.IsNullOrWhiteSpace(material.name))
                aggregate.MaterialNames.Add(material.name);
            if (!string.IsNullOrWhiteSpace(reference.PropertyName))
                aggregate.PropertyUsages.Add($"{material.name}.{reference.PropertyName}");
        }

        if (record.textureReferences.Count == 0)
            record.notes.Add("This material does not reference any texture.");

        return record;
    }

    private static PrefabTextureAuditJsonRecord BuildPrefabTextureAuditRecord(
        PrefabTextureAggregateData aggregate)
    {
        if (aggregate == null || aggregate.Texture == null)
            return null;

        string texturePath = aggregate.TexturePath ?? string.Empty;
        long diskSizeBytes = ResolveDiskSizeBytes(texturePath);
        long storageSizeBytes = ResolveTextureStorageSizeBytes(aggregate.Texture);

        PrefabTextureAuditJsonRecord record = new PrefabTextureAuditJsonRecord
        {
            textureName = aggregate.Texture.name ?? string.Empty,
            texturePath = texturePath,
            width = aggregate.Texture.width,
            height = aggregate.Texture.height,
            resolution = $"{aggregate.Texture.width}x{aggregate.Texture.height}",
            format = ResolveTextureFormat(aggregate.Texture),
            mipCount = ResolveTextureMipCount(aggregate.Texture),
            hasMipmaps = ResolveTextureHasMipmaps(aggregate.Texture, texturePath),
            storageSizeBytes = storageSizeBytes,
            storageSize = FormatBytes(storageSizeBytes),
            diskSizeBytes = diskSizeBytes,
            diskSize = FormatBytes(diskSizeBytes),
            materialCount = aggregate.MaterialNames.Count,
            is4KOrAbove = Mathf.Max(aggregate.Texture.width, aggregate.Texture.height) >= 4096,
            is8KOrAbove = Mathf.Max(aggregate.Texture.width, aggregate.Texture.height) >= 8192
        };

        record.roles.AddRange(aggregate.Roles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase));
        record.usedByMaterials.AddRange(aggregate.MaterialNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        record.materialPropertyUsages.AddRange(aggregate.PropertyUsages.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

        if (record.is8KOrAbove)
            record.notes.Add("8K texture detected. Verify that the resolution is intentional.");
        else if (record.is4KOrAbove)
            record.notes.Add("4K texture detected. Check if lower resolutions would be sufficient.");

        return record;
    }

    private static string BuildTextureAggregateKey(Texture texture, string texturePath)
    {
        if (!string.IsNullOrWhiteSpace(texturePath))
            return texturePath;

        return texture != null ? $"instance:{texture.GetInstanceID()}" : string.Empty;
    }

    private static Vector3 ResolveMeshDimensions(Mesh mesh)
    {
        return mesh != null ? mesh.bounds.size : Vector3.zero;
    }

    private static float ResolvePrefabBoundsSurfaceArea(GameObject prefabAsset)
    {
        Vector3 size = ResolvePrefabDimensions(prefabAsset);
        float x = Mathf.Max(0.0f, size.x);
        float y = Mathf.Max(0.0f, size.y);
        float z = Mathf.Max(0.0f, size.z);
        return 2.0f * ((x * y) + (x * z) + (y * z));
    }

    private static Vector3 ResolvePrefabDimensions(GameObject prefabAsset)
    {
        if (prefabAsset == null)
            return Vector3.zero;

        bool hasBounds = false;
        Bounds combinedBounds = default;
        Matrix4x4 rootWorldToLocal = prefabAsset.transform.worldToLocalMatrix;

        foreach (Renderer renderer in prefabAsset.GetComponentsInChildren<Renderer>(true))
        {
            if (!TryResolveRendererLocalBounds(renderer, out Bounds localBounds))
                continue;

            Matrix4x4 localToRoot = rootWorldToLocal * renderer.transform.localToWorldMatrix;
            Bounds transformedBounds = TransformBounds(localBounds, localToRoot);

            if (!hasBounds)
            {
                combinedBounds = transformedBounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(transformedBounds);
            }
        }

        return hasBounds ? combinedBounds.size : Vector3.zero;
    }

    private static PrefabLodSummary BuildPrefabLodSummary(GameObject prefabAsset)
    {
        PrefabLodSummary summary = new PrefabLodSummary();
        if (prefabAsset == null)
            return summary;

        Dictionary<int, long> prefabLodTriangleCounts = new Dictionary<int, long>();
        Dictionary<int, long> prefabLodVertexCounts = new Dictionary<int, long>();
        Dictionary<string, Dictionary<int, long>> meshLodTriangleCounts =
            new Dictionary<string, Dictionary<int, long>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<Renderer, (int lodIndex, int lodCount)> lodInfo = BuildRendererLodInfo(prefabAsset);

        foreach (Renderer renderer in prefabAsset.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = ResolveSharedMesh(renderer);
            if (mesh == null)
                continue;

            int lodIndex = 0;
            int lodCount = 1;
            if (lodInfo.TryGetValue(renderer, out (int lodIndex, int lodCount) lodData))
            {
                lodIndex = lodData.lodIndex;
                lodCount = lodData.lodCount;
            }

            long triangleCount = CountTriangles(mesh);
            long vertexCount = mesh.vertexCount;
            prefabLodTriangleCounts[lodIndex] = prefabLodTriangleCounts.TryGetValue(lodIndex, out long prefabLodTriangleCount)
                ? prefabLodTriangleCount + triangleCount
                : triangleCount;
            prefabLodVertexCounts[lodIndex] = prefabLodVertexCounts.TryGetValue(lodIndex, out long prefabLodVertexCount)
                ? prefabLodVertexCount + vertexCount
                : vertexCount;

            string meshKey = BuildObjectKey(AssetDatabase.GetAssetPath(mesh), mesh.name);
            if (!meshLodTriangleCounts.TryGetValue(meshKey, out Dictionary<int, long> triangleCountsByLod))
            {
                triangleCountsByLod = new Dictionary<int, long>();
                meshLodTriangleCounts.Add(meshKey, triangleCountsByLod);
            }

            if (!triangleCountsByLod.ContainsKey(lodIndex))
                triangleCountsByLod.Add(lodIndex, triangleCount);

            if (lodCount > 1)
                summary.HasLod = true;
        }

        if (!summary.HasLod)
            summary.HasLod = prefabLodTriangleCounts.Count > 1;

        summary.PrefabLods.AddRange(prefabLodTriangleCounts
            .OrderBy(pair => pair.Key)
            .Select(pair => CreateLodRecord(
                pair.Key,
                pair.Value,
                prefabLodVertexCounts.TryGetValue(pair.Key, out long vertexCount) ? vertexCount : 0L)));

        foreach (KeyValuePair<string, Dictionary<int, long>> pair in meshLodTriangleCounts)
        {
            summary.MeshLodsByMeshKey[pair.Key] = pair.Value
                .OrderBy(pair => pair.Key)
                .Select(pair => CreateLodRecord(pair.Key, pair.Value))
                .ToList();
        }

        return summary;
    }

    private static PrefabLodAuditJsonRecord CreateLodRecord(int lodIndex, long triangleCount, long vertexCount = 0L)
    {
        return new PrefabLodAuditJsonRecord
        {
            lodIndex = lodIndex,
            triangleCount = triangleCount,
            vertexCount = vertexCount
        };
    }

    private static List<PrefabLodAuditJsonRecord> CloneLodRecords(IEnumerable<PrefabLodAuditJsonRecord> source)
    {
        if (source == null)
            return new List<PrefabLodAuditJsonRecord>();

        return source
            .Where(record => record != null)
            .OrderBy(record => record.lodIndex)
            .Select(record => CreateLodRecord(record.lodIndex, record.triangleCount, record.vertexCount))
            .ToList();
    }

    private static bool TryResolveRendererLocalBounds(Renderer renderer, out Bounds localBounds)
    {
        switch (renderer)
        {
            case SkinnedMeshRenderer skinnedMeshRenderer:
                localBounds = skinnedMeshRenderer.localBounds;
                return true;
            case MeshRenderer meshRenderer when meshRenderer.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null:
                localBounds = meshFilter.sharedMesh.bounds;
                return true;
            case ParticleSystemRenderer particleSystemRenderer when particleSystemRenderer.mesh != null:
                localBounds = particleSystemRenderer.mesh.bounds;
                return true;
            default:
                localBounds = default;
                return false;
        }
    }

    private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
        Vector3 extents = bounds.extents;

        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0.0f, 0.0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0.0f, extents.y, 0.0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0.0f, 0.0f, extents.z));

        Vector3 transformedExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

        return new Bounds(center, transformedExtents * 2.0f);
    }

    private static string BuildStableAssetKey(string assetKind, string assetPath, string assetName, int fallbackId)
    {
        string normalizedPath = string.IsNullOrWhiteSpace(assetPath)
            ? string.Empty
            : assetPath.Replace('\\', '/').Trim();
        if (!string.IsNullOrWhiteSpace(normalizedPath))
            return $"{assetKind}|{normalizedPath}|{assetName}";

        return $"{assetKind}|runtime:{fallbackId}|{assetName}";
    }

    private static int ResolvePrefabAssetUsageCount(
        IReadOnlyDictionary<string, Dictionary<string, int>> usageByPrefabKey,
        string prefabKey,
        string assetKey)
    {
        if (usageByPrefabKey == null || string.IsNullOrWhiteSpace(prefabKey) || string.IsNullOrWhiteSpace(assetKey))
            return 0;

        if (!usageByPrefabKey.TryGetValue(prefabKey, out Dictionary<string, int> usageByAssetKey))
            return 0;

        return usageByAssetKey.TryGetValue(assetKey, out int usageCount) ? usageCount : 0;
    }

    private static List<int> ResolvePrefabLodIndices(
        QianxiaRuntimeAssetInstanceSnapshot snapshot,
        string prefabKey,
        Mesh targetMesh)
    {
        List<int> lodIndices = new List<int>();
        if (snapshot == null || string.IsNullOrWhiteSpace(prefabKey) || targetMesh == null)
            return lodIndices;

        string targetMeshPath = AssetDatabase.GetAssetPath(targetMesh);
        string targetMeshName = targetMesh.name ?? string.Empty;

        foreach (QianxiaRuntimeAssetInstanceAggregateRecord prefabRecord in snapshot.prefabs)
        {
            if (prefabRecord == null || !string.Equals(prefabRecord.key, prefabKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(prefabRecord.assetPath))
                break;

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabRecord.assetPath);
            if (prefabAsset == null)
                break;

            Dictionary<Renderer, (int lodIndex, int lodCount)> lodInfo = BuildRendererLodInfo(prefabAsset);
            foreach (Renderer renderer in prefabAsset.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = ResolveSharedMesh(renderer);
                if (mesh == null)
                    continue;

                string meshPath = AssetDatabase.GetAssetPath(mesh);
                if (!string.Equals(mesh.name, targetMeshName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(meshPath, targetMeshPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (lodInfo.TryGetValue(renderer, out (int lodIndex, int lodCount) lodData) &&
                    !lodIndices.Contains(lodData.lodIndex))
                {
                    lodIndices.Add(lodData.lodIndex);
                }
            }

            break;
        }

        lodIndices.Sort();
        return lodIndices;
    }

    private static string BuildMaterialTextureRoleSummary(MaterialTextureProfile profile)
    {
        if (profile == null)
            return string.Empty;

        List<string> segments = new List<string>(4);
        if (profile.AlbedoTextureCount > 0)
            segments.Add($"Albedo:{profile.AlbedoTextureCount}");
        if (profile.NormalTextureCount > 0)
            segments.Add($"Normal:{profile.NormalTextureCount}");
        if (profile.MaskTextureCount > 0)
            segments.Add($"Mask:{profile.MaskTextureCount}");
        if (profile.OtherTextureCount > 0)
            segments.Add($"Other:{profile.OtherTextureCount}");
        return string.Join(" | ", segments);
    }

    private static void AnalyzeGameObjectAsset(GameObject assetRoot, AssetStatsRecord record, AssetUsageIndex usageIndex)
    {
        MeshReferenceContext context = CollectMeshReferenceContext(assetRoot);
        ApplyMeshContext(record, context);
        ApplyUsageInfo(record, ResolveUsageInfo(usageIndex?.MaterialUsage, BuildObjectKey(record.AssetPath, record.AssetName)));

        if (record.RendererCount == 0)
            record.Notes.Add("No MeshRenderer or SkinnedMeshRenderer was found.");
    }

    private static void AnalyzeMeshAsset(Mesh mesh, AssetStatsRecord record, AssetUsageIndex usageIndex)
    {
        record.UniqueMeshCount = mesh != null ? 1 : 0;
        AddMeshStats(mesh, record);
        ApplyUsageInfo(record, ResolveUsageInfo(usageIndex?.MeshUsage, BuildObjectKey(record.AssetPath, record.AssetName)));

        GameObject mainAsset = AssetDatabase.LoadMainAssetAtPath(record.AssetPath) as GameObject;
        if (mainAsset == null)
        {
            record.Notes.Add("No model root was found for this mesh, so material stats are unavailable.");
            return;
        }

        MeshReferenceContext context = CollectMeshReferenceContext(mainAsset, mesh);
        record.RendererCount = context.RendererCount;
        record.MaterialSlotCount = context.MaterialSlotCount;
        record.UniqueMaterialCount = context.UniqueMaterials.Count;
        record.MaterialNames.AddRange(context.UniqueMaterials.Select(material => material != null ? material.name : "<Missing Material>"));
        record.Textures.AddRange(CollectTextureStats(context.UniqueMaterials));
        record.TextureCount = record.Textures.Count;
        PopulateTextureRoleCounts(record, record.Textures);

        if (record.MaterialSlotCount == 0)
            record.Notes.Add("No renderer in the source model references this mesh.");
    }

    private static void AnalyzeMaterialAsset(Material material, AssetStatsRecord record, AssetUsageIndex usageIndex)
    {
        record.MaterialSlotCount = 1;
        record.UniqueMaterialCount = 1;
        record.MaterialNames.Add(material.name);
        record.ShaderName = material.shader != null ? material.shader.name : string.Empty;
        record.Textures.AddRange(CollectTextureStats(new[] { material }));
        record.TextureCount = record.Textures.Count;
        PopulateTextureRoleCounts(record, record.Textures);
        ApplyUsageInfo(record, ResolveUsageInfo(usageIndex?.MaterialUsage, BuildObjectKey(record.AssetPath, record.AssetName)));
    }

    private static void AnalyzeTextureAsset(Texture texture, AssetStatsRecord record, AssetUsageIndex usageIndex)
    {
        TextureStatsRecord textureRecord = BuildTextureRecord(
            texture,
            record.AssetPath,
            ResolveTextureRole(texture, null, string.Empty, record.AssetPath),
            Array.Empty<string>());
        record.Textures.Add(textureRecord);
        record.TextureCount = record.Textures.Count;
        record.TextureWidth = textureRecord.Width;
        record.TextureHeight = textureRecord.Height;
        record.TextureFormat = textureRecord.Format;
        record.TextureMipCount = textureRecord.MipCount;
        record.TextureHasMipmaps = textureRecord.HasMipmaps;
        record.TextureStorageSizeBytes = textureRecord.StorageSizeBytes;
        PopulateTextureRoleCounts(record, record.Textures);
        ApplyUsageInfo(record, ResolveUsageInfo(usageIndex?.TextureUsage, record.AssetPath));

        if (IsTextureAtLeast(record, 8192))
            record.Notes.Add("8K texture detected. Verify that the resolution is intentional.");
        else if (IsTextureAtLeast(record, 4096))
            record.Notes.Add("4K texture detected. Check if lower resolutions would be sufficient.");
    }

    private static void ApplyMeshContext(AssetStatsRecord record, MeshReferenceContext context)
    {
        record.RendererCount = context.RendererCount;
        record.UniqueMeshCount = context.Meshes.Count;
        record.MaterialSlotCount = context.MaterialSlotCount;
        record.UniqueMaterialCount = context.UniqueMaterials.Count;
        record.MaterialNames.AddRange(context.UniqueMaterials.Select(material => material != null ? material.name : "<Missing Material>"));

        foreach (Mesh mesh in context.Meshes)
            AddMeshStats(mesh, record);

        record.Textures.AddRange(CollectTextureStats(context.UniqueMaterials));
        record.TextureCount = record.Textures.Count;
        PopulateTextureRoleCounts(record, record.Textures);
    }

    private static MeshReferenceContext CollectMeshReferenceContext(GameObject root, Mesh onlyMesh = null)
    {
        MeshReferenceContext context = new MeshReferenceContext();
        if (root == null)
            return context;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Mesh sharedMesh = ResolveSharedMesh(renderer);
            if (sharedMesh == null)
                continue;

            if (onlyMesh != null && sharedMesh != onlyMesh)
                continue;

            context.RendererCount++;
            AddMesh(sharedMesh, context);

            Material[] materials = renderer.sharedMaterials;
            context.MaterialSlotCount += materials.Length;
            foreach (Material material in materials)
                AddMaterial(material, context);
        }

        return context;
    }

    private static void AddMesh(Mesh mesh, MeshReferenceContext context)
    {
        if (mesh == null || context == null)
            return;

        if (!context.MeshIds.Add(mesh.GetInstanceID()))
            return;

        context.Meshes.Add(mesh);
    }

    private static void AddMaterial(Material material, MeshReferenceContext context)
    {
        if (material == null || context == null)
            return;

        if (!context.MaterialIds.Add(material.GetInstanceID()))
            return;

        context.UniqueMaterials.Add(material);
    }

    private static void AddMeshStats(Mesh mesh, AssetStatsRecord record)
    {
        if (mesh == null || record == null)
            return;

        record.VertexCount += mesh.vertexCount;
        record.TriangleCount += CountTriangles(mesh);
    }

    private static long CountTriangles(Mesh mesh)
    {
        if (mesh == null)
            return 0L;

        long triangleCount = 0L;
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            MeshTopology topology = mesh.GetTopology(subMeshIndex);
            int indexCount = (int)mesh.GetIndexCount(subMeshIndex);
            triangleCount += CountTriangles(topology, indexCount);
        }

        return triangleCount;
    }

    private static long CountTriangles(MeshTopology topology, int indexCount)
    {
        return topology switch
        {
            MeshTopology.Triangles => indexCount / 3L,
            MeshTopology.Quads => indexCount / 2L,
            _ => 0L
        };
    }

    private static Mesh ResolveSharedMesh(Renderer renderer)
    {
        switch (renderer)
        {
            case SkinnedMeshRenderer skinnedMeshRenderer:
                return skinnedMeshRenderer.sharedMesh;
            case MeshRenderer meshRenderer when meshRenderer.TryGetComponent(out MeshFilter meshFilter):
                return meshFilter.sharedMesh;
            default:
                return null;
        }
    }

    private static List<TextureStatsRecord> CollectTextureStats(IEnumerable<Material> materials)
    {
        Dictionary<string, TextureAggregate> aggregates = new Dictionary<string, TextureAggregate>(StringComparer.OrdinalIgnoreCase);
        if (materials == null)
            return new List<TextureStatsRecord>();

        foreach (Material material in materials)
        {
            MaterialTextureProfile profile = BuildMaterialTextureProfile(material);
            if (profile.References.Count == 0)
                continue;

            foreach (MaterialTextureReference reference in profile.References)
            {
                Texture texture = reference.Texture;
                string texturePath = reference.TexturePath;
                string key = string.IsNullOrEmpty(texturePath)
                    ? $"instance:{texture.GetInstanceID()}"
                    : $"{texturePath}|{reference.Role}";

                if (!aggregates.TryGetValue(key, out TextureAggregate aggregate))
                {
                    aggregate = new TextureAggregate(texture, texturePath, reference.Role);
                    aggregates.Add(key, aggregate);
                }

                aggregate.UsageSet.Add($"{material.name}.{reference.PropertyName}");
            }
        }

        return aggregates.Values
            .Select(aggregate => BuildTextureRecord(
                aggregate.Texture,
                aggregate.AssetPath,
                aggregate.Role,
                aggregate.UsageSet.OrderBy(usage => usage, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(texture => texture.AssetPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(texture => texture.TextureName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static TextureStatsRecord BuildTextureRecord(
        Texture texture,
        string assetPath,
        string role,
        IEnumerable<string> usages)
    {
        TextureStatsRecord record = new TextureStatsRecord
        {
            Texture = texture,
            TextureName = texture != null ? texture.name : "<Missing Texture>",
            AssetPath = assetPath ?? string.Empty,
            Width = texture != null ? texture.width : 0,
            Height = texture != null ? texture.height : 0,
            Format = ResolveTextureFormat(texture),
            MipCount = ResolveTextureMipCount(texture),
            HasMipmaps = ResolveTextureHasMipmaps(texture, assetPath),
            DiskSizeBytes = ResolveDiskSizeBytes(assetPath),
            StorageSizeBytes = ResolveTextureStorageSizeBytes(texture),
            Role = role ?? string.Empty
        };

        if (usages != null)
            record.Usages.AddRange(usages);

        return record;
    }

    private static MaterialTextureProfile BuildMaterialTextureProfile(Material material)
    {
        MaterialTextureProfile profile = new MaterialTextureProfile();
        if (material == null || material.shader == null)
            return profile;

        Shader shader = material.shader;
        int propertyCount = ShaderUtil.GetPropertyCount(shader);
        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (ShaderUtil.GetPropertyType(shader, propertyIndex) != ShaderUtil.ShaderPropertyType.TexEnv)
                continue;

            string propertyName = ShaderUtil.GetPropertyName(shader, propertyIndex);
            Texture texture = material.GetTexture(propertyName);
            if (texture == null)
                continue;

            string texturePath = AssetDatabase.GetAssetPath(texture);
            string role = ResolveTextureRole(texture, material, propertyName, texturePath);
            profile.References.Add(new MaterialTextureReference
            {
                Texture = texture,
                TexturePath = texturePath ?? string.Empty,
                PropertyName = propertyName,
                Role = role
            });

            IncrementRoleCount(profile, role);
        }

        return profile;
    }

    private static void IncrementRoleCount(MaterialTextureProfile profile, string role)
    {
        if (profile == null)
            return;

        switch (role)
        {
            case "Albedo":
                profile.AlbedoTextureCount++;
                break;
            case "Normal":
                profile.NormalTextureCount++;
                break;
            case "Mask":
                profile.MaskTextureCount++;
                break;
            default:
                profile.OtherTextureCount++;
                break;
        }
    }

    private static void PopulateTextureRoleCounts(AssetStatsRecord record, IEnumerable<TextureStatsRecord> textures)
    {
        if (record == null || textures == null)
            return;

        foreach (TextureStatsRecord texture in textures)
        {
            switch (texture.Role)
            {
                case "Albedo":
                    record.AlbedoTextureCount++;
                    break;
                case "Normal":
                    record.NormalTextureCount++;
                    break;
                case "Mask":
                    record.MaskTextureCount++;
                    break;
                default:
                    record.OtherTextureCount++;
                    break;
            }
        }
    }

    private static string ResolveTextureRole(Texture texture, Material material, string propertyName, string texturePath)
    {
        HashSet<string> tokens = Tokenize($"{texture?.name} {texturePath} {propertyName}");
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer != null && importer.textureType == TextureImporterType.NormalMap)
            return "Normal";

        if (ContainsAnyToken(tokens, new[] { "normal", "norm", "bump" }) || propertyName.IndexOf("bump", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Normal";

        if (ContainsAnyToken(tokens, new[] { "mask", "orm", "metallic", "roughness", "smoothness", "occlusion", "ao", "specular", "gloss" }))
            return "Mask";

        if (ContainsAnyToken(tokens, new[] { "albedo", "basecolor", "diffuse", "base", "color" }))
            return "Albedo";

        return "Other";
    }

    private static void ApplyUsageInfo(AssetStatsRecord record, UsageInfo usageInfo)
    {
        if (record == null || usageInfo == null)
            return;

        record.LodCount = Mathf.Max(record.LodCount, usageInfo.MaxLodCount);
        if (usageInfo.LodIndices.Count > 0)
            record.LodIndicesSummary = string.Join(", ", usageInfo.LodIndices.OrderBy(index => index).Select(index => $"LOD{index}"));

        AddRangeIfMissing(record.UsedBySceneNames, usageInfo.SceneNames);
        AddRangeIfMissing(record.UsedByActorNames, usageInfo.ActorNames);
        AddRangeIfMissing(record.UsedByAssetNames, usageInfo.AssetNames);
        AddRangeIfMissing(record.UsedByMaterialNames, usageInfo.MaterialNames);
    }

    private static UsageInfo ResolveUsageInfo(IDictionary<string, UsageInfo> dictionary, string key)
    {
        if (dictionary == null || string.IsNullOrWhiteSpace(key))
            return null;

        return dictionary.TryGetValue(key, out UsageInfo usageInfo) ? usageInfo : null;
    }

    private static bool MatchesTeamFilter(AssetStatsRecord record, AssetStatsQueryOptions options, CurrentSceneUsageData sceneUsage)
    {
        return options.TeamFilter switch
        {
            AssetStatsTeamFilter.All => true,
            AssetStatsTeamFilter.Environment => (record.TeamFlags & AssetStatsTeamFlags.Environment) != 0,
            AssetStatsTeamFilter.Character => (record.TeamFlags & AssetStatsTeamFlags.Character) != 0,
            AssetStatsTeamFilter.Vfx => (record.TeamFlags & AssetStatsTeamFlags.Vfx) != 0,
            AssetStatsTeamFilter.Level => sceneUsage != null &&
                sceneUsage.HasTrackedUsage &&
                record.CurrentSceneUsageCount >= options.MinimumSceneUsageCount,
            _ => true
        };
    }

    private static bool MatchesReportKind(AssetStatsRecord record, AssetStatsReportKind reportKind)
    {
        if (record == null)
            return false;

        return reportKind switch
        {
            AssetStatsReportKind.Overview => true,
            AssetStatsReportKind.Mesh => string.Equals(record.AssetKind, "Mesh", StringComparison.OrdinalIgnoreCase),
            AssetStatsReportKind.Texture => string.Equals(record.AssetKind, "Texture", StringComparison.OrdinalIgnoreCase),
            AssetStatsReportKind.Material => string.Equals(record.AssetKind, "Material", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static void SortRecords(List<AssetStatsRecord> records, AssetStatsQueryOptions options)
    {
        if (records == null)
            return;

        IOrderedEnumerable<AssetStatsRecord> orderedRecords = options.TeamFilter == AssetStatsTeamFilter.Level
            ? records
                .OrderByDescending(record => record.CurrentSceneUsageCount)
                .ThenByDescending(record => record.TriangleCount)
                .ThenBy(record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
            : records
                .OrderByDescending(record => record.CurrentSceneUsageCount)
                .ThenBy(record => record.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.AssetName, StringComparer.OrdinalIgnoreCase);

        List<AssetStatsRecord> sortedRecords = orderedRecords.ToList();
        records.Clear();
        records.AddRange(sortedRecords);
    }

    private static AssetStatsTeamFlags ResolveTeamFlags(UnityEngine.Object asset, string assetPath, string assetKind)
    {
        AssetStatsTeamFlags flags = AssetStatsTeamFlags.None;
        HashSet<string> tokens = Tokenize($"{assetPath} {asset?.name}");

        if (string.Equals(assetKind, "Scene", StringComparison.OrdinalIgnoreCase) || ContainsAnyToken(tokens, EnvironmentTokens))
            flags |= AssetStatsTeamFlags.Environment;

        if (ContainsAnyToken(tokens, CharacterTokens) || HasPathSegment(assetPath, "Project/Characters") || HasPathSegment(assetPath, "Project/Crowds"))
            flags |= AssetStatsTeamFlags.Character;

        if (ContainsAnyToken(tokens, VfxTokens))
            flags |= AssetStatsTeamFlags.Vfx;

        return flags;
    }

    private static bool QuickMatchesTeamFilter(string assetPath, string assetKind, AssetStatsTeamFilter teamFilter)
    {
        if (teamFilter == AssetStatsTeamFilter.All || teamFilter == AssetStatsTeamFilter.Level)
            return true;

        AssetStatsTeamFlags flags = ResolveTeamFlags(null, assetPath, assetKind);
        return teamFilter switch
        {
            AssetStatsTeamFilter.Environment => (flags & AssetStatsTeamFlags.Environment) != 0,
            AssetStatsTeamFilter.Character => (flags & AssetStatsTeamFlags.Character) != 0,
            AssetStatsTeamFilter.Vfx => (flags & AssetStatsTeamFlags.Vfx) != 0,
            _ => true
        };
    }

    private static CurrentSceneUsageData BuildCurrentSceneUsage()
    {
        CurrentSceneUsageData usageData = new CurrentSceneUsageData();
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
            return usageData;

        usageData.IsSceneLoaded = true;
        usageData.SceneName = activeScene.name ?? string.Empty;
        usageData.ScenePath = activeScene.path ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(usageData.ScenePath))
            usageData.AddTrackedPath(usageData.ScenePath);

        foreach (GameObject rootObject in activeScene.GetRootGameObjects())
        {
            CollectPrefabUsage(rootObject.transform, usageData);
            foreach (Renderer renderer in rootObject.GetComponentsInChildren<Renderer>(true))
                CollectRendererUsage(renderer, usageData);
        }

        return usageData;
    }

    private static void CollectPrefabUsage(Transform transform, CurrentSceneUsageData usageData)
    {
        if (transform == null || usageData == null)
            return;

        if (PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject))
        {
            UnityEngine.Object sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(transform.gameObject);
            string assetPath = sourceObject != null ? AssetDatabase.GetAssetPath(sourceObject) : string.Empty;
            AddCount(usageData.GameObjectUsageCounts, assetPath, 1);
            usageData.AddTrackedPath(assetPath);
        }

        foreach (Transform child in transform)
            CollectPrefabUsage(child, usageData);
    }

    private static void CollectRendererUsage(Renderer renderer, CurrentSceneUsageData usageData)
    {
        if (renderer == null || usageData == null)
            return;

        Mesh sharedMesh = ResolveSharedMesh(renderer);
        if (sharedMesh != null)
        {
            string meshPath = AssetDatabase.GetAssetPath(sharedMesh);
            AddCount(usageData.MeshUsageCounts, BuildObjectKey(meshPath, sharedMesh.name), 1);
            usageData.AddTrackedPath(meshPath);
        }

        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;

            string materialPath = AssetDatabase.GetAssetPath(material);
            AddCount(usageData.MaterialUsageCounts, BuildObjectKey(materialPath, material.name), 1);
            usageData.AddTrackedPath(materialPath);

            foreach (Texture texture in CollectMaterialTextures(material))
            {
                string texturePath = AssetDatabase.GetAssetPath(texture);
                AddCount(usageData.TextureUsageCounts, texturePath, 1);
                usageData.AddTrackedPath(texturePath);
            }
        }
    }

    private static IEnumerable<Texture> CollectMaterialTextures(Material material)
    {
        if (material == null || material.shader == null)
            yield break;

        Shader shader = material.shader;
        int propertyCount = ShaderUtil.GetPropertyCount(shader);
        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (ShaderUtil.GetPropertyType(shader, propertyIndex) != ShaderUtil.ShaderPropertyType.TexEnv)
                continue;

            Texture texture = material.GetTexture(ShaderUtil.GetPropertyName(shader, propertyIndex));
            if (texture != null)
                yield return texture;
        }
    }

    private static void AddCount(IDictionary<string, int> counts, string key, int value)
    {
        if (counts == null || string.IsNullOrWhiteSpace(key))
            return;

        if (counts.TryGetValue(key, out int currentValue))
            counts[key] = currentValue + value;
        else
            counts.Add(key, value);
    }

    private static string BuildObjectKey(string assetPath, string objectName)
    {
        string safePath = assetPath ?? string.Empty;
        string safeName = objectName ?? string.Empty;
        return $"{safePath}|{safeName}";
    }

    private static string ResolveAssetKind(UnityEngine.Object asset, string assetPath)
    {
        if (asset == null)
            return "Unknown";

        return ResolveAssetKind(assetPath, asset.GetType());
    }

    private static string ResolveAssetKind(string assetPath, Type assetType)
    {
        if (typeof(SceneAsset).IsAssignableFrom(assetType))
            return "Scene";

        if (typeof(GameObject).IsAssignableFrom(assetType))
        {
            bool isPrefab = AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(GameObject) &&
                assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
            return isPrefab ? "Prefab" : "Model";
        }

        if (typeof(Mesh).IsAssignableFrom(assetType))
            return "Mesh";

        if (typeof(Material).IsAssignableFrom(assetType))
            return "Material";

        if (typeof(Texture).IsAssignableFrom(assetType))
            return "Texture";

        return assetType != null ? assetType.Name : "Unknown";
    }

    private static long ResolveDiskSizeBytes(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return 0L;

        string absolutePath = Path.GetFullPath(assetPath);
        if (!File.Exists(absolutePath))
            return 0L;

        return new FileInfo(absolutePath).Length;
    }

    private static string GetSortKey(UnityEngine.Object asset)
    {
        if (asset == null)
            return string.Empty;

        string assetPath = AssetDatabase.GetAssetPath(asset);
        return string.IsNullOrEmpty(assetPath) ? asset.name : $"{assetPath}|{asset.name}";
    }

    private static string EscapeCsv(string value)
    {
        string safeValue = value ?? string.Empty;
        return $"\"{safeValue.Replace("\"", "\"\"")}\"";
    }

    private static HashSet<string> Tokenize(string value)
    {
        HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return tokens;

        StringBuilder builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (char.IsLetterOrDigit(current))
            {
                builder.Append(char.ToLowerInvariant(current));
            }
            else if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
            tokens.Add(builder.ToString());

        return tokens;
    }

    private static bool ContainsAnyToken(HashSet<string> tokens, IEnumerable<string> keywords)
    {
        if (tokens == null || keywords == null)
            return false;

        foreach (string keyword in keywords)
        {
            if (tokens.Contains(keyword))
                return true;
        }

        return false;
    }

    private static bool HasPathSegment(string assetPath, string segment)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(segment))
            return false;

        string normalizedPath = assetPath.Replace('\\', '/');
        string normalizedSegment = segment.Replace('\\', '/');
        return normalizedPath.IndexOf(normalizedSegment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddRangeIfMissing(ICollection<string> target, IEnumerable<string> source)
    {
        if (target == null || source == null)
            return;

        HashSet<string> seen = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (string item in source)
        {
            if (!string.IsNullOrWhiteSpace(item) && seen.Add(item))
                target.Add(item);
        }
    }

    private static string BuildCompactListSummary(IReadOnlyList<string> values, int maxItems = 12)
    {
        if (values == null || values.Count == 0)
            return string.Empty;

        int takeCount = Mathf.Min(maxItems, values.Count);
        string summary = string.Join(" | ", values.Take(takeCount));
        int remainingCount = values.Count - takeCount;
        return remainingCount > 0 ? $"{summary} | +{remainingCount} more" : summary;
    }

    private static bool IsTextureAtLeast(AssetStatsRecord record, int size)
    {
        if (record == null)
            return false;

        return Mathf.Max(record.TextureWidth, record.TextureHeight) >= size;
    }

    private static string ResolveTextureFormat(Texture texture)
    {
        if (texture == null)
            return string.Empty;

        System.Reflection.PropertyInfo formatProperty = texture.GetType().GetProperty("format");
        object formatValue = formatProperty != null ? formatProperty.GetValue(texture) : null;
        return formatValue != null ? formatValue.ToString() : texture.GetType().Name;
    }

    private static int ResolveTextureMipCount(Texture texture)
    {
        if (texture == null)
            return 0;

        System.Reflection.PropertyInfo mipProperty = texture.GetType().GetProperty("mipmapCount");
        object mipValue = mipProperty != null ? mipProperty.GetValue(texture) : null;
        return mipValue is int value ? value : 0;
    }

    private static bool ResolveTextureHasMipmaps(Texture texture, string assetPath)
    {
        if (texture == null)
            return false;

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
            return importer.mipmapEnabled;

        return ResolveTextureMipCount(texture) > 1;
    }

    private static long ResolveTextureStorageSizeBytes(Texture texture)
    {
        if (texture == null)
            return 0L;

        try
        {
            if (s_textureStorageSizeMethod == null)
            {
                Type textureUtilType = typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil");
                s_textureStorageSizeMethod = textureUtilType?.GetMethod(
                    "GetStorageMemorySizeLong",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Texture) },
                    null);
            }

            if (s_textureStorageSizeMethod != null)
            {
                object storageSize = s_textureStorageSizeMethod.Invoke(null, new object[] { texture });
                if (storageSize is long longValue)
                    return longValue;
                if (storageSize is int intValue)
                    return intValue;
            }
        }
        catch
        {
            // Fall back to file size when reflection is unavailable.
        }

        string assetPath = AssetDatabase.GetAssetPath(texture);
        return ResolveDiskSizeBytes(assetPath);
    }
}
