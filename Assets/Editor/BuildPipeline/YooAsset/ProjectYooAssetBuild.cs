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
    private const string LegacyResourceCheckRoot = "Assets/GameResources";
    private const string DefaultPlanRoot = ".workspace/artifacts/yooasset";
    private const string DefaultBuildOutputRoot = ".workspace/artifacts/yooasset-build";
    private const string PlanFileName = "yooasset_build_plan.json";
    private const string DefaultPackageName = "DefaultPackage";
    private const string GameContentDirectoryName = "Game";
    private const string LegacyGameAssetsDirectoryName = "GameAssets";
    private const string ArtDirectoryName = "Art";
    private const string RuntimeDirectoryName = "Runtime";
    private const string CommonDirectoryName = "Common";
    private const string SharedDirectoryName = "Shared";
    private const string MaterialsDirectoryName = "Materials";
    private const string WorldsDirectoryName = "Worlds";
    private const string SeasonsDirectoryName = "Seasons";
    private const string ScenesDirectoryName = "Scenes";
    private const string CommonShaderRoot = "Common/Shaders";
    private const string CommonFontsRoot = "Common/Fonts";
    private const string CommonFunctionsRoot = "Common/Functions";
    private const string SharedRuntimeShaderRoot = "Assets/Game/Shared/StylizedPackCommon/Runtime/Shaders";
    private const string SharedAspGlobalSettingsRoot = "Assets/Game/Shared/StylizedPackCommon/Runtime/ASP Global Settings";
    private const string SharedTextureRoot = "Assets/Game/Shared/StylizedPackCommon/Art/Sources/Textures";
    private const string MeadowRuntimeRoot = "Assets/Game/Worlds/Meadow/Runtime";
    private const string QianxiaGeneratedRuntimeRoot = "Assets/Game/Characters/Qianxia/Runtime/Generated";
    private static readonly string[] StandaloneArtDependencyRoots =
    {
        "Assets/Game/Characters/Qianxia/Art/Meshs",
        "Assets/Game/Characters/Qianxia/Art/Textures"
    };

    private const string LegacyMeadowEnvironmentPrefabRoot = "Assets/GameAssets/Prefabs/Meadow Environment";
    private const string LegacyMeadowTerrainDetailsPrefabRoot = "Assets/GameAssets/Prefabs/Meadow Terrain Details";
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

    public static void ValidatePrefabBundleFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        ConfigureBatchmodeLogging();

        string bundleFile = GetArgumentValue(args, "--asset-bundle-file");
        string dependencyList = GetArgumentValue(args, "--asset-bundle-dependencies");
        ValidatePrefabBundle(bundleFile, SplitCommandLineList(dependencyList));
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
        bool includeSourceAssets = HasArgument(args, "--yooasset-include-source-assets") &&
                                   !HasArgument(args, "--yooasset-exclude-source-assets");
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

        YooAsset.Editor.BuildResult result = new ProjectYooAssetScriptableBuildPipeline().Run(parameters, true);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "YooAsset build failed. Task=" + result.FailedTask +
                ", Error=" + result.ErrorInfo +
                ", Stack=" + result.ErrorStack);
        }

        string bundleReportPath = string.Empty;
        if (!HasArgument(args, "--bundle-report-disable"))
        {
            bundleReportPath = ProjectYooAssetBundleReportExporter.Export(
                result,
                parameters,
                plan.planPath,
                args,
                ProjectYooAssetScriptableBuildPipeline.LastLayoutSnapshot);
        }

        Debug.Log(
            "YooAsset build succeeded. OutputPackageDirectory=" + result.OutputPackageDirectory +
            ", Plan=" + plan.planPath +
            ", BundleReport=" + bundleReportPath +
            ", Package=" + plan.packageName +
            ", Version=" + packageVersion);
    }

    private static void ValidatePrefabBundle(string bundleFile, IEnumerable<string> dependencyFiles)
    {
        string normalizedBundleFile = Path.GetFullPath(NormalizeAssetPath(bundleFile));
        if (!File.Exists(normalizedBundleFile))
            throw new FileNotFoundException("AssetBundle file not found.", normalizedBundleFile);

        List<AssetBundle> loadedBundles = new List<AssetBundle>();
        List<string> issues = new List<string>();
        try
        {
            foreach (string dependencyFile in dependencyFiles)
            {
                string normalizedDependencyFile = Path.GetFullPath(NormalizeAssetPath(dependencyFile));
                if (!File.Exists(normalizedDependencyFile))
                    throw new FileNotFoundException("AssetBundle dependency file not found.", normalizedDependencyFile);

                AssetBundle dependencyBundle = AssetBundle.LoadFromFile(normalizedDependencyFile);
                if (dependencyBundle == null)
                    throw new InvalidOperationException("Failed to load AssetBundle dependency: " + normalizedDependencyFile);

                loadedBundles.Add(dependencyBundle);
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(normalizedBundleFile);
            if (bundle == null)
                throw new InvalidOperationException("Failed to load AssetBundle: " + normalizedBundleFile);

            loadedBundles.Add(bundle);
            GameObject[] prefabs = bundle.LoadAllAssets<GameObject>();
            if (prefabs.Length == 0)
                throw new InvalidOperationException("No prefab GameObject assets were loaded from AssetBundle: " + normalizedBundleFile);

            int meshFilterCount = 0;
            int skinnedMeshCount = 0;
            int rendererCount = 0;
            int materialSlotCount = 0;

            foreach (GameObject prefab in prefabs)
            {
                foreach (MeshFilter meshFilter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    meshFilterCount++;
                    if (meshFilter.sharedMesh == null)
                        issues.Add(prefab.name + "/" + GetTransformPath(meshFilter.transform) + " missing MeshFilter.sharedMesh");
                }

                foreach (SkinnedMeshRenderer skinnedMeshRenderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skinnedMeshCount++;
                    if (skinnedMeshRenderer.sharedMesh == null)
                        issues.Add(prefab.name + "/" + GetTransformPath(skinnedMeshRenderer.transform) + " missing SkinnedMeshRenderer.sharedMesh");
                }

                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    rendererCount++;
                    Material[] materials = renderer.sharedMaterials;
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        materialSlotCount++;
                        Material material = materials[materialIndex];
                        string owner = prefab.name + "/" + GetTransformPath(renderer.transform) + " material[" + materialIndex + "]";
                        if (material == null)
                        {
                            issues.Add(owner + " is missing Material");
                            continue;
                        }

                        Shader shader = material.shader;
                        if (shader == null)
                        {
                            issues.Add(owner + " is missing Shader");
                            continue;
                        }

                        if (string.Equals(shader.name, "Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase))
                            issues.Add(owner + " uses Hidden/InternalErrorShader");
                    }
                }
            }

            if (issues.Count > 0)
                throw new InvalidOperationException("Prefab bundle validation failed:\n" + string.Join("\n", issues));

            Debug.Log("Prefab bundle validation succeeded. Bundle=" + normalizedBundleFile +
                ", Prefabs=" + prefabs.Length +
                ", MeshFilters=" + meshFilterCount +
                ", SkinnedMeshes=" + skinnedMeshCount +
                ", Renderers=" + rendererCount +
                ", MaterialSlots=" + materialSlotCount +
                ", Dependencies=" + (loadedBundles.Count - 1));
        }
        finally
        {
            for (int index = loadedBundles.Count - 1; index >= 0; index--)
                loadedBundles[index].Unload(true);
        }
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

        List<ProjectGroupSpec> specs = CreateContentModuleStaticRuntimeSpecs(buildRoot).ToList();
        specs.AddRange(CreateContentModuleRuntimeSpecs(buildRoot));
        specs.AddRange(CreateGameAssetSpecs(buildRoot));
        specs.AddRange(CreateContentModuleRuntimeDependencySpecs(buildRoot));
        specs.AddRange(CreateGameAssetDependencySpecs(buildRoot));
        specs.AddRange(CreateContentModuleArtDependencySpecs(buildRoot, includeSourceAssets));
        if (includeSourceAssets)
        {
            specs.AddRange(CreateGameResourceSpecs());
        }

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

            AddPlanGroup(plan, spec, assetPaths, groupNames);
        }

        FillDependencySummary(plan);

        if (plan.groups.Count == 0)
            plan.errors.Add("No YooAsset collector groups were generated.");

        return plan;
    }

    private static void AddPlanGroup(
        YooAssetBuildPlan plan,
        ProjectGroupSpec spec,
        List<string> assetPaths,
        HashSet<string> groupNames)
    {
        string groupName = MakeUniqueGroupName(spec.groupName, groupNames);
        YooAssetGroupPlan group = new YooAssetGroupPlan
        {
            name = groupName,
            assetCount = assetPaths.Count,
            estimatedSourceBytes = assetPaths.Sum(GetFileSizeSafe),
            roots = spec.roots.ToList(),
            explicitAssets = spec.explicitAssets.ToList(),
            assets = assetPaths
        };

        group.collectors = CreateCollectorPlans(spec, groupName, assetPaths, plan);
        if (group.collectors.Count == 0)
            return;

        plan.groups.Add(group);
    }

    private static List<YooAssetCollectorPlan> CreateCollectorPlans(
        ProjectGroupSpec spec,
        string groupName,
        IReadOnlyCollection<string> assetPaths,
        YooAssetBuildPlan plan)
    {
        HashSet<string> assetSet = new HashSet<string>(assetPaths, StringComparer.OrdinalIgnoreCase);
        HashSet<string> collectorPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<YooAssetCollectorPlan> collectors = new List<YooAssetCollectorPlan>();

        for (int rootIndex = 0; rootIndex < spec.roots.Length; rootIndex++)
        {
            string root = NormalizeAssetPath(spec.roots[rootIndex]);
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (AssetDatabase.IsValidFolder(root))
            {
                List<string> rootAssets = assetPaths
                    .Where(assetPath => IsUnderRoot(assetPath, root))
                    .OrderBy(assetPath => assetPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (rootAssets.Count == 0)
                    continue;

                if (CanUseFolderCollector(root, spec, assetSet, plan))
                {
                    AddCollectorPlan(collectors, collectorPaths, spec, groupName, root, plan);
                    continue;
                }

                for (int assetIndex = 0; assetIndex < rootAssets.Count; assetIndex++)
                    AddCollectorPlan(collectors, collectorPaths, spec, groupName, rootAssets[assetIndex], plan);

                continue;
            }

            if (assetSet.Contains(root))
                AddCollectorPlan(collectors, collectorPaths, spec, groupName, root, plan);
        }

        for (int explicitIndex = 0; explicitIndex < spec.explicitAssets.Length; explicitIndex++)
        {
            string assetPath = NormalizeAssetPath(spec.explicitAssets[explicitIndex]);
            if (assetSet.Contains(assetPath))
                AddCollectorPlan(collectors, collectorPaths, spec, groupName, assetPath, plan);
        }

        return collectors
            .OrderBy(collector => collector.collectPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddCollectorPlan(
        List<YooAssetCollectorPlan> collectors,
        HashSet<string> collectorPaths,
        ProjectGroupSpec spec,
        string groupName,
        string collectPath,
        YooAssetBuildPlan plan)
    {
        string normalizedPath = NormalizeAssetPath(collectPath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !collectorPaths.Add(normalizedPath))
            return;

        string guid = AssetDatabase.AssetPathToGUID(normalizedPath);
        if (string.IsNullOrWhiteSpace(guid))
        {
            plan.errors.Add("Missing GUID for collector path: " + normalizedPath);
            return;
        }

        collectors.Add(new YooAssetCollectorPlan
        {
            collectPath = normalizedPath,
            collectorType = spec.collectorType,
            addressRuleName = spec.addressRuleName,
            packRuleName = spec.packRuleName,
            filterRuleName = spec.filterRuleName,
            assetTags = groupName + ";" + spec.assetClass.ToString().ToLowerInvariant(),
            userData = spec.assetClass.ToString().ToLowerInvariant()
        });
    }

    private static bool CanUseFolderCollector(
        string root,
        ProjectGroupSpec spec,
        HashSet<string> plannedAssets,
        YooAssetBuildPlan plan)
    {
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { root });
        for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
        {
            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[guidIndex]));
            if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath) || !File.Exists(assetPath))
                continue;

            if (!WouldCollectorFilterIncludeAsset(assetPath, spec.assetClass))
                continue;

            if (!plannedAssets.Contains(assetPath))
            {
                plan.warnings.Add("YooAsset folder collector downgraded to explicit assets because the folder contains filtered asset: " + assetPath);
                return false;
            }
        }

        return true;
    }

    private static bool WouldCollectorFilterIncludeAsset(string assetPath, CollectorAssetClass assetClass)
    {
        switch (assetClass)
        {
            case CollectorAssetClass.Main:
                return ProjectYooAssetCollectorRuleUtility.IsMainAsset(assetPath);
            case CollectorAssetClass.Depend:
                return ProjectYooAssetCollectorRuleUtility.IsDependencyAsset(assetPath);
            case CollectorAssetClass.Static:
                return ProjectYooAssetCollectorRuleUtility.IsStaticAsset(assetPath);
            case CollectorAssetClass.All:
                return true;
            default:
                return false;
        }
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

            for (int collectorIndex = 0; collectorIndex < groupPlan.collectors.Count; collectorIndex++)
            {
                YooAssetCollectorPlan collectorPlan = groupPlan.collectors[collectorIndex];
                string guid = AssetDatabase.AssetPathToGUID(collectorPlan.collectPath);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    plan.errors.Add("Missing GUID for collector path: " + collectorPlan.collectPath);
                    continue;
                }

                group.Collectors.Add(new BundleCollector
                {
                    CollectPath = collectorPlan.collectPath,
                    CollectorGUID = guid,
                    CollectorType = collectorPlan.collectorType,
                    AddressRuleName = collectorPlan.addressRuleName,
                    PackRuleName = collectorPlan.packRuleName,
                    FilterRuleName = collectorPlan.filterRuleName,
                    AssetTags = collectorPlan.assetTags,
                    UserData = collectorPlan.userData
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
        List<ProjectGroupSpec> specs = new List<ProjectGroupSpec>();
        specs.AddRange(CreateSharedRuntimeSpecs(gameAssetsRoot));

        if (!AssetDatabase.IsValidFolder(gameAssetsRoot))
            return specs;

        string[] excludeRoots = CreateGameAssetExcludeRoots(gameAssetsRoot);
        specs.AddRange(CreateStaticRuntimeSpecs(gameAssetsRoot));
        specs.AddRange(CreateCommonRuntimeSpecs(gameAssetsRoot));
        specs.AddRange(CreateWorldRuntimeSpecs(gameAssetsRoot));
        specs.AddRange(CreateLegacyMeadowSpecs(gameAssetsRoot));
        specs.AddRange(CreateSpecsFromPackageFolders("angrymesh.gameassets", gameAssetsRoot, excludeRoots, DefaultPackageSourceBytes));
        return specs;
    }

    private static IEnumerable<ProjectGroupSpec> CreateContentModuleStaticRuntimeSpecs(string buildRoot)
    {
        if (AssetDatabase.IsValidFolder(SharedRuntimeShaderRoot))
            yield return CreateStaticSpec("angrymesh.static.common.shaders", SharedRuntimeShaderRoot);
    }

    private static List<ProjectGroupSpec> CreateContentModuleRuntimeSpecs(string buildRoot)
    {
        List<ProjectGroupSpec> specs = new List<ProjectGroupSpec>();
        AddMainSpecIfValid(specs, "game.runtime.shared.stylizedpackcommon.asp.global.settings", SharedAspGlobalSettingsRoot);

        string sharedRoot = AssetPathCombine(MeadowRuntimeRoot, SharedDirectoryName);
        AddMainSpecIfValid(specs, "angrymesh.worlds.meadow.shared.configs", ResolveRuntimeMainCollectorRoot(AssetPathCombine(sharedRoot, "Configs")));
        AddMainSpecIfValid(specs, "angrymesh.worlds.meadow.shared.prefabs", AssetPathCombine(sharedRoot, "Prefabs"));

        string seasonsRoot = AssetPathCombine(MeadowRuntimeRoot, SeasonsDirectoryName);
        if (AssetDatabase.IsValidFolder(seasonsRoot))
        {
            foreach (string seasonFolder in AssetDatabase.GetSubFolders(seasonsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string seasonName = SanitizeSegment(Path.GetFileName(seasonFolder));
                foreach (string packageFolder in AssetDatabase.GetSubFolders(seasonFolder).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string packageName = SanitizeSegment(Path.GetFileName(packageFolder));
                    AddMainSpecIfValid(
                        specs,
                        "angrymesh.worlds.meadow.season." + seasonName + "." + packageName,
                        ResolveRuntimeMainCollectorRoot(packageFolder));
                }
            }
        }

        AddMainSpecIfValid(specs, "angrymesh.worlds.meadow.scenes.root", AssetPathCombine(MeadowRuntimeRoot, ScenesDirectoryName), ScenePackageSourceBytes);

        return specs;
    }

    private static string ResolveRuntimeMainCollectorRoot(string packageFolder)
    {
        string normalizedFolder = NormalizeAssetPath(packageFolder);
        if (!string.Equals(Path.GetFileName(normalizedFolder), "Configs", StringComparison.OrdinalIgnoreCase))
            return normalizedFolder;

        string urpPostProcessingRoot = AssetPathCombine(normalizedFolder, "Post Processing/URP");
        return AssetDatabase.IsValidFolder(urpPostProcessingRoot)
            ? urpPostProcessingRoot
            : normalizedFolder;
    }

    private static void AddMainSpecIfValid(
        List<ProjectGroupSpec> specs,
        string groupName,
        string root,
        long maxSourceBytes = DefaultPackageSourceBytes)
    {
        string normalizedRoot = NormalizeAssetPath(root);
        if (!AssetDatabase.IsValidFolder(normalizedRoot))
            return;

        specs.Add(new ProjectGroupSpec(
            groupName,
            new[] { normalizedRoot },
            Array.Empty<string>(),
            null,
            maxSourceBytes));
    }

    private static IEnumerable<ProjectGroupSpec> CreateContentModuleRuntimeDependencySpecs(string buildRoot)
    {
        if (AssetDatabase.IsValidFolder(QianxiaGeneratedRuntimeRoot))
            yield return CreateDependencySpec("game.dependencies.runtime.characters.qianxia.generated", QianxiaGeneratedRuntimeRoot);
    }

    private static IEnumerable<ProjectGroupSpec> CreateContentModuleArtDependencySpecs(
        string buildRoot,
        bool includeSourceAssets)
    {
        string gameContentRoot = ResolveGameContentRoot(buildRoot);
        if (!AssetDatabase.IsValidFolder(gameContentRoot))
            yield break;

        foreach (string artRoot in EnumerateContentModuleArtRoots(gameContentRoot))
        {
            string groupKey = GetContentModuleGroupKey(gameContentRoot, artRoot);
            foreach (string dependencyRoot in EnumerateDependencyFolders(artRoot))
            {
                if (!ShouldCreateStandaloneArtDependencyCollector(dependencyRoot))
                    continue;

                yield return CreateDependencySpec(
                    "game.dependencies." + groupKey + "." + SanitizeSegment(Path.GetFileName(dependencyRoot)),
                    dependencyRoot);
            }
        }
    }

    private static bool ShouldCreateStandaloneArtDependencyCollector(string dependencyRoot)
    {
        string normalizedRoot = NormalizeAssetPath(dependencyRoot).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedRoot))
            return false;

        return StandaloneArtDependencyRoots.Any(root =>
            string.Equals(normalizedRoot, NormalizeAssetPath(root).TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveGameContentRoot(string buildRoot)
    {
        string normalizedBuildRoot = NormalizeAssetPath(buildRoot).TrimEnd('/');
        if (string.Equals(Path.GetFileName(normalizedBuildRoot), GameContentDirectoryName, StringComparison.OrdinalIgnoreCase))
            return normalizedBuildRoot;

        return AssetPathCombine(normalizedBuildRoot, GameContentDirectoryName);
    }

    private static IEnumerable<string> EnumerateContentModuleRuntimeRoots(string gameContentRoot)
    {
        return EnumerateContentModuleSectionRoots(gameContentRoot, RuntimeDirectoryName);
    }

    private static IEnumerable<string> EnumerateContentModuleArtRoots(string gameContentRoot)
    {
        return EnumerateContentModuleSectionRoots(gameContentRoot, ArtDirectoryName);
    }

    private static IEnumerable<string> EnumerateContentModuleSectionRoots(string gameContentRoot, string sectionName)
    {
        string normalizedRoot = NormalizeAssetPath(gameContentRoot);
        if (!AssetDatabase.IsValidFolder(normalizedRoot))
            yield break;

        Stack<string> pending = new Stack<string>();
        pending.Push(normalizedRoot);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string folderName = Path.GetFileName(current);
            if (string.Equals(folderName, sectionName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield return current;
                continue;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(current)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (int index = 0; index < subFolders.Length; index++)
                pending.Push(subFolders[index]);
        }
    }

    private static IEnumerable<string> EnumerateStaticRuntimeFolders(string runtimeRoot)
    {
        string normalizedRoot = NormalizeAssetPath(runtimeRoot);
        if (!AssetDatabase.IsValidFolder(normalizedRoot))
            yield break;

        Stack<string> pending = new Stack<string>();
        pending.Push(normalizedRoot);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string folderName = Path.GetFileName(current);
            if (IsStaticRuntimeFolderName(folderName) &&
                !string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield return current;
                continue;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(current)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (int index = 0; index < subFolders.Length; index++)
                pending.Push(subFolders[index]);
        }
    }

    private static bool IsStaticRuntimeFolderName(string folderName)
    {
        return string.Equals(folderName, "Shaders", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "ShaderVariants", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "Fonts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "Functions", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetContentModuleGroupKey(string gameContentRoot, string runtimeRoot)
    {
        string modulePath = NormalizeAssetPath(Path.GetDirectoryName(NormalizeAssetPath(runtimeRoot)));
        string normalizedRoot = NormalizeAssetPath(gameContentRoot).TrimEnd('/');
        string relativePath = modulePath;
        if (relativePath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath.Substring(normalizedRoot.Length + 1);

        return SanitizeSegment(relativePath.Replace('/', '.'));
    }

    private static string ResolveGameAssetsRoot(string buildRoot)
    {
        string normalizedBuildRoot = NormalizeAssetPath(buildRoot).TrimEnd('/');
        if (string.Equals(Path.GetFileName(normalizedBuildRoot), LegacyGameAssetsDirectoryName, StringComparison.OrdinalIgnoreCase))
            return normalizedBuildRoot;

        return AssetPathCombine(normalizedBuildRoot, LegacyGameAssetsDirectoryName);
    }

    private static string[] CreateGameAssetExcludeRoots(string gameAssetsRoot)
    {
        List<string> roots = new List<string>
        {
            AssetPathCombine(gameAssetsRoot, CommonDirectoryName),
            AssetPathCombine(gameAssetsRoot, WorldsDirectoryName),
            AssetPathCombine(gameAssetsRoot, CommonFontsRoot),
            AssetPathCombine(gameAssetsRoot, CommonFunctionsRoot),
            AssetPathCombine(gameAssetsRoot, CommonShaderRoot),
            LegacyMeadowEnvironmentPrefabRoot,
            LegacyMeadowTerrainDetailsPrefabRoot,
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
        if (AssetDatabase.IsValidFolder(LegacyResourceCheckRoot))
        {
            return EnumerateDependencyFolders(LegacyResourceCheckRoot)
                .Where(path => !IsUnderRoot(path, SharedTextureRoot))
                .Select(path => CreateDependencySpec("angrymesh.dependencies." + SanitizeAssetPath(path), path))
                .ToList();
        }

        return new List<ProjectGroupSpec>();
    }

    private static IEnumerable<ProjectGroupSpec> CreateGameAssetDependencySpecs(string buildRoot)
    {
        string gameAssetsRoot = ResolveGameAssetsRoot(buildRoot);
        if (!AssetDatabase.IsValidFolder(gameAssetsRoot))
            yield break;

        string[] staticRoots = CreateGameAssetStaticRoots(gameAssetsRoot)
            .Where(AssetDatabase.IsValidFolder)
            .ToArray();

        foreach (string dependencyRoot in EnumerateAssetParentFolders(
                     gameAssetsRoot,
                     CollectorAssetClass.Depend,
                     staticRoots))
        {
            yield return CreateDependencySpec("angrymesh.dependencies." + SanitizeAssetPath(dependencyRoot), dependencyRoot);
        }
    }

    private static IEnumerable<ProjectGroupSpec> CreateStaticRuntimeSpecs(string gameAssetsRoot)
    {
        string sharedShaderRoot = AssetPathCombine(gameAssetsRoot, CommonShaderRoot);
        if (AssetDatabase.IsValidFolder(sharedShaderRoot))
            yield return CreateStaticSpec("angrymesh.static.common.shaders", sharedShaderRoot);

        string commonFontsRoot = AssetPathCombine(gameAssetsRoot, CommonFontsRoot);
        if (AssetDatabase.IsValidFolder(commonFontsRoot))
            yield return CreateStaticSpec("angrymesh.static.common.fonts", commonFontsRoot);

        string commonFunctionsRoot = AssetPathCombine(gameAssetsRoot, CommonFunctionsRoot);
        if (AssetDatabase.IsValidFolder(commonFunctionsRoot))
            yield return CreateStaticSpec("angrymesh.static.common.functions", commonFunctionsRoot);

        string worldsRoot = AssetPathCombine(gameAssetsRoot, WorldsDirectoryName);
        if (!AssetDatabase.IsValidFolder(worldsRoot))
            yield break;

        foreach (string worldFolder in AssetDatabase.GetSubFolders(worldsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string worldName = SanitizeSegment(Path.GetFileName(worldFolder));
            string sharedMaterialsRoot = AssetPathCombine(worldFolder, SharedDirectoryName + "/" + MaterialsDirectoryName);
            if (AssetDatabase.IsValidFolder(sharedMaterialsRoot))
                yield return CreateStaticSpec("angrymesh.static.worlds." + worldName + ".shared.materials", sharedMaterialsRoot);
        }
    }

    private static IEnumerable<ProjectGroupSpec> CreateSharedRuntimeSpecs(string gameAssetsRoot)
    {
        if (AssetDatabase.IsValidFolder(SharedTextureRoot))
        {
            yield return CreateDependencySpec("angrymesh.dependencies.shared.textures", SharedTextureRoot);
        }
    }

    private static IEnumerable<ProjectGroupSpec> CreateCommonRuntimeSpecs(string gameAssetsRoot)
    {
        string commonRoot = AssetPathCombine(gameAssetsRoot, CommonDirectoryName);
        if (!AssetDatabase.IsValidFolder(commonRoot))
            yield break;

        string[] commonExcludeRoots = CreateCommonStaticRoots(gameAssetsRoot)
            .Where(AssetDatabase.IsValidFolder)
            .ToArray();

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
                string sharedMaterialsRoot = AssetPathCombine(sharedRoot, MaterialsDirectoryName);
                string[] sharedExcludeRoots = AssetDatabase.IsValidFolder(sharedMaterialsRoot)
                    ? new[] { sharedMaterialsRoot }
                    : Array.Empty<string>();

                foreach (ProjectGroupSpec spec in CreateSpecsFromPackageFolders(
                             groupPrefix + ".shared",
                             sharedRoot,
                             sharedExcludeRoots,
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

            string retiredChunksRoot = AssetPathCombine(worldFolder, "Chunks");
            if (AssetDatabase.IsValidFolder(retiredChunksRoot))
                worldExcludeRoots.Add(retiredChunksRoot);

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
                     LegacyMeadowEnvironmentPrefabRoot,
                     DefaultPackageSourceBytes))
        {
            yield return spec;
        }

        foreach (ProjectGroupSpec spec in CreateSpecsForOptionalRoot(
                     "angrymesh.meadow.terrain.details",
                     LegacyMeadowTerrainDetailsPrefabRoot,
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
        long maxSourceBytes,
        ECollectorType collectorType = ECollectorType.MainAssetCollector,
        string filterRuleName = null,
        string packRuleName = null,
        CollectorAssetClass assetClass = CollectorAssetClass.Main)
    {
        string normalizedRoot = NormalizeAssetPath(root);
        if (!AssetDatabase.IsValidFolder(normalizedRoot))
        {
            yield return new ProjectGroupSpec(
                groupPrefix,
                new[] { normalizedRoot },
                excludeRoots,
                null,
                maxSourceBytes,
                collectorType,
                filterRuleName,
                packRuleName,
                assetClass);
            yield break;
        }

        string[] subFolders = AssetDatabase.GetSubFolders(normalizedRoot)
            .Where(path => !IsUnderAnyRoot(path, excludeRoots))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        List<string> directAssets = CollectDirectAssetPaths(normalizedRoot, excludeRoots, assetClass);
        if (directAssets.Count > 0 && subFolders.Length == 0)
        {
            yield return new ProjectGroupSpec(
                groupPrefix + ".root",
                new[] { normalizedRoot },
                excludeRoots,
                null,
                maxSourceBytes,
                collectorType,
                filterRuleName,
                packRuleName,
                assetClass);
        }
        else if (directAssets.Count > 0)
        {
            yield return new ProjectGroupSpec(
                groupPrefix + ".root",
                Array.Empty<string>(),
                excludeRoots,
                directAssets.ToArray(),
                maxSourceBytes,
                collectorType,
                filterRuleName,
                packRuleName,
                assetClass);
        }

        if (subFolders.Length == 0 && directAssets.Count == 0 && (excludeRoots == null || excludeRoots.Length == 0))
        {
            yield return new ProjectGroupSpec(
                groupPrefix + "." + SanitizeSegment(Path.GetFileName(normalizedRoot)),
                new[] { normalizedRoot },
                excludeRoots,
                null,
                maxSourceBytes,
                collectorType,
                filterRuleName,
                packRuleName,
                assetClass);
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
                maxSourceBytes,
                collectorType,
                filterRuleName,
                packRuleName,
                assetClass);
        }
    }

    private static List<string> CollectDirectAssetPaths(
        string root,
        IReadOnlyList<string> excludeRoots,
        CollectorAssetClass assetClass)
    {
        List<string> result = new List<string>();
        if (!Directory.Exists(root))
            return result;

        foreach (string filePath in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            string assetPath = NormalizeAssetPath(filePath);
            if (ShouldIncludeAssetForClass(assetPath, excludeRoots, assetClass))
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
            if (ShouldIncludeAssetForClass(explicitAsset, spec.excludeRoots, spec.assetClass))
                paths.Add(explicitAsset);
        }

        for (int rootIndex = 0; rootIndex < spec.roots.Length; rootIndex++)
        {
            string root = NormalizeAssetPath(spec.roots[rootIndex]);
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (!AssetDatabase.IsValidFolder(root))
            {
                if (ShouldIncludeAssetForClass(root, spec.excludeRoots, spec.assetClass))
                    paths.Add(root);
                else if (!File.Exists(root))
                    plan.warnings.Add("YooAsset root folder does not exist: " + root);

                continue;
            }

            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { root });
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[guidIndex]));
                if (ShouldIncludeAssetForClass(assetPath, spec.excludeRoots, spec.assetClass))
                    paths.Add(assetPath);
            }
        }

        return paths;
    }

    private static bool ShouldIncludeAssetForClass(
        string assetPath,
        IReadOnlyList<string> excludeRoots,
        CollectorAssetClass assetClass)
    {
        if (!ShouldIncludeAsset(assetPath, excludeRoots))
            return false;

        switch (assetClass)
        {
            case CollectorAssetClass.Main:
                return ProjectYooAssetCollectorRuleUtility.IsMainAsset(assetPath);
            case CollectorAssetClass.Depend:
                return ProjectYooAssetCollectorRuleUtility.IsDependencyAsset(assetPath);
            case CollectorAssetClass.Static:
                return ProjectYooAssetCollectorRuleUtility.IsStaticAsset(assetPath);
            case CollectorAssetClass.All:
                return true;
            default:
                return false;
        }
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

    private static IEnumerable<string> SplitCommandLineList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        string[] parts = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
        }
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        Stack<string> names = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
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

    private static bool IsUnderSourcesFolder(string assetPath)
    {
        return ContainsPathSegment(assetPath, "Sources");
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

    private static string SanitizeAssetPath(string assetPath)
    {
        string normalized = NormalizeAssetPath(assetPath);
        if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("Assets/".Length);

        string result = SanitizeSegment(normalized.Replace('/', '.'));
        return string.IsNullOrWhiteSpace(result) ? "assets" : result;
    }

    private static string[] CreateCommonStaticRoots(string gameAssetsRoot)
    {
        return new[]
        {
            AssetPathCombine(gameAssetsRoot, CommonFontsRoot),
            AssetPathCombine(gameAssetsRoot, CommonFunctionsRoot),
            AssetPathCombine(gameAssetsRoot, CommonShaderRoot)
        };
    }

    private static IEnumerable<string> CreateGameAssetStaticRoots(string gameAssetsRoot)
    {
        foreach (string commonRoot in CreateCommonStaticRoots(gameAssetsRoot))
            yield return commonRoot;

        string worldsRoot = AssetPathCombine(gameAssetsRoot, WorldsDirectoryName);
        if (!AssetDatabase.IsValidFolder(worldsRoot))
            yield break;

        foreach (string worldFolder in AssetDatabase.GetSubFolders(worldsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            yield return AssetPathCombine(worldFolder, SharedDirectoryName + "/" + MaterialsDirectoryName);
    }

    private static ProjectGroupSpec CreateDependencySpec(string groupName, string root)
    {
        return new ProjectGroupSpec(
            groupName,
            new[] { root },
            Array.Empty<string>(),
            null,
            DefaultPackageSourceBytes,
            ECollectorType.DependAssetCollector,
            nameof(ProjectYooAssetDependencyAssetFilter),
            nameof(ProjectYooAssetPackDependencyBucket),
            CollectorAssetClass.Depend);
    }

    private static ProjectGroupSpec CreateStaticSpec(
        string groupName,
        string root,
        string filterRuleName = null,
        string packRuleName = null)
    {
        return new ProjectGroupSpec(
            groupName,
            new[] { root },
            Array.Empty<string>(),
            null,
            DefaultPackageSourceBytes,
            ECollectorType.StaticAssetCollector,
            filterRuleName ?? nameof(ProjectYooAssetStaticAssetFilter),
            packRuleName ?? nameof(PackGroup),
            CollectorAssetClass.Static);
    }

    private static IEnumerable<string> EnumerateDependencyFolders(string root)
    {
        string normalizedRoot = NormalizeAssetPath(root);
        if (!AssetDatabase.IsValidFolder(normalizedRoot))
            yield break;

        Stack<string> pending = new Stack<string>();
        pending.Push(normalizedRoot);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string folderName = Path.GetFileName(current);
            if (ProjectYooAssetCollectorRuleUtility.IsDependencyFolderName(folderName) &&
                !string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield return current;
                continue;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(current)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (int index = 0; index < subFolders.Length; index++)
                pending.Push(subFolders[index]);
        }
    }

    private static IEnumerable<string> EnumerateAssetParentFolders(
        string root,
        CollectorAssetClass assetClass,
        IReadOnlyList<string> excludeRoots)
    {
        string normalizedRoot = NormalizeAssetPath(root);
        if (!AssetDatabase.IsValidFolder(normalizedRoot))
            yield break;

        HashSet<string> parentFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { normalizedRoot });
        for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
        {
            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[guidIndex]));
            if (!ShouldIncludeAssetForClass(assetPath, excludeRoots, assetClass))
                continue;

            string parentFolder = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
            if (!string.IsNullOrWhiteSpace(parentFolder) && AssetDatabase.IsValidFolder(parentFolder))
                parentFolders.Add(parentFolder);
        }

        foreach (string parentFolder in parentFolders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            yield return parentFolder;
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
        public readonly ECollectorType collectorType;
        public readonly string addressRuleName;
        public readonly string packRuleName;
        public readonly string filterRuleName;
        public readonly CollectorAssetClass assetClass;

        public ProjectGroupSpec(
            string groupName,
            string[] roots,
            string[] excludeRoots = null,
            string[] explicitAssets = null,
            long maxSourceBytes = DefaultPackageSourceBytes,
            ECollectorType collectorType = ECollectorType.MainAssetCollector,
            string filterRuleName = null,
            string packRuleName = null,
            CollectorAssetClass assetClass = CollectorAssetClass.Main)
        {
            this.groupName = groupName;
            this.roots = roots ?? Array.Empty<string>();
            this.excludeRoots = excludeRoots ?? Array.Empty<string>();
            this.explicitAssets = explicitAssets ?? Array.Empty<string>();
            this.maxSourceBytes = maxSourceBytes;
            this.collectorType = collectorType;
            this.addressRuleName = nameof(AddressDisable);
            this.packRuleName = packRuleName ?? nameof(PackDirectory);
            this.filterRuleName = filterRuleName ?? nameof(ProjectYooAssetMainAssetFilter);
            this.assetClass = assetClass;
        }
    }

    private enum CollectorAssetClass
    {
        Main,
        Depend,
        Static,
        All
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
        public List<YooAssetCollectorPlan> collectors = new List<YooAssetCollectorPlan>();
        public List<string> assets = new List<string>();
        public List<string> sharedDependencies = new List<string>();
    }

    [Serializable]
    private sealed class YooAssetCollectorPlan
    {
        public string collectPath = string.Empty;
        public ECollectorType collectorType = ECollectorType.MainAssetCollector;
        public string addressRuleName = string.Empty;
        public string packRuleName = string.Empty;
        public string filterRuleName = string.Empty;
        public string assetTags = string.Empty;
        public string userData = string.Empty;
    }
}

public sealed class ProjectYooAssetMainAssetFilter : IAssetFilterRule
{
    public string FindAssetType => EAssetFilterType.All.ToString();

    public bool IsCollectAsset(AssetFilterRuleData data)
    {
        return ProjectYooAssetCollectorRuleUtility.IsMainAsset(data.AssetPath);
    }
}

public sealed class ProjectYooAssetDependencyAssetFilter : IAssetFilterRule
{
    public string FindAssetType => EAssetFilterType.All.ToString();

    public bool IsCollectAsset(AssetFilterRuleData data)
    {
        return ProjectYooAssetCollectorRuleUtility.IsDependencyAsset(data.AssetPath);
    }
}

public sealed class ProjectYooAssetStaticAssetFilter : IAssetFilterRule
{
    public string FindAssetType => EAssetFilterType.All.ToString();

    public bool IsCollectAsset(AssetFilterRuleData data)
    {
        return ProjectYooAssetCollectorRuleUtility.IsStaticAsset(data.AssetPath);
    }
}

public sealed class ProjectYooAssetPackDependencyBucket : IBundlePackRule
{
    public BundlePackRuleResult GetPackRuleResult(BundlePackRuleData data)
    {
        string bucket = ProjectYooAssetCollectorRuleUtility.GetDependencyBucket(data.AssetPath);
        return new BundlePackRuleResult(
            data.GroupName + "." + bucket,
            DefaultBundlePackRule.AssetBundleFileExtension);
    }
}

public static class ProjectYooAssetCollectorRuleUtility
{
    private static readonly HashSet<string> MainAssetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".asset",
        ".bytes",
        ".inputactions",
        ".json",
        ".prefab",
        ".spriteatlas",
        ".txt",
        ".unity"
    };

    private static readonly HashSet<string> DependencyExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".anim",
        ".controller",
        ".exr",
        ".fbx",
        ".hdr",
        ".jpg",
        ".jpeg",
        ".mat",
        ".mask",
        ".mesh",
        ".mp3",
        ".ogg",
        ".png",
        ".psb",
        ".psd",
        ".tga",
        ".terrainlayer",
        ".tif",
        ".tiff",
        ".wav"
    };

    private static readonly HashSet<string> StaticExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".asset",
        ".mat",
        ".otf",
        ".shader",
        ".shadervariants",
        ".ttf"
    };

    private static readonly HashSet<string> DependencyFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Animations",
        "Audio",
        "Materials",
        "Meshes",
        "Meshs",
        "Models",
        "SourceAnimations",
        "Terrain Data",
        "Terrain Layers",
        "Textures"
    };

    public static bool IsMainAsset(string assetPath)
    {
        string normalizedPath = Normalize(assetPath);
        if (normalizedPath.IndexOf("/Configs/Post Processing/Standard/", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        if (IsStaticAsset(normalizedPath) || IsDependencyAsset(normalizedPath))
            return false;

        return MainAssetExtensions.Contains(Path.GetExtension(normalizedPath));
    }

    public static bool IsDependencyAsset(string assetPath)
    {
        string normalizedPath = Normalize(assetPath);
        string extension = Path.GetExtension(normalizedPath);
        if (DependencyExtensions.Contains(extension))
            return true;

        return extension.Equals(".asset", StringComparison.OrdinalIgnoreCase) &&
               (normalizedPath.IndexOf("/Sources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf("/SourceAnimations/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf("/Terrain Data/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedPath.IndexOf("/Terrain Layers/", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static bool IsStaticAsset(string assetPath)
    {
        string normalizedPath = Normalize(assetPath);
        string extension = Path.GetExtension(normalizedPath);
        if (!StaticExtensions.Contains(extension))
            return false;

        return extension.Equals(".shader", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".shadervariants", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.IndexOf("/Common/Fonts/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalizedPath.IndexOf("/Common/Functions/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalizedPath.IndexOf("/Runtime/Fonts/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalizedPath.IndexOf("/Runtime/Functions/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalizedPath.IndexOf("/Shared/Materials/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsDependencyFolderName(string folderName)
    {
        return DependencyFolderNames.Contains(folderName ?? string.Empty);
    }

    public static string GetDependencyBucket(string assetPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(Normalize(assetPath));
        if (string.IsNullOrWhiteSpace(fileName))
            return "other";

        char first = char.ToLowerInvariant(fileName[0]);
        if (first >= '0' && first <= '9')
            return "num";
        if (first >= 'a' && first <= 'z')
            return first.ToString();

        return "other";
    }

    private static string Normalize(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');
    }
}
