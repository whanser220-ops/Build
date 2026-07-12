using System.Collections;
using System.Collections.Generic;
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

    private static readonly Dictionary<string, PackageInitializationState> PackageStates =
        new Dictionary<string, PackageInitializationState>();

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
        PackageInitializationState packageState = GetOrCreatePackageState(PackageName);
        yield return EnsurePackageInitialized(packageState);

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

    private static PackageInitializationState GetOrCreatePackageState(string packageName)
    {
        string resolvedPackageName = string.IsNullOrWhiteSpace(packageName) ? DefaultPackageName : packageName;

        if (!YooAssets.IsInitialized)
            YooAssets.Initialize();

        if (PackageStates.TryGetValue(resolvedPackageName, out PackageInitializationState state))
            return state;

        ResourcePackage package;
        if (!YooAssets.TryGetPackage(resolvedPackageName, out package))
            package = YooAssets.CreatePackage(resolvedPackageName);

        state = new PackageInitializationState(package);
        PackageStates.Add(resolvedPackageName, state);
        return state;
    }

    private static IEnumerator EnsurePackageInitialized(PackageInitializationState state)
    {
        if (state.IsReady || state.IsFailed)
            yield break;

        if (state.InitializeOperation == null)
        {
            OfflinePlayModeOptions options = new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
            };
            state.InitializeOperation = state.Package.InitializePackageAsync(options);
        }

        yield return state.InitializeOperation;
        if (state.InitializeOperation.Status != EOperationStatus.Succeeded)
        {
            state.Fail(state.InitializeOperation.Error);
            yield break;
        }

        if (state.VersionOperation == null)
            state.VersionOperation = state.Package.RequestPackageVersionAsync();

        yield return state.VersionOperation;
        if (state.VersionOperation.Status != EOperationStatus.Succeeded)
        {
            state.Fail(state.VersionOperation.Error);
            yield break;
        }

        if (state.ManifestOperation == null)
        {
            LoadPackageManifestOptions options = new LoadPackageManifestOptions(
                state.VersionOperation.PackageVersion,
                60);
            state.ManifestOperation = state.Package.LoadPackageManifestAsync(options);
        }

        yield return state.ManifestOperation;
        if (state.ManifestOperation.Status != EOperationStatus.Succeeded)
        {
            state.Fail(state.ManifestOperation.Error);
            yield break;
        }

        state.MarkReady();
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

    private sealed class PackageInitializationState
    {
        public readonly ResourcePackage Package;
        public InitializePackageOperation InitializeOperation;
        public RequestPackageVersionOperation VersionOperation;
        public LoadPackageManifestOperation ManifestOperation;
        public string Error = string.Empty;
        public bool IsReady;
        public bool IsFailed;

        public PackageInitializationState(ResourcePackage package)
        {
            Package = package;
        }

        public void MarkReady()
        {
            IsReady = true;
            IsFailed = false;
            Error = string.Empty;
        }

        public void Fail(string error)
        {
            IsFailed = true;
            IsReady = false;
            Error = error ?? string.Empty;
        }
    }
}
