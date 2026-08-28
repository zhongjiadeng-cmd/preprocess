# DXF Preview Block Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove DXF preview block inference and color LINE entities from the exact companion `*.blocks.json`, while keeping DXFs without a companion JSON previewable in one color.

**Architecture:** Add one internal immutable `DxfBlockMetadata` unit that owns companion-path resolution, strict v1 JSON validation, total-LINE validation, and source-entity-index classification. `DxfPreviewControl` continues to scan and render DXF, but obtains `BlockIndex` and `IsBorder` only from this unit; absent metadata maps every LINE to block `0` and no border.

**Tech Stack:** C# 13 / .NET 10, Avalonia 11.3.18, `System.Text.Json`, MSTest 4.3.3, Python 3 `unittest`.

## Global Constraints

- Do not change the existing `*.blocks.json` v1 producer or machine-export consumer contract.
- Missing JSON is valid and produces a one-color preview; present but invalid or inconsistent JSON fails preview.
- JSON keys exactly match the v1 contract and duplicates are invalid.
- Counts and indices are non-negative JSON integers, block indices are unique, centers are finite JSON numbers, and `blocks` is non-empty.
- `border_line_count + sum(line_count)` exactly equals the DXF LINE count.
- Empty blocks remain in the displayed count and consume no entities.
- Sampled entities are classified by original DXF LINE ordinal.
- Add no public API or third-party dependency; preserve unrelated worktree changes.

---

## File Structure

- Create `GrayscaleLayersMac/DxfBlockMetadata.cs`: metadata model, parser, validation, and ordinal mapping.
- Create `GrayscaleLayersMac.Tests/DxfBlockMetadataTests.cs`: strict JSON and mapping tests.
- Create `GrayscaleLayersMac.Tests/DxfPreviewControlTests.cs`: real DXF summary and failure tests.
- Modify `GrayscaleLayersMac/DxfPreviewControl.cs`: remove inference and consume metadata.
- Modify `GrayscaleLayersMac/README.md`: document JSON-backed coloring and one-color fallback.

### Task 1: Strict Companion Metadata Reader and Ordinal Mapper

**Files:**
- Create: `GrayscaleLayersMac/DxfBlockMetadata.cs`
- Create: `GrayscaleLayersMac.Tests/DxfBlockMetadataTests.cs`
- Existing visibility: `GrayscaleLayersMac/Properties/AssemblyInfo.cs`

**Interfaces:**
- Consumes: DXF path `string`; resolve only `Path.ChangeExtension(dxfPath, ".blocks.json")`.
- Produces: `internal sealed record DxfBlockDefinition(int BlockIndex, double CenterX, double CenterY, int LineCount)`.
- Produces: `internal sealed record DxfLineClassification(int BlockIndex, bool IsBorder)`.
- Produces: `internal sealed class DxfBlockMetadata` with `BorderLineCount`, `Blocks`, `LoadForDxf`, `ValidateLineCount`, and `ClassifyLine`.

- [ ] **Step 1: Write the first failing tests**

Create a test class with a fresh `Path.GetTempPath()` directory per test and recursive cleanup. Add:

```csharp
[TestMethod]
public void MissingCompanionReturnsNull()
{
    Assert.IsNull(DxfBlockMetadata.LoadForDxf(Path.Combine(_root, "plain.dxf")));
}

[TestMethod]
public void ReadsV1DocumentAndClassifiesOriginalLineOrdinals()
{
    var dxf = Path.Combine(_root, "layer.dxf");
    File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), """
        {"version":1,"border_line_count":2,"blocks":[
          {"block_index":7,"center_x":1.5,"center_y":-2,"line_count":2},
          {"block_index":9,"center_x":3,"center_y":4.5,"line_count":0},
          {"block_index":3,"center_x":5,"center_y":6,"line_count":1}
        ]}
        """);

    var metadata = DxfBlockMetadata.LoadForDxf(dxf);

    Assert.IsNotNull(metadata);
    metadata.ValidateLineCount(5);
    Assert.AreEqual(3, metadata.Blocks.Count);
    Assert.AreEqual(new DxfLineClassification(0, true), metadata.ClassifyLine(0));
    Assert.AreEqual(new DxfLineClassification(7, false), metadata.ClassifyLine(2));
    Assert.AreEqual(new DxfLineClassification(7, false), metadata.ClassifyLine(3));
    Assert.AreEqual(new DxfLineClassification(3, false), metadata.ClassifyLine(4));
}

[TestMethod]
public void NonContiguousSampleOrdinalsKeepSourceBlockMapping()
{
    var metadata = LoadHappyFixture();

    Assert.AreEqual(7, metadata.ClassifyLine(2).BlockIndex);
    Assert.AreEqual(3, metadata.ClassifyLine(4).BlockIndex);
}

private DxfBlockMetadata LoadHappyFixture()
{
    var dxf = Path.Combine(_root, "sample.dxf");
    File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), """
        {"version":1,"border_line_count":2,"blocks":[
          {"block_index":7,"center_x":1.5,"center_y":-2,"line_count":2},
          {"block_index":9,"center_x":3,"center_y":4.5,"line_count":0},
          {"block_index":3,"center_x":5,"center_y":6,"line_count":1}
        ]}
        """);
    return DxfBlockMetadata.LoadForDxf(dxf)!;
}
```

The last test models a stride that skips source entities across a block boundary and proves classification uses original ordinals rather than positions in the collected sample.

- [ ] **Step 2: Run RED**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~DxfBlockMetadataTests`

Expected: build failure because the metadata types do not exist.

- [ ] **Step 3: Implement the minimum parser and mapper**

Create these exact internal types:

```csharp
internal sealed record DxfBlockDefinition(
    int BlockIndex, double CenterX, double CenterY, int LineCount);

internal sealed record DxfLineClassification(int BlockIndex, bool IsBorder);

internal sealed class DxfBlockMetadata
{
    public int BorderLineCount { get; }
    public IReadOnlyList<DxfBlockDefinition> Blocks { get; }
    public static DxfBlockMetadata? LoadForDxf(string dxfPath);
    public void ValidateLineCount(int lineCount);
    public DxfLineClassification ClassifyLine(int lineIndex);
}
```

`LoadForDxf` returns `null` only when the companion does not exist. A present file must be non-empty, regular, and not a reparse point. Parse with `JsonDocument`; enumerate each object into a dictionary while rejecting duplicate keys and require exact sets `version/border_line_count/blocks` and `block_index/center_x/center_y/line_count`. Use `ValueKind == Number`, `TryGetInt32`, `TryGetDouble`, `double.IsFinite`, `HashSet<int>`, and checked cumulative counts. Wrap file/JSON/validation errors in `InvalidDataException` naming the sidecar.

`ClassifyLine` returns `(0, true)` before `BorderLineCount`, then walks cumulative block ends in JSON order, skipping zero-length ranges. Reject negative or `>= TotalLineCount` indices.

- [ ] **Step 4: Run GREEN**

Run the Task 1 focused command again.

Expected: both tests pass.

- [ ] **Step 5: Add strict failing tests**

Use a data-driven test that writes each raw JSON and expects `InvalidDataException` for: malformed `{`; duplicate `version`; version `2`; Boolean or negative `border_line_count`; missing/extra top-level fields; empty `blocks`; missing/extra block fields; negative or duplicate `block_index`; Boolean, fractional, or negative `line_count`; overflowed/non-finite center; empty file; directory or reparse-point sidecar. Add separate assertions that `ValidateLineCount(4)` rejects the happy fixture total `5`, and `ClassifyLine(-1)`/`ClassifyLine(5)` reject out-of-range ordinals.

Example row:

```csharp
[DataRow("{\"version\":1,\"version\":1,\"border_line_count\":0,\"blocks\":[{\"block_index\":0,\"center_x\":0,\"center_y\":0,\"line_count\":1}]}", "重复")]
```

- [ ] **Step 6: Run RED for incomplete validation**

Run the Task 1 focused command.

Expected: at least the first unimplemented strict case fails while happy-path tests remain green.

- [ ] **Step 7: Complete strict validation minimally**

Add only the validation exercised above. Keep a private checked `TotalLineCount` and use it for total validation and ordinal bounds. Do not catch process-fatal exceptions.

- [ ] **Step 8: Verify and commit Task 1**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~DxfBlockMetadataTests
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
git add GrayscaleLayersMac/DxfBlockMetadata.cs GrayscaleLayersMac.Tests/DxfBlockMetadataTests.cs
git commit -m "feat: read DXF preview block metadata"
```

Expected: all C# tests pass without warnings before the commit.

### Task 2: Replace Preview Inference with Metadata Classification

**Files:**
- Modify: `GrayscaleLayersMac/DxfPreviewControl.cs:223-241,626-724`
- Create: `GrayscaleLayersMac.Tests/DxfPreviewControlTests.cs`

**Interfaces:**
- Consumes: Task 1's `LoadForDxf`, `ValidateLineCount`, `ClassifyLine`, and `Blocks.Count`.
- Produces: unchanged public `LoadFile(string)` and `Summary`; `ScanFile` accepts `DxfBlockMetadata?` and returns no inferred block/vertical fields.

- [ ] **Step 1: Write failing public behavior tests**

Add a `WriteDxf` helper that emits SECTION/ENTITIES, complete LINE group codes `10/20/11/21`, ENDSEC, and EOF using invariant numbers. Add:

```csharp
[TestMethod]
public void MissingCompanionLoadsWithoutInferredBlockSummary()
{
    var dxf = Path.Combine(_root, "plain.dxf");
    WriteDxf(dxf, (0, 0, 0, 10), (0, 5, 5, 5), (0, 10, 5, 10));
    using var preview = new DxfPreviewControl();
    preview.LoadFile(dxf);
    Assert.AreEqual("plain.dxf · 3 条 LINE", preview.Summary);
}

[TestMethod]
public void ValidCompanionReportsDeclaredBlocksIncludingEmptyBlock()
{
    var dxf = Path.Combine(_root, "blocked.dxf");
    WriteDxf(dxf, (0, 0, 10, 0), (0, 1, 10, 1), (0, 2, 10, 2));
    File.WriteAllText(Path.ChangeExtension(dxf, ".blocks.json"), """
        {"version":1,"border_line_count":1,"blocks":[
          {"block_index":4,"center_x":0,"center_y":0,"line_count":1},
          {"block_index":8,"center_x":1,"center_y":1,"line_count":0},
          {"block_index":2,"center_x":2,"center_y":2,"line_count":1}]}
        """);
    using var preview = new DxfPreviewControl();
    preview.LoadFile(dxf);
    Assert.AreEqual("blocked.dxf · 3 条 LINE · 加工块 3 个", preview.Summary);
}
```

Also write `PresentCompanionWithMismatchedLineCountFailsPreview`, using one DXF LINE and metadata `line_count:2`, expecting `InvalidDataException`. The first fixture deliberately contains a vertical LINE and Y resets so the old inference produces the wrong summary.

- [ ] **Step 2: Run RED**

Run: `dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~DxfPreviewControlTests`

Expected: summary tests fail on old “分析出” text and mismatch does not throw.

- [ ] **Step 3: Remove inferred state from `ScanFile`**

Change its shape to:

```csharp
private static (
    int Count, Rect Bounds, List<Segment> Segments,
    double MinZ, double MaxZ) ScanFile(
    string path, int collectEvery, DxfBlockMetadata? metadata)
```

Delete `BlockCount`, `HasVerticalLine`, `detectGeneratedBorder`, `blockIndex`, `previousHatchY`, and vertical/Y-reset calculations. In `CompleteEntity`, before sampling:

```csharp
var entityIndex = count;
var classification = metadata?.ClassifyLine(entityIndex)
    ?? new DxfLineClassification(0, false);
```

Pass that classification to `Segment`. Original `count`, not sampled-list position, preserves correct mapping with `collectEvery > 1`.

- [ ] **Step 4: Load and validate metadata once**

In `LoadFile`:

```csharp
var metadata = DxfBlockMetadata.LoadForDxf(path);
var firstPass = ScanFile(path, 0, metadata: null);
metadata?.ValidateLineCount(firstPass.Count);
var stride = Math.Max(1,
    (int)Math.Ceiling(firstPass.Count / (double)MaximumDisplayedSegments));
var secondPass = ScanFile(path, stride, metadata);
```

Set summary without trailing whitespace:

```csharp
var blockSummary = metadata is null
    ? string.Empty
    : $" · 加工块 {metadata.Blocks.Count} 个";
Summary = stride == 1
    ? $"{Path.GetFileName(path)} · {count:N0} 条 LINE{blockSummary}"
    : $"{Path.GetFileName(path)} · {count:N0} 条 LINE{blockSummary} · 抽样显示 {segments.Count:N0} 条";
```

- [ ] **Step 5: Run GREEN and prove dead inference is gone**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~DxfPreviewControlTests
rg -n "HasVerticalLine|detectGeneratedBorder|previousHatchY|分析出" GrayscaleLayersMac/DxfPreviewControl.cs
```

Expected: tests pass; `rg` prints nothing and exits `1`.

- [ ] **Step 6: Run full C# regression and commit**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
git add GrayscaleLayersMac/DxfPreviewControl.cs GrayscaleLayersMac.Tests/DxfPreviewControlTests.cs
git commit -m "feat: color DXF preview from block metadata"
```

Expected: all C# tests pass without warnings before commit.

### Task 3: Documentation and Full Regression

**Files:**
- Modify: `GrayscaleLayersMac/README.md:20,35`
- Test: C# and both Python suites.

**Interfaces:**
- Consumes: Tasks 1–2 behavior.
- Produces: accurate user-facing preview and sidecar description.

- [ ] **Step 1: Update README behavior**

Replace the Y-reset inference sentence with:

```markdown
生成完成后，“实际 DXF 预览”会读取输出文件中的 LINE 实体，并在存在同名 `*.blocks.json` 时按其中记录的检查边框数量、加工块输出顺序和每块 LINE 数量准确着色；存在但无效或与 DXF LINE 总数不一致的侧车会使预览明确失败。没有配套侧车的普通 DXF 仍可导入和预览，所有 LINE 使用统一颜色。
```

Keep the paragraph's arrow, mouse, layer-selector, row-merging, and sampling text. Change “该文件只在机器加工打包阶段使用” to “该文件同时用于 DXF 预览着色和机器加工打包”.

- [ ] **Step 2: Verify obsolete claims are absent**

Run: `rg -n "Y 坐标重新回到高处|分析出|只在机器加工打包阶段使用|HasVerticalLine|detectGeneratedBorder|previousHatchY" GrayscaleLayersMac/README.md GrayscaleLayersMac/DxfPreviewControl.cs`

Expected: no output and exit `1`.

- [ ] **Step 3: Run formatting and all regressions**

```bash
dotnet format GrayscaleLayersMac/GrayscaleLayersMac.csproj --verify-no-changes
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
python3 -m unittest tests.test_texture_to_hatch_dxf tests.test_dxf_to_machine_file
git diff --check
```

Expected: no formatting diff; all C# and Python tests pass; `git diff --check` is silent.

- [ ] **Step 4: Inspect and commit docs**

```bash
git diff -- GrayscaleLayersMac/DxfBlockMetadata.cs GrayscaleLayersMac/DxfPreviewControl.cs GrayscaleLayersMac.Tests/DxfBlockMetadataTests.cs GrayscaleLayersMac.Tests/DxfPreviewControlTests.cs GrayscaleLayersMac/README.md
git add GrayscaleLayersMac/README.md
git commit -m "docs: explain metadata-backed DXF preview"
git status --short
git log -4 --oneline
```

Expected: focused diff only contains this feature; final status contains only pre-existing unrelated untracked files; implementation commits are visible.
