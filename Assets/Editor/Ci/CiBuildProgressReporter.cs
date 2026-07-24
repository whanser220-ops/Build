using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Unity6.Ci
{
    public static class CiBuildProgressReporter
    {
        private const int RequestTimeoutMs = 2000;

        public static void ReportStage(string stageId, string stageName, int percent, string message = "")
        {
            Report("stage", stageId, stageName, "running", string.Empty, percent, message);
        }

        public static void ReportSuccess(string stageId, string stageName, int percent, string message = "")
        {
            Report("success", stageId, stageName, "success", "SUCCESS", percent, message);
        }

        public static void ReportFailure(string stageId, string stageName, int percent, Exception exception)
        {
            string message = exception == null ? "Unity build failed." : exception.GetType().Name + ": " + exception.Message;
            Report("failure", stageId, stageName, "failure", "FAILURE", percent, message);
        }

        private static void Report(
            string eventType,
            string stageId,
            string stageName,
            string state,
            string result,
            int percent,
            string message)
        {
            string url = Environment.GetEnvironmentVariable("BUILD_PROGRESS_URL");
            string token = Environment.GetEnvironmentVariable("BUILD_PROGRESS_TOKEN");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
                return;

            string payload = CreatePayload(eventType, stageId, stageName, state, result, percent, message);
            ThreadPool.QueueUserWorkItem(_ => Send(url, token, payload));
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
                // Progress reporting must never fail the Unity build.
            }
        }

        private static string CreatePayload(
            string eventType,
            string stageId,
            string stageName,
            string state,
            string result,
            int percent,
            string message)
        {
            List<string> fields = new List<string>
            {
                JsonField("runId", ResolveRunId()),
                JsonField("jobName", GetEnv("BUILD_PROGRESS_JOB", "unity-linux-docker-build")),
                JsonNumberField("buildNumber", GetEnv("BUILD_NUMBER", string.Empty)),
                JsonField("eventType", eventType),
                JsonField("stageId", stageId),
                JsonField("stageName", stageName),
                JsonField("state", state),
                JsonField("result", result),
                JsonNumberField("percent", percent.ToString(CultureInfo.InvariantCulture)),
                JsonField("message", message),
                JsonField("gitRef", GetEnv("GIT_REF", string.Empty)),
                JsonField("gitCommit", GetEnv("GIT_COMMIT", string.Empty)),
                "\"metadata\":{" +
                    JsonField("unityVersion", Application.unityVersion) + "," +
                    JsonField("platform", Application.platform.ToString()) +
                "}"
            };

            return "{" + string.Join(",", fields) + "}";
        }

        private static string ResolveRunId()
        {
            string configured = Environment.GetEnvironmentVariable("BUILD_PROGRESS_RUN_ID");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            string jobName = GetEnv("JOB_NAME", "unity-linux-docker-build");
            string buildNumber = GetEnv("BUILD_NUMBER", "local");
            return jobName + "-" + buildNumber;
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

        private static string JsonNumberField(string name, string value)
        {
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return "\"" + JsonEscape(name) + "\":null";

            return "\"" + JsonEscape(name) + "\":" + parsed.ToString(CultureInfo.InvariantCulture);
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
    }
}
