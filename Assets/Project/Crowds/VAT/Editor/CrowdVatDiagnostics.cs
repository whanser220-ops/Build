using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CrowdVatDiagnostics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MatrixRowsDebugData
    {
        public Vector4 row0;
        public Vector4 row1;
        public Vector4 row2;
    }

    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SampleSceneCrowdObjectName = "QianxiaCrowdIndirect";
    private const string WalkClipAssetPath = "Assets/Project/Characters/Qianxia/SourceAnimations/Walk/Qianxia_Walk_Slow.fbx";
    private const string Lod0SourceModelAssetPath = "Assets/Project/Characters/Qianxia/SourceModels/LOD2.fbx";
    private const string Lod1SourceModelAssetPath = "Assets/Project/Characters/Qianxia/SourceModels/crowd1000.fbx";
    private const string Lod2SourceModelAssetPath = "Assets/Project/Characters/Qianxia/SourceModels/crowd500.fbx";
    private const string Lod0VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod0Vat.prefab";
    private const string Lod1VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod1Vat.prefab";
    private const string Lod2VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string Lod0AnimationAssetPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod0Vat.asset";
    private const string Lod1AnimationAssetPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod1Vat.asset";
    private const string Lod2AnimationAssetPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.asset";
    private const string Lod0MeshAssetPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod0Vat_Mesh.asset";
    private const string Lod1MeshAssetPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod1Vat_Mesh.asset";
    private const string Lod2MeshAssetPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat_Mesh.asset";
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

        int activeSquadStateCount = GetFieldValue<int>(renderer, "_activeSquadStateCount");
        int activeAgentSquadDataCount = GetFieldValue<int>(renderer, "_activeAgentSquadDataCount");
        int activeFormationSlotCount = GetFieldValue<int>(renderer, "_activeFormationSlotCount");
        bool usesGpuVisibleCompaction = GetFieldValue<bool>(renderer, "_usesGpuVisibleInstanceCompactionThisFrame");
        bool visibleUnassignedInstances = GetFieldValue<bool>(renderer, "_visibleUnassignedInstancesThisFrame");
        Array runtimeSquadRenderChunks = GetField<Array>(renderer, "_runtimeSquadRenderChunks");
        uint[] visibleRuntimeSquadMask = GetField<uint[]>(renderer, "_visibleRuntimeSquadMaskUploadCache");
        uint aliveInstanceCountBeforeDraw = ReadCounterValue(GetPrivateFieldValue<ComputeBuffer>(renderer, "_aliveInstanceCounterBuffer"));
        uint visibleInstanceCountGpuBeforeDraw = ReadCounterValue(GetPrivateFieldValue<ComputeBuffer>(renderer, "_visibleInstanceCounterBuffer"));
        int visibleInstanceCountBeforeDraw = GetFieldValue<int>(renderer, "_visibleInstanceCount");
        bool hasVisibleBounds = GetFieldValue<bool>(renderer, "_hasVisibleBounds");
        Bounds visibleWorldBounds = GetFieldValue<Bounds>(renderer, "_visibleWorldBounds");
        Bounds localCrowdBounds = GetFieldValue<Bounds>(renderer, "_localCrowdBounds");
        Array renderChunks = GetField<Array>(renderer, "_renderChunks");
        uint[] visibleInstanceIndices = GetField<uint[]>(renderer, "_visibleInstanceIndexCache");
        bool hasRuntimeRenderResource = GetFieldValue<bool>(renderer, "_hasRuntimeRenderResource");
        bool hasSecondaryRuntimeRenderResource = GetFieldValue<bool>(renderer, "_hasSecondaryRuntimeRenderResource");
        bool hasTertiaryRuntimeRenderResource = GetFieldValue<bool>(renderer, "_hasTertiaryRuntimeRenderResource");
        bool hasClip = GetFieldValue<bool>(renderer, "_hasClip");
        bool isPlaying = GetFieldValue<bool>(renderer, "_isPlaying");
        Array indirectArgsBuffers = GetFieldValue<Array>(renderer, "_indirectArgsBuffers");
        Array activeVisibleLodRenderResources = GetFieldValue<Array>(renderer, "_activeVisibleLodRenderResources");
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
        uint visibleInstanceCountGpuAfterDraw = ReadCounterValue(GetPrivateFieldValue<ComputeBuffer>(renderer, "_visibleInstanceCounterBuffer"));
        bool usesGpuVisibleCompactionAfterDraw = GetFieldValue<bool>(renderer, "_usesGpuVisibleInstanceCompactionThisFrame");
        bool hasVisibleBoundsAfterDraw = GetFieldValue<bool>(renderer, "_hasVisibleBounds");
        Bounds visibleWorldBoundsAfterDraw = GetFieldValue<Bounds>(renderer, "_visibleWorldBounds");
        ComputeBuffer visibleInstanceIndexBuffer = GetPrivateFieldValue<ComputeBuffer>(renderer, "_visibleInstanceIndexBuffer");
        GraphicsBuffer[] indirectArgsBufferArray = indirectArgsBuffers as GraphicsBuffer[];
        uint[] visibleInstanceIndicesGpuAfterDraw = ReadUIntBuffer(
            visibleInstanceIndexBuffer,
            Mathf.Min((int)visibleInstanceCountGpuAfterDraw, 3));
        uint[] indirectArgsAfterDraw = indirectArgsBufferArray != null && indirectArgsBufferArray.Length > 0
            ? ReadGraphicsUIntBuffer(indirectArgsBufferArray[0], 5)
            : Array.Empty<uint>();
        string firstVisibleTransformPreview = "firstVisibleTransform=no-visible-instance";
        if (visibleInstanceIndicesGpuAfterDraw.Length > 0)
        {
            int firstVisibleSourceInstance = (int)visibleInstanceIndicesGpuAfterDraw[0];
            MatrixRowsDebugData? firstVisibleTransform = ReadMatrixRowsBuffer(
                GetPrivateFieldValue<ComputeBuffer>(renderer, "_instanceTransformBuffer"),
                firstVisibleSourceInstance);
            Vector4? firstVisibleFrameData = ReadVector4Buffer(
                GetPrivateFieldValue<ComputeBuffer>(renderer, "_instanceFrameDataBuffer"),
                firstVisibleSourceInstance);
            firstVisibleTransformPreview = BuildFirstVisibleTransformPreview(
                firstVisibleSourceInstance,
                firstVisibleTransform,
                firstVisibleFrameData);
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"SampleScene crowd 检查: {scene.path}");
        builder.AppendLine($"camera={camera.name} pos={camera.transform.position} forward={camera.transform.forward}");
        builder.AppendLine($"crowdPos={crowdObject.transform.position} worldBoundsCenter={worldCrowdBounds.center} worldBoundsSize={worldCrowdBounds.size}");
        builder.AppendLine($"crowdDistance={crowdDistance:F2} crowdIntersectsFrustum={crowdIntersectsFrustum}");
        builder.AppendLine(
            $"initialized={initialized} hasRuntimeRenderResource={hasRuntimeRenderResource} hasClip={hasClip} isPlaying={isPlaying}");
        builder.AppendLine(
            $"lodResources=secondary={hasSecondaryRuntimeRenderResource} tertiary={hasTertiaryRuntimeRenderResource} activeVisibleLodTierCount={(activeVisibleLodRenderResources != null ? activeVisibleLodRenderResources.Length : 0)}");
        builder.AppendLine(
            $"templatePrefab={(templatePrefab != null ? templatePrefab.ToString() : "null")} animationAsset={(animationAsset != null ? animationAsset.ToString() : "null")}");
        builder.AppendLine(
            $"runtimeMesh={(runtimeMesh != null ? runtimeMesh.name : "null")} subMeshCount={(runtimeMesh != null ? runtimeMesh.subMeshCount : 0)} runtimeMaterialCount={(runtimeMaterials != null ? runtimeMaterials.Length : 0)} indirectArgsBufferCount={(indirectArgsBuffers != null ? indirectArgsBuffers.Length : 0)}");
        builder.AppendLine(
            $"renderChunkCount={(renderChunks != null ? renderChunks.Length : 0)} expectedVisibleInstanceCount={expectedVisibleInstanceCount}");
        builder.AppendLine(
            $"runtimeSquadStateCount={activeSquadStateCount} agentSquadDataCount={activeAgentSquadDataCount} formationSlotCount={activeFormationSlotCount} runtimeSquadChunkCount={(runtimeSquadRenderChunks != null ? runtimeSquadRenderChunks.Length : 0)}");
        builder.AppendLine(
            $"usesGpuVisibleCompaction={usesGpuVisibleCompaction} visibleUnassignedInstances={visibleUnassignedInstances} aliveInstanceCountBeforeDraw={aliveInstanceCountBeforeDraw} visibleGpuCountBeforeDraw={visibleInstanceCountGpuBeforeDraw}");
        builder.AppendLine(
            $"visibleInstanceCountBeforeDraw={visibleInstanceCountBeforeDraw} hasVisibleBoundsBeforeDraw={hasVisibleBounds} visibleBoundsBeforeDrawCenter={visibleWorldBounds.center} visibleBoundsBeforeDrawSize={visibleWorldBounds.size}");
        builder.AppendLine(
            $"drawInvocation={drawInvocation} visibleInstanceCountAfterDraw={visibleInstanceCountAfterDraw} visibleGpuCountAfterDraw={visibleInstanceCountGpuAfterDraw} usesGpuVisibleCompactionAfterDraw={usesGpuVisibleCompactionAfterDraw} hasVisibleBoundsAfterDraw={hasVisibleBoundsAfterDraw} visibleBoundsAfterDrawCenter={visibleWorldBoundsAfterDraw.center} visibleBoundsAfterDrawSize={visibleWorldBoundsAfterDraw.size}");
        builder.AppendLine(BuildIndirectArgsPreview(indirectArgsAfterDraw));
        builder.AppendLine(BuildRuntimeSquadMaskPreview(visibleRuntimeSquadMask));
        builder.AppendLine(BuildRenderChunkDiagnostics(crowdObject.transform.localToWorldMatrix, renderChunks, frustumPlanes));
        builder.AppendLine(BuildRuntimeSquadChunkDiagnostics(runtimeSquadRenderChunks));
        builder.AppendLine(BuildVisibleInstancePreview(visibleInstanceCountAfterDraw, visibleInstanceIndices));
        builder.AppendLine(BuildGpuVisibleInstancePreview(visibleInstanceCountGpuAfterDraw, visibleInstanceIndicesGpuAfterDraw));
        builder.AppendLine(firstVisibleTransformPreview);
        return builder.ToString();
    }

    private static string BuildResourceDiagnostics()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Crowd VAT 资源检查");
        builder.AppendLine(BuildAssetLine("Walk Clip", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(WalkClipAssetPath), WalkClipAssetPath));
        builder.AppendLine(BuildAssetLine("LOD0 Source Model", AssetDatabase.LoadAssetAtPath<GameObject>(Lod0SourceModelAssetPath), Lod0SourceModelAssetPath));
        builder.AppendLine(BuildAssetLine("LOD1 Source Model", AssetDatabase.LoadAssetAtPath<GameObject>(Lod1SourceModelAssetPath), Lod1SourceModelAssetPath));
        builder.AppendLine(BuildAssetLine("LOD2 Source Model", AssetDatabase.LoadAssetAtPath<GameObject>(Lod2SourceModelAssetPath), Lod2SourceModelAssetPath));
        builder.AppendLine(BuildAssetLine("LOD0 VAT Prefab", AssetDatabase.LoadAssetAtPath<GameObject>(Lod0VatPrefabPath), Lod0VatPrefabPath));
        builder.AppendLine(BuildAssetLine("LOD1 VAT Prefab", AssetDatabase.LoadAssetAtPath<GameObject>(Lod1VatPrefabPath), Lod1VatPrefabPath));
        builder.AppendLine(BuildAssetLine("LOD2 VAT Prefab", AssetDatabase.LoadAssetAtPath<GameObject>(Lod2VatPrefabPath), Lod2VatPrefabPath));
        builder.AppendLine(BuildAssetLine("LOD0 Animation Asset", AssetDatabase.LoadAssetAtPath<CrowdVatAnimationAsset>(Lod0AnimationAssetPath), Lod0AnimationAssetPath));
        builder.AppendLine(BuildAssetLine("LOD1 Animation Asset", AssetDatabase.LoadAssetAtPath<CrowdVatAnimationAsset>(Lod1AnimationAssetPath), Lod1AnimationAssetPath));
        builder.AppendLine(BuildAssetLine("LOD2 Animation Asset", AssetDatabase.LoadAssetAtPath<CrowdVatAnimationAsset>(Lod2AnimationAssetPath), Lod2AnimationAssetPath));
        builder.AppendLine(BuildMeshAssetLine("LOD0 VAT Mesh", Lod0MeshAssetPath));
        builder.AppendLine(BuildMeshAssetLine("LOD1 VAT Mesh", Lod1MeshAssetPath));
        builder.AppendLine(BuildMeshAssetLine("LOD2 VAT Mesh", Lod2MeshAssetPath));
        builder.AppendLine(BuildAssetLine("Compute Shader", AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath), ComputeShaderPath));
        builder.AppendLine(BuildAssetLine("Indirect Shader", AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath), ShaderPath));
        return builder.ToString();
    }

    private static string BuildAssetLine(string label, UnityEngine.Object asset, string path)
    {
        return $"{label}: {(asset != null ? "OK" : "Missing")} ({path})";
    }

    private static string BuildMeshAssetLine(string label, string path)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
            return $"{label}: Missing ({path})";

        ulong totalIndexCount = 0;
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            totalIndexCount += mesh.GetIndexCount(subMeshIndex);

        return $"{label}: OK ({path}) triangles={totalIndexCount / 3} indices={totalIndexCount} vertices={mesh.vertexCount} subMeshes={mesh.subMeshCount}";
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

    private static string BuildGpuVisibleInstancePreview(uint visibleInstanceCount, uint[] visibleInstanceIndices)
    {
        if (visibleInstanceCount == 0 || visibleInstanceIndices == null || visibleInstanceIndices.Length == 0)
            return "gpu visible preview=no-visible-instance";

        StringBuilder builder = new StringBuilder();
        builder.Append("gpu visible preview=");
        for (int previewIndex = 0; previewIndex < visibleInstanceIndices.Length; previewIndex++)
        {
            if (previewIndex > 0)
                builder.Append(" | ");

            builder.Append($"src={visibleInstanceIndices[previewIndex]}");
        }

        return builder.ToString();
    }

    private static string BuildIndirectArgsPreview(uint[] indirectArgsData)
    {
        if (indirectArgsData == null || indirectArgsData.Length < 5)
            return "indirect args=no-data";

        return $"indirect args=indexCountPerInstance={indirectArgsData[0]} instanceCount={indirectArgsData[1]} startIndex={indirectArgsData[2]} baseVertexIndex={indirectArgsData[3]} startInstance={indirectArgsData[4]}";
    }

    private static string BuildFirstVisibleTransformPreview(
        int sourceInstanceIndex,
        MatrixRowsDebugData? transformData,
        Vector4? frameData)
    {
        if (!transformData.HasValue)
            return $"firstVisibleTransform=unavailable source={sourceInstanceIndex}";

        MatrixRowsDebugData rows = transformData.Value;
        Vector3 center = new Vector3(rows.row0.w, rows.row1.w, rows.row2.w);
        Vector3 axisX = new Vector3(rows.row0.x, rows.row0.y, rows.row0.z);
        Vector3 axisY = new Vector3(rows.row1.x, rows.row1.y, rows.row1.z);
        Vector3 axisZ = new Vector3(rows.row2.x, rows.row2.y, rows.row2.z);
        string framePreview = frameData.HasValue ? frameData.Value.ToString("F3") : "null";
        return $"firstVisibleTransform=source={sourceInstanceIndex} center={center} axisXMag={axisX.magnitude:F3} axisYMag={axisY.magnitude:F3} axisZMag={axisZ.magnitude:F3} frameData={framePreview}";
    }

    private static string BuildRuntimeSquadMaskPreview(uint[] visibleRuntimeSquadMask)
    {
        if (visibleRuntimeSquadMask == null || visibleRuntimeSquadMask.Length == 0)
            return "runtime squad mask=no-mask";

        int previewCount = Mathf.Min(8, visibleRuntimeSquadMask.Length);
        StringBuilder builder = new StringBuilder();
        builder.Append("runtime squad mask=");
        for (int index = 0; index < previewCount; index++)
        {
            if (index > 0)
                builder.Append(" | ");

            builder.Append($"{index}:{visibleRuntimeSquadMask[index]}");
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

    private static string BuildRuntimeSquadChunkDiagnostics(Array runtimeSquadRenderChunks)
    {
        if (runtimeSquadRenderChunks == null || runtimeSquadRenderChunks.Length == 0)
            return "runtime squad chunk diagnostics=no-runtime-squad-chunk";

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("runtime squad chunk diagnostics:");

        for (int chunkIndex = 0; chunkIndex < runtimeSquadRenderChunks.Length; chunkIndex++)
        {
            object chunk = runtimeSquadRenderChunks.GetValue(chunkIndex);
            uint[] instanceIndices = GetPrivateFieldValue<uint[]>(chunk, "instanceIndices");
            int squadIndex = GetPrivateFieldValue<int>(chunk, "squadIndex");
            bool usesRuntimeBounds = GetPrivateFieldValue<bool>(chunk, "usesRuntimeBounds");
            bool hasSlotExtents = GetPrivateFieldValue<bool>(chunk, "hasSlotExtents");

            builder.AppendLine(
                $"  runtimeChunk[{chunkIndex}] squadIndex={squadIndex} instanceCount={(instanceIndices != null ? instanceIndices.Length : 0)} usesRuntimeBounds={usesRuntimeBounds} hasSlotExtents={hasSlotExtents}");
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

    private static uint ReadCounterValue(ComputeBuffer buffer)
    {
        if (buffer == null)
            return 0u;

        uint[] data = new uint[1];
        buffer.GetData(data, 0, 0, 1);
        return data[0];
    }

    private static uint[] ReadUIntBuffer(ComputeBuffer buffer, int count)
    {
        if (buffer == null || count <= 0)
            return Array.Empty<uint>();

        uint[] data = new uint[count];
        buffer.GetData(data, 0, 0, count);
        return data;
    }

    private static uint[] ReadGraphicsUIntBuffer(GraphicsBuffer buffer, int count)
    {
        if (buffer == null || count <= 0)
            return Array.Empty<uint>();

        uint[] data = new uint[count];
        buffer.GetData(data, 0, 0, count);
        return data;
    }

    private static MatrixRowsDebugData? ReadMatrixRowsBuffer(ComputeBuffer buffer, int index)
    {
        if (buffer == null || index < 0 || index >= buffer.count)
            return null;

        MatrixRowsDebugData[] data = new MatrixRowsDebugData[1];
        buffer.GetData(data, 0, index, 1);
        return data[0];
    }

    private static Vector4? ReadVector4Buffer(ComputeBuffer buffer, int index)
    {
        if (buffer == null || index < 0 || index >= buffer.count)
            return null;

        Vector4[] data = new Vector4[1];
        buffer.GetData(data, 0, index, 1);
        return data[0];
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
