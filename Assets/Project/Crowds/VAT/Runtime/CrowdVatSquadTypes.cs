using System;
using System.Runtime.InteropServices;
using UnityEngine;

[Serializable]
public enum CrowdVatSquadFormationType : uint
{
    None = 0,
    Loose = 1,
    Line = 2,
    Column = 3,
    Wedge = 4,
    Block = 5,
    Ring = 6
}

[Serializable]
public enum CrowdVatSquadCommandType : uint
{
    None = 0,
    Hold = 1,
    MoveTo = 2,
    Advance = 3,
    Charge = 4,
    Retreat = 5,
    Regroup = 6
}

[Flags]
public enum CrowdVatSquadFlags : uint
{
    None = 0,
    FaceTarget = 1 << 0,
    FaceMovement = 1 << 1,
    AllowDeformation = 1 << 2
}

[Flags]
public enum CrowdVatSquadMemberFlags : uint
{
    None = 0,
    UseSlotOffsetOverride = 1 << 0,
    PreferAssignedSlot = 1 << 1,
    AllowTemporaryDetach = 1 << 2,
    Unassigned = 1u << 31
}

[Flags]
public enum CrowdVatSquadRoleMask : uint
{
    None = 0,
    Frontline = 1 << 0,
    Rearline = 1 << 1,
    Support = 1 << 2,
    Heavy = 1 << 3,
    Leader = 1 << 4,
    All = Frontline | Rearline | Support | Heavy | Leader
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatSquadState
{
    public Vector3 worldCenter;
    public Vector3 worldForward;
    public Vector3 worldTarget;
    public Vector2 formationSpacing;
    public float moveSpeed;
    public float anchorBlend;
    public float cohesionRadius;
    public float cohesionStrength;
    public CrowdVatSquadFormationType formationType;
    public CrowdVatSquadCommandType commandType;
    public CrowdVatFactionMask factionMask;
    public CrowdVatSquadFlags flags;
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatAgentSquadAssignment
{
    public uint squadId;
    public uint slotIndex;
    public CrowdVatSquadRoleMask roleMask;
    public CrowdVatSquadMemberFlags flags;
    public Vector2 slotOffsetOverride;
    public float weight;
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatFormationSlot
{
    public Vector2 localOffset;
    public CrowdVatSquadRoleMask roleMask;
    public uint rank;
    public float preferredDistance;
}
