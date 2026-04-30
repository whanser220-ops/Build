using System;
using UnityEngine;

public static class CrowdVatSceneQueryUtility
{
    public static bool TrySampleGround(
        CrowdVatGroundFieldDescriptor descriptor,
        CrowdVatGroundQueryRequest request,
        out CrowdVatGroundQueryResult result)
    {
        result = new CrowdVatGroundQueryResult
        {
            queryId = request.queryId,
            valid = false,
            height = request.worldPosition.y,
            normal = Vector3.up,
            walkable = false,
            sourceFlags = CrowdVatGroundQuerySourceFlags.None,
            authoredFlags = 0u
        };

        bool hasTerrainSample = TrySampleTerrain(descriptor, request.worldPosition, out float terrainHeight, out Vector3 terrainNormal);
        bool hasWalkableMaskSample = TrySampleWalkableMask(descriptor, request.worldPosition, out bool walkableMaskValue);
        bool hasGroundFlagsSample = TrySampleGroundFlags(descriptor, request.worldPosition, out uint authoredFlags);

        bool walkable = hasWalkableMaskSample ? walkableMaskValue : hasTerrainSample;
        CrowdVatGroundQuerySourceFlags sourceFlags = CrowdVatGroundQuerySourceFlags.None;
        if (hasTerrainSample)
            sourceFlags |= CrowdVatGroundQuerySourceFlags.Terrain | CrowdVatGroundQuerySourceFlags.InBounds;
        if (hasWalkableMaskSample)
            sourceFlags |= CrowdVatGroundQuerySourceFlags.WalkableMask | CrowdVatGroundQuerySourceFlags.InBounds;
        if (hasGroundFlagsSample)
            sourceFlags |= CrowdVatGroundQuerySourceFlags.GroundFlagsTexture | CrowdVatGroundQuerySourceFlags.InBounds;
        if (walkable)
            sourceFlags |= CrowdVatGroundQuerySourceFlags.Walkable;

        result.height = hasTerrainSample ? terrainHeight + descriptor.terrainHeightOffset : request.worldPosition.y;
        result.normal = hasTerrainSample ? terrainNormal : Vector3.up;
        result.walkable = walkable;
        result.sourceFlags = sourceFlags;
        result.authoredFlags = authoredFlags;

        switch (request.queryType)
        {
            case CrowdVatGroundQueryType.SampleHeight:
            case CrowdVatGroundQueryType.SampleNormal:
                result.valid = hasTerrainSample;
                break;
            case CrowdVatGroundQueryType.CheckWalkable:
                result.valid = hasWalkableMaskSample || hasTerrainSample;
                break;
            default:
                result.valid = hasTerrainSample || hasWalkableMaskSample || hasGroundFlagsSample;
                break;
        }

        return result.valid;
    }

    public static bool TrySampleObstacleDistance(
        CrowdVatObstacleDistanceFieldDescriptor descriptor,
        CrowdVatObstacleDistanceQueryRequest request,
        out CrowdVatObstacleDistanceQueryResult result)
    {
        result = new CrowdVatObstacleDistanceQueryResult
        {
            queryId = request.queryId,
            valid = false,
            signedDistance = float.PositiveInfinity,
            gradient = Vector3.zero,
            inside = false,
            sourceFlags = CrowdVatObstacleDistanceSourceFlags.None
        };

        bool needsGradient =
            request.queryType == CrowdVatObstacleDistanceQueryType.SampleGradient ||
            request.queryType == CrowdVatObstacleDistanceQueryType.SampleSignedDistance;
        if (!TrySampleObstacleDistance(
                descriptor,
                request.worldPosition,
                request.gradientSampleDistance,
                needsGradient,
                out float signedDistance,
                out Vector3 gradient))
        {
            return false;
        }

        bool inside = signedDistance < 0.0f;
        CrowdVatObstacleDistanceSourceFlags sourceFlags =
            CrowdVatObstacleDistanceSourceFlags.StaticSdf |
            CrowdVatObstacleDistanceSourceFlags.InBounds |
            CrowdVatObstacleDistanceSourceFlags.ApproximateCpuSample;
        if (inside)
            sourceFlags |= CrowdVatObstacleDistanceSourceFlags.InsideObstacle;

        result.valid = true;
        result.signedDistance = signedDistance;
        result.gradient = gradient;
        result.inside = inside;
        result.sourceFlags = sourceFlags;
        return true;
    }

    public static bool TrySampleEnvironmentDistance(
        CrowdVatEnvironmentDistanceFieldDescriptor descriptor,
        CrowdVatEnvironmentDistanceQueryRequest request,
        out CrowdVatEnvironmentDistanceQueryResult result)
    {
        result = new CrowdVatEnvironmentDistanceQueryResult
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

        bool sampleGradient = request.queryType != CrowdVatEnvironmentDistanceQueryType.SampleCombinedSignedDistance;
        if (TrySampleBakedEnvironmentDistance(
                descriptor.bakedField,
                request.worldPosition,
                request.gradientSampleDistance,
                sampleGradient,
                out float bakedSignedDistance,
                out Vector3 bakedGradient))
        {
            result.valid = true;
            result.combinedSignedDistance = bakedSignedDistance;
            result.combinedGradient = bakedGradient;
            result.inside = bakedSignedDistance < 0.0f;
            result.sourceFlags =
                CrowdVatEnvironmentDistanceSourceFlags.BakedEnvironmentField |
                CrowdVatEnvironmentDistanceSourceFlags.ApproximateCpuSample;
            return true;
        }

        bool hasTerrain = TrySampleTerrainDistance(
            descriptor.groundField,
            request.worldPosition,
            out float terrainHeight,
            out float terrainSignedDistance,
            out Vector3 terrainGradient);
        bool hasObstacle = TrySampleObstacleDistance(
            descriptor.obstacleField,
            request.worldPosition,
            request.gradientSampleDistance,
            sampleGradient,
            out float obstacleSignedDistance,
            out Vector3 obstacleGradient);
        if (!hasTerrain && !hasObstacle)
            return false;

        CrowdVatEnvironmentDistanceSourceFlags sourceFlags = CrowdVatEnvironmentDistanceSourceFlags.None;
        if (hasTerrain)
        {
            sourceFlags |=
                CrowdVatEnvironmentDistanceSourceFlags.Terrain |
                CrowdVatEnvironmentDistanceSourceFlags.TerrainInBounds |
                CrowdVatEnvironmentDistanceSourceFlags.ApproximateCpuSample;
            if (terrainSignedDistance < 0.0f)
                sourceFlags |= CrowdVatEnvironmentDistanceSourceFlags.TerrainBelowSurface;
        }

        if (hasObstacle)
        {
            sourceFlags |=
                CrowdVatEnvironmentDistanceSourceFlags.StaticSdf |
                CrowdVatEnvironmentDistanceSourceFlags.ObstacleInBounds |
                CrowdVatEnvironmentDistanceSourceFlags.ApproximateCpuSample;
            if (obstacleSignedDistance < 0.0f)
                sourceFlags |= CrowdVatEnvironmentDistanceSourceFlags.InsideObstacle;
        }

        float combinedSignedDistance = float.PositiveInfinity;
        Vector3 combinedGradient = Vector3.zero;
        if (hasTerrain && (!hasObstacle || terrainSignedDistance <= obstacleSignedDistance))
        {
            combinedSignedDistance = terrainSignedDistance;
            combinedGradient = terrainGradient;
            sourceFlags |= CrowdVatEnvironmentDistanceSourceFlags.CombinedFromTerrain;
        }
        else if (hasObstacle)
        {
            combinedSignedDistance = obstacleSignedDistance;
            combinedGradient = obstacleGradient;
            sourceFlags |= CrowdVatEnvironmentDistanceSourceFlags.CombinedFromObstacle;
        }

        result.valid = true;
        result.hasTerrain = hasTerrain;
        result.terrainHeight = terrainHeight;
        result.terrainSignedDistance = terrainSignedDistance;
        result.terrainGradient = terrainGradient;
        result.hasObstacle = hasObstacle;
        result.obstacleSignedDistance = obstacleSignedDistance;
        result.obstacleGradient = obstacleGradient;
        result.combinedSignedDistance = combinedSignedDistance;
        result.combinedGradient = combinedGradient;
        result.inside = combinedSignedDistance < 0.0f;
        result.sourceFlags = sourceFlags;
        return true;
    }

    public static int EvaluateGroundQueries(
        CrowdVatGroundFieldDescriptor descriptor,
        CrowdVatGroundQueryRequest[] requests,
        System.Collections.Generic.List<CrowdVatGroundQueryResult> results)
    {
        if (requests == null)
            throw new ArgumentNullException(nameof(requests));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();

        int validCount = 0;
        for (int index = 0; index < requests.Length; index++)
        {
            if (TrySampleGround(descriptor, requests[index], out CrowdVatGroundQueryResult result))
                validCount++;

            results.Add(result);
        }

        return validCount;
    }

    public static int EvaluateObstacleDistanceQueries(
        CrowdVatObstacleDistanceFieldDescriptor descriptor,
        CrowdVatObstacleDistanceQueryRequest[] requests,
        System.Collections.Generic.List<CrowdVatObstacleDistanceQueryResult> results)
    {
        if (requests == null)
            throw new ArgumentNullException(nameof(requests));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();

        int validCount = 0;
        for (int index = 0; index < requests.Length; index++)
        {
            if (TrySampleObstacleDistance(descriptor, requests[index], out CrowdVatObstacleDistanceQueryResult result))
                validCount++;

            results.Add(result);
        }

        return validCount;
    }

    public static int EvaluateEnvironmentDistanceQueries(
        CrowdVatEnvironmentDistanceFieldDescriptor descriptor,
        CrowdVatEnvironmentDistanceQueryRequest[] requests,
        System.Collections.Generic.List<CrowdVatEnvironmentDistanceQueryResult> results)
    {
        if (requests == null)
            throw new ArgumentNullException(nameof(requests));
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();

        int validCount = 0;
        for (int index = 0; index < requests.Length; index++)
        {
            if (TrySampleEnvironmentDistance(descriptor, requests[index], out CrowdVatEnvironmentDistanceQueryResult result))
                validCount++;

            results.Add(result);
        }

        return validCount;
    }

    private static bool TryWorldToBakedEnvironmentDistanceUv(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 worldPosition,
        out Vector3 uvw)
    {
        uvw = Vector3.zero;
        if (descriptor.sdfTexture == null)
            return false;

        if (descriptor.worldSize.x <= 0.0f || descriptor.worldSize.y <= 0.0f || descriptor.worldSize.z <= 0.0f)
            return false;

        Vector3 min = descriptor.worldCenter - descriptor.worldSize * 0.5f;
        uvw = new Vector3(
            (worldPosition.x - min.x) / descriptor.worldSize.x,
            (worldPosition.y - min.y) / descriptor.worldSize.y,
            (worldPosition.z - min.z) / descriptor.worldSize.z);
        return uvw.x >= 0.0f && uvw.x <= 1.0f &&
            uvw.y >= 0.0f && uvw.y <= 1.0f &&
            uvw.z >= 0.0f && uvw.z <= 1.0f;
    }

    private static bool TrySampleTerrain(
        CrowdVatGroundFieldDescriptor descriptor,
        Vector3 worldPosition,
        out float height,
        out Vector3 normal)
    {
        height = worldPosition.y;
        normal = Vector3.up;
        if (descriptor.terrain == null || descriptor.terrain.terrainData == null)
            return false;

        Vector3 terrainPosition = descriptor.terrain.transform.position;
        Vector3 terrainSize = descriptor.terrain.terrainData.size;
        if (terrainSize.x <= 0.0f || terrainSize.z <= 0.0f)
            return false;

        float normalizedX = (worldPosition.x - terrainPosition.x) / terrainSize.x;
        float normalizedZ = (worldPosition.z - terrainPosition.z) / terrainSize.z;
        if (normalizedX < 0.0f || normalizedX > 1.0f || normalizedZ < 0.0f || normalizedZ > 1.0f)
            return false;

        height = descriptor.terrain.SampleHeight(worldPosition) + terrainPosition.y;
        normal = descriptor.terrain.terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
        return true;
    }

    private static bool TrySampleTerrainDistance(
        CrowdVatGroundFieldDescriptor descriptor,
        Vector3 worldPosition,
        out float height,
        out float signedDistance,
        out Vector3 gradient)
    {
        height = worldPosition.y;
        signedDistance = float.PositiveInfinity;
        gradient = Vector3.up;
        if (!TrySampleTerrain(descriptor, worldPosition, out float sampledHeight, out Vector3 sampledNormal))
            return false;

        height = sampledHeight + descriptor.terrainHeightOffset;
        gradient = sampledNormal.sqrMagnitude > 1e-8f ? sampledNormal.normalized : Vector3.up;
        Vector3 surfacePoint = new Vector3(worldPosition.x, height, worldPosition.z);
        signedDistance = Vector3.Dot(worldPosition - surfacePoint, gradient);
        return true;
    }

    private static bool TrySampleWalkableMask(
        CrowdVatGroundFieldDescriptor descriptor,
        Vector3 worldPosition,
        out bool walkable)
    {
        walkable = false;
        Texture2D texture = descriptor.walkableMaskTexture;
        if (texture == null || !texture.isReadable)
            return false;

        if (!TryGetGroundTextureUv(descriptor, worldPosition, out Vector2 uv))
            return false;

        walkable = texture.GetPixelBilinear(uv.x, uv.y).r >= 0.5f;
        return true;
    }

    private static bool TrySampleGroundFlags(
        CrowdVatGroundFieldDescriptor descriptor,
        Vector3 worldPosition,
        out uint authoredFlags)
    {
        authoredFlags = 0u;
        Texture2D texture = descriptor.groundFlagsTexture;
        if (texture == null || !texture.isReadable)
            return false;

        if (!TryGetGroundTextureUv(descriptor, worldPosition, out Vector2 uv))
            return false;

        authoredFlags = (uint)Mathf.Clamp(Mathf.RoundToInt(texture.GetPixelBilinear(uv.x, uv.y).r * 255.0f), 0, 255);
        return true;
    }

    private static bool TryGetGroundTextureUv(
        CrowdVatGroundFieldDescriptor descriptor,
        Vector3 worldPosition,
        out Vector2 uv)
    {
        uv = Vector2.zero;

        if (descriptor.useTerrainBoundsForTextures &&
            descriptor.terrain != null &&
            descriptor.terrain.terrainData != null)
        {
            Vector3 terrainPosition = descriptor.terrain.transform.position;
            Vector3 terrainSize = descriptor.terrain.terrainData.size;
            if (terrainSize.x <= 0.0f || terrainSize.z <= 0.0f)
                return false;

            float u = (worldPosition.x - terrainPosition.x) / terrainSize.x;
            float v = (worldPosition.z - terrainPosition.z) / terrainSize.z;
            if (u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f)
                return false;

            uv = new Vector2(u, v);
            return true;
        }

        if (descriptor.worldSize.x <= 0.0f || descriptor.worldSize.y <= 0.0f)
            return false;

        float minX = descriptor.worldCenter.x - descriptor.worldSize.x * 0.5f;
        float minZ = descriptor.worldCenter.z - descriptor.worldSize.y * 0.5f;
        float uExplicit = (worldPosition.x - minX) / descriptor.worldSize.x;
        float vExplicit = (worldPosition.z - minZ) / descriptor.worldSize.y;
        if (uExplicit < 0.0f || uExplicit > 1.0f || vExplicit < 0.0f || vExplicit > 1.0f)
            return false;

        uv = new Vector2(uExplicit, vExplicit);
        return true;
    }

    private static bool TryWorldToSdfUv(
        CrowdVatObstacleDistanceFieldDescriptor descriptor,
        Vector3 worldPosition,
        out Vector3 uvw)
    {
        uvw = Vector3.zero;
        if (descriptor.sdfTexture == null)
            return false;

        if (descriptor.worldSize.x <= 0.0f || descriptor.worldSize.y <= 0.0f || descriptor.worldSize.z <= 0.0f)
            return false;

        Vector3 min = descriptor.worldCenter - descriptor.worldSize * 0.5f;
        uvw = new Vector3(
            (worldPosition.x - min.x) / descriptor.worldSize.x,
            (worldPosition.y - min.y) / descriptor.worldSize.y,
            (worldPosition.z - min.z) / descriptor.worldSize.z);
        return uvw.x >= 0.0f && uvw.x <= 1.0f &&
            uvw.y >= 0.0f && uvw.y <= 1.0f &&
            uvw.z >= 0.0f && uvw.z <= 1.0f;
    }

    private static bool TrySampleObstacleDistance(
        CrowdVatObstacleDistanceFieldDescriptor descriptor,
        Vector3 worldPosition,
        float gradientSampleDistance,
        bool sampleGradient,
        out float signedDistance,
        out Vector3 gradient)
    {
        signedDistance = float.PositiveInfinity;
        gradient = Vector3.zero;
        if (descriptor.sdfTexture == null || !descriptor.sdfTexture.isReadable)
            return false;

        if (!TryWorldToSdfUv(descriptor, worldPosition, out Vector3 uvw))
            return false;

        signedDistance = SampleObstacleDistance(descriptor, uvw);
        if (sampleGradient)
        {
            float sampleOffset = gradientSampleDistance > 1e-4f
                ? gradientSampleDistance
                : ComputeDefaultGradientOffset(descriptor);
            gradient = EstimateObstacleGradient(descriptor, worldPosition, sampleOffset);
        }

        return true;
    }

    private static float SampleObstacleDistance(CrowdVatObstacleDistanceFieldDescriptor descriptor, Vector3 uvw)
    {
        Color sample = SampleTexture3DTrilinear(descriptor.sdfTexture, uvw);
        return sample.r * descriptor.distanceScale + descriptor.distanceBias;
    }

    private static Vector3 EstimateObstacleGradient(
        CrowdVatObstacleDistanceFieldDescriptor descriptor,
        Vector3 worldPosition,
        float sampleOffset)
    {
        Vector3 offsetX = new Vector3(sampleOffset, 0.0f, 0.0f);
        Vector3 offsetY = new Vector3(0.0f, sampleOffset, 0.0f);
        Vector3 offsetZ = new Vector3(0.0f, 0.0f, sampleOffset);

        float dx = SampleObstacleDistanceClamped(descriptor, worldPosition + offsetX)
            - SampleObstacleDistanceClamped(descriptor, worldPosition - offsetX);
        float dy = SampleObstacleDistanceClamped(descriptor, worldPosition + offsetY)
            - SampleObstacleDistanceClamped(descriptor, worldPosition - offsetY);
        float dz = SampleObstacleDistanceClamped(descriptor, worldPosition + offsetZ)
            - SampleObstacleDistanceClamped(descriptor, worldPosition - offsetZ);

        Vector3 gradient = new Vector3(dx, dy, dz);
        return gradient.sqrMagnitude > 1e-8f ? gradient.normalized : Vector3.zero;
    }

    private static float SampleObstacleDistanceClamped(
        CrowdVatObstacleDistanceFieldDescriptor descriptor,
        Vector3 worldPosition)
    {
        Vector3 min = descriptor.worldCenter - descriptor.worldSize * 0.5f;
        Vector3 max = descriptor.worldCenter + descriptor.worldSize * 0.5f;
        Vector3 clampedPosition = new Vector3(
            Mathf.Clamp(worldPosition.x, min.x, max.x),
            Mathf.Clamp(worldPosition.y, min.y, max.y),
            Mathf.Clamp(worldPosition.z, min.z, max.z));

        if (!TryWorldToSdfUv(descriptor, clampedPosition, out Vector3 uvw))
            return float.PositiveInfinity;

        return SampleObstacleDistance(descriptor, uvw);
    }

    private static bool TrySampleBakedEnvironmentDistance(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 worldPosition,
        float gradientSampleDistance,
        bool sampleGradient,
        out float signedDistance,
        out Vector3 gradient)
    {
        signedDistance = float.PositiveInfinity;
        gradient = Vector3.zero;
        if (descriptor.sdfTexture == null || !descriptor.sdfTexture.isReadable)
            return false;

        if (!TryWorldToBakedEnvironmentDistanceUv(descriptor, worldPosition, out Vector3 uvw))
            return false;

        signedDistance = SampleBakedEnvironmentDistance(descriptor, uvw);
        if (sampleGradient)
        {
            float sampleOffset = gradientSampleDistance > 1e-4f
                ? gradientSampleDistance
                : ComputeDefaultGradientOffset(descriptor);
            gradient = EstimateBakedEnvironmentGradient(descriptor, worldPosition, sampleOffset);
        }

        return true;
    }

    private static float SampleBakedEnvironmentDistance(CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor, Vector3 uvw)
    {
        Color sample = SampleTexture3DTrilinear(descriptor.sdfTexture, uvw);
        return sample.r * descriptor.distanceScale + descriptor.distanceBias;
    }

    private static Vector3 EstimateBakedEnvironmentGradient(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 worldPosition,
        float sampleOffset)
    {
        Vector3 offsetX = new Vector3(sampleOffset, 0.0f, 0.0f);
        Vector3 offsetY = new Vector3(0.0f, sampleOffset, 0.0f);
        Vector3 offsetZ = new Vector3(0.0f, 0.0f, sampleOffset);

        float dx = SampleBakedEnvironmentDistanceClamped(descriptor, worldPosition + offsetX)
            - SampleBakedEnvironmentDistanceClamped(descriptor, worldPosition - offsetX);
        float dy = SampleBakedEnvironmentDistanceClamped(descriptor, worldPosition + offsetY)
            - SampleBakedEnvironmentDistanceClamped(descriptor, worldPosition - offsetY);
        float dz = SampleBakedEnvironmentDistanceClamped(descriptor, worldPosition + offsetZ)
            - SampleBakedEnvironmentDistanceClamped(descriptor, worldPosition - offsetZ);

        Vector3 gradient = new Vector3(dx, dy, dz);
        return gradient.sqrMagnitude > 1e-8f ? gradient.normalized : Vector3.zero;
    }

    private static float SampleBakedEnvironmentDistanceClamped(
        CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor,
        Vector3 worldPosition)
    {
        Vector3 min = descriptor.worldCenter - descriptor.worldSize * 0.5f;
        Vector3 max = descriptor.worldCenter + descriptor.worldSize * 0.5f;
        Vector3 clampedPosition = new Vector3(
            Mathf.Clamp(worldPosition.x, min.x, max.x),
            Mathf.Clamp(worldPosition.y, min.y, max.y),
            Mathf.Clamp(worldPosition.z, min.z, max.z));

        if (!TryWorldToBakedEnvironmentDistanceUv(descriptor, clampedPosition, out Vector3 uvw))
            return float.PositiveInfinity;

        return SampleBakedEnvironmentDistance(descriptor, uvw);
    }

    private static float ComputeDefaultGradientOffset(CrowdVatObstacleDistanceFieldDescriptor descriptor)
    {
        if (descriptor.sdfTexture == null)
            return 0.05f;

        float voxelSizeX = descriptor.worldSize.x / Mathf.Max(descriptor.sdfTexture.width - 1, 1);
        float voxelSizeY = descriptor.worldSize.y / Mathf.Max(descriptor.sdfTexture.height - 1, 1);
        float voxelSizeZ = descriptor.worldSize.z / Mathf.Max(descriptor.sdfTexture.depth - 1, 1);
        return Mathf.Max(0.01f, Mathf.Min(voxelSizeX, Mathf.Min(voxelSizeY, voxelSizeZ)));
    }

    private static float ComputeDefaultGradientOffset(CrowdVatBakedEnvironmentDistanceFieldDescriptor descriptor)
    {
        if (descriptor.sdfTexture == null)
            return 0.05f;

        float voxelSizeX = descriptor.worldSize.x / Mathf.Max(descriptor.sdfTexture.width - 1, 1);
        float voxelSizeY = descriptor.worldSize.y / Mathf.Max(descriptor.sdfTexture.height - 1, 1);
        float voxelSizeZ = descriptor.worldSize.z / Mathf.Max(descriptor.sdfTexture.depth - 1, 1);
        return Mathf.Max(0.01f, Mathf.Min(voxelSizeX, Mathf.Min(voxelSizeY, voxelSizeZ)));
    }

    private static Color SampleTexture3DTrilinear(Texture3D texture, Vector3 uvw)
    {
        float x = Mathf.Clamp01(uvw.x) * Mathf.Max(texture.width - 1, 0);
        float y = Mathf.Clamp01(uvw.y) * Mathf.Max(texture.height - 1, 0);
        float z = Mathf.Clamp01(uvw.z) * Mathf.Max(texture.depth - 1, 0);

        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, texture.width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, texture.height - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, texture.depth - 1);
        int x1 = Mathf.Min(x0 + 1, texture.width - 1);
        int y1 = Mathf.Min(y0 + 1, texture.height - 1);
        int z1 = Mathf.Min(z0 + 1, texture.depth - 1);

        float tx = x - x0;
        float ty = y - y0;
        float tz = z - z0;

        Color c000 = texture.GetPixel(x0, y0, z0);
        Color c100 = texture.GetPixel(x1, y0, z0);
        Color c010 = texture.GetPixel(x0, y1, z0);
        Color c110 = texture.GetPixel(x1, y1, z0);
        Color c001 = texture.GetPixel(x0, y0, z1);
        Color c101 = texture.GetPixel(x1, y0, z1);
        Color c011 = texture.GetPixel(x0, y1, z1);
        Color c111 = texture.GetPixel(x1, y1, z1);

        Color c00 = Color.LerpUnclamped(c000, c100, tx);
        Color c10 = Color.LerpUnclamped(c010, c110, tx);
        Color c01 = Color.LerpUnclamped(c001, c101, tx);
        Color c11 = Color.LerpUnclamped(c011, c111, tx);
        Color c0 = Color.LerpUnclamped(c00, c10, ty);
        Color c1 = Color.LerpUnclamped(c01, c11, ty);
        return Color.LerpUnclamped(c0, c1, tz);
    }
}
