#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/lib-sts2.sh
source "$script_dir/lib-sts2.sh"

repo_root="$(sts2_resolve_repo_root "${REPO_ROOT:-}")"
sts2_run_validation "$repo_root" main-menu-active-run "$@"
