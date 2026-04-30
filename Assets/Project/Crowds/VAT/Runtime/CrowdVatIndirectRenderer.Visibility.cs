using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class CrowdVatIndirectRenderer : MonoBehaviour
{
    private void Draw()
    {
        Draw(ResolveRenderCamera());
    }

    private void Draw(Camera targetCamera)
    {
        if (!_hasRuntimeRenderResource || _indirectArgsBuffers.Length == 0)
            return;

        bool isSceneViewCamera = IsSceneViewCamera(targetCamera);
        Bounds worldBounds = TransformBounds(transform.localToWorldMatrix, _localCrowdBounds);
        bool hasFrustumPlanes = false;

        if (_enableFrustumCulling && targetCamera != null && !isSceneViewCamera)
        {
            GeometryUtility.CalculateFrustumPlanes(targetCamera, _frustumPlanes);
            hasFrustumPlanes = true;

            if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, worldBounds))
            {
                ResetVisibleIndirectCommands();
                DrawCombatTracers(targetCamera, worldBounds);
                return;
            }
        }

        BuildVisibleIndirectCommands(hasFrustumPlanes);

        RuntimeRenderResource resource = _runtimeRenderResource;
        bool usesGpuVisibleCompaction = _usesGpuVisibleInstanceCompactionThisFrame;
        if ((!usesGpuVisibleCompaction && _visibleInstanceCount <= 0) || !_hasVisibleBounds || resource.mesh == null || resource.runtimeMaterials == null)
        {
            DrawCombatTracers(targetCamera, worldBounds);
            return;
        }

        if (!usesGpuVisibleCompaction)
            _visibleInstanceIndexBuffer.SetData(_visibleInstanceIndexCache, 0, 0, _visibleInstanceCount);

        for (int subMeshIndex = 0; subMeshIndex < resource.runtimeMaterials.Length; subMeshIndex++)
        {
            GraphicsBuffer argsBuffer = _indirectArgsBuffers[subMeshIndex];
            if (argsBuffer == null)
                continue;

            if (!usesGpuVisibleCompaction)
            {
                GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(resource.mesh, subMeshIndex, (uint)_visibleInstanceCount);
                _indirectArgsCache[subMeshIndex] = args;
                argsBuffer.SetData(new[] { args });
            }

            RenderParams renderParams = new RenderParams(resource.runtimeMaterials[subMeshIndex])
            {
                camera = targetCamera,
                instanceID = gameObject.GetInstanceID(),
                worldBounds = _visibleWorldBounds,
                shadowCastingMode = _shadowCastingMode,
                receiveShadows = _receiveShadows,
                layer = gameObject.layer
            };

            Graphics.RenderMeshIndirect(renderParams, resource.mesh, argsBuffer, 1, 0);
        }

        DrawCombatTracers(targetCamera, worldBounds);
    }

    private void DrawCombatTracers(Camera targetCamera, Bounds crowdWorldBounds)
    {
        if (!_enableCombatTracer ||
            !_enableGpuInstanceCombat ||
            _combatTracerMaterial == null ||
            _combatTracerMesh == null ||
            _combatTracerArgsBuffer == null ||
            _instanceCount <= 0)
        {
            return;
        }

        UpdateCombatTracerMaterialParameters();

        GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(_combatTracerMesh, 0, (uint)_instanceCount);
        _combatTracerArgsBuffer.SetData(new[] { args });

        float tracerBoundsPadding = Mathf.Max(_combatRange, 0.0f) +
            Mathf.Max(Mathf.Max(_combatTracerWidth, _combatMuzzleFlashSize), _combatImpactFlashSize) * 8.0f;
        Bounds tracerWorldBounds = ExpandBounds(crowdWorldBounds, tracerBoundsPadding);
        RenderParams renderParams = new RenderParams(_combatTracerMaterial)
        {
            camera = targetCamera,
            instanceID = gameObject.GetInstanceID(),
            worldBounds = tracerWorldBounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            layer = gameObject.layer
        };

        Graphics.RenderMeshIndirect(renderParams, _combatTracerMesh, _combatTracerArgsBuffer, 1, 0);
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

    private void BuildVisibleIndirectCommands(bool hasFrustumPlanes)
    {
        ResetVisibleIndirectCommands();
        _usesGpuVisibleInstanceCompactionThisFrame = false;
        _visibleUnassignedInstancesThisFrame = false;
        ConfigureVisibleComputeParameters(hasFrustumPlanes);

        if (CanUseGpuVisibleRuntimeCompaction())
            BuildVisibleRuntimeSquadCommandsGpu(hasFrustumPlanes);
        else
            BuildVisibleRuntimeSquadCommandsCpu(hasFrustumPlanes);
    }

    private bool CanUseGpuVisibleRuntimeCompaction()
    {
        EnsureRuntimeSquadRenderChunks();
        return CanUseGpuVisibleInstanceCompactionCommon() &&
            _buildVisibleRuntimeInstanceListKernel >= 0 &&
            _runtimeSquadRenderChunks != null &&
            _runtimeSquadRenderChunks.Length > 0 &&
            _agentSquadDataBuffer != null &&
            _visibleRuntimeSquadMaskBuffer != null;
    }

    private bool CanUseGpuVisibleChunkCompaction()
    {
        return CanUseGpuVisibleInstanceCompactionCommon() &&
            _buildVisibleChunkInstanceListKernel >= 0 &&
            _renderChunks != null &&
            _renderChunks.Length > 0 &&
            _instanceRenderChunkBuffer != null &&
            _visibleRenderChunkMaskBuffer != null;
    }

    private bool CanUseGpuVisibleInstanceCompactionCommon()
    {
        return _updateCompute != null &&
            _buildVisibleIndirectArgsKernel >= 0 &&
            _visibleInstanceIndexBuffer != null &&
            _visibleInstanceCounterBuffer != null &&
            _aliveInstanceIndexBuffer != null &&
            _aliveInstanceCounterBuffer != null &&
            _aliveInstanceDispatchArgsBuffer != null &&
            _deathStateBuffer != null &&
            HasSimulationReadBuffers() &&
            _indirectArgsBuffers != null &&
            _indirectArgsBuffers.Length > 0;
    }

    private void BuildVisibleRuntimeSquadCommandsCpu(bool hasFrustumPlanes)
    {
        EnsureRuntimeSquadRenderChunks();
        if (_runtimeSquadRenderChunks == null || _runtimeSquadRenderChunks.Length == 0)
        {
            AddChunkToVisibleCommands(BuildAllInstanceIndices(), TransformBounds(transform.localToWorldMatrix, _localCrowdBounds));
            return;
        }

        Matrix4x4 localToWorldMatrix = transform.localToWorldMatrix;
        for (int chunkIndex = 0; chunkIndex < _runtimeSquadRenderChunks.Length; chunkIndex++)
        {
            RuntimeSquadRenderChunk chunk = _runtimeSquadRenderChunks[chunkIndex];
            if (chunk.instanceIndices == null || chunk.instanceIndices.Length == 0)
                continue;

            if (!ShouldRenderRuntimeSquadChunk(chunk.squadIndex))
                continue;

            Bounds localBounds = chunk.usesRuntimeBounds
                ? BuildRuntimeSquadChunkLocalBounds(chunk)
                : chunk.staticLocalBounds;
            Bounds worldChunkBounds = TransformBounds(localToWorldMatrix, localBounds);
            if (hasFrustumPlanes && !GeometryUtility.TestPlanesAABB(_frustumPlanes, worldChunkBounds))
                continue;

            AddChunkToVisibleCommands(chunk.instanceIndices, worldChunkBounds);
        }
    }

    private void BuildVisibleChunkCommandsCpu(bool hasFrustumPlanes)
    {
        Matrix4x4 localToWorldMatrix = transform.localToWorldMatrix;
        for (int chunkIndex = 0; chunkIndex < _renderChunks.Length; chunkIndex++)
        {
            RenderChunk chunk = _renderChunks[chunkIndex];
            Bounds worldChunkBounds = TransformBounds(localToWorldMatrix, chunk.localBounds);
            if (hasFrustumPlanes && !GeometryUtility.TestPlanesAABB(_frustumPlanes, worldChunkBounds))
                continue;

            AddChunkToVisibleCommands(chunk.instanceIndices, worldChunkBounds);
        }
    }

    private void BuildVisibleRuntimeSquadCommandsGpu(bool hasFrustumPlanes)
    {
        EnsureRuntimeSquadRenderChunks();
        if (_runtimeSquadRenderChunks == null || _runtimeSquadRenderChunks.Length == 0)
        {
            BuildVisibleRuntimeSquadCommandsCpu(hasFrustumPlanes);
            return;
        }

        ClearVisibleRuntimeSquadMaskCache();

        Matrix4x4 localToWorldMatrix = transform.localToWorldMatrix;
        for (int chunkIndex = 0; chunkIndex < _runtimeSquadRenderChunks.Length; chunkIndex++)
        {
            RuntimeSquadRenderChunk chunk = _runtimeSquadRenderChunks[chunkIndex];
            if (chunk.instanceIndices == null || chunk.instanceIndices.Length == 0)
                continue;

            if (!ShouldRenderRuntimeSquadChunk(chunk.squadIndex))
                continue;

            Bounds localBounds = chunk.usesRuntimeBounds
                ? BuildRuntimeSquadChunkLocalBounds(chunk)
                : chunk.staticLocalBounds;
            Bounds worldChunkBounds = TransformBounds(localToWorldMatrix, localBounds);
            if (hasFrustumPlanes && !GeometryUtility.TestPlanesAABB(_frustumPlanes, worldChunkBounds))
                continue;

            AppendVisibleWorldBounds(worldChunkBounds);
            if (chunk.squadIndex >= 0)
            {
                if (chunk.squadIndex < _visibleRuntimeSquadMaskUploadCache.Length)
                    _visibleRuntimeSquadMaskUploadCache[chunk.squadIndex] = 1u;
            }
            else
            {
                _visibleUnassignedInstancesThisFrame = true;
            }
        }

        if (!_hasVisibleBounds)
            return;

        BuildVisibleRuntimeInstanceListGpu();
        _usesGpuVisibleInstanceCompactionThisFrame = true;
    }

    private void BuildVisibleChunkCommandsGpu(bool hasFrustumPlanes)
    {
        if (_renderChunks == null || _renderChunks.Length == 0)
        {
            BuildVisibleChunkCommandsCpu(hasFrustumPlanes);
            return;
        }

        ClearVisibleRenderChunkMaskCache();

        Matrix4x4 localToWorldMatrix = transform.localToWorldMatrix;
        for (int chunkIndex = 0; chunkIndex < _renderChunks.Length; chunkIndex++)
        {
            RenderChunk chunk = _renderChunks[chunkIndex];
            Bounds worldChunkBounds = TransformBounds(localToWorldMatrix, chunk.localBounds);
            if (hasFrustumPlanes && !GeometryUtility.TestPlanesAABB(_frustumPlanes, worldChunkBounds))
                continue;

            AppendVisibleWorldBounds(worldChunkBounds);
            if (chunkIndex < _visibleRenderChunkMaskUploadCache.Length)
                _visibleRenderChunkMaskUploadCache[chunkIndex] = 1u;
        }

        if (!_hasVisibleBounds)
            return;

        BuildVisibleChunkInstanceListGpu();
        _usesGpuVisibleInstanceCompactionThisFrame = true;
    }

    private bool ShouldRenderRuntimeSquadChunk(int squadIndex)
    {
        if (squadIndex < 0)
            return true;

        if (!TryGetRuntimeSquadAliveCount(squadIndex, out int aliveCount))
            return true;

        return aliveCount > 0;
    }

    private void EnsureRuntimeSquadRenderChunks()
    {
        if (!_runtimeSquadVisibilityDirty)
            return;

        if (!HasRuntimeSquadAnchorData())
        {
            if (_instanceCount > 0)
            {
                _runtimeSquadRenderChunks = new[]
                {
                    new RuntimeSquadRenderChunk
                    {
                        instanceIndices = BuildAllInstanceIndices(),
                        staticLocalBounds = _localCrowdBounds,
                        minSlotOffset = Vector2.zero,
                        maxSlotOffset = Vector2.zero,
                        squadIndex = -1,
                        usesRuntimeBounds = false,
                        hasSlotExtents = false
                    }
                };
            }
            else
            {
                _runtimeSquadRenderChunks = Array.Empty<RuntimeSquadRenderChunk>();
            }

            _runtimeSquadVisibilityDirty = false;
            return;
        }

        int squadCount = Mathf.Max(0, _activeSquadStateCount);
        List<uint>[] squadInstanceLists = new List<uint>[squadCount];
        Vector2[] minSlotOffsets = new Vector2[squadCount];
        Vector2[] maxSlotOffsets = new Vector2[squadCount];
        bool[] hasSlotExtents = new bool[squadCount];
        List<uint> unassignedIndices = null;

        int assignmentCount = Mathf.Min(_activeAgentSquadDataCount, _runtimeAgentSquadData.Length);
        for (int instanceIndex = 0; instanceIndex < assignmentCount; instanceIndex++)
        {
            CrowdVatAgentSquadAssignment assignment = _runtimeAgentSquadData[instanceIndex];
            bool isUnassigned = (assignment.flags & CrowdVatSquadMemberFlags.Unassigned) != 0 ||
                assignment.squadId >= squadCount;
            if (isUnassigned)
            {
                unassignedIndices ??= new List<uint>();
                unassignedIndices.Add((uint)instanceIndex);
                continue;
            }

            int squadIndex = (int)assignment.squadId;
            squadInstanceLists[squadIndex] ??= new List<uint>();
            squadInstanceLists[squadIndex].Add((uint)instanceIndex);

            if (!TryGetRuntimeSlotOffset(assignment, out Vector2 slotOffset))
                slotOffset = Vector2.zero;

            if (!hasSlotExtents[squadIndex])
            {
                minSlotOffsets[squadIndex] = slotOffset;
                maxSlotOffsets[squadIndex] = slotOffset;
                hasSlotExtents[squadIndex] = true;
            }
            else
            {
                minSlotOffsets[squadIndex] = Vector2.Min(minSlotOffsets[squadIndex], slotOffset);
                maxSlotOffsets[squadIndex] = Vector2.Max(maxSlotOffsets[squadIndex], slotOffset);
            }
        }

        for (int instanceIndex = assignmentCount; instanceIndex < _instanceCount; instanceIndex++)
        {
            unassignedIndices ??= new List<uint>();
            unassignedIndices.Add((uint)instanceIndex);
        }

        int chunkCount = 0;
        for (int squadIndex = 0; squadIndex < squadCount; squadIndex++)
        {
            if (squadInstanceLists[squadIndex] != null && squadInstanceLists[squadIndex].Count > 0)
                chunkCount++;
        }

        if (unassignedIndices != null && unassignedIndices.Count > 0)
            chunkCount++;

        if (chunkCount <= 0)
        {
            _runtimeSquadRenderChunks = Array.Empty<RuntimeSquadRenderChunk>();
            _runtimeSquadVisibilityDirty = false;
            return;
        }

        _runtimeSquadRenderChunks = new RuntimeSquadRenderChunk[chunkCount];
        int writeIndex = 0;
        for (int squadIndex = 0; squadIndex < squadCount; squadIndex++)
        {
            List<uint> instanceIndices = squadInstanceLists[squadIndex];
            if (instanceIndices == null || instanceIndices.Count <= 0)
                continue;

            _runtimeSquadRenderChunks[writeIndex++] = new RuntimeSquadRenderChunk
            {
                instanceIndices = instanceIndices.ToArray(),
                staticLocalBounds = default,
                minSlotOffset = hasSlotExtents[squadIndex] ? minSlotOffsets[squadIndex] : Vector2.zero,
                maxSlotOffset = hasSlotExtents[squadIndex] ? maxSlotOffsets[squadIndex] : Vector2.zero,
                squadIndex = squadIndex,
                usesRuntimeBounds = true,
                hasSlotExtents = hasSlotExtents[squadIndex]
            };
        }

        if (unassignedIndices != null && unassignedIndices.Count > 0)
        {
            _runtimeSquadRenderChunks[writeIndex] = new RuntimeSquadRenderChunk
            {
                instanceIndices = unassignedIndices.ToArray(),
                staticLocalBounds = _localCrowdBounds,
                minSlotOffset = Vector2.zero,
                maxSlotOffset = Vector2.zero,
                squadIndex = -1,
                usesRuntimeBounds = false,
                hasSlotExtents = false
            };
        }

        _runtimeSquadVisibilityDirty = false;
    }

    private bool TryGetRuntimeSlotOffset(CrowdVatAgentSquadAssignment assignment, out Vector2 slotOffset)
    {
        slotOffset = Vector2.zero;
        int slotIndex = (int)assignment.slotIndex;
        if (slotIndex < 0 || slotIndex >= _activeFormationSlotCount || _runtimeFormationSlots == null || slotIndex >= _runtimeFormationSlots.Length)
            return false;

        slotOffset = _runtimeFormationSlots[slotIndex].localOffset;
        if ((assignment.flags & CrowdVatSquadMemberFlags.UseSlotOffsetOverride) != 0)
            slotOffset += assignment.slotOffsetOverride;

        return true;
    }

    private Bounds BuildRuntimeSquadChunkLocalBounds(RuntimeSquadRenderChunk chunk)
    {
        Bounds fallbackBounds = chunk.staticLocalBounds.size.sqrMagnitude > 0.0f ? chunk.staticLocalBounds : _localCrowdBounds;
        if (chunk.squadIndex < 0 || chunk.squadIndex >= _activeSquadStateCount || chunk.squadIndex >= _runtimeSquadStates.Length)
            return fallbackBounds;

        CrowdVatSquadState squadState = _runtimeSquadStates[chunk.squadIndex];
        Vector3 localCenter = transform.InverseTransformPoint(squadState.worldCenter);
        Vector3 localTarget = transform.InverseTransformPoint(squadState.worldTarget);
        Vector3 localForward = transform.InverseTransformDirection(squadState.worldForward);
        localForward.y = 0.0f;
        if (localForward.sqrMagnitude <= 1e-6f)
        {
            localForward = localTarget - localCenter;
            localForward.y = 0.0f;
        }

        if (localForward.sqrMagnitude <= 1e-6f)
            localForward = Vector3.forward;
        else
            localForward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, localForward).normalized;
        if (right.sqrMagnitude <= 1e-6f)
            right = Vector3.right;

        Bounds agentLocalBounds = ResolveReferenceAgentLocalBounds();
        Vector3 agentCenterOffset = agentLocalBounds.center;
        Vector3 agentExtents = agentLocalBounds.extents;
        Vector3 firstPoint = localCenter + agentCenterOffset;
        Bounds bounds = new Bounds(firstPoint, Vector3.zero);

        if (chunk.hasSlotExtents)
        {
            Vector2 spacing = new Vector2(
                Mathf.Max(1.0f, squadState.formationSpacing.x),
                Mathf.Max(1.0f, squadState.formationSpacing.y));
            Vector2[] formationCorners =
            {
                new Vector2(chunk.minSlotOffset.x * spacing.x, chunk.minSlotOffset.y * spacing.y),
                new Vector2(chunk.minSlotOffset.x * spacing.x, chunk.maxSlotOffset.y * spacing.y),
                new Vector2(chunk.maxSlotOffset.x * spacing.x, chunk.minSlotOffset.y * spacing.y),
                new Vector2(chunk.maxSlotOffset.x * spacing.x, chunk.maxSlotOffset.y * spacing.y)
            };

            for (int cornerIndex = 0; cornerIndex < formationCorners.Length; cornerIndex++)
            {
                Vector2 formationCorner = formationCorners[cornerIndex];
                Vector3 cornerCenter = localCenter
                    + right * formationCorner.x
                    + localForward * formationCorner.y
                    + agentCenterOffset;
                bounds.Encapsulate(cornerCenter - agentExtents);
                bounds.Encapsulate(cornerCenter + agentExtents);
            }
        }
        else
        {
            bounds.Encapsulate(firstPoint - agentExtents);
            bounds.Encapsulate(firstPoint + agentExtents);
        }

        return ExpandRenderBounds(bounds);
    }

    private void ResetVisibleIndirectCommands()
    {
        _visibleInstanceCount = 0;
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

    private void ClearVisibleRenderChunkMaskCache()
    {
        if (_visibleRenderChunkMaskUploadCache == null || _visibleRenderChunkMaskUploadCache.Length == 0)
            return;

        Array.Clear(_visibleRenderChunkMaskUploadCache, 0, _visibleRenderChunkMaskUploadCache.Length);
    }

    private void BuildVisibleRuntimeInstanceListGpu()
    {
        if (_visibleInstanceCounterBuffer == null || _visibleRuntimeSquadMaskBuffer == null || _visibleInstanceIndexBuffer == null)
            return;

        _visibleInstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        UploadVisibleRuntimeSquadMaskCache();

        BindBuildVisibleRuntimeInstanceListKernel();
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

    private void BuildVisibleChunkInstanceListGpu()
    {
        if (_visibleInstanceCounterBuffer == null || _visibleRenderChunkMaskBuffer == null || _visibleInstanceIndexBuffer == null)
            return;

        _visibleInstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        int uploadCount = Mathf.Min(_visibleRenderChunkMaskUploadCache.Length, _visibleRenderChunkMaskBuffer.count);
        if (uploadCount > 0)
            _visibleRenderChunkMaskBuffer.SetData(_visibleRenderChunkMaskUploadCache, 0, 0, uploadCount);

        BindBuildVisibleChunkInstanceListKernel();
        DispatchAliveInstancesIndirect(_buildVisibleChunkInstanceListKernel);
        BuildVisibleIndirectArgsGpu();
    }

    private void BuildVisibleIndirectArgsGpu()
    {
        RuntimeRenderResource resource = _runtimeRenderResource;
        if (resource.mesh == null || _indirectArgsBuffers == null)
            return;

        int subMeshCount = Mathf.Min(resource.mesh.subMeshCount, _indirectArgsBuffers.Length);
        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            GraphicsBuffer argsBuffer = _indirectArgsBuffers[subMeshIndex];
            if (argsBuffer == null)
                continue;
            BindBuildVisibleIndirectArgsKernel(argsBuffer, resource.mesh, subMeshIndex);
            _updateCompute.Dispatch(_buildVisibleIndirectArgsKernel, 1, 1, 1);
        }
    }

    private void ConfigureVisibleComputeParameters(bool hasFrustumPlanes)
    {
        if (_updateCompute == null)
            return;

        Bounds referenceAgentLocalBounds = ResolveReferenceAgentLocalBounds();
        Vector3 maxCornerFromOrigin = new Vector3(
            Mathf.Abs(referenceAgentLocalBounds.center.x) + referenceAgentLocalBounds.extents.x,
            Mathf.Abs(referenceAgentLocalBounds.center.y) + referenceAgentLocalBounds.extents.y,
            Mathf.Abs(referenceAgentLocalBounds.center.z) + referenceAgentLocalBounds.extents.z);

        _updateCompute.SetFloat(VisibleInstanceBoundsRadiusId, Mathf.Max(0.01f, maxCornerFromOrigin.magnitude));
        _updateCompute.SetInt(HasVisibleFrustumPlanesId, hasFrustumPlanes ? 1 : 0);
        _updateCompute.SetVector(VisibleFrustumPlane0Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[0]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane1Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[1]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane2Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[2]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane3Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[3]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane4Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[4]) : Vector4.zero);
        _updateCompute.SetVector(VisibleFrustumPlane5Id, hasFrustumPlanes ? EncodePlane(_frustumPlanes[5]) : Vector4.zero);
    }

    private static Vector4 EncodePlane(Plane plane)
    {
        return new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
    }

    private void AddChunkToVisibleCommands(uint[] instanceIndices, Bounds worldChunkBounds)
    {
        if (instanceIndices == null || instanceIndices.Length == 0)
            return;

        int writeOffset = _visibleInstanceCount;
        if (writeOffset + instanceIndices.Length > _visibleInstanceIndexCache.Length)
            return;

        AppendVisibleWorldBounds(worldChunkBounds);

        Array.Copy(instanceIndices, 0, _visibleInstanceIndexCache, writeOffset, instanceIndices.Length);
        _visibleInstanceCount = writeOffset + instanceIndices.Length;
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

}
