pipeline {
    agent { label 'windows-agent' }

    triggers {
        githubPush()
        pollSCM('H/2 * * * *')
    }

    options {
        timestamps()
        timeout(time: 60, unit: 'MINUTES')
        skipDefaultCheckout(true)
    }

    environment {
        UNITY_EXE = 'C:\\Program Files\\Unity\\Hub\\Editor\\6000.0.46f1\\Editor\\Unity.exe'
        UNITY_LOG = 'unity-smoke.log'
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

        stage('Start Unity') {
            steps {
                bat '''
@echo on
echo Jenkins workspace: %WORKSPACE%
if not exist "%UNITY_EXE%" (
  echo Unity not found: %UNITY_EXE%
  exit /b 1
)
"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%WORKSPACE%" -logFile "%WORKSPACE%\\%UNITY_LOG%"
set UNITY_EXIT=%ERRORLEVEL%
if exist "%WORKSPACE%\\%UNITY_LOG%" type "%WORKSPACE%\\%UNITY_LOG%"
exit /b %UNITY_EXIT%
'''
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: 'unity-smoke.log', allowEmptyArchive: true
        }
    }
}
