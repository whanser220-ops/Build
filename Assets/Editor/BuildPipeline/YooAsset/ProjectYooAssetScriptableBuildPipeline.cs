using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Injector;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.Tasks;
using UnityEngine;
using Unity6.Ci;
using YooAsset.Editor;
using SbpPreferences = UnityEditor.Build.Pipeline.Utilities.ScriptableBuildPipeline;

public sealed class ProjectYooAssetScriptableBuildPipeline
{
    public static ProjectSbpBundleLayoutSnapshot LastLayoutSnapshot { get; private set; }

    public BuildResult Run(ScriptableBuildParameters buildParameters, bool enableLog)
    {
        if (buildParameters == null)
            throw new ArgumentNullException(nameof(buildParameters));

        bool previousSlimWriteResults = SbpPreferences.slimWriteResults;
        LastLayoutSnapshot = null;

        try
        {
            SbpPreferences.slimWriteResults = false;

            List<YooAsset.Editor.IBuildTask> pipeline = new List<YooAsset.Editor.IBuildTask>
            {
                new TaskPrepare_SBP(),
                new TaskGetBuildMap_SBP(),
                new ProjectYooAssetTaskBuildingSbp(),
                new TaskVerifyBuildResult_SBP(),
                new TaskEncryption_SBP(),
                new TaskUpdateBundleInfo_SBP(),
                new TaskCreateManifest_SBP(),
                new TaskCreateReport_SBP(),
                new TaskCreatePackage_SBP(),
                new TaskCopyBundledFiles_SBP(),
                new TaskCreateCatalog_SBP()
            };

            BundleBuilder builder = new BundleBuilder();
            return builder.Run(buildParameters, pipeline, enableLog);
        }
        finally
        {
            SbpPreferences.slimWriteResults = previousSlimWriteResults;
        }
    }

    internal static void SetLastLayoutSnapshot(ProjectSbpBundleLayoutSnapshot snapshot)
    {
        LastLayoutSnapshot = snapshot;
    }
}

public sealed class ProjectYooAssetTaskBuildingSbp : YooAsset.Editor.IBuildTask
{
    public void Run(YooAsset.Editor.BuildContext context)
    {
        BuildMapContext buildMapContext = context.GetContextObject<BuildMapContext>();
        BuildParametersContext buildParametersContext = context.GetContextObject<BuildParametersContext>();
        ScriptableBuildParameters scriptableBuildParameters = buildParametersContext.Parameters as ScriptableBuildParameters;
        if (scriptableBuildParameters == null)
            throw new InvalidOperationException("Project YooAsset SBP task requires ScriptableBuildParameters.");

        AssetBundleBuild[] bundleBuilds = buildMapContext.GetPipelineBuilds(scriptableBuildParameters.ReplaceAssetPathWithAddress);
        BundleBuildContent buildContent = new BundleBuildContent(bundleBuilds);

        string builtinShadersBundleName = scriptableBuildParameters.BuiltinShadersBundleName;
        string monoScriptsBundleName = scriptableBuildParameters.MonoScriptsBundleName;
        IList<UnityEditor.Build.Pipeline.Interfaces.IBuildTask> sbpTasks = CreateSbpTasksWithLayoutCapture(
            builtinShadersBundleName,
            monoScriptsBundleName);

        ProjectSbpBundleLayoutCaptureState captureState = new ProjectSbpBundleLayoutCaptureState();
        IBundleBuildResults buildResults;
        IBundleBuildParameters buildParameters = scriptableBuildParameters.GetBundleBuildParameters();
        CiBuildProgressReporter.ReportStage("yooasset-sbp-content", "YooAsset ContentPipeline.BuildAssetBundles", 68, "Building AssetBundles with Scriptable Build Pipeline.");
        ReturnCode exitCode = ContentPipeline.BuildAssetBundles(
            buildParameters,
            buildContent,
            out buildResults,
            sbpTasks,
            captureState);

        if (exitCode < 0)
        {
            throw new InvalidOperationException($"UnityEngine build failed. ReturnCode: {exitCode}.");
        }

        if (!string.IsNullOrEmpty(builtinShadersBundleName) &&
            buildResults.BundleInfos.ContainsKey(builtinShadersBundleName))
        {
            buildMapContext.CreateEmptyBundleInfo(builtinShadersBundleName);
        }

        if (!string.IsNullOrEmpty(monoScriptsBundleName) &&
            buildResults.BundleInfos.ContainsKey(monoScriptsBundleName))
        {
            buildMapContext.CreateEmptyBundleInfo(monoScriptsBundleName);
        }

        ProjectYooAssetScriptableBuildPipeline.SetLastLayoutSnapshot(captureState.Snapshot);
        CiBuildProgressReporter.ReportStage("yooasset-sbp-layout", "YooAsset bundle layout captured", 72, "Captured YooAsset bundle layout.");

        TaskBuilding_SBP.BuildResultContext buildResultContext = new TaskBuilding_SBP.BuildResultContext
        {
            Results = buildResults,
            BuiltinShadersBundleName = builtinShadersBundleName,
            MonoScriptsBundleName = monoScriptsBundleName
        };
        context.SetContextObject(buildResultContext);

        Debug.Log("UnityEngine build succeeded with ANGRY MESH bundle layout capture.");
    }

    private static IList<UnityEditor.Build.Pipeline.Interfaces.IBuildTask> CreateSbpTasksWithLayoutCapture(
        string builtinShadersBundleName,
        string monoScriptsBundleName)
    {
        List<UnityEditor.Build.Pipeline.Interfaces.IBuildTask> tasks = SBPBuildTasks
            .Create(builtinShadersBundleName, monoScriptsBundleName)
            .ToList();

        int archiveTaskIndex = tasks.FindIndex(task => task is ArchiveAndCompressBundles);
        if (archiveTaskIndex < 0)
            throw new InvalidOperationException("Could not find SBP ArchiveAndCompressBundles task.");

        tasks.Insert(archiveTaskIndex, new ProjectSbpBundleLayoutCaptureTask());
        return tasks;
    }
}

public interface IProjectSbpBundleLayoutCaptureState : IContextObject
{
    ProjectSbpBundleLayoutSnapshot Snapshot { get; set; }
}

public sealed class ProjectSbpBundleLayoutCaptureState : IProjectSbpBundleLayoutCaptureState
{
    public ProjectSbpBundleLayoutSnapshot Snapshot { get; set; } = new ProjectSbpBundleLayoutSnapshot();
}

public sealed class ProjectSbpBundleLayoutCaptureTask : UnityEditor.Build.Pipeline.Interfaces.IBuildTask
{
    public int Version => 1;

#pragma warning disable 649
    [InjectContext(ContextUsage.In)]
    private IBundleWriteData _writeData;

    [InjectContext(ContextUsage.In)]
    private IBuildResults _results;

    [InjectContext]
    private IProjectSbpBundleLayoutCaptureState _captureState;
#pragma warning restore 649

    public ReturnCode Run()
    {
        if (_captureState == null)
            return ReturnCode.Success;

        _captureState.Snapshot = ProjectSbpBundleLayoutSnapshot.Create(_writeData, _results);
        return ReturnCode.Success;
    }
}

[Serializable]
public sealed class ProjectSbpBundleLayoutSnapshot
{
    public List<ProjectSbpBundleLayoutBundle> bundles = new List<ProjectSbpBundleLayoutBundle>();
    public List<ProjectSbpBundleLayoutAssetCopy> assetCopies = new List<ProjectSbpBundleLayoutAssetCopy>();

    public static ProjectSbpBundleLayoutSnapshot Create(IBundleWriteData writeData, IBuildResults results)
    {
        ProjectSbpBundleLayoutSnapshot snapshot = new ProjectSbpBundleLayoutSnapshot();
        if (writeData == null || results == null)
            return snapshot;

        Dictionary<string, ProjectSbpBundleLayoutBundle> bundleByName =
            new Dictionary<string, ProjectSbpBundleLayoutBundle>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ProjectSbpBundleLayoutAssetCopy> copyByAssetAndBundle =
            new Dictionary<string, ProjectSbpBundleLayoutAssetCopy>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> fileToAssetGuids = BuildFileToAssetGuidLookup(writeData);

        foreach (KeyValuePair<string, WriteResult> pair in results.WriteResults)
        {
            string internalName = pair.Key;
            if (!writeData.FileToBundle.TryGetValue(internalName, out string bundleName) ||
                string.IsNullOrWhiteSpace(bundleName))
            {
                continue;
            }

            ProjectSbpBundleLayoutBundle bundle = GetOrCreateBundle(bundleByName, bundleName);
            WriteResult writeResult = pair.Value;

            foreach (ResourceFile resourceFile in writeResult.resourceFiles)
            {
                long resourceBytes = GetFileSize(resourceFile.fileName);
                bundle.uncompressedSizeBytes += resourceBytes;

                if (!resourceFile.serializedFile)
                {
                    AddResourceFileBytesToAssets(
                        copyByAssetAndBundle,
                        fileToAssetGuids,
                        internalName,
                        resourceFile.fileAlias,
                        bundleName,
                        resourceBytes);
                }
            }

            foreach (ObjectSerializedInfo serializedObject in writeResult.serializedObjects)
            {
                GUID assetGuid = serializedObject.serializedObject.guid;
                if (assetGuid.Empty())
                    continue;

                long objectBytes =
                    (long)serializedObject.header.size +
                    (long)serializedObject.rawData.size;
                AddAssetCopyBytes(copyByAssetAndBundle, assetGuid.ToString(), bundleName, objectBytes);
            }
        }

        snapshot.bundles = bundleByName.Values
            .OrderBy(bundle => bundle.bundleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        snapshot.assetCopies = copyByAssetAndBundle.Values
            .OrderBy(copy => copy.assetGuid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(copy => copy.bundleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return snapshot;
    }

    public long GetBundleUncompressedSize(string bundleName)
    {
        ProjectSbpBundleLayoutBundle bundle = bundles.FirstOrDefault(
            item => string.Equals(item.bundleName, bundleName, StringComparison.OrdinalIgnoreCase));
        return bundle != null ? bundle.uncompressedSizeBytes : 0L;
    }

    public long GetAssetCopySize(string assetGuid, string bundleName)
    {
        ProjectSbpBundleLayoutAssetCopy copy = assetCopies.FirstOrDefault(item =>
            string.Equals(item.assetGuid, assetGuid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.bundleName, bundleName, StringComparison.OrdinalIgnoreCase));
        return copy != null ? copy.sizeBytes : 0L;
    }

    private static Dictionary<string, List<string>> BuildFileToAssetGuidLookup(IBundleWriteData writeData)
    {
        Dictionary<string, List<string>> fileToAssetGuids =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<GUID, List<string>> pair in writeData.AssetToFiles)
        {
            string assetGuid = pair.Key.ToString();
            foreach (string fileName in pair.Value)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                if (!fileToAssetGuids.TryGetValue(fileName, out List<string> assetGuids))
                {
                    assetGuids = new List<string>();
                    fileToAssetGuids.Add(fileName, assetGuids);
                }

                if (!assetGuids.Contains(assetGuid, StringComparer.OrdinalIgnoreCase))
                    assetGuids.Add(assetGuid);
            }
        }

        return fileToAssetGuids;
    }

    private static ProjectSbpBundleLayoutBundle GetOrCreateBundle(
        Dictionary<string, ProjectSbpBundleLayoutBundle> bundleByName,
        string bundleName)
    {
        if (!bundleByName.TryGetValue(bundleName, out ProjectSbpBundleLayoutBundle bundle))
        {
            bundle = new ProjectSbpBundleLayoutBundle
            {
                bundleName = bundleName,
                uncompressedSizeBytes = 0L
            };
            bundleByName.Add(bundleName, bundle);
        }

        return bundle;
    }

    private static void AddResourceFileBytesToAssets(
        Dictionary<string, ProjectSbpBundleLayoutAssetCopy> copyByAssetAndBundle,
        Dictionary<string, List<string>> fileToAssetGuids,
        string internalName,
        string fileAlias,
        string bundleName,
        long resourceBytes)
    {
        if (resourceBytes <= 0L)
            return;

        if (!fileToAssetGuids.TryGetValue(internalName, out List<string> assetGuids) &&
            !string.IsNullOrWhiteSpace(fileAlias))
        {
            fileToAssetGuids.TryGetValue(fileAlias, out assetGuids);
        }

        if (assetGuids == null)
            return;

        foreach (string assetGuid in assetGuids)
            AddAssetCopyBytes(copyByAssetAndBundle, assetGuid, bundleName, resourceBytes);
    }

    private static void AddAssetCopyBytes(
        Dictionary<string, ProjectSbpBundleLayoutAssetCopy> copyByAssetAndBundle,
        string assetGuid,
        string bundleName,
        long bytes)
    {
        if (string.IsNullOrWhiteSpace(assetGuid) || string.IsNullOrWhiteSpace(bundleName) || bytes <= 0L)
            return;

        string key = assetGuid + "|" + bundleName;
        if (!copyByAssetAndBundle.TryGetValue(key, out ProjectSbpBundleLayoutAssetCopy copy))
        {
            copy = new ProjectSbpBundleLayoutAssetCopy
            {
                assetGuid = assetGuid,
                bundleName = bundleName,
                sizeBytes = 0L
            };
            copyByAssetAndBundle.Add(key, copy);
        }

        copy.sizeBytes += bytes;
    }

    private static long GetFileSize(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return 0L;

        try
        {
            FileInfo fileInfo = new FileInfo(filePath);
            return fileInfo.Exists ? fileInfo.Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }
}

[Serializable]
public sealed class ProjectSbpBundleLayoutBundle
{
    public string bundleName = string.Empty;
    public long uncompressedSizeBytes;
}

[Serializable]
public sealed class ProjectSbpBundleLayoutAssetCopy
{
    public string assetGuid = string.Empty;
    public string bundleName = string.Empty;
    public long sizeBytes;
}
