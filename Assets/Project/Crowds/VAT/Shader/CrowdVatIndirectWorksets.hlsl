[numthreads(64, 1, 1)]
void BuildAliveInstanceList(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex = dispatchThreadId.x;
    if (instanceIndex >= (uint)_InstanceCount)
        return;

    if (IsInstanceDead(instanceIndex))
        return;

    uint writeIndex = 0u;
    InterlockedAdd(_AliveInstanceCounterBuffer[0], 1u, writeIndex);
    _AliveInstanceIndexBuffer[writeIndex] = instanceIndex;
}

[numthreads(1, 1, 1)]
void BuildAliveDispatchArgs(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint aliveCount = _AliveInstanceCounterBuffer[0];
    uint threadGroupCount = max(1u, (aliveCount + CrowdVatDispatchThreadGroupSize - 1u) / CrowdVatDispatchThreadGroupSize);
    _AliveInstanceDispatchArgsBuffer[0] = threadGroupCount;
    _AliveInstanceDispatchArgsBuffer[1] = 1u;
    _AliveInstanceDispatchArgsBuffer[2] = 1u;
}

[numthreads(64, 1, 1)]
void ClearWakeGrid(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint cellIndex = dispatchThreadId.x;
    if (cellIndex >= (uint)_WakeGridCellCount)
        return;

    _WakeGridCounterBuffer[cellIndex] = 0u;
}

[numthreads(64, 1, 1)]
void BuildWakeGrid(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    float2 positionXZ = GetLocalXZ(state.localPositionAndYaw);
    int2 coord;
    if (!TryGetWakeGridCellCoord(positionXZ, coord))
        return;

    uint cellKey = GetWakeGridCellKey(coord);
    uint slot = 0u;
    InterlockedAdd(_WakeGridCounterBuffer[cellKey], 1u, slot);
    if (slot >= (uint)_MaxCellOccupancy)
        return;

    _WakeGridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + slot] = instanceIndex;
}

[numthreads(64, 1, 1)]
void EvaluatePhysicsActive(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    if (IsInstanceDead(instanceIndex))
    {
        _PhysicsActiveStateBuffer[instanceIndex] = 0u;
        _PhysicsActivationMetaBuffer[instanceIndex] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    InstanceSpawnData spawnData = _SpawnData[instanceIndex];
    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    float2 currentXZ = GetLocalXZ(state.localPositionAndYaw);
    float2 staticAnchorXZ = spawnData.localPosition.xz;
    float2 anchorXZ = staticAnchorXZ;
    float2 formationCenterXZ = staticAnchorXZ;
    float2 commandVelocityXZ = 0.0;
    float formationDeviationRadius = max(_MaxDisplacementFromSpawn, 1e-4);
    float squadProjectionStrength = 0.0;
    float2 desiredForwardXZ = 0.0;
    bool hasRuntimeSquadAnchor = TryResolveRuntimeSquadMotionXZ(
        instanceIndex,
        staticAnchorXZ,
        currentXZ,
        anchorXZ,
        formationCenterXZ,
        commandVelocityXZ,
        formationDeviationRadius,
        squadProjectionStrength,
        desiredForwardXZ);

    float speed = length(state.scaleAndVelocity.yz);
    float anchorError = length(currentXZ - anchorXZ);
    float previousCorrection = _PhysicsActivationMetaBuffer[instanceIndex].z;
    float wakeCorrection = max(_PhysicsWakeAnchorError * 0.25, _CollisionRadius * 0.2);
    float sleepCorrection = max(_PhysicsSleepAnchorError * 0.25, _CollisionRadius * 0.08);
    bool previousPhysicsActive = _PhysicsActiveStateBuffer[instanceIndex] != 0u;
    float4 meta = _PhysicsActivationMetaBuffer[instanceIndex];
    meta.x = max(meta.x - _DeltaTime, 0.0);

    bool commandWake = hasRuntimeSquadAnchor && dot(commandVelocityXZ, commandVelocityXZ) > 1e-6;
    bool unsettledWake = speed > _PhysicsWakeSpeed ||
        anchorError > _PhysicsWakeAnchorError ||
        previousCorrection > wakeCorrection ||
        meta.x > 0.0;
    bool baseWake = commandWake || unsettledWake;

    if (_PhysicsActivationPass == 0)
    {
        meta.w = previousPhysicsActive ? 1.0 : 0.0;
        if (baseWake)
        {
            _PhysicsActiveStateBuffer[instanceIndex] = 1u;
            meta.x = max(meta.x, _PhysicsActiveHoldTime);
            meta.y = 0.0;
        }
        else
        {
            _PhysicsActiveStateBuffer[instanceIndex] = 0u;
        }

        _PhysicsActivationMetaBuffer[instanceIndex] = meta;
        return;
    }

    bool neighborWake = !baseWake && HasPhysicsActiveNeighbor(instanceIndex, currentXZ);
    bool activeThisFrame = baseWake || neighborWake;
    if (activeThisFrame)
    {
        _PhysicsActiveStateBuffer[instanceIndex] = 1u;
        meta.x = max(meta.x, _PhysicsActiveHoldTime);
        meta.y = 0.0;
        meta.w = 1.0;
        _PhysicsActivationMetaBuffer[instanceIndex] = meta;
        return;
    }

    bool wasPhysicsActive = meta.w > 0.5 || _PhysicsActiveStateBuffer[instanceIndex] != 0u;
    bool stableForSleep = speed < _PhysicsSleepSpeed &&
        anchorError < _PhysicsSleepAnchorError &&
        previousCorrection < sleepCorrection &&
        !HasPhysicsActiveNeighbor(instanceIndex, currentXZ);

    if (wasPhysicsActive && stableForSleep)
        meta.y += _DeltaTime;
    else
        meta.y = 0.0;

    bool shouldSleep = !wasPhysicsActive || meta.y >= _PhysicsSleepDelay;
    _PhysicsActiveStateBuffer[instanceIndex] = shouldSleep ? 0u : 1u;
    meta.w = shouldSleep ? 0.0 : 1.0;
    _PhysicsActivationMetaBuffer[instanceIndex] = meta;
}

[numthreads(64, 1, 1)]
void BuildPhysicsActiveInstanceList(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    if (IsInstanceDead(instanceIndex) || _PhysicsActiveStateBuffer[instanceIndex] == 0u)
        return;

    uint writeIndex = 0u;
    InterlockedAdd(_PhysicsActiveInstanceCounterBuffer[0], 1u, writeIndex);
    _PhysicsActiveInstanceIndexBuffer[writeIndex] = instanceIndex;
}

[numthreads(1, 1, 1)]
void BuildPhysicsActiveDispatchArgs(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint activeCount = _PhysicsActiveInstanceCounterBuffer[0];
    uint threadGroupCount = max(1u, (activeCount + CrowdVatDispatchThreadGroupSize - 1u) / CrowdVatDispatchThreadGroupSize);
    _PhysicsActiveInstanceDispatchArgsBuffer[0] = threadGroupCount;
    _PhysicsActiveInstanceDispatchArgsBuffer[1] = 1u;
    _PhysicsActiveInstanceDispatchArgsBuffer[2] = 1u;
}

bool HasCombatVisualInterest(InstanceCombatStateData combatState)
{
    return combatState.muzzleFlash > 0.001 ||
        combatState.localImpactNormalAndHit.w > 0.001 ||
        combatState.healthAndHitFeedback.y > 0.001;
}

[numthreads(64, 1, 1)]
void EvaluateCombatActive(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    if (_EnableGpuInstanceCombat == 0 || IsInstanceDead(instanceIndex))
    {
        _CombatActiveStateBuffer[instanceIndex] = 0u;
        _CombatActivationMetaBuffer[instanceIndex] = 0.0;
        return;
    }

    InstanceCombatStateData combatState = _CombatStateBuffer[instanceIndex];
    TargetAcquisitionStateData acquisitionState = _TargetAcquisitionStateBuffer[instanceIndex];
    float4 meta = _CombatActivationMetaBuffer[instanceIndex];
    meta.x = max(meta.x - _DeltaTime, 0.0);

    bool hasCombatTarget = (combatState.flags & CrowdVatInstanceCombatFlagHasTarget) != 0u ||
        (acquisitionState.flags & CrowdVatTargetAcquisitionFlagHasTarget) != 0u ||
        (acquisitionState.flags & CrowdVatTargetAcquisitionFlagLocked) != 0u;
    bool visualInterest = HasCombatVisualInterest(combatState);
    int probeInterval = max(_CombatActiveProbeIntervalFrames, 1);
    bool probeFrame = ((max(_SimulationFrameIndex, 0) + (int)(instanceIndex % (uint)probeInterval)) % probeInterval) == 0;

    uint reason = 0u;
    if (probeFrame)
        reason |= CrowdVatCombatActiveReasonProbe;
    if (hasCombatTarget)
        reason |= CrowdVatCombatActiveReasonTarget;
    if (visualInterest)
        reason |= CrowdVatCombatActiveReasonVisual;

    if ((reason & (CrowdVatCombatActiveReasonTarget | CrowdVatCombatActiveReasonVisual)) != 0u)
    {
        meta.x = max(meta.x, _CombatActiveHoldTime);
    }
    else if (meta.x > 0.0)
    {
        reason |= CrowdVatCombatActiveReasonHold;
    }

    bool isCombatActive = reason != 0u;
    _CombatActiveStateBuffer[instanceIndex] = isCombatActive ? 1u : 0u;
    meta.y = isCombatActive ? 1.0 : 0.0;
    meta.z = (float)reason;
    meta.w = combatState.targetIndex >= 0 ? (float)combatState.targetIndex : -1.0;
    _CombatActivationMetaBuffer[instanceIndex] = meta;
}

[numthreads(64, 1, 1)]
void BuildCombatActiveInstanceList(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    if (IsInstanceDead(instanceIndex) || _CombatActiveStateBuffer[instanceIndex] == 0u)
        return;

    float reason = _CombatActivationMetaBuffer[instanceIndex].z;
    bool hasTarget = ((uint)reason & CrowdVatCombatActiveReasonTarget) != 0u;

    uint writeIndex = 0u;
    if (hasTarget)
    {
        InterlockedAdd(_CombatActiveInstanceCounterBuffer[1], 1u, writeIndex);
        uint writeSlot = (uint)(_InstanceCount) - 1u - writeIndex;
        _CombatActiveInstanceIndexBuffer[writeSlot] = instanceIndex;
    }
    else
    {
        InterlockedAdd(_CombatActiveInstanceCounterBuffer[0], 1u, writeIndex);
        _CombatActiveInstanceIndexBuffer[writeIndex] = instanceIndex;
    }
}


[numthreads(64, 1, 1)]
void CompactCombatActiveInstanceList(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint threadIndex = dispatchThreadId.x;
    uint count0 = _CombatActiveInstanceCounterBuffer[0];
    uint count1 = _CombatActiveInstanceCounterBuffer[1];
    if (threadIndex >= count1)
        return;

    uint sourceSlot = (uint)(_InstanceCount) - 1u - threadIndex;
    uint destSlot = count0 + threadIndex;
    _CombatActiveInstanceIndexBuffer[destSlot] = _CombatActiveInstanceIndexBuffer[sourceSlot];

    if (threadIndex == 0u)
        _CombatActiveInstanceCounterBuffer[0] = count0 + count1;
}

[numthreads(1, 1, 1)]
void BuildCombatActiveDispatchArgs(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint activeCount = _CombatActiveInstanceCounterBuffer[0];
    uint threadGroupCount = max(1u, (activeCount + CrowdVatDispatchThreadGroupSize - 1u) / CrowdVatDispatchThreadGroupSize);
    _CombatActiveInstanceDispatchArgsBuffer[0] = threadGroupCount;
    _CombatActiveInstanceDispatchArgsBuffer[1] = 1u;
    _CombatActiveInstanceDispatchArgsBuffer[2] = 1u;
}

static const uint CombatCandidateClusterCellsPerTile = 8u;

[numthreads(64, 1, 1)]
void ClearCombatSquadCandidateCounters(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint sourceSquadIndex = dispatchThreadId.x;
    if (sourceSquadIndex >= (uint)_SquadStateCount)
        return;

    _CombatSquadCandidateCounterBuffer[sourceSquadIndex] = 0u;
}

[numthreads(64, 1, 1)]
void BuildCombatCandidateClusters(uint3 groupId : SV_GroupID, uint3 groupThreadId : SV_GroupThreadID)
{
    CombatCandidateClusterWorkItemData workItem = _CombatCandidateClusterWorkItemBuffer[groupId.x];
    uint sourceSquadIndex = workItem.sourceSquadIndex;
    if (sourceSquadIndex >= (uint)_SquadStateCount)
        return;

    if (_EnableGpuInstanceCombat == 0 || _CombatCandidateCapacityPerSquad <= 0 || _CombatRange <= 0.0)
        return;

    SquadStateData sourceSquad = _SquadStateBuffer[sourceSquadIndex];
    uint sourceFaction = DecodeRuntimeUInt(sourceSquad.metadata0.w);
    if (sourceFaction == CrowdVatFactionMaskNone)
        return;

    float2 sourceCenterXZ = sourceSquad.localCenterAndAnchorBlend.xz;
    float sourceRadius = max(max(sourceSquad.metadata1.y, _MaxDisplacementFromSpawn), _CollisionRadius);
    int2 minCoord = workItem.minCoord;
    uint cellWidth = workItem.cellWidth;
    uint cellCount = workItem.cellCount;
    uint tileBegin = workItem.tileBegin;
    if (cellWidth == 0u || tileBegin >= cellCount)
        return;

    uint candidateCapacity = (uint)_CombatCandidateCapacityPerSquad;
    uint candidateBase = sourceSquadIndex * candidateCapacity;
    uint maxOccupantChunkCount = ((uint)_MaxCellOccupancy + CrowdVatDispatchThreadGroupSize - 1u) / CrowdVatDispatchThreadGroupSize;

    [unroll]
    for (uint tileCellOffset = 0u; tileCellOffset < CombatCandidateClusterCellsPerTile; tileCellOffset++)
    {
        uint cellOrdinal = tileBegin + tileCellOffset;
        if (cellOrdinal >= cellCount)
            break;

        int cellX = minCoord.x + (int)(cellOrdinal % cellWidth);
        int cellY = minCoord.y + (int)(cellOrdinal / cellWidth);
        uint cellKey = GetCellKey(int2(cellX, cellY));
        uint occupantCount = min(_GridCounterBuffer[cellKey], (uint)_MaxCellOccupancy);

        [loop]
        for (uint occupantChunkIndex = 0u; occupantChunkIndex < maxOccupantChunkCount; occupantChunkIndex++)
        {
            uint occupantIndex = occupantChunkIndex * CrowdVatDispatchThreadGroupSize + groupThreadId.x;
            if (occupantIndex >= occupantCount)
                continue;

            uint candidateIndex = _GridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + occupantIndex];
            if (candidateIndex >= (uint)_InstanceCount || IsInstanceDead(candidateIndex))
                continue;

            InstanceSpawnData candidateSpawnData = _SpawnData[candidateIndex];
            if (!AreFactionsHostile(sourceFaction, candidateSpawnData.faction))
                continue;

            InstanceSimulationState candidateState = LoadSimulationState(candidateIndex);
            float candidateScale = max(candidateState.scaleAndVelocity.x, candidateSpawnData.uniformScale);
            float candidateRadius = ComputeCombatHitRadius(candidateScale);
            float distanceToSourceBounds = length(candidateState.localPositionAndYaw.xz - sourceCenterXZ) - sourceRadius - candidateRadius;
            if (distanceToSourceBounds > _CombatRange)
                continue;

            uint writeIndex = 0u;
            InterlockedAdd(_CombatSquadCandidateCounterBuffer[sourceSquadIndex], 1u, writeIndex);
            if (writeIndex >= candidateCapacity)
                continue;

            CombatSquadCandidateData candidate;
            candidate.instanceIndex = candidateIndex;
            candidate.faction = candidateSpawnData.faction;
            candidate.targetLocalAndScale = float4(BuildCombatTargetTopLocal(candidateState), candidateScale);
            candidate.distanceToSourceBounds = distanceToSourceBounds;
            candidate.padding = 0u;
            _CombatSquadCandidateBuffer[candidateBase + writeIndex] = candidate;
        }
    }
}
