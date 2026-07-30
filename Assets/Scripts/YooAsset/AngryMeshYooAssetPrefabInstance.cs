using System.Collections;
using UnityEngine;
using YooAsset;

[DisallowMultipleComponent]
public sealed class AngryMeshYooAssetPrefabInstance : MonoBehaviour
{
    private const string DefaultPackageName = "DefaultPackage";

    [SerializeField] private string _bundleName = string.Empty;
    [SerializeField] private string _assetPath = string.Empty;
    [SerializeField] private string _packageName = DefaultPackageName;
    [SerializeField] private bool _loadOnEnable = true;
    [SerializeField] private bool _releaseOnDisable = true;
    [SerializeField] private bool _destroyInstanceOnDisable = true;
    [SerializeField] private bool _instantiateAsChild = true;
    [SerializeField] private bool _logLifecycle;

    private AssetHandle _loadHandle;
    private Coroutine _loadRoutine;
    private GameObject _instance;

    public string Address => _assetPath;
    public string AssetPath => _assetPath;
    public string PackageName => string.IsNullOrWhiteSpace(_packageName) ? DefaultPackageName : _packageName;
    public string LegacyBundleName => _bundleName;
    public GameObject Instance => _instance;
    public bool IsLoaded => _instance != null;

    public void Configure(string address)
    {
        Configure(string.Empty, address, DefaultPackageName);
    }

    public void Configure(string legacyBundleName, string address)
    {
        Configure(legacyBundleName, address, DefaultPackageName);
    }

    public void Configure(string legacyBundleName, string address, string packageName)
    {
        _bundleName = legacyBundleName ?? string.Empty;
        _assetPath = address ?? string.Empty;
        _packageName = string.IsNullOrWhiteSpace(packageName) ? DefaultPackageName : packageName;
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
        if (_instance != null || _loadHandle != null || _loadRoutine != null)
            return;

        if (string.IsNullOrWhiteSpace(_assetPath))
        {
            Debug.LogError("ANGRY MESH YooAsset loader has no asset path.", this);
            return;
        }

        _loadRoutine = StartCoroutine(LoadRoutine());
    }

    public void Release()
    {
        if (_loadRoutine != null)
        {
            StopCoroutine(_loadRoutine);
            _loadRoutine = null;
        }

        if (_loadHandle != null)
        {
            if (_loadHandle.IsValid)
            {
                _loadHandle.Completed -= OnPrefabLoaded;
                _loadHandle.Release();
            }

            _loadHandle = null;
        }

        if (_instance != null && _destroyInstanceOnDisable)
        {
            Destroy(_instance);
            _instance = null;
        }
    }

    private IEnumerator LoadRoutine()
    {
        YooAssetPackageBootstrap.PackageInitializationState packageState =
            YooAssetPackageBootstrap.GetOrCreatePackageState(PackageName);
        yield return YooAssetPackageBootstrap.EnsurePackageReady(packageState);

        if (!packageState.IsReady)
        {
            Debug.LogError(
                "Failed to initialize YooAsset package '" + packageState.Package.PackageName + "'. " + packageState.Error,
                this);
            _loadRoutine = null;
            yield break;
        }

        AssetHandle handle;
        try
        {
            handle = packageState.Package.LoadAssetAsync<GameObject>(_assetPath);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("Failed to start YooAsset prefab load '" + _assetPath + "'. " + exception.Message, this);
            _loadRoutine = null;
            yield break;
        }

        _loadHandle = handle;
        _loadHandle.Completed += OnPrefabLoaded;
        _loadRoutine = null;
    }

    private void OnPrefabLoaded(AssetHandle handle)
    {
        if (_loadHandle == null || !ReferenceEquals(_loadHandle, handle) || !handle.IsValid)
            return;

        if (handle.Status != EOperationStatus.Succeeded)
        {
            Debug.LogError("Failed to load ANGRY MESH YooAsset prefab '" + _assetPath + "'. " + handle.Error, this);
            handle.Completed -= OnPrefabLoaded;
            handle.Release();
            _loadHandle = null;
            return;
        }

        GameObject prefab = handle.GetAssetObject<GameObject>();
        if (prefab == null)
        {
            Debug.LogError("YooAsset location did not resolve to a GameObject prefab: " + _assetPath, this);
            handle.Completed -= OnPrefabLoaded;
            handle.Release();
            _loadHandle = null;
            return;
        }

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
            Debug.Log("Loaded ANGRY MESH YooAsset prefab: " + _assetPath, this);
    }

}
