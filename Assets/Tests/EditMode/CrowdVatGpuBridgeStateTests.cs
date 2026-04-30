using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CrowdVatGpuBridgeStateTests
{
    private Type _gpuBridgeStateType;
    private Type _framePacketType;
    private Type _frameParamsType;
    private Type _gpuEventType;
    private Type _gpuEventTypeEnum;
    private Type _dirtyRangeType;
    private Type _squadStateType;
    private Type _agentAssignmentType;
    private Type _formationSlotType;

    [SetUp]
    public void SetUp()
    {
        _gpuBridgeStateType = Type.GetType("CrowdVatGpuBridgeState, Assembly-CSharp");
        _framePacketType = Type.GetType("CrowdVatFramePacket, Assembly-CSharp");
        _frameParamsType = Type.GetType("CrowdVatFrameParams, Assembly-CSharp");
        _gpuEventType = Type.GetType("CrowdVatGpuEvent, Assembly-CSharp");
        _gpuEventTypeEnum = Type.GetType("CrowdVatGpuEventType, Assembly-CSharp");
        _dirtyRangeType = Type.GetType("CrowdVatDirtyRange, Assembly-CSharp");
        _squadStateType = Type.GetType("CrowdVatSquadState, Assembly-CSharp");
        _agentAssignmentType = Type.GetType("CrowdVatAgentSquadAssignment, Assembly-CSharp");
        _formationSlotType = Type.GetType("CrowdVatFormationSlot, Assembly-CSharp");

        Assert.That(_gpuBridgeStateType, Is.Not.Null);
        Assert.That(_framePacketType, Is.Not.Null);
        Assert.That(_frameParamsType, Is.Not.Null);
        Assert.That(_gpuEventType, Is.Not.Null);
        Assert.That(_gpuEventTypeEnum, Is.Not.Null);
        Assert.That(_dirtyRangeType, Is.Not.Null);
        Assert.That(_squadStateType, Is.Not.Null);
        Assert.That(_agentAssignmentType, Is.Not.Null);
        Assert.That(_formationSlotType, Is.Not.Null);
    }

    [Test]
    public void CaptureFrameProtocol_ClampsInvalidDirtyRangesAndCopiesEvents()
    {
        object state = Activator.CreateInstance(_gpuBridgeStateType);
        object framePacket = Activator.CreateInstance(_framePacketType);
        object frameParams = Activator.CreateInstance(_frameParamsType);
        SetStructField(_frameParamsType, ref frameParams, "deltaTime", 0.25f);
        SetStructField(_frameParamsType, ref frameParams, "frameIndex", 9u);
        SetStructField(_frameParamsType, ref frameParams, "activeAgentCount", 16u);
        SetField(framePacket, _framePacketType, "frameParams", frameParams);
        SetField(framePacket, _framePacketType, "events", CreateGpuEventArray());
        SetField(framePacket, _framePacketType, "dirtyAgentRanges", CreateDirtyRangeArray((-3, 4), (99, 3), (6, 0)));

        InvokeMethod(state, _gpuBridgeStateType, "CaptureFrameProtocol", new[] { framePacket, 12 });

        object capturedFrameParams = GetProperty(state, _gpuBridgeStateType, "FrameParams");
        Assert.That(GetField(capturedFrameParams, _frameParamsType, "eventCount"), Is.EqualTo(1u));

        Array events = (Array)GetProperty(state, _gpuBridgeStateType, "Events");
        Assert.That(events.Length, Is.EqualTo(1));
        object firstEvent = events.GetValue(0);
        Assert.That(GetField(firstEvent, _gpuEventType, "agentId"), Is.EqualTo(5u));

        Array dirtyRanges = (Array)GetProperty(state, _gpuBridgeStateType, "DirtyAgentRanges");
        Assert.That(dirtyRanges.Length, Is.EqualTo(1));
        object firstRange = dirtyRanges.GetValue(0);
        Assert.That(GetField(firstRange, _dirtyRangeType, "startIndex"), Is.EqualTo(0));
        Assert.That(GetField(firstRange, _dirtyRangeType, "count"), Is.EqualTo(4));
    }

    [Test]
    public void SetRuntimeSquadSnapshot_CopiesInputArrays()
    {
        object state = Activator.CreateInstance(_gpuBridgeStateType);
        Array squadStates = Array.CreateInstance(_squadStateType, 1);
        object squadState = Activator.CreateInstance(_squadStateType);
        SetStructField(_squadStateType, ref squadState, "worldCenter", Vector3.one);
        SetStructField(_squadStateType, ref squadState, "worldForward", Vector3.forward);
        SetStructField(_squadStateType, ref squadState, "worldTarget", Vector3.forward);
        SetStructField(_squadStateType, ref squadState, "formationSpacing", new Vector2(1.0f, 2.0f));
        SetStructField(_squadStateType, ref squadState, "moveSpeed", 3.0f);
        SetStructField(_squadStateType, ref squadState, "anchorBlend", 1.0f);
        squadStates.SetValue(squadState, 0);

        uint[] aliveCounts = { 7u };

        Array assignments = Array.CreateInstance(_agentAssignmentType, 1);
        object assignment = Activator.CreateInstance(_agentAssignmentType);
        SetStructField(_agentAssignmentType, ref assignment, "squadId", 0u);
        SetStructField(_agentAssignmentType, ref assignment, "slotIndex", 1u);
        SetStructField(_agentAssignmentType, ref assignment, "weight", 1.0f);
        assignments.SetValue(assignment, 0);

        Array formationSlots = Array.CreateInstance(_formationSlotType, 1);
        object formationSlot = Activator.CreateInstance(_formationSlotType);
        SetStructField(_formationSlotType, ref formationSlot, "localOffset", new Vector2(2.0f, 3.0f));
        SetStructField(_formationSlotType, ref formationSlot, "preferredDistance", 4.0f);
        formationSlots.SetValue(formationSlot, 0);

        InvokeMethod(state, _gpuBridgeStateType, "SetRuntimeSquadSnapshot", new object[] { squadStates, aliveCounts, assignments, formationSlots });

        object mutatedSquadState = squadStates.GetValue(0);
        SetStructField(_squadStateType, ref mutatedSquadState, "moveSpeed", 99.0f);
        squadStates.SetValue(mutatedSquadState, 0);
        aliveCounts[0] = 0u;

        Array copiedSquadStates = (Array)GetProperty(state, _gpuBridgeStateType, "SquadStates");
        Assert.That(copiedSquadStates.Length, Is.EqualTo(1));
        object copiedSquadState = copiedSquadStates.GetValue(0);
        Assert.That(GetField(copiedSquadState, _squadStateType, "moveSpeed"), Is.EqualTo(3.0f));

        uint[] copiedAliveCounts = (uint[])GetProperty(state, _gpuBridgeStateType, "SquadAliveCounts");
        Assert.That(copiedAliveCounts[0], Is.EqualTo(7u));

        Array copiedAssignments = (Array)GetProperty(state, _gpuBridgeStateType, "AgentAssignments");
        Array copiedFormationSlots = (Array)GetProperty(state, _gpuBridgeStateType, "FormationSlots");
        Assert.That(copiedAssignments.Length, Is.EqualTo(1));
        Assert.That(copiedFormationSlots.Length, Is.EqualTo(1));
    }

    private Array CreateGpuEventArray()
    {
        Array array = Array.CreateInstance(_gpuEventType, 1);
        object gpuEvent = Activator.CreateInstance(_gpuEventType);
        SetStructField(_gpuEventType, ref gpuEvent, "type", Enum.Parse(_gpuEventTypeEnum, "Damage"));
        SetStructField(_gpuEventType, ref gpuEvent, "agentId", 5u);
        SetStructField(_gpuEventType, ref gpuEvent, "arg0", 2u);
        SetStructField(_gpuEventType, ref gpuEvent, "payload", new Vector4(10.0f, 0.0f, 0.0f, 0.0f));
        array.SetValue(gpuEvent, 0);
        return array;
    }

    private Array CreateDirtyRangeArray(params (int startIndex, int count)[] values)
    {
        Array array = Array.CreateInstance(_dirtyRangeType, values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            object range = Activator.CreateInstance(_dirtyRangeType);
            SetStructField(_dirtyRangeType, ref range, "startIndex", values[index].startIndex);
            SetStructField(_dirtyRangeType, ref range, "count", values[index].count);
            array.SetValue(range, index);
        }

        return array;
    }

    private static object GetField(object instance, Type instanceType, string fieldName)
    {
        FieldInfo field = instanceType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        return field.GetValue(instance);
    }

    private static void SetField(object instance, Type instanceType, string fieldName, object value)
    {
        FieldInfo field = instanceType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field.SetValue(instance, value);
    }

    private static object GetProperty(object instance, Type instanceType, string propertyName)
    {
        PropertyInfo property = instanceType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(property, Is.Not.Null);
        return property.GetValue(instance);
    }

    private static void InvokeMethod(object instance, Type instanceType, string methodName, object[] arguments)
    {
        MethodInfo method = instanceType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);
        method.Invoke(instance, arguments);
    }

    private static void SetStructField<T>(Type structType, ref object boxedStruct, string fieldName, T value)
    {
        FieldInfo field = structType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field.SetValue(boxedStruct, value);
    }
}
