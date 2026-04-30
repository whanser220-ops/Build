using System;
using UnityEngine;

[Serializable]
public struct CrowdVatWorldSquadTruth
{
    public int sourceSquadIndex;
    public bool enabled;
    public int memberStartIndex;
    public int memberCount;
    public CrowdVatSquadFormationType formationType;
    public CrowdVatSquadCommandType commandType;
    public CrowdVatFactionMask factionMask;
    public CrowdVatSquadFlags flags;
    public CrowdVatSquadRoleMask memberRoleMask;
    public CrowdVatSquadMemberFlags memberFlags;
    public Vector2 formationSpacing;
    public float moveSpeed;
    public float anchorBlend;
    public float cohesionRadius;
    public float cohesionStrength;
    public Vector3 worldCenter;
    public Vector3 worldForward;
    public Vector3 worldTarget;
}

[Serializable]
public struct CrowdVatWorldSquadObservation
{
    public int runtimeSquadIndex;
    public int aliveCount;
    public bool hasObservation;
}
