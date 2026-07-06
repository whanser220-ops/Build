using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class ProjectAssetImportRuleSetWindow : EditorWindow
{
    private const string MenuPath = "Tools/Asset Import/Asset Processor";
    private const float LabelWidth = 178f;
    private const float RowHeight = 22f;
    private const float SmallButtonWidth = 26f;

    private static readonly ImportSettingDefinition[] ModelSettings =
    {
        ImportSettingDefinition.Section("Scene"),
        ImportSettingDefinition.Float("ModelImporter.globalScale", "Scale Factor", "1"),
        ImportSettingDefinition.Bool("ModelImporter.useFileScale", "Use File Scale", "true"),
        ImportSettingDefinition.Bool("ModelImporter.useFileUnits", "Convert Units", "true"),
        ImportSettingDefinition.Bool("ModelImporter.bakeAxisConversion", "Bake Axis Conversion", "false"),
        ImportSettingDefinition.Bool("ModelImporter.importBlendShapes", "Import BlendShapes", "false"),
        ImportSettingDefinition.Enum<ModelImporterNormals>("ModelImporter.importBlendShapeNormals", "Blend Shape Normals", "None"),
        ImportSettingDefinition.Bool("ModelImporter.importBlendShapeDeformPercent", "Blend Shape Deform Percent", "false"),
        ImportSettingDefinition.Bool("ModelImporter.importVisibility", "Import Visibility", "true"),
        ImportSettingDefinition.Bool("ModelImporter.importCameras", "Import Cameras", "false"),
        ImportSettingDefinition.Bool("ModelImporter.importLights", "Import Lights", "false"),
        ImportSettingDefinition.Bool("ModelImporter.preserveHierarchy", "Preserve Hierarchy", "false"),
        ImportSettingDefinition.Bool("ModelImporter.sortHierarchyByName", "Sort Hierarchy By Name", "true"),
        ImportSettingDefinition.String("ModelImporter.extraUserProperties", "Extra User Properties", string.Empty),
        ImportSettingDefinition.Section("Meshes"),
        ImportSettingDefinition.Enum<ModelImporterMeshCompression>("ModelImporter.meshCompression", "Mesh Compression", "Low"),
        ImportSettingDefinition.Bool("ModelImporter.isReadable", "Read/Write", "false"),
        ImportSettingDefinition.Bool("ModelImporter.optimizeMesh", "Optimize Mesh", "true"),
        ImportSettingDefinition.Bool("ModelImporter.optimizeMeshPolygons", "Optimize Polygon Order", "true"),
        ImportSettingDefinition.Bool("ModelImporter.optimizeMeshVertices", "Optimize Vertex Order", "true"),
        ImportSettingDefinition.Enum<MeshOptimizationFlags>("ModelImporter.meshOptimizationFlags", "Mesh Optimization Flags", "Everything"),
        ImportSettingDefinition.Bool("ModelImporter.addCollider", "Generate Colliders", "false"),
        ImportSettingDefinition.Bool("ModelImporter.keepQuads", "Keep Quads", "false"),
        ImportSettingDefinition.Bool("ModelImporter.weldVertices", "Weld Vertices", "true"),
        ImportSettingDefinition.Enum<ModelImporterIndexFormat>("ModelImporter.indexFormat", "Index Format", "Auto"),
        ImportSettingDefinition.Enum<ModelImporterNormals>("ModelImporter.importNormals", "Normals", "Import"),
        ImportSettingDefinition.Enum<ModelImporterTangentSpaceMode>("ModelImporter.normalImportMode", "Normal Import Mode", "Import"),
        ImportSettingDefinition.Enum<ModelImporterNormalCalculationMode>("ModelImporter.normalCalculationMode", "Normal Calculation Mode", "AreaAndAngleWeighted"),
        ImportSettingDefinition.Enum<ModelImporterNormalSmoothingSource>("ModelImporter.normalSmoothingSource", "Smoothing Source", "PreferSmoothingGroups"),
        ImportSettingDefinition.Float("ModelImporter.normalSmoothingAngle", "Smoothing Angle", "60"),
        ImportSettingDefinition.Enum<ModelImporterTangents>("ModelImporter.importTangents", "Tangents", "CalculateMikk"),
        ImportSettingDefinition.Enum<ModelImporterTangentSpaceMode>("ModelImporter.tangentImportMode", "Tangent Import Mode", "Calculate"),
        ImportSettingDefinition.Bool("ModelImporter.splitTangentsAcrossSeams", "Split Tangents Across Seams", "false"),
        ImportSettingDefinition.Bool("ModelImporter.swapUVChannels", "Swap UVs", "false"),
        ImportSettingDefinition.Bool("ModelImporter.strictVertexDataChecks", "Strict Vertex Data Checks", "false"),
        ImportSettingDefinition.Section("Lightmap UVs"),
        ImportSettingDefinition.Bool("ModelImporter.generateSecondaryUV", "Generate Lightmap UVs", "false"),
        ImportSettingDefinition.Enum<ModelImporterSecondaryUVMarginMethod>("ModelImporter.secondaryUVMarginMethod", "Margin Method", "Calculate"),
        ImportSettingDefinition.Float("ModelImporter.secondaryUVHardAngle", "Hard Angle", "88"),
        ImportSettingDefinition.Float("ModelImporter.secondaryUVPackMargin", "Pack Margin", "4"),
        ImportSettingDefinition.Float("ModelImporter.secondaryUVAngleDistortion", "Angle Error", "8"),
        ImportSettingDefinition.Float("ModelImporter.secondaryUVAreaDistortion", "Area Error", "15"),
        ImportSettingDefinition.Float("ModelImporter.secondaryUVMinLightmapResolution", "Min Lightmap Resolution", "40"),
        ImportSettingDefinition.Float("ModelImporter.secondaryUVMinObjectScale", "Min Object Scale", "1")
    };

    private static readonly ImportSettingDefinition[] RigSettings =
    {
        ImportSettingDefinition.Enum<ModelImporterAnimationType>("ModelImporter.animationType", "Animation Type", "None"),
        ImportSettingDefinition.Enum<ModelImporterAvatarSetup>("ModelImporter.avatarSetup", "Avatar Definition", "CreateFromThisModel"),
        ImportSettingDefinition.Enum<ModelImporterSkinWeights>("ModelImporter.skinWeights", "Skin Weights", "Standard"),
        ImportSettingDefinition.Int("ModelImporter.maxBonesPerVertex", "Max Bones/Vertex", "4"),
        ImportSettingDefinition.Float("ModelImporter.minBoneWeight", "Min Bone Weight", "0.001"),
        ImportSettingDefinition.Bool("ModelImporter.optimizeGameObjects", "Optimize Game Objects", "false"),
        ImportSettingDefinition.Bool("ModelImporter.optimizeBones", "Optimize Bones", "true"),
        ImportSettingDefinition.Bool("ModelImporter.autoGenerateAvatarMappingIfUnspecified", "Auto Generate Avatar Mapping", "true"),
        ImportSettingDefinition.Enum<ModelImporterHumanoidOversampling>("ModelImporter.humanoidOversampling", "Humanoid Oversampling", "X1"),
        ImportSettingDefinition.Bool("ModelImporter.bakeIK", "Bake IK", "false")
    };

    private static readonly ImportSettingDefinition[] AnimationSettings =
    {
        ImportSettingDefinition.Bool("ModelImporter.importAnimation", "Import Animation", "false"),
        ImportSettingDefinition.Enum<ModelImporterAnimationCompression>("ModelImporter.animationCompression", "Anim. Compression", "Optimal"),
        ImportSettingDefinition.Float("ModelImporter.animationRotationError", "Rotation Error", "0.5"),
        ImportSettingDefinition.Float("ModelImporter.animationPositionError", "Position Error", "0.5"),
        ImportSettingDefinition.Float("ModelImporter.animationScaleError", "Scale Error", "0.5"),
        ImportSettingDefinition.Enum<WrapMode>("ModelImporter.animationWrapMode", "Wrap Mode", "Default"),
        ImportSettingDefinition.Enum<ModelImporterGenerateAnimations>("ModelImporter.generateAnimations", "Generate Animations", "None"),
        ImportSettingDefinition.Bool("ModelImporter.importConstraints", "Import Constraints", "false"),
        ImportSettingDefinition.Bool("ModelImporter.importAnimatedCustomProperties", "Import Animated Custom Properties", "false"),
        ImportSettingDefinition.Bool("ModelImporter.resampleCurves", "Resample Curves", "true"),
        ImportSettingDefinition.Bool("ModelImporter.resampleRotations", "Resample Rotations", "true"),
        ImportSettingDefinition.Bool("ModelImporter.removeConstantScaleCurves", "Remove Constant Scale Curves", "true"),
        ImportSettingDefinition.Bool("ModelImporter.splitAnimations", "Split Animations", "false"),
        ImportSettingDefinition.String("ModelImporter.motionNodeName", "Motion Node", string.Empty),
        ImportSettingDefinition.Bool("ModelImporter.clipNameFromAsset", "Clip Name From Asset", "false")
    };

    private static readonly ImportSettingDefinition[] MaterialSettings =
    {
        ImportSettingDefinition.Enum<ModelImporterMaterialImportMode>("ModelImporter.materialImportMode", "Material Creation Mode", "ImportStandard"),
        ImportSettingDefinition.Enum<ModelImporterMaterialLocation>("ModelImporter.materialLocation", "Location", "InPrefab"),
        ImportSettingDefinition.Enum<ModelImporterMaterialName>("ModelImporter.materialName", "Naming", "BasedOnMaterialName"),
        ImportSettingDefinition.Enum<ModelImporterMaterialSearch>("ModelImporter.materialSearch", "Search", "Local"),
        ImportSettingDefinition.Bool("ModelImporter.useSRGBMaterialColor", "Use sRGB Material Color", "true")
    };

    private static readonly ImportSettingDefinition[] TextureSettings =
    {
        ImportSettingDefinition.Section("Texture"),
        ImportSettingDefinition.Enum<TextureImporterType>("TextureImporter.textureType", "Texture Type", "Default"),
        ImportSettingDefinition.Enum<TextureImporterShape>("TextureImporter.textureShape", "Texture Shape", "Texture2D"),
        ImportSettingDefinition.Bool("TextureImporter.sRGBTexture", "sRGB", "true"),
        ImportSettingDefinition.Enum<TextureImporterAlphaSource>("TextureImporter.alphaSource", "Alpha Source", "FromInput"),
        ImportSettingDefinition.Bool("TextureImporter.alphaIsTransparency", "Alpha Is Transparency", "false"),
        ImportSettingDefinition.Bool("TextureImporter.ignorePngGamma", "Ignore PNG Gamma", "false"),
        ImportSettingDefinition.Bool("TextureImporter.isReadable", "Read/Write", "false"),
        ImportSettingDefinition.Bool("TextureImporter.vtOnly", "Virtual Texture Only", "false"),
        ImportSettingDefinition.Section("Compression"),
        ImportSettingDefinition.Int("TextureImporter.maxTextureSize", "Max Size", "2048"),
        ImportSettingDefinition.Enum<TextureImporterCompression>("TextureImporter.textureCompression", "Compression", "Compressed"),
        ImportSettingDefinition.Int("TextureImporter.compressionQuality", "Compression Quality", "50"),
        ImportSettingDefinition.Bool("TextureImporter.crunchedCompression", "Use Crunch Compression", "false"),
        ImportSettingDefinition.Enum<TextureImporterFormat>("TextureImporter.textureFormat", "Format", "Automatic"),
        ImportSettingDefinition.Enum<AndroidETC2FallbackOverride>("TextureImporter.androidETC2FallbackOverride", "ETC2 Fallback", "UseBuildSettings"),
        ImportSettingDefinition.Bool("TextureImporter.allowAlphaSplitting", "Allow Alpha Splitting", "false")
    };

    private static readonly ImportSettingDefinition[] TextureAdvancedSettings =
    {
        ImportSettingDefinition.Section("Mip Maps"),
        ImportSettingDefinition.Bool("TextureImporter.mipmapEnabled", "Generate Mip Maps", "true"),
        ImportSettingDefinition.Bool("TextureImporter.borderMipmap", "Border Mip Maps", "false"),
        ImportSettingDefinition.Enum<TextureImporterMipFilter>("TextureImporter.mipmapFilter", "Mip Map Filtering", "BoxFilter"),
        ImportSettingDefinition.Float("TextureImporter.mipMapBias", "Mip Map Bias", "0"),
        ImportSettingDefinition.Bool("TextureImporter.mipMapsPreserveCoverage", "Preserve Coverage", "false"),
        ImportSettingDefinition.Float("TextureImporter.alphaTestReferenceValue", "Alpha Cutoff", "0.5"),
        ImportSettingDefinition.Bool("TextureImporter.fadeout", "Fadeout Mip Maps", "false"),
        ImportSettingDefinition.Int("TextureImporter.mipmapFadeDistanceStart", "Fade Range Start", "1"),
        ImportSettingDefinition.Int("TextureImporter.mipmapFadeDistanceEnd", "Fade Range End", "3"),
        ImportSettingDefinition.Bool("TextureImporter.generateMipsInLinearSpace", "Generate Mips In Linear Space", "false"),
        ImportSettingDefinition.String("TextureImporter.mipmapLimitGroupName", "Mipmap Limit Group", string.Empty),
        ImportSettingDefinition.Bool("TextureImporter.ignoreMipmapLimit", "Ignore Mipmap Limit", "false"),
        ImportSettingDefinition.Bool("TextureImporter.streamingMipmaps", "Streaming Mip Maps", "false"),
        ImportSettingDefinition.Int("TextureImporter.streamingMipmapsPriority", "Streaming Priority", "0"),
        ImportSettingDefinition.Section("Normal Map"),
        ImportSettingDefinition.Bool("TextureImporter.convertToNormalmap", "Create From Grayscale", "false"),
        ImportSettingDefinition.Float("TextureImporter.heightmapScale", "Bumpiness", "0.25"),
        ImportSettingDefinition.Enum<TextureImporterNormalFilter>("TextureImporter.normalmapFilter", "Filtering", "Standard"),
        ImportSettingDefinition.Bool("TextureImporter.flipGreenChannel", "Flip Green Channel", "false"),
        ImportSettingDefinition.Section("Wrap / Filter"),
        ImportSettingDefinition.Enum<TextureWrapMode>("TextureImporter.wrapMode", "Wrap Mode", "Repeat"),
        ImportSettingDefinition.Enum<TextureWrapMode>("TextureImporter.wrapModeU", "Wrap Mode U", "Repeat"),
        ImportSettingDefinition.Enum<TextureWrapMode>("TextureImporter.wrapModeV", "Wrap Mode V", "Repeat"),
        ImportSettingDefinition.Enum<TextureWrapMode>("TextureImporter.wrapModeW", "Wrap Mode W", "Repeat"),
        ImportSettingDefinition.Enum<FilterMode>("TextureImporter.filterMode", "Filter Mode", "Bilinear"),
        ImportSettingDefinition.Int("TextureImporter.anisoLevel", "Aniso Level", "1"),
        ImportSettingDefinition.Enum<TextureImporterNPOTScale>("TextureImporter.npotScale", "Non Power of 2", "ToNearest"),
        ImportSettingDefinition.Section("Shape"),
        ImportSettingDefinition.Enum<TextureImporterGenerateCubemap>("TextureImporter.generateCubemap", "Mapping", "AutoCubemap"),
        ImportSettingDefinition.Bool("TextureImporter.correctGamma", "Correct Gamma", "false"),
        ImportSettingDefinition.Bool("TextureImporter.grayscaleToAlpha", "Grayscale To Alpha", "false"),
        ImportSettingDefinition.Bool("TextureImporter.lightmap", "Lightmap", "false"),
        ImportSettingDefinition.Bool("TextureImporter.linearTexture", "Linear Texture", "false"),
        ImportSettingDefinition.Bool("TextureImporter.normalmap", "Normal Map", "false")
    };

    private static readonly ImportSettingDefinition[] TextureSpriteSettings =
    {
        ImportSettingDefinition.Enum<SpriteImportMode>("TextureImporter.spriteImportMode", "Sprite Mode", "None"),
        ImportSettingDefinition.Float("TextureImporter.spritePixelsPerUnit", "Pixels Per Unit", "100"),
        ImportSettingDefinition.Float("TextureImporter.spritePixelsToUnits", "Pixels To Units", "100"),
        ImportSettingDefinition.String("TextureImporter.spritePackingTag", "Packing Tag", string.Empty)
    };

    private static readonly ImportSettingDefinition[] TextureSwizzleSettings =
    {
        ImportSettingDefinition.Enum<TextureImporterSwizzle>("TextureImporter.swizzleR", "R Channel", "R"),
        ImportSettingDefinition.Enum<TextureImporterSwizzle>("TextureImporter.swizzleG", "G Channel", "G"),
        ImportSettingDefinition.Enum<TextureImporterSwizzle>("TextureImporter.swizzleB", "B Channel", "B"),
        ImportSettingDefinition.Enum<TextureImporterSwizzle>("TextureImporter.swizzleA", "A Channel", "A")
    };

    private ProjectAssetImportRuleSet _ruleSet;
    private SerializedObject _serializedRuleSet;
    private Vector2 _scroll;
    private bool _ruleItemsExpanded = true;
    private bool[] _ruleExpanded = Array.Empty<bool>();
    private int[] _ruleTabIndices = Array.Empty<int>();

    [MenuItem(MenuPath)]
    public static void OpenDefault()
    {
        Open(LoadOrCreateDefaultAsset());
    }

    [MenuItem("Tools/Asset Import/Asset Import Rules")]
    public static void OpenLegacyMenu()
    {
        OpenDefault();
    }

    public static void Open(ProjectAssetImportRuleSet ruleSet)
    {
        ProjectAssetImportRuleSetWindow window = GetWindow<ProjectAssetImportRuleSetWindow>("Asset Processor");
        window.minSize = new Vector2(880f, 520f);
        window.SetRuleSet(ruleSet != null ? ruleSet : LoadOrCreateDefaultAsset());
        window.Show();
    }

    private void OnEnable()
    {
        minSize = new Vector2(880f, 520f);
        if (_ruleSet == null)
            SetRuleSet(LoadOrCreateDefaultAsset());
    }

    private void OnGUI()
    {
        if (_ruleSet == null)
        {
            DrawMissingRuleSet();
            return;
        }

        if (_serializedRuleSet == null || _serializedRuleSet.targetObject != _ruleSet)
            BuildSerializedState();

        _serializedRuleSet.Update();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawRuleItems();
        EditorGUILayout.EndScrollView();

        if (_serializedRuleSet.ApplyModifiedProperties())
            EditorUtility.SetDirty(_ruleSet);
    }

    private void SetRuleSet(ProjectAssetImportRuleSet ruleSet)
    {
        _ruleSet = ruleSet;
        BuildSerializedState();
        Repaint();
    }

    private void BuildSerializedState()
    {
        _serializedRuleSet = _ruleSet != null ? new SerializedObject(_ruleSet) : null;
        SyncFoldoutState();
    }

    private void DrawRuleItems()
    {
        if (!DrawSectionHeader("Rule Items", ref _ruleItemsExpanded))
            return;

        SerializedProperty rules = _serializedRuleSet.FindProperty("rules");
        SyncFoldoutState();
        DrawArrayToolbar(rules.arraySize, ShowAddRuleMenu, () => ClearRuleItems(rules));

        for (int i = 0; i < rules.arraySize; i++)
            DrawRuleItem(rules, i);
    }

    private void DrawRuleItem(SerializedProperty rules, int index)
    {
        SerializedProperty rule = rules.GetArrayElementAtIndex(index);
        SerializedProperty enabled = rule.FindPropertyRelative("enabled");
        SerializedProperty name = rule.FindPropertyRelative("name");

        Rect header = EditorGUILayout.GetControlRect(false, RowHeight);
        header.x += EditorGUI.indentLevel * 15f;
        header.width -= EditorGUI.indentLevel * 15f;
        EditorGUI.DrawRect(header, HeaderColor(0.52f));

        Rect foldoutRect = new Rect(header.x + 6f, header.y + 1f, header.width - 154f, header.height);
        _ruleExpanded[index] = EditorGUI.Foldout(foldoutRect, _ruleExpanded[index], BuildRuleSummary(rule), true);

        Rect toggleRect = new Rect(header.xMax - 146f, header.y + 2f, 18f, header.height - 4f);
        enabled.boolValue = EditorGUI.Toggle(toggleRect, enabled.boolValue);

        Rect buttons = new Rect(header.xMax - 122f, header.y + 1f, 116f, header.height - 2f);
        DrawRuleActionButtons(buttons, rules, index);

        if (!_ruleExpanded[index])
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            DrawPropertyRow("Rule Name", name);
            DrawPropertyRow("Enabled", enabled);

            SerializedProperty filter = rule.FindPropertyRelative("filter");
            DrawMatchRow(
                "Directory",
                filter.FindPropertyRelative("directoryMatch"),
                filter.FindPropertyRelative("directoryPattern"));
            DrawMatchRow(
                "Package Name",
                filter.FindPropertyRelative("packageNameMatch"),
                filter.FindPropertyRelative("packageNamePattern"));
            DrawAssetClassRow(filter.FindPropertyRelative("assetClass"));
            DrawImportSettingsPanel(rule.FindPropertyRelative("propertyItems"), filter.FindPropertyRelative("assetClass"), index);
        }
    }

    private void DrawImportSettingsPanel(SerializedProperty propertyItems, SerializedProperty assetClass, int ruleIndex)
    {
        ProjectAssetClass currentClass = (ProjectAssetClass)assetClass.enumValueIndex;
        string[] tabs = ResolveTabs(currentClass);
        _ruleTabIndices[ruleIndex] = Mathf.Clamp(_ruleTabIndices[ruleIndex], 0, tabs.Length - 1);

        Rect tabRect = EditorGUILayout.GetControlRect(false, 26f);
        float indentOffset = EditorGUI.indentLevel * 15f;
        tabRect.x += indentOffset;
        tabRect.width -= indentOffset;
        tabRect.x += LabelWidth;
        tabRect.width = Mathf.Min(tabRect.width - LabelWidth, tabs.Length * 96f);
        _ruleTabIndices[ruleIndex] = GUI.Toolbar(tabRect, _ruleTabIndices[ruleIndex], tabs);

        using (new EditorGUI.IndentLevelScope())
        {
            switch (tabs[_ruleTabIndices[ruleIndex]])
            {
                case "Model":
                    DrawSettingDefinitions(propertyItems, ModelSettings);
                    break;
                case "Rig":
                    DrawSettingDefinitions(propertyItems, RigSettings);
                    break;
                case "Animation":
                    DrawSettingDefinitions(propertyItems, AnimationSettings);
                    break;
                case "Materials":
                    DrawSettingDefinitions(propertyItems, MaterialSettings);
                    break;
                case "Texture":
                    DrawSettingDefinitions(propertyItems, TextureSettings);
                    break;
                case "Advanced":
                    DrawSettingDefinitions(propertyItems, TextureAdvancedSettings);
                    break;
                case "Sprite":
                    DrawSettingDefinitions(propertyItems, TextureSpriteSettings);
                    break;
                case "Swizzle":
                    DrawSettingDefinitions(propertyItems, TextureSwizzleSettings);
                    break;
                case "Custom":
                    DrawCustomSettings(propertyItems);
                    break;
            }
        }
    }

    private static void DrawSettingDefinitions(SerializedProperty propertyItems, ImportSettingDefinition[] definitions)
    {
        foreach (ImportSettingDefinition definition in definitions)
        {
            if (definition.IsSection)
            {
                DrawSettingSection(definition.Label);
                continue;
            }

            DrawImportSettingRow(propertyItems, definition);
        }
    }

    private static void DrawImportSettingRow(SerializedProperty propertyItems, ImportSettingDefinition definition)
    {
        int propertyIndex = FindPropertyItemIndex(propertyItems, definition.PropertyPath);
        bool enabled = propertyIndex >= 0;

        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset;
        rect.width -= indentOffset;

        Rect toggleRect = new Rect(rect.x + 18f, rect.y + 2f, 16f, rect.height - 4f);
        Rect labelRect = new Rect(toggleRect.xMax + 6f, rect.y + 2f, LabelWidth - 42f, rect.height - 4f);
        Rect valueRect = new Rect(rect.x + LabelWidth, rect.y + 1f, rect.width - LabelWidth - 10f, rect.height - 2f);

        bool nextEnabled = EditorGUI.Toggle(toggleRect, enabled);
        EditorGUI.LabelField(labelRect, definition.Label);

        if (nextEnabled != enabled)
        {
            if (nextEnabled)
                propertyIndex = AddPropertyItem(propertyItems, definition.PropertyPath, definition.ValueKind, definition.DefaultValue);
            else
                propertyItems.DeleteArrayElementAtIndex(propertyIndex);

            enabled = nextEnabled;
        }

        using (new EditorGUI.DisabledScope(!enabled))
        {
            if (!enabled)
            {
                DrawDefinitionValue(valueRect, definition, definition.DefaultValue);
                return;
            }

            SerializedProperty item = propertyItems.GetArrayElementAtIndex(propertyIndex);
            item.FindPropertyRelative("valueKind").enumValueIndex = (int)definition.ValueKind;
            item.FindPropertyRelative("value").stringValue = DrawDefinitionValue(
                valueRect,
                definition,
                item.FindPropertyRelative("value").stringValue);
        }
    }

    private static string DrawDefinitionValue(Rect rect, ImportSettingDefinition definition, string currentValue)
    {
        switch (definition.ValueKind)
        {
            case ProjectAssetPropertyValueKind.Bool:
                bool boolValue = ParseBool(currentValue);
                return EditorGUI.Toggle(rect, boolValue) ? "true" : "false";
            case ProjectAssetPropertyValueKind.Int:
                int intValue = ParseInt(currentValue);
                return EditorGUI.IntField(rect, intValue).ToString(CultureInfo.InvariantCulture);
            case ProjectAssetPropertyValueKind.Float:
                float floatValue = ParseFloat(currentValue);
                return EditorGUI.FloatField(rect, floatValue).ToString(CultureInfo.InvariantCulture);
            case ProjectAssetPropertyValueKind.Enum:
                int optionIndex = ResolveOptionIndex(definition.Options, currentValue, definition.DefaultValue);
                int nextIndex = EditorGUI.Popup(rect, optionIndex, definition.Options);
                return definition.Options[Mathf.Clamp(nextIndex, 0, definition.Options.Length - 1)];
            case ProjectAssetPropertyValueKind.String:
            default:
                return EditorGUI.TextField(rect, currentValue ?? string.Empty);
        }
    }

    private static void DrawCustomSettings(SerializedProperty propertyItems)
    {
        int customCount = 0;
        for (int i = 0; i < propertyItems.arraySize; i++)
        {
            SerializedProperty item = propertyItems.GetArrayElementAtIndex(i);
            if (IsKnownProperty(item.FindPropertyRelative("propertyPath").stringValue))
                continue;

            customCount++;
            DrawCustomPropertyRow(propertyItems, i);
        }

        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset + LabelWidth;
        rect.width = 126f;

        if (GUI.Button(rect, customCount == 0 ? "Add Custom" : "Add More", EditorStyles.miniButton))
            AddPropertyItem(propertyItems, "Custom.Property", ProjectAssetPropertyValueKind.String, string.Empty);
    }

    private static void DrawCustomPropertyRow(SerializedProperty propertyItems, int index)
    {
        SerializedProperty item = propertyItems.GetArrayElementAtIndex(index);
        SerializedProperty propertyPath = item.FindPropertyRelative("propertyPath");
        SerializedProperty valueKind = item.FindPropertyRelative("valueKind");
        SerializedProperty value = item.FindPropertyRelative("value");

        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset;
        rect.width -= indentOffset;

        Rect pathRect = new Rect(rect.x + 18f, rect.y + 1f, LabelWidth - 24f, rect.height - 2f);
        Rect kindRect = new Rect(rect.x + LabelWidth, rect.y + 1f, 110f, rect.height - 2f);
        Rect valueRect = new Rect(kindRect.xMax + 6f, rect.y + 1f, rect.width - LabelWidth - 148f, rect.height - 2f);
        Rect deleteRect = new Rect(rect.xMax - SmallButtonWidth - 4f, rect.y + 1f, SmallButtonWidth, rect.height - 2f);

        propertyPath.stringValue = EditorGUI.TextField(pathRect, propertyPath.stringValue);
        EditorGUI.PropertyField(kindRect, valueKind, GUIContent.none);
        value.stringValue = EditorGUI.TextField(valueRect, value.stringValue);
        if (GUI.Button(deleteRect, "x", EditorStyles.miniButton))
        {
            propertyItems.DeleteArrayElementAtIndex(index);
            GUIUtility.ExitGUI();
        }
    }

    private static void DrawSettingSection(string label)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight + 2f);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset + 18f;
        rect.width -= indentOffset + 18f;
        rect.y += 4f;
        EditorGUI.LabelField(rect, label, EditorStyles.boldLabel);
    }

    private static void DrawMatchRow(string label, SerializedProperty matchMode, SerializedProperty pattern)
    {
        Rect rect = BeginRow(label);
        float modeWidth = 132f;
        Rect modeRect = new Rect(rect.x, rect.y + 1f, modeWidth, rect.height - 2f);
        Rect patternRect = new Rect(modeRect.xMax + 6f, rect.y + 1f, rect.width - modeWidth - 10f, rect.height - 2f);

        EditorGUI.PropertyField(modeRect, matchMode, GUIContent.none);
        using (new EditorGUI.DisabledScope((ProjectAssetStringMatchMode)matchMode.enumValueIndex == ProjectAssetStringMatchMode.Any))
        {
            pattern.stringValue = EditorGUI.TextField(patternRect, pattern.stringValue);
        }
    }

    private static void DrawAssetClassRow(SerializedProperty assetClass)
    {
        Rect rect = BeginRow("Asset Class");
        Rect classRect = new Rect(rect.x, rect.y + 1f, 170f, rect.height - 2f);
        EditorGUI.PropertyField(classRect, assetClass, GUIContent.none);
    }

    private static void DrawPropertyRow(string label, SerializedProperty property)
    {
        Rect rect = BeginRow(label);
        EditorGUI.PropertyField(rect, property, GUIContent.none);
    }

    private static Rect BeginRow(string label)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset;
        rect.width -= indentOffset;

        Rect labelRect = new Rect(rect.x + 18f, rect.y + 2f, LabelWidth - 18f, rect.height - 4f);
        Rect valueRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - 6f, rect.height);
        EditorGUI.LabelField(labelRect, label);
        return valueRect;
    }

    private static bool DrawSectionHeader(string title, ref bool expanded)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 24f);
        EditorGUI.DrawRect(rect, HeaderColor(0.72f));
        Rect foldoutRect = new Rect(rect.x + 7f, rect.y + 2f, rect.width - 14f, rect.height - 4f);
        expanded = EditorGUI.Foldout(foldoutRect, expanded, title, true, EditorStyles.foldout);
        return expanded;
    }

    private void DrawArrayToolbar(int count, Action<Rect> onAdd, Action onClear)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset;
        rect.width -= indentOffset;

        Rect labelRect = new Rect(rect.x + LabelWidth, rect.y + 2f, rect.width - LabelWidth - 70f, rect.height - 4f);
        Rect addRect = new Rect(rect.xMax - 58f, rect.y + 1f, SmallButtonWidth, rect.height - 2f);
        Rect clearRect = new Rect(addRect.xMax + 4f, rect.y + 1f, SmallButtonWidth, rect.height - 2f);
        EditorGUI.LabelField(labelRect, $"{count} Array element{(count == 1 ? string.Empty : "s")}");

        if (GUI.Button(addRect, "+", EditorStyles.miniButton))
            onAdd?.Invoke(addRect);

        using (new EditorGUI.DisabledScope(count == 0))
        {
            if (GUI.Button(clearRect, "-", EditorStyles.miniButton))
                onClear?.Invoke();
        }
    }

    private void DrawRuleActionButtons(Rect rect, SerializedProperty rules, int index)
    {
        Rect duplicateRect = new Rect(rect.x, rect.y, SmallButtonWidth, rect.height);
        Rect upRect = new Rect(duplicateRect.xMax + 4f, rect.y, SmallButtonWidth, rect.height);
        Rect downRect = new Rect(upRect.xMax + 4f, rect.y, SmallButtonWidth, rect.height);
        Rect deleteRect = new Rect(downRect.xMax + 4f, rect.y, SmallButtonWidth, rect.height);

        if (GUI.Button(duplicateRect, "+", EditorStyles.miniButton))
        {
            rules.InsertArrayElementAtIndex(index);
            _ruleExpanded = InsertFoldoutState(_ruleExpanded, index + 1, true);
            _ruleTabIndices = InsertTabState(_ruleTabIndices, index + 1, 0);
        }

        using (new EditorGUI.DisabledScope(index == 0))
        {
            if (GUI.Button(upRect, "^", EditorStyles.miniButton))
            {
                rules.MoveArrayElement(index, index - 1);
                Swap(_ruleExpanded, index, index - 1);
                Swap(_ruleTabIndices, index, index - 1);
            }
        }

        using (new EditorGUI.DisabledScope(index >= rules.arraySize - 1))
        {
            if (GUI.Button(downRect, "v", EditorStyles.miniButton))
            {
                rules.MoveArrayElement(index, index + 1);
                Swap(_ruleExpanded, index, index + 1);
                Swap(_ruleTabIndices, index, index + 1);
            }
        }

        if (GUI.Button(deleteRect, "x", EditorStyles.miniButton))
        {
            if (EditorUtility.DisplayDialog("Remove Rule", "Remove this rule item?", "Remove", "Cancel"))
            {
                rules.DeleteArrayElementAtIndex(index);
                SyncFoldoutState();
                GUIUtility.ExitGUI();
            }
        }
    }

    private void ShowAddRuleMenu(Rect buttonRect)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Empty Rule"), false, () => AddRule("New Rule", ProjectAssetClass.Any));
        menu.AddItem(new GUIContent("Model/Static Model"), false, AddStaticModelRule);
        menu.AddItem(new GUIContent("Model/Character Model"), false, AddCharacterModelRule);
        menu.AddItem(new GUIContent("Texture/Normal Texture"), false, AddNormalTextureRule);
        menu.DropDown(buttonRect);
    }

    private void AddStaticModelRule()
    {
        SerializedProperty rule = AddRule("Static Model Rule", ProjectAssetClass.Model);
        SerializedProperty propertyItems = rule.FindPropertyRelative("propertyItems");
        AddPropertyItem(propertyItems, "ModelImporter.animationType", ProjectAssetPropertyValueKind.Enum, "None");
        AddPropertyItem(propertyItems, "ModelImporter.importAnimation", ProjectAssetPropertyValueKind.Bool, "false");
        AddPropertyItem(propertyItems, "ModelImporter.importBlendShapes", ProjectAssetPropertyValueKind.Bool, "false");
        AddPropertyItem(propertyItems, "ModelImporter.addCollider", ProjectAssetPropertyValueKind.Bool, "true");
        AddPropertyItem(propertyItems, "ModelImporter.generateSecondaryUV", ProjectAssetPropertyValueKind.Bool, "true");
    }

    private void AddCharacterModelRule()
    {
        SerializedProperty rule = AddRule("Character Model Rule", ProjectAssetClass.Model);
        SerializedProperty filter = rule.FindPropertyRelative("filter");
        filter.FindPropertyRelative("directoryMatch").enumValueIndex = (int)ProjectAssetStringMatchMode.Contains;
        filter.FindPropertyRelative("directoryPattern").stringValue = "Characters/";

        SerializedProperty propertyItems = rule.FindPropertyRelative("propertyItems");
        AddPropertyItem(propertyItems, "ModelImporter.animationType", ProjectAssetPropertyValueKind.Enum, "Human");
        AddPropertyItem(propertyItems, "ModelImporter.avatarSetup", ProjectAssetPropertyValueKind.Enum, "CreateFromThisModel");
        AddPropertyItem(propertyItems, "ModelImporter.importAnimation", ProjectAssetPropertyValueKind.Bool, "true");
        AddPropertyItem(propertyItems, "ModelImporter.importBlendShapes", ProjectAssetPropertyValueKind.Bool, "true");
        AddPropertyItem(propertyItems, "ModelImporter.meshCompression", ProjectAssetPropertyValueKind.Enum, "Off");
    }

    private void AddNormalTextureRule()
    {
        SerializedProperty rule = AddRule("Normal Texture Rule", ProjectAssetClass.Texture2D);
        SerializedProperty filter = rule.FindPropertyRelative("filter");
        filter.FindPropertyRelative("packageNameMatch").enumValueIndex = (int)ProjectAssetStringMatchMode.EndsWith;
        filter.FindPropertyRelative("packageNamePattern").stringValue = "_N";

        SerializedProperty propertyItems = rule.FindPropertyRelative("propertyItems");
        AddPropertyItem(propertyItems, "TextureImporter.textureType", ProjectAssetPropertyValueKind.Enum, "NormalMap");
        AddPropertyItem(propertyItems, "TextureImporter.wrapModeU", ProjectAssetPropertyValueKind.Enum, "Clamp");
        AddPropertyItem(propertyItems, "TextureImporter.wrapModeV", ProjectAssetPropertyValueKind.Enum, "Clamp");
    }

    private SerializedProperty AddRule(string ruleName, ProjectAssetClass assetClass)
    {
        _serializedRuleSet.Update();
        SerializedProperty rules = _serializedRuleSet.FindProperty("rules");
        int index = rules.arraySize;
        rules.InsertArrayElementAtIndex(index);
        SerializedProperty rule = rules.GetArrayElementAtIndex(index);
        InitializeRule(rule, ruleName, assetClass);
        _ruleExpanded = InsertFoldoutState(_ruleExpanded, index, true);
        _ruleTabIndices = InsertTabState(_ruleTabIndices, index, 0);
        _serializedRuleSet.ApplyModifiedProperties();
        EditorUtility.SetDirty(_ruleSet);
        return rules.GetArrayElementAtIndex(index);
    }

    private static void InitializeRule(SerializedProperty rule, string ruleName, ProjectAssetClass assetClass)
    {
        rule.FindPropertyRelative("name").stringValue = ruleName;
        rule.FindPropertyRelative("enabled").boolValue = true;

        SerializedProperty filter = rule.FindPropertyRelative("filter");
        filter.FindPropertyRelative("directoryMatch").enumValueIndex = (int)ProjectAssetStringMatchMode.Any;
        filter.FindPropertyRelative("directoryPattern").stringValue = string.Empty;
        filter.FindPropertyRelative("packageNameMatch").enumValueIndex = (int)ProjectAssetStringMatchMode.Any;
        filter.FindPropertyRelative("packageNamePattern").stringValue = string.Empty;
        filter.FindPropertyRelative("assetClass").enumValueIndex = (int)assetClass;

        rule.FindPropertyRelative("propertyItems").arraySize = 0;
    }

    private static int AddPropertyItem(
        SerializedProperty propertyItems,
        string propertyPath,
        ProjectAssetPropertyValueKind valueKind,
        string value)
    {
        int index = propertyItems.arraySize;
        propertyItems.InsertArrayElementAtIndex(index);
        SerializedProperty item = propertyItems.GetArrayElementAtIndex(index);
        item.FindPropertyRelative("propertyPath").stringValue = propertyPath;
        item.FindPropertyRelative("valueKind").enumValueIndex = (int)valueKind;
        item.FindPropertyRelative("value").stringValue = value;
        return index;
    }

    private void ClearRuleItems(SerializedProperty rules)
    {
        if (!EditorUtility.DisplayDialog("Clear Rules", "Remove every rule item?", "Clear", "Cancel"))
            return;

        rules.arraySize = 0;
        SyncFoldoutState();
    }

    private void DrawMissingRuleSet()
    {
        EditorGUILayout.HelpBox("Default asset import rule set could not be loaded.", MessageType.Warning);
        if (GUILayout.Button("Create Default Rule Set"))
            SetRuleSet(LoadOrCreateDefaultAsset());
    }

    private void SyncFoldoutState()
    {
        int count = 0;
        if (_serializedRuleSet != null)
        {
            SerializedProperty rules = _serializedRuleSet.FindProperty("rules");
            count = rules != null ? rules.arraySize : 0;
        }

        ResizeFoldouts(ref _ruleExpanded, count, true);
        ResizeTabs(ref _ruleTabIndices, count, 0);
    }

    private static void ResizeFoldouts(ref bool[] values, int count, bool defaultValue)
    {
        if (values != null && values.Length == count)
            return;

        bool[] resized = new bool[count];
        for (int i = 0; i < resized.Length; i++)
            resized[i] = values != null && i < values.Length ? values[i] : defaultValue;

        values = resized;
    }

    private static void ResizeTabs(ref int[] values, int count, int defaultValue)
    {
        if (values != null && values.Length == count)
            return;

        int[] resized = new int[count];
        for (int i = 0; i < resized.Length; i++)
            resized[i] = values != null && i < values.Length ? values[i] : defaultValue;

        values = resized;
    }

    private static bool[] InsertFoldoutState(bool[] values, int index, bool insertedValue)
    {
        bool[] result = new bool[(values?.Length ?? 0) + 1];
        for (int i = 0; i < result.Length; i++)
        {
            if (i < index)
                result[i] = values[i];
            else if (i == index)
                result[i] = insertedValue;
            else
                result[i] = values[i - 1];
        }

        return result;
    }

    private static int[] InsertTabState(int[] values, int index, int insertedValue)
    {
        int[] result = new int[(values?.Length ?? 0) + 1];
        for (int i = 0; i < result.Length; i++)
        {
            if (i < index)
                result[i] = values[i];
            else if (i == index)
                result[i] = insertedValue;
            else
                result[i] = values[i - 1];
        }

        return result;
    }

    private static void Swap(bool[] values, int first, int second)
    {
        if (values == null || first < 0 || second < 0 || first >= values.Length || second >= values.Length)
            return;

        (values[first], values[second]) = (values[second], values[first]);
    }

    private static void Swap(int[] values, int first, int second)
    {
        if (values == null || first < 0 || second < 0 || first >= values.Length || second >= values.Length)
            return;

        (values[first], values[second]) = (values[second], values[first]);
    }

    private static string[] ResolveTabs(ProjectAssetClass assetClass)
    {
        return assetClass switch
        {
            ProjectAssetClass.Model => new[] { "Model", "Rig", "Animation", "Materials", "Custom" },
            ProjectAssetClass.Texture2D => new[] { "Texture", "Advanced", "Sprite", "Swizzle", "Custom" },
            _ => new[] { "Model", "Rig", "Animation", "Materials", "Texture", "Advanced", "Sprite", "Swizzle", "Custom" }
        };
    }

    private static int FindPropertyItemIndex(SerializedProperty propertyItems, string propertyPath)
    {
        for (int i = 0; i < propertyItems.arraySize; i++)
        {
            SerializedProperty item = propertyItems.GetArrayElementAtIndex(i);
            string candidate = item.FindPropertyRelative("propertyPath").stringValue;
            if (string.Equals(candidate, propertyPath, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static bool IsKnownProperty(string propertyPath)
    {
        return IsKnownProperty(propertyPath, ModelSettings) ||
            IsKnownProperty(propertyPath, RigSettings) ||
            IsKnownProperty(propertyPath, AnimationSettings) ||
            IsKnownProperty(propertyPath, MaterialSettings) ||
            IsKnownProperty(propertyPath, TextureSettings) ||
            IsKnownProperty(propertyPath, TextureAdvancedSettings) ||
            IsKnownProperty(propertyPath, TextureSpriteSettings) ||
            IsKnownProperty(propertyPath, TextureSwizzleSettings);
    }

    private static bool IsKnownProperty(string propertyPath, ImportSettingDefinition[] definitions)
    {
        foreach (ImportSettingDefinition definition in definitions)
        {
            if (!definition.IsSection && string.Equals(definition.PropertyPath, propertyPath, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int ResolveOptionIndex(string[] options, string currentValue, string defaultValue)
    {
        string value = string.IsNullOrWhiteSpace(currentValue) ? defaultValue : currentValue;
        for (int i = 0; i < options.Length; i++)
        {
            if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out bool result))
            return result;

        return false;
    }

    private static int ParseInt(string value)
    {
        if (int.TryParse(value, out int result))
            return result;

        return 0;
    }

    private static float ParseFloat(string value)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            return result;
        if (float.TryParse(value, out result))
            return result;

        return 0f;
    }

    private static string BuildRuleSummary(SerializedProperty rule)
    {
        SerializedProperty name = rule.FindPropertyRelative("name");
        SerializedProperty filter = rule.FindPropertyRelative("filter");
        SerializedProperty directoryMatch = filter.FindPropertyRelative("directoryMatch");
        SerializedProperty directoryPattern = filter.FindPropertyRelative("directoryPattern");
        SerializedProperty packageNameMatch = filter.FindPropertyRelative("packageNameMatch");
        SerializedProperty packageNamePattern = filter.FindPropertyRelative("packageNamePattern");
        SerializedProperty assetClass = filter.FindPropertyRelative("assetClass");

        StringBuilder builder = new StringBuilder();
        builder.Append(string.IsNullOrWhiteSpace(name.stringValue) ? "Rule" : name.stringValue);
        AppendMatchSummary(builder, "Dir", directoryMatch, directoryPattern);
        AppendMatchSummary(builder, "Pkg", packageNameMatch, packageNamePattern);
        builder.Append("  [Class: ").Append(GetEnumName(assetClass)).Append(']');
        return builder.ToString();
    }

    private static void AppendMatchSummary(StringBuilder builder, string label, SerializedProperty matchMode, SerializedProperty pattern)
    {
        ProjectAssetStringMatchMode mode = (ProjectAssetStringMatchMode)matchMode.enumValueIndex;
        if (mode == ProjectAssetStringMatchMode.Any)
        {
            builder.Append("  [").Append(label).Append(": Any]");
            return;
        }

        builder.Append("  [")
            .Append(label)
            .Append(": ")
            .Append(pattern.stringValue)
            .Append(']');
    }

    private static string GetEnumName(SerializedProperty property)
    {
        if (property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length)
            return property.enumDisplayNames[property.enumValueIndex];

        return property.enumValueIndex.ToString();
    }

    private static Color HeaderColor(float strength)
    {
        if (EditorGUIUtility.isProSkin)
            return new Color(strength * 0.2f, strength * 0.2f, strength * 0.2f, 1f);

        return new Color(0.68f + strength * 0.08f, 0.68f + strength * 0.08f, 0.68f + strength * 0.08f, 1f);
    }

    private static ProjectAssetImportRuleSet LoadOrCreateDefaultAsset()
    {
        ProjectAssetImportRuleSet ruleSet = AssetDatabase.LoadAssetAtPath<ProjectAssetImportRuleSet>(
            ProjectAssetImportRuleSet.DefaultAssetPath);
        if (ruleSet != null)
            return ruleSet;

        EnsureAssetFolder(Path.GetDirectoryName(ProjectAssetImportRuleSet.DefaultAssetPath)?.Replace('\\', '/'));

        ruleSet = ProjectAssetImportRuleSet.CreateDefaultInstance();
        AssetDatabase.CreateAsset(ruleSet, ProjectAssetImportRuleSet.DefaultAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return ruleSet;
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            throw new InvalidOperationException($"Asset folder must be under Assets: {folderPath}");

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private readonly struct ImportSettingDefinition
    {
        public readonly bool IsSection;
        public readonly string PropertyPath;
        public readonly string Label;
        public readonly ProjectAssetPropertyValueKind ValueKind;
        public readonly string DefaultValue;
        public readonly string[] Options;

        private ImportSettingDefinition(
            bool isSection,
            string propertyPath,
            string label,
            ProjectAssetPropertyValueKind valueKind,
            string defaultValue,
            string[] options)
        {
            IsSection = isSection;
            PropertyPath = propertyPath;
            Label = label;
            ValueKind = valueKind;
            DefaultValue = defaultValue;
            Options = options ?? Array.Empty<string>();
        }

        public static ImportSettingDefinition Section(string label)
        {
            return new ImportSettingDefinition(true, string.Empty, label, ProjectAssetPropertyValueKind.String, string.Empty, Array.Empty<string>());
        }

        public static ImportSettingDefinition Bool(string propertyPath, string label, string defaultValue)
        {
            return new ImportSettingDefinition(false, propertyPath, label, ProjectAssetPropertyValueKind.Bool, defaultValue, Array.Empty<string>());
        }

        public static ImportSettingDefinition Int(string propertyPath, string label, string defaultValue)
        {
            return new ImportSettingDefinition(false, propertyPath, label, ProjectAssetPropertyValueKind.Int, defaultValue, Array.Empty<string>());
        }

        public static ImportSettingDefinition Float(string propertyPath, string label, string defaultValue)
        {
            return new ImportSettingDefinition(false, propertyPath, label, ProjectAssetPropertyValueKind.Float, defaultValue, Array.Empty<string>());
        }

        public static ImportSettingDefinition String(string propertyPath, string label, string defaultValue)
        {
            return new ImportSettingDefinition(false, propertyPath, label, ProjectAssetPropertyValueKind.String, defaultValue, Array.Empty<string>());
        }

        public static ImportSettingDefinition Enum(string propertyPath, string label, string defaultValue, params string[] options)
        {
            return new ImportSettingDefinition(false, propertyPath, label, ProjectAssetPropertyValueKind.Enum, defaultValue, options);
        }

        public static ImportSettingDefinition Enum<TEnum>(string propertyPath, string label, string defaultValue)
            where TEnum : struct
        {
            return Enum(propertyPath, label, defaultValue, System.Enum.GetNames(typeof(TEnum)));
        }
    }
}

[CustomEditor(typeof(ProjectAssetImportRuleSet))]
internal sealed class ProjectAssetImportRuleSetInspector : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Open Asset Processor"))
            ProjectAssetImportRuleSetWindow.Open((ProjectAssetImportRuleSet)target);

        EditorGUILayout.Space(6f);
        DrawDefaultInspector();
    }
}
