using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Project/Crowd VAT Animation Asset", fileName = "CrowdVatAnimationAsset")]
public sealed class CrowdVatAnimationAsset : ScriptableObject
{
    [Serializable]
    public struct ClipInfo
    {
        [SerializeField] private string _name;
        [SerializeField] private int _startFrame;
        [SerializeField] private int _frameCount;
        [SerializeField] private float _lengthSeconds;
        [SerializeField] private bool _loop;

        public string Name => _name;
        public int StartFrame => _startFrame;
        public int FrameCount => _frameCount;
        public float LengthSeconds => _lengthSeconds;
        public bool Loop => _loop;

#if UNITY_EDITOR
        public ClipInfo(string name, int startFrame, int frameCount, float lengthSeconds, bool loop)
        {
            _name = name;
            _startFrame = startFrame;
            _frameCount = frameCount;
            _lengthSeconds = lengthSeconds;
            _loop = loop;
        }
#endif
    }

    [SerializeField] private Mesh _mesh;
    [SerializeField] private Texture2D _boneAnimationTexture;
    [SerializeField] private Material[] _materials = Array.Empty<Material>();
    [SerializeField] private Bounds _meshBounds = new Bounds(Vector3.zero, Vector3.one);
    [SerializeField] private int _boneCount;
    [SerializeField] private int _bakeFrameRate = 30;
    [SerializeField] private ClipInfo[] _clips = Array.Empty<ClipInfo>();

    public Mesh Mesh => _mesh;
    public Texture2D BoneAnimationTexture => _boneAnimationTexture;
    public Material[] Materials => _materials;
    public Bounds MeshBounds => _meshBounds;
    public int BoneCount => _boneCount;
    public int BakeFrameRate => _bakeFrameRate;
    public ClipInfo[] Clips => _clips;

    public bool TryGetClip(string clipName, out ClipInfo clip)
    {
        if (_clips == null || _clips.Length == 0)
        {
            clip = default;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(clipName))
        {
            for (int i = 0; i < _clips.Length; i++)
            {
                if (string.Equals(_clips[i].Name, clipName, StringComparison.Ordinal))
                {
                    clip = _clips[i];
                    return true;
                }
            }
        }

        clip = _clips[0];
        return true;
    }

#if UNITY_EDITOR
    public void SetData(
        Mesh mesh,
        Texture2D boneAnimationTexture,
        Material[] materials,
        Bounds meshBounds,
        int boneCount,
        int bakeFrameRate,
        ClipInfo[] clips)
    {
        _mesh = mesh;
        _boneAnimationTexture = boneAnimationTexture;
        _materials = materials ?? Array.Empty<Material>();
        _meshBounds = meshBounds;
        _boneCount = boneCount;
        _bakeFrameRate = bakeFrameRate;
        _clips = clips ?? Array.Empty<ClipInfo>();
    }
#endif
}
