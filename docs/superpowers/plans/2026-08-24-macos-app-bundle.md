# macOS Terminal-Free App Bundle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an Apple Silicon `灰度图分层工具.app` that launches from Finder without a Terminal window while preserving `dotnet run` as the terminal-based development workflow.

**Architecture:** Add one testable C# layout resolver so development builds load scripts beside the app while a macOS bundle loads them from `Contents/Resources/scripts`. Package the existing self-contained `osx-arm64` publish output with a checked-in `Info.plist`, generated `.icns`, and reusable bundle verifier, then publish only a fully validated app under `artifacts/macos-arm64`.

**Tech Stack:** .NET 10, Avalonia 11.3.18, MSTest 4.3.3, Bash 3.2-compatible shell, macOS `sips`, `iconutil`, `plutil`, `PlistBuddy`, and `file`.

## Global Constraints

- Target only Apple Silicon macOS with RID `osx-arm64`; reject non-macOS and non-`arm64` hosts.
- Publish self-contained; the destination Mac must not require a separately installed .NET runtime.
- Keep `dotnet run` unchanged as the development workflow and accept that it uses the current terminal.
- Put the main executable and .NET runtime files under `Contents/MacOS`; put Python scripts and `AppIcon.icns` under `Contents/Resources`.
- Do not bundle Python, NumPy, or Pillow and do not change any image, DXF, or machine-file algorithm.
- Build `灰度图分层工具.app` at `artifacts/macos-arm64/灰度图分层工具.app` only after all bundle validation succeeds.
- Use bundle identifier `com.grayscalelayers.preprocess`, version `1.0.0`/build `1`, package type `APPL`, and minimum macOS version `12.0`.
- Do not implement DMG, PKG, auto-update, universal binaries, Developer ID signing, or notarization.
- Preserve all unrelated user files and existing untracked workspace content.

## File Structure

- Create `GrayscaleLayersMac/ApplicationLayout.cs`: resolve the Python scripts directory from either a standard `.app` bundle or the current development/publish base directory.
- Create `GrayscaleLayersMac/Properties/AssemblyInfo.cs`: expose internal layout logic only to the test assembly.
- Create `GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj`: minimal MSTest project referencing the GUI project.
- Create `GrayscaleLayersMac.Tests/ApplicationLayoutTests.cs`: unit tests for bundle and development layouts.
- Modify `GrayscaleLayersMac/MainWindow.cs`: replace three direct `AppContext.BaseDirectory` script lookups with `ApplicationLayout`.
- Create `GrayscaleLayersMac/Packaging/Info.plist`: checked-in Launch Services metadata for the app bundle.
- Create `scripts/verify-macos-app.sh`: validate bundle identity, files, architecture, icon, and script placement.
- Create `scripts/test-macos-app-bundle.sh`: acceptance test that builds and verifies the final app.
- Create `scripts/build-macos-app.sh`: self-contained publish, icon generation, staging, validation, and safe artifact replacement.
- Modify `.gitignore`: ignore generated `artifacts/`.
- Modify `GrayscaleLayersMac/README.md`: distinguish terminal-based development from Finder-based daily use.

---

### Task 1: Testable Python Script Layout Resolution

**Files:**
- Create: `GrayscaleLayersMac/ApplicationLayout.cs`
- Create: `GrayscaleLayersMac/Properties/AssemblyInfo.cs`
- Create: `GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj`
- Create: `GrayscaleLayersMac.Tests/ApplicationLayoutTests.cs`
- Modify: `GrayscaleLayersMac/MainWindow.cs:1026-1034`
- Modify: `GrayscaleLayersMac/MainWindow.cs:1556-1560`
- Modify: `GrayscaleLayersMac/MainWindow.cs:1788-1792`

**Interfaces:**
- Consumes: `AppContext.BaseDirectory` and the three fixed Python script filenames.
- Produces: `internal static string ApplicationLayout.GetScriptsDirectory(string baseDirectory)` and `internal static string ApplicationLayout.GetScriptPath(string baseDirectory, string scriptName)`.

- [ ] **Step 1: Create the test project and write failing layout tests**

Create `GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="4.3.3" />
    <PackageReference Include="MSTest.TestFramework" Version="4.3.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../GrayscaleLayersMac/GrayscaleLayersMac.csproj" />
  </ItemGroup>
</Project>
```

Create `GrayscaleLayersMac.Tests/ApplicationLayoutTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class ApplicationLayoutTests
{
    [TestMethod]
    public void DevelopmentLayoutUsesBaseDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "grayscale-layout", "publish");

        var actual = ApplicationLayout.GetScriptsDirectory(baseDirectory);

        Assert.AreEqual(Path.GetFullPath(baseDirectory), actual);
    }

    [TestMethod]
    public void AppBundleUsesResourcesScriptsDirectory()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(), "灰度图分层工具.app", "Contents", "MacOS");
        var expected = Path.Combine(
            Path.GetTempPath(), "灰度图分层工具.app", "Contents", "Resources", "scripts");

        var actual = ApplicationLayout.GetScriptsDirectory(baseDirectory + Path.DirectorySeparatorChar);

        Assert.AreEqual(Path.GetFullPath(expected), actual);
    }

    [TestMethod]
    public void UnbundledDirectoryNamedMacOSDoesNotUseSiblingResources()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "plain", "Contents", "MacOS");

        var actual = ApplicationLayout.GetScriptsDirectory(baseDirectory);

        Assert.AreEqual(Path.GetFullPath(baseDirectory), actual);
    }

    [TestMethod]
    public void ScriptPathUsesResolvedDirectory()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(), "灰度图分层工具.app", "Contents", "MacOS");

        var actual = ApplicationLayout.GetScriptPath(baseDirectory, "grayscale_layers.py");

        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(
                baseDirectory, "..", "Resources", "scripts", "grayscale_layers.py")),
            actual);
    }
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj \
  --filter ApplicationLayoutTests
```

Expected: compilation fails because `ApplicationLayout` does not exist.

- [ ] **Step 3: Implement the minimal resolver and test visibility**

Create `GrayscaleLayersMac/ApplicationLayout.cs`:

```csharp
namespace GrayscaleLayersMac;

internal static class ApplicationLayout
{
    internal static string GetScriptsDirectory(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var macOsDirectory = new DirectoryInfo(normalized);
        var contentsDirectory = macOsDirectory.Parent;
        var appDirectory = contentsDirectory?.Parent;

        if (macOsDirectory.Name == "MacOS" &&
            contentsDirectory?.Name == "Contents" &&
            string.Equals(appDirectory?.Extension, ".app", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(
                normalized, "..", "Resources", "scripts"));
        }

        return normalized;
    }

    internal static string GetScriptPath(string baseDirectory, string scriptName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptName);
        return Path.Combine(GetScriptsDirectory(baseDirectory), scriptName);
    }
}
```

Create `GrayscaleLayersMac/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GrayscaleLayersMac.Tests")]
```

- [ ] **Step 4: Run the focused tests and verify they pass**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj \
  --filter ApplicationLayoutTests
```

Expected: four tests pass.

- [ ] **Step 5: Route all three UI workflows through the resolver**

In the full pipeline, replace the three direct paths with:

```csharp
var scriptsDirectory = ApplicationLayout.GetScriptsDirectory(AppContext.BaseDirectory);
var layerScript = Path.Combine(scriptsDirectory, "grayscale_layers.py");
var hatchScript = Path.Combine(scriptsDirectory, "texture_to_hatch_dxf.py");
var machineScript = Path.Combine(scriptsDirectory, "dxf_to_machine_file.py");
```

Change its missing-script message to include the resolved directory:

```csharp
await ShowMessageAsync(
    "找不到流程所需的 Python 脚本（grayscale_layers.py、texture_to_hatch_dxf.py、" +
    $"dxf_to_machine_file.py）。请重新编译或发布应用。\n脚本目录：{scriptsDirectory}");
```

In the Hatch workflow use:

```csharp
var script = ApplicationLayout.GetScriptPath(
    AppContext.BaseDirectory, "texture_to_hatch_dxf.py");
```

In the grayscale workflow use:

```csharp
var script = ApplicationLayout.GetScriptPath(
    AppContext.BaseDirectory, "grayscale_layers.py");
```

- [ ] **Step 6: Run the C# tests and build the GUI project**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
```

Expected: all tests pass and the GUI project builds with zero errors.

- [ ] **Step 7: Commit the resolver change**

```bash
git add GrayscaleLayersMac/ApplicationLayout.cs \
  GrayscaleLayersMac/Properties/AssemblyInfo.cs \
  GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj \
  GrayscaleLayersMac.Tests/ApplicationLayoutTests.cs \
  GrayscaleLayersMac/MainWindow.cs
git commit -m "feat: resolve scripts inside macOS app bundle"
```

---

### Task 2: Reproducible and Validated macOS App Packaging

**Files:**
- Create: `GrayscaleLayersMac/Packaging/Info.plist`
- Create: `scripts/verify-macos-app.sh`
- Create: `scripts/test-macos-app-bundle.sh`
- Create: `scripts/build-macos-app.sh`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: the self-contained `dotnet publish` output, `GrayscaleLayersMac/Assets/AppIcon.png`, the three published Python scripts, and `ApplicationLayout`'s `Contents/Resources/scripts` contract.
- Produces: `scripts/build-macos-app.sh` with no arguments and `scripts/verify-macos-app.sh <absolute-or-relative-app-path>`, plus the validated artifact `artifacts/macos-arm64/灰度图分层工具.app`.

- [ ] **Step 1: Add the fixed bundle metadata**

Create `GrayscaleLayersMac/Packaging/Info.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key>
  <string>灰度图分层工具</string>
  <key>CFBundleExecutable</key>
  <string>GrayscaleLayersMac</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleIdentifier</key>
  <string>com.grayscalelayers.preprocess</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>灰度图分层工具</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
```

- [ ] **Step 2: Write the bundle verifier before the builder**

Create executable `scripts/verify-macos-app.sh`:

```bash
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
[[ "$($plist_buddy -c 'Print :CFBundleExecutable' "$plist_path")" == "GrayscaleLayersMac" ]]
[[ "$($plist_buddy -c 'Print :CFBundleIdentifier' "$plist_path")" == "com.grayscalelayers.preprocess" ]]
[[ "$($plist_buddy -c 'Print :CFBundlePackageType' "$plist_path")" == "APPL" ]]
[[ "$($plist_buddy -c 'Print :LSMinimumSystemVersion' "$plist_path")" == "12.0" ]]

file "$executable_path" | grep -Eq 'Mach-O 64-bit executable arm64'
echo "应用包验证通过：$app_path"
```

- [ ] **Step 3: Write and run the failing acceptance test**

Create executable `scripts/test-macos-app-bundle.sh`:

```bash
#!/bin/bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
app_path="$repo_root/artifacts/macos-arm64/灰度图分层工具.app"

"$script_dir/build-macos-app.sh"
"$script_dir/verify-macos-app.sh" "$app_path"
```

Run:

```bash
chmod +x scripts/verify-macos-app.sh scripts/test-macos-app-bundle.sh
scripts/test-macos-app-bundle.sh
```

Expected: FAIL because `scripts/build-macos-app.sh` does not exist.

- [ ] **Step 4: Implement the minimal safe app builder**

Create executable `scripts/build-macos-app.sh`:

```bash
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

dotnet publish "$project_path" -c Release -r osx-arm64 \
  --self-contained true -o "$publish_path"

mkdir -p "$macos_path" "$resources_path/scripts" "$iconset_path"
cp -R "$publish_path/." "$macos_path/"
cp "$plist_source" "$staged_app/Contents/Info.plist"

for script_name in grayscale_layers.py texture_to_hatch_dxf.py dxf_to_machine_file.py; do
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
```

Run:

```bash
chmod +x scripts/build-macos-app.sh
```

- [ ] **Step 5: Ignore generated artifacts and run the acceptance test**

Append to `.gitignore`:

```gitignore

# Packaged application artifacts
artifacts/
```

Run:

```bash
scripts/test-macos-app-bundle.sh
```

Expected: `dotnet publish` succeeds, the verifier prints `应用包验证通过`, and the builder prints the final `.app` path.

- [ ] **Step 6: Verify the bundle preserves script-placement behavior**

Run:

```bash
test -x "artifacts/macos-arm64/灰度图分层工具.app/Contents/MacOS/GrayscaleLayersMac"
test -s "artifacts/macos-arm64/灰度图分层工具.app/Contents/Resources/scripts/grayscale_layers.py"
test ! -e "artifacts/macos-arm64/灰度图分层工具.app/Contents/MacOS/grayscale_layers.py"
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --no-restore
```

Expected: every command exits zero and all C# tests pass.

- [ ] **Step 7: Commit the packaging pipeline**

```bash
git add .gitignore \
  GrayscaleLayersMac/Packaging/Info.plist \
  scripts/build-macos-app.sh \
  scripts/verify-macos-app.sh \
  scripts/test-macos-app-bundle.sh
git commit -m "feat: package terminal-free macOS app"
```

---

### Task 3: User Documentation and End-to-End Verification

**Files:**
- Modify: `GrayscaleLayersMac/README.md:50-69`

**Interfaces:**
- Consumes: `scripts/build-macos-app.sh` and `artifacts/macos-arm64/灰度图分层工具.app` from Task 2.
- Produces: documented developer and Finder launch workflows plus final test evidence.

- [ ] **Step 1: Update the run and publish documentation**

Replace the current development/publish section with:

````markdown
## 开发运行

`dotnet run` 是开发启动方式，应用会使用执行命令的终端；关闭该终端也会影响正在运行的应用。

```bash
cd GrayscaleLayersMac
dotnet run
```

运行前需要安装 Python 3 依赖：

```bash
python3 -m pip install numpy pillow
```

## 构建无终端窗口的 Apple Silicon 应用

在仓库根目录执行：

```bash
scripts/build-macos-app.sh
```

构建完成后，在 Finder 中双击：

```text
artifacts/macos-arm64/灰度图分层工具.app
```

标准 `.app` 通过 macOS Launch Services 启动，只显示图形界面，不会打开终端窗口。该构建是 .NET 自包含版本，但仍需要当前 Mac 已安装带 NumPy 和 Pillow 的 Python 3。

当前本地构建未使用 Apple Developer ID 签名或公证，仅用于本机生成和运行；从其他来源下载时，macOS 可能显示安全提示。
````

- [ ] **Step 2: Run all automated verification**

Run:

```bash
python3 -m unittest discover -s tests -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release --no-restore
scripts/test-macos-app-bundle.sh
git diff --check
```

Expected: all Python and C# tests pass, the Release build succeeds, app packaging and verification succeed, and `git diff --check` emits no errors.

- [ ] **Step 3: Launch through macOS Launch Services and record the process relationship**

Record existing app PIDs, launch a new instance, and identify only the new PID:

```bash
app_executable="$(pwd)/artifacts/macos-arm64/灰度图分层工具.app/Contents/MacOS/GrayscaleLayersMac"
before_pids="$(pgrep -f "$app_executable" || true)"
open -n "artifacts/macos-arm64/灰度图分层工具.app"
sleep 3
after_pids="$(pgrep -f "$app_executable" || true)"
new_pid=""
for candidate_pid in $after_pids; do
  case " $before_pids " in
    *" $candidate_pid "*) ;;
    *) new_pid="$candidate_pid"; break ;;
  esac
done
test -n "$new_pid"
ps -o pid=,ppid=,comm= -p "$new_pid"
```

Expected: a new `GrayscaleLayersMac` process exists and its parent is a macOS launch service rather than Terminal. Visually confirm one application window appears and no new Terminal window appears. Close that newly launched instance from the application menu; do not terminate a pre-existing user instance.

- [ ] **Step 4: Review the final diff and commit documentation**

Run:

```bash
git diff --stat
git diff -- GrayscaleLayersMac/README.md
git status --short
```

Confirm only files from this plan plus pre-existing unrelated untracked files are present, then commit:

```bash
git add GrayscaleLayersMac/README.md
git commit -m "docs: explain terminal-free macOS launch"
```

- [ ] **Step 5: Perform the completion verification gate**

Run the exact final commands again after all commits:

```bash
python3 -m unittest discover -s tests -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release --no-restore
scripts/test-macos-app-bundle.sh
git status --short
```

Expected: tests, build, and packaging pass. `git status --short` lists only the user's pre-existing unrelated untracked `.workbuddy/`, `overlay_viewer/`, and pasted PNG; generated `artifacts/` is ignored.
