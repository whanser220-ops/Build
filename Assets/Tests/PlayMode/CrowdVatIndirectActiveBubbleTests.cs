using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CrowdVatIndirectActiveBubbleTests
{
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string IndirectComputePath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string IndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";

    [UnityTest]
    public IEnumerator CrowdVatIndirect_ActiveBubbleControlsNearFieldSolve()
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

        GameObject inactiveTarget = new GameObject("CrowdBubbleTarget_Far");
        inactiveTarget.transform.position = new Vector3(32.0f, 0.0f, 32.0f);
        Component inactiveRenderer = CreateRenderer(rendererType, templatePlayer, indirectCompute, indirectShader, inactiveTarget.transform);
        yield return WaitFrames(12);
        float farDistance = ReadDistance(inactiveRenderer, rendererType);

        Object.Destroy(((Component)inactiveRenderer).gameObject);
        Object.Destroy(inactiveTarget);

        GameObject activeTarget = new GameObject("CrowdBubbleTarget_Near");
        activeTarget.transform.position = Vector3.zero;
        Component activeRenderer = CreateRenderer(rendererType, templatePlayer, indirectCompute, indirectShader, activeTarget.transform);
        yield return WaitFrames(12);
        float nearDistance = ReadDistance(activeRenderer, rendererType);

        Object.Destroy(((Component)activeRenderer).gameObject);
        Object.Destroy(activeTarget);

        Assert.Less(farDistance, 0.12f, $"远离 active bubble 时 crowd 仍然发生了明显分离，当前距离: {farDistance:F3}");
        Assert.Greater(nearDistance, 0.25f, $"进入 active bubble 后 crowd 没有明显分离，当前距离: {nearDistance:F3}");
#endif
    }

#if UNITY_EDITOR
    private static Component CreateRenderer(
        System.Type rendererType,
        Component templatePlayer,
        ComputeShader indirectCompute,
        Shader indirectShader,
        Transform activeBubbleTarget)
    {
        GameObject root = new GameObject("CrowdVatIndirectActiveBubble");
        root.SetActive(false);

        Component renderer = root.AddComponent(rendererType);
        SetField(renderer, "_templatePrefab", templatePlayer);
        SetField(renderer, "_updateCompute", indirectCompute);
        SetField(renderer, "_indirectShader", indirectShader);
        SetField(renderer, "_clipName", string.Empty);
        SetField(renderer, "_instanceCount", 2);
        SetField(renderer, "_areaSize", new Vector2(0.05f, 0.05f));
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
        SetField(renderer, "_enableApproximateCollision", true);
        SetField(renderer, "_collisionRadius", 0.45f);
        SetField(renderer, "_queryCellSize", 1.0f);
        SetField(renderer, "_maxCellOccupancy", 8);
        SetField(renderer, "_solverIterations", 2);
        SetField(renderer, "_selfCollisionStrength", 1.0f);
        SetField(renderer, "_anchorStiffness", 0.0f);
        SetField(renderer, "_velocityDamping", 0.25f);
        SetField(renderer, "_maxPushPerStep", 0.25f);
        SetField(renderer, "_maxDisplacementFromSpawn", 1.2f);
        SetField(renderer, "_inactiveReturnStrength", 0.35f);
        SetField(renderer, "_enableActiveBubble", true);
        SetField(renderer, "_autoResolveCharacterController", false);
        SetField(renderer, "_activeBubbleTarget", activeBubbleTarget);
        SetField(renderer, "_activeBubbleRadius", 2.0f);
        SetField(renderer, "_activeBubbleRetentionRadius", 2.5f);
        SetField(renderer, "_useCharacterAsInteractionSphere", false);
        SetField(renderer, "_shadowCastingMode", UnityEngine.Rendering.ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);

        root.SetActive(true);
        rendererType.GetMethod("RebuildCrowdLayout")?.Invoke(renderer, null);
        return renderer;
    }

    private static float ReadDistance(Component renderer, System.Type rendererType)
    {
        Vector3[] positions = ReadInstancePositions(renderer, rendererType, 2);
        return Vector2.Distance(
            new Vector2(positions[0].x, positions[0].z),
            new Vector2(positions[1].x, positions[1].z));
    }

    private static Vector3[] ReadInstancePositions(Component renderer, System.Type rendererType, int instanceCount)
    {
        FieldInfo bufferField = rendererType.GetField("_instanceTransformBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(bufferField, "未找到 _instanceTransformBuffer 字段。");

        ComputeBuffer buffer = bufferField.GetValue(renderer) as ComputeBuffer;
        Assert.IsNotNull(buffer, "_instanceTransformBuffer 为空。");

        System.Type matrixRowsType = rendererType.GetNestedType("MatrixRows", BindingFlags.NonPublic);
        Assert.IsNotNull(matrixRowsType, "未找到 MatrixRows 类型。");

        System.Array data = System.Array.CreateInstance(matrixRowsType, instanceCount);
        buffer.GetData(data);

        FieldInfo row0Field = matrixRowsType.GetField("row0", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo row1Field = matrixRowsType.GetField("row1", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo row2Field = matrixRowsType.GetField("row2", BindingFlags.Public | BindingFlags.Instance);

        Vector3[] positions = new Vector3[instanceCount];
        for (int instanceIndex = 0; instanceIndex < instanceCount; instanceIndex++)
        {
            object rowData = data.GetValue(instanceIndex);
            Vector4 row0 = (Vector4)row0Field.GetValue(rowData);
            Vector4 row1 = (Vector4)row1Field.GetValue(rowData);
            Vector4 row2 = (Vector4)row2Field.GetValue(rowData);
            positions[instanceIndex] = new Vector3(row0.w, row1.w, row2.w);
        }

        return positions;
    }

    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int index = 0; index < frameCount; index++)
            yield return null;
    }
#endif

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到字段 {fieldName}");
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
