using UnityEngine;

public static class NewGrassBladeMeshFactory
{
    private struct BladeRow
    {
        public BladeRow(float t, float y, float halfWidth, float uNegative, float uPositive, float uvY)
        {
            this.t = t;
            this.y = y;
            this.halfWidth = halfWidth;
            this.uNegative = uNegative;
            this.uPositive = uPositive;
            this.uvY = uvY;
        }

        public float t;
        public float y;
        public float halfWidth;
        public float uNegative;
        public float uPositive;
        public float uvY;
    }

    private static readonly BladeRow[] k_BladeRows =
    {
        new BladeRow(0.000000f, 0.00000f, 0.03444f, 0.550490f, 0.450038f, 0.000000f),
        new BladeRow(0.141177f, 0.15599f, 0.03445f, 0.550516f, 0.450011f, 0.220262f),
        new BladeRow(0.286275f, 0.27249f, 0.03193f, 0.546832f, 0.453695f, 0.384773f),
        new BladeRow(0.427451f, 0.38111f, 0.02942f, 0.543177f, 0.457350f, 0.538140f),
        new BladeRow(0.572549f, 0.47325f, 0.02620f, 0.538472f, 0.462055f, 0.668258f),
        new BladeRow(0.713726f, 0.55531f, 0.02338f, 0.534360f, 0.466167f, 0.784132f),
        new BladeRow(0.858824f, 0.63064f, 0.01728f, 0.525474f, 0.475053f, 0.890497f),
        new BladeRow(1.000000f, 0.70819f, 0.00000f, 0.500264f, 0.500264f, 1.000000f)
    };

    public static Mesh CreateHighLODMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "NewGrassBlade_HighLOD"
        };

        mesh.vertices = new Vector3[]
        {
            new Vector3(0.000000f, 0.15599f, 0.03445f),
            new Vector3(0.000000f, 0.00000f, -0.03444f),
            new Vector3(0.000000f, 0.00000f, 0.03444f),
            new Vector3(0.000000f, 0.15599f, -0.03445f),
            new Vector3(0.000000f, 0.27249f, -0.03193f),
            new Vector3(0.000000f, 0.27249f, 0.03193f),
            new Vector3(0.000000f, 0.38111f, -0.02942f),
            new Vector3(0.000000f, 0.38111f, 0.02942f),
            new Vector3(0.000000f, 0.47325f, -0.02620f),
            new Vector3(0.000000f, 0.47325f, 0.02620f),
            new Vector3(0.000000f, 0.55531f, -0.02338f),
            new Vector3(0.000000f, 0.55531f, 0.02338f),
            new Vector3(0.000000f, 0.63064f, -0.01728f),
            new Vector3(0.000000f, 0.63064f, 0.01728f),
            new Vector3(0.000000f, 0.70819f, 0.00000f)
        };

        mesh.triangles = new int[]
        {
            0, 1, 2,
            0, 3, 1,
            0, 4, 3,
            0, 5, 4,
            5, 6, 4,
            5, 7, 6,
            7, 8, 6,
            7, 9, 8,
            9, 10, 8,
            9, 11, 10,
            12, 10, 11,
            11, 13, 12,
            13, 14, 12
        };

        mesh.colors = new Color[]
        {
            new Color(0.141177f, 1.000000f, 0.000000f, 1.000000f),
            new Color(0.000000f, 0.000000f, 0.000000f, 1.000000f),
            new Color(0.000000f, 1.000000f, 0.000000f, 1.000000f),
            new Color(0.141177f, 0.000000f, 0.000000f, 1.000000f),
            new Color(0.286275f, 0.000000f, 0.000000f, 1.000000f),
            new Color(0.286275f, 1.000000f, 0.000000f, 1.000000f),
            new Color(0.427451f, 0.000000f, 0.000000f, 1.000000f),
            new Color(0.427451f, 1.000000f, 0.000000f, 1.000000f),
            new Color(0.572549f, 0.000000f, 0.000000f, 1.000000f),
            new Color(0.572549f, 1.000000f, 0.000000f, 1.000000f),
            new Color(0.713726f, 0.000000f, 0.000000f, 1.000000f),
            new Color(0.713726f, 1.000000f, 0.000000f, 1.000000f),
            new Color(0.858824f, 0.000000f, 0.000000f, 1.000000f),
            new Color(0.858824f, 1.000000f, 0.000000f, 1.000000f),
            new Color(1.000000f, 0.498039f, 0.000000f, 1.000000f)
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(0.450011f, 0.220262f),
            new Vector2(0.550490f, 0.000000f),
            new Vector2(0.450038f, 0.000000f),
            new Vector2(0.550516f, 0.220262f),
            new Vector2(0.546832f, 0.384773f),
            new Vector2(0.453695f, 0.384773f),
            new Vector2(0.543177f, 0.538140f),
            new Vector2(0.457350f, 0.538140f),
            new Vector2(0.538472f, 0.668258f),
            new Vector2(0.462055f, 0.668258f),
            new Vector2(0.534360f, 0.784132f),
            new Vector2(0.466167f, 0.784132f),
            new Vector2(0.525474f, 0.890497f),
            new Vector2(0.475053f, 0.890497f),
            new Vector2(0.500264f, 1.000000f)
        };

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.bounds = new Bounds(new Vector3(-0.5f, 0.5f, 0.0f), new Vector3(2.5f, 2.5f, 0.5f));
        return mesh;
    }

    public static Mesh CreateMidLODMesh()
    {
        return CreateStripMesh("NewGrassBlade_MidLOD", new[] { 0, 2, 4, 6, 7 });
    }

    public static Mesh CreateLowLODMesh()
    {
        return CreateStripMesh("NewGrassBlade_LowLOD", new[] { 0, 4, 7 });
    }

    private static Mesh CreateStripMesh(string name, int[] rowIndices)
    {
        Mesh mesh = new Mesh
        {
            name = name
        };

        Vector3[] vertices = BuildVertices(rowIndices);
        Color[] colors = BuildColors(rowIndices);
        Vector2[] uvs = BuildUVs(rowIndices);
        int[] triangles = BuildTriangles(rowIndices);

        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.bounds = new Bounds(new Vector3(-0.5f, 0.5f, 0.0f), new Vector3(2.5f, 2.5f, 0.5f));
        return mesh;
    }

    private static Vector3[] BuildVertices(int[] rowIndices)
    {
        Vector3[] vertices = new Vector3[ComputeVertexCount(rowIndices)];
        int vertexIndex = 0;

        for (int i = 0; i < rowIndices.Length; i++)
        {
            BladeRow row = k_BladeRows[rowIndices[i]];
            if (row.halfWidth <= 0.0f)
            {
                vertices[vertexIndex++] = new Vector3(0.0f, row.y, 0.0f);
                continue;
            }

            vertices[vertexIndex++] = new Vector3(0.0f, row.y, -row.halfWidth);
            vertices[vertexIndex++] = new Vector3(0.0f, row.y, row.halfWidth);
        }

        return vertices;
    }

    private static Color[] BuildColors(int[] rowIndices)
    {
        Color[] colors = new Color[ComputeVertexCount(rowIndices)];
        int vertexIndex = 0;

        for (int i = 0; i < rowIndices.Length; i++)
        {
            BladeRow row = k_BladeRows[rowIndices[i]];
            if (row.halfWidth <= 0.0f)
            {
                colors[vertexIndex++] = new Color(row.t, 0.5f, 0.0f, 1.0f);
                continue;
            }

            colors[vertexIndex++] = new Color(row.t, 0.0f, 0.0f, 1.0f);
            colors[vertexIndex++] = new Color(row.t, 1.0f, 0.0f, 1.0f);
        }

        return colors;
    }

    private static Vector2[] BuildUVs(int[] rowIndices)
    {
        Vector2[] uvs = new Vector2[ComputeVertexCount(rowIndices)];
        int vertexIndex = 0;

        for (int i = 0; i < rowIndices.Length; i++)
        {
            BladeRow row = k_BladeRows[rowIndices[i]];
            if (row.halfWidth <= 0.0f)
            {
                uvs[vertexIndex++] = new Vector2(row.uNegative, row.uvY);
                continue;
            }

            uvs[vertexIndex++] = new Vector2(row.uNegative, row.uvY);
            uvs[vertexIndex++] = new Vector2(row.uPositive, row.uvY);
        }

        return uvs;
    }

    private static int[] BuildTriangles(int[] rowIndices)
    {
        int[] rowVertexStarts = new int[rowIndices.Length];
        int vertexIndex = 0;
        int triangleIndexCount = 0;

        for (int i = 0; i < rowIndices.Length; i++)
        {
            rowVertexStarts[i] = vertexIndex;
            vertexIndex += k_BladeRows[rowIndices[i]].halfWidth <= 0.0f ? 1 : 2;
        }

        for (int i = 0; i < rowIndices.Length - 1; i++)
        {
            bool currentIsTip = k_BladeRows[rowIndices[i]].halfWidth <= 0.0f;
            bool nextIsTip = k_BladeRows[rowIndices[i + 1]].halfWidth <= 0.0f;

            if (currentIsTip)
                break;

            triangleIndexCount += nextIsTip ? 3 : 6;
        }

        int[] triangles = new int[triangleIndexCount];
        int triangleIndex = 0;

        for (int i = 0; i < rowIndices.Length - 1; i++)
        {
            bool currentIsTip = k_BladeRows[rowIndices[i]].halfWidth <= 0.0f;
            bool nextIsTip = k_BladeRows[rowIndices[i + 1]].halfWidth <= 0.0f;
            if (currentIsTip)
                break;

            int currentStart = rowVertexStarts[i];
            int nextStart = rowVertexStarts[i + 1];

            if (nextIsTip)
            {
                triangles[triangleIndex++] = currentStart + 1;
                triangles[triangleIndex++] = nextStart;
                triangles[triangleIndex++] = currentStart;
                continue;
            }

            triangles[triangleIndex++] = currentStart + 1;
            triangles[triangleIndex++] = nextStart;
            triangles[triangleIndex++] = currentStart;

            triangles[triangleIndex++] = currentStart + 1;
            triangles[triangleIndex++] = nextStart + 1;
            triangles[triangleIndex++] = nextStart;
        }

        return triangles;
    }

    private static int ComputeVertexCount(int[] rowIndices)
    {
        int count = 0;
        for (int i = 0; i < rowIndices.Length; i++)
            count += k_BladeRows[rowIndices[i]].halfWidth <= 0.0f ? 1 : 2;

        return count;
    }
}
