# Final Fix Report — DXF Preview Block Metadata

## Scope and files

- GrayscaleLayersMac/DxfBlockMetadata.cs
  - Restricts border_line_count to the existing machine-export v1 contract: integer 0 or 4.
- GrayscaleLayersMac/DxfPreviewControl.cs
  - Exposes the existing Segment record and ScanFile only as internal, preserving the public API while allowing the friend test assembly to verify real sampling.
- GrayscaleLayersMac.Tests/DxfBlockMetadataTests.cs
  - Replaces the contract-invalid happy fixture 2 with 4, updates its source-ordinal assertions, and rejects 1 and 2.
- GrayscaleLayersMac.Tests/DxfPreviewControlTests.cs
  - Replaces the contract-invalid summary fixture 1 with 4 and adds a real DXF ScanFile collectEvery 2 regression across a block boundary.
- docs/superpowers/specs/2026-08-27-dxf-preview-block-metadata-design.md
- docs/superpowers/plans/2026-08-28-dxf-preview-block-metadata.md
  - Align the documented preview contract and examples with 0/4.

## TDD evidence

### RED

1. Added invalid sidecar rows for border_line_count 1 and 2, changed valid fixtures to 4, and added the scan sampling test before changing the parser.
2. Ran:

       dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter "FullyQualifiedName~DxfBlockMetadataTests|FullyQualifiedName~DxfPreviewControlTests"

   First result: build failed with CS0117 because DxfPreviewControl.ScanFile was private, proving the test required the narrow internal seam.

3. Made only Segment and ScanFile internal, then reran the same command.

   Result: 2 failures / 28 passes. Both new invalid rows failed because DxfBlockMetadata.LoadForDxf accepted border_line_count 1 and 2.

### GREEN

1. Changed parser validation to reject every count other than 0 and 4.
2. Reran the same focused command.

   Result: 30 passed, 0 failed.

The sampling regression scans six real DXF LINE entities at stride two. It retains source ordinals 0, 2, and 4, and asserts the hand-derived block sequence 7, 3, 3; a regression that classified sampled-list positions instead would produce 7, 7, 3.

## Final verification

| Command | Result |
| --- | --- |
| dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter "FullyQualifiedName~DxfBlockMetadataTests\|FullyQualifiedName~DxfPreviewControlTests" | 30 passed |
| dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj | 84 passed |
| python3 -m unittest tests.test_texture_to_hatch_dxf tests.test_dxf_to_machine_file | 189 passed |
| obsolete-marker search | no matches; exit 1 as expected |
| valid-fixture review | all happy-path, mapping, and summary fixtures use 0 or 4; 1 and 2 occur only as explicit rejection rows |
| git diff --check | clean; exit 0 |

dotnet format GrayscaleLayersMac/GrayscaleLayersMac.csproj --verify-no-changes still reports the explicitly waived unrelated existing whitespace diagnostics: DxfPreviewControl.cs:461-463 and MainWindow.cs:44,789-799. No formatter edits were made.

## Self-review

- The C# reader now matches the Python machine-export validator’s only permitted border counts.
- The valid metadata fixtures retain border, empty-block, mapping, and summary coverage using 4.
- The sampling test exercises the real DXF parser with collectEvery greater than 1; it does not call ClassifyLine directly.
- The test seam is internal and available only through the existing friend-test assembly; no public API or dependency was added.

## Commit

This report and all final fixes are committed together at HEAD (the final-fix commit).

## Concerns

None beyond the pre-existing, human-waived formatter diagnostics noted above.
