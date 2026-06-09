using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Project.Tools.AngryMeshSourcesImport
{
    public sealed class AngryMeshSourcesImportPostprocessor : AssetPostprocessor
    {
        private static bool s_IsApplying;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (s_IsApplying)
                return;

            AngryMeshSourcesImportProfile profile = AngryMeshSourcesImportWorkflow.LoadProfile();
            if (!profile.autoApplyOnImport)
                return;

            List<string> affectedAssets = new List<string>();
            AddSourceAssets(profile, importedAssets, affectedAssets);
            AddSourceAssets(profile, movedAssets, affectedAssets);

            if (affectedAssets.Count == 0)
                return;

            s_IsApplying = true;
            try
            {
                AngryMeshSourcesImportReport report = AngryMeshSourcesImportWorkflow.ApplyAssets(affectedAssets, false);
                if (report.changedCount > 0 || report.ErrorCount > 0)
                {
                    Debug.Log("[AngryMeshSourcesImport] Auto import check: " + AngryMeshSourcesImportWorkflow.BuildSummary(report));
                }
            }
            finally
            {
                s_IsApplying = false;
            }
        }

        private static void AddSourceAssets(
            AngryMeshSourcesImportProfile profile,
            string[] paths,
            List<string> output)
        {
            if (paths == null)
                return;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = AngryMeshSourcesImportWorkflow.NormalizeAssetPath(paths[i]);
                if (path.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (AngryMeshSourcesImportWorkflow.IsUnderSourceRoot(profile, path))
                    output.Add(path);
            }
        }
    }
}
