# Windows x64 便携版发布设计

**日期：** 2026-08-18
**状态：** 已批准设计，待实施
**目标版本：** v1.0.0
**目标平台：** Windows 10/11 x64

## 1. 背景

当前应用由 .NET 10、Avalonia 11.3.18 和三个 Python 脚本组成。Avalonia UI 本身支持 Windows，但现有实现仍有三处平台阻塞：C# 仅按 macOS 常见路径寻找 `python3`，打开输出目录时调用 macOS 的 `open` 命令，Python 的安全无覆盖重命名只实现了 Darwin 和 Linux 分支。

Windows 首版面向项目所有者自用。发布物采用 ZIP 便携包，解压后直接运行，不要求安装 .NET、Python、NumPy 或 Pillow，不要求管理员权限，也不依赖网络。

## 2. 目标与非目标

### 2.1 目标

- 在 Windows 10/11 x64 上提供解压即用的便携 ZIP。
- 在离线、无管理员权限环境中运行完整三步流程。
- 保持现有 Python 算法为唯一算法实现，避免 Windows 与 macOS 产生两套逻辑。
- 支持应用路径、输入路径和输出路径包含空格、中文及常见 Unicode 字符。
- 保持现有“不覆盖已有输出”和“只清理本次任务临时文件”的安全属性。
- 由一条可复现的 PowerShell 命令生成发布目录、ZIP 和 SHA-256 文件。
- 在 GitHub Actions 的 Windows Runner 上自动执行测试和构建发布物。

### 2.2 非目标

- 不制作 `Setup.exe`、MSI 或 MSIX 安装包。
- 不实现自动更新。
- 不做 Authenticode 代码签名。
- 不支持 Windows 7、Windows 8、32 位 Windows 或 Windows ARM64。
- 不将 Python 算法重写为 C#，不改变算法输出格式。
- 不在首版重命名现有 C# 项目目录或命名空间。

## 3. 选定方案

使用 .NET 自包含发布加内置 CPython 的组合：

- UI：`.NET 10`、`Avalonia 11.3.18`、RID `win-x64`、`self-contained=true`。
- Python：CPython `3.13.14` Windows 64 位嵌入式包。
- Python 依赖：NumPy `2.5.2`、Pillow `12.3.0` 的 CPython 3.13 Windows x86-64 wheels。
- Python 官方包地址：`https://www.python.org/ftp/python/3.13.14/python-3.13.14-embed-amd64.zip`。
- CPython ZIP SHA-256：`90b4e5b9898b72d744650524bff92377c367f44bd5fbd09e3148656c080ad907`。
- Python 包使用带 SHA-256 的锁定文件安装；构建命令必须启用 `--require-hashes`，不得在发布时解析未锁定的最新版。

选择 CPython 3.13 而不是重写算法或用 PyInstaller，是为了让 Windows 与 macOS 继续共用三个脚本，同时避免多个冻结 EXE 重复携带 NumPy/Pillow。

## 4. 发布包结构

```text
GrayscaleLayers-Windows-x64-v1.0.0/
├── GrayscaleLayers.exe
├── *.dll
├── Assets/
│   └── AppIcon.ico
├── scripts/
│   ├── grayscale_layers.py
│   ├── texture_to_hatch_dxf.py
│   └── dxf_to_machine_file.py
├── runtime/
│   └── python/
│       ├── python.exe
│       ├── python313.dll
│       ├── python313._pth
│       └── Lib/
│           └── site-packages/
│               ├── numpy/
│               └── PIL/
├── README-Windows.md
└── THIRD-PARTY-NOTICES.txt
```

ZIP 同级生成 `GrayscaleLayers-Windows-x64-v1.0.0.zip.sha256`。发布目录不得包含 PDB、`obj/`、测试项目、测试数据、NuGet 缓存、pip 缓存或用户生成文件。

不启用 .NET 单文件合并。Avalonia 原生库、脚本和 Python 运行时均以普通文件保留，减少首次启动解压、临时目录权限和防病毒误报风险。

## 5. 应用架构调整

### 5.1 PythonRuntimeLocator

新增独立的 `PythonRuntimeLocator`，负责发现并验证 Python：

1. 发布模式优先检查 `AppContext.BaseDirectory/runtime/python/python.exe`。
2. 包内解释器存在时必须使用包内解释器；包内解释器损坏时不静默回退到系统 Python。
3. 开发模式允许按平台回退：Windows 依次尝试 `py -3`、`python3`、`python`；macOS 保留 Homebrew、`/usr/local/bin/python3`、`/usr/bin/python3` 和 `python3`。
4. 候选解释器使用 `-c` 自检，必须成功导入 `numpy` 和 `PIL`，并输出解释器路径与版本。
5. 自检超时、退出码非零或模块缺失时返回结构化诊断信息，UI 显示可操作的错误提示。

发布包中的三个脚本统一位于 `AppContext.BaseDirectory/scripts/`。所有子进程参数继续使用 `ProcessStartInfo.ArgumentList`，禁止拼接命令行字符串。

### 5.2 PlatformPathLauncher

新增 `PlatformPathLauncher`，封装打开目录或选中文件的行为：

- Windows：以目标路径为 `FileName`，使用 `UseShellExecute=true` 交给 Windows Shell。
- macOS：同样优先使用 Shell 打开；若运行时行为不满足现有体验，保留经过测试的 `open` 回退。
- 路径不存在时返回明确失败，不启动 Shell。
- UI 的三个 `OpenDirectory` 重载统一调用该组件，移除重复的平台命令代码。

### 5.3 Windows 原子无覆盖发布

扩展 `dxf_to_machine_file.py::_rename_no_replace`：

- Windows 分支通过 `ctypes.WinDLL("kernel32", use_last_error=True)` 调用 `MoveFileExW`。
- 标志只使用 `MOVEFILE_WRITE_THROUGH`，禁止使用 `MOVEFILE_REPLACE_EXISTING`。
- 目标已存在时转换为 `FileExistsError`；其他 Win32 错误转换为带源路径、目标路径和系统错误文本的 `OSError`。
- 保留现有锁文件、所有权令牌、inode/file identity 检查和临时目录清理流程。
- Windows 测试必须证明目标存在时原内容不变，异常或取消时不会删除其他任务的锁或目录。

## 6. 数据流

1. 应用启动并按需定位内置 Python。
2. 用户通过 Avalonia StorageProvider 选择输入文件和输出目录。
3. C# 依次启动三个脚本：灰度分层、Hatch DXF、机器加工文件。
4. 标准输出实时写入 UI 日志；标准错误、退出码和运行时版本写入诊断日志。
5. 每一步只读取上一步本次运行生成的清单，现有输出目录冲突时停止，不自动删除。
6. 最终机器文件先写入带所有权锁的临时目录，校验完整后以 Windows 原子无覆盖移动发布。
7. 用户点击打开目录时由 `PlatformPathLauncher` 打开 Windows 资源管理器。

## 7. 错误处理与诊断

- 包内 `python.exe`、脚本或依赖缺失：提示“发布包不完整，请重新解压完整 ZIP”，列出缺失相对路径。
- Python 自检失败：显示 Python 版本、缺失模块、退出码和诊断日志位置。
- 文件被占用或无写入权限：显示具体路径和系统错误，不重试覆盖，不自动提权。
- 子进程失败：保留退出码与标准错误；日志不得记录用户图像内容。
- 用户取消：终止当前子进程，并继续使用所有权令牌规则决定是否清理临时机器目录。
- ZIP 未签名：`README-Windows.md` 说明发布物来源与 SHA-256 校验方式，不自动关闭或绕过 SmartScreen、Defender 或其他系统保护。

诊断日志保存在用户可写的本地应用数据目录，而不是发布目录。日志按次运行覆盖或限制保留数量，避免无限增长。

## 8. 构建与依赖供应链

新增 `scripts/build-windows-portable.ps1`，在仓库根目录执行，完成以下固定步骤：

1. 清理脚本自己的临时构建目录，不删除源目录或用户输出。
2. 执行 `dotnet publish -c Release -r win-x64 --self-contained true`。
3. 下载固定 CPython ZIP并校验上述 SHA-256。
4. 使用 `requirements-windows.lock` 下载并安装固定 wheels 到 `runtime/python/Lib/site-packages`，启用 `--only-binary=:all:` 和 `--require-hashes`。
5. 修改 `python313._pth`，仅加入应用需要的标准库 ZIP、当前目录和 `Lib/site-packages`，并启用 `import site`。
6. 复制三个 Python 脚本、Windows README、第三方许可证和 Windows 图标。
7. 删除 PDB、`__pycache__`、pip 元数据中不需要的缓存和测试目录，但保留许可证文件。
8. 运行包内 Python 自检与端到端烟雾测试。
9. 生成版本化 ZIP 和 `.sha256` 文件。

构建脚本遇到下载哈希不一致、wheel 平台不匹配、测试失败或缺少文件时立即退出非零，不生成可发布 ZIP。

## 9. CI 与发布流程

新增 GitHub Actions 工作流，在以下事件运行：

- Pull request：运行 Python 测试、C# 测试、Windows 集成测试和便携包构建，不创建 GitHub Release。
- 推送到 `main`：执行同样验证，并保留短期 Actions artifact。
- 推送形如 `v*` 的 tag：验证 tag 与项目版本一致后生成 ZIP、SHA-256 和构建清单，上传为 GitHub Release artifact。

Windows job 使用 `windows-latest`。macOS 或 Linux job继续运行现有 Python 测试，防止 Windows 修改破坏原平台行为。

首版发布步骤：

1. 合并通过 CI 的实现分支。
2. 在干净 Windows 10 和 Windows 11 x64 环境完成人工验收清单。
3. 更新版本为 `1.0.0`，提交发布说明。
4. 创建并推送 annotated tag `v1.0.0`。
5. 下载 CI 生成的 ZIP，在另一台干净 Windows 机器复验 SHA-256 和完整流程。
6. 将验证通过的 ZIP 和 SHA-256 文件作为最终自用发布物。

## 10. 测试策略

### 10.1 Python 单元测试

- 现有 53 项测试必须保持通过。
- 新增 Windows `_rename_no_replace` 成功移动测试。
- 新增目标已存在且内容不变测试。
- 新增中文与空格路径测试。
- 新增锁冲突、外来所有权令牌和取消清理测试。
- Darwin 专属 Finder flag 测试继续只在 macOS 运行。

### 10.2 C# 单元测试

新增测试项目，覆盖：

- 包内 Python 优先于系统 Python。
- 包内 Python 缺失与依赖自检失败的诊断结果。
- 发布模式不静默回退系统 Python。
- 开发模式的 Windows/macOS 候选顺序。
- Shell 打开路径的进程参数和不存在路径错误。
- 含空格和中文路径时参数保持单个 ArgumentList 项。

### 10.3 Windows 集成测试

在 CI 中使用小型固定测试图执行完整流程，校验：

- 生成预期数量的 TIFF 与 DXF。
- `machine.json`、patch 数量、NPY dtype/shape 与 Z 值正确。
- 发布目录仅包含清单允许的文件。
- 将构建目录复制到含空格和中文的路径后，包内 Python 自检和完整流程仍通过。
- 测试期间禁用系统 Python 路径或显式断言实际解释器位于 `runtime/python/python.exe`。

### 10.4 人工验收

Windows 10 和 Windows 11 各执行一次：

- 新账户、无管理员权限、未安装 .NET/Python。
- 断网后启动应用并完成完整三步流程。
- 从中文与空格目录运行应用并读写中文与空格路径。
- 验证预览、取消、再次运行、输出冲突和打开目录。
- 验证 Defender/SmartScreen 提示不会被程序绕过。
- 比较同一固定输入在 macOS 与 Windows 的结构化输出；整数和 JSON 必须一致，浮点数组使用 `rtol=1e-6`、`atol=1e-7`。

## 11. 许可证与安全

- `THIRD-PARTY-NOTICES.txt` 列出 CPython、NumPy、Pillow、Avalonia、SkiaSharp 及随包分发的其他运行时组件和许可证入口。
- 发布脚本保存 CPython ZIP 和 Python wheels 的 SHA-256 供应链清单。
- ZIP 不包含访问令牌、绝对开发路径、个人数据或用户输入文件。
- 首版不签名，因此只从项目所有者控制的 GitHub 仓库获取发布物，并使用同级 SHA-256 文件核验完整性。

## 12. 验收标准

只有同时满足以下条件才可标记 Windows v1.0.0 可用：

- Windows 10/11 x64 解压后无需安装依赖即可启动。
- 无管理员权限、断网环境可完成完整三步流程。
- 应用、输入和输出路径含空格与中文时正常工作。
- 现有 Python 测试与新增 Windows/C#测试全部通过。
- `.NET Release win-x64` 构建无错误。
- 目标已存在时不覆盖，取消时不删除外来文件。
- Windows 10/11 人工验收清单全部通过。
- macOS 回归测试通过。
- ZIP 内容清单、第三方许可证和 SHA-256 文件齐全。
- CI 生成物在干净 Windows 机器上完成最终复验。

## 13. 实施边界

实施应按可独立审查的任务拆分：跨平台运行时定位、跨平台目录启动、Windows 原子发布、便携 Python 构建、PowerShell 打包、自动测试、CI 发布、文档与人工验收。每个任务必须先增加失败测试，再实现最小修改，通过该任务相关测试后单独提交。
