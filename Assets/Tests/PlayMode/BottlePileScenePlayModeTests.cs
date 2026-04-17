using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#if UNITY_EDITOR
[PrebuildSetup(typeof(BottlePileScenePlayModeSetup))]
#endif
public class BottlePileScenePlayModeTests
{
    private const string SceneName = "SampleScene";
    private const string PileObjectName = "CokeBottlePile";
    private const int WarmupFrames = 30;
    private const int BenchmarkFrames = 240;

    [Serializable]
    private class BenchmarkResult
    {
        public string sceneName;
        public string pileObjectName;
        public int warmupFrames;
        public int benchmarkFrames;
        public float averageFrameMs;
        public float p95FrameMs;
        public float minFrameMs;
        public float maxFrameMs;
        public string screenshotPath;
        public string reportPath;
        public string timestamp;
    }

    [UnityTest]
    public IEnumerator SampleScene_BottlePile_SmokeAndBenchmark()
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        Assert.IsNotNull(loadOperation, "SampleScene 加载失败。");
        while (!loadOperation.isDone)
            yield return null;

        yield return WaitFrames(10);

        GameObject pileObject = GameObject.Find(PileObjectName);
        Assert.IsNotNull(pileObject, $"场景里缺少 {PileObjectName}。");

        Component pileSystem = pileObject.GetComponent("BottlePileSystem");
        Assert.IsNotNull(pileSystem, $"{PileObjectName} 缺少 BottlePileSystem 组件。");

        Component pileVolume = pileObject.GetComponent("BottlePileVolume");
        Assert.IsNotNull(pileVolume, $"{PileObjectName} 缺少 BottlePileVolume 组件。");

        GameObject player = GameObject.FindWithTag("Player");
        Assert.IsNotNull(player, "场景里缺少 Player。");

        CharacterController characterController = player.GetComponent<CharacterController>();
        Assert.IsNotNull(characterController, "Player 缺少 CharacterController。");

        BoxCollider characterBoxCollider = player.GetComponentInChildren<BoxCollider>(true);
        Assert.IsNotNull(characterBoxCollider, "Player 缺少 BoxCollider 角色代理。");
        Assert.That(
            Mathf.Approximately(characterBoxCollider.size.x, characterBoxCollider.size.y) &&
            Mathf.Approximately(characterBoxCollider.size.y, characterBoxCollider.size.z),
            "角色代理应为立方体盒体。");

        Behaviour fpsController = player.GetComponent("FpsCharacterController") as Behaviour;
        if (fpsController != null)
            fpsController.enabled = false;

        TeleportPlayerForApproach(player.transform, characterController, pileObject.transform.position);
        yield return WaitFrames(10);

        string artifactDirectory = Path.Combine(ProjectRoot, ".workspace", "artifacts", "coke-bottle-pbd");
        Directory.CreateDirectory(artifactDirectory);

        string screenshotPath = Path.Combine(artifactDirectory, "sample-scene-benchmark.png");
        string jsonPath = Path.Combine(artifactDirectory, "sample-scene-benchmark.json");
        string reportPath = Path.Combine(artifactDirectory, "sample-scene-benchmark.md");

        if (File.Exists(screenshotPath))
            File.Delete(screenshotPath);

        List<float> frameTimesMs = new List<float>(BenchmarkFrames);
        Quaternion startRotation = player.transform.rotation;
        Quaternion sweepRotation = Quaternion.LookRotation(
            Vector3.Slerp(player.transform.forward, (player.transform.forward + player.transform.right * 0.45f).normalized, 0.5f),
            Vector3.up);
        Camera mainCamera = Camera.main;

        int totalFrames = WarmupFrames + BenchmarkFrames;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            float phase = Mathf.InverseLerp(0.0f, totalFrames - 1.0f, frame);
            player.transform.rotation = Quaternion.Slerp(startRotation, sweepRotation, Mathf.SmoothStep(0.0f, 1.0f, phase));

            Vector3 motion =
                player.transform.forward * (frame < totalFrames * 0.55f ? 4.8f : 3.9f) +
                player.transform.right * Mathf.Sin(frame * 0.13f) * 0.45f +
                Vector3.down * 2.0f;

            characterController.Move(motion * Time.deltaTime);

            if (frame == WarmupFrames + 90)
                CaptureOverviewImage(mainCamera, pileObject.transform.position, player.transform.forward, screenshotPath);

            if (frame >= WarmupFrames)
                frameTimesMs.Add(Time.unscaledDeltaTime * 1000.0f);

            yield return null;
        }

        yield return WaitFrames(15);

        Assert.IsNotEmpty(frameTimesMs, "没有采到任何帧时间数据。");

        BenchmarkResult result = BuildResult(frameTimesMs, screenshotPath, reportPath);
        File.WriteAllText(jsonPath, JsonUtility.ToJson(result, true), Encoding.UTF8);
        File.WriteAllText(reportPath, BuildReport(result), Encoding.UTF8);

        Debug.Log(
            $"BottlePile benchmark 完成: avg {result.averageFrameMs:F2} ms, p95 {result.p95FrameMs:F2} ms, max {result.maxFrameMs:F2} ms");
    }

    private static BenchmarkResult BuildResult(IReadOnlyList<float> frameTimesMs, string screenshotPath, string reportPath)
    {
        List<float> sorted = frameTimesMs.OrderBy(value => value).ToList();
        int p95Index = Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.95f) - 1, 0, sorted.Count - 1);

        return new BenchmarkResult
        {
            sceneName = SceneName,
            pileObjectName = PileObjectName,
            warmupFrames = WarmupFrames,
            benchmarkFrames = BenchmarkFrames,
            averageFrameMs = frameTimesMs.Average(),
            p95FrameMs = sorted[p95Index],
            minFrameMs = sorted[0],
            maxFrameMs = sorted[sorted.Count - 1],
            screenshotPath = screenshotPath,
            reportPath = reportPath,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        };
    }

    private static string BuildReport(BenchmarkResult result)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# SampleScene 瓶堆短跑结果");
        builder.AppendLine();
        builder.AppendLine($"- 时间: {result.timestamp}");
        builder.AppendLine($"- 场景: {result.sceneName}");
        builder.AppendLine($"- 目标对象: {result.pileObjectName}");
        builder.AppendLine($"- 预热帧: {result.warmupFrames}");
        builder.AppendLine($"- 采样帧: {result.benchmarkFrames}");
        builder.AppendLine($"- 平均帧时间: {result.averageFrameMs:F2} ms");
        builder.AppendLine($"- P95 帧时间: {result.p95FrameMs:F2} ms");
        builder.AppendLine($"- 最快帧时间: {result.minFrameMs:F2} ms");
        builder.AppendLine($"- 最慢帧时间: {result.maxFrameMs:F2} ms");
        builder.AppendLine($"- 截图路径: {result.screenshotPath}");
        builder.AppendLine();
        builder.AppendLine("说明：该结果来自编辑器 PlayMode 自动短跑，数值可用于当前实现的回归对比，不等同于最终构建版本性能。");
        return builder.ToString();
    }

    private static void TeleportPlayerForApproach(Transform playerTransform, CharacterController characterController, Vector3 pilePosition)
    {
        Vector3 direction = Vector3.ProjectOnPlane(pilePosition - playerTransform.position, Vector3.up);
        if (direction.sqrMagnitude < 1e-4f)
            direction = Vector3.forward;

        direction.Normalize();

        Vector3 targetPosition = pilePosition - direction * 5.6f;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
            targetPosition.y = terrain.SampleHeight(targetPosition) + terrain.transform.position.y + 0.05f;
        else
            targetPosition.y = Mathf.Max(targetPosition.y, 0.05f);

        bool restoreController = characterController.enabled;
        characterController.enabled = false;
        playerTransform.SetPositionAndRotation(targetPosition, Quaternion.LookRotation(direction, Vector3.up));
        characterController.enabled = restoreController;
    }

    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
            yield return null;
    }

    private static void CaptureCameraImage(Camera camera, string screenshotPath)
    {
        if (camera == null)
            return;

        const int width = 1280;
        const int height = 720;

        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;

        camera.targetTexture = renderTexture;
        camera.Render();

        RenderTexture.active = renderTexture;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply(false, false);

        byte[] pngBytes = texture.EncodeToPNG();
        File.WriteAllBytes(screenshotPath, pngBytes);

        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(renderTexture);
        UnityEngine.Object.Destroy(texture);
    }

    private static void CaptureOverviewImage(Camera camera, Vector3 pilePosition, Vector3 forwardDirection, string screenshotPath)
    {
        if (camera == null)
            return;

        Transform cameraTransform = camera.transform;
        Terrain terrain = Terrain.activeTerrain;
        Vector3 previousPosition = cameraTransform.position;
        Quaternion previousRotation = cameraTransform.rotation;
        float previousFieldOfView = camera.fieldOfView;
        bool previousDrawTreesAndFoliage = terrain != null && terrain.drawTreesAndFoliage;

        Vector3 flatForward = Vector3.ProjectOnPlane(forwardDirection, Vector3.up);
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;

        flatForward.Normalize();
        Vector3 lateral = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 cameraPosition = pilePosition - flatForward * 2.6f + lateral * 1.35f + Vector3.up * 1.45f;
        Vector3 lookTarget = pilePosition + Vector3.up * 0.45f;

        if (terrain != null)
            terrain.drawTreesAndFoliage = false;

        camera.fieldOfView = 32.0f;
        cameraTransform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation((lookTarget - cameraPosition).normalized, Vector3.up));

        CaptureCameraImage(camera, screenshotPath);

        if (terrain != null)
            terrain.drawTreesAndFoliage = previousDrawTreesAndFoliage;

        camera.fieldOfView = previousFieldOfView;
        cameraTransform.SetPositionAndRotation(previousPosition, previousRotation);
    }

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
}
