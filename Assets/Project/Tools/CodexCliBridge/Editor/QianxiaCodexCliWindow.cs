#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class QianxiaCodexCliWindow : EditorWindow
{
    private const string WindowTitle = "Codex CLI Client";
    private const string MenuRoot = "Tools/Qianxia/Codex CLI";
    private const string CodexCommandPrefKey = "Qianxia.CodexCli.Command";
    private const string CodexScriptPathPrefKey = "Qianxia.CodexCli.ScriptPath";
    private const string SandboxPrefKey = "Qianxia.CodexCli.Sandbox";
    private const string ApprovalPrefKey = "Qianxia.CodexCli.Approval";
    private const string ModelPrefKey = "Qianxia.CodexCli.Model";
    private const string ProfilePrefKey = "Qianxia.CodexCli.Profile";
    private const string ExtraArgsPrefKey = "Qianxia.CodexCli.ExtraArgs";
    private const string TimeoutPrefKey = "Qianxia.CodexCli.TimeoutMinutes";
    private const string IncludeSelectionPrefKey = "Qianxia.CodexCli.IncludeSelection";
    private const string AdditionalContextPrefKey = "Qianxia.CodexCli.AdditionalContext";
    private const string DefaultQuestion = "请分析当前 Unity 项目中我描述的问题，先说明你会检查哪些证据，再给出结论和下一步建议。";

    [SerializeField] private string _codexCommand = string.Empty;
    [SerializeField] private string _codexScriptPath = string.Empty;
    [SerializeField] private string _sandboxMode = "danger-full-access";
    [SerializeField] private string _approvalPolicy = "never";
    [SerializeField] private string _model = string.Empty;
    [SerializeField] private string _profile = string.Empty;
    [SerializeField] private string _extraArgs = string.Empty;
    [SerializeField] private int _timeoutMinutes = 45;
    [SerializeField] private bool _includeSelection = true;
    [SerializeField] private string _question = DefaultQuestion;
    [SerializeField] private string _additionalContext = string.Empty;
    [SerializeField] private string _statusMessage = "Ready.";
    [SerializeField] private string _lastAnswer = string.Empty;
    [SerializeField] private string _lastStdout = string.Empty;
    [SerializeField] private string _lastStderr = string.Empty;
    [SerializeField] private string _lastCommandLine = string.Empty;
    [SerializeField] private bool _showAdvanced;
    [SerializeField] private bool _showLogs;
    [SerializeField] private Vector2 _scrollPosition;

    private readonly object _processLock = new object();
    private Process _runningProcess;
    private bool _isRunning;

    [MenuItem(MenuRoot + "/Client")]
    public static void Open()
    {
        QianxiaCodexCliWindow window = GetWindow<QianxiaCodexCliWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(720.0f, 560.0f);
        window.Show();
    }

    [MenuItem(MenuRoot + "/Analyze Selection")]
    public static void AnalyzeSelection()
    {
        OpenWithPrompt(DefaultQuestion, "Selection Analysis", true);
    }

    public static void OpenWithPrompt(string prompt, string label = "Unity Request", bool runImmediately = false)
    {
        QianxiaCodexCliWindow window = GetWindow<QianxiaCodexCliWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(720.0f, 560.0f);
        window._question = string.IsNullOrWhiteSpace(prompt) ? DefaultQuestion : prompt;
        window._additionalContext = "Request label: " + label;
        window.Show();
        window.Repaint();
        if (runImmediately)
            EditorApplication.delayCall += window.StartAnalysis;
    }

    private void OnEnable()
    {
        LoadPrefs();
    }

    private void OnDisable()
    {
        SavePrefs();
    }

    private void OnGUI()
    {
        using (EditorGUILayout.ScrollViewScope scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition))
        {
            _scrollPosition = scrollView.scrollPosition;
            DrawQuestion();
            DrawAdvanced();
            DrawActions();
            DrawResult();
        }
    }

    private void DrawQuestion()
    {
        EditorGUILayout.LabelField("Unity Request", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Question");
            _question = EditorGUILayout.TextArea(_question, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 4.0f));

            EditorGUILayout.Space();
            _includeSelection = EditorGUILayout.ToggleLeft("Include current Unity selection context", _includeSelection);

            EditorGUILayout.LabelField("Additional Context");
            _additionalContext = EditorGUILayout.TextArea(_additionalContext, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 3.0f));
        }
    }

    private void DrawAdvanced()
    {
        EditorGUILayout.Space();
        _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Codex CLI Settings", true);
        if (!_showAdvanced)
            return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _codexCommand = EditorGUILayout.TextField("Command", _codexCommand);
            _codexScriptPath = EditorGUILayout.TextField("Codex JS", _codexScriptPath);
            _approvalPolicy = EditorGUILayout.TextField("Approval", _approvalPolicy);
            _sandboxMode = EditorGUILayout.TextField("Sandbox", _sandboxMode);
            _model = EditorGUILayout.TextField("Model", _model);
            _profile = EditorGUILayout.TextField("Profile", _profile);
            _extraArgs = EditorGUILayout.TextField("Extra Args", _extraArgs);
            _timeoutMinutes = EditorGUILayout.IntField("Timeout Minutes", Mathf.Max(1, _timeoutMinutes));

            EditorGUILayout.HelpBox(
                "Pipe mode: Unity starts the bundled Codex CLI as a child process, writes the prompt to stdin, reads stdout/stderr in memory, and does not create request artifacts.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Leave Model empty to use the Codex config. Use the bundled CLI path from ~/.codex/config.toml so newer configured models are supported.",
                MessageType.Info);
        }
    }

    private void DrawActions()
    {
        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_isRunning))
            {
                if (GUILayout.Button("Start Codex Analysis", GUILayout.Height(32.0f)))
                    StartAnalysis();
            }

            using (new EditorGUI.DisabledScope(!_isRunning))
            {
                if (GUILayout.Button("Stop", GUILayout.Height(32.0f), GUILayout.Width(120.0f)))
                    StopRunningProcess();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastAnswer)))
            {
                if (GUILayout.Button("Copy Answer"))
                    EditorGUIUtility.systemCopyBuffer = _lastAnswer;
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastCommandLine)))
            {
                if (GUILayout.Button("Copy Command"))
                    EditorGUIUtility.systemCopyBuffer = _lastCommandLine;
            }
        }

        EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
        if (!string.IsNullOrWhiteSpace(_lastCommandLine))
        {
            EditorGUILayout.LabelField("Command");
            EditorGUILayout.TextField(_lastCommandLine);
        }
    }

    private void DrawResult()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Codex Answer", EditorStyles.boldLabel);
        _lastAnswer = EditorGUILayout.TextArea(_lastAnswer, GUILayout.MinHeight(180.0f));

        EditorGUILayout.Space();
        _showLogs = EditorGUILayout.Foldout(_showLogs, "Process Logs", true);
        if (!_showLogs)
            return;

        EditorGUILayout.LabelField("stdout");
        _lastStdout = EditorGUILayout.TextArea(_lastStdout, GUILayout.MinHeight(120.0f));
        EditorGUILayout.LabelField("stderr");
        _lastStderr = EditorGUILayout.TextArea(_lastStderr, GUILayout.MinHeight(80.0f));
    }

    private void StartAnalysis()
    {
        SavePrefs();

        if (string.IsNullOrWhiteSpace(_question))
        {
            _statusMessage = "Question is empty.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_codexCommand))
        {
            _statusMessage = "Codex command is empty.";
            return;
        }

        string codexScriptPath = ResolveCodexScriptPath();
        if (!string.IsNullOrWhiteSpace(codexScriptPath) && !File.Exists(codexScriptPath))
        {
            _statusMessage = "Codex JS does not exist: " + codexScriptPath;
            return;
        }

        string projectRoot = GetProjectRoot();
        string prompt = BuildPrompt(projectRoot);

        _isRunning = true;
        _lastAnswer = string.Empty;
        _lastStdout = string.Empty;
        _lastStderr = string.Empty;
        _statusMessage = "Starting Codex CLI through stdin/stdout pipes...";

        CodexRunSpec spec = new CodexRunSpec(
            _codexCommand,
            BuildCodexArguments(projectRoot, codexScriptPath),
            projectRoot,
            prompt,
            Mathf.Max(1, _timeoutMinutes) * 60 * 1000);

        _lastCommandLine = spec.CommandLine;
        ThreadPool.QueueUserWorkItem(_ => RunCodexOnWorker(spec));
        Repaint();
    }

    private string BuildPrompt(string projectRoot)
    {
        StringBuilder builder = new StringBuilder(4096);
        builder.AppendLine("# Unity Codex Request");
        builder.AppendLine();
        builder.AppendLine("你是通过 Unity Editor 作为客户端启动的 Codex CLI。请分析 Unity 传入的问题，必要时读取仓库文件并运行只读或低风险验证命令。");
        builder.AppendLine("回答请使用中文，先给结论，再给证据路径和建议。不要假设已经有用户在终端里手动执行过命令。");
        builder.AppendLine();
        builder.AppendLine("## User Question");
        builder.AppendLine(_question.Trim());
        builder.AppendLine();
        builder.AppendLine("## Unity Context");
        builder.AppendLine("- Project root: " + projectRoot);
        builder.AppendLine("- Unity version: " + Application.unityVersion);
        builder.AppendLine("- Active scene: " + EditorSceneManager.GetActiveScene().path);
        builder.AppendLine("- Build target: " + EditorUserBuildSettings.activeBuildTarget);
        builder.AppendLine("- Generated at UTC: " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        builder.AppendLine();

        if (_includeSelection)
        {
            builder.AppendLine("## Current Selection");
            builder.Append(BuildSelectionContext());
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(_additionalContext))
        {
            builder.AppendLine("## Additional Context");
            builder.AppendLine(_additionalContext.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("## Available Local Query Tools");
        builder.AppendLine("- RenderDoc post-capture DB materialize: `python tools/renderdoc-db/rdc_capture_postprocess.py --capture \"<capture.rdc>\" --prefab-signatures \"<capture-folder>/unity_prefab_signatures.sqlite\" --json --pretty`");
        builder.AppendLine("- RenderDoc fact DB query: `tools/renderdoc-cli/rdoc-agent.cmd db instance-cost --json --pretty`");
        builder.AppendLine("- Use this database query path when the question asks about imported RenderDoc model/instance GPU cost.");
        builder.AppendLine("- If the user asks about a Unity prefab name such as `P_OakTree_01_Summer`, ensure the capture was materialized with `rdc_capture_postprocess.py`, then query GPU cost with `db instance-cost --prefab-name P_OakTree_01_Summer`.");
        builder.AppendLine("- `--prefab-name` must resolve through the fact database `prefab` and `prefab_model` tables before aggregating RenderDoc model timing.");
        builder.AppendLine("- Do not invent LOD model names. Only run per-LOD follow-up queries for model names that appeared in the database result.");
        builder.AppendLine();
        builder.AppendLine("## Transport");
        builder.AppendLine("- Unity sends this request through Codex CLI stdin and receives stdout/stderr through pipes.");
        builder.AppendLine("- Do not rely on request artifacts being present.");
        builder.AppendLine();
        builder.AppendLine("## Expected Output");
        builder.AppendLine("- 简短结论");
        builder.AppendLine("- 使用过的命令或文件路径");
        builder.AppendLine("- 关键证据");
        builder.AppendLine("- 下一步建议");
        return builder.ToString();
    }

    private string BuildSelectionContext()
    {
        UnityEngine.Object[] selection = Selection.objects;
        if (selection == null || selection.Length == 0)
            return "- No active Unity selection.\n";

        StringBuilder builder = new StringBuilder(2048);
        int maxCount = Mathf.Min(selection.Length, 12);
        for (int index = 0; index < maxCount; index++)
        {
            UnityEngine.Object selected = selection[index];
            if (selected == null)
                continue;

            builder.AppendLine("- " + selected.name + " (" + selected.GetType().Name + ")");
            string assetPath = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrWhiteSpace(assetPath))
                builder.AppendLine("  - assetPath: " + assetPath);

            GameObject gameObject = selected as GameObject;
            if (gameObject != null)
                AppendGameObjectContext(builder, gameObject);
        }

        if (selection.Length > maxCount)
            builder.AppendLine("- Selection truncated: " + maxCount + " of " + selection.Length);

        return builder.ToString();
    }

    private static void AppendGameObjectContext(StringBuilder builder, GameObject gameObject)
    {
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
        if (!string.IsNullOrWhiteSpace(prefabPath))
            builder.AppendLine("  - prefabPath: " + prefabPath);

        Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
        builder.AppendLine("  - rendererCount: " + renderers.Length);

        UniqueNameSet meshNames = new UniqueNameSet();
        UniqueNameSet materialNames = new UniqueNameSet();
        UniqueNameSet shaderNames = new UniqueNameSet();
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            Mesh mesh = ResolveMesh(renderer);
            if (mesh != null)
                meshNames.Add(mesh.name);

            Material[] materials = renderer.sharedMaterials;
            if (materials == null)
                continue;

            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                    continue;

                materialNames.Add(material.name);
                if (material.shader != null)
                    shaderNames.Add(material.shader.name);
            }
        }

        AppendNames(builder, "meshNames", meshNames.ToArray());
        AppendNames(builder, "materialNames", materialNames.ToArray());
        AppendNames(builder, "shaderNames", shaderNames.ToArray());
    }

    private string[] BuildCodexArguments(string projectRoot, string codexScriptPath)
    {
        List<string> args = new List<string>(16);
        if (!string.IsNullOrWhiteSpace(codexScriptPath))
            args.Add(codexScriptPath);

        if (!string.IsNullOrWhiteSpace(_approvalPolicy))
        {
            args.Add("--ask-for-approval");
            args.Add(_approvalPolicy.Trim());
        }

        args.Add("exec");
        args.Add("--cd");
        args.Add(projectRoot);
        args.Add("--ephemeral");

        if (!string.IsNullOrWhiteSpace(_sandboxMode))
        {
            args.Add("--sandbox");
            args.Add(_sandboxMode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_model))
        {
            args.Add("--model");
            args.Add(_model.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_profile))
        {
            args.Add("--profile");
            args.Add(_profile.Trim());
        }

        args.Add("--color");
        args.Add("never");

        if (!string.IsNullOrWhiteSpace(_extraArgs))
            args.Add(_extraArgs.Trim());

        args.Add("-");
        return args.ToArray();
    }

    private void RunCodexOnWorker(CodexRunSpec spec)
    {
        int exitCode = -1;
        string stdout = string.Empty;
        string stderr = string.Empty;
        string error = string.Empty;
        bool timedOut = false;

        try
        {
            ProcessStartInfo startInfo = CreateProcessStartInfo(spec);

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();
                lock (_processLock)
                {
                    _runningProcess = process;
                }

                string capturedStdout = string.Empty;
                string capturedStderr = string.Empty;
                Thread stdoutThread = new Thread(() => capturedStdout = process.StandardOutput.ReadToEnd());
                Thread stderrThread = new Thread(() => capturedStderr = process.StandardError.ReadToEnd());
                stdoutThread.Start();
                stderrThread.Start();

                byte[] promptBytes = Encoding.UTF8.GetBytes(spec.Prompt);
                process.StandardInput.BaseStream.Write(promptBytes, 0, promptBytes.Length);
                process.StandardInput.BaseStream.Flush();
                process.StandardInput.Close();

                if (!process.WaitForExit(spec.TimeoutMilliseconds))
                {
                    timedOut = true;
                    TryKill(process);
                }

                stdoutThread.Join(1000);
                stderrThread.Join(1000);
                exitCode = process.HasExited ? process.ExitCode : -1;
                stdout = capturedStdout ?? string.Empty;
                stderr = capturedStderr ?? string.Empty;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            lock (_processLock)
            {
                _runningProcess = null;
            }
        }

        EditorApplication.delayCall += () => ApplyRunResult(exitCode, timedOut, error, stdout, stderr);
    }

    private static ProcessStartInfo CreateProcessStartInfo(CodexRunSpec spec)
    {
        string fileName = spec.Command;
        string arguments = spec.ArgumentsLine;

        if (RequiresCommandShell(spec.Command))
        {
            fileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            arguments = "/d /s /c " + QuoteArgument(spec.CommandLine);
        }

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private static bool RequiresCommandShell(string command)
    {
        string extension = Path.GetExtension(command);
        return string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyRunResult(int exitCode, bool timedOut, string error, string stdout, string stderr)
    {
        _isRunning = false;
        _lastStdout = stdout ?? string.Empty;
        _lastStderr = stderr ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(stdout))
            _lastAnswer = stdout;
        else
            _lastAnswer = stderr ?? string.Empty;

        if (timedOut)
            _statusMessage = "Codex CLI timed out.";
        else if (!string.IsNullOrWhiteSpace(error))
            _statusMessage = "Codex CLI failed: " + error;
        else
            _statusMessage = "Codex CLI finished with exit code " + exitCode + ".";

        Repaint();
    }

    private void StopRunningProcess()
    {
        lock (_processLock)
        {
            if (_runningProcess != null && !_runningProcess.HasExited)
                TryKill(_runningProcess);
        }
        _statusMessage = "Stop requested.";
        Repaint();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (process != null && !process.HasExited)
                process.Kill();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[QianxiaCodexCli] Failed to kill Codex process: " + exception.Message);
        }
    }

    private static Mesh ResolveMesh(Renderer renderer)
    {
        MeshFilter meshFilter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
        if (meshFilter != null)
            return meshFilter.sharedMesh;

        SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
        return skinned != null ? skinned.sharedMesh : null;
    }

    private static void AppendNames(StringBuilder builder, string label, string[] names)
    {
        if (names == null || names.Length == 0)
            return;

        builder.Append("  - ");
        builder.Append(label);
        builder.Append(": ");
        int count = Mathf.Min(names.Length, 12);
        for (int index = 0; index < count; index++)
        {
            if (index > 0)
                builder.Append(", ");
            builder.Append(names[index]);
        }
        if (names.Length > count)
            builder.Append(", ...");
        builder.AppendLine();
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private string ResolveCodexScriptPath()
    {
        if (string.IsNullOrWhiteSpace(_codexScriptPath))
            return string.Empty;

        return Environment.ExpandEnvironmentVariables(_codexScriptPath.Trim());
    }

    private static string GetDefaultCodexCommand()
    {
        string configCommand = ReadBundledCodexPathFromConfig();
        if (!string.IsNullOrWhiteSpace(configCommand) && File.Exists(configCommand))
            return configCommand;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string codexBinRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(codexBinRoot))
        {
            FileInfo newestCodex = null;
            string[] candidates = Directory.GetFiles(codexBinRoot, "codex.exe", SearchOption.AllDirectories);
            for (int index = 0; index < candidates.Length; index++)
            {
                FileInfo candidate = new FileInfo(candidates[index]);
                if (newestCodex == null || candidate.LastWriteTimeUtc > newestCodex.LastWriteTimeUtc)
                    newestCodex = candidate;
            }

            if (newestCodex != null)
                return newestCodex.FullName;
        }

        string scriptPath = GetDefaultCodexScriptPath();
        return File.Exists(scriptPath) ? "node.exe" : "codex.cmd";
    }

    private static string ReadBundledCodexPathFromConfig()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string configPath = Path.Combine(appData, ".codex", "config.toml");
        if (!File.Exists(configPath))
            return string.Empty;

        try
        {
            string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (!line.StartsWith("CODEX_CLI_PATH", StringComparison.Ordinal))
                    continue;

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex < 0)
                    continue;

                string value = line.Substring(equalsIndex + 1).Trim().Trim('"', '\'');
                value = Environment.ExpandEnvironmentVariables(value);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[QianxiaCodexCli] Failed to read Codex config: " + exception.Message);
        }

        return string.Empty;
    }

    private static string GetDefaultCodexScriptPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "bin", "codex.js");
    }

    private static string BuildDisplayCommandLine(string command, string[] args)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(QuoteArgument(command));
        if (args != null)
        {
            for (int index = 0; index < args.Length; index++)
            {
                builder.Append(' ');
                builder.Append(QuoteArgument(args[index]));
            }
        }
        return builder.ToString();
    }

    private static string BuildArgumentsLine(string[] args)
    {
        if (args == null || args.Length == 0)
            return string.Empty;

        StringBuilder builder = new StringBuilder();
        for (int index = 0; index < args.Length; index++)
        {
            if (index > 0)
                builder.Append(' ');
            builder.Append(QuoteArgument(args[index]));
        }
        return builder.ToString();
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private void LoadPrefs()
    {
        _codexCommand = EditorPrefs.GetString(CodexCommandPrefKey, _codexCommand);
        if (string.IsNullOrWhiteSpace(_codexCommand)
            || string.Equals(_codexCommand, "codex.cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_codexCommand, "codex.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_codexCommand, "node.exe", StringComparison.OrdinalIgnoreCase))
        {
            _codexCommand = GetDefaultCodexCommand();
        }
        _codexScriptPath = EditorPrefs.GetString(CodexScriptPathPrefKey, string.Empty);
        if (string.Equals(Path.GetFileName(_codexCommand), "node.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(_codexScriptPath))
                _codexScriptPath = GetDefaultCodexScriptPath();
        }
        else
        {
            _codexScriptPath = string.Empty;
        }
        _sandboxMode = EditorPrefs.GetString(SandboxPrefKey, _sandboxMode);
        if (string.Equals(_sandboxMode, "workspace-write", StringComparison.OrdinalIgnoreCase))
            _sandboxMode = "danger-full-access";
        _approvalPolicy = EditorPrefs.GetString(ApprovalPrefKey, _approvalPolicy);
        _model = EditorPrefs.GetString(ModelPrefKey, _model);
        if (string.Equals(_model, "gpt-5", StringComparison.OrdinalIgnoreCase))
            _model = string.Empty;
        _profile = EditorPrefs.GetString(ProfilePrefKey, _profile);
        _extraArgs = EditorPrefs.GetString(ExtraArgsPrefKey, _extraArgs);
        _timeoutMinutes = EditorPrefs.GetInt(TimeoutPrefKey, _timeoutMinutes);
        _includeSelection = EditorPrefs.GetBool(IncludeSelectionPrefKey, _includeSelection);
        _additionalContext = EditorPrefs.GetString(AdditionalContextPrefKey, _additionalContext);
    }

    private void SavePrefs()
    {
        EditorPrefs.SetString(CodexCommandPrefKey, _codexCommand ?? string.Empty);
        EditorPrefs.SetString(CodexScriptPathPrefKey, _codexScriptPath ?? string.Empty);
        EditorPrefs.SetString(SandboxPrefKey, _sandboxMode ?? string.Empty);
        EditorPrefs.SetString(ApprovalPrefKey, _approvalPolicy ?? string.Empty);
        EditorPrefs.SetString(ModelPrefKey, _model ?? string.Empty);
        EditorPrefs.SetString(ProfilePrefKey, _profile ?? string.Empty);
        EditorPrefs.SetString(ExtraArgsPrefKey, _extraArgs ?? string.Empty);
        EditorPrefs.SetInt(TimeoutPrefKey, Mathf.Max(1, _timeoutMinutes));
        EditorPrefs.SetBool(IncludeSelectionPrefKey, _includeSelection);
        EditorPrefs.SetString(AdditionalContextPrefKey, _additionalContext ?? string.Empty);
    }

    private sealed class CodexRunSpec
    {
        public readonly string CommandLine;
        public readonly string Command;
        public readonly string ArgumentsLine;
        public readonly string WorkingDirectory;
        public readonly string Prompt;
        public readonly int TimeoutMilliseconds;

        public CodexRunSpec(
            string command,
            string[] args,
            string workingDirectory,
            string prompt,
            int timeoutMilliseconds)
        {
            Command = command;
            ArgumentsLine = BuildArgumentsLine(args);
            CommandLine = BuildDisplayCommandLine(command, args);
            WorkingDirectory = workingDirectory;
            Prompt = prompt;
            TimeoutMilliseconds = timeoutMilliseconds;
        }
    }

    private sealed class UniqueNameSet
    {
        private readonly List<string> _names = new List<string>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Add(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string trimmed = value.Trim();
            if (_seen.Add(trimmed))
                _names.Add(trimmed);
        }

        public string[] ToArray()
        {
            _names.Sort(StringComparer.OrdinalIgnoreCase);
            return _names.ToArray();
        }
    }
}
#endif
