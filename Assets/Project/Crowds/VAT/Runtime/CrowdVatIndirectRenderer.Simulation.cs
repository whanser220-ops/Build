using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class CrowdVatIndirectRenderer : MonoBehaviour
{
    private void AdvancePlayback(float deltaTime)
    {
        if (!_hasClip)
            return;

        if (!_isPlaying)
            return;

        deltaTime = Mathf.Max(0.0f, deltaTime);
        _playbackTime += deltaTime;
        AdvanceInstanceAnimationStates(deltaTime);
    }

    private void UpdateGpuBuffers()
    {
        if (!_hasClip || !HasSimulationReadWriteBuffers())
            return;

        RefreshRuntimeTargets();
        EnsureRuntimeSquadBuffers();
        UploadAnimationClipMetadata();
        UploadInstanceAnimationStates();

        float deltaTime = Mathf.Max(Time.deltaTime, 1e-4f);
        int interactionSphereCount = UploadInteractionSpheres();
        UploadRuntimeSquadStates();
        UploadRuntimeSquadAliveCountsIfDirty();
        UploadRuntimeAgentSquadDataIfDirty();
        UploadRuntimeFormationSlotsIfDirty();
        UploadAgentDataLayoutIfDirty();
        ConfigureCommonComputeParameters(deltaTime, interactionSphereCount);
        int spatialQueryCount = UploadSpatialQueries();
        bool needsCombatGrid = _enableGpuInstanceCombat;
        bool needsSpatialQueryGrid = spatialQueryCount > 0;
        bool usesCrowdApproximation = _enableApproximateCollision && _instanceCount > 1;
        bool usesLocalAvoidance = usesCrowdApproximation && _enableLocalAvoidance;
        RebuildAliveInstanceCompactionData();
        bool captureAiDebugGpuStages = _aiDebugGpuStageCaptureActive;
        if (captureAiDebugGpuStages)
        {
            BeginAiDebugGpuFrameCapture();
            captureAiDebugGpuStages = _aiDebugGpuStageCaptureActive;
        }

        if (usesLocalAvoidance)
        {
            BindClearGridKernel();
            DispatchGrid(_clearGridKernel);

            _updateCompute.SetInt(GridBuildModeId, GridBuildModeAllQueryables);
            BindBuildGridKernel(useActiveInstanceList: false);
            DispatchAliveInstancesIndirect(_buildGridKernel);
        }

        BindPredictKernel();
        DispatchAliveInstancesIndirect(_predictKernel);
        SwapSimulationBuffers();
        if (captureAiDebugGpuStages)
            CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.PredictAfter, -1);

        bool usesEnvironmentPbd = _hasResolvedEnvironmentDistanceField || _hasResolvedTerrainCollision || _hasResolvedStaticSdfCollision;
        int solverPassCount = usesCrowdApproximation || usesEnvironmentPbd ? Mathf.Max(1, _solverIterations) : 0;

        for (int iterationIndex = 0; iterationIndex < solverPassCount; iterationIndex++)
        {
            BindClearGridKernel();
            DispatchGrid(_clearGridKernel);

            BindBuildGridKernel(useActiveInstanceList: false);
            DispatchAliveInstancesIndirect(_buildGridKernel);

            BindSolveKernel();
            DispatchAliveInstancesIndirect(_solveCrowdKernel);
            SwapSimulationBuffers();
            if (captureAiDebugGpuStages)
                CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.SolveAfter, iterationIndex);
        }

        if (needsCombatGrid || needsSpatialQueryGrid)
        {
            BindClearGridKernel();
            DispatchGrid(_clearGridKernel);

            _updateCompute.SetInt(GridBuildModeId, GridBuildModeAllQueryables);
            BindBuildGridKernel(useActiveInstanceList: false);
            DispatchAliveInstancesIndirect(_buildGridKernel);
            if (captureAiDebugGpuStages)
                CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.GridAfter, -1);

            if (needsSpatialQueryGrid)
            {
                BindBuildSpatialElementsKernel();
                DispatchAllInstances(_buildSpatialElementsKernel);
            }

            if (needsCombatGrid)
            {
                BindResolveTargetAcquisitionKernel(useActiveInstanceList: false);
                DispatchAliveInstancesIndirect(_resolveTargetAcquisitionKernel);
                if (captureAiDebugGpuStages)
                    CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.TargetAcquisitionAfter, -1);

                BindResolveInstanceCombatKernel(useActiveInstanceList: false);
                DispatchAliveInstancesIndirect(_resolveInstanceCombatKernel);
                if (captureAiDebugGpuStages)
                    CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.CombatAfter, -1);
            }
        }

        if (needsSpatialQueryGrid)
        {
            BindClearSpatialQueriesKernel();
            DispatchSpatialQueries(_clearSpatialQueriesKernel, spatialQueryCount);

            BindResolveSpatialQueriesKernel();
            DispatchSpatialQueries(_resolveSpatialQueriesKernel, spatialQueryCount);
            if (captureAiDebugGpuStages)
                CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.SpatialQueryAfter, -1);
        }

        _updateCompute.SetInt(GridBuildModeId, GridBuildModeActiveOnly);

        BindFinalizeKernel();
        DispatchAliveInstancesIndirect(_finalizeKernel);
        if (captureAiDebugGpuStages)
            CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.FinalizeAfter, -1);

        if (_aiDebugGpuDiscoveryActive)
            CaptureAiDebugGpuCandidates();
    }

    private void AdvanceInstanceAnimationStates(float deltaTime)
    {
        if (_instanceAnimationStateCpuCache == null || _instanceAnimationStateCpuCache.Length == 0)
            return;

        CrowdVatAnimationAsset animationAsset = ResolveConfiguredAnimationAsset();
        if (animationAsset == null)
            return;

        float basePlaybackSpeed = Mathf.Max(0.0f, _playbackSpeed);
        if (basePlaybackSpeed <= Mathf.Epsilon || deltaTime <= Mathf.Epsilon)
            return;

        for (int instanceIndex = 0; instanceIndex < _instanceAnimationStateCpuCache.Length; instanceIndex++)
        {
            InstanceAnimationStateCpuData state = _instanceAnimationStateCpuCache[instanceIndex];
            if (!animationAsset.TryGetClip(state.currentClipIndex, out CrowdVatAnimationAsset.ClipInfo currentClip))
                continue;

            float effectiveDeltaTime = deltaTime * basePlaybackSpeed * Mathf.Max(0.0f, state.playbackSpeedMultiplier);
            state.currentClipTime = AdvanceClipTime(currentClip, state.currentClipTime, effectiveDeltaTime);

            if (state.isBlending &&
                animationAsset.TryGetClip(state.nextClipIndex, out CrowdVatAnimationAsset.ClipInfo nextClip))
            {
                state.nextClipTime = AdvanceClipTime(nextClip, state.nextClipTime, effectiveDeltaTime);
                state.transitionDuration = Mathf.Max(0.0f, state.transitionDuration);
                state.transitionElapsed += deltaTime;

                if (state.transitionDuration <= Mathf.Epsilon ||
                    state.transitionElapsed >= state.transitionDuration)
                {
                    state.currentClipIndex = state.nextClipIndex;
                    state.currentClipTime = state.nextClipTime;
                    state.isBlending = false;
                    state.transitionElapsed = 0.0f;
                    state.transitionDuration = 0.0f;
                }
            }

            if (!state.isBlending)
            {
                state.nextClipIndex = state.currentClipIndex;
                state.nextClipTime = state.currentClipTime;
                state.transitionElapsed = 0.0f;
                state.transitionDuration = 0.0f;
            }

            _instanceAnimationStateCpuCache[instanceIndex] = state;
        }
    }

    private float AdvanceClipTime(CrowdVatAnimationAsset.ClipInfo clip, float clipTime, float deltaTime)
    {
        float clipLength = Mathf.Max(clip.LengthSeconds, 0.0f);
        if (clipLength <= Mathf.Epsilon || deltaTime <= Mathf.Epsilon)
            return Mathf.Clamp(clipTime, 0.0f, clipLength);

        float advancedTime = clipTime + deltaTime;
        if (ResolveClipLoop(clip))
            return Mathf.Repeat(advancedTime, clipLength);

        return Mathf.Clamp(advancedTime, 0.0f, clipLength);
    }

    private void UploadAnimationClipMetadata()
    {
        if (_animationClipMetadataBuffer == null)
            return;

        CrowdVatAnimationAsset animationAsset = ResolveConfiguredAnimationAsset();
        BuildAnimationClipGpuCache(animationAsset);
        if (_animationClipGpuCache == null || _animationClipGpuCache.Length == 0)
            return;

        if (_animationClipMetadataBuffer.count != _animationClipGpuCache.Length)
            return;

        _animationClipMetadataBuffer.SetData(_animationClipGpuCache);
    }

    private void UploadInstanceAnimationStates()
    {
        if (_instanceAnimationStateBuffer == null)
            return;

        SyncInstanceAnimationStateGpuCache();
        if (_instanceAnimationStateGpuCache == null || _instanceAnimationStateGpuCache.Length == 0)
            return;

        if (_instanceAnimationStateBuffer.count != _instanceAnimationStateGpuCache.Length)
            return;

        _instanceAnimationStateBuffer.SetData(_instanceAnimationStateGpuCache);
    }

    private void RebuildAliveInstanceCompactionData()
    {
        if (_aliveInstanceCounterBuffer == null || _aliveInstanceDispatchArgsBuffer == null)
            return;

        _aliveInstanceCounterBuffer.SetData(AliveInstanceCounterResetData);
        _aliveInstanceDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);

        BindBuildAliveInstanceListKernel();
        DispatchAllInstances(_buildAliveInstanceListKernel);

        BindBuildAliveDispatchArgsKernel();
        _updateCompute.Dispatch(_buildAliveDispatchArgsKernel, 1, 1, 1);
    }

    private void RebuildActiveInstanceCompactionData()
    {
        if (_activeInstanceCounterBuffer == null || _activeInstanceDispatchArgsBuffer == null)
            return;

        _activeInstanceCounterBuffer.SetData(ActiveInstanceCounterResetData);
        _activeInstanceDispatchArgsBuffer.SetData(ActiveInstanceDispatchArgsResetData);

        BindBuildActiveInstanceListKernel();
        DispatchAliveInstancesIndirect(_buildActiveInstanceListKernel);

        BindBuildActiveDispatchArgsKernel();
        _updateCompute.Dispatch(_buildActiveDispatchArgsKernel, 1, 1, 1);
    }

    private void ConfigureCommonComputeParameters(float deltaTime, int interactionSphereCount)
    {
        Vector3 activeBubbleLocalCenter = Vector3.zero;
        bool useActiveBubble = _enableActiveBubble && TryGetActiveBubbleLocalCenter(out activeBubbleLocalCenter);
        bool hasTerrain = TryResolveSceneQueryTerrain(out Terrain terrain) &&
            terrain != null &&
            terrain.terrainData != null &&
            terrain.terrainData.heightmapTexture != null;
        bool hasBakedEnvironmentDistanceField = TryResolveSceneQueryBakedEnvironmentDistanceField(out CrowdVatBakedEnvironmentDistanceFieldDescriptor bakedEnvironmentField);
        bool useEnvironmentDistanceField = hasBakedEnvironmentDistanceField;
        // 人物贴地/碰撞主链只走规则 SDF，避免重新引入 GPU Terrain heightmap 贴地。
        bool useTerrainCollision = false;
        CrowdVatObstacleDistanceFieldDescriptor staticSdfField = default;
        bool hasStaticSdfField = TryResolveSceneQueryObstacleDistanceField(out staticSdfField);
        bool useStaticSdfCollision = _enableStaticSdfCollision && hasStaticSdfField;

        _resolvedTerrainHeightmap = hasTerrain
            ? terrain.terrainData.heightmapTexture
            : Texture2D.blackTexture;
        _resolvedStaticSdfTexture = useStaticSdfCollision
            ? staticSdfField.sdfTexture
            : GetFallbackStaticSdfTexture();
        _resolvedEnvironmentDistanceFieldTexture = useEnvironmentDistanceField
            ? bakedEnvironmentField.sdfTexture
            : GetFallbackStaticSdfTexture();
        _hasResolvedEnvironmentDistanceField = useEnvironmentDistanceField;
        _hasResolvedTerrainCollision = useTerrainCollision;
        _hasResolvedStaticSdfCollision = useStaticSdfCollision;

        _updateCompute.SetInt(InstanceCountId, _instanceCount);
        _updateCompute.SetInt(AnimationClipCountId, _animationClipGpuCache != null ? _animationClipGpuCache.Length : 0);
        _updateCompute.SetFloat(PlaybackTimeId, _playbackTime);
        _updateCompute.SetFloat(BasePlaybackSpeedId, Mathf.Max(0.0f, _playbackSpeed));
        _updateCompute.SetInt(ClipStartFrameId, _currentClip.StartFrame);
        _updateCompute.SetInt(ClipFrameCountId, _currentClip.FrameCount);
        _updateCompute.SetFloat(ClipLengthId, Mathf.Max(_currentClip.LengthSeconds, 0.0f));
        _updateCompute.SetInt(ClipLoopId, (_loopOverride || _currentClip.Loop) ? 1 : 0);
        _updateCompute.SetFloat(DeltaTimeId, deltaTime);
        _updateCompute.SetInt(EnableApproximateCollisionId, _enableApproximateCollision ? 1 : 0);
        _updateCompute.SetInt(UseActiveBubbleId, useActiveBubble ? 1 : 0);
        _updateCompute.SetInt(InteractionSphereCountId, interactionSphereCount);
        _updateCompute.SetVector(ActiveBubbleCenterId, new Vector4(activeBubbleLocalCenter.x, activeBubbleLocalCenter.y, activeBubbleLocalCenter.z, 0.0f));
        _updateCompute.SetFloat(ActiveBubbleRadiusId, _activeBubbleRadius);
        _updateCompute.SetFloat(ActiveBubbleRetentionRadiusId, _activeBubbleRetentionRadius);
        _updateCompute.SetInts(GridDimId, _gridDimensions.x, _gridDimensions.y);
        _updateCompute.SetInt(GridCellCountId, _gridCellCount);
        _updateCompute.SetInt(MaxCellOccupancyId, _maxCellOccupancy);
        _updateCompute.SetFloat(CellSizeId, _queryCellSize);
        _updateCompute.SetFloat(InvCellSizeId, 1.0f / Mathf.Max(_queryCellSize, 1e-4f));
        _updateCompute.SetVector(GridMinXZId, _gridMinXZ);
        _updateCompute.SetVector(GridMaxXZId, _gridMaxXZ);
        _updateCompute.SetFloat(CollisionRadiusId, _collisionRadius);
        _updateCompute.SetFloat(CapsuleHeightId, _collisionHeight);
        _updateCompute.SetInt(CapsulePbdSampleCountId, _capsulePbdSampleCount);
        _updateCompute.SetFloat(MaxSpatialQueryElementRadiusId, Mathf.Max(_maximumQueryAgentRadius, _collisionRadius));
        _updateCompute.SetFloat(AnchorStiffnessId, 0.0f);
        _updateCompute.SetFloat(VelocityDampingId, _velocityDamping);
        _updateCompute.SetFloat(GoalStiffnessId, _goalStiffness);
        _updateCompute.SetInt(EnableNavigationPotentialFieldId, 0);
        _updateCompute.SetFloat(NavigationPotentialProbeDistanceId, 0.0f);
        _updateCompute.SetFloat(NavigationDensityLateralStrengthId, 0.0f);
        _updateCompute.SetFloat(NavigationDensitySlowdownStrengthId, 0.0f);
        _updateCompute.SetInt(NavigationDensityReferenceOccupancyId, 1);
        _updateCompute.SetInt(EnableLocalAvoidanceId, _enableLocalAvoidance ? 1 : 0);
        _updateCompute.SetFloat(LocalAvoidanceRadiusId, Mathf.Max(0.05f, _localAvoidanceRadius));
        _updateCompute.SetFloat(LocalAvoidanceStrengthId, _localAvoidanceStrength);
        _updateCompute.SetFloat(DensitySlowdownStrengthId, 0.0f);
        _updateCompute.SetInt(DensityReferenceNeighborCountId, 1);
        _updateCompute.SetInt(LocalAvoidanceMaxNeighborsId, Mathf.Max(1, _localAvoidanceMaxNeighbors));
        _updateCompute.SetFloat(SelfCollisionStrengthId, _selfCollisionStrength);
        _updateCompute.SetFloat(SelfCollisionSlopId, Mathf.Max(0.0f, _selfCollisionSlop));
        _updateCompute.SetFloat(SelfCollisionMaxPushPerStepId, _selfCollisionMaxPushPerStep);
        _updateCompute.SetInt(EnableDensityConstraintId, 0);
        _updateCompute.SetFloat(DensityComfortDistanceId, 0.0f);
        _updateCompute.SetFloat(DensityConstraintStiffnessId, 0.0f);
        _updateCompute.SetFloat(MaxPushPerStepId, _maxPushPerStep);
        _updateCompute.SetFloat(MaxDisplacementFromSpawnId, _maxDisplacementFromSpawn);
        _updateCompute.SetFloat(InactiveReturnStrengthId, _inactiveReturnStrength);
        _updateCompute.SetInt(EnableEnvironmentDistanceFieldId, useEnvironmentDistanceField ? 1 : 0);
        _updateCompute.SetInt(EnableTerrainCollisionId, useTerrainCollision ? 1 : 0);
        _updateCompute.SetFloat(TerrainHeightOffsetId, _terrainHeightOffset);
        _updateCompute.SetInt(EnableStaticSdfCollisionId, useStaticSdfCollision ? 1 : 0);
        _updateCompute.SetInt(EnableGpuInstanceCombatId, _enableGpuInstanceCombat ? 1 : 0);
        _updateCompute.SetFloat(CombatRangeId, _combatRange);
        _updateCompute.SetFloat(CombatShotsPerSecondId, _combatShotsPerSecond);
        _updateCompute.SetFloat(CombatShotIntervalJitterId, _combatShotIntervalJitter);
        _updateCompute.SetFloat(CombatShotSpreadDegreesId, Mathf.Max(0.0f, _combatShotSpreadDegrees));
        _updateCompute.SetFloat(CombatHitRadiusPaddingId, Mathf.Max(0.0f, _combatHitRadiusPadding));
        _updateCompute.SetFloat(CombatHitHeightPaddingId, Mathf.Max(0.0f, _combatHitHeightPadding));
        _updateCompute.SetInt(EnableCombatTerrainOcclusionId, 0);
        _updateCompute.SetFloat(CombatTerrainOcclusionSampleSpacingId, _combatTerrainOcclusionSampleSpacing);
        _updateCompute.SetFloat(CombatTerrainOcclusionClearanceId, _combatTerrainOcclusionClearance);
        _updateCompute.SetFloat(CombatOriginHeightId, _combatOriginHeight);
        _updateCompute.SetFloat(CombatTargetHeightId, ResolveCombatTargetHeight());
        _updateCompute.SetFloat(CombatMuzzleFlashDecayId, _combatMuzzleFlashDecay);
        _updateCompute.SetFloat(CombatImpactFlashDurationId, Mathf.Max(0.01f, _combatImpactFlashDuration));
        _updateCompute.SetFloat(CombatMaxHealthId, Mathf.Max(1.0f, _combatMaxHealth));
        _updateCompute.SetFloat(CombatDamagePerHitId, Mathf.Max(0.0f, _combatDamagePerHit));
        _updateCompute.SetFloat(CombatHitFlashDecayId, Mathf.Max(0.0f, _combatHitFlashDecay));
        _updateCompute.SetFloat(CombatOccludedTargetRetryDelayId, _combatOccludedTargetRetryDelay);
        _updateCompute.SetFloat(CombatNoTargetRetryDelayId, _combatNoTargetRetryDelay);
        _updateCompute.SetFloat(TargetAcquisitionSearchIntervalMinId, Mathf.Max(0.02f, _targetAcquisitionSearchIntervalMin));
        _updateCompute.SetFloat(TargetAcquisitionSearchIntervalMaxId, Mathf.Max(Mathf.Max(0.02f, _targetAcquisitionSearchIntervalMin), _targetAcquisitionSearchIntervalMax));
        _updateCompute.SetFloat(TargetAcquisitionFarDistanceId, Mathf.Max(0.1f, _targetAcquisitionFarDistance));
        _updateCompute.SetFloat(TargetAcquisitionFarIntervalMultiplierId, Mathf.Max(1.0f, _targetAcquisitionFarIntervalMultiplier));
        _updateCompute.SetFloat(TargetAcquisitionFovCosineId, Mathf.Cos(Mathf.Clamp(_targetAcquisitionFovDegrees, 1.0f, 360.0f) * 0.5f * Mathf.Deg2Rad));
        _updateCompute.SetInt(TargetAcquisitionMaxCandidateChecksId, Mathf.Max(1, _targetAcquisitionMaxCandidateChecks));
        _updateCompute.SetFloat(TargetAcquisitionCurrentTargetBonusId, Mathf.Max(0.0f, _targetAcquisitionCurrentTargetBonus));
        _updateCompute.SetFloat(TargetAcquisitionLastAttackerBonusId, Mathf.Max(0.0f, _targetAcquisitionLastAttackerBonus));
        _updateCompute.SetFloat(TargetAcquisitionLockDurationId, Mathf.Max(0.0f, _targetAcquisitionLockDuration));
        _updateCompute.SetFloat(TargetAcquisitionLostSightGraceId, Mathf.Max(0.0f, _targetAcquisitionLostSightGrace));
        _updateCompute.SetFloat(TargetAcquisitionDistanceScoreWeightId, Mathf.Max(0.0f, _targetAcquisitionDistanceScoreWeight));
        _updateCompute.SetFloat(TargetAcquisitionViewScoreWeightId, Mathf.Max(0.0f, _targetAcquisitionViewScoreWeight));
        _updateCompute.SetVector(EnvironmentDistanceWorldCenterId, hasBakedEnvironmentDistanceField ? bakedEnvironmentField.worldCenter : Vector3.zero);
        _updateCompute.SetVector(EnvironmentDistanceWorldSizeId, hasBakedEnvironmentDistanceField ? bakedEnvironmentField.worldSize : Vector3.one);
        _updateCompute.SetFloat(EnvironmentDistanceDistanceScaleId, hasBakedEnvironmentDistanceField ? bakedEnvironmentField.distanceScale : 1.0f);
        _updateCompute.SetFloat(EnvironmentDistanceDistanceBiasId, hasBakedEnvironmentDistanceField ? bakedEnvironmentField.distanceBias : 0.0f);
        _updateCompute.SetVector(StaticSdfWorldCenterId, hasStaticSdfField ? staticSdfField.worldCenter : Vector3.zero);
        _updateCompute.SetVector(StaticSdfWorldSizeId, hasStaticSdfField ? staticSdfField.worldSize : Vector3.one);
        _updateCompute.SetFloat(StaticSdfDistanceScaleId, hasStaticSdfField ? staticSdfField.distanceScale : 1.0f);
        _updateCompute.SetFloat(StaticSdfDistanceBiasId, hasStaticSdfField ? staticSdfField.distanceBias : 0.0f);
        _updateCompute.SetInt(GridBuildModeId, GridBuildModeActiveOnly);
        _updateCompute.SetInt(SpatialQueryCountId, _activeSpatialQueryCount);
        _updateCompute.SetInt(MaxSpatialQueryHitsId, _maxHitsPerSpatialQuery);
        _updateCompute.SetInt(EnableRuntimeSquadAnchorsId, HasRuntimeSquadAnchorData() ? 1 : 0);
        _updateCompute.SetInt(UseRuntimeFormationSlotsId, _activeFormationSlotCount > 0 ? 1 : 0);
        _updateCompute.SetInt(SquadStateCountId, _activeSquadStateCount);
        _updateCompute.SetInt(AgentSquadDataCountId, _activeAgentSquadDataCount);
        _updateCompute.SetInt(FormationSlotCountId, _activeFormationSlotCount);

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        _updateCompute.SetVector(RootPositionId, new Vector4(localToWorld.m03, localToWorld.m13, localToWorld.m23, 0.0f));
        _updateCompute.SetVector(RootRightId, new Vector4(localToWorld.m00, localToWorld.m10, localToWorld.m20, 0.0f));
        _updateCompute.SetVector(RootUpId, new Vector4(localToWorld.m01, localToWorld.m11, localToWorld.m21, 0.0f));
        _updateCompute.SetVector(RootForwardId, new Vector4(localToWorld.m02, localToWorld.m12, localToWorld.m22, 0.0f));
        _updateCompute.SetVector(WorldToLocalRow0Id, new Vector4(worldToLocal.m00, worldToLocal.m01, worldToLocal.m02, worldToLocal.m03));
        _updateCompute.SetVector(WorldToLocalRow1Id, new Vector4(worldToLocal.m10, worldToLocal.m11, worldToLocal.m12, worldToLocal.m13));
        _updateCompute.SetVector(WorldToLocalRow2Id, new Vector4(worldToLocal.m20, worldToLocal.m21, worldToLocal.m22, worldToLocal.m23));

        if (useTerrainCollision)
        {
            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            _updateCompute.SetVector(TerrainPositionId, new Vector4(terrainPosition.x, terrainPosition.y, terrainPosition.z, 0.0f));
            _updateCompute.SetVector(TerrainSizeId, new Vector4(terrainSize.x, terrainSize.y, terrainSize.z, 0.0f));
        }
        else
        {
            if (hasTerrain)
            {
                Vector3 terrainPosition = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData.size;
                _updateCompute.SetVector(TerrainPositionId, new Vector4(terrainPosition.x, terrainPosition.y, terrainPosition.z, 0.0f));
                _updateCompute.SetVector(TerrainSizeId, new Vector4(terrainSize.x, terrainSize.y, terrainSize.z, 0.0f));
            }
            else
            {
                _updateCompute.SetVector(TerrainPositionId, Vector4.zero);
                _updateCompute.SetVector(TerrainSizeId, Vector4.zero);
            }
        }
    }

    private void BindPredictKernel()
    {
        _updateCompute.SetBuffer(_predictKernel, SpawnDataId, _spawnDataBuffer);
        BindSimulationReadBuffers(_predictKernel);
        BindSimulationWriteBuffers(_predictKernel);
        _updateCompute.SetBuffer(_predictKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_predictKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_predictKernel, InteractionSphereBufferId, _interactionSphereBuffer);
        _updateCompute.SetBuffer(_predictKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_predictKernel, GridOccupantBufferId, _gridOccupantBuffer);
        BindAliveInstanceListBuffers(_predictKernel);
        BindRuntimeSquadBuffers(_predictKernel);
        _updateCompute.SetTexture(_predictKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_predictKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_predictKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindBuildAliveInstanceListKernel()
    {
        _updateCompute.SetBuffer(_buildAliveInstanceListKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_buildAliveInstanceListKernel, AliveInstanceIndexBufferId, _aliveInstanceIndexBuffer);
        _updateCompute.SetBuffer(_buildAliveInstanceListKernel, AliveInstanceCounterBufferId, _aliveInstanceCounterBuffer);
    }

    private void BindBuildAliveDispatchArgsKernel()
    {
        _updateCompute.SetBuffer(_buildAliveDispatchArgsKernel, AliveInstanceCounterBufferId, _aliveInstanceCounterBuffer);
        _updateCompute.SetBuffer(_buildAliveDispatchArgsKernel, AliveInstanceDispatchArgsBufferId, _aliveInstanceDispatchArgsBuffer);
    }

    private void BindBuildActiveInstanceListKernel()
    {
        _updateCompute.SetBuffer(_buildActiveInstanceListKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_buildActiveInstanceListKernel, DeathStateBufferId, _deathStateBuffer);
        BindAliveInstanceListBuffers(_buildActiveInstanceListKernel);
        _updateCompute.SetBuffer(_buildActiveInstanceListKernel, ActiveInstanceIndexBufferId, _activeInstanceIndexBuffer);
        _updateCompute.SetBuffer(_buildActiveInstanceListKernel, ActiveInstanceCounterBufferId, _activeInstanceCounterBuffer);
    }

    private void BindBuildActiveDispatchArgsKernel()
    {
        _updateCompute.SetBuffer(_buildActiveDispatchArgsKernel, ActiveInstanceCounterBufferId, _activeInstanceCounterBuffer);
        _updateCompute.SetBuffer(_buildActiveDispatchArgsKernel, ActiveInstanceDispatchArgsBufferId, _activeInstanceDispatchArgsBuffer);
    }

    private void BindClearGridKernel()
    {
        _updateCompute.SetBuffer(_clearGridKernel, GridCounterBufferId, _gridCounterBuffer);
    }

    private void BindBuildGridKernel(bool useActiveInstanceList)
    {
        BindSimulationReadBuffers(_buildGridKernel);
        _updateCompute.SetBuffer(_buildGridKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_buildGridKernel, GridOccupantBufferId, _gridOccupantBuffer);
        if (useActiveInstanceList)
            BindActiveInstanceListBuffers(_buildGridKernel);
        else
            BindAliveInstanceListBuffers(_buildGridKernel);
    }

    private void BindBuildSpatialElementsKernel()
    {
        _updateCompute.SetBuffer(_buildSpatialElementsKernel, SpawnDataId, _spawnDataBuffer);
        BindSimulationReadBuffers(_buildSpatialElementsKernel);
        _updateCompute.SetBuffer(_buildSpatialElementsKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_buildSpatialElementsKernel, DeathStateBufferId, _deathStateBuffer);
        BindSpatialElementBuffers(_buildSpatialElementsKernel);
    }

    private void BindSolveKernel()
    {
        _updateCompute.SetBuffer(_solveCrowdKernel, SpawnDataId, _spawnDataBuffer);
        BindSimulationReadBuffers(_solveCrowdKernel);
        BindSimulationWriteBuffers(_solveCrowdKernel);
        _updateCompute.SetBuffer(_solveCrowdKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_solveCrowdKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_solveCrowdKernel, GridOccupantBufferId, _gridOccupantBuffer);
        BindAliveInstanceListBuffers(_solveCrowdKernel);
        BindRuntimeSquadBuffers(_solveCrowdKernel);
        _updateCompute.SetTexture(_solveCrowdKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_solveCrowdKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_solveCrowdKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindResolveTargetAcquisitionKernel(bool useActiveInstanceList)
    {
        _updateCompute.SetBuffer(_resolveTargetAcquisitionKernel, SpawnDataId, _spawnDataBuffer);
        BindSimulationReadBuffers(_resolveTargetAcquisitionKernel);
        _updateCompute.SetBuffer(_resolveTargetAcquisitionKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_resolveTargetAcquisitionKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_resolveTargetAcquisitionKernel, GridOccupantBufferId, _gridOccupantBuffer);
        _updateCompute.SetBuffer(_resolveTargetAcquisitionKernel, TargetAcquisitionStateBufferId, _targetAcquisitionStateBuffer);
        if (useActiveInstanceList)
            BindActiveInstanceListBuffers(_resolveTargetAcquisitionKernel);
        else
            BindAliveInstanceListBuffers(_resolveTargetAcquisitionKernel);
        _updateCompute.SetTexture(_resolveTargetAcquisitionKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_resolveTargetAcquisitionKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_resolveTargetAcquisitionKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindResolveInstanceCombatKernel(bool useActiveInstanceList)
    {
        _updateCompute.SetBuffer(_resolveInstanceCombatKernel, SpawnDataId, _spawnDataBuffer);
        BindSimulationReadBuffers(_resolveInstanceCombatKernel);
        _updateCompute.SetBuffer(_resolveInstanceCombatKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_resolveInstanceCombatKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_resolveInstanceCombatKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_resolveInstanceCombatKernel, GridOccupantBufferId, _gridOccupantBuffer);
        _updateCompute.SetBuffer(_resolveInstanceCombatKernel, CombatStateBufferId, _combatStateBuffer);
        _updateCompute.SetBuffer(_resolveInstanceCombatKernel, TargetAcquisitionStateBufferId, _targetAcquisitionStateBuffer);
        _updateCompute.SetBuffer(_resolveInstanceCombatKernel, SquadAliveCountBufferId, _squadAliveCountBuffer);
        if (useActiveInstanceList)
            BindActiveInstanceListBuffers(_resolveInstanceCombatKernel);
        else
            BindAliveInstanceListBuffers(_resolveInstanceCombatKernel);
        BindRuntimeSquadBuffers(_resolveInstanceCombatKernel);
        _updateCompute.SetTexture(_resolveInstanceCombatKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_resolveInstanceCombatKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_resolveInstanceCombatKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindAliveInstanceListBuffers(int kernel)
    {
        _updateCompute.SetBuffer(kernel, AliveInstanceIndexReadBufferId, _aliveInstanceIndexBuffer);
        _updateCompute.SetBuffer(kernel, AliveInstanceCounterReadBufferId, _aliveInstanceCounterBuffer);
    }

    private bool HasSimulationReadBuffers()
    {
        return _simulationPositionYawReadBuffer != null &&
            _simulationScaleReadBuffer != null &&
            _simulationVelocityReadBuffer != null;
    }

    private bool HasSimulationWriteBuffers()
    {
        return _simulationPositionYawWriteBuffer != null &&
            _simulationScaleWriteBuffer != null &&
            _simulationVelocityWriteBuffer != null;
    }

    private bool HasSimulationReadWriteBuffers()
    {
        return HasSimulationReadBuffers() && HasSimulationWriteBuffers();
    }

    private void BindSimulationReadBuffers(int kernel)
    {
        _updateCompute.SetBuffer(kernel, SimulationPositionYawReadBufferId, _simulationPositionYawReadBuffer);
        _updateCompute.SetBuffer(kernel, SimulationScaleReadBufferId, _simulationScaleReadBuffer);
        _updateCompute.SetBuffer(kernel, SimulationVelocityReadBufferId, _simulationVelocityReadBuffer);
    }

    private void BindSimulationWriteBuffers(int kernel)
    {
        _updateCompute.SetBuffer(kernel, SimulationPositionYawWriteBufferId, _simulationPositionYawWriteBuffer);
        _updateCompute.SetBuffer(kernel, SimulationScaleWriteBufferId, _simulationScaleWriteBuffer);
        _updateCompute.SetBuffer(kernel, SimulationVelocityWriteBufferId, _simulationVelocityWriteBuffer);
    }

    private void BindSpatialElementBuffers(int kernel)
    {
        _updateCompute.SetBuffer(kernel, SpatialCapsuleStartRadiusBufferId, _spatialCapsuleStartRadiusBuffer);
        _updateCompute.SetBuffer(kernel, SpatialCapsuleEndHeightBufferId, _spatialCapsuleEndHeightBuffer);
        _updateCompute.SetBuffer(kernel, SpatialOwnerIndexBufferId, _spatialOwnerIndexBuffer);
        _updateCompute.SetBuffer(kernel, SpatialTargetMaskBufferId, _spatialTargetMaskBuffer);
        _updateCompute.SetBuffer(kernel, SpatialFlagsBufferId, _spatialFlagsBuffer);
        _updateCompute.SetBuffer(kernel, SpatialFactionBufferId, _spatialFactionBuffer);
    }

    private void BindActiveInstanceListBuffers(int kernel)
    {
        _updateCompute.SetBuffer(kernel, ActiveInstanceIndexBufferId, _activeInstanceIndexBuffer);
        _updateCompute.SetBuffer(kernel, ActiveInstanceCounterBufferId, _activeInstanceCounterBuffer);
    }

    private void BindBuildVisibleRuntimeInstanceListKernel()
    {
        BindAliveInstanceListBuffers(_buildVisibleRuntimeInstanceListKernel);
        _updateCompute.SetBuffer(_buildVisibleRuntimeInstanceListKernel, DeathStateBufferId, _deathStateBuffer);
        BindSimulationReadBuffers(_buildVisibleRuntimeInstanceListKernel);
        _updateCompute.SetBuffer(_buildVisibleRuntimeInstanceListKernel, AgentSquadDataBufferId, _agentSquadDataBuffer);
        _updateCompute.SetBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleRuntimeSquadMaskBufferId, _visibleRuntimeSquadMaskBuffer);
        _updateCompute.SetBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleInstanceIndicesId, _visibleInstanceIndexBuffer);
        _updateCompute.SetBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleInstanceCounterBufferId, _visibleInstanceCounterBuffer);
        _updateCompute.SetInt(VisibleRuntimeSquadCountId, _activeSquadStateCount);
        _updateCompute.SetInt(VisibleUnassignedInstancesId, _visibleUnassignedInstancesThisFrame ? 1 : 0);
    }

    private void BindBuildVisibleChunkInstanceListKernel()
    {
        BindAliveInstanceListBuffers(_buildVisibleChunkInstanceListKernel);
        _updateCompute.SetBuffer(_buildVisibleChunkInstanceListKernel, DeathStateBufferId, _deathStateBuffer);
        BindSimulationReadBuffers(_buildVisibleChunkInstanceListKernel);
        _updateCompute.SetBuffer(_buildVisibleChunkInstanceListKernel, InstanceRenderChunkBufferId, _instanceRenderChunkBuffer);
        _updateCompute.SetBuffer(_buildVisibleChunkInstanceListKernel, VisibleRenderChunkMaskBufferId, _visibleRenderChunkMaskBuffer);
        _updateCompute.SetBuffer(_buildVisibleChunkInstanceListKernel, VisibleInstanceIndicesId, _visibleInstanceIndexBuffer);
        _updateCompute.SetBuffer(_buildVisibleChunkInstanceListKernel, VisibleInstanceCounterBufferId, _visibleInstanceCounterBuffer);
        _updateCompute.SetInt(VisibleRenderChunkCountId, _renderChunks != null ? _renderChunks.Length : 0);
    }

    private void BindBuildVisibleIndirectArgsKernel(GraphicsBuffer argsBuffer, Mesh mesh, int subMeshIndex)
    {
        _updateCompute.SetBuffer(_buildVisibleIndirectArgsKernel, VisibleInstanceCounterBufferId, _visibleInstanceCounterBuffer);
        _updateCompute.SetBuffer(_buildVisibleIndirectArgsKernel, VisibleRenderArgsBufferId, argsBuffer);
        _updateCompute.SetInt(VisibleRenderArgsIndexCountId, (int)mesh.GetIndexCount(subMeshIndex));
        _updateCompute.SetInt(VisibleRenderArgsStartIndexId, (int)mesh.GetIndexStart(subMeshIndex));
        _updateCompute.SetInt(VisibleRenderArgsBaseVertexId, unchecked((int)mesh.GetBaseVertex(subMeshIndex)));
    }

    private void BindRuntimeSquadBuffers(int kernel)
    {
        _updateCompute.SetBuffer(kernel, SquadStateBufferId, _squadStateBuffer);
        _updateCompute.SetBuffer(kernel, AgentSquadDataBufferId, _agentSquadDataBuffer);
        _updateCompute.SetBuffer(kernel, FormationSlotBufferId, _formationSlotBuffer);
    }

    private void BindFinalizeKernel()
    {
        _updateCompute.SetBuffer(_finalizeKernel, SpawnDataId, _spawnDataBuffer);
        BindSimulationReadBuffers(_finalizeKernel);
        _updateCompute.SetBuffer(_finalizeKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, CombatStateBufferId, _combatStateBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, AnimationClipMetadataBufferId, _animationClipMetadataBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, InstanceAnimationStateBufferId, _instanceAnimationStateBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, InstanceTransformsId, _instanceTransformBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, InstanceFrameDataId, _instanceFrameDataBuffer);
        _updateCompute.SetBuffer(_finalizeKernel, InstanceFrameBlendDataId, _instanceFrameBlendDataBuffer);
        BindAliveInstanceListBuffers(_finalizeKernel);
    }

    private void BindClearSpatialQueriesKernel()
    {
        _updateCompute.SetBuffer(_clearSpatialQueriesKernel, SpatialQueryBufferId, _spatialQueryBuffer);
        _updateCompute.SetBuffer(_clearSpatialQueriesKernel, SpatialQueryResultBufferId, _spatialQueryResultBuffer);
        _updateCompute.SetBuffer(_clearSpatialQueriesKernel, SpatialQueryHitBufferId, _spatialQueryHitBuffer);
    }

    private void BindResolveSpatialQueriesKernel()
    {
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, GridOccupantBufferId, _gridOccupantBuffer);
        BindSpatialElementBuffers(_resolveSpatialQueriesKernel);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, SpatialQueryBufferId, _spatialQueryBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, SpatialQueryResultBufferId, _spatialQueryResultBuffer);
        _updateCompute.SetBuffer(_resolveSpatialQueriesKernel, SpatialQueryHitBufferId, _spatialQueryHitBuffer);
    }

    private int UploadInteractionSpheres()
    {
        int capacity = GetInteractionSphereCapacity();
        if (_interactionSphereUploadCache == null || _interactionSphereUploadCache.Length != capacity)
            _interactionSphereUploadCache = new InteractionSphereData[capacity];

        if (_interactionSphereBuffer == null || _interactionSphereBuffer.count != capacity)
            return 0;

        int activeCount = 0;
        if (_useCharacterAsInteractionSphere)
            TryAppendCharacterInteractionSphere(ref activeCount);

        if (_interactionSpheres != null)
        {
            for (int sphereIndex = 0; sphereIndex < _interactionSpheres.Length; sphereIndex++)
            {
                InteractionSphere sphere = _interactionSpheres[sphereIndex];
                if (sphere.target == null || sphere.radius <= 0.0f || sphere.strength <= 0.0f)
                    continue;

                Vector3 localCenter = transform.InverseTransformPoint(sphere.target.position);
                float radiusScale = MaxAbsComponent(sphere.target.lossyScale);
                float scaledRadius = sphere.radius * Mathf.Max(radiusScale, 0.0001f);
                _interactionSphereUploadCache[activeCount] = new InteractionSphereData
                {
                    localCenterAndRadius = new Vector4(localCenter.x, localCenter.y, localCenter.z, scaledRadius),
                    parameters = new Vector4(sphere.strength, 0.0f, 0.0f, 0.0f)
                };
                activeCount++;
            }
        }

        if (activeCount == 0)
            _interactionSphereUploadCache[0] = default;

        _interactionSphereBuffer.SetData(_interactionSphereUploadCache, 0, 0, Mathf.Max(1, activeCount));
        return activeCount;
    }

    private bool HasRuntimeSquadAnchorData()
    {
        return _activeSquadStateCount > 0 &&
            _activeAgentSquadDataCount > 0;
    }

    private void EnsureRuntimeSquadBuffers()
    {
        EnsureRuntimeSquadStateBuffer();
        EnsureRuntimeSquadAliveCountBuffer();
        EnsureRuntimeAgentSquadDataBuffer();
        EnsureRuntimeFormationSlotBuffer();
        EnsureRuntimeSquadVisibilityMaskBuffer();
    }

    private void EnsureRuntimeSquadStateBuffer()
    {
        int requiredCount = Mathf.Max(1, _activeSquadStateCount);
        if (_squadStateBuffer == null || _squadStateBuffer.count < requiredCount)
        {
            ReleaseBuffer(ref _squadStateBuffer);
            _squadStateBuffer = new ComputeBuffer(requiredCount, Marshal.SizeOf<RuntimeSquadStateGpuData>());
        }

        if (_squadStateUploadCache == null || _squadStateUploadCache.Length != _squadStateBuffer.count)
            _squadStateUploadCache = new RuntimeSquadStateGpuData[_squadStateBuffer.count];
    }

    private void EnsureRuntimeSquadAliveCountBuffer()
    {
        int requiredCount = Mathf.Max(1, _activeSquadStateCount);
        if (_squadAliveCountBuffer == null || _squadAliveCountBuffer.count < requiredCount)
        {
            ReleaseBuffer(ref _squadAliveCountBuffer);
            _squadAliveCountBuffer = new ComputeBuffer(requiredCount, sizeof(uint));
            _runtimeSquadAliveCountDirty = true;
            InvalidateSquadAliveCountReadback();
        }

        if (_runtimeSquadAliveCountReadbackCache == null || _runtimeSquadAliveCountReadbackCache.Length != _squadAliveCountBuffer.count)
            _runtimeSquadAliveCountReadbackCache = new uint[_squadAliveCountBuffer.count];
    }

    private void EnsureRuntimeAgentSquadDataBuffer()
    {
        int requiredCount = Mathf.Max(1, _instanceCount);
        if (_agentSquadDataBuffer == null || _agentSquadDataBuffer.count != requiredCount)
        {
            ReleaseBuffer(ref _agentSquadDataBuffer);
            _agentSquadDataBuffer = new ComputeBuffer(requiredCount, Marshal.SizeOf<RuntimeAgentSquadDataGpuData>());
            _runtimeAgentSquadDataDirty = true;
        }

        if (_agentSquadUploadCache == null || _agentSquadUploadCache.Length != _agentSquadDataBuffer.count)
            _agentSquadUploadCache = new RuntimeAgentSquadDataGpuData[_agentSquadDataBuffer.count];
    }

    private void EnsureRuntimeFormationSlotBuffer()
    {
        int requiredCount = Mathf.Max(1, _activeFormationSlotCount);
        if (_formationSlotBuffer == null || _formationSlotBuffer.count < requiredCount)
        {
            ReleaseBuffer(ref _formationSlotBuffer);
            _formationSlotBuffer = new ComputeBuffer(requiredCount, Marshal.SizeOf<RuntimeFormationSlotGpuData>());
            _runtimeFormationSlotsDirty = true;
        }

        if (_formationSlotUploadCache == null || _formationSlotUploadCache.Length != _formationSlotBuffer.count)
            _formationSlotUploadCache = new RuntimeFormationSlotGpuData[_formationSlotBuffer.count];
    }

    private void EnsureRuntimeSquadVisibilityMaskBuffer()
    {
        int requiredCount = Mathf.Max(1, _activeSquadStateCount);
        if (_visibleRuntimeSquadMaskBuffer == null || _visibleRuntimeSquadMaskBuffer.count < requiredCount)
        {
            ReleaseBuffer(ref _visibleRuntimeSquadMaskBuffer);
            _visibleRuntimeSquadMaskBuffer = new ComputeBuffer(requiredCount, sizeof(uint));
        }

        if (_visibleRuntimeSquadMaskUploadCache == null || _visibleRuntimeSquadMaskUploadCache.Length != _visibleRuntimeSquadMaskBuffer.count)
            _visibleRuntimeSquadMaskUploadCache = new uint[_visibleRuntimeSquadMaskBuffer.count];
    }

    private void UploadRuntimeSquadStates()
    {
        if (_squadStateBuffer == null)
            return;

        if (_activeSquadStateCount <= 0)
        {
            _squadStateUploadCache[0] = default;
            _squadStateBuffer.SetData(_squadStateUploadCache, 0, 0, 1);
            return;
        }

        for (int squadIndex = 0; squadIndex < _activeSquadStateCount; squadIndex++)
        {
            CrowdVatSquadState squadState = _runtimeSquadStates[squadIndex];
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

            Vector2 formationSpacing = new Vector2(
                Mathf.Max(1.0f, squadState.formationSpacing.x),
                Mathf.Max(1.0f, squadState.formationSpacing.y));

            _squadStateUploadCache[squadIndex] = new RuntimeSquadStateGpuData
            {
                localCenterAndAnchorBlend = new Vector4(localCenter.x, localCenter.y, localCenter.z, Mathf.Clamp01(squadState.anchorBlend)),
                localForwardAndMoveSpeed = new Vector4(localForward.x, localForward.y, localForward.z, Mathf.Max(0.0f, squadState.moveSpeed)),
                localTargetAndSpacingX = new Vector4(localTarget.x, localTarget.y, localTarget.z, formationSpacing.x),
                metadata0 = new Vector4(formationSpacing.y, (float)squadState.formationType, (float)squadState.commandType, (float)squadState.factionMask),
                metadata1 = new Vector4(
                    (float)squadState.flags,
                    Mathf.Max(0.1f, squadState.cohesionRadius),
                    Mathf.Clamp01(squadState.cohesionStrength),
                    0.0f)
            };
        }

        _squadStateBuffer.SetData(_squadStateUploadCache, 0, 0, _activeSquadStateCount);
    }

    private void UploadRuntimeSquadAliveCountsIfDirty()
    {
        if (!_runtimeSquadAliveCountDirty || _squadAliveCountBuffer == null)
            return;

        SyncRuntimeSquadAliveCountCpuMirror();
        _squadAliveCountBuffer.SetData(_runtimeSquadAliveCountReadbackCache, 0, 0, _runtimeSquadAliveCountReadbackCache.Length);
        _runtimeSquadAliveCountDirty = false;
        InvalidateSquadAliveCountReadback();
    }

    private void UploadRuntimeAgentSquadDataIfDirty()
    {
        if (!_runtimeAgentSquadDataDirty || _agentSquadDataBuffer == null)
            return;

        RuntimeAgentSquadDataGpuData invalidEntry = CreateInvalidRuntimeAgentSquadData();
        for (int cacheIndex = 0; cacheIndex < _agentSquadUploadCache.Length; cacheIndex++)
            _agentSquadUploadCache[cacheIndex] = invalidEntry;

        int uploadCount = Mathf.Min(_activeAgentSquadDataCount, _agentSquadUploadCache.Length);
        for (int instanceIndex = 0; instanceIndex < uploadCount; instanceIndex++)
        {
            CrowdVatAgentSquadAssignment assignment = _runtimeAgentSquadData[instanceIndex];
            if ((assignment.flags & CrowdVatSquadMemberFlags.Unassigned) != 0)
            {
                _agentSquadUploadCache[instanceIndex] = invalidEntry;
                continue;
            }

            _agentSquadUploadCache[instanceIndex] = new RuntimeAgentSquadDataGpuData
            {
                squadAndSlotAndRoleAndFlags = new Vector4(
                    assignment.squadId,
                    assignment.slotIndex,
                    (float)assignment.roleMask,
                    (float)assignment.flags),
                slotOffsetAndWeight = new Vector4(
                    assignment.slotOffsetOverride.x,
                    assignment.slotOffsetOverride.y,
                    Mathf.Max(0.0f, assignment.weight),
                    0.0f)
            };
        }

        _agentSquadDataBuffer.SetData(_agentSquadUploadCache, 0, 0, _agentSquadUploadCache.Length);
        _runtimeAgentSquadDataDirty = false;
    }

    private static RuntimeAgentSquadDataGpuData CreateInvalidRuntimeAgentSquadData()
    {
        return new RuntimeAgentSquadDataGpuData
        {
            squadAndSlotAndRoleAndFlags = new Vector4(-1.0f, -1.0f, 0.0f, 0.0f),
            slotOffsetAndWeight = Vector4.zero
        };
    }

    private void UploadRuntimeFormationSlotsIfDirty()
    {
        if (!_runtimeFormationSlotsDirty || _formationSlotBuffer == null)
            return;

        Array.Clear(_formationSlotUploadCache, 0, _formationSlotUploadCache.Length);
        int uploadCount = Mathf.Min(_activeFormationSlotCount, _formationSlotUploadCache.Length);
        for (int slotIndex = 0; slotIndex < uploadCount; slotIndex++)
        {
            CrowdVatFormationSlot formationSlot = _runtimeFormationSlots[slotIndex];
            _formationSlotUploadCache[slotIndex] = new RuntimeFormationSlotGpuData
            {
                localOffsetAndRoleAndRank = new Vector4(
                    formationSlot.localOffset.x,
                    formationSlot.localOffset.y,
                    (float)formationSlot.roleMask,
                    formationSlot.rank),
                preferredDistanceAndPadding = new Vector4(
                    Mathf.Max(0.0f, formationSlot.preferredDistance),
                    0.0f,
                    0.0f,
                    0.0f)
            };
        }

        _formationSlotBuffer.SetData(_formationSlotUploadCache, 0, 0, Mathf.Max(1, uploadCount));
        _runtimeFormationSlotsDirty = false;
    }

    private void RebuildAgentDataLayoutCaches()
    {
        EnsureAgentDataLayoutBuffers();
        if (_agentCoreUploadCache == null || _agentPhysicsExtUploadCache == null)
            return;

        CrowdVatAgentComponentMask componentMask = CrowdVatAgentComponentMask.Physics | CrowdVatAgentComponentMask.Crowd;
        CrowdVatAgentStateFlags defaultFlags = CrowdVatAgentStateFlags.Valid | CrowdVatAgentStateFlags.HasPhysics;
        if (_hasClip)
        {
            componentMask |= CrowdVatAgentComponentMask.Animation;
            defaultFlags |= CrowdVatAgentStateFlags.HasAnimation;
        }

        if (_enableGpuInstanceCombat)
        {
            componentMask |= CrowdVatAgentComponentMask.Combat;
            defaultFlags |= CrowdVatAgentStateFlags.HasCombat;
        }

        if (HasRuntimeSquadAnchorData())
        {
            componentMask |= CrowdVatAgentComponentMask.Navigation;
            defaultFlags |= CrowdVatAgentStateFlags.HasNavigation;
        }

        int assignmentCount = Mathf.Min(_activeAgentSquadDataCount, _runtimeAgentSquadData != null ? _runtimeAgentSquadData.Length : 0);
        for (int instanceIndex = 0; instanceIndex < _instanceCount; instanceIndex++)
        {
            bool hasAssignment = instanceIndex < assignmentCount;
            CrowdVatAgentSquadAssignment assignment = hasAssignment
                ? _runtimeAgentSquadData[instanceIndex]
                : default;
            bool isAssignedToSquad = hasAssignment &&
                (assignment.flags & CrowdVatSquadMemberFlags.Unassigned) == 0;

            CrowdVatAgentStateFlags stateFlags = defaultFlags;
            if (isAssignedToSquad)
                stateFlags |= CrowdVatAgentStateFlags.AssignedToSquad;
            else
                stateFlags |= CrowdVatAgentStateFlags.Unassigned;

            _agentCoreUploadCache[instanceIndex] = new CrowdVatAgentCore
            {
                agentId = (uint)instanceIndex,
                stateFlags = stateFlags,
                squadId = isAssignedToSquad ? assignment.squadId : uint.MaxValue,
                behaviorId = isAssignedToSquad ? (uint)assignment.roleMask : 0u,
                componentMask = componentMask,
                extIndex = (uint)instanceIndex,
                simulationIndex = (uint)instanceIndex,
                reserved0 = 0u
            };

            _agentPhysicsExtUploadCache[instanceIndex] = new CrowdVatAgentPhysicsExt
            {
                radius = _collisionRadius,
                height = _collisionHeight,
                maxPushPerStep = _maxPushPerStep,
                collisionMask = _enableApproximateCollision ? 1u : 0u
            };
        }

        _gpuBridgeState.SetAgentDataLayout(_agentCoreUploadCache, _agentPhysicsExtUploadCache);
    }

    private void UploadAgentDataLayout()
    {
        if (_agentCoreBuffer == null || _agentPhysicsExtBuffer == null)
            return;

        _agentCoreBuffer.SetData(_agentCoreUploadCache, 0, 0, _agentCoreUploadCache.Length);
        _agentPhysicsExtBuffer.SetData(_agentPhysicsExtUploadCache, 0, 0, _agentPhysicsExtUploadCache.Length);
    }

    private void UploadAgentDataLayoutIfDirty()
    {
        if (!_agentDataLayoutDirty)
            return;

        RebuildAgentDataLayoutCaches();
        UploadAgentDataLayout();
        _agentDataLayoutDirty = false;
    }

    private int UploadSpatialQueries()
    {
        if (_activeSpatialQueryCount <= 0 ||
            _runtimeSpatialQueries == null ||
            _spatialQueryBuffer == null ||
            _spatialQueryBuffer.count < _activeSpatialQueryCount)
        {
            return 0;
        }

        EnsureSpatialQueryReadbackCaches();

        for (int queryIndex = 0; queryIndex < _activeSpatialQueryCount; queryIndex++)
        {
            CrowdVatSpatialQueryRequest request = _runtimeSpatialQueries[queryIndex];
            Vector3 localStart = transform.InverseTransformPoint(request.worldStart);
            Vector3 localEnd = transform.InverseTransformPoint(request.worldEnd);

            _spatialQueryUploadCache[queryIndex] = new SpatialQueryData
            {
                localStartAndRadius = new Vector4(localStart.x, localStart.y, localStart.z, Mathf.Max(0.0f, request.radius)),
                localEndAndPadding = new Vector4(localEnd.x, localEnd.y, localEnd.z, 0.0f),
                queryId = unchecked((uint)request.queryId),
                targetMask = (uint)request.targetMask,
                factionMask = (uint)request.factionMask,
                shape = (uint)request.shape,
                flags = (uint)request.flags,
                maxHits = (uint)Mathf.Clamp((int)request.maxHits, 0, _maxHitsPerSpatialQuery),
                padding0 = 0u,
                padding1 = 0u
            };
        }

        _spatialQueryBuffer.SetData(_spatialQueryUploadCache, 0, 0, _activeSpatialQueryCount);
        return _activeSpatialQueryCount;
    }

    private void TryAppendCharacterInteractionSphere(ref int activeCount)
    {
        if (_characterController == null || _characterInteractionStrength <= 0.0f || activeCount >= _interactionSphereUploadCache.Length)
            return;

        Vector3 worldCenter = _characterController.transform.TransformPoint(_characterController.center);
        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
        float radius = Mathf.Max(_characterController.radius, _characterController.height * 0.25f) * _characterInteractionRadiusMultiplier;

        _interactionSphereUploadCache[activeCount] = new InteractionSphereData
        {
            localCenterAndRadius = new Vector4(localCenter.x, localCenter.y, localCenter.z, radius),
            parameters = new Vector4(_characterInteractionStrength, 0.0f, 0.0f, 0.0f)
        };
        activeCount++;
    }

    private bool TryGetActiveBubbleLocalCenter(out Vector3 localCenter)
    {
        RefreshRuntimeTargets();

        if (_resolvedActiveBubbleTarget != null)
        {
            localCenter = transform.InverseTransformPoint(_resolvedActiveBubbleTarget.position);
            return true;
        }

        localCenter = default;
        return false;
    }

    private void DispatchAllInstances(int kernel)
    {
        int threadGroupCount = Mathf.CeilToInt(_instanceCount / (float)ThreadGroupSize);
        _updateCompute.Dispatch(kernel, threadGroupCount, 1, 1);
    }

    private void DispatchAliveInstancesIndirect(int kernel)
    {
        if (_aliveInstanceDispatchArgsBuffer == null)
            return;

        _updateCompute.DispatchIndirect(kernel, _aliveInstanceDispatchArgsBuffer, 0);
    }

    private void DispatchActiveInstancesIndirect(int kernel)
    {
        if (_activeInstanceDispatchArgsBuffer == null)
            return;

        _updateCompute.DispatchIndirect(kernel, _activeInstanceDispatchArgsBuffer, 0);
    }

    private void DispatchGrid(int kernel)
    {
        int threadGroupCount = Mathf.CeilToInt(_gridCellCount / (float)ThreadGroupSize);
        _updateCompute.Dispatch(kernel, threadGroupCount, 1, 1);
    }

    private void DispatchSpatialQueries(int kernel, int queryCount)
    {
        int threadGroupCount = Mathf.CeilToInt(queryCount / (float)SpatialQueryThreadGroupSize);
        _updateCompute.Dispatch(kernel, threadGroupCount, 1, 1);
    }

    private void SwapSimulationBuffers()
    {
        ComputeBuffer buffer = _simulationPositionYawReadBuffer;
        _simulationPositionYawReadBuffer = _simulationPositionYawWriteBuffer;
        _simulationPositionYawWriteBuffer = buffer;

        buffer = _simulationScaleReadBuffer;
        _simulationScaleReadBuffer = _simulationScaleWriteBuffer;
        _simulationScaleWriteBuffer = buffer;

        buffer = _simulationVelocityReadBuffer;
        _simulationVelocityReadBuffer = _simulationVelocityWriteBuffer;
        _simulationVelocityWriteBuffer = buffer;
    }

}
