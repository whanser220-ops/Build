param(
    [Parameter(Mandatory = $true)]
    [string] $Port,

    [Parameter(Mandatory = $true)]
    [string] $Client,

    [Parameter(Mandatory = $true)]
    [string] $Root,

    [Parameter(Mandatory = $true)]
    [string] $ViewFile,

    [string] $Changelist = '',

    [string] $LogPath = 'Logs\p4-sync.log',

    [string] $ManifestPath = '.workspace\build-manifest.json'
)

$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string] $Message)

    $timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $line = "[$timestamp] $Message"
    Write-Host $line
    Add-Content -LiteralPath $script:ResolvedLogPath -Value $line -Encoding UTF8
}

function Invoke-P4 {
    param([string[]] $Arguments)

    Write-Log ("p4 " + ($Arguments -join ' '))
    $output = & $script:P4Exe @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    foreach ($line in $output) {
        Write-Log ([string] $line)
    }

    if ($exitCode -ne 0) {
        throw "p4 command failed with exit code $exitCode"
    }

    return $output
}

$p4User = $env:P4_USERNAME
$p4Password = $env:P4_PASSWORD

if ([string]::IsNullOrWhiteSpace($p4User)) {
    throw 'P4_USERNAME environment variable is required.'
}

if ([string]::IsNullOrWhiteSpace($p4Password)) {
    throw 'P4_PASSWORD environment variable is required.'
}

$p4Command = Get-Command p4.exe -ErrorAction SilentlyContinue
if (-not $p4Command) {
    $p4Command = Get-Command p4 -ErrorAction SilentlyContinue
}

if ($p4Command) {
    $script:P4Exe = $p4Command.Source
}

if (-not $script:P4Exe) {
    throw 'p4 command line client was not found on PATH.'
}

$resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
$resolvedViewFile = (Resolve-Path -LiteralPath $ViewFile).Path
$resolvedLogDir = Split-Path -Parent (Join-Path $resolvedRoot $LogPath)
$resolvedManifestDir = Split-Path -Parent (Join-Path $resolvedRoot $ManifestPath)

New-Item -ItemType Directory -Force -Path $resolvedLogDir | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedManifestDir | Out-Null

$script:ResolvedLogPath = Join-Path $resolvedRoot $LogPath
Remove-Item -LiteralPath $script:ResolvedLogPath -Force -ErrorAction SilentlyContinue

$ticketFile = Join-Path $resolvedRoot '.workspace\p4tickets.txt'
Remove-Item -LiteralPath $ticketFile -Force -ErrorAction SilentlyContinue

$env:P4PORT = $Port
$env:P4USER = $p4User
$env:P4CLIENT = $Client
$env:P4TICKETS = $ticketFile

try {
    Write-Log "Using p4 client: $Client"
    Write-Log "Using p4 root: $resolvedRoot"
    Write-Log "Using p4 port: $Port"

    Write-Log 'Logging in to Perforce.'
    $loginOutput = $p4Password | & $script:P4Exe login 2>&1
    $loginExitCode = $LASTEXITCODE
    foreach ($line in $loginOutput) {
        Write-Log ([string] $line)
    }
    if ($loginExitCode -ne 0) {
        throw "p4 login failed with exit code $loginExitCode"
    }

    $viewLines = Get-Content -LiteralPath $resolvedViewFile |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') } |
        ForEach-Object { $_.Replace('${P4_CLIENT}', $Client) }

    if (-not $viewLines -or $viewLines.Count -eq 0) {
        throw "Perforce view file is empty: $resolvedViewFile"
    }

    $clientSpec = @(
        "Client: $Client",
        "Owner: $p4User",
        'Description:',
        "`tJenkins Unity asset sync workspace.",
        "Root: $resolvedRoot",
        'Options: noallwrite clobber nocompress unlocked nomodtime normdir',
        'SubmitOptions: submitunchanged',
        'LineEnd: local',
        'View:'
    )

    foreach ($viewLine in $viewLines) {
        $clientSpec += "`t$viewLine"
    }

    Write-Log 'Creating/updating Perforce client spec.'
    $clientSpecText = $clientSpec -join [Environment]::NewLine
    $clientOutput = $clientSpecText | & $script:P4Exe client -i 2>&1
    $clientExitCode = $LASTEXITCODE
    foreach ($line in $clientOutput) {
        Write-Log ([string] $line)
    }
    if ($clientExitCode -ne 0) {
        throw "p4 client -i failed with exit code $clientExitCode"
    }

    $pin = ''
    if (-not [string]::IsNullOrWhiteSpace($Changelist)) {
        $pin = "@$($Changelist.Trim())"
    }

    Invoke-P4 -Arguments @('sync', '--parallel=threads=8,min=100,minsize=1048576', "//$Client/...$pin") | Out-Null
    Invoke-P4 -Arguments @('clean', "//$Client/...") | Out-Null

    $syncedChange = $Changelist.Trim()
    if ([string]::IsNullOrWhiteSpace($syncedChange)) {
        $changeOutput = Invoke-P4 -Arguments @('changes', '-m1', "//$Client/...")
        $changeLine = $changeOutput | Select-Object -First 1
        if ($changeLine -match '^Change\s+(\d+)\s+') {
            $syncedChange = $Matches[1]
        }
    }

    $gitCommit = ''
    try {
        $gitCommit = (& git -C $resolvedRoot rev-parse HEAD 2>$null).Trim()
    } catch {
        $gitCommit = ''
    }

    $manifest = [ordered]@{
        build_number = $env:BUILD_NUMBER
        job_name = $env:JOB_NAME
        node_name = $env:NODE_NAME
        git_commit = $gitCommit
        p4 = [ordered]@{
            port = $Port
            user = $p4User
            client = $Client
            changelist = $syncedChange
            pinned_changelist = $Changelist.Trim()
            view = @($viewLines)
        }
        generated_at_utc = (Get-Date).ToUniversalTime().ToString('o')
    }

    $manifestPathResolved = Join-Path $resolvedRoot $ManifestPath
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPathResolved -Encoding UTF8
    Write-Log "Wrote build manifest: $manifestPathResolved"
} finally {
    Remove-Item -LiteralPath $ticketFile -Force -ErrorAction SilentlyContinue
    Remove-Item Env:P4_PASSWORD -ErrorAction SilentlyContinue
}
