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

        if (!ResolveRuntimeRenderResources(out CrowdVatAnimationAsset primaryAnimationAsset))
            return false;

        if (_updateCompute == null)
            return false;

        // Unity editor hot-reload can invalidate cached compute kernel handles
        // while keeping GPU buffers alive, so refresh them on each init path.
        ResolveKernelHandles();
        if (!AreKernelHandlesResolved())
            return false;

        if (!TryResolveRuntimeShaders(out Shader shader, out Shader tertiaryLodShader))
            return false;

        if (!TryResolveActiveClip(primaryAnimationAsset))
            return false;

        if (needsRebuild)
        {
            BuildRuntimeMaterials(shader, tertiaryLodShader);
            BuildInstanceBuffers();
            BuildIndirectArgsBuffers();
            BuildCombatTracerResources();
            BindStaticResources();
            _resourcesDirty = false;
        }

        return _hasRuntimeRenderResource &&
            _hasClip &&
            _visibleInstanceIndexBuffer != null &&
            _visibleLod1InstanceIndexBuffer != null &&
            _visibleLod2InstanceIndexBuffer != null &&
            AreActiveVisibleLodArgsReady() &&
            AreKernelHandlesResolved();
    }

    private Shader ResolveIndirectShader()
    {
        if (_indirectShader != null)
            return _indirectShader;

        _indirectShader = Shader.Find(DefaultShaderName);
        return _indirectShader;
    }

    private bool TryResolveRuntimeShaders(out Shader shader, out Shader tertiaryLodShader)
    {
        shader = ResolveIndirectShader();
        if (shader == null)
        {
            tertiaryLodShader = null;
            return false;
        }

        tertiaryLodShader = _hasTertiaryRuntimeRenderResource
            ? ResolveTertiaryLodIndirectShader(shader)
            : shader;
        return tertiaryLodShader != null;
    }

    private Shader ResolveTertiaryLodIndirectShader(Shader fallbackShader)
    {
        if (_tertiaryLodIndirectShader != null)
            return _tertiaryLodIndirectShader;

        _tertiaryLodIndirectShader = Shader.Find(DefaultTertiaryLodShaderName);
        if (_tertiaryLodIndirectShader != null)
            return _tertiaryLodIndirectShader;

        Debug.LogWarning(
            $"Crowd VAT 未找到第三档 LOD shader `{DefaultTertiaryLodShaderName}`，LOD2 将回退到 `{fallbackShader.name}`。");
        return fallbackShader;
    }

    private bool AreActiveVisibleLodArgsReady()
    {
        if (_activeVisibleLodRenderResources == null ||
            _activeVisibleLodArgsBuffers == null ||
            _activeVisibleLodRenderResources.Length == 0 ||
            _activeVisibleLodArgsBuffers.Length != _activeVisibleLodRenderResources.Length)
        {
            return false;
        }

        for (int tierIndex = 0; tierIndex < _activeVisibleLodArgsBuffers.Length; tierIndex++)
        {
            GraphicsBuffer[] argsBuffers = _activeVisibleLodArgsBuffers[tierIndex];
            if (argsBuffers == null || argsBuffers.Length == 0)
                return false;
        }

        return true;
    }

    private bool ResolveRuntimeRenderResources(out CrowdVatAnimationAsset primaryAnimationAsset)
    {
        CrowdVatAnimationAsset animationAsset = ResolveConfiguredAnimationAsset();
        CrowdVatPlayer templatePrefab = ResolveConfiguredTemplatePrefab();
        if (animationAsset == null || animationAsset.Mesh == null)
        {
            primaryAnimationAsset = null;
            _runtimeRenderResource = default;
            _secondaryRuntimeRenderResource = default;
            _tertiaryRuntimeRenderResource = default;
            _activeVisibleLodRenderResources = Array.Empty<RuntimeRenderResource>();
            _hasRuntimeRenderResource = false;
            _hasSecondaryRuntimeRenderResource = false;
            _hasTertiaryRuntimeRenderResource = false;
            return false;
        }

        _runtimeRenderResource = BuildRuntimeRenderResource(_runtimeRenderResource, templatePrefab, animationAsset);
        _hasRuntimeRenderResource = true;

        _hasSecondaryRuntimeRenderResource = TryResolveOptionalRuntimeRenderResource(
            _secondaryLodTemplatePrefab,
            _secondaryLodAnimationAsset,
            animationAsset,
            ref _secondaryRuntimeRenderResource);
        _hasTertiaryRuntimeRenderResource = TryResolveOptionalRuntimeRenderResource(
            _tertiaryLodTemplatePrefab,
            _tertiaryLodAnimationAsset,
            animationAsset,
            ref _tertiaryRuntimeRenderResource);

        RebuildActiveVisibleLodResourceViews();
        primaryAnimationAsset = animationAsset;
        return true;
    }

    private Bounds ResolveGroundingMeshBounds(CrowdVatAnimationAsset animationAsset)
    {
        if (animationAsset == null)
            return new Bounds(Vector3.zero, Vector3.one);

        if (_currentClipIndex != InvalidClipIndex &&
            animationAsset.TryGetClip(_currentClipIndex, out CrowdVatAnimationAsset.ClipInfo currentClip) &&
            currentClip.HasBounds)
        {
            return currentClip.Bounds;
        }

        if (animationAsset.TryGetClipIndex(_clipName, out int configuredClipIndex) &&
            animationAsset.TryGetClip(configuredClipIndex, out CrowdVatAnimationAsset.ClipInfo configuredClip) &&
            configuredClip.HasBounds)
        {
            return configuredClip.Bounds;
        }

        return animationAsset.MeshBounds;
    }

    private CrowdVatPlayer ResolveConfiguredTemplatePrefab()
    {
        return _templatePrefab;
    }

    private CrowdVatAnimationAsset ResolveConfiguredAnimationAsset()
    {
        return ResolveConfiguredAnimationAsset(_templatePrefab, _animationAsset);
    }

    private static CrowdVatAnimationAsset ResolveConfiguredAnimationAsset(
        CrowdVatPlayer templatePrefab,
        CrowdVatAnimationAsset animationAsset)
    {
        if (templatePrefab != null && templatePrefab.AnimationAsset != null)
            return templatePrefab.AnimationAsset;

        return animationAsset;
    }

    private RuntimeRenderResource BuildRuntimeRenderResource(
        RuntimeRenderResource previousResource,
        CrowdVatPlayer templatePrefab,
        CrowdVatAnimationAsset animationAsset)
    {
        Material[] preservedRuntimeMaterials =
            previousResource.animationAsset == animationAsset &&
            previousResource.mesh == animationAsset.Mesh &&
            previousResource.templatePrefab == templatePrefab
                ? previousResource.runtimeMaterials
                : null;

        ResolveCrowdRootRows(
            templatePrefab,
            ResolveGroundingMeshBounds(animationAsset),
            out Vector4 row0,
            out Vector4 row1,
            out Vector4 row2,
            out Matrix4x4 rootLocalMatrix);

        return new RuntimeRenderResource
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
    }

    private bool TryResolveOptionalRuntimeRenderResource(
        CrowdVatPlayer templatePrefab,
        CrowdVatAnimationAsset animationAsset,
        CrowdVatAnimationAsset primaryAnimationAsset,
        ref RuntimeRenderResource runtimeRenderResource)
    {
        CrowdVatAnimationAsset resolvedAnimationAsset = ResolveConfiguredAnimationAsset(templatePrefab, animationAsset);
        if (resolvedAnimationAsset == null || resolvedAnimationAsset.Mesh == null)
        {
            runtimeRenderResource = default;
            return false;
        }

        if (!HasCompatibleRenderLodClips(primaryAnimationAsset, resolvedAnimationAsset))
        {
            Debug.LogWarning(
                $"Crowd VAT LOD `{resolvedAnimationAsset.name}` 与主动画资产 `{primaryAnimationAsset.name}` 的 clip 布局不一致，已跳过该 LOD。");
            runtimeRenderResource = default;
            return false;
        }

        runtimeRenderResource = BuildRuntimeRenderResource(runtimeRenderResource, templatePrefab, resolvedAnimationAsset);
        return true;
    }

    private static bool HasCompatibleRenderLodClips(
        CrowdVatAnimationAsset primaryAnimationAsset,
        CrowdVatAnimationAsset lodAnimationAsset)
    {
        if (primaryAnimationAsset == null || lodAnimationAsset == null)
            return false;

        if (ReferenceEquals(primaryAnimationAsset, lodAnimationAsset))
            return true;

        if (primaryAnimationAsset.ClipCount != lodAnimationAsset.ClipCount)
            return false;

        for (int clipIndex = 0; clipIndex < primaryAnimationAsset.ClipCount; clipIndex++)
        {
            if (!primaryAnimationAsset.TryGetClip(clipIndex, out CrowdVatAnimationAsset.ClipInfo primaryClip) ||
                !lodAnimationAsset.TryGetClip(clipIndex, out CrowdVatAnimationAsset.ClipInfo lodClip))
            {
                return false;
            }

            if (!string.Equals(primaryClip.Name, lodClip.Name, StringComparison.Ordinal) ||
                primaryClip.StartFrame != lodClip.StartFrame ||
                primaryClip.FrameCount != lodClip.FrameCount ||
                !Mathf.Approximately(primaryClip.LengthSeconds, lodClip.LengthSeconds))
            {
                return false;
            }
        }

        return true;
    }

    private void RebuildActiveVisibleLodResourceViews()
    {
        List<RuntimeRenderResource> resources = new List<RuntimeRenderResource>(3);
        if (_hasRuntimeRenderResource)
            resources.Add(_runtimeRenderResource);

        if (_hasSecondaryRuntimeRenderResource)
            resources.Add(_secondaryRuntimeRenderResource);
        else if (_hasTertiaryRuntimeRenderResource)
            resources.Add(_tertiaryRuntimeRenderResource);

        if (_hasSecondaryRuntimeRenderResource && _hasTertiaryRuntimeRenderResource)
            resources.Add(_tertiaryRuntimeRenderResource);

        _activeVisibleLodRenderResources = resources.ToArray();
    }

    private void ResolveCrowdRootRows(
        CrowdVatPlayer templatePrefab,
        Bounds meshBounds,
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

        Quaternion localRotation = Quaternion.Euler(localEulerAngles);
        rootLocalMatrix = Matrix4x4.TRS(localPosition, localRotation, localScale);
        Bounds agentLocalBounds = TransformBounds(rootLocalMatrix, meshBounds);
        if (agentLocalBounds.size.sqrMagnitude > 1e-8f)
        {
            localPosition.y -= agentLocalBounds.min.y;
            rootLocalMatrix = Matrix4x4.TRS(localPosition, localRotation, localScale);
        }

        row0 = new Vector4(rootLocalMatrix.m00, rootLocalMatrix.m10, rootLocalMatrix.m20, rootLocalMatrix.m03);
        row1 = new Vector4(rootLocalMatrix.m01, rootLocalMatrix.m11, rootLocalMatrix.m21, rootLocalMatrix.m13);
        row2 = new Vector4(rootLocalMatrix.m02, rootLocalMatrix.m12, rootLocalMatrix.m22, rootLocalMatrix.m23);
    }

    private void ResolveKernelHandles()
    {
        _buildAliveInstanceListKernel = _updateCompute.FindKernel(BuildAliveInstanceListKernelName);
        _buildAliveDispatchArgsKernel = _updateCompute.FindKernel(BuildAliveDispatchArgsKernelName);
        _clearWakeGridKernel = _updateCompute.FindKernel(ClearWakeGridKernelName);
        _buildWakeGridKernel = _updateCompute.FindKernel(BuildWakeGridKernelName);
        _evaluatePhysicsActiveKernel = _updateCompute.FindKernel(EvaluatePhysicsActiveKernelName);
        _buildPhysicsActiveInstanceListKernel = _updateCompute.FindKernel(BuildPhysicsActiveInstanceListKernelName);
        _buildPhysicsActiveDispatchArgsKernel = _updateCompute.FindKernel(BuildPhysicsActiveDispatchArgsKernelName);
        _evaluateCombatActiveKernel = _updateCompute.FindKernel(EvaluateCombatActiveKernelName);
        _buildCombatActiveInstanceListKernel = _updateCompute.FindKernel(BuildCombatActiveInstanceListKernelName);
        _compactCombatActiveInstanceListKernel = _updateCompute.FindKernel(CompactCombatActiveInstanceListKernelName);
        _buildCombatActiveDispatchArgsKernel = _updateCompute.FindKernel(BuildCombatActiveDispatchArgsKernelName);
        _clearCombatSquadCandidateCountersKernel = _updateCompute.FindKernel(ClearCombatSquadCandidateCountersKernelName);
        _clearVisibleRuntimeSquadBoundsKernel = _updateCompute.FindKernel(ClearVisibleRuntimeSquadBoundsKernelName);
        _buildVisibleRuntimeSquadBoundsKernel = _updateCompute.FindKernel(BuildVisibleRuntimeSquadBoundsKernelName);
        _cullVisibleRuntimeSquadsKernel = _updateCompute.FindKernel(CullVisibleRuntimeSquadsKernelName);
        _buildVisibleRuntimeInstanceListKernel = _updateCompute.FindKernel(BuildVisibleRuntimeInstanceListKernelName);
        _buildVisibleIndirectArgsKernel = _updateCompute.FindKernel(BuildVisibleIndirectArgsKernelName);
        _predictKernel = _updateCompute.FindKernel(PredictKernelName);
        _buildGridClearDispatchArgsKernel = _updateCompute.FindKernel(BuildGridClearDispatchArgsKernelName);
        _clearGridKernel = _updateCompute.FindKernel(ClearGridKernelName);
        _buildGridKernel = _updateCompute.FindKernel(BuildGridKernelName);
        _buildSpatialElementsKernel = _updateCompute.FindKernel(BuildSpatialElementsKernelName);
        _solveCrowdKernel = _updateCompute.FindKernel(SolveCrowdKernelName);
        _buildCombatCandidateClustersKernel = _updateCompute.FindKernel(BuildCombatCandidateClustersKernelName);
        _resolveTargetAcquisitionKernel = _updateCompute.FindKernel(ResolveTargetAcquisitionKernelName);
        _buildTargetAcquisitionLosDispatchArgsKernel = _updateCompute.FindKernel(BuildTargetAcquisitionLosDispatchArgsKernelName);
        _resolveTargetAcquisitionLineOfSightKernel = _updateCompute.FindKernel(ResolveTargetAcquisitionLineOfSightKernelName);
        _buildSquadAcquisitionDispatchArgsKernel = _updateCompute.FindKernel(BuildSquadAcquisitionDispatchArgsKernelName);
        _evaluateSquadAcquisitionCandidatesKernel = _updateCompute.FindKernel(EvaluateSquadAcquisitionCandidatesKernelName);
        _finalizeTargetAcquisitionKernel = _updateCompute.FindKernel(FinalizeTargetAcquisitionKernelName);
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
            _clearWakeGridKernel >= 0 &&
            _buildWakeGridKernel >= 0 &&
            _evaluatePhysicsActiveKernel >= 0 &&
            _buildPhysicsActiveInstanceListKernel >= 0 &&
            _buildPhysicsActiveDispatchArgsKernel >= 0 &&
            _evaluateCombatActiveKernel >= 0 &&
            _buildCombatActiveInstanceListKernel >= 0 &&
            _compactCombatActiveInstanceListKernel >= 0 &&
            _buildCombatActiveDispatchArgsKernel >= 0 &&
            _clearCombatSquadCandidateCountersKernel >= 0 &&
            _clearVisibleRuntimeSquadBoundsKernel >= 0 &&
            _buildVisibleRuntimeSquadBoundsKernel >= 0 &&
            _cullVisibleRuntimeSquadsKernel >= 0 &&
            _buildVisibleRuntimeInstanceListKernel >= 0 &&
            _buildVisibleIndirectArgsKernel >= 0 &&
            _predictKernel >= 0 &&
            _buildGridClearDispatchArgsKernel >= 0 &&
            _clearGridKernel >= 0 &&
            _buildGridKernel >= 0 &&
            _buildSpatialElementsKernel >= 0 &&
            _solveCrowdKernel >= 0 &&
            _buildCombatCandidateClustersKernel >= 0 &&
            _resolveTargetAcquisitionKernel >= 0 &&
            _buildTargetAcquisitionLosDispatchArgsKernel >= 0 &&
            _resolveTargetAcquisitionLineOfSightKernel >= 0 &&
            _buildSquadAcquisitionDispatchArgsKernel >= 0 &&
            _evaluateSquadAcquisitionCandidatesKernel >= 0 &&
            _finalizeTargetAcquisitionKernel >= 0 &&
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
            _deathStateBuffer != null &&
            _aliveInstanceIndexBuffer != null &&
            _aliveInstanceCounterBuffer != null &&
            _aliveInstanceDispatchArgsBuffer != null &&
            _physicsActiveStateBuffer != null &&
            _physicsActivationMetaBuffer != null &&
            _physicsActiveInstanceIndexBuffer != null &&
            _physicsActiveInstanceCounterBuffer != null &&
            _physicsActiveInstanceDispatchArgsBuffer != null &&
            _wakeGridCounterBuffer != null &&
            _wakeGridOccupantBuffer != null &&
            _gridCounterBuffer != null &&
            _gridOccupantBuffer != null &&
            _gridPrevTouchedCellBuffer != null &&
            _gridCurrTouchedCellBuffer != null &&
            _gridPrevTouchedCounterBuffer != null &&
            _gridCurrTouchedCounterBuffer != null &&
            _gridClearDispatchArgsBuffer != null &&
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
            _targetAcquisitionCandidateBuffer != null &&
            _targetAcquisitionLosDispatchArgsBuffer != null &&
            _squadAcquisitionDispatchArgsBuffer != null &&
            _combatActiveStateBuffer != null &&
            _combatActivationMetaBuffer != null &&
            _combatActiveInstanceIndexBuffer != null &&
            _combatActiveInstanceCounterBuffer != null &&
            _combatActiveInstanceDispatchArgsBuffer != null &&
            _combatSquadCandidateBuffer != null &&
            _combatSquadCandidateCounterBuffer != null &&
            _combatCandidateClusterWorkItemBuffer != null &&
            _combatCandidateClusterDispatchArgsBuffer != null &&
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
            _visibleLod1InstanceIndexBuffer != null &&
            _visibleLod1InstanceCounterBuffer != null &&
            _visibleLod2InstanceIndexBuffer != null &&
            _visibleLod2InstanceCounterBuffer != null &&
            _visibleRuntimeSquadMaskBuffer != null &&
            _runtimeSquadBoundsBuffer != null &&
            _indirectArgsBuffers.Length > 0 &&
            AreActiveVisibleLodArgsReady();
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

        if (_secondaryLodTemplatePrefab != null && _secondaryLodTemplatePrefab.AnimationAsset != null)
            _secondaryLodAnimationAsset = _secondaryLodTemplatePrefab.AnimationAsset;

        if (_tertiaryLodTemplatePrefab != null && _tertiaryLodTemplatePrefab.AnimationAsset != null)
            _tertiaryLodAnimationAsset = _tertiaryLodTemplatePrefab.AnimationAsset;
    }

    private void RefreshRuntimeTargets()
    {
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

    private void BuildRuntimeMaterials(Shader shader, Shader tertiaryLodShader)
    {
        if (_hasRuntimeRenderResource)
            _runtimeRenderResource = BuildRuntimeMaterialsForResource(_runtimeRenderResource, shader, _indirectMaterialTemplate);

        if (_hasSecondaryRuntimeRenderResource)
            _secondaryRuntimeRenderResource = BuildRuntimeMaterialsForResource(_secondaryRuntimeRenderResource, shader, _indirectMaterialTemplate);

        if (_hasTertiaryRuntimeRenderResource)
            _tertiaryRuntimeRenderResource = BuildRuntimeMaterialsForResource(_tertiaryRuntimeRenderResource, tertiaryLodShader, _indirectMaterialTemplate);

        RebuildActiveVisibleLodResourceViews();
    }

    private RuntimeRenderResource BuildRuntimeMaterialsForResource(
        RuntimeRenderResource resource,
        Shader shader,
        Material materialTemplate)
    {
        Material[] sourceMaterials = ResolveSourceMaterials(resource.templatePrefab, resource.animationAsset);
        int subMeshCount = Mathf.Max(1, resource.mesh.subMeshCount);
        int materialCount = Mathf.Max(1, Mathf.Min(subMeshCount, sourceMaterials.Length > 0 ? sourceMaterials.Length : subMeshCount));
        resource.runtimeMaterials = new Material[materialCount];

        for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
        {
            Material runtimeMaterial = CreateRuntimeMaterial(shader, materialTemplate);

            runtimeMaterial.name = $"{resource.animationAsset.name}_Indirect_{materialIndex:00}";
            runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
            runtimeMaterial.enableInstancing = true;

            Material sourceMaterial = sourceMaterials.Length > 0
                ? sourceMaterials[Mathf.Min(materialIndex, sourceMaterials.Length - 1)]
                : null;
            CopySourceMaterialProperties(sourceMaterial, runtimeMaterial);
            resource.runtimeMaterials[materialIndex] = runtimeMaterial;
        }

        return resource;
    }

    private static Material CreateRuntimeMaterial(Shader shader, Material materialTemplate)
    {
        Material runtimeMaterial = materialTemplate != null
            ? new Material(materialTemplate)
            : new Material(shader);

        if (runtimeMaterial.shader != shader)
            runtimeMaterial.shader = shader;

        return runtimeMaterial;
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
        _spawnDataCache = spawnData;
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
        _deathStateBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _deathStateBuffer.SetData(new uint[_instanceCount]);
        _aliveInstanceIndexBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _aliveInstanceCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _aliveInstanceCounterBuffer.SetData(AliveInstanceCounterResetData);
        _aliveInstanceDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _aliveInstanceDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);
        _physicsActiveStateBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _physicsActivationMetaBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());
        _physicsActiveStateBuffer.SetData(new uint[_instanceCount]);
        _physicsActivationMetaBuffer.SetData(new Vector4[_instanceCount]);
        _physicsActiveInstanceIndexBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _physicsActiveInstanceCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _physicsActiveInstanceCounterBuffer.SetData(AliveInstanceCounterResetData);
        _physicsActiveInstanceDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _physicsActiveInstanceDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);

        _combatStateBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<InstanceCombatStateData>());
        _combatStateReadbackCache = BuildInitialCombatStates(spawnData, simulationState);
        _combatStateBuffer.SetData(_combatStateReadbackCache);
        _targetAcquisitionStateBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<TargetAcquisitionStateData>());
        _targetAcquisitionStateBuffer.SetData(BuildInitialTargetAcquisitionStates(spawnData, simulationState));
        _targetAcquisitionCandidateBuffer = new ComputeBuffer(
            Mathf.Max(1, _instanceCount * TargetAcquisitionCandidateSlots),
            Marshal.SizeOf<TargetAcquisitionCandidateData>());
        _targetAcquisitionLosDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _targetAcquisitionLosDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);
        _squadAcquisitionDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _squadAcquisitionDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);
        _combatActiveStateBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _combatActiveStateBuffer.SetData(new uint[_instanceCount]);
        _combatActivationMetaBuffer = new ComputeBuffer(_instanceCount, Marshal.SizeOf<Vector4>());
        _combatActivationMetaBuffer.SetData(new Vector4[_instanceCount]);
        _combatActiveInstanceIndexBuffer = new ComputeBuffer(_instanceCount, sizeof(uint));
        _combatActiveInstanceCounterBuffer = new ComputeBuffer(2, sizeof(uint));
        _combatActiveInstanceCounterBuffer.SetData(CombatActiveInstanceCounterResetData);
        _combatActiveInstanceDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _combatActiveInstanceDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);
        EnsureCombatSquadCandidateBuffers();
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
        _gridCounterBuffer.SetData(new uint[_gridCellCount]);
        _gridTouchedCellBufferA = new ComputeBuffer(_gridCellCount, sizeof(uint));
        _gridTouchedCellBufferB = new ComputeBuffer(_gridCellCount, sizeof(uint));
        _gridPrevTouchedCellBuffer = _gridTouchedCellBufferA;
        _gridCurrTouchedCellBuffer = _gridTouchedCellBufferB;
        _gridTouchedCounterBufferA = new ComputeBuffer(1, sizeof(uint));
        _gridTouchedCounterBufferB = new ComputeBuffer(1, sizeof(uint));
        _gridTouchedCounterBufferA.SetData(AliveInstanceCounterResetData);
        _gridTouchedCounterBufferB.SetData(AliveInstanceCounterResetData);
        _gridPrevTouchedCounterBuffer = _gridTouchedCounterBufferA;
        _gridCurrTouchedCounterBuffer = _gridTouchedCounterBufferB;
        _gridClearDispatchArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
        _gridClearDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);
        _wakeGridCounterBuffer = new ComputeBuffer(_wakeGridCellCount, sizeof(uint));
        _wakeGridOccupantBuffer = new ComputeBuffer(_wakeGridCellCount * _maxCellOccupancy, sizeof(uint));
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

        _resolvedWakeGridCellSize = Mathf.Max(
            Mathf.Max(0.05f, _queryCellSize),
            Mathf.Max(_collisionRadius * 4.0f, _physicsWakeGridCellSize));
        _wakeGridDimensions = new Vector2Int(
            Mathf.Max(1, Mathf.CeilToInt(actualSize.x / _resolvedWakeGridCellSize)),
            Mathf.Max(1, Mathf.CeilToInt(actualSize.y / _resolvedWakeGridCellSize)));
        _wakeGridCellCount = Mathf.Max(1, _wakeGridDimensions.x * _wakeGridDimensions.y);
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
                flags = 0u,
                debugShotInfoPacked = 0u,
                debugShotTraceMeta = new Vector4(-1.0f, 0.0f, 0.0f, 0.0f)
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
        float lineOfSightRecheckInterval = Mathf.Max(0.0f, _targetAcquisitionLineOfSightRecheckInterval);

        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
        {
            Vector4 localPositionAndYaw = simulationState[instanceIndex].localPositionAndYaw;
            float searchInterval = Mathf.Lerp(
                searchIntervalMin,
                searchIntervalMax,
                ComputeStableRandom01(unchecked((uint)instanceIndex) ^ 0x5A17C0DEu));
            float initialSearchTimer = searchInterval * ComputeStableRandom01(unchecked((uint)instanceIndex) ^ 0xC0FFEE31u);
            float initialLineOfSightRecheckTimer = lineOfSightRecheckInterval *
                ComputeStableRandom01(unchecked((uint)instanceIndex) ^ 0x8E3779B9u);

            targetStates[instanceIndex] = new TargetAcquisitionStateData
            {
                lastKnownTargetAndScore = new Vector4(localPositionAndYaw.x, localPositionAndYaw.y, localPositionAndYaw.z, 0.0f),
                timers = new Vector4(initialSearchTimer, 0.0f, 0.0f, initialLineOfSightRecheckTimer),
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
            _visibleLod1IndirectArgsBuffers = Array.Empty<GraphicsBuffer>();
            _visibleLod2IndirectArgsBuffers = Array.Empty<GraphicsBuffer>();
            _indirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
            _visibleLod1IndirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
            _visibleLod2IndirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
            _visibleInstanceIndexCache = Array.Empty<uint>();
            _visibleLod1InstanceIndexCache = Array.Empty<uint>();
            _visibleLod2InstanceIndexCache = Array.Empty<uint>();
            _activeVisibleLodArgsBuffers = Array.Empty<GraphicsBuffer[]>();
            return;
        }

        BuildIndirectArgsBufferSet(_runtimeRenderResource, out _indirectArgsBuffers, out _indirectArgsCache);
        if (_hasSecondaryRuntimeRenderResource)
            BuildIndirectArgsBufferSet(_secondaryRuntimeRenderResource, out _visibleLod1IndirectArgsBuffers, out _visibleLod1IndirectArgsCache);
        else if (_hasTertiaryRuntimeRenderResource)
            BuildIndirectArgsBufferSet(_tertiaryRuntimeRenderResource, out _visibleLod1IndirectArgsBuffers, out _visibleLod1IndirectArgsCache);
        else
        {
            _visibleLod1IndirectArgsBuffers = Array.Empty<GraphicsBuffer>();
            _visibleLod1IndirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
        }

        if (_hasSecondaryRuntimeRenderResource && _hasTertiaryRuntimeRenderResource)
            BuildIndirectArgsBufferSet(_tertiaryRuntimeRenderResource, out _visibleLod2IndirectArgsBuffers, out _visibleLod2IndirectArgsCache);
        else
        {
            _visibleLod2IndirectArgsBuffers = Array.Empty<GraphicsBuffer>();
            _visibleLod2IndirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
        }

        _visibleInstanceIndexBuffer = new ComputeBuffer(Mathf.Max(1, _instanceCount), sizeof(uint));
        _visibleInstanceCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _visibleInstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        _visibleLod1InstanceIndexBuffer = new ComputeBuffer(Mathf.Max(1, _instanceCount), sizeof(uint));
        _visibleLod1InstanceCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _visibleLod1InstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        _visibleLod2InstanceIndexBuffer = new ComputeBuffer(Mathf.Max(1, _instanceCount), sizeof(uint));
        _visibleLod2InstanceCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _visibleLod2InstanceCounterBuffer.SetData(VisibleInstanceCounterResetData);
        _visibleInstanceIndexCache = new uint[Mathf.Max(1, _instanceCount)];
        _visibleLod1InstanceIndexCache = new uint[Mathf.Max(1, _instanceCount)];
        _visibleLod2InstanceIndexCache = new uint[Mathf.Max(1, _instanceCount)];
        _allInstanceIndicesCache = new uint[Mathf.Max(1, _instanceCount)];
        for (uint instanceIndex = 0; instanceIndex < _allInstanceIndicesCache.Length; instanceIndex++)
            _allInstanceIndicesCache[instanceIndex] = instanceIndex;

        List<GraphicsBuffer[]> activeArgs = new List<GraphicsBuffer[]>(3) { _indirectArgsBuffers };
        if (_visibleLod1IndirectArgsBuffers.Length > 0)
            activeArgs.Add(_visibleLod1IndirectArgsBuffers);
        if (_visibleLod2IndirectArgsBuffers.Length > 0)
            activeArgs.Add(_visibleLod2IndirectArgsBuffers);
        _activeVisibleLodArgsBuffers = activeArgs.ToArray();
    }

    private static void BuildIndirectArgsBufferSet(
        RuntimeRenderResource resource,
        out GraphicsBuffer[] argsBuffers,
        out GraphicsBuffer.IndirectDrawIndexedArgs[] argsCache)
    {
        int subMeshCount = resource.runtimeMaterials != null
            ? Mathf.Min(resource.mesh.subMeshCount, resource.runtimeMaterials.Length)
            : 0;
        argsBuffers = new GraphicsBuffer[subMeshCount];
        argsCache = new GraphicsBuffer.IndirectDrawIndexedArgs[subMeshCount];

        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            GraphicsBuffer argsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(resource.mesh, subMeshIndex, 0u);
            argsBuffer.SetData(new[] { args });
            argsBuffers[subMeshIndex] = argsBuffer;
            argsCache[subMeshIndex] = args;
        }
    }

    private void BuildCombatTracerResources()
    {
        ReleaseCombatTracerDrawResources();

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

        int tracerTierCount = Mathf.Clamp(_activeVisibleLodRenderResources != null ? _activeVisibleLodRenderResources.Length : 1, 1, 3);
        _combatTracerMaterials = new Material[tracerTierCount];
        _combatTracerArgsBuffers = new GraphicsBuffer[tracerTierCount];
        _combatTracerArgsCache = new GraphicsBuffer.IndirectDrawIndexedArgs[tracerTierCount];

        GraphicsBuffer.IndirectDrawIndexedArgs args = BuildIndirectArgs(_combatTracerMesh, 0, 0u);
        for (int tierIndex = 0; tierIndex < tracerTierCount; tierIndex++)
        {
            Material tracerMaterial = new Material(tracerShader);
            if (_combatTracerMaterialTemplate != null)
                tracerMaterial.CopyPropertiesFromMaterial(_combatTracerMaterialTemplate);
            tracerMaterial.name = $"{_runtimeRenderResource.animationAsset.name}_CombatTracer_Lod{tierIndex}";
            tracerMaterial.hideFlags = HideFlags.HideAndDontSave;
            tracerMaterial.enableInstancing = true;
            _combatTracerMaterials[tierIndex] = tracerMaterial;

            GraphicsBuffer argsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            argsBuffer.SetData(new[] { args });
            _combatTracerArgsBuffers[tierIndex] = argsBuffer;
            _combatTracerArgsCache[tierIndex] = args;
        }

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
        if (_activeVisibleLodRenderResources == null || _activeVisibleLodRenderResources.Length == 0)
            return;

        for (int tierIndex = 0; tierIndex < _activeVisibleLodRenderResources.Length; tierIndex++)
        {
            RuntimeRenderResource resource = _activeVisibleLodRenderResources[tierIndex];
            ComputeBuffer visibleInstanceIndexBuffer = ResolveVisibleInstanceIndexBufferForTier(tierIndex);
            if (resource.runtimeMaterials == null || visibleInstanceIndexBuffer == null)
                continue;

            Texture2D boneTexture = resource.animationAsset.BoneAnimationTexture;
            for (int materialIndex = 0; materialIndex < resource.runtimeMaterials.Length; materialIndex++)
            {
                Material runtimeMaterial = resource.runtimeMaterials[materialIndex];
                if (runtimeMaterial == null)
                    continue;

                runtimeMaterial.SetTexture(BoneAnimationTextureId, boneTexture);
                string gpuPassName = ResolveCrowdRenderSkinnedMeshPassName(tierIndex);
                SetGpuPassMaterialBuffer(runtimeMaterial, gpuPassName, InstanceTransformsId, "_InstanceTransforms", "instanceTransforms", _instanceTransformBuffer, GpuPassBindingAccess.Srv);
                SetGpuPassMaterialBuffer(runtimeMaterial, gpuPassName, InstanceFrameDataId, "_InstanceFrameData", "instanceFrameData", _instanceFrameDataBuffer, GpuPassBindingAccess.Srv);
                SetGpuPassMaterialBuffer(runtimeMaterial, gpuPassName, InstanceFrameBlendDataId, "_InstanceFrameBlendData", "instanceFrameBlendData", _instanceFrameBlendDataBuffer, GpuPassBindingAccess.Srv);
                SetGpuPassMaterialBuffer(runtimeMaterial, gpuPassName, VisibleInstanceIndicesId, "_VisibleInstanceIndices", $"visibleInstanceIndex.lod{tierIndex}", visibleInstanceIndexBuffer, GpuPassBindingAccess.Srv);
                runtimeMaterial.SetVector(CrowdRootLocalRow0Id, resource.crowdRootRow0);
                runtimeMaterial.SetVector(CrowdRootLocalRow1Id, resource.crowdRootRow1);
                runtimeMaterial.SetVector(CrowdRootLocalRow2Id, resource.crowdRootRow2);
            }
        }

        BindCombatTracerMaterial();
    }

    private void BindCombatTracerMaterial()
    {
        if (_combatTracerMaterials == null || _combatTracerMaterials.Length == 0)
            return;

        for (int tierIndex = 0; tierIndex < _combatTracerMaterials.Length; tierIndex++)
        {
            Material combatTracerMaterial = _combatTracerMaterials[tierIndex];
            ComputeBuffer visibleInstanceIndexBuffer = ResolveVisibleInstanceIndexBufferForTier(tierIndex);
            if (combatTracerMaterial == null || visibleInstanceIndexBuffer == null)
                continue;

            SetGpuPassMaterialBuffer(combatTracerMaterial, GpuPassRenderCombatTracer, SpawnDataId, "_SpawnData", $"spawnData.combatTracer.lod{tierIndex}", _spawnDataBuffer, GpuPassBindingAccess.Srv);
            SetGpuPassMaterialBuffer(combatTracerMaterial, GpuPassRenderCombatTracer, CombatStateBufferId, "_CombatStateBuffer", $"combatState.combatTracer.lod{tierIndex}", _combatStateBuffer, GpuPassBindingAccess.Srv);
            SetGpuPassMaterialBuffer(combatTracerMaterial, GpuPassRenderCombatTracer, VisibleInstanceIndicesId, "_VisibleInstanceIndices", $"visibleInstanceIndex.combatTracer.lod{tierIndex}", visibleInstanceIndexBuffer, GpuPassBindingAccess.Srv);
        }

        UpdateCombatTracerMaterialParameters();
    }

    private ComputeBuffer ResolveVisibleInstanceIndexBufferForTier(int tierIndex)
    {
        return tierIndex switch
        {
            0 => _visibleInstanceIndexBuffer,
            1 => _visibleLod1InstanceIndexBuffer,
            2 => _visibleLod2InstanceIndexBuffer,
            _ => null
        };
    }

    private void UpdateCombatTracerMaterialParameters()
    {
        if (_combatTracerMaterials == null || _combatTracerMaterials.Length == 0)
            return;

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        for (int materialIndex = 0; materialIndex < _combatTracerMaterials.Length; materialIndex++)
        {
            Material combatTracerMaterial = _combatTracerMaterials[materialIndex];
            if (combatTracerMaterial == null)
                continue;

            combatTracerMaterial.SetColor(TracerColorCampAId, _combatTracerColorCampA);
            combatTracerMaterial.SetColor(TracerColorCampBId, _combatTracerColorCampB);
            combatTracerMaterial.SetTexture(MuzzleFlashTexId, _combatMuzzleFlashTexture != null ? _combatMuzzleFlashTexture : Texture2D.blackTexture);
            combatTracerMaterial.SetTexture(TracerTexId, _combatTracerTexture != null ? _combatTracerTexture : Texture2D.whiteTexture);
            combatTracerMaterial.SetTexture(ImpactTexId, _combatImpactTexture != null ? _combatImpactTexture : Texture2D.blackTexture);
            combatTracerMaterial.SetFloat(TracerWidthId, Mathf.Max(0.001f, _combatTracerWidth));
            combatTracerMaterial.SetFloat(TracerBrightnessId, Mathf.Max(0.0f, _combatTracerBrightness));
            combatTracerMaterial.SetFloat(TracerMinFlashId, Mathf.Clamp01(_combatTracerMinFlash));
            combatTracerMaterial.SetFloat(CombatRangeId, Mathf.Max(0.1f, _combatRange));
            combatTracerMaterial.SetFloat(MuzzleFlashSizeId, Mathf.Max(0.001f, _combatMuzzleFlashSize));
            combatTracerMaterial.SetFloat(ImpactFlashSizeId, Mathf.Max(0.001f, _combatImpactFlashSize));
            combatTracerMaterial.SetFloat(ImpactPointOffsetId, Mathf.Max(0.0f, _combatImpactPointOffset));
            combatTracerMaterial.SetFloat(MuzzleFlashBrightnessId, Mathf.Max(0.0f, _combatMuzzleFlashBrightness));
            combatTracerMaterial.SetFloat(ImpactFlashBrightnessId, Mathf.Max(0.0f, _combatImpactFlashBrightness));
            combatTracerMaterial.SetVector(
                MuzzleFlashFlipbookId,
                new Vector4(
                    Mathf.Max(1, _combatMuzzleFlashFlipbookColumns),
                    Mathf.Max(1, _combatMuzzleFlashFlipbookRows),
                    0.0f,
                    0.0f));
            combatTracerMaterial.SetVector(
                ImpactFlashFlipbookId,
                new Vector4(
                    Mathf.Max(1, _combatImpactFlashFlipbookColumns),
                    Mathf.Max(1, _combatImpactFlashFlipbookRows),
                    0.0f,
                    0.0f));
            combatTracerMaterial.SetVector(RootPositionId, new Vector4(localToWorld.m03, localToWorld.m13, localToWorld.m23, 0.0f));
            combatTracerMaterial.SetVector(RootRightId, new Vector4(localToWorld.m00, localToWorld.m10, localToWorld.m20, 0.0f));
            combatTracerMaterial.SetVector(RootUpId, new Vector4(localToWorld.m01, localToWorld.m11, localToWorld.m21, 0.0f));
            combatTracerMaterial.SetVector(RootForwardId, new Vector4(localToWorld.m02, localToWorld.m12, localToWorld.m22, 0.0f));
        }
    }

}
