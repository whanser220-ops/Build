using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class QianxiaRigDiagnostics
{
    private const string MotionSourceAssetPath = "Assets/Game/Characters/Qianxia/Art/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx";
    private const string VatTargetAssetPath = QianxiaLodPrefabBuilder.Lod0AssetPath;
    private const string WalkClipAssetPath = "Assets/Game/Characters/Qianxia/Art/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx";
    private const string BoneTextureAssetPath = "Assets/Game/Characters/Qianxia/Runtime/Generated/VAT/QianxiaCrowdLod0Vat_BoneTex.asset";

    [MenuItem("Tools/Qianxia/Run Rig Diagnostics")]
    public static void RunRigDiagnosticsMenu()
    {
        Debug.Log(RunRigDiagnostics());
    }

    public static string RunRigDiagnostics()
    {
        GameObject motionSourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MotionSourceAssetPath);
        GameObject vatTargetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(VatTargetAssetPath);
        AnimationClip walkClip = AssetDatabase.LoadAllAssetsAtPath(WalkClipAssetPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip => clip != null && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
        Texture2D boneTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BoneTextureAssetPath);

        if (motionSourceAsset == null || vatTargetAsset == null || walkClip == null || boneTexture == null)
            throw new InvalidOperationException("Required Qianxia rig diagnostics assets are missing.");

        GameObject motionRoot = null;
        GameObject vatRoot = null;

        try
        {
            motionRoot = InstantiateAsset(motionSourceAsset);
            vatRoot = InstantiateAsset(vatTargetAsset);

            SkinnedMeshRenderer motionRenderer = motionRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer vatRenderer = vatRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Animator motionAnimator = motionRoot.GetComponent<Animator>();
            Avatar motionAvatar = AssetDatabase.LoadAllAssetsAtPath(MotionSourceAssetPath)
                .OfType<Avatar>()
                .FirstOrDefault(avatar => avatar != null && avatar.isValid);
            Avatar vatAvatar = AssetDatabase.LoadAllAssetsAtPath(VatTargetAssetPath)
                .OfType<Avatar>()
                .FirstOrDefault(avatar => avatar != null && avatar.isValid);

            if (motionRenderer == null || vatRenderer == null)
                throw new InvalidOperationException("Diagnostics could not find a SkinnedMeshRenderer.");

            Dictionary<string, Transform> motionBones = motionRenderer.bones
                .Where(bone => bone != null)
                .GroupBy(bone => NormalizeName(bone.name))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            Dictionary<string, Transform> vatBones = vatRenderer.bones
                .Where(bone => bone != null)
                .GroupBy(bone => NormalizeName(bone.name))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            List<string> missingInVat = motionBones.Keys.Where(name => !vatBones.ContainsKey(name)).OrderBy(name => name).ToList();
            List<string> missingInMotion = vatBones.Keys.Where(name => !motionBones.ContainsKey(name)).OrderBy(name => name).ToList();

            Matrix4x4[] beforeMatrices = SampleVatSkinMatrices(vatRenderer);
            walkClip.SampleAnimation(motionRoot, 0.0f);
            CopyPoseByName(motionBones, vatBones);
            Matrix4x4[] copiedStartMatrices = SampleVatSkinMatrices(vatRenderer);

            walkClip.SampleAnimation(motionRoot, Mathf.Min(0.33f, Mathf.Max(0.0f, walkClip.length * 0.5f)));
            CopyPoseByName(motionBones, vatBones);
            Matrix4x4[] copiedLaterMatrices = SampleVatSkinMatrices(vatRenderer);

            float bestDelta = -1.0f;
            int bestBoneIndex = -1;
            for (int i = 0; i < copiedStartMatrices.Length; i++)
            {
                float delta = MatrixDelta(copiedStartMatrices[i], copiedLaterMatrices[i]);
                if (delta > bestDelta)
                {
                    bestDelta = delta;
                    bestBoneIndex = i;
                }
            }

            float copiedVsBindDelta = 0.0f;
            for (int i = 0; i < Mathf.Min(beforeMatrices.Length, copiedStartMatrices.Length); i++)
                copiedVsBindDelta = Mathf.Max(copiedVsBindDelta, MatrixDelta(beforeMatrices[i], copiedStartMatrices[i]));

            int frameIndexA = 0;
            int frameIndexB = Mathf.Min(10, Mathf.Max(1, boneTexture.height - 1));
            int pixelX = Mathf.Max(0, bestBoneIndex) * 3;
            Vector4 storedA0 = ReadTextureRow(boneTexture, pixelX, frameIndexA);
            Vector4 storedB0 = ReadTextureRow(boneTexture, pixelX, frameIndexB);
            Vector4 expectedA0 = ToRow(copiedStartMatrices[Mathf.Max(0, bestBoneIndex)], 0);
            Vector4 expectedB0 = ToRow(copiedLaterMatrices[Mathf.Max(0, bestBoneIndex)], 0);

            return
                "Qianxia Rig Diagnostics\n" +
                $"motionSource={MotionSourceAssetPath}\n" +
                $"vatTarget={VatTargetAssetPath}\n" +
                $"clip={walkClip.name} length={walkClip.length:F3}\n" +
                $"motionAnimatorExists={(motionAnimator != null)} motionAvatarValid={(motionAvatar != null && motionAvatar.isValid)} motionAvatarHuman={(motionAvatar != null && motionAvatar.isHuman)}\n" +
                $"vatAvatarExists={(vatAvatar != null)} vatAvatarHuman={(vatAvatar != null && vatAvatar.isHuman)}\n" +
                $"motionBones={motionBones.Count} vatBones={vatBones.Count} matched={motionBones.Keys.Count(name => vatBones.ContainsKey(name))}\n" +
                $"missingInVatCount={missingInVat.Count} sampleMissingInVat={string.Join(", ", missingInVat.Take(12))}\n" +
                $"missingInMotionCount={missingInMotion.Count} sampleMissingInMotion={string.Join(", ", missingInMotion.Take(12))}\n" +
                $"copiedVsBindDelta={copiedVsBindDelta:F6}\n" +
                $"copiedPoseFrameDelta={bestDelta:F6} bestBoneIndex={bestBoneIndex} bestBoneName={(bestBoneIndex >= 0 && bestBoneIndex < vatRenderer.bones.Length ? vatRenderer.bones[bestBoneIndex].name : "<none>")}\n" +
                $"boneTextureFrameA={storedA0} expectedFrameA={expectedA0}\n" +
                $"boneTextureFrameB={storedB0} expectedFrameB={expectedB0}";
        }
        finally
        {
            if (motionRoot != null)
                UnityEngine.Object.DestroyImmediate(motionRoot);

            if (vatRoot != null)
                UnityEngine.Object.DestroyImmediate(vatRoot);
        }
    }

    private static GameObject InstantiateAsset(GameObject asset)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        if (instance != null)
            return instance;

        return UnityEngine.Object.Instantiate(asset);
    }

    private static void CopyPoseByName(
        IReadOnlyDictionary<string, Transform> motionBones,
        IReadOnlyDictionary<string, Transform> vatBones)
    {
        foreach (KeyValuePair<string, Transform> pair in vatBones)
        {
            if (!motionBones.TryGetValue(pair.Key, out Transform motionBone) || motionBone == null || pair.Value == null)
                continue;

            if (string.Equals(pair.Key, "Hips", StringComparison.OrdinalIgnoreCase))
            {
                Vector3 targetPosition = pair.Value.localPosition;
                pair.Value.localPosition = new Vector3(targetPosition.x, motionBone.localPosition.y, targetPosition.z);
            }

            pair.Value.localRotation = motionBone.localRotation;
        }
    }

    private static string NormalizeName(string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
            return string.Empty;

        int separatorIndex = boneName.LastIndexOf(':');
        if (separatorIndex >= 0 && separatorIndex < boneName.Length - 1)
            return boneName[(separatorIndex + 1)..];

        return boneName;
    }

    private static Matrix4x4[] SampleVatSkinMatrices(SkinnedMeshRenderer renderer)
    {
        Matrix4x4 rootWorldToLocal = renderer.transform.worldToLocalMatrix;
        Matrix4x4[] bindPoses = renderer.sharedMesh.bindposes;
        Matrix4x4[] result = new Matrix4x4[renderer.bones.Length];

        for (int boneIndex = 0; boneIndex < renderer.bones.Length; boneIndex++)
        {
            Transform bone = renderer.bones[boneIndex];
            result[boneIndex] = rootWorldToLocal * bone.localToWorldMatrix * bindPoses[boneIndex];
        }

        return result;
    }

    private static float MatrixDelta(Matrix4x4 lhs, Matrix4x4 rhs)
    {
        float delta = 0.0f;
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
                delta += Mathf.Abs(lhs[row, column] - rhs[row, column]);
        }

        return delta;
    }

    private static Vector4 ReadTextureRow(Texture2D texture, int pixelX, int pixelY)
    {
        Color pixel = texture.GetPixel(pixelX, pixelY);
        return new Vector4(pixel.r, pixel.g, pixel.b, pixel.a);
    }

    private static Vector4 ToRow(Matrix4x4 matrix, int rowIndex)
    {
        return rowIndex switch
        {
            0 => new Vector4(matrix.m00, matrix.m01, matrix.m02, matrix.m03),
            1 => new Vector4(matrix.m10, matrix.m11, matrix.m12, matrix.m13),
            2 => new Vector4(matrix.m20, matrix.m21, matrix.m22, matrix.m23),
            _ => Vector4.zero
        };
    }
}
