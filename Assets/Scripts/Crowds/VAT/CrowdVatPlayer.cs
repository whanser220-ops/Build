using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class CrowdVatPlayer : MonoBehaviour
{
    private static readonly int BoneAnimationTextureId = Shader.PropertyToID("_BoneAnimationTex");
    private static readonly int FrameDataId = Shader.PropertyToID("_FrameData");
    private static readonly int BoneTextureSizeId = Shader.PropertyToID("_BoneTextureSize");

    [SerializeField] private CrowdVatAnimationAsset _animationAsset;
    [SerializeField] private string _defaultClipName;
    [SerializeField] private bool _playOnEnable = true;
    [SerializeField] private bool _loopOverride = true;
    [SerializeField] private float _playbackSpeed = 1.0f;
    [SerializeField] [Range(0.0f, 1.0f)] private float _normalizedStartOffset;
    [SerializeField] private MeshFilter _meshFilter;
    [SerializeField] private MeshRenderer _meshRenderer;

    private CrowdVatAnimationAsset.ClipInfo _currentClip;
    private MaterialPropertyBlock _propertyBlock;
    private bool _hasClip;
    private bool _isPlaying;
    private float _clipTime;

    public CrowdVatAnimationAsset AnimationAsset => _animationAsset;
    public MeshFilter MeshFilter => _meshFilter;
    public MeshRenderer MeshRenderer => _meshRenderer;
    public Transform RenderRoot => _meshRenderer != null ? _meshRenderer.transform : null;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
        ApplyAnimationAssetBindings();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ApplyAnimationAssetBindings();

        if (_playOnEnable)
            PlayClip(_defaultClipName, _normalizedStartOffset);
        else
            ApplyCurrentFrameData();
    }

    private void Update()
    {
        if (!_isPlaying || !_hasClip)
            return;

        AdvanceClip(Time.deltaTime * _playbackSpeed);
        ApplyCurrentFrameData();
    }

    private void OnValidate()
    {
        ResolveReferences();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorApplication.delayCall -= ApplyEditorValidation;
            EditorApplication.delayCall += ApplyEditorValidation;
            return;
        }
#endif

        ApplyAnimationAssetBindings();
        ApplyCurrentFrameData();
    }

    public void Configure(
        CrowdVatAnimationAsset animationAsset,
        MeshRenderer meshRenderer,
        MeshFilter meshFilter,
        string defaultClipName)
    {
        _animationAsset = animationAsset;
        _meshRenderer = meshRenderer;
        _meshFilter = meshFilter;
        _defaultClipName = defaultClipName;

        ResolveReferences();
        ApplyAnimationAssetBindings();
        PlayClip(defaultClipName, _normalizedStartOffset);
    }

    public void PlayClip(string clipName, float normalizedTime = 0.0f)
    {
        if (_animationAsset == null || !_animationAsset.TryGetClip(clipName, out _currentClip))
            return;

        _hasClip = true;
        _isPlaying = true;
        _clipTime = Mathf.Clamp01(normalizedTime) * Mathf.Max(_currentClip.LengthSeconds, 0.0f);
        ApplyCurrentFrameData();
    }

    public void Stop()
    {
        _isPlaying = false;
        ApplyCurrentFrameData();
    }

    public void Resume()
    {
        if (_hasClip)
            _isPlaying = true;
    }

    public void SetNormalizedTime(float normalizedTime)
    {
        if (!_hasClip)
            return;

        _clipTime = Mathf.Clamp01(normalizedTime) * Mathf.Max(_currentClip.LengthSeconds, 0.0f);
        ApplyCurrentFrameData();
    }

    private void ResolveReferences()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponentInChildren<MeshFilter>(true);

        if (_meshRenderer == null)
            _meshRenderer = GetComponentInChildren<MeshRenderer>(true);

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();
    }

    private void ApplyAnimationAssetBindings()
    {
        if (_animationAsset == null)
            return;

        if (_meshFilter != null && _animationAsset.Mesh != null)
            _meshFilter.sharedMesh = _animationAsset.Mesh;

        if (_meshRenderer != null && _animationAsset.Materials != null && _animationAsset.Materials.Length > 0)
            _meshRenderer.sharedMaterials = _animationAsset.Materials;
    }

    private void AdvanceClip(float deltaTime)
    {
        float clipLength = Mathf.Max(_currentClip.LengthSeconds, 0.0f);
        if (clipLength <= Mathf.Epsilon)
            return;

        _clipTime += deltaTime;

        bool shouldLoop = _loopOverride || _currentClip.Loop;
        if (shouldLoop)
        {
            _clipTime = Mathf.Repeat(_clipTime, clipLength);
            return;
        }

        _clipTime = Mathf.Clamp(_clipTime, 0.0f, clipLength);
        if (Mathf.Approximately(_clipTime, clipLength))
            _isPlaying = false;
    }

    private void ApplyCurrentFrameData()
    {
        if (_meshRenderer == null || _animationAsset == null)
            return;

        _meshRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetTexture(BoneAnimationTextureId, _animationAsset.BoneAnimationTexture);

        Texture2D boneAnimationTexture = _animationAsset.BoneAnimationTexture;
        if (boneAnimationTexture != null)
        {
            _propertyBlock.SetVector(
                BoneTextureSizeId,
                new Vector4(
                    boneAnimationTexture.width,
                    boneAnimationTexture.height,
                    1.0f / Mathf.Max(1, boneAnimationTexture.width),
                    1.0f / Mathf.Max(1, boneAnimationTexture.height)));
        }

        Vector4 frameData = Vector4.zero;
        if (_hasClip)
            frameData = BuildFrameData(_currentClip);

        _propertyBlock.SetVector(FrameDataId, frameData);
        _meshRenderer.SetPropertyBlock(_propertyBlock);
    }

    private Vector4 BuildFrameData(CrowdVatAnimationAsset.ClipInfo clip)
    {
        if (clip.FrameCount <= 1)
            return new Vector4(clip.StartFrame, clip.StartFrame, 0.0f, 0.0f);

        float clipLength = Mathf.Max(clip.LengthSeconds, 0.0f);
        bool shouldLoop = _loopOverride || clip.Loop;
        float normalizedTime = clipLength <= Mathf.Epsilon
            ? 0.0f
            : (shouldLoop ? Mathf.Repeat(_clipTime / clipLength, 1.0f) : Mathf.Clamp01(_clipTime / clipLength));

        float frameFloat = shouldLoop
            ? normalizedTime * clip.FrameCount
            : normalizedTime * (clip.FrameCount - 1);

        int localFrame0 = Mathf.Clamp(Mathf.FloorToInt(frameFloat), 0, clip.FrameCount - 1);
        int localFrame1 = shouldLoop
            ? (localFrame0 + 1) % clip.FrameCount
            : Mathf.Min(localFrame0 + 1, clip.FrameCount - 1);
        float blend = Mathf.Clamp01(frameFloat - localFrame0);

        return new Vector4(
            clip.StartFrame + localFrame0,
            clip.StartFrame + localFrame1,
            blend,
            0.0f);
    }

#if UNITY_EDITOR
    private void ApplyEditorValidation()
    {
        if (this == null)
            return;

        ResolveReferences();
        ApplyAnimationAssetBindings();
        ApplyCurrentFrameData();
    }
#endif
}
