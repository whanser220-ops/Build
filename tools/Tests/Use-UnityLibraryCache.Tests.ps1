param()

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$subjectPath = Join-Path $repoRoot "tools\Use-UnityLibraryCache.ps1"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("UseUnityLibraryCacheTests-" + [guid]::NewGuid().ToString("N"))
$projectPath = Join-Path $testRoot "TestProject"
$cacheRoot = Join-Path $testRoot "Cache"
$projectLibraryPath = Join-Path $projectPath "Library"

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $projectPath "ProjectSettings") | Out-Null
    Set-Content -LiteralPath (Join-Path $projectPath "ProjectSettings\ProjectVersion.txt") `
        -Value "m_EditorVersion: 6000.0.46f1" `
        -Encoding UTF8

    Push-Location $projectPath
    try {
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -File $subjectPath `
            -ProjectPath $projectPath `
            -CacheRoot $cacheRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Initial cache attachment failed with exit code $LASTEXITCODE."
        }

        $cacheLibraryPath = Join-Path $cacheRoot "TestProject\Library"
        $sentinelPath = Join-Path $cacheLibraryPath "sentinel.txt"
        Set-Content -LiteralPath $sentinelPath -Value "preserve me" -Encoding UTF8

        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -File $subjectPath `
            -ProjectPath $projectPath `
            -CacheRoot $cacheRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Replacing the existing Library junction failed with exit code $LASTEXITCODE."
        }

        $libraryItem = Get-Item -LiteralPath $projectLibraryPath -Force
        if (($libraryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
            throw "Project Library is not a junction after cache reattachment."
        }
        if (-not (Test-Path -LiteralPath $sentinelPath)) {
            throw "Cache target content was removed while replacing the junction."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $projectLibraryPath) {
        $libraryItem = Get-Item -LiteralPath $projectLibraryPath -Force
        if (($libraryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            [System.IO.Directory]::Delete($projectLibraryPath)
        }
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host "Use-UnityLibraryCache junction reattachment test passed."
