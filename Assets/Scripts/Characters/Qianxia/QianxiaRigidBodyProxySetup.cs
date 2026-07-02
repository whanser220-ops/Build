using UnityEngine;

[DisallowMultipleComponent]
public class QianxiaRigidBodyProxySetup : MonoBehaviour
{
    [Header("Proxy")]
    [SerializeField] private string _proxyName = "BottleInteractionProxy_Body";
    [SerializeField] private Vector3 _sizePadding = new Vector3(0.12f, 0.05f, 0.12f);
    [SerializeField] private float _centerHeightScale = 0.5f;
    [SerializeField] private bool _isTrigger = true;

    public void Apply()
    {
        BoxCollider proxyCollider = GetOrCreateProxyCollider();
        Bounds bounds = CalculateCharacterBounds();

        Vector3 size = bounds.size + _sizePadding;
        size.x = Mathf.Max(0.18f, size.x);
        size.y = Mathf.Max(0.8f, size.y);
        size.z = Mathf.Max(0.18f, size.z);

        Vector3 centerWorld = new Vector3(bounds.center.x, bounds.min.y + (bounds.size.y * Mathf.Clamp01(_centerHeightScale)), bounds.center.z);

        proxyCollider.transform.SetParent(transform, true);
        proxyCollider.transform.localRotation = Quaternion.identity;
        proxyCollider.transform.localScale = Vector3.one;
        proxyCollider.center = transform.InverseTransformPoint(centerWorld);
        proxyCollider.size = size;
        proxyCollider.isTrigger = _isTrigger;
        proxyCollider.enabled = true;
    }

    private void Reset()
    {
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private BoxCollider GetOrCreateProxyCollider()
    {
        Transform proxyTransform = transform.Find(_proxyName);
        if (proxyTransform == null)
        {
            GameObject proxyObject = new GameObject(_proxyName);
            proxyObject.transform.SetParent(transform, false);
            proxyTransform = proxyObject.transform;
        }

        BoxCollider collider = proxyTransform.GetComponent<BoxCollider>();
        if (collider == null)
            collider = proxyTransform.gameObject.AddComponent<BoxCollider>();

        return collider;
    }

    private Bounds CalculateCharacterBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        CharacterController characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            Vector3 worldCenter = transform.TransformPoint(characterController.center);
            Vector3 size = new Vector3(characterController.radius * 2.0f, characterController.height, characterController.radius * 2.0f);
            return new Bounds(worldCenter, size);
        }

        return new Bounds(transform.position + Vector3.up * 0.9f, new Vector3(0.5f, 1.8f, 0.4f));
    }
}
