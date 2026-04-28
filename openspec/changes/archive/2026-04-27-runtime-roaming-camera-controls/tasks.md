## 1. Camera Control Implementation

- [x] 1.1 Remove the fixed view preset enum and `SetCameraView` path from `GSViewerUI`.
- [x] 1.2 Add roaming camera state for yaw, pitch, mouse-look tracking, and speed boost configuration.
- [x] 1.3 Initialize yaw and pitch from the recorded initial camera rotation so reset and first mouse-look start are stable.
- [x] 1.4 Implement `W/A/S/D` camera-relative movement, `Q/E` world-space vertical movement, and `Left Shift` speed boost using `Time.deltaTime`.
- [x] 1.5 Implement right-mouse drag yaw/pitch rotation with pitch clamping and no roll.

## 2. UI and Input Integration

- [x] 2.1 Remove the front/back/left/right/top/bottom preset view buttons from the IMGUI control panel while keeping the reset view button.
- [x] 2.2 Add or reuse a helper that detects whether the current mouse position is over the control or information window.
- [x] 2.3 Prevent right-mouse camera rotation when the cursor is over the IMGUI control or information window.
- [x] 2.4 Preserve existing left-click point-picking behavior and UI-window exclusion for pick attempts.
- [x] 2.5 Update the information panel operation instructions to describe right-drag look, `W/A/S/D`, `Q/E`, `Left Shift`, reset, and left-click picking.

## 3. Validation

- [x] 3.1 Verify `W/A/S/D` movement follows the current camera orientation and `Q/E` moves vertically.
- [x] 3.2 Verify holding `Left Shift` increases movement speed.
- [x] 3.3 Verify right-mouse drag rotates yaw/pitch without rolling or flipping the camera.
- [x] 3.4 Verify right-mouse drag over the IMGUI windows does not rotate the camera.
- [x] 3.5 Verify reset restores the initially recorded camera position and rotation after roaming.
- [x] 3.6 Verify point-picking mode still uses the accurate picker on left-click and is not triggered by right-drag rotation.
