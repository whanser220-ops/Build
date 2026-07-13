[CmdletBinding()]
param(
    [string]$AppDir = (Join-Path $PSScriptRoot "bundle-report-web"),
    [string]$ReportRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) ".workspace\artifacts\bundle-report"),
    [string]$SshHost = "1.117.232.198",
    [string]$SshUser = "ubuntu",
    [string]$SshKeyPath = $env:BUNDLE_REPORT_SSH_KEY,
    [string]$RemoteAppDir = "/opt/unity6-bundle-report/app",
    [string]$RemoteDataDir = "/var/www/unity6-bundle-report/data",
    [int]$RemotePort = 3100,
    [string]$BasePath = "/bundle-report",
    [string]$Pm2Name = "unity6-bundle-report",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Assert-IsUnderPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $fullPath = Resolve-FullPath $Path
    $fullRoot = Resolve-FullPath $Root
    if (-not $fullRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $fullRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$fullPath' is outside expected root '$fullRoot'."
    }
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$appDirFull = Resolve-FullPath $AppDir
$reportRootFull = Resolve-FullPath $ReportRoot
$stagingRoot = Join-Path $repoRoot ".workspace\artifacts\bundle-report-web"
$deployRoot = Join-Path $stagingRoot "deploy"
$stagingApp = Join-Path $deployRoot "app"
$stagingData = Join-Path $deployRoot "data"
$appArchive = Join-Path $stagingRoot "unity6-bundle-report-app.zip"
$dataArchive = Join-Path $stagingRoot "unity6-bundle-report-data.zip"

if ([string]::IsNullOrWhiteSpace($SshKeyPath)) {
    throw "SshKeyPath is required. Pass -SshKeyPath or set BUNDLE_REPORT_SSH_KEY."
}

if (-not (Test-Path -LiteralPath $SshKeyPath)) {
    throw "SSH key was not found: $SshKeyPath"
}

if (-not (Test-Path -LiteralPath $appDirFull)) {
    throw "App directory was not found: $appDirFull"
}

if (-not (Test-Path -LiteralPath $reportRootFull)) {
    throw "Report root was not found: $reportRootFull"
}

if (-not $SkipBuild) {
    Push-Location $appDirFull
    try {
        npm ci
        $env:NEXT_PUBLIC_BASE_PATH = $BasePath
        npm run typecheck
        npm run build
    }
    finally {
        Pop-Location
    }
}

$standaloneDir = Join-Path $appDirFull ".next\standalone"
$staticDir = Join-Path $appDirFull ".next\static"
$publicDir = Join-Path $appDirFull "public"
$ecosystemFile = Join-Path $appDirFull "ecosystem.config.cjs"

if (-not (Test-Path -LiteralPath $standaloneDir)) {
    throw "Next standalone output was not found: $standaloneDir"
}
if (-not (Test-Path -LiteralPath $staticDir)) {
    throw "Next static output was not found: $staticDir"
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
Assert-IsUnderPath -Path $deployRoot -Root $stagingRoot
if (Test-Path -LiteralPath $deployRoot) {
    Remove-Item -LiteralPath $deployRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingApp, $stagingData | Out-Null

Copy-Item -Path (Join-Path $standaloneDir "*") -Destination $stagingApp -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $stagingApp ".next") | Out-Null
Copy-Item -LiteralPath $staticDir -Destination (Join-Path $stagingApp ".next\static") -Recurse -Force
if (Test-Path -LiteralPath $publicDir) {
    Copy-Item -LiteralPath $publicDir -Destination (Join-Path $stagingApp "public") -Recurse -Force
}
Copy-Item -LiteralPath $ecosystemFile -Destination (Join-Path $stagingApp "ecosystem.config.cjs") -Force

Copy-Item -Path (Join-Path $reportRootFull "*") -Destination $stagingData -Recurse -Force

if (Test-Path -LiteralPath $appArchive) {
    Remove-Item -LiteralPath $appArchive -Force
}
if (Test-Path -LiteralPath $dataArchive) {
    Remove-Item -LiteralPath $dataArchive -Force
}

Compress-Archive -Path (Join-Path $stagingApp "*") -DestinationPath $appArchive -Force
Compress-Archive -Path (Join-Path $stagingData "*") -DestinationPath $dataArchive -Force

$sshTarget = "${SshUser}@${SshHost}"
$appArchiveName = Split-Path $appArchive -Leaf
$dataArchiveName = Split-Path $dataArchive -Leaf
$sshOptions = @("-i", $SshKeyPath, "-o", "StrictHostKeyChecking=accept-new")

& scp @sshOptions $appArchive "${sshTarget}:/tmp/$appArchiveName"
if ($LASTEXITCODE -ne 0) { throw "scp app archive failed." }
& scp @sshOptions $dataArchive "${sshTarget}:/tmp/$dataArchiveName"
if ($LASTEXITCODE -ne 0) { throw "scp data archive failed." }

$remoteScript = @"
set -euo pipefail
REMOTE_APP_DIR='$RemoteAppDir'
REMOTE_DATA_DIR='$RemoteDataDir'
REMOTE_PORT='$RemotePort'
BASE_PATH='$BasePath'
PM2_NAME='$Pm2Name'
APP_ARCHIVE='/tmp/$appArchiveName'
DATA_ARCHIVE='/tmp/$dataArchiveName'
REMOTE_USER='$SshUser'

sudo rm -rf "`$REMOTE_APP_DIR"
sudo mkdir -p "`$REMOTE_APP_DIR" "`$REMOTE_DATA_DIR"
sudo chown -R "`$REMOTE_USER":"`$REMOTE_USER" "`$REMOTE_APP_DIR" "`$REMOTE_DATA_DIR"

unzip_allow_warnings() {
  archive="`$1"
  destination="`$2"
  set +e
  unzip -oq "`$archive" -d "`$destination"
  rc="`$?"
  set -e
  if [ "`$rc" -gt 1 ]; then
    exit "`$rc"
  fi
}

unzip_allow_warnings "`$APP_ARCHIVE" "`$REMOTE_APP_DIR"
unzip_allow_warnings "`$DATA_ARCHIVE" "`$REMOTE_DATA_DIR"

REMOTE_DATA_DIR="`$REMOTE_DATA_DIR" node <<'NODE'
const fs = require('fs');
const path = require('path');

const dataDir = process.env.REMOTE_DATA_DIR;
const reports = [];

function walk(dir) {
  if (!fs.existsSync(dir)) return;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const child = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(child);
    } else if (entry.isFile() && entry.name === 'bundle_report.json') {
      const report = JSON.parse(fs.readFileSync(child, 'utf8'));
      const summary = report.summary || {};
      reports.push({
        id: summary.reportId || path.relative(dataDir, child),
        buildTarget: summary.buildTarget || '',
        packageName: summary.packageName || '',
        packageVersion: summary.packageVersion || '',
        generatedAt: summary.generatedAt || '',
        reportPath: path.relative(dataDir, child).split(path.sep).join('/'),
        bundleCount: summary.bundleCount || 0,
        duplicateAssetCount: summary.duplicateAssetCount || 0,
        smallBundleCount: summary.smallBundleCount || 0,
        largeBundleCount: summary.largeBundleCount || 0,
        maxDependencyDepth: summary.maxDependencyDepth || 0
      });
    }
  }
}

walk(dataDir);
reports.sort((a, b) => {
  const dateCompare = String(b.generatedAt).localeCompare(String(a.generatedAt));
  if (dateCompare !== 0) return dateCompare;
  return String(b.packageVersion).localeCompare(String(a.packageVersion));
});

const index = {
  latestReportId: reports[0] ? reports[0].id : '',
  reports
};
fs.writeFileSync(path.join(dataDir, 'index.json'), JSON.stringify(index, null, 2));
if (reports[0]) {
  fs.copyFileSync(path.join(dataDir, reports[0].reportPath), path.join(dataDir, 'latest.json'));
}
NODE

cd "`$REMOTE_APP_DIR"
PM2_APP_NAME="`$PM2_NAME" PORT="`$REMOTE_PORT" BUNDLE_REPORT_DATA_DIR="`$REMOTE_DATA_DIR" NEXT_PUBLIC_BASE_PATH="`$BASE_PATH" pm2 startOrReload ecosystem.config.cjs --update-env
pm2 save || true

sudo tee /etc/nginx/conf.d/unity6-bundle-report.conf >/dev/null <<NGINX
server {
    listen 80;
    server_name $SshHost;

    location = $BasePath {
        return 301 $BasePath/;
    }

    location $BasePath/ {
        proxy_pass http://127.0.0.1:$RemotePort;
        proxy_http_version 1.1;
        proxy_set_header Host \`$host;
        proxy_set_header X-Real-IP \`$remote_addr;
        proxy_set_header X-Forwarded-For \`$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \`$scheme;
        proxy_set_header Upgrade \`$http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
NGINX

sudo nginx -t
sudo systemctl reload nginx
rm -f "`$APP_ARCHIVE" "`$DATA_ARCHIVE"
"@

$remoteScriptPath = Join-Path $stagingRoot "remote-deploy.sh"
$remoteScriptContent = ($remoteScript -replace "`r`n", "`n") -replace "`r", "`n"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($remoteScriptPath, $remoteScriptContent, $utf8NoBom)

$remoteScriptName = Split-Path $remoteScriptPath -Leaf
& scp @sshOptions $remoteScriptPath "${sshTarget}:/tmp/$remoteScriptName"
if ($LASTEXITCODE -ne 0) { throw "scp remote deploy script failed." }
& ssh @sshOptions $sshTarget "bash /tmp/$remoteScriptName; status=`$?; rm -f /tmp/$remoteScriptName; exit `$status"
if ($LASTEXITCODE -ne 0) { throw "remote deploy failed." }

Write-Host "Bundle report web deployed: http://$SshHost$BasePath"
