#!/usr/bin/env bash
set -uo pipefail

event_type="heartbeat"
stage_id=""
stage_name=""
state=""
result=""
percent=""
message=""
log_file=""
tail_lines="30"
artifact_path=""
artifact_name=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --event)
            event_type="${2:-}"
            shift 2
            ;;
        --stage-id)
            stage_id="${2:-}"
            shift 2
            ;;
        --stage)
            stage_name="${2:-}"
            shift 2
            ;;
        --state)
            state="${2:-}"
            shift 2
            ;;
        --result)
            result="${2:-}"
            shift 2
            ;;
        --percent)
            percent="${2:-}"
            shift 2
            ;;
        --message)
            message="${2:-}"
            shift 2
            ;;
        --log-file)
            log_file="${2:-}"
            shift 2
            ;;
        --tail-lines)
            tail_lines="${2:-30}"
            shift 2
            ;;
        --artifact)
            artifact_path="${2:-}"
            shift 2
            ;;
        --artifact-name)
            artifact_name="${2:-}"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done

url="${BUILD_PROGRESS_URL:-}"
token="${BUILD_PROGRESS_TOKEN:-}"

if [[ -z "$url" || -z "$token" ]]; then
    exit 0
fi

if ! command -v curl >/dev/null 2>&1 || ! command -v jq >/dev/null 2>&1; then
    exit 0
fi

run_id="${BUILD_PROGRESS_RUN_ID:-${JOB_NAME:-local}-${BUILD_NUMBER:-local}}"
job_name="${BUILD_PROGRESS_JOB:-${JOB_NAME:-unity-linux-docker-build}}"
build_number="${BUILD_NUMBER:-}"
git_ref="${GIT_REF:-${BRANCH_NAME:-}}"
git_commit="${GIT_COMMIT:-}"

if [[ -z "$git_commit" && -d .git ]]; then
    git_commit="$(git rev-parse --short HEAD 2>/dev/null || true)"
fi

log_tail=""
if [[ -n "$log_file" && -f "$log_file" ]]; then
    log_tail="$(tail -n "$tail_lines" "$log_file" 2>/dev/null || true)"
    log_tail="${log_tail: -12000}"
fi

artifact_json="[]"
if [[ -n "$artifact_path" ]]; then
    artifact_size=0
    if [[ -f "$artifact_path" ]]; then
        artifact_size="$(stat -c '%s' "$artifact_path" 2>/dev/null || echo 0)"
    fi

    if [[ -z "$artifact_name" ]]; then
        artifact_name="$(basename "$artifact_path")"
    fi

    artifact_json="$(jq -cn \
        --arg name "$artifact_name" \
        --arg path "$artifact_path" \
        --arg sizeBytes "$artifact_size" \
        '[{name: $name, path: $path, sizeBytes: ($sizeBytes | tonumber? // 0)}]')"
fi

payload="$(jq -cn \
    --arg runId "$run_id" \
    --arg jobName "$job_name" \
    --arg buildNumber "$build_number" \
    --arg eventType "$event_type" \
    --arg stageId "$stage_id" \
    --arg stageName "$stage_name" \
    --arg state "$state" \
    --arg result "$result" \
    --arg percent "$percent" \
    --arg message "$message" \
    --arg logTail "$log_tail" \
    --arg gitRef "$git_ref" \
    --arg gitCommit "$git_commit" \
    --arg nodeName "${NODE_NAME:-}" \
    --arg workspace "${WORKSPACE:-${UNITY_PROJECT_PATH:-}}" \
    --arg executorNumber "${EXECUTOR_NUMBER:-}" \
    --argjson artifacts "$artifact_json" \
    '{
        runId: $runId,
        jobName: $jobName,
        buildNumber: ($buildNumber | tonumber? // null),
        eventType: $eventType,
        stageId: $stageId,
        stageName: $stageName,
        state: $state,
        result: $result,
        percent: ($percent | tonumber? // null),
        message: $message,
        logTail: $logTail,
        gitRef: $gitRef,
        gitCommit: $gitCommit,
        artifacts: $artifacts,
        metadata: {
            nodeName: $nodeName,
            workspace: $workspace,
            executorNumber: $executorNumber
        }
    }')"

curl --connect-timeout 2 --max-time 4 --fail --silent --show-error \
    -X POST \
    -H "Authorization: Bearer ${token}" \
    -H "Content-Type: application/json" \
    --data "$payload" \
    "$url" >/dev/null 2>&1 || true
