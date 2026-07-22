#!/usr/bin/env bash
set -Eeuo pipefail

unity_executable="${UNITY_EXECUTABLE:-/opt/unity/Editor/Unity}"
license_dir="${UNITY_LICENSE_DIR:-/root/.local/share/unity3d/Unity}"
license_file="${UNITY_LICENSE_TARGET_FILE:-${license_dir}/Unity_lic.ulf}"
activation_log="${UNITY_ACTIVATION_LOG:-/tmp/unity-activation.log}"
license_mode="${UNITY_LICENSE_MODE:-personal}"
personal_reactivate="${UNITY_PERSONAL_REACTIVATE_FROM_LICENSE:-true}"

read_secret() {
    local value="${1:-}"
    local file="${2:-}"
    if [[ -n "${value}" ]]; then
        printf '%s' "${value}"
    elif [[ -n "${file}" && -f "${file}" ]]; then
        tr -d '\r\n' < "${file}"
    fi
}

has_license_file() {
    [[ -s "${license_file}" ]] || compgen -G "${license_dir}/*.ulf" >/dev/null
}

extract_serial_from_license_file() {
    local source_file="${1:-}"
    local developer_data

    if [[ -z "${source_file}" || ! -s "${source_file}" ]]; then
        return 1
    fi

    developer_data="$(grep -o 'DeveloperData Value="[^"]*"' "${source_file}" | head -1 | sed -E 's/.*Value="([^"]*)".*/\1/' || true)"
    if [[ -z "${developer_data}" ]]; then
        return 1
    fi

    printf '%s' "${developer_data}" | base64 -d | dd bs=1 skip=4 status=none
}

randomize_machine_id_for_personal_license() {
    local serial="${1:-}"

    if [[ "${UNITY_RANDOMIZE_MACHINE_ID:-true}" != "true" || "${serial}" != F* ]]; then
        return 0
    fi

    echo "Randomizing machine ID for Unity Personal activation."
    if command -v dbus-uuidgen >/dev/null 2>&1; then
        dbus-uuidgen > /etc/machine-id
    else
        tr -d '-' < /proc/sys/kernel/random/uuid > /etc/machine-id
    fi

    mkdir -p /var/lib/dbus
    ln -sf /etc/machine-id /var/lib/dbus/machine-id
}

activate_with_serial() {
    local unity_email="${1:?}"
    local unity_password="${2:?}"
    local unity_serial="${3:?}"
    local blank_project="${UNITY_ACTIVATION_PROJECT_PATH:-/tmp/unity-license-blank-project}"
    local retry_count=0
    local delay=15
    local activation_exit=1

    mkdir -p "${blank_project}/Assets"

    randomize_machine_id_for_personal_license "${unity_serial}"
    rm -f "${license_dir}"/*.ulf

    while [[ "${retry_count}" -lt 5 ]]; do
        echo "Activating Unity license online with UNITY_EMAIL/UNITY_SERIAL."
        set +e
        "${unity_executable}" \
            -quit \
            -serial "${unity_serial}" \
            -username "${unity_email}" \
            -password "${unity_password}" \
            -projectPath "${blank_project}" \
            -logFile "${activation_log}"
        activation_exit=$?
        set -e

        if [[ "${activation_exit}" -eq 0 ]]; then
            echo "Activation successful."
            return 0
        fi

        retry_count=$((retry_count + 1))
        echo "Activation failed, attempting retry #${retry_count} in ${delay} seconds." >&2
        [[ -f "${activation_log}" ]] && tail -80 "${activation_log}" >&2
        sleep "${delay}"
        delay=$((delay * 2))
    done

    echo "Activation failed after ${retry_count} retries." >&2
    [[ -f "${activation_log}" ]] && tail -200 "${activation_log}" >&2
    return "${activation_exit}"
}

mkdir -p "${license_dir}" "$(dirname "${activation_log}")"
rm -f "${activation_log}"

if [[ ! -x "${unity_executable}" ]]; then
    echo "Unity executable not found or not executable: ${unity_executable}" >&2
    exit 1
fi

if [[ -n "${UNITY_LICENSE_B64:-}" ]]; then
    echo "Installing Unity license from UNITY_LICENSE_B64."
    printf '%s' "${UNITY_LICENSE_B64}" | base64 -d > "${license_file}"
elif [[ -n "${UNITY_LICENSE_B64_FILE:-}" && -f "${UNITY_LICENSE_B64_FILE}" ]]; then
    echo "Installing Unity license from UNITY_LICENSE_B64_FILE."
    base64 -d "${UNITY_LICENSE_B64_FILE}" > "${license_file}"
elif [[ -n "${UNITY_LICENSE:-}" ]]; then
    echo "Installing Unity license from UNITY_LICENSE."
    printf '%s' "${UNITY_LICENSE}" | tr -d '\r' > "${license_file}"
elif [[ -n "${UNITY_LICENSE_FILE:-}" && -f "${UNITY_LICENSE_FILE}" ]]; then
    echo "Installing Unity license from UNITY_LICENSE_FILE."
    tr -d '\r' < "${UNITY_LICENSE_FILE}" > "${license_file}"
fi

unity_email="$(read_secret "${UNITY_EMAIL:-}" "${UNITY_EMAIL_FILE:-}")"
unity_password="$(read_secret "${UNITY_PASSWORD:-}" "${UNITY_PASSWORD_FILE:-}")"
unity_serial="$(read_secret "${UNITY_SERIAL:-}" "${UNITY_SERIAL_FILE:-}")"

if [[ "${license_mode}" == "personal" && "${personal_reactivate}" == "true" && -z "${unity_serial}" && -s "${license_file}" ]]; then
    if unity_serial="$(extract_serial_from_license_file "${license_file}")"; then
        echo "Extracted Unity Personal serial from license file."
    elif [[ -n "${unity_email}" && -n "${unity_password}" ]]; then
        echo "Could not extract Unity Personal serial from ${license_file}." >&2
        echo "The license file must contain a DeveloperData value when using GameCI-style Personal reactivation." >&2
        exit 2
    else
        echo "Could not extract Unity Personal serial from ${license_file}; verifying the provided license file as-is." >&2
    fi
fi

if [[ -n "${unity_email}" && -n "${unity_password}" && -n "${unity_serial}" ]]; then
    if ! activate_with_serial "${unity_email}" "${unity_password}" "${unity_serial}"; then
        unset unity_password unity_serial
        exit 1
    fi
    unset unity_password unity_serial
elif ! has_license_file; then
    if [[ "${license_mode}" == "personal" && ( -n "${unity_email}" || -n "${unity_password}" ) ]]; then
        echo "UNITY_LICENSE_MODE=personal needs both UNITY_EMAIL/UNITY_PASSWORD and either UNITY_SERIAL or a Unity_lic.ulf containing DeveloperData." >&2
        exit 2
    fi
fi

if ! has_license_file; then
    if [[ "${UNITY_CREATE_ALF:-false}" == "true" && "${license_mode}" != "personal" ]]; then
        alf_output_dir="${UNITY_ALF_OUTPUT_DIR:-${UNITY_PROJECT_PATH:-${WORKSPACE:-$(pwd)}}}"
        mkdir -p "${alf_output_dir}"
        echo "Creating Unity manual activation request in ${alf_output_dir}."
        (
            cd "${alf_output_dir}"
            "${unity_executable}" -batchmode -nographics -quit -createManualActivationFile -logFile "${activation_log}" || true
        )
        find "${alf_output_dir}" -maxdepth 1 -type f -name '*.alf' -print
    elif [[ "${UNITY_CREATE_ALF:-false}" == "true" && "${license_mode}" == "personal" ]]; then
        echo "Skipping .alf generation because Unity Personal manual activation is not supported by current Unity licensing." >&2
    fi

    echo "No Unity license is available in the Linux container." >&2
    if [[ "${license_mode}" == "personal" ]]; then
        echo "For Unity Personal, provide a Unity Hub-generated Unity_lic.ulf through UNITY_LICENSE_B64, UNITY_LICENSE, or UNITY_LICENSE_FILE." >&2
    else
        echo "Provide one of: UNITY_LICENSE_B64, UNITY_LICENSE, UNITY_LICENSE_FILE, or UNITY_EMAIL + UNITY_PASSWORD + UNITY_SERIAL." >&2
    fi
    exit 2
fi

if [[ "${UNITY_VERIFY_LICENSE:-true}" == "false" ]]; then
    echo "Unity license installed; verification skipped because UNITY_VERIFY_LICENSE=false."
    exit 0
fi

echo "Verifying Unity license."
set +e
"${unity_executable}" -batchmode -nographics -quit -logFile "${activation_log}"
verify_exit=$?
set -e

if [[ "${verify_exit}" -ne 0 ]]; then
    [[ -f "${activation_log}" ]] && tail -200 "${activation_log}"
    exit "${verify_exit}"
fi

echo "Unity license verified."
