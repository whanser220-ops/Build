# syntax=docker/dockerfile:1

ARG UNITY_IMAGE=unityci/editor:ubuntu-6000.0.46f1-windows-mono-3
FROM ${UNITY_IMAGE}

ARG P4_VERSION=r25.1
ARG P4_URL=https://cdist2.perforce.com/perforce/${P4_VERSION}/bin.linux26x86_64/p4
ARG JENKINS_AGENT_WORKDIR=/home/jenkins/agent
ARG UBUNTU_APT_MIRROR=https://mirrors.tuna.tsinghua.edu.cn/ubuntu

USER root
SHELL ["/bin/bash", "-euxo", "pipefail", "-c"]

ENV DEBIAN_FRONTEND=noninteractive \
    JENKINS_AGENT_WORKDIR=${JENKINS_AGENT_WORKDIR} \
    UNITY_EXECUTABLE=/opt/unity/Editor/Unity \
    JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64

RUN printf 'deb %s jammy main universe\n\
deb %s jammy-updates main universe\n' "${UBUNTU_APT_MIRROR}" "${UBUNTU_APT_MIRROR}" > /etc/apt/sources.list \
    && apt-get -o Acquire::Retries=5 update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        git-lfs \
        jq \
        openjdk-21-jre-headless \
        openssh-client \
        tini \
        unzip \
        zip \
    && git lfs install --system \
    && java -version \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL "${P4_URL}" -o /usr/local/bin/p4 \
    && chmod 0755 /usr/local/bin/p4 \
    && p4 -V | head -20

RUN mkdir -p "${JENKINS_AGENT_WORKDIR}" /opt/jenkins \
    && chmod 0775 "${JENKINS_AGENT_WORKDIR}" /opt/jenkins

COPY scripts/*.sh /usr/local/bin/
RUN chmod 0755 /usr/local/bin/jenkins-agent-entrypoint.sh \
    /usr/local/bin/activate-unity-license.sh \
    /usr/local/bin/bootstrap-unity-workspace.sh \
    /usr/local/bin/build-unity-windows.sh \
    /usr/local/bin/sync-perforce-assets.sh

WORKDIR ${JENKINS_AGENT_WORKDIR}

ENTRYPOINT ["tini", "--", "/usr/local/bin/jenkins-agent-entrypoint.sh"]
