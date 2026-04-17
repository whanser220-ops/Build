using System;
using UnityEngine;

[Serializable]
public enum CrowdVatFaction : uint
{
    None = 0,
    CampA = 1,
    CampB = 2
}

[Flags]
public enum CrowdVatFactionMask : uint
{
    None = 0,
    CampA = 1 << 0,
    CampB = 1 << 1,
    All = CampA | CampB
}

public enum CrowdVatFactionLayoutMode
{
    TwoOpposingFactions = 0,
    SingleCrowd = 1
}

public enum CrowdVatFactionSplitAxis
{
    Depth = 0,
    Width = 1
}

[Flags]
public enum CrowdVatSpatialTargetMask
{
    None = 0,
    CrowdAgent = 1 << 0
}

public enum CrowdVatSpatialQueryShape
{
    OverlapSphere = 0,
    SweepCapsule = 1
}

[Flags]
public enum CrowdVatSpatialQueryFlags
{
    None = 0,
    ActiveOnly = 1 << 0
}

[Serializable]
public struct CrowdVatSpatialQueryRequest
{
    public int queryId;
    public CrowdVatSpatialTargetMask targetMask;
    public CrowdVatFactionMask factionMask;
    public CrowdVatSpatialQueryShape shape;
    public CrowdVatSpatialQueryFlags flags;
    public Vector3 worldStart;
    public Vector3 worldEnd;
    public float radius;
    public uint maxHits;
}

[Serializable]
public struct CrowdVatSpatialQueryResult
{
    public int queryId;
    public int totalHits;
    public int writtenHits;
    public bool overflow;
}

[Serializable]
public struct CrowdVatSpatialQueryHit
{
    public int queryId;
    public int ownerIndex;
    public CrowdVatSpatialTargetMask targetMask;
    public CrowdVatSpatialQueryFlags flags;
    public CrowdVatFaction faction;
    public float normalizedDistance;
}
