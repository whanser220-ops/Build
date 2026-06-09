#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class QianxiaEditorBridgeServer
{
    private const string MenuRoot = "Tools/Qianxia/Editor Bridge";
    private const string AutoStartMenuPath = MenuRoot + "/Auto Start";
    private const string PortPrefKey = "Qianxia.EditorBridge.Port";
    private const string AutoStartPrefKey = "Qianxia.EditorBridge.AutoStart";
    private const int DefaultPort = 8787;
    private const int MinPort = 1024;
    private const int MaxPort = 65535;
    private const int MaxRequestBodyBytes = 1024 * 1024;
    private const int MainThreadRequestTimeoutMs = 30000;
    private const int MaxRequestsPerEditorUpdate = 16;

    private static readonly object QueueLock = new object();
    private static readonly Queue<BridgeWorkItem> PendingRequests = new Queue<BridgeWorkItem>();

    private static HttpListener _listener;
    private static Thread _listenerThread;
    private static string _lastError = string.Empty;

    static QianxiaEditorBridgeServer()
    {
        EditorApplication.update -= PumpMainThreadRequests;
        EditorApplication.update += PumpMainThreadRequests;
        AssemblyReloadEvents.beforeAssemblyReload -= Stop;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.quitting -= Stop;
        EditorApplication.quitting += Stop;

        if (AutoStart)
            EditorApplication.delayCall += Start;
    }

    public static bool IsRunning
    {
        get { return _listener != null && _listener.IsListening; }
    }

    public static int Port
    {
        get { return ReadRequestedPort(); }
        set
        {
            if (IsRunning)
                throw new InvalidOperationException("Stop the editor bridge before changing the port.");

            EditorPrefs.SetInt(PortPrefKey, ClampPort(value));
        }
    }

    public static bool AutoStart
    {
        get { return EditorPrefs.GetBool(AutoStartPrefKey, false); }
        set { EditorPrefs.SetBool(AutoStartPrefKey, value); }
    }

    public static string LocalUrl
    {
        get { return "http://127.0.0.1:" + Port + "/"; }
    }

    public static string LastError
    {
        get { return _lastError; }
    }

    [MenuItem(MenuRoot + "/Open Window")]
    public static void OpenWindow()
    {
        QianxiaEditorBridgeWindow.Open();
    }

    [MenuItem(MenuRoot + "/Start")]
    public static void Start()
    {
        if (IsRunning)
            return;

        _lastError = string.Empty;

        HttpListener listener = new HttpListener();
        string prefix = LocalUrl;

        try
        {
            listener.Prefixes.Add(prefix);
            listener.Start();

            _listener = listener;
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "Qianxia Editor Bridge HTTP"
            };
            _listenerThread.Start();

            Debug.Log("[QianxiaEditorBridge] Listening on " + prefix);
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            try
            {
                listener.Close();
            }
            catch
            {
                // Closing a failed listener is best-effort.
            }

            Debug.LogError("[QianxiaEditorBridge] Failed to start on " + prefix + ": " + exception.Message);
        }
    }

    [MenuItem(MenuRoot + "/Start", true)]
    private static bool ValidateStart()
    {
        return !IsRunning;
    }

    [MenuItem(MenuRoot + "/Stop")]
    public static void Stop()
    {
        HttpListener listener = _listener;
        _listener = null;

        if (listener != null)
        {
            try
            {
                listener.Stop();
                listener.Close();
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
            }
        }

        Thread listenerThread = _listenerThread;
        _listenerThread = null;
        if (listenerThread != null && listenerThread.IsAlive)
            listenerThread.Join(250);

        FlushPendingRequestsAsUnavailable();
    }

    [MenuItem(MenuRoot + "/Stop", true)]
    private static bool ValidateStop()
    {
        return IsRunning;
    }

    [MenuItem(MenuRoot + "/Restart")]
    public static void Restart()
    {
        Stop();
        Start();
    }

    [MenuItem(AutoStartMenuPath)]
    private static void ToggleAutoStart()
    {
        AutoStart = !AutoStart;
        if (AutoStart && !IsRunning)
            Start();
    }

    [MenuItem(AutoStartMenuPath, true)]
    private static bool ValidateToggleAutoStart()
    {
        Menu.SetChecked(AutoStartMenuPath, AutoStart);
        return true;
    }

    public static void StartFromCommandLine()
    {
        Start();
    }

    public static void StopFromCommandLine()
    {
        Stop();
    }

    private static void ListenLoop()
    {
        while (true)
        {
            HttpListener listener = _listener;
            if (listener == null || !listener.IsListening)
                return;

            try
            {
                HttpListenerContext context = listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleContext(context));
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
            }
        }
    }

    private static void HandleContext(HttpListenerContext context)
    {
        if (context == null)
            return;

        try
        {
            if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                WriteResponse(context, new BridgeResponse(204, string.Empty));
                return;
            }

            if (context.Request.ContentLength64 > MaxRequestBodyBytes)
            {
                WriteResponse(context, JsonError(413, "request_too_large", "Request body is larger than 1 MB."));
                return;
            }

            string body = ReadRequestBody(context.Request);
            if (Encoding.UTF8.GetByteCount(body) > MaxRequestBodyBytes)
            {
                WriteResponse(context, JsonError(413, "request_too_large", "Request body is larger than 1 MB."));
                return;
            }

            BridgeWorkItem item = new BridgeWorkItem(new BridgeRequest(
                context.Request.HttpMethod,
                context.Request.Url != null ? context.Request.Url.AbsolutePath : "/",
                context.Request.Url != null ? context.Request.Url.Query : string.Empty,
                body));

            lock (QueueLock)
            {
                PendingRequests.Enqueue(item);
            }

            if (!item.Done.Wait(MainThreadRequestTimeoutMs))
            {
                WriteResponse(context, JsonError(504, "unity_main_thread_timeout", "Unity did not process the request in time."));
                return;
            }

            WriteResponse(context, item.Response ?? JsonError(500, "empty_response", "Unity did not produce a response."));
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            try
            {
                WriteResponse(context, JsonError(500, "bridge_exception", exception.Message));
            }
            catch
            {
                // The client may already be gone.
            }
        }
    }

    private static string ReadRequestBody(HttpListenerRequest request)
    {
        if (request == null || !request.HasEntityBody)
            return string.Empty;

        using (Stream stream = request.InputStream)
        using (StreamReader reader = new StreamReader(stream, request.ContentEncoding ?? Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }

    private static void PumpMainThreadRequests()
    {
        for (int i = 0; i < MaxRequestsPerEditorUpdate; i++)
        {
            BridgeWorkItem item = null;
            lock (QueueLock)
            {
                if (PendingRequests.Count > 0)
                    item = PendingRequests.Dequeue();
            }

            if (item == null)
                return;

            try
            {
                item.Response = HandleRequestOnMainThread(item.Request);
            }
            catch (BridgeRequestException exception)
            {
                item.Response = JsonError(exception.StatusCode, exception.ErrorCode, exception.Message);
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
                item.Response = JsonError(500, "unity_exception", exception.Message);
            }
            finally
            {
                item.Done.Set();
            }
        }
    }

    private static BridgeResponse HandleRequestOnMainThread(BridgeRequest request)
    {
        string method = (request.Method ?? string.Empty).ToUpperInvariant();
        string path = NormalizePath(request.Path);

        if (method == "GET" && (path == "/" || path == "/help"))
            return JsonResponse(BuildHelpJson());

        if (method == "GET" && path == "/ping")
            return JsonResponse("{\"success\":true,\"reply\":\"pong\"}");

        if (method == "GET" && path == "/status")
            return JsonResponse(BuildStatusJson());

        if (method == "GET" && path == "/selection")
            return JsonResponse(BuildSelectionJson());

        if (method != "POST")
            return JsonError(405, "method_not_allowed", "Use GET for /status, /selection, /ping, /help or POST for commands.");

        if (path == "/command")
            return HandleCommand(ParsePayload(request.Body));

        if (path == "/log")
            return HandleCommand(ParseEndpointPayload("log", request.Body));

        if (path == "/execute-menu-item")
            return HandleCommand(ParseEndpointPayload("executeMenuItem", request.Body));

        if (path == "/playmode")
            return HandleCommand(ParseEndpointPayload("setPlayMode", request.Body));

        if (path == "/select-asset")
            return HandleCommand(ParseEndpointPayload("selectAsset", request.Body));

        return JsonError(404, "not_found", "Unknown endpoint: " + path);
    }

    private static BridgeResponse HandleCommand(CommandPayload payload)
    {
        string command = (payload.command ?? string.Empty).Trim();
        if (command.Length == 0)
            throw new BridgeRequestException(400, "missing_command", "Command payload must include a command field.");

        switch (command.ToLowerInvariant())
        {
            case "ping":
                return JsonResponse("{\"success\":true,\"command\":\"ping\",\"reply\":\"pong\",\"message\":\"" + EscapeJson(payload.message) + "\"}");

            case "status":
                return JsonResponse(BuildStatusJson());

            case "selection":
                return JsonResponse(BuildSelectionJson());

            case "log":
                Debug.Log("[QianxiaEditorBridge] " + payload.message);
                return JsonResponse("{\"success\":true,\"command\":\"log\"}");

            case "executemenuitem":
            case "execute-menu-item":
                return ExecuteMenuItem(payload.menuItem);

            case "setplaymode":
            case "playmode":
                EditorApplication.isPlaying = payload.isPlaying;
                return JsonResponse(
                    "{\"success\":true,\"command\":\"setPlayMode\",\"requestedIsPlaying\":" +
                    ToJsonBool(payload.isPlaying) +
                    ",\"currentIsPlaying\":" +
                    ToJsonBool(EditorApplication.isPlaying) +
                    "}");

            case "selectasset":
            case "select-asset":
                return SelectAsset(payload.assetPath);

            case "clearselection":
            case "clear-selection":
                Selection.activeObject = null;
                return JsonResponse("{\"success\":true,\"command\":\"clearSelection\"}");

            default:
                return JsonError(400, "unknown_command", "Unknown command: " + command);
        }
    }

    private static BridgeResponse ExecuteMenuItem(string menuItem)
    {
        if (string.IsNullOrWhiteSpace(menuItem))
            throw new BridgeRequestException(400, "missing_menu_item", "executeMenuItem requires a menuItem field.");

        bool executed = EditorApplication.ExecuteMenuItem(menuItem);
        return JsonResponse(
            "{\"success\":" +
            ToJsonBool(executed) +
            ",\"command\":\"executeMenuItem\",\"menuItem\":\"" +
            EscapeJson(menuItem) +
            "\"}");
    }

    private static BridgeResponse SelectAsset(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            throw new BridgeRequestException(400, "missing_asset_path", "selectAsset requires an assetPath field.");

        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (asset == null)
            return JsonError(404, "asset_not_found", "Asset not found: " + assetPath);

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
        return JsonResponse(
            "{\"success\":true,\"command\":\"selectAsset\",\"assetPath\":\"" +
            EscapeJson(assetPath) +
            "\",\"name\":\"" +
            EscapeJson(asset.name) +
            "\"}");
    }

    private static CommandPayload ParsePayload(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new CommandPayload();

        try
        {
            CommandPayload payload = JsonUtility.FromJson<CommandPayload>(body);
            return payload ?? new CommandPayload();
        }
        catch (Exception exception)
        {
            throw new BridgeRequestException(400, "invalid_json", exception.Message);
        }
    }

    private static CommandPayload ParseEndpointPayload(string command, string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Trim() == "{}")
            return ParsePayload("{\"command\":\"" + EscapeJson(command) + "\"}");

        return ParsePayload("{\"command\":\"" + EscapeJson(command) + "\"," + TrimObjectStart(body));
    }

    private static string TrimObjectStart(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "}";

        string trimmed = body.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            throw new BridgeRequestException(400, "invalid_json", "Request body must be a JSON object.");

        if (trimmed == "{}")
            return "}";

        return trimmed.Substring(1);
    }

    private static string BuildHelpJson()
    {
        return
            "{" +
            "\"success\":true," +
            "\"name\":\"qianxia-editor-bridge\"," +
            "\"url\":\"" + EscapeJson(LocalUrl) + "\"," +
            "\"endpoints\":[" +
            "\"GET /status\"," +
            "\"GET /selection\"," +
            "\"GET /ping\"," +
            "\"POST /command\"," +
            "\"POST /log\"," +
            "\"POST /execute-menu-item\"," +
            "\"POST /playmode\"," +
            "\"POST /select-asset\"" +
            "]," +
            "\"commands\":[" +
            "\"ping\"," +
            "\"status\"," +
            "\"selection\"," +
            "\"log\"," +
            "\"executeMenuItem\"," +
            "\"setPlayMode\"," +
            "\"selectAsset\"," +
            "\"clearSelection\"" +
            "]" +
            "}";
    }

    private static string BuildStatusJson()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        return
            "{" +
            "\"success\":true," +
            "\"name\":\"qianxia-editor-bridge\"," +
            "\"url\":\"" + EscapeJson(LocalUrl) + "\"," +
            "\"unityVersion\":\"" + EscapeJson(Application.unityVersion) + "\"," +
            "\"projectPath\":\"" + EscapeJson(projectPath) + "\"," +
            "\"isPlaying\":" + ToJsonBool(EditorApplication.isPlaying) + "," +
            "\"isPaused\":" + ToJsonBool(EditorApplication.isPaused) + "," +
            "\"isCompiling\":" + ToJsonBool(EditorApplication.isCompiling) + "," +
            "\"isUpdating\":" + ToJsonBool(EditorApplication.isUpdating) + "," +
            "\"activeScene\":{" +
            "\"name\":\"" + EscapeJson(activeScene.name) + "\"," +
            "\"path\":\"" + EscapeJson(activeScene.path) + "\"," +
            "\"isLoaded\":" + ToJsonBool(activeScene.isLoaded) + "," +
            "\"isDirty\":" + ToJsonBool(activeScene.isDirty) +
            "}," +
            "\"selection\":" + BuildSelectionObjectJson() +
            "}";
    }

    private static string BuildSelectionJson()
    {
        return "{\"success\":true,\"selection\":" + BuildSelectionObjectJson() + "}";
    }

    private static string BuildSelectionObjectJson()
    {
        UnityEngine.Object selected = Selection.activeObject;
        if (selected == null)
            return "null";

        string assetPath = AssetDatabase.GetAssetPath(selected);
        return
            "{" +
            "\"name\":\"" + EscapeJson(selected.name) + "\"," +
            "\"type\":\"" + EscapeJson(selected.GetType().Name) + "\"," +
            "\"assetPath\":\"" + EscapeJson(assetPath) + "\"," +
            "\"instanceId\":" + selected.GetInstanceID().ToString() +
            "}";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        string normalized = path.Trim();
        if (normalized.Length > 1)
            normalized = normalized.TrimEnd('/');

        return normalized.ToLowerInvariant();
    }

    private static BridgeResponse JsonResponse(string body)
    {
        return new BridgeResponse(200, body);
    }

    private static BridgeResponse JsonError(int statusCode, string errorCode, string message)
    {
        return new BridgeResponse(
            statusCode,
            "{\"success\":false,\"error\":\"" +
            EscapeJson(errorCode) +
            "\",\"message\":\"" +
            EscapeJson(message) +
            "\"}");
    }

    private static void WriteResponse(HttpListenerContext context, BridgeResponse response)
    {
        HttpListenerResponse httpResponse = context.Response;
        httpResponse.StatusCode = response.StatusCode;
        httpResponse.ContentType = response.Body.Length > 0
            ? "application/json; charset=utf-8"
            : "text/plain; charset=utf-8";
        httpResponse.Headers["Access-Control-Allow-Origin"] = "*";
        httpResponse.Headers["Access-Control-Allow-Headers"] = "content-type";
        httpResponse.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";

        byte[] buffer = Encoding.UTF8.GetBytes(response.Body);
        httpResponse.ContentLength64 = buffer.Length;
        if (buffer.Length > 0)
            httpResponse.OutputStream.Write(buffer, 0, buffer.Length);

        httpResponse.OutputStream.Close();
    }

    private static void FlushPendingRequestsAsUnavailable()
    {
        while (true)
        {
            BridgeWorkItem item = null;
            lock (QueueLock)
            {
                if (PendingRequests.Count > 0)
                    item = PendingRequests.Dequeue();
            }

            if (item == null)
                return;

            item.Response = JsonError(503, "bridge_stopped", "Editor bridge stopped before the request was handled.");
            item.Done.Set();
        }
    }

    private static int ReadRequestedPort()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--qianxia-editor-bridge-port", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int commandLinePort))
            {
                return ClampPort(commandLinePort);
            }
        }

        return ClampPort(EditorPrefs.GetInt(PortPrefKey, DefaultPort));
    }

    private static int ClampPort(int port)
    {
        if (port < MinPort)
            return MinPort;

        if (port > MaxPort)
            return MaxPort;

        return port;
    }

    private static string ToJsonBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder builder = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
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
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (c < 32)
                        builder.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    [Serializable]
    private sealed class CommandPayload
    {
        public string command = string.Empty;
        public string message = string.Empty;
        public string menuItem = string.Empty;
        public string assetPath = string.Empty;
        public bool isPlaying;
    }

    private sealed class BridgeRequest
    {
        public BridgeRequest(string method, string path, string query, string body)
        {
            Method = method;
            Path = path;
            Query = query;
            Body = body;
        }

        public string Method { get; private set; }
        public string Path { get; private set; }
        public string Query { get; private set; }
        public string Body { get; private set; }
    }

    private sealed class BridgeResponse
    {
        public BridgeResponse(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
        }

        public int StatusCode { get; private set; }
        public string Body { get; private set; }
    }

    private sealed class BridgeWorkItem
    {
        public BridgeWorkItem(BridgeRequest request)
        {
            Request = request;
            Done = new ManualResetEventSlim(false);
        }

        public BridgeRequest Request { get; private set; }
        public BridgeResponse Response { get; set; }
        public ManualResetEventSlim Done { get; private set; }
    }

    private sealed class BridgeRequestException : Exception
    {
        public BridgeRequestException(int statusCode, string errorCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }

        public int StatusCode { get; private set; }
        public string ErrorCode { get; private set; }
    }
}

public sealed class QianxiaEditorBridgeWindow : EditorWindow
{
    private int _port;
    private bool _autoStart;

    public static void Open()
    {
        QianxiaEditorBridgeWindow window = GetWindow<QianxiaEditorBridgeWindow>();
        window.titleContent = new GUIContent("Editor Bridge");
        window.minSize = new Vector2(420.0f, 240.0f);
        window.Show();
    }

    private void OnEnable()
    {
        _port = QianxiaEditorBridgeServer.Port;
        _autoStart = QianxiaEditorBridgeServer.AutoStart;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Qianxia Editor Bridge", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Status", QianxiaEditorBridgeServer.IsRunning ? "Running" : "Stopped");
            EditorGUILayout.SelectableLabel(QianxiaEditorBridgeServer.LocalUrl, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            using (new EditorGUI.DisabledScope(QianxiaEditorBridgeServer.IsRunning))
            {
                EditorGUI.BeginChangeCheck();
                _port = EditorGUILayout.IntField("Port", _port);
                if (EditorGUI.EndChangeCheck())
                {
                    _port = Math.Max(1024, Math.Min(65535, _port));
                    QianxiaEditorBridgeServer.Port = _port;
                }
            }

            EditorGUI.BeginChangeCheck();
            _autoStart = EditorGUILayout.Toggle("Auto Start", _autoStart);
            if (EditorGUI.EndChangeCheck())
                QianxiaEditorBridgeServer.AutoStart = _autoStart;

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(QianxiaEditorBridgeServer.IsRunning))
                {
                    if (GUILayout.Button("Start"))
                        QianxiaEditorBridgeServer.Start();
                }

                using (new EditorGUI.DisabledScope(!QianxiaEditorBridgeServer.IsRunning))
                {
                    if (GUILayout.Button("Stop"))
                        QianxiaEditorBridgeServer.Stop();

                    if (GUILayout.Button("Restart"))
                        QianxiaEditorBridgeServer.Restart();
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Endpoints: GET /status, GET /selection, POST /command. The listener binds only to 127.0.0.1.", MessageType.Info);

        string lastError = QianxiaEditorBridgeServer.LastError;
        if (!string.IsNullOrEmpty(lastError))
            EditorGUILayout.HelpBox(lastError, MessageType.Warning);

    }
}
#endif
