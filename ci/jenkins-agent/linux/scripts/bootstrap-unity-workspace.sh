#!/usr/bin/env bash
set -Eeuo pipefail

project_path="${UNITY_PROJECT_PATH:-${WORKSPACE:-/workspace}}"
project_path="$(realpath -m "${project_path}")"
git_remote="${GIT_REMOTE:-origin}"
git_ref="${GIT_REF:-Main}"
git_repository_url="${GIT_REPOSITORY_URL:-}"
local_branch="${GIT_LOCAL_BRANCH:-${git_ref##*/}}"

mkdir -p "${project_path}"
cd "${project_path}"

if [[ ! -d .git ]]; then
    if [[ -z "${git_repository_url}" ]]; then
        echo "GIT_REPOSITORY_URL is required when ${project_path} is not already a Git workspace." >&2
        exit 2
    fi
    git init --initial-branch="${local_branch}"
fi

if git remote get-url "${git_remote}" >/dev/null 2>&1; then
    if [[ -n "${git_repository_url}" ]]; then
        git remote set-url "${git_remote}" "${git_repository_url}"
    fi
else
    if [[ -z "${git_repository_url}" ]]; then
        echo "GIT_REPOSITORY_URL is required because remote ${git_remote} does not exist." >&2
        exit 2
    fi
    git remote add "${git_remote}" "${git_repository_url}"
fi

echo "Fetching Git ref ${git_ref} from ${git_remote}."
git fetch --prune "${git_remote}" "${git_ref}"

echo "Resetting Git-managed workspace to FETCH_HEAD."
git_without_lfs_filters=(
    -c filter.lfs.smudge=
    -c filter.lfs.process=
    -c filter.lfs.required=false
)
GIT_LFS_SKIP_SMUDGE=1 git "${git_without_lfs_filters[@]}" checkout -B "${local_branch}" FETCH_HEAD
GIT_LFS_SKIP_SMUDGE=1 git "${git_without_lfs_filters[@]}" reset --hard FETCH_HEAD

# Keep ignored Perforce assets and Unity caches; they are reconciled by p4 sync
# and reused across ephemeral containers.
git clean -fd \
    -e Library/ \
    -e .workspace/ \
    -e Logs/ \
    -e Temp/ \
    -e UserSettings/

echo "Workspace Git HEAD: $(git rev-parse HEAD)"

if [[ "${BOOTSTRAP_ONLY:-false}" == "true" ]]; then
    echo "BOOTSTRAP_ONLY=true; stopping before Perforce sync and Unity build."
    exit 0
fi

exec build-unity-windows.sh "$@"
