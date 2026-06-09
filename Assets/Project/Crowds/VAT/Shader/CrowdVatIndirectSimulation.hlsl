[numthreads(64, 1, 1)]
void PredictInstances(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    InstanceSpawnData spawnData = _SpawnData[instanceIndex];
    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    if (IsInstanceDead(instanceIndex))
    {
        StoreSimulationState(instanceIndex, BuildState(
            state.localPositionAndYaw.xyz,
            state.localPositionAndYaw.w,
            max(state.scaleAndVelocity.x, spawnData.uniformScale),
            0.0));
        return;
    }

    float2 currentXZ = GetLocalXZ(state.localPositionAndYaw);
    float2 staticAnchorXZ = spawnData.localPosition.xz;
    float2 anchorXZ = staticAnchorXZ;
    float2 formationCenterXZ = staticAnchorXZ;
    float2 commandVelocityXZ = 0.0;
    float formationDeviationRadius = max(_MaxDisplacementFromSpawn, 1e-4);
    float squadProjectionStrength = 0.0;
    float2 velocityXZ = state.scaleAndVelocity.yz;
    float uniformScale = max(state.scaleAndVelocity.x, spawnData.uniformScale);
    float yawRadians = state.localPositionAndYaw.w;
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

    bool influencedByInteraction = false;
    [loop]
    for (int sphereIndex = 0; sphereIndex < _InteractionSphereCount; sphereIndex++)
    {
        InteractionSphereData sphere = _InteractionSphereBuffer[sphereIndex];
        if (sphere.localCenterAndRadius.w <= 0.0)
            continue;

        if (IsWithinBubble(currentXZ, sphere.localCenterAndRadius.xz, sphere.localCenterAndRadius.w))
        {
            influencedByInteraction = true;
            break;
        }
    }

    bool hasRuntimeSquadMovement = hasRuntimeSquadAnchor && dot(commandVelocityXZ, commandVelocityXZ) > 1e-6;

    float damping = pow(saturate(_VelocityDamping), max(_DeltaTime * 60.0, 0.0));
    velocityXZ = commandVelocityXZ + (velocityXZ - commandVelocityXZ) * damping;
    float instanceRadius = ComputeInstanceRadius(uniformScale);
    velocityXZ = ResolveLocalAvoidanceVelocity(instanceIndex, currentXZ, instanceRadius, velocityXZ);

    float2 predictedXZ = currentXZ + velocityXZ * _DeltaTime;
    if (_GoalStiffness > 0.0 && dot(commandVelocityXZ, commandVelocityXZ) > 1e-6)
    {
        float2 goalPositionXZ = currentXZ + commandVelocityXZ * _DeltaTime;
        predictedXZ = lerp(predictedXZ, goalPositionXZ, saturate(_GoalStiffness));
    }

    if (squadProjectionStrength > 0.0)
    {
        float formationBlend = 1.0 - pow(1.0 - saturate(squadProjectionStrength), max(_DeltaTime * 60.0, 0.0));
        predictedXZ = lerp(predictedXZ, anchorXZ, formationBlend);
    }

    bool preserveInactiveReturnYaw = false;
    if (!hasRuntimeSquadMovement && _InactiveReturnStrength > 0.0)
    {
        float inactiveBlend = 1.0 - pow(1.0 - saturate(_InactiveReturnStrength), max(_DeltaTime * 60.0, 0.0));
        predictedXZ = lerp(predictedXZ, anchorXZ, inactiveBlend);
        preserveInactiveReturnYaw = !hasRuntimeSquadAnchor;
    }

    predictedXZ = ClampToSimulationBounds(predictedXZ, instanceRadius);
    float2 constraintVelocityXZ = (predictedXZ - currentXZ) / max(_DeltaTime, 1e-4);
    float groundedPredictedLocalY = ComputeGroundedLocalY(predictedXZ, state.localPositionAndYaw.y);
    float3 predictedLocalPosition = float3(predictedXZ.x, groundedPredictedLocalY, predictedXZ.y);
    predictedLocalPosition = ResolveEnvironmentConstraints(predictedLocalPosition, uniformScale, constraintVelocityXZ);
    float2 predictedVelocityXZ = (predictedLocalPosition.xz - currentXZ) / max(_DeltaTime, 1e-4);

    if (!(preserveInactiveReturnYaw &&
        dot(predictedVelocityXZ, predictedVelocityXZ) <= CrowdVatInactiveReturnYawHoldVelocityThresholdSqr))
    {
        yawRadians = ResolveFacingYawRadians(
            yawRadians,
            hasRuntimeSquadAnchor,
            desiredForwardXZ,
            commandVelocityXZ,
            predictedVelocityXZ);
    }

    StoreSimulationState(instanceIndex, BuildState(
        predictedLocalPosition,
        yawRadians,
        uniformScale,
        predictedVelocityXZ));
}

[numthreads(1, 1, 1)]
void BuildGridClearDispatchArgs(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint touchedCount = min(_GridPrevTouchedCounterBuffer[0], (uint)_GridCellCount);
    uint threadGroupCount = max(1u, (touchedCount + CrowdVatDispatchThreadGroupSize - 1u) / CrowdVatDispatchThreadGroupSize);
    _GridClearDispatchArgsBuffer[0] = threadGroupCount;
    _GridClearDispatchArgsBuffer[1] = 1u;
    _GridClearDispatchArgsBuffer[2] = 1u;
}

[numthreads(64, 1, 1)]
void ClearGrid(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint touchedIndex = dispatchThreadId.x;
    if (touchedIndex == 0u)
        _GridCurrTouchedCounterBuffer[0] = 0u;

    uint touchedCount = min(_GridPrevTouchedCounterBuffer[0], (uint)_GridCellCount);
    if (touchedIndex >= touchedCount)
        return;

    uint cellIndex = _GridPrevTouchedCellBuffer[touchedIndex];
    if (cellIndex >= (uint)_GridCellCount)
        return;

    _GridCounterBuffer[cellIndex] = 0u;
}

[numthreads(64, 1, 1)]
void BuildGrid(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveGridDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    float2 positionXZ = GetLocalXZ(state.localPositionAndYaw);
    int2 coord;
    if (!TryGetCellCoord(positionXZ, coord))
        return;

    uint cellKey = GetCellKey(coord);
    uint slot = 0u;
    InterlockedAdd(_GridCounterBuffer[cellKey], 1u, slot);
    if (slot == 0u)
    {
        uint touchedSlot = 0u;
        InterlockedAdd(_GridCurrTouchedCounterBuffer[0], 1u, touchedSlot);
        if (touchedSlot < (uint)_GridCellCount)
            _GridCurrTouchedCellBuffer[touchedSlot] = cellKey;
    }

    if (slot >= (uint)_MaxCellOccupancy)
        return;

    _GridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + slot] = instanceIndex;
}

[numthreads(64, 1, 1)]
void BuildSpatialElements(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex = dispatchThreadId.x;
    if (instanceIndex >= (uint)_InstanceCount)
        return;

    if (IsInstanceDead(instanceIndex))
    {
        StoreSpatialElement(instanceIndex, BuildEmptySpatialElement());
        return;
    }

    InstanceSpawnData spawnData = _SpawnData[instanceIndex];
    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    uint isPhysicsActive = _PhysicsActiveStateBuffer[instanceIndex];
    StoreSpatialElement(instanceIndex, BuildSpatialElement(instanceIndex, spawnData, state, isPhysicsActive));
}

[numthreads(64, 1, 1)]
void SolveCrowdCollisions(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolvePhysicsActiveDispatchInstanceIndexReadOnly(dispatchThreadId.x, instanceIndex))
        return;

    InstanceSpawnData spawnData = _SpawnData[instanceIndex];
    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    float3 originalLocalPosition = state.localPositionAndYaw.xyz;
    float2 originalXZ = originalLocalPosition.xz;
    float2 previousFrameXZ = originalXZ - state.scaleAndVelocity.yz * max(_DeltaTime, 1e-4);
    float uniformScale = max(state.scaleAndVelocity.x, spawnData.uniformScale);
    float selfInverseMass = GetCrowdPbdInverseMass(instanceIndex);
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
        originalXZ,
        anchorXZ,
        formationCenterXZ,
        commandVelocityXZ,
        formationDeviationRadius,
        squadProjectionStrength,
        desiredForwardXZ);

    float2 resolvedXZ = originalXZ;
    float radius = ComputeInstanceRadius(uniformScale);
    float2 preCrowdEnvironmentVelocityXZ = state.scaleAndVelocity.yz;
    float3 preCrowdEnvironmentPosition = float3(
        resolvedXZ.x,
        ComputeGroundedLocalY(resolvedXZ, originalLocalPosition.y),
        resolvedXZ.y);
    preCrowdEnvironmentPosition = ResolveEnvironmentConstraints(
        preCrowdEnvironmentPosition,
        uniformScale,
        preCrowdEnvironmentVelocityXZ);
    resolvedXZ = ClampToSimulationBounds(preCrowdEnvironmentPosition.xz, radius);

    if (_EnableApproximateCollision != 0 && selfInverseMass > 0.0)
    {
        int2 originCoord;
        if (TryGetCellCoord(resolvedXZ, originCoord))
        {
            float2 hardAccumulatedCorrection = 0.0;
            float2 hardStrongestCorrection = 0.0;
            float hardStrongestCorrectionLengthSquared = 0.0;
            float hardCorrectionCount = 0.0;
            int neighborCellRange = min(
                max(1, (int)ceil((radius + max(_MaxSpatialQueryElementRadius, radius)) * _InvCellSize)),
                6);

            [loop]
            for (int y = -neighborCellRange; y <= neighborCellRange; y++)
            {
                [loop]
                for (int x = -neighborCellRange; x <= neighborCellRange; x++)
                {
                    int2 neighborCoord = originCoord + int2(x, y);
                    if (any(neighborCoord < 0) || any(neighborCoord >= _GridDim))
                        continue;

                    uint cellKey = GetCellKey(neighborCoord);
                    uint occupantCount = min(_GridCounterBuffer[cellKey], (uint)_MaxCellOccupancy);
                    [loop]
                    for (uint occupantIndex = 0u; occupantIndex < occupantCount; occupantIndex++)
                    {
                        uint neighborIndex = _GridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + occupantIndex];
                        if (neighborIndex == instanceIndex || neighborIndex >= (uint)_InstanceCount)
                            continue;

                        float neighborInverseMass = GetCrowdPbdInverseMass(neighborIndex);
                        float inverseMassSum = selfInverseMass + neighborInverseMass;
                        if (inverseMassSum <= 1e-6)
                            continue;

                        InstanceSimulationState neighborState = LoadSimulationState(neighborIndex);
                        float2 neighborXZ = GetLocalXZ(neighborState.localPositionAndYaw);
                        float neighborRadius = ComputeInstanceRadius(neighborState.scaleAndVelocity.x);
                        float combinedRadius = radius + neighborRadius;
                        float separationSlop = min(max(_SelfCollisionSlop, 0.0), combinedRadius * 0.45);
                        float effectiveCombinedRadius = max(combinedRadius - separationSlop, 1e-4);
                        float2 delta = resolvedXZ - neighborXZ;
                        float distanceSquared = dot(delta, delta);
                        if (distanceSquared >= effectiveCombinedRadius * effectiveCombinedRadius)
                            continue;

                        float distance = sqrt(max(distanceSquared, 1e-8));
                        float2 direction = SafeNormalize2D(delta, PairFallbackDirection(instanceIndex, neighborIndex));
                        float constraintError = distance - effectiveCombinedRadius;
                        float selfCorrectionWeight = selfInverseMass / inverseMassSum;
                        float2 pairCorrection = -constraintError * direction * selfCorrectionWeight;
                        AccumulateCrowdPbdCorrection(
                            pairCorrection,
                            hardAccumulatedCorrection,
                            hardStrongestCorrection,
                            hardStrongestCorrectionLengthSquared,
                            hardCorrectionCount);
                    }
                }
            }

            if (hardCorrectionCount > 0.0)
            {
                float2 hardCorrection = ResolveCrowdPbdCorrection(
                    hardAccumulatedCorrection,
                    hardStrongestCorrection,
                    hardStrongestCorrectionLengthSquared,
                    _SelfCollisionMaxPushPerStep);
                resolvedXZ += hardCorrection * _SelfCollisionStrength;
            }
        }
    }

    resolvedXZ = ClampToSimulationBounds(resolvedXZ, radius);
    float2 environmentVelocityXZ = state.scaleAndVelocity.yz;
    float3 resolvedLocalPosition = float3(
        resolvedXZ.x,
        ComputeGroundedLocalY(resolvedXZ, originalLocalPosition.y),
        resolvedXZ.y);
    resolvedLocalPosition = ResolveEnvironmentConstraints(resolvedLocalPosition, uniformScale, environmentVelocityXZ);
    float2 pbdVelocityXZ = (resolvedLocalPosition.xz - previousFrameXZ) / max(_DeltaTime, 1e-4);

    float yawRadians = state.localPositionAndYaw.w;
    yawRadians = ResolveFacingYawRadians(
        yawRadians,
        hasRuntimeSquadAnchor,
        desiredForwardXZ,
        commandVelocityXZ,
        pbdVelocityXZ);

    StoreSimulationState(instanceIndex, BuildState(
        resolvedLocalPosition,
        yawRadians,
        uniformScale,
        pbdVelocityXZ));

    float4 activationMeta = _PhysicsActivationMetaBuffer[instanceIndex];
    activationMeta.z = length(resolvedLocalPosition.xz - originalXZ);
    _PhysicsActivationMetaBuffer[instanceIndex] = activationMeta;
}

