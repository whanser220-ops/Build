using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CrowdVatIndirectAnimationSmokeTests
{
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string IndirectComputePath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string IndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";

    [UnityTest]
    public IEnumerator CrowdVatIndirect_FrameDataAdvancesOverTime()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        GameObject vatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VatPrefabPath);
        ComputeShader indirectCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(IndirectComputePath);
        Shader indirectShader = AssetDatabase.LoadAssetAtPath<Shader>(IndirectShaderPath);

        Assert.IsNotNull(vatPrefab, $"未找到 VAT prefab: {VatPrefabPath}");
        Assert.IsNotNull(indirectCompute, $"未找到 ComputeShader: {IndirectComputePath}");
        Assert.IsNotNull(indirectShader, $"未找到 Shader: {IndirectShaderPath}");

        System.Type playerType = ResolveRuntimeType("CrowdVatPlayer");
        System.Type rendererType = ResolveRuntimeType("CrowdVatIndirectRenderer");

        Assert.IsNotNull(playerType, "未找到 CrowdVatPlayer 类型。");
        Assert.IsNotNull(rendererType, "未找到 CrowdVatIndirectRenderer 类型。");

        Component templatePlayer = vatPrefab.GetComponentInChildren(playerType, true);
        Assert.IsNotNull(templatePlayer, "VAT prefab 缺少 CrowdVatPlayer。");

        GameObject root = new GameObject("CrowdVatIndirectAnimationSmoke");
        root.SetActive(false);

        Component renderer = root.AddComponent(rendererType);
        SetField(renderer, "_templatePrefab", templatePlayer);
        SetField(renderer, "_updateCompute", indirectCompute);
        SetField(renderer, "_indirectShader", indirectShader);
        SetField(renderer, "_clipName", string.Empty);
        SetField(renderer, "_instanceCount", 1);
        SetField(renderer, "_areaSize", new Vector2(1.0f, 1.0f));
        SetField(renderer, "_cellJitter", 0.0f);
        SetField(renderer, "_heightOffset", 0.0f);
        SetField(renderer, "_randomSeed", 1);
        SetField(renderer, "_factionLayout", 1);
        SetField(renderer, "_baseScale", 1.0f);
        SetField(renderer, "_scaleMultiplierRange", Vector2.one);
        SetField(renderer, "_playOnEnable", true);
        SetField(renderer, "_loopOverride", true);
        SetField(renderer, "_playbackSpeed", 1.0f);
        SetField(renderer, "_playbackSpeedMultiplierRange", Vector2.one);
        SetField(renderer, "_normalizedStartOffsetRandom", 0.0f);
        SetField(renderer, "_shadowCastingMode", UnityEngine.Rendering.ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);

        root.SetActive(true);
        rendererType.GetMethod("RebuildCrowdLayout")?.Invoke(renderer, null);

        yield return null;
        yield return null;

        Vector4 firstFrameData = ReadFirstFrameData(renderer, rendererType);

        for (int frameIndex = 0; frameIndex < 8; frameIndex++)
            yield return null;

        Vector4 secondFrameData = ReadFirstFrameData(renderer, rendererType);

        bool advanced =
            !Mathf.Approximately(firstFrameData.x, secondFrameData.x) ||
            !Mathf.Approximately(firstFrameData.y, secondFrameData.y) ||
            !Mathf.Approximately(firstFrameData.z, secondFrameData.z);

        Object.Destroy(root);

        Assert.IsTrue(
            advanced,
            $"实例帧数据没有推进。first={firstFrameData}, second={secondFrameData}");
#endif
    }

    private static Vector4 ReadFirstFrameData(Component renderer, System.Type rendererType)
    {
        FieldInfo field = rendererType.GetField(
            "_instanceFrameDataBuffer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "未找到 _instanceFrameDataBuffer 字段。");

        ComputeBuffer buffer = field.GetValue(renderer) as ComputeBuffer;
        Assert.IsNotNull(buffer, "_instanceFrameDataBuffer 为空。");

        Vector4[] data = new Vector4[1];
        buffer.GetData(data);
        return data[0];
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到字段: {fieldName}");
        object boxedValue = field.FieldType.IsEnum && value is int rawValue
            ? System.Enum.ToObject(field.FieldType, rawValue)
            : value;
        field.SetValue(target, boxedValue);
    }

    private static System.Type ResolveRuntimeType(string typeName)
    {
        foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type type = assembly.GetType(typeName, false);
            if (type != null)
                return type;
        }

        return null;
    }
}
