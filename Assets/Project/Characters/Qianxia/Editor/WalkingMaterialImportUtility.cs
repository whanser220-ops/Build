using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class WalkingMaterialImportUtility
{
    private const string WalkingAssetPath = "Assets/Project/Characters/Qianxia/Reference/Walking1/Walking (1).fbx";
    private const string RenAssetPath = "Assets/Project/Characters/Qianxia/Reference/Ren/ren.fbx";

    [MenuItem("Tools/Qianxia/Inspect Walking FBX Materials")]
    public static void InspectWalkingFbxMaterialsMenu()
    {
        Debug.Log(InspectWalkingFbxMaterials());
    }

    public static string InspectWalkingFbxMaterials()
    {
        GameObject walkingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WalkingAssetPath);
        GameObject renAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RenAssetPath);
        UnityEngine.Object[] walkingSubAssets = AssetDatabase.LoadAllAssetsAtPath(WalkingAssetPath);

        if (walkingAsset == null)
            throw new InvalidOperationException($"未能加载资源: {WalkingAssetPath}");

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Walking FBX Inspect");
        builder.AppendLine($"walkingAsset={WalkingAssetPath}");
        builder.AppendLine($"renAsset={RenAssetPath}");
        builder.AppendLine($"walkingSubAssets={walkingSubAssets.Length}");

        foreach (UnityEngine.Object asset in walkingSubAssets.OrderBy(item => item.GetType().Name).ThenBy(item => item.name))
        {
            builder.AppendLine($"subAsset: type={asset.GetType().Name} name={asset.name}");
            if (asset is Material material)
                AppendMaterialTextureInfo(builder, material);
            if (asset is Texture2D texture)
                builder.AppendLine($"  textureSize={texture.width}x{texture.height}");
        }

        AppendRendererInfo(builder, "walking", walkingAsset);
        if (renAsset != null)
            AppendRendererInfo(builder, "ren", renAsset);

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
