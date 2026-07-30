using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ASPGlobalTintSettings : MonoBehaviour
{
    public bool EnableGlobalTintColor = true;
    public float GlobalTintNoiseUVScale = 1.0f;
    public float GlobalTintNoiseIntensity = 1.0f;
    public float GlobalTintNoiseContrast = 1.0f;
    public Texture GlobalTintNoiseTexture;
    public float GlobalTreeSSSIntensity = 1.0f;
    public float GlobalTreeSSSAoInfluence = 1.0f;
    public float GlobalTreeSSSDistance = 5000.0f;
    public float GlobalTreeAO = 1.0f;

    private static readonly int TintNoiseTextureId = Shader.PropertyToID("ASP_GlobalTintNoiseTexture");
    private static readonly int TintNoiseUvScaleId = Shader.PropertyToID("ASP_GlobalTintNoiseUVScale");
    private static readonly int TintNoiseIntensityId = Shader.PropertyToID("ASP_GlobalTintNoiseIntensity");
    private static readonly int TintNoiseContrastId = Shader.PropertyToID("ASP_GlobalTintNoiseContrast");
    private static readonly int TintNoiseToggleId = Shader.PropertyToID("ASP_GlobalTintNoiseToggle");
    private static readonly int TreeSssIntensityId = Shader.PropertyToID("ASP_GlobalTreeSSSIntensity");
    private static readonly int TreeSssAoInfluenceId = Shader.PropertyToID("ASP_GlobalTreeSSSAOInfluence");
    private static readonly int TreeSssDistanceId = Shader.PropertyToID("ASP_GlobalTreeSSSDistance");
    private static readonly int TreeAoId = Shader.PropertyToID("ASP_GlobalTreeAO");

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
        Shader.SetGlobalFloat(TintNoiseToggleId, EnableGlobalTintColor ? 1.0f : 0.0f);
        Shader.SetGlobalFloat(TintNoiseUvScaleId, GlobalTintNoiseUVScale);
        Shader.SetGlobalFloat(TintNoiseIntensityId, GlobalTintNoiseIntensity);
        Shader.SetGlobalFloat(TintNoiseContrastId, GlobalTintNoiseContrast);
        Shader.SetGlobalFloat(TreeSssIntensityId, GlobalTreeSSSIntensity);
        Shader.SetGlobalFloat(TreeSssAoInfluenceId, GlobalTreeSSSAoInfluence);
        Shader.SetGlobalFloat(TreeSssDistanceId, GlobalTreeSSSDistance);
        Shader.SetGlobalFloat(TreeAoId, GlobalTreeAO);

        if (GlobalTintNoiseTexture != null)
            Shader.SetGlobalTexture(TintNoiseTextureId, GlobalTintNoiseTexture);
    }
}
