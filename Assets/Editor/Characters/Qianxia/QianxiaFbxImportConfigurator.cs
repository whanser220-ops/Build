using System;
using UnityEditor;
using UnityEngine;

public class QianxiaFbxImportConfigurator : AssetPostprocessor
{
    public const string ModelAssetPath = "Assets/GameResources/Characters/Qianxia/Meshs/Qianxia_Rokoko_BlenderClean.fbx";
    public const string WalkAssetPath = "Assets/GameResources/Characters/Qianxia/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx";
    public const string SourceAnimationsRoot = "Assets/GameResources/Characters/Qianxia/SourceAnimations/";

    private void OnPreprocessModel()
    {
        if (assetImporter is not ModelImporter importer)
            return;

        if (string.Equals(assetPath, ModelAssetPath, StringComparison.OrdinalIgnoreCase))
        {
            ConfigureCharacterModel(importer);
            return;
        }

        if (IsSourceAnimationAsset(assetPath))
            ConfigureAnimationAsset(importer, assetPath);
    }

    private static void ConfigureCharacterModel(ModelImporter importer)
    {
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
    }

    private static void ConfigureAnimationAsset(ModelImporter importer, string importedAssetPath)
    {
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;

        ModelImporterClipAnimation[] sourceClips = importer.clipAnimations;
        if (sourceClips == null || sourceClips.Length == 0)
            sourceClips = importer.defaultClipAnimations;

        if (sourceClips == null || sourceClips.Length == 0)
            return;

        bool shouldLoop = ShouldLoopAnimation(importedAssetPath);
        for (int i = 0; i < sourceClips.Length; i++)
        {
            ModelImporterClipAnimation clip = sourceClips[i];
            clip.loopTime = shouldLoop;
            clip.loopPose = shouldLoop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = false;
            clip.keepOriginalPositionY = false;
            clip.keepOriginalPositionXZ = false;
            clip.heightFromFeet = true;
            sourceClips[i] = clip;
        }

        importer.clipAnimations = sourceClips;
    }

    private static bool IsSourceAnimationAsset(string importedAssetPath)
    {
        return !string.IsNullOrWhiteSpace(importedAssetPath) &&
            importedAssetPath.StartsWith(SourceAnimationsRoot, StringComparison.OrdinalIgnoreCase) &&
            importedAssetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldLoopAnimation(string importedAssetPath)
    {
        if (string.IsNullOrWhiteSpace(importedAssetPath))
            return true;

        string normalizedPath = importedAssetPath.Replace('\\', '/');
        return normalizedPath.IndexOf("/OneShot/", StringComparison.OrdinalIgnoreCase) < 0;
    }
}
