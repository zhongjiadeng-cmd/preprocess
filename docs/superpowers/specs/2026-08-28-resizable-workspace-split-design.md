# Resizable Workspace Split Design

## Goal

Add a draggable vertical divider between the preview area and parameter inspector. The divider must let users resize both sides, share one ratio across the workflow tabs, and restore that ratio after the application restarts.

## Layout

`MakeWorkspace` will use three grid columns: preview, splitter, and inspector. The preview and inspector columns use star sizing so the stored value represents a ratio rather than a fixed pixel width. The default is approximately 58% preview and 42% inspector, matching the existing layout at the default window size.

The preview retains a 420 px minimum width. The inspector receives a 460 px minimum width so its multi-column parameter rows remain usable. The splitter occupies an 8 px interaction region with a thin centered rule. Pointer-over and drag states make the rule more visible, and the pointer uses the horizontal resize cursor.

## Interaction

Dragging uses Avalonia's native `GridSplitter`. The active workspace updates continuously during the drag. When the drag completes, the application calculates a normalized preview ratio, clamps it to the valid range implied by both minimum widths, applies it to the other workspace, and saves it.

The “三步流程” and “纹理转 Hatch DXF” pages share one split ratio. Adjusting either page changes the layout used by the other page when it is shown.

## Persistence

A small UI-settings component owns the shared ratio. It stores a versioned JSON document under the current user's local application-data directory in a `GrayscaleLayersMac` subdirectory. Saving uses a temporary sibling file followed by replacement so an interrupted write does not leave a partially written settings file.

Missing files, malformed JSON, unsupported versions, non-finite values, and out-of-range ratios all fall back to the default ratio. Settings failures never block the main processing workflow and are not presented as modal errors.

## Components

- `WorkspaceSplitSettings`: validates, loads, and atomically saves the normalized ratio without depending on Avalonia controls.
- `MainWindow`: owns the current shared ratio and registered workspace column pairs.
- `MakeWorkspace`: creates the three-column layout, native splitter, minimum-width constraints, and drag-completion synchronization.
- `UiTheme`: supplies the splitter's restrained normal and highlighted visual states.

## Verification

- Unit-test valid round trips and fallback behavior for missing, corrupt, unsupported, and out-of-range settings.
- Build the Avalonia project and run the complete C# test suite.
- Launch the application and verify dragging in either shared-layout tab resizes both sides without clipping.
- Switch tabs and confirm the adjusted ratio is shared.
- Resize the main window to its minimum width and confirm neither side crosses its minimum.
- Restart the application and confirm the last completed drag is restored.

## Non-goals

- The grayscale-only tab remains unchanged because it does not use the two-pane workspace.
- No collapse button, double-click reset, per-tab ratio, or new settings screen is added.
- No changes are made to processing logic, preview behavior, or parameter controls.
