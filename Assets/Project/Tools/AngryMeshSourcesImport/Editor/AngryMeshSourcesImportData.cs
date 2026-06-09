using System;
using System.Collections.Generic;

namespace Project.Tools.AngryMeshSourcesImport
{
    [Serializable]
    public sealed class AngryMeshSourcesImportProfile
    {
        public int profileVersion = 1;
        public string sourceRoot = AngryMeshSourcesImportWorkflow.DefaultSourceRoot;
        public string statePath = AngryMeshSourcesImportWorkflow.DefaultStatePath;
        public bool autoApplyOnImport = true;
        public AngryMeshSourcesImportRule[] rules = Array.Empty<AngryMeshSourcesImportRule>();
    }

    [Serializable]
    public sealed class AngryMeshSourcesImportRule
    {
        public string id = string.Empty;
        public bool enabled = true;
        public int order;
        public string assetKind = "Any";
        public string[] pathGlobs = Array.Empty<string>();
        public string[] fileNameGlobs = Array.Empty<string>();
        public string pathRegex = string.Empty;
        public string fileNameRegex = string.Empty;
        public string[] excludeGlobs = Array.Empty<string>();
        public AngryMeshTextureImportSettings texture = new AngryMeshTextureImportSettings();
        public AngryMeshModelImportSettings model = new AngryMeshModelImportSettings();
        public AngryMeshMaterialSettings material = new AngryMeshMaterialSettings();
    }

    [Serializable]
    public sealed class AngryMeshTextureImportSettings
    {
        public bool apply;
        public string textureType = "Default";
        public bool sRGBTexture = true;
        public bool mipmapEnabled = true;
        public int maxTextureSize = 2048;
        public string textureCompression = "CompressedHQ";
        public bool alphaIsTransparency;
        public bool isReadable;
        public string wrapMode = "Repeat";
        public string filterMode = "Trilinear";
        public int anisoLevel = 4;
        public bool useCrunchedCompression;
        public int crunchedCompressionQuality = 75;
    }

    [Serializable]
    public sealed class AngryMeshModelImportSettings
    {
        public bool apply;
        public float globalScale = 1.0f;
        public bool isReadable;
        public bool importCameras;
        public bool importLights;
        public bool importBlendShapes;
        public bool generateColliders;
        public bool generateSecondaryUV;
        public bool optimizeMeshVertices = true;
        public bool optimizeMeshPolygons = true;
        public string meshCompression = "Off";
        public string animationType = "None";
        public string materialImportMode = "None";
    }

    [Serializable]
    public sealed class AngryMeshMaterialSettings
    {
        public bool apply;
        public bool enableInstancing = true;
        public bool doubleSidedGI;
        public int renderQueue = -1;
        public string shaderName = string.Empty;
    }

    [Serializable]
    public sealed class AngryMeshSourcesImportState
    {
        public int profileVersion = 1;
        public List<AngryMeshSourcesImportStateEntry> entries = new List<AngryMeshSourcesImportStateEntry>();
    }

    [Serializable]
    public sealed class AngryMeshSourcesImportStateEntry
    {
        public string assetPath = string.Empty;
        public string guid = string.Empty;
        public int profileVersion = 1;
        public string assetKind = string.Empty;
        public string ruleId = string.Empty;
        public long sourceLength;
        public long sourceLastWriteUtcTicks;
        public string settingsHash = string.Empty;
        public long appliedUtcTicks;
        public int applyCount;
    }

    public sealed class AngryMeshSourcesImportIssue
    {
        public string severity = "Info";
        public string assetPath = string.Empty;
        public string ruleId = string.Empty;
        public string message = string.Empty;
    }

    public sealed class AngryMeshSourcesImportReport
    {
        public int scannedCount;
        public int matchedCount;
        public int changedCount;
        public int reimportedCount;
        public readonly List<AngryMeshSourcesImportIssue> issues = new List<AngryMeshSourcesImportIssue>();

        public int ErrorCount
        {
            get
            {
                int count = 0;
                foreach (AngryMeshSourcesImportIssue issue in issues)
                {
                    if (string.Equals(issue.severity, "Error", StringComparison.OrdinalIgnoreCase))
                        count++;
                }

                return count;
            }
        }

        public int WarningCount
        {
            get
            {
                int count = 0;
                foreach (AngryMeshSourcesImportIssue issue in issues)
                {
                    if (string.Equals(issue.severity, "Warning", StringComparison.OrdinalIgnoreCase))
                        count++;
                }

                return count;
            }
        }
    }
}
