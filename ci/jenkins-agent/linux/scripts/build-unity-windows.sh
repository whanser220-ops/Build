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

cd "${project_path}"

if [[ ! -x "${unity_executable}" ]]; then
    echo "Unity executable not found or not executable: ${unity_executable}" >&2
    exit 1
fi

activate-unity-license.sh

if [[ "${RUN_GIT_LFS_PULL:-true}" != "false" && -d .git ]]; then
    git lfs install --local --force
    git lfs pull
fi

if [[ "${P4_SYNC_ENABLED:-true}" != "false" ]]; then
    sync-perforce-assets.sh
else
    echo "Skipping Perforce asset sync because P4_SYNC_ENABLED=false."
fi

rm -rf ".workspace/builds" ".workspace/artifacts/yooasset-build" "Assets/StreamingAssets/yoo"
rm -f "${unity_log}"
mkdir -p "$(dirname "${unity_log}")" ".workspace/builds/windows" "$(dirname "${yooasset_plan_output}")"
windows_zip_absolute="${project_path}/${windows_zip}"

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
    --yooasset-plan-output "${yooasset_plan_output}"
unity_exit=$?
set -e

if [[ -f "${unity_log}" ]]; then
    cat "${unity_log}"
fi

if [[ "${unity_exit}" -ne 0 ]]; then
    exit "${unity_exit}"
fi

if [[ ! -f "${windows_exe}" ]]; then
    echo "Windows player executable was not found: ${windows_exe}" >&2
    exit 1
fi

rm -f "${windows_zip}"
(
    cd "${windows_build_dir}"
    zip -qr "${windows_zip_absolute}" .
)

echo "Windows player archive: ${windows_zip_absolute}"
