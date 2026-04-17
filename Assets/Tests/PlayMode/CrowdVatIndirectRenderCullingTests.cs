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
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string IndirectComputePath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string IndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";

    private struct RenderTestContext
    {
        public GameObject Root;
        public Component Renderer;
        public System.Type RendererType;
        public Camera Camera;
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_RenderWritesVisibleInstancesIntoSingleIndirectBuffer()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        RenderTestContext context = CreateContext(new Vector3(0.0f, 1.6f, -5.0f), Vector3.forward);

        yield return WaitFrames(3);

        int visibleInstanceCount = ReadVisibleInstanceCount(context.Renderer, context.RendererType);
        Assert.AreEqual(6, visibleInstanceCount, "单一路径渲染应提交全部可见实例。");

        GraphicsBuffer[] argsBuffers = ReadArgsBuffers(context.Renderer, context.RendererType);
        Assert.AreEqual(1, argsBuffers.Length, "单一路径渲染应只创建一组 indirect args buffer。");

        GraphicsBuffer.IndirectDrawIndexedArgs args = ReadIndirectArgs(argsBuffers[0])[0];
        Assert.AreEqual(6u, args.instanceCount, "indirect args 应写入全部可见实例。");
        Assert.AreEqual(0u, args.startInstance, "可见实例索引缓冲接管后，startInstance 应保持为 0。");

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

        int visibleInstanceCount = ReadVisibleInstanceCount(context.Renderer, context.RendererType);
        Assert.AreEqual(0, visibleInstanceCount, "视锥外时不应提交任何 crowd 实例。");

        Cleanup(context);
#endif
    }

#if UNITY_EDITOR
    private static RenderTestContext CreateContext(Vector3 cameraPosition, Vector3 cameraForward)
    {
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

        Component templatePlayer = ResolveTemplatePlayer(vatPrefab, playerType);
        Assert.IsNotNull(templatePlayer, "VAT prefab 缺少 CrowdVatPlayer。");

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
        SetField(renderer, "_updateCompute", indirectCompute);
        SetField(renderer, "_indirectShader", indirectShader);
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
        SetField(renderer, "_enableActiveBubble", false);
        SetField(renderer, "_shadowCastingMode", ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);
        SetField(renderer, "_boundsPadding", 0.05f);
        SetField(renderer, "_enableFrustumCulling", true);
        SetField(renderer, "_renderChunkWorldSize", 2.1f);

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

    private static int ReadVisibleInstanceCount(Component renderer, System.Type rendererType)
    {
        FieldInfo field = rendererType.GetField("_visibleInstanceCount", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "未找到 _visibleInstanceCount 字段。");
        return (int)field.GetValue(renderer);
    }

    private static GraphicsBuffer[] ReadArgsBuffers(Component renderer, System.Type rendererType)
    {
        FieldInfo field = rendererType.GetField("_indirectArgsBuffers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "未找到 _indirectArgsBuffers 字段。");

        GraphicsBuffer[] buffers = field.GetValue(renderer) as GraphicsBuffer[];
        Assert.IsNotNull(buffers, "_indirectArgsBuffers 为空。");
        return buffers;
    }

    private static GraphicsBuffer.IndirectDrawIndexedArgs[] ReadIndirectArgs(GraphicsBuffer buffer)
    {
        Assert.IsNotNull(buffer, "GraphicsBuffer 为空。");
        GraphicsBuffer.IndirectDrawIndexedArgs[] args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        buffer.GetData(args, 0, 0, 1);
        return args;
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
