# HTML 3D Texture Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an in-conversation, interactive Three.js preview of the supplied OBJ with switchable grayscale texture and triangular wireframe rendering.

**Architecture:** A one-off Python generator parses the OBJ, deduplicates face-corner attributes, downsizes the TIFF to an embedded JPEG data URI, and writes one self-contained HTML fragment to the thread visualization directory. The fragment loads only a pinned Three.js module from an approved CDN and contains all geometry and texture data inline, with local UI state controlling materials, wireframe visibility, orbit interaction, and reset.

**Tech Stack:** Python 3, Pillow, Three.js 0.180.0 ES module, HTML/CSS/JavaScript, Playwright-based visualization renderer.

## Global Constraints

- Do not modify or overwrite `/Users/ccc/Downloads/data/surf.exported.obj`, `/Users/ccc/Downloads/data/surf._U1_V1_e.tif`, or `/Users/ccc/Downloads/surf._U1_V1.tiff`.
- Write the visualization fragment to `/Users/ccc/.codex/visualizations/2026/08/26/01a03cc9-46bf-74f2-9b5a-5f66e22df1f5/surface-texture-preview.html`.
- Keep the HTML fragment below 1 MB.
- Do not use `fetch`, XHR, WebSocket, or local absolute resource references in the fragment.
- Support “仅纹理”, “纹理＋网格线”, and “仅网格线” modes.
- Support drag rotation, wheel zoom, texture contrast, bump strength, wireframe strength, and view reset.
- Keep controls usable at 736 px and 360 px widths.

---

### Task 1: Generate compact geometry and texture payload

**Files:**
- Create: `/private/tmp/generate_surface_texture_preview.py`
- Create: `/private/tmp/test_surface_texture_payload.py`
- Create: `/private/tmp/surface-texture-preview-data.json`

**Interfaces:**
- Consumes: OBJ path and grayscale TIFF path from Global Constraints.
- Produces: `build_payload(obj_path: Path, texture_path: Path) -> dict` with `positions`, `normals`, `uvs`, `indices`, `texture`, and `stats` keys.

- [ ] **Step 1: Write the failing payload test**

```python
from pathlib import Path
from generate_surface_texture_preview import build_payload

payload = build_payload(
    Path('/Users/ccc/Downloads/data/surf.exported.obj'),
    Path('/Users/ccc/Downloads/data/surf._U1_V1_e.tif'),
)
assert len(payload['positions']) % 3 == 0
assert len(payload['normals']) == len(payload['positions'])
assert len(payload['uvs']) == len(payload['positions']) // 3 * 2
assert len(payload['indices']) == 5350 * 3
assert payload['texture'].startswith('data:image/jpeg;base64,')
assert payload['stats']['source_faces'] == 5350
```

- [ ] **Step 2: Run the test and confirm the missing-module failure**

Run: `PYTHONPATH=/private/tmp python3 /private/tmp/test_surface_texture_payload.py`

Expected: FAIL with `ModuleNotFoundError: No module named 'generate_surface_texture_preview'`.

- [ ] **Step 3: Implement the minimal parser and encoder**

Implement `build_payload` to parse `v`, `vt`, `vn`, and triangular `f` records; deduplicate `(vertex_index, uv_index, normal_index)` tuples; round geometry values to five decimals; resize the grayscale texture to at most 768 × 768 with Lanczos; encode JPEG quality 82; and return the exact keys asserted above.

- [ ] **Step 4: Run the payload test**

Run: `PYTHONPATH=/private/tmp python3 /private/tmp/test_surface_texture_payload.py`

Expected: PASS with no output.

- [ ] **Step 5: Serialize and size-check the payload**

Run the generator to write `/private/tmp/surface-texture-preview-data.json`, then verify its byte size is below 850,000 so the final HTML has room for UI and runtime code.

### Task 2: Build the interactive HTML fragment

**Files:**
- Modify: `/private/tmp/generate_surface_texture_preview.py`
- Create: `/Users/ccc/.codex/visualizations/2026/08/26/01a03cc9-46bf-74f2-9b5a-5f66e22df1f5/surface-texture-preview.html`
- Create: `/private/tmp/test_surface_texture_fragment.py`

**Interfaces:**
- Consumes: `build_payload(...) -> dict` from Task 1.
- Produces: `build_fragment(payload: dict) -> str` and the final HTML fragment.

- [ ] **Step 1: Write the failing fragment contract test**

```python
from pathlib import Path

html = Path('/Users/ccc/.codex/visualizations/2026/08/26/01a03cc9-46bf-74f2-9b5a-5f66e22df1f5/surface-texture-preview.html').read_text()
assert not html.lstrip().lower().startswith('<!doctype')
assert '<html' not in html.lower()
assert '<body' not in html.lower()
assert len(html.encode()) < 1_000_000
assert 'fetch(' not in html
assert 'XMLHttpRequest' not in html
for label in ('仅纹理', '纹理＋网格线', '仅网格线', '复位视角'):
    assert label in html
for control in ('textureContrast', 'bumpStrength', 'wireStrength'):
    assert f'id="{control}"' in html
```

- [ ] **Step 2: Run the test and confirm the missing-file failure**

Run: `python3 /private/tmp/test_surface_texture_fragment.py`

Expected: FAIL with `FileNotFoundError`.

- [ ] **Step 3: Implement the fragment**

Create one root element with a unique ID, compact native controls, a responsive canvas area, an ARIA live status, and a module script importing `three@0.180.0` plus `OrbitControls`. Build indexed geometry from the inline payload, assign the UV texture to a `MeshStandardMaterial`, create an `EdgesGeometry` line layer, implement the three display modes, map contrast and bump sliders to material parameters, map wire strength to line opacity, fit the camera to the bounding sphere, and make reset restore the initial camera and target.

- [ ] **Step 4: Run the fragment contract test**

Run: `python3 /private/tmp/test_surface_texture_fragment.py`

Expected: PASS with no output.

### Task 3: Render and interaction verification

**Files:**
- Verify: `/Users/ccc/.codex/visualizations/2026/08/26/01a03cc9-46bf-74f2-9b5a-5f66e22df1f5/surface-texture-preview.html`
- Create: `/private/tmp/surface-texture-preview-standalone.html`
- Create: `/private/tmp/surface-texture-preview-wide.png`
- Create: `/private/tmp/surface-texture-preview-narrow.png`

**Interfaces:**
- Consumes: final fragment from Task 2.
- Produces: runtime evidence that the scene renders and controls update visible state.

- [ ] **Step 1: Wrap the fragment with the bundled renderer**

Run: `python3 /Users/ccc/.codex/plugins/cache/openai-bundled/visualize/1.0.22/skills/visualize/scripts/render.py <fragment> /private/tmp/surface-texture-preview-standalone.html`.

Expected: standalone HTML is created without validation errors.

- [ ] **Step 2: Open at 736 px and verify the default scene**

Use a browser screenshot to confirm the model is visible, the texture follows the model UV, the triangular grid is visible, and no error status appears.

- [ ] **Step 3: Exercise interactions**

Switch through all three modes; adjust each slider; drag the model; zoom; click reset. Confirm the canvas changes after each relevant action and reset restores the fitted view.

- [ ] **Step 4: Verify the 360 px layout**

Capture a narrow screenshot and confirm controls wrap without overlap, canvas remains visible, and no horizontal clipping occurs.

- [ ] **Step 5: Run final static checks**

Run both Python contract tests again, `wc -c` on the fragment, and search for forbidden network/local-loading APIs. Expected: both tests pass, size is below 1,000,000 bytes, and the forbidden-pattern search returns no matches.

### Task 4: Repository documentation checkpoint

**Files:**
- Create: `docs/superpowers/plans/2026-08-26-html-3d-texture-preview.md`

**Interfaces:**
- Consumes: approved design at `docs/superpowers/specs/2026-08-26-html-3d-texture-preview-design.md`.
- Produces: this reproducible implementation plan; no preview runtime files are committed to the business repository.

- [ ] **Step 1: Check the plan against the approved design**

Confirm every display mode, control, data constraint, failure state, and responsive verification item from the design appears in Tasks 1–3.

- [ ] **Step 2: Scan for placeholders and inconsistent names**

Run: `rg -n 'T[B]D|T[O]DO|implement[ ]later|fill[ ]in[ ]details' docs/superpowers/plans/2026-08-26-html-3d-texture-preview.md`.

Expected: no matches.

- [ ] **Step 3: Commit only the plan**

```bash
git add docs/superpowers/plans/2026-08-26-html-3d-texture-preview.md
git commit -m "docs: plan interactive 3d texture preview"
```
