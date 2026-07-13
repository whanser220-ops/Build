using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public sealed class ProjectYooAssetBundleReportTests
{
    [Test]
    public void CalculateRedundantSize_UsesAllCopiesMinusSmallestCopy()
    {
        long redundantSize = InvokeStatic<long>(
            "ProjectBundleReportCalculator",
            "CalculateRedundantSize",
            new object[] { new long[] { 300L, 100L, 200L } });

        Assert.That(redundantSize, Is.EqualTo(500L));
    }

    [Test]
    public void CalculateRedundantSize_IgnoresUnknownZeroSizedCopies()
    {
        long redundantSize = InvokeStatic<long>(
            "ProjectBundleReportCalculator",
            "CalculateRedundantSize",
            new object[] { new long[] { 0L, 100L, 200L } });

        Assert.That(redundantSize, Is.EqualTo(200L));
    }

    [Test]
    public void ResolveModuleOwner_ParsesGameDomainAndModule()
    {
        Assert.That(
            ResolveModuleOwner("Assets/Game/Worlds/Meadow/Art/Sources/Textures/T_Grass.png", string.Empty),
            Is.EqualTo("Worlds/Meadow"));
        Assert.That(
            ResolveModuleOwner("Assets/Game/Shared/StylizedPackCommon/Runtime/Materials/M_Wood.mat", string.Empty),
            Is.EqualTo("Shared/StylizedPackCommon"));
    }

    [Test]
    public void ResolveModuleOwner_FallsBackToLegacyPathOrBundleName()
    {
        Assert.That(
            ResolveModuleOwner("Assets/GameAssets/Prefabs/Meadow Environment/Tree.prefab", string.Empty),
            Is.EqualTo("Legacy/Prefabs"));
        Assert.That(
            ResolveModuleOwner("Assets/Plugins/ThirdParty/Runtime.asset", "ui.inventory_1234abcd.bundle"),
            Is.EqualTo("Ui/Inventory"));
    }

    [Test]
    public void FindDependencyChains_ReturnsWarningChainsByDepth()
    {
        Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ui_inventory"] = new List<string> { "common_ui" },
            ["common_ui"] = new List<string> { "common_material" },
            ["common_material"] = new List<string> { "common_shader" },
            ["common_shader"] = new List<string> { "shader_variants" },
            ["shader_variants"] = new List<string>()
        };

        IList chains = InvokeStatic<IList>(
            "ProjectBundleReportCalculator",
            "FindDependencyChains",
            new object[] { graph, 4, 10 });

        Assert.That(chains, Has.Count.EqualTo(1));
        object firstChain = chains[0];
        Assert.That(GetField<int>(firstChain, "depth"), Is.EqualTo(4));
        Assert.That(
            GetField<List<string>>(firstChain, "chain"),
            Is.EqualTo(new[]
            {
                "ui_inventory",
                "common_ui",
                "common_material",
                "common_shader",
                "shader_variants"
            }));
    }

    [Test]
    public void FindDependencyChains_StopsWhenCycleIsFound()
    {
        Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new List<string> { "b" },
            ["b"] = new List<string> { "c" },
            ["c"] = new List<string> { "b" }
        };

        IList chains = InvokeStatic<IList>(
            "ProjectBundleReportCalculator",
            "FindDependencyChains",
            new object[] { graph, 2, 10 });

        Assert.That(chains, Has.Count.GreaterThanOrEqualTo(1));
        List<string> chain = GetField<List<string>>(chains[0], "chain");
        Assert.That(chain.Count, Is.LessThanOrEqualTo(4));
        Assert.That(chain, Does.Contain("b"));
        Assert.That(chain, Does.Contain("c"));
    }

    [Test]
    public void LayoutCaptureTask_OnlyInjectsSbpInterfaceContextKeys()
    {
        Type taskType = GetRequiredType("ProjectSbpBundleLayoutCaptureTask");
        string injectContextAttributeName = "UnityEditor.Build.Pipeline.Injector.InjectContextAttribute";

        List<FieldInfo> injectedFields = taskType
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => field.GetCustomAttributes(false)
                .Any(attribute => attribute.GetType().FullName == injectContextAttributeName))
            .ToList();

        Assert.That(injectedFields.Select(field => field.Name), Is.EquivalentTo(new[] { "_writeData", "_results" }));
        Assert.That(injectedFields.All(field => field.FieldType.IsInterface), Is.True);

        FieldInfo captureStateField = taskType.GetField("_captureState", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(captureStateField, Is.Not.Null, "Missing ProjectSbpBundleLayoutCaptureTask._captureState field.");
        Assert.That(captureStateField.GetCustomAttributes(false)
            .Any(attribute => attribute.GetType().FullName == injectContextAttributeName), Is.False);
    }

    private static string ResolveModuleOwner(string assetPath, string fallbackBundleName)
    {
        return InvokeStatic<string>(
            "ProjectBundleReportCalculator",
            "ResolveModuleOwner",
            new object[] { assetPath, fallbackBundleName });
    }

    private static T InvokeStatic<T>(string typeName, string methodName, object[] args)
    {
        Type type = GetRequiredType(typeName);
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "Missing method: " + typeName + "." + methodName);
        return (T)method.Invoke(null, args);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "Missing field: " + fieldName);
        return (T)field.GetValue(target);
    }

    private static Type GetRequiredType(string typeName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(typeName, false))
            .FirstOrDefault(candidate => candidate != null);

        Assert.That(type, Is.Not.Null, "Unable to find editor type: " + typeName);
        return type;
    }
}
