groupshared float g_SquadAcquisitionScores[CrowdVatDispatchThreadGroupSize * CrowdVatTargetAcquisitionTopK];
groupshared int g_SquadAcquisitionSlots[CrowdVatDispatchThreadGroupSize * CrowdVatTargetAcquisitionTopK];

[numthreads(1, 1, 1)]
void BuildSquadAcquisitionDispatchArgs(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint activeCount = _CombatActiveInstanceCounterBuffer[0];
    _SquadAcquisitionDispatchArgsBuffer[0] = activeCount;
    _SquadAcquisitionDispatchArgsBuffer[1] = 1u;
    _SquadAcquisitionDispatchArgsBuffer[2] = 1u;
}

[numthreads(64, 1, 1)]
void ResolveTargetAcquisition(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveCombatActiveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    ClearTargetAcquisitionTopCandidates(instanceIndex);

    InstanceSimulationState selfState = LoadSimulationState(instanceIndex);
    TargetAcquisitionStateData previousTargetState = _TargetAcquisitionStateBuffer[instanceIndex];
    float nextSearchTimer = max(previousTargetState.timers.x - _DeltaTime, 0.0);
    float lockTimer = max(previousTargetState.timers.y - _DeltaTime, 0.0);
    float lostSightTimer = max(previousTargetState.timers.z - _DeltaTime, 0.0);
    float lineOfSightRecheckTimer = max(previousTargetState.timers.w - _DeltaTime, 0.0);
    TargetAcquisitionStateData targetState = BuildDefaultTargetAcquisitionState(
        selfState,
        nextSearchTimer,
        lockTimer,
        lostSightTimer,
        lineOfSightRecheckTimer,
        previousTargetState.lastAttackerIndex);
    targetState.debugRejectCounts0 = previousTargetState.debugRejectCounts0;
    targetState.debugRejectCounts1 = previousTargetState.debugRejectCounts1;

    if (IsInstanceDead(instanceIndex) || _EnableGpuInstanceCombat == 0)
    {
        _TargetAcquisitionStateBuffer[instanceIndex] = targetState;
        return;
    }

    InstanceSpawnData selfSpawnData = _SpawnData[instanceIndex];
    float combatRange = max(_CombatRange, 0.0);
    if (combatRange <= 0.0)
    {
        _TargetAcquisitionStateBuffer[instanceIndex] = targetState;
        return;
    }

    float3 originLocal = BuildCombatTargetTopLocal(selfState);
    float3 originWorld = TransformLocalPointToWorld(originLocal);
    float2 selfForwardXZ = ComputeCombatForwardXZ(selfState.localPositionAndYaw.w);
    int currentTargetIndex = previousTargetState.currentTargetIndex;
    int lastAttackerIndex = previousTargetState.lastAttackerIndex;

    bool hasCurrentTarget = false;
    bool currentHasLineOfSight = false;
    float3 currentTargetLocal = originLocal;
    float currentTargetDistance = combatRange;
    float currentViewDot = 0.0;
    uint currentRejectReason = 0u;
    bool previousCurrentHasLineOfSight = (previousTargetState.flags & CrowdVatTargetAcquisitionFlagHasLineOfSight) != 0u;
    bool shouldRecheckCurrentLineOfSight = lineOfSightRecheckTimer <= 1e-4;
    if (TryValidateTargetAcquisitionTarget(
        instanceIndex,
        selfSpawnData.faction,
        combatRange,
        originLocal,
        originWorld,
        selfForwardXZ,
        currentTargetIndex,
        currentTargetLocal,
        currentTargetDistance,
        currentViewDot,
        currentRejectReason))
    {
        currentHasLineOfSight = previousCurrentHasLineOfSight;
        if (shouldRecheckCurrentLineOfSight)
        {
            float currentRecheckScore = previousTargetState.lastKnownTargetAndScore.w +
                max(_TargetAcquisitionCurrentTargetBonus, 0.0);
            SetTargetAcquisitionCurrentRecheckCandidate(
                instanceIndex,
                BuildTargetAcquisitionCandidate(
                    (uint)currentTargetIndex,
                    currentRecheckScore,
                    currentTargetLocal,
                    currentTargetDistance,
                    currentRejectReason));
        }

        if (currentHasLineOfSight && !shouldRecheckCurrentLineOfSight)
        {
            hasCurrentTarget = true;
            lostSightTimer = max(_TargetAcquisitionLostSightGrace, 0.0);
        }
        else if (currentHasLineOfSight || lostSightTimer > 1e-4 || shouldRecheckCurrentLineOfSight)
        {
            hasCurrentTarget = true;
        }
    }

    if (hasCurrentTarget && lockTimer > 1e-4)
    {
        targetState.currentTargetIndex = currentTargetIndex;
        targetState.lastKnownTargetAndScore = float4(currentTargetLocal, previousTargetState.lastKnownTargetAndScore.w);
        targetState.timers = float4(nextSearchTimer, lockTimer, lostSightTimer, lineOfSightRecheckTimer);
        targetState.flags = CrowdVatTargetAcquisitionFlagHasTarget | CrowdVatTargetAcquisitionFlagLocked;
        if (currentHasLineOfSight)
        {
            targetState.flags |= CrowdVatTargetAcquisitionFlagHasLineOfSight;
            targetState.debugVisibleSampleIndex = 0;
        }

        _TargetAcquisitionStateBuffer[instanceIndex] = targetState;
        return;
    }

    int bestTargetIndex = hasCurrentTarget ? currentTargetIndex : -1;
    float3 bestTargetLocal = hasCurrentTarget ? currentTargetLocal : originLocal;
    float bestTargetScore = hasCurrentTarget
        ? previousTargetState.lastKnownTargetAndScore.w + max(_TargetAcquisitionCurrentTargetBonus, 0.0)
        : -1.0;
    bool bestHasLineOfSight = hasCurrentTarget && currentHasLineOfSight;
    uint assignedSquadIndex;
    bool hasSquadPath = TryGetAssignedRuntimeSquadIndex(instanceIndex, assignedSquadIndex) &&
        _CombatCandidateCapacityPerSquad > 0 &&
        assignedSquadIndex < (uint)_SquadStateCount;

    if (hasSquadPath)
    {
        nextSearchTimer = ComputeTargetAcquisitionSearchInterval(instanceIndex, selfState);

        if (hasCurrentTarget)
        {
            targetState.currentTargetIndex = currentTargetIndex;
            targetState.lastKnownTargetAndScore = float4(currentTargetLocal, previousTargetState.lastKnownTargetAndScore.w);
            targetState.timers = float4(nextSearchTimer, 0.0, lostSightTimer, lineOfSightRecheckTimer);
            targetState.flags = CrowdVatTargetAcquisitionFlagHasTarget;
        }
        else
        {
            targetState.currentTargetIndex = -1;
            targetState.lastKnownTargetAndScore = float4(currentTargetLocal, -1.0);
            targetState.timers = float4(nextSearchTimer, 0.0, 0.0, 0.0);
            targetState.flags = 0u;
        }

        _TargetAcquisitionStateBuffer[instanceIndex] = targetState;
        return;
    }

    if (!hasSquadPath && _EnableGpuInstanceCombat != 0 && _SquadStateCount > 0)
    {
        assignedSquadIndex = instanceIndex % (uint)_SquadStateCount;
        hasSquadPath = true;
    }

    if (hasSquadPath)
    {
        nextSearchTimer = ComputeTargetAcquisitionSearchInterval(instanceIndex, selfState);

        if (hasCurrentTarget)
        {
            targetState.currentTargetIndex = currentTargetIndex;
            targetState.lastKnownTargetAndScore = float4(currentTargetLocal, previousTargetState.lastKnownTargetAndScore.w);
            targetState.timers = float4(nextSearchTimer, 0.0, lostSightTimer, lineOfSightRecheckTimer);
            targetState.flags = CrowdVatTargetAcquisitionFlagHasTarget;
        }
        else
        {
            targetState.currentTargetIndex = -1;
            targetState.lastKnownTargetAndScore = float4(currentTargetLocal, -1.0);
            targetState.timers = float4(nextSearchTimer, 0.0, 0.0, 0.0);
            targetState.flags = 0u;
        }

        _TargetAcquisitionStateBuffer[instanceIndex] = targetState;
        return;
    }

    bool performedSearch = nextSearchTimer <= 1e-4;

    if (performedSearch)
    {
        uint maxCandidateChecks = (uint)max(_TargetAcquisitionMaxCandidateChecks, 1);
        uint candidateChecks = 0u;
        float4 debugRejectCounts0 = 0.0;
        float4 debugRejectCounts1 = 0.0;

        float broadphaseRadius = combatRange + max(_MaxSpatialQueryElementRadius, 0.0);
        float2 minXZ = selfState.localPositionAndYaw.xz - broadphaseRadius;
        float2 maxXZ = selfState.localPositionAndYaw.xz + broadphaseRadius;
        int2 minCoord;
        int2 maxCoord;
        if (TryGetCellRange(minXZ, maxXZ, minCoord, maxCoord))
        {
            [loop]
            for (int cellY = minCoord.y; cellY <= maxCoord.y; cellY++)
            {
                if (candidateChecks >= maxCandidateChecks)
                    break;

                [loop]
                for (int cellX = minCoord.x; cellX <= maxCoord.x; cellX++)
                {
                    if (candidateChecks >= maxCandidateChecks)
                        break;

                    uint cellKey = GetCellKey(int2(cellX, cellY));
                    uint occupantCount = min(_GridCounterBuffer[cellKey], (uint)_MaxCellOccupancy);
                    [loop]
                    for (uint occupantIndex = 0u; occupantIndex < occupantCount; occupantIndex++)
                    {
                        if (candidateChecks >= maxCandidateChecks)
                            break;

                        uint neighborIndex = _GridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + occupantIndex];
                        if (neighborIndex >= (uint)_InstanceCount ||
                            neighborIndex == instanceIndex ||
                            IsInstanceDead(neighborIndex))
                        {
                            continue;
                        }

                        InstanceSpawnData neighborSpawnData = _SpawnData[neighborIndex];
                        if (!AreFactionsHostile(selfSpawnData.faction, neighborSpawnData.faction))
                            continue;

                        candidateChecks++;
                        float candidateScore;
                        float3 candidateTargetLocal;
                        float candidateTargetDistance;
                        uint candidateRejectReason;
                        if (!TryEvaluateTargetAcquisitionCandidate(
                            instanceIndex,
                            neighborIndex,
                            selfSpawnData.faction,
                            combatRange,
                            originLocal,
                            originWorld,
                            selfForwardXZ,
                            currentTargetIndex,
                            lastAttackerIndex,
                            candidateScore,
                            candidateTargetLocal,
                            candidateTargetDistance,
                            candidateRejectReason))
                        {
                            if ((candidateRejectReason & (1u << 0)) != 0u)
                                debugRejectCounts0.x += 1.0;
                            if ((candidateRejectReason & (1u << 1)) != 0u)
                                debugRejectCounts0.y += 1.0;
                            if ((candidateRejectReason & (1u << 2)) != 0u)
                                debugRejectCounts0.z += 1.0;
                            if ((candidateRejectReason & (1u << 3)) != 0u)
                                debugRejectCounts0.w += 1.0;
                            continue;
                        }

                        InsertTargetAcquisitionTopCandidate(
                            instanceIndex,
                            BuildTargetAcquisitionCandidate(
                                neighborIndex,
                                candidateScore,
                                candidateTargetLocal,
                                candidateTargetDistance,
                                candidateRejectReason));
                    }
                }
            }
        }

        if (candidateChecks >= maxCandidateChecks)
            debugRejectCounts1.x = 1.0;

        targetState.debugRejectCounts0 = debugRejectCounts0;
        targetState.debugRejectCounts1 = debugRejectCounts1;
        nextSearchTimer = ComputeTargetAcquisitionSearchInterval(instanceIndex, selfState);
    }

    if (bestTargetIndex >= 0)
    {
        bool selectedNewTarget = !hasCurrentTarget || bestTargetIndex != currentTargetIndex;
        if (selectedNewTarget)
        {
            lockTimer = max(_TargetAcquisitionLockDuration, 0.0);
            lostSightTimer = max(_TargetAcquisitionLostSightGrace, 0.0);
        }
        if (bestHasLineOfSight && selectedNewTarget)
            lineOfSightRecheckTimer = ComputeTargetAcquisitionLineOfSightRecheckInterval(instanceIndex);

        targetState.currentTargetIndex = bestTargetIndex;
        targetState.lastKnownTargetAndScore = float4(bestTargetLocal, bestTargetScore);
        targetState.timers = float4(nextSearchTimer, lockTimer, lostSightTimer, lineOfSightRecheckTimer);
        targetState.flags = CrowdVatTargetAcquisitionFlagHasTarget;
        if (bestHasLineOfSight)
        {
            targetState.flags |= CrowdVatTargetAcquisitionFlagHasLineOfSight;
            targetState.debugVisibleSampleIndex = 0;
        }
        if (lockTimer > 1e-4)
            targetState.flags |= CrowdVatTargetAcquisitionFlagLocked;
    }
    else
    {
        targetState.timers.x = performedSearch
            ? ComputeTargetAcquisitionSearchInterval(instanceIndex ^ 0x31B5297Du, selfState)
            : nextSearchTimer;
        targetState.timers.w = 0.0;
    }

    _TargetAcquisitionStateBuffer[instanceIndex] = targetState;
}

[numthreads(1, 1, 1)]
void BuildTargetAcquisitionLosDispatchArgs(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint activeCount = _CombatActiveInstanceCounterBuffer[0];
    uint queryCount = activeCount * CrowdVatTargetAcquisitionCandidateSlots;
    uint threadGroupCount = (queryCount + CrowdVatDispatchThreadGroupSize - 1u) / CrowdVatDispatchThreadGroupSize;
    _TargetAcquisitionLosDispatchArgsBuffer[0] = threadGroupCount;
    _TargetAcquisitionLosDispatchArgsBuffer[1] = 1u;
    _TargetAcquisitionLosDispatchArgsBuffer[2] = 1u;
}

[numthreads(64, 1, 1)]
void ResolveTargetAcquisitionLineOfSight(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint queryIndex = dispatchThreadId.x;
    uint activeSlot = queryIndex / CrowdVatTargetAcquisitionCandidateSlots;
    uint activeCount = _CombatActiveInstanceCounterBuffer[0];
    if (activeSlot >= activeCount)
        return;

    uint candidateSlot = queryIndex - activeSlot * CrowdVatTargetAcquisitionCandidateSlots;
    uint instanceIndex = _CombatActiveInstanceIndexBuffer[activeSlot];
    if (instanceIndex >= (uint)_InstanceCount)
        return;

    uint candidateIndex = GetTargetAcquisitionCandidateBaseIndex(instanceIndex) + candidateSlot;
    TargetAcquisitionCandidateData candidate = _TargetAcquisitionCandidateBuffer[candidateIndex];
    if ((candidate.flags & CrowdVatTargetCandidateFlagValid) == 0u ||
        candidate.targetIndex < 0 ||
        candidate.targetIndex >= _InstanceCount ||
        IsInstanceDead((uint)candidate.targetIndex))
    {
        return;
    }

    InstanceSimulationState selfState = LoadSimulationState(instanceIndex);
    float3 lineOfSightOriginLocal = BuildCombatTargetTopLocal(selfState);
    float lineOfSightDistance = length(
        TransformLocalPointToWorld(candidate.targetLocalAndScore.xyz) -
        TransformLocalPointToWorld(lineOfSightOriginLocal));
    uint occlusionMask = lineOfSightDistance <= 1e-4
        ? 0u
        : GetCombatOcclusionRejectMask(lineOfSightOriginLocal, candidate.targetLocalAndScore.xyz, lineOfSightDistance);

    candidate.flags |= CrowdVatTargetCandidateFlagLineOfSightResolved;
    candidate.rejectReason = occlusionMask << 2;
    if (occlusionMask == 0u)
        candidate.flags |= CrowdVatTargetCandidateFlagHasLineOfSight;
    else
        candidate.flags &= ~CrowdVatTargetCandidateFlagHasLineOfSight;

    _TargetAcquisitionCandidateBuffer[candidateIndex] = candidate;
}

[numthreads(64, 1, 1)]
void FinalizeTargetAcquisition(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveCombatActiveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    TargetAcquisitionStateData targetState = _TargetAcquisitionStateBuffer[instanceIndex];
    int currentTargetIndex = targetState.currentTargetIndex;
    int bestTargetIndex = currentTargetIndex;
    float3 bestTargetLocal = targetState.lastKnownTargetAndScore.xyz;
    float bestTargetScore = ((targetState.flags & CrowdVatTargetAcquisitionFlagHasTarget) != 0u)
        ? targetState.lastKnownTargetAndScore.w
        : -1.0;
    bool bestHasLineOfSight = (targetState.flags & CrowdVatTargetAcquisitionFlagHasLineOfSight) != 0u;
    int bestVisibleSampleIndex = targetState.debugVisibleSampleIndex;
    float4 debugRejectCounts0 = targetState.debugRejectCounts0;

    uint candidateBase = GetTargetAcquisitionCandidateBaseIndex(instanceIndex);

    TargetAcquisitionCandidateData currentRecheckCandidate =
        _TargetAcquisitionCandidateBuffer[candidateBase + CrowdVatTargetAcquisitionCurrentCandidateSlot];
    if ((currentRecheckCandidate.flags & CrowdVatTargetCandidateFlagCurrentTargetRecheck) != 0u &&
        (currentRecheckCandidate.flags & CrowdVatTargetCandidateFlagLineOfSightResolved) != 0u)
    {
        bool currentRecheckHasLineOfSight =
            (currentRecheckCandidate.flags & CrowdVatTargetCandidateFlagHasLineOfSight) != 0u;
        if (currentRecheckHasLineOfSight)
        {
            targetState.timers.z = max(_TargetAcquisitionLostSightGrace, 0.0);
            targetState.timers.w = ComputeTargetAcquisitionLineOfSightRecheckInterval(instanceIndex);
            if (currentTargetIndex == currentRecheckCandidate.targetIndex || bestTargetIndex < 0)
            {
                bestTargetIndex = currentRecheckCandidate.targetIndex;
                bestTargetLocal = currentRecheckCandidate.targetLocalAndScore.xyz;
                bestTargetScore = currentRecheckCandidate.targetLocalAndScore.w;
                bestHasLineOfSight = true;
                bestVisibleSampleIndex = 0;
            }
        }
        else
        {
            if ((currentRecheckCandidate.rejectReason & (1u << 2)) != 0u)
                debugRejectCounts0.z += 1.0;
            if ((currentRecheckCandidate.rejectReason & (1u << 3)) != 0u)
                debugRejectCounts0.w += 1.0;

            targetState.timers.w = max(_CombatOccludedTargetRetryDelay, 0.0);
            if (currentTargetIndex == currentRecheckCandidate.targetIndex)
            {
                bestHasLineOfSight = false;
                bestVisibleSampleIndex = -1;
                if (targetState.timers.z <= 1e-4)
                {
                    bestTargetIndex = -1;
                    bestTargetScore = -1.0;
                }
            }
        }
    }

    [unroll]
    for (uint candidateSlot = 0u; candidateSlot < CrowdVatTargetAcquisitionTopK; candidateSlot++)
    {
        uint bufferSlot = CrowdVatTargetAcquisitionSearchCandidateBaseSlot + candidateSlot;
        TargetAcquisitionCandidateData candidate = _TargetAcquisitionCandidateBuffer[candidateBase + bufferSlot];
        if ((candidate.flags & CrowdVatTargetCandidateFlagValid) == 0u)
            continue;

        if ((candidate.flags & CrowdVatTargetCandidateFlagHasLineOfSight) == 0u)
        {
            if ((candidate.rejectReason & (1u << 2)) != 0u)
                debugRejectCounts0.z += 1.0;
            if ((candidate.rejectReason & (1u << 3)) != 0u)
                debugRejectCounts0.w += 1.0;
            continue;
        }

        float candidateScore = candidate.targetLocalAndScore.w;
        if (candidateScore > bestTargetScore)
        {
            bestTargetScore = candidateScore;
            bestTargetIndex = candidate.targetIndex;
            bestTargetLocal = candidate.targetLocalAndScore.xyz;
            bestHasLineOfSight = true;
            bestVisibleSampleIndex = (int)candidateSlot + 1;
        }
    }

    targetState.debugRejectCounts0 = debugRejectCounts0;
    if (bestTargetIndex >= 0)
    {
        bool selectedNewTarget = bestTargetIndex != currentTargetIndex;
        if (selectedNewTarget)
        {
            targetState.timers.y = max(_TargetAcquisitionLockDuration, 0.0);
            targetState.timers.z = max(_TargetAcquisitionLostSightGrace, 0.0);
            targetState.timers.w = ComputeTargetAcquisitionLineOfSightRecheckInterval(instanceIndex);
        }

        targetState.currentTargetIndex = bestTargetIndex;
        targetState.lastKnownTargetAndScore = float4(bestTargetLocal, bestTargetScore);
        targetState.flags = CrowdVatTargetAcquisitionFlagHasTarget;
        if (bestHasLineOfSight)
        {
            targetState.flags |= CrowdVatTargetAcquisitionFlagHasLineOfSight;
            targetState.debugVisibleSampleIndex = bestVisibleSampleIndex;
        }
        if (targetState.timers.y > 1e-4)
            targetState.flags |= CrowdVatTargetAcquisitionFlagLocked;
    }
    else
    {
        targetState.currentTargetIndex = -1;
        targetState.lastKnownTargetAndScore = float4(bestTargetLocal, -1.0);
        targetState.flags = 0u;
        targetState.debugVisibleSampleIndex = -1;
    }

    _TargetAcquisitionStateBuffer[instanceIndex] = targetState;
}

[numthreads(64, 1, 1)]
void ResolveInstanceCombat(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveCombatActiveDispatchInstanceIndexReadOnly(dispatchThreadId.x, instanceIndex))
        return;

    InstanceSimulationState selfState = LoadSimulationState(instanceIndex);
    InstanceCombatStateData previousCombatState = _CombatStateBuffer[instanceIndex];
    uint selfDeathState = _DeathStateBuffer[instanceIndex];
    float currentHealth = ComputeCombatHealth(selfDeathState);
    float previousHealth = previousCombatState.healthAndHitFeedback.x > 0.0
        ? previousCombatState.healthAndHitFeedback.x
        : (float)GetCombatMaxHealthUnits();
    float lastDamage = max(previousHealth - currentHealth, 0.0);
    float cooldown = max(previousCombatState.localTargetAndCooldown.w - _DeltaTime, 0.0);
    float muzzleFlash = DecayCombatMuzzleFlash(previousCombatState.muzzleFlash);
    float impactFlash = DecayCombatImpactFlash(previousCombatState.localImpactNormalAndHit.w);
    float hitFlash = DecayCombatHitFlash(previousCombatState.healthAndHitFeedback.y);
    if (lastDamage > 0.001)
        hitFlash = 1.0;
    float4 healthAndHitFeedback = BuildCombatHealthFeedback(currentHealth, hitFlash, lastDamage);
    InstanceCombatStateData combatState = BuildDefaultCombatState(
        selfState,
        cooldown,
        previousCombatState.debugShotInfoPacked,
        muzzleFlash,
        healthAndHitFeedback);
    bool shouldPreserveShotVisual = CanPreserveCombatShotVisual(previousCombatState, muzzleFlash, impactFlash);
    if (IsDeathStateDead(selfDeathState))
    {
        combatState.localTargetAndCooldown.w = 0.0;
        combatState.muzzleFlash = 0.0;
        combatState.healthAndHitFeedback = BuildCombatHealthFeedback(0.0, 0.0, lastDamage);
        combatState.flags = CrowdVatInstanceCombatFlagDead;
        combatState.debugShotInfoPacked = PackAiDebugCombatShotInfo(-1, false, false, -1);
        combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, false);
        _CombatStateBuffer[instanceIndex] = combatState;
        return;
    }

    if (_EnableGpuInstanceCombat == 0)
    {
        combatState.debugShotInfoPacked = PackAiDebugCombatShotInfo(-1, false, false, -1);
        combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, false);
        _CombatStateBuffer[instanceIndex] = combatState;
        return;
    }

    InstanceSpawnData selfSpawnData = _SpawnData[instanceIndex];
    float combatRange = max(_CombatRange, 0.0);
    if (combatRange <= 0.0)
    {
        combatState.debugShotInfoPacked = PackAiDebugCombatShotInfo(-1, false, false, -1);
        combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, false);
        _CombatStateBuffer[instanceIndex] = combatState;
        return;
    }

    float3 originLocal = BuildCombatTargetTopLocal(selfState);
    float3 originWorld = TransformLocalPointToWorld(originLocal);
    TargetAcquisitionStateData acquisitionState = _TargetAcquisitionStateBuffer[instanceIndex];
    uint acquisitionFlags = acquisitionState.flags;
    int visibleSampleIndex = (acquisitionFlags & CrowdVatTargetAcquisitionFlagHasLineOfSight) != 0u
        ? acquisitionState.debugVisibleSampleIndex
        : -1;
    float bestTargetDistance = combatRange;
    int bestTargetIndex = acquisitionState.currentTargetIndex;
    float3 bestTargetLocal = acquisitionState.lastKnownTargetAndScore.xyz;
    bool hasAcquisitionTarget =
        (acquisitionFlags & CrowdVatTargetAcquisitionFlagHasTarget) != 0u &&
        bestTargetIndex >= 0 &&
        bestTargetIndex < _InstanceCount;

    float shotCooldownDuration = ComputeInstanceCombatCooldownDuration(instanceIndex);
    bool willFire = shotCooldownDuration > 0.0 && cooldown <= 1e-4;

    // Finalized acquisition already carries steady-state target/LOS tracking; only actual firing needs revalidation and tracing.
    [branch]
    if (!hasAcquisitionTarget)
    {
        if (willFire)
            combatState.localTargetAndCooldown.w = shotCooldownDuration;

        if (shouldPreserveShotVisual)
            PreserveCombatShotVisual(combatState, previousCombatState, impactFlash);

        _CombatStateBuffer[instanceIndex] = combatState;
        return;
    }

    float3 acquisitionTargetWorld = TransformLocalPointToWorld(bestTargetLocal);
    bestTargetDistance = length(acquisitionTargetWorld - originWorld);

    [branch]
    if (!willFire)
    {
        combatState.flags |= CrowdVatInstanceCombatFlagHasTarget;
        if ((acquisitionFlags & CrowdVatTargetAcquisitionFlagHasLineOfSight) != 0u)
            combatState.flags |= CrowdVatInstanceCombatFlagHasLineOfSight;

        combatState.localOriginAndDistance = float4(originLocal, bestTargetDistance);
        combatState.localTargetAndCooldown = float4(bestTargetLocal, cooldown);
        combatState.targetIndex = bestTargetIndex;
        combatState.debugShotInfoPacked = PackAiDebugCombatShotInfo(-1, false, false, visibleSampleIndex);
        combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, false);

        if (shouldPreserveShotVisual)
            PreserveCombatShotVisual(combatState, previousCombatState, impactFlash);

        _CombatStateBuffer[instanceIndex] = combatState;
        return;
    }

    float3 lineOfSightOriginLocal = originLocal;
    float3 lineOfSightOriginWorld = originWorld;
    bool skipSdfOcclusion = _CombatShotSpreadDegrees <= 1e-5;
    bool acquisitionTargetOccluded = false;

    float cachedSdfHitWorldDistance = combatRange;
    float3 cachedSdfHitWorld = originWorld;
    bool useCachedSdfResult = false;

    if (!TryResolveCombatTargetByIndex(
        instanceIndex,
        selfSpawnData.faction,
        combatRange,
        originLocal,
        originWorld,
        lineOfSightOriginLocal,
        lineOfSightOriginWorld,
        bestTargetIndex,
        bestTargetLocal,
        bestTargetDistance,
        acquisitionTargetOccluded,
        skipSdfOcclusion))
    {
        bestTargetIndex = -1;
    }

    if (bestTargetIndex >= 0 && skipSdfOcclusion)
    {
        float3 targetWorld = TransformLocalPointToWorld(bestTargetLocal);
        float3 rayToTarget = targetWorld - originWorld;
        float rayLength = length(rayToTarget);
        if (rayLength > 1e-5)
        {
            float3 rayDir = rayToTarget / rayLength;
            float sdfHitDist;
            float3 sdfHitPos;
            if (TryTraceCombatEnvironmentSdfHit(originWorld, rayDir, rayLength, sdfHitDist, sdfHitPos))
            {
                cachedSdfHitWorldDistance = sdfHitDist;
                cachedSdfHitWorld = sdfHitPos;
                useCachedSdfResult = true;
            }
        }
    }

    if (bestTargetIndex < 0)
    {
        if (willFire)
            combatState.localTargetAndCooldown.w = shotCooldownDuration;

        if (shouldPreserveShotVisual)
            PreserveCombatShotVisual(combatState, previousCombatState, impactFlash);

        _CombatStateBuffer[instanceIndex] = combatState;
        return;
    }

    combatState.flags |= CrowdVatInstanceCombatFlagHasTarget |
        CrowdVatInstanceCombatFlagHasLineOfSight;
    combatState.localOriginAndDistance = float4(originLocal, bestTargetDistance);
    combatState.localTargetAndCooldown = float4(bestTargetLocal, cooldown);
    combatState.targetIndex = bestTargetIndex;
    combatState.debugShotInfoPacked = PackAiDebugCombatShotInfo(-1, false, false, visibleSampleIndex);
    combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(-1, 0.0, false, false);

    if (willFire)
    {
        combatState.localTargetAndCooldown.w = shotCooldownDuration;
        combatState.muzzleFlash = 1.0;
        combatState.flags |= CrowdVatInstanceCombatFlagFiredThisFrame;

        float3 shotTargetLocal;
        float shotDistance;
        float3 shotHitNormalLocal;
        int hitAgentIndex;
        bool hitScene;
        bool blockedBySceneBeforeAgent;
        uint traceOccupantCount;
        bool hasShotHit = ResolveCombatShotHit(
            instanceIndex,
            originLocal,
            bestTargetLocal,
            combatRange,
            useCachedSdfResult,
            cachedSdfHitWorldDistance,
            cachedSdfHitWorld,
            shotTargetLocal,
            shotDistance,
            shotHitNormalLocal,
            hitAgentIndex,
            hitScene,
            blockedBySceneBeforeAgent,
            traceOccupantCount);

        combatState.localOriginAndDistance = float4(originLocal, shotDistance);
        combatState.localTargetAndCooldown.xyz = shotTargetLocal;
        combatState.localImpactNormalAndHit = float4(float3(0.0, 1.0, 0.0), hasShotHit ? 1.0 : 0.0);
        combatState.debugShotInfoPacked = PackAiDebugCombatShotInfo(hitAgentIndex, hitScene, blockedBySceneBeforeAgent, visibleSampleIndex);
        bool damageApplied = false;
        if (hitAgentIndex >= 0)
        {
            combatState.targetIndex = hitAgentIndex;
            damageApplied = ApplyCombatDamage((uint)hitAgentIndex, instanceIndex);
        }
        combatState.debugShotTraceMeta = BuildAiDebugCombatShotTraceMeta(hitAgentIndex, (float)traceOccupantCount, damageApplied, false);
    }
    else if (shouldPreserveShotVisual)
    {
        PreserveCombatShotVisual(combatState, previousCombatState, impactFlash);
    }

    _CombatStateBuffer[instanceIndex] = combatState;
}

[numthreads(64, 1, 1)]
void EvaluateSquadAcquisitionCandidates(uint3 groupId : SV_GroupID, uint3 groupThreadId : SV_GroupThreadID)
{
    uint activeSlot = groupId.x;
    uint activeCount = _CombatActiveInstanceCounterBuffer[0];
    bool validAcquisitionGroup = activeSlot < activeCount;

    uint instanceIndex = CrowdVatInvalidInstanceIndex;
    uint sourceSquadIndex = 0u;
    bool hasSquadPath = false;
    if (validAcquisitionGroup)
    {
        instanceIndex = _CombatActiveInstanceIndexBuffer[activeSlot];
        validAcquisitionGroup = instanceIndex < (uint)_InstanceCount;
    }

    if (validAcquisitionGroup)
    {
        hasSquadPath = TryGetAssignedRuntimeSquadIndex(instanceIndex, sourceSquadIndex) &&
            _CombatCandidateCapacityPerSquad > 0 &&
            sourceSquadIndex < (uint)_SquadStateCount;
    }
    validAcquisitionGroup = validAcquisitionGroup && hasSquadPath;

    if (!validAcquisitionGroup && _EnableGpuInstanceCombat != 0 && _SquadStateCount > 0)
    {
        sourceSquadIndex = activeSlot % (uint)_SquadStateCount;
        hasSquadPath = _CombatCandidateCapacityPerSquad > 0;
        validAcquisitionGroup = hasSquadPath;
    }

    uint candidateCapacity = validAcquisitionGroup ? (uint)_CombatCandidateCapacityPerSquad : 0u;
    uint squadCandidateCount = validAcquisitionGroup ? _CombatSquadCandidateCounterBuffer[sourceSquadIndex] : 0u;
    uint availableCandidates = min(squadCandidateCount, candidateCapacity);
    uint maxCandidateChecks = (uint)max(_TargetAcquisitionMaxCandidateChecks, 1);
    uint candidateLimit = validAcquisitionGroup
        ? min(min(availableCandidates, maxCandidateChecks), CrowdVatSquadAcquisitionMaxCandidateSlots)
        : 0u;

    InstanceSimulationState selfState;
    selfState.localPositionAndYaw = 0.0;
    selfState.scaleAndVelocity = float4(1.0, 0.0, 0.0, 0.0);
    InstanceSpawnData selfSpawnData;
    selfSpawnData.localPosition = 0.0;
    selfSpawnData.yawRadians = 0.0;
    selfSpawnData.uniformScale = 1.0;
    selfSpawnData.normalizedTimeOffset = 0.0;
    selfSpawnData.playbackSpeedMultiplier = 1.0;
    selfSpawnData.faction = CrowdVatFactionMaskNone;
    if (validAcquisitionGroup)
    {
        selfState = LoadSimulationState(instanceIndex);
        selfSpawnData = _SpawnData[instanceIndex];
    }
    float combatRange = max(_CombatRange, 0.0);
    float3 originLocal = BuildCombatTargetTopLocal(selfState);
    float3 originWorld = TransformLocalPointToWorld(originLocal);
    float2 selfForwardXZ = ComputeCombatForwardXZ(selfState.localPositionAndYaw.w);

    TargetAcquisitionStateData previousTargetState = BuildDefaultTargetAcquisitionState(
        selfState,
        0.0,
        0.0,
        0.0,
        0.0,
        -1);
    if (validAcquisitionGroup)
        previousTargetState = _TargetAcquisitionStateBuffer[instanceIndex];
    int currentTargetIndex = previousTargetState.currentTargetIndex;
    int lastAttackerIndex = previousTargetState.lastAttackerIndex;

    uint candidateBase = sourceSquadIndex * candidateCapacity;
    float localBestScores[3] = {-1.0, -1.0, -1.0};
    int localBestSlots[3] = {-1, -1, -1};

    [unroll]
    for (uint strideIndex = 0u; strideIndex < CrowdVatSquadAcquisitionCandidateStrideCount; strideIndex++)
    {
        uint squadSlot = strideIndex * CrowdVatDispatchThreadGroupSize + groupThreadId.x;
        float score = -1.0;
        if (squadSlot < candidateLimit)
        {
            CombatSquadCandidateData squadCandidate = _CombatSquadCandidateBuffer[candidateBase + squadSlot];
            uint neighborIndex = squadCandidate.instanceIndex;
            if (neighborIndex < (uint)_InstanceCount &&
                neighborIndex != instanceIndex &&
                AreFactionsHostile(selfSpawnData.faction, squadCandidate.faction))
            {
                float3 targetLocal = squadCandidate.targetLocalAndScale.xyz;
                float3 targetWorld = TransformLocalPointToWorld(targetLocal);
                float targetDistance = length(targetWorld - originWorld);
                if (targetDistance > 1e-4 && targetDistance <= combatRange)
                {
                    float viewDot = ComputeTargetViewDot(selfForwardXZ, originLocal, targetLocal);
                    if (viewDot >= _TargetAcquisitionFovCosine)
                    {
                        float distanceScore = 1.0 - saturate(targetDistance / max(combatRange, 1e-4));
                        float viewScore = saturate((viewDot - _TargetAcquisitionFovCosine) / max(1.0 - _TargetAcquisitionFovCosine, 1e-4));
                        score = distanceScore * max(_TargetAcquisitionDistanceScoreWeight, 0.0) +
                            viewScore * max(_TargetAcquisitionViewScoreWeight, 0.0);

                        if ((int)neighborIndex == currentTargetIndex)
                            score += max(_TargetAcquisitionCurrentTargetBonus, 0.0);

                        if ((int)neighborIndex == lastAttackerIndex)
                            score += max(_TargetAcquisitionLastAttackerBonus, 0.0);
                    }
                }
            }
        }

        if (score > localBestScores[0])
        {
            localBestSlots[2] = localBestSlots[1];
            localBestScores[2] = localBestScores[1];
            localBestSlots[1] = localBestSlots[0];
            localBestScores[1] = localBestScores[0];
            localBestSlots[0] = (int)squadSlot;
            localBestScores[0] = score;
        }
        else if (score > localBestScores[1])
        {
            localBestSlots[2] = localBestSlots[1];
            localBestScores[2] = localBestScores[1];
            localBestSlots[1] = (int)squadSlot;
            localBestScores[1] = score;
        }
        else if (score > localBestScores[2])
        {
            localBestSlots[2] = (int)squadSlot;
            localBestScores[2] = score;
        }
    }

    [unroll]
    for (uint rank = 0u; rank < CrowdVatTargetAcquisitionTopK; rank++)
    {
        uint sharedSlot = groupThreadId.x * CrowdVatTargetAcquisitionTopK + rank;
        g_SquadAcquisitionScores[sharedSlot] = localBestScores[rank];
        g_SquadAcquisitionSlots[sharedSlot] = localBestSlots[rank];
    }

    GroupMemoryBarrierWithGroupSync();

    if (groupThreadId.x == 0)
    {
        if (!validAcquisitionGroup)
            return;

        int bestSlots[3] = {-1, -1, -1};
        float bestScores[3] = {-1.0, -1.0, -1.0};

        for (uint sharedRankSlot = 0u; sharedRankSlot < CrowdVatDispatchThreadGroupSize * CrowdVatTargetAcquisitionTopK; sharedRankSlot++)
        {
            float s = g_SquadAcquisitionScores[sharedRankSlot];
            if (s < 0.0) continue;
            int squadSlot = g_SquadAcquisitionSlots[sharedRankSlot];
            if (squadSlot < 0) continue;

            if (s > bestScores[0])
            {
                bestSlots[2] = bestSlots[1]; bestScores[2] = bestScores[1];
                bestSlots[1] = bestSlots[0]; bestScores[1] = bestScores[0];
                bestSlots[0] = squadSlot; bestScores[0] = s;
            }
            else if (s > bestScores[1])
            {
                bestSlots[2] = bestSlots[1]; bestScores[2] = bestScores[1];
                bestSlots[1] = squadSlot; bestScores[1] = s;
            }
            else if (s > bestScores[2])
            {
                bestSlots[2] = squadSlot; bestScores[2] = s;
            }
        }

        uint agentCandidateBase = GetTargetAcquisitionCandidateBaseIndex(instanceIndex);
        for (uint rank = 0; rank < CrowdVatTargetAcquisitionTopK; rank++)
        {
            int bestSlot = bestSlots[rank];
            if (bestSlot < 0)
            {
                _TargetAcquisitionCandidateBuffer[agentCandidateBase + CrowdVatTargetAcquisitionSearchCandidateBaseSlot + rank] =
                    BuildInvalidTargetAcquisitionCandidate();
                continue;
            }

            CombatSquadCandidateData bestCandidate = _CombatSquadCandidateBuffer[candidateBase + (uint)bestSlot];
            float3 bestTargetLocal = bestCandidate.targetLocalAndScale.xyz;
            float3 bestTargetWorld = TransformLocalPointToWorld(bestTargetLocal);
            float bestTargetDistance = length(bestTargetWorld - originWorld);

            _TargetAcquisitionCandidateBuffer[agentCandidateBase + CrowdVatTargetAcquisitionSearchCandidateBaseSlot + rank] =
                BuildTargetAcquisitionCandidate(
                    bestCandidate.instanceIndex,
                    bestScores[rank],
                    bestTargetLocal,
                    bestTargetDistance,
                    0u);
        }
    }
}
