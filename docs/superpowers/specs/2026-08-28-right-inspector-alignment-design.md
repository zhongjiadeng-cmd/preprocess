# Right Inspector Alignment Design

## Goal

Make every expandable card in the workflow inspector fill the same available width so their right edges align at every supported window size. Preserve the existing field grouping, controls, behavior, and scrolling.

## Scope

- Apply a shared horizontal stretch constraint to the inspector's vertically stacked content.
- Ensure each inspector section and its internal row container consumes the available width.
- Keep path fields flexible while directory and file-picker buttons retain their content-sized width.
- Keep the existing two-, three-, and four-column parameter grids and their current spacing.
- Preserve all existing business logic and the fixed action area at the bottom of the inspector.

## Design

The existing vertical inspector remains a `StackPanel` inside its `ScrollViewer`. Its horizontal content alignment will be set to `Stretch`, establishing one common width for all child cards. `MakeInspectorSection` will return a horizontally stretched card, and its internal row grid will also stretch. Individual controls continue to stretch within their assigned grid columns.

This addresses the root cause without replacing the inspector container, introducing fixed pixel widths, or changing the workspace's preview/inspector split.

## Responsive Behavior

Cards follow the inspector column width when the window is resized. Parameter rows retain equal star-sized columns. Picker buttons remain auto-sized, leaving the adjacent path field to absorb width changes. Existing vertical scrolling continues to handle content that exceeds the available height.

## Verification

- Build the Avalonia project with no new warnings or errors attributable to the change.
- Run the existing C# test suite.
- Launch the application when the environment permits and verify that the right edges of “输入与分层”, “Hatch 与 DXF”, “Voronoi 分块与边界扩散”, and “机器加工文件” align.
- Resize the window and confirm cards continue to share one right edge and controls do not overlap or clip.

## Non-goals

- No changes to colors, typography, card styling, business logic, or input validation.
- No redesign of the preview area or the overall workspace column widths.
- No unrelated refactoring of the current uncommitted work.
