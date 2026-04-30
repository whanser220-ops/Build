using System;

public sealed class CrowdVatWorld
{
    private CrowdVatWorldSquadTruth[] _squadTruth = Array.Empty<CrowdVatWorldSquadTruth>();
    private CrowdVatWorldSquadObservation[] _runtimeSquadObservations = Array.Empty<CrowdVatWorldSquadObservation>();
    private CrowdVatObservationFrameInfo _runtimeSquadAliveFrameInfo;
    private bool _hasRuntimeSquadAliveFrameInfo;

    public void Clear()
    {
        _squadTruth = Array.Empty<CrowdVatWorldSquadTruth>();
        _runtimeSquadObservations = Array.Empty<CrowdVatWorldSquadObservation>();
        _runtimeSquadAliveFrameInfo = default;
        _hasRuntimeSquadAliveFrameInfo = false;
    }

    public void SetSquadTruth(CrowdVatWorldSquadTruth[] squadTruth)
    {
        if (squadTruth == null || squadTruth.Length == 0)
        {
            _squadTruth = Array.Empty<CrowdVatWorldSquadTruth>();
            return;
        }

        if (_squadTruth.Length != squadTruth.Length)
            _squadTruth = new CrowdVatWorldSquadTruth[squadTruth.Length];

        Array.Copy(squadTruth, _squadTruth, squadTruth.Length);
    }

    public void ApplyFrameObservation(CrowdVatFrameObservation frameObservation)
    {
        if (frameObservation == null)
            throw new ArgumentNullException(nameof(frameObservation));

        _runtimeSquadAliveFrameInfo = frameObservation.squadAliveFrameInfo;
        _hasRuntimeSquadAliveFrameInfo = true;

        int requiredCount = 0;
        for (int observationIndex = 0; observationIndex < frameObservation.squadAliveObservations.Count; observationIndex++)
        {
            int runtimeSquadIndex = frameObservation.squadAliveObservations[observationIndex].runtimeSquadIndex;
            requiredCount = Math.Max(requiredCount, runtimeSquadIndex + 1);
        }

        if (requiredCount <= 0)
            return;

        EnsureRuntimeSquadObservationCapacity(requiredCount);
        for (int runtimeSquadIndex = 0; runtimeSquadIndex < _runtimeSquadObservations.Length; runtimeSquadIndex++)
        {
            CrowdVatWorldSquadObservation observation = _runtimeSquadObservations[runtimeSquadIndex];
            observation.runtimeSquadIndex = runtimeSquadIndex;
            observation.hasObservation = false;
            _runtimeSquadObservations[runtimeSquadIndex] = observation;
        }

        for (int observationIndex = 0; observationIndex < frameObservation.squadAliveObservations.Count; observationIndex++)
        {
            CrowdVatSquadAliveObservation aliveObservation = frameObservation.squadAliveObservations[observationIndex];
            if (aliveObservation.runtimeSquadIndex < 0 || aliveObservation.runtimeSquadIndex >= _runtimeSquadObservations.Length)
                continue;

            _runtimeSquadObservations[aliveObservation.runtimeSquadIndex] = new CrowdVatWorldSquadObservation
            {
                runtimeSquadIndex = aliveObservation.runtimeSquadIndex,
                aliveCount = aliveObservation.aliveCount,
                hasObservation = true
            };
        }
    }

    public bool TryGetRuntimeSquadAliveCount(int runtimeSquadIndex, out int aliveCount)
    {
        aliveCount = 0;
        if (runtimeSquadIndex < 0 || runtimeSquadIndex >= _runtimeSquadObservations.Length)
            return false;

        CrowdVatWorldSquadObservation observation = _runtimeSquadObservations[runtimeSquadIndex];
        if (!observation.hasObservation)
            return false;

        aliveCount = observation.aliveCount;
        return true;
    }

    public bool TryGetRuntimeSquadAliveFrameInfo(out CrowdVatObservationFrameInfo frameInfo)
    {
        frameInfo = _runtimeSquadAliveFrameInfo;
        return _hasRuntimeSquadAliveFrameInfo;
    }

    private void EnsureRuntimeSquadObservationCapacity(int requiredCount)
    {
        if (_runtimeSquadObservations.Length == requiredCount)
            return;

        CrowdVatWorldSquadObservation[] resized = new CrowdVatWorldSquadObservation[requiredCount];
        int copyCount = Math.Min(_runtimeSquadObservations.Length, resized.Length);
        if (copyCount > 0)
            Array.Copy(_runtimeSquadObservations, resized, copyCount);

        for (int runtimeSquadIndex = copyCount; runtimeSquadIndex < resized.Length; runtimeSquadIndex++)
            resized[runtimeSquadIndex].runtimeSquadIndex = runtimeSquadIndex;

        _runtimeSquadObservations = resized;
    }
}
