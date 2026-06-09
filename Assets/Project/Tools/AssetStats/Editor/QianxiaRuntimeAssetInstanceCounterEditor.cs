using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(QianxiaRuntimeAssetInstanceCounter))]
public sealed class QianxiaRuntimeAssetInstanceCounterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Unified Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "The editor export workflow is unified into Tools/Qianxia/Asset Stats/Open Window. " +
            "Use that window to scan loaded scenes and export prefab audit JSON.",
            MessageType.Info);

        if (GUILayout.Button("Open Loaded Scene Prefab Audit"))
            QianxiaAssetStatsWindow.Open();
    }
}
