pipeline {
    agent { label 'windows-agent' }

    triggers {
        githubPush()
        pollSCM('H/2 * * * *')
    }

    options {
        timestamps()
        timeout(time: 180, unit: 'MINUTES')
        skipDefaultCheckout(true)
    }

    environment {
        UNITY_EXE = 'C:\\Program Files\\Unity\\Hub\\Editor\\6000.0.46f1\\Editor\\Unity.exe'
        UNITY_LOG = 'Logs\\build-windows.log'
        WINDOWS_BUILD_DIR = '.workspace\\builds\\windows\\Unity6-Windows-Development'
        WINDOWS_EXE = '.workspace\\builds\\windows\\Unity6-Windows-Development\\Unity6.exe'
        WINDOWS_ZIP = '.workspace\\builds\\windows\\Unity6-Windows-Development.zip'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                bat 'git --version'
                bat 'git lfs version'
                bat 'git lfs pull'
            }
        }

        stage('Clean Build Outputs') {
            steps {
                bat '''
@echo on
if exist ".workspace\\builds" rmdir /s /q ".workspace\\builds"
if exist "Logs\\build-windows.log" del /f /q "Logs\\build-windows.log"
mkdir ".workspace\\builds\\windows"
mkdir "Logs"
'''
            }
        }

        stage('Build Windows Player') {
            steps {
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
        }

        stage('Package Windows Player') {
            steps {
                bat '''
@echo on
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; if (-not (Test-Path $env:WINDOWS_EXE)) { throw ('Windows player executable was not found: ' + $env:WINDOWS_EXE) }; if (Test-Path $env:WINDOWS_ZIP) { Remove-Item -LiteralPath $env:WINDOWS_ZIP -Force }; Compress-Archive -Path (Join-Path $env:WINDOWS_BUILD_DIR '*') -DestinationPath $env:WINDOWS_ZIP -Force"
'''
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: 'Logs/build-windows.log', allowEmptyArchive: true
        }
        success {
            archiveArtifacts artifacts: '.workspace/builds/windows/Unity6-Windows-Development.zip', fingerprint: true
        }
    }
}
