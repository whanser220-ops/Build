using UnityEngine;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Vector3 = UnityEngine.Vector3;

public class SkyController : MonoBehaviour
{
    [Range(0f, 24f)]
    public float time = 14;

    [SerializeField] private float _timeSpeed = 0.4f;

    public SkyTimeDataController skyTimeDataController;
    public Light mainLight_Sun;
    public Light mainLight_Moon;
    public Transform sunTransform;
    public Transform moonTransform;
    public Transform MilkyWayTransform;

    public Material targetMaterialCloudA;
    public Material targetMaterialCloudB;
    public Material targetMaterialSkybox;

    public string SkyGradientTex = "_SkyGradientTex";
    public string HorizonGradientTex = "_HorizonGradientTex";
    public string sunDirectionPropertyName = "_SunDirection";
    public string moonDirectionPropertyName = "_MoonDirection";

    private SkyTimeData _currentSkyTimeData;

    private void OnEnable()
    {
        skyTimeDataController ??= GetComponent<SkyTimeDataController>();
    }

    private void Update()
    {
        if (!HasRequiredReferences())
        {
            return;
        }

        time = Mathf.Repeat(time + Time.deltaTime * _timeSpeed, 24f);
        time = 14;
        _currentSkyTimeData = skyTimeDataController.GetSkyTimeData(time);
        if (_currentSkyTimeData == null)
        {
            return;
        }

        Matrix4x4 moonLocalToWorld = mainLight_Moon.transform.localToWorldMatrix;
        targetMaterialSkybox.SetMatrix("SunLToW", moonLocalToWorld);
        Matrix4x4 milkyLocalToWorld = MilkyWayTransform.localToWorldMatrix;
        targetMaterialSkybox.SetMatrix("MilkyLToW", milkyLocalToWorld);

        ControlSunAndMoonTransform();
        SetProperties();
    }

    private bool HasRequiredReferences()
    {
        return skyTimeDataController != null
            && mainLight_Sun != null
            && mainLight_Moon != null
            && sunTransform != null
            && moonTransform != null
            && MilkyWayTransform != null
            && targetMaterialSkybox != null;
    }

    private void ControlSunAndMoonTransform()
    {
        mainLight_Sun.transform.eulerAngles = new Vector3((time - 6f) * 15f, 180f, 0f);
        if (time >= 18f)
        {
            mainLight_Moon.transform.eulerAngles = new Vector3((time - 18f) * 15f, 180f, 0f);
        }
        else
        {
            mainLight_Moon.transform.eulerAngles = new Vector3(time * 15f + 90f, 180f, 0f);
        }

        sunTransform.eulerAngles = mainLight_Sun.transform.eulerAngles;
        moonTransform.eulerAngles = mainLight_Moon.transform.eulerAngles;

        Vector3 sunDirection = sunTransform.forward.normalized;
        Vector3 moonDirection = moonTransform.forward.normalized;

        SetCloudMaterialVector(sunDirectionPropertyName, sunDirection);
        SetCloudMaterialVector(moonDirectionPropertyName, moonDirection);
        targetMaterialSkybox.SetVector(sunDirectionPropertyName, sunDirection);
        targetMaterialSkybox.SetVector(moonDirectionPropertyName, moonDirection);
    }

    private void SetProperties()
    {
        targetMaterialSkybox.SetTexture(HorizonGradientTex, _currentSkyTimeData.horizonColorGradientTex);
        targetMaterialSkybox.SetTexture(SkyGradientTex, _currentSkyTimeData.skyColorGradientTex);
    }

    private void SetCloudMaterialVector(string propertyName, Vector3 value)
    {
        if (targetMaterialCloudA != null)
        {
            targetMaterialCloudA.SetVector(propertyName, value);
        }

        if (targetMaterialCloudB != null)
        {
            targetMaterialCloudB.SetVector(propertyName, value);
        }
    }
}
