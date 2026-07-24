# Unity Jenkins Linux Agent

This image packages a Jenkins inbound agent with Unity `6000.0.46f1`, Windows Mono build support, Git LFS, Perforce CLI, Java 21, and helper scripts for the Unity6 game build.

The existing project `Jenkinsfile` targets a Windows node and uses `bat`. This image is a Linux container agent, so use the included `Jenkinsfile.example` shape or convert the job stages from `bat` to `sh`.

## Build

```powershell
cd C:\Users\huang\Documents\云服务器\Build
docker build -t unity6-jenkins-agent:6000.0.46f1 -f ci\jenkins-agent\linux\Dockerfile ci\jenkins-agent\linux
```

The base Unity image is large. The first build can take a long time.

## Smoke Test

```powershell
docker run --rm unity6-jenkins-agent:6000.0.46f1 bash -lc "java -version && git lfs version && p4 -V && /opt/unity/Editor/Unity -version"
```

## Persistent Workspace Model

The image is designed for ephemeral Jenkins agent containers with persistent host or Docker-volume storage. The persistent workspace is a cache, not the source of truth:

```text
Jenkins controller
  -> creates an agent container on the build machine
     -> mounts a persistent workspace
     -> mounts persistent Unity caches
     -> fetches Git source
     -> syncs Perforce assets
     -> runs the build script
     -> removes the container after the build
```

If you copied `C:\jenkins-agent\workspace\unity-smoke-build` into this layout, treat that copy as a one-time seed only. Future builds should not mount or read the fixed Jenkins node workspace. The container should reuse only its own mounted workspace/cache directories, then reconcile them from Git and Perforce on every run.

Use Docker volumes for the project workspace and Unity caches. Git checkout, Git LFS files, Perforce assets, Unity import cache (`Library`), logs, and build artifacts all live inside Docker-managed storage:

```bash
docker volume create unity6-workspace
docker volume create unity6-library
docker volume create unity6-cache
docker volume create unity6-config

docker run --rm --name unity6-agent-build \
  --mount type=volume,source=unity6-workspace,target=/workspace \
  --mount type=volume,source=unity6-library,target=/workspace/Library \
  --mount type=volume,source=unity6-cache,target=/root/.cache/unity3d \
  --mount type=volume,source=unity6-config,target=/root/.config/unity3d \
  -e UNITY_LICENSE_B64="$UNITY_LICENSE_B64" \
  -e UNITY_EMAIL="$UNITY_EMAIL" \
  -e UNITY_PASSWORD="$UNITY_PASSWORD" \
  -e GIT_REPOSITORY_URL=https://github.com/whanser220-ops/Build.git \
  -e GIT_REF=Main \
  -e P4_PORT=ssl:1.117.232.198:1666 \
  -e P4_CLIENT_NAME=jenkins_unity_linux_agent_art \
  -e P4_USERNAME="$P4_USERNAME" \
  -e P4_PASSWORD="$P4_PASSWORD" \
  -w /workspace \
  unity6-jenkins-agent:6000.0.46f1 \
  bootstrap-unity-workspace.sh
```

On Docker Desktop for Windows, use the same volume-backed layout:

```powershell
docker volume create unity6-workspace
docker volume create unity6-library
docker volume create unity6-cache
docker volume create unity6-config

docker run --rm --name unity6-agent-build `
  --mount type=volume,source=unity6-workspace,target=/workspace `
  --mount type=volume,source=unity6-library,target=/workspace/Library `
  --mount type=volume,source=unity6-cache,target=/root/.cache/unity3d `
  --mount type=volume,source=unity6-config,target=/root/.config/unity3d `
  -e UNITY_LICENSE_B64=$env:UNITY_LICENSE_B64 `
  -e UNITY_EMAIL=$env:UNITY_EMAIL `
  -e UNITY_PASSWORD=$env:UNITY_PASSWORD `
  -e GIT_REPOSITORY_URL=https://github.com/whanser220-ops/Build.git `
  -e GIT_REF=Main `
  -e P4_PORT=ssl:1.117.232.198:1666 `
  -e P4_CLIENT_NAME=jenkins_unity_linux_agent_art `
  -e P4_USERNAME=$env:P4_USERNAME `
  -e P4_PASSWORD=$env:P4_PASSWORD `
  -w /workspace `
  unity6-jenkins-agent:6000.0.46f1 `
  bootstrap-unity-workspace.sh
```

To inspect the volume-backed workspace without copying it to Windows:

```powershell
docker run --rm -it `
  --mount type=volume,source=unity6-workspace,target=/workspace `
  --mount type=volume,source=unity6-library,target=/workspace/Library `
  --mount type=volume,source=unity6-cache,target=/root/.cache/unity3d `
  --mount type=volume,source=unity6-config,target=/root/.config/unity3d `
  --mount type=volume,source=unity6-personal-license,target=/root/.local/share/unity3d `
  -w /workspace `
  unity6-jenkins-agent:6000.0.46f1 `
  bash
```

## Run as an Inbound Agent

Create a Jenkins node, copy its inbound agent secret, then run:

```powershell
docker run -d --name unity6-agent --restart unless-stopped `
  -e JENKINS_URL="http://1.117.232.198:8080" `
  -e JENKINS_AGENT_NAME="unity-linux-agent" `
  -e JENKINS_AGENT_SECRET="<agent-secret>" `
  -e JENKINS_WEB_SOCKET="true" `
  --mount type=volume,source=unity6-agent-work,target=/home/jenkins/agent `
  --mount type=volume,source=unity6-library,target=/home/jenkins/agent/workspace/unity6/Library `
  --mount type=volume,source=unity6-cache,target=/root/.cache/unity3d `
  --mount type=volume,source=unity6-config,target=/root/.config/unity3d `
  --mount type=volume,source=unity6-personal-license,target=/root/.local/share/unity3d `
  unity6-jenkins-agent:6000.0.46f1
```

For an inbound Jenkins agent, Jenkins workspaces live under `/home/jenkins/agent/workspace/...`; because `/home/jenkins/agent` is a Docker volume above, the Git checkout and Perforce asset sync also stay in Docker-managed storage. Keep the nested `unity6-library` mount pointed at the exact Jenkins job workspace `Library` path so Unity's import cache is also persistent and separate from source files.

Unity still needs a valid license. For the free Unity Personal plan, provide a Unity Hub-generated `Unity_lic.ulf`; when Unity requires reactivation, the script can also use Unity account credentials in the same style as GameCI's Linux container activation model:

- `UNITY_LICENSE_B64`: base64-encoded Linux `Unity_lic.ulf`; recommended for Jenkins secret text.
- `UNITY_LICENSE`: raw ULF content.
- `UNITY_LICENSE_FILE`: mounted ULF file path inside the container.
- `UNITY_EMAIL` / `UNITY_PASSWORD`: optional Unity account credentials used to reactivate the Personal license inside the Linux container when needed.
- `UNITY_LICENSE_MODE=personal`: default mode. With `UNITY_LICENSE_B64`/`UNITY_LICENSE` and Unity credentials, the script extracts the Personal serial from the `.ulf` and activates online in the container. Without credentials, it verifies the provided `.ulf` as-is.

Store the `.ulf` as a Jenkins secret text after base64 encoding:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\ProgramData\Unity\Unity_lic.ulf'))
```

A Windows node license such as `C:\ProgramData\Unity\Unity_lic.ulf` can fail in Linux if the machine binding differs. In that case, provide `UNITY_EMAIL` / `UNITY_PASSWORD`; the script uses the `.ulf` as a source for the Personal serial, then activates the Linux container online.

Set `UNITY_PERSONAL_REACTIVATE_FROM_LICENSE=false` only if you want to skip the GameCI-style Personal reactivation and try verifying an already-valid mounted Linux `.ulf` as-is.

## Build Script

Inside Jenkins, run:

```bash
bootstrap-unity-workspace.sh
```

`bootstrap-unity-workspace.sh` performs:

- Git fetch/reset of `GIT_REF` from `GIT_REPOSITORY_URL`
- non-destructive cleanup of ordinary untracked files while preserving ignored Perforce assets and Unity caches
- delegation to `build-unity-windows.sh`

`build-unity-windows.sh` performs:

- Unity license import/activation/verification through `activate-unity-license.sh`
- optional `git lfs pull`
- optional Perforce asset sync through `sync-perforce-assets.sh`
- Unity batchmode build through `Unity6.Ci.CiPlayerBuild.BuildWindowsDevelopment`
- Windows player zip creation under `.workspace/builds/windows/Unity6-Windows-Development.zip`

Useful environment variables:

- `GIT_REPOSITORY_URL` is required for an empty workspace.
- `GIT_REF=Main` selects the branch, tag, or ref fetched before build.
- `BOOTSTRAP_ONLY=true` validates Git workspace bootstrap without running Perforce or Unity.
- `UNITY_LICENSE_MODE=personal` uses Unity Personal mode.
- `UNITY_LICENSE_B64`, `UNITY_LICENSE`, or `UNITY_LICENSE_FILE` provides the Unity Personal license source.
- `UNITY_EMAIL`/`UNITY_PASSWORD` reactivates a Personal license when a `.ulf` source is present; `UNITY_SERIAL` can also be supplied directly for serial-based activation.
- `UNITY_PERSONAL_REACTIVATE_FROM_LICENSE=false` skips serial extraction from a Personal `.ulf`.
- `UNITY_CREATE_ALF=true` creates a manual activation request only when `UNITY_LICENSE_MODE` is not `personal`.
- `UNITY_VERIFY_LICENSE=false` skips the pre-build license verification step.
- `P4_SYNC_ENABLED=false` skips Perforce sync.
- `P4_USERNAME` and `P4_PASSWORD` are required when Perforce sync is enabled.
- `P4_PORT`, `P4_CLIENT_NAME`, `P4_ASSET_CL`, and `P4_VIEW_FILE` override Perforce defaults.
- `RUN_GIT_LFS_PULL=false` skips `git lfs pull`.
- `UNITY_EXECUTABLE` overrides `/opt/unity/Editor/Unity`.
- `UNITY_PROJECT_PATH` overrides the current workspace.
