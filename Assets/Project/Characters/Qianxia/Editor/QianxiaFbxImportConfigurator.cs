using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class QianxiaFbxImportConfigurator : AssetPostprocessor
{
    public const string ModelAssetPath = "Assets/Project/Characters/Qianxia/SourceModels/Qianxia_Rokoko_BlenderClean.fbx";
    public const string WalkAssetPath = "Assets/Project/Characters/Qianxia/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx";

    private void OnPreprocessModel()
    {
        if (assetImporter is not ModelImporter importer)
            return;

        if (string.Equals(assetPath, ModelAssetPath, StringComparison.OrdinalIgnoreCase))
        {
            ConfigureCharacterModel(importer);
            return;
        }

        if (string.Equals(assetPath, WalkAssetPath, StringComparison.OrdinalIgnoreCase))
            ConfigureWalkAnimation(importer);
    }

    private static void ConfigureCharacterModel(ModelImporter importer)
    {
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
    }

    private static void ConfigureWalkAnimation(ModelImporter importer)
    {
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;

        ModelImporterClipAnimation[] sourceClips = importer.clipAnimations;
        if (sourceClips == null || sourceClips.Length == 0)
            sourceClips = importer.defaultClipAnimations;

        if (sourceClips == null || sourceClips.Length == 0)
            return;

        for (int i = 0; i < sourceClips.Length; i++)
        {
            ModelImporterClipAnimation clip = sourceClips[i];
            clip.name = "Qianxia_Walk_Slow";
            clip.loopTime = true;
            clip.loopPose = true;
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
}
