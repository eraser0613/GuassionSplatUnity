## Why

The current 3DGS picking path often returns positions that do not match the visible point under the cursor. Accurate picking is needed for measurement, annotation, and future interactive workflows, but the existing picker approximates the render pipeline separately and reconstructs a world point from clicked pixel plus a splat depth.

## What Changes

- Analyze and document the relevant 3DGS data flow from imported PLY/SPZ data through `GaussianSplatAsset` byte assets, renderer GPU buffers, sorting, view-data calculation, transparent splat rendering, and composition.
- Replace the current approximate `GsPixelPicker` behavior with a render-consistent picking approach that uses the same camera, renderer transform, splat scale, opacity scale, sorted order, Gaussian footprint, and alpha behavior as visible rendering.
- Correctly handle Unity mouse coordinates, camera pixel dimensions, projection/backbuffer orientation, and NDC/depth conversion.
- Return a richer pick result suitable for measurement and debugging, including hit state, world position, splat index, alpha/contribution, depth, and confidence.
- Integrate the new result with the existing viewer UI and measurement tool without redesigning the UI.
- Add validation steps and debug output so inaccurate picks can be diagnosed in dense regions, silhouettes, camera movement, and miss cases.

## Capabilities

### New Capabilities
- `accurate-3dgs-picking`: Render-consistent picking for visible 3D Gaussian Splatting content, returning measurement-ready world positions and debug metadata.

### Modified Capabilities

## Impact

- `Assets/GsPixelPicker.cs`
- `Assets/GsPixelPicker.compute`
- `Assets/GSViewerUI.cs`
- `Assets/ApproxGsMeasureTool.cs`
- Local Gaussian Splatting package analysis and possible minimal API exposure if renderer internals are required.
- No intended changes to the Python training protocol, runtime PLY loading, or broader UI layout.
