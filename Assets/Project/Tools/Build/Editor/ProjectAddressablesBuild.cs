using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Build.Layout;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.Rendering;

public static class ProjectAddressablesBuild
{
    private const string DefaultBuildRoot = "Assets";
    private const string ResourceCheckRoot = "Assets/GameResources";
    private const string DefaultPlanRoot = ".workspace/artifacts/addressables";
    private const string PlanFileName = "addressables_build_plan.json";
    private const string ValidationFileName = "addressables_build_validation.json";
    private const string GameAssetsDirectoryName = "GameAssets";
    private const string ChunksDirectoryName = "Chunks";
    private const string CommonDirectoryName = "Common";
    private const string SharedDirectoryName = "Shared";
    private const string WorldsDirectoryName = "Worlds";
    private const string SeasonsDirectoryName = "Seasons";
    private const string ScenesDirectoryName = "Scenes";
    private const string MeadowWorldRoot = "Worlds/Meadow";
    private const string MeadowEnvironmentPrefabRoot = "Assets/GameAssets/Prefabs/Meadow Environment";
    private const string MeadowTerrainDetailsPrefabRoot = "Assets/GameAssets/Prefabs/Meadow Terrain Details";
    private const string MeadowLegacyConfigRoot = "Configs/Post Processing/Meadow Environment";
    private const string MeadowLegacySceneRoot = "Scenes/Meadow Environment";
    private const string MeadowGeneratedSceneRoot = "Worlds/Meadow/Scenes";
    private const string CommonShaderRoot = "Common/Shaders";
    private const string SharedTextureRoot = "Assets/GameResources/Stylized Pack - Common/Sources/Textures";
    private const string DefaultAndroidGraphicsApi = "Vulkan";
    private const string GeneratedGroupPrefix = "angrymesh.";
    private const string AngryMeshLabel = "angrymesh";
    private const long DefaultMaxBundleBytes = 64L * 1024L * 1024L;
    private const int DefaultMaxBundleDependencies = 16;
    private const string BuildReportsRoot = "Library/com.unity.addressables/BuildReports";

    private static readonly string[] IgnoredExtensions =
    {
        ".asmdef",
        ".asmref",
        ".cs",
        ".dll",
        ".cginc",
        ".h",
        ".hlsl",
        ".md",
        ".meta",
        ".pdf",
        ".unitypackage"
    };

    [MenuItem("Tools/ANGRY MESH/Addressables/Prepare Groups")]
    public static void PrepareGroupsFromMenu()
    {
        PrepareGroups(Environment.GetCommandLineArgs());
    }

    [MenuItem("Tools/ANGRY MESH/Addressables/Build Content")]
    public static void BuildFromMenu()
    {
        BuildFromCommandLine();
    }

    [MenuItem("Tools/ANGRY MESH/Addressables/Validate Latest Build")]
    public static void ValidateLatestBuildFromMenu()
    {
        ValidateLatestBuildFromCommandLine();
    }

    public static void PrepareGroupsFromCommandLine()
    {
        PrepareGroups(Environment.GetCommandLineArgs());
    }

    public static void BuildFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        ConfigureBatchmodeLogging();
        AddressablesBuildPlan plan = PrepareGroups(args);
        BuildValidationOptions validationOptions = ResolveValidationOptions(args, plan);

        if (HasArgument(args, "--addressables-dry-run"))
        {
            BuildValidationReport dryRunReport = ValidateBuildPlan(plan, validationOptions);
            WriteValidationReport(validationOptions.validationOutputPath, dryRunReport);
            Debug.Log("Addressables dry run finished. Plan: " + plan.planPath);
            return;
        }

        DateTime buildStart = DateTime.Now;
        bool previousGenerateBuildLayout = ProjectConfigData.GenerateBuildLayout;
        ProjectConfigData.ReportFileFormat previousReportFileFormat = ProjectConfigData.BuildLayoutReportFileFormat;
        ProjectConfigData.GenerateBuildLayout = true;
        ProjectConfigData.BuildLayoutReportFileFormat = ProjectConfigData.ReportFileFormat.JSON;

        AddressablesPlayerBuildResult result;
        try
        {
            AddressableAssetSettings.BuildPlayerContent(out result);
        }
        finally
        {
            ProjectConfigData.GenerateBuildLayout = previousGenerateBuildLayout;
            ProjectConfigData.BuildLayoutReportFileFormat = previousReportFileFormat;
        }

        if (!string.IsNullOrEmpty(result.Error))
            throw new InvalidOperationException("Addressables build failed: " + result.Error);

        BuildValidationReport validationReport = ValidateBuildResult(plan, validationOptions, result, buildStart);
        WriteValidationReport(validationOptions.validationOutputPath, validationReport);
        if (validationReport.errors.Count > 0)
            throw new InvalidOperationException("Addressables build validation failed. See: " + validationOptions.validationOutputPath);

        if (validationOptions.failOnWarnings && validationReport.warnings.Count > 0)
            throw new InvalidOperationException("Addressables build validation warnings are configured as failures. See: " + validationOptions.validationOutputPath);

        Debug.Log("Addressables build succeeded. OutputPath=" + result.OutputPath + ", Plan=" + plan.planPath + ", Validation=" + validationOptions.validationOutputPath);
    }

    private static void ConfigureBatchmodeLogging()
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
    }

    public static void ValidateLatestBuildFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        AddressablesBuildPlan plan = PrepareGroups(args);
        BuildValidationOptions validationOptions = ResolveValidationOptions(args, plan);
        BuildValidationReport validationReport = ValidateBuildLayout(plan, validationOptions, string.Empty, DateTime.MinValue);
        WriteValidationReport(validationOptions.validationOutputPath, validationReport);

        if (validationReport.errors.Count > 0)
            throw new InvalidOperationException("Addressables latest build validation failed. See: " + validationOptions.validationOutputPath);

        if (validationOptions.failOnWarnings && validationReport.warnings.Count > 0)
            throw new InvalidOperationException("Addressables latest build validation warnings are configured as failures. See: " + validationOptions.validationOutputPath);
    }

    private static AddressablesBuildPlan PrepareGroups(string[] args)
    {
        BuildTarget buildTarget = ResolveBuildTarget(args);
        ConfigureBuildTargetSettings(buildTarget, args);
        SwitchBuildTargetIfNeeded(buildTarget);

        bool includeSamples = HasArgument(args, "--addressables-include-samples");
        bool includeSourceAssets = HasArgument(args, "--addressables-include-source-assets");
        bool keepStaleGroups = HasArgument(args, "--addressables-keep-stale-groups");

        string buildRoot = NormalizeAssetPath(GetArgumentValue(args, "--addressables-build-root"));
        if (string.IsNullOrWhiteSpace(buildRoot))
            buildRoot = DefaultBuildRoot;

        string planOutput = GetArgumentValue(args, "--addressables-plan-output");
        if (string.IsNullOrWhiteSpace(planOutput))
            planOutput = Path.Combine(DefaultPlanRoot, buildTarget.ToString(), "angrymesh", PlanFileName);

        AddressablesBuildPlan plan = BuildPlan(buildRoot, buildTarget, includeSamples, includeSourceAssets);
        plan.planPath = Path.GetFullPath(planOutput).Replace("\\", "/");
        WritePlan(plan.planPath, plan);

        if (plan.errors.Count > 0)
            throw new InvalidOperationException("Addressables plan contains errors. See plan: " + plan.planPath);

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings == null)
            throw new InvalidOperationException("Failed to create or load AddressableAssetSettings.");

        ApplyPlan(settings, plan, keepStaleGroups);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Prepared ANGRY MESH Addressables groups. Plan: " + plan.planPath);
        return plan;
    }

    private static AddressablesBuildPlan BuildPlan(
        string buildRoot,
        BuildTarget buildTarget,
        bool includeSamples,
        bool includeSourceAssets)
    {
        AddressablesBuildPlan plan = new AddressablesBuildPlan
        {
            buildTarget = buildTarget.ToString(),
            buildRoot = buildRoot,
            includeSamples = includeSamples,
            includeSourceAssets = includeSourceAssets
        };

        List<ProjectGroupSpec> specs = CreateGameAssetSpecs(buildRoot);
        if (includeSourceAssets)
            specs.AddRange(CreateGameResourceSpecs(buildRoot));

        if (includeSamples)
            specs.Add(new ProjectGroupSpec("samples.urp", new[] { "Assets/Samples/Universal RP" }));

        HashSet<string> assignedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectGroupSpec spec in specs)
        {
            List<string> assetPaths = ResolveAssetPaths(spec, plan)
                .Where(path => assignedAssets.Add(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (assetPaths.Count == 0)
                continue;

            plan.groups.Add(new AddressablesGroupPlan
            {
                name = spec.groupName,
                roots = spec.roots.ToList(),
                assetCount = assetPaths.Count,
                estimatedSourceBytes = assetPaths.Sum(GetFileSizeSafe),
                assets = assetPaths
            });
        }

        FillDependencySummary(plan);

        if (plan.groups.Count == 0)
            plan.errors.Add("No Addressables groups were generated.");

        return plan;
    }

    private static void ApplyPlan(AddressableAssetSettings settings, AddressablesBuildPlan plan, bool keepStaleGroups)
    {
        settings.AddLabel(AngryMeshLabel);

        HashSet<string> plannedGroups = new HashSet<string>(
            plan.groups.Select(group => group.name),
            StringComparer.OrdinalIgnoreCase);

        if (!keepStaleGroups)
            RemoveGeneratedGroups(settings, plannedGroups);

        for (int groupIndex = 0; groupIndex < plan.groups.Count; groupIndex++)
        {
            AddressablesGroupPlan groupPlan = plan.groups[groupIndex];
            AddressableAssetGroup group = RecreateGroup(settings, groupPlan.name);

            for (int assetIndex = 0; assetIndex < groupPlan.assets.Count; assetIndex++)
            {
                string assetPath = groupPlan.assets[assetIndex];
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    plan.errors.Add("Missing GUID for asset: " + assetPath);
                    continue;
                }

                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = assetPath;
                entry.SetLabel(AngryMeshLabel, true, true);
                entry.SetLabel(groupPlan.name, true, true);
            }
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
    }

    private static AddressableAssetGroup RecreateGroup(AddressableAssetSettings settings, string groupName)
    {
        AddressableAssetGroup existing = settings.FindGroup(groupName);
        if (existing != null)
            settings.RemoveGroup(existing);

        settings.AddLabel(groupName);

        AddressableAssetGroup group = settings.CreateGroup(
            groupName,
            false,
            false,
            true,
            null,
            typeof(BundledAssetGroupSchema),
            typeof(ContentUpdateGroupSchema));

        BundledAssetGroupSchema bundledSchema = group.GetSchema<BundledAssetGroupSchema>();
        if (bundledSchema != null)
        {
            bundledSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            bundledSchema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
            bundledSchema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            bundledSchema.IncludeInBuild = true;
        }

        ContentUpdateGroupSchema contentUpdateSchema = group.GetSchema<ContentUpdateGroupSchema>();
        if (contentUpdateSchema != null)
            contentUpdateSchema.StaticContent = false;

        return group;
    }

    private static void RemoveGeneratedGroups(AddressableAssetSettings settings, HashSet<string> plannedGroups)
    {
        List<AddressableAssetGroup> staleGroups = settings.groups
            .Where(group => group != null &&
                            group.Name.StartsWith(GeneratedGroupPrefix, StringComparison.OrdinalIgnoreCase) &&
                            !plannedGroups.Contains(group.Name))
            .ToList();

        for (int index = 0; index < staleGroups.Count; index++)
            settings.RemoveGroup(staleGroups[index]);
    }

    private static List<ProjectGroupSpec> CreateGameAssetSpecs(string buildRoot)
    {
        string gameAssetsRoot = ResolveGameAssetsRoot(buildRoot);
        string[] excludeRoots = CreateGameAssetExcludeRoots(buildRoot, gameAssetsRoot);
        if (!AssetDatabase.IsValidFolder(gameAssetsRoot))
        {
            return new List<ProjectGroupSpec>
            {
                new ProjectGroupSpec("angrymesh.gameassets", new[] { gameAssetsRoot }, excludeRoots)
            };
        }

        List<ProjectGroupSpec> specs = new List<ProjectGroupSpec>();
        specs.AddRange(CreateSharedRuntimeSpecs(gameAssetsRoot));
        specs.AddRange(CreateCommonRuntimeSpecs(gameAssetsRoot));
        specs.AddRange(CreateWorldRuntimeSpecs(gameAssetsRoot));
        specs.AddRange(CreateSpecsFromPackageFolders("angrymesh.gameassets", gameAssetsRoot, excludeRoots));
        return specs;
    }

    private static string ResolveGameAssetsRoot(string buildRoot)
    {
        string normalizedBuildRoot = NormalizeAssetPath(buildRoot).TrimEnd('/');
        if (string.Equals(Path.GetFileName(normalizedBuildRoot), GameAssetsDirectoryName, StringComparison.OrdinalIgnoreCase))
            return normalizedBuildRoot;

        return AssetPathCombine(normalizedBuildRoot, GameAssetsDirectoryName);
    }

    private static List<ProjectGroupSpec> CreateGameResourceSpecs(string buildRoot)
    {
        if (AssetDatabase.IsValidFolder(ResourceCheckRoot))
            return CreateSpecsFromImmediateChildren("angrymesh.gameresources", ResourceCheckRoot, Array.Empty<string>());

        return new List<ProjectGroupSpec>
        {
            new ProjectGroupSpec("angrymesh.gameresources", new[] { ResourceCheckRoot })
        };
    }

    private static IEnumerable<ProjectGroupSpec> CreateSharedRuntimeSpecs(string gameAssetsRoot)
    {
        string sharedShaderRoot = AssetPathCombine(gameAssetsRoot, CommonShaderRoot);
        if (AssetDatabase.IsValidFolder(sharedShaderRoot))
            yield return new ProjectGroupSpec("angrymesh.shared.shaders", new[] { sharedShaderRoot });

        if (AssetDatabase.IsValidFolder(SharedTextureRoot))
            yield return new ProjectGroupSpec("angrymesh.shared.textures", new[] { SharedTextureRoot });
    }

    private static IEnumerable<ProjectGroupSpec> CreateCommonRuntimeSpecs(string gameAssetsRoot)
    {
        string commonRoot = AssetPathCombine(gameAssetsRoot, CommonDirectoryName);
        if (!AssetDatabase.IsValidFolder(commonRoot))
            yield break;

        string sharedShaderRoot = AssetPathCombine(gameAssetsRoot, CommonShaderRoot);
        string[] commonExcludeRoots = AssetDatabase.IsValidFolder(sharedShaderRoot)
            ? new[] { sharedShaderRoot }
            : Array.Empty<string>();

        yield return new ProjectGroupSpec("angrymesh.gameassets.common", new[] { commonRoot }, commonExcludeRoots);
    }

    private static IEnumerable<ProjectGroupSpec> CreateWorldRuntimeSpecs(string gameAssetsRoot)
    {
        string worldsRoot = AssetPathCombine(gameAssetsRoot, WorldsDirectoryName);
        if (!AssetDatabase.IsValidFolder(worldsRoot))
            yield break;

        foreach (string worldFolder in AssetDatabase.GetSubFolders(worldsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string worldName = SanitizeSegment(Path.GetFileName(worldFolder));
            string groupPrefix = "angrymesh.worlds." + worldName;

            string sharedRoot = AssetPathCombine(worldFolder, SharedDirectoryName);
            if (AssetDatabase.IsValidFolder(sharedRoot))
                yield return new ProjectGroupSpec(groupPrefix + ".shared", new[] { sharedRoot });

            string seasonsRoot = AssetPathCombine(worldFolder, SeasonsDirectoryName);
            if (AssetDatabase.IsValidFolder(seasonsRoot))
            {
                foreach (string seasonFolder in AssetDatabase.GetSubFolders(seasonsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string seasonName = SanitizeSegment(Path.GetFileName(seasonFolder));
                    yield return new ProjectGroupSpec(groupPrefix + ".season." + seasonName, new[] { seasonFolder });
                }
            }

            string chunksRoot = AssetPathCombine(worldFolder, ChunksDirectoryName);
            if (AssetDatabase.IsValidFolder(chunksRoot))
            {
                foreach (string chunkFolder in AssetDatabase.GetSubFolders(chunksRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if (!IsWorldChunkFolder(chunkFolder))
                        continue;

                    string chunkName = SanitizeChunkSegment(Path.GetFileName(chunkFolder));
                    yield return new ProjectGroupSpec(groupPrefix + ".chunks." + chunkName, new[] { chunkFolder });
                }
            }

            string scenesRoot = AssetPathCombine(worldFolder, ScenesDirectoryName);
            if (AssetDatabase.IsValidFolder(scenesRoot))
                yield return new ProjectGroupSpec(groupPrefix + ".scenes", new[] { scenesRoot });
        }
    }

    private static List<ProjectGroupSpec> CreateSpecsFromImmediateChildren(
        string groupPrefix,
        string root,
        string[] excludeRoots)
    {
        string[] childFolders = AssetDatabase.GetSubFolders(root);
        if (childFolders.Length == 0)
            return new List<ProjectGroupSpec> { new ProjectGroupSpec(groupPrefix, new[] { root }, excludeRoots) };

        return childFolders
            .Select(child => new ProjectGroupSpec(
                groupPrefix + "." + SanitizeSegment(Path.GetFileName(child)),
                new[] { NormalizeAssetPath(child) },
                excludeRoots))
            .ToList();
    }

    private static List<ProjectGroupSpec> CreateSpecsFromPackageFolders(
        string groupPrefix,
        string root,
        string[] excludeRoots)
    {
        string normalizedRoot = NormalizeAssetPath(root).TrimEnd('/');
        return CollectPackageFolders(normalizedRoot)
            .Select(folder => new ProjectGroupSpec(
                groupPrefix + "." + SanitizeRelativeGroupPath(normalizedRoot, folder),
                new[] { NormalizeAssetPath(folder) },
                excludeRoots))
            .ToList();
    }

    private static IEnumerable<string> CollectPackageFolders(string root)
    {
        string[] childFolders = AssetDatabase.GetSubFolders(root);
        if (IsWorldChunkFolder(root))
        {
            yield return NormalizeAssetPath(root);
            yield break;
        }

        if (IsMeadowPrefabCategoryFolder(root))
        {
            yield return NormalizeAssetPath(root);
            yield break;
        }

        if (childFolders.Length == 0)
        {
            yield return NormalizeAssetPath(root);
            yield break;
        }

        if (HasDirectCandidateFiles(root))
            yield return NormalizeAssetPath(root);

        foreach (string childFolder in childFolders)
        {
            foreach (string packageFolder in CollectPackageFolders(childFolder))
                yield return packageFolder;
        }
    }

    private static bool IsWorldChunkFolder(string folder)
    {
        string normalizedFolder = NormalizeAssetPath(folder).TrimEnd('/');
        string[] segments = normalizedFolder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < segments.Length - 1; index++)
        {
            if (!string.Equals(segments[index], ChunksDirectoryName, StringComparison.OrdinalIgnoreCase))
                continue;

            return segments[index + 1].StartsWith("Chunk_", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsMeadowPrefabCategoryFolder(string folder)
    {
        return IsDirectChildOfRoot(folder, MeadowEnvironmentPrefabRoot) ||
               IsDirectChildOfRoot(folder, MeadowTerrainDetailsPrefabRoot);
    }

    private static bool IsDirectChildOfRoot(string folder, string root)
    {
        string normalizedFolder = NormalizeAssetPath(folder).TrimEnd('/');
        string normalizedRoot = NormalizeAssetPath(root).TrimEnd('/');
        if (!normalizedFolder.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
            return false;

        string relativePath = normalizedFolder.Substring(normalizedRoot.Length + 1);
        return relativePath.Length > 0 && relativePath.IndexOf('/') < 0;
    }

    private static bool HasDirectCandidateFiles(string root)
    {
        if (!Directory.Exists(root))
            return false;

        foreach (string file in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            string assetPath = NormalizeAssetPath(file);
            if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            string extension = Path.GetExtension(assetPath);
            if (!IgnoredExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string SanitizeRelativeGroupPath(string root, string folder)
    {
        string normalizedRoot = NormalizeAssetPath(root).TrimEnd('/');
        string normalizedFolder = NormalizeAssetPath(folder);
        string relativePath = normalizedFolder.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase)
            ? normalizedFolder.Substring(normalizedRoot.Length + 1)
            : normalizedFolder;

        string[] segments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return SanitizeSegment(Path.GetFileName(normalizedFolder));

        return string.Join(".", segments.Select(SanitizeSegment));
    }

    private static IEnumerable<string> ResolveAssetPaths(ProjectGroupSpec spec, AddressablesBuildPlan plan)
    {
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int rootIndex = 0; rootIndex < spec.roots.Length; rootIndex++)
        {
            string root = NormalizeAssetPath(spec.roots[rootIndex]);
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (!AssetDatabase.IsValidFolder(root))
            {
                plan.warnings.Add("Addressables root folder does not exist: " + root);
                continue;
            }

            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { root });
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[guidIndex]));
                if (ShouldIncludeAsset(assetPath, spec.excludeRoots, plan))
                    paths.Add(assetPath);
            }
        }

        return paths;
    }

    private static bool ShouldIncludeAsset(string assetPath, IReadOnlyList<string> excludeRoots, AddressablesBuildPlan plan)
    {
        if (string.IsNullOrWhiteSpace(assetPath) ||
            !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            AssetDatabase.IsValidFolder(assetPath))
        {
            return false;
        }

        if (ContainsPathSegment(assetPath, "Editor"))
            return false;

        if (ContainsPathSegment(assetPath, "Resources"))
            plan.warnings.Add("Addressable candidate is under a Resources folder: " + assetPath);

        if (excludeRoots != null)
        {
            for (int index = 0; index < excludeRoots.Count; index++)
            {
                if (IsUnderRoot(assetPath, excludeRoots[index]))
                    return false;
            }
        }

        string extension = Path.GetExtension(assetPath);
        return !IgnoredExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static void FillDependencySummary(AddressablesBuildPlan plan)
    {
        Dictionary<string, HashSet<string>> dependencyToGroups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> planAssets = new HashSet<string>(
            plan.groups.SelectMany(group => group.assets),
            StringComparer.OrdinalIgnoreCase);

        for (int groupIndex = 0; groupIndex < plan.groups.Count; groupIndex++)
        {
            AddressablesGroupPlan group = plan.groups[groupIndex];
            HashSet<string> groupDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int assetIndex = 0; assetIndex < group.assets.Count; assetIndex++)
            {
                string[] dependencies = AssetDatabase.GetDependencies(group.assets[assetIndex], true);
                for (int dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
                {
                    string dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
                    if (string.IsNullOrWhiteSpace(dependency) ||
                        planAssets.Contains(dependency) ||
                        AssetDatabase.IsValidFolder(dependency))
                    {
                        continue;
                    }

                    groupDependencies.Add(dependency);
                    if (!dependencyToGroups.TryGetValue(dependency, out HashSet<string> owners))
                    {
                        owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        dependencyToGroups.Add(dependency, owners);
                    }

                    owners.Add(group.name);
                }
            }

            group.dependencyCount = groupDependencies.Count;
        }

        HashSet<string> sharedDependencies = new HashSet<string>(
            dependencyToGroups.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key),
            StringComparer.OrdinalIgnoreCase);

        plan.sharedDependencyCount = sharedDependencies.Count;
        for (int groupIndex = 0; groupIndex < plan.groups.Count; groupIndex++)
        {
            AddressablesGroupPlan group = plan.groups[groupIndex];
            HashSet<string> groupSharedDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int assetIndex = 0; assetIndex < group.assets.Count; assetIndex++)
            {
                string[] dependencies = AssetDatabase.GetDependencies(group.assets[assetIndex], true);
                for (int dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
                {
                    string dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
                    if (sharedDependencies.Contains(dependency))
                        groupSharedDependencies.Add(dependency);
                }
            }

            group.sharedDependencies = groupSharedDependencies
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static string[] CreateDefaultRawExcludeRoots(string buildRoot)
    {
        return new[]
        {
            ResourceCheckRoot
        };
    }

    private static string[] CreateGameAssetExcludeRoots(string buildRoot, string gameAssetsRoot)
    {
        List<string> excludeRoots = new List<string>(CreateDefaultRawExcludeRoots(buildRoot));
        string commonRoot = AssetPathCombine(gameAssetsRoot, CommonDirectoryName);
        if (AssetDatabase.IsValidFolder(commonRoot))
            excludeRoots.Add(commonRoot);

        string worldsRoot = AssetPathCombine(gameAssetsRoot, WorldsDirectoryName);
        if (AssetDatabase.IsValidFolder(worldsRoot))
            excludeRoots.Add(worldsRoot);

        string meadowWorldRoot = AssetPathCombine(gameAssetsRoot, MeadowWorldRoot);
        if (AssetDatabase.IsValidFolder(meadowWorldRoot))
        {
            excludeRoots.Add(MeadowEnvironmentPrefabRoot);
            excludeRoots.Add(MeadowTerrainDetailsPrefabRoot);
            excludeRoots.Add(AssetPathCombine(gameAssetsRoot, MeadowLegacyConfigRoot));
            excludeRoots.Add(AssetPathCombine(gameAssetsRoot, MeadowLegacySceneRoot));
        }

        string generatedMeadowScenes = AssetPathCombine(gameAssetsRoot, MeadowGeneratedSceneRoot);
        if (AssetDatabase.IsValidFolder(generatedMeadowScenes))
            excludeRoots.Add(AssetPathCombine(gameAssetsRoot, MeadowLegacySceneRoot));

        return excludeRoots.ToArray();
    }

    private static void WritePlan(string planPath, AddressablesBuildPlan plan)
    {
        string directory = Path.GetDirectoryName(planPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(planPath, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
    }

    private static BuildValidationOptions ResolveValidationOptions(string[] args, AddressablesBuildPlan plan)
    {
        string validationOutput = GetArgumentValue(args, "--addressables-validation-output");
        if (string.IsNullOrWhiteSpace(validationOutput))
        {
            string planDirectory = Path.GetDirectoryName(plan.planPath);
            validationOutput = Path.Combine(string.IsNullOrWhiteSpace(planDirectory) ? DefaultPlanRoot : planDirectory, ValidationFileName);
        }

        return new BuildValidationOptions
        {
            validationOutputPath = Path.GetFullPath(validationOutput).Replace("\\", "/"),
            maxBundleBytes = ResolveMaxBundleBytes(args),
            maxBundleDependencies = ResolveIntArgument(args, "--addressables-max-bundle-dependencies", DefaultMaxBundleDependencies),
            failOnWarnings = HasArgument(args, "--addressables-fail-on-validation-warning")
        };
    }

    private static BuildValidationReport ValidateBuildPlan(AddressablesBuildPlan plan, BuildValidationOptions options)
    {
        BuildValidationReport report = CreateValidationReport("plan", plan, options);

        HashSet<string> seenAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int groupIndex = 0; groupIndex < plan.groups.Count; groupIndex++)
        {
            AddressablesGroupPlan group = plan.groups[groupIndex];
            for (int assetIndex = 0; assetIndex < group.assets.Count; assetIndex++)
            {
                string assetPath = group.assets[assetIndex];
                if (!seenAssets.Add(assetPath))
                    AddValidationError(report, "DuplicatePlanAsset", assetPath + " appears in more than one Addressables group.");
            }
        }

        foreach (string planError in plan.errors)
            AddValidationError(report, "PlanError", planError);

        foreach (string planWarning in plan.warnings)
            AddValidationWarning(report, "PlanWarning", planWarning);

        ValidateShaderAndMaterialRisks(CollectResourceCheckAssetPaths(), report);
        FinalizeValidationReport(report);
        return report;
    }

    private static BuildValidationReport ValidateBuildResult(
        AddressablesBuildPlan plan,
        BuildValidationOptions options,
        AddressablesPlayerBuildResult buildResult,
        DateTime buildStart)
    {
        return ValidateBuildLayout(plan, options, buildResult.OutputPath, buildStart);
    }

    private static BuildValidationReport ValidateBuildLayout(
        AddressablesBuildPlan plan,
        BuildValidationOptions options,
        string buildOutputPath,
        DateTime buildStart)
    {
        BuildValidationReport report = ValidateBuildPlan(plan, options);
        report.validationMode = "build-result";
        report.buildOutputPath = NormalizeAssetPath(buildOutputPath);

        string layoutPath = FindLatestBuildLayoutReport(buildStart);
        report.buildLayoutPath = NormalizeAssetPath(layoutPath);
        if (string.IsNullOrWhiteSpace(layoutPath))
        {
            AddValidationError(report, "MissingBuildLayout", "Addressables BuildLayout report was not generated.");
            FinalizeValidationReport(report);
            return report;
        }

        BuildLayout layout = BuildLayout.Open(layoutPath, true, true);
        if (layout == null)
        {
            AddValidationError(report, "InvalidBuildLayout", "Failed to read Addressables BuildLayout: " + layoutPath);
            FinalizeValidationReport(report);
            return report;
        }

        try
        {
            report.buildResultHash = layout.BuildResultHash ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(layout.BuildError))
                AddValidationError(report, "BuildLayoutError", layout.BuildError);

            List<BuildLayout.Bundle> bundles = BuildLayoutHelpers.EnumerateBundles(layout)
                .Where(bundle => bundle != null)
                .ToList();

            report.bundleCount = bundles.Count;
            ValidateDuplicateResources(layout, report);
            ValidateBundleDependencyCycles(bundles, report);
            ValidateBundleSizeAndDependencyBudget(bundles, options, report);
            ValidatePackagedAssetClosure(layout, plan, report);
            ValidateBundleHashes(bundles, options.validationOutputPath, report);

            ValidateShaderAndMaterialRisks(CollectResourceCheckAssetPaths(), report);
        }
        finally
        {
            layout.Close();
        }

        FinalizeValidationReport(report);
        return report;
    }

    private static BuildValidationReport CreateValidationReport(string mode, AddressablesBuildPlan plan, BuildValidationOptions options)
    {
        return new BuildValidationReport
        {
            validationMode = mode,
            buildTarget = plan.buildTarget,
            planPath = plan.planPath,
            validationPath = options.validationOutputPath,
            maxBundleBytes = options.maxBundleBytes,
            maxBundleDependencies = options.maxBundleDependencies,
            generatedAt = DateTime.Now.ToString("O")
        };
    }

    private static void ValidateDuplicateResources(BuildLayout layout, BuildValidationReport report)
    {
        if (layout.DuplicatedAssets == null)
            return;

        for (int index = 0; index < layout.DuplicatedAssets.Count; index++)
        {
            BuildLayout.AssetDuplicationData duplicate = layout.DuplicatedAssets[index];
            int duplicateObjectCount = duplicate.DuplicatedObjects == null ? 0 : duplicate.DuplicatedObjects.Count;
            if (duplicateObjectCount == 0)
                continue;

            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(duplicate.AssetGuid));
            if (IsPackageEditorAsset(assetPath))
                continue;

            ValidationDuplicateAsset item = new ValidationDuplicateAsset
            {
                assetGuid = duplicate.AssetGuid,
                assetPath = assetPath,
                duplicatedObjectCount = duplicateObjectCount
            };

            if (duplicate.DuplicatedObjects != null)
            {
                foreach (BuildLayout.ObjectDuplicationData duplicatedObject in duplicate.DuplicatedObjects)
                {
                    if (duplicatedObject.IncludedInBundleFiles == null)
                        continue;

                    foreach (BuildLayout.File file in duplicatedObject.IncludedInBundleFiles)
                    {
                        string bundleName = file != null && file.Bundle != null ? file.Bundle.Name : string.Empty;
                        if (!string.IsNullOrWhiteSpace(bundleName) && !item.bundles.Contains(bundleName))
                            item.bundles.Add(bundleName);
                    }
                }
            }

            item.bundles.Sort(StringComparer.OrdinalIgnoreCase);
            report.duplicatedAssets.Add(item);
            AddValidationWarning(report, "DuplicateAsset", assetPath + " is duplicated in bundles: " + string.Join(", ", item.bundles));
        }
    }

    private static bool IsPackageEditorAsset(string assetPath)
    {
        return assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) &&
               assetPath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ValidateBundleDependencyCycles(List<BuildLayout.Bundle> bundles, BuildValidationReport report)
    {
        Dictionary<BuildLayout.Bundle, VisitState> states = new Dictionary<BuildLayout.Bundle, VisitState>();
        Stack<BuildLayout.Bundle> stack = new Stack<BuildLayout.Bundle>();

        for (int index = 0; index < bundles.Count; index++)
            VisitBundleForCycles(bundles[index], states, stack, report);
    }

    private static void VisitBundleForCycles(
        BuildLayout.Bundle bundle,
        Dictionary<BuildLayout.Bundle, VisitState> states,
        Stack<BuildLayout.Bundle> stack,
        BuildValidationReport report)
    {
        if (bundle == null)
            return;

        if (states.TryGetValue(bundle, out VisitState state))
        {
            if (state != VisitState.Visiting)
                return;

            List<string> cycle = stack
                .Reverse()
                .SkipWhile(item => item != bundle)
                .Select(item => item.Name)
                .ToList();
            cycle.Add(bundle.Name);

            string cycleText = string.Join(" -> ", cycle);
            if (!report.dependencyCycles.Contains(cycleText))
            {
                report.dependencyCycles.Add(cycleText);
                AddValidationError(report, "BundleCycle", cycleText);
            }

            return;
        }

        states[bundle] = VisitState.Visiting;
        stack.Push(bundle);

        if (bundle.Dependencies != null)
        {
            for (int index = 0; index < bundle.Dependencies.Count; index++)
                VisitBundleForCycles(bundle.Dependencies[index], states, stack, report);
        }

        stack.Pop();
        states[bundle] = VisitState.Visited;
    }

    private static void ValidateBundleSizeAndDependencyBudget(
        List<BuildLayout.Bundle> bundles,
        BuildValidationOptions options,
        BuildValidationReport report)
    {
        for (int index = 0; index < bundles.Count; index++)
        {
            BuildLayout.Bundle bundle = bundles[index];
            if (bundle == null)
                continue;

            int dependencyCount = bundle.Dependencies == null ? 0 : bundle.Dependencies.Count;
            ValidationBundleSummary summary = new ValidationBundleSummary
            {
                name = bundle.Name ?? string.Empty,
                internalName = bundle.InternalName ?? string.Empty,
                groupName = bundle.Group == null ? "<built-in>" : bundle.Group.Name,
                fileSize = (long)bundle.FileSize,
                uncompressedFileSize = (long)bundle.UncompressedFileSize,
                dependencyCount = dependencyCount,
                assetCount = bundle.AssetCount,
                hash = bundle.Hash.ToString()
            };

            if (bundle.Dependencies != null)
                summary.dependencies = bundle.Dependencies.Select(dependency => dependency.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

            report.bundles.Add(summary);

            if ((long)bundle.FileSize > options.maxBundleBytes)
            {
                report.oversizedBundles.Add(summary);
                AddValidationError(report, "OversizedBundle", bundle.Name + " is " + bundle.FileSize + " bytes, limit is " + options.maxBundleBytes + " bytes.");
            }

            if (dependencyCount > options.maxBundleDependencies)
            {
                report.highDependencyBundles.Add(summary);
                AddValidationError(report, "HighBundleDependencyCount", bundle.Name + " depends on " + dependencyCount + " bundles, limit is " + options.maxBundleDependencies + ".");
            }
        }
    }

    private static void ValidatePackagedAssetClosure(BuildLayout layout, AddressablesBuildPlan plan, BuildValidationReport report)
    {
        HashSet<string> plannedAssets = new HashSet<string>(
            plan.groups.SelectMany(group => group.assets).Select(NormalizeAssetPath),
            StringComparer.OrdinalIgnoreCase);

        HashSet<string> allowedAssets = CollectPlanAssetAndDependencyPaths(plan);
        foreach (BuildLayout.Bundle bundle in BuildLayoutHelpers.EnumerateBundles(layout))
        {
            if (bundle == null || bundle.Files == null)
                continue;

            foreach (BuildLayout.File file in bundle.Files)
            {
                if (file == null)
                    continue;

                if (file.Assets != null)
                {
                    foreach (BuildLayout.ExplicitAsset asset in file.Assets)
                    {
                        string assetPath = NormalizeAssetPath(asset.AssetPath);
                        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!plannedAssets.Contains(assetPath))
                            AddUnexpectedPackagedAsset(report, assetPath, bundle.Name, "ExplicitAddressableNotInPlan");
                    }
                }

                if (file.OtherAssets == null)
                    continue;

                foreach (BuildLayout.DataFromOtherAsset asset in file.OtherAssets)
                {
                    string assetPath = NormalizeAssetPath(asset.AssetPath);
                    if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!allowedAssets.Contains(assetPath))
                        AddUnexpectedPackagedAsset(report, assetPath, bundle.Name, "ImplicitAssetOutsidePlanDependencyClosure");
                }
            }
        }
    }

    private static void AddUnexpectedPackagedAsset(BuildValidationReport report, string assetPath, string bundleName, string reason)
    {
        ValidationUnexpectedAsset item = new ValidationUnexpectedAsset
        {
            assetPath = assetPath,
            bundleName = bundleName,
            reason = reason
        };
        report.unexpectedAssets.Add(item);
        AddValidationError(report, "UnexpectedPackagedAsset", reason + ": " + assetPath + " in " + bundleName);
    }

    private static void ValidateBundleHashes(List<BuildLayout.Bundle> bundles, string validationOutputPath, BuildValidationReport report)
    {
        BuildValidationReport previousReport = ReadPreviousValidationReport(validationOutputPath);
        Dictionary<string, ValidationBundleSummary> previousBundles = previousReport == null
            ? new Dictionary<string, ValidationBundleSummary>(StringComparer.OrdinalIgnoreCase)
            : previousReport.bundles
                .Where(bundle => !string.IsNullOrWhiteSpace(bundle.stableName))
                .GroupBy(bundle => bundle.stableName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < report.bundles.Count; index++)
        {
            ValidationBundleSummary summary = report.bundles[index];
            summary.stableName = StripBundleHash(string.IsNullOrWhiteSpace(summary.name) ? summary.internalName : summary.name);

            bool generatedProjectBundle = summary.groupName.StartsWith(GeneratedGroupPrefix, StringComparison.OrdinalIgnoreCase);
            if (generatedProjectBundle && !HasHashLikeSegment(summary.name) && !HasHashLikeSegment(summary.internalName))
            {
                AddValidationError(report, "MissingBundleNameHash", summary.name + " does not contain an AppendHash-style hash segment.");
            }

            if (!previousBundles.TryGetValue(summary.stableName, out ValidationBundleSummary previous) ||
                string.IsNullOrWhiteSpace(previous.hash) ||
                string.Equals(previous.hash, summary.hash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ValidationHashChange change = new ValidationHashChange
            {
                stableName = summary.stableName,
                currentName = summary.name,
                previousHash = previous.hash,
                currentHash = summary.hash
            };
            report.hashChanges.Add(change);
            AddValidationWarning(report, "BundleHashChanged", summary.stableName + " hash changed from " + previous.hash + " to " + summary.hash + ".");
        }
    }

    private static void ValidateShaderAndMaterialRisks(HashSet<string> assetPaths, BuildValidationReport report)
    {
        foreach (string assetPath in assetPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            string extension = Path.GetExtension(assetPath);

            if (assetType == typeof(Shader) || string.Equals(extension, ".shader", StringComparison.OrdinalIgnoreCase))
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (shader == null)
                    AddShaderRisk(report, assetPath, "MissingShader", "Shader asset could not be loaded.");
                continue;
            }

            if (assetType != typeof(Material) && !string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase))
                continue;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                AddShaderRisk(report, assetPath, "MissingMaterial", "Material asset could not be loaded.");
                continue;
            }

            if (material.shader == null)
            {
                AddShaderRisk(report, assetPath, "MissingMaterialShader", "Material has no shader assigned.");
                continue;
            }

            if (string.Equals(material.shader.name, "Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase))
                AddShaderRisk(report, assetPath, "MaterialPurpleRisk", "Material uses Hidden/InternalErrorShader.");
        }
    }

    private static void AddShaderRisk(BuildValidationReport report, string assetPath, string reason, string message)
    {
        report.shaderRisks.Add(new ValidationShaderRisk
        {
            assetPath = assetPath,
            reason = reason,
            message = message
        });
        AddValidationError(report, reason, assetPath + ": " + message);
    }

    private static HashSet<string> CollectPlanAssetAndDependencyPaths(AddressablesBuildPlan plan)
    {
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int groupIndex = 0; groupIndex < plan.groups.Count; groupIndex++)
        {
            AddressablesGroupPlan group = plan.groups[groupIndex];
            for (int assetIndex = 0; assetIndex < group.assets.Count; assetIndex++)
            {
                string assetPath = NormalizeAssetPath(group.assets[assetIndex]);
                if (string.IsNullOrWhiteSpace(assetPath))
                    continue;

                paths.Add(assetPath);
                string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
                for (int dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
                {
                    string dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
                    if (!string.IsNullOrWhiteSpace(dependency))
                        paths.Add(dependency);
                }
            }
        }

        return paths;
    }

    private static HashSet<string> CollectResourceCheckAssetPaths()
    {
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!AssetDatabase.IsValidFolder(ResourceCheckRoot))
            return paths;

        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { ResourceCheckRoot });
        for (int index = 0; index < guids.Length; index++)
        {
            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[index]));
            if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                continue;

            paths.Add(assetPath);
        }

        return paths;
    }

    private static HashSet<string> CollectLayoutAssetPaths(BuildLayout layout)
    {
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuildLayout.Bundle bundle in BuildLayoutHelpers.EnumerateBundles(layout))
        {
            if (bundle == null || bundle.Files == null)
                continue;

            foreach (BuildLayout.File file in bundle.Files)
            {
                if (file == null)
                    continue;

                if (file.Assets != null)
                {
                    foreach (BuildLayout.ExplicitAsset asset in file.Assets)
                    {
                        string assetPath = NormalizeAssetPath(asset.AssetPath);
                        if (!string.IsNullOrWhiteSpace(assetPath))
                            paths.Add(assetPath);
                    }
                }

                if (file.OtherAssets == null)
                    continue;

                foreach (BuildLayout.DataFromOtherAsset asset in file.OtherAssets)
                {
                    string assetPath = NormalizeAssetPath(asset.AssetPath);
                    if (!string.IsNullOrWhiteSpace(assetPath))
                        paths.Add(assetPath);
                }
            }
        }

        return paths;
    }

    private static string FindLatestBuildLayoutReport(DateTime buildStart)
    {
        if (!Directory.Exists(BuildReportsRoot))
            return string.Empty;

        DateTime minWriteTime = buildStart == DateTime.MinValue ? DateTime.MinValue : buildStart.AddMinutes(-2);
        FileInfo latest = Directory.GetFiles(BuildReportsRoot, "buildlayout*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists && file.LastWriteTime >= minWriteTime)
            .OrderByDescending(file => file.LastWriteTime)
            .FirstOrDefault();

        return latest == null ? string.Empty : latest.FullName.Replace("\\", "/");
    }

    private static BuildValidationReport ReadPreviousValidationReport(string validationOutputPath)
    {
        if (string.IsNullOrWhiteSpace(validationOutputPath) || !File.Exists(validationOutputPath))
            return null;

        try
        {
            return JsonUtility.FromJson<BuildValidationReport>(File.ReadAllText(validationOutputPath, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    private static void WriteValidationReport(string validationOutputPath, BuildValidationReport report)
    {
        string directory = Path.GetDirectoryName(validationOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(validationOutputPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        Debug.Log("Addressables validation report: " + validationOutputPath + " errors=" + report.errorCount + " warnings=" + report.warningCount);
    }

    private static void AddValidationError(BuildValidationReport report, string code, string message)
    {
        report.errors.Add(code + ": " + message);
    }

    private static void AddValidationWarning(BuildValidationReport report, string code, string message)
    {
        report.warnings.Add(code + ": " + message);
    }

    private static void FinalizeValidationReport(BuildValidationReport report)
    {
        report.errorCount = report.errors.Count;
        report.warningCount = report.warnings.Count;
        report.duplicateAssetCount = report.duplicatedAssets.Count;
        report.cycleCount = report.dependencyCycles.Count;
        report.oversizedBundleCount = report.oversizedBundles.Count;
        report.highDependencyBundleCount = report.highDependencyBundles.Count;
        report.shaderRiskCount = report.shaderRisks.Count;
        report.unexpectedAssetCount = report.unexpectedAssets.Count;
        report.hashChangeCount = report.hashChanges.Count;
    }

    private static string StripBundleHash(string bundleName)
    {
        if (string.IsNullOrWhiteSpace(bundleName))
            return string.Empty;

        string normalized = bundleName.Replace("\\", "/");
        string fileName = Path.GetFileNameWithoutExtension(normalized);
        string extension = Path.GetExtension(normalized);
        string[] separators = { "_", "-", "." };
        foreach (string separator in separators)
        {
            int index = fileName.LastIndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index + 1 >= fileName.Length)
                continue;

            string suffix = fileName.Substring(index + 1);
            if (suffix.Length >= 16 && suffix.All(IsHexChar))
                return normalized.Substring(0, normalized.Length - Path.GetFileName(normalized).Length) + fileName.Substring(0, index) + extension;
        }

        return normalized;
    }

    private static bool HasHashLikeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        int runLength = 0;
        for (int index = 0; index < value.Length; index++)
        {
            runLength = IsHexChar(value[index]) ? runLength + 1 : 0;
            if (runLength >= 32)
                return true;
        }

        return false;
    }

    private static bool IsHexChar(char c)
    {
        return (c >= '0' && c <= '9') ||
               (c >= 'a' && c <= 'f') ||
               (c >= 'A' && c <= 'F');
    }

    private static BuildTarget ResolveBuildTarget(string[] args)
    {
        string targetName = GetArgumentValue(args, "--addressables-target");
        if (string.IsNullOrWhiteSpace(targetName))
            return EditorUserBuildSettings.activeBuildTarget;

        if (Enum.TryParse(targetName, true, out BuildTarget target))
            return target;

        throw new ArgumentException("Unknown Addressables build target: " + targetName);
    }

    private static void SwitchBuildTargetIfNeeded(BuildTarget target)
    {
        if (target == EditorUserBuildSettings.activeBuildTarget)
            return;

        BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(target);
        if (targetGroup == BuildTargetGroup.Unknown)
            throw new ArgumentException("Cannot resolve BuildTargetGroup for: " + target);

        EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target);
    }

    private static void ConfigureBuildTargetSettings(BuildTarget target, string[] args)
    {
        if (target != BuildTarget.Android)
            return;

        string graphicsApiName = GetArgumentValue(args, "--addressables-android-graphics-api");
        if (string.IsNullOrWhiteSpace(graphicsApiName))
            graphicsApiName = DefaultAndroidGraphicsApi;

        GraphicsDeviceType graphicsApi = ResolveAndroidGraphicsApi(graphicsApiName);
        GraphicsDeviceType[] currentApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
        if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) ||
            currentApis.Length != 1 ||
            currentApis[0] != graphicsApi)
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { graphicsApi });
            Debug.Log("Configured Android graphics API for Addressables build: " + graphicsApi);
        }
    }

    private static GraphicsDeviceType ResolveAndroidGraphicsApi(string value)
    {
        if (string.Equals(value, "GLES3", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "OpenGLES3", StringComparison.OrdinalIgnoreCase))
            return GraphicsDeviceType.OpenGLES3;

        if (string.Equals(value, "Vulkan", StringComparison.OrdinalIgnoreCase))
            return GraphicsDeviceType.Vulkan;

        throw new ArgumentException("Unsupported Android graphics API: " + value + ". Use Vulkan or GLES3.");
    }

    private static bool HasArgument(string[] args, string name)
    {
        return args != null && args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetArgumentValue(string[] args, string name)
    {
        if (args == null || string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string prefix = name + "=";
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg.Substring(prefix.Length).Trim('"');

            if (!string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                continue;

            return index + 1 < args.Length ? (args[index + 1] ?? string.Empty).Trim('"') : string.Empty;
        }

        return string.Empty;
    }

    private static int ResolveIntArgument(string[] args, string name, int defaultValue)
    {
        string value = GetArgumentValue(args, name);
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    private static long ResolveLongArgument(string[] args, string name, long defaultValue)
    {
        string value = GetArgumentValue(args, name);
        return long.TryParse(value, out long result) ? result : defaultValue;
    }

    private static long ResolveMaxBundleBytes(string[] args)
    {
        long bytes = ResolveLongArgument(args, "--addressables-max-bundle-bytes", -1L);
        if (bytes > 0)
            return bytes;

        long megabytes = ResolveLongArgument(args, "--addressables-max-bundle-mb", -1L);
        return megabytes > 0 ? megabytes * 1024L * 1024L : DefaultMaxBundleBytes;
    }

    private static long GetFileSizeSafe(string assetPath)
    {
        try
        {
            FileInfo fileInfo = new FileInfo(assetPath);
            return fileInfo.Exists ? fileInfo.Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    private static bool ContainsPathSegment(string assetPath, string segment)
    {
        string normalized = NormalizeAssetPath(assetPath);
        return normalized.IndexOf("/" + segment + "/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsUnderRoot(string assetPath, string root)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        string normalizedRoot = NormalizeAssetPath(root).TrimEnd('/');
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');
    }

    private static string AssetPathCombine(string root, string child)
    {
        return NormalizeAssetPath(NormalizeAssetPath(root).TrimEnd('/') + "/" + NormalizeAssetPath(child).TrimStart('/'));
    }

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "group";

        StringBuilder builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char c = char.ToLowerInvariant(value[index]);
            builder.Append(char.IsLetterOrDigit(c) ? c : '.');
        }

        return builder.ToString().Trim('.').Replace("..", ".");
    }

    private static string SanitizeChunkSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "chunk";

        StringBuilder builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char c = char.ToLowerInvariant(value[index]);
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '.');
        }

        return builder.ToString().Trim('.').Replace("..", ".");
    }

    private sealed class ProjectGroupSpec
    {
        public readonly string groupName;
        public readonly string[] roots;
        public readonly string[] excludeRoots;

        public ProjectGroupSpec(string groupName, string[] roots, string[] excludeRoots = null)
        {
            this.groupName = groupName;
            this.roots = roots ?? Array.Empty<string>();
            this.excludeRoots = excludeRoots ?? Array.Empty<string>();
        }
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }

    private sealed class BuildValidationOptions
    {
        public string validationOutputPath = string.Empty;
        public long maxBundleBytes = DefaultMaxBundleBytes;
        public int maxBundleDependencies = DefaultMaxBundleDependencies;
        public bool failOnWarnings;
    }

    [Serializable]
    private sealed class AddressablesBuildPlan
    {
        public string buildTarget = string.Empty;
        public string buildRoot = string.Empty;
        public string planPath = string.Empty;
        public bool includeSamples;
        public bool includeSourceAssets;
        public int sharedDependencyCount;
        public List<AddressablesGroupPlan> groups = new List<AddressablesGroupPlan>();
        public List<string> warnings = new List<string>();
        public List<string> errors = new List<string>();
    }

    [Serializable]
    private sealed class AddressablesGroupPlan
    {
        public string name = string.Empty;
        public int assetCount;
        public int dependencyCount;
        public long estimatedSourceBytes;
        public List<string> roots = new List<string>();
        public List<string> assets = new List<string>();
        public List<string> sharedDependencies = new List<string>();
    }

    [Serializable]
    private sealed class BuildValidationReport
    {
        public string validationMode = string.Empty;
        public string buildTarget = string.Empty;
        public string generatedAt = string.Empty;
        public string planPath = string.Empty;
        public string validationPath = string.Empty;
        public string buildOutputPath = string.Empty;
        public string buildLayoutPath = string.Empty;
        public string buildResultHash = string.Empty;
        public long maxBundleBytes;
        public int maxBundleDependencies;
        public int errorCount;
        public int warningCount;
        public int bundleCount;
        public int duplicateAssetCount;
        public int cycleCount;
        public int oversizedBundleCount;
        public int highDependencyBundleCount;
        public int shaderRiskCount;
        public int unexpectedAssetCount;
        public int hashChangeCount;
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public List<ValidationBundleSummary> bundles = new List<ValidationBundleSummary>();
        public List<ValidationDuplicateAsset> duplicatedAssets = new List<ValidationDuplicateAsset>();
        public List<string> dependencyCycles = new List<string>();
        public List<ValidationBundleSummary> oversizedBundles = new List<ValidationBundleSummary>();
        public List<ValidationBundleSummary> highDependencyBundles = new List<ValidationBundleSummary>();
        public List<ValidationShaderRisk> shaderRisks = new List<ValidationShaderRisk>();
        public List<ValidationUnexpectedAsset> unexpectedAssets = new List<ValidationUnexpectedAsset>();
        public List<ValidationHashChange> hashChanges = new List<ValidationHashChange>();
    }

    [Serializable]
    private sealed class ValidationBundleSummary
    {
        public string name = string.Empty;
        public string internalName = string.Empty;
        public string stableName = string.Empty;
        public string groupName = string.Empty;
        public long fileSize;
        public long uncompressedFileSize;
        public int dependencyCount;
        public int assetCount;
        public string hash = string.Empty;
        public List<string> dependencies = new List<string>();
    }

    [Serializable]
    private sealed class ValidationDuplicateAsset
    {
        public string assetGuid = string.Empty;
        public string assetPath = string.Empty;
        public int duplicatedObjectCount;
        public List<string> bundles = new List<string>();
    }

    [Serializable]
    private sealed class ValidationShaderRisk
    {
        public string assetPath = string.Empty;
        public string reason = string.Empty;
        public string message = string.Empty;
    }

    [Serializable]
    private sealed class ValidationUnexpectedAsset
    {
        public string assetPath = string.Empty;
        public string bundleName = string.Empty;
        public string reason = string.Empty;
    }

    [Serializable]
    private sealed class ValidationHashChange
    {
        public string stableName = string.Empty;
        public string currentName = string.Empty;
        public string previousHash = string.Empty;
        public string currentHash = string.Empty;
    }
}
