param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$Label,

    [int]$TimeoutSeconds = 600,

    [int]$PollSeconds = 5,

    [string]$LogPath = "",

    [string]$ProcessName = "Unity"
)

$ErrorActionPreference = "Stop"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$startedAt = Get-Date
$sawProcess = $false

while ((Get-Date) -lt $deadline) {
    if (Test-Path $Path) {
        Write-Host "$Label found: $Path"
        exit 0
    }

    $processes = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($processes) {
        $sawProcess = $true
    }

    if ($sawProcess -and -not $processes) {
        break
    }

    if (-not $sawProcess -and ((Get-Date) - $startedAt).TotalSeconds -ge 60) {
        break
    }

    Start-Sleep -Seconds $PollSeconds
}

if (-not [string]::IsNullOrWhiteSpace($LogPath) -and (Test-Path $LogPath)) {
    Write-Host "Last lines from ${LogPath}:"
    Get-Content $LogPath -Tail 80
}

throw "$Label was not generated within $TimeoutSeconds seconds: $Path"
