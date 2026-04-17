using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#if UNITY_EDITOR
[PrebuildSetup(typeof(QianxiaCrowdSampleScenePlayModeSetup))]
#endif
public class QianxiaCrowdSampleScenePlayModeTests
{
    private const string SceneName = "SampleScene";
    private const string CrowdObjectName = "QianxiaCrowdIndirect";

    [UnityTest]
    public IEnumerator SampleScene_Crowd_PlayModeCapture()
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        Assert.IsNotNull(loadOperation, "SampleScene 加载失败。");
        while (!loadOperation.isDone)
            yield return null;

        yield return WaitFrames(30);

        GameObject crowdObject = GameObject.Find(CrowdObjectName);
        Assert.IsNotNull(crowdObject, $"场景中缺少 {CrowdObjectName}。");

        Component renderer = crowdObject.GetComponent("CrowdVatIndirectRenderer");
        Assert.IsNotNull(renderer, $"{CrowdObjectName} 缺少 CrowdVatIndirectRenderer。");

        Camera mainCamera = Camera.main;
        Assert.IsNotNull(mainCamera, "场景中缺少 Main Camera。");

        string artifactDirectory = Path.Combine(ProjectRoot, ".workspace", "artifacts", "qianxia-crowd-sample-scene");
        Directory.CreateDirectory(artifactDirectory);

        string mainScreenshotPath = Path.Combine(artifactDirectory, "sample-scene-main-camera.png");
        string overviewScreenshotPath = Path.Combine(artifactDirectory, "sample-scene-overview-camera.png");

        if (File.Exists(mainScreenshotPath))
            File.Delete(mainScreenshotPath);

        if (File.Exists(overviewScreenshotPath))
            File.Delete(overviewScreenshotPath);

        CaptureCameraImage(mainCamera, mainScreenshotPath);
        CaptureOverviewImage(mainCamera, crowdObject.transform.position, overviewScreenshotPath);

        Debug.Log($"SampleScene crowd PlayMode 截图：main={mainScreenshotPath} overview={overviewScreenshotPath}");
        yield return null;
    }

    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            yield return null;
    }

    private static void CaptureOverviewImage(Camera camera, Vector3 crowdPosition, string screenshotPath)
    {
        Transform cameraTransform = camera.transform;
        Vector3 previousPosition = cameraTransform.position;
        Quaternion previousRotation = cameraTransform.rotation;
        float previousFieldOfView = camera.fieldOfView;

        Vector3 cameraPosition = crowdPosition + new Vector3(0.0f, 9.0f, -7.0f);
        Vector3 lookTarget = crowdPosition + Vector3.up * 1.8f;

        camera.fieldOfView = 34.0f;
        cameraTransform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation((lookTarget - cameraPosition).normalized, Vector3.up));

        CaptureCameraImage(camera, screenshotPath);

        camera.fieldOfView = previousFieldOfView;
        cameraTransform.SetPositionAndRotation(previousPosition, previousRotation);
    }

    private static void CaptureCameraImage(Camera camera, string screenshotPath)
    {
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
        Object.Destroy(texture);
    }

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
}
