#!/bin/bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "用法：$0 <应用.app>" >&2
  exit 64
fi

app_path="$(cd "$(dirname "$1")" 2>/dev/null && pwd)/$(basename "$1")"
contents_path="$app_path/Contents"
macos_path="$contents_path/MacOS"
resources_path="$contents_path/Resources"
plist_path="$contents_path/Info.plist"
executable_path="$macos_path/GrayscaleLayersMac"

[[ -d "$app_path" ]] || { echo "应用包不存在：$app_path" >&2; exit 1; }
[[ -x "$executable_path" ]] || { echo "主程序缺失或不可执行：$executable_path" >&2; exit 1; }
[[ -s "$resources_path/AppIcon.icns" ]] || { echo "应用图标缺失" >&2; exit 1; }

for script_name in grayscale_layers.py texture_to_hatch_dxf.py dxf_to_machine_file.py; do
  [[ -s "$resources_path/scripts/$script_name" ]] || {
    echo "Python 脚本缺失：$script_name" >&2
    exit 1
  }
  [[ ! -e "$macos_path/$script_name" ]] || {
    echo "Python 脚本不应重复出现在 Contents/MacOS：$script_name" >&2
    exit 1
  }
done

plutil -lint "$plist_path" >/dev/null
plist_buddy=/usr/libexec/PlistBuddy
[[ "$("$plist_buddy" -c 'Print :CFBundleExecutable' "$plist_path")" == "GrayscaleLayersMac" ]]
[[ "$("$plist_buddy" -c 'Print :CFBundleIdentifier' "$plist_path")" == "com.grayscalelayers.preprocess" ]]
[[ "$("$plist_buddy" -c 'Print :CFBundlePackageType' "$plist_path")" == "APPL" ]]
[[ "$("$plist_buddy" -c 'Print :LSMinimumSystemVersion' "$plist_path")" == "12.0" ]]

file "$executable_path" | grep -Eq 'Mach-O 64-bit executable arm64'
echo "应用包验证通过：$app_path"
