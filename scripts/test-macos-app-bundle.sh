#!/bin/bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
app_path="$repo_root/artifacts/macos-arm64/灰度图分层工具.app"

"$script_dir/build-macos-app.sh"
"$script_dir/verify-macos-app.sh" "$app_path"
