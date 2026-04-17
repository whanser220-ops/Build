using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CrowdVatIndirectCollisionApproximationTests
{
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string IndirectComputePath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string IndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";

    [UnityTest]
    public IEnumerator CrowdVatIndirect_ApproximateCollisionSeparatesOverlappingInstances()
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

        GameObject root = new GameObject("CrowdVatIndirectCollisionApproximation");
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
        SetField(renderer, "_velocityDamping", 0.2f);
        SetField(renderer, "_maxPushPerStep", 0.25f);
        SetField(renderer, "_maxDisplacementFromSpawn", 1.2f);
        SetField(renderer, "_shadowCastingMode", UnityEngine.Rendering.ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);

        root.SetActive(true);
        rendererType.GetMethod("RebuildCrowdLayout")?.Invoke(renderer, null);

        yield return null;
        yield return null;

        for (int frameIndex = 0; frameIndex < 10; frameIndex++)
            yield return null;

        Vector3[] positions = ReadInstancePositions(renderer, rendererType, 2);
        float distance = Vector2.Distance(
            new Vector2(positions[0].x, positions[0].z),
            new Vector2(positions[1].x, positions[1].z));

        Object.Destroy(root);

        Assert.Greater(distance, 0.25f, $"GPU 近似碰撞没有把重叠实例分开，当前距离: {distance:F3}");
#endif
    }

#if UNITY_EDITOR
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
