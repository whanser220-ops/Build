using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed partial class CrowdVatIndirectRenderer : MonoBehaviour
{
    private bool InitializeIfNeeded()
    {
        RefreshRuntimeTargets();

        bool needsRebuild = _resourcesDirty || !AreBuffersReady();
        if (needsRebuild)
            ReleaseResources();

        if (!ResolveRuntimeRenderResource(out CrowdVatAnimationAsset primaryAnimationAsset))
            return false;

        if (_updateCompute == null)
            return false;

        // Unity editor hot-reload can invalidate cached compute kernel handles
        // while keeping GPU buffers alive, so refresh them on each init path.
        ResolveKernelHandles();
        if (!AreKernelHandlesResolved())
            return false;

        Shader shader = ResolveIndirectShader();
        if (shader == null)
            return false;

        if (!TryResolveActiveClip(primaryAnimationAsset))
            return false;

        if (needsRebuild)
        {
            BuildRuntimeMaterials(shader);
            BuildInstanceBuffers();
            BuildIndirectArgsBuffers();
            BuildCombatTracerResources();
            BindStaticResources();
            _resourcesDirty = false;
        }

        return _hasRuntimeRenderResource &&
            _hasClip &&
            _visibleInstanceIndexBuffer != null &&
            _indirectArgsBuffers.Length > 0 &&
            AreKernelHandlesResolved();
    }

    private Shader ResolveIndirectShader()
    {
        if (_indirectShader != null)
            return _indirectShader;

        _indirectShader = Shader.Find(DefaultShaderName);
        return _indirectShader;
    }

    private bool ResolveRuntimeRenderResource(out CrowdVatAnimationAsset primaryAnimationAsset)
    {
        CrowdVatAnimationAsset animationAsset = ResolveConfiguredAnimationAsset();
        CrowdVatPlayer templatePrefab = ResolveConfiguredTemplatePrefab();
        if (animationAsset == null || animationAsset.Mesh == null)
        {
            primaryAnimationAsset = null;
            _runtimeRenderResource = default;
            _hasRuntimeRenderResource = false;
            return false;
        }

        RuntimeRenderResource previousResource = _runtimeRenderResource;
        Material[] preservedRuntimeMaterials =
            previousResource.animationAsset == animationAsset &&
            previousResource.mesh == animationAsset.Mesh &&
            previousResource.templatePrefab == templatePrefab
                ? previousResource.runtimeMaterials
                : null;

        ResolveCrowdRootRows(templatePrefab, out Vector4 row0, out Vector4 row1, out Vector4 row2, out Matrix4x4 rootLocalMatrix);
        _runtimeRenderResource = new RuntimeRenderResource
        {
            animationAsset = animationAsset,
            templatePrefab = templatePrefab,
            mesh = animationAsset.Mesh,
            runtimeMaterials = preservedRuntimeMaterials,
            crowdRootRow0 = row0,
            crowdRootRow1 = row1,
            crowdRootRow2 = row2,
            agentLocalBounds = TransformBounds(rootLocalMatrix, animationAsset.MeshBounds)
        };
        _hasRuntimeRenderResource = true;
        primaryAnimationAsset = animationAsset;
        return true;
    }

    private CrowdVatPlayer ResolveConfiguredTemplatePrefab()
    {
        return _templatePrefab;
    }

    private CrowdVatAnimationAsset ResolveConfiguredAnimationAsset()
    {
        if (_templatePrefab != null && _templatePrefab.AnimationAsset != null)
            return _templatePrefab.AnimationAsset;

        return _animationAsset;
    }

    private void ResolveCrowdRootRows(
        CrowdVatPlayer templatePrefab,
        out Vector4 row0,
        out Vector4 row1,
        out Vector4 row2,
        out Matrix4x4 rootLocalMatrix)
    {
        Vector3 localPosition = _meshRootLocalPosition;
        Vector3 localEulerAngles = _meshRootLocalEulerAngles;
        Vector3 localScale = _meshRootLocalScale;

        if (templatePrefab != null && templatePrefab.RenderRoot != null)
        {
            Transform renderRoot = templatePrefab.RenderRoot;
            localPosition = renderRoot.localPosition;
            localEulerAngles = renderRoot.localEulerAngles;
            localScale = renderRoot.localScale;
        }

        rootLocalMatrix = Matrix4x4.TRS(localPosition, Quaternion.Euler(localEulerAngles), localScale);
        row0 = new Vector4(rootLocalMatrix.m00, rootLocalMatrix.m10, rootLocalMatrix.m20, rootLocalMatrix.m03);
        row1 = new Vector4(rootLocalMatrix.m01, rootLocalMatrix.m11, rootLocalMatrix.m21, rootLocalMatrix.m13);
        row2 = new Vector4(rootLocalMatrix.m02, rootLocalMatrix.m12, rootLocalMatrix.m22, rootLocalMatrix.m23);
    }

    private void ResolveKernelHandles()
    {
        _buildAliveInstanceListKernel = _updateCompute.FindKernel(BuildAliveInstanceListKernelName);
        _buildAliveDispatchArgsKernel = _updateCompute.FindKernel(BuildAliveDispatchArgsKernelName);
        _buildActiveInstanceListKernel = _updateCompute.FindKernel(BuildActiveInstanceListKernelName);
        _buildActiveDispatchArgsKernel = _updateCompute.FindKernel(BuildActiveDispatchArgsKernelName);
        _buildVisibleRuntimeInstanceListKernel = _updateCompute.FindKernel(BuildVisibleRuntimeInstanceListKernelName);
        _buildVisibleChunkInstanceListKernel = _updateCompute.FindKernel(BuildVisibleChunkInstanceListKernelName);
        _buildVisibleIndirectArgsKernel = _updateCompute.FindKernel(BuildVisibleIndirectArgsKernelName);
        _predictKernel = _updateCompute.FindKernel(PredictKernelName);
        _clearGridKernel = _updateCompute.FindKernel(ClearGridKernelName);
        _buildGridKernel = _updateCompute.FindKernel(BuildGridKernelName);
        _buildSpatialElementsKernel = _updateCompute.FindKernel(BuildSpatialElementsKernelName);
        _solveCrowdKernel = _updateCompute.FindKernel(SolveCrowdKernelName);
        _resolveTargetAcquisitionKernel = _updateCompute.FindKernel(ResolveTargetAcquisitionKernelName);
        _resolveInstanceCombatKernel = _updateCompute.FindKernel(ResolveInstanceCombatKernelName);
        _clearSpatialQueriesKernel = _updateCompute.FindKernel(ClearSpatialQueriesKernelName);
        _resolveSpatialQueriesKernel = _updateCompute.FindKernel(ResolveSpatialQueriesKernelName);
        _finalizeKernel = _updateCompute.FindKernel(FinalizeKernelName);
        _captureAiDebugGpuStageKernel = _updateCompute.FindKernel(CaptureAiDebugGpuStageKernelName);
        _discoverAiDebugGpuCandidatesKernel = _updateCompute.FindKernel(DiscoverAiDebugGpuCandidatesKernelName);
    }

    private bool AreKernelHandlesResolved()
    {
        return _buildAliveInstanceListKernel >= 0 &&
            _buildAliveDispatchArgsKernel >= 0 &&
            _buildActiveInstanceListKernel >= 0 &&
            _buildActiveDispatchArgsKernel >= 0 &&
            _buildVisibleRuntimeInstanceListKernel >= 0 &&
            _buildVisibleChunkInstanceListKernel >= 0 &&
            _buildVisibleIndirectArgsKernel >= 0 &&
            _predictKernel >= 0 &&
            _clearGridKernel >= 0 &&
            _buildGridKernel >= 0 &&
            _buildSpatialElementsKernel >= 0 &&
            _solveCrowdKernel >= 0 &&
            _resolveTargetAcquisitionKernel >= 0 &&
            _resolveInstanceCombatKernel >= 0 &&
            _clearSpatialQueriesKernel >= 0 &&
            _resolveSpatialQueriesKernel >= 0 &&
            _finalizeKernel >= 0 &&
            _captureAiDebugGpuStageKernel >= 0 &&
            _discoverAiDebugGpuCandidatesKernel >= 0;
    }

    private bool AreBuffersReady()
    {
        return _spawnDataBuffer != null &&
            _simulationPositionYawBufferA != null &&
            _simulationPositionYawBufferB != null &&
            _simulationPositionYawReadBuffer != null &&
            _simulationPositionYawWriteBuffer != null &&
            _simulationScaleBufferA != null &&
            _simulationScaleBufferB != null &&
            _simulationScaleReadBuffer != null &&
            _simulationScaleWriteBuffer != null &&
            _simulationVelocityBufferA != null &&
            _simulationVelocityBufferB != null &&
            _simulationVelocityReadBuffer != null &&
            _simulationVelocityWriteBuffer != null &&
            _activeStateBuffer != null &&
            _deathStateBuffer != null &&
            _aliveInstanceIndexBuffer != null &&
            _aliveInstanceCounterBuffer != null &&
            _aliveInstanceDispatchArgsBuffer != null &&
            _activeInstanceIndexBuffer != null &&
            _activeInstanceCounterBuffer != null &&
            _activeInstanceDispatchArgsBuffer != null &&
            _gridCounterBuffer != null &&
            _gridOccupantBuffer != null &&
            _spatialCapsuleStartRadiusBuffer != null &&
            _spatialCapsuleEndHeightBuffer != null &&
            _spatialOwnerIndexBuffer != null &&
            _spatialTargetMaskBuffer != null &&
            _spatialFlagsBuffer != null &&
            _spatialFactionBuffer != null &&
            _spatialQueryBuffer != null &&
            _spatialQueryResultBuffer != null &&
            _spatialQueryHitBuffer != null &&
            _interactionSphereBuffer != null &&
            _combatStateBuffer != null &&
            _targetAcquisitionStateBuffer != null &&
            _squadStateBuffer != null &&
            _squadAliveCountBuffer != null &&
            _agentSquadDataBuffer != null &&
            _formationSlotBuffer != null &&
            _agentCoreBuffer != null &&
            _agentPhysicsExtBuffer != null &&
            _animationClipMetadataBuffer != null &&
            _instanceAnimationStateBuffer != null &&
            _instanceTransformBuffer != null &&
            _instanceFrameDataBuffer != null &&
            _instanceFrameBlendDataBuffer != null &&
            _visibleInstanceIndexBuffer != null &&
            _visibleInstanceCounterBuffer != null &&
            _visibleRuntimeSquadMaskBuffer != null &&
            _indirectArgsBuffers.Length > 0;
    }

    private void SyncTemplateBindings()
    {
        if (_templatePrefab == null)
            return;

        if (_templatePrefab.AnimationAsset != null)
            _animationAsset = _templatePrefab.AnimationAsset;

        Transform renderRoot = _templatePrefab.RenderRoot;
        if (renderRoot == null)
            return;

        _meshRootLocalPosition = renderRoot.localPosition;
        _meshRootLocalEulerAngles = renderRoot.localEulerAngles;
        _meshRootLocalScale = renderRoot.localScale;
    }

    private void RefreshRuntimeTargets()
    {
        if (_activeBubbleTarget != null && _activeBubbleTarget.gameObject.activeInHierarchy)
            _resolvedActiveBubbleTarget = _activeBubbleTarget;
        else
            _resolvedActiveBubbleTarget = null;

        if (_characterController != null && !_characterController.gameObject.activeInHierarchy)
            _characterController = null;

        if (_autoResolveCharacterController && _characterController == null)
            _characterController = FindPreferredCharacterController();

        RefreshTerrainReference();
    }

    private CharacterController FindPreferredCharacterController()
    {
        CharacterController[] controllers = FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        CharacterController fallback = null;

        for (int index = 0; index < controllers.Length; index++)
        {
            CharacterController controller = controllers[index];
            if (controller == null)
                continue;

            if (controller.CompareTag("Player"))
                return controller;

            if (!string.IsNullOrEmpty(_preferredCharacterName) &&
                string.Equals(controller.name, _preferredCharacterName, StringComparison.Ordinal))
            {
                return controller;
            }

            if (fallback == null)
                fallback = controller;
        }

        return fallback;
    }

    private void RefreshTerrainReference()
    {
        Terrain previousTerrain = _terrain;
        if (_terrain != null && (_terrain.terrainData == null || !_terrain.gameObject.activeInHierarchy))
            _terrain = null;

        if (_autoResolveTerrain && _terrain == null)
            _terrain = FindPreferredTerrain();

        if (previousTerrain != _terrain)
            MarkResourcesDirty();
    }

    private Terrain FindPreferredTerrain()
    {
        if (Terrain.activeTerrain != null && Terrain.activeTerrain.terrainData != null)
            return Terrain.activeTerrain;

        Terrain terrain = FindFirstObjectByType<Terrain>();
        return terrain != null && terrain.terrainData != null ? terrain : null;
    }

    private bool TryResolveActiveClip(CrowdVatAnimationAsset animationAsset)
    {
        if (animationAsset == null)
        {
            _currentClip = default;
            _currentClipIndex = InvalidClipIndex;
            _hasClip = false;
            return false;
        }

        if (_currentClipIndex != InvalidClipIndex &&
            animationAsset.TryGetClip(_currentClipIndex, out CrowdVatAnimationAsset.ClipInfo existingClip))
        {
            _currentClip = existingClip;
            _hasClip = true;
            return true;
        }

        if (TryResolveClip(_clipName, out int clipIndex, out CrowdVatAnimationAsset.ClipInfo clip))
        {
            _currentClip = clip;
            _currentClipIndex = clipIndex;
            _hasClip = true;
            return true;
        }

        _currentClip = default;
        _currentClipIndex = InvalidClipIndex;
        _hasClip = false;
        return false;
    }

    private bool TryResolveClip(string clipName, out CrowdVatAnimationAsset.ClipInfo clip)
    {
        if (TryResolveClip(clipName, out _, out clip))
            return true;

        clip = default;
        return false;
    }

    private bool TryResolveClip(string clipName, out int clipIndex, out CrowdVatAnimationAsset.ClipInfo clip)
    {
        CrowdVatAnimationAsset clipSource = ResolveConfiguredAnimationAsset();
        if (clipSource == null)
        {
            clipIndex = InvalidClipIndex;
            clip = default;
            return false;
        }

        if (!clipSource.TryGetClipIndex(clipName, out clipIndex))
        {
            clipIndex = InvalidClipIndex;
            clip = default;
            return false;
        }

        if (clipSource.TryGetClip(clipIndex, out clip))
            return true;

        clipIndex = InvalidClipIndex;
        clip = default;
        return false;
    }

    private void BuildRuntimeMaterials(Shader shader)
    {
        if (!_hasRuntimeRenderResource)
            return;

        RuntimeRenderResource resource = _runtimeRenderResource;
        Material[] sourceMaterials = ResolveSourceMaterials(resource.templatePrefab, resource.animationAsset);
        int subMeshCount = Mathf.Max(1, resource.mesh.subMeshCount);
        int materialCount = Mathf.Max(1, Mathf.Min(subMeshCount, sourceMaterials.Length > 0 ? sourceMaterials.Length : subMeshCount));
        resource.runtimeMaterials = new Material[materialCount];

        for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
        {
            Material runtimeMaterial = _indirectMaterialTemplate != null
                ? new Material(_indirectMaterialTemplate)
                : new Material(shader);

            runtimeMaterial.name = $"{resource.animationAsset.name}_Indirect_{materialIndex:00}";
            runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
            runtimeMaterial.enableInstancing = true;

            Material sourceMaterial = sourceMaterials.Length > 0
                ? sourceMaterials[Mathf.Min(materialIndex, sourceMaterials.Length - 1)]
                : null;
            CopySourceMaterialProperties(sourceMaterial, runtimeMaterial);
            resource.runtimeMaterials[materialIndex] = runtimeMaterial;
        }

        _runtimeRenderResource = resource;
    }

    private Material[] ResolveSourceMaterials(CrowdVatPlayer templatePrefab, CrowdVatAnimationAsset animationAsset)
    {
        if (templatePrefab != null && templatePrefab.MeshRenderer != null)
        {
            Material[] sharedMaterials = templatePrefab.MeshRenderer.sharedMaterials;
            if (sharedMaterials != null && sharedMaterials.Length > 0)
                return sharedMaterials;
        }

        return animationAsset.Materials ?? Array.Empty<Material>();
    }

    private void BuildInstanceBuffers()
    {
        InstanceSpawnData[] spawnData = BuildSpawnData();
        InstanceSimulationState[] simulationState = BuildInitialSimulationState(spawnData);
        Vector4[] simulationPositionYaw = BuildInitialSimulationPositionYawData(simulationState);
        float[] simulationScale = BuildInitialSimulationScaleData(simulationState);
        Vector2[] simulationVelocity = BuildInitialSimulationVelocityData(simulationState);
        BuildAnimationStateCaches(spawnData);

        _spawnDataBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<InstanceSpawnData>());
        _spawnDataBuffer.SetData(spawnData);

        _simulationPositionYawBufferA = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());
        _simulationPositionYawBufferB = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());
        _simulationPositionYawBufferA.SetData(simulationPositionYaw);
        _simulationPositionYawBufferB.SetData(simulationPositionYaw);
        _simulationPositionYawReadBuffer = _simulationPositionYawBufferA;
        _simulationPositionYawWriteBuffer = _simulationPositionYawBufferB;

        _simulationScaleBufferA = new ComputeBuffer(_instanceCount, sizeof(float));
        _simulationScaleBufferB = new ComputeBuffer(_instanceCount, sizeof(float));
        _simulationScaleBufferA.SetData(simulationScale);
        _simulationScaleBufferB.SetData(simulationScale);
        _simulationScaleReadBuffer = _simulationScaleBufferA;
        _simulationScaleWriteBuffer = _simulationScaleBufferB;

        _simulationVelocityBufferA = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector2>());
        _simulationVelocityBufferB = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector2>());
        _simulationVelocityBufferA.SetData(simulationVelocity);
        _simulationVelocityBufferB.SetData(simulationVelocity);
        _simulationVelocityReadBuffer = _simulationVelocityBufferA;
        _simulationVelocityWriteBuffer = _simulationVelocityBufferB;
        _activeStateBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _deathStateBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        uint[] activeState = new uint[_instanceCount];
        for (int instanceIndex = 0; instanceIndex < activeState.Length; instanceIndex++)
            activeState[instanceIndex] = 1u;
        _activeStateBuffer.SetData(activeState);
        _deathStateBuffer.SetData(new uint[_instanceCount]);
        _aliveInstanceIndexBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _aliveInstanceCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _aliveInstanceCounterBuffer.SetData(AliveInstanceCounterResetData);
        _aliveInstanceDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _aliveInstanceDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);
        _activeInstanceIndexBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _activeInstanceCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _activeInstanceCounterBuffer.SetData(ActiveInstanceCounterResetData);
        _activeInstanceDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _activeInstanceDispatchArgsBuffer.SetData(ActiveInstanceDispatchArgsResetData);

        _combatStateBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<InstanceCombatStateData>());
        _combatStateReadbackCache = BuildInitialCombatStates(spawnData, simulationState);
        _combatStateBuffer.SetData(_combatStateReadbackCache);
        _targetAcquisitionStateBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<TargetAcquisitionStateData>());
        _targetAcquisitionStateBuffer.SetData(BuildInitialTargetAcquisitionStates(spawnData, simulationState));
        _lastCombatStateReadbackFrame = -1;

        int clipCount = Mathf.Max(1, _animationClipGpuCache.Length);
        _animationClipMetadataBuffer = new ComputeBuffer(clipCount, Marshal.SizeOf<AnimationClipGpuData>());
        _animationClipMetadataBuffer.SetData(_animationClipGpuCache);
        _instanceAnimationStateBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<InstanceAnimationStateGpuData>());
        _instanceAnimationStateBuffer.SetData(_instanceAnimationStateGpuCache);
        _instanceTransformBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<MatrixRows>());
        _instanceFrameDataBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());
        _instanceFrameBlendDataBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());

        BuildSimulationGridLayout(spawnData);
        _gridCounterBuffer = new ComputeBuffer(_gridCellCount, sizeof(uint));
        _gridOccupantBuffer = new ComputeBuffer(_gridCellCount * _maxCellOccupancy, sizeof(uint));
        _spatialCapsuleStartRadiusBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());
        _spatialCapsuleEndHeightBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());
        _spatialOwnerIndexBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _spatialTargetMaskBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _spatialFlagsBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _spatialFactionBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _spatialQueryBuffer = new ComputeBuffer(_maxSpatialQueries, Marshal.SizeOf<SpatialQueryData>());
        _spatialQueryResultBuffer = new ComputeBuffer(_maxSpatialQueries, Marshal.SizeOf<SpatialQueryResultData>());
        _spatialQueryHitBuffer = new ComputeBuffer(_maxSpatialQueries * _maxHitsPerSpatialQuery, Marshal.SizeOf<SpatialQueryHitData>());
        _spatialQueryUploadCache = new SpatialQueryData[_maxSpatialQueries];
        _spatialQueryResultCache = new SpatialQueryResultData[_maxSpatialQueries];
        _spatialQueryHitCache = new SpatialQueryHitData[_maxSpatialQueries * _maxHitsPerSpatialQuery];

        int interactionCapacity = GetInteractionSphereCapacity();
        _interactionSphereBuffer = new ComputeBuffer(interactionCapacity, Marshal.SizeOf<InteractionSphereData>());
        _interactionSphereUploadCache = new InteractionSphereData[interactionCapacity];
        _interactionSphereBuffer.SetData(_interactionSphereUploadCache);
        EnsureRuntimeSquadBuffers();
        EnsureAgentDataLayoutBuffers();
        RebuildAgentDataLayoutCaches();
        UploadAgentDataLayout();

        Bounds referenceAgentLocalBounds = ResolveReferenceAgentLocalBounds();
        _localCrowdBounds = BuildLocalCrowdBounds(spawnData, referenceAgentLocalBounds);
        _renderChunks = Array.Empty<RenderChunk>();
    }

    private void BuildAnimationStateCaches(InstanceSpawnData[] spawnData)
    {
        CrowdVatAnimationAsset animationAsset = _hasRuntimeRenderResource
            ? _runtimeRenderResource.animationAsset
            : ResolveConfiguredAnimationAsset();

        BuildAnimationClipGpuCache(animationAsset);

        int instanceCapacity = Mathf.Max(_instanceCount, 0);
        if (_instanceAnimationStateCpuCache == null || _instanceAnimationStateCpuCache.Length != instanceCapacity)
            _instanceAnimationStateCpuCache = new InstanceAnimationStateCpuData[instanceCapacity];

        if (_instanceAnimationStateGpuCache == null || _instanceAnimationStateGpuCache.Length != instanceCapacity)
            _instanceAnimationStateGpuCache = new InstanceAnimationStateGpuData[instanceCapacity];

        for (int instanceIndex = 0; instanceIndex < instanceCapacity; instanceIndex++)
        {
            _instanceAnimationStateCpuCache[instanceIndex] = BuildInitialInstanceAnimationState(
                spawnData,
                instanceIndex,
                animationAsset);
        }

        SyncInstanceAnimationStateGpuCache();
    }

    private void EnsureAgentDataLayoutBuffers()
    {
        int capacity = Mathf.Max(1, _instanceCount);
        if (_agentCoreBuffer == null || _agentCoreBuffer.count != capacity)
        {
            ReleaseBuffer(ref _agentCoreBuffer);
            _agentCoreBuffer = new ComputeBuffer(capacity, Marshal.SizeOf<CrowdVatAgentCore>());
        }

        if (_agentPhysicsExtBuffer == null || _agentPhysicsExtBuffer.count != capacity)
        {
            ReleaseBuffer(ref _agentPhysicsExtBuffer);
            _agentPhysicsExtBuffer = new ComputeBuffer(capacity, Marshal.SizeOf<CrowdVatAgentPhysicsExt>());
        }

        if (_agentCoreUploadCache == null || _agentCoreUploadCache.Length != capacity)
            _agentCoreUploadCache = new CrowdVatAgentCore[capacity];

        if (_agentPhysicsExtUploadCache == null || _agentPhysicsExtUploadCache.Length != capacity)
            _agentPhysicsExtUploadCache = new CrowdVatAgentPhysicsExt[capacity];
    }

    private void BuildAnimationClipGpuCache(CrowdVatAnimationAsset animationAsset)
    {
        if (animationAsset == null || animationAsset.ClipCount <= 0)
        {
            _animationClipGpuCache = new[]
            {
                new AnimationClipGpuData
                {
                    startFrame = 0,
                    frameCount = 1,
                    lengthSeconds = 0.0f,
                    loop = 0
                }
            };
            return;
        }

        int clipCount = animationAsset.ClipCount;
        if (_animationClipGpuCache == null || _animationClipGpuCache.Length != clipCount)
            _animationClipGpuCache = new AnimationClipGpuData[clipCount];

        for (int clipIndex = 0; clipIndex < clipCount; clipIndex++)
        {
            if (!animationAsset.TryGetClip(clipIndex, out CrowdVatAnimationAsset.ClipInfo clip))
                clip = default;

            _animationClipGpuCache[clipIndex] = new AnimationClipGpuData
            {
                startFrame = clip.StartFrame,
                frameCount = Mathf.Max(1, clip.FrameCount),
                lengthSeconds = Mathf.Max(clip.LengthSeconds, 0.0f),
                loop = ResolveClipLoop(clip) ? 1 : 0
            };
        }
    }

    private InstanceAnimationStateCpuData BuildInitialInstanceAnimationState(
        InstanceSpawnData[] spawnData,
        int instanceIndex,
        CrowdVatAnimationAsset animationAsset)
    {
        float playbackSpeedMultiplier = 1.0f;
        float normalizedStartOffset = 0.0f;
        if (spawnData != null && instanceIndex >= 0 && instanceIndex < spawnData.Length)
        {
            playbackSpeedMultiplier = Mathf.Max(0.0f, spawnData[instanceIndex].playbackSpeedMultiplier);
            normalizedStartOffset = Mathf.Clamp01(spawnData[instanceIndex].normalizedTimeOffset);
        }

        int currentClipIndex = _currentClipIndex;
        CrowdVatAnimationAsset.ClipInfo currentClip = _currentClip;

        if ((animationAsset == null || !animationAsset.TryGetClip(currentClipIndex, out currentClip)) &&
            !TryResolveClip(_clipName, out currentClipIndex, out currentClip))
        {
            currentClipIndex = InvalidClipIndex;
            currentClip = default;
        }

        float clipLength = Mathf.Max(currentClip.LengthSeconds, 0.0f);
        float initialTime = clipLength * normalizedStartOffset;
        if (ResolveClipLoop(currentClip) && clipLength > Mathf.Epsilon)
            initialTime = Mathf.Repeat(initialTime, clipLength);
        else
            initialTime = Mathf.Clamp(initialTime, 0.0f, clipLength);

        return new InstanceAnimationStateCpuData
        {
            currentClipIndex = currentClipIndex,
            nextClipIndex = currentClipIndex,
            currentClipTime = initialTime,
            nextClipTime = initialTime,
            transitionElapsed = 0.0f,
            transitionDuration = 0.0f,
            playbackSpeedMultiplier = playbackSpeedMultiplier,
            isBlending = false
        };
    }

    private void SyncInstanceAnimationStateGpuCache()
    {
        if (_instanceAnimationStateCpuCache == null)
        {
            _instanceAnimationStateGpuCache = Array.Empty<InstanceAnimationStateGpuData>();
            return;
        }

        int count = _instanceAnimationStateCpuCache.Length;
        if (_instanceAnimationStateGpuCache == null || _instanceAnimationStateGpuCache.Length != count)
            _instanceAnimationStateGpuCache = new InstanceAnimationStateGpuData[count];

        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
        {
            InstanceAnimationStateCpuData state = _instanceAnimationStateCpuCache[instanceIndex];
            _instanceAnimationStateGpuCache[instanceIndex] = new InstanceAnimationStateGpuData
            {
                currentClipIndex = state.currentClipIndex,
                nextClipIndex = state.nextClipIndex,
                isBlending = state.isBlending ? 1 : 0,
                padding0 = 0,
                currentClipTime = state.currentClipTime,
                nextClipTime = state.nextClipTime,
                transitionElapsed = state.transitionElapsed,
                transitionDuration = state.transitionDuration
            };
        }
    }

    private bool ResolveClipLoop(CrowdVatAnimationAsset.ClipInfo clip)
    {
        return _loopOverride || clip.Loop;
    }

    private Bounds ResolveReferenceAgentLocalBounds()
    {
        return _hasRuntimeRenderResource
            ? _runtimeRenderResource.agentLocalBounds
            : new Bounds(Vector3.zero, Vector3.one);
    }

    private float ResolveCombatTargetHeight()
    {
        if (_hasRuntimeRenderResource)
        {
            Bounds agentLocalBounds = _runtimeRenderResource.agentLocalBounds;
            if (agentLocalBounds.size.sqrMagnitude > 1e-8f)
                return Mathf.Max(0.0f, agentLocalBounds.max.y);
        }

        return Mathf.Max(0.0f, _combatTargetHeight);
    }

    private int GetInteractionSphereCapacity()
    {
        int manualCount = _interactionSpheres != null ? _interactionSpheres.Length : 0;
        int automaticCount = _useCharacterAsInteractionSphere ? 1 : 0;
        return Mathf.Max(1, manualCount + automaticCount);
    }

    private void BuildSimulationGridLayout(InstanceSpawnData[] spawnData)
    {
        float maxAgentRadius = Mathf.Max(_maximumQueryAgentRadius, _collisionRadius);
        float padding = Mathf.Max(_maxDisplacementFromSpawn, maxAgentRadius) + _queryCellSize + Mathf.Max(0.0f, _simulationBoundsPadding);
        GetSimulationAreaExtentsXZ(spawnData, out Vector2 minXZ, out Vector2 maxXZ);
        Vector2 paddedMin = minXZ - Vector2.one * padding;
        Vector2 paddedMax = maxXZ + Vector2.one * padding;
        Vector2 paddedSize = new Vector2(
            Mathf.Max(0.01f, paddedMax.x - paddedMin.x),
            Mathf.Max(0.01f, paddedMax.y - paddedMin.y));
        _gridDimensions = new Vector2Int(
            Mathf.Max(1, Mathf.CeilToInt(paddedSize.x / _queryCellSize)),
            Mathf.Max(1, Mathf.CeilToInt(paddedSize.y / _queryCellSize)));
        Vector2 actualSize = new Vector2(_gridDimensions.x * _queryCellSize, _gridDimensions.y * _queryCellSize);
        Vector2 paddedCenter = (paddedMin + paddedMax) * 0.5f;
        _gridMinXZ = paddedCenter - actualSize * 0.5f;
        _gridMaxXZ = _gridMinXZ + actualSize;
        _gridCellCount = Mathf.Max(1, _gridDimensions.x * _gridDimensions.y);
    }

    private InstanceSpawnData[] BuildSpawnData()
    {
        InstanceSpawnData[] spawnData = new InstanceSpawnData[_instanceCount];
        _minimumPlaybackSpeedMultiplier = float.MaxValue;
        _maximumQueryAgentRadius = 0.0f;

        float clampedJitter = Mathf.Clamp01(_cellJitter);
        float startOffsetStrength = Mathf.Clamp01(_normalizedStartOffsetRandom);
        System.Random random = new System.Random(_randomSeed);
        SpawnAreaContext spawnArea = ResolveSpawnAreaContext();

        for (int instanceIndex = 0; instanceIndex < _instanceCount; instanceIndex++)
        {
            CrowdVatFaction faction = ResolveInstanceFaction(instanceIndex);
            ResolveFactionPlacement(instanceIndex, faction, out int localIndex, out int localCount);

            // 阵营范围只影响出生点和初始朝向，不限制后续 GPU 运行时移动。
            Vector3 localPosition = _factionLayout == CrowdVatFactionLayoutMode.TwoOpposingFactions
                ? BuildTwoFactionSpawnPosition(localIndex, localCount, faction, clampedJitter, random, spawnArea)
                : BuildSingleCrowdSpawnPosition(instanceIndex, clampedJitter, random, spawnArea);
            localPosition = ApplyTerrainHeightToLocalPosition(localPosition);

            float yawRadians = _factionLayout == CrowdVatFactionLayoutMode.TwoOpposingFactions && _orientTowardEnemyFaction
                ? GetOpposingFactionYawRadians(faction)
                : Mathf.Deg2Rad * (float)(random.NextDouble() * 360.0);

            float scaleMultiplier = RandomRange(random, _scaleMultiplierRange.x, _scaleMultiplierRange.y);
            float playbackSpeedMultiplier = RandomRange(random, _playbackSpeedMultiplierRange.x, _playbackSpeedMultiplierRange.y);
            float normalizedStartOffset = (float)random.NextDouble() * startOffsetStrength;

            spawnData[instanceIndex] = new InstanceSpawnData
            {
                localPosition = localPosition,
                yawRadians = yawRadians,
                uniformScale = _baseScale * scaleMultiplier,
                normalizedTimeOffset = normalizedStartOffset,
                playbackSpeedMultiplier = playbackSpeedMultiplier,
                faction = (uint)faction
            };

            _minimumPlaybackSpeedMultiplier = Mathf.Min(_minimumPlaybackSpeedMultiplier, playbackSpeedMultiplier);
            _maximumQueryAgentRadius = Mathf.Max(
                _maximumQueryAgentRadius,
                Mathf.Max(0.01f, _collisionRadius * spawnData[instanceIndex].uniformScale));
        }

        if (_minimumPlaybackSpeedMultiplier == float.MaxValue)
            _minimumPlaybackSpeedMultiplier = 1.0f;

        if (_maximumQueryAgentRadius <= 0.0f)
            _maximumQueryAgentRadius = Mathf.Max(0.01f, _collisionRadius * _baseScale);

        return spawnData;
    }

    private CrowdVatFaction ResolveInstanceFaction(int instanceIndex)
    {
        if (_factionLayout != CrowdVatFactionLayoutMode.TwoOpposingFactions)
            return CrowdVatFaction.CampA;

        int campACount = Mathf.CeilToInt(_instanceCount * 0.5f);
        return instanceIndex < campACount ? CrowdVatFaction.CampA : CrowdVatFaction.CampB;
    }

    private void ResolveFactionPlacement(int instanceIndex, CrowdVatFaction faction, out int localIndex, out int localCount)
    {
        if (_factionLayout != CrowdVatFactionLayoutMode.TwoOpposingFactions)
        {
            localIndex = instanceIndex;
            localCount = _instanceCount;
            return;
        }

        int campACount = Mathf.CeilToInt(_instanceCount * 0.5f);
        int campBCount = Mathf.Max(0, _instanceCount - campACount);

        if (faction == CrowdVatFaction.CampB)
        {
            localIndex = Mathf.Max(0, instanceIndex - campACount);
            localCount = Mathf.Max(1, campBCount);
            return;
        }

        localIndex = instanceIndex;
        localCount = Mathf.Max(1, campACount);
    }

    private Vector3 BuildSingleCrowdSpawnPosition(int instanceIndex, float clampedJitter, System.Random random, SpawnAreaContext spawnArea)
    {
        ResolveGridPlacement(instanceIndex, _instanceCount, spawnArea.size, clampedJitter, random, out float localX, out float localZ);
        localX += spawnArea.centerXZ.x;
        localZ += spawnArea.centerXZ.y;
        return new Vector3(localX, _heightOffset, localZ);
    }

    private Vector3 BuildTwoFactionSpawnPosition(int localIndex, int localCount, CrowdVatFaction faction, float clampedJitter, System.Random random, SpawnAreaContext spawnArea)
    {
        Vector2 campAreaSize = GetFactionAreaSize(spawnArea.size, faction);
        ResolveGridPlacement(localIndex, localCount, campAreaSize, clampedJitter, random, out float localX, out float localZ);

        Vector2 factionCenterXZ = GetFactionCenterXZ(spawnArea, faction);
        localX += factionCenterXZ.x;
        localZ += factionCenterXZ.y;
        return new Vector3(localX, _heightOffset, localZ);
    }

    private bool TryGetFactionControlCenterXZ(CrowdVatFaction faction, out Vector2 centerXZ)
    {
        centerXZ = Vector2.zero;
        if (!_useFactionControlTransforms || !TryGetFactionControlTransform(faction, out Transform controlTransform))
            return false;

        Vector3 localPosition = transform.InverseTransformPoint(controlTransform.position);
        centerXZ = new Vector2(localPosition.x, localPosition.z);
        return true;
    }

    private bool TryGetFactionControlTransform(CrowdVatFaction faction, out Transform controlTransform)
    {
        controlTransform = faction == CrowdVatFaction.CampA ? _campAControlTransform : _campBControlTransform;
        return controlTransform != null;
    }

    private bool TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction faction, out Vector2 centerXZ, out Vector2 sizeXZ)
    {
        centerXZ = Vector2.zero;
        sizeXZ = Vector2.zero;
        TryGetFactionControlTransform(faction, out Transform controlTransform);
        if (!TryGetFactionCornerTransforms(
                faction,
                out Transform northWestCornerTransform,
                out Transform northEastCornerTransform,
                out Transform southEastCornerTransform,
                out Transform southWestCornerTransform))
        {
            return false;
        }

        if (controlTransform != null)
        {
            Vector3 northWestLocalOffset = controlTransform.InverseTransformPoint(northWestCornerTransform.position);
            Vector3 northEastLocalOffset = controlTransform.InverseTransformPoint(northEastCornerTransform.position);
            Vector3 southEastLocalOffset = controlTransform.InverseTransformPoint(southEastCornerTransform.position);
            Vector3 southWestLocalOffset = controlTransform.InverseTransformPoint(southWestCornerTransform.position);

            float halfWidth = Mathf.Max(
                Mathf.Abs(northWestLocalOffset.x),
                Mathf.Abs(northEastLocalOffset.x),
                Mathf.Abs(southEastLocalOffset.x),
                Mathf.Abs(southWestLocalOffset.x));
            float halfDepth = Mathf.Max(
                Mathf.Abs(northWestLocalOffset.z),
                Mathf.Abs(northEastLocalOffset.z),
                Mathf.Abs(southEastLocalOffset.z),
                Mathf.Abs(southWestLocalOffset.z));

            Vector3 controlLocalPosition = transform.InverseTransformPoint(controlTransform.position);
            centerXZ = new Vector2(controlLocalPosition.x, controlLocalPosition.z);
            sizeXZ = new Vector2(Mathf.Max(0.01f, halfWidth * 2.0f), Mathf.Max(0.01f, halfDepth * 2.0f));
            return true;
        }

        Vector3 northWestLocalPosition = transform.InverseTransformPoint(northWestCornerTransform.position);
        Vector3 northEastLocalPosition = transform.InverseTransformPoint(northEastCornerTransform.position);
        Vector3 southEastLocalPosition = transform.InverseTransformPoint(southEastCornerTransform.position);
        Vector3 southWestLocalPosition = transform.InverseTransformPoint(southWestCornerTransform.position);

        float minX = Mathf.Min(northWestLocalPosition.x, northEastLocalPosition.x, southEastLocalPosition.x, southWestLocalPosition.x);
        float maxX = Mathf.Max(northWestLocalPosition.x, northEastLocalPosition.x, southEastLocalPosition.x, southWestLocalPosition.x);
        float minZ = Mathf.Min(northWestLocalPosition.z, northEastLocalPosition.z, southEastLocalPosition.z, southWestLocalPosition.z);
        float maxZ = Mathf.Max(northWestLocalPosition.z, northEastLocalPosition.z, southEastLocalPosition.z, southWestLocalPosition.z);

        centerXZ = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        sizeXZ = new Vector2(Mathf.Max(0.01f, maxX - minX), Mathf.Max(0.01f, maxZ - minZ));
        return true;
    }

    private bool TryGetFactionCornerTransforms(
        CrowdVatFaction faction,
        out Transform northWestCornerTransform,
        out Transform northEastCornerTransform,
        out Transform southEastCornerTransform,
        out Transform southWestCornerTransform)
    {
        if (faction == CrowdVatFaction.CampA)
        {
            northWestCornerTransform = _campANorthWestCornerTransform;
            northEastCornerTransform = _campANorthEastCornerTransform;
            southEastCornerTransform = _campASouthEastCornerTransform;
            southWestCornerTransform = _campASouthWestCornerTransform;
        }
        else
        {
            northWestCornerTransform = _campBNorthWestCornerTransform;
            northEastCornerTransform = _campBNorthEastCornerTransform;
            southEastCornerTransform = _campBSouthEastCornerTransform;
            southWestCornerTransform = _campBSouthWestCornerTransform;
        }

        return northWestCornerTransform != null &&
               northEastCornerTransform != null &&
               southEastCornerTransform != null &&
               southWestCornerTransform != null;
    }

    private void SyncFactionControlTransform(Transform controlTransform, Vector2 centerXZ)
    {
        if (controlTransform == null)
            return;

        Vector3 localPosition = transform.InverseTransformPoint(controlTransform.position);
        localPosition.x = centerXZ.x;
        localPosition.z = centerXZ.y;
        controlTransform.position = transform.TransformPoint(localPosition);
        controlTransform.hasChanged = false;
    }

    private bool TrySyncExplicitFactionAreaFromMovedCorner(CrowdVatFaction faction, Transform movedCornerTransform)
    {
        if (movedCornerTransform == null || !TryGetFactionControlTransform(faction, out Transform controlTransform))
            return false;

        Vector3 localOffset = controlTransform.InverseTransformPoint(movedCornerTransform.position);
        Vector2 areaSize = new Vector2(
            Mathf.Max(0.01f, Mathf.Abs(localOffset.x) * 2.0f),
            Mathf.Max(0.01f, Mathf.Abs(localOffset.z) * 2.0f));

        if (faction == CrowdVatFaction.CampA)
            _campAAreaSize = areaSize;
        else
            _campBAreaSize = areaSize;

        _useExplicitFactionAreaSizes = true;
        return true;
    }

    private void SyncFactionRangeCornerTransforms(
        Transform controlTransform,
        Vector2 centerXZ,
        Vector2 sizeXZ,
        Transform northWestCornerTransform,
        Transform northEastCornerTransform,
        Transform southEastCornerTransform,
        Transform southWestCornerTransform)
    {
        if (controlTransform != null)
            SyncFactionControlTransform(controlTransform, centerXZ);

        if (northWestCornerTransform == null ||
            northEastCornerTransform == null ||
            southEastCornerTransform == null ||
            southWestCornerTransform == null)
        {
            return;
        }

        Vector2 clampedSize = new Vector2(Mathf.Max(0.01f, sizeXZ.x), Mathf.Max(0.01f, sizeXZ.y));
        Vector2 halfSize = clampedSize * 0.5f;
        SyncFactionRangeCornerTransform(northWestCornerTransform, centerXZ + new Vector2(-halfSize.x, halfSize.y));
        SyncFactionRangeCornerTransform(northEastCornerTransform, centerXZ + new Vector2(halfSize.x, halfSize.y));
        SyncFactionRangeCornerTransform(southEastCornerTransform, centerXZ + new Vector2(halfSize.x, -halfSize.y));
        SyncFactionRangeCornerTransform(southWestCornerTransform, centerXZ + new Vector2(-halfSize.x, -halfSize.y));
    }

    private void SyncFactionRangeCornerTransform(Transform cornerTransform, Vector2 cornerXZ)
    {
        if (cornerTransform == null)
            return;

        Vector3 localPosition = transform.InverseTransformPoint(cornerTransform.position);
        localPosition.x = cornerXZ.x;
        localPosition.z = cornerXZ.y;
        cornerTransform.position = transform.TransformPoint(localPosition);
        cornerTransform.hasChanged = false;
    }

    private Vector2 GetFactionCenterXZ(SpawnAreaContext spawnArea, CrowdVatFaction faction)
    {
        if (TryGetFactionControlCenterXZ(faction, out Vector2 controlCenterXZ))
            return controlCenterXZ;

        if (TryGetFactionRangeBoundsFromCornerTransforms(faction, out Vector2 rangeCenterXZ, out _))
            return rangeCenterXZ;

        if (_useExplicitFactionCenters)
            return faction == CrowdVatFaction.CampA ? _campACenterXZ : _campBCenterXZ;

        float factionOffset = GetFactionAxisOffset(spawnArea.size);
        Vector2 factionCenter = spawnArea.centerXZ;
        if (_factionSplitAxis == CrowdVatFactionSplitAxis.Width)
            factionCenter.x += faction == CrowdVatFaction.CampA ? -factionOffset : factionOffset;
        else
            factionCenter.y += faction == CrowdVatFaction.CampA ? -factionOffset : factionOffset;

        return factionCenter;
    }

    private Vector2 GetFactionAreaSize(Vector2 totalAreaSize, CrowdVatFaction faction)
    {
        if (TryGetFactionRangeBoundsFromCornerTransforms(faction, out _, out Vector2 controlAreaSize))
            return controlAreaSize;

        if (_useExplicitFactionAreaSizes)
            return faction == CrowdVatFaction.CampA ? _campAAreaSize : _campBAreaSize;

        float totalWidth = Mathf.Max(totalAreaSize.x, 0.01f);
        float totalDepth = Mathf.Max(totalAreaSize.y, 0.01f);
        float splitLength = _factionSplitAxis == CrowdVatFactionSplitAxis.Width ? totalWidth : totalDepth;
        float clampedGap = Mathf.Clamp(_factionCenterGap, 0.0f, Mathf.Max(0.0f, splitLength - 0.02f));
        float factionLength = Mathf.Max(0.01f, (splitLength - clampedGap) * 0.5f);

        return _factionSplitAxis == CrowdVatFactionSplitAxis.Width
            ? new Vector2(factionLength, totalDepth)
            : new Vector2(totalWidth, factionLength);
    }

    private float GetFactionAxisOffset(Vector2 totalAreaSize)
    {
        float splitLength = _factionSplitAxis == CrowdVatFactionSplitAxis.Width
            ? Mathf.Max(totalAreaSize.x, 0.01f)
            : Mathf.Max(totalAreaSize.y, 0.01f);
        float clampedGap = Mathf.Clamp(_factionCenterGap, 0.0f, Mathf.Max(0.0f, splitLength - 0.02f));
        float factionLength = Mathf.Max(0.01f, (splitLength - clampedGap) * 0.5f);
        return clampedGap * 0.5f + factionLength * 0.5f;
    }

    private float GetOpposingFactionYawRadians(CrowdVatFaction faction)
    {
        if (_factionSplitAxis == CrowdVatFactionSplitAxis.Width)
            return faction == CrowdVatFaction.CampA ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f;

        return faction == CrowdVatFaction.CampA ? 0.0f : Mathf.PI;
    }

    private static void ResolveGridPlacement(
        int instanceIndex,
        int instanceCount,
        Vector2 areaSize,
        float clampedJitter,
        System.Random random,
        out float localX,
        out float localZ)
    {
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(instanceCount * (areaSize.x / Mathf.Max(areaSize.y, 0.01f)))));
        int rows = Mathf.Max(1, Mathf.CeilToInt(instanceCount / (float)columns));
        float cellWidth = columns > 1 ? areaSize.x / (columns - 1) : 0.0f;
        float cellDepth = rows > 1 ? areaSize.y / (rows - 1) : 0.0f;

        int column = instanceIndex % columns;
        int row = instanceIndex / columns;
        float baseX = columns > 1 ? -areaSize.x * 0.5f + column * cellWidth : 0.0f;
        float baseZ = rows > 1 ? -areaSize.y * 0.5f + row * cellDepth : 0.0f;

        float jitterX = (float)(random.NextDouble() * 2.0 - 1.0) * cellWidth * 0.5f * clampedJitter;
        float jitterZ = (float)(random.NextDouble() * 2.0 - 1.0) * cellDepth * 0.5f * clampedJitter;

        localX = baseX + jitterX;
        localZ = baseZ + jitterZ;
    }

    private SpawnAreaContext ResolveSpawnAreaContext()
    {
        if (_distributeAcrossWholeTerrain && TryGetResolvedTerrain(out Terrain terrain))
            return BuildTerrainSpawnAreaContext(terrain);

        return new SpawnAreaContext
        {
            size = new Vector2(Mathf.Max(_areaSize.x, 0.01f), Mathf.Max(_areaSize.y, 0.01f)),
            centerXZ = Vector2.zero
        };
    }

    private SpawnAreaContext BuildTerrainSpawnAreaContext(Terrain terrain)
    {
        Vector3 terrainMin = terrain.transform.position;
        Vector3 terrainMax = terrainMin + terrain.terrainData.size;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

        Vector3 localCornerA = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMin.x, transform.position.y, terrainMin.z));
        Vector3 localCornerB = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMin.x, transform.position.y, terrainMax.z));
        Vector3 localCornerC = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMax.x, transform.position.y, terrainMin.z));
        Vector3 localCornerD = worldToLocal.MultiplyPoint3x4(new Vector3(terrainMax.x, transform.position.y, terrainMax.z));

        float minX = Mathf.Min(localCornerA.x, localCornerB.x, localCornerC.x, localCornerD.x);
        float maxX = Mathf.Max(localCornerA.x, localCornerB.x, localCornerC.x, localCornerD.x);
        float minZ = Mathf.Min(localCornerA.z, localCornerB.z, localCornerC.z, localCornerD.z);
        float maxZ = Mathf.Max(localCornerA.z, localCornerB.z, localCornerC.z, localCornerD.z);

        return new SpawnAreaContext
        {
            size = new Vector2(Mathf.Max(0.01f, maxX - minX), Mathf.Max(0.01f, maxZ - minZ)),
            centerXZ = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f)
        };
    }

    private static void GetSpawnExtentsXZ(InstanceSpawnData[] spawnData, out Vector2 minXZ, out Vector2 maxXZ)
    {
        if (spawnData == null || spawnData.Length == 0)
        {
            minXZ = Vector2.zero;
            maxXZ = Vector2.zero;
            return;
        }

        minXZ = new Vector2(float.MaxValue, float.MaxValue);
        maxXZ = new Vector2(float.MinValue, float.MinValue);
        for (int index = 0; index < spawnData.Length; index++)
        {
            Vector3 localPosition = spawnData[index].localPosition;
            minXZ.x = Mathf.Min(minXZ.x, localPosition.x);
            minXZ.y = Mathf.Min(minXZ.y, localPosition.z);
            maxXZ.x = Mathf.Max(maxXZ.x, localPosition.x);
            maxXZ.y = Mathf.Max(maxXZ.y, localPosition.z);
        }
    }

    private void GetSimulationAreaExtentsXZ(InstanceSpawnData[] spawnData, out Vector2 minXZ, out Vector2 maxXZ)
    {
        bool hasBounds = false;
        minXZ = Vector2.zero;
        maxXZ = Vector2.zero;

        if (spawnData != null && spawnData.Length > 0)
        {
            GetSpawnExtentsXZ(spawnData, out minXZ, out maxXZ);
            hasBounds = true;
        }

        SpawnAreaContext spawnArea = ResolveSpawnAreaContext();
        EncapsulateArea(spawnArea.centerXZ, spawnArea.size, ref hasBounds, ref minXZ, ref maxXZ);

        if (_factionLayout == CrowdVatFactionLayoutMode.TwoOpposingFactions)
        {
            EncapsulateArea(GetFactionCenterXZ(spawnArea, CrowdVatFaction.CampA), GetFactionAreaSize(spawnArea.size, CrowdVatFaction.CampA), ref hasBounds, ref minXZ, ref maxXZ);
            EncapsulateArea(GetFactionCenterXZ(spawnArea, CrowdVatFaction.CampB), GetFactionAreaSize(spawnArea.size, CrowdVatFaction.CampB), ref hasBounds, ref minXZ, ref maxXZ);
        }
    }

    private static void EncapsulateArea(Vector2 centerXZ, Vector2 sizeXZ, ref bool hasBounds, ref Vector2 minXZ, ref Vector2 maxXZ)
    {
        Vector2 safeSize = new Vector2(Mathf.Max(0.01f, sizeXZ.x), Mathf.Max(0.01f, sizeXZ.y));
        Vector2 halfSize = safeSize * 0.5f;
        Vector2 areaMin = centerXZ - halfSize;
        Vector2 areaMax = centerXZ + halfSize;

        if (!hasBounds)
        {
            minXZ = areaMin;
            maxXZ = areaMax;
            hasBounds = true;
            return;
        }

        minXZ = Vector2.Min(minXZ, areaMin);
        maxXZ = Vector2.Max(maxXZ, areaMax);
    }

    private static InstanceSimulationState[] BuildInitialSimulationState(InstanceSpawnData[] spawnData)
    {
        InstanceSimulationState[] simulationState = new InstanceSimulationState[spawnData.Length];
        for (int instanceIndex = 0; instanceIndex < spawnData.Length; instanceIndex++)
        {
            InstanceSpawnData spawn = spawnData[instanceIndex];
            simulationState[instanceIndex] = new InstanceSimulationState
            {
                localPositionAndYaw = new Vector4(
                    spawn.localPosition.x,
                    spawn.localPosition.y,
                    spawn.localPosition.z,
                    spawn.yawRadians),
                scaleAndVelocity = new Vector4(spawn.uniformScale, 0.0f, 0.0f, 0.0f)
            };
        }

        return simulationState;
    }

    private static Vector4[] BuildInitialSimulationPositionYawData(InstanceSimulationState[] simulationState)
    {
        int count = simulationState != null ? simulationState.Length : 0;
        Vector4[] data = new Vector4[count];
        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
            data[instanceIndex] = simulationState[instanceIndex].localPositionAndYaw;

        return data;
    }

    private static float[] BuildInitialSimulationScaleData(InstanceSimulationState[] simulationState)
    {
        int count = simulationState != null ? simulationState.Length : 0;
        float[] data = new float[count];
        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
            data[instanceIndex] = simulationState[instanceIndex].scaleAndVelocity.x;

        return data;
    }

    private static Vector2[] BuildInitialSimulationVelocityData(InstanceSimulationState[] simulationState)
    {
        int count = simulationState != null ? simulationState.Length : 0;
        Vector2[] data = new Vector2[count];
        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
        {
            Vector4 scaleAndVelocity = simulationState[instanceIndex].scaleAndVelocity;
            data[instanceIndex] = new Vector2(scaleAndVelocity.y, scaleAndVelocity.z);
        }

        return data;
    }

    private InstanceCombatStateData[] BuildInitialCombatStates(InstanceSpawnData[] spawnData, InstanceSimulationState[] simulationState)
    {
        int count = Mathf.Min(
            spawnData != null ? spawnData.Length : 0,
            simulationState != null ? simulationState.Length : 0);
        InstanceCombatStateData[] combatStates = new InstanceCombatStateData[Mathf.Max(count, _instanceCount)];
        float combatTargetHeight = ResolveCombatTargetHeight();
        float maxHealth = Mathf.Max(1.0f, _combatMaxHealth);

        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
        {
            float cooldownDuration = ComputeInstanceCombatCooldownDuration(
                unchecked((uint)instanceIndex),
                _combatShotsPerSecond,
                _combatShotIntervalJitter);
            float initialCooldown = cooldownDuration * ComputeStableRandom01(unchecked((uint)instanceIndex) ^ 0x68BC21EBu);
            Vector4 localPositionAndYaw = simulationState[instanceIndex].localPositionAndYaw;
            float uniformScale = Mathf.Max(simulationState[instanceIndex].scaleAndVelocity.x, 0.01f);
            float originHeight = Mathf.Max(0.0f, combatTargetHeight * uniformScale);
            Vector3 originLocal = new Vector3(
                localPositionAndYaw.x,
                localPositionAndYaw.y + originHeight,
                localPositionAndYaw.z);

            combatStates[instanceIndex] = new InstanceCombatStateData
            {
                localOriginAndDistance = new Vector4(originLocal.x, originLocal.y, originLocal.z, 1e20f),
                localTargetAndCooldown = new Vector4(originLocal.x, originLocal.y, originLocal.z, initialCooldown),
                localImpactNormalAndHit = new Vector4(0.0f, 1.0f, 0.0f, 0.0f),
                healthAndHitFeedback = new Vector4(maxHealth, 0.0f, 0.0f, 1.0f),
                muzzleFlash = 0.0f,
                targetIndex = -1,
                flags = _enableGpuInstanceCombat ? InstanceCombatFlagActive : 0u,
                debugShotInfoPacked = 0u
            };
        }

        return combatStates;
    }

    private TargetAcquisitionStateData[] BuildInitialTargetAcquisitionStates(InstanceSpawnData[] spawnData, InstanceSimulationState[] simulationState)
    {
        int count = Mathf.Min(
            spawnData != null ? spawnData.Length : 0,
            simulationState != null ? simulationState.Length : 0);
        TargetAcquisitionStateData[] targetStates = new TargetAcquisitionStateData[Mathf.Max(count, _instanceCount)];
        float searchIntervalMin = Mathf.Max(0.02f, _targetAcquisitionSearchIntervalMin);
        float searchIntervalMax = Mathf.Max(searchIntervalMin, _targetAcquisitionSearchIntervalMax);

        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
        {
            Vector4 localPositionAndYaw = simulationState[instanceIndex].localPositionAndYaw;
            float searchInterval = Mathf.Lerp(
                searchIntervalMin,
                searchIntervalMax,
                ComputeStableRandom01(unchecked((uint)instanceIndex) ^ 0x5A17C0DEu));
            float initialSearchTimer = searchInterval * ComputeStableRandom01(unchecked((uint)instanceIndex) ^ 0xC0FFEE31u);

            targetStates[instanceIndex] = new TargetAcquisitionStateData
            {
                lastKnownTargetAndScore = new Vector4(localPositionAndYaw.x, localPositionAndYaw.y, localPositionAndYaw.z, 0.0f),
                timers = new Vector4(initialSearchTimer, 0.0f, 0.0f, 0.0f),
                debugRejectCounts0 = Vector4.zero,
                debugRejectCounts1 = Vector4.zero,
                currentTargetIndex = -1,
                lastAttackerIndex = -1,
                flags = 0u,
                debugVisibleSampleIndex = -1
            };
        }

        return targetStates;
    }

    private static float ComputeInstanceCombatCooldownDuration(uint instanceIndex, float combatShotsPerSecond, float combatShotIntervalJitter)
    {
        float shotsPerSecond = Mathf.Max(combatShotsPerSecond, 0.0f);
        if (shotsPerSecond <= Mathf.Epsilon)
            return 0.0f;

        float baseCooldown = 1.0f / shotsPerSecond;
        float jitter = Mathf.Clamp01(combatShotIntervalJitter);
        if (jitter <= Mathf.Epsilon)
            return baseCooldown;

        float random01 = ComputeStableRandom01(instanceIndex ^ 0x9E3779B9u);
        float multiplier = Mathf.Lerp(1.0f - jitter, 1.0f + jitter, random01);
        return baseCooldown * Mathf.Max(0.05f, multiplier);
    }

    private static float ComputeStableRandom01(uint seed)
    {
        unchecked
        {
            seed ^= 2747636419u;
            seed *= 2654435769u;
            seed ^= seed >> 16;
            seed *= 2654435769u;
            seed ^= seed >> 16;
            seed *= 2654435769u;
            return (seed & 0x00FFFFFFu) / 16777215.0f;
        }
    }

    private Bounds BuildLocalCrowdBounds(InstanceSpawnData[] spawnData, Bounds agentLocalBounds)
    {
        if (spawnData == null || spawnData.Length == 0)
            return new Bounds(agentLocalBounds.center, agentLocalBounds.size);

        Bounds bounds = BuildLocalBoundsForRange(spawnData, 0, spawnData.Length, agentLocalBounds);
        return ExpandBoundsToSimulationArea(bounds);
    }

    private Bounds ExpandBoundsToSimulationArea(Bounds bounds)
    {
        GetSimulationAreaExtentsXZ(null, out Vector2 minXZ, out Vector2 maxXZ);
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float padding = Mathf.Max(_boundsPadding, 0.0f);
        min.x = Mathf.Min(min.x, minXZ.x - padding);
        min.z = Mathf.Min(min.z, minXZ.y - padding);
        max.x = Mathf.Max(max.x, maxXZ.x + padding);
        max.z = Mathf.Max(max.z, maxXZ.y + padding);
        bounds.SetMinMax(min, max);
        return bounds;
    }

    private void BuildIndirectArgsBuffers()
    {
        if (!_hasRuntimeRenderResource)
        {
            _indirectArgsBuffers = Array.Empty<GraphicsBuffer>();
            _indirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
            _visibleInstanceIndexCache = Array.Empty<uint>();
            return;
        }

        RuntimeRenderResource resource = _runtimeRenderResource;
        int subMeshCount = resource.runtimeMaterials != null
            ? Mathf.Min(resource.mesh.subMeshCount, resource.runtimeMaterials.Length)
            : 0;
        _indirectArgsBuffers = new GraphicsBuffer[subMeshCount];
        _indirectArgsCache = new GraphicsBuffer.IndirectDrawIndexedArgs[subMeshCount];

        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            GraphicsBuffer buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(resource.mesh, subMeshIndex, 0u);
            buffer.SetData(new[] { args });
            _indirectArgsBuffers[subMeshIndex] = buffer;
            _indirectArgsCache[subMeshIndex] = args;
        }

        _visibleInstanceIndexBuffer = new ComputeBuffer(Mathf.Max(1, _instanceCount), sizeof(uint));
        _visibleInstanceCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _visibleInstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        _visibleInstanceIndexCache = new uint[Mathf.Max(1, _instanceCount)];
        _allInstanceIndicesCache = new uint[Mathf.Max(1, _instanceCount)];
        for (uint instanceIndex = 0; instanceIndex < _allInstanceIndicesCache.Length; instanceIndex++)
            _allInstanceIndicesCache[instanceIndex] = instanceIndex;
    }

    private void BuildCombatTracerResources()
    {
        ReleaseBuffer(ref _combatTracerArgsBuffer);
        if (_combatTracerMaterial != null)
        {
            DestroyRuntimeMaterial(_combatTracerMaterial);
            _combatTracerMaterial = null;
        }

        if (!_enableCombatTracer || !_enableGpuInstanceCombat)
            return;

        Shader tracerShader = ResolveCombatTracerShader();
        if (tracerShader == null)
            return;

#if UNITY_EDITOR
        ResolveDefaultCombatFxTextures();
#endif

        if (_combatTracerMesh == null)
            _combatTracerMesh = CreateCombatTracerMesh();

        _combatTracerMaterial = new Material(tracerShader);
        if (_combatTracerMaterialTemplate != null)
            _combatTracerMaterial.CopyPropertiesFromMaterial(_combatTracerMaterialTemplate);
        _combatTracerMaterial.name = $"{_runtimeRenderResource.animationAsset.name}_CombatTracer";
        _combatTracerMaterial.hideFlags = HideFlags.HideAndDontSave;
        _combatTracerMaterial.enableInstancing = true;

        _combatTracerArgsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
            1,
            GraphicsBuffer.IndirectDrawIndexedArgs.size);
        GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(_combatTracerMesh, 0, 0u);
        _combatTracerArgsBuffer.SetData(new[] { args });
        UpdateCombatTracerMaterialParameters();
    }

#if UNITY_EDITOR
    private void ResolveDefaultCombatFxTextures()
    {
        if (_combatMuzzleFlashTexture == null)
        {
            _combatMuzzleFlashTexture = LoadDefaultCombatFxTexture(
                DefaultCombatMuzzleFlashTexturePath,
                DefaultCombatMuzzleFlashTextureSearchFilter);
        }

        if (_combatTracerTexture == null)
        {
            _combatTracerTexture = LoadDefaultCombatFxTexture(
                DefaultCombatTracerTexturePath,
                DefaultCombatTracerTextureSearchFilter);
        }

        if (_combatImpactTexture == null)
        {
            _combatImpactTexture = LoadDefaultCombatFxTexture(
                DefaultCombatImpactTexturePath,
                DefaultCombatImpactTextureSearchFilter);
        }
    }

    private static Texture2D LoadDefaultCombatFxTexture(string assetPath, string fallbackSearchFilter)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (texture != null)
            return texture;

        string[] assetGuids = AssetDatabase.FindAssets($"{fallbackSearchFilter} t:Texture2D", new[] { DefaultCombatFxTextureFolder });
        for (int i = 0; i < assetGuids.Length; i++)
        {
            string candidatePath = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(candidatePath);
            if (texture != null)
                return texture;
        }

        return null;
    }
#endif

    private void BuildRenderChunks(InstanceSpawnData[] spawnData, Bounds agentLocalBounds)
    {
        uint[] instanceRenderChunkIndices = new uint[Mathf.Max(1, _instanceCount)];
        if (spawnData == null || spawnData.Length == 0)
        {
            _renderChunks = Array.Empty<RenderChunk>();
            EnsureRenderChunkVisibilityBuffers();
            if (_instanceRenderChunkBuffer != null)
                _instanceRenderChunkBuffer.SetData(instanceRenderChunkIndices, 0, 0, Mathf.Min(instanceRenderChunkIndices.Length, _instanceRenderChunkBuffer.count));
            return;
        }

        float chunkSize = Mathf.Max(0.5f, _renderChunkWorldSize);
        Vector2 origin = new Vector2(_localCrowdBounds.min.x, _localCrowdBounds.min.z);
        Dictionary<Vector2Int, List<int>> chunkInstanceLookup = new Dictionary<Vector2Int, List<int>>();
        List<Vector2Int> chunkOrder = new List<Vector2Int>();

        for (int instanceIndex = 0; instanceIndex < spawnData.Length; instanceIndex++)
        {
            Vector2 localXZ = new Vector2(spawnData[instanceIndex].localPosition.x, spawnData[instanceIndex].localPosition.z);
            Vector2Int chunkCoord = new Vector2Int(
                Mathf.FloorToInt((localXZ.x - origin.x) / chunkSize),
                Mathf.FloorToInt((localXZ.y - origin.y) / chunkSize));

            if (!chunkInstanceLookup.TryGetValue(chunkCoord, out List<int> indices))
            {
                indices = new List<int>();
                chunkInstanceLookup.Add(chunkCoord, indices);
                chunkOrder.Add(chunkCoord);
            }

            indices.Add(instanceIndex);
        }

        _renderChunks = new RenderChunk[chunkOrder.Count];
        for (int chunkIndex = 0; chunkIndex < chunkOrder.Count; chunkIndex++)
        {
            List<int> indices = chunkInstanceLookup[chunkOrder[chunkIndex]];
            uint[] instanceIndices = new uint[indices.Count];
            for (int index = 0; index < indices.Count; index++)
            {
                int instanceIndex = indices[index];
                instanceIndices[index] = (uint)instanceIndex;
                if ((uint)instanceIndex < (uint)instanceRenderChunkIndices.Length)
                    instanceRenderChunkIndices[instanceIndex] = (uint)chunkIndex;
            }

            _renderChunks[chunkIndex] = new RenderChunk
            {
                instanceIndices = instanceIndices,
                localBounds = BuildLocalBoundsForIndices(spawnData, indices, agentLocalBounds)
            };
        }

        EnsureRenderChunkVisibilityBuffers();
        if (_instanceRenderChunkBuffer != null)
            _instanceRenderChunkBuffer.SetData(instanceRenderChunkIndices, 0, 0, Mathf.Min(instanceRenderChunkIndices.Length, _instanceRenderChunkBuffer.count));
    }

    private void EnsureRenderChunkVisibilityBuffers()
    {
        int requiredChunkCount = Mathf.Max(1, _renderChunks != null ? _renderChunks.Length : 0);
        if (_visibleRenderChunkMaskBuffer == null || _visibleRenderChunkMaskBuffer.count != requiredChunkCount)
        {
            ReleaseBuffer(ref _visibleRenderChunkMaskBuffer);
            _visibleRenderChunkMaskBuffer = new ComputeBuffer(requiredChunkCount, sizeof(uint));
        }

        if (_visibleRenderChunkMaskUploadCache == null || _visibleRenderChunkMaskUploadCache.Length != _visibleRenderChunkMaskBuffer.count)
            _visibleRenderChunkMaskUploadCache = new uint[_visibleRenderChunkMaskBuffer.count];

        int requiredInstanceCount = Mathf.Max(1, _instanceCount);
        if (_instanceRenderChunkBuffer == null || _instanceRenderChunkBuffer.count != requiredInstanceCount)
        {
            ReleaseBuffer(ref _instanceRenderChunkBuffer);
            _instanceRenderChunkBuffer = new ComputeBuffer(requiredInstanceCount, sizeof(uint));
        }
    }

    private Bounds BuildLocalBoundsForRange(InstanceSpawnData[] spawnData, int startInstance, int instanceCount, Bounds agentLocalBounds)
    {
        bool hasBounds = false;
        Bounds bounds = default;

        for (int offset = 0; offset < instanceCount; offset++)
        {
            InstanceSpawnData instance = spawnData[startInstance + offset];
            Matrix4x4 instanceMatrix = Matrix4x4.TRS(
                instance.localPosition,
                Quaternion.Euler(0.0f, instance.yawRadians * Mathf.Rad2Deg, 0.0f),
                Vector3.one * instance.uniformScale);
            Bounds instanceBounds = TransformBounds(instanceMatrix, agentLocalBounds);

            if (!hasBounds)
            {
                bounds = instanceBounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(instanceBounds.min);
            bounds.Encapsulate(instanceBounds.max);
        }

        if (!hasBounds)
            return new Bounds(agentLocalBounds.center, agentLocalBounds.size);

        return ExpandRenderBounds(bounds);
    }

    private Bounds BuildLocalBoundsForIndices(InstanceSpawnData[] spawnData, IReadOnlyList<int> indices, Bounds agentLocalBounds)
    {
        if (indices == null || indices.Count == 0)
            return new Bounds(agentLocalBounds.center, agentLocalBounds.size);

        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < indices.Count; i++)
        {
            InstanceSpawnData instance = spawnData[indices[i]];
            Matrix4x4 instanceMatrix = Matrix4x4.TRS(
                instance.localPosition,
                Quaternion.Euler(0.0f, instance.yawRadians * Mathf.Rad2Deg, 0.0f),
                Vector3.one * instance.uniformScale);
            Bounds instanceBounds = TransformBounds(instanceMatrix, agentLocalBounds);
            if (!hasBounds)
            {
                bounds = instanceBounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(instanceBounds.min);
            bounds.Encapsulate(instanceBounds.max);
        }

        return ExpandRenderBounds(bounds);
    }

    private Bounds ExpandRenderBounds(Bounds bounds)
    {
        float dynamicPadding = _boundsPadding + (_enableApproximateCollision ? _maxDisplacementFromSpawn : 0.0f);
        bounds.Expand(dynamicPadding * 2.0f);
        return bounds;
    }

    private static GraphicsBuffer.IndirectDrawIndexedArgs BuildIndirectArgs(Mesh mesh, int subMeshIndex, uint instanceCount)
    {
        return new GraphicsBuffer.IndirectDrawIndexedArgs
        {
            indexCountPerInstance = mesh.GetIndexCount(subMeshIndex),
            instanceCount = instanceCount,
            startIndex = mesh.GetIndexStart(subMeshIndex),
            baseVertexIndex = (uint)mesh.GetBaseVertex(subMeshIndex),
            startInstance = 0u
        };
    }

    private void BindStaticResources()
    {
        if (!_hasRuntimeRenderResource || _runtimeRenderResource.runtimeMaterials == null)
            return;

        RuntimeRenderResource resource = _runtimeRenderResource;
        Texture2D boneTexture = resource.animationAsset.BoneAnimationTexture;

        for (int materialIndex = 0; materialIndex < resource.runtimeMaterials.Length; materialIndex++)
        {
            Material runtimeMaterial = resource.runtimeMaterials[materialIndex];
            runtimeMaterial.SetTexture(BoneAnimationTextureId, boneTexture);
            runtimeMaterial.SetBuffer(InstanceTransformsId, _instanceTransformBuffer);
            runtimeMaterial.SetBuffer(InstanceFrameDataId, _instanceFrameDataBuffer);
            runtimeMaterial.SetBuffer(InstanceFrameBlendDataId, _instanceFrameBlendDataBuffer);
            runtimeMaterial.SetBuffer(VisibleInstanceIndicesId, _visibleInstanceIndexBuffer);
            runtimeMaterial.SetVector(CrowdRootLocalRow0Id, resource.crowdRootRow0);
            runtimeMaterial.SetVector(CrowdRootLocalRow1Id, resource.crowdRootRow1);
            runtimeMaterial.SetVector(CrowdRootLocalRow2Id, resource.crowdRootRow2);
        }

        BindCombatTracerMaterial();
    }

    private void BindCombatTracerMaterial()
    {
        if (_combatTracerMaterial == null)
            return;

        _combatTracerMaterial.SetBuffer(SpawnDataId, _spawnDataBuffer);
        _combatTracerMaterial.SetBuffer(CombatStateBufferId, _combatStateBuffer);
        _combatTracerMaterial.SetBuffer(VisibleInstanceIndicesId, _visibleInstanceIndexBuffer);
        UpdateCombatTracerMaterialParameters();
    }

    private void UpdateCombatTracerMaterialParameters()
    {
        if (_combatTracerMaterial == null)
            return;

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        _combatTracerMaterial.SetColor(TracerColorCampAId, _combatTracerColorCampA);
        _combatTracerMaterial.SetColor(TracerColorCampBId, _combatTracerColorCampB);
        _combatTracerMaterial.SetTexture(MuzzleFlashTexId, _combatMuzzleFlashTexture != null ? _combatMuzzleFlashTexture : Texture2D.blackTexture);
        _combatTracerMaterial.SetTexture(TracerTexId, _combatTracerTexture != null ? _combatTracerTexture : Texture2D.whiteTexture);
        _combatTracerMaterial.SetTexture(ImpactTexId, _combatImpactTexture != null ? _combatImpactTexture : Texture2D.blackTexture);
        _combatTracerMaterial.SetFloat(TracerWidthId, Mathf.Max(0.001f, _combatTracerWidth));
        _combatTracerMaterial.SetFloat(TracerBrightnessId, Mathf.Max(0.0f, _combatTracerBrightness));
        _combatTracerMaterial.SetFloat(TracerMinFlashId, Mathf.Clamp01(_combatTracerMinFlash));
        _combatTracerMaterial.SetFloat(CombatRangeId, Mathf.Max(0.1f, _combatRange));
        _combatTracerMaterial.SetFloat(MuzzleFlashSizeId, Mathf.Max(0.001f, _combatMuzzleFlashSize));
        _combatTracerMaterial.SetFloat(ImpactFlashSizeId, Mathf.Max(0.001f, _combatImpactFlashSize));
        _combatTracerMaterial.SetFloat(ImpactPointOffsetId, Mathf.Max(0.0f, _combatImpactPointOffset));
        _combatTracerMaterial.SetFloat(MuzzleFlashBrightnessId, Mathf.Max(0.0f, _combatMuzzleFlashBrightness));
        _combatTracerMaterial.SetFloat(ImpactFlashBrightnessId, Mathf.Max(0.0f, _combatImpactFlashBrightness));
        _combatTracerMaterial.SetVector(
            MuzzleFlashFlipbookId,
            new Vector4(
                Mathf.Max(1, _combatMuzzleFlashFlipbookColumns),
                Mathf.Max(1, _combatMuzzleFlashFlipbookRows),
                0.0f,
                0.0f));
        _combatTracerMaterial.SetVector(
            ImpactFlashFlipbookId,
            new Vector4(
                Mathf.Max(1, _combatImpactFlashFlipbookColumns),
                Mathf.Max(1, _combatImpactFlashFlipbookRows),
                0.0f,
                0.0f));
        _combatTracerMaterial.SetVector(RootPositionId, new Vector4(localToWorld.m03, localToWorld.m13, localToWorld.m23, 0.0f));
        _combatTracerMaterial.SetVector(RootRightId, new Vector4(localToWorld.m00, localToWorld.m10, localToWorld.m20, 0.0f));
        _combatTracerMaterial.SetVector(RootUpId, new Vector4(localToWorld.m01, localToWorld.m11, localToWorld.m21, 0.0f));
        _combatTracerMaterial.SetVector(RootForwardId, new Vector4(localToWorld.m02, localToWorld.m12, localToWorld.m22, 0.0f));
    }

}
