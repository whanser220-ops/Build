using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

[Serializable]
public struct GpuPassDebugInfo
{
    public string passName;
    public string cppFile;
    public string cppFunction;
    public string shaderFile;
    public string shaderEntry;
    public uint threadGroupX;
    public uint threadGroupY;
    public uint threadGroupZ;
    public string passType;
    public string dispatchKind;
}

public static class GpuPassDebugRegistry
{
    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<string, GpuPassDebugInfo> Infos =
        new Dictionary<string, GpuPassDebugInfo>(StringComparer.Ordinal);

    public static void Register(GpuPassDebugInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.passName))
            throw new ArgumentException("GPU pass debug info requires a pass name.", nameof(info));

        info.passName = info.passName.Trim();
        info.cppFile = info.cppFile ?? string.Empty;
        info.cppFunction = info.cppFunction ?? string.Empty;
        info.shaderFile = info.shaderFile ?? string.Empty;
        info.shaderEntry = info.shaderEntry ?? string.Empty;
        info.passType = info.passType ?? string.Empty;
        info.dispatchKind = info.dispatchKind ?? string.Empty;

        lock (SyncRoot)
        {
            Infos[info.passName] = info;
        }
    }

    public static GpuPassDebugInfo[] Snapshot()
    {
        lock (SyncRoot)
        {
            GpuPassDebugInfo[] snapshot = new GpuPassDebugInfo[Infos.Count];
            Infos.Values.CopyTo(snapshot, 0);
            Array.Sort(snapshot, CompareByPassName);
            return snapshot;
        }
    }

    public static bool TryGet(string passName, out GpuPassDebugInfo info)
    {
        if (string.IsNullOrWhiteSpace(passName))
        {
            info = default;
            return false;
        }

        lock (SyncRoot)
        {
            return Infos.TryGetValue(passName, out info);
        }
    }

    public static string BuildJson(DateTime generatedAtUtc)
    {
        return BuildJson(Snapshot(), generatedAtUtc);
    }

    public static string BuildJson(GpuPassDebugInfo[] infos, DateTime generatedAtUtc)
    {
        if (infos == null)
            infos = Array.Empty<GpuPassDebugInfo>();

        Array.Sort(infos, CompareByPassName);

        StringBuilder builder = new StringBuilder(1024);
        builder.AppendLine("{");
        builder.AppendLine("  \"version\": 1,");
        builder.Append("  \"generated_at_utc\": \"");
        builder.Append(EscapeJson(generatedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
        builder.AppendLine("\",");
        builder.AppendLine("  \"passes\": {");

        for (int index = 0; index < infos.Length; index++)
        {
            GpuPassDebugInfo info = infos[index];
            builder.Append("    \"");
            builder.Append(EscapeJson(info.passName));
            builder.AppendLine("\": {");
            AppendJsonString(builder, "pass_name", info.passName, 6, true);
            AppendJsonString(builder, "cpp_file", info.cppFile, 6, true);
            AppendJsonString(builder, "cpp_function", info.cppFunction, 6, true);
            AppendJsonString(builder, "shader_file", info.shaderFile, 6, true);
            AppendJsonString(builder, "shader_entry", info.shaderEntry, 6, true);
            builder.Append("      \"thread_group\": [");
            builder.Append(info.threadGroupX);
            builder.Append(", ");
            builder.Append(info.threadGroupY);
            builder.Append(", ");
            builder.Append(info.threadGroupZ);
            builder.AppendLine("],");
            AppendJsonString(builder, "type", info.passType, 6, true);
            AppendJsonString(builder, "dispatch_kind", info.dispatchKind, 6, false);
            builder.Append("    }");
            if (index + 1 < infos.Length)
                builder.Append(",");

            builder.AppendLine();
        }

        builder.AppendLine("  }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    public static void WriteJson(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, BuildJson(DateTime.UtcNow), Encoding.UTF8);
    }

    private static int CompareByPassName(GpuPassDebugInfo left, GpuPassDebugInfo right)
    {
        return StringComparer.Ordinal.Compare(left.passName, right.passName);
    }

    private static void AppendJsonString(
        StringBuilder builder,
        string key,
        string value,
        int indent,
        bool trailingComma)
    {
        builder.Append(' ', indent);
        builder.Append("\"");
        builder.Append(EscapeJson(key));
        builder.Append("\": \"");
        builder.Append(EscapeJson(value ?? string.Empty));
        builder.Append("\"");
        if (trailingComma)
            builder.Append(",");

        builder.AppendLine();
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder builder = new StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
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
                    if (character < 32)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }

        return builder.ToString();
    }
}

public static class GpuPassDebugRuntime
{
    private static int _captureMetadataRequestCount;

    public static bool CaptureMetadataEnabled => Volatile.Read(ref _captureMetadataRequestCount) > 0;

    public static void BeginCaptureMetadata()
    {
        Interlocked.Increment(ref _captureMetadataRequestCount);
    }

    public static void EndCaptureMetadata()
    {
        while (true)
        {
            int current = Volatile.Read(ref _captureMetadataRequestCount);
            if (current <= 0)
                return;

            if (Interlocked.CompareExchange(ref _captureMetadataRequestCount, current - 1, current) == current)
                return;
        }
    }

    public static void ResetCaptureMetadata()
    {
        Volatile.Write(ref _captureMetadataRequestCount, 0);
    }
}
