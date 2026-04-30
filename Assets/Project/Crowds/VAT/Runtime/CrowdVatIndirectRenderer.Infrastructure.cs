using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class CrowdVatIndirectRenderer : MonoBehaviour
{
    private void MarkResourcesDirty()
    {
        _resourcesDirty = true;
        _agentDataLayoutDirty = true;
        _buildAliveInstanceListKernel = -1;
        _buildAliveDispatchArgsKernel = -1;
        _buildActiveInstanceListKernel = -1;
        _buildActiveDispatchArgsKernel = -1;
        _buildVisibleRuntimeInstanceListKernel = -1;
        _buildVisibleChunkInstanceListKernel = -1;
        _buildVisibleIndirectArgsKernel = -1;
        _predictKernel = -1;
        _clearGridKernel = -1;
        _buildGridKernel = -1;
        _buildSpatialElementsKernel = -1;
        _solveCrowdKernel = -1;
        _resolveTargetAcquisitionKernel = -1;
        _resolveInstanceCombatKernel = -1;
        _clearSpatialQueriesKernel = -1;
        _resolveSpatialQueriesKernel = -1;
        _finalizeKernel = -1;
        _captureAiDebugGpuStageKernel = -1;
        _lastCombatStateReadbackFrame = -1;
        InvalidateSquadAliveCountReadback();
        _runtimeSquadVisibilityDirty = true;
        _usesGpuVisibleInstanceCompactionThisFrame = false;
        _visibleUnassignedInstancesThisFrame = false;
    }

    private void ReleaseResources()
    {
        ReleaseBuffer(ref _spawnDataBuffer);
        ReleaseBuffer(ref _simulationPositionYawBufferA);
        ReleaseBuffer(ref _simulationPositionYawBufferB);
        _simulationPositionYawReadBuffer = null;
        _simulationPositionYawWriteBuffer = null;
        ReleaseBuffer(ref _simulationScaleBufferA);
        ReleaseBuffer(ref _simulationScaleBufferB);
        _simulationScaleReadBuffer = null;
        _simulationScaleWriteBuffer = null;
        ReleaseBuffer(ref _simulationVelocityBufferA);
        ReleaseBuffer(ref _simulationVelocityBufferB);
        _simulationVelocityReadBuffer = null;
        _simulationVelocityWriteBuffer = null;
        ReleaseBuffer(ref _activeStateBuffer);
        ReleaseBuffer(ref _deathStateBuffer);
        ReleaseBuffer(ref _aliveInstanceIndexBuffer);
        ReleaseBuffer(ref _aliveInstanceCounterBuffer);
        ReleaseBuffer(ref _aliveInstanceDispatchArgsBuffer);
        ReleaseBuffer(ref _activeInstanceIndexBuffer);
        ReleaseBuffer(ref _activeInstanceCounterBuffer);
        ReleaseBuffer(ref _activeInstanceDispatchArgsBuffer);
        ReleaseBuffer(ref _gridCounterBuffer);
        ReleaseBuffer(ref _gridOccupantBuffer);
        ReleaseBuffer(ref _spatialCapsuleStartRadiusBuffer);
        ReleaseBuffer(ref _spatialCapsuleEndHeightBuffer);
        ReleaseBuffer(ref _spatialOwnerIndexBuffer);
        ReleaseBuffer(ref _spatialTargetMaskBuffer);
        ReleaseBuffer(ref _spatialFlagsBuffer);
        ReleaseBuffer(ref _spatialFactionBuffer);
        ReleaseBuffer(ref _spatialQueryBuffer);
        ReleaseBuffer(ref _spatialQueryResultBuffer);
        ReleaseBuffer(ref _spatialQueryHitBuffer);
        ReleaseBuffer(ref _interactionSphereBuffer);
        ReleaseBuffer(ref _combatStateBuffer);
        ReleaseBuffer(ref _targetAcquisitionStateBuffer);
        ReleaseBuffer(ref _squadStateBuffer);
        ReleaseBuffer(ref _squadAliveCountBuffer);
        ReleaseBuffer(ref _agentSquadDataBuffer);
        ReleaseBuffer(ref _formationSlotBuffer);
        ReleaseBuffer(ref _agentCoreBuffer);
        ReleaseBuffer(ref _agentPhysicsExtBuffer);
        ReleaseBuffer(ref _animationClipMetadataBuffer);
        ReleaseBuffer(ref _instanceAnimationStateBuffer);
        ReleaseBuffer(ref _instanceTransformBuffer);
        ReleaseBuffer(ref _instanceFrameDataBuffer);
        ReleaseBuffer(ref _instanceFrameBlendDataBuffer);
        ReleaseBuffer(ref _visibleInstanceIndexBuffer);
        ReleaseBuffer(ref _visibleInstanceCounterBuffer);
        ReleaseBuffer(ref _visibleRuntimeSquadMaskBuffer);
        ReleaseBuffer(ref _instanceRenderChunkBuffer);
        ReleaseBuffer(ref _visibleRenderChunkMaskBuffer);
        ReleaseBuffer(ref _combatTracerArgsBuffer);
        EndAiDebugGpuStageCapture();

        if (_indirectArgsBuffers != null)
        {
            for (int bufferIndex = 0; bufferIndex < _indirectArgsBuffers.Length; bufferIndex++)
                ReleaseBuffer(ref _indirectArgsBuffers[bufferIndex]);
        }

        _indirectArgsBuffers = Array.Empty<GraphicsBuffer>();
        _indirectArgsCache = Array.Empty<GraphicsBuffer.IndirectDrawIndexedArgs>();
        _visibleInstanceIndexCache = Array.Empty<uint>();
        _visibleRuntimeSquadMaskUploadCache = Array.Empty<uint>();
        _visibleRenderChunkMaskUploadCache = Array.Empty<uint>();
        _allInstanceIndicesCache = Array.Empty<uint>();
        _animationClipGpuCache = Array.Empty<AnimationClipGpuData>();
        _instanceAnimationStateCpuCache = Array.Empty<InstanceAnimationStateCpuData>();
        _instanceAnimationStateGpuCache = Array.Empty<InstanceAnimationStateGpuData>();
        _combatStateReadbackCache = Array.Empty<InstanceCombatStateData>();
        _runtimeSquadAliveCountReadbackCache = Array.Empty<uint>();
        _renderChunks = Array.Empty<RenderChunk>();
        _runtimeSquadRenderChunks = Array.Empty<RuntimeSquadRenderChunk>();
        _squadStateUploadCache = Array.Empty<RuntimeSquadStateGpuData>();
        _agentSquadUploadCache = Array.Empty<RuntimeAgentSquadDataGpuData>();
        _formationSlotUploadCache = Array.Empty<RuntimeFormationSlotGpuData>();
        _agentCoreUploadCache = Array.Empty<CrowdVatAgentCore>();
        _agentPhysicsExtUploadCache = Array.Empty<CrowdVatAgentPhysicsExt>();
        _runtimeAgentSquadDataDirty = true;
        _runtimeFormationSlotsDirty = true;
        _runtimeSquadVisibilityDirty = true;
        _runtimeSquadAliveCountDirty = true;
        _agentDataLayoutDirty = true;
        InvalidateSquadAliveCountReadback();
        if (_hasRuntimeRenderResource && _runtimeRenderResource.runtimeMaterials != null)
        {
            for (int materialIndex = 0; materialIndex < _runtimeRenderResource.runtimeMaterials.Length; materialIndex++)
                DestroyRuntimeMaterial(_runtimeRenderResource.runtimeMaterials[materialIndex]);
        }

        if (_combatTracerMaterial != null)
        {
            DestroyRuntimeMaterial(_combatTracerMaterial);
            _combatTracerMaterial = null;
        }

        DestroyRuntimeMesh(ref _combatTracerMesh);

        _runtimeRenderResource = default;
        _hasRuntimeRenderResource = false;
        if (_fallbackStaticSdfTexture != null)
        {
            if (Application.isPlaying)
                Destroy(_fallbackStaticSdfTexture);
            else
                DestroyImmediate(_fallbackStaticSdfTexture);

            _fallbackStaticSdfTexture = null;
        }

        _resolvedEnvironmentDistanceFieldTexture = null;
        _resolvedStaticSdfTexture = null;
        _hasResolvedEnvironmentDistanceField = false;
        _hasResolvedTerrainCollision = false;
        _hasResolvedStaticSdfCollision = false;
        _lastCombatStateReadbackFrame = -1;
        _usesGpuVisibleInstanceCompactionThisFrame = false;
        _visibleUnassignedInstancesThisFrame = false;
        _resourcesDirty = true;
    }

    private static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    private static void ReleaseBuffer(ref GraphicsBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    private static void DestroyRuntimeMaterial(Material material)
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
    }

    private static void DestroyRuntimeMesh(ref Mesh mesh)
    {
        if (mesh == null)
            return;

        if (Application.isPlaying)
            Destroy(mesh);
        else
            DestroyImmediate(mesh);

        mesh = null;
    }

    private Shader ResolveCombatTracerShader()
    {
        return _combatTracerShader != null
            ? _combatTracerShader
            : Shader.Find(DefaultCombatTracerShaderName);
    }

    private static Mesh CreateCombatTracerMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "CrowdCombatBallisticFxQuads",
            hideFlags = HideFlags.HideAndDontSave
        };

        List<Vector3> vertices = new List<Vector3>(12);
        List<Vector2> uvs = new List<Vector2>(12);
        List<Vector2> effectIds = new List<Vector2>(12);
        List<int> triangles = new List<int>(18);
        for (int effectIndex = 0; effectIndex < 3; effectIndex++)
        {
            int baseVertex = vertices.Count;
            vertices.Add(new Vector3(-1.0f, 0.0f, 0.0f));
            vertices.Add(new Vector3(1.0f, 0.0f, 0.0f));
            vertices.Add(new Vector3(-1.0f, 1.0f, 0.0f));
            vertices.Add(new Vector3(1.0f, 1.0f, 0.0f));

            uvs.Add(new Vector2(0.0f, 0.0f));
            uvs.Add(new Vector2(1.0f, 0.0f));
            uvs.Add(new Vector2(0.0f, 1.0f));
            uvs.Add(new Vector2(1.0f, 1.0f));

            Vector2 effectId = new Vector2(effectIndex, 0.0f);
            effectIds.Add(effectId);
            effectIds.Add(effectId);
            effectIds.Add(effectId);
            effectIds.Add(effectId);

            triangles.Add(baseVertex + 0);
            triangles.Add(baseVertex + 2);
            triangles.Add(baseVertex + 1);
            triangles.Add(baseVertex + 1);
            triangles.Add(baseVertex + 2);
            triangles.Add(baseVertex + 3);
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, effectIds);
        mesh.SetTriangles(triangles, 0, true);
        mesh.bounds = new Bounds(new Vector3(0.0f, 0.5f, 0.0f), new Vector3(2.0f, 1.0f, 0.1f));
        mesh.UploadMeshData(true);
        return mesh;
    }

    private static Bounds ExpandBounds(Bounds bounds, float padding)
    {
        Bounds expanded = bounds;
        expanded.Expand(Mathf.Max(0.0f, padding) * 2.0f);
        return expanded;
    }

    private static float RandomRange(System.Random random, float minValue, float maxValue)
    {
        if (maxValue <= minValue)
            return minValue;

        return minValue + (float)random.NextDouble() * (maxValue - minValue);
    }

    private static float MaxAbsComponent(Vector3 value)
    {
        Vector3 absolute = Abs(value);
        return Mathf.Max(absolute.x, Mathf.Max(absolute.y, absolute.z));
    }

    private static void CopySourceMaterialProperties(Material sourceMaterial, Material destinationMaterial)
    {
        if (destinationMaterial == null)
            return;

        destinationMaterial.SetColor("_BaseColor", Color.white);
        destinationMaterial.SetColor("_SpecColor", Color.white);
        destinationMaterial.SetFloat("_AlphaClip", 0.0f);
        destinationMaterial.SetFloat("_Cutoff", 0.5f);
        destinationMaterial.SetFloat("_BumpScale", 1.0f);
        destinationMaterial.SetFloat("_Smoothness", 1.0f);
        destinationMaterial.SetFloat("_SpecularStrength", 0.12f);
        destinationMaterial.SetFloat("_UseNormalMap", 0.0f);
        destinationMaterial.SetFloat("_UseSpecGlossMap", 0.0f);

        if (sourceMaterial == null)
            return;

        if (TryGetTexture(sourceMaterial, out Texture texture, out Vector2 scale, out Vector2 offset))
        {
            destinationMaterial.SetTexture("_BaseMap", texture);
            destinationMaterial.SetTextureScale("_BaseMap", scale);
            destinationMaterial.SetTextureOffset("_BaseMap", offset);
        }

        if (TryGetColor(sourceMaterial, out Color baseColor))
            destinationMaterial.SetColor("_BaseColor", baseColor);

        if (sourceMaterial.HasProperty("_Cutoff"))
            destinationMaterial.SetFloat("_Cutoff", sourceMaterial.GetFloat("_Cutoff"));

        if (sourceMaterial.HasProperty("_BumpMap"))
        {
            Texture normalTexture = sourceMaterial.GetTexture("_BumpMap");
            destinationMaterial.SetTexture("_BumpMap", normalTexture);
            destinationMaterial.SetFloat("_UseNormalMap", normalTexture != null ? 1.0f : 0.0f);
        }

        if (sourceMaterial.HasProperty("_BumpScale"))
            destinationMaterial.SetFloat("_BumpScale", sourceMaterial.GetFloat("_BumpScale"));

        if (sourceMaterial.HasProperty("_SpecGlossMap"))
        {
            Texture specGlossTexture = sourceMaterial.GetTexture("_SpecGlossMap");
            destinationMaterial.SetTexture("_SpecGlossMap", specGlossTexture);
            destinationMaterial.SetFloat("_UseSpecGlossMap", specGlossTexture != null ? 1.0f : 0.0f);
        }

        if (sourceMaterial.HasProperty("_SpecColor"))
            destinationMaterial.SetColor("_SpecColor", sourceMaterial.GetColor("_SpecColor"));

        if (sourceMaterial.HasProperty("_Smoothness"))
            destinationMaterial.SetFloat("_Smoothness", sourceMaterial.GetFloat("_Smoothness"));

        if (sourceMaterial.HasProperty("_SpecularStrength"))
            destinationMaterial.SetFloat("_SpecularStrength", sourceMaterial.GetFloat("_SpecularStrength"));

        bool alphaClip = sourceMaterial.HasProperty("_AlphaClip") && sourceMaterial.GetFloat("_AlphaClip") > 0.5f;
        destinationMaterial.SetFloat("_AlphaClip", alphaClip ? 1.0f : 0.0f);
    }

    private static bool TryGetTexture(Material material, out Texture texture, out Vector2 scale, out Vector2 offset)
    {
        string[] candidates =
        {
            "_BaseMap",
            "_MainTex"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string propertyName = candidates[i];
            if (!material.HasProperty(propertyName))
                continue;

            texture = material.GetTexture(propertyName);
            scale = material.GetTextureScale(propertyName);
            offset = material.GetTextureOffset(propertyName);
            return texture != null;
        }

        texture = null;
        scale = Vector2.one;
        offset = Vector2.zero;
        return false;
    }

    private static bool TryGetColor(Material material, out Color color)
    {
        string[] candidates =
        {
            "_BaseColor",
            "_Color"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string propertyName = candidates[i];
            if (!material.HasProperty(propertyName))
                continue;

            color = material.GetColor(propertyName);
            return true;
        }

        color = Color.white;
        return false;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
        Vector3 extents = bounds.extents;
        Vector3 axisX = new Vector3(matrix.m00, matrix.m10, matrix.m20);
        Vector3 axisY = new Vector3(matrix.m01, matrix.m11, matrix.m21);
        Vector3 axisZ = new Vector3(matrix.m02, matrix.m12, matrix.m22);
        Vector3 transformedExtents =
            Abs(axisX) * extents.x +
            Abs(axisY) * extents.y +
            Abs(axisZ) * extents.z;
        return new Bounds(center, transformedExtents * 2.0f);
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private bool TryResolveSceneQueryTerrain(out Terrain terrain)
    {
        terrain = null;

        if (_terrain != null &&
            (_terrain.terrainData == null || !_terrain.gameObject.activeInHierarchy))
        {
            _terrain = null;
        }

        if (_autoResolveTerrain && _terrain == null)
            _terrain = FindPreferredTerrain();

        if (_terrain == null || _terrain.terrainData == null)
            return false;

        terrain = _terrain;
        return true;
    }

    private bool TryGetResolvedTerrain(out Terrain terrain)
    {
        terrain = null;
        if (!_enableTerrainCollision)
            return false;

        return TryResolveSceneQueryTerrain(out terrain);
    }

    private bool TryResolveSceneQueryObstacleDistanceField(out CrowdVatObstacleDistanceFieldDescriptor descriptor)
    {
        if (_sceneQueryFieldAsset != null &&
            _sceneQueryFieldAsset.TryGetObstacleDistanceFieldDescriptor(out descriptor))
        {
            return true;
        }

        if (_staticSdfTexture == null ||
            _staticSdfWorldSize.x <= 0.0f ||
            _staticSdfWorldSize.y <= 0.0f ||
            _staticSdfWorldSize.z <= 0.0f)
        {
            descriptor = default;
            return false;
        }

        descriptor = new CrowdVatObstacleDistanceFieldDescriptor
        {
            sdfTexture = _staticSdfTexture,
            worldCenter = _staticSdfWorldCenter,
            worldSize = _staticSdfWorldSize,
            distanceScale = _staticSdfDistanceScale,
            distanceBias = _staticSdfDistanceBias
        };
        return true;
    }

    private bool TryResolveSceneQueryBakedEnvironmentDistanceField(out CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor)
    {
        if (_sceneQueryFieldAsset != null &&
            _sceneQueryFieldAsset.TryGetBakedEnvironmentDistanceFieldDescriptor(out descriptor))
        {
            return true;
        }

        descriptor = default;
        return false;
    }

    private bool TryGetResolvedStaticSdfFieldForCollision(out CrowdVatObstacleDistanceFieldDescriptor descriptor)
    {
        descriptor = default;
        if (!_enableStaticSdfCollision)
            return false;

        return TryResolveSceneQueryObstacleDistanceField(out descriptor);
    }

    private bool TryGetResolvedStaticSdf(out Texture3D staticSdfTexture)
    {
        staticSdfTexture = null;
        if (!TryGetResolvedStaticSdfFieldForCollision(out CrowdVatObstacleDistanceFieldDescriptor descriptor))
            return false;

        staticSdfTexture = descriptor.sdfTexture;
        return staticSdfTexture != null;
    }

    private Texture3D GetFallbackStaticSdfTexture()
    {
        if (_fallbackStaticSdfTexture != null)
            return _fallbackStaticSdfTexture;

        _fallbackStaticSdfTexture = new Texture3D(1, 1, 1, TextureFormat.RGBAFloat, false)
        {
            name = "CrowdVatIndirectFallbackStaticSdf",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        _fallbackStaticSdfTexture.SetPixels(new[] { new Color(1024.0f, 0.0f, 0.0f, 0.0f) });
        _fallbackStaticSdfTexture.Apply(false, true);
        return _fallbackStaticSdfTexture;
    }

    private Vector3 ApplyTerrainHeightToLocalPosition(Vector3 localPosition)
    {
        if (!TryGetResolvedTerrain(out Terrain terrain))
            return localPosition;

        Vector3 terrainMin = terrain.transform.position;
        Vector3 terrainMax = terrainMin + terrain.terrainData.size;
        Vector3 worldPosition = transform.TransformPoint(localPosition);

        if (worldPosition.x < terrainMin.x || worldPosition.x > terrainMax.x ||
            worldPosition.z < terrainMin.z || worldPosition.z > terrainMax.z)
        {
            return localPosition;
        }

        worldPosition.y = terrain.SampleHeight(worldPosition) + terrainMin.y + _terrainHeightOffset;
        return transform.InverseTransformPoint(worldPosition);
    }

    private bool TryReadRuntimeSquadAliveCounts()
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

        if (_runtimeSquadAliveCountReadbackCache == null || _runtimeSquadAliveCountReadbackCache.Length != _squadAliveCountBuffer.count)
            _runtimeSquadAliveCountReadbackCache = new uint[_squadAliveCountBuffer.count];

        if (!Application.isPlaying)
            return ReadRuntimeSquadAliveCountsSynchronously();

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            bool shouldRefreshSynchronously = _lastSquadAliveCountReadbackFrame < 0 ||
                Time.frameCount - _lastSquadAliveCountReadbackFrame >= SquadAliveCountReadbackIntervalFrames;
            if (shouldRefreshSynchronously)
                return ReadRuntimeSquadAliveCountsSynchronously();

            return true;
        }

        RequestAsyncRuntimeSquadAliveCountReadbackIfNeeded();
        return true;
    }

    private bool ReadRuntimeSquadAliveCountsSynchronously()
    {
        if (_squadAliveCountBuffer == null)
            return false;

        if (_runtimeSquadAliveCountReadbackCache == null || _runtimeSquadAliveCountReadbackCache.Length != _squadAliveCountBuffer.count)
            _runtimeSquadAliveCountReadbackCache = new uint[_squadAliveCountBuffer.count];

        _squadAliveCountBuffer.GetData(_runtimeSquadAliveCountReadbackCache, 0, 0, _squadAliveCountBuffer.count);
        _hasPendingSquadAliveCountReadback = false;
        _lastSquadAliveCountReadbackFrame = Application.isPlaying ? Time.frameCount : -1;
        return true;
    }

    private void SyncRuntimeSquadAliveCountCpuMirror()
    {
        int requiredCount = Mathf.Max(
            Mathf.Max(_activeSquadStateCount, _runtimeSquadAliveCountUploadCache != null ? _runtimeSquadAliveCountUploadCache.Length : 0),
            _squadAliveCountBuffer != null ? _squadAliveCountBuffer.count : 0);

        if (requiredCount <= 0)
        {
            _runtimeSquadAliveCountReadbackCache = Array.Empty<uint>();
            return;
        }

        if (_runtimeSquadAliveCountReadbackCache == null || _runtimeSquadAliveCountReadbackCache.Length != requiredCount)
            _runtimeSquadAliveCountReadbackCache = new uint[requiredCount];

        Array.Clear(_runtimeSquadAliveCountReadbackCache, 0, _runtimeSquadAliveCountReadbackCache.Length);
        int copyCount = Mathf.Min(_runtimeSquadAliveCountReadbackCache.Length, _runtimeSquadAliveCountUploadCache.Length);
        if (copyCount > 0)
            Array.Copy(_runtimeSquadAliveCountUploadCache, _runtimeSquadAliveCountReadbackCache, copyCount);
    }

    private void ResizeRuntimeSquadAliveCountUploadCache(int requiredCount)
    {
        if (requiredCount <= 0)
        {
            _runtimeSquadAliveCountUploadCache = Array.Empty<uint>();
            return;
        }

        if (_runtimeSquadAliveCountUploadCache != null && _runtimeSquadAliveCountUploadCache.Length == requiredCount)
            return;

        uint[] resizedCache = new uint[requiredCount];
        if (_runtimeSquadAliveCountUploadCache != null && _runtimeSquadAliveCountUploadCache.Length > 0)
        {
            int copyCount = Mathf.Min(_runtimeSquadAliveCountUploadCache.Length, resizedCache.Length);
            Array.Copy(_runtimeSquadAliveCountUploadCache, resizedCache, copyCount);
        }

        _runtimeSquadAliveCountUploadCache = resizedCache;
    }

    private void RequestAsyncRuntimeSquadAliveCountReadbackIfNeeded()
    {
        if (_hasPendingSquadAliveCountReadback ||
            _squadAliveCountBuffer == null ||
            _activeSquadStateCount <= 0)
        {
            return;
        }

        bool shouldRequest = _lastSquadAliveCountReadbackFrame < 0 ||
            Time.frameCount - _lastSquadAliveCountReadbackFrame >= SquadAliveCountReadbackIntervalFrames;
        if (!shouldRequest)
            return;

        int expectedCount = _squadAliveCountBuffer.count;
        int readbackVersion = _squadAliveCountReadbackVersion;
        _hasPendingSquadAliveCountReadback = true;
        _lastSquadAliveCountReadbackFrame = Time.frameCount;

        AsyncGPUReadback.Request(_squadAliveCountBuffer, request =>
        {
            if (readbackVersion != _squadAliveCountReadbackVersion)
                return;

            _hasPendingSquadAliveCountReadback = false;
            if (request.hasError)
                return;

            if (_runtimeSquadAliveCountReadbackCache == null || _runtimeSquadAliveCountReadbackCache.Length != expectedCount)
                _runtimeSquadAliveCountReadbackCache = new uint[expectedCount];

            var data = request.GetData<uint>();
            int copyCount = Mathf.Min(expectedCount, data.Length);
            for (int index = 0; index < copyCount; index++)
                _runtimeSquadAliveCountReadbackCache[index] = data[index];

            for (int index = copyCount; index < _runtimeSquadAliveCountReadbackCache.Length; index++)
                _runtimeSquadAliveCountReadbackCache[index] = 0u;
        });
    }

    private bool TryReadCombatStateBuffer()
    {
        if (!_enableGpuInstanceCombat ||
            _combatStateBuffer == null ||
            _instanceCount <= 0)
        {
            return false;
        }

        EnsureCombatStateReadbackCache();
        bool shouldRefresh = !Application.isPlaying || _lastCombatStateReadbackFrame != Time.frameCount;
        if (!shouldRefresh)
            return true;

        _combatStateBuffer.GetData(_combatStateReadbackCache, 0, 0, _instanceCount);
        _lastCombatStateReadbackFrame = Application.isPlaying ? Time.frameCount : -1;
        return true;
    }

    private CrowdVatInstanceCombatState ConvertCombatStateData(int instanceIndex, InstanceCombatStateData data)
    {
        uint flags = data.flags;
        bool hasTarget = (flags & InstanceCombatFlagHasTarget) != 0u;
        Vector3 localOrigin = new Vector3(data.localOriginAndDistance.x, data.localOriginAndDistance.y, data.localOriginAndDistance.z);
        Vector3 localTarget = hasTarget
            ? new Vector3(data.localTargetAndCooldown.x, data.localTargetAndCooldown.y, data.localTargetAndCooldown.z)
            : localOrigin;
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

        return new CrowdVatInstanceCombatState
        {
            instanceIndex = instanceIndex,
            active = (flags & InstanceCombatFlagActive) != 0u,
            dead = (flags & InstanceCombatFlagDead) != 0u,
            hasTarget = hasTarget,
            hasLineOfSight = (flags & InstanceCombatFlagHasLineOfSight) != 0u,
            firedThisFrame = (flags & InstanceCombatFlagFiredThisFrame) != 0u,
            targetIndex = hasTarget ? data.targetIndex : -1,
            distance = distance,
            muzzleFlash = data.muzzleFlash,
            health = data.healthAndHitFeedback.x,
            normalizedHealth = data.healthAndHitFeedback.w,
            hitFlash = data.healthAndHitFeedback.y,
            lastDamage = data.healthAndHitFeedback.z,
            worldOrigin = transform.TransformPoint(localOrigin),
            worldTargetPoint = transform.TransformPoint(localTarget),
            hasImpact = hasImpact,
            worldImpactNormal = worldImpactNormal
        };
    }

    private void EnsureCombatStateReadbackCache()
    {
        int requiredLength = Mathf.Max(1, _instanceCount);
        if (_combatStateReadbackCache == null || _combatStateReadbackCache.Length != requiredLength)
            _combatStateReadbackCache = new InstanceCombatStateData[requiredLength];
    }

    private void EnsureSpatialQueryReadbackCaches()
    {
        if (_spatialQueryUploadCache == null || _spatialQueryUploadCache.Length != _maxSpatialQueries)
            _spatialQueryUploadCache = new SpatialQueryData[_maxSpatialQueries];

        if (_spatialQueryResultCache == null || _spatialQueryResultCache.Length != _maxSpatialQueries)
            _spatialQueryResultCache = new SpatialQueryResultData[_maxSpatialQueries];

        int hitCapacity = _maxSpatialQueries * _maxHitsPerSpatialQuery;
        if (_spatialQueryHitCache == null || _spatialQueryHitCache.Length != hitCapacity)
            _spatialQueryHitCache = new SpatialQueryHitData[hitCapacity];
    }

    private CrowdVatObservationFrameInfo CreateSquadAliveObservationFrameInfo()
    {
        bool usesCpuMirror = _runtimeSquadAliveCountDirty ||
            _squadAliveCountBuffer == null ||
            _runtimeSquadAliveCountReadbackCache == null ||
            _runtimeSquadAliveCountReadbackCache.Length < _activeSquadStateCount;

        return new CrowdVatObservationFrameInfo
        {
            frameCount = Application.isPlaying ? Time.frameCount : -1,
            sourceKind = usesCpuMirror
                ? CrowdVatObservationSourceKind.CpuMirror
                : CrowdVatObservationSourceKind.SyncGpuReadback,
            isStale = _hasPendingSquadAliveCountReadback
        };
    }
}
