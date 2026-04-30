using System;
using UnityEngine;

public enum CrowdVatSceneQueryFamily
{
    Actor = 0,
    Ground = 1,
    ObstacleDistance = 2,
    EnvironmentDistance = 4
}

public enum CrowdVatSceneQueryExecutionMode
{
    Unsupported = 0,
    CpuImmediate = 1,
    GpuResident = 2
}

public static class CrowdVatSceneQueryDefaults
{
    public const int RecommendedCpuImmediateBatchCount = 32;
}

[Serializable]
public struct CrowdVatSceneQueryBatchDescriptor
{
    public CrowdVatSceneQueryFamily family;
    public CrowdVatSceneQueryExecutionMode executionMode;
    public int recommendedMaxQueryCount;
}

public enum CrowdVatGroundQueryType
{
    SampleHeight = 0,
    SampleNormal = 1,
    CheckWalkable = 2
}

public enum CrowdVatObstacleDistanceQueryType
{
    SampleSignedDistance = 0,
    SampleGradient = 1,
    CheckInsideObstacle = 2
}

public enum CrowdVatEnvironmentDistanceQueryType
{
    SampleCombinedSignedDistance = 0,
    SampleCombinedGradient = 1,
    SampleDetailed = 2
}

[Flags]
public enum CrowdVatGroundQuerySourceFlags : uint
{
    None = 0,
    Terrain = 1 << 0,
    WalkableMask = 1 << 1,
    GroundFlagsTexture = 1 << 2,
    InBounds = 1 << 3,
    Walkable = 1 << 4
}

[Flags]
public enum CrowdVatObstacleDistanceSourceFlags : uint
{
    None = 0,
    StaticSdf = 1 << 0,
    InBounds = 1 << 1,
    InsideObstacle = 1 << 2,
    ApproximateCpuSample = 1 << 3
}

[Flags]
public enum CrowdVatEnvironmentDistanceSourceFlags : uint
{
    None = 0,
    Terrain = 1 << 0,
    TerrainInBounds = 1 << 1,
    TerrainBelowSurface = 1 << 2,
    StaticSdf = 1 << 3,
    ObstacleInBounds = 1 << 4,
    InsideObstacle = 1 << 5,
    ApproximateCpuSample = 1 << 6,
    CombinedFromTerrain = 1 << 7,
    CombinedFromObstacle = 1 << 8,
    BakedEnvironmentField = 1 << 9
}

[Serializable]
public struct CrowdVatGroundFieldDescriptor
{
    public Terrain terrain;
    public Texture2D walkableMaskTexture;
    public Texture2D groundFlagsTexture;
    public Vector3 worldCenter;
    public Vector2 worldSize;
    public bool useTerrainBoundsForTextures;
    public float terrainHeightOffset;
}

[Serializable]
public struct CrowdVatObstacleDistanceFieldDescriptor
{
    public Texture3D sdfTexture;
    public Vector3 worldCenter;
    public Vector3 worldSize;
    public float distanceScale;
    public float distanceBias;
}

[Serializable]
public struct CrowdVatBakedEnvironmentDistanceFieldDescriptor
{
    public Texture3D sdfTexture;
    public Vector3 worldCenter;
    public Vector3 worldSize;
    public float distanceScale;
    public float distanceBias;
}

[Serializable]
public struct CrowdVatEnvironmentDistanceFieldDescriptor
{
    public CrowdVatBakedEnvironmentDistanceFieldDescriptor bakedField;
    public CrowdVatGroundFieldDescriptor groundField;
    public CrowdVatObstacleDistanceFieldDescriptor obstacleField;
}

[Serializable]
public struct CrowdVatGroundQueryRequest
{
    public int queryId;
    public CrowdVatGroundQueryType queryType;
    public Vector3 worldPosition;
}

[Serializable]
public struct CrowdVatGroundQueryResult
{
    public int queryId;
    public bool valid;
    public float height;
    public Vector3 normal;
    public bool walkable;
    public CrowdVatGroundQuerySourceFlags sourceFlags;
    public uint authoredFlags;
}

[Serializable]
public struct CrowdVatObstacleDistanceQueryRequest
{
    public int queryId;
    public CrowdVatObstacleDistanceQueryType queryType;
    public Vector3 worldPosition;
    public float gradientSampleDistance;
}

[Serializable]
public struct CrowdVatObstacleDistanceQueryResult
{
    public int queryId;
    public bool valid;
    public float signedDistance;
    public Vector3 gradient;
    public bool inside;
    public CrowdVatObstacleDistanceSourceFlags sourceFlags;
}

[Serializable]
public struct CrowdVatEnvironmentDistanceQueryRequest
{
    public int queryId;
    public CrowdVatEnvironmentDistanceQueryType queryType;
    public Vector3 worldPosition;
    public float gradientSampleDistance;
}

[Serializable]
public struct CrowdVatEnvironmentDistanceQueryResult
{
    public int queryId;
    public bool valid;
    public bool hasTerrain;
    public float terrainHeight;
    public float terrainSignedDistance;
    public Vector3 terrainGradient;
    public bool hasObstacle;
    public float obstacleSignedDistance;
    public Vector3 obstacleGradient;
    public float combinedSignedDistance;
    public Vector3 combinedGradient;
    public bool inside;
    public CrowdVatEnvironmentDistanceSourceFlags sourceFlags;
}
