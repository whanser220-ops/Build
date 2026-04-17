#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class BottlePileScenePlayModeSetup : IPrebuildSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PileObjectName = "CokeBottlePile";
    private const string EditorAssemblyName = "Assembly-CSharp-Editor";
    private const string AutomationTypeName = "BottlePileSceneAutomation";
    private const string SetupMethodName = "SetupSampleScene";

    public void Setup()
    {
        MethodInfo setupMethod = ResolveSetupMethod();
        setupMethod.Invoke(null, null);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string fullScenePath = Path.Combine(ProjectRoot, ScenePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullScenePath))
            throw new InvalidOperationException($"Scene was not found after setup: {fullScenePath}");

        string sceneText = File.ReadAllText(fullScenePath);
        if (!sceneText.Contains(PileObjectName))
        {
            throw new InvalidOperationException(
                $"Scene setup did not persist {PileObjectName} into {ScenePath}.");
        }
    }

    private static MethodInfo ResolveSetupMethod()
    {
        Assembly editorAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, EditorAssemblyName, StringComparison.Ordinal));
        if (editorAssembly == null)
            throw new InvalidOperationException($"Could not find {EditorAssemblyName}.");

        Type automationType = editorAssembly.GetType(AutomationTypeName, false);
        if (automationType == null)
            throw new InvalidOperationException($"Could not find {AutomationTypeName} in {EditorAssemblyName}.");

        MethodInfo setupMethod = automationType.GetMethod(SetupMethodName, BindingFlags.Public | BindingFlags.Static);
        if (setupMethod == null)
        {
            throw new InvalidOperationException(
                $"Could not find {SetupMethodName} on {AutomationTypeName}.");
        }

        return setupMethod;
    }

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
}
#endif
