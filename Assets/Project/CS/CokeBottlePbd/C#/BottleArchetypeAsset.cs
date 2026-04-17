using System;
using UnityEngine;

[CreateAssetMenu(fileName = "BottleArchetypeAsset", menuName = "CokeBottlePbd/Bottle Archetype")]
public sealed class BottleArchetypeAsset : ScriptableObject
{
    [Header("渲染")]
    [Tooltip("瓶子的占位网格。为空时运行时会退回内置 Capsule。")]
    [SerializeField] private Mesh _bottleMesh;
    [Tooltip("实例化渲染所用材质。")]
    [SerializeField] private Material _instancedMaterial;

    [Header("形体")]
    [Tooltip("瓶子占位高度。")]
    [SerializeField] [Min(0.05f)] private float _bottleHeight = 0.24f;
    [Tooltip("瓶子占位半径。")]
    [SerializeField] [Min(0.01f)] private float _bottleRadius = 0.04f;
    [Tooltip("每个代理粒子的碰撞半径。")]
    [SerializeField] [Min(0.005f)] private float _particleRadius = 0.028f;
    [Tooltip("未指定网格时，是否自动按瓶体尺寸缩放内置 Capsule。")]
    [SerializeField] private bool _autoScaleBuiltInCapsule = true;
    [Tooltip("自定义网格的缩放。使用内置 Capsule 时通常可保持默认。")]
    [SerializeField] private Vector3 _meshScale = Vector3.one;
    [Tooltip("网格相对瓶体姿态中心的局部偏移。")]
    [SerializeField] private Vector3 _meshOffset = Vector3.zero;
    [Tooltip("可选的自定义粒子静止布局。数量需与每瓶粒子数一致。")]
    [SerializeField] private Vector3[] _particleRestOffsets = Array.Empty<Vector3>();

    public Mesh BottleMesh => _bottleMesh;
    public Material InstancedMaterial => _instancedMaterial;
    public float BottleHeight => _bottleHeight;
    public float BottleRadius => _bottleRadius;
    public float ParticleRadius => _particleRadius;
    public bool AutoScaleBuiltInCapsule => _autoScaleBuiltInCapsule;
    public Vector3 MeshScale => _meshScale;
    public Vector3 MeshOffset => _meshOffset;

    public bool TryGetParticleRestOffsets(int expectedCount, out Vector3[] restOffsets)
    {
        if (_particleRestOffsets != null && _particleRestOffsets.Length == expectedCount)
        {
            restOffsets = (Vector3[])_particleRestOffsets.Clone();
            return true;
        }

        restOffsets = null;
        return false;
    }

    private void OnValidate()
    {
        _bottleHeight = Mathf.Max(0.05f, _bottleHeight);
        _bottleRadius = Mathf.Max(0.01f, _bottleRadius);
        _particleRadius = Mathf.Clamp(_particleRadius, 0.005f, _bottleRadius);
    }
}
