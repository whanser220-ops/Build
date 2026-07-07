using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CrowdVatBaker
{
    private const int BakeFrameRate = 30;
    private const int BonePixelOffsetUvChannel = 2;
    private const int BoneWeightUvChannel = 3;
    private const int RowsPerBone = 3;

    public sealed class BakeResult
    {
        public CrowdVatAnimationAsset AnimationAsset { get; set; }
        public GameObject Prefab { get; set; }
        public Mesh Mesh { get; set; }
        public Texture2D BoneTexture { get; set; }
        public Material[] Materials { get; set; }
    }

    public sealed class ClipSource
    {
        public AnimationClip Clip { get; set; }
        public GameObject MotionSourceAsset { get; set; }
        public string ClipNameOverride { get; set; }
    }

    public static BakeResult BakeSkinnedMeshToVat(
        GameObject sourceModelAsset,
        GameObject motionSourceAsset,
        IReadOnlyList<AnimationClip> clips,
        string outputFolder,
        string outputName,
        Shader shader)
    {
        if (clips == null)
            throw new ArgumentNullException(nameof(clips));

        List<ClipSource> clipSources = new List<ClipSource>(clips.Count);
        for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
        {
            clipSources.Add(new ClipSource
            {
                Clip = clips[clipIndex],
                MotionSourceAsset = motionSourceAsset,
                ClipNameOverride = clips[clipIndex] != null ? clips[clipIndex].name : string.Empty
            });
        }

        return BakeSkinnedMeshToVat(
            sourceModelAsset,
            clipSources,
            outputFolder,
            outputName,
            shader);
    }

    public static BakeResult BakeSkinnedMeshToVat(
        GameObject sourceModelAsset,
        IReadOnlyList<ClipSource> clipSources,
        string outputFolder,
        string outputName,
        Shader shader)
    {
        if (sourceModelAsset == null)
            throw new ArgumentNullException(nameof(sourceModelAsset));

        if (clipSources == null || clipSources.Count == 0)
            throw new InvalidOperationException("No animation clips were provided for VAT baking.");

        if (shader == null)
            throw new InvalidOperationException("VAT shader is missing.");

        EnsureFolder(outputFolder);
        string materialFolder = $"{outputFolder}/Materials";
        EnsureFolder(materialFolder);

        GameObject sampleRoot = null;
        Mesh bakedBoundsMesh = null;
        Dictionary<GameObject, GameObject> motionRootsByAsset = null;

        try
        {
            sampleRoot = InstantiateAsset(sourceModelAsset);
            sampleRoot.hideFlags = HideFlags.HideAndDontSave;

            motionRootsByAsset = InstantiateMotionRoots(clipSources, sampleRoot);

            SkinnedMeshRenderer sampleRenderer = sampleRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (sampleRenderer == null)
                throw new InvalidOperationException($"No SkinnedMeshRenderer was found in asset `{sourceModelAsset.name}`.");

            sampleRenderer.updateWhenOffscreen = true;

            Mesh sourceMesh = sampleRenderer.sharedMesh;
            if (sourceMesh == null)
                throw new InvalidOperationException("The source SkinnedMeshRenderer does not have a shared mesh.");

            if (sourceMesh.bindposes == null || sourceMesh.bindposes.Length == 0)
                throw new InvalidOperationException("The source mesh does not contain bind poses.");

            if (sourceMesh.boneWeights == null || sourceMesh.boneWeights.Length != sourceMesh.vertexCount)
                throw new InvalidOperationException("The source mesh does not contain valid bone weights for every vertex.");

            if (sourceMesh.bindposes.Length != sampleRenderer.bones.Length)
                throw new InvalidOperationException("The mesh bind pose count does not match the renderer bone count.");

            List<Vector4> existingUvBuffer = new List<Vector4>();
            sourceMesh.GetUVs(BonePixelOffsetUvChannel, existingUvBuffer);
            if (existingUvBuffer.Count > 0)
                Debug.LogWarning($"VAT baker will overwrite UV{BonePixelOffsetUvChannel + 1} on `{sourceMesh.name}`.");

            existingUvBuffer.Clear();
            sourceMesh.GetUVs(BoneWeightUvChannel, existingUvBuffer);
            if (existingUvBuffer.Count > 0)
                Debug.LogWarning($"VAT baker will overwrite UV{BoneWeightUvChannel + 1} on `{sourceMesh.name}`.");

            Mesh bakedMesh = CreateBakedMesh(sourceMesh, outputName);
            bakedBoundsMesh = new Mesh { name = $"{outputName}_BoundsScratch" };

            CrowdVatAnimationAsset.ClipInfo[] clipInfos;
            Bounds bakedBounds;
            Texture2D boneTexture = BakeBoneAnimationTexture(
                sampleRoot,
                sampleRenderer,
                clipSources,
                motionRootsByAsset,
                bakedBoundsMesh,
                out clipInfos,
                out bakedBounds,
                outputName);

            bakedMesh.bounds = bakedBounds;

            Material[] materials = CreateMaterials(sampleRenderer.sharedMaterials, shader, materialFolder, outputName);

            string meshPath = $"{outputFolder}/{outputName}_Mesh.asset";
            string texturePath = $"{outputFolder}/{outputName}_BoneTex.asset";
            string assetPath = $"{outputFolder}/{outputName}.asset";
            string prefabPath = $"{outputFolder}/{outputName}.prefab";

            ReplaceAsset(meshPath, bakedMesh);
            ReplaceAsset(texturePath, boneTexture);

            CrowdVatAnimationAsset animationAsset = ScriptableObject.CreateInstance<CrowdVatAnimationAsset>();
            animationAsset.name = outputName;
            animationAsset.SetData(
                AssetDatabase.LoadAssetAtPath<Mesh>(meshPath),
                AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath),
                materials,
                bakedBounds,
                sampleRenderer.bones.Length,
                BakeFrameRate,
                clipInfos);
            ReplaceAsset(assetPath, animationAsset);

            CrowdVatAnimationAsset savedAnimationAsset = AssetDatabase.LoadAssetAtPath<CrowdVatAnimationAsset>(assetPath);
            if (savedAnimationAsset == null)
                throw new InvalidOperationException($"Failed to save VAT animation asset at `{assetPath}`.");

            GameObject prefab = CreateOrReplacePrefab(
                sampleRoot,
                sampleRenderer,
                savedAnimationAsset,
                prefabPath,
                outputName,
                clipInfos.FirstOrDefault().Name);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            return new BakeResult
            {
                AnimationAsset = savedAnimationAsset,
                Prefab = prefab,
                Mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath),
                BoneTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath),
                Materials = materials
            };
        }
        finally
        {
            if (bakedBoundsMesh != null)
                UnityEngine.Object.DestroyImmediate(bakedBoundsMesh);

            if (sampleRoot != null)
                UnityEngine.Object.DestroyImmediate(sampleRoot);

            ReleaseMotionRoots(motionRootsByAsset, sampleRoot);
        }
    }

    private static GameObject InstantiateAsset(GameObject sourceModelAsset)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(sourceModelAsset) as GameObject;
        if (instance != null)
            return instance;

        return UnityEngine.Object.Instantiate(sourceModelAsset);
    }

    private static Dictionary<GameObject, GameObject> InstantiateMotionRoots(
        IReadOnlyList<ClipSource> clipSources,
        GameObject sampleRoot)
    {
        Dictionary<GameObject, GameObject> motionRootsByAsset = new Dictionary<GameObject, GameObject>();
        if (clipSources == null)
            return motionRootsByAsset;

        for (int clipIndex = 0; clipIndex < clipSources.Count; clipIndex++)
        {
            ClipSource clipSource = clipSources[clipIndex];
            if (clipSource == null || clipSource.MotionSourceAsset == null)
                continue;

            if (motionRootsByAsset.ContainsKey(clipSource.MotionSourceAsset))
                continue;

            GameObject motionRoot = InstantiateAsset(clipSource.MotionSourceAsset);
            if (motionRoot != null && motionRoot != sampleRoot)
                motionRoot.hideFlags = HideFlags.HideAndDontSave;

            motionRootsByAsset.Add(clipSource.MotionSourceAsset, motionRoot);
        }

        return motionRootsByAsset;
    }

    private static void ReleaseMotionRoots(
        IReadOnlyDictionary<GameObject, GameObject> motionRootsByAsset,
        GameObject sampleRoot)
    {
        if (motionRootsByAsset == null)
            return;

        foreach (GameObject motionRoot in motionRootsByAsset.Values.Distinct())
        {
            if (motionRoot == null || motionRoot == sampleRoot)
                continue;

            UnityEngine.Object.DestroyImmediate(motionRoot);
        }
    }

    private static Mesh CreateBakedMesh(Mesh sourceMesh, string outputName)
    {
        Mesh bakedMesh = UnityEngine.Object.Instantiate(sourceMesh);
        bakedMesh.name = $"{outputName}_Mesh";

        BoneWeight[] boneWeights = sourceMesh.boneWeights;
        Vector4[] bonePixelOffsets = new Vector4[sourceMesh.vertexCount];
        Vector4[] normalizedWeights = new Vector4[sourceMesh.vertexCount];

        for (int vertexIndex = 0; vertexIndex < sourceMesh.vertexCount; vertexIndex++)
        {
            BoneWeight weight = boneWeights[vertexIndex];
            bonePixelOffsets[vertexIndex] = new Vector4(
                weight.boneIndex0 * RowsPerBone,
                weight.boneIndex1 * RowsPerBone,
                weight.boneIndex2 * RowsPerBone,
                weight.boneIndex3 * RowsPerBone);

            normalizedWeights[vertexIndex] = new Vector4(
                weight.weight0,
                weight.weight1,
                weight.weight2,
                weight.weight3);
        }

        bakedMesh.SetUVs(BonePixelOffsetUvChannel, bonePixelOffsets);
        bakedMesh.SetUVs(BoneWeightUvChannel, normalizedWeights);

        return bakedMesh;
    }

    private static Texture2D BakeBoneAnimationTexture(
        GameObject sampleRoot,
        SkinnedMeshRenderer sampleRenderer,
        IReadOnlyList<ClipSource> clipSources,
        IReadOnlyDictionary<GameObject, GameObject> motionRootsByAsset,
        Mesh bakedBoundsMesh,
        out CrowdVatAnimationAsset.ClipInfo[] clipInfos,
        out Bounds bakedBounds,
        string outputName)
    {
        List<ClipBakeInfo> bakeInfos = BuildClipBakeInfos(clipSources, motionRootsByAsset, sampleRoot);
        int totalFrameCount = bakeInfos.Sum(item => item.FrameCount);
        int textureWidth = sampleRenderer.bones.Length * RowsPerBone;

        Texture2D boneTexture = new Texture2D(textureWidth, totalFrameCount, TextureFormat.RGBAHalf, false, true)
        {
            name = $"{outputName}_BoneTex",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0
        };

        bool hasBounds = false;
        bakedBounds = default;
        Matrix4x4 rootWorldToLocal = sampleRenderer.transform.worldToLocalMatrix;
        Matrix4x4[] bindPoses = sampleRenderer.sharedMesh.bindposes;
        Dictionary<GameObject, PoseCopyPair[]> poseCopyPairsByMotionRoot = new Dictionary<GameObject, PoseCopyPair[]>();

        try
        {
            int frameCursor = 0;
            clipInfos = new CrowdVatAnimationAsset.ClipInfo[bakeInfos.Count];

            for (int clipIndex = 0; clipIndex < bakeInfos.Count; clipIndex++)
            {
                ClipBakeInfo bakeInfo = bakeInfos[clipIndex];
                if (!poseCopyPairsByMotionRoot.TryGetValue(bakeInfo.MotionRoot, out PoseCopyPair[] poseCopyPairs))
                {
                    poseCopyPairs = BuildPoseCopyPairs(bakeInfo.MotionRoot, sampleRoot, sampleRenderer);
                    poseCopyPairsByMotionRoot.Add(bakeInfo.MotionRoot, poseCopyPairs);
                }

                bool hasClipBounds = false;
                Bounds clipBounds = default;
                for (int localFrameIndex = 0; localFrameIndex < bakeInfo.FrameCount; localFrameIndex++)
                {
                    float sampleTime = CalculateSampleTime(bakeInfo, localFrameIndex);
                    bakeInfo.Clip.SampleAnimation(bakeInfo.MotionRoot, sampleTime);
                    ApplyPoseCopyPairs(poseCopyPairs);

                    for (int boneIndex = 0; boneIndex < sampleRenderer.bones.Length; boneIndex++)
                    {
                        Transform bone = sampleRenderer.bones[boneIndex];
                        Matrix4x4 skinMatrix = rootWorldToLocal * bone.localToWorldMatrix * bindPoses[boneIndex];

                        int pixelX = boneIndex * RowsPerBone;
                        int pixelY = frameCursor + localFrameIndex;
                        boneTexture.SetPixel(pixelX, pixelY, EncodeRow(skinMatrix, 0));
                        boneTexture.SetPixel(pixelX + 1, pixelY, EncodeRow(skinMatrix, 1));
                        boneTexture.SetPixel(pixelX + 2, pixelY, EncodeRow(skinMatrix, 2));
                    }

                    sampleRenderer.BakeMesh(bakedBoundsMesh);
                    Bounds frameBounds = bakedBoundsMesh.bounds;
                    if (!hasClipBounds)
                    {
                        clipBounds = frameBounds;
                        hasClipBounds = true;
                    }
                    else
                    {
                        clipBounds.Encapsulate(frameBounds.min);
                        clipBounds.Encapsulate(frameBounds.max);
                    }

                    if (!hasBounds)
                    {
                        bakedBounds = frameBounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bakedBounds.Encapsulate(frameBounds.min);
                        bakedBounds.Encapsulate(frameBounds.max);
                    }
                }

                clipInfos[clipIndex] = hasClipBounds
                    ? new CrowdVatAnimationAsset.ClipInfo(
                        bakeInfo.ClipName,
                        frameCursor,
                        bakeInfo.FrameCount,
                        bakeInfo.Clip.length,
                        bakeInfo.Loop,
                        clipBounds)
                    : new CrowdVatAnimationAsset.ClipInfo(
                        bakeInfo.ClipName,
                        frameCursor,
                        bakeInfo.FrameCount,
                        bakeInfo.Clip.length,
                        bakeInfo.Loop);

                frameCursor += bakeInfo.FrameCount;
            }
        }
        finally
        {
        }

        boneTexture.Apply(false, false);
        return boneTexture;
    }

    private static PoseCopyPair[] BuildPoseCopyPairs(GameObject motionRoot, GameObject sampleRoot, SkinnedMeshRenderer sampleRenderer)
    {
        if (motionRoot == null || sampleRoot == null || sampleRenderer == null)
            return Array.Empty<PoseCopyPair>();

        Dictionary<string, Transform> motionByExactName = BuildTransformLookup(motionRoot.GetComponentsInChildren<Transform>(true), normalizeName: false);
        Dictionary<string, Transform> motionByNormalizedName = BuildTransformLookup(motionRoot.GetComponentsInChildren<Transform>(true), normalizeName: true);
        Transform[] targetTransforms = sampleRoot.GetComponentsInChildren<Transform>(true);
        List<PoseCopyPair> result = new List<PoseCopyPair>(sampleRenderer.bones.Length);

        for (int index = 0; index < targetTransforms.Length; index++)
        {
            Transform target = targetTransforms[index];
            if (target == null)
                continue;

            if (!TryResolveMotionTransform(target.name, motionByExactName, motionByNormalizedName, out Transform source))
                continue;

            bool copyPosition = string.Equals(NormalizeTransformName(target.name), "Hips", StringComparison.OrdinalIgnoreCase);
            result.Add(new PoseCopyPair(source, target, copyPosition, source.localPosition, target.localPosition));
        }

        int mappedBoneCount = sampleRenderer.bones.Count(
            bone => bone != null && result.Any(pair => pair.Target == bone));

        if (mappedBoneCount == 0)
            throw new InvalidOperationException("VAT motion source could not be mapped onto the target skeleton.");

        Debug.Log(
            $"Crowd VAT pose mapping prepared: mappedBones={mappedBoneCount}/{sampleRenderer.bones.Length}, " +
            $"mappedTransforms={result.Count}, motionRoot={motionRoot.name}, targetRoot={sampleRoot.name}");

        return result.ToArray();
    }

    private static Dictionary<string, Transform> BuildTransformLookup(IEnumerable<Transform> transforms, bool normalizeName)
    {
        Dictionary<string, Transform> lookup = new Dictionary<string, Transform>(StringComparer.Ordinal);

        foreach (Transform transform in transforms)
        {
            if (transform == null)
                continue;

            string key = normalizeName ? NormalizeTransformName(transform.name) : transform.name;
            if (string.IsNullOrWhiteSpace(key) || lookup.ContainsKey(key))
                continue;

            lookup.Add(key, transform);
        }

        return lookup;
    }

    private static bool TryResolveMotionTransform(
        string targetName,
        IReadOnlyDictionary<string, Transform> motionByExactName,
        IReadOnlyDictionary<string, Transform> motionByNormalizedName,
        out Transform source)
    {
        if (motionByExactName.TryGetValue(targetName, out source))
            return source != null;

        string normalizedTargetName = NormalizeTransformName(targetName);
        if (!string.IsNullOrWhiteSpace(normalizedTargetName) &&
            motionByNormalizedName.TryGetValue(normalizedTargetName, out source))
            return source != null;

        source = null;
        return false;
    }

    private static void ApplyPoseCopyPairs(PoseCopyPair[] poseCopyPairs)
    {
        for (int index = 0; index < poseCopyPairs.Length; index++)
        {
            PoseCopyPair pair = poseCopyPairs[index];
            if (pair.Source == null || pair.Target == null)
                continue;

            if (pair.CopyPosition)
            {
                Vector3 sourceDelta = pair.Source.localPosition - pair.SourceReferenceLocalPosition;
                pair.Target.localPosition = new Vector3(
                    pair.TargetReferenceLocalPosition.x,
                    pair.TargetReferenceLocalPosition.y + sourceDelta.y,
                    pair.TargetReferenceLocalPosition.z);
            }

            pair.Target.localRotation = pair.Source.localRotation;
        }
    }

    private static string NormalizeTransformName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        int separatorIndex = name.LastIndexOf(':');
        if (separatorIndex >= 0 && separatorIndex < name.Length - 1)
            return name[(separatorIndex + 1)..];

        return name;
    }

    private static Material[] CreateMaterials(Material[] sourceMaterials, Shader shader, string materialFolder, string outputName)
    {
        int materialCount = sourceMaterials != null && sourceMaterials.Length > 0 ? sourceMaterials.Length : 1;
        Material[] materials = new Material[materialCount];

        for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
        {
            Material sourceMaterial = sourceMaterials != null && materialIndex < sourceMaterials.Length
                ? sourceMaterials[materialIndex]
                : null;

            Material material = new Material(shader)
            {
                name = $"{outputName}_{materialIndex:00}"
            };

            CopyMaterialProperties(sourceMaterial, material);

            string materialPath = $"{materialFolder}/{SanitizeAssetName(material.name)}.mat";
            ReplaceAsset(materialPath, material);
            materials[materialIndex] = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        return materials;
    }

    private static void CopyMaterialProperties(Material sourceMaterial, Material destinationMaterial)
    {
        if (destinationMaterial == null)
            return;

        destinationMaterial.SetColor("_BaseColor", Color.white);
        destinationMaterial.SetColor("_SpecColor", Color.white);
        destinationMaterial.SetFloat("_AlphaClip", 0.0f);
        destinationMaterial.SetFloat("_Cutoff", 0.5f);
        destinationMaterial.SetFloat("_BumpScale", 1.0f);
        destinationMaterial.SetFloat("_Smoothness", 1.0f);
        destinationMaterial.SetFloat("_SpecularStrength", 0.12f);
        destinationMaterial.SetFloat("_UseNormalMap", 0.0f);
        destinationMaterial.SetFloat("_UseSpecGlossMap", 0.0f);

        if (sourceMaterial == null)
            return;

        if (TryGetTexture(sourceMaterial, out Texture texture, out Vector2 scale, out Vector2 offset))
        {
            destinationMaterial.SetTexture("_BaseMap", texture);
            destinationMaterial.SetTextureScale("_BaseMap", scale);
            destinationMaterial.SetTextureOffset("_BaseMap", offset);
        }

        if (TryGetColor(sourceMaterial, out Color baseColor))
            destinationMaterial.SetColor("_BaseColor", baseColor);

        if (sourceMaterial.HasProperty("_Cutoff"))
            destinationMaterial.SetFloat("_Cutoff", sourceMaterial.GetFloat("_Cutoff"));

        if (sourceMaterial.HasProperty("_BumpMap"))
        {
            Texture normalTexture = sourceMaterial.GetTexture("_BumpMap");
            destinationMaterial.SetTexture("_BumpMap", normalTexture);
            destinationMaterial.SetFloat("_UseNormalMap", normalTexture != null ? 1.0f : 0.0f);
        }

        if (sourceMaterial.HasProperty("_BumpScale"))
            destinationMaterial.SetFloat("_BumpScale", sourceMaterial.GetFloat("_BumpScale"));

        if (sourceMaterial.HasProperty("_SpecGlossMap"))
        {
            Texture specGlossTexture = sourceMaterial.GetTexture("_SpecGlossMap");
            destinationMaterial.SetTexture("_SpecGlossMap", specGlossTexture);
            destinationMaterial.SetFloat("_UseSpecGlossMap", specGlossTexture != null ? 1.0f : 0.0f);
        }

        if (sourceMaterial.HasProperty("_SpecColor"))
            destinationMaterial.SetColor("_SpecColor", sourceMaterial.GetColor("_SpecColor"));

        if (sourceMaterial.HasProperty("_Smoothness"))
            destinationMaterial.SetFloat("_Smoothness", sourceMaterial.GetFloat("_Smoothness"));

        if (sourceMaterial.HasProperty("_SpecularStrength"))
            destinationMaterial.SetFloat("_SpecularStrength", sourceMaterial.GetFloat("_SpecularStrength"));

        bool alphaClip = sourceMaterial.IsKeywordEnabled("_ALPHATEST_ON")
            || (sourceMaterial.HasProperty("_AlphaClip") && sourceMaterial.GetFloat("_AlphaClip") > 0.5f)
            || sourceMaterial.renderQueue == (int)RenderQueue.AlphaTest;
        destinationMaterial.SetFloat("_AlphaClip", alphaClip ? 1.0f : 0.0f);
    }

    private static bool TryGetTexture(Material material, out Texture texture, out Vector2 scale, out Vector2 offset)
    {
        string[] candidates =
        {
            "_BaseMap",
            "_MainTex"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string propertyName = candidates[i];
            if (!material.HasProperty(propertyName))
                continue;

            texture = material.GetTexture(propertyName);
            scale = material.GetTextureScale(propertyName);
            offset = material.GetTextureOffset(propertyName);
            return texture != null;
        }

        texture = null;
        scale = Vector2.one;
        offset = Vector2.zero;
        return false;
    }

    private static bool TryGetColor(Material material, out Color color)
    {
        string[] candidates =
        {
            "_BaseColor",
            "_Color"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string propertyName = candidates[i];
            if (!material.HasProperty(propertyName))
                continue;

            color = material.GetColor(propertyName);
            return true;
        }

        color = Color.white;
        return false;
    }

    private static GameObject CreateOrReplacePrefab(
        GameObject sampleRoot,
        SkinnedMeshRenderer sampleRenderer,
        CrowdVatAnimationAsset animationAsset,
        string prefabPath,
        string outputName,
        string defaultClipName)
    {
        Bounds combinedBounds = CalculateCombinedBounds(sampleRoot);
        Vector3 alignmentOffset = new Vector3(0.0f, -combinedBounds.min.y, 0.0f);
        Matrix4x4 relativeMatrix = sampleRoot.transform.worldToLocalMatrix * sampleRenderer.transform.localToWorldMatrix;

        GameObject root = new GameObject(outputName);
        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            GameObject renderRoot = new GameObject("VatRenderer");
            renderRoot.transform.SetParent(root.transform, false);
            renderRoot.transform.localPosition = ExtractPosition(relativeMatrix) + alignmentOffset;
            renderRoot.transform.localRotation = ExtractRotation(relativeMatrix);
            renderRoot.transform.localScale = ExtractScale(relativeMatrix);

            MeshFilter meshFilter = renderRoot.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = renderRoot.AddComponent<MeshRenderer>();
            CrowdVatPlayer player = root.AddComponent<CrowdVatPlayer>();
            player.Configure(animationAsset, meshRenderer, meshFilter, defaultClipName);

            if (AssetDatabase.DeleteAsset(prefabPath))
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"Failed to save VAT prefab at `{prefabPath}`.");

            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static List<ClipBakeInfo> BuildClipBakeInfos(
        IReadOnlyList<ClipSource> clipSources,
        IReadOnlyDictionary<GameObject, GameObject> motionRootsByAsset,
        GameObject sampleRoot)
    {
        List<ClipBakeInfo> bakeInfos = new List<ClipBakeInfo>(clipSources.Count);
        for (int clipIndex = 0; clipIndex < clipSources.Count; clipIndex++)
        {
            ClipSource clipSource = clipSources[clipIndex];
            AnimationClip clip = clipSource?.Clip;
            if (clip == null)
                continue;

            if (IsPreviewClip(clip))
                continue;

            bool loop = clip.isLooping;
            int frameCount = CalculateFrameCount(clip, loop);
            GameObject motionRoot = ResolveMotionRoot(clipSource, motionRootsByAsset, sampleRoot);
            string clipName = ResolveClipName(clipSource, clipIndex);
            bakeInfos.Add(new ClipBakeInfo(clip, motionRoot, clipName, frameCount, loop));
        }

        if (bakeInfos.Count == 0)
            throw new InvalidOperationException("No valid animation clips remained after filtering preview clips.");

        return bakeInfos;
    }

    private static int CalculateFrameCount(AnimationClip clip, bool loop)
    {
        int baseFrameCount = Mathf.CeilToInt(Mathf.Max(clip.length, 1.0f / BakeFrameRate) * BakeFrameRate);
        if (loop)
            return Mathf.Max(2, baseFrameCount);

        return Mathf.Max(2, baseFrameCount + 1);
    }

    private static GameObject ResolveMotionRoot(
        ClipSource clipSource,
        IReadOnlyDictionary<GameObject, GameObject> motionRootsByAsset,
        GameObject sampleRoot)
    {
        if (clipSource == null || clipSource.MotionSourceAsset == null)
            return sampleRoot;

        if (motionRootsByAsset != null &&
            motionRootsByAsset.TryGetValue(clipSource.MotionSourceAsset, out GameObject motionRoot) &&
            motionRoot != null)
        {
            return motionRoot;
        }

        return sampleRoot;
    }

    private static string ResolveClipName(ClipSource clipSource, int clipIndex)
    {
        if (!string.IsNullOrWhiteSpace(clipSource?.ClipNameOverride))
            return clipSource.ClipNameOverride;

        if (clipSource?.Clip != null)
            return clipSource.Clip.name;

        return $"Clip_{clipIndex:00}";
    }

    private static float CalculateSampleTime(ClipBakeInfo bakeInfo, int localFrameIndex)
    {
        if (bakeInfo.FrameCount <= 1)
            return 0.0f;

        if (bakeInfo.Loop)
            return (float)localFrameIndex / bakeInfo.FrameCount * bakeInfo.Clip.length;

        return (float)localFrameIndex / (bakeInfo.FrameCount - 1) * bakeInfo.Clip.length;
    }

    private static Bounds CalculateCombinedBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);

        return bounds;
    }

    private static Color EncodeRow(Matrix4x4 matrix, int rowIndex)
    {
        return rowIndex switch
        {
            0 => new Color(matrix.m00, matrix.m01, matrix.m02, matrix.m03),
            1 => new Color(matrix.m10, matrix.m11, matrix.m12, matrix.m13),
            2 => new Color(matrix.m20, matrix.m21, matrix.m22, matrix.m23),
            _ => throw new ArgumentOutOfRangeException(nameof(rowIndex), rowIndex, "Only matrix rows 0-2 are stored in the VAT texture.")
        };
    }

    private static void ReplaceAsset(string assetPath, UnityEngine.Object asset)
    {
        string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(directory))
            EnsureFolder(directory);

        if (AssetDatabase.DeleteAsset(assetPath))
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        AssetDatabase.CreateAsset(asset, assetPath);
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string[] segments = assetPath.Split('/');
        string current = segments[0];

        for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
        {
            string next = $"{current}/{segments[segmentIndex]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[segmentIndex]);

            current = next;
        }
    }

    private static string SanitizeAssetName(string assetName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] sanitized = assetName
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray();
        return new string(sanitized);
    }

    private static Vector3 ExtractPosition(Matrix4x4 matrix)
    {
        return new Vector3(matrix.m03, matrix.m13, matrix.m23);
    }

    private static Quaternion ExtractRotation(Matrix4x4 matrix)
    {
        Vector3 forward = new Vector3(matrix.m02, matrix.m12, matrix.m22).normalized;
        Vector3 upwards = new Vector3(matrix.m01, matrix.m11, matrix.m21).normalized;

        if (forward.sqrMagnitude <= 1e-6f || upwards.sqrMagnitude <= 1e-6f)
            return Quaternion.identity;

        return Quaternion.LookRotation(forward, upwards);
    }

    private static Vector3 ExtractScale(Matrix4x4 matrix)
    {
        Vector3 column0 = new Vector3(matrix.m00, matrix.m10, matrix.m20);
        Vector3 column1 = new Vector3(matrix.m01, matrix.m11, matrix.m21);
        Vector3 column2 = new Vector3(matrix.m02, matrix.m12, matrix.m22);
        return new Vector3(column0.magnitude, column1.magnitude, column2.magnitude);
    }

    private static bool IsPreviewClip(AnimationClip clip)
    {
        if (clip == null)
            return true;

        return clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase);
    }

    private readonly struct ClipBakeInfo
    {
        public ClipBakeInfo(
            AnimationClip clip,
            GameObject motionRoot,
            string clipName,
            int frameCount,
            bool loop)
        {
            Clip = clip;
            MotionRoot = motionRoot;
            ClipName = clipName;
            FrameCount = frameCount;
            Loop = loop;
        }

        public AnimationClip Clip { get; }
        public GameObject MotionRoot { get; }
        public string ClipName { get; }
        public int FrameCount { get; }
        public bool Loop { get; }
    }

    private readonly struct PoseCopyPair
    {
        public PoseCopyPair(Transform source, Transform target, bool copyPosition, Vector3 sourceReferenceLocalPosition, Vector3 targetReferenceLocalPosition)
        {
            Source = source;
            Target = target;
            CopyPosition = copyPosition;
            SourceReferenceLocalPosition = sourceReferenceLocalPosition;
            TargetReferenceLocalPosition = targetReferenceLocalPosition;
        }

        public Transform Source { get; }
        public Transform Target { get; }
        public bool CopyPosition { get; }
        public Vector3 SourceReferenceLocalPosition { get; }
        public Vector3 TargetReferenceLocalPosition { get; }
    }
}
