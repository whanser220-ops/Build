using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public sealed class ProjectAssetImportRuleSetWindow : EditorWindow
{
    private const string MenuPath = "Tools/Asset Import/Asset Processor";
    private const float LabelWidth = 185f;
    private const float RowHeight = 22f;
    private const float SmallButtonWidth = 26f;

    private static readonly PropertySuggestion[] PropertySuggestions =
    {
        new PropertySuggestion("ModelImporter.importCameras", "Import Cameras", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("ModelImporter.importLights", "Import Lights", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("ModelImporter.importVisibility", "Import Visibility", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("ModelImporter.sortHierarchyByName", "Sort Hierarchy", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("ModelImporter.extraUserProperties", "Extra User Properties", ProjectAssetPropertyValueKind.String, "Collision;Collider"),
        new PropertySuggestion("ModelImporter.preserveHierarchy", "Preserve Hierarchy", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("ModelImporter.animationType", "Animation Type", ProjectAssetPropertyValueKind.Enum, "None"),
        new PropertySuggestion("ModelImporter.avatarSetup", "Avatar Setup", ProjectAssetPropertyValueKind.Enum, "CreateFromThisModel"),
        new PropertySuggestion("ModelImporter.importAnimation", "Import Animation", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("ModelImporter.importBlendShapes", "Import Blend Shapes", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("ModelImporter.addCollider", "Add Collider", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("ModelImporter.generateSecondaryUV", "Generate Lightmap UV", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("ModelImporter.isReadable", "Read/Write", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("ModelImporter.meshCompression", "Mesh Compression", ProjectAssetPropertyValueKind.Enum, "Low"),
        new PropertySuggestion("ModelImporter.animationCompression", "Animation Compression", ProjectAssetPropertyValueKind.Enum, "Optimal"),
        new PropertySuggestion("ModelImporter.clipNameFromAsset", "Clip Name From Asset", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("TextureImporter.textureType", "Texture Type", ProjectAssetPropertyValueKind.Enum, "Default"),
        new PropertySuggestion("TextureImporter.textureCompression", "Compression Settings", ProjectAssetPropertyValueKind.Enum, "Compressed"),
        new PropertySuggestion("TextureImporter.wrapMode", "Tiling Method", ProjectAssetPropertyValueKind.Enum, "Repeat"),
        new PropertySuggestion("TextureImporter.wrapModeU", "X-axis Tiling Method", ProjectAssetPropertyValueKind.Enum, "Clamp"),
        new PropertySuggestion("TextureImporter.wrapModeV", "Y-axis Tiling Method", ProjectAssetPropertyValueKind.Enum, "Clamp"),
        new PropertySuggestion("TextureImporter.wrapModeW", "Z-axis Tiling Method", ProjectAssetPropertyValueKind.Enum, "Clamp"),
        new PropertySuggestion("TextureImporter.sRGBTexture", "sRGB", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("TextureImporter.mipmapEnabled", "Mip Maps", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("TextureImporter.isReadable", "Read/Write", ProjectAssetPropertyValueKind.Bool, "false")
    };

    private static readonly GUIContent[] PropertyPopupLabels = BuildPropertyPopupLabels();

    private ProjectAssetImportRuleSet _ruleSet;
    private SerializedObject _serializedRuleSet;
    private Vector2 _scroll;
    private bool _globalSettingsExpanded = true;
    private bool _ruleSettingsExpanded = true;
    private bool _ruleItemsExpanded = true;
    private bool[] _ruleExpanded = Array.Empty<bool>();
    private bool[] _propertyItemsExpanded = Array.Empty<bool>();
    private string _regexPattern = string.Empty;
    private string _regexText = string.Empty;
    private string _regexResult = string.Empty;

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
        window.minSize = new Vector2(960f, 580f);
        window.SetRuleSet(ruleSet != null ? ruleSet : LoadOrCreateDefaultAsset());
        window.Show();
    }

    private void OnEnable()
    {
        minSize = new Vector2(960f, 580f);
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
        DrawHeader();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawGlobalSettings();
        DrawRuleSettings();
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

    private void DrawHeader()
    {
        EditorGUILayout.Space(8f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Plugins - Asset Processor", HeaderTitleStyle(), GUILayout.Height(28f));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Export...", GUILayout.Width(86f), GUILayout.Height(22f)))
                ExportRuleSet();

            if (GUILayout.Button("Import...", GUILayout.Width(86f), GUILayout.Height(22f)))
                ImportRuleSet();

            GUILayout.Space(8f);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(24f);
            EditorGUILayout.LabelField("Asset processor settings", EditorStyles.miniLabel);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(18f);
            EditorGUILayout.LabelField(BuildWritableStatus(), EditorStyles.wordWrappedMiniLabel);
        }

        EditorGUILayout.Space(8f);
    }

    private void DrawGlobalSettings()
    {
        if (!DrawSectionHeader("Global Settings", ref _globalSettingsExpanded))
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            SerializedProperty applyRuleOnSave = _serializedRuleSet.FindProperty("applyRuleOnSave");
            if (applyRuleOnSave != null)
                DrawPropertyRow("Apply Rule on Save", applyRuleOnSave);

            DrawPropertyRow("Apply All Matching Rules", _serializedRuleSet.FindProperty("applyAllMatchingRules"));
            DrawRegexTester();
        }
    }

    private void DrawRuleSettings()
    {
        if (!DrawSectionHeader("Rule Settings", ref _ruleSettingsExpanded))
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            Rect ruleSetRect = BeginRow("Rule Set");
            ProjectAssetImportRuleSet selected = (ProjectAssetImportRuleSet)EditorGUI.ObjectField(
                ruleSetRect,
                _ruleSet,
                typeof(ProjectAssetImportRuleSet),
                false);
            if (selected != _ruleSet)
            {
                SetRuleSet(selected);
                GUIUtility.ExitGUI();
            }

            Rect pathRect = BeginRow("Default Asset");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextField(pathRect, ProjectAssetImportRuleSet.DefaultAssetPath);
            }

            Rect buttonRect = BeginRow(string.Empty);
            DrawRuleSettingsButtons(buttonRect);
        }
    }

    private void DrawRuleItems()
    {
        if (!DrawSectionHeader("Rule Items", ref _ruleItemsExpanded))
            return;

        SerializedProperty rules = _serializedRuleSet.FindProperty("rules");
        SyncFoldoutState();
        DrawArrayToolbar("Rule Items", rules.arraySize, ShowAddRuleMenu, () => ClearRuleItems(rules));

        for (int i = 0; i < rules.arraySize; i++)
            DrawRuleItem(rules, i);
    }

    private void DrawRegexTester()
    {
        Rect rect = BeginRow("Regex Tester");
        float labelWidth = 52f;
        float patternWidth = Mathf.Max(90f, rect.width * 0.23f);
        float textWidth = Mathf.Max(90f, rect.width * 0.23f);
        float resultWidth = Mathf.Max(88f, rect.width - patternWidth - textWidth - labelWidth * 3f - 18f);

        Rect patternLabelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
        Rect patternRect = new Rect(patternLabelRect.xMax + 4f, rect.y, patternWidth, rect.height);
        Rect textLabelRect = new Rect(patternRect.xMax + 8f, rect.y, labelWidth, rect.height);
        Rect textRect = new Rect(textLabelRect.xMax + 4f, rect.y, textWidth, rect.height);
        Rect resultLabelRect = new Rect(textRect.xMax + 8f, rect.y, labelWidth, rect.height);
        Rect resultRect = new Rect(resultLabelRect.xMax + 4f, rect.y, resultWidth, rect.height);

        EditorGUI.LabelField(patternLabelRect, "Pattern:");
        _regexPattern = EditorGUI.TextField(patternRect, _regexPattern);
        EditorGUI.LabelField(textLabelRect, "Text:");
        _regexText = EditorGUI.TextField(textRect, _regexText);
        EditorGUI.LabelField(resultLabelRect, "Result:");
        _regexResult = EvaluateRegex(_regexPattern, _regexText);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUI.TextField(resultRect, _regexResult);
        }
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
        _ruleExpanded[index] = EditorGUI.Foldout(
            foldoutRect,
            _ruleExpanded[index],
            BuildRuleSummary(rule),
            true);

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
            DrawDisabledToggleRow("Filter Sub Asset or Object", false);
            DrawDisabledPopupRow("Property Modify Type", "Property Chain and Value");
            DrawPropertyItems(rule.FindPropertyRelative("propertyItems"), index);
        }
    }

    private void DrawPropertyItems(SerializedProperty propertyItems, int ruleIndex)
    {
        Rect header = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        header.x += indentOffset;
        header.width -= indentOffset;
        EditorGUI.DrawRect(header, HeaderColor(0.35f));

        Rect foldoutRect = new Rect(header.x + 6f, header.y + 1f, header.width - 130f, header.height);
        _propertyItemsExpanded[ruleIndex] = EditorGUI.Foldout(
            foldoutRect,
            _propertyItemsExpanded[ruleIndex],
            $"Property Items                                  {propertyItems.arraySize} Array element{(propertyItems.arraySize == 1 ? string.Empty : "s")}",
            true);

        Rect addRect = new Rect(header.xMax - 58f, header.y + 1f, SmallButtonWidth, header.height - 2f);
        Rect clearRect = new Rect(addRect.xMax + 4f, header.y + 1f, SmallButtonWidth, header.height - 2f);
        if (GUI.Button(addRect, "+", EditorStyles.miniButton))
            AddPropertyItem(propertyItems, PropertySuggestions[0]);
        if (GUI.Button(clearRect, "-", EditorStyles.miniButton))
            ClearPropertyItems(propertyItems);

        if (!_propertyItemsExpanded[ruleIndex])
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            for (int i = 0; i < propertyItems.arraySize; i++)
                DrawPropertyItemRow(propertyItems, i);
        }
    }

    private static void DrawPropertyItemRow(SerializedProperty propertyItems, int index)
    {
        SerializedProperty item = propertyItems.GetArrayElementAtIndex(index);
        SerializedProperty propertyPath = item.FindPropertyRelative("propertyPath");
        SerializedProperty valueKind = item.FindPropertyRelative("valueKind");
        SerializedProperty value = item.FindPropertyRelative("value");

        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset;
        rect.width -= indentOffset;

        float popupWidth = Mathf.Min(300f, rect.width * 0.34f);
        float valueWidth = Mathf.Min(180f, rect.width * 0.22f);
        float kindWidth = 96f;
        float buttonArea = SmallButtonWidth * 3f + 10f;

        Rect popupRect = new Rect(rect.x, rect.y + 1f, popupWidth, rect.height - 2f);
        Rect valueRect = new Rect(rect.xMax - valueWidth - kindWidth - buttonArea - 14f, rect.y + 1f, valueWidth, rect.height - 2f);
        Rect kindRect = new Rect(valueRect.xMax + 6f, rect.y + 1f, kindWidth, rect.height - 2f);
        Rect pathRect = new Rect(popupRect.xMax + 6f, rect.y + 1f, valueRect.x - popupRect.xMax - 12f, rect.height - 2f);

        int currentSuggestion = FindSuggestionIndex(propertyPath.stringValue);
        int currentPopupIndex = currentSuggestion >= 0 ? currentSuggestion + 1 : 0;
        int nextPopupIndex = EditorGUI.Popup(popupRect, currentPopupIndex, PropertyPopupLabels);
        if (nextPopupIndex > 0 && nextPopupIndex != currentPopupIndex)
            ApplyPropertySuggestion(item, PropertySuggestions[nextPopupIndex - 1]);

        propertyPath.stringValue = EditorGUI.TextField(pathRect, propertyPath.stringValue);
        value.stringValue = EditorGUI.TextField(valueRect, value.stringValue);
        EditorGUI.PropertyField(kindRect, valueKind, GUIContent.none);

        Rect upRect = new Rect(kindRect.xMax + 6f, rect.y + 1f, SmallButtonWidth, rect.height - 2f);
        Rect downRect = new Rect(upRect.xMax + 4f, rect.y + 1f, SmallButtonWidth, rect.height - 2f);
        Rect deleteRect = new Rect(downRect.xMax + 4f, rect.y + 1f, SmallButtonWidth, rect.height - 2f);

        using (new EditorGUI.DisabledScope(index == 0))
        {
            if (GUI.Button(upRect, "^", EditorStyles.miniButton))
                propertyItems.MoveArrayElement(index, index - 1);
        }

        using (new EditorGUI.DisabledScope(index >= propertyItems.arraySize - 1))
        {
            if (GUI.Button(downRect, "v", EditorStyles.miniButton))
                propertyItems.MoveArrayElement(index, index + 1);
        }

        if (GUI.Button(deleteRect, "x", EditorStyles.miniButton))
        {
            propertyItems.DeleteArrayElementAtIndex(index);
            GUIUtility.ExitGUI();
        }
    }

    private static void DrawMatchRow(string label, SerializedProperty matchMode, SerializedProperty pattern)
    {
        Rect rect = BeginRow(label);
        float modeWidth = 132f;
        Rect modeRect = new Rect(rect.x, rect.y + 1f, modeWidth, rect.height - 2f);
        Rect patternRect = new Rect(modeRect.xMax + 6f, rect.y + 1f, rect.width - modeWidth - 42f, rect.height - 2f);
        Rect browseRect = new Rect(patternRect.xMax + 6f, rect.y + 1f, 30f, rect.height - 2f);

        EditorGUI.PropertyField(modeRect, matchMode, GUIContent.none);
        using (new EditorGUI.DisabledScope((ProjectAssetStringMatchMode)matchMode.enumValueIndex == ProjectAssetStringMatchMode.Any))
        {
            pattern.stringValue = EditorGUI.TextField(patternRect, pattern.stringValue);
        }

        using (new EditorGUI.DisabledScope(true))
        {
            GUI.Button(browseRect, "...", EditorStyles.miniButton);
        }
    }

    private static void DrawAssetClassRow(SerializedProperty assetClass)
    {
        Rect rect = BeginRow("Asset Class");
        float includeWidth = 142f;
        Rect includeRect = new Rect(rect.x, rect.y + 1f, includeWidth, rect.height - 2f);
        Rect classRect = new Rect(includeRect.xMax + 6f, rect.y + 1f, 160f, rect.height - 2f);
        Rect infoRect = new Rect(classRect.xMax + 8f, rect.y + 1f, 250f, rect.height - 2f);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUI.Popup(includeRect, 0, new[] { "Include Sub Class" });
        }

        EditorGUI.PropertyField(classRect, assetClass, GUIContent.none);
        EditorGUI.LabelField(infoRect, "Model / Texture2D / Any", EditorStyles.miniLabel);
    }

    private static void DrawPropertyRow(string label, SerializedProperty property)
    {
        Rect rect = BeginRow(label);
        EditorGUI.PropertyField(rect, property, GUIContent.none);
    }

    private static void DrawDisabledToggleRow(string label, bool value)
    {
        Rect rect = BeginRow(label);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUI.Toggle(rect, value);
        }
    }

    private static void DrawDisabledPopupRow(string label, string value)
    {
        Rect rect = BeginRow(label);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUI.Popup(rect, 0, new[] { value });
        }
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

    private void DrawArrayToolbar(string label, int count, Action<Rect> onAdd, Action onClear)
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

    private void DrawRuleSettingsButtons(Rect rect)
    {
        Rect pingRect = new Rect(rect.x, rect.y + 1f, 80f, rect.height - 2f);
        Rect saveRect = new Rect(pingRect.xMax + 6f, rect.y + 1f, 80f, rect.height - 2f);
        Rect resetRect = new Rect(saveRect.xMax + 6f, rect.y + 1f, 116f, rect.height - 2f);

        if (GUI.Button(pingRect, "Ping"))
            EditorGUIUtility.PingObject(_ruleSet);

        if (GUI.Button(saveRect, "Save"))
            SaveRuleSet();

        if (GUI.Button(resetRect, "Reset Defaults"))
            ResetDefaultsWithPrompt();
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
            _propertyItemsExpanded = InsertFoldoutState(_propertyItemsExpanded, index + 1, true);
        }

        using (new EditorGUI.DisabledScope(index == 0))
        {
            if (GUI.Button(upRect, "^", EditorStyles.miniButton))
            {
                rules.MoveArrayElement(index, index - 1);
                Swap(_ruleExpanded, index, index - 1);
                Swap(_propertyItemsExpanded, index, index - 1);
            }
        }

        using (new EditorGUI.DisabledScope(index >= rules.arraySize - 1))
        {
            if (GUI.Button(downRect, "v", EditorStyles.miniButton))
            {
                rules.MoveArrayElement(index, index + 1);
                Swap(_ruleExpanded, index, index + 1);
                Swap(_propertyItemsExpanded, index, index + 1);
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
        _propertyItemsExpanded = InsertFoldoutState(_propertyItemsExpanded, index, true);
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

    private static void AddPropertyItem(SerializedProperty propertyItems, PropertySuggestion suggestion)
    {
        AddPropertyItem(propertyItems, suggestion.PropertyPath, suggestion.ValueKind, suggestion.DefaultValue);
    }

    private static void AddPropertyItem(
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
    }

    private void ClearRuleItems(SerializedProperty rules)
    {
        if (!EditorUtility.DisplayDialog("Clear Rules", "Remove every rule item?", "Clear", "Cancel"))
            return;

        rules.arraySize = 0;
        SyncFoldoutState();
    }

    private static void ClearPropertyItems(SerializedProperty propertyItems)
    {
        if (!EditorUtility.DisplayDialog("Clear Property Items", "Remove every property item in this rule?", "Clear", "Cancel"))
            return;

        propertyItems.arraySize = 0;
    }

    private void ResetDefaultsWithPrompt()
    {
        if (!EditorUtility.DisplayDialog(
            "Reset Import Rules",
            "Replace the current rule list with the project default rules?",
            "Reset",
            "Cancel"))
            return;

        Undo.RecordObject(_ruleSet, "Reset Asset Processor Rules");
        _ruleSet.applyRuleOnSave = ProjectAssetRuleOnSaveMode.Disable;
        _ruleSet.applyAllMatchingRules = true;
        _ruleSet.rules = ProjectAssetImportRuleSet.CreateDefaultRules();
        EditorUtility.SetDirty(_ruleSet);
        BuildSerializedState();
        SaveRuleSet();
    }

    private void ExportRuleSet()
    {
        _serializedRuleSet.ApplyModifiedProperties();

        string path = EditorUtility.SaveFilePanel(
            "Export Asset Processor Settings",
            string.Empty,
            "ProjectAssetImportRuleSet.json",
            "json");
        if (string.IsNullOrWhiteSpace(path))
            return;

        File.WriteAllText(path, EditorJsonUtility.ToJson(_ruleSet, true), new UTF8Encoding(false));
    }

    private void ImportRuleSet()
    {
        string path = EditorUtility.OpenFilePanel("Import Asset Processor Settings", string.Empty, "json");
        if (string.IsNullOrWhiteSpace(path))
            return;

        string json = File.ReadAllText(path, Encoding.UTF8);
        Undo.RecordObject(_ruleSet, "Import Asset Processor Settings");
        EditorJsonUtility.FromJsonOverwrite(json, _ruleSet);
        EditorUtility.SetDirty(_ruleSet);
        BuildSerializedState();
        SaveRuleSet();
    }

    private void SaveRuleSet()
    {
        _serializedRuleSet?.ApplyModifiedProperties();
        if (_ruleSet != null)
            EditorUtility.SetDirty(_ruleSet);

        AssetDatabase.SaveAssets();
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
        ResizeFoldouts(ref _propertyItemsExpanded, count, true);
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

    private static void Swap(bool[] values, int first, int second)
    {
        if (values == null || first < 0 || second < 0 || first >= values.Length || second >= values.Length)
            return;

        (values[first], values[second]) = (values[second], values[first]);
    }

    private static void ApplyPropertySuggestion(SerializedProperty item, PropertySuggestion suggestion)
    {
        item.FindPropertyRelative("propertyPath").stringValue = suggestion.PropertyPath;
        item.FindPropertyRelative("valueKind").enumValueIndex = (int)suggestion.ValueKind;
        item.FindPropertyRelative("value").stringValue = suggestion.DefaultValue;
    }

    private static int FindSuggestionIndex(string propertyPath)
    {
        for (int i = 0; i < PropertySuggestions.Length; i++)
        {
            if (string.Equals(PropertySuggestions[i].PropertyPath, propertyPath, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static GUIContent[] BuildPropertyPopupLabels()
    {
        GUIContent[] labels = new GUIContent[PropertySuggestions.Length + 1];
        labels[0] = new GUIContent("Custom Property");
        for (int i = 0; i < PropertySuggestions.Length; i++)
            labels[i + 1] = new GUIContent($"{PropertySuggestions[i].PropertyPath}    [{PropertySuggestions[i].DisplayName}]");

        return labels;
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

    private static string EvaluateRegex(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.Empty;

        try
        {
            return Regex.IsMatch(text ?? string.Empty, pattern) ? "Match" : "No Match";
        }
        catch (ArgumentException)
        {
            return "Invalid";
        }
    }

    private static string BuildWritableStatus()
    {
        string fullPath = Path.GetFullPath(ProjectAssetImportRuleSet.DefaultAssetPath);
        if (!File.Exists(fullPath))
            return $"These settings are saved in {ProjectAssetImportRuleSet.DefaultAssetPath}.";

        FileAttributes attributes = File.GetAttributes(fullPath);
        string writableState = (attributes & FileAttributes.ReadOnly) == 0 ? "currently writable" : "read-only";
        return $"These settings are saved in {ProjectAssetImportRuleSet.DefaultAssetPath}, which is {writableState}.";
    }

    private static Color HeaderColor(float strength)
    {
        if (EditorGUIUtility.isProSkin)
            return new Color(strength * 0.2f, strength * 0.2f, strength * 0.2f, 1f);

        return new Color(0.68f + strength * 0.08f, 0.68f + strength * 0.08f, 0.68f + strength * 0.08f, 1f);
    }

    private static GUIStyle HeaderTitleStyle()
    {
        GUIStyle style = new GUIStyle(EditorStyles.largeLabel)
        {
            fontSize = 20,
            fontStyle = FontStyle.Normal
        };
        return style;
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

    private readonly struct PropertySuggestion
    {
        public readonly string PropertyPath;
        public readonly string DisplayName;
        public readonly ProjectAssetPropertyValueKind ValueKind;
        public readonly string DefaultValue;

        public PropertySuggestion(
            string propertyPath,
            string displayName,
            ProjectAssetPropertyValueKind valueKind,
            string defaultValue)
        {
            PropertyPath = propertyPath;
            DisplayName = displayName;
            ValueKind = valueKind;
            DefaultValue = defaultValue;
        }
    }
}

[CustomEditor(typeof(ProjectAssetImportRuleSet))]
internal sealed class ProjectAssetImportRuleSetInspector : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Use the Asset Processor panel to maintain filters and importer property items.",
            MessageType.Info);

        if (GUILayout.Button("Open Asset Processor"))
            ProjectAssetImportRuleSetWindow.Open((ProjectAssetImportRuleSet)target);

        EditorGUILayout.Space(6f);
        DrawDefaultInspector();
    }
}
