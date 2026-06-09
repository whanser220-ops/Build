using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class QianxiaVatBuilder
{
    private const string WalkClipAssetPath = "Assets/Project/Characters/Qianxia/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx";
    private const string WalkClipName = "Qianxia_Walk_Slow";
    private const string IdleClipAssetPath = "Assets/Project/Characters/Qianxia/SourceAnimations/Idle/Ch36_nonPBR@Zombie Idle.fbx";
    private const string IdleClipName = "Ch36_nonPBR@Zombie Idle";
    private const string OutputFolder = "Assets/Project/Characters/Qianxia/Generated/VAT";
    private const string ShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatLit.shader";
    private const string BuildRequestFile = ".workspace/artifacts/qianxia-vat-build.request";
    private const string BuildResultFile = ".workspace/artifacts/qianxia-vat-build.result.txt";

    private readonly struct VatBuildConfig
    {
        public VatBuildConfig(string sourceModelAssetPath, string outputName)
        {
            SourceModelAssetPath = sourceModelAssetPath;
            OutputName = outputName;
        }

        public string SourceModelAssetPath { get; }
        public string OutputName { get; }
    }

    private static readonly VatBuildConfig[] BuildConfigs =
    {
        new VatBuildConfig(QianxiaLodPrefabBuilder.Lod0AssetPath, "QianxiaCrowdLod0Vat"),
        new VatBuildConfig(QianxiaLodPrefabBuilder.Lod1AssetPath, "QianxiaCrowdLod1Vat"),
        new VatBuildConfig(QianxiaLodPrefabBuilder.Lod2AssetPath, "QianxiaCrowdLod2Vat")
    };

    [MenuItem("Tools/Qianxia/Create Or Replace Crowd VAT")]
    public static void CreateOrReplaceCrowdVat()
    {
        CreateOrReplaceCrowdVatSetInternal(true);
    }

    public static string GetBuildRequestPath()
    {
        return Path.Combine(GetProjectRoot(), BuildRequestFile);
    }

    public static string GetBuildResultPath()
    {
        return Path.Combine(GetProjectRoot(), BuildResultFile);
    }

    public static IReadOnlyList<CrowdVatBaker.BakeResult> CreateOrReplaceCrowdVatSetInternal(bool logToConsole)
    {
        List<CrowdVatBaker.BakeResult> results = new List<CrowdVatBaker.BakeResult>(BuildConfigs.Length);
        for (int configIndex = 0; configIndex < BuildConfigs.Length; configIndex++)
        {
            VatBuildConfig config = BuildConfigs[configIndex];
            results.Add(BuildCrowdVat(config.SourceModelAssetPath, config.OutputName));
        }

        if (logToConsole)
        {
            List<string> lines = new List<string>(results.Count * 5 + 1)
            {
                "Qianxia crowd VAT set created."
            };

            for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
            {
                CrowdVatBaker.BakeResult result = results[resultIndex];
                lines.Add($"[{resultIndex}] Asset: {AssetDatabase.GetAssetPath(result.AnimationAsset)}");
                lines.Add($"[{resultIndex}] Prefab: {AssetDatabase.GetAssetPath(result.Prefab)}");
                lines.Add($"[{resultIndex}] Mesh: {AssetDatabase.GetAssetPath(result.Mesh)}");
                lines.Add($"[{resultIndex}] Texture: {AssetDatabase.GetAssetPath(result.BoneTexture)}");
            }

            Debug.Log(string.Join(Environment.NewLine, lines));
        }

        return results;
    }

    private static CrowdVatBaker.BakeResult BuildCrowdVat(string sourceModelAssetPath, string outputName)
    {
        GameObject sourceModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourceModelAssetPath);
        if (sourceModelAsset == null)
            throw new InvalidOperationException($"Source crowd model was not found at `{sourceModelAssetPath}`.");

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
            throw new InvalidOperationException($"VAT shader was not found at `{ShaderPath}`.");

        List<CrowdVatBaker.ClipSource> clipSources = LoadLocomotionClipSources();
        if (clipSources.Count == 0)
            throw new InvalidOperationException("No valid locomotion clips were found for Qianxia VAT baking.");

        CrowdVatBaker.BakeResult result = CrowdVatBaker.BakeSkinnedMeshToVat(
            sourceModelAsset,
            clipSources,
            OutputFolder,
            outputName,
            shader);

        QianxiaCrowdTextureApplyUtility.ApplyWalkingTexturesToCrowdVat();
        return result;
    }

    private static List<CrowdVatBaker.ClipSource> LoadLocomotionClipSources()
    {
        return new List<CrowdVatBaker.ClipSource>
        {
            LoadClipSource(WalkClipAssetPath, WalkClipName, "walk"),
            LoadClipSource(IdleClipAssetPath, IdleClipName, "idle")
        };
    }

    private static CrowdVatBaker.ClipSource LoadClipSource(string assetPath, string preferredClipName, string label)
    {
        GameObject motionSourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (motionSourceAsset == null)
            throw new InvalidOperationException($"{label} motion source was not found at `{assetPath}`.");

        List<AnimationClip> clips = LoadClips(assetPath);
        AnimationClip clip = clips.FirstOrDefault(candidate => candidate != null && string.Equals(candidate.name, preferredClipName, StringComparison.Ordinal));
        if (clip == null && clips.Count == 1)
            clip = clips[0];

        if (clip == null)
        {
            throw new InvalidOperationException(
                $"{label} clip `{preferredClipName}` was not found at `{assetPath}`. Available clips: {string.Join(", ", clips.Select(candidate => candidate != null ? candidate.name : "<null>"))}");
        }

        return new CrowdVatBaker.ClipSource
        {
            Clip = clip,
            MotionSourceAsset = motionSourceAsset,
            ClipNameOverride = preferredClipName
        };
    }

    private static List<AnimationClip> LoadClips(string assetPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<AnimationClip>()
            .Where(clip => clip != null && !IsPreviewClip(clip))
            .ToList();
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
            IReadOnlyList<CrowdVatBaker.BakeResult> results = QianxiaVatBuilder.CreateOrReplaceCrowdVatSetInternal(false);
            List<string> lines = new List<string>(results.Count * 4 + 2)
            {
                "SUCCESS",
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };

            for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
            {
                CrowdVatBaker.BakeResult result = results[resultIndex];
                lines.Add(AssetDatabase.GetAssetPath(result.AnimationAsset));
                lines.Add(AssetDatabase.GetAssetPath(result.Prefab));
                lines.Add(AssetDatabase.GetAssetPath(result.Mesh));
                lines.Add(AssetDatabase.GetAssetPath(result.BoneTexture));
            }

            File.WriteAllText(resultPath, string.Join(Environment.NewLine, lines));

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
