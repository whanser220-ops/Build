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
        RefreshGpuPassDebugMetadataState();

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
        RebuildAliveInstanceCompactionData();
        bool captureAiDebugGpuStages = _aiDebugGpuStageCaptureActive;
        if (captureAiDebugGpuStages)
        {
            BeginAiDebugGpuFrameCapture();
            captureAiDebugGpuStages = _aiDebugGpuStageCaptureActive;
        }

        BindPredictKernel();
        DispatchAliveInstancesIndirect(_predictKernel);
        SwapSimulationBuffers();

        if (captureAiDebugGpuStages)
            CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.PredictAfter, -1);

        BindClearWakeGridKernel();
        DispatchWakeGrid(_clearWakeGridKernel);

        BindBuildWakeGridKernel();
        DispatchAliveInstancesIndirect(_buildWakeGridKernel);

        EvaluatePhysicsActive();
        RebuildPhysicsActiveInstanceCompactionData();
        if (!needsCombatGrid)
            ResetCombatActiveInstanceCompactionData();

        bool usesEnvironmentPbd = _hasResolvedEnvironmentDistanceField || _hasResolvedTerrainCollision || _hasResolvedStaticSdfCollision;
        int solverPassCount = usesCrowdApproximation || usesEnvironmentPbd ? Mathf.Max(1, _solverIterations) : 0;

        for (int iterationIndex = 0; iterationIndex < solverPassCount; iterationIndex++)
        {
            ClearGridForRebuild();

            _updateCompute.SetInt(GridBuildModeId, GridBuildModePhysicsActiveOnly);
            BindBuildGridKernel(usePhysicsActiveInstanceList: true);
            DispatchPhysicsActiveInstancesIndirect(_buildGridKernel);
            SwapGridTouchedBuffers();

            BindSolveKernel();
            DispatchPhysicsActiveInstancesIndirect(_solveCrowdKernel);
            SwapSimulationBuffers();
            if (captureAiDebugGpuStages)
                CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.SolveAfter, iterationIndex);
        }

        if (needsCombatGrid || needsSpatialQueryGrid)
        {
            ClearGridForRebuild();

            _updateCompute.SetInt(GridBuildModeId, GridBuildModeAllQueryables);
            BindBuildGridKernel(usePhysicsActiveInstanceList: false);
            DispatchAliveInstancesIndirect(_buildGridKernel);
            SwapGridTouchedBuffers();
            if (captureAiDebugGpuStages)
                CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.GridAfter, -1);

            if (needsCombatGrid)
                RebuildCombatActiveInstanceCompactionData();
            else
                ResetCombatActiveInstanceCompactionData();

            if (needsSpatialQueryGrid)
            {
                BindBuildSpatialElementsKernel();
                DispatchAllInstances(_buildSpatialElementsKernel);
            }

            if (needsCombatGrid)
            {
                BuildCombatSquadCandidates();

                BindResolveTargetAcquisitionKernel();
                DispatchCombatActiveInstancesIndirect(_resolveTargetAcquisitionKernel);

                BindBuildSquadAcquisitionDispatchArgsKernel();
                DispatchGpuPass(_buildSquadAcquisitionDispatchArgsKernel, 1, 1, 1);

                BindEvaluateSquadAcquisitionCandidatesKernel();
                DispatchSquadAcquisitionIndirect(_evaluateSquadAcquisitionCandidatesKernel);

                BindBuildTargetAcquisitionLosDispatchArgsKernel();
                DispatchGpuPass(_buildTargetAcquisitionLosDispatchArgsKernel, 1, 1, 1);

                BindResolveTargetAcquisitionLineOfSightKernel();
                DispatchTargetAcquisitionLosQueriesIndirect(_resolveTargetAcquisitionLineOfSightKernel);

                BindFinalizeTargetAcquisitionKernel();
                DispatchCombatActiveInstancesIndirect(_finalizeTargetAcquisitionKernel);
                if (captureAiDebugGpuStages)
                    CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId.TargetAcquisitionAfter, -1);

                BindResolveInstanceCombatKernel();
                DispatchCombatActiveInstancesIndirect(_resolveInstanceCombatKernel);
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

        _updateCompute.SetInt(GridBuildModeId, GridBuildModePhysicsActiveOnly);

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
        DispatchGpuPass(_buildAliveDispatchArgsKernel, 1, 1, 1);
    }

    private void RebuildPhysicsActiveInstanceCompactionData()
    {
        if (_physicsActiveInstanceCounterBuffer == null || _physicsActiveInstanceDispatchArgsBuffer == null)
            return;

        _physicsActiveInstanceCounterBuffer.SetData(AliveInstanceCounterResetData);
        _physicsActiveInstanceDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);

        BindBuildPhysicsActiveInstanceListKernel();
        DispatchAliveInstancesIndirect(_buildPhysicsActiveInstanceListKernel);

        BindBuildPhysicsActiveDispatchArgsKernel();
        DispatchGpuPass(_buildPhysicsActiveDispatchArgsKernel, 1, 1, 1);
    }

    private void RebuildCombatActiveInstanceCompactionData()
    {
        if (_combatActiveInstanceCounterBuffer == null || _combatActiveInstanceDispatchArgsBuffer == null)
            return;

        _combatActiveInstanceCounterBuffer.SetData(CombatActiveInstanceCounterResetData);
        _combatActiveInstanceDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);

        BindEvaluateCombatActiveKernel();
        DispatchAliveInstancesIndirect(_evaluateCombatActiveKernel);

        BindBuildCombatActiveInstanceListKernel();
        DispatchAliveInstancesIndirect(_buildCombatActiveInstanceListKernel);

        BindCompactCombatActiveInstanceListKernel();
        DispatchAllInstances(_compactCombatActiveInstanceListKernel);

        BindBuildCombatActiveDispatchArgsKernel();
        DispatchGpuPass(_buildCombatActiveDispatchArgsKernel, 1, 1, 1);
    }

    private void BuildCombatSquadCandidates()
    {
        if (!HasRuntimeSquadAnchorData() ||
            _clearCombatSquadCandidateCountersKernel < 0 ||
            _buildCombatCandidateClustersKernel < 0 ||
            _combatSquadCandidateBuffer == null ||
            _combatSquadCandidateCounterBuffer == null ||
            _combatCandidateClusterDispatchArgsBuffer == null)
        {
            return;
        }

        BindClearCombatSquadCandidateCountersKernel();
        DispatchSquads(_clearCombatSquadCandidateCountersKernel, _activeSquadStateCount);

        int workItemCount = BuildCombatCandidateClusterWorkItems();
        if (workItemCount <= 0)
            return;

        BindBuildCombatCandidateClustersKernel();
        DispatchCombatCandidateClustersIndirect(_buildCombatCandidateClustersKernel);
    }

    private int BuildCombatCandidateClusterWorkItems()
    {
        if (_runtimeSquadStates == null ||
            _activeSquadStateCount <= 0 ||
            _gridDimensions.x <= 0 ||
            _gridDimensions.y <= 0 ||
            _queryCellSize <= 0.0f ||
            !_enableGpuInstanceCombat ||
            _combatRange <= 0.0f)
        {
            return 0;
        }

        int squadCount = Mathf.Min(_activeSquadStateCount, _runtimeSquadStates.Length);
        float combatRange = Mathf.Max(_combatRange, 0.0f);
        float targetPadding = Mathf.Max(_maximumQueryAgentRadius, _collisionRadius);
        int workItemCount = 0;
        for (int squadIndex = 0; squadIndex < squadCount; squadIndex++)
        {
            CrowdVatSquadState squadState = _runtimeSquadStates[squadIndex];
            if (squadState.factionMask == CrowdVatFactionMask.None)
                continue;

            Vector3 localCenter = transform.InverseTransformPoint(squadState.worldCenter);
            float sourceRadius = Mathf.Max(Mathf.Max(squadState.cohesionRadius, _maxDisplacementFromSpawn), _collisionRadius);
            float broadphaseRadius = sourceRadius + combatRange + targetPadding;
            Vector2 sourceCenterXZ = new Vector2(localCenter.x, localCenter.z);
            if (!TryGetSimulationGridCellRange(
                sourceCenterXZ - Vector2.one * broadphaseRadius,
                sourceCenterXZ + Vector2.one * broadphaseRadius,
                out Vector2Int minCoord,
                out Vector2Int maxCoord))
            {
                continue;
            }

            int cellWidth = maxCoord.x - minCoord.x + 1;
            int cellHeight = maxCoord.y - minCoord.y + 1;
            int cellCount = Mathf.Max(0, cellWidth * cellHeight);
            int tileCount = Mathf.CeilToInt(cellCount / (float)CombatCandidateClusterCellsPerTile);
            if (tileCount <= 0)
                continue;

            EnsureCombatCandidateClusterWorkItemUploadCapacity(workItemCount + tileCount);
            for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
            {
                _combatCandidateClusterWorkItemUploadCache[workItemCount] = new CombatCandidateClusterWorkItemData
                {
                    sourceSquadIndex = (uint)squadIndex,
                    tileBegin = (uint)(tileIndex * CombatCandidateClusterCellsPerTile),
                    cellWidth = (uint)cellWidth,
                    cellCount = (uint)cellCount,
                    minCoordX = minCoord.x,
                    minCoordY = minCoord.y
                };
                workItemCount++;
            }
        }

        if (workItemCount <= 0)
            return 0;

        EnsureCombatCandidateClusterWorksetBuffers(workItemCount);
        _combatCandidateClusterWorkItemBuffer.SetData(
            _combatCandidateClusterWorkItemUploadCache,
            0,
            0,
            workItemCount);
        _combatCandidateClusterDispatchArgsUploadCache[0] = (uint)workItemCount;
        _combatCandidateClusterDispatchArgsUploadCache[1] = 1u;
        _combatCandidateClusterDispatchArgsUploadCache[2] = 1u;
        _combatCandidateClusterDispatchArgsBuffer.SetData(_combatCandidateClusterDispatchArgsUploadCache);
        return workItemCount;
    }

    private void EnsureCombatCandidateClusterWorkItemUploadCapacity(int requiredCount)
    {
        if (_combatCandidateClusterWorkItemUploadCache != null &&
            _combatCandidateClusterWorkItemUploadCache.Length >= requiredCount)
        {
            return;
        }

        int capacity = Mathf.Max(1, _combatCandidateClusterWorkItemUploadCache?.Length ?? 0);
        while (capacity < requiredCount)
            capacity *= 2;

        Array.Resize(ref _combatCandidateClusterWorkItemUploadCache, capacity);
    }

    private bool TryGetSimulationGridCellRange(Vector2 minXZ, Vector2 maxXZ, out Vector2Int minCoord, out Vector2Int maxCoord)
    {
        minCoord = default;
        maxCoord = default;
        if (_gridDimensions.x <= 0 ||
            _gridDimensions.y <= 0 ||
            _queryCellSize <= 0.0f ||
            maxXZ.x < _gridMinXZ.x ||
            maxXZ.y < _gridMinXZ.y ||
            minXZ.x >= _gridMaxXZ.x ||
            minXZ.y >= _gridMaxXZ.y)
        {
            return false;
        }

        Vector2 clampedMin = Vector2.Max(minXZ, _gridMinXZ);
        Vector2 clampedMax = Vector2.Min(maxXZ, _gridMaxXZ - Vector2.one * 1e-4f);
        minCoord = GetClampedSimulationGridCellCoord(clampedMin);
        maxCoord = GetClampedSimulationGridCellCoord(clampedMax);
        return true;
    }

    private Vector2Int GetClampedSimulationGridCellCoord(Vector2 localXZ)
    {
        float invCellSize = 1.0f / Mathf.Max(_queryCellSize, 1e-4f);
        int cellX = Mathf.Clamp(Mathf.FloorToInt((localXZ.x - _gridMinXZ.x) * invCellSize), 0, _gridDimensions.x - 1);
        int cellY = Mathf.Clamp(Mathf.FloorToInt((localXZ.y - _gridMinXZ.y) * invCellSize), 0, _gridDimensions.y - 1);
        return new Vector2Int(cellX, cellY);
    }

    private void ResetCombatActiveInstanceCompactionData()
    {
        if (_combatActiveInstanceCounterBuffer == null || _combatActiveInstanceDispatchArgsBuffer == null)
            return;

        _combatActiveInstanceCounterBuffer.SetData(CombatActiveInstanceCounterResetData);
        _combatActiveInstanceDispatchArgsBuffer.SetData(AliveInstanceDispatchArgsResetData);
    }

    private void EvaluatePhysicsActive()
    {
        BindEvaluatePhysicsActiveKernel();
        _updateCompute.SetInt(PhysicsActivationPassId, 0);
        DispatchAliveInstancesIndirect(_evaluatePhysicsActiveKernel);

        _updateCompute.SetInt(PhysicsActivationPassId, 1);
        DispatchAliveInstancesIndirect(_evaluatePhysicsActiveKernel);
    }

    private void ConfigureCommonComputeParameters(float deltaTime, int interactionSphereCount)
    {
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
        _updateCompute.SetInt(InteractionSphereCountId, interactionSphereCount);
        _updateCompute.SetInts(GridDimId, _gridDimensions.x, _gridDimensions.y);
        _updateCompute.SetInt(GridCellCountId, _gridCellCount);
        _updateCompute.SetInt(MaxCellOccupancyId, _maxCellOccupancy);
        _updateCompute.SetFloat(CellSizeId, _queryCellSize);
        _updateCompute.SetFloat(InvCellSizeId, 1.0f / Mathf.Max(_queryCellSize, 1e-4f));
        _updateCompute.SetInts(WakeGridDimId, _wakeGridDimensions.x, _wakeGridDimensions.y);
        _updateCompute.SetInt(WakeGridCellCountId, _wakeGridCellCount);
        _updateCompute.SetFloat(WakeGridCellSizeId, _resolvedWakeGridCellSize);
        _updateCompute.SetFloat(WakeInvGridCellSizeId, 1.0f / Mathf.Max(_resolvedWakeGridCellSize, 1e-4f));
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
        _updateCompute.SetFloat(PhysicsWakeSpeedId, Mathf.Max(0.0f, _physicsWakeSpeed));
        _updateCompute.SetFloat(PhysicsSleepSpeedId, Mathf.Max(0.0f, _physicsSleepSpeed));
        _updateCompute.SetFloat(PhysicsWakeAnchorErrorId, Mathf.Max(0.0f, _physicsWakeAnchorError));
        _updateCompute.SetFloat(PhysicsSleepAnchorErrorId, Mathf.Max(0.0f, _physicsSleepAnchorError));
        _updateCompute.SetFloat(PhysicsWakeNeighborRadiusId, Mathf.Max(_physicsWakeNeighborRadius, Mathf.Max(_collisionRadius * 3.0f, 1.1f)));
        _updateCompute.SetFloat(PhysicsActiveHoldTimeId, Mathf.Max(0.0f, _physicsActiveHoldTime));
        _updateCompute.SetFloat(PhysicsSleepDelayId, Mathf.Max(0.0f, _physicsSleepDelay));
        _updateCompute.SetFloat(CombatActiveHoldTimeId, Mathf.Max(0.0f, _combatActiveHoldTime));
        _updateCompute.SetInt(CombatActiveProbeIntervalFramesId, Mathf.Max(1, _combatActiveProbeIntervalFrames));
        _updateCompute.SetInt(SimulationFrameIndexId, Application.isPlaying ? Mathf.Max(Time.frameCount, 0) : 0);
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
        _updateCompute.SetInt(CombatEnvironmentOcclusionMaxStepsId, Mathf.Clamp(_combatEnvironmentOcclusionMaxSteps, 1, 96));
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
        _updateCompute.SetFloat(TargetAcquisitionFovCosineId, Mathf.Cos(Mathf.Clamp(_targetAcquisitionFovDegrees, 1.0f, 360.0f) * 0.5f * Mathf.Deg2Rad));
        _updateCompute.SetInt(TargetAcquisitionMaxCandidateChecksId, Mathf.Max(1, _targetAcquisitionMaxCandidateChecks));
        _updateCompute.SetFloat(TargetAcquisitionCurrentTargetBonusId, Mathf.Max(0.0f, _targetAcquisitionCurrentTargetBonus));
        _updateCompute.SetFloat(TargetAcquisitionLastAttackerBonusId, Mathf.Max(0.0f, _targetAcquisitionLastAttackerBonus));
        _updateCompute.SetFloat(TargetAcquisitionLockDurationId, Mathf.Max(0.0f, _targetAcquisitionLockDuration));
        _updateCompute.SetFloat(TargetAcquisitionLostSightGraceId, Mathf.Max(0.0f, _targetAcquisitionLostSightGrace));
        _updateCompute.SetFloat(TargetAcquisitionLineOfSightRecheckIntervalId, Mathf.Max(0.0f, _targetAcquisitionLineOfSightRecheckInterval));
        _updateCompute.SetFloat(TargetAcquisitionDistanceScoreWeightId, Mathf.Max(0.0f, _targetAcquisitionDistanceScoreWeight));
        _updateCompute.SetFloat(TargetAcquisitionViewScoreWeightId, Mathf.Max(0.0f, _targetAcquisitionViewScoreWeight));
        _updateCompute.SetInt(CombatCandidateCapacityPerSquadId, HasRuntimeSquadAnchorData() ? Mathf.Clamp(_combatCandidateCapacityPerSquad, 1, CombatCandidateCapacityPerSquadMax) : 0);
        _updateCompute.SetVector(EnvironmentDistanceWorldCenterId, hasBakedEnvironmentDistanceField ? bakedEnvironmentField.worldCenter : Vector3.zero);
        _updateCompute.SetVector(EnvironmentDistanceWorldSizeId, hasBakedEnvironmentDistanceField ? bakedEnvironmentField.worldSize : Vector3.one);
        _updateCompute.SetFloat(EnvironmentDistanceDistanceScaleId, hasBakedEnvironmentDistanceField ? bakedEnvironmentField.distanceScale : 1.0f);
        _updateCompute.SetFloat(EnvironmentDistanceDistanceBiasId, hasBakedEnvironmentDistanceField ? bakedEnvironmentField.distanceBias : 0.0f);
        _updateCompute.SetVector(StaticSdfWorldCenterId, hasStaticSdfField ? staticSdfField.worldCenter : Vector3.zero);
        _updateCompute.SetVector(StaticSdfWorldSizeId, hasStaticSdfField ? staticSdfField.worldSize : Vector3.one);
        _updateCompute.SetFloat(StaticSdfDistanceScaleId, hasStaticSdfField ? staticSdfField.distanceScale : 1.0f);
        _updateCompute.SetFloat(StaticSdfDistanceBiasId, hasStaticSdfField ? staticSdfField.distanceBias : 0.0f);
        _updateCompute.SetInt(GridBuildModeId, GridBuildModePhysicsActiveOnly);
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
        SetGpuPassBuffer(_predictKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_predictKernel);
        BindSimulationWriteBuffers(_predictKernel);
        SetGpuPassBuffer(_predictKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_predictKernel, InteractionSphereBufferId, "_InteractionSphereBuffer", "interactionSphere", _interactionSphereBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_predictKernel, WakeGridCounterBufferId, "_WakeGridCounterBuffer", "wakeGridCounter", _wakeGridCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_predictKernel, WakeGridOccupantBufferId, "_WakeGridOccupantBuffer", "wakeGridOccupant", _wakeGridOccupantBuffer, GpuPassBindingAccess.Srv);
        BindAliveInstanceListBuffers(_predictKernel);
        BindRuntimeSquadBuffers(_predictKernel);
        _updateCompute.SetTexture(_predictKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_predictKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_predictKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindBuildAliveInstanceListKernel()
    {
        SetGpuPassBuffer(_buildAliveInstanceListKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildAliveInstanceListKernel, AliveInstanceIndexBufferId, "_AliveInstanceIndexBuffer", "aliveInstanceIndex", _aliveInstanceIndexBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildAliveInstanceListKernel, AliveInstanceCounterBufferId, "_AliveInstanceCounterBuffer", "aliveInstanceCounter", _aliveInstanceCounterBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildAliveDispatchArgsKernel()
    {
        SetGpuPassBuffer(_buildAliveDispatchArgsKernel, AliveInstanceCounterBufferId, "_AliveInstanceCounterBuffer", "aliveInstanceCounter", _aliveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildAliveDispatchArgsKernel, AliveInstanceDispatchArgsBufferId, "_AliveInstanceDispatchArgsBuffer", "aliveInstanceDispatchArgs", _aliveInstanceDispatchArgsBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindClearWakeGridKernel()
    {
        SetGpuPassBuffer(_clearWakeGridKernel, WakeGridCounterBufferId, "_WakeGridCounterBuffer", "wakeGridCounter", _wakeGridCounterBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildWakeGridKernel()
    {
        BindSimulationReadBuffers(_buildWakeGridKernel);
        SetGpuPassBuffer(_buildWakeGridKernel, WakeGridCounterBufferId, "_WakeGridCounterBuffer", "wakeGridCounter", _wakeGridCounterBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildWakeGridKernel, WakeGridOccupantBufferId, "_WakeGridOccupantBuffer", "wakeGridOccupant", _wakeGridOccupantBuffer, GpuPassBindingAccess.Uav);
        BindAliveInstanceListBuffers(_buildWakeGridKernel);
    }

    private void BindEvaluatePhysicsActiveKernel()
    {
        SetGpuPassBuffer(_evaluatePhysicsActiveKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_evaluatePhysicsActiveKernel);
        SetGpuPassBuffer(_evaluatePhysicsActiveKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_evaluatePhysicsActiveKernel, PhysicsActiveStateBufferId, "_PhysicsActiveStateBuffer", "physicsActiveState", _physicsActiveStateBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_evaluatePhysicsActiveKernel, PhysicsActivationMetaBufferId, "_PhysicsActivationMetaBuffer", "physicsActivationMeta", _physicsActivationMetaBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_evaluatePhysicsActiveKernel, WakeGridCounterBufferId, "_WakeGridCounterBuffer", "wakeGridCounter", _wakeGridCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_evaluatePhysicsActiveKernel, WakeGridOccupantBufferId, "_WakeGridOccupantBuffer", "wakeGridOccupant", _wakeGridOccupantBuffer, GpuPassBindingAccess.Srv);
        BindAliveInstanceListBuffers(_evaluatePhysicsActiveKernel);
        BindRuntimeSquadBuffers(_evaluatePhysicsActiveKernel);
    }

    private void BindBuildPhysicsActiveInstanceListKernel()
    {
        SetGpuPassBuffer(_buildPhysicsActiveInstanceListKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildPhysicsActiveInstanceListKernel, PhysicsActiveStateBufferId, "_PhysicsActiveStateBuffer", "physicsActiveState", _physicsActiveStateBuffer, GpuPassBindingAccess.Srv);
        BindAliveInstanceListBuffers(_buildPhysicsActiveInstanceListKernel);
        SetGpuPassBuffer(_buildPhysicsActiveInstanceListKernel, PhysicsActiveInstanceIndexBufferId, "_PhysicsActiveInstanceIndexBuffer", "physicsActiveInstanceIndex", _physicsActiveInstanceIndexBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildPhysicsActiveInstanceListKernel, PhysicsActiveInstanceCounterBufferId, "_PhysicsActiveInstanceCounterBuffer", "physicsActiveInstanceCounter", _physicsActiveInstanceCounterBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildPhysicsActiveDispatchArgsKernel()
    {
        SetGpuPassBuffer(_buildPhysicsActiveDispatchArgsKernel, PhysicsActiveInstanceCounterBufferId, "_PhysicsActiveInstanceCounterBuffer", "physicsActiveInstanceCounter", _physicsActiveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildPhysicsActiveDispatchArgsKernel, PhysicsActiveInstanceDispatchArgsBufferId, "_PhysicsActiveInstanceDispatchArgsBuffer", "physicsActiveInstanceDispatchArgs", _physicsActiveInstanceDispatchArgsBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindEvaluateCombatActiveKernel()
    {
        SetGpuPassBuffer(_evaluateCombatActiveKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_evaluateCombatActiveKernel, CombatStateBufferId, "_CombatStateBuffer", "combatState", _combatStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_evaluateCombatActiveKernel, TargetAcquisitionStateBufferId, "_TargetAcquisitionStateBuffer", "targetAcquisitionState", _targetAcquisitionStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_evaluateCombatActiveKernel, CombatActiveStateBufferId, "_CombatActiveStateBuffer", "combatActiveState", _combatActiveStateBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_evaluateCombatActiveKernel, CombatActivationMetaBufferId, "_CombatActivationMetaBuffer", "combatActivationMeta", _combatActivationMetaBuffer, GpuPassBindingAccess.Uav);
        BindAliveInstanceListBuffers(_evaluateCombatActiveKernel);
    }

    private void BindBuildCombatActiveInstanceListKernel()
    {
        SetGpuPassBuffer(_buildCombatActiveInstanceListKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildCombatActiveInstanceListKernel, CombatActiveStateBufferId, "_CombatActiveStateBuffer", "combatActiveState", _combatActiveStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildCombatActiveInstanceListKernel, CombatActivationMetaBufferId, "_CombatActivationMetaBuffer", "combatActivationMeta", _combatActivationMetaBuffer, GpuPassBindingAccess.Srv);
        BindAliveInstanceListBuffers(_buildCombatActiveInstanceListKernel);
        SetGpuPassBuffer(_buildCombatActiveInstanceListKernel, CombatActiveInstanceIndexBufferId, "_CombatActiveInstanceIndexBuffer", "combatActiveInstanceIndex", _combatActiveInstanceIndexBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildCombatActiveInstanceListKernel, CombatActiveInstanceCounterBufferId, "_CombatActiveInstanceCounterBuffer", "combatActiveInstanceCounter", _combatActiveInstanceCounterBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindCompactCombatActiveInstanceListKernel()
    {
        SetGpuPassBuffer(_compactCombatActiveInstanceListKernel, CombatActiveInstanceIndexBufferId, "_CombatActiveInstanceIndexBuffer", "combatActiveInstanceIndex", _combatActiveInstanceIndexBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_compactCombatActiveInstanceListKernel, CombatActiveInstanceCounterBufferId, "_CombatActiveInstanceCounterBuffer", "combatActiveInstanceCounter", _combatActiveInstanceCounterBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildCombatActiveDispatchArgsKernel()
    {
        SetGpuPassBuffer(_buildCombatActiveDispatchArgsKernel, CombatActiveInstanceCounterBufferId, "_CombatActiveInstanceCounterBuffer", "combatActiveInstanceCounter", _combatActiveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildCombatActiveDispatchArgsKernel, CombatActiveInstanceDispatchArgsBufferId, "_CombatActiveInstanceDispatchArgsBuffer", "combatActiveInstanceDispatchArgs", _combatActiveInstanceDispatchArgsBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildGridClearDispatchArgsKernel()
    {
        SetGpuPassBuffer(_buildGridClearDispatchArgsKernel, GridPrevTouchedCounterBufferId, "_GridPrevTouchedCounterBuffer", "gridPrevTouchedCounter", _gridPrevTouchedCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildGridClearDispatchArgsKernel, GridClearDispatchArgsBufferId, "_GridClearDispatchArgsBuffer", "gridClearDispatchArgs", _gridClearDispatchArgsBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindClearGridKernel()
    {
        SetGpuPassBuffer(_clearGridKernel, GridCounterBufferId, "_GridCounterBuffer", "gridCounter", _gridCounterBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_clearGridKernel, GridPrevTouchedCellBufferId, "_GridPrevTouchedCellBuffer", "gridPrevTouchedCell", _gridPrevTouchedCellBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_clearGridKernel, GridPrevTouchedCounterBufferId, "_GridPrevTouchedCounterBuffer", "gridPrevTouchedCounter", _gridPrevTouchedCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_clearGridKernel, GridCurrTouchedCounterBufferId, "_GridCurrTouchedCounterBuffer", "gridCurrTouchedCounter", _gridCurrTouchedCounterBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildGridKernel(bool usePhysicsActiveInstanceList)
    {
        BindSimulationReadBuffers(_buildGridKernel);
        _updateCompute.SetInt(GridBuildModeId, usePhysicsActiveInstanceList ? GridBuildModePhysicsActiveOnly : GridBuildModeAllQueryables);
        SetGpuPassBuffer(_buildGridKernel, GridCounterBufferId, "_GridCounterBuffer", "gridCounter", _gridCounterBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildGridKernel, GridOccupantBufferId, "_GridOccupantBuffer", "gridOccupant", _gridOccupantBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildGridKernel, GridCurrTouchedCellBufferId, "_GridCurrTouchedCellBuffer", "gridCurrTouchedCell", _gridCurrTouchedCellBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildGridKernel, GridCurrTouchedCounterBufferId, "_GridCurrTouchedCounterBuffer", "gridCurrTouchedCounter", _gridCurrTouchedCounterBuffer, GpuPassBindingAccess.Uav);
        BindPhysicsActiveInstanceListBuffers(_buildGridKernel, GpuPassBindingAccess.Srv);
        BindAliveInstanceListBuffers(_buildGridKernel);
    }

    private void BindBuildSpatialElementsKernel()
    {
        SetGpuPassBuffer(_buildSpatialElementsKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_buildSpatialElementsKernel);
        SetGpuPassBuffer(_buildSpatialElementsKernel, PhysicsActiveStateBufferId, "_PhysicsActiveStateBuffer", "physicsActiveState", _physicsActiveStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildSpatialElementsKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        BindSpatialElementBuffers(_buildSpatialElementsKernel, GpuPassBindingAccess.Uav);
    }

    private void BindBuildCombatCandidateClustersKernel()
    {
        SetGpuPassBuffer(_buildCombatCandidateClustersKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_buildCombatCandidateClustersKernel);
        SetGpuPassBuffer(_buildCombatCandidateClustersKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildCombatCandidateClustersKernel, GridCounterBufferId, "_GridCounterBuffer", "gridCounter", _gridCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildCombatCandidateClustersKernel, GridOccupantBufferId, "_GridOccupantBuffer", "gridOccupant", _gridOccupantBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildCombatCandidateClustersKernel, CombatCandidateClusterWorkItemBufferId, "_CombatCandidateClusterWorkItemBuffer", "combatCandidateClusterWorkItem", _combatCandidateClusterWorkItemBuffer, GpuPassBindingAccess.Srv);
        BindRuntimeSquadBuffers(_buildCombatCandidateClustersKernel);
        BindCombatSquadCandidateBuffers(_buildCombatCandidateClustersKernel, GpuPassBindingAccess.Uav);
    }

    private void BindClearCombatSquadCandidateCountersKernel()
    {
        SetGpuPassBuffer(_clearCombatSquadCandidateCountersKernel, CombatSquadCandidateCounterBufferId, "_CombatSquadCandidateCounterBuffer", "combatSquadCandidateCounter", _combatSquadCandidateCounterBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindSolveKernel()
    {
        SetGpuPassBuffer(_solveCrowdKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_solveCrowdKernel);
        BindSimulationWriteBuffers(_solveCrowdKernel);
        SetGpuPassBuffer(_solveCrowdKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_solveCrowdKernel, PhysicsActivationMetaBufferId, "_PhysicsActivationMetaBuffer", "physicsActivationMeta", _physicsActivationMetaBuffer, GpuPassBindingAccess.Uav);
        BindPhysicsActiveInstanceListReadBuffers(_solveCrowdKernel);
        SetGpuPassBuffer(_solveCrowdKernel, GridCounterBufferId, "_GridCounterBuffer", "gridCounter", _gridCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_solveCrowdKernel, GridOccupantBufferId, "_GridOccupantBuffer", "gridOccupant", _gridOccupantBuffer, GpuPassBindingAccess.Srv);
        BindRuntimeSquadBuffers(_solveCrowdKernel);
        _updateCompute.SetTexture(_solveCrowdKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_solveCrowdKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_solveCrowdKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindResolveTargetAcquisitionKernel()
    {
        SetGpuPassBuffer(_resolveTargetAcquisitionKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_resolveTargetAcquisitionKernel);
        SetGpuPassBuffer(_resolveTargetAcquisitionKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveTargetAcquisitionKernel, GridCounterBufferId, "_GridCounterBuffer", "gridCounter", _gridCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveTargetAcquisitionKernel, GridOccupantBufferId, "_GridOccupantBuffer", "gridOccupant", _gridOccupantBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveTargetAcquisitionKernel, TargetAcquisitionStateBufferId, "_TargetAcquisitionStateBuffer", "targetAcquisitionState", _targetAcquisitionStateBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_resolveTargetAcquisitionKernel, TargetAcquisitionCandidateBufferId, "_TargetAcquisitionCandidateBuffer", "targetAcquisitionCandidate", _targetAcquisitionCandidateBuffer, GpuPassBindingAccess.Uav);
        BindCombatActiveInstanceListBuffers(_resolveTargetAcquisitionKernel);
        BindRuntimeSquadBuffers(_resolveTargetAcquisitionKernel);
        _updateCompute.SetTexture(_resolveTargetAcquisitionKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_resolveTargetAcquisitionKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_resolveTargetAcquisitionKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindBuildTargetAcquisitionLosDispatchArgsKernel()
    {
        SetGpuPassBuffer(_buildTargetAcquisitionLosDispatchArgsKernel, CombatActiveInstanceCounterBufferId, "_CombatActiveInstanceCounterBuffer", "combatActiveInstanceCounter", _combatActiveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildTargetAcquisitionLosDispatchArgsKernel, TargetAcquisitionLosDispatchArgsBufferId, "_TargetAcquisitionLosDispatchArgsBuffer", "targetAcquisitionLosDispatchArgs", _targetAcquisitionLosDispatchArgsBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildSquadAcquisitionDispatchArgsKernel()
    {
        SetGpuPassBuffer(_buildSquadAcquisitionDispatchArgsKernel, CombatActiveInstanceCounterBufferId, "_CombatActiveInstanceCounterBuffer", "combatActiveInstanceCounter", _combatActiveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildSquadAcquisitionDispatchArgsKernel, SquadAcquisitionDispatchArgsBufferId, "_SquadAcquisitionDispatchArgsBuffer", "squadAcquisitionDispatchArgs", _squadAcquisitionDispatchArgsBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindEvaluateSquadAcquisitionCandidatesKernel()
    {
        BindSimulationReadBuffers(_evaluateSquadAcquisitionCandidatesKernel);
        SetGpuPassBuffer(_evaluateSquadAcquisitionCandidatesKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_evaluateSquadAcquisitionCandidatesKernel, TargetAcquisitionStateBufferId, "_TargetAcquisitionStateBuffer", "targetAcquisitionState", _targetAcquisitionStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_evaluateSquadAcquisitionCandidatesKernel, TargetAcquisitionCandidateBufferId, "_TargetAcquisitionCandidateBuffer", "targetAcquisitionCandidate", _targetAcquisitionCandidateBuffer, GpuPassBindingAccess.Uav);
        BindCombatActiveInstanceListBuffers(_evaluateSquadAcquisitionCandidatesKernel);
        BindRuntimeSquadBuffers(_evaluateSquadAcquisitionCandidatesKernel);
        BindCombatSquadCandidateBuffers(_evaluateSquadAcquisitionCandidatesKernel, GpuPassBindingAccess.Srv);
    }

    private void BindResolveTargetAcquisitionLineOfSightKernel()
    {
        BindSimulationReadBuffers(_resolveTargetAcquisitionLineOfSightKernel);
        SetGpuPassBuffer(_resolveTargetAcquisitionLineOfSightKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveTargetAcquisitionLineOfSightKernel, TargetAcquisitionCandidateBufferId, "_TargetAcquisitionCandidateBuffer", "targetAcquisitionCandidate", _targetAcquisitionCandidateBuffer, GpuPassBindingAccess.Uav);
        BindCombatActiveInstanceListBuffers(_resolveTargetAcquisitionLineOfSightKernel);
        _updateCompute.SetTexture(_resolveTargetAcquisitionLineOfSightKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_resolveTargetAcquisitionLineOfSightKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_resolveTargetAcquisitionLineOfSightKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindFinalizeTargetAcquisitionKernel()
    {
        SetGpuPassBuffer(_finalizeTargetAcquisitionKernel, TargetAcquisitionStateBufferId, "_TargetAcquisitionStateBuffer", "targetAcquisitionState", _targetAcquisitionStateBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_finalizeTargetAcquisitionKernel, TargetAcquisitionCandidateBufferId, "_TargetAcquisitionCandidateBuffer", "targetAcquisitionCandidate", _targetAcquisitionCandidateBuffer, GpuPassBindingAccess.Uav);
        BindCombatActiveInstanceListBuffers(_finalizeTargetAcquisitionKernel);
    }

    private void BindResolveInstanceCombatKernel()
    {
        SetGpuPassBuffer(_resolveInstanceCombatKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_resolveInstanceCombatKernel);
        SetGpuPassBuffer(_resolveInstanceCombatKernel, PhysicsActiveStateBufferId, "_PhysicsActiveStateBuffer", "physicsActiveState", _physicsActiveStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveInstanceCombatKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_resolveInstanceCombatKernel, GridCounterBufferId, "_GridCounterBuffer", "gridCounter", _gridCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveInstanceCombatKernel, GridOccupantBufferId, "_GridOccupantBuffer", "gridOccupant", _gridOccupantBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveInstanceCombatKernel, CombatStateBufferId, "_CombatStateBuffer", "combatState", _combatStateBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_resolveInstanceCombatKernel, TargetAcquisitionStateBufferId, "_TargetAcquisitionStateBuffer", "targetAcquisitionState", _targetAcquisitionStateBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_resolveInstanceCombatKernel, SquadAliveCountBufferId, "_SquadAliveCountBuffer", "squadAliveCount", _squadAliveCountBuffer, GpuPassBindingAccess.Srv);
        BindCombatActiveInstanceListReadBuffers(_resolveInstanceCombatKernel);
        BindRuntimeSquadBuffers(_resolveInstanceCombatKernel);
        _updateCompute.SetTexture(_resolveInstanceCombatKernel, TerrainHeightmapId, _resolvedTerrainHeightmap != null ? _resolvedTerrainHeightmap : Texture2D.blackTexture);
        _updateCompute.SetTexture(_resolveInstanceCombatKernel, StaticSdfTextureId, _resolvedStaticSdfTexture != null ? _resolvedStaticSdfTexture : GetFallbackStaticSdfTexture());
        _updateCompute.SetTexture(_resolveInstanceCombatKernel, EnvironmentDistanceFieldTextureId, _resolvedEnvironmentDistanceFieldTexture != null ? _resolvedEnvironmentDistanceFieldTexture : GetFallbackStaticSdfTexture());
    }

    private void BindAliveInstanceListBuffers(int kernel)
    {
        SetGpuPassBuffer(kernel, AliveInstanceIndexReadBufferId, "_AliveInstanceIndexReadBuffer", "aliveInstanceIndex", _aliveInstanceIndexBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(kernel, AliveInstanceCounterReadBufferId, "_AliveInstanceCounterReadBuffer", "aliveInstanceCounter", _aliveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
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
        SetGpuPassBuffer(kernel, SimulationPositionYawReadBufferId, "_SimulationPositionYawReadBuffer", "simulationPositionYaw.read", _simulationPositionYawReadBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(kernel, SimulationScaleReadBufferId, "_SimulationScaleReadBuffer", "simulationScale.read", _simulationScaleReadBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(kernel, SimulationVelocityReadBufferId, "_SimulationVelocityReadBuffer", "simulationVelocity.read", _simulationVelocityReadBuffer, GpuPassBindingAccess.Srv);
    }

    private void BindSimulationWriteBuffers(int kernel)
    {
        SetGpuPassBuffer(kernel, SimulationPositionYawWriteBufferId, "_SimulationPositionYawWriteBuffer", "simulationPositionYaw.write", _simulationPositionYawWriteBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(kernel, SimulationScaleWriteBufferId, "_SimulationScaleWriteBuffer", "simulationScale.write", _simulationScaleWriteBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(kernel, SimulationVelocityWriteBufferId, "_SimulationVelocityWriteBuffer", "simulationVelocity.write", _simulationVelocityWriteBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindSpatialElementBuffers(int kernel, GpuPassBindingAccess access)
    {
        SetGpuPassBuffer(kernel, SpatialCapsuleStartRadiusBufferId, "_SpatialCapsuleStartRadiusBuffer", "spatialCapsuleStartRadius", _spatialCapsuleStartRadiusBuffer, access);
        SetGpuPassBuffer(kernel, SpatialCapsuleEndHeightBufferId, "_SpatialCapsuleEndHeightBuffer", "spatialCapsuleEndHeight", _spatialCapsuleEndHeightBuffer, access);
        SetGpuPassBuffer(kernel, SpatialOwnerIndexBufferId, "_SpatialOwnerIndexBuffer", "spatialOwnerIndex", _spatialOwnerIndexBuffer, access);
        SetGpuPassBuffer(kernel, SpatialTargetMaskBufferId, "_SpatialTargetMaskBuffer", "spatialTargetMask", _spatialTargetMaskBuffer, access);
        SetGpuPassBuffer(kernel, SpatialFlagsBufferId, "_SpatialFlagsBuffer", "spatialFlags", _spatialFlagsBuffer, access);
        SetGpuPassBuffer(kernel, SpatialFactionBufferId, "_SpatialFactionBuffer", "spatialFaction", _spatialFactionBuffer, access);
    }

    private void BindPhysicsActiveInstanceListBuffers(int kernel, GpuPassBindingAccess access)
    {
        SetGpuPassBuffer(kernel, PhysicsActiveInstanceIndexBufferId, "_PhysicsActiveInstanceIndexBuffer", "physicsActiveInstanceIndex", _physicsActiveInstanceIndexBuffer, access);
        SetGpuPassBuffer(kernel, PhysicsActiveInstanceCounterBufferId, "_PhysicsActiveInstanceCounterBuffer", "physicsActiveInstanceCounter", _physicsActiveInstanceCounterBuffer, access);
    }

    private void BindPhysicsActiveInstanceListReadBuffers(int kernel)
    {
        SetGpuPassBuffer(kernel, PhysicsActiveInstanceIndexReadBufferId, "_PhysicsActiveInstanceIndexReadBuffer", "physicsActiveInstanceIndex", _physicsActiveInstanceIndexBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(kernel, PhysicsActiveInstanceCounterReadBufferId, "_PhysicsActiveInstanceCounterReadBuffer", "physicsActiveInstanceCounter", _physicsActiveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
    }

    private void BindCombatActiveInstanceListBuffers(int kernel)
    {
        SetGpuPassBuffer(kernel, CombatActiveInstanceIndexBufferId, "_CombatActiveInstanceIndexBuffer", "combatActiveInstanceIndex", _combatActiveInstanceIndexBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(kernel, CombatActiveInstanceCounterBufferId, "_CombatActiveInstanceCounterBuffer", "combatActiveInstanceCounter", _combatActiveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
    }

    private void BindCombatActiveInstanceListReadBuffers(int kernel)
    {
        SetGpuPassBuffer(kernel, CombatActiveInstanceIndexReadBufferId, "_CombatActiveInstanceIndexReadBuffer", "combatActiveInstanceIndex", _combatActiveInstanceIndexBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(kernel, CombatActiveInstanceCounterReadBufferId, "_CombatActiveInstanceCounterReadBuffer", "combatActiveInstanceCounter", _combatActiveInstanceCounterBuffer, GpuPassBindingAccess.Srv);
    }

    private void BindCombatSquadCandidateBuffers(int kernel, GpuPassBindingAccess access)
    {
        SetGpuPassBuffer(kernel, CombatSquadCandidateBufferId, "_CombatSquadCandidateBuffer", "combatSquadCandidate", _combatSquadCandidateBuffer, access);
        SetGpuPassBuffer(kernel, CombatSquadCandidateCounterBufferId, "_CombatSquadCandidateCounterBuffer", "combatSquadCandidateCounter", _combatSquadCandidateCounterBuffer, access);
    }

    private void BindClearVisibleRuntimeSquadBoundsKernel()
    {
        SetGpuPassBuffer(_clearVisibleRuntimeSquadBoundsKernel, RuntimeSquadBoundsBufferId, "_RuntimeSquadBoundsBuffer", "runtimeSquadBounds", _runtimeSquadBoundsBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_clearVisibleRuntimeSquadBoundsKernel, VisibleRuntimeSquadMaskBufferId, "_VisibleRuntimeSquadMaskBuffer", "visibleRuntimeSquadMask", _visibleRuntimeSquadMaskBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildVisibleRuntimeSquadBoundsKernel()
    {
        BindAliveInstanceListBuffers(_buildVisibleRuntimeSquadBoundsKernel);
        SetGpuPassBuffer(_buildVisibleRuntimeSquadBoundsKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_buildVisibleRuntimeSquadBoundsKernel);
        SetGpuPassBuffer(_buildVisibleRuntimeSquadBoundsKernel, AgentSquadDataBufferId, "_AgentSquadDataBuffer", "agentSquadData", _agentSquadDataBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildVisibleRuntimeSquadBoundsKernel, RuntimeSquadBoundsBufferId, "_RuntimeSquadBoundsBuffer", "runtimeSquadBounds", _runtimeSquadBoundsBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindCullVisibleRuntimeSquadsKernel()
    {
        SetGpuPassBuffer(_cullVisibleRuntimeSquadsKernel, RuntimeSquadBoundsBufferId, "_RuntimeSquadBoundsBuffer", "runtimeSquadBounds", _runtimeSquadBoundsBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_cullVisibleRuntimeSquadsKernel, VisibleRuntimeSquadMaskBufferId, "_VisibleRuntimeSquadMaskBuffer", "visibleRuntimeSquadMask", _visibleRuntimeSquadMaskBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindBuildVisibleRuntimeInstanceListKernel(bool renderAllAliveInstances = false)
    {
        BindAliveInstanceListBuffers(_buildVisibleRuntimeInstanceListKernel);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_buildVisibleRuntimeInstanceListKernel);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, AgentSquadDataBufferId, "_AgentSquadDataBuffer", "agentSquadData", _agentSquadDataBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleRuntimeSquadMaskBufferId, "_VisibleRuntimeSquadMaskBuffer", "visibleRuntimeSquadMask", _visibleRuntimeSquadMaskBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleInstanceIndicesId, "_VisibleInstanceIndices", "visibleInstanceIndex", _visibleInstanceIndexBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleInstanceCounterBufferId, "_VisibleInstanceCounterBuffer", "visibleInstanceCounter", _visibleInstanceCounterBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleLod1InstanceIndicesId, "_VisibleLod1InstanceIndices", "visibleLod1InstanceIndex", _visibleLod1InstanceIndexBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleLod1InstanceCounterBufferId, "_VisibleLod1InstanceCounterBuffer", "visibleLod1InstanceCounter", _visibleLod1InstanceCounterBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleLod2InstanceIndicesId, "_VisibleLod2InstanceIndices", "visibleLod2InstanceIndex", _visibleLod2InstanceIndexBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_buildVisibleRuntimeInstanceListKernel, VisibleLod2InstanceCounterBufferId, "_VisibleLod2InstanceCounterBuffer", "visibleLod2InstanceCounter", _visibleLod2InstanceCounterBuffer, GpuPassBindingAccess.Uav);
        _updateCompute.SetInt(VisibleRuntimeSquadCountId, renderAllAliveInstances ? 0 : _activeSquadStateCount);
        _updateCompute.SetInt(VisibleUnassignedInstancesId, renderAllAliveInstances || _visibleUnassignedInstancesThisFrame ? 1 : 0);
        SyncVisibleLodParameters();
    }

    private void BindBuildVisibleIndirectArgsKernel(
        GraphicsBuffer argsBuffer,
        Mesh mesh,
        int subMeshIndex,
        ComputeBuffer visibleCounterBuffer,
        string runtimeName)
    {
        SetGpuPassBuffer(_buildVisibleIndirectArgsKernel, VisibleInstanceCounterBufferId, "_VisibleInstanceCounterBuffer", runtimeName, visibleCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_buildVisibleIndirectArgsKernel, VisibleRenderArgsBufferId, "_VisibleRenderArgsBuffer", "visibleRenderArgs", argsBuffer, GpuPassBindingAccess.Uav);
        _updateCompute.SetInt(VisibleRenderArgsIndexCountId, (int)mesh.GetIndexCount(subMeshIndex));
        _updateCompute.SetInt(VisibleRenderArgsStartIndexId, (int)mesh.GetIndexStart(subMeshIndex));
        _updateCompute.SetInt(VisibleRenderArgsBaseVertexId, unchecked((int)mesh.GetBaseVertex(subMeshIndex)));
    }

    private void SyncVisibleLodParameters()
    {
        _updateCompute.SetInt(VisibleLodTierCountId, Mathf.Max(1, _activeVisibleLodTierCountThisFrame));
        _updateCompute.SetFloat(VisibleLod1StartDistanceId, _visibleLod1StartDistanceThisFrame);
        _updateCompute.SetFloat(VisibleLod2StartDistanceId, _visibleLod2StartDistanceThisFrame);
    }

    private void BindRuntimeSquadBuffers(int kernel)
    {
        SetGpuPassBuffer(kernel, SquadStateBufferId, "_SquadStateBuffer", "squadState", _squadStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(kernel, AgentSquadDataBufferId, "_AgentSquadDataBuffer", "agentSquadData", _agentSquadDataBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(kernel, FormationSlotBufferId, "_FormationSlotBuffer", "formationSlot", _formationSlotBuffer, GpuPassBindingAccess.Srv);
    }

    private void BindFinalizeKernel()
    {
        SetGpuPassBuffer(_finalizeKernel, SpawnDataId, "_SpawnData", "spawnData", _spawnDataBuffer, GpuPassBindingAccess.Srv);
        BindSimulationReadBuffers(_finalizeKernel);
        SetGpuPassBuffer(_finalizeKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_finalizeKernel, CombatStateBufferId, "_CombatStateBuffer", "combatState", _combatStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_finalizeKernel, AnimationClipMetadataBufferId, "_AnimationClipMetadataBuffer", "animationClipMetadata", _animationClipMetadataBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_finalizeKernel, InstanceAnimationStateBufferId, "_InstanceAnimationStateBuffer", "instanceAnimationState", _instanceAnimationStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_finalizeKernel, InstanceTransformsId, "_InstanceTransforms", "instanceTransforms", _instanceTransformBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_finalizeKernel, InstanceFrameDataId, "_InstanceFrameData", "instanceFrameData", _instanceFrameDataBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_finalizeKernel, InstanceFrameBlendDataId, "_InstanceFrameBlendData", "instanceFrameBlendData", _instanceFrameBlendDataBuffer, GpuPassBindingAccess.Uav);
        BindAliveInstanceListBuffers(_finalizeKernel);
    }

    private void BindClearSpatialQueriesKernel()
    {
        SetGpuPassBuffer(_clearSpatialQueriesKernel, SpatialQueryBufferId, "_SpatialQueryBuffer", "spatialQuery", _spatialQueryBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_clearSpatialQueriesKernel, SpatialQueryResultBufferId, "_SpatialQueryResultBuffer", "spatialQueryResult", _spatialQueryResultBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_clearSpatialQueriesKernel, SpatialQueryHitBufferId, "_SpatialQueryHitBuffer", "spatialQueryHit", _spatialQueryHitBuffer, GpuPassBindingAccess.Uav);
    }

    private void BindResolveSpatialQueriesKernel()
    {
        SetGpuPassBuffer(_resolveSpatialQueriesKernel, PhysicsActiveStateBufferId, "_PhysicsActiveStateBuffer", "physicsActiveState", _physicsActiveStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveSpatialQueriesKernel, DeathStateBufferId, "_DeathStateBuffer", "deathState", _deathStateBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveSpatialQueriesKernel, GridCounterBufferId, "_GridCounterBuffer", "gridCounter", _gridCounterBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveSpatialQueriesKernel, GridOccupantBufferId, "_GridOccupantBuffer", "gridOccupant", _gridOccupantBuffer, GpuPassBindingAccess.Srv);
        BindSpatialElementBuffers(_resolveSpatialQueriesKernel, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveSpatialQueriesKernel, SpatialQueryBufferId, "_SpatialQueryBuffer", "spatialQuery", _spatialQueryBuffer, GpuPassBindingAccess.Srv);
        SetGpuPassBuffer(_resolveSpatialQueriesKernel, SpatialQueryResultBufferId, "_SpatialQueryResultBuffer", "spatialQueryResult", _spatialQueryResultBuffer, GpuPassBindingAccess.Uav);
        SetGpuPassBuffer(_resolveSpatialQueriesKernel, SpatialQueryHitBufferId, "_SpatialQueryHitBuffer", "spatialQueryHit", _spatialQueryHitBuffer, GpuPassBindingAccess.Uav);
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
        EnsureRuntimeSquadBoundsBuffer();
        EnsureCombatSquadCandidateBuffers();
    }

    private void EnsureCombatSquadCandidateBuffers()
    {
        int squadCount = Mathf.Max(1, _activeSquadStateCount);
        int candidateCapacity = Mathf.Clamp(_combatCandidateCapacityPerSquad, 1, CombatCandidateCapacityPerSquadMax);
        int requiredCandidateCount = Mathf.Max(1, squadCount * candidateCapacity);

        if (_combatSquadCandidateBuffer == null || _combatSquadCandidateBuffer.count < requiredCandidateCount)
        {
            ReleaseBuffer(ref _combatSquadCandidateBuffer);
            _combatSquadCandidateBuffer = new ComputeBuffer(requiredCandidateCount, Marshal.SizeOf<CombatSquadCandidateData>());
        }

        if (_combatSquadCandidateCounterBuffer == null || _combatSquadCandidateCounterBuffer.count < squadCount)
        {
            ReleaseBuffer(ref _combatSquadCandidateCounterBuffer);
            _combatSquadCandidateCounterBuffer = new ComputeBuffer(squadCount, sizeof(uint));
            _combatSquadCandidateCounterBuffer.SetData(new uint[squadCount]);
        }

        EnsureCombatCandidateClusterWorksetBuffers(1);
    }

    private void EnsureCombatCandidateClusterWorksetBuffers(int requiredWorkItemCount)
    {
        int requiredCount = Mathf.Max(1, requiredWorkItemCount);
        if (_combatCandidateClusterWorkItemBuffer == null ||
            _combatCandidateClusterWorkItemBuffer.count < requiredCount)
        {
            ReleaseBuffer(ref _combatCandidateClusterWorkItemBuffer);
            _combatCandidateClusterWorkItemBuffer = new ComputeBuffer(
                requiredCount,
                Marshal.SizeOf<CombatCandidateClusterWorkItemData>());
        }

        if (_combatCandidateClusterDispatchArgsBuffer == null)
        {
            _combatCandidateClusterDispatchArgsBuffer = new ComputeBuffer(
                3,
                sizeof(uint),
                ComputeBufferType.IndirectArguments);
            _combatCandidateClusterDispatchArgsBuffer.SetData(EmptyDispatchArgsResetData);
        }
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

    private void EnsureRuntimeSquadBoundsBuffer()
    {
        int requiredCount = Mathf.Max(1, _activeSquadStateCount);
        if (_runtimeSquadBoundsBuffer == null || _runtimeSquadBoundsBuffer.count < requiredCount)
        {
            ReleaseBuffer(ref _runtimeSquadBoundsBuffer);
            _runtimeSquadBoundsBuffer = new ComputeBuffer(requiredCount, Marshal.SizeOf<RuntimeSquadBoundsGpuData>());
        }
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
        bool hasUnassignedInstances = _activeAgentSquadDataCount < _instanceCount;
        for (int cacheIndex = 0; cacheIndex < _agentSquadUploadCache.Length; cacheIndex++)
            _agentSquadUploadCache[cacheIndex] = invalidEntry;

        int uploadCount = Mathf.Min(_activeAgentSquadDataCount, _agentSquadUploadCache.Length);
        for (int instanceIndex = 0; instanceIndex < uploadCount; instanceIndex++)
        {
            CrowdVatAgentSquadAssignment assignment = _runtimeAgentSquadData[instanceIndex];
            if ((assignment.flags & CrowdVatSquadMemberFlags.Unassigned) != 0)
            {
                _agentSquadUploadCache[instanceIndex] = invalidEntry;
                hasUnassignedInstances = true;
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
        _hasUnassignedRuntimeInstances = hasUnassignedInstances;
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

    private void DispatchAllInstances(int kernel)
    {
        int threadGroupCount = Mathf.CeilToInt(_instanceCount / (float)ThreadGroupSize);
        DispatchGpuPass(kernel, threadGroupCount, 1, 1);
    }

    private void DispatchAliveInstancesIndirect(int kernel)
    {
        if (_aliveInstanceDispatchArgsBuffer == null)
            return;

        DispatchGpuPassIndirect(kernel, _aliveInstanceDispatchArgsBuffer);
    }

    private void DispatchPhysicsActiveInstancesIndirect(int kernel)
    {
        if (_physicsActiveInstanceDispatchArgsBuffer == null)
            return;

        DispatchGpuPassIndirect(kernel, _physicsActiveInstanceDispatchArgsBuffer);
    }

    private void DispatchCombatActiveInstancesIndirect(int kernel)
    {
        if (_combatActiveInstanceDispatchArgsBuffer == null)
            return;

        DispatchGpuPassIndirect(kernel, _combatActiveInstanceDispatchArgsBuffer);
    }

    private void DispatchTargetAcquisitionLosQueriesIndirect(int kernel)
    {
        if (_targetAcquisitionLosDispatchArgsBuffer == null)
            return;

        DispatchGpuPassIndirect(kernel, _targetAcquisitionLosDispatchArgsBuffer);
    }

    private void DispatchSquadAcquisitionIndirect(int kernel)
    {
        if (_squadAcquisitionDispatchArgsBuffer == null)
            return;

        DispatchGpuPassIndirect(kernel, _squadAcquisitionDispatchArgsBuffer);
    }

    private void DispatchCombatCandidateClustersIndirect(int kernel)
    {
        if (_combatCandidateClusterDispatchArgsBuffer == null)
            return;

        DispatchGpuPassIndirect(kernel, _combatCandidateClusterDispatchArgsBuffer);
    }

    private void DispatchSquads(int kernel, int squadCount)
    {
        if (squadCount <= 0)
            return;

        int threadGroupCount = Mathf.Max(1, Mathf.CeilToInt(squadCount / (float)ThreadGroupSize));
        DispatchGpuPass(kernel, threadGroupCount, 1, 1);
    }

    private void ClearGridForRebuild()
    {
        if (_gridClearDispatchArgsBuffer == null)
            return;

        BindBuildGridClearDispatchArgsKernel();
        DispatchGpuPass(_buildGridClearDispatchArgsKernel, 1, 1, 1);

        BindClearGridKernel();
        DispatchGridClearTouchedCells();
    }

    private void DispatchGridClearTouchedCells()
    {
        if (_gridClearDispatchArgsBuffer == null)
            return;

        DispatchGpuPassIndirect(_clearGridKernel, _gridClearDispatchArgsBuffer);
    }

    private void DispatchWakeGrid(int kernel)
    {
        int threadGroupCount = Mathf.CeilToInt(_wakeGridCellCount / (float)ThreadGroupSize);
        DispatchGpuPass(kernel, threadGroupCount, 1, 1);
    }

    private void DispatchSpatialQueries(int kernel, int queryCount)
    {
        int threadGroupCount = Mathf.CeilToInt(queryCount / (float)SpatialQueryThreadGroupSize);
        DispatchGpuPass(kernel, threadGroupCount, 1, 1);
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

    private void SwapGridTouchedBuffers()
    {
        ComputeBuffer buffer = _gridPrevTouchedCellBuffer;
        _gridPrevTouchedCellBuffer = _gridCurrTouchedCellBuffer;
        _gridCurrTouchedCellBuffer = buffer;

        buffer = _gridPrevTouchedCounterBuffer;
        _gridPrevTouchedCounterBuffer = _gridCurrTouchedCounterBuffer;
        _gridCurrTouchedCounterBuffer = buffer;
    }

}
