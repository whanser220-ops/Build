using UnityEditor;
using UnityEngine;

namespace Project.Tools.AngryMeshSourcesImport
{
    public sealed class AngryMeshSourcesImportWindow : EditorWindow
    {
        private Vector2 _scroll;
        private AngryMeshSourcesImportReport _lastReport;

        [MenuItem("Tools/ANGRY MESH/Sources Import Workflow")]
        private static void Open()
        {
            GetWindow<AngryMeshSourcesImportWindow>("ANGRY MESH Sources");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Profile", AngryMeshSourcesImportWorkflow.DefaultProfilePath);
            EditorGUILayout.LabelField("Source Root", AngryMeshSourcesImportWorkflow.DefaultSourceRoot);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ensure Profile"))
                {
                    AngryMeshSourcesImportWorkflow.EnsureDefaultFiles(true);
                    AssetDatabase.Refresh();
                }

                if (GUILayout.Button("Open Profile"))
                {
                    Object profileAsset = AssetDatabase.LoadAssetAtPath<Object>(AngryMeshSourcesImportWorkflow.DefaultProfilePath);
                    Selection.activeObject = profileAsset;
                    EditorGUIUtility.PingObject(profileAsset);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check Sources"))
                    _lastReport = AngryMeshSourcesImportWorkflow.CheckAll();

                if (GUILayout.Button("Apply Changed"))
                    _lastReport = AngryMeshSourcesImportWorkflow.ApplyAll(false);

                if (GUILayout.Button("Force Reimport All"))
                {
                    if (EditorUtility.DisplayDialog("Force Reimport All", "Reimport every matched ANGRY MESH source asset?", "Proceed", "Cancel"))
                        _lastReport = AngryMeshSourcesImportWorkflow.ApplyAll(true);
                }
            }

            EditorGUILayout.Space();
            if (_lastReport == null)
            {
                EditorGUILayout.HelpBox("Run Check Sources or Apply Changed to see a report.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Last Report", AngryMeshSourcesImportWorkflow.BuildSummary(_lastReport));
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            int issueCount = Mathf.Min(_lastReport.issues.Count, 120);
            for (int i = 0; i < issueCount; i++)
            {
                AngryMeshSourcesImportIssue issue = _lastReport.issues[i];
                MessageType messageType = MessageType.Info;
                if (issue.severity == "Error")
                    messageType = MessageType.Error;
                else if (issue.severity == "Warning")
                    messageType = MessageType.Warning;

                EditorGUILayout.HelpBox(
                    issue.severity + " [" + issue.ruleId + "] " + issue.assetPath + "\n" + issue.message,
                    messageType);
            }

            if (_lastReport.issues.Count > issueCount)
                EditorGUILayout.HelpBox("Report truncated in UI. Use CLI or Console for full details.", MessageType.Info);

            EditorGUILayout.EndScrollView();
        }
    }
}
