param(
    [string]$ProjectPath = ".",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$UnityArgs
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

function Resolve-UnityEditorPath {
    param(
        [string]$UnityVersion
    )

    $candidatePaths = New-Object System.Collections.Generic.List[string]

    if ($env:UNITY_EXE) {
        $candidatePaths.Add($env:UNITY_EXE)
    }

    if ($env:UNITY_EDITOR_PATH) {
        $candidatePaths.Add($env:UNITY_EDITOR_PATH)
    }

    $candidatePaths.Add("C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe")
    $candidatePaths.Add("C:\Program Files\Unity\Editor\Unity.exe")
    $candidatePaths.Add("C:\Program Files (x86)\Unity\Editor\Unity.exe")

    foreach ($candidatePath in $candidatePaths) {
        if ([string]::IsNullOrWhiteSpace($candidatePath)) {
            continue
        }

        if (Test-Path $candidatePath) {
            return (Resolve-Path $candidatePath).Path
        }
    }

    $hubEditorsRoot = "C:\Program Files\Unity\Hub\Editor"
    if (Test-Path $hubEditorsRoot) {
        $fallbackPath = Get-ChildItem -Path $hubEditorsRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "Editor\Unity.exe" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1

        if ($fallbackPath) {
            Write-Warning "Unity.exe for project version $UnityVersion was not found. Falling back to: $fallbackPath"
            return (Resolve-Path $fallbackPath).Path
        }
    }

    throw "Unity.exe not found. Install Unity $UnityVersion via Unity Hub, or set UNITY_EXE / UNITY_EDITOR_PATH."
}

$resolvedProjectPath = (Resolve-Path $ProjectPath).Path
$unityVersion = Get-ProjectUnityVersion -ResolvedProjectPath $resolvedProjectPath
$unityEditorPath = Resolve-UnityEditorPath -UnityVersion $unityVersion

$forwardArgs = New-Object System.Collections.Generic.List[string]
if ($UnityArgs) {
    foreach ($arg in $UnityArgs) {
        $forwardArgs.Add($arg)
    }
}

if (-not ($forwardArgs -contains "-projectPath")) {
    $forwardArgs.Insert(0, $resolvedProjectPath)
    $forwardArgs.Insert(0, "-projectPath")
}

Write-Host "Unity version: $unityVersion"
Write-Host "Unity path: $unityEditorPath"
Write-Host "Project path: $resolvedProjectPath"

& $unityEditorPath @forwardArgs
exit $LASTEXITCODE
