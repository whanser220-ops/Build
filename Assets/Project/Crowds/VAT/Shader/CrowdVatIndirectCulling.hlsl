void AppendVisibleRenderInstance(uint instanceIndex, InstanceSimulationState state)
{
    uint writeIndex = 0u;
    if (_VisibleLodTierCount > 1 && _VisibleLod1StartDistance > 0.0)
    {
        float3 worldCenter = TransformLocalPointToWorld(state.localPositionAndYaw.xyz);
        float3 cameraDelta = worldCenter - _VisibleCameraPosition;
        float distanceSquared = dot(cameraDelta, cameraDelta);
        float lod1DistanceSquared = _VisibleLod1StartDistance * _VisibleLod1StartDistance;
        if (distanceSquared >= lod1DistanceSquared)
        {
            if (_VisibleLodTierCount > 2 && _VisibleLod2StartDistance > _VisibleLod1StartDistance)
            {
                float lod2DistanceSquared = _VisibleLod2StartDistance * _VisibleLod2StartDistance;
                if (distanceSquared >= lod2DistanceSquared)
                {
                    InterlockedAdd(_VisibleLod2InstanceCounterBuffer[0], 1u, writeIndex);
                    _VisibleLod2InstanceIndices[writeIndex] = instanceIndex;
                    return;
                }
            }

            InterlockedAdd(_VisibleLod1InstanceCounterBuffer[0], 1u, writeIndex);
            _VisibleLod1InstanceIndices[writeIndex] = instanceIndex;
            return;
        }
    }

    InterlockedAdd(_VisibleInstanceCounterBuffer[0], 1u, writeIndex);
    _VisibleInstanceIndices[writeIndex] = instanceIndex;
}

[numthreads(64, 1, 1)]
void ClearVisibleRuntimeSquadBounds(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint squadIndex = dispatchThreadId.x;
    if (squadIndex >= (uint)_SquadStateCount)
        return;

    RuntimeSquadBoundsData bounds;
    bounds.minX = 0xFF800000u;
    bounds.minY = 0xFF800000u;
    bounds.minZ = 0xFF800000u;
    bounds.maxX = 0x007FFFFFu;
    bounds.maxY = 0x007FFFFFu;
    bounds.maxZ = 0x007FFFFFu;
    bounds.validCount = 0u;
    bounds.padding0 = 0u;
    _RuntimeSquadBoundsBuffer[squadIndex] = bounds;
    _VisibleRuntimeSquadMaskBuffer[squadIndex] = 0u;
}

[numthreads(64, 1, 1)]
void BuildVisibleRuntimeSquadBounds(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    if (IsInstanceDead(instanceIndex))
        return;

    uint squadIndex;
    if (!TryGetAssignedRuntimeSquadIndex(instanceIndex, squadIndex))
        return;

    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    float3 localMin;
    float3 localMax;
    ComputeVisibleInstanceLocalAabb(state, localMin, localMax);

    InterlockedMin(_RuntimeSquadBoundsBuffer[squadIndex].minX, EncodeOrderedFloat(localMin.x));
    InterlockedMin(_RuntimeSquadBoundsBuffer[squadIndex].minY, EncodeOrderedFloat(localMin.y));
    InterlockedMin(_RuntimeSquadBoundsBuffer[squadIndex].minZ, EncodeOrderedFloat(localMin.z));
    InterlockedMax(_RuntimeSquadBoundsBuffer[squadIndex].maxX, EncodeOrderedFloat(localMax.x));
    InterlockedMax(_RuntimeSquadBoundsBuffer[squadIndex].maxY, EncodeOrderedFloat(localMax.y));
    InterlockedMax(_RuntimeSquadBoundsBuffer[squadIndex].maxZ, EncodeOrderedFloat(localMax.z));
    InterlockedAdd(_RuntimeSquadBoundsBuffer[squadIndex].validCount, 1u);
}

[numthreads(64, 1, 1)]
void CullVisibleRuntimeSquads(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint squadIndex = dispatchThreadId.x;
    if (squadIndex >= (uint)_SquadStateCount)
        return;

    float3 localMin;
    float3 localMax;
    if (!TryLoadRuntimeSquadBounds(squadIndex, localMin, localMax))
    {
        _VisibleRuntimeSquadMaskBuffer[squadIndex] = 0u;
        return;
    }

    _VisibleRuntimeSquadMaskBuffer[squadIndex] = IsLocalBoundsVisibleInFrustum(localMin, localMax) ? 1u : 0u;
}

[numthreads(64, 1, 1)]
void BuildVisibleRuntimeInstanceList(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint instanceIndex;
    if (!TryResolveAliveDispatchInstanceIndex(dispatchThreadId.x, instanceIndex))
        return;

    if (IsInstanceDead(instanceIndex))
        return;

    if (!IsRuntimeInstanceVisibleForRender(instanceIndex))
        return;

    InstanceSimulationState state = LoadSimulationState(instanceIndex);
    if (!IsInstanceVisibleInFrustum(state))
        return;

    AppendVisibleRenderInstance(instanceIndex, state);
}

[numthreads(1, 1, 1)]
void BuildVisibleIndirectArgs(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    IndirectDrawIndexedArgsData args;
    args.indexCountPerInstance = (uint)max(_VisibleRenderArgsIndexCount, 0);
    args.instanceCount = _VisibleInstanceCounterBuffer[0];
    args.startIndex = (uint)max(_VisibleRenderArgsStartIndex, 0);
    args.baseVertexIndex = (uint)max(_VisibleRenderArgsBaseVertex, 0);
    args.startInstance = 0u;
    _VisibleRenderArgsBuffer[0] = args;
}

