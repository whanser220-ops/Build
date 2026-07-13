properties([
    pipelineTriggers([
        githubPush(),
        pollSCM('H/2 * * * *')
    ]),
    parameters([
        booleanParam(
            name: 'P4_SYNC_ENABLED',
            defaultValue: true,
            description: 'Sync Unity assets from Perforce before building.'
        ),
        string(
            name: 'P4_PORT',
            defaultValue: 'ssl:1.117.232.198:1666',
            description: 'Perforce server address, for example ssl:perforce.example.com:1666.'
        ),
        string(
            name: 'P4_CREDENTIALS_ID',
            defaultValue: 'perforce-jenkins',
            description: 'Jenkins username/password credential ID for Perforce.'
        ),
        string(
            name: 'P4_CLIENT',
            defaultValue: 'jenkins_unity_smoke_build_art',
            description: 'Perforce workspace/client used on the Windows build node.'
        ),
        string(
            name: 'P4_ASSET_CL',
            defaultValue: '',
            description: 'Optional Perforce changelist to pin the asset sync. Leave empty for latest.'
        ),
        text(
            name: 'P4_VIEW',
            defaultValue: '''//depot/Assets/Game/Characters/Qianxia/Art/... //${P4_CLIENT}/Assets/Game/Characters/Qianxia/Art/...
//depot/Assets/Game/Characters/Qianxia/Runtime/... //${P4_CLIENT}/Assets/Game/Characters/Qianxia/Runtime/...
//depot/Assets/Game/Core/Input/Runtime/... //${P4_CLIENT}/Assets/Game/Core/Input/Runtime/...
//depot/Assets/Game/Shared/StylizedPackCommon/Art/... //${P4_CLIENT}/Assets/Game/Shared/StylizedPackCommon/Art/...
//depot/Assets/Game/Shared/StylizedPackCommon/Runtime/... //${P4_CLIENT}/Assets/Game/Shared/StylizedPackCommon/Runtime/...
//depot/Assets/Game/UI/UILogin/Runtime/... //${P4_CLIENT}/Assets/Game/UI/UILogin/Runtime/...
//depot/Assets/Game/UI/UIMain/Runtime/... //${P4_CLIENT}/Assets/Game/UI/UIMain/Runtime/...
//depot/Assets/Game/Worlds/Meadow/Art/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Art/...
//depot/Assets/Game/Worlds/Meadow/Runtime/Configs/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Runtime/Configs/...
//depot/Assets/Game/Worlds/Meadow/Runtime/Scenes/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Runtime/Scenes/...
//depot/Assets/Game/Worlds/Meadow/Runtime/Seasons/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Runtime/Seasons/...
//depot/Assets/Game/Worlds/Meadow/Runtime/Shared/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Runtime/Shared/...''',
            description: 'Perforce client view for Unity assets. Use ${P4_CLIENT} as the client placeholder.'
        ),
        booleanParam(
            name: 'BUNDLE_REPORT_DEPLOY_ENABLED',
            defaultValue: true,
            description: 'Deploy the YooAsset bundle report web app after a successful formal build.'
        ),
        string(
            name: 'BUNDLE_REPORT_HOST',
            defaultValue: '1.117.232.198',
            description: 'Bundle report web server host.'
        ),
        string(
            name: 'BUNDLE_REPORT_SSH_USER',
            defaultValue: 'ubuntu',
            description: 'SSH user for bundle report deployment.'
        ),
        string(
            name: 'BUNDLE_REPORT_SSH_CREDENTIALS_ID',
            defaultValue: 'bundle-report-ssh-key',
            description: 'Jenkins SSH private key credential ID for bundle report deployment.'
        )
    ])
])

def defaultP4Port = 'ssl:1.117.232.198:1666'
def defaultP4CredentialsId = 'perforce-jenkins'
def defaultP4Client = 'jenkins_unity_smoke_build_art'
def defaultP4View = '''//depot/Assets/Game/Characters/Qianxia/Art/... //${P4_CLIENT}/Assets/Game/Characters/Qianxia/Art/...
//depot/Assets/Game/Characters/Qianxia/Runtime/... //${P4_CLIENT}/Assets/Game/Characters/Qianxia/Runtime/...
//depot/Assets/Game/Core/Input/Runtime/... //${P4_CLIENT}/Assets/Game/Core/Input/Runtime/...
//depot/Assets/Game/Shared/StylizedPackCommon/Art/... //${P4_CLIENT}/Assets/Game/Shared/StylizedPackCommon/Art/...
//depot/Assets/Game/Shared/StylizedPackCommon/Runtime/... //${P4_CLIENT}/Assets/Game/Shared/StylizedPackCommon/Runtime/...
//depot/Assets/Game/UI/UILogin/Runtime/... //${P4_CLIENT}/Assets/Game/UI/UILogin/Runtime/...
//depot/Assets/Game/UI/UIMain/Runtime/... //${P4_CLIENT}/Assets/Game/UI/UIMain/Runtime/...
//depot/Assets/Game/Worlds/Meadow/Art/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Art/...
//depot/Assets/Game/Worlds/Meadow/Runtime/Configs/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Runtime/Configs/...
//depot/Assets/Game/Worlds/Meadow/Runtime/Scenes/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Runtime/Scenes/...
//depot/Assets/Game/Worlds/Meadow/Runtime/Seasons/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Runtime/Seasons/...
//depot/Assets/Game/Worlds/Meadow/Runtime/Shared/... //${P4_CLIENT}/Assets/Game/Worlds/Meadow/Runtime/Shared/...'''

def runWindowsPlayerBuild = {
    timestamps {
        timeout(time: 180, unit: 'MINUTES') {
            withEnv([
                'UNITY_EXE=C:\\Program Files\\Unity\\Hub\\Editor\\6000.0.46f1\\Editor\\Unity.exe',
                'UNITY_LOG=Logs\\build-windows.log',
                'WINDOWS_BUILD_DIR=.workspace\\builds\\windows\\Unity6-Windows-Development',
                'WINDOWS_EXE=.workspace\\builds\\windows\\Unity6-Windows-Development\\Unity6.exe',
                'WINDOWS_ZIP=.workspace\\builds\\windows\\Unity6-Windows-Development.zip',
                'P4_SYNC_LOG=Logs\\p4-sync.log',
                'BUILD_MANIFEST=.workspace\\build-manifest.json',
                'BUNDLE_REPORT_ROOT=.workspace\\artifacts\\bundle-report',
                'BUNDLE_REPORT_WEB_DIR=tools\\bundle-report-web',
                'BUNDLE_REPORT_BASE_PATH=/bundle-report'
            ]) {
                stage('Checkout') {
                        if (env.JENKINSFILE_BOOTSTRAPPED != 'true') {
                            checkout scm
                        }
                        bat 'git --version'
                        bat 'git lfs version'
                        bat 'git lfs pull'
                    }

                    stage('Sync Perforce Assets') {
                        script {
                            def p4SyncEnabled = params.P4_SYNC_ENABLED == null ? true : params.P4_SYNC_ENABLED

                            if (!p4SyncEnabled) {
                                echo 'Skipping Perforce asset sync because P4_SYNC_ENABLED=false.'
                            } else {
                                def p4Port = params.P4_PORT?.trim() ?: defaultP4Port
                                def p4CredentialsId = params.P4_CREDENTIALS_ID?.trim() ?: defaultP4CredentialsId
                                def configuredP4Client = params.P4_CLIENT?.trim() ?: defaultP4Client
                                def p4AssetCl = params.P4_ASSET_CL?.trim()
                                def p4View = params.P4_VIEW?.trim() ?: defaultP4View
                                def p4Client = configuredP4Client ?: "jenkins-${env.NODE_NAME}-${env.JOB_NAME}-${env.EXECUTOR_NUMBER}".replaceAll(/[^A-Za-z0-9_.-]/, '_')

                                if (!p4Port) {
                                    error 'P4_PORT is required before Perforce asset sync can run.'
                                }
                                if (!p4CredentialsId) {
                                    error 'P4_CREDENTIALS_ID is required before Perforce asset sync can run.'
                                }
                                if (!p4View) {
                                    error 'P4_VIEW is required before Perforce asset sync can run.'
                                }

                                bat 'if not exist ".workspace" mkdir ".workspace"'
                                writeFile file: '.workspace/p4-view.txt', text: p4View + '\n'

                                withCredentials([usernamePassword(
                                    credentialsId: p4CredentialsId,
                                    usernameVariable: 'P4_USERNAME',
                                    passwordVariable: 'P4_PASSWORD'
                                )]) {
                                    withEnv([
                                        "P4_PORT=${p4Port}",
                                        "P4_ASSET_CL=${p4AssetCl ?: ''}",
                                        "P4_CLIENT_NAME=${p4Client}"
                                    ]) {
                                        bat '''
@echo on
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "tools\\Sync-PerforceAssets.ps1" -Port "%P4_PORT%" -Client "%P4_CLIENT_NAME%" -Root "%WORKSPACE%" -ViewFile ".workspace\\p4-view.txt" -Changelist "%P4_ASSET_CL%" -LogPath "%P4_SYNC_LOG%" -ManifestPath "%BUILD_MANIFEST%"
'''
                                    }
                                }
                            }
                        }
                    }

                    stage('Clean Build Outputs') {
                        bat '''
@echo on
if exist ".workspace\\builds" rmdir /s /q ".workspace\\builds"
if exist ".workspace\\artifacts\\yooasset-build" rmdir /s /q ".workspace\\artifacts\\yooasset-build"
if exist ".workspace\\artifacts\\bundle-report-web" rmdir /s /q ".workspace\\artifacts\\bundle-report-web"
if exist "Assets\\StreamingAssets\\yoo" rmdir /s /q "Assets\\StreamingAssets\\yoo"
if exist "Logs\\build-windows.log" del /f /q "Logs\\build-windows.log"
if not exist ".workspace\\builds\\windows" mkdir ".workspace\\builds\\windows"
if not exist "%BUNDLE_REPORT_ROOT%\\StandaloneWindows64\\DefaultPackage\\%BUILD_NUMBER%" mkdir "%BUNDLE_REPORT_ROOT%\\StandaloneWindows64\\DefaultPackage\\%BUILD_NUMBER%"
if not exist "Logs" mkdir "Logs"
'''
                    }

                    stage('Build Windows Player') {
                        bat '''
@echo on
echo Jenkins workspace: %WORKSPACE%
if not exist "%UNITY_EXE%" (
  echo Unity not found: %UNITY_EXE%
  exit /b 1
)
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "tools\\Invoke-Unity.ps1" -ProjectPath . -batchmode -quit -executeMethod Unity6.Ci.CiPlayerBuild.BuildWindowsDevelopment -logFile "%UNITY_LOG%" --ci-output "%WINDOWS_EXE%" --yooasset-target StandaloneWindows64 --yooasset-exclude-source-assets --yooasset-package-name DefaultPackage --yooasset-package-version "%BUILD_NUMBER%" --yooasset-build-output "%WORKSPACE%\\.workspace\\artifacts\\yooasset-build" --yooasset-plan-output "%WORKSPACE%\\.workspace\\artifacts\\yooasset\\StandaloneWindows64\\angrymesh\\yooasset_build_plan.json" --bundle-report-output "%WORKSPACE%\\%BUNDLE_REPORT_ROOT%\\StandaloneWindows64\\DefaultPackage\\%BUILD_NUMBER%\\bundle_report.json"
set UNITY_EXIT=%ERRORLEVEL%
if "%UNITY_EXIT%"=="0" if exist "%WORKSPACE%\\.workspace\\artifacts\\yooasset-build\\StandaloneWindows64\\DefaultPackage\\OutputCache" rmdir /s /q "%WORKSPACE%\\.workspace\\artifacts\\yooasset-build\\StandaloneWindows64\\DefaultPackage\\OutputCache"
if exist "%WORKSPACE%\\%UNITY_LOG%" type "%WORKSPACE%\\%UNITY_LOG%"
exit /b %UNITY_EXIT%
'''
                    }

                    stage('Package Windows Player') {
                        bat '''
@echo on
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; if (-not (Test-Path $env:WINDOWS_EXE)) { throw ('Windows player executable was not found: ' + $env:WINDOWS_EXE) }; if (Test-Path $env:WINDOWS_ZIP) { Remove-Item -LiteralPath $env:WINDOWS_ZIP -Force }; Compress-Archive -Path (Join-Path $env:WINDOWS_BUILD_DIR '*') -DestinationPath $env:WINDOWS_ZIP -Force"
'''
                    }

                    stage('Archive Bundle Reports') {
                        archiveArtifacts artifacts: '.workspace/artifacts/bundle-report/**, .workspace/artifacts/yooasset-build/**/*.report', allowEmptyArchive: false
                    }

                    stage('Build Bundle Report Web') {
                        bat '''
@echo on
cd "%BUNDLE_REPORT_WEB_DIR%"
set NEXT_PUBLIC_BASE_PATH=%BUNDLE_REPORT_BASE_PATH%
npm ci
npm run typecheck
npm run build
'''
                    }

                    stage('Deploy Bundle Report Web') {
                        script {
                            def deployEnabled = params.BUNDLE_REPORT_DEPLOY_ENABLED == null ? true : params.BUNDLE_REPORT_DEPLOY_ENABLED
                            if (!deployEnabled) {
                                echo 'Skipping bundle report web deploy because BUNDLE_REPORT_DEPLOY_ENABLED=false.'
                            } else {
                                def reportHost = params.BUNDLE_REPORT_HOST?.trim() ?: '1.117.232.198'
                                def reportUser = params.BUNDLE_REPORT_SSH_USER?.trim() ?: 'ubuntu'
                                def reportCredentialsId = params.BUNDLE_REPORT_SSH_CREDENTIALS_ID?.trim() ?: 'bundle-report-ssh-key'

                                withCredentials([sshUserPrivateKey(
                                    credentialsId: reportCredentialsId,
                                    keyFileVariable: 'BUNDLE_REPORT_SSH_KEY'
                                )]) {
                                    withEnv([
                                        "BUNDLE_REPORT_DEPLOY_HOST=${reportHost}",
                                        "BUNDLE_REPORT_DEPLOY_USER=${reportUser}"
                                    ]) {
                                        bat '''
@echo on
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "tools\\Deploy-BundleReportWeb.ps1" -AppDir "%WORKSPACE%\\%BUNDLE_REPORT_WEB_DIR%" -ReportRoot "%WORKSPACE%\\%BUNDLE_REPORT_ROOT%" -SshHost "%BUNDLE_REPORT_DEPLOY_HOST%" -SshUser "%BUNDLE_REPORT_DEPLOY_USER%" -SshKeyPath "%BUNDLE_REPORT_SSH_KEY%" -BasePath "%BUNDLE_REPORT_BASE_PATH%" -SkipBuild
'''
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

if (env.JENKINSFILE_BOOTSTRAPPED == 'true') {
    runWindowsPlayerBuild()
} else {
    node('windows-agent') {
        runWindowsPlayerBuild()
    }
}
