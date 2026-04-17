using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class QianxiaVatBuilder
{
    private const string SourceModelAssetPath = "Assets/Project/Characters/Qianxia/SourceModels/LOD2.fbx";
    private const string WalkClipAssetPath = "Assets/Project/Characters/Qianxia/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx";
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

        GameObject motionSourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WalkClipAssetPath);
        if (motionSourceAsset == null)
            throw new InvalidOperationException($"Motion source model was not found at `{WalkClipAssetPath}`.");

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
            throw new InvalidOperationException($"VAT shader was not found at `{ShaderPath}`.");

        List<AnimationClip> clips = LoadClips(WalkClipAssetPath);
        if (clips.Count == 0)
            throw new InvalidOperationException($"No valid animation clips were found at `{WalkClipAssetPath}`.");

        CrowdVatBaker.BakeResult result = CrowdVatBaker.BakeSkinnedMeshToVat(
            sourceModelAsset,
            motionSourceAsset,
            clips,
            OutputFolder,
            outputName,
            shader);

        QianxiaCrowdTextureApplyUtility.ApplyWalkingTexturesToCrowdVat();
        return result;
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
