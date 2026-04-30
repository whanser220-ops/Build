using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

public class CrowdVatIndirectRendererSquadAliveCountTests
{
    private GameObject _gameObject;
    private Component _renderer;
    private System.Type _rendererType;
    private System.Type _squadStateType;

    [SetUp]
    public void SetUp()
    {
        _rendererType = System.Type.GetType("CrowdVatIndirectRenderer, Assembly-CSharp");
        _squadStateType = System.Type.GetType("CrowdVatSquadState, Assembly-CSharp");

        Assert.That(_rendererType, Is.Not.Null);
        Assert.That(_squadStateType, Is.Not.Null);

        _gameObject = new GameObject("CrowdVatIndirectRendererSquadAliveCountTests");
        _renderer = _gameObject.AddComponent(_rendererType);
    }

    [TearDown]
    public void TearDown()
    {
        if (_gameObject != null)
            Object.DestroyImmediate(_gameObject);
    }

    [Test]
    public void ReleaseResources_PreservesRuntimeSquadAliveCountUploadCache()
    {
        ApplySingleSquadState();

        InvokePrivateMethod("ReleaseResources");

        uint[] uploadCache = GetPrivateField<uint[]>("_runtimeSquadAliveCountUploadCache");
        Assert.That(uploadCache, Is.Not.Null);
        Assert.That(uploadCache.Length, Is.EqualTo(1));
        Assert.That(uploadCache[0], Is.EqualTo(37u));
    }

    [Test]
    public void TryGetRuntimeSquadAliveCount_WhenDirtyAndBuffersReleased_UsesCpuMirror()
    {
        ApplySingleSquadState();
        InvokePrivateMethod("ReleaseResources");

        object[] arguments = { 0, 0 };
        MethodInfo method = _rendererType.GetMethod("TryGetRuntimeSquadAliveCount", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        bool success = (bool)method.Invoke(_renderer, arguments);
        Assert.That(success, Is.True);
        Assert.That((int)arguments[1], Is.EqualTo(37));
    }

    [Test]
    public void SetSquadStates_WhenSquadCountExpands_ResizesAliveCountMirrorForNewSquad()
    {
        ApplySingleSquadState();
        SetPrivateField("_runtimeSquadAliveCountDirty", false);

        InvokePublicMethod("SetSquadStates", CreateSquadStates(2));

        Assert.That(GetPrivateField<bool>("_runtimeSquadAliveCountDirty"), Is.True);

        uint[] uploadCache = GetPrivateField<uint[]>("_runtimeSquadAliveCountUploadCache");
        Assert.That(uploadCache.Length, Is.EqualTo(2));
        Assert.That(uploadCache[0], Is.EqualTo(37u));
        Assert.That(uploadCache[1], Is.EqualTo(0u));

        object[] arguments = { 1, 0 };
        MethodInfo method = _rendererType.GetMethod("TryGetRuntimeSquadAliveCount", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        bool success = (bool)method.Invoke(_renderer, arguments);
        Assert.That(success, Is.True);
        Assert.That((int)arguments[1], Is.EqualTo(0));
    }

    [Test]
    public void TryRefreshRuntimeSquadAliveCountsImmediately_WhenBufferHasNewerData_UsesSynchronousSnapshot()
    {
        ApplySingleSquadState();
        SetPrivateField("_runtimeSquadAliveCountDirty", false);

        ComputeBuffer buffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        try
        {
            buffer.SetData(new uint[] { 12u });
            SetPrivateField("_squadAliveCountBuffer", buffer);
            SetPrivateField("_runtimeSquadAliveCountReadbackCache", new uint[] { 37u });

            MethodInfo refreshMethod = _rendererType.GetMethod("TryRefreshRuntimeSquadAliveCountsImmediately", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(refreshMethod, Is.Not.Null);

            bool refreshed = (bool)refreshMethod.Invoke(_renderer, null);
            Assert.That(refreshed, Is.True);

            object[] arguments = { 0, 0 };
            MethodInfo countMethod = _rendererType.GetMethod("TryGetRuntimeSquadAliveCount", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(countMethod, Is.Not.Null);

            bool success = (bool)countMethod.Invoke(_renderer, arguments);
            Assert.That(success, Is.True);
            Assert.That((int)arguments[1], Is.EqualTo(12));
        }
        finally
        {
            SetPrivateField<ComputeBuffer>("_squadAliveCountBuffer", null);
            buffer.Release();
        }
    }

    [Test]
    public void ShouldRenderRuntimeSquadChunk_WhenAliveCountIsZero_ReturnsFalse()
    {
        ApplySingleSquadState();
        InvokePublicMethod("SetSquadAliveCounts", new[] { 0 });

        MethodInfo method = _rendererType.GetMethod("ShouldRenderRuntimeSquadChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        bool shouldRender = (bool)method.Invoke(_renderer, new object[] { 0 });
        Assert.That(shouldRender, Is.False);
    }

    [Test]
    public void ShouldRenderRuntimeSquadChunk_WhenAliveCountIsPositive_ReturnsTrue()
    {
        ApplySingleSquadState();

        MethodInfo method = _rendererType.GetMethod("ShouldRenderRuntimeSquadChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        bool shouldRender = (bool)method.Invoke(_renderer, new object[] { 0 });
        Assert.That(shouldRender, Is.True);
    }

    private void ApplySingleSquadState()
    {
        InvokePublicMethod("SetSquadStates", CreateSquadStates(1));
        InvokePublicMethod("SetSquadAliveCounts", new[] { 37 });
    }

    private System.Array CreateSquadStates(int count)
    {
        System.Array squadStates = System.Array.CreateInstance(_squadStateType, count);
        for (int index = 0; index < count; index++)
        {
            object squadState = System.Activator.CreateInstance(_squadStateType);
            SetStructField(ref squadState, "worldCenter", new Vector3(index * 2.0f, 0.0f, 0.0f));
            SetStructField(ref squadState, "worldForward", Vector3.forward);
            SetStructField(ref squadState, "worldTarget", new Vector3(index * 2.0f, 0.0f, 4.0f));
            SetStructField(ref squadState, "formationSpacing", new Vector2(1.4f, 1.8f));
            SetStructField(ref squadState, "moveSpeed", 2.0f);
            SetStructField(ref squadState, "anchorBlend", 1.0f);
            squadStates.SetValue(squadState, index);
        }

        return squadStates;
    }

    private void SetStructField<T>(ref object boxedStruct, string fieldName, T value)
    {
        FieldInfo field = _squadStateType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        object updated = boxedStruct;
        field.SetValue(updated, value);
        boxedStruct = updated;
    }

    private void InvokePublicMethod(string methodName, object argument)
    {
        MethodInfo method = _rendererType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"Method '{methodName}' was not found.");
        method.Invoke(_renderer, new[] { argument });
    }

    private void InvokePrivateMethod(string methodName)
    {
        MethodInfo method = _rendererType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Method '{methodName}' was not found.");
        method.Invoke(_renderer, null);
    }

    private T GetPrivateField<T>(string fieldName)
    {
        FieldInfo field = _rendererType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        return (T)field.GetValue(_renderer);
    }

    private void SetPrivateField<T>(string fieldName, T value)
    {
        FieldInfo field = _rendererType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        field.SetValue(_renderer, value);
    }
}
