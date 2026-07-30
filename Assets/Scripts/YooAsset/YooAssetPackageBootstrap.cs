using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

public static class YooAssetPackageBootstrap
{
    public const string DefaultPackageName = "DefaultPackage";

    private static readonly Dictionary<string, PackageInitializationState> PackageStates =
        new Dictionary<string, PackageInitializationState>();

    public static PackageInitializationState GetOrCreatePackageState(string packageName)
    {
        string resolvedPackageName = ResolvePackageName(packageName);

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

    public static IEnumerator EnsurePackageReady(PackageInitializationState state)
    {
        if (state == null)
            yield break;

        if (state.IsReady || state.IsFailed)
            yield break;

        if (state.Package.PackageValid)
        {
            state.MarkReady();
            yield break;
        }

        if (state.Package.InitializeStatus == EOperationStatus.None)
        {
            OfflinePlayModeOptions options = new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
            };
            state.InitializeOperation = state.Package.InitializePackageAsync(options);
        }

        if (state.InitializeOperation != null)
            yield return state.InitializeOperation;
        else
            yield return WaitForExternalPackageInitialization(state);

        if (state.Package.InitializeStatus != EOperationStatus.Succeeded)
        {
            state.Fail(GetPackageInitializationError(state));
            yield break;
        }

        if (state.Package.PackageValid)
        {
            state.MarkReady();
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

    private static IEnumerator WaitForExternalPackageInitialization(PackageInitializationState state)
    {
        while (state.Package.InitializeStatus == EOperationStatus.Processing)
            yield return null;
    }

    private static string GetPackageInitializationError(PackageInitializationState state)
    {
        if (state.InitializeOperation != null && !string.IsNullOrWhiteSpace(state.InitializeOperation.Error))
            return state.InitializeOperation.Error;

        return "Package initialize status is " + state.Package.InitializeStatus;
    }

    private static string ResolvePackageName(string packageName)
    {
        return string.IsNullOrWhiteSpace(packageName) ? DefaultPackageName : packageName.Trim();
    }

    public sealed class PackageInitializationState
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
