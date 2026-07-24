#!/usr/bin/env bash
set -Eeuo pipefail

project_path="${UNITY_PROJECT_PATH:-${WORKSPACE:-$(pwd)}}"
project_path="$(realpath "${project_path}")"
lock_file="${P4_LOCK_FILE:-p4-assets.lock.json}"
lock_path="${project_path}/${lock_file}"

p4_port="${P4_PORT:-}"
p4_client="${P4_CLIENT_NAME:-${P4_CLIENT:-jenkins-unity-${HOSTNAME:-agent}}}"
p4_view_file="${P4_VIEW_FILE:-}"
p4_asset_cl="${P4_ASSET_CL:-}"
p4_sync_log="${P4_SYNC_LOG:-Logs/p4-sync.log}"
build_manifest="${BUILD_MANIFEST:-.workspace/build-manifest.json}"

if [[ -f "${lock_path}" ]]; then
    if [[ -z "${p4_port}" ]]; then
        p4_port="$(jq -r '.port // empty' "${lock_path}")"
    fi
    if [[ -z "${p4_view_file}" ]]; then
        p4_view_file="$(jq -r '.viewFile // empty' "${lock_path}")"
    fi
    if [[ -z "${p4_asset_cl}" ]]; then
        p4_asset_cl="$(jq -r '.changelist // empty' "${lock_path}")"
    fi
fi

if [[ -z "${P4_USERNAME:-}" ]]; then
    echo "P4_USERNAME is required." >&2
    exit 2
fi

if [[ -z "${P4_PASSWORD:-}" ]]; then
    echo "P4_PASSWORD is required." >&2
    exit 2
fi

if [[ -z "${p4_port}" ]]; then
    echo "P4_PORT is required." >&2
    exit 2
fi

if [[ -z "${p4_view_file}" ]]; then
    echo "P4_VIEW_FILE is required, or ${lock_file} must provide viewFile." >&2
    exit 2
fi

if [[ "${p4_view_file}" != /* ]]; then
    p4_view_file="${project_path}/${p4_view_file}"
fi

if [[ ! -f "${p4_view_file}" ]]; then
    echo "Perforce view file not found: ${p4_view_file}" >&2
    exit 2
fi

mkdir -p "${project_path}/$(dirname "${p4_sync_log}")" "${project_path}/$(dirname "${build_manifest}")" "${project_path}/.workspace"
log_path="${project_path}/${p4_sync_log}"
manifest_path="${project_path}/${build_manifest}"
ticket_file="${project_path}/.workspace/p4tickets.txt"
trust_file="${P4_TRUST_FILE:-${project_path}/.workspace/p4trust.txt}"
rm -f "${log_path}" "${ticket_file}"

log() {
    local timestamp
    timestamp="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    printf '[%s] %s\n' "${timestamp}" "$*" | tee -a "${log_path}"
}

run_p4() {
    log "p4 $*"
    local tmp_output exit_code
    local attempt=1
    local max_attempts="${P4_RETRY_COUNT:-5}"
    tmp_output="$(mktemp)"

    while true; do
        : > "${tmp_output}"
        set +e
        p4 "$@" 2>&1 | tee -a "${log_path}" | tee "${tmp_output}"
        exit_code=${PIPESTATUS[0]}
        set -e

        if [[ "${exit_code}" -eq 0 ]]; then
            rm -f "${tmp_output}"
            return 0
        fi

        if [[ "$1" == "sync" ]] && grep -q 'file(s) up-to-date' "${tmp_output}"; then
            rm -f "${tmp_output}"
            log "p4 sync reported files already up-to-date; treating this as success."
            return 0
        fi

        if [[ "${attempt}" -ge "${max_attempts}" ]]; then
            rm -f "${tmp_output}"
            return "${exit_code}"
        fi

        log "p4 $1 failed with exit ${exit_code}; retrying attempt $((attempt + 1))/${max_attempts}."
        sleep $((attempt * 10))
        attempt=$((attempt + 1))
    done
}

run_p4_input() {
    local input="${1:?}"
    shift

    log "p4 $*"
    local tmp_output exit_code
    local attempt=1
    local max_attempts="${P4_RETRY_COUNT:-5}"
    tmp_output="$(mktemp)"

    while true; do
        : > "${tmp_output}"
        set +e
        printf '%s\n' "${input}" | p4 "$@" 2>&1 | tee -a "${log_path}" | tee "${tmp_output}"
        exit_code=${PIPESTATUS[1]}
        set -e

        if [[ "${exit_code}" -eq 0 ]]; then
            rm -f "${tmp_output}"
            return 0
        fi

        if [[ "${attempt}" -ge "${max_attempts}" ]]; then
            rm -f "${tmp_output}"
            return "${exit_code}"
        fi

        log "p4 $1 failed with exit ${exit_code}; retrying attempt $((attempt + 1))/${max_attempts}."
        sleep $((attempt * 10))
        attempt=$((attempt + 1))
    done
}

run_p4_trust() {
    local attempt=1
    local max_attempts="${P4_RETRY_COUNT:-5}"

    while true; do
        log "p4 trust -y"
        set +e
        p4 trust -y 2>&1 | tee -a "${log_path}"
        exit_code=${PIPESTATUS[0]}
        set -e

        if [[ "${exit_code}" -eq 0 ]]; then
            return 0
        fi

        if [[ "${attempt}" -ge "${max_attempts}" ]]; then
            return "${exit_code}"
        fi

        log "p4 trust failed with exit ${exit_code}; retrying attempt $((attempt + 1))/${max_attempts}."
        sleep $((attempt * 10))
        attempt=$((attempt + 1))
    done
}

export P4PORT="${p4_port}"
export P4USER="${P4_USERNAME}"
export P4CLIENT="${p4_client}"
export P4TICKETS="${ticket_file}"
export P4TRUST="${trust_file}"

trap 'rm -f "${ticket_file}"; unset P4PASSWORD P4_PASSWORD' EXIT

log "Using p4 client: ${p4_client}"
log "Using p4 root: ${project_path}"
log "Using p4 port: ${p4_port}"
log "Using p4 trust file: ${trust_file}"

if [[ "${P4_TRUST_AUTO_ACCEPT:-true}" != "false" && "${p4_port}" == ssl:* ]]; then
    log "Trusting Perforce SSL fingerprint for ${p4_port}."
    run_p4_trust
fi

log "Logging in to Perforce."
run_p4_input "${P4_PASSWORD}" login

client_spec="$(mktemp)"
{
    echo "Client: ${p4_client}"
    echo "Owner: ${P4_USERNAME}"
    echo "Description:"
    echo -e "\tJenkins Unity asset sync workspace."
    echo "Root: ${project_path}"
    echo "Options: noallwrite clobber nocompress unlocked nomodtime normdir"
    echo "SubmitOptions: submitunchanged"
    echo "LineEnd: local"
    echo "View:"
    while IFS= read -r view_line || [[ -n "${view_line}" ]]; do
        view_line="${view_line#"${view_line%%[![:space:]]*}"}"
        view_line="${view_line%"${view_line##*[![:space:]]}"}"
        [[ -z "${view_line}" || "${view_line}" == \#* ]] && continue
        view_line="${view_line//\$\{P4_CLIENT\}/${p4_client}}"
        echo -e "\t${view_line}"
    done < "${p4_view_file}"
} > "${client_spec}"

log "Creating/updating Perforce client spec."
run_p4_input "$(cat "${client_spec}")" client -i
rm -f "${client_spec}"

pin=""
if [[ -n "${p4_asset_cl}" ]]; then
    pin="@${p4_asset_cl}"
fi

run_p4 sync --parallel=threads=8,min=100,minsize=1048576 "//${p4_client}/...${pin}"
run_p4 clean "//${p4_client}/..."

synced_change="${p4_asset_cl}"
if [[ -z "${synced_change}" ]]; then
    synced_change="$(p4 changes -m1 "//${p4_client}/..." | sed -nE 's/^Change ([0-9]+) .*/\1/p' | head -1)"
fi

git_commit=""
if git -C "${project_path}" rev-parse HEAD >/dev/null 2>&1; then
    git_commit="$(git -C "${project_path}" rev-parse HEAD)"
fi

jq -n \
    --arg build_number "${BUILD_NUMBER:-}" \
    --arg job_name "${JOB_NAME:-}" \
    --arg node_name "${NODE_NAME:-}" \
    --arg git_commit "${git_commit}" \
    --arg p4_port "${p4_port}" \
    --arg p4_user "${P4_USERNAME}" \
    --arg p4_client "${p4_client}" \
    --arg changelist "${synced_change}" \
    --arg pinned_changelist "${p4_asset_cl}" \
    --arg generated_at_utc "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" \
    '{
        build_number: $build_number,
        job_name: $job_name,
        node_name: $node_name,
        git_commit: $git_commit,
        p4: {
            port: $p4_port,
            user: $p4_user,
            client: $p4_client,
            changelist: $changelist,
            pinned_changelist: $pinned_changelist
        },
        generated_at_utc: $generated_at_utc
    }' > "${manifest_path}"

log "Wrote build manifest: ${manifest_path}"
