#!/bin/bash
# Install a copy outside the runner and checkout; point the runner's
# ACTIONS_RUNNER_HOOK_JOB_STARTED variable at that trusted local copy.
set -euo pipefail

expected_repo='Uncanny-Abyss-Interactive/TerrariaDynamicWorlds'
expected_workflow="$expected_repo/.github/workflows/deploy-tmodloader.yml@refs/heads/main"

if [[ "${GITHUB_REPOSITORY:-}" != "$expected_repo" ||
      "${GITHUB_REF:-}" != 'refs/heads/main' ||
      "${GITHUB_WORKFLOW_REF:-}" != "$expected_workflow" ]]; then
  echo 'This runner accepts only the Dynamic Worlds deployment workflow on main.' >&2
  exit 1
fi

case "${GITHUB_EVENT_NAME:-}" in
  push|workflow_dispatch) ;;
  *) echo 'This runner does not accept pull request or other events.' >&2; exit 1 ;;
esac
