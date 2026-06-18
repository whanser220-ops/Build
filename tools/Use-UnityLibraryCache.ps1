param(
    [string]$ProjectPath = ".",
    [string]$CacheRoot = "",
    [string]$SchemaVersion = "single-library-v1"
)

$ErrorActionPreference = "Stop"

function Get-ProjectUnityVersion {
    param(
        [string]$ResolvedProjectPath
    )

    $projectVersionFile = Join-Path $ResolvedProjectPath "ProjectSettings\ProjectVersion.txt"
    if (-not (Test-Path $projectVersionFile)) {
        throw "ProjectVersion.txt not found: $projectVersionFile"
    }

    $versionLine = Select-String -Path $projectVersionFile -Pattern "^m_EditorVersion:\s*(.+)$" | Select-Object -First 1
    if ($null -eq $versionLine) {
        throw "Failed to parse Unity version from: $projectVersionFile"
    }

    return $versionLine.Matches[0].Groups[1].Value.Trim()
}

function Get-RepoId {
    $candidate = $env:GITHUB_REPOSITORY
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = Split-Path -Leaf (Resolve-Path ".").Path
    }

    return ($candidate -replace '[\\/:*?"<>|]', "_")
}

function Test-IsPathInside {
    param(
        [string]$ChildPath,
        [string]$ParentPath
    )

    $child = [System.IO.Path]::GetFullPath($ChildPath).TrimEnd('\', '/')
    $parent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\', '/')
    return $child.StartsWith($parent + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Read-CacheInfo {
    param(
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        return $null
    }

    try {
        return Get-Content -Raw $Path | ConvertFrom-Json
    }
    catch {
        Write-Warning "Failed to read Unity Library cache info; cache will be reset: $Path"
        return $null
    }
}

$resolvedProjectPath = (Resolve-Path $ProjectPath).Path
$unityVersion = Get-ProjectUnityVersion -ResolvedProjectPath $resolvedProjectPath
$repoId = Get-RepoId

if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is not set. Pass -CacheRoot explicitly."
    }

    $CacheRoot = Join-Path $env:LOCALAPPDATA "Unity6Ci\LibraryCache"
}

$resolvedCacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
$repoCacheRoot = Join-Path $resolvedCacheRoot $repoId
$libraryCachePath = Join-Path $repoCacheRoot "Library"
$cacheInfoPath = Join-Path $repoCacheRoot "cache-info.json"
$workspaceLibraryPath = Join-Path $resolvedProjectPath "Library"

New-Item -ItemType Directory -Force -Path $repoCacheRoot | Out-Null

$cacheInfo = Read-CacheInfo -Path $cacheInfoPath
$hasExistingLibraryCache = Test-Path $libraryCachePath
$isCompatible = $false

if ($null -ne $cacheInfo) {
    $isCompatible =
        ([string]$cacheInfo.schemaVersion -eq $SchemaVersion) -and
        ([string]$cacheInfo.unityVersion -eq $unityVersion) -and
        ([string]$cacheInfo.repoId -eq $repoId)
}

if ($hasExistingLibraryCache -and -not $isCompatible) {
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $stalePath = Join-Path $repoCacheRoot "Library.stale.$timestamp"
    Write-Host "Unity Library cache is incompatible; moving old cache to: $stalePath"
    Move-Item -LiteralPath $libraryCachePath -Destination $stalePath
}

if (-not (Test-Path $libraryCachePath)) {
    New-Item -ItemType Directory -Force -Path $libraryCachePath | Out-Null
}

$cacheInfoDocument = [ordered]@{
    schemaVersion = $SchemaVersion
    unityVersion = $unityVersion
    repoId = $repoId
    updatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}
$cacheInfoDocument | ConvertTo-Json | Set-Content -Encoding UTF8 -Path $cacheInfoPath

$workspaceLibraryFullPath = [System.IO.Path]::GetFullPath($workspaceLibraryPath)
if (-not (Test-IsPathInside -ChildPath $workspaceLibraryFullPath -ParentPath $resolvedProjectPath)) {
    throw "Refusing to modify Library outside project path: $workspaceLibraryFullPath"
}

if (Test-Path $workspaceLibraryPath) {
    $libraryItem = Get-Item -LiteralPath $workspaceLibraryPath -Force
    if (($libraryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        [System.IO.Directory]::Delete($workspaceLibraryFullPath)
    }
    else {
        Remove-Item -LiteralPath $workspaceLibraryPath -Recurse -Force
    }
}

New-Item -ItemType Junction -Path $workspaceLibraryPath -Target $libraryCachePath | Out-Null
Write-Host "Unity Library cache attached:"
Write-Host "  Project Library: $workspaceLibraryPath"
Write-Host "  Cache Library:   $libraryCachePath"
Write-Host "  Unity version:   $unityVersion"
Write-Host "  Schema version:  $SchemaVersion"
