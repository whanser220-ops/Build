using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

[Flags]
public enum CrowdVatObservationCaptureFlags
{
    None = 0,
    SquadAliveCounts = 1 << 0,
    Combat = 1 << 1,
    SpatialQueries = 1 << 2,
    All = SquadAliveCounts | Combat | SpatialQueries
}

public enum CrowdVatObservationSourceKind
{
    None = 0,
    CpuMirror = 1,
    SyncGpuReadback = 2
}

[Serializable]
public struct CrowdVatObservationFrameInfo
{
    public int frameCount;
    public CrowdVatObservationSourceKind sourceKind;
    public bool isStale;
}

[Serializable]
public struct CrowdVatSquadAliveObservation
{
    public int runtimeSquadIndex;
    public int aliveCount;
}

[Serializable]
public struct CrowdVatCombatObservation
{
    public int runtimeInstanceIndex;
    public bool active;
    public bool dead;
    public bool hasTarget;
    public int runtimeTargetInstanceIndex;
    public bool hasLineOfSight;
    public float distance;
    public bool firedThisFrame;
    public float health;
    public float normalizedHealth;
    public float hitFlash;
    public float lastDamage;
    public bool hasImpact;
    public Vector3 worldImpactNormal;
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatFrameParams
{
    public float deltaTime;
    public uint frameIndex;
    public uint activeAgentCount;
    public uint eventCount;
}

[Serializable]
public enum CrowdVatGpuEventType : uint
{
    None = 0,
    Spawn = 1,
    Despawn = 2,
    SetDestination = 3,
    ChangeFormation = 4,
    ChangeMode = 5,
    Damage = 6
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatGpuEvent
{
    public CrowdVatGpuEventType type;
    public uint agentId;
    public uint arg0;
    public uint arg1;
    public Vector4 payload;
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatDirtyRange
{
    public int startIndex;
    public int count;

    public readonly bool IsValid => startIndex >= 0 && count > 0;
}

[Flags]
public enum CrowdVatAgentStateFlags : uint
{
    None = 0,
    Valid = 1 << 0,
    AssignedToSquad = 1 << 1,
    HasAnimation = 1 << 2,
    HasCombat = 1 << 3,
    HasNavigation = 1 << 4,
    HasPhysics = 1 << 5,
    Unassigned = 1u << 31
}

[Flags]
public enum CrowdVatAgentComponentMask : uint
{
    None = 0,
    Physics = 1 << 0,
    Combat = 1 << 1,
    Animation = 1 << 2,
    Navigation = 1 << 3,
    Crowd = 1 << 4
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatAgentCore
{
    public uint agentId;
    public CrowdVatAgentStateFlags stateFlags;
    public uint squadId;
    public uint behaviorId;
    public CrowdVatAgentComponentMask componentMask;
    public uint extIndex;
    public uint simulationIndex;
    public uint reserved0;
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatAgentPhysicsExt
{
    public float radius;
    public float height;
    public float maxPushPerStep;
    public uint collisionMask;
}

[Flags]
public enum CrowdVatFramePacketFlags
{
    None = 0,
    ClearRuntimeSquadData = 1 << 0,
    UploadSquadStates = 1 << 1,
    UploadSquadAliveCounts = 1 << 2,
    UploadAgentAssignments = 1 << 3,
    UploadFormationSlots = 1 << 4,
    ReplaceSpatialQueries = 1 << 5
}

public sealed class CrowdVatFramePacket
{
    public CrowdVatFrameParams frameParams;
    public CrowdVatFramePacketFlags flags;
    public CrowdVatGpuEvent[] events = Array.Empty<CrowdVatGpuEvent>();
    public CrowdVatDirtyRange[] dirtyAgentRanges = Array.Empty<CrowdVatDirtyRange>();
    public CrowdVatDirtyRange[] dirtySquadRanges = Array.Empty<CrowdVatDirtyRange>();
    public CrowdVatSpatialQueryRequest[] spatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
    public CrowdVatSquadState[] squadStates = Array.Empty<CrowdVatSquadState>();
    public int[] squadAliveCounts = Array.Empty<int>();
    public CrowdVatAgentSquadAssignment[] agentAssignments = Array.Empty<CrowdVatAgentSquadAssignment>();
    public CrowdVatFormationSlot[] formationSlots = Array.Empty<CrowdVatFormationSlot>();

    public bool HasFlag(CrowdVatFramePacketFlags flag)
    {
        return (flags & flag) != 0;
    }

    public void Reset()
    {
        frameParams = default;
        flags = CrowdVatFramePacketFlags.None;
        events = Array.Empty<CrowdVatGpuEvent>();
        dirtyAgentRanges = Array.Empty<CrowdVatDirtyRange>();
        dirtySquadRanges = Array.Empty<CrowdVatDirtyRange>();
        spatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
        squadStates = Array.Empty<CrowdVatSquadState>();
        squadAliveCounts = Array.Empty<int>();
        agentAssignments = Array.Empty<CrowdVatAgentSquadAssignment>();
        formationSlots = Array.Empty<CrowdVatFormationSlot>();
    }
}

public sealed class CrowdVatFrameInput
{
    public CrowdVatFrameParams frameParams;
    public bool clearRuntimeSquadData;
    public bool uploadSquadStates;
    public bool uploadSquadAliveCounts;
    public bool uploadAgentAssignments;
    public bool uploadFormationSlots;
    public CrowdVatGpuEvent[] events = Array.Empty<CrowdVatGpuEvent>();
    public CrowdVatDirtyRange[] dirtyAgentRanges = Array.Empty<CrowdVatDirtyRange>();
    public CrowdVatDirtyRange[] dirtySquadRanges = Array.Empty<CrowdVatDirtyRange>();
    public CrowdVatSquadState[] squadStates = Array.Empty<CrowdVatSquadState>();
    public int[] squadAliveCounts = Array.Empty<int>();
    public CrowdVatAgentSquadAssignment[] agentAssignments = Array.Empty<CrowdVatAgentSquadAssignment>();
    public CrowdVatFormationSlot[] formationSlots = Array.Empty<CrowdVatFormationSlot>();

    public void Reset()
    {
        frameParams = default;
        clearRuntimeSquadData = false;
        uploadSquadStates = false;
        uploadSquadAliveCounts = false;
        uploadAgentAssignments = false;
        uploadFormationSlots = false;
        events = Array.Empty<CrowdVatGpuEvent>();
        dirtyAgentRanges = Array.Empty<CrowdVatDirtyRange>();
        dirtySquadRanges = Array.Empty<CrowdVatDirtyRange>();
        squadStates = Array.Empty<CrowdVatSquadState>();
        squadAliveCounts = Array.Empty<int>();
        agentAssignments = Array.Empty<CrowdVatAgentSquadAssignment>();
        formationSlots = Array.Empty<CrowdVatFormationSlot>();
    }
}

public static class CrowdVatFrameInputLegacyAdapter
{
    public static void CopyToPacket(CrowdVatFrameInput frameInput, CrowdVatFramePacket framePacket)
    {
        if (frameInput == null)
            throw new ArgumentNullException(nameof(frameInput));
        if (framePacket == null)
            throw new ArgumentNullException(nameof(framePacket));

        framePacket.Reset();
        framePacket.frameParams = frameInput.frameParams;
        framePacket.events = frameInput.events ?? Array.Empty<CrowdVatGpuEvent>();
        framePacket.dirtyAgentRanges = frameInput.dirtyAgentRanges ?? Array.Empty<CrowdVatDirtyRange>();
        framePacket.dirtySquadRanges = frameInput.dirtySquadRanges ?? Array.Empty<CrowdVatDirtyRange>();
        framePacket.squadStates = frameInput.squadStates ?? Array.Empty<CrowdVatSquadState>();
        framePacket.squadAliveCounts = frameInput.squadAliveCounts ?? Array.Empty<int>();
        framePacket.agentAssignments = frameInput.agentAssignments ?? Array.Empty<CrowdVatAgentSquadAssignment>();
        framePacket.formationSlots = frameInput.formationSlots ?? Array.Empty<CrowdVatFormationSlot>();

        if (frameInput.clearRuntimeSquadData)
            framePacket.flags |= CrowdVatFramePacketFlags.ClearRuntimeSquadData;
        if (frameInput.uploadSquadStates)
            framePacket.flags |= CrowdVatFramePacketFlags.UploadSquadStates;
        if (frameInput.uploadSquadAliveCounts)
            framePacket.flags |= CrowdVatFramePacketFlags.UploadSquadAliveCounts;
        if (frameInput.uploadAgentAssignments)
            framePacket.flags |= CrowdVatFramePacketFlags.UploadAgentAssignments;
        if (frameInput.uploadFormationSlots)
            framePacket.flags |= CrowdVatFramePacketFlags.UploadFormationSlots;
    }
}

public sealed class CrowdVatFrameObservation
{
    public CrowdVatObservationFrameInfo squadAliveFrameInfo;
    public CrowdVatObservationFrameInfo combatFrameInfo;
    public CrowdVatObservationFrameInfo spatialQueryFrameInfo;
    public readonly List<CrowdVatSquadAliveObservation> squadAliveObservations = new List<CrowdVatSquadAliveObservation>(32);
    public readonly List<CrowdVatCombatObservation> combatObservations = new List<CrowdVatCombatObservation>(256);
    public readonly List<CrowdVatSpatialQueryResult> spatialQueryResults = new List<CrowdVatSpatialQueryResult>(16);
    public readonly List<CrowdVatSpatialQueryHit> spatialQueryHits = new List<CrowdVatSpatialQueryHit>(64);

    public void Clear()
    {
        squadAliveFrameInfo = default;
        combatFrameInfo = default;
        spatialQueryFrameInfo = default;
        squadAliveObservations.Clear();
        combatObservations.Clear();
        spatialQueryResults.Clear();
        spatialQueryHits.Clear();
    }
}

public sealed class CrowdVatGpuBridgeState
{
    private CrowdVatFrameParams _frameParams;
    private CrowdVatGpuEvent[] _events = Array.Empty<CrowdVatGpuEvent>();
    private CrowdVatDirtyRange[] _dirtyAgentRanges = Array.Empty<CrowdVatDirtyRange>();
    private CrowdVatDirtyRange[] _dirtySquadRanges = Array.Empty<CrowdVatDirtyRange>();
    private CrowdVatSpatialQueryRequest[] _spatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
    private CrowdVatSquadState[] _squadStates = Array.Empty<CrowdVatSquadState>();
    private CrowdVatAgentSquadAssignment[] _agentAssignments = Array.Empty<CrowdVatAgentSquadAssignment>();
    private CrowdVatFormationSlot[] _formationSlots = Array.Empty<CrowdVatFormationSlot>();
    private uint[] _squadAliveCounts = Array.Empty<uint>();
    private CrowdVatAgentCore[] _agentCore = Array.Empty<CrowdVatAgentCore>();
    private CrowdVatAgentPhysicsExt[] _agentPhysicsExt = Array.Empty<CrowdVatAgentPhysicsExt>();

    public CrowdVatFrameParams FrameParams => _frameParams;
    public CrowdVatGpuEvent[] Events => _events;
    public CrowdVatDirtyRange[] DirtyAgentRanges => _dirtyAgentRanges;
    public CrowdVatDirtyRange[] DirtySquadRanges => _dirtySquadRanges;
    public CrowdVatSpatialQueryRequest[] SpatialQueries => _spatialQueries;
    public CrowdVatSquadState[] SquadStates => _squadStates;
    public CrowdVatAgentSquadAssignment[] AgentAssignments => _agentAssignments;
    public CrowdVatFormationSlot[] FormationSlots => _formationSlots;
    public uint[] SquadAliveCounts => _squadAliveCounts;
    public CrowdVatAgentCore[] AgentCore => _agentCore;
    public CrowdVatAgentPhysicsExt[] AgentPhysicsExt => _agentPhysicsExt;
    public int ActiveSpatialQueryCount => _spatialQueries.Length;
    public int ActiveSquadStateCount => _squadStates.Length;
    public int ActiveAgentAssignmentCount => _agentAssignments.Length;
    public int ActiveFormationSlotCount => _formationSlots.Length;

    public void CaptureFrameProtocol(CrowdVatFramePacket framePacket, int maxAgentCount)
    {
        if (framePacket == null)
            throw new ArgumentNullException(nameof(framePacket));

        _frameParams = framePacket.frameParams;
        _events = CopyArray(framePacket.events);
        if (_frameParams.eventCount == 0u && _events.Length > 0)
            _frameParams.eventCount = (uint)_events.Length;

        _dirtyAgentRanges = SanitizeRanges(framePacket.dirtyAgentRanges, maxAgentCount);
        _dirtySquadRanges = SanitizeRanges(framePacket.dirtySquadRanges, int.MaxValue);
    }

    public void SetSpatialQueries(CrowdVatSpatialQueryRequest[] queries, int maxSpatialQueryCount)
    {
        if (queries == null || queries.Length == 0 || maxSpatialQueryCount <= 0)
        {
            _spatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
            return;
        }

        int count = Math.Min(queries.Length, maxSpatialQueryCount);
        _spatialQueries = new CrowdVatSpatialQueryRequest[count];
        Array.Copy(queries, _spatialQueries, count);
    }

    public void ClearSpatialQueries()
    {
        _spatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
    }

    public void SetRuntimeSquadSnapshot(
        CrowdVatSquadState[] squadStates,
        uint[] squadAliveCounts,
        CrowdVatAgentSquadAssignment[] agentAssignments,
        CrowdVatFormationSlot[] formationSlots)
    {
        _squadStates = CopyArray(squadStates);
        _squadAliveCounts = CopyArray(squadAliveCounts);
        _agentAssignments = CopyArray(agentAssignments);
        _formationSlots = CopyArray(formationSlots);
    }

    public void SetAgentDataLayout(CrowdVatAgentCore[] agentCore, CrowdVatAgentPhysicsExt[] agentPhysicsExt)
    {
        _agentCore = CopyArray(agentCore);
        _agentPhysicsExt = CopyArray(agentPhysicsExt);
    }

    private static T[] CopyArray<T>(T[] source)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<T>();

        T[] copy = new T[source.Length];
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private static CrowdVatDirtyRange[] SanitizeRanges(CrowdVatDirtyRange[] source, int maxCount)
    {
        if (source == null || source.Length == 0)
            return Array.Empty<CrowdVatDirtyRange>();

        CrowdVatDirtyRange[] sanitized = new CrowdVatDirtyRange[source.Length];
        int writeIndex = 0;
        for (int index = 0; index < source.Length; index++)
        {
            CrowdVatDirtyRange range = source[index];
            if (!range.IsValid)
                continue;

            range.startIndex = Math.Max(0, range.startIndex);
            if (maxCount != int.MaxValue)
            {
                int maxAvailable = Math.Max(0, maxCount - range.startIndex);
                range.count = Math.Min(range.count, maxAvailable);
            }

            if (!range.IsValid)
                continue;

            sanitized[writeIndex++] = range;
        }

        if (writeIndex == 0)
            return Array.Empty<CrowdVatDirtyRange>();

        if (writeIndex == sanitized.Length)
            return sanitized;

        CrowdVatDirtyRange[] resized = new CrowdVatDirtyRange[writeIndex];
        Array.Copy(sanitized, resized, writeIndex);
        return resized;
    }
}
