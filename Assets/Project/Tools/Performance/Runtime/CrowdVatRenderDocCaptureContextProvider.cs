using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CrowdVatIndirectRenderer))]
public sealed class CrowdVatRenderDocCaptureContextProvider : MonoBehaviour, IRenderDocCaptureContextProvider
{
    [Serializable]
    private sealed class SnapshotPayload
    {
        public string providerId;
        public string objectName;
        public CrowdVatAiDebugSystemSnapshot systemSnapshot;
        public bool hasSceneQueryFieldAsset;
        public string sceneQueryFieldAssetName;
        public int runtimeSpatialQueryCount;
    }

    [SerializeField] private CrowdVatIndirectRenderer _renderer;

    public string ProviderId => "crowdVat";

    private void Reset()
    {
        _renderer = GetComponent<CrowdVatIndirectRenderer>();
    }

    public bool TryBuildPayload(out RenderDocCaptureProviderPayload payload)
    {
        if (_renderer == null)
            _renderer = GetComponent<CrowdVatIndirectRenderer>();

        if (_renderer == null || !_renderer.TryGetAiDebugSystemSnapshot(out CrowdVatAiDebugSystemSnapshot systemSnapshot))
        {
            payload = default;
            return false;
        }

        SnapshotPayload snapshotPayload = new SnapshotPayload
        {
            providerId = ProviderId,
            objectName = _renderer.name,
            systemSnapshot = systemSnapshot,
            hasSceneQueryFieldAsset = _renderer.SceneQueryFieldAsset != null,
            sceneQueryFieldAssetName = _renderer.SceneQueryFieldAsset != null ? _renderer.SceneQueryFieldAsset.name : string.Empty,
            runtimeSpatialQueryCount = systemSnapshot.activeSpatialQueryCount
        };

        payload = new RenderDocCaptureProviderPayload
        {
            providerId = ProviderId,
            summary = string.Format(
                "instances={0}, visibleGpu={1}, physicsActiveGpu={2}, combatActiveGpu={3}, gridCellCount={4}",
                systemSnapshot.instanceCount,
                systemSnapshot.visibleGpuCount,
                systemSnapshot.physicsActiveGpuCount,
                systemSnapshot.combatActiveGpuCount,
                systemSnapshot.gridCellCount),
            suggestedFileName = "crowd-vat.json",
            jsonPayload = JsonUtility.ToJson(snapshotPayload, true)
        };
        return true;
    }
}
