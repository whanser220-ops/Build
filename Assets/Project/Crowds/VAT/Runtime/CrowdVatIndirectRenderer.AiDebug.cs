using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public sealed partial class CrowdVatIndirectRenderer
{
    private const uint AiDebugDeathStateDeadFlag = 0x80000000u;
    private const uint AiDebugDeathStateDamageMask = 0x7fffffffu;
    private const int AiDebugGridNeighborPreviewCount = 8;
    private const int AiDebugMaxGpuStageTargets = 128;
    private const int AiDebugMaxGpuStagesPerFrame = 64;
    private const int AiDebugGpuStageThreadGroupSize = 64;
    private const int AiDebugMaxGpuDiscoveryCandidates = 128;
    private const float AiDebugDiscoveryDefaultHighSpeedThreshold = 6.0f;
    private const uint AiDebugDiscoveryFlagRegionSphere = 1u << 0;
    private const uint AiDebugDiscoveryFlagRegionBox = 1u << 1;
    private const uint AiDebugDiscoveryFlagScreenRect = 1u << 2;
    private const uint AiDebugDiscoveryFlagSquadFilter = 1u << 3;
    private const uint AiDebugDiscoveryFlagRequireAnomaly = 1u << 4;
    private CrowdVatAiDebugRuntimeSampler _aiDebugRuntimeSampler;
    private readonly List<int> _aiDebugGpuStageTargetScratch = new List<int>(AiDebugMaxGpuStageTargets);
    private readonly List<int> _aiDebugGpuDiscoveredCandidateTargets = new List<int>(AiDebugMaxGpuDiscoveryCandidates);
    private ComputeBuffer _aiDebugTargetIndexBuffer;
    private ComputeBuffer _aiDebugStageRecordBuffer;
    private ComputeBuffer _aiDebugCandidateCounterBuffer;
    private ComputeBuffer _aiDebugCandidateRecordBuffer;
    private ComputeBuffer _aiDebugDiscoveryHistoryBuffer;
    private uint[] _aiDebugTargetIndexUploadCache = Array.Empty<uint>();
    private uint[] _aiDebugCandidateCounterReadbackCache = Array.Empty<uint>();
    private CrowdVatAiDebugGpuStageRecord[] _aiDebugStageRecordReadbackCache = Array.Empty<CrowdVatAiDebugGpuStageRecord>();
    private CrowdVatAiDebugGpuCandidateRecord[] _aiDebugCandidateRecordReadbackCache = Array.Empty<CrowdVatAiDebugGpuCandidateRecord>();
    private CrowdVatAiDebugGpuDiscoveryHistoryRecord[] _aiDebugDiscoveryHistoryResetCache = Array.Empty<CrowdVatAiDebugGpuDiscoveryHistoryRecord>();
    private int _aiDebugGpuStageTargetCount;
    private int _aiDebugGpuStageRecordCount;
    private int _aiDebugGpuDiscoveryCandidateCapacity = AiDebugMaxGpuDiscoveryCandidates;
    private CrowdVatAiDebugDiscoverySettings _aiDebugDiscoverySettings = CrowdVatAiDebugDiscoverySettings.CreateDefault();
    private bool _aiDebugGpuStageCaptureActive;
    private bool _aiDebugGpuDiscoveryActive;

    [StructLayout(LayoutKind.Sequential)]
    private struct CrowdVatAiDebugGpuDiscoveryHistoryRecord
    {
        public Vector4 localPositionFrame;
        public Vector4 velocityStuck;
    }

    private static int DecodeAiDebugPackedIndex(uint packed, int shift, uint mask)
    {
        uint encoded = (packed >> shift) & mask;
        return encoded == 0u ? -1 : (int)encoded - 1;
    }

    private static int DecodeAiDebugShotHitAgent(uint packed)
    {
        return DecodeAiDebugPackedIndex(packed, 0, 0xffffu);
    }

    private static int DecodeAiDebugVisibleSampleIndex(uint packed)
    {
        return DecodeAiDebugPackedIndex(packed, 16, 0xffu);
    }

    private static bool DecodeAiDebugShotHitScene(uint packed)
    {
        return (packed & (1u << 24)) != 0u;
    }

    private static bool DecodeAiDebugShotBlockedBySceneBeforeAgent(uint packed)
    {
        return (packed & (1u << 25)) != 0u;
    }

    public void SetAiDebugRuntimeRecorder(CrowdVatAiDebugRecorder recorder)
    {
        if (recorder == null)
        {
            ClearAiDebugRuntimeRecorder(null);
            return;
        }

        if (_aiDebugRuntimeSampler == null)
            _aiDebugRuntimeSampler = gameObject.GetComponent<CrowdVatAiDebugRuntimeSampler>();

        if (_aiDebugRuntimeSampler == null)
        {
            _aiDebugRuntimeSampler = gameObject.AddComponent<CrowdVatAiDebugRuntimeSampler>();
            _aiDebugRuntimeSampler.hideFlags = HideFlags.HideInInspector | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        _aiDebugRuntimeSampler.Initialize(this, recorder);
    }

    public void ClearAiDebugRuntimeRecorder(CrowdVatAiDebugRecorder recorder)
    {
        if (_aiDebugRuntimeSampler == null)
            return;

        if (recorder != null && _aiDebugRuntimeSampler.Recorder != recorder)
            return;

        CrowdVatAiDebugRuntimeSampler sampler = _aiDebugRuntimeSampler;
        _aiDebugRuntimeSampler = null;
        sampler.Shutdown();
    }

    public void BeginAiDebugGpuStageCapture(
        IReadOnlyList<CrowdVatAiDebugTarget> targets,
        CrowdVatAiDebugDiscoverySettings discoverySettings)
    {
        _aiDebugDiscoverySettings = discoverySettings.Sanitized();
        _aiDebugGpuDiscoveryCandidateCapacity = Mathf.Clamp(
            _aiDebugDiscoverySettings.candidateCapacity,
            1,
            AiDebugMaxGpuDiscoveryCandidates);

        bool wantsDiscovery = WantsAiDebugGpuDiscovery(targets);
        _aiDebugGpuDiscoveryActive = wantsDiscovery &&
            _updateCompute != null &&
            _discoverAiDebugGpuCandidatesKernel >= 0 &&
            _instanceCount > 0;

        if (_aiDebugGpuDiscoveryActive)
            EnsureAiDebugGpuDiscoveryBuffers(_aiDebugGpuDiscoveryCandidateCapacity);
        else
            ReleaseAiDebugGpuDiscoveryBuffers();

        CollectAiDebugGpuStageTargets(targets, _aiDebugGpuStageTargetScratch, AiDebugMaxGpuStageTargets);
        if (_aiDebugGpuDiscoveryActive)
            AddAiDebugGpuDiscoveredCandidateTargets(_aiDebugGpuStageTargetScratch, AiDebugMaxGpuStageTargets);

        int targetCount = _aiDebugGpuStageTargetScratch.Count;
        if (targetCount <= 0 || _updateCompute == null || _captureAiDebugGpuStageKernel < 0 || _instanceCount <= 0)
        {
            _aiDebugGpuStageCaptureActive = false;
            _aiDebugGpuStageTargetCount = 0;
            _aiDebugGpuStageRecordCount = 0;
            ReleaseAiDebugGpuStageBuffers();
            return;
        }

        EnsureAiDebugGpuStageBuffers(targetCount);
        if (_aiDebugTargetIndexBuffer == null || _aiDebugStageRecordBuffer == null)
        {
            _aiDebugGpuStageCaptureActive = false;
            _aiDebugGpuStageTargetCount = 0;
            _aiDebugGpuStageRecordCount = 0;
            ReleaseAiDebugGpuStageBuffers();
            return;
        }

        if (_aiDebugTargetIndexUploadCache == null || _aiDebugTargetIndexUploadCache.Length < targetCount)
            _aiDebugTargetIndexUploadCache = new uint[targetCount];

        for (int index = 0; index < targetCount; index++)
            _aiDebugTargetIndexUploadCache[index] = (uint)_aiDebugGpuStageTargetScratch[index];

        _aiDebugTargetIndexBuffer.SetData(_aiDebugTargetIndexUploadCache, 0, 0, targetCount);
        _aiDebugGpuStageTargetCount = targetCount;
        _aiDebugGpuStageRecordCount = 0;
        _aiDebugGpuStageCaptureActive = true;
    }

    public void EndAiDebugGpuStageCapture()
    {
        _aiDebugGpuStageCaptureActive = false;
        _aiDebugGpuDiscoveryActive = false;
        _aiDebugGpuStageTargetCount = 0;
        _aiDebugGpuStageRecordCount = 0;
        _aiDebugGpuDiscoveredCandidateTargets.Clear();
        ReleaseAiDebugGpuStageBuffers();
        ReleaseAiDebugGpuDiscoveryBuffers();
    }

    public bool TryReadAiDebugGpuStageRecords(List<CrowdVatAiDebugGpuStageRecord> records)
    {
        if (records == null)
            throw new ArgumentNullException(nameof(records));

        records.Clear();
        if (!_aiDebugGpuStageCaptureActive ||
            _aiDebugStageRecordBuffer == null ||
            _aiDebugGpuStageRecordCount <= 0)
        {
            return false;
        }

        int readCount = Mathf.Min(_aiDebugGpuStageRecordCount, _aiDebugStageRecordBuffer.count);
        if (_aiDebugStageRecordReadbackCache == null || _aiDebugStageRecordReadbackCache.Length < readCount)
            _aiDebugStageRecordReadbackCache = new CrowdVatAiDebugGpuStageRecord[readCount];

        try
        {
            _aiDebugStageRecordBuffer.GetData(_aiDebugStageRecordReadbackCache, 0, 0, readCount);
            for (int index = 0; index < readCount; index++)
                records.Add(_aiDebugStageRecordReadbackCache[index]);
        }
        catch
        {
            records.Clear();
            return false;
        }
        finally
        {
            _aiDebugGpuStageRecordCount = 0;
        }

        return records.Count > 0;
    }

    public bool TryReadAiDebugGpuCandidateRecords(
        List<CrowdVatAiDebugGpuCandidateRecord> records,
        out int discoveredCount,
        out int discoveryCapacity)
    {
        if (records == null)
            throw new ArgumentNullException(nameof(records));

        records.Clear();
        discoveredCount = 0;
        discoveryCapacity = _aiDebugCandidateRecordBuffer != null ? _aiDebugCandidateRecordBuffer.count : 0;
        _aiDebugGpuDiscoveredCandidateTargets.Clear();
        if (!_aiDebugGpuDiscoveryActive ||
            _aiDebugCandidateCounterBuffer == null ||
            _aiDebugCandidateRecordBuffer == null)
        {
            return false;
        }

        if (_aiDebugCandidateCounterReadbackCache == null || _aiDebugCandidateCounterReadbackCache.Length < 1)
            _aiDebugCandidateCounterReadbackCache = new uint[1];

        try
        {
            _aiDebugCandidateCounterBuffer.GetData(_aiDebugCandidateCounterReadbackCache, 0, 0, 1);
            uint rawDiscoveredCount = _aiDebugCandidateCounterReadbackCache[0];
            discoveredCount = rawDiscoveredCount > int.MaxValue ? int.MaxValue : (int)rawDiscoveredCount;
            int readCount = Mathf.Min(discoveredCount, _aiDebugCandidateRecordBuffer.count);
            if (readCount <= 0)
                return true;

            if (_aiDebugCandidateRecordReadbackCache == null || _aiDebugCandidateRecordReadbackCache.Length < readCount)
                _aiDebugCandidateRecordReadbackCache = new CrowdVatAiDebugGpuCandidateRecord[readCount];

            _aiDebugCandidateRecordBuffer.GetData(_aiDebugCandidateRecordReadbackCache, 0, 0, readCount);
            for (int index = 0; index < readCount; index++)
            {
                CrowdVatAiDebugGpuCandidateRecord record = _aiDebugCandidateRecordReadbackCache[index];
                records.Add(record);
                if (record.valid != 0u)
                    AddAiDebugGpuStageTarget(_aiDebugGpuDiscoveredCandidateTargets, (int)record.instanceIndex, AiDebugMaxGpuDiscoveryCandidates);
            }
        }
        catch
        {
            records.Clear();
            discoveredCount = 0;
            _aiDebugGpuDiscoveredCandidateTargets.Clear();
            return false;
        }

        return true;
    }

    public bool TryGetAiDebugSystemSnapshot(out CrowdVatAiDebugSystemSnapshot snapshot)
    {
        snapshot = new CrowdVatAiDebugSystemSnapshot
        {
            instanceCount = _instanceCount,
            activeSquadCount = _activeSquadStateCount,
            activeAgentAssignmentCount = _activeAgentSquadDataCount,
            activeFormationSlotCount = _activeFormationSlotCount,
            gridCellCount = _gridCellCount,
            gridDimensions = _gridDimensions,
            gridMinXZ = _gridMinXZ,
            gridMaxXZ = _gridMaxXZ,
            gridCellSize = _queryCellSize,
            maxCellOccupancy = _maxCellOccupancy,
            solverIterations = _solverIterations,
            visibleInstanceCount = _visibleInstanceCount,
            hasVisibleBounds = _hasVisibleBounds,
            activeSpatialQueryCount = _activeSpatialQueryCount,
            maxSpatialQueries = _maxSpatialQueries,
            maxSpatialQueryHits = _maxHitsPerSpatialQuery,
            gpuCombatEnabled = _enableGpuInstanceCombat,
            approximateCollisionEnabled = _enableApproximateCollision,
            localAvoidanceEnabled = _enableLocalAvoidance,
            terrainCollisionEnabled = _enableTerrainCollision,
            staticSdfCollisionEnabled = _enableStaticSdfCollision,
            environmentDistanceFieldEnabled = _hasResolvedEnvironmentDistanceField,
            runtimeSquadAnchorsEnabled = HasRuntimeSquadAnchorData(),
            hasRuntimeRenderResource = _hasRuntimeRenderResource,
            hasClip = _hasClip,
            isPlayingClip = _isPlaying,
            aliveGpuCount = ReadSingleUInt(_aliveInstanceCounterBuffer),
            activeGpuCount = ReadSingleUInt(_activeInstanceCounterBuffer),
            visibleGpuCount = ReadSingleUInt(_visibleInstanceCounterBuffer)
        };
        return _instanceCount > 0;
    }

    public void GetAiDebugBufferSnapshots(List<CrowdVatAiDebugBufferSnapshot> snapshots)
    {
        if (snapshots == null)
            throw new ArgumentNullException(nameof(snapshots));

        snapshots.Clear();
        AddBufferSnapshot(snapshots, "spawn", _spawnDataBuffer);
        AddBufferSnapshot(snapshots, "sim.posYaw.read", _simulationPositionYawReadBuffer);
        AddBufferSnapshot(snapshots, "sim.posYaw.write", _simulationPositionYawWriteBuffer);
        AddBufferSnapshot(snapshots, "sim.scale.read", _simulationScaleReadBuffer);
        AddBufferSnapshot(snapshots, "sim.scale.write", _simulationScaleWriteBuffer);
        AddBufferSnapshot(snapshots, "sim.vel.read", _simulationVelocityReadBuffer);
        AddBufferSnapshot(snapshots, "sim.vel.write", _simulationVelocityWriteBuffer);
        AddBufferSnapshot(snapshots, "active", _activeStateBuffer);
        AddBufferSnapshot(snapshots, "death", _deathStateBuffer);
        AddBufferSnapshot(snapshots, "grid.counter", _gridCounterBuffer);
        AddBufferSnapshot(snapshots, "grid.occupant", _gridOccupantBuffer);
        AddBufferSnapshot(snapshots, "combat.state", _combatStateBuffer);
        AddBufferSnapshot(snapshots, "combat.acquisition", _targetAcquisitionStateBuffer);
        AddBufferSnapshot(snapshots, "aiDebug.stageTarget", _aiDebugTargetIndexBuffer);
        AddBufferSnapshot(snapshots, "aiDebug.stageRecord", _aiDebugStageRecordBuffer);
        AddBufferSnapshot(snapshots, "aiDebug.candidateCounter", _aiDebugCandidateCounterBuffer);
        AddBufferSnapshot(snapshots, "aiDebug.candidateRecord", _aiDebugCandidateRecordBuffer);
        AddBufferSnapshot(snapshots, "squad.state", _squadStateBuffer);
        AddBufferSnapshot(snapshots, "squad.alive", _squadAliveCountBuffer);
        AddBufferSnapshot(snapshots, "agent.squad", _agentSquadDataBuffer);
        AddBufferSnapshot(snapshots, "anim.state", _instanceAnimationStateBuffer);
        AddBufferSnapshot(snapshots, "visible.index", _visibleInstanceIndexBuffer);
    }

    public bool TryGetAiDebugSquadSnapshot(int runtimeSquadIndex, out CrowdVatAiDebugSquadSnapshot snapshot)
    {
        snapshot = default;
        if (runtimeSquadIndex < 0 ||
            runtimeSquadIndex >= _activeSquadStateCount ||
            _runtimeSquadStates == null ||
            runtimeSquadIndex >= _runtimeSquadStates.Length)
        {
            return false;
        }

        CrowdVatSquadState state = _runtimeSquadStates[runtimeSquadIndex];
        snapshot.valid = true;
        snapshot.runtimeSquadIndex = runtimeSquadIndex;
        snapshot.commandType = state.commandType;
        snapshot.formationType = state.formationType;
        snapshot.factionMask = state.factionMask;
        snapshot.flags = state.flags;
        snapshot.worldCenter = state.worldCenter;
        snapshot.worldForward = state.worldForward;
        snapshot.worldTarget = state.worldTarget;
        snapshot.formationSpacing = state.formationSpacing;
        snapshot.moveSpeed = state.moveSpeed;
        snapshot.anchorBlend = state.anchorBlend;
        snapshot.cohesionRadius = state.cohesionRadius;
        snapshot.cohesionStrength = state.cohesionStrength;
        snapshot.hasAliveCount = TryGetRuntimeSquadAliveCount(runtimeSquadIndex, out snapshot.aliveCount);
        return true;
    }

    public bool TryGetAiDebugAgentSnapshot(int runtimeInstanceIndex, out CrowdVatAiDebugAgentSnapshot snapshot)
    {
        snapshot = default;
        if (runtimeInstanceIndex < 0 || runtimeInstanceIndex >= _instanceCount)
            return false;

        if (!TryReadBufferElement(_simulationPositionYawReadBuffer, runtimeInstanceIndex, out Vector4 positionYaw) ||
            !TryReadBufferElement(_simulationScaleReadBuffer, runtimeInstanceIndex, out float scale) ||
            !TryReadBufferElement(_simulationVelocityReadBuffer, runtimeInstanceIndex, out Vector2 velocity))
        {
            return false;
        }

        snapshot.valid = true;
        snapshot.runtimeInstanceIndex = runtimeInstanceIndex;
        snapshot.localPosition = new Vector3(positionYaw.x, positionYaw.y, positionYaw.z);
        snapshot.worldPosition = transform.TransformPoint(snapshot.localPosition);
        snapshot.yawDegrees = Mathf.Repeat(positionYaw.w * Mathf.Rad2Deg, 360.0f);
        snapshot.localVelocity = velocity;
        snapshot.worldVelocity = transform.TransformVector(new Vector3(velocity.x, 0.0f, velocity.y));
        snapshot.speed = velocity.magnitude;
        snapshot.scale = scale;
        snapshot.runtimeSquadIndex = -1;
        snapshot.combatTargetIndex = -1;
        snapshot.combatVisibleSampleIndex = -1;
        snapshot.combatShotHitAgentIndex = -1;
        snapshot.acquisitionCurrentTargetIndex = -1;
        snapshot.acquisitionLastAttackerIndex = -1;
        snapshot.gridCellKey = -1;
        snapshot.gridNeighborPreview = Array.Empty<int>();

        if (TryReadBufferElement(_activeStateBuffer, runtimeInstanceIndex, out uint activeRaw))
        {
            snapshot.activeRaw = activeRaw;
            snapshot.active = activeRaw != 0u;
        }

        if (TryReadBufferElement(_deathStateBuffer, runtimeInstanceIndex, out uint deathRaw))
        {
            snapshot.deathRaw = deathRaw;
            snapshot.dead = (deathRaw & AiDebugDeathStateDeadFlag) != 0u;
            uint damageTaken = Math.Min(deathRaw & AiDebugDeathStateDamageMask, (uint)Mathf.RoundToInt(Mathf.Max(_combatMaxHealth, 1.0f)));
            snapshot.health = Mathf.Max(Mathf.Round(Mathf.Max(_combatMaxHealth, 1.0f)) - damageTaken, 0.0f);
        }

        if (TryReadBufferElement(_spawnDataBuffer, runtimeInstanceIndex, out InstanceSpawnData spawnData))
        {
            snapshot.spawnLocalPosition = spawnData.localPosition;
            snapshot.spawnYawDegrees = Mathf.Repeat(spawnData.yawRadians * Mathf.Rad2Deg, 360.0f);
            snapshot.faction = (CrowdVatFaction)spawnData.faction;
        }

        if (_runtimeAgentSquadData != null && runtimeInstanceIndex < _runtimeAgentSquadData.Length)
        {
            CrowdVatAgentSquadAssignment assignment = _runtimeAgentSquadData[runtimeInstanceIndex];
            if ((assignment.flags & CrowdVatSquadMemberFlags.Unassigned) == 0)
                snapshot.runtimeSquadIndex = (int)assignment.squadId;
            snapshot.slotIndex = assignment.slotIndex;
            snapshot.roleMask = assignment.roleMask;
            snapshot.memberFlags = assignment.flags;
        }

        FillAiDebugGridSnapshot(ref snapshot);
        FillAiDebugCombatSnapshot(ref snapshot);
        FillAiDebugTargetAcquisitionSnapshot(ref snapshot);
        FillAiDebugAnimationSnapshot(ref snapshot);
        return true;
    }

    private void FillAiDebugGridSnapshot(ref CrowdVatAiDebugAgentSnapshot snapshot)
    {
        if (_gridCounterBuffer == null ||
            _gridOccupantBuffer == null ||
            _gridDimensions.x <= 0 ||
            _gridDimensions.y <= 0 ||
            _queryCellSize <= 0.0f)
        {
            return;
        }

        Vector2 localXZ = new Vector2(snapshot.localPosition.x, snapshot.localPosition.z);
        Vector2 normalized = (localXZ - _gridMinXZ) / _queryCellSize;
        int cellX = Mathf.FloorToInt(normalized.x);
        int cellY = Mathf.FloorToInt(normalized.y);
        if (cellX < 0 || cellY < 0 || cellX >= _gridDimensions.x || cellY >= _gridDimensions.y)
            return;

        int cellKey = cellY * _gridDimensions.x + cellX;
        uint occupantCountRaw = 0u;
        if (!TryReadBufferElement(_gridCounterBuffer, cellKey, out occupantCountRaw))
            return;

        int clampedOccupantCount = Mathf.Min((int)occupantCountRaw, Mathf.Max(_maxCellOccupancy, 0));
        int previewCount = Mathf.Min(clampedOccupantCount, AiDebugGridNeighborPreviewCount);
        int[] neighbors = Array.Empty<int>();
        if (previewCount > 0)
        {
            uint[] rawNeighbors = new uint[previewCount];
            if (TryReadBufferRange(_gridOccupantBuffer, cellKey * _maxCellOccupancy, rawNeighbors, previewCount))
            {
                neighbors = new int[previewCount];
                for (int index = 0; index < previewCount; index++)
                    neighbors[index] = (int)rawNeighbors[index];
            }
        }

        snapshot.gridValid = true;
        snapshot.gridCell = new Vector2Int(cellX, cellY);
        snapshot.gridCellKey = cellKey;
        snapshot.gridOccupantCount = (int)occupantCountRaw;
        snapshot.gridOverflow = occupantCountRaw > (uint)Mathf.Max(_maxCellOccupancy, 0);
        snapshot.gridNeighborPreview = neighbors;
    }

    private void FillAiDebugCombatSnapshot(ref CrowdVatAiDebugAgentSnapshot snapshot)
    {
        if (!_enableGpuInstanceCombat ||
            !TryReadBufferElement(_combatStateBuffer, snapshot.runtimeInstanceIndex, out InstanceCombatStateData combatState))
        {
            return;
        }

        uint flags = combatState.flags;
        snapshot.combatAvailable = true;
        snapshot.combatHasTarget = (flags & InstanceCombatFlagHasTarget) != 0u;
        snapshot.combatHasLineOfSight = (flags & InstanceCombatFlagHasLineOfSight) != 0u;
        snapshot.combatFiredThisFrame = (flags & InstanceCombatFlagFiredThisFrame) != 0u;
        snapshot.combatTargetIndex = snapshot.combatHasTarget ? combatState.targetIndex : -1;
        snapshot.combatDistance = combatState.localOriginAndDistance.w >= 1e19f
            ? float.PositiveInfinity
            : combatState.localOriginAndDistance.w;
        snapshot.combatCooldown = combatState.localTargetAndCooldown.w;
        snapshot.combatHealth = combatState.healthAndHitFeedback.x;
        snapshot.combatNormalizedHealth = combatState.healthAndHitFeedback.w;
        snapshot.combatHitFlash = combatState.healthAndHitFeedback.y;
        snapshot.combatLastDamage = combatState.healthAndHitFeedback.z;
        snapshot.combatMuzzleFlash = combatState.muzzleFlash;
        snapshot.combatHasImpact = combatState.localImpactNormalAndHit.w > 0.001f;
        snapshot.combatVisibleSampleIndex = DecodeAiDebugVisibleSampleIndex(combatState.debugShotInfoPacked);
        snapshot.combatShotHitAgentIndex = DecodeAiDebugShotHitAgent(combatState.debugShotInfoPacked);
        snapshot.combatShotHitScene = DecodeAiDebugShotHitScene(combatState.debugShotInfoPacked);
        snapshot.combatShotBlockedBySceneBeforeAgent = DecodeAiDebugShotBlockedBySceneBeforeAgent(combatState.debugShotInfoPacked);

        Vector3 localOrigin = new Vector3(
            combatState.localOriginAndDistance.x,
            combatState.localOriginAndDistance.y,
            combatState.localOriginAndDistance.z);
        Vector3 localTarget = new Vector3(
            combatState.localTargetAndCooldown.x,
            combatState.localTargetAndCooldown.y,
            combatState.localTargetAndCooldown.z);
        Vector3 localImpactNormal = new Vector3(
            combatState.localImpactNormalAndHit.x,
            combatState.localImpactNormalAndHit.y,
            combatState.localImpactNormalAndHit.z);
        snapshot.combatWorldOrigin = transform.TransformPoint(localOrigin);
        snapshot.combatWorldTarget = transform.TransformPoint(localTarget);
        snapshot.combatWorldImpactNormal = snapshot.combatHasImpact && localImpactNormal.sqrMagnitude > 1e-8f
            ? transform.TransformDirection(localImpactNormal).normalized
            : Vector3.zero;

        if (snapshot.combatHealth > 0.0f || snapshot.combatAvailable)
            snapshot.health = snapshot.combatHealth;
    }

    private void FillAiDebugTargetAcquisitionSnapshot(ref CrowdVatAiDebugAgentSnapshot snapshot)
    {
        if (!TryReadBufferElement(_targetAcquisitionStateBuffer, snapshot.runtimeInstanceIndex, out TargetAcquisitionStateData acquisitionState))
            return;

        snapshot.targetAcquisitionAvailable = true;
        snapshot.acquisitionCurrentTargetIndex = acquisitionState.currentTargetIndex;
        snapshot.acquisitionLastAttackerIndex = acquisitionState.lastAttackerIndex;
        snapshot.acquisitionFlags = acquisitionState.flags;
        snapshot.acquisitionLastKnownTargetLocal = new Vector3(
            acquisitionState.lastKnownTargetAndScore.x,
            acquisitionState.lastKnownTargetAndScore.y,
            acquisitionState.lastKnownTargetAndScore.z);
        snapshot.acquisitionScore = acquisitionState.lastKnownTargetAndScore.w;
        snapshot.acquisitionTimers = acquisitionState.timers;
        snapshot.acquisitionRejectCounts0 = acquisitionState.debugRejectCounts0;
        snapshot.acquisitionRejectCounts1 = acquisitionState.debugRejectCounts1;
        if (snapshot.combatVisibleSampleIndex < 0)
            snapshot.combatVisibleSampleIndex = acquisitionState.debugVisibleSampleIndex;
    }

    private void FillAiDebugAnimationSnapshot(ref CrowdVatAiDebugAgentSnapshot snapshot)
    {
        if (_instanceAnimationStateCpuCache == null || snapshot.runtimeInstanceIndex >= _instanceAnimationStateCpuCache.Length)
            return;

        InstanceAnimationStateCpuData animationState = _instanceAnimationStateCpuCache[snapshot.runtimeInstanceIndex];
        snapshot.animationAvailable = true;
        snapshot.animationCurrentClipIndex = animationState.currentClipIndex;
        snapshot.animationNextClipIndex = animationState.nextClipIndex;
        snapshot.animationCurrentClipTime = animationState.currentClipTime;
        snapshot.animationNextClipTime = animationState.nextClipTime;
        snapshot.animationTransitionElapsed = animationState.transitionElapsed;
        snapshot.animationTransitionDuration = animationState.transitionDuration;
        snapshot.animationPlaybackSpeedMultiplier = animationState.playbackSpeedMultiplier;
        snapshot.animationIsBlending = animationState.isBlending;
    }

    private void BeginAiDebugGpuFrameCapture()
    {
        if (!_aiDebugGpuStageCaptureActive)
            return;

        if (_aiDebugTargetIndexBuffer == null ||
            _aiDebugStageRecordBuffer == null ||
            _aiDebugGpuStageTargetCount <= 0 ||
            _captureAiDebugGpuStageKernel < 0)
        {
            _aiDebugGpuStageCaptureActive = false;
            _aiDebugGpuStageRecordCount = 0;
            return;
        }

        _aiDebugGpuStageRecordCount = 0;
    }

    private void CaptureAiDebugGpuStage(CrowdVatAiDebugGpuStageId stageId, int solverIteration)
    {
        if (!_aiDebugGpuStageCaptureActive ||
            _aiDebugTargetIndexBuffer == null ||
            _aiDebugStageRecordBuffer == null ||
            _aiDebugGpuStageTargetCount <= 0 ||
            _captureAiDebugGpuStageKernel < 0)
        {
            return;
        }

        int nextRecordCount = _aiDebugGpuStageRecordCount + _aiDebugGpuStageTargetCount;
        if (nextRecordCount > _aiDebugStageRecordBuffer.count)
            return;

        BindAiDebugGpuStageKernel(stageId, solverIteration, _aiDebugGpuStageRecordCount);
        int threadGroups = Mathf.CeilToInt(_aiDebugGpuStageTargetCount / (float)AiDebugGpuStageThreadGroupSize);
        _updateCompute.Dispatch(_captureAiDebugGpuStageKernel, threadGroups, 1, 1);
        _aiDebugGpuStageRecordCount = nextRecordCount;
    }

    private void CaptureAiDebugGpuCandidates()
    {
        if (!_aiDebugGpuDiscoveryActive ||
            _aiDebugCandidateCounterBuffer == null ||
            _aiDebugCandidateRecordBuffer == null ||
            _aiDebugDiscoveryHistoryBuffer == null ||
            _discoverAiDebugGpuCandidatesKernel < 0 ||
            _instanceCount <= 0)
        {
            return;
        }

        _aiDebugCandidateCounterBuffer.SetData(AliveInstanceCounterResetData);
        BindAiDebugGpuDiscoveryKernel();
        int threadGroups = Mathf.CeilToInt(_instanceCount / (float)AiDebugGpuStageThreadGroupSize);
        _updateCompute.Dispatch(_discoverAiDebugGpuCandidatesKernel, threadGroups, 1, 1);
    }

    private void BindAiDebugGpuStageKernel(CrowdVatAiDebugGpuStageId stageId, int solverIteration, int writeOffset)
    {
        BindSimulationReadBuffers(_captureAiDebugGpuStageKernel);
        _updateCompute.SetBuffer(_captureAiDebugGpuStageKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_captureAiDebugGpuStageKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_captureAiDebugGpuStageKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_captureAiDebugGpuStageKernel, GridOccupantBufferId, _gridOccupantBuffer);
        _updateCompute.SetBuffer(_captureAiDebugGpuStageKernel, CombatStateBufferId, _combatStateBuffer);
        _updateCompute.SetBuffer(_captureAiDebugGpuStageKernel, TargetAcquisitionStateBufferId, _targetAcquisitionStateBuffer);
        _updateCompute.SetBuffer(_captureAiDebugGpuStageKernel, AiDebugTargetIndexBufferId, _aiDebugTargetIndexBuffer);
        _updateCompute.SetBuffer(_captureAiDebugGpuStageKernel, AiDebugStageRecordBufferId, _aiDebugStageRecordBuffer);
        _updateCompute.SetInt(AiDebugTargetCountId, _aiDebugGpuStageTargetCount);
        _updateCompute.SetInt(AiDebugStageId, (int)stageId);
        _updateCompute.SetInt(AiDebugFrameIndexId, Application.isPlaying ? Time.frameCount : 0);
        _updateCompute.SetInt(AiDebugSolverIterationId, solverIteration);
        _updateCompute.SetInt(AiDebugStageWriteOffsetId, writeOffset);
    }

    private void BindAiDebugGpuDiscoveryKernel()
    {
        BindSimulationReadBuffers(_discoverAiDebugGpuCandidatesKernel);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, ActiveStateBufferId, _activeStateBuffer);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, DeathStateBufferId, _deathStateBuffer);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, GridCounterBufferId, _gridCounterBuffer);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, GridOccupantBufferId, _gridOccupantBuffer);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, CombatStateBufferId, _combatStateBuffer);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, TargetAcquisitionStateBufferId, _targetAcquisitionStateBuffer);
        if (_agentSquadDataBuffer != null)
            _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, AgentSquadDataBufferId, _agentSquadDataBuffer);
        _updateCompute.SetInt(AgentSquadDataCountId, _agentSquadDataBuffer != null ? _activeAgentSquadDataCount : 0);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, AiDebugCandidateCounterBufferId, _aiDebugCandidateCounterBuffer);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, AiDebugCandidateRecordBufferId, _aiDebugCandidateRecordBuffer);
        _updateCompute.SetBuffer(_discoverAiDebugGpuCandidatesKernel, AiDebugDiscoveryHistoryBufferId, _aiDebugDiscoveryHistoryBuffer);
        _updateCompute.SetInt(AiDebugCandidateCapacityId, _aiDebugCandidateRecordBuffer != null ? _aiDebugCandidateRecordBuffer.count : 0);
        _updateCompute.SetInt(AiDebugDiscoveryFrameIndexId, Application.isPlaying ? Time.frameCount : 0);
        _updateCompute.SetInt(AiDebugDiscoveryFlagsId, (int)BuildAiDebugDiscoveryFlags(_aiDebugDiscoverySettings));
        _updateCompute.SetVector(AiDebugDiscoveryCenterRadiusId, BuildAiDebugDiscoveryCenterRadius(_aiDebugDiscoverySettings));
        _updateCompute.SetVector(AiDebugDiscoveryBoxCenterId, transform.InverseTransformPoint(_aiDebugDiscoverySettings.worldBoxCenter));
        _updateCompute.SetVector(AiDebugDiscoveryBoxExtentsId, BuildAiDebugDiscoveryLocalBoxExtents(_aiDebugDiscoverySettings.worldBoxExtents));
        _updateCompute.SetVector(AiDebugDiscoveryScreenRectId, BuildAiDebugDiscoveryScreenRect(_aiDebugDiscoverySettings.screenRect01));
        SetAiDebugDiscoveryWorldToClip(_aiDebugDiscoverySettings.worldToClip);
        _updateCompute.SetInt(AiDebugDiscoverySquadId, _aiDebugDiscoverySettings.squadId);
        _updateCompute.SetFloat(AiDebugDiscoveryHighSpeedThresholdId, _aiDebugDiscoverySettings.highSpeedThreshold);
        _updateCompute.SetFloat(AiDebugDiscoveryInactiveMovingSpeedThresholdId, _aiDebugDiscoverySettings.inactiveMovingSpeedThreshold);
        _updateCompute.SetFloat(AiDebugDiscoverySpeedSpikeThresholdId, _aiDebugDiscoverySettings.speedSpikeThreshold);
        _updateCompute.SetFloat(AiDebugDiscoveryPositionJumpThresholdId, _aiDebugDiscoverySettings.positionJumpThreshold);
        _updateCompute.SetFloat(AiDebugDiscoveryStuckSpeedThresholdId, _aiDebugDiscoverySettings.stuckSpeedThreshold);
        _updateCompute.SetFloat(AiDebugDiscoveryStuckMoveThresholdId, _aiDebugDiscoverySettings.stuckMoveThreshold);
        _updateCompute.SetFloat(AiDebugDiscoveryStuckIntentSpeedThresholdId, _aiDebugDiscoverySettings.stuckIntentSpeedThreshold);
        _updateCompute.SetInt(AiDebugDiscoveryStuckFrameThresholdId, _aiDebugDiscoverySettings.stuckFrameThreshold);
    }

    private static uint BuildAiDebugDiscoveryFlags(CrowdVatAiDebugDiscoverySettings settings)
    {
        uint flags = 0u;
        switch (settings.mode)
        {
            case CrowdVatAiDebugDiscoveryMode.RegionSphere:
                flags |= AiDebugDiscoveryFlagRegionSphere;
                break;
            case CrowdVatAiDebugDiscoveryMode.RegionBox:
                flags |= AiDebugDiscoveryFlagRegionBox;
                break;
            case CrowdVatAiDebugDiscoveryMode.ScreenRect:
                if (settings.hasWorldToClip)
                    flags |= AiDebugDiscoveryFlagScreenRect;
                break;
            case CrowdVatAiDebugDiscoveryMode.SquadAll:
                flags |= AiDebugDiscoveryFlagSquadFilter;
                break;
            case CrowdVatAiDebugDiscoveryMode.SquadAnomaly:
                flags |= AiDebugDiscoveryFlagSquadFilter | AiDebugDiscoveryFlagRequireAnomaly;
                break;
        }

        return flags;
    }

    private Vector4 BuildAiDebugDiscoveryCenterRadius(CrowdVatAiDebugDiscoverySettings settings)
    {
        Vector3 localCenter = transform.InverseTransformPoint(settings.worldCenter);
        float localRadius = settings.radius;
        Vector3 scale = transform.lossyScale;
        float maxHorizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 1e-5f);
        localRadius /= maxHorizontalScale;
        return new Vector4(localCenter.x, localCenter.y, localCenter.z, Mathf.Max(localRadius, 0.0f));
    }

    private Vector4 BuildAiDebugDiscoveryLocalBoxExtents(Vector3 worldExtents)
    {
        Vector3 scale = transform.lossyScale;
        return new Vector4(
            Mathf.Abs(worldExtents.x) / Mathf.Max(Mathf.Abs(scale.x), 1e-5f),
            Mathf.Abs(worldExtents.y) / Mathf.Max(Mathf.Abs(scale.y), 1e-5f),
            Mathf.Abs(worldExtents.z) / Mathf.Max(Mathf.Abs(scale.z), 1e-5f),
            0.0f);
    }

    private static Vector4 BuildAiDebugDiscoveryScreenRect(Rect screenRect01)
    {
        return new Vector4(screenRect01.xMin, screenRect01.yMin, screenRect01.xMax, screenRect01.yMax);
    }

    private void SetAiDebugDiscoveryWorldToClip(Matrix4x4 worldToClip)
    {
        _updateCompute.SetVector(AiDebugDiscoveryWorldToClipRow0Id, new Vector4(worldToClip.m00, worldToClip.m01, worldToClip.m02, worldToClip.m03));
        _updateCompute.SetVector(AiDebugDiscoveryWorldToClipRow1Id, new Vector4(worldToClip.m10, worldToClip.m11, worldToClip.m12, worldToClip.m13));
        _updateCompute.SetVector(AiDebugDiscoveryWorldToClipRow2Id, new Vector4(worldToClip.m20, worldToClip.m21, worldToClip.m22, worldToClip.m23));
        _updateCompute.SetVector(AiDebugDiscoveryWorldToClipRow3Id, new Vector4(worldToClip.m30, worldToClip.m31, worldToClip.m32, worldToClip.m33));
    }

    private void EnsureAiDebugGpuStageBuffers(int targetCount)
    {
        int safeTargetCount = Mathf.Clamp(targetCount, 1, AiDebugMaxGpuStageTargets);
        int recordCapacity = safeTargetCount * AiDebugMaxGpuStagesPerFrame;

        if (_aiDebugTargetIndexBuffer == null || _aiDebugTargetIndexBuffer.count != safeTargetCount)
        {
            ReleaseBuffer(ref _aiDebugTargetIndexBuffer);
            _aiDebugTargetIndexBuffer = new ComputeBuffer(safeTargetCount, sizeof(uint));
        }

        int recordStride = Marshal.SizeOf<CrowdVatAiDebugGpuStageRecord>();
        if (_aiDebugStageRecordBuffer == null ||
            _aiDebugStageRecordBuffer.count != recordCapacity ||
            _aiDebugStageRecordBuffer.stride != recordStride)
        {
            ReleaseBuffer(ref _aiDebugStageRecordBuffer);
            _aiDebugStageRecordBuffer = new ComputeBuffer(recordCapacity, recordStride);
        }
    }

    private void EnsureAiDebugGpuDiscoveryBuffers(int candidateCapacity)
    {
        int safeCandidateCapacity = Mathf.Clamp(candidateCapacity, 1, AiDebugMaxGpuDiscoveryCandidates);
        if (_aiDebugCandidateCounterBuffer == null || _aiDebugCandidateCounterBuffer.count != 1)
        {
            ReleaseBuffer(ref _aiDebugCandidateCounterBuffer);
            _aiDebugCandidateCounterBuffer = new ComputeBuffer(1, sizeof(uint));
            _aiDebugCandidateCounterBuffer.SetData(AliveInstanceCounterResetData);
        }

        int recordStride = Marshal.SizeOf<CrowdVatAiDebugGpuCandidateRecord>();
        if (_aiDebugCandidateRecordBuffer == null ||
            _aiDebugCandidateRecordBuffer.count != safeCandidateCapacity ||
            _aiDebugCandidateRecordBuffer.stride != recordStride)
        {
            ReleaseBuffer(ref _aiDebugCandidateRecordBuffer);
            _aiDebugCandidateRecordBuffer = new ComputeBuffer(safeCandidateCapacity, recordStride);
        }

        int historyStride = Marshal.SizeOf<CrowdVatAiDebugGpuDiscoveryHistoryRecord>();
        int historyCount = Mathf.Max(_instanceCount, 1);
        if (_aiDebugDiscoveryHistoryBuffer == null ||
            _aiDebugDiscoveryHistoryBuffer.count != historyCount ||
            _aiDebugDiscoveryHistoryBuffer.stride != historyStride)
        {
            ReleaseBuffer(ref _aiDebugDiscoveryHistoryBuffer);
            _aiDebugDiscoveryHistoryBuffer = new ComputeBuffer(historyCount, historyStride);
            if (_aiDebugDiscoveryHistoryResetCache == null || _aiDebugDiscoveryHistoryResetCache.Length < historyCount)
                _aiDebugDiscoveryHistoryResetCache = new CrowdVatAiDebugGpuDiscoveryHistoryRecord[historyCount];
            Array.Clear(_aiDebugDiscoveryHistoryResetCache, 0, historyCount);
            _aiDebugDiscoveryHistoryBuffer.SetData(_aiDebugDiscoveryHistoryResetCache, 0, 0, historyCount);
        }
    }

    private void ReleaseAiDebugGpuStageBuffers()
    {
        ReleaseBuffer(ref _aiDebugTargetIndexBuffer);
        ReleaseBuffer(ref _aiDebugStageRecordBuffer);
        _aiDebugTargetIndexUploadCache = Array.Empty<uint>();
        _aiDebugStageRecordReadbackCache = Array.Empty<CrowdVatAiDebugGpuStageRecord>();
    }

    private void ReleaseAiDebugGpuDiscoveryBuffers()
    {
        ReleaseBuffer(ref _aiDebugCandidateCounterBuffer);
        ReleaseBuffer(ref _aiDebugCandidateRecordBuffer);
        ReleaseBuffer(ref _aiDebugDiscoveryHistoryBuffer);
        _aiDebugGpuDiscoveredCandidateTargets.Clear();
        _aiDebugCandidateCounterReadbackCache = Array.Empty<uint>();
        _aiDebugCandidateRecordReadbackCache = Array.Empty<CrowdVatAiDebugGpuCandidateRecord>();
        _aiDebugDiscoveryHistoryResetCache = Array.Empty<CrowdVatAiDebugGpuDiscoveryHistoryRecord>();
    }

    private void CollectAiDebugGpuStageTargets(
        IReadOnlyList<CrowdVatAiDebugTarget> targets,
        List<int> result,
        int maxCount)
    {
        result.Clear();
        if (targets == null || maxCount <= 0)
            return;

        for (int targetIndex = 0; targetIndex < targets.Count && result.Count < maxCount; targetIndex++)
        {
            CrowdVatAiDebugTarget target = targets[targetIndex];
            switch (target.kind)
            {
                case CrowdVatAiDebugTargetKind.RuntimeId:
                    AddAiDebugGpuStageTarget(result, target.id, maxCount);
                    break;
                case CrowdVatAiDebugTargetKind.SquadId:
                    AddAiDebugGpuStageSquadTargets(result, target.id, maxCount);
                    break;
                case CrowdVatAiDebugTargetKind.GpuDiscovery:
                    break;
            }
        }
    }

    private static bool WantsAiDebugGpuDiscovery(IReadOnlyList<CrowdVatAiDebugTarget> targets)
    {
        if (targets == null)
            return false;

        for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            if (targets[targetIndex].kind == CrowdVatAiDebugTargetKind.GpuDiscovery)
                return true;
        }

        return false;
    }

    private void AddAiDebugGpuDiscoveredCandidateTargets(List<int> result, int maxCount)
    {
        for (int index = 0; index < _aiDebugGpuDiscoveredCandidateTargets.Count && result.Count < maxCount; index++)
            AddAiDebugGpuStageTarget(result, _aiDebugGpuDiscoveredCandidateTargets[index], maxCount);
    }

    private void AddAiDebugGpuStageSquadTargets(List<int> result, int runtimeSquadIndex, int maxCount)
    {
        if (runtimeSquadIndex < 0 || _runtimeAgentSquadData == null)
            return;

        int assignmentCount = Mathf.Min(_activeAgentSquadDataCount, _runtimeAgentSquadData.Length, _instanceCount);
        uint targetSquadId = (uint)runtimeSquadIndex;
        for (int instanceIndex = 0; instanceIndex < assignmentCount && result.Count < maxCount; instanceIndex++)
        {
            CrowdVatAgentSquadAssignment assignment = _runtimeAgentSquadData[instanceIndex];
            if ((assignment.flags & CrowdVatSquadMemberFlags.Unassigned) != 0 || assignment.squadId != targetSquadId)
                continue;

            AddAiDebugGpuStageTarget(result, instanceIndex, maxCount);
        }
    }

    private void AddAiDebugGpuStageTarget(List<int> result, int runtimeInstanceIndex, int maxCount)
    {
        if (runtimeInstanceIndex < 0 || runtimeInstanceIndex >= _instanceCount || result.Count >= maxCount)
            return;

        if (!result.Contains(runtimeInstanceIndex))
            result.Add(runtimeInstanceIndex);
    }

    private static void AddBufferSnapshot(List<CrowdVatAiDebugBufferSnapshot> snapshots, string name, ComputeBuffer buffer)
    {
        snapshots.Add(new CrowdVatAiDebugBufferSnapshot
        {
            name = name,
            bound = buffer != null,
            count = buffer != null ? buffer.count : 0,
            stride = buffer != null ? buffer.stride : 0
        });
    }

    private static uint ReadSingleUInt(ComputeBuffer buffer)
    {
        if (buffer == null || buffer.count <= 0)
            return 0u;

        uint[] value = new uint[1];
        try
        {
            buffer.GetData(value, 0, 0, 1);
            return value[0];
        }
        catch
        {
            return 0u;
        }
    }

    private static bool TryReadBufferElement<T>(ComputeBuffer buffer, int index, out T value) where T : struct
    {
        value = default;
        if (buffer == null || index < 0 || index >= buffer.count)
            return false;

        T[] values = new T[1];
        try
        {
            buffer.GetData(values, 0, index, 1);
            value = values[0];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadBufferRange<T>(ComputeBuffer buffer, int startIndex, T[] values, int count) where T : struct
    {
        if (buffer == null || values == null || count <= 0 || startIndex < 0 || startIndex + count > buffer.count || count > values.Length)
            return false;

        try
        {
            buffer.GetData(values, 0, startIndex, count);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class CrowdVatAiDebugRuntimeSampler : MonoBehaviour
{
    private CrowdVatIndirectRenderer _renderer;
    private CrowdVatAiDebugRecorder _recorder;

    public CrowdVatAiDebugRecorder Recorder => _recorder;

    public void Initialize(CrowdVatIndirectRenderer renderer, CrowdVatAiDebugRecorder recorder)
    {
        _renderer = renderer;
        _recorder = recorder;
        enabled = _renderer != null && _recorder != null && _recorder.IsRecording;
        if (enabled)
            _renderer.BeginAiDebugGpuStageCapture(_recorder.Targets, _recorder.DiscoverySettings);
    }

    public void Shutdown()
    {
        if (_renderer != null && _recorder != null && _recorder.IsRecording)
            _recorder.CaptureFrame();

        if (_renderer != null)
            _renderer.EndAiDebugGpuStageCapture();

        _renderer = null;
        _recorder = null;
        enabled = false;

        if (Application.isPlaying)
            Destroy(this);
        else
            DestroyImmediate(this);
    }

    private void LateUpdate()
    {
        if (_renderer == null || _recorder == null || !_recorder.IsRecording)
        {
            enabled = false;
            return;
        }

        _recorder.CaptureFrame();
        _renderer.BeginAiDebugGpuStageCapture(_recorder.Targets, _recorder.DiscoverySettings);
    }
}
