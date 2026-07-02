using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class RenMaterialImportUtility
{
    private const string RenAssetPath = "Assets/GameResources/Characters/Qianxia/Reference/Ren/ren.fbx";
    private const string TargetModelPath = "Assets/GameResources/Characters/Qianxia/SourceModels/Qianxia_Rokoko_BlenderClean.fbx";

    [MenuItem("Tools/Qianxia/Inspect Ren FBX Materials")]
    public static void InspectRenFbxMaterialsMenu()
    {
        Debug.Log(InspectRenFbxMaterials());
    }

    public static string InspectRenFbxMaterials()
    {
        GameObject renAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RenAssetPath);
        GameObject targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TargetModelPath);
        UnityEngine.Object[] renSubAssets = AssetDatabase.LoadAllAssetsAtPath(RenAssetPath);

        if (renAsset == null)
            throw new InvalidOperationException($"未能加载资源: {RenAssetPath}");

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Ren FBX Inspect");
        builder.AppendLine($"renAsset={RenAssetPath}");
        builder.AppendLine($"targetAsset={TargetModelPath}");
        builder.AppendLine($"renSubAssets={renSubAssets.Length}");

        foreach (UnityEngine.Object asset in renSubAssets.OrderBy(item => item.GetType().Name).ThenBy(item => item.name))
        {
            builder.AppendLine($"subAsset: type={asset.GetType().Name} name={asset.name}");
            if (asset is Material material)
                AppendMaterialTextureInfo(builder, material);
        }

        AppendRendererInfo(builder, "ren", renAsset);

        if (targetAsset != null)
            AppendRendererInfo(builder, "target", targetAsset);

        return builder.ToString();
    }

    private static void AppendRendererInfo(StringBuilder builder, string label, GameObject asset)
    {
        Renderer[] renderers = asset.GetComponentsInChildren<Renderer>(true);
        builder.AppendLine($"{label}.rendererCount={renderers.Length}");

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            Material[] materials = renderer.sharedMaterials;
            builder.AppendLine($"{label}.renderer[{rendererIndex}]={renderer.name} materialCount={materials.Length}");
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                builder.AppendLine($"  material[{materialIndex}]={(material != null ? material.name : "<null>")}");
            }
        }
    }

    private static void AppendMaterialTextureInfo(StringBuilder builder, Material material)
    {
        string[] textureProperties =
        {
            "_BaseMap",
            "_MainTex",
            "_BumpMap",
            "_MetallicGlossMap",
            "_SpecGlossMap"
        };

        foreach (string propertyName in textureProperties)
        {
            if (!material.HasProperty(propertyName))
                continue;

            Texture texture = material.GetTexture(propertyName);
            builder.AppendLine($"  textureProperty {propertyName}={(texture != null ? texture.name : "<null>")}");
        }
    }
}
