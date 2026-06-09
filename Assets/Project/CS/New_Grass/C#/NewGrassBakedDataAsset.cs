using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NewGrassBakedData", menuName = "Rendering/New Grass/Baked Data")]
public class NewGrassBakedDataAsset : ScriptableObject
{
    [SerializeField] private Texture2D _sourceDistributionMap;
    [SerializeField] private int _sourceSettingsHash;
    [SerializeField] private int _totalBladeCount;
    [SerializeField] [HideInInspector] private NewGrassBakedClusterInstanceData[] _clusters = Array.Empty<NewGrassBakedClusterInstanceData>();
    [SerializeField] [HideInInspector] private NewGrassBakedClusterCullingCellData[] _cullingCells = Array.Empty<NewGrassBakedClusterCullingCellData>();
    [SerializeField] [HideInInspector] private NewGrassBakedClusterDispatchData[] _dispatches = Array.Empty<NewGrassBakedClusterDispatchData>();

    public Texture2D SourceDistributionMap => _sourceDistributionMap;
    public int SourceSettingsHash => _sourceSettingsHash;
    public int TotalBladeCount => _totalBladeCount;
    public NewGrassBakedClusterInstanceData[] Clusters => _clusters ?? Array.Empty<NewGrassBakedClusterInstanceData>();
    public NewGrassBakedClusterCullingCellData[] CullingCells => _cullingCells ?? Array.Empty<NewGrassBakedClusterCullingCellData>();
    public NewGrassBakedClusterDispatchData[] Dispatches => _dispatches ?? Array.Empty<NewGrassBakedClusterDispatchData>();
    public bool HasData => _clusters != null && _cullingCells != null && _dispatches != null;

    public void SetData(
        Texture2D sourceDistributionMap,
        int sourceSettingsHash,
        int totalBladeCount,
        NewGrassBakedClusterInstanceData[] clusters,
        NewGrassBakedClusterCullingCellData[] cullingCells,
        NewGrassBakedClusterDispatchData[] dispatches)
    {
        _sourceDistributionMap = sourceDistributionMap;
        _sourceSettingsHash = sourceSettingsHash;
        _totalBladeCount = Mathf.Max(0, totalBladeCount);
        _clusters = clusters ?? Array.Empty<NewGrassBakedClusterInstanceData>();
        _cullingCells = cullingCells ?? Array.Empty<NewGrassBakedClusterCullingCellData>();
        _dispatches = dispatches ?? Array.Empty<NewGrassBakedClusterDispatchData>();
    }
}

[Serializable]
public struct NewGrassBakedClusterInstanceData
{
    public Vector2 centerLocalXZ;
    public float sigma;
    public int grassCount;
    public int clumpTypeIndex;
    public float sharedFacingAngle;
    public int startIndex;
}

[Serializable]
public struct NewGrassBakedClusterCullingCellData
{
    public Vector3 minLocal;
    public int dispatchStartIndex;
    public Vector3 maxLocal;
    public int dispatchCount;
}

[Serializable]
public struct NewGrassBakedClusterDispatchData
{
    public int clusterIndex;
    public int sampleOffset;
}
