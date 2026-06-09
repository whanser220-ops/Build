using UnityEditor;
using UnityEngine;

namespace Project.Tools.AngryMeshSourcesImport
{
    public static class AngryMeshSourcesImportCli
    {
        public static void EnsureProfile()
        {
            AngryMeshSourcesImportWorkflow.EnsureDefaultFiles(true);
            Debug.Log("[AngryMeshSourcesImport] Ensured default profile.");
        }

        public static void Check()
        {
            AngryMeshSourcesImportWorkflow.EnsureDefaultFiles(true);
            AngryMeshSourcesImportReport report = AngryMeshSourcesImportWorkflow.CheckAll();
            Debug.Log("[AngryMeshSourcesImport] Check: " + AngryMeshSourcesImportWorkflow.BuildSummary(report));
            LogIssues(report);
            if (Application.isBatchMode && report.ErrorCount > 0)
                EditorApplication.Exit(2);
        }

        public static void Apply()
        {
            AngryMeshSourcesImportWorkflow.EnsureDefaultFiles(true);
            AngryMeshSourcesImportReport report = AngryMeshSourcesImportWorkflow.ApplyAll(false);
            Debug.Log("[AngryMeshSourcesImport] Apply: " + AngryMeshSourcesImportWorkflow.BuildSummary(report));
            LogIssues(report);
            if (Application.isBatchMode && report.ErrorCount > 0)
                EditorApplication.Exit(2);
        }

        public static void ForceReimportAll()
        {
            AngryMeshSourcesImportWorkflow.EnsureDefaultFiles(true);
            AngryMeshSourcesImportReport report = AngryMeshSourcesImportWorkflow.ApplyAll(true);
            Debug.Log("[AngryMeshSourcesImport] Force reimport: " + AngryMeshSourcesImportWorkflow.BuildSummary(report));
            LogIssues(report);
            if (Application.isBatchMode && report.ErrorCount > 0)
                EditorApplication.Exit(2);
        }

        private static void LogIssues(AngryMeshSourcesImportReport report)
        {
            for (int i = 0; i < report.issues.Count; i++)
            {
                AngryMeshSourcesImportIssue issue = report.issues[i];
                string message = string.Format(
                    "[AngryMeshSourcesImport] {0} [{1}] {2}: {3}",
                    issue.severity,
                    issue.ruleId,
                    issue.assetPath,
                    issue.message);

                if (issue.severity == "Error")
                    Debug.LogError(message);
                else if (issue.severity == "Warning")
                    Debug.LogWarning(message);
                else
                    Debug.Log(message);
            }
        }
    }
}
