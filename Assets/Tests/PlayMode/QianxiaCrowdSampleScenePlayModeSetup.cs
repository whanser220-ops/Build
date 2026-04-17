#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class QianxiaCrowdSampleScenePlayModeSetup : IPrebuildSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string CrowdObjectName = "QianxiaCrowdIndirect";
    private const string EditorAssemblyName = "Assembly-CSharp-Editor";
    private const string AutomationTypeName = "QianxiaCrowdSceneAutomation";
    private const string SetupMethodName = "SetupSampleScene";

    public void Setup()
    {
        MethodInfo setupMethod = ResolveSetupMethod();
        setupMethod.Invoke(null, null);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string fullScenePath = Path.Combine(ProjectRoot, ScenePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullScenePath))
            throw new InvalidOperationException($"场景不存在：{fullScenePath}");

        string sceneText = File.ReadAllText(fullScenePath);
        if (!sceneText.Contains(CrowdObjectName))
            throw new InvalidOperationException($"场景中没有持久化写入 {CrowdObjectName}。");
    }

    private static MethodInfo ResolveSetupMethod()
    {
        Assembly editorAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, EditorAssemblyName, StringComparison.Ordinal));
        if (editorAssembly == null)
            throw new InvalidOperationException($"找不到 {EditorAssemblyName}。");

        Type automationType = editorAssembly.GetType(AutomationTypeName, false);
        if (automationType == null)
            throw new InvalidOperationException($"找不到 {AutomationTypeName}。");

        MethodInfo setupMethod = automationType.GetMethod(SetupMethodName, BindingFlags.Public | BindingFlags.Static);
        if (setupMethod == null)
            throw new InvalidOperationException($"找不到 {AutomationTypeName}.{SetupMethodName}。");

        return setupMethod;
    }

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
}
#endif
