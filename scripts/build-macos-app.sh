#!/bin/bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "此构建脚本仅支持 macOS。" >&2
  exit 1
fi
if [[ "$(uname -m)" != "arm64" ]]; then
  echo "此构建脚本仅支持 Apple Silicon arm64。" >&2
  exit 1
fi

for required_tool in dotnet sips iconutil plutil file; do
  command -v "$required_tool" >/dev/null || {
    echo "缺少构建工具：$required_tool" >&2
    exit 1
  }
done
[[ -x /usr/libexec/PlistBuddy ]] || { echo "缺少 PlistBuddy" >&2; exit 1; }

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
project_path="$repo_root/GrayscaleLayersMac/GrayscaleLayersMac.csproj"
icon_source="$repo_root/GrayscaleLayersMac/Assets/AppIcon.png"
plist_source="$repo_root/GrayscaleLayersMac/Packaging/Info.plist"
artifact_root="$repo_root/artifacts/macos-arm64"
final_app="$artifact_root/灰度图分层工具.app"
package_tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/grayscale-layers-macos.XXXXXX")"
publish_path="$package_tmp_dir/publish"
staged_app="$package_tmp_dir/灰度图分层工具.app"
macos_path="$staged_app/Contents/MacOS"
resources_path="$staged_app/Contents/Resources"
iconset_path="$package_tmp_dir/AppIcon.iconset"
previous_app="$package_tmp_dir/previous.app"

cleanup() {
  rm -rf "$package_tmp_dir"
}
trap cleanup EXIT

[[ -f "$project_path" ]] || { echo "项目文件不存在：$project_path" >&2; exit 1; }
[[ -s "$icon_source" ]] || { echo "源图标不存在：$icon_source" >&2; exit 1; }
[[ -s "$plist_source" ]] || { echo "Info.plist 不存在：$plist_source" >&2; exit 1; }

dotnet publish "$project_path" -c Release -r osx-arm64 -p:NuGetAudit=false \
  --self-contained true -o "$publish_path"

mkdir -p "$macos_path" "$resources_path/scripts" "$iconset_path"
cp -R "$publish_path/." "$macos_path/"
cp "$plist_source" "$staged_app/Contents/Info.plist"

for script_name in grayscale_layers.py texture_to_hatch_dxf.py dxf_to_machine_file.py laser_pmt.py; do
  [[ -s "$macos_path/$script_name" ]] || {
    echo "发布输出缺少 Python 脚本：$script_name" >&2
    exit 1
  }
  mv "$macos_path/$script_name" "$resources_path/scripts/$script_name"
done

for icon_size in 16 32 128 256 512; do
  double_size=$((icon_size * 2))
  sips -z "$icon_size" "$icon_size" "$icon_source" \
    --out "$iconset_path/icon_${icon_size}x${icon_size}.png" >/dev/null
  sips -z "$double_size" "$double_size" "$icon_source" \
    --out "$iconset_path/icon_${icon_size}x${icon_size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset_path" -o "$resources_path/AppIcon.icns"
chmod +x "$macos_path/GrayscaleLayersMac"

"$script_dir/verify-macos-app.sh" "$staged_app"

mkdir -p "$artifact_root"
if [[ -e "$final_app" || -L "$final_app" ]]; then
  mv "$final_app" "$previous_app"
fi
if ! mv "$staged_app" "$final_app"; then
  if [[ -e "$previous_app" || -L "$previous_app" ]]; then
    mv "$previous_app" "$final_app"
  fi
  exit 1
fi

echo "已生成：$final_app"
