using System;
using UnityEngine;

public sealed partial class CrowdVatSquadController : MonoBehaviour
{
    private void ApplyToRenderer(bool forceLayoutUpload)
    {
        if (_renderer == null)
            return;

        EnsureFrameBridge();
        GatherActiveSquads(_renderer.InstanceCount, out int totalSlotCount);
        if (_activeSquadSourceIndices.Count == 0)
        {
            _framePacket.Reset();
            _framePacket.frameParams = BuildFrameParams();
            _framePacket.flags = CrowdVatFramePacketFlags.ClearRuntimeSquadData;
            _frameBridge?.ApplyFramePacket(_framePacket);
            _layoutDirty = false;
            _hasAppliedData = true;
            _lastAppliedRuntimeSquadSourceIndices = Array.Empty<int>();
            _world.Clear();
            InvalidateAliveObservationCache();
            return;
        }

        bool needsLayoutUpload = forceLayoutUpload ||
            _layoutDirty ||
            _agentAssignmentCache.Length != _renderer.InstanceCount ||
            _squadStateCache.Length != _activeSquadSourceIndices.Count ||
            _formationSlotCache.Length != totalSlotCount;

        int[] preservedAliveCounts = needsLayoutUpload
            ? BuildRuntimeSquadAliveCountsForUpload()
            : Array.Empty<int>();

        if (needsLayoutUpload)
            BuildLayoutCaches(_renderer.InstanceCount, totalSlotCount);

        BuildSquadStates();
        BuildWorldSquadTruthCache();
        _world.SetSquadTruth(_worldSquadTruthCache);

        _framePacket.Reset();
        _framePacket.frameParams = BuildFrameParams();
        _framePacket.flags = CrowdVatFramePacketFlags.UploadSquadStates;
        _framePacket.squadStates = _squadStateCache;
        _framePacket.dirtySquadRanges = BuildDirtyRanges(true, _squadStateCache != null ? _squadStateCache.Length : 0);

        if (needsLayoutUpload)
        {
            _framePacket.flags |= CrowdVatFramePacketFlags.UploadSquadAliveCounts;
            _framePacket.flags |= CrowdVatFramePacketFlags.UploadAgentAssignments;
            _framePacket.flags |= CrowdVatFramePacketFlags.UploadFormationSlots;
            _framePacket.squadAliveCounts = preservedAliveCounts;
            _framePacket.agentAssignments = _agentAssignmentCache;
            _framePacket.formationSlots = _formationSlotCache;
            _framePacket.dirtyAgentRanges = BuildDirtyRanges(true, _agentAssignmentCache != null ? _agentAssignmentCache.Length : 0);
        }

        InvalidateAliveObservationCache();
        _frameBridge?.ApplyFramePacket(_framePacket);

        _layoutDirty = false;
        _hasAppliedData = true;
        CacheLastAppliedRuntimeSquadSourceIndices();
    }

    private int[] BuildRuntimeSquadAliveCountsForUpload()
    {
        if (_activeSquadSourceIndices.Count == 0)
            return Array.Empty<int>();

        int[] aliveCounts = new int[_activeSquadSourceIndices.Count];
        Array.Copy(_runtimeSquadInitialAliveCountCache, aliveCounts, aliveCounts.Length);

        if (_renderer == null)
            return aliveCounts;

        if (_lastAppliedRuntimeSquadSourceIndices != null &&
            _lastAppliedRuntimeSquadSourceIndices.Length > 0 &&
            TryRefreshRuntimeSquadAliveObservations())
        {
            for (int previousRuntimeIndex = 0; previousRuntimeIndex < _lastAppliedRuntimeSquadSourceIndices.Length; previousRuntimeIndex++)
            {
                int sourceSquadIndex = _lastAppliedRuntimeSquadSourceIndices[previousRuntimeIndex];
                if (sourceSquadIndex < 0 ||
                    _sourceSquadToRuntimeIndexCache == null ||
                    sourceSquadIndex >= _sourceSquadToRuntimeIndexCache.Length)
                {
                    continue;
                }

                int currentRuntimeIndex = _sourceSquadToRuntimeIndexCache[sourceSquadIndex];
                if (currentRuntimeIndex < 0 || currentRuntimeIndex >= aliveCounts.Length)
                    continue;

                if (_world.TryGetRuntimeSquadAliveCount(previousRuntimeIndex, out int observedAliveCount))
                    aliveCounts[currentRuntimeIndex] = Mathf.Clamp(observedAliveCount, 0, aliveCounts[currentRuntimeIndex]);
            }

            return aliveCounts;
        }

        EnsureFrameBridge();
        if (_renderer.GpuInstanceCombatEnabled &&
            _frameBridge != null &&
            _frameBridge.ObservationHub.TryCaptureFrameObservation(_frameObservation, CrowdVatObservationCaptureFlags.Combat))
        {
            int combatStateCount = _frameObservation.combatObservations.Count;
            if (combatStateCount <= 0)
                return aliveCounts;

            Array.Clear(aliveCounts, 0, aliveCounts.Length);
            for (int runtimeSquadIndex = 0; runtimeSquadIndex < _activeSquadSourceIndices.Count; runtimeSquadIndex++)
            {
                int sourceSquadIndex = _activeSquadSourceIndices[runtimeSquadIndex];
                SquadAuthoring squad = _squads[sourceSquadIndex];
                if (!TryGetActiveMemberRange(squad, _renderer.InstanceCount, out int memberStartIndex, out int memberCount))
                    continue;

                int memberEndExclusive = Mathf.Min(memberStartIndex + memberCount, combatStateCount);
                int aliveCount = 0;
                for (int instanceIndex = memberStartIndex; instanceIndex < memberEndExclusive; instanceIndex++)
                {
                    if (!_frameObservation.combatObservations[instanceIndex].dead)
                        aliveCount++;
                }

                aliveCounts[runtimeSquadIndex] = aliveCount;
            }

            return aliveCounts;
        }

        return aliveCounts;
    }

    private void CacheLastAppliedRuntimeSquadSourceIndices()
    {
        if (_activeSquadSourceIndices.Count <= 0)
        {
            _lastAppliedRuntimeSquadSourceIndices = Array.Empty<int>();
            return;
        }

        if (_lastAppliedRuntimeSquadSourceIndices == null ||
            _lastAppliedRuntimeSquadSourceIndices.Length != _activeSquadSourceIndices.Count)
        {
            _lastAppliedRuntimeSquadSourceIndices = new int[_activeSquadSourceIndices.Count];
        }

        for (int runtimeSquadIndex = 0; runtimeSquadIndex < _activeSquadSourceIndices.Count; runtimeSquadIndex++)
            _lastAppliedRuntimeSquadSourceIndices[runtimeSquadIndex] = _activeSquadSourceIndices[runtimeSquadIndex];
    }

    private CrowdVatFrameParams BuildFrameParams()
    {
        return new CrowdVatFrameParams
        {
            deltaTime = Mathf.Max(Time.deltaTime, 0.0f),
            frameIndex = Application.isPlaying ? (uint)Mathf.Max(Time.frameCount, 0) : 0u,
            activeAgentCount = (uint)Mathf.Max(_renderer != null ? _renderer.InstanceCount : 0, 0),
            eventCount = (uint)(_framePacket.events != null ? _framePacket.events.Length : 0)
        };
    }

    private static CrowdVatDirtyRange[] BuildDirtyRanges(bool shouldUpload, int count)
    {
        if (!shouldUpload || count <= 0)
            return Array.Empty<CrowdVatDirtyRange>();

        return new[]
        {
            new CrowdVatDirtyRange
            {
                startIndex = 0,
                count = count
            }
        };
    }

    private void BuildLayoutCaches(int instanceCount, int totalSlotCount)
    {
        if (_agentAssignmentCache == null || _agentAssignmentCache.Length != instanceCount)
            _agentAssignmentCache = new CrowdVatAgentSquadAssignment[instanceCount];

        CrowdVatAgentSquadAssignment unassignedAssignment = CreateUnassignedAssignment();
        for (int instanceIndex = 0; instanceIndex < _agentAssignmentCache.Length; instanceIndex++)
            _agentAssignmentCache[instanceIndex] = unassignedAssignment;

        if (_formationSlotCache == null || _formationSlotCache.Length != totalSlotCount)
            _formationSlotCache = new CrowdVatFormationSlot[totalSlotCount];

        int slotWriteIndex = 0;
        for (int squadRuntimeIndex = 0; squadRuntimeIndex < _activeSquadSourceIndices.Count; squadRuntimeIndex++)
        {
            SquadAuthoring squad = _squads[_activeSquadSourceIndices[squadRuntimeIndex]];
            if (!TryGetActiveMemberRange(squad, instanceCount, out int memberStartIndex, out int memberCount))
                continue;

            bool usesRuntimeFormation = HasRuntimeFormationSlots(squad);
            int slotBaseIndex = slotWriteIndex;
            if (usesRuntimeFormation)
            {
                WriteFormationSlots(squad, slotBaseIndex, memberCount);
                slotWriteIndex += memberCount;
            }

            CrowdVatSquadMemberFlags memberFlags = squad.memberFlags &
                ~CrowdVatSquadMemberFlags.Unassigned &
                ~CrowdVatSquadMemberFlags.PreferAssignedSlot &
                ~CrowdVatSquadMemberFlags.UseSlotOffsetOverride;
            for (int localMemberIndex = 0; localMemberIndex < memberCount; localMemberIndex++)
            {
                int instanceIndex = memberStartIndex + localMemberIndex;
                _agentAssignmentCache[instanceIndex] = new CrowdVatAgentSquadAssignment
                {
                    squadId = (uint)squadRuntimeIndex,
                    slotIndex = usesRuntimeFormation ? (uint)(slotBaseIndex + localMemberIndex) : uint.MaxValue,
                    roleMask = squad.memberRoleMask,
                    flags = memberFlags,
                    slotOffsetOverride = Vector2.zero,
                    weight = Mathf.Max(0.0f, squad.memberWeight)
                };
            }
        }

        EnsureCombatSquadCoverage();
    }

    private void EnsureCombatSquadCoverage()
    {
        if (_renderer == null || !_renderer.GpuInstanceCombatEnabled || _activeSquadSourceIndices.Count == 0)
            return;

        SquaredDistance[] nearest = BuildNearestSquadMapByInstanceIndex(_agentAssignmentCache.Length);
        for (int instanceIndex = 0; instanceIndex < _agentAssignmentCache.Length; instanceIndex++)
        {
            if ((_agentAssignmentCache[instanceIndex].flags & CrowdVatSquadMemberFlags.Unassigned) == 0)
                continue;

            int nearestSquadIndex = nearest[instanceIndex].squadRuntimeIndex;
            if (nearestSquadIndex < 0)
                continue;

            SquadAuthoring squad = _squads[_activeSquadSourceIndices[nearestSquadIndex]];
            CrowdVatSquadMemberFlags memberFlags = squad.memberFlags &
                ~CrowdVatSquadMemberFlags.Unassigned &
                ~CrowdVatSquadMemberFlags.PreferAssignedSlot &
                ~CrowdVatSquadMemberFlags.UseSlotOffsetOverride;

            _agentAssignmentCache[instanceIndex] = new CrowdVatAgentSquadAssignment
            {
                squadId = (uint)nearestSquadIndex,
                slotIndex = uint.MaxValue,
                roleMask = squad.memberRoleMask,
                flags = memberFlags,
                slotOffsetOverride = Vector2.zero,
                weight = Mathf.Max(0.0f, squad.memberWeight)
            };
        }
    }

    private struct SquaredDistance
    {
        public int squadRuntimeIndex;
        public int distance;
    }

    private SquaredDistance[] BuildNearestSquadMapByInstanceIndex(int instanceCount)
    {
        if (_activeSquadSourceIndices.Count <= 0)
            return Array.Empty<SquaredDistance>();

        int[] squadRangeEnds = new int[_activeSquadSourceIndices.Count];
        for (int squadRuntimeIndex = 0; squadRuntimeIndex < _activeSquadSourceIndices.Count; squadRuntimeIndex++)
        {
            SquadAuthoring squad = _squads[_activeSquadSourceIndices[squadRuntimeIndex]];
            if (TryGetActiveMemberRange(squad, instanceCount, out int start, out int count))
                squadRangeEnds[squadRuntimeIndex] = start + count;
            else
                squadRangeEnds[squadRuntimeIndex] = int.MaxValue;
        }

        SquaredDistance[] result = new SquaredDistance[instanceCount];
        for (int instanceIndex = 0; instanceIndex < instanceCount; instanceIndex++)
        {
            int bestSquad = -1;
            int bestDistance = int.MaxValue;
            for (int squadRuntimeIndex = 0; squadRuntimeIndex < _activeSquadSourceIndices.Count; squadRuntimeIndex++)
            {
                int rangeEnd = squadRangeEnds[squadRuntimeIndex];
                int distance = instanceIndex < rangeEnd
                    ? rangeEnd - instanceIndex
                    : instanceIndex - rangeEnd + 1;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSquad = squadRuntimeIndex;
                }
            }
            result[instanceIndex] = new SquaredDistance { squadRuntimeIndex = bestSquad, distance = bestDistance };
        }

        return result;
    }

    private void BuildSquadStates()
    {
        if (_squadStateCache == null || _squadStateCache.Length != _activeSquadSourceIndices.Count)
            _squadStateCache = new CrowdVatSquadState[_activeSquadSourceIndices.Count];

        for (int squadRuntimeIndex = 0; squadRuntimeIndex < _activeSquadSourceIndices.Count; squadRuntimeIndex++)
        {
            int sourceSquadIndex = _activeSquadSourceIndices[squadRuntimeIndex];
            SquadAuthoring squad = _squads[sourceSquadIndex];
            Vector3 centerPosition = squad.centerTransform != null ? squad.centerTransform.position : transform.position;
            Vector3 forward = ResolveWorldForward(sourceSquadIndex, squad, centerPosition);
            Vector3 targetPosition = ResolveWorldTargetPosition(squad, centerPosition, forward);
            float runtimeMoveSpeed = ResolveRuntimeMoveSpeed(sourceSquadIndex, squad);
            Vector2 formationSpacing = ResolveFormationSpacing(squad);
            float formationStrength = ResolveFormationStrength(squad);

            _squadStateCache[squadRuntimeIndex] = new CrowdVatSquadState
            {
                worldCenter = centerPosition,
                worldForward = forward,
                worldTarget = targetPosition,
                formationSpacing = formationSpacing,
                moveSpeed = runtimeMoveSpeed,
                anchorBlend = formationStrength,
                cohesionRadius = 0.0f,
                cohesionStrength = 0.0f,
                formationType = squad.formationType,
                commandType = squad.commandType,
                factionMask = squad.factionMask,
                flags = squad.flags
            };
        }
    }

    private void GatherActiveSquads(int instanceCount, out int totalSlotCount)
    {
        _activeSquadSourceIndices.Clear();
        totalSlotCount = 0;

        if (_squads == null)
            return;

        if (_sourceSquadToRuntimeIndexCache == null || _sourceSquadToRuntimeIndexCache.Length != _squads.Length)
            _sourceSquadToRuntimeIndexCache = new int[_squads.Length];

        Array.Fill(_sourceSquadToRuntimeIndexCache, -1);

        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            if (!TryGetActiveMemberRange(squad, instanceCount, out _, out int memberCount))
                continue;

            int runtimeSquadIndex = _activeSquadSourceIndices.Count;
            _activeSquadSourceIndices.Add(squadIndex);
            _sourceSquadToRuntimeIndexCache[squadIndex] = runtimeSquadIndex;
            if (HasRuntimeFormationSlots(squad))
                totalSlotCount += memberCount;
        }

        if (_runtimeSquadInitialAliveCountCache == null || _runtimeSquadInitialAliveCountCache.Length != _activeSquadSourceIndices.Count)
            _runtimeSquadInitialAliveCountCache = new int[_activeSquadSourceIndices.Count];

        for (int runtimeSquadIndex = 0; runtimeSquadIndex < _runtimeSquadInitialAliveCountCache.Length; runtimeSquadIndex++)
        {
            SquadAuthoring squad = _squads[_activeSquadSourceIndices[runtimeSquadIndex]];
            TryGetActiveMemberRange(squad, instanceCount, out _, out int memberCount);
            _runtimeSquadInitialAliveCountCache[runtimeSquadIndex] = memberCount;
        }
    }

    private void WriteFormationSlots(SquadAuthoring squad, int slotBaseIndex, int memberCount)
    {
        for (int localMemberIndex = 0; localMemberIndex < memberCount; localMemberIndex++)
        {
            int slotIndex = slotBaseIndex + localMemberIndex;
            if (slotIndex < 0 || slotIndex >= _formationSlotCache.Length)
                continue;

            Vector2 localOffset = ResolveFormationSlotOffset(squad, localMemberIndex, memberCount);
            _formationSlotCache[slotIndex] = new CrowdVatFormationSlot
            {
                localOffset = localOffset,
                roleMask = squad.memberRoleMask,
                rank = (uint)localMemberIndex,
                preferredDistance = localOffset.magnitude
            };
        }
    }

    private bool TryGetActiveMemberRange(SquadAuthoring squad, int instanceCount, out int memberStartIndex, out int memberCount)
    {
        memberStartIndex = 0;
        memberCount = 0;
        if (squad == null || !squad.enabled || squad.centerTransform == null || instanceCount <= 0)
            return false;

        if (squad.memberStartIndex >= instanceCount)
            return false;

        memberStartIndex = Mathf.Clamp(squad.memberStartIndex, 0, Mathf.Max(0, instanceCount - 1));
        int remainingCount = Mathf.Max(0, instanceCount - memberStartIndex);
        memberCount = Mathf.Clamp(squad.memberCount, 0, remainingCount);
        return memberCount > 0;
    }

    private bool IsSourceSquadSelectable(int squadIndex)
    {
        if (squadIndex < 0 ||
            _squads == null ||
            squadIndex >= _squads.Length)
        {
            return false;
        }

        if (!Application.isPlaying ||
            _renderer == null ||
            !_renderer.GpuInstanceCombatEnabled)
        {
            return true;
        }

        EnsureSelectableStateCache();
        int currentFrame = Time.frameCount;
        if (squadIndex < _sourceSquadSelectableFrameCache.Length &&
            _sourceSquadSelectableFrameCache[squadIndex] == currentFrame)
        {
            return _sourceSquadSelectableValueCache[squadIndex];
        }

        if (!TryGetRuntimeSquadAliveCountForSourceSquad(squadIndex, out int aliveCount))
            return CacheSelectableState(squadIndex, true);

        return CacheSelectableState(squadIndex, aliveCount > 0);
    }

    private bool ConsumeTrackedTransformChanges()
    {
        bool changed = false;
        if (_squads == null)
            return false;

        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            if (squad == null)
                continue;

            changed |= ConsumeTransformChanged(squad.centerTransform);
            changed |= ConsumeTransformChanged(squad.forwardTransform);
            changed |= ConsumeTransformChanged(squad.targetTransform);
        }

        return changed;
    }

    private bool AdvanceCentersTowardsTargets(float deltaTime)
    {
        if (!_driveCenterToTargetInPlayMode || !Application.isPlaying || _squads == null)
            return false;

        float safeDeltaTime = Mathf.Max(deltaTime, 1e-4f);
        bool changed = false;
        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            if (squad == null || !squad.enabled || squad.centerTransform == null || squad.targetTransform == null)
                continue;

            if (!ShouldAdvanceCenterFromCommand(squad.commandType))
                continue;

            float moveSpeed = Mathf.Max(0.0f, squad.moveSpeed);
            if (moveSpeed <= 0.0f)
                continue;

            Vector3 currentPosition = squad.centerTransform.position;
            Vector3 forward = ResolveWorldForward(squadIndex, squad, currentPosition);
            Vector3 targetPosition = ResolveWorldTargetPosition(squad, currentPosition, forward);
            Vector3 toTarget = targetPosition - currentPosition;
            toTarget.y = 0.0f;

            float distance = toTarget.magnitude;
            if (distance <= 1e-4f)
                continue;

            Vector3 desiredMoveDirection = squad.commandType == CrowdVatSquadCommandType.Retreat
                ? -toTarget / distance
                : toTarget / distance;
            float waypointDistance = distance;

            if (squad.commandType != CrowdVatSquadCommandType.Retreat && distance <= _arrivalDistance)
                continue;

            float maxStep = moveSpeed * safeDeltaTime;
            maxStep = Mathf.Min(maxStep, Mathf.Max(0.0f, waypointDistance));
            if (squad.commandType != CrowdVatSquadCommandType.Retreat)
                maxStep = Mathf.Min(maxStep, Mathf.Max(0.0f, distance - _arrivalDistance));

            if (maxStep <= 1e-5f)
                continue;

            Vector3 currentHeading = ResolveWorldForward(squadIndex, squad, currentPosition);
            Vector3 moveDirection = RotateHorizontalDirectionTowards(currentHeading, desiredMoveDirection, _formationTurnSpeedDegreesPerSecond * safeDeltaTime);
            if (moveDirection.sqrMagnitude <= 1e-6f)
                moveDirection = desiredMoveDirection;

            Vector3 nextPosition = currentPosition + moveDirection * maxStep;
            nextPosition.y = currentPosition.y;

            if ((nextPosition - currentPosition).sqrMagnitude <= 1e-8f)
                continue;

            squad.centerTransform.position = nextPosition;
            squad.centerTransform.hasChanged = true;
            changed = true;
        }

        return changed;
    }

}
