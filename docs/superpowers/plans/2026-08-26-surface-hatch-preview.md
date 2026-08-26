# Surface Hatch Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update the existing interactive model preview to use `layer_06_gray_lt_128.tiff` and render adjustable parallel hatch lines only inside its black regions, with seamless geometric segmentation at every OBJ triangle boundary.

**Architecture:** A one-off Python generator parses the OBJ and binary TIFF, builds compact geometry, a lossless mask texture, and UV triangle lookup data, then writes a sub-1 MB inline HTML fragment. Browser-side Three.js code scans the mask with a global family of parallel UV lines, detects triangle changes, resolves exact boundary points, maps every per-triangle segment to 3D with barycentric interpolation, and rebuilds the line geometry when angle or spacing changes.

**Tech Stack:** Python 3, Pillow, NumPy, Three.js 0.180.0 ES module, HTML/CSS/JavaScript, in-app browser validation.

## Global Constraints

- Treat `/Users/ccc/Downloads/data/surf.exported.obj` and `/Users/ccc/Desktop/preprocess/layer_06_gray_lt_128.tiff` as read-only inputs.
- Do not modify the user-provided attachment page.
- Classify texture pixels below grayscale 128 as black.
- Default hatch angle is 45 degrees.
- Split hatch geometry at OBJ triangle boundaries without adding a visible gap.
- Write the updated fragment to `/Users/ccc/.codex/visualizations/2026/08/26/01a03cc9-46bf-74f2-9b5a-5f66e22df1f5/surface-hatch-preview.html`.
- Keep the fragment below 1,000,000 bytes and do not include `fetch`, XHR, WebSocket, or local absolute source paths.
- Preserve orbit rotation, wheel zoom, reset, solid-texture mode, independent triangular-grid display, and responsive layouts down to 320 px.

---

### Task 1: Build lossless mask and triangle lookup payload

**Files:**
- Create: `/private/tmp/generate_surface_hatch_preview.py`
- Create: `/private/tmp/test_surface_hatch_payload.py`
- Create: `/private/tmp/surface-hatch-preview-data.json`

**Interfaces:**
- Consumes: `Path` values for the OBJ and TIFF.
- Produces: `build_payload(obj_path: Path, texture_path: Path, atlas_size: int = 768) -> dict` with `positions`, `normals`, `uvs`, `indices`, `mask_png`, `triangle_map_png`, and `stats`.
- Produces: `rasterize_triangle_map(uvs, indices, size) -> Image.Image`, encoding triangle index plus one as RGB and reserving RGB zero for no triangle.

- [ ] **Step 1: Write failing payload tests**

```python
from pathlib import Path
from PIL import Image
from generate_surface_hatch_preview import build_payload, decode_data_uri

payload = build_payload(
    Path('/Users/ccc/Downloads/data/surf.exported.obj'),
    Path('/Users/ccc/Desktop/preprocess/layer_06_gray_lt_128.tiff'),
)
assert payload['stats']['source_faces'] == 5350
assert payload['stats']['source_texture_size'] == [5326, 5326]
assert abs(payload['stats']['black_fraction'] - 0.183371) < 0.00001
assert len(payload['indices']) == 5350 * 3
assert payload['mask_png'].startswith('data:image/png;base64,')
assert payload['triangle_map_png'].startswith('data:image/png;base64,')
mask = Image.open(decode_data_uri(payload['mask_png']))
triangle_map = Image.open(decode_data_uri(payload['triangle_map_png']))
assert mask.size == (768, 768)
assert triangle_map.size == (768, 768)
assert set(mask.getdata()) <= {0, 255}
```

- [ ] **Step 2: Run the test and verify the missing-module failure**

Run: `PYTHONPATH=/private/tmp python3 /private/tmp/test_surface_hatch_payload.py`

Expected: FAIL with `ModuleNotFoundError: No module named 'generate_surface_hatch_preview'`.

- [ ] **Step 3: Implement the minimal payload generator**

Parse `v`, `vt`, `vn`, and triangular `f` records; deduplicate `(v, vt, vn)` face corners; round geometry values to five decimals and UVs to six decimals. Convert the TIFF to mode `L`, threshold with `pixel < 128`, resize the binary mask to 768 × 768 with nearest-neighbor sampling, encode it as optimized PNG, rasterize every indexed UV triangle into a 24-bit triangle-ID PNG, and return the exact payload keys above.

- [ ] **Step 4: Run the payload test**

Run: `PYTHONPATH=/private/tmp python3 /private/tmp/test_surface_hatch_payload.py`

Expected: PASS with no output.

- [ ] **Step 5: Serialize and size-check the payload**

Run: `python3 /private/tmp/generate_surface_hatch_preview.py --payload-only`

Expected: `/private/tmp/surface-hatch-preview-data.json` exists and is below 800,000 bytes.

### Task 2: Prove black-only clipping and seamless triangle breaks

**Files:**
- Modify: `/private/tmp/generate_surface_hatch_preview.py`
- Create: `/private/tmp/test_surface_hatch_geometry.py`

**Interfaces:**
- Consumes: compact payload plus decoded binary mask.
- Produces: `generate_hatch_segments(payload: dict, mask: Image.Image, angle_degrees: float, spacing_pixels: int) -> list[dict]` for reference verification.
- Each result item has `triangle`, `uv0`, `uv1`, `xyz0`, and `xyz1` fields.

- [ ] **Step 1: Write failing geometry tests against a two-triangle fixture**

```python
segments = generate_hatch_segments(
    two_triangle_payload(),
    split_black_mask(),
    angle_degrees=0,
    spacing_pixels=8,
)
assert segments
assert all(mask_is_black(segment['uv0']) for segment in segments)
assert all(mask_is_black(segment['uv1']) for segment in segments)
left = next(s for s in segments if s['triangle'] == 0 and touches_shared_edge(s))
right = next(s for s in segments if s['triangle'] == 1 and touches_shared_edge(s))
assert distance(left['xyz1'], right['xyz0']) < 1e-6
assert left['xyz1'] == point_from_barycentric(left['triangle'], left['uv1'])
assert right['xyz0'] == point_from_barycentric(right['triangle'], right['uv0'])
```

- [ ] **Step 2: Run the test and verify the missing-function failure**

Run: `PYTHONPATH=/private/tmp python3 /private/tmp/test_surface_hatch_geometry.py`

Expected: FAIL because `generate_hatch_segments` is not defined.

- [ ] **Step 3: Implement reference hatch generation**

Generate a single global family of UV scanlines from angle and spacing. Clip each scanline to each UV triangle, sample the clipped interval against the binary mask, split black runs at mask transitions, map endpoints to 3D with barycentric interpolation, and apply a normal offset equal to `bounding_sphere_radius * 0.00035`. Use the same global line offset for adjacent triangles so independently clipped segments meet at their common edge.

- [ ] **Step 4: Run geometry tests and the real-model smoke test**

Run: `PYTHONPATH=/private/tmp python3 /private/tmp/test_surface_hatch_geometry.py`

Expected: PASS; the real-model 45-degree, 12-pixel-spacing smoke case produces at least one segment, all endpoint barycentric weights are within `[-1e-6, 1 + 1e-6]`, and the segment count remains below 200,000.

### Task 3: Build the updated interactive fragment

**Files:**
- Modify: `/private/tmp/generate_surface_hatch_preview.py`
- Create: `/private/tmp/test_surface_hatch_fragment.py`
- Create: `/Users/ccc/.codex/visualizations/2026/08/26/01a03cc9-46bf-74f2-9b5a-5f66e22df1f5/surface-hatch-preview.html`

**Interfaces:**
- Consumes: `build_payload(...) -> dict`.
- Produces: `build_fragment(payload: dict) -> str` and the final HTML fragment.

- [ ] **Step 1: Write the failing fragment contract test**

```python
from pathlib import Path

path = Path('/Users/ccc/.codex/visualizations/2026/08/26/01a03cc9-46bf-74f2-9b5a-5f66e22df1f5/surface-hatch-preview.html')
html = path.read_text(encoding='utf-8')
assert not html.lstrip().lower().startswith('<!doctype')
assert '<html' not in html.lower() and '<body' not in html.lower()
assert len(html.encode()) < 1_000_000
for forbidden in ('fetch(', 'XMLHttpRequest', 'WebSocket(', '/Users/ccc/Downloads', '/Users/ccc/Desktop'):
    assert forbidden not in html
for label in ('原始实心', '平行阴影线', '阴影线角度', '阴影线间距', '显示三角网格', '复位视角'):
    assert label in html
for control in ('surfaceMode', 'hatchAngle', 'hatchSpacing', 'hatchStrength', 'meshGrid', 'meshStrength'):
    assert f'id="{control}"' in html
```

- [ ] **Step 2: Run the test and verify the missing-file failure**

Run: `python3 /private/tmp/test_surface_hatch_fragment.py`

Expected: FAIL with `FileNotFoundError`.

- [ ] **Step 3: Implement the Three.js preview and controls**

Embed the compact payload in the visualization root. Load pinned Three.js from `esm.sh`. Decode mask and triangle-map PNGs into offscreen canvases, create the solid `MeshStandardMaterial`, and port the verified scanline, triangle-boundary, barycentric, and normal-offset logic from Task 2 to JavaScript. Render hatches as `LineSegments`, replace its position attribute after coalesced angle or spacing changes, and use independent visibility/opacity state for the solid texture, hatch layer, and triangle grid.

Use native controls for: `surfaceMode` with `solid` and `hatch`; `hatchAngle` default 45; `hatchSpacing` with a safe minimum; `hatchStrength`; `meshGrid`; `meshStrength`; bump strength; and reset. Show the current segment count in one concise status line and show an explicit empty-state message if regeneration yields zero segments.

- [ ] **Step 4: Run the fragment contract test**

Run: `python3 /private/tmp/test_surface_hatch_fragment.py`

Expected: PASS with no output.

### Task 4: Browser and final verification

**Files:**
- Verify: `/Users/ccc/.codex/visualizations/2026/08/26/01a03cc9-46bf-74f2-9b5a-5f66e22df1f5/surface-hatch-preview.html`
- Create: `/private/tmp/surface-hatch-preview-standalone.html`

**Interfaces:**
- Consumes: final fragment from Task 3.
- Produces: runtime evidence for rendering, interactions, continuity, and responsive layout.

- [ ] **Step 1: Wrap the fragment with the bundled renderer**

Run: `python3 /Users/ccc/.codex/plugins/cache/openai-bundled/visualize/1.0.22/skills/visualize/scripts/render.py <fragment> /private/tmp/surface-hatch-preview-standalone.html`.

Expected: standalone HTML is created without validation errors.

- [ ] **Step 2: Verify the 736 px default view**

Open the local wrapper, wait until the status reports a nonzero hatch segment count, and verify that 45-degree lines appear only inside the former black hexagons. Toggle to solid mode and back; enable the triangle grid and confirm hatch lines remain visually continuous while changing direction at the curved surface.

- [ ] **Step 3: Exercise regeneration and camera interactions**

Change angle to 0 and 90 degrees, change spacing to its minimum and maximum, and verify each valid change updates the segment count without console errors. Drag to rotate, use wheel zoom, click reset, and confirm the fitted view returns.

- [ ] **Step 4: Verify 360 px responsive layout**

Apply a 360 × 800 viewport and confirm controls stack without overlap, the stage remains visible, and no horizontal clipping occurs. Restore the browser viewport afterward.

- [ ] **Step 5: Run fresh final checks**

Run all three `/private/tmp/test_surface_hatch_*.py` files, the repository Python suite, `wc -c` on the fragment, and a forbidden-pattern scan. Expected: all tests pass, the fragment is below 1,000,000 bytes, and the forbidden-pattern scan has no matches.

### Task 5: Repository documentation checkpoint

**Files:**
- Create: `docs/superpowers/plans/2026-08-26-surface-hatch-preview.md`

**Interfaces:**
- Consumes: approved design at `docs/superpowers/specs/2026-08-26-surface-hatch-preview-design.md`.
- Produces: this reproducible plan; generated preview and temporary test files remain outside the business source tree.

- [ ] **Step 1: Check plan coverage and type consistency**

Confirm Tasks 1–4 cover the approved input files, black threshold, seamless triangle breaks, controls, error states, sub-1 MB requirement, and 736/360 px validation. Confirm `build_payload`, `generate_hatch_segments`, and `build_fragment` names match across tasks.

- [ ] **Step 2: Scan for placeholder language**

Run: `rg -n 'T[B]D|T[O]DO|implement[ ]later|fill[ ]in[ ]details|Similar[ ]to[ ]Task' docs/superpowers/plans/2026-08-26-surface-hatch-preview.md`.

Expected: no matches.

- [ ] **Step 3: Commit only the plan**

```bash
git add docs/superpowers/plans/2026-08-26-surface-hatch-preview.md
git commit -m "docs: plan surface hatch preview"
```
