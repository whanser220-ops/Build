using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CrowdVatSquadControllerAliveCountTests
{
    private GameObject _root;
    private Component _renderer;
    private Component _controller;
    private Type _rendererType;
    private Type _controllerType;
    private Type _squadAuthoringType;

    [SetUp]
    public void SetUp()
    {
        _rendererType = Type.GetType("CrowdVatIndirectRenderer, Assembly-CSharp");
        _controllerType = Type.GetType("CrowdVatSquadController, Assembly-CSharp");
        _squadAuthoringType = _controllerType?.GetNestedType("SquadAuthoring", BindingFlags.NonPublic);

        Assert.That(_rendererType, Is.Not.Null);
        Assert.That(_controllerType, Is.Not.Null);
        Assert.That(_squadAuthoringType, Is.Not.Null);

        _root = new GameObject("CrowdVatSquadControllerAliveCountTests");
        _renderer = _root.AddComponent(_rendererType);
        _controller = _root.AddComponent(_controllerType);
    }

    [TearDown]
    public void TearDown()
    {
        if (_root != null)
            UnityEngine.Object.DestroyImmediate(_root);
    }

    [Test]
    public void ApplyToRenderer_WhenLayoutUploadRebuilds_KeepsExistingRuntimeAliveCount()
    {
        SetPrivateField(_renderer, _rendererType, "_instanceCount", 8);
        SetPrivateField(_renderer, _rendererType, "_enableGpuInstanceCombat", true);
        SetPrivateField(_controller, _controllerType, "_renderer", _renderer);
        SetPrivateField(_controller, _controllerType, "_autoResolveRenderer", false);
        SetPrivateField(_controller, _controllerType, "_squads", CreateSingleSquadArray());

        InvokePrivateMethod(_controller, _controllerType, "ApplyToRenderer", true);
        InvokePublicMethod(_renderer, _rendererType, "SetSquadAliveCounts", new[] { 3 });

        SetPrivateField(_controller, _controllerType, "_layoutDirty", true);
        InvokePrivateMethod(_controller, _controllerType, "ApplyToRenderer", false);

        object[] arguments = { 0, 0 };
        MethodInfo method = _rendererType.GetMethod("TryGetRuntimeSquadAliveCount", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        bool success = (bool)method.Invoke(_renderer, arguments);
        Assert.That(success, Is.True);
        Assert.That((int)arguments[1], Is.EqualTo(3));
    }

    [Test]
    public void ApplyToRenderer_WhenLayoutUploadRemapsRuntimeSquads_PreservesAliveCountBySourceSquad()
    {
        SetPrivateField(_renderer, _rendererType, "_instanceCount", 12);
        SetPrivateField(_renderer, _rendererType, "_enableGpuInstanceCombat", true);
        SetPrivateField(_controller, _controllerType, "_renderer", _renderer);
        SetPrivateField(_controller, _controllerType, "_autoResolveRenderer", false);

        Array initialSquads = CreateSquadArray(
            CreateSquadDefinition("Alpha", 0, 4, true),
            CreateSquadDefinition("Bravo", 4, 4, true),
            CreateSquadDefinition("Charlie", 8, 4, true));
        SetPrivateField(_controller, _controllerType, "_squads", initialSquads);

        InvokePrivateMethod(_controller, _controllerType, "ApplyToRenderer", true);
        InvokePublicMethod(_renderer, _rendererType, "SetSquadAliveCounts", new[] { 4, 1, 2 });

        Array remappedSquads = CreateSquadArray(
            CreateSquadDefinition("AlphaDisabled", 0, 4, false),
            CreateSquadDefinition("Bravo", 4, 4, true),
            CreateSquadDefinition("Charlie", 8, 4, true));
        SetPrivateField(_controller, _controllerType, "_squads", remappedSquads);
        SetPrivateField(_controller, _controllerType, "_layoutDirty", true);

        InvokePrivateMethod(_controller, _controllerType, "ApplyToRenderer", false);

        AssertRuntimeAliveCount(0, 1);
        AssertRuntimeAliveCount(1, 2);
    }

    [Test]
    public void ApplyToRenderer_WhenCustomFormationSlotsProvided_UploadsMemberSlotsAndStrength()
    {
        SetPrivateField(_renderer, _rendererType, "_instanceCount", 3);
        SetPrivateField(_controller, _controllerType, "_renderer", _renderer);
        SetPrivateField(_controller, _controllerType, "_autoResolveRenderer", false);

        Array squads = CreateSingleSquadArray(3);
        object squad = squads.GetValue(0);
        SetFieldValue(ref squad, "useCustomFormationSlots", true);
        SetFieldValue(ref squad, "customFormationStrength", 0.45f);
        SetFieldValue(ref squad, "customFormationSlots", new[]
        {
            new Vector2(-1.0f, 0.0f),
            new Vector2(1.0f, 0.0f),
            new Vector2(0.0f, 1.5f)
        });
        squads.SetValue(squad, 0);
        SetPrivateField(_controller, _controllerType, "_squads", squads);

        InvokePrivateMethod(_controller, _controllerType, "ApplyToRenderer", true);

        Array formationSlots = GetDebugArray(_renderer, _rendererType, "TryGetDebugFormationSlots");
        Assert.That(formationSlots.Length, Is.EqualTo(3));
        AssertVector2Field(formationSlots.GetValue(0), "localOffset", new Vector2(-1.0f, 0.0f));
        AssertVector2Field(formationSlots.GetValue(1), "localOffset", new Vector2(1.0f, 0.0f));
        AssertVector2Field(formationSlots.GetValue(2), "localOffset", new Vector2(0.0f, 1.5f));

        Array squadStates = GetDebugArray(_renderer, _rendererType, "TryGetDebugSquadStates");
        Assert.That(squadStates.Length, Is.EqualTo(1));
        Assert.That((float)GetFieldValue(squadStates.GetValue(0), "anchorBlend"), Is.EqualTo(0.45f).Within(0.0001f));

        Array assignments = (Array)GetPrivateField(_renderer, _rendererType, "_runtimeAgentSquadData");
        Assert.That(assignments.Length, Is.EqualTo(3));
        Assert.That((uint)GetFieldValue(assignments.GetValue(0), "slotIndex"), Is.EqualTo(0u));
        Assert.That((uint)GetFieldValue(assignments.GetValue(1), "slotIndex"), Is.EqualTo(1u));
        Assert.That((uint)GetFieldValue(assignments.GetValue(2), "slotIndex"), Is.EqualTo(2u));
    }

    [Test]
    public void ApplyToRenderer_WhenBuiltInBlockFormationSelected_GeneratesCenteredMemberSlots()
    {
        SetPrivateField(_renderer, _rendererType, "_instanceCount", 4);
        SetPrivateField(_controller, _controllerType, "_renderer", _renderer);
        SetPrivateField(_controller, _controllerType, "_autoResolveRenderer", false);

        Array squads = CreateSingleSquadArray(4);
        object squad = squads.GetValue(0);
        Type formationType = Type.GetType("CrowdVatSquadFormationType, Assembly-CSharp");
        Assert.That(formationType, Is.Not.Null);
        SetFieldValue(ref squad, "formationType", Enum.Parse(formationType, "Block"));
        SetFieldValue(ref squad, "formationSpacing", new Vector2(2.0f, 3.0f));
        SetFieldValue(ref squad, "customFormationStrength", 0.6f);
        squads.SetValue(squad, 0);
        SetPrivateField(_controller, _controllerType, "_squads", squads);

        InvokePrivateMethod(_controller, _controllerType, "ApplyToRenderer", true);

        Array formationSlots = GetDebugArray(_renderer, _rendererType, "TryGetDebugFormationSlots");
        Assert.That(formationSlots.Length, Is.EqualTo(4));
        AssertVector2Field(formationSlots.GetValue(0), "localOffset", new Vector2(-1.0f, 1.5f));
        AssertVector2Field(formationSlots.GetValue(1), "localOffset", new Vector2(1.0f, 1.5f));
        AssertVector2Field(formationSlots.GetValue(2), "localOffset", new Vector2(-1.0f, -1.5f));
        AssertVector2Field(formationSlots.GetValue(3), "localOffset", new Vector2(1.0f, -1.5f));

        Array squadStates = GetDebugArray(_renderer, _rendererType, "TryGetDebugSquadStates");
        Assert.That(GetFieldValue(squadStates.GetValue(0), "formationType").ToString(), Is.EqualTo("Block"));
        AssertVector2Field(squadStates.GetValue(0), "formationSpacing", new Vector2(2.0f, 3.0f));
        Assert.That((float)GetFieldValue(squadStates.GetValue(0), "anchorBlend"), Is.EqualTo(0.6f).Within(0.0001f));
    }

    [Test]
    public void ApplyToRenderer_WhenBuiltInSpacingBelowCollisionDiameter_ExpandsGeneratedSlots()
    {
        SetPrivateField(_renderer, _rendererType, "_instanceCount", 2);
        SetPrivateField(_renderer, _rendererType, "_baseScale", 1.6f);
        SetPrivateField(_renderer, _rendererType, "_scaleMultiplierRange", new Vector2(0.95f, 1.05f));
        SetPrivateField(_renderer, _rendererType, "_collisionRadius", 0.38f);
        SetPrivateField(_controller, _controllerType, "_renderer", _renderer);
        SetPrivateField(_controller, _controllerType, "_autoResolveRenderer", false);

        Array squads = CreateSingleSquadArray(2);
        object squad = squads.GetValue(0);
        Type formationType = Type.GetType("CrowdVatSquadFormationType, Assembly-CSharp");
        Assert.That(formationType, Is.Not.Null);
        SetFieldValue(ref squad, "formationType", Enum.Parse(formationType, "Line"));
        SetFieldValue(ref squad, "formationSpacing", new Vector2(1.0f, 1.0f));
        SetFieldValue(ref squad, "customFormationStrength", 0.35f);
        squads.SetValue(squad, 0);
        SetPrivateField(_controller, _controllerType, "_squads", squads);

        InvokePrivateMethod(_controller, _controllerType, "ApplyToRenderer", true);

        const float expectedSpacing = 0.38f * 1.6f * 1.05f * 2.0f + 0.08f;
        Array formationSlots = GetDebugArray(_renderer, _rendererType, "TryGetDebugFormationSlots");
        Assert.That(formationSlots.Length, Is.EqualTo(2));
        AssertVector2Field(formationSlots.GetValue(0), "localOffset", new Vector2(expectedSpacing * -0.5f, 0.0f));
        AssertVector2Field(formationSlots.GetValue(1), "localOffset", new Vector2(expectedSpacing * 0.5f, 0.0f));

        Array squadStates = GetDebugArray(_renderer, _rendererType, "TryGetDebugSquadStates");
        AssertVector2Field(squadStates.GetValue(0), "formationSpacing", new Vector2(expectedSpacing, expectedSpacing));
    }

    private Array CreateSingleSquadArray(int memberCount = 8)
    {
        return CreateSquadArray(CreateSquadDefinition("Center", 0, memberCount, true));
    }

    private Array CreateSquadArray(params SquadDefinition[] definitions)
    {
        Array squads = Array.CreateInstance(_squadAuthoringType, definitions.Length);
        for (int index = 0; index < definitions.Length; index++)
        {
            SquadDefinition definition = definitions[index];
            object squad = Activator.CreateInstance(_squadAuthoringType);

            GameObject centerObject = new GameObject(definition.name);
            centerObject.transform.SetParent(_root.transform, false);

            SetFieldValue(ref squad, "enabled", definition.enabled);
            SetFieldValue(ref squad, "centerTransform", centerObject.transform);
            SetFieldValue(ref squad, "memberStartIndex", definition.memberStartIndex);
            SetFieldValue(ref squad, "memberCount", definition.memberCount);
            squads.SetValue(squad, index);
        }

        return squads;
    }

    private static SquadDefinition CreateSquadDefinition(string name, int memberStartIndex, int memberCount, bool enabled)
    {
        return new SquadDefinition
        {
            name = name,
            memberStartIndex = memberStartIndex,
            memberCount = memberCount,
            enabled = enabled
        };
    }

    private void AssertRuntimeAliveCount(int runtimeSquadIndex, int expectedAliveCount)
    {
        object[] arguments = { runtimeSquadIndex, 0 };
        MethodInfo method = _rendererType.GetMethod("TryGetRuntimeSquadAliveCount", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        bool success = (bool)method.Invoke(_renderer, arguments);
        Assert.That(success, Is.True);
        Assert.That((int)arguments[1], Is.EqualTo(expectedAliveCount));
    }

    private static Array GetDebugArray(object target, Type targetType, string methodName)
    {
        object[] arguments = { null };
        MethodInfo method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"Method '{methodName}' was not found.");

        bool success = (bool)method.Invoke(target, arguments);
        Assert.That(success, Is.True);
        return (Array)arguments[0];
    }

    private static void AssertVector2Field(object target, string fieldName, Vector2 expected)
    {
        Vector2 actual = (Vector2)GetFieldValue(target, fieldName);
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
    }

    private static void SetFieldValue<T>(ref object instance, string fieldName, T value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        object boxed = instance;
        field.SetValue(boxed, value);
        instance = boxed;
    }

    private static object GetFieldValue(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        return field.GetValue(instance);
    }

    private static void InvokePublicMethod(object target, Type targetType, string methodName, object argument)
    {
        MethodInfo method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"Method '{methodName}' was not found.");
        method.Invoke(target, new[] { argument });
    }

    private static void InvokePrivateMethod(object target, Type targetType, string methodName, object argument)
    {
        MethodInfo method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Method '{methodName}' was not found.");
        method.Invoke(target, new[] { argument });
    }

    private static void SetPrivateField<T>(object target, Type targetType, string fieldName, T value)
    {
        FieldInfo field = targetType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        field.SetValue(target, value);
    }

    private static object GetPrivateField(object target, Type targetType, string fieldName)
    {
        FieldInfo field = targetType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        return field.GetValue(target);
    }

    private struct SquadDefinition
    {
        public string name;
        public int memberStartIndex;
        public int memberCount;
        public bool enabled;
    }

}
