using System;
using UnityEngine;

public sealed partial class CrowdVatSquadController : MonoBehaviour
{
    private bool ResolveRendererReference()
    {
        if (_renderer != null)
        {
            EnsureFrameBridge();
            return true;
        }

        if (!_autoResolveRenderer)
        {
            EnsureFrameBridge();
            return false;
        }

        _renderer = GetComponent<CrowdVatIndirectRenderer>();
        if (_renderer == null)
            _renderer = GetComponentInParent<CrowdVatIndirectRenderer>();
        EnsureFrameBridge();
        return _renderer != null;
    }

    private void EnsureSquadArray()
    {
        if (_squads != null && _squads.Length > 0)
            return;

        _squads = new[] { new SquadAuthoring() };
    }

    private void ClampSerializedValues()
    {
        _defaultTargetDistance = Mathf.Max(0.0f, _defaultTargetDistance);
        _arrivalDistance = Mathf.Max(0.0f, _arrivalDistance);
        _maxPreviewSlots = Mathf.Max(1, _maxPreviewSlots);
        _selectionHeight = Mathf.Max(0.1f, _selectionHeight);
        _selectionPadding = Mathf.Max(0.0f, _selectionPadding);
        _defaultSquadsPerFaction = Mathf.Max(1, _defaultSquadsPerFaction);
        _defaultAnchorSpacing = Mathf.Max(0.5f, _defaultAnchorSpacing);
        _moveAnimationEnterSpeed = Mathf.Max(0.0f, _moveAnimationEnterSpeed);
        _moveAnimationExitSpeed = Mathf.Clamp(_moveAnimationExitSpeed, 0.0f, _moveAnimationEnterSpeed);
        _movementAnimationTransitionDuration = Mathf.Max(0.0f, _movementAnimationTransitionDuration);

        if (_squads == null)
            return;

        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            if (squad == null)
            {
                _squads[squadIndex] = new SquadAuthoring();
                continue;
            }

            squad.memberStartIndex = Mathf.Max(0, squad.memberStartIndex);
            squad.memberCount = Mathf.Max(1, squad.memberCount);
            if (!Enum.IsDefined(typeof(CrowdVatSquadFormationType), squad.formationType))
                squad.formationType = CrowdVatSquadFormationType.None;
            squad.formationSpacing = ClampFormationSpacing(squad.formationSpacing);
            if (squad.customFormationSlots == null)
                squad.customFormationSlots = Array.Empty<Vector2>();
            squad.customFormationStrength = Mathf.Clamp01(squad.customFormationStrength);
            squad.moveSpeed = Mathf.Max(0.0f, squad.moveSpeed);
            squad.anchorBlend = 0.0f;
            squad.cohesionRadius = 0.0f;
            squad.cohesionStrength = 0.0f;
            squad.memberFlags &= ~CrowdVatSquadMemberFlags.PreferAssignedSlot;
            squad.memberFlags &= ~CrowdVatSquadMemberFlags.UseSlotOffsetOverride;
            squad.memberWeight = Mathf.Max(0.0f, squad.memberWeight);
        }
    }

    private void EnsureSelectableStateCache()
    {
        int squadCount = _squads != null ? _squads.Length : 0;
        if (_sourceSquadSelectableFrameCache != null &&
            _sourceSquadSelectableValueCache != null &&
            _sourceSquadSelectableFrameCache.Length == squadCount &&
            _sourceSquadSelectableValueCache.Length == squadCount)
        {
            return;
        }

        _sourceSquadSelectableFrameCache = new int[squadCount];
        _sourceSquadSelectableValueCache = new bool[squadCount];
        Array.Fill(_sourceSquadSelectableFrameCache, -1);
    }

    private void InvalidateSelectableStateCache()
    {
        EnsureSelectableStateCache();
        Array.Fill(_sourceSquadSelectableFrameCache, -1);
    }

    private bool CacheSelectableState(int squadIndex, bool selectable)
    {
        if (Application.isPlaying &&
            squadIndex >= 0 &&
            squadIndex < _sourceSquadSelectableFrameCache.Length)
        {
            _sourceSquadSelectableFrameCache[squadIndex] = Time.frameCount;
            _sourceSquadSelectableValueCache[squadIndex] = selectable;
        }

        return selectable;
    }

    private void InvalidateSelectionSphereCache()
    {
        if (_selectionRadiusCache == null || _selectionRadiusCache.Length != (_squads != null ? _squads.Length : 0))
        {
            _selectionRadiusCache = new float[_squads != null ? _squads.Length : 0];
        }

        if (_selectionRadiusMemberCountCache == null || _selectionRadiusMemberCountCache.Length != (_squads != null ? _squads.Length : 0))
            _selectionRadiusMemberCountCache = new int[_squads != null ? _squads.Length : 0];

        for (int squadIndex = 0; squadIndex < _selectionRadiusCache.Length; squadIndex++)
        {
            _selectionRadiusCache[squadIndex] = -1.0f;
            _selectionRadiusMemberCountCache[squadIndex] = -1;
        }
    }

    private float GetSelectionSphereRadius(int squadIndex, SquadAuthoring squad, int memberCount)
    {
        if (_selectionRadiusCache == null || _selectionRadiusCache.Length != (_squads != null ? _squads.Length : 0))
            InvalidateSelectionSphereCache();

        if (squadIndex >= 0 &&
            squadIndex < _selectionRadiusCache.Length &&
            _selectionRadiusCache[squadIndex] > 0.0f &&
            _selectionRadiusMemberCountCache[squadIndex] == memberCount)
        {
            return _selectionRadiusCache[squadIndex];
        }

        float computedRadius = ComputeSelectionSphereRadius(squad, memberCount);
        if (squadIndex >= 0 && squadIndex < _selectionRadiusCache.Length)
        {
            _selectionRadiusCache[squadIndex] = computedRadius;
            _selectionRadiusMemberCountCache[squadIndex] = memberCount;
        }

        return computedRadius;
    }

    private float ComputeSelectionSphereRadius(SquadAuthoring squad, int memberCount)
    {
        float crowdFootprintRadius = EstimateMinimumFreeSquadRadius(memberCount);
        float formationRadius = ComputeFormationRadius(squad, memberCount);
        return Mathf.Max(0.6f, Mathf.Max(crowdFootprintRadius, formationRadius) + _selectionPadding);
    }

    private static bool HasCustomFormationSlots(SquadAuthoring squad)
    {
        return squad != null &&
            squad.useCustomFormationSlots &&
            squad.customFormationStrength > 0.0f &&
            squad.customFormationSlots != null &&
            squad.customFormationSlots.Length > 0;
    }

    private static bool HasGeneratedFormationSlots(SquadAuthoring squad)
    {
        return squad != null &&
            squad.formationType != CrowdVatSquadFormationType.None &&
            squad.customFormationStrength > 0.0f;
    }

    private static bool HasRuntimeFormationSlots(SquadAuthoring squad)
    {
        return HasCustomFormationSlots(squad) || HasGeneratedFormationSlots(squad);
    }

    private static float ResolveFormationStrength(SquadAuthoring squad)
    {
        return HasRuntimeFormationSlots(squad) ? Mathf.Clamp01(squad.customFormationStrength) : 0.0f;
    }

    private static Vector2 ClampFormationSpacing(Vector2 spacing)
    {
        return new Vector2(
            Mathf.Max(MinimumFormationSpacing, spacing.x),
            Mathf.Max(MinimumFormationSpacing, spacing.y));
    }

    private Vector2 ResolveFormationSpacing(SquadAuthoring squad)
    {
        Vector2 authoredSpacing = squad != null
            ? ClampFormationSpacing(squad.formationSpacing)
            : new Vector2(DefaultFormationSpacing, DefaultFormationSpacing);

        if (!HasGeneratedFormationSlots(squad))
            return authoredSpacing;

        float collisionSafeSpacing = ResolveMinimumCollisionFormationSpacing();
        return new Vector2(
            Mathf.Max(authoredSpacing.x, collisionSafeSpacing),
            Mathf.Max(authoredSpacing.y, collisionSafeSpacing));
    }

    private static Vector2 ResolveCustomFormationSlotOffset(SquadAuthoring squad, int localMemberIndex)
    {
        if (squad == null ||
            squad.customFormationSlots == null ||
            localMemberIndex < 0 ||
            localMemberIndex >= squad.customFormationSlots.Length)
        {
            return Vector2.zero;
        }

        return squad.customFormationSlots[localMemberIndex];
    }

    private float ResolveMinimumCollisionFormationSpacing()
    {
        float agentRadius = _renderer != null ? _renderer.MaximumAgentCollisionRadius : DefaultFormationSpacing * 0.5f;
        return Mathf.Max(MinimumFormationSpacing, agentRadius * 2.0f + FormationCollisionSpacingPadding);
    }

    private Vector2 ResolveFormationSlotOffset(SquadAuthoring squad, int localMemberIndex, int memberCount)
    {
        if (HasCustomFormationSlots(squad))
            return ResolveCustomFormationSlotOffset(squad, localMemberIndex);

        if (!HasGeneratedFormationSlots(squad))
            return Vector2.zero;

        Vector2 spacing = ResolveFormationSpacing(squad);
        Vector2 centerOffset = ComputeGeneratedFormationCenter(squad.formationType, memberCount, spacing);
        return ComputeGeneratedFormationSlotRawOffset(squad.formationType, localMemberIndex, memberCount, spacing) - centerOffset;
    }

    private float ComputeFormationRadius(SquadAuthoring squad, int memberCount)
    {
        if (!HasRuntimeFormationSlots(squad))
            return 0.0f;

        float maxRadiusSqr = 0.0f;
        int slotCount = Mathf.Max(0, memberCount);
        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            maxRadiusSqr = Mathf.Max(maxRadiusSqr, ResolveFormationSlotOffset(squad, slotIndex, slotCount).sqrMagnitude);

        return Mathf.Sqrt(maxRadiusSqr);
    }

    private static Vector2 ComputeGeneratedFormationCenter(CrowdVatSquadFormationType formationType, int memberCount, Vector2 spacing)
    {
        int safeMemberCount = Mathf.Max(1, memberCount);
        Vector2 sum = Vector2.zero;
        for (int slotIndex = 0; slotIndex < safeMemberCount; slotIndex++)
            sum += ComputeGeneratedFormationSlotRawOffset(formationType, slotIndex, safeMemberCount, spacing);

        return sum / safeMemberCount;
    }

    private static Vector2 ComputeGeneratedFormationSlotRawOffset(
        CrowdVatSquadFormationType formationType,
        int localMemberIndex,
        int memberCount,
        Vector2 spacing)
    {
        int safeMemberCount = Mathf.Max(1, memberCount);
        int safeIndex = Mathf.Clamp(localMemberIndex, 0, safeMemberCount - 1);
        Vector2 safeSpacing = ClampFormationSpacing(spacing);

        switch (formationType)
        {
            case CrowdVatSquadFormationType.Loose:
                return ComputeGridFormationOffset(safeIndex, safeMemberCount, safeSpacing, true);

            case CrowdVatSquadFormationType.Line:
                return new Vector2(
                    (safeIndex - (safeMemberCount - 1) * 0.5f) * safeSpacing.x,
                    0.0f);

            case CrowdVatSquadFormationType.Column:
                return new Vector2(
                    0.0f,
                    ((safeMemberCount - 1) * 0.5f - safeIndex) * safeSpacing.y);

            case CrowdVatSquadFormationType.Wedge:
                return ComputeWedgeFormationOffset(safeIndex, safeSpacing);

            case CrowdVatSquadFormationType.Ring:
                return ComputeRingFormationOffset(safeIndex, safeMemberCount, safeSpacing);

            case CrowdVatSquadFormationType.Block:
                return ComputeGridFormationOffset(safeIndex, safeMemberCount, safeSpacing, false);

            case CrowdVatSquadFormationType.None:
            default:
                return Vector2.zero;
        }
    }

    private static Vector2 ComputeGridFormationOffset(int localMemberIndex, int memberCount, Vector2 spacing, bool loose)
    {
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(memberCount)));
        int rows = Mathf.Max(1, Mathf.CeilToInt(memberCount / (float)columns));
        int row = localMemberIndex / columns;
        int column = localMemberIndex % columns;
        int rowStartIndex = row * columns;
        int rowMemberCount = Mathf.Clamp(memberCount - rowStartIndex, 1, columns);
        float rowStagger = loose && (row & 1) != 0 ? spacing.x * 0.5f : 0.0f;
        float rowSpacing = loose ? spacing.y * 1.25f : spacing.y;

        return new Vector2(
            (column - (rowMemberCount - 1) * 0.5f) * spacing.x + rowStagger,
            ((rows - 1) * 0.5f - row) * rowSpacing);
    }

    private static Vector2 ComputeWedgeFormationOffset(int localMemberIndex, Vector2 spacing)
    {
        if (localMemberIndex <= 0)
            return Vector2.zero;

        int rank = (localMemberIndex + 1) / 2;
        float side = (localMemberIndex & 1) == 0 ? 1.0f : -1.0f;
        return new Vector2(side * rank * spacing.x, -rank * spacing.y);
    }

    private static Vector2 ComputeRingFormationOffset(int localMemberIndex, int memberCount, Vector2 spacing)
    {
        if (memberCount <= 1)
            return Vector2.zero;

        float ringStep = Mathf.Max(spacing.x, spacing.y);
        int remainingIndex = localMemberIndex;
        for (int ringIndex = 1; ringIndex < memberCount + 1; ringIndex++)
        {
            float radius = ringIndex * ringStep;
            int ringCapacity = Mathf.Max(6, Mathf.RoundToInt((Mathf.PI * 2.0f * radius) / Mathf.Max(MinimumFormationSpacing, spacing.x)));
            if (remainingIndex < ringCapacity)
            {
                float angle = (Mathf.PI * 0.5f) - (remainingIndex * Mathf.PI * 2.0f / ringCapacity);
                return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }

            remainingIndex -= ringCapacity;
        }

        return Vector2.zero;
    }

    private static Vector3 ResolveCustomFormationSlotWorldPosition(Vector3 centerPosition, Vector3 forward, Vector2 localOffset)
    {
        forward.y = 0.0f;
        if (forward.sqrMagnitude <= 1e-6f)
            forward = Vector3.forward;
        else
            forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude <= 1e-6f)
            right = Vector3.right;
        else
            right.Normalize();

        return centerPosition + right * localOffset.x + forward * localOffset.y;
    }

    private bool TryBuildSquadSelectionState(int squadIndex, SquadAuthoring squad, int memberCount, out Vector3 squadCenter, out float selectionRadius)
    {
        squadCenter = Vector3.zero;
        selectionRadius = 0.0f;
        if (squad == null)
            return false;

        squadCenter = squad.centerTransform != null ? squad.centerTransform.position : transform.position;
        selectionRadius = GetSelectionSphereRadius(squadIndex, squad, memberCount);
        return selectionRadius > 0.0f;
    }

    private bool TryGetRuntimeSquadAliveCountForSourceSquad(int squadIndex, out int aliveCount)
    {
        aliveCount = 0;
        if (_renderer == null || !_renderer.GpuInstanceCombatEnabled)
            return false;

        if (_sourceSquadToRuntimeIndexCache == null || squadIndex < 0 || squadIndex >= _sourceSquadToRuntimeIndexCache.Length)
            return false;

        int runtimeSquadIndex = _sourceSquadToRuntimeIndexCache[squadIndex];
        if (runtimeSquadIndex < 0)
            return false;

        if (!TryRefreshRuntimeSquadAliveObservations())
            return false;

        return _world.TryGetRuntimeSquadAliveCount(runtimeSquadIndex, out aliveCount);
    }

    private void EnsureFrameBridge()
    {
        if (_frameBridge == null)
            _frameBridge = new CrowdVatRendererFrameBridge(_renderer);
        else
            _frameBridge.SetRenderer(_renderer);
    }

    private bool TryRefreshRuntimeSquadAliveObservations()
    {
        EnsureFrameBridge();
        if (_frameBridge == null)
            return false;

        int currentFrame = Application.isPlaying ? Time.frameCount : 0;
        if (_hasAliveObservationSnapshot && _lastAliveObservationCaptureFrame == currentFrame)
            return true;

        if (!_frameBridge.ObservationHub.TryCaptureFrameObservation(_frameObservation, CrowdVatObservationCaptureFlags.SquadAliveCounts))
        {
            _hasAliveObservationSnapshot = false;
            return false;
        }

        _world.ApplyFrameObservation(_frameObservation);
        _lastAliveObservationCaptureFrame = currentFrame;
        _hasAliveObservationSnapshot = true;
        return true;
    }

    private void BuildWorldSquadTruthCache()
    {
        int runtimeSquadCount = _activeSquadSourceIndices.Count;
        if (_worldSquadTruthCache.Length != runtimeSquadCount)
            _worldSquadTruthCache = new CrowdVatWorldSquadTruth[runtimeSquadCount];

        for (int runtimeSquadIndex = 0; runtimeSquadIndex < runtimeSquadCount; runtimeSquadIndex++)
        {
            int sourceSquadIndex = _activeSquadSourceIndices[runtimeSquadIndex];
            SquadAuthoring squad = _squads[sourceSquadIndex];
            CrowdVatSquadState squadState = runtimeSquadIndex < _squadStateCache.Length
                ? _squadStateCache[runtimeSquadIndex]
                : default;

            _worldSquadTruthCache[runtimeSquadIndex] = new CrowdVatWorldSquadTruth
            {
                sourceSquadIndex = sourceSquadIndex,
                enabled = squad != null && squad.enabled,
                memberStartIndex = squad?.memberStartIndex ?? 0,
                memberCount = squad?.memberCount ?? 0,
                formationType = squad?.formationType ?? CrowdVatSquadFormationType.None,
                commandType = squad?.commandType ?? CrowdVatSquadCommandType.None,
                factionMask = squad?.factionMask ?? CrowdVatFactionMask.None,
                flags = squad?.flags ?? CrowdVatSquadFlags.None,
                memberRoleMask = squad?.memberRoleMask ?? CrowdVatSquadRoleMask.None,
                memberFlags = squad != null
                    ? squad.memberFlags & ~CrowdVatSquadMemberFlags.PreferAssignedSlot & ~CrowdVatSquadMemberFlags.UseSlotOffsetOverride
                    : CrowdVatSquadMemberFlags.None,
                formationSpacing = squad != null ? ResolveFormationSpacing(squad) : Vector2.one,
                moveSpeed = squad?.moveSpeed ?? 0.0f,
                anchorBlend = ResolveFormationStrength(squad),
                cohesionRadius = 0.0f,
                cohesionStrength = 0.0f,
                worldCenter = squadState.worldCenter,
                worldForward = squadState.worldForward,
                worldTarget = squadState.worldTarget
            };
        }
    }

    private void InvalidateAliveObservationCache()
    {
        _lastAliveObservationCaptureFrame = int.MinValue;
        _hasAliveObservationSnapshot = false;
    }

    private Vector3 BuildSelectionSphereCenter(Vector3 squadCenter)
    {
        return squadCenter + Vector3.up * Mathf.Max(0.05f, _selectionHeight * 0.5f);
    }

    private static bool ConsumeTransformChanged(Transform trackedTransform)
    {
        if (trackedTransform == null || !trackedTransform.hasChanged)
            return false;

        trackedTransform.hasChanged = false;
        return true;
    }

    private static bool ShouldAdvanceCenterFromCommand(CrowdVatSquadCommandType commandType)
    {
        switch (commandType)
        {
            case CrowdVatSquadCommandType.MoveTo:
            case CrowdVatSquadCommandType.Advance:
            case CrowdVatSquadCommandType.Charge:
            case CrowdVatSquadCommandType.Retreat:
            case CrowdVatSquadCommandType.Regroup:
                return true;

            case CrowdVatSquadCommandType.None:
            case CrowdVatSquadCommandType.Hold:
            default:
                return false;
        }
    }

    private static Transform CreateAnchorTransform(Transform parent, string objectName, Vector3 worldPosition)
    {
        GameObject anchorObject = new GameObject(objectName);
        Transform anchorTransform = anchorObject.transform;
        anchorTransform.SetParent(parent, true);
        anchorTransform.position = worldPosition;
        anchorTransform.rotation = Quaternion.identity;
        anchorTransform.localScale = Vector3.one;
        anchorTransform.hasChanged = true;
        return anchorTransform;
    }

    private bool TryGetSquadForRuntimeAccess(int squadIndex, out SquadAuthoring squad, out int memberStartIndex, out int memberCount)
    {
        squad = null;
        memberStartIndex = 0;
        memberCount = 0;
        ResolveRendererReference();
        EnsureSquadArray();

        if (_squads == null || squadIndex < 0 || squadIndex >= _squads.Length)
            return false;

        squad = _squads[squadIndex];
        if (squad == null)
            return false;

        int instanceCount = _renderer != null ? _renderer.InstanceCount : int.MaxValue;
        return TryGetActiveMemberRange(squad, instanceCount, out memberStartIndex, out memberCount) &&
            IsSourceSquadSelectable(squadIndex);
    }

    private int FillDefaultFactionSquads(
        int arrayStartIndex,
        CrowdVatFactionMask factionMask,
        string namePrefix,
        int memberStartIndex,
        int memberCount,
        int squadCount)
    {
        int safeSquadCount = Mathf.Max(1, squadCount);
        int activeSquadCount = memberCount > 0 ? Mathf.Min(safeSquadCount, memberCount) : 0;
        int baseMemberCount = activeSquadCount > 0 ? memberCount / activeSquadCount : 0;
        int remainder = activeSquadCount > 0 ? memberCount % activeSquadCount : 0;
        int nextMemberStart = memberStartIndex;

        for (int localSquadIndex = 0; localSquadIndex < safeSquadCount; localSquadIndex++)
        {
            bool isActive = localSquadIndex < activeSquadCount;
            int localMemberCount = isActive ? baseMemberCount + (localSquadIndex < remainder ? 1 : 0) : 0;
            SquadAuthoring squad = new SquadAuthoring
            {
                name = $"{namePrefix}_Squad_{localSquadIndex:00}",
                enabled = isActive,
                memberStartIndex = nextMemberStart,
                memberCount = Mathf.Max(1, localMemberCount),
                formationType = CrowdVatSquadFormationType.Block,
                commandType = CrowdVatSquadCommandType.Hold,
                factionMask = factionMask,
                flags = CrowdVatSquadFlags.FaceTarget,
                formationSpacing = new Vector2(DefaultFormationSpacing, DefaultFormationSpacing),
                useCustomFormationSlots = false,
                customFormationSlots = Array.Empty<Vector2>(),
                customFormationStrength = DefaultFormationStrength,
                moveSpeed = 2.0f,
                anchorBlend = 0.0f,
                cohesionRadius = 0.0f,
                cohesionStrength = 0.0f,
                memberRoleMask = CrowdVatSquadRoleMask.All,
                memberFlags = CrowdVatSquadMemberFlags.None,
                memberWeight = 1.0f
            };

            _squads[arrayStartIndex + localSquadIndex] = squad;
            nextMemberStart += localMemberCount;
        }

        return arrayStartIndex + safeSquadCount;
    }

    private void CreateOrRefreshDefaultSquadAnchors(bool forceRecreate)
    {
        ResolveRendererReference();
        EnsureSquadArray();

        Transform ownerRoot = _renderer != null ? _renderer.transform : transform;
        Vector3 ownerForward = ownerRoot.forward.sqrMagnitude > 1e-6f ? ownerRoot.forward.normalized : Vector3.forward;
        Vector3 ownerRight = ownerRoot.right.sqrMagnitude > 1e-6f ? ownerRoot.right.normalized : Vector3.right;

        int campAOrdinal = 0;
        int campBOrdinal = 0;
        int sharedOrdinal = 0;

        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            if (squad == null)
                continue;

            Transform baseTransform;
            int ordinal;
            if (squad.factionMask == CrowdVatFactionMask.CampA)
            {
                baseTransform = _renderer != null && _renderer.CampAControlTransform != null ? _renderer.CampAControlTransform : ownerRoot;
                ordinal = campAOrdinal++;
            }
            else if (squad.factionMask == CrowdVatFactionMask.CampB)
            {
                baseTransform = _renderer != null && _renderer.CampBControlTransform != null ? _renderer.CampBControlTransform : ownerRoot;
                ordinal = campBOrdinal++;
            }
            else
            {
                baseTransform = ownerRoot;
                ordinal = sharedOrdinal++;
            }

            Vector3 basePosition = baseTransform != null ? baseTransform.position : ownerRoot.position;
            Vector2 gridOffset = ResolveDefaultAnchorGridOffset(ordinal);
            Vector3 centerPosition = basePosition
                + ownerRight * (gridOffset.x * _defaultAnchorSpacing)
                + ownerForward * (gridOffset.y * _defaultAnchorSpacing);
            Vector3 forwardPosition = centerPosition + ownerForward * 3.0f;
            Vector3 targetPosition = centerPosition + ownerForward * _defaultTargetDistance;
            string squadLabel = string.IsNullOrWhiteSpace(squad.name) ? $"Squad_{squadIndex:00}" : squad.name;

            if (forceRecreate)
            {
                if (squad.centerTransform != null)
                    squad.centerTransform.SetPositionAndRotation(centerPosition, Quaternion.LookRotation(ownerForward, Vector3.up));
                else
                    squad.centerTransform = CreateAnchorTransform(ownerRoot, $"{squadLabel}_Center", centerPosition);

                if (squad.forwardTransform != null)
                    squad.forwardTransform.position = forwardPosition;
                else
                    squad.forwardTransform = CreateAnchorTransform(ownerRoot, $"{squadLabel}_Forward", forwardPosition);

                if (squad.targetTransform != null)
                    squad.targetTransform.position = targetPosition;
                else
                    squad.targetTransform = CreateAnchorTransform(ownerRoot, $"{squadLabel}_Target", targetPosition);

                squad.centerTransform.hasChanged = true;
                squad.forwardTransform.hasChanged = true;
                squad.targetTransform.hasChanged = true;
                continue;
            }

            if (squad.centerTransform == null)
                squad.centerTransform = CreateAnchorTransform(ownerRoot, $"{squadLabel}_Center", centerPosition);

            if (squad.forwardTransform == null)
                squad.forwardTransform = CreateAnchorTransform(ownerRoot, $"{squadLabel}_Forward", forwardPosition);

            if (squad.targetTransform == null)
                squad.targetTransform = CreateAnchorTransform(ownerRoot, $"{squadLabel}_Target", targetPosition);
        }
    }

    private static Vector2 ResolveDefaultAnchorGridOffset(int ordinal)
    {
        if (ordinal <= 0)
            return Vector2.zero;

        int columns = 2;
        int row = ordinal / columns;
        int column = ordinal % columns;
        float x = column == 0 ? -0.5f : 0.5f;
        float z = -row;
        return new Vector2(x, z);
    }

    private float EstimateMinimumFreeSquadRadius(int memberCount)
    {
        int safeMemberCount = Mathf.Max(1, memberCount);
        float agentRadius = _renderer != null ? _renderer.MaximumAgentCollisionRadius : 0.5f;
        agentRadius = Mathf.Max(0.01f, agentRadius);
        return agentRadius * (Mathf.Sqrt(safeMemberCount / SquadPbdPackingDensity) + SquadPbdRadiusPaddingInAgents);
    }

    private static bool TryIntersectRaySphere(Ray ray, Vector3 center, float radius, float maxDistance, out float hitDistance)
    {
        hitDistance = float.PositiveInfinity;
        float safeRadius = Mathf.Max(0.001f, radius);
        Vector3 offset = ray.origin - center;
        float a = Vector3.Dot(ray.direction, ray.direction);
        float b = 2.0f * Vector3.Dot(offset, ray.direction);
        float c = Vector3.Dot(offset, offset) - (safeRadius * safeRadius);
        float discriminant = (b * b) - (4.0f * a * c);

        if (discriminant < 0.0f)
            return false;

        float sqrtDiscriminant = Mathf.Sqrt(discriminant);
        float denominator = 2.0f * a;
        float t0 = (-b - sqrtDiscriminant) / denominator;
        float t1 = (-b + sqrtDiscriminant) / denominator;
        float bestT = t0 >= 0.0f ? t0 : t1;
        if (bestT < 0.0f || bestT > maxDistance)
            return false;

        hitDistance = bestT;
        return true;
    }
}
