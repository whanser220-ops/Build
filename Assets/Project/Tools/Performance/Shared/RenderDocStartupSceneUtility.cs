using System;
using System.Collections.Generic;

public static class RenderDocStartupSceneUtility
{
    public static string NormalizeScenePath(string scenePath)
    {
        return string.IsNullOrWhiteSpace(scenePath)
            ? string.Empty
            : scenePath.Trim().Replace('\\', '/');
    }

    public static bool ScenePathsMatch(string left, string right)
    {
        return string.Equals(
            NormalizeScenePath(left),
            NormalizeScenePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string[] ResolveBuildScenePaths(string activeScenePath, IEnumerable<string> enabledScenePaths)
    {
        List<string> resolvedScenePaths = new List<string>();
        HashSet<string> seenScenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddScenePath(activeScenePath, resolvedScenePaths, seenScenePaths);

        if (enabledScenePaths != null)
        {
            foreach (string scenePath in enabledScenePaths)
                AddScenePath(scenePath, resolvedScenePaths, seenScenePaths);
        }

        return resolvedScenePaths.ToArray();
    }

    private static void AddScenePath(string scenePath, List<string> resolvedScenePaths, HashSet<string> seenScenePaths)
    {
        string normalizedScenePath = NormalizeScenePath(scenePath);
        if (string.IsNullOrWhiteSpace(normalizedScenePath) || !seenScenePaths.Add(normalizedScenePath))
            return;

        resolvedScenePaths.Add(normalizedScenePath);
    }
}
