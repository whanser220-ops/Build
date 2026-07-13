using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class QianxiaCrowdTextureApplyUtility
{
    private const string SourceWalkingFbxPath = "Assets/Game/Characters/Qianxia/Art/Meshs/Ch36_nonPBR@Walking.fbx";
    private const string OutputTextureFolder = "Assets/Game/Characters/Qianxia/Runtime/Generated/Textures/Walking";
    private const string DiffuseTextureName = "Ch36_1001_Diffuse.png";
    private const string NormalTextureName = "Ch36_1001_Normal.png";
    private const string SpecularTextureName = "Ch36_1001_Specular.png";
    private const string GlossinessTextureName = "Ch36_1001_Glossiness.png";
    private const string SpecGlossTextureName = "Ch36_1001_SpecGloss.png";
    private const string VatMaterialsFolder = "Assets/Game/Characters/Qianxia/Runtime/Generated/VAT/Materials";

    private static readonly string[] ExpectedTextureNames =
    {
        DiffuseTextureName,
        NormalTextureName,
        SpecularTextureName,
        GlossinessTextureName
    };

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] PngEndSignature = { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };
    private static readonly Regex TextureNameRegex = new Regex(@"Ch36_1001_[A-Za-z0-9_]+\.png", RegexOptions.Compiled);

    [MenuItem("Tools/Qianxia/Apply Walking Textures To Crowd VAT")]
    public static void ApplyWalkingTexturesToCrowdVatMenu()
    {
        ApplyWalkingTexturesToCrowdVat();
    }

    public static void ApplyWalkingTexturesToCrowdVat()
    {
        EnsureFolder(OutputTextureFolder);
        ExtractEmbeddedTextures();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        string diffusePath = GetOutputTexturePath(DiffuseTextureName);
        string normalPath = GetOutputTexturePath(NormalTextureName);
        string specularPath = GetOutputTexturePath(SpecularTextureName);
        string glossinessPath = GetOutputTexturePath(GlossinessTextureName);
        string specGlossPath = GetOutputTexturePath(SpecGlossTextureName);

        ConfigureTextureImporter(diffusePath, TextureImporterType.Default, false, true);
        ConfigureTextureImporter(normalPath, TextureImporterType.NormalMap, false, false);
        ConfigureTextureImporter(specularPath, TextureImporterType.Default, true, true);
        ConfigureTextureImporter(glossinessPath, TextureImporterType.Default, true, false);

        CreateOrUpdateSpecGlossTexture(specularPath, glossinessPath, specGlossPath);
        ConfigureTextureImporter(specularPath, TextureImporterType.Default, false, true);
        ConfigureTextureImporter(glossinessPath, TextureImporterType.Default, false, false);
        ConfigureTextureImporter(specGlossPath, TextureImporterType.Default, false, true);

        Texture2D diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
        Texture2D normalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        Texture2D specGlossTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(specGlossPath);

        if (diffuseTexture == null || normalTexture == null || specGlossTexture == null)
            throw new InvalidOperationException("One or more extracted crowd textures failed to import.");

        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { VatMaterialsFolder });
        List<string> updatedMaterialPaths = new List<string>(materialGuids.Length);
        for (int guidIndex = 0; guidIndex < materialGuids.Length; guidIndex++)
        {
            string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[guidIndex]);
            Material vatMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (vatMaterial == null || !vatMaterial.name.StartsWith("QianxiaCrowd", StringComparison.Ordinal))
                continue;

            vatMaterial.SetTexture("_BaseMap", diffuseTexture);
            vatMaterial.SetColor("_BaseColor", Color.white);
            vatMaterial.SetTexture("_BumpMap", normalTexture);
            vatMaterial.SetFloat("_BumpScale", 1.0f);
            vatMaterial.SetFloat("_UseNormalMap", 1.0f);
            vatMaterial.SetTexture("_SpecGlossMap", specGlossTexture);
            vatMaterial.SetColor("_SpecColor", Color.white);
            vatMaterial.SetFloat("_Smoothness", 1.0f);
            vatMaterial.SetFloat("_SpecularStrength", 0.18f);
            vatMaterial.SetFloat("_UseSpecGlossMap", 1.0f);
            vatMaterial.SetFloat("_AlphaClip", 0.0f);
            vatMaterial.SetFloat("_Cutoff", 0.5f);
            EditorUtility.SetDirty(vatMaterial);
            updatedMaterialPaths.Add(materialPath);
        }

        if (updatedMaterialPaths.Count == 0)
            throw new InvalidOperationException($"No crowd VAT materials were found under `{VatMaterialsFolder}`.");

        AssetDatabase.SaveAssets();

        Debug.Log(
            "Applied walking textures to crowd VAT materials.\n" +
            $"Material Count: {updatedMaterialPaths.Count}\n" +
            $"Diffuse: {diffusePath}\n" +
            $"Normal: {normalPath}\n" +
            $"SpecGloss: {specGlossPath}");
    }

    private static void ExtractEmbeddedTextures()
    {
        string absoluteFbxPath = GetAbsolutePath(SourceWalkingFbxPath);
        if (!File.Exists(absoluteFbxPath))
            throw new FileNotFoundException("Walking FBX source file was not found.", absoluteFbxPath);

        byte[] bytes = File.ReadAllBytes(absoluteFbxPath);
        Dictionary<string, byte[]> extractedTextures = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        int searchOffset = 0;
        while (searchOffset <= bytes.Length - PngSignature.Length)
        {
            int pngStart = FindSequence(bytes, PngSignature, searchOffset);
            if (pngStart < 0)
                break;

            int pngEnd = FindSequence(bytes, PngEndSignature, pngStart + PngSignature.Length);
            if (pngEnd < 0)
                break;

            pngEnd += PngEndSignature.Length;
            string textureName = ResolveEmbeddedTextureName(bytes, pngStart);
            if (!string.IsNullOrEmpty(textureName))
            {
                byte[] pngBytes = new byte[pngEnd - pngStart];
                Buffer.BlockCopy(bytes, pngStart, pngBytes, 0, pngBytes.Length);
                extractedTextures[textureName] = pngBytes;
            }

            searchOffset = pngEnd;
        }

        foreach (string textureName in ExpectedTextureNames)
        {
            if (!extractedTextures.TryGetValue(textureName, out byte[] textureBytes))
                throw new InvalidOperationException($"Embedded texture `{textureName}` was not found in `{SourceWalkingFbxPath}`.");

            WriteFileIfChanged(GetAbsolutePath(GetOutputTexturePath(textureName)), textureBytes);
        }
    }

    private static string ResolveEmbeddedTextureName(byte[] bytes, int pngStart)
    {
        int windowStart = Math.Max(0, pngStart - 4096);
        string windowText = System.Text.Encoding.ASCII.GetString(bytes, windowStart, pngStart - windowStart);
        MatchCollection matches = TextureNameRegex.Matches(windowText);
        if (matches.Count == 0)
            return null;

        string textureName = matches[matches.Count - 1].Value;
        for (int i = 0; i < ExpectedTextureNames.Length; i++)
        {
            if (string.Equals(ExpectedTextureNames[i], textureName, StringComparison.OrdinalIgnoreCase))
                return ExpectedTextureNames[i];
        }

        return null;
    }

    private static void CreateOrUpdateSpecGlossTexture(string specularTexturePath, string glossinessTexturePath, string outputTexturePath)
    {
        Texture2D specularTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(specularTexturePath);
        Texture2D glossinessTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(glossinessTexturePath);
        if (specularTexture == null || glossinessTexture == null)
            throw new InvalidOperationException("Specular or glossiness texture is missing.");

        if (specularTexture.width != glossinessTexture.width || specularTexture.height != glossinessTexture.height)
            throw new InvalidOperationException("Specular and glossiness texture sizes do not match.");

        Color32[] specularPixels = specularTexture.GetPixels32();
        Color32[] glossinessPixels = glossinessTexture.GetPixels32();
        Color32[] outputPixels = new Color32[specularPixels.Length];

        for (int pixelIndex = 0; pixelIndex < outputPixels.Length; pixelIndex++)
        {
            Color32 specular = specularPixels[pixelIndex];
            Color32 glossiness = glossinessPixels[pixelIndex];
            outputPixels[pixelIndex] = new Color32(specular.r, specular.g, specular.b, glossiness.r);
        }

        Texture2D combinedTexture = new Texture2D(specularTexture.width, specularTexture.height, TextureFormat.RGBA32, false, false)
        {
            name = Path.GetFileNameWithoutExtension(outputTexturePath)
        };
        combinedTexture.SetPixels32(outputPixels);
        combinedTexture.Apply(false, false);

        byte[] pngBytes = combinedTexture.EncodeToPNG();
        WriteFileIfChanged(GetAbsolutePath(outputTexturePath), pngBytes);
        UnityEngine.Object.DestroyImmediate(combinedTexture);
        AssetDatabase.ImportAsset(outputTexturePath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void ConfigureTextureImporter(string assetPath, TextureImporterType textureType, bool isReadable, bool sRgb)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"Texture importer was not found for `{assetPath}`.");

        bool changed = importer.textureType != textureType
            || importer.isReadable != isReadable
            || importer.sRGBTexture != sRgb;

        if (!changed)
            return;

        importer.textureType = textureType;
        importer.isReadable = isReadable;
        importer.sRGBTexture = sRgb;
        importer.alphaIsTransparency = false;
        importer.SaveAndReimport();
    }

    private static int FindSequence(byte[] buffer, byte[] sequence, int startIndex)
    {
        for (int index = startIndex; index <= buffer.Length - sequence.Length; index++)
        {
            bool matched = true;
            for (int sequenceIndex = 0; sequenceIndex < sequence.Length; sequenceIndex++)
            {
                if (buffer[index + sequenceIndex] != sequence[sequenceIndex])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return index;
        }

        return -1;
    }

    private static void WriteFileIfChanged(string absolutePath, byte[] bytes)
    {
        if (File.Exists(absolutePath))
        {
            byte[] currentBytes = File.ReadAllBytes(absolutePath);
            if (AreEqual(currentBytes, bytes))
                return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException("Output directory is invalid."));
        File.WriteAllBytes(absolutePath, bytes);
    }

    private static bool AreEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left == null || right == null || left.Length != right.Length)
            return false;

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
                return false;
        }

        return true;
    }

    private static string GetOutputTexturePath(string fileName)
    {
        return $"{OutputTextureFolder}/{fileName}";
    }

    private static string GetAbsolutePath(string assetPath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string[] segments = assetPath.Split('/');
        string currentPath = segments[0];

        for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
        {
            string nextPath = $"{currentPath}/{segments[segmentIndex]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
                AssetDatabase.CreateFolder(currentPath, segments[segmentIndex]);

            currentPath = nextPath;
        }
    }
}
