using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ASPTopLayerSettings : MonoBehaviour
{
    public float TopLayerHeightStart = -5000.0f;
    public float TopLayerHeightFade = 1.0f;
    public float TopLayerTreeIntensity = 1.0f;
    public float TopLayerTreeOffset = 1.0f;
    public float TopLayerTreeContrast = 1.0f;
    public float TopLayerTreeArrowDirection = 1.0f;
    public float TopLayerBottomOffset = 1.0f;
    public float TopLayerPropsIntensity = 1.0f;
    public float TopLayerPropsOffset = 1.0f;
    public float TopLayerPropsContrast = 1.0f;

    private static readonly int TreeOffsetId = Shader.PropertyToID("ASPT_TopLayerOffset");
    private static readonly int TreeContrastId = Shader.PropertyToID("ASPT_TopLayerContrast");
    private static readonly int TreeIntensityId = Shader.PropertyToID("ASPT_TopLayerIntensity");
    private static readonly int TreeArrowDirectionId = Shader.PropertyToID("ASPT_TopLayerArrowDirection");
    private static readonly int TreeBottomOffsetId = Shader.PropertyToID("ASPT_TopLayerBottomOffset");
    private static readonly int TreeHeightStartId = Shader.PropertyToID("ASPT_TopLayerHeightStart");
    private static readonly int TreeHeightFadeId = Shader.PropertyToID("ASPT_TopLayerHeightFade");
    private static readonly int PropsOffsetId = Shader.PropertyToID("ASPP_TopLayerOffset");
    private static readonly int PropsContrastId = Shader.PropertyToID("ASPP_TopLayerContrast");
    private static readonly int PropsIntensityId = Shader.PropertyToID("ASPP_TopLayerIntensity");

    private void OnEnable()
    {
        Apply();
    }

    private void Update()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Apply()
    {
        Shader.SetGlobalFloat(TreeOffsetId, TopLayerTreeOffset);
        Shader.SetGlobalFloat(TreeContrastId, TopLayerTreeContrast);
        Shader.SetGlobalFloat(TreeIntensityId, TopLayerTreeIntensity);
        Shader.SetGlobalFloat(TreeArrowDirectionId, TopLayerTreeArrowDirection);
        Shader.SetGlobalFloat(TreeBottomOffsetId, TopLayerBottomOffset);
        Shader.SetGlobalFloat(TreeHeightStartId, TopLayerHeightStart);
        Shader.SetGlobalFloat(TreeHeightFadeId, TopLayerHeightFade);
        Shader.SetGlobalFloat(PropsOffsetId, TopLayerPropsOffset);
        Shader.SetGlobalFloat(PropsContrastId, TopLayerPropsContrast);
        Shader.SetGlobalFloat(PropsIntensityId, TopLayerPropsIntensity);
    }
}
