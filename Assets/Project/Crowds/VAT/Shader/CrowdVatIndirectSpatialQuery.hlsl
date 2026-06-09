[numthreads(8, 1, 1)]
void ClearSpatialQueryResults(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint queryIndex = dispatchThreadId.x;
    if (queryIndex >= (uint)_SpatialQueryCount)
        return;

    SpatialQueryData query = _SpatialQueryBuffer[queryIndex];
    SpatialQueryResultData emptyResult;
    emptyResult.queryId = 0u;
    emptyResult.totalHits = 0u;
    emptyResult.writtenHits = 0u;
    emptyResult.overflow = 0u;
    _SpatialQueryResultBuffer[queryIndex] = emptyResult;

    uint hitCapacity = min(query.maxHits, (uint)_MaxSpatialQueryHits);
    uint hitBaseIndex = queryIndex * (uint)_MaxSpatialQueryHits;
    [loop]
    for (uint hitIndex = 0u; hitIndex < hitCapacity; hitIndex++)
    {
        SpatialQueryHitData emptyHit;
        emptyHit.queryId = 0u;
        emptyHit.ownerIndex = 0u;
        emptyHit.targetMask = 0u;
        emptyHit.flags = 0u;
        emptyHit.faction = 0u;
        emptyHit.normalizedDistance = 0.0;
        emptyHit.padding0 = 0u;
        emptyHit.padding1 = 0u;
        _SpatialQueryHitBuffer[hitBaseIndex + hitIndex] = emptyHit;
    }
}

[numthreads(8, 1, 1)]
void ResolveSpatialQueries(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint queryIndex = dispatchThreadId.x;
    if (queryIndex >= (uint)_SpatialQueryCount)
        return;

    SpatialQueryData query = _SpatialQueryBuffer[queryIndex];
    float queryRadius = max(query.localStartAndRadius.w, 0.0);
    float3 queryStart = query.localStartAndRadius.xyz;
    float3 queryEnd = query.localEndAndPadding.xyz;
    float broadphasePadding = queryRadius + max(_MaxSpatialQueryElementRadius, 0.0);
    float2 queryMinXZ = min(queryStart.xz, queryEnd.xz) - broadphasePadding;
    float2 queryMaxXZ = max(queryStart.xz, queryEnd.xz) + broadphasePadding;

    SpatialQueryResultData result;
    result.queryId = query.queryId;
    result.totalHits = 0u;
    result.writtenHits = 0u;
    result.overflow = 0u;

    int2 minCoord;
    int2 maxCoord;
    if (!TryGetCellRange(queryMinXZ, queryMaxXZ, minCoord, maxCoord))
    {
        _SpatialQueryResultBuffer[queryIndex] = result;
        return;
    }

    uint writeCapacity = min(query.maxHits, (uint)_MaxSpatialQueryHits);
    uint hitBaseIndex = queryIndex * (uint)_MaxSpatialQueryHits;

    [loop]
    for (int cellY = minCoord.y; cellY <= maxCoord.y; cellY++)
    {
        [loop]
        for (int cellX = minCoord.x; cellX <= maxCoord.x; cellX++)
        {
            uint cellKey = GetCellKey(int2(cellX, cellY));
            uint rawOccupantCount = _GridCounterBuffer[cellKey];
            if (rawOccupantCount > (uint)_MaxCellOccupancy)
                result.overflow = 1u;

            uint occupantCount = min(rawOccupantCount, (uint)_MaxCellOccupancy);
            [loop]
            for (uint occupantIndex = 0u; occupantIndex < occupantCount; occupantIndex++)
            {
                uint ownerIndex = _GridOccupantBuffer[cellKey * (uint)_MaxCellOccupancy + occupantIndex];
                if (ownerIndex >= (uint)_InstanceCount)
                    continue;

                if (IsInstanceDead(ownerIndex))
                    continue;

                SpatialElementData element = LoadSpatialElement(ownerIndex);
                if ((element.targetMask & query.targetMask) == 0u)
                    continue;

                if (query.factionMask != CrowdVatFactionMaskNone &&
                    (query.factionMask & element.faction) == 0u)
                {
                    continue;
                }

                if ((query.flags & CrowdVatSpatialQueryFlagActiveOnly) != 0u &&
                    (element.flags & CrowdVatSpatialElementFlagActive) == 0u)
                {
                    continue;
                }

                float normalizedDistance = 0.0;
                float3 capsuleStart = element.localCapsuleStartAndRadius.xyz;
                float3 capsuleEnd = element.localCapsuleEndAndHeight.xyz;
                float capsuleRadius = max(element.localCapsuleStartAndRadius.w, 0.0);
                bool matched = query.shape == CrowdVatSpatialQueryShapeSweepCapsule
                    ? SweepCapsuleMatches(queryStart, queryEnd, queryRadius, capsuleStart, capsuleEnd, capsuleRadius, normalizedDistance)
                    : OverlapSphereMatches(queryStart, queryRadius, capsuleStart, capsuleEnd, capsuleRadius, normalizedDistance);

                if (!matched)
                    continue;

                uint writeIndex = result.writtenHits;
                result.totalHits += 1u;
                if (writeIndex >= writeCapacity)
                {
                    result.overflow = 1u;
                    continue;
                }

                SpatialQueryHitData hit;
                hit.queryId = query.queryId;
                hit.ownerIndex = element.ownerIndex;
                hit.targetMask = element.targetMask;
                hit.flags = element.flags;
                hit.faction = element.faction;
                hit.normalizedDistance = normalizedDistance;
                hit.padding0 = 0u;
                hit.padding1 = 0u;
                _SpatialQueryHitBuffer[hitBaseIndex + writeIndex] = hit;
                result.writtenHits += 1u;
            }
        }
    }

    _SpatialQueryResultBuffer[queryIndex] = result;
}

