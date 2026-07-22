#!/usr/bin/env bash
set -Eeuo pipefail

display="${DISPLAY:-:99}"
novnc_port="${NOVNC_PORT:-6080}"
geometry="${VNC_GEOMETRY:-1440x900x24}"

mkdir -p "${XDG_RUNTIME_DIR:-/tmp/runtime-root}" \
    /root/.config/UnityHub \
    /root/.config/google-chrome \
    /root/.local/share/applications \
    /root/.local/share/unity3d/Unity \
    /root/.cache/unity3d
chmod 0700 "${XDG_RUNTIME_DIR:-/tmp/runtime-root}"

rm -f "/tmp/.X${display#:}-lock"
cat >/root/.config/mimeapps.list <<'EOF'
[Default Applications]
x-scheme-handler/http=container-browser.desktop
x-scheme-handler/https=container-browser.desktop
x-scheme-handler/unityhub=container-browser.desktop
text/html=container-browser.desktop
EOF

Xvfb "${display}" -screen 0 "${geometry}" -ac +extension GLX +render -noreset &
xvfb_pid=$!

sleep 2
fluxbox >/tmp/fluxbox.log 2>&1 &
x11vnc -display "${display}" -forever -shared -rfbport 5900 -nopw >/tmp/x11vnc.log 2>&1 &
websockify --web=/usr/share/novnc/ "0.0.0.0:${novnc_port}" localhost:5900 >/tmp/novnc.log 2>&1 &

echo "Open this URL on the host: http://localhost:${novnc_port}/vnc.html?host=localhost&port=${novnc_port}&autoconnect=true"
echo "In Unity Hub: sign in, add/get a free Personal license, then leave this container running until Unity_lic.ulf exists."
echo "License path in container: /root/.local/share/unity3d/Unity/Unity_lic.ulf"

dbus-run-session -- unityhub --no-sandbox "$@" >/tmp/unityhub.log 2>&1 &
hub_pid=$!

trap 'kill "${hub_pid}" "${xvfb_pid}" 2>/dev/null || true' EXIT
wait "${hub_pid}"
