using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CrowdVatIndirectRenderCullingTests
{
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod0Vat.prefab";
    private const string SecondaryLodVatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod1Vat.prefab";
    private const string TertiaryLodVatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string IndirectComputePath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string IndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";
    private const string TertiaryLodIndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectSimple.shader";

    private struct RenderTestContext
    {
        public GameObject Root;
        public Component Renderer;
        public System.Type RendererType;
        public Camera Camera;
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_RenderSplitsVisibleInstancesIntoThreeLodIndirectBuffers()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        RenderTestContext context = CreateContext(new Vector3(0.0f, 1.6f, -5.0f), Vector3.forward, 5.0f);

        yield return WaitFrames(3);
        InvokeDraw(context);

        bool usesGpuVisibleCompaction = ReadPrivateBool(
            context.Renderer,
            context.RendererType,
            "_usesGpuVisibleInstanceCompactionThisFrame");
        Assert.IsTrue(usesGpuVisibleCompaction, "当前 crowd 渲染应按默认配置走 GPU-driven visible compaction。");

        GraphicsBuffer[] lod0ArgsBuffers = ReadArgsBuffers(context.Renderer, context.RendererType, "_indirectArgsBuffers");
        GraphicsBuffer[] lod1ArgsBuffers = ReadArgsBuffers(context.Renderer, context.RendererType, "_visibleLod1IndirectArgsBuffers");
        GraphicsBuffer[] lod2ArgsBuffers = ReadArgsBuffers(context.Renderer, context.RendererType, "_visibleLod2IndirectArgsBuffers");
        Assert.AreEqual(1, lod0ArgsBuffers.Length, "第一档 crowd 应创建一组 indirect args buffer。");
        Assert.AreEqual(1, lod1ArgsBuffers.Length, "第二档 crowd 应创建一组 indirect args buffer。");
        Assert.AreEqual(1, lod2ArgsBuffers.Length, "第三档 crowd 应创建一组 indirect args buffer。");

        GraphicsBuffer.IndirectDrawIndexedArgs lod0Args = ReadIndirectArgs(lod0ArgsBuffers[0])[0];
        GraphicsBuffer.IndirectDrawIndexedArgs lod1Args = ReadIndirectArgs(lod1ArgsBuffers[0])[0];
        GraphicsBuffer.IndirectDrawIndexedArgs lod2Args = ReadIndirectArgs(lod2ArgsBuffers[0])[0];
        Assert.AreEqual(6u, lod0Args.instanceCount + lod1Args.instanceCount + lod2Args.instanceCount, "三档 LOD indirect args 应覆盖全部可见实例。");
        Assert.Greater(lod0Args.instanceCount, 0u, "测试场景应保留一部分第一档 crowd。");
        Assert.Greater(lod1Args.instanceCount, 0u, "中距离 crowd 应写入第二档 indirect args。");
        Assert.Greater(lod2Args.instanceCount, 0u, "更远距离 crowd 应写入第三档 indirect args。");
        Assert.AreEqual(0u, lod0Args.startInstance, "可见实例索引缓冲接管后，第一档 startInstance 应保持为 0。");
        Assert.AreEqual(0u, lod1Args.startInstance, "可见实例索引缓冲接管后，第二档 startInstance 应保持为 0。");
        Assert.AreEqual(0u, lod2Args.startInstance, "可见实例索引缓冲接管后，第三档 startInstance 应保持为 0。");

        Cleanup(context);
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_RenderDefaultsToGpuDrivenVisibleCompaction()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        RenderTestContext context = CreateContext(new Vector3(0.0f, 1.6f, -5.0f), Vector3.forward, 5.0f);

        yield return WaitFrames(3);
        InvokeDraw(context);

        bool usesGpuVisibleCompaction = ReadPrivateBool(
            context.Renderer,
            context.RendererType,
            "_usesGpuVisibleInstanceCompactionThisFrame");
        Assert.IsTrue(usesGpuVisibleCompaction, "当前 crowd 渲染应默认走 GPU-driven visible compaction。");

        Cleanup(context);
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_RenderResolvesGpuRuntimeSquadAabbCullingKernels()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        RenderTestContext context = CreateContext(new Vector3(0.0f, 1.6f, -5.0f), Vector3.forward, 5.0f);

        yield return WaitFrames(1);

        Assert.GreaterOrEqual(
            ReadPrivateInt(context.Renderer, context.RendererType, "_clearVisibleRuntimeSquadBoundsKernel"),
            0,
            "应解析 ClearVisibleRuntimeSquadBounds kernel。");
        Assert.GreaterOrEqual(
            ReadPrivateInt(context.Renderer, context.RendererType, "_buildVisibleRuntimeSquadBoundsKernel"),
            0,
            "应解析 BuildVisibleRuntimeSquadBounds kernel。");
        Assert.GreaterOrEqual(
            ReadPrivateInt(context.Renderer, context.RendererType, "_cullVisibleRuntimeSquadsKernel"),
            0,
            "应解析 CullVisibleRuntimeSquads kernel。");

        Cleanup(context);
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_RenderUsesSimpleShaderForTertiaryLod()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        RenderTestContext context = CreateContext(new Vector3(0.0f, 1.6f, -5.0f), Vector3.forward, 5.0f);

        yield return WaitFrames(1);

        Material[] tertiaryRuntimeMaterials = ReadRuntimeRenderResourceMaterials(
            context.Renderer,
            context.RendererType,
            "_tertiaryRuntimeRenderResource");
        Assert.IsNotNull(tertiaryRuntimeMaterials, "LOD2 应生成运行时材质。");
        Assert.Greater(tertiaryRuntimeMaterials.Length, 0, "LOD2 至少应生成一份运行时材质。");
        Assert.AreEqual(
            "Project/Crowd/VATIndirectSimple",
            tertiaryRuntimeMaterials[0].shader.name,
            "LOD2 应切到 Simple shader，而不是继续沿用 Lit shader。");

        Cleanup(context);
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_FrustumCullSkipsAllInstancesWhenCrowdIsOutsideView()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        RenderTestContext context = CreateContext(new Vector3(0.0f, 1.6f, -5.0f), Vector3.back);

        yield return WaitFrames(3);
        InvokeDraw(context);

        bool usesGpuVisibleCompaction = ReadPrivateBool(
            context.Renderer,
            context.RendererType,
            "_usesGpuVisibleInstanceCompactionThisFrame");
        Assert.IsTrue(usesGpuVisibleCompaction, "视锥外时也应继续走 GPU-driven visible compaction，再把可见绘制参数压成 0。");

        GraphicsBuffer[] lod0ArgsBuffers = ReadArgsBuffers(context.Renderer, context.RendererType, "_indirectArgsBuffers");
        GraphicsBuffer[] lod1ArgsBuffers = ReadArgsBuffers(context.Renderer, context.RendererType, "_visibleLod1IndirectArgsBuffers");
        GraphicsBuffer[] lod2ArgsBuffers = ReadArgsBuffers(context.Renderer, context.RendererType, "_visibleLod2IndirectArgsBuffers");
        GraphicsBuffer.IndirectDrawIndexedArgs lod0Args = ReadIndirectArgs(lod0ArgsBuffers[0])[0];
        GraphicsBuffer.IndirectDrawIndexedArgs lod1Args = ReadIndirectArgs(lod1ArgsBuffers[0])[0];
        GraphicsBuffer.IndirectDrawIndexedArgs lod2Args = ReadIndirectArgs(lod2ArgsBuffers[0])[0];
        Assert.AreEqual(0u, lod0Args.instanceCount + lod1Args.instanceCount + lod2Args.instanceCount, "视锥外时不应提交任何 crowd 实例。");

        Cleanup(context);
#endif
    }

#if UNITY_EDITOR
    private static RenderTestContext CreateContext(
        Vector3 cameraPosition,
        Vector3 cameraForward,
        float secondaryLodStartDistance = 5.0f,
        float tertiaryLodStartDistance = 9.0f)
    {
        GameObject vatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VatPrefabPath);
        GameObject secondaryLodVatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SecondaryLodVatPrefabPath);
        GameObject tertiaryLodVatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TertiaryLodVatPrefabPath);
        ComputeShader indirectCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(IndirectComputePath);
        Shader indirectShader = AssetDatabase.LoadAssetAtPath<Shader>(IndirectShaderPath);
        Shader tertiaryLodIndirectShader = AssetDatabase.LoadAssetAtPath<Shader>(TertiaryLodIndirectShaderPath);

        Assert.IsNotNull(vatPrefab, $"未找到 VAT prefab: {VatPrefabPath}");
        Assert.IsNotNull(secondaryLodVatPrefab, $"未找到 VAT prefab: {SecondaryLodVatPrefabPath}");
        Assert.IsNotNull(tertiaryLodVatPrefab, $"未找到 VAT prefab: {TertiaryLodVatPrefabPath}");
        Assert.IsNotNull(indirectCompute, $"未找到 ComputeShader: {IndirectComputePath}");
        Assert.IsNotNull(indirectShader, $"未找到 Shader: {IndirectShaderPath}");
        Assert.IsNotNull(tertiaryLodIndirectShader, $"未找到 Shader: {TertiaryLodIndirectShaderPath}");

        System.Type playerType = ResolveRuntimeType("CrowdVatPlayer");
        System.Type rendererType = ResolveRuntimeType("CrowdVatIndirectRenderer");
        Assert.IsNotNull(playerType, "未找到 CrowdVatPlayer 类型。");
        Assert.IsNotNull(rendererType, "未找到 CrowdVatIndirectRenderer 类型。");

        Component templatePlayer = ResolveTemplatePlayer(vatPrefab, playerType);
        Component secondaryLodTemplatePlayer = ResolveTemplatePlayer(secondaryLodVatPrefab, playerType);
        Component tertiaryLodTemplatePlayer = ResolveTemplatePlayer(tertiaryLodVatPrefab, playerType);
        Assert.IsNotNull(templatePlayer, "VAT prefab 缺少 CrowdVatPlayer。");
        Assert.IsNotNull(secondaryLodTemplatePlayer, "LOD1 VAT prefab 缺少 CrowdVatPlayer。");
        Assert.IsNotNull(tertiaryLodTemplatePlayer, "LOD2 VAT prefab 缺少 CrowdVatPlayer。");

        GameObject cameraObject = new GameObject("CrowdVatIndirectRenderTestCamera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = cameraPosition;
        camera.transform.rotation = Quaternion.LookRotation(cameraForward, Vector3.up);
        camera.fieldOfView = 60.0f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 50.0f;

        GameObject root = new GameObject("CrowdVatIndirectRenderCulling");
        root.SetActive(false);

        Component renderer = root.AddComponent(rendererType);
        SetField(renderer, "_templatePrefab", templatePlayer);
        SetField(renderer, "_secondaryLodTemplatePrefab", secondaryLodTemplatePlayer);
        SetField(renderer, "_tertiaryLodTemplatePrefab", tertiaryLodTemplatePlayer);
        SetField(renderer, "_updateCompute", indirectCompute);
        SetField(renderer, "_indirectShader", indirectShader);
        SetField(renderer, "_tertiaryLodIndirectShader", tertiaryLodIndirectShader);
        SetField(renderer, "_clipName", string.Empty);
        SetField(renderer, "_playOnEnable", false);
        SetField(renderer, "_instanceCount", 6);
        SetField(renderer, "_areaSize", new Vector2(0.1f, 6.0f));
        SetField(renderer, "_cellJitter", 0.0f);
        SetField(renderer, "_heightOffset", 0.0f);
        SetField(renderer, "_randomSeed", 7);
        SetField(renderer, "_factionLayout", 1);
        SetField(renderer, "_enableTerrainCollision", false);
        SetField(renderer, "_autoResolveTerrain", false);
        SetField(renderer, "_enableApproximateCollision", false);
        SetField(renderer, "_shadowCastingMode", ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);
        SetField(renderer, "_boundsPadding", 0.05f);
        SetField(renderer, "_enableFrustumCulling", true);
        SetField(renderer, "_secondaryLodStartDistance", secondaryLodStartDistance);
        SetField(renderer, "_tertiaryLodStartDistance", tertiaryLodStartDistance);

        root.SetActive(true);
        rendererType.GetMethod("RebuildCrowdLayout")?.Invoke(renderer, null);

        return new RenderTestContext
        {
            Root = root,
            Renderer = renderer,
            RendererType = rendererType,
            Camera = camera
        };
    }

    private static GraphicsBuffer[] ReadArgsBuffers(Component renderer, System.Type rendererType, string fieldName)
    {
        FieldInfo field = rendererType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到 {fieldName} 字段。");

        GraphicsBuffer[] buffers = field.GetValue(renderer) as GraphicsBuffer[];
        Assert.IsNotNull(buffers, $"{fieldName} 为空。");
        return buffers;
    }

    private static Material[] ReadRuntimeRenderResourceMaterials(Component renderer, System.Type rendererType, string fieldName)
    {
        FieldInfo field = rendererType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到 {fieldName} 字段。");

        object resource = field.GetValue(renderer);
        FieldInfo materialsField = field.FieldType.GetField("runtimeMaterials", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(materialsField, $"{fieldName}.runtimeMaterials 字段不存在。");

        return materialsField.GetValue(resource) as Material[];
    }

    private static bool ReadPrivateBool(Component renderer, System.Type rendererType, string fieldName)
    {
        FieldInfo field = rendererType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到 {fieldName} 字段。");
        Assert.IsTrue(field.FieldType == typeof(bool), $"{fieldName} 不是 bool 字段。");
        return (bool)field.GetValue(renderer);
    }

    private static int ReadPrivateInt(Component renderer, System.Type rendererType, string fieldName)
    {
        FieldInfo field = rendererType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到 {fieldName} 字段。");
        Assert.IsTrue(field.FieldType == typeof(int), $"{fieldName} 不是 int 字段。");
        return (int)field.GetValue(renderer);
    }

    private static GraphicsBuffer.IndirectDrawIndexedArgs[] ReadIndirectArgs(GraphicsBuffer buffer)
    {
        Assert.IsNotNull(buffer, "GraphicsBuffer 为空。");
        GraphicsBuffer.IndirectDrawIndexedArgs[] args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        buffer.GetData(args, 0, 0, 1);
        return args;
    }

    private static void InvokeDraw(RenderTestContext context)
    {
        MethodInfo drawMethod = context.RendererType.GetMethod(
            "Draw",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(Camera) },
            null);
        Assert.IsNotNull(drawMethod, "未找到 Draw(Camera) 方法。");
        drawMethod.Invoke(context.Renderer, new object[] { context.Camera });
    }

    private static Component ResolveTemplatePlayer(GameObject vatPrefab, System.Type playerType)
    {
        return vatPrefab != null ? vatPrefab.GetComponentInChildren(playerType, true) : null;
    }

    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
            yield return null;
    }

    private static void Cleanup(RenderTestContext context)
    {
        if (context.Root != null)
            Object.Destroy(context.Root);

        if (context.Camera != null)
            Object.Destroy(context.Camera.gameObject);
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
