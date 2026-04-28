## Context

The project currently contains a Unity 2022.3 3D Gaussian Splatting viewer with IMGUI controls, a measurement tool, and a custom pixel picker. The active viewer path is:

```text
GSViewerUI.TryPickPoint()
  -> GsPixelPicker.TryPick(Input.mousePosition)
  -> GsPixelPicker.compute scans splats and returns clicked pixel + NDC depth
  -> C# reconstructs a world position from camera inverse VP
```

The underlying renderer path is different:

```text
PLY/SPZ input
  -> GaussianSplatAssetCreator
     -> bounds calculation
     -> Morton reordering
     -> chunk/position/other/color/SH byte assets
  -> GaussianSplatAsset
  -> GaussianSplatRenderer GPU resources
     -> m_GpuPosData / m_GpuOtherData / m_GpuColorData / m_GpuSHData / m_GpuChunks
     -> m_GpuSortKeys / m_GpuView
  -> SortPoints(camera)
  -> CalcViewData(camera)
  -> RenderGaussianSplats.shader transparent splat rendering
  -> composite into camera target
```

The current picker approximates the render path in a separate compute shader. It loops over `_OrderBuffer`, projects the click into each splat ellipse using `SplatViewData.axis1/axis2`, accumulates alpha until a threshold, returns a depth, and reconstructs a point along the clicked pixel ray. This can diverge from the visual result when sorting is stale, coordinate origins differ, backbuffer/projection flips are involved, or when the chosen depth does not represent the visually dominant contribution.

The Gaussian Splatting package is referenced through a local path outside this Unity project repository. Implementation should prefer adding picker logic under `Assets/` and use existing exposed renderer buffers. If package changes become necessary, they should be minimal and documented.

## Goals / Non-Goals

**Goals:**

- Understand and document the renderer data flow enough to align picking with visible rendering.
- Replace the approximate point picker with a render-consistent picking path.
- Return a measurement-ready world position and structured debug metadata.
- Correct mouse-to-render-pixel coordinate conversion, including projection/backbuffer orientation.
- Keep existing viewer and measurement workflows intact while improving pick accuracy.
- Provide debug output that makes inaccurate hits diagnosable.

**Non-Goals:**

- Do not implement runtime PLY loading.
- Do not change the Python training protocol.
- Do not redesign the IMGUI UI layout.
- Do not implement general 3DGS editing, deletion, or annotation persistence.
- Do not require uploading or versioning large generated `.bytes` assets.

## Decisions

### Decision 1: Define picking as visible contribution, not geometric raycast

Use the visible rendered contribution at the clicked pixel as the picking semantic. The primary hit should be the splat or weighted splat contribution that best explains the visible pixel, rather than a ray/AABB intersection with asset bounds.

Rationale:
- 3DGS has no hard triangle surface to raycast.
- Bounds/chunk fallback can hit empty space or the front of a bounding box rather than the visible Gaussian density.
- Measurement should correspond to what the user clicked visually.

Alternative considered: CPU or GPU raycast against chunk AABBs. This is cheaper conceptually but is not visually accurate and should remain only as an optional fallback/debug path.

### Decision 2: Use a render-consistent GPU picker based on renderer buffers

The picker should use the same renderer-provided GPU data used for drawing: position data, chunk data, sort keys, and calculated view data. Before picking, it should ensure view data and sort keys are current for the active camera, then evaluate splat footprints for the clicked pixel with the same alpha cutoff and opacity scaling model used by `RenderGaussianSplats.shader`.

Rationale:
- The renderer already computes the projection-space center, axis vectors, color, and opacity into `SplatViewData`.
- Matching `RenderGaussianSplats.shader` minimizes drift between visible splats and picked splats.
- Returning the splat index and contribution data makes validation possible.

Alternative considered: Offscreen ID render pass. It can be highly accurate but transparent blending makes a single object ID ambiguous unless the pass reproduces contribution weighting; it also adds render target management complexity.

### Decision 3: Return structured pick metadata

`GsPixelPicker` should return a structured result instead of only `bool + Vector3`.

The result should include:
- `hit`
- `worldPosition`
- `splatIndex`
- `splatCenterWorld`
- `depth` or clip/NDC depth
- `alpha`
- `contribution`
- `accumulatedAlpha`
- `confidence`
- optional diagnostic text

Rationale:
- Existing UI and measurement tools can still consume `worldPosition`.
- Debug fields are necessary to compare old vs. new behavior and diagnose dense-region misses.

Alternative considered: Keep the old signature only. This is simpler but preserves the current black-box failure mode.

### Decision 4: Use surface-alpha weighted visible depth

The implementation uses a surface-alpha weighted visible-depth semantic. The picker walks splats at the clicked pixel in render order, computes each splat's visible contribution, accumulates front-to-back alpha until `surfaceAlpha`, and returns a ray-aligned world point at the weighted visible surface depth. It still reports the max-contribution splat index and center for debugging.

Rationale:
- Returning the max-contribution splat center kept the screen marker aligned only after projecting onto the click ray, but depth could still feel wrong because a single Gaussian center is not the perceived surface.
- Accumulating to a configurable `surfaceAlpha` better matches the user's perceived front surface and was validated by the user as more accurate than the previous max-contribution/center-depth version.
- The returned `worldPosition` remains aligned with the clicked screen pixel, while `splatCenterWorld` remains available for diagnosis.

Alternative considered: Alpha-threshold first-hit. This matches a compositing threshold but can select the wrong layer in semi-transparent overlap and is sensitive to the threshold value. Pure max-contribution is useful for debugging but was less suitable as the measurement depth.

### Decision 5: Make coordinate conversion explicit and testable

The picker should isolate conversion from `Input.mousePosition` into picker/render pixel coordinates. It should account for camera pixel rect, screen height origin differences, render target size, NDC conversion, and projection/backbuffer flip behavior.

Rationale:
- Several likely accuracy failures are vertical flip or pixel-origin mismatches.
- A small explicit conversion path can be logged and validated independently.

Alternative considered: Continue passing `Input.mousePosition` directly. This keeps the bug surface hidden.

## Risks / Trade-offs

- Render-consistent picking may still be ambiguous in dense translucent areas → expose contribution/confidence and support a neighborhood or weighted mode if needed.
- Forcing sort/view refresh on every click can add GPU work → only do it on click, and reuse renderer buffers where possible.
- Reading one result back from GPU can stall the CPU → keep the readback small; consider async readback later only if clicks become frequent.
- External package internals may not expose every buffer/API needed → prefer current public accessors first; if required, add minimal public API to the local package and document it.
- Backbuffer/projection flip behavior can differ between built-in render pipeline and SRP → validate in the current project pipeline first and keep debug output for pixel coordinate transformations.
- World position derived from splat center may not lie exactly on a perceived surface → choose a clear semantic and document it in the UI/debug output.
