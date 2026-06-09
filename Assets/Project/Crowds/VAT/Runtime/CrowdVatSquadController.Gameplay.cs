using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class CrowdVatSquadController : MonoBehaviour
{
    private static CrowdVatAgentSquadAssignment CreateUnassignedAssignment()
    {
        return new CrowdVatAgentSquadAssignment
        {
            squadId = 0u,
            slotIndex = 0u,
            roleMask = CrowdVatSquadRoleMask.None,
            flags = CrowdVatSquadMemberFlags.Unassigned,
            slotOffsetOverride = Vector2.zero,
            weight = 0.0f
        };
    }

    public bool TryGetFirstActiveSquadIndex(out int squadIndex)
    {
        squadIndex = -1;
        ResolveRendererReference();
        int instanceCount = _renderer != null ? _renderer.InstanceCount : int.MaxValue;

        if (_squads == null)
            return false;

        for (int index = 0; index < _squads.Length; index++)
        {
            if (TryGetActiveMemberRange(_squads[index], instanceCount, out _, out _) &&
                IsSourceSquadSelectable(index))
            {
                squadIndex = index;
                return true;
            }
        }

        return false;
    }

    public bool TryGetSquadName(int squadIndex, out string squadName)
    {
        squadName = string.Empty;
        if (!TryGetSquadForRuntimeAccess(squadIndex, out SquadAuthoring squad, out _, out _))
            return false;

        squadName = string.IsNullOrWhiteSpace(squad.name) ? $"Squad_{squadIndex:00}" : squad.name;
        return true;
    }

    public bool TryGetSquadCenter(int squadIndex, out Vector3 worldCenter)
    {
        worldCenter = Vector3.zero;
        if (!TryGetSquadForRuntimeAccess(squadIndex, out SquadAuthoring squad, out _, out _))
            return false;

        worldCenter = squad.centerTransform != null ? squad.centerTransform.position : transform.position;
        return true;
    }

    public bool TryGetSquadFactionMask(int squadIndex, out CrowdVatFactionMask factionMask)
    {
        factionMask = CrowdVatFactionMask.None;
        if (!TryGetSquadForRuntimeAccess(squadIndex, out SquadAuthoring squad, out _, out _))
            return false;

        factionMask = squad.factionMask;
        return true;
    }

    public bool TryGetSquadSelectionSphere(int squadIndex, out Vector3 worldCenter, out float radius)
    {
        worldCenter = Vector3.zero;
        radius = 0.0f;
        if (!TryGetSquadSelectionState(squadIndex, out Vector3 squadCenter, out radius))
            return false;

        worldCenter = BuildSelectionSphereCenter(squadCenter);
        return true;
    }

    public bool TryGetSquadSelectionState(int squadIndex, out Vector3 squadCenter, out float selectionRadius)
    {
        squadCenter = Vector3.zero;
        selectionRadius = 0.0f;
        if (!TryGetSquadForRuntimeAccess(squadIndex, out SquadAuthoring squad, out _, out int memberCount))
            return false;

        return TryBuildSquadSelectionState(squadIndex, squad, memberCount, out squadCenter, out selectionRadius);
    }

    public bool TryGetSquadCommand(int squadIndex, out CrowdVatSquadCommandType commandType)
    {
        commandType = CrowdVatSquadCommandType.None;
        if (!TryGetSquadForRuntimeAccess(squadIndex, out SquadAuthoring squad, out _, out _))
            return false;

        commandType = squad.commandType;
        return true;
    }

    public bool TryGetClosestSquad(
        CrowdVatFactionMask factionMask,
        Vector3 worldPoint,
        out int squadIndex,
        out Vector3 worldCenter,
        out float horizontalDistance)
    {
        squadIndex = -1;
        worldCenter = Vector3.zero;
        horizontalDistance = 0.0f;
        ResolveRendererReference();
        int instanceCount = _renderer != null ? _renderer.InstanceCount : 0;
        if (_squads == null)
            return false;

        CrowdVatFactionMask allowedMask = factionMask == CrowdVatFactionMask.None ? CrowdVatFactionMask.All : factionMask;
        float bestDistanceSqr = float.PositiveInfinity;
        for (int index = 0; index < _squads.Length; index++)
        {
            SquadAuthoring squad = _squads[index];
            if (!TryGetActiveMemberRange(squad, instanceCount, out _, out _) ||
                !IsSourceSquadSelectable(index))
            {
                continue;
            }

            if ((squad.factionMask & allowedMask) == CrowdVatFactionMask.None)
                continue;

            Vector3 candidateCenter = squad.centerTransform != null ? squad.centerTransform.position : transform.position;
            Vector3 delta = candidateCenter - worldPoint;
            delta.y = 0.0f;
            float distanceSqr = delta.sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            squadIndex = index;
            worldCenter = candidateCenter;
        }

        if (squadIndex < 0)
            return false;

        horizontalDistance = Mathf.Sqrt(bestDistanceSqr);
        return true;
    }

    public bool TryRaycastSquad(Ray ray, float maxDistance, CrowdVatFactionMask factionMask, out int squadIndex, out float hitDistance)
    {
        squadIndex = -1;
        hitDistance = float.PositiveInfinity;
        ResolveRendererReference();
        int instanceCount = _renderer != null ? _renderer.InstanceCount : 0;
        if (_squads == null || maxDistance <= 0.0f)
            return false;

        CrowdVatFactionMask allowedMask = factionMask == CrowdVatFactionMask.None ? CrowdVatFactionMask.All : factionMask;
        float bestDistance = float.PositiveInfinity;

        for (int index = 0; index < _squads.Length; index++)
        {
            SquadAuthoring squad = _squads[index];
            if (!TryGetActiveMemberRange(squad, instanceCount, out _, out int memberCount) ||
                !IsSourceSquadSelectable(index))
            {
                continue;
            }

            if ((squad.factionMask & allowedMask) == CrowdVatFactionMask.None)
                continue;

            if (!TryBuildSquadSelectionState(index, squad, memberCount, out Vector3 squadCenter, out float radius))
                continue;

            Vector3 center = BuildSelectionSphereCenter(squadCenter);
            if (!TryIntersectRaySphere(ray, center, radius, maxDistance, out float distance))
                continue;

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            squadIndex = index;
        }

        if (squadIndex < 0)
            return false;

        hitDistance = bestDistance;
        return true;
    }

    public bool IsSquadSelectable(int squadIndex)
    {
        return TryGetSquadForRuntimeAccess(squadIndex, out _, out _, out _);
    }

    public bool TrySetSquadCommand(int squadIndex, CrowdVatSquadCommandType commandType)
    {
        if (!TryGetSquadForRuntimeAccess(squadIndex, out SquadAuthoring squad, out _, out _))
            return false;

        squad.commandType = commandType;
        ApplyToRenderer(forceLayoutUpload: _layoutDirty || !_hasAppliedData);
        return true;
    }

    public bool TryCommandMoveTo(int squadIndex, Vector3 worldPosition, CrowdVatSquadCommandType commandType, bool updateForward)
    {
        if (!TryGetSquadForRuntimeAccess(squadIndex, out SquadAuthoring squad, out _, out _))
            return false;

        Transform ownerRoot = _renderer != null ? _renderer.transform : transform;
        string squadLabel = string.IsNullOrWhiteSpace(squad.name) ? $"Squad_{squadIndex:00}" : squad.name;

        if (squad.targetTransform == null)
        {
            squad.targetTransform = CreateAnchorTransform(ownerRoot, $"{squadLabel}_Target", worldPosition);
            _layoutDirty = true;
        }
        else
        {
            squad.targetTransform.position = worldPosition;
            squad.targetTransform.hasChanged = true;
        }

        if (updateForward && squad.centerTransform != null)
        {
            Vector3 resolvedTargetPosition = squad.targetTransform != null ? squad.targetTransform.position : worldPosition;
            Vector3 direction = resolvedTargetPosition - squad.centerTransform.position;
            direction.y = 0.0f;
            if (direction.sqrMagnitude > 1e-6f)
            {
                Vector3 normalizedDirection = direction.normalized;
                float forwardDistance = Mathf.Max(2.0f, _defaultTargetDistance * 0.5f);
                Vector3 forwardPosition = squad.centerTransform.position + normalizedDirection * forwardDistance;

                if (squad.forwardTransform == null)
                {
                    squad.forwardTransform = CreateAnchorTransform(ownerRoot, $"{squadLabel}_Forward", forwardPosition);
                    _layoutDirty = true;
                }
                else
                {
                    squad.forwardTransform.position = forwardPosition;
                    squad.forwardTransform.hasChanged = true;
                }
            }
        }

        squad.commandType = commandType;
        ApplyToRenderer(forceLayoutUpload: _layoutDirty || !_hasAppliedData);
        return true;
    }

    private Vector3 ResolveWorldForward(int squadIndex, SquadAuthoring squad, Vector3 centerPosition)
    {
        if (Application.isPlaying &&
            ShouldAdvanceCenterFromCommand(squad != null ? squad.commandType : CrowdVatSquadCommandType.None) &&
            TryGetSquadMotionState(squadIndex, out SquadMotionState motionState))
        {
            Vector3 runtimeForward = motionState.worldForward;
            runtimeForward.y = 0.0f;
            if (runtimeForward.sqrMagnitude > 1e-6f)
                return runtimeForward.normalized;

            Vector3 runtimeVelocity = motionState.worldVelocity;
            runtimeVelocity.y = 0.0f;
            if (runtimeVelocity.sqrMagnitude > 1e-6f)
                return runtimeVelocity.normalized;
        }

        return ResolveAuthoredWorldForward(squad, centerPosition);
    }

    private float ResolveRuntimeMoveSpeed(int squadIndex, SquadAuthoring squad)
    {
        float authoredMoveSpeed = squad != null ? Mathf.Max(0.0f, squad.moveSpeed) : 0.0f;
        if (!Application.isPlaying ||
            !ShouldAdvanceCenterFromCommand(squad != null ? squad.commandType : CrowdVatSquadCommandType.None))
        {
            return authoredMoveSpeed;
        }

        if (!TryGetSquadMotionState(squadIndex, out SquadMotionState motionState))
            return HasPendingRuntimeMove(squadIndex, squad) ? authoredMoveSpeed : 0.0f;

        Vector3 runtimeVelocity = motionState.worldVelocity;
        runtimeVelocity.y = 0.0f;
        if (runtimeVelocity.sqrMagnitude > 1e-6f)
            return runtimeVelocity.magnitude;

        return HasPendingRuntimeMove(squadIndex, squad) ? authoredMoveSpeed : 0.0f;
    }

    private bool HasPendingRuntimeMove(int squadIndex, SquadAuthoring squad)
    {
        if (squad == null || squad.centerTransform == null || squad.targetTransform == null)
            return false;

        Vector3 centerPosition = squad.centerTransform.position;
        Vector3 forward = ResolveWorldForward(squadIndex, squad, centerPosition);
        Vector3 targetPosition = ResolveWorldTargetPosition(squad, centerPosition, forward);
        Vector3 toTarget = targetPosition - centerPosition;
        toTarget.y = 0.0f;
        if (toTarget.sqrMagnitude <= 1e-8f)
            return false;

        if (squad.commandType == CrowdVatSquadCommandType.Retreat)
            return true;

        const float arrivalEpsilon = 0.001f;
        return toTarget.magnitude > Mathf.Max(_arrivalDistance, 0.0f) + arrivalEpsilon;
    }

    private Vector3 ResolveAuthoredWorldForward(SquadAuthoring squad, Vector3 centerPosition)
    {
        if (squad.forwardTransform != null)
        {
            Vector3 direction = squad.forwardTransform.position - centerPosition;
            direction.y = 0.0f;
            if (direction.sqrMagnitude > 1e-6f)
                return direction.normalized;
        }

        if (squad.centerTransform != null)
        {
            Vector3 fallback = squad.centerTransform.forward;
            fallback.y = 0.0f;
            if (fallback.sqrMagnitude > 1e-6f)
                return fallback.normalized;
        }

        Vector3 ownerForward = transform.forward;
        ownerForward.y = 0.0f;
        return ownerForward.sqrMagnitude > 1e-6f ? ownerForward.normalized : Vector3.forward;
    }

    private void InvalidateSquadMotionStates()
    {
        _motionStateCache = Array.Empty<SquadMotionState>();
    }

    private void InvalidateMovementAnimationStates()
    {
        _movementAnimationStateCache = Array.Empty<SquadMovementAnimationState>();
        _hasLoggedMovementAnimationConfigurationWarning = false;
    }

    private void EnsureSquadMotionStates()
    {
        int requiredCount = _squads != null ? _squads.Length : 0;
        if (_motionStateCache == null || _motionStateCache.Length != requiredCount)
            _motionStateCache = new SquadMotionState[requiredCount];
    }

    private void EnsureMovementAnimationStates()
    {
        int requiredCount = _squads != null ? _squads.Length : 0;
        if (_movementAnimationStateCache == null || _movementAnimationStateCache.Length != requiredCount)
            _movementAnimationStateCache = new SquadMovementAnimationState[requiredCount];
    }

    private bool TryGetSquadMotionState(int squadIndex, out SquadMotionState motionState)
    {
        motionState = default;
        if (_motionStateCache == null ||
            squadIndex < 0 ||
            squadIndex >= _motionStateCache.Length)
        {
            return false;
        }

        motionState = _motionStateCache[squadIndex];
        return motionState.initialized;
    }

    private bool RefreshSquadMotionStates(float deltaTime)
    {
        EnsureSquadMotionStates();
        if (_squads == null || _motionStateCache == null || _motionStateCache.Length == 0)
            return false;

        float safeDeltaTime = Mathf.Max(deltaTime, 1e-4f);
        float maxTurnDegreesDelta = Mathf.Max(0.0f, _formationTurnSpeedDegreesPerSecond) * safeDeltaTime;
        bool changed = false;
        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            Vector3 centerPosition = squad != null && squad.centerTransform != null
                ? squad.centerTransform.position
                : transform.position;
            Vector3 authoredForward = ResolveAuthoredWorldForward(squad, centerPosition);
            SquadMotionState motionState = _motionStateCache[squadIndex];
            if (!motionState.initialized)
            {
                motionState.lastCenter = centerPosition;
                motionState.worldVelocity = Vector3.zero;
                motionState.worldForward = authoredForward;
                motionState.initialized = true;
                _motionStateCache[squadIndex] = motionState;
                changed = true;
                continue;
            }

            Vector3 positionDelta = centerPosition - motionState.lastCenter;
            positionDelta.y = 0.0f;
            Vector3 worldVelocity = Application.isPlaying
                ? positionDelta / safeDeltaTime
                : Vector3.zero;
            worldVelocity.y = 0.0f;

            Vector3 worldForward = motionState.worldForward;
            worldForward = NormalizeHorizontalDirection(worldForward, authoredForward);
            if (Application.isPlaying &&
                ShouldAdvanceCenterFromCommand(squad != null ? squad.commandType : CrowdVatSquadCommandType.None))
            {
                Vector3 desiredForward = worldVelocity.sqrMagnitude > 1e-6f
                    ? worldVelocity
                    : worldForward;
                worldForward = RotateHorizontalDirectionTowards(worldForward, desiredForward, maxTurnDegreesDelta);
            }
            else
            {
                worldForward = authoredForward;
            }

            if ((centerPosition - motionState.lastCenter).sqrMagnitude > 1e-8f ||
                (worldVelocity - motionState.worldVelocity).sqrMagnitude > 1e-8f ||
                (worldForward - motionState.worldForward).sqrMagnitude > 1e-8f)
            {
                changed = true;
            }

            motionState.lastCenter = centerPosition;
            motionState.worldVelocity = worldVelocity;
            motionState.worldForward = worldForward;
            motionState.initialized = true;
            _motionStateCache[squadIndex] = motionState;
        }

        return changed;
    }

    private bool RefreshMovementAnimationStates()
    {
        EnsureMovementAnimationStates();
        if (!Application.isPlaying || !_enableMovementAnimationBlending || _renderer == null || _squads == null)
            return ClearMovementAnimationStates();

        if (!TryResolveMovementAnimationClipNames(out string idleClipName, out string walkClipName))
        {
            ClearMovementAnimationStates();
            return false;
        }

        float enterSpeed = Mathf.Max(0.0f, _moveAnimationEnterSpeed);
        float exitSpeed = Mathf.Clamp(_moveAnimationExitSpeed, 0.0f, enterSpeed);
        float transitionDuration = Mathf.Max(0.0f, _movementAnimationTransitionDuration);
        bool changed = false;

        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            if (!TryGetActiveMemberRange(squad, _renderer.InstanceCount, out int memberStartIndex, out int memberCount))
            {
                if (_movementAnimationStateCache[squadIndex] != SquadMovementAnimationState.Unknown)
                {
                    _movementAnimationStateCache[squadIndex] = SquadMovementAnimationState.Unknown;
                    changed = true;
                }

                continue;
            }

            float movementSpeed = 0.0f;
            if (TryGetSquadMotionState(squadIndex, out SquadMotionState motionState))
            {
                Vector3 worldVelocity = motionState.worldVelocity;
                worldVelocity.y = 0.0f;
                movementSpeed = worldVelocity.magnitude;
            }

            SquadMovementAnimationState previousState = _movementAnimationStateCache[squadIndex];
            bool wantsWalk = previousState == SquadMovementAnimationState.Walk
                ? movementSpeed > exitSpeed
                : movementSpeed >= enterSpeed;
            SquadMovementAnimationState targetState = wantsWalk
                ? SquadMovementAnimationState.Walk
                : SquadMovementAnimationState.Idle;
            if (previousState == targetState)
                continue;

            PopulateMovementAnimationInstanceIndices(memberStartIndex, memberCount);
            string targetClipName = targetState == SquadMovementAnimationState.Walk
                ? walkClipName
                : idleClipName;
            if (!_renderer.TrySetInstancesAnimation(
                    _movementAnimationInstanceIndexScratch,
                    targetClipName,
                    transitionDuration,
                    0.0f))
            {
                continue;
            }

            _movementAnimationStateCache[squadIndex] = targetState;
            changed = true;
        }

        return changed;
    }

    private bool ClearMovementAnimationStates()
    {
        if (_movementAnimationStateCache == null || _movementAnimationStateCache.Length == 0)
            return false;

        bool changed = false;
        for (int squadIndex = 0; squadIndex < _movementAnimationStateCache.Length; squadIndex++)
        {
            if (_movementAnimationStateCache[squadIndex] == SquadMovementAnimationState.Unknown)
                continue;

            _movementAnimationStateCache[squadIndex] = SquadMovementAnimationState.Unknown;
            changed = true;
        }

        _hasLoggedMovementAnimationConfigurationWarning = false;
        return changed;
    }

    private bool TryResolveMovementAnimationClipNames(out string idleClipName, out string walkClipName)
    {
        idleClipName = _idleClipName;
        walkClipName = _walkClipName;

        CrowdVatAnimationAsset animationAsset = _renderer != null ? _renderer.AnimationAsset : null;
        if (animationAsset != null)
        {
            if (string.IsNullOrWhiteSpace(idleClipName))
                idleClipName = FindMovementClipName(animationAsset, "idle");

            if (string.IsNullOrWhiteSpace(walkClipName))
                walkClipName = FindMovementClipName(animationAsset, "walk", "run");

            if (string.IsNullOrWhiteSpace(idleClipName) &&
                animationAsset.Clips != null &&
                animationAsset.Clips.Length > 0)
            {
                idleClipName = animationAsset.Clips[0].Name;
            }

            if (string.IsNullOrWhiteSpace(walkClipName))
                walkClipName = FindAlternateMovementClipName(animationAsset, idleClipName);
        }

        bool idleValid = TryValidateMovementAnimationClipName(idleClipName);
        bool walkValid = TryValidateMovementAnimationClipName(walkClipName);
        if (idleValid && walkValid)
        {
            _hasLoggedMovementAnimationConfigurationWarning = false;
            return true;
        }

        if (!_hasLoggedMovementAnimationConfigurationWarning)
        {
            Debug.LogWarning(
                $"CrowdVatSquadController 自动动画未生效，找不到可用的 idle / walk clip。" +
                $" Idle=`{idleClipName}` Walk=`{walkClipName}`。",
                this);
            _hasLoggedMovementAnimationConfigurationWarning = true;
        }

        return false;
    }

    private bool TryValidateMovementAnimationClipName(string clipName)
    {
        return _renderer != null &&
            !string.IsNullOrWhiteSpace(clipName) &&
            _renderer.TryGetClipIndex(clipName, out _);
    }

    private static string FindMovementClipName(CrowdVatAnimationAsset animationAsset, params string[] keywords)
    {
        if (animationAsset == null || animationAsset.Clips == null || keywords == null || keywords.Length == 0)
            return string.Empty;

        for (int clipIndex = 0; clipIndex < animationAsset.Clips.Length; clipIndex++)
        {
            string clipName = animationAsset.Clips[clipIndex].Name;
            if (string.IsNullOrWhiteSpace(clipName))
                continue;

            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                string keyword = keywords[keywordIndex];
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    clipName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return clipName;
                }
            }
        }

        return string.Empty;
    }

    private static string FindAlternateMovementClipName(CrowdVatAnimationAsset animationAsset, string excludedClipName)
    {
        if (animationAsset == null || animationAsset.Clips == null)
            return string.Empty;

        for (int clipIndex = 0; clipIndex < animationAsset.Clips.Length; clipIndex++)
        {
            string clipName = animationAsset.Clips[clipIndex].Name;
            if (!string.IsNullOrWhiteSpace(clipName) &&
                !string.Equals(clipName, excludedClipName, StringComparison.Ordinal))
            {
                return clipName;
            }
        }

        return string.Empty;
    }

    private void PopulateMovementAnimationInstanceIndices(int memberStartIndex, int memberCount)
    {
        _movementAnimationInstanceIndexScratch.Clear();
        if (memberCount <= 0)
            return;

        if (_movementAnimationInstanceIndexScratch.Capacity < memberCount)
            _movementAnimationInstanceIndexScratch.Capacity = memberCount;

        int memberEndExclusive = memberStartIndex + memberCount;
        for (int instanceIndex = memberStartIndex; instanceIndex < memberEndExclusive; instanceIndex++)
            _movementAnimationInstanceIndexScratch.Add(instanceIndex);
    }

    private static Vector3 NormalizeHorizontalDirection(Vector3 direction, Vector3 fallbackDirection)
    {
        direction.y = 0.0f;
        if (direction.sqrMagnitude > 1e-6f)
            return direction.normalized;

        fallbackDirection.y = 0.0f;
        if (fallbackDirection.sqrMagnitude > 1e-6f)
            return fallbackDirection.normalized;

        return Vector3.forward;
    }

    private static Vector3 RotateHorizontalDirectionTowards(Vector3 currentDirection, Vector3 desiredDirection, float maxDegreesDelta)
    {
        Vector3 normalizedCurrent = NormalizeHorizontalDirection(currentDirection, Vector3.forward);
        Vector3 normalizedDesired = NormalizeHorizontalDirection(desiredDirection, normalizedCurrent);
        if (maxDegreesDelta <= 0.0f)
            return normalizedDesired;

        Vector3 rotatedDirection = Vector3.RotateTowards(
            normalizedCurrent,
            normalizedDesired,
            maxDegreesDelta * Mathf.Deg2Rad,
            0.0f);
        return NormalizeHorizontalDirection(rotatedDirection, normalizedDesired);
    }

    private Vector3 ResolveWorldTargetPosition(SquadAuthoring squad, Vector3 centerPosition, Vector3 forward)
    {
        if (squad.targetTransform != null)
            return squad.targetTransform.position;

        return centerPosition + forward * Mathf.Max(0.0f, _defaultTargetDistance);
    }

}
