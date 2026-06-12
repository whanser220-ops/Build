using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[DisallowMultipleComponent]
public sealed class AngryMeshAddressablePrefabInstance : MonoBehaviour
{
    [SerializeField] private string _bundleName = string.Empty;
    [SerializeField] private string _assetPath = string.Empty;
    [SerializeField] private bool _loadOnEnable = true;
    [SerializeField] private bool _releaseOnDisable = true;
    [SerializeField] private bool _destroyInstanceOnDisable = true;
    [SerializeField] private bool _instantiateAsChild = true;
    [SerializeField] private bool _logLifecycle;

    private AsyncOperationHandle<GameObject> _loadHandle;
    private bool _hasLoadHandle;
    private GameObject _instance;

    public string Address => _assetPath;
    public string AssetPath => _assetPath;
    public string LegacyBundleName => _bundleName;
    public GameObject Instance => _instance;
    public bool IsLoaded => _instance != null;

    public void Configure(string address)
    {
        _bundleName = string.Empty;
        _assetPath = address ?? string.Empty;
    }

    public void Configure(string legacyBundleName, string address)
    {
        _bundleName = legacyBundleName ?? string.Empty;
        _assetPath = address ?? string.Empty;
    }

    private void OnEnable()
    {
        if (_loadOnEnable)
            Load();
    }

    private void OnDisable()
    {
        if (_releaseOnDisable)
            Release();
    }

    private void OnDestroy()
    {
        Release();
    }

    public void Load()
    {
        if (_instance != null || _hasLoadHandle)
            return;

        if (string.IsNullOrWhiteSpace(_assetPath))
        {
            Debug.LogError("ANGRY MESH Addressables loader has no address.", this);
            return;
        }

        _loadHandle = Addressables.LoadAssetAsync<GameObject>(_assetPath);
        _hasLoadHandle = true;
        _loadHandle.Completed += OnPrefabLoaded;
    }

    public void Release()
    {
        if (_hasLoadHandle)
        {
            _loadHandle.Completed -= OnPrefabLoaded;
            Addressables.Release(_loadHandle);
            _hasLoadHandle = false;
        }

        if (_instance != null && _destroyInstanceOnDisable)
        {
            Destroy(_instance);
            _instance = null;
        }
    }

    private void OnPrefabLoaded(AsyncOperationHandle<GameObject> operation)
    {
        if (!_hasLoadHandle || !operation.IsValid())
            return;

        if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
        {
            Debug.LogError("Failed to load ANGRY MESH addressable prefab '" + _assetPath + "'.", this);
            Addressables.Release(operation);
            _hasLoadHandle = false;
            return;
        }

        GameObject prefab = operation.Result;
        if (_instantiateAsChild)
        {
            _instance = Instantiate(prefab, transform);
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;
            _instance.transform.localScale = Vector3.one;
        }
        else
        {
            _instance = Instantiate(prefab, transform.position, transform.rotation, transform.parent);
            _instance.transform.localScale = transform.localScale;
        }

        _instance.name = prefab.name;

        if (_logLifecycle)
            Debug.Log("Loaded ANGRY MESH addressable prefab: " + _assetPath, this);
    }
}
