[numthreads(64, 1, 1)]
void CaptureAiDebugGpuStage(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint targetSlot = dispatchThreadId.x;
    if (targetSlot >= (uint)_AiDebugTargetCount)
        return;

    uint writeIndex = (uint)_AiDebugStageWriteOffset + targetSlot;
    uint instanceIndex = _AiDebugTargetIndexBuffer[targetSlot];

    CrowdVatAiDebugGpuStageRecord record;
    record.frameIndex = (uint)max(_AiDebugFrameIndex, 0);
    record.stageId = (uint)max(_AiDebugStageId, 0);
    record.solverIteration = _AiDebugSolverIteration;
    record.targetSlot = targetSlot;
    record.instanceIndex = instanceIndex;
    record.physicsActive = 0u;
    record.deathState = 0u;
    record.gridCellKey = CrowdVatInvalidInstanceIndex;
    record.gridOccupantCount = 0u;
    record.gridOverflow = 0u;
    record.combatFlags = 0u;
    record.acquisitionFlags = 0u;
    record.combatTargetIndex = -1;
    record.acquisitionTargetIndex = -1;
    record.acquisitionLastAttackerIndex = -1;
    record.valid = 0u;
    record.localPositionYaw = 0.0;
    record.velocityScaleHealth = 0.0;
    record.combatDistanceCooldownMuzzleHit = 0.0;
    record.combatImpactDamageNormalizedHealth = 0.0;
    record.acquisitionLastKnownScore = 0.0;
    record.acquisitionTimers = 0.0;
    record.acquisitionRejectCounts0 = 0.0;
    record.acquisitionRejectCounts1 = 0.0;
    record.combatDebugShotInfo = float4(-1.0, -1.0, 0.0, 0.0);
    record.combatDebugShotTrace = float4(-1.0, 0.0, 0.0, 0.0);

    if (instanceIndex < (uint)_InstanceCount)
    {
        InstanceSimulationState state = LoadSimulationState(instanceIndex);
        uint physicsActiveState = _PhysicsActiveStateBuffer[instanceIndex];
        uint deathState = _DeathStateBuffer[instanceIndex];
        InstanceCombatStateData combatState = _CombatStateBuffer[instanceIndex];
        TargetAcquisitionStateData acquisitionState = _TargetAcquisitionStateBuffer[instanceIndex];
        float health = IsDeathStateDead(deathState)
            ? 0.0
            : (combatState.healthAndHitFeedback.x > 0.0 ? combatState.healthAndHitFeedback.x : ComputeCombatHealth(deathState));

        record.valid = 1u;
        record.physicsActive = physicsActiveState;
        record.deathState = deathState;
        record.combatFlags = combatState.flags;
        record.acquisitionFlags = acquisitionState.flags;
        record.combatTargetIndex = combatState.targetIndex;
        record.acquisitionTargetIndex = acquisitionState.currentTargetIndex;
        record.acquisitionLastAttackerIndex = acquisitionState.lastAttackerIndex;
        record.localPositionYaw = state.localPositionAndYaw;
        record.velocityScaleHealth = float4(
            state.scaleAndVelocity.y,
            state.scaleAndVelocity.z,
            state.scaleAndVelocity.x,
            health);
        record.combatDistanceCooldownMuzzleHit = float4(
            combatState.localOriginAndDistance.w,
            combatState.localTargetAndCooldown.w,
            combatState.muzzleFlash,
            combatState.healthAndHitFeedback.y);
        record.combatImpactDamageNormalizedHealth = float4(
            combatState.localImpactNormalAndHit.w,
            combatState.healthAndHitFeedback.z,
            combatState.healthAndHitFeedback.w,
            0.0);
        record.acquisitionLastKnownScore = acquisitionState.lastKnownTargetAndScore;
        record.acquisitionTimers = acquisitionState.timers;
        record.acquisitionRejectCounts0 = acquisitionState.debugRejectCounts0;
        record.acquisitionRejectCounts1 = acquisitionState.debugRejectCounts1;
        record.combatDebugShotInfo = DecodeAiDebugCombatShotInfo(combatState.debugShotInfoPacked);
        record.combatDebugShotTrace = combatState.debugShotTraceMeta;

        int2 coord;
        if (TryGetCellCoord(GetLocalXZ(state.localPositionAndYaw), coord))
        {
            uint cellKey = GetCellKey(coord);
            uint occupantCount = _GridCounterBuffer[cellKey];
            record.gridCellKey = cellKey;
            record.gridOccupantCount = occupantCount;
            record.gridOverflow = occupantCount > (uint)_MaxCellOccupancy ? 1u : 0u;
        }
    }

    _AiDebugStageRecordBuffer[writeIndex] = record;
}

bool IsAiDebugFinite4(float4 value)
{
    return all(value == value) && all(abs(value) < 3.402823e38);
}

bool IsAiDebugInvalidLiveTarget(int targetIndex)
{
    if (targetIndex < 0 || targetIndex >= _InstanceCount)
        return true;

    return IsDeathStateDead(_DeathStateBuffer[(uint)targetIndex]);
}

bool AiDebugHasDiscoveryFlag(uint flag)
{
    return ((uint)_AiDebugDiscoveryFlags & flag) != 0u;
}

bool IsAiDebugSquadMatch(uint instanceIndex)
{
    if (_AgentSquadDataCount <= 0 || instanceIndex >= (uint)_AgentSquadDataCount)
        return false;

    AgentSquadData agentData = _AgentSquadDataBuffer[instanceIndex];
    uint flags = (uint)round(agentData.squadAndSlotAndRoleAndFlags.w);
    if ((flags & CrowdVatSquadMemberFlagUnassigned) != 0u)
        return false;

    uint squadId = (uint)round(agentData.squadAndSlotAndRoleAndFlags.x);
    return squadId == (uint)max(_AiDebugDiscoverySquadId, 0);
}

bool IsAiDebugScreenRectHit(float3 localPosition)
{
    float3 worldPosition = TransformLocalPointToWorld(localPosition);
    float4 world = float4(worldPosition, 1.0);
    float4 clip = float4(
        dot(_AiDebugDiscoveryWorldToClipRow0, world),
        dot(_AiDebugDiscoveryWorldToClipRow1, world),
        dot(_AiDebugDiscoveryWorldToClipRow2, world),
        dot(_AiDebugDiscoveryWorldToClipRow3, world));

    if (clip.w <= 1e-5)
        return false;

    float2 uv = clip.xy / clip.w * 0.5 + 0.5;
    float2 rectMin = min(_AiDebugDiscoveryScreenRect.xy, _AiDebugDiscoveryScreenRect.zw);
    float2 rectMax = max(_AiDebugDiscoveryScreenRect.xy, _AiDebugDiscoveryScreenRect.zw);
    return all(uv >= rectMin) && all(uv <= rectMax);
}

[numthreads(64, 1, 1)]
void DiscoverAiDebugGpuCandidates(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex = dispatchThreadId.x;
    if (instanceIndex >= (uint)_InstanceCount || _AiDebugCandidateCapacity <= 0)
        return;

    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    uint physicsActiveState = _PhysicsActiveStateBuffer[instanceIndex];
    uint deathState = _DeathStateBuffer[instanceIndex];
    InstanceCombatStateData combatState = _CombatStateBuffer[instanceIndex];
    TargetAcquisitionStateData acquisitionState = _TargetAcquisitionStateBuffer[instanceIndex];

    bool stateIsFinite = IsAiDebugFinite4(state.localPositionAndYaw) && IsAiDebugFinite4(state.scaleAndVelocity);
    if (IsDeathStateDead(deathState))
        return;

    uint gridCellKey = CrowdVatInvalidInstanceIndex;
    uint gridOccupantCount = 0u;
    uint gridOverflow = 0u;
    if (stateIsFinite && _GridDim.x > 0 && _GridDim.y > 0 && _GridCellCount > 0 && _MaxCellOccupancy > 0)
    {
        int2 coord;
        if (TryGetCellCoord(GetLocalXZ(state.localPositionAndYaw), coord))
        {
            gridCellKey = GetCellKey(coord);
            gridOccupantCount = _GridCounterBuffer[gridCellKey];
            if (gridOccupantCount > (uint)_MaxCellOccupancy)
                gridOverflow = 1u;
        }
    }

    uint filterReasonMask = 0u;
    bool hasFilter = false;
    bool filterHit = false;

    if (AiDebugHasDiscoveryFlag(CrowdVatAiDebugDiscoveryFlagRegionSphere) && stateIsFinite)
    {
        hasFilter = true;
        float radius = max(_AiDebugDiscoveryCenterRadius.w, 0.0);
        float2 deltaXZ = state.localPositionAndYaw.xz - _AiDebugDiscoveryCenterRadius.xz;
        if (radius > 0.0 && dot(deltaXZ, deltaXZ) <= radius * radius)
        {
            filterHit = true;
            filterReasonMask |= CrowdVatAiDebugCandidateRegion;
        }
    }

    if (AiDebugHasDiscoveryFlag(CrowdVatAiDebugDiscoveryFlagRegionBox) && stateIsFinite)
    {
        hasFilter = true;
        float3 delta = abs(state.localPositionAndYaw.xyz - _AiDebugDiscoveryBoxCenter.xyz);
        if (all(delta <= _AiDebugDiscoveryBoxExtents.xyz))
        {
            filterHit = true;
            filterReasonMask |= CrowdVatAiDebugCandidateRegion;
        }
    }

    if (AiDebugHasDiscoveryFlag(CrowdVatAiDebugDiscoveryFlagScreenRect) && stateIsFinite)
    {
        hasFilter = true;
        if (IsAiDebugScreenRectHit(state.localPositionAndYaw.xyz))
        {
            filterHit = true;
            filterReasonMask |= CrowdVatAiDebugCandidateScreen;
        }
    }

    if (AiDebugHasDiscoveryFlag(CrowdVatAiDebugDiscoveryFlagSquadFilter))
    {
        hasFilter = true;
        if (IsAiDebugSquadMatch(instanceIndex))
        {
            filterHit = true;
            filterReasonMask |= CrowdVatAiDebugCandidateSquad;
        }
    }

    if (hasFilter && !filterHit)
        return;

    uint reasonMask = filterReasonMask;
    if (reasonMask == 0u)
        return;

    uint candidateSlot;
    InterlockedAdd(_AiDebugCandidateCounterBuffer[0], 1u, candidateSlot);
    if (candidateSlot >= (uint)_AiDebugCandidateCapacity)
        return;

    float health = combatState.healthAndHitFeedback.x > 0.0 ? combatState.healthAndHitFeedback.x : ComputeCombatHealth(deathState);

    CrowdVatAiDebugGpuCandidateRecord record;
    record.frameIndex = (uint)max(_AiDebugDiscoveryFrameIndex, 0);
    record.candidateSlot = candidateSlot;
    record.instanceIndex = instanceIndex;
    record.reasonMask = reasonMask;
    record.reservedFloat0 = 0.0;
    record.physicsActive = physicsActiveState;
    record.deathState = deathState;
    record.gridCellKey = gridCellKey;
    record.gridOccupantCount = gridOccupantCount;
    record.gridOverflow = gridOverflow;
    record.combatFlags = combatState.flags;
    record.acquisitionFlags = acquisitionState.flags;
    record.combatTargetIndex = combatState.targetIndex;
    record.acquisitionTargetIndex = acquisitionState.currentTargetIndex;
    record.valid = 1u;
    record.reserved0 = 0u;
    record.localPositionYaw = state.localPositionAndYaw;
    record.velocityScaleHealth = float4(
        state.scaleAndVelocity.y,
        state.scaleAndVelocity.z,
        state.scaleAndVelocity.x,
        health);
    record.acquisitionRejectCounts0 = acquisitionState.debugRejectCounts0;
    record.acquisitionRejectCounts1 = acquisitionState.debugRejectCounts1;
    record.combatDistanceCooldownMuzzleHit = float4(
        combatState.localOriginAndDistance.w,
        combatState.localTargetAndCooldown.w,
        combatState.muzzleFlash,
        combatState.healthAndHitFeedback.y);

    _AiDebugCandidateRecordBuffer[candidateSlot] = record;
}
