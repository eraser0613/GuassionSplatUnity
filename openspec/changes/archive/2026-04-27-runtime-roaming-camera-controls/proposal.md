## Why

The runtime viewer currently relies on fixed front/back/left/right/top/bottom camera preset buttons, which is limiting for exploring a reconstructed 3DGS environment. A first-person style roaming control scheme lets users move through and inspect the scene naturally while preserving the existing measurement workflow.

## What Changes

- Remove the runtime preset view buttons for front, back, left, right, top, and bottom views.
- Add roaming camera controls:
  - `W` / `S` move forward and backward relative to the current camera orientation.
  - `A` / `D` move left and right relative to the current camera orientation.
  - `Q` / `E` move down and up in world space.
  - `Left Shift` temporarily increases movement speed.
  - Holding the right mouse button and dragging rotates the camera view.
- Preserve the existing reset view button so users can return to the initial camera transform.
- Preserve left-click point picking and the existing accurate 3DGS picker / measurement flow.
- Prevent right-mouse look from triggering while the cursor is over the IMGUI control or information windows.
- Update the on-screen operation instructions to describe the roaming controls.

## Capabilities

### New Capabilities
- `runtime-roaming-camera-controls`: Runtime camera movement and mouse-look controls for roaming a 3DGS scene while preserving measurement interactions.

### Modified Capabilities

## Impact

- `Assets/GSViewerUI.cs`
- Runtime IMGUI camera control panel and operation instructions.
- Existing camera reset, point picking, pick markers, and `GsPixelPicker` integration should remain compatible.
- No intended changes to Gaussian splat rendering, training, import pipeline, or the accurate picking compute implementation.
