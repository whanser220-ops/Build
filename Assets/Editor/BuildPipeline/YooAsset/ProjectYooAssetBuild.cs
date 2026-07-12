using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using YooAsset;
using YooAsset.Editor;

public static class ProjectYooAssetBuild
{
    private const string DefaultBuildRoot = "Assets";
    private const string ResourceCheckRoot = "Assets/GameResources";
    private const string DefaultPlanRoot = ".workspace/artifacts/yooasset";
    private const string DefaultBuildOutputRoot = ".workspace/artifacts/yooasset-build";
    private const string PlanFileName = "yooasset_build_plan.json";
    private const string DefaultPackageName = "DefaultPackage";
    private const string GameAssetsDirectoryName = "GameAssets";
    private const string ChunksDirectoryName = "Chunks";
    private const string CommonDirectoryName = "Common";
    private const string SharedDirectoryName = "Shared";
    private const string WorldsDirectoryName = "Worlds";
    private const string SeasonsDirectoryName = "Seasons";
    private const string ScenesDirectoryName = "Scenes";
    private const string CommonShaderRoot = "Common/Shaders";
    private const string SharedTextureRoot = "Assets/GameResources/Stylized Pack - Common/Sources/Textures";
    private const string MeadowEnvironmentPrefabRoot = "Assets/GameAssets/Prefabs/Meadow Environment";
    private const string MeadowTerrainDetailsPrefabRoot = "Assets/GameAssets/Prefabs/Meadow Terrain Details";
    private const string MeadowLegacyConfigRoot = "Configs/Post Processing/Meadow Environment";
    private const string MeadowLegacySceneRoot = "Scenes/Meadow Environment";
    private const string MeadowGeneratedSceneRoot = "Worlds/Meadow/Scenes";
    private const string DefaultAndroidGraphicsApi = "Vulkan";
    private const string AngryMeshTag = "angrymesh";
    private const long DefaultPackageSourceBytes = 32L * 1024L * 1024L;
    private const long ScenePackageSourceBytes = 16L * 1024L * 1024L;

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

    [MenuItem("Tools/ANGRY MESH/YooAsset/Prepare Collectors")]
    public static void PrepareCollectorsFromMenu()
    {
        PrepareCollectors(Environment.GetCommandLineArgs());
    }

    [MenuItem("Tools/ANGRY MESH/YooAsset/Build Content")]
    public static void BuildFromMenu()
    {
        BuildFromCommandLine();
    }

    public static void PrepareCollectorsFromCommandLine()
    {
        ConfigureBatchmodeLogging();
        PrepareCollectors(Environment.GetCommandLineArgs());
    }

    public static void BuildFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        ConfigureBatchmodeLogging();

        YooAssetBuildPlan plan = PrepareCollectors(args);
        if (HasArgument(args, "--yooasset-dry-run"))
        {
            Debug.Log("YooAsset dry run finished. Plan: " + plan.planPath);
            return;
        }

        BuildYooAssetContent(args, plan);
    }

    private static void ConfigureBatchmodeLogging()
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
    }

    private static YooAssetBuildPlan PrepareCollectors(string[] args)
    {
        BuildTarget buildTarget = ResolveBuildTarget(args);
        ConfigureBuildTargetSettings(buildTarget, args);
        SwitchBuildTargetIfNeeded(buildTarget);

        bool includeSamples = HasArgument(args, "--yooasset-include-samples");
        bool includeSourceAssets = HasArgument(args, "--yooasset-include-source-assets");
        string packageName = ResolvePackageName(args);

        string buildRoot = NormalizeAssetPath(GetArgumentValue(args, "--yooasset-build-root"));
        if (string.IsNullOrWhiteSpace(buildRoot))
            buildRoot = DefaultBuildRoot;

        string planOutput = GetArgumentValue(args, "--yooasset-plan-output");
        if (string.IsNullOrWhiteSpace(planOutput))
            planOutput = Path.Combine(DefaultPlanRoot, buildTarget.ToString(), AngryMeshTag, PlanFileName);

        YooAssetBuildPlan plan = BuildPlan(buildRoot, buildTarget, packageName, includeSamples, includeSourceAssets);
        plan.planPath = Path.GetFullPath(planOutput).Replace("\\", "/");
        WritePlan(plan.planPath, plan);

        if (plan.errors.Count > 0)
            throw new InvalidOperationException("YooAsset plan contains errors. See plan: " + plan.planPath);

        ApplyPlan(plan);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Prepared ANGRY MESH YooAsset collectors. Plan: " + plan.planPath);
        return plan;
    }

    private static void BuildYooAssetContent(string[] args, YooAssetBuildPlan plan)
    {
        string packageVersion = ResolvePackageVersion(args);
        string buildOutputRoot = GetArgumentValue(args, "--yooasset-build-output");
        if (string.IsNullOrWhiteSpace(buildOutputRoot))
            buildOutputRoot = DefaultBuildOutputRoot;

        ScriptableBuildParameters parameters = new ScriptableBuildParameters
        {
            BuildOutputRoot = Path.GetFullPath(buildOutputRoot).Replace("\\", "/"),
            BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
            BuildPipeline = "ScriptableBuildPipeline",
            BuildBundleType = (int)EBundleType.AssetBundle,
            BuildTarget = ResolveBuildTarget(args),
            PackageName = plan.packageName,
            PackageVersion = packageVersion,
            PackageNote = "CI YooAsset build",
            ClearBuildCacheFiles = HasArgument(args, "--yooasset-clear-build-cache"),
            UseAssetDependencyDB = true,
            EnableSharePackRule = false,
            SingleReferencedPackAlone = false,
            VerifyBuildingResult = true,
            FileNameStyle = EFileNameStyle.BundleName_HashName,
            BundledCopyOption = HasArgument(args, "--yooasset-no-bundled-copy")
                ? EBundledCopyOption.None
                : EBundledCopyOption.ClearAndCopyAll,
            CompressOption = ECompressOption.LZ4,
            StripUnityVersion = false,
            DisableWriteTypeTree = false,
            ReplaceAssetPathWithAddress = false,
            TrackSpriteAtlasDependencies = true,
            WriteLinkXML = true,
            BuiltinShadersBundleName = "unityshaders.bundle",
            MonoScriptsBundleName = "unitymonos.bundle"
        };

        YooAsset.Editor.BuildResult result = new YooAsset.Editor.ScriptableBuildPipeline().Run(parameters, true);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "YooAsset build failed. Task=" + result.FailedTask +
                ", Error=" + result.ErrorInfo +
                ", Stack=" + result.ErrorStack);
        }

        Debug.Log(
            "YooAsset build succeeded. OutputPackageDirectory=" + result.OutputPackageDirectory +
            ", Plan=" + plan.planPath +
            ", Package=" + plan.packageName +
            ", Version=" + packageVersion);
    }

    private static YooAssetBuildPlan BuildPlan(
        string buildRoot,
        BuildTarget buildTarget,
        string packageName,
        bool includeSamples,
        bool includeSourceAssets)
    {
        YooAssetBuildPlan plan = new YooAssetBuildPlan
        {
            buildTarget = buildTarget.ToString(),
            buildRoot = buildRoot,
            packageName = packageName,
            includeSamples = includeSamples,
            includeSourceAssets = includeSourceAssets
        };

        List<ProjectGroupSpec> specs = CreateGameAssetSpecs(buildRoot);
        if (includeSourceAssets)
            specs.AddRange(CreateGameResourceSpecs());

        if (includeSamples)
            specs.Add(new ProjectGroupSpec("angrymesh.samples.urp", new[] { "Assets/Samples/Universal RP" }));

        HashSet<string> assignedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectGroupSpec spec in specs)
        {
            List<string> assetPaths = ResolveAssetPaths(spec, plan)
                .Where(path => assignedAssets.Add(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (assetPaths.Count == 0)
                continue;

            AddPlanGroups(plan, spec, assetPaths, groupNames);
        }

        FillDependencySummary(plan);

        if (plan.groups.Count == 0)
            plan.errors.Add("No YooAsset collector groups were generated.");

        return plan;
    }

    private static void AddPlanGroups(
        YooAssetBuildPlan plan,
        ProjectGroupSpec spec,
        List<string> assetPaths,
        HashSet<string> groupNames)
    {
        List<List<string>> batches = SplitAssetPathsBySourceBytes(assetPaths, spec.maxSourceBytes).ToList();
        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            List<string> batch = batches[batchIndex];
            string groupName = batches.Count == 1
                ? spec.groupName
                : spec.groupName + ".part" + (batchIndex + 1).ToString("00");

            groupName = MakeUniqueGroupName(groupName, groupNames);
            plan.groups.Add(new YooAssetGroupPlan
            {
                name = groupName,
                assetCount = batch.Count,
                estimatedSourceBytes = batch.Sum(GetFileSizeSafe),
                roots = spec.roots.ToList(),
                explicitAssets = spec.explicitAssets.ToList(),
                assets = batch
            });
        }
    }

    private static IEnumerable<List<string>> SplitAssetPathsBySourceBytes(List<string> assetPaths, long maxSourceBytes)
    {
        if (maxSourceBytes <= 0 || assetPaths.Sum(GetFileSizeSafe) <= maxSourceBytes)
        {
            yield return assetPaths;
            yield break;
        }

        List<string> current = new List<string>();
        long currentBytes = 0L;
        foreach (string assetPath in assetPaths)
        {
            long fileSize = Math.Max(1L, GetFileSizeSafe(assetPath));
            if (current.Count > 0 && currentBytes + fileSize > maxSourceBytes)
            {
                yield return current;
                current = new List<string>();
                currentBytes = 0L;
            }

            current.Add(assetPath);
            currentBytes += fileSize;
        }

        if (current.Count > 0)
            yield return current;
    }

    private static void ApplyPlan(YooAssetBuildPlan plan)
    {
        BundleCollectorSetting setting = BundleCollectorSettingData.Setting;
        setting.ClearAll();
        setting.ShowPackageView = true;
        setting.UniqueBundleName = false;

        BundleCollectorPackage package = new BundleCollectorPackage
        {
            PackageName = plan.packageName,
            PackageDesc = "Generated by ProjectYooAssetBuild",
            EnableAddressable = false,
            SupportExtensionless = true,
            LocationToLower = false,
            IncludeAssetGUID = true,
            AutoCollectShaders = false,
            IgnoreRuleName = nameof(NormalIgnoreRule)
        };
        setting.Packages.Add(package);

        for (int groupIndex = 0; groupIndex < plan.groups.Count; groupIndex++)
        {
            YooAssetGroupPlan groupPlan = plan.groups[groupIndex];
            BundleCollectorGroup group = new BundleCollectorGroup
            {
                GroupName = groupPlan.name,
                GroupDesc = "Generated by ProjectYooAssetBuild",
                AssetTags = AngryMeshTag + ";" + groupPlan.name,
                ActiveRuleName = nameof(EnableGroup)
            };

            for (int assetIndex = 0; assetIndex < groupPlan.assets.Count; assetIndex++)
            {
                string assetPath = groupPlan.assets[assetIndex];
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    plan.errors.Add("Missing GUID for asset: " + assetPath);
                    continue;
                }

                group.Collectors.Add(new BundleCollector
                {
                    CollectPath = assetPath,
                    CollectorGUID = guid,
                    CollectorType = ECollectorType.MainAssetCollector,
                    AddressRuleName = nameof(AddressDisable),
                    PackRuleName = nameof(PackGroup),
                    FilterRuleName = nameof(CollectAll),
                    AssetTags = groupPlan.name,
                    UserData = "generated"
                });
            }

            package.Groups.Add(group);
        }

        if (plan.errors.Count > 0)
            throw new InvalidOperationException("YooAsset plan contains collector errors. See plan: " + plan.planPath);

        setting.CheckPackageConfigError(plan.packageName);
        EditorUtility.SetDirty(setting);
        BundleCollectorSettingData.SaveFile();
    }

    private static List<ProjectGroupSpec> CreateGameAssetSpecs(string buildRoot)
    {
        string gameAssetsRoot = ResolveGameAssetsRoot(buildRoot);
        string[] excludeRoots = CreateGameAssetExcludeRoots(gameAssetsRoot);
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
        specs.AddRange(CreateLegacyMeadowSpecs(gameAssetsRoot));
        specs.AddRange(CreateSpecsFromPackageFolders("angrymesh.gameassets", gameAssetsRoot, excludeRoots, DefaultPackageSourceBytes));
        return specs;
    }

    private static string ResolveGameAssetsRoot(string buildRoot)
    {
        string normalizedBuildRoot = NormalizeAssetPath(buildRoot).TrimEnd('/');
        if (string.Equals(Path.GetFileName(normalizedBuildRoot), GameAssetsDirectoryName, StringComparison.OrdinalIgnoreCase))
            return normalizedBuildRoot;

        return AssetPathCombine(normalizedBuildRoot, GameAssetsDirectoryName);
    }

    private static string[] CreateGameAssetExcludeRoots(string gameAssetsRoot)
    {
        List<string> roots = new List<string>
        {
            AssetPathCombine(gameAssetsRoot, CommonDirectoryName),
            AssetPathCombine(gameAssetsRoot, WorldsDirectoryName),
            MeadowEnvironmentPrefabRoot,
            MeadowTerrainDetailsPrefabRoot,
            AssetPathCombine(gameAssetsRoot, MeadowLegacyConfigRoot),
            AssetPathCombine(gameAssetsRoot, MeadowLegacySceneRoot),
            AssetPathCombine(gameAssetsRoot, MeadowGeneratedSceneRoot)
        };

        return roots
            .Select(NormalizeAssetPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static List<ProjectGroupSpec> CreateGameResourceSpecs()
    {
        if (AssetDatabase.IsValidFolder(ResourceCheckRoot))
        {
            return CreateSpecsFromPackageFolders(
                "angrymesh.gameresources",
                ResourceCheckRoot,
                Array.Empty<string>(),
                DefaultPackageSourceBytes).ToList();
        }

        return new List<ProjectGroupSpec>
        {
            new ProjectGroupSpec("angrymesh.gameresources", new[] { ResourceCheckRoot })
        };
    }

    private static IEnumerable<ProjectGroupSpec> CreateSharedRuntimeSpecs(string gameAssetsRoot)
    {
        string sharedShaderRoot = AssetPathCombine(gameAssetsRoot, CommonShaderRoot);
        if (AssetDatabase.IsValidFolder(sharedShaderRoot))
        {
            foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(
                         "angrymesh.shared.shaders",
                         sharedShaderRoot,
                         Array.Empty<string>(),
                         DefaultPackageSourceBytes))
            {
                yield return spec;
            }
        }

        if (AssetDatabase.IsValidFolder(SharedTextureRoot))
        {
            foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(
                         "angrymesh.shared.textures",
                         SharedTextureRoot,
                         Array.Empty<string>(),
                         DefaultPackageSourceBytes))
            {
                yield return spec;
            }
        }
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

        foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(
                     "angrymesh.gameassets.common",
                     commonRoot,
                     commonExcludeRoots,
                     DefaultPackageSourceBytes))
        {
            yield return spec;
        }
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
            List<string> worldExcludeRoots = new List<string>();

            string sharedRoot = AssetPathCombine(worldFolder, SharedDirectoryName);
            if (AssetDatabase.IsValidFolder(sharedRoot))
            {
                worldExcludeRoots.Add(sharedRoot);
                foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(
                             groupPrefix + ".shared",
                             sharedRoot,
                             Array.Empty<string>(),
                             DefaultPackageSourceBytes))
                {
                    yield return spec;
                }
            }

            string seasonsRoot = AssetPathCombine(worldFolder, SeasonsDirectoryName);
            if (AssetDatabase.IsValidFolder(seasonsRoot))
            {
                worldExcludeRoots.Add(seasonsRoot);
                foreach (string seasonFolder in AssetDatabase.GetSubFolders(seasonsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string seasonName = SanitizeSegment(Path.GetFileName(seasonFolder));
                    foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(
                                 groupPrefix + ".season." + seasonName,
                                 seasonFolder,
                                 Array.Empty<string>(),
                                 DefaultPackageSourceBytes))
                    {
                        yield return spec;
                    }
                }
            }

            string chunksRoot = AssetPathCombine(worldFolder, ChunksDirectoryName);
            if (AssetDatabase.IsValidFolder(chunksRoot))
            {
                worldExcludeRoots.Add(chunksRoot);
                foreach (string chunkFolder in AssetDatabase.GetSubFolders(chunksRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string chunkName = SanitizeChunkSegment(Path.GetFileName(chunkFolder));
                    yield return new ProjectGroupSpec(
                        groupPrefix + ".chunks." + chunkName,
                        new[] { chunkFolder },
                        Array.Empty<string>(),
                        null,
                        ScenePackageSourceBytes);
                }
            }

            string scenesRoot = AssetPathCombine(worldFolder, ScenesDirectoryName);
            if (AssetDatabase.IsValidFolder(scenesRoot))
            {
                worldExcludeRoots.Add(scenesRoot);
                foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(
                             groupPrefix + ".scenes",
                             scenesRoot,
                             Array.Empty<string>(),
                             ScenePackageSourceBytes))
                {
                    yield return spec;
                }
            }

            foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(
                         groupPrefix + ".misc",
                         worldFolder,
                         worldExcludeRoots.ToArray(),
                         DefaultPackageSourceBytes))
            {
                yield return spec;
            }
        }
    }

    private static IEnumerable<ProjectGroupSpec> CreateLegacyMeadowSpecs(string gameAssetsRoot)
    {
        foreach (ProjectGroupSpec spec in CreateSpecsForOptionalRoot(
                     "angrymesh.meadow.environment.prefabs",
                     MeadowEnvironmentPrefabRoot,
                     DefaultPackageSourceBytes))
        {
            yield return spec;
        }

        foreach (ProjectGroupSpec spec in CreateSpecsForOptionalRoot(
                     "angrymesh.meadow.terrain.details",
                     MeadowTerrainDetailsPrefabRoot,
                     DefaultPackageSourceBytes))
        {
            yield return spec;
        }

        foreach (ProjectGroupSpec spec in CreateSpecsForOptionalRoot(
                     "angrymesh.meadow.legacy.config",
                     AssetPathCombine(gameAssetsRoot, MeadowLegacyConfigRoot),
                     DefaultPackageSourceBytes))
        {
            yield return spec;
        }

        foreach (ProjectGroupSpec spec in CreateSpecsForOptionalRoot(
                     "angrymesh.meadow.legacy.scenes",
                     AssetPathCombine(gameAssetsRoot, MeadowLegacySceneRoot),
                     ScenePackageSourceBytes))
        {
            yield return spec;
        }

        foreach (ProjectGroupSpec spec in CreateSpecsForOptionalRoot(
                     "angrymesh.meadow.generated.scenes",
                     AssetPathCombine(gameAssetsRoot, MeadowGeneratedSceneRoot),
                     ScenePackageSourceBytes))
        {
            yield return spec;
        }
    }

    private static IEnumerable<ProjectGroupSpec> CreateSpecsForOptionalRoot(string groupPrefix, string root, long maxSourceBytes)
    {
        if (!AssetDatabase.IsValidFolder(root))
            yield break;

        foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(groupPrefix, root, Array.Empty<string>(), maxSourceBytes))
            yield return spec;
    }

    private static IEnumerable<ProjectGroupSpec> CreateSpecsFromPackageFolders(
        string groupPrefix,
        string root,
        string[] excludeRoots,
        long maxSourceBytes)
    {
        string normalizedRoot = NormalizeAssetPath(root);
        if (!AssetDatabase.IsValidFolder(normalizedRoot))
        {
            yield return new ProjectGroupSpec(groupPrefix, new[] { normalizedRoot }, excludeRoots, null, maxSourceBytes);
            yield break;
        }

        List<string> directAssets = CollectDirectAssetPaths(normalizedRoot, excludeRoots);
        if (directAssets.Count > 0)
        {
            yield return new ProjectGroupSpec(
                groupPrefix + ".root",
                Array.Empty<string>(),
                excludeRoots,
                directAssets.ToArray(),
                maxSourceBytes);
        }

        string[] subFolders = AssetDatabase.GetSubFolders(normalizedRoot)
            .Where(path => !IsUnderAnyRoot(path, excludeRoots))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (subFolders.Length == 0 && directAssets.Count == 0)
        {
            yield return new ProjectGroupSpec(
                groupPrefix + "." + SanitizeSegment(Path.GetFileName(normalizedRoot)),
                new[] { normalizedRoot },
                excludeRoots,
                null,
                maxSourceBytes);
            yield break;
        }

        for (int index = 0; index < subFolders.Length; index++)
        {
            string subFolder = subFolders[index];
            string segment = SanitizeSegment(Path.GetFileName(subFolder));
            yield return new ProjectGroupSpec(
                groupPrefix + "." + segment,
                new[] { subFolder },
                excludeRoots,
                null,
                maxSourceBytes);
        }
    }

    private static List<string> CollectDirectAssetPaths(string root, IReadOnlyList<string> excludeRoots)
    {
        List<string> result = new List<string>();
        if (!Directory.Exists(root))
            return result;

        foreach (string filePath in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            string assetPath = NormalizeAssetPath(filePath);
            if (ShouldIncludeAsset(assetPath, excludeRoots))
                result.Add(assetPath);
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> ResolveAssetPaths(ProjectGroupSpec spec, YooAssetBuildPlan plan)
    {
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < spec.explicitAssets.Length; index++)
        {
            string explicitAsset = NormalizeAssetPath(spec.explicitAssets[index]);
            if (ShouldIncludeAsset(explicitAsset, spec.excludeRoots))
                paths.Add(explicitAsset);
        }

        for (int rootIndex = 0; rootIndex < spec.roots.Length; rootIndex++)
        {
            string root = NormalizeAssetPath(spec.roots[rootIndex]);
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (!AssetDatabase.IsValidFolder(root))
            {
                if (ShouldIncludeAsset(root, spec.excludeRoots))
                    paths.Add(root);
                else if (!File.Exists(root))
                    plan.warnings.Add("YooAsset root folder does not exist: " + root);

                continue;
            }

            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { root });
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[guidIndex]));
                if (ShouldIncludeAsset(assetPath, spec.excludeRoots))
                    paths.Add(assetPath);
            }
        }

        return paths;
    }

    private static bool ShouldIncludeAsset(string assetPath, IReadOnlyList<string> excludeRoots)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        if (!normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (AssetDatabase.IsValidFolder(normalizedPath))
            return false;

        if (ContainsPathSegment(normalizedPath, "Editor"))
            return false;

        string extension = Path.GetExtension(normalizedPath);
        if (IgnoredExtensions.Any(ignored => string.Equals(ignored, extension, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (IsUnderAnyRoot(normalizedPath, excludeRoots))
            return false;

        if (AssetDatabase.GetMainAssetTypeAtPath(normalizedPath) == null)
            return false;

        return File.Exists(normalizedPath);
    }

    private static void FillDependencySummary(YooAssetBuildPlan plan)
    {
        Dictionary<string, HashSet<string>> dependencyToGroups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> groupDependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        for (int groupIndex = 0; groupIndex < plan.groups.Count; groupIndex++)
        {
            YooAssetGroupPlan group = plan.groups[groupIndex];
            HashSet<string> groupAssets = new HashSet<string>(group.assets, StringComparer.OrdinalIgnoreCase);
            HashSet<string> dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int assetIndex = 0; assetIndex < group.assets.Count; assetIndex++)
            {
                string assetPath = group.assets[assetIndex];
                string[] assetDependencies = AssetDatabase.GetDependencies(assetPath, true);
                for (int dependencyIndex = 0; dependencyIndex < assetDependencies.Length; dependencyIndex++)
                {
                    string dependency = NormalizeAssetPath(assetDependencies[dependencyIndex]);
                    if (string.IsNullOrWhiteSpace(dependency) || groupAssets.Contains(dependency))
                        continue;

                    if (!ShouldTrackDependency(dependency))
                        continue;

                    dependencies.Add(dependency);
                    if (!dependencyToGroups.TryGetValue(dependency, out HashSet<string> ownerGroups))
                    {
                        ownerGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        dependencyToGroups.Add(dependency, ownerGroups);
                    }

                    ownerGroups.Add(group.name);
                }
            }

            groupDependencies[group.name] = dependencies;
            group.dependencyCount = dependencies.Count;
        }

        plan.sharedDependencyCount = dependencyToGroups.Count(pair => pair.Value.Count > 1);
        for (int groupIndex = 0; groupIndex < plan.groups.Count; groupIndex++)
        {
            YooAssetGroupPlan group = plan.groups[groupIndex];
            if (!groupDependencies.TryGetValue(group.name, out HashSet<string> dependencies))
                continue;

            group.sharedDependencies = dependencies
                .Where(dependency => dependencyToGroups.TryGetValue(dependency, out HashSet<string> owners) && owners.Count > 1)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();
        }
    }

    private static bool ShouldTrackDependency(string assetPath)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        if (!normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return false;

        string extension = Path.GetExtension(normalizedPath);
        if (IgnoredExtensions.Any(ignored => string.Equals(ignored, extension, StringComparison.OrdinalIgnoreCase)))
            return false;

        return !AssetDatabase.IsValidFolder(normalizedPath);
    }

    private static void WritePlan(string planPath, YooAssetBuildPlan plan)
    {
        string directory = Path.GetDirectoryName(planPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(planPath, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
        Debug.Log("YooAsset build plan: " + planPath + " groups=" + plan.groups.Count);
    }

    private static BuildTarget ResolveBuildTarget(string[] args)
    {
        string targetName = GetArgumentValue(args, "--yooasset-target");
        if (string.IsNullOrWhiteSpace(targetName))
            return EditorUserBuildSettings.activeBuildTarget;

        if (Enum.TryParse(targetName, true, out BuildTarget target))
            return target;

        throw new ArgumentException("Unknown YooAsset build target: " + targetName);
    }

    private static string ResolvePackageName(string[] args)
    {
        string packageName = GetArgumentValue(args, "--yooasset-package-name");
        return string.IsNullOrWhiteSpace(packageName) ? DefaultPackageName : packageName.Trim();
    }

    private static string ResolvePackageVersion(string[] args)
    {
        string packageVersion = GetArgumentValue(args, "--yooasset-package-version");
        if (!string.IsNullOrWhiteSpace(packageVersion))
            return packageVersion.Trim();

        packageVersion = Environment.GetEnvironmentVariable("BUILD_NUMBER");
        if (!string.IsNullOrWhiteSpace(packageVersion))
            return packageVersion.Trim();

        return DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    }

    private static void SwitchBuildTargetIfNeeded(BuildTarget target)
    {
        if (target == EditorUserBuildSettings.activeBuildTarget)
            return;

        BuildTargetGroup targetGroup = UnityEditor.BuildPipeline.GetBuildTargetGroup(target);
        if (targetGroup == BuildTargetGroup.Unknown)
            throw new ArgumentException("Cannot resolve BuildTargetGroup for: " + target);

        EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target);
    }

    private static void ConfigureBuildTargetSettings(BuildTarget target, string[] args)
    {
        if (target != BuildTarget.Android)
            return;

        string graphicsApiName = GetArgumentValue(args, "--yooasset-android-graphics-api");
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
            Debug.Log("Configured Android graphics API for YooAsset build: " + graphicsApi);
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

    private static bool IsUnderAnyRoot(string assetPath, IReadOnlyList<string> roots)
    {
        if (roots == null)
            return false;

        for (int index = 0; index < roots.Count; index++)
        {
            if (IsUnderRoot(assetPath, roots[index]))
                return true;
        }

        return false;
    }

    private static bool IsUnderRoot(string assetPath, string root)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        string normalizedRoot = NormalizeAssetPath(root).TrimEnd('/');
        return !string.IsNullOrWhiteSpace(normalizedRoot) &&
               (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase));
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

        string result = builder.ToString().Trim('.');
        while (result.Contains(".."))
            result = result.Replace("..", ".");

        return string.IsNullOrWhiteSpace(result) ? "group" : result;
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

        string result = builder.ToString().Trim('.');
        while (result.Contains(".."))
            result = result.Replace("..", ".");

        return string.IsNullOrWhiteSpace(result) ? "chunk" : result;
    }

    private static string MakeUniqueGroupName(string groupName, HashSet<string> existingNames)
    {
        string candidate = groupName;
        int suffix = 2;
        while (!existingNames.Add(candidate))
        {
            candidate = groupName + "." + suffix.ToString("00");
            suffix++;
        }

        return candidate;
    }

    private sealed class ProjectGroupSpec
    {
        public readonly string groupName;
        public readonly string[] roots;
        public readonly string[] excludeRoots;
        public readonly string[] explicitAssets;
        public readonly long maxSourceBytes;

        public ProjectGroupSpec(
            string groupName,
            string[] roots,
            string[] excludeRoots = null,
            string[] explicitAssets = null,
            long maxSourceBytes = DefaultPackageSourceBytes)
        {
            this.groupName = groupName;
            this.roots = roots ?? Array.Empty<string>();
            this.excludeRoots = excludeRoots ?? Array.Empty<string>();
            this.explicitAssets = explicitAssets ?? Array.Empty<string>();
            this.maxSourceBytes = maxSourceBytes;
        }
    }

    [Serializable]
    private sealed class YooAssetBuildPlan
    {
        public string buildTarget = string.Empty;
        public string buildRoot = string.Empty;
        public string packageName = string.Empty;
        public string planPath = string.Empty;
        public bool includeSamples;
        public bool includeSourceAssets;
        public int sharedDependencyCount;
        public List<YooAssetGroupPlan> groups = new List<YooAssetGroupPlan>();
        public List<string> warnings = new List<string>();
        public List<string> errors = new List<string>();
    }

    [Serializable]
    private sealed class YooAssetGroupPlan
    {
        public string name = string.Empty;
        public int assetCount;
        public int dependencyCount;
        public long estimatedSourceBytes;
        public List<string> roots = new List<string>();
        public List<string> explicitAssets = new List<string>();
        public List<string> assets = new List<string>();
        public List<string> sharedDependencies = new List<string>();
    }
}
