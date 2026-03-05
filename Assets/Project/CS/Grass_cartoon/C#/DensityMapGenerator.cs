using UnityEngine;

public static class DensityMapGenerator
{
    public static Texture2D GenerateDensityMap(
        Vector3 terrainSize,
        GrassFeature.GrassClumpSettings[] clumpSettings,
        int resolution = 128)
    {
        // Create R8 texture
        Texture2D densityMap = new Texture2D(resolution, resolution, TextureFormat.R8, false, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "T_Grass_DensityMap"
        };

        // Iterate all pixels, calculate Voronoi density
        Color[] pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float2 uv = new float2((float)x / resolution, (float)y / resolution);
                float density = CalculateVoronoiDensity(uv, terrainSize, clumpSettings);
                pixels[y * resolution + x] = new Color(density, 0, 0, 1);
            }
        }

        densityMap.SetPixels(pixels);
        densityMap.Apply();
        return densityMap;
    }

    private static float CalculateVoronoiDensity(float2 uv, Vector3 terrainSize, GrassFeature.GrassClumpSettings[] clumps)
    {
        if (clumps == null || clumps.Length == 0)
            return 0.15f;

        float maxDensity = 0.15f;

        // Evaluate density for each clump type and take the maximum
        for (int i = 0; i < clumps.Length; i++)
        {
            var clump = clumps[i];
            float scale = Mathf.Max(clump.voronoiScale, 0.001f);

            // 3x3 Voronoi neighborhood search
            float2 st = new float2(uv.x * terrainSize.x * scale, uv.y * terrainSize.z * scale);
            float2 i_st = Floor(st);
            float2 f_st = Frac(st);

            float minDist = 10.0f;

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    float2 neighbor = new float2(x, y);
                    float2 currentId = new float2(i_st.x + neighbor.x, i_st.y + neighbor.y);

                    // Hash function matching the compute shader
                    float2 p = Frac(new float2(
                        Dot(currentId, new float2(127.1f, 311.7f)),
                        Dot(currentId, new float2(269.5f, 183.3f))
                    ) * 43758.5453f);

                    float2 diff = new float2(neighbor.x + p.x - f_st.x, neighbor.y + p.y - f_st.y);
                    float dist = Length(diff);

                    if (dist < minDist)
                    {
                        minDist = dist;
                    }
                }
            }

            // Calculate valid radius based on cluster tightness
            float validRadius = Mathf.Lerp(1.2f, 0.3f, clump.clusterTightness);

            // Calculate density based on distance from cluster center
            // Closer to center = higher density
            float density = 1.0f - Smoothstep(validRadius * 0.35f, validRadius, minDist);
            density = Mathf.Max(density, 0.15f);

            maxDensity = Mathf.Max(maxDensity, density);
        }

        return maxDensity;
    }

    // Helper struct for float2 math
    private struct float2
    {
        public float x;
        public float y;

        public float2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    // Helper methods for float2 math
    private static float2 Floor(float2 v)
    {
        return new float2(Mathf.Floor(v.x), Mathf.Floor(v.y));
    }

    private static float2 Frac(float2 v)
    {
        return new float2(v.x - Mathf.Floor(v.x), v.y - Mathf.Floor(v.y));
    }

    private static float Dot(float2 a, float2 b)
    {
        return a.x * b.x + a.y * b.y;
    }

    private static float Length(float2 v)
    {
        return Mathf.Sqrt(v.x * v.x + v.y * v.y);
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3.0f - 2.0f * t);
    }
}
