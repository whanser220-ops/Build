using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class ProjectAssetImportRuleSetWindow : EditorWindow
{
    private const string MenuPath = "Tools/Asset Import/Asset Processor";
    private const float LabelWidth = 178f;
    private const float RowHeight = 22f;
    private const float SmallButtonWidth = 26f;
    private const float RuleProcessorButtonWidth = 28f;

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

        Rect foldoutRect = new Rect(header.x + 6f, header.y + 1f, header.width - 244f, header.height);
        _ruleExpanded[index] = EditorGUI.Foldout(foldoutRect, _ruleExpanded[index], BuildRuleSummary(rule), true);

        Rect processorButtons = new Rect(header.xMax - 234f, header.y + 1f, 84f, header.height - 2f);
        DrawRuleProcessorButtons(processorButtons, index, enabled.boolValue);

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

    private void DrawRuleProcessorButtons(Rect rect, int index, bool enabled)
    {
        Rect listRect = new Rect(rect.x, rect.y, RuleProcessorButtonWidth, rect.height);
        Rect checkRect = new Rect(listRect.xMax, rect.y, RuleProcessorButtonWidth, rect.height);
        Rect applyRect = new Rect(checkRect.xMax, rect.y, RuleProcessorButtonWidth, rect.height);

        using (new EditorGUI.DisabledScope(!enabled))
        {
            if (GUI.Button(listRect, new GUIContent("列", "列出符合当前规则筛选条件的资产"), EditorStyles.miniButtonLeft))
            {
                RunRuleProcessorAction(index, ProjectAssetProcessorAction.List);
                GUIUtility.ExitGUI();
            }

            if (GUI.Button(checkRect, new GUIContent("查", "检查会被当前规则修改的属性"), EditorStyles.miniButtonMid))
            {
                RunRuleProcessorAction(index, ProjectAssetProcessorAction.Check);
                GUIUtility.ExitGUI();
            }

            if (GUI.Button(applyRect, new GUIContent("改", "应用当前规则并列出修改结果"), EditorStyles.miniButtonRight))
            {
                RunRuleProcessorAction(index, ProjectAssetProcessorAction.Apply);
                GUIUtility.ExitGUI();
            }
        }
    }

    private void RunRuleProcessorAction(int ruleIndex, ProjectAssetProcessorAction action)
    {
        CommitSerializedRuleSet();
        if (_ruleSet == null || _ruleSet.rules == null || ruleIndex < 0 || ruleIndex >= _ruleSet.rules.Length)
            return;

        ProjectAssetImportRule rule = _ruleSet.rules[ruleIndex];
        string report = ProjectAssetProcessorRunner.Run(_ruleSet, rule, action);
        ProjectAssetProcessorReportWindow.ShowReport(ResolveReportTitle(action, rule), report);
    }

    private void CommitSerializedRuleSet()
    {
        if (_serializedRuleSet == null)
            return;

        _serializedRuleSet.ApplyModifiedProperties();
        _ruleSet.EnsureMigrated();
        EditorUtility.SetDirty(_ruleSet);
    }

    private static string ResolveReportTitle(ProjectAssetProcessorAction action, ProjectAssetImportRule rule)
    {
        string ruleName = string.IsNullOrWhiteSpace(rule?.name) ? "Unnamed Rule" : rule.name;
        return action switch
        {
            ProjectAssetProcessorAction.List => $"匹配资产 - {ruleName}",
            ProjectAssetProcessorAction.Check => $"检查报告 - {ruleName}",
            ProjectAssetProcessorAction.Apply => $"应用报告 - {ruleName}",
            _ => ruleName
        };
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

internal enum ProjectAssetProcessorAction
{
    List,
    Check,
    Apply
}

internal static class ProjectAssetProcessorRunner
{
    private const string SpecialWriteOnlyClipName = "modelimporter.clipnamefromasset";

    public static string Run(ProjectAssetImportRuleSet ruleSet, ProjectAssetImportRule rule, ProjectAssetProcessorAction action)
    {
        StringBuilder report = new StringBuilder();
        string ruleName = string.IsNullOrWhiteSpace(rule?.name) ? "Unnamed Rule" : rule.name;
        AppendHeader(report, action, ruleName);

        if (ruleSet == null || rule == null)
        {
            report.AppendLine("规则不存在。");
            return report.ToString();
        }

        List<ProjectAssetRuleContext> matches = FindMatchingAssets(rule, report);
        report.AppendLine($"匹配资产数量: {matches.Count}");
        report.AppendLine();

        if (action == ProjectAssetProcessorAction.List)
        {
            AppendAssetList(report, matches);
            return report.ToString();
        }

        if (action == ProjectAssetProcessorAction.Apply &&
            !EditorUtility.DisplayDialog(
                "应用资产导入规则",
                $"将对 {matches.Count} 个匹配资产应用规则 \"{ruleName}\"。继续？",
                "应用",
                "取消"))
        {
            report.AppendLine("用户取消应用。");
            return report.ToString();
        }

        int changedAssetCount = 0;
        int changedPropertyCount = 0;
        int appliedAssetCount = 0;
        int skippedPropertyCount = 0;

        try
        {
            for (int i = 0; i < matches.Count; i++)
            {
                ProjectAssetRuleContext context = matches[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                    ResolveProgressTitle(action),
                    context.assetPath,
                    matches.Count == 0 ? 1f : (float)i / matches.Count))
                {
                    report.AppendLine("用户取消执行。");
                    break;
                }

                ProjectAssetPropertyItem[] targetItems = ruleSet
                    .BuildRuleImportSettings(rule, context)
                    .ToPropertyItems();
                List<ProjectAssetProcessorPropertyChange> changes = BuildPropertyChanges(context.importer, targetItems);

                if (changes.Count == 0)
                    continue;

                changedAssetCount++;
                changedPropertyCount += CountApplicableChanges(changes);
                skippedPropertyCount += CountSkippedChanges(changes);
                AppendChanges(report, context.assetPath, changes, action);

                if (action == ProjectAssetProcessorAction.Apply)
                {
                    ProjectAssetPropertyItem[] itemsToApply = BuildItemsToApply(changes);
                    if (itemsToApply.Length == 0)
                        continue;

                    try
                    {
                        ProjectAssetPropertyApplier.Apply(context.importer, context.assetPath, itemsToApply);
                        context.importer.SaveAndReimport();
                        appliedAssetCount++;
                    }
                    catch (Exception exception)
                    {
                        report.AppendLine($"  [ERROR] 应用失败: {exception.Message}");
                        report.AppendLine();
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        report.AppendLine();
        report.AppendLine("Summary");
        report.AppendLine($"需要修改的资产: {changedAssetCount}");
        report.AppendLine($"需要修改的属性: {changedPropertyCount}");
        if (skippedPropertyCount > 0)
            report.AppendLine($"无法自动检查/应用的属性: {skippedPropertyCount}");
        if (action == ProjectAssetProcessorAction.Apply)
            report.AppendLine($"已应用资产: {appliedAssetCount}");

        return report.ToString();
    }

    private static List<ProjectAssetRuleContext> FindMatchingAssets(ProjectAssetImportRule rule, StringBuilder report)
    {
        List<ProjectAssetRuleContext> matches = new List<ProjectAssetRuleContext>();
        string[] assetPaths = AssetDatabase.GetAllAssetPaths();

        try
        {
            for (int i = 0; i < assetPaths.Length; i++)
            {
                string assetPath = NormalizeAssetPath(assetPaths[i]);
                if (i % 64 == 0 &&
                    EditorUtility.DisplayCancelableProgressBar(
                        "扫描匹配资产",
                        assetPath,
                        assetPaths.Length == 0 ? 1f : (float)i / assetPaths.Length))
                {
                    report.AppendLine("用户取消扫描。");
                    break;
                }

                if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                    AssetDatabase.IsValidFolder(assetPath))
                    continue;

                AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                if (importer == null)
                    continue;

                ProjectAssetRuleContext context = ProjectAssetRuleContext.FromImporter(assetPath, importer);
                if (rule.Matches(context))
                    matches.Add(context);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        return matches;
    }

    private static List<ProjectAssetProcessorPropertyChange> BuildPropertyChanges(
        AssetImporter importer,
        ProjectAssetPropertyItem[] targetItems)
    {
        List<ProjectAssetProcessorPropertyChange> changes = new List<ProjectAssetProcessorPropertyChange>();
        if (importer == null || targetItems == null)
            return changes;

        foreach (ProjectAssetPropertyItem item in targetItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.propertyPath))
                continue;

            if (TryReadImporterProperty(importer, item, out string currentValue, out string readError))
            {
                if (!ValuesEqual(item, currentValue, item.value))
                {
                    changes.Add(new ProjectAssetProcessorPropertyChange
                    {
                        item = item,
                        currentValue = currentValue,
                        targetValue = item.value,
                        canApply = true
                    });
                }

                continue;
            }

            if (CanApplyWithoutRead(item))
            {
                changes.Add(new ProjectAssetProcessorPropertyChange
                {
                    item = item,
                    currentValue = "(special)",
                    targetValue = item.value,
                    canApply = true,
                    note = readError
                });
                continue;
            }

            changes.Add(new ProjectAssetProcessorPropertyChange
            {
                item = item,
                currentValue = "(unknown)",
                targetValue = item.value,
                canApply = false,
                note = readError
            });
        }

        return changes;
    }

    private static bool TryReadImporterProperty(
        AssetImporter importer,
        ProjectAssetPropertyItem item,
        out string value,
        out string error)
    {
        value = null;
        error = null;

        string normalized = NormalizePropertyKey(item.propertyPath);
        if (string.Equals(normalized, SpecialWriteOnlyClipName, StringComparison.OrdinalIgnoreCase))
        {
            error = "特殊操作：根据资源名修正 Animation Clip 名称，无法直接读取当前值。";
            return false;
        }

        if (string.Equals(normalized, "modelimporter.extrauserproperties", StringComparison.OrdinalIgnoreCase) &&
            importer is ModelImporter modelImporter)
        {
            value = string.Join(";", modelImporter.extraUserProperties ?? Array.Empty<string>());
            return true;
        }

        if (TryReadPublicImporterProperty(importer, item, "ModelImporter", out value) ||
            TryReadPublicImporterProperty(importer, item, "TextureImporter", out value))
            return true;

        SerializedObject serializedObject = new SerializedObject(importer);
        SerializedProperty property = serializedObject.FindProperty(item.propertyPath);
        if (property == null)
        {
            error = "找不到 importer 属性。";
            return false;
        }

        return TryReadSerializedProperty(property, out value, out error);
    }

    private static bool TryReadPublicImporterProperty(
        AssetImporter importer,
        ProjectAssetPropertyItem item,
        string prefix,
        out string value)
    {
        value = null;
        string propertyPath = item.propertyPath?.Trim();
        string prefixWithDot = prefix + ".";
        if (string.IsNullOrWhiteSpace(propertyPath) ||
            !propertyPath.StartsWith(prefixWithDot, StringComparison.OrdinalIgnoreCase))
            return false;

        string propertyName = propertyPath.Substring(prefixWithDot.Length);
        PropertyInfo property = importer.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property == null || property.GetMethod == null)
            return false;

        object rawValue = property.GetValue(importer);
        value = ConvertValueToReportString(rawValue);
        return true;
    }

    private static bool TryReadSerializedProperty(SerializedProperty property, out string value, out string error)
    {
        error = null;
        switch (property.propertyType)
        {
            case SerializedPropertyType.Boolean:
                value = property.boolValue ? "true" : "false";
                return true;
            case SerializedPropertyType.Integer:
                value = property.intValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case SerializedPropertyType.Float:
                value = property.floatValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case SerializedPropertyType.String:
                value = property.stringValue ?? string.Empty;
                return true;
            case SerializedPropertyType.Enum:
                value = property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                    ? property.enumNames[property.enumValueIndex]
                    : property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                value = null;
                error = $"不支持读取的 serialized property 类型: {property.propertyType}";
                return false;
        }
    }

    private static bool ValuesEqual(ProjectAssetPropertyItem item, string currentValue, string targetValue)
    {
        string normalized = NormalizePropertyKey(item.propertyPath);
        if (string.Equals(normalized, "modelimporter.extrauserproperties", StringComparison.OrdinalIgnoreCase))
            return ContainsAllTokens(currentValue, targetValue);

        switch (item.valueKind)
        {
            case ProjectAssetPropertyValueKind.Bool:
                if (TryParseBool(currentValue, out bool currentBool) && TryParseBool(targetValue, out bool targetBool))
                    return currentBool == targetBool;
                break;
            case ProjectAssetPropertyValueKind.Int:
                if (int.TryParse(currentValue, out int currentInt) && int.TryParse(targetValue, out int targetInt))
                    return currentInt == targetInt;
                break;
            case ProjectAssetPropertyValueKind.Float:
                if (TryParseFloat(currentValue, out float currentFloat) && TryParseFloat(targetValue, out float targetFloat))
                    return Mathf.Abs(currentFloat - targetFloat) <= 0.0001f;
                break;
            case ProjectAssetPropertyValueKind.Enum:
                return string.Equals(currentValue, targetValue, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(currentValue ?? string.Empty, targetValue ?? string.Empty, StringComparison.Ordinal);
    }

    private static bool ContainsAllTokens(string currentValue, string requiredValue)
    {
        string[] currentTokens = SplitTokens(currentValue);
        foreach (string requiredToken in SplitTokens(requiredValue))
        {
            bool found = false;
            foreach (string currentToken in currentTokens)
            {
                if (string.Equals(currentToken, requiredToken, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    private static ProjectAssetPropertyItem[] BuildItemsToApply(List<ProjectAssetProcessorPropertyChange> changes)
    {
        List<ProjectAssetPropertyItem> items = new List<ProjectAssetPropertyItem>();
        foreach (ProjectAssetProcessorPropertyChange change in changes)
        {
            if (change.canApply && change.item != null)
                items.Add(change.item);
        }

        return items.ToArray();
    }

    private static int CountApplicableChanges(List<ProjectAssetProcessorPropertyChange> changes)
    {
        int count = 0;
        foreach (ProjectAssetProcessorPropertyChange change in changes)
        {
            if (change.canApply)
                count++;
        }

        return count;
    }

    private static int CountSkippedChanges(List<ProjectAssetProcessorPropertyChange> changes)
    {
        int count = 0;
        foreach (ProjectAssetProcessorPropertyChange change in changes)
        {
            if (!change.canApply)
                count++;
        }

        return count;
    }

    private static void AppendHeader(StringBuilder report, ProjectAssetProcessorAction action, string ruleName)
    {
        report.AppendLine("Asset Processor Report");
        report.AppendLine($"Action: {action}");
        report.AppendLine($"Rule: {ruleName}");
        report.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(new string('-', 72));
    }

    private static void AppendAssetList(StringBuilder report, List<ProjectAssetRuleContext> matches)
    {
        if (matches.Count == 0)
        {
            report.AppendLine("没有资产符合当前规则。");
            return;
        }

        foreach (ProjectAssetRuleContext context in matches)
            report.AppendLine(context.assetPath);
    }

    private static void AppendChanges(
        StringBuilder report,
        string assetPath,
        List<ProjectAssetProcessorPropertyChange> changes,
        ProjectAssetProcessorAction action)
    {
        report.AppendLine(assetPath);
        foreach (ProjectAssetProcessorPropertyChange change in changes)
        {
            string state = change.canApply
                ? action == ProjectAssetProcessorAction.Apply ? "APPLIED" : "WILL MODIFY"
                : "SKIPPED";
            report.Append("  [")
                .Append(state)
                .Append("] ")
                .Append(change.item.propertyPath)
                .Append(": ")
                .Append(change.currentValue)
                .Append(" -> ")
                .AppendLine(change.targetValue);

            if (!string.IsNullOrWhiteSpace(change.note))
                report.AppendLine("    Note: " + change.note);
        }

        report.AppendLine();
    }

    private static string ResolveProgressTitle(ProjectAssetProcessorAction action)
    {
        return action == ProjectAssetProcessorAction.Apply ? "应用资产导入规则" : "检查资产导入规则";
    }

    private static bool CanApplyWithoutRead(ProjectAssetPropertyItem item)
    {
        return string.Equals(NormalizePropertyKey(item.propertyPath), SpecialWriteOnlyClipName, StringComparison.OrdinalIgnoreCase) &&
            TryParseBool(item.value, out bool enabled) &&
            enabled;
    }

    private static string ConvertValueToReportString(object rawValue)
    {
        if (rawValue == null)
            return string.Empty;

        if (rawValue is bool boolValue)
            return boolValue ? "true" : "false";
        if (rawValue is float floatValue)
            return floatValue.ToString(CultureInfo.InvariantCulture);
        if (rawValue is double doubleValue)
            return doubleValue.ToString(CultureInfo.InvariantCulture);
        if (rawValue is int intValue)
            return intValue.ToString(CultureInfo.InvariantCulture);
        if (rawValue is Array array)
        {
            List<string> values = new List<string>();
            foreach (object item in array)
                values.Add(ConvertValueToReportString(item));

            return string.Join(";", values);
        }

        return Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool TryParseBool(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
            return true;
        if (int.TryParse(value, out int intValue))
        {
            result = intValue != 0;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
            float.TryParse(value, out result);
    }

    private static string[] SplitTokens(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return (assetPath ?? string.Empty).Replace('\\', '/');
    }

    private static string NormalizePropertyKey(string propertyPath)
    {
        return (propertyPath ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }
}

internal sealed class ProjectAssetProcessorPropertyChange
{
    public ProjectAssetPropertyItem item;
    public string currentValue;
    public string targetValue;
    public bool canApply;
    public string note;
}

public sealed class ProjectAssetProcessorReportWindow : EditorWindow
{
    private string _report;
    private Vector2 _scroll;

    public static void ShowReport(string title, string report)
    {
        ProjectAssetProcessorReportWindow window = GetWindow<ProjectAssetProcessorReportWindow>("Asset Processor Report");
        window.titleContent = new GUIContent(string.IsNullOrWhiteSpace(title) ? "Asset Processor Report" : title);
        window._report = report ?? string.Empty;
        window._scroll = Vector2.zero;
        window.minSize = new Vector2(720f, 420f);
        window.Show();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("复制报告", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                GUIUtility.systemCopyBuffer = _report ?? string.Empty;

            GUILayout.FlexibleSpace();
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.TextArea(_report ?? string.Empty, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
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
