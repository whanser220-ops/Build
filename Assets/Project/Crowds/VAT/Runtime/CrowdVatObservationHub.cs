using System;

public sealed class CrowdVatObservationHub
{
    private CrowdVatIndirectRenderer _renderer;

    public CrowdVatObservationHub(CrowdVatIndirectRenderer renderer)
    {
        _renderer = renderer;
    }

    public void SetRenderer(CrowdVatIndirectRenderer renderer)
    {
        _renderer = renderer;
    }

    public bool TryCaptureFrameObservation(CrowdVatFrameObservation frameObservation, CrowdVatObservationCaptureFlags captureFlags)
    {
        if (frameObservation == null)
            throw new ArgumentNullException(nameof(frameObservation));

        frameObservation.Clear();
        if (_renderer == null)
            return false;

        bool capturedAny = false;
        if ((captureFlags & CrowdVatObservationCaptureFlags.SquadAliveCounts) != 0)
            capturedAny |= _renderer.TryReadSquadAliveObservations(frameObservation.squadAliveObservations, out frameObservation.squadAliveFrameInfo);

        if ((captureFlags & CrowdVatObservationCaptureFlags.Combat) != 0)
            capturedAny |= _renderer.ReadCombatObservations(frameObservation.combatObservations, out frameObservation.combatFrameInfo) > 0;

        if ((captureFlags & CrowdVatObservationCaptureFlags.SpatialQueries) != 0)
        {
            _renderer.ReadSpatialQueryObservations(
                frameObservation.spatialQueryResults,
                frameObservation.spatialQueryHits,
                out frameObservation.spatialQueryFrameInfo);
            capturedAny |= frameObservation.spatialQueryFrameInfo.sourceKind != CrowdVatObservationSourceKind.None;
        }

        return capturedAny;
    }
}
