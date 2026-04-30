using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CrowdVatIndirectRendererCombatOriginTests
{
    private GameObject _gameObject;
    private Component _renderer;
    private Type _rendererType;
    private Type _spawnDataType;
    private Type _simulationStateType;
    private Type _combatStateType;

    [SetUp]
    public void SetUp()
    {
        _rendererType = Type.GetType("CrowdVatIndirectRenderer, Assembly-CSharp");
        Assert.That(_rendererType, Is.Not.Null);

        _spawnDataType = _rendererType.GetNestedType("InstanceSpawnData", BindingFlags.NonPublic);
        _simulationStateType = _rendererType.GetNestedType("InstanceSimulationState", BindingFlags.NonPublic);
        _combatStateType = _rendererType.GetNestedType("InstanceCombatStateData", BindingFlags.NonPublic);

        Assert.That(_spawnDataType, Is.Not.Null);
        Assert.That(_simulationStateType, Is.Not.Null);
        Assert.That(_combatStateType, Is.Not.Null);

        _gameObject = new GameObject("CrowdVatIndirectRendererCombatOriginTests");
        _renderer = _gameObject.AddComponent(_rendererType);
    }

    [TearDown]
    public void TearDown()
    {
        if (_gameObject != null)
            UnityEngine.Object.DestroyImmediate(_gameObject);
    }

    [Test]
    public void BuildInitialCombatStates_UsesScaledCombatTargetTopAsOrigin()
    {
        SetPrivateField("_combatOriginHeight", 0.35f);
        SetPrivateField("_combatTargetHeight", 1.6f);
        SetPrivateField("_hasRuntimeRenderResource", false);

        Array spawnData = Array.CreateInstance(_spawnDataType, 1);
        object spawn = Activator.CreateInstance(_spawnDataType);
        SetStructField(_spawnDataType, ref spawn, "uniformScale", 1.25f);
        spawnData.SetValue(spawn, 0);

        Array simulationState = Array.CreateInstance(_simulationStateType, 1);
        object simulation = Activator.CreateInstance(_simulationStateType);
        SetStructField(_simulationStateType, ref simulation, "localPositionAndYaw", new Vector4(2.0f, 3.0f, 4.0f, 0.0f));
        SetStructField(_simulationStateType, ref simulation, "scaleAndVelocity", new Vector4(1.25f, 0.0f, 0.0f, 0.0f));
        simulationState.SetValue(simulation, 0);

        MethodInfo method = _rendererType.GetMethod("BuildInitialCombatStates", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        Array combatStates = (Array)method.Invoke(_renderer, new object[] { spawnData, simulationState });
        Assert.That(combatStates, Is.Not.Null);
        Assert.That(combatStates.Length, Is.GreaterThanOrEqualTo(1));

        object firstState = combatStates.GetValue(0);
        Vector4 originAndDistance = (Vector4)GetStructField(_combatStateType, firstState, "localOriginAndDistance");
        Vector4 targetAndCooldown = (Vector4)GetStructField(_combatStateType, firstState, "localTargetAndCooldown");

        float expectedOriginY = 3.0f + 1.6f * 1.25f;
        Assert.That(originAndDistance.x, Is.EqualTo(2.0f).Within(1e-5f));
        Assert.That(originAndDistance.y, Is.EqualTo(expectedOriginY).Within(1e-5f));
        Assert.That(originAndDistance.z, Is.EqualTo(4.0f).Within(1e-5f));
        Assert.That(targetAndCooldown.x, Is.EqualTo(2.0f).Within(1e-5f));
        Assert.That(targetAndCooldown.y, Is.EqualTo(expectedOriginY).Within(1e-5f));
        Assert.That(targetAndCooldown.z, Is.EqualTo(4.0f).Within(1e-5f));
    }

    private void SetPrivateField<T>(string fieldName, T value)
    {
        FieldInfo field = _rendererType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        field.SetValue(_renderer, value);
    }

    private static void SetStructField<T>(Type structType, ref object boxedStruct, string fieldName, T value)
    {
        FieldInfo field = structType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        field.SetValue(boxedStruct, value);
    }

    private static object GetStructField(Type structType, object boxedStruct, string fieldName)
    {
        FieldInfo field = structType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        return field.GetValue(boxedStruct);
    }
}
