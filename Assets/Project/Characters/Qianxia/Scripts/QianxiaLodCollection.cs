using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class QianxiaLodCollection : MonoBehaviour
{
    [Header("LOD Group")]
    [SerializeField] private LODGroup _lodGroup;

    [Header("LOD0")]
    [SerializeField] private float _lod0ScreenRelativeTransitionHeight = 0.6f;
    [SerializeField] private GameObject _lod0Root;
    [SerializeField] private Animator _lod0Animator;
    [SerializeField] private Renderer[] _lod0Renderers = Array.Empty<Renderer>();

    [Header("LOD1")]
    [SerializeField] private float _lod1ScreenRelativeTransitionHeight = 0.25f;
    [SerializeField] private GameObject _lod1Root;
    [SerializeField] private Animator _lod1Animator;
    [SerializeField] private Renderer[] _lod1Renderers = Array.Empty<Renderer>();

    [Header("LOD2")]
    [SerializeField] private float _lod2ScreenRelativeTransitionHeight = 0.02f;
    [SerializeField] private GameObject _lod2Root;
    [SerializeField] private Animator _lod2Animator;
    [SerializeField] private Renderer[] _lod2Renderers = Array.Empty<Renderer>();

    public LODGroup LodGroup => _lodGroup;
    public float Lod0ScreenRelativeTransitionHeight => _lod0ScreenRelativeTransitionHeight;
    public float Lod1ScreenRelativeTransitionHeight => _lod1ScreenRelativeTransitionHeight;
    public float Lod2ScreenRelativeTransitionHeight => _lod2ScreenRelativeTransitionHeight;
    public GameObject Lod0Root => _lod0Root;
    public GameObject Lod1Root => _lod1Root;
    public GameObject Lod2Root => _lod2Root;
    public Animator Lod0Animator => _lod0Animator;
    public Animator Lod1Animator => _lod1Animator;
    public Animator Lod2Animator => _lod2Animator;

    private void OnValidate()
    {
        RefreshCachedReferences();
    }

    public void Configure(
        LODGroup lodGroup,
        GameObject lod0Root,
        float lod0ScreenRelativeTransitionHeight,
        GameObject lod1Root,
        float lod1ScreenRelativeTransitionHeight,
        GameObject lod2Root,
        float lod2ScreenRelativeTransitionHeight)
    {
        _lodGroup = lodGroup;
        _lod0Root = lod0Root;
        _lod1Root = lod1Root;
        _lod2Root = lod2Root;
        _lod0ScreenRelativeTransitionHeight = lod0ScreenRelativeTransitionHeight;
        _lod1ScreenRelativeTransitionHeight = lod1ScreenRelativeTransitionHeight;
        _lod2ScreenRelativeTransitionHeight = lod2ScreenRelativeTransitionHeight;
        RefreshCachedReferences();
    }

    public void RefreshCachedReferences()
    {
        _lod0Animator = ResolveAnimator(_lod0Root);
        _lod1Animator = ResolveAnimator(_lod1Root);
        _lod2Animator = ResolveAnimator(_lod2Root);
        _lod0Renderers = ResolveRenderers(_lod0Root);
        _lod1Renderers = ResolveRenderers(_lod1Root);
        _lod2Renderers = ResolveRenderers(_lod2Root);
    }

    public GameObject GetLodRoot(int lodIndex)
    {
        return lodIndex switch
        {
            0 => _lod0Root,
            1 => _lod1Root,
            2 => _lod2Root,
            _ => throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index must be between 0 and 2.")
        };
    }

    public Animator GetAnimator(int lodIndex)
    {
        return lodIndex switch
        {
            0 => _lod0Animator,
            1 => _lod1Animator,
            2 => _lod2Animator,
            _ => throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index must be between 0 and 2.")
        };
    }

    public Renderer[] GetRenderers(int lodIndex)
    {
        return lodIndex switch
        {
            0 => _lod0Renderers,
            1 => _lod1Renderers,
            2 => _lod2Renderers,
            _ => throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index must be between 0 and 2.")
        };
    }

    public Mesh[] GetSharedMeshes(int lodIndex)
    {
        Renderer[] renderers = GetRenderers(lodIndex);
        List<Mesh> meshes = new List<Mesh>(renderers.Length);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            if (renderers[i] is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                if (skinnedMeshRenderer.sharedMesh != null)
                    meshes.Add(skinnedMeshRenderer.sharedMesh);

                continue;
            }

            if (renderers[i] is MeshRenderer meshRenderer &&
                meshRenderer.TryGetComponent(out MeshFilter meshFilter) &&
                meshFilter.sharedMesh != null)
            {
                meshes.Add(meshFilter.sharedMesh);
            }
        }

        return meshes.ToArray();
    }

    public Bounds CalculateLodBounds(int lodIndex)
    {
        Renderer[] renderers = GetRenderers(lodIndex);
        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(renderers[i].bounds);
        }

        if (hasBounds)
            return bounds;

        return new Bounds(transform.position, Vector3.one);
    }

    private static Animator ResolveAnimator(GameObject root)
    {
        return root == null ? null : root.GetComponent<Animator>();
    }

    private static Renderer[] ResolveRenderers(GameObject root)
    {
        return root == null ? Array.Empty<Renderer>() : root.GetComponentsInChildren<Renderer>(true);
    }
}
