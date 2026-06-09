using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[Serializable]
public sealed class RenderDocCaptureManifest
{
    public string captureId;
    public string label;
    public string triggerMode;
    public string status;
    public string timestampLocal;
    public string timestampUtc;
    public int requestedFrameIndex;
    public int capturedFrameIndex;
    public string unityVersion;
    public string applicationPlatform;
    public string runContext;
    public string sceneName;
    public string scenePath;
    public string graphicsDeviceType;
    public string graphicsDeviceName;
    public string renderPipelineAssetName;
    public int screenWidth;
    public int screenHeight;
    public string qualityLevel;
    public string cameraName;
    public Vector3 cameraPosition;
    public Vector3 cameraForward;
    public string renderDocDllPath;
    public string captureFilePath;
    public string screenshotFilePath;
    public string unityMaterialMetadataPath;
    public string unityPrefabMetadataPath;
    public string unityPrefabSignatureDatabasePath;
    public string notes;
    public ulong renderDocCaptureTimestampUnixSeconds;
    public RenderDocCaptureManifestProviderEntry[] providers = Array.Empty<RenderDocCaptureManifestProviderEntry>();
}

[Serializable]
public sealed class RenderDocCaptureManifestProviderEntry
{
    public string providerId;
    public string summary;
}

[Serializable]
public sealed class RenderDocCaptureArtifactInfo
{
    public string artifactDirectory;
    public string captureFilePath;
    public string screenshotFilePath;
    public string unityMaterialMetadataPath;
    public string unityPrefabMetadataPath;
    public string unityPrefabSignatureDatabasePath;
}

public static class RenderDocCaptureArtifacts
{
    public const string DefaultRelativeArtifactsRoot = ".workspace/artifacts/renderdoc-captures";
    public const string DefaultRelativeAiDebugArtifactsRoot = ".workspace/artifacts/ai-debug";
    public const string ScreenshotFileName = "gameview.png";
    public const string UnityMaterialMetadataFileName = "unity_material_metadata.json";
    public const string UnityPrefabMetadataFileName = "unity_prefab_metadata.json";
    public const string UnityPrefabSignatureDatabaseFileName = "unity_prefab_signatures.sqlite";
    public const string AiDebugProfileFileName = "external-player-profile.json";
    public const string AiDebugReproTracesDirectoryName = "repro-traces";
    public const string CaptureTemplateBaseName = "capture";

    public static string GetApplicationRoot()
    {
        string dataPath = Application.dataPath;
        if (string.IsNullOrEmpty(dataPath))
            return Directory.GetCurrentDirectory();

        DirectoryInfo parent = Directory.GetParent(dataPath);
        return parent != null ? parent.FullName : Directory.GetCurrentDirectory();
    }

    public static string ResolveArtifactsRoot(string overridePath, string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return NormalizePath(overridePath);

        string baseRoot = GetApplicationRoot();
        string resolvedRelative = string.IsNullOrWhiteSpace(relativePath) ? DefaultRelativeArtifactsRoot : relativePath;
        return NormalizePath(Path.Combine(baseRoot, resolvedRelative));
    }

    public static string BuildCaptureDirectoryName(DateTime localTimestamp, string sceneName, string label)
    {
        string sanitizedSceneName = SanitizeLabel(sceneName);
        string sanitizedLabel = SanitizeLabel(label);
        string timestamp = localTimestamp.ToString("yyyyMMdd_HHmmss");
        if (string.IsNullOrEmpty(sanitizedSceneName))
            sanitizedSceneName = "scene";

        if (string.IsNullOrEmpty(sanitizedLabel))
            return string.Format("{0}_{1}", timestamp, sanitizedSceneName);

        return string.Format("{0}_{1}-{2}", timestamp, sanitizedSceneName, sanitizedLabel);
    }

    public static string SanitizeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "capture";

        StringBuilder builder = new StringBuilder(value.Length);
        bool previousWasSeparator = false;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            bool isValidCharacter = char.IsLetterOrDigit(character) || character == '-' || character == '_';
            if (isValidCharacter)
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        string sanitized = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(sanitized) ? "capture" : sanitized;
    }

    public static string CreateArtifactDirectory(string artifactsRoot, DateTime localTimestamp, string sceneName, string label)
    {
        string root = NormalizePath(artifactsRoot);
        Directory.CreateDirectory(root);
        string directoryName = BuildCaptureDirectoryName(localTimestamp, sceneName, label);
        string fullPath = Path.Combine(root, directoryName);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public static string BuildCaptureTemplatePath(string artifactDirectory)
    {
        return Path.Combine(artifactDirectory, CaptureTemplateBaseName);
    }

    public static string BuildScreenshotPath(string artifactDirectory)
    {
        return Path.Combine(artifactDirectory, ScreenshotFileName);
    }

    public static string BuildUnityMaterialMetadataPath(string artifactDirectory)
    {
        return Path.Combine(artifactDirectory, UnityMaterialMetadataFileName);
    }

    public static string BuildUnityPrefabMetadataPath(string artifactDirectory)
    {
        return Path.Combine(artifactDirectory, UnityPrefabMetadataFileName);
    }

    public static string BuildUnityPrefabSignatureDatabasePath(string artifactDirectory)
    {
        return Path.Combine(artifactDirectory, UnityPrefabSignatureDatabaseFileName);
    }

    public static string BuildAnalysisPrompt(RenderDocCaptureManifest manifest)
    {
        if (manifest == null)
            return string.Empty;

        RenderDocCaptureArtifactInfo artifactInfo = new RenderDocCaptureArtifactInfo
        {
            captureFilePath = manifest.captureFilePath,
            screenshotFilePath = manifest.screenshotFilePath,
            unityMaterialMetadataPath = manifest.unityMaterialMetadataPath,
            unityPrefabMetadataPath = manifest.unityPrefabMetadataPath,
            unityPrefabSignatureDatabasePath = manifest.unityPrefabSignatureDatabasePath
        };

        string prompt = BuildAnalysisPrompt(artifactInfo);
        if (manifest.providers == null || manifest.providers.Length == 0)
            return prompt;

        StringBuilder builder = new StringBuilder(prompt.Length + 256);
        builder.Append(prompt.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Provider 摘要：");
        for (int index = 0; index < manifest.providers.Length; index++)
        {
            RenderDocCaptureManifestProviderEntry provider = manifest.providers[index];
            builder.AppendLine(string.Format("- {0}: {1}", provider.providerId, provider.summary));
        }

        return builder.ToString();
    }

    public static string BuildAnalysisPrompt(RenderDocCaptureArtifactInfo artifactInfo)
    {
        return BuildAnalysisPrompt(artifactInfo, string.Empty);
    }

    public static string BuildAnalysisPrompt(RenderDocCaptureArtifactInfo artifactInfo, string analysisRequest)
    {
        if (artifactInfo == null)
            return string.Empty;

        string aiDebugRoot = ResolveArtifactsRoot(string.Empty, DefaultRelativeAiDebugArtifactsRoot);
        string aiDebugProfilePath = Path.Combine(aiDebugRoot, AiDebugProfileFileName);
        string aiDebugReproTraceDirectory = Path.Combine(aiDebugRoot, AiDebugReproTracesDirectoryName);
        string aiDebugJsonlHint = Path.Combine(aiDebugRoot, "crowd_ai_debug_*.jsonl");
        string aiDebugMarkdownHint = Path.Combine(aiDebugRoot, "crowd_ai_debug_*.md");
        string trimmedAnalysisRequest = string.IsNullOrWhiteSpace(analysisRequest)
            ? "请先概览该帧，再指出最重要的 GPU 热点、对应运行时原因和优先优化建议。"
            : analysisRequest.Trim();

        StringBuilder builder = new StringBuilder(2048);
        builder.AppendLine("请使用 [$renderdoc-cli](C:\\Users\\huang\\.codex\\skills\\renderdoc-cli\\SKILL.md) Agent Skill 分析以下 RenderDoc 捕获；当前 Unity6 正式离线分析走 rdoc-agent CLI。");
        builder.AppendLine("不要默认运行 `tools/renderdoc-cli/renderdoc_gpu_context_cli.py`；它是 Crowd 系统专用的隐藏分析入口。");
        builder.AppendLine(string.Format("- capture: {0}", artifactInfo.captureFilePath));
        builder.AppendLine(string.Format("- screenshot: {0}", artifactInfo.screenshotFilePath));
        builder.AppendLine(string.Format("- optional unity material metadata: {0}", ResolveUnityMaterialMetadataPath(artifactInfo)));
        builder.AppendLine(string.Format("- optional unity prefab metadata: {0}", ResolveUnityPrefabMetadataPath(artifactInfo)));
        builder.AppendLine(string.Format("- optional unity prefab signature sqlite for entity list Prefab timing / entity match-prefabs: {0}", ResolveUnityPrefabSignatureDatabasePath(artifactInfo)));
        builder.AppendLine(string.Format("- postprocess fact DB: python tools/renderdoc-db/rdc_capture_postprocess.py --capture \"{0}\" --prefab-signatures \"{1}\" --json --pretty", artifactInfo.captureFilePath, ResolveUnityPrefabSignatureDatabasePath(artifactInfo)));
        builder.AppendLine("- after postprocess, query prefab GPU cost with: .\\tools\\renderdoc-cli\\rdoc-agent.cmd db instance-cost --prefab-name <PrefabName> --json --pretty");
        builder.AppendLine(string.Format("- crowd-only ai debug jsonl: {0}", aiDebugJsonlHint));
        builder.AppendLine(string.Format("- crowd-only ai debug md: {0}", aiDebugMarkdownHint));
        builder.AppendLine(string.Format("- crowd-only ai debug profile: {0}", aiDebugProfilePath));
        builder.AppendLine(string.Format("- crowd-only ai debug repro traces: {0}", aiDebugReproTraceDirectory));
        builder.AppendLine();
        builder.AppendLine(string.Format("分析目标：{0}", trimmedAnalysisRequest));
        builder.AppendLine();
        builder.AppendLine("必须先接入 rdoc-agent 数据：");
        builder.AppendLine(string.Format("1. .\\tools\\renderdoc-cli\\rdoc-agent.cmd capture info --capture \"{0}\" --json --pretty", artifactInfo.captureFilePath));
        builder.AppendLine(string.Format("2. .\\tools\\renderdoc-cli\\rdoc-agent.cmd events overview --capture \"{0}\" --json --pretty", artifactInfo.captureFilePath));
        builder.AppendLine(string.Format("3. .\\tools\\renderdoc-cli\\rdoc-agent.cmd events expand --capture \"{0}\" --event <eventId> --depth 1 --json --pretty", artifactInfo.captureFilePath));
        builder.AppendLine(string.Format("4. .\\tools\\renderdoc-cli\\rdoc-agent.cmd find --capture \"{0}\" --q \"<name>\" --scope last --json --pretty", artifactInfo.captureFilePath));
        builder.AppendLine(string.Format("5. .\\tools\\renderdoc-cli\\rdoc-agent.cmd entity list --capture \"{0}\" --from-search <searchId> --json --pretty", artifactInfo.captureFilePath));
        builder.AppendLine(string.Format("6. 如需单独查看 Prefab 候选细节：.\\tools\\renderdoc-cli\\rdoc-agent.cmd entity match-prefabs --capture \"{0}\" --from-search <searchId> --prefab-signatures \"{1}\" --json --pretty", artifactInfo.captureFilePath, ResolveUnityPrefabSignatureDatabasePath(artifactInfo)));
        builder.AppendLine("7. 从高耗时实体或 `prefabTimingSummary` 选择 `representativeEventId`，并把它作为后续回查锚点保存；当前 rdoc-agent 不再提供 `events inspect`，如需 event 级绑定细节请改用 qrenderdoc 或后续专用工具。");
        builder.AppendLine("8. 只有分析目标明确涉及 Crowd/VAT 时，才配对 `.workspace/artifacts/ai-debug/crowd_ai_debug_*.jsonl` 并用 `tools/ai-debug-cli/crowd_ai_debug_query.py` 查询 runtime 旁证。");
        builder.AppendLine();
        builder.AppendLine("分析约束：");
        builder.AppendLine("- `entity list` 中“实体”仍指 capture-visible draw signature 聚合；如 capture 同目录存在 `unity_prefab_signatures.sqlite`，会额外输出 Prefab 候选和 `prefabTimingSummary`。");
        builder.AppendLine("- `entity list` 输出 `entityCount`、`entities[]` 与 `aggregationSummary`；每个实体包含 `representativeEventId`、event ids、pass/LOD/shader/texture 摘要和签名；有 SQLite 时还包含 Prefab/LOD GPU timing。");
        builder.AppendLine("- `entity match-prefabs` 仍可用于单独查看 SQLite 候选细节；Prefab 候选是当前加载场景中的实例侧候选，不是 RenderDoc 原生事实。");
        builder.AppendLine("- 当前 rdoc-agent 不再提供完整 pipeline/shader/resource 绑定的单 event 读取动作；如需这些细节，只保留 `representativeEventId` 供后续 qrenderdoc 或专用工具回查。");
        builder.AppendLine("- `tools/renderdoc-cli/renderdoc_gpu_context_cli.py` 依赖 BeginSample / ProfilingScope / CommandBuffer marker / 自定义 draw label，只适合 Crowd 专用语义分析，普通 MeshRenderer + Material 不走它。");
        builder.AppendLine("- 普通 MeshRenderer + Material 绘制没有稳定业务 label 时，不要强行猜 pass_id；只报告 RenderDoc 中可核查的事件名、shader hash、resource 与 timing。");
        builder.AppendLine("- AI Debug JSONL 是 Crowd runtime 旁证，只有相关时才使用；它不替代 RenderDoc timing。");
        builder.AppendLine("- 不要输出 suggested_next_queries；由你根据问题自主决定下一步查哪个 pass、resource、provider、frame、agent id 或 raw evidence。");
        builder.AppendLine("- 不要默认全文打开完整 AI Debug JSONL；优先使用查询 CLI 的分层命令。");
        builder.AppendLine("- 不通过 GUID、文件名或源码搜索把 RenderDoc draw 手工推断为本地材质、Shader、Prefab 或 Renderer 身份；Prefab 场景实例关联以 `unity_prefab_signatures.sqlite` 候选为准。");
        builder.AppendLine("- 如果本次确实是 Crowd 语义 pass 问题，再回查 `docs/gpu-pass-catalog/gpu_pass_catalog.yaml` 并结合 `.rdc` marker 与 AI Debug 旁证；普通 MeshRenderer 分析不以 catalog 为前置。");
        builder.AppendLine();
        builder.AppendLine("请输出：");
        builder.AppendLine("- rdoc-agent 打开状态和使用的 `.rdc` 路径");
        builder.AppendLine("- 帧概览：top-level markers、draw/dispatch 覆盖范围和可用 GPU timing");
        builder.AppendLine("- 最重的 draw / dispatch：event id、timing、shader、pipeline state、关键资源");
        builder.AppendLine("- 普通 MeshRenderer/Material 或 Crowd 专用证据的适用性判断");
        builder.AppendLine("- 如果使用了 AI Debug runtime 旁证，说明配对路径、配对置信度和它只支持哪些结论");
        builder.AppendLine("- 与项目业务最相关的热点及捕获可见 mesh / shader hash / texture / resource 线索");
        builder.AppendLine("- 3 条最高优先级优化建议，并说明每条对应的 RenderDoc 证据");
        return builder.ToString();
    }

    public static bool TryLoadLatestArtifactInfo(string artifactsRoot, out RenderDocCaptureArtifactInfo artifactInfo)
    {
        artifactInfo = null;
        string root = NormalizePath(artifactsRoot);
        if (!Directory.Exists(root))
            return false;

        string[] capturePaths = Directory.GetFiles(root, "*.rdc", SearchOption.AllDirectories);
        if (capturePaths == null || capturePaths.Length == 0)
            return false;

        string latestCapturePath = string.Empty;
        DateTime latestCaptureTimeUtc = DateTime.MinValue;
        for (int index = 0; index < capturePaths.Length; index++)
        {
            string capturePath = capturePaths[index];
            DateTime captureTimeUtc = File.GetLastWriteTimeUtc(capturePath);
            bool isNewer = captureTimeUtc > latestCaptureTimeUtc;
            bool isSameTimeButLaterPath =
                captureTimeUtc == latestCaptureTimeUtc &&
                !string.IsNullOrEmpty(latestCapturePath) &&
                StringComparer.OrdinalIgnoreCase.Compare(capturePath, latestCapturePath) > 0;

            if (!isNewer && !isSameTimeButLaterPath)
                continue;

            latestCapturePath = capturePath;
            latestCaptureTimeUtc = captureTimeUtc;
        }

        if (string.IsNullOrEmpty(latestCapturePath))
            return false;

        string artifactDirectory = Path.GetDirectoryName(latestCapturePath);
        if (string.IsNullOrEmpty(artifactDirectory))
            return false;

        artifactInfo = new RenderDocCaptureArtifactInfo
        {
            artifactDirectory = artifactDirectory,
            captureFilePath = latestCapturePath,
            screenshotFilePath = BuildScreenshotPath(artifactDirectory),
            unityMaterialMetadataPath = BuildUnityMaterialMetadataPath(artifactDirectory),
            unityPrefabMetadataPath = BuildUnityPrefabMetadataPath(artifactDirectory),
            unityPrefabSignatureDatabasePath = BuildUnityPrefabSignatureDatabasePath(artifactDirectory)
        };
        return true;
    }

    private static string ResolveUnityMaterialMetadataPath(RenderDocCaptureArtifactInfo artifactInfo)
    {
        if (!string.IsNullOrWhiteSpace(artifactInfo.unityMaterialMetadataPath))
            return artifactInfo.unityMaterialMetadataPath;

        if (!string.IsNullOrWhiteSpace(artifactInfo.artifactDirectory))
            return BuildUnityMaterialMetadataPath(artifactInfo.artifactDirectory);

        return UnityMaterialMetadataFileName;
    }

    private static string ResolveUnityPrefabMetadataPath(RenderDocCaptureArtifactInfo artifactInfo)
    {
        if (!string.IsNullOrWhiteSpace(artifactInfo.unityPrefabMetadataPath))
            return artifactInfo.unityPrefabMetadataPath;

        if (!string.IsNullOrWhiteSpace(artifactInfo.artifactDirectory))
            return BuildUnityPrefabMetadataPath(artifactInfo.artifactDirectory);

        return UnityPrefabMetadataFileName;
    }

    private static string ResolveUnityPrefabSignatureDatabasePath(RenderDocCaptureArtifactInfo artifactInfo)
    {
        if (!string.IsNullOrWhiteSpace(artifactInfo.unityPrefabSignatureDatabasePath))
            return artifactInfo.unityPrefabSignatureDatabasePath;

        if (!string.IsNullOrWhiteSpace(artifactInfo.artifactDirectory))
            return BuildUnityPrefabSignatureDatabasePath(artifactInfo.artifactDirectory);

        return UnityPrefabSignatureDatabaseFileName;
    }

    public static string GetActiveRenderPipelineAssetName()
    {
        RenderPipelineAsset renderPipelineAsset = GraphicsSettings.currentRenderPipeline;
        return renderPipelineAsset != null ? renderPipelineAsset.name : "Built-in";
    }

    public static string GetActiveQualityLevelName()
    {
        int qualityIndex = QualitySettings.GetQualityLevel();
        string[] qualityNames = QualitySettings.names;
        if (qualityNames == null || qualityIndex < 0 || qualityIndex >= qualityNames.Length)
            return "Unknown";

        return qualityNames[qualityIndex];
    }

    public static string GetActiveSceneName()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.name))
            return "scene";

        return activeScene.name;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', Path.DirectorySeparatorChar);
    }
}
