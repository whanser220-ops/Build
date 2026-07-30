using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ASPGlobalWindSettings : MonoBehaviour
{
    public int Gata;
    public bool EnableTest = true;
    public bool EnableGlobalWind = true;
    public bool EnableWindSwitch;
    public bool EnableStaticMeshSupport;
    public float WindTreeAmplitude = 1.0f;
    public float WindTreeSpeed = 1.0f;
    public float WindTreeFlexibility = 1.0f;
    public float WindTreeLeafAmplitude = 1.0f;
    public float WindTreeLeafSpeed = 1.0f;
    public float WindTreeLeafTurbulence = 1.0f;
    public float WindTreeLeafOffset = 1.0f;
    public float WindGrassAmplitude = 1.0f;
    public float WindGrassSpeed = 1.0f;
    public float WindGrassTurbulence = 1.0f;
    public float WindGrassFlexibility = 1.0f;
    public float WindGrassWavesAmplitude = 1.0f;
    public float WindGrassWavesSpeed = 1.0f;
    public float WindGrassWavesScale = 1.0f;
    public Texture WindGrassWavesNoiseTexture;

    private static readonly int WindDirectionId = Shader.PropertyToID("ASPW_WindDirection");
    private static readonly int WindToggleId = Shader.PropertyToID("ASPW_WindToggle");
    private static readonly int WindTreeAmplitudeId = Shader.PropertyToID("ASPW_WindTreeAmplitude");
    private static readonly int WindTreeSpeedId = Shader.PropertyToID("ASPW_WindTreeSpeed");
    private static readonly int WindTreeFlexibilityId = Shader.PropertyToID("ASPW_WindTreeFlexibility");
    private static readonly int WindTreeLeafAmplitudeId = Shader.PropertyToID("ASPW_WindTreeLeafAmplitude");
    private static readonly int WindTreeLeafSpeedId = Shader.PropertyToID("ASPW_WindTreeLeafSpeed");
    private static readonly int WindTreeLeafTurbulenceId = Shader.PropertyToID("ASPW_WindTreeLeafTurbulence");
    private static readonly int WindTreeLeafOffsetId = Shader.PropertyToID("ASPW_WindTreeLeafOffset");
    private static readonly int WindGrassAmplitudeId = Shader.PropertyToID("ASPW_WindGrassAmplitude");
    private static readonly int WindGrassSpeedId = Shader.PropertyToID("ASPW_WindGrassSpeed");
    private static readonly int WindGrassTurbulenceId = Shader.PropertyToID("ASPW_WindGrassTurbulence");
    private static readonly int WindGrassFlexibilityId = Shader.PropertyToID("ASPW_WindGrassFlexibility");
    private static readonly int WindGrassWavesAmplitudeId = Shader.PropertyToID("ASPW_WindGrassWavesAmplitude");
    private static readonly int WindGrassWavesSpeedId = Shader.PropertyToID("ASPW_WindGrassWavesSpeed");
    private static readonly int WindGrassWavesScaleId = Shader.PropertyToID("ASPW_WindGrassWavesScale");
    private static readonly int WindGrassWavesNoiseTextureId = Shader.PropertyToID("ASPW_WindGrassWavesNoiseTexture");

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
        Vector3 direction = transform.forward;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.forward;

        direction.Normalize();
        Shader.SetGlobalVector(WindDirectionId, new Vector4(direction.x, direction.y, direction.z, 0.0f));
        Shader.SetGlobalFloat(WindToggleId, EnableGlobalWind ? 1.0f : 0.0f);
        Shader.SetGlobalFloat(WindTreeAmplitudeId, WindTreeAmplitude);
        Shader.SetGlobalFloat(WindTreeSpeedId, WindTreeSpeed);
        Shader.SetGlobalFloat(WindTreeFlexibilityId, WindTreeFlexibility);
        Shader.SetGlobalFloat(WindTreeLeafAmplitudeId, WindTreeLeafAmplitude);
        Shader.SetGlobalFloat(WindTreeLeafSpeedId, WindTreeLeafSpeed);
        Shader.SetGlobalFloat(WindTreeLeafTurbulenceId, WindTreeLeafTurbulence);
        Shader.SetGlobalFloat(WindTreeLeafOffsetId, WindTreeLeafOffset);
        Shader.SetGlobalFloat(WindGrassAmplitudeId, WindGrassAmplitude);
        Shader.SetGlobalFloat(WindGrassSpeedId, WindGrassSpeed);
        Shader.SetGlobalFloat(WindGrassTurbulenceId, WindGrassTurbulence);
        Shader.SetGlobalFloat(WindGrassFlexibilityId, WindGrassFlexibility);
        Shader.SetGlobalFloat(WindGrassWavesAmplitudeId, WindGrassWavesAmplitude);
        Shader.SetGlobalFloat(WindGrassWavesSpeedId, WindGrassWavesSpeed);
        Shader.SetGlobalFloat(WindGrassWavesScaleId, WindGrassWavesScale);

        if (WindGrassWavesNoiseTexture != null)
            Shader.SetGlobalTexture(WindGrassWavesNoiseTextureId, WindGrassWavesNoiseTexture);
    }
}
