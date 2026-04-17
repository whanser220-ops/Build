using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CrowdVatIndirectSpatialQueryTests
{
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string IndirectComputePath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string IndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";

    private struct SpatialQueryRequestSpec
    {
        public int QueryId;
        public int TargetMask;
        public int FactionMask;
        public int Shape;
        public int Flags;
        public Vector3 WorldStart;
        public Vector3 WorldEnd;
        public float Radius;
        public uint MaxHits;
    }

    private struct SpatialQueryResultInfo
    {
        public int QueryId;
        public int TotalHits;
        public int WrittenHits;
        public bool Overflow;
    }

    private struct SpatialQueryHitInfo
    {
        public int QueryId;
        public int OwnerIndex;
        public int TargetMask;
        public int Flags;
        public int Faction;
        public float NormalizedDistance;
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_OverlapSphereQueryFindsNearbyAgents()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        CrowdTestContext context = CreateContext();
        yield return null;
        yield return null;

        SetQueries(context.Renderer, context.RendererType, new[]
        {
            new SpatialQueryRequestSpec
            {
                QueryId = 101,
                TargetMask = 1,
                FactionMask = 0,
                Shape = 0,
                Flags = 0,
                WorldStart = new Vector3(0.0f, 1.0f, 0.0f),
                WorldEnd = new Vector3(0.0f, 1.0f, 0.0f),
                Radius = 1.1f,
                MaxHits = 8u
            }
        });

        yield return WaitFrames(2);

        ReadQueryResults(
            context.Renderer,
            context.RendererType,
            out List<SpatialQueryResultInfo> results,
            out List<SpatialQueryHitInfo> hits);

        SpatialQueryResultInfo queryResult = results.Single(result => result.QueryId == 101);
        Assert.AreEqual(4, queryResult.TotalHits, "中心区域的范围查询应命中四个 crowd 代理。");
        Assert.AreEqual(4, queryResult.WrittenHits, "范围查询写回的命中数量不应被截断。");
        Assert.IsFalse(queryResult.Overflow, "本例命中数量很小，不应发生查询溢出。");
        Assert.AreEqual(4, hits.Count(hit => hit.QueryId == 101), "返回的命中列表应包含四个代理。");

        Cleanup(context);
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_SweepCapsuleQueryReturnsSortedHits()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        CrowdTestContext context = CreateContext();
        yield return WaitFrames(2);

        SetQueries(context.Renderer, context.RendererType, new[]
        {
            new SpatialQueryRequestSpec
            {
                QueryId = 202,
                TargetMask = 1,
                FactionMask = 0,
                Shape = 1,
                Flags = 0,
                WorldStart = new Vector3(-1.25f, 0.0f, -1.0f),
                WorldEnd = new Vector3(1.25f, 0.0f, -1.0f),
                Radius = 0.2f,
                MaxHits = 8u
            }
        });

        yield return WaitFrames(2);

        ReadQueryResults(
            context.Renderer,
            context.RendererType,
            out List<SpatialQueryResultInfo> results,
            out List<SpatialQueryHitInfo> hits);

        SpatialQueryResultInfo queryResult = results.Single(result => result.QueryId == 202);
        List<SpatialQueryHitInfo> queryHits = hits.Where(hit => hit.QueryId == 202).ToList();

        Assert.AreEqual(2, queryResult.TotalHits, "扫掠胶囊应只命中前排的两个 crowd 代理。");
        Assert.AreEqual(2, queryHits.Count, "扫掠胶囊应返回两个命中。");
        Assert.That(queryHits[0].NormalizedDistance, Is.LessThanOrEqualTo(queryHits[1].NormalizedDistance), "命中结果应按路径归一化距离升序返回。");

        Cleanup(context);
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_ActiveOnlyQueryFiltersInactiveAgents()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        GameObject activeTarget = new GameObject("CrowdSpatialQueryActiveTarget");
        activeTarget.transform.position = new Vector3(0.0f, 0.0f, -1.0f);

        CrowdTestContext context = CreateContext(
            enableActiveBubble: true,
            activeBubbleTarget: activeTarget.transform,
            activeBubbleRadius: 1.1f,
            activeBubbleRetentionRadius: 1.2f);

        yield return WaitFrames(4);

        SetQueries(context.Renderer, context.RendererType, new[]
        {
            new SpatialQueryRequestSpec
            {
                QueryId = 301,
                TargetMask = 1,
                FactionMask = 0,
                Shape = 0,
                Flags = 0,
                WorldStart = new Vector3(0.0f, 1.0f, 0.0f),
                WorldEnd = new Vector3(0.0f, 1.0f, 0.0f),
                Radius = 1.1f,
                MaxHits = 8u
            },
            new SpatialQueryRequestSpec
            {
                QueryId = 302,
                TargetMask = 1,
                FactionMask = 0,
                Shape = 0,
                Flags = 1,
                WorldStart = new Vector3(0.0f, 1.0f, 0.0f),
                WorldEnd = new Vector3(0.0f, 1.0f, 0.0f),
                Radius = 1.1f,
                MaxHits = 8u
            }
        });

        yield return WaitFrames(2);

        ReadQueryResults(
            context.Renderer,
            context.RendererType,
            out List<SpatialQueryResultInfo> results,
            out _);

        SpatialQueryResultInfo allAgents = results.Single(result => result.QueryId == 301);
        SpatialQueryResultInfo activeAgents = results.Single(result => result.QueryId == 302);

        Assert.AreEqual(4, allAgents.TotalHits, "无过滤查询应覆盖全部 crowd 代理。");
        Assert.AreEqual(2, activeAgents.TotalHits, "ActiveOnly 查询应只保留 active bubble 内的前排代理。");

        Cleanup(context);
        Object.Destroy(activeTarget);
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_TwoFactionLayoutSplitsAgentsIntoOpposingCamps()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        CrowdTestContext context = CreateContext(
            factionLayout: 0,
            factionCenterGap: 0.8f,
            areaSize: new Vector2(2.0f, 4.0f));

        yield return WaitFrames(2);

        SetQueries(context.Renderer, context.RendererType, new[]
        {
            new SpatialQueryRequestSpec
            {
                QueryId = 401,
                TargetMask = 1,
                FactionMask = 1,
                Shape = 0,
                Flags = 0,
                WorldStart = new Vector3(0.0f, 0.0f, -1.2f),
                WorldEnd = new Vector3(0.0f, 0.0f, -1.2f),
                Radius = 1.2f,
                MaxHits = 8u
            },
            new SpatialQueryRequestSpec
            {
                QueryId = 402,
                TargetMask = 1,
                FactionMask = 2,
                Shape = 0,
                Flags = 0,
                WorldStart = new Vector3(0.0f, 0.0f, 1.2f),
                WorldEnd = new Vector3(0.0f, 0.0f, 1.2f),
                Radius = 1.2f,
                MaxHits = 8u
            }
        });

        yield return WaitFrames(2);

        ReadQueryResults(
            context.Renderer,
            context.RendererType,
            out List<SpatialQueryResultInfo> results,
            out List<SpatialQueryHitInfo> hits);

        SpatialQueryResultInfo campA = results.Single(result => result.QueryId == 401);
        SpatialQueryResultInfo campB = results.Single(result => result.QueryId == 402);

        Assert.AreEqual(2, campA.TotalHits, "A 阵营查询应只命中前方的两名 A 阵营单位。");
        Assert.AreEqual(2, campB.TotalHits, "B 阵营查询应只命中对侧的两名 B 阵营单位。");
        Assert.IsTrue(hits.Where(hit => hit.QueryId == 401).All(hit => hit.Faction == 1), "A 阵营命中结果必须全部带回 CampA。");
        Assert.IsTrue(hits.Where(hit => hit.QueryId == 402).All(hit => hit.Faction == 2), "B 阵营命中结果必须全部带回 CampB。");

        Cleanup(context);
#endif
    }

    [UnityTest]
    public IEnumerator CrowdVatIndirect_OverlapSphereQueryRespectsCapsuleHeight()
    {
#if !UNITY_EDITOR
        Assert.Ignore("璇ユ祴璇曚緷璧?AssetDatabase锛屼粎鍦?Unity Editor PlayMode 涓嬭繍琛屻€?);
        yield break;
#else
        CrowdTestContext context = CreateContext(
            areaSize: new Vector2(0.01f, 0.01f),
            instanceCount: 1);

        yield return WaitFrames(2);

        SetQueries(context.Renderer, context.RendererType, new[]
        {
            new SpatialQueryRequestSpec
            {
                QueryId = 501,
                TargetMask = 1,
                FactionMask = 0,
                Shape = 0,
                Flags = 0,
                WorldStart = new Vector3(0.0f, 1.0f, 0.0f),
                WorldEnd = new Vector3(0.0f, 1.0f, 0.0f),
                Radius = 0.1f,
                MaxHits = 4u
            },
            new SpatialQueryRequestSpec
            {
                QueryId = 502,
                TargetMask = 1,
                FactionMask = 0,
                Shape = 0,
                Flags = 0,
                WorldStart = new Vector3(0.0f, 2.1f, 0.0f),
                WorldEnd = new Vector3(0.0f, 2.1f, 0.0f),
                Radius = 0.1f,
                MaxHits = 4u
            }
        });

        yield return WaitFrames(2);

        ReadQueryResults(
            context.Renderer,
            context.RendererType,
            out List<SpatialQueryResultInfo> results,
            out _);

        SpatialQueryResultInfo chestHeight = results.Single(result => result.QueryId == 501);
        SpatialQueryResultInfo aboveHead = results.Single(result => result.QueryId == 502);

        Assert.AreEqual(1, chestHeight.TotalHits, "胸口高度的 OverlapSphere 应命中单个 capsule crowd 角色。");
        Assert.AreEqual(0, aboveHead.TotalHits, "头顶上方的 OverlapSphere 不应再命中 capsule crowd 角色。");

        Cleanup(context);
#endif
    }

#if UNITY_EDITOR
    private struct CrowdTestContext
    {
        public CrowdTestContext(Component renderer, System.Type rendererType)
        {
            Renderer = renderer;
            RendererType = rendererType;
        }

        public Component Renderer { get; }
        public System.Type RendererType { get; }
    }

    private static CrowdTestContext CreateContext(
        bool enableActiveBubble = false,
        Transform activeBubbleTarget = null,
        float activeBubbleRadius = 0.0f,
        float activeBubbleRetentionRadius = 0.0f,
        int factionLayout = 1,
        float factionCenterGap = 0.8f,
        Vector2? areaSize = null,
        int instanceCount = 4)
    {
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

        GameObject root = new GameObject("CrowdVatIndirectSpatialQuery");
        root.SetActive(false);

        Component renderer = root.AddComponent(rendererType);
        SetField(renderer, "_templatePrefab", templatePlayer);
        SetField(renderer, "_updateCompute", indirectCompute);
        SetField(renderer, "_indirectShader", indirectShader);
        SetField(renderer, "_clipName", string.Empty);
        SetField(renderer, "_instanceCount", instanceCount);
        SetField(renderer, "_distributeAcrossWholeTerrain", false);
        SetField(renderer, "_areaSize", areaSize ?? new Vector2(2.0f, 2.0f));
        SetField(renderer, "_cellJitter", 0.0f);
        SetField(renderer, "_heightOffset", 0.0f);
        SetField(renderer, "_randomSeed", 1);
        SetField(renderer, "_enableTerrainCollision", false);
        SetField(renderer, "_autoResolveTerrain", false);
        SetField(renderer, "_terrain", null);
        SetField(renderer, "_factionLayout", factionLayout);
        SetField(renderer, "_factionCenterGap", factionCenterGap);
        SetField(renderer, "_baseScale", 1.0f);
        SetField(renderer, "_scaleMultiplierRange", Vector2.one);
        SetField(renderer, "_playOnEnable", true);
        SetField(renderer, "_loopOverride", true);
        SetField(renderer, "_playbackSpeed", 1.0f);
        SetField(renderer, "_playbackSpeedMultiplierRange", Vector2.one);
        SetField(renderer, "_normalizedStartOffsetRandom", 0.0f);
        SetField(renderer, "_enableApproximateCollision", false);
        SetField(renderer, "_collisionRadius", 0.35f);
        SetField(renderer, "_collisionHeight", 1.65f);
        SetField(renderer, "_queryCellSize", 1.0f);
        SetField(renderer, "_maxCellOccupancy", 8);
        SetField(renderer, "_maxSpatialQueries", 8);
        SetField(renderer, "_maxHitsPerSpatialQuery", 8);
        SetField(renderer, "_solverIterations", 1);
        SetField(renderer, "_selfCollisionStrength", 1.0f);
        SetField(renderer, "_anchorStiffness", 0.0f);
        SetField(renderer, "_velocityDamping", 0.2f);
        SetField(renderer, "_maxPushPerStep", 0.2f);
        SetField(renderer, "_maxDisplacementFromSpawn", 0.25f);
        SetField(renderer, "_inactiveReturnStrength", 0.25f);
        SetField(renderer, "_enableActiveBubble", enableActiveBubble);
        SetField(renderer, "_autoResolveCharacterController", false);
        SetField(renderer, "_activeBubbleTarget", activeBubbleTarget);
        SetField(renderer, "_activeBubbleRadius", activeBubbleRadius);
        SetField(renderer, "_activeBubbleRetentionRadius", activeBubbleRetentionRadius);
        SetField(renderer, "_useCharacterAsInteractionSphere", false);
        SetField(renderer, "_shadowCastingMode", UnityEngine.Rendering.ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);

        root.SetActive(true);
        rendererType.GetMethod("RebuildCrowdLayout")?.Invoke(renderer, null);
        return new CrowdTestContext(renderer, rendererType);
    }

    private static void Cleanup(CrowdTestContext context)
    {
        if (context.Renderer != null)
            Object.Destroy(((Component)context.Renderer).gameObject);
    }

    private static void SetQueries(Component renderer, System.Type rendererType, SpatialQueryRequestSpec[] queries)
    {
        MethodInfo method = rendererType.GetMethod("SetSpatialQueries", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(method, "未找到 SetSpatialQueries 方法。");

        System.Type requestType = ResolveRuntimeType("CrowdVatSpatialQueryRequest");
        Assert.IsNotNull(requestType, "未找到 CrowdVatSpatialQueryRequest 类型。");

        System.Array runtimeArray = System.Array.CreateInstance(requestType, queries.Length);
        for (int index = 0; index < queries.Length; index++)
        {
            object request = System.Activator.CreateInstance(requestType);
            SpatialQueryRequestSpec spec = queries[index];

            SetRuntimeField(requestType, request, "queryId", spec.QueryId);
            SetRuntimeEnumField(requestType, request, "targetMask", spec.TargetMask);
            SetRuntimeEnumField(requestType, request, "factionMask", spec.FactionMask);
            SetRuntimeEnumField(requestType, request, "shape", spec.Shape);
            SetRuntimeEnumField(requestType, request, "flags", spec.Flags);
            SetRuntimeField(requestType, request, "worldStart", spec.WorldStart);
            SetRuntimeField(requestType, request, "worldEnd", spec.WorldEnd);
            SetRuntimeField(requestType, request, "radius", spec.Radius);
            SetRuntimeField(requestType, request, "maxHits", spec.MaxHits);
            runtimeArray.SetValue(request, index);
        }

        method.Invoke(renderer, new object[] { runtimeArray });
    }

    private static void ReadQueryResults(
        Component renderer,
        System.Type rendererType,
        out List<SpatialQueryResultInfo> results,
        out List<SpatialQueryHitInfo> hits)
    {
        results = new List<SpatialQueryResultInfo>();
        hits = new List<SpatialQueryHitInfo>();

        System.Type resultType = ResolveRuntimeType("CrowdVatSpatialQueryResult");
        System.Type hitType = ResolveRuntimeType("CrowdVatSpatialQueryHit");
        Assert.IsNotNull(resultType, "未找到 CrowdVatSpatialQueryResult 类型。");
        Assert.IsNotNull(hitType, "未找到 CrowdVatSpatialQueryHit 类型。");

        System.Type resultListType = typeof(List<>).MakeGenericType(resultType);
        System.Type hitListType = typeof(List<>).MakeGenericType(hitType);
        object runtimeResults = System.Activator.CreateInstance(resultListType);
        object runtimeHits = System.Activator.CreateInstance(hitListType);

        MethodInfo method = rendererType.GetMethod("ReadSpatialQueryResults", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(method, "未找到 ReadSpatialQueryResults 方法。");
        object returnValue = method.Invoke(renderer, new[] { runtimeResults, runtimeHits });
        Assert.IsNotNull(returnValue, "ReadSpatialQueryResults 返回值不应为空。");

        foreach (object result in (System.Collections.IEnumerable)runtimeResults)
        {
            results.Add(new SpatialQueryResultInfo
            {
                QueryId = System.Convert.ToInt32(GetRuntimeField(resultType, result, "queryId")),
                TotalHits = System.Convert.ToInt32(GetRuntimeField(resultType, result, "totalHits")),
                WrittenHits = System.Convert.ToInt32(GetRuntimeField(resultType, result, "writtenHits")),
                Overflow = (bool)GetRuntimeField(resultType, result, "overflow")
            });
        }

        foreach (object hit in (System.Collections.IEnumerable)runtimeHits)
        {
            hits.Add(new SpatialQueryHitInfo
            {
                QueryId = System.Convert.ToInt32(GetRuntimeField(hitType, hit, "queryId")),
                OwnerIndex = System.Convert.ToInt32(GetRuntimeField(hitType, hit, "ownerIndex")),
                TargetMask = System.Convert.ToInt32(GetRuntimeField(hitType, hit, "targetMask")),
                Flags = System.Convert.ToInt32(GetRuntimeField(hitType, hit, "flags")),
                Faction = System.Convert.ToInt32(GetRuntimeField(hitType, hit, "faction")),
                NormalizedDistance = System.Convert.ToSingle(GetRuntimeField(hitType, hit, "normalizedDistance"))
            });
        }
    }

    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int index = 0; index < frameCount; index++)
            yield return null;
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

    private static void SetRuntimeField(System.Type type, object target, string fieldName, object value)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到运行时字段 {fieldName}");
        field.SetValue(target, value);
    }

    private static void SetRuntimeEnumField(System.Type type, object target, string fieldName, int rawValue)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到运行时枚举字段 {fieldName}");
        field.SetValue(target, System.Enum.ToObject(field.FieldType, rawValue));
    }

    private static object GetRuntimeField(System.Type type, object target, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(field, $"未找到运行时字段 {fieldName}");
        return field.GetValue(target);
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
