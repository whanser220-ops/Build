using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class QianxiaLodPrefabBuilder
{
    public const string Lod0AssetPath = "Assets/Game/Characters/Qianxia/Art/Meshs/LOD2.fbx";
    public const string Lod1AssetPath = "Assets/Game/Characters/Qianxia/Art/Meshs/crowd1000.fbx";
    public const string Lod2AssetPath = "Assets/Game/Characters/Qianxia/Art/Meshs/crowd500.fbx";
    public const string LodPrefabPath = "Assets/Game/Characters/Qianxia/Runtime/Generated/QianxiaLodCharacter.prefab";

    private const string GeneratedFolder = "Assets/Game/Characters/Qianxia/Runtime/Generated";
    private const string AnimatorControllerPath = GeneratedFolder + "/QianxiaLocomotion.controller";
    private const string BuildRequestFile = ".workspace/artifacts/qianxia-lod-build.request";
    private const string BuildResultFile = ".workspace/artifacts/qianxia-lod-build.result.txt";
    private const float Lod0TransitionHeight = 0.6f;
    private const float Lod1TransitionHeight = 0.25f;
    private const float Lod2TransitionHeight = 0.02f;

    [MenuItem("Tools/Qianxia/Create Or Replace LOD Prefab")]
    public static void CreateOrReplaceQianxiaLodPrefab()
    {
        CreateOrReplaceQianxiaLodPrefabInternal(true);
    }

    public static string GetBuildRequestPath()
    {
        return Path.Combine(GetProjectRoot(), BuildRequestFile);
    }

    public static string GetBuildResultPath()
    {
        return Path.Combine(GetProjectRoot(), BuildResultFile);
    }

    public static GameObject CreateOrReplaceQianxiaLodPrefabInternal(bool logToConsole)
    {
        EnsureFolder(GeneratedFolder);

        GameObject lod0Asset = LoadModelAsset(Lod0AssetPath);
        GameObject lod1Asset = LoadModelAsset(Lod1AssetPath);
        GameObject lod2Asset = LoadModelAsset(Lod2AssetPath);
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorControllerPath);

        GameObject root = new GameObject("QianxiaLodCharacter");
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        try
        {
            LODGroup lodGroup = root.AddComponent<LODGroup>();
            QianxiaLodCollection lodCollection = root.AddComponent<QianxiaLodCollection>();

            GameObject lod0Instance = InstantiateModel(root.transform, lod0Asset, "LOD0");
            GameObject lod1Instance = InstantiateModel(root.transform, lod1Asset, "LOD1");
            GameObject lod2Instance = InstantiateModel(root.transform, lod2Asset, "LOD2");

            Vector3 sharedOffset = CalculateSharedAlignmentOffset(lod0Instance);
            ApplyLocalTransform(lod0Instance.transform, sharedOffset);
            ApplyLocalTransform(lod1Instance.transform, sharedOffset);
            ApplyLocalTransform(lod2Instance.transform, sharedOffset);

            ConfigureAnimator(lod0Instance, Lod0AssetPath, controller);
            ConfigureAnimator(lod1Instance, Lod1AssetPath, controller);
            ConfigureAnimator(lod2Instance, Lod2AssetPath, controller);

            Renderer[] lod0Renderers = CollectRenderers(lod0Instance);
            Renderer[] lod1Renderers = CollectRenderers(lod1Instance);
            Renderer[] lod2Renderers = CollectRenderers(lod2Instance);

            ValidateRenderers(lod0Renderers, Lod0AssetPath);
            ValidateRenderers(lod1Renderers, Lod1AssetPath);
            ValidateRenderers(lod2Renderers, Lod2AssetPath);

            lodGroup.fadeMode = LODFadeMode.None;
            lodGroup.animateCrossFading = false;
            lodGroup.SetLODs(new[]
            {
                new LOD(Lod0TransitionHeight, lod0Renderers),
                new LOD(Lod1TransitionHeight, lod1Renderers),
                new LOD(Lod2TransitionHeight, lod2Renderers)
            });
            lodGroup.RecalculateBounds();

            lodCollection.Configure(
                lodGroup,
                lod0Instance,
                Lod0TransitionHeight,
                lod1Instance,
                Lod1TransitionHeight,
                lod2Instance,
                Lod2TransitionHeight);

            if (AssetDatabase.DeleteAsset(LodPrefabPath))
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, LodPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"Failed to save prefab at path: {LodPrefabPath}");

            if (logToConsole)
                Debug.Log($"Qianxia LOD prefab created: {LodPrefabPath}");

            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static GameObject LoadModelAsset(string assetPath)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (modelAsset == null)
            throw new InvalidOperationException($"Model asset not found at path: {assetPath}");

        return modelAsset;
    }

    private static GameObject InstantiateModel(Transform parent, GameObject modelAsset, string lodName)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
        if (instance == null)
            throw new InvalidOperationException($"Unable to instantiate model prefab: {modelAsset.name}");

        instance.name = lodName;
        instance.transform.SetParent(parent, false);
        instance.transform.SetAsLastSibling();
        return instance;
    }

    private static void ApplyLocalTransform(Transform target, Vector3 localPosition)
    {
        target.localPosition = localPosition;
        target.localRotation = Quaternion.identity;
        target.localScale = Vector3.one;
    }

    private static Vector3 CalculateSharedAlignmentOffset(GameObject referenceInstance)
    {
        Bounds bounds = CalculateCombinedBounds(referenceInstance);
        return new Vector3(0.0f, -bounds.min.y, 0.0f);
    }

    private static void ConfigureAnimator(GameObject instance, string assetPath, RuntimeAnimatorController controller)
    {
        Animator animator = instance.GetComponent<Animator>();
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<Avatar>()
            .FirstOrDefault(item => item != null && item.isValid && item.isHuman);

        if (animator == null && avatar == null && controller == null)
            return;

        if (animator == null)
            animator = instance.AddComponent<Animator>();

        if (avatar != null)
            animator.avatar = avatar;

        if (controller != null)
            animator.runtimeAnimatorController = controller;

        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
    }

    private static Renderer[] CollectRenderers(GameObject root)
    {
        return root.GetComponentsInChildren<Renderer>(true);
    }

    private static void ValidateRenderers(Renderer[] renderers, string assetPath)
    {
        if (renderers == null || renderers.Length == 0)
            throw new InvalidOperationException($"No renderers were found in model asset: {assetPath}");
    }

    private static Bounds CalculateCombinedBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(Vector3.zero, new Vector3(1.0f, 1.8f, 1.0f));

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
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

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}

[InitializeOnLoad]
public static class QianxiaLodPrefabBootstrap
{
    private static double _nextCheckTime;

    static QianxiaLodPrefabBootstrap()
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
        string requestPath = QianxiaLodPrefabBuilder.GetBuildRequestPath();
        if (!File.Exists(requestPath))
            return;

        string resultPath = QianxiaLodPrefabBuilder.GetBuildResultPath();

        try
        {
            File.Delete(requestPath);
            GameObject prefab = QianxiaLodPrefabBuilder.CreateOrReplaceQianxiaLodPrefabInternal(false);
            File.WriteAllText(
                resultPath,
                $"SUCCESS{Environment.NewLine}{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{QianxiaLodPrefabBuilder.LodPrefabPath}{Environment.NewLine}{AssetDatabase.GetAssetPath(prefab)}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        catch (Exception exception)
        {
            File.WriteAllText(resultPath, $"FAILED{Environment.NewLine}{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}");
            Debug.LogException(exception);
        }
    }
}
