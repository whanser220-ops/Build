using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

[DisallowMultipleComponent]
public sealed class YooAssetBootSceneLoader : MonoBehaviour
{
    private const string SceneArgument = "--meadow-scene";
    private const string SeasonArgument = "--meadow-season";
    private const string SummerScenePath = "Assets/Game/Worlds/Meadow/Runtime/Scenes/Scene_MeadowEnvironment_01_Summer.unity";
    private const string AutumnScenePath = "Assets/Game/Worlds/Meadow/Runtime/Scenes/Scene_MeadowEnvironment_02_Autumn.unity";
    private const string WinterScenePath = "Assets/Game/Worlds/Meadow/Runtime/Scenes/Scene_MeadowEnvironment_03_Winter.unity";

    [SerializeField] private string _packageName = YooAssetPackageBootstrap.DefaultPackageName;
    [SerializeField] private string _initialScene = "summer";
    [SerializeField] private bool _loadOnStart = true;
    [SerializeField] private bool _dontDestroyOnLoad = true;
    [SerializeField] private bool _logLifecycle = true;

    private SceneHandle _loadedScene;
    private bool _isLoading;

    private IEnumerator Start()
    {
        if (!_loadOnStart)
            yield break;

        yield return LoadInitialSceneRoutine();
    }

    public void LoadInitialScene()
    {
        if (!_isLoading)
            StartCoroutine(LoadInitialSceneRoutine());
    }

    private IEnumerator LoadInitialSceneRoutine()
    {
        if (_isLoading)
            yield break;

        _isLoading = true;

        if (_dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        string selectedScene = ResolveSelectedScenePath();
        if (string.IsNullOrWhiteSpace(selectedScene))
        {
            Debug.LogError("No YooAsset boot scene could be resolved.");
            _isLoading = false;
            yield break;
        }

        if (_logLifecycle)
            Debug.Log("Boot loading YooAsset scene: " + selectedScene);

        YooAssetPackageBootstrap.PackageInitializationState packageState =
            YooAssetPackageBootstrap.GetOrCreatePackageState(_packageName);
        yield return YooAssetPackageBootstrap.EnsurePackageReady(packageState);

        if (!packageState.IsReady)
        {
            Debug.LogError("Failed to initialize YooAsset package '" + packageState.Package.PackageName + "'. " + packageState.Error);
            _isLoading = false;
            yield break;
        }

        _loadedScene = packageState.Package.LoadSceneAsync(selectedScene, LoadSceneMode.Single);
        yield return _loadedScene;

        if (_loadedScene.Status != EOperationStatus.Succeeded)
        {
            Debug.LogError("Failed to load YooAsset scene '" + selectedScene + "'. " + _loadedScene.Error);
            _loadedScene = null;
            _isLoading = false;
            yield break;
        }

        _loadedScene.ActivateScene();
        _isLoading = false;
    }

    private string ResolveSelectedScenePath()
    {
        string requestedScene = GetCommandLineValue(SceneArgument);
        if (string.IsNullOrWhiteSpace(requestedScene))
            requestedScene = GetCommandLineValue(SeasonArgument);

        if (string.IsNullOrWhiteSpace(requestedScene))
            requestedScene = _initialScene;

        string scenePath = ResolveScenePath(requestedScene);
        if (!string.IsNullOrWhiteSpace(scenePath))
            return scenePath;

        Debug.LogWarning("Unknown Meadow scene selection '" + requestedScene + "'. Falling back to '" + _initialScene + "'.");
        return ResolveScenePath(_initialScene);
    }

    private static string ResolveScenePath(string keyOrPath)
    {
        string key = (keyOrPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (IsKnownScenePath(key))
            return key;

        key = key.ToLowerInvariant();
        key = key.Replace("scene_meadowenvironment_", string.Empty);
        key = key.Replace(".unity", string.Empty);

        switch (key)
        {
            case "summer":
            case "01":
            case "01_summer":
                return SummerScenePath;
            case "autumn":
            case "02":
            case "02_autumn":
                return AutumnScenePath;
            case "winter":
            case "03":
            case "03_winter":
                return WinterScenePath;
            default:
                return string.Empty;
        }
    }

    private static bool IsKnownScenePath(string path)
    {
        return string.Equals(path, SummerScenePath, System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, AutumnScenePath, System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, WinterScenePath, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCommandLineValue(string argumentName)
    {
        string[] arguments = System.Environment.GetCommandLineArgs();
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (string.Equals(argument, argumentName, System.StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < arguments.Length)
                    return arguments[index + 1];

                return string.Empty;
            }

            string prefix = argumentName + "=";
            if (argument.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return argument.Substring(prefix.Length);
        }

        return string.Empty;
    }
}
