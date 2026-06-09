using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class PerformancePoiPoint : MonoBehaviour
{
    public const string DefaultRelativeReplayTraceRoot = ".workspace/artifacts/performance-poi/repro-traces";

    public enum SamplingMode
    {
        RecordedReplay = 0,
        TurntableSweep = 1
    }

    public enum CameraPoseMode
    {
        LookAtTarget = 0,
        ExplicitRotation = 1,
        CameraPoseTransform = 2
    }

    [Header("Identity")]
    [SerializeField] private string _poiKey = string.Empty;
    [SerializeField] private string _poiId = "poi";
    [SerializeField] private string _category = "General";
    [SerializeField] [TextArea(2, 5)] private string _notes = string.Empty;
    [SerializeField] private int _sortIndex;
    [SerializeField] private bool _includeInAutomaticRuns = true;

    [Header("Sampling Mode")]
    [SerializeField] private SamplingMode _samplingMode = SamplingMode.RecordedReplay;
    [SerializeField] private float _turntableDegreesPerSecond = 30.0f;

    [Header("Sampling Overrides")]
    [SerializeField] private int _settleFramesOverride = -1;
    [SerializeField] private int _warmupFramesOverride = -1;
    [SerializeField] private int _sampleFramesOverride = -1;
    [SerializeField] private bool _captureScreenshot = true;
    [SerializeField] private bool _captureRenderDoc;
    [SerializeField] private bool _exportProfilerRaw;

    [Header("Character Pose")]
    [SerializeField] private bool _usePointForwardForCharacter = true;
    [SerializeField] private float _characterYawOffset;
    [SerializeField] private bool _projectCharacterToGround = true;
    [SerializeField] private float _groundProbeHeight = 2.0f;
    [SerializeField] private float _groundProbeDistance = 16.0f;
    [SerializeField] private LayerMask _groundLayers = ~0;

    [Header("Camera Pose")]
    [SerializeField] private CameraPoseMode _cameraPoseMode = CameraPoseMode.LookAtTarget;
    [SerializeField] private Transform _cameraPoseTransform;
    [SerializeField] private Vector3 _lookAtLocalOffset = new Vector3(0.0f, 1.55f, 3.4f);
    [SerializeField] private Vector3 _cameraLocalEulerAngles = new Vector3(8.0f, 0.0f, 0.0f);

    [Header("Gizmo")]
    [SerializeField] private Color _gizmoColor = new Color(0.10f, 0.82f, 0.92f, 1.0f);
    [SerializeField] private float _gizmoRadius = 0.28f;

    public string PoiKey => string.IsNullOrWhiteSpace(_poiKey) ? BuildDefaultPoiKey() : _poiKey.Trim();
    public string PoiId => string.IsNullOrWhiteSpace(_poiId) ? gameObject.name : _poiId.Trim();
    public string Category => string.IsNullOrWhiteSpace(_category) ? "General" : _category.Trim();
    public string Notes => _notes ?? string.Empty;
    public int SortIndex => _sortIndex;
    public bool IncludeInAutomaticRuns => _includeInAutomaticRuns;
    public SamplingMode PoiSamplingMode => _samplingMode;
    public bool RequiresReplayTrace => _samplingMode == SamplingMode.RecordedReplay;
    public bool UsesTurntableSweep => _samplingMode == SamplingMode.TurntableSweep;
    public bool CaptureScreenshot => _captureScreenshot;
    public bool CaptureRenderDoc => _captureRenderDoc;
    public bool ExportProfilerRaw => _exportProfilerRaw;
    public CameraPoseMode CameraAuthoringMode => _cameraPoseMode;
    public Transform CameraPoseTransform => _cameraPoseTransform;

    private void Reset()
    {
        EnsurePersistentPoiKey();
        _poiId = gameObject.name;
    }

    private void OnValidate()
    {
        EnsurePersistentPoiKey();
    }

    public string BuildReplayTracePath(string traceRootOverride = null)
    {
        string traceRoot = PerformancePoiSamplingArtifacts.ResolveReplayTraceRoot(traceRootOverride);
        string sceneLabel = RenderDocCaptureArtifacts.SanitizeLabel(gameObject.scene.name);
        string poiLabel = RenderDocCaptureArtifacts.SanitizeLabel(BuildPoiStorageLabel());

        if (string.IsNullOrWhiteSpace(sceneLabel))
            sceneLabel = "scene";

        if (string.IsNullOrWhiteSpace(poiLabel))
            poiLabel = "poi";

        return Path.Combine(traceRoot, sceneLabel, poiLabel + ".json");
    }

    public string BuildLegacyReplayTracePath(string traceRootOverride = null)
    {
        string traceRoot = PerformancePoiSamplingArtifacts.ResolveReplayTraceRoot(traceRootOverride);
        string sceneLabel = RenderDocCaptureArtifacts.SanitizeLabel(gameObject.scene.name);
        string poiLabel = RenderDocCaptureArtifacts.SanitizeLabel(PoiId);

        if (string.IsNullOrWhiteSpace(sceneLabel))
            sceneLabel = "scene";

        if (string.IsNullOrWhiteSpace(poiLabel))
            poiLabel = "poi";

        return Path.Combine(traceRoot, sceneLabel, poiLabel + ".json");
    }

    public string BuildPoiStorageLabel()
    {
        string shortKey = ComputeDeterministicHash(PoiKey);
        if (shortKey.Length > 8)
            shortKey = shortKey.Substring(0, 8);

        return string.Format("{0}-{1}", PoiId, shortKey);
    }

    public bool TryDeleteObsoleteReplayTraces(
        string traceRootOverride,
        out string deletedTracePaths,
        out string errorMessage)
    {
        deletedTracePaths = string.Empty;
        errorMessage = string.Empty;

        string currentTracePath = Path.GetFullPath(BuildReplayTracePath(traceRootOverride));
        string legacyTracePath = Path.GetFullPath(BuildLegacyReplayTracePath(traceRootOverride));
        if (string.Equals(currentTracePath, legacyTracePath, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!File.Exists(legacyTracePath))
            return true;

        try
        {
            File.Delete(legacyTracePath);
            deletedTracePaths = legacyTracePath;
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = "Failed to delete obsolete replay trace: " + exception.Message;
            return false;
        }
    }

    public bool TryLoadReplayTrace(
        string traceRootOverride,
        out PerformancePoiReplayTrace trace,
        out string tracePath,
        out string errorMessage)
    {
        trace = null;
        tracePath = BuildReplayTracePath(traceRootOverride);
        errorMessage = string.Empty;

        if (!File.Exists(tracePath))
        {
            errorMessage = "Replay trace file does not exist: " + tracePath;
            return false;
        }

        try
        {
            trace = PerformancePoiReplayController.LoadTrace(tracePath);
        }
        catch (System.Exception exception)
        {
            errorMessage = "Failed to load replay trace: " + exception.Message;
            return false;
        }

        if (trace == null || trace.poseSamples == null || trace.poseSamples.Count == 0)
        {
            errorMessage = "Replay trace is empty or invalid: " + tracePath;
            trace = null;
            return false;
        }

        return true;
    }

    public int ResolveSettleFrames(int fallbackValue)
    {
        return _settleFramesOverride >= 0 ? _settleFramesOverride : fallbackValue;
    }

    public int ResolveWarmupFrames(int fallbackValue)
    {
        return _warmupFramesOverride >= 0 ? _warmupFramesOverride : fallbackValue;
    }

    public int ResolveSampleFrames(int fallbackValue)
    {
        return _sampleFramesOverride >= 0 ? _sampleFramesOverride : fallbackValue;
    }

    public float ResolveTurntableDegreesPerSecond()
    {
        return _turntableDegreesPerSecond;
    }

    public float ResolveTurntableDurationSeconds()
    {
        float absDegreesPerSecond = Mathf.Abs(_turntableDegreesPerSecond);
        if (absDegreesPerSecond < 0.01f)
            return 0.0f;

        return 360.0f / absDegreesPerSecond;
    }

    public void ResolveCharacterPose(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;

        Vector3 forward = _usePointForwardForCharacter
            ? Vector3.ProjectOnPlane(transform.forward, Vector3.up)
            : Vector3.forward;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) * Quaternion.Euler(0.0f, _characterYawOffset, 0.0f);
    }

    public void ResolveCameraPose(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;

        if (_cameraPoseMode == CameraPoseMode.CameraPoseTransform && _cameraPoseTransform != null)
        {
            rotation = _cameraPoseTransform.rotation;
            return;
        }

        if (_cameraPoseMode == CameraPoseMode.ExplicitRotation)
        {
            rotation = transform.rotation * Quaternion.Euler(_cameraLocalEulerAngles);
            return;
        }

        Vector3 lookAtWorld = ResolveCameraFocusPoint(position, Quaternion.identity);
        Vector3 forward = lookAtWorld - position;
        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;

        rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    public Vector3 ResolveCameraFocusPoint()
    {
        ResolveCameraPose(out Vector3 position, out Quaternion rotation);
        return ResolveCameraFocusPoint(position, rotation);
    }

    public void ResolveTurntableCameraPose(float elapsedSeconds, out Vector3 position, out Quaternion rotation)
    {
        ResolveCameraPose(out position, out Quaternion baseRotation);
        float yawDelta = ResolveTurntableDegreesPerSecond() * Mathf.Max(0.0f, elapsedSeconds);
        rotation = Quaternion.AngleAxis(yawDelta, Vector3.up) * baseRotation;
    }

    public void ProjectCharacterToGround(ref Vector3 position)
    {
        if (!_projectCharacterToGround)
            return;

        Vector3 origin = position + Vector3.up * Mathf.Max(0.1f, _groundProbeHeight);
        float maxDistance = Mathf.Max(0.1f, _groundProbeHeight + _groundProbeDistance);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDistance, _groundLayers, QueryTriggerInteraction.Ignore))
            return;

        position = hit.point;
    }

    private void OnDrawGizmos()
    {
        DrawGizmoBody(selected: false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmoBody(selected: true);
    }

    private void DrawGizmoBody(bool selected)
    {
        Color previousColor = Gizmos.color;
        Color color = _gizmoColor;
        color.a = selected ? 0.95f : 0.75f;

        Gizmos.color = color;
        Gizmos.DrawSphere(transform.position, Mathf.Max(0.05f, _gizmoRadius));
        Gizmos.color = previousColor;
    }

    private Vector3 ResolveCameraFocusPoint(Vector3 cameraPosition, Quaternion cameraRotation)
    {
        if (_cameraPoseMode == CameraPoseMode.LookAtTarget)
            return transform.TransformPoint(_lookAtLocalOffset);

        if (_cameraPoseMode == CameraPoseMode.CameraPoseTransform && _cameraPoseTransform == null)
            return transform.TransformPoint(_lookAtLocalOffset);

        Quaternion rotation = cameraRotation == Quaternion.identity
            ? transform.rotation * Quaternion.Euler(_cameraLocalEulerAngles)
            : cameraRotation;
        return cameraPosition + rotation * Vector3.forward * 3.0f;
    }

    private void EnsurePersistentPoiKey()
    {
        if (!string.IsNullOrWhiteSpace(_poiKey))
            return;

        _poiKey = BuildDefaultPoiKey();
    }

    private string BuildDefaultPoiKey()
    {
#if UNITY_EDITOR
        string editorPersistentKey = BuildEditorPersistentPoiKey();
        if (!string.IsNullOrWhiteSpace(editorPersistentKey))
            return editorPersistentKey;
#endif
        return BuildFallbackPoiKey();
    }

    private string BuildFallbackPoiKey()
    {
        return string.Format(
            "{0}|{1}",
            gameObject.scene.path ?? string.Empty,
            BuildHierarchyPath(transform));
    }

#if UNITY_EDITOR
    public string BuildEditorPersistentPoiKey()
    {
        GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(this);
        string key = globalObjectId.ToString();
        if (string.IsNullOrWhiteSpace(key) ||
            string.Equals(key, "GlobalObjectId_V1-0-0-0-0", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return key;
    }
#endif

    private static string BuildHierarchyPath(Transform current)
    {
        if (current == null)
            return string.Empty;

        string segment = string.Format("{0}[{1}]", current.name, current.GetSiblingIndex());
        while (current.parent != null)
        {
            current = current.parent;
            segment = string.Format("{0}[{1}]/{2}", current.name, current.GetSiblingIndex(), segment);
        }

        return segment;
    }

    private static string ComputeDeterministicHash(string value)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            uint hash = offsetBasis;

            string safeValue = value ?? string.Empty;
            for (int index = 0; index < safeValue.Length; index++)
            {
                hash ^= safeValue[index];
                hash *= prime;
            }

            return hash.ToString("x8");
        }
    }
}
