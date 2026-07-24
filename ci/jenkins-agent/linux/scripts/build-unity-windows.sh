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
last_progress_percent="0"
last_progress_stage="initializing"

report_progress() {
    local event_type="${1:-stage}"
    local stage_id="${2:-}"
    local stage_name="${3:-$stage_id}"
    local percent="${4:-$last_progress_percent}"
    local state="${5:-running}"
    local result="${6:-}"
    local message="${7:-}"
    shift 7 || true

    last_progress_percent="$percent"
    last_progress_stage="$stage_name"

    report-build-progress.sh \
        --event "$event_type" \
        --stage-id "$stage_id" \
        --stage "$stage_name" \
        --percent "$percent" \
        --state "$state" \
        --result "$result" \
        --message "$message" \
        "$@" || true
}

report_failure() {
    local exit_code="${1:-1}"
    report_progress failure "$last_progress_stage" "$last_progress_stage" "$last_progress_percent" failure FAILURE \
        "Unity container build failed with exit code ${exit_code}." \
        --log-file "$unity_log" --tail-lines 40
}

on_error() {
    local exit_code=$?
    report_failure "$exit_code"
    exit "$exit_code"
}

trap on_error ERR

cd "${project_path}"

if [[ ! -x "${unity_executable}" ]]; then
    echo "Unity executable not found or not executable: ${unity_executable}" >&2
    exit 1
fi

report_progress stage agent-ready "Agent ready" 5 running "" "Unity Linux build container is ready."

report_progress stage unity-license "Activate Unity license" 12 running "" "Activating Unity license."
activate-unity-license.sh

if [[ "${RUN_GIT_LFS_PULL:-true}" != "false" && -d .git ]]; then
    report_progress stage git-lfs "Git LFS pull" 20 running "" "Pulling Git LFS content."
    git lfs install --local --force
    git lfs pull
fi

if [[ "${P4_SYNC_ENABLED:-true}" != "false" ]]; then
    report_progress stage p4-sync "Perforce asset sync" 30 running "" "Syncing Perforce assets."
    sync-perforce-assets.sh
else
    echo "Skipping Perforce asset sync because P4_SYNC_ENABLED=false."
    report_progress stage p4-sync "Perforce asset sync" 30 running "" "Perforce asset sync skipped."
fi

report_progress stage cleanup "Clean build outputs" 35 running "" "Cleaning Unity build outputs."
rm -rf ".workspace/builds" ".workspace/artifacts/yooasset-build" "Assets/StreamingAssets/yoo"
rm -f "${unity_log}"
mkdir -p "$(dirname "${unity_log}")" ".workspace/builds/windows" "$(dirname "${yooasset_plan_output}")"
windows_zip_absolute="${project_path}/${windows_zip}"

report_progress stage unity-start "Unity editor start" 40 running "" "Starting Unity batchmode player build."
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
    --yooasset-package-name DefaultPackage \
    --yooasset-package-version "${build_number}" \
    --yooasset-build-output "${yooasset_build_output}" \
    --yooasset-plan-output "${yooasset_plan_output}" &
unity_pid=$!

while kill -0 "${unity_pid}" >/dev/null 2>&1; do
    sleep 5
    if kill -0 "${unity_pid}" >/dev/null 2>&1; then
        report-build-progress.sh \
            --event heartbeat \
            --state running \
            --message "Unity batchmode process is still running." || true
    fi
done

wait "${unity_pid}"
unity_exit=$?
set -e

if [[ -f "${unity_log}" ]]; then
    cat "${unity_log}"
fi

if [[ "${unity_exit}" -ne 0 ]]; then
    report_failure "${unity_exit}"
    exit "${unity_exit}"
fi

if [[ ! -f "${windows_exe}" ]]; then
    echo "Windows player executable was not found: ${windows_exe}" >&2
    exit 1
fi

report_progress stage package-player "Package Windows player" 95 running "" "Packaging Windows player archive."
rm -f "${windows_zip}"
(
    cd "${windows_build_dir}"
    zip -qr "${windows_zip_absolute}" .
)

report-build-progress.sh \
    --event artifact \
    --stage-id package-player \
    --stage "Package Windows player" \
    --percent 98 \
    --state running \
    --message "Windows player archive created." \
    --artifact "$windows_zip_absolute" \
    --artifact-name "Unity6-Windows-Development.zip" || true

report_progress success complete "Build complete" 100 success SUCCESS "Unity container build completed successfully."
echo "Windows player archive: ${windows_zip_absolute}"
