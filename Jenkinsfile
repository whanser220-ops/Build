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
            defaultValue: '''//depot/Assets/GameResources/... //${P4_CLIENT}/Assets/GameResources/...
//depot/Assets/GameResources.meta //${P4_CLIENT}/Assets/GameResources.meta
//depot/Assets/GameAssets/... //${P4_CLIENT}/Assets/GameAssets/...
//depot/Assets/GameAssets.meta //${P4_CLIENT}/Assets/GameAssets.meta''',
            description: 'Perforce client view for Unity assets. Use ${P4_CLIENT} as the client placeholder.'
        )
    ])
])

def defaultP4Port = 'ssl:1.117.232.198:1666'
def defaultP4CredentialsId = 'perforce-jenkins'
def defaultP4Client = 'jenkins_unity_smoke_build_art'
def defaultP4View = '''//depot/Assets/GameResources/... //${P4_CLIENT}/Assets/GameResources/...
//depot/Assets/GameResources.meta //${P4_CLIENT}/Assets/GameResources.meta
//depot/Assets/GameAssets/... //${P4_CLIENT}/Assets/GameAssets/...
//depot/Assets/GameAssets.meta //${P4_CLIENT}/Assets/GameAssets.meta'''

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
                'BUILD_MANIFEST=.workspace\\build-manifest.json'
            ]) {
                try {
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
if exist "Logs\\build-windows.log" del /f /q "Logs\\build-windows.log"
if not exist ".workspace\\builds\\windows" mkdir ".workspace\\builds\\windows"
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
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "tools\\Invoke-Unity.ps1" -ProjectPath . -batchmode -quit -executeMethod Unity6.Ci.CiPlayerBuild.BuildWindowsDevelopment -logFile "%UNITY_LOG%" --ci-output "%WINDOWS_EXE%"
set UNITY_EXIT=%ERRORLEVEL%
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
                } finally {
                    stage('Archive') {
                        archiveArtifacts artifacts: 'Logs/build-windows.log,Logs/p4-sync.log,.workspace/build-manifest.json', allowEmptyArchive: true
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
