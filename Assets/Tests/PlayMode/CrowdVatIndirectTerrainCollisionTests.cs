using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CrowdVatIndirectTerrainCollisionTests
{
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string IndirectComputePath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string IndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";
    private const float DefaultCollisionRadius = 0.35f;
    private const float DefaultCollisionHeight = 1.65f;
    private const int TerrainCapsuleSampleCount = 4;

    private struct InstanceSimulationInfo
    {
        public Vector3 localPosition;
        public float uniformScale;
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_TerrainCollisionKeepsCapsuleOutsideTerrainSurface()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        GameObject vatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VatPrefabPath);
        ComputeShader indirectCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(IndirectComputePath);
        Shader indirectShader = AssetDatabase.LoadAssetAtPath<Shader>(IndirectShaderPath);

        Assert.IsNotNull(vatPrefab, $"未找到 VAT prefab: {VatPrefabPath}");
        Assert.IsNotNull(indirectCompute, $"未找到 ComputeShader: {IndirectComputePath}");
        Assert.IsNotNull(indirectShader, $"未找到 Shader: {IndirectShaderPath}");

        System.Type playerType = ResolveRuntimeType("CrowdVatPlayer");
        System.Type rendererType = ResolveRuntimeType("CrowdVatIndirectRenderer");
        Assert.IsNotNull(playerType, "未找到 CrowdVatPlayer 类型。");
        Assert.IsNotNull(rendererType, "未找到 CrowdVatIndirectRenderer 类型。");

        Component templatePlayer = vatPrefab.GetComponentInChildren(playerType, true);
        Assert.IsNotNull(templatePlayer, "VAT prefab 缺少 CrowdVatPlayer。");

        TerrainData terrainData = BuildSlopedTerrainData();
        GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
        terrainObject.transform.position = new Vector3(-4.0f, 0.0f, -4.0f);
        Terrain terrain = terrainObject.GetComponent<Terrain>();

        GameObject root = new GameObject("CrowdVatIndirectTerrainCollision");
        root.SetActive(false);

        Component renderer = root.AddComponent(rendererType);
        SetField(renderer, "_templatePrefab", templatePlayer);
        SetField(renderer, "_updateCompute", indirectCompute);
        SetField(renderer, "_indirectShader", indirectShader);
        SetField(renderer, "_clipName", string.Empty);
        SetField(renderer, "_instanceCount", 4);
        SetField(renderer, "_distributeAcrossWholeTerrain", false);
        SetField(renderer, "_areaSize", new Vector2(2.0f, 2.0f));
        SetField(renderer, "_cellJitter", 0.0f);
        SetField(renderer, "_heightOffset", 0.0f);
        SetField(renderer, "_randomSeed", 7);
        SetField(renderer, "_factionLayout", 1);
        SetField(renderer, "_enableTerrainCollision", true);
        SetField(renderer, "_autoResolveTerrain", false);
        SetField(renderer, "_terrain", terrain);
        SetField(renderer, "_terrainHeightOffset", 0.0f);
        SetField(renderer, "_enableStaticSdfCollision", false);
        SetField(renderer, "_enableApproximateCollision", false);
        SetField(renderer, "_collisionRadius", DefaultCollisionRadius);
        SetField(renderer, "_collisionHeight", DefaultCollisionHeight);
        SetField(renderer, "_capsulePbdSampleCount", TerrainCapsuleSampleCount);
        SetField(renderer, "_enableActiveBubble", false);
        SetField(renderer, "_shadowCastingMode", UnityEngine.Rendering.ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);

        root.SetActive(true);
        rendererType.GetMethod("RebuildCrowdLayout")?.Invoke(renderer, null);

        yield return null;
        yield return null;
        yield return null;

        InstanceSimulationInfo[] simulationStates = ReadSimulationStates(renderer, rendererType, 4);
        float worstTerrainPenetration = 0.0f;
        for (int i = 0; i < simulationStates.Length; i++)
        {
            InstanceSimulationInfo state = simulationStates[i];
            float uniformScale = Mathf.Max(state.uniformScale, 0.01f);
            float radius = Mathf.Max(0.01f, DefaultCollisionRadius * uniformScale);
            float capsuleHeight = Mathf.Max(radius * 2.0f, DefaultCollisionHeight * uniformScale);
            Vector3 rootWorldPosition = root.transform.TransformPoint(state.localPosition);
            Vector3 capsuleStart = rootWorldPosition + Vector3.up * radius;
            Vector3 capsuleEnd = rootWorldPosition + Vector3.up * (capsuleHeight - radius);

            for (int sampleIndex = 0; sampleIndex < TerrainCapsuleSampleCount; sampleIndex++)
            {
                float sampleT = sampleIndex / (float)(TerrainCapsuleSampleCount - 1);
                Vector3 sampleWorldCenter = Vector3.Lerp(capsuleStart, capsuleEnd, sampleT);
                float terrainHeight = terrain.SampleHeight(sampleWorldCenter) + terrain.transform.position.y;
                Vector3 terrainNormal = SampleTerrainNormal(terrain, sampleWorldCenter);
                Vector3 terrainSurfacePoint = new Vector3(sampleWorldCenter.x, terrainHeight, sampleWorldCenter.z);
                float signedDistance = Vector3.Dot(sampleWorldCenter - terrainSurfacePoint, terrainNormal);
                float penetration = Mathf.Max(0.0f, radius - signedDistance);
                worstTerrainPenetration = Mathf.Max(worstTerrainPenetration, penetration);

                Assert.GreaterOrEqual(
                    signedDistance + 0.03f,
                    radius,
                    $"实例 {i} 的 capsule 采样点 {sampleIndex} 仍然穿入地形，signedDistance={signedDistance:F3}, radius={radius:F3}");
            }
        }

        Object.Destroy(root);
        Object.Destroy(terrainObject);
        Object.Destroy(terrainData);

        Assert.Less(worstTerrainPenetration, 0.03f, $"crowd capsule 穿入地形过深，最深穿透 {worstTerrainPenetration:F3}");
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_StaticSdfCollisionPushesCapsuleOutOfVolume()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        GameObject vatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VatPrefabPath);
        ComputeShader indirectCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(IndirectComputePath);
        Shader indirectShader = AssetDatabase.LoadAssetAtPath<Shader>(IndirectShaderPath);

        Assert.IsNotNull(vatPrefab, $"未找到 VAT prefab: {VatPrefabPath}");
        Assert.IsNotNull(indirectCompute, $"未找到 ComputeShader: {IndirectComputePath}");
        Assert.IsNotNull(indirectShader, $"未找到 Shader: {IndirectShaderPath}");

        System.Type playerType = ResolveRuntimeType("CrowdVatPlayer");
        System.Type rendererType = ResolveRuntimeType("CrowdVatIndirectRenderer");
        Assert.IsNotNull(playerType, "未找到 CrowdVatPlayer 类型。");
        Assert.IsNotNull(rendererType, "未找到 CrowdVatIndirectRenderer 类型。");

        Component templatePlayer = vatPrefab.GetComponentInChildren(playerType, true);
        Assert.IsNotNull(templatePlayer, "VAT prefab 缺少 CrowdVatPlayer。");

        Vector3 sdfVolumeSize = new Vector3(2.0f, 4.0f, 2.0f);
        Vector3 sdfHalfExtents = new Vector3(0.5f, 1.8f, 0.5f);
        Texture3D staticSdfTexture = BuildBoxSdfTexture(24, sdfVolumeSize, sdfHalfExtents);

        GameObject root = new GameObject("CrowdVatIndirectStaticSdfCollision");
        root.transform.position = new Vector3(0.25f, 0.0f, 0.0f);
        root.SetActive(false);

        Component renderer = root.AddComponent(rendererType);
        SetField(renderer, "_templatePrefab", templatePlayer);
        SetField(renderer, "_updateCompute", indirectCompute);
        SetField(renderer, "_indirectShader", indirectShader);
        SetField(renderer, "_clipName", string.Empty);
        SetField(renderer, "_instanceCount", 1);
        SetField(renderer, "_distributeAcrossWholeTerrain", false);
        SetField(renderer, "_areaSize", new Vector2(0.01f, 0.01f));
        SetField(renderer, "_cellJitter", 0.0f);
        SetField(renderer, "_heightOffset", 0.0f);
        SetField(renderer, "_randomSeed", 11);
        SetField(renderer, "_factionLayout", 1);
        SetField(renderer, "_enableTerrainCollision", false);
        SetField(renderer, "_autoResolveTerrain", false);
        SetField(renderer, "_terrain", null);
        SetField(renderer, "_enableStaticSdfCollision", true);
        SetField(renderer, "_staticSdfTexture", staticSdfTexture);
        SetField(renderer, "_staticSdfWorldCenter", Vector3.zero);
        SetField(renderer, "_staticSdfWorldSize", sdfVolumeSize);
        SetField(renderer, "_staticSdfDistanceScale", 1.0f);
        SetField(renderer, "_staticSdfDistanceBias", 0.0f);
        SetField(renderer, "_enableApproximateCollision", false);
        SetField(renderer, "_collisionRadius", DefaultCollisionRadius);
        SetField(renderer, "_collisionHeight", DefaultCollisionHeight);
        SetField(renderer, "_solverIterations", 4);
        SetField(renderer, "_capsulePbdSampleCount", 4);
        SetField(renderer, "_maxPushPerStep", 0.2f);
        SetField(renderer, "_enableActiveBubble", false);
        SetField(renderer, "_shadowCastingMode", UnityEngine.Rendering.ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);

        root.SetActive(true);
        rendererType.GetMethod("RebuildCrowdLayout")?.Invoke(renderer, null);

        yield return null;
        yield return null;
        yield return null;
        yield return null;

        Vector3 rootWorldPosition = ReadInstancePositions(renderer, rendererType, 1)[0];
        Vector3 bottomSphereCenter = rootWorldPosition + Vector3.up * DefaultCollisionRadius;
        float bottomDistance = ComputeBoxSignedDistance(bottomSphereCenter, sdfHalfExtents);

        Object.Destroy(root);
        Object.Destroy(staticSdfTexture);

        Assert.Greater(rootWorldPosition.x, 0.6f, $"static SDF 未将 capsule 明显推出体积，当前位置 {rootWorldPosition}");
        Assert.GreaterOrEqual(bottomDistance, 0.30f, $"capsule 底部采样点仍然过于接近 static SDF，距离 {bottomDistance:F3}");
#endif
    }

#if UNITY_EDITOR
    private static TerrainData BuildSlopedTerrainData()
    {
        const int resolution = 33;
        TerrainData terrainData = new TerrainData
        {
            heightmapResolution = resolution,
            size = new Vector3(8.0f, 3.0f, 8.0f)
        };

        float[,] heights = new float[resolution, resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float nx = x / (float)(resolution - 1);
                float ny = y / (float)(resolution - 1);
                heights[y, x] = Mathf.Clamp01((nx * 0.75f) + (ny * 0.25f)) * 0.6f;
            }
        }

        terrainData.SetHeights(0, 0, heights);
        return terrainData;
    }

    private static Texture3D BuildBoxSdfTexture(int resolution, Vector3 volumeSize, Vector3 halfExtents)
    {
        Texture3D texture = new Texture3D(resolution, resolution, resolution, TextureFormat.RGBAFloat, false)
        {
            name = "CrowdVatIndirectStaticSdfTest",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] voxels = new Color[resolution * resolution * resolution];
        int writeIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            float wz = Mathf.Lerp(-volumeSize.z * 0.5f, volumeSize.z * 0.5f, z / (float)(resolution - 1));
            for (int y = 0; y < resolution; y++)
            {
                float wy = Mathf.Lerp(-volumeSize.y * 0.5f, volumeSize.y * 0.5f, y / (float)(resolution - 1));
                for (int x = 0; x < resolution; x++)
                {
                    float wx = Mathf.Lerp(-volumeSize.x * 0.5f, volumeSize.x * 0.5f, x / (float)(resolution - 1));
                    float distance = ComputeBoxSignedDistance(new Vector3(wx, wy, wz), halfExtents);
                    voxels[writeIndex++] = new Color(distance, 0.0f, 0.0f, 0.0f);
                }
            }
        }

        texture.SetPixels(voxels);
        texture.Apply(false, false);
        return texture;
    }

    private static float ComputeBoxSignedDistance(Vector3 point, Vector3 halfExtents)
    {
        Vector3 q = new Vector3(Mathf.Abs(point.x), Mathf.Abs(point.y), Mathf.Abs(point.z)) - halfExtents;
        Vector3 outside = new Vector3(Mathf.Max(q.x, 0.0f), Mathf.Max(q.y, 0.0f), Mathf.Max(q.z, 0.0f));
        float outsideDistance = outside.magnitude;
        float insideDistance = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0.0f);
        return outsideDistance + insideDistance;
    }

    private static Vector3 SampleTerrainNormal(Terrain terrain, Vector3 worldPosition)
    {
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainMin = terrain.transform.position;
        Vector3 terrainSize = terrainData.size;
        float u = Mathf.Clamp01((worldPosition.x - terrainMin.x) / Mathf.Max(terrainSize.x, 0.0001f));
        float v = Mathf.Clamp01((worldPosition.z - terrainMin.z) / Mathf.Max(terrainSize.z, 0.0001f));
        return terrain.transform.TransformDirection(terrainData.GetInterpolatedNormal(u, v)).normalized;
    }

    private static Vector3[] ReadInstancePositions(Component renderer, System.Type rendererType, int instanceCount)
    {
        FieldInfo bufferField = rendererType.GetField("_instanceTransformBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(bufferField, "未找到 _instanceTransformBuffer 字段。");

        ComputeBuffer buffer = bufferField.GetValue(renderer) as ComputeBuffer;
        Assert.IsNotNull(buffer, "_instanceTransformBuffer 为空。");

        System.Type matrixRowsType = rendererType.GetNestedType("MatrixRows", BindingFlags.NonPublic);
        Assert.IsNotNull(matrixRowsType, "未找到 MatrixRows 类型。");

        System.Array data = System.Array.CreateInstance(matrixRowsType, instanceCount);
        buffer.GetData(data);

        FieldInfo row0Field = matrixRowsType.GetField("row0", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo row1Field = matrixRowsType.GetField("row1", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo row2Field = matrixRowsType.GetField("row2", BindingFlags.Public | BindingFlags.Instance);

        Vector3[] positions = new Vector3[instanceCount];
        for (int instanceIndex = 0; instanceIndex < instanceCount; instanceIndex++)
        {
            object rowData = data.GetValue(instanceIndex);
            Vector4 row0 = (Vector4)row0Field.GetValue(rowData);
            Vector4 row1 = (Vector4)row1Field.GetValue(rowData);
            Vector4 row2 = (Vector4)row2Field.GetValue(rowData);
            positions[instanceIndex] = new Vector3(row0.w, row1.w, row2.w);
        }

        return positions;
    }

    private static InstanceSimulationInfo[] ReadSimulationStates(Component renderer, System.Type rendererType, int instanceCount)
    {
        FieldInfo bufferField = rendererType.GetField("_simulationReadBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(bufferField, "未找到 _simulationReadBuffer 字段。");

        ComputeBuffer buffer = bufferField.GetValue(renderer) as ComputeBuffer;
        Assert.IsNotNull(buffer, "_simulationReadBuffer 为空。");

        System.Type stateType = rendererType.GetNestedType("InstanceSimulationState", BindingFlags.NonPublic);
        Assert.IsNotNull(stateType, "未找到 InstanceSimulationState 类型。");

        System.Array data = System.Array.CreateInstance(stateType, instanceCount);
        buffer.GetData(data);

        FieldInfo localPositionAndYawField = stateType.GetField("localPositionAndYaw", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo scaleAndVelocityField = stateType.GetField("scaleAndVelocity", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(localPositionAndYawField, "未找到 localPositionAndYaw 字段。");
        Assert.IsNotNull(scaleAndVelocityField, "未找到 scaleAndVelocity 字段。");

        InstanceSimulationInfo[] states = new InstanceSimulationInfo[instanceCount];
        for (int instanceIndex = 0; instanceIndex < instanceCount; instanceIndex++)
        {
            object rowData = data.GetValue(instanceIndex);
            Vector4 localPositionAndYaw = (Vector4)localPositionAndYawField.GetValue(rowData);
            Vector4 scaleAndVelocity = (Vector4)scaleAndVelocityField.GetValue(rowData);
            states[instanceIndex] = new InstanceSimulationInfo
            {
                localPosition = new Vector3(localPositionAndYaw.x, localPositionAndYaw.y, localPositionAndYaw.z),
                uniformScale = scaleAndVelocity.x
            };
        }

        return states;
    }
#endif

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到字段 {fieldName}");
        object boxedValue = field.FieldType.IsEnum && value is int rawValue
            ? System.Enum.ToObject(field.FieldType, rawValue)
            : value;
        field.SetValue(target, boxedValue);
    }

    private static System.Type ResolveRuntimeType(string typeName)
    {
        foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type type = assembly.GetType(typeName, false);
            if (type != null)
                return type;
        }

        return null;
    }
}
