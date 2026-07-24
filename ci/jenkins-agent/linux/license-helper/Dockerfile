# syntax=docker/dockerfile:1

ARG AGENT_IMAGE=unity6-jenkins-agent:6000.0.46f1
FROM ${AGENT_IMAGE}

USER root
SHELL ["/bin/bash", "-euxo", "pipefail", "-c"]

ENV DISPLAY=:99 \
    NOVNC_PORT=6080 \
    VNC_GEOMETRY=1440x900x24 \
    XDG_RUNTIME_DIR=/tmp/runtime-root \
    CHROME_FLAGS="--no-sandbox --no-first-run --no-default-browser-check --disable-dev-shm-usage" \
    BROWSER=/usr/local/bin/container-browser

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        dbus-x11 \
        fluxbox \
        fonts-noto-cjk \
        gnupg \
        libasound2 \
        libgbm1 \
        libgtk-3-0 \
        libnss3 \
        novnc \
        websockify \
        wget \
        x11vnc \
        xdg-utils \
        xvfb \
    && install -d /etc/apt/keyrings \
    && curl -fsSL https://hub.unity3d.com/linux/keys/public | gpg --dearmor -o /etc/apt/keyrings/unityhub.gpg \
    && echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/unityhub.gpg] https://hub.unity3d.com/linux/repos/deb stable main" > /etc/apt/sources.list.d/unityhub.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends unityhub \
    && curl -fsSL https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb -o /tmp/google-chrome.deb \
    && apt-get install -y --no-install-recommends /tmp/google-chrome.deb \
    && rm -f /tmp/google-chrome.deb \
    && rm -rf /var/lib/apt/lists/*

RUN mv /opt/google/chrome/google-chrome /opt/google/chrome/google-chrome.real \
    && printf '#!/usr/bin/env bash\nexec /opt/google/chrome/google-chrome.real ${CHROME_FLAGS:-} "$@"\n' > /opt/google/chrome/google-chrome \
    && chmod 0755 /opt/google/chrome/google-chrome \
    && printf '#!/usr/bin/env bash\nexec google-chrome-stable ${CHROME_FLAGS:-} "$@"\n' > /usr/local/bin/container-browser \
    && chmod 0755 /usr/local/bin/container-browser \
    && printf '%s\n' \
        '[Desktop Entry]' \
        'Name=Container Browser' \
        'Exec=/usr/local/bin/container-browser %U' \
        'Type=Application' \
        'Terminal=false' \
        'MimeType=text/html;x-scheme-handler/http;x-scheme-handler/https;x-scheme-handler/unityhub;' \
        > /usr/share/applications/container-browser.desktop \
    && update-alternatives --install /usr/bin/x-www-browser x-www-browser /usr/local/bin/container-browser 200 \
    && update-alternatives --set x-www-browser /usr/local/bin/container-browser

COPY scripts/*.sh /usr/local/bin/
RUN chmod 0755 /usr/local/bin/start-unity-hub-vnc.sh

ENTRYPOINT ["/usr/local/bin/start-unity-hub-vnc.sh"]
