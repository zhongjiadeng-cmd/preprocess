# Windows x64 Portable Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Windows 10/11 x64 ZIP that runs the existing Avalonia application and all three Python processing stages offline without requiring .NET, Python, NumPy, Pillow, administrator rights, or an installer.

**Architecture:** Keep the existing Python algorithms as the single source of truth. Publish the Avalonia application self-contained for `win-x64`, place a hash-verified CPython runtime beside it, and isolate platform behavior behind testable C# services plus a Windows implementation of Python's atomic no-replace move.

**Tech Stack:** .NET 10, Avalonia 11.3.18, MSTest, CPython 3.13.14 embedded x64, NumPy 2.5.2, Pillow 12.3.0, PowerShell 7, GitHub Actions.

## Global Constraints

- Supported runtime targets are exactly Windows 10 and Windows 11 x64; Windows 7/8, x86, and ARM64 are out of scope.
- The distributable is a portable ZIP only; do not add MSI, MSIX, `Setup.exe`, auto-update, elevation, or code signing.
- Runtime versions are fixed: .NET 10, Avalonia 11.3.18, CPython 3.13.14, NumPy 2.5.2, Pillow 12.3.0.
- CPython must be downloaded from `https://www.python.org/ftp/python/3.13.14/python-3.13.14-embed-amd64.zip` and match SHA-256 `90b4e5b9898b72d744650524bff92377c367f44bd5fbd09e3148656c080ad907`.
- NumPy wheel SHA-256 is `85aaccb24182c25df891ad0ec333585967e115269d5f1b17f2c9ae005bc96657`.
- Pillow wheel SHA-256 is `1cca606cd25738df4ed873d5ad46bbdb3d83b5cbca291f6b4ff13a4df6b0bbe8`.
- Published mode must use `runtime/python/python.exe`; a missing or broken bundled runtime is fatal and must not silently fall back to system Python.
- Development mode may fall back to system Python. Existing macOS behavior must remain supported.
- All subprocess arguments must use `ProcessStartInfo.ArgumentList`; never construct a shell command string.
- Existing outputs must never be overwritten. Cancellation may delete only artifacts whose ownership token matches the current run.
- App, input, and output paths containing spaces and Chinese characters must work.
- The three existing Python algorithms and their output formats must not be rewritten.
- Each task uses TDD, passes its focused tests, passes the relevant regression suite, and ends with one focused commit.

## File Map

- `GrayscaleLayersMac/Services/PythonRuntimeLocator.cs`: discover and probe bundled/development Python runtimes.
- `GrayscaleLayersMac/Services/ApplicationLayout.cs`: resolve scripts and portable-release marker relative to the executable.
- `GrayscaleLayersMac/Services/PlatformPathLauncher.cs`: open directories through the host shell.
- `GrayscaleLayersMac/Properties/AssemblyInfo.cs`: expose internal services to the test assembly.
- `GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj`: C# test project.
- `GrayscaleLayersMac.Tests/PythonRuntimeLocatorTests.cs`: runtime candidate and fallback tests.
- `GrayscaleLayersMac.Tests/ApplicationLayoutTests.cs`: script-layout validation tests.
- `GrayscaleLayersMac.Tests/PlatformPathLauncherTests.cs`: shell-launch configuration tests.
- `dxf_to_machine_file.py`: add Windows atomic no-replace directory publication.
- `tests/test_dxf_to_machine_file.py`: platform-neutral error mapping and Windows integration tests.
- `packaging/windows/requirements-windows.lock`: hash-locked NumPy and Pillow wheels.
- `scripts/build-windows-portable.ps1`: deterministic Windows publish and ZIP pipeline.
- `tests/windows_portable_smoke.py`: exercise the packaged Python runtime and all processing stages.
- `.github/workflows/windows-portable.yml`: CI and tagged-release automation.
- `GrayscaleLayersMac/Assets/AppIcon.ico`: Windows executable icon.
- `README-Windows.md`: end-user launch, checksum, and troubleshooting instructions.
- `THIRD-PARTY-NOTICES.txt`: redistributed runtime notices.
- `packaging/licenses/Avalonia.LICENSE.md`: Avalonia 11.3.18 license text from the matching upstream tag.
- `packaging/licenses/SkiaSharp.LICENSE.txt`: SkiaSharp 2.88.9 license text from the restored NuGet package.
- `packaging/licenses/DotNetRuntime.LICENSE.txt`: .NET 10 runtime license text from the matching upstream tag.
- `docs/windows-release-checklist.md`: Windows 10/11 manual release gate.

---

### Task 1: Add C# test infrastructure and the Python runtime locator

**Files:**
- Create: `GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj`
- Create: `GrayscaleLayersMac.Tests/PythonRuntimeLocatorTests.cs`
- Create: `GrayscaleLayersMac/Properties/AssemblyInfo.cs`
- Create: `GrayscaleLayersMac/Services/PythonRuntimeLocator.cs`

**Interfaces:**
- Produces: `PythonCommand(string FileName, IReadOnlyList<string> PrefixArguments, bool IsBundled)`.
- Produces: `PythonProbeResult(bool Success, string Version, string Error)`.
- Produces: `PythonRuntime(string FileName, IReadOnlyList<string> PrefixArguments, string Version, bool IsBundled)`.
- Produces: `PythonLookupResult(PythonRuntime? Runtime, string? Error)` with `Success => Runtime is not null`.
- Produces: `IPythonProbe.ProbeAsync(PythonCommand, CancellationToken)`.
- Produces: `PythonRuntimeLocator.LocateAsync(string appBaseDirectory, bool portableRelease, CancellationToken)`.

- [ ] **Step 1: Create the MSTest project and friend-assembly declaration**

Use this exact test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.10.2" />
    <PackageReference Include="MSTest.TestFramework" Version="3.10.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../GrayscaleLayersMac/GrayscaleLayersMac.csproj" />
  </ItemGroup>
</Project>
```

`AssemblyInfo.cs` must contain:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GrayscaleLayersMac.Tests")]
```

- [ ] **Step 2: Write failing locator tests**

Use a fake `IPythonProbe` that records candidates and returns configured results. Add tests with these assertions:

```csharp
[TestMethod]
public async Task PortableReleaseUsesBundledPythonOnly()
{
    using var root = new TemporaryDirectory();
    var bundled = Path.Combine(root.Path, "runtime", "python", "python.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
    File.WriteAllBytes(bundled, []);
    var probe = new FakeProbe(successfulFileName: bundled, version: "3.13.14");

    var result = await new PythonRuntimeLocator(probe, isWindows: true, isMacOS: false)
        .LocateAsync(root.Path, portableRelease: true, CancellationToken.None);

    Assert.IsTrue(result.Success);
    Assert.AreEqual(bundled, result.Runtime!.FileName);
    Assert.IsTrue(result.Runtime.IsBundled);
    CollectionAssert.AreEqual(new[] { bundled }, probe.Seen.Select(x => x.FileName).ToArray());
}

[TestMethod]
public async Task MissingBundledPythonDoesNotFallBack()
{
    using var root = new TemporaryDirectory();
    var probe = new FakeProbe(successfulFileName: "python", version: "3.13.14");

    var result = await new PythonRuntimeLocator(probe, isWindows: true, isMacOS: false)
        .LocateAsync(root.Path, portableRelease: true, CancellationToken.None);

    Assert.IsFalse(result.Success);
    StringAssert.Contains(result.Error, Path.Combine("runtime", "python", "python.exe"));
    Assert.AreEqual(0, probe.Seen.Count);
}

[TestMethod]
public async Task WindowsDevelopmentCandidatesPreservePyPrefixArgument()
{
    using var root = new TemporaryDirectory();
    var probe = new FakeProbe(successfulFileName: "py", version: "3.13.14");

    var result = await new PythonRuntimeLocator(probe, isWindows: true, isMacOS: false)
        .LocateAsync(root.Path, portableRelease: false, CancellationToken.None);

    Assert.IsTrue(result.Success);
    CollectionAssert.AreEqual(new[] { "-3" }, result.Runtime!.PrefixArguments.ToArray());
    CollectionAssert.AreEqual(new[] { "py" }, probe.Seen.Select(x => x.FileName).ToArray());
}
```

Also test macOS candidate order exactly as `/opt/homebrew/bin/python3`, `/usr/local/bin/python3`, `/usr/bin/python3`, `python3`, and test that a failed probe includes its error in the final diagnostic.

Define both test helpers in the same file so the test task has no hidden dependency:

```csharp
private sealed class FakeProbe(string successfulFileName, string version) : IPythonProbe
{
    public List<PythonCommand> Seen { get; } = [];
    public Task<PythonProbeResult> ProbeAsync(PythonCommand command, CancellationToken cancellationToken)
    {
        Seen.Add(command);
        return Task.FromResult(command.FileName == successfulFileName
            ? new PythonProbeResult(true, version, "")
            : new PythonProbeResult(false, "", $"probe failed: {command.FileName}"));
    }
}

private sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
```

- [ ] **Step 3: Run the tests and confirm the intended failure**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter PythonRuntimeLocatorTests
```

Expected: compilation fails because `PythonRuntimeLocator` and related contracts do not exist.

- [ ] **Step 4: Implement the locator contracts and candidate policy**

Implement the records and class in namespace `GrayscaleLayersMac.Services`. Candidate selection must follow this exact shape:

```csharp
var bundled = Path.Combine(appBaseDirectory, "runtime", "python", "python.exe");
if (portableRelease)
{
    if (!File.Exists(bundled))
        return new(null, $"发布包不完整，缺少：{bundled}");
    candidates = [new PythonCommand(bundled, [], IsBundled: true)];
}
else if (_isWindows)
{
    candidates =
    [
        new PythonCommand("py", ["-3"], IsBundled: false),
        new PythonCommand("python3", [], IsBundled: false),
        new PythonCommand("python", [], IsBundled: false)
    ];
}
else if (_isMacOS)
{
    candidates =
    [
        new PythonCommand("/opt/homebrew/bin/python3", [], IsBundled: false),
        new PythonCommand("/usr/local/bin/python3", [], IsBundled: false),
        new PythonCommand("/usr/bin/python3", [], IsBundled: false),
        new PythonCommand("python3", [], IsBundled: false)
    ];
}
else
{
    candidates = [new PythonCommand("python3", [], IsBundled: false)];
}
```

Add `ProcessPythonProbe`. It must create a process with `UseShellExecute=false`, redirected stdout/stderr, `CreateNoWindow=true`, append prefix arguments first, then append:

```text
-c
import json,numpy,PIL,sys; print(json.dumps({'executable':sys.executable,'version':sys.version.split()[0]}))
```

Use a 10-second linked cancellation timeout. Return stderr in `PythonProbeResult.Error` when the exit code is nonzero.

- [ ] **Step 5: Run focused and full C# tests**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter PythonRuntimeLocatorTests
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
```

Expected: all locator tests pass and both commands exit 0.

- [ ] **Step 6: Commit Task 1**

```bash
git add GrayscaleLayersMac.Tests GrayscaleLayersMac/Properties/AssemblyInfo.cs GrayscaleLayersMac/Services/PythonRuntimeLocator.cs
git commit -m "feat: add cross-platform Python runtime locator"
```

---

### Task 2: Resolve packaged scripts and integrate the runtime into the UI

**Files:**
- Create: `GrayscaleLayersMac/Services/ApplicationLayout.cs`
- Create: `GrayscaleLayersMac.Tests/ApplicationLayoutTests.cs`
- Modify: `GrayscaleLayersMac/GrayscaleLayersMac.csproj:17-31`
- Modify: `GrayscaleLayersMac/MainWindow.cs:1055-1070,1475-1482,1603-1609,1835-1840,1920-1947`

**Interfaces:**
- Consumes: `PythonRuntimeLocator`, `PythonRuntime` from Task 1.
- Produces: `ApplicationLayout.FromBaseDirectory(string)`.
- Produces: properties `PortableRelease`, `LayerScript`, `HatchScript`, `MachineScript` and method `ValidateScripts()` returning `string?`.
- Produces: `CreatePythonProcess(PythonRuntime runtime)` in `MainWindow`.

- [ ] **Step 1: Write failing layout tests**

Cover the exact `scripts/` layout and portable marker:

```csharp
[TestMethod]
public void LayoutUsesScriptsDirectoryAndPortableMarker()
{
    using var root = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(root.Path, "portable-release.json"), "{}");
    var scripts = Path.Combine(root.Path, "scripts");
    Directory.CreateDirectory(scripts);
    foreach (var name in new[] { "grayscale_layers.py", "texture_to_hatch_dxf.py", "dxf_to_machine_file.py" })
        File.WriteAllText(Path.Combine(scripts, name), "# test");

    var layout = ApplicationLayout.FromBaseDirectory(root.Path);

    Assert.IsTrue(layout.PortableRelease);
    Assert.IsNull(layout.ValidateScripts());
    Assert.AreEqual(Path.Combine(scripts, "grayscale_layers.py"), layout.LayerScript);
}
```

Add a second test asserting `ValidateScripts()` lists the missing relative path `scripts/dxf_to_machine_file.py`.

- [ ] **Step 2: Run the layout tests and verify they fail**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter ApplicationLayoutTests
```

Expected: compilation fails because `ApplicationLayout` does not exist.

- [ ] **Step 3: Implement `ApplicationLayout`**

Use `Path.GetFullPath`, derive all paths from the supplied base directory, and define portable mode only by the presence of `portable-release.json`. `ValidateScripts()` must return `null` when all three files exist; otherwise return one Chinese error containing every missing relative path.

- [ ] **Step 4: Move script content links under `scripts/`**

Change each project content link from the repository-root filename to `scripts/<filename>`:

```xml
<Content Include="../grayscale_layers.py"
         Link="scripts/grayscale_layers.py"
         CopyToOutputDirectory="PreserveNewest"
         CopyToPublishDirectory="PreserveNewest" />
```

Apply the same structure to the hatch and machine scripts.

- [ ] **Step 5: Replace `FindPythonAsync` usage in all three UI workflows**

At each workflow, create one layout and resolve the runtime:

```csharp
var layout = ApplicationLayout.FromBaseDirectory(AppContext.BaseDirectory);
var scriptError = layout.ValidateScripts();
if (scriptError is not null)
{
    await ShowMessageAsync(scriptError);
    return;
}
var lookup = await _pythonRuntimeLocator.LocateAsync(
    AppContext.BaseDirectory,
    layout.PortableRelease,
    CancellationToken.None);
if (!lookup.Success)
{
    await ShowMessageAsync(lookup.Error!);
    return;
}
var python = lookup.Runtime!;
```

Add a `PythonRuntimeLocator` field initialized with `ProcessPythonProbe`, replace script paths with layout properties, delete `FindPythonAsync`, and change `CreatePythonProcess` to append `runtime.PrefixArguments` before callers append the script and workflow arguments.

- [ ] **Step 6: Run tests and a macOS development build**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
python3 -m unittest discover -s tests
```

Expected: C# tests pass, .NET build exits 0, and all existing Python tests pass.

- [ ] **Step 7: Commit Task 2**

```bash
git add GrayscaleLayersMac/Services/ApplicationLayout.cs GrayscaleLayersMac.Tests/ApplicationLayoutTests.cs GrayscaleLayersMac/GrayscaleLayersMac.csproj GrayscaleLayersMac/MainWindow.cs
git commit -m "feat: use packaged Python scripts and runtime"
```

---

### Task 3: Add a cross-platform directory launcher

**Files:**
- Create: `GrayscaleLayersMac/Services/PlatformPathLauncher.cs`
- Create: `GrayscaleLayersMac.Tests/PlatformPathLauncherTests.cs`
- Modify: `GrayscaleLayersMac/MainWindow.cs:2018-2046`

**Interfaces:**
- Produces: `PlatformPathLauncher.CreateStartInfo(string directory)`.
- Produces: `PlatformPathLauncher.OpenDirectory(string directory)`.

- [ ] **Step 1: Write failing launcher tests**

```csharp
[TestMethod]
public void CreateStartInfoUsesShellAndKeepsUnicodePathIntact()
{
    var path = Path.GetFullPath(Path.Combine("测试 输出", "结果"));
    var info = PlatformPathLauncher.CreateStartInfo(path);

    Assert.AreEqual(path, info.FileName);
    Assert.IsTrue(info.UseShellExecute);
    Assert.AreEqual(0, info.ArgumentList.Count);
}

[TestMethod]
public void OpenDirectoryRejectsMissingPath()
{
    Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
        PlatformPathLauncher.OpenDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
}
```

- [ ] **Step 2: Run and verify the tests fail**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter PlatformPathLauncherTests
```

Expected: compilation fails because `PlatformPathLauncher` does not exist.

- [ ] **Step 3: Implement and integrate the launcher**

`CreateStartInfo` must return exactly:

```csharp
new ProcessStartInfo
{
    FileName = Path.GetFullPath(directory),
    UseShellExecute = true
};
```

`OpenDirectory` must reject a missing directory and throw if `Process.Start` returns `null`. Replace all three `open` command implementations in `MainWindow` with this service. Preserve the current UI behavior of ignoring blank paths. Convert the three UI wrapper methods to instance `async void` methods; catch `Exception` around the launcher call and `await ShowMessageAsync($"无法打开输出目录：{ex.Message}")`.

- [ ] **Step 4: Run focused tests and build**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter PlatformPathLauncherTests
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj --no-restore
```

Expected: tests pass and build exits 0.

- [ ] **Step 5: Commit Task 3**

```bash
git add GrayscaleLayersMac/Services/PlatformPathLauncher.cs GrayscaleLayersMac.Tests/PlatformPathLauncherTests.cs GrayscaleLayersMac/MainWindow.cs
git commit -m "feat: open output folders across platforms"
```

---

### Task 4: Implement Windows atomic no-replace publication

**Files:**
- Modify: `dxf_to_machine_file.py:382-437`
- Modify: `tests/test_dxf_to_machine_file.py:246-288`

**Interfaces:**
- Produces: `_windows_move_no_replace(source: Path, destination: Path, *, move_file_ex=None, last_error=None) -> None`.
- Extends: `_rename_no_replace(source: Path, destination: Path) -> None` with `sys.platform == "win32"`.

- [ ] **Step 1: Add platform-neutral failing error-mapping tests**

```python
def test_windows_move_no_replace_maps_existing_destination(self) -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        with self.assertRaises(FileExistsError) as caught:
            machine._windows_move_no_replace(
                root / "源 source",
                root / "目标 destination",
                move_file_ex=lambda _source, _destination, _flags: 0,
                last_error=lambda: 183,
            )
        self.assertEqual(caught.exception.filename, os.fspath(root / "目标 destination"))

def test_windows_move_no_replace_uses_write_through_without_replace(self) -> None:
    calls: list[tuple[str, str, int]] = []
    machine._windows_move_no_replace(
        Path("源"),
        Path("目标"),
        move_file_ex=lambda source, destination, flags: calls.append((source, destination, flags)) or 1,
        last_error=lambda: 0,
    )
    self.assertEqual(calls, [("源", "目标", 0x00000008)])
```

- [ ] **Step 2: Run the focused tests and verify failure**

```bash
python3 -m unittest tests.test_dxf_to_machine_file.AtomicPublicationTests -v
```

Expected: new tests fail because `_windows_move_no_replace` does not exist.

- [ ] **Step 3: Implement the Win32 helper**

Use `MoveFileExW` and only `MOVEFILE_WRITE_THROUGH`:

```python
def _windows_move_no_replace(
    source: Path,
    destination: Path,
    *,
    move_file_ex=None,
    last_error=None,
) -> None:
    if move_file_ex is None:
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        move_file_ex = kernel32.MoveFileExW
        move_file_ex.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint]
        move_file_ex.restype = ctypes.c_int
    if move_file_ex(os.fspath(source), os.fspath(destination), 0x00000008):
        return
    error_number = (last_error or ctypes.get_last_error)() or errno.EIO
    message = ctypes.FormatError(error_number).strip() if sys.platform == "win32" else os.strerror(error_number)
    if error_number in {80, 183}:
        raise FileExistsError(error_number, message, os.fspath(destination))
    raise OSError(error_number, f"atomic no-replace move failed: {source} -> {destination}: {message}")
```

At the start of `_rename_no_replace`, call this helper and return when `sys.platform == "win32"`. Instantiate `ctypes.CDLL(None, use_errno=True)` only after the Windows branch so Windows never tries the Darwin/Linux path.

- [ ] **Step 4: Run Python regression tests**

```bash
python3 -m unittest discover -s tests -v
```

Expected: every test passes on the development host.

- [ ] **Step 5: Run the atomic tests on Windows**

```powershell
py -3.13 -m unittest tests.test_dxf_to_machine_file.AtomicPublicationTests -v
```

Expected: the existing real filesystem move tests and the new mapping tests pass with Unicode paths.

- [ ] **Step 6: Commit Task 4**

```bash
git add dxf_to_machine_file.py tests/test_dxf_to_machine_file.py
git commit -m "feat: publish machine files atomically on Windows"
```

---

### Task 5: Add Windows product metadata and the deterministic portable builder

**Files:**
- Create: `packaging/windows/requirements-windows.lock`
- Create: `scripts/build-windows-portable.ps1`
- Create: `GrayscaleLayersMac/Assets/AppIcon.ico`
- Modify: `GrayscaleLayersMac/GrayscaleLayersMac.csproj:2-9,17-31`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `scripts/build-windows-portable.ps1 -Version 1.0.0 -OutputRoot artifacts`.
- Produces: `artifacts/GrayscaleLayers-Windows-x64-v1.0.0.zip` and matching `.zip.sha256`.
- Consumes: runtime and layout contracts from Tasks 1-2.

- [ ] **Step 1: Add the exact dependency lock**

`requirements-windows.lock` must contain only:

```text
numpy==2.5.2 --hash=sha256:85aaccb24182c25df891ad0ec333585967e115269d5f1b17f2c9ae005bc96657
pillow==12.3.0 --hash=sha256:1cca606cd25738df4ed873d5ad46bbdb3d83b5cbca291f6b4ff13a4df6b0bbe8
```

- [ ] **Step 2: Add Windows product metadata**

Add these properties to the application project:

```xml
<AssemblyName>GrayscaleLayers</AssemblyName>
<ApplicationTitle>灰度纹理预处理工具</ApplicationTitle>
<ApplicationIcon>Assets/AppIcon.ico</ApplicationIcon>
<Version>1.0.0</Version>
<Authors>zhongjiadeng-cmd</Authors>
```

Generate `AppIcon.ico` from the existing PNG with Pillow sizes `16, 24, 32, 48, 64, 128, 256`; verify the ICO contains a 256×256 frame. Do not remove the PNG because Avalonia uses it as a runtime resource.

- [ ] **Step 3: Write the initial failing builder validation**

Start the PowerShell script with parameters and explicit prerequisite checks:

```powershell
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0',
    [string]$OutputRoot = 'artifacts'
)
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputRoot = [IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputRoot))
$PackageName = "GrayscaleLayers-Windows-x64-v$Version"
```

Before the builder exists, run the command in Step 7 and record the expected missing-script failure.

- [ ] **Step 4: Implement runtime download and hash verification**

The script must download the CPython ZIP to a build-only directory, compute `Get-FileHash -Algorithm SHA256`, compare case-insensitively with the global constraint hash, and stop before extraction on mismatch. Use `Expand-Archive` into `$PublishRoot/runtime/python`.

Install wheels from the lock with the Windows CPython 3.13 build interpreter:

```powershell
py -3.13 -m pip install `
  --disable-pip-version-check `
  --only-binary=:all: `
  --require-hashes `
  --target (Join-Path $PythonRoot 'Lib/site-packages') `
  -r (Join-Path $RepoRoot 'packaging/windows/requirements-windows.lock')
if ($LASTEXITCODE -ne 0) { throw 'Hash-locked Python dependency installation failed.' }
```

Rewrite `python313._pth` to contain these four lines exactly:

```text
python313.zip
.
Lib/site-packages
import site
```

- [ ] **Step 5: Implement .NET publish and package assembly**

Run:

```powershell
dotnet publish (Join-Path $RepoRoot 'GrayscaleLayersMac/GrayscaleLayersMac.csproj') `
  -c Release -r win-x64 --self-contained true `
  -p:Version=$Version -o $PublishRoot
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
```

Create `portable-release.json` containing `version`, `rid`, `python`, `numpy`, and `pillow`. Confirm the three scripts exist under `scripts/`. Delete `*.pdb`, `__pycache__`, and `*.pyc` only inside the builder-owned publish directory. Add `artifacts/` and `*.zip.sha256` to `.gitignore`.

- [ ] **Step 6: Generate the ZIP and checksum**

Use `Compress-Archive` on the versioned top-level directory, then create the UTF-8 checksum line with this exact PowerShell:

```powershell
$ZipHash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$ZipHash  $([IO.Path]::GetFileName($ZipPath))" | Set-Content "$ZipPath.sha256" -Encoding utf8NoBOM
```

Fail if the ZIP contains a PDB, `obj/`, `.git/`, test source, or a path outside the one versioned top-level directory.

- [ ] **Step 7: Run the builder on Windows**

```powershell
pwsh -File scripts/build-windows-portable.ps1 -Version 1.0.0 -OutputRoot artifacts
```

Expected: command exits 0 and creates the ZIP plus `.sha256`; do not claim the package releasable until Task 6 smoke tests run.

- [ ] **Step 8: Commit Task 5**

```bash
git add packaging/windows/requirements-windows.lock scripts/build-windows-portable.ps1 GrayscaleLayersMac/Assets/AppIcon.ico GrayscaleLayersMac/GrayscaleLayersMac.csproj .gitignore
git commit -m "build: create Windows portable package"
```

---

### Task 6: Add packaged-runtime and end-to-end smoke validation

**Files:**
- Create: `tests/windows_portable_smoke.py`
- Modify: `scripts/build-windows-portable.ps1`

**Interfaces:**
- Produces: `python tests/windows_portable_smoke.py artifacts/GrayscaleLayers-Windows-x64-v1.0.0` with exit code 0 only for a valid portable tree.

- [ ] **Step 1: Write the failing portable smoke test**

The script must accept exactly one publish-root argument and assert these files exist:

```python
required = [
    "GrayscaleLayers.exe",
    "portable-release.json",
    "runtime/python/python.exe",
    "scripts/grayscale_layers.py",
    "scripts/texture_to_hatch_dxf.py",
    "scripts/dxf_to_machine_file.py",
]
```

It must invoke the packaged interpreter with:

```python
probe = subprocess.run(
    [str(python), "-c", "import numpy,PIL,sys; print(sys.version.split()[0])"],
    text=True,
    capture_output=True,
    check=False,
)
assert probe.returncode == 0, probe.stderr
assert probe.stdout.strip() == "3.13.14"
```

Generate an 8×8 grayscale TIFF with 300 DPI using packaged Pillow, run `grayscale_layers.py` for two layers, run `texture_to_hatch_dxf.py` with `--width 1 --height 1 --spacing 0.1 --dpi 300 --blocks 0`, then run `dxf_to_machine_file.py` with `--layer-step-um 3`. Assert two TIFFs, two DXFs, `machine.json`, `patches/0_0.npy`, and `patches/1_0.npy` exist. Load both NPY files using packaged NumPy and assert dtype `float32`, shape second dimension `6`, first Z `0.0`, and second Z `-0.003` within `1e-7`.

- [ ] **Step 2: Run against an incomplete directory and verify failure**

```powershell
py -3.13 tests/windows_portable_smoke.py artifacts/empty
```

Expected: exits nonzero and lists `GrayscaleLayers.exe` as missing.

- [ ] **Step 3: Invoke smoke validation from the builder before compression**

Add both validations:

```powershell
& (Join-Path $PublishRoot 'runtime/python/python.exe') `
  (Join-Path $RepoRoot 'tests/windows_portable_smoke.py') $PublishRoot
if ($LASTEXITCODE -ne 0) { throw 'Portable runtime smoke test failed.' }
```

Copy the complete publish tree to a temporary parent named `中文 路径`, rerun the same smoke test there, and remove only that builder-owned temporary copy afterward.

- [ ] **Step 4: Run the complete builder and inspect contents**

```powershell
pwsh -File scripts/build-windows-portable.ps1 -Version 1.0.0 -OutputRoot artifacts
tar -tf artifacts/GrayscaleLayers-Windows-x64-v1.0.0.zip
```

Expected: builder and both smoke runs pass; the archive contains one top-level directory and none of the forbidden paths from Task 5.

- [ ] **Step 5: Commit Task 6**

```bash
git add tests/windows_portable_smoke.py scripts/build-windows-portable.ps1
git commit -m "test: validate Windows portable runtime"
```

---

### Task 7: Add Windows CI and tagged release automation

**Files:**
- Create: `.github/workflows/windows-portable.yml`

**Interfaces:**
- Consumes: test/build commands from Tasks 1-6.
- Produces: CI artifact `GrayscaleLayers-Windows-x64-v1.0.0` and GitHub Release assets for `v1.0.0`.

- [ ] **Step 1: Add the workflow triggers and least privileges**

Use:

```yaml
name: Windows portable
on:
  pull_request:
  push:
    branches: [main]
    tags: ['v*']
permissions:
  contents: read
```

- [ ] **Step 2: Add cross-platform Python regression jobs**

Use a matrix of `ubuntu-latest`, `macos-latest`, and `windows-latest`, `actions/checkout@v4`, `actions/setup-python@v5` with Python `3.13`, install `numpy==2.5.2 pillow==12.3.0`, and run:

```yaml
- run: python -m unittest discover -s tests -v
```

- [ ] **Step 3: Add the Windows build job**

On `windows-latest`, use `actions/setup-dotnet@v4` with `10.0.x`, `actions/setup-python@v5` with `3.13`, then run:

```yaml
- run: dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj -c Release
- shell: pwsh
  run: ./scripts/build-windows-portable.ps1 -Version 1.0.0 -OutputRoot artifacts
- uses: actions/upload-artifact@v4
  with:
    name: GrayscaleLayers-Windows-x64-v1.0.0
    path: |
      artifacts/GrayscaleLayers-Windows-x64-v1.0.0.zip
      artifacts/GrayscaleLayers-Windows-x64-v1.0.0.zip.sha256
```

Set `needs: python-tests`. A Python regression failure must prevent packaging.

- [ ] **Step 4: Add tag consistency and release upload**

For tag refs, compare `GITHUB_REF_NAME` to `v1.0.0` and fail on mismatch. Add a separate release job with `permissions: contents: write`, download the build artifact, and run:

```yaml
- shell: pwsh
  env:
    GH_TOKEN: ${{ github.token }}
  run: gh release create $env:GITHUB_REF_NAME ./*.zip ./*.sha256 --generate-notes
```

The release job runs only when `startsWith(github.ref, 'refs/tags/v')`.

- [ ] **Step 5: Validate workflow syntax and push for a real Windows run**

Install the fixed parser and run local YAML parsing:

```bash
python3 -m pip install PyYAML==6.0.3
python3 -c 'import pathlib,yaml; yaml.safe_load(pathlib.Path(".github/workflows/windows-portable.yml").read_text())'
```

Then push the task branch and inspect the actual GitHub Actions run. Expected: all three Python matrix jobs pass, C# tests pass on Windows, the package job passes, and the two release files are downloadable as an Actions artifact.

- [ ] **Step 6: Commit Task 7**

```bash
git add .github/workflows/windows-portable.yml
git commit -m "ci: build Windows portable release"
```

---

### Task 8: Add end-user documentation, licenses, and the release gate

**Files:**
- Create: `README-Windows.md`
- Create: `THIRD-PARTY-NOTICES.txt`
- Create: `packaging/licenses/Avalonia.LICENSE.md`
- Create: `packaging/licenses/SkiaSharp.LICENSE.txt`
- Create: `packaging/licenses/DotNetRuntime.LICENSE.txt`
- Create: `docs/windows-release-checklist.md`
- Modify: `GrayscaleLayersMac/README.md`
- Modify: `scripts/build-windows-portable.ps1`

**Interfaces:**
- Produces: complete offline usage and verification instructions included in every ZIP.
- Produces: a signed-off Windows 10/11 manual validation record for v1.0.0.

- [ ] **Step 1: Write `README-Windows.md`**

Include these exact operational sections:

1. Supported systems: Windows 10/11 x64 only.
2. Start: extract the entire ZIP, then double-click `GrayscaleLayers.exe`; never run from inside the ZIP viewer.
3. Dependencies: no separate .NET or Python installation is required.
4. SHA-256: `Get-FileHash .\GrayscaleLayers-Windows-x64-v1.0.0.zip -Algorithm SHA256` and compare with the `.sha256` file.
5. SmartScreen: explain that v1.0.0 is unsigned and instruct the user to verify repository source and checksum; do not advise disabling Defender or SmartScreen.
6. Troubleshooting: incomplete extraction, write permission, locked output, existing output name, log location, and how to preserve the failed output for diagnosis.
7. Privacy: processing is local and logs do not contain image bytes.

- [ ] **Step 2: Write third-party notices**

List CPython 3.13.14, NumPy 2.5.2, Pillow 12.3.0, Avalonia 11.3.18, SkiaSharp 2.88.9, and the .NET 10 runtime. For each, include project URL, redistributed version, license name, and where its full license text is present in the package.

Preserve CPython's `runtime/python/LICENSE.txt` and the `*.dist-info/licenses/` directories installed by the two wheels. Commit the three static license files listed above from the matching upstream release or restored NuGet package, copy them into ZIP directory `licenses/`, and make the builder fail if any of these six license locations is absent.

- [ ] **Step 3: Write the manual release checklist**

Create separate Windows 10 x64 and Windows 11 x64 sections. Each must contain checkboxes for:

- clean standard user account with no .NET/Python installed;
- ZIP checksum verification;
- offline launch;
- app installed under a path containing `中文 测试`;
- input and output paths containing spaces and Chinese characters;
- complete TIFF → DXF → machine-file pipeline;
- DXF preview, zoom, pan, layer switching, and direction arrows;
- cancellation during each stage and ownership-safe cleanup;
- same-name output collision with original content unchanged;
- open-output-directory behavior;
- second run after cancellation;
- macOS/Windows fixed-fixture comparison using `rtol=1e-6`, `atol=1e-7`;
- diagnostic log review confirming no image bytes or secrets.

Each environment section must record OS build, ZIP SHA-256, commit SHA, tester, date, and pass/fail. A release is blocked unless every checkbox passes on both systems.

- [ ] **Step 4: Update project README and builder**

Rename the README heading so it is not macOS-only, preserve existing macOS run/publish commands, and link to `README-Windows.md` plus the design spec. Make the builder copy both documentation files and all required license files before smoke validation.

- [ ] **Step 5: Run documentation and package checks**

```bash
rg -n 'Windows 10/11 x64|Get-FileHash|SmartScreen|不需要.*Python|offline|离线' README-Windows.md docs/windows-release-checklist.md
rg -n 'CPython 3.13.14|NumPy 2.5.2|Pillow 12.3.0|Avalonia 11.3.18|SkiaSharp 2.88.9|.NET 10' THIRD-PARTY-NOTICES.txt
```

On Windows, rerun the full builder. Expected: docs and licenses appear in the ZIP and smoke validation passes.

- [ ] **Step 6: Commit Task 8**

```bash
git add README-Windows.md THIRD-PARTY-NOTICES.txt packaging/licenses docs/windows-release-checklist.md GrayscaleLayersMac/README.md scripts/build-windows-portable.ps1
git commit -m "docs: add Windows release instructions"
```

---

### Task 9: Execute the final release-candidate gate

**Files:**
- Modify: `docs/windows-release-checklist.md`
- Modify: `GrayscaleLayersMac/GrayscaleLayersMac.csproj`

**Interfaces:**
- Consumes: every artifact and test command from Tasks 1-8.
- Produces: an auditable v1.0.0 go/no-go result tied to a commit and ZIP SHA-256.

- [ ] **Step 1: Run the complete automated suite from a clean checkout**

```bash
python3 -m unittest discover -s tests -v
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj -c Release
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release
```

On Windows also run:

```powershell
pwsh -File scripts/build-windows-portable.ps1 -Version 1.0.0 -OutputRoot artifacts
```

Expected: every command exits 0; record counts and artifact SHA-256 in the checklist.

- [ ] **Step 2: Compare the archive against the allowed manifest**

Confirm the ZIP contains only the app publish files, `Assets/`, `scripts/`, `runtime/python/`, `portable-release.json`, `README-Windows.md`, and `THIRD-PARTY-NOTICES.txt`. Fail the gate for PDBs, build caches, source test directories, access tokens, absolute developer paths, or user data.

- [ ] **Step 3: Complete Windows 10 and Windows 11 manual checks**

Use two clean x64 virtual machines, disconnect networking before launch, and fill every field and checkbox in `docs/windows-release-checklist.md`. Do not mark a skipped check as passing.

- [ ] **Step 4: Verify macOS regression and cross-platform fixture parity**

Run the application development build on macOS, execute the same fixed fixture, and compare structured output to Windows using NumPy `assert_allclose(rtol=1e-6, atol=1e-7)`. JSON keys, integer fields, DXF entity counts, and filenames must match exactly.

- [ ] **Step 5: Record the go/no-go decision**

For a passing checklist, generate the evidence line from the actual repository and artifact:

```powershell
$Commit = (git rev-parse HEAD).Trim()
$ZipHash = (Get-FileHash artifacts/GrayscaleLayers-Windows-x64-v1.0.0.zip -Algorithm SHA256).Hash.ToLowerInvariant()
"GO — Windows v1.0.0 approved for self-use; commit $Commit; ZIP SHA-256 $ZipHash." | Add-Content docs/windows-release-checklist.md -Encoding utf8
```

For a failing checklist, append `NO-GO — Windows v1.0.0 blocked; failing checklist item(s):` followed by the actual unchecked checklist identifiers. A `NO-GO` result returns to the task that owns the failure; do not create the tag.

- [ ] **Step 6: Commit the release evidence**

```bash
git add docs/windows-release-checklist.md GrayscaleLayersMac/GrayscaleLayersMac.csproj
git commit -m "chore: record Windows v1.0.0 release validation"
```

- [ ] **Step 7: Create the release only for a GO decision**

```bash
git tag -a v1.0.0 -m "Windows portable v1.0.0"
git push origin HEAD
git push origin v1.0.0
```

Expected: the tag workflow regenerates the ZIP, passes all automated gates, and creates a GitHub Release containing exactly the ZIP and `.sha256` file. Download those assets to a clean Windows machine, verify the checksum once more, and archive the final checklist with the release notes.
