using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CrowdVatDiagnostics
{
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SampleSceneCrowdObjectName = "QianxiaCrowdIndirect";
    private const string SourceModelAssetPath = "Assets/Project/Characters/Qianxia/SourceModels/LOD2.fbx";
    private const string WalkClipAssetPath = "Assets/Project/Characters/Qianxia/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx";
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string AnimationAssetPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.asset";
    private const string ComputeShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string ShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";

    [MenuItem("Tools/Qianxia/Run Crowd VAT Diagnostics")]
    public static void RunCrowdVatDiagnosticsMenu()
    {
        RunCrowdVatDiagnostics();
    }

    public static void RunCrowdVatDiagnostics()
    {
        Debug.Log(BuildResourceDiagnostics());
    }

    [MenuItem("Tools/Qianxia/Inspect Crowd In Sample Scene")]
    public static void InspectCrowdInSampleSceneMenu()
    {
        Debug.Log(InspectCrowdInSampleScene());
    }

    public static string InspectCrowdInSampleScene()
    {
        Scene scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        GameObject crowdObject = GameObject.Find(SampleSceneCrowdObjectName);
        if (crowdObject == null)
            throw new InvalidOperationException($"场景中未找到 crowd 对象: {SampleSceneCrowdObjectName}");

        CrowdVatIndirectRenderer renderer = crowdObject.GetComponent<CrowdVatIndirectRenderer>();
        if (renderer == null)
            throw new InvalidOperationException("QianxiaCrowdIndirect 缺少 CrowdVatIndirectRenderer。");

        Camera camera = Camera.main;
        if (camera == null)
            camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (camera == null)
            throw new InvalidOperationException("SampleScene 中未找到可用 Camera。");

        Invoke(renderer, "SyncTemplateBindings");
        renderer.RebuildCrowdLayout();
        bool initialized = (bool)Invoke(renderer, "InitializeIfNeeded");
        if (!initialized)
            throw new InvalidOperationException("SampleScene crowd 初始化失败。");

        renderer.PlayClip(string.Empty);
        Invoke(renderer, "AdvancePlayback", 0.15f);
        Invoke(renderer, "UpdateGpuBuffers");

        int visibleInstanceCountBeforeDraw = GetFieldValue<int>(renderer, "_visibleInstanceCount");
        bool hasVisibleBounds = GetFieldValue<bool>(renderer, "_hasVisibleBounds");
        Bounds visibleWorldBounds = GetFieldValue<Bounds>(renderer, "_visibleWorldBounds");
        Bounds localCrowdBounds = GetFieldValue<Bounds>(renderer, "_localCrowdBounds");
        Array renderChunks = GetFieldValue<Array>(renderer, "_renderChunks");
        uint[] visibleInstanceIndices = GetField<uint[]>(renderer, "_visibleInstanceIndexCache");
        bool hasRuntimeRenderResource = GetFieldValue<bool>(renderer, "_hasRuntimeRenderResource");
        bool hasClip = GetFieldValue<bool>(renderer, "_hasClip");
        bool isPlaying = GetFieldValue<bool>(renderer, "_isPlaying");
        Array indirectArgsBuffers = GetFieldValue<Array>(renderer, "_indirectArgsBuffers");
        object runtimeRenderResource = GetPrivateFieldValue(renderer, "_runtimeRenderResource");
        object templatePrefab = GetPrivateFieldValue(runtimeRenderResource, "templatePrefab");
        object animationAsset = GetPrivateFieldValue(runtimeRenderResource, "animationAsset");
        Mesh runtimeMesh = GetPrivateFieldValue<Mesh>(runtimeRenderResource, "mesh");
        Material[] runtimeMaterials = GetPrivateFieldValue<Material[]>(runtimeRenderResource, "runtimeMaterials");

        Bounds worldCrowdBounds = TransformBounds(crowdObject.transform.localToWorldMatrix, localCrowdBounds);
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
        bool crowdIntersectsFrustum = GeometryUtility.TestPlanesAABB(frustumPlanes, worldCrowdBounds);
        float crowdDistance = Vector3.Distance(camera.transform.position, worldCrowdBounds.ClosestPoint(camera.transform.position));
        int expectedVisibleInstanceCount = CountExpectedVisibleInstances(crowdObject.transform.localToWorldMatrix, renderChunks, frustumPlanes);

        string drawInvocation = "not-invoked";
        try
        {
            Invoke(renderer, "Draw", new[] { typeof(Camera) }, camera);
            drawInvocation = "ok";
        }
        catch (TargetInvocationException exception)
        {
            drawInvocation = $"failed: {exception.InnerException?.GetType().Name}: {exception.InnerException?.Message}";
        }
        catch (Exception exception)
        {
            drawInvocation = $"failed: {exception.GetType().Name}: {exception.Message}";
        }

        int visibleInstanceCountAfterDraw = GetFieldValue<int>(renderer, "_visibleInstanceCount");
        bool hasVisibleBoundsAfterDraw = GetFieldValue<bool>(renderer, "_hasVisibleBounds");
        Bounds visibleWorldBoundsAfterDraw = GetFieldValue<Bounds>(renderer, "_visibleWorldBounds");

        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"SampleScene crowd 检查: {scene.path}");
        builder.AppendLine($"camera={camera.name} pos={camera.transform.position} forward={camera.transform.forward}");
        builder.AppendLine($"crowdPos={crowdObject.transform.position} worldBoundsCenter={worldCrowdBounds.center} worldBoundsSize={worldCrowdBounds.size}");
        builder.AppendLine($"crowdDistance={crowdDistance:F2} crowdIntersectsFrustum={crowdIntersectsFrustum}");
        builder.AppendLine(
            $"initialized={initialized} hasRuntimeRenderResource={hasRuntimeRenderResource} hasClip={hasClip} isPlaying={isPlaying}");
        builder.AppendLine(
            $"templatePrefab={(templatePrefab != null ? templatePrefab.ToString() : "null")} animationAsset={(animationAsset != null ? animationAsset.ToString() : "null")}");
        builder.AppendLine(
            $"runtimeMesh={(runtimeMesh != null ? runtimeMesh.name : "null")} subMeshCount={(runtimeMesh != null ? runtimeMesh.subMeshCount : 0)} runtimeMaterialCount={(runtimeMaterials != null ? runtimeMaterials.Length : 0)} indirectArgsBufferCount={(indirectArgsBuffers != null ? indirectArgsBuffers.Length : 0)}");
        builder.AppendLine(
            $"renderChunkCount={(renderChunks != null ? renderChunks.Length : 0)} expectedVisibleInstanceCount={expectedVisibleInstanceCount}");
        builder.AppendLine(
            $"visibleInstanceCountBeforeDraw={visibleInstanceCountBeforeDraw} hasVisibleBoundsBeforeDraw={hasVisibleBounds} visibleBoundsBeforeDrawCenter={visibleWorldBounds.center} visibleBoundsBeforeDrawSize={visibleWorldBounds.size}");
        builder.AppendLine(
            $"drawInvocation={drawInvocation} visibleInstanceCountAfterDraw={visibleInstanceCountAfterDraw} hasVisibleBoundsAfterDraw={hasVisibleBoundsAfterDraw} visibleBoundsAfterDrawCenter={visibleWorldBoundsAfterDraw.center} visibleBoundsAfterDrawSize={visibleWorldBoundsAfterDraw.size}");
        builder.AppendLine(BuildRenderChunkDiagnostics(crowdObject.transform.localToWorldMatrix, renderChunks, frustumPlanes));
        builder.AppendLine(BuildVisibleInstancePreview(visibleInstanceCountAfterDraw, visibleInstanceIndices));
        return builder.ToString();
    }

    private static string BuildResourceDiagnostics()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Crowd VAT 资源检查");
        builder.AppendLine(BuildAssetLine("Source Model", AssetDatabase.LoadAssetAtPath<GameObject>(SourceModelAssetPath), SourceModelAssetPath));
        builder.AppendLine(BuildAssetLine("Walk Clip", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(WalkClipAssetPath), WalkClipAssetPath));
        builder.AppendLine(BuildAssetLine("VAT Prefab", AssetDatabase.LoadAssetAtPath<GameObject>(VatPrefabPath), VatPrefabPath));
        builder.AppendLine(BuildAssetLine("Animation Asset", AssetDatabase.LoadAssetAtPath<CrowdVatAnimationAsset>(AnimationAssetPath), AnimationAssetPath));
        builder.AppendLine(BuildAssetLine("Compute Shader", AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath), ComputeShaderPath));
        builder.AppendLine(BuildAssetLine("Indirect Shader", AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath), ShaderPath));
        return builder.ToString();
    }

    private static string BuildAssetLine(string label, UnityEngine.Object asset, string path)
    {
        return $"{label}: {(asset != null ? "OK" : "Missing")} ({path})";
    }

    private static string BuildVisibleInstancePreview(int visibleInstanceCount, uint[] visibleInstanceIndices)
    {
        if (visibleInstanceCount <= 0 || visibleInstanceIndices == null || visibleInstanceIndices.Length == 0)
            return "visible preview=no-visible-instance";

        int previewCount = Mathf.Min(3, visibleInstanceCount, visibleInstanceIndices.Length);
        StringBuilder builder = new StringBuilder();
        builder.Append("visible preview=");
        for (int previewIndex = 0; previewIndex < previewCount; previewIndex++)
        {
            if (previewIndex > 0)
                builder.Append(" | ");

            builder.Append($"src={visibleInstanceIndices[previewIndex]}");
        }

        return builder.ToString();
    }

    private static string BuildRenderChunkDiagnostics(Matrix4x4 localToWorldMatrix, Array renderChunks, Plane[] frustumPlanes)
    {
        if (renderChunks == null || renderChunks.Length == 0)
            return "chunk diagnostics=no-render-chunk";

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("chunk diagnostics:");

        for (int chunkIndex = 0; chunkIndex < renderChunks.Length; chunkIndex++)
        {
            object chunk = renderChunks.GetValue(chunkIndex);
            uint[] instanceIndices = GetPrivateFieldValue<uint[]>(chunk, "instanceIndices");
            Bounds localBounds = GetPrivateFieldValue<Bounds>(chunk, "localBounds");
            Bounds worldBounds = TransformBounds(localToWorldMatrix, localBounds);
            bool visible = GeometryUtility.TestPlanesAABB(frustumPlanes, worldBounds);
            int instanceCount = instanceIndices != null ? instanceIndices.Length : 0;

            builder.AppendLine(
                $"  chunk[{chunkIndex}] visible={visible} instanceCount={instanceCount} worldCenter={worldBounds.center} worldSize={worldBounds.size}");
        }

        return builder.ToString().TrimEnd();
    }

    private static int CountExpectedVisibleInstances(Matrix4x4 localToWorldMatrix, Array renderChunks, Plane[] frustumPlanes)
    {
        if (renderChunks == null || renderChunks.Length == 0)
            return 0;

        int visibleCount = 0;
        for (int chunkIndex = 0; chunkIndex < renderChunks.Length; chunkIndex++)
        {
            object chunk = renderChunks.GetValue(chunkIndex);
            uint[] instanceIndices = GetPrivateFieldValue<uint[]>(chunk, "instanceIndices");
            Bounds localBounds = GetPrivateFieldValue<Bounds>(chunk, "localBounds");
            Bounds worldBounds = TransformBounds(localToWorldMatrix, localBounds);
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, worldBounds))
                continue;

            visibleCount += instanceIndices != null ? instanceIndices.Length : 0;
        }

        return visibleCount;
    }

    private static T GetField<T>(object instance, string fieldName) where T : class
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? field.GetValue(instance) as T : null;
    }

    private static T GetFieldValue<T>(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(instance.GetType().Name, fieldName);

        return (T)field.GetValue(instance);
    }

    private static object GetPrivateFieldValue(object instance, string fieldName)
    {
        if (instance == null)
            return null;

        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null)
            throw new MissingFieldException(instance.GetType().Name, fieldName);

        return field.GetValue(instance);
    }

    private static T GetPrivateFieldValue<T>(object instance, string fieldName)
    {
        object value = GetPrivateFieldValue(instance, fieldName);
        return value is T typedValue ? typedValue : default;
    }

    private static object Invoke(object instance, string methodName, params object[] arguments)
    {
        return Invoke(instance, methodName, null, arguments);
    }

    private static object Invoke(object instance, string methodName, Type[] parameterTypes, params object[] arguments)
    {
        MethodInfo method = parameterTypes == null
            ? instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            : instance.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);
        if (method == null)
            throw new MissingMethodException(instance.GetType().Name, methodName);

        return method.Invoke(instance, arguments);
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds localBounds)
    {
        Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
        Vector3 extents = localBounds.extents;
        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0.0f, 0.0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0.0f, extents.y, 0.0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0.0f, 0.0f, extents.z));
        Vector3 worldExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        return new Bounds(center, worldExtents * 2.0f);
    }
}
