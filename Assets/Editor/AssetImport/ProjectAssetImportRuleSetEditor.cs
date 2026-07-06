using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public sealed class ProjectAssetImportRuleSetWindow : EditorWindow
{
    private const string MenuPath = "Tools/Asset Import/Asset Import Rules";
    private const float SidebarWidth = 340f;
    private const float CompactButtonWidth = 92f;

    private static readonly PropertySuggestion[] PropertySuggestions =
    {
        new PropertySuggestion("Model/Import Cameras", "ModelImporter.importCameras", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("Model/Import Lights", "ModelImporter.importLights", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("Model/Import Visibility", "ModelImporter.importVisibility", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("Model/Sort Hierarchy", "ModelImporter.sortHierarchyByName", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("Model/Extra User Properties", "ModelImporter.extraUserProperties", ProjectAssetPropertyValueKind.String, "Collision;Collider"),
        new PropertySuggestion("Model/Preserve Hierarchy", "ModelImporter.preserveHierarchy", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("Model/Animation Type", "ModelImporter.animationType", ProjectAssetPropertyValueKind.Enum, "None"),
        new PropertySuggestion("Model/Avatar Setup", "ModelImporter.avatarSetup", ProjectAssetPropertyValueKind.Enum, "CreateFromThisModel"),
        new PropertySuggestion("Model/Import Animation", "ModelImporter.importAnimation", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("Model/Import Blend Shapes", "ModelImporter.importBlendShapes", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("Model/Add Collider", "ModelImporter.addCollider", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("Model/Generate Lightmap UV", "ModelImporter.generateSecondaryUV", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("Model/Read/Write", "ModelImporter.isReadable", ProjectAssetPropertyValueKind.Bool, "false"),
        new PropertySuggestion("Model/Mesh Compression", "ModelImporter.meshCompression", ProjectAssetPropertyValueKind.Enum, "Low"),
        new PropertySuggestion("Model/Animation Compression", "ModelImporter.animationCompression", ProjectAssetPropertyValueKind.Enum, "Optimal"),
        new PropertySuggestion("Model/Clip Name From Asset", "ModelImporter.clipNameFromAsset", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("Texture/Texture Type", "TextureImporter.textureType", ProjectAssetPropertyValueKind.Enum, "Default"),
        new PropertySuggestion("Texture/Compression", "TextureImporter.textureCompression", ProjectAssetPropertyValueKind.Enum, "Compressed"),
        new PropertySuggestion("Texture/Wrap Mode", "TextureImporter.wrapMode", ProjectAssetPropertyValueKind.Enum, "Repeat"),
        new PropertySuggestion("Texture/Address X", "TextureImporter.wrapModeU", ProjectAssetPropertyValueKind.Enum, "Clamp"),
        new PropertySuggestion("Texture/Address Y", "TextureImporter.wrapModeV", ProjectAssetPropertyValueKind.Enum, "Clamp"),
        new PropertySuggestion("Texture/Address Z", "TextureImporter.wrapModeW", ProjectAssetPropertyValueKind.Enum, "Clamp"),
        new PropertySuggestion("Texture/sRGB", "TextureImporter.sRGBTexture", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("Texture/Mip Maps", "TextureImporter.mipmapEnabled", ProjectAssetPropertyValueKind.Bool, "true"),
        new PropertySuggestion("Texture/Read/Write", "TextureImporter.isReadable", ProjectAssetPropertyValueKind.Bool, "false")
    };

    private static readonly GUIContent[] PropertySuggestionLabels = BuildPropertySuggestionLabels();

    private ProjectAssetImportRuleSet _ruleSet;
    private SerializedObject _serializedRuleSet;
    private ReorderableList _rulesList;
    private Vector2 _rightScroll;
    private string _previewAssetPath = "Assets/GameResources/Stylized Pack - Meadow Environment/Sources/Meshes/zzz.fbx";
    private ProjectAssetClass _previewAssetClass = ProjectAssetClass.Model;
    private string _previewResult = "Preview has not run yet.";

    [MenuItem(MenuPath)]
    public static void OpenDefault()
    {
        Open(LoadOrCreateDefaultAsset());
    }

    public static void Open(ProjectAssetImportRuleSet ruleSet)
    {
        ProjectAssetImportRuleSetWindow window = GetWindow<ProjectAssetImportRuleSetWindow>("Asset Import Rules");
        window.minSize = new Vector2(900f, 540f);
        window.SetRuleSet(ruleSet != null ? ruleSet : LoadOrCreateDefaultAsset());
        window.Show();
    }

    private void OnEnable()
    {
        minSize = new Vector2(900f, 540f);
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

        DrawToolbar();
        EditorGUILayout.Space(4f);
        DrawApplyMode();
        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawRuleList();
            DrawSelectedRule();
        }

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
        _rulesList = null;
        if (_serializedRuleSet != null)
            BuildRulesList();
    }

    private void BuildRulesList()
    {
        SerializedProperty rules = _serializedRuleSet.FindProperty("rules");
        _rulesList = new ReorderableList(_serializedRuleSet, rules, true, true, true, true);
        _rulesList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Rules");
        _rulesList.elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 2f + 10f;
        _rulesList.drawElementCallback = DrawRuleListElement;
        _rulesList.onAddDropdownCallback = ShowAddRuleMenu;
        _rulesList.onRemoveCallback = RemoveSelectedRule;
        _rulesList.onSelectCallback = list => list.index = Mathf.Clamp(list.index, 0, list.serializedProperty.arraySize - 1);
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Rule Set", GUILayout.Width(54f));
            ProjectAssetImportRuleSet selected = (ProjectAssetImportRuleSet)EditorGUILayout.ObjectField(
                _ruleSet,
                typeof(ProjectAssetImportRuleSet),
                false,
                GUILayout.MinWidth(220f));

            if (selected != _ruleSet)
            {
                SetRuleSet(selected);
                GUIUtility.ExitGUI();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.Width(CompactButtonWidth)))
                EditorGUIUtility.PingObject(_ruleSet);

            if (GUILayout.Button("Open Asset", EditorStyles.toolbarButton, GUILayout.Width(CompactButtonWidth)))
                Selection.activeObject = _ruleSet;

            if (GUILayout.Button("Reset Defaults", EditorStyles.toolbarButton, GUILayout.Width(112f)))
                ResetDefaultsWithPrompt();

            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(CompactButtonWidth)))
                SaveRuleSet();
        }
    }

    private void DrawApplyMode()
    {
        SerializedProperty applyAllMatchingRules = _serializedRuleSet.FindProperty("applyAllMatchingRules");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(applyAllMatchingRules, new GUIContent("Apply All Matching Rules"));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("First matching rule only when disabled.", EditorStyles.miniLabel, GUILayout.Width(220f));
        }
    }

    private void DrawRuleList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(SidebarWidth)))
        {
            _rulesList.DoLayoutList();
        }
    }

    private void DrawSelectedRule()
    {
        SerializedProperty rules = _serializedRuleSet.FindProperty("rules");
        if (rules.arraySize == 0 || _rulesList.index < 0 || _rulesList.index >= rules.arraySize)
        {
            using (new EditorGUILayout.VerticalScope())
                EditorGUILayout.HelpBox("Create or select a rule to edit its filter and property items.", MessageType.Info);
            return;
        }

        SerializedProperty rule = rules.GetArrayElementAtIndex(_rulesList.index);
        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

        EditorGUILayout.LabelField("Selected Rule", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(rule.FindPropertyRelative("enabled"));
            EditorGUILayout.PropertyField(rule.FindPropertyRelative("name"));
        }

        EditorGUILayout.Space(6f);
        DrawFilter(rule.FindPropertyRelative("filter"));

        EditorGUILayout.Space(6f);
        DrawPropertyItems(rule.FindPropertyRelative("propertyItems"));

        EditorGUILayout.Space(6f);
        DrawPreview();

        EditorGUILayout.EndScrollView();
    }

    private void DrawFilter(SerializedProperty filter)
    {
        EditorGUILayout.LabelField("Asset Filter", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawMatchRow(
                "Directory",
                filter.FindPropertyRelative("directoryMatch"),
                filter.FindPropertyRelative("directoryPattern"));
            DrawMatchRow(
                "Package Name",
                filter.FindPropertyRelative("packageNameMatch"),
                filter.FindPropertyRelative("packageNamePattern"));
            EditorGUILayout.PropertyField(filter.FindPropertyRelative("assetClass"), new GUIContent("Asset Class"));
        }
    }

    private static void DrawMatchRow(string label, SerializedProperty matchMode, SerializedProperty pattern)
    {
        Rect rect = EditorGUILayout.GetControlRect();
        float labelWidth = 104f;
        float modeWidth = 136f;
        Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
        Rect modeRect = new Rect(labelRect.xMax + 4f, rect.y, modeWidth, rect.height);
        Rect patternRect = new Rect(modeRect.xMax + 6f, rect.y, rect.width - labelWidth - modeWidth - 10f, rect.height);

        EditorGUI.LabelField(labelRect, label);
        EditorGUI.PropertyField(modeRect, matchMode, GUIContent.none);
        using (new EditorGUI.DisabledScope((ProjectAssetStringMatchMode)matchMode.enumValueIndex == ProjectAssetStringMatchMode.Any))
        {
            pattern.stringValue = EditorGUI.TextField(patternRect, pattern.stringValue);
        }
    }

    private void DrawPropertyItems(SerializedProperty propertyItems)
    {
        ReorderableList list = new ReorderableList(_serializedRuleSet, propertyItems, true, true, true, true);
        list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Property Items");
        list.elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 2f + 10f;
        list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            SerializedProperty item = propertyItems.GetArrayElementAtIndex(index);
            DrawPropertyItemElement(rect, item);
        };
        list.onAddCallback = _ =>
        {
            int index = propertyItems.arraySize;
            propertyItems.InsertArrayElementAtIndex(index);
            SerializedProperty item = propertyItems.GetArrayElementAtIndex(index);
            ApplyPropertySuggestion(item, PropertySuggestions[0]);
        };
        list.DoLayoutList();
    }

    private static void DrawPropertyItemElement(Rect rect, SerializedProperty item)
    {
        SerializedProperty propertyPath = item.FindPropertyRelative("propertyPath");
        SerializedProperty valueKind = item.FindPropertyRelative("valueKind");
        SerializedProperty value = item.FindPropertyRelative("value");

        rect.y += 2f;
        Rect firstLine = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
        Rect secondLine = new Rect(rect.x, firstLine.yMax + 4f, rect.width, EditorGUIUtility.singleLineHeight);

        float labelWidth = 62f;
        float popupWidth = Mathf.Min(220f, firstLine.width * 0.38f);
        Rect propertyLabelRect = new Rect(firstLine.x, firstLine.y, labelWidth, firstLine.height);
        Rect popupRect = new Rect(propertyLabelRect.xMax + 4f, firstLine.y, popupWidth, firstLine.height);
        Rect pathRect = new Rect(popupRect.xMax + 6f, firstLine.y, firstLine.width - labelWidth - popupWidth - 10f, firstLine.height);

        EditorGUI.LabelField(propertyLabelRect, "Property");
        int currentSuggestion = FindSuggestionIndex(propertyPath.stringValue);
        int currentPopupIndex = currentSuggestion >= 0 ? currentSuggestion + 1 : 0;
        int nextPopupIndex = EditorGUI.Popup(popupRect, currentPopupIndex, PropertySuggestionLabels);
        if (nextPopupIndex > 0 && nextPopupIndex != currentPopupIndex)
            ApplyPropertySuggestion(item, PropertySuggestions[nextPopupIndex - 1]);

        propertyPath.stringValue = EditorGUI.TextField(pathRect, propertyPath.stringValue);

        Rect valueLabelRect = new Rect(secondLine.x, secondLine.y, labelWidth, secondLine.height);
        Rect kindRect = new Rect(valueLabelRect.xMax + 4f, secondLine.y, 116f, secondLine.height);
        Rect valueRect = new Rect(kindRect.xMax + 6f, secondLine.y, secondLine.width - labelWidth - 126f, secondLine.height);

        EditorGUI.LabelField(valueLabelRect, "Value");
        EditorGUI.PropertyField(kindRect, valueKind, GUIContent.none);
        value.stringValue = EditorGUI.TextField(valueRect, value.stringValue);
    }

    private void DrawPreview()
    {
        EditorGUILayout.LabelField("Match Preview", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _previewAssetPath = EditorGUILayout.TextField("Asset Path", _previewAssetPath);
            _previewAssetClass = (ProjectAssetClass)EditorGUILayout.EnumPopup("Asset Class", _previewAssetClass);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Preview Match", GUILayout.Width(130f)))
                    _previewResult = BuildPreviewResult();
            }

            EditorGUILayout.TextArea(_previewResult, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(54f));
        }
    }

    private string BuildPreviewResult()
    {
        _serializedRuleSet.ApplyModifiedProperties();
        EditorUtility.SetDirty(_ruleSet);

        string normalizedPath = (_previewAssetPath ?? string.Empty).Replace('\\', '/');
        ProjectAssetRuleContext context = new ProjectAssetRuleContext
        {
            assetPath = normalizedPath,
            directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? string.Empty,
            packageName = Path.GetFileNameWithoutExtension(normalizedPath),
            assetClass = _previewAssetClass
        };

        ProjectAssetImportRule[] matches = _ruleSet.ResolveRules(context);
        if (matches.Length == 0)
            return "No matching rules.";

        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"Matched {matches.Length} rule(s):");
        foreach (ProjectAssetImportRule rule in matches)
        {
            builder.Append("- ").AppendLine(rule.name);
            if (rule.propertyItems == null)
                continue;

            foreach (ProjectAssetPropertyItem item in rule.propertyItems)
                builder.Append("  ").Append(item.propertyPath).Append(" = ").AppendLine(item.value);
        }

        return builder.ToString().TrimEnd();
    }

    private void DrawRuleListElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty rule = _rulesList.serializedProperty.GetArrayElementAtIndex(index);
        SerializedProperty enabled = rule.FindPropertyRelative("enabled");
        SerializedProperty name = rule.FindPropertyRelative("name");

        rect.y += 2f;
        Rect firstLine = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
        Rect secondLine = new Rect(rect.x + 22f, firstLine.yMax + 3f, rect.width - 22f, EditorGUIUtility.singleLineHeight);
        Rect toggleRect = new Rect(firstLine.x, firstLine.y, 18f, firstLine.height);
        Rect nameRect = new Rect(toggleRect.xMax + 4f, firstLine.y, firstLine.width - 22f, firstLine.height);

        enabled.boolValue = EditorGUI.Toggle(toggleRect, enabled.boolValue);
        name.stringValue = EditorGUI.TextField(nameRect, name.stringValue);
        EditorGUI.LabelField(secondLine, BuildRuleSummary(rule), EditorStyles.miniLabel);
    }

    private static string BuildRuleSummary(SerializedProperty rule)
    {
        SerializedProperty filter = rule.FindPropertyRelative("filter");
        SerializedProperty assetClass = filter.FindPropertyRelative("assetClass");
        SerializedProperty directoryMatch = filter.FindPropertyRelative("directoryMatch");
        SerializedProperty directoryPattern = filter.FindPropertyRelative("directoryPattern");
        SerializedProperty packageNameMatch = filter.FindPropertyRelative("packageNameMatch");
        SerializedProperty packageNamePattern = filter.FindPropertyRelative("packageNamePattern");
        SerializedProperty propertyItems = rule.FindPropertyRelative("propertyItems");

        StringBuilder builder = new StringBuilder();
        builder.Append("[Class: ").Append(GetEnumName(assetClass)).Append("]");
        AppendMatchSummary(builder, "Dir", directoryMatch, directoryPattern);
        AppendMatchSummary(builder, "Pkg", packageNameMatch, packageNamePattern);
        builder.Append("  Props: ").Append(propertyItems.arraySize);
        return builder.ToString();
    }

    private static void AppendMatchSummary(StringBuilder builder, string label, SerializedProperty matchMode, SerializedProperty pattern)
    {
        ProjectAssetStringMatchMode mode = (ProjectAssetStringMatchMode)matchMode.enumValueIndex;
        if (mode == ProjectAssetStringMatchMode.Any)
            return;

        builder.Append("  ")
            .Append(label)
            .Append(' ')
            .Append(GetEnumName(matchMode))
            .Append(" \"")
            .Append(pattern.stringValue)
            .Append('"');
    }

    private void ShowAddRuleMenu(Rect buttonRect, ReorderableList list)
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
        SaveRuleSet();
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
        SaveRuleSet();
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
        SaveRuleSet();
    }

    private SerializedProperty AddRule(string ruleName, ProjectAssetClass assetClass)
    {
        _serializedRuleSet.Update();
        SerializedProperty rules = _serializedRuleSet.FindProperty("rules");
        int index = rules.arraySize;
        rules.InsertArrayElementAtIndex(index);
        SerializedProperty rule = rules.GetArrayElementAtIndex(index);
        InitializeRule(rule, ruleName, assetClass);
        _rulesList.index = index;
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

        SerializedProperty propertyItems = rule.FindPropertyRelative("propertyItems");
        propertyItems.arraySize = 0;
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

    private void RemoveSelectedRule(ReorderableList list)
    {
        if (list.index < 0 || list.index >= list.serializedProperty.arraySize)
            return;

        if (!EditorUtility.DisplayDialog("Remove Rule", "Remove the selected import rule?", "Remove", "Cancel"))
            return;

        ReorderableList.defaultBehaviours.DoRemoveButton(list);
        list.index = Mathf.Clamp(list.index, 0, list.serializedProperty.arraySize - 1);
        SaveRuleSet();
    }

    private void ResetDefaultsWithPrompt()
    {
        if (!EditorUtility.DisplayDialog(
            "Reset Import Rules",
            "Replace the current rule list with the project default rules?",
            "Reset",
            "Cancel"))
            return;

        Undo.RecordObject(_ruleSet, "Reset Asset Import Rules");
        _ruleSet.applyAllMatchingRules = true;
        _ruleSet.rules = ProjectAssetImportRuleSet.CreateDefaultRules();
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

    private static GUIContent[] BuildPropertySuggestionLabels()
    {
        GUIContent[] labels = new GUIContent[PropertySuggestions.Length + 1];
        labels[0] = new GUIContent("Custom");
        for (int i = 0; i < PropertySuggestions.Length; i++)
            labels[i + 1] = new GUIContent(PropertySuggestions[i].Label);

        return labels;
    }

    private static string GetEnumName(SerializedProperty property)
    {
        if (property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length)
            return property.enumDisplayNames[property.enumValueIndex];

        return property.enumValueIndex.ToString();
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
        public readonly string Label;
        public readonly string PropertyPath;
        public readonly ProjectAssetPropertyValueKind ValueKind;
        public readonly string DefaultValue;

        public PropertySuggestion(
            string label,
            string propertyPath,
            ProjectAssetPropertyValueKind valueKind,
            string defaultValue)
        {
            Label = label;
            PropertyPath = propertyPath;
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
            "Use the rule editor to maintain asset filters and importer property items.",
            MessageType.Info);

        if (GUILayout.Button("Open Rule Editor"))
            ProjectAssetImportRuleSetWindow.Open((ProjectAssetImportRuleSet)target);

        EditorGUILayout.Space(6f);
        DrawDefaultInspector();
    }
}
