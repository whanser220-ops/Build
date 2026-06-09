using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public enum CrowdVatCodexAgentPlannerMode
{
    LocalRules = 0,
    OnlineLlm = 1,
    OnlineLlmWithLocalFallback = 2,
    CodexCli = 3,
    CodexCliWithLocalFallback = 4
}

[Serializable]
public struct CrowdVatCodexAgentPlannerSettings
{
    [Min(0.0f)]
    public float retreatDistance;
    [Min(0.0f)]
    public float holdDistance;
    [Min(0.0f)]
    public float chargeDistance;
    [Min(0.0f)]
    public float flankOffsetDistance;
    public bool allowRetreat;

    public static CrowdVatCodexAgentPlannerSettings Default => new CrowdVatCodexAgentPlannerSettings
    {
        retreatDistance = 5.0f,
        holdDistance = 11.0f,
        chargeDistance = 26.0f,
        flankOffsetDistance = 3.0f,
        allowRetreat = true
    };
}

[Serializable]
public struct CrowdVatCodexAgentOnlineSettings
{
    public string endpoint;
    public string model;
    public string apiKeyEnvironmentVariable;
    public string modelEnvironmentVariable;
    [Min(1)]
    public int requestTimeoutSeconds;
    [Min(0.0f)]
    public float minRequestInterval;
    [Min(1.0f)]
    public float maxCommandDistanceFromControlledSquad;

    public static CrowdVatCodexAgentOnlineSettings Default => new CrowdVatCodexAgentOnlineSettings
    {
        endpoint = "https://api.openai.com/v1/responses",
        model = "gpt-5-mini",
        apiKeyEnvironmentVariable = "OPENAI_API_KEY",
        modelEnvironmentVariable = "OPENAI_MODEL",
        requestTimeoutSeconds = 4,
        minRequestInterval = 1.5f,
        maxCommandDistanceFromControlledSquad = 80.0f
    };
}

[Serializable]
public struct CrowdVatCodexAgentCliSettings
{
    public string executable;
    public string model;
    [Min(1)]
    public int requestTimeoutSeconds;
    [Min(0.0f)]
    public float minRequestInterval;
    [Min(1.0f)]
    public float maxCommandDistanceFromControlledSquad;

    public static CrowdVatCodexAgentCliSettings Default => new CrowdVatCodexAgentCliSettings
    {
        executable = "codex.cmd",
        model = string.Empty,
        requestTimeoutSeconds = 30,
        minRequestInterval = 12.0f,
        maxCommandDistanceFromControlledSquad = 80.0f
    };
}

[Serializable]
public struct CrowdVatCodexAgentObservation
{
    public int controlledSquadIndex;
    public Vector3 controlledCenter;
    public int targetSquadIndex;
    public Vector3 targetCenter;
    public float targetDistance;
    public bool hasTarget;
}

[Serializable]
public struct CrowdVatCodexAgentPlan
{
    public bool hasPlan;
    public int controlledSquadIndex;
    public int targetSquadIndex;
    public CrowdVatSquadCommandType commandType;
    public Vector3 commandPoint;
    public string reason;
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-110)]
public sealed class CrowdVatCodexAgentController : MonoBehaviour
{
    [Header("Agent")]
    [SerializeField] private string _agentName = "Codex";
    [SerializeField] private CrowdVatSquadController _squadController;
    [SerializeField] private CrowdVatFactionMask _controlledFaction = CrowdVatFactionMask.CampB;
    [SerializeField] private CrowdVatFactionMask _opponentFaction = CrowdVatFactionMask.CampA;

    [Header("Planning")]
    [SerializeField] [Min(0.05f)] private float _thinkInterval = 0.5f;
    [SerializeField] private CrowdVatCodexAgentPlannerMode _plannerMode = CrowdVatCodexAgentPlannerMode.CodexCliWithLocalFallback;
    [SerializeField] private CrowdVatCodexAgentPlannerSettings _plannerSettings = CrowdVatCodexAgentPlannerSettings.Default;

    [Header("Online LLM")]
    [SerializeField] private CrowdVatCodexAgentOnlineSettings _onlineSettings = CrowdVatCodexAgentOnlineSettings.Default;

    [Header("Codex CLI")]
    [SerializeField] private CrowdVatCodexAgentCliSettings _codexCliSettings = CrowdVatCodexAgentCliSettings.Default;

    [Header("Debug")]
    [SerializeField] private bool _showRuntimeHud = true;
    [SerializeField] private Vector2 _hudPosition = new Vector2(18.0f, 86.0f);

    private readonly List<CrowdVatCodexAgentObservation> _observationScratch = new List<CrowdVatCodexAgentObservation>(16);
    private float _nextThinkTime;
    private float _nextOnlineRequestTime;
    private float _nextCodexCliRequestTime;
    private int _lastIssuedPlanCount;
    private bool _onlinePlanPending;
    private bool _codexCliPlanPending;
    private string _lastPlannerStatus = "local";
    private Coroutine _onlinePlannerCoroutine;
    private Coroutine _codexCliPlannerCoroutine;
    private System.Diagnostics.Process _codexCliProcess;
    private CrowdVatCodexAgentPlan _lastPlan;

    public string AgentName => string.IsNullOrWhiteSpace(_agentName) ? "Codex" : _agentName;

    public int LastIssuedPlanCount => _lastIssuedPlanCount;

    public CrowdVatCodexAgentPlan LastPlan => _lastPlan;

    private void OnEnable()
    {
        ResolveReferences();
        _nextThinkTime = 0.0f;
        _nextOnlineRequestTime = 0.0f;
        _nextCodexCliRequestTime = 0.0f;
        _lastIssuedPlanCount = 0;
        _onlinePlanPending = false;
        _codexCliPlanPending = false;
        _lastPlannerStatus = "local";
        _lastPlan = default;
    }
    
    private void OnDisable()
    {
        if (_onlinePlannerCoroutine != null)
        {
            StopCoroutine(_onlinePlannerCoroutine);
            _onlinePlannerCoroutine = null;
        }

        _onlinePlanPending = false;

        if (_codexCliPlannerCoroutine != null)
        {
            StopCoroutine(_codexCliPlannerCoroutine);
            _codexCliPlannerCoroutine = null;
        }

        _codexCliPlanPending = false;
        StopCodexCliProcess();
    }

    private void OnValidate()
    {
        _thinkInterval = Mathf.Max(0.05f, _thinkInterval);
        _plannerSettings.retreatDistance = Mathf.Max(0.0f, _plannerSettings.retreatDistance);
        _plannerSettings.holdDistance = Mathf.Max(_plannerSettings.retreatDistance, _plannerSettings.holdDistance);
        _plannerSettings.chargeDistance = Mathf.Max(_plannerSettings.holdDistance, _plannerSettings.chargeDistance);
        _plannerSettings.flankOffsetDistance = Mathf.Max(0.0f, _plannerSettings.flankOffsetDistance);
        if (string.IsNullOrWhiteSpace(_onlineSettings.endpoint))
            _onlineSettings.endpoint = CrowdVatCodexAgentOnlineSettings.Default.endpoint;
        if (string.IsNullOrWhiteSpace(_onlineSettings.model))
            _onlineSettings.model = CrowdVatCodexAgentOnlineSettings.Default.model;
        if (string.IsNullOrWhiteSpace(_onlineSettings.apiKeyEnvironmentVariable))
            _onlineSettings.apiKeyEnvironmentVariable = CrowdVatCodexAgentOnlineSettings.Default.apiKeyEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(_onlineSettings.modelEnvironmentVariable))
            _onlineSettings.modelEnvironmentVariable = CrowdVatCodexAgentOnlineSettings.Default.modelEnvironmentVariable;
        _onlineSettings.requestTimeoutSeconds = Mathf.Max(1, _onlineSettings.requestTimeoutSeconds);
        _onlineSettings.minRequestInterval = Mathf.Max(0.0f, _onlineSettings.minRequestInterval);
        _onlineSettings.maxCommandDistanceFromControlledSquad = Mathf.Max(1.0f, _onlineSettings.maxCommandDistanceFromControlledSquad);

        if (string.IsNullOrWhiteSpace(_codexCliSettings.executable))
            _codexCliSettings.executable = CrowdVatCodexAgentCliSettings.Default.executable;
        _codexCliSettings.requestTimeoutSeconds = Mathf.Max(1, _codexCliSettings.requestTimeoutSeconds);
        _codexCliSettings.minRequestInterval = Mathf.Max(0.0f, _codexCliSettings.minRequestInterval);
        _codexCliSettings.maxCommandDistanceFromControlledSquad = Mathf.Max(1.0f, _codexCliSettings.maxCommandDistanceFromControlledSquad);
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        ResolveReferences();
        if (_squadController == null || Time.time < _nextThinkTime)
            return;

        _nextThinkTime = Time.time + _thinkInterval;
        TickAgent();
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !_showRuntimeHud)
            return;

        string lastLine = _lastPlan.hasPlan
            ? $"{_lastPlan.commandType} B#{_lastPlan.controlledSquadIndex} -> A#{_lastPlan.targetSquadIndex} ({_lastPlan.reason})"
            : "waiting for squads";
        GUI.Label(
            new Rect(_hudPosition.x, _hudPosition.y, 620.0f, 60.0f),
            $"{AgentName} Agent: {_plannerMode} [{_lastPlannerStatus}] plans {_lastIssuedPlanCount}\n{lastLine}");
    }

    private void ResolveReferences()
    {
        if (_squadController != null)
            return;

        _squadController = GetComponent<CrowdVatSquadController>();
        if (_squadController == null)
            _squadController = GetComponentInParent<CrowdVatSquadController>();
        if (_squadController == null)
            _squadController = FindFirstObjectByType<CrowdVatSquadController>();
    }

    private void TickAgent()
    {
        _observationScratch.Clear();
        CollectObservations(_observationScratch);
        if (_observationScratch.Count == 0)
        {
            _lastIssuedPlanCount = 0;
            _lastPlannerStatus = "no_observations";
            return;
        }

        if (_plannerMode == CrowdVatCodexAgentPlannerMode.LocalRules)
        {
            ApplyLocalPlans(_observationScratch, "local");
            return;
        }

        if (IsOnlinePlannerMode(_plannerMode))
        {
            TryStartOnlinePlanner(_observationScratch);

            if (_plannerMode == CrowdVatCodexAgentPlannerMode.OnlineLlmWithLocalFallback)
                ApplyLocalPlans(_observationScratch, _onlinePlanPending ? "online_pending_local_fallback" : _lastPlannerStatus);
            return;
        }

        if (IsCodexCliPlannerMode(_plannerMode))
        {
            TryStartCodexCliPlanner(_observationScratch);

            if (_plannerMode == CrowdVatCodexAgentPlannerMode.CodexCliWithLocalFallback)
                ApplyLocalPlans(_observationScratch, _codexCliPlanPending ? "codex_cli_pending_local_fallback" : _lastPlannerStatus);
        }
    }

    private void CollectObservations(List<CrowdVatCodexAgentObservation> observations)
    {
        int squadCount = _squadController.SquadCount;
        for (int squadIndex = 0; squadIndex < squadCount; squadIndex++)
        {
            if (!TryBuildObservation(squadIndex, out CrowdVatCodexAgentObservation observation))
                continue;

            observations.Add(observation);
        }
    }

    private static bool IsOnlinePlannerMode(CrowdVatCodexAgentPlannerMode mode)
    {
        return mode == CrowdVatCodexAgentPlannerMode.OnlineLlm ||
            mode == CrowdVatCodexAgentPlannerMode.OnlineLlmWithLocalFallback;
    }

    private static bool IsCodexCliPlannerMode(CrowdVatCodexAgentPlannerMode mode)
    {
        return mode == CrowdVatCodexAgentPlannerMode.CodexCli ||
            mode == CrowdVatCodexAgentPlannerMode.CodexCliWithLocalFallback;
    }

    private int ApplyLocalPlans(IReadOnlyList<CrowdVatCodexAgentObservation> observations, string plannerStatus)
    {
        _lastIssuedPlanCount = 0;
        _lastPlannerStatus = plannerStatus;
        for (int index = 0; index < observations.Count; index++)
        {
            CrowdVatCodexAgentObservation observation = observations[index];
            CrowdVatCodexAgentPlan plan = BuildPlan(observation, _plannerSettings);
            if (!TryApplyPlan(plan, observation.controlledCenter, float.PositiveInfinity))
                continue;

            _lastIssuedPlanCount++;
        }

        return _lastIssuedPlanCount;
    }

    private void TryStartOnlinePlanner(IReadOnlyList<CrowdVatCodexAgentObservation> observations)
    {
        if (_onlinePlanPending || Time.unscaledTime < _nextOnlineRequestTime)
            return;

        string apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _lastPlannerStatus = "missing_api_key";
            return;
        }

        CrowdVatCodexAgentObservation[] observationSnapshot = new CrowdVatCodexAgentObservation[observations.Count];
        for (int index = 0; index < observations.Count; index++)
            observationSnapshot[index] = observations[index];

        _onlinePlanPending = true;
        _lastPlannerStatus = "online_pending";
        _nextOnlineRequestTime = Time.unscaledTime + Mathf.Max(_thinkInterval, _onlineSettings.minRequestInterval);
        _onlinePlannerCoroutine = StartCoroutine(RequestOnlinePlans(observationSnapshot, apiKey));
    }

    private IEnumerator RequestOnlinePlans(CrowdVatCodexAgentObservation[] observations, string apiKey)
    {
        string requestJson = BuildOnlinePlannerRequestJson(observations, _plannerSettings, _onlineSettings, AgentName);
        byte[] body = Encoding.UTF8.GetBytes(requestJson);

        using (UnityWebRequest request = new UnityWebRequest(ResolveEndpoint(), UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, _onlineSettings.requestTimeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            _onlinePlanPending = false;
            _onlinePlannerCoroutine = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                _lastPlannerStatus = $"online_error:{request.responseCode}";
                yield break;
            }

            string responseJson = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (!TryExtractOpenAiOutputText(responseJson, out string plannerJson) ||
                !TryParseOnlinePlanBatchJson(plannerJson, out CrowdVatCodexAgentPlan[] plans))
            {
                _lastPlannerStatus = "online_parse_failed";
                yield break;
            }

            int appliedCount = ApplyOnlinePlans(plans, observations);
            _lastIssuedPlanCount = appliedCount;
            _lastPlannerStatus = appliedCount > 0 ? "online_applied" : "online_no_valid_plan";
        }
    }

    private void TryStartCodexCliPlanner(IReadOnlyList<CrowdVatCodexAgentObservation> observations)
    {
        if (_codexCliPlanPending || Time.unscaledTime < _nextCodexCliRequestTime)
            return;

        CrowdVatCodexAgentObservation[] observationSnapshot = new CrowdVatCodexAgentObservation[observations.Count];
        for (int index = 0; index < observations.Count; index++)
            observationSnapshot[index] = observations[index];

        _codexCliPlanPending = true;
        _lastPlannerStatus = "codex_cli_pending";
        _nextCodexCliRequestTime = Time.unscaledTime + Mathf.Max(_thinkInterval, _codexCliSettings.minRequestInterval);
        _codexCliPlannerCoroutine = StartCoroutine(RequestCodexCliPlans(observationSnapshot));
    }

    private IEnumerator RequestCodexCliPlans(CrowdVatCodexAgentObservation[] observations)
    {
        string tempDirectory = ResolveCodexCliTempDirectory();
        string schemaPath = System.IO.Path.Combine(tempDirectory, "crowd_vat_codex_plan.schema.json");
        string outputPath = System.IO.Path.Combine(tempDirectory, $"crowd_vat_codex_plan_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json");
        string prompt = BuildCodexCliPlannerPrompt(observations, _plannerSettings, AgentName);
        StringBuilder stdoutBuilder = new StringBuilder(1024);
        StringBuilder stderrBuilder = new StringBuilder(1024);
        object outputLock = new object();

        System.Diagnostics.Process process = null;
        bool startFailed = false;
        try
        {
            System.IO.Directory.CreateDirectory(tempDirectory);
            System.IO.File.WriteAllText(schemaPath, BuildCodexCliOutputSchemaJson(), Encoding.UTF8);

            process = CreateCodexCliProcess(schemaPath, outputPath);
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data == null)
                    return;

                lock (outputLock)
                    stdoutBuilder.AppendLine(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data == null)
                    return;

                lock (outputLock)
                    stderrBuilder.AppendLine(args.Data);
            };
            _codexCliProcess = process;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Write(prompt);
            process.StandardInput.Close();
        }
        catch (Exception exception)
        {
            _lastPlannerStatus = $"codex_cli_start_failed:{exception.GetType().Name}";
            startFailed = true;
        }

        if (startFailed)
        {
            FinishCodexCliPlanner(process);
            yield break;
        }

        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(1, _codexCliSettings.requestTimeoutSeconds);
        while (!process.HasExited && Time.realtimeSinceStartup < timeoutAt)
            yield return null;

        if (!process.HasExited)
        {
            TryKillCodexCliProcess(process);
            _lastPlannerStatus = "codex_cli_timeout";
            FinishCodexCliPlanner(process);
            yield break;
        }

        int exitCode = process.ExitCode;
        try
        {
            process.WaitForExit();
        }
        catch (Exception)
        {
        }

        if (exitCode != 0)
        {
            _lastPlannerStatus = $"codex_cli_error:{exitCode}";
            FinishCodexCliPlanner(process);
            yield break;
        }

        string stdout;
        string stderr;
        lock (outputLock)
        {
            stdout = stdoutBuilder.ToString();
            stderr = stderrBuilder.ToString();
        }

        string plannerJson = ReadCodexCliPlanOutput(outputPath, stdout, stderr);
        if (!TryParseOnlinePlanBatchJson(plannerJson, out CrowdVatCodexAgentPlan[] plans))
        {
            _lastPlannerStatus = "codex_cli_parse_failed";
            FinishCodexCliPlanner(process);
            yield break;
        }

        int appliedCount = ApplyCodexCliPlans(plans, observations);
        _lastIssuedPlanCount = appliedCount;
        _lastPlannerStatus = appliedCount > 0 ? "codex_cli_applied" : "codex_cli_no_valid_plan";
        FinishCodexCliPlanner(process);
    }

    private System.Diagnostics.Process CreateCodexCliProcess(string schemaPath, string outputPath)
    {
        System.Diagnostics.ProcessStartInfo processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ResolveCodexCliExecutable(),
            Arguments = BuildCodexCliArguments(schemaPath, outputPath, ResolveCodexCliWorkingDirectory()),
            WorkingDirectory = ResolveCodexCliWorkingDirectory(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        return new System.Diagnostics.Process
        {
            StartInfo = processInfo,
            EnableRaisingEvents = false
        };
    }

    private int ApplyCodexCliPlans(IReadOnlyList<CrowdVatCodexAgentPlan> plans, CrowdVatCodexAgentObservation[] observations)
    {
        int appliedCount = 0;
        for (int planIndex = 0; planIndex < plans.Count; planIndex++)
        {
            CrowdVatCodexAgentPlan plan = plans[planIndex];
            if (!TryFindObservation(plan.controlledSquadIndex, observations, out CrowdVatCodexAgentObservation observation))
                continue;

            if (!TryApplyPlan(plan, observation.controlledCenter, _codexCliSettings.maxCommandDistanceFromControlledSquad))
                continue;

            appliedCount++;
        }

        return appliedCount;
    }

    private int ApplyOnlinePlans(IReadOnlyList<CrowdVatCodexAgentPlan> plans, CrowdVatCodexAgentObservation[] observations)
    {
        int appliedCount = 0;
        for (int planIndex = 0; planIndex < plans.Count; planIndex++)
        {
            CrowdVatCodexAgentPlan plan = plans[planIndex];
            if (!TryFindObservation(plan.controlledSquadIndex, observations, out CrowdVatCodexAgentObservation observation))
                continue;

            if (!TryApplyPlan(plan, observation.controlledCenter, _onlineSettings.maxCommandDistanceFromControlledSquad))
                continue;

            appliedCount++;
        }

        return appliedCount;
    }

    private bool TryApplyPlan(CrowdVatCodexAgentPlan plan, Vector3 controlledCenter, float maxCommandDistance)
    {
        if (!IsPlanAllowed(plan, controlledCenter, maxCommandDistance))
            return false;

        if (!_squadController.TryCommandMoveTo(plan.controlledSquadIndex, plan.commandPoint, plan.commandType, true))
            return false;

        _lastPlan = plan;
        return true;
    }

    private bool IsPlanAllowed(CrowdVatCodexAgentPlan plan, Vector3 controlledCenter, float maxCommandDistance)
    {
        if (!plan.hasPlan || !IsAllowedCommand(plan.commandType) || !IsFinite(plan.commandPoint))
            return false;

        if (!_squadController.TryGetSquadFactionMask(plan.controlledSquadIndex, out CrowdVatFactionMask factionMask) ||
            (factionMask & _controlledFaction) == CrowdVatFactionMask.None)
        {
            return false;
        }

        Vector3 delta = plan.commandPoint - controlledCenter;
        delta.y = 0.0f;
        return delta.magnitude <= maxCommandDistance;
    }

    private bool TryBuildObservation(int squadIndex, out CrowdVatCodexAgentObservation observation)
    {
        observation = default;
        if (!_squadController.TryGetSquadFactionMask(squadIndex, out CrowdVatFactionMask factionMask) ||
            (factionMask & _controlledFaction) == CrowdVatFactionMask.None)
        {
            return false;
        }

        if (!_squadController.TryGetSquadCenter(squadIndex, out Vector3 controlledCenter))
            return false;

        observation.controlledSquadIndex = squadIndex;
        observation.controlledCenter = controlledCenter;
        observation.targetSquadIndex = -1;
        observation.targetCenter = controlledCenter;
        observation.targetDistance = 0.0f;
        observation.hasTarget = _squadController.TryGetClosestSquad(
            _opponentFaction,
            controlledCenter,
            out observation.targetSquadIndex,
            out observation.targetCenter,
            out observation.targetDistance);
        return true;
    }

    public static CrowdVatCodexAgentPlan BuildPlan(
        CrowdVatCodexAgentObservation observation,
        CrowdVatCodexAgentPlannerSettings settings)
    {
        if (!observation.hasTarget)
        {
            return new CrowdVatCodexAgentPlan
            {
                hasPlan = true,
                controlledSquadIndex = observation.controlledSquadIndex,
                targetSquadIndex = -1,
                commandType = CrowdVatSquadCommandType.Hold,
                commandPoint = observation.controlledCenter,
                reason = "no_target"
            };
        }

        settings.retreatDistance = Mathf.Max(0.0f, settings.retreatDistance);
        settings.holdDistance = Mathf.Max(settings.retreatDistance, settings.holdDistance);
        settings.chargeDistance = Mathf.Max(settings.holdDistance, settings.chargeDistance);
        settings.flankOffsetDistance = Mathf.Max(0.0f, settings.flankOffsetDistance);

        float targetDistance = observation.targetDistance;
        if (targetDistance <= 0.0f)
        {
            Vector3 delta = observation.targetCenter - observation.controlledCenter;
            delta.y = 0.0f;
            targetDistance = delta.magnitude;
        }

        if (settings.allowRetreat && targetDistance < settings.retreatDistance)
            return CreatePlan(observation, CrowdVatSquadCommandType.Retreat, observation.targetCenter, "too_close");

        if (targetDistance <= settings.holdDistance)
            return CreatePlan(observation, CrowdVatSquadCommandType.Hold, observation.targetCenter, "in_firing_range");

        Vector3 attackPoint = ResolveAttackPoint(observation, settings.flankOffsetDistance);
        if (targetDistance >= settings.chargeDistance)
            return CreatePlan(observation, CrowdVatSquadCommandType.Charge, attackPoint, "closing_distance");

        return CreatePlan(observation, CrowdVatSquadCommandType.Advance, attackPoint, "pressure_target");
    }

    private static CrowdVatCodexAgentPlan CreatePlan(
        CrowdVatCodexAgentObservation observation,
        CrowdVatSquadCommandType commandType,
        Vector3 commandPoint,
        string reason)
    {
        return new CrowdVatCodexAgentPlan
        {
            hasPlan = true,
            controlledSquadIndex = observation.controlledSquadIndex,
            targetSquadIndex = observation.targetSquadIndex,
            commandType = commandType,
            commandPoint = commandPoint,
            reason = reason
        };
    }

    private static Vector3 ResolveAttackPoint(CrowdVatCodexAgentObservation observation, float flankOffsetDistance)
    {
        Vector3 toTarget = observation.targetCenter - observation.controlledCenter;
        toTarget.y = 0.0f;
        if (toTarget.sqrMagnitude <= 1e-6f || flankOffsetDistance <= 0.0f)
            return observation.targetCenter;

        Vector3 forward = toTarget.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude <= 1e-6f)
            return observation.targetCenter;

        float side = (observation.controlledSquadIndex & 1) == 0 ? -1.0f : 1.0f;
        return observation.targetCenter + right.normalized * (side * flankOffsetDistance);
    }

    public static bool TryParseOnlinePlanBatchJson(string json, out CrowdVatCodexAgentPlan[] plans)
    {
        plans = Array.Empty<CrowdVatCodexAgentPlan>();
        if (string.IsNullOrWhiteSpace(json))
            return false;

        string normalizedJson = ExtractJsonObject(json);
        if (string.IsNullOrWhiteSpace(normalizedJson))
            return false;

        OnlinePlanBatchDto batch;
        try
        {
            batch = JsonUtility.FromJson<OnlinePlanBatchDto>(normalizedJson);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (batch == null || batch.plans == null)
            return false;

        List<CrowdVatCodexAgentPlan> parsedPlans = new List<CrowdVatCodexAgentPlan>(batch.plans.Length);
        for (int index = 0; index < batch.plans.Length; index++)
        {
            OnlinePlanDto dto = batch.plans[index];
            if (dto == null || !TryParseCommandType(dto.commandType, out CrowdVatSquadCommandType commandType))
                continue;

            parsedPlans.Add(new CrowdVatCodexAgentPlan
            {
                hasPlan = true,
                controlledSquadIndex = dto.controlledSquadIndex,
                targetSquadIndex = dto.targetSquadIndex,
                commandType = commandType,
                commandPoint = new Vector3(dto.commandPoint.x, dto.commandPoint.y, dto.commandPoint.z),
                reason = string.IsNullOrWhiteSpace(dto.reason) ? "online_llm" : dto.reason
            });
        }

        plans = parsedPlans.ToArray();
        return plans.Length > 0;
    }

    private static bool TryParseCommandType(string value, out CrowdVatSquadCommandType commandType)
    {
        commandType = CrowdVatSquadCommandType.None;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Enum.TryParse(value, true, out commandType) && IsAllowedCommand(commandType);
    }

    private static bool IsAllowedCommand(CrowdVatSquadCommandType commandType)
    {
        return commandType == CrowdVatSquadCommandType.Hold ||
            commandType == CrowdVatSquadCommandType.Advance ||
            commandType == CrowdVatSquadCommandType.Charge ||
            commandType == CrowdVatSquadCommandType.Retreat ||
            commandType == CrowdVatSquadCommandType.Regroup;
    }

    private static bool TryFindObservation(
        int controlledSquadIndex,
        CrowdVatCodexAgentObservation[] observations,
        out CrowdVatCodexAgentObservation observation)
    {
        observation = default;
        if (observations == null)
            return false;

        for (int index = 0; index < observations.Length; index++)
        {
            if (observations[index].controlledSquadIndex != controlledSquadIndex)
                continue;

            observation = observations[index];
            return true;
        }

        return false;
    }

    private string ResolveApiKey()
    {
        return Environment.GetEnvironmentVariable(_onlineSettings.apiKeyEnvironmentVariable);
    }

    private string ResolveModel()
    {
        string modelFromEnvironment = Environment.GetEnvironmentVariable(_onlineSettings.modelEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(modelFromEnvironment))
            return modelFromEnvironment;

        return string.IsNullOrWhiteSpace(_onlineSettings.model)
            ? CrowdVatCodexAgentOnlineSettings.Default.model
            : _onlineSettings.model;
    }

    private string ResolveEndpoint()
    {
        return string.IsNullOrWhiteSpace(_onlineSettings.endpoint)
            ? CrowdVatCodexAgentOnlineSettings.Default.endpoint
            : _onlineSettings.endpoint;
    }

    private string ResolveCodexCliExecutable()
    {
        return string.IsNullOrWhiteSpace(_codexCliSettings.executable)
            ? CrowdVatCodexAgentCliSettings.Default.executable
            : _codexCliSettings.executable;
    }

    private static string ResolveCodexCliWorkingDirectory()
    {
        string dataPath = Application.dataPath;
        if (string.IsNullOrWhiteSpace(dataPath))
            return System.IO.Directory.GetCurrentDirectory();

        System.IO.DirectoryInfo dataDirectory = new System.IO.DirectoryInfo(dataPath);
        return dataDirectory.Parent != null ? dataDirectory.Parent.FullName : dataPath;
    }

    private static string ResolveCodexCliTempDirectory()
    {
        string cachePath = Application.temporaryCachePath;
        if (string.IsNullOrWhiteSpace(cachePath))
            cachePath = System.IO.Path.GetTempPath();

        return System.IO.Path.Combine(cachePath, "CrowdVatCodexCliAgent");
    }

    private string BuildCodexCliArguments(string schemaPath, string outputPath, string workingDirectory)
    {
        StringBuilder builder = new StringBuilder(512);
        AppendProcessArgument(builder, "exec");
        AppendProcessArgument(builder, "--cd");
        AppendProcessArgument(builder, workingDirectory);
        AppendProcessArgument(builder, "--sandbox");
        AppendProcessArgument(builder, "read-only");
        AppendProcessArgument(builder, "--ephemeral");

        string resolvedModel = string.IsNullOrWhiteSpace(_codexCliSettings.model) ? string.Empty : _codexCliSettings.model.Trim();
        if (!string.IsNullOrEmpty(resolvedModel))
        {
            AppendProcessArgument(builder, "--model");
            AppendProcessArgument(builder, resolvedModel);
        }

        AppendProcessArgument(builder, "--output-schema");
        AppendProcessArgument(builder, schemaPath);
        AppendProcessArgument(builder, "--output-last-message");
        AppendProcessArgument(builder, outputPath);
        AppendProcessArgument(builder, "-");
        return builder.ToString();
    }

    private static string ReadCodexCliPlanOutput(string outputPath, string stdout, string stderr)
    {
        if (!string.IsNullOrWhiteSpace(outputPath) && System.IO.File.Exists(outputPath))
            return System.IO.File.ReadAllText(outputPath, Encoding.UTF8);

        if (!string.IsNullOrWhiteSpace(stdout))
            return stdout;

        return stderr ?? string.Empty;
    }

    private void FinishCodexCliPlanner(System.Diagnostics.Process process)
    {
        _codexCliPlanPending = false;
        _codexCliPlannerCoroutine = null;

        if (_codexCliProcess == process)
            _codexCliProcess = null;

        if (process != null)
            process.Dispose();
    }

    private void StopCodexCliProcess()
    {
        System.Diagnostics.Process process = _codexCliProcess;
        _codexCliProcess = null;
        if (process == null)
            return;

        TryKillCodexCliProcess(process);
        process.Dispose();
    }

    private static void TryKillCodexCliProcess(System.Diagnostics.Process process)
    {
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (Exception)
        {
        }
    }

    private string BuildOnlinePlannerRequestJson(
        CrowdVatCodexAgentObservation[] observations,
        CrowdVatCodexAgentPlannerSettings plannerSettings,
        CrowdVatCodexAgentOnlineSettings onlineSettings,
        string agentName)
    {
        string inputJson = BuildOnlinePlannerInputJson(observations, plannerSettings, agentName);
        string systemPrompt =
            "You are Codex controlling CampB squads in a Unity crowd battle. " +
            "Return JSON only. Produce one plan per controlled squad. " +
            "Valid commandType values are Hold, Advance, Charge, Retreat, Regroup. " +
            "Use commandPoint as world-space coordinates. Do not invent squad ids. " +
            "Keep plans conservative and playable.";

        StringBuilder builder = new StringBuilder(4096);
        builder.Append('{');
        AppendJsonProperty(builder, "model", ResolveModel());
        builder.Append(",\"input\":[");
        AppendInputMessage(builder, "system", systemPrompt);
        builder.Append(',');
        AppendInputMessage(builder, "user", inputJson);
        builder.Append(']');
        builder.Append(",\"text\":{\"format\":");
        AppendPlanJsonSchema(builder);
        builder.Append('}');
        builder.Append(",\"max_output_tokens\":900");
        builder.Append(",\"store\":false");
        builder.Append('}');
        return builder.ToString();
    }

    private static string BuildOnlinePlannerInputJson(
        CrowdVatCodexAgentObservation[] observations,
        CrowdVatCodexAgentPlannerSettings settings,
        string agentName)
    {
        StringBuilder builder = new StringBuilder(2048);
        builder.Append('{');
        AppendJsonProperty(builder, "agent", agentName);
        builder.Append(",\"controlledFaction\":\"CampB\"");
        builder.Append(",\"opponentFaction\":\"CampA\"");
        builder.Append(",\"settings\":{");
        AppendJsonProperty(builder, "retreatDistance", settings.retreatDistance);
        AppendJsonProperty(builder, "holdDistance", settings.holdDistance);
        AppendJsonProperty(builder, "chargeDistance", settings.chargeDistance);
        AppendJsonProperty(builder, "flankOffsetDistance", settings.flankOffsetDistance);
        builder.Append(",\"allowRetreat\":").Append(settings.allowRetreat ? "true" : "false");
        builder.Append('}');
        builder.Append(",\"observations\":[");
        if (observations != null)
        {
            for (int index = 0; index < observations.Length; index++)
            {
                if (index > 0)
                    builder.Append(',');

                AppendObservation(builder, observations[index]);
            }
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static string BuildCodexCliPlannerPrompt(
        CrowdVatCodexAgentObservation[] observations,
        CrowdVatCodexAgentPlannerSettings settings,
        string agentName)
    {
        string inputJson = BuildOnlinePlannerInputJson(observations, settings, agentName);
        StringBuilder builder = new StringBuilder(3072);
        builder.AppendLine("You are the Codex CLI agent controlling CampB squads in a running Unity crowd battle.");
        builder.AppendLine("This is a runtime tactical decision, not a code editing task.");
        builder.AppendLine("Do not edit files. Do not run shell commands. Do not inspect the repository.");
        builder.AppendLine("Use only the JSON observation below and return one conservative plan per controlled squad.");
        builder.AppendLine("Valid commandType values are Hold, Advance, Charge, Retreat, Regroup.");
        builder.AppendLine("Use commandPoint as world-space coordinates and do not invent squad ids.");
        builder.AppendLine("Return only JSON that matches the output schema.");
        builder.AppendLine();
        builder.Append(inputJson);
        return builder.ToString();
    }

    public static string BuildCodexCliOutputSchemaJson()
    {
        StringBuilder builder = new StringBuilder(2048);
        AppendPlanJsonSchemaObject(builder);
        return builder.ToString();
    }

    private static void AppendObservation(StringBuilder builder, CrowdVatCodexAgentObservation observation)
    {
        builder.Append('{');
        AppendJsonProperty(builder, "controlledSquadIndex", observation.controlledSquadIndex);
        builder.Append(",\"controlledCenter\":");
        AppendVector3(builder, observation.controlledCenter);
        AppendJsonProperty(builder, "targetSquadIndex", observation.targetSquadIndex);
        builder.Append(",\"targetCenter\":");
        AppendVector3(builder, observation.targetCenter);
        AppendJsonProperty(builder, "targetDistance", observation.targetDistance);
        builder.Append(",\"hasTarget\":").Append(observation.hasTarget ? "true" : "false");
        builder.Append('}');
    }

    private static void AppendInputMessage(StringBuilder builder, string role, string content)
    {
        builder.Append("{\"role\":\"");
        builder.Append(JsonEscape(role));
        builder.Append("\",\"content\":\"");
        builder.Append(JsonEscape(content));
        builder.Append("\"}");
    }

    private static void AppendPlanJsonSchema(StringBuilder builder)
    {
        builder.Append("{\"type\":\"json_schema\",\"name\":\"crowd_vat_codex_plan_batch\",\"strict\":true,\"schema\":");
        AppendPlanJsonSchemaObject(builder);
        builder.Append('}');
    }

    private static void AppendPlanJsonSchemaObject(StringBuilder builder)
    {
        builder.Append('{');
        builder.Append("\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"plans\"],\"properties\":{");
        builder.Append("\"plans\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[");
        builder.Append("\"controlledSquadIndex\",\"targetSquadIndex\",\"commandType\",\"commandPoint\",\"reason\"],\"properties\":{");
        builder.Append("\"controlledSquadIndex\":{\"type\":\"integer\"},");
        builder.Append("\"targetSquadIndex\":{\"type\":\"integer\"},");
        builder.Append("\"commandType\":{\"type\":\"string\",\"enum\":[\"Hold\",\"Advance\",\"Charge\",\"Retreat\",\"Regroup\"]},");
        builder.Append("\"commandPoint\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"x\",\"y\",\"z\"],\"properties\":{");
        builder.Append("\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}}},");
        builder.Append("\"reason\":{\"type\":\"string\"}");
        builder.Append("}}}}}}");
    }

    private static bool TryExtractOpenAiOutputText(string responseJson, out string outputText)
    {
        outputText = string.Empty;
        if (string.IsNullOrWhiteSpace(responseJson))
            return false;

        OpenAiResponseEnvelope response;
        try
        {
            response = JsonUtility.FromJson<OpenAiResponseEnvelope>(responseJson);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (response == null)
            return false;

        if (!string.IsNullOrWhiteSpace(response.output_text))
        {
            outputText = response.output_text;
            return true;
        }

        if (response.output == null)
            return false;

        for (int outputIndex = 0; outputIndex < response.output.Length; outputIndex++)
        {
            OpenAiOutputItem item = response.output[outputIndex];
            if (item == null || item.content == null)
                continue;

            for (int contentIndex = 0; contentIndex < item.content.Length; contentIndex++)
            {
                OpenAiContentItem content = item.content[contentIndex];
                if (content == null || string.IsNullOrWhiteSpace(content.text))
                    continue;

                outputText = content.text;
                return true;
            }
        }

        return false;
    }

    private static void AppendVector3(StringBuilder builder, Vector3 value)
    {
        builder.Append('{');
        AppendJsonProperty(builder, "x", value.x);
        AppendJsonProperty(builder, "y", value.y);
        AppendJsonProperty(builder, "z", value.z);
        builder.Append('}');
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, string value)
    {
        if (builder.Length > 0 && builder[builder.Length - 1] != '{' && builder[builder.Length - 1] != '[')
            builder.Append(',');

        builder.Append('\"');
        builder.Append(JsonEscape(name));
        builder.Append("\":\"");
        builder.Append(JsonEscape(value ?? string.Empty));
        builder.Append('\"');
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, int value)
    {
        if (builder.Length > 0 && builder[builder.Length - 1] != '{' && builder[builder.Length - 1] != '[')
            builder.Append(',');

        builder.Append('\"');
        builder.Append(JsonEscape(name));
        builder.Append("\":");
        builder.Append(value);
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, float value)
    {
        if (builder.Length > 0 && builder[builder.Length - 1] != '{' && builder[builder.Length - 1] != '[')
            builder.Append(',');

        builder.Append('\"');
        builder.Append(JsonEscape(name));
        builder.Append("\":");
        builder.Append(value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AppendProcessArgument(StringBuilder builder, string argument)
    {
        if (builder.Length > 0)
            builder.Append(' ');

        builder.Append(QuoteProcessArgument(argument));
    }

    private static string QuoteProcessArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return "\"\"";

        bool requiresQuotes = false;
        for (int index = 0; index < argument.Length; index++)
        {
            char character = argument[index];
            if (char.IsWhiteSpace(character) || character == '"')
            {
                requiresQuotes = true;
                break;
            }
        }

        if (!requiresQuotes)
            return argument;

        StringBuilder builder = new StringBuilder(argument.Length + 8);
        builder.Append('"');
        for (int index = 0; index < argument.Length; index++)
        {
            char character = argument[index];
            if (character == '"')
                builder.Append("\\\"");
            else
                builder.Append(character);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string JsonEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder builder = new StringBuilder(value.Length + 16);
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

        return builder.ToString();
    }

    private static string ExtractJsonObject(string value)
    {
        int startIndex = value.IndexOf('{');
        int endIndex = value.LastIndexOf('}');
        if (startIndex < 0 || endIndex < startIndex)
            return string.Empty;

        return value.Substring(startIndex, endIndex - startIndex + 1);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Serializable]
    private sealed class OpenAiResponseEnvelope
    {
        public string output_text;
        public OpenAiOutputItem[] output;
    }

    [Serializable]
    private sealed class OpenAiOutputItem
    {
        public OpenAiContentItem[] content;
    }

    [Serializable]
    private sealed class OpenAiContentItem
    {
        public string text;
    }

    [Serializable]
    private sealed class OnlinePlanBatchDto
    {
        public OnlinePlanDto[] plans;
    }

    [Serializable]
    private sealed class OnlinePlanDto
    {
        public int controlledSquadIndex;
        public int targetSquadIndex;
        public string commandType;
        public OnlineVector3Dto commandPoint;
        public string reason;
    }

    [Serializable]
    private struct OnlineVector3Dto
    {
        public float x;
        public float y;
        public float z;
    }
}
