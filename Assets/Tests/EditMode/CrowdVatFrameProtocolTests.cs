using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CrowdVatFrameProtocolTests
{
    private Type _framePacketType;
    private Type _framePacketFlagsType;
    private Type _frameParamsType;
    private Type _frameInputType;
    private Type _gpuEventType;
    private Type _gpuEventTypeEnum;
    private Type _dirtyRangeType;
    private Type _spatialQueryRequestType;
    private Type _squadStateType;
    private Type _agentAssignmentType;
    private Type _formationSlotType;
    private Type _legacyAdapterType;

    [SetUp]
    public void SetUp()
    {
        _framePacketType = Type.GetType("CrowdVatFramePacket, Assembly-CSharp");
        _framePacketFlagsType = Type.GetType("CrowdVatFramePacketFlags, Assembly-CSharp");
        _frameParamsType = Type.GetType("CrowdVatFrameParams, Assembly-CSharp");
        _frameInputType = Type.GetType("CrowdVatFrameInput, Assembly-CSharp");
        _gpuEventType = Type.GetType("CrowdVatGpuEvent, Assembly-CSharp");
        _gpuEventTypeEnum = Type.GetType("CrowdVatGpuEventType, Assembly-CSharp");
        _dirtyRangeType = Type.GetType("CrowdVatDirtyRange, Assembly-CSharp");
        _spatialQueryRequestType = Type.GetType("CrowdVatSpatialQueryRequest, Assembly-CSharp");
        _squadStateType = Type.GetType("CrowdVatSquadState, Assembly-CSharp");
        _agentAssignmentType = Type.GetType("CrowdVatAgentSquadAssignment, Assembly-CSharp");
        _formationSlotType = Type.GetType("CrowdVatFormationSlot, Assembly-CSharp");
        _legacyAdapterType = Type.GetType("CrowdVatFrameInputLegacyAdapter, Assembly-CSharp");

        Assert.That(_framePacketType, Is.Not.Null);
        Assert.That(_framePacketFlagsType, Is.Not.Null);
        Assert.That(_frameParamsType, Is.Not.Null);
        Assert.That(_frameInputType, Is.Not.Null);
        Assert.That(_gpuEventType, Is.Not.Null);
        Assert.That(_gpuEventTypeEnum, Is.Not.Null);
        Assert.That(_dirtyRangeType, Is.Not.Null);
        Assert.That(_spatialQueryRequestType, Is.Not.Null);
        Assert.That(_squadStateType, Is.Not.Null);
        Assert.That(_agentAssignmentType, Is.Not.Null);
        Assert.That(_formationSlotType, Is.Not.Null);
        Assert.That(_legacyAdapterType, Is.Not.Null);
    }

    [Test]
    public void FramePacket_Reset_ClearsFlagsAndPayloadArrays()
    {
        object framePacket = Activator.CreateInstance(_framePacketType);
        object frameParams = Activator.CreateInstance(_frameParamsType);
        SetStructField(_frameParamsType, ref frameParams, "deltaTime", 0.25f);
        SetStructField(_frameParamsType, ref frameParams, "frameIndex", 11u);
        SetStructField(_frameParamsType, ref frameParams, "activeAgentCount", 32u);
        SetStructField(_frameParamsType, ref frameParams, "eventCount", 2u);

        SetField(framePacket, _framePacketType, "frameParams", frameParams);
        SetField(framePacket, _framePacketType, "flags", ParseEnum(_framePacketFlagsType, "UploadSquadStates, ReplaceSpatialQueries"));
        SetField(framePacket, _framePacketType, "events", CreateGpuEventArray("ChangeMode", 4u));
        SetField(framePacket, _framePacketType, "dirtyAgentRanges", CreateDirtyRangeArray((0, 16)));
        SetField(framePacket, _framePacketType, "dirtySquadRanges", CreateDirtyRangeArray((0, 4)));
        SetField(framePacket, _framePacketType, "spatialQueries", CreateSpatialQueryArray(3, 2.0f));
        SetField(framePacket, _framePacketType, "squadStates", Array.CreateInstance(_squadStateType, 1));
        SetField(framePacket, _framePacketType, "squadAliveCounts", new[] { 5 });
        SetField(framePacket, _framePacketType, "agentAssignments", Array.CreateInstance(_agentAssignmentType, 1));
        SetField(framePacket, _framePacketType, "formationSlots", Array.CreateInstance(_formationSlotType, 1));

        InvokeMethod(framePacket, _framePacketType, "Reset");

        object resetFrameParams = GetField(framePacket, _framePacketType, "frameParams");
        Assert.That(GetField(resetFrameParams, _frameParamsType, "deltaTime"), Is.EqualTo(0.0f));
        Assert.That(GetField(resetFrameParams, _frameParamsType, "frameIndex"), Is.EqualTo(0u));
        Assert.That(GetField(framePacket, _framePacketType, "flags").ToString(), Is.EqualTo("None"));
        AssertArrayLength(framePacket, _framePacketType, "events", 0);
        AssertArrayLength(framePacket, _framePacketType, "dirtyAgentRanges", 0);
        AssertArrayLength(framePacket, _framePacketType, "dirtySquadRanges", 0);
        AssertArrayLength(framePacket, _framePacketType, "spatialQueries", 0);
        AssertArrayLength(framePacket, _framePacketType, "squadStates", 0);
        AssertArrayLength(framePacket, _framePacketType, "squadAliveCounts", 0);
        AssertArrayLength(framePacket, _framePacketType, "agentAssignments", 0);
        AssertArrayLength(framePacket, _framePacketType, "formationSlots", 0);
    }

    [Test]
    public void Reset_ClearsProtocolFieldsAndPayloadArrays()
    {
        object frameInput = Activator.CreateInstance(_frameInputType);
        object frameParams = Activator.CreateInstance(_frameParamsType);
        SetStructField(_frameParamsType, ref frameParams, "deltaTime", 0.5f);
        SetStructField(_frameParamsType, ref frameParams, "frameIndex", 7u);
        SetStructField(_frameParamsType, ref frameParams, "activeAgentCount", 12u);
        SetStructField(_frameParamsType, ref frameParams, "eventCount", 1u);

        SetField(frameInput, _frameInputType, "frameParams", frameParams);
        SetField(frameInput, _frameInputType, "clearRuntimeSquadData", true);
        SetField(frameInput, _frameInputType, "uploadSquadStates", true);
        SetField(frameInput, _frameInputType, "uploadSquadAliveCounts", true);
        SetField(frameInput, _frameInputType, "uploadAgentAssignments", true);
        SetField(frameInput, _frameInputType, "uploadFormationSlots", true);
        SetField(frameInput, _frameInputType, "events", CreateGpuEventArray("SetDestination", 3u));
        SetField(frameInput, _frameInputType, "dirtyAgentRanges", CreateDirtyRangeArray((0, 8)));
        SetField(frameInput, _frameInputType, "dirtySquadRanges", CreateDirtyRangeArray((0, 2)));
        SetField(frameInput, _frameInputType, "squadStates", Array.CreateInstance(_squadStateType, 1));
        SetField(frameInput, _frameInputType, "squadAliveCounts", new[] { 4 });
        SetField(frameInput, _frameInputType, "agentAssignments", Array.CreateInstance(_agentAssignmentType, 1));
        SetField(frameInput, _frameInputType, "formationSlots", Array.CreateInstance(_formationSlotType, 1));

        InvokeMethod(frameInput, _frameInputType, "Reset");

        object resetFrameParams = GetField(frameInput, _frameInputType, "frameParams");
        Assert.That(GetField(resetFrameParams, _frameParamsType, "deltaTime"), Is.EqualTo(0.0f));
        Assert.That(GetField(resetFrameParams, _frameParamsType, "frameIndex"), Is.EqualTo(0u));
        Assert.That((bool)GetField(frameInput, _frameInputType, "clearRuntimeSquadData"), Is.False);
        Assert.That((bool)GetField(frameInput, _frameInputType, "uploadSquadStates"), Is.False);
        Assert.That((bool)GetField(frameInput, _frameInputType, "uploadSquadAliveCounts"), Is.False);
        Assert.That((bool)GetField(frameInput, _frameInputType, "uploadAgentAssignments"), Is.False);
        Assert.That((bool)GetField(frameInput, _frameInputType, "uploadFormationSlots"), Is.False);
        AssertArrayLength(frameInput, _frameInputType, "events", 0);
        AssertArrayLength(frameInput, _frameInputType, "dirtyAgentRanges", 0);
        AssertArrayLength(frameInput, _frameInputType, "dirtySquadRanges", 0);
        AssertArrayLength(frameInput, _frameInputType, "squadStates", 0);
        AssertArrayLength(frameInput, _frameInputType, "squadAliveCounts", 0);
        AssertArrayLength(frameInput, _frameInputType, "agentAssignments", 0);
        AssertArrayLength(frameInput, _frameInputType, "formationSlots", 0);
    }

    [Test]
    public void LegacyAdapter_CopyToPacket_MapsFlagsAndArrays()
    {
        object frameInput = Activator.CreateInstance(_frameInputType);
        object framePacket = Activator.CreateInstance(_framePacketType);
        object frameParams = Activator.CreateInstance(_frameParamsType);
        SetStructField(_frameParamsType, ref frameParams, "deltaTime", 0.125f);
        SetStructField(_frameParamsType, ref frameParams, "frameIndex", 6u);
        SetStructField(_frameParamsType, ref frameParams, "activeAgentCount", 18u);
        SetStructField(_frameParamsType, ref frameParams, "eventCount", 1u);

        SetField(frameInput, _frameInputType, "frameParams", frameParams);
        SetField(frameInput, _frameInputType, "uploadSquadStates", true);
        SetField(frameInput, _frameInputType, "uploadSquadAliveCounts", true);
        SetField(frameInput, _frameInputType, "uploadAgentAssignments", true);
        SetField(frameInput, _frameInputType, "events", CreateGpuEventArray("SetDestination", 9u));
        SetField(frameInput, _frameInputType, "dirtyAgentRanges", CreateDirtyRangeArray((2, 5)));
        SetField(frameInput, _frameInputType, "dirtySquadRanges", CreateDirtyRangeArray((1, 2)));
        SetField(frameInput, _frameInputType, "squadStates", Array.CreateInstance(_squadStateType, 1));
        SetField(frameInput, _frameInputType, "squadAliveCounts", new[] { 7 });
        SetField(frameInput, _frameInputType, "agentAssignments", Array.CreateInstance(_agentAssignmentType, 1));

        MethodInfo method = _legacyAdapterType.GetMethod("CopyToPacket", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        method.Invoke(null, new[] { frameInput, framePacket });

        object packetFrameParams = GetField(framePacket, _framePacketType, "frameParams");
        Assert.That(GetField(packetFrameParams, _frameParamsType, "frameIndex"), Is.EqualTo(6u));
        Assert.That(HasFlag(framePacket, "UploadSquadStates"), Is.True);
        Assert.That(HasFlag(framePacket, "UploadSquadAliveCounts"), Is.True);
        Assert.That(HasFlag(framePacket, "UploadAgentAssignments"), Is.True);
        Assert.That(HasFlag(framePacket, "UploadFormationSlots"), Is.False);
        AssertArrayLength(framePacket, _framePacketType, "events", 1);
        AssertArrayLength(framePacket, _framePacketType, "dirtyAgentRanges", 1);
        AssertArrayLength(framePacket, _framePacketType, "dirtySquadRanges", 1);
        AssertArrayLength(framePacket, _framePacketType, "squadStates", 1);
        AssertArrayLength(framePacket, _framePacketType, "squadAliveCounts", 1);
        AssertArrayLength(framePacket, _framePacketType, "agentAssignments", 1);
    }

    private Array CreateGpuEventArray(string eventTypeName, uint agentId)
    {
        Array array = Array.CreateInstance(_gpuEventType, 1);
        object gpuEvent = Activator.CreateInstance(_gpuEventType);
        SetStructField(_gpuEventType, ref gpuEvent, "type", ParseEnum(_gpuEventTypeEnum, eventTypeName));
        SetStructField(_gpuEventType, ref gpuEvent, "agentId", agentId);
        SetStructField(_gpuEventType, ref gpuEvent, "payload", new Vector4(1.0f, 2.0f, 3.0f, 4.0f));
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

    private Array CreateSpatialQueryArray(int queryId, float radius)
    {
        Array array = Array.CreateInstance(_spatialQueryRequestType, 1);
        object query = Activator.CreateInstance(_spatialQueryRequestType);
        SetStructField(_spatialQueryRequestType, ref query, "queryId", queryId);
        SetStructField(_spatialQueryRequestType, ref query, "radius", radius);
        array.SetValue(query, 0);
        return array;
    }

    private bool HasFlag(object framePacket, string flagName)
    {
        MethodInfo method = _framePacketType.GetMethod("HasFlag", BindingFlags.Public | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(framePacket, new[] { ParseEnum(_framePacketFlagsType, flagName) });
    }

    private static object ParseEnum(Type enumType, string value)
    {
        return Enum.Parse(enumType, value);
    }

    private static void InvokeMethod(object instance, Type instanceType, string methodName)
    {
        MethodInfo method = instanceType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);
        method.Invoke(instance, null);
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

    private static void AssertArrayLength(object instance, Type instanceType, string fieldName, int expectedLength)
    {
        Array array = (Array)GetField(instance, instanceType, fieldName);
        Assert.That(array.Length, Is.EqualTo(expectedLength));
    }

    private static void SetStructField<T>(Type structType, ref object boxedStruct, string fieldName, T value)
    {
        FieldInfo field = structType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field.SetValue(boxedStruct, value);
    }
}
