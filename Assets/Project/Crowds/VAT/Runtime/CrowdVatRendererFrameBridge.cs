public sealed class CrowdVatRendererFrameBridge
{
    private CrowdVatIndirectRenderer _renderer;
    private readonly CrowdVatObservationHub _observationHub;

    public CrowdVatRendererFrameBridge(CrowdVatIndirectRenderer renderer)
    {
        _renderer = renderer;
        _observationHub = new CrowdVatObservationHub(renderer);
    }

    public CrowdVatObservationHub ObservationHub => _observationHub;

    public void SetRenderer(CrowdVatIndirectRenderer renderer)
    {
        _renderer = renderer;
        _observationHub.SetRenderer(renderer);
    }

    public void ApplyFrameInput(CrowdVatFrameInput frameInput)
    {
        if (_renderer == null)
            return;

        _renderer.ApplyFrameInput(frameInput);
    }

    public void ApplyFramePacket(CrowdVatFramePacket framePacket)
    {
        if (_renderer == null)
            return;

        _renderer.ApplyFramePacket(framePacket);
    }
}
