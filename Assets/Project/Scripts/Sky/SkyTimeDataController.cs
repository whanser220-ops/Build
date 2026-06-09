using UnityEngine;

public class SkyTimeDataController : MonoBehaviour
{
    private const int GradientResolution = 128;

    [System.Serializable]
    public class SkyTimeDataCollection
    {
        public SkyTimeData time0;
        public SkyTimeData time6;
        public SkyTimeData time12;
        public SkyTimeData time18;
    }

    public SkyTimeDataCollection skyTimeDataCollection = new SkyTimeDataCollection();

    private SkyTimeData _runtimeSkyTimeData;
    private Texture2D _skyGradientTexture;
    private Texture2D _horizonGradientTexture;

    private void OnEnable()
    {
        EnsureRuntimeResources();
    }

    private void OnDisable()
    {
        ReleaseTexture(ref _skyGradientTexture);
        ReleaseTexture(ref _horizonGradientTexture);
        ReleaseRuntimeData();
    }

    public SkyTimeData GetSkyTimeData(float time)
    {
        if (!HasRequiredTimeData())
        {
            return null;
        }

        EnsureRuntimeResources();

        SkyTimeData start = skyTimeDataCollection.time0;
        SkyTimeData end = skyTimeDataCollection.time0;
        float normalizedTime = Mathf.Repeat(time, 24f);

        if (normalizedTime > 0f && normalizedTime < 6f)
        {
            start = skyTimeDataCollection.time0;
            end = skyTimeDataCollection.time6;
        }
        else if (normalizedTime >= 6f && normalizedTime < 12f)
        {
            start = skyTimeDataCollection.time6;
            end = skyTimeDataCollection.time12;
        }
        else if (normalizedTime >= 12f && normalizedTime < 18f)
        {
            start = skyTimeDataCollection.time12;
            end = skyTimeDataCollection.time18;
        }
        else if (normalizedTime >= 18f)
        {
            start = skyTimeDataCollection.time18;
            end = skyTimeDataCollection.time0;
        }

        float lerpValue = (normalizedTime % 6f) / 6f;

        UpdateGradientTexture(start.skyColorGradient, end.skyColorGradient, _skyGradientTexture, lerpValue);
        UpdateGradientTexture(start.horizonColorGradient, end.horizonColorGradient, _horizonGradientTexture, lerpValue);

        _runtimeSkyTimeData.skyColorGradientTex = _skyGradientTexture;
        _runtimeSkyTimeData.horizonColorGradientTex = _horizonGradientTexture;
        _runtimeSkyTimeData.scatteringIntensity = Mathf.Lerp(start.scatteringIntensity, end.scatteringIntensity, lerpValue);
        _runtimeSkyTimeData.starIntensity = Mathf.Lerp(start.starIntensity, end.starIntensity, lerpValue);
        _runtimeSkyTimeData.milkywayIntensity = Mathf.Lerp(start.milkywayIntensity, end.milkywayIntensity, lerpValue);

        return _runtimeSkyTimeData;
    }

    private bool HasRequiredTimeData()
    {
        return skyTimeDataCollection.time0 != null
            && skyTimeDataCollection.time6 != null
            && skyTimeDataCollection.time12 != null
            && skyTimeDataCollection.time18 != null;
    }

    private void EnsureRuntimeResources()
    {
        if (_runtimeSkyTimeData == null)
        {
            _runtimeSkyTimeData = ScriptableObject.CreateInstance<SkyTimeData>();
            _runtimeSkyTimeData.hideFlags = HideFlags.HideAndDontSave;
        }

        _skyGradientTexture = CreateGradientTexture(_skyGradientTexture, $"{name}_SkyGradient");
        _horizonGradientTexture = CreateGradientTexture(_horizonGradientTexture, $"{name}_HorizonGradient");
    }

    private Texture2D CreateGradientTexture(Texture2D texture, string textureName)
    {
        if (texture != null && texture.width == GradientResolution && texture.height == 1)
        {
            return texture;
        }

        ReleaseTexture(ref texture);
        texture = new Texture2D(GradientResolution, 1, TextureFormat.RGBAFloat, false, true)
        {
            name = textureName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        return texture;
    }

    private static void UpdateGradientTexture(Gradient startGradient, Gradient endGradient, Texture2D texture, float lerpValue)
    {
        if (startGradient == null || endGradient == null || texture == null)
        {
            return;
        }

        int lastPixelIndex = texture.width - 1;
        for (int i = 0; i < texture.width; i++)
        {
            float sample = lastPixelIndex > 0 ? i / (float)lastPixelIndex : 0f;
            Color start = startGradient.Evaluate(sample).linear;
            Color end = endGradient.Evaluate(sample).linear;
            texture.SetPixel(i, 0, Color.Lerp(start, end, lerpValue));
        }

        texture.Apply(false, false);
    }

    private void ReleaseRuntimeData()
    {
        if (_runtimeSkyTimeData == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(_runtimeSkyTimeData);
        }
        else
        {
            DestroyImmediate(_runtimeSkyTimeData);
        }

        _runtimeSkyTimeData = null;
    }

    private void ReleaseTexture(ref Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(texture);
        }

        texture = null;
    }
}
