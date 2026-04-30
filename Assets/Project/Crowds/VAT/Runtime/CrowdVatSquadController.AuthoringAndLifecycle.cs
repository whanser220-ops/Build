using System;
using UnityEngine;

public sealed partial class CrowdVatSquadController : MonoBehaviour
{
    private void Reset()
    {
        ResolveRendererReference();
        EnsureSquadArray();
    }

    private void OnEnable()
    {
        ResolveRendererReference();
        EnsureSquadArray();
        InvalidateSquadMotionStates();
        InvalidateMovementAnimationStates();
        InvalidateAliveObservationCache();
        _layoutDirty = true;
        InvalidateSelectableStateCache();
        InvalidateSelectionSphereCache();
        ApplyToRenderer(forceLayoutUpload: true);
    }

    private void OnDisable()
    {
        _framePacket.Reset();
        _framePacket.frameParams = BuildFrameParams();
        _framePacket.flags = CrowdVatFramePacketFlags.ClearRuntimeSquadData;
        EnsureFrameBridge();
        _frameBridge?.ApplyFramePacket(_framePacket);
        _world.Clear();
        InvalidateAliveObservationCache();

        _hasAppliedData = false;
        InvalidateSquadMotionStates();
        InvalidateMovementAnimationStates();
        _lastAppliedRuntimeSquadSourceIndices = Array.Empty<int>();
    }

    private void OnValidate()
    {
        ResolveRendererReference();
        EnsureSquadArray();
        ClampSerializedValues();
        InvalidateSquadMotionStates();
        InvalidateMovementAnimationStates();
        InvalidateAliveObservationCache();
        _layoutDirty = true;
        InvalidateSelectableStateCache();
        InvalidateSelectionSphereCache();
        ApplyToRenderer(forceLayoutUpload: true);
    }

    private void Update()
    {
        ResolveRendererReference();
        if (_renderer == null)
            return;

        bool stateDirty = ConsumeTrackedTransformChanges();

        bool centersMoved = AdvanceCentersTowardsTargets(Time.deltaTime);
        stateDirty |= centersMoved;

        stateDirty |= RefreshSquadMotionStates(Time.deltaTime);
        RefreshMovementAnimationStates();

        if (_layoutDirty || !_hasAppliedData || stateDirty)
            ApplyToRenderer(forceLayoutUpload: _layoutDirty || !_hasAppliedData);
    }

    [ContextMenu("创建默认小队控制点")]
    public void CreateDefaultSquadAnchors()
    {
        CreateOrRefreshDefaultSquadAnchors(forceRecreate: false);
        _layoutDirty = true;
        InvalidateSelectableStateCache();
        InvalidateSelectionSphereCache();
        ApplyToRenderer(forceLayoutUpload: true);
    }

    public bool HasAnchorTransforms()
    {
        EnsureSquadArray();

        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            if (squad != null && squad.enabled && squad.centerTransform != null)
                return true;
        }

        return false;
    }

    public void CreateDefaultTacticalSquads()
    {
        ResolveRendererReference();
        EnsureSquadArray();
        ClampSerializedValues();

        int instanceCount = Mathf.Max(1, _renderer != null ? _renderer.InstanceCount : 1);
        int squadCountPerFaction = Mathf.Max(1, _defaultSquadsPerFaction);

        if (_renderer != null && _renderer.FactionLayoutMode == CrowdVatFactionLayoutMode.TwoOpposingFactions)
        {
            int campAMemberCount = Mathf.CeilToInt(instanceCount * 0.5f);
            int campBMemberCount = Mathf.Max(0, instanceCount - campAMemberCount);
            _squads = new SquadAuthoring[squadCountPerFaction * 2];

            int nextSquadIndex = FillDefaultFactionSquads(
                0,
                CrowdVatFactionMask.CampA,
                "CampA",
                0,
                campAMemberCount,
                squadCountPerFaction);

            FillDefaultFactionSquads(
                nextSquadIndex,
                CrowdVatFactionMask.CampB,
                "CampB",
                campAMemberCount,
                campBMemberCount,
                squadCountPerFaction);
        }
        else
        {
            _squads = new SquadAuthoring[squadCountPerFaction];
            FillDefaultFactionSquads(
                0,
                CrowdVatFactionMask.All,
                "Squad",
                0,
                instanceCount,
                squadCountPerFaction);
        }

        CreateOrRefreshDefaultSquadAnchors(forceRecreate: true);
        InvalidateSquadMotionStates();
        InvalidateMovementAnimationStates();
        InvalidateAliveObservationCache();
        _layoutDirty = true;
        InvalidateSelectableStateCache();
        InvalidateSelectionSphereCache();
        ApplyToRenderer(forceLayoutUpload: true);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_showFormationPreview)
            return;

        ResolveRendererReference();
        EnsureSquadArray();

        int drawnSlotCount = 0;
        for (int squadIndex = 0; squadIndex < _squads.Length; squadIndex++)
        {
            SquadAuthoring squad = _squads[squadIndex];
            if (!TryGetActiveMemberRange(squad, _renderer != null ? _renderer.InstanceCount : int.MaxValue, out _, out int memberCount))
                continue;

            Color color = Color.HSVToRGB(Mathf.Repeat(squadIndex * 0.19f, 1.0f), 0.72f, 1.0f);
            Vector3 centerPosition = squad.centerTransform.position;
            Vector3 forward = ResolveWorldForward(squadIndex, squad, centerPosition);
            Vector3 targetPosition = ResolveWorldTargetPosition(squad, centerPosition, forward);

            Gizmos.color = color;
            Gizmos.DrawWireSphere(centerPosition, 0.3f);
            Gizmos.DrawLine(centerPosition, centerPosition + forward * 2.0f);
            Gizmos.DrawWireSphere(targetPosition, 0.2f);
            Gizmos.DrawLine(centerPosition, targetPosition);
            drawnSlotCount++;

            if (HasRuntimeFormationSlots(squad))
            {
                int previewMemberCount = Mathf.Min(memberCount, Mathf.Max(0, _maxPreviewSlots - drawnSlotCount));
                for (int localMemberIndex = 0; localMemberIndex < previewMemberCount; localMemberIndex++)
                {
                    Vector2 localOffset = ResolveFormationSlotOffset(squad, localMemberIndex, memberCount);
                    Vector3 slotPosition = ResolveCustomFormationSlotWorldPosition(centerPosition, forward, localOffset);
                    Gizmos.DrawWireSphere(slotPosition, 0.12f);
                    drawnSlotCount++;
                    if (drawnSlotCount >= _maxPreviewSlots)
                        break;
                }
            }

            if (drawnSlotCount >= _maxPreviewSlots)
                break;
        }
    }

    public int SquadCount => _squads != null ? _squads.Length : 0;
}
