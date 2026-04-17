using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralFlower : MonoBehaviour
{
    [Header("General")]
    public int seed = 1;
    public bool regenerateInEditMode = true;

    [Header("Stem")]
    [Min(0.05f)] public float stemHeight = 1.2f;
    [Min(0.005f)] public float stemRadius = 0.025f;
    [Range(3, 24)] public int stemSides = 8;

    [Header("Flower Center")]
    [Min(0.02f)] public float centerRadius = 0.10f;
    [Range(6, 24)] public int centerLongitude = 14;
    [Range(3, 12)] public int centerLatitude = 5;
    [Range(0.2f, 1.5f)] public float centerHeightScale = 0.55f;

    [Header("Petals")]
    [Range(3, 24)] public int petalCount = 10;
    [Min(0.05f)] public float petalLength = 0.38f;
    [Min(0.02f)] public float petalWidth = 0.16f;
    [Range(2, 16)] public int petalSegments = 7;
    [Min(0f)] public float petalBend = 0.03f;
    [Range(0f, 80f)] public float petalOpenAngle = 14f;
    [Range(0f, 20f)] public float petalOpenAngleJitter = 5f;
    [Range(0f, 20f)] public float petalAngleJitter = 4f;
    [Range(0f, 20f)] public float petalTwistJitter = 10f;
    [Range(0.2f, 2.0f)] public float petalWidthPower = 0.62f;
    [Range(0.0f, 1.0f)] public float petalBaseRadius = 0.6f;

    [Header("Leaves")]
    [Range(0, 4)] public int leafCount = 0;
    [Min(0.05f)] public float leafLength = 0.22f;
    [Min(0.02f)] public float leafWidth = 0.08f;
    [Range(2, 12)] public int leafSegments = 4;
    [Min(0f)] public float leafBend = 0.05f;

    [Header("Materials (SubMesh 0/1/2)")]
    public Material stemMaterial;
    public Material petalMaterial;
    public Material centerMaterial;

    private Mesh _generatedMesh;
    private Material _generatedStemMaterial;
    private Material _generatedPetalMaterial;
    private Material _generatedCenterMaterial;

    [ContextMenu("Generate Flower")]
    public void GenerateFlower()
    {
        System.Random rng = new System.Random(seed);
        MeshBuilder meshBuilder = new MeshBuilder(3);

        AddStem(meshBuilder, 0);
        AddLeaves(meshBuilder, 0, rng);
        AddCenter(meshBuilder, 2);
        AddPetals(meshBuilder, 1, rng);

        Mesh mesh = meshBuilder.ToMesh("ProceduralFlowerMesh");
        mesh.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        AssignMesh(mesh);
        AssignMaterials();
    }

    [ContextMenu("Apply Daisy Preset")]
    public void ApplyDaisyPreset()
    {
        stemHeight = 1.0f;
        stemRadius = 0.018f;
        stemSides = 6;

        centerRadius = 0.10f;
        centerLongitude = 14;
        centerLatitude = 5;
        centerHeightScale = 0.52f;

        petalCount = 11;
        petalLength = 0.40f;
        petalWidth = 0.17f;
        petalSegments = 8;
        petalBend = 0.025f;
        petalOpenAngle = 12f;
        petalOpenAngleJitter = 4f;
        petalAngleJitter = 4f;
        petalTwistJitter = 12f;
        petalWidthPower = 0.58f;
        petalBaseRadius = 0.7f;

        leafCount = 0;
        leafLength = 0.18f;
        leafWidth = 0.06f;
        leafSegments = 4;
        leafBend = 0.03f;

        GenerateFlower();
    }

    private void OnEnable()
    {
        if (Application.isPlaying || regenerateInEditMode)
            GenerateFlower();
    }

    private void Start()
    {
        if (Application.isPlaying && _generatedMesh == null)
            GenerateFlower();
    }

    private void OnValidate()
    {
        if (!regenerateInEditMode || !isActiveAndEnabled)
            return;

        GenerateFlower();
    }

    private void OnDisable()
    {
        CleanupGeneratedResources();
    }

    private void OnDestroy()
    {
        CleanupGeneratedResources();
    }

    private void AssignMesh(Mesh mesh)
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();

        if (_generatedMesh != null && meshFilter.sharedMesh == _generatedMesh)
            DestroyOwnedObject(_generatedMesh);

        _generatedMesh = mesh;
        meshFilter.sharedMesh = mesh;
    }

    private void AssignMaterials()
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        Material[] existingMaterials = meshRenderer.sharedMaterials;
        Material[] materials =
        {
            ResolveMaterial(stemMaterial, existingMaterials, 0, ref _generatedStemMaterial, "ProceduralFlower_Stem", new Color(0.42f, 0.60f, 0.30f)),
            ResolveMaterial(petalMaterial, existingMaterials, 1, ref _generatedPetalMaterial, "ProceduralFlower_Petal", new Color(0.98f, 0.97f, 0.92f)),
            ResolveMaterial(centerMaterial, existingMaterials, 2, ref _generatedCenterMaterial, "ProceduralFlower_Center", new Color(0.92f, 0.73f, 0.19f))
        };

        meshRenderer.sharedMaterials = materials;
    }

    private Material ResolveMaterial(Material preferredMaterial, Material[] existingMaterials, int index, ref Material cachedMaterial, string materialName, Color color)
    {
        if (preferredMaterial != null)
            return preferredMaterial;

        if (existingMaterials != null && index >= 0 && index < existingMaterials.Length && existingMaterials[index] != null)
            return existingMaterials[index];

        return GetOrCreateFallbackMaterial(ref cachedMaterial, materialName, color);
    }

    private Material GetOrCreateFallbackMaterial(ref Material cachedMaterial, string materialName, Color color)
    {
        if (cachedMaterial != null)
            return cachedMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        cachedMaterial = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
        };

        if (cachedMaterial.HasProperty("_BaseColor"))
            cachedMaterial.SetColor("_BaseColor", color);
        if (cachedMaterial.HasProperty("_Color"))
            cachedMaterial.SetColor("_Color", color);

        return cachedMaterial;
    }

    private void CleanupGeneratedResources()
    {
        if (_generatedMesh != null)
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh == _generatedMesh)
                meshFilter.sharedMesh = null;

            DestroyOwnedObject(_generatedMesh);
            _generatedMesh = null;
        }

        DestroyOwnedObject(_generatedStemMaterial);
        DestroyOwnedObject(_generatedPetalMaterial);
        DestroyOwnedObject(_generatedCenterMaterial);
        _generatedStemMaterial = null;
        _generatedPetalMaterial = null;
        _generatedCenterMaterial = null;
    }

    private void AddStem(MeshBuilder meshBuilder, int subMesh)
    {
        AddCylinder(
            meshBuilder,
            subMesh,
            baseCenter: Vector3.zero,
            height: Mathf.Max(0.05f, stemHeight),
            radius: Mathf.Max(0.005f, stemRadius),
            sides: Mathf.Max(3, stemSides));
    }

    private void AddCenter(MeshBuilder meshBuilder, int subMesh)
    {
        Vector3 centerPos = new Vector3(0f, stemHeight, 0f);

        AddUVSphere(
            meshBuilder,
            subMesh,
            center: centerPos,
            radius: Mathf.Max(0.02f, centerRadius),
            longitudes: Mathf.Max(3, centerLongitude),
            latitudes: Mathf.Max(2, centerLatitude),
            yScale: Mathf.Max(0.1f, centerHeightScale));
    }

    private void AddPetals(MeshBuilder meshBuilder, int subMesh, System.Random rng)
    {
        Vector3 flowerCenter = new Vector3(0f, stemHeight, 0f);
        int count = Mathf.Max(3, petalCount);

        for (int i = 0; i < count; i++)
        {
            float baseAngle = 360f * i / count;
            float yaw = baseAngle + Range(rng, -petalAngleJitter, petalAngleJitter);
            float roll = Range(rng, -petalTwistJitter, petalTwistJitter);
            float openAngle = petalOpenAngle + Range(rng, -petalOpenAngleJitter, petalOpenAngleJitter);
            float openAngleRad = openAngle * Mathf.Deg2Rad;

            Vector3 horizontalDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 dir = (horizontalDir * Mathf.Cos(openAngleRad) +
                           Vector3.up * Mathf.Sin(openAngleRad)).normalized;

            Quaternion rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
            Vector3 basePos = flowerCenter
                + horizontalDir * (centerRadius * petalBaseRadius)
                + Vector3.up * (centerRadius * 0.01f);

            AddRibbon(
                meshBuilder,
                subMesh,
                basePos,
                rotation,
                length: petalLength * Range(rng, 0.92f, 1.08f),
                width: petalWidth * Range(rng, 0.92f, 1.08f),
                segments: Mathf.Max(1, petalSegments),
                bend: petalBend * Range(rng, 0.9f, 1.1f),
                widthPower: petalWidthPower);
        }
    }

    private void AddLeaves(MeshBuilder meshBuilder, int subMesh, System.Random rng)
    {
        if (leafCount <= 0)
            return;

        int segments = Mathf.Max(1, leafSegments);

        for (int i = 0; i < leafCount; i++)
        {
            float side = (i % 2 == 0) ? -1f : 1f;
            float height01 = leafCount == 1
                ? 0.45f
                : Mathf.Lerp(0.35f, 0.65f, i / Mathf.Max(1f, leafCount - 1f));
            float y = stemHeight * height01;

            float yaw = (side < 0f ? -70f : 70f) + Range(rng, -18f, 18f);
            float pitch = Range(rng, 15f, 35f);

            Vector3 horizontalDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 dir = (horizontalDir * Mathf.Cos(pitch * Mathf.Deg2Rad) +
                           Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;

            Quaternion rotation = Quaternion.LookRotation(dir, Vector3.up)
                * Quaternion.Euler(0f, 0f, side * Range(rng, 8f, 18f));

            AddRibbon(
                meshBuilder,
                subMesh,
                new Vector3(0f, y, 0f),
                rotation,
                length: leafLength * Range(rng, 0.9f, 1.15f),
                width: leafWidth * Range(rng, 0.9f, 1.1f),
                segments: segments,
                bend: leafBend * Range(rng, 0.9f, 1.15f),
                widthPower: 0.55f);
        }
    }

    private static void AddRibbon(
        MeshBuilder meshBuilder,
        int subMesh,
        Vector3 basePosition,
        Quaternion rotation,
        float length,
        float width,
        int segments,
        float bend,
        float widthPower)
    {
        Matrix4x4 matrix = Matrix4x4.TRS(basePosition, rotation, Vector3.one);
        int segmentCount = Mathf.Max(1, segments);

        int[] frontLeft = new int[segmentCount + 1];
        int[] frontRight = new int[segmentCount + 1];
        int[] backLeft = new int[segmentCount + 1];
        int[] backRight = new int[segmentCount + 1];

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            float z = t * length;
            float y = Mathf.Pow(Mathf.Sin(t * Mathf.PI * 0.5f), 2f) * bend;
            float widthMask = Mathf.Max(0.0f, Mathf.Sin(t * Mathf.PI));
            float shape = Mathf.Pow(widthMask, widthPower);
            float halfWidth = shape * width * 0.5f;

            Vector3 left = new Vector3(-halfWidth, y, z);
            Vector3 right = new Vector3(halfWidth, y, z);
            Vector2 uvLeft = new Vector2(0f, t);
            Vector2 uvRight = new Vector2(1f, t);

            frontLeft[i] = meshBuilder.AddVertex(matrix.MultiplyPoint3x4(left), uvLeft);
            frontRight[i] = meshBuilder.AddVertex(matrix.MultiplyPoint3x4(right), uvRight);
            backLeft[i] = meshBuilder.AddVertex(matrix.MultiplyPoint3x4(left), uvLeft);
            backRight[i] = meshBuilder.AddVertex(matrix.MultiplyPoint3x4(right), uvRight);
        }

        for (int i = 0; i < segmentCount; i++)
        {
            meshBuilder.AddTriangle(subMesh, frontLeft[i], frontLeft[i + 1], frontRight[i + 1]);
            meshBuilder.AddTriangle(subMesh, frontLeft[i], frontRight[i + 1], frontRight[i]);
            meshBuilder.AddTriangle(subMesh, backRight[i], backRight[i + 1], backLeft[i + 1]);
            meshBuilder.AddTriangle(subMesh, backRight[i], backLeft[i + 1], backLeft[i]);
        }
    }

    private static void AddCylinder(
        MeshBuilder meshBuilder,
        int subMesh,
        Vector3 baseCenter,
        float height,
        float radius,
        int sides)
    {
        int sideCount = Mathf.Max(3, sides);
        int[] sideBottom = new int[sideCount + 1];
        int[] sideTop = new int[sideCount + 1];

        for (int i = 0; i <= sideCount; i++)
        {
            float t = i / (float)sideCount;
            float angle = t * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;

            Vector3 bottom = baseCenter + new Vector3(x, 0f, z);
            Vector3 top = baseCenter + new Vector3(x, height, z);

            sideBottom[i] = meshBuilder.AddVertex(bottom, new Vector2(t, 0f));
            sideTop[i] = meshBuilder.AddVertex(top, new Vector2(t, 1f));
        }

        for (int i = 0; i < sideCount; i++)
        {
            meshBuilder.AddTriangle(subMesh, sideBottom[i], sideTop[i], sideTop[i + 1]);
            meshBuilder.AddTriangle(subMesh, sideBottom[i], sideTop[i + 1], sideBottom[i + 1]);
        }

        int topCenter = meshBuilder.AddVertex(baseCenter + new Vector3(0f, height, 0f), new Vector2(0.5f, 0.5f));
        int bottomCenter = meshBuilder.AddVertex(baseCenter, new Vector2(0.5f, 0.5f));
        int[] topRing = new int[sideCount + 1];
        int[] bottomRing = new int[sideCount + 1];

        for (int i = 0; i <= sideCount; i++)
        {
            float t = i / (float)sideCount;
            float angle = t * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;

            Vector2 capUv = new Vector2((x / radius + 1f) * 0.5f, (z / radius + 1f) * 0.5f);
            topRing[i] = meshBuilder.AddVertex(baseCenter + new Vector3(x, height, z), capUv);
            bottomRing[i] = meshBuilder.AddVertex(baseCenter + new Vector3(x, 0f, z), capUv);
        }

        for (int i = 0; i < sideCount; i++)
        {
            meshBuilder.AddTriangle(subMesh, topCenter, topRing[i], topRing[i + 1]);
            meshBuilder.AddTriangle(subMesh, bottomCenter, bottomRing[i + 1], bottomRing[i]);
        }
    }

    private static void AddUVSphere(
        MeshBuilder meshBuilder,
        int subMesh,
        Vector3 center,
        float radius,
        int longitudes,
        int latitudes,
        float yScale)
    {
        int longitudeCount = Mathf.Max(3, longitudes);
        int latitudeCount = Mathf.Max(2, latitudes);
        int[,] ids = new int[latitudeCount + 1, longitudeCount + 1];

        for (int lat = 0; lat <= latitudeCount; lat++)
        {
            float v = lat / (float)latitudeCount;
            float phi = Mathf.PI * v;
            float y = Mathf.Cos(phi);
            float ringRadius = Mathf.Sin(phi);

            for (int lon = 0; lon <= longitudeCount; lon++)
            {
                float u = lon / (float)longitudeCount;
                float theta = u * Mathf.PI * 2f;
                float x = Mathf.Cos(theta) * ringRadius;
                float z = Mathf.Sin(theta) * ringRadius;

                Vector3 position = center + new Vector3(x, y * yScale, z) * radius;
                ids[lat, lon] = meshBuilder.AddVertex(position, new Vector2(u, v));
            }
        }

        for (int lat = 0; lat < latitudeCount; lat++)
        {
            for (int lon = 0; lon < longitudeCount; lon++)
            {
                int a = ids[lat, lon];
                int b = ids[lat + 1, lon];
                int c = ids[lat + 1, lon + 1];
                int d = ids[lat, lon + 1];

                meshBuilder.AddTriangle(subMesh, a, b, c);
                meshBuilder.AddTriangle(subMesh, a, c, d);
            }
        }
    }

    private static float Range(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    private static void DestroyOwnedObject(UnityEngine.Object ownedObject)
    {
        if (ownedObject == null)
            return;

        if (Application.isPlaying)
            Destroy(ownedObject);
        else
            DestroyImmediate(ownedObject);
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int>[] _triangles;

        public MeshBuilder(int subMeshCount)
        {
            _triangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                _triangles[i] = new List<int>();
        }

        public int AddVertex(Vector3 position, Vector2 uv)
        {
            int index = _vertices.Count;
            _vertices.Add(position);
            _uvs.Add(uv);
            return index;
        }

        public void AddTriangle(int subMesh, int a, int b, int c)
        {
            _triangles[subMesh].Add(a);
            _triangles[subMesh].Add(b);
            _triangles[subMesh].Add(c);
        }

        public Mesh ToMesh(string meshName)
        {
            Mesh mesh = new Mesh
            {
                name = meshName
            };

            if (_vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _triangles.Length;

            for (int i = 0; i < _triangles.Length; i++)
                mesh.SetTriangles(_triangles[i], i, true);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
