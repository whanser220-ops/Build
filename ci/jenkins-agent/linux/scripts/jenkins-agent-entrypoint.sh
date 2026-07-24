#!/usr/bin/env bash
set -Eeuo pipefail

: "${JENKINS_AGENT_WORKDIR:=/home/jenkins/agent}"
: "${JENKINS_AGENT_JAR:=/opt/jenkins/agent.jar}"
: "${JENKINS_WEB_SOCKET:=true}"

if [[ $# -gt 0 ]]; then
    if [[ "$1" == "-url" && $# -ge 4 ]]; then
        export JENKINS_URL="${2}"
        export JENKINS_AGENT_SECRET="${3}"
        export JENKINS_AGENT_NAME="${4}"
        shift 4
    elif [[ $# -eq 2 && -z "${JENKINS_AGENT_SECRET:-${JENKINS_SECRET:-}}" && -z "${JENKINS_AGENT_NAME:-${JENKINS_NAME:-}}" ]]; then
        export JENKINS_AGENT_SECRET="${1}"
        export JENKINS_AGENT_NAME="${2}"
        shift 2
    else
        exec "$@"
    fi
fi

agent_name="${JENKINS_AGENT_NAME:-${JENKINS_NAME:-}}"
secret="${JENKINS_AGENT_SECRET:-${JENKINS_SECRET:-}}"

if [[ -z "${secret}" && -n "${JENKINS_AGENT_SECRET_FILE:-}" ]]; then
    secret="$(<"${JENKINS_AGENT_SECRET_FILE}")"
fi

if [[ -z "${JENKINS_URL:-}" ]]; then
    echo "JENKINS_URL is required." >&2
    exit 2
fi

if [[ -z "${agent_name}" ]]; then
    echo "JENKINS_AGENT_NAME is required." >&2
    exit 2
fi

if [[ -z "${secret}" ]]; then
    echo "JENKINS_AGENT_SECRET, JENKINS_SECRET, or JENKINS_AGENT_SECRET_FILE is required." >&2
    exit 2
fi

mkdir -p "${JENKINS_AGENT_WORKDIR}" "$(dirname "${JENKINS_AGENT_JAR}")"

if [[ ! -s "${JENKINS_AGENT_JAR}" || "${JENKINS_AGENT_JAR_REFRESH:-false}" == "true" ]]; then
    jar_url="${JENKINS_AGENT_JAR_URL:-${JENKINS_URL%/}/jnlpJars/agent.jar}"
    tmp_jar="${JENKINS_AGENT_JAR}.tmp"
    echo "Downloading Jenkins agent jar from ${jar_url}"
    for attempt in 1 2 3 4 5; do
        rm -f "${tmp_jar}"
        if curl --http1.1 --connect-timeout 10 --max-time 60 --retry 2 --retry-all-errors --retry-delay 2 -fsSL "${jar_url}" -o "${tmp_jar}"; then
            mv "${tmp_jar}" "${JENKINS_AGENT_JAR}"
            break
        fi
        echo "Jenkins agent jar download failed on attempt ${attempt}; retrying." >&2
        sleep 2
    done
    if [[ ! -s "${JENKINS_AGENT_JAR}" ]]; then
        echo "Failed to download Jenkins agent jar from ${jar_url}." >&2
        exit 3
    fi
fi

java_args=(
    -jar "${JENKINS_AGENT_JAR}"
    -url "${JENKINS_URL}"
    -secret "${secret}"
    -name "${agent_name}"
    -workDir "${JENKINS_AGENT_WORKDIR}"
)

if [[ "${JENKINS_WEB_SOCKET}" != "false" ]]; then
    java_args+=(-webSocket)
fi

if [[ -n "${JENKINS_AGENT_TUNNEL:-}" ]]; then
    java_args+=(-tunnel "${JENKINS_AGENT_TUNNEL}")
fi

exec java ${JAVA_OPTS:-} "${java_args[@]}"
