using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class RenTextureApplyUtility
{
    private const string RenAssetPath = "Assets/GameResources/Characters/Qianxia/Reference/Ren/ren.fbx";
    private const string DiffuseTexturePath = "Assets/GameResources/Characters/Qianxia/Reference/Walking1/ExtractedTextures/Ch36_1001_Diffuse.png";
    private const string MaterialFolderPath = "Assets/GameResources/Characters/Qianxia/Reference/Ren/Materials";
    private const string MaterialAPath = MaterialFolderPath + "/Ch36_Body.mat";
    private const string MaterialBPath = MaterialFolderPath + "/Ch36_Body_001.mat";

    [MenuItem("Tools/Qianxia/Apply Walking Texture To Ren")]
    public static void ApplyWalkingTextureToRenMenu()
    {
        ApplyWalkingTextureToRen();
    }

    public static void ApplyWalkingTextureToRen()
    {
        Texture2D diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffuseTexturePath);
        if (diffuseTexture == null)
            throw new InvalidOperationException($"Diffuse texture not found: {DiffuseTexturePath}");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("URP/Lit shader not found.");

        EnsureFolder(MaterialFolderPath);

        Material materialA = CreateOrUpdateMaterial(MaterialAPath, shader, diffuseTexture, "Ch36_Body");
        Material materialB = CreateOrUpdateMaterial(MaterialBPath, shader, diffuseTexture, "Ch36_Body.001");

        ModelImporter importer = AssetImporter.GetAtPath(RenAssetPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException($"Unable to load ModelImporter: {RenAssetPath}");

        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Ch36_Body"), materialA);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Ch36_Body.001"), materialB);
        importer.SaveAndReimport();

        Debug.Log(
            "Applied walking diffuse texture to ren.fbx\n" +
            $"Texture: {DiffuseTexturePath}\n" +
            $"Materials: {MaterialAPath}, {MaterialBPath}");
    }

    private static Material CreateOrUpdateMaterial(string materialPath, Shader shader, Texture2D diffuseTexture, string materialName)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = materialName
            };
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.shader = shader;
        material.SetTexture("_BaseMap", diffuseTexture);
        material.SetTexture("_MainTex", diffuseTexture);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Surface", 0.0f);
        material.SetFloat("_AlphaClip", 0.0f);
        material.SetFloat("_Cutoff", 0.5f);
        material.SetFloat("_WorkflowMode", 1.0f);
        material.SetFloat("_Smoothness", 0.0f);
        material.SetFloat("_Metallic", 0.0f);
        material.renderQueue = (int)RenderQueue.Geometry;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string[] segments = assetPath.Split('/');
        string current = segments[0];

        for (int i = 1; i < segments.Length; i++)
        {
            string next = $"{current}/{segments[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);

            current = next;
        }
    }
}
