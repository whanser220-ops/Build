using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AngryMeshYooAssetReferenceConverter
{
    private const string LegacyGameAssetsRoot = "Assets/GameAssets/";
    private const string GameContentRoot = "Assets/Game/";
    private const string LegacyAngryMeshRoot = "Assets/ANGRY MESH/";
    private const string DefaultConvertedScenePath = "Assets/Scenes/Scenes 1/Scene_MeadowEnvironment_01_Summer_YooAsset.unity";

    [MenuItem("Tools/ANGRY MESH/YooAsset/Convert Enabled Scene Prefabs To YooAsset Loaders")]
    public static void ConvertEnabledScenePrefabsFromMenu()
    {
        ConvertFromArgs(new[] { "--yooasset-convert-enabled-scenes" });
    }

    public static void ConvertEnabledScenePrefabsFromCommandLine()
    {
        ConvertFromArgs(Environment.GetCommandLineArgs());
    }

    private static void ConvertFromArgs(string[] args)
    {
        bool dryRun = HasArgument(args, "--yooasset-convert-dry-run");
        bool enabledScenes = HasArgument(args, "--yooasset-convert-enabled-scenes") ||
                             !HasArgument(args, "--yooasset-convert-scene");
        bool updateBuildSettings = HasArgument(args, "--yooasset-convert-update-build-settings");
        int limit = ParseInt(GetArgumentValue(args, "--yooasset-convert-limit"), int.MaxValue);

        List<string> sourceScenes = new List<string>();
        string explicitScene = NormalizeAssetPath(GetArgumentValue(args, "--yooasset-convert-scene"));
        if (!string.IsNullOrWhiteSpace(explicitScene))
            sourceScenes.Add(explicitScene);

        if (enabledScenes)
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                    sourceScenes.Add(NormalizeAssetPath(scene.path));
            }
        }

        sourceScenes = sourceScenes
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sourceScenes.Count == 0)
            throw new InvalidOperationException("No scene path was provided and no enabled scene was found.");

        List<SceneConversionResult> results = new List<SceneConversionResult>();
        for (int index = 0; index < sourceScenes.Count; index++)
        {
            string destinationScene = NormalizeAssetPath(GetArgumentValue(args, "--yooasset-convert-copy-to"));
            if (string.IsNullOrWhiteSpace(destinationScene) && sourceScenes.Count == 1 && !dryRun)
                destinationScene = DefaultConvertedScenePath;

            results.Add(ConvertScene(sourceScenes[index], destinationScene, dryRun, limit));
        }

        if (!dryRun && updateBuildSettings)
            UpdateBuildSettings(results);

        LogResults(results, dryRun);
    }

    private static SceneConversionResult ConvertScene(string sourceScenePath, string destinationScenePath, bool dryRun, int limit)
    {
        SceneConversionResult result = new SceneConversionResult
        {
            sourceScenePath = sourceScenePath,
            destinationScenePath = string.IsNullOrWhiteSpace(destinationScenePath) ? sourceScenePath : destinationScenePath
        };

        if (!File.Exists(sourceScenePath))
        {
            result.errors.Add("Scene file not found: " + sourceScenePath);
            return result;
        }

        string workingScenePath = sourceScenePath;
        if (!dryRun && !string.IsNullOrWhiteSpace(destinationScenePath) &&
            !string.Equals(sourceScenePath, destinationScenePath, StringComparison.OrdinalIgnoreCase))
        {
            EnsureAssetFolder(Path.GetDirectoryName(destinationScenePath));
            if (!AssetDatabase.CopyAsset(sourceScenePath, destinationScenePath))
            {
                result.errors.Add("Failed to copy scene to: " + destinationScenePath);
                return result;
            }

            workingScenePath = destinationScenePath;
        }

        Scene scene = EditorSceneManager.OpenScene(workingScenePath, OpenSceneMode.Single);
        List<GameObject> prefabRoots = CollectConvertiblePrefabRoots(scene, result);
        result.candidateCount = prefabRoots.Count;

        if (dryRun)
            return result;

        int converted = 0;
        foreach (GameObject root in prefabRoots)
        {
            if (converted >= limit)
                break;

            string assetPath = NormalizeAssetPath(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root));
            ReplacePrefabInstanceWithLoader(root, assetPath);
            converted++;
        }

        result.convertedCount = converted;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return result;
    }

    private static List<GameObject> CollectConvertiblePrefabRoots(Scene scene, SceneConversionResult result)
    {
        HashSet<GameObject> uniqueRoots = new HashSet<GameObject>();
        List<GameObject> roots = new List<GameObject>();
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int index = 0; index < allObjects.Length; index++)
        {
            GameObject gameObject = allObjects[index];
            if (gameObject == null || gameObject.scene != scene || EditorUtility.IsPersistent(gameObject))
                continue;

            GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
            if (prefabRoot == null || prefabRoot.scene != scene || !uniqueRoots.Add(prefabRoot))
                continue;

            string assetPath = NormalizeAssetPath(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabRoot));
            if (!IsConvertibleYooAssetAssetPath(assetPath))
                continue;

            roots.Add(prefabRoot);
            result.entries.Add(new SceneConversionEntry
            {
                sceneObjectName = prefabRoot.name,
                address = assetPath,
                propertyModificationCount = CountPropertyModifications(prefabRoot)
            });
        }

        return roots;
    }

    private static bool IsConvertibleYooAssetAssetPath(string assetPath)
    {
        return assetPath.StartsWith(LegacyGameAssetsRoot, StringComparison.OrdinalIgnoreCase) ||
               IsContentModuleRuntimeAssetPath(assetPath) ||
               assetPath.StartsWith(LegacyAngryMeshRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContentModuleRuntimeAssetPath(string assetPath)
    {
        string normalizedPath = NormalizeAssetPath(assetPath);
        return normalizedPath.StartsWith(GameContentRoot, StringComparison.OrdinalIgnoreCase) &&
               normalizedPath.IndexOf("/Runtime/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ReplacePrefabInstanceWithLoader(GameObject prefabRoot, string address)
    {
        Transform sourceTransform = prefabRoot.transform;
        Transform parent = sourceTransform.parent;
        int siblingIndex = sourceTransform.GetSiblingIndex();
        bool activeSelf = prefabRoot.activeSelf;

        GameObject placeholder = new GameObject(prefabRoot.name);
        placeholder.layer = prefabRoot.layer;
        placeholder.isStatic = prefabRoot.isStatic;
        TryCopyTag(prefabRoot, placeholder);
        GameObjectUtility.SetStaticEditorFlags(placeholder, GameObjectUtility.GetStaticEditorFlags(prefabRoot));

        placeholder.transform.SetParent(parent, false);
        placeholder.transform.localPosition = sourceTransform.localPosition;
        placeholder.transform.localRotation = sourceTransform.localRotation;
        placeholder.transform.localScale = sourceTransform.localScale;
        placeholder.transform.SetSiblingIndex(siblingIndex);

        AngryMeshYooAssetPrefabInstance loader = placeholder.AddComponent<AngryMeshYooAssetPrefabInstance>();
        loader.Configure(address);

        placeholder.SetActive(activeSelf);
        UnityEngine.Object.DestroyImmediate(prefabRoot);
        EditorUtility.SetDirty(placeholder);
    }

    private static void UpdateBuildSettings(IReadOnlyList<SceneConversionResult> results)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            SceneConversionResult result = results[resultIndex];
            if (result.errors.Count > 0 || string.IsNullOrWhiteSpace(result.destinationScenePath))
                continue;

            for (int sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
            {
                if (!string.Equals(scenes[sceneIndex].path, result.sourceScenePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                scenes[sceneIndex] = new EditorBuildSettingsScene(result.destinationScenePath, scenes[sceneIndex].enabled);
                break;
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void LogResults(IReadOnlyList<SceneConversionResult> results, bool dryRun)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("ANGRY MESH YooAsset reference conversion " + (dryRun ? "dry run" : "finished") + ".");

        for (int index = 0; index < results.Count; index++)
        {
            SceneConversionResult result = results[index];
            builder.AppendLine("Scene: " + result.sourceScenePath);
            builder.AppendLine("Output: " + result.destinationScenePath);
            builder.AppendLine("Candidates: " + result.candidateCount + ", Converted: " + result.convertedCount);

            foreach (string error in result.errors)
                builder.AppendLine("Error: " + error);

            for (int entryIndex = 0; entryIndex < Mathf.Min(result.entries.Count, 40); entryIndex++)
            {
                SceneConversionEntry entry = result.entries[entryIndex];
                builder.AppendLine("  " + entry.sceneObjectName + " -> " + entry.address +
                                   " (mods=" + entry.propertyModificationCount + ")");
            }

            if (result.entries.Count > 40)
                builder.AppendLine("  ... " + (result.entries.Count - 40) + " more candidate(s)");
        }

        Debug.Log(builder.ToString());
    }

    private static int CountPropertyModifications(GameObject prefabRoot)
    {
        PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(prefabRoot);
        return modifications != null ? modifications.Length : 0;
    }

    private static void TryCopyTag(GameObject source, GameObject destination)
    {
        try
        {
            destination.tag = source.tag;
        }
        catch
        {
            destination.tag = "Untagged";
        }
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        string normalized = NormalizeAssetPath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized) || AssetDatabase.IsValidFolder(normalized))
            return;

        string[] segments = normalized.Split('/');
        string current = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[index]);

            current = next;
        }
    }

    private static bool HasArgument(string[] args, string name)
    {
        return args != null && args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetArgumentValue(string[] args, string name)
    {
        if (args == null || string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string prefix = name + "=";
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg.Substring(prefix.Length).Trim('"');

            if (!string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                continue;

            return index + 1 < args.Length ? (args[index + 1] ?? string.Empty).Trim('"') : string.Empty;
        }

        return string.Empty;
    }

    private static int ParseInt(string value, int defaultValue)
    {
        return int.TryParse(value, out int parsed) ? Mathf.Max(0, parsed) : defaultValue;
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');
    }

    private sealed class SceneConversionResult
    {
        public string sourceScenePath = string.Empty;
        public string destinationScenePath = string.Empty;
        public int candidateCount;
        public int convertedCount;
        public readonly List<SceneConversionEntry> entries = new List<SceneConversionEntry>();
        public readonly List<string> errors = new List<string>();
    }

    private sealed class SceneConversionEntry
    {
        public string sceneObjectName = string.Empty;
        public string address = string.Empty;
        public int propertyModificationCount;
    }
}
