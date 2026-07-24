using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Unity6.Ci
{
    public sealed class CiBuildMetricAssetType
    {
        public string assetType = string.Empty;
        public int count;
        public long sizeBytes;
    }

    public static class CiBuildMetricsReporter
    {
        private const int RequestTimeoutMs = 2000;
        private static readonly object PendingLock = new object();
        private static readonly object EditorContextLock = new object();
        private static readonly ManualResetEventSlim PendingIdle = new ManualResetEventSlim(true);
        private static int _pendingRequests;
        private static string _cachedBuildTarget = string.Empty;
        private static string _cachedUnityVersion = string.Empty;
        private static string _cachedPlatform = string.Empty;

        public static void CaptureEditorContext()
        {
            try
            {
                lock (EditorContextLock)
                {
                    _cachedBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
                    _cachedUnityVersion = Application.unityVersion;
                    _cachedPlatform = Application.platform.ToString();
                }
            }
            catch
            {
                // Build metrics must never fail the Unity build.
            }
        }

        public static void ReportRunStarted(string message = "")
        {
            Report(new MetricEvent
            {
                eventType = "run_started",
                state = "running",
                message = message
            });
        }

        public static void ReportRunFinished(string state, string result, long durationMs, string message = "")
        {
            Report(new MetricEvent
            {
                eventType = "run_finished",
                state = state,
                result = result,
                durationMs = durationMs,
                message = message
            });
        }

        public static void ReportStageStarted(string stageId, string stageName, string message = "")
        {
            Report(new MetricEvent
            {
                eventType = "stage_started",
                stageId = stageId,
                stageName = stageName,
                state = "running",
                message = message
            });
        }

        public static void ReportStageFinished(
            string stageId,
            string stageName,
            long durationMs,
            string state = "success",
            string result = "SUCCESS",
            string message = "")
        {
            Report(new MetricEvent
            {
                eventType = state == "failure" ? "stage_failed" : "stage_finished",
                stageId = stageId,
                stageName = stageName,
                state = state,
                result = result,
                durationMs = durationMs,
                message = message
            });
        }

        public static void ReportFailure(string stageId, string stageName, Exception exception)
        {
            string message = exception == null
                ? "Unity build failed."
                : exception.GetType().Name + ": " + exception.Message;

            Report(new MetricEvent
            {
                eventType = "stage_failed",
                stageId = stageId,
                stageName = stageName,
                state = "failure",
                result = "FAILURE",
                message = message
            });
        }

        public static void ReportBundleStarted(
            string bundleName,
            int completedBundles,
            int totalBundles,
            long inputSizeBytes,
            int assetCount,
            bool cached)
        {
            Report(new MetricEvent
            {
                eventType = "bundle_started",
                bundleName = bundleName,
                state = "running",
                completedBundles = completedBundles,
                totalBundles = totalBundles,
                inputSizeBytes = inputSizeBytes,
                assetCount = assetCount,
                cached = cached,
                message = cached ? "Copying cached AssetBundle." : "Archiving AssetBundle."
            });
        }

        public static void ReportBundleFinished(
            string bundleName,
            int completedBundles,
            int totalBundles,
            long inputSizeBytes,
            long sizeBytes,
            int assetCount,
            bool cached,
            long durationMs)
        {
            Report(new MetricEvent
            {
                eventType = "bundle_finished",
                bundleName = bundleName,
                state = "success",
                result = "SUCCESS",
                completedBundles = completedBundles,
                totalBundles = totalBundles,
                inputSizeBytes = inputSizeBytes,
                sizeBytes = sizeBytes,
                assetCount = assetCount,
                cached = cached,
                durationMs = durationMs,
                message = cached ? "Cached AssetBundle copied." : "AssetBundle archived."
            });
        }

        public static void ReportBundleFailed(
            string bundleName,
            int completedBundles,
            int totalBundles,
            long inputSizeBytes,
            int assetCount,
            bool cached,
            long durationMs,
            Exception exception)
        {
            Report(new MetricEvent
            {
                eventType = "bundle_failed",
                bundleName = bundleName,
                state = "failure",
                result = "FAILURE",
                completedBundles = completedBundles,
                totalBundles = totalBundles,
                inputSizeBytes = inputSizeBytes,
                assetCount = assetCount,
                cached = cached,
                durationMs = durationMs,
                message = exception == null
                    ? "AssetBundle failed."
                    : exception.GetType().Name + ": " + exception.Message
            });
        }

        public static void ReportAssetTypes(IEnumerable<CiBuildMetricAssetType> assetTypes)
        {
            Report(new MetricEvent
            {
                eventType = "asset_type_summary",
                state = "running",
                assetTypes = assetTypes == null
                    ? new List<CiBuildMetricAssetType>()
                    : new List<CiBuildMetricAssetType>(assetTypes),
                message = "Asset type size summary updated."
            });
        }

        public static void Flush(int timeoutMs = 8000)
        {
            try
            {
                PendingIdle.Wait(Math.Max(0, timeoutMs));
            }
            catch
            {
                // Build metrics must not affect the Unity build.
            }
        }

        private static void Report(MetricEvent metricEvent)
        {
            string url = Environment.GetEnvironmentVariable("BUILD_METRICS_URL");
            string token = Environment.GetEnvironmentVariable("BUILD_METRICS_INGEST_TOKEN");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
                return;

            string payload = CreatePayload(metricEvent);
            IncrementPending();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Send(url, token, payload);
                }
                finally
                {
                    DecrementPending();
                }
            });
        }

        private static void Send(string url, string token, string payload)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(payload);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = RequestTimeoutMs;
                request.ReadWriteTimeout = RequestTimeoutMs;
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                request.ContentLength = body.Length;

                using (Stream stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);

                using (request.GetResponse())
                {
                }
            }
            catch
            {
                // Build metrics must never fail the Unity build.
            }
        }

        private static void IncrementPending()
        {
            lock (PendingLock)
            {
                _pendingRequests++;
                PendingIdle.Reset();
            }
        }

        private static void DecrementPending()
        {
            lock (PendingLock)
            {
                _pendingRequests = Math.Max(0, _pendingRequests - 1);
                if (_pendingRequests == 0)
                    PendingIdle.Set();
            }
        }

        private static string CreatePayload(MetricEvent metricEvent)
        {
            List<string> fields = new List<string>
            {
                JsonField("runId", ResolveRunId()),
                JsonField("jobName", GetEnv("BUILD_METRICS_JOB", "unity-linux-docker-build")),
                JsonNumberField("buildNumber", GetEnv("BUILD_NUMBER", string.Empty)),
                JsonField("eventType", metricEvent.eventType),
                JsonField("stageId", metricEvent.stageId),
                JsonField("stageName", metricEvent.stageName),
                JsonField("bundleName", metricEvent.bundleName),
                JsonField("state", metricEvent.state),
                JsonField("result", metricEvent.result),
                JsonNumberField("durationMs", metricEvent.durationMs),
                JsonNumberField("totalBundles", metricEvent.totalBundles),
                JsonNumberField("completedBundles", metricEvent.completedBundles),
                JsonNumberField("sizeBytes", metricEvent.sizeBytes),
                JsonNumberField("inputSizeBytes", metricEvent.inputSizeBytes),
                JsonNumberField("assetCount", metricEvent.assetCount),
                JsonNullableBoolField("cached", metricEvent.cached),
                JsonField("message", metricEvent.message),
                JsonField("gitRef", GetEnv("GIT_REF", string.Empty)),
                JsonField("gitCommit", ResolveGitCommit()),
                JsonField("buildTarget", GetEnv("BUILD_TARGET", GetCachedEditorContextValue(ref _cachedBuildTarget))),
                JsonField("packageName", GetEnv("YOOASSET_PACKAGE_NAME", "DefaultPackage")),
                "\"metadata\":{" +
                    JsonField("unityVersion", GetEnv("UNITY_VERSION", GetCachedEditorContextValue(ref _cachedUnityVersion))) + "," +
                    JsonField("platform", GetCachedEditorContextValue(ref _cachedPlatform)) +
                "}",
                JsonAssetTypesField(metricEvent.assetTypes)
            };

            return "{" + string.Join(",", fields) + "}";
        }

        private static string GetCachedEditorContextValue(ref string value)
        {
            lock (EditorContextLock)
                return value ?? string.Empty;
        }

        private static string ResolveRunId()
        {
            string configured = Environment.GetEnvironmentVariable("BUILD_METRICS_RUN_ID");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            string jobName = GetEnv("JOB_NAME", "unity-linux-docker-build");
            string buildNumber = GetEnv("BUILD_NUMBER", "local");
            return jobName + "-" + buildNumber;
        }

        private static string ResolveGitCommit()
        {
            string commit = Environment.GetEnvironmentVariable("GIT_COMMIT");
            if (!string.IsNullOrWhiteSpace(commit))
                return commit;

            try
            {
                string headPath = Path.Combine(Directory.GetCurrentDirectory(), ".git", "HEAD");
                if (!File.Exists(headPath))
                    return string.Empty;

                string head = File.ReadAllText(headPath).Trim();
                if (!head.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
                    return head.Length > 12 ? head.Substring(0, 12) : head;

                string refPath = Path.Combine(Directory.GetCurrentDirectory(), ".git", head.Substring(4).Trim());
                if (!File.Exists(refPath))
                    return string.Empty;

                string value = File.ReadAllText(refPath).Trim();
                return value.Length > 12 ? value.Substring(0, 12) : value;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetEnv(string name, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string JsonField(string name, string value)
        {
            return "\"" + JsonEscape(name) + "\":\"" + JsonEscape(value ?? string.Empty) + "\"";
        }

        private static string JsonNumberField(string name, long? value)
        {
            if (!value.HasValue)
                return "\"" + JsonEscape(name) + "\":null";

            return "\"" + JsonEscape(name) + "\":" + value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string JsonNumberField(string name, int? value)
        {
            if (!value.HasValue)
                return "\"" + JsonEscape(name) + "\":null";

            return "\"" + JsonEscape(name) + "\":" + value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string JsonNumberField(string name, string value)
        {
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return "\"" + JsonEscape(name) + "\":null";

            return "\"" + JsonEscape(name) + "\":" + parsed.ToString(CultureInfo.InvariantCulture);
        }

        private static string JsonNullableBoolField(string name, bool? value)
        {
            if (!value.HasValue)
                return "\"" + JsonEscape(name) + "\":null";

            return "\"" + JsonEscape(name) + "\":" + (value.Value ? "true" : "false");
        }

        private static string JsonAssetTypesField(List<CiBuildMetricAssetType> assetTypes)
        {
            if (assetTypes == null || assetTypes.Count == 0)
                return "\"assetTypes\":[]";

            List<string> rows = new List<string>();
            for (int i = 0; i < assetTypes.Count; i++)
            {
                CiBuildMetricAssetType item = assetTypes[i];
                if (item == null || string.IsNullOrWhiteSpace(item.assetType))
                    continue;

                rows.Add("{" +
                    JsonField("assetType", item.assetType) + "," +
                    JsonNumberField("count", item.count) + "," +
                    JsonNumberField("sizeBytes", item.sizeBytes) +
                "}");
            }

            return "\"assetTypes\":[" + string.Join(",", rows) + "]";
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            StringBuilder builder = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character))
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }

        private sealed class MetricEvent
        {
            public string eventType = "event";
            public string stageId = string.Empty;
            public string stageName = string.Empty;
            public string bundleName = string.Empty;
            public string state = "running";
            public string result = string.Empty;
            public string message = string.Empty;
            public long? durationMs;
            public int? totalBundles;
            public int? completedBundles;
            public long? sizeBytes;
            public long? inputSizeBytes;
            public int? assetCount;
            public bool? cached;
            public List<CiBuildMetricAssetType> assetTypes = new List<CiBuildMetricAssetType>();
        }
    }
}
