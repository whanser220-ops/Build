using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class CrowdVatIndirectRenderer : MonoBehaviour
{
    private void Reset()
    {
        SyncTemplateBindings();
        MarkResourcesDirty();
    }

    private void Awake()
    {
        SyncTemplateBindings();
        InitializeIfNeeded();
    }

    private void OnEnable()
    {
        GpuSemanticDrawRegistry.Register(this);
        SubscribeRenderCallbacks();
        SyncTemplateBindings();
        InitializeIfNeeded();

        if (_playOnEnable)
            PlayClip(_clipName);
    }

    private void LateUpdate()
    {
        if (!InitializeIfNeeded())
            return;

        AdvancePlayback(Time.deltaTime);
        UpdateGpuBuffers();
    }

    private void OnDisable()
    {
        GpuSemanticDrawRegistry.Unregister(this);
        UnsubscribeRenderCallbacks();
        ReleaseResources();
    }

    private void OnDestroy()
    {
        GpuSemanticDrawRegistry.Unregister(this);
        UnsubscribeRenderCallbacks();
        ReleaseResources();
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!ShouldRenderForCamera(camera) || !InitializeIfNeeded())
            return;

        if (!Application.isPlaying)
            UpdateGpuBuffers();

        if (ShouldUseGpuSemanticDrawPass(camera))
        {
            PrepareGpuSemanticDrawState(camera);
            return;
        }

        Draw(camera);
    }

    private void OnDrawGizmosSelected()
    {
    }

    private void OnValidate()
    {
        _instanceCount = Mathf.Max(1, _instanceCount);
        _areaSize.x = Mathf.Max(0.01f, _areaSize.x);
        _areaSize.y = Mathf.Max(0.01f, _areaSize.y);
        _campAAreaSize.x = Mathf.Max(0.01f, _campAAreaSize.x);
        _campAAreaSize.y = Mathf.Max(0.01f, _campAAreaSize.y);
        _campBAreaSize.x = Mathf.Max(0.01f, _campBAreaSize.x);
        _campBAreaSize.y = Mathf.Max(0.01f, _campBAreaSize.y);
        _baseScale = Mathf.Max(0.01f, _baseScale);
        _scaleMultiplierRange.x = Mathf.Max(0.01f, _scaleMultiplierRange.x);
        _scaleMultiplierRange.y = Mathf.Max(_scaleMultiplierRange.x, _scaleMultiplierRange.y);
        _playbackSpeedMultiplierRange.x = Mathf.Max(0.01f, _playbackSpeedMultiplierRange.x);
        _playbackSpeedMultiplierRange.y = Mathf.Max(_playbackSpeedMultiplierRange.x, _playbackSpeedMultiplierRange.y);
        _collisionRadius = Mathf.Max(0.01f, _collisionRadius);
        _collisionHeight = Mathf.Max(_collisionRadius * 2.0f, _collisionHeight);
        _staticSdfWorldSize.x = Mathf.Max(0.01f, _staticSdfWorldSize.x);
        _staticSdfWorldSize.y = Mathf.Max(0.01f, _staticSdfWorldSize.y);
        _staticSdfWorldSize.z = Mathf.Max(0.01f, _staticSdfWorldSize.z);
        _queryCellSize = Mathf.Max(0.05f, _queryCellSize);
        _simulationBoundsPadding = Mathf.Max(0.0f, _simulationBoundsPadding);
        _maxCellOccupancy = Mathf.Max(4, _maxCellOccupancy);
        _maxSpatialQueries = Mathf.Max(1, _maxSpatialQueries);
        _maxHitsPerSpatialQuery = Mathf.Max(1, _maxHitsPerSpatialQuery);
        _solverIterations = Mathf.Max(1, _solverIterations);
        _capsulePbdSampleCount = Mathf.Clamp(_capsulePbdSampleCount, 2, 8);
        _selfCollisionSlop = Mathf.Max(0.0f, _selfCollisionSlop);
        _selfCollisionMaxPushPerStep = Mathf.Max(0.01f, _selfCollisionMaxPushPerStep);
        _goalStiffness = Mathf.Clamp01(_goalStiffness);
        _localAvoidanceRadius = Mathf.Max(0.05f, _localAvoidanceRadius);
        _localAvoidanceMaxNeighbors = Mathf.Max(1, _localAvoidanceMaxNeighbors);
        _maxPushPerStep = Mathf.Max(0.01f, _maxPushPerStep);
        _maxDisplacementFromSpawn = Mathf.Max(0.01f, _maxDisplacementFromSpawn);
        _factionCenterGap = Mathf.Max(0.0f, _factionCenterGap);
        _inactiveReturnStrength = Mathf.Clamp01(_inactiveReturnStrength);
        _characterInteractionRadiusMultiplier = Mathf.Max(0.1f, _characterInteractionRadiusMultiplier);
        _characterInteractionStrength = Mathf.Max(0.0f, _characterInteractionStrength);
        _combatRange = Mathf.Max(0.1f, _combatRange);
        _combatShotsPerSecond = Mathf.Max(0.0f, _combatShotsPerSecond);
        _combatShotIntervalJitter = Mathf.Clamp01(_combatShotIntervalJitter);
        _combatShotSpreadDegrees = Mathf.Max(0.0f, _combatShotSpreadDegrees);
        _combatHitRadiusPadding = Mathf.Max(0.0f, _combatHitRadiusPadding);
        _combatHitHeightPadding = Mathf.Max(0.0f, _combatHitHeightPadding);
        _combatTerrainOcclusionSampleSpacing = Mathf.Max(0.1f, _combatTerrainOcclusionSampleSpacing);
        _combatTerrainOcclusionClearance = Mathf.Max(0.0f, _combatTerrainOcclusionClearance);
        _combatEnvironmentOcclusionMaxSteps = Mathf.Clamp(_combatEnvironmentOcclusionMaxSteps, 1, 96);
        _combatOriginHeight = Mathf.Max(0.0f, _combatOriginHeight);
        _combatTargetHeight = Mathf.Max(0.0f, _combatTargetHeight);
        _combatMuzzleFlashDecay = Mathf.Max(0.0f, _combatMuzzleFlashDecay);
        _combatMaxHealth = Mathf.Max(1.0f, _combatMaxHealth);
        _combatDamagePerHit = Mathf.Max(0.0f, _combatDamagePerHit);
        _combatHitFlashDecay = Mathf.Max(0.0f, _combatHitFlashDecay);
        _combatOccludedTargetRetryDelay = Mathf.Max(0.0f, _combatOccludedTargetRetryDelay);
        _combatNoTargetRetryDelay = Mathf.Max(0.0f, _combatNoTargetRetryDelay);
        _targetAcquisitionSearchIntervalMin = Mathf.Max(0.02f, _targetAcquisitionSearchIntervalMin);
        _targetAcquisitionSearchIntervalMax = Mathf.Max(_targetAcquisitionSearchIntervalMin, _targetAcquisitionSearchIntervalMax);
        _targetAcquisitionFovDegrees = Mathf.Clamp(_targetAcquisitionFovDegrees, 1.0f, 360.0f);
        _targetAcquisitionMaxCandidateChecks = Mathf.Max(1, _targetAcquisitionMaxCandidateChecks);
        _combatCandidateCapacityPerSquad = Mathf.Clamp(_combatCandidateCapacityPerSquad, 1, CombatCandidateCapacityPerSquadMax);
        _targetAcquisitionCurrentTargetBonus = Mathf.Max(0.0f, _targetAcquisitionCurrentTargetBonus);
        _targetAcquisitionLastAttackerBonus = Mathf.Max(0.0f, _targetAcquisitionLastAttackerBonus);
        _targetAcquisitionLockDuration = Mathf.Max(0.0f, _targetAcquisitionLockDuration);
        _targetAcquisitionLostSightGrace = Mathf.Max(0.0f, _targetAcquisitionLostSightGrace);
        _targetAcquisitionLineOfSightRecheckInterval = Mathf.Max(0.0f, _targetAcquisitionLineOfSightRecheckInterval);
        _targetAcquisitionDistanceScoreWeight = Mathf.Max(0.0f, _targetAcquisitionDistanceScoreWeight);
        _targetAcquisitionViewScoreWeight = Mathf.Max(0.0f, _targetAcquisitionViewScoreWeight);
        _combatTracerWidth = Mathf.Max(0.001f, _combatTracerWidth);
        _combatTracerBrightness = Mathf.Max(0.0f, _combatTracerBrightness);
        _combatTracerMinFlash = Mathf.Clamp01(_combatTracerMinFlash);
        _combatMuzzleFlashSize = Mathf.Max(0.001f, _combatMuzzleFlashSize);
        _combatImpactFlashSize = Mathf.Max(0.001f, _combatImpactFlashSize);
        _combatImpactFlashDuration = Mathf.Max(0.01f, _combatImpactFlashDuration);
        _combatImpactPointOffset = Mathf.Max(0.0f, _combatImpactPointOffset);
        _combatMuzzleFlashBrightness = Mathf.Max(0.0f, _combatMuzzleFlashBrightness);
        _combatImpactFlashBrightness = Mathf.Max(0.0f, _combatImpactFlashBrightness);
        _combatMuzzleFlashFlipbookColumns = Mathf.Max(1, _combatMuzzleFlashFlipbookColumns);
        _combatMuzzleFlashFlipbookRows = Mathf.Max(1, _combatMuzzleFlashFlipbookRows);
        _combatImpactFlashFlipbookColumns = Mathf.Max(1, _combatImpactFlashFlipbookColumns);
        _combatImpactFlashFlipbookRows = Mathf.Max(1, _combatImpactFlashFlipbookRows);
#if UNITY_EDITOR
        ResolveDefaultCombatFxTextures();
#endif
        _secondaryLodStartDistance = Mathf.Max(0.0f, _secondaryLodStartDistance);
        _tertiaryLodStartDistance = Mathf.Max(_secondaryLodStartDistance, _tertiaryLodStartDistance);
        _meshRootLocalScale.x = Mathf.Max(0.001f, _meshRootLocalScale.x);
        _meshRootLocalScale.y = Mathf.Max(0.001f, _meshRootLocalScale.y);
        _meshRootLocalScale.z = Mathf.Max(0.001f, _meshRootLocalScale.z);
        SyncExplicitFactionAreasFromControlTransforms();
        SyncExplicitFactionCentersFromControlTransforms();

        SyncTemplateBindings();
        MarkResourcesDirty();
    }

    [ContextMenu("Rebuild Crowd Layout")]
    public void RebuildCrowdLayout()
    {
        MarkResourcesDirty();
        InitializeIfNeeded();
    }

    public bool UseFactionControlTransforms => _useFactionControlTransforms;

    public CrowdVatFactionLayoutMode FactionLayoutMode => _factionLayout;
    public bool HasRuntimeSquadAnchors => HasRuntimeSquadAnchorData();
    public bool GpuInstanceCombatEnabled => _enableGpuInstanceCombat;
    public int InstanceCount => _instanceCount;
    public float MaximumAgentCollisionRadius => Mathf.Max(
        0.01f,
        _collisionRadius * Mathf.Max(_baseScale, 0.01f) * Mathf.Max(Mathf.Max(_scaleMultiplierRange.x, _scaleMultiplierRange.y), 0.01f));
    public CrowdVatAnimationAsset AnimationAsset => ResolveConfiguredAnimationAsset();
    public CrowdVatSceneQueryFieldAsset SceneQueryFieldAsset => _sceneQueryFieldAsset;

    public bool TryGetDebugCombatSquadCandidateRadius(float squadCohesionRadius, out float radius)
    {
        float sourceRadius = Mathf.Max(
            Mathf.Max(Mathf.Max(squadCohesionRadius, _maxDisplacementFromSpawn), _collisionRadius),
            0.0f);
        float targetPadding = Mathf.Max(Mathf.Max(_maximumQueryAgentRadius, MaximumAgentCollisionRadius), _collisionRadius);
        radius = sourceRadius + Mathf.Max(_combatRange, 0.0f) + targetPadding;
        return radius > 0.0f;
    }

    public Transform CampAControlTransform => _campAControlTransform;

    public Transform CampBControlTransform => _campBControlTransform;

    public bool TryGetDebugSpawnArea(out Vector2 centerXZ, out Vector2 sizeXZ)
    {
        SpawnAreaContext spawnArea = ResolveSpawnAreaContext();
        centerXZ = spawnArea.centerXZ;
        sizeXZ = spawnArea.size;
        return sizeXZ.x > 0.0f && sizeXZ.y > 0.0f;
    }

    public bool TryGetDebugFactionArea(CrowdVatFaction faction, out Vector2 centerXZ, out Vector2 sizeXZ)
    {
        centerXZ = Vector2.zero;
        sizeXZ = Vector2.zero;
        if (_factionLayout != CrowdVatFactionLayoutMode.TwoOpposingFactions)
            return false;

        SpawnAreaContext spawnArea = ResolveSpawnAreaContext();
        centerXZ = GetFactionCenterXZ(spawnArea, faction);
        sizeXZ = GetFactionAreaSize(spawnArea.size, faction);
        return sizeXZ.x > 0.0f && sizeXZ.y > 0.0f;
    }

    public bool TryGetDebugGridBounds(out Vector2 minXZ, out Vector2 maxXZ, out Vector2Int dimensions)
    {
        return TryGetDebugGridBounds(out minXZ, out maxXZ, out dimensions, out _);
    }

    public bool TryGetDebugGridBounds(out Vector2 minXZ, out Vector2 maxXZ, out Vector2Int dimensions, out float cellSize)
    {
        minXZ = _gridMinXZ;
        maxXZ = _gridMaxXZ;
        dimensions = _gridDimensions;
        cellSize = _queryCellSize;
        return _gridCellCount > 0 && dimensions.x > 0 && dimensions.y > 0 && cellSize > 0.0f;
    }

    public bool TryGetGroundQueryFieldDescriptor(out CrowdVatGroundFieldDescriptor descriptor)
    {
        if (!TryResolveSceneQueryTerrain(out Terrain terrain))
            terrain = null;

        if (_sceneQueryFieldAsset != null &&
            _sceneQueryFieldAsset.TryGetGroundFieldDescriptor(out descriptor))
        {
            descriptor.terrain = terrain;
            descriptor.terrainHeightOffset = _terrainHeightOffset;
            return descriptor.terrain != null ||
                descriptor.walkableMaskTexture != null ||
                descriptor.groundFlagsTexture != null;
        }

        if (terrain == null)
        {
            descriptor = default;
            return false;
        }

        Vector3 terrainSize = terrain.terrainData.size;
        descriptor = new CrowdVatGroundFieldDescriptor
        {
            terrain = terrain,
            walkableMaskTexture = null,
            groundFlagsTexture = null,
            worldCenter = terrain.transform.position + new Vector3(terrainSize.x * 0.5f, 0.0f, terrainSize.z * 0.5f),
            worldSize = new Vector2(terrainSize.x, terrainSize.z),
            useTerrainBoundsForTextures = true,
            terrainHeightOffset = _terrainHeightOffset
        };
        return true;
    }

    public bool TryGetObstacleDistanceFieldDescriptor(out CrowdVatObstacleDistanceFieldDescriptor descriptor)
    {
        return TryResolveSceneQueryObstacleDistanceField(out descriptor);
    }

    public bool TryGetEnvironmentDistanceFieldDescriptor(out CrowdVatEnvironmentDistanceFieldDescriptor descriptor)
    {
        bool hasBakedEnvironmentDistanceField = TryResolveSceneQueryBakedEnvironmentDistanceField(out CrowdVatBakedEnvironmentDistanceFieldDescriptor bakedDescriptor);
        bool hasTerrainDistanceField = TryGetGroundQueryFieldDescriptor(out CrowdVatGroundFieldDescriptor groundDescriptor) &&
            groundDescriptor.terrain != null;
        bool hasObstacleDistanceField = TryResolveSceneQueryObstacleDistanceField(out CrowdVatObstacleDistanceFieldDescriptor obstacleDescriptor);
        descriptor = new CrowdVatEnvironmentDistanceFieldDescriptor
        {
            bakedField = hasBakedEnvironmentDistanceField ? bakedDescriptor : default,
            groundField = hasTerrainDistanceField ? groundDescriptor : default,
            obstacleField = hasObstacleDistanceField ? obstacleDescriptor : default
        };
        return hasBakedEnvironmentDistanceField || hasTerrainDistanceField || hasObstacleDistanceField;
    }

    public bool TryGetGroundQueryBatchDescriptor(out CrowdVatSceneQueryBatchDescriptor descriptor)
    {
        if (!TryGetGroundQueryFieldDescriptor(out _))
        {
            descriptor = new CrowdVatSceneQueryBatchDescriptor
            {
                family = CrowdVatSceneQueryFamily.Ground,
                executionMode = CrowdVatSceneQueryExecutionMode.Unsupported,
                recommendedMaxQueryCount = 0
            };
            return false;
        }

        descriptor = new CrowdVatSceneQueryBatchDescriptor
        {
            family = CrowdVatSceneQueryFamily.Ground,
            executionMode = CrowdVatSceneQueryExecutionMode.CpuImmediate,
            recommendedMaxQueryCount = CrowdVatSceneQueryDefaults.RecommendedCpuImmediateBatchCount
        };
        return true;
    }

    public bool TryGetObstacleDistanceQueryBatchDescriptor(out CrowdVatSceneQueryBatchDescriptor descriptor)
    {
        if (!TryResolveSceneQueryObstacleDistanceField(out _))
        {
            descriptor = new CrowdVatSceneQueryBatchDescriptor
            {
                family = CrowdVatSceneQueryFamily.ObstacleDistance,
                executionMode = CrowdVatSceneQueryExecutionMode.Unsupported,
                recommendedMaxQueryCount = 0
            };
            return false;
        }

        descriptor = new CrowdVatSceneQueryBatchDescriptor
        {
            family = CrowdVatSceneQueryFamily.ObstacleDistance,
            executionMode = CrowdVatSceneQueryExecutionMode.CpuImmediate,
            recommendedMaxQueryCount = CrowdVatSceneQueryDefaults.RecommendedCpuImmediateBatchCount
        };
        return true;
    }

    public bool TryGetEnvironmentDistanceQueryBatchDescriptor(out CrowdVatSceneQueryBatchDescriptor descriptor)
    {
        if (!TryGetEnvironmentDistanceFieldDescriptor(out _))
        {
            descriptor = new CrowdVatSceneQueryBatchDescriptor
            {
                family = CrowdVatSceneQueryFamily.EnvironmentDistance,
                executionMode = CrowdVatSceneQueryExecutionMode.Unsupported,
                recommendedMaxQueryCount = 0
            };
            return false;
        }

        descriptor = new CrowdVatSceneQueryBatchDescriptor
        {
            family = CrowdVatSceneQueryFamily.EnvironmentDistance,
            executionMode = CrowdVatSceneQueryExecutionMode.CpuImmediate,
            recommendedMaxQueryCount = CrowdVatSceneQueryDefaults.RecommendedCpuImmediateBatchCount
        };
        return true;
    }

    public bool TrySampleGroundQuery(CrowdVatGroundQueryRequest request, out CrowdVatGroundQueryResult result)
    {
        if (!TryGetGroundQueryFieldDescriptor(out CrowdVatGroundFieldDescriptor descriptor))
        {
            result = CreateUnavailableGroundQueryResult(request);
            return false;
        }

        return CrowdVatSceneQueryUtility.TrySampleGround(descriptor, request, out result);
    }

    public bool TrySampleObstacleDistanceQuery(CrowdVatObstacleDistanceQueryRequest request, out CrowdVatObstacleDistanceQueryResult result)
    {
        if (!TryResolveSceneQueryObstacleDistanceField(out CrowdVatObstacleDistanceFieldDescriptor descriptor))
        {
            result = CreateUnavailableObstacleDistanceQueryResult(request);
            return false;
        }

        return CrowdVatSceneQueryUtility.TrySampleObstacleDistance(descriptor, request, out result);
    }

    public bool TrySampleEnvironmentDistanceQuery(CrowdVatEnvironmentDistanceQueryRequest request, out CrowdVatEnvironmentDistanceQueryResult result)
    {
        if (!TryGetEnvironmentDistanceFieldDescriptor(out CrowdVatEnvironmentDistanceFieldDescriptor descriptor))
        {
            result = CreateUnavailableEnvironmentDistanceQueryResult(request);
            return false;
        }

        return CrowdVatSceneQueryUtility.TrySampleEnvironmentDistance(descriptor, request, out result);
    }

    public int EvaluateGroundQueries(CrowdVatGroundQueryRequest[] requests, List<CrowdVatGroundQueryResult> results)
    {
        if (requests == null)
            throw new ArgumentNullException(nameof(requests));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (requests.Length == 0)
            return 0;

        if (!TryGetGroundQueryFieldDescriptor(out CrowdVatGroundFieldDescriptor descriptor))
        {
            for (int index = 0; index < requests.Length; index++)
                results.Add(CreateUnavailableGroundQueryResult(requests[index]));

            return 0;
        }

        return CrowdVatSceneQueryUtility.EvaluateGroundQueries(descriptor, requests, results);
    }

    public int EvaluateObstacleDistanceQueries(CrowdVatObstacleDistanceQueryRequest[] requests, List<CrowdVatObstacleDistanceQueryResult> results)
    {
        if (requests == null)
            throw new ArgumentNullException(nameof(requests));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (requests.Length == 0)
            return 0;

        if (!TryResolveSceneQueryObstacleDistanceField(out CrowdVatObstacleDistanceFieldDescriptor descriptor))
        {
            for (int index = 0; index < requests.Length; index++)
                results.Add(CreateUnavailableObstacleDistanceQueryResult(requests[index]));

            return 0;
        }

        return CrowdVatSceneQueryUtility.EvaluateObstacleDistanceQueries(descriptor, requests, results);
    }

    public int EvaluateEnvironmentDistanceQueries(CrowdVatEnvironmentDistanceQueryRequest[] requests, List<CrowdVatEnvironmentDistanceQueryResult> results)
    {
        if (requests == null)
            throw new ArgumentNullException(nameof(requests));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (requests.Length == 0)
            return 0;

        if (!TryGetEnvironmentDistanceFieldDescriptor(out CrowdVatEnvironmentDistanceFieldDescriptor descriptor))
        {
            for (int index = 0; index < requests.Length; index++)
                results.Add(CreateUnavailableEnvironmentDistanceQueryResult(requests[index]));

            return 0;
        }

        return CrowdVatSceneQueryUtility.EvaluateEnvironmentDistanceQueries(descriptor, requests, results);
    }

    public bool TryGetInstanceCombatState(int instanceIndex, out CrowdVatInstanceCombatState state)
    {
        state = default;
        if (instanceIndex < 0 ||
            instanceIndex >= _instanceCount ||
            !TryReadCombatStateBuffer())
        {
            return false;
        }

        TryReadPhysicsActiveStateBuffer();
        state = ConvertCombatStateData(instanceIndex, _combatStateReadbackCache[instanceIndex]);
        return true;
    }

    public int ReadInstanceCombatStates(List<CrowdVatInstanceCombatState> states)
    {
        if (states == null)
            throw new ArgumentNullException(nameof(states));

        states.Clear();
        if (!TryReadCombatStateBuffer())
            return 0;

        TryReadPhysicsActiveStateBuffer();
        int count = Mathf.Min(_instanceCount, _combatStateReadbackCache.Length);
        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
            states.Add(ConvertCombatStateData(instanceIndex, _combatStateReadbackCache[instanceIndex]));

        return count;
    }

    private static CrowdVatGroundQueryResult CreateUnavailableGroundQueryResult(CrowdVatGroundQueryRequest request)
    {
        return new CrowdVatGroundQueryResult
        {
            queryId = request.queryId,
            valid = false,
            height = request.worldPosition.y,
            normal = Vector3.up,
            walkable = false,
            sourceFlags = CrowdVatGroundQuerySourceFlags.None,
            authoredFlags = 0u
        };
    }

    private static CrowdVatObstacleDistanceQueryResult CreateUnavailableObstacleDistanceQueryResult(CrowdVatObstacleDistanceQueryRequest request)
    {
        return new CrowdVatObstacleDistanceQueryResult
        {
            queryId = request.queryId,
            valid = false,
            signedDistance = float.PositiveInfinity,
            gradient = Vector3.zero,
            inside = false,
            sourceFlags = CrowdVatObstacleDistanceSourceFlags.None
        };
    }

    private static CrowdVatEnvironmentDistanceQueryResult CreateUnavailableEnvironmentDistanceQueryResult(CrowdVatEnvironmentDistanceQueryRequest request)
    {
        return new CrowdVatEnvironmentDistanceQueryResult
        {
            queryId = request.queryId,
            valid = false,
            hasTerrain = false,
            terrainHeight = request.worldPosition.y,
            terrainSignedDistance = float.PositiveInfinity,
            terrainGradient = Vector3.up,
            hasObstacle = false,
            obstacleSignedDistance = float.PositiveInfinity,
            obstacleGradient = Vector3.zero,
            combinedSignedDistance = float.PositiveInfinity,
            combinedGradient = Vector3.zero,
            inside = false,
            sourceFlags = CrowdVatEnvironmentDistanceSourceFlags.None
        };
    }

    public int GetDebugSpatialQueries(List<CrowdVatSpatialQueryRequest> queries)
    {
        if (queries == null)
            throw new ArgumentNullException(nameof(queries));

        queries.Clear();

        AppendDebugInteractionSphereQueries(queries);

        if (_activeSpatialQueryCount <= 0 || _runtimeSpatialQueries == null)
            return queries.Count;

        int count = Mathf.Min(_activeSpatialQueryCount, _runtimeSpatialQueries.Length);
        for (int index = 0; index < count; index++)
            queries.Add(_runtimeSpatialQueries[index]);

        return queries.Count;
    }

    public void SetFactionControlTransforms(Transform campAControlTransform, Transform campBControlTransform)
    {
        _campAControlTransform = campAControlTransform;
        _campBControlTransform = campBControlTransform;
        _useFactionControlTransforms = _campAControlTransform != null || _campBControlTransform != null;
        SyncExplicitFactionCentersFromControlTransforms();
    }

    public void SetFactionRangeControlTransforms(
        Transform campANorthWestCornerTransform,
        Transform campANorthEastCornerTransform,
        Transform campASouthEastCornerTransform,
        Transform campASouthWestCornerTransform,
        Transform campBNorthWestCornerTransform,
        Transform campBNorthEastCornerTransform,
        Transform campBSouthEastCornerTransform,
        Transform campBSouthWestCornerTransform)
    {
        _campANorthWestCornerTransform = campANorthWestCornerTransform;
        _campANorthEastCornerTransform = campANorthEastCornerTransform;
        _campASouthEastCornerTransform = campASouthEastCornerTransform;
        _campASouthWestCornerTransform = campASouthWestCornerTransform;
        _campBNorthWestCornerTransform = campBNorthWestCornerTransform;
        _campBNorthEastCornerTransform = campBNorthEastCornerTransform;
        _campBSouthEastCornerTransform = campBSouthEastCornerTransform;
        _campBSouthWestCornerTransform = campBSouthWestCornerTransform;
        SyncExplicitFactionAreasFromControlTransforms();
    }

    public void SyncExplicitFactionCentersFromControlTransforms()
    {
        bool updated = false;
        if (TryGetFactionControlCenterXZ(CrowdVatFaction.CampA, out Vector2 campACenter))
        {
            _campACenterXZ = campACenter;
            updated = true;
        }
        else if (TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction.CampA, out Vector2 campARangeCenter, out _))
        {
            _campACenterXZ = campARangeCenter;
            updated = true;
        }

        if (TryGetFactionControlCenterXZ(CrowdVatFaction.CampB, out Vector2 campBCenter))
        {
            _campBCenterXZ = campBCenter;
            updated = true;
        }
        else if (TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction.CampB, out Vector2 campBRangeCenter, out _))
        {
            _campBCenterXZ = campBRangeCenter;
            updated = true;
        }

        if (updated)
            _useExplicitFactionCenters = true;
    }

    public void SyncExplicitFactionAreasFromControlTransforms()
    {
        bool updated = false;
        if (TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction.CampA, out _, out Vector2 campAAreaSize))
        {
            _campAAreaSize = campAAreaSize;
            updated = true;
        }

        if (TryGetFactionRangeBoundsFromCornerTransforms(CrowdVatFaction.CampB, out _, out Vector2 campBAreaSize))
        {
            _campBAreaSize = campBAreaSize;
            updated = true;
        }

        if (updated)
            _useExplicitFactionAreaSizes = true;
    }

    public void SyncFactionControlTransformsFromExplicitCenters()
    {
        SyncFactionControlTransform(_campAControlTransform, _campACenterXZ);
        SyncFactionControlTransform(_campBControlTransform, _campBCenterXZ);
    }

    public void SyncFactionRangeControlTransformsFromExplicitAreas()
    {
        SyncFactionRangeCornerTransforms(
            _campAControlTransform,
            _campACenterXZ,
            _campAAreaSize,
            _campANorthWestCornerTransform,
            _campANorthEastCornerTransform,
            _campASouthEastCornerTransform,
            _campASouthWestCornerTransform);
        SyncFactionRangeCornerTransforms(
            _campBControlTransform,
            _campBCenterXZ,
            _campBAreaSize,
            _campBNorthWestCornerTransform,
            _campBNorthEastCornerTransform,
            _campBSouthEastCornerTransform,
            _campBSouthWestCornerTransform);
    }

    public void NotifyFactionControlTransformsChanged()
    {
        SyncExplicitFactionCentersFromControlTransforms();
        SyncExplicitFactionAreasFromControlTransforms();
        SyncFactionControlTransformsFromExplicitCenters();
        SyncFactionRangeControlTransformsFromExplicitAreas();
        MarkFactionControlStateDirtyAndRebuild();
    }

    public void NotifyFactionControlPointChanged(CrowdVatFaction faction, CrowdVatFactionControlPointKind kind, Transform controlPointTransform)
    {
        if (kind == CrowdVatFactionControlPointKind.Center)
        {
            NotifyFactionControlTransformsChanged();
            return;
        }

        SyncExplicitFactionCentersFromControlTransforms();
        if (!TrySyncExplicitFactionAreaFromMovedCorner(faction, controlPointTransform))
            SyncExplicitFactionAreasFromControlTransforms();

        SyncFactionControlTransformsFromExplicitCenters();
        SyncFactionRangeControlTransformsFromExplicitAreas();
        MarkFactionControlStateDirtyAndRebuild();
    }

    private void MarkFactionControlStateDirtyAndRebuild()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
        RebuildCrowdLayout();
    }

    public bool TryGetClipIndex(string clipName, out int clipIndex)
    {
        CrowdVatAnimationAsset animationAsset = ResolveConfiguredAnimationAsset();
        if (animationAsset == null)
        {
            clipIndex = InvalidClipIndex;
            return false;
        }

        return animationAsset.TryGetClipIndex(clipName, out clipIndex);
    }

    public void PlayClip(string clipName)
    {
        SyncTemplateBindings();
        if (!TryResolveClip(clipName, out int clipIndex, out CrowdVatAnimationAsset.ClipInfo clip))
        {
            _currentClipIndex = InvalidClipIndex;
            _hasClip = false;
            return;
        }

        _currentClipIndex = clipIndex;
        _currentClip = clip;
        _hasClip = true;
        _isPlaying = true;
        _playbackTime = 0.0f;
        _clipName = clip.Name;

        if (_instanceAnimationStateCpuCache != null && _instanceAnimationStateCpuCache.Length == _instanceCount)
            TrySetAllInstancesAnimation(clip.Name, 0.0f, 0.0f);
    }

    public void Stop()
    {
        _isPlaying = false;
    }

    public void Resume()
    {
        if (_hasClip)
            _isPlaying = true;
    }

    public bool TrySetInstanceAnimation(
        int instanceIndex,
        string clipName,
        float transitionDuration = -1.0f,
        float normalizedTime = 0.0f)
    {
        if (instanceIndex < 0 || instanceIndex >= _instanceCount)
            return false;

        if (!TryResolveClip(clipName, out int clipIndex, out CrowdVatAnimationAsset.ClipInfo clip))
            return false;

        if (_instanceAnimationStateCpuCache == null || _instanceAnimationStateCpuCache.Length != _instanceCount)
            InitializeIfNeeded();

        if (_instanceAnimationStateCpuCache == null || instanceIndex >= _instanceAnimationStateCpuCache.Length)
            return false;

        ApplyInstanceAnimationState(instanceIndex, clipIndex, clip, ResolveTransitionDuration(transitionDuration), normalizedTime);
        return true;
    }

    public bool TrySetInstancesAnimation(
        IReadOnlyList<int> instanceIndices,
        string clipName,
        float transitionDuration = -1.0f,
        float normalizedTime = 0.0f)
    {
        if (instanceIndices == null || instanceIndices.Count == 0)
            return false;

        if (!TryResolveClip(clipName, out int clipIndex, out CrowdVatAnimationAsset.ClipInfo clip))
            return false;

        if (_instanceAnimationStateCpuCache == null || _instanceAnimationStateCpuCache.Length != _instanceCount)
            InitializeIfNeeded();

        if (_instanceAnimationStateCpuCache == null || _instanceAnimationStateCpuCache.Length != _instanceCount)
            return false;

        bool changedAny = false;
        float resolvedTransitionDuration = ResolveTransitionDuration(transitionDuration);
        for (int i = 0; i < instanceIndices.Count; i++)
        {
            int instanceIndex = instanceIndices[i];
            if (instanceIndex < 0 || instanceIndex >= _instanceAnimationStateCpuCache.Length)
                continue;

            ApplyInstanceAnimationState(instanceIndex, clipIndex, clip, resolvedTransitionDuration, normalizedTime);
            changedAny = true;
        }

        return changedAny;
    }

    public bool TrySetAllInstancesAnimation(
        string clipName,
        float transitionDuration = -1.0f,
        float normalizedTime = 0.0f)
    {
        if (!TryResolveClip(clipName, out int clipIndex, out CrowdVatAnimationAsset.ClipInfo clip))
            return false;

        if (_instanceAnimationStateCpuCache == null || _instanceAnimationStateCpuCache.Length != _instanceCount)
            InitializeIfNeeded();

        _currentClipIndex = clipIndex;
        _currentClip = clip;
        _clipName = clip.Name;
        _hasClip = true;

        if (_instanceAnimationStateCpuCache == null || _instanceAnimationStateCpuCache.Length != _instanceCount)
            return true;

        float resolvedTransitionDuration = ResolveTransitionDuration(transitionDuration);
        for (int instanceIndex = 0; instanceIndex < _instanceAnimationStateCpuCache.Length; instanceIndex++)
            ApplyInstanceAnimationState(instanceIndex, clipIndex, clip, resolvedTransitionDuration, normalizedTime);

        return true;
    }

    private float ResolveTransitionDuration(float transitionDuration)
    {
        return transitionDuration < 0.0f
            ? DefaultInstanceAnimationTransitionDuration
            : Mathf.Max(0.0f, transitionDuration);
    }

    private void ApplyInstanceAnimationState(
        int instanceIndex,
        int clipIndex,
        CrowdVatAnimationAsset.ClipInfo clip,
        float transitionDuration,
        float normalizedTime)
    {
        InstanceAnimationStateCpuData state = _instanceAnimationStateCpuCache[instanceIndex];
        normalizedTime = Mathf.Clamp01(normalizedTime);
        float clipLength = Mathf.Max(clip.LengthSeconds, 0.0f);
        float targetTime = clipLength <= Mathf.Epsilon
            ? 0.0f
            : normalizedTime * clipLength;

        state = CollapseAnimationStateForRetarget(state);

        if (state.currentClipIndex == InvalidClipIndex || transitionDuration <= Mathf.Epsilon || state.currentClipIndex == clipIndex)
        {
            state.currentClipIndex = clipIndex;
            state.nextClipIndex = clipIndex;
            state.currentClipTime = targetTime;
            state.nextClipTime = targetTime;
            state.transitionElapsed = 0.0f;
            state.transitionDuration = 0.0f;
            state.isBlending = false;
            _instanceAnimationStateCpuCache[instanceIndex] = state;
            return;
        }

        state.nextClipIndex = clipIndex;
        state.nextClipTime = targetTime;
        state.transitionElapsed = 0.0f;
        state.transitionDuration = transitionDuration;
        state.isBlending = true;
        _instanceAnimationStateCpuCache[instanceIndex] = state;
    }

    private InstanceAnimationStateCpuData CollapseAnimationStateForRetarget(InstanceAnimationStateCpuData state)
    {
        if (!state.isBlending)
            return state;

        bool promoteNextClip = state.transitionDuration <= Mathf.Epsilon ||
            Mathf.Clamp01(state.transitionElapsed / Mathf.Max(state.transitionDuration, 1e-5f)) >= 0.5f;

        if (promoteNextClip && state.nextClipIndex != InvalidClipIndex)
        {
            state.currentClipIndex = state.nextClipIndex;
            state.currentClipTime = state.nextClipTime;
        }

        state.nextClipIndex = state.currentClipIndex;
        state.nextClipTime = state.currentClipTime;
        state.transitionElapsed = 0.0f;
        state.transitionDuration = 0.0f;
        state.isBlending = false;
        return state;
    }

    public void SetSpatialQueries(CrowdVatSpatialQueryRequest[] queries)
    {
        _legacyFramePacketScratch.Reset();
        _legacyFramePacketScratch.frameParams = BuildRendererFrameParams(0);
        _legacyFramePacketScratch.flags = CrowdVatFramePacketFlags.ReplaceSpatialQueries;
        _legacyFramePacketScratch.spatialQueries = queries ?? Array.Empty<CrowdVatSpatialQueryRequest>();
        ApplyFramePacket(_legacyFramePacketScratch);
    }

    public void ApplyFrameInput(CrowdVatFrameInput frameInput)
    {
        if (frameInput == null)
            throw new ArgumentNullException(nameof(frameInput));

        _legacyFramePacketScratch.Reset();
        CrowdVatFrameInputLegacyAdapter.CopyToPacket(frameInput, _legacyFramePacketScratch);
        ApplyFramePacket(_legacyFramePacketScratch);
    }

    public void ApplyFramePacket(CrowdVatFramePacket framePacket)
    {
        if (framePacket == null)
            throw new ArgumentNullException(nameof(framePacket));

        _gpuBridgeState.CaptureFrameProtocol(framePacket, _instanceCount);

        if (framePacket.HasFlag(CrowdVatFramePacketFlags.ReplaceSpatialQueries))
            ApplySpatialQueries(framePacket.spatialQueries);

        if (framePacket.HasFlag(CrowdVatFramePacketFlags.ClearRuntimeSquadData))
        {
            ClearSquadRuntimeData();
            return;
        }

        if (framePacket.HasFlag(CrowdVatFramePacketFlags.UploadSquadStates))
            SetSquadStates(framePacket.squadStates);

        if (framePacket.HasFlag(CrowdVatFramePacketFlags.UploadSquadAliveCounts))
            SetSquadAliveCounts(framePacket.squadAliveCounts);

        if (framePacket.HasFlag(CrowdVatFramePacketFlags.UploadAgentAssignments))
            SetAgentSquadData(framePacket.agentAssignments);

        if (framePacket.HasFlag(CrowdVatFramePacketFlags.UploadFormationSlots))
            SetFormationSlots(framePacket.formationSlots);

        SyncGpuBridgeRuntimeSnapshot();
    }

    public void ClearSpatialQueries()
    {
        SetSpatialQueries(Array.Empty<CrowdVatSpatialQueryRequest>());
    }

    public void SetSquadStates(CrowdVatSquadState[] squadStates)
    {
        int previousCount = _activeSquadStateCount;
        if (squadStates == null || squadStates.Length == 0)
        {
            _runtimeSquadStates = Array.Empty<CrowdVatSquadState>();
            _activeSquadStateCount = 0;
            if (previousCount != 0)
            {
                ResizeRuntimeSquadAliveCountUploadCache(0);
                _runtimeSquadAliveCountDirty = true;
                SyncRuntimeSquadAliveCountCpuMirror();
                InvalidateSquadAliveCountReadback();
            }

            _agentDataLayoutDirty = true;
            SyncGpuBridgeRuntimeSnapshot();
            return;
        }

        int count = squadStates.Length;
        if (_runtimeSquadStates == null || _runtimeSquadStates.Length != count)
            _runtimeSquadStates = new CrowdVatSquadState[count];

        Array.Copy(squadStates, _runtimeSquadStates, count);
        _activeSquadStateCount = count;
        if (previousCount != count)
        {
            ResizeRuntimeSquadAliveCountUploadCache(count);
            _runtimeSquadAliveCountDirty = true;
            SyncRuntimeSquadAliveCountCpuMirror();
            InvalidateSquadAliveCountReadback();
        }

        _agentDataLayoutDirty = true;
        SyncGpuBridgeRuntimeSnapshot();
    }

    public void SetAgentSquadData(CrowdVatAgentSquadAssignment[] agentSquadData)
    {
        if (agentSquadData == null || agentSquadData.Length == 0)
        {
            _runtimeAgentSquadData = Array.Empty<CrowdVatAgentSquadAssignment>();
            _activeAgentSquadDataCount = 0;
            _runtimeAgentSquadDataDirty = true;
            _agentDataLayoutDirty = true;
            SyncGpuBridgeRuntimeSnapshot();
            return;
        }

        int sourceCount = Mathf.Min(agentSquadData.Length, _instanceCount);
        int requiredCount = _enableGpuInstanceCombat ? _instanceCount : sourceCount;
        if (_runtimeAgentSquadData == null || _runtimeAgentSquadData.Length != requiredCount)
            _runtimeAgentSquadData = new CrowdVatAgentSquadAssignment[requiredCount];

        Array.Copy(agentSquadData, _runtimeAgentSquadData, sourceCount);
        for (int i = sourceCount; i < requiredCount; i++)
            _runtimeAgentSquadData[i] = CreateUnassignedAssignment();

        _activeAgentSquadDataCount = requiredCount;
        _runtimeAgentSquadDataDirty = true;
        _agentDataLayoutDirty = true;
        SyncGpuBridgeRuntimeSnapshot();
    }

    private static CrowdVatAgentSquadAssignment CreateUnassignedAssignment()
    {
        return new CrowdVatAgentSquadAssignment
        {
            squadId = 0u,
            slotIndex = 0u,
            roleMask = CrowdVatSquadRoleMask.None,
            flags = CrowdVatSquadMemberFlags.Unassigned,
            slotOffsetOverride = Vector2.zero,
            weight = 0.0f
        };
    }

    public void SetSquadAliveCounts(int[] squadAliveCounts)
    {
        if (squadAliveCounts == null || squadAliveCounts.Length == 0)
        {
            _runtimeSquadAliveCountUploadCache = Array.Empty<uint>();
            SyncRuntimeSquadAliveCountCpuMirror();
            _runtimeSquadAliveCountDirty = true;
            InvalidateSquadAliveCountReadback();
            SyncGpuBridgeRuntimeSnapshot();
            return;
        }

        int count = Mathf.Min(squadAliveCounts.Length, Mathf.Max(_activeSquadStateCount, 0));
        if (_runtimeSquadAliveCountUploadCache == null || _runtimeSquadAliveCountUploadCache.Length != count)
            _runtimeSquadAliveCountUploadCache = new uint[count];

        for (int squadIndex = 0; squadIndex < count; squadIndex++)
            _runtimeSquadAliveCountUploadCache[squadIndex] = (uint)Mathf.Max(0, squadAliveCounts[squadIndex]);

        SyncRuntimeSquadAliveCountCpuMirror();
        _runtimeSquadAliveCountDirty = true;
        InvalidateSquadAliveCountReadback();
        SyncGpuBridgeRuntimeSnapshot();
    }

    public void SetFormationSlots(CrowdVatFormationSlot[] formationSlots)
    {
        if (formationSlots == null || formationSlots.Length == 0)
        {
            _runtimeFormationSlots = Array.Empty<CrowdVatFormationSlot>();
            _activeFormationSlotCount = 0;
            _runtimeFormationSlotsDirty = true;
            _agentDataLayoutDirty = true;
            SyncGpuBridgeRuntimeSnapshot();
            return;
        }

        int count = formationSlots.Length;
        if (_runtimeFormationSlots == null || _runtimeFormationSlots.Length != count)
            _runtimeFormationSlots = new CrowdVatFormationSlot[count];

        Array.Copy(formationSlots, _runtimeFormationSlots, count);
        _activeFormationSlotCount = count;
        _runtimeFormationSlotsDirty = true;
        _agentDataLayoutDirty = true;
        SyncGpuBridgeRuntimeSnapshot();
    }

    public void ClearSquadRuntimeData()
    {
        _runtimeSquadStates = Array.Empty<CrowdVatSquadState>();
        _runtimeSquadAliveCountUploadCache = Array.Empty<uint>();
        _runtimeAgentSquadData = Array.Empty<CrowdVatAgentSquadAssignment>();
        _runtimeFormationSlots = Array.Empty<CrowdVatFormationSlot>();
        _activeSquadStateCount = 0;
        _activeAgentSquadDataCount = 0;
        _activeFormationSlotCount = 0;
        _runtimeSquadAliveCountDirty = true;
        _runtimeAgentSquadDataDirty = true;
        _runtimeFormationSlotsDirty = true;
        InvalidateSquadAliveCountReadback();
        _agentDataLayoutDirty = true;
        SyncGpuBridgeRuntimeSnapshot();
    }

    public bool TryGetRuntimeSquadAliveCount(int runtimeSquadIndex, out int aliveCount)
    {
        aliveCount = 0;
        if (runtimeSquadIndex < 0 ||
            runtimeSquadIndex >= _activeSquadStateCount ||
            !TryReadRuntimeSquadAliveCounts())
        {
            return false;
        }

        if (_runtimeSquadAliveCountReadbackCache == null || runtimeSquadIndex >= _runtimeSquadAliveCountReadbackCache.Length)
            return false;

        aliveCount = (int)_runtimeSquadAliveCountReadbackCache[runtimeSquadIndex];
        return true;
    }

    public bool TryRefreshRuntimeSquadAliveCountsImmediately()
    {
        if (_activeSquadStateCount <= 0)
            return false;

        if (_runtimeSquadAliveCountDirty)
        {
            SyncRuntimeSquadAliveCountCpuMirror();
            return _runtimeSquadAliveCountReadbackCache.Length >= _activeSquadStateCount;
        }

        if (_squadAliveCountBuffer == null)
            return false;

        if (_hasPendingSquadAliveCountReadback)
            InvalidateSquadAliveCountReadback();

        return ReadRuntimeSquadAliveCountsSynchronously();
    }

    private void InvalidateSquadAliveCountReadback()
    {
        _hasPendingSquadAliveCountReadback = false;
        _lastSquadAliveCountReadbackFrame = -1;
        _squadAliveCountReadbackVersion++;
    }

    public bool TryGetDebugSquadStates(out CrowdVatSquadState[] squadStates)
    {
        if (_activeSquadStateCount <= 0)
        {
            squadStates = Array.Empty<CrowdVatSquadState>();
            return false;
        }

        squadStates = new CrowdVatSquadState[_activeSquadStateCount];
        Array.Copy(_runtimeSquadStates, squadStates, _activeSquadStateCount);
        return true;
    }

    public bool TryGetDebugFormationSlots(out CrowdVatFormationSlot[] formationSlots)
    {
        if (_activeFormationSlotCount <= 0)
        {
            formationSlots = Array.Empty<CrowdVatFormationSlot>();
            return false;
        }

        formationSlots = new CrowdVatFormationSlot[_activeFormationSlotCount];
        Array.Copy(_runtimeFormationSlots, formationSlots, _activeFormationSlotCount);
        return true;
    }

    private void AppendDebugInteractionSphereQueries(List<CrowdVatSpatialQueryRequest> queries)
    {
        RefreshRuntimeTargets();

        if (_useCharacterAsInteractionSphere &&
            TryBuildCharacterInteractionDebugQuery(out CrowdVatSpatialQueryRequest characterQuery))
        {
            queries.Add(characterQuery);
        }

        if (_interactionSpheres == null)
            return;

        for (int sphereIndex = 0; sphereIndex < _interactionSpheres.Length; sphereIndex++)
        {
            if (TryBuildCustomInteractionDebugQuery(_interactionSpheres[sphereIndex], sphereIndex, out CrowdVatSpatialQueryRequest customQuery))
                queries.Add(customQuery);
        }
    }

    private bool TryBuildCharacterInteractionDebugQuery(out CrowdVatSpatialQueryRequest query)
    {
        query = default;
        if (_characterController == null || _characterInteractionStrength <= 0.0f)
            return false;

        Vector3 worldCenter = _characterController.transform.TransformPoint(_characterController.center);
        float radius = Mathf.Max(_characterController.radius, _characterController.height * 0.25f) * _characterInteractionRadiusMultiplier;
        query = CreateDebugSphereQuery(DebugCharacterInteractionQueryId, worldCenter, radius, CrowdVatSpatialQueryFlags.ActiveOnly);
        return true;
    }

    private bool TryBuildCustomInteractionDebugQuery(InteractionSphere sphere, int sphereIndex, out CrowdVatSpatialQueryRequest query)
    {
        query = default;
        if (sphere.target == null || sphere.radius <= 0.0f || sphere.strength <= 0.0f)
            return false;

        float radiusScale = MaxAbsComponent(sphere.target.lossyScale);
        float radius = sphere.radius * Mathf.Max(radiusScale, 0.0001f);
        query = CreateDebugSphereQuery(DebugCustomInteractionQueryIdBase - sphereIndex, sphere.target.position, radius, CrowdVatSpatialQueryFlags.None);
        return true;
    }

    private static CrowdVatSpatialQueryRequest CreateDebugSphereQuery(int queryId, Vector3 worldCenter, float radius, CrowdVatSpatialQueryFlags flags)
    {
        return new CrowdVatSpatialQueryRequest
        {
            queryId = queryId,
            targetMask = CrowdVatSpatialTargetMask.CrowdAgent,
            factionMask = CrowdVatFactionMask.All,
            shape = CrowdVatSpatialQueryShape.OverlapSphere,
            flags = flags,
            worldStart = worldCenter,
            worldEnd = worldCenter,
            radius = radius,
            maxHits = 0u
        };
    }

    public int ReadSpatialQueryResults(List<CrowdVatSpatialQueryResult> results, List<CrowdVatSpatialQueryHit> hits)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));
        if (hits == null)
            throw new ArgumentNullException(nameof(hits));

        results.Clear();
        hits.Clear();

        if (_activeSpatialQueryCount <= 0 ||
            _spatialQueryResultBuffer == null ||
            _spatialQueryHitBuffer == null)
        {
            return 0;
        }

        EnsureSpatialQueryReadbackCaches();
        _spatialQueryResultBuffer.GetData(_spatialQueryResultCache, 0, 0, _activeSpatialQueryCount);
        _spatialQueryHitBuffer.GetData(_spatialQueryHitCache, 0, 0, _activeSpatialQueryCount * _maxHitsPerSpatialQuery);

        int totalWrittenHits = 0;
        for (int queryIndex = 0; queryIndex < _activeSpatialQueryCount; queryIndex++)
        {
            SpatialQueryResultData resultData = _spatialQueryResultCache[queryIndex];
            int writtenHits = Mathf.Min((int)resultData.writtenHits, _maxHitsPerSpatialQuery);
            totalWrittenHits += writtenHits;

            results.Add(new CrowdVatSpatialQueryResult
            {
                queryId = (int)resultData.queryId,
                totalHits = (int)resultData.totalHits,
                writtenHits = writtenHits,
                overflow = resultData.overflow != 0u
            });

            int hitBaseIndex = queryIndex * _maxHitsPerSpatialQuery;
            List<CrowdVatSpatialQueryHit> sortedHits = new List<CrowdVatSpatialQueryHit>(writtenHits);
            for (int hitIndex = 0; hitIndex < writtenHits; hitIndex++)
            {
                SpatialQueryHitData hitData = _spatialQueryHitCache[hitBaseIndex + hitIndex];
                sortedHits.Add(new CrowdVatSpatialQueryHit
                {
                    queryId = (int)hitData.queryId,
                    ownerIndex = (int)hitData.ownerIndex,
                    targetMask = (CrowdVatSpatialTargetMask)hitData.targetMask,
                    flags = (CrowdVatSpatialHitFlags)hitData.flags,
                    faction = (CrowdVatFaction)hitData.faction,
                    normalizedDistance = hitData.normalizedDistance
                });
            }

            sortedHits.Sort(static (left, right) => left.normalizedDistance.CompareTo(right.normalizedDistance));
            hits.AddRange(sortedHits);
        }

        return totalWrittenHits;
    }

    public bool TryReadSquadAliveObservations(List<CrowdVatSquadAliveObservation> observations, out CrowdVatObservationFrameInfo frameInfo)
    {
        if (observations == null)
            throw new ArgumentNullException(nameof(observations));

        observations.Clear();
        frameInfo = default;
        if (_activeSquadStateCount <= 0)
            return false;

        bool hasData = TryReadRuntimeSquadAliveCounts();
        frameInfo = CreateSquadAliveObservationFrameInfo();
        if (!hasData)
            return false;

        for (int runtimeSquadIndex = 0; runtimeSquadIndex < _activeSquadStateCount; runtimeSquadIndex++)
        {
            if (!TryGetRuntimeSquadAliveCount(runtimeSquadIndex, out int aliveCount))
                continue;

            observations.Add(new CrowdVatSquadAliveObservation
            {
                runtimeSquadIndex = runtimeSquadIndex,
                aliveCount = aliveCount
            });
        }

        return observations.Count > 0;
    }

    public int ReadCombatObservations(List<CrowdVatCombatObservation> observations, out CrowdVatObservationFrameInfo frameInfo)
    {
        if (observations == null)
            throw new ArgumentNullException(nameof(observations));

        observations.Clear();
        frameInfo = default;
        if (!TryReadCombatStateBuffer())
            return 0;

        TryReadPhysicsActiveStateBuffer();
        frameInfo = new CrowdVatObservationFrameInfo
        {
            frameCount = Application.isPlaying ? Time.frameCount : -1,
            sourceKind = CrowdVatObservationSourceKind.SyncGpuReadback,
            isStale = false
        };

        int count = Mathf.Min(_instanceCount, _combatStateReadbackCache.Length);
        for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
        {
            InstanceCombatStateData data = _combatStateReadbackCache[instanceIndex];
            uint flags = data.flags;
            bool hasTarget = (flags & InstanceCombatFlagHasTarget) != 0u;
            float distance = data.localOriginAndDistance.w >= 1e19f
                ? float.PositiveInfinity
                : data.localOriginAndDistance.w;
            bool hasImpact = data.localImpactNormalAndHit.w > 0.001f;
            Vector3 localImpactNormal = new Vector3(
                data.localImpactNormalAndHit.x,
                data.localImpactNormalAndHit.y,
                data.localImpactNormalAndHit.z);
            Vector3 worldImpactNormal = hasImpact && localImpactNormal.sqrMagnitude > 1e-8f
                ? transform.TransformDirection(localImpactNormal).normalized
                : Vector3.zero;

            observations.Add(new CrowdVatCombatObservation
            {
                runtimeInstanceIndex = instanceIndex,
                physicsActive = IsPhysicsActiveFromReadback(instanceIndex),
                dead = (flags & InstanceCombatFlagDead) != 0u,
                hasTarget = hasTarget,
                runtimeTargetInstanceIndex = hasTarget ? data.targetIndex : -1,
                hasLineOfSight = (flags & InstanceCombatFlagHasLineOfSight) != 0u,
                distance = distance,
                firedThisFrame = (flags & InstanceCombatFlagFiredThisFrame) != 0u,
                health = data.healthAndHitFeedback.x,
                normalizedHealth = data.healthAndHitFeedback.w,
                hitFlash = data.healthAndHitFeedback.y,
                lastDamage = data.healthAndHitFeedback.z,
                hasImpact = hasImpact,
                worldImpactNormal = worldImpactNormal
            });
        }

        return observations.Count;
    }

    public int ReadSpatialQueryObservations(
        List<CrowdVatSpatialQueryResult> results,
        List<CrowdVatSpatialQueryHit> hits,
        out CrowdVatObservationFrameInfo frameInfo)
    {
        int writtenHitCount = ReadSpatialQueryResults(results, hits);
        frameInfo = new CrowdVatObservationFrameInfo
        {
            frameCount = Application.isPlaying ? Time.frameCount : -1,
            sourceKind = writtenHitCount > 0 || _activeSpatialQueryCount > 0
                ? CrowdVatObservationSourceKind.SyncGpuReadback
                : CrowdVatObservationSourceKind.None,
            isStale = false
        };

        return writtenHitCount;
    }

    private CrowdVatFrameParams BuildRendererFrameParams(int eventCount)
    {
        return new CrowdVatFrameParams
        {
            deltaTime = Application.isPlaying ? Mathf.Max(Time.deltaTime, 0.0f) : 0.0f,
            frameIndex = Application.isPlaying ? (uint)Mathf.Max(Time.frameCount, 0) : 0u,
            activeAgentCount = (uint)Mathf.Max(_instanceCount, 0),
            eventCount = (uint)Mathf.Max(eventCount, 0)
        };
    }

    private void ApplySpatialQueries(CrowdVatSpatialQueryRequest[] queries)
    {
        if (queries == null || queries.Length == 0)
        {
            _runtimeSpatialQueries = Array.Empty<CrowdVatSpatialQueryRequest>();
            _activeSpatialQueryCount = 0;
            return;
        }

        int count = Mathf.Min(queries.Length, _maxSpatialQueries);
        if (_runtimeSpatialQueries == null || _runtimeSpatialQueries.Length != count)
            _runtimeSpatialQueries = new CrowdVatSpatialQueryRequest[count];

        Array.Copy(queries, _runtimeSpatialQueries, count);
        _activeSpatialQueryCount = count;
    }

    private void SyncGpuBridgeRuntimeSnapshot()
    {
        _gpuBridgeState.SetRuntimeSquadSnapshot(
            _runtimeSquadStates,
            _runtimeSquadAliveCountUploadCache,
            _runtimeAgentSquadData,
            _runtimeFormationSlots);
        _gpuBridgeState.SetSpatialQueries(_runtimeSpatialQueries, _maxSpatialQueries);
    }

}
