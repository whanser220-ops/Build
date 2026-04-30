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

[Flags]
public enum CrowdVatSpatialHitFlags
{
    None = 0,
    Active = 1 << 0
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
    public CrowdVatSpatialHitFlags flags;
    public CrowdVatFaction faction;
    public float normalizedDistance;
}

[Serializable]
public struct CrowdVatInstanceCombatState
{
    public int instanceIndex;
    public bool active;
    public bool dead;
    public bool hasTarget;
    public bool hasLineOfSight;
    public bool firedThisFrame;
    public int targetIndex;
    public float distance;
    public float muzzleFlash;
    public float health;
    public float normalizedHealth;
    public float hitFlash;
    public float lastDamage;
    public Vector3 worldOrigin;
    public Vector3 worldTargetPoint;
    public bool hasImpact;
    public Vector3 worldImpactNormal;
}
