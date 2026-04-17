using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Utils;

public class NewTreeLeafCrownRendererTests
{
    private const string ComputePath = "Assets/Project/CS/New_Tree/Shader/NewTreeLeafCrownScatter.compute";
    private const string MaterialPath = "Assets/Project/CS/New_Tree/Materials/NewTreeLeafCrown.mat";

    [StructLayout(LayoutKind.Sequential)]
    private struct LeafCardData
    {
        public Vector3 centerWS;
        public float size;
        public Vector3 outwardNormalWS;
        public float rollRadians;
        public float random01;
        public Vector3 padding;
    }

    private GameObject _gameObject;
    private Component _renderer;
    private System.Type _rendererType;

    [SetUp]
    public void SetUp()
    {
        _gameObject = new GameObject("NewTreeLeafCrownRendererTests");
        _rendererType = System.Type.GetType("NewTreeLeafCrownRenderer, Assembly-CSharp");
        Assert.That(_rendererType, Is.Not.Null);
        _renderer = _gameObject.AddComponent(_rendererType);

        SetPrivateField("scatterCompute", AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath));
        SetPrivateField("leafMaterial", AssetDatabase.LoadAssetAtPath<Material>(MaterialPath));
    }

    [TearDown]
    public void TearDown()
    {
        if (_gameObject != null)
            Object.DestroyImmediate(_gameObject);
    }

    [Test]
    public void RebuildNow_UpdatesIndirectArgsInstanceCount()
    {
        SetPrivateField("leafCount", 123);

        InvokeRebuildNow();

        GraphicsBuffer argsBuffer = GetPrivateField<GraphicsBuffer>("_argsBuffer");
        GraphicsBuffer.IndirectDrawArgs[] args = new GraphicsBuffer.IndirectDrawArgs[1];
        argsBuffer.GetData(args);

        Assert.That(args[0].instanceCount, Is.EqualTo((uint)123));
        Assert.That(args[0].vertexCountPerInstance, Is.EqualTo((uint)6));
    }

    [Test]
    public void RebuildNow_WithSameSeed_ReproducesLeafCards()
    {
        SetPrivateField("leafCount", 32);
        SetPrivateField("randomSeed", 77);
        SetPrivateField("crownRadius", 1.35f);
        SetPrivateField("leafSizeRange", new Vector2(0.1f, 0.2f));

        InvokeRebuildNow();
        LeafCardData[] first = ReadLeafCards();

        InvokeRebuildNow();
        LeafCardData[] second = ReadLeafCards();

        for (int i = 0; i < 8; i++)
        {
            Assert.That(second[i].centerWS, Is.EqualTo(first[i].centerWS).Using(Vector3EqualityComparer.Instance));
            Assert.That(second[i].outwardNormalWS, Is.EqualTo(first[i].outwardNormalWS).Using(Vector3EqualityComparer.Instance));
            Assert.That(second[i].size, Is.EqualTo(first[i].size).Within(1e-6f));
            Assert.That(second[i].rollRadians, Is.EqualTo(first[i].rollRadians).Within(1e-6f));
            Assert.That(second[i].random01, Is.EqualTo(first[i].random01).Within(1e-6f));
        }
    }

    [Test]
    public void RebuildNow_KeepsCardsInsideConfiguredRadiusAndSizeRange()
    {
        SetPrivateField("leafCount", 128);
        SetPrivateField("crownRadius", 1.7f);
        SetPrivateField("leafSizeRange", new Vector2(0.11f, 0.22f));
        _gameObject.transform.position = new Vector3(3.0f, 5.0f, -2.0f);

        InvokeRebuildNow();
        LeafCardData[] cards = ReadLeafCards();

        foreach (LeafCardData card in cards)
        {
            float distance = Vector3.Distance(_gameObject.transform.position, card.centerWS);
            Assert.That(distance, Is.GreaterThanOrEqualTo(1.7f * 0.45f - 1e-4f));
            Assert.That(distance, Is.LessThanOrEqualTo(1.7f + 1e-4f));
            Assert.That(card.size, Is.InRange(0.11f, 0.22f));
            Assert.That(card.outwardNormalWS.magnitude, Is.EqualTo(1.0f).Within(1e-4f));
        }
    }

    [Test]
    public void OnValidate_WindOnlyChanges_DoNotMarkRebuild()
    {
        InvokeRebuildNow();
        Assert.That(GetPrivateField<bool>("_needsRebuild"), Is.False);

        SetPrivateField("windStrength", 0.25f);
        InvokePrivateMethod("OnValidate");

        Assert.That(GetPrivateField<bool>("_needsRebuild"), Is.False);
    }

    [Test]
    public void ResolveWindDirection_UsesDefaultDirectionWhenInputIsZero()
    {
        MethodInfo method = _rendererType.GetMethod("ResolveWindDirection", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        Vector3 direction = (Vector3)method.Invoke(null, new object[] { Vector3.zero });
        Vector3 expected = new Vector3(1.0f, 0.0f, 0.35f).normalized;

        Assert.That(direction, Is.EqualTo(expected).Using(Vector3EqualityComparer.Instance));
    }

    private LeafCardData[] ReadLeafCards()
    {
        GraphicsBuffer leafCardsBuffer = GetPrivateField<GraphicsBuffer>("_leafCardsBuffer");
        LeafCardData[] cards = new LeafCardData[leafCardsBuffer.count];
        leafCardsBuffer.GetData(cards);
        return cards;
    }

    private void SetPrivateField<T>(string fieldName, T value)
    {
        FieldInfo field = _rendererType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        field.SetValue(_renderer, value);
    }

    private T GetPrivateField<T>(string fieldName)
    {
        FieldInfo field = _rendererType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");
        return (T)field.GetValue(_renderer);
    }

    private void InvokeRebuildNow()
    {
        InvokePrivateMethod("RebuildNow", BindingFlags.Instance | BindingFlags.Public);
    }

    private void InvokePrivateMethod(string methodName, BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic)
    {
        MethodInfo method = _rendererType.GetMethod(methodName, bindingFlags);
        Assert.That(method, Is.Not.Null);
        method.Invoke(_renderer, null);
    }
}
