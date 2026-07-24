#!/usr/bin/env bash
set -uo pipefail

event_type="event"
stage_id=""
stage_name=""
bundle_name=""
state="running"
result=""
message=""
duration_ms=""
total_bundles=""
completed_bundles=""
size_bytes=""
input_size_bytes=""
asset_count=""
cached=""

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
        --bundle)
            bundle_name="${2:-}"
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
        --message)
            message="${2:-}"
            shift 2
            ;;
        --duration-ms)
            duration_ms="${2:-}"
            shift 2
            ;;
        --total-bundles)
            total_bundles="${2:-}"
            shift 2
            ;;
        --completed-bundles)
            completed_bundles="${2:-}"
            shift 2
            ;;
        --size-bytes)
            size_bytes="${2:-}"
            shift 2
            ;;
        --input-size-bytes)
            input_size_bytes="${2:-}"
            shift 2
            ;;
        --asset-count)
            asset_count="${2:-}"
            shift 2
            ;;
        --cached)
            cached="${2:-}"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done

url="${BUILD_METRICS_URL:-}"
token="${BUILD_METRICS_INGEST_TOKEN:-}"

if [[ -z "$url" || -z "$token" ]]; then
    exit 0
fi

if ! command -v curl >/dev/null 2>&1 || ! command -v jq >/dev/null 2>&1; then
    exit 0
fi

run_id="${BUILD_METRICS_RUN_ID:-${JOB_NAME:-local}-${BUILD_NUMBER:-local}}"
job_name="${BUILD_METRICS_JOB:-${JOB_NAME:-unity-linux-docker-build}}"
build_number="${BUILD_NUMBER:-}"
git_ref="${GIT_REF:-${BRANCH_NAME:-}}"
git_commit="${GIT_COMMIT:-}"

if [[ -z "$git_commit" && -d .git ]]; then
    git_commit="$(git rev-parse --short HEAD 2>/dev/null || true)"
fi

payload="$(jq -cn \
    --arg runId "$run_id" \
    --arg jobName "$job_name" \
    --arg buildNumber "$build_number" \
    --arg eventType "$event_type" \
    --arg stageId "$stage_id" \
    --arg stageName "$stage_name" \
    --arg bundleName "$bundle_name" \
    --arg state "$state" \
    --arg result "$result" \
    --arg message "$message" \
    --arg durationMs "$duration_ms" \
    --arg totalBundles "$total_bundles" \
    --arg completedBundles "$completed_bundles" \
    --arg sizeBytes "$size_bytes" \
    --arg inputSizeBytes "$input_size_bytes" \
    --arg assetCount "$asset_count" \
    --arg cached "$cached" \
    --arg gitRef "$git_ref" \
    --arg gitCommit "$git_commit" \
    --arg nodeName "${NODE_NAME:-}" \
    --arg workspace "${WORKSPACE:-${UNITY_PROJECT_PATH:-}}" \
    --arg executorNumber "${EXECUTOR_NUMBER:-}" \
    '{
        runId: $runId,
        jobName: $jobName,
        buildNumber: ($buildNumber | tonumber? // null),
        eventType: $eventType,
        stageId: $stageId,
        stageName: $stageName,
        bundleName: $bundleName,
        state: $state,
        result: $result,
        message: $message,
        durationMs: ($durationMs | tonumber? // null),
        totalBundles: ($totalBundles | tonumber? // null),
        completedBundles: ($completedBundles | tonumber? // null),
        sizeBytes: ($sizeBytes | tonumber? // null),
        inputSizeBytes: ($inputSizeBytes | tonumber? // null),
        assetCount: ($assetCount | tonumber? // null),
        cached: (if $cached == "true" then true elif $cached == "false" then false else null end),
        gitRef: $gitRef,
        gitCommit: $gitCommit,
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
