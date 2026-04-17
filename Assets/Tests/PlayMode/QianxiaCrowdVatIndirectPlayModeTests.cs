using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class QianxiaCrowdVatIndirectPlayModeTests
{
    private const string VatPrefabPath = "Assets/Project/Characters/Qianxia/Generated/VAT/QianxiaCrowdLod2Vat.prefab";
    private const string IndirectComputePath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirect.compute";
    private const string IndirectShaderPath = "Assets/Project/Crowds/VAT/Shader/CrowdVatIndirectLit.shader";
    private const string ArtifactDirectoryName = "qianxia-crowd-vat-benchmark";
    private const int WarmupFrames = 90;
    private const int BenchmarkFrames = 300;
    private const int TargetWidth = 1920;
    private const int TargetHeight = 1080;

    private static readonly int[] ScenarioInstanceCounts = { 1000, 5000, 10000 };

    [Serializable]
    private class BenchmarkSuiteResult
    {
        public string timestamp;
        public string unityVersion;
        public string operatingSystem;
        public string processorType;
        public string graphicsDeviceName;
        public int targetWidth;
        public int targetHeight;
        public int warmupFrames;
        public int benchmarkFrames;
        public BenchmarkScenarioResult[] scenarios;
    }

    [Serializable]
    private class BenchmarkScenarioResult
    {
        public int instanceCount;
        public float areaWidth;
        public float areaHeight;
        public float averageFrameMs;
        public float p95FrameMs;
        public float minFrameMs;
        public float maxFrameMs;
        public string profilerPath;
        public string screenshotPath;
        public CounterSummary[] counters;
    }

    [Serializable]
    private class CounterSummary
    {
        public string name;
        public string unit;
        public double average;
        public double p95;
        public double min;
        public double max;
    }

    private sealed class CounterRecorder : IDisposable
    {
        public CounterRecorder(string name, ProfilerCategory category, string statName, string unit, double scale)
        {
            Name = name;
            Unit = unit;
            Scale = scale;

            try
            {
                Recorder = ProfilerRecorder.StartNew(category, statName, 1);
            }
            catch
            {
                Recorder = default;
            }
        }

        public string Name { get; }
        public string Unit { get; }
        public double Scale { get; }
        public ProfilerRecorder Recorder { get; private set; }
        public List<double> Samples { get; } = new List<double>(BenchmarkFrames);
        public bool IsValid => Recorder.Valid;

        public void Sample()
        {
            if (!Recorder.Valid)
                return;

            Samples.Add(Recorder.LastValue * Scale);
        }

        public CounterSummary BuildSummary()
        {
            if (Samples.Count == 0)
                return null;

            List<double> sorted = Samples.OrderBy(value => value).ToList();
            int p95Index = Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.95f) - 1, 0, sorted.Count - 1);

            return new CounterSummary
            {
                name = Name,
                unit = Unit,
                average = Samples.Average(),
                p95 = sorted[p95Index],
                min = sorted[0],
                max = sorted[sorted.Count - 1]
            };
        }

        public void Dispose()
        {
            if (Recorder.Valid)
                Recorder.Dispose();

            Recorder = default;
        }
    }

    [UnityTest]
    public IEnumerator QianxiaCrowdVatIndirect_PlayModeProfilerBenchmark()
    {
#if !UNITY_EDITOR
        Assert.Ignore("该基准测试依赖 AssetDatabase，仅在 Unity Editor PlayMode 下运行。");
        yield break;
#else
        Directory.CreateDirectory(ArtifactDirectory);

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        GameObject vatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VatPrefabPath);
        ComputeShader indirectCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(IndirectComputePath);
        Shader indirectShader = AssetDatabase.LoadAssetAtPath<Shader>(IndirectShaderPath);

        Assert.IsNotNull(vatPrefab, $"未找到 VAT prefab: {VatPrefabPath}");
        Assert.IsNotNull(indirectCompute, $"未找到 ComputeShader: {IndirectComputePath}");
        Assert.IsNotNull(indirectShader, $"未找到 Shader: {IndirectShaderPath}");

        List<BenchmarkScenarioResult> scenarioResults = new List<BenchmarkScenarioResult>(ScenarioInstanceCounts.Length);

        for (int scenarioIndex = 0; scenarioIndex < ScenarioInstanceCounts.Length; scenarioIndex++)
        {
            int instanceCount = ScenarioInstanceCounts[scenarioIndex];
            yield return RunScenario(vatPrefab, indirectCompute, indirectShader, instanceCount, scenarioResults);
        }

        BenchmarkSuiteResult suiteResult = new BenchmarkSuiteResult
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            unityVersion = Application.unityVersion,
            operatingSystem = SystemInfo.operatingSystem,
            processorType = SystemInfo.processorType,
            graphicsDeviceName = SystemInfo.graphicsDeviceName,
            targetWidth = TargetWidth,
            targetHeight = TargetHeight,
            warmupFrames = WarmupFrames,
            benchmarkFrames = BenchmarkFrames,
            scenarios = scenarioResults.ToArray()
        };

        string jsonPath = Path.Combine(ArtifactDirectory, "qianxia-crowd-vat-playmode-benchmark.json");
        string reportPath = Path.Combine(ArtifactDirectory, "qianxia-crowd-vat-playmode-benchmark.md");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(suiteResult, true), Encoding.UTF8);
        File.WriteAllText(reportPath, BuildMarkdownReport(suiteResult), Encoding.UTF8);

        foreach (BenchmarkScenarioResult scenario in scenarioResults)
        {
            Debug.Log(
                $"Qianxia crowd VAT benchmark {scenario.instanceCount}: avg {scenario.averageFrameMs:F2} ms, p95 {scenario.p95FrameMs:F2} ms, max {scenario.maxFrameMs:F2} ms");
        }
#endif
    }

#if UNITY_EDITOR
    private static IEnumerator RunScenario(
        GameObject vatPrefab,
        ComputeShader indirectCompute,
        Shader indirectShader,
        int instanceCount,
        List<BenchmarkScenarioResult> scenarioResults)
    {
        string sceneName = $"QianxiaCrowdBenchmark_{instanceCount}";
        Scene scene = SceneManager.CreateScene(sceneName);
        SceneManager.SetActiveScene(scene);

        RenderTexture targetTexture = null;
        GameObject scenarioRoot = null;
        Camera benchmarkCamera = null;
        List<CounterRecorder> counters = null;

        float spacing = 1.4f;
        float sideLength = Mathf.Ceil(Mathf.Sqrt(instanceCount)) * spacing;
        Vector2 areaSize = new Vector2(sideLength, sideLength);

        CreateLighting();
        benchmarkCamera = CreateCamera(sideLength, out targetTexture);
        scenarioRoot = CreateRendererRoot(vatPrefab, indirectCompute, indirectShader, instanceCount, areaSize);

        yield return WaitFrames(10);

        List<float> frameTimesMs = new List<float>(BenchmarkFrames);
        counters = CreateCounterRecorders();

        string profilerPath = Path.Combine(ArtifactDirectory, $"qianxia-crowd-indirect-{instanceCount}.data.raw");
        string screenshotPath = Path.Combine(ArtifactDirectory, $"qianxia-crowd-indirect-{instanceCount}.png");

        if (File.Exists(profilerPath))
            File.Delete(profilerPath);

        if (File.Exists(screenshotPath))
            File.Delete(screenshotPath);

        Profiler.logFile = profilerPath;
        Profiler.enableBinaryLog = true;
        Profiler.enabled = true;

        int totalFrames = WarmupFrames + BenchmarkFrames;
        for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
        {
            UpdateCameraPose(benchmarkCamera.transform, sideLength, frameIndex, totalFrames);

            if (frameIndex >= WarmupFrames)
            {
                frameTimesMs.Add(Time.unscaledDeltaTime * 1000.0f);
                for (int recorderIndex = 0; recorderIndex < counters.Count; recorderIndex++)
                    counters[recorderIndex].Sample();
            }

            yield return null;
        }

        Profiler.enabled = false;
        Profiler.enableBinaryLog = false;
        Profiler.logFile = string.Empty;

        CaptureRenderTexture(targetTexture, screenshotPath);

        Assert.IsNotEmpty(frameTimesMs, $"实例数 {instanceCount} 未采集到任何帧时间。");

        List<float> sortedFrameTimes = frameTimesMs.OrderBy(value => value).ToList();
        int p95Index = Mathf.Clamp(Mathf.CeilToInt(sortedFrameTimes.Count * 0.95f) - 1, 0, sortedFrameTimes.Count - 1);

        List<CounterSummary> counterSummaries = new List<CounterSummary>();
        for (int recorderIndex = 0; recorderIndex < counters.Count; recorderIndex++)
        {
            CounterSummary summary = counters[recorderIndex].BuildSummary();
            if (summary != null)
                counterSummaries.Add(summary);
        }

        scenarioResults.Add(new BenchmarkScenarioResult
        {
            instanceCount = instanceCount,
            areaWidth = areaSize.x,
            areaHeight = areaSize.y,
            averageFrameMs = frameTimesMs.Average(),
            p95FrameMs = sortedFrameTimes[p95Index],
            minFrameMs = sortedFrameTimes[0],
            maxFrameMs = sortedFrameTimes[sortedFrameTimes.Count - 1],
            profilerPath = profilerPath,
            screenshotPath = screenshotPath,
            counters = counterSummaries.ToArray()
        });

        Profiler.enabled = false;
        Profiler.enableBinaryLog = false;
        Profiler.logFile = string.Empty;

        if (counters != null)
        {
            for (int recorderIndex = 0; recorderIndex < counters.Count; recorderIndex++)
                counters[recorderIndex].Dispose();
        }

        if (scenarioRoot != null)
            UnityEngine.Object.Destroy(scenarioRoot);

        if (benchmarkCamera != null)
            UnityEngine.Object.Destroy(benchmarkCamera.gameObject);

        Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int lightIndex = 0; lightIndex < lights.Length; lightIndex++)
        {
            if (lights[lightIndex] != null && lights[lightIndex].gameObject.scene == scene)
                UnityEngine.Object.Destroy(lights[lightIndex].gameObject);
        }

        if (targetTexture != null)
        {
            targetTexture.Release();
            UnityEngine.Object.Destroy(targetTexture);
        }

        yield return null;
        yield return null;

        if (scene.IsValid() && scene.isLoaded)
        {
            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(scene);
            if (unloadOperation != null)
            {
                while (!unloadOperation.isDone)
                    yield return null;
            }
        }

        yield return Resources.UnloadUnusedAssets();
        GC.Collect();
        yield return null;
    }

    private static GameObject CreateRendererRoot(
        GameObject vatPrefab,
        ComputeShader indirectCompute,
        Shader indirectShader,
        int instanceCount,
        Vector2 areaSize)
    {
        Component templatePlayer = vatPrefab.GetComponentInChildren(ResolveType("CrowdVatPlayer"), true);
        Assert.IsNotNull(templatePlayer, $"VAT prefab 缺少 CrowdVatPlayer: {VatPrefabPath}");

        Type rendererType = ResolveType("CrowdVatIndirectRenderer");
        Assert.IsNotNull(rendererType, "未能解析 CrowdVatIndirectRenderer 类型。");

        GameObject root = new GameObject($"QianxiaCrowdIndirect_{instanceCount}");
        root.SetActive(false);

        Component renderer = root.AddComponent(rendererType);
        SetField(renderer, "_templatePrefab", templatePlayer);
        SetField(renderer, "_updateCompute", indirectCompute);
        SetField(renderer, "_indirectShader", indirectShader);
        SetField(renderer, "_clipName", string.Empty);
        SetField(renderer, "_instanceCount", instanceCount);
        SetField(renderer, "_areaSize", areaSize);
        SetField(renderer, "_cellJitter", 0.65f);
        SetField(renderer, "_heightOffset", 0.0f);
        SetField(renderer, "_randomSeed", 20260416);
        SetField(renderer, "_factionLayout", 1);
        SetField(renderer, "_baseScale", 1.0f);
        SetField(renderer, "_scaleMultiplierRange", new Vector2(0.95f, 1.05f));
        SetField(renderer, "_playOnEnable", true);
        SetField(renderer, "_loopOverride", true);
        SetField(renderer, "_playbackSpeed", 1.0f);
        SetField(renderer, "_playbackSpeedMultiplierRange", new Vector2(0.9f, 1.1f));
        SetField(renderer, "_normalizedStartOffsetRandom", 1.0f);
        SetField(renderer, "_boundsPadding", 4.0f);
        SetField(renderer, "_shadowCastingMode", UnityEngine.Rendering.ShadowCastingMode.Off);
        SetField(renderer, "_receiveShadows", false);

        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;
        root.SetActive(true);

        MethodInfo rebuildMethod = rendererType.GetMethod("RebuildCrowdLayout", BindingFlags.Public | BindingFlags.Instance);
        rebuildMethod?.Invoke(renderer, null);

        return root;
    }

    private static Camera CreateCamera(float sideLength, out RenderTexture targetTexture)
    {
        GameObject cameraObject = new GameObject("BenchmarkCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.28f, 0.31f, 0.36f);
        camera.fieldOfView = 36.0f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = Mathf.Max(256.0f, sideLength * 4.0f);
        camera.allowHDR = false;
        camera.allowMSAA = false;

        targetTexture = new RenderTexture(TargetWidth, TargetHeight, 24, RenderTextureFormat.ARGB32)
        {
            name = "QianxiaCrowdBenchmarkRT"
        };
        targetTexture.Create();
        camera.targetTexture = targetTexture;

        UpdateCameraPose(camera.transform, sideLength, 0, 1);
        return camera;
    }

    private static void CreateLighting()
    {
        GameObject lightObject = new GameObject("BenchmarkDirectionalLight");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.color = Color.white;
        light.shadows = LightShadows.None;
        lightObject.transform.rotation = Quaternion.Euler(42.0f, -32.0f, 0.0f);
    }

    private static void UpdateCameraPose(Transform cameraTransform, float sideLength, int frameIndex, int totalFrames)
    {
        float phase = totalFrames <= 1 ? 0.0f : frameIndex / (float)(totalFrames - 1);
        float yaw = Mathf.Lerp(-8.0f, 18.0f, Mathf.SmoothStep(0.0f, 1.0f, phase));
        Quaternion orbitRotation = Quaternion.Euler(18.0f, yaw, 0.0f);
        Vector3 baseOffset = new Vector3(0.0f, sideLength * 0.45f, -sideLength * 0.85f);
        Vector3 cameraPosition = orbitRotation * baseOffset + new Vector3(0.0f, Mathf.Max(6.0f, sideLength * 0.08f), 0.0f);
        Vector3 lookTarget = new Vector3(0.0f, Mathf.Max(1.0f, sideLength * 0.08f), 0.0f);

        cameraTransform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation((lookTarget - cameraPosition).normalized, Vector3.up));
    }

    private static List<CounterRecorder> CreateCounterRecorders()
    {
        return new List<CounterRecorder>
        {
            new CounterRecorder("Batches", ProfilerCategory.Render, "Batches Count", "count", 1.0),
            new CounterRecorder("SetPass", ProfilerCategory.Render, "SetPass Calls Count", "count", 1.0),
            new CounterRecorder("DrawCalls", ProfilerCategory.Render, "Draw Calls Count", "count", 1.0),
            new CounterRecorder("MainThread", ProfilerCategory.Internal, "Main Thread", "ms", 1e-6),
            new CounterRecorder("RenderThread", ProfilerCategory.Internal, "Render Thread", "ms", 1e-6)
        };
    }

    private static void CaptureRenderTexture(RenderTexture targetTexture, string screenshotPath)
    {
        if (targetTexture == null)
            return;

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = targetTexture;

        Texture2D texture = new Texture2D(targetTexture.width, targetTexture.height, TextureFormat.RGBA32, false, false);
        texture.ReadPixels(new Rect(0, 0, targetTexture.width, targetTexture.height), 0, 0);
        texture.Apply(false, false);

        byte[] pngBytes = texture.EncodeToPNG();
        File.WriteAllBytes(screenshotPath, pngBytes);

        UnityEngine.Object.Destroy(texture);
        RenderTexture.active = previous;
    }

    private static string BuildMarkdownReport(BenchmarkSuiteResult suiteResult)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# 千夏 Crowd VAT PlayMode Benchmark");
        builder.AppendLine();
        builder.AppendLine($"- 时间：{suiteResult.timestamp}");
        builder.AppendLine($"- Unity：{suiteResult.unityVersion}");
        builder.AppendLine($"- 操作系统：{suiteResult.operatingSystem}");
        builder.AppendLine($"- CPU：{suiteResult.processorType}");
        builder.AppendLine($"- GPU：{suiteResult.graphicsDeviceName}");
        builder.AppendLine($"- 分辨率：{suiteResult.targetWidth} x {suiteResult.targetHeight}");
        builder.AppendLine($"- 预热帧：{suiteResult.warmupFrames}");
        builder.AppendLine($"- 采样帧：{suiteResult.benchmarkFrames}");
        builder.AppendLine();

        for (int scenarioIndex = 0; scenarioIndex < suiteResult.scenarios.Length; scenarioIndex++)
        {
            BenchmarkScenarioResult scenario = suiteResult.scenarios[scenarioIndex];
            builder.AppendLine($"## {scenario.instanceCount} 实例");
            builder.AppendLine();
            builder.AppendLine($"- 布局范围：{scenario.areaWidth:F2} x {scenario.areaHeight:F2}");
            builder.AppendLine($"- 平均帧时：{scenario.averageFrameMs:F2} ms");
            builder.AppendLine($"- P95 帧时：{scenario.p95FrameMs:F2} ms");
            builder.AppendLine($"- 最快帧时：{scenario.minFrameMs:F2} ms");
            builder.AppendLine($"- 最慢帧时：{scenario.maxFrameMs:F2} ms");
            builder.AppendLine($"- Profiler：{scenario.profilerPath}");
            builder.AppendLine($"- 截图：{scenario.screenshotPath}");
            builder.AppendLine();

            if (scenario.counters != null && scenario.counters.Length > 0)
            {
                builder.AppendLine("| 计数器 | 平均 | P95 | 最小 | 最大 | 单位 |");
                builder.AppendLine("| --- | ---: | ---: | ---: | ---: | --- |");

                for (int counterIndex = 0; counterIndex < scenario.counters.Length; counterIndex++)
                {
                    CounterSummary counter = scenario.counters[counterIndex];
                    builder.AppendLine(
                        $"| {counter.name} | {counter.average:F2} | {counter.p95:F2} | {counter.min:F2} | {counter.max:F2} | {counter.unit} |");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("说明：该结果来自 Unity Editor PlayMode 自动化短跑，主要用于当前 RenderMeshIndirect + VAT 实现的回归对比，不等同于最终 Player 构建版。");
        return builder.ToString();
    }

    private static Type ResolveType(string typeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
        {
            Type directType = assemblies[assemblyIndex].GetType(typeName, false);
            if (directType != null)
                return directType;

            Type[] assemblyTypes;
            try
            {
                assemblyTypes = assemblies[assemblyIndex].GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                assemblyTypes = exception.Types;
            }

            if (assemblyTypes == null)
                continue;

            for (int typeIndex = 0; typeIndex < assemblyTypes.Length; typeIndex++)
            {
                Type assemblyType = assemblyTypes[typeIndex];
                if (assemblyType != null && string.Equals(assemblyType.Name, typeName, StringComparison.Ordinal))
                    return assemblyType;
            }
        }

        return null;
    }

    private static void SetField(Component component, string fieldName, object value)
    {
        FieldInfo field = component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"未找到字段 {fieldName}");
        object boxedValue = field.FieldType.IsEnum && value is int rawValue
            ? Enum.ToObject(field.FieldType, rawValue)
            : value;
        field.SetValue(component, boxedValue);
    }

    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int index = 0; index < frameCount; index++)
            yield return null;
    }

    private static string ArtifactDirectory => Path.Combine(ProjectRoot, ".workspace", "artifacts", ArtifactDirectoryName);
    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#endif
}
