using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public enum GpuSemanticDrawPhase
{
    Opaque = 0,
    Transparent = 1
}

public interface IGpuSemanticDrawProvider
{
    void RecordGpuSemanticDraws(RasterCommandBuffer commandBuffer, Camera camera, GpuSemanticDrawPhase phase);
}

public readonly struct GpuSemanticDrawCommand
{
    private readonly Action<RasterCommandBuffer, Camera> _record;

    public GpuSemanticDrawCommand(string markerLabel, Action<RasterCommandBuffer, Camera> record)
    {
        MarkerLabel = markerLabel ?? string.Empty;
        _record = record;
    }

    public string MarkerLabel { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(MarkerLabel) && _record != null;

    public void Record(RasterCommandBuffer commandBuffer, Camera camera)
    {
        _record?.Invoke(commandBuffer, camera);
    }
}

public interface IGpuSemanticDrawCommandProvider
{
    void CollectGpuSemanticDrawCommands(List<GpuSemanticDrawCommand> commands, Camera camera, GpuSemanticDrawPhase phase);
}

public static class GpuSemanticDrawRegistry
{
    private static readonly List<IGpuSemanticDrawProvider> Providers = new List<IGpuSemanticDrawProvider>(8);

    public static bool HasProviders => Providers.Count > 0;

    public static void Register(IGpuSemanticDrawProvider provider)
    {
        if (provider == null || Providers.Contains(provider))
            return;

        Providers.Add(provider);
    }

    public static void Unregister(IGpuSemanticDrawProvider provider)
    {
        if (provider == null)
            return;

        Providers.Remove(provider);
    }

    public static void Record(RasterCommandBuffer commandBuffer, Camera camera, GpuSemanticDrawPhase phase)
    {
        if (commandBuffer == null || camera == null)
            return;

        for (int index = 0; index < Providers.Count; index++)
        {
            IGpuSemanticDrawProvider provider = Providers[index];
            provider?.RecordGpuSemanticDraws(commandBuffer, camera, phase);
        }
    }

    public static void RecordLegacy(RasterCommandBuffer commandBuffer, Camera camera, GpuSemanticDrawPhase phase)
    {
        if (commandBuffer == null || camera == null)
            return;

        for (int index = 0; index < Providers.Count; index++)
        {
            IGpuSemanticDrawProvider provider = Providers[index];
            if (provider == null || provider is IGpuSemanticDrawCommandProvider)
                continue;

            provider.RecordGpuSemanticDraws(commandBuffer, camera, phase);
        }
    }

    public static bool CollectCommands(List<GpuSemanticDrawCommand> commands, Camera camera, GpuSemanticDrawPhase phase)
    {
        if (commands == null || camera == null)
            return false;

        bool hasLegacyProviders = false;
        for (int index = 0; index < Providers.Count; index++)
        {
            IGpuSemanticDrawProvider provider = Providers[index];
            if (provider == null)
                continue;

            IGpuSemanticDrawCommandProvider commandProvider = provider as IGpuSemanticDrawCommandProvider;
            if (commandProvider != null)
            {
                commandProvider.CollectGpuSemanticDrawCommands(commands, camera, phase);
                continue;
            }

            hasLegacyProviders = true;
        }

        return hasLegacyProviders;
    }
}

public static class GpuSemanticDrawPassUtility
{
    public static int ResolveMaterialPassIndex(Material material, string preferredPassName)
    {
        if (material == null)
            return 0;

        if (!string.IsNullOrEmpty(preferredPassName))
        {
            int preferredPass = material.FindPass(preferredPassName);
            if (preferredPass >= 0)
                return preferredPass;
        }

        return 0;
    }
}

public sealed class GpuSemanticDrawFeature : ScriptableRendererFeature
{
    [Serializable]
    public sealed class Settings
    {
        public RenderPassEvent opaquePassEvent = RenderPassEvent.AfterRenderingOpaques;
        public RenderPassEvent transparentPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    private sealed class SemanticDrawPass : ScriptableRenderPass
    {
        private sealed class PassData
        {
            public Camera camera;
            public GpuSemanticDrawPhase phase;
            public GpuSemanticDrawCommand command;
            public bool legacyOnly;
        }

        private readonly GpuSemanticDrawPhase _phase;
        private readonly List<GpuSemanticDrawCommand> _commands = new List<GpuSemanticDrawCommand>(8);

        public SemanticDrawPass(GpuSemanticDrawPhase phase)
        {
            _phase = phase;
        }

#pragma warning disable 618, 672
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (!ShouldRecord(camera))
                return;

            CommandBuffer commandBuffer = CommandBufferPool.Get();
            GpuSemanticDrawRegistry.Record(CommandBufferHelpers.GetRasterCommandBuffer(commandBuffer), camera, _phase);
            context.ExecuteCommandBuffer(commandBuffer);
            CommandBufferPool.Release(commandBuffer);
        }
#pragma warning restore 618, 672

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            Camera camera = cameraData.camera;
            if (!ShouldRecord(camera))
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            string passName = _phase == GpuSemanticDrawPhase.Opaque
                ? "GPU Semantic Draw Opaque"
                : "GPU Semantic Draw Transparent";

            _commands.Clear();
            bool hasLegacyProviders = GpuSemanticDrawRegistry.CollectCommands(_commands, camera, _phase);

            for (int index = 0; index < _commands.Count; index++)
            {
                GpuSemanticDrawCommand command = _commands[index];
                if (!command.IsValid)
                    continue;

                AddRenderGraphPass(renderGraph, resourceData, camera, _phase, command.MarkerLabel, command, false);
            }

            if (hasLegacyProviders)
            {
                AddRenderGraphPass(
                    renderGraph,
                    resourceData,
                    camera,
                    _phase,
                    passName,
                    default,
                    true);
            }
        }

        private static void AddRenderGraphPass(
            RenderGraph renderGraph,
            UniversalResourceData resourceData,
            Camera camera,
            GpuSemanticDrawPhase phase,
            string passName,
            GpuSemanticDrawCommand command,
            bool legacyOnly)
        {
            if (!resourceData.activeColorTexture.IsValid())
                return;

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(passName, out PassData passData))
            {
                passData.camera = camera;
                passData.phase = phase;
                passData.command = command;
                passData.legacyOnly = legacyOnly;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);

                if (resourceData.activeDepthTexture.IsValid())
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    if (data.legacyOnly)
                    {
                        GpuSemanticDrawRegistry.RecordLegacy(context.cmd, data.camera, data.phase);
                        return;
                    }

                    data.command.Record(context.cmd, data.camera);
                });
            }
        }

        private static bool ShouldRecord(Camera camera)
        {
            return GpuPassDebugRuntime.CaptureMetadataEnabled &&
                GpuSemanticDrawRegistry.HasProviders &&
                camera != null &&
                camera.cameraType != CameraType.Preview &&
                camera.cameraType != CameraType.Reflection;
        }
    }

    private static int s_InstallCount;

    [SerializeField] private Settings settings = new Settings();

    private SemanticDrawPass _opaquePass;
    private SemanticDrawPass _transparentPass;
    private bool _registeredInstallation;

    public static bool IsInstalled => s_InstallCount > 0;

    public override void Create()
    {
        _opaquePass = new SemanticDrawPass(GpuSemanticDrawPhase.Opaque)
        {
            renderPassEvent = settings.opaquePassEvent
        };
        _transparentPass = new SemanticDrawPass(GpuSemanticDrawPhase.Transparent)
        {
            renderPassEvent = settings.transparentPassEvent
        };

        if (_registeredInstallation)
            return;

        s_InstallCount++;
        _registeredInstallation = true;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (!GpuPassDebugRuntime.CaptureMetadataEnabled ||
            !GpuSemanticDrawRegistry.HasProviders ||
            camera == null ||
            camera.cameraType == CameraType.Preview ||
            camera.cameraType == CameraType.Reflection)
        {
            return;
        }

        renderer.EnqueuePass(_opaquePass);
        renderer.EnqueuePass(_transparentPass);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_registeredInstallation)
            return;

        s_InstallCount = Math.Max(0, s_InstallCount - 1);
        _registeredInstallation = false;
    }
}
