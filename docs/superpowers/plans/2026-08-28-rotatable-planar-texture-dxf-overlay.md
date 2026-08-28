# Rotatable Planar Texture/DXF Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the registered planar texture visible and exactly aligned with its DXF in top, isometric, and freely rotated views.

**Architecture:** Treat the texture as a `Z = 0` raster rectangle and derive its screen affine transform from the same `ToScreen` projection used by DXF endpoints. A focused internal helper maps the projected raster corners to an Avalonia `Matrix`; the preview clips the transformed image with the projected four-corner frame polygon. Generation, registration metadata, and the future OBJ workflow remain untouched.

**Tech Stack:** C# 14 / .NET 10, Avalonia 11.3.18, MSTest 4.3.3

## Global Constraints

- Texture and generated DXF remain planar at `Z = 0`.
- Do not add OBJ, STL, height-map, mesh, UV, or WebGL dependencies.
- Do not change generation scripts, machine output, or `DxfTextureRegistration`.
- Preserve opacity/toggle controls, top/isometric buttons, orbit, zoom, pan, and fit-to-view.
- Near-edge-on views may naturally collapse to a thin line; do not invent thickness or clamp tilt.
- Follow red-green-refactor: observe every new test fail for the intended reason before production edits.

---

## File Structure

- Create `GrayscaleLayersMac/PlanarTextureProjection.cs`: projected-quad value and pure raster-to-screen matrix builder.
- Create `GrayscaleLayersMac/Properties/AssemblyInfo.cs`: test-only access to internal projection code.
- Create `GrayscaleLayersMac.Tests/PlanarTextureProjectionTests.cs`: literal corner and transform tests.
- Modify `GrayscaleLayersMac/DxfOverlayState.cs`: remove the view-angle visibility restriction.
- Modify `GrayscaleLayersMac.Tests/DxfOverlayStateTests.cs`: protect view-independent visibility.
- Modify `GrayscaleLayersMac/DxfPreviewControl.cs`: status copy, affine drawing, and quadrilateral clipping.

### Task 1: Make Texture Visibility Independent of View Angle

**Files:**
- Modify: `GrayscaleLayersMac.Tests/DxfOverlayStateTests.cs`
- Modify: `GrayscaleLayersMac/DxfOverlayState.cs`
- Modify: `GrayscaleLayersMac/DxfPreviewControl.cs:65-69`

**Interfaces:**
- Consumes: `DxfOverlayState.TextureAvailable`, `ShowTexture`, and `IsTopView`.
- Produces: `ShouldDrawTexture == TextureAvailable && ShowTexture`; view-independent `TextureStatus`.

- [ ] **Step 1: Replace the old suppression test with the failing new contract**

Replace `IsometricSuppressesTextureWithoutLosingSelection` with:

```csharp
[TestMethod]
public void AvailableSelectedTextureRemainsVisibleOutsideTopView()
{
    var state = new DxfOverlayState();
    state.SetTextureAvailable(true);
    state.IsTopView = false;

    Assert.IsTrue(state.ShouldDrawTexture);

    state.IsTopView = true;
    Assert.IsTrue(state.ShouldDrawTexture);
}
```

This fails if `&& IsTopView` remains or is later reintroduced.

- [ ] **Step 2: Run the focused test and observe RED**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~AvailableSelectedTextureRemainsVisibleOutsideTopView
```

Expected: FAIL on the first `Assert.IsTrue` because the current state suppresses non-top views.

- [ ] **Step 3: Implement the minimal state rule**

Change only:

```csharp
public bool ShouldDrawTexture => TextureAvailable && ShowTexture;
```

Keep `IsTopView`; existing view initialization still consumes it and removal is outside scope.

- [ ] **Step 4: Run all overlay-state tests and observe GREEN**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~DxfOverlayStateTests
```

Expected: all overlay-state tests PASS, including disabled and unavailable texture cases.

- [ ] **Step 5: Remove obsolete top-view-only status copy**

Change `DxfPreviewControl.TextureStatus` to:

```csharp
public string TextureStatus => !HasTexture
    ? "此 DXF 没有配对纹理"
    : "已加载配准纹理";
```

Do not change `MainWindow`; its control availability already depends on texture/line state rather than view angle.

- [ ] **Step 6: Build and commit**

Run:

```bash
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release
git add GrayscaleLayersMac/DxfOverlayState.cs GrayscaleLayersMac/DxfPreviewControl.cs GrayscaleLayersMac.Tests/DxfOverlayStateTests.cs
git commit -m "fix: keep registered texture visible while orbiting"
```

Expected: Release build succeeds without warnings or errors; commit contains only these three files.

### Task 2: Build and Test the Raster-to-Screen Affine Transform

**Files:**
- Create: `GrayscaleLayersMac/PlanarTextureProjection.cs`
- Create: `GrayscaleLayersMac/Properties/AssemblyInfo.cs`
- Create: `GrayscaleLayersMac.Tests/PlanarTextureProjectionTests.cs`

**Interfaces:**
- Consumes: raster pixel `Size` and four screen points produced by `DxfPreviewControl.ToScreen`.
- Produces: `ProjectedTextureQuad` and `CreateImageToScreenTransform(Size)`.

- [ ] **Step 1: Expose internals to tests and write the failing transform tests**

Create `GrayscaleLayersMac/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GrayscaleLayersMac.Tests")]
```

Create `GrayscaleLayersMac.Tests/PlanarTextureProjectionTests.cs`:

```csharp
using Avalonia;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class PlanarTextureProjectionTests
{
    [TestMethod]
    public void ImageTransformMapsAllRasterCornersToProjectedQuad()
    {
        var quad = new ProjectedTextureQuad(
            new Point(20, 30), new Point(220, 70),
            new Point(160, 170), new Point(-40, 130));

        var transform = quad.CreateImageToScreenTransform(new Size(100, 50));

        AssertPoint(new Point(20, 30), transform.Transform(new Point(0, 0)));
        AssertPoint(new Point(220, 70), transform.Transform(new Point(100, 0)));
        AssertPoint(new Point(160, 170), transform.Transform(new Point(100, 50)));
        AssertPoint(new Point(-40, 130), transform.Transform(new Point(0, 50)));
    }

    [TestMethod]
    public void ImageTransformRejectsNonPositivePixelDimensions()
    {
        var quad = new ProjectedTextureQuad(
            new Point(0, 0), new Point(1, 0),
            new Point(1, 1), new Point(0, 1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            quad.CreateImageToScreenTransform(new Size(0, 10)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            quad.CreateImageToScreenTransform(new Size(10, 0)));
    }

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-9);
        Assert.AreEqual(expected.Y, actual.Y, 1e-9);
    }
}
```

The literal skewed quad catches swapped Y corners, lost shear, and axis-aligned rendering.

- [ ] **Step 2: Run the projection tests and observe RED**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~PlanarTextureProjectionTests
```

Expected: compilation FAIL because `ProjectedTextureQuad` is missing.

- [ ] **Step 3: Add the minimal affine helper**

Create `GrayscaleLayersMac/PlanarTextureProjection.cs`:

```csharp
using Avalonia;

namespace GrayscaleLayersMac;

internal readonly record struct ProjectedTextureQuad(
    Point RasterTopLeft,
    Point RasterTopRight,
    Point RasterBottomRight,
    Point RasterBottomLeft)
{
    public Matrix CreateImageToScreenTransform(Size pixelSize)
    {
        if (!double.IsFinite(pixelSize.Width) || pixelSize.Width <= 0 ||
            !double.IsFinite(pixelSize.Height) || pixelSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));

        var across = (RasterTopRight - RasterTopLeft) / pixelSize.Width;
        var down = (RasterBottomLeft - RasterTopLeft) / pixelSize.Height;
        return new Matrix(
            across.X, across.Y, down.X, down.Y,
            RasterTopLeft.X, RasterTopLeft.Y);
    }
}
```

- [ ] **Step 4: Run the projection tests and observe GREEN**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~PlanarTextureProjectionTests
```

Expected: both tests PASS.

- [ ] **Step 5: Add a failing non-affine-quad test**

Append:

```csharp
[TestMethod]
public void ImageTransformRejectsQuadWhoseFourthCornerBreaksAffinePlane()
{
    var quad = new ProjectedTextureQuad(
        new Point(20, 30), new Point(220, 70),
        new Point(165, 170), new Point(-40, 130));

    Assert.ThrowsExactly<InvalidOperationException>(() =>
        quad.CreateImageToScreenTransform(new Size(100, 50)));
}
```

Also append the invalid-coordinate behavior test:

```csharp
[TestMethod]
public void ImageTransformRejectsNonFiniteProjectedPoint()
{
    var quad = new ProjectedTextureQuad(
        new Point(double.NaN, 0), new Point(1, 0),
        new Point(1, 1), new Point(0, 1));

    Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        quad.CreateImageToScreenTransform(new Size(10, 10)));
}
```

- [ ] **Step 6: Run the new test and observe RED**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~ImageTransformRejectsQuadWhoseFourthCornerBreaksAffinePlane
```

Expected: FAIL because the helper does not validate `RasterBottomRight`.

- [ ] **Step 7: Add finite-point and parallelogram validation**

Add this property and guard to `ProjectedTextureQuad`:

```csharp
public bool IsFinite =>
    double.IsFinite(RasterTopLeft.X) && double.IsFinite(RasterTopLeft.Y) &&
    double.IsFinite(RasterTopRight.X) && double.IsFinite(RasterTopRight.Y) &&
    double.IsFinite(RasterBottomRight.X) && double.IsFinite(RasterBottomRight.Y) &&
    double.IsFinite(RasterBottomLeft.X) && double.IsFinite(RasterBottomLeft.Y);
```

At the start of `CreateImageToScreenTransform`, after validating `pixelSize`, add:

```csharp
if (!IsFinite)
    throw new ArgumentOutOfRangeException(nameof(ProjectedTextureQuad));
```

Then add after calculating `across` and `down`:

```csharp
var expectedBottomRight = RasterTopLeft +
    across * pixelSize.Width + down * pixelSize.Height;
const double tolerance = 1e-7;
if (Math.Abs(expectedBottomRight.X - RasterBottomRight.X) > tolerance ||
    Math.Abs(expectedBottomRight.Y - RasterBottomRight.Y) > tolerance)
{
    throw new InvalidOperationException("平面纹理投影必须形成平行四边形。");
}
```

- [ ] **Step 8: Verify and commit the primitive**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~PlanarTextureProjectionTests
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release
git add GrayscaleLayersMac/PlanarTextureProjection.cs GrayscaleLayersMac/Properties/AssemblyInfo.cs GrayscaleLayersMac.Tests/PlanarTextureProjectionTests.cs
git commit -m "feat: add planar texture projection transform"
```

Expected: tests PASS, Release build succeeds, and only the three listed files are committed.

### Task 3: Render and Clip with the Shared DXF Projection

**Files:**
- Modify: `GrayscaleLayersMac/DxfPreviewControl.cs:245-297`
- Modify: `GrayscaleLayersMac/PlanarTextureProjection.cs`
- Modify: `GrayscaleLayersMac.Tests/PlanarTextureProjectionTests.cs`

**Interfaces:**
- Consumes: `ProjectedTextureQuad`, its affine transform, and existing `ToScreen(x, y, z, scale, center)`.
- Produces: ordered model corners plus an affine image draw clipped to the projected frame polygon.

- [ ] **Step 1: Write a failing test for PNG-Y/model-Y corner order**

Append:

```csharp
[TestMethod]
public void ModelCornersUseHigherModelYForRasterTop()
{
    var corners = ProjectedTextureQuad.ModelCorners(new Rect(-10, -20, 30, 50));

    Assert.AreEqual(new Point(-10, 30), corners.RasterTopLeft);
    Assert.AreEqual(new Point(20, 30), corners.RasterTopRight);
    Assert.AreEqual(new Point(20, -20), corners.RasterBottomRight);
    Assert.AreEqual(new Point(-10, -20), corners.RasterBottomLeft);
}
```

- [ ] **Step 2: Run it and observe RED**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~ModelCornersUseHigherModelYForRasterTop
```

Expected: compilation FAIL because `ModelCorners` is missing.

- [ ] **Step 3: Add the corner factory and observe GREEN**

Add to `ProjectedTextureQuad`:

```csharp
public static ProjectedTextureQuad ModelCorners(Rect bounds) => new(
    new Point(bounds.Left, bounds.Bottom),
    new Point(bounds.Right, bounds.Bottom),
    new Point(bounds.Right, bounds.Top),
    new Point(bounds.Left, bounds.Top));
```

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj --filter FullyQualifiedName~PlanarTextureProjectionTests
```

Expected: all projection tests PASS. `Rect.Bottom` is the higher model Y because `_textureBounds.Y` stores `RasterBottomMm`.

- [ ] **Step 4: Add projected-bounds and clip-geometry helpers to the preview**

Add to `DxfPreviewControl`:

```csharp
private ProjectedTextureQuad ProjectTextureBounds(Rect bounds, double scale, Point center)
{
    var corners = ProjectedTextureQuad.ModelCorners(bounds);
    return new ProjectedTextureQuad(
        ToScreen(corners.RasterTopLeft.X, corners.RasterTopLeft.Y, 0, scale, center),
        ToScreen(corners.RasterTopRight.X, corners.RasterTopRight.Y, 0, scale, center),
        ToScreen(corners.RasterBottomRight.X, corners.RasterBottomRight.Y, 0, scale, center),
        ToScreen(corners.RasterBottomLeft.X, corners.RasterBottomLeft.Y, 0, scale, center));
}

private static StreamGeometry CreateClipGeometry(ProjectedTextureQuad quad)
{
    var geometry = new StreamGeometry();
    using var drawing = geometry.Open();
    drawing.BeginFigure(quad.RasterTopLeft, isFilled: true);
    drawing.LineTo(quad.RasterTopRight);
    drawing.LineTo(quad.RasterBottomRight);
    drawing.LineTo(quad.RasterBottomLeft);
    drawing.EndFigure(isClosed: true);
    return geometry;
}
```

- [ ] **Step 5: Replace the axis-aligned image draw**

After the bitmap null guard in `DrawTextureOverlay`, use:

```csharp
var textureQuad = ProjectTextureBounds(_textureBounds, scale, center);
var frameQuad = ProjectTextureBounds(_textureFrameBounds, scale, center);
if (!textureQuad.IsFinite || !frameQuad.IsFinite)
    return;
var imageTransform = textureQuad.CreateImageToScreenTransform(_textureBitmap.Size);
var clipGeometry = CreateClipGeometry(frameQuad);
using (context.PushGeometryClip(clipGeometry))
using (context.PushOpacity(_overlay.TextureOpacity))
using (context.PushTransform(imageTransform))
{
    context.DrawImage(
        _textureBitmap,
        new Rect(_textureBitmap.Size),
        new Rect(_textureBitmap.Size));
}
```

Push the screen-space geometry clip before the image transform. Never replace it with the quad's axis-aligned bounding rectangle.

- [ ] **Step 6: Build and make only syntax-level Avalonia API corrections**

Run:

```bash
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release
```

Expected: success without warnings/errors. If an Avalonia 11.3.18 signature differs, consult its installed reference XML and preserve the same matrix and four-point clip semantics.

- [ ] **Step 7: Run all C# tests**

Run:

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
```

Expected: all tests PASS without warnings/errors.

- [ ] **Step 8: Verify the actual rendered interaction**

Run:

```bash
dotnet run --project GrayscaleLayersMac/GrayscaleLayersMac.csproj
```

With one paired `.dxf` and `.preview.png`, check top view, isometric view, two arbitrary orbit angles, and a near-edge-on angle. At each usable angle, confirm the raster boundary/pattern stays on the Hatch geometry. Also check wheel zoom, middle-button pan, fit, all toggles, opacity, and the status “已加载配准纹理”.

- [ ] **Step 9: Commit renderer integration**

```bash
git add GrayscaleLayersMac/DxfPreviewControl.cs GrayscaleLayersMac/PlanarTextureProjection.cs GrayscaleLayersMac.Tests/PlanarTextureProjectionTests.cs
git commit -m "feat: align planar texture with rotated dxf view"
```

### Task 4: Final Regression Verification

**Files:** Verify only; no planned production edits.

**Interfaces:**
- Consumes: completed visibility, transform, and renderer tasks.
- Produces: final automated and visual evidence.

- [ ] **Step 1: Run the full verification set**

```bash
dotnet test GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj
dotnet build GrayscaleLayersMac/GrayscaleLayersMac.csproj -c Release
python3 -m unittest discover -s tests -p 'test_*.py'
```

Expected: all C# and Python tests PASS; Release build succeeds without warnings/errors.

- [ ] **Step 2: Check scope and whitespace**

```bash
git diff --check HEAD~3..HEAD
git status --short
```

Expected: no whitespace errors. Existing unrelated untracked paths (`.workbuddy/`, `1/`, `overlay_viewer/`, and the pasted PNG) remain unmodified and uncommitted.

- [ ] **Step 3: Report evidence**

Report exact test counts, Release build result, manually checked view angles, and commit hashes. If visual verification could not be observed, state that limitation and give the launch command instead of claiming it passed.
