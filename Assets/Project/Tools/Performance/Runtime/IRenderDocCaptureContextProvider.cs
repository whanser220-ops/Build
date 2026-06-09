using System;
using UnityEngine;

public interface IRenderDocCaptureContextProvider
{
    string ProviderId { get; }

    bool TryBuildPayload(out RenderDocCaptureProviderPayload payload);
}

public interface IRenderDocCaptureLifecycleListener
{
    void OnRenderDocCaptureTriggered(RenderDocCaptureRuntimeContext context);
    void OnRenderDocCaptureCompleted(RenderDocCaptureRuntimeContext context, string capturePath);
    void OnRenderDocCaptureAborted(RenderDocCaptureRuntimeContext context, string errorMessage);
}

[Serializable]
public struct RenderDocCaptureRuntimeContext
{
    public string artifactDirectory;
    public string label;
    public string triggerMode;
    public string notes;
    public string sceneName;
    public string scenePath;
    public int requestedFrameIndex;
    public int triggerFrameIndex;
    public DateTime localTimestamp;
    public Camera captureCamera;
}

[Serializable]
public struct RenderDocCaptureProviderPayload
{
    public string providerId;
    public string summary;
    public string suggestedFileName;
    public string jsonPayload;
}
