param(
    [string]$ProjectPath = ".",
    [string]$LockFile = "svn-assets.lock.json",
    [string]$SvnExe = "",
    [string]$Username = "",
    [string]$Password = "",
    [switch]$NoAuthCache,
    [switch]$AllowExistingNonWorkingCopy
)

$ErrorActionPreference = "Stop"

function Resolve-SvnExe {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "svn.exe was not found: $ExplicitPath"
        }

        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $command = Get-Command svn.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "C:\Program Files\VisualSVN Server\bin\svn.exe",
        "C:\Program Files\TortoiseSVN\bin\svn.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "svn.exe was not found. Install VisualSVN Server or TortoiseSVN, or pass -SvnExe."
}

function ConvertTo-SvnUrl {
    param(
        [string]$RepositoryUrl,
        [string]$RepositoryPath
    )

    $baseUrl = $RepositoryUrl.TrimEnd("/")
    $segments = $RepositoryPath -split "[/\\]" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($segment in $segments) {
        $baseUrl += "/" + [uri]::EscapeDataString($segment)
    }

    return $baseUrl
}

function Invoke-Svn {
    param([string[]]$Arguments)

    Write-Host "svn $(Format-SvnArguments -Arguments $Arguments)"
    & $script:SvnExePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "svn failed with exit code $LASTEXITCODE"
    }
}

function Format-SvnArguments {
    param([string[]]$Arguments)

    $sanitized = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $Arguments.Length; $i++) {
        $argument = $Arguments[$i]
        $sanitized.Add($argument)
        if ($argument -eq "--password" -and $i + 1 -lt $Arguments.Length) {
            $i++
            $sanitized.Add("<redacted>")
        }
    }

    return ($sanitized -join " ")
}

function Get-SvnOutput {
    param([string[]]$Arguments)

    $output = & $script:SvnExePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "svn failed with exit code $LASTEXITCODE"
    }

    return $output
}

function Test-SvnWorkingCopy {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    & $script:SvnExePath info $Path *> $null
    return $LASTEXITCODE -eq 0
}

function Assert-CleanWorkingCopy {
    param([string]$Path)

    $status = @(Get-SvnOutput @("status", "--quiet", $Path))
    if ($status.Count -gt 0) {
        Write-Host "SVN local changes in ${Path}:"
        $status | ForEach-Object { Write-Host $_ }
        throw "Refusing to update dirty SVN working copy: $Path"
    }
}

$resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$resolvedLockFile = Join-Path $resolvedProjectPath $LockFile
if (-not (Test-Path -LiteralPath $resolvedLockFile -PathType Leaf)) {
    throw "SVN assets lock file was not found: $resolvedLockFile"
}

$lock = Get-Content -LiteralPath $resolvedLockFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $lock.repositoryUrl -or $null -eq $lock.revision -or $null -eq $lock.roots) {
    throw "Invalid SVN assets lock file: $resolvedLockFile"
}

$script:SvnExePath = Resolve-SvnExe -ExplicitPath $SvnExe
$svnCommonArgs = @("--non-interactive", "--trust-server-cert-failures=unknown-ca,cn-mismatch,expired,not-yet-valid,other")

if ([string]::IsNullOrWhiteSpace($Username) -and -not [string]::IsNullOrWhiteSpace($env:SVN_USERNAME)) {
    $Username = $env:SVN_USERNAME
}

if ([string]::IsNullOrWhiteSpace($Password) -and -not [string]::IsNullOrWhiteSpace($env:SVN_PASSWORD)) {
    $Password = $env:SVN_PASSWORD
}

if (-not [string]::IsNullOrWhiteSpace($Username)) {
    $svnCommonArgs += @("--username", $Username)
}

if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $svnCommonArgs += @("--password", $Password)
}

if ($NoAuthCache) {
    $svnCommonArgs += "--no-auth-cache"
}

$revision = [string]$lock.revision
foreach ($root in $lock.roots) {
    $localRelativePath = [string]$root.localPath
    $repositoryPath = [string]$root.repositoryPath

    if ([string]::IsNullOrWhiteSpace($localRelativePath) -or [string]::IsNullOrWhiteSpace($repositoryPath)) {
        throw "Each SVN root requires repositoryPath and localPath."
    }

    $localPath = Join-Path $resolvedProjectPath $localRelativePath
    $remoteUrl = ConvertTo-SvnUrl -RepositoryUrl ([string]$lock.repositoryUrl) -RepositoryPath $repositoryPath

    if (Test-SvnWorkingCopy -Path $localPath) {
        Assert-CleanWorkingCopy -Path $localPath
        Invoke-Svn (@("update", $localPath, "--revision", $revision) + $svnCommonArgs)
    }
    elseif (Test-Path -LiteralPath $localPath) {
        if (-not $AllowExistingNonWorkingCopy) {
            throw "Path exists but is not an SVN working copy: $localPath. Pass -AllowExistingNonWorkingCopy to checkout with --force."
        }

        Invoke-Svn (@("checkout", $remoteUrl, $localPath, "--revision", $revision, "--force") + $svnCommonArgs)
    }
    else {
        $parent = Split-Path -Parent $localPath
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
        }

        Invoke-Svn (@("checkout", $remoteUrl, $localPath, "--revision", $revision) + $svnCommonArgs)
    }

    $actualRevision = (Get-SvnOutput @("info", $localPath, "--show-item", "revision")).Trim()
    $actualUrl = (Get-SvnOutput @("info", $localPath, "--show-item", "url")).Trim()
    if ($actualRevision -ne $revision) {
        throw "SVN revision mismatch for ${localRelativePath}: expected r$revision, got r$actualRevision"
    }

    if ($actualUrl -ne $remoteUrl) {
        throw "SVN URL mismatch for ${localRelativePath}: expected $remoteUrl, got $actualUrl"
    }

    Write-Host "Synced ${localRelativePath} to r$actualRevision"
}
