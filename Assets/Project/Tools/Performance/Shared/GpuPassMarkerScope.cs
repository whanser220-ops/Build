using System;
using UnityEngine.Rendering;

public readonly struct GpuPassMarkerScope : IDisposable
{
    private readonly CommandBuffer _commandBuffer;
    private readonly string _label;

    public GpuPassMarkerScope(CommandBuffer commandBuffer, string passId, string displayName = null)
    {
        _commandBuffer = commandBuffer;
        _label = GpuPassMarkerUtility.BuildMarkerLabel(passId, displayName);
        _commandBuffer?.BeginSample(_label);
    }

    public void Dispose()
    {
        _commandBuffer?.EndSample(_label);
    }
}

public static class GpuPassMarkerUtility
{
    public static string BuildMarkerLabel(string passId, string displayName = null)
    {
        if (!IsValidPassId(passId))
            throw new ArgumentException("GPU pass id must be lowercase dot-separated text, for example crowd.sim.solve_crowd.", nameof(passId));

        if (string.IsNullOrWhiteSpace(displayName))
            return "[" + passId + "] " + passId;

        return "[" + passId + "] " + displayName.Trim();
    }

    public static bool TryExtractPassId(string markerLabel, out string passId)
    {
        passId = string.Empty;
        if (string.IsNullOrWhiteSpace(markerLabel) || markerLabel[0] != '[')
            return false;

        int endIndex = markerLabel.IndexOf(']');
        if (endIndex <= 1)
            return false;

        string candidate = markerLabel.Substring(1, endIndex - 1);
        if (!IsValidPassId(candidate))
            return false;

        passId = candidate;
        return true;
    }

    public static bool IsValidPassId(string passId)
    {
        if (string.IsNullOrWhiteSpace(passId))
            return false;

        bool previousWasDot = false;
        bool hasDot = false;
        for (int index = 0; index < passId.Length; index++)
        {
            char value = passId[index];
            bool valid = value >= 'a' && value <= 'z' ||
                value >= '0' && value <= '9' ||
                value == '_';

            if (value == '.')
            {
                if (index == 0 || previousWasDot)
                    return false;

                previousWasDot = true;
                hasDot = true;
                continue;
            }

            if (!valid)
                return false;

            previousWasDot = false;
        }

        return hasDot && !previousWasDot;
    }
}
