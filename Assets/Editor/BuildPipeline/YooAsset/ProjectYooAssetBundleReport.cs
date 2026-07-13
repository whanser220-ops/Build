using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

public static class ProjectYooAssetBundleReportExporter
{
    private const string DefaultReportRoot = ".workspace/artifacts/bundle-report";
    private const string ReportFileName = "bundle_report.json";
    private const string LatestFileName = "latest.json";
    private const string IndexFileName = "index.json";
    private const int DefaultSmallBundleKb = 128;
    private const int DefaultLargeBundleMb = 50;
    private const int DefaultDepthWarningEdges = 4;

    public static string Export(
        YooAsset.Editor.BuildResult buildResult,
        ScriptableBuildParameters parameters,
        string planPath,
        string[] args,
        ProjectSbpBundleLayoutSnapshot layoutSnapshot)
    {
        if (buildResult == null)
            throw new ArgumentNullException(nameof(buildResult));
        if (parameters == null)
            throw new ArgumentNullException(nameof(parameters));
        if (args == null)
            args = Array.Empty<string>();

        ProjectBundleReportThresholds thresholds = ProjectBundleReportCalculator.CreateThresholds(
            GetIntArgument(args, "--bundle-report-small-bundle-kb", DefaultSmallBundleKb) * 1024L,
            GetIntArgument(args, "--bundle-report-large-bundle-mb", DefaultLargeBundleMb) * 1024L * 1024L,
            GetIntArgument(args, "--bundle-report-depth-warning", DefaultDepthWarningEdges));

        string outputPath = ResolveOutputPath(args, parameters);
        string reportDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(reportDirectory))
            Directory.CreateDirectory(reportDirectory);

        string yooReportPath = FindYooAssetReportPath(buildResult, parameters);
        if (string.IsNullOrWhiteSpace(yooReportPath) || !File.Exists(yooReportPath))
            throw new FileNotFoundException("YooAsset build report was not found.", yooReportPath);

        BuildReport yooReport = BuildReport.Deserialize(File.ReadAllText(yooReportPath));
        ProjectBundleReport report = ProjectBundleReportCalculator.CreateReport(
            yooReport,
            parameters,
            planPath,
            yooReportPath,
            outputPath,
            thresholds,
            layoutSnapshot ?? new ProjectSbpBundleLayoutSnapshot());

        WriteJson(outputPath, report);
        UpdateLatestAndIndex(outputPath, report);
        Debug.Log("YooAsset bundle report exported: " + outputPath);
        return outputPath.Replace("\\", "/");
    }

    private static string ResolveOutputPath(string[] args, ScriptableBuildParameters parameters)
    {
        string configured = GetArgumentValue(args, "--bundle-report-output");
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Path.Combine(
                DefaultReportRoot,
                parameters.BuildTarget.ToString(),
                parameters.PackageName,
                parameters.PackageVersion,
                ReportFileName);
        }

        string fullPath = Path.GetFullPath(configured);
        if (string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
            return fullPath;

        return Path.Combine(fullPath, ReportFileName);
    }

    private static string FindYooAssetReportPath(
        YooAsset.Editor.BuildResult buildResult,
        ScriptableBuildParameters parameters)
    {
        string expectedName = YooAssetConfiguration.GetBuildReportFileName(
            parameters.PackageName,
            parameters.PackageVersion);

        List<string> searchDirectories = new List<string>();
        if (!string.IsNullOrWhiteSpace(buildResult.OutputPackageDirectory))
            searchDirectories.Add(buildResult.OutputPackageDirectory);
        if (!string.IsNullOrWhiteSpace(parameters.BuildOutputRoot))
            searchDirectories.Add(parameters.BuildOutputRoot);

        foreach (string directory in searchDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
                continue;

            string expectedPath = Path.Combine(directory, expectedName);
            if (File.Exists(expectedPath))
                return expectedPath;

            string latestReport = Directory.GetFiles(directory, "*.report", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(latestReport))
                return latestReport;
        }

        return string.Empty;
    }

    private static void UpdateLatestAndIndex(string reportPath, ProjectBundleReport report)
    {
        string reportRoot = ResolveReportRoot(reportPath);
        Directory.CreateDirectory(reportRoot);

        string latestPath = Path.Combine(reportRoot, LatestFileName);
        string indexPath = Path.Combine(reportRoot, IndexFileName);
        WriteJson(latestPath, report);

        ProjectBundleReportIndex index = File.Exists(indexPath)
            ? JsonUtility.FromJson<ProjectBundleReportIndex>(File.ReadAllText(indexPath))
            : new ProjectBundleReportIndex();
        if (index == null)
            index = new ProjectBundleReportIndex();
        if (index.reports == null)
            index.reports = new List<ProjectBundleReportIndexEntry>();

        string relativeReportPath = ProjectBundleReportCalculator.ToPortableRelativePath(reportRoot, reportPath);
        ProjectBundleReportIndexEntry entry = new ProjectBundleReportIndexEntry
        {
            id = report.summary.reportId,
            buildTarget = report.summary.buildTarget,
            packageName = report.summary.packageName,
            packageVersion = report.summary.packageVersion,
            generatedAt = report.summary.generatedAt,
            reportPath = relativeReportPath,
            bundleCount = report.summary.bundleCount,
            duplicateAssetCount = report.summary.duplicateAssetCount,
            smallBundleCount = report.summary.smallBundleCount,
            largeBundleCount = report.summary.largeBundleCount,
            maxDependencyDepth = report.summary.maxDependencyDepth
        };

        index.reports.RemoveAll(item => string.Equals(item.id, entry.id, StringComparison.OrdinalIgnoreCase));
        index.reports.Add(entry);
        index.reports = index.reports
            .OrderByDescending(item => item.generatedAt, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.packageVersion, StringComparer.OrdinalIgnoreCase)
            .ToList();
        index.latestReportId = entry.id;

        WriteJson(indexPath, index);
    }

    private static string ResolveReportRoot(string reportPath)
    {
        string fullPath = Path.GetFullPath(reportPath);
        string normalized = fullPath.Replace('\\', '/');
        const string marker = "/bundle-report/";
        int markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            return normalized.Substring(0, markerIndex + marker.Length - 1).Replace('/', Path.DirectorySeparatorChar);

        return Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
    }

    private static void WriteJson<T>(string path, T payload)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string json = JsonUtility.ToJson(payload, true);
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
    }

    private static int GetIntArgument(string[] args, string name, int defaultValue)
    {
        string value = GetArgumentValue(args, name);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        throw new ArgumentException("Invalid integer value for " + name + ": " + value);
    }

    private static string GetArgumentValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Length)
                throw new ArgumentException("Missing value for argument: " + name);

            return args[i + 1];
        }

        string prefix = name + "=";
        foreach (string arg in args)
        {
            if (arg != null && arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg.Substring(prefix.Length);
        }

        return string.Empty;
    }
}

public static class ProjectBundleReportCalculator
{
    private static readonly Regex BundleHashSuffixPattern =
        new Regex(@"[_-][0-9a-f]{8,32}(\.bundle)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ProjectBundleReportThresholds CreateThresholds(
        long smallBundleBytes,
        long largeBundleBytes,
        int dependencyDepthWarningEdges)
    {
        return new ProjectBundleReportThresholds
        {
            smallBundleBytes = Math.Max(1L, smallBundleBytes),
            largeBundleBytes = Math.Max(1L, largeBundleBytes),
            dependencyDepthWarningEdges = Math.Max(1, dependencyDepthWarningEdges)
        };
    }

    public static ProjectBundleReport CreateReport(
        BuildReport yooReport,
        ScriptableBuildParameters parameters,
        string planPath,
        string yooReportPath,
        string outputPath,
        ProjectBundleReportThresholds thresholds,
        ProjectSbpBundleLayoutSnapshot layoutSnapshot)
    {
        if (yooReport == null)
            throw new ArgumentNullException(nameof(yooReport));
        if (parameters == null)
            throw new ArgumentNullException(nameof(parameters));

        layoutSnapshot = layoutSnapshot ?? new ProjectSbpBundleLayoutSnapshot();
        thresholds = thresholds ?? CreateThresholds(128L * 1024L, 50L * 1024L * 1024L, 4);

        Dictionary<string, ReportAssetInfo> mainAssetByPath = SafeList(yooReport.AssetInfos)
            .Where(asset => asset != null && !string.IsNullOrWhiteSpace(asset.AssetPath))
            .GroupBy(asset => NormalizeAssetPath(asset.AssetPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<ProjectAssetCopyInfo> assetCopies = CollectAssetCopies(yooReport, layoutSnapshot);
        List<ProjectBundleReportBundle> bundles = CreateBundleRows(
            yooReport,
            mainAssetByPath,
            thresholds,
            layoutSnapshot);
        List<ProjectDuplicateAssetReportItem> duplicateAssets = CreateDuplicateRows(
            assetCopies,
            mainAssetByPath);
        Dictionary<string, List<string>> dependencyGraph = bundles.ToDictionary(
            item => item.bundleName,
            item => item.dependBundles,
            StringComparer.OrdinalIgnoreCase);
        List<ProjectDependencyChainReportItem> allDependencyChains = FindDependencyChains(
            dependencyGraph,
            0,
            bundles.Count);
        List<ProjectDependencyChainReportItem> dependencyChains = FindDependencyChains(
            dependencyGraph,
            thresholds.dependencyDepthWarningEdges,
            100);

        ProjectBundleReport report = new ProjectBundleReport();
        report.thresholds = thresholds;
        report.bundles = bundles;
        report.duplicateAssets = duplicateAssets;
        report.smallBundles = bundles.Where(bundle => bundle.isSmall).ToList();
        report.largeBundles = bundles.Where(bundle => bundle.isLarge).ToList();
        report.dependencyChains = dependencyChains;
        report.summary = CreateSummary(
            parameters,
            planPath,
            yooReportPath,
            outputPath,
            yooReport,
            bundles,
            duplicateAssets,
            dependencyChains);
        report.summary.maxDependencyDepth = allDependencyChains.Count == 0
            ? 0
            : allDependencyChains.Max(chain => chain.depth);
        return report;
    }

    public static long CalculateRedundantSize(IEnumerable<long> copySizes)
    {
        List<long> positiveSizes = copySizes
            .Where(size => size > 0L)
            .OrderBy(size => size)
            .ToList();
        if (positiveSizes.Count <= 1)
            return 0L;

        return positiveSizes.Sum() - positiveSizes[0];
    }

    public static string ResolveModuleOwner(string assetPath, string fallbackBundleName)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        string[] parts = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 &&
            string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[1], "Game", StringComparison.OrdinalIgnoreCase))
        {
            return ToDisplaySegment(parts[2]) + "/" + ToDisplaySegment(parts[3]);
        }

        if (parts.Length >= 3 &&
            string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[1], "GameAssets", StringComparison.OrdinalIgnoreCase))
        {
            return "Legacy/" + ToDisplaySegment(parts[2]);
        }

        return ResolveBundleOwner(fallbackBundleName);
    }

    public static List<ProjectDependencyChainReportItem> FindDependencyChains(
        Dictionary<string, List<string>> dependencyGraph,
        int warningDepthEdges,
        int maxCount)
    {
        List<ProjectDependencyChainReportItem> chains = new List<ProjectDependencyChainReportItem>();
        if (dependencyGraph == null || dependencyGraph.Count == 0)
            return chains;

        foreach (string rootBundle in dependencyGraph.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            List<string> bestChain = FindLongestChain(rootBundle, dependencyGraph);
            int depth = Math.Max(0, bestChain.Count - 1);
            if (depth < warningDepthEdges)
                continue;

            chains.Add(new ProjectDependencyChainReportItem
            {
                rootBundle = rootBundle,
                depth = depth,
                chain = bestChain
            });
        }

        return chains
            .OrderByDescending(item => item.depth)
            .ThenBy(item => item.rootBundle, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxCount))
            .ToList();
    }

    public static string ToPortableRelativePath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            return path ?? string.Empty;

        Uri rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
        Uri pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('\\', '/');
    }

    private static ProjectBundleReportSummary CreateSummary(
        ScriptableBuildParameters parameters,
        string planPath,
        string yooReportPath,
        string outputPath,
        BuildReport yooReport,
        List<ProjectBundleReportBundle> bundles,
        List<ProjectDuplicateAssetReportItem> duplicateAssets,
        List<ProjectDependencyChainReportItem> dependencyChains)
    {
        string generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string packageVersion = parameters.PackageVersion ?? string.Empty;
        string packageName = parameters.PackageName ?? string.Empty;
        string buildTarget = parameters.BuildTarget.ToString();
        string reportId = string.Join(
            "_",
            new[] { buildTarget, packageName, packageVersion }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(SanitizeIdPart));

        return new ProjectBundleReportSummary
        {
            reportId = reportId,
            generatedAt = generatedAt,
            buildTarget = buildTarget,
            packageName = packageName,
            packageVersion = packageVersion,
            unityVersion = Application.unityVersion,
            yooAssetVersion = yooReport.Summary != null ? yooReport.Summary.YooVersion : string.Empty,
            planPath = NormalizeFilePath(planPath),
            yooReportPath = NormalizeFilePath(yooReportPath),
            outputPath = NormalizeFilePath(outputPath),
            bundleCount = bundles.Count,
            assetCount = yooReport.AssetInfos != null ? yooReport.AssetInfos.Count : 0,
            duplicateAssetCount = duplicateAssets.Count,
            smallBundleCount = bundles.Count(bundle => bundle.isSmall),
            largeBundleCount = bundles.Count(bundle => bundle.isLarge),
            maxDependencyDepth = dependencyChains.Count == 0 ? 0 : dependencyChains.Max(chain => chain.depth),
            totalUncompressedSizeBytes = bundles.Sum(bundle => bundle.uncompressedSizeBytes),
            totalCompressedSizeBytes = bundles.Sum(bundle => bundle.compressedSizeBytes),
            totalRedundantSizeBytes = duplicateAssets.Sum(asset => asset.redundantSizeBytes)
        };
    }

    private static List<ProjectAssetCopyInfo> CollectAssetCopies(
        BuildReport yooReport,
        ProjectSbpBundleLayoutSnapshot layoutSnapshot)
    {
        List<ProjectAssetCopyInfo> copies = new List<ProjectAssetCopyInfo>();
        foreach (ReportBundleInfo bundleInfo in SafeList(yooReport.BundleInfos))
        {
            string bundleName = bundleInfo.BundleName ?? string.Empty;
            foreach (EditorAssetInfo assetInfo in SafeList(bundleInfo.BundleContents))
            {
                string assetPath = NormalizeAssetPath(assetInfo.AssetPath);
                if (string.IsNullOrWhiteSpace(assetPath))
                    continue;

                string assetGuid = ResolveAssetGuid(assetInfo, assetPath);
                copies.Add(new ProjectAssetCopyInfo
                {
                    assetPath = assetPath,
                    assetGuid = assetGuid,
                    assetType = ResolveAssetType(assetInfo, assetPath),
                    bundleName = bundleName,
                    copySizeBytes = layoutSnapshot.GetAssetCopySize(assetGuid, bundleName),
                    moduleOwner = ResolveModuleOwner(assetPath, bundleName)
                });
            }
        }

        return copies;
    }

    private static List<ProjectBundleReportBundle> CreateBundleRows(
        BuildReport yooReport,
        Dictionary<string, ReportAssetInfo> mainAssetByPath,
        ProjectBundleReportThresholds thresholds,
        ProjectSbpBundleLayoutSnapshot layoutSnapshot)
    {
        List<ProjectBundleReportBundle> rows = new List<ProjectBundleReportBundle>();
        foreach (ReportBundleInfo bundleInfo in SafeList(yooReport.BundleInfos))
        {
            string bundleName = bundleInfo.BundleName ?? string.Empty;
            List<string> bundleContentPaths = SafeList(bundleInfo.BundleContents)
                .Select(asset => NormalizeAssetPath(asset.AssetPath))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            int directAssetCount = mainAssetByPath.Values.Count(asset =>
                string.Equals(asset.MainBundleName, bundleName, StringComparison.OrdinalIgnoreCase));
            int assetCount = bundleContentPaths.Count;

            long compressedSize = Math.Max(0L, bundleInfo.FileSize);
            long uncompressedSize = Math.Max(0L, layoutSnapshot.GetBundleUncompressedSize(bundleName));

            ProjectBundleReportBundle row = new ProjectBundleReportBundle
            {
                bundleName = bundleName,
                fileName = bundleInfo.FileName ?? string.Empty,
                uncompressedSizeBytes = uncompressedSize,
                compressedSizeBytes = compressedSize,
                assetCount = assetCount,
                directAssetCount = directAssetCount,
                dependencyAssetCount = Math.Max(0, assetCount - directAssetCount),
                dependBundles = SafeList(bundleInfo.DependBundles)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                referenceBundles = SafeList(bundleInfo.ReferenceBundles)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                moduleOwner = ResolveBundleModuleOwner(bundleInfo, bundleContentPaths),
                isSmall = compressedSize < thresholds.smallBundleBytes,
                isLarge = compressedSize >= thresholds.largeBundleBytes
            };

            rows.Add(row);
        }

        return rows.OrderByDescending(row => row.compressedSizeBytes)
            .ThenBy(row => row.bundleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ProjectDuplicateAssetReportItem> CreateDuplicateRows(
        List<ProjectAssetCopyInfo> assetCopies,
        Dictionary<string, ReportAssetInfo> mainAssetByPath)
    {
        List<ProjectDuplicateAssetReportItem> duplicates = new List<ProjectDuplicateAssetReportItem>();
        foreach (IGrouping<string, ProjectAssetCopyInfo> group in assetCopies
                     .GroupBy(copy => copy.assetPath, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<ProjectAssetCopyInfo> distinctBundleCopies = group
                .GroupBy(copy => copy.bundleName, StringComparer.OrdinalIgnoreCase)
                .Select(bundleGroup => new ProjectAssetCopyInfo
                {
                    assetPath = group.Key,
                    assetGuid = bundleGroup.First().assetGuid,
                    assetType = bundleGroup.First().assetType,
                    bundleName = bundleGroup.Key,
                    copySizeBytes = bundleGroup.Sum(copy => copy.copySizeBytes),
                    moduleOwner = bundleGroup.First().moduleOwner
                })
                .ToList();

            if (distinctBundleCopies.Count <= 1)
                continue;

            List<long> copySizes = distinctBundleCopies.Select(copy => copy.copySizeBytes).ToList();
            long buildSize = copySizes.Where(size => size > 0L).DefaultIfEmpty(0L).Min();
            long redundantSize = CalculateRedundantSize(copySizes);
            List<string> directReferrers = ResolveDirectReferrers(group.Key, mainAssetByPath);
            List<string> moduleOwners = distinctBundleCopies
                .Select(copy => copy.moduleOwner)
                .Concat(directReferrers.Select(referrer => ResolveModuleOwner(referrer, string.Empty)))
                .Where(owner => !string.IsNullOrWhiteSpace(owner))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase)
                .ToList();

            duplicates.Add(new ProjectDuplicateAssetReportItem
            {
                assetPath = group.Key,
                assetType = distinctBundleCopies.First().assetType,
                buildSizeBytes = buildSize,
                bundleCount = distinctBundleCopies.Count,
                bundles = distinctBundleCopies
                    .OrderBy(copy => copy.bundleName, StringComparer.OrdinalIgnoreCase)
                    .Select(copy => new ProjectDuplicateAssetBundleCopy
                    {
                        bundleName = copy.bundleName,
                        copySizeBytes = copy.copySizeBytes
                    })
                    .ToList(),
                redundantSizeBytes = redundantSize,
                directReferrers = directReferrers,
                moduleOwners = moduleOwners
            });
        }

        return duplicates
            .OrderByDescending(item => item.redundantSizeBytes)
            .ThenBy(item => item.assetPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ResolveDirectReferrers(
        string duplicateAssetPath,
        Dictionary<string, ReportAssetInfo> mainAssetByPath)
    {
        HashSet<string> referrers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ReportAssetInfo reportAsset in mainAssetByPath.Values)
        {
            string assetPath = NormalizeAssetPath(reportAsset.AssetPath);
            if (string.Equals(assetPath, duplicateAssetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (EditorAssetInfo dependAsset in SafeList(reportAsset.DependAssets))
            {
                if (string.Equals(NormalizeAssetPath(dependAsset.AssetPath), duplicateAssetPath, StringComparison.OrdinalIgnoreCase))
                    referrers.Add(assetPath);
            }

            if (!File.Exists(assetPath))
                continue;

            foreach (string dependency in AssetDatabase.GetDependencies(assetPath, false))
            {
                if (string.Equals(NormalizeAssetPath(dependency), duplicateAssetPath, StringComparison.OrdinalIgnoreCase))
                    referrers.Add(assetPath);
            }
        }

        return referrers.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> FindLongestChain(
        string rootBundle,
        Dictionary<string, List<string>> dependencyGraph)
    {
        List<string> best = new List<string> { rootBundle };
        FindLongestChainRecursive(
            rootBundle,
            dependencyGraph,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new List<string>(),
            ref best);
        return best;
    }

    private static void FindLongestChainRecursive(
        string bundleName,
        Dictionary<string, List<string>> dependencyGraph,
        HashSet<string> visiting,
        List<string> path,
        ref List<string> best)
    {
        if (visiting.Contains(bundleName))
        {
            List<string> cyclePath = new List<string>(path) { bundleName };
            if (cyclePath.Count > best.Count)
                best = cyclePath;
            return;
        }

        visiting.Add(bundleName);
        path.Add(bundleName);

        if (path.Count > best.Count)
            best = new List<string>(path);

        if (dependencyGraph.TryGetValue(bundleName, out List<string> dependencies))
        {
            foreach (string dependency in dependencies.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                FindLongestChainRecursive(dependency, dependencyGraph, visiting, path, ref best);
        }

        path.RemoveAt(path.Count - 1);
        visiting.Remove(bundleName);
    }

    private static string ResolveBundleModuleOwner(ReportBundleInfo bundleInfo, List<string> bundleContentPaths)
    {
        string assetOwner = bundleContentPaths
            .Select(path => ResolveModuleOwner(path, bundleInfo.BundleName))
            .Where(owner => !string.IsNullOrWhiteSpace(owner))
            .GroupBy(owner => owner, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(assetOwner)
            ? ResolveBundleOwner(bundleInfo.BundleName)
            : assetOwner;
    }

    private static string ResolveBundleOwner(string bundleName)
    {
        if (string.IsNullOrWhiteSpace(bundleName))
            return "Unknown";

        string withoutExtension = Path.GetFileNameWithoutExtension(bundleName);
        if (string.IsNullOrWhiteSpace(withoutExtension))
            withoutExtension = bundleName;

        string cleanName = BundleHashSuffixPattern.Replace(withoutExtension, string.Empty)
            .Replace('\\', '/')
            .Replace('-', '.')
            .Replace('_', '.');
        string[] parts = cleanName.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return ToDisplaySegment(parts[0]) + "/" + ToDisplaySegment(parts[1]);
        if (parts.Length == 1)
            return ToDisplaySegment(parts[0]);

        return "Unknown";
    }

    private static string ResolveAssetGuid(EditorAssetInfo assetInfo, string assetPath)
    {
        if (assetInfo != null && !string.IsNullOrWhiteSpace(assetInfo.AssetGUID))
            return assetInfo.AssetGUID;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        return guid ?? string.Empty;
    }

    private static string ResolveAssetType(EditorAssetInfo assetInfo, string assetPath)
    {
        Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
        if (assetType != null)
            return assetType.Name;
        if (assetInfo != null && assetInfo.AssetType != null)
            return assetInfo.AssetType.Name;

        string extension = Path.GetExtension(assetPath);
        return string.IsNullOrWhiteSpace(extension)
            ? "Unknown"
            : extension.TrimStart('.').ToUpperInvariant();
    }

    private static string ToDisplaySegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";

        string[] words = value
            .Replace('-', '_')
            .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
            return char.ToUpperInvariant(value[0]) + value.Substring(1);

        return string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return (assetPath ?? string.Empty).Replace('\\', '/').Trim();
    }

    private static string NormalizeFilePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFullPath(path).Replace("\\", "/");
    }

    private static string SanitizeIdPart(string value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return Regex.Replace(trimmed, @"[^A-Za-z0-9_.-]+", "-");
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Path.DirectorySeparatorChar.ToString();
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            return path;
        return path + Path.DirectorySeparatorChar;
    }

    private static IEnumerable<T> SafeList<T>(List<T> values)
    {
        return values ?? Enumerable.Empty<T>();
    }
}

internal sealed class ProjectAssetCopyInfo
{
    public string assetPath = string.Empty;
    public string assetGuid = string.Empty;
    public string assetType = string.Empty;
    public string bundleName = string.Empty;
    public long copySizeBytes;
    public string moduleOwner = string.Empty;
}

[Serializable]
public sealed class ProjectBundleReport
{
    public string schemaVersion = "1";
    public ProjectBundleReportSummary summary = new ProjectBundleReportSummary();
    public ProjectBundleReportThresholds thresholds = new ProjectBundleReportThresholds();
    public List<ProjectDuplicateAssetReportItem> duplicateAssets = new List<ProjectDuplicateAssetReportItem>();
    public List<ProjectBundleReportBundle> bundles = new List<ProjectBundleReportBundle>();
    public List<ProjectBundleReportBundle> smallBundles = new List<ProjectBundleReportBundle>();
    public List<ProjectBundleReportBundle> largeBundles = new List<ProjectBundleReportBundle>();
    public List<ProjectDependencyChainReportItem> dependencyChains = new List<ProjectDependencyChainReportItem>();
}

[Serializable]
public sealed class ProjectBundleReportSummary
{
    public string reportId = string.Empty;
    public string generatedAt = string.Empty;
    public string buildTarget = string.Empty;
    public string packageName = string.Empty;
    public string packageVersion = string.Empty;
    public string unityVersion = string.Empty;
    public string yooAssetVersion = string.Empty;
    public string planPath = string.Empty;
    public string yooReportPath = string.Empty;
    public string outputPath = string.Empty;
    public int bundleCount;
    public int assetCount;
    public int duplicateAssetCount;
    public int smallBundleCount;
    public int largeBundleCount;
    public int maxDependencyDepth;
    public long totalUncompressedSizeBytes;
    public long totalCompressedSizeBytes;
    public long totalRedundantSizeBytes;
}

[Serializable]
public sealed class ProjectBundleReportThresholds
{
    public long smallBundleBytes;
    public long largeBundleBytes;
    public int dependencyDepthWarningEdges;
}

[Serializable]
public sealed class ProjectDuplicateAssetReportItem
{
    public string assetPath = string.Empty;
    public string assetType = string.Empty;
    public long buildSizeBytes;
    public int bundleCount;
    public List<ProjectDuplicateAssetBundleCopy> bundles = new List<ProjectDuplicateAssetBundleCopy>();
    public long redundantSizeBytes;
    public List<string> directReferrers = new List<string>();
    public List<string> moduleOwners = new List<string>();
}

[Serializable]
public sealed class ProjectDuplicateAssetBundleCopy
{
    public string bundleName = string.Empty;
    public long copySizeBytes;
}

[Serializable]
public sealed class ProjectBundleReportBundle
{
    public string bundleName = string.Empty;
    public string fileName = string.Empty;
    public long uncompressedSizeBytes;
    public long compressedSizeBytes;
    public int assetCount;
    public int directAssetCount;
    public int dependencyAssetCount;
    public List<string> dependBundles = new List<string>();
    public List<string> referenceBundles = new List<string>();
    public string moduleOwner = string.Empty;
    public bool isSmall;
    public bool isLarge;
}

[Serializable]
public sealed class ProjectDependencyChainReportItem
{
    public string rootBundle = string.Empty;
    public int depth;
    public List<string> chain = new List<string>();
}

[Serializable]
public sealed class ProjectBundleReportIndex
{
    public string latestReportId = string.Empty;
    public List<ProjectBundleReportIndexEntry> reports = new List<ProjectBundleReportIndexEntry>();
}

[Serializable]
public sealed class ProjectBundleReportIndexEntry
{
    public string id = string.Empty;
    public string buildTarget = string.Empty;
    public string packageName = string.Empty;
    public string packageVersion = string.Empty;
    public string generatedAt = string.Empty;
    public string reportPath = string.Empty;
    public int bundleCount;
    public int duplicateAssetCount;
    public int smallBundleCount;
    public int largeBundleCount;
    public int maxDependencyDepth;
}
