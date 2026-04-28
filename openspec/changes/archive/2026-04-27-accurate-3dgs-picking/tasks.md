## 1. Rendering and Picking Investigation

- [x] 1.1 Document the current `GsPixelPicker` call chain from `GSViewerUI` and `ApproxGsMeasureTool` through `GsPixelPicker.cs` and `GsPixelPicker.compute`.
- [x] 1.2 Document the Gaussian asset build path from `GaussianSplatAssetCreator` through chunk, position, other, color, and SH byte assets.
- [x] 1.3 Document the runtime renderer path from `GaussianSplatRenderer` GPU buffer creation through `SortPoints`, `CalcViewData`, `RenderGaussianSplats.shader`, and composite.
- [x] 1.4 Identify all coordinate spaces used by the current picker and renderer, including Unity mouse coordinates, camera pixel rect, screen pixels, NDC, clip depth, and backbuffer/projection flip behavior.

## 2. Pick Result and Semantics

- [x] 2.1 Define a structured pick result type with hit state, world position, splat index, splat center, depth, alpha, contribution, accumulated alpha, confidence, and diagnostic text.
- [x] 2.2 Decide and document the primary hit semantic as max visible contribution at the clicked pixel, with any fallback or weighted-centroid behavior explicitly named.
- [x] 2.3 Keep compatibility for existing callers by preserving a simple `TryPick(Vector2, out Vector3)` path that delegates to the structured result.

## 3. Render-Consistent Picker Implementation

- [x] 3.1 Update the picker compute shader to evaluate splat coverage and contribution at the clicked pixel using renderer view data, sort order, opacity, and alpha cutoff consistent with visible rendering.
- [x] 3.2 Update `GsPixelPicker.cs` to prepare current camera-dependent renderer data before picking and bind all compute inputs required by the new shader.
- [x] 3.3 Implement explicit mouse-to-pick-pixel conversion that handles camera pixel rect, screen origin, render size, and projection/backbuffer orientation.
- [x] 3.4 Return the selected splat index and compute a measurement-ready world position using the chosen hit semantic.
- [x] 3.5 Ensure miss behavior does not silently return whole-asset or chunk-bound positions as primary accurate picks.

## 4. Viewer and Measurement Integration

- [x] 4.1 Update `GSViewerUI` to display the structured pick result, including debug fields and visible screen/world pick markers when available.
- [x] 4.2 Update `ApproxGsMeasureTool` to consume the accurate picker result for measurement points while keeping reset and marker behavior unchanged.
- [x] 4.3 Preserve existing point-picking workflow and references so the scene can still use `GSViewerUI`, `GsPixelPicker`, and `GaussianSplatRenderer` without new setup.

## 5. Validation

- [x] 5.1 Validate picking on isolated visible splats and object silhouettes. User validation: the surface-alpha weighted version aligns with clicked screen locations and is comparatively accurate.
- [x] 5.2 Validate picking in dense overlapping regions and inspect contribution/confidence output. User validation: the surface-alpha weighted visible-depth version is more accurate than the previous center-depth/max-contribution version.
- [x] 5.3 Validate picking immediately after camera movement to confirm current sort/view data are used. Static validation: picker uses renderer-produced view/sort data by default; manual view refresh is disabled because refreshing outside the render pass can produce offscreen picks.
- [x] 5.4 Validate miss behavior outside visible 3DGS content. Static validation: primary accurate picker returns miss diagnostics and does not produce bounds fallback points.
- [x] 5.5 Compare old and new behavior using debug logs or screenshots, and record any known ambiguity that remains for dense translucent regions. Recorded: center-depth/max-contribution could place depth on the wrong Gaussian center; surface-alpha weighted depth is the accepted current approach, with `surfaceAlpha` left configurable for ambiguous translucent regions.
