# Unity Personal License Helper

This one-time helper runs Unity Hub in a Linux GUI session exposed through noVNC. Use it to activate a free Unity Personal license for the Linux container environment.

Build:

```powershell
docker build -t unity6-license-helper:6000.0.46f1 -f ci\jenkins-agent\linux\license-helper\Dockerfile ci\jenkins-agent\linux\license-helper
```

Run:

```powershell
docker run -d --name unity6-license-helper `
  -p 6080:6080 `
  --mount type=volume,source=unity6-personal-license,target=/root/.local/share/unity3d `
  --mount type=volume,source=unity6-hub-config,target=/root/.config/UnityHub `
  unity6-license-helper:6000.0.46f1
```

Open:

```text
http://localhost:6080/vnc.html?host=localhost&port=6080&autoconnect=true
```

After Unity Hub creates `/root/.local/share/unity3d/Unity/Unity_lic.ulf`, verify it with the build image:

```powershell
docker run --rm `
  --mount type=volume,source=unity6-personal-license,target=/root/.local/share/unity3d `
  unity6-jenkins-agent:6000.0.46f1 `
  activate-unity-license.sh
```
