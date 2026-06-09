using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class RenderDocCaptureController : MonoBehaviour
{
    private const int MaxCaptureWaitFrames = 240;

    public event Action<RenderDocCaptureManifest> CaptureCompleted;

    private enum CaptureState
    {
        Idle = 0,
        Pending = 1,
        WaitingForCapture = 2
    }

    [Serializable]
    private sealed class CaptureRequest
    {
        public string label;
        public string triggerMode;
        public string notes;
        public int requestedFrameIndex;
        public int delayFramesRemaining;
        public Camera captureCameraOverride;
        public DateTime localTimestamp;
        public DateTime utcTimestamp;
    }

    [Header("Output")]
    [SerializeField] private string _artifactsRootOverride = string.Empty;
    [SerializeField] private string _artifactsRelativePath = RenderDocCaptureArtifacts.DefaultRelativeArtifactsRoot;
    [SerializeField] private string _defaultLabel = "manual";

    [Header("RenderDoc")]
    [SerializeField] private string _renderDocDllPathOverride = string.Empty;
    [SerializeField] private bool _showReplayUiAfterCapture;
    [SerializeField] private bool _includeGameViewScreenshot = true;

    [Header("Hotkey")]
    [SerializeField] private bool _enableHotkey = true;
    [SerializeField] private KeyCode _captureHotkey = KeyCode.F8;
    [SerializeField] private bool _requireLeftShift = true;
    [SerializeField] private bool _requireLeftControl;

    [Header("Defaults")]
    [SerializeField] private int _defaultDelayFrames;
    [SerializeField] private Camera _captureCamera;

    private CaptureRequest _request;
    private CaptureState _state;
    private string _statusMessage = "No capture has been requested.";
    private string _lastArtifactDirectory = string.Empty;
    private string _lastCaptureFilePath = string.Empty;
    private string _lastScreenshotPath = string.Empty;
    private string _lastUnityMaterialMetadataPath = string.Empty;
    private string _lastUnityPrefabMetadataPath = string.Empty;
    private string _lastUnityPrefabSignatureDatabasePath = string.Empty;
    private string _pendingArtifactDirectory = string.Empty;
    private string _pendingScreenshotPath = string.Empty;
    private string _pendingUnityMaterialMetadataPath = string.Empty;
    private string _pendingUnityPrefabMetadataPath = string.Empty;
    private string _pendingUnityPrefabSignatureDatabasePath = string.Empty;
    private uint _pendingCaptureStartCount;
    private int _pendingCaptureWaitFrames;
    private int _pendingCaptureTriggerFrame = -1;
    private Coroutine _screenshotCoroutine;
    private bool _captureMetadataCollectionActive;

    public string StatusMessage => _statusMessage;
    public string LastArtifactDirectory => _lastArtifactDirectory;
    public string LastCaptureFilePath => _lastCaptureFilePath;
    public string LastScreenshotPath => _lastScreenshotPath;
    public string LastUnityMaterialMetadataPath => _lastUnityMaterialMetadataPath;
    public string LastUnityPrefabMetadataPath => _lastUnityPrefabMetadataPath;
    public string LastUnityPrefabSignatureDatabasePath => _lastUnityPrefabSignatureDatabasePath;
    public string ResolvedArtifactsRoot => RenderDocCaptureArtifacts.ResolveArtifactsRoot(_artifactsRootOverride, _artifactsRelativePath);

    private void OnDisable()
    {
        if (_screenshotCoroutine != null)
        {
            StopCoroutine(_screenshotCoroutine);
            _screenshotCoroutine = null;
        }

        EndCaptureMetadataCollection();
        _state = CaptureState.Idle;
        _request = null;
        ClearPendingCapturePaths();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        HandleHotkey();
        UpdateCaptureRequest();
    }

    private void HandleHotkey()
    {
        if (!_enableHotkey)
            return;

        if (!Input.GetKeyDown(_captureHotkey))
            return;

        if (_requireLeftShift && !Input.GetKey(KeyCode.LeftShift))
            return;

        if (_requireLeftControl && !Input.GetKey(KeyCode.LeftControl))
            return;

        QueueCapture(_defaultLabel, _defaultDelayFrames, "hotkey", string.Empty, _captureCamera);
    }

    [ContextMenu("Queue Capture Now")]
    public void QueueCaptureNow()
    {
        QueueCapture(_defaultLabel, 0, "context-menu", string.Empty, _captureCamera);
    }

    public void ApplyRuntimeOverrides(string artifactsRootOverride, string defaultLabel, string renderDocDllPathOverride, bool? includeGameViewScreenshot)
    {
        if (!string.IsNullOrWhiteSpace(artifactsRootOverride))
            _artifactsRootOverride = artifactsRootOverride;

        if (!string.IsNullOrWhiteSpace(defaultLabel))
            _defaultLabel = defaultLabel;

        if (!string.IsNullOrWhiteSpace(renderDocDllPathOverride))
            _renderDocDllPathOverride = renderDocDllPathOverride;

        if (includeGameViewScreenshot.HasValue)
            _includeGameViewScreenshot = includeGameViewScreenshot.Value;
    }

    public bool QueueCapture(string label, int delayFrames, string triggerMode, string notes, Camera captureCameraOverride)
    {
        if (_state != CaptureState.Idle)
        {
            _statusMessage = "A capture request is already in progress.";
            return false;
        }

        string bridgeStatus;
        if (!RenderDocCaptureBridge.TryInitialize(_renderDocDllPathOverride, out bridgeStatus))
        {
            _statusMessage = bridgeStatus;
            return false;
        }

        _request = new CaptureRequest
        {
            label = string.IsNullOrWhiteSpace(label) ? _defaultLabel : label,
            triggerMode = string.IsNullOrWhiteSpace(triggerMode) ? "manual" : triggerMode,
            notes = notes ?? string.Empty,
            requestedFrameIndex = Time.frameCount,
            delayFramesRemaining = Mathf.Max(0, delayFrames),
            captureCameraOverride = captureCameraOverride,
            localTimestamp = DateTime.Now,
            utcTimestamp = DateTime.UtcNow
        };

        _state = CaptureState.Pending;
        _statusMessage = string.Format(
            "Queued RenderDoc capture: label={0}, delayFrames={1}",
            _request.label,
            _request.delayFramesRemaining);
        return true;
    }

    private void UpdateCaptureRequest()
    {
        if (_state == CaptureState.Pending)
        {
            UpdatePendingCapture();
            return;
        }

        if (_state == CaptureState.WaitingForCapture)
            PollPendingCapture();
    }

    private void UpdatePendingCapture()
    {
        if (_request == null)
        {
            AbortPendingCapture("RenderDoc capture request was lost before it could start.");
            return;
        }

        if (_request.delayFramesRemaining > 0)
        {
            _request.delayFramesRemaining--;
            _statusMessage = string.Format(
                "Queued RenderDoc capture: label={0}, delayFrames={1}",
                _request.label,
                _request.delayFramesRemaining);
            return;
        }

        TriggerPendingCapture();
    }

    private void TriggerPendingCapture()
    {
        Camera resolvedCamera = ResolveCaptureCamera();
        string artifactDirectory = RenderDocCaptureArtifacts.CreateArtifactDirectory(
            ResolvedArtifactsRoot,
            _request.localTimestamp,
            RenderDocCaptureArtifacts.GetActiveSceneName(),
            _request.label);

        PreparePendingCapturePaths(artifactDirectory);

        string captureTemplatePath = RenderDocCaptureArtifacts.BuildCaptureTemplatePath(artifactDirectory);
        string errorMessage;
        if (!RenderDocCaptureBridge.SetCaptureTemplatePath(captureTemplatePath, out errorMessage))
        {
            AbortPendingCapture(errorMessage);
            return;
        }

        RenderDocCaptureBridge.SetCaptureTitle(_request.label, out _);

        uint captureCount;
        if (!RenderDocCaptureBridge.TryGetCaptureCount(out captureCount, out errorMessage))
        {
            AbortPendingCapture(errorMessage);
            return;
        }

        BeginCaptureMetadataCollection();
        if (!RenderDocCaptureBridge.TriggerCapture(out errorMessage))
        {
            AbortPendingCapture(errorMessage);
            return;
        }

        _request.captureCameraOverride = resolvedCamera;
        _pendingCaptureStartCount = captureCount;
        _pendingCaptureWaitFrames = 0;
        _pendingCaptureTriggerFrame = Time.frameCount;
        _state = CaptureState.WaitingForCapture;
        _statusMessage = string.Format(
            "RenderDoc capture triggered on frame {0}; waiting for completed frame capture.",
            _pendingCaptureTriggerFrame);
    }

    private void PollPendingCapture()
    {
        _pendingCaptureWaitFrames++;

        uint captureCount;
        string errorMessage;
        if (!RenderDocCaptureBridge.TryGetCaptureCount(out captureCount, out errorMessage))
        {
            AbortPendingCapture(errorMessage);
            return;
        }

        if (captureCount <= _pendingCaptureStartCount)
        {
            if (_pendingCaptureWaitFrames > MaxCaptureWaitFrames)
            {
                AbortPendingCapture(string.Format(
                    "Timed out waiting for RenderDoc capture after {0} frames.",
                    MaxCaptureWaitFrames));
                return;
            }

            _statusMessage = string.Format(
                "Waiting for RenderDoc capture file. TriggerFrame={0}, waitedFrames={1}",
                _pendingCaptureTriggerFrame,
                _pendingCaptureWaitFrames);
            return;
        }

        string capturePath;
        ulong captureTimestamp;
        if (!RenderDocCaptureBridge.TryGetLatestCapture(out capturePath, out captureTimestamp, out errorMessage))
        {
            AbortPendingCapture(errorMessage);
            return;
        }

        CompletePendingCapture(capturePath, captureTimestamp);
    }

    private void CompletePendingCapture(string capturePath, ulong captureTimestamp)
    {
        RenderDocCaptureManifest manifest = BuildManifest(capturePath, captureTimestamp);
        CollectProviderSummaries(manifest);
        EndCaptureMetadataCollection();
        RenderDocCaptureBridge.SetCaptureComments(capturePath, BuildCaptureComments(manifest), out _);

        if (_showReplayUiAfterCapture)
            RenderDocCaptureBridge.ShowReplayUi(out _);

        CommitSuccessfulCapture(capturePath);

        if (_includeGameViewScreenshot)
        {
            if (_screenshotCoroutine != null)
                StopCoroutine(_screenshotCoroutine);

            _screenshotCoroutine = StartCoroutine(CaptureScreenshotAtEndOfFrame(_lastScreenshotPath));
        }

        _statusMessage = string.Format("RenderDoc capture completed: {0}", capturePath);
        _state = CaptureState.Idle;
        _request = null;

        try
        {
            CaptureCompleted?.Invoke(manifest);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private RenderDocCaptureManifest BuildManifest(string capturePath, ulong captureTimestamp)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        Camera captureCamera = _request != null ? _request.captureCameraOverride : null;
        if (captureCamera == null)
            captureCamera = _captureCamera != null ? _captureCamera : Camera.main;

        RenderDocCaptureManifest manifest = new RenderDocCaptureManifest
        {
            captureId = Path.GetFileName(_pendingArtifactDirectory),
            label = _request != null ? _request.label : _defaultLabel,
            triggerMode = _request != null ? _request.triggerMode : "unknown",
            status = "Captured",
            timestampLocal = (_request != null ? _request.localTimestamp : DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss"),
            timestampUtc = (_request != null ? _request.utcTimestamp : DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
            requestedFrameIndex = _request != null ? _request.requestedFrameIndex : Time.frameCount,
            capturedFrameIndex = _pendingCaptureTriggerFrame >= 0 ? _pendingCaptureTriggerFrame : Time.frameCount,
            unityVersion = Application.unityVersion,
            applicationPlatform = Application.platform.ToString(),
            runContext = Application.isEditor ? "Editor PlayMode" : "Player",
            sceneName = activeScene.name,
            scenePath = activeScene.path,
            graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
            graphicsDeviceName = SystemInfo.graphicsDeviceName,
            renderPipelineAssetName = RenderDocCaptureArtifacts.GetActiveRenderPipelineAssetName(),
            screenWidth = Screen.width,
            screenHeight = Screen.height,
            qualityLevel = RenderDocCaptureArtifacts.GetActiveQualityLevelName(),
            cameraName = captureCamera != null ? captureCamera.name : "None",
            cameraPosition = captureCamera != null ? captureCamera.transform.position : Vector3.zero,
            cameraForward = captureCamera != null ? captureCamera.transform.forward : Vector3.forward,
            renderDocDllPath = RenderDocCaptureBridge.LoadedLibraryPath,
            captureFilePath = capturePath,
            screenshotFilePath = _pendingScreenshotPath,
            unityMaterialMetadataPath = _pendingUnityMaterialMetadataPath,
            unityPrefabMetadataPath = _pendingUnityPrefabMetadataPath,
            unityPrefabSignatureDatabasePath = _pendingUnityPrefabSignatureDatabasePath,
            notes = _request != null ? _request.notes : string.Empty,
            renderDocCaptureTimestampUnixSeconds = captureTimestamp
        };

        return manifest;
    }

    private void CollectProviderSummaries(RenderDocCaptureManifest manifest)
    {
        List<RenderDocCaptureManifestProviderEntry> entries = new List<RenderDocCaptureManifestProviderEntry>();
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < behaviours.Length; index++)
        {
            MonoBehaviour behaviour = behaviours[index];
            if (behaviour == null || !behaviour.isActiveAndEnabled)
                continue;

            IRenderDocCaptureContextProvider provider = behaviour as IRenderDocCaptureContextProvider;
            if (provider == null)
                continue;

            RenderDocCaptureProviderPayload payload;
            if (!provider.TryBuildPayload(out payload))
                continue;

            if (string.IsNullOrWhiteSpace(payload.summary))
                continue;

            entries.Add(new RenderDocCaptureManifestProviderEntry
            {
                providerId = payload.providerId,
                summary = payload.summary
            });
        }

        manifest.providers = entries.ToArray();
    }

    private string BuildCaptureComments(RenderDocCaptureManifest manifest)
    {
        if (manifest == null)
            return string.Empty;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
        builder.AppendFormat("Scene={0}; Label={1}; Trigger={2}; Frame={3}; Resolution={4}x{5}",
            manifest.sceneName,
            manifest.label,
            manifest.triggerMode,
            manifest.capturedFrameIndex,
            manifest.screenWidth,
            manifest.screenHeight);

        if (manifest.providers != null && manifest.providers.Length > 0)
        {
            builder.Append("; Providers=");
            for (int index = 0; index < manifest.providers.Length; index++)
            {
                if (index > 0)
                    builder.Append(", ");

                builder.Append(manifest.providers[index].providerId);
            }
        }

        return builder.ToString();
    }

    private Camera ResolveCaptureCamera()
    {
        if (_request != null && _request.captureCameraOverride != null)
            return _request.captureCameraOverride;

        if (_captureCamera != null)
            return _captureCamera;

        return Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
    }

    private IEnumerator CaptureScreenshotAtEndOfFrame(string screenshotPath)
    {
        yield return new WaitForEndOfFrame();
        _screenshotCoroutine = null;

        if (string.IsNullOrEmpty(screenshotPath))
            yield break;

        Texture2D screenshotTexture = null;
        try
        {
            screenshotTexture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false, false);
            screenshotTexture.ReadPixels(new Rect(0.0f, 0.0f, Screen.width, Screen.height), 0, 0);
            screenshotTexture.Apply(false, false);
            byte[] pngData = screenshotTexture.EncodeToPNG();
            File.WriteAllBytes(screenshotPath, pngData);
        }
        catch (Exception exception)
        {
            _statusMessage = string.Format(
                "RenderDoc capture completed, but failed to save screenshot: {0}",
                exception.Message);
        }
        finally
        {
            if (screenshotTexture != null)
                Destroy(screenshotTexture);
        }
    }

    private void PreparePendingCapturePaths(string artifactDirectory)
    {
        _pendingArtifactDirectory = artifactDirectory;
        _pendingScreenshotPath = RenderDocCaptureArtifacts.BuildScreenshotPath(artifactDirectory);
        _pendingUnityMaterialMetadataPath = RenderDocCaptureArtifacts.BuildUnityMaterialMetadataPath(artifactDirectory);
        _pendingUnityPrefabMetadataPath = RenderDocCaptureArtifacts.BuildUnityPrefabMetadataPath(artifactDirectory);
        _pendingUnityPrefabSignatureDatabasePath = RenderDocCaptureArtifacts.BuildUnityPrefabSignatureDatabasePath(artifactDirectory);
    }

    private void CommitSuccessfulCapture(string capturePath)
    {
        _lastArtifactDirectory = _pendingArtifactDirectory;
        _lastCaptureFilePath = capturePath;
        _lastScreenshotPath = _pendingScreenshotPath;
        _lastUnityMaterialMetadataPath = _pendingUnityMaterialMetadataPath;
        _lastUnityPrefabMetadataPath = _pendingUnityPrefabMetadataPath;
        _lastUnityPrefabSignatureDatabasePath = _pendingUnityPrefabSignatureDatabasePath;
        ClearPendingCapturePaths();
    }

    private void AbortPendingCapture(string errorMessage)
    {
        bool captureAlreadyTriggered = _state == CaptureState.WaitingForCapture;
        _statusMessage = errorMessage;
        _state = CaptureState.Idle;
        _request = null;
        EndCaptureMetadataCollection();
        if (!captureAlreadyTriggered)
            DeleteDirectoryIfEmpty(_pendingArtifactDirectory);

        ClearPendingCapturePaths();
    }

    private void BeginCaptureMetadataCollection()
    {
        if (_captureMetadataCollectionActive)
            return;

        GpuPassBindingDebugRegistry.Clear();
        GpuPassDebugRuntime.BeginCaptureMetadata();
        _captureMetadataCollectionActive = true;
    }

    private void EndCaptureMetadataCollection()
    {
        if (!_captureMetadataCollectionActive)
            return;

        GpuPassDebugRuntime.EndCaptureMetadata();
        _captureMetadataCollectionActive = false;
    }

    private void ClearPendingCapturePaths()
    {
        _pendingArtifactDirectory = string.Empty;
        _pendingScreenshotPath = string.Empty;
        _pendingUnityMaterialMetadataPath = string.Empty;
        _pendingUnityPrefabMetadataPath = string.Empty;
        _pendingUnityPrefabSignatureDatabasePath = string.Empty;
        _pendingCaptureStartCount = 0u;
        _pendingCaptureWaitFrames = 0;
        _pendingCaptureTriggerFrame = -1;
    }

    private static void DeleteDirectoryIfEmpty(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            return;

        if (Directory.GetFiles(directoryPath).Length > 0)
            return;

        if (Directory.GetDirectories(directoryPath).Length > 0)
            return;

        Directory.Delete(directoryPath, false);
    }
}
