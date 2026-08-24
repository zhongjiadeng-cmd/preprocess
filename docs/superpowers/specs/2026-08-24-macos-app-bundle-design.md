# macOS 无终端窗口应用包设计

**日期：** 2026-08-24  
**状态：** 已批准方案，待用户审阅书面规格  
**目标平台：** Apple Silicon macOS（`osx-arm64`）

## 1. 背景与根因

当前 Avalonia 项目是图形界面程序，`GrayscaleLayersMac.csproj` 已使用 `WinExe`，所有 Python 子进程也已设置 `UseShellExecute=false`、重定向标准输出和错误，并启用 `CreateNoWindow=true`。因此终端窗口不是业务流程主动创建的。

现在的 macOS 发布物是一个裸 Unix 可执行文件及其依赖目录，没有标准 `.app` 目录、`Contents/Info.plist` 和 Launch Services 元数据。Finder 双击裸可执行文件时会通过终端运行；`dotnet run` 本身也是终端开发命令，所以执行期间终端必然存在。

## 2. 目标与非目标

### 2.1 目标

- 生成可在 Finder 中双击启动的 `灰度图分层工具.app`。
- 从 Finder 启动时只显示 Avalonia 图形界面，不打开或依附终端窗口。
- 使用 .NET 自包含的 Apple Silicon 发布物，不要求目标 Mac 安装 .NET。
- 保留现有三个 Python 脚本、处理算法、界面行为和日志输出方式。
- 采用符合 macOS 约定的应用包结构，将主程序放在 `Contents/MacOS`，脚本和图标放在 `Contents/Resources`。
- 提供一条可重复执行的本地构建命令，并自动验证应用包结构。

### 2.2 非目标

- 不改变 `dotnet run`：它继续用于开发，并继续依赖执行它的终端。
- 本次不制作 DMG、PKG、自动更新或通用二进制。
- 本次不申请 Apple Developer ID，不做公证；发布物面向当前 Mac 本地使用。
- 不内置 Python、NumPy 或 Pillow；继续使用当前机器已安装且能通过现有自检的 Python 3。
- 不修改图像、DXF 或机器文件算法。

## 3. 选定方案

增加一个仓库内的 macOS 打包脚本。脚本先执行：

```bash
package_tmp_dir="$(mktemp -d)"
dotnet publish GrayscaleLayersMac/GrayscaleLayersMac.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -o "$package_tmp_dir/publish"
```

随后只在脚本拥有的临时目录中组装标准应用包，通过校验后发布到：

```text
artifacts/macos-arm64/灰度图分层工具.app
```

Apple 的应用包约定要求 `Info.plist` 位于 `Contents`，主可执行代码位于 `Contents/MacOS`，非代码资源位于 `Contents/Resources`。本设计遵循该布局，不把 Python 脚本伪装成主程序代码资源。

## 4. 应用包结构

```text
灰度图分层工具.app/
└── Contents/
    ├── Info.plist
    ├── MacOS/
    │   ├── GrayscaleLayersMac
    │   ├── GrayscaleLayersMac.dll
    │   ├── *.dll
    │   ├── *.dylib
    │   ├── *.deps.json
    │   └── *.runtimeconfig.json
    └── Resources/
        ├── AppIcon.icns
        └── scripts/
            ├── grayscale_layers.py
            ├── texture_to_hatch_dxf.py
            └── dxf_to_machine_file.py
```

`Info.plist` 至少包含：

- `CFBundleExecutable=GrayscaleLayersMac`
- `CFBundleIdentifier=com.grayscalelayers.preprocess`
- `CFBundleName` 和 `CFBundleDisplayName=灰度图分层工具`
- `CFBundlePackageType=APPL`
- `CFBundleShortVersionString=1.0.0`
- `CFBundleVersion=1`
- `CFBundleIconFile=AppIcon`
- `LSMinimumSystemVersion=12.0`
- `NSHighResolutionCapable=true`

现有 1024×1024 PNG 通过 macOS 自带的 `sips` 和 `iconutil` 生成完整的 `.icns`，Finder 和 Dock 使用同一个应用图标。

## 5. 运行时资源定位

新增一个小型、可单独测试的应用布局组件，统一解析 Python 脚本目录：

1. 如果 `AppContext.BaseDirectory` 位于标准 `.app/Contents/MacOS/` 中，则使用相邻的 `../Resources/scripts/`。
2. 普通 `dotnet run`、测试和裸发布模式继续使用 `AppContext.BaseDirectory`，保持现有开发体验。
3. 三个入口页全部调用同一个解析组件，删除散落的 `Path.Combine(AppContext.BaseDirectory, "*.py")`。
4. 缺少脚本时继续在界面显示明确错误，错误中列出实际检查的目录。

Python 解释器发现顺序不在本次修改范围内。Finder 启动的环境变量通常少于终端环境，但现有实现会优先检查 `/opt/homebrew/bin/python3`、`/usr/local/bin/python3` 和 `/usr/bin/python3`，因此不依赖用户终端的 `PATH` 才能找到常见安装位置。

## 6. 构建脚本与安全边界

新增 `scripts/build-macos-app.sh`，行为如下：

1. 仅支持在 macOS 上执行，并验证当前架构为 `arm64`。
2. 检查 `dotnet`、`sips`、`iconutil` 和源图标是否存在。
3. 使用 `mktemp -d` 创建脚本专属暂存目录，并注册退出清理。
4. 将 `dotnet publish` 输出复制到暂存应用包的 `Contents/MacOS`。
5. 从暂存的 `Contents/MacOS` 删除由现有项目发布规则复制出的三个 Python 脚本，再将脚本复制到 `Contents/Resources/scripts`，避免包内出现两份脚本。
6. 生成 `.icns` 和 `Info.plist`。
7. 保证主可执行文件有执行权限；Python 脚本只需要普通可读权限。
8. 完成结构、属性列表和启动依赖校验后，才替换脚本自己管理的最终 artifact。
9. 不删除源代码、用户输入或用户生成的处理结果。

如果最终 `.app` 已存在，脚本只允许替换 `artifacts/macos-arm64/灰度图分层工具.app` 这个明确的构建产物；不接受任意删除目标参数。

## 7. 测试与验证

### 7.1 自动测试

- 为应用布局组件增加测试，覆盖 `.app` 布局和普通开发布局。
- 运行现有 Python 测试，证明算法行为没有变化。
- 运行 `dotnet build` 和新增的 C# 测试。
- 执行打包脚本后检查三个 Python 脚本、主程序、运行时文件和图标均存在。
- 使用 `plutil -lint` 验证 `Info.plist`，并检查关键键值。
- 使用 `file` 验证主程序为 Apple Silicon Mach-O 可执行文件。

### 7.2 启动烟雾测试

通过 macOS Launch Services 执行：

```bash
open "artifacts/macos-arm64/灰度图分层工具.app"
```

确认应用进程启动并显示主窗口，同时没有启动新的 Terminal 进程或窗口。烟雾测试完成后只终止本次测试启动的应用实例。

### 7.3 人工验收

- 在 Finder 双击 `.app`，只出现应用窗口。
- Dock 和 Finder 显示正确图标及中文名称。
- 分别运行灰度分层、纹理转 Hatch DXF 和完整三步流程，确认三个脚本均可找到。
- 点击打开输出目录仍由 Finder 打开，不出现终端。
- 关闭应用后无残留主程序或 Python 子进程。

## 8. 错误处理

- 非 macOS 或非 `arm64`：构建脚本立即停止，并说明当前平台不受此构建入口支持。
- 缺少构建工具、源脚本或图标：不发布不完整 `.app`。
- `dotnet publish`、图标生成或属性列表校验失败：保留原有已验证 artifact，不以半成品覆盖。
- 应用运行时缺少 Python 或依赖：沿用现有界面错误提示；这不应导致终端窗口出现。
- 应用运行时缺少包内脚本：显示包不完整及实际资源路径，不回退到仓库外的任意脚本。

## 9. 文档更新

更新 `GrayscaleLayersMac/README.md`：

- 将 `dotnet run` 明确标注为开发启动方式，会使用当前终端。
- 增加 `.app` 构建命令、产物路径和 Finder 双击说明。
- 说明首版未做 Developer ID 签名和公证，仅用于本地构建与运行。

## 10. 验收标准

只有同时满足以下条件才算完成：

- 构建命令成功生成 `artifacts/macos-arm64/灰度图分层工具.app`。
- 应用包通过结构、`Info.plist`、Mach-O 架构和资源完整性检查。
- Finder/Launch Services 启动时不出现终端窗口。
- 三个功能入口能从 `Contents/Resources/scripts` 找到对应脚本。
- 现有 Python 测试、新增布局测试和 .NET 构建全部通过。
- README 清楚区分开发运行与日常 `.app` 启动。
