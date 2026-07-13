param(
    [string] $Port = 'ssl:1.117.232.198:1666',

    [Parameter(Mandatory = $true)]
    [string] $Client,

    [Parameter(Mandatory = $true)]
    [string] $Root,

    [string] $DepotRoot = '//depot',

    [switch] $Apply,

    [switch] $Submit,

    [switch] $Force,

    [switch] $SkipSync,

    [string] $LogPath = 'Logs\p4-layout-migration.log'
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
    param(
        [string[]] $Arguments,
        [switch] $AllowFailure
    )

    Write-Log ("p4 " + ($Arguments -join ' '))
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & $script:P4Exe @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Log ([string] $line)
    }

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "p4 command failed with exit code $exitCode"
    }

    return @{
        ExitCode = $exitCode
        Output = @($output | ForEach-Object { [string] $_ })
    }
}

function Join-DepotPath {
    param([string] $RelativePath)

    return ($DepotRoot.TrimEnd('/') + '/' + $RelativePath.TrimStart('/')).Replace('\', '/')
}

function Convert-ToClientViewLine {
    param([string] $DepotPath)

    $normalizedDepotRoot = $DepotRoot.TrimEnd('/')
    if (-not $DepotPath.StartsWith($normalizedDepotRoot + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Depot path is outside DepotRoot: $DepotPath"
    }

    $relativePath = $DepotPath.Substring($normalizedDepotRoot.Length + 1)
    $clientPath = "//$Client/$relativePath"
    return '"' + $DepotPath + '" "' + $clientPath + '"'
}

function Test-P4HasFiles {
    param([string] $DepotPath)

    $result = Invoke-P4 -Arguments @('files', $DepotPath) -AllowFailure
    return ($result.Output | Where-Object { $_ -match '^//' }).Count -gt 0
}

function New-P4Change {
    param([string] $Description)

    $changeSpec = @(
        'Change: new',
        "Client: $Client",
        "User: $script:P4User",
        'Status: new',
        'Description:',
        "`t$Description"
    ) -join [Environment]::NewLine

    Write-Log 'Creating migration changelist.'
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = $changeSpec | & $script:P4Exe change -i 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Log ([string] $line)
    }

    if ($exitCode -ne 0) {
        throw "p4 change -i failed with exit code $exitCode"
    }

    $changeLine = $output | Select-Object -First 1
    if ($changeLine -notmatch 'Change\s+(\d+)\s+created') {
        throw "Could not parse changelist number from: $changeLine"
    }

    return $Matches[1]
}

$resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
$resolvedLogDir = Split-Path -Parent (Join-Path $resolvedRoot $LogPath)
New-Item -ItemType Directory -Force -Path $resolvedLogDir | Out-Null
$script:ResolvedLogPath = Join-Path $resolvedRoot $LogPath
Remove-Item -LiteralPath $script:ResolvedLogPath -Force -ErrorAction SilentlyContinue

$script:P4User = $env:P4_USERNAME
$p4Password = $env:P4_PASSWORD
if ([string]::IsNullOrWhiteSpace($script:P4User)) {
    throw 'P4_USERNAME environment variable is required.'
}

if ([string]::IsNullOrWhiteSpace($p4Password)) {
    throw 'P4_PASSWORD environment variable is required.'
}

$p4Command = Get-Command p4.exe -ErrorAction SilentlyContinue
if (-not $p4Command) {
    $p4Command = Get-Command p4 -ErrorAction SilentlyContinue
}

if (-not $p4Command) {
    throw 'p4 command line client was not found on PATH.'
}

$script:P4Exe = $p4Command.Source
$preview = -not $Apply -and -not $Submit
if ($Submit) {
    $Apply = $true
}

$moveMappings = @(
    @{ From = Join-DepotPath 'Assets/GameResources/Characters/Qianxia/...'; To = Join-DepotPath 'Assets/Game/Characters/Qianxia/Art/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Characters/Qianxia/...'; To = Join-DepotPath 'Assets/Game/Characters/Qianxia/Runtime/...' },
    @{ From = Join-DepotPath 'Assets/GameResources/Stylized Pack - Meadow Environment/...'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Art/...' },
    @{ From = Join-DepotPath 'Assets/GameResources/Stylized Pack - Common/...'; To = Join-DepotPath 'Assets/Game/Shared/StylizedPackCommon/Art/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Common/...'; To = Join-DepotPath 'Assets/Game/Shared/StylizedPackCommon/Runtime/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Configs/Input/...'; To = Join-DepotPath 'Assets/Game/Core/Input/Runtime/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Configs/Post Processing/...'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Configs/Post Processing/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Worlds/Meadow/Shared/...'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Shared/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Worlds/Meadow/Seasons/...'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Seasons/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Worlds/Meadow/Scenes/...'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Scenes/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/UIModules/UILogin/...'; To = Join-DepotPath 'Assets/Game/UI/UILogin/Runtime/...' },
    @{ From = Join-DepotPath 'Assets/GameAssets/UIModules/UIMain/...'; To = Join-DepotPath 'Assets/Game/UI/UIMain/Runtime/...' }
)

$moveFileMappings = @(
    @{ From = Join-DepotPath 'Assets/GameAssets/Configs/Post Processing.meta'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Configs/Post Processing.meta' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Worlds/Meadow/Shared.meta'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Shared.meta' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Worlds/Meadow/Seasons.meta'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Seasons.meta' },
    @{ From = Join-DepotPath 'Assets/GameAssets/Worlds/Meadow/Scenes.meta'; To = Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Scenes.meta' }
)

$deletePaths = @(
    Join-DepotPath 'Assets/GameAssets/Worlds/Meadow/Chunks/...',
    Join-DepotPath 'Assets/GameAssets/Worlds/Meadow/Chunks.meta',
    Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Chunks/...',
    Join-DepotPath 'Assets/Game/Worlds/Meadow/Runtime/Chunks.meta'
)

$allViewPaths = New-Object System.Collections.Generic.List[string]
foreach ($mapping in $moveMappings + $moveFileMappings) {
    $allViewPaths.Add($mapping.From)
    $allViewPaths.Add($mapping.To)
}
foreach ($deletePath in $deletePaths) {
    $allViewPaths.Add($deletePath)
}

$viewLines = $allViewPaths |
    Sort-Object -Unique |
    ForEach-Object { Convert-ToClientViewLine $_ }

$ticketFile = Join-Path $resolvedRoot '.workspace\p4-layout-migration-tickets.txt'
Remove-Item -LiteralPath $ticketFile -Force -ErrorAction SilentlyContinue

$env:P4PORT = $Port
$env:P4USER = $script:P4User
$env:P4CLIENT = $Client
$env:P4TICKETS = $ticketFile

try {
    Write-Log "Using p4 client: $Client"
    Write-Log "Using p4 root: $resolvedRoot"
    Write-Log "Using p4 port: $Port"
    Write-Log "Mode: $(if ($preview) { 'preview' } elseif ($Submit) { 'apply-and-submit' } else { 'apply-open-changelist' })"

    Write-Log 'Logging in to Perforce.'
    $loginOutput = $p4Password | & $script:P4Exe login 2>&1
    $loginExitCode = $LASTEXITCODE
    foreach ($line in $loginOutput) {
        Write-Log ([string] $line)
    }
    if ($loginExitCode -ne 0) {
        throw "p4 login failed with exit code $loginExitCode"
    }

    $clientSpec = @(
        "Client: $Client",
        "Owner: $script:P4User",
        'Description:',
        "`tUnity asset layout migration workspace.",
        "Root: $resolvedRoot",
        'Options: noallwrite clobber nocompress unlocked nomodtime normdir',
        'SubmitOptions: submitunchanged',
        'LineEnd: local',
        'View:'
    )

    foreach ($viewLine in $viewLines) {
        $clientSpec += "`t$viewLine"
    }

    Write-Log 'Creating/updating Perforce migration client spec.'
    $clientSpecText = $clientSpec -join [Environment]::NewLine
    $clientOutput = $clientSpecText | & $script:P4Exe client -i 2>&1
    $clientExitCode = $LASTEXITCODE
    foreach ($line in $clientOutput) {
        Write-Log ([string] $line)
    }
    if ($clientExitCode -ne 0) {
        throw "p4 client -i failed with exit code $clientExitCode"
    }

    if (-not $SkipSync) {
        Invoke-P4 -Arguments @('sync', '--parallel=threads=8,min=100,minsize=1048576', "//$Client/...") | Out-Null
    }

    $change = ''
    if ($Apply) {
        $change = New-P4Change 'Move Unity art assets to Assets/Game module Art/Runtime layout and remove Meadow chunks.'
    }

    foreach ($mapping in $moveMappings + $moveFileMappings) {
        if (-not (Test-P4HasFiles $mapping.From)) {
            Write-Log "Skipping missing source: $($mapping.From)"
            continue
        }

        if (Test-P4HasFiles $mapping.To) {
            throw "Target already contains files; resolve manually before moving: $($mapping.To)"
        }

        $args = @('move')
        if ($preview) {
            $args += '-n'
        }
        if ($Force) {
            $args += '-f'
        }
        if ($Apply) {
            $args += @('-c', $change)
        }
        $args += @($mapping.From, $mapping.To)
        Invoke-P4 -Arguments $args | Out-Null
    }

    foreach ($deletePath in $deletePaths) {
        if (-not (Test-P4HasFiles $deletePath)) {
            Write-Log "Skipping missing delete path: $deletePath"
            continue
        }

        $args = @('delete')
        if ($preview) {
            $args += '-n'
        }
        if ($Apply) {
            $args += @('-c', $change)
        }
        $args += $deletePath
        Invoke-P4 -Arguments $args | Out-Null
    }

    if ($Submit) {
        Invoke-P4 -Arguments @('submit', '-c', $change) | Out-Null
        Write-Log "Submitted Perforce migration changelist: $change"
    } elseif ($Apply) {
        Write-Log "Opened Perforce migration changelist for review: $change"
    } else {
        Write-Log 'Preview finished. Re-run with -Apply to open files, or -Submit to submit after opening.'
    }
} finally {
    Remove-Item -LiteralPath $ticketFile -Force -ErrorAction SilentlyContinue
    Remove-Item Env:P4_PASSWORD -ErrorAction SilentlyContinue
}
