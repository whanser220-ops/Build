using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CrowdVatObservationHubTests
{
    private GameObject _gameObject;
    private Component _renderer;
    private object _observationHub;
    private Type _rendererType;
    private Type _observationHubType;
    private Type _frameObservationType;
    private Type _squadStateType;
    private Type _observationSourceKindType;
    private Type _captureFlagsType;
    private Type _combatObservationType;

    [SetUp]
    public void SetUp()
    {
        _rendererType = Type.GetType("CrowdVatIndirectRenderer, Assembly-CSharp");
        _observationHubType = Type.GetType("CrowdVatObservationHub, Assembly-CSharp");
        _frameObservationType = Type.GetType("CrowdVatFrameObservation, Assembly-CSharp");
        _squadStateType = Type.GetType("CrowdVatSquadState, Assembly-CSharp");
        _observationSourceKindType = Type.GetType("CrowdVatObservationSourceKind, Assembly-CSharp");
        _captureFlagsType = Type.GetType("CrowdVatObservationCaptureFlags, Assembly-CSharp");
        _combatObservationType = Type.GetType("CrowdVatCombatObservation, Assembly-CSharp");

        Assert.That(_rendererType, Is.Not.Null);
        Assert.That(_observationHubType, Is.Not.Null);
        Assert.That(_frameObservationType, Is.Not.Null);
        Assert.That(_squadStateType, Is.Not.Null);
        Assert.That(_observationSourceKindType, Is.Not.Null);
        Assert.That(_captureFlagsType, Is.Not.Null);
        Assert.That(_combatObservationType, Is.Not.Null);

        _gameObject = new GameObject("CrowdVatObservationHubTests");
        _renderer = _gameObject.AddComponent(_rendererType);
        ConstructorInfo constructor = _observationHubType.GetConstructor(new[] { _rendererType });
        Assert.That(constructor, Is.Not.Null);
        _observationHub = constructor.Invoke(new object[] { _renderer });
    }

    [TearDown]
    public void TearDown()
    {
        if (_gameObject != null)
            UnityEngine.Object.DestroyImmediate(_gameObject);
    }

    [Test]
    public void TryCaptureFrameObservation_WhenSquadAliveCountsDirty_UsesCpuMirrorObservation()
    {
        InvokePublicMethod(_renderer, _rendererType, "SetSquadStates", CreateSingleSquadStateArray());
        InvokePublicMethod(_renderer, _rendererType, "SetSquadAliveCounts", new[] { 7 });

        object frameObservation = Activator.CreateInstance(_frameObservationType);
        object flags = Enum.Parse(_captureFlagsType, "SquadAliveCounts");
        MethodInfo method = _observationHubType.GetMethod("TryCaptureFrameObservation", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        bool captured = (bool)method.Invoke(_observationHub, new[] { frameObservation, flags });
        Assert.That(captured, Is.True);

        object frameInfo = GetPublicField(frameObservation, _frameObservationType, "squadAliveFrameInfo");
        object sourceKind = GetPublicField(frameInfo, frameInfo.GetType(), "sourceKind");
        Assert.That(sourceKind.ToString(), Is.EqualTo("CpuMirror"));

        IList observations = (IList)GetPublicField(frameObservation, _frameObservationType, "squadAliveObservations");
        Assert.That(observations.Count, Is.EqualTo(1));

        object firstObservation = observations[0];
        Assert.That((int)GetPublicField(firstObservation, firstObservation.GetType(), "aliveCount"), Is.EqualTo(7));
    }

    [Test]
    public void ReadCombatObservations_DoesNotExposePresentationOnlyFields()
    {
        Assert.That(_combatObservationType.GetField("distance"), Is.Not.Null);
        Assert.That(_combatObservationType.GetField("firedThisFrame"), Is.Not.Null);
        Assert.That(_combatObservationType.GetField("muzzleFlash"), Is.Null);
        Assert.That(_combatObservationType.GetField("worldOrigin"), Is.Null);
        Assert.That(_combatObservationType.GetField("worldTargetPoint"), Is.Null);
    }

    [Test]
    public void TryCaptureFrameObservation_WhenSpatialQueriesHaveZeroHits_StillReturnsSuccess()
    {
        Type spatialQueryRequestType = Type.GetType("CrowdVatSpatialQueryRequest, Assembly-CSharp");
        Assert.That(spatialQueryRequestType, Is.Not.Null);

        Array queries = Array.CreateInstance(spatialQueryRequestType, 1);
        object query = Activator.CreateInstance(spatialQueryRequestType);
        SetStructField(spatialQueryRequestType, ref query, "queryId", 1);
        SetStructField(spatialQueryRequestType, ref query, "targetMask", Enum.Parse(Type.GetType("CrowdVatSpatialTargetMask, Assembly-CSharp"), "CrowdAgent"));
        SetStructField(spatialQueryRequestType, ref query, "factionMask", Enum.Parse(Type.GetType("CrowdVatFactionMask, Assembly-CSharp"), "All"));
        SetStructField(spatialQueryRequestType, ref query, "shape", Enum.Parse(Type.GetType("CrowdVatSpatialQueryShape, Assembly-CSharp"), "OverlapSphere"));
        SetStructField(spatialQueryRequestType, ref query, "flags", Enum.Parse(Type.GetType("CrowdVatSpatialQueryFlags, Assembly-CSharp"), "None"));
        SetStructField(spatialQueryRequestType, ref query, "worldStart", Vector3.zero);
        SetStructField(spatialQueryRequestType, ref query, "worldEnd", Vector3.zero);
        SetStructField(spatialQueryRequestType, ref query, "radius", 1.0f);
        SetStructField(spatialQueryRequestType, ref query, "maxHits", 0u);
        queries.SetValue(query, 0);

        InvokePublicMethod(_renderer, _rendererType, "SetSpatialQueries", queries);

        object frameObservation = Activator.CreateInstance(_frameObservationType);
        object flags = Enum.Parse(_captureFlagsType, "SpatialQueries");
        MethodInfo method = _observationHubType.GetMethod("TryCaptureFrameObservation", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        bool captured = (bool)method.Invoke(_observationHub, new[] { frameObservation, flags });
        Assert.That(captured, Is.True);

        object frameInfo = GetPublicField(frameObservation, _frameObservationType, "spatialQueryFrameInfo");
        object sourceKind = GetPublicField(frameInfo, frameInfo.GetType(), "sourceKind");
        Assert.That(sourceKind.ToString(), Is.EqualTo("SyncGpuReadback"));
    }

    private Array CreateSingleSquadStateArray()
    {
        Array squadStates = Array.CreateInstance(_squadStateType, 1);
        object squadState = Activator.CreateInstance(_squadStateType);
        SetStructField(ref squadState, "worldCenter", Vector3.zero);
        SetStructField(ref squadState, "worldForward", Vector3.forward);
        SetStructField(ref squadState, "worldTarget", Vector3.forward);
        SetStructField(ref squadState, "formationSpacing", new Vector2(1.4f, 1.8f));
        SetStructField(ref squadState, "moveSpeed", 2.0f);
        SetStructField(ref squadState, "anchorBlend", 1.0f);
        squadStates.SetValue(squadState, 0);
        return squadStates;
    }

    private void SetStructField<T>(ref object boxedStruct, string fieldName, T value)
    {
        SetStructField(_squadStateType, ref boxedStruct, fieldName, value);
    }

    private static void SetStructField<T>(Type targetType, ref object boxedStruct, string fieldName, T value)
    {
        FieldInfo field = targetType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        object updated = boxedStruct;
        field.SetValue(updated, value);
        boxedStruct = updated;
    }

    private static void InvokePublicMethod(object target, Type targetType, string methodName, object argument)
    {
        MethodInfo method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"Method '{methodName}' was not found.");
        method.Invoke(target, new[] { argument });
    }

    private static object GetPublicField(object target, Type targetType, string fieldName)
    {
        FieldInfo field = targetType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        return field.GetValue(target);
    }
}
