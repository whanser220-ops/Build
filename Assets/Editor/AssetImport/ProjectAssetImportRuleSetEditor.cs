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

    private ProjectAssetImportRuleSet _ruleSet;
    private SerializedObject _serializedRuleSet;
    private Vector2 _scroll;
    private bool _ruleItemsExpanded = true;
    private bool _previewExpanded = true;
    private bool[] _ruleExpanded = Array.Empty<bool>();
    private int[] _ruleTabIndices = Array.Empty<int>();
    private string _previewAssetPath = "Assets/GameResources/Stylized Pack - Meadow Environment/Sources/Meshes/zzz.fbx";
    private ProjectAssetClass _previewAssetClass = ProjectAssetClass.Model;

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
        window.minSize = new Vector2(920f, 620f);
        window.SetRuleSet(ruleSet != null ? ruleSet : LoadOrCreateDefaultAsset());
        window.Show();
    }

    private void OnEnable()
    {
        minSize = new Vector2(920f, 620f);
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
        {
            _ruleSet.EnsureMigrated();
            EditorUtility.SetDirty(_ruleSet);
        }

        DrawPreviewPanel();
    }

    private void SetRuleSet(ProjectAssetImportRuleSet ruleSet)
    {
        _ruleSet = ruleSet;
        if (_ruleSet != null)
        {
            _ruleSet.EnsureMigrated();
            EditorUtility.SetDirty(_ruleSet);
        }

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
            DrawPropertyRow("规则名称", name);
            DrawPropertyRow("启用", enabled);

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
            DrawImportSettingsPanel(rule, filter.FindPropertyRelative("assetClass"), index);
        }
    }

    private void DrawImportSettingsPanel(SerializedProperty rule, SerializedProperty assetClass, int ruleIndex)
    {
        ProjectAssetClass currentClass = (ProjectAssetClass)assetClass.enumValueIndex;
        string[] tabs = ProjectAssetImportSettingCatalog.GetTabs(currentClass);
        _ruleTabIndices[ruleIndex] = Mathf.Clamp(_ruleTabIndices[ruleIndex], 0, tabs.Length - 1);

        Rect tabRect = EditorGUILayout.GetControlRect(false, 26f);
        float indentOffset = EditorGUI.indentLevel * 15f;
        tabRect.x += indentOffset + LabelWidth;
        tabRect.width -= indentOffset + LabelWidth + 8f;
        tabRect.width = Mathf.Min(tabRect.width, tabs.Length * 96f);
        _ruleTabIndices[ruleIndex] = GUI.Toolbar(tabRect, _ruleTabIndices[ruleIndex], tabs);

        string tab = tabs[_ruleTabIndices[ruleIndex]];
        using (new EditorGUI.IndentLevelScope())
        {
            if (string.Equals(tab, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                DrawCustomSettings(rule.FindPropertyRelative("customImportSettings").FindPropertyRelative("propertyItems"));
                return;
            }

            DrawSettingDefinitions(rule, ProjectAssetImportSettingCatalog.GetDefinitions(currentClass, tab));
        }
    }

    private static void DrawSettingDefinitions(SerializedProperty rule, ProjectAssetImportSettingDefinition[] definitions)
    {
        foreach (ProjectAssetImportSettingDefinition definition in definitions)
        {
            if (!ShouldDrawDefinition(rule, definition))
                continue;

            if (definition.isSection)
            {
                DrawSettingSection(definition.label);
                continue;
            }

            DrawImportSettingRow(rule, definition);
        }
    }

    private static bool ShouldDrawDefinition(SerializedProperty rule, ProjectAssetImportSettingDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.visibleWhenSettingPath))
            return true;

        SerializedProperty dependency = FindRelative(rule, definition.visibleWhenSettingPath);
        if (dependency == null)
            return true;

        SerializedProperty dependencyOverride = dependency.FindPropertyRelative("overrideEnabled");
        SerializedProperty dependencyValue = dependency.FindPropertyRelative("value");
        ProjectAssetImportSettingDefinition dependencyDefinition =
            ProjectAssetImportSettingCatalog.FindBySettingPath(definition.visibleWhenSettingPath);

        string currentValue = dependencyOverride != null && dependencyOverride.boolValue
            ? dependencyValue?.stringValue
            : dependencyDefinition?.defaultValue;

        if (string.IsNullOrEmpty(currentValue))
            currentValue = dependencyDefinition?.defaultValue ?? string.Empty;

        foreach (string acceptedValue in definition.visibleWhenValues)
        {
            if (string.Equals(currentValue, acceptedValue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void DrawImportSettingRow(SerializedProperty rule, ProjectAssetImportSettingDefinition definition)
    {
        SerializedProperty setting = FindRelative(rule, definition.settingPath);
        if (setting == null)
            return;

        SerializedProperty overrideEnabled = setting.FindPropertyRelative("overrideEnabled");
        SerializedProperty valueKind = setting.FindPropertyRelative("valueKind");
        SerializedProperty value = setting.FindPropertyRelative("value");

        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset;
        rect.width -= indentOffset;

        Rect overrideRect = new Rect(rect.x + 18f, rect.y + 1f, LabelWidth - 24f, rect.height - 2f);
        Rect valueRect = new Rect(rect.x + LabelWidth, rect.y + 1f, rect.width - LabelWidth - 10f, rect.height - 2f);

        bool wasEnabled = overrideEnabled.boolValue;
        bool isEnabled = EditorGUI.ToggleLeft(
            overrideRect,
            new GUIContent(definition.label, "勾选后当前规则会覆盖这个导入字段"),
            wasEnabled);

        if (isEnabled != wasEnabled)
        {
            overrideEnabled.boolValue = isEnabled;
            valueKind.enumValueIndex = (int)definition.valueKind;
            if (isEnabled && string.IsNullOrEmpty(value.stringValue))
                value.stringValue = definition.defaultValue;
        }

        using (new EditorGUI.DisabledScope(!isEnabled))
        {
            string currentValue = isEnabled ? value.stringValue : definition.defaultValue;
            string nextValue = DrawDefinitionValue(valueRect, definition, currentValue);
            if (isEnabled)
            {
                valueKind.enumValueIndex = (int)definition.valueKind;
                value.stringValue = nextValue;
            }
        }
    }

    private static string DrawDefinitionValue(Rect rect, ProjectAssetImportSettingDefinition definition, string currentValue)
    {
        switch (definition.valueKind)
        {
            case ProjectAssetPropertyValueKind.Bool:
                return EditorGUI.Toggle(rect, ParseBool(currentValue)) ? "true" : "false";
            case ProjectAssetPropertyValueKind.Int:
                return EditorGUI.IntField(rect, ParseInt(currentValue)).ToString(CultureInfo.InvariantCulture);
            case ProjectAssetPropertyValueKind.Float:
                return EditorGUI.FloatField(rect, ParseFloat(currentValue)).ToString(CultureInfo.InvariantCulture);
            case ProjectAssetPropertyValueKind.Enum:
                int optionIndex = ResolveOptionIndex(definition.options, currentValue, definition.defaultValue);
                int nextIndex = EditorGUI.Popup(rect, optionIndex, definition.options);
                return definition.options[Mathf.Clamp(nextIndex, 0, definition.options.Length - 1)];
            case ProjectAssetPropertyValueKind.String:
            default:
                return EditorGUI.TextField(rect, currentValue ?? string.Empty);
        }
    }

    private static void DrawCustomSettings(SerializedProperty propertyItems)
    {
        if (propertyItems == null)
            return;

        for (int i = 0; i < propertyItems.arraySize; i++)
            DrawCustomPropertyRow(propertyItems, i);

        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
        float indentOffset = EditorGUI.indentLevel * 15f;
        rect.x += indentOffset + LabelWidth;
        rect.width = 126f;

        if (GUI.Button(rect, propertyItems.arraySize == 0 ? "添加 Custom" : "继续添加", EditorStyles.miniButton))
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

    private void DrawPreviewPanel()
    {
        if (!DrawSectionHeader("Effective Import Settings Preview", ref _previewExpanded))
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            _previewAssetPath = EditorGUILayout.TextField("资产路径", _previewAssetPath);
            _previewAssetClass = (ProjectAssetClass)EditorGUILayout.EnumPopup("资产类型", _previewAssetClass);

            ProjectAssetRuleContext context = CreatePreviewContext(_previewAssetPath, _previewAssetClass);
            ProjectEffectiveImportSettings effective = _ruleSet.BuildEffectiveImportSettings(context);
            DrawMatchedRules(effective);
            DrawEffectiveSettings(effective);
        }
    }

    private static void DrawMatchedRules(ProjectEffectiveImportSettings effective)
    {
        StringBuilder builder = new StringBuilder();
        ProjectAssetImportRule[] rules = effective?.matchedRules ?? Array.Empty<ProjectAssetImportRule>();
        for (int i = 0; i < rules.Length; i++)
        {
            if (i > 0)
                builder.Append("  >  ");

            builder.Append(string.IsNullOrWhiteSpace(rules[i]?.name) ? "Unnamed Rule" : rules[i].name);
        }

        EditorGUILayout.LabelField("命中规则", rules.Length == 0 ? "无" : builder.ToString());
    }

    private static void DrawEffectiveSettings(ProjectEffectiveImportSettings effective)
    {
        ProjectEffectiveImportSetting[] settings = effective?.settings ?? Array.Empty<ProjectEffectiveImportSetting>();
        EditorGUILayout.LabelField("最终字段", settings.Length == 0 ? "无启用字段" : $"{settings.Length} 项");

        using (new EditorGUI.IndentLevelScope())
        {
            for (int i = 0; i < settings.Length; i++)
            {
                ProjectEffectiveImportSetting setting = settings[i];
                Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
                float indentOffset = EditorGUI.indentLevel * 15f;
                rect.x += indentOffset;
                rect.width -= indentOffset;

                Rect pathRect = new Rect(rect.x + 18f, rect.y + 2f, Mathf.Min(300f, rect.width * 0.42f), rect.height - 4f);
                Rect valueRect = new Rect(pathRect.xMax + 8f, rect.y + 2f, Mathf.Min(180f, rect.width * 0.24f), rect.height - 4f);
                Rect sourceRect = new Rect(valueRect.xMax + 8f, rect.y + 2f, rect.xMax - valueRect.xMax - 12f, rect.height - 4f);
                EditorGUI.LabelField(pathRect, setting.propertyPath);
                EditorGUI.LabelField(valueRect, setting.value);
                EditorGUI.LabelField(sourceRect, setting.sourceRuleName);
            }
        }
    }

    private static ProjectAssetRuleContext CreatePreviewContext(string assetPath, ProjectAssetClass assetClass)
    {
        string normalizedPath = (assetPath ?? string.Empty).Replace('\\', '/');
        return new ProjectAssetRuleContext
        {
            assetPath = normalizedPath,
            directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? string.Empty,
            packageName = Path.GetFileNameWithoutExtension(normalizedPath),
            assetClass = assetClass
        };
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
        SetKnownSetting(rule, "ModelImporter.animationType", "None");
        SetKnownSetting(rule, "ModelImporter.importAnimation", "false");
        SetKnownSetting(rule, "ModelImporter.importBlendShapes", "false");
        SetKnownSetting(rule, "ModelImporter.addCollider", "true");
        SetKnownSetting(rule, "ModelImporter.generateSecondaryUV", "true");
    }

    private void AddCharacterModelRule()
    {
        SerializedProperty rule = AddRule("Character Model Rule", ProjectAssetClass.Model);
        SerializedProperty filter = rule.FindPropertyRelative("filter");
        filter.FindPropertyRelative("directoryMatch").enumValueIndex = (int)ProjectAssetStringMatchMode.Contains;
        filter.FindPropertyRelative("directoryPattern").stringValue = "Characters/";

        SetKnownSetting(rule, "ModelImporter.animationType", "Human");
        SetKnownSetting(rule, "ModelImporter.avatarSetup", "CreateFromThisModel");
        SetKnownSetting(rule, "ModelImporter.importAnimation", "true");
        SetKnownSetting(rule, "ModelImporter.importBlendShapes", "true");
        SetKnownSetting(rule, "ModelImporter.meshCompression", "Off");
    }

    private void AddNormalTextureRule()
    {
        SerializedProperty rule = AddRule("Normal Texture Rule", ProjectAssetClass.Texture2D);
        SerializedProperty filter = rule.FindPropertyRelative("filter");
        filter.FindPropertyRelative("packageNameMatch").enumValueIndex = (int)ProjectAssetStringMatchMode.EndsWith;
        filter.FindPropertyRelative("packageNamePattern").stringValue = "_N";

        SetKnownSetting(rule, "TextureImporter.textureType", "NormalMap");
        SetKnownSetting(rule, "TextureImporter.wrapModeU", "Clamp");
        SetKnownSetting(rule, "TextureImporter.wrapModeV", "Clamp");
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
        _ruleSet.EnsureMigrated();
        EditorUtility.SetDirty(_ruleSet);
        return rules.GetArrayElementAtIndex(index);
    }

    private static void InitializeRule(SerializedProperty rule, string ruleName, ProjectAssetClass assetClass)
    {
        rule.FindPropertyRelative("name").stringValue = ruleName;
        rule.FindPropertyRelative("enabled").boolValue = true;
        rule.FindPropertyRelative("legacyPropertyItemsMigrated").boolValue = true;

        SerializedProperty filter = rule.FindPropertyRelative("filter");
        filter.FindPropertyRelative("directoryMatch").enumValueIndex = (int)ProjectAssetStringMatchMode.Any;
        filter.FindPropertyRelative("directoryPattern").stringValue = string.Empty;
        filter.FindPropertyRelative("packageNameMatch").enumValueIndex = (int)ProjectAssetStringMatchMode.Any;
        filter.FindPropertyRelative("packageNamePattern").stringValue = string.Empty;
        filter.FindPropertyRelative("assetClass").enumValueIndex = (int)assetClass;

        foreach (ProjectAssetImportSettingDefinition definition in ProjectAssetImportSettingCatalog.GetDefinitionsForAssetClass(ProjectAssetClass.Any))
            ClearKnownSetting(rule, definition);

        rule.FindPropertyRelative("customImportSettings").FindPropertyRelative("propertyItems").arraySize = 0;
        rule.FindPropertyRelative("propertyItems").arraySize = 0;
    }

    private static void ClearKnownSetting(SerializedProperty rule, ProjectAssetImportSettingDefinition definition)
    {
        SerializedProperty setting = FindRelative(rule, definition.settingPath);
        if (setting == null)
            return;

        setting.FindPropertyRelative("overrideEnabled").boolValue = false;
        setting.FindPropertyRelative("valueKind").enumValueIndex = (int)definition.valueKind;
        setting.FindPropertyRelative("value").stringValue = definition.defaultValue;
    }

    private static void SetKnownSetting(SerializedProperty rule, string propertyPath, string value)
    {
        ProjectAssetImportSettingDefinition definition = ProjectAssetImportSettingCatalog.FindByPropertyPath(propertyPath);
        if (definition == null)
            return;

        SerializedProperty setting = FindRelative(rule, definition.settingPath);
        if (setting == null)
            return;

        setting.FindPropertyRelative("overrideEnabled").boolValue = true;
        setting.FindPropertyRelative("valueKind").enumValueIndex = (int)definition.valueKind;
        setting.FindPropertyRelative("value").stringValue = value;
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

    private static SerializedProperty FindRelative(SerializedProperty root, string relativePath)
    {
        if (root == null || string.IsNullOrWhiteSpace(relativePath))
            return null;

        SerializedProperty current = root;
        string[] segments = relativePath.Split('.');
        foreach (string segment in segments)
        {
            current = current.FindPropertyRelative(segment);
            if (current == null)
                return null;
        }

        return current;
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
        {
            ruleSet.EnsureMigrated();
            EditorUtility.SetDirty(ruleSet);
            return ruleSet;
        }

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
}

[CustomEditor(typeof(ProjectAssetImportRuleSet))]
internal sealed class ProjectAssetImportRuleSetInspector : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Open Asset Processor"))
            ProjectAssetImportRuleSetWindow.Open((ProjectAssetImportRuleSet)target);

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox("请通过 Asset Processor 面板编辑规则。", MessageType.Info);
    }
}
