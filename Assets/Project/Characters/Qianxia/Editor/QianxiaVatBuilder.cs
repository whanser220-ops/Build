using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class QianxiaVatBuilder
{
    private const string SourceModelAssetPath = "Assets/Project/Characters/Qianxia/SourceModels/LOD2.fbx";
    private const string SourceAnimationsRoot = "Assets/Project/Characters/Qianxia/SourceAnimations";
    private const string OutputFolder = "Assets/Project/Characters/Qianxia/Generated/VAT";
    private const string OutputName = "QianxiaCrowdLod2Vat";
    private const string ShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatLit.shader";
    private const string BuildRequestFile = ".workspace/artifacts/qianxia-vat-build.request";
    private const string BuildResultFile = ".workspace/artifacts/qianxia-vat-build.result.txt";

    [MenuItem("Tools/Qianxia/Create Or Replace Crowd VAT")]
    public static void CreateOrReplaceCrowdVat()
    {
        CreateOrReplaceCrowdVatInternal(true);
    }

    public static string GetBuildRequestPath()
    {
        return Path.Combine(GetProjectRoot(), BuildRequestFile);
    }

    public static string GetBuildResultPath()
    {
        return Path.Combine(GetProjectRoot(), BuildResultFile);
    }

    public static CrowdVatBaker.BakeResult CreateOrReplaceCrowdVatInternal(bool logToConsole)
    {
        CrowdVatBaker.BakeResult result = BuildCrowdVat(SourceModelAssetPath, OutputName);

        if (logToConsole)
        {
            Debug.Log(
                $"Qianxia crowd VAT created.\n" +
                $"Asset: {AssetDatabase.GetAssetPath(result.AnimationAsset)}\n" +
                $"Prefab: {AssetDatabase.GetAssetPath(result.Prefab)}\n" +
                $"Mesh: {AssetDatabase.GetAssetPath(result.Mesh)}\n" +
                $"Texture: {AssetDatabase.GetAssetPath(result.BoneTexture)}");
        }

        return result;
    }

    private static CrowdVatBaker.BakeResult BuildCrowdVat(string sourceModelAssetPath, string outputName)
    {
        GameObject sourceModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourceModelAssetPath);
        if (sourceModelAsset == null)
            throw new InvalidOperationException($"Source crowd model was not found at `{sourceModelAssetPath}`.");

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
            throw new InvalidOperationException($"VAT shader was not found at `{ShaderPath}`.");

        List<CrowdVatBaker.ClipSource> clipSources = LoadClipSources(SourceAnimationsRoot);
        if (clipSources.Count == 0)
            throw new InvalidOperationException($"No valid animation clips were found under `{SourceAnimationsRoot}`.");

        CrowdVatBaker.BakeResult result = CrowdVatBaker.BakeSkinnedMeshToVat(
            sourceModelAsset,
            clipSources,
            OutputFolder,
            outputName,
            shader);

        QianxiaCrowdTextureApplyUtility.ApplyWalkingTexturesToCrowdVat();
        return result;
    }

    private static List<CrowdVatBaker.ClipSource> LoadClipSources(string rootFolder)
    {
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { rootFolder });
        List<string> assetPaths = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<CrowdVatBaker.ClipSource> clipSources = new List<CrowdVatBaker.ClipSource>();
        for (int pathIndex = 0; pathIndex < assetPaths.Count; pathIndex++)
        {
            string assetPath = assetPaths[pathIndex];
            GameObject motionSourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (motionSourceAsset == null)
                continue;

            List<AnimationClip> clips = LoadClips(assetPath);
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                AnimationClip clip = clips[clipIndex];
                if (clip == null)
                    continue;

                clipSources.Add(new CrowdVatBaker.ClipSource
                {
                    Clip = clip,
                    MotionSourceAsset = motionSourceAsset,
                    ClipNameOverride = BuildClipName(assetPath, clips.Count, clip)
                });
            }
        }

        return clipSources;
    }

    private static List<AnimationClip> LoadClips(string assetPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<AnimationClip>()
            .Where(clip => clip != null && !IsPreviewClip(clip))
            .ToList();
    }

    private static string BuildClipName(string assetPath, int clipCountInAsset, AnimationClip clip)
    {
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (clip == null)
            return fileName;

        if (clipCountInAsset <= 1 || string.Equals(fileName, clip.name, StringComparison.OrdinalIgnoreCase))
            return fileName;

        return $"{fileName}_{clip.name}";
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static bool IsPreviewClip(AnimationClip clip)
    {
        if (clip == null)
            return true;

        return clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase);
    }
}

[InitializeOnLoad]
public static class QianxiaVatBuildBootstrap
{
    private static double _nextCheckTime;

    static QianxiaVatBuildBootstrap()
    {
        EditorApplication.delayCall += TryRunQueuedBuild;
        EditorApplication.update += PollQueuedBuild;
    }

    private static void PollQueuedBuild()
    {
        if (EditorApplication.timeSinceStartup < _nextCheckTime)
            return;

        _nextCheckTime = EditorApplication.timeSinceStartup + 2.0d;
        TryRunQueuedBuild();
    }

    private static void TryRunQueuedBuild()
    {
        string requestPath = QianxiaVatBuilder.GetBuildRequestPath();
        if (!File.Exists(requestPath))
            return;

        string resultPath = QianxiaVatBuilder.GetBuildResultPath();

        try
        {
            File.Delete(requestPath);
            CrowdVatBaker.BakeResult result = QianxiaVatBuilder.CreateOrReplaceCrowdVatInternal(false);
            File.WriteAllText(
                resultPath,
                $"SUCCESS{Environment.NewLine}" +
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"{AssetDatabase.GetAssetPath(result.AnimationAsset)}{Environment.NewLine}" +
                $"{AssetDatabase.GetAssetPath(result.Prefab)}{Environment.NewLine}" +
                $"{AssetDatabase.GetAssetPath(result.Mesh)}{Environment.NewLine}" +
                $"{AssetDatabase.GetAssetPath(result.BoneTexture)}");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                resultPath,
                $"FAILED{Environment.NewLine}{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}");
            Debug.LogException(exception);
        }
    }
}
