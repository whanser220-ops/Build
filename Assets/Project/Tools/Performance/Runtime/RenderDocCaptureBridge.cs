using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class RenderDocCaptureBridge
{
    private const int RenderDocApiVersion170 = 10700;
    private const string DefaultRenderDocLibraryPath = @"C:\Program Files\RenderDoc\renderdoc.dll";
    private const int InitialCapturePathCapacity = 4096;
    private static readonly object SyncRoot = new object();

    private static bool _isAvailable;
    private static string _statusMessage = "RenderDoc bridge not initialized.";
    private static string _libraryPath = string.Empty;
    private static IntPtr _moduleHandle = IntPtr.Zero;
    private static RenderDocApi _api;

    [StructLayout(LayoutKind.Sequential)]
    private struct RenderDocApiV170Layout
    {
        public IntPtr GetAPIVersion;
        public IntPtr SetCaptureOptionU32;
        public IntPtr SetCaptureOptionF32;
        public IntPtr GetCaptureOptionU32;
        public IntPtr GetCaptureOptionF32;
        public IntPtr SetCaptureKeys;
        public IntPtr GetCaptureKeys;
        public IntPtr SetFocusToggleKeys;
        public IntPtr GetFocusToggleKeys;
        public IntPtr RemoveHooks;
        public IntPtr UnloadCrashHandler;
        public IntPtr SetCaptureFilePathTemplate;
        public IntPtr GetCaptureFilePathTemplate;
        public IntPtr GetNumCaptures;
        public IntPtr GetCapture;
        public IntPtr TriggerCapture;
        public IntPtr IsTargetControlConnected;
        public IntPtr LaunchReplayUI;
        public IntPtr SetActiveWindow;
        public IntPtr StartFrameCapture;
        public IntPtr IsFrameCapturing;
        public IntPtr EndFrameCapture;
        public IntPtr TriggerMultiFrameCapture;
        public IntPtr SetCaptureFileComments;
        public IntPtr DiscardFrameCapture;
        public IntPtr ShowReplayUI;
        public IntPtr SetCaptureTitle;
        public IntPtr SetObjectAnnotation;
        public IntPtr SetCommandAnnotation;
    }

    private sealed class RenderDocApi
    {
        public GetApiVersionDelegate GetApiVersion;
        public SetCaptureFilePathTemplateDelegate SetCaptureFilePathTemplate;
        public GetNumCapturesDelegate GetNumCaptures;
        public GetCaptureDelegate GetCapture;
        public TriggerCaptureDelegate TriggerCapture;
        public StartFrameCaptureDelegate StartFrameCapture;
        public IsFrameCapturingDelegate IsFrameCapturing;
        public EndFrameCaptureDelegate EndFrameCapture;
        public SetCaptureFileCommentsDelegate SetCaptureFileComments;
        public ShowReplayUIDelegate ShowReplayUI;
        public SetCaptureTitleDelegate SetCaptureTitle;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetRenderDocApiDelegate(int version, out IntPtr apiPointers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetApiVersionDelegate(out int major, out int minor, out int patch);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetCaptureFilePathTemplateDelegate(IntPtr pathTemplateUtf8);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetNumCapturesDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetCaptureDelegate(uint index, IntPtr filename, ref uint pathLength, out ulong timestamp);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TriggerCaptureDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StartFrameCaptureDelegate(IntPtr device, IntPtr windowHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint IsFrameCapturingDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint EndFrameCaptureDelegate(IntPtr device, IntPtr windowHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetCaptureFileCommentsDelegate(IntPtr filePathUtf8, IntPtr commentsUtf8);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ShowReplayUIDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetCaptureTitleDelegate(IntPtr titleUtf8);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    public static bool IsAvailable
    {
        get
        {
            lock (SyncRoot)
                return _isAvailable;
        }
    }

    public static string StatusMessage
    {
        get
        {
            lock (SyncRoot)
                return _statusMessage;
        }
    }

    public static string LoadedLibraryPath
    {
        get
        {
            lock (SyncRoot)
                return _libraryPath;
        }
    }

    public static bool TryInitialize(string overrideLibraryPath, out string statusMessage)
    {
        lock (SyncRoot)
        {
            if (_isAvailable)
            {
                statusMessage = _statusMessage;
                return true;
            }

            string libraryPath = ResolveLibraryPath(overrideLibraryPath);
            if (string.IsNullOrEmpty(libraryPath) || !File.Exists(libraryPath))
            {
                _statusMessage = string.Format("RenderDoc DLL not found: {0}", libraryPath);
                statusMessage = _statusMessage;
                return false;
            }

            _moduleHandle = GetModuleHandle(Path.GetFileName(libraryPath));
            if (_moduleHandle == IntPtr.Zero)
                _moduleHandle = LoadLibrary(libraryPath);

            if (_moduleHandle == IntPtr.Zero)
            {
                _statusMessage = string.Format("Failed to load RenderDoc DLL: {0}", libraryPath);
                statusMessage = _statusMessage;
                return false;
            }

            IntPtr getApiPointer = GetProcAddress(_moduleHandle, "RENDERDOC_GetAPI");
            if (getApiPointer == IntPtr.Zero)
            {
                _statusMessage = "RenderDoc DLL is missing the RENDERDOC_GetAPI export.";
                statusMessage = _statusMessage;
                return false;
            }

            GetRenderDocApiDelegate getApi = Marshal.GetDelegateForFunctionPointer<GetRenderDocApiDelegate>(getApiPointer);
            IntPtr apiPointers;
            int result = getApi(RenderDocApiVersion170, out apiPointers);
            if (result != 1 || apiPointers == IntPtr.Zero)
            {
                _statusMessage = string.Format("Failed to initialize RenderDoc API 1.7.0. Return code: {0}", result);
                statusMessage = _statusMessage;
                return false;
            }

            RenderDocApiV170Layout layout = Marshal.PtrToStructure<RenderDocApiV170Layout>(apiPointers);
            _api = new RenderDocApi
            {
                GetApiVersion = MarshalFunction<GetApiVersionDelegate>(layout.GetAPIVersion),
                SetCaptureFilePathTemplate = MarshalFunction<SetCaptureFilePathTemplateDelegate>(layout.SetCaptureFilePathTemplate),
                GetNumCaptures = MarshalFunction<GetNumCapturesDelegate>(layout.GetNumCaptures),
                GetCapture = MarshalFunction<GetCaptureDelegate>(layout.GetCapture),
                TriggerCapture = MarshalFunction<TriggerCaptureDelegate>(layout.TriggerCapture),
                StartFrameCapture = MarshalFunction<StartFrameCaptureDelegate>(layout.StartFrameCapture),
                IsFrameCapturing = MarshalFunction<IsFrameCapturingDelegate>(layout.IsFrameCapturing),
                EndFrameCapture = MarshalFunction<EndFrameCaptureDelegate>(layout.EndFrameCapture),
                SetCaptureFileComments = MarshalFunction<SetCaptureFileCommentsDelegate>(layout.SetCaptureFileComments),
                ShowReplayUI = MarshalFunction<ShowReplayUIDelegate>(layout.ShowReplayUI),
                SetCaptureTitle = MarshalFunction<SetCaptureTitleDelegate>(layout.SetCaptureTitle)
            };

            _libraryPath = libraryPath;
            _isAvailable = _api.GetApiVersion != null &&
                _api.SetCaptureFilePathTemplate != null &&
                _api.GetNumCaptures != null &&
                _api.GetCapture != null &&
                _api.TriggerCapture != null;

            if (!_isAvailable)
            {
                _statusMessage = "RenderDoc API pointers are incomplete. The bridge is unavailable.";
                statusMessage = _statusMessage;
                return false;
            }

            int major;
            int minor;
            int patch;
            _api.GetApiVersion(out major, out minor, out patch);
            _statusMessage = string.Format("Connected to RenderDoc API {0}.{1}.{2}.", major, minor, patch);
            statusMessage = _statusMessage;
            return true;
        }
    }

    public static bool SetCaptureTemplatePath(string captureTemplatePath, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        InvokeUtf8(captureTemplatePath, _api.SetCaptureFilePathTemplate);
        return true;
    }

    public static bool SetCaptureTitle(string captureTitle, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        if (_api.SetCaptureTitle == null)
            return true;

        InvokeUtf8(captureTitle, _api.SetCaptureTitle);
        return true;
    }

    public static bool StartFrameCapture(out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        if (_api.StartFrameCapture == null)
        {
            errorMessage = "The current RenderDoc API does not support StartFrameCapture.";
            return false;
        }

        _api.StartFrameCapture(IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    public static bool EndFrameCapture(out string capturePath, out ulong timestamp, out string errorMessage)
    {
        capturePath = string.Empty;
        timestamp = 0UL;
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        if (_api.EndFrameCapture == null)
        {
            errorMessage = "The current RenderDoc API does not support EndFrameCapture.";
            return false;
        }

        uint succeeded = _api.EndFrameCapture(IntPtr.Zero, IntPtr.Zero);
        if (succeeded == 0u)
        {
            errorMessage = "RenderDoc failed to end the frame capture.";
            return false;
        }

        if (!TryGetLatestCapture(out capturePath, out timestamp, out errorMessage))
            return false;

        return true;
    }

    public static bool TriggerCapture(out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        if (_api.TriggerCapture == null)
        {
            errorMessage = "The current RenderDoc API does not support TriggerCapture.";
            return false;
        }

        _api.TriggerCapture();
        return true;
    }

    public static bool TryGetCaptureCount(out uint captureCount, out string errorMessage)
    {
        captureCount = 0u;
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        captureCount = _api.GetNumCaptures();
        return true;
    }

    public static bool TryGetLatestCapture(out string capturePath, out ulong timestamp, out string errorMessage)
    {
        capturePath = string.Empty;
        timestamp = 0UL;
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        uint captureCount = _api.GetNumCaptures();
        if (captureCount == 0u)
        {
            errorMessage = "RenderDoc has not recorded any captures yet.";
            return false;
        }

        return TryGetCapture(captureCount - 1u, out capturePath, out timestamp, out errorMessage);
    }

    public static bool SetCaptureComments(string capturePath, string comments, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        if (_api.SetCaptureFileComments == null || string.IsNullOrEmpty(capturePath))
            return false;

        InvokeUtf8Pair(capturePath, comments, _api.SetCaptureFileComments);
        return true;
    }

    public static bool ShowReplayUi(out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!EnsureAvailable(out errorMessage))
            return false;

        if (_api.ShowReplayUI == null)
        {
            errorMessage = "The current RenderDoc API does not support ShowReplayUI.";
            return false;
        }

        return _api.ShowReplayUI() != 0u;
    }

    private static bool TryGetCapture(uint captureIndex, out string capturePath, out ulong timestamp, out string errorMessage)
    {
        capturePath = string.Empty;
        timestamp = 0UL;
        errorMessage = string.Empty;

        uint pathLength = InitialCapturePathCapacity;
        IntPtr buffer = Marshal.AllocHGlobal((int)pathLength);
        try
        {
            uint valid = _api.GetCapture(captureIndex, buffer, ref pathLength, out timestamp);
            if (valid == 0u)
            {
                errorMessage = string.Format("RenderDoc capture index is invalid: {0}", captureIndex);
                return false;
            }

            if (pathLength > InitialCapturePathCapacity)
            {
                Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal((int)pathLength);
                uint secondPassLength = pathLength;
                valid = _api.GetCapture(captureIndex, buffer, ref secondPassLength, out timestamp);
                if (valid == 0u)
                {
                    errorMessage = string.Format("Failed to read RenderDoc capture at index {0}", captureIndex);
                    return false;
                }

                pathLength = secondPassLength;
            }

            byte[] bytes = new byte[pathLength];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            capturePath = DecodeUtf8(bytes);
            if (string.IsNullOrEmpty(capturePath))
            {
                errorMessage = "RenderDoc returned an empty capture path.";
                return false;
            }

            return true;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    private static string ResolveLibraryPath(string overrideLibraryPath)
    {
        if (!string.IsNullOrWhiteSpace(overrideLibraryPath))
            return Path.GetFullPath(overrideLibraryPath);

        return DefaultRenderDocLibraryPath;
    }

    private static bool EnsureAvailable(out string errorMessage)
    {
        if (_isAvailable)
        {
            errorMessage = string.Empty;
            return true;
        }

        return TryInitialize(string.Empty, out errorMessage);
    }

    private static T MarshalFunction<T>(IntPtr pointer) where T : class
    {
        if (pointer == IntPtr.Zero)
            return null;

        return Marshal.GetDelegateForFunctionPointer(pointer, typeof(T)) as T;
    }

    private static void InvokeUtf8(string value, SetCaptureFilePathTemplateDelegate action)
    {
        IntPtr pointer = StringToUtf8(value);
        try
        {
            action(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void InvokeUtf8(string value, SetCaptureTitleDelegate action)
    {
        IntPtr pointer = StringToUtf8(value);
        try
        {
            action(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void InvokeUtf8Pair(string first, string second, SetCaptureFileCommentsDelegate action)
    {
        IntPtr firstPointer = StringToUtf8(first);
        IntPtr secondPointer = StringToUtf8(second);
        try
        {
            action(firstPointer, secondPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(firstPointer);
            Marshal.FreeHGlobal(secondPointer);
        }
    }

    private static IntPtr StringToUtf8(string value)
    {
        string safeValue = value ?? string.Empty;
        byte[] bytes = Encoding.UTF8.GetBytes(safeValue + '\0');
        IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        int terminatorIndex = Array.IndexOf(bytes, (byte)0);
        int length = terminatorIndex >= 0 ? terminatorIndex : bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }
}
