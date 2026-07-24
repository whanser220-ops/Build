using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity6.Ci;
using UnityEditor.Build;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Injector;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEngine;
using UnityEngine.Build.Pipeline;
using SbpPreferences = UnityEditor.Build.Pipeline.Utilities.ScriptableBuildPipeline;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace UnityEditor.Build.Pipeline.Tasks
{
#if UNITY_2018_3_OR_NEWER
    using BuildCompression = UnityEngine.BuildCompression;
#else
    using BuildCompression = UnityEditor.Build.Content.BuildCompression;
#endif

    public sealed class ProjectInstrumentedArchiveAndCompressBundles : IBuildTask
    {
        private const int TaskVersion = 2;
        public int Version => TaskVersion;

#pragma warning disable 649
        [InjectContext(ContextUsage.In)]
        private IBuildParameters _parameters;

        [InjectContext(ContextUsage.In)]
        private IBundleWriteData _writeData;

#if UNITY_2019_3_OR_NEWER
        [InjectContext(ContextUsage.In)]
        private IBundleBuildContent _content;
#endif

        [InjectContext]
        private IBundleBuildResults _results;

        [InjectContext(ContextUsage.In, true)]
        private IProgressTracker _tracker;

        [InjectContext(ContextUsage.In, true)]
        private IBuildCache _cache;

        [InjectContext(ContextUsage.In, true)]
        private IBuildLogger _log;
#pragma warning restore 649

        public ReturnCode Run()
        {
            TaskInput input = new TaskInput
            {
                InternalFilenameToWriteResults = _results.WriteResults,
#if UNITY_2019_3_OR_NEWER
                BundleNameToAdditionalFiles = _content.AdditionalFiles,
#endif
                InternalFilenameToBundleName = _writeData.FileToBundle,
                GetCompressionForIdentifier = identifier => _parameters.GetCompressionForIdentifier(identifier),
                GetOutputFilePathForIdentifier = identifier => _parameters.GetOutputFilePathForIdentifier(identifier),
                BuildCache = _parameters.UseCache ? _cache : null,
                ProgressTracker = _tracker,
                TempOutputFolder = _parameters.TempOutputFolder,
                AssetToFilesDependencies = _writeData.AssetToFiles,
                InternalFilenameToWriteMetaData = _results.WriteResultsMetaData,
                Log = _log,
                StripUnityVersion = (_parameters.GetContentBuildSettings().buildFlags & ContentBuildFlags.StripUnityVersion) != 0,
                Threaded = SbpPreferences.threadedArchiving
            };

            ReturnCode code = Run(input, out TaskOutput output);
            if (code == ReturnCode.Success)
            {
                foreach (KeyValuePair<string, BundleDetails> item in output.BundleDetails)
                    _results.BundleInfos.Add(item.Key, item.Value);
            }

            return code;
        }

        private static void CopyFileWithTimestampIfDifferent(string sourcePath, string destinationPath, IBuildLogger log)
        {
            if (sourcePath == destinationPath)
                return;

            sourcePath = Path.GetFullPath(sourcePath);
            destinationPath = Path.GetFullPath(destinationPath);

#if UNITY_EDITOR_WIN
            const int maxPath = 260;
            if (sourcePath.Length > maxPath)
                throw new PathTooLongException(sourcePath);
            if (destinationPath.Length > maxPath)
                throw new PathTooLongException(destinationPath);
#endif

            DateTime sourceTime = File.GetLastWriteTime(sourcePath);
            DateTime destinationTime = File.Exists(destinationPath) ? File.GetLastWriteTime(destinationPath) : new DateTime();
            if (destinationTime == sourceTime)
                return;

            using (log.ScopedStep(LogLevel.Verbose, "Copying From Cache", sourcePath + " -> " + destinationPath))
            {
                string directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.Copy(sourcePath, destinationPath, true);
            }
        }

        private static CacheEntry GetCacheEntry(
            IBuildCache cache,
            string bundleName,
            IEnumerable<ResourceFile> resources,
            BuildCompression compression,
            List<SerializedFileMetaData> hashes)
        {
            CacheEntry entry = new CacheEntry();
            SetCacheEntryProperty(ref entry, "Type", CacheEntry.EntryType.Data);
            SetCacheEntryProperty(ref entry, "Guid", HashingMethods.Calculate("ArchiveAndCompressBundles", bundleName).ToGUID());

            List<object> toHash = new List<object> { TaskVersion, compression };
            foreach (ResourceFile resource in resources)
            {
                toHash.Add(resource.serializedFile);
                toHash.Add(resource.fileAlias);
            }

            toHash.AddRange(hashes.Select(item => (object)item.RawFileHash));
            SetCacheEntryProperty(ref entry, "Hash", HashingMethods.Calculate(toHash).ToHash128());
            SetCacheEntryProperty(ref entry, "Version", TaskVersion);
            return entry;
        }

        private static void SetCacheEntryProperty(ref CacheEntry entry, string propertyName, object value)
        {
            PropertyInfo property = typeof(CacheEntry).GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = property == null ? null : property.GetSetMethod(true);
            if (setter == null)
                throw new InvalidOperationException("CacheEntry property is not writable: " + propertyName);

            object boxed = entry;
            setter.Invoke(boxed, new[] { value });
            entry = (CacheEntry)boxed;
        }

        private static CachedInfo GetCachedInfo(
            IBuildCache cache,
            CacheEntry entry,
            IEnumerable<ResourceFile> resources,
            BundleDetails details)
        {
            return new CachedInfo
            {
                Asset = entry,
                Dependencies = new CacheEntry[0],
                Data = new object[] { details }
            };
        }

        private static Hash128 CalculateHashVersion(ArchiveWorkItem item, string[] dependencies)
        {
            List<Hash128> hashes = new List<Hash128>();
            hashes.AddRange(item.SerializedFileMetaDatas.Select(value => value.ContentHash));
            return HashingMethods.Calculate(hashes, dependencies).ToHash128();
        }

        private static ReturnCode Run(TaskInput input, out TaskOutput output)
        {
            output = new TaskOutput
            {
                BundleDetails = new Dictionary<string, BundleDetails>()
            };

            List<ArchiveWorkItem> allItems = CreateWorkItems(input);

            IList<CacheEntry> cacheEntries = null;
            IList<CachedInfo> cachedInfo = null;
            List<ArchiveWorkItem> cachedItems = new List<ArchiveWorkItem>();
            List<ArchiveWorkItem> nonCachedItems = allItems;

            if (input.BuildCache != null)
            {
                using (input.Log.ScopedStep(LogLevel.Info, "Creating Cache Entries"))
                {
                    cacheEntries = allItems
                        .Select(item => GetCacheEntry(
                            input.BuildCache,
                            item.BundleName,
                            item.ResourceFiles,
                            item.Compression,
                            item.SerializedFileMetaDatas))
                        .ToList();
                }

                using (input.Log.ScopedStep(LogLevel.Info, "Load Cached Data"))
                    input.BuildCache.LoadCachedData(cacheEntries, out cachedInfo);

                cachedItems = allItems.Where(item => cachedInfo[item.Index] != null).ToList();
                nonCachedItems = allItems.Where(item => cachedInfo[item.Index] == null).ToList();
                foreach (ArchiveWorkItem item in allItems)
                {
                    item.CachedArtifactPath = string.Format(
                        "{0}/{1}",
                        input.BuildCache.GetCachedArtifactsDirectory(cacheEntries[item.Index]),
                        HashingMethods.Calculate(item.BundleName));
                }
            }

            int cachedCompletedCount = 0;
            using (input.Log.ScopedStep(LogLevel.Info, "CopyingCachedFiles"))
            {
                foreach (ArchiveWorkItem item in cachedItems)
                {
                    if (input.ProgressTracker != null &&
                        !input.ProgressTracker.UpdateInfo(string.Format("{0} (Cached)", item.BundleName)))
                    {
                        return ReturnCode.Canceled;
                    }

                    item.ResultDetails = (BundleDetails)cachedInfo[item.Index].Data[0];
                    item.ResultDetails.FileName = item.OutputFilePath;
                    CopyCachedItemWithMetrics(item, input.Log, allItems.Count, ref cachedCompletedCount);
                }
            }

            if (!ArchiveItems(
                    nonCachedItems,
                    input.TempOutputFolder,
                    input.ProgressTracker,
                    input.Threaded,
                    input.Log,
                    input.StripUnityVersion,
                    allItems.Count,
                    cachedCompletedCount))
            {
                return ReturnCode.Canceled;
            }

            PostArchiveProcessing(allItems, input.AssetToFilesDependencies.Values.ToList(), input.InternalFilenameToBundleName, input.Log);

            if (input.BuildCache != null)
            {
                using (input.Log.ScopedStep(LogLevel.Info, "Copying To Cache"))
                {
                    List<CachedInfo> uncachedInfo = nonCachedItems
                        .Select(item => GetCachedInfo(input.BuildCache, cacheEntries[item.Index], item.ResourceFiles, item.ResultDetails))
                        .ToList();
                    input.BuildCache.SaveCachedData(uncachedInfo);
                }
            }

            output.BundleDetails = allItems.ToDictionary(item => item.BundleName, item => item.ResultDetails);
            return ReturnCode.Success;
        }

        private static void CopyCachedItemWithMetrics(
            ArchiveWorkItem item,
            IBuildLogger log,
            int totalCount,
            ref int cachedCompletedCount)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            CiBuildMetricsReporter.ReportBundleStarted(
                item.BundleName,
                cachedCompletedCount,
                totalCount,
                item.EstimatedInputSizeBytes,
                item.AssetCount,
                true);

            try
            {
                CopyFileWithTimestampIfDifferent(item.CachedArtifactPath, item.ResultDetails.FileName, log);
                int completed = Interlocked.Increment(ref cachedCompletedCount);
                CiBuildMetricsReporter.ReportBundleFinished(
                    item.BundleName,
                    completed,
                    totalCount,
                    item.EstimatedInputSizeBytes,
                    GetFileSize(item.ResultDetails.FileName),
                    item.AssetCount,
                    true,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                CiBuildMetricsReporter.ReportBundleFailed(
                    item.BundleName,
                    cachedCompletedCount,
                    totalCount,
                    item.EstimatedInputSizeBytes,
                    item.AssetCount,
                    true,
                    stopwatch.ElapsedMilliseconds,
                    exception);
                throw;
            }
        }

        private static Dictionary<string, string[]> CalculateBundleDependencies(
            List<List<string>> assetFileList,
            Dictionary<string, string> filenameToBundleName)
        {
            Dictionary<string, string[]> bundleDependencies = new Dictionary<string, string[]>();
            Dictionary<string, HashSet<string>> bundleDependenciesHash = new Dictionary<string, HashSet<string>>();
            foreach (List<string> files in assetFileList)
            {
                if (files == null || files.Count == 0)
                    continue;

                string bundle = filenameToBundleName[files.First()];
                HashSet<string> dependencies = GetOrAdd(bundleDependenciesHash, bundle);
                dependencies.UnionWith(files.Select(file => filenameToBundleName[file]));
                dependencies.Remove(bundle);

                foreach (string file in files)
                    GetOrAdd(bundleDependenciesHash, filenameToBundleName[file]);
            }

            foreach (KeyValuePair<string, HashSet<string>> dependencyPair in bundleDependenciesHash)
            {
                List<string> dependencies = dependencyPair.Value.ToList();
                for (int i = 0; i < dependencies.Count; i++)
                {
                    if (!bundleDependenciesHash.TryGetValue(dependencies[i], out HashSet<string> recursiveDependencies))
                        continue;

                    foreach (string recursiveDependency in recursiveDependencies)
                    {
                        if (dependencyPair.Value.Add(recursiveDependency))
                            dependencies.Add(recursiveDependency);
                    }
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> dependency in bundleDependenciesHash)
            {
                string[] values = dependency.Value.ToArray();
                Array.Sort(values);
                bundleDependencies.Add(dependency.Key, values);
            }

            return bundleDependencies;
        }

        private static HashSet<string> GetOrAdd(Dictionary<string, HashSet<string>> dictionary, string key)
        {
            if (!dictionary.TryGetValue(key, out HashSet<string> value))
            {
                value = new HashSet<string>();
                dictionary.Add(key, value);
            }

            return value;
        }

        private static void PostArchiveProcessing(
            List<ArchiveWorkItem> items,
            List<List<string>> assetFileList,
            Dictionary<string, string> filenameToBundleName,
            IBuildLogger log)
        {
            using (log.ScopedStep(LogLevel.Info, "PostArchiveProcessing"))
            {
                Dictionary<string, string[]> bundleDependencies = CalculateBundleDependencies(assetFileList, filenameToBundleName);
                foreach (ArchiveWorkItem item in items)
                {
                    item.ResultDetails.Dependencies = bundleDependencies.ContainsKey(item.BundleName)
                        ? bundleDependencies[item.BundleName]
                        : new string[0];
                    item.ResultDetails.Hash = CalculateHashVersion(item, item.ResultDetails.Dependencies);
                }
            }
        }

        private static ArchiveWorkItem GetOrCreateWorkItem(
            TaskInput input,
            string bundleName,
            Dictionary<string, ArchiveWorkItem> bundleToWorkItem)
        {
            if (!bundleToWorkItem.TryGetValue(bundleName, out ArchiveWorkItem item))
            {
                item = new ArchiveWorkItem
                {
                    BundleName = bundleName,
                    Compression = input.GetCompressionForIdentifier(bundleName),
                    OutputFilePath = input.GetOutputFilePathForIdentifier(bundleName),
                    ResourceFiles = new List<ResourceFile>()
                };
                bundleToWorkItem[bundleName] = item;
            }

            return item;
        }

        private static RawHash HashResourceFiles(List<ResourceFile> files)
        {
            return HashingMethods.Calculate(files.Select(file => HashingMethods.CalculateFile(file.fileName)));
        }

        private static List<ArchiveWorkItem> CreateWorkItems(TaskInput input)
        {
            using (input.Log.ScopedStep(LogLevel.Info, "CreateWorkItems"))
            {
                Dictionary<string, ArchiveWorkItem> bundleNameToWorkItem =
                    new Dictionary<string, ArchiveWorkItem>(StringComparer.OrdinalIgnoreCase);

                foreach (KeyValuePair<string, WriteResult> pair in input.InternalFilenameToWriteResults)
                {
                    string internalName = pair.Key;
                    string bundleName = input.InternalFilenameToBundleName[internalName];
                    ArchiveWorkItem item = GetOrCreateWorkItem(input, bundleName, bundleNameToWorkItem);

                    if (input.InternalFilenameToWriteMetaData.TryGetValue(pair.Key, out SerializedFileMetaData metadata))
                        item.SerializedFileMetaDatas.Add(metadata);
                    else
                        throw new Exception("Archive " + bundleName + " with internal name " + internalName + " does not have associated SerializedFileMetaData");

                    item.ResourceFiles.AddRange(pair.Value.resourceFiles);

#if UNITY_2019_3_OR_NEWER
                    if (input.BundleNameToAdditionalFiles.TryGetValue(bundleName, out List<ResourceFile> additionalFiles))
                    {
                        RawHash hash = HashResourceFiles(additionalFiles);
                        item.SerializedFileMetaDatas.Add(new SerializedFileMetaData
                        {
                            ContentHash = hash.ToHash128(),
                            RawFileHash = hash.ToHash128()
                        });
                        item.ResourceFiles.AddRange(additionalFiles);
                    }
#endif
                }

                List<ArchiveWorkItem> allItems = bundleNameToWorkItem
                    .Select((pair, index) =>
                    {
                        pair.Value.Index = index;
                        return pair.Value;
                    })
                    .ToList();

                PopulateWorkItemMetrics(input, allItems);
                return allItems;
            }
        }

        private static void PopulateWorkItemMetrics(TaskInput input, List<ArchiveWorkItem> items)
        {
            Dictionary<string, ArchiveWorkItem> byBundle = items.ToDictionary(
                item => item.BundleName,
                item => item,
                StringComparer.OrdinalIgnoreCase);

            foreach (ArchiveWorkItem item in items)
                item.EstimatedInputSizeBytes = item.ResourceFiles.Sum(file => GetFileSize(file.fileName));

            foreach (KeyValuePair<GUID, List<string>> pair in input.AssetToFilesDependencies)
            {
                HashSet<string> assetBundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string fileName in pair.Value)
                {
                    if (input.InternalFilenameToBundleName.TryGetValue(fileName, out string bundleName))
                        assetBundles.Add(bundleName);
                }

                foreach (string bundleName in assetBundles)
                {
                    if (byBundle.TryGetValue(bundleName, out ArchiveWorkItem item))
                        item.AssetCount++;
                }
            }
        }

        private static void ArchiveSingleItem(
            ArchiveWorkItem item,
            string tempOutputFolder,
            IBuildLogger log,
            bool stripUnityVersion)
        {
            using (log.ScopedStep(LogLevel.Info, "ArchiveSingleItem", item.BundleName))
            {
                item.ResultDetails = new BundleDetails();
                string writePath = string.Format("{0}/{1}", tempOutputFolder, item.BundleName);
                if (!string.IsNullOrEmpty(item.CachedArtifactPath))
                    writePath = item.CachedArtifactPath;

                string directory = Path.GetDirectoryName(writePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                item.ResultDetails.FileName = item.OutputFilePath;
                item.ResultDetails.Crc = ContentBuildInterface.ArchiveAndCompress(
                    item.ResourceFiles.ToArray(),
                    writePath,
                    item.Compression,
                    stripUnityVersion);

                CopyFileWithTimestampIfDifferent(writePath, item.ResultDetails.FileName, log);
            }
        }

        private static bool ArchiveItems(
            List<ArchiveWorkItem> items,
            string tempOutputFolder,
            IProgressTracker tracker,
            bool threaded,
            IBuildLogger log,
            bool stripUnityVersion,
            int totalCount,
            int alreadyCompleted)
        {
            using (log.ScopedStep(LogLevel.Info, "ArchiveItems", threaded))
            {
                log?.AddEntry(LogLevel.Info, "Archiving " + items.Count + " Bundles");
                if (threaded)
                    return ArchiveItemsThreaded(items, tempOutputFolder, tracker, log, stripUnityVersion, totalCount, alreadyCompleted);

                int completedCount = alreadyCompleted;
                foreach (ArchiveWorkItem item in items)
                {
                    if (tracker != null && !tracker.UpdateInfo(item.BundleName))
                        return false;

                    ArchiveItemWithMetrics(
                        item,
                        tempOutputFolder,
                        log,
                        stripUnityVersion,
                        totalCount,
                        false,
                        () => completedCount,
                        () => ++completedCount);
                }

                return true;
            }
        }

        private static bool ArchiveItemsThreaded(
            List<ArchiveWorkItem> items,
            string tempOutputFolder,
            IProgressTracker tracker,
            IBuildLogger log,
            bool stripUnityVersion,
            int totalCount,
            int alreadyCompleted)
        {
            CancellationTokenSource sourceToken = new CancellationTokenSource();
            SemaphoreSlim semaphore = new SemaphoreSlim(0);
            List<Task> tasks = new List<Task>(items.Count);
            int completedCount = alreadyCompleted;

            foreach (ArchiveWorkItem item in items)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        ArchiveItemWithMetrics(
                            item,
                            tempOutputFolder,
                            log,
                            stripUnityVersion,
                            totalCount,
                            false,
                            () => Volatile.Read(ref completedCount),
                            () => Interlocked.Increment(ref completedCount));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, sourceToken.Token));
            }

            for (int i = 0; i < items.Count; i++)
            {
                semaphore.Wait(sourceToken.Token);
                if (tracker != null && !tracker.UpdateInfo("Archive " + (i + 1) + "/" + items.Count))
                {
                    sourceToken.Cancel();
                    break;
                }
            }

            try
            {
                Task.WaitAll(tasks.ToArray());
            }
            catch (AggregateException)
            {
                // Exceptions are counted and reported below so SBP logs each one consistently.
            }

            int exceptionCount = 0;
            foreach (Task task in tasks)
            {
                if (task.Exception == null)
                    continue;

                Debug.LogException(task.Exception);
                exceptionCount++;
            }

            if (exceptionCount > 0)
                throw new BuildFailedException("ProjectInstrumentedArchiveAndCompressBundles encountered " + exceptionCount + " exception(s). See console for logged exceptions.");

            return !sourceToken.Token.IsCancellationRequested;
        }

        private static void ArchiveItemWithMetrics(
            ArchiveWorkItem item,
            string tempOutputFolder,
            IBuildLogger log,
            bool stripUnityVersion,
            int totalCount,
            bool cached,
            Func<int> getCompleted,
            Func<int> markCompleted)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int completedBefore = getCompleted == null ? 0 : getCompleted();
            CiBuildMetricsReporter.ReportBundleStarted(
                item.BundleName,
                completedBefore,
                totalCount,
                item.EstimatedInputSizeBytes,
                item.AssetCount,
                cached);

            try
            {
                ArchiveSingleItem(item, tempOutputFolder, log, stripUnityVersion);
                int completed = markCompleted == null ? 0 : markCompleted();
                CiBuildMetricsReporter.ReportBundleFinished(
                    item.BundleName,
                    completed,
                    totalCount,
                    item.EstimatedInputSizeBytes,
                    GetFileSize(item.ResultDetails.FileName),
                    item.AssetCount,
                    cached,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                CiBuildMetricsReporter.ReportBundleFailed(
                    item.BundleName,
                    getCompleted == null ? 0 : getCompleted(),
                    totalCount,
                    item.EstimatedInputSizeBytes,
                    item.AssetCount,
                    cached,
                    stopwatch.ElapsedMilliseconds,
                    exception);
                throw;
            }
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

        private sealed class ArchiveWorkItem
        {
            public int Index;
            public string BundleName;
            public string OutputFilePath;
            public string CachedArtifactPath;
            public List<ResourceFile> ResourceFiles;
            public BuildCompression Compression;
            public BundleDetails ResultDetails;
            public readonly List<SerializedFileMetaData> SerializedFileMetaDatas = new List<SerializedFileMetaData>();
            public long EstimatedInputSizeBytes;
            public int AssetCount;
        }

        private struct TaskInput
        {
            public Dictionary<string, WriteResult> InternalFilenameToWriteResults;
            public Dictionary<string, SerializedFileMetaData> InternalFilenameToWriteMetaData;
#if UNITY_2019_3_OR_NEWER
            public Dictionary<string, List<ResourceFile>> BundleNameToAdditionalFiles;
#endif
            public Dictionary<string, string> InternalFilenameToBundleName;
            public Func<string, BuildCompression> GetCompressionForIdentifier;
            public Func<string, string> GetOutputFilePathForIdentifier;
            public IBuildCache BuildCache;
            public Dictionary<GUID, List<string>> AssetToFilesDependencies;
            public IProgressTracker ProgressTracker;
            public string TempOutputFolder;
            public bool Threaded;
            public IBuildLogger Log;
            public bool StripUnityVersion;
        }

        private struct TaskOutput
        {
            public Dictionary<string, BundleDetails> BundleDetails;
        }
    }
}
