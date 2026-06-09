using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CrowdVatCodexAgentControllerTests
{
    private Type _agentType;
    private Type _observationType;
    private Type _settingsType;
    private MethodInfo _buildPlanMethod;
    private MethodInfo _tryParseOnlinePlanBatchMethod;
    private MethodInfo _buildCodexCliOutputSchemaMethod;

    [SetUp]
    public void SetUp()
    {
        _agentType = Type.GetType("CrowdVatCodexAgentController, Assembly-CSharp");
        _observationType = Type.GetType("CrowdVatCodexAgentObservation, Assembly-CSharp");
        _settingsType = Type.GetType("CrowdVatCodexAgentPlannerSettings, Assembly-CSharp");

        Assert.That(_agentType, Is.Not.Null);
        Assert.That(_observationType, Is.Not.Null);
        Assert.That(_settingsType, Is.Not.Null);

        _buildPlanMethod = _agentType.GetMethod("BuildPlan", BindingFlags.Public | BindingFlags.Static);
        Assert.That(_buildPlanMethod, Is.Not.Null);
        _tryParseOnlinePlanBatchMethod = _agentType.GetMethod("TryParseOnlinePlanBatchJson", BindingFlags.Public | BindingFlags.Static);
        Assert.That(_tryParseOnlinePlanBatchMethod, Is.Not.Null);
        _buildCodexCliOutputSchemaMethod = _agentType.GetMethod("BuildCodexCliOutputSchemaJson", BindingFlags.Public | BindingFlags.Static);
        Assert.That(_buildCodexCliOutputSchemaMethod, Is.Not.Null);
    }

    [Test]
    public void BuildPlan_WhenNoTarget_HoldsCurrentPosition()
    {
        object observation = CreateObservation(
            controlledSquadIndex: 4,
            controlledCenter: new Vector3(2.0f, 0.0f, 3.0f),
            targetSquadIndex: -1,
            targetCenter: Vector3.zero,
            targetDistance: 0.0f,
            hasTarget: false);
        object settings = CreateSettings();

        object plan = BuildPlan(observation, settings);

        Assert.That(GetFieldValue(plan, "hasPlan"), Is.EqualTo(true));
        Assert.That(GetFieldValue(plan, "commandType").ToString(), Is.EqualTo("Hold"));
        AssertVector3Field(plan, "commandPoint", new Vector3(2.0f, 0.0f, 3.0f));
        Assert.That(GetFieldValue(plan, "reason"), Is.EqualTo("no_target"));
    }

    [Test]
    public void BuildPlan_WhenTargetTooClose_RetreatsFromTarget()
    {
        object observation = CreateObservation(
            controlledSquadIndex: 5,
            controlledCenter: Vector3.zero,
            targetSquadIndex: 1,
            targetCenter: new Vector3(0.0f, 0.0f, 3.0f),
            targetDistance: 3.0f,
            hasTarget: true);
        object settings = CreateSettings();

        object plan = BuildPlan(observation, settings);

        Assert.That(GetFieldValue(plan, "commandType").ToString(), Is.EqualTo("Retreat"));
        AssertVector3Field(plan, "commandPoint", new Vector3(0.0f, 0.0f, 3.0f));
        Assert.That(GetFieldValue(plan, "reason"), Is.EqualTo("too_close"));
    }

    [Test]
    public void BuildPlan_WhenTargetInsideHoldDistance_HoldsFacingTarget()
    {
        object observation = CreateObservation(
            controlledSquadIndex: 5,
            controlledCenter: Vector3.zero,
            targetSquadIndex: 1,
            targetCenter: new Vector3(0.0f, 0.0f, 10.0f),
            targetDistance: 10.0f,
            hasTarget: true);
        object settings = CreateSettings();

        object plan = BuildPlan(observation, settings);

        Assert.That(GetFieldValue(plan, "commandType").ToString(), Is.EqualTo("Hold"));
        AssertVector3Field(plan, "commandPoint", new Vector3(0.0f, 0.0f, 10.0f));
        Assert.That(GetFieldValue(plan, "reason"), Is.EqualTo("in_firing_range"));
    }

    [Test]
    public void BuildPlan_WhenTargetFar_ChargesWithFlankOffset()
    {
        object observation = CreateObservation(
            controlledSquadIndex: 2,
            controlledCenter: Vector3.zero,
            targetSquadIndex: 1,
            targetCenter: new Vector3(0.0f, 0.0f, 40.0f),
            targetDistance: 40.0f,
            hasTarget: true);
        object settings = CreateSettings();

        object plan = BuildPlan(observation, settings);

        Assert.That(GetFieldValue(plan, "commandType").ToString(), Is.EqualTo("Charge"));
        AssertVector3Field(plan, "commandPoint", new Vector3(-3.0f, 0.0f, 40.0f));
        Assert.That(GetFieldValue(plan, "reason"), Is.EqualTo("closing_distance"));
    }

    [Test]
    public void TryParseOnlinePlanBatchJson_WhenStructuredPlanReturned_ParsesPlans()
    {
        const string json = "{\"plans\":[{\"controlledSquadIndex\":4,\"targetSquadIndex\":1,\"commandType\":\"Advance\",\"commandPoint\":{\"x\":3,\"y\":0,\"z\":12},\"reason\":\"online_test\"}]}";
        object[] arguments = { json, null };

        bool success = (bool)_tryParseOnlinePlanBatchMethod.Invoke(null, arguments);

        Assert.That(success, Is.True);
        Array plans = arguments[1] as Array;
        Assert.That(plans, Is.Not.Null);
        Assert.That(plans.Length, Is.EqualTo(1));
        object plan = plans.GetValue(0);
        Assert.That(GetFieldValue(plan, "controlledSquadIndex"), Is.EqualTo(4));
        Assert.That(GetFieldValue(plan, "targetSquadIndex"), Is.EqualTo(1));
        Assert.That(GetFieldValue(plan, "commandType").ToString(), Is.EqualTo("Advance"));
        AssertVector3Field(plan, "commandPoint", new Vector3(3.0f, 0.0f, 12.0f));
        Assert.That(GetFieldValue(plan, "reason"), Is.EqualTo("online_test"));
    }

    [Test]
    public void BuildCodexCliOutputSchemaJson_IncludesPlanContract()
    {
        string schema = (string)_buildCodexCliOutputSchemaMethod.Invoke(null, null);

        Assert.That(schema, Does.Contain("\"plans\""));
        Assert.That(schema, Does.Contain("\"commandType\""));
        Assert.That(schema, Does.Contain("\"Hold\""));
        Assert.That(schema, Does.Contain("\"Regroup\""));
    }

    private object BuildPlan(object observation, object settings)
    {
        return _buildPlanMethod.Invoke(null, new[] { observation, settings });
    }

    private object CreateObservation(
        int controlledSquadIndex,
        Vector3 controlledCenter,
        int targetSquadIndex,
        Vector3 targetCenter,
        float targetDistance,
        bool hasTarget)
    {
        object observation = Activator.CreateInstance(_observationType);
        SetFieldValue(observation, "controlledSquadIndex", controlledSquadIndex);
        SetFieldValue(observation, "controlledCenter", controlledCenter);
        SetFieldValue(observation, "targetSquadIndex", targetSquadIndex);
        SetFieldValue(observation, "targetCenter", targetCenter);
        SetFieldValue(observation, "targetDistance", targetDistance);
        SetFieldValue(observation, "hasTarget", hasTarget);
        return observation;
    }

    private object CreateSettings()
    {
        object settings = Activator.CreateInstance(_settingsType);
        SetFieldValue(settings, "retreatDistance", 5.0f);
        SetFieldValue(settings, "holdDistance", 11.0f);
        SetFieldValue(settings, "chargeDistance", 26.0f);
        SetFieldValue(settings, "flankOffsetDistance", 3.0f);
        SetFieldValue(settings, "allowRetreat", true);
        return settings;
    }

    private static void SetFieldValue(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null);
        field.SetValue(instance, value);
    }

    private static object GetFieldValue(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null);
        return field.GetValue(instance);
    }

    private static void AssertVector3Field(object instance, string fieldName, Vector3 expected)
    {
        Vector3 actual = (Vector3)GetFieldValue(instance, fieldName);
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
    }
}
