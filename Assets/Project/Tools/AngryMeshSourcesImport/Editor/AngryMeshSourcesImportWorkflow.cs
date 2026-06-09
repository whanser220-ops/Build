using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Project.Tools.AngryMeshSourcesImport
{
    public static class AngryMeshSourcesImportWorkflow
    {
        public const string DefaultSourceRoot = "Assets/GameResources";
        public const string DefaultProfilePath = "Assets/Project/Tools/AngryMeshSourcesImport/Profiles/angry_mesh_sources_import_profile.json";
        public const string DefaultStatePath = "Assets/Project/Tools/AngryMeshSourcesImport/Profiles/angry_mesh_sources_import_state.json";

        private static readonly Dictionary<string, Regex> GlobRegexCache = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);

        public static AngryMeshSourcesImportProfile LoadProfile()
        {
            EnsureDefaultFiles(false);

            if (!File.Exists(DefaultProfilePath))
                return CreateDefaultProfile();

            string json = File.ReadAllText(DefaultProfilePath, Encoding.UTF8);
            AngryMeshSourcesImportProfile profile = JsonUtility.FromJson<AngryMeshSourcesImportProfile>(json);
            return SanitizeProfile(profile);
        }

        public static void EnsureDefaultFiles(bool importAssets)
        {
            EnsureFolder(Path.GetDirectoryName(DefaultProfilePath));

            if (!File.Exists(DefaultProfilePath))
                File.WriteAllText(DefaultProfilePath, JsonUtility.ToJson(CreateDefaultProfile(), true), new UTF8Encoding(false));

            if (!File.Exists(DefaultStatePath))
                File.WriteAllText(DefaultStatePath, JsonUtility.ToJson(new AngryMeshSourcesImportState(), true), new UTF8Encoding(false));

            if (importAssets)
            {
                AssetDatabase.ImportAsset(DefaultProfilePath);
                AssetDatabase.ImportAsset(DefaultStatePath);
            }
        }

        public static AngryMeshSourcesImportReport CheckAll()
        {
            AngryMeshSourcesImportProfile profile = LoadProfile();
            AngryMeshSourcesImportState state = LoadState(profile);
            AngryMeshSourcesImportReport report = new AngryMeshSourcesImportReport();

            foreach (string assetPath in EnumerateSourceAssets(profile))
            {
                report.scannedCount++;
                CheckAsset(profile, state, assetPath, report);
            }

            return report;
        }

        public static AngryMeshSourcesImportReport ApplyAll(bool forceReimport)
        {
            AngryMeshSourcesImportProfile profile = LoadProfile();
            AngryMeshSourcesImportState state = LoadState(profile);
            AngryMeshSourcesImportReport report = new AngryMeshSourcesImportReport();

            foreach (string assetPath in EnumerateSourceAssets(profile))
            {
                report.scannedCount++;
                ApplyAsset(profile, state, assetPath, forceReimport, report);
            }

            PruneMissingEntries(state);
            SaveState(profile, state);
            AssetDatabase.SaveAssets();
            return report;
        }

        public static AngryMeshSourcesImportReport ApplyAssets(IReadOnlyList<string> assetPaths, bool forceReimport)
        {
            AngryMeshSourcesImportProfile profile = LoadProfile();
            AngryMeshSourcesImportState state = LoadState(profile);
            AngryMeshSourcesImportReport report = new AngryMeshSourcesImportReport();

            for (int i = 0; i < assetPaths.Count; i++)
            {
                string assetPath = NormalizeAssetPath(assetPaths[i]);
                if (!IsUnderSourceRoot(profile, assetPath))
                    continue;

                report.scannedCount++;
                ApplyAsset(profile, state, assetPath, forceReimport, report);
            }

            SaveState(profile, state);
            AssetDatabase.SaveAssets();
            return report;
        }

        public static bool IsUnderSourceRoot(AngryMeshSourcesImportProfile profile, string assetPath)
        {
            string normalizedRoot = NormalizeAssetPath(profile.sourceRoot).TrimEnd('/');
            string normalizedPath = NormalizeAssetPath(assetPath);
            return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildSummary(AngryMeshSourcesImportReport report)
        {
            return string.Format(
                "scanned={0}, matched={1}, changed={2}, reimported={3}, warnings={4}, errors={5}",
                report.scannedCount,
                report.matchedCount,
                report.changedCount,
                report.reimportedCount,
                report.WarningCount,
                report.ErrorCount);
        }

        private static void CheckAsset(
            AngryMeshSourcesImportProfile profile,
            AngryMeshSourcesImportState state,
            string assetPath,
            AngryMeshSourcesImportReport report)
        {
            string kind = DetermineAssetKind(assetPath);
            AngryMeshSourcesImportRule rule = FindRule(profile, assetPath, kind);
            if (rule == null)
            {
                AddIssue(report, "Error", assetPath, string.Empty, "No matching import rule.");
                return;
            }

            report.matchedCount++;
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null && !string.Equals(kind, "Material", StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(report, "Error", assetPath, rule.id, "AssetImporter not found.");
                return;
            }

            bool mismatched = CollectSettingsIssues(assetPath, kind, rule, report);
            AngryMeshSourcesImportStateEntry entry = FindStateEntry(state, assetPath);
            if (entry == null || IsStateOutdated(profile, state, assetPath, kind, rule, entry))
            {
                mismatched = true;
                AddIssue(report, "Info", assetPath, rule.id, "Persistent import state is missing or stale.");
            }

            if (mismatched)
                report.changedCount++;
        }

        private static void ApplyAsset(
            AngryMeshSourcesImportProfile profile,
            AngryMeshSourcesImportState state,
            string assetPath,
            bool forceReimport,
            AngryMeshSourcesImportReport report)
        {
            string kind = DetermineAssetKind(assetPath);
            AngryMeshSourcesImportRule rule = FindRule(profile, assetPath, kind);
            if (rule == null)
            {
                AddIssue(report, "Error", assetPath, string.Empty, "No matching import rule.");
                return;
            }

            report.matchedCount++;
            bool changed = ApplySettings(assetPath, kind, rule, report);
            AngryMeshSourcesImportStateEntry entry = FindStateEntry(state, assetPath);
            bool stateOutdated = entry == null || IsStateOutdated(profile, state, assetPath, kind, rule, entry);

            if (changed || stateOutdated || forceReimport)
            {
                report.changedCount++;
                UpdateStateEntry(profile, state, assetPath, kind, rule);
            }

            if ((changed || forceReimport) && !string.Equals(kind, "Material", StringComparison.OrdinalIgnoreCase))
            {
                AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                if (importer != null)
                {
                    importer.SaveAndReimport();
                    report.reimportedCount++;
                }
            }
        }

        private static bool CollectSettingsIssues(
            string assetPath,
            string kind,
            AngryMeshSourcesImportRule rule,
            AngryMeshSourcesImportReport report)
        {
            bool mismatch = false;

            if (string.Equals(kind, "Texture", StringComparison.OrdinalIgnoreCase) && rule.texture.apply)
            {
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null)
                    return true;

                CheckValue(importer.textureType, ParseEnum(rule.texture.textureType, TextureImporterType.Default), "textureType");
                CheckValue(importer.sRGBTexture, rule.texture.sRGBTexture, "sRGBTexture");
                CheckValue(importer.mipmapEnabled, rule.texture.mipmapEnabled, "mipmapEnabled");
                CheckValue(importer.maxTextureSize, rule.texture.maxTextureSize, "maxTextureSize");
                CheckValue(importer.textureCompression, ParseEnum(rule.texture.textureCompression, TextureImporterCompression.CompressedHQ), "textureCompression");
                CheckValue(importer.alphaIsTransparency, rule.texture.alphaIsTransparency, "alphaIsTransparency");
                CheckValue(importer.isReadable, rule.texture.isReadable, "isReadable");
                CheckValue(importer.wrapMode, ParseEnum(rule.texture.wrapMode, TextureWrapMode.Repeat), "wrapMode");
                CheckValue(importer.filterMode, ParseEnum(rule.texture.filterMode, FilterMode.Trilinear), "filterMode");
                CheckValue(importer.anisoLevel, Mathf.Clamp(rule.texture.anisoLevel, 0, 16), "anisoLevel");
                CheckValue(importer.crunchedCompression, rule.texture.useCrunchedCompression, "useCrunchedCompression");
                CheckValue(importer.compressionQuality, Mathf.Clamp(rule.texture.crunchedCompressionQuality, 0, 100), "crunchedCompressionQuality");
            }
            else if (string.Equals(kind, "Model", StringComparison.OrdinalIgnoreCase) && rule.model.apply)
            {
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null)
                    return true;

                CheckValue(importer.globalScale, rule.model.globalScale, "globalScale");
                CheckValue(importer.isReadable, rule.model.isReadable, "isReadable");
                CheckValue(importer.importCameras, rule.model.importCameras, "importCameras");
                CheckValue(importer.importLights, rule.model.importLights, "importLights");
                CheckValue(importer.importBlendShapes, rule.model.importBlendShapes, "importBlendShapes");
                CheckValue(importer.addCollider, rule.model.generateColliders, "generateColliders");
                CheckValue(importer.generateSecondaryUV, rule.model.generateSecondaryUV, "generateSecondaryUV");
                CheckValue(importer.optimizeMeshVertices, rule.model.optimizeMeshVertices, "optimizeMeshVertices");
                CheckValue(importer.optimizeMeshPolygons, rule.model.optimizeMeshPolygons, "optimizeMeshPolygons");
                CheckValue(importer.meshCompression, ParseEnum(rule.model.meshCompression, ModelImporterMeshCompression.Off), "meshCompression");
                CheckValue(importer.animationType, ParseEnum(rule.model.animationType, ModelImporterAnimationType.None), "animationType");
                CheckValue(importer.materialImportMode, ParseEnum(rule.model.materialImportMode, ModelImporterMaterialImportMode.None), "materialImportMode");
            }
            else if (string.Equals(kind, "Material", StringComparison.OrdinalIgnoreCase) && rule.material.apply)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (material == null)
                    return true;

                CheckValue(material.enableInstancing, rule.material.enableInstancing, "enableInstancing");
                CheckValue(material.doubleSidedGI, rule.material.doubleSidedGI, "doubleSidedGI");
                CheckValue(material.renderQueue, rule.material.renderQueue, "renderQueue");

                if (!string.IsNullOrWhiteSpace(rule.material.shaderName))
                {
                    string currentShader = material.shader != null ? material.shader.name : string.Empty;
                    CheckValue(currentShader, rule.material.shaderName, "shaderName");
                }
            }

            return mismatch;

            void CheckValue<T>(T current, T expected, string label)
            {
                if (EqualityComparer<T>.Default.Equals(current, expected))
                    return;

                mismatch = true;
                AddIssue(report, "Warning", assetPath, rule.id, label + " mismatch. current=" + current + ", expected=" + expected);
            }
        }

        private static bool ApplySettings(
            string assetPath,
            string kind,
            AngryMeshSourcesImportRule rule,
            AngryMeshSourcesImportReport report)
        {
            if (string.Equals(kind, "Texture", StringComparison.OrdinalIgnoreCase) && rule.texture.apply)
                return ApplyTextureSettings(assetPath, rule, report);

            if (string.Equals(kind, "Model", StringComparison.OrdinalIgnoreCase) && rule.model.apply)
                return ApplyModelSettings(assetPath, rule, report);

            if (string.Equals(kind, "Material", StringComparison.OrdinalIgnoreCase) && rule.material.apply)
                return ApplyMaterialSettings(assetPath, rule, report);

            return false;
        }

        private static bool ApplyTextureSettings(string assetPath, AngryMeshSourcesImportRule rule, AngryMeshSourcesImportReport report)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                AddIssue(report, "Error", assetPath, rule.id, "TextureImporter not found.");
                return false;
            }

            bool changed = false;
            SetValue(importer.textureType, ParseEnum(rule.texture.textureType, TextureImporterType.Default), value => importer.textureType = value);
            SetValue(importer.sRGBTexture, rule.texture.sRGBTexture, value => importer.sRGBTexture = value);
            SetValue(importer.mipmapEnabled, rule.texture.mipmapEnabled, value => importer.mipmapEnabled = value);
            SetValue(importer.maxTextureSize, rule.texture.maxTextureSize, value => importer.maxTextureSize = value);
            SetValue(importer.textureCompression, ParseEnum(rule.texture.textureCompression, TextureImporterCompression.CompressedHQ), value => importer.textureCompression = value);
            SetValue(importer.alphaIsTransparency, rule.texture.alphaIsTransparency, value => importer.alphaIsTransparency = value);
            SetValue(importer.isReadable, rule.texture.isReadable, value => importer.isReadable = value);
            SetValue(importer.wrapMode, ParseEnum(rule.texture.wrapMode, TextureWrapMode.Repeat), value => importer.wrapMode = value);
            SetValue(importer.filterMode, ParseEnum(rule.texture.filterMode, FilterMode.Trilinear), value => importer.filterMode = value);
            SetValue(importer.anisoLevel, Mathf.Clamp(rule.texture.anisoLevel, 0, 16), value => importer.anisoLevel = value);
            SetValue(importer.crunchedCompression, rule.texture.useCrunchedCompression, value => importer.crunchedCompression = value);
            SetValue(importer.compressionQuality, Mathf.Clamp(rule.texture.crunchedCompressionQuality, 0, 100), value => importer.compressionQuality = value);
            return changed;

            void SetValue<T>(T current, T expected, Action<T> setter)
            {
                if (EqualityComparer<T>.Default.Equals(current, expected))
                    return;

                setter(expected);
                changed = true;
            }
        }

        private static bool ApplyModelSettings(string assetPath, AngryMeshSourcesImportRule rule, AngryMeshSourcesImportReport report)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                AddIssue(report, "Error", assetPath, rule.id, "ModelImporter not found.");
                return false;
            }

            bool changed = false;
            SetValue(importer.globalScale, rule.model.globalScale, value => importer.globalScale = value);
            SetValue(importer.isReadable, rule.model.isReadable, value => importer.isReadable = value);
            SetValue(importer.importCameras, rule.model.importCameras, value => importer.importCameras = value);
            SetValue(importer.importLights, rule.model.importLights, value => importer.importLights = value);
            SetValue(importer.importBlendShapes, rule.model.importBlendShapes, value => importer.importBlendShapes = value);
            SetValue(importer.addCollider, rule.model.generateColliders, value => importer.addCollider = value);
            SetValue(importer.generateSecondaryUV, rule.model.generateSecondaryUV, value => importer.generateSecondaryUV = value);
            SetValue(importer.optimizeMeshVertices, rule.model.optimizeMeshVertices, value => importer.optimizeMeshVertices = value);
            SetValue(importer.optimizeMeshPolygons, rule.model.optimizeMeshPolygons, value => importer.optimizeMeshPolygons = value);
            SetValue(importer.meshCompression, ParseEnum(rule.model.meshCompression, ModelImporterMeshCompression.Off), value => importer.meshCompression = value);
            SetValue(importer.animationType, ParseEnum(rule.model.animationType, ModelImporterAnimationType.None), value => importer.animationType = value);
            SetValue(importer.materialImportMode, ParseEnum(rule.model.materialImportMode, ModelImporterMaterialImportMode.None), value => importer.materialImportMode = value);
            return changed;

            void SetValue<T>(T current, T expected, Action<T> setter)
            {
                if (EqualityComparer<T>.Default.Equals(current, expected))
                    return;

                setter(expected);
                changed = true;
            }
        }

        private static bool ApplyMaterialSettings(string assetPath, AngryMeshSourcesImportRule rule, AngryMeshSourcesImportReport report)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                AddIssue(report, "Error", assetPath, rule.id, "Material asset not found.");
                return false;
            }

            bool changed = false;
            SetValue(material.enableInstancing, rule.material.enableInstancing, value => material.enableInstancing = value);
            SetValue(material.doubleSidedGI, rule.material.doubleSidedGI, value => material.doubleSidedGI = value);
            SetValue(material.renderQueue, rule.material.renderQueue, value => material.renderQueue = value);

            if (!string.IsNullOrWhiteSpace(rule.material.shaderName))
            {
                Shader shader = Shader.Find(rule.material.shaderName);
                if (shader == null)
                {
                    AddIssue(report, "Warning", assetPath, rule.id, "Shader not found: " + rule.material.shaderName);
                }
                else if (material.shader != shader)
                {
                    material.shader = shader;
                    changed = true;
                }
            }

            if (changed)
                EditorUtility.SetDirty(material);

            return changed;

            void SetValue<T>(T current, T expected, Action<T> setter)
            {
                if (EqualityComparer<T>.Default.Equals(current, expected))
                    return;

                setter(expected);
                changed = true;
            }
        }

        private static AngryMeshSourcesImportRule FindRule(AngryMeshSourcesImportProfile profile, string assetPath, string assetKind)
        {
            AngryMeshSourcesImportRule bestRule = null;
            int bestOrder = int.MaxValue;
            string relativePath = GetRelativeSourcePath(profile, assetPath);
            string fileName = Path.GetFileName(assetPath);

            AngryMeshSourcesImportRule[] rules = profile.rules ?? Array.Empty<AngryMeshSourcesImportRule>();
            for (int i = 0; i < rules.Length; i++)
            {
                AngryMeshSourcesImportRule rule = rules[i];
                if (rule == null || !rule.enabled)
                    continue;

                if (!MatchesKind(rule.assetKind, assetKind))
                    continue;

                if (!MatchesAnyGlob(rule.pathGlobs, relativePath))
                    continue;

                if (!MatchesAnyGlob(rule.fileNameGlobs, fileName))
                    continue;

                if (!MatchesRegex(rule.pathRegex, relativePath))
                    continue;

                if (!MatchesRegex(rule.fileNameRegex, fileName))
                    continue;

                if (MatchesAnyExcludeGlob(rule.excludeGlobs, relativePath))
                    continue;

                if (rule.order < bestOrder)
                {
                    bestRule = rule;
                    bestOrder = rule.order;
                }
            }

            return bestRule;
        }

        private static IEnumerable<string> EnumerateSourceAssets(AngryMeshSourcesImportProfile profile)
        {
            string root = NormalizeAssetPath(profile.sourceRoot);
            if (!Directory.Exists(root))
                yield break;

            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string assetPath = NormalizeAssetPath(file);
                if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return assetPath;
            }
        }

        private static string DetermineAssetKind(string assetPath)
        {
            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            switch (extension)
            {
                case ".tif":
                case ".tiff":
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd":
                case ".hdr":
                case ".exr":
                    return "Texture";
                case ".fbx":
                case ".obj":
                case ".blend":
                    return "Model";
                case ".mat":
                    return "Material";
                case ".terrainlayer":
                    return "TerrainLayer";
                default:
                    return "Generic";
            }
        }

        private static string GetRelativeSourcePath(AngryMeshSourcesImportProfile profile, string assetPath)
        {
            string root = NormalizeAssetPath(profile.sourceRoot).TrimEnd('/');
            string path = NormalizeAssetPath(assetPath);
            if (path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return path.Substring(root.Length + 1);

            return path;
        }

        private static bool MatchesKind(string ruleKind, string assetKind)
        {
            return string.IsNullOrWhiteSpace(ruleKind)
                || string.Equals(ruleKind, "Any", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ruleKind, assetKind, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesAnyGlob(string[] patterns, string value)
        {
            if (patterns == null || patterns.Length == 0)
                return true;

            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = patterns[i];
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                if (GetGlobRegex(pattern).IsMatch(value))
                    return true;
            }

            return false;
        }

        private static bool MatchesAnyExcludeGlob(string[] patterns, string value)
        {
            if (patterns == null || patterns.Length == 0)
                return false;

            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = patterns[i];
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                if (GetGlobRegex(pattern).IsMatch(value))
                    return true;
            }

            return false;
        }

        private static bool MatchesRegex(string pattern, string value)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return true;

            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static Regex GetGlobRegex(string glob)
        {
            string normalized = NormalizeAssetPath(glob);
            if (GlobRegexCache.TryGetValue(normalized, out Regex regex))
                return regex;

            StringBuilder builder = new StringBuilder();
            builder.Append("^");
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (c == '*')
                {
                    bool isDouble = i + 1 < normalized.Length && normalized[i + 1] == '*';
                    if (isDouble)
                    {
                        builder.Append(".*");
                        i++;
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    builder.Append("[^/]");
                }
                else
                {
                    builder.Append(Regex.Escape(c.ToString()));
                }
            }

            builder.Append("$");
            regex = new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            GlobRegexCache[normalized] = regex;
            return regex;
        }

        private static AngryMeshSourcesImportProfile SanitizeProfile(AngryMeshSourcesImportProfile profile)
        {
            if (profile == null)
                profile = CreateDefaultProfile();

            if (string.IsNullOrWhiteSpace(profile.sourceRoot))
                profile.sourceRoot = DefaultSourceRoot;

            if (string.IsNullOrWhiteSpace(profile.statePath))
                profile.statePath = DefaultStatePath;

            if (profile.rules == null)
                profile.rules = Array.Empty<AngryMeshSourcesImportRule>();

            for (int i = 0; i < profile.rules.Length; i++)
            {
                AngryMeshSourcesImportRule rule = profile.rules[i];
                if (rule == null)
                    continue;

                if (rule.pathGlobs == null)
                    rule.pathGlobs = Array.Empty<string>();

                if (rule.fileNameGlobs == null)
                    rule.fileNameGlobs = Array.Empty<string>();

                if (rule.excludeGlobs == null)
                    rule.excludeGlobs = Array.Empty<string>();

                if (rule.texture == null)
                    rule.texture = new AngryMeshTextureImportSettings();

                if (rule.model == null)
                    rule.model = new AngryMeshModelImportSettings();

                if (rule.material == null)
                    rule.material = new AngryMeshMaterialSettings();
            }

            return profile;
        }

        private static AngryMeshSourcesImportState LoadState(AngryMeshSourcesImportProfile profile)
        {
            if (!File.Exists(profile.statePath))
                return new AngryMeshSourcesImportState { profileVersion = profile.profileVersion };

            string json = File.ReadAllText(profile.statePath, Encoding.UTF8);
            AngryMeshSourcesImportState state = JsonUtility.FromJson<AngryMeshSourcesImportState>(json);
            if (state == null)
                state = new AngryMeshSourcesImportState();

            if (state.entries == null)
                state.entries = new List<AngryMeshSourcesImportStateEntry>();

            state.profileVersion = profile.profileVersion;
            return state;
        }

        private static void SaveState(AngryMeshSourcesImportProfile profile, AngryMeshSourcesImportState state)
        {
            EnsureFolder(Path.GetDirectoryName(profile.statePath));
            state.profileVersion = profile.profileVersion;
            File.WriteAllText(profile.statePath, JsonUtility.ToJson(state, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(profile.statePath);
        }

        private static bool IsStateOutdated(
            AngryMeshSourcesImportProfile profile,
            AngryMeshSourcesImportState state,
            string assetPath,
            string kind,
            AngryMeshSourcesImportRule rule,
            AngryMeshSourcesImportStateEntry entry)
        {
            FileInfo fileInfo = new FileInfo(assetPath);
            if (!fileInfo.Exists)
                return true;

            return entry.profileVersion != profile.profileVersion
                || !string.Equals(entry.guid, AssetDatabase.AssetPathToGUID(assetPath), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entry.assetKind, kind, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(entry.ruleId, rule.id, StringComparison.Ordinal)
                || entry.sourceLength != fileInfo.Length
                || entry.sourceLastWriteUtcTicks != fileInfo.LastWriteTimeUtc.Ticks
                || !string.Equals(entry.settingsHash, BuildSettingsHash(profile, rule), StringComparison.Ordinal);
        }

        private static void UpdateStateEntry(
            AngryMeshSourcesImportProfile profile,
            AngryMeshSourcesImportState state,
            string assetPath,
            string kind,
            AngryMeshSourcesImportRule rule)
        {
            FileInfo fileInfo = new FileInfo(assetPath);
            AngryMeshSourcesImportStateEntry entry = FindStateEntry(state, assetPath);
            if (entry == null)
            {
                entry = new AngryMeshSourcesImportStateEntry();
                state.entries.Add(entry);
            }

            entry.assetPath = NormalizeAssetPath(assetPath);
            entry.guid = AssetDatabase.AssetPathToGUID(assetPath);
            entry.profileVersion = profile.profileVersion;
            entry.assetKind = kind;
            entry.ruleId = rule.id;
            entry.sourceLength = fileInfo.Exists ? fileInfo.Length : 0;
            entry.sourceLastWriteUtcTicks = fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks : 0;
            entry.settingsHash = BuildSettingsHash(profile, rule);
            entry.appliedUtcTicks = DateTime.UtcNow.Ticks;
            entry.applyCount++;
        }

        private static AngryMeshSourcesImportStateEntry FindStateEntry(AngryMeshSourcesImportState state, string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            for (int i = 0; i < state.entries.Count; i++)
            {
                AngryMeshSourcesImportStateEntry entry = state.entries[i];
                if (entry != null && string.Equals(entry.assetPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        private static void PruneMissingEntries(AngryMeshSourcesImportState state)
        {
            state.entries.RemoveAll(entry => entry == null || string.IsNullOrWhiteSpace(entry.assetPath) || !File.Exists(entry.assetPath));
        }

        private static string BuildSettingsHash(AngryMeshSourcesImportProfile profile, AngryMeshSourcesImportRule rule)
        {
            string text = profile.profileVersion + "|" + JsonUtility.ToJson(rule);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }

        private static void AddIssue(AngryMeshSourcesImportReport report, string severity, string assetPath, string ruleId, string message)
        {
            report.issues.Add(new AngryMeshSourcesImportIssue
            {
                severity = severity,
                assetPath = assetPath,
                ruleId = ruleId,
                message = message
            });
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || Directory.Exists(folder))
                return;

            Directory.CreateDirectory(folder);
        }

        public static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim();
        }

        private static AngryMeshSourcesImportProfile CreateDefaultProfile()
        {
            return new AngryMeshSourcesImportProfile
            {
                profileVersion = 1,
                sourceRoot = DefaultSourceRoot,
                statePath = DefaultStatePath,
                autoApplyOnImport = true,
                rules = new[]
                {
                    new AngryMeshSourcesImportRule
                    {
                        id = "texture-skybox-hdr",
                        enabled = true,
                        order = 10,
                        assetKind = "Texture",
                        pathGlobs = new[] { "*/Sources/Textures/Skybox/**" },
                        fileNameGlobs = new[] { "*.HDR", "*.hdr" },
                        texture = new AngryMeshTextureImportSettings
                        {
                            apply = true,
                            textureType = "Default",
                            sRGBTexture = true,
                            mipmapEnabled = true,
                            maxTextureSize = 4096,
                            textureCompression = "CompressedHQ",
                            wrapMode = "Clamp",
                            filterMode = "Trilinear",
                            anisoLevel = 1
                        }
                    },
                    new AngryMeshSourcesImportRule
                    {
                        id = "texture-normal",
                        enabled = true,
                        order = 20,
                        assetKind = "Texture",
                        pathGlobs = new[] { "*/Sources/Textures/**" },
                        fileNameGlobs = new[] { "*_N.tif", "*_Normal.tif" },
                        texture = new AngryMeshTextureImportSettings
                        {
                            apply = true,
                            textureType = "NormalMap",
                            sRGBTexture = false,
                            mipmapEnabled = true,
                            maxTextureSize = 2048,
                            textureCompression = "CompressedHQ",
                            wrapMode = "Repeat",
                            filterMode = "Trilinear",
                            anisoLevel = 8
                        }
                    },
                    new AngryMeshSourcesImportRule
                    {
                        id = "texture-linear-mask",
                        enabled = true,
                        order = 30,
                        assetKind = "Texture",
                        pathGlobs = new[] { "*/Sources/Textures/**" },
                        fileNameGlobs = new[] { "*_SMA.tif", "*_Mask.tif", "*_H.tif", "*_O.tif" },
                        texture = new AngryMeshTextureImportSettings
                        {
                            apply = true,
                            textureType = "Default",
                            sRGBTexture = false,
                            mipmapEnabled = true,
                            maxTextureSize = 2048,
                            textureCompression = "CompressedHQ",
                            wrapMode = "Repeat",
                            filterMode = "Trilinear",
                            anisoLevel = 4
                        }
                    },
                    new AngryMeshSourcesImportRule
                    {
                        id = "texture-color-default",
                        enabled = true,
                        order = 40,
                        assetKind = "Texture",
                        pathGlobs = new[] { "*/Sources/Textures/**" },
                        fileNameGlobs = new[] { "*.tif" },
                        texture = new AngryMeshTextureImportSettings
                        {
                            apply = true,
                            textureType = "Default",
                            sRGBTexture = true,
                            mipmapEnabled = true,
                            maxTextureSize = 2048,
                            textureCompression = "CompressedHQ",
                            wrapMode = "Repeat",
                            filterMode = "Trilinear",
                            anisoLevel = 4
                        }
                    },
                    new AngryMeshSourcesImportRule
                    {
                        id = "model-foliage-card",
                        enabled = true,
                        order = 100,
                        assetKind = "Model",
                        pathGlobs = new[] { "*/Sources/Meshes/Grass/**", "*/Sources/Meshes/Flowers/**", "*/Sources/Meshes/Leaves/**", "*/Sources/Meshes/Plants/**", "*/Sources/Meshes/VFX/**" },
                        fileNameGlobs = new[] { "*.fbx" },
                        model = new AngryMeshModelImportSettings
                        {
                            apply = true,
                            globalScale = 1.0f,
                            isReadable = false,
                            importCameras = false,
                            importLights = false,
                            importBlendShapes = false,
                            generateColliders = false,
                            generateSecondaryUV = false,
                            optimizeMeshVertices = true,
                            optimizeMeshPolygons = true,
                            meshCompression = "Low",
                            animationType = "None",
                            materialImportMode = "None"
                        }
                    },
                    new AngryMeshSourcesImportRule
                    {
                        id = "model-static-default",
                        enabled = true,
                        order = 110,
                        assetKind = "Model",
                        pathGlobs = new[] { "*/Sources/Meshes/**" },
                        fileNameGlobs = new[] { "*.fbx" },
                        model = new AngryMeshModelImportSettings
                        {
                            apply = true,
                            globalScale = 1.0f,
                            isReadable = false,
                            importCameras = false,
                            importLights = false,
                            importBlendShapes = false,
                            generateColliders = false,
                            generateSecondaryUV = false,
                            optimizeMeshVertices = true,
                            optimizeMeshPolygons = true,
                            meshCompression = "Off",
                            animationType = "None",
                            materialImportMode = "None"
                        }
                    },
                    new AngryMeshSourcesImportRule
                    {
                        id = "material-default",
                        enabled = true,
                        order = 200,
                        assetKind = "Material",
                        pathGlobs = new[] { "*/Sources/Materials/**" },
                        fileNameGlobs = new[] { "*.mat" },
                        material = new AngryMeshMaterialSettings
                        {
                            apply = true,
                            enableInstancing = true,
                            doubleSidedGI = false,
                            renderQueue = -1,
                            shaderName = string.Empty
                        }
                    },
                    new AngryMeshSourcesImportRule
                    {
                        id = "terrain-layer-track-only",
                        enabled = true,
                        order = 300,
                        assetKind = "TerrainLayer",
                        pathGlobs = new[] { "*/Sources/Terrain Layers/**" },
                        fileNameGlobs = new[] { "*.terrainlayer" }
                    },
                    new AngryMeshSourcesImportRule
                    {
                        id = "terrain-data-track-only",
                        enabled = true,
                        order = 310,
                        assetKind = "Generic",
                        pathGlobs = new[] { "*/Sources/Terrain Data/**" },
                        fileNameGlobs = new[] { "*.asset" }
                    }
                }
            };
        }
    }
}
