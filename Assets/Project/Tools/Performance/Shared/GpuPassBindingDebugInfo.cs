using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public enum GpuPassBindingAccess
{
    Srv,
    Uav,
    Cbv
}

public static class GpuPassBindingDebugRegistry
{
    private sealed class PassRecord
    {
        public string passName;
        public readonly List<BindingRecord> srv = new List<BindingRecord>();
        public readonly List<BindingRecord> uav = new List<BindingRecord>();
        public readonly List<BindingRecord> cbv = new List<BindingRecord>();
    }

    private sealed class BindingRecord
    {
        public string passName;
        public string shaderName;
        public string runtimeName;
        public string resourceName;
        public string resourceType;
        public string access;
        public int count;
        public int strideBytes;
        public string accessPattern;
    }

    private sealed class ResourceRecord
    {
        public string name;
        public string runtimeName;
        public string resourceType;
        public int count;
        public int strideBytes;
        public readonly HashSet<string> usage = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> producerPasses = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> consumerPasses = new HashSet<string>(StringComparer.Ordinal);
        public string readAccessPattern;
        public string writeAccessPattern;
        public readonly HashSet<string> randomAccess = new HashSet<string>(StringComparer.Ordinal);
    }

    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<string, PassRecord> Passes =
        new Dictionary<string, PassRecord>(StringComparer.Ordinal);
    private static readonly Dictionary<string, BindingRecord> Bindings =
        new Dictionary<string, BindingRecord>(StringComparer.Ordinal);
    private static readonly Dictionary<string, ResourceRecord> Resources =
        new Dictionary<string, ResourceRecord>(StringComparer.Ordinal);

    public static void Clear()
    {
        lock (SyncRoot)
        {
            Passes.Clear();
            Bindings.Clear();
            Resources.Clear();
        }
    }

    public static void RecordBufferBinding(
        string passName,
        string shaderName,
        string runtimeName,
        string resourceType,
        GpuPassBindingAccess access,
        int count,
        int strideBytes,
        string accessPattern)
    {
        RecordBufferBinding(
            passName,
            shaderName,
            runtimeName,
            runtimeName,
            resourceType,
            access,
            count,
            strideBytes,
            accessPattern,
            string.Empty);
    }

    public static void RecordBufferBinding(
        string passName,
        string shaderName,
        string runtimeName,
        string resourceName,
        string resourceType,
        GpuPassBindingAccess access,
        int count,
        int strideBytes,
        string accessPattern,
        string randomAccess)
    {
        if (string.IsNullOrWhiteSpace(passName) || string.IsNullOrWhiteSpace(runtimeName))
            return;

        passName = passName.Trim();
        shaderName = NormalizeString(shaderName);
        runtimeName = runtimeName.Trim();
        resourceName = string.IsNullOrWhiteSpace(resourceName) ? runtimeName : resourceName.Trim();
        resourceType = NormalizeString(resourceType);
        string accessText = ToAccessString(access);
        string usageText = accessText.ToUpperInvariant();
        count = Math.Max(0, count);
        strideBytes = Math.Max(0, strideBytes);
        accessPattern = NormalizeString(accessPattern);
        randomAccess = NormalizeString(randomAccess);

        lock (SyncRoot)
        {
            PassRecord pass = GetOrCreatePass(passName);
            string bindingKey = passName + "\n" + accessText + "\n" + shaderName + "\n" + runtimeName;
            BindingRecord binding;
            if (!Bindings.TryGetValue(bindingKey, out binding))
            {
                binding = new BindingRecord();
                Bindings.Add(bindingKey, binding);
                AddBindingToPass(pass, accessText, binding);
            }

            binding.passName = passName;
            binding.shaderName = shaderName;
            binding.runtimeName = runtimeName;
            binding.resourceName = resourceName;
            binding.resourceType = resourceType;
            binding.access = accessText;
            binding.count = count;
            binding.strideBytes = strideBytes;
            binding.accessPattern = accessPattern;

            ResourceRecord resource = GetOrCreateResource(runtimeName);
            resource.name = resourceName;
            resource.runtimeName = runtimeName;
            resource.resourceType = resourceType;
            resource.count = count;
            resource.strideBytes = strideBytes;
            resource.usage.Add(usageText);

            if (access == GpuPassBindingAccess.Uav)
            {
                resource.producerPasses.Add(passName);
                if (string.IsNullOrEmpty(resource.writeAccessPattern))
                    resource.writeAccessPattern = accessPattern;
            }
            else
            {
                resource.consumerPasses.Add(passName);
                if (string.IsNullOrEmpty(resource.readAccessPattern))
                    resource.readAccessPattern = accessPattern;
            }

            if (!string.IsNullOrEmpty(randomAccess))
                resource.randomAccess.Add(randomAccess);
        }
    }

    public static string BuildJson(DateTime generatedAtUtc)
    {
        lock (SyncRoot)
        {
            string[] passNames = BuildSortedKeys(Passes);
            string[] resourceNames = BuildSortedKeys(Resources);

            StringBuilder builder = new StringBuilder(4096);
            builder.AppendLine("{");
            builder.AppendLine("  \"version\": 1,");
            builder.Append("  \"generated_at_utc\": \"");
            builder.Append(EscapeJson(generatedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
            builder.AppendLine("\",");
            builder.Append("  \"pass_count\": ");
            builder.Append(passNames.Length);
            builder.AppendLine(",");
            builder.Append("  \"resource_count\": ");
            builder.Append(resourceNames.Length);
            builder.AppendLine(",");
            builder.AppendLine("  \"passes\": {");
            for (int index = 0; index < passNames.Length; index++)
            {
                PassRecord pass = Passes[passNames[index]];
                AppendPassRecord(builder, pass);
                if (index + 1 < passNames.Length)
                    builder.Append(",");

                builder.AppendLine();
            }

            builder.AppendLine("  },");
            builder.AppendLine("  \"resources\": {");
            for (int index = 0; index < resourceNames.Length; index++)
            {
                ResourceRecord resource = Resources[resourceNames[index]];
                AppendResourceRecord(builder, resource);
                if (index + 1 < resourceNames.Length)
                    builder.Append(",");

                builder.AppendLine();
            }

            builder.AppendLine("  }");
            builder.AppendLine("}");
            return builder.ToString();
        }
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

    private static PassRecord GetOrCreatePass(string passName)
    {
        PassRecord pass;
        if (Passes.TryGetValue(passName, out pass))
            return pass;

        pass = new PassRecord
        {
            passName = passName
        };
        Passes.Add(passName, pass);
        return pass;
    }

    private static ResourceRecord GetOrCreateResource(string runtimeName)
    {
        ResourceRecord resource;
        if (Resources.TryGetValue(runtimeName, out resource))
            return resource;

        resource = new ResourceRecord
        {
            runtimeName = runtimeName,
            name = runtimeName
        };
        Resources.Add(runtimeName, resource);
        return resource;
    }

    private static void AddBindingToPass(PassRecord pass, string accessText, BindingRecord binding)
    {
        if (accessText == "uav")
        {
            pass.uav.Add(binding);
            return;
        }

        if (accessText == "cbv")
        {
            pass.cbv.Add(binding);
            return;
        }

        pass.srv.Add(binding);
    }

    private static void AppendPassRecord(StringBuilder builder, PassRecord pass)
    {
        builder.Append("    \"");
        builder.Append(EscapeJson(pass.passName));
        builder.AppendLine("\": {");
        AppendJsonString(builder, "pass_id", pass.passName, 6, true);
        builder.AppendLine("      \"bindings\": {");
        AppendBindingArray(builder, "srv", pass.srv, 8, true);
        AppendBindingArray(builder, "uav", pass.uav, 8, true);
        AppendBindingArray(builder, "cbv", pass.cbv, 8, false);
        builder.AppendLine("      }");
        builder.Append("    }");
    }

    private static void AppendBindingArray(
        StringBuilder builder,
        string key,
        List<BindingRecord> records,
        int indent,
        bool trailingComma)
    {
        records.Sort(CompareBindingRecords);
        builder.Append(' ', indent);
        builder.Append("\"");
        builder.Append(EscapeJson(key));
        builder.AppendLine("\": [");
        for (int index = 0; index < records.Count; index++)
        {
            BindingRecord record = records[index];
            builder.Append(' ', indent + 2);
            builder.AppendLine("{");
            AppendJsonString(builder, "shader_name", record.shaderName, indent + 4, true);
            AppendJsonString(builder, "runtime_name", record.runtimeName, indent + 4, true);
            AppendJsonString(builder, "resource_name", record.resourceName, indent + 4, true);
            AppendJsonString(builder, "type", record.resourceType, indent + 4, true);
            AppendJsonNumber(builder, "count", record.count, indent + 4, true);
            AppendJsonNumber(builder, "stride_bytes", record.strideBytes, indent + 4, true);
            AppendJsonNumber(builder, "size_mb", ComputeSizeMb(record.count, record.strideBytes), indent + 4, true);
            AppendJsonString(builder, "access_pattern", record.accessPattern, indent + 4, false);
            builder.Append(' ', indent + 2);
            builder.Append("}");
            if (index + 1 < records.Count)
                builder.Append(",");

            builder.AppendLine();
        }

        builder.Append(' ', indent);
        builder.Append("]");
        if (trailingComma)
            builder.Append(",");

        builder.AppendLine();
    }

    private static void AppendResourceRecord(StringBuilder builder, ResourceRecord resource)
    {
        builder.Append("    \"");
        builder.Append(EscapeJson(resource.runtimeName));
        builder.AppendLine("\": {");
        AppendJsonString(builder, "name", resource.name, 6, true);
        AppendJsonString(builder, "runtime_name", resource.runtimeName, 6, true);
        AppendJsonString(builder, "type", resource.resourceType, 6, true);
        AppendJsonNumber(builder, "stride_bytes", resource.strideBytes, 6, true);
        AppendJsonNumber(builder, "count", resource.count, 6, true);
        AppendJsonNumber(builder, "size_mb", ComputeSizeMb(resource.count, resource.strideBytes), 6, true);
        AppendJsonStringArray(builder, "usage", BuildSortedValues(resource.usage), 6, true);
        AppendJsonString(builder, "producer", FirstSortedValue(resource.producerPasses), 6, true);
        AppendJsonStringArray(builder, "producer_passes", BuildSortedValues(resource.producerPasses), 6, true);
        AppendJsonStringArray(builder, "consumer_passes", BuildSortedValues(resource.consumerPasses), 6, true);
        builder.AppendLine("      \"access_pattern\": {");
        AppendJsonString(builder, "read", resource.readAccessPattern, 8, true);
        AppendJsonString(builder, "write", resource.writeAccessPattern, 8, true);
        AppendJsonStringArray(builder, "random_access", BuildSortedValues(resource.randomAccess), 8, false);
        builder.AppendLine("      }");
        builder.Append("    }");
    }

    private static string[] BuildSortedKeys<TValue>(Dictionary<string, TValue> dictionary)
    {
        string[] keys = new string[dictionary.Count];
        dictionary.Keys.CopyTo(keys, 0);
        Array.Sort(keys, StringComparer.Ordinal);
        return keys;
    }

    private static string[] BuildSortedValues(HashSet<string> values)
    {
        string[] sorted = new string[values.Count];
        values.CopyTo(sorted);
        Array.Sort(sorted, StringComparer.Ordinal);
        return sorted;
    }

    private static string FirstSortedValue(HashSet<string> values)
    {
        string[] sorted = BuildSortedValues(values);
        return sorted.Length > 0 ? sorted[0] : string.Empty;
    }

    private static int CompareBindingRecords(BindingRecord left, BindingRecord right)
    {
        int shaderCompare = StringComparer.Ordinal.Compare(left.shaderName, right.shaderName);
        if (shaderCompare != 0)
            return shaderCompare;

        return StringComparer.Ordinal.Compare(left.runtimeName, right.runtimeName);
    }

    private static string ToAccessString(GpuPassBindingAccess access)
    {
        switch (access)
        {
            case GpuPassBindingAccess.Uav:
                return "uav";
            case GpuPassBindingAccess.Cbv:
                return "cbv";
            default:
                return "srv";
        }
    }

    private static double ComputeSizeMb(int count, int strideBytes)
    {
        return Math.Max(0.0, (double)count * Math.Max(0, strideBytes) / (1024.0 * 1024.0));
    }

    private static string NormalizeString(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
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

    private static void AppendJsonStringArray(
        StringBuilder builder,
        string key,
        string[] values,
        int indent,
        bool trailingComma)
    {
        builder.Append(' ', indent);
        builder.Append("\"");
        builder.Append(EscapeJson(key));
        builder.Append("\": [");
        for (int index = 0; index < values.Length; index++)
        {
            if (index > 0)
                builder.Append(", ");

            builder.Append("\"");
            builder.Append(EscapeJson(values[index]));
            builder.Append("\"");
        }

        builder.Append("]");
        if (trailingComma)
            builder.Append(",");

        builder.AppendLine();
    }

    private static void AppendJsonNumber(
        StringBuilder builder,
        string key,
        int value,
        int indent,
        bool trailingComma)
    {
        builder.Append(' ', indent);
        builder.Append("\"");
        builder.Append(EscapeJson(key));
        builder.Append("\": ");
        builder.Append(value);
        if (trailingComma)
            builder.Append(",");

        builder.AppendLine();
    }

    private static void AppendJsonNumber(
        StringBuilder builder,
        string key,
        double value,
        int indent,
        bool trailingComma)
    {
        builder.Append(' ', indent);
        builder.Append("\"");
        builder.Append(EscapeJson(key));
        builder.Append("\": ");
        builder.Append(value.ToString("0.######", CultureInfo.InvariantCulture));
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
