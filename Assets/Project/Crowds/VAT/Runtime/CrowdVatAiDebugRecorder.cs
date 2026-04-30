using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public enum CrowdVatAiDebugTargetKind
{
    RuntimeId = 0,
    SquadId = 1,
    GpuDiscovery = 2
}

[Serializable]
public struct CrowdVatAiDebugTarget
{
    public CrowdVatAiDebugTargetKind kind;
    public int id;
}

public enum CrowdVatAiDebugDiscoveryMode
{
    Anomaly = 0,
    RegionSphere = 1,
    RegionBox = 2,
    ScreenRect = 3,
    SquadAll = 4,
    SquadAnomaly = 5
}

[Serializable]
public struct CrowdVatAiDebugDiscoverySettings
{
    public const int MaxCandidateCapacity = 128;

    public CrowdVatAiDebugDiscoveryMode mode;
    public int candidateCapacity;
    public float highSpeedThreshold;
    public float inactiveMovingSpeedThreshold;
    public float speedSpikeThreshold;
    public float positionJumpThreshold;
    public float stuckSpeedThreshold;
    public float stuckMoveThreshold;
    public float stuckIntentSpeedThreshold;
    public int stuckFrameThreshold;
    public Vector3 worldCenter;
    public float radius;
    public Vector3 worldBoxCenter;
    public Vector3 worldBoxExtents;
    public Rect screenRect01;
    public Matrix4x4 worldToClip;
    public bool hasWorldToClip;
    public int squadId;

    public static CrowdVatAiDebugDiscoverySettings CreateDefault()
    {
        return new CrowdVatAiDebugDiscoverySettings
        {
            mode = CrowdVatAiDebugDiscoveryMode.Anomaly,
            candidateCapacity = MaxCandidateCapacity,
            highSpeedThreshold = 6.0f,
            inactiveMovingSpeedThreshold = 0.05f,
            speedSpikeThreshold = 4.0f,
            positionJumpThreshold = 1.5f,
            stuckSpeedThreshold = 0.05f,
            stuckMoveThreshold = 0.02f,
            stuckIntentSpeedThreshold = 0.5f,
            stuckFrameThreshold = 30,
            worldCenter = Vector3.zero,
            radius = 5.0f,
            worldBoxCenter = Vector3.zero,
            worldBoxExtents = new Vector3(5.0f, 5.0f, 5.0f),
            screenRect01 = new Rect(0.25f, 0.25f, 0.5f, 0.5f),
            worldToClip = Matrix4x4.identity,
            hasWorldToClip = false,
            squadId = 0
        };
    }

    public CrowdVatAiDebugDiscoverySettings Sanitized()
    {
        CrowdVatAiDebugDiscoverySettings settings = this;
        settings.candidateCapacity = Mathf.Clamp(settings.candidateCapacity, 1, MaxCandidateCapacity);
        settings.highSpeedThreshold = Mathf.Max(0.0f, settings.highSpeedThreshold);
        settings.inactiveMovingSpeedThreshold = Mathf.Max(0.0f, settings.inactiveMovingSpeedThreshold);
        settings.speedSpikeThreshold = Mathf.Max(0.0f, settings.speedSpikeThreshold);
        settings.positionJumpThreshold = Mathf.Max(0.0f, settings.positionJumpThreshold);
        settings.stuckSpeedThreshold = Mathf.Max(0.0f, settings.stuckSpeedThreshold);
        settings.stuckMoveThreshold = Mathf.Max(0.0f, settings.stuckMoveThreshold);
        settings.stuckIntentSpeedThreshold = Mathf.Max(0.0f, settings.stuckIntentSpeedThreshold);
        settings.stuckFrameThreshold = Mathf.Clamp(settings.stuckFrameThreshold, 1, 600);
        settings.radius = Mathf.Max(0.0f, settings.radius);
        settings.worldBoxExtents = new Vector3(
            Mathf.Max(0.0f, settings.worldBoxExtents.x),
            Mathf.Max(0.0f, settings.worldBoxExtents.y),
            Mathf.Max(0.0f, settings.worldBoxExtents.z));
        settings.screenRect01 = SanitizeScreenRect(settings.screenRect01);
        settings.squadId = Mathf.Max(0, settings.squadId);
        return settings;
    }

    public bool RequiresCameraMatrix()
    {
        return mode == CrowdVatAiDebugDiscoveryMode.ScreenRect;
    }

    private static Rect SanitizeScreenRect(Rect rect)
    {
        float xMin = Mathf.Clamp01(Mathf.Min(rect.xMin, rect.xMax));
        float xMax = Mathf.Clamp01(Mathf.Max(rect.xMin, rect.xMax));
        float yMin = Mathf.Clamp01(Mathf.Min(rect.yMin, rect.yMax));
        float yMax = Mathf.Clamp01(Mathf.Max(rect.yMin, rect.yMax));
        if (xMax <= xMin)
            xMax = Mathf.Min(xMin + 0.01f, 1.0f);
        if (yMax <= yMin)
            yMax = Mathf.Min(yMin + 0.01f, 1.0f);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }
}

public enum CrowdVatAiDebugGpuStageId
{
    PredictAfter = 10,
    SolveAfter = 20,
    GridAfter = 30,
    TargetAcquisitionAfter = 40,
    CombatAfter = 50,
    SpatialQueryAfter = 60,
    FinalizeAfter = 70
}

[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatAiDebugGpuStageRecord
{
    public uint frameIndex;
    public uint stageId;
    public int solverIteration;
    public uint targetSlot;
    public uint instanceIndex;
    public uint active;
    public uint deathState;
    public uint gridCellKey;
    public uint gridOccupantCount;
    public uint gridOverflow;
    public uint combatFlags;
    public uint acquisitionFlags;
    public int combatTargetIndex;
    public int acquisitionTargetIndex;
    public int acquisitionLastAttackerIndex;
    public uint valid;
    public Vector4 localPositionYaw;
    public Vector4 velocityScaleHealth;
    public Vector4 combatDistanceCooldownMuzzleHit;
    public Vector4 combatImpactDamageNormalizedHealth;
    public Vector4 acquisitionLastKnownScore;
    public Vector4 acquisitionTimers;
    public Vector4 acquisitionRejectCounts0;
    public Vector4 acquisitionRejectCounts1;
    public Vector4 combatDebugShotInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct CrowdVatAiDebugGpuCandidateRecord
{
    public uint frameIndex;
    public uint candidateSlot;
    public uint instanceIndex;
    public uint reasonMask;
    public float score;
    public uint active;
    public uint deathState;
    public uint gridCellKey;
    public uint gridOccupantCount;
    public uint gridOverflow;
    public uint combatFlags;
    public uint acquisitionFlags;
    public int combatTargetIndex;
    public int acquisitionTargetIndex;
    public uint valid;
    public uint reserved0;
    public Vector4 localPositionYaw;
    public Vector4 velocityScaleHealth;
    public Vector4 acquisitionRejectCounts0;
    public Vector4 acquisitionRejectCounts1;
    public Vector4 combatDistanceCooldownMuzzleHit;
}

public struct CrowdVatAiDebugSystemSnapshot
{
    public int instanceCount;
    public int activeSquadCount;
    public int activeAgentAssignmentCount;
    public int activeFormationSlotCount;
    public int gridCellCount;
    public Vector2Int gridDimensions;
    public Vector2 gridMinXZ;
    public Vector2 gridMaxXZ;
    public float gridCellSize;
    public int maxCellOccupancy;
    public int solverIterations;
    public int visibleInstanceCount;
    public bool hasVisibleBounds;
    public int activeSpatialQueryCount;
    public int maxSpatialQueries;
    public int maxSpatialQueryHits;
    public bool gpuCombatEnabled;
    public bool approximateCollisionEnabled;
    public bool localAvoidanceEnabled;
    public bool terrainCollisionEnabled;
    public bool staticSdfCollisionEnabled;
    public bool environmentDistanceFieldEnabled;
    public bool runtimeSquadAnchorsEnabled;
    public bool hasRuntimeRenderResource;
    public bool hasClip;
    public bool isPlayingClip;
    public uint aliveGpuCount;
    public uint activeGpuCount;
    public uint visibleGpuCount;
}

public struct CrowdVatAiDebugBufferSnapshot
{
    public string name;
    public bool bound;
    public int count;
    public int stride;
}

public struct CrowdVatAiDebugSquadSnapshot
{
    public bool valid;
    public int runtimeSquadIndex;
    public CrowdVatSquadCommandType commandType;
    public CrowdVatSquadFormationType formationType;
    public CrowdVatFactionMask factionMask;
    public CrowdVatSquadFlags flags;
    public Vector3 worldCenter;
    public Vector3 worldForward;
    public Vector3 worldTarget;
    public Vector2 formationSpacing;
    public float moveSpeed;
    public float anchorBlend;
    public float cohesionRadius;
    public float cohesionStrength;
    public int aliveCount;
    public bool hasAliveCount;
}

public struct CrowdVatAiDebugAgentSnapshot
{
    public bool valid;
    public int runtimeInstanceIndex;
    public Vector3 localPosition;
    public Vector3 worldPosition;
    public float yawDegrees;
    public Vector2 localVelocity;
    public Vector3 worldVelocity;
    public float speed;
    public float scale;
    public bool active;
    public uint activeRaw;
    public bool dead;
    public uint deathRaw;
    public float health;
    public int runtimeSquadIndex;
    public uint slotIndex;
    public CrowdVatSquadRoleMask roleMask;
    public CrowdVatSquadMemberFlags memberFlags;
    public CrowdVatFaction faction;
    public Vector3 spawnLocalPosition;
    public float spawnYawDegrees;
    public bool gridValid;
    public Vector2Int gridCell;
    public int gridCellKey;
    public int gridOccupantCount;
    public bool gridOverflow;
    public int[] gridNeighborPreview;
    public bool combatAvailable;
    public bool combatHasTarget;
    public bool combatHasLineOfSight;
    public bool combatFiredThisFrame;
    public int combatTargetIndex;
    public float combatDistance;
    public float combatCooldown;
    public float combatHealth;
    public float combatNormalizedHealth;
    public float combatHitFlash;
    public float combatLastDamage;
    public float combatMuzzleFlash;
    public bool combatHasImpact;
    public int combatVisibleSampleIndex;
    public int combatShotHitAgentIndex;
    public bool combatShotHitScene;
    public bool combatShotBlockedBySceneBeforeAgent;
    public Vector3 combatWorldOrigin;
    public Vector3 combatWorldTarget;
    public Vector3 combatWorldImpactNormal;
    public bool targetAcquisitionAvailable;
    public int acquisitionCurrentTargetIndex;
    public int acquisitionLastAttackerIndex;
    public uint acquisitionFlags;
    public Vector3 acquisitionLastKnownTargetLocal;
    public float acquisitionScore;
    public Vector4 acquisitionTimers;
    public Vector4 acquisitionRejectCounts0;
    public Vector4 acquisitionRejectCounts1;
    public bool animationAvailable;
    public int animationCurrentClipIndex;
    public int animationNextClipIndex;
    public float animationCurrentClipTime;
    public float animationNextClipTime;
    public float animationTransitionElapsed;
    public float animationTransitionDuration;
    public float animationPlaybackSpeedMultiplier;
    public bool animationIsBlending;
}

public sealed class CrowdVatAiDebugRecord
{
    private readonly List<KeyValuePair<string, object>> _fields = new List<KeyValuePair<string, object>>(24);

    public IReadOnlyList<KeyValuePair<string, object>> Fields => _fields;

    public void Add(string key, object value)
    {
        _fields.Add(new KeyValuePair<string, object>(key, value));
    }

    public string ToJson()
    {
        StringBuilder builder = new StringBuilder(256);
        builder.Append('{');
        for (int index = 0; index < _fields.Count; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append('"');
            AppendEscaped(builder, _fields[index].Key);
            builder.Append("\":");
            AppendJsonValue(builder, _fields[index].Value);
        }

        builder.Append('}');
        return builder.ToString();
    }

    public static string ToStableString(object value)
    {
        StringBuilder builder = new StringBuilder(64);
        AppendJsonValue(builder, value);
        return builder.ToString();
    }

    public static void AppendJsonValue(StringBuilder builder, object value)
    {
        if (value == null)
        {
            builder.Append("null");
            return;
        }

        switch (value)
        {
            case string text:
                builder.Append('"');
                AppendEscaped(builder, text);
                builder.Append('"');
                return;
            case bool flag:
                builder.Append(flag ? "true" : "false");
                return;
            case int intValue:
                builder.Append(intValue.ToString(CultureInfo.InvariantCulture));
                return;
            case uint uintValue:
                builder.Append(uintValue.ToString(CultureInfo.InvariantCulture));
                return;
            case long longValue:
                builder.Append(longValue.ToString(CultureInfo.InvariantCulture));
                return;
            case float floatValue:
                AppendFloat(builder, floatValue);
                return;
            case double doubleValue:
                AppendFloat(builder, (float)doubleValue);
                return;
            case Vector2 vector2:
                AppendVector2(builder, vector2);
                return;
            case Vector2Int vector2Int:
                builder.Append('[');
                builder.Append(vector2Int.x.ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(vector2Int.y.ToString(CultureInfo.InvariantCulture));
                builder.Append(']');
                return;
            case Vector3 vector3:
                AppendVector3(builder, vector3);
                return;
            case Vector4 vector4:
                builder.Append('[');
                AppendFloat(builder, vector4.x);
                builder.Append(',');
                AppendFloat(builder, vector4.y);
                builder.Append(',');
                AppendFloat(builder, vector4.z);
                builder.Append(',');
                AppendFloat(builder, vector4.w);
                builder.Append(']');
                return;
            case int[] intArray:
                AppendIntArray(builder, intArray);
                return;
            case uint[] uintArray:
                AppendUIntArray(builder, uintArray);
                return;
            case Enum enumValue:
                builder.Append('"');
                AppendEscaped(builder, enumValue.ToString());
                builder.Append('"');
                return;
            default:
                builder.Append('"');
                AppendEscaped(builder, value.ToString());
                builder.Append('"');
                return;
        }
    }

    private static void AppendVector2(StringBuilder builder, Vector2 value)
    {
        builder.Append('[');
        AppendFloat(builder, value.x);
        builder.Append(',');
        AppendFloat(builder, value.y);
        builder.Append(']');
    }

    private static void AppendVector3(StringBuilder builder, Vector3 value)
    {
        builder.Append('[');
        AppendFloat(builder, value.x);
        builder.Append(',');
        AppendFloat(builder, value.y);
        builder.Append(',');
        AppendFloat(builder, value.z);
        builder.Append(']');
    }

    private static void AppendIntArray(StringBuilder builder, int[] values)
    {
        builder.Append('[');
        for (int index = 0; index < values.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append(values[index].ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(']');
    }

    private static void AppendUIntArray(StringBuilder builder, uint[] values)
    {
        builder.Append('[');
        for (int index = 0; index < values.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append(values[index].ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(']');
    }

    private static void AppendFloat(StringBuilder builder, float value)
    {
        if (float.IsNaN(value))
        {
            builder.Append("\"NaN\"");
            return;
        }

        if (float.IsPositiveInfinity(value))
        {
            builder.Append("\"Infinity\"");
            return;
        }

        if (float.IsNegativeInfinity(value))
        {
            builder.Append("\"-Infinity\"");
            return;
        }

        builder.Append(value.ToString("0.####", CultureInfo.InvariantCulture));
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }
    }
}

public sealed class CrowdVatAiDebugRecorder
{
    private const uint AiDebugDeathStateDeadFlag = 0x80000000u;
    private const uint InstanceCombatFlagHasTarget = 1u << 0;
    private const uint InstanceCombatFlagHasLineOfSight = 1u << 1;
    private const uint InstanceCombatFlagFiredThisFrame = 1u << 2;
    private const uint GpuCandidateReasonRegion = 1u << 0;
    private const uint GpuCandidateReasonDeadButActive = 1u << 1;
    private const uint GpuCandidateReasonGridOverflow = 1u << 2;
    private const uint GpuCandidateReasonInvalidState = 1u << 3;
    private const uint GpuCandidateReasonHighSpeed = 1u << 4;
    private const uint GpuCandidateReasonCombatTargetInvalid = 1u << 5;
    private const uint GpuCandidateReasonAcquisitionTargetInvalid = 1u << 6;
    private const uint GpuCandidateReasonRejectSaturated = 1u << 7;
    private const uint GpuCandidateReasonShotBlockedByScene = 1u << 8;
    private const uint GpuCandidateReasonScreen = 1u << 9;
    private const uint GpuCandidateReasonSquad = 1u << 10;
    private const uint GpuCandidateReasonInactiveMoving = 1u << 11;
    private const uint GpuCandidateReasonNoLineOfSight = 1u << 12;
    private const uint GpuCandidateReasonSpeedSpike = 1u << 13;
    private const uint GpuCandidateReasonStuck = 1u << 14;
    private const uint GpuCandidateReasonPositionJump = 1u << 15;

    private readonly List<CrowdVatAiDebugTarget> _targets = new List<CrowdVatAiDebugTarget>(8);
    private readonly List<CrowdVatAiDebugRecord> _records = new List<CrowdVatAiDebugRecord>(4096);
    private readonly Dictionary<string, string> _lastValues = new Dictionary<string, string>(4096);
    private readonly HashSet<int> _capturedRelatedAgents = new HashSet<int>();
    private readonly List<CrowdVatAiDebugBufferSnapshot> _bufferSnapshots = new List<CrowdVatAiDebugBufferSnapshot>(32);
    private readonly List<CrowdVatAiDebugGpuStageRecord> _gpuStageRecords = new List<CrowdVatAiDebugGpuStageRecord>(256);
    private readonly List<CrowdVatAiDebugGpuCandidateRecord> _gpuCandidateRecords = new List<CrowdVatAiDebugGpuCandidateRecord>(128);

    private CrowdVatIndirectRenderer _renderer;
    private string _phenomenon = string.Empty;
    private string _reproductionSteps = string.Empty;
    private string _expectedBehavior = string.Empty;
    private string _sessionName = string.Empty;
    private CrowdVatAiDebugDiscoverySettings _discoverySettings = CrowdVatAiDebugDiscoverySettings.CreateDefault();
    private int _startFrame;
    private float _startTime;
    private int _lastCapturedFrame = -1;
    private float _lastCapturedTime;
    private int _droppedUnchangedFrames;
    private bool _isRecording;

    public bool IsRecording => _isRecording;
    public int RecordCount => _records.Count;
    public int DroppedUnchangedFrames => _droppedUnchangedFrames;
    public string SessionName => _sessionName;
    public IReadOnlyList<CrowdVatAiDebugTarget> Targets => _targets;
    public CrowdVatAiDebugDiscoverySettings DiscoverySettings => _discoverySettings;

    public void Start(
        CrowdVatIndirectRenderer renderer,
        IReadOnlyList<CrowdVatAiDebugTarget> targets,
        string phenomenon,
        string reproductionSteps,
        string expectedBehavior)
    {
        Start(renderer, targets, phenomenon, reproductionSteps, expectedBehavior, CrowdVatAiDebugDiscoverySettings.CreateDefault());
    }

    public void Start(
        CrowdVatIndirectRenderer renderer,
        IReadOnlyList<CrowdVatAiDebugTarget> targets,
        string phenomenon,
        string reproductionSteps,
        string expectedBehavior,
        CrowdVatAiDebugDiscoverySettings discoverySettings)
    {
        _renderer = renderer;
        _targets.Clear();
        if (targets != null)
            _targets.AddRange(targets);

        _discoverySettings = discoverySettings.Sanitized();
        _phenomenon = phenomenon ?? string.Empty;
        _reproductionSteps = reproductionSteps ?? string.Empty;
        _expectedBehavior = expectedBehavior ?? string.Empty;
        _sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        _records.Clear();
        _lastValues.Clear();
        _capturedRelatedAgents.Clear();
        _bufferSnapshots.Clear();
        _gpuStageRecords.Clear();
        _gpuCandidateRecords.Clear();
        _startFrame = Application.isPlaying ? Time.frameCount : 0;
        _startTime = Application.isPlaying ? Time.realtimeSinceStartup : 0.0f;
        _lastCapturedFrame = -1;
        _lastCapturedTime = _startTime;
        _droppedUnchangedFrames = 0;
        _isRecording = true;

        CrowdVatAiDebugRecord record = CreateRecord("session.begin", -1, 0.0f);
        record.Add("targetCount", _targets.Count);
        record.Add("phenomenon", _phenomenon);
        record.Add("repro", _reproductionSteps);
        record.Add("expected", _expectedBehavior);
        record.Add("discovery.mode", _discoverySettings.mode);
        record.Add("discovery.capacity", _discoverySettings.candidateCapacity);
        record.Add("discovery.highSpeed", _discoverySettings.highSpeedThreshold);
        record.Add("discovery.speedSpike", _discoverySettings.speedSpikeThreshold);
        record.Add("discovery.positionJump", _discoverySettings.positionJumpThreshold);
        record.Add("discovery.stuckFrames", _discoverySettings.stuckFrameThreshold);
        record.Add("discovery.center", _discoverySettings.worldCenter);
        record.Add("discovery.radius", _discoverySettings.radius);
        record.Add("discovery.boxCenter", _discoverySettings.worldBoxCenter);
        record.Add("discovery.boxExtents", _discoverySettings.worldBoxExtents);
        record.Add("discovery.screenRect01", new Vector4(
            _discoverySettings.screenRect01.xMin,
            _discoverySettings.screenRect01.yMin,
            _discoverySettings.screenRect01.xMax,
            _discoverySettings.screenRect01.yMax));
        record.Add("discovery.hasWorldToClip", _discoverySettings.hasWorldToClip);
        record.Add("discovery.squad", _discoverySettings.squadId);
        _records.Add(record);
    }

    public void Stop()
    {
        if (!_isRecording)
            return;

        CrowdVatAiDebugRecord record = CreateRecord("session.end", -1, ComputeDeltaTime());
        record.Add("recordCount", _records.Count);
        record.Add("droppedUnchangedFrames", _droppedUnchangedFrames);
        _records.Add(record);
        _isRecording = false;
    }

    public void Discard()
    {
        _isRecording = false;
        _records.Clear();
        _lastValues.Clear();
        _capturedRelatedAgents.Clear();
        _bufferSnapshots.Clear();
        _gpuStageRecords.Clear();
        _gpuCandidateRecords.Clear();
        _discoverySettings = CrowdVatAiDebugDiscoverySettings.CreateDefault();
    }

    public void CaptureFrame()
    {
        if (!_isRecording || _renderer == null)
            return;

        int frame = Application.isPlaying ? Time.frameCount : 0;
        if (Application.isPlaying && frame == _lastCapturedFrame)
            return;

        float deltaTime = ComputeDeltaTime();
        int recordCountBefore = _records.Count;
        _capturedRelatedAgents.Clear();

        if (_renderer.TryGetAiDebugSystemSnapshot(out CrowdVatAiDebugSystemSnapshot systemSnapshot))
        {
            EmitSystemSnapshot(systemSnapshot, deltaTime);
            EmitBufferSnapshots(deltaTime);
        }

        for (int targetIndex = 0; targetIndex < _targets.Count; targetIndex++)
            CaptureTarget(_targets[targetIndex], deltaTime);

        if (_renderer.TryReadAiDebugGpuCandidateRecords(_gpuCandidateRecords, out int discoveredCount, out int discoveryCapacity))
            EmitGpuDiscoveryRecords(_gpuCandidateRecords, discoveredCount, discoveryCapacity, deltaTime);

        if (_renderer.TryReadAiDebugGpuStageRecords(_gpuStageRecords))
            EmitGpuStageRecords(_gpuStageRecords, deltaTime);

        if (_records.Count == recordCountBefore)
            _droppedUnchangedFrames++;

        _lastCapturedFrame = frame;
        _lastCapturedTime = Application.isPlaying ? Time.realtimeSinceStartup : _lastCapturedTime;
    }

    public string Export(string directory)
    {
        if (string.IsNullOrEmpty(directory))
            throw new ArgumentException("导出目录不能为空。", nameof(directory));

        Directory.CreateDirectory(directory);
        string baseName = "crowd_ai_debug_" + (string.IsNullOrEmpty(_sessionName) ? DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) : _sessionName);
        string jsonlPath = Path.Combine(directory, baseName + ".jsonl");
        string markdownPath = Path.Combine(directory, baseName + ".md");

        using (StreamWriter writer = new StreamWriter(jsonlPath, false, Encoding.UTF8))
        {
            for (int index = 0; index < _records.Count; index++)
                writer.WriteLine(_records[index].ToJson());
        }

        File.WriteAllText(markdownPath, BuildMarkdownSummary(jsonlPath), Encoding.UTF8);
        return markdownPath;
    }

    private void CaptureTarget(CrowdVatAiDebugTarget target, float deltaTime)
    {
        switch (target.kind)
        {
            case CrowdVatAiDebugTargetKind.RuntimeId:
                CaptureAgent(target.id, "target", deltaTime);
                break;
            case CrowdVatAiDebugTargetKind.SquadId:
                CaptureSquad(target.id, "target", deltaTime);
                break;
            case CrowdVatAiDebugTargetKind.GpuDiscovery:
                break;
        }
    }

    private void CaptureAgent(int instanceIndex, string role, float deltaTime)
    {
        if (!_renderer.TryGetAiDebugAgentSnapshot(instanceIndex, out CrowdVatAiDebugAgentSnapshot snapshot))
            return;

        EmitAgentState(snapshot, role, deltaTime);
        EmitGpuFinal(snapshot, role, deltaTime);
        EmitSpatialGrid(snapshot, role, deltaTime);
        EmitCombat(snapshot, role, deltaTime);
        EmitAnimation(snapshot, role, deltaTime);
        EmitVisibility(snapshot, role, deltaTime);

        if (snapshot.runtimeSquadIndex >= 0)
            CaptureSquad(snapshot.runtimeSquadIndex, "agent.squad", deltaTime);

        if (snapshot.combatHasTarget && snapshot.combatTargetIndex >= 0 && _capturedRelatedAgents.Add(snapshot.combatTargetIndex))
            CaptureRelatedAgent(snapshot.combatTargetIndex, "combat.target", snapshot.runtimeInstanceIndex, deltaTime);

        if (snapshot.gridNeighborPreview != null)
        {
            int previewCount = Mathf.Min(snapshot.gridNeighborPreview.Length, 3);
            for (int index = 0; index < previewCount; index++)
            {
                int neighborIndex = snapshot.gridNeighborPreview[index];
                if (neighborIndex < 0 || neighborIndex == snapshot.runtimeInstanceIndex || !_capturedRelatedAgents.Add(neighborIndex))
                    continue;

                CaptureRelatedAgent(neighborIndex, "grid.neighbor", snapshot.runtimeInstanceIndex, deltaTime);
            }
        }
    }

    private void CaptureRelatedAgent(int instanceIndex, string relation, int ownerIndex, float deltaTime)
    {
        if (!_renderer.TryGetAiDebugAgentSnapshot(instanceIndex, out CrowdVatAiDebugAgentSnapshot snapshot))
            return;

        CrowdVatAiDebugRecord relationRecord = CreateRecord("relation.agent", ownerIndex, deltaTime);
        relationRecord.Add("rel", relation);
        relationRecord.Add("other", instanceIndex);
        relationRecord.Add("other.pos", snapshot.worldPosition);
        relationRecord.Add("other.dead", snapshot.dead);
        relationRecord.Add("other.squad", snapshot.runtimeSquadIndex);
        relationRecord.Add("other.faction", snapshot.faction);
        _records.Add(relationRecord);
    }

    private void CaptureSquad(int runtimeSquadIndex, string role, float deltaTime)
    {
        if (!_renderer.TryGetAiDebugSquadSnapshot(runtimeSquadIndex, out CrowdVatAiDebugSquadSnapshot snapshot))
            return;

        CrowdVatAiDebugRecord record = CreateSparseRecord("squad.intent", runtimeSquadIndex, deltaTime);
        AddSparse(record, "role", role);
        AddSparse(record, "cmd", snapshot.commandType);
        AddSparse(record, "formation", snapshot.formationType);
        AddSparse(record, "factionMask", snapshot.factionMask);
        AddSparse(record, "flags", snapshot.flags);
        AddSparse(record, "center", snapshot.worldCenter);
        AddSparse(record, "forward", snapshot.worldForward);
        AddSparse(record, "target", snapshot.worldTarget);
        AddSparse(record, "spacing", snapshot.formationSpacing);
        AddSparse(record, "moveSpeed", snapshot.moveSpeed);
        AddSparse(record, "anchorBlend", snapshot.anchorBlend);
        AddSparse(record, "cohesionRadius", snapshot.cohesionRadius);
        AddSparse(record, "cohesionStrength", snapshot.cohesionStrength);
        AddSparse(record, "alive.has", snapshot.hasAliveCount);
        if (snapshot.hasAliveCount)
            AddSparse(record, "alive", snapshot.aliveCount);
        CommitSparse(record);
    }

    private void EmitSystemSnapshot(CrowdVatAiDebugSystemSnapshot snapshot, float deltaTime)
    {
        CrowdVatAiDebugRecord record = CreateSparseRecord("frame.dispatch", -1, deltaTime);
        AddSparse(record, "sys.instance", snapshot.instanceCount);
        AddSparse(record, "sys.squad", snapshot.activeSquadCount);
        AddSparse(record, "sys.assignment", snapshot.activeAgentAssignmentCount);
        AddSparse(record, "sys.formationSlot", snapshot.activeFormationSlotCount);
        AddSparse(record, "sys.aliveGpu", snapshot.aliveGpuCount);
        AddSparse(record, "sys.activeGpu", snapshot.activeGpuCount);
        AddSparse(record, "sys.visibleGpu", snapshot.visibleGpuCount);
        AddSparse(record, "grid.dim", snapshot.gridDimensions);
        AddSparse(record, "grid.cellSize", snapshot.gridCellSize);
        AddSparse(record, "grid.maxOcc", snapshot.maxCellOccupancy);
        AddSparse(record, "gpu.solve.iter", snapshot.solverIterations);
        AddSparse(record, "gpu.combat", snapshot.gpuCombatEnabled);
        AddSparse(record, "gpu.approxCollision", snapshot.approximateCollisionEnabled);
        AddSparse(record, "gpu.localAvoid", snapshot.localAvoidanceEnabled);
        AddSparse(record, "gpu.terrainCollision", snapshot.terrainCollisionEnabled);
        AddSparse(record, "gpu.staticSdf", snapshot.staticSdfCollisionEnabled);
        AddSparse(record, "gpu.envDistance", snapshot.environmentDistanceFieldEnabled);
        AddSparse(record, "gpu.runtimeSquad", snapshot.runtimeSquadAnchorsEnabled);
        AddSparse(record, "render.visible", snapshot.visibleInstanceCount);
        AddSparse(record, "render.hasBounds", snapshot.hasVisibleBounds);
        AddSparse(record, "resource.hasRuntimeRender", snapshot.hasRuntimeRenderResource);
        AddSparse(record, "clip.has", snapshot.hasClip);
        AddSparse(record, "clip.playing", snapshot.isPlayingClip);
        AddSparse(record, "query.active", snapshot.activeSpatialQueryCount);
        AddSparse(record, "query.max", snapshot.maxSpatialQueries);
        AddSparse(record, "query.maxHits", snapshot.maxSpatialQueryHits);
        CommitSparse(record);
    }

    private void EmitBufferSnapshots(float deltaTime)
    {
        _bufferSnapshots.Clear();
        _renderer.GetAiDebugBufferSnapshots(_bufferSnapshots);
        for (int index = 0; index < _bufferSnapshots.Count; index++)
        {
            CrowdVatAiDebugBufferSnapshot snapshot = _bufferSnapshots[index];
            CrowdVatAiDebugRecord record = CreateSparseRecord("gpu.resource", -1, deltaTime);
            string prefix = snapshot.name + ".";
            AddSparse(record, prefix + "bound", snapshot.bound);
            AddSparse(record, prefix + "count", snapshot.count);
            AddSparse(record, prefix + "stride", snapshot.stride);
            CommitSparse(record);
        }
    }

    private void EmitAgentState(CrowdVatAiDebugAgentSnapshot snapshot, string role, float deltaTime)
    {
        CrowdVatAiDebugRecord record = CreateSparseRecord("agent.state", snapshot.runtimeInstanceIndex, deltaTime);
        AddSparse(record, "role", role);
        AddSparse(record, "pos", snapshot.worldPosition);
        AddSparse(record, "localPos", snapshot.localPosition);
        AddSparse(record, "yaw", snapshot.yawDegrees);
        AddSparse(record, "vel", snapshot.worldVelocity);
        AddSparse(record, "localVel", snapshot.localVelocity);
        AddSparse(record, "speed", snapshot.speed);
        AddSparse(record, "scale", snapshot.scale);
        AddSparse(record, "active", snapshot.active);
        AddSparse(record, "dead", snapshot.dead);
        AddSparse(record, "death.raw", snapshot.deathRaw);
        AddSparse(record, "health", snapshot.health);
        AddSparse(record, "squad", snapshot.runtimeSquadIndex);
        AddSparse(record, "slot", (int)snapshot.slotIndex);
        AddSparse(record, "roleMask", snapshot.roleMask);
        AddSparse(record, "memberFlags", snapshot.memberFlags);
        AddSparse(record, "faction", snapshot.faction);
        CommitSparse(record);
    }

    private void EmitGpuFinal(CrowdVatAiDebugAgentSnapshot snapshot, string role, float deltaTime)
    {
        CrowdVatAiDebugRecord record = CreateSparseRecord("gpu.phase", snapshot.runtimeInstanceIndex, deltaTime);
        AddSparse(record, "role", role);
        AddSparse(record, "ph", "gpu.final");
        AddSparse(record, "pos", snapshot.worldPosition);
        AddSparse(record, "vel", snapshot.worldVelocity);
        AddSparse(record, "yaw", snapshot.yawDegrees);
        AddSparse(record, "activeRaw", snapshot.activeRaw);
        AddSparse(record, "deathRaw", snapshot.deathRaw);
        CommitSparse(record);
    }

    private void EmitGpuDiscoveryRecords(
        IReadOnlyList<CrowdVatAiDebugGpuCandidateRecord> records,
        int discoveredCount,
        int discoveryCapacity,
        float deltaTime)
    {
        int recordedCount = records != null ? records.Count : 0;
        CrowdVatAiDebugRecord summaryRecord = CreateSparseRecord("gpu.discovery", -1, deltaTime);
        AddSparse(summaryRecord, "found", discoveredCount);
        AddSparse(summaryRecord, "recorded", recordedCount);
        AddSparse(summaryRecord, "capacity", discoveryCapacity);
        CommitSparse(summaryRecord);

        if (records == null)
            return;

        for (int index = 0; index < records.Count; index++)
        {
            CrowdVatAiDebugGpuCandidateRecord snapshot = records[index];
            if (snapshot.valid == 0u)
                continue;

            Vector3 localPosition = new Vector3(
                snapshot.localPositionYaw.x,
                snapshot.localPositionYaw.y,
                snapshot.localPositionYaw.z);
            Vector2 localVelocity = new Vector2(
                snapshot.velocityScaleHealth.x,
                snapshot.velocityScaleHealth.y);
            Vector3 worldPosition = TransformAiDebugLocalPoint(localPosition);
            Vector3 worldVelocity = TransformAiDebugLocalVector(new Vector3(localVelocity.x, 0.0f, localVelocity.y));

            CrowdVatAiDebugRecord record = CreateSparseRecord("gpu.discovery", (int)snapshot.instanceIndex, deltaTime);
            AddSparse(record, "gpu.frame", (int)snapshot.frameIndex);
            AddSparse(record, "slot", (int)snapshot.candidateSlot);
            AddSparse(record, "reason", GetGpuCandidateReasonText(snapshot.reasonMask));
            AddSparse(record, "reasonMask", snapshot.reasonMask);
            AddSparse(record, "score", snapshot.score);
            AddSparse(record, "pos", worldPosition);
            AddSparse(record, "localPos", localPosition);
            AddSparse(record, "yaw", Mathf.Repeat(snapshot.localPositionYaw.w * Mathf.Rad2Deg, 360.0f));
            AddSparse(record, "vel", worldVelocity);
            AddSparse(record, "localVel", localVelocity);
            AddSparse(record, "speed", worldVelocity.magnitude);
            AddSparse(record, "scale", snapshot.velocityScaleHealth.z);
            AddSparse(record, "activeRaw", snapshot.active);
            AddSparse(record, "dead", (snapshot.deathState & AiDebugDeathStateDeadFlag) != 0u);
            AddSparse(record, "deathRaw", snapshot.deathState);
            AddSparse(record, "health", snapshot.velocityScaleHealth.w);

            if (snapshot.gridCellKey != uint.MaxValue)
            {
                AddSparse(record, "grid.cellKey", (int)snapshot.gridCellKey);
                AddSparse(record, "grid.occ", (int)snapshot.gridOccupantCount);
                AddSparse(record, "grid.overflow", snapshot.gridOverflow != 0u);
            }

            AddSparse(record, "combat.flags", snapshot.combatFlags);
            AddSparse(record, "target", snapshot.combatTargetIndex);
            AddSparse(record, "hasTarget", (snapshot.combatFlags & InstanceCombatFlagHasTarget) != 0u);
            AddSparse(record, "los", (snapshot.combatFlags & InstanceCombatFlagHasLineOfSight) != 0u);
            AddSparse(record, "fired", (snapshot.combatFlags & InstanceCombatFlagFiredThisFrame) != 0u);
            AddSparse(record, "dist", snapshot.combatDistanceCooldownMuzzleHit.x);
            AddSparse(record, "cooldown", snapshot.combatDistanceCooldownMuzzleHit.y);
            AddSparse(record, "muzzleFlash", snapshot.combatDistanceCooldownMuzzleHit.z);
            AddSparse(record, "hitFlash", snapshot.combatDistanceCooldownMuzzleHit.w);
            AddSparse(record, "acq.target", snapshot.acquisitionTargetIndex);
            AddSparse(record, "acq.flags", snapshot.acquisitionFlags);
            EmitTargetRejectCounts(record, snapshot.acquisitionRejectCounts0, snapshot.acquisitionRejectCounts1);
            CommitSparse(record);
        }
    }

    private void EmitGpuStageRecords(IReadOnlyList<CrowdVatAiDebugGpuStageRecord> records, float deltaTime)
    {
        if (records == null)
            return;

        for (int index = 0; index < records.Count; index++)
        {
            CrowdVatAiDebugGpuStageRecord snapshot = records[index];
            if (snapshot.valid == 0u)
                continue;

            Vector3 localPosition = new Vector3(
                snapshot.localPositionYaw.x,
                snapshot.localPositionYaw.y,
                snapshot.localPositionYaw.z);
            Vector2 localVelocity = new Vector2(
                snapshot.velocityScaleHealth.x,
                snapshot.velocityScaleHealth.y);
            Vector3 worldPosition = TransformAiDebugLocalPoint(localPosition);
            Vector3 worldVelocity = TransformAiDebugLocalVector(new Vector3(localVelocity.x, 0.0f, localVelocity.y));
            Vector3 acquisitionLastKnownLocal = new Vector3(
                snapshot.acquisitionLastKnownScore.x,
                snapshot.acquisitionLastKnownScore.y,
                snapshot.acquisitionLastKnownScore.z);

            CrowdVatAiDebugRecord record = CreateSparseRecord("gpu.stage", (int)snapshot.instanceIndex, deltaTime);
            AddSparse(record, "gpu.frame", (int)snapshot.frameIndex);
            AddSparse(record, "slot", (int)snapshot.targetSlot);
            AddSparse(record, "ph", GetGpuStageName(snapshot.stageId));
            if (snapshot.solverIteration >= 0)
                AddSparse(record, "iter", snapshot.solverIteration);
            AddSparse(record, "pos", worldPosition);
            AddSparse(record, "localPos", localPosition);
            AddSparse(record, "yaw", Mathf.Repeat(snapshot.localPositionYaw.w * Mathf.Rad2Deg, 360.0f));
            AddSparse(record, "vel", worldVelocity);
            AddSparse(record, "localVel", localVelocity);
            AddSparse(record, "speed", worldVelocity.magnitude);
            AddSparse(record, "scale", snapshot.velocityScaleHealth.z);
            AddSparse(record, "activeRaw", snapshot.active);
            AddSparse(record, "dead", (snapshot.deathState & AiDebugDeathStateDeadFlag) != 0u);
            AddSparse(record, "deathRaw", snapshot.deathState);
            AddSparse(record, "health", snapshot.velocityScaleHealth.w);

            if (snapshot.gridCellKey != uint.MaxValue)
            {
                AddSparse(record, "grid.cellKey", (int)snapshot.gridCellKey);
                AddSparse(record, "grid.occ", (int)snapshot.gridOccupantCount);
                AddSparse(record, "grid.overflow", snapshot.gridOverflow != 0u);
            }

            AddSparse(record, "combat.flags", snapshot.combatFlags);
            AddSparse(record, "target", snapshot.combatTargetIndex);
            AddSparse(record, "hasTarget", (snapshot.combatFlags & InstanceCombatFlagHasTarget) != 0u);
            AddSparse(record, "los", (snapshot.combatFlags & InstanceCombatFlagHasLineOfSight) != 0u);
            AddSparse(record, "fired", (snapshot.combatFlags & InstanceCombatFlagFiredThisFrame) != 0u);
            AddSparse(record, "dist", snapshot.combatDistanceCooldownMuzzleHit.x);
            AddSparse(record, "cooldown", snapshot.combatDistanceCooldownMuzzleHit.y);
            AddSparse(record, "muzzleFlash", snapshot.combatDistanceCooldownMuzzleHit.z);
            AddSparse(record, "hitFlash", snapshot.combatDistanceCooldownMuzzleHit.w);
            AddSparse(record, "impact", snapshot.combatImpactDamageNormalizedHealth.x > 0.001f);
            AddSparse(record, "lastDamage", snapshot.combatImpactDamageNormalizedHealth.y);
            AddSparse(record, "nHealth", snapshot.combatImpactDamageNormalizedHealth.z);
            AddSparse(record, "visibleSampleIndex", Mathf.RoundToInt(snapshot.combatDebugShotInfo.x));
            AddSparse(record, "shot.hitAgent", Mathf.RoundToInt(snapshot.combatDebugShotInfo.y));
            AddSparse(record, "shot.hitScene", snapshot.combatDebugShotInfo.z > 0.5f);
            AddSparse(record, "shot.blockedBySceneBeforeAgent", snapshot.combatDebugShotInfo.w > 0.5f);

            AddSparse(record, "acq.target", snapshot.acquisitionTargetIndex);
            AddSparse(record, "acq.lastAttacker", snapshot.acquisitionLastAttackerIndex);
            AddSparse(record, "acq.flags", snapshot.acquisitionFlags);
            AddSparse(record, "acq.lastKnownLocal", acquisitionLastKnownLocal);
            AddSparse(record, "acq.lastKnown", TransformAiDebugLocalPoint(acquisitionLastKnownLocal));
            AddSparse(record, "acq.score", snapshot.acquisitionLastKnownScore.w);
            AddSparse(record, "acq.timers", snapshot.acquisitionTimers);
            EmitTargetRejectCounts(record, snapshot.acquisitionRejectCounts0, snapshot.acquisitionRejectCounts1);
            CommitSparse(record);
        }
    }

    private void EmitSpatialGrid(CrowdVatAiDebugAgentSnapshot snapshot, string role, float deltaTime)
    {
        CrowdVatAiDebugRecord record = CreateSparseRecord("spatial.grid", snapshot.runtimeInstanceIndex, deltaTime);
        AddSparse(record, "role", role);
        AddSparse(record, "valid", snapshot.gridValid);
        if (snapshot.gridValid)
        {
            AddSparse(record, "cell", snapshot.gridCell);
            AddSparse(record, "cellKey", snapshot.gridCellKey);
            AddSparse(record, "occ", snapshot.gridOccupantCount);
            AddSparse(record, "overflow", snapshot.gridOverflow);
            AddSparse(record, "neighbors", snapshot.gridNeighborPreview ?? Array.Empty<int>());
        }
        CommitSparse(record);
    }

    private void EmitCombat(CrowdVatAiDebugAgentSnapshot snapshot, string role, float deltaTime)
    {
        CrowdVatAiDebugRecord record = CreateSparseRecord("combat.query", snapshot.runtimeInstanceIndex, deltaTime);
        AddSparse(record, "role", role);
        AddSparse(record, "available", snapshot.combatAvailable);
        if (snapshot.combatAvailable)
        {
            AddSparse(record, "target", snapshot.combatTargetIndex);
            AddSparse(record, "hasTarget", snapshot.combatHasTarget);
            AddSparse(record, "los", snapshot.combatHasLineOfSight);
            AddSparse(record, "fired", snapshot.combatFiredThisFrame);
            AddSparse(record, "dist", snapshot.combatDistance);
            AddSparse(record, "cooldown", snapshot.combatCooldown);
            AddSparse(record, "health", snapshot.combatHealth);
            AddSparse(record, "nHealth", snapshot.combatNormalizedHealth);
            AddSparse(record, "hitFlash", snapshot.combatHitFlash);
            AddSparse(record, "lastDamage", snapshot.combatLastDamage);
            AddSparse(record, "muzzleFlash", snapshot.combatMuzzleFlash);
            AddSparse(record, "impact", snapshot.combatHasImpact);
            AddSparse(record, "visibleSampleIndex", snapshot.combatVisibleSampleIndex);
            AddSparse(record, "shot.hitAgent", snapshot.combatShotHitAgentIndex);
            AddSparse(record, "shot.hitScene", snapshot.combatShotHitScene);
            AddSparse(record, "shot.blockedBySceneBeforeAgent", snapshot.combatShotBlockedBySceneBeforeAgent);
            AddSparse(record, "origin", snapshot.combatWorldOrigin);
            AddSparse(record, "targetPoint", snapshot.combatWorldTarget);
            AddSparse(record, "impactNormal", snapshot.combatWorldImpactNormal);
        }

        if (snapshot.targetAcquisitionAvailable)
        {
            AddSparse(record, "acq.target", snapshot.acquisitionCurrentTargetIndex);
            AddSparse(record, "acq.lastAttacker", snapshot.acquisitionLastAttackerIndex);
            AddSparse(record, "acq.flags", snapshot.acquisitionFlags);
            AddSparse(record, "acq.lastKnownLocal", snapshot.acquisitionLastKnownTargetLocal);
            AddSparse(record, "acq.score", snapshot.acquisitionScore);
            AddSparse(record, "acq.timers", snapshot.acquisitionTimers);
            EmitTargetRejectCounts(record, snapshot.acquisitionRejectCounts0, snapshot.acquisitionRejectCounts1);
        }
        CommitSparse(record);
    }

    private void EmitTargetRejectCounts(CrowdVatAiDebugRecord record, Vector4 rejectCounts0, Vector4 rejectCounts1)
    {
        AddSparse(record, "reject.fov", Mathf.RoundToInt(rejectCounts0.x));
        AddSparse(record, "reject.range", Mathf.RoundToInt(rejectCounts0.y));
        AddSparse(record, "reject.terrain", Mathf.RoundToInt(rejectCounts0.z));
        AddSparse(record, "reject.envSdf", Mathf.RoundToInt(rejectCounts0.w));
        AddSparse(record, "reject.maxCandidates", rejectCounts1.x > 0.5f);
    }

    private void EmitAnimation(CrowdVatAiDebugAgentSnapshot snapshot, string role, float deltaTime)
    {
        CrowdVatAiDebugRecord record = CreateSparseRecord("animation.vat", snapshot.runtimeInstanceIndex, deltaTime);
        AddSparse(record, "role", role);
        AddSparse(record, "available", snapshot.animationAvailable);
        if (snapshot.animationAvailable)
        {
            AddSparse(record, "clip", snapshot.animationCurrentClipIndex);
            AddSparse(record, "nextClip", snapshot.animationNextClipIndex);
            AddSparse(record, "clipTime", snapshot.animationCurrentClipTime);
            AddSparse(record, "nextTime", snapshot.animationNextClipTime);
            AddSparse(record, "blend", snapshot.animationIsBlending);
            AddSparse(record, "transitionElapsed", snapshot.animationTransitionElapsed);
            AddSparse(record, "transitionDuration", snapshot.animationTransitionDuration);
            AddSparse(record, "speedMul", snapshot.animationPlaybackSpeedMultiplier);
        }
        CommitSparse(record);
    }

    private void EmitVisibility(CrowdVatAiDebugAgentSnapshot snapshot, string role, float deltaTime)
    {
        CrowdVatAiDebugRecord record = CreateSparseRecord("visibility.render", snapshot.runtimeInstanceIndex, deltaTime);
        AddSparse(record, "role", role);
        AddSparse(record, "active", snapshot.active);
        AddSparse(record, "dead", snapshot.dead);
        AddSparse(record, "renderScale", snapshot.dead ? 0.0f : snapshot.scale);
        CommitSparse(record);
    }

    private Vector3 TransformAiDebugLocalPoint(Vector3 localPoint)
    {
        return _renderer != null ? _renderer.transform.TransformPoint(localPoint) : localPoint;
    }

    private Vector3 TransformAiDebugLocalVector(Vector3 localVector)
    {
        return _renderer != null ? _renderer.transform.TransformVector(localVector) : localVector;
    }

    private static string GetGpuStageName(uint stageId)
    {
        switch ((CrowdVatAiDebugGpuStageId)stageId)
        {
            case CrowdVatAiDebugGpuStageId.PredictAfter:
                return "predict.after";
            case CrowdVatAiDebugGpuStageId.SolveAfter:
                return "solve.after";
            case CrowdVatAiDebugGpuStageId.GridAfter:
                return "grid.after";
            case CrowdVatAiDebugGpuStageId.TargetAcquisitionAfter:
                return "targetAcq.after";
            case CrowdVatAiDebugGpuStageId.CombatAfter:
                return "combat.after";
            case CrowdVatAiDebugGpuStageId.SpatialQueryAfter:
                return "spatialQuery.after";
            case CrowdVatAiDebugGpuStageId.FinalizeAfter:
                return "finalize.after";
            default:
                return "stage." + stageId.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string GetGpuCandidateReasonText(uint reasonMask)
    {
        if (reasonMask == 0u)
            return "none";

        StringBuilder builder = new StringBuilder(96);
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonRegion, "region");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonDeadButActive, "deadButActive");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonGridOverflow, "gridOverflow");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonInvalidState, "invalidState");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonHighSpeed, "highSpeed");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonCombatTargetInvalid, "combatTargetInvalid");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonAcquisitionTargetInvalid, "acqTargetInvalid");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonRejectSaturated, "rejectSaturated");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonShotBlockedByScene, "shotBlockedByScene");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonScreen, "screen");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonSquad, "squad");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonInactiveMoving, "inactiveMoving");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonNoLineOfSight, "noLineOfSight");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonSpeedSpike, "speedSpike");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonStuck, "stuck");
        AppendGpuCandidateReason(builder, reasonMask, GpuCandidateReasonPositionJump, "positionJump");
        return builder.Length > 0 ? builder.ToString() : "unknown";
    }

    private static void AppendGpuCandidateReason(StringBuilder builder, uint reasonMask, uint flag, string label)
    {
        if ((reasonMask & flag) == 0u)
            return;

        if (builder.Length > 0)
            builder.Append('|');

        builder.Append(label);
    }

    private CrowdVatAiDebugRecord CreateRecord(string provider, int entityId, float deltaTime)
    {
        CrowdVatAiDebugRecord record = new CrowdVatAiDebugRecord();
        int frame = Application.isPlaying ? Time.frameCount : 0;
        float time = Application.isPlaying ? Time.realtimeSinceStartup : 0.0f;
        record.Add("f", frame);
        record.Add("t", time);
        record.Add("dt", Mathf.Max(deltaTime, 0.0f));
        record.Add("p", provider);
        if (entityId >= 0)
            record.Add("id", entityId);
        return record;
    }

    private CrowdVatAiDebugRecord CreateSparseRecord(string provider, int entityId, float deltaTime)
    {
        return CreateRecord(provider, entityId, deltaTime);
    }

    private void AddSparse(CrowdVatAiDebugRecord record, string key, object value)
    {
        string provider = "unknown";
        int entityId = -1;
        IReadOnlyList<KeyValuePair<string, object>> fields = record.Fields;
        for (int index = 0; index < fields.Count; index++)
        {
            if (fields[index].Key == "p")
                provider = fields[index].Value as string ?? provider;
            else if (fields[index].Key == "id" && fields[index].Value is int id)
                entityId = id;
        }

        string stateKey = provider + ":" + entityId.ToString(CultureInfo.InvariantCulture) + ":" + key;
        string stableValue = CrowdVatAiDebugRecord.ToStableString(value);
        if (_lastValues.TryGetValue(stateKey, out string previousValue) && previousValue == stableValue)
            return;

        _lastValues[stateKey] = stableValue;
        record.Add(key, value);
    }

    private void CommitSparse(CrowdVatAiDebugRecord record)
    {
        if (record.Fields.Count <= 4)
            return;

        if (record.Fields.Count == 5 && record.Fields[4].Key == "id")
            return;

        _records.Add(record);
    }

    private float ComputeDeltaTime()
    {
        if (!Application.isPlaying)
            return 0.0f;

        float now = Time.realtimeSinceStartup;
        if (_lastCapturedFrame < 0)
            return Mathf.Max(now - _startTime, 0.0f);

        return Mathf.Max(now - _lastCapturedTime, 0.0f);
    }

    private string BuildMarkdownSummary(string jsonlPath)
    {
        StringBuilder builder = new StringBuilder(2048);
        builder.AppendLine("# 人群系统 AI Debug 录制");
        builder.AppendLine();
        builder.AppendLine("## 基本信息");
        builder.AppendLine();
        builder.AppendLine("- 会话：" + _sessionName);
        builder.AppendLine("- 起始帧：" + _startFrame.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- 记录条数：" + _records.Count.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- 未变化帧数：" + _droppedUnchangedFrames.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- JSONL：" + jsonlPath.Replace("\\", "/"));
        builder.AppendLine();
        builder.AppendLine("## 录制目标");
        builder.AppendLine();
        for (int index = 0; index < _targets.Count; index++)
        {
            CrowdVatAiDebugTarget target = _targets[index];
            if (target.kind == CrowdVatAiDebugTargetKind.GpuDiscovery)
                builder.AppendLine("- GPU 自动发现候选实例");
            else
                builder.AppendLine("- " + target.kind + "：" + target.id.ToString(CultureInfo.InvariantCulture));
        }

        builder.AppendLine();
        builder.AppendLine("## GPU Discovery 设置");
        builder.AppendLine();
        builder.AppendLine("- 模式：" + _discoverySettings.mode);
        builder.AppendLine("- 候选容量：" + _discoverySettings.candidateCapacity.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- 高速阈值：" + _discoverySettings.highSpeedThreshold.ToString("0.####", CultureInfo.InvariantCulture));
        builder.AppendLine("- 速度突变阈值：" + _discoverySettings.speedSpikeThreshold.ToString("0.####", CultureInfo.InvariantCulture));
        builder.AppendLine("- 帧间位移跳变阈值：" + _discoverySettings.positionJumpThreshold.ToString("0.####", CultureInfo.InvariantCulture));
        builder.AppendLine("- 卡住判定帧数：" + _discoverySettings.stuckFrameThreshold.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- 区域中心：" + CrowdVatAiDebugRecord.ToStableString(_discoverySettings.worldCenter));
        builder.AppendLine("- 区域半径：" + _discoverySettings.radius.ToString("0.####", CultureInfo.InvariantCulture));
        builder.AppendLine("- 盒子中心：" + CrowdVatAiDebugRecord.ToStableString(_discoverySettings.worldBoxCenter));
        builder.AppendLine("- 盒子半径：" + CrowdVatAiDebugRecord.ToStableString(_discoverySettings.worldBoxExtents));
        builder.AppendLine("- 屏幕矩形01：" + CrowdVatAiDebugRecord.ToStableString(new Vector4(
            _discoverySettings.screenRect01.xMin,
            _discoverySettings.screenRect01.yMin,
            _discoverySettings.screenRect01.xMax,
            _discoverySettings.screenRect01.yMax)));
        builder.AppendLine("- 小队 ID：" + _discoverySettings.squadId.ToString(CultureInfo.InvariantCulture));

        builder.AppendLine();
        builder.AppendLine("## 异常描述");
        builder.AppendLine();
        builder.AppendLine("### 现象");
        builder.AppendLine(string.IsNullOrWhiteSpace(_phenomenon) ? "未填写。" : _phenomenon);
        builder.AppendLine();
        builder.AppendLine("### 复现步骤");
        builder.AppendLine(string.IsNullOrWhiteSpace(_reproductionSteps) ? "未填写。" : _reproductionSteps);
        builder.AppendLine();
        builder.AppendLine("### 期望表现");
        builder.AppendLine(string.IsNullOrWhiteSpace(_expectedBehavior) ? "未填写。" : _expectedBehavior);
        builder.AppendLine();
        builder.AppendLine("## 给 AI 的分析提示");
        builder.AppendLine();
        builder.AppendLine("请把 JSONL 当作稀疏时间线读取。`p` 表示 provider，`id` 表示 runtime instance 或 squad，字段只在发生变化时出现。优先寻找：");
        builder.AppendLine();
        builder.AppendLine("- `agent.state` 中位置、速度、死亡、所属小队的异常变化。");
        builder.AppendLine("- `gpu.phase` 中 GPU 帧末状态是否和 CPU 意图不一致。");
        builder.AppendLine("- `gpu.discovery` 中 GPU 自动发现的异常候选实例、reason、score 与候选容量是否打满。");
        builder.AppendLine("- `spatial.grid` 中 cell occupant、overflow、neighbor 变化。");
        builder.AppendLine("- `combat.query` 中目标、LOS、开火、命中、血量变化。");
        builder.AppendLine("- `squad.intent` 中命令、目标点、移动速度、alive count 变化。");
        builder.AppendLine("- `gpu.resource` 中关键 buffer 是否未绑定、count 是否异常。");
        builder.AppendLine();
        builder.AppendLine("请先按帧号列出异常字段，再给出最可能的系统阶段和根因。");
        return builder.ToString();
    }
}
