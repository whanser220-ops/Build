#!/usr/bin/env bash
set -Eeuo pipefail

project_path="${UNITY_PROJECT_PATH:-${WORKSPACE:-$(pwd)}}"
project_path="$(realpath "${project_path}")"
unity_executable="${UNITY_EXECUTABLE:-/opt/unity/Editor/Unity}"
build_number="${BUILD_NUMBER:-local}"

unity_log="${UNITY_LOG:-Logs/build-windows.log}"
windows_build_dir="${WINDOWS_BUILD_DIR:-.workspace/builds/windows/Unity6-Windows-Development}"
windows_exe="${WINDOWS_EXE:-.workspace/builds/windows/Unity6-Windows-Development/Unity6.exe}"
windows_zip="${WINDOWS_ZIP:-.workspace/builds/windows/Unity6-Windows-Development.zip}"
yooasset_build_output="${YOOASSET_BUILD_OUTPUT:-${project_path}/.workspace/artifacts/yooasset-build}"
yooasset_plan_output="${YOOASSET_PLAN_OUTPUT:-${project_path}/.workspace/artifacts/yooasset/StandaloneWindows64/angrymesh/yooasset_build_plan.json}"
bundle_report_output="${BUNDLE_REPORT_OUTPUT:-${project_path}/.workspace/artifacts/bundle-report/StandaloneWindows64/DefaultPackage/${build_number}/bundle_report.json}"

run_started_ms=""
active_stage_id=""
active_stage_name=""
active_stage_started_ms=""

now_ms() {
    date +%s%3N
}

duration_since_ms() {
    local started="${1:-}"
    if [[ -z "$started" ]]; then
        echo ""
        return
    fi

    echo "$(( $(now_ms) - started ))"
}

metric_event() {
    report-build-metrics.sh "$@" || true
}

stage_start() {
    active_stage_id="${1:-}"
    active_stage_name="${2:-$active_stage_id}"
    active_stage_started_ms="$(now_ms)"

    metric_event \
        --event stage_started \
        --stage-id "$active_stage_id" \
        --stage "$active_stage_name" \
        --state running \
        --message "${3:-}"
}

stage_finish() {
    local state="${1:-success}"
    local result="${2:-SUCCESS}"
    local message="${3:-}"
    local event_type="stage_finished"
    local duration_ms

    if [[ -z "$active_stage_id" ]]; then
        return
    fi

    if [[ "$state" == "failure" ]]; then
        event_type="stage_failed"
    fi

    duration_ms="$(duration_since_ms "$active_stage_started_ms")"
    metric_event \
        --event "$event_type" \
        --stage-id "$active_stage_id" \
        --stage "$active_stage_name" \
        --state "$state" \
        --result "$result" \
        --duration-ms "$duration_ms" \
        --message "$message"

    active_stage_id=""
    active_stage_name=""
    active_stage_started_ms=""
}

finish_run() {
    local state="${1:-success}"
    local result="${2:-SUCCESS}"
    local message="${3:-}"
    local duration_ms

    duration_ms="$(duration_since_ms "$run_started_ms")"
    metric_event \
        --event run_finished \
        --state "$state" \
        --result "$result" \
        --duration-ms "$duration_ms" \
        --message "$message"
}

on_error() {
    local exit_code=$?
    stage_finish failure FAILURE "Unity container build failed with exit code ${exit_code}."
    finish_run failure FAILURE "Unity container build failed with exit code ${exit_code}."
    exit "$exit_code"
}

trap on_error ERR

cd "${project_path}"

run_started_ms="$(now_ms)"
metric_event \
    --event run_started \
    --state running \
    --message "Unity Linux build container run started."

stage_start agent-ready "Agent ready" "Validating Unity Linux build container."
if [[ ! -x "${unity_executable}" ]]; then
    echo "Unity executable not found or not executable: ${unity_executable}" >&2
    exit 1
fi
stage_finish success SUCCESS "Unity Linux build container is ready."

stage_start unity-license "Activate Unity license" "Activating Unity license."
activate-unity-license.sh
stage_finish success SUCCESS "Unity license activation completed."

if [[ "${RUN_GIT_LFS_PULL:-true}" != "false" && -d .git ]]; then
    stage_start git-lfs "Git LFS pull" "Pulling Git LFS content."
    git lfs install --local --force
    git lfs pull
    stage_finish success SUCCESS "Git LFS content pulled."
fi

if [[ "${P4_SYNC_ENABLED:-true}" != "false" ]]; then
    stage_start p4-sync "Perforce asset sync" "Syncing Perforce assets."
    sync-perforce-assets.sh
    stage_finish success SUCCESS "Perforce asset sync completed."
else
    echo "Skipping Perforce asset sync because P4_SYNC_ENABLED=false."
    stage_start p4-sync "Perforce asset sync" "Perforce asset sync skipped."
    stage_finish success SUCCESS "Perforce asset sync skipped."
fi

stage_start cleanup "Clean build outputs" "Cleaning Unity build outputs."
rm -rf ".workspace/builds" ".workspace/artifacts/yooasset-build" ".workspace/artifacts/bundle-report" "Assets/StreamingAssets/yoo"
rm -f "${unity_log}"
mkdir -p "$(dirname "${unity_log}")" ".workspace/builds/windows" "$(dirname "${yooasset_plan_output}")" "$(dirname "${bundle_report_output}")"
windows_zip_absolute="${project_path}/${windows_zip}"
stage_finish success SUCCESS "Unity build outputs cleaned."

stage_start unity-process "Unity editor build" "Starting Unity batchmode player build."
set +e
"${unity_executable}" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "${project_path}" \
    -executeMethod Unity6.Ci.CiPlayerBuild.BuildWindowsDevelopment \
    -logFile "${unity_log}" \
    --ci-output "${windows_exe}" \
    --yooasset-target StandaloneWindows64 \
    --yooasset-exclude-source-assets \
    --yooasset-force-refresh-assets \
    --yooasset-clear-build-cache \
    --yooasset-package-name DefaultPackage \
    --yooasset-package-version "${build_number}" \
    --yooasset-build-output "${yooasset_build_output}" \
    --yooasset-plan-output "${yooasset_plan_output}" \
    --bundle-report-output "${bundle_report_output}" &
unity_pid=$!

while kill -0 "${unity_pid}" >/dev/null 2>&1; do
    sleep 15
    if kill -0 "${unity_pid}" >/dev/null 2>&1; then
        metric_event \
            --event stage_heartbeat \
            --stage-id "$active_stage_id" \
            --stage "$active_stage_name" \
            --state running \
            --message "Unity batchmode process is still running."
    fi
done

wait "${unity_pid}"
unity_exit=$?
set -e

if [[ -f "${unity_log}" ]]; then
    cat "${unity_log}"
fi

if [[ "${unity_exit}" -ne 0 ]]; then
    stage_finish failure FAILURE "Unity batchmode exited with ${unity_exit}."
    finish_run failure FAILURE "Unity batchmode exited with ${unity_exit}."
    exit "${unity_exit}"
fi

stage_finish success SUCCESS "Unity batchmode player build completed."

if [[ ! -f "${windows_exe}" ]]; then
    echo "Windows player executable was not found: ${windows_exe}" >&2
    exit 1
fi

stage_start package-player "Package Windows player" "Packaging Windows player archive."
rm -f "${windows_zip}"
(
    cd "${windows_build_dir}"
    zip -qr "${windows_zip_absolute}" .
)
zip_size="$(stat -c '%s' "${windows_zip_absolute}" 2>/dev/null || echo 0)"
stage_finish success SUCCESS "Windows player archive created."

metric_event \
    --event artifact_created \
    --stage-id package-player \
    --stage "Package Windows player" \
    --state success \
    --result SUCCESS \
    --size-bytes "$zip_size" \
    --message "Windows player archive created."

finish_run success SUCCESS "Unity container build completed successfully."
echo "Windows player archive: ${windows_zip_absolute}"
