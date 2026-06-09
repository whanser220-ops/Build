using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class CrowdVatIndirectRenderer : MonoBehaviour, IGpuSemanticDrawProvider, IGpuSemanticDrawCommandProvider
{
    private int _gpuSemanticPreparedFrame = -1;
    private int _gpuSemanticPreparedCameraInstanceId = -1;
    private bool _gpuSemanticOpaqueDrawReady;
    private bool _gpuSemanticUsesVisibleCompaction;

    private void Draw()
    {
        Draw(ResolveRenderCamera());
    }

    private void Draw(Camera targetCamera)
    {
        if (!TryPrepareCrowdSkinnedMeshDraw(targetCamera, out bool usesGpuVisibleCompaction, out Bounds worldBounds))
            return;

        for (int tierIndex = 0; tierIndex < _activeVisibleLodRenderResources.Length; tierIndex++)
        {
            RuntimeRenderResource resource = _activeVisibleLodRenderResources[tierIndex];
            if (resource.runtimeMaterials == null || resource.mesh == null)
                continue;

            for (int subMeshIndex = 0; subMeshIndex < resource.runtimeMaterials.Length; subMeshIndex++)
            {
                if (!TryPrepareCrowdSubMeshArgs(tierIndex, resource, subMeshIndex, usesGpuVisibleCompaction, out GraphicsBuffer argsBuffer))
                    continue;

                Material material = resource.runtimeMaterials[subMeshIndex];
                if (material == null)
                    continue;

                RenderParams renderParams = new RenderParams(material)
                {
                    camera = targetCamera,
                    instanceID = gameObject.GetInstanceID(),
                    worldBounds = _visibleWorldBounds,
                    shadowCastingMode = _shadowCastingMode,
                    receiveShadows = _receiveShadows,
                    layer = gameObject.layer
                };

                DrawGpuPassRenderMeshIndirect(
                    ResolveCrowdRenderSkinnedMeshMarker(tierIndex),
                    renderParams,
                    resource.mesh,
                    argsBuffer,
                    1,
                    0);
            }
        }

        DrawCombatTracers(targetCamera, worldBounds, usesGpuVisibleCompaction);
    }

    private void DrawCombatTracers(Camera targetCamera, Bounds crowdWorldBounds, bool usesGpuVisibleCompaction)
    {
        if (!HasCombatTracerDrawResources())
        {
            return;
        }

        UpdateCombatTracerMaterialParameters();

        float tracerBoundsPadding = Mathf.Max(_combatRange, 0.0f) +
            Mathf.Max(Mathf.Max(_combatTracerWidth, _combatMuzzleFlashSize), _combatImpactFlashSize) * 8.0f;
        Bounds tracerWorldBounds = ExpandBounds(crowdWorldBounds, tracerBoundsPadding);
        CommandBuffer commandBuffer = null;
        bool wrapMarker = ShouldWrapGpuPassDispatch(GpuPassRenderCombatTracerMarker) &&
            TryBeginImmediateGpuPassMarker(GpuPassRenderCombatTracerMarker, out commandBuffer);

        try
        {
            int tracerTierCount = ResolveCombatTracerTierCount();
            for (int tierIndex = 0; tierIndex < tracerTierCount; tierIndex++)
            {
                if (!TryPrepareCombatTracerTierDraw(tierIndex, usesGpuVisibleCompaction, out Material combatTracerMaterial, out GraphicsBuffer argsBuffer))
                    continue;

                RenderParams renderParams = new RenderParams(combatTracerMaterial)
                {
                    camera = targetCamera,
                    instanceID = gameObject.GetInstanceID(),
                    worldBounds = tracerWorldBounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    layer = gameObject.layer
                };

                Graphics.RenderMeshIndirect(renderParams, _combatTracerMesh, argsBuffer, 1, 0);
            }
        }
        finally
        {
            if (wrapMarker)
                EndImmediateGpuPassMarker(GpuPassRenderCombatTracerMarker, commandBuffer);
        }
    }

    public void RecordGpuSemanticDraws(RasterCommandBuffer commandBuffer, Camera camera, GpuSemanticDrawPhase phase)
    {
        if (!ShouldUseGpuSemanticDrawPass(camera) || commandBuffer == null)
            return;

        if (phase == GpuSemanticDrawPhase.Opaque)
        {
            RecordCrowdSkinnedMeshSemanticDraws(commandBuffer, camera, true);
            return;
        }

        if (phase == GpuSemanticDrawPhase.Transparent)
            RecordCrowdCombatTracerSemanticDraws(commandBuffer, camera, true);
    }

    public void CollectGpuSemanticDrawCommands(List<GpuSemanticDrawCommand> commands, Camera camera, GpuSemanticDrawPhase phase)
    {
        if (commands == null || !ShouldUseGpuSemanticDrawPass(camera))
            return;

        if (phase == GpuSemanticDrawPhase.Opaque)
        {
            if (HasPreparedGpuSemanticDrawState(camera))
            {
                int activeLodCount = _activeVisibleLodRenderResources != null ? _activeVisibleLodRenderResources.Length : 0;
                if (activeLodCount > 0)
                    commands.Add(new GpuSemanticDrawCommand(ResolveCrowdRenderSkinnedMeshMarker(0), RecordCrowdSkinnedMeshLod0SemanticDraw));
                if (activeLodCount > 1)
                    commands.Add(new GpuSemanticDrawCommand(ResolveCrowdRenderSkinnedMeshMarker(1), RecordCrowdSkinnedMeshLod1SemanticDraw));
                if (activeLodCount > 2)
                    commands.Add(new GpuSemanticDrawCommand(ResolveCrowdRenderSkinnedMeshMarker(2), RecordCrowdSkinnedMeshLod2SemanticDraw));
            }

            return;
        }

        if (phase == GpuSemanticDrawPhase.Transparent && CanRecordCrowdCombatTracerSemanticDraw(camera))
            commands.Add(new GpuSemanticDrawCommand(GpuPassRenderCombatTracerMarker, RecordCrowdCombatTracerSemanticDraw));
    }

    private void RecordCrowdSkinnedMeshSemanticDraw(RasterCommandBuffer commandBuffer, Camera targetCamera)
    {
        RecordCrowdSkinnedMeshSemanticDraws(commandBuffer, targetCamera, false);
    }

    private void RecordCrowdSkinnedMeshLod0SemanticDraw(RasterCommandBuffer commandBuffer, Camera targetCamera)
    {
        RecordCrowdSkinnedMeshLodSemanticDraw(commandBuffer, targetCamera, 0, false);
    }

    private void RecordCrowdSkinnedMeshLod1SemanticDraw(RasterCommandBuffer commandBuffer, Camera targetCamera)
    {
        RecordCrowdSkinnedMeshLodSemanticDraw(commandBuffer, targetCamera, 1, false);
    }

    private void RecordCrowdSkinnedMeshLod2SemanticDraw(RasterCommandBuffer commandBuffer, Camera targetCamera)
    {
        RecordCrowdSkinnedMeshLodSemanticDraw(commandBuffer, targetCamera, 2, false);
    }

    private void RecordCrowdSkinnedMeshSemanticDraws(RasterCommandBuffer commandBuffer, Camera targetCamera, bool wrapMarker)
    {
        if (!HasPreparedGpuSemanticDrawState(targetCamera))
            return;

        int activeLodCount = _activeVisibleLodRenderResources != null ? _activeVisibleLodRenderResources.Length : 0;
        for (int tierIndex = 0; tierIndex < activeLodCount; tierIndex++)
            RecordCrowdSkinnedMeshLodSemanticDraw(commandBuffer, targetCamera, tierIndex, wrapMarker);
    }

    private void RecordCrowdSkinnedMeshLodSemanticDraw(
        RasterCommandBuffer commandBuffer,
        Camera targetCamera,
        int tierIndex,
        bool wrapMarker)
    {
        if (!HasPreparedGpuSemanticDrawState(targetCamera) ||
            _activeVisibleLodRenderResources == null ||
            tierIndex < 0 ||
            tierIndex >= _activeVisibleLodRenderResources.Length)
        {
            return;
        }

        bool usesGpuVisibleCompaction = _gpuSemanticUsesVisibleCompaction;
        RuntimeRenderResource resource = _activeVisibleLodRenderResources[tierIndex];
        if (resource.runtimeMaterials == null || resource.mesh == null)
            return;

        string markerLabel = ResolveCrowdRenderSkinnedMeshMarker(tierIndex);
        bool recordedAnyDraw = false;

        for (int subMeshIndex = 0; subMeshIndex < resource.runtimeMaterials.Length; subMeshIndex++)
        {
            if (!TryPrepareCrowdSubMeshArgs(tierIndex, resource, subMeshIndex, usesGpuVisibleCompaction, out GraphicsBuffer argsBuffer))
                continue;

            Material material = resource.runtimeMaterials[subMeshIndex];
            if (material == null)
                continue;

            int forwardPassIndex = GpuSemanticDrawPassUtility.ResolveMaterialPassIndex(material, CrowdForwardLitPassName);

            if (!recordedAnyDraw)
            {
                if (wrapMarker)
                    commandBuffer.BeginSample(markerLabel);

                recordedAnyDraw = true;
            }

            commandBuffer.DrawMeshInstancedIndirect(resource.mesh, subMeshIndex, material, forwardPassIndex, argsBuffer, 0);
        }

        if (recordedAnyDraw && wrapMarker)
            commandBuffer.EndSample(markerLabel);
    }

    private void PrepareGpuSemanticDrawState(Camera targetCamera)
    {
        _gpuSemanticPreparedFrame = Time.frameCount;
        _gpuSemanticPreparedCameraInstanceId = targetCamera != null ? targetCamera.GetInstanceID() : -1;
        _gpuSemanticOpaqueDrawReady = false;
        _gpuSemanticUsesVisibleCompaction = false;

        if (!TryPrepareCrowdSkinnedMeshDraw(targetCamera, out bool usesGpuVisibleCompaction, out _))
            return;

        _gpuSemanticOpaqueDrawReady = true;
        _gpuSemanticUsesVisibleCompaction = usesGpuVisibleCompaction;
    }

    private bool HasPreparedGpuSemanticDrawState(Camera targetCamera)
    {
        return _gpuSemanticOpaqueDrawReady &&
            _gpuSemanticPreparedFrame == Time.frameCount &&
            targetCamera != null &&
            _gpuSemanticPreparedCameraInstanceId == targetCamera.GetInstanceID();
    }

    private void RecordCrowdCombatTracerSemanticDraw(RasterCommandBuffer commandBuffer, Camera targetCamera)
    {
        RecordCrowdCombatTracerSemanticDraws(commandBuffer, targetCamera, false);
    }

    private void RecordCrowdCombatTracerSemanticDraws(RasterCommandBuffer commandBuffer, Camera targetCamera, bool wrapMarker)
    {
        if (!CanRecordCrowdCombatTracerSemanticDraw(targetCamera))
            return;

        UpdateCombatTracerMaterialParameters();

        bool recordedAnyDraw = false;
        int tracerTierCount = ResolveCombatTracerTierCount();
        for (int tierIndex = 0; tierIndex < tracerTierCount; tierIndex++)
        {
            if (!TryPrepareCombatTracerTierDraw(tierIndex, _gpuSemanticUsesVisibleCompaction, out Material combatTracerMaterial, out GraphicsBuffer argsBuffer))
                continue;

            int forwardPassIndex = GpuSemanticDrawPassUtility.ResolveMaterialPassIndex(combatTracerMaterial, CrowdCombatTracerForwardPassName);
            if (!recordedAnyDraw && wrapMarker)
                commandBuffer.BeginSample(GpuPassRenderCombatTracerMarker);

            commandBuffer.DrawMeshInstancedIndirect(_combatTracerMesh, 0, combatTracerMaterial, forwardPassIndex, argsBuffer, 0);
            recordedAnyDraw = true;
        }

        if (recordedAnyDraw && wrapMarker)
            commandBuffer.EndSample(GpuPassRenderCombatTracerMarker);
    }

    private bool CanRecordCrowdCombatTracerSemanticDraw(Camera targetCamera)
    {
        return HasPreparedGpuSemanticDrawState(targetCamera) && HasCombatTracerDrawResources();
    }

    private bool TryPrepareCrowdSkinnedMeshDraw(
        Camera targetCamera,
        out bool usesGpuVisibleCompaction,
        out Bounds worldBounds)
    {
        usesGpuVisibleCompaction = false;
        worldBounds = TransformBounds(transform.localToWorldMatrix, _localCrowdBounds);

        if (!_hasRuntimeRenderResource || !AreActiveVisibleLodArgsReady())
            return false;

        bool isSceneViewCamera = IsSceneViewCamera(targetCamera);
        bool hasFrustumPlanes = false;

        if (_enableFrustumCulling && targetCamera != null && !isSceneViewCamera)
        {
            GeometryUtility.CalculateFrustumPlanes(targetCamera, _frustumPlanes);
            hasFrustumPlanes = true;
        }

        BuildVisibleIndirectCommands(hasFrustumPlanes, targetCamera);

        usesGpuVisibleCompaction = _usesGpuVisibleInstanceCompactionThisFrame;
        if ((!usesGpuVisibleCompaction && GetVisibleInstanceCountTotal() <= 0) ||
            !_hasVisibleBounds ||
            _activeVisibleLodRenderResources == null ||
            _activeVisibleLodRenderResources.Length == 0)
        {
            return false;
        }

        if (!usesGpuVisibleCompaction)
            UploadVisibleInstanceCachesCpu();

        return true;
    }

    private bool TryPrepareCrowdSubMeshArgs(
        int tierIndex,
        RuntimeRenderResource resource,
        int subMeshIndex,
        bool usesGpuVisibleCompaction,
        out GraphicsBuffer argsBuffer)
    {
        argsBuffer = null;
        GraphicsBuffer[] argsBuffers = ResolveActiveVisibleLodArgsBuffers(tierIndex);
        GraphicsBuffer.IndirectDrawIndexedArgs[] argsCache = ResolveActiveVisibleLodArgsCache(tierIndex);
        if (resource.runtimeMaterials == null ||
            subMeshIndex < 0 ||
            subMeshIndex >= resource.runtimeMaterials.Length ||
            argsBuffers == null ||
            subMeshIndex >= argsBuffers.Length)
        {
            return false;
        }

        argsBuffer = argsBuffers[subMeshIndex];
        if (argsBuffer == null)
            return false;

        if (usesGpuVisibleCompaction)
            return true;

        GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(resource.mesh, subMeshIndex, (uint)ResolveVisibleInstanceCountForTier(tierIndex));
        if (argsCache != null && subMeshIndex < argsCache.Length)
            argsCache[subMeshIndex] = args;
        argsBuffer.SetData(new[] { args });
        return true;
    }

    private bool ShouldUseGpuSemanticDrawPass(Camera camera)
    {
        return GpuSemanticDrawFeature.IsInstalled &&
            GpuPassDebugRuntime.CaptureMetadataEnabled &&
            ShouldRenderForCamera(camera);
    }

    private Camera ResolveRenderCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        if (Camera.current != null)
            return Camera.current;

        return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
    }

    private void SubscribeRenderCallbacks()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void UnsubscribeRenderCallbacks()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private static bool ShouldRenderForCamera(Camera camera)
    {
        if (camera == null || !camera.enabled)
            return false;

        return camera.cameraType != CameraType.Preview && camera.cameraType != CameraType.Reflection;
    }

    private static bool IsSceneViewCamera(Camera camera)
    {
        return camera != null && camera.cameraType == CameraType.SceneView;
    }

    private void BuildVisibleIndirectCommands(bool hasFrustumPlanes, Camera targetCamera)
    {
        ResetVisibleIndirectCommands();
        _usesGpuVisibleInstanceCompactionThisFrame = false;
        _visibleUnassignedInstancesThisFrame = false;
        ConfigureVisibleComputeParameters(hasFrustumPlanes, targetCamera);

        if (CanUseGpuDrivenVisibleCompaction())
        {
            BuildVisibleGpuDrivenCommands();
            return;
        }

        BuildVisibleWholeCrowdCommandsCpu();
    }

    private bool CanUseGpuDrivenVisibleCompaction()
    {
        return CanUseGpuVisibleInstanceCompactionCommon() &&
            _buildVisibleRuntimeInstanceListKernel >= 0 &&
            _agentSquadDataBuffer != null &&
            _visibleRuntimeSquadMaskBuffer != null;
    }

    private void BuildVisibleGpuDrivenCommands()
    {
        // GPU-driven path keeps the CPU side on conservative whole-crowd bounds,
        // while the GPU refines visibility from squad AABBs down to instances.
        AppendVisibleWorldBounds(TransformBounds(transform.localToWorldMatrix, _localCrowdBounds));
        if (CanUseGpuVisibleRuntimeSquadAabbCulling())
        {
            BuildVisibleRuntimeSquadAabbCommandsGpu();
        }
        else
        {
            ClearVisibleRuntimeSquadMaskCache();
            BuildVisibleRuntimeInstanceListGpu(renderAllAliveInstances: true);
        }

        _usesGpuVisibleInstanceCompactionThisFrame = true;
    }

    private bool CanUseGpuVisibleRuntimeSquadAabbCulling()
    {
        return _enableFrustumCulling &&
            HasRuntimeSquadAnchorData() &&
            CanUseGpuVisibleInstanceCompactionCommon() &&
            _clearVisibleRuntimeSquadBoundsKernel >= 0 &&
            _buildVisibleRuntimeSquadBoundsKernel >= 0 &&
            _cullVisibleRuntimeSquadsKernel >= 0 &&
            _runtimeSquadBoundsBuffer != null;
    }

    private void BuildVisibleRuntimeSquadAabbCommandsGpu()
    {
        ClearVisibleRuntimeSquadMaskCache();
        _visibleUnassignedInstancesThisFrame = _hasUnassignedRuntimeInstances;

        BindClearVisibleRuntimeSquadBoundsKernel();
        DispatchSquads(_clearVisibleRuntimeSquadBoundsKernel, _activeSquadStateCount);

        BindBuildVisibleRuntimeSquadBoundsKernel();
        DispatchAliveInstancesIndirect(_buildVisibleRuntimeSquadBoundsKernel);

        BindCullVisibleRuntimeSquadsKernel();
        DispatchSquads(_cullVisibleRuntimeSquadsKernel, _activeSquadStateCount);

        BuildVisibleRuntimeInstanceListGpu(renderAllAliveInstances: false, uploadVisibleRuntimeSquadMask: false);
    }

    private void BuildVisibleWholeCrowdCommandsCpu()
    {
        Bounds worldBounds = TransformBounds(transform.localToWorldMatrix, _localCrowdBounds);
        AddChunkToVisibleCommands(BuildAllInstanceIndices(), worldBounds);
    }

    private bool CanUseGpuVisibleInstanceCompactionCommon()
    {
        return _updateCompute != null &&
            _buildVisibleIndirectArgsKernel >= 0 &&
            _visibleInstanceIndexBuffer != null &&
            _visibleInstanceCounterBuffer != null &&
            _visibleLod1InstanceIndexBuffer != null &&
            _visibleLod1InstanceCounterBuffer != null &&
            _visibleLod2InstanceIndexBuffer != null &&
            _visibleLod2InstanceCounterBuffer != null &&
            _aliveInstanceIndexBuffer != null &&
            _aliveInstanceCounterBuffer != null &&
            _aliveInstanceDispatchArgsBuffer != null &&
            _deathStateBuffer != null &&
            HasSimulationReadBuffers() &&
            AreActiveVisibleLodArgsReady();
    }

    private void ResetVisibleIndirectCommands()
    {
        _visibleInstanceCount = 0;
        _visibleLod1InstanceCount = 0;
        _visibleLod2InstanceCount = 0;
        _activeVisibleLodTierCountThisFrame = 0;
        _visibleLod1StartDistanceThisFrame = float.MaxValue;
        _visibleLod2StartDistanceThisFrame = float.MaxValue;
        _visibleCameraPositionThisFrame = Vector3.zero;
        _hasVisibleBounds = false;
        _visibleWorldBounds = default;
    }

    private void AppendVisibleWorldBounds(Bounds worldChunkBounds)
    {
        if (!_hasVisibleBounds)
        {
            _visibleWorldBounds = worldChunkBounds;
            _hasVisibleBounds = true;
            return;
        }

        _visibleWorldBounds.Encapsulate(worldChunkBounds.min);
        _visibleWorldBounds.Encapsulate(worldChunkBounds.max);
    }

    private void ClearVisibleRuntimeSquadMaskCache()
    {
        if (_visibleRuntimeSquadMaskUploadCache == null || _visibleRuntimeSquadMaskUploadCache.Length == 0)
            return;

        Array.Clear(_visibleRuntimeSquadMaskUploadCache, 0, _visibleRuntimeSquadMaskUploadCache.Length);
        _visibleUnassignedInstancesThisFrame = false;
    }

    private void BuildVisibleRuntimeInstanceListGpu(bool renderAllAliveInstances = false, bool uploadVisibleRuntimeSquadMask = true)
    {
        if (_visibleInstanceCounterBuffer == null ||
            _visibleLod1InstanceCounterBuffer == null ||
            _visibleLod2InstanceCounterBuffer == null ||
            _visibleRuntimeSquadMaskBuffer == null ||
            _visibleInstanceIndexBuffer == null ||
            _visibleLod1InstanceIndexBuffer == null ||
            _visibleLod2InstanceIndexBuffer == null)
        {
            return;
        }

        _visibleInstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        _visibleLod1InstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        _visibleLod2InstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        if (!renderAllAliveInstances && uploadVisibleRuntimeSquadMask)
            UploadVisibleRuntimeSquadMaskCache();

        BindBuildVisibleRuntimeInstanceListKernel(renderAllAliveInstances);
        DispatchAliveInstancesIndirect(_buildVisibleRuntimeInstanceListKernel);
        BuildVisibleIndirectArgsGpu();
    }

    private bool UploadVisibleRuntimeSquadMaskCache()
    {
        if (_visibleRuntimeSquadMaskBuffer == null)
            return false;

        int uploadCount = Mathf.Min(_visibleRuntimeSquadMaskUploadCache.Length, _visibleRuntimeSquadMaskBuffer.count);
        if (uploadCount <= 0)
            return false;

        _visibleRuntimeSquadMaskBuffer.SetData(_visibleRuntimeSquadMaskUploadCache, 0, 0, uploadCount);
        return true;
    }

    private void BuildVisibleIndirectArgsGpu()
    {
        if (_activeVisibleLodRenderResources == null || _activeVisibleLodArgsBuffers == null)
            return;

        for (int tierIndex = 0; tierIndex < _activeVisibleLodRenderResources.Length; tierIndex++)
        {
            RuntimeRenderResource resource = _activeVisibleLodRenderResources[tierIndex];
            GraphicsBuffer[] argsBuffers = ResolveActiveVisibleLodArgsBuffers(tierIndex);
            ComputeBuffer visibleCounterBuffer = ResolveVisibleInstanceCounterBufferForTier(tierIndex);
            if (resource.mesh == null || argsBuffers == null || visibleCounterBuffer == null)
                continue;

            int subMeshCount = Mathf.Min(resource.mesh.subMeshCount, argsBuffers.Length);
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                GraphicsBuffer argsBuffer = argsBuffers[subMeshIndex];
                if (argsBuffer == null)
                    continue;

                BindBuildVisibleIndirectArgsKernel(
                    argsBuffer,
                    resource.mesh,
                    subMeshIndex,
                    visibleCounterBuffer,
                    $"visibleInstanceCounter.lod{tierIndex}");
                DispatchGpuPass(_buildVisibleIndirectArgsKernel, 1, 1, 1);
            }
        }

        BuildCombatTracerArgsGpu();
    }

    private void ConfigureVisibleComputeParameters(bool hasFrustumPlanes, Camera targetCamera)
    {
        if (_updateCompute == null)
            return;

        Bounds referenceAgentLocalBounds = ResolveReferenceAgentLocalBounds();
        float dynamicPadding = _boundsPadding + (_enableApproximateCollision ? _maxDisplacementFromSpawn : 0.0f);
        Vector3 expandedReferenceExtents = referenceAgentLocalBounds.extents + Vector3.one * Mathf.Max(0.0f, dynamicPadding);
        Vector3 maxCornerFromOrigin = new Vector3(
            Mathf.Abs(referenceAgentLocalBounds.center.x) + expandedReferenceExtents.x,
            Mathf.Abs(referenceAgentLocalBounds.center.y) + expandedReferenceExtents.y,
            Mathf.Abs(referenceAgentLocalBounds.center.z) + expandedReferenceExtents.z);

        _updateCompute.SetVector(
            VisibleBoundsCenterId,
            new Vector4(referenceAgentLocalBounds.center.x, referenceAgentLocalBounds.center.y, referenceAgentLocalBounds.center.z, 0.0f));
        _updateCompute.SetVector(
            VisibleBoundsExtentsId,
            new Vector4(expandedReferenceExtents.x, expandedReferenceExtents.y, expandedReferenceExtents.z, 0.0f));
        _updateCompute.SetFloat(VisibleInstanceBoundsRadiusId, Mathf.Max(0.01f, maxCornerFromOrigin.magnitude));
        _updateCompute.SetInt(HasVisibleFrustumPlanesId, hasFrustumPlanes ? 1 : 0);
        _updateCompute.SetVector(VisibleFrustumPlane0Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[0]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane1Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[1]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane2Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[2]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane3Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[3]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane4Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[4]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane5Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[5]) : Vector4.zero);

        ConfigureActiveVisibleLodRanges();
        _visibleCameraPositionThisFrame = targetCamera != null ? targetCamera.transform.position : Vector3.zero;
        _updateCompute.SetVector(
            VisibleCameraPositionId,
            _visibleCameraPositionThisFrame);
        _updateCompute.SetInt(VisibleLodTierCountId, Mathf.Max(1, _activeVisibleLodTierCountThisFrame));
        _updateCompute.SetFloat(VisibleLod1StartDistanceId, _visibleLod1StartDistanceThisFrame);
        _updateCompute.SetFloat(VisibleLod2StartDistanceId, _visibleLod2StartDistanceThisFrame);
    }

    private static Vector4 EncodePlane(Plane plane)
    {
        return new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
    }

    private void AddChunkToVisibleCommands(uint[] instanceIndices, Bounds worldChunkBounds)
    {
        if (instanceIndices == null || instanceIndices.Length == 0)
            return;

        if (_activeVisibleLodTierCountThisFrame <= 1)
        {
            int writeOffset = _visibleInstanceCount;
            if (writeOffset + instanceIndices.Length > _visibleInstanceIndexCache.Length)
                return;

            AppendVisibleWorldBounds(worldChunkBounds);
            Array.Copy(instanceIndices, 0, _visibleInstanceIndexCache, writeOffset, instanceIndices.Length);
            _visibleInstanceCount = writeOffset + instanceIndices.Length;
            return;
        }

        AppendVisibleWorldBounds(worldChunkBounds);
        for (int index = 0; index < instanceIndices.Length; index++)
            AddVisibleCpuInstance(instanceIndices[index]);
    }

    private void AddVisibleCpuInstance(uint instanceIndex)
    {
        int lodTier = ResolveVisibleLodTierForCpuInstance(instanceIndex);
        switch (lodTier)
        {
            case 0:
                if (_visibleInstanceCount < _visibleInstanceIndexCache.Length)
                    _visibleInstanceIndexCache[_visibleInstanceCount++] = instanceIndex;
                return;
            case 1:
                if (_visibleLod1InstanceCount < _visibleLod1InstanceIndexCache.Length)
                    _visibleLod1InstanceIndexCache[_visibleLod1InstanceCount++] = instanceIndex;
                return;
            default:
                if (_visibleLod2InstanceCount < _visibleLod2InstanceIndexCache.Length)
                    _visibleLod2InstanceIndexCache[_visibleLod2InstanceCount++] = instanceIndex;
                return;
        }
    }

    private int ResolveVisibleLodTierForCpuInstance(uint instanceIndex)
    {
        int cacheIndex = (int)instanceIndex;
        if (_spawnDataCache == null || cacheIndex >= _spawnDataCache.Length)
            return 0;

        Vector3 worldPosition = transform.TransformPoint(_spawnDataCache[cacheIndex].localPosition);
        Vector3 cameraDelta = worldPosition - _visibleCameraPositionThisFrame;
        float distanceSquared = cameraDelta.sqrMagnitude;
        if (_activeVisibleLodTierCountThisFrame <= 1)
            return 0;

        if (distanceSquared < _visibleLod1StartDistanceThisFrame * _visibleLod1StartDistanceThisFrame)
            return 0;

        if (_activeVisibleLodTierCountThisFrame <= 2 ||
            distanceSquared < _visibleLod2StartDistanceThisFrame * _visibleLod2StartDistanceThisFrame)
        {
            return 1;
        }

        return 2;
    }

    private uint[] BuildAllInstanceIndices()
    {
        if (_allInstanceIndicesCache == null || _allInstanceIndicesCache.Length != _instanceCount)
        {
            _allInstanceIndicesCache = new uint[Mathf.Max(1, _instanceCount)];
            for (uint instanceIndex = 0; instanceIndex < _allInstanceIndicesCache.Length; instanceIndex++)
                _allInstanceIndicesCache[instanceIndex] = instanceIndex;
        }

        return _allInstanceIndicesCache;
    }

    private void ConfigureActiveVisibleLodRanges()
    {
        _activeVisibleLodTierCountThisFrame = _activeVisibleLodRenderResources != null
            ? Mathf.Max(1, _activeVisibleLodRenderResources.Length)
            : 1;
        _visibleLod1StartDistanceThisFrame = float.MaxValue;
        _visibleLod2StartDistanceThisFrame = float.MaxValue;

        if (_activeVisibleLodTierCountThisFrame >= 2)
        {
            _visibleLod1StartDistanceThisFrame = _hasSecondaryRuntimeRenderResource
                ? Mathf.Max(0.0f, _secondaryLodStartDistance)
                : Mathf.Max(0.0f, _tertiaryLodStartDistance);
        }

        if (_activeVisibleLodTierCountThisFrame >= 3)
            _visibleLod2StartDistanceThisFrame = Mathf.Max(_visibleLod1StartDistanceThisFrame, _tertiaryLodStartDistance);
    }

    private int GetVisibleInstanceCountTotal()
    {
        return _visibleInstanceCount + _visibleLod1InstanceCount + _visibleLod2InstanceCount;
    }

    private void UploadVisibleInstanceCachesCpu()
    {
        if (_visibleInstanceCount > 0)
            _visibleInstanceIndexBuffer.SetData(_visibleInstanceIndexCache, 0, 0, _visibleInstanceCount);
        if (_visibleLod1InstanceCount > 0)
            _visibleLod1InstanceIndexBuffer.SetData(_visibleLod1InstanceIndexCache, 0, 0, _visibleLod1InstanceCount);
        if (_visibleLod2InstanceCount > 0)
            _visibleLod2InstanceIndexBuffer.SetData(_visibleLod2InstanceIndexCache, 0, 0, _visibleLod2InstanceCount);
    }

    private int ResolveVisibleInstanceCountForTier(int tierIndex)
    {
        return tierIndex switch
        {
            0 => _visibleInstanceCount,
            1 => _visibleLod1InstanceCount,
            2 => _visibleLod2InstanceCount,
            _ => 0
        };
    }

    private ComputeBuffer ResolveVisibleInstanceCounterBufferForTier(int tierIndex)
    {
        return tierIndex switch
        {
            0 => _visibleInstanceCounterBuffer,
            1 => _visibleLod1InstanceCounterBuffer,
            2 => _visibleLod2InstanceCounterBuffer,
            _ => null
        };
    }

    private GraphicsBuffer[] ResolveActiveVisibleLodArgsBuffers(int tierIndex)
    {
        if (_activeVisibleLodArgsBuffers == null || tierIndex < 0 || tierIndex >= _activeVisibleLodArgsBuffers.Length)
            return null;

        return _activeVisibleLodArgsBuffers[tierIndex];
    }

    private GraphicsBuffer.IndirectDrawIndexedArgs[] ResolveActiveVisibleLodArgsCache(int tierIndex)
    {
        return tierIndex switch
        {
            0 => _indirectArgsCache,
            1 => _visibleLod1IndirectArgsCache,
            2 => _visibleLod2IndirectArgsCache,
            _ => null
        };
    }

    private int ResolveCombatTracerTierCount()
    {
        if (_activeVisibleLodRenderResources == null ||
            _combatTracerMaterials == null ||
            _combatTracerArgsBuffers == null)
        {
            return 0;
        }

        return Mathf.Min(
            _activeVisibleLodRenderResources.Length,
            Mathf.Min(_combatTracerMaterials.Length, _combatTracerArgsBuffers.Length));
    }

    private bool HasCombatTracerDrawResources()
    {
        return _enableCombatTracer &&
            _enableGpuInstanceCombat &&
            _combatTracerMesh != null &&
            ResolveCombatTracerTierCount() > 0;
    }

    private bool TryPrepareCombatTracerTierDraw(
        int tierIndex,
        bool usesGpuVisibleCompaction,
        out Material combatTracerMaterial,
        out GraphicsBuffer argsBuffer)
    {
        combatTracerMaterial = null;
        argsBuffer = null;

        int tracerTierCount = ResolveCombatTracerTierCount();
        if (tierIndex < 0 || tierIndex >= tracerTierCount)
            return false;

        combatTracerMaterial = _combatTracerMaterials[tierIndex];
        argsBuffer = _combatTracerArgsBuffers[tierIndex];
        if (combatTracerMaterial == null || argsBuffer == null)
            return false;

        if (usesGpuVisibleCompaction)
            return true;

        int visibleInstanceCount = ResolveVisibleInstanceCountForTier(tierIndex);
        if (visibleInstanceCount <= 0)
            return false;

        GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(_combatTracerMesh, 0, (uint)visibleInstanceCount);
        if (_combatTracerArgsCache != null && tierIndex < _combatTracerArgsCache.Length)
            _combatTracerArgsCache[tierIndex] = args;
        argsBuffer.SetData(new[] { args });
        return true;
    }

    private void BuildCombatTracerArgsGpu()
    {
        if (_combatTracerMesh == null || _combatTracerArgsBuffers == null)
            return;

        int tracerTierCount = ResolveCombatTracerTierCount();
        for (int tierIndex = 0; tierIndex < tracerTierCount; tierIndex++)
        {
            GraphicsBuffer argsBuffer = _combatTracerArgsBuffers[tierIndex];
            ComputeBuffer visibleCounterBuffer = ResolveVisibleInstanceCounterBufferForTier(tierIndex);
            if (argsBuffer == null || visibleCounterBuffer == null)
                continue;

            BindBuildVisibleIndirectArgsKernel(
                argsBuffer,
                _combatTracerMesh,
                0,
                visibleCounterBuffer,
                $"visibleInstanceCounter.combatTracer.lod{tierIndex}");
            DispatchGpuPass(_buildVisibleIndirectArgsKernel, 1, 1, 1);
        }
    }

}
